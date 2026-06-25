using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MiMotor.Mathematics;
using Quaternion = MiMotor.Mathematics.Quaternion;
using Transform = MiMotor.Mathematics.Transform;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine
{

    // =====================================================
    // PHYSICS ENGINE
    // =====================================================
    public class PhysicsEngine
    {
        private readonly List<Collider> colliders = new List<Collider>();
        private readonly HashSet<Collider> colliderSet = new();
        private readonly Dictionary<Collider, (Vector3 Min, Vector3 Max)> colliderBoundsCache = new();
        private readonly Dictionary<(int x, int y, int z), List<Collider>> spatialHash = new();
        private readonly List<Collider> largeColliders = new();
        private readonly HashSet<Collider> querySet = new();
        private readonly HashSet<ColliderPairKey> previousCollisionPairs = new();
        private readonly HashSet<ColliderPairKey> currentCollisionPairs = new();
        private readonly HashSet<ColliderPairKey> previousTriggerPairs = new();
        private readonly HashSet<ColliderPairKey> currentTriggerPairs = new();
        private readonly Dictionary<ColliderPairKey, BepuContactEvent> currentBepuContacts = new();
        private readonly HashSet<Collider> syncColliderSet = new();
        private readonly HashSet<Collider> bepuColliderSnapshot = new();
        private readonly List<Collider> dispatchSnapshot = new();
        private readonly HashSet<Collider> bepuManagedScratch = new();
        private readonly Stack<List<Collider>> cellListPool = new();
        private readonly Dictionary<Collider, int> bepuColliderSignatures = new();
        private readonly List<Component> stepComponentSnapshot = new();

        public int MaxIterations { get; set; } = 4;
        public float SpatialCellSize { get; set; } = 6f;
        public int MaxSubsteps { get; set; } = 16;
        public float MaxSubstepDelta { get; set; } = 1f / 90f;
        public float ContactSlop { get; set; } = 0.0005f;
        public bool ContinuousCollision { get; set; } = true;
        public bool BroadphaseRaycast { get; set; } = true;
        public int LastRaycastCandidateCount { get; private set; }

        private bool spatialHashDirty = true;

        public void RegisterCollider(Collider collider)
        {
            if (colliderSet.Add(collider))
            {
                colliders.Add(collider);
                spatialHashDirty = true;
                BepuBackend.MarkDirty();
            }
        }

        public void UnregisterCollider(Collider collider)
        {
            if (colliderSet.Remove(collider))
            {
                colliders.Remove(collider);
                colliderBoundsCache.Remove(collider);
                bepuColliderSignatures.Remove(collider);
                spatialHashDirty = true;
                BepuBackend.MarkDirty();
            }
        }

        public void ClearColliders()
        {
            colliders.Clear();
            colliderSet.Clear();
            colliderBoundsCache.Clear();
            syncColliderSet.Clear();
            spatialHash.Clear();
            largeColliders.Clear();
            previousCollisionPairs.Clear();
            currentCollisionPairs.Clear();
            previousTriggerPairs.Clear();
            currentTriggerPairs.Clear();
            currentBepuContacts.Clear();
            bepuColliderSnapshot.Clear();
            bepuColliderSignatures.Clear();
            spatialHashDirty = false;
            BepuBackend.MarkDirty();
        }

        public IReadOnlyList<Collider> GetColliders() => colliders;

        public void MarkSpatialHashDirty()
        {
            spatialHashDirty = true;
            BepuBackend.MarkDirty();
        }

        /// <summary>
        /// Prepara la fachada de física antes de ejecutar scripts.
        /// El CharacterController usa BEPU para sweeps durante Update(), por eso el editor
        /// debe garantizar que la simulación exista antes del pase de scripts, sin avanzar tiempo.
        /// </summary>
        public void EnsureSimulationBuilt(List<GameObject> objects)
        {
            // Solo garantiza que la simulación de BEPU EXISTA (los sweeps del CharacterController
            // la usan durante el pase de scripts). NO hace el sync legacy completo ni reconstruye
            // el spatial hash: de eso se encarga Step() una sola vez por frame. Antes, esto
            // duplicaba SyncPhysicsComponents + 2x RebuildSpatialHash en CADA frame (carísimo).
            if (BepuBackend.Enabled)
                BepuBackend.Step(objects, 0.0);   // dt=0: construye si hace falta, no avanza tiempo
        }

        /// <summary>
        /// Paso único oficial para editor, runtime y tests. Mantiene BEPU, broadphase legacy
        /// y eventos Collision/Trigger en el mismo camino para evitar divergencias.
        /// </summary>
        public void Step(List<GameObject> objects, double deltaTime)
        {
            // Fachada profesional: mantiene colliders/queries/eventos sincronizados y deja
            // la resolución real a BEPU cuando está activo. Esto evita la regresión donde
            // PhysicsEngine.Step(...) ya no disparaba OnCollision/OnTrigger en tests/scripts.
            // rebuildHash:false → el hash se reconstruye UNA vez, después de Bepu (las poses
            // dinámicas cambian con el write-back). Evita reconstruirlo dos veces por frame.
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            SyncPhysicsComponents(objects, rebuildHash: false);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

            if (BepuBackend.Enabled)
            {
                BepuBackend.Step(objects, deltaTime);
                long tB = System.Diagnostics.Stopwatch.GetTimestamp();
                spatialHashDirty = true; // las poses dinámicas pudieron cambiar tras el write-back.
                RebuildSpatialHash();
                long tH = System.Diagnostics.Stopwatch.GetTimestamp();
                LastBepuMs = StepMs(t1, tB);
                LastHashMs = StepMs(tB, tH);
            }
            else
            {
                foreach (var obj in objects)
                    StepRecursive(obj, deltaTime);
                spatialHashDirty = true;
                RebuildSpatialHash();
                LastBepuMs = StepMs(t1, System.Diagnostics.Stopwatch.GetTimestamp());
                LastHashMs = 0f;
            }

            long tE0 = System.Diagnostics.Stopwatch.GetTimestamp();
            DispatchPhysicsEvents();
            long tE1 = System.Diagnostics.Stopwatch.GetTimestamp();
            LastSyncMs = StepMs(t0, t1);
            LastEventsMs = StepMs(tE0, tE1);
        }

        // Desglose del coste de Step() para el profiler (Sync / Bepu / Hash / Eventos).
        public float LastSyncMs, LastBepuMs, LastHashMs, LastEventsMs;
        private static float StepMs(long from, long to) =>
            (float)((to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        public void SyncPhysicsComponents(IEnumerable<GameObject> objects) => SyncPhysicsComponents(objects, true);

        public void SyncPhysicsComponents(IEnumerable<GameObject> objects, bool rebuildHash)
        {
            // Re-registrar los MISMOS colliders cada frame (para eventos/queries) NO debe
            // ensuciar BEPU: si lo hace, BepuBackend.Step reconstruye toda la simulación
            // (BufferPool + Simulation + malla del terreno) en cada frame y se hunde el FPS.
            BepuBackend.SuppressDirty = true;
            try
            {
                colliders.Clear();
                colliderSet.Clear();
                colliderBoundsCache.Clear();
                syncColliderSet.Clear();

                foreach (var obj in objects)
                    SyncPhysicsRecursive(obj);

                // rebuildHash=false cuando el llamador (Step) reconstruye el hash DESPUÉS de Bepu:
                // así no se reconstruye dos veces por frame (con sus asignaciones de listas por celda).
                if (rebuildHash)
                    RebuildSpatialHash();
                else
                    spatialHashDirty = true;
            }
            finally
            {
                BepuBackend.SuppressDirty = false;
            }

            // Reconstruir BEPU SOLO cuando cambia el conjunto de colliders o alguna firma
            // relevante (forma/material/trigger; pose mundial para colliders estáticos).
            // No ensuciamos por write-back de rigidbodies dinámicos cada frame.
            RefreshBepuSnapshotIfNeeded();
        }

        private void RefreshBepuSnapshotIfNeeded()
        {
            // CLAVE: solo comparamos los colliders que BEPU REALMENTE gestiona. El collider del
            // CharacterController (y los triggers) están EXCLUIDOS de BEPU (ver AddObject), pero
            // se mueven cada frame al caminar. Si los incluíamos aquí, su pose cambiante marcaba la
            // sim como sucia y BEPU reconstruía TODO (incl. la malla del terreno) en cada frame.
            bepuManagedScratch.Clear();
            foreach (var c in colliders)
                if (IsBepuManaged(c))
                    bepuManagedScratch.Add(c);

            bool dirty = !bepuManagedScratch.SetEquals(bepuColliderSnapshot);

            if (!dirty)
            {
                foreach (var collider in bepuManagedScratch)
                {
                    int signature = ComputeBepuColliderSignature(collider);
                    if (!bepuColliderSignatures.TryGetValue(collider, out int previous) || previous != signature)
                    {
                        dirty = true;
                        break;
                    }
                }
            }

            if (!dirty)
                return;

            BepuBackend.MarkDirty();
            bepuColliderSnapshot.Clear();
            bepuColliderSnapshot.UnionWith(bepuManagedScratch);
            bepuColliderSignatures.Clear();
            foreach (var collider in bepuManagedScratch)
                bepuColliderSignatures[collider] = ComputeBepuColliderSignature(collider);
        }

        // Mismo criterio que BepuBackend.AddObject: lo que BEPU NO añade no debe disparar rebuilds.
        private static bool IsBepuManaged(Collider collider)
        {
            var go = collider.gameObject;
            if (go == null)
                return false;
            if (go.GetComponent<CharacterController>() != null)   // el character no entra en BEPU
                return false;
            if (collider.IsTrigger)                                // los triggers son sensores, no entran
                return false;
            return collider is BoxCollider or SphereCollider or CapsuleCollider or MeshCollider or TerrainCollider;
        }

        private static int ComputeBepuColliderSignature(Collider collider)
        {
            var hash = new HashCode();
            hash.Add(collider.GetType());
            hash.Add(collider.Enabled);
            hash.Add(collider.gameObject?.IsActive ?? false);
            hash.Add(collider.IsTrigger);
            hash.Add(collider.PhysicMaterial ?? string.Empty, StringComparer.Ordinal);
            hash.Add(Quantize(collider.Friction));
            hash.Add(Quantize(collider.Bounciness));
            hash.Add(Quantize(collider.Center.X));
            hash.Add(Quantize(collider.Center.Y));
            hash.Add(Quantize(collider.Center.Z));

            // Collider ESTATICO (sin Rigidbody): su POSE MUNDIAL entra en la firma -> si el objeto se
            // mueve/rota/escala, hay que reconstruir BEPU. Con Rigidbody (dinamico o kinematic) la pose
            // la gestiona BEPU, NO se incluye (si no, reconstruiria cada frame al integrar la posicion).
            if (collider.gameObject != null && collider.gameObject.GetComponent<Rigidbody>() == null)
            {
                var wb = collider.GetBounds();
                hash.Add(Quantize(wb.Min.X)); hash.Add(Quantize(wb.Min.Y)); hash.Add(Quantize(wb.Min.Z));
                hash.Add(Quantize(wb.Max.X)); hash.Add(Quantize(wb.Max.Y)); hash.Add(Quantize(wb.Max.Z));
            }

            switch (collider)
            {
                case BoxCollider box:
                    hash.Add(Quantize(box.Size.X));
                    hash.Add(Quantize(box.Size.Y));
                    hash.Add(Quantize(box.Size.Z));
                    break;
                case SphereCollider sphere:
                    hash.Add(Quantize(sphere.Radius));
                    break;
                case CapsuleCollider capsule:
                    hash.Add(Quantize(capsule.Radius));
                    hash.Add(Quantize(capsule.Height));
                    hash.Add(capsule.Axis);
                    break;
                case MeshCollider mesh:
                    hash.Add(Quantize(mesh.Size.X));
                    hash.Add(Quantize(mesh.Size.Y));
                    hash.Add(Quantize(mesh.Size.Z));
                    hash.Add(mesh.UseMeshBounds);
                    var mf = collider.gameObject?.GetComponent<MeshFilter>();
                    hash.Add(mf?.MeshPath ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                    hash.Add(mf?.SubmeshIndex ?? -1);
                    hash.Add(Quantize(mf?.ImportScale ?? 1f));
                    break;
                case TerrainCollider:
                    var terrain = collider.gameObject?.GetComponent<Terrain>();
                    hash.Add(terrain?.Version ?? 0);
                    hash.Add(Quantize(terrain?.SizeX ?? 1f));
                    hash.Add(Quantize(terrain?.SizeZ ?? 1f));
                    hash.Add(Quantize(terrain?.HeightScale ?? 1f));
                    break;
            }

            var rb = collider.gameObject?.GetComponent<Rigidbody>();
            bool isStaticCollider = rb == null;
            if (rb != null)
            {
                hash.Add(rb.IsKinematic);
                hash.Add(Quantize(rb.Mass));
                hash.Add(Quantize(rb.Friction));
                hash.Add(Quantize(rb.Bounciness));
            }

            // BEPU actualiza dinámicos/kinemáticos sin reconstruir. Los colliders sin Rigidbody
            // son estáticos, así que si se mueven/rotan/escalan hay que reconstruir la shape estática.
            if (isStaticCollider && collider.gameObject != null)
            {
                var p = collider.gameObject.WorldPosition;
                var s = collider.gameObject.WorldScale;
                hash.Add(Quantize(p.X));
                hash.Add(Quantize(p.Y));
                hash.Add(Quantize(p.Z));
                hash.Add(Quantize(s.X));
                hash.Add(Quantize(s.Y));
                hash.Add(Quantize(s.Z));
                hash.Add(Quantize(collider.gameObject.RotX));
                hash.Add(Quantize(collider.gameObject.RotY));
                hash.Add(Quantize(collider.gameObject.RotZ));

                // Los huesos animados y algunos sistemas de runtime pueden rotar usando
                // transform.Rotation directamente (cuaternión) sin tocar RotX/RotY/RotZ.
                // Si el collider es estático, BEPU necesita reconstruir la shape cuando
                // cambia esa orientación exacta, igual que Unity actualiza sus static colliders.
                hash.Add(collider.gameObject.UseQuaternionRotation);
                var q = collider.gameObject.transform.Rotation;
                hash.Add(Quantize(q.X));
                hash.Add(Quantize(q.Y));
                hash.Add(Quantize(q.Z));
                hash.Add(Quantize(q.W));
            }

            return hash.ToHashCode();
        }

        private static int Quantize(float value)
            => (int)MathF.Round(value * 10000f);

        private void SyncPhysicsComponentsIncremental(IEnumerable<GameObject> objects)
        {
            syncColliderSet.Clear();
            foreach (var obj in objects)
                SyncPhysicsRecursive(obj, syncColliderSet);

            for (int i = colliders.Count - 1; i >= 0; i--)
            {
                if (!syncColliderSet.Contains(colliders[i]))
                {
                    colliderSet.Remove(colliders[i]);
                    colliderBoundsCache.Remove(colliders[i]);
                    colliders.RemoveAt(i);
                    spatialHashDirty = true;
                    BepuBackend.MarkDirty();
                }
            }
        }

        private void SyncPhysicsRecursive(GameObject obj)
        {
            foreach (var component in obj.Components)
            {
                if (component is Collider collider)
                    RegisterCollider(collider);

                if (component is Rigidbody rigidbody)
                    rigidbody.Physics = this;

                if (component is CharacterController characterController)
                    characterController.Physics = this;

                if (component is ParticleSystem particleSystem)
                    particleSystem.Physics = this;
            }

            foreach (var child in obj.Children)
                SyncPhysicsRecursive(child);
        }

        private void SyncPhysicsRecursive(GameObject obj, HashSet<Collider> liveColliders)
        {
            foreach (var component in obj.Components)
            {
                if (component is Collider collider)
                {
                    liveColliders.Add(collider);
                    if (colliderSet.Add(collider))
                    {
                        colliders.Add(collider);
                        var bounds = collider.GetBounds();
                        colliderBoundsCache[collider] = (bounds.Min, bounds.Max);
                        spatialHashDirty = true;
                        BepuBackend.MarkDirty();
                    }
                    else
                    {
                        var bounds = collider.GetBounds();
                        if (!colliderBoundsCache.TryGetValue(collider, out var cached) ||
                            !SameVector(cached.Min, bounds.Min) ||
                            !SameVector(cached.Max, bounds.Max))
                        {
                            spatialHashDirty = true;
                            BepuBackend.MarkDirty();
                        }

                        colliderBoundsCache[collider] = (bounds.Min, bounds.Max);
                    }
                }

                if (component is Rigidbody rigidbody)
                    rigidbody.Physics = this;

                if (component is CharacterController characterController)
                    characterController.Physics = this;

                if (component is ParticleSystem particleSystem)
                    particleSystem.Physics = this;
            }

            foreach (var child in obj.Children)
                SyncPhysicsRecursive(child, liveColliders);
        }

        private static bool SameVector(Vector3 a, Vector3 b)
        {
            const float epsilon = 0.0001f;
            return Math.Abs(a.X - b.X) <= epsilon &&
                   Math.Abs(a.Y - b.Y) <= epsilon &&
                   Math.Abs(a.Z - b.Z) <= epsilon;
        }

        private void StepRecursive(GameObject obj, double deltaTime)
        {
            if (!obj.IsActive)
                return;

            stepComponentSnapshot.Clear();
            stepComponentSnapshot.AddRange(obj.Components);

            foreach (var component in stepComponentSnapshot)
            {
                if (!component.Enabled)
                    continue;
                if (component is not Rigidbody && component is not CharacterController && component is not ParticleSystem)
                    continue;

                component.InternalAwake();
                component.InternalStart();
                component.Update(deltaTime);
            }

            foreach (var child in obj.Children)
                StepRecursive(child, deltaTime);
        }

        // Puente de eventos legacy. NO resuelve física ni mueve rigidbodies.
        // Úsalo solo si necesitas mantener OnCollision/OnTrigger antiguos mientras se migran
        // a callbacks puros de Bepu.
        public void DispatchPhysicsEvents()
        {
            EnsureSpatialHash();

            currentCollisionPairs.Clear();
            currentTriggerPairs.Clear();
            currentBepuContacts.Clear();

            bool useBepuCollisionEvents = BepuBackend.Enabled &&
                                          BepuBackend.IsReady &&
                                          BepuBackend.HasFreshContactFrame;

            if (useBepuCollisionEvents)
                FillBepuCollisionPairs();

            // Triggers siguen por broadphase legacy: no se insertan como sólidos en BEPU para
            // no bloquear movimiento. Las colisiones sólidas usan BEPU cuando hay frame fresco.
            dispatchSnapshot.Clear();
            dispatchSnapshot.AddRange(colliders);
            foreach (var collider in dispatchSnapshot)
            {
                if (!IsColliderAvailable(collider))
                    continue;

                var bounds = collider.GetBounds();
                foreach (var otherCollider in QueryNearby(collider))
                {
                    if (!IsColliderAvailable(otherCollider))
                        continue;

                    var key = new ColliderPairKey(collider, otherCollider);
                    if (currentCollisionPairs.Contains(key) || currentTriggerPairs.Contains(key))
                        continue;

                    if (!bounds.Intersects(GetNarrowBounds(otherCollider, collider)))
                        continue;

                    if (collider.IsTrigger || otherCollider.IsTrigger)
                    {
                        currentTriggerPairs.Add(key);
                    }
                    else if (!useBepuCollisionEvents || IsStaticStaticCompatibilityPair(collider, otherCollider))
                    {
                        // Fallback legacy solo cuando BEPU está apagado/no listo/no hubo timestep.
                        // Compatibilidad: BEPU no genera contactos static-static, pero scripts viejos
                        // podían usar OnCollision entre dos objetos sin Rigidbody.
                        currentCollisionPairs.Add(key);
                    }
                }
            }

            if (useBepuCollisionEvents)
                DispatchBepuCollisionPairEvents();
            else
                DispatchCollisionPairEvents();
            DispatchTriggerPairEvents();

            CopyPairs(currentCollisionPairs, previousCollisionPairs);
            CopyPairs(currentTriggerPairs, previousTriggerPairs);
        }

        private static bool IsStaticStaticCompatibilityPair(Collider a, Collider b)
        {
            return a.gameObject?.GetComponent<Rigidbody>() == null &&
                   b.gameObject?.GetComponent<Rigidbody>() == null;
        }

        private void FillBepuCollisionPairs()
        {
            foreach (var contact in BepuBackend.ContactEvents)
            {
                var a = contact.A.GetComponent<Collider>();
                var b = contact.B.GetComponent<Collider>();
                if (a == null || b == null || !IsColliderAvailable(a) || !IsColliderAvailable(b))
                    continue;
                if (a.IsTrigger || b.IsTrigger)
                    continue;

                var key = new ColliderPairKey(a, b);
                currentCollisionPairs.Add(key);
                currentBepuContacts[key] = contact;
                ApplyRigidbodyContactState(contact);
            }
        }

        private static void ApplyRigidbodyContactState(BepuContactEvent contact)
        {
            ApplyRigidbodyContactState(contact.A, contact.Normal);
            ApplyRigidbodyContactState(contact.B, Scale(contact.Normal, -1f));
        }

        private static void ApplyRigidbodyContactState(GameObject obj, Vector3 normal)
        {
            var rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            rb.LastCollisionNormal = normal;
            if (normal.Y > 0.55f)
                rb.IsGrounded = true;
        }

        private void DispatchBepuCollisionPairEvents()
        {
            foreach (var pair in currentCollisionPairs)
            {
                var kind = previousCollisionPairs.Contains(pair) ? PhysicsEventKind.Stay : PhysicsEventKind.Enter;
                if (currentBepuContacts.TryGetValue(pair, out var contact))
                    DispatchBepuCollisionPair(pair, contact, kind);
                else
                    DispatchCollisionPair(pair, kind);
            }

            foreach (var pair in previousCollisionPairs)
            {
                if (!currentCollisionPairs.Contains(pair))
                    DispatchCollisionPair(pair, PhysicsEventKind.Exit);
            }
        }

        private static void DispatchBepuCollisionPair(ColliderPairKey pair, BepuContactEvent contact, PhysicsEventKind kind)
        {
            var normalA = ContactNormalFor(contact, pair.A);
            var normalB = Scale(normalA, -1f);
            DispatchCollision(pair.A, new Collision(pair.A, pair.B, contact.Point, normalA), kind);
            DispatchCollision(pair.B, new Collision(pair.B, pair.A, contact.Point, normalB), kind);
        }

        private static Vector3 ContactNormalFor(BepuContactEvent contact, Collider receiver)
        {
            if (ReferenceEquals(receiver.gameObject, contact.A))
                return contact.Normal;
            return Scale(contact.Normal, -1f);
        }

        private void DispatchCollisionPairEvents()
        {
            foreach (var pair in currentCollisionPairs)
            {
                var kind = previousCollisionPairs.Contains(pair) ? PhysicsEventKind.Stay : PhysicsEventKind.Enter;
                DispatchCollisionPair(pair, kind);
            }

            foreach (var pair in previousCollisionPairs)
            {
                if (!currentCollisionPairs.Contains(pair))
                    DispatchCollisionPair(pair, PhysicsEventKind.Exit);
            }
        }

        private void DispatchTriggerPairEvents()
        {
            foreach (var pair in currentTriggerPairs)
            {
                var kind = previousTriggerPairs.Contains(pair) ? PhysicsEventKind.Stay : PhysicsEventKind.Enter;
                DispatchTriggerPair(pair, kind);
            }

            foreach (var pair in previousTriggerPairs)
            {
                if (!currentTriggerPairs.Contains(pair))
                    DispatchTriggerPair(pair, PhysicsEventKind.Exit);
            }
        }

        private static void DispatchCollisionPair(ColliderPairKey pair, PhysicsEventKind kind)
        {
            var point = ContactPoint(pair.A, pair.B);
            var normalA = CollisionNormal(pair.A, pair.B);
            var normalB = Scale(normalA, -1f);

            DispatchCollision(pair.A, new Collision(pair.A, pair.B, point, normalA), kind);
            DispatchCollision(pair.B, new Collision(pair.B, pair.A, point, normalB), kind);
        }

        private static void DispatchTriggerPair(ColliderPairKey pair, PhysicsEventKind kind)
        {
            DispatchTrigger(pair.A, pair.B, kind);
            DispatchTrigger(pair.B, pair.A, kind);
        }

        private static void DispatchCollision(Collider receiver, Collision collision, PhysicsEventKind kind)
        {
            var obj = receiver.gameObject;
            if (obj == null || !obj.IsActive)
                return;

            foreach (var behaviour in obj.GetComponents<MonoBehaviour>())
            {
                if (!behaviour.Enabled)
                    continue;

                if (kind == PhysicsEventKind.Enter)
                    behaviour.OnCollisionEnter(collision);
                else if (kind == PhysicsEventKind.Stay)
                    behaviour.OnCollisionStay(collision);
                else
                    behaviour.OnCollisionExit(collision);
            }
        }

        private static void DispatchTrigger(Collider receiver, Collider other, PhysicsEventKind kind)
        {
            var obj = receiver.gameObject;
            if (obj == null || !obj.IsActive)
                return;

            foreach (var behaviour in obj.GetComponents<MonoBehaviour>())
            {
                if (!behaviour.Enabled)
                    continue;

                if (kind == PhysicsEventKind.Enter)
                    behaviour.OnTriggerEnter(other);
                else if (kind == PhysicsEventKind.Stay)
                    behaviour.OnTriggerStay(other);
                else
                    behaviour.OnTriggerExit(other);
            }
        }

        private static void CopyPairs(HashSet<ColliderPairKey> source, HashSet<ColliderPairKey> destination)
        {
            destination.Clear();
            foreach (var pair in source)
                destination.Add(pair);
        }

        private static bool IsColliderAvailable(Collider collider)
        {
            return collider.Enabled &&
                   collider.gameObject != null &&
                   collider.gameObject.IsActive;
        }

        private static Vector3 ContactPoint(Collider a, Collider b)
        {
            var aBounds = a.GetBounds();
            var bBounds = b.GetBounds();
            float minX = Math.Max(aBounds.Min.X, bBounds.Min.X);
            float minY = Math.Max(aBounds.Min.Y, bBounds.Min.Y);
            float minZ = Math.Max(aBounds.Min.Z, bBounds.Min.Z);
            float maxX = Math.Min(aBounds.Max.X, bBounds.Max.X);
            float maxY = Math.Min(aBounds.Max.Y, bBounds.Max.Y);
            float maxZ = Math.Min(aBounds.Max.Z, bBounds.Max.Z);

            if (minX <= maxX && minY <= maxY && minZ <= maxZ)
                return new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);

            var centerA = BoundsCenter(aBounds);
            var centerB = BoundsCenter(bBounds);
            return Scale(Add(centerA, centerB), 0.5f);
        }

        private static Vector3 CollisionNormal(Collider self, Collider other)
        {
            var delta = Sub(BoundsCenter(self.GetBounds()), BoundsCenter(other.GetBounds()));
            float length = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
            if (length <= 0.000001f)
                return new Vector3(0f, 1f, 0f);

            return Scale(delta, 1f / length);
        }

        private static Vector3 BoundsCenter(Bounds bounds)
        {
            return new Vector3(
                (bounds.Min.X + bounds.Max.X) * 0.5f,
                (bounds.Min.Y + bounds.Max.Y) * 0.5f,
                (bounds.Min.Z + bounds.Max.Z) * 0.5f);
        }

        private enum PhysicsEventKind
        {
            Enter,
            Stay,
            Exit
        }

        private readonly struct ColliderPairKey : IEquatable<ColliderPairKey>
        {
            public ColliderPairKey(Collider a, Collider b)
            {
                A = a;
                B = b;
            }

            public Collider A { get; }
            public Collider B { get; }

            public bool Equals(ColliderPairKey other)
            {
                return (ReferenceEquals(A, other.A) && ReferenceEquals(B, other.B)) ||
                       (ReferenceEquals(A, other.B) && ReferenceEquals(B, other.A));
            }

            public override bool Equals(object? obj) => obj is ColliderPairKey other && Equals(other);

            public override int GetHashCode()
            {
                return RuntimeHelpers.GetHashCode(A) ^ RuntimeHelpers.GetHashCode(B);
            }
        }

        private void EnsureSpatialHash()
        {
            if (spatialHashDirty)
                RebuildSpatialHash();
        }

        private void RebuildSpatialHash()
        {
            // Recicla las listas de celda en un pool en vez de descartarlas y crear nuevas cada
            // frame: eso generaba basura constante (gen0) → pausas de GC → tirones. Tras el
            // calentamiento, reconstruir el hash no asigna memoria.
            foreach (var kv in spatialHash)
            {
                kv.Value.Clear();
                cellListPool.Push(kv.Value);
            }
            spatialHash.Clear();
            largeColliders.Clear();

            foreach (var collider in colliders)
            {
                if (!IsColliderAvailable(collider))
                    continue;

                var bounds = collider.GetBounds();
                colliderBoundsCache[collider] = (bounds.Min, bounds.Max);
                var min = CellOf(bounds.Min);
                var max = CellOf(bounds.Max);
                if (IsLargeBroadphaseCollider(min, max))
                {
                    largeColliders.Add(collider);
                    continue;
                }

                for (int x = min.x; x <= max.x; x++)
                    for (int y = min.y; y <= max.y; y++)
                        for (int z = min.z; z <= max.z; z++)
                        {
                            var key = (x, y, z);

                            if (!spatialHash.TryGetValue(key, out var list))
                            {
                                list = cellListPool.Count > 0 ? cellListPool.Pop() : new List<Collider>(4);
                                spatialHash[key] = list;
                            }

                            list.Add(collider);
                        }
            }

            spatialHashDirty = false;
        }

        private static bool IsLargeBroadphaseCollider((int x, int y, int z) min, (int x, int y, int z) max)
        {
            int cellsX = Math.Max(1, max.x - min.x + 1);
            int cellsY = Math.Max(1, max.y - min.y + 1);
            int cellsZ = Math.Max(1, max.z - min.z + 1);
            return cellsX * cellsY * cellsZ > 256;
        }

        private (int x, int y, int z) CellOf(Vector3 position)
        {
            float size = Math.Max(0.001f, SpatialCellSize);

            return (
                (int)MathF.Floor(position.X / size),
                (int)MathF.Floor(position.Y / size),
                (int)MathF.Floor(position.Z / size));
        }

        private List<Collider> QueryNearby(Collider myCollider)
        {
            EnsureSpatialHash();

            querySet.Clear();

            var bounds = myCollider.GetBounds();
            var min = CellOf(bounds.Min);
            var max = CellOf(bounds.Max);

            for (int x = min.x - 1; x <= max.x + 1; x++)
                for (int y = min.y - 1; y <= max.y + 1; y++)
                    for (int z = min.z - 1; z <= max.z + 1; z++)
                    {
                        if (spatialHash.TryGetValue((x, y, z), out var list))
                        {
                            foreach (var collider in list)
                            {
                                if (collider != myCollider && collider.gameObject != myCollider.gameObject)
                                    querySet.Add(collider);
                            }
                        }
                    }

            foreach (var collider in largeColliders)
                if (collider != myCollider && collider.gameObject != myCollider.gameObject)
                    querySet.Add(collider);

            return new List<Collider>(querySet);
        }

        public void MoveRigidbody(Rigidbody rigidbody, Collider myCollider, Vector3 targetWorldPosition, float deltaTime)
        {
            if (myCollider.IsTrigger)
            {
                myCollider.gameObject.Position = targetWorldPosition;
                spatialHashDirty = true;
                return;
            }

            var start = myCollider.gameObject.Position;
            var totalDelta = Sub(targetWorldPosition, start);
            int substeps = ComputeSubsteps(myCollider, totalDelta, deltaTime);
            var stepDelta = Scale(totalDelta, 1f / substeps);

            for (int step = 0; step < substeps; step++)
            {
                if (!rigidbody.FreezePositionY)
                    MoveAxis(rigidbody, myCollider, 1, stepDelta.Y);
                if (!rigidbody.FreezePositionX)
                    MoveAxis(rigidbody, myCollider, 0, stepDelta.X);
                if (!rigidbody.FreezePositionZ)
                    MoveAxis(rigidbody, myCollider, 2, stepDelta.Z);

                for (int iteration = 0; iteration < MaxIterations; iteration++)
                {
                    if (!Depenetrate(rigidbody, myCollider))
                        break;
                }
            }

            if (rigidbody.IsGrounded)
            {
                float friction = Math.Clamp(Math.Max(rigidbody.Friction, myCollider.Friction), 0f, 1f);
                float factor = Math.Max(0f, 1f - friction * deltaTime * 12f);
                rigidbody.Velocity.X *= factor;
                rigidbody.Velocity.Z *= factor;
            }

            spatialHashDirty = true;
        }

        public CollisionFlags MoveCharacterController(CharacterController controller, CapsuleCollider collider, Vector3 displacement)
        {
            if (collider.IsTrigger)
            {
                collider.gameObject.Position = Add(collider.gameObject.Position, displacement);
                spatialHashDirty = true;
                return CollisionFlags.None;
            }

            CollisionFlags flags = CollisionFlags.None;
            var startPosition = collider.gameObject.Position;
            int substeps = ComputeSubsteps(collider, displacement, 1f / 60f);
            var stepDelta = Scale(displacement, 1f / substeps);

            for (int step = 0; step < substeps; step++)
            {
                if (stepDelta.Y != 0f)
                    flags |= MoveCharacterAxis(controller, collider, 1, stepDelta.Y, allowStep: false);

                if (stepDelta.X != 0f)
                    flags |= MoveCharacterAxis(controller, collider, 0, stepDelta.X, allowStep: true);

                if (stepDelta.Z != 0f)
                    flags |= MoveCharacterAxis(controller, collider, 2, stepDelta.Z, allowStep: true);
            }

            if ((flags & CollisionFlags.Below) != 0 && controller.Velocity.Y < 0f)
                controller.Velocity.Y = -0.5f;
            if ((flags & CollisionFlags.Above) != 0 && controller.Velocity.Y > 0f)
                controller.Velocity.Y = 0f;
            var endPosition = collider.gameObject.Position;
            if ((Math.Abs(displacement.X) > 0.0001f && Math.Abs((endPosition.X - startPosition.X) - displacement.X) > controller.SkinWidth * 2f) ||
                (Math.Abs(displacement.Z) > 0.0001f && Math.Abs((endPosition.Z - startPosition.Z) - displacement.Z) > controller.SkinWidth * 2f))
                flags |= CollisionFlags.Sides;
            if ((flags & CollisionFlags.Below) == 0 && ProbeCharacterGround(controller, collider))
            {
                flags |= CollisionFlags.Below;
                if (controller.Velocity.Y < 0f)
                    controller.Velocity.Y = -0.5f;
            }

            spatialHashDirty = true;
            return flags;
        }

        private bool ProbeCharacterGround(CharacterController controller, CapsuleCollider collider)
        {
            var bounds = collider.GetBounds();
            float snapDistance = Math.Max(0.03f, controller.SkinWidth * 2f + 0.01f);

            foreach (var otherCollider in QueryNearby(collider))
            {
                if (!IsSolidObstacle(collider, otherCollider))
                    continue;

                var other = GetNarrowBounds(otherCollider, collider);
                bool xOverlap = bounds.Min.X < other.Max.X && bounds.Max.X > other.Min.X;
                bool zOverlap = bounds.Min.Z < other.Max.Z && bounds.Max.Z > other.Min.Z;
                if (!xOverlap || !zOverlap)
                    continue;

                float distance = bounds.Min.Y - other.Max.Y;
                if (distance >= -controller.SkinWidth && distance <= snapDistance)
                {
                    var p = collider.gameObject.Position;
                    p.Y += other.Max.Y - bounds.Min.Y + controller.SkinWidth;
                    collider.gameObject.Position = p;
                    spatialHashDirty = true;
                    return true;
                }
            }

            return false;
        }

        private CollisionFlags MoveCharacterAxis(CharacterController controller, CapsuleCollider collider, int axis, float delta, bool allowStep)
        {
            if (Math.Abs(delta) <= 0.000001f)
                return CollisionFlags.None;

            CollisionFlags flags = CollisionFlags.None;
            int dir = delta > 0f ? 1 : -1;
            var original = collider.gameObject.Position;

            SetWorldAxis(collider.gameObject, axis, GetWorldAxis(collider.gameObject, axis) + delta);
            spatialHashDirty = true;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                bool hit = false;
                var myBounds = collider.GetBounds();

                foreach (var otherCollider in QueryNearby(collider))
                {
                    if (!IsSolidObstacle(collider, otherCollider))
                        continue;

                    var otherBounds = GetNarrowBounds(otherCollider, collider);
                    if (!myBounds.Intersects(otherBounds))
                        continue;

                    if (allowStep && axis != 1 && controller.IsGrounded && controller.StepOffset > controller.SkinWidth &&
                        TryCharacterStep(controller, collider, original, axis, delta))
                    {
                        return flags | CollisionFlags.Below;
                    }

                    float overlap = AxisOverlap(myBounds, otherBounds, axis, dir);
                    if (overlap <= 0f)
                        continue;

                    SetWorldAxis(collider.gameObject, axis, GetWorldAxis(collider.gameObject, axis) - dir * (overlap + controller.SkinWidth));
                    flags |= CharacterFlagsFor(axis, dir);
                    PushDynamicBody(controller, otherCollider, axis, dir);
                    myBounds = collider.GetBounds();
                    spatialHashDirty = true;
                    hit = true;
                }

                if (!hit)
                    break;
            }

            return flags;
        }

        private bool TryCharacterStep(CharacterController controller, CapsuleCollider collider, Vector3 originalPosition, int axis, float delta)
        {
            float stepHeight = Math.Max(0f, controller.StepOffset) + Math.Max(0f, controller.SkinWidth);
            if (stepHeight <= 0.0001f)
                return false;

            collider.gameObject.Position = originalPosition;
            collider.gameObject.Position = Add(collider.gameObject.Position, new Vector3(0f, stepHeight, 0f));
            SetWorldAxis(collider.gameObject, axis, GetWorldAxis(collider.gameObject, axis) + delta);
            spatialHashDirty = true;

            if (HasSolidOverlap(collider))
            {
                collider.gameObject.Position = originalPosition;
                spatialHashDirty = true;
                return false;
            }

            var snapFlags = MoveCharacterAxis(controller, collider, 1, -stepHeight - controller.SkinWidth, allowStep: false);
            if ((snapFlags & CollisionFlags.Below) == 0)
            {
                collider.gameObject.Position = originalPosition;
                spatialHashDirty = true;
                return false;
            }

            return true;
        }

        private bool HasSolidOverlap(CapsuleCollider collider)
        {
            var bounds = collider.GetBounds();
            foreach (var otherCollider in QueryNearby(collider))
            {
                if (IsSolidObstacle(collider, otherCollider) && bounds.Intersects(GetNarrowBounds(otherCollider, collider)))
                    return true;
            }

            return false;
        }

        private static bool IsSolidObstacle(Collider self, Collider other)
        {
            return other != self &&
                   !other.IsTrigger &&
                   other.gameObject != null &&
                   other.gameObject.IsActive;
        }

        private static CollisionFlags CharacterFlagsFor(int axis, int dir)
        {
            if (axis == 1)
                return dir < 0 ? CollisionFlags.Below : CollisionFlags.Above;
            return CollisionFlags.Sides;
        }

        private void PushDynamicBody(CharacterController controller, Collider otherCollider, int axis, int dir)
        {
            if (controller.PushPower <= 0f || axis == 1)
                return;

            var rb = otherCollider.gameObject.GetComponent<Rigidbody>();
            if (rb == null || rb.IsKinematic || AxisFrozen(rb, axis))
                return;

            float current = GetVelocityAxis(rb, axis);
            float push = dir * controller.PushPower;
            if (Math.Abs(push) > Math.Abs(current))
                SetVelocityAxis(rb, axis, push);
        }

        private int ComputeSubsteps(Collider collider, Vector3 delta, float deltaTime)
        {
            if (!ContinuousCollision)
                return 1;

            var bounds = collider.GetBounds();
            float minHalfExtent = Math.Max(0.01f, Math.Min(
                Math.Min((bounds.Max.X - bounds.Min.X) * 0.5f, (bounds.Max.Y - bounds.Min.Y) * 0.5f),
                (bounds.Max.Z - bounds.Min.Z) * 0.5f));
            float maxDistance = Math.Max(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)), Math.Abs(delta.Z));
            int byDistance = (int)MathF.Ceiling(maxDistance / Math.Max(0.02f, minHalfExtent * 0.5f));
            int byTime = MaxSubstepDelta > 0f ? (int)MathF.Ceiling(deltaTime / MaxSubstepDelta) : 1;

            return Math.Clamp(Math.Max(Math.Max(byDistance, byTime), 1), 1, Math.Max(1, MaxSubsteps));
        }

        private void MoveAxis(Rigidbody rigidbody, Collider myCollider, int axis, float delta)
        {
            if (Math.Abs(delta) <= 0.000001f)
                return;

            int dir = delta > 0f ? 1 : -1;
            var obj = myCollider.gameObject;
            SetWorldAxis(obj, axis, GetWorldAxis(obj, axis) + delta);
            spatialHashDirty = true;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                bool hit = false;
                var myBounds = myCollider.GetBounds();

                foreach (var otherCollider in QueryNearby(myCollider))
                {
                    if (otherCollider.IsTrigger || otherCollider.gameObject == null || !otherCollider.gameObject.IsActive)
                        continue;

                    var otherBounds = GetNarrowBounds(otherCollider, myCollider);
                    if (!myBounds.Intersects(otherBounds))
                        continue;

                    float overlap = AxisOverlap(myBounds, otherBounds, axis, dir);
                    if (overlap <= 0f)
                        continue;

                    ResolveAxisContact(rigidbody, myCollider, otherCollider, axis, dir, overlap + ContactSlop);
                    MarkContact(rigidbody, axis, dir);
                    myBounds = myCollider.GetBounds();
                    hit = true;
                }

                if (!hit)
                    break;
            }
        }

        private bool Depenetrate(Rigidbody rigidbody, Collider myCollider)
        {
            var myBounds = myCollider.GetBounds();

            foreach (var otherCollider in QueryNearby(myCollider))
            {
                if (otherCollider.IsTrigger || otherCollider.gameObject == null || !otherCollider.gameObject.IsActive)
                    continue;

                var otherBounds = GetNarrowBounds(otherCollider, myCollider);
                if (!myBounds.Intersects(otherBounds))
                    continue;

                float penX = Math.Min(myBounds.Max.X - otherBounds.Min.X, otherBounds.Max.X - myBounds.Min.X);
                float penY = Math.Min(myBounds.Max.Y - otherBounds.Min.Y, otherBounds.Max.Y - myBounds.Min.Y);
                float penZ = Math.Min(myBounds.Max.Z - otherBounds.Min.Z, otherBounds.Max.Z - myBounds.Min.Z);

                int axis = 0;
                float penetration = penX;
                if (penY < penetration) { axis = 1; penetration = penY; }
                if (penZ < penetration) { axis = 2; penetration = penZ; }

                float myCenter = AxisCenter(myBounds, axis);
                float otherCenter = AxisCenter(otherBounds, axis);
                int dir = myCenter >= otherCenter ? 1 : -1;

                ResolveAxisContact(rigidbody, myCollider, otherCollider, axis, -dir, penetration + ContactSlop);
                if (axis == 1 && dir > 0)
                    MarkContact(rigidbody, axis, -1);
                return true;
            }

            return false;
        }

        private void ResolveAxisContact(Rigidbody rigidbody, Collider myCollider, Collider otherCollider, int axis, int dir, float correction)
        {
            var otherRb = otherCollider.gameObject.GetComponent<Rigidbody>();
            bool otherDynamic = otherRb != null && !otherRb.IsKinematic && !AxisFrozen(otherRb, axis);

            if (!otherDynamic)
            {
                SetWorldAxis(myCollider.gameObject, axis, GetWorldAxis(myCollider.gameObject, axis) - dir * correction);
                ResolveStaticVelocity(rigidbody, myCollider, otherCollider, axis);
                spatialHashDirty = true;
                return;
            }

            float invSelf = 1f / Math.Max(0.0001f, rigidbody.Mass);
            float invOther = 1f / Math.Max(0.0001f, otherRb!.Mass);
            float invTotal = invSelf + invOther;
            float selfPart = invSelf / invTotal;
            float otherPart = invOther / invTotal;

            SetWorldAxis(myCollider.gameObject, axis, GetWorldAxis(myCollider.gameObject, axis) - dir * correction * selfPart);
            SetWorldAxis(otherCollider.gameObject, axis, GetWorldAxis(otherCollider.gameObject, axis) + dir * correction * otherPart);

            ResolveDynamicVelocity(rigidbody, myCollider, otherRb, otherCollider, axis);
            spatialHashDirty = true;
        }

        private void ResolveStaticVelocity(Rigidbody rigidbody, Collider myCollider, Collider otherCollider, int axis)
        {
            float restitution = CombinedBounciness(rigidbody, myCollider, otherCollider);
            float velocity = GetVelocityAxis(rigidbody, axis);

            if (Math.Abs(velocity) < 0.001f || restitution <= 0.001f)
                SetVelocityAxis(rigidbody, axis, 0f);
            else
                SetVelocityAxis(rigidbody, axis, -velocity * restitution);
        }

        private void ResolveDynamicVelocity(Rigidbody a, Collider aCollider, Rigidbody b, Collider bCollider, int axis)
        {
            float ma = Math.Max(0.0001f, a.Mass);
            float mb = Math.Max(0.0001f, b.Mass);
            float va = GetVelocityAxis(a, axis);
            float vb = GetVelocityAxis(b, axis);
            float restitution = Math.Max(CombinedBounciness(a, aCollider, bCollider), CombinedBounciness(b, bCollider, aCollider));

            float newA = ((ma - restitution * mb) * va + (1f + restitution) * mb * vb) / (ma + mb);
            float newB = ((mb - restitution * ma) * vb + (1f + restitution) * ma * va) / (ma + mb);

            if (Math.Abs(newA) < 0.001f) newA = 0f;
            if (Math.Abs(newB) < 0.001f) newB = 0f;

            SetVelocityAxis(a, axis, newA);
            SetVelocityAxis(b, axis, newB);
        }

        private void MarkContact(Rigidbody rigidbody, int axis, int dir)
        {
            var normal = axis switch
            {
                0 => new Vector3(-dir, 0f, 0f),
                1 => new Vector3(0f, -dir, 0f),
                _ => new Vector3(0f, 0f, -dir)
            };

            rigidbody.LastCollisionNormal = normal;
            if (axis == 1 && dir < 0)
                rigidbody.IsGrounded = true;
        }

        private static bool AxisFrozen(Rigidbody rigidbody, int axis) => axis switch
        {
            0 => rigidbody.FreezePositionX,
            1 => rigidbody.FreezePositionY,
            _ => rigidbody.FreezePositionZ
        };

        private static float CombinedBounciness(Rigidbody rigidbody, Collider self, Collider other) =>
            Math.Clamp(Math.Max(Math.Max(rigidbody.Bounciness, self.Bounciness), other.Bounciness), 0f, 1f);

        // Bounds "estrecho" de un collider visto desde otro: para TerrainCollider, recorta el techo (Max.Y)
        // a la altura real del heightmap bajo el centro XZ de relativeTo, en lugar del AABB plano.
        private static Bounds GetNarrowBounds(Collider collider, Collider relativeTo)
        {
            if (collider is TerrainCollider terrain)
            {
                var bounds = collider.GetBounds();
                var otherBounds = relativeTo.GetBounds();
                float cx = (otherBounds.Min.X + otherBounds.Max.X) * 0.5f;
                float cz = (otherBounds.Min.Z + otherBounds.Max.Z) * 0.5f;
                float surfaceY = terrain.SampleHeight(cx, cz);
                return new Bounds(bounds.Min, new Vector3(bounds.Max.X, surfaceY, bounds.Max.Z));
            }

            // Mesh Collider con malla: el "techo" de la caja se recorta a la altura real de la superficie
            // bajo el otro objeto → permite rampas/pendientes (el personaje se apoya en la malla inclinada).
            if (collider is MeshCollider meshCol && meshCol.UseMeshBounds)
            {
                var bounds = collider.GetBounds();
                var otherBounds = relativeTo.GetBounds();
                float cx = (otherBounds.Min.X + otherBounds.Max.X) * 0.5f;
                float cz = (otherBounds.Min.Z + otherBounds.Max.Z) * 0.5f;
                if (meshCol.TrySampleHeight(cx, cz, out float surfaceY))
                    return new Bounds(bounds.Min, new Vector3(bounds.Max.X, surfaceY, bounds.Max.Z));
                return bounds;
            }

            return collider.GetBounds();
        }

        private static float AxisOverlap(Bounds a, Bounds b, int axis, int dir) =>
            dir > 0
                ? GetAxis(a.Max, axis) - GetAxis(b.Min, axis)
                : GetAxis(b.Max, axis) - GetAxis(a.Min, axis);

        private static float AxisCenter(Bounds bounds, int axis) =>
            (GetAxis(bounds.Min, axis) + GetAxis(bounds.Max, axis)) * 0.5f;

        private static float GetWorldAxis(GameObject obj, int axis) => GetAxis(obj.Position, axis);

        private static void SetWorldAxis(GameObject obj, int axis, float value)
        {
            var p = obj.Position;
            if (axis == 0) p.X = value;
            else if (axis == 1) p.Y = value;
            else p.Z = value;
            obj.Position = p;
        }

        private static float GetVelocityAxis(Rigidbody rigidbody, int axis) => GetAxis(rigidbody.Velocity, axis);

        private static void SetVelocityAxis(Rigidbody rigidbody, int axis, float value)
        {
            if (axis == 0) rigidbody.Velocity.X = value;
            else if (axis == 1) rigidbody.Velocity.Y = value;
            else rigidbody.Velocity.Z = value;
        }

        private static float GetAxis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        private static Vector3 Add(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        private static Vector3 Sub(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        private static Vector3 Scale(Vector3 v, float scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);

        public void ResolveCollision(Rigidbody rigidbody, BoxCollider myCollider, ref float posY) =>
            ResolveCollision(rigidbody, myCollider, ref posY, ref rigidbody.Velocity.Y, myCollider.gameObject.PosY);

        public void ResolveCollision(Rigidbody rigidbody, BoxCollider myCollider, ref float posY, ref float velY) =>
            ResolveCollision(rigidbody, myCollider, ref posY, ref velY, myCollider.gameObject.PosY);

        public void ResolveCollision(Rigidbody rigidbody, BoxCollider myCollider, ref float posY, ref float velY, float previousPosY)
        {
            if (velY > 0f || myCollider.IsTrigger)
                return;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                float highestMaxY = float.MinValue;
                float centerOffsetY = myCollider.Center.Y * myCollider.gameObject.ScaleY;
                float halfHeight = Math.Max(0.0001f, Math.Abs(myCollider.Size.Y * myCollider.gameObject.ScaleY)) / 2f;
                bool anyOverlap = false;

                myCollider.gameObject.PosY = posY;

                var myBounds = myCollider.GetBounds();

                foreach (var otherCollider in QueryNearby(myCollider))
                {
                    if (otherCollider.IsTrigger)
                        continue;

                    var otherBounds = otherCollider.GetBounds();

                    float previousCenterY = previousPosY + myCollider.Center.Y * myCollider.gameObject.ScaleY;
                    float previousBottom = previousCenterY - halfHeight;
                    bool wasAbove = previousBottom >= otherBounds.Max.Y - 0.001f;

                    if (myBounds.Intersects(otherBounds) && wasAbove)
                    {
                        anyOverlap = true;

                        if (otherBounds.Max.Y > highestMaxY)
                            highestMaxY = otherBounds.Max.Y;
                    }
                }

                if (!anyOverlap)
                    break;

                posY = highestMaxY + halfHeight - centerOffsetY;
                myCollider.gameObject.PosY = posY;
                velY = 0f;
            }
        }

        // Fracción del solapamiento que se corrige por frame (evita "teletransporte" del empuje).
        private const float PushCorrectionFactor = 0.2f;

        public bool CheckCollisionX(Rigidbody rigidbody, BoxCollider myCollider, float newPosX)
        {
            if (myCollider.IsTrigger)
                return false;

            float savedX = myCollider.gameObject.PosX;
            myCollider.gameObject.PosX = newPosX;

            var myBounds = myCollider.GetBounds();
            float dir = newPosX - savedX;
            bool blockedByStatic = false;

            foreach (var otherCollider in QueryNearby(myCollider))
            {
                if (otherCollider.IsTrigger)
                    continue;

                if (!myBounds.Intersects(otherCollider.GetBounds()))
                    continue;

                var otherRb = otherCollider.gameObject.GetComponent<Rigidbody>();

                if (otherRb != null && !otherRb.IsKinematic && !otherRb.FreezePositionX)
                {
                    var otherBounds = otherCollider.GetBounds();
                    float overlap = dir > 0
                        ? myBounds.Max.X - otherBounds.Min.X
                        : myBounds.Min.X - otherBounds.Max.X;

                    float myMass = Math.Max(0.0001f, rigidbody.Mass);
                    float otherMass = Math.Max(0.0001f, otherRb.Mass);
                    float total = myMass + otherMass;

                    // Transferencia de velocidad (empuje real, suave por inercia).
                    float impulse = rigidbody.Velocity.X * (myMass / total);
                    if (dir > 0)
                        otherRb.Velocity.X = Math.Max(otherRb.Velocity.X, impulse);
                    else
                        otherRb.Velocity.X = Math.Min(otherRb.Velocity.X, impulse);

                    rigidbody.Velocity.X *= (otherMass / total);

                    // Corrección posicional gradual para evitar penetración.
                    float correction = overlap * PushCorrectionFactor;
                    otherCollider.gameObject.PosX += correction * (myMass / total);
                    myCollider.gameObject.PosX -= correction * (otherMass / total);
                    myBounds = myCollider.GetBounds();

                    spatialHashDirty = true;
                    BepuBackend.MarkDirty();
                }
                else
                {
                    blockedByStatic = true;
                }
            }

            if (blockedByStatic)
            {
                myCollider.gameObject.PosX = savedX;
                return true;
            }

            return false;
        }

        public bool CheckCollisionZ(Rigidbody rigidbody, BoxCollider myCollider, float newPosZ)
        {
            if (myCollider.IsTrigger)
                return false;

            float savedZ = myCollider.gameObject.PosZ;
            myCollider.gameObject.PosZ = newPosZ;

            var myBounds = myCollider.GetBounds();
            float dir = newPosZ - savedZ;
            bool blockedByStatic = false;

            foreach (var otherCollider in QueryNearby(myCollider))
            {
                if (otherCollider.IsTrigger)
                    continue;

                if (!myBounds.Intersects(otherCollider.GetBounds()))
                    continue;

                var otherRb = otherCollider.gameObject.GetComponent<Rigidbody>();

                if (otherRb != null && !otherRb.IsKinematic && !otherRb.FreezePositionZ)
                {
                    var otherBounds = otherCollider.GetBounds();
                    float overlap = dir > 0
                        ? myBounds.Max.Z - otherBounds.Min.Z
                        : myBounds.Min.Z - otherBounds.Max.Z;

                    float myMass = Math.Max(0.0001f, rigidbody.Mass);
                    float otherMass = Math.Max(0.0001f, otherRb.Mass);
                    float total = myMass + otherMass;

                    float impulse = rigidbody.Velocity.Z * (myMass / total);
                    if (dir > 0)
                        otherRb.Velocity.Z = Math.Max(otherRb.Velocity.Z, impulse);
                    else
                        otherRb.Velocity.Z = Math.Min(otherRb.Velocity.Z, impulse);

                    rigidbody.Velocity.Z *= (otherMass / total);

                    float correction = overlap * PushCorrectionFactor;
                    otherCollider.gameObject.PosZ += correction * (myMass / total);
                    myCollider.gameObject.PosZ -= correction * (otherMass / total);
                    myBounds = myCollider.GetBounds();

                    spatialHashDirty = true;
                    BepuBackend.MarkDirty();
                }
                else
                {
                    blockedByStatic = true;
                }
            }

            if (blockedByStatic)
            {
                myCollider.gameObject.PosZ = savedZ;
                return true;
            }

            return false;
        }

        public List<Collider> QueryBounds(Bounds bounds)
        {
            EnsureSpatialHash();

            querySet.Clear();

            var min = CellOf(bounds.Min);
            var max = CellOf(bounds.Max);

            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                    {
                        if (spatialHash.TryGetValue((x, y, z), out var list))
                        {
                            foreach (var collider in list)
                                querySet.Add(collider);
                        }
                    }

            foreach (var collider in largeColliders)
                querySet.Add(collider);

            var result = new List<Collider>();

            foreach (var collider in querySet)
            {
                if (collider.GetBounds().Intersects(bounds))
                    result.Add(collider);
            }

            return result;
        }

        public List<Collider> OverlapBox(Vector3 center, Vector3 size, bool includeTriggers = true)
        {
            var half = new Vector3(
                Math.Max(0.0001f, Math.Abs(size.X)) * 0.5f,
                Math.Max(0.0001f, Math.Abs(size.Y)) * 0.5f,
                Math.Max(0.0001f, Math.Abs(size.Z)) * 0.5f);
            var bounds = new Bounds(new Vector3(center.X - half.X, center.Y - half.Y, center.Z - half.Z),
                                    new Vector3(center.X + half.X, center.Y + half.Y, center.Z + half.Z));
            var hits = QueryBounds(bounds);
            if (includeTriggers)
                return hits;

            return hits.FindAll(c => !c.IsTrigger);
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, bool includeTriggers = false)
        {
            return Raycast(origin, direction, maxDistance, out hit, LayerMask.Everything, includeTriggers);
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, int layerMask, bool includeTriggers = false)
        {
            hit = default;

            // Con BEPU listo usamos raycast preciso contra shapes reales. Si BEPU todavía
            // no fue construido (por ejemplo tras SyncPhysicsComponents en tests/editor),
            // caemos al broadphase legacy para que Physics.Raycast siga funcionando.
            bool bepuHit = false;
            PhysicsRaycastHit bepuRayHit = default;
            if (BepuBackend.Enabled && BepuBackend.IsReady)
                bepuHit = BepuBackend.TryRaycast(origin, direction, maxDistance, includeTriggers, layerMask, out bepuRayHit);

            LastRaycastCandidateCount = 0;
            float length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
            if (length <= 0.000001f || maxDistance <= 0f)
                return false;

            var dir = new Vector3(direction.X / length, direction.Y / length, direction.Z / length);
            float bestDistance = maxDistance;
            bool found = false;
            var candidates = BroadphaseRaycast
                ? QueryRayCandidates(origin, dir, maxDistance)
                : new List<Collider>(colliders);
            LastRaycastCandidateCount = candidates.Count;

            foreach (var collider in candidates)
            {
                if (!IsColliderAvailable(collider))
                    continue;
                if (!includeTriggers && collider.IsTrigger)
                    continue;
                if (!LayerMask.Contains(layerMask, collider.gameObject.Layer))
                    continue;

                if (RayIntersectsBounds(origin, dir, collider.GetBounds(), maxDistance, out float distance, out var normal) &&
                    distance <= bestDistance)
                {
                    bestDistance = distance;
                    var point = new Vector3(
                        origin.X + dir.X * distance,
                        origin.Y + dir.Y * distance,
                        origin.Z + dir.Z * distance);
                    hit = new PhysicsRaycastHit(collider, point, normal, distance);
                    found = true;
                }
            }

            if (bepuHit && (!found || bepuRayHit.Distance <= bestDistance))
            {
                hit = bepuRayHit;
                return true;
            }

            return found;
        }

        private List<Collider> QueryRayCandidates(Vector3 origin, Vector3 direction, float maxDistance)
        {
            const float padding = 0.001f;
            var end = new Vector3(
                origin.X + direction.X * maxDistance,
                origin.Y + direction.Y * maxDistance,
                origin.Z + direction.Z * maxDistance);

            var min = new Vector3(
                MathF.Min(origin.X, end.X) - padding,
                MathF.Min(origin.Y, end.Y) - padding,
                MathF.Min(origin.Z, end.Z) - padding);
            var max = new Vector3(
                MathF.Max(origin.X, end.X) + padding,
                MathF.Max(origin.Y, end.Y) + padding,
                MathF.Max(origin.Z, end.Z) + padding);

            return QueryBounds(new Bounds(min, max));
        }

        private static bool RayIntersectsBounds(Vector3 origin, Vector3 direction, Bounds bounds, float maxDistance, out float distance, out Vector3 normal)
        {
            float tMin = 0f;
            float tMax = maxDistance;
            normal = Vector3.Zero;

            if (!RaySlab(origin.X, direction.X, bounds.Min.X, bounds.Max.X, new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), ref tMin, ref tMax, ref normal)) { distance = 0f; return false; }
            if (!RaySlab(origin.Y, direction.Y, bounds.Min.Y, bounds.Max.Y, new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), ref tMin, ref tMax, ref normal)) { distance = 0f; return false; }
            if (!RaySlab(origin.Z, direction.Z, bounds.Min.Z, bounds.Max.Z, new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), ref tMin, ref tMax, ref normal)) { distance = 0f; return false; }

            distance = tMin;
            if (distance <= 0.000001f)
                normal = new Vector3(-direction.X, -direction.Y, -direction.Z);
            return tMax >= 0f && distance <= maxDistance;
        }

        private static bool RaySlab(float origin, float direction, float min, float max, Vector3 minNormal, Vector3 maxNormal, ref float tMin, ref float tMax, ref Vector3 normal)
        {
            if (Math.Abs(direction) <= 0.000001f)
                return origin >= min && origin <= max;

            float inv = 1f / direction;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            var nearNormal = minNormal;

            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                nearNormal = maxNormal;
            }

            if (t1 > tMin)
            {
                tMin = t1;
                normal = nearNormal;
            }

            tMax = Math.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
