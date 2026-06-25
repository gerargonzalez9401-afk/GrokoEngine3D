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
    // CHARACTER CONTROLLER
    // =====================================================
    public class CharacterController : Component
    {
        public float Height = 2f;
        public float Radius = 0.35f;
        public Vector3 Center = new Vector3(0f, 1f, 0f);
        public bool AutoCenter = true;
        public float SkinWidth = 0.02f;
        public float StepOffset = 0.35f;
        public float SlopeLimit = 45f;
        public bool UseGravity = true;
        public float Gravity = 9.81f;
        public float JumpSpeed = 5.5f;
        public float MaxFallSpeed = 40f;
        public float PushPower = 1f;
        public Vector3 Velocity = Vector3.Zero;
        public Vector3 LastMoveDelta { get; private set; } = Vector3.Zero;
        public CollisionFlags LastMoveFlags { get; private set; }
        public bool IsGrounded { get; internal set; }
        public CollisionFlags CollisionFlags { get; internal set; }
        // Normal del último contacto válido detectado por BEPU.
        // Sirve para suavizar el movimiento al correr contra paredes sin vibración.
        public Vector3 LastHitNormal { get; internal set; } = Vector3.Zero;
        public PhysicsEngine? Physics { get; set; }

        private CapsuleCollider? runtimeCollider;

        public override void Update(double dt)
        {
            // En modo Bepu el movimiento lo resuelve BepuBackend.MoveCharacter (no necesita Physics).
            if (Physics == null && !BepuBackend.Enabled)
                return;

            float fdt = Math.Clamp((float)dt, 0f, 0.25f);
            if (fdt <= 0f)
                return;

            if (UseGravity)
            {
                if (IsGrounded && Velocity.Y <= 0f)
                {
                    // Mantener el personaje pegado al suelo sin meter un empuje vertical constante.
                    // El empuje anterior (-0.5f) hacía que el sweep tocara el piso cada frame y
                    // generaba micro-subidas/micro-bajadas visuales.
                    Velocity.Y = 0f;
                    Move(new Vector3(0f, -Math.Max(0.002f, SkinWidth * 0.25f), 0f));
                }
                else
                {
                    Velocity.Y = Math.Max(Velocity.Y - Gravity * fdt, -Math.Abs(MaxFallSpeed));
                    Move(new Vector3(0f, Velocity.Y * fdt, 0f));
                }
            }
        }

        public CollisionFlags Move(Vector3 displacement)
        {
            LastMoveDelta = displacement;
            LastHitNormal = Vector3.Zero;

            // Modo Bepu: collide-and-slide kinemático por sweeps (geometría rotada/rampas/mallas).
            if (BepuBackend.Enabled)
            {
                var bflags = BepuBackend.MoveCharacter(this, displacement);
                CollisionFlags = bflags;
                LastMoveFlags = bflags;
                IsGrounded = (bflags & CollisionFlags.Below) != 0;
                return bflags;
            }

            if (Physics == null)
            {
                gameObject.Position = Add(gameObject.Position, displacement);
                LastMoveFlags = CollisionFlags.None;
                return CollisionFlags.None;
            }

            var collider = EnsureCollider();
            SyncColliderShape(collider);

            var flags = Physics.MoveCharacterController(this, collider, displacement);
            CollisionFlags = flags;
            LastMoveFlags = flags;
            IsGrounded = (flags & CollisionFlags.Below) != 0;
            return flags;
        }

        public CollisionFlags SimpleMove(Vector3 speed, double dt)
        {
            float fdt = Math.Clamp((float)dt, 0f, 0.25f);
            Velocity.X = speed.X;
            Velocity.Z = speed.Z;
            if (UseGravity)
            {
                if (IsGrounded && Velocity.Y <= 0f)
                    Velocity.Y = 0f;
                else
                    Velocity.Y = Math.Max(Velocity.Y - Gravity * fdt, -Math.Abs(MaxFallSpeed));
            }

            return Move(new Vector3(Velocity.X * fdt, Velocity.Y * fdt, Velocity.Z * fdt));
        }

        public void Jump()
        {
            if (IsGrounded)
            {
                Velocity.Y = Math.Max(0f, JumpSpeed);
                IsGrounded = false;
                CollisionFlags &= ~CollisionFlags.Below;
            }
        }

        public CapsuleCollider EnsureCollider()
        {
            runtimeCollider = gameObject.GetComponent<CapsuleCollider>();
            if (runtimeCollider == null)
                runtimeCollider = Physics != null
                    ? gameObject.AddComponentWithEngine<CapsuleCollider>(Physics)
                    : gameObject.AddComponent<CapsuleCollider>();
            SyncColliderShape(runtimeCollider);
            return runtimeCollider;
        }

        private void SyncColliderShape(CapsuleCollider collider)
        {
            collider.Radius = Math.Max(0.001f, Radius);
            collider.Height = Math.Max(collider.Radius * 2f, Height);
            if (AutoCenter)
                Center = new Vector3(Center.X, collider.Height * 0.5f, Center.Z);
            collider.Center = Center;
            collider.Axis = CapsuleAxis.Y;
            collider.Bounciness = 0f;
        }

        private static Vector3 Add(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }
}
