using ImGuiNET;
using System;
using System.IO;
using System.Numerics;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private static readonly string[] LightmapperLabels =
    {
        "Progressive CPU",
        "Progressive GPU (Preview)"
    };

    private static readonly string[] MixedLightingModeLabels =
    {
        "Baked Indirect",
        "Shadowmask",
        "Subtractive"
    };

    private static readonly string[] FilteringLabels =
    {
        "Auto",
        "None",
        "Advanced"
    };

    private static readonly string[] DirectionalModeLabels =
    {
        "Directional",
        "Non-Directional"
    };

    private static readonly int[] LightmapMaxSizes =
    {
        256,
        512,
        1024,
        2048,
        4096
    };

    private bool showLightingWindow;
    private bool lightmapRealtimeGlobalIllumination;
    private bool lightmapBakedGlobalIllumination = true;
    private bool lightmapProgressiveUpdates = true;
    private bool lightmapMultipleImportanceSampling = true;
    private bool lightmapCompress = true;
    private bool lightmapAmbientOcclusion;
    private bool lightmapAutoGenerate;
    private int lightmapLightmapperIndex;
    private int lightmapMixedModeIndex = 1;
    private bool lightmapDistanceShadowmask = true;
    private float lightmapShadowmaskDistance = 40f;
    private float lightmapShadowmaskDistanceFade = 8f;
    private int lightmapDirectSamples = 32;
    private int lightmapIndirectSamples = 200;
    private int lightmapEnvironmentSamples = 200;
    private int lightmapBounces = 2;
    private int lightmapFilteringIndex;
    private int lightmapIndirectResolution = 2;
    private int lightmapResolution = 20;
    private int lightmapPadding = 2;
    private int lightmapMaxSizeIndex = 2;
    private int lightmapDirectionalModeIndex;
    private float lightmapAlbedoBoost = 1f;
    private float lightmapIndirectIntensity = 1f;
    private float lightmapLastProgress;
    private string lightmapLastLog = "Lighting listo.";
    private LightingSettingsAsset? lightingSettingsAsset;
    private string lightingSettingsPath = string.Empty;

    private readonly record struct LightingSettingsSnapshot(
        bool RealtimeGi, bool BakedGi, bool ProgressiveUpdates, bool Mis, bool Compress, bool AmbientOcclusion, bool AutoGenerate,
        int Lightmapper, int MixedMode, bool DistanceShadowmask, float ShadowmaskDistance, float ShadowmaskDistanceFade, int DirectSamples, int IndirectSamples, int EnvironmentSamples, int Bounces, int Filtering,
        int IndirectResolution, int Resolution, int Padding, int MaxSize, int DirectionalMode, float AlbedoBoost, float IndirectIntensity);

    private int CurrentLightmapResolution
        => LightmapMaxSizes[Math.Clamp(lightmapMaxSizeIndex, 0, LightmapMaxSizes.Length - 1)];

    private void DrawLightingWindow()
    {
        if (!showLightingWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(560f, 720f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Lighting", ref showLightingWindow, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        TrackToolWindowMouse();

        EnsureLightingSettingsAsset();
        var settingsBefore = CaptureLightingSettings();

        DrawLightingHeader();

        if (ImGui.BeginTabBar("##LightingTabs"))
        {
            if (ImGui.BeginTabItem("Scene"))
            {
                DrawLightingSceneTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Environment"))
            {
                DrawLightingEnvironmentTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Realtime Lightmaps"))
            {
                DrawLightingRealtimeTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Baked Lightmaps"))
            {
                DrawLightingBakedTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (!settingsBefore.Equals(CaptureLightingSettings()))
            SaveLightingSettingsAsset();

        ImGui.End();
    }

    private void DrawLightingHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.84f, 0.90f, 1f));
        ImGui.Text("Lighting Settings");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextDisabled("Baked GI / Lightmaps");

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.105f, 0.112f, 0.118f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 7f);
        ImGui.BeginChild("##LightingSettingsAsset", new Vector2(0f, 54f), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
        ImGui.TextDisabled("Lighting Settings");
        ImGui.SameLine(165f);
        ImGui.Text(lightingSettingsAsset?.Name ?? "SceneLightingSettings");
        ImGui.SameLine(ImGui.GetWindowWidth() - 176f);
        if (ImGui.Button("Reset to Balanced", new Vector2(166f, 25f)))
            ResetLightingSettingsToBalanced();
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawLightingSceneTab()
    {
        DrawLightingRealtimeSection();
        DrawLightingMixedSection();
        DrawLightingBakeSettingsSection();
        DrawLightingWorkflowSection();
        DrawLightingBakeStatusSection();
    }

    private void DrawLightingEnvironmentTab()
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Environment");
        ImGui.Separator();
        ImGui.TextWrapped("Esta pestaña queda preparada para Skybox, Ambient Source, Environment Reflections y Reflection Probe defaults.");
        ImGui.BulletText("Skybox/HDRI ya se controla desde Render Pipeline.");
        ImGui.BulletText("Reflection Probes ya pueden bakearse y visualizarse en escena.");
        ImGui.BulletText("Siguiente corte: mover defaults globales de IBL y probes aquí.");
    }

    private void DrawLightingRealtimeTab()
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Realtime Lightmaps");
        ImGui.Separator();
        ImGui.Checkbox("Realtime Global Illumination", ref lightmapRealtimeGlobalIllumination);
        ImGui.TextWrapped("Reservado para GI runtime. El baker actual trabaja como Progressive CPU para lightmaps baked.");
    }

    private void DrawLightingBakedTab()
    {
        DrawLightingBakeSettingsSection();
        DrawLightingWorkflowSection();
        DrawLightingBakeStatusSection();
    }

    private void DrawLightingRealtimeSection()
    {
        if (!ImGui.CollapsingHeader("Realtime Lighting", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Checkbox("Realtime Global Illumination", ref lightmapRealtimeGlobalIllumination);
        ImGui.SameLine();
        ImGui.TextDisabled("(experimental)");
        ImGui.TextDisabled("Realtime Enlighten/GI no está activo todavía; se conserva como opción de pipeline.");
        ImGui.Spacing();
    }

    private void DrawLightingMixedSection()
    {
        if (!ImGui.CollapsingHeader("Mixed Lighting", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Checkbox("Baked Global Illumination", ref lightmapBakedGlobalIllumination);
        DrawLightingCombo("Lighting Mode", ref lightmapMixedModeIndex, MixedLightingModeLabels);
        if (lightmapMixedModeIndex == (int)MixedLightingMode.Shadowmask)
        {
            ImGui.Checkbox("Distance Shadowmask", ref lightmapDistanceShadowmask);
            ImGui.SameLine();
            ImGui.TextDisabled("realtime cerca, baked lejos");
            DrawLightingSlider("Shadow Distance", ref lightmapShadowmaskDistance, 1f, 250f);
            DrawLightingSlider("Shadow Fade", ref lightmapShadowmaskDistanceFade, 0.1f, 50f);
            lightmapShadowmaskDistanceFade = Math.Min(lightmapShadowmaskDistanceFade, lightmapShadowmaskDistance);
        }
        ImGui.TextWrapped("Shadowmask escribe oclusión estática RGBA. Cerca combina sin oscurecer dos veces; lejos usa el bake estable. Baked Indirect sólo hornea el rebote.");
        ImGui.Spacing();
    }

    private void DrawLightingBakeSettingsSection()
    {
        if (!ImGui.CollapsingHeader("Lightmapping Settings", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        DrawLightingCombo("Lightmapper", ref lightmapLightmapperIndex, LightmapperLabels);
        ImGui.Checkbox("Progressive Updates", ref lightmapProgressiveUpdates);
        ImGui.Checkbox("Multiple Importance Sampling", ref lightmapMultipleImportanceSampling);

        DrawLightingInt("Direct Samples", ref lightmapDirectSamples, 1, 4096);
        DrawLightingInt("Indirect Samples", ref lightmapIndirectSamples, 1, 8192);
        DrawLightingInt("Environment Samples", ref lightmapEnvironmentSamples, 1, 8192);
        DrawLightingInt("Bounces", ref lightmapBounces, 0, 16);
        DrawLightingCombo("Filtering", ref lightmapFilteringIndex, FilteringLabels);

        ImGui.Separator();
        DrawLightingInt("Indirect Resolution", ref lightmapIndirectResolution, 1, 100);
        ImGui.SameLine();
        ImGui.TextDisabled("texels per unit");
        DrawLightingInt("Lightmap Resolution", ref lightmapResolution, 1, 200);
        ImGui.SameLine();
        ImGui.TextDisabled("texels per unit");
        DrawLightingInt("Lightmap Padding", ref lightmapPadding, 1, 64);
        ImGui.SameLine();
        ImGui.TextDisabled("texels");

        DrawLightmapSizeCombo();
        ImGui.Checkbox("Compress Lightmaps", ref lightmapCompress);
        ImGui.Checkbox("Ambient Occlusion", ref lightmapAmbientOcclusion);
        DrawLightingCombo("Directional Mode", ref lightmapDirectionalModeIndex, DirectionalModeLabels);

        DrawLightingSlider("Albedo Boost", ref lightmapAlbedoBoost, 0f, 4f);
        DrawLightingSlider("Indirect Intensity", ref lightmapIndirectIntensity, 0f, 4f);
        ImGui.Spacing();
    }

    private void DrawLightingWorkflowSection()
    {
        if (!ImGui.CollapsingHeader("Workflow Settings", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Checkbox("Auto Generate", ref lightmapAutoGenerate);
        ImGui.SameLine();
        ImGui.TextDisabled("manual recomendado: el baker actual no rehornea automáticamente en cada cambio");

        using (new DisabledScope(_isBaking || isPlaying || !lightmapBakedGlobalIllumination))
        {
            if (ImGui.Button("Generate Lighting", new Vector2(180f, 30f)))
                _ = BakeLightmapsAsync(CurrentLightmapResolution);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Scene Lightmap Links", new Vector2(210f, 30f)))
        {
            ClearLightmapLinks(objects);
            _lightmapPaths.Clear();
            sceneRenderer.InvalidateStaticBatch();
            sceneDirty = true;
            lightmapLastProgress = 0f;
            lightmapLastLog = "Lightmap links limpiados en la escena. Los archivos horneados no fueron borrados.";
            statusMessage = lightmapLastLog;
        }
    }

    private void DrawLightingBakeStatusSection()
    {
        if (!ImGui.CollapsingHeader("Bake Status", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        int staticCount = CountStaticMeshes(objects);
        string outputDir = string.IsNullOrWhiteSpace(projectPath)
            ? "(sin proyecto abierto)"
            : Path.Combine(projectPath, "Assets", "Lightmaps");

        ImGui.Text($"Static Meshes: {staticCount}");
        ImGui.Text($"Baked Lightmaps: {_lightmapPaths.Count}");
        ImGui.Text($"Max Lightmap Size: {CurrentLightmapResolution}x{CurrentLightmapResolution}");
        ImGui.TextDisabled(outputDir);

        float progress = _isBaking ? lightmapLastProgress : (_lightmapPaths.Count > 0 ? 1f : lightmapLastProgress);
        ImGui.ProgressBar(progress, new Vector2(-1f, 22f), _isBaking ? $"{(int)(progress * 100f)}%" : "Ready");
        ImGui.TextWrapped(lightmapLastLog);
    }

    private void ResetLightingSettingsToBalanced()
    {
        lightmapRealtimeGlobalIllumination = false;
        lightmapBakedGlobalIllumination = true;
        lightmapProgressiveUpdates = true;
        lightmapMultipleImportanceSampling = true;
        lightmapCompress = true;
        lightmapAmbientOcclusion = false;
        lightmapAutoGenerate = false;
        lightmapLightmapperIndex = 0;
        lightmapMixedModeIndex = 1;
        lightmapDistanceShadowmask = true;
        lightmapShadowmaskDistance = 40f;
        lightmapShadowmaskDistanceFade = 8f;
        lightmapDirectSamples = 32;
        lightmapIndirectSamples = 200;
        lightmapEnvironmentSamples = 200;
        lightmapBounces = 2;
        lightmapFilteringIndex = 0;
        lightmapIndirectResolution = 2;
        lightmapResolution = 20;
        lightmapPadding = 2;
        lightmapMaxSizeIndex = 2;
        lightmapDirectionalModeIndex = 0;
        lightmapAlbedoBoost = 1f;
        lightmapIndirectIntensity = 1f;
        lightmapLastLog = "Lighting Settings restaurado a Balanced.";
        statusMessage = lightmapLastLog;
        SaveLightingSettingsAsset();
    }

    private void EnsureLightingSettingsAsset()
    {
        if (lightingSettingsAsset != null || string.IsNullOrWhiteSpace(projectPath))
            return;

        string settingsDirectory = Path.Combine(rootAssetsPath, "Settings");
        lightingSettingsPath = Path.Combine(settingsDirectory, "SceneLightingSettings.asset");
        if (File.Exists(lightingSettingsPath))
            lightingSettingsAsset = ScriptableObjectAsset.Load(lightingSettingsPath, physicsEngine, scriptCompiler) as LightingSettingsAsset;

        if (lightingSettingsAsset == null)
        {
            Directory.CreateDirectory(settingsDirectory);
            lightingSettingsAsset = new LightingSettingsAsset
            {
                Name = "SceneLightingSettings",
                AssetPath = lightingSettingsPath
            };
            SaveLightingSettingsAsset();
            assetService.AssetDatabase.Refresh(createMissingMeta: true);
        }

        lightingSettingsAsset.Normalize();
        ApplyLightingSettingsAsset(lightingSettingsAsset);
        lightmapLastLog = $"Lighting Settings cargado: {lightingSettingsPath}";
    }

    private LightingSettingsSnapshot CaptureLightingSettings() => new(
        lightmapRealtimeGlobalIllumination, lightmapBakedGlobalIllumination, lightmapProgressiveUpdates, lightmapMultipleImportanceSampling,
        lightmapCompress, lightmapAmbientOcclusion, lightmapAutoGenerate, lightmapLightmapperIndex, lightmapMixedModeIndex, lightmapDistanceShadowmask, lightmapShadowmaskDistance, lightmapShadowmaskDistanceFade,
        lightmapDirectSamples, lightmapIndirectSamples, lightmapEnvironmentSamples, lightmapBounces, lightmapFilteringIndex,
        lightmapIndirectResolution, lightmapResolution, lightmapPadding, CurrentLightmapResolution, lightmapDirectionalModeIndex,
        lightmapAlbedoBoost, lightmapIndirectIntensity);

    private void ApplyLightingSettingsAsset(LightingSettingsAsset settings)
    {
        lightmapRealtimeGlobalIllumination = settings.RealtimeGlobalIllumination;
        lightmapBakedGlobalIllumination = settings.BakedGlobalIllumination;
        lightmapProgressiveUpdates = settings.ProgressiveUpdates;
        lightmapMultipleImportanceSampling = settings.MultipleImportanceSampling;
        lightmapCompress = settings.CompressLightmaps;
        lightmapAmbientOcclusion = settings.AmbientOcclusion;
        lightmapAutoGenerate = settings.AutoGenerate;
        lightmapLightmapperIndex = settings.Lightmapper;
        lightmapMixedModeIndex = settings.MixedLightingMode;
        lightmapDistanceShadowmask = settings.DistanceShadowmask;
        lightmapShadowmaskDistance = settings.ShadowmaskDistance;
        lightmapShadowmaskDistanceFade = settings.ShadowmaskDistanceFade;
        lightmapDirectSamples = settings.DirectSamples;
        lightmapIndirectSamples = settings.IndirectSamples;
        lightmapEnvironmentSamples = settings.EnvironmentSamples;
        lightmapBounces = settings.Bounces;
        lightmapFilteringIndex = settings.Filtering;
        lightmapIndirectResolution = settings.IndirectResolution;
        lightmapResolution = settings.LightmapResolution;
        lightmapPadding = settings.LightmapPadding;
        lightmapMaxSizeIndex = Array.IndexOf(LightmapMaxSizes, LightingSettingsAsset.NormalizeSize(settings.MaxLightmapSize));
        lightmapMaxSizeIndex = Math.Max(0, lightmapMaxSizeIndex);
        lightmapDirectionalModeIndex = settings.DirectionalMode;
        lightmapAlbedoBoost = settings.AlbedoBoost;
        lightmapIndirectIntensity = settings.IndirectIntensity;
    }

    private void SaveLightingSettingsAsset()
    {
        if (lightingSettingsAsset == null || string.IsNullOrWhiteSpace(lightingSettingsPath))
            return;

        lightingSettingsAsset.RealtimeGlobalIllumination = lightmapRealtimeGlobalIllumination;
        lightingSettingsAsset.BakedGlobalIllumination = lightmapBakedGlobalIllumination;
        lightingSettingsAsset.ProgressiveUpdates = lightmapProgressiveUpdates;
        lightingSettingsAsset.MultipleImportanceSampling = lightmapMultipleImportanceSampling;
        lightingSettingsAsset.CompressLightmaps = lightmapCompress;
        lightingSettingsAsset.AmbientOcclusion = lightmapAmbientOcclusion;
        lightingSettingsAsset.AutoGenerate = lightmapAutoGenerate;
        lightingSettingsAsset.Lightmapper = lightmapLightmapperIndex;
        lightingSettingsAsset.MixedLightingMode = lightmapMixedModeIndex;
        lightingSettingsAsset.DistanceShadowmask = lightmapDistanceShadowmask;
        lightingSettingsAsset.ShadowmaskDistance = lightmapShadowmaskDistance;
        lightingSettingsAsset.ShadowmaskDistanceFade = lightmapShadowmaskDistanceFade;
        lightingSettingsAsset.DirectSamples = lightmapDirectSamples;
        lightingSettingsAsset.IndirectSamples = lightmapIndirectSamples;
        lightingSettingsAsset.EnvironmentSamples = lightmapEnvironmentSamples;
        lightingSettingsAsset.Bounces = lightmapBounces;
        lightingSettingsAsset.Filtering = lightmapFilteringIndex;
        lightingSettingsAsset.IndirectResolution = lightmapIndirectResolution;
        lightingSettingsAsset.LightmapResolution = lightmapResolution;
        lightingSettingsAsset.LightmapPadding = lightmapPadding;
        lightingSettingsAsset.MaxLightmapSize = CurrentLightmapResolution;
        lightingSettingsAsset.DirectionalMode = lightmapDirectionalModeIndex;
        lightingSettingsAsset.AlbedoBoost = lightmapAlbedoBoost;
        lightingSettingsAsset.IndirectIntensity = lightmapIndirectIntensity;
        lightingSettingsAsset.Normalize();
        ScriptableObjectAsset.Save(lightingSettingsPath, lightingSettingsAsset, physicsEngine);
    }

    private static void ClearLightmapLinks(System.Collections.Generic.IEnumerable<GameObject> roots)
    {
        foreach (var obj in roots)
        {
            if (obj.GetComponent<MeshRenderer>() is { } renderer)
            {
                renderer.LightmapPath = string.Empty;
                renderer.ShadowmaskPath = string.Empty;
                renderer.ReceiveGlobalIllumination = 0;
                renderer.LightmapScaleX = 1f;
                renderer.LightmapScaleY = 1f;
                renderer.LightmapOffsetX = 0f;
                renderer.LightmapOffsetY = 0f;
                renderer.UseGeneratedLightmapUv = false;
            }
            ClearLightmapLinks(obj.Children);
        }
    }

    private static void DrawLightingCombo(string label, ref int index, string[] labels)
    {
        index = Math.Clamp(index, 0, labels.Length - 1);
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(185f);
        ImGui.SetNextItemWidth(-1f);
        ImGui.Combo("##" + label, ref index, labels, labels.Length);
    }

    private static void DrawLightingInt(string label, ref int value, int min, int max)
    {
        value = Math.Clamp(value, min, max);
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(185f);
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("##" + label, ref value))
            value = Math.Clamp(value, min, max);
    }

    private static void DrawLightingSlider(string label, ref float value, float min, float max)
    {
        value = Math.Clamp(value, min, max);
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(185f);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##" + label, ref value, min, max, "%.2f"))
            value = Math.Clamp(value, min, max);
    }

    private void DrawLightmapSizeCombo()
    {
        string[] labels = new string[LightmapMaxSizes.Length];
        for (int i = 0; i < LightmapMaxSizes.Length; i++)
            labels[i] = LightmapMaxSizes[i].ToString();

        DrawLightingCombo("Max Lightmap Size", ref lightmapMaxSizeIndex, labels);
    }
}
