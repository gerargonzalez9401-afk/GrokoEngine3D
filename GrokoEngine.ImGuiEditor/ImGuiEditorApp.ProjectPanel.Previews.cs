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
            // DrawMaterialPreview dibuja la esfera cacheada si existe y, con
            // requestPreview, encola la generación del visible.
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

private static void DrawSubmeshAssetPreview(ImDrawListPtr drawList, Vector2 min, float size)
    {
        var max = min + new Vector2(size, size);
        uint bg = ImGui.GetColorU32(new System.Numerics.Vector4(0.19f, 0.24f, 0.27f, 1f));
        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(0.48f, 0.78f, 0.92f, 1f));
        uint soft = ImGui.GetColorU32(new System.Numerics.Vector4(0.24f, 0.48f, 0.58f, 1f));
        drawList.AddRectFilled(min, max, bg, 4f);

        float pad = size * 0.18f;
        var a = min + new Vector2(pad, size * 0.70f);
        var b = min + new Vector2(size * 0.48f, pad);
        var c = min + new Vector2(size - pad, size * 0.70f);
        var d = min + new Vector2(size * 0.50f, size * 0.84f);

        drawList.AddTriangleFilled(a, b, d, soft);
        drawList.AddTriangleFilled(b, c, d, soft);
        drawList.AddLine(a, b, line, Math.Max(1f, size * 0.035f));
        drawList.AddLine(b, c, line, Math.Max(1f, size * 0.035f));
        drawList.AddLine(c, d, line, Math.Max(1f, size * 0.035f));
        drawList.AddLine(d, a, line, Math.Max(1f, size * 0.035f));
        drawList.AddLine(b, d, line, Math.Max(1f, size * 0.03f));
    }

private static void DrawAvatarAssetPreview(ImDrawListPtr drawList, Vector2 min, float size)
    {
        DrawFileAssetPreview(drawList, min, size, "AV", ProjectAssetColorForKind("AVATAR"));
        uint mint = ImGui.GetColorU32(new System.Numerics.Vector4(0.55f, 1f, 0.88f, 1f));
        var c = min + new Vector2(size * 0.5f, size * 0.44f);
        float r = size * 0.08f;
        drawList.AddCircleFilled(c + new Vector2(0f, -size * 0.16f), r, mint, 18);
        drawList.AddLine(c + new Vector2(0f, -size * 0.07f), c + new Vector2(0f, size * 0.18f), mint, Math.Max(1.6f, size * 0.045f));
        drawList.AddLine(c + new Vector2(-size * 0.17f, size * 0.02f), c + new Vector2(size * 0.17f, size * 0.02f), mint, Math.Max(1.4f, size * 0.04f));
        drawList.AddLine(c + new Vector2(0f, size * 0.18f), c + new Vector2(-size * 0.13f, size * 0.34f), mint, Math.Max(1.4f, size * 0.04f));
        drawList.AddLine(c + new Vector2(0f, size * 0.18f), c + new Vector2(size * 0.13f, size * 0.34f), mint, Math.Max(1.4f, size * 0.04f));
    }

private static void DrawAnimationAssetPreview(ImDrawListPtr drawList, Vector2 min, float size)
    {
        DrawFileAssetPreview(drawList, min, size, "AN", ProjectAssetColorForKind("ANIM"));
        uint teal = ImGui.GetColorU32(new System.Numerics.Vector4(0.48f, 0.98f, 0.86f, 1f));
        var a = min + new Vector2(size * 0.34f, size * 0.24f);
        var b = min + new Vector2(size * 0.70f, size * 0.50f);
        var c = min + new Vector2(size * 0.34f, size * 0.76f);
        drawList.AddTriangleFilled(a, b, c, teal);
    }

