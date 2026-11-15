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
private void DrawAssetDragPreview(string path)
    {
        int dragCount = GetProjectDragAssetPaths(path).Count;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiPanelSoft);
        ImGui.BeginChild("AssetDragPreview", new Vector2(220f, 58f), ImGuiChildFlags.None);
        var drawList = ImGui.GetWindowDrawList();
        var previewMin = ImGui.GetCursorScreenPos() + new Vector2(4f, 4f);
        bool isDirectory = Directory.Exists(path);
        DrawProjectAssetPreview(drawList, path, isDirectory, previewMin, 42f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 54f);
        ImGui.Text(dragCount > 1 ? $"{dragCount} assets" : Path.GetFileName(path));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 54f);
        ImGui.TextDisabled(isDirectory ? "Folder" : GetAssetKind(path));
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

private void DrawProjectAssetPreview(ImDrawListPtr drawList, string path, bool isDirectory, Vector2 min, float size)
    {
        // Política: lo que YA está en caché se dibuja SIEMPRE (un AddImage es
        // barato, no depende del hover ni del profiler). Lo visible y aún no
        // cacheado entra en cola; la generación está limitada por frame en
        // ProcessPreviewGenerationQueue (1 job/frame, presupuesto de ms y de
        // trabajos de disco), así que carga "de a poco" sin bloquear FPS.

        if (isDirectory)
        {
            // No escanear el contenido de cada carpeta en cada frame.
            // Para máximo FPS dibujamos icono de carpeta rápido.
            DrawFolderPreview(drawList, min, size, false);
            return;
        }

        if (MaterialAsset.IsTexturePath(path))
        {
            if (TryGetCachedPreviewTexture(path, out var textureId))
            {
                DrawImagePreview(drawList, textureId, min, size);
                return;
            }

            QueuePreviewGeneration(path);
            DrawFileAssetPreview(drawList, min, size, "IMG", ProjectAssetColorForKind("IMG"));
            return;
        }

        if (MaterialAsset.IsMaterialPath(path))
        {
            // Esfera PBR cacheada (Library/Previews); si aún no existe, se encola
            // su generación en fondo y mientras tanto se ve el placeholder.
            DrawMaterialPreview(drawList, path, min, size, requestPreview: true);
            return;
        }

        string kind = GetAssetKind(path);
        if (kind == "MESH" || kind == "PREF" || kind == "SCENE")
        {
            if (TryGetGeneratedAssetPreviewTexture(path, kind, requestPreview: true, out var generatedTextureId))
            {
                DrawImagePreview(drawList, generatedTextureId, min, size);
                return;
            }

            DrawMeshLikePreview(drawList, min, size, kind);
            return;
        }

        if (kind == "CS")
        {
            if (TryGetEditorScriptIconTexture(out var scriptTextureId))
                DrawImagePreview(drawList, scriptTextureId, min, size);
            else
                DrawScriptAssetPreview(drawList, min, size);
            return;
        }

        if (kind == "PART")
        {
            DrawParticlePresetAssetPreview(drawList, min, size);
            return;
        }

        if (kind == "CTRL")
        {
            DrawAnimatorControllerAssetPreview(drawList, min, size);
            return;
        }

        if (kind == "AUDIO" && TryGetCustomEditorIconTexture("audioclip_64.png", out var audioTextureId))
        {
            // Onda naranja a todo color (no teñida), centrada en la celda.
            DrawImagePreview(drawList, audioTextureId, min, size);
            return;
        }

        DrawFileAssetPreview(drawList, min, size, kind, ProjectAssetColorForKind(kind));
    }

private void DrawProjectEntryPreview(ImDrawListPtr drawList, ProjectAssetEntry entry, Vector2 min, float size)
    {
        if (entry.IsVirtualSubAsset)
        {
            if (entry.Kind == "SUBMESH")
            {
                DrawSubmeshAssetPreview(drawList, min, size);
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.SourceMaterialPath) && File.Exists(entry.SourceMaterialPath))
            {
                DrawProjectAssetPreview(drawList, entry.SourceMaterialPath, false, min, size);
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.SourceAvatarPath) && File.Exists(entry.SourceAvatarPath))
            {
                DrawAvatarAssetPreview(drawList, min, size);
                return;
            }

            if (entry.Kind == "ANIM")
            {
                DrawAnimationAssetPreview(drawList, min, size);
                return;
            }
        }

        DrawProjectAssetPreview(drawList, entry.Path, entry.IsDirectory, min, size);
    }

private void DrawMaterialPreview(ImDrawListPtr drawList, string path, Vector2 min, float size, bool requestPreview)
    {
        if (TryGetMaterialSpherePreviewTexture(path, requestPreview, out var textureId))
        {
            DrawMaterialSphereImagePreview(drawList, textureId, min, size);
            return;
        }

        DrawMeshLikePreview(drawList, min, size, "MAT");
    }

private static void DrawMaterialSphereImagePreview(ImDrawListPtr drawList, IntPtr textureId, Vector2 min, float size)
    {
        var max = min + new Vector2(size, size);
        drawList.AddImage(textureId, min, max, Vector2.Zero, Vector2.One);
    }

private bool TryGetCachedPreviewTexture(string path, out IntPtr textureId)
    {
        textureId = IntPtr.Zero;

        string key = PreviewTextureCacheKey(path);
        if (TryGetFrameCachedAssetPreview(key, out textureId))
            return true;

        if (assetPreviewFrameMisses.Contains(key))
            return false;

        if (!assetPreviewTextures.TryGetValue(key, out int cachedTexture) || cachedTexture == 0)
        {
            assetPreviewFrameMisses.Add(key);
            return false;
        }

        var now = DateTime.UtcNow;
        if (assetPreviewNextValidationUtc.TryGetValue(key, out DateTime nextValidation) && nextValidation > now)
        {
            textureId = (IntPtr)cachedTexture;
            assetPreviewFrameCache[key] = textureId;
            return true;
        }

        // En Play NO se sondea el disco: los cambios hechos EN el editor invalidan directo
        // (InvalidateAssetPreview) y las ediciones externas pueden esperar a salir de Play.
        // Con el proyecto en OneDrive, cada stat costaba varios ms y bajaba los FPS del juego.
        if (isPlaying)
        {
            assetPreviewNextValidationUtc[key] = now + PreviewValidationDelay();
            textureId = (IntPtr)cachedTexture;
            assetPreviewFrameCache[key] = textureId;
            return true;
        }

        if (!File.Exists(path))
        {
            InvalidateAssetPreview(path, deleteTexture: true);
            assetPreviewFrameMisses.Add(key);
            return false;
        }

        DateTime writeTime = GetPreviewDependencyStamp(path);
        if (assetPreviewWriteTimes.TryGetValue(key, out DateTime cachedTime) && cachedTime == writeTime)
        {
            assetPreviewNextValidationUtc[key] = now + PreviewValidationDelay();
            textureId = (IntPtr)cachedTexture;
            assetPreviewFrameCache[key] = textureId;
            return true;
        }

        if (cachedTexture != 0)
        {
            GL.DeleteTexture(cachedTexture);
            assetPreviewTextures.Remove(key);
            assetPreviewWriteTimes.Remove(key);
            assetPreviewNextValidationUtc.Remove(key);
        }

        return false;
    }

private bool TryGetFrameCachedAssetPreview(string key, out IntPtr textureId)
    {
        if (assetPreviewFrameCache.TryGetValue(key, out textureId) && textureId != IntPtr.Zero)
            return true;

        textureId = IntPtr.Zero;
        return false;
    }

private static string PreviewTextureCacheKey(string path) => "preview:" + path;

private void InvalidateAssetPreview(string path, bool deleteTexture = false)
    {
        string key = PreviewTextureCacheKey(path);
        if (deleteTexture && assetPreviewTextures.TryGetValue(key, out int texture) && texture != 0)
            GL.DeleteTexture(texture);

        assetPreviewTextures.Remove(key);
        assetPreviewWriteTimes.Remove(key);
        assetPreviewNextValidationUtc.Remove(key);
        assetPreviewFrameCache.Remove(key);
        assetPreviewFrameMisses.Remove(key);
        materialPreviewSourceCache.TryRemove(path, out _);

        string shaderGraphKey = ShaderGraphMaterialPreviewCacheKey(path);
        if (deleteTexture && assetPreviewTextures.TryGetValue(shaderGraphKey, out int shaderGraphTexture) && shaderGraphTexture != 0)
            GL.DeleteTexture(shaderGraphTexture);

        assetPreviewTextures.Remove(shaderGraphKey);
        assetPreviewWriteTimes.Remove(shaderGraphKey);
        assetPreviewNextValidationUtc.Remove(shaderGraphKey);
        assetPreviewFrameCache.Remove(shaderGraphKey);
        assetPreviewFrameMisses.Remove(shaderGraphKey);
        lock (previewJobLock)
            previewGenerationFailures.Remove(shaderGraphKey);
    }

private void QueuePreviewGeneration(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        if (!ShouldQueuePreviewAsset(path))
            return;

        if (TryGetCachedPreviewTexture(path, out _))
            return;

        if (previewGenerationQueued.Contains(path))
            return;

        lock (previewJobLock)
        {
            if (previewDiskJobsRunning.Contains(path))
                return;
        }

        DateTime stamp = GetPreviewDependencyStamp(path);
        lock (previewJobLock)
        {
            if (previewGenerationFailures.TryGetValue(path, out var failedStamp) && failedStamp == stamp)
                return;
        }

        if (previewQueueRequestsThisFrame >= MaxPreviewQueueRequestsPerFrame)
            return;

        if (previewGenerationQueued.Add(path))
        {
            previewGenerationQueue.Enqueue(path);
            previewQueueRequestsThisFrame++;
        }
    }

