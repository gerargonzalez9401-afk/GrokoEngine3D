using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GrokoEngine
{
    public class MaterialAssetData
    {
        public string Name { get; set; } = "New Material";
        public float R { get; set; } = 0.8f;
        public float G { get; set; } = 0.8f;
        public float B { get; set; } = 0.8f;
        public string TexturePath { get; set; } = "";
        public string AlbedoPath { get; set; } = "";
        public string NormalMapPath { get; set; } = "";
        public string RoughnessMapPath { get; set; } = "";
        public string MetallicMapPath { get; set; } = "";
        public string ShaderGraphPath { get; set; } = "";
        public Dictionary<string, float[]> ShaderGraphProperties { get; set; } = new();
        public Dictionary<string, string> ShaderGraphTextures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public float Roughness { get; set; } = 0.5f;
        public float Metallic { get; set; } = 0f;
        public float EmissionR { get; set; } = 0f;
        public float EmissionG { get; set; } = 0f;
        public float EmissionB { get; set; } = 0f;
        public float EmissionIntensity { get; set; } = 0f;

        public void Normalize()
        {
            Name ??= "New Material";
            TexturePath ??= "";
            AlbedoPath ??= "";
            NormalMapPath ??= "";
            RoughnessMapPath ??= "";
            MetallicMapPath ??= "";
            ShaderGraphPath ??= "";
            ShaderGraphProperties ??= new Dictionary<string, float[]>();
            ShaderGraphTextures ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static class MaterialAsset
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        public static bool IsMaterialPath(string path) =>
            path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);

        public static bool IsTexturePath(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga";
        }

        public static string Create(string directory, string baseName = "New Material")
        {
            Directory.CreateDirectory(directory);
            string path = GetUniquePath(Path.Combine(directory, baseName + ".mat"));
            var data = new MaterialAssetData
            {
                Name = Path.GetFileNameWithoutExtension(path),
                R = 0.82f,
                G = 0.82f,
                B = 0.78f
            };
            Save(path, data);
            return path;
        }

        // Crea un .mat a partir de los datos embebidos de una sub-malla importada (color/textura del FBX/OBJ).
        public static string CreateFromImported(string directory, string baseName, float r, float g, float b, string? texturePath)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, baseName + ".mat");
            if (File.Exists(path))
                return path;

            var data = new MaterialAssetData
            {
                Name = Path.GetFileNameWithoutExtension(path),
                R = Clamp01(r),
                G = Clamp01(g),
                B = Clamp01(b),
                AlbedoPath = texturePath ?? "",
                TexturePath = texturePath ?? ""
            };
            Save(path, data);
            return path;
        }

        public static MaterialAssetData Load(string path)
        {
            try
            {
                var text = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<MaterialAssetData>(text) ?? new MaterialAssetData();
                data.Normalize();
                if (string.IsNullOrWhiteSpace(data.AlbedoPath) && !string.IsNullOrWhiteSpace(data.TexturePath))
                    data.AlbedoPath = data.TexturePath;
                ResolveAssetPaths(data, path);
                data.TexturePath = data.AlbedoPath;
                return data;
            }
            catch
            {
                return new MaterialAssetData { Name = Path.GetFileNameWithoutExtension(path) };
            }
        }

        public static void Save(string path, MaterialAssetData data)
        {
            data.Normalize();
            data.Name = string.IsNullOrWhiteSpace(data.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : data.Name;
            if (string.IsNullOrWhiteSpace(data.AlbedoPath) && !string.IsNullOrWhiteSpace(data.TexturePath))
                data.AlbedoPath = data.TexturePath;
            RelativizeAssetPaths(data, path);
            data.TexturePath = data.AlbedoPath;
            File.WriteAllText(path, JsonSerializer.Serialize(data, Options));
            EnsureMetaIfInsideAssets(path);
        }

        public static void ApplyTo(GameObject obj, string path)
        {
            var data = Load(path);
            var mat = obj.GetComponent<Material>() ?? obj.AddComponent<Material>();
            mat.R = Clamp01(data.R);
            mat.G = Clamp01(data.G);
            mat.B = Clamp01(data.B);
            mat.AssetPath = path;
            mat.TexturePath = GetAlbedo(data);
            mat.NormalMapPath = data.NormalMapPath;
            mat.RoughnessMapPath = data.RoughnessMapPath;
            mat.MetallicMapPath = data.MetallicMapPath;
            mat.ShaderGraphPath = data.ShaderGraphPath;
            mat.ShaderGraphProperties = new Dictionary<string, float[]>(data.ShaderGraphProperties, StringComparer.OrdinalIgnoreCase);
            mat.ShaderGraphTextures = new Dictionary<string, string>(data.ShaderGraphTextures, StringComparer.OrdinalIgnoreCase);
            mat.Roughness = Clamp01(data.Roughness);
            mat.Metallic = Clamp01(data.Metallic);
            mat.EmissionR = Clamp01(data.EmissionR);
            mat.EmissionG = Clamp01(data.EmissionG);
            mat.EmissionB = Clamp01(data.EmissionB);
            mat.EmissionIntensity = Math.Max(0f, data.EmissionIntensity);
            mat.IsInstance = false;
        }

        public static void SaveFromMaterial(Material material)
        {
            if (string.IsNullOrWhiteSpace(material.AssetPath) || !File.Exists(material.AssetPath)) return;
            Save(material.AssetPath, new MaterialAssetData
            {
                Name = Path.GetFileNameWithoutExtension(material.AssetPath),
                R = material.R,
                G = material.G,
                B = material.B,
                TexturePath = material.TexturePath,
                AlbedoPath = material.TexturePath,
                NormalMapPath = material.NormalMapPath,
                RoughnessMapPath = material.RoughnessMapPath,
                MetallicMapPath = material.MetallicMapPath,
                ShaderGraphPath = material.ShaderGraphPath,
                ShaderGraphProperties = new Dictionary<string, float[]>(material.ShaderGraphProperties, StringComparer.OrdinalIgnoreCase),
                ShaderGraphTextures = new Dictionary<string, string>(material.ShaderGraphTextures, StringComparer.OrdinalIgnoreCase),
                Roughness = material.Roughness,
                Metallic = material.Metallic,
                EmissionR = material.EmissionR,
                EmissionG = material.EmissionG,
                EmissionB = material.EmissionB,
                EmissionIntensity = material.EmissionIntensity
            });
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

        public static string GetAlbedo(MaterialAssetData data) =>
            string.IsNullOrWhiteSpace(data.AlbedoPath) ? data.TexturePath : data.AlbedoPath;

        private static void RelativizeAssetPaths(MaterialAssetData data, string materialPath)
        {
            string? assetsRoot = InferAssetsRoot(materialPath);
            data.AlbedoPath = SerializeAssetPath(data.AlbedoPath, assetsRoot);
            data.TexturePath = SerializeAssetPath(data.TexturePath, assetsRoot);
            data.NormalMapPath = SerializeAssetPath(data.NormalMapPath, assetsRoot);
            data.RoughnessMapPath = SerializeAssetPath(data.RoughnessMapPath, assetsRoot);
            data.MetallicMapPath = SerializeAssetPath(data.MetallicMapPath, assetsRoot);
            data.ShaderGraphPath = SerializeAssetPath(data.ShaderGraphPath, assetsRoot);
            if (data.ShaderGraphTextures.Count > 0)
                data.ShaderGraphTextures = data.ShaderGraphTextures.ToDictionary(
                    kv => kv.Key,
                    kv => SerializeAssetPath(kv.Value, assetsRoot),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static void ResolveAssetPaths(MaterialAssetData data, string materialPath)
        {
            string? assetsRoot = InferAssetsRoot(materialPath);
            data.AlbedoPath = ResolveAssetPath(data.AlbedoPath, assetsRoot);
            data.TexturePath = ResolveAssetPath(data.TexturePath, assetsRoot);
            data.NormalMapPath = ResolveAssetPath(data.NormalMapPath, assetsRoot);
            data.RoughnessMapPath = ResolveAssetPath(data.RoughnessMapPath, assetsRoot);
            data.MetallicMapPath = ResolveAssetPath(data.MetallicMapPath, assetsRoot);
            data.ShaderGraphPath = ResolveAssetPath(data.ShaderGraphPath, assetsRoot);
            if (data.ShaderGraphTextures.Count > 0)
                data.ShaderGraphTextures = data.ShaderGraphTextures.ToDictionary(
                    kv => kv.Key,
                    kv => ResolveAssetPath(kv.Value, assetsRoot),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string SerializeAssetPath(string? path, string? assetsRoot)
            => AssetDatabase.SerializeReference(path, assetsRoot);

        private static string ResolveAssetPath(string? path, string? assetsRoot)
            => AssetDatabase.ResolveReference(path, assetsRoot) ?? "";

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

        private static string NormalizeFullPath(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static void EnsureMetaIfInsideAssets(string path)
        {
            string? assetsRoot = InferAssetsRoot(path);
            if (string.IsNullOrWhiteSpace(assetsRoot) || !File.Exists(path))
                return;

            AssetDatabase.Get(assetsRoot).GetOrCreateGuid(path);
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