private static void DrawParticlePresetAssetPreview(ImDrawListPtr drawList, Vector2 min, float size)
    {
        var max = min + new Vector2(size, size);
        uint bgTop = ImGui.GetColorU32(new System.Numerics.Vector4(0.15f, 0.08f, 0.20f, 1f));
        uint bgBottom = ImGui.GetColorU32(new System.Numerics.Vector4(0.04f, 0.05f, 0.08f, 1f));
        uint border = ImGui.GetColorU32(new System.Numerics.Vector4(0.78f, 0.42f, 1.00f, 1f));
        uint glow = ImGui.GetColorU32(new System.Numerics.Vector4(0.72f, 0.32f, 1.00f, 0.32f));
        uint hot = ImGui.GetColorU32(new System.Numerics.Vector4(1.00f, 0.72f, 0.25f, 1f));
        uint pink = ImGui.GetColorU32(new System.Numerics.Vector4(0.92f, 0.35f, 1.00f, 1f));
        uint blue = ImGui.GetColorU32(new System.Numerics.Vector4(0.34f, 0.78f, 1.00f, 1f));
        uint white = ImGui.GetColorU32(new System.Numerics.Vector4(0.92f, 0.96f, 1.00f, 1f));

        drawList.AddRectFilledMultiColor(min, max, bgTop, bgTop, bgBottom, bgBottom);
        drawList.AddRect(min, max, border, 6f, ImDrawFlags.None, Math.Max(1.2f, size * 0.035f));

        var center = min + new Vector2(size * 0.50f, size * 0.55f);
        for (int i = 0; i < 7; i++)
        {
            float a = -1.2f + i * 0.42f;
            float len = size * (0.22f + (i % 3) * 0.045f);
            var p0 = center + new Vector2(MathF.Cos(a) * size * 0.06f, MathF.Sin(a) * size * 0.04f);
            var p1 = center + new Vector2(MathF.Cos(a) * len, MathF.Sin(a) * len);
            uint col = i % 3 == 0 ? hot : i % 3 == 1 ? pink : blue;
            drawList.AddLine(p0, p1, col, Math.Max(1.2f, size * 0.035f));
            drawList.AddCircleFilled(p1, Math.Max(1.6f, size * 0.045f), col, 14);
        }

        drawList.AddCircleFilled(center, size * 0.18f, glow, 24);
        drawList.AddCircleFilled(center, size * 0.075f, white, 18);

        if (size >= 38f)
            drawList.AddText(min + new Vector2(size * 0.17f, size * 0.09f), white, "FX");
    }

private void DrawFolderPreview(ImDrawListPtr drawList, Vector2 min, float size, bool hasContent)
    {
        IntPtr textureId;
        bool gotTexture = hasContent
            ? TryGetFolderFullIconTexture(out textureId)
            : TryGetFolderEmptyIconTexture(out textureId);

        if (gotTexture)
        {
            DrawImagePreview(drawList, textureId, min, size);
            return;
        }

        DrawFileAssetPreview(drawList, min, size, "DIR", new System.Numerics.Vector4(0.55f, 0.55f, 0.58f, 1f));
    }

