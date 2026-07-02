using System;
using System.IO;
using System.Text.Json;

namespace GrokoEngine
{
    public class CubemapAssetData
    {
        public string Name { get; set; } = "New Cubemap";
        public string SourceTexturePath { get; set; } = "";

        public void Normalize()
        {
            Name ??= "New Cubemap";
            SourceTexturePath ??= "";
        }
    }

    /// <summary>
    /// Asset tipo Unity: Create > Cubemap crea un .cubemap vacío en el que se arrastra una
    /// textura HDRI equirectangular. El asset en sí solo guarda la referencia a esa textura;
    /// el motor sigue usando la textura equirect directamente (no genera 6 caras), así que
    /// asignar este Cubemap al slot "HDRI Environment" resuelve a SourceTexturePath.
    /// </summary>
    public static class CubemapAsset
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        public static bool IsCubemapPath(string path) =>
            path.EndsWith(".cubemap", StringComparison.OrdinalIgnoreCase);

        public static string Create(string directory, string baseName = "New Cubemap")
        {
            Directory.CreateDirectory(directory);
            string path = GetUniquePath(Path.Combine(directory, baseName + ".cubemap"));
            var data = new CubemapAssetData { Name = Path.GetFileNameWithoutExtension(path) };
            Save(path, data);
            return path;
        }

        public static CubemapAssetData Load(string path)
        {
            try
            {
                var text = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<CubemapAssetData>(text) ?? new CubemapAssetData();
                data.Normalize();
                ResolveAssetPaths(data, path);
                return data;
            }
            catch
            {
                return new CubemapAssetData { Name = Path.GetFileNameWithoutExtension(path) };
            }
        }

        public static void Save(string path, CubemapAssetData data)
        {
            data.Normalize();
            data.Name = string.IsNullOrWhiteSpace(data.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : data.Name;
            RelativizeAssetPaths(data, path);
            File.WriteAllText(path, JsonSerializer.Serialize(data, Options));
            EnsureMetaIfInsideAssets(path);
        }

        private static void RelativizeAssetPaths(CubemapAssetData data, string cubemapPath)
        {
            string? assetsRoot = InferAssetsRoot(cubemapPath);
            data.SourceTexturePath = AssetDatabase.SerializeReference(data.SourceTexturePath, assetsRoot);
        }

        private static void ResolveAssetPaths(CubemapAssetData data, string cubemapPath)
        {
            string? assetsRoot = InferAssetsRoot(cubemapPath);
            data.SourceTexturePath = AssetDatabase.ResolveReference(data.SourceTexturePath, assetsRoot) ?? "";
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
