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
    private void DrawHierarchyPanel(Vector2 size)
    {
        BeginPanel("##HierarchyPanel", size);
        DrawPanelHeader("Hierarchy", $"{GetCachedObjectCount()} objects");

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##HierarchySearch", "Search hierarchy", ref hierarchyFilter, 128);
        ImGui.Separator();

        ImGui.PushStyleColor(ImGuiCol.DragDropTarget, new System.Numerics.Vector4(0.92f, 0.94f, 0.97f, 0.55f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 10f)); // margen interior estilo Unity
        ImGui.BeginChild("HierarchyTree", new Vector2(0f, -34f), ImGuiChildFlags.None);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 14f);
        var rootSnapshot = objects.ToList();
        for (int i = 0; i < rootSnapshot.Count; i++)
        {
            DrawHierarchyInsertDropTarget(null, i, "root_before_" + rootSnapshot[i].EditorId);
            DrawHierarchyNode(rootSnapshot[i]);
        }
        DrawHierarchyRootDropSpace();
        // Clic derecho en zona vacía → crear objetos en la raíz (estilo Unity).
        PushContextMenuStyle();
        if (ImGui.BeginPopupContextWindow("HierarchyEmptyMenu",
                ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (isPlaying) ImGui.TextDisabled("Detén el Play para crear objetos");
            else DrawObjectCreationMenu(null);
            ImGui.EndPopup();
        }
        PopContextMenuStyle();
        ImGui.PopStyleVar(2);
        ImGui.EndChild();
        ImGui.PopStyleVar(); // WindowPadding

        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.13f, 0.13f, 0.14f, 1f));
        ImGui.Button("Drop here to move to root", new Vector2(-1f, 24f));
        ImGui.PopStyleColor();
        DrawHierarchyRootDropTarget();
        ImGui.PopStyleColor();

        ImGui.EndChild();
    }

    private void DrawHierarchyRootDropSpace()
    {
        if (isPlaying || !string.IsNullOrWhiteSpace(hierarchyFilter))
            return;

        float height = Math.Max(24f, ImGui.GetContentRegionAvail().Y);
        ImGui.InvisibleButton("##HierarchyRootDropSpace", new Vector2(-1f, height));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            selection.Clear();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            selection.Clear();
            ImGui.OpenPopup("HierarchyEmptyMenu");
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && hierarchyDragObjectId != null)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();
            uint color = ImGui.GetColorU32(new System.Numerics.Vector4(0.35f, 0.62f, 1f, 0.85f));
            drawList.AddRect(min + new Vector2(3f, 3f), max - new Vector2(3f, 3f), color, 3f, ImDrawFlags.None, 1.5f);
        }

        if (!ImGui.BeginDragDropTarget())
            return;

        bool delivered = AcceptDragDropOnRelease("GROKO_HIERARCHY_OBJECT");
        if (delivered && hierarchyDragObjectId != null)
            DropHierarchyObject(null, objects.Count);

        ImGui.EndDragDropTarget();
    }

    private void DrawHierarchyRootDropTarget()
    {
        DrawHierarchyInsertDropTarget(null, objects.Count, "root_footer");
    }

    private void DrawHierarchyInsertDropTarget(GameObject? parent, int index, string id)
    {
        if (isPlaying || !string.IsNullOrWhiteSpace(hierarchyFilter))
            return;

        ImGui.PushID(id);
        Vector2 min = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##HierarchyInsertDrop", new Vector2(-1f, 6f));
        bool hovered = ImGui.IsItemHovered();

        if (hovered && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && hierarchyDragObjectId != null)
        {
            var max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();
            uint color = ImGui.GetColorU32(new System.Numerics.Vector4(0.35f, 0.62f, 1f, 1f));
            float y = min.Y + 3f;
            drawList.AddLine(new Vector2(min.X + 4f, y), new Vector2(max.X - 4f, y), color, 2f);
        }

        if (!ImGui.BeginDragDropTarget())
        {
            ImGui.PopID();
            return;
        }

        bool delivered = AcceptDragDropOnRelease("GROKO_HIERARCHY_OBJECT");
        if (delivered && hierarchyDragObjectId != null)
        {
            DropHierarchyObject(parent, index);
        }

        ImGui.EndDragDropTarget();
        ImGui.PopID();
    }

    private void DropHierarchyObject(GameObject? parent, int index)
    {
        var dragged = hierarchyDragObjectId != null ? sceneGraph.FindById(hierarchyDragObjectId) : null;
        hierarchyDragObjectId = null;
        if (dragged == null)
            return;

        ReparentObject(dragged, parent, index);
    }

    private void DrawHierarchyNode(GameObject obj)
    {
        bool filtering = !string.IsNullOrWhiteSpace(hierarchyFilter);
        if (filtering && !HierarchyMatchesFilter(obj))
            return;

        bool isSelected = selection.Selected.Contains(obj);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;
        if (obj.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (filtering) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        bool active = obj.IsActive;

        ImGui.PushID(obj.EditorId);

        // Resaltado de selección redondeado, de fila completa (estilo Unity de la referencia).
        var rowMin = ImGui.GetCursorScreenPos();
        float rowWidth = ImGui.GetContentRegionAvail().X;
        if (isSelected)
        {
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(
                new Vector2(rowMin.X - 6f, rowMin.Y - 1f),
                new Vector2(rowMin.X + rowWidth - 2f, rowMin.Y + 16f),
                ImGui.GetColorU32(new System.Numerics.Vector4(0.29f, 0.31f, 0.35f, 1f)), 4f);
        }
        // Nuestra selección es redondeada → ocultamos el resaltado cuadrado del TreeNode.
        ImGui.PushStyleColor(ImGuiCol.Header, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new System.Numerics.Vector4(1f, 1f, 1f, 0.06f));

        if (obj.GetComponent<Camera>() != null || obj.IsCamera)
            DrawInlineIcon(EditorIcon.Camera, "Camera", 16f);
        else
            DrawInlineCubeIcon(GetObjectIconTooltip(obj), 16f);
        ImGui.SameLine(0f, 4f);

        bool prefab = !string.IsNullOrWhiteSpace(obj.PrefabAssetPath);
        bool renaming = inlineRenameObjectId == obj.EditorId;
        if (!active)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.48f, 0.50f, 0.52f, 1f));
        else if (prefab)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.56f, 0.76f, 1f, 1f));
        bool open;
        if (renaming)
        {
            open = ImGui.TreeNodeEx("##Node", flags);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(60f, ImGui.GetContentRegionAvail().X - 4f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2f, 1f));
            if (inlineRenameObjectFocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                inlineRenameObjectFocusPending = false;
            }
            bool enter = ImGui.InputText("##rename", ref inlineRenameObjectName, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool deactivated = ImGui.IsItemDeactivated();
            ImGui.PopStyleVar();
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                CancelInlineRenameObject();
            else if (enter || deactivated)
                CommitInlineRenameObject(obj);
        }
        else
        {
            open = ImGui.TreeNodeEx("Node", flags, GetHierarchyLabel(obj));
        }
        if (!active || prefab)
            ImGui.PopStyleColor();
        ImGui.PopStyleColor(3); // Header / HeaderActive / HeaderHovered
        DrawHierarchyBranchLine();
        if (!renaming && ImGui.IsItemClicked())
        {
            bool wasSelected = selection.Selected.Contains(obj);
            double now = GLFW.GetTime();
            if (wasSelected && string.Equals(lastObjectClickId, obj.EditorId, StringComparison.Ordinal)
                && (now - lastObjectClickTime) > 0.35 && (now - lastObjectClickTime) < 1.2)
            {
                BeginRenameObject(obj);
            }
            lastObjectClickId = obj.EditorId;
            lastObjectClickTime = now;
            selection.SelectFromViewport(obj, ImGui.IsKeyDown(ImGuiKey.ModCtrl) || ImGui.IsKeyDown(ImGuiKey.ModShift));
        }
        if (!renaming && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            selection.SelectSingle(obj);

        if (!renaming)
        {
            DrawHierarchyDragDrop(obj);
            DrawHierarchyContextMenu(obj);
        }

        if (open)
        {
            var childSnapshot = obj.Children.ToList();
            for (int i = 0; i < childSnapshot.Count; i++)
            {
                DrawHierarchyInsertDropTarget(obj, i, "child_before_" + childSnapshot[i].EditorId);
                DrawHierarchyNode(childSnapshot[i]);
            }
            DrawHierarchyInsertDropTarget(obj, obj.Children.Count, "child_end_" + obj.EditorId);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    private static void DrawHierarchyBranchLine()
    {
        // Sin líneas de guía ni subrayado de hover: la fila se resalta con el rect redondeado.
    }

    private string GetHierarchyLabel(GameObject obj)
    {
        return obj.Name;
    }

    private static string GetHierarchyIcon(GameObject obj)
    {
        if (!string.IsNullOrWhiteSpace(obj.PrefabAssetPath)) return "[P]";
        if (obj.GetComponent<Camera>() != null || obj.IsCamera) return "[C]";
        if (obj.Components.Any(IsLightComponent)) return "[L]";
        if (obj.GetComponent<MeshFilter>() != null) return "[O]";
        return "[ ]";
    }

    private static EditorIcon GetObjectIcon(GameObject obj)
    {
        if (!string.IsNullOrWhiteSpace(obj.PrefabAssetPath)) return EditorIcon.Prefab;
        if (obj.GetComponent<Camera>() != null || obj.IsCamera) return EditorIcon.Camera;
        if (obj.Components.Any(IsLightComponent)) return EditorIcon.Light;
        if (obj.GetComponent<MeshFilter>() != null) return EditorIcon.Mesh;
        if (obj.Components.Any(c => c is MonoBehaviour)) return EditorIcon.Script;
        return EditorIcon.Transform;
    }

    private static string GetObjectIconTooltip(GameObject obj)
    {
        if (!string.IsNullOrWhiteSpace(obj.PrefabAssetPath)) return "Prefab instance";
        if (obj.GetComponent<Camera>() != null || obj.IsCamera) return "Camera";
        if (obj.Components.Any(IsLightComponent)) return "Light";
        if (obj.GetComponent<MeshFilter>() != null) return "Mesh object";
        if (obj.Components.Any(c => c is MonoBehaviour)) return "Script object";
        return "GameObject";
    }

    private static bool IsLightComponent(Component component) =>
        component is DirectionalLight or PointLight or SpotLight or AmbientLight or AreaLight or RectangleLight;

    private bool HierarchyMatchesFilter(GameObject obj)
    {
        if (obj.Name.Contains(hierarchyFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var component in obj.Components)
            if (component.GetType().Name.Contains(hierarchyFilter, StringComparison.OrdinalIgnoreCase))
                return true;

        return obj.Children.Any(HierarchyMatchesFilter);
    }

    private void DrawHierarchyDragDrop(GameObject obj)
    {
        if (isPlaying || !string.IsNullOrWhiteSpace(hierarchyFilter))
            return;

        if (ImGui.BeginDragDropSource())
        {
            hierarchyDragObjectId = obj.EditorId;
            ImGui.SetDragDropPayload("GROKO_HIERARCHY_OBJECT", IntPtr.Zero, 0);
            ImGui.Text(obj.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_HIERARCHY_OBJECT");
            if (delivered && hierarchyDragObjectId != null)
                DropHierarchyObject(obj, obj.Children.Count);
            ImGui.EndDragDropTarget();
        }
    }

    private void DrawHierarchyContextMenu(GameObject obj)
    {
        PushContextMenuStyle();
        if (!ImGui.BeginPopupContextItem("HierarchyContext_" + obj.EditorId))
        {
            PopContextMenuStyle();
            return;
        }

        if (ImGui.MenuItem("Frame"))
        {
            selected = obj;
            FrameObject(obj);
        }

        if (!isPlaying)
        {
            ImGui.Separator();
            if (ImGui.MenuItem("Rename", "F2")) BeginRenameObject(obj);
            if (ImGui.MenuItem("Duplicate", "Ctrl+D")) DuplicateObject(obj);
            if (ImGui.MenuItem("Delete", "Del")) DeleteObject(obj);

            ImGui.Separator();
            if (ImGui.BeginMenu("Create Child"))
            {
                DrawObjectCreationMenu(obj);
                ImGui.EndMenu();
            }

            if (!string.IsNullOrWhiteSpace(obj.PrefabAssetPath) || obj.Parent != null)
                ImGui.Separator();
            if (!string.IsNullOrWhiteSpace(obj.PrefabAssetPath) && ImGui.MenuItem("Apply Prefab"))
                ApplyPrefab(obj);
            if (obj.Parent != null && ImGui.MenuItem("Move To Root"))
                ReparentObject(obj, null, objects.Count);
        }

        ImGui.EndPopup();
        PopContextMenuStyle();
    }

}
