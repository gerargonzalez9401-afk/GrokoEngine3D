using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using MVector3 = MiMotor.Mathematics.Vector3;
using MQuaternion = MiMotor.Mathematics.Quaternion;

namespace GrokoEngine
{
    // ============================================================
    //  Backend de física profesional basado en BepuPhysics v2 (C# puro, sin nativas).
    //
    //  Tu API pública NO cambia: los scripts siguen usando Rigidbody, Collider,
    //  BoxCollider, SphereCollider, CapsuleCollider, MeshCollider, TerrainCollider
    //  y Physics.Raycast. BEPU queda oculto como solver interno.
    //
    //  Incluye:
    //   - Rigidbody dinámicos con gravedad, drag, fuerzas, impulsos y velocidad inicial.
    //   - Box/Sphere/Capsule dinámicos o estáticos.
    //   - MeshCollider/TerrainCollider estáticos como malla de triángulos.
    //   - Jerarquía: pose de mundo -> write-back local si el objeto tiene padre.
    //   - Raycast preciso contra el mundo de BEPU.
    //   - CharacterController híbrido por sweeps, para no romper gameplay existente.
    //   - BEPU es la única implementación física real. PhysicsEngine queda solo como fachada/API.
    // ============================================================
    public readonly struct BepuContactEvent
    {
        public BepuContactEvent(GameObject a, GameObject b, MVector3 point, MVector3 normal, float depth)
        {
            A = a;
            B = b;
            Point = point;
            Normal = normal;
            Depth = depth;
        }

        public GameObject A { get; }
        public GameObject B { get; }
        public MVector3 Point { get; }
        // Normal orientada desde B hacia A. Para B se usa -Normal.
        public MVector3 Normal { get; }
        public float Depth { get; }
    }

    public static class BepuBackend
    {
        // BEPU queda activado por defecto, pero se puede desactivar para tests, debugging
        // o para mantener vivo el fallback legacy sin tener dos solvers corriendo a la vez.
        private static bool _enabled = true;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;