private static bool ShouldQueuePreviewAsset(string path)
    {
        if (MaterialAsset.IsTexturePath(path))
            return true;

        // Los .mat generan su esfera PBR en fondo, igual que texturas y mallas.
        if (MaterialAsset.IsMaterialPath(path))
            return true;

        string kind = GetAssetKind(path);
        return kind == "MESH" || kind == "PREF" || kind == "SCENE";
    }

private void BuildProjectPreviewCacheDuringLoad()
    {
        var assets = EnumeratePreviewPrewarmAssets(rootAssetsPath).ToArray();
        if (assets.Length == 0)
            return;

        int loaded = 0;
        for (int i = 0; i < assets.Length; i++)
        {
            string path = assets[i];
            float progress = 0.86f + 0.08f * ((i + 1f) / assets.Length);
            Program.UpdateSplash($"Loading assets {i + 1}/{assets.Length}: {Path.GetFileName(path)}", progress);

            if (BuildAndLoadPreviewNow(path))
                loaded++;

            if ((i & 7) == 7)
                System.Threading.Thread.Sleep(1);
        }

        statusMessage = $"Loaded {loaded}/{assets.Length} image/material preview(s)";
    }

private void BuildImportedPreviewCache(IEnumerable<string> importedPaths)
    {
        var assets = EnumeratePreviewPrewarmAssets(importedPaths).ToArray();
        if (assets.Length == 0)
            return;

        for (int i = 0; i < assets.Length; i++)
        {
            string path = assets[i];
            UpdateEditorProgress($"Loading preview {i + 1}/{assets.Length}: {Path.GetFileName(path)}", 0.48f + 0.20f * ((i + 1f) / assets.Length));
            BuildAndLoadPreviewNow(path);
            if ((i & 7) == 7)
                System.Threading.Thread.Sleep(1);
        }
    }

private bool BuildAndLoadPreviewNow(string path)
    {
        if (!EnsureProjectAssetDiskPreview(path, out _))
            return false;

        if (TryGetPreviewTexture(path, out _))
        {
            lock (previewJobLock)
            {
                previewGenerationFailures.Remove(path);
                previewGenerationQueued.Remove(path);
            }
            return true;
        }

        return false;
    }

private static IEnumerable<string> EnumeratePreviewPrewarmAssets(string root)
    {
        if (!Directory.Exists(root))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (string file in files)
        {
            if (ShouldPrewarmPreviewAtLoad(file))
                yield return file;
        }
    }

private static IEnumerable<string> EnumeratePreviewPrewarmAssets(IEnumerable<string> roots)
    {
        foreach (string root in roots)
        {
            if (File.Exists(root))
            {
                if (ShouldPrewarmPreviewAtLoad(root))
                    yield return root;
                continue;
            }

            if (!Directory.Exists(root))
                continue;

            foreach (string file in EnumeratePreviewPrewarmAssets(root))
                yield return file;
        }
    }

    // Intervalo de revalidación CON jitter: evita que decenas de previews cacheadas en el mismo
    // frame expiren todas a la vez (ráfaga de lecturas de disco → tirón de UI periódico). Las
    // reparte a lo largo de ~3 s extra. Random.Shared: esto también se llama desde los workers
    // de fondo de previews, y un Random normal se corrompe con acceso concurrente.
    private static TimeSpan PreviewValidationDelay() =>
        AssetPreviewValidationInterval + TimeSpan.FromMilliseconds(System.Random.Shared.Next(0, 3000));

private void ProcessPreviewGenerationQueue()
    {
        TrimAssetPreviewTextureCacheIfNeeded();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int processed = 0;

        while (processed < PreviewJobsPerFrame && sw.ElapsedMilliseconds < PreviewFrameBudgetMs)
        {
            string? readyPath = DequeueReadyPreview();
            if (readyPath == null)
                break;

            TryGetPreviewTexture(readyPath, out _);
            processed++;
        }

        if (previewGenerationQueue.Count == 0)
            return;

        var io = ImGui.GetIO();

        bool userIsInteracting =
            MathF.Abs(io.MouseDelta.X) > 0.01f ||
            MathF.Abs(io.MouseDelta.Y) > 0.01f ||
            MathF.Abs(io.MouseWheel) > 0.01f ||
            ImGui.IsMouseDown(ImGuiMouseButton.Left) ||
            ImGui.IsMouseDown(ImGuiMouseButton.Right) ||
            ImGui.IsMouseDown(ImGuiMouseButton.Middle);

        if (userIsInteracting)
            return;

        previewQueueFrameSkip++;
        if (previewQueueFrameSkip < PreviewFrameSkipCount)
            return;

        previewQueueFrameSkip = 0;

        if (GetPreviewDiskJobCount() >= MaxPreviewDiskJobs)
            return;

        while (previewGenerationQueue.Count > 0 &&
               processed < PreviewJobsPerFrame &&
               sw.ElapsedMilliseconds < PreviewFrameBudgetMs &&
               GetPreviewDiskJobCount() < MaxPreviewDiskJobs)
        {
            string path = previewGenerationQueue.Dequeue();
            previewGenerationQueued.Remove(path);

            if (!File.Exists(path) || !ShouldQueuePreviewAsset(path))
                continue;

            if (TryGetPreviewTexture(path, out _))
            {
                processed++;
                continue;
            }

            StartPreviewDiskJob(path);
            processed++;
        }
    }

private void TrimAssetPreviewTextureCacheIfNeeded()
    {
        assetPreviewCacheTrimCountdown++;
        if (assetPreviewCacheTrimCountdown < 120)
            return;

        assetPreviewCacheTrimCountdown = 0;
        if (assetPreviewTextures.Count <= MaxAssetPreviewTextureCache)
            return;

        int removeCount = assetPreviewTextures.Count - MaxAssetPreviewTextureCache;
        var removableKeys = assetPreviewTextures.Keys
            .Where(IsProjectPreviewTextureKey)
            .OrderBy(key => assetPreviewNextValidationUtc.TryGetValue(key, out var next) ? next : DateTime.MinValue)
            .ThenBy(key => assetPreviewWriteTimes.TryGetValue(key, out var stamp) ? stamp : DateTime.MinValue)
            .Take(removeCount)
            .ToArray();

        foreach (string key in removableKeys)
            RemoveAssetPreviewTextureKey(key);
    }

private void RemoveAssetPreviewTextureKey(string key)
    {
        if (assetPreviewTextures.TryGetValue(key, out int texture) && texture != 0)
            GL.DeleteTexture(texture);

        assetPreviewTextures.Remove(key);
        assetPreviewWriteTimes.Remove(key);
        assetPreviewNextValidationUtc.Remove(key);
        assetPreviewFrameCache.Remove(key);
        assetPreviewFrameMisses.Remove(key);
    }

private int GetPreviewDiskJobCount()
    {
        lock (previewJobLock)
            return previewDiskJobsRunning.Count;
    }

