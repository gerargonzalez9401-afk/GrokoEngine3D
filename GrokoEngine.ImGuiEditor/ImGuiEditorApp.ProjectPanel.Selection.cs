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
private void DrawSelectedAssetDetail()
    {
        if (selectedAssetPath == null)
            return;

        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiPanelSoft);
        ImGui.BeginChild("SelectedAssetDetail", new Vector2(0f, 50f), ImGuiChildFlags.None);
        bool folder = Directory.Exists(selectedAssetPath);
        string kind = folder ? "FOLDER" : GetAssetKind(selectedAssetPath);
        var drawList = ImGui.GetWindowDrawList();
        var previewMin = ImGui.GetCursorScreenPos() + new Vector2(2f, 3f);
        DrawProjectAssetPreview(drawList, selectedAssetPath, folder, previewMin, 34f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 46f);
        ImGui.Text(kind);
        ImGui.SameLine();
        ImGui.Text(Path.GetFileName(selectedAssetPath));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 46f);
        ImGui.TextDisabled(IsInsideAssets(selectedAssetPath)
            ? Path.GetRelativePath(rootAssetsPath, selectedAssetPath).Replace('\\', '/')
            : selectedAssetPath);
        if (!folder && File.Exists(selectedAssetPath))
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - 92f);
            ImGui.TextDisabled($"{new FileInfo(selectedAssetPath).Length / 1024f:F1} KB");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

private bool TryDuplicateSelectedProjectAnimationSubAsset()
    {
        if (string.IsNullOrWhiteSpace(selectedProjectSubAssetKey))
            return false;
        if (!TryParseProjectSubAssetKey(selectedProjectSubAssetKey, out var parentPath, out var kind, out var index))
            return false;
        if (!string.Equals(kind, "animation", StringComparison.OrdinalIgnoreCase))
            return false;

        return ExtractImportedAnimationClip(parentPath, index, selectCreated: true) != null;
    }

private static string ProjectEntrySelectionKey(ProjectAssetEntry entry) => entry.Path;

private bool IsProjectEntrySelected(ProjectAssetEntry entry)
    {
        string key = ProjectEntrySelectionKey(entry);
        if (selectedProjectEntryKeys.Contains(key))
            return true;

        if (selectedProjectEntryKeys.Count > 0)
            return false;

        if (entry.IsVirtualSubAsset)
            return string.Equals(selectedProjectSubAssetKey, entry.Path, StringComparison.OrdinalIgnoreCase);

        return selectedProjectSubAssetKey == null &&
               string.Equals(selectedAssetPath, entry.Path, StringComparison.OrdinalIgnoreCase);
    }

private void RegisterProjectAssetSelectionRect(ProjectAssetEntry entry, Vector2 min, Vector2 max)
    {
        string key = ProjectEntrySelectionKey(entry);
        projectAssetSelectionRects.Add(new ProjectAssetSelectionRect(key, min, max));
        projectAssetSelectionEntries[key] = entry;
    }

private void SetProjectPrimarySelection(ProjectAssetEntry entry)
    {
        selected = null;
        if (entry.IsVirtualSubAsset)
        {
            selectedProjectSubAssetKey = entry.Path;
            selectedAssetPath = !string.IsNullOrWhiteSpace(entry.SourceMaterialPath) && File.Exists(entry.SourceMaterialPath)
                ? entry.SourceMaterialPath
                : !string.IsNullOrWhiteSpace(entry.SourceAvatarPath) && File.Exists(entry.SourceAvatarPath)
                    ? entry.SourceAvatarPath
                    : entry.ParentPath;
            return;
        }

        selectedProjectSubAssetKey = null;
        selectedAssetPath = entry.Path;
    }

private void ClearProjectEntrySelection(bool clearSceneSelection = true)
    {
        selectedProjectEntryKeys.Clear();
        selectedProjectSubAssetKey = null;
        selectedAssetPath = null;
        projectSelectionAnchorKey = null;

        if (clearSceneSelection)
            selection.Clear();
    }

