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
    private void DrawProjectToolbar()
    {
        float toolbarH = 27f;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5f, 3f));
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.205f, 0.205f, 0.205f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.270f, 0.270f, 0.270f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.125f, 0.365f, 0.610f, 1f));

        if (ImGui.Button("+##ProjectCreate", new Vector2(24f, toolbarH)))
            ImGui.OpenPopup("ProjectCreateMenu");
        DrawTooltip("Create asset");

        if (ImGui.BeginPopup("ProjectCreateMenu"))
        {
            string targetDirectory = currentProjectDirectory ?? rootAssetsPath;
            DrawProjectCreateMenuItems(targetDirectory);
            ImGui.EndPopup();
        }

        ImGui.SameLine(0f, 4f);
        if (ImGui.Button("^##ProjectUp", new Vector2(24f, toolbarH)) && currentProjectDirectory != null)
        {
            string? parent = Directory.GetParent(currentProjectDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && IsInsideAssets(parent))
                currentProjectDirectory = parent;
        }
        DrawTooltip("Up one folder");

        ImGui.SameLine(0f, 10f);
        DrawProjectBreadcrumb();

        float rightControlsW = 280f;
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 8f, ImGui.GetWindowWidth() - rightControlsW));
        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##ProjectSearch", "Search assets", ref projectSearch, 128);
        ImGui.SameLine(0f, 5f);
        if (DrawToggleButton(projectListView ? "List" : "Grid", projectListView, new Vector2(48f, toolbarH)))
            projectListView = !projectListView;
        DrawTooltip("Toggle Project view mode");
        ImGui.SameLine(0f, 5f);
        if (ImGui.BeginCombo("##ProjectSort", projectSortMode.ToString(), ImGuiComboFlags.NoArrowButton))
        {
            foreach (AssetSortMode mode in Enum.GetValues<AssetSortMode>())
                if (ImGui.Selectable(mode.ToString(), projectSortMode == mode))
                    projectSortMode = mode;
            ImGui.EndCombo();
        }
        DrawTooltip("Sort assets");

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
    }

    private void DrawProjectBreadcrumb()
    {
        if (currentProjectDirectory == null)
        {
            ImGui.TextDisabled("Assets");
            return;
        }

        if (ImGui.SmallButton("Assets"))
            currentProjectDirectory = rootAssetsPath;

        string relative = Path.GetRelativePath(rootAssetsPath, currentProjectDirectory);
        if (relative == ".")
            return;

        string walk = rootAssetsPath;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            walk = Path.Combine(walk, part);
            ImGui.SameLine();
            ImGui.TextDisabled("/");
            ImGui.SameLine();
            if (ImGui.SmallButton(part + "##crumb" + walk))
                currentProjectDirectory = walk;
        }
    }

    


    private void DrawProjectRootTreeHeader(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.56f, 0.56f, 0.56f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
        ImGui.TreeNodeEx("##ProjectHeader" + label, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen, label);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    


    


    private IReadOnlyList<ProjectAssetEntry> GetCachedProjectDirectories(string directory)
    {
        var entries = GetCachedProjectEntries(directory);
        if (!projectFolderCache.TryGetValue(directory, out var cache))
            return entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        if (cache.Directories != null)
            return cache.Directories;

        cache.Directories = entries
            .Where(e => e.IsDirectory)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return cache.Directories;
    }

    private IReadOnlyList<ProjectAssetEntry> GetVisibleProjectEntries(string directory)
    {
        var entries = GetCachedProjectEntries(directory);

        // No hagas Directory.GetLastWriteTimeUtc() aquí cada frame. GetCachedProjectEntries()
        // ya valida por TTL + LastWriteTime. Esta clave usa la versión cacheada y evita I/O
        // innecesario mientras se dibuja el Project Browser.
        string folderVersion = projectFolderCache.TryGetValue(directory, out var folderCache)
            ? folderCache.DirectoryWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + folderCache.Entries.Count.ToString(CultureInfo.InvariantCulture)
            : "0";

        string cacheKey =
            directory + "|" +
            folderVersion + "|" +
            projectSearch + "|" +
            projectSortMode + "|" +
            projectSortDescending + "|" +
            projectMeshExpansionVersion;

        if (string.Equals(projectVisibleEntriesCacheKey, cacheKey, StringComparison.Ordinal) &&
            projectVisibleEntriesCache != null)
        {
            return projectVisibleEntriesCache;
        }

        IEnumerable<ProjectAssetEntry> query = entries;

        if (!string.IsNullOrWhiteSpace(projectSearch))
        {
            query = query.Where(e =>
                e.Name.Contains(projectSearch, StringComparison.OrdinalIgnoreCase) ||
                Path.GetRelativePath(rootAssetsPath, e.Path).Replace('\\', '/').Contains(projectSearch, StringComparison.OrdinalIgnoreCase));
        }

        query = projectSortMode switch
        {
            AssetSortMode.Type => query.OrderBy(e => e.IsDirectory ? 0 : 1)
                                       .ThenBy(e => e.Kind, StringComparer.OrdinalIgnoreCase)
                                       .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            AssetSortMode.Size => query.OrderBy(e => e.IsDirectory ? 0 : 1)
                                       .ThenBy(e => e.IsDirectory ? -1 : e.SizeBytes)
                                       .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            AssetSortMode.Modified => query.OrderBy(e => e.IsDirectory ? 0 : 1)
                                           .ThenByDescending(e => e.ModifiedUtc)
                                           .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(e => e.IsDirectory ? 0 : 1)
                      .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        };

        if (projectSortDescending)
            query = query.Reverse();

        projectVisibleEntriesCache = ExpandProjectMeshEntries(query).ToList();
        projectVisibleEntriesCacheKey = cacheKey;

        return projectVisibleEntriesCache;
    }

    private IEnumerable<ProjectAssetEntry> ExpandProjectMeshEntries(IEnumerable<ProjectAssetEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;

            if (entry.IsDirectory || entry.Kind != "MESH" || !expandedMeshAssetPaths.Contains(entry.Path))
                continue;

            foreach (var child in CreateMeshSubAssetEntries(entry))
                yield return child;
        }
    }

    


    private static ProjectAssetEntry CreateVirtualSubmeshEntry(string parentPath, string name, int submeshIndex, DateTime modified) => new()
    {
        Path = MakeProjectSubAssetKey(parentPath, "submesh", submeshIndex),
        ParentPath = parentPath,
        Name = string.IsNullOrWhiteSpace(name) ? $"Submesh {submeshIndex}" : name,
        Kind = "SUBMESH",
        IsDirectory = false,
        SizeBytes = 0,
        ModifiedUtc = modified,
        IsVirtualSubAsset = true,
        SubmeshIndex = submeshIndex
    };

    private static ProjectAssetEntry CreateVirtualMaterialEntry(string parentPath, string materialPath, int submeshIndex)
    {
        var info = new FileInfo(materialPath);
        return new ProjectAssetEntry
        {
            Path = MakeProjectSubAssetKey(parentPath, "material", submeshIndex),
            ParentPath = parentPath,
            SourceMaterialPath = materialPath,
            Name = Path.GetFileNameWithoutExtension(materialPath),
            Kind = "MAT",
            IsDirectory = false,
            SizeBytes = info.Exists ? info.Length : 0,
            ModifiedUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue,
            IsVirtualSubAsset = true,
            SubmeshIndex = submeshIndex
        };
    }

    private static ProjectAssetEntry CreateVirtualAvatarEntry(string parentPath, string avatarPath, DateTime modified) => new()
    {
        Path = MakeProjectSubAssetKey(parentPath, "avatar", 0),
        ParentPath = parentPath,
        SourceAvatarPath = avatarPath,
        Name = Path.GetFileNameWithoutExtension(avatarPath),
        Kind = "AVATAR",
        IsDirectory = false,
        SizeBytes = File.Exists(avatarPath) ? new FileInfo(avatarPath).Length : 0,
        ModifiedUtc = modified,
        IsVirtualSubAsset = true
    };

    private static ProjectAssetEntry CreateVirtualAnimationEntry(string parentPath, string name, int clipIndex, DateTime modified) => new()
    {
        Path = MakeProjectSubAssetKey(parentPath, "animation", clipIndex),
        ParentPath = parentPath,
        Name = string.IsNullOrWhiteSpace(name) ? $"Animation {clipIndex + 1}" : name,
        Kind = "ANIM",
        IsDirectory = false,
        SizeBytes = 0,
        ModifiedUtc = modified,
        IsVirtualSubAsset = true,
        AnimationClipIndex = clipIndex
    };

    private static string MakeProjectSubAssetKey(string parentPath, string kind, int index) =>
        parentPath + "::" + kind + ":" + index.ToString(CultureInfo.InvariantCulture);

    


    private string? ExtractImportedAnimationClip(string modelPath, int clipIndex, bool selectCreated)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath) || !ObjLoader.IsSupportedMesh(modelPath))
            return null;

        ParsedMesh? mesh;
        try { mesh = ObjLoader.Load(modelPath); }
        catch { return null; }
        if (mesh == null || clipIndex < 0 || clipIndex >= mesh.Animations.Count)
            return null;

        var settings = ModelImportSettingsAsset.Load(modelPath);
        ModelImportSettingsAsset.Save(modelPath, settings);
        string avatarPath = settings.AvatarDefinition == "Copy From Other Avatar"
            ? settings.AvatarSource
            : settings.CreatedAvatarPath;
        if (string.IsNullOrWhiteSpace(avatarPath) && File.Exists(modelPath + ".avatar"))
            avatarPath = modelPath + ".avatar";

        string clipName = string.IsNullOrWhiteSpace(mesh.Animations[clipIndex].Name)
            ? Path.GetFileNameWithoutExtension(modelPath)
            : mesh.Animations[clipIndex].Name;
        string directory = Path.GetDirectoryName(modelPath) ?? rootAssetsPath;
        string created = AnimationClipAsset.CreateFromModelClip(
            directory,
            modelPath,
            clipName,
            avatarPath,
            string.Equals(settings.AnimationType, "Humanoid", StringComparison.OrdinalIgnoreCase),
            settings);

        InvalidateProjectFolderCache(directory);
        statusMessage = "Animation clip extracted: " + Path.GetFileName(created);
        if (selectCreated)
        {
            selectedProjectSubAssetKey = null;
            selectedAssetPath = created;
            selected = null;
        }

        return created;
    }

    


    private IReadOnlyList<ProjectAssetEntry> GetCachedProjectEntries(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Array.Empty<ProjectAssetEntry>();

        DateTime now = DateTime.UtcNow;
        if (projectFolderCache.TryGetValue(directory, out var cached) && cached.NextValidationUtc > now)
            return cached.Entries;

        DateTime writeUtc;
        try
        {
            writeUtc = Directory.GetLastWriteTimeUtc(directory);
        }
        catch
        {
            return Array.Empty<ProjectAssetEntry>();
        }

        if (cached != null && cached.DirectoryWriteUtc == writeUtc)
        {
            cached.NextValidationUtc = now.AddSeconds(2.0);
            return cached.Entries;
        }

        var entries = new List<ProjectAssetEntry>(128);

        try
        {
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (ShouldShowProjectPath(child))
                    entries.Add(CreateProjectAssetEntry(child, true));
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (ShouldShowProjectPath(file))
                    entries.Add(CreateProjectAssetEntry(file, false));
            }
        }
        catch
        {
            // Si Windows bloquea una carpeta, el editor no debe romperse.
        }

        projectFolderCache[directory] = new ProjectFolderCache
        {
            DirectoryWriteUtc = writeUtc,
            NextValidationUtc = DateTime.UtcNow.AddSeconds(2.0),
            Entries = entries
        };

        return entries;
    }

    


    


    


    


    


    


    


    


    


    


    


    


    private void ToggleMeshEntryExpanded(ProjectAssetEntry entry)
    {
        if (entry.IsDirectory || entry.Kind != "MESH")
            return;

        if (!expandedMeshAssetPaths.Remove(entry.Path))
            expandedMeshAssetPaths.Add(entry.Path);

        projectMeshExpansionVersion++;
        InvalidateProjectFolderCache(currentProjectDirectory);
    }

    


    


    


    


    


    


    


    


    


    // Guarda el objeto arrastrado como un asset .prefab en la carpeta destino y vincula
    // la instancia de la escena al prefab (PrefabAssetPath).
    


    


    


    /// <summary>
    /// Botón invisible que cubre toda el área visible de assets (incluyendo los huecos
    /// entre/junto a los tiles), colocado después de dibujarlos para no robarles el
    /// ActiveId. Clic izquierdo en un hueco vacío deselecciona el asset/objeto. Clic
    /// derecho en un hueco vacío también deselecciona y abre el menú "Create"; si el
    /// clic derecho cae sobre un item, se deja su propio menú contextual intacto.
    /// </summary>
    


    


    


    


    


    private List<string> NormalizeProjectFileOperationPaths(IEnumerable<string> paths)
    {
        string assetsFull = Path.GetFullPath(rootAssetsPath);
        var distinct = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => IsInsideAssets(path) && !string.Equals(path, assetsFull, StringComparison.OrdinalIgnoreCase))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToList();

        var result = new List<string>();
        foreach (string path in distinct)
        {
            bool coveredBySelectedParent = result.Any(parent =>
                Directory.Exists(parent) && IsPathInsideDirectory(path, parent));
            if (!coveredBySelectedParent)
                result.Add(path);
        }

        return result;
    }

    


    


    


    private bool ProjectSearchMatches(string path)
    {
        if (string.IsNullOrWhiteSpace(projectSearch))
            return true;
        return Path.GetFileName(path).Contains(projectSearch, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<string> SortProjectPaths(IEnumerable<string> paths, bool directories)
    {
        IOrderedEnumerable<string> sorted = projectSortMode switch
        {
            AssetSortMode.Type => projectSortDescending
                ? paths.OrderByDescending(p => directories ? "Folder" : GetAssetKind(p), StringComparer.OrdinalIgnoreCase)
                : paths.OrderBy(p => directories ? "Folder" : GetAssetKind(p), StringComparer.OrdinalIgnoreCase),
            AssetSortMode.Size => projectSortDescending
                ? paths.OrderByDescending(p => directories ? 0L : new FileInfo(p).Length)
                : paths.OrderBy(p => directories ? 0L : new FileInfo(p).Length),
            AssetSortMode.Modified => projectSortDescending
                ? paths.OrderByDescending(p => directories ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p))
                : paths.OrderBy(p => directories ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p)),
            _ => projectSortDescending
                ? paths.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                : paths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        };

        return projectSortDescending
            ? sorted.ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            : sorted.ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
    }

    


    


    


    


    


    private static void DrawMeshFoldoutGlyph(ImDrawListPtr drawList, Vector2 min, float size, bool expanded)
    {
        var center = min + new Vector2(size * 0.5f, size * 0.5f);
        uint circle = ImGui.GetColorU32(new System.Numerics.Vector4(0.78f, 0.80f, 0.82f, 0.92f));
        uint arrow = ImGui.GetColorU32(new System.Numerics.Vector4(0.18f, 0.18f, 0.18f, 1f));
        drawList.AddCircleFilled(center, size * 0.48f, circle, 20);

        if (expanded)
        {
            drawList.AddTriangleFilled(
                center + new Vector2(-size * 0.22f, -size * 0.10f),
                center + new Vector2(size * 0.22f, -size * 0.10f),
                center + new Vector2(0f, size * 0.18f),
                arrow);
        }
        else
        {
            drawList.AddTriangleFilled(
                center + new Vector2(-size * 0.09f, -size * 0.24f),
                center + new Vector2(-size * 0.09f, size * 0.24f),
                center + new Vector2(size * 0.20f, 0f),
                arrow);
        }
    }

    


    

    


    


    


    


    


    


    


    



    // Dibuja un preview de textura con un borde y fondo para mejorar su visibilidad, usado para texturas de materiales y assets generados.
    


    


    


    


    


    


    


    


    


    


    


    


    


    


    private static bool ShouldPrewarmPreviewAtLoad(string path) =>
        MaterialAsset.IsTexturePath(path) || MaterialAsset.IsMaterialPath(path);

    


    


    private static bool IsProjectPreviewTextureKey(string key) =>
        key.StartsWith("preview:", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("shadergraph-material-preview:", StringComparison.OrdinalIgnoreCase);

    


    


    


    


    


    // Genera un preview para assets que no son texturas pero pueden tener un preview visual (ej. meshes, prefabs, escenas) usando una función de generación específica para cada tipo.
    


    


    

    


    


    


    


    


    /// <summary>Renderiza la miniatura de un material que usa Shader Graph, aplicando los overrides del propio asset .mat.</summary>
    


    


    private static System.Drawing.Bitmap? LoadSmallMaterialAlbedo(string albedoPath, int maxSize)
    {
        if (string.IsNullOrWhiteSpace(albedoPath) || !File.Exists(albedoPath))
            return null;

        try
        {
            using var source = System.Drawing.Image.FromFile(albedoPath);

            float scale = Math.Min(
                (float)maxSize / source.Width,
                (float)maxSize / source.Height);

            scale = Math.Min(scale, 1f);

            int newW = Math.Max(1, (int)(source.Width * scale));
            int newH = Math.Max(1, (int)(source.Height * scale));

            var bitmap = new System.Drawing.Bitmap(
                newW,
                newH,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, newW, newH);

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    


    


    // ToByte(float) está definido en ImGuiEditorApp.Components.cs (misma clase parcial).

    

    


    


    // Extrae la ruta de malla del primer MeshFilter de un .prefab (para su miniatura).
    private static bool TryGetPrefabMeshPath(string prefabPath, out string meshPath)
    {
        meshPath = "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(prefabPath));
            string? found = FindMeshPathInElement(doc.RootElement);
            if (string.IsNullOrWhiteSpace(found))
                return false;
            string? resolved = SceneViewportRenderer.NormalizeExistingAssetPath(found);
            if (resolved != null && File.Exists(resolved))
            {
                meshPath = resolved;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string? FindMeshPathInElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.NameEquals("MeshPath") && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        string? s = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            return s;
                    }
                    string? r = FindMeshPathInElement(prop.Value);
                    if (r != null) return r;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    string? r = FindMeshPathInElement(item);
                    if (r != null) return r;
                }
                break;
        }
        return null;
    }

    


    


    


    


    


    


    


    


    


    


    


    // Carga una textura de preview desde un archivo, sin usar cache. Devuelve false si hubo un error al cargar o procesar la imagen.
    


    


    


    


    


    private static void AddDependencyStamp(string? path, ref DateTime stamp)
    {
        string? fullPath = SceneViewportRenderer.NormalizeExistingAssetPath(path);
        if (fullPath == null || !File.Exists(fullPath))
            return;

        DateTime dependencyStamp = File.GetLastWriteTimeUtc(fullPath);
        if (dependencyStamp > stamp)
            stamp = dependencyStamp;
    }

    


    


    


    

    private static string Ellipsize(string value, int max)
    {
        if (value.Length <= max) return value;
        return value[..Math.Max(1, max - 3)] + "...";
    }

    private void DrawProjectDirectory(string directory)
    {
        var entries = GetCachedProjectEntries(directory);

        foreach (var entry in entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            string child = entry.Path;
            bool open = ImGui.TreeNode(entry.Name);
            if (ImGui.IsItemClicked())
                selectedAssetPath = child;
            DrawProjectContextMenu(child);
            if (open)
            {
                DrawProjectDirectory(child);
                ImGui.TreePop();
            }
        }

        foreach (var entry in entries.Where(e => !e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            string file = entry.Path;
            bool isSelected = string.Equals(selectedAssetPath, file, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(entry.Name, isSelected))
                selectedAssetPath = file;
            DrawProjectContextMenu(file);

            if (ImGui.BeginDragDropSource())
            {
                ImGui.SetDragDropPayload("GROKO_ASSET", IntPtr.Zero, 0);
                draggingAssetPath = file;
                ImGui.Text(entry.Name);
                ImGui.EndDragDropSource();
            }
        }
    }

    


    


    


    private static void PushContextMenuStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 3f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new System.Numerics.Vector4(0.120f, 0.120f, 0.125f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Header, new System.Numerics.Vector4(0.235f, 0.340f, 0.455f, 0.82f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new System.Numerics.Vector4(0.270f, 0.420f, 0.610f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new System.Numerics.Vector4(0.320f, 0.500f, 0.740f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Separator, new System.Numerics.Vector4(0.255f, 0.255f, 0.265f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.82f, 0.82f, 0.82f, 1f));
    }

    private static void PopContextMenuStyle()
    {
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(3);
    }

    


    


    private static bool ShouldShowProjectPath(string path)
    {
        string name = Path.GetFileName(path);

        // Carpetas internas de compilación de scripts.
        if (AssetDatabase.IsMetaPath(path) ||
            string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ".vs", StringComparison.OrdinalIgnoreCase))
            return false;

        // Archivos internos del editor (proyecto/solución de scripts generados por
        // el ScriptCompiler). Existen en disco pero no se muestran en el panel Project.
        string ext = Path.GetExtension(name);
        if (string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".sln", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".user", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    


    


    /// <summary>Creates a new Material asset already pointing at the given Shader Graph, like Unity's "Create > Material" from a Shader asset.</summary>
    


    /// <summary>Loads a .shadergraph asset and opens it in the Shader Graph editor window, like double-clicking a Shader Graph asset in Unity.</summary>
    


    


    


    


    


    


    


    private static string RemapProjectEntryKeyAfterMove(string key, string source, string destination)
    {
        int separator = key.IndexOf("::", StringComparison.Ordinal);
        string pathPart = separator >= 0 ? key[..separator] : key;
        string suffix = separator >= 0 ? key[separator..] : string.Empty;
        string? remapped = RemapPathAfterMove(pathPart, source, destination);
        return remapped == null ? key : remapped + suffix;
    }

    


    


    


    


    

}