private string? DequeueReadyPreview()
    {
        lock (previewJobLock)
        {
            while (previewDiskReadyQueue.Count > 0)
            {
                string path = previewDiskReadyQueue.Dequeue();
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

private void StartPreviewDiskJob(string path)
    {
        lock (previewJobLock)
        {
            if (!previewDiskJobsRunning.Add(path))
                return;
        }

        System.Threading.Tasks.Task.Run(() =>
        {
            bool ok = false;
            try
            {
                ok = EnsureProjectAssetDiskPreview(path, out _);
            }
            finally
            {
                lock (previewJobLock)
                {
                    previewDiskJobsRunning.Remove(path);
                    if (ok)
                    {
                        previewGenerationFailures.Remove(path);
                        previewDiskReadyQueue.Enqueue(path);
                    }
                    else if (File.Exists(path))
                    {
                        previewGenerationFailures[path] = GetPreviewDependencyStamp(path);
                    }
                }
            }
        });
    }

private bool TryGetPreviewTexture(string path, out IntPtr textureId)
    {
        textureId = IntPtr.Zero;

        string key = PreviewTextureCacheKey(path);
        DateTime writeTime;
        if (TryGetFrameCachedAssetPreview(key, out textureId))
            return true;

        if (assetPreviewTextures.TryGetValue(key, out int cachedTexture) && cachedTexture != 0)
        {
            var now = DateTime.UtcNow;
            if (assetPreviewNextValidationUtc.TryGetValue(key, out DateTime nextValidation) && nextValidation > now)
            {
                textureId = (IntPtr)cachedTexture;
                assetPreviewFrameCache[key] = textureId;
                return true;
            }

            // En Play no se sondea el disco (ver TryGetCachedPreviewTexture).
            if (isPlaying)
            {
                assetPreviewNextValidationUtc[key] = now + PreviewValidationDelay();
                textureId = (IntPtr)cachedTexture;
                assetPreviewFrameCache[key] = textureId;
                return true;
            }

            if (!File.Exists(path))
            {
                InvalidateAssetPreview(path, deleteTexture: true);
                return false;
            }

            writeTime = GetPreviewDependencyStamp(path);
            if (assetPreviewWriteTimes.TryGetValue(key, out DateTime cachedTime) && cachedTime == writeTime)
            {
                assetPreviewNextValidationUtc[key] = now + PreviewValidationDelay();
                textureId = (IntPtr)cachedTexture;
                assetPreviewFrameCache[key] = textureId;
                return true;
            }

            GL.DeleteTexture(cachedTexture);
            assetPreviewTextures.Remove(key);
            assetPreviewWriteTimes.Remove(key);
            assetPreviewNextValidationUtc.Remove(key);
        }
        else
        {
            if (!File.Exists(path))
                return false;

            writeTime = GetPreviewDependencyStamp(path);
        }

        if (!LoadPreviewTextureFromDiskCache(path, out int texture))
        {
            QueuePreviewGeneration(path);
            return false;
        }

        assetPreviewTextures[key] = texture;
        assetPreviewWriteTimes[key] = writeTime;
        assetPreviewNextValidationUtc[key] = DateTime.UtcNow + PreviewValidationDelay();

        textureId = (IntPtr)texture;
        assetPreviewFrameCache[key] = textureId;
        return true;
    }

private bool TryGetGeneratedAssetPreviewTexture(string path, string kind, bool requestPreview, out IntPtr textureId)
    {
        if (TryGetCachedPreviewTexture(path, out textureId))
            return true;

        if (requestPreview)
            QueuePreviewGeneration(path);
        return false;
    }

private static DateTime GetGeneratedAssetPreviewStamp(string path, string kind)
    {
        DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (kind != "MESH")
            return stamp;

        try
        {
            var mesh = ObjLoader.Load(path);
            if (mesh == null)
                return stamp;

            foreach (var submesh in mesh.Submeshes)
            {
                if (string.IsNullOrWhiteSpace(submesh.TexturePath))
                    continue;

                string? texturePath = SceneViewportRenderer.NormalizeExistingAssetPath(submesh.TexturePath);
                if (texturePath != null && File.Exists(texturePath))
                {
                    DateTime textureStamp = File.GetLastWriteTimeUtc(texturePath);
                    if (textureStamp > stamp)
                        stamp = textureStamp;
                }
            }
        }
        catch
        {
            return stamp;
        }

        return stamp;
    }

private static System.Drawing.Bitmap CreatePreviewBitmap(System.Drawing.Bitmap original, int maxSize = 256)
    {
        float scale = Math.Min(
            (float)maxSize / original.Width,
            (float)maxSize / original.Height);

        if (scale >= 1f)
            return new System.Drawing.Bitmap(original);

        int newW = Math.Max(1, (int)(original.Width * scale));
        int newH = Math.Max(1, (int)(original.Height * scale));

        var preview = new System.Drawing.Bitmap(
            newW,
            newH,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var g = System.Drawing.Graphics.FromImage(preview);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        g.DrawImage(original, 0, 0, newW, newH);

        return preview;
    }

private bool TryGetMaterialSpherePreviewTexture(string path, bool requestPreview, out IntPtr textureId)
    {
        if (TryGetCachedPreviewTexture(path, out textureId))
            return true;

        if (requestPreview)
            QueuePreviewGeneration(path);
        return false;
    }

private bool TryGetShaderGraphMaterialPreviewTexture(string path, MaterialAssetData data, string shaderGraphPath, bool requestPreview, out IntPtr textureId)
    {
        textureId = IntPtr.Zero;

        string key = ShaderGraphMaterialPreviewCacheKey(path);
        if (TryGetFrameCachedAssetPreview(key, out textureId))
            return true;

        if (assetPreviewFrameMisses.Contains(key))
            return false;

        DateTime writeTime = GetPreviewDependencyStamp(path);
        var now = DateTime.UtcNow;

        if (assetPreviewTextures.TryGetValue(key, out int cachedTexture) && cachedTexture != 0)
        {
            if (assetPreviewNextValidationUtc.TryGetValue(key, out DateTime nextValidation) &&
                nextValidation > now &&
                assetPreviewWriteTimes.TryGetValue(key, out DateTime cachedTime) &&
                cachedTime == writeTime)
            {
                textureId = (IntPtr)cachedTexture;
                assetPreviewFrameCache[key] = textureId;
                return true;
            }

            GL.DeleteTexture(cachedTexture);
            assetPreviewTextures.Remove(key);
            assetPreviewWriteTimes.Remove(key);
            assetPreviewNextValidationUtc.Remove(key);
        }

        lock (previewJobLock)
        {
            if (previewGenerationFailures.TryGetValue(key, out var failedStamp) && failedStamp == writeTime)
            {
                assetPreviewFrameMisses.Add(key);
                return false;
            }
        }

        if (TryGetCachedPreviewTexture(path, out textureId))
            return true;

        if (requestPreview)
            QueuePreviewGeneration(path);

        // Mantener el Project Panel ligero: no cocinamos previews ShaderGraph dentro del draw del tile.
        // El preview normal del material se carga por cola/cache y evita caídas de FPS al mostrar muchos assets.
        assetPreviewFrameMisses.Add(key);
        return false;

    }

private bool TryGetShaderGraphMaterialPreviewSource(string path, out MaterialAssetData data, out string shaderGraphPath)
    {
        data = new MaterialAssetData();
        shaderGraphPath = string.Empty;

        if (!TryGetMaterialPreviewSourceCache(path, out var cached) || !cached.HasShaderGraph)
            return false;

        data = cached.Data;
        shaderGraphPath = cached.ShaderGraphPath;
        return true;
    }

private bool TryGetMaterialPreviewSourceCache(string path, out MaterialPreviewSourceCache cached)
    {
        cached = new MaterialPreviewSourceCache();

        if (!MaterialAsset.IsMaterialPath(path))
            return false;

        try
        {
            // Dentro de la ventana de validación (o en Play) NO se toca el disco en absoluto
            // (ni File.Exists): este método corre en el draw de tiles visibles y del preview
            // del inspector. Un borrado externo se detecta en la siguiente revalidación.
            if (materialPreviewSourceCache.TryGetValue(path, out var existing) &&
                (existing.NextValidationUtc > DateTime.UtcNow || isPlaying))
            {
                cached = existing;
                return true;
            }

            if (!File.Exists(path))
            {
                materialPreviewSourceCache.TryRemove(path, out _);
                return false;
            }

            DateTime materialWriteUtc = File.GetLastWriteTimeUtc(path);
            if (existing != null && existing.MaterialWriteUtc == materialWriteUtc)
            {
                var now = DateTime.UtcNow;

                // El .mat no cambió: revalidar SOLO las dependencias (stats de fecha), sin
                // re-parsear el JSON (el Load periódico por material era un coste real,
                // peor en OneDrive).
                DateTime revalidatedStamp = materialWriteUtc;
                AddDependencyStamp(MaterialAsset.GetAlbedo(existing.Data), ref revalidatedStamp);
                AddDependencyStamp(existing.Data.NormalMapPath, ref revalidatedStamp);
                AddDependencyStamp(existing.Data.RoughnessMapPath, ref revalidatedStamp);
                AddDependencyStamp(existing.Data.MetallicMapPath, ref revalidatedStamp);
                foreach (var texturePath in existing.Data.ShaderGraphTextures.Values)
                    AddDependencyStamp(texturePath, ref revalidatedStamp);
                if (existing.HasShaderGraph && File.Exists(existing.ShaderGraphPath))
                {
                    DateTime sgWrite = File.GetLastWriteTimeUtc(existing.ShaderGraphPath);
                    if (sgWrite > revalidatedStamp)
                        revalidatedStamp = sgWrite;
                }

                cached = new MaterialPreviewSourceCache
                {
                    MaterialWriteUtc = existing.MaterialWriteUtc,
                    HasShaderGraph = existing.HasShaderGraph,
                    Data = existing.Data,
                    ShaderGraphPath = existing.ShaderGraphPath,
                    ShaderGraphWriteUtc = existing.ShaderGraphWriteUtc,
                    DependencyStamp = revalidatedStamp,
                    NextValidationUtc = now + PreviewValidationDelay()
                };
                materialPreviewSourceCache[path] = cached;
                return true;
            }

            var data = MaterialAsset.Load(path);
            DateTime dependencyStamp = materialWriteUtc;
            AddDependencyStamp(MaterialAsset.GetAlbedo(data), ref dependencyStamp);
            AddDependencyStamp(data.NormalMapPath, ref dependencyStamp);
            AddDependencyStamp(data.RoughnessMapPath, ref dependencyStamp);
            AddDependencyStamp(data.MetallicMapPath, ref dependencyStamp);
            foreach (var texturePath in data.ShaderGraphTextures.Values)
                AddDependencyStamp(texturePath, ref dependencyStamp);

            string? fullShaderGraphPath = SceneViewportRenderer.NormalizeExistingAssetPath(data.ShaderGraphPath);
            bool hasShaderGraph = !string.IsNullOrWhiteSpace(fullShaderGraphPath) && File.Exists(fullShaderGraphPath);
            DateTime shaderGraphWriteUtc = DateTime.MinValue;
            if (hasShaderGraph)
            {
                shaderGraphWriteUtc = File.GetLastWriteTimeUtc(fullShaderGraphPath!);
                if (shaderGraphWriteUtc > dependencyStamp)
                    dependencyStamp = shaderGraphWriteUtc;
            }

            cached = new MaterialPreviewSourceCache
            {
                MaterialWriteUtc = materialWriteUtc,
                HasShaderGraph = hasShaderGraph,
                Data = data,
                ShaderGraphPath = hasShaderGraph ? fullShaderGraphPath! : string.Empty,
                ShaderGraphWriteUtc = shaderGraphWriteUtc,
                DependencyStamp = dependencyStamp,
                NextValidationUtc = DateTime.UtcNow + PreviewValidationDelay()
            };
            materialPreviewSourceCache[path] = cached;
            return true;
        }
        catch
        {
            materialPreviewSourceCache.TryRemove(path, out _);
            cached = new MaterialPreviewSourceCache();
            return false;
        }
    }

private static string ShaderGraphMaterialPreviewCacheKey(string path) => "shadergraph-material-preview:" + path;

private System.Drawing.Bitmap? GenerateShaderGraphMaterialPreviewBitmap(MaterialAssetData data, string shaderGraphPath)
    {
        try
        {
            var model = GrokoShaderGraphPro.Services.GraphSerializer.Load(shaderGraphPath);
            var generator = new ShaderCodeGenerator();
            string fragmentSrc = generator.GenerateFragmentShader(model);
            string vertexSrc = generator.GenerateVertexShader();

            materialAssetShaderPreview ??= new ShaderGraphPreview();
            if (!materialAssetShaderPreview.SetShader(vertexSrc, fragmentSrc))
            {
                GrokoEngine.Debug.LogWarning($"Shader Graph material preview failed for '{Path.GetFileName(shaderGraphPath)}': {materialAssetShaderPreview.CompileError}");
                return null;
            }

            const int size = 128;
            materialAssetShaderPreview.Resize(size, size);
            materialAssetShaderPreview.Render(model, fragmentSrc, 0.6f, -0.3f, 0f, ClientSize.X, ClientSize.Y,
                data.ShaderGraphProperties, data.ShaderGraphTextures);

            return materialAssetShaderPreview.CaptureBitmap();
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"Shader Graph material preview failed for '{Path.GetFileName(shaderGraphPath)}': {ex.Message}");
            return null;
        }
    }

    // ── Esfera de material estilo Unity ──────────────────────────────────────
    // PBR real en espacio LINEAL (Cook-Torrance GGX + ambiente de estudio
    // procedural), con albedo/normal/roughness/metallic maps, emisión HDR,
    // damero para transparentes y tone mapping ACES. Se rasteriza a 2x del
    // tamaño final (SSAA) sobre un buffer bloqueado — cero SetPixel/GetPixel.
    // Corre en el hilo de fondo de previews y el resultado va al cache de disco
    // (Library/Previews): el coste por frame del editor es solo un AddImage.
private static System.Drawing.Bitmap GenerateMaterialSpherePreviewBitmap(MaterialAssetData data)
    => GenerateMaterialSpherePreviewBitmap(data, 0f, 0f, null);

    // yaw/pitch giran la BOLA (no la luz): la iluminación de estudio queda fija en cámara,
    // como el preview interactivo de Unity, y la textura recorre la superficie al arrastrar.
private static System.Drawing.Bitmap GenerateMaterialSpherePreviewBitmap(MaterialAssetData data, float yawDeg, float pitchDeg, MaterialPreviewMaps? maps)
    {
        const int size = 256; // EnsureProjectAssetDiskPreview lo reduce a 128 (bicúbico) = SSAA 2x
        var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        PreviewSampler? albedoMap = maps != null ? maps.Albedo : PreviewSampler.Load(MaterialAsset.GetAlbedo(data), 256);
        PreviewSampler? normalMap = maps != null ? maps.Normal : PreviewSampler.Load(data.NormalMapPath, 256);
        PreviewSampler? roughnessMap = maps != null ? maps.Roughness : PreviewSampler.Load(data.RoughnessMapPath, 128);
        PreviewSampler? metallicMap = maps != null ? maps.Metallic : PreviewSampler.Load(data.MetallicMapPath, 128);

        bool rotated = MathF.Abs(yawDeg) > 0.001f || MathF.Abs(pitchDeg) > 0.001f;
        float yawCos = MathF.Cos(yawDeg * 0.017453292f), yawSin = MathF.Sin(yawDeg * 0.017453292f);
        float pitCos = MathF.Cos(pitchDeg * 0.017453292f), pitSin = MathF.Sin(pitchDeg * 0.017453292f);

        float radius = size * 0.47f;
        float cx = size * 0.50f;
        float cy = size * 0.50f;

        // Color base y emisión en LINEAL (los valores/álbedo del asset están en sRGB).
        float baseLinR = SrgbToLinear(Math.Clamp(data.R, 0f, 1f));
        float baseLinG = SrgbToLinear(Math.Clamp(data.G, 0f, 1f));
        float baseLinB = SrgbToLinear(Math.Clamp(data.B, 0f, 1f));
        float emisR = SrgbToLinear(Math.Clamp(data.EmissionR, 0f, 1f)) * MathF.Max(0f, data.EmissionIntensity);
        float emisG = SrgbToLinear(Math.Clamp(data.EmissionG, 0f, 1f)) * MathF.Max(0f, data.EmissionIntensity);
        float emisB = SrgbToLinear(Math.Clamp(data.EmissionB, 0f, 1f)) * MathF.Max(0f, data.EmissionIntensity);

        bool transparent = data.SurfaceType == MaterialSurfaceType.Transparent;
        float surfaceAlpha = transparent ? Math.Clamp(data.Alpha, 0f, 1f) : 1f;

        // Iluminación de estudio (como el preview de Unity): key cálida arriba-izquierda,
        // fill fría suave desde la derecha, más ambiente/reflejo procedural.
        var key = Normalize3(-0.42f, 0.62f, 0.66f);
        var fill = Normalize3(0.65f, -0.10f, 0.55f);
        const float keyIntensity = 2.6f;
        const float fillIntensity = 0.45f;

        var pixels = new byte[size * size * 4];

        // Cada fila escribe un rango disjunto del buffer y solo LEE los samplers →
        // paralelizable sin locks. Importa para el preview interactivo (rota a ~10 ms
        // en vez de ~90 en Debug) y acelera el precalentado de la Library.
        System.Threading.Tasks.Parallel.For(0, size, y =>
        {
            int row = y * size * 4;
            for (int x = 0; x < size; x++)
            {
                // Coordenadas de esfera; +Y arriba en mundo (la Y de pantalla crece hacia abajo).
                float sx = (x + 0.5f - cx) / radius;
                float sy = -((y + 0.5f - cy) / radius);
                float r2 = sx * sx + sy * sy;
                float coverage = Math.Clamp((1f - MathF.Sqrt(MathF.Max(r2, 1e-8f))) * radius, 0f, 1f);
                if (coverage <= 0f)
                    continue; // fuera de la esfera: transparente (el buffer ya está a cero)

                float nz = MathF.Sqrt(MathF.Max(0f, 1f - r2));
                float nx = sx, ny = sy;

                // Normal en espacio de OBJETO: la rotación del usuario gira la bola bajo la
                // cámara fija. N_obj = Rx(-pitch) · Ry(-yaw) · N_vista.
                float ox = nx, oy = ny, oz = nz;
                if (rotated)
                {
                    float rx1 = ox * yawCos - oz * yawSin;
                    float rz1 = ox * yawSin + oz * yawCos;
                    ox = rx1;
                    float ry1 = oy * pitCos + rz1 * pitSin;
                    oz = -oy * pitSin + rz1 * pitCos;
                    oy = ry1;
                }

                // UV esférico sobre la bola rotada (como Unity: la textura envuelve la bola).
                float u = 0.5f + MathF.Atan2(ox, oz) / (MathF.PI * 2f);
                float v = 0.5f - oy * 0.5f;

                // Normal map en espacio tangente (T a lo largo de U, B a lo largo de V), calculado
                // en espacio de objeto y devuelto a espacio de vista para iluminar.
                if (normalMap is { } nm)
                {
                    nm.Sample(u, v, out float mr, out float mg, out float mb);
                    float tnx = mr * 2f - 1f, tny = mg * 2f - 1f, tnz = MathF.Max(0.2f, mb * 2f - 1f);
                    float phi = MathF.Atan2(ox, oz);
                    float cphi = MathF.Cos(phi), sphi = MathF.Sin(phi);
                    var t = (X: cphi, Y: 0f, Z: -sphi);
                    var b = Normalize3(-oy * sphi, oz * cphi + ox * sphi, -oy * cphi);
                    var pn = Normalize3(
                        t.X * tnx + b.X * tny + ox * tnz,
                        t.Y * tnx + b.Y * tny + oy * tnz,
                        t.Z * tnx + b.Z * tny + oz * tnz);

                    // De vuelta a vista: N_vista = Ry(yaw) · Rx(pitch) · N_obj.
                    float px = pn.X, py = pn.Y, pz = pn.Z;
                    if (rotated)
                    {
                        float ry2 = py * pitCos - pz * pitSin;
                        float rz2 = py * pitSin + pz * pitCos;
                        py = ry2;
                        float rx2 = px * yawCos + rz2 * yawSin;
                        pz = -px * yawSin + rz2 * yawCos;
                        px = rx2;
                    }
                    nx = px; ny = py; nz = pz;
                }

                // Parámetros de superficie (mapas en lineal; el albedo sí es sRGB).
                float albR = baseLinR, albG = baseLinG, albB = baseLinB;
                if (albedoMap is { } am)
                {
                    am.Sample(u, v, out float tr, out float tg, out float tb);
                    albR *= SrgbToLinear(tr); albG *= SrgbToLinear(tg); albB *= SrgbToLinear(tb);
                }

                float roughness = Math.Clamp(data.Roughness, 0f, 1f);
                if (roughnessMap is { } rm) { rm.Sample(u, v, out float rr0, out _, out _); roughness *= rr0; }
                roughness = Math.Clamp(roughness, 0.045f, 1f);

                float metallic = Math.Clamp(data.Metallic, 0f, 1f);
                if (metallicMap is { } mm) { mm.Sample(u, v, out float mm0, out _, out _); metallic *= mm0; }

                // Cook-Torrance: F0 dieléctrico 4%, metálico usa el albedo como F0.
                float f0R = 0.04f + (albR - 0.04f) * metallic;
                float f0G = 0.04f + (albG - 0.04f) * metallic;
                float f0B = 0.04f + (albB - 0.04f) * metallic;
                float kdR = albR * (1f - metallic);
                float kdG = albG * (1f - metallic);
                float kdB = albB * (1f - metallic);

                float ndv = MathF.Max(nz, 1e-4f); // V = (0,0,1)

                float outR = 0f, outG = 0f, outB = 0f;
                AccumulateGgxLight(nx, ny, nz, ndv, key, keyIntensity, 1.00f, 0.98f, 0.94f, roughness, f0R, f0G, f0B, kdR, kdG, kdB, specScale: 1f, ref outR, ref outG, ref outB);
                // El fill es solo difuso (softbox): un segundo lóbulo especular pintaba "dos soles" en metales pulidos.
                AccumulateGgxLight(nx, ny, nz, ndv, fill, fillIntensity, 0.62f, 0.70f, 0.86f, roughness, f0R, f0G, f0B, kdR, kdG, kdB, specScale: 0f, ref outR, ref outG, ref outB);

                // Ambiente difuso: gradiente de estudio (cielo arriba, suelo abajo).
                float hemi = ny * 0.5f + 0.5f;
                float ambR = 0.055f + hemi * 0.115f;
                float ambG = 0.060f + hemi * 0.120f;
                float ambB = 0.070f + hemi * 0.135f;
                outR += kdR * ambR; outG += kdG * ambG; outB += kdB * ambB;

                // Reflejo especular de ambiente: R = reflect(-V, N), entorno procedural
                // (gradiente + "ventana" de estudio) difuminado según roughness.
                float rx = 2f * ndv * nx, ry = 2f * ndv * ny, rz = 2f * ndv * nz - 1f;
                SampleStudioEnvironment(rx, ry, rz, roughness, out float envR, out float envG, out float envB);
                float fresnel = MathF.Pow(1f - ndv, 5f);
                float grazing = MathF.Max(1f - roughness, 0.04f);
                float envSpecR = f0R + (grazing - f0R) * fresnel;
                float envSpecG = f0G + (grazing - f0G) * fresnel;
                float envSpecB = f0B + (grazing - f0B) * fresnel;
                float envFade = 1f - roughness * roughness * 0.55f; // los rugosos reflejan menos definido
                outR += envR * envSpecR * envFade;
                outG += envG * envSpecG * envFade;
                outB += envB * envSpecB * envFade;

                outR += emisR; outG += emisG; outB += emisB;

                // ACES + lineal→sRGB.
                float srgbR = LinearToSrgb(TonemapAces(outR));
                float srgbG = LinearToSrgb(TonemapAces(outG));
                float srgbB = LinearToSrgb(TonemapAces(outB));

                if (transparent)
                {
                    // Damero estilo Unity detrás de la superficie transparente.
                    bool check = (((x / 24) + (y / 24)) & 1) == 0;
                    float checker = check ? 0.44f : 0.30f;
                    srgbR = srgbR * surfaceAlpha + checker * (1f - surfaceAlpha);
                    srgbG = srgbG * surfaceAlpha + checker * (1f - surfaceAlpha);
                    srgbB = srgbB * surfaceAlpha + checker * (1f - surfaceAlpha);
                }

                int idx = row + x * 4;
                pixels[idx + 0] = (byte)ToByte(srgbB);
                pixels[idx + 1] = (byte)ToByte(srgbG);
                pixels[idx + 2] = (byte)ToByte(srgbR);
                pixels[idx + 3] = (byte)(coverage * 255f + 0.5f);
            }
        });

        var rect = new System.Drawing.Rectangle(0, 0, size, size);
        var locked = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            // El buffer propio es contiguo (size*4 por fila); el destino puede tener stride mayor.
            if (locked.Stride == size * 4)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, locked.Scan0, pixels.Length);
            }
            else
            {
                for (int y = 0; y < size; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * size * 4, locked.Scan0 + y * locked.Stride, size * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return bitmap;
    }

    // Una luz direccional con BRDF Cook-Torrance (GGX + Smith-Schlick + Fresnel-Schlick).
private static void AccumulateGgxLight(
        float nx, float ny, float nz, float ndv,
        (float X, float Y, float Z) l, float intensity, float lr, float lg, float lb,
        float roughness, float f0R, float f0G, float f0B, float kdR, float kdG, float kdB,
        float specScale,
        ref float outR, ref float outG, ref float outB)
    {
        float ndl = nx * l.X + ny * l.Y + nz * l.Z;
        if (ndl <= 0f)
            return;

        // H = normalize(L + V), V = (0,0,1).
        var h = Normalize3(l.X, l.Y, l.Z + 1f);
        float ndh = MathF.Max(0f, nx * h.X + ny * h.Y + nz * h.Z);
        float vdh = MathF.Max(1e-4f, h.Z); // V·H = H.Z

        float a = roughness * roughness;
        float a2 = a * a;
        float d = ndh * ndh * (a2 - 1f) + 1f;
        float ggxD = a2 / MathF.Max(1e-6f, MathF.PI * d * d);

        float k = a * 0.5f;
        float gv = ndv / (ndv * (1f - k) + k);
        float gl = ndl / (ndl * (1f - k) + k);
        float vis = (gv * gl) / MathF.Max(1e-4f, 4f * ndv * ndl);

        float fres = MathF.Pow(1f - vdh, 5f);
        float fR = f0R + (1f - f0R) * fres;
        float fG = f0G + (1f - f0G) * fres;
        float fB = f0B + (1f - f0B) * fres;

        float specCommon = ggxD * vis * ndl * intensity * specScale;
        float diffCommon = ndl * intensity / MathF.PI;

        outR += (kdR * diffCommon + fR * specCommon) * lr;
        outG += (kdG * diffCommon + fG * specCommon) * lg;
        outB += (kdB * diffCommon + fB * specCommon) * lb;
    }

    // Entorno de estudio procedural: gradiente vertical (suelo→horizonte→cielo)
    // más una "ventana" suave hacia la key light; el roughness difumina ambos.
private static void SampleStudioEnvironment(float rx, float ry, float rz, float roughness, out float r, out float g, out float b)
    {
        float len = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
        if (len > 1e-6f) { rx /= len; ry /= len; rz /= len; }

        float upness = ry * 0.5f + 0.5f;
        float ground = 0.050f, horizon = 0.135f, sky = 0.330f;
        float grad = upness < 0.5f
            ? ground + (horizon - ground) * (upness * 2f)
            : horizon + (sky - horizon) * ((upness - 0.5f) * 2f);

        // "Ventana": highlight rectangular suave (lo que hace que un metal pulido se lea como tal).
        var wdir = Normalize3(-0.38f, 0.58f, 0.72f);
        float towards = MathF.Max(0f, rx * wdir.X + ry * wdir.Y + rz * wdir.Z);
        float sharpness = 2f + (1f - roughness) * (1f - roughness) * 220f;
        float window = MathF.Pow(towards, sharpness) * (0.55f + (1f - roughness) * 1.35f);

        // El roughness alto tiende el entorno a su promedio (reflejo difuminado).
        float avg = 0.16f;
        float t = roughness * roughness;
        float baseEnv = grad + (avg - grad) * t;

        r = baseEnv + window * 1.00f;
        g = baseEnv + window * 0.99f;
        b = baseEnv * 1.06f + window * 0.96f;
    }

    private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float c)
    {
        c = MathF.Max(0f, c);
        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    // ACES aproximado (Narkowicz): mantiene los brillos especulares/emisión sin quemar.
    private static float TonemapAces(float x)
    {
        return Math.Clamp(x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f), 0f, 1f);
    }

    // ── Preview interactivo del inspector ────────────────────────────────────
    // Estado de la esfera rotable: solo el preview GRANDE del inspector renderiza
    // en vivo (y únicamente cuando cambian ángulo o material); las tiles del panel
    // siguen siendo la imagen estática del cache de disco.
    private string? matSphereLivePath;
    private float matSphereYaw;
    private float matSpherePitch;
    private int matSphereLiveTexture;
    private float matSphereRenderedYaw = float.NaN;
    private float matSphereRenderedPitch;
    private DateTime matSphereRenderedStamp;
    private MaterialPreviewMaps? matSphereLiveMaps;

    // Render en HILO DE FONDO (latest-wins, máx. 1 en vuelo): el hilo de UI solo sube la
    // textura cuando hay un bitmap listo (~0.3 ms). Renderizar en el draw bajaba los FPS
    // durante el arrastre (~4 ms/frame con base de 120 FPS = bajada visible).
    private readonly object matSphereRenderLock = new();
    private volatile bool matSphereRenderRunning;
    private System.Drawing.Bitmap? matSphereReadyBitmap;
    private float matSphereReadyYaw;
    private float matSphereReadyPitch;
    private DateTime matSphereReadyStamp;

    private IntPtr GetLiveMaterialSphereTexture(string path)
    {
        if (!TryGetMaterialPreviewSourceCache(path, out var source))
            return IntPtr.Zero;

        // 1) Si el worker dejó un bitmap listo, subirlo a GPU (esto es lo único que paga la UI).
        System.Drawing.Bitmap? ready = null;
        float readyYaw = 0f, readyPitch = 0f;
        DateTime readyStamp = default;
        lock (matSphereRenderLock)
        {
            if (matSphereReadyBitmap != null)
            {
                ready = matSphereReadyBitmap;
                matSphereReadyBitmap = null;
                readyYaw = matSphereReadyYaw;
                readyPitch = matSphereReadyPitch;
                readyStamp = matSphereReadyStamp;
            }
        }

        if (ready != null)
        {
            using (ready)
            {
                if (CreatePreviewTextureFromBitmap(ready, out int texture))
                {
                    if (matSphereLiveTexture != 0)
                        GL.DeleteTexture(matSphereLiveTexture);
                    matSphereLiveTexture = texture;
                    matSphereRenderedYaw = readyYaw;
                    matSphereRenderedPitch = readyPitch;
                    matSphereRenderedStamp = readyStamp;
                }
            }
        }

        // 2) Si el estado visible no coincide con el pedido, lanzar OTRO render en fondo
        //    (como mucho uno en vuelo; si el ratón siguió moviéndose, el siguiente lo recoge).
        bool dirty = matSphereLiveTexture == 0 ||
                     matSphereRenderedYaw != matSphereYaw ||
                     matSphereRenderedPitch != matSpherePitch ||
                     matSphereRenderedStamp != source.DependencyStamp;
        if (dirty && !matSphereRenderRunning)
        {
            // Los samplers se cargan UNA vez y se reutilizan durante el arrastre; se recargan
            // solo si el material cambió de mapas o de contenido.
            string mapsKey = MaterialPreviewMaps.KeyFor(source.Data);
            if (matSphereLiveMaps == null || matSphereLiveMaps.Key != mapsKey || matSphereRenderedStamp != source.DependencyStamp)
                matSphereLiveMaps = MaterialPreviewMaps.Load(source.Data, mapsKey);

            matSphereRenderRunning = true;
            float yaw = matSphereYaw;
            float pitch = matSpherePitch;
            var data = source.Data;
            var maps = matSphereLiveMaps;
            var stamp = source.DependencyStamp;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var bmp = GenerateMaterialSpherePreviewBitmap(data, yaw, pitch, maps);
                    lock (matSphereRenderLock)
                    {
                        matSphereReadyBitmap?.Dispose();
                        matSphereReadyBitmap = bmp;
                        matSphereReadyYaw = yaw;
                        matSphereReadyPitch = pitch;
                        matSphereReadyStamp = stamp;
                    }
                }
                catch (Exception ex)
                {
                    GrokoEngine.Debug.LogWarning($"[MaterialPreview] Render interactivo falló: {ex.Message}");
                }
                finally
                {
                    matSphereRenderRunning = false;
                }
            });
        }

        return matSphereLiveTexture != 0 ? (IntPtr)matSphereLiveTexture : IntPtr.Zero;
    }

    private void ResetLiveMaterialSphere(string path)
    {
        matSphereLivePath = path;
        matSphereYaw = 0f;
        matSpherePitch = 0f;
        matSphereRenderedYaw = float.NaN;
        matSphereLiveMaps = null;
    }

    // Los 4 samplers del material precargados para el render interactivo.
    private sealed class MaterialPreviewMaps
    {
        public PreviewSampler? Albedo;
        public PreviewSampler? Normal;
        public PreviewSampler? Roughness;
        public PreviewSampler? Metallic;
        public string Key = string.Empty;

        public static string KeyFor(MaterialAssetData data) =>
            (MaterialAsset.GetAlbedo(data) ?? "") + "|" + (data.NormalMapPath ?? "") + "|" +
            (data.RoughnessMapPath ?? "") + "|" + (data.MetallicMapPath ?? "");

        public static MaterialPreviewMaps Load(MaterialAssetData data, string key) => new()
        {
            Albedo = PreviewSampler.Load(MaterialAsset.GetAlbedo(data), 256),
            Normal = PreviewSampler.Load(data.NormalMapPath, 256),
            Roughness = PreviewSampler.Load(data.RoughnessMapPath, 128),
            Metallic = PreviewSampler.Load(data.MetallicMapPath, 128),
            Key = key
        };
    }

    // Textura pequeña bloqueada en memoria para el generador de previews:
    // BGRA contigua + muestreo bilineal con wrap. Nada de Bitmap.GetPixel.
    private readonly struct PreviewSampler
    {
        private readonly byte[] pixels;
        private readonly int width;
        private readonly int height;
        private readonly int stride;

        private PreviewSampler(byte[] pixels, int width, int height, int stride)
        {
            this.pixels = pixels;
            this.width = width;
            this.height = height;
            this.stride = stride;
        }

        public static PreviewSampler? Load(string? path, int maxSize)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string? full = SceneViewportRenderer.NormalizeExistingAssetPath(path);
            if (full == null || !File.Exists(full))
                return null;

            try
            {
                using var bitmap = LoadSmallMaterialAlbedo(full, maxSize);
                if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                    return null;

                var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var px = new byte[data.Stride * data.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                    return new PreviewSampler(px, bitmap.Width, bitmap.Height, data.Stride);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            catch
            {
                return null;
            }
        }

        // Bilineal con wrap. Devuelve canales 0..1 tal cual están en la textura (sRGB para
        // albedo, lineal para mapas de datos); el llamador decide la conversión.
        public void Sample(float u, float v, out float r, out float g, out float b)
        {
            u -= MathF.Floor(u);
            v -= MathF.Floor(v);
            float fx = u * width - 0.5f;
            float fy = v * height - 0.5f;
            int x0 = (int)MathF.Floor(fx);
            int y0 = (int)MathF.Floor(fy);
            float tx = fx - x0;
            float ty = fy - y0;
            int x1 = Wrap(x0 + 1, width);
            int y1 = Wrap(y0 + 1, height);
            x0 = Wrap(x0, width);
            y0 = Wrap(y0, height);

            int i00 = y0 * stride + x0 * 4;
            int i10 = y0 * stride + x1 * 4;
            int i01 = y1 * stride + x0 * 4;
            int i11 = y1 * stride + x1 * 4;

            b = Bilerp(pixels[i00 + 0], pixels[i10 + 0], pixels[i01 + 0], pixels[i11 + 0], tx, ty);
            g = Bilerp(pixels[i00 + 1], pixels[i10 + 1], pixels[i01 + 1], pixels[i11 + 1], tx, ty);
            r = Bilerp(pixels[i00 + 2], pixels[i10 + 2], pixels[i01 + 2], pixels[i11 + 2], tx, ty);
        }

        private static int Wrap(int i, int n)
        {
            i %= n;
            return i < 0 ? i + n : i;
        }

        private static float Bilerp(byte c00, byte c10, byte c01, byte c11, float tx, float ty)
        {
            float top = c00 + (c10 - c00) * tx;
            float bottom = c01 + (c11 - c01) * tx;
            return (top + (bottom - top) * ty) / 255f;
        }
    }

private static (float X, float Y, float Z) Normalize3(float x, float y, float z)
    {
        float len = MathF.Sqrt(x * x + y * y + z * z);
        return len <= 0f ? (0f, 0f, 1f) : (x / len, y / len, z / len);
    }

private readonly record struct MeshPreviewVertex(float X, float Y, float Z, float ScreenX, float ScreenY, float U, float V);

private readonly record struct MeshPreviewTriangle(System.Drawing.PointF A, System.Drawing.PointF B, System.Drawing.PointF C, float Depth, System.Drawing.Color Color);

private static System.Drawing.Bitmap GenerateAssetPreviewBitmap(string path, string kind)
    {
        if (kind == "MESH" && TryGenerateMeshAssetPreviewBitmap(path, out var meshBitmap))
            return meshBitmap;

        // Prefab: renderiza la malla real del objeto (como Unity), no un cubo genérico.
        if (kind == "PREF" && TryGetPrefabMeshPath(path, out var prefabMesh)
            && TryGenerateMeshAssetPreviewBitmap(prefabMesh, out var prefabBitmap))
            return prefabBitmap;

        const int size = 128;
        var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.FromArgb(255, 29, 33, 38));
        using var gridPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(70, 100, 115, 130), 1f);
        for (int i = 16; i < size; i += 16)
        {
            g.DrawLine(gridPen, i, 28, i, 108);
            g.DrawLine(gridPen, 14, i, 114, i);
        }

        using var shadow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(80, 0, 0, 0));
        g.FillEllipse(shadow, 30, 86, 68, 16);

        var main = kind == "PREF"
            ? System.Drawing.Color.FromArgb(255, 84, 138, 230)
            : kind == "SCENE"
                ? System.Drawing.Color.FromArgb(255, 86, 180, 160)
                : System.Drawing.Color.FromArgb(255, 210, 145, 74);
        using var face = new System.Drawing.SolidBrush(main);
        using var top = new System.Drawing.SolidBrush(AdjustColor(main, 1.28f));
        using var side = new System.Drawing.SolidBrush(AdjustColor(main, 0.68f));
        using var edge = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 12, 14, 16), 2f);

        if (kind == "SCENE")
        {
            using var cam = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(235, 160, 205, 255));
            using var light = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(245, 255, 198, 82));
            g.FillRectangle(cam, 24, 34, 34, 24);
            g.FillPolygon(cam, new[] { new System.Drawing.PointF(58, 40), new System.Drawing.PointF(78, 32), new System.Drawing.PointF(78, 66) });
            g.FillEllipse(light, 82, 36, 18, 18);
            using var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 13f, System.Drawing.FontStyle.Bold);
            g.DrawString("SCENE", font, System.Drawing.Brushes.WhiteSmoke, 23, 94);
            return bitmap;
        }

        var p1 = new[] { new System.Drawing.PointF(36, 44), new System.Drawing.PointF(64, 28), new System.Drawing.PointF(92, 44), new System.Drawing.PointF(64, 60) };
        var p2 = new[] { new System.Drawing.PointF(92, 44), new System.Drawing.PointF(92, 78), new System.Drawing.PointF(64, 96), new System.Drawing.PointF(64, 60) };
        var p3 = new[] { new System.Drawing.PointF(36, 44), new System.Drawing.PointF(64, 60), new System.Drawing.PointF(64, 96), new System.Drawing.PointF(36, 78) };
        g.FillPolygon(top, p1);
        g.FillPolygon(side, p2);
        g.FillPolygon(face, p3);
        g.DrawPolygon(edge, p1);
        g.DrawPolygon(edge, p2);
        g.DrawPolygon(edge, p3);

        if (kind == "MESH")
        {
            var mesh = ObjLoader.Load(path);
            using var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 10f, System.Drawing.FontStyle.Bold);
            string meta = mesh == null ? "MESH" : $"{mesh.Positions.Length / 3}v";
            g.DrawString(meta, font, System.Drawing.Brushes.WhiteSmoke, 40, 102);
        }
        else
        {
            using var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 12f, System.Drawing.FontStyle.Bold);
            g.DrawString("PREFAB", font, System.Drawing.Brushes.WhiteSmoke, 28, 102);
        }

        return bitmap;
    }

