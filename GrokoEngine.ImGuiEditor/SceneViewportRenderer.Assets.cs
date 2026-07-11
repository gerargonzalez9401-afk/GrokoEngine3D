using System;
using System.IO;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private static string? NormalizeTexturePath(string? path) => NormalizeExistingAssetPath(path);

    // Memoización con TTL: esto corre en caminos MUY calientes (por slot de material de cada
    // objeto Static por frame vía ObjectUsesTransparentMaterial, y 4 veces por draw dinámico
    // para las texturas). Sin cache, cada llamada era GetFullPath + File.Exists — cientos de
    // consultas de disco por frame que ESCALAN con el número de objetos (brutal en OneDrive).
    // La existencia de un asset cambia raras veces; 4 s de retardo máximo es aceptable, y
    // InvalidateNormalizedAssetPath() permite refresco inmediato al guardar/importar.
    private const long NormalizedPathTtlMs = 4000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string? Normalized, long ExpiresAt)> normalizedPathCache =
        new(StringComparer.OrdinalIgnoreCase);

    internal static string? NormalizeExistingAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        long now = Environment.TickCount64;
        if (normalizedPathCache.TryGetValue(path, out var cached) && now < cached.ExpiresAt)
            return cached.Normalized;

        string? normalized;
        try
        {
            string fullPath = Path.GetFullPath(path);
            normalized = File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"[SceneViewportRenderer] Ruta de asset invalida '{path}': {ex.Message}");
            normalized = null;
        }

        // Tope de seguridad para sesiones largas con muchos assets distintos.
        if (normalizedPathCache.Count > 4096)
            normalizedPathCache.Clear();

        normalizedPathCache[path] = (normalized, now + NormalizedPathTtlMs);
        return normalized;
    }

    /// <summary>Olvida la resolución cacheada de una ruta (llamar al guardar/importar/borrar un asset).</summary>
    internal static void InvalidateNormalizedAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        normalizedPathCache.TryRemove(path, out _);
    }

    private void LogAssetWarning(string message)
    {
        if (assetWarningLog.Add(message))
            GrokoEngine.Debug.LogWarning(message);
    }
}
