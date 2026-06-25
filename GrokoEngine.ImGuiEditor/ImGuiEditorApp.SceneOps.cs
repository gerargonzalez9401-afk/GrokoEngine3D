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
    private T CommitSceneMutation<T>(string label, Func<T> mutation)
    {
        if (suppressHistory || isPlaying)
            return mutation();

        var before = CaptureSceneState();
        suppressHistory = true;
        try
        {
            T result = mutation();
            var after = CaptureSceneState();
            suppressHistory = false;
            PushSceneState(label, before, after);
            if (!string.Equals(before.Json, after.Json, StringComparison.Ordinal))
            {
                sceneRenderer.InvalidateStaticBatch();
                sceneRenderer.InvalidateCullingState();
            }
            return result;
        }
        finally
        {
            suppressHistory = false;
        }
    }

    private SceneStateSnapshot CaptureSceneState() =>
        new(SceneSerializer.Serialize(objects), selection.CaptureSelectedIds());

    private void PushSceneState(string label, SceneStateSnapshot before, SceneStateSnapshot after)
    {
        if (suppressHistory) return;
        if (string.Equals(before.Json, after.Json, StringComparison.Ordinal) &&
            before.SelectedIds.SequenceEqual(after.SelectedIds))
            return;

        sceneHistory.Push(new SnapshotSceneCommand(this, label, before, after), execute: false);
    }

    private void RestoreSceneState(SceneStateSnapshot state)
    {
        suppressHistory = true;
        try
        {
            physicsEngine.ClearColliders();
            objects.Clear();
            objects.AddRange(SceneSerializer.Deserialize(state.Json, physicsEngine, scriptCompiler));
            selection.RestoreSelectedIds(state.SelectedIds);
            sceneRenderer.InvalidateStaticBatch();
            sceneRenderer.InvalidateCullingState();
        }
        finally
        {
            suppressHistory = false;
        }
    }

    private void UndoScene()
    {
        if (isPlaying)
        {
            statusMessage = "Cannot undo while playing";
            return;
        }

        statusMessage = sceneHistory.Undo() ? "Undo" : "Nothing to undo";
    }

    private void RedoScene()
    {
        if (isPlaying)
        {
            statusMessage = "Cannot redo while playing";
            return;
        }

        statusMessage = sceneHistory.Redo() ? "Redo" : "Nothing to redo";
    }

    private GameObject CreateEmpty() =>
        CommitSceneMutation("Create Empty", () => CreateObject("GameObject", 0));

    private GameObject CreateObject(string baseName, int type)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot create objects while playing";
            return new GameObject { Name = "Blocked", Type = type };
        }

        var obj = new GameObject { Name = GetUniqueObjectName(baseName), Type = type };
        objects.Add(obj);
        selected = obj;
        statusMessage = $"Created {obj.Name}";
        return obj;
    }

    // ── Creación de UI (estilo Unity: el objeto se crea ya con su componente) ──
    private GameObject CreateCanvas() => CommitSceneMutation("Create Canvas", () =>
    {
        var obj = CreateObject("Canvas", 0);
        obj.AddComponent<Canvas>();
        return obj;
    });

    // Crea un elemento de UI como hijo de un Canvas (crea uno si no existe).
    private GameObject CreateUIElement<T>(string name) where T : Component, new()
        => CommitSceneMutation("Create " + name, () =>
        {
            var canvas = FindCanvasObject();
            if (canvas == null)
            {
                canvas = CreateObject("Canvas", 0);
                canvas.AddComponent<Canvas>();
            }
            var obj = new GameObject { Name = GetUniqueChildObjectName(canvas, name), Type = 0, Parent = canvas };
            obj.AddComponent<T>();
            selected = obj;
            statusMessage = $"Created {obj.Name}";
            return obj;
        });

    private GameObject? FindCanvasObject()
    {
        foreach (var root in objects)
        {
            var found = FindCanvasRecursive(root);
            if (found != null) return found;
        }
        return null;
    }

    private static GameObject? FindCanvasRecursive(GameObject go)
    {
        if (go.GetComponent<Canvas>() != null) return go;
        foreach (var child in go.Children)
        {
            var found = FindCanvasRecursive(child);
            if (found != null) return found;
        }
        return null;
    }

    private GameObject CreateChild(GameObject parent, string baseName, int type)
    {
        return CommitSceneMutation("Create Child " + baseName, () =>
        {
            var obj = new GameObject
            {
                Name = GetUniqueChildObjectName(parent, baseName),
                Type = type,
                Parent = parent
            };
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildCube(GameObject parent)
    {
        return CommitSceneMutation("Create Child Cube", () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, "Cube"), Type = 1, Parent = parent };
            obj.AddComponentWithEngine<BoxCollider>(physicsEngine);
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildPlane(GameObject parent)
    {
        return CommitSceneMutation("Create Child Plane", () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, "Plane"), Type = 2, Parent = parent, ScaleX = 4f, ScaleZ = 4f };
            var collider = obj.AddComponentWithEngine<BoxCollider>(physicsEngine);
            collider.Size = new Vector3(1f, 0.05f, 1f);
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildPrimitive(GameObject parent, string name, int type)
    {
        return CommitSceneMutation("Create Child " + name, () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, name), Type = type, Parent = parent };
            AddDefaultPrimitiveCollider(obj);
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildCamera(GameObject parent)
    {
        return CommitSceneMutation("Create Child Camera", () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, "Main Camera"), Type = 0, Parent = parent, IsCamera = true, PosY = 1f, PosZ = 5f, RotY = 180f };
            obj.AddComponent<Camera>();
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildLight<T>(GameObject parent, string name) where T : Component, new()
    {
        return CommitSceneMutation("Create Child " + name, () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, name), Type = 0, Parent = parent, PosY = 2f };
            obj.AddComponent<T>();
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildTerrain(GameObject parent)
    {
        return CommitSceneMutation("Create Child Terrain", () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, "Terrain"), Type = 0, Parent = parent };
            obj.AddComponent<Terrain>();
            obj.AddComponentWithEngine<TerrainCollider>(physicsEngine);
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildCubeWithGravity(GameObject parent)
    {
        return CommitSceneMutation("Create Child Cube With Gravity", () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, "Cube"), Type = 1, Parent = parent, PosY = 3f };
            obj.AddComponentWithEngine<BoxCollider>(physicsEngine);
            obj.AddComponentWithEngine<Rigidbody>(physicsEngine);
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private GameObject CreateChildParticleSystem(GameObject parent)
    {
        return CommitSceneMutation("Create Child Particle System", () =>
        {
            var obj = new GameObject { Name = GetUniqueChildObjectName(parent, "Particle System"), Type = 0, Parent = parent };
            AddPreviewParticleSystem(obj);
            selected = obj;
            statusMessage = $"Created child {obj.Name}";
            return obj;
        });
    }

    private void DrawAssetSlot(string label, string value, string emptyText, Action<string> onDrop, Func<string, bool>? assetFilter = null)
    {
        FieldRow(label);   // nombre a la izquierda alineado + margen izq, igual que los demás campos
        ImGui.PushID(label);
        float slotH = currentDrawingApp?.guiAssetSlotHeight ?? 20f;
        bool empty = string.IsNullOrWhiteSpace(value);

        float available = ImGui.GetContentRegionAvail().X - 8f;   // margen derecho
        float pickerW = available > 72f ? 20f : 0f;
        float width = Math.Max(34f, available - pickerW - (pickerW > 0f ? 4f : 0f));
        string display = empty ? emptyText : Path.GetFileName(value);
        display = Ellipsize(display, Math.Max(4, (int)(width / 7.5f)));

        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.17f, 0.20f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, empty
            ? new System.Numerics.Vector4(0.52f, 0.55f, 0.60f, 1f)
            : new System.Numerics.Vector4(0.85f, 0.87f, 0.90f, 1f));
        if (ImGui.Button(display + "##assetField", new Vector2(width, slotH)))
        {
            assetSlotSearch = string.Empty;
            ImGui.OpenPopup("AssetSlotPicker");
        }
        RegisterGuiElement(GuiStyleClass.AssetSlot, label);
        ImGui.PopStyleColor(3);
        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) &&
                (assetFilter == null || assetFilter(draggingAssetPath)))
            {
                onDrop(draggingAssetPath);
                statusMessage = "Assigned " + Path.GetFileName(draggingAssetPath);
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }

        if (pickerW > 0f)
        {
            ImGui.SameLine(0f, 4f);
            bool pickAssetClicked = ImGui.Button("##pickAsset", new Vector2(pickerW, slotH));
            DrawPickerIcon();   // ícono ◉ como en las referencias (estilo Unity / Mesh Filter)
            if (pickAssetClicked)
            {
                assetSlotSearch = string.Empty;
                ImGui.OpenPopup("AssetSlotPicker");
            }
        }
        DrawAssetSlotPickerPopup(value, onDrop, assetFilter);
        ImGui.PopID();
    }

    private void DrawTextureMapSlot(string label, string value, string emptyText, Action<string> onChange)
    {
        ImGui.PushID(label);

        float rowH = currentDrawingApp?.guiAssetSlotHeight ?? 20f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 18f);
        float available = Math.Max(96f, ImGui.GetContentRegionAvail().X - 10f);
        bool hasTexture = !string.IsNullOrWhiteSpace(value);
        bool enabled = hasTexture;
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (SmallCheckbox("##TextureMapEnabled", ref enabled))
        {
            if (!enabled)
                onChange("");
            else
            {
                assetSlotSearch = string.Empty;
                ImGui.OpenPopup("AssetSlotPicker");
            }
        }
        RegisterGuiElement(GuiStyleClass.Checkbox, label);

        Vector2 checkMax = ImGui.GetItemRectMax();
        float previewSize = Math.Clamp(rowH - 2f, 16f, 20f);
        Vector2 previewMin = new(checkMax.X + 7f, rowMin.Y + Math.Max(0f, (rowH - previewSize) * 0.5f));
        Vector2 previewMax = previewMin + new Vector2(previewSize, previewSize);
        DrawTextureMapPreviewBox(value, previewMin, previewSize);

        ImGui.SetCursorScreenPos(previewMin);
        if (ImGui.InvisibleButton("##TexturePreviewPicker", new Vector2(previewSize, previewSize)))
        {
            assetSlotSearch = string.Empty;
            ImGui.OpenPopup("AssetSlotPicker");
        }
        RegisterGuiElement(GuiStyleClass.AssetSlot, label + " Preview");
        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) && MaterialAsset.IsTexturePath(draggingAssetPath))
            {
                onChange(draggingAssetPath);
                statusMessage = "Assigned " + Path.GetFileName(draggingAssetPath);
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }

        float labelX = previewMax.X + 7f;
        float hitW = Math.Max(42f, rowMin.X + available - labelX);
        string displayLabel = hasTexture
            ? $"{label}: {Path.GetFileName(value)}"
            : label;
        displayLabel = Ellipsize(displayLabel, Math.Max(5, (int)(hitW / 7.2f)));

        ImGui.SetCursorScreenPos(new Vector2(labelX, rowMin.Y));
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.20f, 0.20f, 0.205f, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.24f, 0.24f, 0.25f, 0.65f));
        if (ImGui.Button("##TextureMapRow", new Vector2(hitW, rowH)))
        {
            assetSlotSearch = string.Empty;
            ImGui.OpenPopup("AssetSlotPicker");
        }
        Vector2 textMin = ImGui.GetItemRectMin() + new Vector2(2f, 3f);
        uint textColor = ImGui.GetColorU32(hasTexture
            ? new System.Numerics.Vector4(0.70f, 0.70f, 0.70f, 1f)
            : new System.Numerics.Vector4(0.56f, 0.56f, 0.56f, 1f));
        drawList.AddText(textMin, textColor, displayLabel);
        RegisterGuiElement(GuiStyleClass.AssetSlot, label);
        ImGui.PopStyleColor(3);
        if (ImGui.IsItemHovered() && hasTexture)
            DrawTooltip(Path.GetFileName(value));

        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) && MaterialAsset.IsTexturePath(draggingAssetPath))
            {
                onChange(draggingAssetPath);
                statusMessage = "Assigned " + Path.GetFileName(draggingAssetPath);
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }

        DrawAssetSlotPickerPopup(value, path =>
        {
            if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                onChange(path);
        }, MaterialAsset.IsTexturePath);

        ImGui.PopID();
    }

    private void DrawTextureMapPreviewBox(string value, Vector2 min, float size)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 max = min + new Vector2(size, size);
        bool hasTexture = !string.IsNullOrWhiteSpace(value);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0.105f, 0.105f, 0.110f, 1f)), 1f);
        if (hasTexture && File.Exists(value) && TryGetCachedPreviewTexture(value, out var textureId))
        {
            drawList.AddImage(textureId, min + new Vector2(1f, 1f), max - new Vector2(1f, 1f), Vector2.Zero, Vector2.One);
        }
        else
        {
            drawList.AddRectFilled(min + new Vector2(2f, 2f), max - new Vector2(2f, 2f), ImGui.GetColorU32(new System.Numerics.Vector4(0.155f, 0.155f, 0.160f, 1f)), 1f);
            if (hasTexture && File.Exists(value))
                QueuePreviewGeneration(value);
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(hasTexture
            ? new System.Numerics.Vector4(0.42f, 0.42f, 0.43f, 1f)
            : new System.Numerics.Vector4(0.055f, 0.055f, 0.060f, 1f)), 1f);
    }

    private void DrawAssetSlotPickerPopup(string value, Action<string> onPick, Func<string, bool>? assetFilter)
    {
        if (!ImGui.BeginPopup("AssetSlotPicker"))
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
        float popupW = 360f;
        ImGui.SetNextItemWidth(popupW - 68f);
        ImGui.InputTextWithHint("##AssetSlotSearch", "Search", ref assetSlotSearch, 128);
        ImGui.SameLine(0f, 6f);
        if (ImGui.Button("None", new Vector2(56f, 22f)))
        {
            onPick("");
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleVar();

        ImGui.Separator();
        var assets = GetPickerAssets(assetFilter)
            .Where(assetPath =>
            {
                string fileName = Path.GetFileName(assetPath);
                string rel = Path.GetRelativePath(rootAssetsPath, assetPath).Replace('\\', '/');
                return string.IsNullOrWhiteSpace(assetSlotSearch) ||
                       fileName.Contains(assetSlotSearch, StringComparison.OrdinalIgnoreCase) ||
                       rel.Contains(assetSlotSearch, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        ImGui.BeginChild("AssetSlotPickerList", new Vector2(popupW, 280f), ImGuiChildFlags.None);

        const float rowH = 34f;
        int count = assets.Count;
        float scrollY = ImGui.GetScrollY();
        float visibleH = Math.Max(1f, ImGui.GetWindowHeight());
        int first = Math.Max(0, (int)MathF.Floor(scrollY / rowH) - 2);
        int visible = Math.Max(1, (int)MathF.Ceiling(visibleH / rowH) + 5);
        int last = Math.Min(count, first + visible);

        float topPadding = first * rowH;
        if (topPadding > 0f)
            ImGui.Dummy(new Vector2(1f, topPadding));

        for (int i = first; i < last; i++)
        {
            var assetPath = assets[i];
            string fileName = Path.GetFileName(assetPath);
            string rel = Path.GetRelativePath(rootAssetsPath, assetPath).Replace('\\', '/');

            ImGui.PushID(assetPath);
            bool isSelected = string.Equals(value, assetPath, StringComparison.OrdinalIgnoreCase);
            Vector2 rowMin = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##AssetPickerRow", new Vector2(popupW - 8f, rowH)))
            {
                onPick(assetPath);
                statusMessage = "Assigned " + fileName;
                ImGui.CloseCurrentPopup();
            }

            bool hovered = ImGui.IsItemHovered();
            Vector2 rowMax = rowMin + new Vector2(popupW - 8f, rowH);
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(isSelected
                ? new System.Numerics.Vector4(0.20f, 0.36f, 0.55f, 0.95f)
                : hovered
                    ? new System.Numerics.Vector4(0.22f, 0.22f, 0.23f, 0.92f)
                    : new System.Numerics.Vector4(0.145f, 0.145f, 0.150f, 0.72f)), 2f);

            Vector2 previewMin = rowMin + new Vector2(5f, 5f);
            DrawTextureMapPreviewBox(assetPath, previewMin, 24f);
            string display = Ellipsize(rel, Math.Max(12, (int)((popupW - 46f) / 7.2f)));
            drawList.AddText(rowMin + new Vector2(36f, 6f), ImGui.GetColorU32(new System.Numerics.Vector4(0.82f, 0.82f, 0.82f, 1f)), display);
            drawList.AddText(rowMin + new Vector2(36f, 20f), ImGui.GetColorU32(new System.Numerics.Vector4(0.48f, 0.50f, 0.52f, 1f)), GetAssetKind(assetPath));
            ImGui.PopID();
        }

        float bottomPadding = Math.Max(0f, (count - last) * rowH);
        if (bottomPadding > 0f)
            ImGui.Dummy(new Vector2(1f, bottomPadding));

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    private IEnumerable<string> GetPickerAssets(Func<string, bool>? assetFilter)
    {
        if (!Directory.Exists(rootAssetsPath))
            yield break;

        if (assetPickerFileCacheDirty)
        {
            try
            {
                assetPickerFileCache = Directory
                    .EnumerateFiles(rootAssetsPath, "*.*", SearchOption.AllDirectories)
                    .Where(ShouldShowProjectPath)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                assetPickerFileCache = Array.Empty<string>();
            }

            assetPickerFileCacheDirty = false;
        }

        foreach (var file in assetPickerFileCache)
        {
            if (File.Exists(file) && (assetFilter == null || assetFilter(file)))
                yield return file;
        }
    }


    private void AssignMeshToSelected(string meshPath)
    {
        if (selected == null) return;
        CommitSceneMutation("Assign Mesh", () =>
        {
            if (assetService.AssignMesh(selected, meshPath, out var mesh) && mesh != null)
                statusMessage = $"Mesh assigned: {Path.GetFileName(meshPath)}";
            else
                statusMessage = GetMeshLoadFailureMessage("Mesh assignment failed");
            return true;
        });
    }

    private void ApplyMaterialToSelected(string materialPath)
    {
        if (selected == null) return;
        CommitSceneMutation("Apply Material", () =>
        {
            statusMessage = assetService.ApplyMaterial(selected, materialPath)
                ? $"Material applied: {Path.GetFileName(materialPath)}"
                : "Material apply failed";
            return true;
        });
    }

    private void ApplyMaterialToSubmesh(MeshFilter meshFilter, int submeshIndex, string materialPath)
    {
        CommitSceneMutation("Apply Submesh Material", () =>
        {
            while (meshFilter.MaterialSlots.Count <= submeshIndex)
                meshFilter.MaterialSlots.Add("");
            meshFilter.MaterialSlots[submeshIndex] = materialPath;
            statusMessage = $"Material applied: {Path.GetFileName(materialPath)}";
            return true;
        });
    }

    private void ApplyTextureToSelected(string texturePath)
    {
        if (selected == null) return;
        CommitSceneMutation("Apply Texture", () =>
        {
            statusMessage = assetService.ApplyTexture(selected, texturePath)
                ? $"Texture applied: {Path.GetFileName(texturePath)}"
                : "Texture apply failed";
            return true;
        });
    }

    private GameObject? CreateMeshObject(string meshPath)
    {
        return CommitSceneMutation("Create Mesh Object", () =>
        {
            var obj = CreateObject(Path.GetFileNameWithoutExtension(meshPath), 0);
            if (assetService.AssignMesh(obj, meshPath, out var mesh) && mesh != null)
            {
                if (ShouldSplitIntoHierarchy(meshPath, mesh))
                {
                    obj.RemoveComponent<MeshFilter>(physicsEngine); // la raíz será un contenedor
                    ApplyImportScale(obj, mesh, meshPath);          // la raíz lleva la escala; los hijos la heredan
                    BuildModelHierarchy(obj, meshPath, mesh);
                    return obj;
                }

                PrepareMeshObjectAssets(obj, mesh);
                BuildModelRig(obj, meshPath);
                EnsureMeshRenderer(obj);
                ApplyImportScale(obj, mesh, meshPath); // tras el rig: escala el armature, no la raíz
                statusMessage = $"Mesh object created: {obj.Name}";
                return obj;
            }

            objects.Remove(obj);
            selection.Clear();
            statusMessage = GetMeshLoadFailureMessage("Mesh load failed");
            return null;
        });
    }

    private void PrepareMeshObjectAssets(GameObject obj, ParsedMesh mesh)
    {
        var meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter == null)
            return;

        meshFilter.ImportScale = ComputeMeshImportScale(mesh);
        if (mesh.Submeshes.Count > 0)
            EnsureMaterialSlots(meshFilter, mesh);

        TryPrewarmMeshAsset(meshFilter.MeshPath, meshFilter.ImportScale, meshFilter.MaterialSlots);
    }

    private void PrewarmRenderableAssets(GameObject obj)
    {
        if (obj.GetComponent<MeshFilter>() is { } mf && !string.IsNullOrWhiteSpace(mf.MeshPath))
            TryPrewarmMeshAsset(mf.MeshPath, mf.ImportScale, mf.MaterialSlots);

        foreach (var child in obj.Children)
            PrewarmRenderableAssets(child);
    }

    private void PrewarmSceneRenderableAssetsDuringLoad()
    {
        if (objects.Count == 0)
            return;

        int total = objects.Count;
        int index = 0;
        foreach (var obj in objects)
        {
            index++;
            Program.UpdateSplash($"Prewarming scene assets {index}/{total}: {obj.Name}", 0.82f + 0.04f * (index / (float)Math.Max(1, total)));
            PrewarmRenderableAssets(obj);
        }
    }

    private void TryPrewarmMeshAsset(string? meshPath, float importScale, IReadOnlyList<string>? materialSlots)
    {
        try
        {
            sceneRenderer.PrewarmMeshAsset(meshPath, importScale, materialSlots);
        }
        catch (Exception ex)
        {
            string name = string.IsNullOrWhiteSpace(meshPath) ? "mesh" : Path.GetFileName(meshPath);
            statusMessage = $"GPU prewarm skipped: {name}";
            GrokoEngine.Debug.LogWarning($"GPU prewarm skipped for '{meshPath}': {ex.Message}");
        }
    }

    private static string GetMeshLoadFailureMessage(string fallback)
    {
        return string.IsNullOrWhiteSpace(ObjLoader.LastError)
            ? fallback
            : fallback + ": " + ObjLoader.LastError;
    }

    private void InstantiatePrefab(string prefabPath)
    {
        if (!File.Exists(prefabPath)) return;
        CommitSceneMutation("Instantiate Prefab", () =>
        {
            var obj = SceneSerializer.LoadPrefab(prefabPath, physicsEngine, scriptCompiler);
            ResetIds(obj);
            obj.Name = GetUniqueObjectName(obj.Name);
            obj.PrefabAssetPath = prefabPath;
            objects.Add(obj);
            selected = obj;
            PrewarmRenderableAssets(obj);
            statusMessage = $"Prefab instantiated: {obj.Name}";
            return obj;
        });
    }

    private void DropAssetIntoViewport(string assetPath, Vector2 localMouse)
    {
        if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            InstantiatePrefabAt(assetPath, GetViewportDropPosition(localMouse));
            return;
        }

        if (ObjLoader.IsSupportedMesh(assetPath))
        {
            CreateMeshObjectAt(assetPath, GetViewportDropPosition(localMouse));
            return;
        }

        var target = PickObjectAt(localMouse.X, localMouse.Y) ?? selected;
        if (target == null)
        {
            statusMessage = "Drop target not found";
            return;
        }

        selected = target;
        if (MaterialAsset.IsMaterialPath(assetPath))
        {
            int submeshIndex = -1;
            if (BuildCameraRay(localMouse.X, localMouse.Y, out var rayOrigin, out var rayDir))
                submeshIndex = sceneRenderer.PickMeshSubmesh(target, ToTk(rayOrigin), ToTk(rayDir)) ?? -1;

            if (submeshIndex >= 0 && target.GetComponent<MeshFilter>() is { } targetMf)
                ApplyMaterialToSubmesh(targetMf, submeshIndex, assetPath);
            else
                ApplyMaterialToSelected(assetPath);
        }
        else if (MaterialAsset.IsTexturePath(assetPath))
            ApplyTextureToSelected(assetPath);
    }

    private void InstantiatePrefabAt(string prefabPath, Vector3 position)
    {
        if (!File.Exists(prefabPath)) return;
        CommitSceneMutation("Instantiate Prefab In Viewport", () =>
        {
            var obj = SceneSerializer.LoadPrefab(prefabPath, physicsEngine, scriptCompiler);
            ResetIds(obj);
            obj.Name = GetUniqueObjectName(obj.Name);
            obj.PrefabAssetPath = prefabPath;
            SetObjectPosition(obj, position);
            objects.Add(obj);
            selected = obj;
            PrewarmRenderableAssets(obj);
            statusMessage = $"Prefab dropped: {obj.Name}";
            return obj;
        });
    }

    private GameObject? CreateMeshObjectAt(string meshPath, Vector3 position)
    {
        return CommitSceneMutation("Drop Mesh In Viewport", () =>
        {
            var obj = CreateObject(Path.GetFileNameWithoutExtension(meshPath), 0);
            SetObjectPosition(obj, position);
            if (assetService.AssignMesh(obj, meshPath, out var mesh) && mesh != null)
            {
                if (ShouldSplitIntoHierarchy(meshPath, mesh))
                {
                    obj.RemoveComponent<MeshFilter>(physicsEngine); // la raíz será un contenedor
                    ApplyImportScale(obj, mesh, meshPath);          // la raíz lleva la escala; los hijos la heredan
                    BuildModelHierarchy(obj, meshPath, mesh);
                    statusMessage = $"Mesh dropped (hierarchy): {obj.Name}";
                    return obj;
                }

                PrepareMeshObjectAssets(obj, mesh);
                BuildModelRig(obj, meshPath);
                EnsureMeshRenderer(obj);
                ApplyImportScale(obj, mesh, meshPath); // tras el rig: escala el armature, no la raíz
                statusMessage = $"Mesh dropped: {obj.Name}";
                return obj;
            }

            objects.Remove(obj);
            selection.Clear();
            statusMessage = GetMeshLoadFailureMessage("Mesh drop failed");
            return null;
        });
    }

    // Escala el modelo importado a tamaño Unity (1u=1m) según el factor de unidades del FBX.
    // Para modelos CON huesos: escala el ARMATURE (los hijos del rig), dejando la RAÍZ a escala 1
    // — así un GameObject que metas dentro de la raíz para organizar no hereda el 0.01 ni se rompe
    // el gizmo. El skinning queda igual (el factor solo baja un nivel en la cadena de huesos).
    // Para modelos estáticos (sin huesos): escala la raíz como antes.
    private void ApplyImportScale(GameObject obj, ParsedMesh mesh, string meshPath)
    {
        var settings = ModelImportSettingsAsset.Load(meshPath);

        // Wrapper "Rig" creado por BuildModelRig: lleva él la escala de importación.
        // La raíz queda a 1 (parentar/gizmo 1:1) y, como el wrapper NO se anima, el
        // factor sobrevive al Play (la animación reescribe los huesos, no el wrapper).
        var rigWrapper = obj.Children.FirstOrDefault(c => c.Name == "Rig");
        if (mesh.HasSkin && rigWrapper != null)
        {
            // Skinned: el wrapper "Rig" lleva la conversión de unidades (cm→m) de los huesos.
            float s = ModelImportSettingsAsset.EffectiveScale(settings, mesh.RecommendedScale);
            if (s <= 0f || MathF.Abs(s - 1f) < 0.0001f)
                return;
            rigWrapper.ScaleX = s;
            rigWrapper.ScaleY = s;
            rigWrapper.ScaleZ = s;
        }
        else
        {
            // Estático: la malla ya se normaliza a ~1 unidad vía MeshFilter.ImportScale (1/maxDim),
            // así que NO se vuelve a aplicar la conversión de unidades aquí (si no, los modelos grandes
            // salían diminutos: 1/maxDim × 0.01 ≈ 0). Solo se aplica el ScaleFactor manual del usuario.
            float s = settings.ScaleFactor;
            if (s <= 0f || MathF.Abs(s - 1f) < 0.0001f)
                return;
            obj.ScaleX = s;
            obj.ScaleY = s;
            obj.ScaleZ = s;
        }
    }

    // Reconstruye la jerarquía del esqueleto del modelo (armature "metarig" + huesos)
    // como GameObjects hijos del objeto importado, al estilo de Unity.
    private void BuildModelRig(GameObject root, string meshPath)
    {
        try
        {
            var rig = ObjLoader.ReadHierarchy(meshPath);
            if (rig == null) return; // modelo sin huesos

            var mesh = ObjLoader.Load(meshPath);
            int created = 0;

            // En modelos skinned, los huesos cuelgan de un nodo intermedio "Rig" (no animado)
            // que es quien lleva la escala de importación (cm->m). Así la raíz queda a escala 1
            // (parentar/gizmo 1:1) y, al reproducir, la animación reescribe los huesos pero el
            // factor de escala del wrapper se conserva (el modelo no sale gigante en Play).
            GameObject rigParent = root;
            if (mesh != null && mesh.HasSkin)
            {
                rigParent = new GameObject { Name = "Rig", Parent = root };
                rigParent.SetLocalTRS(
                    new Vector3(0, 0, 0),
                    new MiMotor.Mathematics.Quaternion(0, 0, 0, 1),
                    new Vector3(1, 1, 1));
            }

            foreach (var node in rig.Children)
                CreateRigNode(node, rigParent, ref created);

            if (mesh != null && mesh.HasSkin)
            {
                var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
                animator.ModelPath = meshPath;
                animator.PlayOnAwake = false;

                var settings = ModelImportSettingsAsset.Load(meshPath);
                if (settings.AnimationType is "Generic" or "Humanoid")
                {
                    if (settings.AvatarDefinition == "Copy From Other Avatar")
                        animator.AvatarPath = settings.AvatarSource;
                    else
                    {
                        if (string.IsNullOrWhiteSpace(settings.CreatedAvatarPath) || !File.Exists(settings.CreatedAvatarPath))
                        {
                            settings.CreatedAvatarPath = AvatarAsset.CreateFromModel(meshPath);
                            ModelImportSettingsAsset.Save(meshPath, settings);
                        }
                        animator.AvatarPath = settings.CreatedAvatarPath;
                    }
                }
            }

            if (created > 0)
                statusMessage = $"{created} huesos importados en {root.Name}";
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudo crear el rig: " + ex.Message);
        }
    }

    private void CreateRigNode(ModelNode node, GameObject parent, ref int created)
    {
        var go = new GameObject { Name = node.Name, Parent = parent };
        // Euler aproximado para serialización/inspector...
        go.PosX = node.PosX; go.PosY = node.PosY; go.PosZ = node.PosZ;
        go.RotX = node.RotX; go.RotY = node.RotY; go.RotZ = node.RotZ;
        go.ScaleX = node.ScaleX; go.ScaleY = node.ScaleY; go.ScaleZ = node.ScaleZ;
        // ...y el cuaternión EXACTO para la pose de bind (el Euler no basta para skinning).
        go.SetLocalTRS(
            new Vector3(node.PosX, node.PosY, node.PosZ),
            new MiMotor.Mathematics.Quaternion(node.Qx, node.Qy, node.Qz, node.Qw),
            new Vector3(node.ScaleX, node.ScaleY, node.ScaleZ));
        created++;

        foreach (var child in node.Children)
            CreateRigNode(child, go, ref created);
    }

    // ¿Se debe importar el FBX "con hijos" (un GameObject por cada parte separada, estilo Unity)?
    // Personajes con huesos: NO (se mantienen como objeto único + rig).
    // Automático cuando el FBX trae varios OBJETOS separados (varios nodos con malla), como Unity.
    // El toggle "Preserve Hierarchy" sigue sirviendo para forzarlo también en objetos de un solo nodo
    // con varios materiales (que por defecto se dejan como un objeto con varios slots de material).
    private static bool ShouldSplitIntoHierarchy(string meshPath, ParsedMesh mesh)
    {
        if (mesh.HasSkin) return false;
        if (mesh.Submeshes.Count < 2) return false;
        if (mesh.SeparateObjectCount > 1) return true; // varios objetos de verdad → automático
        return ModelImportSettingsAsset.Load(meshPath).PreserveHierarchy;
    }

    // Crea un GameObject hijo por cada parte del modelo. Cada hijo dibuja solo SU submalla (SubmeshIndex)
    // reutilizando el mismo ImportScale que el objeto único, así el conjunto se ve idéntico pero como
    // objetos separados. La escala de importación la lleva la raíz (los hijos la heredan al estar parentados).
    private void BuildModelHierarchy(GameObject root, string meshPath, ParsedMesh mesh)
    {
        float importScale = ComputeMeshImportScale(mesh);
        int created = 0;

        for (int i = 0; i < mesh.Submeshes.Count; i++)
        {
            var sub = mesh.Submeshes[i];
            string name = !string.IsNullOrWhiteSpace(sub.MeshName) ? sub.MeshName
                : !string.IsNullOrWhiteSpace(sub.Name) ? sub.Name
                : $"Part {i + 1}";

            var child = new GameObject { Name = name, Parent = root };
            child.SetLocalTRS(
                new Vector3(0, 0, 0),
                new MiMotor.Mathematics.Quaternion(0, 0, 0, 1),
                new Vector3(1, 1, 1));

            var mf = child.AddComponent<MeshFilter>();
            mf.MeshPath = meshPath;
            mf.ImportScale = importScale;
            mf.SubmeshIndex = i;
            EnsureMeshRenderer(child);
            TryPrewarmMeshAsset(mf.MeshPath, mf.ImportScale, mf.MaterialSlots);
            created++;
        }

        statusMessage = $"{created} partes importadas como hijos de {root.Name}";
    }

    private Vector3 GetViewportDropPosition(Vector2 localMouse)
    {
        if (!BuildCameraRay(localMouse.X, localMouse.Y, out var origin, out var dir))
            return new Vector3(0, 0, 0);

        if (Math.Abs(dir.Y) > 0.0001f)
        {
            float t = -origin.Y / dir.Y;
            if (t > 0f)
                return origin + dir * t;
        }

        return origin + dir * 5f;
    }

    private static void SetObjectPosition(GameObject obj, Vector3 position)
    {
        obj.PosX = position.X;
        obj.PosY = position.Y;
        obj.PosZ = position.Z;
    }

    private void ImportExternalFiles(IEnumerable<string> paths)
    {
        var importPaths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (importPaths.Length == 0)
            return;

        string target = selectedAssetPath != null && Directory.Exists(selectedAssetPath)
            ? selectedAssetPath
            : rootAssetsPath;

        QueueEditorProgressTask("Importing assets", $"Copying {importPaths.Length} asset(s)", () =>
        {
            UpdateEditorProgress("Copying files into Assets", 0.28f);
            var result = assetService.ImportExternalFiles(importPaths, target);
            BuildImportedPreviewCache(result.ImportedPaths);
            if (result.ImportedScripts)
            {
                UpdateEditorProgress("Compiling imported scripts", 0.68f);
                CompileScriptsNow();
            }
            selectedAssetPath = result.ImportedPaths.LastOrDefault() ?? selectedAssetPath;
            statusMessage = result.Errors.Count == 0
                ? $"Imported {result.ImportedCount} asset(s)"
                : string.Join(Environment.NewLine, result.Errors);
        });
    }

    private static float ComputeMeshImportScale(ParsedMesh mesh)
    {
        float sizeX = mesh.BoundsMax.X - mesh.BoundsMin.X;
        float sizeY = mesh.BoundsMax.Y - mesh.BoundsMin.Y;
        float sizeZ = mesh.BoundsMax.Z - mesh.BoundsMin.Z;
        float max = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
        return max > 0.001f ? 1f / max : 1f;
    }

    private void UnregisterCollidersRecursive(GameObject obj)
    {
        foreach (var component in obj.Components)
            if (component is Collider collider)
                physicsEngine.UnregisterCollider(collider);

        foreach (var child in obj.Children)
            UnregisterCollidersRecursive(child);
    }

    private static void ResetIds(GameObject obj)
    {
        obj.EditorId = Guid.NewGuid().ToString("N");
        foreach (var child in obj.Children)
            ResetIds(child);
    }

    private string GetUniqueObjectName(string baseName)
    {
        var existing = new HashSet<string>(objects.SelectMany(Flatten).Select(o => o.Name));
        if (!existing.Contains(baseName)) return baseName;
        int i = 1;
        string candidate;
        do candidate = $"{baseName}_{i++}";
        while (existing.Contains(candidate));
        return candidate;
    }

    private static string GetUniqueChildObjectName(GameObject parent, string baseName)
    {
        var existing = new HashSet<string>(parent.Children.Select(o => o.Name));
        if (!existing.Contains(baseName)) return baseName;
        int i = 1;
        string candidate;
        do candidate = $"{baseName}_{i++}";
        while (existing.Contains(candidate));
        return candidate;
    }

    private string GetUniqueSiblingObjectName(GameObject obj, string baseName)
    {
        var siblings = obj.Parent != null ? obj.Parent.Children : objects;
        var existing = new HashSet<string>(
            siblings.Where(o => !ReferenceEquals(o, obj)).Select(o => o.Name),
            StringComparer.Ordinal);
        if (!existing.Contains(baseName)) return baseName;
        int i = 1;
        string candidate;
        do candidate = $"{baseName}_{i++}";
        while (existing.Contains(candidate));
        return candidate;
    }

    private static IEnumerable<GameObject> Flatten(GameObject obj)
    {
        yield return obj;
        foreach (var child in obj.Children)
            foreach (var nested in Flatten(child))
                yield return nested;
    }

    private static int CountRecursive(GameObject obj) => Flatten(obj).Count();

    private int GetCachedObjectCount()
    {
        double now = GLFW.GetTime();
        if (now - lastObjectCountRefresh > 0.25)
        {
            cachedObjectCount = objects.Sum(CountRecursive);
            lastObjectCountRefresh = now;
        }

        return cachedObjectCount;
    }

    private static float Distance(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X;
        float y = a.Y - b.Y;
        float z = a.Z - b.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    private static float Dot(Vector3 a, Vector3 b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Vector3 NormalizeSafe(Vector3 value)
    {
        float length = MathF.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return length <= 0.00001f
            ? Vector3.Zero
            : new Vector3(value.X / length, value.Y / length, value.Z / length);
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return (point - start).Length();

        float t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        var closest = start + segment * t;
        return (point - closest).Length();
    }

    private static float DistancePointToPolyline(Vector2 point, Vector2[] points, bool closed)
    {
        if (points.Length == 0)
            return float.MaxValue;
        if (points.Length == 1)
            return (point - points[0]).Length();

        float best = float.MaxValue;
        int segmentCount = closed ? points.Length : points.Length - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            var start = points[i];
            var end = points[(i + 1) % points.Length];
            best = Math.Min(best, DistancePointToSegment(point, start, end));
        }

        return best;
    }

}
