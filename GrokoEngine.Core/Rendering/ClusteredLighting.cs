using System;

namespace GrokoEngine;

/// <summary>
/// Configuracion base para Forward+/clustered lighting.
/// No cambia el render actual todavia: es la base testeable para partir el frustum en clusters
/// y luego asignar luces por tile/slice sin limites pequenos por objeto.
/// </summary>
public readonly record struct ClusteredLightingSettings(
    int TileSize,
    int DepthSlices,
    int MaxLightsPerCluster)
{
    public static readonly ClusteredLightingSettings Default = new(
        TileSize: 16,
        DepthSlices: 24,
        MaxLightsPerCluster: 64);

    public ClusteredLightingSettings Normalize() => new(
        Math.Clamp(TileSize, 8, 64),
        Math.Clamp(DepthSlices, 4, 64),
        Math.Clamp(MaxLightsPerCluster, 8, 512));
}

public readonly record struct ClusteredLightingGrid(
    int TileCountX,
    int TileCountY,
    int DepthSlices,
    int ClusterCount);

public static class ClusteredLighting
{
    public static ClusteredLightingGrid ComputeGrid(int renderWidth, int renderHeight, ClusteredLightingSettings settings)
    {
        settings = settings.Normalize();
        int w = Math.Max(1, renderWidth);
        int h = Math.Max(1, renderHeight);
        int tilesX = (w + settings.TileSize - 1) / settings.TileSize;
        int tilesY = (h + settings.TileSize - 1) / settings.TileSize;
        return new ClusteredLightingGrid(tilesX, tilesY, settings.DepthSlices, tilesX * tilesY * settings.DepthSlices);
    }

    public static int ComputeClusterIndex(
        int pixelX,
        int pixelY,
        float viewDepth,
        int renderWidth,
        int renderHeight,
        float nearClip,
        float farClip,
        ClusteredLightingSettings settings)
    {
        settings = settings.Normalize();
        var grid = ComputeGrid(renderWidth, renderHeight, settings);
        int tileX = Math.Clamp(pixelX / settings.TileSize, 0, grid.TileCountX - 1);
        int tileY = Math.Clamp(pixelY / settings.TileSize, 0, grid.TileCountY - 1);
        int slice = ComputeDepthSlice(viewDepth, nearClip, farClip, grid.DepthSlices);
        return (slice * grid.TileCountY + tileY) * grid.TileCountX + tileX;
    }

    public static int ComputeDepthSlice(float viewDepth, float nearClip, float farClip, int depthSlices)
    {
        int slices = Math.Clamp(depthSlices, 1, 128);
        float near = Math.Max(0.001f, nearClip);
        float far = Math.Max(near + 0.001f, farClip);
        float depth = Math.Clamp(MathF.Abs(viewDepth), near, far);

        // Distribucion logaritmica: mas precision cerca de camara, igual que clustered/Forward+
        // moderno. Es estable aunque near/far cambien mucho.
        float t = MathF.Log(depth / near) / MathF.Log(far / near);
        return Math.Clamp((int)MathF.Floor(t * slices), 0, slices - 1);
    }
}
