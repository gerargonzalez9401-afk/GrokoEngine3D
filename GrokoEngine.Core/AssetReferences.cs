using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrokoEngine;

/// <summary>
/// Runtime-only asset reference codec. It intentionally knows only the stable
/// GUID sidecar format, not the editor asset database or import pipeline.
/// </summary>
public static class AssetReferences
{
    private const string GuidPrefix = "guid:";
    private const string PathSeparator = ";path:";
    // La deserialización de una escena grande puede resolver cientos de rutas con GUID.
    // Sin este índice, cada una recorría todos los .meta de Assets desde el disco.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> GuidIndices =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Descarta el índice de GUID de un proyecto tras modificar sus assets o .meta.</summary>
    public static void InvalidateCache(string? assetsRoot)
    {
        if (string.IsNullOrWhiteSpace(assetsRoot))
            return;

        try
        {
            GuidIndices.TryRemove(NormalizeFullPath(assetsRoot), out _);
        }
        catch
        {
            // La siguiente resolución puede seguir usando el fallback path de la escena.
        }
    }

    public static string Serialize(string? path, string? assetsRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(assetsRoot))
            return path ?? "";
        if (TryParse(path, out _, out _))
            return path;

        string root = NormalizeFullPath(assetsRoot);
        string fullPath = Path.IsPathRooted(path)
            ? NormalizeFullPath(path)
            : NormalizeFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        string relative = IsInsideRoot(fullPath, root)
            ? Path.GetRelativePath(root, fullPath).Replace('\\', '/')
            : path.Replace('\\', '/');

        if (!IsInsideRoot(fullPath, root) || !Exists(fullPath) || fullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            return relative;

        return $"{GuidPrefix}{GetOrCreateGuid(fullPath)}{PathSeparator}{relative}";
    }

    public static string? Resolve(string? reference, string? assetsRoot)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return reference;
        if (!TryParse(reference, out string guid, out string fallback))
            return ResolvePath(reference, assetsRoot);

        if (!string.IsNullOrWhiteSpace(assetsRoot) && TryResolveGuid(assetsRoot, guid, out string resolved))
            return resolved;
        return ResolvePath(fallback, assetsRoot);
    }

    public static string GetOrCreateGuid(string assetPath)
    {
        string fullPath = NormalizeFullPath(assetPath);
        if (!Exists(fullPath))
            throw new FileNotFoundException("Asset or folder not found.", fullPath);

        string metaPath = fullPath + ".meta";
        JsonObject root = new();
        try
        {
            if (File.Exists(metaPath) && JsonNode.Parse(File.ReadAllText(metaPath)) is JsonObject parsed)
                root = parsed;
        }
        catch
        {
            // Corrupt metadata is repaired with a new identifier rather than trusted.
        }

        string guid = NormalizeGuid(root["Guid"]?.GetValue<string>());
        if (string.IsNullOrWhiteSpace(guid))
        {
            guid = Guid.NewGuid().ToString("N");
            root["Guid"] = guid;
            AtomicFile.WriteAllText(metaPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        return guid;
    }

    private static bool TryResolveGuid(string assetsRoot, string guid, out string resolvedPath)
    {
        resolvedPath = "";
        string normalizedGuid = NormalizeGuid(guid);
        if (string.IsNullOrWhiteSpace(normalizedGuid) || !Directory.Exists(assetsRoot))
            return false;

        string root = NormalizeFullPath(assetsRoot);
        var index = GuidIndices.GetOrAdd(root, BuildGuidIndex);
        if (index.TryGetValue(normalizedGuid, out string? indexedPath))
        {
            resolvedPath = indexedPath;
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> BuildGuidIndex(string assetsRoot)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string metaPath in Directory.EnumerateFiles(assetsRoot, "*.meta", SearchOption.AllDirectories))
        {
            string owner = metaPath[..^5];
            if (!Exists(owner))
                continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
                if (document.RootElement.TryGetProperty("Guid", out var property) &&
                    !string.IsNullOrWhiteSpace(NormalizeGuid(property.GetString())))
                {
                    string indexedGuid = NormalizeGuid(property.GetString());
                    // Conserva el primer dueño, que coincide con la resolución previa por
                    // recorrido. Las colisiones se corrigen en AssetDatabase.Refresh.
                    index.TryAdd(indexedGuid, NormalizeFullPath(owner));
                }
            }
            catch (JsonException)
            {
                // Ignore malformed sidecars; their fallback path still remains usable.
            }
            catch (IOException)
            {
                // Un sincronizador puede bloquear transitoriamente un .meta; el fallback
                // path de la escena permite continuar sin abortar la carga.
            }
        }

        return index;
    }

    private static bool TryParse(string value, out string guid, out string fallbackPath)
    {
        guid = "";
        fallbackPath = "";
        if (!value.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        int separator = value.IndexOf(PathSeparator, StringComparison.OrdinalIgnoreCase);
        guid = NormalizeGuid(separator < 0 ? value[GuidPrefix.Length..] : value[GuidPrefix.Length..separator]);
        fallbackPath = separator < 0 ? "" : value[(separator + PathSeparator.Length)..];
        return !string.IsNullOrWhiteSpace(guid);
    }

    private static string NormalizeGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return Guid.TryParse(value.Trim(), out Guid guid)
            ? guid.ToString("N")
            : new string(value.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
    }

    private static string? ResolvePath(string? path, string? assetsRoot) =>
        string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(assetsRoot)
            ? path
            : Path.GetFullPath(Path.Combine(assetsRoot, path.Replace('/', Path.DirectorySeparatorChar)));

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static string NormalizeFullPath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static bool IsInsideRoot(string path, string root) =>
        string.Equals(NormalizeFullPath(path), NormalizeFullPath(root), StringComparison.OrdinalIgnoreCase) ||
        NormalizeFullPath(path).StartsWith(NormalizeFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
