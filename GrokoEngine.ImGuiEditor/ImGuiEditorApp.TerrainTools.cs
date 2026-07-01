using GrokoEngine;
using System;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    public enum TerrainEditMode
    {
        Sculpt,
        Paint
    }

    private TerrainEditMode terrainEditMode = TerrainEditMode.Sculpt;
    private TerrainBrushTool terrainBrushTool = TerrainBrushTool.Raise;
    private int terrainPaintLayer;
    private float terrainBrushRadius = 5f;
    private float terrainBrushStrength = 0.5f;
    private int terrainNoiseSeed = 12345;
    private bool previousTerrainSculptMouseDown;
    private SceneStateSnapshot? terrainSculptUndoStart;
    private float? terrainFlattenHeight;

    private void HandleTerrainSculpting(float deltaTime)
    {
        if (isPlaying) return;
        if (IsSceneViewportInputBlockedByUi())
        {
            previousTerrainSculptMouseDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
            return;
        }

        var terrain = selected?.GetComponent<Terrain>();
        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        bool pressed = leftDown && !previousTerrainSculptMouseDown;
        bool released = !leftDown && previousTerrainSculptMouseDown;
        previousTerrainSculptMouseDown = leftDown;

        if (terrain == null)
        {
            if (released)
                FinishTerrainSculpt();
            return;
        }

        var mouse = new Vector2(MouseState.X, MouseState.Y);

        if (gizmoDragging || gizmoMouseCaptured || IsMouseOverSelectedGizmo(mouse))
        {
            if (released)
                FinishTerrainSculpt();
            return;
        }

        if (!leftDown)
        {
            if (released)
                FinishTerrainSculpt();
            return;
        }

        if (!TryGetTerrainBrushHit(terrain, mouse, out float localX, out float localZ))
            return;

        if (pressed)
        {
            terrainSculptUndoStart = CaptureSceneState();
            terrainFlattenHeight = null;
        }

        if (terrainEditMode == TerrainEditMode.Paint)
            ApplyTerrainPaintBrush(terrain, localX, localZ, deltaTime);
        else
            ApplyTerrainSculptBrush(terrain, localX, localZ, deltaTime);
    }

    private bool TryGetTerrainBrushHit(Terrain terrain, Vector2 mouse, out float localX, out float localZ)
    {
        localX = 0f;
        localZ = 0f;

        if (!IsMouseInsideViewport(mouse.X, mouse.Y))
            return false;

        var localMouse = new Vector2(mouse.X - viewportContentMin.X, mouse.Y - viewportContentMin.Y);
        if (!BuildCameraRay(localMouse.X, localMouse.Y, out var origin, out var direction))
            return false;

        float planeY = selected!.GlobalPosition.Y;
        if (MathF.Abs(direction.Y) < 1e-6f)
            return false;

        float t = (planeY - origin.Y) / direction.Y;
        if (t < 0f)
            return false;

        var hit = origin + direction * t;
        localX = hit.X - selected.GlobalPosition.X;
        localZ = hit.Z - selected.GlobalPosition.Z;
        return true;
    }

    private void FinishTerrainSculpt()
    {
        if (terrainSculptUndoStart.HasValue && selected != null)
        {
            string label = terrainEditMode == TerrainEditMode.Paint ? "Paint Terrain " : "Sculpt Terrain ";
            PushSceneState(label + selected.Name, terrainSculptUndoStart.Value, CaptureSceneState());
        }

        terrainSculptUndoStart = null;
        terrainFlattenHeight = null;
    }

    // La matematica de las brochas (celdas afectadas, sculpt, paint) vive en GrokoEngine.Core
    // (TerrainBrush.cs), testeable sin ImGui/OpenGL. Aqui solo se traduce el hit de raycast a
    // localX/localZ y se aplica el resultado (Version/SplatVersion los sube el propio helper de Core).
    private void ApplyTerrainSculptBrush(Terrain terrain, float localX, float localZ, float deltaTime)
    {
        bool changed = TerrainBrush.ApplySculpt(terrain, terrainBrushTool, localX, localZ,
            terrainBrushRadius, terrainBrushStrength, deltaTime, terrainNoiseSeed, ref terrainFlattenHeight);
        if (changed)
            terrain.Version++;
    }

    private void ApplyTerrainPaintBrush(Terrain terrain, float localX, float localZ, float deltaTime)
    {
        bool changed = TerrainBrush.ApplyPaint(terrain, terrainPaintLayer, localX, localZ,
            terrainBrushRadius, terrainBrushStrength, deltaTime);
        if (changed)
            terrain.SplatVersion++;
    }
}
