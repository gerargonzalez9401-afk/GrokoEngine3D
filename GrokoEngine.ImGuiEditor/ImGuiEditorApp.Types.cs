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
    private enum TransformTool
    {
        Move,
        Rotate,
        Scale
    }

    private enum AssetSortMode
    {
        Name,
        Type,
        Size,
        Modified
    }

    private enum LightingDebugView
    {
        Final,
        Albedo,
        Normals,
        Roughness,
        Metallic,
        Shadows
    }

    private enum SceneGridAxis
    {
        X,
        Y,
        Z,
        All
    }

    private enum ConsoleSeverity
    {
        Info,
        Warning,
        Error
    }

    private readonly record struct ConsoleEntry(DateTime Time, ConsoleSeverity Severity, string Message)
    {
        public string DisplayText => $"[{Time:HH:mm:ss}] {Message}";
        public string CopyText => $"[{Time:HH:mm:ss}] [{Severity}] {Message}";
    }

    private enum GuiDesignerPreset
    {
        Default,
        WideInspector,
        AssetWork,
        Compact
    }

    private enum GuiStyleClass
    {
        Button,
        InspectorLabel,
        Slider,
        Checkbox,
        AssetSlot,
        Panel,
        GlobalFont
    }

    private enum GuiElementPickFilter
    {
        EditableOnly,
        Buttons,
        InspectorFields,
        AssetSlots,
        Layout
    }

    private enum EditorIcon
    {
        Move,
        Rotate,
        Scale,
        Play,
        Stop,
        Pause,
        Step,
        Transform,
        Camera,
        Light,
        Mesh,
        Prefab,
        Script,
        Folder,
        Visible,
        Hidden,
        Lock,
        Unlock,
        Console,
        Asset,
        Cube,
        Plane,
        Frame,
        Settings,
        ShaderGraph,
        CameraGizmo,
        Save,
        Refresh
    }

    private readonly struct DisabledScope : IDisposable
    {
        private readonly bool disabled;

        public DisabledScope(bool disabled)
        {
            this.disabled = disabled;
            if (disabled)
                ImGui.BeginDisabled();
        }

        public void Dispose()
        {
            if (disabled)
                ImGui.EndDisabled();
        }
    }

    private readonly record struct SceneStateSnapshot(string Json, List<string> SelectedIds);
    private readonly record struct IconAtlasRegion(Vector2 Uv0, Vector2 Uv1);
    private readonly record struct AddComponentEntry(string Category, string Name, string Description, bool Enabled, Func<Component> Add);
    private readonly record struct ViewportResolutionPreset(string Key, string Label, int Width, int Height, bool Free);
    private sealed record PendingMaterialSave(MaterialAssetData Data, DateTime SaveAfterUtc);
    private sealed record EditorProgressTask(string Title, string Detail, Action Work);

    private sealed class EditorSettingsData
    {
        public bool VSync { get; set; }
        public bool ViewportGizmosVisible { get; set; } = true;
        public bool ViewportAnimatorDebugVisible { get; set; }
        public bool ViewportShadedMode { get; set; } = true;
        public bool ViewportLocalSpace { get; set; }
        public bool ViewportPivotCenter { get; set; } = true;
        public string ViewportResolutionPreset { get; set; } = "free";
        public float RenderScale { get; set; } = 1f;
        public bool FxaaEnabled { get; set; } = true;
        public int BloomQuality { get; set; } = 2;
        public int AmbientOcclusionQuality { get; set; } = 2;
        public float ShadowBias { get; set; } = 1f;
        public bool SceneGridVisible { get; set; } = true;
        public bool SceneGridSnapEnabled { get; set; }
        public float SceneGridSize { get; set; } = 1f;
        public float SceneGridOpacity { get; set; } = 0.55f;
        public string SceneGridAxis { get; set; } = nameof(ImGuiEditorApp.SceneGridAxis.Y);
        public bool ProjectListView { get; set; } = false;
        public float ProjectTileScale { get; set; } = 1f;
        public string ProjectSortMode { get; set; } = nameof(AssetSortMode.Name);
        public bool ProjectSortDescending { get; set; }
        public float PanelLeftWidth { get; set; } = 250f;
        public float PanelRightWidth { get; set; } = 350f;
        public float PanelBottomHeight { get; set; } = 250f;
        public float GuiPanelPadding { get; set; } = 4f;
        public float GuiRowHeight { get; set; } = 20f;
        public float GuiSplitterSize { get; set; } = 4f;
        public float GuiPanelAlpha { get; set; } = 1f;
        public float GuiLabelRatio { get; set; } = 0.34f;
        public bool GuiShowGuides { get; set; }
        public string GuiSelectedStyleClass { get; set; } = nameof(ImGuiEditorApp.GuiStyleClass.Button);
        public string GuiElementPickFilter { get; set; } = nameof(ImGuiEditorApp.GuiElementPickFilter.EditableOnly);
        public float GuiFontScale { get; set; } = 1f;
        public float GuiButtonHeight { get; set; } = 24f;
        public float GuiButtonRounding { get; set; } = 3f;
        public float GuiCheckboxSize { get; set; } = 12f;
        public float GuiSliderHeight { get; set; } = 20f;
        public float GuiAssetSlotHeight { get; set; } = 20f;
        public float GuiLabelBrightness { get; set; } = 0.58f;
        public string LightingDebugView { get; set; } = nameof(ImGuiEditorApp.LightingDebugView.Final);
        public string ShadowQuality { get; set; } = nameof(GrokoEngine.ImGuiEditor.ShadowQuality.High);
        public string ColorSpace { get; set; } = nameof(GrokoEngine.ImGuiEditor.ColorSpace.Linear);
        public bool ImageBasedLighting { get; set; } = true;
        public string HdriPath { get; set; } = "";
        public bool ConsoleShowInfo { get; set; } = true;
        public bool ConsoleShowWarnings { get; set; } = true;
        public bool ConsoleShowErrors { get; set; } = true;
    }

    private readonly record struct TransformSnapshot(Vector3 Position, Vector3 Rotation, Vector3 Scale)
    {
        public static TransformSnapshot Capture(GameObject obj) =>
            new(
                new Vector3(obj.PosX, obj.PosY, obj.PosZ),
                new Vector3(obj.RotX, obj.RotY, obj.RotZ),
                new Vector3(obj.ScaleX, obj.ScaleY, obj.ScaleZ));
    }

    private readonly record struct SelectionBounds(Vector3 Min, Vector3 Max)
    {
        public Vector3 Center => (Min + Max) * 0.5f;

        public IEnumerable<Vector3> Corners()
        {
            yield return new Vector3(Min.X, Min.Y, Min.Z);
            yield return new Vector3(Max.X, Min.Y, Min.Z);
            yield return new Vector3(Min.X, Max.Y, Min.Z);
            yield return new Vector3(Max.X, Max.Y, Min.Z);
            yield return new Vector3(Min.X, Min.Y, Max.Z);
            yield return new Vector3(Max.X, Min.Y, Max.Z);
            yield return new Vector3(Min.X, Max.Y, Max.Z);
            yield return new Vector3(Max.X, Max.Y, Max.Z);
        }
    }

    private sealed class SnapshotSceneCommand : ISceneCommand
    {
        private readonly ImGuiEditorApp app;
        private readonly string label;
        private readonly SceneStateSnapshot before;
        private readonly SceneStateSnapshot after;

        public SnapshotSceneCommand(ImGuiEditorApp app, string label, SceneStateSnapshot before, SceneStateSnapshot after)
        {
            this.app = app;
            this.label = label;
            this.before = before;
            this.after = after;
        }

        public void Execute()
        {
            app.RestoreSceneState(after);
            app.statusMessage = "Redo: " + label;
        }

        public void Undo()
        {
            app.RestoreSceneState(before);
            app.statusMessage = "Undo: " + label;
        }
    }

    internal sealed class EditorCameraState
    {
        // Cámara del editor detrás del origen mirando hacia +Z (convención Unity)
        public Vector3 Position = new(0, 3, -8);
        public Vector3 Front = new Vector3(0, -0.28f, 1f).Normalized();
        public Vector3 Up = new(0, 1, 0);
        public float Yaw = 90f;   // 90° → mirando +Z
        public float Pitch = -15f;
        public float FOV = 70f;
        public float NearClip = 0.01f;
        public float FarClip = 2000f;
        // Modo 2D (estilo Unity): proyección ortográfica de frente, sin perspectiva.
        public bool Orthographic = false;
        public float OrthoSize = 5f; // mitad de la altura visible en unidades de mundo
        public bool AntiAliasing = true;
        public int AntiAliasingSamples = 4;
        public bool FrustumCulling = false;
        public bool OcclusionCulling = false;

        public void UpdateFront()
        {
            float yaw = MathHelper.DegreesToRadians(Yaw);
            float pitch = MathHelper.DegreesToRadians(Pitch);
            Front = new Vector3(
                MathF.Cos(yaw) * MathF.Cos(pitch),
                MathF.Sin(pitch),
                MathF.Sin(yaw) * MathF.Cos(pitch)).Normalized();
        }

        public void SetLookDirection(Vector3 direction)
        {
            Front = direction.Normalized();
            Pitch = MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(Front.Y, -1f, 1f)));
            Yaw = MathHelper.RadiansToDegrees(MathF.Atan2(Front.Z, Front.X));
        }
    }
}
