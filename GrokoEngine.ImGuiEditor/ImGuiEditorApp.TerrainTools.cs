using GrokoEngine;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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

    // ── Import Heightmap desde imagen (estilo Unity "Import Raw/Heightmap") ──────

    private static string? BrowseForHeightmapImage()
    {
        try
        {
            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select Heightmap Image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                CheckFileExists = true
            };
            return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : null;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudo abrir el diálogo de archivos: " + ex.Message);
            return null;
        }
    }

    // Igual que un pincel de sculpt, pero de una sola pasada sobre todo el heightmap: se
    // captura el estado antes/después como un solo paso de Undo.
    private void ImportTerrainHeightmapFromFile(Terrain terrain, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        byte[]? gray;
        int width, height;
        try
        {
            using var bitmap = new Bitmap(imagePath);
            width = bitmap.Width;
            height = bitmap.Height;
            gray = ExtractGrayscale(bitmap);
        }
        catch (Exception ex)
        {
            statusMessage = "No se pudo leer la imagen: " + ex.Message;
            GrokoEngine.Debug.LogWarning($"[Terrain] Import Heightmap falló para '{imagePath}': {ex.Message}");
            return;
        }

        var before = CaptureSceneState();
        TerrainHeightmapImport.Apply(terrain, gray, width, height);
        terrain.Version++;
        PushSceneState("Import Heightmap " + (selected?.Name ?? terrain.gameObject.Name), before, CaptureSceneState());
        statusMessage = $"Heightmap importado desde {Path.GetFileName(imagePath)}";
    }

    // Luminancia (BT.601) por pixel, row-major, 0..255. Mismo patrón de LockBits que usa
    // SceneViewportRenderer.Textures.cs para subir texturas a la GPU.
    private static byte[] ExtractGrayscale(Bitmap original)
    {
        using var bitmap = original.PixelFormat == PixelFormat.Format32bppArgb
            ? new Bitmap(original)
            : original.Clone(new Rectangle(0, 0, original.Width, original.Height), PixelFormat.Format32bppArgb);

        int w = bitmap.Width, h = bitmap.Height;
        var gray = new byte[w * h];
        var rect = new Rectangle(0, 0, w, h);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] row = new byte[Math.Abs(stride)];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * stride, row, 0, row.Length);
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4; // BGRA
                    byte b = row[i], g = row[i + 1], r = row[i + 2];
                    gray[y * w + x] = (byte)Math.Clamp((r * 299 + g * 587 + b * 114) / 1000, 0, 255);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return gray;
    }
}
