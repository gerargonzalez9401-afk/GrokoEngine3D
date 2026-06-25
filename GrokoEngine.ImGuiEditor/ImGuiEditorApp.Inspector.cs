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
    private void DrawInspectorPanel(Vector2 size)
    {
        BeginPanel("##InspectorPanel", size);
        // Guardamos el rect del Inspector para que clicar en él NO deseleccione el asset del Project.
        inspectorPanelMin = ImGui.GetWindowPos();
        inspectorPanelMax = inspectorPanelMin + ImGui.GetWindowSize();

        // Si el Inspector está bloqueado, mostramos el objetivo bloqueado en
        // lugar de la selección actual (igual que el candado de Unity).
        GameObject? inspected = selected;
        string? inspectedAssetPath = selectedAssetPath;
        if (inspectorLocked)
        {
            if (lockedInspectorObject != null && EnumerateObjects(objects).Contains(lockedInspectorObject))
            {
                inspected = lockedInspectorObject;
                inspectedAssetPath = null;
            }
            else if (!string.IsNullOrWhiteSpace(lockedInspectorAssetPath) &&
                     (File.Exists(lockedInspectorAssetPath) || Directory.Exists(lockedInspectorAssetPath)))
            {
                inspected = null;
                inspectedAssetPath = lockedInspectorAssetPath;
            }
            else
            {
                // El objetivo bloqueado ya no existe: liberamos el bloqueo.
                inspectorLocked = false;
                lockedInspectorObject = null;
                lockedInspectorAssetPath = null;
                inspected = selected;
                inspectedAssetPath = selectedAssetPath;
            }
        }

        DrawPanelHeader("Inspector", inspected?.Name, () =>
        {
            bool wasLocked = inspectorLocked;
            if (DrawInlineIconToggle(wasLocked ? EditorIcon.Lock : EditorIcon.Unlock,
                    wasLocked ? "Desbloquear Inspector" : "Bloquear Inspector", wasLocked))
            {
                inspectorLocked = !wasLocked;
                if (inspectorLocked)
                {
                    lockedInspectorObject = inspected;
                    lockedInspectorAssetPath = inspected == null ? inspectedAssetPath : null;
                }
                else
                {
                    lockedInspectorObject = null;
                    lockedInspectorAssetPath = null;
                }
            }
        });

        if (inspected == null)
        {
            if (inspectedAssetPath != null && (File.Exists(inspectedAssetPath) || Directory.Exists(inspectedAssetPath)))
                DrawSelectedAssetSummary(inspectedAssetPath);
            else
                ImGui.TextDisabled("No selection");
            ImGui.EndChild();
            return;
        }

        DrawSelectedObjectHeader(inspected);
        HandleInspectorAssetDropTarget(inspected);
        DrawObjectEditorMeta(inspected);
        DrawInspectorAssetDropZone(inspected);

        string name = inspected.Name;
        FieldRow("Name");
        if (ImGui.InputText("##ObjectName", ref name, 128))
        {
            BeginNameEdit(inspected);
            inspected.Name = name;
        }
        EndNameEdit(inspected);

        if (!string.IsNullOrWhiteSpace(inspected.PrefabAssetPath))
        {
            ImGui.TextWrapped("Prefab: " + Path.GetFileName(inspected.PrefabAssetPath));
            if (!isPlaying && ImGui.Button("Apply Prefab"))
                ApplyPrefab(inspected);
            ImGui.Separator();
        }

        DrawTransform(inspected);

        // Como Unity: todo objeto renderizable muestra un Mesh Renderer (se añade si falta).
        EnsureMeshRenderer(inspected);

        ImGui.Separator();
        ImGui.TextDisabled("Components");
        for (int i = inspected.Components.Count - 1; i >= 0; i--)
            DrawComponent(inspected, inspected.Components[i]);

        ImGui.Dummy(new Vector2(0f, 16f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 12f));

        // Botón Add Component centrado y más estrecho (estilo Unity).
        const float addW = 200f;
        float addAvail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (addAvail - addW) * 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.24f, 0.34f, 0.52f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.29f, 0.44f, 0.66f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.24f, 0.48f, 0.74f, 1f));
        if (ImGui.Button("Add Component", new Vector2(addW, 26f)))
        {
            addComponentSearch = string.Empty;
            addComponentSearchFocus = true;
            ImGui.OpenPopup("AddComponentPopup");
        }
        ImGui.PopStyleColor(3);
        DrawAddComponentPopup(inspected);
        ImGui.Spacing();

        ImGui.EndChild();
    }

    private void DrawSelectedObjectHeader(GameObject obj)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.095f, 0.108f, 0.126f, 1f));
        ImGui.BeginChild("InspectorObjectHeader", new Vector2(0f, 42f), ImGuiChildFlags.None);
        DrawInlineIcon(GetObjectIcon(obj), GetObjectIconTooltip(obj), 22f);
        ImGui.SameLine(0f, 6f);
        bool active = obj.IsActive;
        if (SmallCheckbox("##HeaderObjectActive", ref active))
            CommitSceneMutation(active ? "Activate Object" : "Deactivate Object", () =>
            {
                obj.IsActive = active;
                sceneRenderer.InvalidateStaticBatch();
                sceneRenderer.InvalidateCullingState();
                return obj;
            });
        DrawTooltip("Active");
        ImGui.SameLine(0f, 6f);
        ImGui.AlignTextToFramePadding();
        float headerW = ImGui.GetContentRegionAvail().X;
        float staticW = 58f;
        string title = Ellipsize(obj.Name, Math.Max(6, (int)((headerW - staticW - 54f) / 7.5f)));
        ImGui.TextUnformatted(title);
        if (headerW > 240f)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"{obj.Components.Count} comp   {obj.Children.Count} child");
        }
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 8f, ImGui.GetWindowWidth() - staticW - 12f));
        bool isStatic = obj.IsStatic;
        if (SmallCheckbox("##HeaderObjectStatic", ref isStatic))
        {
            obj.IsStatic = isStatic;
            objectStatic[obj.EditorId] = isStatic;
            sceneRenderer.InvalidateStaticBatch();
        }
        ImGui.SameLine(0f, 4f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Static");
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawObjectEditorMeta(GameObject obj)
    {

        // Reconstruir el batch estático al cambiar el flag
        DrawObjectTagLayerRow(obj);
    }

    private void DrawObjectTagLayerRow(GameObject obj)
    {
        string tag = GetObjectEditorString(objectTag, obj.EditorId, "Untagged");
        string layer = LayerMask.LayerToName(obj.Layer);
        float width = ImGui.GetContentRegionAvail().X;

        void DrawTagCombo(float itemWidth)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Tag");
            ImGui.SameLine(0f, 4f);
            ImGui.SetNextItemWidth(itemWidth);
            if (ImGui.BeginCombo("##ObjectTag", tag))
            {
                foreach (string option in new[] { "Untagged", "Player", "Enemy", "MainCamera", "EditorOnly" })
                    if (ImGui.Selectable(option, string.Equals(tag, option, StringComparison.Ordinal)))
                        objectTag[obj.EditorId] = option;
                ImGui.EndCombo();
            }
        }

        void DrawLayerCombo(float itemWidth)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Layer");
            ImGui.SameLine(0f, 4f);
            ImGui.SetNextItemWidth(itemWidth);
            if (ImGui.BeginCombo("##ObjectLayer", layer))
            {
                foreach (string option in new[] { "Default", "TransparentFX", "Ignore Raycast", "Water", "UI", "Player", "Enemy", "Ground", "Pickup", "Interactable" })
                    if (ImGui.Selectable(option, string.Equals(layer, option, StringComparison.Ordinal)))
                        obj.Layer = LayerMask.NameToLayer(option);
                ImGui.EndCombo();
            }
        }

        if (width < 330f)
        {
            DrawTagCombo(Math.Max(80f, width - 34f));
            DrawLayerCombo(Math.Max(80f, width - 48f));
        }
        else
        {
            float half = Math.Max(92f, (width - 88f) * 0.5f);
            DrawTagCombo(half);
            ImGui.SameLine(0f, 10f);
            DrawLayerCombo(-1f);
        }

        ImGui.Separator();
    }

    private void DrawInspectorAssetDropZone(GameObject obj)
    {
        if (draggingAssetPath == null || !IsInspectorAssignableAsset(draggingAssetPath))
            return;

        string kind = MaterialAsset.IsMaterialPath(draggingAssetPath) ? "material" :
            MaterialAsset.IsTexturePath(draggingAssetPath) ? "texture" : "mesh";

        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.24f, 0.24f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.30f, 0.30f, 0.30f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiAccent);
        ImGui.Button($"Drop {kind}: {Path.GetFileName(draggingAssetPath)}", new Vector2(-1f, 24f));
        ImGui.PopStyleColor(3);
        HandleInspectorAssetDropTarget(obj);
    }

    private void HandleInspectorAssetDropTarget(GameObject obj)
    {
        if (!ImGui.BeginDragDropTarget())
        {
            if (!MouseState.IsButtonDown(GlfwMouseButton.Left))
                inspectorAssetDropArmed = false;
            return;
        }

        bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
        if (MouseState.IsButtonDown(GlfwMouseButton.Left))
        {
            inspectorAssetDropArmed = true;
        }
        else if (delivered && inspectorAssetDropArmed && draggingAssetPath != null && IsInspectorAssignableAsset(draggingAssetPath))
        {
            ApplyAssetToObjectFromInspector(obj, draggingAssetPath);
            draggingAssetPath = null;
            inspectorAssetDropArmed = false;
        }

        ImGui.EndDragDropTarget();
    }

    private static bool IsInspectorAssignableAsset(string path) =>
        File.Exists(path) &&
        (MaterialAsset.IsMaterialPath(path) ||
         MaterialAsset.IsTexturePath(path) ||
         ObjLoader.IsSupportedMesh(path));

    private void ApplyAssetToObjectFromInspector(GameObject obj, string assetPath)
    {
        if (isPlaying)
        {
            statusMessage = "Stop Play mode to assign assets";
            return;
        }

        CommitSceneMutation("Assign Asset", () =>
        {
            if (MaterialAsset.IsMaterialPath(assetPath))
            {
                statusMessage = assetService.ApplyMaterial(obj, assetPath)
                    ? $"Material applied: {Path.GetFileName(assetPath)}"
                    : "Material apply failed";
            }
            else if (MaterialAsset.IsTexturePath(assetPath))
            {
                statusMessage = assetService.ApplyTexture(obj, assetPath)
                    ? $"Texture applied: {Path.GetFileName(assetPath)}"
                    : "Texture apply failed";
            }
            else if (ObjLoader.IsSupportedMesh(assetPath))
            {
                statusMessage = assetService.AssignMesh(obj, assetPath, out var mesh) && mesh != null
                    ? $"Mesh assigned: {Path.GetFileName(assetPath)}"
                    : GetMeshLoadFailureMessage("Mesh assignment failed");
            }

            return true;
        });
    }


    //metodo para dibujar el header del inspector de un asset, con su preview a la izquierda y su informacion a la derecha, se llama desde DrawInspectorPanel cuando el seleccionado es un asset
    private void DrawSelectedAssetSummary(string path)
    {
        bool folder = Directory.Exists(path);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.095f, 0.108f, 0.126f, 1f));//color del header del inspector 
        ImGui.BeginChild("InspectorAssetHeader", new Vector2(0f, 70f), ImGuiChildFlags.None);//alto del header del inspector de faul 70
        var drawList = ImGui.GetWindowDrawList();
        var previewMin = ImGui.GetCursorScreenPos() + new Vector2(6f, 8f);
        DrawProjectAssetPreview(drawList, path, folder, previewMin, 55f);//visualisacion del asset a la izquierda del inspector de faul 76
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 92f);
        ImGui.Text(folder ? "FOLDER" : GetAssetKind(path));
        ImGui.SameLine();
        ImGui.Text(Path.GetFileName(path));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 92f);
        ImGui.TextDisabled(IsInsideAssets(path) ? Path.GetRelativePath(rootAssetsPath, path).Replace('\\', '/') : path);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 92f);
        ImGui.TextDisabled(folder ? $"{Directory.GetFiles(path).Length} file(s)" : $"{new FileInfo(path).Length / 1024f:F1} KB");
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 92f);
        ImGui.TextDisabled((folder ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path)).ToString("yyyy-MM-dd HH:mm"));
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.Separator();
        if (!folder)
            DrawAssetSpecificInspector(path);
    }

    private void DrawAssetSpecificInspector(string path)
    {
        if (MaterialAsset.IsMaterialPath(path))
        {
            DrawMaterialAssetInspector(path);
            return;
        }

        if (MaterialAsset.IsTexturePath(path))
        {
            DrawTextureAssetInspector(path);
            return;
        }

        if (ObjLoader.IsSupportedMesh(path))
        {
            DrawMeshAssetInspector(path);
            return;
        }

        if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            DrawPrefabAssetInspector(path);
            return;
        }

        if (path.EndsWith(".gscene", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextDisabled("Scene Asset");
            ImGui.TextWrapped("Double click support is planned; use File > Reload Scene for now.");
            return;
        }

        if (ScriptableObjectAsset.IsAssetPath(path))
        {
            DrawScriptableObjectAssetInspector(path);
            return;
        }

        ImGui.TextDisabled("Asset settings");
        ImGui.TextWrapped(Path.GetExtension(path).TrimStart('.').ToUpperInvariant());
    }

    private void DrawTextureAssetInspector(string path)
    {
        var settings = TextureImportSettingsAsset.Load(path);
        bool changed = false;
        bool looksLikeNormal = TextureImportSettings.LooksLikeNormalMap(path);

        if (TryGetTextureMetadata(path, out var metadata))
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.55f, 0.78f, 0.98f, 1f), Path.GetFileName(path));
            ImGui.TextDisabled("Texture 2D Import Settings");
            ImGui.Separator();

            string[] textureTypes = { "Default", "Normal Map" };
            int textureTypeIndex = settings.IsNormalMap ? 1 : 0;
            if (DrawTextureImportCombo("Texture Type", textureTypes, ref textureTypeIndex))
            {
                settings.TextureType = textureTypes[textureTypeIndex];
                if (settings.IsNormalMap)
                {
                    settings.SRgb = false;
                    settings.AlphaIsTransparency = false;
                }
                changed = true;
            }

            string[] textureShapes = { "2D" };
            int textureShapeIndex = 0;
            if (DrawTextureImportCombo("Texture Shape", textureShapes, ref textureShapeIndex))
            {
                settings.TextureShape = textureShapes[textureShapeIndex];
                changed = true;
            }

            bool srgb = settings.SRgb;
            FieldRow("sRGB (Color Texture)");
            if (settings.IsNormalMap)
            {
                ImGui.BeginDisabled();
                ImGui.Checkbox("##tex_srgb", ref srgb);
                ImGui.EndDisabled();
                if (settings.SRgb)
                {
                    settings.SRgb = false;
                    changed = true;
                }
            }
            else if (ImGui.Checkbox("##tex_srgb", ref srgb))
            {
                settings.SRgb = srgb;
                changed = true;
            }

            string[] alphaSources = { "Input Texture Alpha", "None" };
            int alphaIndex = Array.FindIndex(alphaSources, x => x.Equals(settings.AlphaSource, StringComparison.OrdinalIgnoreCase));
            if (alphaIndex < 0) alphaIndex = 0;
            if (DrawTextureImportCombo("Alpha Source", alphaSources, ref alphaIndex))
            {
                settings.AlphaSource = alphaSources[alphaIndex];
                changed = true;
            }

            bool alphaTransparency = settings.AlphaIsTransparency;
            FieldRow("Alpha Is Transparency");
            if (settings.IsNormalMap)
            {
                ImGui.BeginDisabled();
                ImGui.Checkbox("##tex_alpha_trans", ref alphaTransparency);
                ImGui.EndDisabled();
            }
            else if (ImGui.Checkbox("##tex_alpha_trans", ref alphaTransparency))
            {
                settings.AlphaIsTransparency = alphaTransparency;
                changed = true;
            }

            if ((looksLikeNormal && !settings.IsNormalMap) || (settings.IsNormalMap && settings.SRgb))
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.73f, 0.28f, 1f));
                ImGui.TextWrapped("This texture looks like a normal map. Normal maps must use Texture Type = Normal Map and sRGB off.");
                ImGui.PopStyleColor();
                if (ImGui.Button("Fix Now##normalmap_fix", new Vector2(-1f, 22f)))
                {
                    settings.TextureType = "Normal Map";
                    settings.SRgb = false;
                    settings.AlphaIsTransparency = false;
                    settings.Normalize(path);
                    TextureImportSettingsAsset.Save(path, settings);
                    InvalidateTextureAsset(path);
                    statusMessageValue = "Normal map import settings applied";
                    changed = false;
                }
            }

            ImGui.Spacing();
            if (SectionHeader("Advanced"))
            {
                string[] wrapModes = { "Repeat", "Clamp", "Mirror" };
                int wrapIndex = Array.FindIndex(wrapModes, x => x.Equals(settings.WrapMode, StringComparison.OrdinalIgnoreCase));
                if (wrapIndex < 0) wrapIndex = 0;
                if (DrawTextureImportCombo("Wrap Mode", wrapModes, ref wrapIndex))
                {
                    settings.WrapMode = wrapModes[wrapIndex];
                    changed = true;
                }

                string[] filterModes = { "Point", "Bilinear", "Trilinear" };
                int filterIndex = Array.FindIndex(filterModes, x => x.Equals(settings.FilterMode, StringComparison.OrdinalIgnoreCase));
                if (filterIndex < 0) filterIndex = 1;
                if (DrawTextureImportCombo("Filter Mode", filterModes, ref filterIndex))
                {
                    settings.FilterMode = filterModes[filterIndex];
                    changed = true;
                }

                FieldRow("Aniso Level");
                int aniso = settings.AnisoLevel;
                if (ImGui.SliderInt("##tex_aniso", ref aniso, 0, 16))
                {
                    settings.AnisoLevel = aniso;
                    changed = true;
                }

                string[] maxSizes = { "256", "512", "1024", "2048", "4096", "8192" };
                int maxIndex = Array.FindIndex(maxSizes, x => int.TryParse(x, out int size) && size == settings.MaxSize);
                if (maxIndex < 0) maxIndex = 3;
                if (DrawTextureImportCombo("Max Size", maxSizes, ref maxIndex))
                {
                    settings.MaxSize = int.Parse(maxSizes[maxIndex], CultureInfo.InvariantCulture);
                    changed = true;
                }

                string[] resizeAlgorithms = { "Mitchell", "Bilinear", "Nearest" };
                int resizeIndex = Array.FindIndex(resizeAlgorithms, x => x.Equals(settings.ResizeAlgorithm, StringComparison.OrdinalIgnoreCase));
                if (resizeIndex < 0) resizeIndex = 0;
                if (DrawTextureImportCombo("Resize Algorithm", resizeAlgorithms, ref resizeIndex))
                {
                    settings.ResizeAlgorithm = resizeAlgorithms[resizeIndex];
                    changed = true;
                }

                string[] formats = { "Automatic", "RGBA32", "RGB24" };
                int formatIndex = Array.FindIndex(formats, x => x.Equals(settings.Format, StringComparison.OrdinalIgnoreCase));
                if (formatIndex < 0) formatIndex = 0;
                if (DrawTextureImportCombo("Format", formats, ref formatIndex))
                {
                    settings.Format = formats[formatIndex];
                    changed = true;
                }

                string[] compression = { "None", "Low Quality", "Normal Quality", "High Quality" };
                int compressionIndex = Array.FindIndex(compression, x => x.Equals(settings.Compression, StringComparison.OrdinalIgnoreCase));
                if (compressionIndex < 0) compressionIndex = 2;
                if (DrawTextureImportCombo("Compression", compression, ref compressionIndex))
                {
                    settings.Compression = compression[compressionIndex];
                    changed = true;
                }
            }

            ImGui.Spacing();
            ImGui.BeginDisabled(!changed);
            if (ImGui.Button("Revert##tex_revert", new Vector2(Math.Max(84f, ImGui.GetContentRegionAvail().X * 0.5f - 3f), 22f)))
            {
                settings = TextureImportSettingsAsset.Load(path);
                changed = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Apply##tex_apply", new Vector2(-1f, 22f)))
            {
                settings.Normalize(path);
                TextureImportSettingsAsset.Save(path, settings);
                InvalidateTextureAsset(path);
                statusMessageValue = "Texture import settings applied";
                changed = false;
            }
            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("Preview");
            ImGui.Text($"Size: {metadata.Width} x {metadata.Height}");
            ImGui.Text($"Format: {metadata.Format}");
            ImGui.TextDisabled(settings.IsNormalMap ? "Normal map: sRGB off, sampled as vector data" : "Color texture");

            ImGui.Spacing();

            if (TryGetCachedPreviewTexture(path, out var textureId))
            {
                float previewSize = Math.Min(220f, ImGui.GetContentRegionAvail().X);
                ImGui.Image(textureId, new Vector2(previewSize, previewSize), Vector2.Zero, Vector2.One);
            }
            else
            {
                QueuePreviewGeneration(path);
                ImGui.TextDisabled("Preview queued...");
            }
        }
        else
        {
            ImGui.TextDisabled("Texture metadata unavailable");
        }
    }

    private static bool DrawTextureImportCombo(string label, string[] items, ref int current)
    {
        FieldRow(label);
        current = Math.Clamp(current, 0, Math.Max(0, items.Length - 1));
        return ImGui.Combo("##tex_" + label, ref current, items, items.Length);
    }

    private void InvalidateTextureAsset(string path)
    {
        InvalidateAssetPreview(path, deleteTexture: true);
        sceneRenderer.InvalidateTexture(path);
        sceneRenderer.InvalidateStaticBatch();
    }
    private string GetPreviewCachePath(string assetPath)
    {
        string previewsDir = Path.Combine(projectPath, "Library", "Previews");
        Directory.CreateDirectory(previewsDir);

        string safeName = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(assetPath.ToLowerInvariant())));

        return Path.Combine(previewsDir, safeName + ".preview.png");
    }
    private bool EnsureDiskPreview(string assetPath, out string previewPath)
    {
        previewPath = GetPreviewCachePath(assetPath);

        if (!File.Exists(assetPath))
            return false;

        DateTime assetTime = File.GetLastWriteTimeUtc(assetPath);

        if (File.Exists(previewPath))
        {
            try
            {
                DateTime previewTime = File.GetLastWriteTimeUtc(previewPath);
                if (previewTime >= assetTime)
                    return true;
            }
            catch
            {
                try { File.Delete(previewPath); } catch { }
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);

            const int maxPreviewSize = 128;

            using var source = System.Drawing.Image.FromFile(assetPath);

            float scale = Math.Min(
                (float)maxPreviewSize / source.Width,
                (float)maxPreviewSize / source.Height);

            scale = Math.Min(scale, 1f);

            int newW = Math.Max(1, (int)(source.Width * scale));
            int newH = Math.Max(1, (int)(source.Height * scale));

            using var bitmap = new System.Drawing.Bitmap(
                newW,
                newH,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, 0, 0, newW, newH);
            }

            string tempPath = previewPath + ".tmp";
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }

            bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
            File.SetLastWriteTimeUtc(tempPath, assetTime);

            if (File.Exists(previewPath))
                File.Delete(previewPath);

            File.Move(tempPath, previewPath);
            File.SetLastWriteTimeUtc(previewPath, assetTime);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetTextureMetadata(string path, out TextureMetadata metadata)
    {
        metadata = default;

        if (!File.Exists(path))
            return false;

        string key = "meta:" + path;
        DateTime writeTime = File.GetLastWriteTimeUtc(path);

        if (textureMetadataCache.TryGetValue(key, out var cached) &&
            cached.WriteTime == writeTime)
        {
            metadata = cached;
            return true;
        }

        try
        {
            using var image = System.Drawing.Image.FromFile(path);

            metadata = new TextureMetadata(
                writeTime,
                image.Width,
                image.Height,
                image.PixelFormat.ToString());

            textureMetadataCache[key] = metadata;
            return true;
        }
        catch
        {
            textureMetadataCache.Remove(key);
            return false;
        }
    }

    private string modelImportPath = "";
    private string modelImportSelectionKey = "";
    private ModelImportSettings modelImportSettings = new();
    private int pendingModelImportTab = -1;
    private int selectedImportClipIndex;

    private void DrawMeshAssetInspector(string path)
    {
        var mesh = ObjLoader.Load(path) ?? new ParsedMesh();

        // Cargar ajustes al cambiar de modelo seleccionado.
        if (!string.Equals(modelImportPath, path, StringComparison.OrdinalIgnoreCase))
        {
            modelImportPath = path;
            modelImportSettings = ModelImportSettingsAsset.Load(path);
            selectedImportClipIndex = 0;
            modelImportSelectionKey = "";
        }
        var s = modelImportSettings;

        ImGui.TextColored(new System.Numerics.Vector4(0.55f, 0.78f, 0.98f, 1f), Path.GetFileName(path));
        ImGui.TextDisabled("Import Settings");
        ImGui.Separator();

        string currentSelectionKey = path + "|" + (selectedProjectSubAssetKey ?? "");
        if (!string.Equals(modelImportSelectionKey, currentSelectionKey, StringComparison.OrdinalIgnoreCase))
        {
            modelImportSelectionKey = currentSelectionKey;
            pendingModelImportTab = -1;

            if (!string.IsNullOrWhiteSpace(selectedProjectSubAssetKey) &&
                TryParseProjectSubAssetKey(selectedProjectSubAssetKey, out var subParent, out var subKind, out var subIndex) &&
                string.Equals(subParent, path, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(subKind, "animation", StringComparison.OrdinalIgnoreCase))
                {
                    pendingModelImportTab = 2;
                    selectedImportClipIndex = Math.Max(0, subIndex);
                }
                else if (string.Equals(subKind, "avatar", StringComparison.OrdinalIgnoreCase))
                    pendingModelImportTab = 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedProjectSubAssetKey) &&
            TryParseProjectSubAssetKey(selectedProjectSubAssetKey, out var activeSubParent, out var activeSubKind, out var activeSubIndex) &&
            string.Equals(activeSubParent, path, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(activeSubKind, "animation", StringComparison.OrdinalIgnoreCase))
                selectedImportClipIndex = Math.Max(0, activeSubIndex);
        }

        if (ImGui.BeginTabBar("##modelImportTabs"))
        {
            bool tabOpen = true;
            if (ImGui.BeginTabItem("Model", ref tabOpen, pendingModelImportTab == 0 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) { DrawModelTab(s, mesh); ImGui.EndTabItem(); }
            tabOpen = true;
            if (ImGui.BeginTabItem("Rig", ref tabOpen, pendingModelImportTab == 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) { DrawRigTab(s, mesh); ImGui.EndTabItem(); }
            tabOpen = true;
            if (ImGui.BeginTabItem("Animation", ref tabOpen, pendingModelImportTab == 2 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) { DrawAnimationTab(s, mesh); ImGui.EndTabItem(); }
            tabOpen = true;
            if (ImGui.BeginTabItem("Materials", ref tabOpen, pendingModelImportTab == 3 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) { DrawMaterialsTab(mesh); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        pendingModelImportTab = -1;

        ImGui.Separator();
        float w = ImGui.GetContentRegionAvail().X;
        if (ImGui.Button("Apply", new System.Numerics.Vector2(w * 0.5f - 4f, 0f)))
        {
            var previousSettings = ModelImportSettingsAsset.Load(path);
            ModelImportSettingsAsset.Save(path, s);
            int touched = ApplyModelImportSettingsLive(path, previousSettings, s, mesh);
            statusMessage = touched > 0
                ? $"Import settings applied: {touched} assets/objects refreshed"
                : "Import settings applied";
        }
        ImGui.SameLine();
        if (ImGui.Button("Revert", new System.Numerics.Vector2(w * 0.5f - 4f, 0f)))
            modelImportSettings = ModelImportSettingsAsset.Load(path);
    }

    private int ApplyModelImportSettingsLive(string modelPath, ModelImportSettings previousSettings, ModelImportSettings settings, ParsedMesh mesh)
    {
        int touched = 0;
        ObjLoader.InvalidateCache(modelPath);

        if (!string.IsNullOrWhiteSpace(rootAssetsPath) && Directory.Exists(rootAssetsPath))
        {
            foreach (string animPath in Directory.EnumerateFiles(rootAssetsPath, "*.anim", SearchOption.AllDirectories))
            {
                var clip = AnimationClipAsset.Load(animPath);
                if (!PathsEqual(clip.SourceModelPath, modelPath))
                    continue;

                clip.Loop = settings.LoopTime;
                clip.LoopPose = settings.LoopPose;
                clip.CycleOffset = settings.CycleOffset;
                clip.BakeRootRotationIntoPose = settings.BakeRootRotationIntoPose;
                clip.RootRotationBasedUpon = settings.RootRotationBasedUpon;
                clip.RootRotationOffset = settings.RootRotationOffset;
                clip.BakeRootPositionYIntoPose = settings.BakeRootPositionYIntoPose;
                clip.RootPositionYBasedUpon = settings.RootPositionYBasedUpon;
                clip.RootPositionYOffset = settings.RootPositionYOffset;
                clip.BakeRootPositionXZIntoPose = settings.BakeRootPositionXZIntoPose;
                clip.RootPositionXZBasedUpon = settings.RootPositionXZBasedUpon;
                clip.Mirror = settings.Mirror;
                clip.AdditiveReferencePose = settings.AdditiveReferencePose;
                clip.AvatarPath = settings.AvatarDefinition == "Copy From Other Avatar"
                    ? settings.AvatarSource
                    : settings.CreatedAvatarPath;
                clip.Humanoid = string.Equals(settings.AnimationType, "Humanoid", StringComparison.OrdinalIgnoreCase);
                AnimationClipAsset.Save(animPath, clip);
                touched++;
            }
        }

        float previousScale = ModelImportSettingsAsset.EffectiveScale(previousSettings, mesh.RecommendedScale);
        float currentScale = ModelImportSettingsAsset.EffectiveScale(settings, mesh.RecommendedScale);
        foreach (var obj in EnumerateObjects(objects))
        {
            bool objectTouched = false;
            if (obj.GetComponent<MeshFilter>() is { } mf && PathsEqual(mf.MeshPath, modelPath))
            {
                if (IsNearImportScale(obj, previousScale))
                {
                    obj.ScaleX = currentScale;
                    obj.ScaleY = currentScale;
                    obj.ScaleZ = currentScale;
                }
                objectTouched = true;
            }

            if (obj.GetComponent<Animator>() is { } animator && AnimatorUsesModel(animator, modelPath))
            {
                animator.InvalidateCache();
                objectTouched = true;
            }

            if (objectTouched)
                touched++;
        }

        InvalidateProjectFolderCache(Path.GetDirectoryName(modelPath));
        InvalidateAssetPreview(modelPath, deleteTexture: true);
        sceneRenderer.InvalidateStaticBatch();
        sceneRenderer.InvalidateCullingState();
        return touched;
    }

    private static bool IsNearImportScale(GameObject obj, float importScale)
    {
        if (importScale <= 0f)
            return false;

        const float epsilon = 0.0005f;
        return MathF.Abs(obj.ScaleX - importScale) <= epsilon &&
               MathF.Abs(obj.ScaleY - importScale) <= epsilon &&
               MathF.Abs(obj.ScaleZ - importScale) <= epsilon;
    }

    private static bool AnimatorUsesModel(Animator animator, string modelPath)
    {
        if (PathsEqual(animator.ModelPath, modelPath) || PathsEqual(animator.ClipPath, modelPath))
            return true;

        if (animator.AnimationSources.Any(path => PathsEqual(path, modelPath)))
            return true;

        if (AnimationClipAsset.IsAnimationPath(animator.ClipPath) && AnimationClipUsesModel(animator.ClipPath, modelPath))
            return true;

        var controller = animator.GetController();
        if (controller == null)
            return false;

        return controller.States.Any(state =>
            PathsEqual(state.ClipPath, modelPath) ||
            (AnimationClipAsset.IsAnimationPath(state.ClipPath) && AnimationClipUsesModel(state.ClipPath, modelPath)));
    }

    private static bool AnimationClipUsesModel(string animPath, string modelPath)
    {
        if (string.IsNullOrWhiteSpace(animPath) || !File.Exists(animPath))
            return false;

        return PathsEqual(AnimationClipAsset.Load(animPath).SourceModelPath, modelPath);
    }

    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void DrawModelTab(ModelImportSettings s, ParsedMesh mesh)
    {
        ImGui.Dummy(new System.Numerics.Vector2(0f, 2f));
        ImGui.TextDisabled("Scene");
        ImpFloat("Scale Factor", () => s.ScaleFactor, v => s.ScaleFactor = v);
        ImpCheck("Convert Units", () => s.ConvertUnits, v => s.ConvertUnits = v, "1cm (File) → 0.01m (Engine)");
        ImpCheck("Bake Axis Conversion", () => s.BakeAxisConversion, v => s.BakeAxisConversion = v);
        ImpCheck("Import BlendShapes", () => s.ImportBlendShapes, v => s.ImportBlendShapes = v);
        ImpCheck("Import Visibility", () => s.ImportVisibility, v => s.ImportVisibility = v);
        ImpCheck("Import Cameras", () => s.ImportCameras, v => s.ImportCameras = v);
        ImpCheck("Import Lights", () => s.ImportLights, v => s.ImportLights = v);
        ImpCheck("Preserve Hierarchy", () => s.PreserveHierarchy, v => s.PreserveHierarchy = v);
        ImpCheck("Sort Hierarchy By Name", () => s.SortHierarchyByName, v => s.SortHierarchyByName = v);

        ImGui.Dummy(new System.Numerics.Vector2(0f, 4f));
        ImGui.TextDisabled("Meshes");
        ImpCombo("Mesh Compression", new[] { "Off", "Low", "Medium", "High" }, () => s.MeshCompression, v => s.MeshCompression = v);
        ImpCheck("Read/Write", () => s.ReadWrite, v => s.ReadWrite = v);
        ImpCombo("Optimize Mesh", new[] { "Nothing", "Everything", "Polygon Order", "Vertex Order" }, () => s.OptimizeMesh, v => s.OptimizeMesh = v);
        ImpCheck("Generate Colliders", () => s.GenerateColliders, v => s.GenerateColliders = v);

        ImGui.Dummy(new System.Numerics.Vector2(0f, 4f));
        ImGui.TextDisabled("Geometry");
        ImpCheck("Weld Vertices", () => s.WeldVertices, v => s.WeldVertices = v);
        ImpCombo("Normals", new[] { "Import", "Calculate", "None" }, () => s.Normals, v => s.Normals = v);
        ImpCombo("Tangents", new[] { "Calculate Mikktspace", "Calculate Legacy", "Import", "None" }, () => s.Tangents, v => s.Tangents = v);
        float sa = s.SmoothingAngle;
        FieldRow("Smoothing Angle");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##smooth", ref sa, 0f, 180f, "%.0f")) s.SmoothingAngle = sa;

        if (mesh != null)
        {
            ImGui.Dummy(new System.Numerics.Vector2(0f, 6f));
            ImGui.TextDisabled($"Verts: {mesh.Positions.Length / 3}   Tris: {mesh.TriangleCount}   Escala efectiva: {ModelImportSettingsAsset.EffectiveScale(s, mesh.RecommendedScale):0.###}");
        }
    }

    private void DrawRigTab(ModelImportSettings s, ParsedMesh mesh)
    {
        ImGui.Dummy(new System.Numerics.Vector2(0f, 2f));
        ImpCombo("Animation Type", new[] { "None", "Generic", "Humanoid" }, () => s.AnimationType, v => s.AnimationType = v);

        bool rigged = s.AnimationType is "Generic" or "Humanoid";
        if (rigged)
        {
            ImpCombo("Avatar Definition", new[] { "Create From This Model", "Copy From Other Avatar" },
                () => s.AvatarDefinition, v => s.AvatarDefinition = v);

            if (s.AvatarDefinition == "Create From This Model")
            {
                if (!string.IsNullOrWhiteSpace(s.CreatedAvatarPath) && File.Exists(s.CreatedAvatarPath))
                    ImGui.TextDisabled("Avatar: " + Path.GetFileName(s.CreatedAvatarPath));
                else
                    ImGui.TextDisabled("El avatar se crea al pulsar 'Create Avatar' (tras Apply).");

                if (ImGui.Button("Create Avatar", new System.Numerics.Vector2(-1f, 0f)))
                {
                    ModelImportSettingsAsset.Save(modelImportPath, s);          // persiste ajustes
                    s.CreatedAvatarPath = AvatarAsset.CreateFromModel(modelImportPath);
                    ModelImportSettingsAsset.Save(modelImportPath, s);
                    selectedAssetPath = s.CreatedAvatarPath;
                    statusMessage = "Avatar creado: " + Path.GetFileName(s.CreatedAvatarPath);
                }
            }
            else // Copy From Other Avatar
            {
                DrawAssetSlot("Source Avatar", s.AvatarSource, "Drop .avatar del modelo",
                    p => s.AvatarSource = p, AvatarAsset.IsAvatarPath);
                if (!string.IsNullOrWhiteSpace(s.AvatarSource) && File.Exists(s.AvatarSource))
                {
                    var av = AvatarAsset.Load(s.AvatarSource);
                    bool compatible = AvatarAsset.IsCompatibleWithModel(s.AvatarSource, modelImportPath, out string compatibility);
                    ImGui.TextColored(
                        compatible
                            ? new System.Numerics.Vector4(0.48f, 0.86f, 0.55f, 1f)
                            : new System.Numerics.Vector4(0.95f, 0.46f, 0.42f, 1f),
                        compatible ? "Compatible: " + compatibility : "No compatible: " + compatibility);
                    ImGui.TextDisabled($"Avatar '{av.Name}' — {av.BoneNames.Count} huesos");
                }
                else
                    ImGui.TextDisabled("Pega aquí el .avatar de tu modelo (huesos por nombre).");
            }

            ImGui.Dummy(new System.Numerics.Vector2(0f, 4f));
            ImpCombo("Skin Weights", new[] { "Standard (4 Bones)", "Custom" }, () => s.SkinWeights, v => s.SkinWeights = v);
            ImpCheck("Strip Bones", () => s.StripBones, v => s.StripBones = v);
            ImpCheck("Optimize Game Objects", () => s.OptimizeGameObjects, v => s.OptimizeGameObjects = v);
        }

        ImGui.Dummy(new System.Numerics.Vector2(0f, 6f));
        int boneCount = CountNodes(mesh?.Hierarchy);
        ImGui.TextDisabled($"Huesos detectados: {boneCount}");
        if (mesh != null && !mesh.HasSkin)
        {
            if (mesh.Animations.Count > 0)
                ImGui.TextDisabled("(FBX solo animacion: usa Copy From Other Avatar y extrae el clip)");
            else
                ImGui.TextDisabled("(este modelo no tiene skinning)");
        }
    }

    private void DrawAnimationTab(ModelImportSettings s, ParsedMesh mesh)
    {
        ImGui.Dummy(new System.Numerics.Vector2(0f, 2f));
        ImpCheck("Import Constraints", () => false, _ => { });
        ImpCheck("Import Animation", () => s.ImportAnimation, v => s.ImportAnimation = v);
        ImpCheck("Import Animated Custom Properties", () => s.ImportAnimatedCustomProperties, v => s.ImportAnimatedCustomProperties = v);
        ImpCheck("Bake Animations", () => s.BakeAnimations, v => s.BakeAnimations = v);
        ImpCombo("Anim. Compression", new[] { "Off", "Keyframe Reduction", "Optimal" }, () => s.AnimationCompression, v => s.AnimationCompression = v);
        ImpFloat("Rotation Error", () => s.RotationError, v => s.RotationError = MathF.Max(0f, v));
        ImpFloat("Position Error", () => s.PositionError, v => s.PositionError = MathF.Max(0f, v));
        ImpFloat("Scale Error", () => s.ScaleError, v => s.ScaleError = MathF.Max(0f, v));
        ImpCheck("Remove Constant Scale Curves", () => s.RemoveConstantScaleCurves, v => s.RemoveConstantScaleCurves = v);

        ImGui.Separator();
        if (mesh == null || mesh.Animations.Count == 0)
        {
            ImGui.TextDisabled("No embedded animation clips.");
            return;
        }

        selectedImportClipIndex = Math.Clamp(selectedImportClipIndex, 0, mesh.Animations.Count - 1);
        ImGui.TextDisabled("Clips");
        if (ImGui.BeginTable("##importClips", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Start", ImGuiTableColumnFlags.WidthFixed, 52f);
            ImGui.TableSetupColumn("End", ImGuiTableColumnFlags.WidthFixed, 52f);
            ImGui.TableHeadersRow();
            for (int i = 0; i < mesh.Animations.Count; i++)
            {
                var clip = mesh.Animations[i];
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                bool selectedClip = selectedImportClipIndex == i;
                string clipName = string.IsNullOrWhiteSpace(clip.Name) ? $"Animation {i + 1}" : clip.Name;
                if (ImGui.Selectable(clipName + "##clip" + i, selectedClip, ImGuiSelectableFlags.SpanAllColumns))
                    selectedImportClipIndex = i;
                ImGui.TableSetColumnIndex(1);
                ImGui.TextDisabled("0");
                ImGui.TableSetColumnIndex(2);
                ImGui.TextDisabled(MathF.Round((float)(clip.DurationSeconds * 30.0)).ToString(CultureInfo.InvariantCulture));
            }
            ImGui.EndTable();
        }

        ImGui.Separator();
        var selectedClipInfo = mesh.Animations[selectedImportClipIndex];
        string selectedClipName = string.IsNullOrWhiteSpace(selectedClipInfo.Name)
            ? $"Animation {selectedImportClipIndex + 1}"
            : selectedClipInfo.Name;

        ImGui.TextColored(new System.Numerics.Vector4(0.55f, 0.92f, 0.84f, 1f), selectedClipName);
        ImGui.TextDisabled($"Length {selectedClipInfo.DurationSeconds:0.###}s     30 FPS");
        ImpIntReadonly("Start", 0);
        ImpIntReadonly("End", (int)MathF.Round((float)(selectedClipInfo.DurationSeconds * 30.0)));
        ImpCheck("Loop Time", () => s.LoopTime, v => s.LoopTime = v);
        ImpCheck("Loop Pose", () => s.LoopPose, v => s.LoopPose = v);
        ImpFloat("Cycle Offset", () => s.CycleOffset, v => s.CycleOffset = v);

        ImGui.Dummy(new System.Numerics.Vector2(0f, 4f));
        ImGui.TextDisabled("Root Transform Rotation");
        ImpCheck("Bake Into Pose", () => s.BakeRootRotationIntoPose, v => s.BakeRootRotationIntoPose = v);
        ImpCombo("Based Upon", new[] { "Body Orientation", "Original" }, () => s.RootRotationBasedUpon, v => s.RootRotationBasedUpon = v);
        ImpFloat("Offset", () => s.RootRotationOffset, v => s.RootRotationOffset = v);

        ImGui.Dummy(new System.Numerics.Vector2(0f, 4f));
        ImGui.TextDisabled("Root Transform Position (Y)");
        ImpCheck("Bake Into Pose##RootY", () => s.BakeRootPositionYIntoPose, v => s.BakeRootPositionYIntoPose = v);
        ImpCombo("Based Upon##RootY", new[] { "Original", "Center Of Mass", "Feet" }, () => s.RootPositionYBasedUpon, v => s.RootPositionYBasedUpon = v);
        ImpFloat("Offset##RootY", () => s.RootPositionYOffset, v => s.RootPositionYOffset = v);

        ImGui.Dummy(new System.Numerics.Vector2(0f, 4f));
        ImGui.TextDisabled("Root Transform Position (XZ)");
        ImpCheck("Bake Into Pose##RootXZ", () => s.BakeRootPositionXZIntoPose, v => s.BakeRootPositionXZIntoPose = v);
        ImpCombo("Based Upon##RootXZ", new[] { "Center Of Mass", "Original" }, () => s.RootPositionXZBasedUpon, v => s.RootPositionXZBasedUpon = v);
        ImpCheck("Mirror", () => s.Mirror, v => s.Mirror = v);
        ImpCheck("Additive Reference Pose", () => s.AdditiveReferencePose, v => s.AdditiveReferencePose = v);

        ImGui.Dummy(new System.Numerics.Vector2(0f, 6f));
        if (ImGui.Button("Extract Clip", new System.Numerics.Vector2(-1f, 0f)))
        {
            ModelImportSettingsAsset.Save(modelImportPath, s);
            ExtractImportedAnimationClip(modelImportPath, selectedImportClipIndex, selectCreated: true);
        }
    }

    private void DrawAnimationTab(ParsedMesh mesh)
    {
        ImGui.Dummy(new System.Numerics.Vector2(0f, 2f));
        if (mesh == null || mesh.Animations.Count == 0)
        {
            ImGui.TextDisabled("Sin animaciones embebidas.");
            return;
        }
        ImGui.TextDisabled($"Clips embebidos: {mesh.Animations.Count}");
        ImGui.Separator();
        foreach (var a in mesh.Animations)
            ImGui.BulletText($"{a.Name}  —  {a.DurationSeconds:0.00}s, {a.ChannelCount} huesos");
    }

    private void DrawMaterialsTab(ParsedMesh mesh)
    {
        ImGui.Dummy(new System.Numerics.Vector2(0f, 2f));
        if (mesh == null) { ImGui.TextDisabled("Modelo no disponible."); return; }
        int subs = mesh.Submeshes.Count == 0 ? 1 : mesh.Submeshes.Count;
        ImGui.TextDisabled($"Materiales / sub-mallas: {subs}");
        ImGui.Separator();
        if (mesh.Submeshes.Count == 0)
            ImGui.BulletText("Material 1");
        else
            foreach (var sm in mesh.Submeshes)
                ImGui.BulletText(string.IsNullOrWhiteSpace(sm.Name) ? "Material" : sm.Name);
    }

    private static int CountNodes(ModelNode? n)
    {
        if (n == null) return 0;
        int c = 0;
        foreach (var ch in n.Children) c += 1 + CountNodes(ch);
        return c;
    }

    // ── Helpers de fila para el panel de import (label izquierda + control derecha) ──
    private static void ImpCheck(string label, Func<bool> get, Action<bool> set, string hint = "")
    {
        FieldRow(label);
        bool v = get();
        if (ImGui.Checkbox("##" + label, ref v)) set(v);
        if (!string.IsNullOrEmpty(hint)) { ImGui.SameLine(); ImGui.TextDisabled(hint); }
    }

    private static void ImpFloat(string label, Func<float> get, Action<float> set)
    {
        FieldRow(label);
        float v = get();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputFloat("##" + label, ref v)) set(v);
    }

    private static void ImpIntReadonly(string label, int value)
    {
        FieldRow(label);
        ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputInt("##" + label, ref value);
        ImGui.EndDisabled();
    }

    private static void ImpCombo(string label, string[] opts, Func<string> get, Action<string> set)
    {
        FieldRow(label);
        int i = Array.IndexOf(opts, get());
        if (i < 0) i = 0;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##" + label, ref i, opts, opts.Length)) set(opts[i]);
    }

    private static void DrawPrefabAssetInspector(string path)
    {
        ImGui.TextDisabled("Prefab Asset");
        try
        {
            string text = File.ReadAllText(path);
            ImGui.Text($"Size: {text.Length / 1024f:F1} KB");
            ImGui.Text($"Objects: {text.Split(new[] { "\"Name\"" }, StringSplitOptions.None).Length - 1}");
        }
        catch
        {
            ImGui.TextWrapped("Prefab metadata unavailable.");
        }
    }

    private void DrawTransform(GameObject obj)
    {
        // Objetos de UI: mostrar "Rect Transform" en lugar del Transform normal (como Unity).
        UIElement? uiEl = null;
        foreach (var c in obj.Components)
            if (c is UIElement e) { uiEl = e; break; }
        if (uiEl != null) { DrawRectTransformFrame(obj, uiEl); return; }
        if (obj.GetComponent<Canvas>() is { } canvas) { DrawCanvasRectTransformFrame(obj, canvas); return; }

        if (!DrawComponentFrame("Transform", "Transform:" + obj.EditorId, null, false, false, out _, out _))
            return;

        float px = obj.PosX, py = obj.PosY, pz = obj.PosZ;
        if (DrawAxisFloat3("Position", ref px, ref py, ref pz, 0.05f, -100000f, 100000f))
        {
            BeginInspectorTransformEdit(obj);
            obj.PosX = px; obj.PosY = py; obj.PosZ = pz;
            NotifyObjectTransformChanged(obj);
        }
        EndInspectorTransformEdit(obj);

        float rx = obj.RotX, ry = obj.RotY, rz = obj.RotZ;
        if (DrawAxisFloat3("Rotation", ref rx, ref ry, ref rz, 0.5f, -36000f, 36000f))
        {
            BeginInspectorTransformEdit(obj);
            obj.RotX = rx; obj.RotY = ry; obj.RotZ = rz;
            NotifyObjectTransformChanged(obj);
        }
        EndInspectorTransformEdit(obj);

        float sx = obj.ScaleX, sy = obj.ScaleY, sz = obj.ScaleZ;
        if (DrawAxisFloat3("Scale", ref sx, ref sy, ref sz, 0.05f, 0.001f, 1000f))
        {
            BeginInspectorTransformEdit(obj);
            obj.ScaleX = sx; obj.ScaleY = sy; obj.ScaleZ = sz;
            NotifyObjectTransformChanged(obj);
        }
        EndInspectorTransformEdit(obj);
    }

    // "Rect Transform" para objetos de UI (Image/Text/Health Bar): reemplaza al Transform normal, como Unity.
    private void DrawRectTransformFrame(GameObject obj, UIElement el)
    {
        if (!DrawComponentFrame("Rect Transform", "RectTransform:" + obj.EditorId, null, false, false, out _, out _))
            return;
        DrawRectTransformSection(el);
    }

    // "Rect Transform" del Canvas: ahora editable (como Unity).
    private void DrawCanvasRectTransformFrame(GameObject obj, Canvas canvas)
    {
        if (!DrawComponentFrame("Rect Transform", "RectTransform:" + obj.EditorId, null, false, false, out _, out _))
            return;

        // Posición del Canvas en pantalla
        DrawVec2Row("Position", canvas.PosX, canvas.PosY, 1f, -100000f, 100000f, (x, y) => { canvas.PosX = x; canvas.PosY = y; });
        // Tamaño del Canvas
        DrawVec2Row("Width / Height", canvas.Width, canvas.Height, 1f, 1f, 100000f, (x, y) => { canvas.Width = x; canvas.Height = y; });
        // Pivote del Canvas
        DrawVec2Row("Pivot", canvas.PivotX, canvas.PivotY, 0.01f, 0f, 1f, (x, y) => { canvas.PivotX = x; canvas.PivotY = y; });
    }

    private bool DrawComponentFrame(string title, string stateId, Component? component, bool removable, bool scriptComponent, out bool menuRequested, out bool openScriptRequested)
    {
        menuRequested = false;
        openScriptRequested = false;
        EditorIcon icon = GetComponentIcon(title);
        if (!componentFoldoutStates.TryGetValue(stateId, out bool open))
            open = true;

        ImGui.PushID(stateId);
        float rowH = 21f;
        float avail = Math.Max(120f, ImGui.GetContentRegionAvail().X);
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        Vector2 rowMax = rowMin + new Vector2(avail, rowH);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.185f, 0.185f, 0.190f, 1f)));
        drawList.AddLine(rowMin, rowMin + new Vector2(avail, 0f), ImGui.GetColorU32(new System.Numerics.Vector4(0.245f, 0.245f, 0.250f, 1f)));
        drawList.AddLine(rowMin + new Vector2(0f, rowH - 1f), rowMax - new Vector2(0f, 1f), ImGui.GetColorU32(new System.Numerics.Vector4(0.105f, 0.105f, 0.110f, 1f)));

        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.26f, 0.26f, 0.27f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.30f, 0.30f, 0.31f, 0.95f));
        ImGui.SetCursorScreenPos(rowMin + new Vector2(2f, 2f));
        if (ImGui.Button(open ? "v##fold" : ">##fold", new Vector2(17f, 18f)))
        {
            open = !open;
            componentFoldoutStates[stateId] = open;
        }
        DrawTooltip(open ? "Collapse component" : "Expand component");
        ImGui.PopStyleColor(3);

        float x = rowMin.X + 22f;
        if (component != null && TryGetComponentEnabled(component, out bool enabled))
        {
            ImGui.SetCursorScreenPos(new Vector2(x, rowMin.Y + 2f));
            if (SmallCheckbox("##ComponentEnabled", ref enabled))
                SetComponentEnabled(component, enabled);
            DrawTooltip(enabled ? "Component enabled" : "Component disabled");
            x += 18f;
        }

        if (title.Contains("Rigidbody", StringComparison.OrdinalIgnoreCase) && TryGetRigidbodyIconTexture(out var rigidbodyTextureId))
        {
            Vector2 iconMin = new Vector2(x, rowMin.Y + 4f);
            drawList.AddImage(rigidbodyTextureId, iconMin, iconMin + new Vector2(14f, 14f));
        }
        else
        {
            DrawAtlasIconOrFallback(drawList, icon, new Vector2(x, rowMin.Y + 4f), 14f, ImGui.GetColorU32(UiIcon));
        }
        x += 20f;

        float rightReserve = (scriptComponent ? 52f : 0f) + (removable ? 28f : 0f) + 8f;
        float textW = Math.Max(36f, rowMax.X - x - rightReserve);
        string displayTitle = Ellipsize(title, Math.Max(4, (int)(textW / 7.2f)));
        drawList.AddText(new Vector2(x, rowMin.Y + 3f), ImGui.GetColorU32(UiText), displayTitle);

        if (scriptComponent)
        {
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - rightReserve + 2f, rowMin.Y + 2f));
            if (ImGui.SmallButton("Open"))
                openScriptRequested = true;
            DrawTooltip("Open script");
        }

        if (removable)
        {
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - 25f, rowMin.Y + 2f));
            if (ImGui.SmallButton("..."))
                menuRequested = true;
            DrawTooltip("Component options");
        }

        bool headerHovered = ImGui.IsMouseHoveringRect(rowMin, rowMax);
        if (headerHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (scriptComponent)
                openScriptRequested = true;
            else
            {
                open = !open;
                componentFoldoutStates[stateId] = open;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMin.Y + rowH + 2f));
        ImGui.PopID();
        return open;
    }

    private static EditorIcon GetComponentIcon(string title)
    {
        if (title.Contains("Transform", StringComparison.OrdinalIgnoreCase)) return EditorIcon.Transform;
        if (title.Contains("Camera", StringComparison.OrdinalIgnoreCase)) return EditorIcon.Camera;
        if (title.Contains("Light", StringComparison.OrdinalIgnoreCase)) return EditorIcon.Light;
        if (title.Contains("Mesh", StringComparison.OrdinalIgnoreCase)) return EditorIcon.Mesh;
        if (title.Contains("Material", StringComparison.OrdinalIgnoreCase)) return EditorIcon.Prefab;
        if (title.Contains("Script", StringComparison.OrdinalIgnoreCase) || title.Contains("Mono", StringComparison.OrdinalIgnoreCase)) return EditorIcon.Script;
        return EditorIcon.Asset;
    }

    private static bool DrawAxisFloat3(string label, ref float x, ref float y, ref float z, float speed, float min, float max)
    {
        bool changed = false;
        FieldRow(label);
        ImGui.PushID(label);
        float available = Math.Max(72f, ImGui.GetContentRegionAvail().X);
        float width = Math.Max(24f, (available - 8f) / 3f);
        changed |= DrawAxisFloat("X", ref x, speed, min, max, width, new System.Numerics.Vector4(0.74f, 0.74f, 0.74f, 1f));
        ImGui.SameLine(0f, 4f);
        changed |= DrawAxisFloat("Y", ref y, speed, min, max, width, new System.Numerics.Vector4(0.74f, 0.74f, 0.74f, 1f));
        ImGui.SameLine(0f, 4f);
        changed |= DrawAxisFloat("Z", ref z, speed, min, max, width, new System.Numerics.Vector4(0.74f, 0.74f, 0.74f, 1f));
        ImGui.PopID();
        return changed;
    }

    private static bool DrawAxisFloat(string axis, ref float value, float speed, float min, float max, float width, System.Numerics.Vector4 color)
    {
        ImGui.PushID(axis);
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.28f, 0.28f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.34f, 0.34f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.74f, 0.74f, 0.74f, 1f));
        ImGui.Button(axis, new Vector2(18f, 0f));
        ImGui.PopStyleColor(4);
        ImGui.SameLine(0f, 2f);
        ImGui.SetNextItemWidth(Math.Max(20f, width - 20f));
        bool changed = ImGui.DragFloat("##value", ref value, speed, min, max);
        ImGui.PopID();
        return changed;
    }

    private void BeginInspectorTransformEdit(GameObject obj)
    {
        if (isPlaying || suppressHistory) return;
        if (pendingInspectorUndoStart.HasValue && pendingInspectorUndoObjectId == obj.EditorId) return;

        pendingInspectorUndoStart = CaptureSceneState();
        pendingInspectorUndoObjectId = obj.EditorId;
    }

    private void EndInspectorTransformEdit(GameObject obj)
    {
        if (!pendingInspectorUndoStart.HasValue || pendingInspectorUndoObjectId != obj.EditorId) return;
        if (!ImGui.IsItemDeactivatedAfterEdit()) return;

        PushSceneState("Transform " + obj.Name, pendingInspectorUndoStart.Value, CaptureSceneState());
        pendingInspectorUndoStart = null;
        pendingInspectorUndoObjectId = null;
    }

    private void NotifyObjectTransformChanged(GameObject obj)
    {
        if (obj.IsStatic)
            sceneRenderer.InvalidateStaticBatch();
        sceneRenderer.InvalidateCullingState();
    }

    private void BeginNameEdit(GameObject obj)
    {
        if (isPlaying || suppressHistory) return;
        if (pendingNameUndoStart.HasValue && pendingNameUndoObjectId == obj.EditorId) return;

        pendingNameUndoStart = CaptureSceneState();
        pendingNameUndoObjectId = obj.EditorId;
    }

    private void EndNameEdit(GameObject obj)
    {
        if (!pendingNameUndoStart.HasValue || pendingNameUndoObjectId != obj.EditorId) return;
        if (!ImGui.IsItemDeactivatedAfterEdit()) return;

        PushSceneState("Rename " + obj.Name, pendingNameUndoStart.Value, CaptureSceneState());
        pendingNameUndoStart = null;
        pendingNameUndoObjectId = null;
    }

    private void BeginRenameObject(GameObject obj)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot rename while playing";
            return;
        }

        selected = obj;
        selection.SelectSingle(obj);
        inlineRenameObjectId = obj.EditorId;
        inlineRenameObjectName = obj.Name;
        inlineRenameObjectFocusPending = true;
    }

    private void CommitInlineRenameObject(GameObject obj)
    {
        inlineRenameObjectId = null;
        renameObjectName = inlineRenameObjectName;
        RenameObject(obj);
        inlineRenameObjectName = string.Empty;
    }

    private void CancelInlineRenameObject()
    {
        inlineRenameObjectId = null;
        inlineRenameObjectName = string.Empty;
    }

    private void RenameObject(GameObject obj)
    {
        string desired = string.IsNullOrWhiteSpace(renameObjectName) ? obj.Name : renameObjectName.Trim();
        string finalName = GetUniqueSiblingObjectName(obj, desired);
        if (string.Equals(obj.Name, finalName, StringComparison.Ordinal))
        {
            renameObjectName = string.Empty;
            return;
        }

        string oldName = obj.Name;
        CommitSceneMutation("Rename " + oldName, () =>
        {
            obj.Name = finalName;
            selected = obj;
            statusMessage = $"Renamed {oldName} to {finalName}";
            return obj;
        });

        renameObjectName = string.Empty;
    }

    private void BeginMaterialEdit(GameObject obj)
    {
        if (isPlaying || suppressHistory) return;
        if (pendingMaterialUndoStart.HasValue && pendingMaterialUndoObjectId == obj.EditorId) return;

        pendingMaterialUndoStart = CaptureSceneState();
        pendingMaterialUndoObjectId = obj.EditorId;
    }

    private void EndMaterialEdit(GameObject obj, Material? mat = null)
    {
        if (!pendingMaterialUndoStart.HasValue || pendingMaterialUndoObjectId != obj.EditorId) return;
        if (!ImGui.IsItemDeactivatedAfterEdit()) return;

        PushSceneState("Edit Material " + obj.Name, pendingMaterialUndoStart.Value, CaptureSceneState());
        pendingMaterialUndoStart = null;
        pendingMaterialUndoObjectId = null;

        SaveMaterialComponentToAsset(mat);
    }

    /// <summary>Vuelca los valores actuales del componente Material al .mat del que proviene, propagando el cambio a los demás objetos que lo comparten.</summary>
    private void SaveMaterialComponentToAsset(Material? mat)
    {
        if (mat != null && !string.IsNullOrWhiteSpace(mat.AssetPath) && File.Exists(mat.AssetPath))
            QueueMaterialAssetSave(mat.AssetPath, MaterialDataFromComponent(mat));
    }

    // Header de sección colapsable (estilo Unity: barra gris, abierto por defecto).
    private static bool LegacySectionHeader(string title)
    {
        ImGui.PushStyleColor(ImGuiCol.Header, new System.Numerics.Vector4(0.17f, 0.17f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new System.Numerics.Vector4(0.22f, 0.24f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new System.Numerics.Vector4(0.20f, 0.22f, 0.25f, 1f));
        bool open = ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);
        return open;
    }

    private bool SectionHeader(string title)
    {
        string key = ImGui.GetID(title).ToString();
        if (!sectionFoldoutStates.TryGetValue(key, out bool open))
            open = true;

        float width = Math.Max(80f, ImGui.GetContentRegionAvail().X);
        float rowH = 22f;
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 max = min + new Vector2(width, rowH);
        var drawList = ImGui.GetWindowDrawList();
        bool hovered = ImGui.IsMouseHoveringRect(min, max);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(hovered
            ? new System.Numerics.Vector4(0.205f, 0.205f, 0.210f, 1f)
            : new System.Numerics.Vector4(0.155f, 0.155f, 0.160f, 1f)));
        drawList.AddLine(max - new Vector2(width, 1f), max - new Vector2(0f, 1f), ImGui.GetColorU32(new System.Numerics.Vector4(0.220f, 0.220f, 0.225f, 0.75f)));
        drawList.AddText(min + new Vector2(6f, 3f), ImGui.GetColorU32(new System.Numerics.Vector4(0.82f, 0.82f, 0.82f, 1f)), open ? "v" : ">");
        drawList.AddText(min + new Vector2(24f, 3f), ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.72f, 0.72f, 1f)), title);

        ImGui.InvisibleButton("##sectionHeader" + title, new Vector2(width, rowH));
        if (ImGui.IsItemClicked())
        {
            open = !open;
            sectionFoldoutStates[key] = open;
        }

        return open;
    }

    // Swatch de color ancho (estilo Unity): clic abre el selector de color con barras.
    // Sliders R/G/B en 0-255, compartidos por los selectores de color "normal" y "HDR".
    private static bool DrawRgb255Sliders(string id, ref System.Numerics.Vector3 col)
    {
        bool changed = false;
        int r = (int)MathF.Round(Math.Clamp(col.X, 0f, 1f) * 255f);
        int g = (int)MathF.Round(Math.Clamp(col.Y, 0f, 1f) * 255f);
        int b = (int)MathF.Round(Math.Clamp(col.Z, 0f, 1f) * 255f);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt(id + "##r255", ref r, 0, 255, "R %d")) { col.X = r / 255f; changed = true; }
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt(id + "##g255", ref g, 0, 255, "G %d")) { col.Y = g / 255f; changed = true; }
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt(id + "##b255", ref b, 0, 255, "B %d")) { col.Z = b / 255f; changed = true; }
        return changed;
    }

    // Campo de color hexadecimal (RRGGBB), compartido por los selectores de color.
    private static bool DrawHexField3(string id, ref System.Numerics.Vector3 col)
    {
        bool changed = false;
        string hex = $"{(int)MathF.Round(Math.Clamp(col.X, 0f, 1f) * 255f):X2}" +
                      $"{(int)MathF.Round(Math.Clamp(col.Y, 0f, 1f) * 255f):X2}" +
                      $"{(int)MathF.Round(Math.Clamp(col.Z, 0f, 1f) * 255f):X2}";
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Hex");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        if (ImGui.InputText(id + "##hex3", ref hex, 9, ImGuiInputTextFlags.CharsHexadecimal))
        {
            var clean = hex.TrimStart('#').Trim();
            if (clean.Length == 6
                && int.TryParse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rr)
                && int.TryParse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int gg)
                && int.TryParse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bb))
            {
                col = new System.Numerics.Vector3(rr / 255f, gg / 255f, bb / 255f);
                changed = true;
            }
        }
        return changed;
    }

    private static bool ColorField(string id, ref System.Numerics.Vector3 col)
    {
        bool changed = false;
        float w = Math.Max(40f, ImGui.GetContentRegionAvail().X - 8f);   // margen derecho
        var preview = new System.Numerics.Vector4(col.X, col.Y, col.Z, 1f);
        if (ImGui.ColorButton(id, preview, ImGuiColorEditFlags.None, new Vector2(w, 20f)))
            ImGui.OpenPopup(id + "pick");
        if (ImGui.BeginPopup(id + "pick"))
        {
            ImGui.TextDisabled("Color");
            ImGui.Separator();
            if (ImGui.ColorPicker3(id + "wheel", ref col,
                    ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs))
                changed = true;
            ImGui.Dummy(new Vector2(0f, 2f));
            if (DrawRgb255Sliders(id, ref col)) changed = true;
            if (DrawHexField3(id, ref col)) changed = true;
            ImGui.EndPopup();
        }
        return changed;
    }

    private static bool ColorField4(string id, ref System.Numerics.Vector4 col)
    {
        bool changed = false;
        float w = Math.Max(40f, ImGui.GetContentRegionAvail().X - 8f);   // margen derecho
        if (ImGui.ColorButton(id, col, ImGuiColorEditFlags.AlphaPreviewHalf, new Vector2(w, 20f)))
            ImGui.OpenPopup(id + "pick");
        if (ImGui.BeginPopup(id + "pick"))
        {
            ImGui.TextDisabled("Color");
            ImGui.Separator();
            if (ImGui.ColorPicker4(id + "wheel", ref col,
                    ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs))
                changed = true;

            var rgb = new System.Numerics.Vector3(col.X, col.Y, col.Z);
            ImGui.Dummy(new Vector2(0f, 2f));
            if (DrawRgb255Sliders(id, ref rgb)) { col.X = rgb.X; col.Y = rgb.Y; col.Z = rgb.Z; changed = true; }

            int a = (int)MathF.Round(Math.Clamp(col.W, 0f, 1f) * 255f);
            ImGui.SetNextItemWidth(220f);
            if (ImGui.SliderInt(id + "##a255", ref a, 0, 255, "A %d")) { col.W = a / 255f; changed = true; }

            if (DrawHexField3(id, ref rgb)) { col.X = rgb.X; col.Y = rgb.Y; col.Z = rgb.Z; changed = true; }
            ImGui.EndPopup();
        }
        return changed;
    }

    // Paleta de swatches rápidos para el HDR Color picker.
    private static readonly System.Numerics.Vector3[] HdrSwatches =
    {
        new(1f, 1f, 1f),     new(0f, 0f, 0f),     new(1f, 0f, 0f),     new(0f, 1f, 0f),     new(0f, 0.45f, 1f),
        new(1f, 1f, 0f),     new(0f, 1f, 1f),     new(1f, 0f, 1f),     new(1f, 0.5f, 0f),   new(0.55f, 0f, 1f),
    };

    // Botones de exposición rápida (en stops, x2 por cada +1) para el HDR Color picker.
    private static readonly float[] HdrExposureStops = { -2f, -1f, 0f, 1f, 2f };

    private static bool DrawExposurePresets(string id, ref float intensity)
    {
        bool changed = false;
        for (int i = 0; i < HdrExposureStops.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            float stop = HdrExposureStops[i];
            float value = MathF.Pow(2f, stop);
            bool isCurrent = MathF.Abs(intensity - value) < 0.01f;
            string label = stop == 0f ? "1x" : (stop > 0f ? $"+{stop:0}" : $"{stop:0}");
            if (isCurrent) ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.26f, 0.59f, 0.98f, 0.8f));
            if (ImGui.Button(label + "##exp" + i, new Vector2(40f, 0f)))
            {
                intensity = value;
                changed = true;
            }
            if (isCurrent) ImGui.PopStyleColor();
        }
        return changed;
    }

    // Selector de color HDR estilo Unity: rueda de color + sliders RGB + slider de
    // Intensidad (multiplicador HDR) + swatches. El color base se guarda en 'col'
    // (0..1) y la energía extra en 'intensity'; el color final emitido = col * intensity.
    private static bool HdrColorField(string id, ref System.Numerics.Vector3 col, ref float intensity)
    {
        bool changed = false;
        float w = Math.Max(40f, ImGui.GetContentRegionAvail().X - 8f);

        // Preview: color final (base * intensidad), recortado a [0,1] para mostrar.
        float mul = Math.Max(0f, intensity);
        var finalPreview = new System.Numerics.Vector4(
            Math.Min(col.X * mul, 1f), Math.Min(col.Y * mul, 1f), Math.Min(col.Z * mul, 1f), 1f);

        if (ImGui.ColorButton(id, finalPreview, ImGuiColorEditFlags.None, new Vector2(w, 20f)))
            ImGui.OpenPopup(id + "hdr");

        if (ImGui.BeginPopup(id + "hdr"))
        {
            ImGui.TextDisabled("HDR Color");
            ImGui.Separator();

            if (ImGui.ColorPicker3(id + "wheel", ref col,
                    ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs))
                changed = true;

            ImGui.Dummy(new Vector2(0f, 2f));
            if (DrawRgb255Sliders(id, ref col)) changed = true;
            if (DrawHexField3(id, ref col)) changed = true;

            ImGui.Dummy(new Vector2(0f, 4f));
            ImGui.SetNextItemWidth(w > 0f ? 240f : 200f);
            if (ImGui.DragFloat("Intensity", ref intensity, 0.01f, 0f, 100f, "%.2f x"))
            {
                if (intensity < 0f) intensity = 0f;
                changed = true;
            }
            if (DrawExposurePresets(id, ref intensity)) changed = true;

            ImGui.Dummy(new Vector2(0f, 2f));
            ImGui.TextDisabled("Swatches");
            for (int i = 0; i < HdrSwatches.Length; i++)
            {
                if (i % 5 != 0) ImGui.SameLine();
                var s = HdrSwatches[i];
                if (ImGui.ColorButton(id + "sw" + i, new System.Numerics.Vector4(s.X, s.Y, s.Z, 1f),
                        ImGuiColorEditFlags.None, new Vector2(24f, 24f)))
                {
                    col = s;
                    changed = true;
                }
            }
            ImGui.EndPopup();
        }
        return changed;
    }

    private MaterialAssetData GetEditableMaterialAsset(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return pendingMaterialSaves.TryGetValue(fullPath, out var pending)
            ? CloneMaterialData(pending.Data)
            : MaterialAsset.Load(fullPath);
    }

    private void QueueMaterialAssetSave(string path, MaterialAssetData data)
    {
        string fullPath = Path.GetFullPath(path);
        var draft = CloneMaterialData(data);
        ApplyMaterialAssetLive(fullPath, draft);
        pendingMaterialSaves[fullPath] = new PendingMaterialSave(draft, DateTime.UtcNow + MaterialSaveDelay);
        statusMessageValue = "Material updated live";
    }

    private void ProcessPendingMaterialSaves()
    {
        if (pendingMaterialSaves.Count == 0) return;

        var now = DateTime.UtcNow;
        List<string>? ready = null;
        foreach (var pair in pendingMaterialSaves)
        {
            if (pair.Value.SaveAfterUtc > now) continue;
            ready ??= new List<string>();
            ready.Add(pair.Key);
        }

        if (ready == null) return;
        foreach (string path in ready)
        {
            if (!pendingMaterialSaves.TryGetValue(path, out var pending)) continue;
            MaterialAsset.Save(path, CloneMaterialData(pending.Data));
            pendingMaterialSaves.Remove(path);
            InvalidateAssetPreview(path, deleteTexture: true);
            statusMessageValue = "Material asset saved";
        }
    }

    private void FlushPendingMaterialSaves()
    {
        if (pendingMaterialSaves.Count == 0) return;

        foreach (var pair in pendingMaterialSaves.ToArray())
        {
            MaterialAsset.Save(pair.Key, CloneMaterialData(pair.Value.Data));
            InvalidateAssetPreview(pair.Key, deleteTexture: true);
        }
        pendingMaterialSaves.Clear();
    }

    private void ApplyMaterialAssetLive(string path, MaterialAssetData data)
    {
        bool changedAny = false;
        foreach (var obj in EnumerateObjects(objects))
        {
            var mat = obj.GetComponent<Material>();
            if (mat == null || mat.IsInstance) continue;
            if (!SameAssetPath(mat.AssetPath, path)) continue;
            ApplyMaterialDataToComponent(mat, path, data, sharedAsset: true);
            changedAny = true;
        }

        if (changedAny)
            sceneRenderer.InvalidateStaticBatch();
    }

    private static void ApplyMaterialDataToComponent(Material mat, string assetPath, MaterialAssetData data, bool sharedAsset)
    {
        mat.R = Clamp01(data.R);
        mat.G = Clamp01(data.G);
        mat.B = Clamp01(data.B);
        mat.AssetPath = assetPath;
        mat.TexturePath = MaterialAsset.GetAlbedo(data);
        mat.NormalMapPath = data.NormalMapPath;
        mat.RoughnessMapPath = data.RoughnessMapPath;
        mat.MetallicMapPath = data.MetallicMapPath;
        mat.ShaderGraphPath = data.ShaderGraphPath;
        mat.ShaderGraphProperties = new Dictionary<string, float[]>(data.ShaderGraphProperties, StringComparer.OrdinalIgnoreCase);
        mat.ShaderGraphTextures = new Dictionary<string, string>(data.ShaderGraphTextures, StringComparer.OrdinalIgnoreCase);
        mat.Roughness = Clamp01(data.Roughness);
        mat.Metallic = Clamp01(data.Metallic);
        mat.EmissionR = Clamp01(data.EmissionR);
        mat.EmissionG = Clamp01(data.EmissionG);
        mat.EmissionB = Clamp01(data.EmissionB);
        mat.EmissionIntensity = Math.Max(0f, data.EmissionIntensity);
        mat.IsInstance = !sharedAsset;
    }

    /// <summary>Dibuja en el Inspector un control editable por cada propiedad expuesta del Shader Graph asignado al material, como las "Material Properties" de Unity.</summary>
    private void DrawShaderGraphPropertyFields(GameObject obj, Material mat)
    {
        if (string.IsNullOrWhiteSpace(mat.ShaderGraphPath)) return;

        string? sgPath = SceneViewportRenderer.NormalizeExistingAssetPath(mat.ShaderGraphPath);
        if (sgPath == null) return;

        var entry = sceneRenderer.GetShaderGraphEntry(sgPath);
        if (entry == null || entry.ExposedProperties.Count == 0) return;

        if (!SectionHeader("Exposed Properties")) return;

        if (ImGui.SmallButton("Reset To Graph Defaults"))
        {
            CommitSceneMutation("Reset Shader Properties", () =>
            {
                mat.ShaderGraphProperties.Clear();
                mat.ShaderGraphTextures.Clear();
                sceneRenderer.InvalidateStaticBatch();
                return mat;
            });
            SaveMaterialComponentToAsset(mat);
        }
        ImGui.Separator();

        foreach (var kv in entry.ExposedProperties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string uniform = kv.Key;
            var info = kv.Value;
            string label = info.DisplayName;

            if (info.Type == PinType.Texture2D)
            {
                mat.ShaderGraphTextures.TryGetValue(uniform, out var texPath);
                texPath ??= string.Empty;
                DrawTextureMapSlot(label, texPath, "Drop texture", path =>
                {
                    if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                    {
                        CommitSceneMutation("Edit Shader Texture", () =>
                        {
                            mat.ShaderGraphTextures[uniform] = path;
                            sceneRenderer.InvalidateStaticBatch();
                            return mat;
                        });
                        SaveMaterialComponentToAsset(mat);
                    }
                });
                continue;
            }

            if (!entry.PropertyDefaults.TryGetValue(uniform, out var defaults)) continue;
            float[] current = mat.ShaderGraphProperties.TryGetValue(uniform, out var cur) && cur.Length == defaults.Length
                ? cur
                : defaults;
            bool changed = false;
            float[] next = (float[])current.Clone();

            switch (defaults.Length)
            {
                case 1:
                {
                    FieldRow(label);
                    float f = current[0];
                    if (ImGui.DragFloat("##sg_" + uniform, ref f, 0.01f))
                    {
                        next[0] = f;
                        changed = true;
                    }
                    break;
                }
                case 2:
                {
                    FieldRow(label);
                    var v2 = new System.Numerics.Vector2(current[0], current[1]);
                    if (ImGui.DragFloat2("##sg_" + uniform, ref v2, 0.01f))
                    {
                        next[0] = v2.X; next[1] = v2.Y;
                        changed = true;
                    }
                    break;
                }
                case 3:
                {
                    FieldRow(label);
                    var v3 = new System.Numerics.Vector3(current[0], current[1], current[2]);
                    if (ImGui.DragFloat3("##sg_" + uniform, ref v3, 0.01f))
                    {
                        next[0] = v3.X; next[1] = v3.Y; next[2] = v3.Z;
                        changed = true;
                    }
                    break;
                }
                case 4 when info.IsHdr && info.Type == PinType.Vec3:
                {
                    // Color HDR (vec3): [r, g, b, intensity]
                    FieldRow(label);
                    var rgb = new System.Numerics.Vector3(current[0], current[1], current[2]);
                    var intensity = current[3];
                    if (HdrColorField("##sg_" + uniform, ref rgb, ref intensity))
                    {
                        next[0] = rgb.X; next[1] = rgb.Y; next[2] = rgb.Z; next[3] = intensity;
                        changed = true;
                    }
                    break;
                }
                case 4:
                {
                    FieldRow(label);
                    var v4 = new System.Numerics.Vector4(current[0], current[1], current[2], current[3]);
                    if (ColorField4("##sg_" + uniform, ref v4))
                    {
                        next[0] = v4.X; next[1] = v4.Y; next[2] = v4.Z; next[3] = v4.W;
                        changed = true;
                    }
                    break;
                }
                case 5:
                {
                    // Color HDR (vec4): [r, g, b, a, intensity]
                    FieldRow(label);
                    var rgb = new System.Numerics.Vector3(current[0], current[1], current[2]);
                    var intensity = current[4];
                    if (HdrColorField("##sg_" + uniform, ref rgb, ref intensity))
                    {
                        next[0] = rgb.X; next[1] = rgb.Y; next[2] = rgb.Z; next[4] = intensity;
                        changed = true;
                    }
                    break;
                }
            }

            if (changed)
            {
                BeginMaterialEdit(obj);
                mat.ShaderGraphProperties[uniform] = next;
                sceneRenderer.InvalidateStaticBatch();
                SaveMaterialComponentToAsset(mat);
            }
            EndMaterialEdit(obj, mat);
        }

        ImGui.Dummy(new Vector2(0f, 3f));
    }

    private static MaterialAssetData MaterialDataFromComponent(Material mat) => new()
    {
        Name = string.IsNullOrWhiteSpace(mat.AssetPath) ? "Material Instance" : Path.GetFileNameWithoutExtension(mat.AssetPath),
        R = mat.R,
        G = mat.G,
        B = mat.B,
        TexturePath = mat.TexturePath,
        AlbedoPath = mat.TexturePath,
        NormalMapPath = mat.NormalMapPath,
        RoughnessMapPath = mat.RoughnessMapPath,
        MetallicMapPath = mat.MetallicMapPath,
        ShaderGraphPath = mat.ShaderGraphPath,
        ShaderGraphProperties = new Dictionary<string, float[]>(mat.ShaderGraphProperties, StringComparer.OrdinalIgnoreCase),
        ShaderGraphTextures = new Dictionary<string, string>(mat.ShaderGraphTextures, StringComparer.OrdinalIgnoreCase),
        Roughness = mat.Roughness,
        Metallic = mat.Metallic,
        EmissionR = mat.EmissionR,
        EmissionG = mat.EmissionG,
        EmissionB = mat.EmissionB,
        EmissionIntensity = mat.EmissionIntensity
    };

    private static MaterialAssetData CloneMaterialData(MaterialAssetData data) => new()
    {
        Name = data.Name,
        R = data.R,
        G = data.G,
        B = data.B,
        TexturePath = data.TexturePath,
        AlbedoPath = data.AlbedoPath,
        NormalMapPath = data.NormalMapPath,
        RoughnessMapPath = data.RoughnessMapPath,
        MetallicMapPath = data.MetallicMapPath,
        ShaderGraphPath = data.ShaderGraphPath,
        ShaderGraphProperties = new Dictionary<string, float[]>(data.ShaderGraphProperties, StringComparer.OrdinalIgnoreCase),
        ShaderGraphTextures = new Dictionary<string, string>(data.ShaderGraphTextures, StringComparer.OrdinalIgnoreCase),
        Roughness = data.Roughness,
        Metallic = data.Metallic,
        EmissionR = data.EmissionR,
        EmissionG = data.EmissionG,
        EmissionB = data.EmissionB,
        EmissionIntensity = data.EmissionIntensity
    };

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static bool SameAssetPath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<GameObject> EnumerateObjects(IEnumerable<GameObject> roots)
    {
        foreach (var obj in roots)
        {
            yield return obj;
            foreach (var child in EnumerateObjects(obj.Children))
                yield return child;
        }
    }

    private void MakeMaterialInstance(Material mat)
    {
        if (mat.IsInstance) return;
        mat.IsInstance = true;
        sceneRenderer.InvalidateStaticBatch();
        statusMessageValue = "Material instance created";
    }

    private void MakeMaterialInstanceIfShared(Material mat)
    {
        if (!mat.IsInstance && !string.IsNullOrWhiteSpace(mat.AssetPath))
            MakeMaterialInstance(mat);
    }

    private void DrawMaterialAssetInspector(string path)
    {
        var data = GetEditableMaterialAsset(path);
        bool changed = false;

        ImGui.TextDisabled("Material Asset");
        ImGui.TextWrapped(Path.GetFileName(path));
        ImGui.Spacing();

        string name = data.Name;
        FieldRow("Name");
        if (ImGui.InputText("##matname", ref name, 128)) { data.Name = name; changed = true; }
        ImGui.Spacing();

        DrawAssetSlot("Shader", data.ShaderGraphPath, "Standard", sgPath =>
        {
            if (string.IsNullOrWhiteSpace(sgPath) || sgPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
            {
                data.ShaderGraphPath = sgPath;
                QueueMaterialAssetSave(path, data);
            }
        }, p => p.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase));

        bool hasShaderGraph = !string.IsNullOrWhiteSpace(data.ShaderGraphPath);
        if (hasShaderGraph)
        {
            FieldRow("");
            if (ImGui.Button("Edit...##matasset_shader_edit", new Vector2(-1f, 22f)))
            {
                string? sgFull = SceneViewportRenderer.NormalizeExistingAssetPath(data.ShaderGraphPath);
                if (sgFull != null) OpenShaderGraphAsset(sgFull);
            }
        }

        if (DrawShaderGraphAssetPropertyFields(path, data))
            changed = true;

        if (!hasShaderGraph && SectionHeader("Surface Inputs"))
        {
            var color = new System.Numerics.Vector3(data.R, data.G, data.B);
            FieldRow("Base Color");
            if (ColorField("##base", ref color))
            {
                data.R = color.X; data.G = color.Y; data.B = color.Z; changed = true;
            }

            string albedo = data.AlbedoPath;
            if (DrawMaterialAssetPath("Base Map", ref albedo)) { data.AlbedoPath = albedo; changed = true; }
            string normal = data.NormalMapPath;
            if (DrawMaterialAssetPath("Normal Map", ref normal)) { data.NormalMapPath = normal; changed = true; }
            string roughnessMap = data.RoughnessMapPath;
            if (DrawMaterialAssetPath("Roughness Map", ref roughnessMap)) { data.RoughnessMapPath = roughnessMap; changed = true; }
            string metallicMap = data.MetallicMapPath;
            if (DrawMaterialAssetPath("Metallic Map", ref metallicMap)) { data.MetallicMapPath = metallicMap; changed = true; }
            ImGui.Dummy(new Vector2(0f, 3f));
        }

        if (!hasShaderGraph && SectionHeader("Surface"))
        {
            float metallic = data.Metallic;
            if (DrawUnitySliderFloat("Metallic", ref metallic, 0f, 1f)) { data.Metallic = metallic; changed = true; }

            float roughness = data.Roughness;
            if (DrawUnitySliderFloat("Roughness", ref roughness, 0f, 1f)) { data.Roughness = roughness; changed = true; }
            ImGui.Dummy(new Vector2(0f, 3f));
        }

        if (!hasShaderGraph && SectionHeader("Emission"))
        {
            // HDR Color picker estilo Unity: color + intensidad en un solo control.
            var emission = new System.Numerics.Vector3(data.EmissionR, data.EmissionG, data.EmissionB);
            float emissionIntensity = data.EmissionIntensity;
            FieldRow("Emission (HDR)");
            if (HdrColorField("##emis", ref emission, ref emissionIntensity))
            {
                data.EmissionR = emission.X; data.EmissionG = emission.Y; data.EmissionB = emission.Z;
                data.EmissionIntensity = Math.Max(0f, emissionIntensity);
                changed = true;
            }
            ImGui.Dummy(new Vector2(0f, 3f));
        }

        if (changed)
        {
            QueueMaterialAssetSave(path, data);
        }
    }

    /// <summary>Igual que <see cref="DrawShaderGraphPropertyFields"/> pero para el inspector de un asset .mat (sin objeto de escena asociado).</summary>
    private bool DrawShaderGraphAssetPropertyFields(string path, MaterialAssetData data)
    {
        if (string.IsNullOrWhiteSpace(data.ShaderGraphPath)) return false;

        string? sgPath = SceneViewportRenderer.NormalizeExistingAssetPath(data.ShaderGraphPath);
        if (sgPath == null) return false;

        var entry = sceneRenderer.GetShaderGraphEntry(sgPath);
        if (entry == null || entry.ExposedProperties.Count == 0) return false;

        if (!SectionHeader("Exposed Properties")) return false;

        bool changed = false;

        if (ImGui.SmallButton("Reset To Graph Defaults"))
        {
            data.ShaderGraphProperties.Clear();
            data.ShaderGraphTextures.Clear();
            QueueMaterialAssetSave(path, data);
            changed = true;
        }
        ImGui.Separator();

        foreach (var kv in entry.ExposedProperties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string uniform = kv.Key;
            var info = kv.Value;
            string label = info.DisplayName;

            if (info.Type == PinType.Texture2D)
            {
                data.ShaderGraphTextures.TryGetValue(uniform, out var texPath);
                texPath ??= string.Empty;
                DrawTextureMapSlot(label, texPath, "Drop texture", texturePath =>
                {
                    if (string.IsNullOrWhiteSpace(texturePath) || MaterialAsset.IsTexturePath(texturePath))
                    {
                        data.ShaderGraphTextures[uniform] = texturePath;
                        QueueMaterialAssetSave(path, data);
                    }
                });
                continue;
            }

            if (!entry.PropertyDefaults.TryGetValue(uniform, out var defaults)) continue;
            float[] current = data.ShaderGraphProperties.TryGetValue(uniform, out var cur) && cur.Length == defaults.Length
                ? cur
                : defaults;
            bool fieldChanged = false;
            float[] next = (float[])current.Clone();

            switch (defaults.Length)
            {
                case 1:
                {
                    FieldRow(label);
                    float f = current[0];
                    if (ImGui.DragFloat("##sga_" + uniform, ref f, 0.01f))
                    {
                        next[0] = f;
                        fieldChanged = true;
                    }
                    break;
                }
                case 2:
                {
                    FieldRow(label);
                    var v2 = new System.Numerics.Vector2(current[0], current[1]);
                    if (ImGui.DragFloat2("##sga_" + uniform, ref v2, 0.01f))
                    {
                        next[0] = v2.X; next[1] = v2.Y;
                        fieldChanged = true;
                    }
                    break;
                }
                case 3:
                {
                    FieldRow(label);
                    var v3 = new System.Numerics.Vector3(current[0], current[1], current[2]);
                    if (ImGui.DragFloat3("##sga_" + uniform, ref v3, 0.01f))
                    {
                        next[0] = v3.X; next[1] = v3.Y; next[2] = v3.Z;
                        fieldChanged = true;
                    }
                    break;
                }
                case 4 when info.IsHdr && info.Type == PinType.Vec3:
                {
                    // Color HDR (vec3): [r, g, b, intensity]
                    FieldRow(label);
                    var rgb = new System.Numerics.Vector3(current[0], current[1], current[2]);
                    var intensity = current[3];
                    if (HdrColorField("##sga_" + uniform, ref rgb, ref intensity))
                    {
                        next[0] = rgb.X; next[1] = rgb.Y; next[2] = rgb.Z; next[3] = intensity;
                        fieldChanged = true;
                    }
                    break;
                }
                case 4:
                {
                    FieldRow(label);
                    var v4 = new System.Numerics.Vector4(current[0], current[1], current[2], current[3]);
                    if (ColorField4("##sga_" + uniform, ref v4))
                    {
                        next[0] = v4.X; next[1] = v4.Y; next[2] = v4.Z; next[3] = v4.W;
                        fieldChanged = true;
                    }
                    break;
                }
                case 5:
                {
                    // Color HDR (vec4): [r, g, b, a, intensity]
                    FieldRow(label);
                    var rgb = new System.Numerics.Vector3(current[0], current[1], current[2]);
                    var intensity = current[4];
                    if (HdrColorField("##sga_" + uniform, ref rgb, ref intensity))
                    {
                        next[0] = rgb.X; next[1] = rgb.Y; next[2] = rgb.Z; next[4] = intensity;
                        fieldChanged = true;
                    }
                    break;
                }
            }

            if (fieldChanged)
            {
                data.ShaderGraphProperties[uniform] = next;
                QueueMaterialAssetSave(path, data);
                changed = true;
            }
        }

        ImGui.Dummy(new Vector2(0f, 3f));
        return changed;
    }

    private bool DrawMaterialAssetPath(string label, ref string value)
    {
        bool changed = false;
        string edit = value;
        DrawTextureMapSlot(label, value, "Drop texture", path =>
        {
            if (label.Contains("Normal", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            {
                TextureImportSettingsAsset.EnsureNormalMap(path);
                sceneRenderer.InvalidateTexture(path);
            }
            edit = path;
            changed = true;
        });

        if (changed)
            value = edit;
        return changed;
    }

    private static readonly TimeSpan ScriptableObjectSaveDelay = TimeSpan.FromMilliseconds(550);

    private ScriptableObject? GetEditableScriptableObject(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (scriptableObjectCache.TryGetValue(fullPath, out var cached))
            return cached;

        var loaded = ScriptableObjectAsset.Load(fullPath, physicsEngine, scriptCompiler);
        if (loaded != null)
            scriptableObjectCache[fullPath] = loaded;
        return loaded;
    }

    private void QueueScriptableObjectSave(string path)
    {
        string fullPath = Path.GetFullPath(path);
        pendingScriptableObjectSaves[fullPath] = DateTime.UtcNow + ScriptableObjectSaveDelay;
        statusMessageValue = "Asset updated";
    }

    private void ProcessPendingScriptableObjectSaves()
    {
        if (pendingScriptableObjectSaves.Count == 0) return;

        var now = DateTime.UtcNow;
        List<string>? ready = null;
        foreach (var pair in pendingScriptableObjectSaves)
        {
            if (pair.Value > now) continue;
            ready ??= new List<string>();
            ready.Add(pair.Key);
        }

        if (ready == null) return;
        foreach (string path in ready)
        {
            pendingScriptableObjectSaves.Remove(path);
            if (!scriptableObjectCache.TryGetValue(path, out var instance)) continue;
            InvalidateAssetPreview(path, deleteTexture: true);
            statusMessageValue = ScriptableObjectAsset.Save(path, instance, physicsEngine)
                ? "Asset saved"
                : $"No se pudo guardar {Path.GetFileName(path)}: contiene una referencia no soportada";
        }
    }

    private void FlushPendingScriptableObjectSaves()
    {
        if (pendingScriptableObjectSaves.Count == 0) return;

        foreach (string path in pendingScriptableObjectSaves.Keys.ToArray())
        {
            if (scriptableObjectCache.TryGetValue(path, out var instance))
            {
                ScriptableObjectAsset.Save(path, instance, physicsEngine);
                InvalidateAssetPreview(path, deleteTexture: true);
            }
        }
        pendingScriptableObjectSaves.Clear();
    }

    private void DrawScriptableObjectAssetInspector(string path)
    {
        var instance = GetEditableScriptableObject(path);
        ImGui.TextDisabled("ScriptableObject Asset");
        ImGui.TextWrapped(Path.GetFileName(path));

        if (instance == null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.36f, 0.30f, 1f),
                "No se pudo resolver el tipo. Compila los scripts del proyecto.");
            return;
        }

        ImGui.TextDisabled(instance.GetType().Name);
        ImGui.Spacing();

        string name = instance.Name;
        FieldRow("Name");
        if (ImGui.InputText("##soname", ref name, 128) && name != instance.Name)
        {
            instance.Name = name;
            QueueScriptableObjectSave(path);
        }
        ImGui.Spacing();

        DrawScriptFields(instance, () => QueueScriptableObjectSave(path));
    }

}
