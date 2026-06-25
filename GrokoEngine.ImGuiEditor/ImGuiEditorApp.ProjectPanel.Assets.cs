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
private void DrawProjectFolderTree(string directory)
    {
        bool selectedFolder = string.Equals(currentProjectDirectory, directory, StringComparison.OrdinalIgnoreCase);
        bool onCurrentPath = IsProjectFolderOnCurrentPath(directory);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (selectedFolder && projectFolderHighlightActive) flags |= ImGuiTreeNodeFlags.Selected;

        if (onCurrentPath)
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);

        string label = Path.GetFileName(directory);
        if (string.IsNullOrWhiteSpace(label))
            label = directory;

        bool open = ImGui.TreeNodeEx("ProjectFolderTreeV3:" + directory, flags, label);
        if (ImGui.IsItemClicked())
        {
            currentProjectDirectory = directory;
            selectedAssetPath = directory;
            projectFolderHighlightActive = true;
        }
        DrawProjectContextMenu(directory);
        DrawProjectFolderDropTarget(directory);

        if (open)
        {
            foreach (var child in GetCachedProjectDirectories(directory))
                DrawProjectFolderTree(child.Path);
            ImGui.TreePop();
        }
    }

private bool IsProjectFolderOnCurrentPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(currentProjectDirectory))
            return false;

        try
        {
            string current = Path.GetFullPath(currentProjectDirectory);
            string folder = Path.GetFullPath(directory);
            return string.Equals(current, folder, StringComparison.OrdinalIgnoreCase) ||
                   IsPathInsideDirectory(current, folder);
        }
        catch
        {
            return false;
        }
    }

private IEnumerable<ProjectAssetEntry> CreateMeshSubAssetEntries(ProjectAssetEntry parent)
    {
        ParsedMesh? mesh;
        try
        {
            mesh = ObjLoader.Load(parent.Path);
        }
        catch
        {
            yield break;
        }

        if (mesh == null)
            yield break;

        string meshName = Path.GetFileNameWithoutExtension(parent.Path);
        string meshDir = Path.GetDirectoryName(parent.Path) ?? rootAssetsPath;
        DateTime modified = File.Exists(parent.Path) ? File.GetLastWriteTimeUtc(parent.Path) : DateTime.MinValue;
        var importSettings = ModelImportSettingsAsset.Load(parent.Path);
        string avatarPath = !string.IsNullOrWhiteSpace(importSettings.CreatedAvatarPath)
            ? importSettings.CreatedAvatarPath
            : parent.Path + ".avatar";

        if (File.Exists(avatarPath))
            yield return CreateVirtualAvatarEntry(parent.Path, avatarPath, modified);

        for (int i = 0; i < mesh.Animations.Count; i++)
        {
            string clipName = string.IsNullOrWhiteSpace(mesh.Animations[i].Name)
                ? $"{meshName} {i + 1}"
                : mesh.Animations[i].Name;
            yield return CreateVirtualAnimationEntry(parent.Path, clipName, i, modified);
        }

        if (mesh.Submeshes.Count == 0)
        {
            yield return CreateVirtualSubmeshEntry(parent.Path, meshName, 0, modified);
            yield break;
        }

        for (int i = 0; i < mesh.Submeshes.Count; i++)
        {
            var submesh = mesh.Submeshes[i];
            string submeshName = string.IsNullOrWhiteSpace(submesh.Name) ? $"Submesh {i}" : submesh.Name;
            yield return CreateVirtualSubmeshEntry(parent.Path, submeshName, i, modified);

            string materialPath = Path.Combine(meshDir, $"{meshName}_{submeshName}.mat");
            if (File.Exists(materialPath))
                yield return CreateVirtualMaterialEntry(parent.Path, materialPath, i);
        }
    }

