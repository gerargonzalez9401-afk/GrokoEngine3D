namespace GrokoEngine.ImGuiEditor;

/// <summary>
/// Espacio de color en el que trabaja el pipeline de render — equivalente a
/// "Color Space" en Unity (Project Settings → Player → Other Settings).
/// </summary>
public enum ColorSpace
{
    Linear,
    Gamma
}

/// <summary>
/// Nivel de calidad de las sombras dinámicas, equivalente a "Shadow Resolution"
/// en Unity (Project Settings → Quality).
/// </summary>
public enum ShadowQuality
{
    Low,
    Medium,
    High,
    Ultra
}

internal readonly record struct SceneRenderStats(
    int DrawCalls,
    int ShadowDrawCalls,
    int InstancedDrawCalls,
    int Instances,
    long Triangles,
    int StaticRanges,
    int SolidRanges,
    int DynamicRanges,
    int LineDrawCalls,
    int ParticleDrawCalls,
    int TextureCacheCount,
    int GpuMeshCacheCount,
    int ParsedMeshCacheCount,
    int ShaderGraphCacheCount,
    int RenderWidth,
    int RenderHeight,
    float BuildSceneMs,
    float ShadowMs,
    float SkyboxMs,
    float StaticOpaqueMs,
    float DynamicOpaqueMs,
    float ShaderGraphMs,
    float TerrainMs,
    float LinesGizmosMs,
    float ParticlesMs,
    float OcclusionMs,
    float DirectionalShadowMs,
    float SpotShadowMs,
    float PointShadowMs,
    float RenderOtherMs)
{
    public static readonly SceneRenderStats Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
