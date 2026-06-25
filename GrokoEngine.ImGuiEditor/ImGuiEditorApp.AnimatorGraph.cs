using GrokoEngine;
using ImGuiNET;
using System;
using System.Linq;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    // ── Estado de la ventana del Animator Controller (grafo de estados) ──
    private Vector2 animGraphPan = new(40f, 40f);
    private string animGraphSelectedState = "";
    private string animGraphSelTransFrom = "";
    private int animGraphSelTransIndex = -1;
    private string animGraphMakeTransitionFrom = "";
    private Vector2 animGraphCreatePos;
    private string animGraphNewParamName = "Param";
    private string animGraphNewFloatParamName = "New Float";
    private AnimatorParameterType animGraphNewParamType = AnimatorParameterType.Bool;

    private static readonly Vector2 AnimNodeSize = new(140f, 44f);
    private static readonly Vector2 AnimSpecialNodeSize = new(128f, 30f);
    private static readonly Vector4 AnimNodeDefaultCol = new(0.78f, 0.45f, 0.18f, 1f);  // naranja (default state)
    private static readonly Vector4 AnimNodeCol = new(0.22f, 0.34f, 0.46f, 1f);
    private static readonly Vector4 AnimNodeSelCol = new(0.30f, 0.55f, 0.85f, 1f);
    private const string AnimAnyStateId = "__ANY_STATE__";

    private void DrawAnimatorGraphWindow()
    {
        if (!showAnimatorGraph)
            return;

        ImGui.SetNextWindowSize(new Vector2(900f, 480f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Animator", ref showAnimatorGraph, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }
        TrackToolWindowMouse();

        var animator = selected?.GetComponent<Animator>();
        if (animator == null || string.IsNullOrWhiteSpace(animator.ControllerPath))
        {
            ImGui.TextDisabled(animator == null
                ? "Selecciona un objeto con un componente Animator."
                : "El Animator no tiene un Animator Controller asignado.\nAsígnalo en el Inspector (slot Controller).");
            ImGui.End();
            return;
        }

        var ctrl = animator.GetController();
        if (ctrl == null)
        {
            ImGui.TextDisabled("No se pudo cargar el Animator Controller.");
            ImGui.End();
            return;
        }

        float paramW = 180f;
        float inspectorW = 250f;
        var avail = ImGui.GetContentRegionAvail();

        DrawAnimatorParametersPanel(animator, ctrl, new Vector2(paramW, avail.Y));
        ImGui.SameLine(0f, 4f);
        DrawAnimatorCanvas(animator, ctrl, new Vector2(avail.X - paramW - inspectorW - 8f, avail.Y));
        ImGui.SameLine(0f, 4f);
        DrawAnimatorInspectorPanel(animator, ctrl, new Vector2(inspectorW, avail.Y));

        ImGui.End();
    }

    private void SaveAnimatorController(Animator animator, AnimatorControllerData ctrl)
    {
        AnimatorControllerAsset.Save(animator.ControllerPath, ctrl);
        animator.InvalidateCache();
    }

    // ───────────────────────── Parámetros ─────────────────────────
    private void DrawAnimatorParametersPanel(Animator animator, AnimatorControllerData ctrl, Vector2 size)
    {
        ImGui.BeginChild("##animParams", size, ImGuiChildFlags.None);
        ImGui.TextDisabled("Parameters");
        ImGui.SameLine(size.X - 28f);
        if (ImGui.SmallButton("+##addparam"))
            ImGui.OpenPopup("##addParamPopup");

        if (ImGui.BeginPopup("##addParamPopup"))
        {
            ImGui.SetNextItemWidth(140f);
            ImGui.InputText("##pname", ref animGraphNewParamName, 64);
            int ti = (int)animGraphNewParamType;
            string[] tnames = Enum.GetNames<AnimatorParameterType>();
            ImGui.SetNextItemWidth(140f);
            if (ImGui.Combo("##ptype", ref ti, tnames, tnames.Length))
                animGraphNewParamType = (AnimatorParameterType)ti;
            if (ImGui.Button("Add", new Vector2(140f, 0f)))
            {
                string name = string.IsNullOrWhiteSpace(animGraphNewParamName) ? "Param" : animGraphNewParamName.Trim();
                if (ctrl.Parameters.All(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    ctrl.Parameters.Add(new AnimatorParameter { Name = name, Type = animGraphNewParamType });
                    SaveAnimatorController(animator, ctrl);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        ImGui.Separator();

        AnimatorParameter? toDelete = null;
        foreach (var p in ctrl.Parameters)
        {
            ImGui.PushID(p.Name);
            ImGui.SetNextItemWidth(size.X - 96f);
            float val = animator.GetFloat(p.Name);
            switch (p.Type)
            {
                case AnimatorParameterType.Bool:
                {
                    bool b = val != 0f;
                    if (ImGui.Checkbox("##v", ref b)) animator.SetBool(p.Name, b);
                    break;
                }
                case AnimatorParameterType.Trigger:
                {
                    if (ImGui.RadioButton("##v", val != 0f)) animator.SetTrigger(p.Name);
                    break;
                }
                case AnimatorParameterType.Int:
                {
                    int iv = (int)val;
                    if (ImGui.DragInt("##v", ref iv)) animator.SetInteger(p.Name, iv);
                    break;
                }
                default:
                {
                    float fv = val;
                    if (ImGui.DragFloat("##v", ref fv, 0.05f)) animator.SetFloat(p.Name, fv);
                    break;
                }
            }
            ImGui.SameLine();
            ImGui.Text(p.Name);
            ImGui.SameLine(size.X - 24f);
            if (ImGui.SmallButton("x")) toDelete = p;
            ImGui.TextDisabled($"   {p.Type}");
            ImGui.PopID();
        }

        if (toDelete != null)
        {
            ctrl.Parameters.Remove(toDelete);
            SaveAnimatorController(animator, ctrl);
        }

        ImGui.EndChild();
    }

    // ───────────────────────── Canvas / grafo ─────────────────────────
    private void DrawAnimatorCanvas(Animator animator, AnimatorControllerData ctrl, Vector2 size)
    {
        ImGui.BeginChild("##animCanvas", size, ImGuiChildFlags.None, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        // Fondo + grid
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.11f, 0.11f, 0.12f, 1f)));
        uint grid = ImGui.GetColorU32(new Vector4(0.16f, 0.16f, 0.18f, 1f));
        for (float x = animGraphPan.X % 24f; x < size.X; x += 24f)
            drawList.AddLine(new Vector2(origin.X + x, origin.Y), new Vector2(origin.X + x, origin.Y + size.Y), grid);
        for (float y = animGraphPan.Y % 24f; y < size.Y; y += 24f)
            drawList.AddLine(new Vector2(origin.X, origin.Y + y), new Vector2(origin.X + size.X, origin.Y + y), grid);

        Vector2 NodePos(AnimatorStateData s) => origin + animGraphPan + new Vector2(s.EditorX, s.EditorY);
        Vector2 NodeCenter(AnimatorStateData s) => NodePos(s) + AnimNodeSize * 0.5f;
        Vector2 SpecialPos(float x, float y) => origin + animGraphPan + new Vector2(x, y);
        Vector2 SpecialCenter(float x, float y) => SpecialPos(x, y) + AnimSpecialNodeSize * 0.5f;
        void DrawArrow(Vector2 a, Vector2 b, uint col, float thickness = 2f)
        {
            var dir = Normalize(b - a);
            var perp = new Vector2(-dir.Y, dir.X);
            drawList.AddLine(a, b, col, thickness);
            drawList.AddTriangleFilled(b, b - dir * 12f + perp * 6f, b - dir * 12f - perp * 6f, col);
        }

        // ── Transiciones (flechas) ──
        uint transCol = ImGui.GetColorU32(new Vector4(0.70f, 0.72f, 0.78f, 1f));
        uint transSelCol = ImGui.GetColorU32(new Vector4(0.95f, 0.80f, 0.30f, 1f));
        uint entryCol = ImGui.GetColorU32(new Vector4(0.95f, 0.62f, 0.16f, 0.86f));
        var defaultState = ctrl.GetDefaultState();
        if (defaultState != null)
            DrawArrow(SpecialCenter(-240f, 96f), NodeCenter(defaultState) - new Vector2(AnimNodeSize.X * 0.5f, 0f), entryCol, 2f);

        for (int ti = 0; ti < ctrl.AnyStateTransitions.Count; ti++)
        {
            var t = ctrl.AnyStateTransitions[ti];
            var target = ctrl.FindState(t.ToState);
            if (target == null) continue;
            var a = SpecialCenter(-240f, 28f);
            var b = NodeCenter(target);
            bool selected = animGraphSelTransFrom == AnimAnyStateId && animGraphSelTransIndex == ti;
            uint col = selected ? transSelCol : ImGui.GetColorU32(new Vector4(0.55f, 0.86f, 0.78f, 1f));
            DrawArrow(a, b, col, selected ? 3f : 2f);
            var mid = (a + b) * 0.5f;
            ImGui.SetCursorScreenPos(mid - new Vector2(10f, 10f));
            ImGui.InvisibleButton($"##tr_any_{ti}", new Vector2(20f, 20f));
            if (ImGui.IsItemClicked())
            {
                animGraphSelTransFrom = AnimAnyStateId;
                animGraphSelTransIndex = ti;
                animGraphSelectedState = "";
            }
        }
        DrawAnimatorSpecialNode(drawList, SpecialPos(-240f, 28f), AnimSpecialNodeSize, "Any State", new Vector4(0.28f, 0.62f, 0.56f, 1f), animGraphMakeTransitionFrom == AnimAnyStateId);
        ImGui.SetCursorScreenPos(SpecialPos(-240f, 28f));
        ImGui.InvisibleButton("##animAnyState", AnimSpecialNodeSize);
        if (ImGui.IsItemClicked())
        {
            animGraphSelectedState = "";
            animGraphSelTransIndex = -1;
        }
        if (ImGui.BeginPopupContextItem("##anyStateCtx"))
        {
            if (ImGui.MenuItem("Make Transition"))
                animGraphMakeTransitionFrom = AnimAnyStateId;
            ImGui.EndPopup();
        }

        DrawAnimatorSpecialNode(drawList, SpecialPos(-240f, 96f), AnimSpecialNodeSize, "Entry", new Vector4(0.08f, 0.50f, 0.22f, 1f), false);
        DrawAnimatorSpecialNode(drawList, SpecialPos(500f, 96f), AnimSpecialNodeSize, "Exit", new Vector4(0.72f, 0.12f, 0.12f, 1f), false);

        foreach (var s in ctrl.States)
        {
            for (int ti = 0; ti < s.Transitions.Count; ti++)
            {
                var t = s.Transitions[ti];
                var target = ctrl.FindState(t.ToState);
                if (target == null || target == s) continue;

                var dir = Normalize(NodeCenter(target) - NodeCenter(s));
                var perp = new Vector2(-dir.Y, dir.X);
                // Desplaza la flecha perpendicularmente; como 'perp' invierte su signo al invertir
                // la dirección, A→B y B→A quedan en lados opuestos (paralelas, sin solaparse).
                var off = perp * 6f;
                var a = NodeCenter(s) + off;
                var b = NodeCenter(target) + off;
                bool selected = animGraphSelTransFrom == s.Name && animGraphSelTransIndex == ti;
                uint col = selected ? transSelCol : transCol;
                drawList.AddLine(a, b, col, selected ? 3f : 2f);
                // Flecha en el punto medio (apuntando hacia el destino)
                var mid = (a + b) * 0.5f;
                drawList.AddTriangleFilled(mid + dir * 8f, mid - dir * 6f + perp * 5f, mid - dir * 6f - perp * 5f, col);

                // Botón invisible para seleccionar la transición
                ImGui.SetCursorScreenPos(mid - new Vector2(9f, 9f));
                ImGui.InvisibleButton($"##tr_{s.Name}_{ti}", new Vector2(18f, 18f));
                if (ImGui.IsItemClicked())
                {
                    animGraphSelTransFrom = s.Name;
                    animGraphSelTransIndex = ti;
                    animGraphSelectedState = "";
                }
            }
        }

        // Línea de "creando transición"
        if (!string.IsNullOrWhiteSpace(animGraphMakeTransitionFrom))
        {
            var from = ctrl.FindState(animGraphMakeTransitionFrom);
            if (from != null)
                drawList.AddLine(NodeCenter(from), ImGui.GetIO().MousePos,
                    ImGui.GetColorU32(new Vector4(0.95f, 0.80f, 0.30f, 0.8f)), 2f);
            else if (animGraphMakeTransitionFrom == AnimAnyStateId)
                drawList.AddLine(SpecialCenter(-240f, 28f), ImGui.GetIO().MousePos,
                    ImGui.GetColorU32(new Vector4(0.55f, 0.86f, 0.78f, 0.85f)), 2f);
        }

        // ── Nodos de estado ──
        foreach (var s in ctrl.States)
        {
            var pos = NodePos(s);
            bool isDefault = s.Name == ctrl.DefaultState;
            bool isSel = s.Name == animGraphSelectedState;

            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton($"##node_{s.Name}", AnimNodeSize);
            bool active = ImGui.IsItemActive();
            bool clicked = ImGui.IsItemClicked();

            if (clicked)
            {
                if (!string.IsNullOrWhiteSpace(animGraphMakeTransitionFrom))
                {
                    // Completar transición hacia este nodo
                    if (animGraphMakeTransitionFrom == AnimAnyStateId)
                    {
                        ctrl.AnyStateTransitions.Add(new AnimatorTransition { ToState = s.Name, HasExitTime = false, ExitTime = 0f });
                        SaveAnimatorController(animator, ctrl);
                    }
                    else
                    {
                        var src = ctrl.FindState(animGraphMakeTransitionFrom);
                        if (src != null && src != s)
                        {
                            src.Transitions.Add(new AnimatorTransition { ToState = s.Name, HasExitTime = true, ExitTime = 1f });
                            SaveAnimatorController(animator, ctrl);
                        }
                    }
                    animGraphMakeTransitionFrom = "";
                }
                else
                {
                    animGraphSelectedState = s.Name;
                    animGraphSelTransIndex = -1;
                }
            }

            if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && string.IsNullOrWhiteSpace(animGraphMakeTransitionFrom))
            {
                var d = ImGui.GetIO().MouseDelta;
                s.EditorX += d.X;
                s.EditorY += d.Y;
            }
            if (ImGui.IsItemDeactivated())
                SaveAnimatorController(animator, ctrl);

            // Menú contextual del nodo
            if (ImGui.BeginPopupContextItem($"##nodectx_{s.Name}"))
            {
                if (ImGui.MenuItem("Set as Default State")) { ctrl.DefaultState = s.Name; SaveAnimatorController(animator, ctrl); }
                if (ImGui.MenuItem("Make Transition")) { animGraphMakeTransitionFrom = s.Name; }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete State"))
                {
                    ctrl.States.Remove(s);
                    foreach (var other in ctrl.States)
                        other.Transitions.RemoveAll(t => string.Equals(t.ToState, s.Name, StringComparison.OrdinalIgnoreCase));
                    ctrl.AnyStateTransitions.RemoveAll(t => string.Equals(t.ToState, s.Name, StringComparison.OrdinalIgnoreCase));
                    if (ctrl.DefaultState == s.Name)
                        ctrl.DefaultState = ctrl.States.FirstOrDefault()?.Name ?? "";
                    SaveAnimatorController(animator, ctrl);
                    ImGui.EndPopup();
                    break;
                }
                ImGui.EndPopup();
            }

            Vector4 baseCol = isDefault ? AnimNodeDefaultCol : AnimNodeCol;
            uint fill = ImGui.GetColorU32(baseCol);
            uint border = ImGui.GetColorU32(isSel ? AnimNodeSelCol : new Vector4(0.05f, 0.05f, 0.06f, 1f));
            drawList.AddRectFilled(pos, pos + AnimNodeSize, fill, 5f);
            drawList.AddRect(pos, pos + AnimNodeSize, border, 5f, ImDrawFlags.None, isSel ? 2.5f : 1.5f);
            drawList.AddText(pos + new Vector2(8f, 6f), ImGui.GetColorU32(Vector4.One), s.Name);
            string clipName = s.MotionType == AnimatorMotionType.BlendTree
                ? $"Blend Tree ({s.BlendTree.Children.Count})"
                : string.IsNullOrWhiteSpace(s.ClipPath) ? "(no clip)" : System.IO.Path.GetFileNameWithoutExtension(s.ClipPath);
            drawList.AddText(pos + new Vector2(8f, 24f), ImGui.GetColorU32(new Vector4(0.85f, 0.88f, 0.92f, 0.8f)), clipName);

            // Indicador de estado activo en runtime
            if (string.Equals(animator.GetActiveState()?.Name, s.Name, StringComparison.OrdinalIgnoreCase) && animator.IsPlaying)
                drawList.AddRect(pos - new Vector2(2f, 2f), pos + AnimNodeSize + new Vector2(2f, 2f),
                    ImGui.GetColorU32(new Vector4(0.3f, 0.9f, 0.4f, 1f)), 6f, ImDrawFlags.None, 2f);
        }

        // Interacción de fondo: pan (botón medio), menú crear estado (clic derecho), cancelar make-transition (clic izq vacío)
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##animCanvasBg", size);
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            animGraphPan += ImGui.GetIO().MouseDelta;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            animGraphMakeTransitionFrom = "";
            animGraphSelectedState = "";
            animGraphSelTransIndex = -1;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            animGraphCreatePos = ImGui.GetIO().MousePos - origin - animGraphPan;
            ImGui.OpenPopup("##animCanvasCtx");
        }
        if (ImGui.BeginPopup("##animCanvasCtx"))
        {
            if (ImGui.MenuItem("Create State"))
            {
                string name = UniqueStateName(ctrl, "New State");
                ctrl.States.Add(new AnimatorStateData { Name = name, EditorX = animGraphCreatePos.X, EditorY = animGraphCreatePos.Y });
                if (ctrl.States.Count == 1) ctrl.DefaultState = name;
                SaveAnimatorController(animator, ctrl);
                animGraphSelectedState = name;
            }
            if (ImGui.MenuItem("Create Blend Tree"))
            {
                string name = UniqueStateName(ctrl, "Blend Tree");
                EnsureBlendTreeParameters(ctrl);
                ctrl.States.Add(new AnimatorStateData
                {
                    Name = name,
                    MotionType = AnimatorMotionType.BlendTree,
                    EditorX = animGraphCreatePos.X,
                    EditorY = animGraphCreatePos.Y,
                    BlendTree = new BlendTreeData { Name = name }
                });
                if (ctrl.States.Count == 1) ctrl.DefaultState = name;
                SaveAnimatorController(animator, ctrl);
                animGraphSelectedState = name;
            }
            ImGui.EndPopup();
        }

        ImGui.EndChild();
    }

    // ───────────────────────── Inspector (estado / transición) ─────────────────────────
    private void DrawAnimatorInspectorPanel(Animator animator, AnimatorControllerData ctrl, Vector2 size)
    {
        ImGui.BeginChild("##animInspector", size, ImGuiChildFlags.None);

        if (animGraphSelTransIndex >= 0)
        {
            DrawTransitionInspector(animator, ctrl);
        }
        else
        {
            var s = ctrl.FindState(animGraphSelectedState);
            if (s == null)
            {
                ImGui.TextDisabled("Selecciona un estado o transición.");
                ImGui.TextDisabled("");
                ImGui.TextDisabled("Clic derecho en el lienzo:");
                ImGui.TextDisabled("  Create State");
                ImGui.TextDisabled("Clic derecho en un nodo:");
                ImGui.TextDisabled("  Set as Default / Make");
                ImGui.TextDisabled("  Transition / Delete");
            }
            else
            {
                DrawStateInspector(animator, ctrl, s);
            }
        }

        ImGui.EndChild();
    }

    private void DrawStateInspector(Animator animator, AnimatorControllerData ctrl, AnimatorStateData s)
    {
        ImGui.TextDisabled("State");
        ImGui.Separator();

        string name = s.Name;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##statename", ref name, 64) && !string.IsNullOrWhiteSpace(name) && name != s.Name)
        {
            string old = s.Name;
            string unique = UniqueStateName(ctrl, name.Trim(), s);
            // Reapuntar transiciones y default
            foreach (var st in ctrl.States)
                foreach (var t in st.Transitions)
                    if (string.Equals(t.ToState, old, StringComparison.OrdinalIgnoreCase)) t.ToState = unique;
            foreach (var t in ctrl.AnyStateTransitions)
                if (string.Equals(t.ToState, old, StringComparison.OrdinalIgnoreCase)) t.ToState = unique;
            if (ctrl.DefaultState == old) ctrl.DefaultState = unique;
            s.Name = unique;
            animGraphSelectedState = unique;
            SaveAnimatorController(animator, ctrl);
        }

        ImGui.Dummy(new Vector2(0f, 4f));
        int motionType = (int)s.MotionType;
        if (ImGui.Combo("Motion Type", ref motionType, new[] { "Clip", "Blend Tree" }, 2))
        {
            s.MotionType = (AnimatorMotionType)motionType;
            if (s.MotionType == AnimatorMotionType.BlendTree)
                EnsureBlendTreeParameters(ctrl);
            SaveAnimatorController(animator, ctrl);
        }

        if (s.MotionType == AnimatorMotionType.BlendTree)
        {
            DrawBlendTreeInspector(animator, ctrl, s);
        }
        else
        {
            DrawAssetSlot("Clip", s.ClipPath, "Drop animation clip", path =>
        {
            s.ClipPath = path;
            var defaultState = ctrl.GetDefaultState();
            if (defaultState == null || string.IsNullOrWhiteSpace(defaultState.ClipPath))
                ctrl.DefaultState = s.Name;
            SaveAnimatorController(animator, ctrl);
            }, AnimationClipAsset.IsPlayableAnimationPath);
        }

        DrawFloat("Speed", s.Speed, v => { s.Speed = v; SaveAnimatorController(animator, ctrl); }, 0.05f, -10f, 10f);
        DrawCheckRow("Loop", s.Loop, v => { s.Loop = v; SaveAnimatorController(animator, ctrl); });

        bool isDefault = s.Name == ctrl.DefaultState;
        ImGui.BeginDisabled(isDefault);
        if (ImGui.Button(isDefault ? "Default State" : "Set as Default", new Vector2(-1f, 0f)))
        {
            ctrl.DefaultState = s.Name;
            SaveAnimatorController(animator, ctrl);
        }
        ImGui.EndDisabled();

        if (ImGui.Button("Make Transition", new Vector2(-1f, 0f)))
            animGraphMakeTransitionFrom = s.Name;

        ImGui.Dummy(new Vector2(0f, 6f));
        ImGui.TextDisabled($"Transitions ({s.Transitions.Count})");
        ImGui.Separator();
        for (int i = 0; i < s.Transitions.Count; i++)
        {
            var t = s.Transitions[i];
            ImGui.PushID(i);
            if (ImGui.Selectable($"→ {t.ToState}##tr{i}", animGraphSelTransFrom == s.Name && animGraphSelTransIndex == i))
            {
                animGraphSelTransFrom = s.Name;
                animGraphSelTransIndex = i;
            }
            ImGui.PopID();
        }
    }

    private void DrawBlendTreeInspector(Animator animator, AnimatorControllerData ctrl, AnimatorStateData state)
    {
        var tree = state.BlendTree;
        tree.Normalize();

        string name = tree.Name;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##blendTreeName", ref name, 64))
        {
            tree.Name = string.IsNullOrWhiteSpace(name) ? "Blend Tree" : name.Trim();
            SaveAnimatorController(animator, ctrl);
        }

        int blendType = (int)tree.BlendType;
        if (ImGui.Combo("Blend Type", ref blendType, new[] { "1D", "2D Freeform Directional" }, 2))
        {
            tree.BlendType = (BlendTreeType)blendType;
            SaveAnimatorController(animator, ctrl);
        }

        if (tree.BlendType == BlendTreeType.Simple1D)
            DrawAnimatorParameterCombo("Parameter", animator, ctrl, tree.Parameter, v => { tree.Parameter = v; SaveAnimatorController(animator, ctrl); });
        else
        {
            DrawAnimatorParameterCombo("Parameter X", animator, ctrl, tree.ParameterX, v => { tree.ParameterX = v; SaveAnimatorController(animator, ctrl); });
            DrawAnimatorParameterCombo("Parameter Y", animator, ctrl, tree.ParameterY, v => { tree.ParameterY = v; SaveAnimatorController(animator, ctrl); });
            if (ImGui.Button("2D Locomotion Preset", new Vector2(-1f, 0f)))
            {
                ApplyLocomotionBlendTreePreset(ctrl, tree);
                SaveAnimatorController(animator, ctrl);
            }
            DrawBlendTreeMap(animator, tree);
        }

        ImGui.Dummy(new Vector2(0f, 4f));
        ImGui.TextDisabled("Motions");
        for (int i = 0; i < tree.Children.Count; i++)
        {
            var child = tree.Children[i];
            ImGui.PushID(i);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.16f, 1f));
            ImGui.BeginChild("##blendMotionRow", new Vector2(0f, tree.BlendType == BlendTreeType.Simple1D ? 76f : 96f), ImGuiChildFlags.None);

            ImGui.TextDisabled($"Motion {i}");
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12f);
            if (ImGui.SmallButton("-"))
            {
                tree.Children.RemoveAt(i);
                SaveAnimatorController(animator, ctrl);
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.PopID();
                break;
            }

            DrawAssetSlot("Motion", child.MotionPath, "None (Motion)", path =>
            {
                child.MotionPath = path;
                SaveAnimatorController(animator, ctrl);
            }, AnimationClipAsset.IsPlayableAnimationPath);

            if (tree.BlendType == BlendTreeType.Simple1D)
            {
                float threshold = child.Threshold;
                ImGui.SetNextItemWidth(92f);
                if (ImGui.DragFloat("Threshold", ref threshold, 0.05f))
                {
                    child.Threshold = threshold;
                    child.PosX = threshold;
                    SaveAnimatorController(animator, ctrl);
                }
            }
            else
            {
                float x = child.PosX;
                ImGui.SetNextItemWidth(74f);
                if (ImGui.DragFloat("Pos X", ref x, 0.05f)) { child.PosX = x; SaveAnimatorController(animator, ctrl); }
                ImGui.SameLine();
                float y = child.PosY;
                ImGui.SetNextItemWidth(74f);
                if (ImGui.DragFloat("Pos Y", ref y, 0.05f)) { child.PosY = y; SaveAnimatorController(animator, ctrl); }
            }

            float speed = child.Speed;
            ImGui.SetNextItemWidth(74f);
            if (ImGui.DragFloat("Speed", ref speed, 0.05f, -10f, 10f))
            {
                child.Speed = MathF.Abs(speed) < 0.0001f ? 1f : speed;
                SaveAnimatorController(animator, ctrl);
            }
            ImGui.SameLine();
            bool mirror = child.Mirror;
            if (ImGui.Checkbox("Mirror", ref mirror)) { child.Mirror = mirror; SaveAnimatorController(animator, ctrl); }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopID();
        }

        if (ImGui.Button("+ Add Motion", new Vector2(-1f, 0f)))
        {
            int count = tree.Children.Count;
            var pos = GetDefaultBlendTreePosition(count);
            tree.Children.Add(new BlendTreeChildMotion
            {
                Threshold = count,
                PosX = tree.BlendType == BlendTreeType.Simple1D ? count : pos.X,
                PosY = tree.BlendType == BlendTreeType.Simple1D ? 0f : pos.Y
            });
            SaveAnimatorController(animator, ctrl);
        }
    }

    private static Vector2 GetDefaultBlendTreePosition(int index)
    {
        return index switch
        {
            0 => new Vector2(0f, 0f),   // Idle / centro
            1 => new Vector2(0f, 1f),   // Forward
            2 => new Vector2(1f, 0f),   // Right
            3 => new Vector2(-1f, 0f),  // Left
            4 => new Vector2(0f, -1f),  // Back
            _ => GetRadialBlendTreePosition(index - 5)
        };
    }

    private static Vector2 GetRadialBlendTreePosition(int extraIndex)
    {
        const float radius = 1f;
        float angle = extraIndex * (MathF.PI * 2f / 8f) + MathF.PI * 0.25f;
        return new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
    }

    private static void ApplyLocomotionBlendTreePreset(AnimatorControllerData ctrl, BlendTreeData tree)
    {
        EnsureFloatParameter(ctrl, "VelX");
        EnsureFloatParameter(ctrl, "VelY");
        tree.BlendType = BlendTreeType.FreeformDirectional2D;
        tree.ParameterX = "VelX";
        tree.ParameterY = "VelY";

        for (int i = 0; i < tree.Children.Count; i++)
        {
            var pos = GetLocomotionBlendTreePosition(tree.Children[i], i);
            tree.Children[i].PosX = pos.X;
            tree.Children[i].PosY = pos.Y;
        }
    }

    private static Vector2 GetLocomotionBlendTreePosition(BlendTreeChildMotion child, int fallbackIndex)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(child.MotionPath ?? "");
        name = name.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();

        if (name.Contains("idle") || name.Contains("stand"))
            return new Vector2(0f, 0f);
        if (name.Contains("back") || name.Contains("backward") || name.Contains("reverse"))
            return new Vector2(0f, -1f);
        if (name.Contains("left"))
            return new Vector2(-1f, 0f);
        if (name.Contains("right"))
            return new Vector2(1f, 0f);
        if (name.Contains("forward") || name.Contains("forwards") || name.Contains("fwd") ||
            name.Contains("walk") || name.Contains("run") || name.Contains("jog"))
            return new Vector2(0f, 1f);

        return GetDefaultBlendTreePosition(fallbackIndex);
    }

    private void DrawBlendTreeMap(Animator animator, BlendTreeData tree)
    {
        Vector2 size = new(Math.Max(120f, ImGui.GetContentRegionAvail().X), 150f);
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 max = min + size;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.13f, 1f)), 3f);
        uint grid = ImGui.GetColorU32(new Vector4(0.20f, 0.20f, 0.22f, 1f));
        for (int i = 1; i < 4; i++)
        {
            float x = min.X + size.X * i / 4f;
            float y = min.Y + size.Y * i / 4f;
            draw.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), grid);
            draw.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), grid);
        }

        Vector2 ToScreen(float x, float y)
        {
            float sx = min.X + size.X * (0.5f + x * 0.25f);
            float sy = min.Y + size.Y * (0.5f - y * 0.25f);
            return new Vector2(Math.Clamp(sx, min.X + 6f, max.X - 6f), Math.Clamp(sy, min.Y + 6f, max.Y - 6f));
        }

        foreach (var child in tree.Children)
        {
            var p = ToScreen(child.PosX, child.PosY);
            draw.AddCircleFilled(p, 4f, ImGui.GetColorU32(new Vector4(0.78f, 0.80f, 0.84f, 1f)));
        }
        var value = ToScreen(animator.GetFloat(tree.ParameterX), animator.GetFloat(tree.ParameterY));
        draw.AddCircleFilled(value, 5f, ImGui.GetColorU32(new Vector4(1f, 0.35f, 0.35f, 1f)));
        draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.30f, 0.32f, 0.35f, 1f)), 3f);
        ImGui.Dummy(size);
    }

    private void DrawAnimatorParameterCombo(string label, Animator animator, AnimatorControllerData ctrl, string current, Action<string> set)
    {
        var names = ctrl.Parameters
            .Where(p => p.Type is AnimatorParameterType.Float or AnimatorParameterType.Int)
            .Select(p => p.Name)
            .DefaultIfEmpty("")
            .ToArray();
        int index = Math.Max(0, Array.FindIndex(names, n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase)));
        float fullWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(Math.Max(80f, fullWidth - 62f));
        if (ImGui.Combo(label, ref index, names, names.Length) && index >= 0)
            set(names[index]);
        ImGui.SameLine();
        if (ImGui.SmallButton($"+ Float##{label}"))
        {
            animGraphNewFloatParamName = MakeUniqueParameterName(ctrl, string.IsNullOrWhiteSpace(label) ? "Blend" : label.Replace("Parameter", "").Trim());
            ImGui.OpenPopup($"##newFloatParam{label}");
        }
        if (ImGui.BeginPopup($"##newFloatParam{label}"))
        {
            ImGui.TextDisabled("New Float Parameter");
            ImGui.SetNextItemWidth(170f);
            ImGui.InputText("##name", ref animGraphNewFloatParamName, 64);
            if (ImGui.Button("Create", new Vector2(170f, 0f)))
            {
                string name = MakeUniqueParameterName(ctrl, animGraphNewFloatParamName);
                ctrl.Parameters.Add(new AnimatorParameter { Name = name, Type = AnimatorParameterType.Float });
                set(name);
                SaveAnimatorController(animator, ctrl);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private static string MakeUniqueParameterName(AnimatorControllerData ctrl, string baseName)
    {
        string root = string.IsNullOrWhiteSpace(baseName) ? "New Float" : baseName.Trim();
        string name = root;
        int i = 1;
        while (ctrl.Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{root} {i++}";
        return name;
    }

    private static void EnsureBlendTreeParameters(AnimatorControllerData ctrl)
    {
        if (ctrl.Parameters.All(p => !string.Equals(p.Name, "Blend", StringComparison.OrdinalIgnoreCase)))
            ctrl.Parameters.Add(new AnimatorParameter { Name = "Blend", Type = AnimatorParameterType.Float });
        EnsureFloatParameter(ctrl, "VelX");
        EnsureFloatParameter(ctrl, "VelY");
    }

    private static void EnsureFloatParameter(AnimatorControllerData ctrl, string name)
    {
        if (ctrl.Parameters.All(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            ctrl.Parameters.Add(new AnimatorParameter { Name = name, Type = AnimatorParameterType.Float });
    }

    private void DrawTransitionInspector(Animator animator, AnimatorControllerData ctrl)
    {
        bool fromAnyState = animGraphSelTransFrom == AnimAnyStateId;
        var from = fromAnyState ? null : ctrl.FindState(animGraphSelTransFrom);
        var transitions = fromAnyState ? ctrl.AnyStateTransitions : from?.Transitions;
        string fromName = fromAnyState ? "Any State" : from?.Name ?? "";
        if (transitions == null || animGraphSelTransIndex < 0 || animGraphSelTransIndex >= transitions.Count)
        {
            animGraphSelTransIndex = -1;
            ImGui.TextDisabled("Transición no válida.");
            return;
        }

        var t = transitions[animGraphSelTransIndex];
        string title = $"{fromName} -> {t.ToState}";

        // ── Cabecera (estilo Unity) ──
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.17f, 0.17f, 0.19f, 1f));
        ImGui.BeginChild("##trHeader", new Vector2(0f, 38f), ImGuiChildFlags.None);
        ImGui.SetCursorPos(new Vector2(8f, 5f));
        ImGui.TextColored(new Vector4(0.45f, 0.78f, 0.95f, 1f), "↘ " + title);
        ImGui.SetCursorPos(new Vector2(8f, 21f));
        ImGui.TextDisabled("1 AnimatorTransitionBase");
        ImGui.EndChild();
        ImGui.PopStyleColor();

        // ── Lista de transiciones del grupo (mismo origen→destino) con Solo/Mute ──
        var group = transitions
            .Select((tr, idx) => (tr, idx))
            .Where(x => string.Equals(x.tr.ToState, t.ToState, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ImGui.Dummy(new Vector2(0f, 2f));
        float fullW = ImGui.GetContentRegionAvail().X;
        ImGui.TextDisabled("Transitions");
        ImGui.SameLine(fullW - 76f); ImGui.TextDisabled("Solo");
        ImGui.SameLine(fullW - 40f); ImGui.TextDisabled("Mute");

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.13f, 0.13f, 0.14f, 1f));
        ImGui.BeginChild("##trList", new Vector2(0f, Math.Min(96f, 24f + group.Count * 22f)), ImGuiChildFlags.None);
        foreach (var (tr, idx) in group)
        {
            ImGui.PushID(idx);
            bool sel = idx == animGraphSelTransIndex;
            if (ImGui.Selectable($"{fromName} -> {tr.ToState}##row", sel, ImGuiSelectableFlags.None, new Vector2(fullW - 90f, 0f)))
                animGraphSelTransIndex = idx;
            ImGui.SameLine(fullW - 78f);
            bool solo = tr.Solo;
            if (ImGui.Checkbox("##solo", ref solo)) { tr.Solo = solo; SaveAnimatorController(animator, ctrl); }
            ImGui.SameLine(fullW - 42f);
            bool mute = tr.Mute;
            if (ImGui.Checkbox("##mute", ref mute)) { tr.Mute = mute; SaveAnimatorController(animator, ctrl); }
            ImGui.PopID();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        // refrescar t por si cambió la selección
        if (animGraphSelTransIndex >= transitions.Count) { animGraphSelTransIndex = -1; return; }
        t = transitions[animGraphSelTransIndex];

        // ── Sub-cabecera con el nombre de la transición ──
        ImGui.Dummy(new Vector2(0f, 2f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.17f, 0.17f, 0.19f, 1f));
        ImGui.BeginChild("##trName", new Vector2(0f, 24f), ImGuiChildFlags.None);
        ImGui.SetCursorPos(new Vector2(8f, 4f));
        ImGui.Text(title);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        // ── Has Exit Time + Settings ──
        DrawCheckRow("Has Exit Time", t.HasExitTime, v => { t.HasExitTime = v; SaveAnimatorController(animator, ctrl); });

        if (ImGui.TreeNodeEx("Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawFloat("Exit Time", t.ExitTime, v => { t.ExitTime = Math.Clamp(v, 0f, 1f); SaveAnimatorController(animator, ctrl); }, 0.01f, 0f, 1f);
            DrawCheckRow("Fixed Duration", t.FixedDuration, v => { t.FixedDuration = v; SaveAnimatorController(animator, ctrl); });
            DrawFloat("Transition Duration", t.TransitionDuration, v => { t.TransitionDuration = Math.Max(0f, v); SaveAnimatorController(animator, ctrl); }, 0.01f, 0f, 100f);
            DrawFloat("Transition Offset", t.TransitionOffset, v => { t.TransitionOffset = Math.Clamp(v, 0f, 1f); SaveAnimatorController(animator, ctrl); }, 0.01f, 0f, 1f);
            DrawEnumCombo("Interruption Source", t.Interruption, v => { t.Interruption = v; SaveAnimatorController(animator, ctrl); });
            ImGui.BeginDisabled(t.Interruption == AnimatorInterruptionSource.None);
            DrawCheckRow("Ordered Interruption", t.OrderedInterruption, v => { t.OrderedInterruption = v; SaveAnimatorController(animator, ctrl); });
            ImGui.EndDisabled();
            ImGui.TreePop();
        }

        // ── Aviso de preview (no tenemos preview real) ──
        var dstState = ctrl.FindState(t.ToState);
        bool canPreview = (from != null && !string.IsNullOrWhiteSpace(from.ClipPath)) ||
                          (dstState != null && !string.IsNullOrWhiteSpace(dstState.ClipPath));
        if (!canPreview)
        {
            ImGui.Dummy(new Vector2(0f, 2f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.20f, 0.18f, 0.10f, 1f));
            ImGui.BeginChild("##trWarn", new Vector2(0f, 40f), ImGuiChildFlags.None);
            ImGui.SetCursorPos(new Vector2(6f, 5f));
            ImGui.TextColored(new Vector4(0.95f, 0.82f, 0.30f, 1f), "⚠");
            ImGui.SameLine();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled("Cannot preview transition, must at least have a motion on either source or destination state");
            ImGui.PopTextWrapPos();
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        // ── Conditions ──
        ImGui.Dummy(new Vector2(0f, 4f));
        ImGui.TextDisabled("Conditions");
        ImGui.Separator();
        DrawTransitionConditions(animator, ctrl, t);

        ImGui.Dummy(new Vector2(0f, 8f));
        if (ImGui.Button("Delete Transition", new Vector2(-1f, 0f)))
        {
            transitions.RemoveAt(animGraphSelTransIndex);
            animGraphSelTransIndex = -1;
            SaveAnimatorController(animator, ctrl);
        }
    }

    // Filas de condición adaptadas al tipo del parámetro (como Unity):
    // Bool → true/false; Trigger → solo el parámetro; Float → Greater/Less + valor; Int → Greater/Less/Equals/NotEquals + valor.
    private void DrawTransitionConditions(Animator animator, AnimatorControllerData ctrl, AnimatorTransition t)
    {
        string[] paramNames = ctrl.Parameters.Select(p => p.Name).ToArray();
        if (paramNames.Length == 0)
        {
            ImGui.TextDisabled("No parameters. Add a parameter to create conditions.");
            return;
        }

        AnimatorParameterType TypeOf(string name) =>
            ctrl.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Type
            ?? AnimatorParameterType.Float;

        for (int i = 0; i < t.Conditions.Count; i++)
        {
            var c = t.Conditions[i];
            ImGui.PushID(i);
            float rowW = ImGui.GetContentRegionAvail().X;

            int pi = Array.FindIndex(paramNames, n => n == c.Parameter);
            ImGui.SetNextItemWidth(rowW * 0.5f);
            if (ImGui.Combo("##cp", ref pi, paramNames, paramNames.Length) && pi >= 0)
            {
                c.Parameter = paramNames[pi];
                SaveAnimatorController(animator, ctrl);
            }

            var ptype = TypeOf(c.Parameter);
            ImGui.SameLine();
            switch (ptype)
            {
                case AnimatorParameterType.Bool:
                {
                    int bv = c.Mode == AnimatorConditionMode.IfNot ? 1 : 0; // 0=true,1=false
                    ImGui.SetNextItemWidth(rowW * 0.42f);
                    if (ImGui.Combo("##cbool", ref bv, new[] { "true", "false" }, 2))
                    {
                        c.Mode = bv == 0 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                        SaveAnimatorController(animator, ctrl);
                    }
                    break;
                }
                case AnimatorParameterType.Trigger:
                {
                    c.Mode = AnimatorConditionMode.If;
                    ImGui.TextDisabled("(trigger)");
                    break;
                }
                case AnimatorParameterType.Int:
                {
                    string[] modes = { "Greater", "Less", "Equals", "NotEquals" };
                    AnimatorConditionMode[] map = { AnimatorConditionMode.Greater, AnimatorConditionMode.Less, AnimatorConditionMode.Equals, AnimatorConditionMode.NotEquals };
                    int mi = Math.Max(0, Array.IndexOf(map, c.Mode));
                    ImGui.SetNextItemWidth(rowW * 0.28f);
                    if (ImGui.Combo("##cm", ref mi, modes, modes.Length)) { c.Mode = map[mi]; SaveAnimatorController(animator, ctrl); }
                    ImGui.SameLine();
                    int iv = (int)c.Threshold;
                    ImGui.SetNextItemWidth(rowW * 0.16f);
                    if (ImGui.DragInt("##ct", ref iv)) { c.Threshold = iv; SaveAnimatorController(animator, ctrl); }
                    break;
                }
                default: // Float
                {
                    string[] modes = { "Greater", "Less" };
                    AnimatorConditionMode[] map = { AnimatorConditionMode.Greater, AnimatorConditionMode.Less };
                    int mi = c.Mode == AnimatorConditionMode.Less ? 1 : 0;
                    ImGui.SetNextItemWidth(rowW * 0.28f);
                    if (ImGui.Combo("##cm", ref mi, modes, modes.Length)) { c.Mode = map[mi]; SaveAnimatorController(animator, ctrl); }
                    ImGui.SameLine();
                    float fv = c.Threshold;
                    ImGui.SetNextItemWidth(rowW * 0.16f);
                    if (ImGui.DragFloat("##ct", ref fv, 0.1f)) { c.Threshold = fv; SaveAnimatorController(animator, ctrl); }
                    break;
                }
            }
            ImGui.PopID();
        }

        // Botones +/- alineados a la derecha (como Unity)
        float w = ImGui.GetContentRegionAvail().X;
        ImGui.Dummy(new Vector2(0f, 2f));
        ImGui.SameLine(w - 52f);
        if (ImGui.Button("+##addcond", new Vector2(24f, 0f)))
        {
            t.Conditions.Add(new AnimatorCondition
            {
                Parameter = ctrl.Parameters.FirstOrDefault()?.Name ?? "",
                Mode = AnimatorConditionMode.If
            });
            SaveAnimatorController(animator, ctrl);
        }
        ImGui.SameLine();
        if (ImGui.Button("-##delcond", new Vector2(24f, 0f)) && t.Conditions.Count > 0)
        {
            t.Conditions.RemoveAt(t.Conditions.Count - 1);
            SaveAnimatorController(animator, ctrl);
        }
    }

    private static string UniqueStateName(AnimatorControllerData ctrl, string baseName, AnimatorStateData? ignore = null)
    {
        string name = baseName;
        int i = 1;
        while (ctrl.States.Any(s => s != ignore && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {i++}";
        return name;
    }

    private static void DrawAnimatorSpecialNode(ImDrawListPtr drawList, Vector2 pos, Vector2 size, string label, Vector4 color, bool selected)
    {
        var top = new Vector4(
            Math.Min(1f, color.X + 0.10f),
            Math.Min(1f, color.Y + 0.10f),
            Math.Min(1f, color.Z + 0.10f),
            color.W);
        var bottom = new Vector4(color.X * 0.70f, color.Y * 0.70f, color.Z * 0.70f, color.W);
        drawList.AddRectFilledMultiColor(
            pos,
            pos + size,
            ImGui.GetColorU32(top),
            ImGui.GetColorU32(top),
            ImGui.GetColorU32(bottom),
            ImGui.GetColorU32(bottom));
        drawList.AddRect(pos, pos + size,
            ImGui.GetColorU32(selected ? new Vector4(0.95f, 0.88f, 0.44f, 1f) : new Vector4(0.05f, 0.05f, 0.06f, 1f)),
            4f, ImDrawFlags.None, selected ? 2f : 1.2f);

        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText(pos + (size - textSize) * 0.5f, ImGui.GetColorU32(new Vector4(0.94f, 0.96f, 0.97f, 1f)), label);
    }

    private static Vector2 Normalize(Vector2 v)
    {
        float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return len > 1e-5f ? v / len : new Vector2(1f, 0f);
    }
}