private static bool TryParseProjectSubAssetKey(string key, out string parentPath, out string kind, out int index)
    {
        parentPath = "";
        kind = "";
        index = -1;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        int marker = key.LastIndexOf("::", StringComparison.Ordinal);
        if (marker < 0 || marker + 2 >= key.Length)
            return false;

        parentPath = key[..marker];
        string rest = key[(marker + 2)..];
        int colon = rest.LastIndexOf(':');
        if (colon <= 0 || colon + 1 >= rest.Length)
            return false;

        kind = rest[..colon];
        return int.TryParse(rest[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

private ProjectAssetEntry CreateProjectAssetEntry(string path, bool isDirectory)
    {
        string name = Path.GetFileName(path);
        string kind = isDirectory ? "FOLDER" : GetAssetKind(path);
        long size = 0;
        DateTime modified = DateTime.MinValue;

        try
        {
            if (isDirectory)
            {
                modified = Directory.GetLastWriteTimeUtc(path);
            }
            else
            {
                var info = new FileInfo(path);
                size = info.Exists ? info.Length : 0;
                modified = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            }
        }
        catch
        {
        }

        return new ProjectAssetEntry
        {
            Path = path,
            Name = name,
            Kind = kind,
            IsDirectory = isDirectory,
            SizeBytes = size,
            ModifiedUtc = modified
        };
    }

private void InvalidateProjectFolderCache(string? directory = null)
    {
        assetPickerFileCacheDirty = true;
        projectVisibleEntriesCacheKey = null;
        projectVisibleEntriesCache = Array.Empty<ProjectAssetEntry>();

        if (string.IsNullOrWhiteSpace(directory))
        {
            projectFolderCache.Clear();
            return;
        }

        projectFolderCache.Remove(directory);
    }

private void DrawProjectAssetGrid(string directory)
    {
        float tileWidth = Math.Max(48f, 94f * projectTileScale);
        float tileHeight = Math.Max(54f, 92f * projectTileScale);
        float available = Math.Max(tileWidth, ImGui.GetContentRegionAvail().X);
        int columns = Math.Max(1, (int)(available / tileWidth));

        var entries = GetVisibleProjectEntries(directory);
        int count = entries.Count;

        if (count == 0)
        {
            ImGui.Dummy(new Vector2(1f, 24f));
            ImGui.TextDisabled("This folder is empty");
            return;
        }

        // Virtualización segura para ImGui:
        // NO usamos SetCursorPosY para extender el scroll porque ImGui hace assert.
        // Creamos espacio real con Dummy() antes y después de los items visibles.
        float scrollY = ImGui.GetScrollY();
        float visibleH = Math.Max(1f, ImGui.GetWindowHeight());
        int totalRows = Math.Max(1, (int)Math.Ceiling(count / (float)columns));

        int firstRow = Math.Max(0, (int)Math.Floor(scrollY / tileHeight) - 2);
        int visibleRows = Math.Max(1, (int)Math.Ceiling(visibleH / tileHeight) + 5);
        int lastRow = Math.Min(totalRows, firstRow + visibleRows);

        int firstIndex = Math.Min(count, firstRow * columns);
        int lastIndex = Math.Min(count, lastRow * columns);

        float topPadding = firstRow * tileHeight;
        if (topPadding > 0f)
            ImGui.Dummy(new Vector2(1f, topPadding));

        // Solo se dibujan las filas visibles (virtualización). Cada tile pide
        // su preview: si está cacheado se dibuja, si no entra en cola limitada.
        for (int i = firstIndex; i < lastIndex; i++)
            DrawProjectAssetTile(entries[i], entries, i, columns, tileWidth, tileHeight);

        float bottomPadding = Math.Max(0f, (totalRows - lastRow) * tileHeight);
        if (bottomPadding > 0f)
            ImGui.Dummy(new Vector2(1f, bottomPadding));
    }

private void DrawProjectAssetList(string directory)
    {
        var entries = GetVisibleProjectEntries(directory);
        float contentWidth = Math.Max(260f, ImGui.GetContentRegionAvail().X);

        // Header sin ImGui.Columns: menos estado interno y permite virtualizar filas igual que el grid.
        float nameW = contentWidth * 0.54f;
        float typeW = contentWidth * 0.16f;
        float sizeW = contentWidth * 0.14f;
        if (ImGui.SmallButton("Name")) { projectSortMode = AssetSortMode.Name; InvalidateProjectFolderCache(directory); }
        ImGui.SameLine(nameW);
        if (ImGui.SmallButton("Type")) { projectSortMode = AssetSortMode.Type; InvalidateProjectFolderCache(directory); }
        ImGui.SameLine(nameW + typeW);
        if (ImGui.SmallButton("Size")) { projectSortMode = AssetSortMode.Size; InvalidateProjectFolderCache(directory); }
        ImGui.SameLine(nameW + typeW + sizeW);
        if (ImGui.SmallButton("Modified")) { projectSortMode = AssetSortMode.Modified; InvalidateProjectFolderCache(directory); }
        ImGui.Separator();

        int count = entries.Count;
        if (count == 0)
        {
            ImGui.TextDisabled("This folder is empty");
            return;
        }

        const float rowHeight = 22f;
        float listStartY = ImGui.GetCursorPosY();
        float scrolledIntoList = Math.Max(0f, ImGui.GetScrollY() - listStartY);
        float visibleH = Math.Max(1f, ImGui.GetWindowHeight());
        int firstIndex = Math.Max(0, (int)Math.Floor(scrolledIntoList / rowHeight) - 4);
        int visibleRows = Math.Max(1, (int)Math.Ceiling(visibleH / rowHeight) + 8);
        int lastIndex = Math.Min(count, firstIndex + visibleRows);

        float topPadding = firstIndex * rowHeight;
        if (topPadding > 0f)
            ImGui.Dummy(new Vector2(1f, topPadding));

        for (int i = firstIndex; i < lastIndex; i++)
            DrawProjectAssetListRowVirtual(entries[i], entries, i, contentWidth, rowHeight);

        float bottomPadding = Math.Max(0f, (count - lastIndex) * rowHeight);
        if (bottomPadding > 0f)
            ImGui.Dummy(new Vector2(1f, bottomPadding));
    }

private void DrawProjectAssetListRowVirtual(ProjectAssetEntry entry, IReadOnlyList<ProjectAssetEntry> visibleEntries, int index, float contentWidth, float rowHeight)
    {
        string path = entry.Path;
        bool isDirectory = entry.IsDirectory;
        string actionPath = entry.SourceMaterialPath ?? entry.SourceAvatarPath ?? path;

        ImGui.PushID(path);
        bool selectedItem = IsProjectEntrySelected(entry);
        ImGui.InvisibleButton("assetRowVirtual", new Vector2(contentWidth, rowHeight));
        bool hovered = ImGui.IsItemHovered();
        if (hovered)
            projectHoveredAnyAssetItem = true;
        bool clicked = ImGui.IsItemClicked();
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        RegisterProjectAssetSelectionRect(entry, rowMin, rowMax);

        if (clicked)
            HandleProjectEntrySelection(entry, visibleEntries, index);

        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (!entry.IsVirtualSubAsset && entry.Kind == "MESH")
                ToggleMeshEntryExpanded(entry);
            else if (isDirectory)
            {
                currentProjectDirectory = path;
                projectFolderHighlightActive = true;
            }
            else if (actionPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                scriptCompiler.OpenInEditor(actionPath);
            else if (actionPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                OpenShaderGraphAsset(actionPath);
        }

        if (!entry.IsVirtualSubAsset || !string.IsNullOrWhiteSpace(entry.SourceMaterialPath) || !string.IsNullOrWhiteSpace(entry.SourceAvatarPath))
        {
            DrawProjectContextMenu(actionPath);
            DrawProjectAssetDragSource(actionPath);
        }
        if (isDirectory)
            DrawProjectFolderDropTarget(path);

        var drawList = ImGui.GetWindowDrawList();
        if (selectedItem || hovered)
        {
            uint bg = selectedItem
                ? ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 0.36f, 0.55f, 0.80f))
                : ImGui.GetColorU32(new System.Numerics.Vector4(0.24f, 0.25f, 0.27f, 0.34f));
            drawList.AddRectFilled(rowMin, rowMax, bg, 3f);
        }

        float indent = entry.IsVirtualSubAsset ? 18f : 0f;
        var icon = isDirectory ? EditorIcon.Folder : IconForAssetKind(entry.Kind);
        var iconColor = isDirectory
            ? ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.68f, 0.24f, 1f))
            : ImGui.GetColorU32(ProjectAssetColorForKind(entry.Kind));

        if (!isDirectory && entry.Kind == "MESH" && !entry.IsVirtualSubAsset)
            DrawMeshFoldoutGlyph(drawList, rowMin + new Vector2(5f, 4f), 12f, IsMeshEntryExpanded(entry));

        if (!isDirectory && entry.Kind == "CS")
        {
            if (TryGetEditorScriptIconTexture(out var scriptTextureId))
                drawList.AddImage(scriptTextureId, rowMin + new Vector2(4f + indent, 2f), rowMin + new Vector2(18f + indent, 18f), Vector2.Zero, Vector2.One);
            else
                DrawScriptAssetPreview(drawList, rowMin + new Vector2(1f + indent, 1f), 18f);
        }
        else if (entry.IsVirtualSubAsset && entry.Kind == "SUBMESH")
        {
            DrawSubmeshAssetPreview(drawList, rowMin + new Vector2(4f + indent, 2f), 16f);
        }
        else
        {
            DrawAtlasIconOrFallback(drawList, icon, rowMin + new Vector2(4f + indent, 3f), 14f, iconColor);
        }

        uint textColor = ImGui.GetColorU32(selectedItem ? new System.Numerics.Vector4(0.80f, 0.90f, 1f, 1f) : UiText);
        float typeX = rowMin.X + contentWidth * 0.54f;
        float sizeX = rowMin.X + contentWidth * 0.70f;
        float modifiedX = rowMin.X + contentWidth * 0.84f;
        drawList.AddText(rowMin + new Vector2(24f + indent, 3f), textColor, entry.Name);
        drawList.AddText(new Vector2(typeX, rowMin.Y + 3f), ImGui.GetColorU32(new System.Numerics.Vector4(0.62f, 0.66f, 0.70f, 1f)), isDirectory ? "Folder" : entry.Kind);
        drawList.AddText(new Vector2(sizeX, rowMin.Y + 3f), ImGui.GetColorU32(new System.Numerics.Vector4(0.62f, 0.66f, 0.70f, 1f)), isDirectory ? "-" : $"{entry.SizeBytes / 1024f:F1} KB");
        drawList.AddText(new Vector2(modifiedX, rowMin.Y + 3f), ImGui.GetColorU32(new System.Numerics.Vector4(0.62f, 0.66f, 0.70f, 1f)), entry.ModifiedUtc.ToLocalTime().ToString("HH:mm"));

        if (selectedItem && !string.Equals(lastFlashedAssetPath, path, StringComparison.OrdinalIgnoreCase))
        {
            lastFlashedAssetPath = path;
            lastAssetSelectionFlashTime = GLFW.GetTime();
        }

        ImGui.PopID();
    }