private void SelectProjectEntry(ProjectAssetEntry entry)
    {
        selectedProjectEntryKeys.Clear();
        selectedProjectEntryKeys.Add(ProjectEntrySelectionKey(entry));
        SetProjectPrimarySelection(entry);
        projectSelectionAnchorKey = ProjectEntrySelectionKey(entry);
    }

private void ToggleProjectEntrySelection(ProjectAssetEntry entry, IReadOnlyList<ProjectAssetEntry> visibleEntries)
    {
        string key = ProjectEntrySelectionKey(entry);
        if (selectedProjectEntryKeys.Remove(key))
        {
            if (selectedProjectEntryKeys.Count == 0)
            {
                ClearProjectEntrySelection(clearSceneSelection: false);
                return;
            }

            ProjectAssetEntry? next = visibleEntries.FirstOrDefault(candidate =>
                selectedProjectEntryKeys.Contains(ProjectEntrySelectionKey(candidate)));
            if (next != null)
                SetProjectPrimarySelection(next);
            return;
        }

        selectedProjectEntryKeys.Add(key);
        SetProjectPrimarySelection(entry);
        projectSelectionAnchorKey = key;
    }

private void SelectProjectEntryRange(IReadOnlyList<ProjectAssetEntry> visibleEntries, int index, bool additive)
    {
        if (visibleEntries.Count == 0)
            return;

        if (!additive)
            selectedProjectEntryKeys.Clear();

        int anchorIndex = -1;
        if (!string.IsNullOrWhiteSpace(projectSelectionAnchorKey))
        {
            for (int i = 0; i < visibleEntries.Count; i++)
            {
                if (string.Equals(ProjectEntrySelectionKey(visibleEntries[i]), projectSelectionAnchorKey, StringComparison.OrdinalIgnoreCase))
                {
                    anchorIndex = i;
                    break;
                }
            }
        }

        if (anchorIndex < 0)
            anchorIndex = index;

        int start = Math.Min(anchorIndex, index);
        int end = Math.Max(anchorIndex, index);
        for (int i = start; i <= end; i++)
            selectedProjectEntryKeys.Add(ProjectEntrySelectionKey(visibleEntries[i]));

        SetProjectPrimarySelection(visibleEntries[index]);
    }

private void HandleProjectEntrySelection(ProjectAssetEntry entry, IReadOnlyList<ProjectAssetEntry> visibleEntries, int index)
    {
        var io = ImGui.GetIO();
        if (io.KeyShift)
            SelectProjectEntryRange(visibleEntries, index, additive: io.KeyCtrl);
        else if (io.KeyCtrl)
            ToggleProjectEntrySelection(entry, visibleEntries);
        else
            SelectProjectEntry(entry);
    }

private bool IsMeshEntryExpanded(ProjectAssetEntry entry) =>
        !entry.IsDirectory && entry.Kind == "MESH" && expandedMeshAssetPaths.Contains(entry.Path);

private void ApplyProjectAssetBoxSelection(bool final)
    {
        var min = new Vector2(
            MathF.Min(projectAssetBoxStart.X, projectAssetBoxCurrent.X),
            MathF.Min(projectAssetBoxStart.Y, projectAssetBoxCurrent.Y));
        var max = new Vector2(
            MathF.Max(projectAssetBoxStart.X, projectAssetBoxCurrent.X),
            MathF.Max(projectAssetBoxStart.Y, projectAssetBoxCurrent.Y));

        bool hasArea = MathF.Abs(max.X - min.X) > 4f || MathF.Abs(max.Y - min.Y) > 4f;
        if (!hasArea)
        {
            if (final && !projectAssetBoxSelectAdditive)
                ClearProjectEntrySelection();
            return;
        }

        var nextSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (projectAssetBoxSelectAdditive)
        {
            foreach (string key in projectAssetBoxSelectionBase)
                nextSelection.Add(key);
        }

        ProjectAssetEntry? primaryEntry = null;
        foreach (ProjectAssetSelectionRect rect in projectAssetSelectionRects)
        {
            if (!ProjectSelectionRectsIntersect(min, max, rect.Min, rect.Max))
                continue;

            nextSelection.Add(rect.Key);
            if (projectAssetSelectionEntries.TryGetValue(rect.Key, out var entry))
                primaryEntry = entry;
        }

        selectedProjectEntryKeys.Clear();
        foreach (string key in nextSelection)
            selectedProjectEntryKeys.Add(key);

        if (primaryEntry != null)
        {
            SetProjectPrimarySelection(primaryEntry);
            projectSelectionAnchorKey = ProjectEntrySelectionKey(primaryEntry);
        }
        else if (selectedProjectEntryKeys.Count == 0 && !projectAssetBoxSelectAdditive)
        {
            ClearProjectEntrySelection();
        }
    }