private static void DrawScriptAssetPreview(ImDrawListPtr drawList, Vector2 min, float size)
    {
        var max = min + new Vector2(size, size);
        uint shadow = ImGui.GetColorU32(new System.Numerics.Vector4(0.05f, 0.06f, 0.06f, 1f));
        uint paper = ImGui.GetColorU32(new System.Numerics.Vector4(0.90f, 0.91f, 0.90f, 1f));   // documento gris claro
        uint fold = ImGui.GetColorU32(new System.Numerics.Vector4(0.68f, 0.70f, 0.68f, 1f));   // esquina doblada
        uint green = ImGui.GetColorU32(new System.Numerics.Vector4(0.13f, 0.52f, 0.20f, 1f));   // verde C#

        Vector2 a = min + new Vector2(size * 0.18f, size * 0.05f);
        Vector2 b = max - new Vector2(size * 0.16f, size * 0.05f);
        float cut = size * 0.22f;

        // Documento con esquina superior-derecha doblada
        drawList.AddRectFilled(a + new Vector2(2f, 3f), b + new Vector2(2f, 3f), shadow, 3f);
        drawList.AddRectFilled(a, b, paper, 3f);
        drawList.AddTriangleFilled(new Vector2(b.X - cut, a.Y), new Vector2(b.X, a.Y + cut), new Vector2(b.X - cut, a.Y + cut), fold);

        // "#" verde grande (2 barras verticales inclinadas + 2 horizontales)
        float cx = (a.X + b.X) * 0.5f;
        float cy = (a.Y + b.Y) * 0.5f - size * 0.02f;
        float h = size * 0.19f;
        float w = size * 0.17f;
        float sp = size * 0.075f;
        float sk = size * 0.045f;
        float th = Math.Max(1.6f, size * 0.05f);
        drawList.AddLine(new Vector2(cx - sp - sk, cy - h), new Vector2(cx - sp + sk, cy + h), green, th);
        drawList.AddLine(new Vector2(cx + sp - sk, cy - h), new Vector2(cx + sp + sk, cy + h), green, th);
        drawList.AddLine(new Vector2(cx - w, cy - sp), new Vector2(cx + w, cy - sp), green, th);
        drawList.AddLine(new Vector2(cx - w, cy + sp), new Vector2(cx + w, cy + sp), green, th);

        // "cs" abajo-derecha (solo si el ícono es suficientemente grande)
        if (size >= 44f)
            drawList.AddText(new Vector2(b.X - size * 0.27f, b.Y - size * 0.24f), green, "cs");
    }

private static void DrawFileAssetPreview(ImDrawListPtr drawList, Vector2 min, float size, string kind, System.Numerics.Vector4 colorValue)
    {
        uint color = ImGui.GetColorU32(colorValue);
        uint shadow = ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.08f, 0.09f, 1f));
        uint fold = ImGui.GetColorU32(new System.Numerics.Vector4(colorValue.X * 0.60f, colorValue.Y * 0.60f, colorValue.Z * 0.60f, 1f));
        Vector2 docMin = min + new Vector2(size * 0.16f, size * 0.06f);
        Vector2 docMax = min + new Vector2(size * 0.84f, size * 0.92f);
        drawList.AddRectFilled(docMin + new Vector2(2f, 3f), docMax + new Vector2(2f, 3f), shadow, 4f);
        drawList.AddRectFilled(docMin, docMax, color, 4f);
        drawList.AddTriangleFilled(new Vector2(docMax.X - size * 0.22f, docMin.Y), new Vector2(docMax.X, docMin.Y), new Vector2(docMax.X, docMin.Y + size * 0.22f), fold);
        drawList.AddText(docMin + new Vector2(size * 0.10f, size * 0.36f), ImGui.GetColorU32(new System.Numerics.Vector4(0.96f, 0.96f, 0.96f, 1f)), kind);
    }

private bool TryGetEditorScriptIconTexture(out IntPtr textureId)
    {
        string iconPath = Path.Combine(projectPath, "Docs", "Photoshop", "CS_ICO.png");
        if (!File.Exists(iconPath))
        {
            textureId = IntPtr.Zero;
            return false;
        }

        return TryGetPreviewTexture(iconPath, out textureId);
    }

private bool TryGetCustomEditorIconTexture(string fileName, out IntPtr textureId)
    {
        textureId = IntPtr.Zero;

        string iconPath = Path.Combine(AppContext.BaseDirectory, "EditorIcons", fileName);

        if (!File.Exists(iconPath))
            return false;

        string key = "editoricon:" + iconPath;
        DateTime writeTime = File.GetLastWriteTimeUtc(iconPath);

        if (assetPreviewTextures.TryGetValue(key, out int cachedTexture) &&
            assetPreviewWriteTimes.TryGetValue(key, out DateTime cachedTime) &&
            cachedTime == writeTime &&
            cachedTexture != 0)
        {
            textureId = (IntPtr)cachedTexture;
            return true;
        }

        if (cachedTexture != 0)
        {
            GL.DeleteTexture(cachedTexture);
            assetPreviewTextures.Remove(key);
            assetPreviewWriteTimes.Remove(key);
        }

        if (!LoadPreviewTextureFile(iconPath, out int texture))
            return false;

        assetPreviewTextures[key] = texture;
        assetPreviewWriteTimes[key] = writeTime;

        textureId = (IntPtr)texture;
        return true;
    }

