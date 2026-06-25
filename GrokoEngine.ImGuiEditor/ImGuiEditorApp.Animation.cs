using GrokoEngine;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    // ── Estado de la ventana de animación (dope sheet tipo Unity) ──
    private bool animRecordMode;
    private float animPixelsPerSecond = 120f;
    private int? animDraggingKeyframe;
    private GameObject? animRecordObj;
    private float[]? animRecordLastPose;

    private const float AnimTrackLabelWidth = 110f;
    private const float AnimRulerHeight = 26f;
    private const float AnimRowHeight = 30f;
    private const float AnimEndPadSeconds = 2f;

    // Dope sheet por-propiedad (estilo Unity): grupos Position / Rotation / Scale.
    private readonly bool[] animGroupShown = { true, false, false };
    private readonly bool[] animGroupExpanded = { true, true, true };
    private static readonly string[] AnimGroupNames = { "Position", "Rotation", "Scale" };
    private const float AnimPropPanelW = 230f;
    private const float AnimPropRowH = 19f;

    private static readonly Vector4 AnimAccent = new(0.29f, 0.565f, 0.851f, 1f); // #4A90D9
    private static readonly Vector4 AnimPlayheadCol = new(0.95f, 0.35f, 0.30f, 1f);
    private static readonly Vector4 AnimRecordCol = new(0.90f, 0.20f, 0.20f, 1f);

    private void DrawAnimationWindow()
    {
        if (!showAnimationWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(820f, 320f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Animation", ref showAnimationWindow, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }
        TrackToolWindowMouse();

        if (selected == null)
        {
            DrawAnimationCentered("Selecciona un objeto para animarlo.", null);
            ImGui.End();
            animRecordMode = false;
            return;
        }

        var animator = selected.GetComponent<Animator>();
        var clip = animator?.GetClip();

        if (animator == null || clip == null)
        {
            // Estado vacío estilo Unity: barra superior atenuada + botón Create centrado.
            DrawAnimationToolbar(animator, null);
            ImGui.Separator();
            DrawAnimationCentered($"To begin animating {selected.Name}, create an Animation Clip.", () => CreateAnimationClipForSelected());
            ImGui.End();
            animRecordMode = false;
            return;
        }

        DrawAnimationToolbar(animator, clip);

        // ── Auto-keyframing en modo Record ──
        HandleAnimationRecording(animator, clip);

        ImGui.Separator();

        if (ImGui.BeginTabBar("##animTabs"))
        {
            if (ImGui.BeginTabItem("Dopesheet"))
            {
                DrawAnimationTimeline(animator, clip);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Curves"))
            {
                DrawAnimationCurves(animator, clip);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    // Texto centrado vertical/horizontalmente, con botón "Create" opcional debajo (estilo Unity).
    private void DrawAnimationCentered(string message, Action? onCreate)
    {
        var avail = ImGui.GetContentRegionAvail();
        float textW = ImGui.CalcTextSize(message).X;
        float cy = ImGui.GetCursorPosY() + avail.Y * 0.42f;
        ImGui.SetCursorPosY(cy);
        ImGui.SetCursorPosX(MathF.Max(8f, (avail.X - textW) * 0.5f));
        ImGui.TextDisabled(message);

        if (onCreate != null)
        {
            float btnW = 90f;
            ImGui.SetCursorPosX(MathF.Max(8f, (avail.X - btnW) * 0.5f));
            ImGui.SetCursorPosY(cy + 24f);
            if (ImGui.Button("Create", new Vector2(btnW, 0f)))
                onCreate();
        }
    }

    // Crea un AnimationClip nuevo: pide ruta con un diálogo nativo, lo guarda,
    // asegura un componente Animator en el objeto y le asigna el clip.
    private void CreateAnimationClipForSelected()
    {
        if (selected == null) return;

        string? path = BrowseSaveAnimationClip(selected.Name);
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            path += ".anim";

        try
        {
            var data = new AnimationClipData { Name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AnimationClipAsset.Save(path, data);

            var animator = selected.GetComponent<Animator>() ?? selected.AddComponent<Animator>();
            var ctrl = animator.GetController();
            if (ctrl != null)
            {
                // Con controller: asigna el clip al estado por defecto (creándolo si hace falta).
                var st = ctrl.GetDefaultState();
                if (st == null)
                {
                    st = new AnimatorStateData { Name = "State" };
                    ctrl.States.Add(st);
                    ctrl.DefaultState = st.Name;
                }
                st.ClipPath = path;
                AnimatorControllerAsset.Save(animator.ControllerPath, ctrl);
            }
            else
            {
                animator.ClipPath = path;
            }
            animator.InvalidateCache();
            statusMessage = "Animation Clip creado: " + System.IO.Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            statusMessage = "No se pudo crear el Animation Clip: " + ex.Message;
        }
    }

    // Diálogo nativo "Guardar como" para el .anim. Devuelve la ruta elegida o null.
    private string? BrowseSaveAnimationClip(string defaultName)
    {
        try
        {
            using var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Title = "Create Animation Clip",
                Filter = "Animation Clip (*.anim)|*.anim",
                DefaultExt = "anim",
                AddExtension = true,
                FileName = (string.IsNullOrWhiteSpace(defaultName) ? "New Animation" : defaultName) + ".anim",
                InitialDirectory = currentProjectDirectory ?? rootAssetsPath
            };
            return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : null;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudo abrir el diálogo de guardado: " + ex.Message);
            return null;
        }
    }

    private float animFps = 24f; // frame rate del editor (estilo Blender)

    private void DrawAnimationToolbar(Animator? animator, AnimationClipData? clip)
    {
        bool has = animator != null && clip != null;

        // ── Fila 1: Preview · Record · transporte · frame ──
        ImGui.TextDisabled("Preview");
        ImGui.SameLine();

        ImGui.BeginDisabled(animator == null);
        // Record (círculo rojo cuando está activo)
        if (animRecordMode)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.25f, 0.25f, 1f));
        if (ImGui.Button(animRecordMode ? "●##rec" : "○##rec"))
        {
            animRecordMode = !animRecordMode;
            if (animRecordMode && animator != null)
            {
                animRecordObj = animator.gameObject;
                animRecordLastPose = CapturePose(animator.gameObject);
                statusMessage = "Animation Record ON — mueve el objeto para crear keyframes";
            }
            else statusMessage = "Animation Record OFF";
        }
        if (animRecordMode)
            ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Record: al mover el objeto se crean keyframes");
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!has);
        // |◄  ir al inicio
        if (ImGui.Button("|◄##first")) { animator!.Time = 0f; if (!isPlaying) animator.Sample(0f); RefreshRecordBaseline(animator); }
        ImGui.SameLine();
        // ◄| keyframe anterior
        if (ImGui.Button("◄|##prevkey")) GoToAdjacentKeyframe(animator!, clip!, -1);
        ImGui.SameLine();
        if (ImGui.Button(animator is { IsPlaying: true } ? "❚❚##play" : "▶##play"))
            animator!.IsPlaying = !animator.IsPlaying;
        ImGui.SameLine();
        // |► keyframe siguiente
        if (ImGui.Button("|►##nextkey")) GoToAdjacentKeyframe(animator!, clip!, 1);
        ImGui.SameLine();
        // ►| ir al final
        if (ImGui.Button("►|##last")) { float len = clip!.Keyframes.Count > 0 ? clip.Keyframes[^1].Time : 0f; animator!.Time = len; if (!isPlaying) animator.Sample(len); RefreshRecordBaseline(animator); }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(54f);
        int frame = has ? (int)MathF.Round(animator!.Time * animFps) : 0;
        if (ImGui.InputInt("##frame", ref frame, 0) && has)
        {
            animator!.Time = MathF.Max(0f, frame / animFps);
            if (!isPlaying) animator.Sample(animator.Time);
            RefreshRecordBaseline(animator);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Frame actual (a " + animFps.ToString("0") + " fps)");
        ImGui.EndDisabled();

        // ── Fila 2: dropdown de clip · keys · loop · zoom ──
        ImGui.BeginDisabled(!has);
        DrawAnimationClipDropdown(animator, clip);

        ImGui.SameLine();
        if (ImGui.Button("◆+##addkey") && has)
            UpsertKeyframe(animator!, clip!, animator!.Time);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add Keyframe: captura la pose actual en el tiempo actual");
        ImGui.SameLine();
        if (ImGui.Button("◆-##delkey") && has)
        {
            var existing = clip!.Keyframes.FirstOrDefault(k => MathF.Abs(k.Time - animator!.Time) < 0.02f);
            if (existing != null) { clip.Keyframes.Remove(existing); AnimationClipAsset.Save(animator!.EffectiveClipPath(), clip); }
        }

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(8f, 0f));
        ImGui.SameLine();
        bool loop = clip?.Loop ?? false;
        if (ImGui.Checkbox("Loop", ref loop) && has)
        {
            clip!.Loop = loop;
            AnimationClipAsset.Save(animator!.EffectiveClipPath(), clip);
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        float pps = animPixelsPerSecond;
        if (ImGui.SliderFloat("Zoom", ref pps, 30f, 400f, "%.0f px/s"))
            animPixelsPerSecond = pps;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(54f);
        int fps = (int)animFps;
        if (ImGui.InputInt("fps", ref fps, 0))
            animFps = Math.Clamp(fps, 1, 240);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Frame rate del editor (Blender usa 24)");
        ImGui.EndDisabled();
    }

    // Dropdown con el clip activo; si hay controller, lista los clips de sus estados.
    private void DrawAnimationClipDropdown(Animator? animator, AnimationClipData? clip)
    {
        string current = "[No Clip]";
        string effective = animator?.EffectiveClipPath() ?? "";
        if (!string.IsNullOrWhiteSpace(effective))
            current = System.IO.Path.GetFileNameWithoutExtension(effective);

        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("##clipsel", current))
        {
            var ctrl = animator?.GetController();
            if (ctrl != null)
            {
                foreach (var st in ctrl.States)
                {
                    if (string.IsNullOrWhiteSpace(st.ClipPath)) continue;
                    string nm = System.IO.Path.GetFileNameWithoutExtension(st.ClipPath);
                    bool sel = string.Equals(animator!.GetActiveState()?.Name, st.Name, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable($"{nm}  ({st.Name})", sel))
                    {
                        animator.SetState(st.Name);
                        animator.Time = 0f;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(effective))
            {
                ImGui.Selectable(current, true);
            }
            ImGui.EndCombo();
        }
    }

    // Salta al keyframe anterior (dir<0) o siguiente (dir>0) respecto al tiempo actual.
    private void GoToAdjacentKeyframe(Animator animator, AnimationClipData clip, int dir)
    {
        if (clip.Keyframes.Count == 0) return;
        float tNow = animator.Time;
        float target = tNow;
        if (dir > 0)
            target = clip.Keyframes.FirstOrDefault(k => k.Time > tNow + 0.0001f)?.Time ?? clip.Keyframes[^1].Time;
        else
            target = clip.Keyframes.LastOrDefault(k => k.Time < tNow - 0.0001f)?.Time ?? clip.Keyframes[0].Time;
        animator.Time = target;
        if (!isPlaying) animator.Sample(target);
        RefreshRecordBaseline(animator);
    }

    private void DrawAnimationTimeline(Animator animator, AnimationClipData clip)
    {
        var go = animator.gameObject;
        float length = clip.Keyframes.Count > 0 ? clip.Keyframes[^1].Time : 0f;
        float totalSeconds = MathF.Max(length + AnimEndPadSeconds, 4f);
        float pps = animPixelsPerSecond;
        float rowH = AnimPropRowH;

        // Filas visibles: (group, axis); axis<0 = cabecera de grupo.
        var rows = new List<(int group, int axis)>();
        for (int g = 0; g < 3; g++)
        {
            if (!animGroupShown[g]) continue;
            rows.Add((g, -1));
            if (animGroupExpanded[g])
                for (int a = 0; a < 3; a++) rows.Add((g, a));
        }

        float GetCh(int g, int a) => g switch
        {
            0 => a == 0 ? go.PosX : a == 1 ? go.PosY : go.PosZ,
            1 => a == 0 ? go.RotX : a == 1 ? go.RotY : go.RotZ,
            _ => a == 0 ? go.ScaleX : a == 1 ? go.ScaleY : go.ScaleZ
        };
        void SetCh(int g, int a, float v)
        {
            switch (g)
            {
                case 0: if (a == 0) go.PosX = v; else if (a == 1) go.PosY = v; else go.PosZ = v; break;
                case 1: if (a == 0) go.RotX = v; else if (a == 1) go.RotY = v; else go.RotZ = v; break;
                default: if (a == 0) go.ScaleX = v; else if (a == 1) go.ScaleY = v; else go.ScaleZ = v; break;
            }
        }

        var avail = ImGui.GetContentRegionAvail();

        // ───────── Panel izquierdo: lista de propiedades ─────────
        ImGui.BeginChild("##animProps", new Vector2(AnimPropPanelW, avail.Y), ImGuiChildFlags.None);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 1f));
        for (int i = 0; i < rows.Count; i++)
        {
            var (g, a) = rows[i];
            float y = AnimRulerHeight + i * rowH;
            if (a < 0)
            {
                ImGui.SetCursorPos(new Vector2(2f, y));
                string arrow = animGroupExpanded[g] ? "v" : ">";
                if (ImGui.Selectable($"{arrow}  {go.Name} : {AnimGroupNames[g]}##hdr{g}", false,
                    ImGuiSelectableFlags.None, new Vector2(AnimPropPanelW - 28f, rowH)))
                    animGroupExpanded[g] = !animGroupExpanded[g];
                ImGui.SetCursorPos(new Vector2(AnimPropPanelW - 22f, y));
                if (ImGui.SmallButton($"◆##gk{g}")) UpsertKeyframe(animator, clip, animator.Time);
            }
            else
            {
                ImGui.SetCursorPos(new Vector2(24f, y));
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{AnimGroupNames[g]}.{"xyz"[a]}");
                ImGui.SetCursorPos(new Vector2(AnimPropPanelW - 92f, y));
                ImGui.SetNextItemWidth(62f);
                float val = GetCh(g, a);
                if (ImGui.InputFloat($"##v{g}{a}", ref val))
                {
                    SetCh(g, a, val);
                    var kfAt = clip.Keyframes.FirstOrDefault(k => MathF.Abs(k.Time - animator.Time) < 0.02f);
                    if (kfAt != null || animRecordMode)
                        UpsertKeyframe(animator, clip, animator.Time);
                }
                ImGui.SetCursorPos(new Vector2(AnimPropPanelW - 22f, y));
                if (ImGui.SmallButton($"◆##k{g}{a}")) UpsertKeyframe(animator, clip, animator.Time);
            }
        }

        ImGui.SetCursorPos(new Vector2(18f, AnimRulerHeight + rows.Count * rowH + 6f));
        if (ImGui.Button("Add Property", new Vector2(AnimPropPanelW - 36f, 0f)))
            ImGui.OpenPopup("##addPropPopup");
        if (ImGui.BeginPopup("##addPropPopup"))
        {
            for (int g = 0; g < 3; g++)
            {
                bool shown = animGroupShown[g];
                if (ImGui.MenuItem(AnimGroupNames[g], "", shown))
                    animGroupShown[g] = !animGroupShown[g];
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();
        ImGui.EndChild();

        ImGui.SameLine(0f, 0f);

        // ───────── Panel derecho: dope sheet ─────────
        ImGui.BeginChild("##animTimeline", new Vector2(avail.X - AnimPropPanelW, avail.Y),
            ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        var origin = ImGui.GetCursorScreenPos();
        float contentW = totalSeconds * pps + 20f;
        float gridH = AnimRulerHeight + rows.Count * rowH;
        ImGui.Dummy(new Vector2(contentW, MathF.Max(gridH, avail.Y)));

        var drawList = ImGui.GetWindowDrawList();
        uint colRuler = ImGui.GetColorU32(new Vector4(0.16f, 0.16f, 0.18f, 1f));
        uint colRowA = ImGui.GetColorU32(new Vector4(0.13f, 0.13f, 0.14f, 1f));
        uint colRowB = ImGui.GetColorU32(new Vector4(0.115f, 0.115f, 0.125f, 1f));
        uint colTick = ImGui.GetColorU32(new Vector4(0.40f, 0.40f, 0.44f, 1f));
        uint colTickMinor = ImGui.GetColorU32(new Vector4(0.24f, 0.24f, 0.27f, 1f));
        uint colText = ImGui.GetColorU32(new Vector4(0.70f, 0.72f, 0.76f, 1f));

        float top = origin.Y;
        float rulerBottom = top + AnimRulerHeight;
        float right = origin.X + contentW;
        float bottom = top + MathF.Max(gridH, avail.Y);

        drawList.AddRectFilled(new Vector2(origin.X, top), new Vector2(right, rulerBottom), colRuler);
        // Bandas por fila
        for (int i = 0; i < rows.Count; i++)
        {
            float ry = rulerBottom + i * rowH;
            drawList.AddRectFilled(new Vector2(origin.X, ry), new Vector2(right, ry + rowH),
                (i % 2 == 0) ? colRowA : colRowB);
        }

        // Ticks de tiempo
        float minorStep = pps >= 200f ? 0.1f : pps >= 100f ? 0.25f : 0.5f;
        float majorStep = pps >= 200f ? 0.5f : 1f;
        for (float t = 0f; t <= totalSeconds + 0.001f; t += minorStep)
        {
            float x = origin.X + t * pps;
            bool major = MathF.Abs(t / majorStep - MathF.Round(t / majorStep)) < 0.001f;
            drawList.AddLine(new Vector2(x, major ? top + 4f : top + 14f), new Vector2(x, bottom),
                major ? colTick : colTickMinor);
            if (major)
                drawList.AddText(new Vector2(x + 3f, top + 3f), colText, $"{t:0.##}s");
        }

        // Diamantes por keyframe en cada fila (todos comparten tiempos: pose completa)
        float diamond = 5.5f;
        int? deleteIndex = null;
        uint colDiamond = ImGui.GetColorU32(AnimAccent);
        uint colDiamondBorder = ImGui.GetColorU32(new Vector4(0.9f, 0.95f, 1f, 1f));

        for (int i = 0; i < clip.Keyframes.Count; i++)
        {
            var kf = clip.Keyframes[i];
            float cx = origin.X + kf.Time * pps;

            for (int r = 0; r < rows.Count; r++)
            {
                float ry = rulerBottom + r * rowH + rowH * 0.5f;
                bool isHeaderRow = rows[r].axis < 0;
                float s = diamond;

                if (isHeaderRow)
                {
                    // Solo las filas de cabecera son interactivas (arrastrar / borrar / seleccionar).
                    ImGui.SetCursorScreenPos(new Vector2(cx - diamond, ry - diamond));
                    ImGui.InvisibleButton($"##kf{i}_{r}", new Vector2(diamond * 2f, diamond * 2f));
                    bool hovered = ImGui.IsItemHovered();
                    if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                    {
                        animDraggingKeyframe = i;
                        kf.Time = MathF.Max(0f, (ImGui.GetIO().MousePos.X - origin.X) / pps);
                    }
                    if (ImGui.IsItemDeactivated() && animDraggingKeyframe == i)
                    {
                        animDraggingKeyframe = null;
                        AnimationClipAsset.Save(animator.EffectiveClipPath(), clip);
                    }
                    if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) deleteIndex = i;
                    if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        animator.Time = kf.Time;
                        if (!isPlaying) { animator.Sample(kf.Time); RefreshRecordBaseline(animator); }
                    }
                    if (hovered) { s = diamond + 1.5f; ImGui.SetTooltip($"Keyframe {kf.Time:F2}s"); }
                }

                var p1 = new Vector2(cx, ry - s);
                var p2 = new Vector2(cx + s, ry);
                var p3 = new Vector2(cx, ry + s);
                var p4 = new Vector2(cx - s, ry);
                drawList.AddQuadFilled(p1, p2, p3, p4, colDiamond);
                drawList.AddQuad(p1, p2, p3, p4, colDiamondBorder, 1f);
            }
        }

        if (deleteIndex.HasValue)
        {
            clip.Keyframes.RemoveAt(deleteIndex.Value);
            AnimationClipAsset.Save(animator.EffectiveClipPath(), clip);
        }

        // Scrubbing en la regla
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##animScrub", new Vector2(contentW, AnimRulerHeight));
        if ((ImGui.IsItemActive() || (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)))
            && animDraggingKeyframe == null)
        {
            float t = MathF.Max(0f, MathF.Min(totalSeconds, (ImGui.GetIO().MousePos.X - origin.X) / pps));
            animator.Time = t;
            if (!isPlaying) { animator.Sample(t); RefreshRecordBaseline(animator); }
        }

        // Playhead (línea blanca de altura completa)
        float headX = origin.X + animator.Time * pps;
        uint colHead = ImGui.GetColorU32(animRecordMode ? AnimRecordCol : AnimPlayheadCol);
        drawList.AddLine(new Vector2(headX, top), new Vector2(headX, bottom), colHead, 1.5f);
        drawList.AddTriangleFilled(
            new Vector2(headX - 5f, top),
            new Vector2(headX + 5f, top),
            new Vector2(headX, top + 7f), colHead);

        ImGui.EndChild();
    }

    // ── Accesores de canal por keyframe (group: 0=Pos,1=Rot,2=Scale; axis: 0/1/2 = x/y/z) ──
    private static float GetKfChannel(AnimationKeyframe kf, int g, int a) => g switch
    {
        0 => a == 0 ? kf.PosX : a == 1 ? kf.PosY : kf.PosZ,
        1 => a == 0 ? kf.RotX : a == 1 ? kf.RotY : kf.RotZ,
        _ => a == 0 ? kf.ScaleX : a == 1 ? kf.ScaleY : kf.ScaleZ
    };
    private static void SetKfChannel(AnimationKeyframe kf, int g, int a, float v)
    {
        switch (g)
        {
            case 0: if (a == 0) kf.PosX = v; else if (a == 1) kf.PosY = v; else kf.PosZ = v; break;
            case 1: if (a == 0) kf.RotX = v; else if (a == 1) kf.RotY = v; else kf.RotZ = v; break;
            default: if (a == 0) kf.ScaleX = v; else if (a == 1) kf.ScaleY = v; else kf.ScaleZ = v; break;
        }
    }
    private static Vector4 AxisColor(int a) => a switch
    {
        0 => new Vector4(0.90f, 0.32f, 0.32f, 1f), // x rojo
        1 => new Vector4(0.40f, 0.85f, 0.35f, 1f), // y verde
        _ => new Vector4(0.40f, 0.55f, 0.95f, 1f)  // z azul
    };

    // Editor de curvas estilo Blender/Unity: cada canal visible se dibuja como curva
    // (valor vs tiempo) con puntos de keyframe arrastrables (vertical=valor, horizontal=tiempo).
    private void DrawAnimationCurves(Animator animator, AnimationClipData clip)
    {
        // Canales visibles (de los grupos mostrados/expandidos del dope sheet).
        var channels = new List<(int g, int a)>();
        for (int g = 0; g < 3; g++)
        {
            if (!animGroupShown[g]) continue;
            if (animGroupExpanded[g])
                for (int a = 0; a < 3; a++) channels.Add((g, a));
        }

        var avail = ImGui.GetContentRegionAvail();

        // ── Panel izquierdo: leyenda de canales ──
        ImGui.BeginChild("##curveLegend", new Vector2(AnimPropPanelW, avail.Y), ImGuiChildFlags.None);
        ImGui.TextDisabled("Curves");
        ImGui.Separator();
        foreach (var (g, a) in channels)
        {
            var col = AxisColor(a);
            ImGui.ColorButton($"##c{g}{a}", col, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoInputs, new Vector2(12f, 12f));
            ImGui.SameLine();
            ImGui.Text($"{AnimGroupNames[g]}.{"xyz"[a]}");
        }
        if (channels.Count == 0)
            ImGui.TextDisabled("Activa propiedades en\nDopesheet → Add Property.");

        // ── Presets de suavizado (movimiento orgánico) ──
        ImGui.Dummy(new Vector2(0f, 6f));
        ImGui.TextDisabled("Suavizado (preset)");
        ImGui.Separator();
        ImGui.TextDisabled($"Actual: {clip.Easing}");

        (string label, AnimationEasing ease)[] presets =
        {
            ("Lineal", AnimationEasing.Linear),
            ("Suave In-Out", AnimationEasing.EaseInOut),
            ("Smooth", AnimationEasing.Smooth),
            ("Ease In", AnimationEasing.EaseIn),
            ("Ease Out", AnimationEasing.EaseOut),
            ("Back ↩", AnimationEasing.Back),
            ("Rebote", AnimationEasing.Bounce),
            ("Elástico", AnimationEasing.Elastic),
        };
        float btnW = (AnimPropPanelW - 24f) * 0.5f;
        for (int i = 0; i < presets.Length; i++)
        {
            bool active = clip.Easing == presets[i].ease;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, AnimAccent);
            if (ImGui.Button(presets[i].label + $"##ez{i}", new Vector2(btnW, 0f)))
            {
                clip.Easing = presets[i].ease;
                AnimationClipAsset.Save(animator.EffectiveClipPath(), clip);
                animator.InvalidateCache();
            }
            if (active) ImGui.PopStyleColor();
            if (i % 2 == 0) ImGui.SameLine();
        }

        ImGui.SetCursorPosY(avail.Y - 28f);
        if (ImGui.Button("Add Property", new Vector2(AnimPropPanelW - 36f, 0f)))
            ImGui.OpenPopup("##addPropPopupCurve");
        if (ImGui.BeginPopup("##addPropPopupCurve"))
        {
            for (int g = 0; g < 3; g++)
            {
                bool shown = animGroupShown[g];
                if (ImGui.MenuItem(AnimGroupNames[g], "", shown)) animGroupShown[g] = !animGroupShown[g];
            }
            ImGui.EndPopup();
        }
        ImGui.EndChild();

        ImGui.SameLine(0f, 0f);

        // ── Panel derecho: gráfico ──
        ImGui.BeginChild("##curveGraph", new Vector2(avail.X - AnimPropPanelW, avail.Y), ImGuiChildFlags.None);
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        float left = origin.X + 44f;            // margen para etiquetas de valor
        float graphW = MathF.Max(40f, size.X - 52f);
        float top = origin.Y + 6f;
        float graphH = MathF.Max(40f, size.Y - 12f);
        float bottom = top + graphH;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.11f, 1f)));

        float length = clip.Keyframes.Count > 0 ? clip.Keyframes[^1].Time : 0f;
        float span = MathF.Max(length, 0.0001f);

        // Rango de valores (auto-fit con margen).
        float minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
        foreach (var kf in clip.Keyframes)
            foreach (var (g, a) in channels)
            {
                float v = GetKfChannel(kf, g, a);
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
        if (float.IsInfinity(minV) || float.IsInfinity(maxV)) { minV = -1f; maxV = 1f; }
        if (MathF.Abs(maxV - minV) < 0.001f) { minV -= 1f; maxV += 1f; }
        float padV = (maxV - minV) * 0.12f;
        minV -= padV; maxV += padV;

        float XOf(float t) => left + (t / span) * graphW;
        float YOf(float v) => top + graphH * (1f - (v - minV) / (maxV - minV));
        float ValOf(float y) => minV + (1f - (y - top) / graphH) * (maxV - minV);
        float TimeOf(float x) => MathF.Max(0f, (x - left) / graphW * span);

        uint colGrid = ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.20f, 1f));
        uint colAxis = ImGui.GetColorU32(new Vector4(0.30f, 0.30f, 0.34f, 1f));
        uint colLbl = ImGui.GetColorU32(new Vector4(0.62f, 0.64f, 0.68f, 1f));

        // Rejilla horizontal (valor) con etiquetas.
        const int hLines = 5;
        for (int i = 0; i <= hLines; i++)
        {
            float v = minV + (maxV - minV) * i / hLines;
            float y = YOf(v);
            drawList.AddLine(new Vector2(left, y), new Vector2(left + graphW, y), MathF.Abs(v) < 1e-4f ? colAxis : colGrid);
            drawList.AddText(new Vector2(origin.X + 4f, y - 7f), colLbl, $"{v:0.##}");
        }
        // Rejilla vertical (tiempo).
        float tStep = span <= 2f ? 0.25f : span <= 6f ? 0.5f : 1f;
        for (float t = 0f; t <= span + 1e-4f; t += tStep)
        {
            float x = XOf(t);
            drawList.AddLine(new Vector2(x, top), new Vector2(x, bottom), colGrid);
            drawList.AddText(new Vector2(x + 2f, bottom + 0f), colLbl, $"{t:0.##}s");
        }

        // Curvas por canal: se muestrea el easing del clip (forma orgánica real del runtime).
        const int curveSteps = 24;
        foreach (var (g, a) in channels)
        {
            uint col = ImGui.GetColorU32(AxisColor(a));
            for (int i = 0; i < clip.Keyframes.Count - 1; i++)
            {
                var k0 = clip.Keyframes[i];
                var k1 = clip.Keyframes[i + 1];
                float v0 = GetKfChannel(k0, g, a);
                float v1 = GetKfChannel(k1, g, a);
                var prev = new Vector2(XOf(k0.Time), YOf(v0));
                for (int s = 1; s <= curveSteps; s++)
                {
                    float lt = s / (float)curveSteps;
                    float et = AnimationEase.Apply(clip.Easing, lt);
                    float tt = k0.Time + (k1.Time - k0.Time) * lt;
                    var cur = new Vector2(XOf(tt), YOf(v0 + (v1 - v0) * et));
                    drawList.AddLine(prev, cur, col, 1.8f);
                    prev = cur;
                }
            }
        }

        // Puntos de keyframe (arrastrables).
        bool changed = false;
        for (int ci = 0; ci < channels.Count; ci++)
        {
            var (g, a) = channels[ci];
            uint col = ImGui.GetColorU32(AxisColor(a));
            for (int i = 0; i < clip.Keyframes.Count; i++)
            {
                var kf = clip.Keyframes[i];
                float px = XOf(kf.Time);
                float py = YOf(GetKfChannel(kf, g, a));
                ImGui.SetCursorScreenPos(new Vector2(px - 5f, py - 5f));
                ImGui.InvisibleButton($"##cv{ci}_{i}", new Vector2(10f, 10f));
                bool hovered = ImGui.IsItemHovered();
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var m = ImGui.GetIO().MousePos;
                    kf.Time = TimeOf(m.X);
                    SetKfChannel(kf, g, a, ValOf(m.Y));
                    animDraggingKeyframe = i;
                    changed = true;
                }
                if (ImGui.IsItemDeactivated() && animDraggingKeyframe == i)
                {
                    animDraggingKeyframe = null;
                    AnimationClipAsset.Save(animator.EffectiveClipPath(), clip);
                }
                if (hovered)
                    ImGui.SetTooltip($"{AnimGroupNames[g]}.{"xyz"[a]}\nt={kf.Time:F2}s  v={GetKfChannel(kf, g, a):F3}");

                float r = hovered ? 5.5f : 4f;
                drawList.AddCircleFilled(new Vector2(px, py), r, col);
                drawList.AddCircle(new Vector2(px, py), r, ImGui.GetColorU32(new Vector4(0.95f, 0.97f, 1f, 1f)), 0, 1.2f);
            }
        }
        if (changed && animDraggingKeyframe == null)
            AnimationClipAsset.Save(animator.EffectiveClipPath(), clip);

        // Scrubbing en la zona del gráfico (clic donde no haya punto).
        ImGui.SetCursorScreenPos(new Vector2(left, top));
        ImGui.InvisibleButton("##curveScrub", new Vector2(graphW, graphH));
        if ((ImGui.IsItemActive() || (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)))
            && animDraggingKeyframe == null)
        {
            float t = TimeOf(ImGui.GetIO().MousePos.X);
            animator.Time = MathF.Min(span, t);
            if (!isPlaying) { animator.Sample(animator.Time); RefreshRecordBaseline(animator); }
        }

        // Playhead.
        float headX = XOf(MathF.Min(animator.Time, span));
        uint colHead = ImGui.GetColorU32(animRecordMode ? AnimRecordCol : AnimPlayheadCol);
        drawList.AddLine(new Vector2(headX, top), new Vector2(headX, bottom), colHead, 1.5f);

        ImGui.EndChild();
    }

    // Actualiza todos los Animator de la escena en Play Mode (ExecuteUpdate solo corre
    // scripts de usuario, no componentes del motor como Animator).
    private void StepAnimators(IEnumerable<GameObject> roots, double dt)
    {
        foreach (var obj in roots)
        {
            var snapshot = new List<GrokoEngine.Component>(obj.Components);
            foreach (var component in snapshot)
            {
                if (component is Animator animator)
                {
                    if (!animator.HasStarted) { animator.Start(); animator.HasStarted = true; }
                    // AnimatePhysics se actualiza en el pase de físicas (StepPhysicsAnimators).
                    if (animator.UpdateMode == AnimatorUpdateMode.AnimatePhysics)
                        continue;
                    // UnscaledTime ignora TimeScale; Normal usa el delta escalado.
                    double useDt = animator.UpdateMode == AnimatorUpdateMode.UnscaledTime
                        ? GrokoEngine.Time.UnscaledDeltaTime
                        : GrokoEngine.Time.DeltaTime;
                    animator.Update(useDt);
                }
            }

            StepAnimators(obj.Children, dt);
        }
    }

    // Pase para los Animator en modo AnimatePhysics: corre junto a las físicas.
    private void StepPhysicsAnimators(IEnumerable<GameObject> roots, double dt)
    {
        foreach (var obj in roots)
        {
            var snapshot = new List<GrokoEngine.Component>(obj.Components);
            foreach (var component in snapshot)
            {
                if (component is Animator animator && animator.UpdateMode == AnimatorUpdateMode.AnimatePhysics)
                {
                    if (!animator.HasStarted) { animator.Start(); animator.HasStarted = true; }
                    animator.Update(dt);
                }
            }

            StepPhysicsAnimators(obj.Children, dt);
        }
    }

    // Avanza la animación del objeto seleccionado en Edit Mode (preview en el editor),
    // ya que Component.Update solo se ejecuta en Play Mode.
    private void StepAnimationPreview(double dt)
    {
        if (isPlaying || !showAnimationWindow)
            return;

        var animator = selected?.GetComponent<Animator>();
        if (animator != null && animator.IsPlaying)
        {
            animator.IsVisible = true;   // el preview ignora el culling (siempre anima)
            animator.Update(dt);
            // La reproducción cambió la pose: refresca la baseline para que Record no grabe falsos keyframes.
            RefreshRecordBaseline(animator);
        }
    }

    // Crea o actualiza el keyframe en 'time' con la pose actual del objeto.
    private void UpsertKeyframe(Animator animator, AnimationClipData clip, float time)
    {
        var go = animator.gameObject;
        var existing = clip.Keyframes.FirstOrDefault(k => MathF.Abs(k.Time - time) < 0.02f);
        var kf = existing ?? new AnimationKeyframe { Time = time };
        kf.PosX = go.PosX; kf.PosY = go.PosY; kf.PosZ = go.PosZ;
        kf.RotX = go.RotX; kf.RotY = go.RotY; kf.RotZ = go.RotZ;
        kf.ScaleX = go.ScaleX; kf.ScaleY = go.ScaleY; kf.ScaleZ = go.ScaleZ;
        if (existing == null)
            clip.Keyframes.Add(kf);
        AnimationClipAsset.Save(animator.EffectiveClipPath(), clip);
    }

    private void HandleAnimationRecording(Animator animator, AnimationClipData clip)
    {
        if (!animRecordMode || isPlaying)
            return;

        var go = animator.gameObject;
        if (animRecordObj != go)
        {
            animRecordObj = go;
            animRecordLastPose = CapturePose(go);
            return;
        }

        var pose = CapturePose(go);
        if (animRecordLastPose != null && PoseDiffers(pose, animRecordLastPose))
        {
            UpsertKeyframe(animator, clip, animator.Time);
            statusMessage = $"Keyframe grabado en {animator.Time:F2}s";
        }
        animRecordLastPose = pose;
    }

    private void RefreshRecordBaseline(Animator animator)
    {
        if (animRecordMode)
        {
            animRecordObj = animator.gameObject;
            animRecordLastPose = CapturePose(animator.gameObject);
        }
    }

    private static float[] CapturePose(GameObject go) => new[]
    {
        go.PosX, go.PosY, go.PosZ,
        go.RotX, go.RotY, go.RotZ,
        go.ScaleX, go.ScaleY, go.ScaleZ
    };

    private static bool PoseDiffers(float[] a, float[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (MathF.Abs(a[i] - b[i]) > 1e-4f)
                return true;
        return false;
    }
}