                _enabled = value;
                Reset();
            }
        }

        public static bool IsReady => _enabled && _built && _sim != null;

        private static BufferPool? _pool;
        private static Simulation? _sim;
        private static bool _built;
        private static bool _dirty;
        private static readonly List<(GameObject obj, BodyHandle handle, MVector3 localScale, NVector3 centerOffset)> _dynamics = new();
        // Cuerpos KINEMÁTICOS: los mueve el script vía transform; Bepu los trata como masa
        // infinita que empuja a los dinámicos. Su pose se actualiza cada frame desde el transform.
        private static readonly List<(GameObject obj, BodyHandle handle, MVector3 localScale, NVector3 centerOffset)> _kinematics = new();
        private static readonly Dictionary<Rigidbody, BodyHandle> _rigidbodyToBody = new();
        private static readonly Dictionary<Rigidbody, NVector3> _pendingVelocityChanges = new();
        // Mapas handle->GameObject para resolver lo que golpea un Raycast.
        private static readonly Dictionary<int, GameObject> _bodyToObj = new();
        private static readonly Dictionary<int, GameObject> _staticToObj = new();
        // Pose congelada (centro del cuerpo + orientación) de los cuerpos con algún Freeze*.
        private static readonly Dictionary<int, (NVector3 pos, NQuaternion orient)> _frozen = new();

        // Contactos REALES reportados por BEPU desde INarrowPhaseCallbacks.
        // PhysicsEngine los consume para OnCollisionEnter/Stay/Exit; los triggers siguen
        // en el puente legacy porque no se insertan como sólidos en BEPU.
        private static readonly Dictionary<BepuContactKey, BepuContactEvent> _contactMap = new();
        private static readonly List<BepuContactEvent> _contactSnapshot = new();
        private static bool _contactFrameReady;

        public static bool HasFreshContactFrame => _enabled && _built && _contactFrameReady;
        public static IReadOnlyList<BepuContactEvent> ContactEvents => _contactSnapshot;

        // Timestep físico fijo estilo engine pro. BEPU es mucho más estable con pasos pequeños
        // que con un delta grande de frame (por ejemplo un tirón de 0.16s).
        private const float FixedSubstepDelta = 1f / 90f;
        private const int MaxFixedSubsteps = 16;
        private const float MaxFrameDelta = FixedSubstepDelta * MaxFixedSubsteps;

        // Libera la simulación; la próxima llamada a Step reconstruye desde la escena.
        public static void Reset()
        {
            _dynamics.Clear();
            _kinematics.Clear();
            _bodyToObj.Clear();
            _staticToObj.Clear();
            _rigidbodyToBody.Clear();
            _pendingVelocityChanges.Clear();
            _frozen.Clear();
            _contactMap.Clear();
            _contactSnapshot.Clear();
            _contactFrameReady = false;
            _sim?.Dispose();
            _sim = null;
            _pool?.Clear();
            _pool = null;
            _built = false;
            _dirty = false;
        }

        // Cuando es true, MarkDirty se ignora. Lo usa SyncPhysicsComponents mientras
        // re-registra los MISMOS colliders cada frame: eso no debe reconstruir la simulación.
        public static bool SuppressDirty;

        // Marca el mundo BEPU como sucio. Úsalo cuando se añade/quita un collider,
        // cambia su tamaño, cambia IsTrigger, se mueve un static/mesh/terrain o se carga una escena.
        public static void MarkDirty()
        {
            if (!SuppressDirty)
                _dirty = true;
        }

        public static void Wake(Rigidbody rb)
        {
            if (_sim == null || !_rigidbodyToBody.TryGetValue(rb, out var handle))
                return;
            var body = _sim.Bodies[handle];
            body.Awake = true;
        }

        public static void AddVelocityChange(Rigidbody rb, MVector3 deltaVelocity)
        {
            var n = new NVector3(deltaVelocity.X, deltaVelocity.Y, deltaVelocity.Z);
            _pendingVelocityChanges.TryGetValue(rb, out var current);
            _pendingVelocityChanges[rb] = current + n;
            Wake(rb);
        }

        public static void Step(List<GameObject> objects, double deltaTime)
        {
            if (!_enabled)
                return;

            if (!_built || _dirty)
                Build(objects);

            float frameDt = Math.Clamp((float)deltaTime, 0f, MaxFrameDelta);
            if (frameDt <= 0f || _sim == null)
            {
                _contactFrameReady = false;
                return;
            }

            BeginContactFrame();

            // Kinematics y pushes se calculan una sola vez con el delta real del frame.
            // Si se recalcularan dentro de cada substep, una plataforma o personaje que ya
            // llegó a su transform final generaría velocidades artificialmente altas.
            UpdateKinematics(frameDt);
            ApplyCharacterPushes(objects, frameDt);

            float remaining = frameDt;
            int substep = 0;
            while (remaining > 1e-6f && substep < MaxFixedSubsteps)
            {
                float dt = MathF.Min(FixedSubstepDelta, remaining);
                ApplyRigidbodyDynamics(dt);      // gravedad + drag por cuerpo (UseGravity/Gravity/Drag/AngularDrag)
                _sim.Timestep(dt);
                ApplyFreezeConstraints();        // respeta FreezePosition*/FreezeRotation*
                remaining -= dt;
                substep++;
            }

            EndContactFrame();
            WriteBackDynamics();
        }

        private static void BeginContactFrame()
        {
            _contactMap.Clear();
            _contactSnapshot.Clear();
            _contactFrameReady = false;
        }

        private static void EndContactFrame()
        {
            _contactSnapshot.AddRange(_contactMap.Values);
            _contactFrameReady = true;
        }

        internal static void RecordContact(CollidablePair pair, int contactCount)
        {
            // Fallback compatible: solo se usa si un callback no puede entregar el manifold.
            RecordContactApproximate(pair, contactCount);
        }

        internal static void RecordContact<TManifold>(CollidablePair pair, ref TManifold manifold)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            if (!_enabled || _sim == null || manifold.Count <= 0)
                return;

            var a = ResolveGameObject(pair.A);
            var b = ResolveGameObject(pair.B);
            if (!CanRecordContact(a, b))
                return;

            ExtractManifoldContact(pair, ref manifold, a!, b!, out var point, out var normal, out float depth);
            RecordResolvedContact(a!, b!, point, normal, depth);
        }

        private static void RecordContactApproximate(CollidablePair pair, int contactCount)
        {
            if (!_enabled || _sim == null || contactCount <= 0)
                return;

            var a = ResolveGameObject(pair.A);
            var b = ResolveGameObject(pair.B);
            if (!CanRecordContact(a, b))
                return;

            ComputeFallbackContact(a!, b!, out var point, out var normal);
            RecordResolvedContact(a!, b!, point, normal, 0f);
        }

        private static bool CanRecordContact(GameObject? a, GameObject? b)
        {
            if (a == null || b == null || ReferenceEquals(a, b))
                return false;

            var ca = a.GetComponent<Collider>();
            var cb = b.GetComponent<Collider>();
            return ca != null && cb != null && !ca.IsTrigger && !cb.IsTrigger;
        }

        private static void RecordResolvedContact(GameObject a, GameObject b, MVector3 point, MVector3 normal, float depth)
        {
            var key = new BepuContactKey(a, b);

            if (_contactMap.TryGetValue(key, out var existing))
            {
                // Conserva el contacto más profundo del frame; si no hay profundidad real,
                // mantiene el último punto válido del par.
                if (depth <= existing.Depth)
                    return;
            }

            _contactMap[key] = new BepuContactEvent(a, b, point, normal, MathF.Max(0f, depth));
        }

        private static void ExtractManifoldContact<TManifold>(CollidablePair pair, ref TManifold manifold, GameObject a, GameObject b,
            out MVector3 point, out MVector3 normal, out float depth)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            ComputeFallbackContact(a, b, out point, out normal);
            depth = 0f;

            try
            {
                object boxed = manifold;
                Type manifoldType = boxed.GetType();

                var normalCandidate = ReadVector3Member(boxed, manifoldType, "Normal");
                if (normalCandidate.HasValue)
                    normal = ToMotorVector(OrientNormalFromBToA(normalCandidate.Value, a, b));

                var getContact = FindGetContactMethod(manifoldType);
                if (getContact == null)
                    return;

                if (!TryGetCollidablePosition(pair.A, out var poseA))
                {
                    var aw = a.WorldPosition;
                    poseA = new NVector3(aw.X, aw.Y, aw.Z);
                }

                NVector3 sumPoint = NVector3.Zero;
                NVector3 sumNormal = normalCandidate ?? NVector3.Zero;
                int pointCount = 0;
                int normalCount = normalCandidate.HasValue ? 1 : 0;
                float maxDepth = 0f;

                for (int i = 0; i < manifold.Count; i++)
                {
                    object?[] args = CreateGetContactArgs(getContact, i);
                    getContact.Invoke(boxed, args);

                    var offset = FirstOutVector(args, getContact, preferNormal: false);
                    if (offset.HasValue)
                    {
                        // BEPU reporta offsets de contacto en espacio de mundo relativo al centro de A.
                        // Para eventos del motor convertimos a punto de mundo.
                        sumPoint += poseA + offset.Value;
                        pointCount++;
                    }

                    var perContactNormal = FirstOutVector(args, getContact, preferNormal: true);
                    if (perContactNormal.HasValue)
                    {
                        sumNormal += OrientNormalFromBToA(perContactNormal.Value, a, b);
                        normalCount++;
                    }

                    var contactDepth = FirstOutFloat(args);
                    if (contactDepth.HasValue)
                        maxDepth = MathF.Max(maxDepth, contactDepth.Value);
                }

                if (pointCount > 0)
                    point = ToMotorVector(sumPoint / pointCount);

                if (normalCount > 0 && sumNormal.LengthSquared() > 1e-10f)
                    normal = ToMotorVector(NVector3.Normalize(sumNormal));

                depth = maxDepth;
            }
            catch
            {
                // Nunca permitas que un evento de contacto rompa el solver físico.
                // Si BEPU cambia internamente el shape del manifold, el motor mantiene
                // un evento de par real con punto/normal aproximados.
            }
        }

        private static System.Reflection.MethodInfo? FindGetContactMethod(Type manifoldType)
        {
            return manifoldType
                .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "GetContact" && m.GetParameters().Length >= 3);
        }

        private static object?[] CreateGetContactArgs(System.Reflection.MethodInfo method, int index)
        {
            var ps = method.GetParameters();
            var args = new object?[ps.Length];
            args[0] = index;
            for (int i = 1; i < ps.Length; i++)
            {
                var type = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType()! : ps[i].ParameterType;
                args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
            }
            return args;
        }

        private static NVector3? FirstOutVector(object?[] args, System.Reflection.MethodInfo method, bool preferNormal)
        {
            var ps = method.GetParameters();
            NVector3? firstVector = null;
            for (int i = 1; i < ps.Length && i < args.Length; i++)
            {
                var elementType = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType;
                if (elementType != typeof(NVector3) || args[i] is not NVector3 v)
                    continue;

                string name = ps[i].Name ?? string.Empty;
                bool looksLikeNormal = name.Contains("normal", StringComparison.OrdinalIgnoreCase);
                if (preferNormal && looksLikeNormal)
                    return v;
                if (!preferNormal && !looksLikeNormal)
                    return v;
                firstVector ??= v;
            }
            return preferNormal ? null : firstVector;
        }

        private static float? FirstOutFloat(object?[] args)
        {
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] is float f)
                    return f;
            }
            return null;
        }

        private static NVector3? ReadVector3Member(object target, Type type, string name)
        {
            var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(target) is NVector3 fv)
                return fv;

            var property = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (property?.GetValue(target) is NVector3 pv)
                return pv;

            return null;
        }

        private static void ComputeFallbackContact(GameObject a, GameObject b, out MVector3 point, out MVector3 normal)
        {
            var pa = a.WorldPosition;
            var pb = b.WorldPosition;
            var delta = new MVector3(pa.X - pb.X, pa.Y - pb.Y, pa.Z - pb.Z);
            float len = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
            normal = len > 0.000001f
                ? new MVector3(delta.X / len, delta.Y / len, delta.Z / len)
                : new MVector3(0f, 1f, 0f);
            point = new MVector3((pa.X + pb.X) * 0.5f, (pa.Y + pb.Y) * 0.5f, (pa.Z + pb.Z) * 0.5f);
        }

        private static NVector3 OrientNormalFromBToA(NVector3 candidate, GameObject a, GameObject b)
        {
            if (candidate.LengthSquared() < 1e-12f)
                return new NVector3(0f, 1f, 0f);

            var aw = a.WorldPosition;
            var bw = b.WorldPosition;
            var bToA = new NVector3(aw.X - bw.X, aw.Y - bw.Y, aw.Z - bw.Z);
            var n = NVector3.Normalize(candidate);
            if (bToA.LengthSquared() > 1e-10f && NVector3.Dot(n, bToA) < 0f)
                n = -n;
            return n;
        }

        private static MVector3 ToMotorVector(NVector3 v) => new(v.X, v.Y, v.Z);

        private static bool TryGetCollidablePosition(CollidableReference c, out NVector3 position)
        {
            position = NVector3.Zero;
            if (_sim == null)
                return false;

            if (c.Mobility == CollidableMobility.Static)
            {
                position = _sim.Statics[c.StaticHandle].Pose.Position;
                return true;
            }

            position = _sim.Bodies[c.BodyHandle].Pose.Position;
            return true;
        }

        private readonly struct BepuContactKey : IEquatable<BepuContactKey>
        {
            private readonly GameObject a;
            private readonly GameObject b;

            public BepuContactKey(GameObject a, GameObject b)
            {
                this.a = a;
                this.b = b;
            }

            public bool Equals(BepuContactKey other) =>
                (ReferenceEquals(a, other.a) && ReferenceEquals(b, other.b)) ||
                (ReferenceEquals(a, other.b) && ReferenceEquals(b, other.a));

            public override bool Equals(object? obj) => obj is BepuContactKey other && Equals(other);

            public override int GetHashCode() =>
                RuntimeHelpers.GetHashCode(a) ^ RuntimeHelpers.GetHashCode(b);
        }

        private static void WriteBackDynamics()
        {
            if (_sim == null)
                return;

            // Escribir la pose simulada (mundo) de vuelta a cada GameObject dinámico.
            foreach (var (obj, handle, localScale, centerOffset) in _dynamics)
            {
                var pose = _sim.Bodies[handle].Pose;
                // El cuerpo está en el centro del collider; el origen del objeto resta ese offset.
                var objPos = pose.Position - NVector3.Transform(centerOffset, pose.Orientation);
                WriteBackWorldPose(obj, objPos, pose.Orientation, localScale);

                var rb = obj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    var vel = _sim.Bodies[handle].Velocity.Linear;
                    rb.SyncVelocityFromBepu(new MVector3(vel.X, vel.Y, vel.Z));
                    rb.IsGrounded = false;
                    rb.LastCollisionNormal = MVector3.Zero;
                }
            }
        }

        private static void Build(List<GameObject> objects)
        {
            Reset();
            _pool = new BufferPool();
            _sim = Simulation.Create(
                _pool,
                new NarrowPhaseCallbacks(),
                new PoseIntegratorCallbacks(NVector3.Zero), // gravedad por cuerpo se aplica manualmente
                new SolveDescription(8, 1));

            // objects = roots de la escena; añadimos también hijos activos.
            // La pose siempre se calcula en mundo y el write-back convierte a local si tiene padre.
            foreach (var obj in EnumerateActive(objects))
                AddObject(obj);

            _built = true;
            _dirty = false;
        }

        private static IEnumerable<GameObject> EnumerateActive(IEnumerable<GameObject> roots)
        {
            foreach (var root in roots)
            {
                if (root == null || !root.IsActive)
                    continue;
                yield return root;
                foreach (var child in EnumerateActive(root.Children))
                    yield return child;
            }
        }

        private static void AddObject(GameObject obj)
        {
            // El CharacterController lo lleva tu controlador propio (modo híbrido), no Bepu.
            if (obj.GetComponent<CharacterController>() != null)
                return;

            var collider = obj.GetComponent<Collider>();

            // Los triggers son SENSORES: OnTrigger* lo detecta el motor legacy (DispatchPhysicsEvents).
            // No deben entrar en Bepu o se volverían un static sólido que bloquea el paso.
            if (collider != null && collider.IsTrigger)
                return;

            // Terrain / Mesh → malla de triángulos ESTÁTICA en Bepu (las cajas caen encima).
            if (collider is TerrainCollider) { AddTerrainStatic(obj); return; }
            if (collider is MeshCollider) { AddMeshStatic(obj); return; }

            if (collider is not BoxCollider && collider is not SphereCollider && collider is not CapsuleCollider)
                return;

            // Pose de MUNDO (correcta tenga o no padre). Escala local para el write-back.
            DecomposeWorld(obj, out var npos, out var orient, out var scale);
            var localScale = obj.transform.Scale;

            // Offset del centro del collider (Collider.Center * escala, sin rotar): el cuerpo
            // de Bepu se coloca en el centro del collider, no en el origen del objeto.
            var c = collider.Center;
            var centerOffset = new NVector3(c.X * scale.X, c.Y * scale.Y, c.Z * scale.Z);
            var bodyPos = npos + NVector3.Transform(centerOffset, orient);

            // Forma + inercia desde la misma instancia.
            TypedIndex shapeIndex;
            BodyInertia inertia;
            var rb = obj.GetComponent<Rigidbody>();
            float mass = MathF.Max(0.0001f, rb?.Mass ?? 1f);

            if (collider is BoxCollider box)
            {
                var shape = new Box(
                    MathF.Max(0.01f, MathF.Abs(box.Size.X * scale.X)),
                    MathF.Max(0.01f, MathF.Abs(box.Size.Y * scale.Y)),
                    MathF.Max(0.01f, MathF.Abs(box.Size.Z * scale.Z)));
                shapeIndex = _sim!.Shapes.Add(shape);
                inertia = shape.ComputeInertia(mass);
            }
            else if (collider is CapsuleCollider cap)
            {
                // Bepu Capsule es a lo largo de Y (igual que CapsuleAxis.Y, el caso habitual).
                float rScale = MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Z));
                float radius = MathF.Max(0.01f, MathF.Abs(cap.Radius) * rScale);
                float totalH = MathF.Abs(cap.Height) * MathF.Abs(scale.Y);
                float length = MathF.Max(0.01f, totalH - 2f * radius);
                var shape = new Capsule(radius, length);
                shapeIndex = _sim!.Shapes.Add(shape);
                inertia = shape.ComputeInertia(mass);
            }
            else
            {
                var s = (SphereCollider)collider;
                float r = MathF.Max(0.01f, MathF.Abs(s.Radius) *
                    MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z))));
                var shape = new Sphere(r);
                shapeIndex = _sim!.Shapes.Add(shape);
                inertia = shape.ComputeInertia(mass);
            }

            bool dynamic = rb != null && !rb.IsKinematic;
            bool kinematic = rb != null && rb.IsKinematic;
            if (dynamic)
            {
                var handle = _sim!.Bodies.Add(BodyDescription.CreateDynamic(
                    new RigidPose(bodyPos, orient),
                    inertia,
                    // CCD (detección continua) → los objetos rápidos no atraviesan paredes.
                    new CollidableDescription(shapeIndex, 0.1f, ContinuousDetection.Continuous(1e-3f, 1e-3f)),
                    new BodyActivityDescription(0.01f)));
                _dynamics.Add((obj, handle, localScale, centerOffset));
                _rigidbodyToBody[rb!] = handle;
                if (rb != null)
                {
                    var v = rb.Velocity;
                    var body = _sim.Bodies[handle];
                    body.Velocity.Linear = new NVector3(v.X, v.Y, v.Z);
                    rb.MarkBepuVelocityDirty();
                }
                _bodyToObj[handle.Value] = obj;
                if (rb!.FreezePositionX || rb.FreezePositionY || rb.FreezePositionZ ||
                    rb.FreezeRotationX || rb.FreezeRotationY || rb.FreezeRotationZ)
                    _frozen[handle.Value] = (bodyPos, orient);
            }
            else if (kinematic)
            {
                // Masa infinita: no cae, no recibe write-back. Su pose la marca el transform
                // cada frame en UpdateKinematics; ahí también se le da velocidad para empujar.
                var handle = _sim!.Bodies.Add(BodyDescription.CreateKinematic(
                    new RigidPose(bodyPos, orient),
                    new CollidableDescription(shapeIndex, 0.1f),
                    new BodyActivityDescription(0.01f)));
                _kinematics.Add((obj, handle, localScale, centerOffset));
                _rigidbodyToBody[rb!] = handle;
                _bodyToObj[handle.Value] = obj;
            }
            else
            {
                var sh = _sim!.Statics.Add(new StaticDescription(bodyPos, orient, shapeIndex));
                _staticToObj[sh.Value] = obj;
            }
        }

        // Gravedad + drag por cuerpo (respeta UseGravity/Gravity/Drag/AngularDrag). Solo despiertos.
        private static void ApplyRigidbodyDynamics(float dt)
        {
            if (_sim == null || dt <= 0f)
                return;
            foreach (var dyn in _dynamics)
            {
                var rb = dyn.obj.GetComponent<Rigidbody>();
                if (rb == null)
                    continue;
                var bref = _sim.Bodies[dyn.handle];
                if (!bref.Awake)
                    continue;
                ref var vel = ref bref.Velocity;

                if (rb.BepuVelocityDirty)
                {
                    vel.Linear = new NVector3(rb.Velocity.X, rb.Velocity.Y, rb.Velocity.Z);
                    rb.SyncVelocityFromBepu(rb.Velocity);
                }

                if (_pendingVelocityChanges.Remove(rb, out var dv))
                    vel.Linear += dv;

                if (rb.BepuForcesDirty)
                {
                    var f = rb.ConsumeAccumulatedForce();
                    float invMass = 1f / MathF.Max(0.0001f, rb.Mass);
                    vel.Linear += new NVector3(f.X, f.Y, f.Z) * (invMass * dt);
                }

                if (rb.UseGravity)
                    vel.Linear.Y -= rb.Gravity * dt;
                if (rb.Drag > 0f)
                {
                    float f = 1f / (1f + rb.Drag * dt);
                    vel.Linear.X *= f; vel.Linear.Y *= f; vel.Linear.Z *= f;
                }
                if (rb.AngularDrag > 0f)
                {
                    float f = 1f / (1f + rb.AngularDrag * dt);
                    vel.Angular.X *= f; vel.Angular.Y *= f; vel.Angular.Z *= f;
                }
            }
        }

        // Plataformas móviles / cuerpos kinemáticos: cada frame la pose la dicta el transform
        // (lo mueve el script). Se le calcula la velocidad lineal desde el desplazamiento para
        // que BEPU empuje correctamente a los dinámicos que tenga encima/al lado.
        private static void UpdateKinematics(float dt)
        {
            if (_sim == null || dt <= 0f || _kinematics.Count == 0)
                return;
            foreach (var (obj, handle, localScale, centerOffset) in _kinematics)
            {
                DecomposeWorld(obj, out var npos, out var orient, out _);
                var bodyPos = npos + NVector3.Transform(centerOffset, orient);
                var body = _sim.Bodies[handle];
                var delta = bodyPos - body.Pose.Position;
                body.Pose.Position = bodyPos;
                body.Pose.Orientation = orient;
                if (delta.LengthSquared() > 1e-10f)
                {
                    var v = delta / dt;
                    // Un teletransporte (delta enorme en un frame) generaría una velocidad absurda
                    // que lanzaría los dinámicos encima. En ese caso solo movemos la pose, sin velocidad.
                    const float maxKinSpeed = 30f;
                    body.Velocity.Linear = v.LengthSquared() > maxKinSpeed * maxKinSpeed ? default : v;
                    body.Awake = true;
                }
                else
                {
                    body.Velocity.Linear = default;
                }
            }
        }

        // Respeta los Freeze* tras el timestep: fija posición/rotación de los ejes congelados
        // a su valor inicial y anula su velocidad.
        private static void ApplyFreezeConstraints()
        {
            if (_sim == null || _frozen.Count == 0)
                return;
            foreach (var dyn in _dynamics)
            {
                var rb = dyn.obj.GetComponent<Rigidbody>();
                if (rb == null || !_frozen.TryGetValue(dyn.handle.Value, out var f))
                    continue;

                var bref = _sim.Bodies[dyn.handle];
                ref var pose = ref bref.Pose;
                ref var vel = ref bref.Velocity;

                if (rb.FreezePositionX) { pose.Position.X = f.pos.X; vel.Linear.X = 0f; }
                if (rb.FreezePositionY) { pose.Position.Y = f.pos.Y; vel.Linear.Y = 0f; }
                if (rb.FreezePositionZ) { pose.Position.Z = f.pos.Z; vel.Linear.Z = 0f; }

                if (rb.FreezeRotationX && rb.FreezeRotationY && rb.FreezeRotationZ)
                {
                    pose.Orientation = f.orient;
                    vel.Angular = default;
                }
                else
                {
                    if (rb.FreezeRotationX) vel.Angular.X = 0f;
                    if (rb.FreezeRotationY) vel.Angular.Y = 0f;
                    if (rb.FreezeRotationZ) vel.Angular.Z = 0f;
                }
            }
        }

        // Empuje 2 vías aproximado: si un CharacterController se mueve contra una
        // caja dinámica de Bepu, le imprime velocidad horizontal en la dirección de alejamiento.
        private static void ApplyCharacterPushes(List<GameObject> objects, float dt)
        {
            if (_dynamics.Count == 0 || dt <= 0f || _sim == null)
                return;

            foreach (var obj in EnumerateActive(objects))
            {
                var cc = obj.GetComponent<CharacterController>();
                if (cc == null)
                    continue;

                var cp = obj.Position;
                float charRadius = MathF.Max(0.1f, cc.Radius);
                float halfH = MathF.Max(charRadius, cc.Height * 0.5f);
                float cy = cp.Y + cc.Center.Y;        // centro de la cápsula
                float feetY = cy - halfH;             // pies del personaje

                var md = cc.LastMoveDelta;
                float horizLen = MathF.Sqrt(md.X * md.X + md.Z * md.Z);
                float charSpeed = horizLen / dt;
                if (charSpeed < 0.05f)
                    continue;
                // PushPower 0 (escenas guardadas con el default viejo) = empuje NORMAL (1), no desactivado.
                // Quien quiera más/menos fuerza lo sube/baja; ya no se apaga al estar en 0.
                float effPush = cc.PushPower > 0f ? cc.PushPower : 1f;
                // Dirección de avance del personaje (horizontal, normalizada).
                float mvx = md.X / horizLen, mvz = md.Z / horizLen;
                float pushSpeed = MathF.Min(8f, charSpeed * effPush);

                foreach (var dyn in _dynamics)
                {
                    var bp = _sim.Bodies[dyn.handle].Pose.Position;
                    float dx = bp.X - cp.X, dz = bp.Z - cp.Z;
                    float hdist = MathF.Sqrt(dx * dx + dz * dz);
                    // El alcance tiene en cuenta el TAMAÑO de la caja (su escala): medíamos la distancia
                    // a su CENTRO, así que una caja grande quedaba fuera de un radio fijo y no se empujaba.
                    float boxApprox = 0.5f * MathF.Max(MathF.Abs(dyn.localScale.X), MathF.Abs(dyn.localScale.Z));
                    if (hdist > charRadius + boxApprox + 0.25f || hdist < 1e-4f)
                        continue;
                    if (MathF.Abs(bp.Y - cy) > halfH + 0.6f)
                        continue;
                    // Si el cubo está bajo los pies, el personaje lo PISA (no lo empuja de lado):
                    // no aplicar empuje horizontal — así no se desliza al subirte o bajarte.
                    if (bp.Y < feetY + 0.15f)
                        continue;

                    float nx = dx / hdist, nz = dz / hdist;
                    // Solo empujamos lo que tenemos DELANTE (en la dirección de avance). Antes el
                    // empuje era radial desde el centro → las cajas salían de lado al pasar cerca.
                    float facing = nx * mvx + nz * mvz;
                    if (facing < 0.2f)
                        continue;

                    // Dirección de empuje = mezcla de "hacia donde camino" + "alejar del centro".
                    // Da un empuje natural hacia delante pero con separación limpia si chocas de refilón.
                    float px = mvx * 0.7f + nx * 0.3f;
                    float pz = mvz * 0.7f + nz * 0.3f;
                    float plen = MathF.Sqrt(px * px + pz * pz);
                    if (plen < 1e-5f)
                        continue;
                    px /= plen; pz /= plen;

                    // Respeta la masa: una caja pesada se empuja menos que una ligera (como en Unreal).
                    float invMass = _sim.Bodies[dyn.handle].LocalInertia.InverseMass;
                    float massFactor = Math.Clamp(invMass, 0.2f, 1f);
                    float target = pushSpeed * massFactor;

                    // BEPU DUERME los cuerpos en reposo, por eso la caja se comporta como "static"
                    // tras caer y quedar quieta. La despertamos con el Awakener (fiable) y accedemos a
                    // la velocidad con una referencia FRESCA: al despertar, el cuerpo cambia de set de
                    // memoria y la referencia anterior dejaba de apuntar al sitio correcto.
                    _sim.Awakener.AwakenBody(dyn.handle);
                    ref var vel = ref _sim.Bodies[dyn.handle].Velocity;
                    float along = vel.Linear.X * px + vel.Linear.Z * pz;
                    if (along < target)
                    {
                        vel.Linear.X += (target - along) * px;
                        vel.Linear.Z += (target - along) * pz;
                    }
                }
            }
        }

        // ───────── CharacterController KINEMÁTICO sobre Bepu (collide-and-slide por sweeps) ─────────
        // El personaje NO es un cuerpo de Bepu: es una cápsula kinemática que barre (Sweep) contra
        // el mundo de Bepu y se desliza por las superficies. Colisiona correcto contra geometría
        // rotada, rampas y mallas (a diferencia del AABB del motor propio).
        public static CollisionFlags MoveCharacter(CharacterController cc, MVector3 displacement)
        {
            var obj = cc.gameObject;
            var p = obj.Position;
            bool wasGrounded = cc.IsGrounded;

            if (_sim == null || _pool == null)
            {
                obj.Position = new MVector3(p.X + displacement.X, p.Y + displacement.Y, p.Z + displacement.Z);
                return CollisionFlags.None;
            }

            try
            {
            float radius = MathF.Max(0.05f, cc.Radius);
            float height = MathF.Max(radius * 2f, cc.Height);
            float skin = Math.Clamp(cc.SkinWidth, 0.005f, 0.08f);
            float slopeCos = MathF.Cos(Math.Clamp(cc.SlopeLimit, 1f, 89f) * MathF.PI / 180f);
            float stepOffset = Math.Clamp(cc.StepOffset, 0f, MathF.Max(0f, height - radius * 2f));
            var capsule = new Capsule(MathF.Max(0.01f, radius - skin), MathF.Max(0.01f, height - 2f * radius));

            // La posición del objeto está en los pies; el centro de la cápsula suma Center.
            var center = new NVector3(p.X + cc.Center.X, p.Y + cc.Center.Y, p.Z + cc.Center.Z);
            var remaining = new NVector3(displacement.X, displacement.Y, displacement.Z);
            CollisionFlags flags = CollisionFlags.None;
            NVector3 lastHitNormal = default;
            NVector3 lastWallNormal = default;

            for (int iter = 0; iter < 5; iter++)
            {
                if (remaining.Length() < 1e-5f)
                    break;

                var pose = new RigidPose(center, NQuaternion.Identity);
                var velocity = new BodyVelocity { Linear = remaining };
                var handler = new ClosestSweepHandler();
                _sim.Sweep(capsule, pose, velocity, 1f, _pool, ref handler);

                // Contacto en T=0 (la cápsula ya está tocando algo): BEPU no da normal. Re-barremos
                // desde un poco ATRÁS en la dirección de avance para obtener la normal REAL del
                // obstáculo. Así distinguimos suelo (n.Y alto → no bloquea el avance) de pared
                // (n.Y bajo → bloquea y desliza), y dejamos de ATRAVESAR las paredes que rozamos.
                if (handler.Hit && handler.ZeroT)
                {
                    float rl = remaining.Length();
                    NVector3 contactN = default;
                    if (rl > 1e-6f)
                    {
                        var dir = remaining / rl;
                        var rePose = new RigidPose(center - dir * radius, NQuaternion.Identity);
                        var reVel = new BodyVelocity { Linear = dir * (radius + rl + skin) };
                        var reHandler = new ClosestSweepHandler();
                        _sim.Sweep(capsule, rePose, reVel, 1f, _pool, ref reHandler);
                        contactN = (reHandler.Hit && reHandler.Normal.Length() > 1e-5f)
                            ? reHandler.Normal
                            : -dir;   // fallback: bloquear en la dirección de avance
                    }
                    handler.Normal = contactN;
                    handler.T = 0f;
                }

                if (!handler.Hit)
                {
                    center += remaining;
                    break;
                }

                float t = Math.Clamp(handler.T, 0f, 1f);
                center += remaining * t;

                var n = handler.Normal;
                float nlen = n.Length();
                if (nlen < 1e-5f)
                    break; // solape sin normal: parar para no atravesar
                n /= nlen;
                lastHitNormal = n;
                bool isWalkableSlope = n.Y >= slopeCos;
                bool isWallLike = !isWalkableSlope && MathF.Abs(n.Y) <= 0.65f;
                if (isWallLike)
                    lastWallNormal = n;

                // StepOffset real: si chocamos de lado caminando y estamos en suelo, probamos subir,
                // avanzar horizontalmente y bajar otra vez. Esto evita que escalones bajos se sientan
                // como paredes y reduce vibración al correr contra bordes.
                if (isWallLike && (wasGrounded || (flags & CollisionFlags.Below) != 0) && stepOffset > 0.001f)
                {
                    var horizontalRem = new NVector3(remaining.X, 0f, remaining.Z) * (1f - t);
                    if (horizontalRem.LengthSquared() > 1e-6f && TryStepUpAndForward(capsule, center, horizontalRem, stepOffset, skin, out var steppedCenter))
                    {
                        center = steppedCenter;
                        flags |= CollisionFlags.Below | CollisionFlags.Sides;
                        remaining = new NVector3(0f, MathF.Min(0f, remaining.Y), 0f);
                        continue;
                    }
                }

                // Separación estable:
                // Antes siempre empujábamos por todo el SkinWidth. Cuando el personaje estaba
                // apoyado y la gravedad mandaba un pequeño movimiento hacia abajo cada frame,
                // BEPU encontraba el suelo y nosotros subíamos otra vez `skin`, creando vibración.
                // En suelo/rampa y movimiento descendente solo aplicamos una separación mínima.
                float separation;
                if (n.Y > 0.5f && remaining.Y <= 0f)
                    separation = 0.001f;              // suelo estable: no subir/bajar cada frame
                else if (MathF.Abs(n.Y) <= 0.5f)
                    separation = 0.0015f;             // pared estable: no empujar todo el SkinWidth lateral
                else
                    separation = MathF.Min(skin, 0.006f); // techo/otros contactos
                center += n * separation;

                if (n.Y >= slopeCos) flags |= CollisionFlags.Below;       // suelo/rampa pisable según SlopeLimit
                else if (n.Y < -0.5f) flags |= CollisionFlags.Above; // techo
                else flags |= CollisionFlags.Sides;                  // pared o rampa demasiado inclinada

                // Deslizar el resto del movimiento sobre el plano de contacto.
                var rem = remaining * (1f - t);
                float intoSurface = NVector3.Dot(rem, n);
                if (intoSurface < 0f)
                    rem -= n * intoSurface;
                else
                    rem -= n * NVector3.Dot(rem, n);

                // Evita micro-oscilaciones al mantener W contra una pared:
                // si el resto es casi todo empuje hacia el obstáculo, se cancela.
                if (MathF.Abs(n.Y) <= 0.5f && rem.LengthSquared() < 1e-6f)
                    rem = default;
                remaining = rem;
            }

            // Sonda de suelo: barrido corto hacia abajo. Detecta el apoyo de forma robusta
            // (independiente del sweep de movimiento), así IsGrounded funciona al estar parado.
            {
                // Empezamos la sonda un poco arriba para evitar hits en T=0 al estar justo tocando el piso.
                // Ese caso no trae normal fiable en BEPU y puede romper IsGrounded/caminar pegado al suelo.
                var probePose = new RigidPose(center + new NVector3(0f, skin * 2f, 0f), NQuaternion.Identity);
                var probeVel = new BodyVelocity { Linear = new NVector3(0f, -1f, 0f) };
                var probe = new ClosestSweepHandler();
                _sim.Sweep(capsule, probePose, probeVel, skin * 3f + 0.14f, _pool, ref probe);
                // Apoyado si hay algo justo debajo, y la superficie no es casi vertical (pared).
                if (probe.Hit && (probe.Normal.Length() < 1e-5f || probe.Normal.Y >= slopeCos))
                    flags |= CollisionFlags.Below;
            }

            var chosenNormal = lastWallNormal.LengthSquared() > 1e-8f ? lastWallNormal : lastHitNormal;
            cc.LastHitNormal = new MVector3(chosenNormal.X, chosenNormal.Y, chosenNormal.Z);
            obj.Position = new MVector3(center.X - cc.Center.X, center.Y - cc.Center.Y, center.Z - cc.Center.Z);
            return flags;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bepu char] {ex.GetType().Name}: {ex.Message}");
                // Fallback seguro: nunca atravesar paredes si falla un sweep.
                // Mantiene el Play vivo y deja una pista visible en la consola.
                obj.Position = p;
                cc.LastHitNormal = MVector3.Zero;
                return CollisionFlags.Sides;
            }
        }

        private static bool TryStepUpAndForward(Capsule capsule, NVector3 center, NVector3 horizontalMove, float stepOffset, float skin, out NVector3 steppedCenter)
        {
            steppedCenter = center;
            if (_sim == null || _pool == null || horizontalMove.LengthSquared() < 1e-8f)
                return false;

            // 1) Subir: si hay techo/obstáculo encima, no hay escalón posible.
            var upPose = new RigidPose(center, NQuaternion.Identity);
            var upHandler = new ClosestSweepHandler();
            _sim.Sweep(capsule, upPose, new BodyVelocity { Linear = new NVector3(0f, stepOffset, 0f) }, 1f, _pool, ref upHandler);
            if (upHandler.Hit)
                return false;

            var raised = center + new NVector3(0f, stepOffset, 0f);

            // 2) Avanzar desde arriba del escalón.
            var fwdPose = new RigidPose(raised, NQuaternion.Identity);
            var fwdHandler = new ClosestSweepHandler();
            _sim.Sweep(capsule, fwdPose, new BodyVelocity { Linear = horizontalMove }, 1f, _pool, ref fwdHandler);
            if (fwdHandler.Hit)
                return false;

            var forward = raised + horizontalMove;

            // 3) Bajar y aterrizar. Si no hay suelo debajo, no lo aceptamos.
            var downPose = new RigidPose(forward, NQuaternion.Identity);
            var downHandler = new ClosestSweepHandler();
            float downDistance = stepOffset + skin * 4f + 0.05f;
            _sim.Sweep(capsule, downPose, new BodyVelocity { Linear = new NVector3(0f, -downDistance, 0f) }, 1f, _pool, ref downHandler);
            if (!downHandler.Hit)
                return false;

            float t = Math.Clamp(downHandler.T, 0f, 1f);
            steppedCenter = forward + new NVector3(0f, -downDistance * t, 0f);
            if (downHandler.Normal.LengthSquared() > 1e-8f)
                steppedCenter += NVector3.Normalize(downHandler.Normal) * MathF.Min(skin, 0.006f);
            return true;
        }

        private struct ClosestSweepHandler : ISweepHitHandler
        {
            public bool Hit;
            public float T;
            public NVector3 Normal;
            public bool ZeroT;   // true si el contacto se reportó en T=0 (cápsula ya tocando, sin normal fiable)

            public bool AllowTest(CollidableReference collidable) => true;
            public bool AllowTest(CollidableReference collidable, int childIndex) => true;

            public void OnHit(ref float maximumT, float t, in NVector3 hitLocation, in NVector3 hitNormal, CollidableReference collidable)
            {
                if (!Hit || t < T)
                {
                    Hit = true;
                    T = t;
                    Normal = hitNormal;
                    ZeroT = false;
                    if (t < maximumT) maximumT = t;
                }
            }

            public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
            {
                // BEPU reporta T=0 cuando la cápsula YA está tocando algo (suelo o pared) y no da
                // normal. Antes se ignoraba y el personaje ATRAVESABA las paredes que rozaba. Ahora
                // lo registramos como contacto en T=0; MoveCharacter re-barre desde atrás para sacar
                // la normal real y decidir si bloquea (pared) o no (suelo).
                if (!Hit)
                {
                    Hit = true;
                    T = 0f;
                    Normal = default;
                    ZeroT = true;
                }
            }
        }

        // Pose de mundo (pos, rotación, escala) del objeto desde su WorldMatrix.
        private static void DecomposeWorld(GameObject obj, out NVector3 pos, out NQuaternion rot, out MVector3 scale)
        {
            if (Matrix4x4.Decompose(obj.WorldMatrix, out var s, out var r, out var t))
            {
                pos = t;
                rot = NQuaternion.Normalize(r);
                scale = new MVector3(MathF.Abs(s.X), MathF.Abs(s.Y), MathF.Abs(s.Z));
            }
            else
            {
                var p = obj.Position;
                pos = new NVector3(p.X, p.Y, p.Z);
                rot = NQuaternion.Identity;
                var ws = obj.WorldScale;
                scale = new MVector3(MathF.Abs(ws.X), MathF.Abs(ws.Y), MathF.Abs(ws.Z));
            }
        }

        // Escribe una pose de MUNDO (de Bepu) en el GameObject; convierte a local si tiene padre.
        private static void WriteBackWorldPose(GameObject obj, NVector3 wpos, NQuaternion wrot, MVector3 localScale)
        {
            if (obj.Parent == null)
            {
                obj.SetLocalTRS(
                    new MVector3(wpos.X, wpos.Y, wpos.Z),
                    new MQuaternion(wrot.X, wrot.Y, wrot.Z, wrot.W),
                    localScale);
                return;
            }

            // Con padre: posición vía el setter world→local; rotación local = inv(rotPadre) * rotMundo.
            obj.Position = new MVector3(wpos.X, wpos.Y, wpos.Z);
            Matrix4x4.Decompose(obj.Parent.WorldMatrix, out _, out var pRot, out _);
            var lq = NQuaternion.Normalize(NQuaternion.Inverse(pRot) * wrot);
            obj.transform.Rotation = new MQuaternion(lq.X, lq.Y, lq.Z, lq.W);
            obj.UseQuaternionRotation = true;
            obj.MarkTransformDirtyRecursive();
        }

        // ParsedMesh (lista de triángulos, 9 floats/tri) → malla de triángulos de Bepu.
        private static bool TryBuildBepuMesh(ParsedMesh pm, MVector3 scale, out Mesh mesh)
        {
            mesh = default;
            var p = pm.Positions;
            int triCount = p.Length / 9;
            if (triCount <= 0)
                return false;

            _pool!.Take<Triangle>(triCount, out var tris);
            for (int t = 0; t < triCount; t++)
            {
                int b = t * 9;
                tris[t] = new Triangle(
                    new NVector3(p[b], p[b + 1], p[b + 2]),
                    new NVector3(p[b + 3], p[b + 4], p[b + 5]),
                    new NVector3(p[b + 6], p[b + 7], p[b + 8]));
            }
            mesh = new Mesh(tris, new NVector3(scale.X, scale.Y, scale.Z), _pool);
            return true;
        }

        private static void AddTerrainStatic(GameObject obj)
        {
            var terrain = obj.GetComponent<Terrain>();
            if (terrain == null)
                return;
            var pm = TerrainMeshGenerator.Generate(terrain);
            DecomposeWorld(obj, out var npos, out var orient, out var scale);
            if (!TryBuildBepuMesh(pm, scale, out var mesh))
                return;
            var sh = _sim!.Statics.Add(new StaticDescription(npos, orient, _sim.Shapes.Add(mesh)));
            _staticToObj[sh.Value] = obj;
        }

        private static void AddMeshStatic(GameObject obj)
        {
            var mf = obj.GetComponent<MeshFilter>();
            if (mf == null || string.IsNullOrWhiteSpace(mf.MeshPath))
                return;
            var pm = ObjLoader.Load(mf.MeshPath);
            if (pm == null)
                return;
            float imp = MathF.Max(0.0001f, mf.ImportScale);
            DecomposeWorld(obj, out var npos, out var orient, out var scale);
            var combined = new MVector3(scale.X * imp, scale.Y * imp, scale.Z * imp);
            if (!TryBuildBepuMesh(pm, combined, out var mesh))
                return;
            var sh = _sim!.Statics.Add(new StaticDescription(npos, orient, _sim.Shapes.Add(mesh)));
            _staticToObj[sh.Value] = obj;
        }

        // ───────── Raycast (preciso, contra todas las formas de Bepu incl. terreno/mesh) ─────────

        public static bool TryRaycast(MVector3 origin, MVector3 direction, float maxDistance,
            bool includeTriggers, int layerMask, out PhysicsRaycastHit hit)
        {
            hit = default;
            if (!_enabled || _sim == null)
                return false;

            var d = new NVector3(direction.X, direction.Y, direction.Z);
            float len = d.Length();
            if (len < 1e-6f || maxDistance <= 0f)
                return false;
            d /= len;
            var o = new NVector3(origin.X, origin.Y, origin.Z);

            var handler = new ClosestHitHandler { IncludeTriggers = includeTriggers, LayerMask = layerMask, T = float.MaxValue };
            _sim.RayCast(o, d, maxDistance, ref handler, 0);
            if (!handler.Hit)
                return false;

            var obj = ResolveGameObject(handler.Collidable);
            var col = obj?.GetComponent<Collider>();
            if (col == null)
                return false;

            var p = o + d * handler.T;
            hit = new PhysicsRaycastHit(col,
                new MVector3(p.X, p.Y, p.Z),
                new MVector3(handler.Normal.X, handler.Normal.Y, handler.Normal.Z),
                handler.T);
            return true;
        }

        internal static GameObject? ResolveGameObject(CollidableReference c)
        {
            if (c.Mobility == CollidableMobility.Static)
                return _staticToObj.TryGetValue(c.StaticHandle.Value, out var s) ? s : null;
            return _bodyToObj.TryGetValue(c.BodyHandle.Value, out var b) ? b : null;
        }

        private struct ClosestHitHandler : IRayHitHandler
        {
            public bool IncludeTriggers;
            public int LayerMask;
            public bool Hit;
            public float T;
            public NVector3 Normal;
            public CollidableReference Collidable;

            public bool AllowTest(CollidableReference collidable)
            {
                var obj = ResolveGameObject(collidable);
                if (obj == null)
                    return false;
                if (!GrokoEngine.LayerMask.Contains(LayerMask, obj.Layer))
                    return false;
                if (!IncludeTriggers)
                {
                    var col = obj.GetComponent<Collider>();
                    if (col != null && col.IsTrigger)
                        return false;
                }
                return true;
            }

            public bool AllowTest(CollidableReference collidable, int childIndex) => true;

            public void OnRayHit(in RayData ray, ref float maximumT, float t, in NVector3 normal, CollidableReference collidable, int childIndex)
            {
                if (!Hit || t < T)
                {
                    Hit = true;
                    T = t;
                    Normal = normal;
                    Collidable = collidable;
                    maximumT = t; // limita siguientes tests a impactos más cercanos
                }
            }
        }
    }

    // ─────── Callbacks requeridos por Bepu (estándar de los demos) ───────

    struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
            => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial.FrictionCoefficient = 1f;
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30f, 1f);
            if (manifold.Count > 0)
                BepuBackend.RecordContact(pair, ref manifold);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
        {
            if (manifold.Count > 0)
                BepuBackend.RecordContact(pair, ref manifold);
            return true;
        }

        public void Dispose() { }
    }

    struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public bool IntegrateVelocityForKinematics => false;

        NVector3 gravity;
        Vector3Wide gravityWideDt;

        public PoseIntegratorCallbacks(NVector3 gravity) : this()
        {
            this.gravity = gravity;
        }

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt)
        {
            Vector3Wide.Broadcast(gravity * dt, out gravityWideDt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            velocity.Linear += gravityWideDt;
        }
    }
}
