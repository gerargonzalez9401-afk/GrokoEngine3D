using ImGuiNET;
using System;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private static void DrawReadOnlyComboRow(string label, string value)
    {
        FieldRow(label);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.12f, 0.12f, 0.13f, 1f));
        ImGui.SetNextItemWidth(-1f);
        ImGui.BeginDisabled();
        ImGui.InputText("##" + label, ref value, 256, ImGuiInputTextFlags.ReadOnly);
        ImGui.EndDisabled();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private static void DrawFloat(string label, float value, Action<float> set, float speed, float min, float max)
    {
        FieldRow(label);
        float edit = value;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.12f, 0.12f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new System.Numerics.Vector4(0.17f, 0.17f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new System.Numerics.Vector4(0.18f, 0.18f, 0.19f, 1f));
        if (ImGui.DragFloat("##" + label, ref edit, speed, min, max))
            set(Math.Clamp(edit, min, max));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
    }

    private static void DrawInt(string label, int value, Action<int> set, float speed, int min, int max)
    {
        FieldRow(label);
        int edit = value;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.12f, 0.12f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new System.Numerics.Vector4(0.17f, 0.17f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new System.Numerics.Vector4(0.18f, 0.18f, 0.19f, 1f));
        if (ImGui.DragInt("##" + label, ref edit, speed, min, max))
            set(Math.Clamp(edit, min, max));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
    }

    private static bool DrawUnitySliderFloat(string label, ref float value, float min, float max, float speed = 0.01f)
    {
        FieldRow(label);
        ImGui.PushID(label);

        float available = ImGui.GetContentRegionAvail().X;
        float rowH = currentDrawingApp?.guiSliderHeight ?? currentDrawingApp?.designerRowHeight ?? 20f;
        float numericW = available > 128f ? 54f : Math.Clamp(available * 0.36f, 42f, 54f);
        float sliderW = Math.Max(34f, available - numericW - 4f);
        bool changed = false;

        changed |= DrawUnityTrackSlider("##slider", ref value, min, max, sliderW, rowH);
        RegisterGuiElement(GuiStyleClass.Slider, label);

        ImGui.SameLine(0f, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.12f, 0.12f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new System.Numerics.Vector4(0.17f, 0.17f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new System.Numerics.Vector4(0.18f, 0.18f, 0.19f, 1f));
        ImGui.SetNextItemWidth(numericW);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, Math.Max(1f, (rowH - 16f) * 0.5f)));
        changed |= ImGui.DragFloat("##value", ref value, speed, min, max, "%.3f");
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        value = Math.Clamp(value, min, max);
        ImGui.PopID();
        ImGui.Dummy(new Vector2(0f, 3f));
        return changed;
    }

    private static bool DrawUnityTrackSlider(string id, ref float value, float min, float max, float width, float height)
    {
        ImGui.PushID(id);
        float controlH = Math.Max(18f, height);
        float safeWidth = Math.Max(24f, width);
        value = Math.Clamp(value, min, max);
        Vector2 minPos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##track", new Vector2(safeWidth, controlH));

        bool changed = false;
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        if ((hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) || active)
        {
            Vector2 mouse = ImGui.GetMousePos();
            float t = Math.Clamp((mouse.X - minPos.X) / safeWidth, 0f, 1f);
            float next = min + (max - min) * t;
            if (Math.Abs(next - value) > float.Epsilon)
            {
                value = next;
                changed = true;
            }
        }

        var drawList = ImGui.GetWindowDrawList();
        float centerY = minPos.Y + controlH * 0.5f;
        float knobRadius = active ? 6.6f : hovered ? 6.2f : 5.8f;
        float valueT = max <= min ? 0f : Math.Clamp((value - min) / (max - min), 0f, 1f);
        Vector2 lineA = new(minPos.X + knobRadius, centerY);
        Vector2 lineB = new(minPos.X + safeWidth - knobRadius, centerY);
        Vector2 knob = new(lineA.X + (lineB.X - lineA.X) * valueT, centerY);

        uint track = ImGui.GetColorU32(new System.Numerics.Vector4(0.36f, 0.36f, 0.36f, 1f));
        uint trackHot = ImGui.GetColorU32(new System.Numerics.Vector4(0.47f, 0.47f, 0.47f, 1f));
        uint knobFill = ImGui.GetColorU32(active
            ? new System.Numerics.Vector4(0.76f, 0.76f, 0.76f, 1f)
            : new System.Numerics.Vector4(0.62f, 0.62f, 0.62f, 1f));
        uint knobBorder = ImGui.GetColorU32(new System.Numerics.Vector4(0.18f, 0.18f, 0.18f, 1f));

        drawList.AddLine(lineA, lineB, hovered || active ? trackHot : track, 2.2f);
        drawList.AddCircleFilled(knob, knobRadius, knobFill, 18);
        drawList.AddCircle(knob, knobRadius, knobBorder, 18, 1f);

        ImGui.PopID();
        return changed;
    }

    private static void DrawVector3(string label, Vector3 value, Action<Vector3> set, float speed)
    {
        float x = value.X, y = value.Y, z = value.Z;
        if (DrawAxisFloat3(label, ref x, ref y, ref z, speed, -100000f, 100000f))
            set(new Vector3(x, y, z));
    }

    private static void DrawFloat2(string label, float a, float b, Action<float, float> set, float speed, float min, float max)
    {
        FieldRow(label);
        var edit = new System.Numerics.Vector2(a, b);
        if (ImGui.DragFloat2("##" + label, ref edit, speed, min, max))
            set(edit.X, edit.Y);
    }
}
