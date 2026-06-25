using ImGuiNET;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using Vector2 = System.Numerics.Vector2;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private void QueueEditorProgressTask(string title, string detail, Action work)
    {
        pendingEditorProgressTask = new EditorProgressTask(title, detail, work);
        ShowEditorProgress(title, detail, 0.04f, running: true);
    }

    private void ProcessQueuedEditorProgressTask()
    {
        if (pendingEditorProgressTask == null)
            return;

        var task = pendingEditorProgressTask;
        pendingEditorProgressTask = null;

        ShowEditorProgress(task.Title, task.Detail, 0.18f, running: true);
        try
        {
            task.Work();
            if (editorProgressRunning)
                CompleteEditorProgress(task.Title, "Done");
        }
        catch (Exception ex)
        {
            CompleteEditorProgress(task.Title, "Failed: " + ex.Message);
            statusMessage = task.Title + " failed: " + ex.Message;
            GrokoEngine.Debug.LogError(statusMessage);
        }
    }

    private void ShowEditorProgress(string title, string detail, float progress, bool running)
    {
        editorProgressVisible = true;
        editorProgressRunning = running;
        editorProgressTitle = title;
        editorProgressDetail = detail;
        editorProgressValue = Math.Clamp(progress, 0f, 1f);
        editorProgressHideAfter = 0.0;
        statusMessage = detail;
    }

    private void UpdateEditorProgress(string detail, float progress)
    {
        editorProgressDetail = detail;
        editorProgressValue = Math.Clamp(progress, 0f, 1f);
        statusMessage = detail;
    }

    private void CompleteEditorProgress(string title, string detail)
    {
        editorProgressVisible = true;
        editorProgressRunning = false;
        editorProgressTitle = title;
        editorProgressDetail = detail;
        editorProgressValue = 1f;
        editorProgressHideAfter = GLFW.GetTime() + 0.65;
        statusMessage = detail;
    }

    private void DrawEditorProgressOverlay()
    {
        if (!editorProgressVisible)
            return;

        double now = GLFW.GetTime();
        if (!editorProgressRunning && editorProgressHideAfter > 0.0 && now >= editorProgressHideAfter)
        {
            editorProgressVisible = false;
            return;
        }

        var viewport = ImGui.GetMainViewport();
        var bgMin = viewport.Pos;
        var bgMax = viewport.Pos + viewport.Size;
        var draw = ImGui.GetForegroundDrawList();
        draw.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new System.Numerics.Vector4(0f, 0f, 0f, 0.26f)));

        float width = Math.Clamp(viewport.Size.X * 0.48f, 440f, 680f);
        float height = 96f;
        var pos = viewport.Pos + new Vector2((viewport.Size.X - width) * 0.5f, Math.Max(72f, viewport.Size.Y * 0.24f));
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.92f, 0.92f, 0.92f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.10f, 0.10f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new System.Numerics.Vector4(0.14f, 0.55f, 0.95f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.76f, 0.76f, 0.76f, 1f));
        ImGui.Begin("##EditorProgressOverlay",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoResize);

        ImGui.TextUnformatted(editorProgressTitle);
        ImGui.Spacing();
        ImGui.ProgressBar(editorProgressRunning && editorProgressValue < 0.98f ? AnimatedProgress(now) : editorProgressValue, new Vector2(-1f, 18f), "");
        ImGui.Spacing();
        ImGui.TextUnformatted(editorProgressDetail);

        ImGui.End();
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
    }

    private float AnimatedProgress(double now)
    {
        float pulse = (float)((Math.Sin(now * 4.2) + 1.0) * 0.035);
        return Math.Clamp(editorProgressValue + pulse, 0.02f, 0.96f);
    }
}