private void DrawProjectAssetListRow(ProjectAssetEntry entry, IReadOnlyList<ProjectAssetEntry> visibleEntries, int index)
    {
        string path = entry.Path;
        bool isDirectory = entry.IsDirectory;
        string actionPath = entry.SourceMaterialPath ?? entry.SourceAvatarPath ?? path;

        ImGui.PushID(path);
        bool selectedItem = IsProjectEntrySelected(entry);
        if (selectedItem)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.58f, 0.78f, 1f, 1f));

        if (ImGui.Selectable("##assetRow", selectedItem, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, 20f)))
        {
            HandleProjectEntrySelection(entry, visibleEntries, index);
        }
        if (ImGui.IsItemHovered())
            projectHoveredAnyAssetItem = true;

        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        RegisterProjectAssetSelectionRect(entry, rowMin, rowMax);
        var icon = isDirectory ? EditorIcon.Folder : IconForAssetKind(entry.Kind);
        var iconColor = isDirectory
            ? ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.68f, 0.24f, 1f))
            : ImGui.GetColorU32(ProjectAssetColorForKind(entry.Kind));

        float indent = entry.IsVirtualSubAsset ? 18f : 0f;
        if (!isDirectory && entry.Kind == "MESH" && !entry.IsVirtualSubAsset)
            DrawMeshFoldoutGlyph(ImGui.GetWindowDrawList(), rowMin + new Vector2(5f, 4f), 12f, IsMeshEntryExpanded(entry));

        if (!isDirectory && entry.Kind == "CS")
        {
            if (TryGetEditorScriptIconTexture(out var scriptTextureId))
                ImGui.GetWindowDrawList().AddImage(scriptTextureId, rowMin + new Vector2(4f + indent, 2f), rowMin + new Vector2(18f + indent, 18f), Vector2.Zero, Vector2.One);
            else
                DrawScriptAssetPreview(ImGui.GetWindowDrawList(), rowMin + new Vector2(1f + indent, 1f), 18f);
        }
        else if (entry.IsVirtualSubAsset && entry.Kind == "SUBMESH")
        {
            DrawSubmeshAssetPreview(ImGui.GetWindowDrawList(), rowMin + new Vector2(4f + indent, 2f), 16f);
        }
        else
        {
            DrawAtlasIconOrFallback(ImGui.GetWindowDrawList(), icon, rowMin + new Vector2(4f + indent, 3f), 14f, iconColor);
        }

        ImGui.GetWindowDrawList().AddText(
            rowMin + new Vector2(24f + indent, 3f),
            ImGui.GetColorU32(selectedItem ? new System.Numerics.Vector4(0.80f, 0.90f, 1f, 1f) : UiText),
            entry.Name);

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (!entry.IsVirtualSubAsset && entry.Kind == "MESH")
                ToggleMeshEntryExpanded(entry);
            else if (isDirectory)
            {
                currentProjectDirectory = path;
                projectFolderHighlightActive = true;
            }
            else if (actionPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                scriptCompiler.OpenInEditor(actionPath);
            else if (actionPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                OpenShaderGraphAsset(actionPath);
        }

        if (!entry.IsVirtualSubAsset || !string.IsNullOrWhiteSpace(entry.SourceMaterialPath) || !string.IsNullOrWhiteSpace(entry.SourceAvatarPath))
            DrawProjectContextMenu(actionPath);
        if (selectedItem && !string.Equals(lastFlashedAssetPath, path, StringComparison.OrdinalIgnoreCase))
        {
            lastFlashedAssetPath = path;
            lastAssetSelectionFlashTime = GLFW.GetTime();
        }

        if (!entry.IsVirtualSubAsset || !string.IsNullOrWhiteSpace(entry.SourceMaterialPath) || !string.IsNullOrWhiteSpace(entry.SourceAvatarPath))
            DrawProjectAssetDragSource(actionPath);
        if (isDirectory)
            DrawProjectFolderDropTarget(path);

        if (selectedItem)
            ImGui.PopStyleColor();

        ImGui.NextColumn();
        ImGui.TextDisabled(isDirectory ? "Folder" : entry.Kind); ImGui.NextColumn();
        ImGui.TextDisabled(isDirectory ? "-" : $"{entry.SizeBytes / 1024f:F1} KB"); ImGui.NextColumn();
        ImGui.TextDisabled(entry.ModifiedUtc.ToLocalTime().ToString("HH:mm")); ImGui.NextColumn();

        ImGui.PopID();
    }

private void DrawProjectAssetListRow(string path, bool isDirectory)
    {
        ProjectAssetEntry entry = CreateProjectAssetEntry(path, isDirectory);
        DrawProjectAssetListRow(entry, new[] { entry }, 0);
    }

