using GrokoEngine;
using System;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    public enum TerrainBrushTool
    {
        Raise,
        Lower,
        Smooth,
        Flatten,
        Noise,
        Extrude
    }

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

    // Calcula la celda central (gx/gz) y el rango de celdas afectadas por el pincel para localX/localZ.
    // Devuelve false si el pincel no toca la grilla.
    private bool ComputeTerrainBrushCells(Terrain terrain, float localX, float localZ,
        out float gx, out float gz, out float stepX, out float stepZ,
        out int minX, out int maxX, out int minZ, out int maxZ)
    {
        int resolution = terrain.Resolution;
        stepX = terrain.SizeX / (resolution - 1);
        stepZ = terrain.SizeZ / (resolution - 1);
        gx = 0f;
        gz = 0f;
        minX = maxX = minZ = maxZ = 0;
        if (stepX <= 0f || stepZ <= 0f)
            return false;

        float halfX = terrain.SizeX * 0.5f;
        float halfZ = terrain.SizeZ * 0.5f;

        gx = (localX + halfX) / stepX;
        gz = (localZ + halfZ) / stepZ;

        float radiusCellsX = terrainBrushRadius / stepX;
        float radiusCellsZ = terrainBrushRadius / stepZ;

        if (gx < -radiusCellsX || gx > resolution - 1 + radiusCellsX ||
            gz < -radiusCellsZ || gz > resolution - 1 + radiusCellsZ)
            return false;

        minX = Math.Max(0, (int)MathF.Floor(gx - radiusCellsX));
        maxX = Math.Min(resolution - 1, (int)MathF.Ceiling(gx + radiusCellsX));
        minZ = Math.Max(0, (int)MathF.Floor(gz - radiusCellsZ));
        maxZ = Math.Min(resolution - 1, (int)MathF.Ceiling(gz + radiusCellsZ));
        return true;
    }

    private void ApplyTerrainSculptBrush(Terrain terrain, float localX, float localZ, float deltaTime)
    {
        if (!ComputeTerrainBrushCells(terrain, localX, localZ, out float gx, out float gz, out float stepX, out float stepZ,
            out int minX, out int maxX, out int minZ, out int maxZ))
            return;

        int resolution = terrain.Resolution;

        if (terrainBrushTool == TerrainBrushTool.Flatten && terrainFlattenHeight == null)
        {
            int centerX = Math.Clamp((int)MathF.Round(gx), 0, resolution - 1);
            int centerZ = Math.Clamp((int)MathF.Round(gz), 0, resolution - 1);
            terrainFlattenHeight = terrain.GetHeight(centerX, centerZ);
        }

        bool changed = false;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (x - gx) * stepX;
                float dz = (z - gz) * stepZ;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > terrainBrushRadius)
                    continue;

                float t = 1f - dist / terrainBrushRadius;
                float falloff = t * t * (3f - 2f * t);
                int index = z * resolution + x;

                switch (terrainBrushTool)
                {
                    case TerrainBrushTool.Raise:
                        terrain.Heightmap[index] = Math.Clamp(terrain.Heightmap[index] + falloff * terrainBrushStrength * deltaTime, -2f, 2f);
                        changed = true;
                        break;
                    case TerrainBrushTool.Lower:
                        terrain.Heightmap[index] = Math.Clamp(terrain.Heightmap[index] - falloff * terrainBrushStrength * deltaTime, -2f, 2f);
                        changed = true;
                        break;
                    case TerrainBrushTool.Smooth:
                    {
                        float avg = (terrain.GetHeight(x - 1, z) + terrain.GetHeight(x + 1, z) +
                                      terrain.GetHeight(x, z - 1) + terrain.GetHeight(x, z + 1)) * 0.25f;
                        terrain.Heightmap[index] += (avg - terrain.Heightmap[index]) * falloff * terrainBrushStrength * deltaTime;
                        changed = true;
                        break;
                    }
                    case TerrainBrushTool.Flatten:
                    {
                        float target = terrainFlattenHeight ?? terrain.Heightmap[index];
                        terrain.Heightmap[index] += (target - terrain.Heightmap[index]) * falloff * terrainBrushStrength * deltaTime;
                        changed = true;
                        break;
                    }
                    case TerrainBrushTool.Noise:
                    {
                        float n = HashNoise(x, z, terrainNoiseSeed);
                        terrain.Heightmap[index] = Math.Clamp(terrain.Heightmap[index] + n * falloff * terrainBrushStrength * deltaTime, -2f, 2f);
                        changed = true;
                        break;
                    }
                    case TerrainBrushTool.Extrude:
                        terrain.Heightmap[index] = Math.Clamp(terrain.Heightmap[index] + terrainBrushStrength * deltaTime, -2f, 2f);
                        changed = true;
                        break;
                }
            }
        }

        if (changed)
            terrain.Version++;
    }

    private static float HashNoise(int x, int z, int seed)
    {
        int n = x * 73856093 ^ z * 19349663 ^ seed * 83492791;
        n = (n << 13) ^ n;
        int nn = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
        return 1f - (nn / 1073741824f); // -1..1
    }

    private void ApplyTerrainPaintBrush(Terrain terrain, float localX, float localZ, float deltaTime)
    {
        if (!ComputeTerrainBrushCells(terrain, localX, localZ, out float gx, out float gz, out float stepX, out float stepZ,
            out int minX, out int maxX, out int minZ, out int maxZ))
            return;

        int resolution = terrain.Resolution;
        int activeLayer = Math.Clamp(terrainPaintLayer, 0, 3);
        bool changed = false;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (x - gx) * stepX;
                float dz = (z - gz) * stepZ;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > terrainBrushRadius)
                    continue;

                float t = 1f - dist / terrainBrushRadius;
                float falloff = t * t * (3f - 2f * t);
                int pixel = (z * resolution + x) * 4;

                float increase = falloff * terrainBrushStrength * deltaTime * 255f;
                if (increase <= 0f)
                    continue;

                float current = terrain.SplatMap[pixel + activeLayer];
                float newValue = Math.Min(255f, current + increase);
                float delta = newValue - current;
                if (delta <= 0f)
                    continue;

                // Resta el incremento a las otras 3 capas, proporcional a su peso actual, para mantener la suma en ~255.
                float otherSum = 0f;
                for (int c = 0; c < 4; c++)
                {
                    if (c != activeLayer)
                        otherSum += terrain.SplatMap[pixel + c];
                }

                if (otherSum > 0f)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        if (c == activeLayer) continue;
                        float share = terrain.SplatMap[pixel + c] / otherSum;
                        float reduced = Math.Max(0f, terrain.SplatMap[pixel + c] - delta * share);
                        terrain.SplatMap[pixel + c] = (byte)Math.Round(reduced);
                    }
                }

                terrain.SplatMap[pixel + activeLayer] = (byte)Math.Round(newValue);
                changed = true;
            }
        }

        if (changed)
            terrain.SplatVersion++;
    }
}