private static bool TryGenerateMeshAssetPreviewBitmap(string path, out System.Drawing.Bitmap bitmap)
    {
        const int size = 128;
        bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        var mesh = ObjLoader.Load(path);
        if (mesh == null || mesh.Positions.Length < 9)
            return false;

        var textureCache = new Dictionary<string, System.Drawing.Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.FromArgb(255, 82, 82, 82));

            DrawUnityLikePreviewBackground(g, size);

            int vertexCount = mesh.Positions.Length / 3;
            var vertices = new MeshPreviewVertex[vertexCount];
            BuildMeshPreviewVertices(mesh, vertices, size);

            float minX = vertices.Min(v => v.ScreenX);
            float maxX = vertices.Max(v => v.ScreenX);
            float minY = vertices.Min(v => v.ScreenY);
            float maxY = vertices.Max(v => v.ScreenY);
            using (var shadow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(82, 0, 0, 0)))
            {
                float shadowW = Math.Clamp((maxX - minX) * 0.70f, 22f, size * 0.76f);
                float shadowH = Math.Clamp((maxY - minY) * 0.09f, 5f, 14f);
                g.FillEllipse(shadow, (size - shadowW) * 0.5f, Math.Min(size - 18f, maxY - shadowH * 0.55f), shadowW, shadowH);
            }

            var triangles = BuildMeshPreviewTriangles(mesh, vertices, textureCache);
            foreach (var tri in triangles.OrderBy(t => t.Depth))
            {
                using var brush = new System.Drawing.SolidBrush(tri.Color);
                g.FillPolygon(brush, new[] { tri.A, tri.B, tri.C });
            }

            using var rim = new System.Drawing.Pen(System.Drawing.Color.FromArgb(92, 18, 18, 18), 1.1f);
            foreach (var tri in triangles.Where(t => IsLargePreviewTriangle(t)))
                g.DrawPolygon(rim, new[] { tri.A, tri.B, tri.C });

            using var frame = new System.Drawing.Pen(System.Drawing.Color.FromArgb(95, 25, 25, 25), 1f);
            g.DrawRectangle(frame, 0.5f, 0.5f, size - 1f, size - 1f);
            return true;
        }
        catch
        {
            bitmap.Dispose();
            bitmap = null!;
            return false;
        }
        finally
        {
            foreach (var texture in textureCache.Values)
                texture.Dispose();
        }
    }