private void DrawProjectAssetTile(ProjectAssetEntry entry, IReadOnlyList<ProjectAssetEntry> visibleEntries, int index, int columns, float tileWidth, float tileHeight)
    {
        string path = entry.Path;
        bool isDirectory = entry.IsDirectory;
        string actionPath = entry.SourceMaterialPath ?? entry.SourceAvatarPath ?? path;

        if (index % columns != 0)
            ImGui.SameLine();

        ImGui.PushID(path);
        bool selectedItem = IsProjectEntrySelected(entry);
        ImGui.InvisibleButton("assetTile", new Vector2(tileWidth - 8f, tileHeight));
        var afterTileCursor = ImGui.GetCursorScreenPos();
        bool hovered = ImGui.IsItemHovered();
        if (hovered)
            projectHoveredAnyAssetItem = true;
        bool clicked = ImGui.IsItemClicked();
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        RegisterProjectAssetSelectionRect(entry, rectMin, rectMax);
        bool meshExpandable = !entry.IsVirtualSubAsset && entry.Kind == "MESH";
        var arrowMin = rectMin + new Vector2(4f, Math.Max(6f, tileHeight * 0.42f - 7f));
        var arrowMax = arrowMin + new Vector2(16f, 16f);
        var mouse = ImGui.GetIO().MousePos;
        bool arrowClicked = clicked && meshExpandable &&
            mouse.X >= arrowMin.X && mouse.X <= arrowMax.X &&
            mouse.Y >= arrowMin.Y && mouse.Y <= arrowMax.Y;

        if (arrowClicked)
        {
            ToggleMeshEntryExpanded(entry);
        }
        else if (clicked)
        {
            double now = GLFW.GetTime();
            if (!entry.IsVirtualSubAsset && selectedItem && string.Equals(lastAssetClickPath, path, StringComparison.OrdinalIgnoreCase)
                && (now - lastAssetClickTime) > 0.35 && (now - lastAssetClickTime) < 1.2)
            {
                BeginRenameAsset(path);
            }
            lastAssetClickPath = path;
            lastAssetClickTime = now;
            HandleProjectEntrySelection(entry, visibleEntries, index);
        }

        if (!arrowClicked && hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (meshExpandable)
                ToggleMeshEntryExpanded(entry);
            else if (isDirectory)
            {
                currentProjectDirectory = path;
                projectFolderHighlightActive = true;
            }
            else if (actionPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                scriptCompiler.OpenInEditor(actionPath);
            else if (actionPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                OpenShaderGraphAsset(actionPath);
        }

        if (!entry.IsVirtualSubAsset || !string.IsNullOrWhiteSpace(entry.SourceMaterialPath) || !string.IsNullOrWhiteSpace(entry.SourceAvatarPath))
            DrawProjectContextMenu(actionPath);
        if (!entry.IsVirtualSubAsset || !string.IsNullOrWhiteSpace(entry.SourceMaterialPath) || !string.IsNullOrWhiteSpace(entry.SourceAvatarPath))
            DrawProjectAssetDragSource(actionPath);
        if (isDirectory)
            DrawProjectFolderDropTarget(path);

        var drawList = ImGui.GetWindowDrawList();
        if (selectedItem || hovered)
        {
            uint bg = selectedItem
                ? ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 0.36f, 0.55f, 0.80f))
                : ImGui.GetColorU32(new System.Numerics.Vector4(0.24f, 0.25f, 0.27f, 0.34f));
            drawList.AddRectFilled(rectMin, rectMax, bg, 3f);
        }

        if (selectedItem)
        {
            drawList.AddRect(rectMin, rectMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.36f, 0.62f, 0.92f, 1f)), 3f, ImDrawFlags.None, 1.6f);

            float flash = (float)Math.Clamp(1.0 - (GLFW.GetTime() - lastAssetSelectionFlashTime) / 0.55, 0.0, 1.0);
            if (flash > 0f)
                drawList.AddRect(rectMin - new Vector2(2f, 2f), rectMax + new Vector2(2f, 2f), ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.88f, 1f, flash)), 5f, ImDrawFlags.None, 2f);
        }

        float iconSize = 42f * projectTileScale;
        var iconMin = rectMin + new Vector2((tileWidth - iconSize) * 0.5f - 4f, 9f);
        DrawProjectEntryPreview(drawList, entry, iconMin, iconSize);
        if (meshExpandable)
            DrawMeshFoldoutGlyph(drawList, arrowMin, 16f, IsMeshEntryExpanded(entry));

        var textPos = rectMin + new Vector2(7f, Math.Max(58f, tileHeight - 30f));
        if (inlineRenameAssetPath == path)
        {
            ImGui.SetCursorScreenPos(textPos - new Vector2(2f, 2f));
            ImGui.SetNextItemWidth(tileWidth - 14f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2f, 1f));
            if (inlineRenameFocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                inlineRenameFocusPending = false;
            }
            bool enter = ImGui.InputText("##rename", ref inlineRenameAssetName, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool deactivated = ImGui.IsItemDeactivated();
            ImGui.PopStyleVar();
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                CancelInlineRenameAsset();
            else if (enter || deactivated)
                CommitInlineRenameAsset();
            ImGui.SetCursorScreenPos(afterTileCursor);
        }
        else
        {
            string displayName = isDirectory || entry.IsVirtualSubAsset ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
            string label = Ellipsize(displayName, Math.Max(10, (int)(15 * projectTileScale)));
            drawList.AddText(textPos, ImGui.GetColorU32(new System.Numerics.Vector4(0.82f, 0.84f, 0.86f, 1f)), label);
        }

        ImGui.PopID();
    }

private void DrawProjectAssetTile(string path, bool isDirectory, int index, int columns, float tileWidth, float tileHeight)
    {
        ProjectAssetEntry entry = CreateProjectAssetEntry(path, isDirectory);
        DrawProjectAssetTile(entry, new[] { entry }, index, columns, tileWidth, tileHeight);
    }

private void DrawProjectAssetDragSource(string path)
    {
        // BeginDragDropSource es barato y devuelve false salvo que se esté arrastrando ESTE item.
        // Lo comprobamos PRIMERO: antes, la validación (IsInsideAssets + 2x Path.GetFullPath) corría
        // en cada tile y cada frame (cientos de GetFullPath/frame). Ahora solo al arrastrar de verdad.
        if (!ImGui.BeginDragDropSource())
            return;

        if (IsInsideAssets(path) && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(rootAssetsPath), StringComparison.OrdinalIgnoreCase))
        {
            ImGui.SetDragDropPayload("GROKO_ASSET", IntPtr.Zero, 0);
            draggingAssetPath = path;
            DrawAssetDragPreview(path);
        }

        ImGui.EndDragDropSource();
    }

