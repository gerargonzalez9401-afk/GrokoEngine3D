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
private static void RegisterGuiElement(GuiStyleClass styleClass, string label)
    {
        var app = currentDrawingApp;
        if (app == null)
            return;

        bool allowed = app.GuiElementPassesPickFilter(styleClass);
        bool selectedClass = app.guiSelectedStyleClass == styleClass;
        bool showPickOverlay = app.showGuiDesigner || app.guiInspectMode || app.designerShowGuides;
        if (showPickOverlay && ((app.guiInspectMode && allowed) || selectedClass))
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var color = selectedClass
                ? new System.Numerics.Vector4(0.35f, 0.66f, 1f, 0.55f)
                : new System.Numerics.Vector4(0.95f, 0.72f, 0.22f, 0.45f);
            ImGui.GetWindowDrawList().AddRect(min - new Vector2(1f, 1f), max + new Vector2(1f, 1f), ImGui.GetColorU32(color), 2f, ImDrawFlags.None, 1.5f);
        }

        if (app.guiInspectMode && allowed && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            app.guiSelectedStyleClass = styleClass;
            app.statusMessage = "GUI class selected: " + styleClass;
        }
    }

private bool GuiElementPassesPickFilter(GuiStyleClass styleClass)
    {
        return guiElementPickFilter switch
        {
            GuiElementPickFilter.Buttons => styleClass == GuiStyleClass.Button,
            GuiElementPickFilter.InspectorFields => styleClass is GuiStyleClass.InspectorLabel or GuiStyleClass.Slider or GuiStyleClass.Checkbox,
            GuiElementPickFilter.AssetSlots => styleClass == GuiStyleClass.AssetSlot,
            GuiElementPickFilter.Layout => styleClass == GuiStyleClass.Panel,
            _ => styleClass is GuiStyleClass.Button
                or GuiStyleClass.InspectorLabel
                or GuiStyleClass.Slider
                or GuiStyleClass.Checkbox
                or GuiStyleClass.AssetSlot
                or GuiStyleClass.Panel
                or GuiStyleClass.GlobalFont
        };
    }

private static void DrawCheckRow(string label, bool value, Action<bool> set)
    {
        FieldRow(label);
        bool v = value;
        if (ImGui.Checkbox("##" + label, ref v)) set(v);
    }

private static void DrawComboRow(string label, string[] items, int current, Action<int> set)
    {
        FieldRow(label);
        int idx = Math.Clamp(current, 0, Math.Max(0, items.Length - 1));
        if (ImGui.Combo("##" + label, ref idx, items, items.Length)) set(idx);
    }

private static void DrawSliderRow(string label, float value, float min, float max, Action<float> set)
    {
        FieldRow(label);
        float v = value;
        if (ImGui.SliderFloat("##" + label, ref v, min, max)) set(v);
    }

private static void DrawVec2Row(string label, float x, float y, float speed, float min, float max, Action<float, float> set)
    {
        FieldRow(label);
        var v = new System.Numerics.Vector2(x, y);
        if (ImGui.DragFloat2("##" + label, ref v, speed, min, max)) set(v.X, v.Y);
    }

private static void DrawVec3Row(string label, float x, float y, float z, float speed, Action<float, float, float> set)
    {
        FieldRow(label);
        var v = new System.Numerics.Vector3(x, y, z);
        if (ImGui.DragFloat3("##" + label, ref v, speed)) set(v.X, v.Y, v.Z);
    }

private static void DrawTextRightOfLabel(string label, string text)
    {
        FieldRow(label);
        ImGui.TextDisabled(text);
    }

