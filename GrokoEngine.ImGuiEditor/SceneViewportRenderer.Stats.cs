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
        _statsDynamicMeshGroups = 0;
        _statsDynamicMeshSingles = 0;
        _statsDynamicMeshLargestGroup = 0;
        _statsOpaqueDynamicMeshes = 0;
        _statsTransparentDynamicMeshes = 0;
        _statsTransparentSkinnedMeshes = 0;
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
        _statsDeferredGBufferMs = 0f;
        _statsDeferredLightingMs = 0f;
        _statsDeferredDepthCopyMs = 0f;
        _statsDeferredSsgiMs = 0f;
        _statsDeferredSsgiActive = false;
        _statsDeferredSsrMs = 0f;
        _statsRenderOtherMs = 0f;
        _statsTerrainTreeMesh = 0;
        _statsTerrainTreeBillboard = 0;
        _statsTerrainTreeCulled = 0;
        _statsTerrainTreeCells = 0;
        _statsHierarchyLod0 = 0;
        _statsHierarchyLod1 = 0;
        _statsHierarchyLod2 = 0;
        _statsHierarchyLod3 = 0;
        _statsHierarchyLodSkipped = 0;
        _statsHierarchyLodCulled = 0;
        _statsStaticHierarchyLodFast = 0;
        _statsHierarchyLodGpuReplays = 0;
        _statsHierarchyLodGpuMisses = 0;
        _statsHierarchyLodGpuFrustumCulled = 0;
        _statsHierarchyLodGpuParts = 0;
        _perfBuildGridMs = 0f;
        _perfBuildWalkMs = 0f;
        _perfParticleCollectMs = 0f;
        _perfWalkNodes = 0;
        _perfWalkComponentTicks = 0;
        _perfWalkRouteTicks = 0;
        _perfWalkGeometryTicks = 0;
        Array.Clear(_perfSlowNodes, 0, _perfSlowNodes.Length);
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

    private void TrackDynamicMeshGroup(int instanceCount)
    {
        _statsDynamicMeshGroups++;
        if (instanceCount <= 1)
            _statsDynamicMeshSingles++;
        if (instanceCount > _statsDynamicMeshLargestGroup)
            _statsDynamicMeshLargestGroup = instanceCount;
    }

    private void TrackLineDraw()
    {
        _statsDrawCalls++;
        _statsLineDrawCalls++;
    }

    // ── Log de diagnóstico de rendimiento ────────────────────────────────────
    // Activo SOLO si la variable de entorno GROKO_PERF_LOG apunta a un archivo:
    // una línea por segundo con el desglose de pases y contadores del frame.
    // Coste con la variable ausente: una comparación de bool por frame.
    private static readonly string? PerfLogPath = Environment.GetEnvironmentVariable("GROKO_PERF_LOG");
    private long _perfLogNextTick;
    private float _perfBuildGridMs;
    private float _perfBuildWalkMs;
    private float _perfParticleCollectMs;
    private int _perfBuildSolidVerts;
    private int _perfBuildLineVerts;
    private int _perfWalkNodes;
    private long _perfWalkComponentTicks;
    private long _perfWalkRouteTicks;
    private long _perfWalkGeometryTicks;

    private static float TicksToMs(long ticks) => (float)(ticks * 1000.0 / Stopwatch.Frequency);

    // Top de nodos más lentos del frame (solo con GROKO_PERF_LOG): nombre + ms propios.
    private readonly (string Name, long Ticks)[] _perfSlowNodes = new (string, long)[6];

    private void RecordSlowWalkNode(GameObject obj, long selfTicks)
    {
        if (PerfLogPath == null)
            return;

        int slot = -1;
        long worst = long.MaxValue;
        for (int i = 0; i < _perfSlowNodes.Length; i++)
        {
            if (_perfSlowNodes[i].Ticks < worst)
            {
                worst = _perfSlowNodes[i].Ticks;
                slot = i;
            }
        }

        if (slot >= 0 && selfTicks > worst)
            _perfSlowNodes[slot] = (obj.Name, selfTicks);
    }

    private void MaybeWritePerfLog()
    {
        if (PerfLogPath == null)
            return;

        long now = Environment.TickCount64;
        if (now < _perfLogNextTick)
            return;
        _perfLogNextTick = now + 1000;

        try
        {
            string line = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{DateTime.Now:HH:mm:ss} build={_statsBuildSceneMs:F2} (grid={_perfBuildGridMs:F2} walk={_perfBuildWalkMs:F2} nodes={_perfWalkNodes} comp={TicksToMs(_perfWalkComponentTicks):F2} route={TicksToMs(_perfWalkRouteTicks):F2} geom={TicksToMs(_perfWalkGeometryTicks):F2} solidV={_perfBuildSolidVerts} lineV={_perfBuildLineVerts}) shadows={_statsShadowMs:F2} (dir={_statsDirectionalShadowMs:F2} spot={_statsSpotShadowMs:F2} point={_statsPointShadowMs:F2}) deferred=(gbuf={_statsDeferredGBufferMs:F2} light={_statsDeferredLightingMs:F2} depth={_statsDeferredDepthCopyMs:F2} ssgi={_statsDeferredSsgiMs:F2} ssr={_statsDeferredSsrMs:F2}) sky={_statsSkyboxMs:F2} static={_statsStaticOpaqueMs:F2} dyn={_statsDynamicOpaqueMs:F2} sgraph={_statsShaderGraphMs:F2} terrain={_statsTerrainMs:F2} occl={_statsOcclusionMs:F2} lines={_statsLinesGizmosMs:F2} part={_statsParticlesMs:F2} | draws={_statsDrawCalls} shadowDraws={_statsShadowDrawCalls} instDraws={_statsInstancedDrawCalls} instances={_statsInstances} tris={_statsTriangles}");
            var slow = new System.Text.StringBuilder();
            foreach (var (name, ticks) in _perfSlowNodes)
                if (ticks > 0)
                    slow.Append($" {name}={TicksToMs(ticks):F2}");
            if (slow.Length > 0)
                line += " | top:" + slow;

            System.IO.File.AppendAllText(PerfLogPath, line + Environment.NewLine);
        }
        catch
        {
            // nunca romper el render por el log
        }
    }
}
