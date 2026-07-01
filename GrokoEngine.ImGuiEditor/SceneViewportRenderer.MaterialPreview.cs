using GrokoEngine;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    // Vista previa de material en arrastre (estilo Unity): mientras se arrastra un .mat
    // sobre el viewport, el objeto/sub-malla bajo el cursor muestra ese material sin soltarlo.
    public string? PreviewMaterialObjectId;
    public int PreviewMaterialSubmeshIndex = -1; // -1 = objeto de un solo material (componente Material)
    public string? PreviewMaterialAssetPath;
    private string? previewMaterialDataPath;
    private DateTime previewMaterialDataFileTime;
    private MaterialAssetData? previewMaterialData;
    private readonly Dictionary<string, (DateTime WriteTime, int Frame, MaterialAssetData Data)> materialAssetRenderCache = new(StringComparer.OrdinalIgnoreCase);

    private MaterialAssetData? GetPreviewOverrideData(GameObject obj, int submeshIndex)
    {
        if (string.IsNullOrWhiteSpace(PreviewMaterialAssetPath) ||
            PreviewMaterialObjectId != obj.EditorId ||
            PreviewMaterialSubmeshIndex != submeshIndex ||
            !System.IO.File.Exists(PreviewMaterialAssetPath))
            return null;
        return GetPreviewMaterialData(PreviewMaterialAssetPath);
    }

    private MaterialAssetData? GetPreviewMaterialData(string path)
    {
        DateTime fileTime;
        try
        {
            fileTime = System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"[SceneViewportRenderer] No se pudo leer fecha del material preview '{path}': {ex.Message}");
            return null;
        }

        if (previewMaterialData != null &&
            previewMaterialDataFileTime == fileTime &&
            string.Equals(previewMaterialDataPath, path, StringComparison.OrdinalIgnoreCase))
            return previewMaterialData;

        previewMaterialDataPath = path;
        previewMaterialDataFileTime = fileTime;
        previewMaterialData = MaterialAsset.Load(path);
        return previewMaterialData;
    }

    private MaterialAssetData? GetCachedMaterialAssetData(string? path)
    {
        string? normalized = NormalizeExistingAssetPath(path);
        if (string.IsNullOrWhiteSpace(normalized) || !System.IO.File.Exists(normalized))
            return null;

        const int refreshEveryFrames = 30;
        if (materialAssetRenderCache.TryGetValue(normalized, out var cached) &&
            _frameCount - cached.Frame < refreshEveryFrames)
            return cached.Data;

        DateTime fileTime;
        try
        {
            fileTime = System.IO.File.GetLastWriteTimeUtc(normalized);
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"[SceneViewportRenderer] No se pudo leer material '{normalized}': {ex.Message}");
            return cached.Data;
        }

        if (cached.Data != null && cached.WriteTime == fileTime)
        {
            materialAssetRenderCache[normalized] = (fileTime, _frameCount, cached.Data);
            return cached.Data;
        }

        if (materialAssetRenderCache.Count > 512)
            materialAssetRenderCache.Clear();

        var data = MaterialAsset.Load(normalized);
        materialAssetRenderCache[normalized] = (fileTime, _frameCount, data);
        return data;
    }

    private Vector4 GetObjectColor(GameObject obj, Vector4 fallback)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return new Vector4(Math.Clamp(over.R, 0f, 1f), Math.Clamp(over.G, 0f, 1f), Math.Clamp(over.B, 0f, 1f), Math.Clamp(over.Alpha, 0f, 1f));

        var material = obj.GetComponent<Material>();
        return material == null
            ? fallback
            : new Vector4(material.R, material.G, material.B, Math.Clamp(material.Alpha, 0f, 1f));
    }

    private string? GetObjectTexturePath(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return string.IsNullOrWhiteSpace(MaterialAsset.GetAlbedo(over)) ? null : MaterialAsset.GetAlbedo(over);

        var material = obj.GetComponent<Material>();
        return material?.TexturePath;
    }

    private (Vector4 Material, Vector4 Emission) GetObjectSurface(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return (
                new Vector4(Math.Clamp(over.Metallic, 0f, 1f), Math.Clamp(over.Roughness, 0.04f, 1f), over.SpecularHighlights ? 1f : 0f, over.EnvironmentReflections ? 1f : 0f),
                new Vector4(Math.Max(0f, over.EmissionR), Math.Max(0f, over.EmissionG), Math.Max(0f, over.EmissionB), Math.Max(0f, over.EmissionIntensity)));

        var material = obj.GetComponent<Material>();
        if (material == null)
            return (new Vector4(0f, 0.5f, 1f, 1f), Vector4.Zero);

        return (
            new Vector4(Math.Clamp(material.Metallic, 0f, 1f), Math.Clamp(material.Roughness, 0.04f, 1f), material.SpecularHighlights ? 1f : 0f, material.EnvironmentReflections ? 1f : 0f),
            new Vector4(Math.Max(0f, material.EmissionR), Math.Max(0f, material.EmissionG), Math.Max(0f, material.EmissionB), Math.Max(0f, material.EmissionIntensity)));
    }

    private SurfaceMaps GetObjectSurfaceMaps(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return new SurfaceMaps(over.NormalMapPath, over.RoughnessMapPath, over.MetallicMapPath);

        var material = obj.GetComponent<Material>();
        return material == null
            ? new SurfaceMaps(null, null, null)
            : new SurfaceMaps(material.NormalMapPath, material.RoughnessMapPath, material.MetallicMapPath);
    }

    private SurfaceTransparency GetObjectTransparency(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return new SurfaceTransparency(over.SurfaceType, Math.Clamp(over.Alpha, 0f, 1f), Math.Clamp(over.RefractionStrength, 0f, 1f), Math.Clamp(over.DistortionStrength, 0f, 1f));

        var material = obj.GetComponent<Material>();
        return material == null
            ? SurfaceTransparency.Opaque
            : new SurfaceTransparency(material.SurfaceType, Math.Clamp(material.Alpha, 0f, 1f), Math.Clamp(material.RefractionStrength, 0f, 1f), Math.Clamp(material.DistortionStrength, 0f, 1f));
    }

    private bool ObjectUsesTransparentMaterial(GameObject obj, MeshFilter? meshFilter)
    {
        if (GetObjectTransparency(obj).IsTransparent)
            return true;

        if (meshFilter == null || meshFilter.MaterialSlots.Count == 0)
            return false;

        for (int i = 0; i < meshFilter.MaterialSlots.Count; i++)
        {
            string? slotPath = meshFilter.MaterialSlots[i];
            if (!string.IsNullOrWhiteSpace(PreviewMaterialAssetPath) &&
                PreviewMaterialObjectId == obj.EditorId &&
                PreviewMaterialSubmeshIndex == i)
                slotPath = PreviewMaterialAssetPath;

            var data = GetCachedMaterialAssetData(slotPath);
            if (data == null)
                continue;

            var transparency = new SurfaceTransparency(
                data.SurfaceType,
                Math.Clamp(data.Alpha, 0f, 1f),
                Math.Clamp(data.RefractionStrength, 0f, 1f),
                Math.Clamp(data.DistortionStrength, 0f, 1f));

            if (transparency.IsTransparent)
                return true;
        }

        return false;
    }

    private readonly record struct SubmeshSurface(
        Vector4 Color,
        string? TexturePath,
        SurfaceMaps Maps,
        Vector4 Material,
        Vector4 Emission,
        SurfaceTransparency Transparency,
        string? ShaderGraphPath,
        IReadOnlyDictionary<string, float[]>? ShaderGraphProperties,
        IReadOnlyDictionary<string, string>? ShaderGraphTextures);

    // Resuelve el material de una sub-malla: si tiene un .mat asignado en MaterialSlots
    // se usa ese; si no, se usan los datos embebidos del modelo importado (color/textura del FBX/OBJ).
    private SubmeshSurface GetSubmeshSurface(string? materialAssetPath, MeshSubmesh submesh, Vector4 fallbackColor)
    {
        var data = GetCachedMaterialAssetData(materialAssetPath);
        if (data != null)
        {
            string? albedo = MaterialAsset.GetAlbedo(data);
            return new SubmeshSurface(
                new Vector4(Math.Clamp(data.R, 0f, 1f), Math.Clamp(data.G, 0f, 1f), Math.Clamp(data.B, 0f, 1f), Math.Clamp(data.Alpha, 0f, 1f)),
                string.IsNullOrWhiteSpace(albedo) ? null : albedo,
                new SurfaceMaps(data.NormalMapPath, data.RoughnessMapPath, data.MetallicMapPath),
                new Vector4(Math.Clamp(data.Metallic, 0f, 1f), Math.Clamp(data.Roughness, 0.04f, 1f), data.SpecularHighlights ? 1f : 0f, data.EnvironmentReflections ? 1f : 0f),
                new Vector4(Math.Max(0f, data.EmissionR), Math.Max(0f, data.EmissionG), Math.Max(0f, data.EmissionB), Math.Max(0f, data.EmissionIntensity)),
                new SurfaceTransparency(data.SurfaceType, Math.Clamp(data.Alpha, 0f, 1f), Math.Clamp(data.RefractionStrength, 0f, 1f), Math.Clamp(data.DistortionStrength, 0f, 1f)),
                string.IsNullOrWhiteSpace(data.ShaderGraphPath) ? null : data.ShaderGraphPath,
                data.ShaderGraphProperties.Count > 0 ? data.ShaderGraphProperties : null,
                data.ShaderGraphTextures.Count > 0 ? data.ShaderGraphTextures : null);
        }

        Vector4 color = string.IsNullOrWhiteSpace(submesh.TexturePath) && submesh.DiffuseR == 0.8f && submesh.DiffuseG == 0.8f && submesh.DiffuseB == 0.8f
            ? fallbackColor
            : new Vector4(submesh.DiffuseR, submesh.DiffuseG, submesh.DiffuseB, 1f);

        return new SubmeshSurface(
            color,
            submesh.TexturePath,
            new SurfaceMaps(null, null, null),
            new Vector4(0f, 0.5f, 1f, 1f),
            Vector4.Zero,
            SurfaceTransparency.Opaque,
            null, null, null);
    }
}
