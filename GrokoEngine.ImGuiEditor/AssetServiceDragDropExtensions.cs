using System;
using System.IO;
using System.Linq;

namespace GrokoEngine;

internal static class AssetServiceDragDropExtensions
{
    public static bool TryGetPrefabPath(this AssetService service, System.Windows.IDataObject data, out string prefabPath) =>
        TryGetPath(data, p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase), out prefabPath);

    public static bool TryGetPrefabPath(this AssetService service, System.Windows.Forms.IDataObject? data, out string prefabPath) =>
        TryGetPath(data, p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase), out prefabPath);

    public static bool TryGetMaterialPath(this AssetService service, System.Windows.IDataObject data, out string materialPath) =>
        TryGetPath(data, MaterialAsset.IsMaterialPath, out materialPath);

    public static bool TryGetMaterialPath(this AssetService service, System.Windows.Forms.IDataObject? data, out string materialPath) =>
        TryGetPath(data, MaterialAsset.IsMaterialPath, out materialPath);

    public static bool TryGetTexturePath(this AssetService service, System.Windows.IDataObject data, out string texturePath) =>
        TryGetPath(data, MaterialAsset.IsTexturePath, out texturePath);

    public static bool TryGetTexturePath(this AssetService service, System.Windows.Forms.IDataObject? data, out string texturePath) =>
        TryGetPath(data, MaterialAsset.IsTexturePath, out texturePath);

    public static bool TryGetMeshPath(this AssetService service, System.Windows.IDataObject data, string importDirectory, out string meshPath)
    {
        meshPath = "";
        if (TryGetFileDrop(data, out var files))
        {
            var firstMesh = files.FirstOrDefault(f => File.Exists(f) && ObjLoader.IsSupportedMesh(f));
            if (firstMesh != null)
            {
                meshPath = service.ImportExternalMeshToAssets(firstMesh, importDirectory);
                return true;
            }
        }

        return TryGetPath(data, ObjLoader.IsSupportedMesh, out meshPath);
    }

    public static bool TryGetMeshPath(this AssetService service, System.Windows.Forms.IDataObject? data, string importDirectory, out string meshPath)
    {
        meshPath = "";
        if (data != null &&
            data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop) &&
            data.GetData(System.Windows.Forms.DataFormats.FileDrop) is string[] files)
        {
            var firstMesh = files.FirstOrDefault(f => File.Exists(f) && ObjLoader.IsSupportedMesh(f));
            if (firstMesh != null)
            {
                meshPath = service.ImportExternalMeshToAssets(firstMesh, importDirectory);
                return true;
            }
        }

        return TryGetPath(data, ObjLoader.IsSupportedMesh, out meshPath);
    }

    public static bool IsMeshDragData(this AssetService service, System.Windows.IDataObject data)
    {
        if (TryGetFileDrop(data, out var files) && files.Any(f => File.Exists(f) && ObjLoader.IsSupportedMesh(f)))
            return true;

        var path = GetDragString(data);
        return path != null && ObjLoader.IsSupportedMesh(path) && File.Exists(path);
    }

    public static bool IsMeshDragData(this AssetService service, System.Windows.Forms.IDataObject? data)
    {
        if (data == null) return false;
        if (data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop) &&
            data.GetData(System.Windows.Forms.DataFormats.FileDrop) is string[] files &&
            files.Any(f => File.Exists(f) && ObjLoader.IsSupportedMesh(f)))
            return true;

        var path = GetDragString(data);
        return path != null && ObjLoader.IsSupportedMesh(path) && File.Exists(path);
    }

    public static string? GetDragString(System.Windows.IDataObject data)
    {
        if (data.GetDataPresent(System.Windows.DataFormats.StringFormat))
            return data.GetData(System.Windows.DataFormats.StringFormat)?.ToString();
        if (data.GetDataPresent(System.Windows.DataFormats.Text))
            return data.GetData(System.Windows.DataFormats.Text)?.ToString();
        if (data.GetDataPresent(System.Windows.Forms.DataFormats.StringFormat))
            return data.GetData(System.Windows.Forms.DataFormats.StringFormat)?.ToString();
        if (data.GetDataPresent(System.Windows.Forms.DataFormats.Text))
            return data.GetData(System.Windows.Forms.DataFormats.Text)?.ToString();
        return null;
    }

    public static string? GetDragString(System.Windows.Forms.IDataObject data)
    {
        if (data.GetDataPresent(System.Windows.Forms.DataFormats.StringFormat))
            return data.GetData(System.Windows.Forms.DataFormats.StringFormat)?.ToString();
        if (data.GetDataPresent(System.Windows.Forms.DataFormats.Text))
            return data.GetData(System.Windows.Forms.DataFormats.Text)?.ToString();
        return null;
    }

    private static bool TryGetPath(System.Windows.IDataObject data, Func<string, bool> predicate, out string path)
    {
        path = "";
        var candidate = GetDragString(data);
        if (candidate == null || !predicate(candidate) || !File.Exists(candidate)) return false;
        path = candidate;
        return true;
    }

    private static bool TryGetPath(System.Windows.Forms.IDataObject? data, Func<string, bool> predicate, out string path)
    {
        path = "";
        if (data == null) return false;
        var candidate = GetDragString(data);
        if (candidate == null || !predicate(candidate) || !File.Exists(candidate)) return false;
        path = candidate;
        return true;
    }

    private static bool TryGetFileDrop(System.Windows.IDataObject data, out string[] files)
    {
        files = Array.Empty<string>();
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return false;
        if (data.GetData(System.Windows.DataFormats.FileDrop) is not string[] dropped) return false;
        files = dropped;
        return true;
    }
}
