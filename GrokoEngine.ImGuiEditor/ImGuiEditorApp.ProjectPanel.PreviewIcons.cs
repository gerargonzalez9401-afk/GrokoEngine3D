using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using System;
using System.IO;
using Vector2 = System.Numerics.Vector2;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
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

    private static void DrawAnimatorControllerAssetPreview(ImDrawListPtr drawList, Vector2 min, float size)
    {
        var max = min + new Vector2(size, size);
        uint bgTop = ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.13f, 0.16f, 1f));
        uint bgBottom = ImGui.GetColorU32(new System.Numerics.Vector4(0.05f, 0.06f, 0.08f, 1f));
        uint glow = ImGui.GetColorU32(new System.Numerics.Vector4(0.20f, 0.66f, 1.00f, 0.24f));
        uint pipeShadow = ImGui.GetColorU32(new System.Numerics.Vector4(0.02f, 0.03f, 0.04f, 0.55f));
        uint pipe = ImGui.GetColorU32(new System.Numerics.Vector4(0.35f, 0.72f, 1.00f, 1f));
        uint pipeCore = ImGui.GetColorU32(new System.Numerics.Vector4(0.80f, 0.94f, 1.00f, 1f));
        uint plate = ImGui.GetColorU32(new System.Numerics.Vector4(0.70f, 0.72f, 0.72f, 1f));
        uint plateDark = ImGui.GetColorU32(new System.Numerics.Vector4(0.38f, 0.40f, 0.42f, 1f));
        uint plateHi = ImGui.GetColorU32(new System.Numerics.Vector4(0.96f, 0.97f, 0.97f, 1f));
        uint blue = ImGui.GetColorU32(new System.Numerics.Vector4(0.26f, 0.70f, 1.00f, 1f));
        uint blueHi = ImGui.GetColorU32(new System.Numerics.Vector4(0.70f, 0.92f, 1.00f, 1f));

        drawList.AddRectFilledMultiColor(min, max, bgTop, bgTop, bgBottom, bgBottom);
        drawList.AddRectFilled(min + new Vector2(size * 0.08f, size * 0.08f), max - new Vector2(size * 0.08f, size * 0.08f), glow, size * 0.16f);

        Vector2 NodeCenter(float x, float y) => min + new Vector2(size * x, size * y);
        float nodeW = size * 0.34f;
        float nodeH = size * 0.25f;
        float r = Math.Max(2f, size * 0.055f);
        var n0 = NodeCenter(0.36f, 0.28f);
        var n1 = NodeCenter(0.68f, 0.52f);
        var n2 = NodeCenter(0.36f, 0.75f);

        DrawPipe(n0 + new Vector2(nodeW * 0.50f, 0f), new Vector2(n1.X, n0.Y), n1 + new Vector2(0f, -nodeH * 0.50f));
        DrawPipe(n2 + new Vector2(nodeW * 0.50f, 0f), new Vector2(n1.X, n2.Y), n1 + new Vector2(0f, nodeH * 0.50f));
        DrawNode(n0);
        DrawNode(n2);
        DrawNode(n1);

        if (size >= 44f)
        {
            uint text = ImGui.GetColorU32(new System.Numerics.Vector4(0.86f, 0.94f, 1.00f, 0.95f));
            drawList.AddText(min + new Vector2(size * 0.10f, size * 0.08f), text, "CTRL");
        }

        void DrawPipe(Vector2 a, Vector2 mid, Vector2 b)
        {
            float thick = Math.Max(2.0f, size * 0.080f);
            float core = Math.Max(1.0f, size * 0.032f);
            drawList.AddLine(a + new Vector2(1f, 1f), mid + new Vector2(1f, 1f), pipeShadow, thick + 1.5f);
            drawList.AddLine(mid + new Vector2(1f, 1f), b + new Vector2(1f, 1f), pipeShadow, thick + 1.5f);
            drawList.AddLine(a, mid, pipe, thick);
            drawList.AddLine(mid, b, pipe, thick);
            drawList.AddLine(a, mid, pipeCore, core);
            drawList.AddLine(mid, b, pipeCore, core);
        }

        void DrawNode(Vector2 center)
        {
            var a = center - new Vector2(nodeW * 0.5f, nodeH * 0.5f);
            var b = center + new Vector2(nodeW * 0.5f, nodeH * 0.5f);
            drawList.AddRectFilled(a + new Vector2(1.5f, 2f), b + new Vector2(1.5f, 2f), ImGui.GetColorU32(new System.Numerics.Vector4(0.02f, 0.02f, 0.025f, 0.55f)), r);
            drawList.AddRectFilled(a, b, plate, r);
            drawList.AddRect(a, b, plateDark, r, ImDrawFlags.None, Math.Max(1f, size * 0.025f));
            drawList.AddRect(a + new Vector2(size * 0.035f), b - new Vector2(size * 0.035f), blue, r * 0.70f, ImDrawFlags.None, Math.Max(1f, size * 0.025f));

            float pad = size * 0.095f;
            var ia = center - new Vector2(pad, pad);
            var ib = center + new Vector2(pad, pad);
            drawList.AddRectFilled(ia, ib, blue, r * 0.50f);
            drawList.AddRect(ia, ib, blueHi, r * 0.50f, ImDrawFlags.None, Math.Max(1f, size * 0.022f));
            drawList.AddLine(a + new Vector2(r, 1f), new Vector2(b.X - r, a.Y + 1f), plateHi, Math.Max(1f, size * 0.018f));
        }
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
        uint paper = ImGui.GetColorU32(new System.Numerics.Vector4(0.90f, 0.91f, 0.90f, 1f));
        uint fold = ImGui.GetColorU32(new System.Numerics.Vector4(0.68f, 0.70f, 0.68f, 1f));
        uint green = ImGui.GetColorU32(new System.Numerics.Vector4(0.13f, 0.52f, 0.20f, 1f));

        Vector2 a = min + new Vector2(size * 0.18f, size * 0.05f);
        Vector2 b = max - new Vector2(size * 0.16f, size * 0.05f);
        float cut = size * 0.22f;

        drawList.AddRectFilled(a + new Vector2(2f, 3f), b + new Vector2(2f, 3f), shadow, 3f);
        drawList.AddRectFilled(a, b, paper, 3f);
        drawList.AddTriangleFilled(new Vector2(b.X - cut, a.Y), new Vector2(b.X, a.Y + cut), new Vector2(b.X - cut, a.Y + cut), fold);

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

    private static void DrawImagePreview(ImDrawListPtr drawList, IntPtr textureId, Vector2 min, float size)
    {
        var max = min + new Vector2(size, size);
        drawList.AddImage(textureId, min, max, new Vector2(0f, 0f), new Vector2(1f, 1f));
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