private bool TryGetFolderEmptyIconTexture(out IntPtr textureId) =>
        TryGetCustomEditorIconTexture("folder_empty.png", out textureId);

private bool TryGetFolderFullIconTexture(out IntPtr textureId) =>
        TryGetCustomEditorIconTexture("folder_full.png", out textureId);

private bool TryGetRigidbodyIconTexture(out IntPtr textureId) =>
        TryGetCustomEditorIconTexture("rigidbody.png", out textureId);

private static void DrawImagePreview(
    ImDrawListPtr drawList,
    IntPtr textureId,
    Vector2 min,
    float size)
    {
        var max = min + new Vector2(size, size);

        drawList.AddImage(
            textureId,
            min,
            max,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f)
        );
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
        materialPreviewSourceCache.Remove(path);

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
        if (MaterialAsset.IsTexturePath(path) || MaterialAsset.IsMaterialPath(path))
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

    private readonly Random previewValidationRng = new();

    // Intervalo de revalidación CON jitter: evita que decenas de previews cacheadas en el mismo
    // frame expiren todas a la vez (ráfaga de lecturas de disco → tirón de UI periódico). Las
    // reparte a lo largo de ~1 s extra.
    private TimeSpan PreviewValidationDelay() =>
        AssetPreviewValidationInterval + TimeSpan.FromMilliseconds(previewValidationRng.Next(0, 1000));

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
        if (TryGetShaderGraphMaterialPreviewSource(path, out var data, out var shaderGraphPath))
            return TryGetShaderGraphMaterialPreviewTexture(path, data, shaderGraphPath, requestPreview, out textureId);

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

        if (!requestPreview)
        {
            assetPreviewFrameMisses.Add(key);
            return false;
        }

        using System.Drawing.Bitmap? bitmap = GenerateShaderGraphMaterialPreviewBitmap(data, shaderGraphPath);
        if (bitmap == null)
        {
            lock (previewJobLock)
                previewGenerationFailures[key] = writeTime;
            return false;
        }

        using var previewBitmap = CreatePreviewBitmap(bitmap, 128);
        if (!CreatePreviewTextureFromBitmap(previewBitmap, out int texture))
        {
            lock (previewJobLock)
                previewGenerationFailures[key] = writeTime;
            return false;
        }

        assetPreviewTextures[key] = texture;
        assetPreviewWriteTimes[key] = writeTime;
        assetPreviewNextValidationUtc[key] = now + PreviewValidationDelay();

        lock (previewJobLock)
            previewGenerationFailures.Remove(key);

        textureId = (IntPtr)texture;
        assetPreviewFrameCache[key] = textureId;
        return true;
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

        if (!MaterialAsset.IsMaterialPath(path) || !File.Exists(path))
            return false;

        try
        {
            DateTime materialWriteUtc = File.GetLastWriteTimeUtc(path);
            if (materialPreviewSourceCache.TryGetValue(path, out var existing) &&
                existing.MaterialWriteUtc == materialWriteUtc)
            {
                var now = DateTime.UtcNow;
                if (!existing.HasShaderGraph && existing.NextValidationUtc > now)
                {
                    cached = existing;
                    return true;
                }

                if (existing.HasShaderGraph && File.Exists(existing.ShaderGraphPath))
                {
                    DateTime cachedShaderGraphWriteUtc = File.GetLastWriteTimeUtc(existing.ShaderGraphPath);
                    if (cachedShaderGraphWriteUtc == existing.ShaderGraphWriteUtc && existing.NextValidationUtc > now)
                    {
                        cached = existing;
                        return true;
                    }
                }
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
            materialPreviewSourceCache.Remove(path);
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

private static System.Drawing.Bitmap GenerateMaterialSpherePreviewBitmap(MaterialAssetData data, string albedoPath)
    {
        const int size = 128;
        var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using System.Drawing.Bitmap? albedo = LoadSmallMaterialAlbedo(albedoPath, 256);

        float radius = size * 0.46f;
        float cx = size * 0.50f;
        float cy = size * 0.48f;
        float roughness = Math.Clamp(data.Roughness, 0f, 1f);
        float metallic = Math.Clamp(data.Metallic, 0f, 1f);
        var light = Normalize3(-0.45f, -0.65f, 0.80f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f - cx) / radius;
                float ny = (y + 0.5f - cy) / radius;
                float r2 = nx * nx + ny * ny;
                if (r2 > 1f)
                {
                    bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(0, 0, 0, 0));
                    continue;
                }

                float nz = MathF.Sqrt(MathF.Max(0f, 1f - r2));
                float diffuse = Math.Clamp(nx * light.X + ny * light.Y + nz * light.Z, 0f, 1f);
                float fresnel = MathF.Pow(1f - Math.Clamp(nz, 0f, 1f), 2.2f);
                float spec = MathF.Pow(Math.Clamp(nx * -0.35f + ny * -0.45f + nz * 0.85f, 0f, 1f), 18f + (1f - roughness) * 70f);

                SampleMaterialBase(albedo, nx, ny, nz, out float tr, out float tg, out float tb);
                float baseR = Math.Clamp(data.R * tr, 0f, 1f);
                float baseG = Math.Clamp(data.G * tg, 0f, 1f);
                float baseB = Math.Clamp(data.B * tb, 0f, 1f);

                float shade = 0.58f + diffuse * 0.58f;
                shade += fresnel * (0.16f + metallic * 0.22f);
                float specular = spec * (0.26f + metallic * 0.48f) * (1.10f - roughness * 0.50f);

                int rr = ToByte(baseR * shade + specular + 0.035f);
                int gg = ToByte(baseG * shade + specular + 0.035f);
                int bb = ToByte(baseB * shade + specular + 0.035f);
                bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(255, rr, gg, bb));
            }
        }

        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var rim = new System.Drawing.Pen(System.Drawing.Color.FromArgb(170, 13, 14, 16), 2f);
        g.DrawEllipse(rim, cx - radius, cy - radius, radius * 2f, radius * 2f);
        return bitmap;
    }