private static void DrawUnityLikePreviewBackground(System.Drawing.Graphics g, int size)
    {
        using var top = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.Rectangle(0, 0, size, size),
            System.Drawing.Color.FromArgb(255, 96, 96, 96),
            System.Drawing.Color.FromArgb(255, 62, 62, 62),
            90f);
        g.FillRectangle(top, 0, 0, size, size);

        using var vignette = new System.Drawing.Drawing2D.GraphicsPath();
        vignette.AddEllipse(-size * 0.18f, -size * 0.24f, size * 1.36f, size * 1.42f);
        using var brush = new System.Drawing.Drawing2D.PathGradientBrush(vignette)
        {
            CenterColor = System.Drawing.Color.FromArgb(0, 255, 255, 255),
            SurroundColors = new[] { System.Drawing.Color.FromArgb(78, 20, 20, 20) }
        };
        g.FillRectangle(brush, 0, 0, size, size);
    }

private static void BuildMeshPreviewVertices(ParsedMesh mesh, MeshPreviewVertex[] vertices, int size)
    {
        float minX = mesh.BoundsMin.X;
        float minY = mesh.BoundsMin.Y;
        float minZ = mesh.BoundsMin.Z;
        float maxX = mesh.BoundsMax.X;
        float maxY = mesh.BoundsMax.Y;
        float maxZ = mesh.BoundsMax.Z;

        if (maxX <= minX || maxY <= minY || maxZ <= minZ)
        {
            minX = minY = minZ = float.PositiveInfinity;
            maxX = maxY = maxZ = float.NegativeInfinity;
            for (int i = 0; i < mesh.Positions.Length; i += 3)
            {
                float x = mesh.Positions[i];
                float y = mesh.Positions[i + 1];
                float z = mesh.Positions[i + 2];
                minX = MathF.Min(minX, x); minY = MathF.Min(minY, y); minZ = MathF.Min(minZ, z);
                maxX = MathF.Max(maxX, x); maxY = MathF.Max(maxY, y); maxZ = MathF.Max(maxZ, z);
            }
        }

        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float cz = (minZ + maxZ) * 0.5f;
        const float yaw = -0.48f;
        const float pitch = 0.20f;
        float cyaw = MathF.Cos(yaw);
        float syaw = MathF.Sin(yaw);
        float cpitch = MathF.Cos(pitch);
        float spitch = MathF.Sin(pitch);

        float projMinX = float.PositiveInfinity;
        float projMaxX = float.NegativeInfinity;
        float projMinY = float.PositiveInfinity;
        float projMaxY = float.NegativeInfinity;
        var projected = new (float X, float Y, float Z)[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            int pi = i * 3;
            float x = mesh.Positions[pi] - cx;
            float y = mesh.Positions[pi + 1] - cy;
            float z = mesh.Positions[pi + 2] - cz;

            float rx = x * cyaw + z * syaw;
            float rz = -x * syaw + z * cyaw;
            float ry = y * cpitch - rz * spitch;
            float rz2 = y * spitch + rz * cpitch;

            projected[i] = (rx, ry, rz2);
            projMinX = MathF.Min(projMinX, rx);
            projMaxX = MathF.Max(projMaxX, rx);
            projMinY = MathF.Min(projMinY, ry);
            projMaxY = MathF.Max(projMaxY, ry);
        }

        float width = MathF.Max(0.0001f, projMaxX - projMinX);
        float height = MathF.Max(0.0001f, projMaxY - projMinY);
        float scale = size * 0.80f / MathF.Max(width, height);
        float offsetX = size * 0.5f - (projMinX + projMaxX) * 0.5f * scale;
        float offsetY = size * 0.52f + (projMinY + projMaxY) * 0.5f * scale;

        for (int i = 0; i < vertices.Length; i++)
        {
            int ui = i * 2;
            float u = mesh.UVs.Length > ui + 1 ? mesh.UVs[ui] : 0.5f;
            float v = mesh.UVs.Length > ui + 1 ? mesh.UVs[ui + 1] : 0.5f;
            var p = projected[i];
            vertices[i] = new MeshPreviewVertex(
                p.X,
                p.Y,
                p.Z,
                offsetX + p.X * scale,
                offsetY - p.Y * scale,
                u,
                v);
        }
    }