private void DrawProjectFolderDropTarget(string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory) || !IsInsideAssets(targetDirectory))
            return;

        if (!ImGui.BeginDragDropTarget())
            return;

        bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
        if (delivered && draggingAssetPath != null)
        {
            MoveProjectDraggedAssetsToDirectory(draggingAssetPath, targetDirectory);
            draggingAssetPath = null;
        }

        // Arrastrar un objeto de la jerarquía al panel de Assets → crear un prefab.
        bool prefabDelivered = AcceptDragDropOnRelease("GROKO_HIERARCHY_OBJECT");
        if (prefabDelivered && hierarchyDragObjectId != null)
        {
            CreatePrefabFromHierarchyObject(targetDirectory);
            hierarchyDragObjectId = null;
        }

        ImGui.EndDragDropTarget();
    }

private void CreatePrefabFromHierarchyObject(string targetDirectory)
    {
        var obj = hierarchyDragObjectId != null ? sceneGraph.FindById(hierarchyDragObjectId) : null;
        if (obj == null) return;

        try
        {
            Directory.CreateDirectory(targetDirectory);
            string baseName = string.IsNullOrWhiteSpace(obj.Name) ? "Prefab" : obj.Name;
            string path = Path.Combine(targetDirectory, baseName + ".prefab");
            int i = 1;
            while (File.Exists(path))
                path = Path.Combine(targetDirectory, $"{baseName}_{i++}.prefab");

            SceneSerializer.SavePrefab(path, obj);
            obj.PrefabAssetPath = path; // la instancia queda vinculada al prefab
            selectedAssetPath = path;
            statusMessage = "Prefab creado: " + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            statusMessage = "No se pudo crear el prefab: " + ex.Message;
        }
    }

private List<string> GetProjectDragAssetPaths(string draggedPath)
    {
        var selectedPaths = GetSelectedProjectAssetPathsForFileOperation();
        string draggedFull = Path.GetFullPath(draggedPath);
        if (selectedPaths.Any(path => string.Equals(path, draggedFull, StringComparison.OrdinalIgnoreCase)))
            return selectedPaths;

        return NormalizeProjectFileOperationPaths(new[] { draggedFull });
    }

private void MoveProjectDraggedAssetsToDirectory(string draggedPath, string targetDirectory)
    {
        List<string> paths = GetProjectDragAssetPaths(draggedPath);
        foreach (string path in paths.ToArray())
            MoveProjectAssetToDirectory(path, targetDirectory);
    }

private void DrawProjectAssetBackgroundClickCatcher(Vector2 origin, Vector2 size, string? targetDirectory)
    {
        if (size.X < 1f || size.Y < 1f)
            return;

        var beforePos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##ProjectAssetBackground", size);

        if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
            DrawProjectFolderDropTarget(targetDirectory);

        var io = ImGui.GetIO();
        if (!projectHoveredAnyAssetItem && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            projectAssetBoxSelecting = true;
            projectAssetBoxSelectAdditive = io.KeyCtrl || io.KeyShift;
            projectAssetBoxStart = io.MousePos;
            projectAssetBoxCurrent = projectAssetBoxStart;
            projectAssetBoxSelectionBase.Clear();
            foreach (string key in selectedProjectEntryKeys)
                projectAssetBoxSelectionBase.Add(key);

            // Clic en zona vacía (sin Ctrl/Shift) → deseleccionar carpeta/asset (árbol y grilla).
            if (!projectAssetBoxSelectAdditive)
            {
                ClearProjectEntrySelection();
                projectFolderHighlightActive = false;
            }
        }
        else if (!projectHoveredAnyAssetItem && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            ClearProjectEntrySelection();
            if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
                ImGui.OpenPopup("ProjectAssetBackgroundContext");
        }

        if (projectAssetBoxSelecting)
        {
            projectAssetBoxCurrent = io.MousePos;
            bool finished = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            ApplyProjectAssetBoxSelection(finished);
            DrawProjectAssetBoxSelection();

            if (finished)
            {
                projectAssetBoxSelecting = false;
                projectAssetBoxSelectionBase.Clear();
            }
        }

        PushContextMenuStyle();
        if (ImGui.BeginPopup("ProjectAssetBackgroundContext"))
        {
            if (ImGui.BeginMenu("Create"))
            {
                DrawProjectCreateMenuItems(targetDirectory!);
                ImGui.EndMenu();
            }
            ImGui.EndPopup();
        }
        PopContextMenuStyle();

        ImGui.SetCursorScreenPos(beforePos);
    }

private void QueueDeleteProjectAsset(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (selectedProjectEntryKeys.Count > 1 &&
            selectedProjectEntryKeys.Contains(fullPath))
        {
            QueueDeleteSelectedProjectAssets();
            return;
        }

        pendingDeleteAssetPaths.Clear();
        pendingDeleteAssetPath = fullPath;
    }

private static uint GetProjectAssetIconColor(string path) =>
        ImGui.GetColorU32(ProjectAssetColorForKind(GetAssetKind(path)));

private static System.Numerics.Vector4 ProjectAssetColorForKind(string kind) => kind switch
    {
        "MAT" => new System.Numerics.Vector4(0.45f, 0.76f, 0.80f, 1f),
        "IMG" => new System.Numerics.Vector4(0.32f, 0.62f, 0.88f, 1f),
        "CS" => new System.Numerics.Vector4(0.30f, 0.70f, 0.38f, 1f),
        "MESH" => new System.Numerics.Vector4(0.86f, 0.58f, 0.34f, 1f),
        "SUBMESH" => new System.Numerics.Vector4(0.48f, 0.78f, 0.92f, 1f),
        "AVATAR" => new System.Numerics.Vector4(0.42f, 0.78f, 0.74f, 1f),
        "ANIM" => new System.Numerics.Vector4(0.38f, 0.82f, 0.70f, 1f),
        "PREF" => new System.Numerics.Vector4(0.38f, 0.62f, 0.96f, 1f),
        "SCENE" => new System.Numerics.Vector4(0.56f, 0.72f, 0.92f, 1f),
        _ => new System.Numerics.Vector4(0.62f, 0.64f, 0.66f, 1f)
    };

