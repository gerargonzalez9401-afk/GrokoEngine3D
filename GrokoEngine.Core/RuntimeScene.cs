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

    public static class RuntimeScene
    {
        private static IList<GameObject>? roots;
        private static PhysicsEngine? physicsEngine;
        private static ScriptCompiler? scriptCompiler;
        private static readonly List<PendingDestroy> pendingDestroy = new();

        public static bool IsReady => roots != null && physicsEngine != null && scriptCompiler != null;

        public static void SetContext(IList<GameObject> sceneRoots, PhysicsEngine physics, ScriptCompiler compiler)
        {
            bool contextChanged =
                !ReferenceEquals(roots, sceneRoots) ||
                !ReferenceEquals(physicsEngine, physics) ||
                !ReferenceEquals(scriptCompiler, compiler);

            roots = sceneRoots;
            physicsEngine = physics;
            scriptCompiler = compiler;
            if (contextChanged)
                pendingDestroy.Clear();
        }

        public static void ClearContext()
        {
            roots = null;
            physicsEngine = null;
            scriptCompiler = null;
            pendingDestroy.Clear();
        }

        public static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            EnsureReady();
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));

            string json = SceneSerializer.SerializeObject(prefab);
            var obj = SceneSerializer.DeserializeObject(json, physicsEngine!, scriptCompiler!);
            RegenerateIds(obj);
            obj.Parent = null;
            obj.Position = position;
            // Convertir el cuaternión a ángulos Euler en grados (los campos RotX/Y/Z son grados).
            var (ex, ey, ez) = QuaternionToEulerDeg(rotation);
            obj.RotX = ex;
            obj.RotY = ey;
            obj.RotZ = ez;
            roots!.Add(obj);
            StartEnabledComponents(obj);
            physicsEngine!.MarkSpatialHashDirty();
            BepuBackend.Reset();
            return obj;
        }

        public static GameObject Instantiate(string prefabPath, Vector3 position, Vector3 rotation)
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(prefabPath))
                throw new ArgumentException("Prefab path is empty.", nameof(prefabPath));
            if (!File.Exists(prefabPath))
                throw new FileNotFoundException("Prefab asset not found.", prefabPath);

            var obj = SceneSerializer.LoadPrefab(prefabPath, physicsEngine!, scriptCompiler!);
            RegenerateIds(obj);
            obj.PrefabAssetPath = prefabPath;
            obj.Parent = null;
            obj.Position = position;
            obj.RotX = rotation.X;
            obj.RotY = rotation.Y;
            obj.RotZ = rotation.Z;
            roots!.Add(obj);
            StartEnabledComponents(obj);
            physicsEngine!.MarkSpatialHashDirty();
            BepuBackend.Reset();
            return obj;
        }

        public static void SetParent(GameObject obj, GameObject? parent, bool worldPositionStays = true)
        {
            EnsureReady();
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (ReferenceEquals(obj, parent))
                throw new InvalidOperationException("Cannot parent an object under itself.");

            if (parent != null)
            {
                var check = parent;
                while (check != null)
                {
                    if (ReferenceEquals(check, obj))
                        throw new InvalidOperationException("Cannot parent an object under one of its children.");

                    check = check.Parent;
                }
            }

            Vector3 worldPosition = obj.Position;
            Vector3 localPosition = new Vector3(obj.PosX, obj.PosY, obj.PosZ);
            obj.Parent = parent;

            if (worldPositionStays)
                obj.Position = worldPosition;
            else
            {
                obj.PosX = localPosition.X;
                obj.PosY = localPosition.Y;
                obj.PosZ = localPosition.Z;
            }

            physicsEngine!.MarkSpatialHashDirty();
        }

        public static void Unparent(GameObject obj, bool worldPositionStays = true)
        {
            SetParent(obj, null, worldPositionStays);
        }

        internal static void NotifyParentChanged(GameObject obj, GameObject? oldParent, GameObject? newParent)
        {
            if (roots == null || obj == null)
                return;

            if (newParent == null)
            {
                if (!roots.Contains(obj))
                    roots.Add(obj);
            }
            else
            {
                roots.Remove(obj);
            }

            physicsEngine?.MarkSpatialHashDirty();
        }

        public static void Destroy(GameObject obj, float delay = 0f)
        {
            EnsureReady();
            if (obj == null)
                return;

            double remaining = Math.Max(0f, delay);
            for (int i = 0; i < pendingDestroy.Count; i++)
            {
                if (pendingDestroy[i].Object == obj)
                {
                    pendingDestroy[i] = new PendingDestroy(obj, Math.Min(pendingDestroy[i].Remaining, remaining));
                    return;
                }
            }

            pendingDestroy.Add(new PendingDestroy(obj, remaining));
        }

        public static void Tick(double deltaTime)
        {
            if (roots == null || physicsEngine == null)
                return;

            for (int i = pendingDestroy.Count - 1; i >= 0; i--)
            {
                var pending = pendingDestroy[i];
                pending.Remaining -= Math.Max(0.0, deltaTime);
                if (pending.Remaining > 0.0)
                {
                    pendingDestroy[i] = pending;
                    continue;
                }

                pendingDestroy.RemoveAt(i);
                DestroyNow(pending.Object);
            }
        }

        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, bool includeTriggers = false)
        {
            EnsureReady();
            return physicsEngine!.Raycast(origin, direction, maxDistance, out hit, includeTriggers);
        }

        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, int layerMask, bool includeTriggers = false)
        {
            EnsureReady();
            return physicsEngine!.Raycast(origin, direction, maxDistance, out hit, layerMask, includeTriggers);
        }

        public static List<Collider> OverlapBox(Vector3 center, Vector3 size, bool includeTriggers = true)
        {
            EnsureReady();
            return physicsEngine!.OverlapBox(center, size, includeTriggers);
        }

        public static GameObject? FindObjectByName(string name)
        {
            if (roots == null || string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var root in roots)
            {
                var match = FindObjectByNameRecursive(root, name);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static void DestroyNow(GameObject obj)
        {
            foreach (var child in new List<GameObject>(obj.Children))
                DestroyNow(child);

            foreach (var component in new List<Component>(obj.Components))
                obj.RemoveComponent(component, physicsEngine);

            obj.Parent = null;
            roots?.Remove(obj);
            obj.Children.Clear();
            physicsEngine?.MarkSpatialHashDirty();
        }

        private static void StartEnabledComponents(GameObject obj)
        {
            if (!obj.IsActive)
                return;

            foreach (var component in new List<Component>(obj.Components))
            {
                if (component.Enabled)
                    component.InternalStart();
            }

            foreach (var child in obj.Children)
                StartEnabledComponents(child);
        }

        private static void RegenerateIds(GameObject obj)
        {
            obj.EditorId = Guid.NewGuid().ToString("N");
            foreach (var child in obj.Children)
                RegenerateIds(child);
        }

        private static GameObject? FindObjectByNameRecursive(GameObject obj, string name)
        {
            if (string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
                return obj;

            foreach (var child in obj.Children)
            {
                var match = FindObjectByNameRecursive(child, name);
                if (match != null)
                    return match;
            }

            return null;
        }

        /// <summary>
        /// Convierte un cuaternión a ángulos Euler en grados (orden ZXY, igual que el motor).
        /// Los campos RotX/RotY/RotZ del GameObject son grados, no componentes del cuaternión.
        /// </summary>
        private static (float x, float y, float z) QuaternionToEulerDeg(Quaternion q)
        {
            // Normalizar para evitar artefactos en ángulos límite.
            float n = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            if (n > 1e-6f) { q = new Quaternion(q.X / n, q.Y / n, q.Z / n, q.W / n); }

            // Extraer Euler ZXY (el mismo orden que usa RebuildMatricesIfNeeded en GameObject).
            // Pitch (X): asin(2*(w*x - y*z))
            float sinPitch = 2f * (q.W * q.X - q.Y * q.Z);
            float pitch = MathF.Abs(sinPitch) >= 1f
                ? MathF.CopySign(90f, sinPitch)
                : MathF.Asin(sinPitch) * (180f / MathF.PI);

            // Yaw (Y): atan2(2*(w*y + x*z), 1 - 2*(x*x + y*y))
            float yaw = MathF.Atan2(
                2f * (q.W * q.Y + q.X * q.Z),
                1f - 2f * (q.X * q.X + q.Y * q.Y)) * (180f / MathF.PI);

            // Roll (Z): atan2(2*(w*z + x*y), 1 - 2*(x*x + z*z))
            float roll = MathF.Atan2(
                2f * (q.W * q.Z + q.X * q.Y),
                1f - 2f * (q.X * q.X + q.Z * q.Z)) * (180f / MathF.PI);

            return (pitch, yaw, roll);
        }

        private static void EnsureReady()
        {
            if (!IsReady)
                throw new InvalidOperationException("RuntimeScene is not ready. Enter Play Mode before using Instantiate, Destroy, or Physics.Raycast.");
        }

        private struct PendingDestroy
        {
            public PendingDestroy(GameObject obj, double remaining)
            {
                Object = obj;
                Remaining = remaining;
            }

            public GameObject Object { get; }
            public double Remaining { get; set; }
        }
    }
}
