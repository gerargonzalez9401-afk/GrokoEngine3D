using System;
using System.Diagnostics;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private static long RenderTimestamp() => Stopwatch.GetTimestamp();

    private static float RenderElapsedMs(long start) =>
        (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private void ResetRenderStats()
    {
        _statsDrawCalls = 0;
        _statsShadowDrawCalls = 0;
        _statsInstancedDrawCalls = 0;
        _statsInstances = 0;
        _statsTriangles = 0;
        _statsLineDrawCalls = 0;
        _statsParticleDrawCalls = 0;
        _statsBuildSceneMs = 0f;
        _statsShadowMs = 0f;
        _statsSkyboxMs = 0f;
        _statsStaticOpaqueMs = 0f;
        _statsDynamicOpaqueMs = 0f;
        _statsShaderGraphMs = 0f;
        _statsTerrainMs = 0f;
        _statsLinesGizmosMs = 0f;
        _statsParticlesMs = 0f;
        _statsOcclusionMs = 0f;
        _statsDirectionalShadowMs = 0f;
        _statsSpotShadowMs = 0f;
        _statsPointShadowMs = 0f;
        _statsRenderOtherMs = 0f;
    }

    private void TrackMainDraw(int vertexCount, int instanceCount = 1)
    {
        _statsDrawCalls++;
        if (instanceCount > 1)
        {
            _statsInstancedDrawCalls++;
            _statsInstances += instanceCount;
        }

        _statsTriangles += Math.Max(0, vertexCount / 3) * Math.Max(1, instanceCount);
    }

    private void TrackLineDraw()
    {
        _statsDrawCalls++;
        _statsLineDrawCalls++;
    }

}
