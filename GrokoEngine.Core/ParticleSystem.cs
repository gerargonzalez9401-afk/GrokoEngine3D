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
    // PARTICLE SYSTEM
    // =====================================================
    public enum ParticleShape { Sphere, Cone, Box, Circle }
    public enum ParticleSimulationSpace { Local, World }
    public enum ParticleStopAction { None, Disable, Destroy }
    public enum ParticleBlendMode { Alpha, Additive, Multiply }
    public enum ParticleRenderMode { Billboard, StretchedBillboard, HorizontalBillboard, VerticalBillboard }
    public enum ParticleSortMode { None, Distance, OldestInFront, YoungestInFront }
    public enum ParticleScalingMode { Hierarchy, Local, Shape }
    public enum ParticleValueMode { Constant, RandomBetweenTwoConstants, Curve, RandomBetweenTwoCurves }

    public struct BurstEvent
    {
        public float Time;
        public int Count;
        public int Cycles;      // 0 = infinito
        public float Interval;    // segundos entre ciclos
        // estado interno
        internal int _cyclesDone;
        internal float _nextTime;
    }

    // Punto de un trail
    public struct TrailPoint
    {
        public Vector3 Position;
        public float Age;
        public float MaxAge;
    }

    public struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age;
        public float Lifetime;
        public float SizeStart;
        public float SizeEnd;
        public float ColorStartR, ColorStartG, ColorStartB, ColorStartA;
        public float ColorEndR, ColorEndG, ColorEndB, ColorEndA;
        public float RotationSpeed;
        public float Rotation;

        // ID único por partícula (para trails)
        public int Id;
        // Índice de ruido único por partícula (para turbulencia no correlacionada)
        public float NoiseSeed;

        public float NormalizedAge => Lifetime > 0f ? Math.Clamp(Age / Lifetime, 0f, 1f) : 1f;
        public float CurrentSize => SizeStart + (SizeEnd - SizeStart) * NormalizedAge;
        public float CurrentR => ColorStartR + (ColorEndR - ColorStartR) * NormalizedAge;
        public float CurrentG => ColorStartG + (ColorEndG - ColorStartG) * NormalizedAge;
        public float CurrentB => ColorStartB + (ColorEndB - ColorStartB) * NormalizedAge;
        public float CurrentA => ColorStartA + (ColorEndA - ColorStartA) * NormalizedAge;
    }

    // =====================================================
    // PARTICLE MODULES
    // =====================================================
    // Estos módulos separan datos por responsabilidad, estilo Unity,
    // sin romper los campos públicos existentes de ParticleSystem.
    public sealed class ParticleMainModule
    {
        public bool Enabled = true;
        public float Duration = 5f;
        public bool Looping = true;
        public float StartDelay = 0f;
        public float SimulationSpeed = 1f;
        public int MaxParticles = 500;
        public bool PlayOnAwake = true;
        public bool Prewarm = false;
    }

    public sealed class ParticleEmissionModule
    {
        public bool Enabled = true;
        public float RateOverTime = 20f;
        public bool BurstEnabled = false;
        public int BurstCount = 30;
        public float BurstTime = 0f;
        public float BurstProbability = 1f;
        public bool RateOverDistanceEnabled = false;
        public float RateOverDistance = 5f;
        public List<BurstEvent> ExtraBursts = new();
    }

    public sealed class ParticleShapeModule
    {
        public bool Enabled = true;
        public ParticleShape Shape = ParticleShape.Cone;
        public float Radius = 0.1f;
        public float Angle = 25f;
        public float Arc = 360f;
        public float RadiusThickness = 1f;
        public bool EmitFromShell = false;
        public float RandomDirectionAmount = 0f;
        public float BoxSizeX = 1f;
        public float BoxSizeY = 1f;
        public float BoxSizeZ = 1f;
    }

    public sealed class ParticleColorModule
    {
        public bool Enabled = true;
        public int KeyCount = 2;
        public float CK1T = 0f, CK1R = 1f, CK1G = 0.5f, CK1B = 0.1f, CK1A = 1f;
        public float CK2T = 1f, CK2R = 0.8f, CK2G = 0.05f, CK2B = 0f, CK2A = 0f;
        public float CK3T = 0.5f, CK3R = 1f, CK3G = 0.2f, CK3B = 0f, CK3A = 0.8f;
        public float CK4T = 0.75f, CK4R = 0.5f, CK4G = 0.1f, CK4B = 0f, CK4A = 0.3f;
    }

    public sealed class ParticleNoiseModule
    {
        public bool Enabled = false;
        public float Strength = 0f;
        public float Frequency = 1f;
    }

    /// <summary>Calidad de colisión de partículas. Fast = AABB barato (solo cajas). High = raycast
    /// de barrido contra BEPU (todas las formas, sin tunneling, normal real), más caro por partícula.</summary>
    public enum ParticleCollisionQuality { Fast, High }

    public sealed class ParticleCollisionModule
    {
        public bool Enabled = false;
        public float Bounciness = 0.3f;
        public float Dampen = 0.5f;
        public ParticleCollisionQuality Quality = ParticleCollisionQuality.Fast;
    }

    public class ParticleSystem : MonoBehaviour
    {
        public readonly ParticleMainModule Main = new();
        public readonly ParticleEmissionModule Emission = new();
        public readonly ParticleShapeModule ShapeModule = new();
        public readonly ParticleColorModule ColorModule = new();
        public readonly ParticleNoiseModule NoiseModule = new();
        public readonly ParticleCollisionModule Collision = new();

        // ── Emisión ──────────────────────────────────────────────
        public float EmitRate = 20f;
        public bool Looping = true;
        public float Duration = 5f;
        public float StartDelay = 0f;
        public float SimulationSpeed = 1f;
        public int MaxParticles = 500;
        public bool PlayOnAwake = true;
        public bool Prewarm = false;
        public bool AutoRandomSeed = true;
        public int RandomSeed = 1;
        public ParticleSimulationSpace SimulationSpace = ParticleSimulationSpace.World;
        public ParticleScalingMode ScalingMode = ParticleScalingMode.Local;

        public bool MainModuleEnabled = true;
        public bool EmissionModuleEnabled = true;
        public bool ShapeModuleEnabled = true;
        public bool VelocityOverLifetimeModuleEnabled = false;
        public bool ForceOverLifetimeModuleEnabled = false;
        public bool LimitVelocityOverLifetimeModuleEnabled = false;
        public bool ColorOverLifetimeModuleEnabled = true;
        public bool SizeOverLifetimeModuleEnabled = true;
        public bool RotationOverLifetimeModuleEnabled = true;
        public bool NoiseModuleEnabled = false;
        public bool TextureSheetAnimationModuleEnabled = false;
        public bool TrailsModuleEnabled = false;
        public bool CollisionModuleEnabled = false;
        public bool RendererModuleEnabled = true;

        // ── Burst ────────────────────────────────────────────────
        public bool BurstEnabled = false;
        public int BurstCount = 30;
        public float BurstTime = 0f;
        public float BurstProbability = 1f;

        // ── Vida de partícula ────────────────────────────────────
        public ParticleValueMode LifetimeMode = ParticleValueMode.RandomBetweenTwoConstants;
        public ParticleValueMode SpeedMode = ParticleValueMode.RandomBetweenTwoConstants;
        public ParticleValueMode SizeMode = ParticleValueMode.Curve;
        public ParticleValueMode GravityMode = ParticleValueMode.Constant;
        public float LifetimeMin = 1.0f, LifetimeMax = 2.0f;
        public float SpeedMin = 1.5f, SpeedMax = 4.0f;
        public float SizeStart = 0.15f, SizeEnd = 0.0f;
        public bool StartSize3D = false;
        public float SizeStartX = 0.15f, SizeStartY = 0.15f, SizeStartZ = 0.15f;
        public float SizeEndX = 0.0f, SizeEndY = 0.0f, SizeEndZ = 0.0f;
        public float LifetimeCurveMid = 0.5f;
        public float LifetimeCurveMidValue = 1f;
        public float SpeedCurveMid = 0.5f;
        public float SpeedCurveMidValue = 1f;
        public float StartSizeCurveMid = 0.5f;
        public float StartSizeCurveMidValue = 1f;
        public float SizeCurveMid = 0.5f;
        public float SizeCurveMidValue = 0.65f;
        public float GravityScale = 0f;
        public float GravityCurveMid = 0.5f;
        public float GravityCurveMidValue = 1f;

        // ── Color over lifetime ──────────────────────────────────
        public float ColorStartR = 1f, ColorStartG = 0.5f, ColorStartB = 0.1f, ColorStartA = 1.0f;
        public float ColorEndR = 1f, ColorEndG = 0.1f, ColorEndB = 0.0f, ColorEndA = 0.0f;

        // ── Forma de emisión ─────────────────────────────────────
        public ParticleShape Shape = ParticleShape.Cone;
        public float ShapeRadius = 0.1f;
        public float ShapeAngle = 25f;
        public float ShapeArc = 360f;
        public float ShapeRadiusThickness = 1f;
        public bool ShapeEmitFromShell = false;
        public float ShapeRandomDirectionAmount = 0f;
        public float ShapeBoxSizeX = 1f, ShapeBoxSizeY = 1f, ShapeBoxSizeZ = 1f;

        // ── Textura ──────────────────────────────────────────────
        public string MaterialPath = "";
        public string TexturePath = "";

        // ── Texture Sheet Animation ───────────────────────────────
        /// <summary>Columnas del sprite sheet (1 = sin animación)</summary>
        public int SheetColumns = 1;
        /// <summary>Filas del sprite sheet (1 = sin animación)</summary>
        public int SheetRows = 1;
        /// <summary>Fotogramas por segundo de la animación</summary>
        public float SheetFrameRate = 8f;

        // ── Velocidad over lifetime ───────────────────────────────
        /// <summary>Fuerza adicional aplicada en X a lo largo de la vida</summary>
        public float VelOverLifeX = 0f;
        /// <summary>Fuerza adicional aplicada en Y a lo largo de la vida</summary>
        public float VelOverLifeY = 0f;
        /// <summary>Fuerza adicional aplicada en Z a lo largo de la vida</summary>
        public float VelOverLifeZ = 0f;

        // ── Force / Limit Velocity over lifetime ───────────────────
        public float ForceOverLifeX = 0f;
        public float ForceOverLifeY = 0f;
        public float ForceOverLifeZ = 0f;
        public float LimitVelocity = 10f;
        public float LimitVelocityDampen = 0.5f;

        // ── Turbulencia ───────────────────────────────────────────
        public float TurbulenceStrength = 0f;
        public float TurbulenceFrequency = 1f;

        // ── Gradiente de color (hasta 4 paradas) ──────────────────
        public int ColorKeyCount = 2;    // 2–4 paradas activas
        public float CK1T = 0f, CK1R = 1f, CK1G = 0.5f, CK1B = 0.1f, CK1A = 1f;
        public float CK2T = 1f, CK2R = 0.8f, CK2G = 0.05f, CK2B = 0f, CK2A = 0f;
        public float CK3T = 0.5f, CK3R = 1f, CK3G = 0.2f, CK3B = 0f, CK3A = 0.8f;
        public float CK4T = 0.75f, CK4R = 0.5f, CK4G = 0.1f, CK4B = 0f, CK4A = 0.3f;

        // ── Stretched billboard ───────────────────────────────────
        /// <summary>Estira el quad en la dirección de movimiento (lluvia, misiles)</summary>
        public bool StretchedBillboard = false;
        public float StretchSpeedScale = 0.5f;   // multiplicador del estiramiento
        public float StretchLengthScale = 1f;
        public ParticleBlendMode BlendMode = ParticleBlendMode.Alpha;
        public ParticleRenderMode RenderMode = ParticleRenderMode.Billboard;
        public ParticleSortMode SortMode = ParticleSortMode.Distance;
        public bool SoftParticles = false;
        public float SoftParticleRange = 0.15f;
        public float HdrIntensity = 1f;
        public bool SortParticles = true;
        public int SortingFudge = 0;
        public bool AllowRoll = true;
        public bool FlipU = false;
        public bool FlipV = false;
        public float PivotX = 0f;
        public float PivotY = 0f;

        // ── Trails ────────────────────────────────────────────────
        public bool TrailEnabled = false;
        public float TrailLifetime = 0.3f;
        public float TrailWidthStart = 0.05f;
        public float TrailWidthEnd = 0f;

        // ── Colisión con BoxColliders ─────────────────────────────
        public bool CollisionEnabled = false;
        public float CollisionBounciness = 0.3f;
        public float CollisionDampen = 0.5f;
        public ParticleCollisionQuality CollisionQuality = ParticleCollisionQuality.Fast;

        // Switch simple y seguro para el Inspector.
        // Si activas esto, prende todos los flags necesarios para colisión.
        public bool ParticleCollision
        {
            get => CollisionModuleEnabled || CollisionEnabled || Collision.Enabled;
            set
            {
                CollisionModuleEnabled = value;
                CollisionEnabled = value;
                Collision.Enabled = value;
            }
        }

        // ── Sub-emisores ──────────────────────────────────────────
        /// <summary>EditorId del ParticleSystem que se activa al nacer cada partícula</summary>
        public string SubEmitterBirth = "";
        /// <summary>EditorId del ParticleSystem que se activa al morir cada partícula</summary>
        public string SubEmitterDeath = "";
        public int SubEmitterCount = 5;

        // ── Rate over distance ────────────────────────────────────
        public bool RateOverDistanceEnabled = false;
        public float RateOverDistance = 5f;

        // ── Múltiples bursts ──────────────────────────────────────
        public List<BurstEvent> ExtraBursts = new();

        // ── Stop Action ───────────────────────────────────────────
        public ParticleStopAction StopAction = ParticleStopAction.None;

        // ── Velocidad heredada del emisor ─────────────────────────
        public float InheritVelocity = 0f;

        // ── LOD ───────────────────────────────────────────────────
        public float LODDistance = 50f;
        public float LODDistanceMax = 100f;

        // ── Eventos internos (para sub-emisores) ──────────────────
        internal readonly List<Vector3> _birthPositions = new();
        internal readonly List<Vector3> _deathPositions = new();

        // Referencia al motor de física para colisiones de partículas
        public PhysicsEngine? Physics { get; set; }

        // ── Rotación de partícula ────────────────────────────────
        public float RotationSpeedMin = -90f, RotationSpeedMax = 90f;

        // ── Estado interno ────────────────────────────────────────
        private readonly List<Particle> _particles = new();
        private float _emitTimer;
        private float _distanceAccum;
        private Vector3 _lastPos;
        private Vector3 _lastEmitterVelocityPos;
        private Vector3 _emitterVelocity;
        private int _nextParticleId;
        private float _time;
        private bool _burstFired;
        private bool _playing;
        private bool _warnedMissingParticlePhysics;
        private Random _rng = new();

        public IReadOnlyList<Particle> Particles => _particles;
        public bool IsPlaying => _playing;
        public float Time => _time;

        public void Play()
        {
            _rng = AutoRandomSeed ? new Random() : new Random(RandomSeed);
            _playing = true;
            _time = 0f;
            _emitTimer = 0f;
            _distanceAccum = 0f;
            _lastPos = new Vector3(gameObject.PosX, gameObject.PosY, gameObject.PosZ);
            _lastEmitterVelocityPos = _lastPos;
            _emitterVelocity = Vector3.Zero;
            _burstFired = false;
            _pendingDestroy = false;
            _particles.Clear();
            if (Prewarm && Looping && Duration > 0f)
                Simulate(Math.Clamp(Duration, 0f, 30f), restart: false);
        }
        public void Stop() { _playing = false; _particles.Clear(); }
        public void Pause() { _playing = false; }
        public void Restart() { Stop(); Play(); }
        public void EmitOne() { if (_particles.Count < MaxParticles) Emit(); }
        public void Simulate(double seconds, bool restart)
        {
            if (restart) Play();
            bool wasPlaying = _playing;
            _playing = true;
            double remaining = Math.Max(0.0, seconds);
            while (remaining > 0.0)
            {
                double step = Math.Min(1.0 / 30.0, remaining);
                Update(step);
                remaining -= step;
            }
            _playing = wasPlaying;
        }

        public override void Update(double dt)
        {
            SyncLegacyFieldsToModules();

            if (!_playing) return;
            float fdt = (float)dt * Math.Max(0f, SimulationSpeed);
            if (fdt <= 0f) return;
            _time += fdt;
            var currentEmitterPos = new Vector3(gameObject!.PosX, gameObject.PosY, gameObject.PosZ);
            _emitterVelocity = new Vector3(
                (currentEmitterPos.X - _lastEmitterVelocityPos.X) / fdt,
                (currentEmitterPos.Y - _lastEmitterVelocityPos.Y) / fdt,
                (currentEmitterPos.Z - _lastEmitterVelocityPos.Z) / fdt);
            _lastEmitterVelocityPos = currentEmitterPos;

            // Actualizar partículas existentes
            bool hasVelLife = VelocityOverLifetimeModuleEnabled && (VelOverLifeX != 0f || VelOverLifeY != 0f || VelOverLifeZ != 0f);
            bool hasForceLife = ForceOverLifetimeModuleEnabled && (ForceOverLifeX != 0f || ForceOverLifeY != 0f || ForceOverLifeZ != 0f);
            bool limitVelocity = LimitVelocityOverLifetimeModuleEnabled && LimitVelocity > 0f;
            bool hasTurbulence = NoiseModuleEnabled && TurbulenceStrength > 0f;
            bool collisionRequested = CollisionModuleEnabled || CollisionEnabled || Collision.Enabled;
            bool hasCollision = collisionRequested && Physics != null;
            // Calidad alta: barrido (raycast) contra BEPU. Solo si BEPU esta listo; si no, cae a AABB.
            var collQuality = Collision.Enabled ? Collision.Quality : CollisionQuality;
            bool useBepuSweep = hasCollision && collQuality == ParticleCollisionQuality.High
                                && BepuBackend.Enabled && BepuBackend.IsReady;

            if (collisionRequested && Physics == null && !_warnedMissingParticlePhysics)
            {
                Debug.LogWarning($"ParticleSystem '{gameObject?.Name}' has collision enabled but Physics is null. Call physicsEngine.SyncPhysicsComponents(objects) after loading the scene or add the component with AddComponentWithEngine<ParticleSystem>(physicsEngine).");
                _warnedMissingParticlePhysics = true;
            }

            _deathPositions.Clear();
            _birthPositions.Clear();   // Bug fix: clear birth list each frame so sub-emitters don't replay stale births

            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += fdt;
                if (p.Age >= p.Lifetime)
                {
                    if (!string.IsNullOrEmpty(SubEmitterDeath))
                        _deathPositions.Add(p.Position);
                    _particles.RemoveAt(i);
                    continue;
                }

                // Velocidad over lifetime: fuerza que aumenta progresivamente
                if (hasVelLife)
                {
                    float t = p.NormalizedAge;
                    p.Velocity.X += VelOverLifeX * t * fdt;
                    p.Velocity.Y += VelOverLifeY * t * fdt;
                    p.Velocity.Z += VelOverLifeZ * t * fdt;
                }

                if (hasForceLife)
                {
                    p.Velocity.X += ForceOverLifeX * fdt;
                    p.Velocity.Y += ForceOverLifeY * fdt;
                    p.Velocity.Z += ForceOverLifeZ * fdt;
                }

                // Turbulencia: ruido por partícula basado en su seed y el tiempo
                if (hasTurbulence)
                {
                    float noiseT = p.NoiseSeed + _time * TurbulenceFrequency;
                    p.Velocity.X += MathF.Sin(noiseT * 1.3f) * TurbulenceStrength * fdt;
                    p.Velocity.Y += MathF.Sin(noiseT * 0.7f + 1.5f) * TurbulenceStrength * fdt;
                    p.Velocity.Z += MathF.Sin(noiseT * 1.7f + 3.1f) * TurbulenceStrength * fdt;
                }

                float gravityCurve = GravityMode is ParticleValueMode.Curve or ParticleValueMode.RandomBetweenTwoCurves
                    ? EvaluateSimpleCurve(p.NormalizedAge, GravityCurveMid, GravityCurveMidValue)
                    : 1f;
                float gravity = GravityScale * gravityCurve;
                p.Velocity.Y -= gravity * 9.81f * fdt;

                if (limitVelocity)
                {
                    float speedSq = p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y + p.Velocity.Z * p.Velocity.Z;
                    float maxSpeed = Math.Max(0.001f, LimitVelocity);
                    if (speedSq > maxSpeed * maxSpeed)
                    {
                        float speed = MathF.Sqrt(speedSq);
                        float damp = Math.Clamp(LimitVelocityDampen, 0f, 1f);
                        float newSpeed = Lerp(speed, maxSpeed, damp);
                        float scale = newSpeed / speed;
                        p.Velocity.X *= scale;
                        p.Velocity.Y *= scale;
                        p.Velocity.Z *= scale;
                    }
                }

                float prevX = p.Position.X, prevY = p.Position.Y, prevZ = p.Position.Z;
                p.Position.X += p.Velocity.X * fdt;
                p.Position.Y += p.Velocity.Y * fdt;
                p.Position.Z += p.Velocity.Z * fdt;
                p.Rotation += p.RotationSpeed * fdt;

                // ── Colisión: High = barrido a BEPU (preciso); Fast = AABB barato ──
                if (useBepuSweep)
                {
                    // Raycast de la posicion anterior a la nueva contra las shapes REALES de BEPU:
                    // todas las formas (esfera/capsula/malla/terreno), sin tunneling, normal real.
                    float dvx = p.Position.X - prevX, dvy = p.Position.Y - prevY, dvz = p.Position.Z - prevZ;
                    float dist = MathF.Sqrt(dvx * dvx + dvy * dvy + dvz * dvz);
                    if (dist > 1e-5f &&
                        BepuBackend.TryRaycast(new Vector3(prevX, prevY, prevZ), new Vector3(dvx, dvy, dvz),
                            dist, false, LayerMask.Everything, out var phit))
                    {
                        float bounce = Collision.Enabled ? Collision.Bounciness : CollisionBounciness;
                        float dampen = Collision.Enabled ? Collision.Dampen : CollisionDampen;
                        var n = phit.Normal;
                        // Reposicionar justo en el impacto, un pelin sobre la superficie.
                        p.Position.X = phit.Point.X + n.X * 0.01f;
                        p.Position.Y = phit.Point.Y + n.Y * 0.01f;
                        p.Position.Z = phit.Point.Z + n.Z * 0.01f;
                        // Reflejar: componente NORMAL rebota (bounce), componente TANGENCIAL amortigua (dampen).
                        float vn = p.Velocity.X * n.X + p.Velocity.Y * n.Y + p.Velocity.Z * n.Z;
                        float tx = p.Velocity.X - vn * n.X, ty = p.Velocity.Y - vn * n.Y, tz = p.Velocity.Z - vn * n.Z;
                        p.Velocity.X = tx * dampen - bounce * vn * n.X;
                        p.Velocity.Y = ty * dampen - bounce * vn * n.Y;
                        p.Velocity.Z = tz * dampen - bounce * vn * n.Z;
                    }
                }
                else if (hasCollision)
                {
                    var particleBounds = new Bounds(
                        new Vector3(p.Position.X - 0.001f, p.Position.Y - 0.001f, p.Position.Z - 0.001f),
                        new Vector3(p.Position.X + 0.001f, p.Position.Y + 0.001f, p.Position.Z + 0.001f));

                    foreach (var col in Physics!.QueryBounds(particleBounds))
                    {
                        var b = col.GetBounds();

                        if (p.Position.X >= b.Min.X && p.Position.X <= b.Max.X &&
                            p.Position.Y >= b.Min.Y && p.Position.Y <= b.Max.Y &&
                            p.Position.Z >= b.Min.Z && p.Position.Z <= b.Max.Z)
                        {
                            float bounce = Collision.Enabled ? Collision.Bounciness : CollisionBounciness;
                            float dampen = Collision.Enabled ? Collision.Dampen : CollisionDampen;

                            // Resolver por la cara MAS CERCANA (menor penetracion), no siempre en Y:
                            // una particula que choca de lado se empuja por ese lado, no hacia arriba.
                            float penXmin = p.Position.X - b.Min.X, penXmax = b.Max.X - p.Position.X;
                            float penYmin = p.Position.Y - b.Min.Y, penYmax = b.Max.Y - p.Position.Y;
                            float penZmin = p.Position.Z - b.Min.Z, penZmax = b.Max.Z - p.Position.Z;
                            float penX = MathF.Min(penXmin, penXmax);
                            float penY = MathF.Min(penYmin, penYmax);
                            float penZ = MathF.Min(penZmin, penZmax);

                            if (penX <= penY && penX <= penZ)
                            {
                                if (penXmax < penXmin) { p.Position.X = b.Max.X + 0.001f; p.Velocity.X = MathF.Abs(p.Velocity.X) * bounce; }
                                else                   { p.Position.X = b.Min.X - 0.001f; p.Velocity.X = -MathF.Abs(p.Velocity.X) * bounce; }
                                p.Velocity.Y *= dampen; p.Velocity.Z *= dampen;
                            }
                            else if (penY <= penZ)
                            {
                                if (penYmax < penYmin) { p.Position.Y = b.Max.Y + 0.001f; p.Velocity.Y = MathF.Abs(p.Velocity.Y) * bounce; }
                                else                   { p.Position.Y = b.Min.Y - 0.001f; p.Velocity.Y = -MathF.Abs(p.Velocity.Y) * bounce; }
                                p.Velocity.X *= dampen; p.Velocity.Z *= dampen;
                            }
                            else
                            {
                                if (penZmax < penZmin) { p.Position.Z = b.Max.Z + 0.001f; p.Velocity.Z = MathF.Abs(p.Velocity.Z) * bounce; }
                                else                   { p.Position.Z = b.Min.Z - 0.001f; p.Velocity.Z = -MathF.Abs(p.Velocity.Z) * bounce; }
                                p.Velocity.X *= dampen; p.Velocity.Y *= dampen;
                            }
                            break;
                        }
                    }
                }

                _particles[i] = p;
            }

            // ── Emitir nuevas (tasa continua) ─────────────────────
            bool canEmit = _time >= StartDelay;
            float localTime = Math.Max(0f, _time - StartDelay);
            float interval = EmitRate > 0f ? 1f / EmitRate : float.MaxValue;
            if (canEmit && EmissionModuleEnabled)
            {
                _emitTimer += fdt;
                while (_emitTimer >= interval && _particles.Count < MaxParticles)
                {
                    Emit();
                    _emitTimer -= interval;
                }
            }

            // ── Rate over distance ─────────────────────────────────
            if (canEmit && EmissionModuleEnabled && RateOverDistanceEnabled)
            {
                var curPos = currentEmitterPos;
                float dx = curPos.X - _lastPos.X, dy = curPos.Y - _lastPos.Y, dz = curPos.Z - _lastPos.Z;
                float moved = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                _distanceAccum += moved;
                float distInterval = RateOverDistance > 0f ? 1f / RateOverDistance : float.MaxValue;
                while (_distanceAccum >= distInterval && _particles.Count < MaxParticles)
                {
                    Emit();
                    _distanceAccum -= distInterval;
                }
                _lastPos = curPos;
            }

            // ── Burst principal ────────────────────────────────────
            if (canEmit && EmissionModuleEnabled && BurstEnabled && !_burstFired && localTime >= BurstTime)
            {
                if (_rng.NextDouble() <= Math.Clamp(BurstProbability, 0f, 1f))
                    for (int i = 0; i < BurstCount && _particles.Count < MaxParticles; i++)
                        Emit();
                _burstFired = true;
            }

            // ── Bursts adicionales ─────────────────────────────────
            for (int b = 0; b < ExtraBursts.Count; b++)
            {
                var burst = ExtraBursts[b];
                // Bug fix: _nextTime defaults to 0 for newly-added bursts; initialise it
                // from burst.Time so the first fire respects the scheduled time.
                if (burst._cyclesDone == 0 && burst._nextTime == 0f)
                    burst._nextTime = burst.Time;
                if (canEmit && EmissionModuleEnabled && localTime >= burst._nextTime)
                {
                    for (int i = 0; i < burst.Count && _particles.Count < MaxParticles; i++)
                        Emit();
                    burst._cyclesDone++;
                    if (burst.Cycles == 0 || burst._cyclesDone < burst.Cycles)
                        burst._nextTime += burst.Interval > 0f ? burst.Interval : Duration;
                    else
                        burst._nextTime = float.MaxValue;
                    ExtraBursts[b] = burst;
                }
            }

            // ── Loop / Stop ────────────────────────────────────────
            if (!Looping && _time >= Duration && _particles.Count == 0)
            {
                _playing = false;
                switch (StopAction)
                {
                    case ParticleStopAction.Disable:
                        // El editor puede detectar IsPlaying == false y desactivar el objeto
                        break;
                    case ParticleStopAction.Destroy:
                        // Marcar para destrucción — el editor lo maneja
                        _pendingDestroy = true;
                        break;
                }
            }
        }

        internal bool _pendingDestroy = false;

        public void SyncLegacyFieldsToModules()
        {
            Main.Enabled = MainModuleEnabled;
            Main.Duration = Duration;
            Main.Looping = Looping;
            Main.StartDelay = StartDelay;
            Main.SimulationSpeed = SimulationSpeed;
            Main.MaxParticles = MaxParticles;
            Main.PlayOnAwake = PlayOnAwake;
            Main.Prewarm = Prewarm;

            Emission.Enabled = EmissionModuleEnabled;
            Emission.RateOverTime = EmitRate;
            Emission.BurstEnabled = BurstEnabled;
            Emission.BurstCount = BurstCount;
            Emission.BurstTime = BurstTime;
            Emission.BurstProbability = BurstProbability;
            Emission.RateOverDistanceEnabled = RateOverDistanceEnabled;
            Emission.RateOverDistance = RateOverDistance;

            ShapeModule.Enabled = ShapeModuleEnabled;
            ShapeModule.Shape = Shape;
            ShapeModule.Radius = ShapeRadius;
            ShapeModule.Angle = ShapeAngle;
            ShapeModule.Arc = ShapeArc;
            ShapeModule.RadiusThickness = ShapeRadiusThickness;
            ShapeModule.EmitFromShell = ShapeEmitFromShell;
            ShapeModule.RandomDirectionAmount = ShapeRandomDirectionAmount;
            ShapeModule.BoxSizeX = ShapeBoxSizeX;
            ShapeModule.BoxSizeY = ShapeBoxSizeY;
            ShapeModule.BoxSizeZ = ShapeBoxSizeZ;

            ColorModule.Enabled = ColorOverLifetimeModuleEnabled;
            ColorModule.KeyCount = ColorKeyCount;
            ColorModule.CK1T = CK1T; ColorModule.CK1R = CK1R; ColorModule.CK1G = CK1G; ColorModule.CK1B = CK1B; ColorModule.CK1A = CK1A;
            ColorModule.CK2T = CK2T; ColorModule.CK2R = CK2R; ColorModule.CK2G = CK2G; ColorModule.CK2B = CK2B; ColorModule.CK2A = CK2A;
            ColorModule.CK3T = CK3T; ColorModule.CK3R = CK3R; ColorModule.CK3G = CK3G; ColorModule.CK3B = CK3B; ColorModule.CK3A = CK3A;
            ColorModule.CK4T = CK4T; ColorModule.CK4R = CK4R; ColorModule.CK4G = CK4G; ColorModule.CK4B = CK4B; ColorModule.CK4A = CK4A;

            NoiseModule.Enabled = NoiseModuleEnabled;
            NoiseModule.Strength = TurbulenceStrength;
            NoiseModule.Frequency = TurbulenceFrequency;

            bool collisionOn = CollisionModuleEnabled || CollisionEnabled || Collision.Enabled;
            CollisionModuleEnabled = collisionOn;
            CollisionEnabled = collisionOn;
            Collision.Enabled = collisionOn;
            Collision.Bounciness = CollisionBounciness;
            Collision.Dampen = CollisionDampen;
            Collision.Quality = CollisionQuality;
        }

        public void SyncModulesToLegacyFields()
        {
            MainModuleEnabled = Main.Enabled;
            Duration = Main.Duration;
            Looping = Main.Looping;
            StartDelay = Main.StartDelay;
            SimulationSpeed = Main.SimulationSpeed;
            MaxParticles = Main.MaxParticles;
            PlayOnAwake = Main.PlayOnAwake;
            Prewarm = Main.Prewarm;

            EmissionModuleEnabled = Emission.Enabled;
            EmitRate = Emission.RateOverTime;
            BurstEnabled = Emission.BurstEnabled;
            BurstCount = Emission.BurstCount;
            BurstTime = Emission.BurstTime;
            BurstProbability = Emission.BurstProbability;
            RateOverDistanceEnabled = Emission.RateOverDistanceEnabled;
            RateOverDistance = Emission.RateOverDistance;

            ShapeModuleEnabled = ShapeModule.Enabled;
            Shape = ShapeModule.Shape;
            ShapeRadius = ShapeModule.Radius;
            ShapeAngle = ShapeModule.Angle;
            ShapeArc = ShapeModule.Arc;
            ShapeRadiusThickness = ShapeModule.RadiusThickness;
            ShapeEmitFromShell = ShapeModule.EmitFromShell;
            ShapeRandomDirectionAmount = ShapeModule.RandomDirectionAmount;
            ShapeBoxSizeX = ShapeModule.BoxSizeX;
            ShapeBoxSizeY = ShapeModule.BoxSizeY;
            ShapeBoxSizeZ = ShapeModule.BoxSizeZ;

            ColorOverLifetimeModuleEnabled = ColorModule.Enabled;
            ColorKeyCount = ColorModule.KeyCount;
            CK1T = ColorModule.CK1T; CK1R = ColorModule.CK1R; CK1G = ColorModule.CK1G; CK1B = ColorModule.CK1B; CK1A = ColorModule.CK1A;
            CK2T = ColorModule.CK2T; CK2R = ColorModule.CK2R; CK2G = ColorModule.CK2G; CK2B = ColorModule.CK2B; CK2A = ColorModule.CK2A;
            CK3T = ColorModule.CK3T; CK3R = ColorModule.CK3R; CK3G = ColorModule.CK3G; CK3B = ColorModule.CK3B; CK3A = ColorModule.CK3A;
            CK4T = ColorModule.CK4T; CK4R = ColorModule.CK4R; CK4G = ColorModule.CK4G; CK4B = ColorModule.CK4B; CK4A = ColorModule.CK4A;

            NoiseModuleEnabled = NoiseModule.Enabled;
            TurbulenceStrength = NoiseModule.Strength;
            TurbulenceFrequency = NoiseModule.Frequency;

            bool moduleCollisionOn = Collision.Enabled || CollisionModuleEnabled || CollisionEnabled;
            Collision.Enabled = moduleCollisionOn;
            CollisionModuleEnabled = moduleCollisionOn;
            CollisionEnabled = moduleCollisionOn;
            CollisionBounciness = Collision.Bounciness;
            CollisionDampen = Collision.Dampen;
            CollisionQuality = Collision.Quality;
        }

        public override void OnEnable()
        {
            if (PlayOnAwake && !_playing && _particles.Count == 0)
                Play();
        }

        public override void OnDisable()
        {
            Pause();
        }

        public override void OnDestroy()
        {
            Stop();
        }

        public override void Start()
        {
            SyncLegacyFieldsToModules();
            if (PlayOnAwake) Play();
        }

        private void Emit()
        {
            var dir = ShapeModuleEnabled ? SampleDirection() : new Vector3(0f, 1f, 0f);
            float curveT = NormalizedEmitterTime();
            float speed = SampleValue(SpeedMode, SpeedMin, SpeedMax, SpeedCurveMid, SpeedCurveMidValue, curveT);
            float life = SampleValue(LifetimeMode, LifetimeMin, LifetimeMax, LifetimeCurveMid, LifetimeCurveMidValue, curveT);
            float rot = RotationOverLifetimeModuleEnabled
                ? Lerp(RotationSpeedMin, RotationSpeedMax, (float)_rng.NextDouble())
                : 0f;

            var origin = SimulationSpace == ParticleSimulationSpace.World
                ? new Vector3(gameObject!.PosX, gameObject.PosY, gameObject.PosZ)
                : Vector3.Zero;

            var offset = ShapeModuleEnabled ? SampleOffset() : Vector3.Zero;
            origin.X += offset.X;
            origin.Y += offset.Y;
            origin.Z += offset.Z;

            float sizeA = StartSize3D ? SizeStartX : SizeStart;
            float sizeB = StartSize3D ? SizeEndX : SizeEnd;
            float startSize = SizeMode is ParticleValueMode.RandomBetweenTwoConstants or ParticleValueMode.RandomBetweenTwoCurves
                ? SampleValue(SizeMode, Math.Min(sizeA, sizeB), Math.Max(sizeA, sizeB), StartSizeCurveMid, StartSizeCurveMidValue, curveT)
                : SampleValue(SizeMode, sizeA, sizeA, StartSizeCurveMid, StartSizeCurveMidValue, curveT);
            float endSize = SizeOverLifetimeModuleEnabled ? sizeB : startSize;

            _particles.Add(new Particle
            {
                Position = origin,
                Velocity = new Vector3(
                    dir.X * speed + _emitterVelocity.X * InheritVelocity,
                    dir.Y * speed + _emitterVelocity.Y * InheritVelocity,
                    dir.Z * speed + _emitterVelocity.Z * InheritVelocity),
                Age = 0f,
                Lifetime = life,
                SizeStart = startSize,
                SizeEnd = endSize,
                ColorStartR = ColorStartR,
                ColorStartG = ColorStartG,
                ColorStartB = ColorStartB,
                ColorStartA = ColorStartA,
                ColorEndR = ColorEndR,
                ColorEndG = ColorEndG,
                ColorEndB = ColorEndB,
                ColorEndA = ColorEndA,
                Id = _nextParticleId++,
                RotationSpeed = rot,
                Rotation = 0f,
                NoiseSeed = (float)(_rng.NextDouble() * Math.PI * 20)
            });

            // Track birth position so the renderer can trigger sub-emitters
            if (!string.IsNullOrEmpty(SubEmitterBirth))
                _birthPositions.Add(origin);
        }

        private Vector3 SampleDirection()
        {
            Vector3 dir;
            switch (Shape)
            {
                case ParticleShape.Sphere:
                    {
                        double theta = _rng.NextDouble() * Math.Clamp(ShapeArc, 0f, 360f) * Math.PI / 180.0;
                        double phi = Math.Acos(2 * _rng.NextDouble() - 1);
                        dir = new Vector3(
                            (float)(Math.Sin(phi) * Math.Cos(theta)),
                            (float)(Math.Sin(phi) * Math.Sin(theta)),
                            (float)Math.Cos(phi));
                        break;
                    }
                case ParticleShape.Cone:
                    {
                        double angleRad = ShapeAngle * Math.PI / 180.0;
                        double theta = _rng.NextDouble() * Math.Clamp(ShapeArc, 0f, 360f) * Math.PI / 180.0;
                        double phi = _rng.NextDouble() * angleRad;
                        dir = new Vector3(
                            (float)(Math.Sin(phi) * Math.Cos(theta)),
                            (float)Math.Cos(phi),
                            (float)(Math.Sin(phi) * Math.Sin(theta)));
                        break;
                    }
                case ParticleShape.Circle:
                    {
                        double theta = _rng.NextDouble() * Math.Clamp(ShapeArc, 0f, 360f) * Math.PI / 180.0;
                        dir = new Vector3((float)Math.Cos(theta), 0f, (float)Math.Sin(theta));
                        break;
                    }
                default: // Box
                    dir = new Vector3(0f, 1f, 0f);
                    break;
            }

            float random = Math.Clamp(ShapeRandomDirectionAmount, 0f, 1f);
            if (random > 0f)
            {
                double theta = _rng.NextDouble() * 2 * Math.PI;
                double phi = Math.Acos(2 * _rng.NextDouble() - 1);
                var rnd = new Vector3(
                    (float)(Math.Sin(phi) * Math.Cos(theta)),
                    (float)(Math.Sin(phi) * Math.Sin(theta)),
                    (float)Math.Cos(phi));
                dir = new Vector3(
                    Lerp(dir.X, rnd.X, random),
                    Lerp(dir.Y, rnd.Y, random),
                    Lerp(dir.Z, rnd.Z, random));
            }

            float len = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
            return len > 0.0001f ? new Vector3(dir.X / len, dir.Y / len, dir.Z / len) : new Vector3(0f, 1f, 0f);
        }

        private Vector3 SampleOffset()
        {
            if (Shape == ParticleShape.Box)
                return new Vector3(
                    ((float)_rng.NextDouble() - 0.5f) * ShapeBoxSizeX,
                    ((float)_rng.NextDouble() - 0.5f) * ShapeBoxSizeY,
                    ((float)_rng.NextDouble() - 0.5f) * ShapeBoxSizeZ);

            double theta = _rng.NextDouble() * Math.Clamp(ShapeArc, 0f, 360f) * Math.PI / 180.0;
            float minR = ShapeEmitFromShell ? ShapeRadius : ShapeRadius * Math.Clamp(1f - ShapeRadiusThickness, 0f, 1f);
            float maxR = ShapeRadius;
            float r = maxR <= 0f ? 0f : Lerp(minR, maxR, (float)_rng.NextDouble());
            return new Vector3((float)(r * Math.Cos(theta)), 0f, (float)(r * Math.Sin(theta)));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private float SampleValue(ParticleValueMode mode, float min, float max, float curveMid, float curveMidValue, float curveT)
        {
            float curve = EvaluateSimpleCurve(curveT, curveMid, curveMidValue);
            return mode switch
            {
                ParticleValueMode.Constant => max,
                ParticleValueMode.Curve => max * curve,
                ParticleValueMode.RandomBetweenTwoCurves => Lerp(min, max, (float)_rng.NextDouble()) * curve,
                _ => Lerp(min, max, (float)_rng.NextDouble())
            };
        }

        private float NormalizedEmitterTime()
        {
            float local = Math.Max(0f, _time - StartDelay);
            return Duration <= 0.0001f ? 0f : Math.Clamp(local / Duration, 0f, 1f);
        }

        public static float EvaluateSimpleCurve(float t, float mid, float midValue)
        {
            mid = Math.Clamp(mid, 0.001f, 0.999f);
            t = Math.Clamp(t, 0f, 1f);
            if (t <= mid)
                return Lerp(1f, midValue, t / mid);
            return Lerp(midValue, 1f, (t - mid) / (1f - mid));
        }

        /// <summary>Samplea el gradiente de color en t [0,1] usando hasta 4 paradas.</summary>
        public (float r, float g, float b, float a) SampleGradient(float t)
        {
            // Construir arreglo de paradas activas ordenadas por tiempo
            Span<(float t, float r, float g, float b, float a)> keys = stackalloc (float, float, float, float, float)[4];
            keys[0] = (CK1T, CK1R, CK1G, CK1B, CK1A);
            keys[1] = (CK2T, CK2R, CK2G, CK2B, CK2A);
            keys[2] = (CK3T, CK3R, CK3G, CK3B, CK3A);
            keys[3] = (CK4T, CK4R, CK4G, CK4B, CK4A);
            int count = Math.Clamp(ColorKeyCount, 2, 4);

            // Ordenar por tiempo (insertion sort, N pequeño)
            for (int i = 1; i < count; i++)
                for (int j = i; j > 0 && keys[j].t < keys[j - 1].t; j--)
                    (keys[j], keys[j - 1]) = (keys[j - 1], keys[j]);

            if (t <= keys[0].t) return (keys[0].r, keys[0].g, keys[0].b, keys[0].a);
            if (t >= keys[count - 1].t) return (keys[count - 1].r, keys[count - 1].g, keys[count - 1].b, keys[count - 1].a);

            for (int i = 0; i < count - 1; i++)
            {
                if (t >= keys[i].t && t <= keys[i + 1].t)
                {
                    float f = (t - keys[i].t) / Math.Max(0.0001f, keys[i + 1].t - keys[i].t);
                    return (
                        Lerp(keys[i].r, keys[i + 1].r, f),
                        Lerp(keys[i].g, keys[i + 1].g, f),
                        Lerp(keys[i].b, keys[i + 1].b, f),
                        Lerp(keys[i].a, keys[i + 1].a, f));
                }
            }
            return (keys[count - 1].r, keys[count - 1].g, keys[count - 1].b, keys[count - 1].a);
        }
    }
}