private void DrawProjectAssetBoxSelection()
    {
        var min = new Vector2(
            MathF.Min(projectAssetBoxStart.X, projectAssetBoxCurrent.X),
            MathF.Min(projectAssetBoxStart.Y, projectAssetBoxCurrent.Y));
        var max = new Vector2(
            MathF.Max(projectAssetBoxStart.X, projectAssetBoxCurrent.X),
            MathF.Max(projectAssetBoxStart.Y, projectAssetBoxCurrent.Y));

        if (MathF.Abs(max.X - min.X) <= 4f && MathF.Abs(max.Y - min.Y) <= 4f)
            return;

        var drawList = ImGui.GetWindowDrawList();
        uint fill = ImGui.GetColorU32(new System.Numerics.Vector4(0.22f, 0.48f, 0.86f, 0.20f));
        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(0.44f, 0.72f, 1f, 0.92f));
        drawList.AddRectFilled(min, max, fill, 2f);
        drawList.AddRect(min, max, line, 2f, ImDrawFlags.None, 1.2f);
    }

private static bool ProjectSelectionRectsIntersect(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax) =>
        aMin.X <= bMax.X && aMax.X >= bMin.X &&
        aMin.Y <= bMax.Y && aMax.Y >= bMin.Y;

private List<string> GetSelectedProjectAssetPathsForFileOperation()
    {
        var paths = new List<string>();
        foreach (string key in selectedProjectEntryKeys)
        {
            string? path = null;
            if (projectAssetSelectionEntries.TryGetValue(key, out var entry))
                path = entry.SourceMaterialPath ?? entry.SourceAvatarPath ?? (entry.IsVirtualSubAsset ? null : entry.Path);
            else if (File.Exists(key) || Directory.Exists(key))
                path = key;

            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        if (paths.Count == 0 && selectedProjectSubAssetKey == null && !string.IsNullOrWhiteSpace(selectedAssetPath))
            paths.Add(selectedAssetPath);

        return NormalizeProjectFileOperationPaths(paths);
    }

private void QueueDeleteSelectedProjectAssets()
    {
        List<string> paths = GetSelectedProjectAssetPathsForFileOperation();
        if (paths.Count == 0)
            return;

        pendingDeleteAssetPaths.Clear();
        pendingDeleteAssetPaths.AddRange(paths);
        pendingDeleteAssetPath = paths.Count == 1 ? paths[0] : null;
    }

private void SelectProjectContextPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        selected = null;
        selectedProjectSubAssetKey = null;
        selectedAssetPath = fullPath;

        if (selectedProjectEntryKeys.Contains(fullPath))
            return;

        selectedProjectEntryKeys.Clear();
        selectedProjectEntryKeys.Add(fullPath);
        projectSelectionAnchorKey = fullPath;
    }

private void RemapProjectSelectionAfterMove(string source, string destination)
    {
        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in selectedProjectEntryKeys)
            next.Add(RemapProjectEntryKeyAfterMove(key, source, destination));

        selectedProjectEntryKeys.Clear();
        foreach (string key in next)
            selectedProjectEntryKeys.Add(key);

        if (!string.IsNullOrWhiteSpace(projectSelectionAnchorKey))
            projectSelectionAnchorKey = RemapProjectEntryKeyAfterMove(projectSelectionAnchorKey, source, destination);
        if (!string.IsNullOrWhiteSpace(selectedProjectSubAssetKey))
            selectedProjectSubAssetKey = RemapProjectEntryKeyAfterMove(selectedProjectSubAssetKey, source, destination);
    }
}
