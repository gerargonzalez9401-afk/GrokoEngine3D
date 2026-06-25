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
using ShaderGraphDynamicTypes = GrokoShaderGraphPro.Services.ShaderGraphDynamicTypes;
using ShaderGraphSchemaRepair = GrokoShaderGraphPro.Services.ShaderGraphSchemaRepair;
using ShaderGraphValidator = GrokoShaderGraphPro.Services.GraphValidator;
using GraphProfiler = GrokoShaderGraphPro.Services.GraphProfiler;
using GraphPin = GrokoShaderGraphPro.Models.GraphPin;
using ShaderNode = GrokoShaderGraphPro.Models.ShaderNode;
using GraphConnection = GrokoShaderGraphPro.Models.GraphConnection;
using NodeKind = GrokoShaderGraphPro.Models.NodeKind;
using PinType = GrokoShaderGraphPro.Models.PinType;
using PinDirection = GrokoShaderGraphPro.Models.PinDirection;
using GraphProperty = GrokoShaderGraphPro.Models.GraphProperty;
using GraphGroup = GrokoShaderGraphPro.Models.GraphGroup;
using PropertyAttribute = GrokoShaderGraphPro.Models.PropertyAttribute;
using PropertyColorMode = GrokoShaderGraphPro.Models.PropertyColorMode;
using TextureImportSettings = GrokoShaderGraphPro.Models.TextureImportSettings;
using GlfwKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace GrokoEngine.ImGuiEditor;
internal sealed partial class ImGuiEditorApp
{
    private void DrawShaderGraphWindow()
    {
        if (!showShaderGraph)
            return;

        if (shaderGraphMaximized)
        {
            var vp = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(vp.Pos);
            ImGui.SetNextWindowSize(vp.Size);
        }
        else
        {
            ImGui.SetNextWindowSize(shaderGraphRestoreSize, ImGuiCond.FirstUseEver);
        }

        var windowFlags = ImGuiWindowFlags.NoCollapse;
        if (shaderGraphMaximized)
            windowFlags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        if (!ImGui.Begin("Shader Graph", ref showShaderGraph, windowFlags))
        {
            ImGui.End();
            return;
        }
        TrackToolWindowMouse();

        DrawPanelHeader("Shader Graph", "GrokoEngine.ShaderGraph integration", () =>
        {
            if (ImGui.SmallButton(shaderGraphMaximized ? "Restore" : "Maximize"))
            {
                if (!shaderGraphMaximized)
                {
                    shaderGraphRestorePos = ImGui.GetWindowPos();
                    shaderGraphRestoreSize = ImGui.GetWindowSize();
                    shaderGraphMaximized = true;
                }
                else
                {
                    shaderGraphMaximized = false;
                    ImGui.SetWindowPos(shaderGraphRestorePos);
                    ImGui.SetWindowSize(shaderGraphRestoreSize);
                }
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(showShaderGraphInspector ? "Hide Inspector" : "Show Inspector"))
                showShaderGraphInspector = !showShaderGraphInspector;
            ImGui.SameLine();
            if (ImGui.SmallButton(shaderGraphInspectorFloating ? "Dock Inspector" : "Float Inspector"))
                shaderGraphInspectorFloating = !shaderGraphInspectorFloating;
            ImGui.SameLine();
            if (ImGui.SmallButton(showShaderGraphPreview ? "Hide Final Preview" : "Show Final Preview"))
            {
                showShaderGraphPreview = !showShaderGraphPreview;
                if (showShaderGraphPreview)
                    shaderGraphPreviewWidth = Math.Max(shaderGraphPreviewWidth, 240f);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(shaderGraphPreviewFloating ? "Dock Final Preview" : "Float Final Preview"))
            {
                shaderGraphPreviewFloating = !shaderGraphPreviewFloating;
                if (showShaderGraphPreview && shaderGraphPreviewFloating)
                    ResetShaderGraphFinalPreviewPanel();
            }
        }, trailingWidth: 560f);

        shaderGraphModel ??= ShaderGraphTemplates.Empty();

        var assetLabel = shaderGraphAssetPath != null ? Path.GetFileName(shaderGraphAssetPath) : "(unsaved)";
        ImGui.TextDisabled($"Graph: {shaderGraphModel.Name}  |  Asset: {assetLabel}  |  Nodes: {shaderGraphModel.Nodes.Count}  |  Connections: {shaderGraphModel.Connections.Count}");

        bool saveShortcut = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S);
        HandleShaderGraphUndoRedoShortcuts();

        if (shaderGraphAssetPath != null && (ImGui.Button("Save (Ctrl+S)") || saveShortcut))
        {
            SyncShaderGraphEditorStateToModel();
            GrokoShaderGraphPro.Services.GraphSerializer.Save(shaderGraphAssetPath, shaderGraphModel);
            shaderGraphStatus = "Shader graph saved.";
        }
        if (shaderGraphAssetPath != null)
            ImGui.SameLine();
        if (ImGui.Button("New Empty Graph"))
        {
            shaderGraphModel = ShaderGraphTemplates.Empty();
            shaderGraphAssetPath = null;
            shaderGraphCode = string.Empty;
            shaderGraphStatus = string.Empty;
            ClearShaderGraphNodeSelection();
            shaderGraphCenterPending = true;
            ResetShaderGraphEditorState();
        }
        ImGui.SameLine();
        if (ImGui.Button("Load Lava Template"))
        {
            shaderGraphModel = ShaderGraphTemplates.Lava();
            shaderGraphAssetPath = null;
            shaderGraphCode = string.Empty;
            shaderGraphStatus = string.Empty;
            ClearShaderGraphNodeSelection();
            shaderGraphCenterPending = true;
            ResetShaderGraphEditorState();
        }
        ImGui.SameLine();
        if (ImGui.Button("Load Portal Template"))
        {
            shaderGraphModel = ShaderGraphTemplates.Portal();
            shaderGraphAssetPath = null;
            shaderGraphCode = string.Empty;
            shaderGraphStatus = string.Empty;
            ClearShaderGraphNodeSelection();
            shaderGraphCenterPending = true;
            ResetShaderGraphEditorState();
        }
        ImGui.SameLine();
        if (ImGui.Button("Load Water Deep Template"))
        {
            shaderGraphModel = ShaderGraphTemplates.WaterDeep();
            shaderGraphAssetPath = null;
            shaderGraphCode = string.Empty;
            shaderGraphStatus = string.Empty;
            ClearShaderGraphNodeSelection();
            shaderGraphCenterPending = true;
            ResetShaderGraphEditorState();
        }
        ImGui.SameLine();
        if (ImGui.Button("Frame All"))
            shaderGraphCenterPending = true;
        ImGui.SameLine();
        if (ImGui.Button("Frame Selected"))
            shaderGraphFrameSelectedPending = true;
        ImGui.SameLine();
        if (ImGui.Button("Add Group"))
            AddShaderGraphGroupAroundSelection();
        ImGui.SameLine();
        if (ImGui.Button("Add Note"))
            AddShaderGraphNoteAtViewCenter();
        ImGui.SameLine();
        if (ImGui.SmallButton(shaderGraphShowMiniMap ? "Hide MiniMap" : "Show MiniMap"))
            shaderGraphShowMiniMap = !shaderGraphShowMiniMap;
        ImGui.SameLine();
        if (ImGui.Button("Generate GLSL"))
        {
            var issues = ShaderGraphValidator.Validate(shaderGraphModel);
            var errors = issues.Where(i => i.Severity == GrokoShaderGraphPro.Services.ValidationSeverity.Error).ToList();
            shaderGraphStatus = errors.Count > 0
                ? $"{errors.Count} validation error(s): {string.Join("; ", errors.Select(e => e.Message))}"
                : "GLSL fragment shader generated.";
        }

        RegenerateShaderGraphCode();

        if (!string.IsNullOrEmpty(shaderGraphStatus))
            ImGui.TextWrapped(shaderGraphStatus);

        ImGui.Separator();

        var avail = ImGui.GetContentRegionAvail();
        var codeHeight = 160f;
        var bodyHeight = Math.Max(160f, avail.Y - codeHeight - 28f);
        const float splitter = 4f;

        shaderGraphBlackboardWidth = Math.Clamp(shaderGraphBlackboardWidth, 100f, 400f);
        shaderGraphInspectorWidth = Math.Clamp(shaderGraphInspectorWidth, 160f, 500f);
        shaderGraphPreviewWidth = Math.Clamp(shaderGraphPreviewWidth, 200f, 500f);

        bool inspectorEmbedded = showShaderGraphInspector && !shaderGraphInspectorFloating;
        bool previewEmbedded = showShaderGraphPreview && !shaderGraphPreviewFloating;

        var rightWidth = (inspectorEmbedded ? shaderGraphInspectorWidth + splitter : 0f)
                       + (previewEmbedded ? shaderGraphPreviewWidth + splitter : 0f);
        var canvasWidth = avail.X - shaderGraphBlackboardWidth - splitter - rightWidth;

        var bodyOrigin = ImGui.GetCursorScreenPos();

        DrawShaderGraphBlackboard(new Vector2(shaderGraphBlackboardWidth, bodyHeight));
        ImGui.SameLine();
        DrawVerticalSplitter("##sg_split_bb", ref shaderGraphBlackboardWidth, bodyHeight, splitter, false);
        ImGui.SameLine();
        DrawShaderGraphCanvas(new Vector2(Math.Max(1f, canvasWidth), bodyHeight));

        if (inspectorEmbedded)
        {
            ImGui.SameLine();
            DrawVerticalSplitter("##sg_split_insp", ref shaderGraphInspectorWidth, bodyHeight, splitter, true);
            ImGui.SameLine();
            DrawShaderGraphInspector(new Vector2(shaderGraphInspectorWidth, bodyHeight));
        }

        if (previewEmbedded)
        {
            ImGui.SameLine();
            DrawVerticalSplitter("##sg_split_prev", ref shaderGraphPreviewWidth, bodyHeight, splitter, true);
            ImGui.SameLine();
            DrawShaderGraphPreviewPanel(new Vector2(shaderGraphPreviewWidth, bodyHeight));
        }

        var bodyContainerSize = new Vector2(avail.X, bodyHeight);

        if (showShaderGraphInspector && shaderGraphInspectorFloating)
        {
            DrawShaderGraphFloatingPanel("##sg_float_inspector", "Graph Inspector",
                ref shaderGraphInspectorFloatPos, ref shaderGraphInspectorFloatSize,
                bodyOrigin, bodyContainerSize, () => showShaderGraphInspector = false,
                DrawShaderGraphInspectorContent);
        }

        if (showShaderGraphPreview && shaderGraphPreviewFloating)
        {
            DrawShaderGraphFloatingPanel("##sg_float_preview", "Final Preview",
                ref shaderGraphPreviewFloatPos, ref shaderGraphPreviewFloatSize,
                bodyOrigin, bodyContainerSize, () => showShaderGraphPreview = false,
                () => DrawShaderGraphPreviewContent(ImGui.GetContentRegionAvail()));
        }

        ImGui.SetCursorScreenPos(bodyOrigin + new Vector2(0f, bodyHeight));

        ImGui.Separator();
        ImGui.TextUnformatted("Generated GLSL:");
        ImGui.InputTextMultiline("##ShaderGraphCode", ref shaderGraphCode, 16384, new Vector2(-1, codeHeight - 24f), ImGuiInputTextFlags.ReadOnly);

