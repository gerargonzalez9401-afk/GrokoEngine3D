using ImGuiNET;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Diagnostics;
using Vector2 = System.Numerics.Vector2;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private void CaptureProfilerFrameState()
    {
        profiler.Fps = Math.Max(0.001f, ImGui.GetIO().Framerate);
        profiler.ObjectCount = GetCachedObjectCount();
        profiler.ColliderCount = physicsEngine.GetColliders().Count;
        profiler.ManagedMemoryMb = GC.GetTotalMemory(false) / (1024f * 1024f);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        profiler.MainThreadAllocKb = profiler.LastMainThreadAllocatedBytes <= 0
            ? 0f
            : Math.Max(0f, (allocatedBytes - profiler.LastMainThreadAllocatedBytes) / 1024f);
        profiler.LastMainThreadAllocatedBytes = allocatedBytes;
        profiler.GcGen0 = GC.CollectionCount(0);
        profiler.GcGen1 = GC.CollectionCount(1);
        profiler.GcGen2 = GC.CollectionCount(2);
        profiler.PreviewQueueCount = previewGenerationQueue.Count;
        profiler.PreviewDiskJobCount = GetPreviewDiskJobCount();
        profiler.PreviewReadyCount = GetPreviewReadyCount();
        profiler.LastRaycastCandidateCount = physicsEngine.LastRaycastCandidateCount;
        profiler.RenderScale = renderScale;
        profiler.RenderStats = sceneRenderer.LastStats;
        profiler.UiDrawStats = imgui.LastDrawStats;
        profiler.Playing = isPlaying;
        profiler.PlayPaused = playPaused;
        profiler.SimulatingPhysics = simulatePhysics;
        profiler.VSync = vsync;
    }

    private int GetPreviewReadyCount()
    {
        lock (previewJobLock)
            return previewDiskReadyQueue.Count;
    }

    private void DrawProfilerWindow()
    {
        if (!showProfiler)
            return;

        ImGui.SetNextWindowSize(new Vector2(520f, 620f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Profiler", ref showProfiler, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }
        TrackToolWindowMouse();

        float frameBudget = 1000f / Math.Max(1f, profiler.VSync ? 60f : Math.Max(60f, profiler.Fps));
        ImGui.Text($"FPS {profiler.Fps:F0}");
        ImGui.SameLine();
        ImGui.TextDisabled($"{profiler.FrameMs:F2} ms");
        ImGui.SameLine();
        ImGui.TextDisabled(profiler.Playing ? (profiler.PlayPaused ? "Play Paused" : "Play") : profiler.SimulatingPhysics ? "Physics" : "Edit");
        ImGui.Separator();

        // Frame-time CRUDO (sin suavizar) + pico de la ventana + gráfico: aquí SÍ se ven los
        // tirones que el promedio (EMA) esconde. Si el pico es muy superior al frame normal,
        // hay un spike intermitente que cazar.
        float framePeak = profiler.FramePeakMs();
        ImGui.TextDisabled($"raw {profiler.RawFrameMs:F1} ms  ·  peak {framePeak:F1} ms ({(framePeak > 0.1f ? 1000f / framePeak : 0f):F0} fps)  ·  mem {profiler.ManagedMemoryMb:F0} MB");
        // Desglose del PEOR frame: dice qué subsistema causó el tirón (el más alto es el culpable).
        ImGui.TextDisabled($"worst {profiler.PeakFrameMs:F1} ms →  Update {profiler.PeakBreakUpdate:F1}   Render {profiler.PeakBreakRender:F1}   UI {profiler.PeakBreakUi:F1}   Swap {profiler.PeakBreakSwap:F1}");
        ImGui.PlotLines("##frametime", ref profiler.FrameHistory[0], profiler.FrameHistory.Length,
            profiler.FrameHistoryIndex, "frame ms", 0f, Math.Max(20f, framePeak * 1.1f),
            new Vector2(ImGui.GetContentRegionAvail().X, 48f));
        ImGui.Separator();

        if (ImGui.CollapsingHeader("CPU", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawProfilerBar("Frame", profiler.FrameMs, frameBudget);
            DrawProfilerBar("Update", profiler.UpdateMs, frameBudget);
            DrawProfilerBar("Render Scene", profiler.RenderSceneMs, frameBudget);
            DrawProfilerBar("UI", profiler.UiMs, frameBudget);
            DrawProfilerBar("Swap", profiler.SwapMs, frameBudget);
        }

        if (ImGui.CollapsingHeader("Update (runtime)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Desglose del sample "Update" (StepRuntime). Los sweeps del CharacterController
            // se cuentan en "Scripts" (se llaman desde el Update del script del jugador).
            DrawProfilerBar("Scripts", profiler.ScriptUpdateMs, frameBudget);
            DrawProfilerBar("Physics", profiler.PhysicsMs, frameBudget);
            DrawProfilerBar("  Sync", physicsEngine.LastSyncMs, frameBudget);
            DrawProfilerBar("  Bepu", physicsEngine.LastBepuMs, frameBudget);
            DrawProfilerBar("  Hash", physicsEngine.LastHashMs, frameBudget);
            DrawProfilerBar("  Events", physicsEngine.LastEventsMs, frameBudget);
            DrawProfilerBar("Scene", profiler.RuntimeSceneMs, frameBudget);
            DrawProfilerBar("Context", profiler.RuntimeContextMs, frameBudget);
        }

        if (ImGui.CollapsingHeader("UI", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawProfilerBar("Build", profiler.UiBuildMs, frameBudget);
            DrawProfilerBar("Draw", profiler.UiDrawMs, frameBudget);
            DrawProfilerBar("Project", profiler.UiProjectMs, frameBudget);
            DrawProfilerBar("Inspector", profiler.UiInspectorMs, frameBudget);
            DrawProfilerBar("Scene Panel", profiler.UiScenePanelMs, frameBudget);
            DrawProfilerBar("Hierarchy", profiler.UiHierarchyMs, frameBudget);
            DrawProfilerBar("Menu/Status", profiler.UiMenuMs + profiler.UiStatusMs, frameBudget);
            DrawProfilerBar("Tools", profiler.UiToolsMs, frameBudget);
            DrawProfilerBar("Profiler", profiler.UiProfilerMs, frameBudget);
            DrawProfilerMetric("UI Draw Calls", profiler.UiDrawStats.DrawCalls.ToString());
            DrawProfilerMetric("UI Texture Binds", profiler.UiDrawStats.TextureBinds.ToString());
            DrawProfilerMetric("UI Vertices", FormatLarge(profiler.UiDrawStats.Vertices));
        }

        if (ImGui.CollapsingHeader("Runtime", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawProfilerBar("Runtime Total", profiler.RuntimeMs, frameBudget);
            DrawProfilerBar("Scripts Update", profiler.ScriptUpdateMs, frameBudget);
            DrawProfilerBar("Physics Step", profiler.PhysicsMs, frameBudget);
            DrawProfilerBar("Runtime Tick", profiler.RuntimeSceneMs, frameBudget);
            DrawProfilerBar("Particles", profiler.ParticlesMs, frameBudget);
            DrawProfilerBar("Input/Selection", profiler.EditorInputMs, frameBudget);
        }

        if (ImGui.CollapsingHeader("Render", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawProfilerBar("Scene Renderer", profiler.SceneRendererMs, frameBudget);
            DrawProfilerBar("Post Process", profiler.PostProcessMs, frameBudget);

            var stats = profiler.RenderStats;
            DrawProfilerBar("  Build/Collect", stats.BuildSceneMs, frameBudget);
            DrawProfilerBar("  Shadows Total", stats.ShadowMs, frameBudget);
            DrawProfilerBar("    Directional", stats.DirectionalShadowMs, frameBudget);
            DrawProfilerBar("    Spot", stats.SpotShadowMs, frameBudget);
            DrawProfilerBar("    Point", stats.PointShadowMs, frameBudget);
            DrawProfilerBar("  Skybox", stats.SkyboxMs, frameBudget);
            DrawProfilerBar("  Static Opaque", stats.StaticOpaqueMs, frameBudget);
            DrawProfilerBar("  Dynamic Opaque", stats.DynamicOpaqueMs, frameBudget);
            DrawProfilerBar("  Shader Graph", stats.ShaderGraphMs, frameBudget);
            DrawProfilerBar("  Terrain", stats.TerrainMs, frameBudget);
            DrawProfilerBar("  Gizmos/Lines", stats.LinesGizmosMs, frameBudget);
            DrawProfilerBar("  Particles", stats.ParticlesMs, frameBudget);
            DrawProfilerBar("  Occlusion", stats.OcclusionMs, frameBudget);
            DrawProfilerBar("  Other / Driver", stats.RenderOtherMs, frameBudget);
            DrawProfilerMetric("Resolution", $"{profiler.RenderWidth} x {profiler.RenderHeight}");
            DrawProfilerMetric("Render Scale", $"{profiler.RenderScale:F2}x");
            DrawProfilerMetric("Draw Calls", stats.DrawCalls.ToString());
            DrawProfilerMetric("Shadow Draws", stats.ShadowDrawCalls.ToString());
            DrawProfilerMetric("Triangles", FormatLarge(stats.Triangles));
            DrawProfilerMetric("Instanced Draws", stats.InstancedDrawCalls.ToString());
            DrawProfilerMetric("Instances", stats.Instances.ToString());
            DrawProfilerMetric("Static Ranges", stats.StaticRanges.ToString());
            DrawProfilerMetric("Dynamic Ranges", stats.DynamicRanges.ToString());
            DrawProfilerMetric("Textures Cached", stats.TextureCacheCount.ToString());
            DrawProfilerMetric("GPU Mesh Cache", stats.GpuMeshCacheCount.ToString());
            DrawProfilerMetric("Parsed Mesh Cache", stats.ParsedMeshCacheCount.ToString());
        }

        if (ImGui.CollapsingHeader("Assets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawProfilerBar("Asset Saves", profiler.AssetSavesMs, frameBudget);
            DrawProfilerBar("Auto Compile", profiler.ScriptCompileMs, frameBudget);
            DrawProfilerBar("Preview Queue", profiler.PreviewMs, frameBudget);
            DrawProfilerMetric("Queued", profiler.PreviewQueueCount.ToString());
            DrawProfilerMetric("Disk Jobs", profiler.PreviewDiskJobCount.ToString());
            DrawProfilerMetric("Ready", profiler.PreviewReadyCount.ToString());
            DrawProfilerMetric("New Requests/Frame", previewQueueRequestsThisFrame.ToString());
        }

        if (ImGui.CollapsingHeader("Scene", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawProfilerMetric("Objects", profiler.ObjectCount.ToString());
            DrawProfilerMetric("Colliders", profiler.ColliderCount.ToString());
            DrawProfilerMetric("Raycast Candidates", profiler.LastRaycastCandidateCount.ToString());
            DrawProfilerMetric("Managed Memory", $"{profiler.ManagedMemoryMb:F1} MB");
            DrawProfilerMetric("Alloc / Frame", $"{profiler.MainThreadAllocKb:F1} KB");
            DrawProfilerMetric("GC Gen0/1/2", $"{profiler.GcGen0}/{profiler.GcGen1}/{profiler.GcGen2}");
        }

        ImGui.End();
    }

    private static void DrawProfilerMetric(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(170f);
        ImGui.Text(value);
    }

    private static void DrawProfilerBar(string label, float valueMs, float budgetMs)
    {
        float safeBudget = Math.Max(0.001f, budgetMs);
        float normalized = Math.Clamp(valueMs / safeBudget, 0f, 1f);
        var color = valueMs <= safeBudget * 0.55f
            ? new System.Numerics.Vector4(0.22f, 0.70f, 0.36f, 1f)
            : valueMs <= safeBudget
                ? new System.Numerics.Vector4(0.92f, 0.68f, 0.20f, 1f)
                : new System.Numerics.Vector4(0.90f, 0.28f, 0.24f, 1f);

        ImGui.TextDisabled(label);
        ImGui.SameLine(170f);
        ImGui.Text($"{valueMs:F2} ms");
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(normalized, new Vector2(-1f, 7f), "");
        ImGui.PopStyleColor();
    }

    private static string FormatLarge(long value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000f:F2}M";
        if (value >= 1_000)
            return $"{value / 1_000f:F1}K";
        return value.ToString();
    }

    private sealed class EditorProfiler
    {
        private const float Smooth = 0.18f;

        public float Fps;
        public float FrameMs;
        // Frame-time CRUDO (sin suavizar) + histórico para ver los picos/tirones que el EMA esconde.
        public float RawFrameMs;
        public readonly float[] FrameHistory = new float[160];
        public int FrameHistoryIndex;
        // Valores crudos por categoría + desglose del PEOR frame de la ventana (para saber qué subsistema causa el tirón).
        public float RawUpdateMs, RawRenderSceneMs, RawUiMs, RawSwapMs;
        public float PeakFrameMs, PeakBreakUpdate, PeakBreakRender, PeakBreakUi, PeakBreakSwap;
        private int peakResetCountdown;
        public float UpdateMs;
        public float RenderSceneMs;
        public float SceneRendererMs;
        public float PostProcessMs;
        public float UiMs;
        public float UiBuildMs;
        public float UiDrawMs;
        public float UiMenuMs;
        public float UiStatusMs;
        public float UiHierarchyMs;
        public float UiScenePanelMs;
        public float UiInspectorMs;
        public float UiProjectMs;
        public float UiToolsMs;
        public float UiProfilerMs;
        public float SwapMs;
        public float RuntimeMs;
        public float RuntimeContextMs;
        public float ScriptUpdateMs;
        public float PhysicsMs;
        public float RuntimeSceneMs;
        public float ParticlesMs;
        public float EditorInputMs;
        public float AssetSavesMs;
        public float ScriptCompileMs;
        public float PreviewMs;
        public int ObjectCount;
        public int ColliderCount;
        public float ManagedMemoryMb;
        public float MainThreadAllocKb;
        public long LastMainThreadAllocatedBytes;
        public int GcGen0;
        public int GcGen1;
        public int GcGen2;
        public int PreviewQueueCount;
        public int PreviewDiskJobCount;
        public int PreviewReadyCount;
        public int LastRaycastCandidateCount;
        public int RenderWidth;
        public int RenderHeight;
        public float RenderScale = 1f;
        public bool Playing;
        public bool PlayPaused;
        public bool SimulatingPhysics;
        public bool VSync;
        public SceneRenderStats RenderStats = SceneRenderStats.Empty;
        public ImGuiDrawStats UiDrawStats = ImGuiDrawStats.Empty;

        public static long Timestamp() => Stopwatch.GetTimestamp();

        public static float ElapsedMs(long start) =>
            (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        public void SampleFrame(float value)
        {
            Sample(ref FrameMs, value);
            RawFrameMs = value;
            FrameHistory[FrameHistoryIndex] = value;
            FrameHistoryIndex = (FrameHistoryIndex + 1) % FrameHistory.Length;

            // Pico por ventana (~ tamaño del histórico): al renovarse, captura el desglose del
            // peor frame para que se vea QUÉ subsistema (Update/Render/UI/Swap) provocó el tirón.
            if (--peakResetCountdown <= 0)
            {
                PeakFrameMs = 0f;
                peakResetCountdown = FrameHistory.Length;
            }
            if (value > PeakFrameMs)
            {
                PeakFrameMs = value;
                PeakBreakUpdate = RawUpdateMs;
                PeakBreakRender = RawRenderSceneMs;
                PeakBreakUi = RawUiMs;
                PeakBreakSwap = RawSwapMs;
            }
        }

        public float FramePeakMs()
        {
            float peak = 0f;
            foreach (float v in FrameHistory)
                if (v > peak) peak = v;
            return peak;
        }
        public void SampleUpdate(float value) { RawUpdateMs = value; Sample(ref UpdateMs, value); }
        public void SampleRenderScene(float value) { RawRenderSceneMs = value; Sample(ref RenderSceneMs, value); }
        public void SampleSceneRenderer(float value) => Sample(ref SceneRendererMs, value);
        public void SamplePostProcess(float value) => Sample(ref PostProcessMs, value);
        public void SampleUi(float value) { RawUiMs = value; Sample(ref UiMs, value); }
        public void SampleUiBuild(float value) => Sample(ref UiBuildMs, value);
        public void SampleUiDraw(float value) => Sample(ref UiDrawMs, value);
        public void SampleUiMenu(float value) => Sample(ref UiMenuMs, value);
        public void SampleUiStatus(float value) => Sample(ref UiStatusMs, value);
        public void SampleUiHierarchy(float value) => Sample(ref UiHierarchyMs, value);
        public void SampleUiScenePanel(float value) => Sample(ref UiScenePanelMs, value);
        public void SampleUiInspector(float value) => Sample(ref UiInspectorMs, value);
        public void SampleUiProject(float value) => Sample(ref UiProjectMs, value);
        public void SampleUiTools(float value) => Sample(ref UiToolsMs, value);
        public void SampleUiProfiler(float value) => Sample(ref UiProfilerMs, value);
        public void SampleSwap(float value) { RawSwapMs = value; Sample(ref SwapMs, value); }
        public void SampleRuntime(float value) => Sample(ref RuntimeMs, value);
        public void SampleRuntimeContext(float value) => Sample(ref RuntimeContextMs, value);
        public void SampleScriptUpdate(float value) => Sample(ref ScriptUpdateMs, value);
        public void SamplePhysics(float value) => Sample(ref PhysicsMs, value);
        public void SampleRuntimeScene(float value) => Sample(ref RuntimeSceneMs, value);
        public void SampleParticles(float value) => Sample(ref ParticlesMs, value);
        public void SampleEditorInput(float value) => Sample(ref EditorInputMs, value);
        public void SampleAssetSaves(float value) => Sample(ref AssetSavesMs, value);
        public void SampleScriptCompile(float value) => Sample(ref ScriptCompileMs, value);
        public void SamplePreview(float value) => Sample(ref PreviewMs, value);

        private static void Sample(ref float target, float value)
        {
            target = target <= 0.0001f ? value : target + (value - target) * Smooth;
        }
    }
}