private static string GetAssetKind(string path)
    {
        if (MaterialAsset.IsMaterialPath(path)) return "MAT";
        if (MaterialAsset.IsTexturePath(path)) return "IMG";
        if (ObjLoader.IsSupportedMesh(path)) return "MESH";
        if (AvatarAsset.IsAvatarPath(path)) return "AVATAR";
        if (AnimationClipAsset.IsAnimationPath(path)) return "ANIM";
        if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return "PREF";
        if (path.EndsWith(".gscene", StringComparison.OrdinalIgnoreCase)) return "SCENE";
        if (path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)) return "SHDR";
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return "CS";
        if (ScriptableObjectAsset.IsAssetPath(path)) return "SO";
        string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrWhiteSpace(ext) ? "FILE" : ext.Length <= 4 ? ext : ext[..4];
    }

private static EditorIcon IconForAssetKind(string kind) => kind switch
    {
        "MAT" => EditorIcon.Prefab,
        "SHDR" => EditorIcon.ShaderGraph,
        "MESH" => EditorIcon.Mesh,
        "SUBMESH" => EditorIcon.Mesh,
        "AVATAR" => EditorIcon.Asset,
        "ANIM" => EditorIcon.Asset,
        "PREF" => EditorIcon.Prefab,
        "SCENE" => EditorIcon.Asset,
        "CS" => EditorIcon.Script,
        "SO" => EditorIcon.Asset,
        "IMG" => EditorIcon.Asset,
        _ => EditorIcon.Asset
    };

private void DrawProjectContextMenu(string path)
    {
        PushContextMenuStyle();
        // Sin argumento: el popup se ancla al id del último item (tile/fila/nodo de carpeta),
        // único en cada sitio. Evita construir el string "ProjectContext_"+path en cada tile y frame.
        if (!ImGui.BeginPopupContextItem())
        {
            PopContextMenuStyle();
            return;
        }

        SelectProjectContextPath(path);
        bool isDirectory = Directory.Exists(path);
        bool isFile = File.Exists(path);

        if (isDirectory)
        {
            if (ImGui.BeginMenu("Create"))
            {
                DrawProjectCreateMenuItems(path);
                ImGui.EndMenu();
            }
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(rootAssetsPath), StringComparison.OrdinalIgnoreCase) &&
                ImGui.MenuItem("Move To Parent"))
            {
                string? parent = Directory.GetParent(path)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent))
                    MoveProjectAssetToDirectory(path, parent);
            }
            ImGui.Separator();
        }

        if (isFile)
        {
            if (ObjLoader.IsSupportedMesh(path) && ImGui.MenuItem("Create Mesh Object"))
                CreateMeshObject(path);
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && ImGui.MenuItem("Instantiate Prefab"))
                InstantiatePrefab(path);
            if (MaterialAsset.IsMaterialPath(path) && selected != null && ImGui.MenuItem("Apply Material"))
                ApplyMaterialToSelected(path);
            if (MaterialAsset.IsTexturePath(path) && selected != null && ImGui.MenuItem("Apply Texture"))
                ApplyTextureToSelected(path);
            if (path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) && ImGui.MenuItem("Create Material"))
                CreateMaterialFromShaderGraph(path);
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Rename"))
            BeginRenameAsset(path);
        if (ImGui.MenuItem("Delete"))
            QueueDeleteProjectAsset(path);

        ImGui.EndPopup();
        PopContextMenuStyle();
    }

private void DrawProjectBackgroundContextMenu(string? targetDirectory)
    {
        PushContextMenuStyle();
        if (!ImGui.BeginPopupContextWindow("ProjectBackgroundContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            PopContextMenuStyle();
            return;
        }

        string directory = !string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory)
            ? targetDirectory
            : rootAssetsPath;

        ClearProjectEntrySelection();
        selectedAssetPath = directory;
        if (ImGui.BeginMenu("Create"))
        {
            DrawProjectCreateMenuItems(directory);
            ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Show In Assets"))
            currentProjectDirectory = rootAssetsPath;

        ImGui.EndPopup();
        PopContextMenuStyle();
    }

private void DrawProjectCreateMenuItems(string targetDirectory)
    {
        ImGui.TextDisabled("Create asset");
        ImGui.Separator();
        if (ImGui.MenuItem("+ Folder"))
            CreateAssetFolder(targetDirectory);
        if (ImGui.MenuItem("# C# Script"))
            CreateScript(targetDirectory);
        if (ImGui.MenuItem("Player Controller Pro"))
            CreatePlayerControllerScript(targetDirectory);
        if (ImGui.MenuItem("o Material"))
        {
            selectedAssetPath = MaterialAsset.Create(targetDirectory);
            statusMessage = "Material created";
        }
        if (ImGui.MenuItem("Animation Clip"))
        {
            selectedAssetPath = AnimationClipAsset.Create(targetDirectory);
            statusMessage = "Animation Clip created";
        }
        if (ImGui.MenuItem("Animator Controller"))
        {
            selectedAssetPath = AnimatorControllerAsset.Create(targetDirectory);
            statusMessage = "Animator Controller created";
        }
        if (ImGui.MenuItem("Shader Graph"))
        {
            string path = CreateShaderGraphAsset(targetDirectory);
            selectedAssetPath = path;
            BeginRenameAsset(path);
            statusMessage = "Shader Graph created";
        }

        if (scriptCompiler.ScriptableObjectTypes.Count > 0 && ImGui.BeginMenu("ScriptableObject"))
        {
            foreach (Type type in scriptCompiler.ScriptableObjectTypes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                Type capturedType = type;
                if (ImGui.MenuItem(capturedType.Name))
                {
                    selectedAssetPath = ScriptableObjectAsset.Create(targetDirectory, capturedType, physicsEngine: physicsEngine);
                    statusMessage = $"{capturedType.Name} asset created";
                }
            }
            ImGui.EndMenu();
        }
    }

private void DrawDeleteAssetPopup()
    {
        if (pendingDeleteAssetPath != null || pendingDeleteAssetPaths.Count > 0)
            ImGui.OpenPopup("Delete Asset");

        ImGui.SetNextWindowSize(new Vector2(320f, 0f), ImGuiCond.Appearing);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 16f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new System.Numerics.Vector4(0.130f, 0.130f, 0.140f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(0.06f, 0.06f, 0.07f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.85f, 0.86f, 0.88f, 1f));

        if (!ImGui.BeginPopupModal("Delete Asset", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize))
        {
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(4);
            return;
        }

        int deleteCount = pendingDeleteAssetPaths.Count > 0 ? pendingDeleteAssetPaths.Count : (pendingDeleteAssetPath != null ? 1 : 0);
        string name = deleteCount > 1
            ? $"{deleteCount} assets"
            : pendingDeleteAssetPath == null
                ? (pendingDeleteAssetPaths.Count == 1 ? Path.GetFileName(pendingDeleteAssetPaths[0]) : "")
                : Path.GetFileName(pendingDeleteAssetPath);
        ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.40f, 0.36f, 1f), "Eliminar asset");
        ImGui.Spacing();
        ImGui.TextWrapped($"¿Seguro que quieres eliminar \"{name}\"?");
        ImGui.TextDisabled("Esta acción no se puede deshacer.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float buttonWidth = (ImGui.GetContentRegionAvail().X - 8f) * 0.5f;
        ImGui.PushStyleColor(ImGuiCol.Button, UiDanger);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiDangerHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiDangerHover);
        if (ImGui.Button("Eliminar", new Vector2(buttonWidth, 30f)) && deleteCount > 0)
        {
            string[] paths = pendingDeleteAssetPaths.Count > 0
                ? pendingDeleteAssetPaths.ToArray()
                : new[] { pendingDeleteAssetPath! };
            pendingDeleteAssetPath = null;
            pendingDeleteAssetPaths.Clear();
            foreach (string path in paths)
                DeleteAsset(path);
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(3);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.24f, 0.24f, 0.26f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.30f, 0.30f, 0.33f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.34f, 0.34f, 0.37f, 1f));
        if (ImGui.Button("Cancelar", new Vector2(buttonWidth, 30f)) || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            pendingDeleteAssetPath = null;
            pendingDeleteAssetPaths.Clear();
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(3);

        ImGui.EndPopup();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(4);
    }