private static List<MeshPreviewTriangle> BuildMeshPreviewTriangles(ParsedMesh mesh, MeshPreviewVertex[] vertices, Dictionary<string, System.Drawing.Bitmap> textureCache)
    {
        var triangles = new List<MeshPreviewTriangle>(Math.Min(mesh.TriangleCount, 16000));
        int triangleCount = Math.Min(mesh.TriangleCount, vertices.Length / 3);
        int step = Math.Max(1, triangleCount / 14000);
        int submeshHint = 0;
        var light = Normalize3(-0.42f, -0.58f, 0.72f);

        for (int tri = 0; tri < triangleCount; tri += step)
        {
            int vi = tri * 3;
            if (vi + 2 >= vertices.Length)
                break;

            var a = vertices[vi];
            var b = vertices[vi + 1];
            var c = vertices[vi + 2];

            float area = (b.ScreenX - a.ScreenX) * (c.ScreenY - a.ScreenY) - (b.ScreenY - a.ScreenY) * (c.ScreenX - a.ScreenX);
            if (MathF.Abs(area) < 0.08f)
                continue;

            var normal = PreviewTriangleNormal(a, b, c);
            float facing = MathF.Abs(normal.Z);
            float diffuse = Math.Clamp(MathF.Abs(normal.X * light.X + normal.Y * light.Y + normal.Z * light.Z), 0f, 1f);
            float shade = Math.Clamp(0.34f + diffuse * 0.58f + facing * 0.18f, 0.25f, 1.18f);

            var submesh = ResolvePreviewSubmesh(mesh, vi, ref submeshHint);
            float r = submesh?.DiffuseR ?? 0.62f;
            float g = submesh?.DiffuseG ?? 0.65f;
            float bl = submesh?.DiffuseB ?? 0.68f;

            System.Drawing.Bitmap? texture = GetPreviewSubmeshTexture(submesh, textureCache);
            if (texture != null)
                SamplePreviewTexture(texture, (a.U + b.U + c.U) / 3f, (a.V + b.V + c.V) / 3f, ref r, ref g, ref bl);

            int alpha = facing < 0.08f ? 210 : 255;
            var color = System.Drawing.Color.FromArgb(alpha, ToByte(r * shade + 0.025f), ToByte(g * shade + 0.025f), ToByte(bl * shade + 0.025f));
            triangles.Add(new MeshPreviewTriangle(
                new System.Drawing.PointF(a.ScreenX, a.ScreenY),
                new System.Drawing.PointF(b.ScreenX, b.ScreenY),
                new System.Drawing.PointF(c.ScreenX, c.ScreenY),
                (a.Z + b.Z + c.Z) / 3f,
                color));
        }

        return triangles;
    }

