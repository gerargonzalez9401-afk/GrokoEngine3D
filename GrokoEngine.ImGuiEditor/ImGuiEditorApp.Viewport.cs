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
    private void DrawScenePanel(Vector2 size)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.075f, 0.090f, 0.110f, 1f));
        ImGui.BeginChild("##ScenePanel", size, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

        DrawViewportLayoutBar();
        if (viewportPanelLayout != ViewportPanelLayout.Tabs)
        {
            DrawSplitViewportPanels();
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            return;
        }

        // Pestañas Scene / Game (como Unity). La pestaña activa decide cámara y gizmos.
        if (ImGui.BeginTabBar("##sceneGameTabs", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("Scene")) { gameViewActive = false; ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Game")) { gameViewActive = true; ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        viewportMin = ImGui.GetWindowPos();
        viewportMax = viewportMin + ImGui.GetWindowSize();
        viewportPanelContentMin = ImGui.GetCursorScreenPos();
        viewportPanelContentSize = ImGui.GetContentRegionAvail();
        viewportPanelContentSize.X = Math.Max(1f, viewportPanelContentSize.X);
        viewportPanelContentSize.Y = Math.Max(1f, viewportPanelContentSize.Y);
        ComputeViewportImageRect(viewportPanelContentMin, viewportPanelContentSize, out viewportContentMin, out viewportContentSize);
        viewportReady = true;

        ImGui.GetWindowDrawList().AddRectFilled(viewportPanelContentMin, viewportPanelContentMin + viewportPanelContentSize, ImGui.GetColorU32(new System.Numerics.Vector4(0.015f, 0.017f, 0.020f, 1f)));
        ImGui.SetCursorScreenPos(viewportContentMin);
        ImGui.Image(sceneTarget.TextureId, viewportContentSize, new Vector2(0f, 1f), new Vector2(1f, 0f));
        // HUD del juego (Canvas / UI) dibujado sobre la imagen de la escena.
        RenderCanvasUI(ImGui.GetWindowDrawList(), viewportContentMin, viewportContentSize);
        HandleViewportAssetDrop();

        var drawList = ImGui.GetWindowDrawList();
        uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.08f, 0.09f, 1f));
        uint text = ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.75f, 0.78f, 0.95f));
        uint accent = ImGui.GetColorU32(new System.Numerics.Vector4(0.98f, 0.76f, 0.22f, 0.95f));

        drawList.AddRect(viewportContentMin, viewportContentMin + viewportContentSize, border, 0f, ImDrawFlags.None, 1.5f);
        if (isPlaying)
            drawList.AddRectFilled(viewportContentMin, viewportContentMin + new Vector2(viewportContentSize.X, 3f), ImGui.GetColorU32(new System.Numerics.Vector4(0.24f, 0.55f, 0.95f, 1f)));
        drawList.AddRectFilled(viewportContentMin, viewportContentMin + new Vector2(viewportContentSize.X, 28f), ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 0.88f)));
        drawList.AddText(viewportContentMin + new Vector2(10f, 7f), text, (isPlaying || gameViewActive) ? "Game" : "Scene");
        string hint = viewportContentSize.X < 520f ? "RMB+WASD  F" : "RMB+WASD   MMB pan   Alt+LMB orbit   F focus";
        drawList.AddText(viewportContentMin + new Vector2(62f, 7f), ImGui.GetColorU32(new System.Numerics.Vector4(0.45f, 0.48f, 0.50f, 0.95f)), hint);
        if (selected != null)
            drawList.AddText(viewportContentMin + new Vector2(56f, 38f), accent, selected.Name);
        if (selection.Selected.Count > 1)
            drawList.AddText(viewportContentMin + new Vector2(56f, 56f), ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.82f, 0.95f, 0.95f)), $"{selection.Selected.Count} selected");

        if (objects.Count == 0)
            drawList.AddText(
                viewportContentMin + new Vector2(Math.Max(12f, viewportContentSize.X * 0.5f - 74f), Math.Max(80f, viewportContentSize.Y * 0.5f - 12f)),
                text,
                "Scene is empty");

        DrawSelectionBoxOverlay(drawList);
        DrawViewportStatusToast(drawList);
        DrawAnimatorDebugOverlay(drawList);
        DrawViewportCameraIcons(drawList);

        DrawViewportResolutionOverlay();
        DrawViewportToolbarOverlay();
        DrawViewportQuickActions();
        DrawViewCubeGizmo(drawList);

        if (viewportGizmosVisible)
            DrawBoxColliderEditGizmo(drawList);
        if (viewportGizmosVisible && selected?.EditorId != colliderEditObjectId)
            DrawTransformGizmo(drawList);

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private void DrawViewportLayoutBar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3f, 0f));
        if (DrawViewportLayoutButton("Tabs", viewportPanelLayout == ViewportPanelLayout.Tabs))
            viewportPanelLayout = ViewportPanelLayout.Tabs;
        ImGui.SameLine();
        if (DrawViewportLayoutButton("Split H", viewportPanelLayout == ViewportPanelLayout.SplitHorizontal))
        {
            viewportPanelLayout = ViewportPanelLayout.SplitHorizontal;
            gameViewActive = false;
        }
        ImGui.SameLine();
        if (DrawViewportLayoutButton("Split V", viewportPanelLayout == ViewportPanelLayout.SplitVertical))
        {
            viewportPanelLayout = ViewportPanelLayout.SplitVertical;
            gameViewActive = false;
        }
        ImGui.SameLine();
        if (DrawViewportLayoutButton("Dock", viewportPanelLayout == ViewportPanelLayout.DockableWindows))
        {
            viewportPanelLayout = ViewportPanelLayout.DockableWindows;
            gameViewActive = false;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Scene + Game like Unity");
        ImGui.PopStyleVar(2);
    }

    private static bool DrawViewportLayoutButton(string label, bool active)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, active
            ? new System.Numerics.Vector4(0.22f, 0.42f, 0.66f, 1f)
            : new System.Numerics.Vector4(0.13f, 0.14f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.28f, 0.50f, 0.76f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.20f, 0.45f, 0.72f, 1f));
        bool clicked = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private void DrawSplitViewportPanels()
    {
        if (viewportPanelLayout == ViewportPanelLayout.DockableWindows)
        {
            var dockAvail = ImGui.GetContentRegionAvail();
            viewportPanelContentMin = ImGui.GetCursorScreenPos();
            viewportPanelContentSize = new Vector2(Math.Max(1f, dockAvail.X), Math.Max(1f, dockAvail.Y));
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(viewportPanelContentMin, viewportPanelContentMin + viewportPanelContentSize,
                ImGui.GetColorU32(new System.Numerics.Vector4(0.06f, 0.07f, 0.08f, 1f)));
            const string msg = "Dock mode: drag the Scene and Game window title bars like Unity.";
            var textSize = ImGui.CalcTextSize(msg);
            drawList.AddText(
                viewportPanelContentMin + new Vector2(Math.Max(12f, (viewportPanelContentSize.X - textSize.X) * 0.5f), Math.Max(24f, (viewportPanelContentSize.Y - textSize.Y) * 0.5f)),
                ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.76f, 0.80f, 0.92f)),
                msg);
            ImGui.Dummy(viewportPanelContentSize);
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        const float gap = 4f;
        gameViewActive = false;

        if (viewportPanelLayout == ViewportPanelLayout.SplitVertical)
        {
            float eachH = Math.Max(1f, (avail.Y - gap) * 0.5f);
            DrawSplitViewportPane("Scene", sceneTarget, new Vector2(avail.X, eachH), gamePanel: false);
            ImGui.Dummy(new Vector2(0f, gap));
            DrawSplitViewportPane("Game", gamePreviewTarget, new Vector2(avail.X, eachH), gamePanel: true);
            return;
        }

        float eachW = Math.Max(1f, (avail.X - gap) * 0.5f);
        DrawSplitViewportPane("Scene", sceneTarget, new Vector2(eachW, avail.Y), gamePanel: false);
        ImGui.SameLine(0f, gap);
        DrawSplitViewportPane("Game", gamePreviewTarget, new Vector2(eachW, avail.Y), gamePanel: true);
    }

    private void DrawSplitViewportPane(string title, SceneRenderTarget target, Vector2 size, bool gamePanel)
    {
        size.X = Math.Max(1f, size.X);
        size.Y = Math.Max(1f, size.Y);
        ImGui.BeginChild("##SplitViewport" + title, size, ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);

        var savedViewportMin = viewportMin;
        var savedViewportMax = viewportMax;
        var savedPanelMin = viewportPanelContentMin;
        var savedPanelSize = viewportPanelContentSize;
        var savedContentMin = viewportContentMin;
        var savedContentSize = viewportContentSize;
        bool savedReady = viewportReady;
        bool savedGameView = gameViewActive;
        gameViewActive = gamePanel;
        viewportMin = ImGui.GetWindowPos();
        viewportMax = viewportMin + ImGui.GetWindowSize();
        viewportPanelContentMin = ImGui.GetCursorScreenPos();
        viewportPanelContentSize = ImGui.GetContentRegionAvail();
        viewportPanelContentSize.X = Math.Max(1f, viewportPanelContentSize.X);
        viewportPanelContentSize.Y = Math.Max(1f, viewportPanelContentSize.Y);
        ComputeViewportImageRect(viewportPanelContentMin, viewportPanelContentSize, out viewportContentMin, out viewportContentSize);
        viewportReady = true;
        if (gamePanel)
            gamePreviewPanelContentSize = viewportPanelContentSize;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(viewportPanelContentMin, viewportPanelContentMin + viewportPanelContentSize, ImGui.GetColorU32(new System.Numerics.Vector4(0.015f, 0.017f, 0.020f, 1f)));
        ImGui.SetCursorScreenPos(viewportContentMin);
        ImGui.Image(target.TextureId, viewportContentSize, new Vector2(0f, 1f), new Vector2(1f, 0f));
        RenderCanvasUI(drawList, viewportContentMin, viewportContentSize);

        uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.08f, 0.09f, 1f));
        uint text = ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.75f, 0.78f, 0.95f));
        drawList.AddRect(viewportContentMin, viewportContentMin + viewportContentSize, border, 0f, ImDrawFlags.None, 1.5f);
        drawList.AddRectFilled(viewportContentMin, viewportContentMin + new Vector2(viewportContentSize.X, 28f), ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 0.88f)));
        drawList.AddText(viewportContentMin + new Vector2(10f, 7f), text, title);

        if (!gamePanel)
        {
            HandleViewportAssetDrop();
            DrawViewportToolbarOverlay();
            DrawViewportResolutionOverlay();
            DrawViewCubeGizmo(drawList);
            if (viewportGizmosVisible)
                DrawTransformGizmo(drawList);
        }

        ImGui.EndChild();
        if (gamePanel)
        {
            viewportMin = savedViewportMin;
            viewportMax = savedViewportMax;
            viewportPanelContentMin = savedPanelMin;
            viewportPanelContentSize = savedPanelSize;
            viewportContentMin = savedContentMin;
            viewportContentSize = savedContentSize;
            viewportReady = savedReady;
        }
        gameViewActive = savedGameView;
    }

    private void DrawDockableViewportWindows()
    {
        if (gameMode || viewportPanelLayout != ViewportPanelLayout.DockableWindows)
            return;

        ImGui.SetNextWindowSize(new Vector2(720f, 420f), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Scene###DockableSceneViewport", ImGuiWindowFlags.NoCollapse))
            DrawDockableViewportContent("Scene", sceneTarget, gamePanel: false);
        ImGui.End();

        ImGui.SetNextWindowSize(new Vector2(520f, 320f), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Game###DockableGameViewport", ImGuiWindowFlags.NoCollapse))
            DrawDockableViewportContent("Game", gamePreviewTarget, gamePanel: true);
        ImGui.End();
    }

    private void DrawDockableViewportContent(string title, SceneRenderTarget target, bool gamePanel)
    {
        var savedViewportMin = viewportMin;
        var savedViewportMax = viewportMax;
        var savedPanelMin = viewportPanelContentMin;
        var savedPanelSize = viewportPanelContentSize;
        var savedContentMin = viewportContentMin;
        var savedContentSize = viewportContentSize;
        bool savedReady = viewportReady;
        bool savedGameView = gameViewActive;

        gameViewActive = gamePanel;
        viewportMin = ImGui.GetWindowPos();
        viewportMax = viewportMin + ImGui.GetWindowSize();
        viewportPanelContentMin = ImGui.GetCursorScreenPos();
        viewportPanelContentSize = ImGui.GetContentRegionAvail();
        viewportPanelContentSize.X = Math.Max(1f, viewportPanelContentSize.X);
        viewportPanelContentSize.Y = Math.Max(1f, viewportPanelContentSize.Y);
        ComputeViewportImageRect(viewportPanelContentMin, viewportPanelContentSize, out viewportContentMin, out viewportContentSize);
        viewportReady = true;
        if (gamePanel)
            gamePreviewPanelContentSize = viewportPanelContentSize;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(viewportPanelContentMin, viewportPanelContentMin + viewportPanelContentSize, ImGui.GetColorU32(new System.Numerics.Vector4(0.015f, 0.017f, 0.020f, 1f)));
        ImGui.SetCursorScreenPos(viewportContentMin);
        ImGui.Image(target.TextureId, viewportContentSize, new Vector2(0f, 1f), new Vector2(1f, 0f));
        RenderCanvasUI(drawList, viewportContentMin, viewportContentSize);

        uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.08f, 0.09f, 1f));
        uint text = ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.75f, 0.78f, 0.95f));
        uint accent = ImGui.GetColorU32(new System.Numerics.Vector4(0.98f, 0.76f, 0.22f, 0.95f));
        drawList.AddRect(viewportContentMin, viewportContentMin + viewportContentSize, border, 0f, ImDrawFlags.None, 1.5f);
        drawList.AddRectFilled(viewportContentMin, viewportContentMin + new Vector2(viewportContentSize.X, 28f), ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 0.88f)));
        drawList.AddText(viewportContentMin + new Vector2(10f, 7f), text, title);

        if (!gamePanel)
        {
            string hint = viewportContentSize.X < 520f ? "RMB+WASD  F" : "RMB+WASD   MMB pan   Alt+LMB orbit   F focus";
            drawList.AddText(viewportContentMin + new Vector2(62f, 7f), ImGui.GetColorU32(new System.Numerics.Vector4(0.45f, 0.48f, 0.50f, 0.95f)), hint);
            if (selected != null)
                drawList.AddText(viewportContentMin + new Vector2(56f, 38f), accent, selected.Name);
            HandleViewportAssetDrop();
            DrawSelectionBoxOverlay(drawList);
            DrawViewportStatusToast(drawList);
            DrawAnimatorDebugOverlay(drawList);
            DrawViewportCameraIcons(drawList);
            DrawViewportResolutionOverlay();
            DrawViewportToolbarOverlay();
            DrawViewportQuickActions();
            DrawViewCubeGizmo(drawList);
            if (viewportGizmosVisible)
                DrawBoxColliderEditGizmo(drawList);
            if (viewportGizmosVisible && selected?.EditorId != colliderEditObjectId)
                DrawTransformGizmo(drawList);
        }
        else if (target.TextureId == 0)
        {
            drawList.AddText(viewportContentMin + new Vector2(12f, 38f), text, "Game preview waiting for render...");
        }

        if (gamePanel)
        {
            viewportMin = savedViewportMin;
            viewportMax = savedViewportMax;
            viewportPanelContentMin = savedPanelMin;
            viewportPanelContentSize = savedPanelSize;
            viewportContentMin = savedContentMin;
            viewportContentSize = savedContentSize;
            viewportReady = savedReady;
        }
        gameViewActive = savedGameView;
    }
    // Billboard 2D del icono de cámara (cine) sobre cada cámara de la escena. Siempre
    // visible (estilo Unity); se oculta en Game/Play o si los gizmos están desactivados.
    private void DrawViewportCameraIcons(ImDrawListPtr drawList)
    {
        if (isPlaying || gameViewActive || !viewportGizmosVisible) return;
        if (!iconAtlasReady || iconAtlasTexture == 0) return;
        if (!iconAtlasRegions.TryGetValue(EditorIcon.CameraGizmo, out var region)) return;

        var contentMax = viewportContentMin + viewportContentSize;
        const float size = 32f;

        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;
            if (!(obj.IsCamera || obj.GetComponent<Camera>() != null)) continue;
            if (!TryProject(obj.GlobalPosition, out var local)) continue;

            var center = viewportContentMin + local;
            // Descartar si cae fuera del viewport (deja margen para la barra superior).
            if (center.X < viewportContentMin.X || center.X > contentMax.X ||
                center.Y < viewportContentMin.Y + 30f || center.Y > contentMax.Y)
                continue;

            bool isSel = obj == selected;
            uint tint = ImGui.GetColorU32(isSel
                ? new System.Numerics.Vector4(1f, 0.78f, 0.18f, 1f)
                : new System.Numerics.Vector4(0.86f, 0.90f, 0.96f, 0.92f));

            var iconMin = center - new Vector2(size * 0.5f, size * 0.5f);
            drawList.AddImage((IntPtr)iconAtlasTexture, iconMin, iconMin + new Vector2(size, size), region.Uv0, region.Uv1, tint);
        }
    }

    private void ComputeViewportImageRect(Vector2 panelMin, Vector2 panelSize, out Vector2 imageMin, out Vector2 imageSize)
    {
        var preset = GetCurrentViewportResolutionPreset();
        if (preset.Free)
        {
            imageMin = panelMin;
            imageSize = panelSize;
            return;
        }

        float aspect = preset.Width / (float)Math.Max(1, preset.Height);
        float panelAspect = panelSize.X / Math.Max(1f, panelSize.Y);

        if (panelAspect > aspect)
        {
            imageSize = new Vector2(MathF.Floor(panelSize.Y * aspect), panelSize.Y);
            imageMin = panelMin + new Vector2(MathF.Floor((panelSize.X - imageSize.X) * 0.5f), 0f);
        }
        else
        {
            imageSize = new Vector2(panelSize.X, MathF.Floor(panelSize.X / aspect));
            imageMin = panelMin + new Vector2(0f, MathF.Floor((panelSize.Y - imageSize.Y) * 0.5f));
        }

        imageSize.X = Math.Max(1f, imageSize.X);
        imageSize.Y = Math.Max(1f, imageSize.Y);
    }

    // View gizmo estilo Unity (cubo de ejes, esquina superior derecha): clic en un eje para girar la cámara
    // a esa vista (front/top/side), y etiqueta Persp/Iso para alternar perspectiva/ortográfica.
    private void DrawViewCubeGizmo(ImDrawListPtr drawList)
    {
        viewGizmoMouseCaptured = false;
        if (isPlaying || gameViewActive) return;
        if (viewportContentSize.X < 220f || viewportContentSize.Y < 150f) return;

        float radius = 30f;
        var center = viewportContentMin + new Vector2(viewportContentSize.X - radius - 26f, 46f + radius);

        var front = camera.Front.Normalized();
        var right = Vector3.Cross(front, camera.Up).Normalized();
        var up = Vector3.Cross(right, front).Normalized();

        var mouse = ImGui.GetMousePos();
        bool overGizmo = (mouse - center).Length() <= radius + 16f && IsMouseInsideViewport(mouse.X, mouse.Y);

        uint colX = ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.32f, 0.36f, 1f));
        uint colY = ImGui.GetColorU32(new System.Numerics.Vector4(0.55f, 0.85f, 0.32f, 1f));
        uint colZ = ImGui.GetColorU32(new System.Numerics.Vector4(0.32f, 0.56f, 0.96f, 1f));
        uint dim = ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.11f, 0.13f, 1f));

        var dirs = new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1),
                           new Vector3(-1, 0, 0), new Vector3(0, -1, 0), new Vector3(0, 0, -1) };
        var cols = new[] { colX, colY, colZ, colX, colY, colZ };
        var labels = new[] { "X", "Y", "Z", "", "", "" };
        var positive = new[] { true, true, true, false, false, false };

        var pos2d = new Vector2[6];
        var depth = new float[6];
        for (int i = 0; i < 6; i++)
        {
            pos2d[i] = center + new Vector2(Vector3.Dot(dirs[i], right), -Vector3.Dot(dirs[i], up)) * radius;
            depth[i] = Vector3.Dot(dirs[i], front);
        }

        int hoverIdx = -1;
        if (overGizmo)
        {
            float best = 12f;
            for (int i = 0; i < 6; i++)
            {
                float dd = (mouse - pos2d[i]).Length();
                if (dd < best) { best = dd; hoverIdx = i; }
            }
        }

        drawList.AddCircleFilled(center, radius + 13f, ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.11f, 0.13f, overGizmo ? 0.6f : 0.32f)), 32);

        int[] order = { 0, 1, 2, 3, 4, 5 };
        Array.Sort(order, (a, b) => depth[b].CompareTo(depth[a])); // atrás → delante

        foreach (int i in order)
        {
            if (positive[i])
                drawList.AddLine(center, pos2d[i], cols[i], 2.2f);
            float r = positive[i] ? 9f : 6.5f;
            bool hov = i == hoverIdx;
            if (hov) r += 2f;
            if (positive[i] || hov)
            {
                drawList.AddCircleFilled(pos2d[i], r, cols[i], 22);
                if (!string.IsNullOrEmpty(labels[i]))
                    drawList.AddText(pos2d[i] - new Vector2(3.5f, 7f), 0xFF0A0A0A, labels[i]);
            }
            else
            {
                drawList.AddCircleFilled(pos2d[i], r, dim, 22);
                drawList.AddCircle(pos2d[i], r, cols[i], 22, 1.7f);
            }
        }

        // Etiqueta Persp / Iso (clic = alternar proyección).
        string projLabel = camera.Orthographic ? "Iso" : "Persp";
        var labelPos = center + new Vector2(-16f, radius + 8f);
        var labelSize = ImGui.CalcTextSize(projLabel);
        bool labelHover = IsMouseInsideViewport(mouse.X, mouse.Y) &&
            mouse.X >= labelPos.X - 4f && mouse.X <= labelPos.X + labelSize.X + 4f &&
            mouse.Y >= labelPos.Y - 2f && mouse.Y <= labelPos.Y + labelSize.Y + 2f;
        drawList.AddText(labelPos, labelHover ? 0xFFFFFFFF : 0xFFAEB2B6, projLabel);

        viewGizmoMouseCaptured = overGizmo || labelHover;

        if ((overGizmo || labelHover) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (labelHover) ToggleViewportOrthographic();
            else if (hoverIdx >= 0) SnapCameraToAxis(dirs[hoverIdx]);
        }
    }

    // Gira la cámara para mirar a lo largo del eje (la vista front/top/side), conservando el punto de enfoque.
    private void SnapCameraToAxis(Vector3 axis)
    {
        var focus = selected != null ? GetObjectCenter(selected) : EstimateSceneCenter();
        float distance = Math.Max(2f, Distance(camera.Position, focus));
        var look = new Vector3(-axis.X, -axis.Y, -axis.Z).Normalized();

        camera.Up = new Vector3(0f, 1f, 0f);
        if (MathF.Abs(look.Y) > 0.99f)
        {
            // Vista superior/inferior: pitch casi vertical (evita el caso degenerado) manteniendo Up=(0,1,0).
            camera.Yaw = 90f;
            camera.Pitch = look.Y < 0f ? -89.9f : 89.9f;
            camera.UpdateFront();
        }
        else
        {
            camera.SetLookDirection(look);
        }
        camera.Position = focus - camera.Front * distance;
        statusMessage = "View: " + AxisViewName(axis);
    }

    private static string AxisViewName(Vector3 axis)
    {
        if (axis.X > 0.5f) return "Right (+X)";
        if (axis.X < -0.5f) return "Left (-X)";
        if (axis.Y > 0.5f) return "Top (+Y)";
        if (axis.Y < -0.5f) return "Bottom (-Y)";
        if (axis.Z > 0.5f) return "Front (+Z)";
        return "Back (-Z)";
    }

    private void ToggleViewportOrthographic()
    {
        camera.Orthographic = !camera.Orthographic;
        if (camera.Orthographic)
        {
            var focus = selected != null ? GetObjectCenter(selected) : EstimateSceneCenter();
            camera.OrthoSize = Math.Clamp(Distance(camera.Position, focus) * 0.5f, 0.5f, 200f);
        }
        statusMessage = camera.Orthographic ? "Orthographic" : "Perspective";
    }

    private void DrawViewportResolutionOverlay()
    {
        if (viewportContentSize.X < 250f || viewportContentSize.Y < 34f)
            return;

        var preset = GetCurrentViewportResolutionPreset();
        string label = preset.Free ? "Free Aspect" : $"{preset.Width} x {preset.Height}";
        float width = Math.Min(176f, Math.Max(130f, viewportContentSize.X - 24f));
        var pos = viewportContentMin + new Vector2(Math.Max(8f, viewportContentSize.X - width - 8f), 4f);

        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.08f, 0.08f, 0.09f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new System.Numerics.Vector4(0.14f, 0.16f, 0.18f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new System.Numerics.Vector4(0.08f, 0.08f, 0.09f, 0.98f));

        if (ImGui.BeginCombo("##ViewportResolutionPreset", label))
        {
            for (int i = 0; i < viewportResolutionPresets.Count; i++)
            {
                var item = viewportResolutionPresets[i];
                if (i == 1 || item.Key.StartsWith("monitor:", StringComparison.Ordinal))
                    ImGui.Separator();

                bool selectedItem = item.Key == viewportResolutionPresetKey;
                if (ImGui.Selectable(item.Label, selectedItem))
                {
                    viewportResolutionPresetKey = item.Key;
                    statusMessage = item.Free
                        ? "Viewport resolution: Free Aspect"
                        : $"Viewport resolution: {item.Width}x{item.Height}";
                }

                if (selectedItem)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            var renderSize = GetViewportRenderSize();
            ImGui.SetTooltip($"Render {renderSize.Width}x{renderSize.Height}\nDisplay {(int)viewportContentSize.X}x{(int)viewportContentSize.Y}");
        }

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
    }

    private void DrawViewportStatusToast(ImDrawListPtr drawList)
    {
        double age = GLFW.GetTime() - lastStatusFlashTime;
        if (age > 2.2 || string.IsNullOrWhiteSpace(statusMessageValue))
            return;

        float alpha = (float)Math.Clamp(1.0 - Math.Max(0.0, age - 1.25) / 0.95, 0.0, 1.0);
        string text = statusMessageValue.Length > 54 ? statusMessageValue[..51] + "..." : statusMessageValue;
        var textSize = ImGui.CalcTextSize(text);
        var min = viewportContentMin + new Vector2(Math.Max(12f, viewportContentSize.X * 0.5f - textSize.X * 0.5f - 16f), 72f);
        var max = min + new Vector2(textSize.X + 32f, 28f);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0.05f, 0.07f, 0.09f, 0.82f * alpha)), 5f);
        var toastColor = lastStatusSeverity switch
        {
            ConsoleSeverity.Error => new System.Numerics.Vector4(0.95f, 0.34f, 0.30f, 0.82f * alpha),
            ConsoleSeverity.Warning => new System.Numerics.Vector4(0.95f, 0.70f, 0.26f, 0.82f * alpha),
            _ => new System.Numerics.Vector4(0.30f, 0.56f, 0.82f, 0.75f * alpha)
        };
        drawList.AddRect(min, max, ImGui.GetColorU32(toastColor), 5f);
        drawList.AddText(min + new Vector2(16f, 6f), ImGui.GetColorU32(new System.Numerics.Vector4(0.92f, 0.96f, 1f, alpha)), text);
    }

    private void DrawViewportToolbarOverlay()
    {
        var drawList = ImGui.GetWindowDrawList();
        if (viewportContentSize.X < 180f)
            return;

        bool compactModes = viewportContentSize.X < 480f;
        var cursor = viewportContentMin + new Vector2(8f, 34f);
        float maxX = viewportContentMin.X + viewportContentSize.X - 8f;

        DrawViewportToolbarPanel(drawList, ref cursor, viewportContentMin.X + 8f, maxX, new Vector2(122f, 30f), DrawSceneToolButtons);
        DrawViewportToolbarPanel(drawList, ref cursor, viewportContentMin.X + 8f, maxX, compactModes ? new Vector2(124f, 30f) : new Vector2(202f, 30f), () => DrawSceneModeButtons(compactModes));
        DrawViewportToolbarPanel(drawList, ref cursor, viewportContentMin.X + 8f, maxX, new Vector2(178f, 30f), DrawSceneUtilityButtons);
    }

    private static void DrawViewportToolbarPanel(ImDrawListPtr drawList, ref Vector2 cursor, float originX, float maxX, Vector2 size, Action drawContent)
    {
        if (cursor.X + size.X > maxX)
        {
            cursor.X = originX;
            cursor.Y += size.Y + 4f;
        }

        uint panelBg = ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.10f, 0.105f, 0.86f));
        uint panelBorder = ImGui.GetColorU32(new System.Numerics.Vector4(0.42f, 0.46f, 0.50f, 0.55f));
        var min = cursor;
        var max = min + size;
        drawList.AddRectFilled(min, max, panelBg);
        drawList.AddRect(min, max, panelBorder, 0f, ImDrawFlags.None, 1f);

        ImGui.SetCursorScreenPos(min + new Vector2(6f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.25f, 0.25f, 0.25f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.34f, 0.34f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.28f, 0.46f, 0.64f, 1f));
        drawContent();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);

        cursor.X += size.X + 6f;
    }

    private void DrawSceneToolButtons()
    {
        var buttonSize = new Vector2(26f, 22f);
        if (DrawIconButton(EditorIcon.Move, "Move tool", currentTool == TransformTool.Move, buttonSize))
            currentTool = TransformTool.Move;
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Rotate, "Rotate tool", currentTool == TransformTool.Rotate, buttonSize))
            currentTool = TransformTool.Rotate;
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Scale, "Scale tool", currentTool == TransformTool.Scale, buttonSize))
            currentTool = TransformTool.Scale;
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Frame, selected != null ? "Frame selected" : "Frame scene", false, buttonSize))
        {
            if (selected != null) FrameObject(selected);
            else FrameScene();
        }
    }

    private void DrawSceneModeButtons(bool compact)
    {
        var buttonSize = compact ? new Vector2(34f, 22f) : new Vector2(60f, 22f);
        if (DrawToggleButton(compact ? (viewportLocalSpace ? "L" : "W") : (viewportLocalSpace ? "Local" : "World"), viewportLocalSpace, buttonSize))
            viewportLocalSpace = !viewportLocalSpace;
        DrawTooltip(viewportLocalSpace ? "Local orientation" : "World orientation");
        ImGui.SameLine();
        if (DrawToggleButton(compact ? (viewportPivotCenter ? "C" : "P") : (viewportPivotCenter ? "Center" : "Pivot"), viewportPivotCenter, buttonSize))
            viewportPivotCenter = !viewportPivotCenter;
        DrawTooltip(viewportPivotCenter ? "Center pivot mode" : "Pivot mode");
        ImGui.SameLine();
        if (DrawToggleButton(compact ? (viewportShadedMode ? "S" : "W") : (viewportShadedMode ? "Shaded" : "Wire"), viewportShadedMode, buttonSize))
            viewportShadedMode = !viewportShadedMode;
        DrawTooltip(viewportShadedMode ? "Shaded display" : "Wire display");
    }

    private void DrawSceneUtilityButtons()
    {
        var buttonSize = new Vector2(26f, 22f);
        // Toggle 2D/3D (estilo Unity): vista ortográfica plana de frente.
        if (DrawToggleButton(camera.Orthographic ? "2D" : "3D", camera.Orthographic, new Vector2(40f, 22f)))
            SetCamera2D(!camera.Orthographic);
        DrawTooltip(camera.Orthographic ? "Cambiar a 3D (perspectiva)" : "Cambiar a 2D (vista plana ortográfica)");
        ImGui.SameLine();
        if (DrawToggleButton("Grid", sceneGridVisible, new Vector2(55f, 22f)))
            sceneGridVisible = !sceneGridVisible;
        DrawTooltip(sceneGridVisible ? "Hide scene grid" : "Show scene grid");
        ImGui.SameLine();
        if (DrawToggleButton("Snap", sceneGridSnapEnabled, new Vector2(55f, 22f)))
            sceneGridSnapEnabled = !sceneGridSnapEnabled;
        DrawTooltip(sceneGridSnapEnabled ? "Disable grid snapping" : "Enable grid snapping");
        ImGui.SameLine();
        if (DrawIconButton(viewportGizmosVisible ? EditorIcon.Visible : EditorIcon.Hidden, "Toggle gizmos", viewportGizmosVisible, buttonSize))
            viewportGizmosVisible = !viewportGizmosVisible;
        ImGui.SameLine();
        if (DrawToggleButton("Ani", viewportAnimatorDebugVisible, new Vector2(36f, 22f)))
            viewportAnimatorDebugVisible = !viewportAnimatorDebugVisible;
        DrawTooltip(viewportAnimatorDebugVisible ? "Hide Animator debug overlay" : "Show Animator debug overlay");
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Settings, "Viewport settings", false, buttonSize))
            showEditorSettings = true;
    }

    private void DrawAnimatorDebugOverlay(ImDrawListPtr drawList)
    {
        if (!viewportAnimatorDebugVisible || viewportContentSize.X < 300f || viewportContentSize.Y < 180f)
            return;

        var obj = FindAnimatorDebugObject();
        var animator = obj?.GetComponent<Animator>();
        if (obj == null || animator == null)
            return;

        DrawAnimatorMovementVectors(drawList, obj, animator);

        var info = animator.GetRuntimeInfo();
        var controller = obj.GetComponent<CharacterController>();
        var weights = animator.GetBlendWeights()
            .OrderByDescending(w => w.Weight)
            .Take(5)
            .ToArray();

        float panelWidth = Math.Min(310f, viewportContentSize.X - 24f);
        float rowHeight = 17f;
        float panelHeight = 110f + weights.Length * rowHeight + (controller != null ? rowHeight * 2f : 0f);
        var min = viewportContentMin + new Vector2(12f, Math.Max(72f, viewportContentSize.Y - panelHeight - 42f));
        var max = min + new Vector2(panelWidth, panelHeight);

        uint bg = ImGui.GetColorU32(new System.Numerics.Vector4(0.035f, 0.040f, 0.048f, 0.86f));
        uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.24f, 0.55f, 0.78f, 0.80f));
        uint title = ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.92f, 1f, 1f));
        uint text = ImGui.GetColorU32(new System.Numerics.Vector4(0.82f, 0.84f, 0.86f, 0.96f));
        uint muted = ImGui.GetColorU32(new System.Numerics.Vector4(0.55f, 0.58f, 0.62f, 0.95f));
        uint barBg = ImGui.GetColorU32(new System.Numerics.Vector4(0.12f, 0.13f, 0.15f, 0.96f));
        uint barFg = ImGui.GetColorU32(new System.Numerics.Vector4(0.25f, 0.74f, 0.94f, 0.92f));

        drawList.AddRectFilled(min, max, bg, 5f);
        drawList.AddRect(min, max, border, 5f, ImDrawFlags.None, 1.25f);
        drawList.AddText(min + new Vector2(10f, 8f), title, "Animator Debug");
        drawList.AddText(min + new Vector2(panelWidth - 64f, 8f), info.IsPlaying ? title : muted, info.IsPlaying ? "PLAY" : "PAUSE");

        float y = min.Y + 30f;
        drawList.AddText(new Vector2(min.X + 10f, y), text, obj.Name);
        y += rowHeight;
        drawList.AddText(new Vector2(min.X + 10f, y), muted, $"{info.StateName} / {info.MotionType}");
        y += rowHeight;
        drawList.AddText(new Vector2(min.X + 10f, y), muted, $"VelX {animator.GetFloat("VelX"):0.00}   VelY {animator.GetFloat("VelY"):0.00}   T {info.NormalizedTime:0.00}");
        y += rowHeight + 4f;

        drawList.AddText(new Vector2(min.X + 10f, y), muted, $"Root Motion {(animator.ApplyRootMotion ? "On" : "Off")}   Culling {animator.CullingMode}");
        y += rowHeight;
        if (controller != null)
        {
            drawList.AddText(new Vector2(min.X + 10f, y), muted, $"Grounded {(controller.IsGrounded ? "Yes" : "No")}   Flags {controller.CollisionFlags}");
            y += rowHeight;
            drawList.AddText(new Vector2(min.X + 10f, y), muted, $"Move {HorizontalLength(controller.LastMoveDelta):0.00}   Vy {controller.Velocity.Y:0.00}");
            y += rowHeight;
        }

        if (weights.Length == 0)
        {
            drawList.AddText(new Vector2(min.X + 10f, y), muted, string.IsNullOrWhiteSpace(info.ClipName) ? "No active motion" : info.ClipName);
            return;
        }

        foreach (var weight in weights)
        {
            string label = weight.DisplayName.Length > 20 ? weight.DisplayName[..17] + "..." : weight.DisplayName;
            drawList.AddText(new Vector2(min.X + 10f, y), text, label);
            drawList.AddText(new Vector2(min.X + panelWidth - 44f, y), muted, weight.Weight.ToString("0.00", CultureInfo.InvariantCulture));

            var barMin = new Vector2(min.X + 108f, y + 4f);
            var barMax = new Vector2(min.X + panelWidth - 52f, y + 12f);
            drawList.AddRectFilled(barMin, barMax, barBg, 3f);
            drawList.AddRectFilled(barMin, new Vector2(barMin.X + (barMax.X - barMin.X) * Math.Clamp(weight.Weight, 0f, 1f), barMax.Y), barFg, 3f);
            y += rowHeight;
        }
    }

    private void DrawAnimatorMovementVectors(ImDrawListPtr drawList, GameObject obj, Animator animator)
    {
        var controller = obj.GetComponent<CharacterController>();
        var origin = GetObjectCenter(obj);
        float length = Math.Max(0.65f, Distance(camera.Position, origin) * 0.085f);

        var inputDir = NormalizeSafe(new Vector3(animator.GetFloat("VelX"), 0f, animator.GetFloat("VelY")));
        var facingDir = GetObjectForward(obj);
        var moveDir = controller != null ? NormalizeSafe(new Vector3(controller.LastMoveDelta.X, 0f, controller.LastMoveDelta.Z)) : Vector3.Zero;

        DrawWorldArrow(drawList, origin, inputDir * length, new System.Numerics.Vector4(0.28f, 0.60f, 1f, 0.95f), "Input");
        DrawWorldArrow(drawList, origin, facingDir * length * 1.1f, new System.Numerics.Vector4(0.34f, 0.95f, 0.48f, 0.95f), "Face");
        if (controller != null && HorizontalLength(controller.LastMoveDelta) > 0.0001f)
            DrawWorldArrow(drawList, origin, moveDir * length * 1.2f, new System.Numerics.Vector4(1f, 0.62f, 0.22f, 0.95f), "Move");
    }

    private void DrawWorldArrow(ImDrawListPtr drawList, Vector3 origin, Vector3 delta, System.Numerics.Vector4 color, string label)
    {
        if (HorizontalLength(delta) <= 0.0001f)
            return;

        var end = origin + delta;
        if (!TryProject(origin, out var originLocal) || !TryProject(end, out var endLocal))
            return;

        var start = viewportContentMin + originLocal;
        var finish = viewportContentMin + endLocal;
        uint col = ImGui.GetColorU32(color);
        drawList.AddLine(start, finish, col, 2.2f);

        var dir = finish - start;
        float len = dir.Length();
        if (len > 1f)
        {
            dir /= len;
            var side = new Vector2(-dir.Y, dir.X);
            drawList.AddTriangleFilled(finish, finish - dir * 10f + side * 4f, finish - dir * 10f - side * 4f, col);
            drawList.AddText(finish + new Vector2(5f, -12f), col, label);
        }
    }

    private static Vector3 GetObjectForward(GameObject obj)
    {
        float yaw = MathHelper.DegreesToRadians(obj.RotY);
        return NormalizeSafe(new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw)));
    }

    private static float HorizontalLength(Vector3 value) =>
        MathF.Sqrt(value.X * value.X + value.Z * value.Z);

    private GameObject? FindAnimatorDebugObject()
    {
        if (selected?.GetComponent<Animator>() != null)
            return selected;

        foreach (var obj in selection.Selected)
            if (obj.GetComponent<Animator>() != null)
                return obj;

        foreach (var obj in objects)
        {
            var found = FindAnimatorDebugObjectRecursive(obj);
            if (found != null)
                return found;
        }

        return null;
    }

    private static GameObject? FindAnimatorDebugObjectRecursive(GameObject obj)
    {
        if (obj.GetComponent<Animator>() != null)
            return obj;

        foreach (var child in obj.Children)
        {
            var found = FindAnimatorDebugObjectRecursive(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private void DrawViewportQuickActions()
    {
        ImGui.SetCursorScreenPos(viewportContentMin + new Vector2(10f, viewportContentSize.Y - 34f));
        if (viewportContentSize.X < 360f)
            return;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, 0f));
        if (DrawIconButton(EditorIcon.Cube, "Create cube", false, new Vector2(30f, 24f)))
            CreateCube();
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Plane, "Create plane", false, new Vector2(30f, 24f)))
            CreatePlane();
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Frame, "Frame scene selection", false, new Vector2(30f, 24f)))
            FrameScene();
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Settings, "Repair extreme transforms and frame scene", false, new Vector2(30f, 24f)))
        {
            int fixedCount = SanitizeExtremeTransforms();
            FrameScene();
            statusMessage = fixedCount == 0 ? "Scene already looks sane" : $"Fixed {fixedCount} extreme transform(s)";
        }
        ImGui.PopStyleVar();
    }

    private void HandleViewportAssetDrop()
    {
        if (isPlaying)
            return;

        if (!ImGui.BeginDragDropTarget())
        {
            if (!MouseState.IsButtonDown(GlfwMouseButton.Left))
            {
                viewportAssetDropArmed = false;
                ClearMaterialPreview();
            }
            return;
        }

        bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
        if (MouseState.IsButtonDown(GlfwMouseButton.Left))
        {
            viewportAssetDropArmed = true;

            if (draggingAssetPath != null && MaterialAsset.IsMaterialPath(draggingAssetPath) && File.Exists(draggingAssetPath) && IsMouseInsideViewport(MouseState.X, MouseState.Y))
                UpdateMaterialPreview(draggingAssetPath, new Vector2(MouseState.X - viewportContentMin.X, MouseState.Y - viewportContentMin.Y));
            else
                ClearMaterialPreview();
        }
        else if (delivered && viewportAssetDropArmed && draggingAssetPath != null && File.Exists(draggingAssetPath) && IsMouseInsideViewport(MouseState.X, MouseState.Y))
        {
            DropAssetIntoViewport(draggingAssetPath, new Vector2(MouseState.X - viewportContentMin.X, MouseState.Y - viewportContentMin.Y));
            draggingAssetPath = null;
            viewportAssetDropArmed = false;
            ClearMaterialPreview();
        }
        else
        {
            ClearMaterialPreview();
        }

        ImGui.EndDragDropTarget();
    }

    // Vista previa "estilo Unity": al pasar un .mat por encima de un objeto/sub-malla sin soltar,
    // se muestra ese material temporalmente en esa parte (sin modificar la escena todavía).
    private void UpdateMaterialPreview(string materialPath, Vector2 localMouse)
    {
        double now = GLFW.GetTime();
        if (CanReuseMaterialPreview(materialPath, now))
        {
            ApplyCachedMaterialPreview(materialPath);
            return;
        }

        materialPreviewNextPickTime = now + 0.12;
        materialPreviewCachePath = materialPath;

        var target = PickObjectAt(localMouse.X, localMouse.Y);
        if (target == null || target.GetComponent<MeshFilter>() == null)
        {
            materialPreviewCacheObjectId = null;
            materialPreviewCacheSubmeshIndex = -1;
            sceneRenderer.PreviewMaterialObjectId = null;
            sceneRenderer.PreviewMaterialSubmeshIndex = -1;
            sceneRenderer.PreviewMaterialAssetPath = null;
            return;
        }

        int submeshIndex = -1;
        if (BuildCameraRay(localMouse.X, localMouse.Y, out var rayOrigin, out var rayDir))
            submeshIndex = sceneRenderer.PickMeshSubmesh(target, ToTk(rayOrigin), ToTk(rayDir)) ?? -1;

        materialPreviewCacheObjectId = target.EditorId;
        materialPreviewCacheSubmeshIndex = submeshIndex;
        sceneRenderer.PreviewMaterialObjectId = target.EditorId;
        sceneRenderer.PreviewMaterialSubmeshIndex = submeshIndex;
        sceneRenderer.PreviewMaterialAssetPath = materialPath;
    }

    private bool CanReuseMaterialPreview(string materialPath, double now) =>
        now < materialPreviewNextPickTime &&
        string.Equals(materialPreviewCachePath, materialPath, StringComparison.OrdinalIgnoreCase);

    private void ApplyCachedMaterialPreview(string materialPath)
    {
        if (materialPreviewCacheObjectId == null)
        {
            sceneRenderer.PreviewMaterialObjectId = null;
            sceneRenderer.PreviewMaterialSubmeshIndex = -1;
            sceneRenderer.PreviewMaterialAssetPath = null;
            return;
        }

        sceneRenderer.PreviewMaterialObjectId = materialPreviewCacheObjectId;
        sceneRenderer.PreviewMaterialSubmeshIndex = materialPreviewCacheSubmeshIndex;
        sceneRenderer.PreviewMaterialAssetPath = materialPath;
    }

    private void ClearMaterialPreview()
    {
        materialPreviewCachePath = null;
        materialPreviewCacheObjectId = null;
        materialPreviewCacheSubmeshIndex = -1;
        materialPreviewNextPickTime = 0.0;
        sceneRenderer.PreviewMaterialObjectId = null;
        sceneRenderer.PreviewMaterialSubmeshIndex = -1;
        sceneRenderer.PreviewMaterialAssetPath = null;
    }

    private static bool AcceptDragDropOnRelease(string payloadType)
    {
        ImGui.AcceptDragDropPayload(payloadType);
        return ImGui.IsMouseReleased(ImGuiMouseButton.Left);
    }

    private void DrawToolButton(string label, TransformTool tool)
    {
        bool active = currentTool == tool;
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.18f, 0.38f, 0.60f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.24f, 0.48f, 0.74f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.28f, 0.56f, 0.84f, 1f));
        }

        if (ImGui.Button(label, new Vector2(68f, 24f)))
            currentTool = tool;

        if (active)
            ImGui.PopStyleColor(3);
    }

    private static bool DrawIconButton(EditorIcon icon, string tooltip, bool active, Vector2 size)
    {
        ImGui.PushID(tooltip);
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiAccent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiAccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiAccentActive);
        }

        bool clicked = ImGui.Button("##iconButton", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        DrawAtlasIconOrFallback(ImGui.GetWindowDrawList(), icon, min + new Vector2(5f, 5f), Math.Min(size.X, size.Y) - 10f, ImGui.GetColorU32(active ? UiIconActive : UiIcon));
        DrawTooltip(tooltip);

        if (active)
            ImGui.PopStyleColor(3);
        ImGui.PopID();
        return clicked;
    }

    private static bool DrawInlineIconToggle(EditorIcon icon, string tooltip, bool active)
    {
        bool clicked = DrawIconButton(icon, tooltip, active, new Vector2(22f, 22f));
        return clicked;
    }

    private static void DrawInlineIcon(EditorIcon icon, string tooltip, float size = 18f)
    {
        var min = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##inlineIcon", new Vector2(size, size));
        DrawAtlasIconOrFallback(ImGui.GetWindowDrawList(), icon, min + new Vector2(1f, 1f), size - 2f, ImGui.GetColorU32(UiIcon));
        DrawTooltip(tooltip);
    }

    // Icono de cubo 3D (plateado con acentos azules) para los objetos de la jerarquía.
    private static void DrawInlineCubeIcon(string tooltip, float size = 18f)
    {
        var min = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##cubeIcon", new Vector2(size, size));
        DrawCubeHierarchyIcon(ImGui.GetWindowDrawList(), min + new Vector2(1f, 1f), size - 2f);
        DrawTooltip(tooltip);
    }

    private static void DrawCubeHierarchyIcon(ImDrawListPtr drawList, Vector2 min, float size)
    {
        Vector2 P(float fx, float fy) => min + new Vector2(size * fx, size * fy);

        // Vértices de un cubo isométrico (un poco más estrecho para que respire).
        var T = P(0.50f, 0.08f);   // back-top
        var Rt = P(0.90f, 0.30f);  // right-top
        var Lt = P(0.10f, 0.30f);  // left-top
        var C = P(0.50f, 0.52f);   // front-top (centro)
        var Lb = P(0.10f, 0.70f);  // left-bottom
        var Rb = P(0.90f, 0.70f);  // right-bottom
        var Bb = P(0.50f, 0.92f);  // front-bottom

        // Cubo de "cristal" azul: cara superior clara, laterales azules con un triángulo
        // inferior más oscuro (profundidad), y aristas blancas en "Y" desde el centro.
        uint topFill = ImGui.GetColorU32(new System.Numerics.Vector4(0.87f, 0.93f, 0.99f, 1f));
        uint leftFill = ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.84f, 0.98f, 1f));
        uint rightFill = ImGui.GetColorU32(new System.Numerics.Vector4(0.61f, 0.78f, 0.96f, 1f));
        uint leftDark = ImGui.GetColorU32(new System.Numerics.Vector4(0.64f, 0.79f, 0.97f, 1f));
        uint rightDark = ImGui.GetColorU32(new System.Numerics.Vector4(0.52f, 0.71f, 0.94f, 1f));
        uint white = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.95f));
        uint whiteSoft = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.55f));

        // Caras.
        drawList.AddQuadFilled(Lt, C, Bb, Lb, leftFill);
        drawList.AddQuadFilled(C, Rt, Rb, Bb, rightFill);
        // Triángulos inferiores más oscuros (degradado de cristal).
        drawList.AddTriangleFilled(C, Bb, Lb, leftDark);
        drawList.AddTriangleFilled(C, Rb, Bb, rightDark);
        // Cara superior encima.
        drawList.AddQuadFilled(T, Rt, C, Lt, topFill);

        // Aristas blancas: "Y" desde el centro + brillo suave en el borde superior.
        drawList.AddLine(C, Lt, white, 1.6f);
        drawList.AddLine(C, Rt, white, 1.6f);
        drawList.AddLine(C, Bb, white, 1.6f);
        drawList.AddLine(T, Lt, whiteSoft, 1.2f);
        drawList.AddLine(T, Rt, whiteSoft, 1.2f);
    }

    private static void DrawAtlasIconOrFallback(ImDrawListPtr drawList, EditorIcon icon, Vector2 min, float size, uint color)
    {
        if (icon == EditorIcon.Transform)
        {
            DrawIcon(drawList, icon, min, size, color);
            return;
        }

        var app = currentDrawingApp;
        if (app != null && app.iconAtlasReady && app.iconAtlasTexture != 0 && app.iconAtlasRegions.TryGetValue(icon, out var region))
        {
            drawList.AddImage((IntPtr)app.iconAtlasTexture, min, min + new Vector2(size, size), region.Uv0, region.Uv1, color);
            return;
        }

        DrawIcon(drawList, icon, min, size, color);
    }

    private static void DrawIcon(ImDrawListPtr drawList, EditorIcon icon, Vector2 min, float size, uint color)
    {
        var max = min + new Vector2(size, size);
        var center = min + new Vector2(size * 0.5f, size * 0.5f);
        uint dim = ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.09f, 0.10f, 1f));

        switch (icon)
        {
            case EditorIcon.Transform:
                DrawTransformIcon(drawList, min, size);
                break;
            case EditorIcon.Move:
                drawList.AddLine(center, min + new Vector2(size, size * 0.5f), color, 1.8f);
                drawList.AddLine(center, min + new Vector2(size * 0.5f, 0f), color, 1.8f);
                drawList.AddLine(center, min + new Vector2(size * 0.18f, size * 0.82f), color, 1.8f);
                drawList.AddTriangleFilled(min + new Vector2(size, size * 0.5f), min + new Vector2(size * 0.78f, size * 0.38f), min + new Vector2(size * 0.78f, size * 0.62f), color);
                drawList.AddTriangleFilled(min + new Vector2(size * 0.5f, 0f), min + new Vector2(size * 0.38f, size * 0.22f), min + new Vector2(size * 0.62f, size * 0.22f), color);
                break;
            case EditorIcon.Rotate:
                drawList.AddCircle(center, size * 0.38f, color, 32, 1.8f);
                drawList.AddTriangleFilled(min + new Vector2(size * 0.80f, size * 0.28f), min + new Vector2(size * 0.95f, size * 0.30f), min + new Vector2(size * 0.84f, size * 0.45f), color);
                break;
            case EditorIcon.Scale:
                drawList.AddRect(min + new Vector2(size * 0.18f, size * 0.18f), max - new Vector2(size * 0.18f, size * 0.18f), color, 1f, ImDrawFlags.None, 1.8f);
                drawList.AddRectFilled(max - new Vector2(size * 0.32f, size * 0.32f), max - new Vector2(size * 0.10f, size * 0.10f), color, 1f);
                break;
            case EditorIcon.Play:
                drawList.AddTriangleFilled(min + new Vector2(size * 0.34f, size * 0.22f), min + new Vector2(size * 0.34f, size * 0.78f), min + new Vector2(size * 0.78f, size * 0.50f), color);
                break;
            case EditorIcon.Stop:
                drawList.AddRectFilled(min + new Vector2(size * 0.28f, size * 0.28f), max - new Vector2(size * 0.28f, size * 0.28f), color, 1f);
                break;
            case EditorIcon.Pause:
                drawList.AddRectFilled(min + new Vector2(size * 0.28f, size * 0.22f), min + new Vector2(size * 0.42f, size * 0.78f), color, 1f);
                drawList.AddRectFilled(min + new Vector2(size * 0.58f, size * 0.22f), min + new Vector2(size * 0.72f, size * 0.78f), color, 1f);
                break;
            case EditorIcon.Step:
                drawList.AddTriangleFilled(min + new Vector2(size * 0.20f, size * 0.24f), min + new Vector2(size * 0.20f, size * 0.76f), min + new Vector2(size * 0.58f, size * 0.50f), color);
                drawList.AddRectFilled(min + new Vector2(size * 0.68f, size * 0.24f), min + new Vector2(size * 0.78f, size * 0.76f), color, 1f);
                break;
            case EditorIcon.Camera:
                drawList.AddRectFilled(min + new Vector2(size * 0.16f, size * 0.34f), min + new Vector2(size * 0.64f, size * 0.70f), color, 2f);
                drawList.AddTriangleFilled(min + new Vector2(size * 0.64f, size * 0.42f), min + new Vector2(size * 0.92f, size * 0.30f), min + new Vector2(size * 0.92f, size * 0.82f), color);
                break;
            case EditorIcon.Light:
                drawList.AddCircleFilled(center, size * 0.20f, color);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI * 0.25f;
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    drawList.AddLine(center + d * size * 0.30f, center + d * size * 0.46f, color, 1.4f);
                }
                break;
            case EditorIcon.Mesh:
                drawList.AddRect(min + new Vector2(size * 0.18f, size * 0.22f), max - new Vector2(size * 0.18f, size * 0.18f), color, 2f, ImDrawFlags.None, 1.6f);
                drawList.AddLine(min + new Vector2(size * 0.18f, size * 0.22f), center, color, 1.2f);
                drawList.AddLine(max - new Vector2(size * 0.18f, size * 0.18f), center, color, 1.2f);
                break;
            case EditorIcon.Prefab:
                drawList.AddCircleFilled(center, size * 0.36f, dim);
                drawList.AddCircle(center, size * 0.36f, color, 6, 2f);
                break;
            case EditorIcon.Script:
                drawList.AddRect(min + new Vector2(size * 0.24f, size * 0.14f), max - new Vector2(size * 0.20f, size * 0.12f), color, 2f, ImDrawFlags.None, 1.4f);
                drawList.AddLine(min + new Vector2(size * 0.34f, size * 0.44f), min + new Vector2(size * 0.66f, size * 0.44f), color, 1.2f);
                drawList.AddLine(min + new Vector2(size * 0.34f, size * 0.60f), min + new Vector2(size * 0.58f, size * 0.60f), color, 1.2f);
                break;
            case EditorIcon.Folder:
                drawList.AddRectFilled(min + new Vector2(size * 0.08f, size * 0.32f), max - new Vector2(size * 0.06f, size * 0.12f), color, 2f);
                drawList.AddRectFilled(min + new Vector2(size * 0.14f, size * 0.18f), min + new Vector2(size * 0.56f, size * 0.36f), color, 2f);
                break;
            case EditorIcon.Visible:
                drawList.AddCircle(center, size * 0.34f, color, 24, 1.5f);
                drawList.AddCircleFilled(center, size * 0.11f, color);
                break;
            case EditorIcon.Hidden:
                drawList.AddLine(min + new Vector2(size * 0.18f, size * 0.82f), max - new Vector2(size * 0.18f, size * 0.18f), color, 1.8f);
                drawList.AddCircle(center, size * 0.32f, color, 24, 1.2f);
                break;
            case EditorIcon.Lock:
                drawList.AddRectFilled(min + new Vector2(size * 0.24f, size * 0.44f), max - new Vector2(size * 0.24f, size * 0.14f), color, 2f);
                drawList.AddCircle(min + new Vector2(size * 0.50f, size * 0.44f), size * 0.22f, color, 18, 1.6f);
                break;
            case EditorIcon.Unlock:
                drawList.AddRectFilled(min + new Vector2(size * 0.24f, size * 0.46f), max - new Vector2(size * 0.24f, size * 0.14f), color, 2f);
                drawList.AddCircle(min + new Vector2(size * 0.62f, size * 0.42f), size * 0.22f, color, 18, 1.4f);
                break;
            case EditorIcon.Console:
                drawList.AddRect(min + new Vector2(size * 0.14f, size * 0.18f), max - new Vector2(size * 0.14f, size * 0.18f), color, 2f, ImDrawFlags.None, 1.5f);
                drawList.AddLine(min + new Vector2(size * 0.26f, size * 0.42f), min + new Vector2(size * 0.42f, size * 0.52f), color, 1.5f);
                drawList.AddLine(min + new Vector2(size * 0.42f, size * 0.52f), min + new Vector2(size * 0.26f, size * 0.62f), color, 1.5f);
                drawList.AddLine(min + new Vector2(size * 0.52f, size * 0.64f), min + new Vector2(size * 0.76f, size * 0.64f), color, 1.5f);
                break;
            case EditorIcon.Asset:
                drawList.AddRectFilled(min + new Vector2(size * 0.22f, size * 0.12f), max - new Vector2(size * 0.18f, size * 0.12f), color, 2f);
                drawList.AddTriangleFilled(max - new Vector2(size * 0.34f, size * 0.88f), max - new Vector2(size * 0.18f, size * 0.72f), max - new Vector2(size * 0.34f, size * 0.72f), dim);
                break;
            case EditorIcon.Cube:
                DrawMeshLikeIcon(drawList, min, size, color);
                break;
            case EditorIcon.Plane:
                drawList.AddQuad(min + new Vector2(size * .18f, size * .62f), min + new Vector2(size * .50f, size * .36f), min + new Vector2(size * .84f, size * .62f), min + new Vector2(size * .50f, size * .84f), color, 1.8f);
                break;
            case EditorIcon.Frame:
                drawList.AddRect(min + new Vector2(size * .18f, size * .18f), max - new Vector2(size * .18f, size * .18f), color, 1f, ImDrawFlags.None, 1.8f);
                drawList.AddCircle(center, size * .16f, color, 16, 1.6f);
                break;
            case EditorIcon.Settings:
                drawList.AddCircle(center, size * .28f, color, 16, 1.8f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI * .25f;
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    drawList.AddLine(center + d * size * .34f, center + d * size * .46f, color, 1.6f);
                }
                break;
        }
    }

    private static void DrawTransformIcon(ImDrawListPtr drawList, Vector2 min, float size)
    {
        var origin = min + new Vector2(size * 0.54f, size * 0.58f);
        var yEnd = min + new Vector2(size * 0.54f, size * 0.16f);
        var xEnd = min + new Vector2(size * 0.86f, size * 0.78f);
        var zEnd = min + new Vector2(size * 0.18f, size * 0.78f);
        float thickness = Math.Max(2.2f, size * 0.16f);
        float rounding = thickness * 0.45f;

        uint green = ImGui.GetColorU32(new System.Numerics.Vector4(0.04f, 0.95f, 0.22f, 1f));
        uint red = ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.06f, 0.24f, 1f));
        uint blue = ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.10f, 0.95f, 1f));
        uint joint = ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.12f, 0.14f, 0.70f));

        drawList.AddLine(origin, zEnd, blue, thickness);
        drawList.AddLine(origin, xEnd, red, thickness);
        drawList.AddLine(origin, yEnd, green, thickness);
        drawList.AddCircleFilled(origin, rounding, joint, 16);
        drawList.AddCircleFilled(yEnd, rounding, green, 16);
        drawList.AddCircleFilled(xEnd, rounding, red, 16);
        drawList.AddCircleFilled(zEnd, rounding, blue, 16);
    }

    private static void DrawMeshLikeIcon(ImDrawListPtr drawList, Vector2 min, float size, uint color)
    {
        var c = min + new Vector2(size * .5f, size * .52f);
        var p1 = c + new Vector2(-size * .28f, -size * .18f);
        var p2 = c + new Vector2(0f, -size * .34f);
        var p3 = c + new Vector2(size * .28f, -size * .18f);
        var p4 = c + new Vector2(size * .28f, size * .22f);
        var p5 = c + new Vector2(0f, size * .36f);
        var p6 = c + new Vector2(-size * .28f, size * .22f);
        drawList.AddLine(p1, p2, color, 1.5f);
        drawList.AddLine(p2, p3, color, 1.5f);
        drawList.AddLine(p3, p4, color, 1.5f);
        drawList.AddLine(p4, p5, color, 1.5f);
        drawList.AddLine(p5, p6, color, 1.5f);
        drawList.AddLine(p6, p1, color, 1.5f);
        drawList.AddLine(p2, c, color, 1.1f);
        drawList.AddLine(c, p4, color, 1.1f);
        drawList.AddLine(c, p6, color, 1.1f);
    }

    private static bool DrawToggleButton(string label, bool active, Vector2 size)
    {
        var app = currentDrawingApp;
        float rounding = app?.guiButtonRounding ?? 3f;
        float buttonHeight = app?.guiButtonHeight ?? size.Y;
        size.Y = size.Y <= 0f ? buttonHeight : Math.Max(size.Y, buttonHeight);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, rounding);
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.18f, 0.38f, 0.60f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.24f, 0.48f, 0.74f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.28f, 0.56f, 0.84f, 1f));
        }

        bool clicked = ImGui.Button(label, size);
        RegisterGuiElement(GuiStyleClass.Button, label);

        if (active)
            ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        return clicked && !(app?.guiInspectMode ?? false);
    }

    private static bool DrawTinyToggle(string label, bool active, float width)
    {
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.18f, 0.30f, 0.42f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.24f, 0.40f, 0.56f, 1f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.13f, 0.13f, 0.14f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.22f, 0.22f, 0.24f, 1f));
        }

        bool clicked = ImGui.Button(label, new Vector2(width, 20f));
        ImGui.PopStyleColor(2);
        return clicked;
    }

    private static void DrawTooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    private static bool GetObjectEditorBool(Dictionary<string, bool> values, string id, bool fallback) =>
        values.TryGetValue(id, out bool value) ? value : fallback;

    private static string GetObjectEditorString(Dictionary<string, string> values, string id, string fallback) =>
        values.TryGetValue(id, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static void DrawPanelHeader(string title, string? meta = null, Action? trailing = null, float trailingWidth = 20f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        float height = 22f;
        var headerMin = min - new Vector2(4f, 4f);
        var headerMax = min + new Vector2(width + 4f, height);
        drawList.AddRectFilled(headerMin, headerMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.12f, 0.12f, 0.12f, 1f)));
        drawList.AddLine(headerMin + new Vector2(0f, height + 4f), headerMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.08f, 0.08f, 1f)));

        ImGui.PushStyleColor(ImGuiCol.Text, UiText);
        ImGui.TextUnformatted(title);
        RegisterGuiElement(GuiStyleClass.Panel, title);
        ImGui.PopStyleColor();
        if (!string.IsNullOrWhiteSpace(meta))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(meta);
        }
        if (trailing != null)
        {
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 6f, width - trailingWidth));
            trailing();
        }
        ImGui.Dummy(new Vector2(0f, 2f));
    }

    private void DrawTransformGizmo(ImDrawListPtr drawList)
    {
        if (selected == null || !TryProject(GetObjectCenter(selected), out var originLocal))
            return;

        var originScreen = viewportContentMin + originLocal;
        float axisLength = Math.Clamp(viewportContentSize.Y * 0.12f, 54f, 112f);
        var colors = new[]
        {
            ImGui.GetColorU32(new System.Numerics.Vector4(0.92f, 0.20f, 0.18f, 1f)),
            ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 0.80f, 0.28f, 1f)),
            ImGui.GetColorU32(new System.Numerics.Vector4(0.22f, 0.46f, 0.95f, 1f))
        };
        var axes = new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
        var axisEnds = new Vector2[3];
        var rotationRings = currentTool == TransformTool.Rotate ? new Vector2[3][] : null;
        var center = GetObjectCenter(selected);
        float rotateRadius = GetRotationGizmoRadius(center);

        for (int i = 0; i < axes.Length; i++)
        {
            var axis = GetGizmoAxis(i, selected);
            axisEnds[i] = GetAxisScreenEnd(selected, axis, originScreen, axisLength);
            uint color = gizmoAxis == i ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.20f, 1f)) : colors[i];
            float thickness = gizmoAxis == i ? 4f : 2.6f;
            if (currentTool == TransformTool.Rotate)
            {
                rotationRings![i] = BuildRotationRing(center, axis, rotateRadius);
                DrawRotationRing(drawList, rotationRings[i], color, thickness);
            }
            else
            {
                drawList.AddLine(originScreen, axisEnds[i], color, thickness);
                DrawGizmoHandle(drawList, originScreen, axisEnds[i], color, currentTool == TransformTool.Scale);
            }

            string axisLabel = i == 0 ? "X" : i == 1 ? "Y" : "Z";
            drawList.AddText(axisEnds[i] + new Vector2(6f, -7f), color, axisLabel);
        }

        drawList.AddCircleFilled(originScreen, 5.5f, ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.95f, 0.96f, 1f)));
        drawList.AddCircle(originScreen, 7.5f, ImGui.GetColorU32(new System.Numerics.Vector4(0.04f, 0.04f, 0.045f, 1f)), 18, 1.2f);
        CaptureGizmoMouse(originScreen, axisEnds, rotationRings);
        HandleGizmoInput(originScreen, axisEnds, rotationRings);
    }

    private void DrawBoxColliderEditGizmo(ImDrawListPtr drawList)
    {
        colliderEditMouseCaptured = false;
        if (selected == null || colliderEditObjectId != selected.EditorId)
            return;

        if (selected.GetComponent<CharacterController>() != null && selected.GetComponent<CapsuleCollider>() is { } characterCapsule)
        {
            DrawCapsuleColliderEditGizmo(drawList, characterCapsule);
            return;
        }

        if (selected.GetComponent<BoxCollider>() is { } box)
        {
            DrawBoxColliderEditGizmo(drawList, box);
            return;
        }

        if (selected.GetComponent<SphereCollider>() is { } sphere)
        {
            DrawSphereColliderEditGizmo(drawList, sphere);
            return;
        }

        if (selected.GetComponent<CapsuleCollider>() is { } capsule)
            DrawCapsuleColliderEditGizmo(drawList, capsule);
    }

    private void DrawBoxColliderEditGizmo(ImDrawListPtr drawList, BoxCollider box)
    {
        // El collider sigue la rotación del objeto: vértices LOCALES (centro ± medias dimensiones)
        // transformados por la matriz de mundo (rotación + escala + posición), no por el AABB.
        var m = box.gameObject.WorldMatrix;
        var cLocal = box.Center;
        float hx = box.Size.X * 0.5f, hy = box.Size.Y * 0.5f, hz = box.Size.Z * 0.5f;
        Vector3 BoxCorner(float sx, float sy, float sz)
        {
            var wp = System.Numerics.Vector3.Transform(
                new System.Numerics.Vector3(cLocal.X + sx * hx, cLocal.Y + sy * hy, cLocal.Z + sz * hz), m);
            return new Vector3(wp.X, wp.Y, wp.Z);
        }
        var corners = new[]
        {
            BoxCorner(-1, -1, -1), BoxCorner(1, -1, -1), BoxCorner(1, 1, -1), BoxCorner(-1, 1, -1),
            BoxCorner(-1, -1, 1),  BoxCorner(1, -1, 1),  BoxCorner(1, 1, 1),  BoxCorner(-1, 1, 1)
        };

        var projected = new Vector2?[8];
        for (int i = 0; i < corners.Length; i++)
        {
            if (TryProject(corners[i], out var screen))
                projected[i] = viewportContentMin + screen;
        }

        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 1.00f, 0.28f, 0.95f));
        uint glow = ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 1.00f, 0.28f, 0.22f));
        DrawProjectedEdge(drawList, projected, 0, 1, glow, 6f);
        DrawProjectedEdge(drawList, projected, 1, 2, glow, 6f);
        DrawProjectedEdge(drawList, projected, 2, 3, glow, 6f);
        DrawProjectedEdge(drawList, projected, 3, 0, glow, 6f);
        DrawProjectedEdge(drawList, projected, 4, 5, glow, 6f);
        DrawProjectedEdge(drawList, projected, 5, 6, glow, 6f);
        DrawProjectedEdge(drawList, projected, 6, 7, glow, 6f);
        DrawProjectedEdge(drawList, projected, 7, 4, glow, 6f);
        DrawProjectedEdge(drawList, projected, 0, 4, glow, 6f);
        DrawProjectedEdge(drawList, projected, 1, 5, glow, 6f);
        DrawProjectedEdge(drawList, projected, 2, 6, glow, 6f);
        DrawProjectedEdge(drawList, projected, 3, 7, glow, 6f);

        DrawProjectedEdge(drawList, projected, 0, 1, line, 2f);
        DrawProjectedEdge(drawList, projected, 1, 2, line, 2f);
        DrawProjectedEdge(drawList, projected, 2, 3, line, 2f);
        DrawProjectedEdge(drawList, projected, 3, 0, line, 2f);
        DrawProjectedEdge(drawList, projected, 4, 5, line, 2f);
        DrawProjectedEdge(drawList, projected, 5, 6, line, 2f);
        DrawProjectedEdge(drawList, projected, 6, 7, line, 2f);
        DrawProjectedEdge(drawList, projected, 7, 4, line, 2f);
        DrawProjectedEdge(drawList, projected, 0, 4, line, 2f);
        DrawProjectedEdge(drawList, projected, 1, 5, line, 2f);
        DrawProjectedEdge(drawList, projected, 2, 6, line, 2f);
        DrawProjectedEdge(drawList, projected, 3, 7, line, 2f);

        // Handles en las caras (rotados con el objeto) y ejes de arrastre en espacio local rotado.
        Vector3[] handles =
        {
            BoxCorner(1, 0, 0), BoxCorner(-1, 0, 0),
            BoxCorner(0, 1, 0), BoxCorner(0, -1, 0),
            BoxCorner(0, 0, 1), BoxCorner(0, 0, -1)
        };
        Vector3 BoxAxis(float x, float y, float z)
        {
            var d = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(x, y, z), m);
            float len = d.Length();
            if (len > 1e-6f) d /= len;
            return new Vector3(d.X, d.Y, d.Z);
        }
        Vector3[] axes =
        {
            BoxAxis(1, 0, 0), BoxAxis(-1, 0, 0),
            BoxAxis(0, 1, 0), BoxAxis(0, -1, 0),
            BoxAxis(0, 0, 1), BoxAxis(0, 0, -1)
        };

        var handleScreens = new Vector2?[handles.Length];
        for (int i = 0; i < handles.Length; i++)
        {
            if (!TryProject(handles[i], out var local))
                continue;

            var screen = viewportContentMin + local;
            handleScreens[i] = screen;
            bool active = colliderEditHandle == i;
            uint fill = ImGui.GetColorU32(active
                ? new System.Numerics.Vector4(1f, 0.92f, 0.18f, 1f)
                : new System.Numerics.Vector4(0.34f, 1f, 0.38f, 1f));
            uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.02f, 0.08f, 0.02f, 1f));
            drawList.AddCircleFilled(screen, active ? 7.5f : 6.5f, fill, 24);
            drawList.AddCircle(screen, active ? 8.8f : 7.8f, border, 24, 1.4f);
        }

        CaptureBoxColliderEditMouse(handleScreens);
        HandleBoxColliderEditInput(box, handles, axes, handleScreens);
    }

    private void DrawCapsuleColliderEditGizmo(ImDrawListPtr drawList, CapsuleCollider capsule)
    {
        var bounds = capsule.GetBounds();
        var min = bounds.Min;
        var max = bounds.Max;
        var corners = new[]
        {
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z)
        };

        var projected = new Vector2?[8];
        for (int i = 0; i < corners.Length; i++)
        {
            if (TryProject(corners[i], out var screen))
                projected[i] = viewportContentMin + screen;
        }

        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 0.78f, 1.00f, 0.95f));
        uint glow = ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 0.78f, 1.00f, 0.22f));
        DrawProjectedEdge(drawList, projected, 0, 1, glow, 6f);
        DrawProjectedEdge(drawList, projected, 1, 2, glow, 6f);
        DrawProjectedEdge(drawList, projected, 2, 3, glow, 6f);
        DrawProjectedEdge(drawList, projected, 3, 0, glow, 6f);
        DrawProjectedEdge(drawList, projected, 4, 5, glow, 6f);
        DrawProjectedEdge(drawList, projected, 5, 6, glow, 6f);
        DrawProjectedEdge(drawList, projected, 6, 7, glow, 6f);
        DrawProjectedEdge(drawList, projected, 7, 4, glow, 6f);
        DrawProjectedEdge(drawList, projected, 0, 4, glow, 6f);
        DrawProjectedEdge(drawList, projected, 1, 5, glow, 6f);
        DrawProjectedEdge(drawList, projected, 2, 6, glow, 6f);
        DrawProjectedEdge(drawList, projected, 3, 7, glow, 6f);

        DrawProjectedEdge(drawList, projected, 0, 1, line, 2f);
        DrawProjectedEdge(drawList, projected, 1, 2, line, 2f);
        DrawProjectedEdge(drawList, projected, 2, 3, line, 2f);
        DrawProjectedEdge(drawList, projected, 3, 0, line, 2f);
        DrawProjectedEdge(drawList, projected, 4, 5, line, 2f);
        DrawProjectedEdge(drawList, projected, 5, 6, line, 2f);
        DrawProjectedEdge(drawList, projected, 6, 7, line, 2f);
        DrawProjectedEdge(drawList, projected, 7, 4, line, 2f);
        DrawProjectedEdge(drawList, projected, 0, 4, line, 2f);
        DrawProjectedEdge(drawList, projected, 1, 5, line, 2f);
        DrawProjectedEdge(drawList, projected, 2, 6, line, 2f);
        DrawProjectedEdge(drawList, projected, 3, 7, line, 2f);

        GetCapsuleEditAxes(capsule.Axis, out var mainAxis, out var radialA, out var radialB);
        var center = (min + max) * 0.5f;
        var worldScale = capsule.gameObject.WorldScale;
        float axisScale = GetAxisScale(worldScale, mainAxis);
        float radialAScale = GetAxisScale(worldScale, radialA);
        float radialBScale = GetAxisScale(worldScale, radialB);
        float radiusA = Math.Max(0.0001f, Math.Abs(capsule.Radius) * radialAScale);
        float radiusB = Math.Max(0.0001f, Math.Abs(capsule.Radius) * radialBScale);
        float halfHeight = Math.Max(Math.Max(radiusA, radiusB), Math.Abs(capsule.Height) * axisScale * 0.5f);

        Vector3[] handles =
        {
            center + radialA * radiusA,
            center - radialA * radiusA,
            center + mainAxis * halfHeight,
            center - mainAxis * halfHeight,
            center + radialB * radiusB,
            center - radialB * radiusB
        };
        Vector3[] axes =
        {
            radialA,
            radialA * -1f,
            mainAxis,
            mainAxis * -1f,
            radialB,
            radialB * -1f
        };

        var handleScreens = new Vector2?[handles.Length];
        for (int i = 0; i < handles.Length; i++)
        {
            if (!TryProject(handles[i], out var local))
                continue;

            var screen = viewportContentMin + local;
            handleScreens[i] = screen;
            bool active = colliderEditHandle == i;
            uint fill = ImGui.GetColorU32(active
                ? new System.Numerics.Vector4(1f, 0.92f, 0.18f, 1f)
                : new System.Numerics.Vector4(0.34f, 0.82f, 1f, 1f));
            uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.02f, 0.05f, 0.08f, 1f));
            drawList.AddCircleFilled(screen, active ? 7.5f : 6.5f, fill, 24);
            drawList.AddCircle(screen, active ? 8.8f : 7.8f, border, 24, 1.4f);
        }

        CaptureBoxColliderEditMouse(handleScreens);
        HandleCapsuleColliderEditInput(capsule, handles, axes, handleScreens);
    }

    private void DrawSphereColliderEditGizmo(ImDrawListPtr drawList, SphereCollider sphere)
    {
        var worldPosition = sphere.gameObject.WorldPosition;
        var worldScale = sphere.gameObject.WorldScale;
        var worldCenter = new Vector3(
            worldPosition.X + sphere.Center.X * worldScale.X,
            worldPosition.Y + sphere.Center.Y * worldScale.Y,
            worldPosition.Z + sphere.Center.Z * worldScale.Z);

        float maxScale = Math.Max(0.0001f, Math.Max(Math.Abs(worldScale.X), Math.Max(Math.Abs(worldScale.Y), Math.Abs(worldScale.Z))));
        float radiusWorld = Math.Max(0.0001f, Math.Abs(sphere.Radius) * maxScale);

        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(0.30f, 1.00f, 0.55f, 0.95f));
        uint glow = ImGui.GetColorU32(new System.Numerics.Vector4(0.30f, 1.00f, 0.55f, 0.20f));

        // 3 círculos máximos (perpendiculares a X, Y, Z) para representar la esfera.
        var ringAxes = new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f) };
        for (int i = 0; i < ringAxes.Length; i++)
        {
            var ring = BuildRotationRing(worldCenter, ringAxes[i], radiusWorld);
            DrawRotationRing(drawList, ring, glow, 6f);
            DrawRotationRing(drawList, ring, line, 2f);
        }

        Vector3[] axes =
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, -1f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, -1f)
        };

        var handles = new Vector3[axes.Length];
        for (int i = 0; i < axes.Length; i++)
            handles[i] = worldCenter + axes[i] * radiusWorld;

        var handleScreens = new Vector2?[handles.Length];
        for (int i = 0; i < handles.Length; i++)
        {
            if (!TryProject(handles[i], out var local))
                continue;

            var screen = viewportContentMin + local;
            handleScreens[i] = screen;
            bool active = colliderEditHandle == i;
            uint fill = ImGui.GetColorU32(active
                ? new System.Numerics.Vector4(1f, 0.92f, 0.18f, 1f)
                : new System.Numerics.Vector4(0.40f, 1f, 0.55f, 1f));
            uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.02f, 0.08f, 0.04f, 1f));
            drawList.AddCircleFilled(screen, active ? 7.5f : 6.5f, fill, 24);
            drawList.AddCircle(screen, active ? 8.8f : 7.8f, border, 24, 1.4f);
        }

        CaptureBoxColliderEditMouse(handleScreens);
        HandleSphereColliderEditInput(sphere, handles, axes, handleScreens);
    }

    private void HandleSphereColliderEditInput(SphereCollider sphere, Vector3[] handles, Vector3[] axes, Vector2?[] handleScreens)
    {
        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        if (IsSceneViewportInputBlockedByUi())
        {
            previousGizmoLeftMouseDown = leftDown;
            return;
        }

        var mouse = new Vector2(MouseState.X, MouseState.Y);
        bool pressed = leftDown && !previousGizmoLeftMouseDown;
        previousGizmoLeftMouseDown = leftDown;

        if (!colliderEditDragging && pressed && IsMouseInsideViewport(mouse.X, mouse.Y))
        {
            int hit = FindNearestColliderHandle(mouse, handleScreens);
            if (hit >= 0)
            {
                colliderEditDragging = true;
                colliderEditHandle = hit;
                colliderEditMouseCaptured = true;
                colliderEditDragStartMouse = mouse;
                colliderEditDragStartCenter = sphere.Center;
                colliderEditDragStartSize = new Vector3(sphere.Radius, 0f, 0f);
                colliderEditDragStartAxisDistance = TryGetMouseAxisDistance(mouse, handles[hit], axes[hit], out float distance)
                    ? distance
                    : 0f;
                colliderEditUndoStart = CaptureSceneState();
            }
        }

        if (!colliderEditDragging)
            return;

        if (leftDown && colliderEditHandle >= 0)
        {
            ApplySphereColliderHandleDrag(sphere, mouse, handles[colliderEditHandle], axes[colliderEditHandle]);
            return;
        }

        if (colliderEditUndoStart.HasValue)
            PushSceneState("Edit Sphere Collider " + sphere.gameObject.Name, colliderEditUndoStart.Value, CaptureSceneState());

        colliderEditDragging = false;
        colliderEditHandle = -1;
        colliderEditUndoStart = null;
        physicsEngine.MarkSpatialHashDirty();
    }

    private void ApplySphereColliderHandleDrag(SphereCollider sphere, Vector2 mouse, Vector3 handleWorld, Vector3 axisWorld)
    {
        float worldDelta;
        if (TryGetMouseAxisDistance(mouse, handleWorld, axisWorld, out float axisDistance))
            worldDelta = axisDistance - colliderEditDragStartAxisDistance;
        else
            worldDelta = Vector2.Distance(mouse, colliderEditDragStartMouse) * 0.01f * Math.Sign(Vector2.Dot(mouse - colliderEditDragStartMouse, new Vector2(1f, -1f)));

        var worldScale = sphere.gameObject.WorldScale;
        float maxScale = Math.Max(0.0001f, Math.Max(Math.Abs(worldScale.X), Math.Max(Math.Abs(worldScale.Y), Math.Abs(worldScale.Z))));

        // Cada handle apunta hacia afuera: arrastrarlo hacia afuera (delta positivo) agranda el radio.
        float startRadius = Math.Abs(colliderEditDragStartSize.X);
        sphere.Radius = Math.Max(0.0001f, startRadius + worldDelta / maxScale);
        physicsEngine.MarkSpatialHashDirty();
    }

    private static void DrawProjectedEdge(ImDrawListPtr drawList, Vector2?[] points, int a, int b, uint color, float thickness)
    {
        if (points[a].HasValue && points[b].HasValue)
            drawList.AddLine(points[a]!.Value, points[b]!.Value, color, thickness);
    }

    private void CaptureBoxColliderEditMouse(Vector2?[] handleScreens)
    {
        if (IsSceneViewportInputBlockedByUi())
        {
            colliderEditMouseCaptured = false;
            return;
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        bool any = false;
        foreach (var handle in handleScreens)
        {
            if (!handle.HasValue)
                continue;

            min = Vector2.Min(min, handle.Value);
            max = Vector2.Max(max, handle.Value);
            any = true;
        }

        if (!any)
            return;

        min = ClampToViewport(min - new Vector2(24f, 24f));
        max = ClampToViewport(max + new Vector2(24f, 24f));
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##BoxColliderEditCapture", Vector2.Max(new Vector2(12f, 12f), max - min));
        bool hovered = ImGui.IsItemHovered();
        int near = FindNearestColliderHandle(new Vector2(MouseState.X, MouseState.Y), handleScreens);
        colliderEditMouseCaptured = colliderEditDragging || (hovered && near >= 0);
    }

    private void HandleBoxColliderEditInput(BoxCollider box, Vector3[] handles, Vector3[] axes, Vector2?[] handleScreens)
    {
        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        if (IsSceneViewportInputBlockedByUi())
        {
            previousGizmoLeftMouseDown = leftDown;
            return;
        }

        var mouse = new Vector2(MouseState.X, MouseState.Y);
        bool pressed = leftDown && !previousGizmoLeftMouseDown;
        previousGizmoLeftMouseDown = leftDown;

        if (!colliderEditDragging && pressed && IsMouseInsideViewport(mouse.X, mouse.Y))
        {
            int hit = FindNearestColliderHandle(mouse, handleScreens);
            if (hit >= 0)
            {
                colliderEditDragging = true;
                colliderEditHandle = hit;
                colliderEditMouseCaptured = true;
                colliderEditDragStartMouse = mouse;
                colliderEditDragStartCenter = box.Center;
                colliderEditDragStartSize = box.Size;
                colliderEditDragStartAxisDistance = TryGetMouseAxisDistance(mouse, handles[hit], axes[hit], out float distance)
                    ? distance
                    : 0f;
                colliderEditUndoStart = CaptureSceneState();
            }
        }

        if (!colliderEditDragging)
            return;

        if (leftDown && colliderEditHandle >= 0)
        {
            ApplyBoxColliderHandleDrag(box, mouse, handles[colliderEditHandle], axes[colliderEditHandle]);
            return;
        }

        if (colliderEditUndoStart.HasValue)
            PushSceneState("Edit Box Collider " + box.gameObject.Name, colliderEditUndoStart.Value, CaptureSceneState());

        colliderEditDragging = false;
        colliderEditHandle = -1;
        colliderEditUndoStart = null;
        physicsEngine.MarkSpatialHashDirty();
    }

    private void HandleCapsuleColliderEditInput(CapsuleCollider capsule, Vector3[] handles, Vector3[] axes, Vector2?[] handleScreens)
    {
        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        if (IsSceneViewportInputBlockedByUi())
        {
            previousGizmoLeftMouseDown = leftDown;
            return;
        }

        var mouse = new Vector2(MouseState.X, MouseState.Y);
        bool pressed = leftDown && !previousGizmoLeftMouseDown;
        previousGizmoLeftMouseDown = leftDown;

        if (!colliderEditDragging && pressed && IsMouseInsideViewport(mouse.X, mouse.Y))
        {
            int hit = FindNearestColliderHandle(mouse, handleScreens);
            if (hit >= 0)
            {
                colliderEditDragging = true;
                colliderEditHandle = hit;
                colliderEditMouseCaptured = true;
                colliderEditDragStartMouse = mouse;
                colliderEditDragStartCenter = capsule.Center;
                colliderEditDragStartSize = new Vector3(capsule.Radius, capsule.Height, 0f);
                colliderEditDragStartAxisDistance = TryGetMouseAxisDistance(mouse, handles[hit], axes[hit], out float distance)
                    ? distance
                    : 0f;
                colliderEditUndoStart = CaptureSceneState();
            }
        }

        if (!colliderEditDragging)
            return;

        if (leftDown && colliderEditHandle >= 0)
        {
            ApplyCapsuleColliderHandleDrag(capsule, mouse, handles[colliderEditHandle], axes[colliderEditHandle]);
            return;
        }

        if (colliderEditUndoStart.HasValue)
            PushSceneState("Edit Capsule Collider " + capsule.gameObject.Name, colliderEditUndoStart.Value, CaptureSceneState());

        colliderEditDragging = false;
        colliderEditHandle = -1;
        colliderEditUndoStart = null;
        physicsEngine.MarkSpatialHashDirty();
    }

    private static int FindNearestColliderHandle(Vector2 mouse, Vector2?[] handleScreens)
    {
        int best = -1;
        float bestDistance = 17f; // radio de agarre (px): un poco mayor para que sea más fácil pinchar el handle
        for (int i = 0; i < handleScreens.Length; i++)
        {
            if (!handleScreens[i].HasValue)
                continue;

            float distance = Vector2.Distance(mouse, handleScreens[i]!.Value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private void ApplyBoxColliderHandleDrag(BoxCollider box, Vector2 mouse, Vector3 handleWorld, Vector3 axisWorld)
    {
        float worldDelta;
        if (TryGetMouseAxisDistance(mouse, handleWorld, axisWorld, out float axisDistance))
            worldDelta = axisDistance - colliderEditDragStartAxisDistance;
        else
            worldDelta = Vector2.Distance(mouse, colliderEditDragStartMouse) * 0.01f * Math.Sign(Vector2.Dot(mouse - colliderEditDragStartMouse, new Vector2(1f, -1f)));

        var obj = box.gameObject;
        float sx = Math.Max(0.0001f, Math.Abs(obj.ScaleX));
        float sy = Math.Max(0.0001f, Math.Abs(obj.ScaleY));
        float sz = Math.Max(0.0001f, Math.Abs(obj.ScaleZ));

        var center = colliderEditDragStartCenter;
        var size = colliderEditDragStartSize;

        switch (colliderEditHandle)
        {
            case 0:
                ApplyColliderAxisDelta(ref center.X, ref size.X, worldDelta / sx, positiveHandle: true);
                break;
            case 1:
                ApplyColliderAxisDelta(ref center.X, ref size.X, worldDelta / sx, positiveHandle: false);
                break;
            case 2:
                ApplyColliderAxisDelta(ref center.Y, ref size.Y, worldDelta / sy, positiveHandle: true);
                break;
            case 3:
                ApplyColliderAxisDelta(ref center.Y, ref size.Y, worldDelta / sy, positiveHandle: false);
                break;
            case 4:
                ApplyColliderAxisDelta(ref center.Z, ref size.Z, worldDelta / sz, positiveHandle: true);
                break;
            case 5:
                ApplyColliderAxisDelta(ref center.Z, ref size.Z, worldDelta / sz, positiveHandle: false);
                break;
        }

        box.Center = center;
        box.Size = new Vector3(
            Math.Max(0.0001f, Math.Abs(size.X)),
            Math.Max(0.0001f, Math.Abs(size.Y)),
            Math.Max(0.0001f, Math.Abs(size.Z)));
        physicsEngine.MarkSpatialHashDirty();
    }

    private void ApplyCapsuleColliderHandleDrag(CapsuleCollider capsule, Vector2 mouse, Vector3 handleWorld, Vector3 axisWorld)
    {
        float worldDelta;
        if (TryGetMouseAxisDistance(mouse, handleWorld, axisWorld, out float axisDistance))
            worldDelta = axisDistance - colliderEditDragStartAxisDistance;
        else
            worldDelta = Vector2.Distance(mouse, colliderEditDragStartMouse) * 0.01f * Math.Sign(Vector2.Dot(mouse - colliderEditDragStartMouse, new Vector2(1f, -1f)));

        GetCapsuleEditAxes(capsule.Axis, out var mainAxis, out var radialA, out var radialB);
        var worldScale = capsule.gameObject.WorldScale;
        var center = colliderEditDragStartCenter;
        float radius = Math.Max(0.0001f, Math.Abs(colliderEditDragStartSize.X));
        float height = Math.Max(radius * 2f, Math.Abs(colliderEditDragStartSize.Y));

        switch (colliderEditHandle)
        {
            case 0:
                radius += worldDelta / GetAxisScale(worldScale, radialA);
                break;
            case 1:
                radius -= worldDelta / GetAxisScale(worldScale, radialA);
                break;
            case 2:
                ApplyCapsuleHeightDelta(ref center, ref height, worldDelta / GetAxisScale(worldScale, mainAxis), capsule.Axis, positiveHandle: true);
                break;
            case 3:
                ApplyCapsuleHeightDelta(ref center, ref height, worldDelta / GetAxisScale(worldScale, mainAxis), capsule.Axis, positiveHandle: false);
                break;
            case 4:
                radius += worldDelta / GetAxisScale(worldScale, radialB);
                break;
            case 5:
                radius -= worldDelta / GetAxisScale(worldScale, radialB);
                break;
        }

        radius = Math.Max(0.0001f, Math.Abs(radius));
        height = Math.Max(radius * 2f, Math.Abs(height));

        if (capsule.gameObject.GetComponent<CharacterController>() is { } controller)
        {
            controller.AutoCenter = false;
            controller.Center = center;
            controller.Radius = radius;
            controller.Height = height;
            controller.EnsureCollider();
        }
        else
        {
            capsule.Center = center;
            capsule.Radius = radius;
            capsule.Height = height;
        }

        physicsEngine.MarkSpatialHashDirty();
    }

    private static void ApplyColliderAxisDelta(ref float center, ref float size, float delta, bool positiveHandle)
    {
        float sign = positiveHandle ? 1f : -1f;
        float nextSize = Math.Max(0.0001f, size + delta * sign);
        float applied = (nextSize - size) * sign;
        center += applied * 0.5f;
        size = nextSize;
    }

    private static void ApplyCapsuleHeightDelta(ref Vector3 center, ref float height, float delta, CapsuleAxis axis, bool positiveHandle)
    {
        float c = axis switch
        {
            CapsuleAxis.X => center.X,
            CapsuleAxis.Y => center.Y,
            _ => center.Z
        };

        ApplyColliderAxisDelta(ref c, ref height, delta, positiveHandle);

        center = axis switch
        {
            CapsuleAxis.X => new Vector3(c, center.Y, center.Z),
            CapsuleAxis.Y => new Vector3(center.X, c, center.Z),
            _ => new Vector3(center.X, center.Y, c)
        };
    }

    private static void GetCapsuleEditAxes(CapsuleAxis axis, out Vector3 mainAxis, out Vector3 radialA, out Vector3 radialB)
    {
        switch (axis)
        {
            case CapsuleAxis.X:
                mainAxis = new Vector3(1f, 0f, 0f);
                radialA = new Vector3(0f, 1f, 0f);
                radialB = new Vector3(0f, 0f, 1f);
                break;
            case CapsuleAxis.Z:
                mainAxis = new Vector3(0f, 0f, 1f);
                radialA = new Vector3(1f, 0f, 0f);
                radialB = new Vector3(0f, 1f, 0f);
                break;
            default:
                mainAxis = new Vector3(0f, 1f, 0f);
                radialA = new Vector3(1f, 0f, 0f);
                radialB = new Vector3(0f, 0f, 1f);
                break;
        }
    }

    private static float GetAxisScale(Vector3 scale, Vector3 axis)
    {
        if (Math.Abs(axis.X) > 0.5f)
            return Math.Max(0.0001f, Math.Abs(scale.X));
        if (Math.Abs(axis.Y) > 0.5f)
            return Math.Max(0.0001f, Math.Abs(scale.Y));
        return Math.Max(0.0001f, Math.Abs(scale.Z));
    }

    private void CaptureGizmoMouse(Vector2 originScreen, Vector2[] axisEnds, Vector2[][]? rotationRings)
    {
        if (IsSceneViewportInputBlockedByUi())
        {
            gizmoMouseCaptured = false;
            return;
        }

        var min = originScreen;
        var max = originScreen;
        foreach (var end in axisEnds)
        {
            min = Vector2.Min(min, end);
            max = Vector2.Max(max, end);
        }
        if (rotationRings != null)
        {
            foreach (var ring in rotationRings)
                foreach (var point in ring)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
        }

        min -= new Vector2(24f, 24f);
        max += new Vector2(24f, 24f);
        min = ClampToViewport(min);
        max = ClampToViewport(max);
        var size = Vector2.Max(new Vector2(8f, 8f), max - min);

        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##GizmoMouseCapture", size);
        bool hovered = ImGui.IsItemHovered();
        bool mouseNearAxis = FindNearestGizmoAxis(new Vector2(MouseState.X, MouseState.Y), originScreen, axisEnds, rotationRings) >= 0;
        gizmoMouseCaptured = gizmoDragging || (hovered && mouseNearAxis);
    }

    private Vector2[] BuildRotationRing(Vector3 center, Vector3 axis, float radius)
    {
        const int segments = 96;
        var points = new List<Vector2>(segments);
        GetRingBasis(axis, out var a, out var b);
        for (int i = 0; i < segments; i++)
        {
            float angle = MathF.PI * 2f * i / segments;
            var world = center + (a * MathF.Cos(angle) + b * MathF.Sin(angle)) * radius;
            if (TryProject(world, out var projected))
                points.Add(viewportContentMin + projected);
        }

        return points.ToArray();
    }

    private static void DrawRotationRing(ImDrawListPtr drawList, Vector2[] points, uint color, float thickness)
    {
        if (points.Length < 3)
            return;

        for (int i = 0; i < points.Length; i++)
            drawList.AddLine(points[i], points[(i + 1) % points.Length], color, thickness);
    }

    private static void DrawGizmoHandle(ImDrawListPtr drawList, Vector2 origin, Vector2 end, uint color, bool square)
    {
        var direction = end - origin;
        if (direction.LengthSquared() < 0.001f)
            return;

        direction = Vector2.Normalize(direction);
        if (square)
        {
            var half = new Vector2(5.5f, 5.5f);
            drawList.AddRectFilled(end - half, end + half, color, 1.5f);
            drawList.AddRect(end - half, end + half, ImGui.GetColorU32(new System.Numerics.Vector4(0.04f, 0.04f, 0.045f, 1f)), 1.5f);
            return;
        }

        var tangent = new Vector2(-direction.Y, direction.X);
        var tip = end + direction * 7f;
        var left = end - direction * 7f + tangent * 5f;
        var right = end - direction * 7f - tangent * 5f;
        drawList.AddTriangleFilled(tip, left, right, color);
        drawList.AddTriangle(tip, left, right, ImGui.GetColorU32(new System.Numerics.Vector4(0.04f, 0.04f, 0.045f, 1f)), 1.1f);
    }

    private Vector2 GetAxisScreenEnd(GameObject obj, Vector3 axis, Vector2 originScreen, float fallbackLength)
    {
        var center = GetObjectCenter(obj);
        float worldLength = Math.Max(0.75f, Distance(camera.Position, center) * 0.12f);
        if (TryProject(center + axis * worldLength, out var projected))
            return viewportContentMin + projected;

        return originScreen + new Vector2(axis.X * fallbackLength + axis.Z * fallbackLength * 0.55f, -axis.Y * fallbackLength + axis.Z * fallbackLength * 0.35f);
    }

    private void HandleGizmoInput(Vector2 originScreen, Vector2[] axisEnds, Vector2[][]? rotationRings)
    {
        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        if (IsSceneViewportInputBlockedByUi())
        {
            previousGizmoLeftMouseDown = leftDown;
            return;
        }

        var mouse = new Vector2(MouseState.X, MouseState.Y);

        bool pressed = leftDown && !previousGizmoLeftMouseDown;
        previousGizmoLeftMouseDown = leftDown;

        if (!gizmoDragging && pressed && IsMouseInsideViewport(mouse.X, mouse.Y))
        {
            int hitAxis = FindNearestGizmoAxis(mouse, originScreen, axisEnds, rotationRings);
            if (hitAxis >= 0)
            {
                gizmoMouseCaptured = true;
                gizmoDragging = true;
                gizmoAxis = hitAxis;
                gizmoDragStartMouse = mouse;
                gizmoDragStartWorldCenter = GetObjectCenter(selected!);
                gizmoDragStartAxisDistance = TryGetMouseAxisDistance(mouse, gizmoDragStartWorldCenter, GetGizmoAxis(hitAxis, selected!), out float axisDistance)
                    ? axisDistance
                    : 0f;
                gizmoDragStartTransform = TransformSnapshot.Capture(selected!);
                gizmoUndoStart = CaptureSceneState();
            }
        }

        if (gizmoDragging && selected != null)
        {
            if (leftDown)
                ApplyGizmoDrag(mouse, originScreen, axisEnds[gizmoAxis]);
            else
            {
                if (gizmoUndoStart.HasValue)
                    PushSceneState("Transform " + selected.Name, gizmoUndoStart.Value, CaptureSceneState());
                gizmoDragging = false;
                gizmoAxis = -1;
                gizmoUndoStart = null;
            }
        }
    }

    private static int FindNearestGizmoAxis(Vector2 mouse, Vector2 origin, Vector2[] ends, Vector2[][]? rotationRings = null)
    {
        int bestAxis = -1;
        float best = GizmoHitRadius;
        for (int i = 0; i < ends.Length; i++)
        {
            float distance = rotationRings != null
                ? DistancePointToPolyline(mouse, rotationRings[i], closed: true)
                : DistancePointToSegment(mouse, origin, ends[i]);
            if (distance < best)
            {
                best = distance;
                bestAxis = i;
            }
        }

        return bestAxis;
    }

    private void ApplyGizmoDrag(Vector2 mouse, Vector2 originScreen, Vector2 axisEnd)
    {
        if (selected == null || gizmoAxis < 0) return;

        var axisScreen = axisEnd - originScreen;
        if (axisScreen.LengthSquared() < 0.0001f)
            axisScreen = gizmoAxis == 1 ? new Vector2(0, -1) : new Vector2(1, 0);
        axisScreen = Vector2.Normalize(axisScreen);
        float screenDelta = Vector2.Dot(mouse - gizmoDragStartMouse, axisScreen);
        float worldDelta = screenDelta * Math.Max(0.004f, Distance(camera.Position, GetObjectCenter(selected)) * 0.0015f);

        var t = gizmoDragStartTransform;
        switch (currentTool)
        {
            case TransformTool.Move:
                var axis = GetGizmoAxis(gizmoAxis, selected);
                if (TryGetMouseAxisDistance(mouse, gizmoDragStartWorldCenter, axis, out float axisDistance))
                    worldDelta = axisDistance - gizmoDragStartAxisDistance;

                var worldOffset = axis * worldDelta;
                var localOffset = WorldOffsetToLocalParentOffset(selected, worldOffset);
                selected.PosX = t.Position.X + localOffset.X;
                selected.PosY = t.Position.Y + localOffset.Y;
                selected.PosZ = t.Position.Z + localOffset.Z;
                ApplyGridSnapToObject(selected);
                break;
            case TransformTool.Rotate:
                float degrees = screenDelta * 0.5f;
                if (gizmoAxis == 0) selected.RotX = t.Rotation.X + degrees;
                if (gizmoAxis == 1) selected.RotY = t.Rotation.Y + degrees;
                if (gizmoAxis == 2) selected.RotZ = t.Rotation.Z + degrees;
                break;
            case TransformTool.Scale:
                float scaleDelta = Math.Max(-0.95f, screenDelta * 0.01f);
                if (gizmoAxis == 0) selected.ScaleX = Math.Clamp(t.Scale.X + scaleDelta, 0.001f, 1000f);
                if (gizmoAxis == 1) selected.ScaleY = Math.Clamp(t.Scale.Y + scaleDelta, 0.001f, 1000f);
                if (gizmoAxis == 2) selected.ScaleZ = Math.Clamp(t.Scale.Z + scaleDelta, 0.001f, 1000f);
                break;
        }

        NotifyObjectTransformChanged(selected);
    }

    private void ApplyGridSnapToObject(GameObject obj)
    {
        if (!sceneGridSnapEnabled || viewportLocalSpace)
            return;

        float step = Math.Clamp(sceneGridSize, 0.01f, 1024f);
        obj.PosX = SnapValue(obj.PosX, step);
        obj.PosY = SnapValue(obj.PosY, step);
        obj.PosZ = SnapValue(obj.PosZ, step);
    }

    private static float SnapValue(float value, float step) =>
        MathF.Round(value / step) * step;

    private Vector3 GetGizmoAxis(int axis, GameObject? obj = null)
    {
        var worldAxis = axis switch
        {
            0 => new Vector3(1, 0, 0),
            1 => new Vector3(0, 1, 0),
            _ => new Vector3(0, 0, 1)
        };

        if (!viewportLocalSpace || obj == null)
            return worldAxis;

        Matrix4 rotation =
            Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(obj.RotZ)) *
            Matrix4.CreateRotationX(MathHelper.DegreesToRadians(obj.RotX)) *
            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(obj.RotY));

        var rotated = OpenTK.Mathematics.Vector3.TransformNormal(
            new OpenTK.Mathematics.Vector3(worldAxis.X, worldAxis.Y, worldAxis.Z),
            rotation);

        float len = rotated.Length;
        return len > 0.0001f
            ? new Vector3(rotated.X / len, rotated.Y / len, rotated.Z / len)
            : worldAxis;
    }

    private Vector3 WorldOffsetToLocalParentOffset(GameObject obj, Vector3 worldOffset)
    {
        if (obj.Parent == null)
            return worldOffset;

        var parent = obj.Parent;
        Matrix4 parentRotation =
            Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(parent.RotZ)) *
            Matrix4.CreateRotationX(MathHelper.DegreesToRadians(parent.RotX)) *
            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(parent.RotY));

        Matrix4.Invert(parentRotation, out var invParentRotation);
        var local = OpenTK.Mathematics.Vector3.TransformNormal(
            new OpenTK.Mathematics.Vector3(worldOffset.X, worldOffset.Y, worldOffset.Z),
            invParentRotation);

        return new Vector3(local.X, local.Y, local.Z);
    }

    private bool TryGetMouseAxisDistance(Vector2 mouseScreen, Vector3 axisOrigin, Vector3 axis, out float axisDistance)
    {
        axisDistance = 0f;
        var localMouse = mouseScreen - viewportContentMin;
        if (!BuildCameraRay(localMouse.X, localMouse.Y, out var rayOrigin, out var rayDirection))
            return false;

        axis = NormalizeSafe(axis);
        rayDirection = NormalizeSafe(rayDirection);
        var between = axisOrigin - rayOrigin;
        float axisDotRay = Dot(axis, rayDirection);
        float denominator = 1f - axisDotRay * axisDotRay;
        if (Math.Abs(denominator) < 0.0001f)
            return false;

        float axisDotBetween = Dot(axis, between);
        float rayDotBetween = Dot(rayDirection, between);
        axisDistance = (axisDotRay * rayDotBetween - axisDotBetween) / denominator;
        return float.IsFinite(axisDistance);
    }

    private float GetRotationGizmoRadius(Vector3 center) =>
        Math.Max(0.65f, Distance(camera.Position, center) * 0.14f);

    private static void GetRingBasis(Vector3 axis, out Vector3 a, out Vector3 b)
    {
        if (Math.Abs(axis.X) > 0.5f)
        {
            a = new Vector3(0, 1, 0);
            b = new Vector3(0, 0, 1);
        }
        else if (Math.Abs(axis.Y) > 0.5f)
        {
            a = new Vector3(1, 0, 0);
            b = new Vector3(0, 0, 1);
        }
        else
        {
            a = new Vector3(1, 0, 0);
            b = new Vector3(0, 1, 0);
        }
    }
}