private void DrawAssetActions()
    {
        if (selectedAssetPath == null) return;

        if (File.Exists(selectedAssetPath) && selected != null)
        {
            if (ObjLoader.IsSupportedMesh(selectedAssetPath))
            {
                if (ImGui.Button("Assign Mesh"))
                    AssignMeshToSelected(selectedAssetPath);
                ImGui.SameLine();
                if (ImGui.Button("Create Mesh Object"))
                    CreateMeshObject(selectedAssetPath);
            }
            else if (MaterialAsset.IsMaterialPath(selectedAssetPath))
            {
                if (ImGui.Button("Apply Material"))
                    ApplyMaterialToSelected(selectedAssetPath);
            }
            else if (MaterialAsset.IsTexturePath(selectedAssetPath))
            {
                if (ImGui.Button("Apply Texture"))
                    ApplyTextureToSelected(selectedAssetPath);
            }
            else if (selectedAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                if (ImGui.Button("Instantiate Prefab"))
                    InstantiatePrefab(selectedAssetPath);
            }
        }

        if (Directory.Exists(selectedAssetPath))
        {
            if (ImGui.Button("Create Material"))
            {
                selectedAssetPath = MaterialAsset.Create(selectedAssetPath);
                statusMessage = "Material created";
            }
        }

        if (selected != null && ImGui.Button("Create Prefab From Selected"))
        {
            string target = Directory.Exists(selectedAssetPath)
                ? selectedAssetPath
                : Path.GetDirectoryName(selectedAssetPath) ?? rootAssetsPath;
            selectedAssetPath = assetService.CreatePrefab(target, selected);
            statusMessage = "Prefab created";
        }
    }

private void CreateAssetFolder(string parentDirectory)
    {
        if (!Directory.Exists(parentDirectory) || !IsInsideAssets(parentDirectory)) return;
        string basePath = Path.Combine(parentDirectory, "New Folder");
        string path = basePath;
        int i = 1;
        while (Directory.Exists(path))
            path = basePath + " " + i++;

        Directory.CreateDirectory(path);
        selectedAssetPath = path;
        statusMessage = "Folder created";
    }

private string CreateShaderGraphAsset(string parentDirectory)
    {
        Directory.CreateDirectory(parentDirectory);
        string basePath = Path.Combine(parentDirectory, "New Shader Graph");
        string path = basePath + ".shadergraph";
        int i = 1;
        while (File.Exists(path))
            path = basePath + " " + i++ + ".shadergraph";

        var model = ShaderGraphTemplates.Empty();
        model.Name = Path.GetFileNameWithoutExtension(path);
        GrokoShaderGraphPro.Services.GraphSerializer.Save(path, model);
        assetService.AssetDatabase.GetOrCreateGuid(path);
        return path;
    }

private void CreateMaterialFromShaderGraph(string shaderGraphPath)
    {
        string directory = Path.GetDirectoryName(shaderGraphPath) ?? rootAssetsPath;
        string baseName = Path.GetFileNameWithoutExtension(shaderGraphPath);
        string matPath = MaterialAsset.Create(directory, baseName);

        var data = MaterialAsset.Load(matPath);
        data.ShaderGraphPath = shaderGraphPath;
        MaterialAsset.Save(matPath, data);

        selectedAssetPath = matPath;
        BeginRenameAsset(matPath);
    }

private void OpenShaderGraphAsset(string path)
    {
        try
        {
            shaderGraphModel = GrokoShaderGraphPro.Services.GraphSerializer.Load(path);
        }
        catch
        {
            shaderGraphModel = ShaderGraphTemplates.Empty();
            shaderGraphModel.Name = Path.GetFileNameWithoutExtension(path);
        }

        shaderGraphAssetPath = path;
        shaderGraphCode = string.Empty;
        shaderGraphPreviewedCode = string.Empty;
        shaderGraphStatus = string.Empty;
        shaderGraphSelectedNodeId = null;
        shaderGraphSelectedPropertyId = null;
        ApplyShaderGraphEditorStateFromModel();
        showShaderGraph = true;
    }

private void BeginRenameAsset(string path)
    {
        if (!IsInsideAssets(path)) return;
        inlineRenameAssetPath = path;
        inlineRenameAssetName = Directory.Exists(path) ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        inlineRenameFocusPending = true;
    }

private void CommitInlineRenameAsset()
    {
        string? path = inlineRenameAssetPath;
        string newBase = inlineRenameAssetName;
        inlineRenameAssetPath = null;
        if (path == null || string.IsNullOrWhiteSpace(newBase)) return;

        string ext = Directory.Exists(path) ? "" : Path.GetExtension(path);
        renameAssetPath = path;
        renameAssetName = newBase.Trim() + ext;
        RenameAsset();
    }

private void CancelInlineRenameAsset()
    {
        inlineRenameAssetPath = null;
        inlineRenameAssetName = string.Empty;
    }

