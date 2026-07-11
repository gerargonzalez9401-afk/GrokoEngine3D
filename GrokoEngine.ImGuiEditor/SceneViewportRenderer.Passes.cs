using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private readonly record struct RenderPassDescriptor(
        SceneRenderPass Pass,
        string Name,
        string Stage,
        string Description);

    private enum SceneRenderPass
    {
        BuildScene,
        Shadows,
        Skybox,
        StaticOpaque,
        DynamicOpaque,
        ShaderGraph,
        Terrain,
        Occlusion,
        LinesGizmos,
        Particles
    }

    // Fuente unica del orden del pipeline. El render loop debe seguir este orden para mantener
    // profiler, Render Debug y futuras opciones de pipeline sincronizadas.
    private static readonly RenderPassDescriptor[] RenderPipelineLayout =
    [
        new(SceneRenderPass.BuildScene, "Build/Collect", "CPU",
            "Recolecta objetos visibles, rutas de malla, instancias, terrenos, particulas y lineas."),
        new(SceneRenderPass.Shadows, "Shadow Pass", "Depth",
            "Genera mapas de sombra direccionales, spot, point y depth del terreno."),
        new(SceneRenderPass.Skybox, "Skybox", "Background",
            "Dibuja HDRI o cielo procedural antes de la geometria opaca."),
        new(SceneRenderPass.StaticOpaque, "Static Opaque", "Opaque",
            "Dibuja batches estaticos opacos."),
        new(SceneRenderPass.DynamicOpaque, "Dynamic/Skinned Opaque", "Opaque",
            "Dibuja mallas dinamicas, skinned meshes y rangos opacos no estaticos."),
        new(SceneRenderPass.ShaderGraph, "Shader Graph", "Material",
            "Dibuja materiales custom generados por Shader Graph y su prepass de depth si aplica."),
        new(SceneRenderPass.Terrain, "Terrain/CDLOD", "Terrain",
            "Dibuja terrenos CDLOD, arboles billboard y detalles."),
        new(SceneRenderPass.Occlusion, "Occlusion Queries", "Visibility",
            "Actualiza consultas de oclusion despues de la geometria opaca."),
        new(SceneRenderPass.LinesGizmos, "Gizmos/Lines", "Editor",
            "Dibuja gizmos, bounds, helpers de editor y lineas."),
        new(SceneRenderPass.Particles, "Particles/Transparent", "Transparent",
            "Dibuja particulas transparentes ordenadas y efectos con distorsion/refraction.")
    ];

    private static IReadOnlyList<RenderPassDescriptor> GetRenderPipelineLayout() => RenderPipelineLayout;

    private static long BeginRenderPass(SceneRenderPass pass) => RenderTimestamp();

    private float EndRenderPass(SceneRenderPass pass, long start)
    {
        float elapsedMs = RenderElapsedMs(start);
        SetRenderPassMs(pass, elapsedMs);
        return elapsedMs;
    }

    private void SetRenderPassMs(SceneRenderPass pass, float elapsedMs)
    {
        switch (pass)
        {
            case SceneRenderPass.BuildScene:
                _statsBuildSceneMs = elapsedMs;
                break;
            case SceneRenderPass.Shadows:
                _statsShadowMs = elapsedMs;
                break;
            case SceneRenderPass.Skybox:
                _statsSkyboxMs = elapsedMs;
                break;
            case SceneRenderPass.StaticOpaque:
                _statsStaticOpaqueMs = elapsedMs;
                break;
            case SceneRenderPass.DynamicOpaque:
                _statsDynamicOpaqueMs = elapsedMs;
                break;
            case SceneRenderPass.ShaderGraph:
                _statsShaderGraphMs = elapsedMs;
                break;
            case SceneRenderPass.Terrain:
                _statsTerrainMs = elapsedMs;
                break;
            case SceneRenderPass.Occlusion:
                _statsOcclusionMs = elapsedMs;
                break;
            case SceneRenderPass.LinesGizmos:
                _statsLinesGizmosMs = elapsedMs;
                break;
            case SceneRenderPass.Particles:
                _statsParticlesMs = elapsedMs;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pass), pass, null);
        }
    }
}
