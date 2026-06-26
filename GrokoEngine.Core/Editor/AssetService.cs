using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GrokoEngine
{
    public sealed class AssetImportResult
    {
        public int ImportedCount { get; set; }
        public bool ImportedScripts { get; set; }
        public bool ImportedMeshes { get; set; }
        public List<string> ImportedPaths { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }

    public sealed class AssetService
    {
        private readonly string rootAssetsPath;

        public AssetDatabase AssetDatabase { get; }

        public AssetService(string rootAssetsPath)
        {
            this.rootAssetsPath = rootAssetsPath;
            AssetDatabase = GrokoEngine.AssetDatabase.Get(rootAssetsPath);
        }

        public bool IsSupportedObjectAssetPath(string? path)
        {
            return path != null &&
                   (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                    ObjLoader.IsSupportedMesh(path) ||
                    MaterialAsset.IsMaterialPath(path) ||
                    MaterialAsset.IsTexturePath(path) ||
                    ScriptableObjectAsset.IsAssetPath(path));
        }

        public bool ApplyMaterial(GameObject obj, string materialPath)
        {
            if (!IsExistingMaterial(materialPath)) return false;
            MaterialAsset.ApplyTo(obj, materialPath);
            return true;
        }

        public bool ApplyTexture(GameObject obj, string texturePath)
        {
            if (!IsExistingTexture(texturePath)) return false;
            var mat = obj.GetComponent<Material>() ?? obj.AddComponent<Material>();
            mat.TexturePath = texturePath;
            mat.AssetPath = "";
            return true;
        }

        public bool AssignMesh(GameObject obj, string meshPath, out ParsedMesh? mesh)
        {
            mesh = null;
            if (!File.Exists(meshPath) || !ObjLoader.IsSupportedMesh(meshPath)) return false;
            ObjLoader.InvalidateCache(meshPath);
            mesh = ObjLoader.Load(meshPath);
            if (mesh == null) return false;
            var filter = obj.GetComponent<MeshFilter>() ?? obj.AddComponent<MeshFilter>();
            filter.MeshPath = meshPath;
            return true;
        }

        public string CreatePrefab(string directory, GameObject obj)
        {
            string prefabPath = GetUniqueAssetPath(Path.Combine(directory, SanitizeFileName(obj.Name) + ".prefab"));
            SceneSerializer.SavePrefab(prefabPath, obj);
            AssetDatabase.GetOrCreateGuid(prefabPath);
            return prefabPath;
        }

        public AssetImportResult ImportExternalFiles(IEnumerable<string> paths, string targetDirectory)
        {
            var result = new AssetImportResult();
            Directory.CreateDirectory(targetDirectory);

            foreach (var source in paths)
            {
                try
                {
                    if (Directory.Exists(source))
                    {
                        string target = GetUniqueAssetPath(Path.Combine(targetDirectory, Path.GetFileName(source)));
                        CopyDirectory(source, target);
                        result.ImportedCount++;
                        result.ImportedPaths.Add(target);
                        result.ImportedScripts |= Directory.GetFiles(target, "*.cs", SearchOption.AllDirectories).Length > 0;
                        result.ImportedMeshes |= Directory.GetFiles(target, "*.*", SearchOption.AllDirectories).Any(ObjLoader.IsSupportedMesh);
                    }
                    else if (File.Exists(source))
                    {
                        string target = GetUniqueAssetPath(Path.Combine(targetDirectory, Path.GetFileName(source)));
                        File.Copy(source, target);
                        result.ImportedCount++;
                        result.ImportedPaths.Add(target);
                        result.ImportedScripts |= target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
                        result.ImportedMeshes |= ObjLoader.IsSupportedMesh(target);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"No se pudo importar '{Path.GetFileName(source)}': {ex.Message}");
                }
            }

            if (result.ImportedMeshes)
            {
                foreach (var mesh in result.ImportedPaths.SelectMany(EnumerateMeshes))
                    ObjLoader.InvalidateCache(mesh);
            }

            EnsureImportedMetas(result.ImportedPaths);

            return result;
        }

        public string ImportExternalMeshToAssets(string sourcePath, string importDirectory)
        {
            string fullSource = Path.GetFullPath(sourcePath);
            string fullAssets = NormalizeFullPath(rootAssetsPath);
            if (fullSource.StartsWith(fullAssets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return sourcePath;

            Directory.CreateDirectory(importDirectory);
            string target = GetUniqueAssetPath(Path.Combine(importDirectory, Path.GetFileName(sourcePath)));
            File.Copy(sourcePath, target);
            ObjLoader.InvalidateCache(target);
            AssetDatabase.GetOrCreateGuid(target);
            return target;
        }

        public bool MoveAsset(string sourcePath, string destinationPath, out string error)
            => AssetDatabase.MoveAsset(sourcePath, destinationPath, out error);

        public bool DeleteAsset(string assetPath, out string error)
            => AssetDatabase.DeleteAsset(assetPath, out error);

        public AssetDatabaseValidationReport ValidateAndRepairAssetDatabase(bool removeOrphanMetaFiles = false)
            => AssetDatabase.ValidateAndRepair(removeOrphanMetaFiles);

        public string GetUniqueAssetPath(string desiredPath)
        {
            if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath)) return desiredPath;

            string directory = Path.GetDirectoryName(desiredPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(desiredPath);
            string extension = Path.GetExtension(desiredPath);
            int i = 1;
            string candidate;
            do candidate = Path.Combine(directory, $"{name}_{i++}{extension}");
            while (File.Exists(candidate) || Directory.Exists(candidate));
            return candidate;
        }

        private static bool IsExistingMaterial(string path) =>
            MaterialAsset.IsMaterialPath(path) && File.Exists(path);

        private static bool IsExistingTexture(string path) =>
            MaterialAsset.IsTexturePath(path) && File.Exists(path);

        private static IEnumerable<string> EnumerateMeshes(string path)
        {
            if (File.Exists(path) && ObjLoader.IsSupportedMesh(path)) return new[] { path };
            if (!Directory.Exists(path)) return Array.Empty<string>();
            return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(ObjLoader.IsSupportedMesh);
        }

        private void EnsureImportedMetas(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    if (!GrokoEngine.AssetDatabase.IsMetaPath(path))
                        AssetDatabase.GetOrCreateGuid(path);
                    continue;
                }

                if (!Directory.Exists(path))
                    continue;

                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (!GrokoEngine.AssetDatabase.IsMetaPath(file))
                        AssetDatabase.GetOrCreateGuid(file);
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string sanitized = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Prefab" : sanitized;
        }

        private static string NormalizeFullPath(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
