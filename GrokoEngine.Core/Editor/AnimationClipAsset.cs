using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GrokoEngine
{
    public class AnimationKeyframe
    {
        public float Time { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float RotX { get; set; }
        public float RotY { get; set; }
        public float RotZ { get; set; }
        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;
        public float ScaleZ { get; set; } = 1f;
    }

    // Curvas de suavizado (presets) aplicadas a la interpolación entre keyframes.
    public enum AnimationEasing
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
        Smooth,
        Back,
        Bounce,
        Elastic
    }

    public static class AnimationEase
    {
        public static float Apply(AnimationEasing e, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            switch (e)
            {
                case AnimationEasing.Linear: return t;
                case AnimationEasing.EaseIn: return t * t * t;
                case AnimationEasing.EaseOut: return 1f - MathF.Pow(1f - t, 3f);
                case AnimationEasing.EaseInOut:
                    return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
                case AnimationEasing.Smooth: // smootherstep
                    return t * t * t * (t * (6f * t - 15f) + 10f);
                case AnimationEasing.Back:
                {
                    const float c1 = 1.70158f;
                    const float c2 = c1 * 1.525f;
                    return t < 0.5f
                        ? (MathF.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2)) / 2f
                        : (MathF.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) / 2f;
                }
                case AnimationEasing.Bounce:
                    return BounceOut(t);
                case AnimationEasing.Elastic:
                {
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    const float c4 = (2f * MathF.PI) / 3f;
                    return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * c4) + 1f;
                }
                default: return t;
            }
        }

        private static float BounceOut(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1; return n1 * t * t + 0.984375f;
        }
    }

    public class AnimationClipData
    {
        public string Name { get; set; } = "New Animation";
        public bool Loop { get; set; } = true;
        public AnimationEasing Easing { get; set; } = AnimationEasing.EaseInOut;
        public List<AnimationKeyframe> Keyframes { get; set; } = new();

        // When an .anim is extracted from an imported FBX/model, keep it as a
        // lightweight Unity-style clip that references the embedded skeletal clip.
        public string SourceModelPath { get; set; } = "";
        public string SourceClipName { get; set; } = "";
        public string AvatarPath { get; set; } = "";
        public bool Humanoid { get; set; } = false;
        public bool LoopPose { get; set; } = false;
        public float CycleOffset { get; set; } = 0f;
        public bool BakeRootRotationIntoPose { get; set; } = false;
        public string RootRotationBasedUpon { get; set; } = "Body Orientation";
        public float RootRotationOffset { get; set; } = 0f;
        public bool BakeRootPositionYIntoPose { get; set; } = false;
        public string RootPositionYBasedUpon { get; set; } = "Original";
        public float RootPositionYOffset { get; set; } = 0f;
        public bool BakeRootPositionXZIntoPose { get; set; } = false;
        public string RootPositionXZBasedUpon { get; set; } = "Center Of Mass";
        public bool Mirror { get; set; } = false;
        public bool AdditiveReferencePose { get; set; } = false;

        public void Normalize()
        {
            Name ??= "New Animation";
            Keyframes ??= new List<AnimationKeyframe>();
            SourceModelPath ??= "";
            SourceClipName ??= "";
            AvatarPath ??= "";
            RootRotationBasedUpon ??= "Body Orientation";
            RootPositionYBasedUpon ??= "Original";
            RootPositionXZBasedUpon ??= "Center Of Mass";
            if (!float.IsFinite(CycleOffset)) CycleOffset = 0f;
            if (!float.IsFinite(RootRotationOffset)) RootRotationOffset = 0f;
            if (!float.IsFinite(RootPositionYOffset)) RootPositionYOffset = 0f;
        }
    }

    public static class AnimationClipAsset
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        // Caché de clips ya cargados (clave: ruta → datos + fecha de escritura). Evita
        // releer/parsear el .anim desde disco en cada frame, p.ej. en los Blend Trees,
        // donde LoadBlendClip se llama varias veces por frame por cada hijo del árbol.
        private static readonly ConcurrentDictionary<string, (AnimationClipData Data, DateTime Write)> _cache = new();
        private const int MaxCached = 256;

        public static bool IsAnimationPath(string path) =>
            !string.IsNullOrWhiteSpace(path) && path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase);

        public static bool IsPlayableAnimationPath(string path) =>
            !string.IsNullOrWhiteSpace(path) && (IsAnimationPath(path) || ObjLoader.IsSupportedMesh(path));

        public static string Create(string directory, string baseName = "New Animation")
        {
            Directory.CreateDirectory(directory);
            string path = GetUniquePath(Path.Combine(directory, baseName + ".anim"));
            var data = new AnimationClipData
            {
                Name = Path.GetFileNameWithoutExtension(path)
            };
            Save(path, data);
            return path;
        }

        // Los clips importados de FBX (Mixamo, etc.) suelen llamarse "Armature|Idle" o
        // "metarig|Walk". La barra vertical '|' —y otros caracteres como \ / : * ? " < > |—
        // son ILEGALES en nombres de archivo de Windows, así que File.WriteAllText reventaba
        // el editor al extraer el clip. Se descarta el prefijo de la armadura (lo de antes del
        // último '|', convención de Mixamo/Unity) y se sustituye cualquier carácter inválido.
        private static string SanitizeClipFileName(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = fallback;

            int bar = name.LastIndexOf('|');
            if (bar >= 0 && bar < name.Length - 1)
                name = name.Substring(bar + 1);

            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            name = name.Trim().Trim('.');

            return string.IsNullOrWhiteSpace(name)
                ? (string.IsNullOrWhiteSpace(fallback) ? "Clip" : fallback)
                : name;
        }

        public static string CreateFromModelClip(string directory, string modelPath, string clipName, string avatarPath, bool humanoid, ModelImportSettings settings)
        {
            Directory.CreateDirectory(directory);
            settings.Normalize();
            string safeName = string.IsNullOrWhiteSpace(clipName)
                ? Path.GetFileNameWithoutExtension(modelPath)
                : clipName;
            safeName = SanitizeClipFileName(safeName, Path.GetFileNameWithoutExtension(modelPath));
            string path = GetUniquePath(Path.Combine(directory, safeName + ".anim"));
            var data = new AnimationClipData
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Loop = settings.LoopTime,
                SourceModelPath = modelPath,
                SourceClipName = clipName,
                AvatarPath = avatarPath,
                Humanoid = humanoid,
                LoopPose = settings.LoopPose,
                CycleOffset = settings.CycleOffset,
                BakeRootRotationIntoPose = settings.BakeRootRotationIntoPose,
                RootRotationBasedUpon = settings.RootRotationBasedUpon,
                RootRotationOffset = settings.RootRotationOffset,
                BakeRootPositionYIntoPose = settings.BakeRootPositionYIntoPose,
                RootPositionYBasedUpon = settings.RootPositionYBasedUpon,
                RootPositionYOffset = settings.RootPositionYOffset,
                BakeRootPositionXZIntoPose = settings.BakeRootPositionXZIntoPose,
                RootPositionXZBasedUpon = settings.RootPositionXZBasedUpon,
                Mirror = settings.Mirror,
                AdditiveReferencePose = settings.AdditiveReferencePose
            };
            Save(path, data);
            return path;
        }

        public static AnimationClipData Load(string path)
        {
            try
            {
                DateTime write = File.GetLastWriteTimeUtc(path);
                if (_cache.TryGetValue(path, out var cached) && cached.Write == write)
                    return cached.Data;

                var text = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<AnimationClipData>(text) ?? new AnimationClipData();
                data.Normalize();
                ResolveAssetPaths(data, path);

                if (_cache.Count >= MaxCached) _cache.Clear();
                _cache[path] = (data, write);
                return data;
            }
            catch (Exception ex)
            {
                // No avisar si simplemente no existe (caso normal: referencia opcional sin asignar).
                if (File.Exists(path))
                    Debug.LogWarning($"[AnimationClipAsset] No se pudo cargar '{path}': {ex.Message}");
                return new AnimationClipData { Name = Path.GetFileNameWithoutExtension(path) };
            }
        }

        public static void Save(string path, AnimationClipData data)
        {
            data.Normalize();
            data.Name = string.IsNullOrWhiteSpace(data.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : data.Name;
            data.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));

            // SourceModelPath/AvatarPath se guardan como referencia relativa (guid:...;path:...),
            // igual que MaterialAsset, para que el .anim no se rompa si se mueve la carpeta del
            // proyecto. Se relativiza solo para el JSON escrito a disco; el objeto en memoria (y
            // lo que queda en cache) se queda con las rutas absolutas resueltas, que es lo que
            // esperan Animator.cs/Animator.Skeletal.cs al leer data.SourceModelPath directamente.
            string originalSourceModelPath = data.SourceModelPath;
            string originalAvatarPath = data.AvatarPath;
            try
            {
                RelativizeAssetPaths(data, path);
                File.WriteAllText(path, JsonSerializer.Serialize(data, Options));
            }
            finally
            {
                data.SourceModelPath = originalSourceModelPath;
                data.AvatarPath = originalAvatarPath;
            }

            // Mantén el caché coherente: la instancia recién guardada pasa a ser la cacheada.
            try { _cache[path] = (data, File.GetLastWriteTimeUtc(path)); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnimationClipAsset] No se pudo actualizar cache para '{path}': {ex.Message}");
            }
        }

        private static void RelativizeAssetPaths(AnimationClipData data, string clipPath)
        {
            string? assetsRoot = InferAssetsRoot(clipPath);
            data.SourceModelPath = AssetDatabase.SerializeReference(data.SourceModelPath, assetsRoot);
            data.AvatarPath = AssetDatabase.SerializeReference(data.AvatarPath, assetsRoot);
        }

        private static void ResolveAssetPaths(AnimationClipData data, string clipPath)
        {
            string? assetsRoot = InferAssetsRoot(clipPath);
            data.SourceModelPath = AssetDatabase.ResolveReference(data.SourceModelPath, assetsRoot) ?? "";
            data.AvatarPath = AssetDatabase.ResolveReference(data.AvatarPath, assetsRoot) ?? "";
        }

        private static string? InferAssetsRoot(string filePath)
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "");
            while (dir != null)
            {
                if (string.Equals(dir.Name, "Assets", StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }

        private static string GetUniquePath(string desiredPath)
        {
            if (!File.Exists(desiredPath)) return desiredPath;

            string directory = Path.GetDirectoryName(desiredPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(desiredPath);
            string extension = Path.GetExtension(desiredPath);
            int i = 1;
            string candidate;
            do candidate = Path.Combine(directory, $"{name}_{i++}{extension}");
            while (File.Exists(candidate));
            return candidate;
        }
    }
}
