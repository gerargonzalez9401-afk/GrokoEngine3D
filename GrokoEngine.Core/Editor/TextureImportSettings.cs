using System;
using System.IO;

namespace GrokoEngine
{
    public sealed class TextureImportSettings
    {
        public string TextureType { get; set; } = "Default";
        public string TextureShape { get; set; } = "2D";
        public bool SRgb { get; set; } = true;
        public string AlphaSource { get; set; } = "Input Texture Alpha";
        public bool AlphaIsTransparency { get; set; } = false;
        public string WrapMode { get; set; } = "Repeat";
        public string FilterMode { get; set; } = "Bilinear";
        public int AnisoLevel { get; set; } = 1;
        public int MaxSize { get; set; } = 2048;
        public string ResizeAlgorithm { get; set; } = "Mitchell";
        public string Format { get; set; } = "Automatic";
        public string Compression { get; set; } = "Normal Quality";
        public bool UseCrunchCompression { get; set; } = false;

        public bool IsNormalMap =>
            TextureType.Equals("Normal Map", StringComparison.OrdinalIgnoreCase) ||
            TextureType.Equals("NormalMap", StringComparison.OrdinalIgnoreCase);

        public void Normalize(string assetPath = "")
        {
            TextureType = NormalizeChoice(TextureType, "Default");
            TextureShape = NormalizeChoice(TextureShape, "2D");
            AlphaSource = NormalizeChoice(AlphaSource, "Input Texture Alpha");
            WrapMode = NormalizeChoice(WrapMode, "Repeat");
            FilterMode = NormalizeChoice(FilterMode, "Bilinear");
            ResizeAlgorithm = NormalizeChoice(ResizeAlgorithm, "Mitchell");
            Format = NormalizeChoice(Format, "Automatic");
            Compression = NormalizeChoice(Compression, "Normal Quality");
            AnisoLevel = Math.Clamp(AnisoLevel, 0, 16);
            MaxSize = Math.Clamp(MaxSize <= 0 ? 2048 : MaxSize, 32, 16384);

            if (IsNormalMap)
            {
                TextureType = "Normal Map";
                SRgb = false;
                AlphaIsTransparency = false;
            }
            else if (string.IsNullOrWhiteSpace(TextureType))
            {
                TextureType = LooksLikeNormalMap(assetPath) ? "Normal Map" : "Default";
                SRgb = !IsNormalMap;
            }
        }

        public static TextureImportSettings CreateDefault(string assetPath)
        {
            bool normal = LooksLikeNormalMap(assetPath);
            var settings = new TextureImportSettings
            {
                TextureType = normal ? "Normal Map" : "Default",
                SRgb = !normal,
                AlphaIsTransparency = false
            };
            settings.Normalize(assetPath);
            return settings;
        }

        public static bool LooksLikeNormalMap(string? assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            name = name.Replace('-', '_').Replace(' ', '_').Replace('.', '_');
            string padded = "_" + name + "_";

            return padded.Contains("_normal_", StringComparison.Ordinal) ||
                   padded.Contains("_normalmap_", StringComparison.Ordinal) ||
                   padded.Contains("_nrm_", StringComparison.Ordinal) ||
                   padded.Contains("_nrml_", StringComparison.Ordinal) ||
                   padded.Contains("_nor_", StringComparison.Ordinal) ||
                   padded.Contains("_nm_", StringComparison.Ordinal) ||
                   padded.Contains("_bump_", StringComparison.Ordinal);
        }

        private static string NormalizeChoice(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public static class TextureImportSettingsAsset
    {
        public static TextureImportSettings Load(string texturePath)
        {
            string? assetsRoot = InferAssetsRoot(texturePath);
            if (!string.IsNullOrWhiteSpace(assetsRoot) && File.Exists(texturePath))
                return AssetDatabase.Get(assetsRoot).GetOrCreateTextureSettings(texturePath);

            return TextureImportSettings.CreateDefault(texturePath);
        }

        public static void Save(string texturePath, TextureImportSettings settings)
        {
            string? assetsRoot = InferAssetsRoot(texturePath);
            if (string.IsNullOrWhiteSpace(assetsRoot) || !File.Exists(texturePath))
                return;

            AssetDatabase.Get(assetsRoot).SaveTextureSettings(texturePath, settings);
        }

        public static bool EnsureNormalMap(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath) || !File.Exists(texturePath) || !MaterialAsset.IsTexturePath(texturePath))
                return false;

            var settings = Load(texturePath);
            bool changed = !settings.IsNormalMap || settings.SRgb;
            settings.TextureType = "Normal Map";
            settings.SRgb = false;
            settings.AlphaIsTransparency = false;
            settings.Normalize(texturePath);
            Save(texturePath, settings);
            return changed;
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
    }
}