        ImGui.End();
    }

    private void ResetShaderGraphFinalPreviewPanel()
    {
        shaderGraphPreviewFloating = true;
        shaderGraphPreviewFloatPos = new Vector2(24f, 24f);
        shaderGraphPreviewFloatSize = new Vector2(280f, 340f);
        shaderGraphPreviewWidth = Math.Max(shaderGraphPreviewWidth, 240f);
    }

    private void SyncShaderGraphEditorStateToModel()
    {
        if (shaderGraphModel == null) return;
        shaderGraphModel.EditorState.PanX = shaderGraphPan.X;
        shaderGraphModel.EditorState.PanY = shaderGraphPan.Y;
        shaderGraphModel.EditorState.Zoom = shaderGraphZoom;
        shaderGraphModel.EditorState.PreviewShape = shaderGraphPreviewShape;
        shaderGraphModel.EditorState.CollapsedPreviewNodeIds = shaderGraphCollapsedNodePreviews.ToList();
    }

    private void ApplyShaderGraphEditorStateFromModel()
    {
        if (shaderGraphModel == null) return;
        var state = shaderGraphModel.EditorState;
        state.Normalize();
        shaderGraphPan = new Vector2(state.PanX, state.PanY);
        shaderGraphZoom = state.Zoom;
        shaderGraphPreviewShape = string.IsNullOrWhiteSpace(state.PreviewShape) ? "Sphere" : state.PreviewShape;
        shaderGraphCollapsedNodePreviews.Clear();
        foreach (var id in state.CollapsedPreviewNodeIds)
            shaderGraphCollapsedNodePreviews.Add(id);
        shaderGraphCenterPending = false;
    }

    private void ResetShaderGraphEditorState()
    {
        shaderGraphPan = new Vector2(40f, 40f);
        shaderGraphZoom = 1f;
        shaderGraphPreviewShape = "Sphere";
        shaderGraphCollapsedNodePreviews.Clear();
    }

    private void PushShaderGraphUndoSnapshot()
    {
        if (shaderGraphModel == null) return;
        SyncShaderGraphEditorStateToModel();
        shaderGraphUndoStack.Push(SerializeShaderGraphSnapshot(shaderGraphModel));
        shaderGraphRedoStack.Clear();
    }

    private void HandleShaderGraphUndoRedoShortcuts()
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;

        var io = ImGui.GetIO();
        if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete) && shaderGraphModel != null)
        {
            DeleteSelectedShaderGraphNodes(shaderGraphModel);
            return;
        }

        if (!io.KeyCtrl)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.Z))
            RestoreShaderGraphSnapshot(shaderGraphUndoStack, shaderGraphRedoStack);
        else if (ImGui.IsKeyPressed(ImGuiKey.Y))
            RestoreShaderGraphSnapshot(shaderGraphRedoStack, shaderGraphUndoStack);
    }

    private void RestoreShaderGraphSnapshot(Stack<string> from, Stack<string> to)
    {
        if (shaderGraphModel == null || from.Count == 0)
            return;

        SyncShaderGraphEditorStateToModel();
        to.Push(SerializeShaderGraphSnapshot(shaderGraphModel));
        shaderGraphModel = DeserializeShaderGraphSnapshot(from.Pop());
        ApplyShaderGraphEditorStateFromModel();
        shaderGraphSelectedNodeId = null;
        shaderGraphSelectedNodeIds.Clear();
        shaderGraphSelectedPropertyId = null;
        shaderGraphPreviewedCode = string.Empty;
    }

    private readonly record struct ShaderGraphNodeScreenRect(Guid Id, Vector2 Min, Vector2 Max);

    private bool IsShaderGraphNodeSelected(Guid nodeId) =>
        shaderGraphSelectedNodeIds.Contains(nodeId);

    private void ClearShaderGraphNodeSelection()
    {
        shaderGraphSelectedNodeId = null;
        shaderGraphSelectedNodeIds.Clear();
    }

    private void SelectOnlyShaderGraphNode(Guid nodeId)
    {
        shaderGraphSelectedNodeIds.Clear();
        shaderGraphSelectedNodeIds.Add(nodeId);
        shaderGraphSelectedNodeId = nodeId;
        shaderGraphSelectedPropertyId = null;
        shaderGraphSelectedGroupId = null;
    }

    private void ToggleShaderGraphNodeSelection(Guid nodeId)
    {
        if (!shaderGraphSelectedNodeIds.Remove(nodeId))
            shaderGraphSelectedNodeIds.Add(nodeId);

        shaderGraphSelectedNodeId = shaderGraphSelectedNodeIds.Contains(nodeId)
            ? nodeId
            : shaderGraphSelectedNodeIds.FirstOrDefault();
        if (shaderGraphSelectedNodeId == Guid.Empty)
            shaderGraphSelectedNodeId = null;

        shaderGraphSelectedPropertyId = null;
        shaderGraphSelectedGroupId = null;
    }

    private void SelectShaderGraphNodeFromClick(Guid nodeId)
    {
        var io = ImGui.GetIO();
        if (io.KeyCtrl || io.KeyShift)
            ToggleShaderGraphNodeSelection(nodeId);
        else if (!IsShaderGraphNodeSelected(nodeId) || shaderGraphSelectedNodeIds.Count <= 1)
            SelectOnlyShaderGraphNode(nodeId);
        else
            shaderGraphSelectedNodeId = nodeId;
    }

    private void SelectShaderGraphNodeForContext(Guid nodeId)
    {
        if (!IsShaderGraphNodeSelected(nodeId))
            SelectOnlyShaderGraphNode(nodeId);
        else
            shaderGraphSelectedNodeId = nodeId;
    }

    private HashSet<Guid> GetActiveShaderGraphNodeSelection()
    {
        if (shaderGraphSelectedNodeIds.Count > 0)
            return new HashSet<Guid>(shaderGraphSelectedNodeIds);

        return shaderGraphSelectedNodeId.HasValue ? [shaderGraphSelectedNodeId.Value] : [];
    }

    private void MoveShaderGraphNodeSelection(ShaderGraphModel model, ShaderNode activeNode, Vector2 delta)
    {
        if (!IsShaderGraphNodeSelected(activeNode.Id))
            SelectOnlyShaderGraphNode(activeNode.Id);

        var selectedIds = GetActiveShaderGraphNodeSelection();
        foreach (var node in model.Nodes)
        {
            if (!selectedIds.Contains(node.Id))
                continue;

            node.X += delta.X;
            node.Y += delta.Y;
        }
    }

    private void DeleteSelectedShaderGraphNodes(ShaderGraphModel model)
    {
        var selectedIds = GetActiveShaderGraphNodeSelection();
        if (selectedIds.Count == 0)
            return;

        PushShaderGraphUndoSnapshot();
        model.Connections.RemoveAll(c =>
        {
            var fromNode = model.FindNodeByPin(c.FromPinId);
            var toNode = model.FindNodeByPin(c.ToPinId);
            return (fromNode != null && selectedIds.Contains(fromNode.Id)) ||
                   (toNode != null && selectedIds.Contains(toNode.Id));
        });
        model.Nodes.RemoveAll(n => selectedIds.Contains(n.Id));
        ClearShaderGraphNodeSelection();
    }

    private static bool ShaderGraphRectsIntersect(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax) =>
        aMin.X <= bMax.X && aMax.X >= bMin.X && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y;

    private void ApplyShaderGraphBoxSelection(IReadOnlyList<ShaderGraphNodeScreenRect> nodeRects, bool final)
    {
        var min = Vector2.Min(shaderGraphBoxSelectStart, shaderGraphBoxSelectCurrent);
        var max = Vector2.Max(shaderGraphBoxSelectStart, shaderGraphBoxSelectCurrent);
        bool hasArea = MathF.Abs(max.X - min.X) > 4f || MathF.Abs(max.Y - min.Y) > 4f;

        if (!hasArea)
        {
            if (final && !shaderGraphBoxSelectAdditive)
                ClearShaderGraphNodeSelection();
            return;
        }

        var selection = shaderGraphBoxSelectAdditive
            ? new HashSet<Guid>(shaderGraphBoxSelectionBase)
            : new HashSet<Guid>();

        foreach (var rect in nodeRects)
        {
            if (ShaderGraphRectsIntersect(min, max, rect.Min, rect.Max))
                selection.Add(rect.Id);
        }

        shaderGraphSelectedNodeIds.Clear();
        foreach (var id in selection)
            shaderGraphSelectedNodeIds.Add(id);

        shaderGraphSelectedNodeId = shaderGraphSelectedNodeIds.LastOrDefault();
        if (shaderGraphSelectedNodeId == Guid.Empty)
            shaderGraphSelectedNodeId = null;

        shaderGraphSelectedPropertyId = null;
        shaderGraphSelectedGroupId = null;
    }

    private void HandleShaderGraphBoxSelection(
        ImDrawListPtr drawList,
        IReadOnlyList<ShaderGraphNodeScreenRect> nodeRects,
        bool canvasHovered,
        bool hoveredAnyNode,
        bool panModifier)
    {
        bool canStart =
            canvasHovered &&
            !hoveredAnyNode &&
            shaderGraphHoverPin == null &&
            shaderGraphDragFromPin == null &&
            !panModifier &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (canStart)
        {
            var io = ImGui.GetIO();
            shaderGraphBoxSelecting = true;
            shaderGraphBoxSelectAdditive = io.KeyCtrl || io.KeyShift;
            shaderGraphBoxSelectStart = ImGui.GetMousePos();
            shaderGraphBoxSelectCurrent = shaderGraphBoxSelectStart;
            shaderGraphBoxSelectionBase.Clear();
            foreach (var id in shaderGraphSelectedNodeIds)
                shaderGraphBoxSelectionBase.Add(id);
        }

        if (!shaderGraphBoxSelecting)
            return;

        shaderGraphBoxSelectCurrent = ImGui.GetMousePos();
        ApplyShaderGraphBoxSelection(nodeRects, final: false);

        var min = Vector2.Min(shaderGraphBoxSelectStart, shaderGraphBoxSelectCurrent);
        var max = Vector2.Max(shaderGraphBoxSelectStart, shaderGraphBoxSelectCurrent);
        var fill = ImGui.GetColorU32(new Vec4(0.24f, 0.62f, 1f, 0.14f));
        var border = ImGui.GetColorU32(new Vec4(0.38f, 0.78f, 1f, 0.95f));
        drawList.AddRectFilled(min, max, fill, 2f);
        drawList.AddRect(min, max, border, 2f, ImDrawFlags.None, 1.3f);

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            ApplyShaderGraphBoxSelection(nodeRects, final: true);
            shaderGraphBoxSelecting = false;
            shaderGraphBoxSelectionBase.Clear();
        }
    }

    private static string SerializeShaderGraphSnapshot(ShaderGraphModel model)
        => JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

    private static ShaderGraphModel DeserializeShaderGraphSnapshot(string json)
    {
        var model = JsonSerializer.Deserialize<ShaderGraphModel>(json, new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        }) ?? ShaderGraphTemplates.Empty();
        model.Normalize();
        ShaderGraphSchemaRepair.Repair(model);
        ShaderGraphDynamicTypes.Synchronize(model);
        return model;
    }

    /// <summary>Draws a draggable/resizable child panel positioned absolutely within the current window, like a Unity floating tab.</summary>
    private static void DrawShaderGraphFloatingPanel(string id, string title, ref Vector2 pos, ref Vector2 size,
        Vector2 containerOrigin, Vector2 containerSize, Action onClose, Action drawContent)
    {
        size.X = Math.Clamp(size.X, 220f, Math.Max(220f, containerSize.X));
        size.Y = Math.Clamp(size.Y, 180f, Math.Max(180f, containerSize.Y));
        pos.X = Math.Clamp(pos.X, 0f, Math.Max(0f, containerSize.X - size.X));
        pos.Y = Math.Clamp(pos.Y, 0f, Math.Max(0f, containerSize.Y - size.Y));

        var screenPos = containerOrigin + pos;
        ImGui.SetCursorScreenPos(screenPos);
        ImGui.GetWindowDrawList().AddRect(screenPos, screenPos + size, ImGui.GetColorU32(new Vec4(0.25f, 0.52f, 0.80f, 0.9f)), 2f, ImDrawFlags.None, 1.5f);
        ImGui.BeginChild(id, size, ImGuiChildFlags.None, ImGuiWindowFlags.NoBringToFrontOnFocus);

        // Title bar (drag to move, click X to close/dock)
        ImGui.PushStyleColor(ImGuiCol.Header, new Vec4(0.18f, 0.40f, 0.64f, 1f));
        ImGui.Selectable($"  {title}##titlebar", true, ImGuiSelectableFlags.None, new Vector2(size.X - 26f, 20f));
        ImGui.PopStyleColor();
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            pos += ImGui.GetIO().MouseDelta;
        ImGui.SameLine();
        if (ImGui.SmallButton("x##close"))
            onClose();

        ImGui.Separator();
        drawContent();

        // Resize handle (bottom-right corner)
        var min = ImGui.GetWindowPos();
        var handleSize = new Vector2(12f, 12f);
        var handlePos = min + size - handleSize;
        ImGui.SetCursorScreenPos(handlePos);
        ImGui.InvisibleButton("##resize", handleSize);
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
        if (ImGui.IsItemActive())
            size += ImGui.GetIO().MouseDelta;

        var dl = ImGui.GetWindowDrawList();
        dl.AddTriangleFilled(
            handlePos + new Vector2(handleSize.X, 0f),
            handlePos + handleSize,
            handlePos + new Vector2(0f, handleSize.Y),
            ImGui.GetColorU32(new Vec4(0.25f, 0.52f, 0.80f, 0.9f)));

        ImGui.EndChild();
    }

    private void DrawShaderGraphCanvas(Vector2 size)
    {
        var model = shaderGraphModel!;

        ImGui.BeginChild("##sg_canvas", size, ImGuiChildFlags.None, ImGuiWindowFlags.NoMove);

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();
        drawList.AddRectFilled(origin, origin + canvasSize, ImGui.GetColorU32(new Vec4(0.13f, 0.13f, 0.13f, 1f)));

        bool hovered = ImGui.IsWindowHovered();

        var io = ImGui.GetIO();
        bool panModifier = IsShaderGraphPanModifierDown();
        bool panningWithMouse = ImGui.IsMouseDragging(ImGuiMouseButton.Middle)
            || (panModifier && ImGui.IsMouseDragging(ImGuiMouseButton.Left));

        if (hovered && panningWithMouse)
            shaderGraphPan += ImGui.GetIO().MouseDelta;

        var wheel = io.MouseWheel;
        if (hovered && wheel != 0f)
        {
            var mouse = ImGui.GetMousePos();
            var worldBefore = (mouse - origin - shaderGraphPan) / shaderGraphZoom;
            shaderGraphZoom = Math.Clamp(shaderGraphZoom * (1f + wheel * 0.1f), 0.25f, 2.5f);
            shaderGraphPan = mouse - origin - worldBefore * shaderGraphZoom;
        }

        // Background grid, scaled and panned with the canvas.
        const float gridStep = 50f;
        var step = gridStep * shaderGraphZoom;
        if (step > 4f)
        {
            var gridColor = ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.05f));
            var startX = origin.X + Mod(shaderGraphPan.X, step);
            for (float x = startX; x < origin.X + canvasSize.X; x += step)
                drawList.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y + canvasSize.Y), gridColor);
            var startY = origin.Y + Mod(shaderGraphPan.Y, step);
            for (float y = startY; y < origin.Y + canvasSize.Y; y += step)
                drawList.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + canvasSize.X, y), gridColor);
        }

        if (shaderGraphCenterPending && model.Nodes.Count > 0)
        {
            const float nodeW = 160f;
            const float nodeHApprox = 60f;
            var minX = model.Nodes.Min(n => (float)n.X);
            var maxX = model.Nodes.Max(n => (float)n.X) + nodeW;
            var minY = model.Nodes.Min(n => (float)n.Y);
            var maxY = model.Nodes.Max(n => (float)n.Y) + nodeHApprox;
            var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            shaderGraphPan = canvasSize * 0.5f - center * shaderGraphZoom;
            shaderGraphCenterPending = false;
        }
        else if (shaderGraphCenterPending)
        {
            shaderGraphPan = canvasSize * 0.5f;
            shaderGraphCenterPending = false;
        }

        if (shaderGraphFrameSelectedPending)
        {
            var selectedNodes = model.Nodes.Where(n => IsShaderGraphNodeSelected(n.Id)).ToList();
            if (selectedNodes.Count > 0)
            {
                const float nodeW = 170f;
                const float nodeH = 90f;
                float minX = selectedNodes.Min(n => (float)n.X);
                float minY = selectedNodes.Min(n => (float)n.Y);
                float maxX = selectedNodes.Max(n => (float)n.X) + nodeW;
                float maxY = selectedNodes.Max(n => (float)n.Y) + nodeH;
                var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                shaderGraphPan = canvasSize * 0.5f - center * shaderGraphZoom;
            }
            shaderGraphFrameSelectedPending = false;
        }

        var pinScreenPos = new Dictionary<Guid, Vector2>();
        shaderGraphHoverPin = null;
        bool hoveredAnyNode = false;
        foreach (var group in model.Groups.OrderBy(g => g.Y).ThenBy(g => g.X).ToList())
            DrawShaderGraphGroup(drawList, group, origin, shaderGraphPan, shaderGraphZoom, ref hoveredAnyNode);
        var zoom = shaderGraphZoom;
        ShaderGraphSchemaRepair.Repair(model);
        ShaderGraphDynamicTypes.Synchronize(model);
        var validationByNode = ShaderGraphValidator.Validate(model)
            .Where(i => i.NodeId.HasValue && i.Severity != GrokoShaderGraphPro.Services.ValidationSeverity.Info)
            .GroupBy(i => i.NodeId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.Severity).ToList());
        var validationByPin = BuildShaderGraphPinValidationMap(model, validationByNode);
        var nodeRects = new List<ShaderGraphNodeScreenRect>(model.Nodes.Count);

        ImGui.SetWindowFontScale(zoom);
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        foreach (var node in model.Nodes)
        {
            if (node.Kind == NodeKind.ScreenPosition)
                NormalizeScreenPositionNode(model, node);
            else if (node.Kind == NodeKind.Voronoi)
                NormalizeVoronoiNode(model, node);
            else if (node.Kind == NodeKind.Time)
                NormalizeTimeNode(model, node);
            else if (node.Kind == NodeKind.TextureSample)
                NormalizeTextureSampleNode(node);
            else if (node.Kind == NodeKind.SceneDepth)
                NormalizeSceneDepthNode(model, node);
            else if (node.Kind == NodeKind.Flipbook)
                NormalizeFlipbookNode(node);
            else if (node.Kind is NodeKind.Panner or NodeKind.UVScroll)
                NormalizePannerNode(node);
            else if (node.Kind == NodeKind.EmissionPulse)
                NormalizeEmissionPulseNode(model, node);
            else if (node.Kind == NodeKind.Gradient)
                NormalizeGradientNode(model, node);
            else if (node.Kind == NodeKind.Output)
                NormalizeOutputNode(node);

            var pos = origin + shaderGraphPan + new Vector2((float)node.X, (float)node.Y) * zoom;

            if (IsShaderGraphCompactValueNode(node))
            {
                DrawShaderGraphCompactValueNode(drawList, node, pos, pinScreenPos, zoom, ref hoveredAnyNode, nodeRects);
                continue;
            }

            // Cada pin de entrada sin conexión y con un valor numérico por defecto ("0.5",
            // "vec2(0.5, 0.5)", ...) muestra su propio editor inline (estilo Unity Shader
            // Graph): 1 fila para la etiqueta + 1 fila para el widget Drag1/2/3/4.
            var inputHasWidget = new bool[node.Inputs.Count];
            var inputRowY = new float[node.Inputs.Count];
            float inputHeight = 0f;
            for (int i = 0; i < node.Inputs.Count; i++)
            {
                var pin = node.Inputs[i];
                inputRowY[i] = inputHeight;
                bool hasWidget = model.FindConnectionToInput(pin.Id) == null && ShouldDrawShaderGraphInlinePinDefault(pin);
                inputHasWidget[i] = hasWidget;
                inputHeight += 20f;
            }

            var outputHeight = node.Outputs.Count * 20f;
            var extraBodyHeight = GetShaderGraphNodeExtraBodyHeight(node, IsShaderGraphNodePreviewCollapsed(node));
            var bodyHeight = Math.Max(inputHeight, outputHeight) + extraBodyHeight;
            var width = HasShaderGraphNodePreview(node) ? ShaderGraphNodePreviewWidth : 170f;
            var size2 = new Vector2(width, 28f + bodyHeight + 8f) * zoom;
            nodeRects.Add(new ShaderGraphNodeScreenRect(node.Id, pos, pos + size2));

            bool selected = IsShaderGraphNodeSelected(node.Id);
            var headerCol = HasShaderGraphNodePreview(node)
                ? new Vec4(0.22f, 0.22f, 0.22f, 1f)
                : GetCategoryColor(GrokoShaderGraphPro.Services.NodeFactory.GetCategory(node.Kind));
            drawList.AddRectFilled(pos, pos + size2, ImGui.GetColorU32(new Vec4(0.18f, 0.18f, 0.18f, 1f)), 4f);
            drawList.AddRectFilled(pos, pos + new Vector2(size2.X, 22f * zoom), ImGui.GetColorU32(headerCol), 4f, ImDrawFlags.RoundCornersTop);
            drawList.AddText(pos + new Vector2(6f, 3f) * zoom, ImGui.GetColorU32(Vec4.One), node.Title);

            ImGui.PushID(node.Id.GetHashCode());

            // Pin hit-test buttons y los editores de valor por defecto deben dibujarse antes
            // del handle de arrastre del nodo, para que reciban el HoveredId primero;
            // de lo contrario el InvisibleButton del nodo se lleva el click.
            for (int i = 0; i < node.Inputs.Count; i++)
            {
                var pin = node.Inputs[i];
                var pinPos = pos + new Vector2(0f, 28f + inputRowY[i]) * zoom;
                pinScreenPos[pin.Id] = pinPos;
                DrawShaderGraphPin(drawList, pin, pinPos, pinScreenPos);

                if (inputHasWidget[i])
                {
                    var widgetWidth = GetShaderGraphInlinePinDefaultWidth(pin) * zoom;
                    var widgetPos = pinPos - new Vector2(widgetWidth + 10f * zoom, 8f * zoom);
                    var widgetPinPos = new Vector2(pinPos.X - 6f * zoom, pinPos.Y);
                    DrawShaderGraphInlinePinDefault(pin, widgetPos, widgetWidth);
                    var pinColor = ImGui.GetColorU32(GetPinTypeColor(pin.Type));
                    drawList.AddLine(widgetPinPos, pinPos, pinColor, 1.5f * zoom);
                    drawList.AddCircleFilled(widgetPinPos, 2.5f * zoom, pinColor);
                }
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                var pin = node.Outputs[i];
                var pinPos = pos + new Vector2(size2.X, (28f + i * 20f) * zoom);
                pinScreenPos[pin.Id] = pinPos;
                DrawShaderGraphPin(drawList, pin, pinPos, pinScreenPos);
            }

            DrawShaderGraphNodeInlineControls(node, pos, size2, Math.Max(inputHeight, outputHeight), zoom);

            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton("##sgnode", size2);
            ImGui.PopID();

            if (ImGui.IsItemHovered())
            {
                hoveredAnyNode = true;
                var tooltip = BuildShaderGraphNodeTooltip(node, validationByNode.TryGetValue(node.Id, out var issues) ? issues : null);
                if (!string.IsNullOrWhiteSpace(tooltip))
                    ImGui.SetTooltip(tooltip);
            }

            if (!panModifier && ImGui.IsItemActivated())
            {
                shaderGraphMoveUndoCaptured = false;
                if (!IsShaderGraphNodeSelected(node.Id) && !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
                    SelectOnlyShaderGraphNode(node.Id);
            }

            if (!panModifier && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                if (!shaderGraphMoveUndoCaptured)
                {
                    PushShaderGraphUndoSnapshot();
                    shaderGraphMoveUndoCaptured = true;
                }
                var d = ImGui.GetIO().MouseDelta / zoom;
                MoveShaderGraphNodeSelection(model, node, d);
                pos += d * zoom;
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                SelectShaderGraphNodeFromClick(node.Id);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                SelectShaderGraphNodeForContext(node.Id);
                shaderGraphContextNodeId = node.Id;
                ImGui.OpenPopup("##sg_node_ctx");
            }

            for (int i = 0; i < node.Inputs.Count; i++)
            {
                var pin = node.Inputs[i];
                var pinPos = pinScreenPos[pin.Id];
                // Si el pin no tiene conexión y muestra su propio editor de valor, no
                // dibujamos el conector: el hit-test invisible sigue ahí (DrawShaderGraphPin)
                // para poder arrastrar una conexión encima si hiciera falta.
                drawList.AddCircleFilled(pinPos, 5f * zoom, ImGui.GetColorU32(GetPinTypeColor(pin.Type)));
                if (validationByPin.TryGetValue(pin.Id, out var inputSeverity))
                    DrawShaderGraphPinIssueRing(drawList, pinPos, zoom, inputSeverity);
                if (shaderGraphHoverPin == pin.Id)
                    drawList.AddCircle(pinPos, 7f * zoom, ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.9f)), 0, 2f);
                var pinLabel = inputHasWidget[i] ? $"{pin.Name}({GetShaderGraphPinComponentCount(pin.Type)})" : pin.Name;
                drawList.AddText(pinPos + new Vector2(8f, -7f) * zoom, ImGui.GetColorU32(new Vec4(0.85f, 0.85f, 0.85f, 1f)), pinLabel);
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                var pin = node.Outputs[i];
                var pinPos = pinScreenPos[pin.Id];
                drawList.AddCircleFilled(pinPos, 5f * zoom, ImGui.GetColorU32(GetPinTypeColor(pin.Type)));
                if (validationByPin.TryGetValue(pin.Id, out var outputSeverity))
                    DrawShaderGraphPinIssueRing(drawList, pinPos, zoom, outputSeverity);
                if (shaderGraphHoverPin == pin.Id)
                    drawList.AddCircle(pinPos, 7f * zoom, ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.9f)), 0, 2f);
                var outputLabel = node.Kind == NodeKind.ScreenPosition && pin.Name.Equals("Out", StringComparison.OrdinalIgnoreCase)
                    ? "Out(4)"
                    : node.Kind == NodeKind.Time
                    ? $"{pin.Name}(1)"
                    : pin.Name;
                var textSize = ImGui.CalcTextSize(outputLabel);
                drawList.AddText(pinPos - new Vector2(8f * zoom + textSize.X, 7f * zoom), ImGui.GetColorU32(new Vec4(0.85f, 0.85f, 0.85f, 1f)), outputLabel);
            }

            var selectedColor = HasShaderGraphNodePreview(node)
                ? new Vec4(0.18f, 0.78f, 1f, 1f)
                : new Vec4(1f, 0.65f, 0.1f, 1f);
            drawList.AddRect(pos, pos + size2, ImGui.GetColorU32(selected ? selectedColor : new Vec4(0f, 0f, 0f, 0.6f)), 4f, ImDrawFlags.None, selected ? 2f : 1f);
            if (validationByNode.TryGetValue(node.Id, out var nodeIssues))
            {
                var severity = nodeIssues[0].Severity;
                var issueColor = severity == GrokoShaderGraphPro.Services.ValidationSeverity.Error
                    ? new Vec4(1f, 0.20f, 0.16f, 1f)
                    : new Vec4(1f, 0.74f, 0.18f, 1f);
                drawList.AddRect(pos - new Vector2(2f, 2f), pos + size2 + new Vector2(2f, 2f), ImGui.GetColorU32(issueColor), 5f, ImDrawFlags.None, 2f);
            }
        }

        ImGui.SetWindowFontScale(1f);

        // Drop target que cubre todo el canvas, para que soltar texturas o propiedades del
        // Blackboard funcione en cualquier punto vacío, no solo justo encima de un nodo.
        // Se coloca después de los botones de los nodos para no robarles el ActiveId
        // (que rompería el arrastre de nodos).
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##sg_canvas_dropzone", canvasSize);
        ImGui.SetCursorScreenPos(origin);

        if (ImGui.BeginDragDropTarget())
        {
            bool deliveredAsset = AcceptDragDropOnRelease("GROKO_ASSET");
            if (deliveredAsset && draggingAssetPath != null && File.Exists(draggingAssetPath) && MaterialAsset.IsTexturePath(draggingAssetPath))
            {
                PushShaderGraphUndoSnapshot();
                var dropPos = (ImGui.GetMousePos() - origin - shaderGraphPan) / zoom;
                var node = GrokoShaderGraphPro.Services.NodeFactory.Create(NodeKind.TextureSample, dropPos.X, dropPos.Y);
                node.TexturePath = draggingAssetPath;
                node.TextValue = MakeUniqueShaderGraphTextureUniformName(model, draggingAssetPath);
                model.Nodes.Add(node);
                SelectOnlyShaderGraphNode(node.Id);
                shaderGraphSelectedPropertyId = null;
                draggingAssetPath = null;
            }

            bool deliveredProperty = AcceptDragDropOnRelease("GROKO_SHADERGRAPH_PROPERTY");
            if (deliveredProperty && shaderGraphDraggingPropertyId.HasValue)
            {
                var prop = model.Properties.FirstOrDefault(p => p.Id == shaderGraphDraggingPropertyId.Value);
                if (prop != null)
                {
                    var kind = prop.Type switch
                    {
                        PinType.Float => NodeKind.PropertyFloat,
                        PinType.Vec2 => NodeKind.PropertyVector2,
                        PinType.Vec3 => NodeKind.PropertyVector3,
                        PinType.Vec4 => prop.ColorMode == PropertyColorMode.Hdr || prop.DisplayName.Contains("Color", StringComparison.OrdinalIgnoreCase)
                            ? NodeKind.PropertyColor
                            : NodeKind.PropertyVector4,
                        PinType.Texture2D => NodeKind.PropertyTexture2D,
                        _ => NodeKind.PropertyFloat
                    };
                    var dropPos = (ImGui.GetMousePos() - origin - shaderGraphPan) / zoom;
                    PushShaderGraphUndoSnapshot();
                    var node = GrokoShaderGraphPro.Services.NodeFactory.Create(kind, dropPos.X, dropPos.Y);
                    node.TextValue = prop.Name;
                    node.ColorHex = prop.ColorHex;
                    node.TexturePath = prop.TexturePath;
                    model.Nodes.Add(node);
                    SelectOnlyShaderGraphNode(node.Id);
                    shaderGraphSelectedPropertyId = null;
                }
                shaderGraphDraggingPropertyId = null;
            }
            ImGui.EndDragDropTarget();
        }

        // Connections
        drawList.ChannelsSetCurrent(0);
        foreach (var conn in model.Connections)
        {
            if (!pinScreenPos.TryGetValue(conn.FromPinId, out var p1) || !pinScreenPos.TryGetValue(conn.ToPinId, out var p2))
                continue;
            var pinType = model.FindPin(conn.FromPinId)?.Type ?? PinType.Float;
            DrawShaderGraphBezier(drawList, p1, p2, ImGui.GetColorU32(GetPinTypeColor(pinType)));
        }

        // Pending connection drag
        if (shaderGraphDragFromPin.HasValue)
        {
            if (pinScreenPos.TryGetValue(shaderGraphDragFromPin.Value, out var fromPos))
                DrawShaderGraphBezier(drawList, fromPos, ImGui.GetMousePos(), ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.6f)));

            // While the mouse is captured by the source pin's InvisibleButton, ImGui won't
            // report other pins as hovered, so find the nearest pin under the cursor manually.
            var mouse = ImGui.GetMousePos();
            Guid? nearest = null;
            float bestDist = 14f;
            foreach (var kv in pinScreenPos)
            {
                if (kv.Key == shaderGraphDragFromPin.Value)
                    continue;
                var dist = Vector2.Distance(kv.Value, mouse);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = kv.Key;
                }
            }
            shaderGraphHoverPin = nearest;
            if (nearest.HasValue)
            {
                drawList.ChannelsSetCurrent(1);
                drawList.AddCircle(pinScreenPos[nearest.Value], 7f, ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.9f)), 0, 2f);
                drawList.ChannelsSetCurrent(0);
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (shaderGraphHoverPin.HasValue && shaderGraphHoverPin.Value != shaderGraphDragFromPin.Value)
                    TryConnectShaderGraphPins(shaderGraphDragFromPin.Value, shaderGraphHoverPin.Value);
                shaderGraphDragFromPin = null;
            }
        }
        drawList.ChannelsMerge();

        HandleShaderGraphBoxSelection(drawList, nodeRects, hovered, hoveredAnyNode, panModifier);

        if (shaderGraphShowMiniMap)
            DrawShaderGraphMiniMap(drawList, model, origin, canvasSize);

        // Node context menu
        if (ImGui.BeginPopup("##sg_node_ctx"))
        {
            if (shaderGraphContextNodeId.HasValue)
            {
                var ctxNode = model.FindNode(shaderGraphContextNodeId.Value);
                if (ctxNode != null)
                {
                    var favorite = shaderGraphFavoriteNodeKinds.Contains(ctxNode.Kind);
                    if (ImGui.MenuItem(favorite ? "Remove From Favorites" : "Add To Favorites"))
                    {
                        if (favorite)
                            shaderGraphFavoriteNodeKinds.Remove(ctxNode.Kind);
                        else
                            shaderGraphFavoriteNodeKinds.Add(ctxNode.Kind);
                    }
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem(shaderGraphSelectedNodeIds.Count > 1 ? "Delete Selected Nodes" : "Delete Node"))
            {
                DeleteSelectedShaderGraphNodes(model);
            }
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopup("##sg_group_ctx"))
        {
            if (ImGui.MenuItem("Delete Group"))
            {
                var id = shaderGraphContextGroupId;
                if (id.HasValue)
                {
                    PushShaderGraphUndoSnapshot();
                    model.Groups.RemoveAll(g => g.Id == id.Value);
                    if (shaderGraphSelectedGroupId == id)
                        shaderGraphSelectedGroupId = null;
                }
            }
            ImGui.EndPopup();
        }

        // Empty-canvas right click -> create node menu
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !hoveredAnyNode && shaderGraphHoverPin == null && shaderGraphDragFromPin == null)
        {
            shaderGraphCreatePos = (ImGui.GetMousePos() - origin - shaderGraphPan) / zoom;
            shaderGraphCreateSearch = string.Empty;
            ImGui.OpenPopup("##sg_create_node");
        }

        if (ImGui.BeginPopup("##sg_create_node"))
        {
            if (ImGui.MenuItem("Create Group"))
            {
                PushShaderGraphUndoSnapshot();
                model.Groups.Add(new GraphGroup
                {
                    Title = "Group",
                    X = shaderGraphCreatePos.X,
                    Y = shaderGraphCreatePos.Y,
                    Width = 420,
                    Height = 260,
                    ColorHex = "#552563EB"
                });
            }
            if (ImGui.MenuItem("Create Note"))
            {
                PushShaderGraphUndoSnapshot();
                model.Groups.Add(new GraphGroup
                {
                    Title = "Note",
                    Comment = "New note",
                    X = shaderGraphCreatePos.X,
                    Y = shaderGraphCreatePos.Y,
                    Width = 260,
                    Height = 120,
                    ColorHex = "#6657572A"
                });
            }
            ImGui.Separator();
            ImGui.SetNextItemWidth(260f);
            ImGui.InputTextWithHint("##sg_node_search", "Search nodes...", ref shaderGraphCreateSearch, 96);
            ImGui.Separator();

            var query = shaderGraphCreateSearch.Trim();
            var kinds = Enum.GetValues<NodeKind>()
                .Where(kind =>
                {
                    if (string.IsNullOrWhiteSpace(query)) return true;
                    var title = GrokoShaderGraphPro.Services.NodeFactory.GetTitle(kind);
                    var category = GrokoShaderGraphPro.Services.NodeFactory.GetCategory(kind);
                    return title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           kind.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (string.IsNullOrWhiteSpace(query))
            {
                if (shaderGraphFavoriteNodeKinds.Count > 0 && ImGui.BeginMenu("Favorites"))
                {
                    foreach (var kind in shaderGraphFavoriteNodeKinds.OrderBy(GrokoShaderGraphPro.Services.NodeFactory.GetTitle))
                    {
                        if (ImGui.MenuItem(GrokoShaderGraphPro.Services.NodeFactory.GetTitle(kind)))
                            CreateShaderGraphNodeAt(kind, shaderGraphCreatePos);
                    }
                    ImGui.EndMenu();
                }

                if (shaderGraphRecentNodeKinds.Count > 0 && ImGui.BeginMenu("Recent"))
                {
                    foreach (var kind in shaderGraphRecentNodeKinds)
                    {
                        if (ImGui.MenuItem(GrokoShaderGraphPro.Services.NodeFactory.GetTitle(kind)))
                            CreateShaderGraphNodeAt(kind, shaderGraphCreatePos);
                    }
                    ImGui.EndMenu();
                }
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var kind in kinds.OrderBy(GrokoShaderGraphPro.Services.NodeFactory.GetTitle))
                {
                    var title = GrokoShaderGraphPro.Services.NodeFactory.GetTitle(kind);
                    var category = GrokoShaderGraphPro.Services.NodeFactory.GetCategory(kind);
                    if (ImGui.MenuItem($"{title}   ({category})"))
                        CreateShaderGraphNodeAt(kind, shaderGraphCreatePos);
                }
            }
            else
            {
                foreach (var group in kinds.GroupBy(GrokoShaderGraphPro.Services.NodeFactory.GetCategory))
                {
                    if (ImGui.BeginMenu(group.Key))
                    {
                        foreach (var kind in group)
                        {
                            if (ImGui.MenuItem(GrokoShaderGraphPro.Services.NodeFactory.GetTitle(kind)))
                                CreateShaderGraphNodeAt(kind, shaderGraphCreatePos);
                        }
                        ImGui.EndMenu();
                    }
                }
            }
            ImGui.EndPopup();
        }

        ImGui.EndChild();
    }

    private void DrawShaderGraphMiniMap(ImDrawListPtr drawList, ShaderGraphModel model, Vector2 canvasOrigin, Vector2 canvasSize)
    {
        if (model.Nodes.Count == 0 && model.Groups.Count == 0)
            return;

        var mapSize = new Vector2(180f, 120f);
        var mapPos = canvasOrigin + new Vector2(canvasSize.X - mapSize.X - 12f, 12f);
        var mapMax = mapPos + mapSize;
        drawList.AddRectFilled(mapPos, mapMax, ImGui.GetColorU32(new Vec4(0.08f, 0.08f, 0.08f, 0.88f)), 6f);
        drawList.AddRect(mapPos, mapMax, ImGui.GetColorU32(new Vec4(0.34f, 0.34f, 0.34f, 0.9f)), 6f);
        drawList.AddText(mapPos + new Vector2(8f, 5f), ImGui.GetColorU32(new Vec4(0.75f, 0.75f, 0.75f, 1f)), "Mini Map");
        drawList.AddText(mapPos + new Vector2(mapSize.X - 62f, 5f), ImGui.GetColorU32(new Vec4(0.62f, 0.62f, 0.62f, 1f)), "LMB pan");

        var bounds = GetShaderGraphWorldBounds(model);
        var worldSize = new Vector2(MathF.Max(1f, bounds.Max.X - bounds.Min.X), MathF.Max(1f, bounds.Max.Y - bounds.Min.Y));
        var contentPos = mapPos + new Vector2(8f, 24f);
        var contentSize = mapSize - new Vector2(16f, 32f);
        var scale = MathF.Min(contentSize.X / worldSize.X, contentSize.Y / worldSize.Y);
        var offset = contentPos + (contentSize - worldSize * scale) * 0.5f;

        Vector2 Map(Vector2 world) => offset + (world - bounds.Min) * scale;

        foreach (var group in model.Groups)
        {
            var gMin = Map(new Vector2((float)group.X, (float)group.Y));
            var gMax = Map(new Vector2((float)(group.X + group.Width), (float)(group.Y + group.Height)));
            drawList.AddRect(gMin, gMax, ImGui.GetColorU32(new Vec4(0.38f, 0.56f, 0.88f, 0.55f)), 2f);
        }

        foreach (var node in model.Nodes)
        {
            var n = Map(new Vector2((float)node.X, (float)node.Y));
            var color = IsShaderGraphNodeSelected(node.Id)
                ? new Vec4(0.20f, 0.80f, 1f, 1f)
                : GetCategoryColor(GrokoShaderGraphPro.Services.NodeFactory.GetCategory(node.Kind));
            drawList.AddRectFilled(n, n + new Vector2(16f, 8f), ImGui.GetColorU32(color), 1.5f);
        }

        var viewMin = (-shaderGraphPan / shaderGraphZoom);
        var viewMax = viewMin + canvasSize / shaderGraphZoom;
        drawList.AddRect(Map(viewMin), Map(viewMax), ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.75f)), 2f);

        var mouse = ImGui.GetMousePos();
        var hovered = mouse.X >= mapPos.X && mouse.X <= mapMax.X && mouse.Y >= mapPos.Y && mouse.Y <= mapMax.Y;
        if (hovered)
        {
            ImGui.SetTooltip("Click para mover la vista del Shader Graph.");
            if (ImGui.GetIO().MouseWheel != 0f)
                shaderGraphZoom = Math.Clamp(shaderGraphZoom * (1f + ImGui.GetIO().MouseWheel * 0.1f), 0.25f, 2.5f);
        }
        if (hovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseDragging(ImGuiMouseButton.Left)))
        {
            var clamped = Vector2.Clamp(mouse, contentPos, contentPos + contentSize);
            var world = bounds.Min + (clamped - offset) / MathF.Max(scale, 0.0001f);
            shaderGraphPan = canvasSize * 0.5f - world * shaderGraphZoom;
        }
    }

    private static (Vector2 Min, Vector2 Max) GetShaderGraphWorldBounds(ShaderGraphModel model)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        foreach (var node in model.Nodes)
        {
            var p = new Vector2((float)node.X, (float)node.Y);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p + new Vector2(240f, 260f));
        }

        foreach (var group in model.Groups)
        {
            var p = new Vector2((float)group.X, (float)group.Y);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p + new Vector2((float)group.Width, (float)group.Height));
        }

        if (min.X == float.MaxValue)
            return (new Vector2(-300f, -200f), new Vector2(300f, 200f));

        return (min - new Vector2(80f, 80f), max + new Vector2(80f, 80f));
    }

    private void DrawShaderGraphPin(ImDrawListPtr drawList, GraphPin pin, Vector2 pinPos, Dictionary<Guid, Vector2> pinScreenPos)
    {
        ImGui.SetCursorScreenPos(pinPos - new Vector2(8f, 8f));
        ImGui.PushID(pin.Id.GetHashCode());
        ImGui.InvisibleButton("##sgpin", new Vector2(16f, 16f));
        ImGui.PopID();

        if (ImGui.IsItemActivated())
            shaderGraphDragFromPin = pin.Id;
        if (ImGui.IsItemHovered())
            shaderGraphHoverPin = pin.Id;
        if (pin.Direction == PinDirection.Input && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            PushShaderGraphUndoSnapshot();
            shaderGraphModel!.Connections.RemoveAll(c => c.ToPinId == pin.Id);
            ShaderGraphDynamicTypes.Synchronize(shaderGraphModel);
        }
    }

    private void TryConnectShaderGraphPins(Guid pinAId, Guid pinBId)
    {
        var model = shaderGraphModel!;
        var pinA = model.FindPin(pinAId);
        var pinB = model.FindPin(pinBId);
        if (pinA == null || pinB == null || pinA.Direction == pinB.Direction)
            return;

        var output = pinA.Direction == PinDirection.Output ? pinA : pinB;
        var input = pinA.Direction == PinDirection.Input ? pinA : pinB;

        if ((output.Type == PinType.Texture2D) != (input.Type == PinType.Texture2D))
            return;
        if (output.Type == PinType.Texture2D && input.Type == PinType.Texture2D && output.Type != input.Type)
            return;

        PushShaderGraphUndoSnapshot();
        model.Connections.RemoveAll(c => c.ToPinId == input.Id);
        model.Connections.Add(new GraphConnection { FromPinId = output.Id, ToPinId = input.Id });
        ShaderGraphDynamicTypes.Synchronize(model);
    }

    private void CreateShaderGraphNodeAt(NodeKind kind, Vector2 worldPos)
    {
        var model = shaderGraphModel!;
        PushShaderGraphUndoSnapshot();
        var node = GrokoShaderGraphPro.Services.NodeFactory.Create(kind, worldPos.X, worldPos.Y);
        model.Nodes.Add(node);
        SelectOnlyShaderGraphNode(node.Id);

        shaderGraphRecentNodeKinds.Remove(kind);
        shaderGraphRecentNodeKinds.Insert(0, kind);
        if (shaderGraphRecentNodeKinds.Count > 8)
            shaderGraphRecentNodeKinds.RemoveRange(8, shaderGraphRecentNodeKinds.Count - 8);
    }

    private static void DrawShaderGraphBezier(ImDrawListPtr drawList, Vector2 p1, Vector2 p2, uint color)
    {
        var bend = MathF.Min(MathF.Max(MathF.Abs(p2.X - p1.X) * 0.5f, 30f), 160f);
        drawList.AddBezierCubic(p1, p1 + new Vector2(bend, 0f), p2 - new Vector2(bend, 0f), p2, color, 2.5f);
    }

    private static Dictionary<Guid, GrokoShaderGraphPro.Services.ValidationSeverity> BuildShaderGraphPinValidationMap(
        ShaderGraphModel model,
        Dictionary<Guid, List<GrokoShaderGraphPro.Services.GraphValidationIssue>> validationByNode)
    {
        var map = new Dictionary<Guid, GrokoShaderGraphPro.Services.ValidationSeverity>();

        foreach (var (nodeId, issues) in validationByNode)
        {
            var node = model.FindNode(nodeId);
            if (node == null) continue;

            foreach (var issue in issues)
            {
                foreach (var pinId in issue.PinIds)
                    SetPinSeverity(map, pinId, issue.Severity);

                var pinName = issue.Code switch
                {
                    "TEXTURE_EMPTY" or "TEXTURE_MISSING" or "NORMAL_EMPTY" => "Texture",
                    "OUTPUT_BASECOLOR" => "Base Color",
                    "ALPHA_DEFAULT" => "Alpha",
                    "PROPERTY_NAME" => node.Outputs.FirstOrDefault()?.Name,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(pinName))
                {
                    var pin = node.Input(pinName) ?? node.Output(pinName);
                    if (pin != null)
                        SetPinSeverity(map, pin.Id, issue.Severity);
                }
            }
        }

        return map;
    }

    private static void SetPinSeverity(
        Dictionary<Guid, GrokoShaderGraphPro.Services.ValidationSeverity> map,
        Guid pinId,
        GrokoShaderGraphPro.Services.ValidationSeverity severity)
    {
        if (!map.TryGetValue(pinId, out var existing) || severity > existing)
            map[pinId] = severity;
    }

    private static void DrawShaderGraphPinIssueRing(
        ImDrawListPtr drawList,
        Vector2 pinPos,
        float zoom,
        GrokoShaderGraphPro.Services.ValidationSeverity severity)
    {
        var color = severity == GrokoShaderGraphPro.Services.ValidationSeverity.Error
            ? new Vec4(1f, 0.18f, 0.12f, 1f)
            : new Vec4(1f, 0.72f, 0.16f, 1f);
        drawList.AddCircle(pinPos, 8.2f * zoom, ImGui.GetColorU32(color), 0, 2f * zoom);
    }

    private static string BuildShaderGraphNodeTooltip(
        ShaderNode node,
        List<GrokoShaderGraphPro.Services.GraphValidationIssue>? issues)
    {
        var title = GrokoShaderGraphPro.Services.NodeFactory.GetTitle(node.Kind);
        var category = GrokoShaderGraphPro.Services.NodeFactory.GetCategory(node.Kind);
        var description = GetShaderGraphNodeDescription(node.Kind);
        var lines = new List<string> { $"{title} ({category})" };

        if (!string.IsNullOrWhiteSpace(description))
            lines.Add(description);
        if (!string.IsNullOrWhiteSpace(node.Comment))
            lines.Add("Note: " + node.Comment.Trim());
        if (issues is { Count: > 0 })
        {
            lines.Add("");
            lines.AddRange(issues.Select(i => i.Message));
        }

        return string.Join("\n", lines);
    }

    private static string GetShaderGraphNodeDescription(NodeKind kind) => kind switch
    {
        NodeKind.TextureSample => "Samples a Texture2D using UV input and outputs RGB/RGBA/channels.",
        NodeKind.NormalMap => "Converts a texture into a tangent-space normal value.",
        NodeKind.NormalStrength => "Adjusts normal intensity without adding hidden time or speed logic.",
        NodeKind.Voronoi => "Procedural cell pattern with UV, angle offset and density inputs.",
        NodeKind.SceneDepth => "Reads scene depth using the selected sampling mode.",
        NodeKind.ScreenPosition => "Screen-space coordinates with Unity-style mode selection.",
        NodeKind.Time => "Engine time outputs only; connect it explicitly where animation is needed.",
        NodeKind.Add => "Adds A and B. Supports scalar/vector auto casts in the generator.",
        NodeKind.Multiply => "Multiplies A and B. Useful for masks, colors and intensities.",
        NodeKind.Lerp => "Interpolates between A and B using T.",
        NodeKind.Remap => "Maps a value from one range into another range.",
        NodeKind.OneMinus => "Returns 1 - input.",
        NodeKind.Saturate => "Clamps input between 0 and 1.",
        NodeKind.Negate => "Returns -X.",
        NodeKind.Reciprocal => "Returns 1 / X with a safe divisor.",
        NodeKind.MultiplyAdd => "Returns A * B + C.",
        NodeKind.Step => "Returns 0 or 1 based on edge comparison.",
        NodeKind.Smoothstep => "Smooth threshold interpolation.",
        NodeKind.Split or NodeKind.ChannelSplit => "Splits vector/color channels.",
        NodeKind.Combine or NodeKind.ChannelCombine => "Combines scalar channels into a vector/color.",
        NodeKind.Blend => "Blends two colors using the selected mode.",
        NodeKind.NormalBlend => "Blends two tangent-space normals.",
        NodeKind.SubGraph => "Reusable graph reference.",
        NodeKind.Output => "Final material output.",
        _ => "Shader graph node."
    };

    private static Vec4 GetPinTypeColor(PinType type) => type switch
    {
        PinType.Float => new Vec4(0.61f, 0.64f, 0.69f, 1f),
        PinType.Vec2 => new Vec4(0.20f, 0.84f, 0.79f, 1f),
        PinType.Vec3 => new Vec4(0.95f, 0.78f, 0.27f, 1f),
        PinType.Vec4 => new Vec4(0.91f, 0.45f, 0.23f, 1f),
        PinType.Texture2D => new Vec4(0.78f, 0.49f, 1f, 1f),
        _ => Vec4.One
    };

    private static Vec4 GetCategoryColor(string category) => category switch
    {
        "Constants" => new Vec4(0.35f, 0.35f, 0.40f, 1f),
        "Inputs" => new Vec4(0.30f, 0.45f, 0.55f, 1f),
        "Blackboard" => new Vec4(0.30f, 0.55f, 0.40f, 1f),
        "Math" => new Vec4(0.45f, 0.35f, 0.55f, 1f),
        "Vector" => new Vec4(0.40f, 0.40f, 0.60f, 1f),
        "Textures" => new Vec4(0.55f, 0.35f, 0.55f, 1f),
        "Color" => new Vec4(0.55f, 0.45f, 0.30f, 1f),
        "Procedural" => new Vec4(0.35f, 0.50f, 0.45f, 1f),
        "Final" => new Vec4(0.55f, 0.30f, 0.30f, 1f),
        _ => new Vec4(0.40f, 0.40f, 0.40f, 1f)
    };

    private static bool IsShaderGraphCompactValueNode(ShaderNode node) => node.Kind switch
    {
        NodeKind.PropertyFloat or NodeKind.PropertyColor or NodeKind.PropertyVector2 or NodeKind.PropertyVector3 or NodeKind.PropertyVector4 or NodeKind.PropertyTexture2D => true,
        _ => false
    };

    private void DrawShaderGraphCompactValueNode(
        ImDrawListPtr drawList,
        ShaderNode node,
        Vector2 pos,
        Dictionary<Guid, Vector2> pinScreenPos,
        float zoom,
        ref bool hoveredAnyNode,
        List<ShaderGraphNodeScreenRect> nodeRects)
    {
        var pin = node.Outputs.FirstOrDefault();
        var label = GetShaderGraphCompactValueLabel(node, pin);
        var textSize = ImGui.CalcTextSize(label);
        var width = MathF.Max(86f, textSize.X / MathF.Max(zoom, 0.001f) + 42f);
        var size = new Vector2(width, 24f) * zoom;
        nodeRects.Add(new ShaderGraphNodeScreenRect(node.Id, pos, pos + size));
        var selected = IsShaderGraphNodeSelected(node.Id);
        var rounding = 10f * zoom;

        var bg = ImGui.GetColorU32(new Vec4(0.25f, 0.25f, 0.25f, 0.98f));
        var outline = ImGui.GetColorU32(selected ? new Vec4(0.25f, 0.72f, 1f, 1f) : new Vec4(0.06f, 0.06f, 0.06f, 0.85f));
        drawList.AddRectFilled(pos, pos + size, bg, rounding);
        drawList.AddRect(pos, pos + size, outline, rounding, ImDrawFlags.None, selected ? 1.8f * zoom : 1f * zoom);

        var leftDot = pos + new Vector2(12f, 12f) * zoom;
        drawList.AddCircleFilled(leftDot, 2.2f * zoom, ImGui.GetColorU32(new Vec4(0.55f, 1f, 0.35f, 1f)));

        var labelPos = pos + new Vector2(24f, 5f) * zoom;
        drawList.AddText(labelPos, ImGui.GetColorU32(new Vec4(0.78f, 0.78f, 0.78f, 1f)), label);

        if (pin != null)
        {
            var pinPos = pos + new Vector2(size.X, size.Y * 0.5f);
            pinScreenPos[pin.Id] = pinPos;
            ImGui.PushID(node.Id.GetHashCode());
            DrawShaderGraphPin(drawList, pin, pinPos, pinScreenPos);
            ImGui.PopID();

            drawList.AddCircleFilled(pinPos, 4.5f * zoom, ImGui.GetColorU32(GetPinTypeColor(pin.Type)));
            if (shaderGraphHoverPin == pin.Id)
                drawList.AddCircle(pinPos, 6.5f * zoom, ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.9f)), 0, 2f);
        }

        ImGui.PushID(node.Id.GetHashCode());
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##sgcompactnode", size);
        ImGui.PopID();

        if (ImGui.IsItemHovered())
            hoveredAnyNode = true;

        if (!IsShaderGraphPanModifierDown() && ImGui.IsItemActivated())
        {
            shaderGraphMoveUndoCaptured = false;
            if (!IsShaderGraphNodeSelected(node.Id) && !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
                SelectOnlyShaderGraphNode(node.Id);
        }

        if (!IsShaderGraphPanModifierDown() && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            if (!shaderGraphMoveUndoCaptured)
            {
                PushShaderGraphUndoSnapshot();
                shaderGraphMoveUndoCaptured = true;
            }
            var d = ImGui.GetIO().MouseDelta / zoom;
            MoveShaderGraphNodeSelection(shaderGraphModel!, node, d);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectShaderGraphNodeFromClick(node.Id);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            SelectShaderGraphNodeForContext(node.Id);
            shaderGraphContextNodeId = node.Id;
            ImGui.OpenPopup("##sg_node_ctx");
        }
    }

    private static string GetShaderGraphCompactValueLabel(ShaderNode node, GraphPin? pin)
    {
        return node.Kind switch
        {
            NodeKind.PropertyFloat => "Float(1)",
            NodeKind.PropertyTexture2D => "Texture2D(T2)",
            NodeKind.PropertyColor => "Color(4)",
            NodeKind.PropertyVector2 => "Vector2(2)",
            NodeKind.PropertyVector3 => "Vector3(3)",
            NodeKind.PropertyVector4 => "Vector4(4)",
            _ => $"{node.Title}({GetShaderGraphPinComponentCount(pin?.Type ?? PinType.Float)})"
        };
    }

    private static readonly string[] ShaderGraphScreenPositionModes =
        { "Default", "Raw", "Center", "Tiled" };
    private static readonly string[] ShaderGraphSceneDepthModes =
        { "Linear01", "Raw", "Eye" };

    private static bool IsShaderGraphPanModifierDown()
        => ImGui.GetIO().KeyAlt || ImGui.IsKeyDown(ImGuiKey.Space);

    private static float Mod(float value, float modulus)
        => ((value % modulus) + modulus) % modulus;

    private void DrawShaderGraphGroup(
        ImDrawListPtr drawList,
        GraphGroup group,
        Vector2 origin,
        Vector2 pan,
        float zoom,
        ref bool hoveredAnyNode)
    {
        var pos = origin + pan + new Vector2((float)group.X, (float)group.Y) * zoom;
        var size = new Vector2(MathF.Max(120f, (float)group.Width), MathF.Max(70f, (float)group.Height)) * zoom;
        var selected = shaderGraphSelectedGroupId == group.Id;
        var fill = HexArgbToVec4(group.ColorHex);
        fill.W = Math.Clamp(fill.W <= 0f ? 0.26f : fill.W, 0.12f, 0.42f);
        var border = selected ? new Vec4(0.24f, 0.76f, 1f, 1f) : new Vec4(fill.X + 0.18f, fill.Y + 0.18f, fill.Z + 0.18f, 0.75f);

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(fill), 7f * zoom);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(border), 7f * zoom, ImDrawFlags.None, selected ? 2f * zoom : 1f * zoom);
        drawList.AddRectFilled(pos, pos + new Vector2(size.X, 24f * zoom), ImGui.GetColorU32(new Vec4(fill.X, fill.Y, fill.Z, 0.46f)), 7f * zoom, ImDrawFlags.RoundCornersTop);

        var title = string.IsNullOrWhiteSpace(group.Title) ? "Group" : group.Title;
        drawList.AddText(pos + new Vector2(8f, 4f) * zoom, ImGui.GetColorU32(new Vec4(0.92f, 0.92f, 0.92f, 1f)), title);
        if (!string.IsNullOrWhiteSpace(group.Comment))
            drawList.AddText(pos + new Vector2(10f, 34f) * zoom, ImGui.GetColorU32(new Vec4(0.78f, 0.78f, 0.78f, 0.9f)), group.Comment);

        ImGui.PushID(group.Id.GetHashCode());
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##sg_group_header", new Vector2(size.X, 24f * zoom));
        if (ImGui.IsItemHovered())
            hoveredAnyNode = true;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            shaderGraphSelectedGroupId = group.Id;
            ClearShaderGraphNodeSelection();
            shaderGraphSelectedPropertyId = null;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            shaderGraphSelectedGroupId = group.Id;
            ClearShaderGraphNodeSelection();
            shaderGraphSelectedPropertyId = null;
            shaderGraphContextGroupId = group.Id;
            ImGui.OpenPopup("##sg_group_ctx");
        }
        if (!IsShaderGraphPanModifierDown() && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var d = ImGui.GetIO().MouseDelta / zoom;
            group.X += d.X;
            group.Y += d.Y;
        }

        var handle = new Vector2(14f, 14f) * zoom;
        ImGui.SetCursorScreenPos(pos + size - handle);
        ImGui.InvisibleButton("##sg_group_resize", handle);
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
        if (ImGui.IsItemActive())
        {
            var d = ImGui.GetIO().MouseDelta / zoom;
            group.Width = Math.Max(120, group.Width + d.X);
            group.Height = Math.Max(70, group.Height + d.Y);
        }
        ImGui.PopID();
    }

    private const float ShaderGraphValueDragSpeed = 0.08f;
    private const float ShaderGraphNodePreviewWidth = 220f;
    private const float ShaderGraphNodePreviewSize = 216f;
    private const float ShaderGraphNodePreviewToolbarHeight = 28f;

    private static float GetShaderGraphNodeExtraBodyHeight(ShaderNode node, bool previewCollapsed)
    {
        var height = node.Kind is NodeKind.ScreenPosition or NodeKind.SceneDepth ? 34f : 0f;
        if (HasShaderGraphNodePreview(node))
        {
            if (node.Kind == NodeKind.Voronoi)
                height += ShaderGraphNodePreviewToolbarHeight;
            else if (node.Kind == NodeKind.TextureSample)
                height += ShaderGraphNodePreviewToolbarHeight * 2f;
            height += previewCollapsed ? 28f : ShaderGraphNodePreviewSize + 6f;
        }
        return height;
    }

    private static bool HasShaderGraphNodePreview(ShaderNode node) => node.Kind switch
    {
        NodeKind.Voronoi or NodeKind.Noise or NodeKind.GradientNoise or NodeKind.Checkerboard
            or NodeKind.Add or NodeKind.Multiply or NodeKind.TextureSample
            or NodeKind.Lerp or NodeKind.Gradient or NodeKind.ColorRamp or NodeKind.NormalMap
            or NodeKind.TilingOffset or NodeKind.Rotator or NodeKind.Fresnel or NodeKind.FresnelPro => true,
        _ => false
    };

    private static void NormalizeScreenPositionNode(ShaderGraphModel model, ShaderNode node)
    {
        if (string.IsNullOrWhiteSpace(node.TextValue))
            node.TextValue = "Raw";

        var outPin = node.Outputs.FirstOrDefault(p => p.Name.Equals("Out", StringComparison.OrdinalIgnoreCase))
            ?? node.Outputs.FirstOrDefault(p => p.Name.Equals("Raw", StringComparison.OrdinalIgnoreCase))
            ?? node.Outputs.FirstOrDefault(p => p.Name.Equals("UV", StringComparison.OrdinalIgnoreCase));

        var oldOutputIds = node.Outputs
            .Where(p => p.Direction == PinDirection.Output)
            .Select(p => p.Id)
            .ToHashSet();

        outPin ??= new GraphPin
        {
            NodeId = node.Id,
            Direction = PinDirection.Output
        };

        outPin.NodeId = node.Id;
        outPin.Name = "Out";
        outPin.Direction = PinDirection.Output;
        outPin.Type = PinType.Vec4;

        foreach (var conn in model.Connections)
        {
            if (oldOutputIds.Contains(conn.FromPinId))
                conn.FromPinId = outPin.Id;
        }

        node.Inputs.Clear();
        node.Outputs.Clear();
        node.Outputs.Add(outPin);
    }

    private static void NormalizeVoronoiNode(ShaderGraphModel model, ShaderNode node)
    {
        var scalePin = node.Input("Scale");
        if (scalePin != null)
            scalePin.Name = "Cell Density";

        var anglePin = node.Input("Angle Offset") ?? node.Input("AngleOffset");
        if (anglePin == null)
        {
            node.Inputs.Insert(Math.Min(1, node.Inputs.Count), new GraphPin
            {
                NodeId = node.Id,
                Name = "Angle Offset",
                Direction = PinDirection.Input,
                Type = PinType.Float,
                DefaultValue = "2.0"
            });
        }
        else
        {
            anglePin.Name = "Angle Offset";
        }

        var densityPin = node.Input("Cell Density") ?? node.Input("CellDensity");
        if (densityPin == null)
        {
            node.Inputs.Add(new GraphPin
            {
                NodeId = node.Id,
                Name = "Cell Density",
                Direction = PinDirection.Input,
                Type = PinType.Float,
                DefaultValue = "5.0"
            });
        }
        else
        {
            densityPin.Name = "Cell Density";
        }

        var removedInputIds = node.Inputs
            .Where(p => p.Name.Equals("Speed", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Id)
            .ToHashSet();
        if (removedInputIds.Count > 0)
            model.Connections.RemoveAll(c => removedInputIds.Contains(c.ToPinId));
        node.Inputs.RemoveAll(p => removedInputIds.Contains(p.Id));

        var valueOut = node.Output("Value");
        if (valueOut != null)
            valueOut.Name = "Out";
    }

    private static void NormalizeTimeNode(ShaderGraphModel model, ShaderNode node)
    {
        if (node.Inputs.Count > 0)
        {
            var removedInputIds = node.Inputs.Select(p => p.Id).ToHashSet();
            model.Connections.RemoveAll(c => removedInputIds.Contains(c.ToPinId));
        }

        node.Inputs.Clear();

        var names = new[] { "Time", "Sine Time", "Cosine Time", "Delta Time", "Smooth Delta" };
        var existing = node.Outputs
            .Where(p => p.Direction == PinDirection.Output)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        node.Outputs.Clear();
        foreach (var name in names)
        {
            if (!existing.TryGetValue(name, out var pin))
            {
                pin = new GraphPin
                {
                    NodeId = node.Id,
                    Direction = PinDirection.Output,
                    Type = PinType.Float
                };
            }

            pin.NodeId = node.Id;
            pin.Name = name;
            pin.Direction = PinDirection.Output;
            pin.Type = PinType.Float;
            node.Outputs.Add(pin);
        }
    }

    private static void NormalizeSceneDepthNode(ShaderGraphModel model, ShaderNode node)
    {
        if (string.IsNullOrWhiteSpace(node.TextValue))
            node.TextValue = "Eye";

        var outPin = node.Outputs.FirstOrDefault(p => p.Name.Equals("Out", StringComparison.OrdinalIgnoreCase))
            ?? node.Outputs.FirstOrDefault(p => p.Name.Equals("Linear01", StringComparison.OrdinalIgnoreCase))
            ?? node.Outputs.FirstOrDefault(p => p.Name.Equals("Raw", StringComparison.OrdinalIgnoreCase))
            ?? node.Outputs.FirstOrDefault(p => p.Name.Equals("Eye", StringComparison.OrdinalIgnoreCase));

        var oldOutputIds = node.Outputs
            .Where(p => p.Direction == PinDirection.Output)
            .Select(p => p.Id)
            .ToHashSet();

        outPin ??= new GraphPin
        {
            NodeId = node.Id,
            Direction = PinDirection.Output
        };

        outPin.NodeId = node.Id;
        outPin.Name = "Out";
        outPin.Direction = PinDirection.Output;
        outPin.Type = PinType.Float;

        foreach (var conn in model.Connections)
        {
            if (oldOutputIds.Contains(conn.FromPinId))
                conn.FromPinId = outPin.Id;
        }

        var uv = node.Input("UV") ?? new GraphPin
        {
            NodeId = node.Id,
            Name = "UV",
            Direction = PinDirection.Input,
            Type = PinType.Vec4,
            DefaultValue = "v_ScreenPos"
        };

        uv.NodeId = node.Id;
        uv.Name = "UV";
        uv.Direction = PinDirection.Input;
        uv.Type = PinType.Vec4;
        if (string.IsNullOrWhiteSpace(uv.DefaultValue) ||
            uv.DefaultValue.Contains("v_ScreenPos.xy", StringComparison.OrdinalIgnoreCase) ||
            uv.DefaultValue.Contains("max(v_ScreenPos.w", StringComparison.OrdinalIgnoreCase))
        {
            uv.DefaultValue = "v_ScreenPos";
        }

        node.Inputs.Clear();
        node.Inputs.Add(uv);
        node.Outputs.Clear();
        node.Outputs.Add(outPin);
        node.Comment = string.Empty;
    }

    private static void NormalizeFlipbookNode(ShaderNode node)
    {
        var frame = node.Input("Frame");
        if (frame != null && frame.DefaultValue.Contains("u_Time", StringComparison.OrdinalIgnoreCase))
            frame.DefaultValue = "0.0";
        node.Comment = string.Empty;
    }

    private static void NormalizePannerNode(ShaderNode node)
    {
        if (node.Input("Time") == null)
        {
            node.Inputs.Add(new GraphPin
            {
                NodeId = node.Id,
                Name = "Time",
                Direction = PinDirection.Input,
                Type = PinType.Float,
                DefaultValue = "0.0"
            });
        }

        node.Comment = string.Empty;
    }

    private static void NormalizeEmissionPulseNode(ShaderGraphModel model, ShaderNode node)
    {
        var color = node.Input("Color") ?? new GraphPin
        {
            NodeId = node.Id,
            Name = "Color",
            Direction = PinDirection.Input,
            Type = PinType.Vec3,
            DefaultValue = "vec3(1.0, 0.35, 0.05)"
        };

        var intensity = node.Input("Intensity")
            ?? node.Input("Pulse")
            ?? node.Input("Max")
            ?? new GraphPin
            {
                NodeId = node.Id,
                Direction = PinDirection.Input,
                Type = PinType.Float,
                DefaultValue = "1.0"
            };

        intensity.NodeId = node.Id;
        intensity.Name = "Intensity";
        intensity.Direction = PinDirection.Input;
        intensity.Type = PinType.Float;
        if (string.IsNullOrWhiteSpace(intensity.DefaultValue))
            intensity.DefaultValue = "1.0";

        var removedInputIds = node.Inputs
            .Where(p => p.Name.Equals("Speed", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("Min", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("Max", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Id)
            .Where(id => id != intensity.Id)
            .ToHashSet();

        if (removedInputIds.Count > 0)
            model.Connections.RemoveAll(c => removedInputIds.Contains(c.ToPinId));

        var colorOut = node.Output("Color") ?? new GraphPin
        {
            NodeId = node.Id,
            Name = "Color",
            Direction = PinDirection.Output,
            Type = PinType.Vec3
        };

        colorOut.NodeId = node.Id;
        colorOut.Name = "Color";
        colorOut.Direction = PinDirection.Output;
        colorOut.Type = PinType.Vec3;

        var intensityOut = node.Output("Intensity") ?? node.Output("Pulse") ?? new GraphPin
        {
            NodeId = node.Id,
            Direction = PinDirection.Output,
            Type = PinType.Float
        };

        intensityOut.NodeId = node.Id;
        intensityOut.Name = "Intensity";
        intensityOut.Direction = PinDirection.Output;
        intensityOut.Type = PinType.Float;

        node.Inputs.Clear();
        color.NodeId = node.Id;
        color.Name = "Color";
        color.Direction = PinDirection.Input;
        color.Type = PinType.Vec3;
        node.Inputs.Add(color);
        node.Inputs.Add(intensity);

        node.Outputs.Clear();
        node.Outputs.Add(colorOut);
        node.Outputs.Add(intensityOut);
        node.Comment = string.Empty;
    }

    private static void NormalizeOutputNode(ShaderNode node)
    {
        if (node.Input("Smoothness") == null)
        {
            var roughnessIndex = node.Inputs.FindIndex(p => p.Name.Equals("Roughness", StringComparison.OrdinalIgnoreCase));
            var smoothness = new GraphPin
            {
                NodeId = node.Id,
                Name = "Smoothness",
                Direction = PinDirection.Input,
                Type = PinType.Float,
                DefaultValue = "0.5"
            };
            if (roughnessIndex >= 0)
                node.Inputs.Insert(Math.Min(roughnessIndex + 1, node.Inputs.Count), smoothness);
            else
                node.Inputs.Add(smoothness);
        }
    }

    private static void NormalizeTextureSampleNode(ShaderNode node)
    {
        if (node.Title.Equals("Sample Texture2D Pro", StringComparison.OrdinalIgnoreCase))
            node.Title = "Sample Texture 2D";

        var texture = node.Input("Texture");
        if (texture == null)
        {
            texture = new GraphPin
            {
                NodeId = node.Id,
                Name = "Texture",
                Direction = PinDirection.Input,
                Type = PinType.Texture2D,
                DefaultValue = "u_MainTex"
            };
            node.Inputs.Insert(0, texture);
        }
        else
        {
            texture.NodeId = node.Id;
            texture.Name = "Texture";
            texture.Direction = PinDirection.Input;
            texture.Type = PinType.Texture2D;
            if (string.IsNullOrWhiteSpace(texture.DefaultValue))
                texture.DefaultValue = "u_MainTex";
        }

        var uv = node.Input("UV");
        if (uv == null)
        {
            uv = new GraphPin
            {
                NodeId = node.Id,
                Name = "UV",
                Direction = PinDirection.Input,
                Type = PinType.Vec2,
                DefaultValue = "v_UV"
            };
            node.Inputs.Insert(Math.Min(1, node.Inputs.Count), uv);
        }
        else
        {
            uv.NodeId = node.Id;
            uv.Name = "UV";
            uv.Direction = PinDirection.Input;
            uv.Type = PinType.Vec2;
            if (string.IsNullOrWhiteSpace(uv.DefaultValue))
                uv.DefaultValue = "v_UV";
        }

        if (node.Input("Tiling") == null)
        {
            node.Inputs.Add(new GraphPin
            {
                NodeId = node.Id,
                Name = "Tiling",
                Direction = PinDirection.Input,
                Type = PinType.Vec2,
                DefaultValue = "vec2(1.0, 1.0)"
            });
        }

        if (node.Input("Offset") == null)
        {
            node.Inputs.Add(new GraphPin
            {
                NodeId = node.Id,
                Name = "Offset",
                Direction = PinDirection.Input,
                Type = PinType.Vec2,
                DefaultValue = "vec2(0.0, 0.0)"
            });
        }
    }

    private static void NormalizeGradientNode(ShaderGraphModel model, ShaderNode node)
    {
        var t = node.Input("T");
        if (t == null)
            return;

        if (string.IsNullOrWhiteSpace(node.TextValue))
            node.TextValue = "Vertical";

        if (model.FindConnectionToInput(t.Id) != null)
            return;

        if (node.TextValue.Equals("Vertical", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.DefaultValue, "v_UV.x", StringComparison.OrdinalIgnoreCase))
        {
            node.TextValue = "Vertical";
            t.DefaultValue = "v_UV.y";
        }
        else if (node.TextValue.Equals("Horizontal", StringComparison.OrdinalIgnoreCase))
        {
            t.DefaultValue = "v_UV.x";
        }
    }

    private void DrawShaderGraphNodeInlineControls(ShaderNode node, Vector2 pos, Vector2 size, float pinBodyHeight, float zoom)
    {
        var cursorY = pos.Y + (28f + pinBodyHeight + 8f) * zoom;

        if (node.Kind == NodeKind.ScreenPosition)
        {
            var mode = string.IsNullOrWhiteSpace(node.TextValue) ? "Default" : node.TextValue.Trim();
            var modeIndex = Array.FindIndex(ShaderGraphScreenPositionModes, x => x.Equals(mode, StringComparison.OrdinalIgnoreCase));
            if (modeIndex < 0) modeIndex = 0;

            ImGui.SetCursorScreenPos(new Vector2(pos.X + 8f * zoom, cursorY));
            ImGui.TextUnformatted("Mode");
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(pos.X + size.X - 84f * zoom, cursorY - 2f * zoom));
            ImGui.SetNextItemWidth(76f * zoom);
            if (ImGui.Combo("##screen_pos_mode", ref modeIndex, ShaderGraphScreenPositionModes, ShaderGraphScreenPositionModes.Length))
                node.TextValue = ShaderGraphScreenPositionModes[modeIndex];

            cursorY += 34f * zoom;
        }
        else if (node.Kind == NodeKind.SceneDepth)
        {
            var mode = string.IsNullOrWhiteSpace(node.TextValue) ? "Linear01" : node.TextValue.Trim();
            var modeIndex = Array.FindIndex(ShaderGraphSceneDepthModes, x => x.Equals(mode, StringComparison.OrdinalIgnoreCase));
            if (modeIndex < 0) modeIndex = 0;

            ImGui.SetCursorScreenPos(new Vector2(pos.X + 8f * zoom, cursorY));
            ImGui.TextUnformatted("Sampling");
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(pos.X + size.X - 84f * zoom, cursorY - 2f * zoom));
            ImGui.SetNextItemWidth(76f * zoom);
            if (ImGui.Combo("##scene_depth_mode", ref modeIndex, ShaderGraphSceneDepthModes, ShaderGraphSceneDepthModes.Length))
                node.TextValue = ShaderGraphSceneDepthModes[modeIndex];

            cursorY += 34f * zoom;
        }

        if (HasShaderGraphNodePreview(node))
        {
            if (node.Kind == NodeKind.Voronoi)
            {
                DrawShaderGraphVoronoiPreviewToolbar(node, pos, size, cursorY, zoom);
                cursorY += ShaderGraphNodePreviewToolbarHeight * zoom;
            }
            else if (node.Kind == NodeKind.TextureSample)
            {
                DrawShaderGraphTextureSamplePreviewToolbar(node, pos, size, cursorY, zoom);
                cursorY += ShaderGraphNodePreviewToolbarHeight * 2f * zoom;
            }

            bool collapsed = IsShaderGraphNodePreviewCollapsed(node);
            var previewHeight = collapsed ? 24f * zoom : ShaderGraphNodePreviewSize * zoom;
            var previewSize = new Vector2(size.X - 4f * zoom, previewHeight);
            DrawShaderGraphNodePreview(ImGui.GetWindowDrawList(), node, new Vector2(pos.X + 2f * zoom, cursorY), previewSize, zoom, collapsed);
        }
    }

    private static void DrawShaderGraphVoronoiPreviewToolbar(ShaderNode node, Vector2 pos, Vector2 size, float y, float zoom)
    {
        var drawList = ImGui.GetWindowDrawList();
        var rowMin = new Vector2(pos.X, y);
        var rowMax = new Vector2(pos.X + size.X, y + ShaderGraphNodePreviewToolbarHeight * zoom);
        drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new Vec4(0.20f, 0.20f, 0.20f, 1f)));
        drawList.AddLine(rowMin, new Vector2(rowMax.X, rowMin.Y), ImGui.GetColorU32(new Vec4(0f, 0f, 0f, 0.35f)));

        ImGui.SetCursorScreenPos(new Vector2(pos.X + 10f * zoom, y + 6f * zoom));
        ImGui.TextUnformatted("Hash Type");

        ImGui.PushID(node.Id.GetHashCode());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + size.X - 104f * zoom, y + 5f * zoom));
        ImGui.SetNextItemWidth(94f * zoom);
        var hashIndex = 0;
        ImGui.Combo("##hash_type", ref hashIndex, new[] { "Deterministic" }, 1);
        ImGui.PopStyleVar(2);
        ImGui.PopID();
    }

    private static void DrawShaderGraphTextureSamplePreviewToolbar(ShaderNode node, Vector2 pos, Vector2 size, float y, float zoom)
    {
        var typeOptions = new[] { "Default", "Normal" };
        var colorSpaceOptions = new[] { "sRGB", "Linear" };
        var normalSpaceOptions = new[] { "Tangent", "Object", "World" };

        DrawShaderGraphTextureSampleToolbarRow(
            node,
            "Type",
            y,
            pos,
            size,
            zoom,
            node.TextureSettings.IsNormalMap ? 1 : 0,
            typeOptions,
            index =>
            {
                node.TextureSettings.IsNormalMap = index == 1;
                node.TextureSettings.SRgb = index == 0;
                if (node.TextureSettings.IsNormalMap && string.IsNullOrWhiteSpace(node.TextureSettings.NormalSpace))
                    node.TextureSettings.NormalSpace = "Tangent";
            });

        var spaceOptions = node.TextureSettings.IsNormalMap ? normalSpaceOptions : colorSpaceOptions;
        var spaceIndex = node.TextureSettings.IsNormalMap
            ? Array.FindIndex(spaceOptions, x => x.Equals(node.TextureSettings.NormalSpace, StringComparison.OrdinalIgnoreCase))
            : node.TextureSettings.SRgb ? 0 : 1;
        if (spaceIndex < 0) spaceIndex = 0;

        DrawShaderGraphTextureSampleToolbarRow(
            node,
            "Space",
            y + ShaderGraphNodePreviewToolbarHeight * zoom,
            pos,
            size,
            zoom,
            spaceIndex,
            spaceOptions,
            index =>
            {
                if (node.TextureSettings.IsNormalMap)
                    node.TextureSettings.NormalSpace = spaceOptions[Math.Clamp(index, 0, spaceOptions.Length - 1)];
                else
                    node.TextureSettings.SRgb = index == 0;
            });
    }

    private static void DrawShaderGraphTextureSampleToolbarRow(
        ShaderNode node,
        string label,
        float y,
        Vector2 pos,
        Vector2 size,
        float zoom,
        int currentIndex,
        string[] options,
        Action<int> onChanged)
    {
        var drawList = ImGui.GetWindowDrawList();
        var rowMin = new Vector2(pos.X, y);
        var rowMax = new Vector2(pos.X + size.X, y + ShaderGraphNodePreviewToolbarHeight * zoom);
        drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new Vec4(0.20f, 0.20f, 0.20f, 1f)));
        drawList.AddLine(rowMin, new Vector2(rowMax.X, rowMin.Y), ImGui.GetColorU32(new Vec4(0f, 0f, 0f, 0.35f)));

        ImGui.SetCursorScreenPos(new Vector2(pos.X + 10f * zoom, y + 6f * zoom));
        ImGui.TextUnformatted(label);

        ImGui.PushID(node.Id.GetHashCode());
        ImGui.PushID(label);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + size.X - 104f * zoom, y + 5f * zoom));
        ImGui.SetNextItemWidth(94f * zoom);
        var index = Math.Clamp(currentIndex, 0, options.Length - 1);
        if (ImGui.Combo("##texture_sample_option", ref index, options, options.Length))
            onChanged(index);
        ImGui.PopStyleVar(2);
        ImGui.PopID();
        ImGui.PopID();
    }

    private bool IsShaderGraphNodePreviewCollapsed(ShaderNode node)
        => shaderGraphCollapsedNodePreviews.Contains(node.Id);

    private void SetShaderGraphNodePreviewCollapsed(ShaderNode node, bool collapsed)
    {
        if (collapsed)
            shaderGraphCollapsedNodePreviews.Add(node.Id);
        else
            shaderGraphCollapsedNodePreviews.Remove(node.Id);
    }

    private void DrawShaderGraphNodePreview(ImDrawListPtr drawList, ShaderNode node, Vector2 pos, Vector2 size, float zoom, bool collapsed)
    {
        var min = pos;
        var max = pos + size;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vec4(0.10f, 0.10f, 0.10f, 1f)), 2f * zoom);

        if (!collapsed && TryDrawRealShaderGraphNodePreview(node, pos, size))
        {
            drawList.AddRect(min, max, ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.10f)), 2f * zoom);
            DrawShaderGraphNodePreviewToggle(node, min, size, zoom, collapsed);
            return;
        }

        if (!collapsed)
        {
            var clipMin = min;
            var clipMax = max;
            drawList.PushClipRect(clipMin, clipMax, true);

            switch (node.Kind)
            {
                case NodeKind.Voronoi:
                    DrawShaderGraphVoronoiPreview(drawList, min, size);
                    break;
                case NodeKind.Checkerboard:
                    DrawShaderGraphCheckerPreview(drawList, min, size);
                    break;
                case NodeKind.Noise:
                case NodeKind.GradientNoise:
                    DrawShaderGraphNoisePreview(drawList, min, size, node.Kind == NodeKind.GradientNoise);
                    break;
                case NodeKind.Add:
                case NodeKind.Multiply:
                    DrawShaderGraphMathPreviewFallback(drawList, min, size, node.Kind);
                    break;
            }

            drawList.PopClipRect();
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(new Vec4(1f, 1f, 1f, 0.10f)), 2f * zoom);
        DrawShaderGraphNodePreviewToggle(node, min, size, zoom, collapsed);
    }

    private void DrawShaderGraphNodePreviewToggle(ShaderNode node, Vector2 previewPos, Vector2 previewSize, float zoom, bool collapsed)
    {
        var buttonSize = new Vector2(22f, 20f) * zoom;
        var buttonPos = new Vector2(
            previewPos.X + (previewSize.X - buttonSize.X) * 0.5f,
            previewPos.Y + (previewSize.Y - buttonSize.Y) * 0.5f);

        ImGui.PushID(node.Id.GetHashCode());
        ImGui.SetCursorScreenPos(buttonPos);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        var label = collapsed ? "v##preview_toggle" : "^##preview_toggle";
        if (ImGui.Button(label, buttonSize))
            SetShaderGraphNodePreviewCollapsed(node, !collapsed);
        ImGui.PopStyleVar(2);
        ImGui.PopID();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(collapsed ? "Show preview" : "Hide preview");
    }

    private bool TryDrawRealShaderGraphNodePreview(ShaderNode node, Vector2 pos, Vector2 size)
    {
        var model = shaderGraphModel;
        if (model == null || node.Outputs.Count == 0)
            return false;

        var renderer = GetShaderGraphNodePreviewRenderer(node.Id);
        var fragment = new ShaderCodeGenerator().GenerateNodePreviewFragmentShader(model, node.Id, node.Outputs[0].Id);
        renderer.Resize(Math.Max(8, (int)size.X), Math.Max(8, (int)size.Y));
        if (!renderer.Render(model, fragment, (float)ImGui.GetTime(), ClientSize.X, ClientSize.Y))
            return false;

        ImGui.SetCursorScreenPos(pos);
        ImGui.Image(renderer.TextureId, size, new Vector2(0f, 1f), new Vector2(1f, 0f));
        return true;
    }

    private ShaderGraphNodePreviewRenderer GetShaderGraphNodePreviewRenderer(Guid nodeId)
    {
        if (!shaderGraphNodePreviews.TryGetValue(nodeId, out var renderer))
        {
            renderer = new ShaderGraphNodePreviewRenderer();
            shaderGraphNodePreviews[nodeId] = renderer;
        }
        return renderer;
    }

    private static void DrawShaderGraphVoronoiPreview(ImDrawListPtr drawList, Vector2 min, Vector2 size)
    {
        const int cols = 26;
        const int rows = 18;
        var seedPoints = new Vector2[36];
        for (int i = 0; i < seedPoints.Length; i++)
        {
            var h1 = Hash01(i * 17.13f + 2.7f);
            var h2 = Hash01(i * 31.71f + 8.3f);
            seedPoints[i] = new Vector2(h1, h2);
        }

        var cell = new Vector2(size.X / cols, size.Y / rows);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                var uv = new Vector2((x + 0.5f) / cols, (y + 0.5f) / rows);
                float d1 = 10f;
                float d2 = 10f;
                foreach (var p in seedPoints)
                {
                    var d = Vector2.DistanceSquared(uv, p);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                var edge = Math.Clamp((MathF.Sqrt(d2) - MathF.Sqrt(d1)) * 10f, 0f, 1f);
                var center = Math.Clamp(MathF.Sqrt(d1) * 7f, 0f, 1f);
                var v = Math.Clamp(edge * 0.9f + center * 0.15f, 0f, 1f);
                var col = ImGui.GetColorU32(new Vec4(v, v, v, 1f));
                var p0 = min + new Vector2(x * cell.X, y * cell.Y);
                drawList.AddRectFilled(p0, p0 + cell + new Vector2(1f, 1f), col);
            }
        }
    }

    private static void DrawShaderGraphCheckerPreview(ImDrawListPtr drawList, Vector2 min, Vector2 size)
    {
        const int cells = 8;
        var step = new Vector2(size.X / cells, size.Y / cells);
        for (int y = 0; y < cells; y++)
        {
            for (int x = 0; x < cells; x++)
            {
                var bright = ((x + y) & 1) == 0 ? 0.82f : 0.22f;
                var p0 = min + new Vector2(x * step.X, y * step.Y);
                drawList.AddRectFilled(p0, p0 + step + new Vector2(1f, 1f), ImGui.GetColorU32(new Vec4(bright, bright, bright, 1f)));
            }
        }
    }

    private static void DrawShaderGraphNoisePreview(ImDrawListPtr drawList, Vector2 min, Vector2 size, bool smooth)
    {
        const int cols = 28;
        const int rows = 18;
        var cell = new Vector2(size.X / cols, size.Y / rows);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                var n = smooth ? SmoothNoise((float)x / cols * 5f, (float)y / rows * 5f) : Hash01(x * 12.9898f + y * 78.233f);
                var p0 = min + new Vector2(x * cell.X, y * cell.Y);
                drawList.AddRectFilled(p0, p0 + cell + new Vector2(1f, 1f), ImGui.GetColorU32(new Vec4(n, n, n, 1f)));
            }
        }
    }

    private static void DrawShaderGraphMathPreviewFallback(ImDrawListPtr drawList, Vector2 min, Vector2 size, NodeKind kind)
    {
        const int cols = 18;
        const int rows = 18;
        var cell = new Vector2(size.X / cols, size.Y / rows);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                var u = (float)x / Math.Max(1, cols - 1);
                var v = (float)y / Math.Max(1, rows - 1);
                var value = kind == NodeKind.Multiply ? u * v : Math.Clamp((u + v) * 0.5f, 0f, 1f);
                var p0 = min + new Vector2(x * cell.X, y * cell.Y);
                drawList.AddRectFilled(p0, p0 + cell + new Vector2(1f, 1f), ImGui.GetColorU32(new Vec4(value, value, value, 1f)));
            }
        }
    }

    private static float SmoothNoise(float x, float y)
    {
        var ix = MathF.Floor(x);
        var iy = MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Hash01(ix * 12.9898f + iy * 78.233f);
        var b = Hash01((ix + 1f) * 12.9898f + iy * 78.233f);
        var c = Hash01(ix * 12.9898f + (iy + 1f) * 78.233f);
        var d = Hash01((ix + 1f) * 12.9898f + (iy + 1f) * 78.233f);
        return Lerp(Lerp(a, b, fx), Lerp(c, d, fx), fy);
    }

    private static float Hash01(float n)
    {
        var s = MathF.Sin(n) * 43758.5453f;
        return s - MathF.Floor(s);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private void DrawShaderGraphInspector(Vector2 size)
    {
        ImGui.BeginChild("##sg_inspector", size, ImGuiChildFlags.None);
        DrawShaderGraphInspectorContent();
        ImGui.EndChild();
    }

    private void DrawShaderGraphInspectorContent()
    {
        var model = shaderGraphModel!;

        ImGui.TextUnformatted("Graph Inspector");
        ImGui.Separator();

        if (ImGui.BeginTabBar("##sg_inspector_tabs"))
        {
            if (ImGui.BeginTabItem("Node Settings"))
            {
                DrawShaderGraphNodeSettingsTab(model);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Graph Settings"))
            {
                DrawShaderGraphGraphSettingsTab(model);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Validation"))
            {
                DrawShaderGraphValidationTab(model);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Profiler"))
            {
                DrawShaderGraphProfilerTab(model);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawShaderGraphNodeSettingsTab(ShaderGraphModel model)
    {
        if (shaderGraphSelectedPropertyId.HasValue)
        {
            var prop = model.Properties.FirstOrDefault(p => p.Id == shaderGraphSelectedPropertyId.Value);
            if (prop == null)
            {
                shaderGraphSelectedPropertyId = null;
            }
            else
            {
                DrawShaderGraphPropertyInspector(prop);
                return;
            }
        }

        if (shaderGraphSelectedGroupId.HasValue)
        {
            var group = model.Groups.FirstOrDefault(g => g.Id == shaderGraphSelectedGroupId.Value);
            if (group == null)
            {
                shaderGraphSelectedGroupId = null;
            }
            else
            {
                DrawShaderGraphGroupInspector(group);
                return;
            }
        }

        var node = shaderGraphSelectedNodeId.HasValue ? model.FindNode(shaderGraphSelectedNodeId.Value) : null;
        if (node == null)
        {
            ImGui.TextDisabled("Select a node to edit its properties.");
            return;
        }

        ImGui.TextUnformatted(node.Title);
        ImGui.TextDisabled(node.Kind.ToString());
        ImGui.Separator();

        DrawShaderGraphPinDefaults(model, node);

        switch (node.Kind)
        {
            case NodeKind.Rotator:
            {
                if (string.IsNullOrEmpty(node.TextValue)) node.TextValue = "Radians";
                DrawShaderGraphStringCombo("Unit", node.TextValue, ["Radians", "Degrees"], v => node.TextValue = v);
                break;
            }
            case NodeKind.ConstantFloat:
            case NodeKind.PropertyFloat:
            {
                var v = node.FloatValue;
                if (ImGui.DragFloat("Value", ref v, ShaderGraphValueDragSpeed)) node.FloatValue = v;
                break;
            }
            case NodeKind.ConstantVector2:
            case NodeKind.ConstantVector3:
            {
                var v = node.TextValue;
                if (ImGui.InputText("Vector", ref v, 64)) node.TextValue = v;
                break;
            }
            case NodeKind.ConstantColor:
            case NodeKind.PropertyColor:
            {
                var hex = node.ColorHex;
                if (ImGui.InputText("Color (hex)", ref hex, 16)) node.ColorHex = hex;
                var intensity = node.ColorIntensity;
                if (ImGui.DragFloat("Intensity", ref intensity, ShaderGraphValueDragSpeed, 0f, 10f)) node.ColorIntensity = intensity;
                break;
            }
            case NodeKind.PropertyVector2:
            case NodeKind.PropertyVector3:
            case NodeKind.PropertyVector4:
            {
                var v = node.TextValue;
                if (ImGui.InputText("Name", ref v, 64)) node.TextValue = v;
                break;
            }
            case NodeKind.PropertyTexture2D:
            {
                var v = node.TextValue;
                if (ImGui.InputText("Name", ref v, 64)) node.TextValue = v;
                DrawAssetSlot("Texture", node.TexturePath, "Drop texture", path =>
                {
                    if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                        node.TexturePath = path;
                }, MaterialAsset.IsTexturePath);
                break;
            }
            case NodeKind.TextureSample:
            case NodeKind.NormalMap:
            {
                DrawAssetSlot("Texture", node.TexturePath, "Drop texture", path =>
                {
                    if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                        node.TexturePath = path;
                }, MaterialAsset.IsTexturePath);
                break;
            }
            case NodeKind.Swizzle:
            case NodeKind.ChannelMask:
            {
                var v = node.TextValue;
                if (ImGui.InputText("Pattern", ref v, 8)) node.TextValue = v;
                break;
            }
            case NodeKind.Gradient:
            {
                var axis = string.IsNullOrWhiteSpace(node.TextValue) ? "Vertical" : node.TextValue;
                DrawShaderGraphStringCombo("Axis", axis, ["Vertical", "Horizontal", "Custom"], v =>
                {
                    node.TextValue = v;
                    var t = node.Input("T");
                    if (t != null && model.FindConnectionToInput(t.Id) == null)
                    {
                        if (v == "Vertical") t.DefaultValue = "v_UV.y";
                        else if (v == "Horizontal") t.DefaultValue = "v_UV.x";
                    }
                });
                break;
            }
            case NodeKind.Blend:
            {
                var mode = string.IsNullOrWhiteSpace(node.TextValue) ? "Add" : node.TextValue;
                DrawShaderGraphStringCombo("Mode", mode, ["Add", "Multiply", "Screen", "Overlay", "Alpha"], v => node.TextValue = v);
                break;
            }
            case NodeKind.SubGraph:
            {
                var v = string.IsNullOrWhiteSpace(node.TextValue) ? "SubGraphName" : node.TextValue;
                if (ImGui.InputText("Sub Graph", ref v, 128)) node.TextValue = v.Trim();

                if (model.SubGraphs.Count > 0)
                {
                    var names = model.SubGraphs.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
                    var index = Array.FindIndex(names, n => n.Equals(node.TextValue, StringComparison.OrdinalIgnoreCase));
                    if (index < 0) index = 0;
                    if (names.Length > 0 && ImGui.Combo("Available", ref index, names, names.Length))
                        node.TextValue = names[index];
                }
                break;
            }
            default:
                if (node.Inputs.Concat(node.Outputs).Any(p => p.Type == PinType.Float))
                {
                    var v1 = node.FloatValue;
                    if (ImGui.DragFloat("Value", ref v1, ShaderGraphValueDragSpeed)) node.FloatValue = v1;
                    var v2 = node.FloatValue2;
                    if (ImGui.DragFloat("Value 2", ref v2, ShaderGraphValueDragSpeed)) node.FloatValue2 = v2;
                }
                break;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Comment:");
        var comment = node.Comment;
        if (ImGui.InputTextMultiline("##sg_comment", ref comment, 256, new Vector2(-1, 60f))) node.Comment = comment;

        ImGui.Separator();
        string deleteLabel = shaderGraphSelectedNodeIds.Count > 1
            ? $"Delete {shaderGraphSelectedNodeIds.Count} Nodes"
            : "Delete Node";
        if (ImGui.Button(deleteLabel, new Vector2(-1, 0)))
            DeleteSelectedShaderGraphNodes(model);
    }

    private static readonly (string Label, PinType Type, bool IsColor)[] ShaderGraphPropertyTypes =
    {
        ("Float", PinType.Float, false),
        ("Vector2", PinType.Vec2, false),
        ("Vector3", PinType.Vec3, false),
        ("Vector4", PinType.Vec4, false),
        ("Color", PinType.Vec4, true),
        ("Texture2D", PinType.Texture2D, false),
    };

    private void DrawShaderGraphBlackboard(Vector2 size)
    {
        var model = shaderGraphModel!;
        ImGui.BeginChild("##sg_blackboard", size, ImGuiChildFlags.None);

        ImGui.TextUnformatted(model.Name);
        ImGui.SameLine(size.X - 24f);
        if (ImGui.SmallButton("+"))
            ImGui.OpenPopup("##sg_add_property");

        if (ImGui.BeginPopup("##sg_add_property"))
        {
            foreach (var entry in ShaderGraphPropertyTypes)
            {
                if (ImGui.MenuItem(entry.Label))
                {
                    PushShaderGraphUndoSnapshot();
                    string baseName = SanitizeShaderGraphPropertyName("New" + entry.Label.Replace(" ", string.Empty));
                    string name = MakeUniqueShaderGraphPropertyName(model, baseName);

                    var prop = new GraphProperty
                    {
                        Name = name,
                        DisplayName = entry.Label,
                        Type = entry.Type
                    };
                    if (entry.IsColor)
                        prop.ColorHex = "#FFFFFFFF";
                    model.Properties.Add(prop);
                    shaderGraphSelectedPropertyId = prop.Id;
                    ClearShaderGraphNodeSelection();
                    shaderGraphSelectedGroupId = null;
                }
            }
            ImGui.EndPopup();
        }

        ImGui.Separator();

        if (model.Properties.Count == 0)
            ImGui.TextDisabled("No properties.\nClick + to add one.");

        foreach (var prop in model.Properties.ToList())
        {
            ImGui.PushID(prop.Id.GetHashCode());
            bool selected = shaderGraphSelectedPropertyId == prop.Id;

            var dot = GetPinTypeColor(prop.Type);
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddCircleFilled(cursor + new Vector2(6f, 8f), 4f, ImGui.GetColorU32(dot));
            ImGui.Indent(14f);
            var label = string.IsNullOrWhiteSpace(prop.DisplayName) ? prop.Name : prop.DisplayName;
            if (ImGui.Selectable(label, selected))
            {
                shaderGraphSelectedPropertyId = prop.Id;
                ClearShaderGraphNodeSelection();
                shaderGraphSelectedGroupId = null;
            }

            if (ImGui.BeginDragDropSource())
            {
                ImGui.SetDragDropPayload("GROKO_SHADERGRAPH_PROPERTY", IntPtr.Zero, 0);
                shaderGraphDraggingPropertyId = prop.Id;
                ImGui.TextUnformatted(label);
                ImGui.EndDragDropSource();
            }

            ImGui.SameLine();
            ImGui.TextDisabled(prop.UniformName);
            ImGui.Unindent(14f);

            if (ImGui.BeginPopupContextItem("##sg_prop_ctx"))
            {
                if (ImGui.MenuItem("Delete Property"))
                {
                    PushShaderGraphUndoSnapshot();
                    model.Properties.Remove(prop);
                    if (shaderGraphSelectedPropertyId == prop.Id)
                        shaderGraphSelectedPropertyId = null;
                }
                ImGui.EndPopup();
            }
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private static string SanitizeShaderGraphPropertyName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Property" : value.Trim();
        var chars = value.Select((c, i) => char.IsLetter(c) || c == '_' || (i > 0 && char.IsDigit(c)) ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "Property" : result;
    }

    private static string MakeUniqueShaderGraphPropertyName(ShaderGraphModel model, string baseName, Guid? currentPropertyId = null)
    {
        baseName = SanitizeShaderGraphPropertyName(baseName);
        var name = baseName;
        var suffix = 1;
        while (model.Properties.Any(p =>
                   (!currentPropertyId.HasValue || p.Id != currentPropertyId.Value) &&
                   string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
            name = baseName + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }

    private static string MakeUniqueShaderGraphTextureUniformName(ShaderGraphModel model, string assetPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(assetPath);
        var baseName = SanitizeShaderGraphPropertyName(string.IsNullOrWhiteSpace(fileName) ? "Texture" : fileName);
        if (!baseName.StartsWith("u_", StringComparison.OrdinalIgnoreCase))
            baseName = "u_" + baseName;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in model.Properties ?? [])
            used.Add(SanitizeShaderGraphPropertyName(prop.UniformName));
        foreach (var node in model.Nodes ?? [])
        {
            if (node.Kind is NodeKind.TextureSample or NodeKind.NormalMap or NodeKind.Triplanar
                or NodeKind.MetallicMap or NodeKind.SmoothnessMap or NodeKind.AmbientOcclusionMap
                or NodeKind.PropertyTexture2D)
            {
                var value = string.IsNullOrWhiteSpace(node.TextValue) ? "u_MainTex" : node.TextValue.Trim();
                used.Add(SanitizeShaderGraphPropertyName(value));
            }
        }

        var name = baseName;
        var suffix = 1;
        while (used.Contains(name))
        {
            suffix++;
            name = baseName + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }

    private void AddShaderGraphGroupAroundSelection()
    {
        var model = shaderGraphModel!;
        PushShaderGraphUndoSnapshot();

        var selected = shaderGraphSelectedNodeId.HasValue ? model.FindNode(shaderGraphSelectedNodeId.Value) : null;
        var x = selected?.X - 40 ?? (-shaderGraphPan.X / Math.Max(shaderGraphZoom, 0.001f) + 120);
        var y = selected?.Y - 45 ?? (-shaderGraphPan.Y / Math.Max(shaderGraphZoom, 0.001f) + 90);
        var group = new GraphGroup
        {
            Title = "Group",
            X = x,
            Y = y,
            Width = selected is null ? 420 : 300,
            Height = selected is null ? 260 : 190,
            ColorHex = "#552563EB"
        };
        model.Groups.Add(group);
        shaderGraphSelectedGroupId = group.Id;
        ClearShaderGraphNodeSelection();
        shaderGraphSelectedPropertyId = null;
    }

    private void AddShaderGraphNoteAtViewCenter()
    {
        var model = shaderGraphModel!;
        PushShaderGraphUndoSnapshot();
        var world = (-shaderGraphPan + new Vector2(260f, 180f)) / Math.Max(shaderGraphZoom, 0.001f);
        var group = new GraphGroup
        {
            Title = "Note",
            Comment = "New note",
            X = world.X,
            Y = world.Y,
            Width = 260,
            Height = 120,
            ColorHex = "#6657572A"
        };
        model.Groups.Add(group);
        shaderGraphSelectedGroupId = group.Id;
        ClearShaderGraphNodeSelection();
        shaderGraphSelectedPropertyId = null;
    }

    private static readonly string[] ShaderGraphSurfaceOptions = { "Opaque", "Transparent" };
    private static readonly string[] ShaderGraphBlendModeOptions = { "Alpha", "Additive", "Multiply", "Premultiply" };
    private static readonly string[] ShaderGraphCullModeOptions = { "Back", "Front", "None" };
    private static readonly string[] ShaderGraphZTestOptions = { "Less", "LEqual", "Equal", "Greater", "GEqual", "Always" };

    private void DrawShaderGraphGraphSettingsTab(ShaderGraphModel model)
    {
        ImGui.TextUnformatted("Target Settings");
        ImGui.Separator();

        DrawShaderGraphStringCombo("Surface", model.Surface, ShaderGraphSurfaceOptions, v => model.Surface = v);
        DrawShaderGraphStringCombo("Blend Mode", model.BlendMode, ShaderGraphBlendModeOptions, v => model.BlendMode = v);
        DrawShaderGraphStringCombo("Cull Mode", model.CullMode, ShaderGraphCullModeOptions, v => model.CullMode = v);
        DrawShaderGraphStringCombo("Z Test", model.ZTest, ShaderGraphZTestOptions, v => model.ZTest = v);

        FieldRow("Depth Write");
        bool depthWrite = model.DepthWrite;
        if (ImGui.Checkbox("##sg_depth_write", ref depthWrite)) model.DepthWrite = depthWrite;

        FieldRow("Depth Test");
        bool depthTest = model.DepthTest;
        if (ImGui.Checkbox("##sg_depth_test", ref depthTest)) model.DepthTest = depthTest;

        FieldRow("Two Sided");
        bool doubleSided = model.DoubleSided;
        if (ImGui.Checkbox("##sg_double_sided", ref doubleSided)) model.DoubleSided = doubleSided;

        var renderQueue = model.RenderQueue;
        if (ImGui.DragInt("Render Queue", ref renderQueue, 1f, 0, 5000)) model.RenderQueue = renderQueue;

        ImGui.Separator();
        ImGui.TextUnformatted("Sub Graphs");

        if (ImGui.Button("Add Empty SubGraph"))
        {
            PushShaderGraphUndoSnapshot();
            model.SubGraphs.Add(new GrokoShaderGraphPro.Models.SubGraphAsset
            {
                Name = MakeUniqueSubGraphName(model, "SubGraph")
            });
        }

        if (shaderGraphSelectedGroupId.HasValue)
        {
            ImGui.SameLine();
            if (ImGui.Button("Create From Selected Group"))
            {
                var group = model.Groups.FirstOrDefault(g => g.Id == shaderGraphSelectedGroupId.Value);
                if (group != null)
                {
                    PushShaderGraphUndoSnapshot();
                    CreateSubGraphFromGroup(model, group);
                }
            }
        }

        if (shaderGraphSelectedNodeId.HasValue)
        {
            ImGui.SameLine();
            if (ImGui.Button("Create From Selected Node"))
            {
                var selected = model.FindNode(shaderGraphSelectedNodeId.Value);
                if (selected != null && selected.Kind != NodeKind.Output)
                {
                    PushShaderGraphUndoSnapshot();
                    model.SubGraphs.Add(new GrokoShaderGraphPro.Models.SubGraphAsset
                    {
                        Name = MakeUniqueSubGraphName(model, selected.Title.Replace(" ", string.Empty)),
                        Description = selected.Title,
                        Nodes = [CloneShaderGraphNodeForSubGraph(selected)]
                    });
                }
            }
        }

        foreach (var subGraph in model.SubGraphs.ToList())
        {
            ImGui.PushID(subGraph.Id.GetHashCode());
            var name = subGraph.Name;
            ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - 36f));
            if (ImGui.InputText("##subgraph_name", ref name, 80))
                subGraph.Name = MakeUniqueSubGraphName(model, SanitizeShaderGraphPropertyName(name), subGraph.Id);
            ImGui.SameLine();
            if (ImGui.SmallButton("x"))
            {
                PushShaderGraphUndoSnapshot();
                model.SubGraphs.Remove(subGraph);
            }
            ImGui.PopID();
        }
    }

    private void DrawShaderGraphGroupInspector(GraphGroup group)
    {
        ImGui.TextUnformatted("Group / Note");
        ImGui.TextDisabled("Visual organization only; it does not affect shader code.");
        ImGui.Separator();

        var title = group.Title;
        if (ImGui.InputText("Title", ref title, 96))
            group.Title = title;

        var comment = group.Comment;
        if (ImGui.InputTextMultiline("Comment", ref comment, 512, new Vector2(-1f, 90f)))
            group.Comment = comment;

        var x = (float)group.X;
        var y = (float)group.Y;
        var pos = new Vector2(x, y);
        if (ImGui.DragFloat2("Position", ref pos, 1f))
        {
            group.X = pos.X;
            group.Y = pos.Y;
        }

        var size = new Vector2((float)group.Width, (float)group.Height);
        if (ImGui.DragFloat2("Size", ref size, 1f, 80f, 2000f))
        {
            group.Width = Math.Max(80, size.X);
            group.Height = Math.Max(60, size.Y);
        }

        var color = HexArgbToVec4(group.ColorHex);
        if (ColorField4("Color", ref color))
            group.ColorHex = Vec4ToHexArgb(color);

        ImGui.Separator();
        if (ImGui.Button("Delete Group", new Vector2(-1f, 0f)))
        {
            PushShaderGraphUndoSnapshot();
            shaderGraphModel!.Groups.Remove(group);
            shaderGraphSelectedGroupId = null;
        }
    }

    private void DrawShaderGraphValidationTab(ShaderGraphModel model)
    {
        var issues = ShaderGraphValidator.Validate(model)
            .Where(i => i.Severity != GrokoShaderGraphPro.Services.ValidationSeverity.Info)
            .OrderByDescending(i => i.Severity)
            .ToList();

        if (issues.Count == 0)
        {
            ImGui.TextDisabled("No warnings or errors.");
            return;
        }

        foreach (var issue in issues)
        {
            ImGui.PushID(issue.GetHashCode());
            var color = issue.Severity == GrokoShaderGraphPro.Services.ValidationSeverity.Error
                ? new Vec4(1f, 0.32f, 0.25f, 1f)
                : new Vec4(1f, 0.76f, 0.22f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            var label = string.IsNullOrWhiteSpace(issue.NodeTitle)
                ? issue.Message
                : $"{issue.NodeTitle}: {issue.Message}";
            if (ImGui.Selectable(label, false, ImGuiSelectableFlags.AllowDoubleClick))
            {
                if (issue.NodeId.HasValue)
                {
                    SelectOnlyShaderGraphNode(issue.NodeId.Value);
                    shaderGraphFrameSelectedPending = true;
                }
            }
            ImGui.PopStyleColor();
            if (!string.IsNullOrWhiteSpace(issue.Code))
                ImGui.TextDisabled(issue.Code);
            ImGui.PopID();
        }
    }

    private static void DrawShaderGraphProfilerTab(ShaderGraphModel model)
    {
        var report = GraphProfiler.Analyze(model);

        ImGui.TextUnformatted("Shader Cost");
        ImGui.TextDisabled(report.PerformanceTier);
        DrawShaderGraphProfilerBar("Estimated", report.EstimatedInstructionCost, 280);
        DrawShaderGraphProfilerBar("Textures", report.TextureCount, 8);
        DrawShaderGraphProfilerBar("Procedural", report.ProceduralCount, 10);
        DrawShaderGraphProfilerBar("Math", report.MathCount, 40);

        ImGui.Separator();
        ImGui.Text($"Nodes: {report.NodeCount}");
        ImGui.Text($"Connections: {report.ConnectionCount}");
        ImGui.Text($"Outputs: {report.OutputCount}");

        ImGui.Separator();
        ImGui.TextUnformatted("Heavy Nodes");
        if (report.HeavyNodes.Count == 0)
        {
            ImGui.TextDisabled("None.");
        }
        else
        {
            foreach (var heavy in report.HeavyNodes.Take(12))
                ImGui.BulletText(heavy);
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Warnings");
        if (report.Warnings.Count == 0)
        {
            ImGui.TextDisabled("No performance warnings.");
        }
        else
        {
            foreach (var warning in report.Warnings)
                ImGui.BulletText(warning);
        }
    }

    private static void DrawShaderGraphProfilerBar(string label, int value, int max)
    {
        var t = Math.Clamp(value / (float)Math.Max(1, max), 0f, 1f);
        var color = t < 0.45f
            ? new Vec4(0.24f, 0.78f, 0.38f, 1f)
            : t < 0.75f
                ? new Vec4(1f, 0.72f, 0.20f, 1f)
                : new Vec4(1f, 0.28f, 0.20f, 1f);

        ImGui.TextUnformatted($"{label}: {value}");
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(80f, ImGui.GetContentRegionAvail().X), 8f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vec4(0.18f, 0.18f, 0.18f, 1f)), 3f);
        dl.AddRectFilled(pos, pos + new Vector2(size.X * t, size.Y), ImGui.GetColorU32(color), 3f);
        ImGui.Dummy(size + new Vector2(0f, 4f));
    }

    private static string MakeUniqueSubGraphName(ShaderGraphModel model, string baseName, Guid? currentId = null)
    {
        baseName = SanitizeShaderGraphPropertyName(baseName);
        var name = baseName;
        var suffix = 1;
        while (model.SubGraphs.Any(s =>
                   (!currentId.HasValue || s.Id != currentId.Value) &&
                   string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
            name = baseName + suffix.ToString(CultureInfo.InvariantCulture);
        }
        return name;
    }

    private static void CreateSubGraphFromGroup(ShaderGraphModel model, GraphGroup group)
    {
        var contained = model.Nodes
            .Where(n => n.X >= group.X && n.Y >= group.Y && n.X <= group.X + group.Width && n.Y <= group.Y + group.Height)
            .Where(n => n.Kind != NodeKind.Output)
            .ToList();

        if (contained.Count == 0)
            return;

        var map = new Dictionary<Guid, Guid>();
        var clones = new List<ShaderNode>();
        foreach (var node in contained)
        {
            var clone = CloneShaderGraphNodeForSubGraph(node);
            clone.X -= group.X;
            clone.Y -= group.Y;
            map[node.Id] = clone.Id;
            clones.Add(clone);
        }

        var oldPinToNewPin = contained
            .Zip(clones)
            .SelectMany(pair => pair.First.Inputs.Concat(pair.First.Outputs).Zip(pair.Second.Inputs.Concat(pair.Second.Outputs)))
            .ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);

        var connections = model.Connections
            .Where(c => oldPinToNewPin.ContainsKey(c.FromPinId) && oldPinToNewPin.ContainsKey(c.ToPinId))
            .Select(c => new GraphConnection
            {
                FromPinId = oldPinToNewPin[c.FromPinId],
                ToPinId = oldPinToNewPin[c.ToPinId]
            })
            .ToList();

        model.SubGraphs.Add(new GrokoShaderGraphPro.Models.SubGraphAsset
        {
            Name = MakeUniqueSubGraphName(model, string.IsNullOrWhiteSpace(group.Title) ? "SubGraph" : group.Title.Replace(" ", string.Empty)),
            Description = group.Comment,
            Nodes = clones,
            Connections = connections
        });
    }

    private static ShaderNode CloneShaderGraphNodeForSubGraph(ShaderNode source)
    {
        var clone = JsonSerializer.Deserialize<ShaderNode>(JsonSerializer.Serialize(source, new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        }), new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        }) ?? new ShaderNode();
        clone.Id = Guid.NewGuid();
        clone.X = 0;
        clone.Y = 0;
        foreach (var pin in clone.Inputs.Concat(clone.Outputs))
        {
            pin.Id = Guid.NewGuid();
            pin.NodeId = clone.Id;
        }
        return clone;
    }

    /// <summary>Tries to read a pin's GLSL default ("0.5", "vec2(0.5, 0.5)", ...) as raw numeric components.</summary>
    private static bool TryParsePinDefault(GraphPin pin, out float[] values)
    {
        values = Array.Empty<float>();
        var s = (pin.DefaultValue ?? string.Empty).Trim();
        var expected = pin.Type switch
        {
            PinType.Float => 1,
            PinType.Vec2 => 2,
            PinType.Vec3 => 3,
            PinType.Vec4 => 4,
            _ => 0
        };
        if (expected == 0) return false;

        if (expected == 1)
        {
            if (!float.TryParse(s.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return false;
            values = new[] { f };
            return true;
        }

        var prefix = $"vec{expected}(";
        if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !s.EndsWith(")"))
            return false;

        var parts = s.Substring(prefix.Length, s.Length - prefix.Length - 1).Split(',');
        if (parts.Length != expected) return false;

        var arr = new float[expected];
        for (int i = 0; i < expected; i++)
        {
            if (!float.TryParse(parts[i].Trim().TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out arr[i]))
                return false;
        }
        values = arr;
        return true;
    }

    private static string FormatPinDefault(PinType type, float[] values)
    {
        var inv = CultureInfo.InvariantCulture;
        return type switch
        {
            PinType.Float => values[0].ToString(inv),
            PinType.Vec2 => $"vec2({values[0].ToString(inv)}, {values[1].ToString(inv)})",
            PinType.Vec3 => $"vec3({values[0].ToString(inv)}, {values[1].ToString(inv)}, {values[2].ToString(inv)})",
            PinType.Vec4 => $"vec4({values[0].ToString(inv)}, {values[1].ToString(inv)}, {values[2].ToString(inv)}, {values[3].ToString(inv)})",
            _ => string.Empty
        };
    }

    private static int GetShaderGraphPinComponentCount(PinType type) => type switch
    {
        PinType.Vec2 => 2,
        PinType.Vec3 => 3,
        PinType.Vec4 => 4,
        _ => 1
    };

    private static bool ShouldDrawShaderGraphInlinePinDefault(GraphPin pin)
    {
        return TryParsePinDefault(pin, out _) || TryGetShaderGraphInlinePinToken(pin, out _);
    }

    private static bool IsShaderGraphColorPin(GraphPin pin)
    {
        if (pin.Type is not (PinType.Vec3 or PinType.Vec4))
            return false;

        return pin.Name.Contains("Color", StringComparison.OrdinalIgnoreCase)
            || pin.Name.Contains("Emission", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetShaderGraphInlinePinToken(GraphPin pin, out string token)
    {
        token = string.Empty;
        if (pin.Type is not (PinType.Float or PinType.Vec2 or PinType.Vec3 or PinType.Vec4))
            return false;

        var value = (pin.DefaultValue ?? string.Empty).Trim();
        if (value.Length == 0)
            return false;

        if (string.Equals(value, "v_UV", StringComparison.OrdinalIgnoreCase))
        {
            token = "UV0";
            return true;
        }

        if (string.Equals(value, "v_UV.x", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "v_UV.y", StringComparison.OrdinalIgnoreCase))
        {
            token = value;
            return true;
        }

        if (value.Length <= 18 && value.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            token = value;
            return true;
        }

        return false;
    }

    private static float GetShaderGraphInlinePinDefaultWidth(GraphPin pin)
    {
        if (!TryParsePinDefault(pin, out _))
            return 52f;

        if (IsShaderGraphColorPin(pin))
            return 82f;

        var components = GetShaderGraphPinComponentCount(pin.Type);
        return components * 48f + MathF.Max(0, components - 1) * 3f;
    }

    /// <summary>Draws the inline Drag1/2/3/4 widget for one unconnected pin directly on the
    /// node body in the canvas, mirroring Unity Shader Graph's "Center(2) X 0.5 Y 0.5" style.</summary>
    private static void DrawShaderGraphInlinePinDefault(GraphPin pin, Vector2 screenPos, float width)
    {
        ImGui.PushID(pin.Id.GetHashCode());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vec4(0.86f, 0.86f, 0.86f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vec4(0.25f, 0.25f, 0.25f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vec4(0.33f, 0.33f, 0.33f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vec4(0.40f, 0.40f, 0.40f, 1f));

        if (TryParsePinDefault(pin, out var values) && IsShaderGraphColorPin(pin))
        {
            var color = new Vec4(
                values.Length > 0 ? values[0] : 1f,
                values.Length > 1 ? values[1] : 1f,
                values.Length > 2 ? values[2] : 1f,
                values.Length > 3 ? values[3] : 1f);

            ImGui.SetCursorScreenPos(screenPos);
            if (ImGui.ColorButton("##swatch", color, ImGuiColorEditFlags.NoTooltip, new Vector2(width, 18f)))
                ImGui.OpenPopup("##color_pick");

            if (ImGui.BeginPopup("##color_pick"))
            {
                var rgb = new System.Numerics.Vector3(color.X, color.Y, color.Z);
                if (ImGui.ColorPicker3("##picker", ref rgb,
                    ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs))
                {
                    if (pin.Type == PinType.Vec4)
                        pin.DefaultValue = FormatPinDefault(pin.Type, new[] { rgb.X, rgb.Y, rgb.Z, color.W });
                    else
                        pin.DefaultValue = FormatPinDefault(pin.Type, new[] { rgb.X, rgb.Y, rgb.Z });
                }
                ImGui.EndPopup();
            }
        }
        else if (TryParsePinDefault(pin, out values))
        {
            var labels = new[] { "X", "Y", "Z", "W" };
            var components = Math.Min(values.Length, GetShaderGraphPinComponentCount(pin.Type));
            var fieldWidth = MathF.Max(34f, (width - MathF.Max(0, components - 1) * 3f) / MathF.Max(1, components));
            var changed = false;

            ImGui.SetCursorScreenPos(screenPos);
            for (int i = 0; i < components; i++)
            {
                if (i > 0)
                    ImGui.SameLine();

                ImGui.BeginGroup();
                ImGui.TextUnformatted(labels[i]);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(MathF.Max(20f, fieldWidth - 15f));
                var v = values[i];
                if (ImGui.DragFloat("##v" + i, ref v, ShaderGraphValueDragSpeed, 0f, 0f, "%.3g"))
                {
                    values[i] = v;
                    changed = true;
                }
                ImGui.EndGroup();
            }

            if (changed)
                pin.DefaultValue = FormatPinDefault(pin.Type, values);
        }
        else if (TryGetShaderGraphInlinePinToken(pin, out var token))
        {
            ImGui.SetCursorScreenPos(screenPos);
            ImGui.SetNextItemWidth(width);
            ImGui.BeginDisabled();
            ImGui.InputText("##token", ref token, 32, ImGuiInputTextFlags.ReadOnly);
            ImGui.EndDisabled();
        }

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(3);
        ImGui.PopID();
    }

    /// <summary>Draws an inline editor (DragFloat/2/3/4) for every unconnected input pin whose
    /// default value is a plain numeric literal, so the user can tweak constants like in Unity's
    /// Shader Graph without wiring a Constant node.</summary>
    private void DrawShaderGraphPinDefaults(ShaderGraphModel model, ShaderNode node)
    {
        bool any = false;
        foreach (var pin in node.Inputs)
        {
            if (model.FindConnectionToInput(pin.Id) != null) continue;
            if (!TryParsePinDefault(pin, out var values)) continue;

            any = true;
            switch (pin.Type)
            {
                case PinType.Float:
                {
                    var v = values[0];
                    if (ImGui.DragFloat(pin.Name, ref v, ShaderGraphValueDragSpeed))
                        pin.DefaultValue = FormatPinDefault(pin.Type, new[] { v });
                    break;
                }
                case PinType.Vec2:
                {
                    var v = new Vector2(values[0], values[1]);
                    if (ImGui.DragFloat2(pin.Name, ref v, ShaderGraphValueDragSpeed))
                        pin.DefaultValue = FormatPinDefault(pin.Type, new[] { v.X, v.Y });
                    break;
                }
                case PinType.Vec3:
                {
                    var v = new System.Numerics.Vector3(values[0], values[1], values[2]);
                    if (ImGui.DragFloat3(pin.Name, ref v, ShaderGraphValueDragSpeed))
                        pin.DefaultValue = FormatPinDefault(pin.Type, new[] { v.X, v.Y, v.Z });
                    break;
                }
                case PinType.Vec4:
                {
                    var v = new Vec4(values[0], values[1], values[2], values[3]);
                    if (ImGui.DragFloat4(pin.Name, ref v, ShaderGraphValueDragSpeed))
                        pin.DefaultValue = FormatPinDefault(pin.Type, new[] { v.X, v.Y, v.Z, v.W });
                    break;
                }
            }
        }

        if (any)
            ImGui.Separator();
    }

    private static void DrawShaderGraphStringCombo(string label, string value, string[] options, Action<string> set)
    {
        FieldRow(label);
        int index = Array.IndexOf(options, value);
        if (index < 0) index = 0;
        if (ImGui.Combo("##" + label, ref index, options, options.Length))
            set(options[index]);
    }

    private void DrawShaderGraphPropertyInspector(GraphProperty prop)
    {
        var model = shaderGraphModel!;
        ImGui.TextUnformatted($"Property: {prop.Type}");
        ImGui.TextDisabled(prop.UniformName);
        ImGui.Separator();

        DrawString("Name", prop.DisplayName, v => prop.DisplayName = v);

        var reference = prop.Name;
        if (ImGui.InputText("##sg_prop_reference_label", ref reference, 64))
            prop.Name = MakeUniqueShaderGraphPropertyName(model, SanitizeShaderGraphPropertyName(reference), prop.Id);
        ImGui.SameLine();
        ImGui.TextDisabled("Reference");

        DrawEnumCombo("Precision", prop.Precision, v => prop.Precision = v);
        DrawEnumCombo("Scope", prop.Scope, v => prop.Scope = v);

        FieldRow("Show In Inspector");
        bool exposed = prop.Exposed;
        if (ImGui.Checkbox("##sg_prop_exposed", ref exposed)) prop.Exposed = exposed;

        FieldRow("Read Only");
        bool readOnly = prop.ReadOnly;
        if (ImGui.Checkbox("##sg_prop_readonly", ref readOnly)) prop.ReadOnly = readOnly;

        if (prop.Type is PinType.Vec3 or PinType.Vec4)
            DrawEnumCombo("Mode", prop.ColorMode, v => prop.ColorMode = v);

        ImGui.Separator();
        ImGui.TextUnformatted("Default Value:");

        switch (prop.Type)
        {
            case PinType.Float:
            {
                var v = float.TryParse(prop.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
                if (ImGui.DragFloat("##sg_prop_default_float", ref v, ShaderGraphValueDragSpeed))
                    prop.DefaultValue = v.ToString(CultureInfo.InvariantCulture);
                break;
            }
            case PinType.Vec2:
            {
                var v = string.IsNullOrWhiteSpace(prop.DefaultValue) ? "0,0" : prop.DefaultValue;
                if (ImGui.InputText("##sg_prop_default_vec2", ref v, 64))
                    prop.DefaultValue = v;
                break;
            }
            case PinType.Vec3:
            case PinType.Vec4:
            {
                var color = HexArgbToVec4(prop.ColorHex);
                if (prop.ColorMode == PropertyColorMode.Hdr)
                {
                    var rgb = new System.Numerics.Vector3(color.X, color.Y, color.Z);
                    var intensity = prop.ColorIntensity;
                    if (HdrColorField("##sg_prop_default_color", ref rgb, ref intensity))
                    {
                        prop.ColorHex = Vec4ToHexArgb(new Vec4(rgb.X, rgb.Y, rgb.Z, color.W));
                        prop.ColorIntensity = intensity;
                    }
                }
                else if (prop.Type == PinType.Vec3)
                {
                    var rgb = new System.Numerics.Vector3(color.X, color.Y, color.Z);
                    if (ColorField("##sg_prop_default_color", ref rgb))
                        prop.ColorHex = Vec4ToHexArgb(new Vec4(rgb.X, rgb.Y, rgb.Z, color.W));
                }
                else
                {
                    if (ColorField4("##sg_prop_default_color", ref color))
                        prop.ColorHex = Vec4ToHexArgb(color);
                }
                break;
            }
            case PinType.Texture2D:
            {
                DrawAssetSlot("Texture", prop.TexturePath, "Drop texture", path =>
                {
                    if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                        prop.TexturePath = path;
                }, MaterialAsset.IsTexturePath);
                break;
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Custom Attributes");
        for (int i = 0; i < prop.CustomAttributes.Count; i++)
        {
            ImGui.PushID(i);
            var attr = prop.CustomAttributes[i];
            var aw = (ImGui.GetContentRegionAvail().X - 24f) * 0.5f;

            ImGui.SetNextItemWidth(aw);
            var name = attr.Name;
            if (ImGui.InputText("##name", ref name, 64)) attr.Name = name;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(aw);
            var value = attr.Value;
            if (ImGui.InputText("##value", ref value, 64)) attr.Value = value;

            ImGui.SameLine();
            if (ImGui.SmallButton("x"))
            {
                prop.CustomAttributes.RemoveAt(i);
                ImGui.PopID();
                i--;
                continue;
            }
            ImGui.PopID();
        }

        if (ImGui.Button("+", new Vector2(24f, 0f)))
            prop.CustomAttributes.Add(new PropertyAttribute());
        ImGui.SameLine();
        if (ImGui.Button("-", new Vector2(24f, 0f)) && prop.CustomAttributes.Count > 0)
            prop.CustomAttributes.RemoveAt(prop.CustomAttributes.Count - 1);

        ImGui.Separator();
        if (ImGui.Button("Delete Property", new Vector2(-1, 0)))
        {
            PushShaderGraphUndoSnapshot();
            shaderGraphModel!.Properties.Remove(prop);
            shaderGraphSelectedPropertyId = null;
        }
    }

    private static Vec4 HexArgbToVec4(string hex)
    {
        hex = (hex ?? string.Empty).TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        if (hex.Length != 8) return Vec4.One;
        try
        {
            byte a = Convert.ToByte(hex.Substring(0, 2), 16);
            byte r = Convert.ToByte(hex.Substring(2, 2), 16);
            byte g = Convert.ToByte(hex.Substring(4, 2), 16);
            byte b = Convert.ToByte(hex.Substring(6, 2), 16);
            return new Vec4(r / 255f, g / 255f, b / 255f, a / 255f);
        }
        catch
        {
            return Vec4.One;
        }
    }

    private static string Vec4ToHexArgb(Vec4 c)
    {
        byte a = (byte)(Math.Clamp(c.W, 0f, 1f) * 255f);
        byte r = (byte)(Math.Clamp(c.X, 0f, 1f) * 255f);
        byte g = (byte)(Math.Clamp(c.Y, 0f, 1f) * 255f);
        byte b = (byte)(Math.Clamp(c.Z, 0f, 1f) * 255f);
        return $"#{a:X2}{r:X2}{g:X2}{b:X2}";
    }

    private void DrawShaderGraphPreviewPanel(Vector2 size)
    {
        ImGui.BeginChild("##sg_preview", size, ImGuiChildFlags.None);
        DrawShaderGraphPreviewContent(size);
        ImGui.EndChild();
    }

    /// <summary>Regenerates the GLSL fragment shader from the current graph model so the preview stays live.</summary>
    private void RegenerateShaderGraphCode()
    {
        var model = shaderGraphModel;
        if (model == null)
            return;

        var issues = ShaderGraphValidator.Validate(model);
        var hasErrors = issues.Any(i => i.Severity == GrokoShaderGraphPro.Services.ValidationSeverity.Error);
        if (hasErrors)
        {
            shaderGraphCode = string.Empty;
            return;
        }

        var generator = new ShaderCodeGenerator();
        shaderGraphCode = generator.GenerateFragmentShader(model);
    }

    private void DrawShaderGraphPreviewContent(Vector2 size)
    {
        var model = shaderGraphModel!;

        ImGui.TextUnformatted("Final Preview");
        ImGui.Separator();

        var shapeIndex = Array.FindIndex(ShaderGraphPreviewShapes, s => s.Equals(shaderGraphPreviewShape, StringComparison.OrdinalIgnoreCase));
        if (shapeIndex < 0)
            shapeIndex = 0;
        ImGui.SetNextItemWidth(138f);
        if (ImGui.Combo("Shape", ref shapeIndex, ShaderGraphPreviewShapes, ShaderGraphPreviewShapes.Length))
        {
            shaderGraphPreviewShape = ShaderGraphPreviewShapes[shapeIndex];
            if (shaderGraphPreviewShape != "Custom Mesh")
                shaderGraphPreviewCustomMeshPath = string.Empty;
            shaderGraphPreviewRenderKey = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset Orbit"))
        {
            shaderGraphPreviewYaw = 0.6f;
            shaderGraphPreviewPitch = -0.3f;
            shaderGraphPreviewRenderKey = string.Empty;
        }

        if (string.IsNullOrEmpty(shaderGraphCode))
        {
            ImGui.TextDisabled("Connect nodes to an Output node to see a live preview.");
            return;
        }

        shaderGraphPreview ??= new ShaderGraphPreview();

        if (shaderGraphCode != shaderGraphPreviewedCode)
        {
            var vertexSrc = new ShaderCodeGenerator().GenerateVertexShader();
            if (shaderGraphPreview.SetShader(vertexSrc, shaderGraphCode))
                shaderGraphPreviewError = string.Empty;
            else
                shaderGraphPreviewError = shaderGraphPreview.CompileError ?? "Unknown shader error.";
            shaderGraphPreviewedCode = shaderGraphCode;
            shaderGraphPreviewRenderKey = string.Empty;
        }

        var imageDim = Math.Max(64f, Math.Min(size.X, size.Y) - 8f);
        var imageSize = new Vector2(imageDim, imageDim);
        shaderGraphPreview.Resize((int)imageSize.X, (int)imageSize.Y);

        if (string.IsNullOrEmpty(shaderGraphPreviewError))
        {
            if (ShouldRenderShaderGraphPreview(shaderGraphCode, imageSize))
            {
                shaderGraphPreview.SetShape(shaderGraphPreviewShape);
                shaderGraphPreview.Render(model, shaderGraphCode, shaderGraphPreviewYaw, shaderGraphPreviewPitch, (float)ImGui.GetTime(), ClientSize.X, ClientSize.Y);
            }
            ImGui.Image(shaderGraphPreview.TextureId, imageSize, new Vector2(0f, 1f), new Vector2(1f, 0f));
            DrawShaderGraphPreviewContextMenu();

            if (ImGui.IsItemHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var d = ImGui.GetIO().MouseDelta;
                shaderGraphPreviewYaw += d.X * 0.01f;
                shaderGraphPreviewPitch += d.Y * 0.01f;
                shaderGraphPreviewRenderKey = string.Empty;
            }
        }
        else
        {
            ImGui.TextColored(new Vec4(1f, 0.4f, 0.4f, 1f), "Shader compile error:");
            ImGui.TextWrapped(shaderGraphPreviewError);
        }

        ImGui.Separator();
        ImGui.TextDisabled($"Drag to orbit | {shaderGraphPreviewShape}");
    }

    private bool ShouldRenderShaderGraphPreview(string fragmentSrc, Vector2 imageSize)
    {
        double now = ImGui.GetTime();
        string key = BuildShaderGraphPreviewRenderKey(fragmentSrc, imageSize);
        bool keyChanged = !string.Equals(shaderGraphPreviewRenderKey, key, StringComparison.Ordinal);
        bool animated = ShaderGraphFragmentUsesTime(fragmentSrc);
        bool animationDue = animated && now - shaderGraphPreviewLastRenderTime >= 1.0 / 15.0;

        if (!keyChanged && !animationDue)
            return false;

        shaderGraphPreviewRenderKey = key;
        shaderGraphPreviewLastRenderTime = now;
        return true;
    }

    private string BuildShaderGraphPreviewRenderKey(string fragmentSrc, Vector2 imageSize)
    {
        int codeHash = StringComparer.Ordinal.GetHashCode(fragmentSrc);
        int width = Math.Max(1, (int)imageSize.X);
        int height = Math.Max(1, (int)imageSize.Y);
        int yaw = (int)MathF.Round(shaderGraphPreviewYaw * 1000f);
        int pitch = (int)MathF.Round(shaderGraphPreviewPitch * 1000f);
        return $"{codeHash}|{width}x{height}|{yaw}|{pitch}|{shaderGraphPreviewShape}|{shaderGraphPreviewCustomMeshPath}";
    }

    private static bool ShaderGraphFragmentUsesTime(string fragmentSrc)
    {
        int first = fragmentSrc.IndexOf("u_Time", StringComparison.Ordinal);
        return first >= 0 && fragmentSrc.IndexOf("u_Time", first + "u_Time".Length, StringComparison.Ordinal) >= 0;
    }

    private static readonly string[] ShaderGraphPreviewShapes =
        { "Sphere", "Capsule", "Cylinder", "Cube", "Quad", "Sprite", "Custom Mesh" };

    private void DrawShaderGraphPreviewContextMenu()
    {
        if (!ImGui.BeginPopupContextItem("##sg_preview_context", ImGuiPopupFlags.MouseButtonRight))
            return;

        foreach (var shape in ShaderGraphPreviewShapes)
        {
            bool selected = shaderGraphPreviewShape.Equals(shape, StringComparison.OrdinalIgnoreCase);
            if (ImGui.MenuItem(shape, selected ? "✓" : string.Empty, selected))
            {
                shaderGraphPreviewShape = shape;
                if (shape != "Custom Mesh")
                    shaderGraphPreviewCustomMeshPath = string.Empty;
                shaderGraphPreviewRenderKey = string.Empty;
            }
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Clear Reference", string.Empty, false, !string.IsNullOrWhiteSpace(shaderGraphPreviewCustomMeshPath)))
        {
            shaderGraphPreviewCustomMeshPath = string.Empty;
            shaderGraphPreviewRenderKey = string.Empty;
        }

        ImGui.EndPopup();
    }

}
