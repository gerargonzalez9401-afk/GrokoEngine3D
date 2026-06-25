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
    // RIGIDBODY
    // =====================================================
    public enum ForceMode
    {
        Force,
        Acceleration,
        Impulse,
        VelocityChange
    }

    public class Rigidbody : Component
    {
        // ── Propiedades físicas ───────────────────────────────
        public float Gravity = 9.81f;
        public float Mass = 1f;
        public bool UseGravity = true;
        public bool IsKinematic = false;
        public float Drag = 0f;
        public float AngularDrag = 0.05f;
        public float Bounciness = 0f;
        public float Friction = 0.5f;

        // ── Freeze position ───────────────────────────────────
        public bool FreezePositionX = false;
        public bool FreezePositionY = false;
        public bool FreezePositionZ = false;

        // ── Freeze rotation ───────────────────────────────────
        public bool FreezeRotationX = false;
        public bool FreezeRotationY = false;
        public bool FreezeRotationZ = false;

        // ── Estado interno ────────────────────────────────────
        public Vector3 Velocity = Vector3.Zero;
        public PhysicsEngine? Physics { get; set; }
        public bool IsGrounded { get; internal set; }
        public Vector3 LastCollisionNormal { get; internal set; } = Vector3.Zero;

        private Vector3 accumulatedForce = Vector3.Zero;

        // Estado puente para BepuPhysics.
        // Bepu es el solver real, pero Rigidbody sigue siendo la API pública del motor.
        internal bool BepuVelocityDirty { get; private set; } = true;
        internal bool BepuForcesDirty => accumulatedForce.X != 0f || accumulatedForce.Y != 0f || accumulatedForce.Z != 0f;

        internal Vector3 ConsumeAccumulatedForce()
        {
            var f = accumulatedForce;
            accumulatedForce = Vector3.Zero;
            return f;
        }

        internal void SyncVelocityFromBepu(Vector3 velocity)
        {
            Velocity = velocity;
            BepuVelocityDirty = false;
        }

        internal void MarkBepuVelocityDirty()
        {
            BepuVelocityDirty = true;
        }

        // Valores de rotación capturados la primera vez que se activa el freeze.
        private float _frozenRotX, _frozenRotY, _frozenRotZ;
        private bool _rotXFrozen, _rotYFrozen, _rotZFrozen;

        // Umbral de velocidad para considerar el objeto "en reposo" en Y
        private const float RestThresholdY = 0.001f;

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            float invMass = 1f / Math.Max(0.0001f, Mass);

            switch (mode)
            {
                case ForceMode.Acceleration:
                    accumulatedForce = Add(accumulatedForce, Scale(force, Math.Max(0.0001f, Mass)));
                    break;
                case ForceMode.Impulse:
                    Velocity = Add(Velocity, Scale(force, invMass));
                    BepuVelocityDirty = true;
                    break;
                case ForceMode.VelocityChange:
                    Velocity = Add(Velocity, force);
                    BepuVelocityDirty = true;
                    break;
                default:
                    accumulatedForce = Add(accumulatedForce, force);
                    break;
            }

            if (BepuBackend.Enabled)
                BepuBackend.Wake(this);
        }

        public override void Update(double dt)
        {
            // Con el backend Bepu activo, la simulación la lleva Bepu: no integramos aquí.
            if (BepuBackend.Enabled) return;
            if (IsKinematic) return;

            float stableDt = Math.Clamp((float)dt, 0f, 0.25f);
            if (stableDt <= 0f)
                return;

            IsGrounded = false;
            LastCollisionNormal = Vector3.Zero;

            if (UseGravity)
                Velocity.Y -= Gravity * stableDt;

            if (accumulatedForce.X != 0f || accumulatedForce.Y != 0f || accumulatedForce.Z != 0f)
            {
                float invMass = 1f / Math.Max(0.0001f, Mass);
                Velocity = Add(Velocity, Scale(accumulatedForce, invMass * stableDt));
                accumulatedForce = Vector3.Zero;
            }

            if (Drag > 0f)
            {
                float dragFactor = 1f / (1f + Drag * stableDt);
                Velocity.X *= dragFactor;
                Velocity.Y *= dragFactor;
                Velocity.Z *= dragFactor;
            }

            var stableCollider = gameObject.GetComponent<Collider>();
            var currentWorld = gameObject.Position;
            var targetWorld = new Vector3(
                FreezePositionX ? currentWorld.X : currentWorld.X + Velocity.X * stableDt,
                FreezePositionY ? currentWorld.Y : currentWorld.Y + Velocity.Y * stableDt,
                FreezePositionZ ? currentWorld.Z : currentWorld.Z + Velocity.Z * stableDt);

            if (stableCollider != null && Physics != null && !stableCollider.IsTrigger)
                Physics.MoveRigidbody(this, stableCollider, targetWorld, stableDt);
            else
                gameObject.Position = targetWorld;

            Physics?.MarkSpatialHashDirty();
            ApplyFrozenRotation();
            return;
        }

        private void ApplyFrozenRotation()
        {
            if (FreezeRotationX)
            {
                if (!_rotXFrozen) { _frozenRotX = gameObject.RotX; _rotXFrozen = true; }
                gameObject.RotX = _frozenRotX;
            }
            else _rotXFrozen = false;

            if (FreezeRotationY)
            {
                if (!_rotYFrozen) { _frozenRotY = gameObject.RotY; _rotYFrozen = true; }
                gameObject.RotY = _frozenRotY;
            }
            else _rotYFrozen = false;

            if (FreezeRotationZ)
            {
                if (!_rotZFrozen) { _frozenRotZ = gameObject.RotZ; _rotZFrozen = true; }
                gameObject.RotZ = _frozenRotZ;
            }
            else _rotZFrozen = false;
        }

        private static Vector3 Add(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        private static Vector3 Scale(Vector3 v, float scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
    }
}
