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
        BeginHierarchyDropFrame();

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##HierarchySearch", "Search hierarchy", ref hierarchyFilter, 128);
        ImGui.Separator();

        ImGui.PushStyleColor(ImGuiCol.DragDropTarget, new System.Numerics.Vector4(0.72f, 0.72f, 0.70f, 0.55f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        ImGui.BeginChild("HierarchyTree", new Vector2(0f, -34f), ImGuiChildFlags.None);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 16f);
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
        bool rootBtnHot = !isPlaying && hierarchyDragObjectId != null;
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.Button("Drop here to move to root", new Vector2(-1f, 24f));
        ImGui.PopStyleColor();
        if (rootBtnHot && ImGui.IsItemHovered())
        {
            var dragged = sceneGraph.FindById(hierarchyDragObjectId!);
            if (dragged != null && CanDropHierarchyTargets(GetHierarchyDragTargets(dragged), null))
            {
                hierarchyDropPending = true;
                hierarchyDropParent = null;
                hierarchyDropIndex = objects.Count;
                hierarchyDropIntent = 1;
            }
        }
        CommitHierarchyDropFrame();   // aplica el drop pendiente al soltar (ratón crudo)
        ImGui.PopStyleColor();

        ImGui.EndChild();
    }

    private void BeginHierarchyDropFrame()
    {
        hierarchyDropPending = false;
        hierarchyDropParent = null;
        hierarchyDropIndex = 0;
    }

    private void CommitHierarchyDropFrame()
    {
        // Detección de "soltar" con el estado CRUDO del ratón (no ImGui.IsMouseReleased): un
        // drag NATIVO activo consume IsMouseReleased y por eso el commit nunca disparaba. Esto
        // sí funciona siempre y evita que un drag quede "pegado" (síntoma tipo modo Play).
        bool released = hierarchyMouseWasDown && !ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (released && hierarchyDropPending && hierarchyDragObjectId != null)
        {
            DropHierarchyObject(hierarchyDropParent, hierarchyDropIndex);
            hierarchyMouseDownObjectId = null;
        }
        hierarchyMouseWasDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
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
        // Soltar en el área vacía de abajo = mover a la raíz (destino MANUAL, ver CommitHierarchyDropFrame).
        if (ImGui.IsItemHovered() && hierarchyDragObjectId != null)
        {
            var dragged = sceneGraph.FindById(hierarchyDragObjectId);
            if (dragged != null && CanDropHierarchyTargets(GetHierarchyDragTargets(dragged), null))
            {
                hierarchyDropPending = true;
                hierarchyDropParent = null;
                hierarchyDropIndex = objects.Count;
                hierarchyDropIntent = 1;

                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var drawList = ImGui.GetWindowDrawList();
                uint color = ImGui.GetColorU32(new System.Numerics.Vector4(0.55f, 0.86f, 0.62f, 0.85f));
                drawList.AddRect(min + new Vector2(3f, 3f), max - new Vector2(3f, 3f), color, 3f, ImDrawFlags.None, 1.5f);
            }
        }
    }

    private void DrawHierarchyRootDropTarget()
    {
        DrawHierarchyInsertDropTarget(null, objects.Count, "root_footer");
    }

    private void ClearHierarchyDragIfReleased()
    {
        if (hierarchyDragObjectId == null && hierarchyMouseDownObjectId == null)
            return;

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            hierarchyDragObjectId = null;
            hierarchyMouseDownObjectId = null;
        }
    }

    // Los huecos de inserción ya no se usan: el reordenar (antes/después) lo resuelven las ZONAS
    // de la fila (30% superior = antes, 30% inferior = después, centro = como hijo). Sin ítems
    // fantasma de 6px la lista queda compacta. Se deja como no-op para no tocar los llamadores.
    private void DrawHierarchyInsertDropTarget(GameObject? parent, int index, string id)
    {
    }

    private void DropHierarchyObject(GameObject? parent, int index)
    {
        var dragged = hierarchyDragObjectId != null ? sceneGraph.FindById(hierarchyDragObjectId) : null;
        hierarchyDragObjectId = null;
        if (dragged == null)
            return;

        var targets = GetHierarchyDragTargets(dragged);
        if (targets.Count == 1)
            ReparentObject(targets[0], parent, index);
        else
            ReparentObjects(targets, parent, index);
    }

    private List<GameObject> GetHierarchyDragTargets(GameObject dragged)
    {
        if (!selection.Selected.Contains(dragged))
            return new List<GameObject> { dragged };

        var selectedTopLevel = GetTopLevelSelectionTargets();
        if (selectedTopLevel.Count <= 1)
            return new List<GameObject> { dragged };

        var selectedSet = selectedTopLevel.ToHashSet();
        return sceneGraph.Flatten()
            .Where(selectedSet.Contains)
            .ToList();
    }

    private void DrawHierarchyNode(GameObject obj)
    {
        bool filtering = !string.IsNullOrWhiteSpace(hierarchyFilter);
        if (filtering && !HierarchyMatchesFilter(obj))
            return;

        bool isSelected = selection.Selected.Contains(obj);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.SpanFullWidth;
        if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;
        if (obj.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (filtering) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        bool active = obj.IsActive;

        ImGui.PushID(obj.EditorId);

        // Resaltado de selección redondeado, de fila completa (estilo Unity de la referencia).
        var rowMin = ImGui.GetCursorScreenPos();
        float rowWidth = ImGui.GetContentRegionAvail().X;
        const float rowHeight = 24f;
        var rowRectMin = new Vector2(rowMin.X - 6f, rowMin.Y - 2f);
        var rowRectMax = new Vector2(rowMin.X + rowWidth - 2f, rowMin.Y + rowHeight - 2f);
        bool rowHovered = ImGui.IsMouseHoveringRect(rowRectMin, rowRectMax);
        if (rowHovered && !isSelected)
        {
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(rowRectMin, rowRectMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.245f, 0.245f, 0.245f, 0.72f)), 5f);
        }
        if (isSelected)
        {
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(rowRectMin, rowRectMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.325f, 0.325f, 0.325f, 0.96f)), 5f);
            dl.AddRect(rowRectMin, rowRectMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.525f, 0.525f, 0.505f, 0.75f)), 5f, ImDrawFlags.None, 1f);
        }
        // Nuestra selección es redondeada → ocultamos el resaltado cuadrado del TreeNode.
        ImGui.PushStyleColor(ImGuiCol.Header, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new System.Numerics.Vector4(0.72f, 0.72f, 0.72f, 0.10f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2f, 3f));

        // Icono con Dummy (NO InvisibleButton): un botón robaría el hover del target de drop
        // en la zona del icono. Dummy reserva el espacio sin capturar hover, así el TreeNode
        // (SpanFullWidth) es target de fila completa y se puede soltar sobre el nombre.
        {
            var iconMin = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(18f, 18f));
            DrawAtlasIconOrFallback(ImGui.GetWindowDrawList(), GetObjectIcon(obj), iconMin + new Vector2(1f, 1f), 16f, ImGui.GetColorU32(UiIcon));
        }
        ImGui.SameLine(0f, 6f);

        bool prefab = !string.IsNullOrWhiteSpace(obj.PrefabAssetPath);
        bool renaming = inlineRenameObjectId == obj.EditorId;
        if (!active)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.48f, 0.50f, 0.52f, 1f));
        else if (prefab)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.80f, 0.84f, 0.88f, 1f));
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
        DrawHierarchyRowBadges(obj, rowRectMax);
        ImGui.PopStyleVar();
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
            bool ctrl = ImGui.IsKeyDown(ImGuiKey.ModCtrl);
            bool shift = ImGui.IsKeyDown(ImGuiKey.ModShift);
            var mode = ctrl
                ? SelectionService.SelectionMode.Toggle
                : shift
                    ? SelectionService.SelectionMode.Add
                    : SelectionService.SelectionMode.Replace;
            selection.Select(obj, mode);
        }
        if (!renaming && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (!selection.Selected.Contains(obj))
                selection.SelectSingle(obj);
        }

        if (!renaming)
        {
            DrawHierarchyDragDrop(obj);
            HandleHierarchyRowDrop(obj, rowHovered, rowRectMin, rowRectMax);
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

    private void HandleHierarchyRowDrop(GameObject target, bool rowHovered, Vector2 rowRectMin, Vector2 rowRectMax)
    {
        if (isPlaying || !string.IsNullOrWhiteSpace(hierarchyFilter) || hierarchyDragObjectId == null || !rowHovered)
            return;

        var dragged = sceneGraph.FindById(hierarchyDragObjectId);
        if (dragged == null)
        {
            hierarchyDragObjectId = null;
            return;
        }

        var targets = GetHierarchyDragTargets(dragged);
        float rowHeight = Math.Max(1f, rowRectMax.Y - rowRectMin.Y);
        float mouseY = ImGui.GetMousePos().Y;
        float topZone = rowRectMin.Y + rowHeight * 0.25f;
        float bottomZone = rowRectMin.Y + rowHeight * 0.75f;

        GameObject? dropParent;
        int dropIndex;
        HierarchyDropIntent intent;

        bool targetIsEmptyContainer = target.Children.Count == 0;

        if (targetIsEmptyContainer)
        {
            intent = HierarchyDropIntent.AsChild;
            dropParent = target;
            dropIndex = 0;
        }
        else if (mouseY < topZone)
        {
            intent = HierarchyDropIntent.Before;
            dropParent = target.Parent;
            dropIndex = sceneGraph.IndexOf(target);
        }
        else if (mouseY > bottomZone)
        {
            intent = HierarchyDropIntent.After;
            dropParent = target.Parent;
            dropIndex = sceneGraph.IndexOf(target) + 1;
        }
        else
        {
            intent = HierarchyDropIntent.AsChild;
            dropParent = target;
            dropIndex = target.Children.Count;
        }

        bool valid = CanDropHierarchyTargets(targets, dropParent);
        if (valid)
        {
            hierarchyDropPending = true;
            hierarchyDropParent = dropParent;
            hierarchyDropIndex = dropIndex;
            hierarchyDropIntent = (int)intent;
        }

        var drawList = ImGui.GetWindowDrawList();
        // Sin texto: solo el recuadro/línea. Verde = destino válido, rojo tenue = inválido.
        uint border = ImGui.GetColorU32(valid
            ? new System.Numerics.Vector4(0.42f, 0.86f, 0.55f, 0.95f)
            : new System.Numerics.Vector4(0.92f, 0.42f, 0.42f, 0.85f));

        if (intent == HierarchyDropIntent.AsChild)
        {
            // Pastilla redondeada limpia: relleno verde suave + borde a juego (estilo Unity).
            uint fill = ImGui.GetColorU32(valid
                ? new System.Numerics.Vector4(0.26f, 0.55f, 0.36f, 0.22f)
                : new System.Numerics.Vector4(0.46f, 0.18f, 0.18f, 0.20f));
            drawList.AddRectFilled(rowRectMin, rowRectMax, fill, 6f);
            drawList.AddRect(rowRectMin, rowRectMax, border, 6f, ImDrawFlags.None, 1.8f);
        }
        else
        {
            // Línea de inserción con topes redondos en ambos extremos.
            float y = intent == HierarchyDropIntent.Before ? rowRectMin.Y + 1f : rowRectMax.Y - 2f;
            float x0 = rowRectMin.X + 8f, x1 = rowRectMax.X - 8f;
            drawList.AddLine(new Vector2(x0, y), new Vector2(x1, y), border, 2.4f);
            drawList.AddCircleFilled(new Vector2(x0, y), 3.2f, border, 12);
            drawList.AddCircleFilled(new Vector2(x1, y), 3.2f, border, 12);
        }
    }

    private enum HierarchyDropIntent
    {
        Before,
        AsChild,
        After
    }

    private void UpdateHierarchyManualDragSource(GameObject obj, bool rowHovered)
    {
        if (isPlaying || !string.IsNullOrWhiteSpace(hierarchyFilter))
            return;

        if (rowHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            hierarchyMouseDownObjectId = obj.EditorId;
            hierarchyDragObjectId = null;
        }

        if (hierarchyMouseDownObjectId == obj.EditorId &&
            ImGui.IsMouseDown(ImGuiMouseButton.Left) &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3f))
        {
            hierarchyDragObjectId = obj.EditorId;
            DrawHierarchyDragPreview(obj);
        }
    }

    private void DrawHierarchyDragPreview(GameObject obj)
    {
        string text = selection.Selected.Contains(obj) && selection.Selected.Count > 1
            ? $"{GetTopLevelSelectionTargets().Count} objects"
            : obj.Name;

        var mouse = ImGui.GetMousePos();
        var textSize = ImGui.CalcTextSize(text);
        var min = mouse + new Vector2(14f, 14f);
        var max = min + textSize + new Vector2(18f, 10f);
        var drawList = ImGui.GetForegroundDrawList();
        uint bg = ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.09f, 0.10f, 0.92f));
        uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.52f, 0.72f, 0.95f, 0.88f));
        uint fg = ImGui.GetColorU32(new System.Numerics.Vector4(0.92f, 0.95f, 0.98f, 1f));
        drawList.AddRectFilled(min, max, bg, 6f);
        drawList.AddRect(min, max, border, 6f, ImDrawFlags.None, 1.2f);
        drawList.AddText(min + new Vector2(9f, 4f), fg, text);
    }

    private bool CanDropHierarchyTargets(IReadOnlyList<GameObject> targets, GameObject? newParent)
    {
        if (targets.Count == 0)
            return false;

        if (newParent == null)
            return true;

        foreach (var target in targets)
        {
            if (ReferenceEquals(target, newParent))
                return false;

            if (sceneGraph.IsDescendantOf(newParent, target))
                return false;
        }

        return true;
    }

    private static void DrawHierarchyRowBadges(GameObject obj, Vector2 rowMax)
    {
        var drawList = ImGui.GetWindowDrawList();
        float x = rowMax.X - 8f;
        float y = rowMax.Y - 12f;

        void Dot(System.Numerics.Vector4 color)
        {
            drawList.AddCircleFilled(new Vector2(x, y), 3.2f, ImGui.GetColorU32(color), 12);
            x -= 10f;
        }

        Dot(obj.IsActive
            ? new System.Numerics.Vector4(0.35f, 0.95f, 0.62f, 0.95f)
            : new System.Numerics.Vector4(0.45f, 0.48f, 0.54f, 0.80f));

        if (!string.IsNullOrWhiteSpace(obj.PrefabAssetPath))
            Dot(new System.Numerics.Vector4(0.62f, 0.70f, 0.78f, 0.95f));

        int enabledComponents = obj.Components.Count(c => c.Enabled);
        if (enabledComponents <= 0)
            return;

        string label = enabledComponents > 9 ? "9+" : enabledComponents.ToString(CultureInfo.InvariantCulture);
        var textSize = ImGui.CalcTextSize(label);
        var min = new Vector2(x - textSize.X - 9f, rowMax.Y - 20f);
        var max = new Vector2(x + 1f, rowMax.Y - 7f);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.13f, 0.17f, 0.92f)), 5f);
        drawList.AddRect(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0.22f, 0.30f, 0.40f, 0.85f)), 5f);
        drawList.AddText(min + new Vector2(5f, -1f), ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.82f, 0.95f, 1f)), label);
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
        if (obj.Type == 1 || obj.Type == 3 || obj.Type == 4 || obj.Type == 6) return EditorIcon.Cube;
        if (obj.Type == 2 || obj.Type == 5) return EditorIcon.Plane;
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

    // Drag-drop NATIVO de ImGui (fiable): la fila es a la vez source y target.
    // Antes esto convivía con un sistema MANUAL (rect + release propios) que peleaba por el
    // release del ratón, y soltar sobre la fila de un GameObject VACÍO para emparentarlo no
    // cuajaba. Ahora el target de la fila usa AcceptDragDropOnRelease → DropHierarchyObject
    // como hijo, que es el mecanismo que sí funciona (el mismo de los huecos de reordenar).
    private void DrawHierarchyDragDrop(GameObject obj)
    {
        if (isPlaying || !string.IsNullOrWhiteSpace(hierarchyFilter))
            return;

        // SOLO source nativo: publica el payload "GROKO_HIERARCHY_OBJECT" para que OTROS paneles
        // (inspector, project→prefab) puedan aceptar el objeto. DENTRO de la jerarquía el destino
        // es MANUAL por zonas (HandleHierarchyRowDrop) con commit por estado crudo del ratón, que
        // no depende de la entrega nativa (esa era la causa de que soltara al root o no soltara).
        if (ImGui.BeginDragDropSource())
        {
            hierarchyDragObjectId = obj.EditorId;
            ImGui.SetDragDropPayload("GROKO_HIERARCHY_OBJECT", IntPtr.Zero, 0);
            if (selection.Selected.Contains(obj) && selection.Selected.Count > 1)
                ImGui.Text($"{GetTopLevelSelectionTargets().Count} objects");
            else
                ImGui.Text(obj.Name);
            ImGui.EndDragDropSource();
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
