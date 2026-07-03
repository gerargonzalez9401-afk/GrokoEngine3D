using GrokoEngine;
using ImGuiNET;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using GlfwKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    public enum TerrainEditMode
    {
        Sculpt,
        Paint,
        Trees,
        Details
    }

    private static string SculptToolTooltip(TerrainBrushTool tool) => tool switch
    {
        TerrainBrushTool.Raise => "Levanta el terreno bajo el pincel.",
        TerrainBrushTool.Lower => "Hunde el terreno bajo el pincel.",
        TerrainBrushTool.Smooth => "Suaviza promediando con los vecinos.",
        TerrainBrushTool.Flatten => "Nivela hacia la altura del primer clic.",
        TerrainBrushTool.Noise => "Agrega ruido aleatorio.",
        TerrainBrushTool.Extrude => "Empuja parejo hacia arriba, sin promediar con la altura actual.",
        _ => ""
    };

    private TerrainEditMode terrainEditMode = TerrainEditMode.Sculpt;
    private TerrainBrushTool terrainBrushTool = TerrainBrushTool.Raise;
    private TerrainBrushFalloff terrainBrushFalloff = TerrainBrushFalloff.Soft;
    private int terrainPaintLayer;
    private float terrainBrushRadius = 5f;
    private float terrainBrushStrength = 0.5f;
    private int terrainNoiseSeed = 12345;
    private bool previousTerrainSculptMouseDown;
    private SceneStateSnapshot? terrainSculptUndoStart;
    private float? terrainFlattenHeight;

    // ── Trees ──────────────────────────────────────────────────────
    private int terrainTreePrototypeIndex;
    private float terrainTreeDensity = 2f;
    private readonly Random terrainTreeRng = new();

    // ── Details/pasto ──────────────────────────────────────────────
    private int terrainDetailPrototypeIndex;
    private float terrainDetailDensity = 40f;
    private readonly Random terrainDetailRng = new();

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
        else if (terrainEditMode == TerrainEditMode.Trees)
            ApplyTerrainTreeBrush(terrain, localX, localZ, deltaTime);
        else if (terrainEditMode == TerrainEditMode.Details)
            ApplyTerrainDetailBrush(terrain, localX, localZ, deltaTime);
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
            string label = terrainEditMode switch
            {
                TerrainEditMode.Paint => "Paint Terrain ",
                TerrainEditMode.Trees => "Plant Trees ",
                TerrainEditMode.Details => "Paint Details ",
                _ => "Sculpt Terrain "
            };
            PushSceneState(label + selected.Name, terrainSculptUndoStart.Value, CaptureSceneState());

            // Un Terrain Static (el default al crearlo, como en Unity) vive horneado en el batch
            // estático general junto con el resto de la geometría estática de la escena. Si lo
            // invalidáramos en CADA frame mientras se arrastra el pincel, cada trazo forzaría
            // reconstruir TODO ese batch (no solo el terreno) 60 veces por segundo — así que se
            // invalida una vez, al soltar el mouse, cuando ya termina el trazo completo. Mientras
            // se sculpea/pinta/planta en vivo, un Terrain Static no se actualiza visualmente
            // hasta soltar (uno no-Static sí, porque pasa por el pase dinámico normal cada frame).
            if (selected.IsStatic)
                sceneRenderer.InvalidateStaticBatch();
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
            terrainBrushRadius, terrainBrushStrength, deltaTime, terrainNoiseSeed, ref terrainFlattenHeight,
            terrainBrushFalloff);
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

    // Lista de prototipos de árbol (mesh/prefab), estilo Unity "Tree Prototypes": un slot de
    // asset por prototipo, con un toggle para elegir cuál planta el pincel y un botón para
    // sacarlo de la lista (reindexando los árboles ya plantados con ese prototipo).
    private void DrawTerrainTreePrototypeList(Terrain terrain)
    {
        FieldRow("");
        ImGui.Text($"{terrain.TreePrototypes.Count} prototipo(s)  —  {terrain.Trees.Count} árbol(es) plantado(s)");

        int removeIndex = -1;
        for (int i = 0; i < terrain.TreePrototypes.Count; i++)
        {
            int idx = i;
            ImGui.PushID("treeProto" + idx);
            DrawAssetSlot($"Tree {idx + 1}", terrain.TreePrototypes[idx], "Drop mesh",
                path => terrain.TreePrototypes[idx] = path,
                path => ObjLoader.IsSupportedMesh(path) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));

            FieldRow("");
            if (DrawToggleButton("Plant this tree", terrainTreePrototypeIndex == idx, new Vector2(-70f, 0f)))
                terrainTreePrototypeIndex = idx;
            ImGui.SameLine();
            if (ImGui.Button("Remove", new Vector2(-1f, 0f)))
                removeIndex = idx;
            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            terrain.TreePrototypes.RemoveAt(removeIndex);
            terrain.Trees.RemoveAll(t => t.PrototypeIndex == removeIndex);
            for (int i = 0; i < terrain.Trees.Count; i++)
            {
                var t = terrain.Trees[i];
                if (t.PrototypeIndex > removeIndex)
                {
                    t.PrototypeIndex--;
                    terrain.Trees[i] = t;
                }
            }
            if (terrainTreePrototypeIndex >= terrain.TreePrototypes.Count)
                terrainTreePrototypeIndex = Math.Max(0, terrain.TreePrototypes.Count - 1);
            terrain.TreeVersion++;
            if (terrain.gameObject.IsStatic)
                sceneRenderer.InvalidateStaticBatch();
        }

        FieldRow("");
        if (ImGui.Button("+ Add Tree Prototype", new Vector2(-1f, 0f)))
            terrain.TreePrototypes.Add("");
    }

    // Shift+click borra árboles en vez de plantar (mismo atajo que Unity).
    private void ApplyTerrainTreeBrush(Terrain terrain, float localX, float localZ, float deltaTime)
    {
        bool erasing = KeyboardState.IsKeyDown(GlfwKeys.LeftShift) || KeyboardState.IsKeyDown(GlfwKeys.RightShift);
        bool changed = erasing
            ? TerrainTreeBrush.Erase(terrain, localX, localZ, terrainBrushRadius)
            : TerrainTreeBrush.Paint(terrain, terrainTreePrototypeIndex, localX, localZ, terrainBrushRadius,
                terrainTreeDensity, deltaTime, terrainTreeRng);
        if (changed)
            terrain.TreeVersion++;
    }

    // Lista de prototipos de detalle (textura + tamaño), estilo Unity "Detail Prototypes": a
    // diferencia de los árboles no hay slot de malla, porque un detalle siempre se dibuja como
    // billboard plano (nunca 3D). Width/Height son el tamaño del quad en unidades del mundo.
    private void DrawTerrainDetailPrototypeList(Terrain terrain)
    {
        FieldRow("");
        ImGui.Text($"{terrain.DetailPrototypes.Count} prototipo(s)  —  {terrain.Details.Count} detalle(s) plantado(s)");

        int removeIndex = -1;
        for (int i = 0; i < terrain.DetailPrototypes.Count; i++)
        {
            int idx = i;
            var proto = terrain.DetailPrototypes[idx];
            ImGui.PushID("detailProto" + idx);
            DrawAssetSlot($"Detail {idx + 1}", proto.TexturePath, "Drop texture",
                path =>
                {
                    var p = terrain.DetailPrototypes[idx];
                    p.TexturePath = path;
                    terrain.DetailPrototypes[idx] = p;
                },
                MaterialAsset.IsTexturePath);

            DrawFloat("Width", proto.Width, v =>
            {
                var p = terrain.DetailPrototypes[idx];
                p.Width = Math.Max(0.01f, v);
                terrain.DetailPrototypes[idx] = p;
            }, 0.05f, 0.01f, 20f);
            DrawFloat("Height", proto.Height, v =>
            {
                var p = terrain.DetailPrototypes[idx];
                p.Height = Math.Max(0.01f, v);
                terrain.DetailPrototypes[idx] = p;
            }, 0.05f, 0.01f, 20f);

            FieldRow("");
            if (DrawToggleButton("Plant this detail", terrainDetailPrototypeIndex == idx, new Vector2(-70f, 0f)))
                terrainDetailPrototypeIndex = idx;
            ImGui.SameLine();
            if (ImGui.Button("Remove", new Vector2(-1f, 0f)))
                removeIndex = idx;
            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            terrain.DetailPrototypes.RemoveAt(removeIndex);
            terrain.Details.RemoveAll(d => d.PrototypeIndex == removeIndex);
            for (int i = 0; i < terrain.Details.Count; i++)
            {
                var d = terrain.Details[i];
                if (d.PrototypeIndex > removeIndex)
                {
                    d.PrototypeIndex--;
                    terrain.Details[i] = d;
                }
            }
            if (terrainDetailPrototypeIndex >= terrain.DetailPrototypes.Count)
                terrainDetailPrototypeIndex = Math.Max(0, terrain.DetailPrototypes.Count - 1);
            terrain.DetailVersion++;
            if (terrain.gameObject.IsStatic)
                sceneRenderer.InvalidateStaticBatch();
        }

        FieldRow("");
        if (ImGui.Button("+ Add Detail Prototype", new Vector2(-1f, 0f)))
            terrain.DetailPrototypes.Add(new TerrainDetailPrototype { TexturePath = "", Width = 0.5f, Height = 0.5f });
    }

    // Shift+click borra detalles en vez de plantar (mismo atajo que árboles/Unity).
    private void ApplyTerrainDetailBrush(Terrain terrain, float localX, float localZ, float deltaTime)
    {
        bool erasing = KeyboardState.IsKeyDown(GlfwKeys.LeftShift) || KeyboardState.IsKeyDown(GlfwKeys.RightShift);
        bool changed = erasing
            ? TerrainDetailBrush.Erase(terrain, localX, localZ, terrainBrushRadius)
            : TerrainDetailBrush.Paint(terrain, terrainDetailPrototypeIndex, localX, localZ, terrainBrushRadius,
                terrainDetailDensity, deltaTime, terrainDetailRng);
        if (changed)
            terrain.DetailVersion++;
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
        if (terrain.gameObject.IsStatic)
            sceneRenderer.InvalidateStaticBatch();
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