private static void DrawAnchorPresets(UIElement el)
    {
        FieldRow("Anchor Preset");
        if (ImGui.Button("▦  Anchors / Pivot##anchorpreset", new System.Numerics.Vector2(-8f, 0f)))
            ImGui.OpenPopup("AnchorPresetsPopup");

        if (ImGui.BeginPopup("AnchorPresetsPopup"))
        {
            ImGui.TextDisabled("Anchor & pivot presets");
            ImGui.Separator();
            for (int i = 0; i < AnchorPresets.Length; i++)
            {
                var p = AnchorPresets[i];
                if (ImGui.Button(p.Label + "##ap" + i, new System.Numerics.Vector2(96f, 0f)))
                {
                    ApplyAnchorPreset(el, p.MinX, p.MinY, p.MaxX, p.MaxY, p.PivX, p.PivY);
                    ImGui.CloseCurrentPopup();
                }
                if (i % 3 != 2) ImGui.SameLine();
            }
            ImGui.Separator();
            if (ImGui.Button("Stretch Horizontal", new System.Numerics.Vector2(150f, 0f)))
            { ApplyAnchorPreset(el, 0f, el.AnchorMinY, 1f, el.AnchorMaxY, 0.5f, el.PivotY); ImGui.CloseCurrentPopup(); }
            if (ImGui.Button("Stretch Vertical", new System.Numerics.Vector2(150f, 0f)))
            { ApplyAnchorPreset(el, el.AnchorMinX, 0f, el.AnchorMaxX, 1f, el.PivotX, 0.5f); ImGui.CloseCurrentPopup(); }
            if (ImGui.Button("Stretch All", new System.Numerics.Vector2(150f, 0f)))
            { ApplyAnchorPreset(el, 0f, 0f, 1f, 1f, 0.5f, 0.5f); ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
    }

private static void ApplyAnchorPreset(UIElement el, float minX, float minY, float maxX, float maxY, float pivX, float pivY)
    {
        el.AnchorMinX = minX; el.AnchorMinY = minY;
        el.AnchorMaxX = maxX; el.AnchorMaxY = maxY;
        el.PivotX = pivX; el.PivotY = pivY;
        el.PosX = 0f; el.PosY = 0f; // snap al ancla elegida

        // Con anclas separadas, Width/Height funcionan como Size Delta.
        // Para presets Stretch se deja en 0 igual que en Unity.
        if (Math.Abs(minX - maxX) > 0.0001f) el.Width = 0f;
        if (Math.Abs(minY - maxY) > 0.0001f) el.Height = 0f;
    }

private static void DrawRectTransformSection(UIElement el)
    {
        DrawAnchorPresets(el);
        DrawVec3Row("Anchored Position", el.PosX, el.PosY, el.PosZ, 1f, (x, y, z) => { el.PosX = x; el.PosY = y; el.PosZ = z; });
        DrawVec2Row("Size Delta", el.Width, el.Height, 1f, -100000f, 100000f, (x, y) => { el.Width = x; el.Height = y; });
        DrawCheckRow("Use Offsets", el.UseOffsets, v => el.UseOffsets = v);
        if (el.UseOffsets)
        {
            DrawVec2Row("Left / Right", el.Left, el.Right, 1f, -100000f, 100000f, (x, y) => { el.Left = x; el.Right = y; });
            DrawVec2Row("Top / Bottom", el.Top, el.Bottom, 1f, -100000f, 100000f, (x, y) => { el.Top = x; el.Bottom = y; });
        }
        if (ImGui.TreeNode("Anchors"))
        {
            DrawVec2Row("Min", el.AnchorMinX, el.AnchorMinY, 0.01f, 0f, 1f, (x, y) => { el.AnchorMinX = x; el.AnchorMinY = y; });
            DrawVec2Row("Max", el.AnchorMaxX, el.AnchorMaxY, 0.01f, 0f, 1f, (x, y) => { el.AnchorMaxX = x; el.AnchorMaxY = y; });
            ImGui.TreePop();
        }
        DrawVec2Row("Pivot", el.PivotX, el.PivotY, 0.01f, 0f, 1f, (x, y) => { el.PivotX = x; el.PivotY = y; });
        DrawVec3Row("Rotation", el.RotX, el.RotY, el.RotZ, 0.5f, (x, y, z) => { el.RotX = x; el.RotY = y; el.RotZ = z; });
        DrawVec3Row("Scale", el.ScaleX, el.ScaleY, el.ScaleZ, 0.01f, (x, y, z) => { el.ScaleX = x; el.ScaleY = y; el.ScaleZ = z; });
        DrawFloat("Sort Order", el.SortOrder, v => el.SortOrder = (int)v, 1f, -10000f, 10000f);
    }

private void DrawCanvasInspector(Canvas canvas)
    {
        // El "Rect Transform" (driven by Canvas) se dibuja arriba en lugar del Transform normal (DrawTransform).
        // ── Canvas ──
        if (Section("Canvas"))
        {
        string[] renderModes = { "Screen Space - Overlay", "Screen Space - Camera", "World Space" };
        DrawComboRow("Render Mode", renderModes, canvas.RenderMode, v => canvas.RenderMode = v);

        string[] sortingLayers = { "Default" };

        if (canvas.RenderMode == 0) // Screen Space - Overlay
        {
            DrawCheckRow("Pixel Perfect", canvas.PixelPerfect, v => canvas.PixelPerfect = v);
            DrawFloat("Sort Order", canvas.SortOrder, v => canvas.SortOrder = (int)v, 1f, -1000f, 1000f);
            DrawComboRow("Target Display", new[] { "Display 1" }, canvas.TargetDisplay, v => canvas.TargetDisplay = v);
        }
        else if (canvas.RenderMode == 1) // Screen Space - Camera
        {
            DrawCheckRow("Pixel Perfect", canvas.PixelPerfect, v => canvas.PixelPerfect = v);
            DrawString("Render Camera", canvas.RenderCameraName, v => canvas.RenderCameraName = v);
            DrawString("Render Camera Id", canvas.RenderCameraId, v => canvas.RenderCameraId = v);
            DrawUseSelectedCameraButton("Use Selected Render Camera",
                () => { canvas.RenderCameraName = selected!.Name; canvas.RenderCameraId = selected.EditorId; });
            DrawFloat("Plane Distance", canvas.PlaneDistance, v => canvas.PlaneDistance = v, 0.1f, 0.01f, 10000f);
            DrawCheckRow("Resize Canvas", canvas.ResizeCanvas, v => canvas.ResizeCanvas = v);
            DrawSortingLayerOrder(canvas, sortingLayers);
        }
        else // World Space
        {
            DrawString("Event Camera", canvas.EventCameraName, v => canvas.EventCameraName = v);
            DrawString("Event Camera Id", canvas.EventCameraId, v => canvas.EventCameraId = v);
            DrawUseSelectedCameraButton("Use Selected Event Camera",
                () => { canvas.EventCameraName = selected!.Name; canvas.EventCameraId = selected.EditorId; });
            DrawCheckRow("Billboard", canvas.WorldSpaceBillboard, v => canvas.WorldSpaceBillboard = v);
            DrawCheckRow("Hide Behind Camera", canvas.HideWhenBehindCamera, v => canvas.HideWhenBehindCamera = v);
            DrawSortingLayerOrder(canvas, sortingLayers);
        }

        DrawComboRow("Additional Shader Channels", new[] { "Nothing", "Everything" }, canvas.AdditionalShaderChannels, v => canvas.AdditionalShaderChannels = v);
        DrawCheckRow("Vertex Color Always In Gamma Color Space", canvas.VertexColorAlwaysGammaSpace, v => canvas.VertexColorAlwaysGammaSpace = v);
        DrawCheckRow("Clip To Canvas", canvas.ClipToCanvas, v => canvas.ClipToCanvas = v);
        DrawCheckRow("Show Gizmos", canvas.ShowGizmos, v => canvas.ShowGizmos = v);
        DrawComboRow("Editor Preview", new[] { "Full Graphics", "Game Only", "Gizmos Only" }, canvas.EditorPreviewMode, v => canvas.EditorPreviewMode = v);
        DrawCheckRow("Scene Canvas Preview", canvas.SceneViewCanvasPreview, v => canvas.SceneViewCanvasPreview = v);
        if (canvas.SceneViewCanvasPreview && canvas.RenderMode == 0)
        {
            DrawFloat("Scene Preview Zoom", canvas.SceneViewZoom, v => canvas.SceneViewZoom = Math.Clamp(v, 0.05f, 4f), 0.01f, 0.05f, 4f);
            DrawFloat("Scene Preview Pan X", canvas.SceneViewPanX, v => canvas.SceneViewPanX = v, 1f, -10000f, 10000f);
            DrawFloat("Scene Preview Pan Y", canvas.SceneViewPanY, v => canvas.SceneViewPanY = v, 1f, -10000f, 10000f);
            if (ImGui.SmallButton("Reset Scene Preview"))
            {
                canvas.SceneViewZoom = 0.35f;
                canvas.SceneViewPanX = 0f;
                canvas.SceneViewPanY = 0f;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Ctrl+Wheel zoom / Middle drag pan");
        }
        DrawCheckRow("Override Sorting", canvas.OverrideSorting, v => canvas.OverrideSorting = v);
        }

        // ── Canvas Scaler ──
        if (Section("Canvas Scaler"))
        {
        string[] scaleModes = { "Constant Pixel Size", "Scale With Screen Size", "Constant Physical Size" };
        DrawComboRow("UI Scale Mode", scaleModes, canvas.UIScaleMode, v => canvas.UIScaleMode = v);

        if (canvas.RenderMode == 2)
        {
            DrawFloat("Dynamic Pixels Per Unit", canvas.DynamicPixelsPerUnit, v => canvas.DynamicPixelsPerUnit = v, 0.1f, 0.01f, 1000f);
            DrawFloat("Reference Pixels Per Unit", canvas.ReferencePixelsPerUnit, v => canvas.ReferencePixelsPerUnit = v, 1f, 1f, 1000f);
        }
        else if (canvas.UIScaleMode == 0) // Constant Pixel Size
        {
            DrawFloat("Scale Factor", canvas.ScaleFactor, v => canvas.ScaleFactor = v, 0.01f, 0.01f, 100f);
            DrawFloat("Reference Pixels Per Unit", canvas.ReferencePixelsPerUnit, v => canvas.ReferencePixelsPerUnit = v, 1f, 1f, 1000f);
        }
        else if (canvas.UIScaleMode == 1) // Scale With Screen Size
        {
            DrawVec2Row("Reference Resolution", canvas.ReferenceWidth, canvas.ReferenceHeight, 1f, 1f, 10000f,
                (x, y) => { canvas.ReferenceWidth = (int)x; canvas.ReferenceHeight = (int)y; });
            DrawComboRow("Screen Match Mode", new[] { "Match Width Or Height", "Expand", "Shrink" }, canvas.ScreenMatchMode, v => canvas.ScreenMatchMode = v);
            if (canvas.ScreenMatchMode == 0)
                DrawSliderRow("Match", canvas.MatchWidthOrHeight, 0f, 1f, v => canvas.MatchWidthOrHeight = v);
            DrawFloat("Reference Pixels Per Unit", canvas.ReferencePixelsPerUnit, v => canvas.ReferencePixelsPerUnit = v, 1f, 1f, 1000f);
        }
        else // Constant Physical Size
        {
            DrawComboRow("Physical Unit", new[] { "Centimeters", "Millimeters", "Inches", "Points", "Picas" }, canvas.PhysicalUnit, v => canvas.PhysicalUnit = v);
            DrawFloat("Fallback Screen DPI", canvas.FallbackScreenDPI, v => canvas.FallbackScreenDPI = v, 1f, 1f, 1000f);
            DrawFloat("Default Sprite DPI", canvas.DefaultSpriteDPI, v => canvas.DefaultSpriteDPI = v, 1f, 1f, 1000f);
            DrawFloat("Reference Pixels Per Unit", canvas.ReferencePixelsPerUnit, v => canvas.ReferencePixelsPerUnit = v, 1f, 1f, 1000f);
        }
        }

        // ── Graphic Raycaster ──
        if (Section("Graphic Raycaster"))
        {
        DrawCheckRow("Ignore Reversed Graphics", canvas.IgnoreReversedGraphics, v => canvas.IgnoreReversedGraphics = v);
        DrawComboRow("Blocking Objects", new[] { "None", "Two D", "Three D", "All" }, canvas.BlockingObjects, v => canvas.BlockingObjects = v);
        if (canvas.BlockingObjects != 0)
            DrawComboRow("Blocking Mask", new[] { "Everything", "Custom" }, canvas.BlockingMask == -1 ? 0 : 1, v => canvas.BlockingMask = v == 0 ? -1 : 0);
        DrawTextRightOfLabel("Pointer Over UI", UIRaycast.PointerOverUI ? (UIRaycast.HoveredObject?.Name ?? "Graphic") : "No");
        }
    }

private void DrawUseSelectedCameraButton(string label, Action assign)
    {
        bool canUse = selected != null && (selected.IsCamera || selected.GetComponent<Camera>() != null);
        FieldRow(label);
        ImGui.BeginDisabled(!canUse);
        if (ImGui.Button("Use Selected##" + label))
            assign();
        ImGui.EndDisabled();
        if (!canUse && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Selecciona un GameObject con componente Camera para asignarlo aquí.");
    }

private void DrawSortingLayerOrder(Canvas canvas, string[] sortingLayers)
    {
        DrawComboRow("Sorting Layer", sortingLayers, 0, _ => { });
        DrawFloat("Order in Layer", canvas.OrderInLayer, v => canvas.OrderInLayer = (int)v, 1f, -32768f, 32767f);
    }
}
