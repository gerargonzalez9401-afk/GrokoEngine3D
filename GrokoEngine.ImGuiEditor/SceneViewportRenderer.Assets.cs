using System;
using System.IO;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private static string? NormalizeTexturePath(string? path) => NormalizeExistingAssetPath(path);

    internal static string? NormalizeExistingAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            string fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"[SceneViewportRenderer] Ruta de asset invalida '{path}': {ex.Message}");
            return null;
        }
    }

    private void LogAssetWarning(string message)
    {
        if (assetWarningLog.Add(message))
            GrokoEngine.Debug.LogWarning(message);
    }
}