private static (float X, float Y, float Z) PreviewTriangleNormal(MeshPreviewVertex a, MeshPreviewVertex b, MeshPreviewVertex c)
    {
        float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        float nx = uy * vz - uz * vy;
        float ny = uz * vx - ux * vz;
        float nz = ux * vy - uy * vx;
        float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        return len <= 0.0001f ? (0f, 0f, 1f) : (nx / len, ny / len, nz / len);
    }

private static MeshSubmesh? ResolvePreviewSubmesh(ParsedMesh mesh, int vertexStart, ref int hint)
    {
        if (mesh.Submeshes.Count == 0)
            return null;

        hint = Math.Clamp(hint, 0, mesh.Submeshes.Count - 1);
        while (hint + 1 < mesh.Submeshes.Count && vertexStart >= mesh.Submeshes[hint].VertexStart + mesh.Submeshes[hint].VertexCount)
            hint++;

        var candidate = mesh.Submeshes[hint];
        if (vertexStart >= candidate.VertexStart && vertexStart < candidate.VertexStart + candidate.VertexCount)
            return candidate;

        for (int i = 0; i < mesh.Submeshes.Count; i++)
        {
            var sub = mesh.Submeshes[i];
            if (vertexStart >= sub.VertexStart && vertexStart < sub.VertexStart + sub.VertexCount)
            {
                hint = i;
                return sub;
            }
        }

        return null;
    }

