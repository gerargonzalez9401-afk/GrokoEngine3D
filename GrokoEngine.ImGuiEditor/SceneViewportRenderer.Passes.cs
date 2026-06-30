using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
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