private void RenameAsset()
    {
        if (renameAssetPath == null || !IsInsideAssets(renameAssetPath)) return;
        string cleanName = SanitizeAssetName(renameAssetName);
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            statusMessage = "Invalid asset name";
            return;
        }

        string parent = Path.GetDirectoryName(renameAssetPath) ?? rootAssetsPath;
        string target = Path.Combine(parent, cleanName);
        if (string.Equals(renameAssetPath, target, StringComparison.OrdinalIgnoreCase))
        {
            renameAssetPath = null;
            return;
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            statusMessage = "An asset with that name already exists";
            return;
        }

        string source = Path.GetFullPath(renameAssetPath);
        string destination = Path.GetFullPath(target);

        if (!assetService.MoveAsset(source, destination, out string renameError))
        {
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                renameAssetPath = null;
                renameAssetName = string.Empty;
                statusMessage = "Asset no longer exists";
            }
            else
            {
                statusMessage = "Rename failed: " + renameError;
            }
            return;
        }

        selectedAssetPath = RemapPathAfterMove(selectedAssetPath, source, destination) ?? destination;
        lockedInspectorAssetPath = RemapPathAfterMove(lockedInspectorAssetPath, source, destination);
        shaderGraphAssetPath = RemapPathAfterMove(shaderGraphAssetPath, source, destination);
        currentProjectDirectory = RemapPathAfterMove(currentProjectDirectory, source, destination) ?? currentProjectDirectory;
        renameAssetPath = null;
        renameAssetName = string.Empty;
        statusMessage = "Asset renamed";

        if (destination.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var graph = GrokoShaderGraphPro.Services.GraphSerializer.Load(destination);
                graph.Name = Path.GetFileNameWithoutExtension(destination);
                GrokoShaderGraphPro.Services.GraphSerializer.Save(destination, graph);
            }
            catch { /* keep existing graph contents if it can't be re-saved */ }

            OpenShaderGraphAsset(destination);
        }
    }

private void MoveProjectAssetToDirectory(string sourcePath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetDirectory))
            return;

        string sourceFull = Path.GetFullPath(sourcePath);
        string targetDirFull = Path.GetFullPath(targetDirectory);
        string assetsFull = Path.GetFullPath(rootAssetsPath);

        if (!IsInsideAssets(sourceFull) || !IsInsideAssets(targetDirFull) || !Directory.Exists(targetDirFull))
        {
            statusMessage = "Invalid target folder";
            return;
        }

        if (string.Equals(sourceFull, assetsFull, StringComparison.OrdinalIgnoreCase))
        {
            statusMessage = "Assets root cannot be moved";
            return;
        }

        bool sourceIsDirectory = Directory.Exists(sourceFull);
        bool sourceIsFile = File.Exists(sourceFull);
        if (!sourceIsDirectory && !sourceIsFile)
            return;

        string currentParent = Path.GetFullPath(Path.GetDirectoryName(sourceFull) ?? assetsFull);
        if (string.Equals(currentParent, targetDirFull, StringComparison.OrdinalIgnoreCase))
        {
            statusMessage = "Asset is already in that folder";
            return;
        }

        if (sourceIsDirectory && IsPathInsideDirectory(targetDirFull, sourceFull))
        {
            statusMessage = "Cannot move a folder inside itself";
            return;
        }

        string destination = Path.Combine(targetDirFull, Path.GetFileName(sourceFull));
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            statusMessage = "Target folder already has an asset with that name";
            return;
        }

        if (assetService.MoveAsset(sourceFull, destination, out string moveError))
        {
            selectedAssetPath = RemapPathAfterMove(selectedAssetPath, sourceFull, destination) ?? destination;
            lockedInspectorAssetPath = RemapPathAfterMove(lockedInspectorAssetPath, sourceFull, destination);
            shaderGraphAssetPath = RemapPathAfterMove(shaderGraphAssetPath, sourceFull, destination);
            if (currentProjectDirectory != null)
                currentProjectDirectory = RemapPathAfterMove(currentProjectDirectory, sourceFull, destination);

            RemapProjectSelectionAfterMove(sourceFull, destination);
            InvalidateProjectFolderCache(Path.GetDirectoryName(sourceFull));
            InvalidateProjectFolderCache(Path.GetDirectoryName(destination));
            statusMessage = "Asset moved";
        }
        else
        {
            statusMessage = "Move failed: " + moveError;
        }
    }

private void DeleteAsset(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!IsInsideAssets(fullPath) || string.Equals(fullPath, Path.GetFullPath(rootAssetsPath), StringComparison.OrdinalIgnoreCase))
            return;

        if (!assetService.DeleteAsset(fullPath, out string deleteError))
        {
            statusMessage = "Delete failed: " + deleteError;
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedAssetPath) &&
            (string.Equals(Path.GetFullPath(selectedAssetPath), fullPath, StringComparison.OrdinalIgnoreCase) ||
             IsPathInsideDirectory(selectedAssetPath, fullPath)))
            selectedAssetPath = rootAssetsPath;
        if (!string.IsNullOrWhiteSpace(lockedInspectorAssetPath) &&
            (string.Equals(Path.GetFullPath(lockedInspectorAssetPath), fullPath, StringComparison.OrdinalIgnoreCase) ||
             IsPathInsideDirectory(lockedInspectorAssetPath, fullPath)))
            lockedInspectorAssetPath = null;
        if (!string.IsNullOrWhiteSpace(shaderGraphAssetPath) &&
            (string.Equals(Path.GetFullPath(shaderGraphAssetPath), fullPath, StringComparison.OrdinalIgnoreCase) ||
             IsPathInsideDirectory(shaderGraphAssetPath, fullPath)))
        {
            shaderGraphAssetPath = null;
            showShaderGraph = false;
        }
        if (currentProjectDirectory != null &&
            (string.Equals(Path.GetFullPath(currentProjectDirectory), fullPath, StringComparison.OrdinalIgnoreCase) ||
             IsPathInsideDirectory(currentProjectDirectory, fullPath)))
            currentProjectDirectory = rootAssetsPath;
        InvalidateProjectFolderCache(Path.GetDirectoryName(fullPath));
        statusMessage = "Asset deleted";
    }

private bool IsInsideAssets(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(rootAssetsPath);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

private static bool IsPathInsideDirectory(string path, string directory)
    {
        string fullPath = Path.GetFullPath(path);
        string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

private static string? RemapPathAfterMove(string? path, string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string fullPath = Path.GetFullPath(path);
        string fullOld = Path.GetFullPath(oldPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullNew = Path.GetFullPath(newPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(fullPath, fullOld, StringComparison.OrdinalIgnoreCase))
            return fullNew;

        if (IsPathInsideDirectory(fullPath, fullOld))
            return fullNew + fullPath[fullOld.Length..];

        return path;
    }

private static string SanitizeAssetName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
