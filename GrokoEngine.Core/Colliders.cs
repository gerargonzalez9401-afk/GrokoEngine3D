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
    // COLLIDERS
    // =====================================================
    public enum CapsuleAxis { X, Y, Z }

    public abstract class Collider : Component
    {
        public Vector3 Center = Vector3.Zero;
        public bool IsTrigger = false;
        public string PhysicMaterial = "Default";
        public float Friction = 0.5f;
        public float Bounciness = 0f;

        public abstract Bounds GetBounds();

        protected Vector3 WorldCenter()
        {
            var worldPosition = gameObject.WorldPosition;
            var worldScale = gameObject.WorldScale;
            return new Vector3(
                worldPosition.X + Center.X * worldScale.X,
                worldPosition.Y + Center.Y * worldScale.Y,
                worldPosition.Z + Center.Z * worldScale.Z);
        }
    }

    public class BoxCollider : Collider
    {
        public Vector3 Size = new Vector3(1, 1, 1);

        public override Bounds GetBounds()
        {
            // AABB que ENVUELVE la caja, teniendo en cuenta la ROTACIÓN del objeto:
            // se transforman las 8 esquinas locales por la matriz de mundo y se toma min/max.
            // (Para un objeto sin rotar, equivale a center ± half de antes.)
            var m = gameObject.WorldMatrix;
            float hx = Size.X * 0.5f, hy = Size.Y * 0.5f, hz = Size.Z * 0.5f;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                float sz = (i & 4) == 0 ? -1f : 1f;
                var wp = System.Numerics.Vector3.Transform(
                    new System.Numerics.Vector3(Center.X + sx * hx, Center.Y + sy * hy, Center.Z + sz * hz), m);
                if (wp.X < minX) minX = wp.X; if (wp.X > maxX) maxX = wp.X;
                if (wp.Y < minY) minY = wp.Y; if (wp.Y > maxY) maxY = wp.Y;
                if (wp.Z < minZ) minZ = wp.Z; if (wp.Z > maxZ) maxZ = wp.Z;
            }

            // Margen mínimo para evitar un AABB degenerado (grosor 0).
            const float eps = 0.0001f;
            if (maxX - minX < eps) { maxX += eps; minX -= eps; }
            if (maxY - minY < eps) { maxY += eps; minY -= eps; }
            if (maxZ - minZ < eps) { maxZ += eps; minZ -= eps; }

            return new Bounds(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
        }
    }

    public class TerrainCollider : Collider
    {
        private int _cachedHeightVersion = -1;
        private float _cachedMinHeight;
        private float _cachedMaxHeight;

        private void EnsureHeightRange(Terrain terrain)
        {
            // Cacheado por CollisionVersion (no Version): este escaneo recorre el heightmap
            // COMPLETO (4M de floats a resolución 2049) y Version cambia en cada frame de
            // pincelada — se recalcula al soltar el pincel, cuando la física realmente rebuildea.
            if (_cachedHeightVersion == terrain.CollisionVersion)
                return;

            float min = 0f, max = 0f;
            if (terrain.Heightmap.Length > 0)
            {
                min = float.MaxValue;
                max = float.MinValue;
                for (int i = 0; i < terrain.Heightmap.Length; i++)
                {
                    float h = terrain.Heightmap[i];
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }

            _cachedMinHeight = min * terrain.HeightScale;
            _cachedMaxHeight = max * terrain.HeightScale;
            _cachedHeightVersion = terrain.CollisionVersion;
        }

        // Altura mundial real del terreno bajo (worldX, worldZ), vía interpolación bilineal del heightmap.
        // Asume que el terreno no está rotado (misma limitación que el pincel de sculpting de la Fase 2).
        public float SampleHeight(float worldX, float worldZ)
        {
            var terrain = gameObject.GetComponent<Terrain>();
            var worldPosition = gameObject.WorldPosition;
            if (terrain == null)
                return worldPosition.Y;

            return worldPosition.Y + terrain.GetHeightLocal(worldX - worldPosition.X, worldZ - worldPosition.Z);
        }

        public override Bounds GetBounds()
        {
            var worldPosition = gameObject.WorldPosition;
            var worldScale = gameObject.WorldScale;
            var terrain = gameObject.GetComponent<Terrain>();

            float sizeX = terrain?.SizeX ?? 1f;
            float sizeZ = terrain?.SizeZ ?? 1f;

            float halfX = Math.Abs(sizeX * worldScale.X) / 2f;
            float halfZ = Math.Abs(sizeZ * worldScale.Z) / 2f;

            float minY = 0f;
            float maxY = Math.Abs((terrain?.HeightScale ?? 1f) * worldScale.Y);
            if (terrain != null)
            {
                EnsureHeightRange(terrain);
                minY = Math.Min(0f, _cachedMinHeight);
                maxY = Math.Max(0f, _cachedMaxHeight);
            }

            var min = new Vector3(worldPosition.X - halfX, worldPosition.Y + minY, worldPosition.Z - halfZ);
            var max = new Vector3(worldPosition.X + halfX, worldPosition.Y + maxY, worldPosition.Z + halfZ);
            return new Bounds(min, max);
        }
    }

    public class SphereCollider : Collider
    {
        public float Radius = 0.5f;

        public override Bounds GetBounds()
        {
            var center = WorldCenter();
            var scale = gameObject.WorldScale;
            float radius = Math.Max(0.0001f, Math.Abs(Radius) * Math.Max(Math.Abs(scale.X), Math.Max(Math.Abs(scale.Y), Math.Abs(scale.Z))));
            var half = new Vector3(radius, radius, radius);
            return new Bounds(center - half, center + half);
        }
    }

    public class CapsuleCollider : Collider
    {
        public float Radius = 0.5f;
        public float Height = 2f;
        public CapsuleAxis Axis = CapsuleAxis.Y;

        public override Bounds GetBounds()
        {
            var center = WorldCenter();
            var scale = gameObject.WorldScale;
            float sx = Math.Abs(scale.X);
            float sy = Math.Abs(scale.Y);
            float sz = Math.Abs(scale.Z);
            float radiusScale = Axis switch
            {
                CapsuleAxis.X => Math.Max(sy, sz),
                CapsuleAxis.Y => Math.Max(sx, sz),
                _ => Math.Max(sx, sy)
            };
            float axisScale = Axis switch
            {
                CapsuleAxis.X => sx,
                CapsuleAxis.Y => sy,
                _ => sz
            };
            float radius = Math.Max(0.0001f, Math.Abs(Radius) * radiusScale);
            float height = Math.Max(radius * 2f, Math.Abs(Height) * axisScale);
            var half = new Vector3(radius, radius, radius);
            if (Axis == CapsuleAxis.X) half.X = height * 0.5f;
            else if (Axis == CapsuleAxis.Y) half.Y = height * 0.5f;
            else half.Z = height * 0.5f;
            return new Bounds(center - half, center + half);
        }
    }

    public class MeshCollider : Collider
    {
        public Vector3 Size = new Vector3(1, 1, 1);
        public bool UseMeshBounds = true;

        public override Bounds GetBounds()
        {
            if (UseMeshBounds &&
                gameObject.GetComponent<MeshFilter>() is { } mf &&
                !string.IsNullOrWhiteSpace(mf.MeshPath) &&
                ObjLoader.Load(mf.MeshPath) is { } mesh)
            {
                float importScale = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
                var wm = gameObject.WorldMatrix;

                // Si el objeto dibuja solo una submalla (hijo de un FBX "con hijos"), la caja es la de ESA parte.
                Vector3 bmin = mesh.BoundsMin, bmax = mesh.BoundsMax;
                if (mf.SubmeshIndex >= 0 && mf.SubmeshIndex < mesh.Submeshes.Count)
                {
                    var sub = mesh.Submeshes[mf.SubmeshIndex];
                    bmin = new Vector3(sub.MinX, sub.MinY, sub.MinZ);
                    bmax = new Vector3(sub.MaxX, sub.MaxY, sub.MaxZ);
                }

                // AABB mundial: transformar las 8 esquinas por la matriz mundial completa (incluye ROTACIÓN).
                float lminX = bmin.X * importScale, lminY = bmin.Y * importScale, lminZ = bmin.Z * importScale;
                float lmaxX = bmax.X * importScale, lmaxY = bmax.Y * importScale, lmaxZ = bmax.Z * importScale;
                var wmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var wmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int ci = 0; ci < 8; ci++)
                {
                    float cxv = (ci & 1) == 0 ? lminX : lmaxX;
                    float cyv = (ci & 2) == 0 ? lminY : lmaxY;
                    float czv = (ci & 4) == 0 ? lminZ : lmaxZ;
                    var w = ToWorld(cxv, cyv, czv, wm);
                    wmin = new Vector3(Math.Min(wmin.X, w.X), Math.Min(wmin.Y, w.Y), Math.Min(wmin.Z, w.Z));
                    wmax = new Vector3(Math.Max(wmax.X, w.X), Math.Max(wmax.Y, w.Y), Math.Max(wmax.Z, w.Z));
                }
                return new Bounds(wmin, wmax);
            }

            var center = WorldCenter();
            var scale = gameObject.WorldScale;
            var half = new Vector3(
                Math.Max(0.0001f, Math.Abs(Size.X * scale.X)) / 2f,
                Math.Max(0.0001f, Math.Abs(Size.Y * scale.Y)) / 2f,
                Math.Max(0.0001f, Math.Abs(Size.Z * scale.Z)) / 2f);
            return new Bounds(center - half, center + half);
        }

        // Memo de la última consulta: la física llama a TrySampleHeight varias veces por frame
        // con el MISMO (x,z) (probe de suelo + resolución por eje del CharacterController),
        // así que repetir el barrido de triángulos sería tirar el trabajo.
        private object? _memoMesh;
        private System.Numerics.Matrix4x4 _memoMatrix;
        private float _memoX, _memoZ, _memoSurfaceY;
        private bool _memoFound, _memoValid;

        // Altura mundial de la SUPERFICIE de la malla bajo (worldX, worldZ): busca el triángulo más alto
        // que cubre ese punto (en XZ) e interpola su Y. Permite colisión de RAMPAS/pendientes (la inclinación
        // está en la geometría de la malla), sin reescribir el motor de físicas AABB. Respeta la ROTACIÓN
        // del objeto (p. ej. un puente girado 90°): el rayo vertical mundial se lleva al espacio de la MALLA
        // con la inversa de la matriz mundial, y el barrido corre sobre las posiciones crudas SIN transformar
        // cada vértice (Möller–Trumbore por triángulo; antes eran 3 transformaciones de matriz por triángulo).
        public bool TrySampleHeight(float worldX, float worldZ, out float surfaceY)
        {
            surfaceY = 0f;
            if (!UseMeshBounds) return false;
            var mf = gameObject.GetComponent<MeshFilter>();
            if (mf == null || string.IsNullOrWhiteSpace(mf.MeshPath)) return false;
            var mesh = ObjLoader.Load(mf.MeshPath);
            if (mesh == null || mesh.Positions.Length < 9) return false;

            var wm = gameObject.WorldMatrix;
            if (_memoValid && ReferenceEquals(_memoMesh, mesh) && _memoX == worldX && _memoZ == worldZ && _memoMatrix == wm)
            {
                surfaceY = _memoSurfaceY;
                return _memoFound;
            }

            if (!System.Numerics.Matrix4x4.Invert(wm, out var inv))
                return false;

            float importScale = mf.ImportScale <= 0f ? 1f : mf.ImportScale;

            int vStart = 0;
            int vEnd = mesh.Positions.Length / 3;
            if (mf.SubmeshIndex >= 0 && mf.SubmeshIndex < mesh.Submeshes.Count)
            {
                var sub = mesh.Submeshes[mf.SubmeshIndex];
                vStart = sub.VertexStart;
                vEnd = Math.Min(vEnd, sub.VertexStart + sub.VertexCount);
            }

            // Rayo mundial: origen (worldX, 0, worldZ), dirección (0,-1,0). En espacio de malla:
            // mundo = wm · (raw·importScale + Center)  →  raw(t) = ((inv·o - Center) + t·(inv·d)) / importScale.
            // El parámetro t se conserva, así que la Y mundial del impacto es simplemente -t.
            float invScale = 1f / importScale;
            var oL = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(worldX, 0f, worldZ), inv);
            var dL = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(0f, -1f, 0f), inv);
            float rox = (oL.X - Center.X) * invScale, roy = (oL.Y - Center.Y) * invScale, roz = (oL.Z - Center.Z) * invScale;
            float rdx = dL.X * invScale, rdy = dL.Y * invScale, rdz = dL.Z * invScale;

            bool found = false;
            float bestT = float.MaxValue;   // t mínimo = Y mundial máxima (dirección -Y)
            var positions = mesh.Positions;
            const float eps = 0.0005f;      // misma tolerancia de borde que la versión baricéntrica anterior
            for (int v = vStart; v + 2 < vEnd; v += 3)
            {
                int i = v * 3;
                float ax = positions[i], ay = positions[i + 1], az = positions[i + 2];
                float e1x = positions[i + 3] - ax, e1y = positions[i + 4] - ay, e1z = positions[i + 5] - az;
                float e2x = positions[i + 6] - ax, e2y = positions[i + 7] - ay, e2z = positions[i + 8] - az;

                float px = rdy * e2z - rdz * e2y;
                float py = rdz * e2x - rdx * e2z;
                float pz = rdx * e2y - rdy * e2x;
                float det = e1x * px + e1y * py + e1z * pz;
                if (det > -1e-9f && det < 1e-9f) continue;   // triángulo paralelo al rayo (pared vertical)

                float invDet = 1f / det;
                float tx = rox - ax, ty = roy - ay, tz = roz - az;
                float u = (tx * px + ty * py + tz * pz) * invDet;
                if (u < -eps || u > 1f + eps) continue;

                float qx = ty * e1z - tz * e1y;
                float qy = tz * e1x - tx * e1z;
                float qz = tx * e1y - ty * e1x;
                float w = (rdx * qx + rdy * qy + rdz * qz) * invDet;
                if (w < -eps || u + w > 1f + eps) continue;

                // Línea infinita, no rayo: t puede ser negativo (triángulo por encima del origen Y=0).
                float t = (e2x * qx + e2y * qy + e2z * qz) * invDet;
                if (t < bestT) { bestT = t; found = true; }
            }

            if (found) surfaceY = -bestT;

            _memoMesh = mesh;
            _memoMatrix = wm;
            _memoX = worldX;
            _memoZ = worldZ;
            _memoSurfaceY = surfaceY;
            _memoFound = found;
            _memoValid = true;
            return found;
        }

        // Pasa una posición local (malla × importScale) a mundo, sumándole el Center y aplicando la matriz
        // mundial completa (posición + rotación + escala). Coherente con cómo el render coloca la malla.
        private Vector3 ToWorld(float lx, float ly, float lz, System.Numerics.Matrix4x4 wm)
        {
            var p = System.Numerics.Vector3.Transform(
                new System.Numerics.Vector3(lx + Center.X, ly + Center.Y, lz + Center.Z), wm);
            return new Vector3(p.X, p.Y, p.Z);
        }
    }
}