private static System.Drawing.Bitmap? GetPreviewSubmeshTexture(MeshSubmesh? submesh, Dictionary<string, System.Drawing.Bitmap> textureCache)
    {
        if (string.IsNullOrWhiteSpace(submesh?.TexturePath))
            return null;

        string? texturePath = SceneViewportRenderer.NormalizeExistingAssetPath(submesh.TexturePath);
        if (texturePath == null || !File.Exists(texturePath))
            return null;

        if (textureCache.TryGetValue(texturePath, out var cached))
            return cached;

        var bitmap = LoadSmallMaterialAlbedo(texturePath, 192);
        if (bitmap == null)
            return null;

        textureCache[texturePath] = bitmap;
        return bitmap;
    }

private static void SamplePreviewTexture(System.Drawing.Bitmap texture, float u, float v, ref float r, ref float g, ref float b)
    {
        if (texture.Width <= 0 || texture.Height <= 0)
            return;

        u -= MathF.Floor(u);
        v = 1f - v;
        v -= MathF.Floor(v);

        int x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
        int y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
        var color = texture.GetPixel(x, y);
        r = color.R / 255f;
        g = color.G / 255f;
        b = color.B / 255f;
    }

private static bool IsLargePreviewTriangle(MeshPreviewTriangle tri)
    {
        float area = MathF.Abs((tri.B.X - tri.A.X) * (tri.C.Y - tri.A.Y) - (tri.B.Y - tri.A.Y) * (tri.C.X - tri.A.X));
        return area > 18f;
    }

private static System.Drawing.Color AdjustColor(System.Drawing.Color color, float factor)
    {
        int r = Math.Clamp((int)(color.R * factor), 0, 255);
        int g = Math.Clamp((int)(color.G * factor), 0, 255);
        int b = Math.Clamp((int)(color.B * factor), 0, 255);
        return System.Drawing.Color.FromArgb(color.A, r, g, b);
    }

private static bool CreatePreviewTextureFromBitmap(System.Drawing.Bitmap bitmap, out int texture)
    {
        texture = 0;

        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(
            rect,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                bitmap.Width,
                bitmap.Height,
                0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                PixelType.UnsignedByte,
                data.Scan0);

            return true;
        }
        catch
        {
            if (texture != 0)
            {
                GL.DeleteTexture(texture);
                texture = 0;
            }

            return false;
        }
        finally
        {
            bitmap.UnlockBits(data);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
    }

private bool LoadPreviewTextureFromDiskCache(string path, out int texture)
    {
        texture = 0;

        if (!TryGetCurrentDiskPreview(path, out string previewPath))
            return false;

        // No llamar este mismo método con previewPath: eso causa recursión.
        // Aquí cargamos directamente el PNG pequeño guardado en Library/Previews.
        return LoadPreviewTextureFile(previewPath, out texture);
    }

private bool EnsureProjectAssetDiskPreview(string assetPath, out string previewPath)
    {
        previewPath = GetPreviewCachePath(assetPath);
        if (!File.Exists(assetPath) || !ShouldQueuePreviewAsset(assetPath))
            return false;

        DateTime stamp = GetPreviewDependencyStamp(assetPath);
        if (File.Exists(previewPath))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(previewPath) >= stamp)
                    return true;
            }
            catch (Exception ex)
            {
                GrokoEngine.Debug.LogWarning($"No se pudo validar preview cache '{previewPath}': {ex.Message}");
                try { File.Delete(previewPath); }
                catch (Exception deleteEx)
                {
                    GrokoEngine.Debug.LogWarning($"No se pudo borrar preview cache corrupto '{previewPath}': {deleteEx.Message}");
                }
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);

            using System.Drawing.Bitmap? bitmap = CreateDiskPreviewBitmap(assetPath);
            if (bitmap == null)
                return false;

            using var previewBitmap = CreatePreviewBitmap(bitmap, 128);
            string tempPath = previewPath + ".tmp";
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                GrokoEngine.Debug.LogWarning($"No se pudo borrar preview temporal '{tempPath}': {ex.Message}");
            }

            previewBitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
            File.SetLastWriteTimeUtc(tempPath, stamp);

            if (File.Exists(previewPath))
                File.Delete(previewPath);

            File.Move(tempPath, previewPath);
            File.SetLastWriteTimeUtc(previewPath, stamp);
            return true;
        }
        catch
        {
            return false;
        }
    }

private static System.Drawing.Bitmap? CreateDiskPreviewBitmap(string assetPath)
    {
        if (MaterialAsset.IsTexturePath(assetPath))
        {
            using var original = EditorImageLoader.LoadBitmap(assetPath);
            if (original == null)
                return null;

            return CreatePreviewBitmap(original, 128);
        }

        if (MaterialAsset.IsMaterialPath(assetPath))
        {
            var data = MaterialAsset.Load(assetPath);
            return GenerateMaterialSpherePreviewBitmap(data);
        }

        string kind = GetAssetKind(assetPath);
        if (kind == "MESH" || kind == "PREF" || kind == "SCENE")
            return GenerateAssetPreviewBitmap(assetPath, kind);

        return null;
    }

private bool TryGetCurrentDiskPreview(string assetPath, out string previewPath)
    {
        previewPath = GetPreviewCachePath(assetPath);
        if (!File.Exists(assetPath) || !File.Exists(previewPath))
            return false;

        try
        {
            return File.GetLastWriteTimeUtc(previewPath) >= GetPreviewDependencyStamp(assetPath);
        }
        catch
        {
            return false;
        }
    }

private DateTime GetPreviewDependencyStamp(string path)
    {
        DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

        try
        {
            if (MaterialAsset.IsMaterialPath(path))
            {
                if (TryGetMaterialPreviewSourceCache(path, out var cached))
                    return cached.DependencyStamp > stamp ? cached.DependencyStamp : stamp;
            }
            else if (GetAssetKind(path) == "MESH")
            {
                var mesh = ObjLoader.Load(path);
                if (mesh != null)
                {
                    foreach (var submesh in mesh.Submeshes)
                        AddDependencyStamp(submesh.TexturePath, ref stamp);
                }
            }
        }
        catch
        {
            return stamp;
        }

        return stamp;
    }

private static bool LoadPreviewTextureFile(string path, out int texture)
    {
        texture = 0;

        try
        {
            using var source = EditorImageLoader.LoadBitmap(path);
            if (source == null)
                return false;

            using var bitmap = new System.Drawing.Bitmap(
                source.Width,
                source.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            return CreatePreviewTextureFromBitmap(bitmap, out texture);
        }
        catch
        {
            if (texture != 0)
            {
                GL.DeleteTexture(texture);
                texture = 0;
            }

            return false;
        }
    }


}
