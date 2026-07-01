using GrokoEngine;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using Vec4 = System.Numerics.Vector4;
using ShaderGraphModel = GrokoShaderGraphPro.Models.ShaderGraphModel;
using ShaderGraphTemplates = GrokoShaderGraphPro.Services.GraphTemplates;
using ShaderCodeGenerator = GrokoShaderGraphPro.Services.ShaderCodeGenerator;
using ShaderGraphValidator = GrokoShaderGraphPro.Services.GraphValidator;
using GraphPin = GrokoShaderGraphPro.Models.GraphPin;
using GraphConnection = GrokoShaderGraphPro.Models.GraphConnection;
using NodeKind = GrokoShaderGraphPro.Models.NodeKind;
using PinType = GrokoShaderGraphPro.Models.PinType;
using PinDirection = GrokoShaderGraphPro.Models.PinDirection;
using GraphProperty = GrokoShaderGraphPro.Models.GraphProperty;
using PropertyAttribute = GrokoShaderGraphPro.Models.PropertyAttribute;
using PropertyColorMode = GrokoShaderGraphPro.Models.PropertyColorMode;
using GlfwKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace GrokoEngine.ImGuiEditor;
internal sealed partial class ImGuiEditorApp
{
    private void DrawEditorSettingsWindow()
    {
        if (!showEditorSettings)
            return;

        ImGui.SetNextWindowSize(new Vector2(420f, 360f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Editor Settings", ref showEditorSettings, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }
        TrackToolWindowMouse();

        DrawPanelHeader("Editor Settings", "visual workflow");
        if (ImGui.Button("Save Preferences", new Vector2(150f, 26f)))
        {
            SaveEditorSettings();
            statusMessage = "Editor preferences saved";
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset Preferences", new Vector2(150f, 26f)))
        {
            try
            {
                if (File.Exists(editorSettingsPath))
                    File.Delete(editorSettingsPath);
                statusMessage = "Preferences reset; reopen editor to apply defaults";
            }
            catch (Exception ex)
            {
                statusMessage = "Preferences reset failed: " + ex.Message;
            }
        }
        ImGui.Separator();
        if (ImGui.BeginTabBar("EditorSettingsTabs"))
        {
            if (ImGui.BeginTabItem("Viewport"))
            {
                SmallCheckbox("Show Gizmos", ref viewportGizmosVisible);
                SmallCheckbox("Shaded Mode", ref viewportShadedMode);
                SmallCheckbox("Local Space", ref viewportLocalSpace);
                SmallCheckbox("Pivot Center", ref viewportPivotCenter);
                ImGui.SeparatorText("Grid");
                SmallCheckbox("Show Grid", ref sceneGridVisible);
                SmallCheckbox("Snap To Grid", ref sceneGridSnapEnabled);
                DrawUnitySliderFloat("Grid Size", ref sceneGridSize, 0.01f, 16f, 0.01f);
                DrawUnitySliderFloat("Grid Opacity", ref sceneGridOpacity, 0f, 1f, 0.01f);
                if (ImGui.BeginCombo("Grid Plane", sceneGridAxis.ToString()))
                {
                    foreach (SceneGridAxis axis in Enum.GetValues<SceneGridAxis>())
                    {
                        bool selectedAxis = sceneGridAxis == axis;
                        if (ImGui.Selectable(axis.ToString(), selectedAxis))
                            sceneGridAxis = axis;
                        if (selectedAxis) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                if (SmallCheckbox("VSync", ref vsync))
                    VSync = vsync ? VSyncMode.On : VSyncMode.Off;
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Project"))
            {
                SmallCheckbox("List View", ref projectListView);
                DrawUnitySliderFloat("Icon Size", ref projectTileScale, ProjectTileScaleMin, ProjectTileScaleMax);
                if (ImGui.BeginCombo("Sort Mode", projectSortMode.ToString()))
                {
                    foreach (AssetSortMode mode in Enum.GetValues<AssetSortMode>())
                        if (ImGui.Selectable(mode.ToString(), projectSortMode == mode))
                            projectSortMode = mode;
                    ImGui.EndCombo();
                }
                SmallCheckbox("Descending", ref projectSortDescending);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Console"))
            {
                SmallCheckbox("Logs", ref consoleShowInfo);
                SmallCheckbox("Warnings", ref consoleShowWarnings);
                SmallCheckbox("Errors", ref consoleShowErrors);
                if (ImGui.Button("Clear Console", new Vector2(-1f, 26f)))
                    ClearConsole();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Render"))
            {
                ImGui.TextUnformatted("Frame Quality");
                DrawUnitySliderFloat("Render Scale", ref renderScale, 0.25f, 2f, 0.01f);
                renderScale = Math.Clamp(renderScale, 0.25f, 2f);
                ImGui.TextDisabled($"Internal render target: {GetViewportRenderSize().Width} x {GetViewportRenderSize().Height}");

                SmallCheckbox("FXAA", ref fxaaEnabled);
                ImGui.TextDisabled("Fast post anti-aliasing. Useful when MSAA is off or when using low Render Scale.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextUnformatted("Post Quality");
                DrawQualityCombo("Bloom Quality", ref bloomQuality);
                DrawQualityCombo("AO Quality", ref ambientOcclusionQuality);
                ImGui.TextDisabled("Quality 0 disables the extra samples. Higher values are smoother but heavier.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextUnformatted("Shadow Stability");
                DrawUnitySliderFloat("Shadow Bias", ref shadowBias, 0.1f, 4f, 0.01f);
                shadowBias = Math.Clamp(shadowBias, 0.1f, 4f);
                ImGui.TextDisabled("Lower = tighter contact shadows. Higher = less acne/flicker but more peter-panning.");

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Render Pipeline"))
            {
                DrawRenderPipelineSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Lighting"))
            {
                if (ImGui.BeginCombo("Debug View", lightingDebugView.ToString()))
                {
                    foreach (LightingDebugView view in Enum.GetValues<LightingDebugView>())
                    {
                        bool selectedView = lightingDebugView == view;
                        if (ImGui.Selectable(view.ToString(), selectedView))
                            lightingDebugView = view;
                        if (selectedView) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                ImGui.TextDisabled("Use debug views to inspect albedo, normals, material maps, depth and shadowing.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextUnformatted("Shadow Quality");
                if (ImGui.BeginCombo("Resolution", shadowQuality.ToString()))
                {
                    foreach (ShadowQuality quality in Enum.GetValues<ShadowQuality>())
                    {
                        bool selectedQuality = shadowQuality == quality;
                        if (ImGui.Selectable(quality.ToString(), selectedQuality))
                            shadowQuality = quality;
                        if (selectedQuality) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                ImGui.TextDisabled(ShadowQualityDescription(shadowQuality));
                ImGui.TextDisabled("Raises both the shadow-map resolution and the soft-shadow (PCF) sample count together — like Unity's Shadow Resolution + Soft Shadows combined. Higher levels look smoother and less jagged at the cost of VRAM and per-frame GPU time. Takes effect immediately — no reload needed.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextUnformatted("Color Space");
                if (ImGui.BeginCombo("Workflow", colorSpace.ToString()))
                {
                    foreach (ColorSpace space in Enum.GetValues<ColorSpace>())
                    {
                        bool selectedSpace = colorSpace == space;
                        if (ImGui.Selectable(space.ToString(), selectedSpace))
                            colorSpace = space;
                        if (selectedSpace) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                ImGui.TextDisabled(ColorSpaceDescription(colorSpace));
                ImGui.TextDisabled("Switch freely between \"Linear\" and \"Gamma\" to compare which look fits your scene best — exactly like changing Color Space in Unity's Player Settings, but live and without a reload. \"Linear\" drives physically correct lighting (recommended for PBR materials); \"Gamma\" keeps the classic, more contrasty look some stylized projects prefer. Takes effect immediately.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextUnformatted("Environment Lighting (IBL)");
                ImGui.Checkbox("IBL Diffuse Irradiance", ref iblEnabled);
                DrawUnitySliderFloat("Diffuse Strength##lightingIbl", ref iblDiffuseStrength, 0f, 1.5f, 0.01f);
                iblDiffuseStrength = Math.Clamp(iblDiffuseStrength, 0f, 1.5f);
                DrawUnitySliderFloat("Reflection Strength##lightingIblSpec", ref iblSpecularStrength, 0f, 0.35f, 0.005f);
                iblSpecularStrength = Math.Clamp(iblSpecularStrength, 0f, 0.35f);
                ImGui.TextDisabled("Diffuse irradiance + reflections seguras por material. Usa Environment Reflections y Specular Highlights en cada Material para comparar A/B.");

                ImGui.Spacing();
                ImGui.TextUnformatted("HDRI Environment (.hdr)");
                // Unity-style asset slot: click to browse (native file dialog), or
                // drag a .hdr from the Project panel. No typing required.
                string hdriSlot = string.IsNullOrWhiteSpace(hdriPath)
                    ? "None  —  click to choose .hdr"
                    : Path.GetFileName(hdriPath);
                ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.17f, 0.20f, 0.24f, 1f));
                bool clickedHdriSlot = ImGui.Button(hdriSlot + "##hdriSlot", new System.Numerics.Vector2(360f, 0f));
                ImGui.PopStyleColor(2);
                if (clickedHdriSlot)
                {
                    string? picked = BrowseForHdri();
                    if (picked != null) hdriPath = picked;
                }
                if (ImGui.BeginDragDropTarget())
                {
                    bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
                    if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) && GrokoEngine.HdrLoader.IsHdr(draggingAssetPath))
                    {
                        hdriPath = draggingAssetPath;
                        draggingAssetPath = null;
                    }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear##hdri"))
                    hdriPath = "";
                if (!string.IsNullOrWhiteSpace(hdriPath))
                    ImGui.TextDisabled(sceneRenderer.EnvironmentLoaded
                        ? "Loaded — surfaces reflect this HDRI."
                        : "Not loaded (file missing or not a .hdr). See Console.");
                ImGui.TextDisabled("Click the slot to browse for a Radiance .hdr (RGBE), or drag one from the Project panel. When set, IBL reflects this image instead of the procedural sky. (.exr is NOT supported.)");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Render Pro"))
            {
                DrawRenderProSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Layout"))
            {
                ImGui.TextWrapped("The editor remembers docked windows in the project layout file.");
                ImGui.TextDisabled(Path.GetFileName(imguiLayoutPath));
                if (ImGui.Button("Save Layout", new Vector2(-1f, 26f)))
                {
                    SaveImGuiLayout();
                    statusMessage = "Editor layout saved";
                }
                if (ImGui.Button("Reset Saved Layout", new Vector2(-1f, 26f)))
                {
                    try
                    {
                        if (File.Exists(imguiLayoutPath))
                            File.Delete(imguiLayoutPath);
                        statusMessage = "Saved layout reset; reopen editor to rebuild default layout";
                    }
                    catch (Exception ex)
                    {
                        statusMessage = "Layout reset failed: " + ex.Message;
                    }
                }
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }
    private void HandleProjectAssetZoom()
    {
        var io = ImGui.GetIO();

        bool ctrlDown =
            io.KeyCtrl ||
            KeyboardState.IsKeyDown(GlfwKeys.LeftControl) ||
            KeyboardState.IsKeyDown(GlfwKeys.RightControl);

        if (!ctrlDown)
            return;

        if (MathF.Abs(io.MouseWheel) < 0.01f)
            return;

        projectTileScale += io.MouseWheel * ProjectTileScaleWheelStep;

        projectTileScale = Math.Clamp(
            projectTileScale,
            ProjectTileScaleMin,
            ProjectTileScaleMax);

        projectListView = projectTileScale <= ProjectTileListThreshold;
    }
    private void DrawGuiDesignerWindow()
    {
        if (!showGuiDesigner)
            return;

        ImGui.SetNextWindowSize(new Vector2(430f, 520f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("GUI Designer", ref showGuiDesigner, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }
        TrackToolWindowMouse();

        DrawPanelHeader("GUI Designer", "live layout editor");
        ImGui.TextDisabled("Edita la interfaz en vivo. Guarda para que MiMotor quede con esta forma.");

        if (ImGui.BeginTabBar("GuiDesignerTabs"))
        {
            if (ImGui.BeginTabItem("Layout"))
            {
                DrawUnitySliderFloat("Hierarchy Width", ref _panelLeftW, 120f, Math.Max(180f, ClientSize.X * 0.45f), 1f);
                DrawUnitySliderFloat("Inspector Width", ref _panelRightW, 140f, Math.Max(180f, ClientSize.X * 0.45f), 1f);
                DrawUnitySliderFloat("Project Height", ref _panelBottomH, 60f, Math.Max(120f, ClientSize.Y * 0.65f), 1f);
                DrawUnitySliderFloat("Splitter Size", ref designerSplitterSize, 1f, 10f, 0.25f);
                SmallCheckbox("Show Guides", ref designerShowGuides);
                ImGui.Spacing();
                if (ImGui.Button("Center Layout", new Vector2(-1f, 26f)))
                    ApplyGuiDesignerPreset(GuiDesignerPreset.Default);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Inspector"))
            {
                DrawUnitySliderFloat("Panel Padding", ref designerPanelPadding, 0f, 14f, 0.25f);
                DrawUnitySliderFloat("Row Height", ref designerRowHeight, 16f, 30f, 0.25f);
                DrawUnitySliderFloat("Label Width", ref designerLabelRatio, 0.22f, 0.48f, 0.005f);
                ImGui.TextDisabled("Afecta labels, slots y filas tipo Unity.");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Style"))
            {
                DrawUnitySliderFloat("Panel Alpha", ref designerPanelAlpha, 0.55f, 1f, 0.01f);
                ImGui.TextDisabled("Transparencia visual de paneles principales.");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Elements"))
            {
                SmallCheckbox("Select UI Element", ref guiInspectMode);
                ImGui.TextDisabled(guiInspectMode ? "Click any supported UI element to edit its style class." : "Select a class or enable picking.");
                if (ImGui.BeginCombo("Pick Filter", guiElementPickFilter.ToString()))
                {
                    foreach (GuiElementPickFilter filter in Enum.GetValues<GuiElementPickFilter>())
                    {
                        bool selectedFilter = guiElementPickFilter == filter;
                        if (ImGui.Selectable(filter.ToString(), selectedFilter))
                            guiElementPickFilter = filter;
                        if (selectedFilter) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                if (ImGui.BeginCombo("Style Class", guiSelectedStyleClass.ToString()))
                {
                    foreach (GuiStyleClass styleClass in Enum.GetValues<GuiStyleClass>())
                    {
                        bool selectedClass = guiSelectedStyleClass == styleClass;
                        if (ImGui.Selectable(styleClass.ToString(), selectedClass))
                            guiSelectedStyleClass = styleClass;
                        if (selectedClass) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                DrawGuiStyleClassEditor(guiSelectedStyleClass);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Presets"))
            {
                if (ImGui.Button("Default", new Vector2(-1f, 26f)))
                    ApplyGuiDesignerPreset(GuiDesignerPreset.Default);
                if (ImGui.Button("Wide Inspector", new Vector2(-1f, 26f)))
                    ApplyGuiDesignerPreset(GuiDesignerPreset.WideInspector);
                if (ImGui.Button("Asset Work", new Vector2(-1f, 26f)))
                    ApplyGuiDesignerPreset(GuiDesignerPreset.AssetWork);
                if (ImGui.Button("Compact", new Vector2(-1f, 26f)))
                    ApplyGuiDesignerPreset(GuiDesignerPreset.Compact);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.Separator();
        if (ImGui.Button("Save GUI", new Vector2(132f, 26f)))
        {
            SaveEditorSettings();
            SaveImGuiLayout();
            statusMessage = "GUI layout saved";
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset GUI", new Vector2(132f, 26f)))
        {
            ApplyGuiDesignerPreset(GuiDesignerPreset.Default);
            statusMessage = "GUI layout reset";
        }
        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(88f, 26f)))
            showGuiDesigner = false;

        ImGui.End();
    }

    private void ApplyGuiDesignerPreset(GuiDesignerPreset preset)
    {
        switch (preset)
        {
            case GuiDesignerPreset.WideInspector:
                _panelLeftW = 220f;
                _panelRightW = 360f;
                _panelBottomH = 220f;
                designerPanelPadding = 5f;
                designerLabelRatio = 0.38f;
                designerRowHeight = 21f;
                break;
            case GuiDesignerPreset.AssetWork:
                _panelLeftW = 240f;
                _panelRightW = 300f;
                _panelBottomH = 300f;
                designerPanelPadding = 5f;
                designerLabelRatio = 0.34f;
                designerRowHeight = 20f;
                break;
            case GuiDesignerPreset.Compact:
                _panelLeftW = 190f;
                _panelRightW = 260f;
                _panelBottomH = 190f;
                designerPanelPadding = 3f;
                designerLabelRatio = 0.30f;
                designerRowHeight = 18f;
                break;
            default:
                _panelLeftW = 220f;
                _panelRightW = 280f;
                _panelBottomH = 220f;
                designerPanelPadding = 4f;
                designerLabelRatio = 0.34f;
                designerRowHeight = 20f;
                break;
        }

        designerSplitterSize = 3f;
        designerPanelAlpha = 1f;
        guiFontScale = 1f;
        guiButtonHeight = 24f;
        guiButtonRounding = 3f;
        guiCheckboxSize = 12f;
        guiSliderHeight = designerRowHeight;
        guiAssetSlotHeight = 20f;
        guiLabelBrightness = 0.58f;
    }

    private void DrawGuiStyleClassEditor(GuiStyleClass styleClass)
    {
        ImGui.Separator();
        switch (styleClass)
        {
            case GuiStyleClass.GlobalFont:
                DrawUnitySliderFloat("Font Scale", ref guiFontScale, 0.8f, 1.35f, 0.01f);
                break;
            case GuiStyleClass.Button:
                DrawUnitySliderFloat("Button Height", ref guiButtonHeight, 18f, 38f, 0.25f);
                DrawUnitySliderFloat("Button Rounding", ref guiButtonRounding, 0f, 12f, 0.25f);
                break;
            case GuiStyleClass.InspectorLabel:
                DrawUnitySliderFloat("Label Brightness", ref guiLabelBrightness, 0.35f, 0.95f, 0.01f);
                DrawUnitySliderFloat("Label Width", ref designerLabelRatio, 0.22f, 0.48f, 0.005f);
                break;
            case GuiStyleClass.Checkbox:
                DrawUnitySliderFloat("Checkbox Size", ref guiCheckboxSize, 8f, 20f, 0.25f);
                break;
            case GuiStyleClass.Slider:
                DrawUnitySliderFloat("Slider Height", ref guiSliderHeight, 16f, 30f, 0.25f);
                break;
            case GuiStyleClass.AssetSlot:
                DrawUnitySliderFloat("Slot Height", ref guiAssetSlotHeight, 16f, 30f, 0.25f);
                DrawUnitySliderFloat("Label Brightness", ref guiLabelBrightness, 0.35f, 0.95f, 0.01f);
                break;
            case GuiStyleClass.Panel:
                DrawUnitySliderFloat("Panel Padding", ref designerPanelPadding, 0f, 14f, 0.25f);
                DrawUnitySliderFloat("Panel Alpha", ref designerPanelAlpha, 0.55f, 1f, 0.01f);
                break;
        }
    }

    private void DrawConsoleTabContent()
    {
        DrainPendingConsoleLog();
        DrawPanelHeader("Console", statusMessage);
        int errors = consoleLog.Count(entry => entry.Severity == ConsoleSeverity.Error);
        int warnings = consoleLog.Count(entry => entry.Severity == ConsoleSeverity.Warning);
        int infos = consoleLog.Count(entry => entry.Severity == ConsoleSeverity.Info);

        if (ImGui.Button("Clear", new Vector2(58f, 24f)))
            ClearConsole();
        DrawTooltip("Clear console");
        ImGui.SameLine();
        if (ImGui.Button("Copy", new Vector2(56f, 24f)))
            ImGui.SetClipboardText(string.Join(Environment.NewLine, consoleLog.Select(entry => entry.CopyText)));
        DrawTooltip("Copy all console output");
        ImGui.SameLine();
        if (ImGui.Button("Errors", new Vector2(58f, 24f)))
            ImGui.SetClipboardText(string.Join(Environment.NewLine, consoleLog.Where(entry => entry.Severity == ConsoleSeverity.Error).Select(entry => entry.CopyText)));
        DrawTooltip("Copy errors only");
        ImGui.SameLine();
        if (DrawToggleButton($"Log {infos}", consoleShowInfo, new Vector2(74f, 24f)))
            consoleShowInfo = !consoleShowInfo;
        ImGui.SameLine();
        if (DrawToggleButton($"Warn {warnings}", consoleShowWarnings, new Vector2(82f, 24f)))
            consoleShowWarnings = !consoleShowWarnings;
        ImGui.SameLine();
        if (DrawToggleButton($"Error {errors}", consoleShowErrors, new Vector2(86f, 24f)))
            consoleShowErrors = !consoleShowErrors;
        ImGui.SameLine();
        ImGui.TextDisabled($"{consoleLog.Count} entries");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##ConsoleSearch", "Search console", ref consoleSearch, 128);
        ImGui.Separator();
        int visibleLines = consoleLog.Count(entry => ShouldShowConsoleLine(entry) && MatchesConsoleSearch(entry));
        ImGui.TextDisabled($"{visibleLines} visible");
        ImGui.BeginChild("ConsoleScroll", new Vector2(0, 0), ImGuiChildFlags.None);
        ConsoleEntry? previous = null;
        string? previousKey = null;
        int repeat = 1;
        foreach (var entry in consoleLog)
        {
            if (!ShouldShowConsoleLine(entry))
                continue;
            if (!MatchesConsoleSearch(entry))
                continue;

            string key = NormalizeConsoleLine(entry);
            if (previousKey == key)
            {
                repeat++;
                previous = entry;
                continue;
            }

            if (previous.HasValue)
                DrawConsoleLine(previous.Value, repeat);

            previous = entry;
            previousKey = key;
            repeat = 1;
        }

        if (previous.HasValue)
            DrawConsoleLine(previous.Value, repeat);
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
            ImGui.SetScrollHereY(1f);
        ImGui.EndChild();
    }

    private static void DrawConsoleLine(ConsoleEntry entry, int repeat)
    {
        var color = entry.Severity == ConsoleSeverity.Error
            ? new System.Numerics.Vector4(0.95f, 0.34f, 0.30f, 1f)
            : entry.Severity == ConsoleSeverity.Warning
                ? new System.Numerics.Vector4(0.95f, 0.70f, 0.26f, 1f)
                : new System.Numerics.Vector4(0.76f, 0.78f, 0.80f, 1f);
        string prefix = entry.Severity == ConsoleSeverity.Error ? "[ERR] " : entry.Severity == ConsoleSeverity.Warning ? "[WRN] " : "[LOG] ";
        string suffix = repeat > 1 ? $"  x{repeat}" : "";
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(prefix + entry.DisplayText + suffix);
        ImGui.PopStyleColor();
        ImGui.PushID(HashCode.Combine(entry.Time, entry.Severity, entry.Message));
        PushContextMenuStyle();
        if (ImGui.BeginPopupContextItem("ConsoleLineContext"))
        {
            if (ImGui.MenuItem("Copy Line"))
                ImGui.SetClipboardText(entry.CopyText);
            ImGui.EndPopup();
        }
        PopContextMenuStyle();
        ImGui.PopID();
        if (entry.Severity == ConsoleSeverity.Error && entry.Message.Contains(" at ", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.Indent(18f);
            ImGui.TextDisabled("Stack trace detected");
            ImGui.Unindent(18f);
        }
    }

    private static string NormalizeConsoleLine(ConsoleEntry entry)
    {
        return $"{entry.Severity}|{entry.Message}";
    }

    private bool ShouldShowConsoleLine(ConsoleEntry entry)
    {
        if (entry.Severity == ConsoleSeverity.Error) return consoleShowErrors;
        if (entry.Severity == ConsoleSeverity.Warning) return consoleShowWarnings;
        return consoleShowInfo;
    }

    private bool MatchesConsoleSearch(ConsoleEntry entry)
    {
        return string.IsNullOrWhiteSpace(consoleSearch) ||
               entry.Message.Contains(consoleSearch, StringComparison.OrdinalIgnoreCase) ||
               entry.DisplayText.Contains(consoleSearch, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DrawQualityCombo(string label, ref int value)
    {
        string[] labels = ["Off", "Low", "Medium", "High"];
        value = Math.Clamp(value, 0, labels.Length - 1);
        bool changed = false;
        if (ImGui.BeginCombo(label, labels[value]))
        {
            for (int i = 0; i < labels.Length; i++)
            {
                bool selected = value == i;
                if (ImGui.Selectable(labels[i], selected))
                {
                    value = i;
                    changed = true;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawRenderPipelineSettingsTab()
    {
        ImGui.TextColored(new Vec4(0.86f, 0.86f, 0.82f, 1f), "Groko Render Pipeline Asset");
        ImGui.TextDisabled("Ajustes globales estilo URP: render scale, MSAA, texturas, sombras, SSAO y Renderer Data.");
        ImGui.Separator();

        if (ImGui.BeginTable("##renderPipelineRoot", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Asset", ImGuiTableColumnFlags.WidthStretch, 0.92f);
            ImGui.TableSetupColumn("Renderer", ImGuiTableColumnFlags.WidthStretch, 1.08f);

            ImGui.TableNextColumn();
            DrawRenderProSection("Pipeline Asset");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("Asset Name##rpAssetName", ref renderPipelineAssetName, 96);
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("Renderer Data##rpRendererName", ref rendererDataName, 96);

            ImGui.Spacing();
            DrawRenderProSection("Rendering");
            SmallCheckbox("Depth Texture##rpDepth", ref depthTextureEnabled);
            ImGui.TextDisabled("Prepara profundidad para postprocess, soft particles y efectos de pantalla.");
            SmallCheckbox("Opaque Texture##rpOpaque", ref opaqueTextureEnabled);
            DrawStringCombo("Opaque Downsampling##rpOpaqueDown", ref opaqueDownsampling, ["None", "2x Bilinear", "4x Box", "4x Bilinear"]);
            SmallCheckbox("HDR##rpHdr", ref hdrEnabled);
            DrawMsaaCombo("MSAA##rpMsaa", ref msaaSamples);
            DrawUnitySliderFloat("Render Scale##rpScale", ref renderScale, 0.25f, 2f, 0.01f);
            renderScale = Math.Clamp(renderScale, 0.25f, 2f);
            ImGui.TextDisabled($"Target: {GetViewportRenderSize().Width} x {GetViewportRenderSize().Height}");
            ImGui.TextDisabled($"Runtime: Depth {(sceneTarget.DepthTextureEnabled ? "ON" : "OFF")} / Opaque {(sceneTarget.HasOpaqueTexture ? "Captured" : "Idle")}");

            ImGui.Spacing();
            DrawRenderProSection("Presets");
            if (ImGui.Button("Performance##rpPreset", new Vector2(112f, 26f)))
                ApplyRenderPipelinePreset(0);
            ImGui.SameLine();
            if (ImGui.Button("Balanced##rpPreset", new Vector2(104f, 26f)))
                ApplyRenderPipelinePreset(1);
            ImGui.SameLine();
            if (ImGui.Button("Cinematic##rpPreset", new Vector2(108f, 26f)))
                ApplyRenderPipelinePreset(2);

            ImGui.TableNextColumn();
            DrawRenderProSection("Renderer Data / Filtering");
            DrawLayerMaskCombo("Prepass Layer Mask##rpPrepassMask", ref prepassLayerMask);
            DrawLayerMaskCombo("Opaque Layer Mask##rpOpaqueMask", ref opaqueLayerMask);
            DrawLayerMaskCombo("Transparent Layer Mask##rpTransparentMask", ref transparentLayerMask);

            ImGui.Spacing();
            DrawRenderProSection("Renderer Data / Path");
            DrawStringCombo("Rendering Path##rpPath", ref renderingPath, ["Forward", "Forward+"]);
            ImGui.TextDisabled(renderingPath == "Forward+"
                ? "Forward+ prepara el camino para muchas luces por tile/cluster."
                : "Forward clasico: simple, estable y barato para escenas pequenas.");
            SmallCheckbox("Transparent Receive Shadows##rpTransparentShadows", ref transparentReceiveShadows);
            SmallCheckbox("FXAA##rpFxaa", ref fxaaEnabled);

            ImGui.Spacing();
            DrawRenderProSection("Screen Space Ambient Occlusion");
            SmallCheckbox("Enabled##rpSsaoEnabled", ref ssaoEnabled);
            DrawStringCombo("Method##rpSsaoMethod", ref ssaoMethod, ["Blue Noise", "Interleaved Gradient"]);
            DrawQualityCombo("Quality##rpSsaoQuality", ref ambientOcclusionQuality);
            DrawUnitySliderFloat("Intensity##rpSsaoIntensity", ref ssaoIntensity, 0f, 2f, 0.01f);
            DrawUnitySliderFloat("Radius##rpSsaoRadius", ref ssaoRadius, 0.02f, 3f, 0.01f);
            DrawUnitySliderFloat("Falloff Distance##rpSsaoFalloff", ref ssaoFalloffDistance, 1f, 500f, 1f);
            DrawUnitySliderFloat("Direct Lighting Strength##rpSsaoDirect", ref ssaoDirectLightingStrength, 0f, 1f, 0.01f);

            ImGui.Spacing();
            DrawRenderProSection("Shadows");
            DrawShadowQualityCombo("Shadow Quality##rpShadowQuality");
            DrawUnitySliderFloat("Shadow Bias##rpShadowBias", ref shadowBias, 0.1f, 4f, 0.01f);
            shadowBias = Math.Clamp(shadowBias, 0.1f, 4f);
            ImGui.TextDisabled(ShadowQualityDescription(shadowQuality));

            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.TextDisabled("Depth Texture alimenta AO/God Rays cuando esta activo. Opaque Texture ya se captura despues del resolve MSAA y queda lista para distortion/refraction.");
    }

    private static bool DrawStringCombo(string label, ref string value, IReadOnlyList<string> options)
    {
        if (options.Count == 0)
            return false;

        if (!options.Contains(value))
            value = options[0];

        bool changed = false;
        if (ImGui.BeginCombo(label, value))
        {
            foreach (string option in options)
            {
                bool selected = string.Equals(value, option, StringComparison.Ordinal);
                if (ImGui.Selectable(option, selected))
                {
                    value = option;
                    changed = true;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        return changed;
    }

    private static bool DrawLayerMaskCombo(string label, ref string value)
    {
        return DrawStringCombo(label, ref value, LayerMask.CullingMaskOptions);
    }

    private static bool DrawMsaaCombo(string label, ref int value)
    {
        int[] samples = [1, 2, 4, 8];
        if (!samples.Contains(value))
            value = 4;

        string preview = value == 1 ? "Disabled" : value + "x";
        bool changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            foreach (int sample in samples)
            {
                string text = sample == 1 ? "Disabled" : sample + "x";
                bool selected = value == sample;
                if (ImGui.Selectable(text, selected))
                {
                    value = sample;
                    changed = true;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawShadowQualityCombo(string label)
    {
        if (ImGui.BeginCombo(label, shadowQuality.ToString()))
        {
            foreach (ShadowQuality quality in Enum.GetValues<ShadowQuality>())
            {
                bool selected = shadowQuality == quality;
                if (ImGui.Selectable(quality.ToString(), selected))
                    shadowQuality = quality;
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    private void ApplyRenderPipelinePreset(int preset)
    {
        switch (preset)
        {
            case 0:
                renderingPath = "Forward";
                renderScale = 0.8f;
                msaaSamples = 2;
                fxaaEnabled = true;
                hdrEnabled = true;
                bloomQuality = 1;
                ssaoEnabled = true;
                ambientOcclusionQuality = 1;
                ssaoIntensity = 0.28f;
                ssaoRadius = 0.22f;
                shadowQuality = ShadowQuality.Medium;
                shadowBias = 1.25f;
                break;
            case 2:
                renderingPath = "Forward+";
                renderScale = 1f;
                msaaSamples = 8;
                fxaaEnabled = true;
                hdrEnabled = true;
                bloomQuality = 3;
                ssaoEnabled = true;
                ambientOcclusionQuality = 3;
                ssaoIntensity = 0.55f;
                ssaoRadius = 0.45f;
                shadowQuality = ShadowQuality.Ultra;
                shadowBias = 0.85f;
                break;
            default:
                renderingPath = "Forward+";
                renderScale = 1f;
                msaaSamples = 4;
                fxaaEnabled = true;
                hdrEnabled = true;
                bloomQuality = 2;
                ssaoEnabled = true;
                ambientOcclusionQuality = 2;
                ssaoIntensity = 0.4f;
                ssaoRadius = 0.3f;
                shadowQuality = ShadowQuality.High;
                shadowBias = 1f;
                break;
        }

        depthTextureEnabled = true;
        opaqueTextureEnabled = true;
        transparentReceiveShadows = true;
        statusMessage = "Render Pipeline preset applied";
    }

    private void DrawRenderProSettingsTab()
    {
        ImGui.TextColored(new Vec4(0.86f, 0.86f, 0.82f, 1f), "Render Debug / Quality");
        ImGui.TextDisabled("Panel profesional para calibrar IBL, sombras, AO y materiales sin tocar codigo.");
        ImGui.Separator();

        if (ImGui.BeginTable("##renderProGrid", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextColumn();
            DrawRenderProSection("Frame");
            DrawUnitySliderFloat("Render Scale##pro", ref renderScale, 0.25f, 2f, 0.01f);
            renderScale = Math.Clamp(renderScale, 0.25f, 2f);
            SmallCheckbox("FXAA##pro", ref fxaaEnabled);
            ImGui.TextDisabled($"Target: {GetViewportRenderSize().Width} x {GetViewportRenderSize().Height}");

            ImGui.Spacing();
            DrawRenderProSection("Post");
            DrawQualityCombo("Bloom Quality##pro", ref bloomQuality);
            DrawQualityCombo("AO Quality##pro", ref ambientOcclusionQuality);
            ImGui.TextDisabled("AO usa sampling tipo GTAO para contacto moderno.");

            ImGui.Spacing();
            DrawRenderProSection("Presets");
            if (ImGui.Button("Balanced##renderPreset", new Vector2(106f, 26f)))
                ApplyRenderQualityPreset(1);
            ImGui.SameLine();
            if (ImGui.Button("Performance##renderPreset", new Vector2(116f, 26f)))
                ApplyRenderQualityPreset(0);
            ImGui.SameLine();
            if (ImGui.Button("Cinematic##renderPreset", new Vector2(106f, 26f)))
                ApplyRenderQualityPreset(2);

            ImGui.TableNextColumn();
            DrawRenderProSection("Debug View");
            if (ImGui.BeginCombo("Mode##renderDebug", LightingDebugViewLabel(lightingDebugView)))
            {
                foreach (LightingDebugView view in Enum.GetValues<LightingDebugView>())
                {
                    bool selected = lightingDebugView == view;
                    if (ImGui.Selectable(LightingDebugViewLabel(view), selected))
                        lightingDebugView = view;
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(LightingDebugViewDescription(lightingDebugView));
            if (ImGui.Button("Final##debugFast", new Vector2(76f, 24f)))
                lightingDebugView = LightingDebugView.Final;
            ImGui.SameLine();
            if (ImGui.Button("Material Health##debugFast", new Vector2(136f, 24f)))
                lightingDebugView = LightingDebugView.MaterialHealth;
            ImGui.SameLine();
            if (ImGui.Button("Cascades##debugFast", new Vector2(96f, 24f)))
                lightingDebugView = LightingDebugView.ShadowCascades;

            ImGui.Spacing();
            DrawRenderProSection("Lighting / IBL");
            SmallCheckbox("IBL Diffuse Irradiance##pro", ref iblEnabled);
            DrawUnitySliderFloat("IBL Diffuse##pro", ref iblDiffuseStrength, 0f, 1.5f, 0.01f);
            iblDiffuseStrength = Math.Clamp(iblDiffuseStrength, 0f, 1.5f);
            DrawUnitySliderFloat("IBL Reflections##pro", ref iblSpecularStrength, 0f, 0.35f, 0.005f);
            iblSpecularStrength = Math.Clamp(iblSpecularStrength, 0f, 0.35f);
            ImGui.TextDisabled("Reflections filtradas: cada material puede apagarlas con Environment Reflections. Normal maps reciben blur extra para evitar artefactos.");
            if (ImGui.BeginCombo("Color Space##pro", colorSpace.ToString()))
            {
                foreach (ColorSpace space in Enum.GetValues<ColorSpace>())
                {
                    bool selected = colorSpace == space;
                    if (ImGui.Selectable(space.ToString(), selected))
                        colorSpace = space;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(colorSpace == ColorSpace.Linear
                ? "Linear recomendado para PBR fisico."
                : "Gamma mantiene look legacy/estilizado.");

            ImGui.Spacing();
            DrawRenderProSection("Shadows");
            if (ImGui.BeginCombo("Shadow Quality##pro", shadowQuality.ToString()))
            {
                foreach (ShadowQuality quality in Enum.GetValues<ShadowQuality>())
                {
                    bool selected = shadowQuality == quality;
                    if (ImGui.Selectable(quality.ToString(), selected))
                        shadowQuality = quality;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            DrawUnitySliderFloat("Shadow Bias##pro", ref shadowBias, 0.1f, 4f, 0.01f);
            shadowBias = Math.Clamp(shadowBias, 0.1f, 4f);
            ImGui.TextDisabled(ShadowQualityDescription(shadowQuality));

            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawRenderProSection("HDRI Environment");
        string hdriSlot = string.IsNullOrWhiteSpace(hdriPath)
            ? "None - click to choose .hdr"
            : Path.GetFileName(hdriPath);
        if (ImGui.Button(hdriSlot + "##hdriSlotPro", new Vector2(320f, 28f)))
        {
            string? picked = BrowseForHdri();
            if (picked != null)
                hdriPath = picked;
        }
        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) && GrokoEngine.HdrLoader.IsHdr(draggingAssetPath))
            {
                hdriPath = draggingAssetPath;
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear##hdriPro", new Vector2(72f, 28f)))
            hdriPath = "";
        ImGui.SameLine();
        ImGui.TextDisabled(sceneRenderer.EnvironmentLoaded ? "Loaded" : "No HDRI loaded");

        ImGui.Spacing();
        DrawRenderProSection("Material Calibration Checklist");
        ImGui.BulletText("Albedo: revisa que no este quemado; Material Health marca valores peligrosos.");
        ImGui.BulletText("Metallic: metal puro debe tener albedo oscuro/realista y reflejo fuerte.");
        ImGui.BulletText("Roughness: usa Roughness debug para evitar extremos accidentales.");
        ImGui.BulletText("Shadows: usa Shadow Cascades para revisar cortes de cascadas.");
    }

    private void ApplyRenderQualityPreset(int preset)
    {
        switch (preset)
        {
            case 0:
                renderScale = 0.75f;
                fxaaEnabled = true;
                bloomQuality = 1;
                ambientOcclusionQuality = 1;
                shadowQuality = ShadowQuality.Medium;
                shadowBias = 1.25f;
                iblDiffuseStrength = 0.45f;
                iblSpecularStrength = 0.10f;
                break;
            case 2:
                renderScale = 1f;
                fxaaEnabled = true;
                bloomQuality = 3;
                ambientOcclusionQuality = 3;
                shadowQuality = ShadowQuality.Ultra;
                shadowBias = 0.85f;
                iblDiffuseStrength = 0.85f;
                iblSpecularStrength = 0.22f;
                break;
            default:
                renderScale = 1f;
                fxaaEnabled = true;
                bloomQuality = 2;
                ambientOcclusionQuality = 2;
                shadowQuality = ShadowQuality.High;
                shadowBias = 1f;
                iblDiffuseStrength = 0.65f;
                iblSpecularStrength = 0.16f;
                break;
        }

        lightingDebugView = LightingDebugView.Final;
        statusMessage = "Render quality preset applied";
    }

    private static void DrawRenderProSection(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vec4(0.82f, 0.82f, 0.78f, 1f), title);
        ImGui.Separator();
    }

    private static string LightingDebugViewLabel(LightingDebugView view) => view switch
    {
        LightingDebugView.Final => "Final",
        LightingDebugView.Albedo => "Albedo",
        LightingDebugView.Normals => "Normals",
        LightingDebugView.Roughness => "Roughness",
        LightingDebugView.Metallic => "Metallic",
        LightingDebugView.Shadows => "Shadow Mask",
        LightingDebugView.ShadowCascades => "Shadow Cascades",
        LightingDebugView.IblSpecular => "IBL Specular",
        LightingDebugView.MaterialHealth => "Material Health",
        _ => view.ToString()
    };

    private static string LightingDebugViewDescription(LightingDebugView view) => view switch
    {
        LightingDebugView.Final => "Render final con postprocess, IBL, sombras y materiales.",
        LightingDebugView.Albedo => "Color base sin iluminacion. Util para detectar albedo quemado.",
        LightingDebugView.Normals => "Normales en espacio mundo. Util para normal maps invertidos o rotos.",
        LightingDebugView.Roughness => "Blanco = rugoso, negro = espejo. Ideal para calibrar PBR.",
        LightingDebugView.Metallic => "Blanco = metal, negro = dielectrico.",
        LightingDebugView.Shadows => "Mascara de sombra principal.",
        LightingDebugView.ShadowCascades => "Colores por cascada de sombra direccional.",
        LightingDebugView.IblSpecular => "Solo la energia especular del IBL.",
        LightingDebugView.MaterialHealth => "Semaforo de materiales: verde OK, amarillo revisar, rojo peligro.",
        _ => string.Empty
    };

    private void HandleEngineLogMessage(string message, string severity)
    {
        pendingConsoleLog.Enqueue(new ConsoleEntry(DateTime.Now, ParseConsoleSeverity(severity), message));
    }

    private void DrainPendingConsoleLog()
    {
        while (pendingConsoleLog.TryDequeue(out var entry))
            AddConsoleEntry(entry);
    }

    private static ConsoleSeverity ParseConsoleSeverity(string severity)
    {
        if (severity.Equals("Error", StringComparison.OrdinalIgnoreCase))
            return ConsoleSeverity.Error;
        if (severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) || severity.Equals("Warn", StringComparison.OrdinalIgnoreCase))
            return ConsoleSeverity.Warning;
        return ConsoleSeverity.Info;
    }

    private void Log(string message, ConsoleSeverity severity = ConsoleSeverity.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        AddConsoleEntry(new ConsoleEntry(DateTime.Now, severity, message));
    }

    private void ClearConsole()
    {
        consoleLog.Clear();
        while (pendingConsoleLog.TryDequeue(out _)) { }
    }

    private void AddConsoleEntry(ConsoleEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Message))
            return;

        string message = entry.Message;
        statusMessageValue = message;
        lastStatusSeverity = entry.Severity;
        lastStatusFlashTime = GLFW.GetTime();
        EditorDiagnostics.Log(message, entry.Severity.ToString());
        if (consoleLog.Count == 0 || consoleLog[^1].Message != entry.Message || consoleLog[^1].Severity != entry.Severity)
            consoleLog.Add(entry);
        if (consoleLog.Count > 500)
            consoleLog.RemoveRange(0, consoleLog.Count - 500);
    }
}
