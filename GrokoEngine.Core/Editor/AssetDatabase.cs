using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrokoEngine
{
    public sealed class AssetMetaData
    {
        public string Guid { get; set; } = "";
        public string Type { get; set; } = "";
        public string Importer { get; set; } = "";
        public string CreatedUtc { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TextureImportSettings? TextureSettings { get; set; }

        public void Normalize(string assetPath)
        {
            Guid = NormalizeGuid(Guid);
            if (string.IsNullOrWhiteSpace(Guid))
                Guid = NewGuid();

            Type = string.IsNullOrWhiteSpace(Type) ? AssetDatabase.GetAssetType(assetPath) : Type;
            Importer = string.IsNullOrWhiteSpace(Importer) ? AssetDatabase.GetImporterName(assetPath) : Importer;
            CreatedUtc = string.IsNullOrWhiteSpace(CreatedUtc) ? DateTime.UtcNow.ToString("O") : CreatedUtc;
            if (Type.Equals("Texture", StringComparison.OrdinalIgnoreCase))
            {
                TextureSettings ??= TextureImportSettings.CreateDefault(assetPath);
                TextureSettings.Normalize(assetPath);
            }
            else
            {
                TextureSettings = null;
            }
        }

        internal static string NewGuid() => System.Guid.NewGuid().ToString("N");

        internal static string NormalizeGuid(string? guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return "";

            string trimmed = guid.Trim();
            return System.Guid.TryParse(trimmed, out var parsed)
                ? parsed.ToString("N")
                : new string(trimmed.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        }
    }

    public sealed class AssetDatabaseValidationReport
    {
        public int AssetCount { get; set; }
        public int CreatedOrRepairedMetaFiles { get; set; }
        public int RemovedOrphanMetaFiles { get; set; }
        public List<string> OrphanMetaFiles { get; } = new();
        public List<string> Errors { get; } = new();

        public bool HasProblems => OrphanMetaFiles.Count > 0 || Errors.Count > 0;
    }

    public sealed class AssetDatabase
    {
        private const string GuidPrefix = "guid:";
        private const string PathSeparator = ";path:";
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
        private static readonly Dictionary<string, AssetDatabase> Shared = new(StringComparer.OrdinalIgnoreCase);

        private readonly string assetsRoot;
        private readonly Dictionary<string, AssetMetaData> metaByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> pathByGuid = new(StringComparer.OrdinalIgnoreCase);

        public AssetDatabase(string assetsRoot)
        {
            this.assetsRoot = NormalizeFullPath(assetsRoot);
            Directory.CreateDirectory(this.assetsRoot);
        }

        public string AssetsRoot => assetsRoot;

        public static AssetDatabase Get(string assetsRoot)
        {
            string key = NormalizeFullPath(assetsRoot);
            if (!Shared.TryGetValue(key, out var database))
            {
                database = new AssetDatabase(key);
                Shared[key] = database;
            }

            return database;
        }

        public void Refresh(bool createMissingMeta = true)
        {
            metaByPath.Clear();
            pathByGuid.Clear();

            if (!Directory.Exists(assetsRoot))
                return;

            // Unity-style: folders own .meta files too. Track folders before files so
            // references to folders can resolve by GUID and folder moves preserve identity.
            foreach (string path in Directory.EnumerateDirectories(assetsRoot, "*", SearchOption.AllDirectories))
            {
                if (!ShouldTrackAssetPath(path))
                    continue;

                if (createMissingMeta)
                    EnsureMeta(path);
                else
                    TryLoadMeta(path, out _);
            }

            foreach (string path in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                if (IsMetaPath(path) || !ShouldTrackAssetPath(path))
                    continue;

                if (createMissingMeta)
                    EnsureMeta(path);
                else
                    TryLoadMeta(path, out _);
            }
        }

        public string GetOrCreateGuid(string assetPath)
        {
            var meta = EnsureMeta(assetPath);
            return meta.Guid;
        }

        public TextureImportSettings GetOrCreateTextureSettings(string assetPath)
        {
            var meta = EnsureMeta(assetPath);
            if (!meta.Type.Equals("Texture", StringComparison.OrdinalIgnoreCase))
                return TextureImportSettings.CreateDefault(assetPath);

            meta.TextureSettings ??= TextureImportSettings.CreateDefault(assetPath);
            meta.TextureSettings.Normalize(assetPath);
            SaveMetaFile(GetMetaPath(NormalizeFullPath(assetPath)), meta);
            Index(NormalizeFullPath(assetPath), meta);
            return CloneTextureSettings(meta.TextureSettings);
        }

        public void SaveTextureSettings(string assetPath, TextureImportSettings settings)
        {
            string fullPath = NormalizeFullPath(assetPath);
            if (!File.Exists(fullPath))
                return;

            var meta = EnsureMeta(fullPath);
            meta.Type = "Texture";
            meta.Importer = "TextureImporter";
            settings.Normalize(fullPath);
            meta.TextureSettings = CloneTextureSettings(settings);
            meta.Normalize(fullPath);
            SaveMetaFile(GetMetaPath(fullPath), meta);
            Index(fullPath, meta);
        }

        public bool TryGetGuid(string assetPath, out string guid)
        {
            guid = "";
            string fullPath = NormalizeFullPath(assetPath);

            if (metaByPath.TryGetValue(fullPath, out var cached))
            {
                guid = cached.Guid;
                return true;
            }

            if (!AssetExists(fullPath) || !TryLoadMeta(fullPath, out var meta))
                return false;

            guid = meta.Guid;
            return true;
        }

        public bool TryResolveGuid(string guid, out string assetPath)
        {
            assetPath = "";
            guid = AssetMetaData.NormalizeGuid(guid);
            if (string.IsNullOrWhiteSpace(guid))
                return false;

            if (pathByGuid.TryGetValue(guid, out var cachedPath) && AssetExists(cachedPath))
            {
                assetPath = cachedPath;
                return true;
            }

            Refresh(createMissingMeta: false);
            if (pathByGuid.TryGetValue(guid, out var refreshedPath) && AssetExists(refreshedPath))
            {
                assetPath = refreshedPath;
                return true;
            }

            return false;
        }

        public AssetMetaData EnsureMeta(string assetPath)
        {
            string fullPath = NormalizeFullPath(assetPath);
            if (IsMetaPath(fullPath))
                throw new ArgumentException("Meta files cannot own another meta file.", nameof(assetPath));
            if (!ShouldTrackAssetPath(fullPath))
                throw new ArgumentException("Editor-generated files cannot own asset metadata.", nameof(assetPath));

            if (!AssetExists(fullPath))
                throw new FileNotFoundException("Asset or folder not found.", fullPath);

            if (metaByPath.TryGetValue(fullPath, out var cached))
                return cached;

            string metaPath = GetMetaPath(fullPath);
            AssetMetaData meta;
            if (File.Exists(metaPath))
            {
                meta = LoadMetaFile(metaPath);
                meta.Normalize(fullPath);
            }
            else
            {
                meta = new AssetMetaData();
                meta.Normalize(fullPath);
            }

            if (pathByGuid.TryGetValue(meta.Guid, out var existingPath) &&
                !string.Equals(existingPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                meta.Guid = AssetMetaData.NewGuid();
            }

            SaveMetaFile(metaPath, meta);
            Index(fullPath, meta);
            return meta;
        }

        public static string SerializeReference(string? path, string? assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(assetsRoot))
                return path ?? "";
            if (TryParseReference(path, out _, out _))
                return path;

            string root = NormalizeFullPath(assetsRoot);
            string fullPath = Path.IsPathRooted(path)
                ? NormalizeFullPath(path)
                : NormalizeFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

            string relative = IsInsideRoot(fullPath, root)
                ? NormalizeAssetPath(Path.GetRelativePath(root, fullPath))
                : path.Replace('\\', '/');

            if (!AssetExists(fullPath) || IsMetaPath(fullPath) || !IsInsideRoot(fullPath, root) || !ShouldTrackAssetPath(fullPath))
                return relative;

            string guid = Get(root).GetOrCreateGuid(fullPath);
            return $"{GuidPrefix}{guid}{PathSeparator}{relative}";
        }

        public static string? ResolveReference(string? reference, string? assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return reference;

            if (!TryParseReference(reference, out string guid, out string fallbackPath))
                return ResolvePath(reference, assetsRoot);

            if (!string.IsNullOrWhiteSpace(assetsRoot) && Get(assetsRoot).TryResolveGuid(guid, out string resolved))
                return resolved;

            return ResolvePath(fallbackPath, assetsRoot);
        }

        public bool MoveAsset(string sourcePath, string destinationPath, out string error)
        {
            error = "";
            string source = NormalizeFullPath(sourcePath);
            string destination = NormalizeFullPath(destinationPath);

            if (!IsInsideRoot(source, assetsRoot) || !IsInsideRoot(destination, assetsRoot))
            {
                error = "Source and destination must be inside Assets.";
                return false;
            }
            if (IsMetaPath(source) || IsMetaPath(destination))
            {
                error = "Meta files are managed automatically.";
                return false;
            }
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                error = "Destination already exists.";
                return false;
            }

            try
            {
                if (Directory.Exists(source))
                {
                    if (IsInsideRoot(destination, source))
                    {
                        error = "Cannot move a folder inside itself.";
                        return false;
                    }

                    EnsureMeta(source); // garantiza GUID de carpeta antes de mover.
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Directory.Move(source, destination);

                    string oldMeta = GetMetaPath(source);
                    string newMeta = GetMetaPath(destination);
                    if (File.Exists(oldMeta))
                    {
                        if (File.Exists(newMeta))
                            File.Delete(newMeta);
                        File.Move(oldMeta, newMeta);
                    }
                }
                else if (File.Exists(source))
                {
                    EnsureMeta(source); // garantiza GUID antes de mover.
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(source, destination);

                    string oldMeta = GetMetaPath(source);
                    string newMeta = GetMetaPath(destination);
                    if (File.Exists(oldMeta))
                    {
                        if (File.Exists(newMeta))
                            File.Delete(newMeta);
                        File.Move(oldMeta, newMeta);
                    }
                }
                else
                {
                    error = "Source asset does not exist.";
                    return false;
                }

                Refresh(createMissingMeta: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Refresh(createMissingMeta: false);
                return false;
            }
        }

        public bool DeleteAsset(string assetPath, out string error)
        {
            error = "";
            string fullPath = NormalizeFullPath(assetPath);
            if (!IsInsideRoot(fullPath, assetsRoot) || string.Equals(fullPath, assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "Asset must be inside Assets and cannot be the root folder.";
                return false;
            }
            if (IsMetaPath(fullPath))
            {
                error = "Delete the asset, not its meta file.";
                return false;
            }

            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    string metaPath = GetMetaPath(fullPath);
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                }
                else if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                    string metaPath = GetMetaPath(fullPath);
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                }
                else
                {
                    error = "Asset does not exist.";
                    return false;
                }

                Refresh(createMissingMeta: false);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Refresh(createMissingMeta: false);
                return false;
            }
        }

        public AssetDatabaseValidationReport ValidateAndRepair(bool removeOrphanMetaFiles = false)
        {
            var report = new AssetDatabaseValidationReport();
            metaByPath.Clear();
            pathByGuid.Clear();

            if (!Directory.Exists(assetsRoot))
                return report;

            foreach (string path in EnumerateTrackableAssets(includeFolders: true))
            {
                report.AssetCount++;
                string metaPath = GetMetaPath(path);
                bool existed = File.Exists(metaPath);
                try
                {
                    EnsureMeta(path);
                    if (!existed)
                        report.CreatedOrRepairedMetaFiles++;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{path}: {ex.Message}");
                }
            }

            foreach (string metaPath in Directory.EnumerateFiles(assetsRoot, "*.meta", SearchOption.AllDirectories))
            {
                string owner = metaPath[..^5];
                if (File.Exists(owner) || Directory.Exists(owner))
                    continue;

                if (removeOrphanMetaFiles)
                {
                    try
                    {
                        File.Delete(metaPath);
                        report.RemovedOrphanMetaFiles++;
                    }
                    catch (Exception ex)
                    {
                        report.Errors.Add($"{metaPath}: {ex.Message}");
                    }
                }
                else
                {
                    report.OrphanMetaFiles.Add(metaPath);
                }
            }

            Refresh(createMissingMeta: false);
            return report;
        }

        public static bool IsMetaPath(string path) =>
            path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);

        public static string GetMetaPath(string assetPath) => assetPath + ".meta";

        public static string GetAssetType(string path)
        {
            if (Directory.Exists(path))
                return "Folder";

            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".mat" => "Material",
                ".prefab" => "Prefab",
                ".gscene" => "Scene",
                ".shadergraph" => "ShaderGraph",
                ".cs" => "Script",
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" => "Texture",
                ".obj" or ".fbx" or ".dae" or ".blend" or ".3ds" or ".gltf" or ".glb" => "Model",
                ".asset" => "ScriptableObject",
                _ => "Asset"
            };
        }

        public static string GetImporterName(string path) =>
            Directory.Exists(path) ? "FolderImporter" : GetAssetType(path) + "Importer";


        private IEnumerable<string> EnumerateTrackableAssets(bool includeFolders)
        {
            if (includeFolders)
            {
                foreach (string path in Directory.EnumerateDirectories(assetsRoot, "*", SearchOption.AllDirectories))
                {
                    if (ShouldTrackAssetPath(path))
                        yield return path;
                }
            }

            foreach (string path in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                if (IsMetaPath(path) || !ShouldTrackAssetPath(path))
                    continue;
                yield return path;
            }
        }

        private static bool AssetExists(string path) => File.Exists(path) || Directory.Exists(path);

        private bool TryLoadMeta(string assetPath, out AssetMetaData meta)
        {
            string fullPath = NormalizeFullPath(assetPath);
            string metaPath = GetMetaPath(fullPath);
            if (!File.Exists(metaPath))
            {
                meta = new AssetMetaData();
                return false;
            }

            meta = LoadMetaFile(metaPath);
            meta.Normalize(fullPath);
            Index(fullPath, meta);
            return true;
        }

        private void Index(string assetPath, AssetMetaData meta)
        {
            string fullPath = NormalizeFullPath(assetPath);
            metaByPath[fullPath] = meta;
            pathByGuid[meta.Guid] = fullPath;
        }

        private static AssetMetaData LoadMetaFile(string metaPath)
        {
            try
            {
                return JsonSerializer.Deserialize<AssetMetaData>(File.ReadAllText(metaPath), Options) ?? new AssetMetaData();
            }
            catch
            {
                return new AssetMetaData();
            }
        }

        private static void SaveMetaFile(string metaPath, AssetMetaData meta)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, Options));
        }

        private static bool TryParseReference(string reference, out string guid, out string fallbackPath)
        {
            guid = "";
            fallbackPath = "";
            if (!reference.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            int pathIndex = reference.IndexOf(PathSeparator, StringComparison.OrdinalIgnoreCase);
            if (pathIndex < 0)
            {
                guid = AssetMetaData.NormalizeGuid(reference[GuidPrefix.Length..]);
                return !string.IsNullOrWhiteSpace(guid);
            }

            guid = AssetMetaData.NormalizeGuid(reference.Substring(GuidPrefix.Length, pathIndex - GuidPrefix.Length));
            fallbackPath = reference[(pathIndex + PathSeparator.Length)..];
            return !string.IsNullOrWhiteSpace(guid);
        }

        private static string? ResolvePath(string? path, string? assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;
            if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(assetsRoot))
                return path;

            return Path.GetFullPath(Path.Combine(assetsRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool IsInsideRoot(string fullPath, string root)
        {
            string normalizedPath = NormalizeFullPath(fullPath);
            string normalizedRoot = NormalizeFullPath(root);
            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldTrackAssetPath(string path)
        {
            if (IsEditorHiddenPath(path))
                return false;

            string ext = Path.GetExtension(path);
            if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".user", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals(".vs", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static bool IsEditorHiddenPath(string path)
        {
            string file = Path.GetFileName(path);
            return file.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
                   file.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFullPath(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string NormalizeAssetPath(string path) => path.Replace('\\', '/');

        private static TextureImportSettings CloneTextureSettings(TextureImportSettings source) => new()
        {
            TextureType = source.TextureType,
            TextureShape = source.TextureShape,
            SRgb = source.SRgb,
            AlphaSource = source.AlphaSource,
            AlphaIsTransparency = source.AlphaIsTransparency,
            WrapMode = source.WrapMode,
            FilterMode = source.FilterMode,
            AnisoLevel = source.AnisoLevel,
            MaxSize = source.MaxSize,
            ResizeAlgorithm = source.ResizeAlgorithm,
            Format = source.Format,
            Compression = source.Compression,
            UseCrunchCompression = source.UseCrunchCompression
        };
    }
}
