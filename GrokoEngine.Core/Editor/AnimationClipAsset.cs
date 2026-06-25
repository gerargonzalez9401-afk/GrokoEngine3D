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

        public static string CreateFromModelClip(string directory, string modelPath, string clipName, string avatarPath, bool humanoid, ModelImportSettings settings)
        {
            Directory.CreateDirectory(directory);
            settings.Normalize();
            string safeName = string.IsNullOrWhiteSpace(clipName)
                ? Path.GetFileNameWithoutExtension(modelPath)
                : clipName;
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

                if (_cache.Count >= MaxCached) _cache.Clear();
                _cache[path] = (data, write);
                return data;
            }
            catch
            {
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
            File.WriteAllText(path, JsonSerializer.Serialize(data, Options));
            // Mantén el caché coherente: la instancia recién guardada pasa a ser la cacheada.
            try { _cache[path] = (data, File.GetLastWriteTimeUtc(path)); } catch { }
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