private static void SampleMaterialBase(System.Drawing.Bitmap? albedo, float nx, float ny, float nz, out float r, out float g, out float b)
    {
        if (albedo == null)
        {
            r = g = b = 1f;
            return;
        }

        float u = 0.5f + MathF.Atan2(nx, nz) / (MathF.PI * 2f);
        float v = 0.5f - ny * 0.50f;
        u -= MathF.Floor(u);
        v = Math.Clamp(v, 0f, 1f);
        int px = Math.Clamp((int)(u * (albedo.Width - 1)), 0, albedo.Width - 1);
        int py = Math.Clamp((int)(v * (albedo.Height - 1)), 0, albedo.Height - 1);
        var c = albedo.GetPixel(px, py);
        r = c.R / 255f;
        g = c.G / 255f;
        b = c.B / 255f;
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
            catch
            {
                try { File.Delete(previewPath); } catch { }
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
            catch { }

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
            using var source = System.Drawing.Image.FromFile(assetPath);
            using var original = new System.Drawing.Bitmap(source);
            return CreatePreviewBitmap(original, 128);
        }

        if (MaterialAsset.IsMaterialPath(assetPath))
        {
            var data = MaterialAsset.Load(assetPath);
            return GenerateMaterialSpherePreviewBitmap(data, MaterialAsset.GetAlbedo(data));
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
            using var source = new System.Drawing.Bitmap(path);
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

private static void DrawMeshLikePreview(ImDrawListPtr drawList, Vector2 min, float size, string kind)
    {
        var cardMin = min - new Vector2(2f, 2f);
        var cardMax = min + new Vector2(size + 2f, size + 2f);
        drawList.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.09f, 0.10f, 1f)), 5f);
        drawList.AddRectFilled(min, min + new Vector2(size, size), ImGui.GetColorU32(new System.Numerics.Vector4(0.13f, 0.145f, 0.16f, 1f)), 4f);

        if (kind == "SCENE")
        {
            uint grid = ImGui.GetColorU32(new System.Numerics.Vector4(0.30f, 0.36f, 0.42f, 0.82f));
            for (int i = 1; i < 4; i++)
            {
                float t = i / 4f;
                drawList.AddLine(min + new Vector2(size * t, size * 0.22f), min + new Vector2(size * t, size * 0.78f), grid, 1f);
                drawList.AddLine(min + new Vector2(size * 0.18f, size * t), min + new Vector2(size * 0.82f, size * t), grid, 1f);
            }
            DrawIcon(drawList, EditorIcon.Camera, min + new Vector2(size * 0.12f, size * 0.12f), size * 0.25f, ImGui.GetColorU32(new System.Numerics.Vector4(0.70f, 0.86f, 1f, 1f)));
            DrawIcon(drawList, EditorIcon.Light, min + new Vector2(size * 0.62f, size * 0.12f), size * 0.22f, ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.78f, 0.32f, 1f)));
            drawList.AddText(min + new Vector2(4f, size - 12f), ImGui.GetColorU32(new System.Numerics.Vector4(0.92f, 0.96f, 1f, 0.95f)), "SCENE");
            return;
        }

        var c = min + new Vector2(size * 0.5f, size * 0.5f);
        float s = size * 0.32f;
        uint front = ImGui.GetColorU32(kind == "PREF"
            ? new System.Numerics.Vector4(0.34f, 0.56f, 0.92f, 1f)
            : new System.Numerics.Vector4(0.76f, 0.52f, 0.30f, 1f));
        uint top = ImGui.GetColorU32(kind == "PREF"
            ? new System.Numerics.Vector4(0.44f, 0.66f, 1f, 1f)
            : new System.Numerics.Vector4(0.92f, 0.66f, 0.38f, 1f));
        uint side = ImGui.GetColorU32(kind == "PREF"
            ? new System.Numerics.Vector4(0.20f, 0.36f, 0.66f, 1f)
            : new System.Numerics.Vector4(0.52f, 0.34f, 0.20f, 1f));
        uint edge = ImGui.GetColorU32(new System.Numerics.Vector4(0.06f, 0.06f, 0.07f, 1f));
        var p1 = c + new Vector2(-s, -s * 0.45f);
        var p2 = c + new Vector2(0f, -s);
        var p3 = c + new Vector2(s, -s * 0.45f);
        var p4 = c + new Vector2(s, s * 0.55f);
        var p5 = c + new Vector2(0f, s);
        var p6 = c + new Vector2(-s, s * 0.55f);
        drawList.AddQuadFilled(p1, p2, c, p6, top);
        drawList.AddQuadFilled(p2, p3, p4, c, side);
        drawList.AddQuadFilled(c, p4, p5, p6, front);
        drawList.AddLine(p1, p2, edge, 1.4f);
        drawList.AddLine(p2, p3, edge, 1.4f);
        drawList.AddLine(p3, p4, edge, 1.4f);
        drawList.AddLine(p4, p5, edge, 1.4f);
        drawList.AddLine(p5, p6, edge, 1.4f);
        drawList.AddLine(p6, p1, edge, 1.4f);
        drawList.AddLine(p2, c, edge, 1.1f);
        drawList.AddLine(c, p6, edge, 1.1f);
        drawList.AddLine(c, p4, edge, 1.1f);
        drawList.AddText(min + new Vector2(4f, size - 12f), ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.95f, 0.95f, 0.95f)), kind == "PREF" ? "PREF" : "MESH");
    }
}
