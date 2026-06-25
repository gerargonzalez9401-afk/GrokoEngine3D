using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using GrokoEngine;
using NumVector2 = System.Numerics.Vector2;
using NumVector4 = System.Numerics.Vector4;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using TkVector3 = OpenTK.Mathematics.Vector3;
using TkVector4 = OpenTK.Mathematics.Vector4;
using TkMatrix4 = OpenTK.Mathematics.Matrix4;
using TkMathHelper = OpenTK.Mathematics.MathHelper;
using CoreVector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private readonly Dictionary<string, (int Tex, int W, int H)> uiTextureCache = new(StringComparer.OrdinalIgnoreCase);

    private UIElement? hoveredUiGraphic;
    private UIElement? pressedUiGraphic;
    private UISelectable? pressedUiSelectable;
    private bool previousUiLeftMouseDown;
    private EditorCameraState activeUiCameraState = new();

    // Unity-style Rect Tool for UI: move from the body, resize from edges/corners.
    // 0 none, 1 move, 2 left, 3 right, 4 top, 5 bottom, 6 top-left, 7 top-right, 8 bottom-left, 9 bottom-right.
    private GameObject? activeUiRectToolObject;
    private int activeUiRectToolHandle;
    private NumVector2 activeUiRectToolStartMouse;
    private float activeUiStartPosX, activeUiStartPosY, activeUiStartWidth, activeUiStartHeight;
    private float activeUiStartPivotX, activeUiStartPivotY;

    private readonly List<Canvas> uiCanvasScratch = new();
    private readonly List<(Canvas Canvas, UiRect Rect, float Scale)> uiCanvasDataScratch = new();
    private readonly List<UiHitCandidate> uiHitScratch = new();

    // El layout de UI calcula posiciones/tamaños escribiendo TEMPORALMENTE en los campos del
    // componente (anchors/pivot/pos/size) para reutilizar ResolveElementRect. Para no corromper
    // los valores que autoriza el usuario (ni guardarlos con pérdida), se snapshotean antes del
    // layout y se restauran al final del frame. Así la mutación es puramente transitoria.
    private readonly List<(UIElement El, UiLayoutSnapshot Snap)> uiLayoutSnapshots = new();
    private readonly List<UIElement> layoutChildrenScratch = new();

    private readonly struct UiLayoutSnapshot
    {
        public readonly float PosX, PosY, Width, Height;
        public readonly float AnchorMinX, AnchorMinY, AnchorMaxX, AnchorMaxY;
        public readonly float PivotX, PivotY;

        public UiLayoutSnapshot(UIElement e)
        {
            PosX = e.PosX; PosY = e.PosY; Width = e.Width; Height = e.Height;
            AnchorMinX = e.AnchorMinX; AnchorMinY = e.AnchorMinY;
            AnchorMaxX = e.AnchorMaxX; AnchorMaxY = e.AnchorMaxY;
            PivotX = e.PivotX; PivotY = e.PivotY;
        }
    }

    private readonly struct UiRect
    {
        public readonly NumVector2 Min;
        public readonly NumVector2 Max;

        public UiRect(NumVector2 min, NumVector2 max)
        {
            Min = min;
            Max = max;
        }

        public float Width => MathF.Max(0f, Max.X - Min.X);
        public float Height => MathF.Max(0f, Max.Y - Min.Y);
        public NumVector2 Center => new((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f);
        public NumVector2 Size => new(Width, Height);
        public bool IsValid => Width > 0.5f && Height > 0.5f;
    }

    private readonly struct UiHitCandidate
    {
        public readonly UIElement Element;
        public readonly UiRect Rect;
        public readonly UiRect ClipRect;
        public readonly Canvas Canvas;

        public UiHitCandidate(UIElement element, UiRect rect, UiRect clipRect, Canvas canvas)
        {
            Element = element;
            Rect = rect;
            ClipRect = clipRect;
            Canvas = canvas;
        }
    }

    private void RenderCanvasUI(ImDrawListPtr dl, NumVector2 origin, NumVector2 size)
    {
        try
        {
            if (size.X < 1f || size.Y < 1f)
                return;

            uiCanvasScratch.Clear();
            foreach (var root in objects)
                CollectCanvases(root, uiCanvasScratch);
            if (uiCanvasScratch.Count == 0)
            {
                ResetUiRaycastState(null);
                return;
            }

            uiCanvasScratch.Sort((a, b) =>
            {
                int layer = a.OrderInLayer.CompareTo(b.OrderInLayer);
                if (layer != 0) return layer;
                return a.SortOrder.CompareTo(b.SortOrder);
            });

            uiCanvasDataScratch.Clear();
            foreach (var canvas in uiCanvasScratch)
            {
                if (!canvas.Enabled || canvas.gameObject == null || !canvas.gameObject.IsActive)
                    continue;

                bool sceneOnlyGizmo = !isPlaying && !gameMode && !gameViewActive && canvas.EditorPreviewMode == 2;
                bool gameOnlyHidden = !isPlaying && !gameMode && !gameViewActive && canvas.EditorPreviewMode == 1;
                if (gameOnlyHidden)
                    continue;

                float scale = ComputeCanvasScale(canvas, size);
                var canvasRect = ResolveCanvasRect(canvas, origin, size, scale, canvas.PixelPerfect);
                if (!canvasRect.IsValid && canvas.HideWhenBehindCamera)
                    continue;

                SnapshotUiLayoutRecursive(canvas.gameObject);
                ApplyUILayouts(canvas.gameObject, canvasRect, scale, canvas.PixelPerfect);
                uiCanvasDataScratch.Add((canvas, canvasRect, scale));
            }

            bool runtimeUiInteraction = isPlaying || gameMode || gameViewActive;
            if (runtimeUiInteraction)
                ProcessGraphicRaycaster(uiCanvasDataScratch);
            else
                ResetUiRaycastState(null);

            HandleSelectedUiRectTool(uiCanvasDataScratch);

            bool drawEditorGizmos = !isPlaying && !gameMode && !gameViewActive;

            foreach (var item in uiCanvasDataScratch)
            {
                var canvas = item.Canvas;
                var canvasRect = item.Rect;
                float scale = item.Scale;
                float canvasAlpha = Math.Clamp(canvas.Alpha, 0f, 1f);
                bool sceneGizmoOnly = drawEditorGizmos && canvas.EditorPreviewMode == 2;

                if (canvas.ClipToCanvas)
                    dl.PushClipRect(canvasRect.Min, canvasRect.Max, true);

                if (!sceneGizmoOnly)
                    DrawUINode(dl, canvas.gameObject, canvasRect, canvasRect, scale, canvas.PixelPerfect, canvasAlpha, drawEditorGizmos && canvas.ShowGizmos);
                else
                    DrawUINodeGizmosOnly(dl, canvas.gameObject, canvasRect, canvasRect, scale, canvas.PixelPerfect);

                if (canvas.ClipToCanvas)
                    dl.PopClipRect();

                if (drawEditorGizmos && canvas.ShowGizmos)
                    DrawCanvasGizmo(dl, canvas, canvasRect);
            }
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("RenderCanvasUI: " + ex.Message);
        }
        finally
        {
            // Restaura los campos que el layout mutó de forma transitoria: el estado persistente
            // del usuario queda intacto (sin corrupción ni guardado con pérdida).
            RestoreUiLayout();
        }
    }

    private void SnapshotUiLayoutRecursive(GameObject go)
    {
        foreach (var comp in go.Components)
            if (comp is UIElement el)
                uiLayoutSnapshots.Add((el, new UiLayoutSnapshot(el)));
        foreach (var child in go.Children)
            SnapshotUiLayoutRecursive(child);
    }

    private void RestoreUiLayout()
    {
        foreach (var (el, s) in uiLayoutSnapshots)
        {
            el.PosX = s.PosX; el.PosY = s.PosY; el.Width = s.Width; el.Height = s.Height;
            el.AnchorMinX = s.AnchorMinX; el.AnchorMinY = s.AnchorMinY;
            el.AnchorMaxX = s.AnchorMaxX; el.AnchorMaxY = s.AnchorMaxY;
            el.PivotX = s.PivotX; el.PivotY = s.PivotY;
        }
        uiLayoutSnapshots.Clear();
    }

    private static float ComputeCanvasScale(Canvas c, NumVector2 size)
    {
        float refW = Math.Max(1, c.ReferenceWidth);
        float refH = Math.Max(1, c.ReferenceHeight);
        return c.UIScaleMode switch
        {
            1 => ComputeScaleWithScreenSize(c, size, refW, refH),
            2 => Math.Max(0.0001f, c.FallbackScreenDPI) / 96f,
            _ => c.ScaleFactor <= 0f ? 1f : c.ScaleFactor
        };
    }

    private static float ComputeScaleWithScreenSize(Canvas c, NumVector2 size, float refW, float refH)
    {
        float sx = size.X / refW;
        float sy = size.Y / refH;
        if (c.ScreenMatchMode == 1) return MathF.Min(sx, sy);
        if (c.ScreenMatchMode == 2) return MathF.Max(sx, sy);
        return MathF.Pow(2f,
            MathF.Log2(Math.Max(0.0001f, sx)) +
            (MathF.Log2(Math.Max(0.0001f, sy)) - MathF.Log2(Math.Max(0.0001f, sx))) * Math.Clamp(c.MatchWidthOrHeight, 0f, 1f));
    }

    private UiRect ResolveCanvasRect(Canvas canvas, NumVector2 origin, NumVector2 viewportSize, float scale, bool pixelPerfect)
    {
        if (canvas.RenderMode == 0)
        {
            if (!isPlaying && !gameMode && !gameViewActive && canvas.SceneViewCanvasPreview)
            {
                float zoom = Math.Clamp(canvas.SceneViewZoom, 0.05f, 4f);
                float w = MathF.Max(1f, canvas.Width) * scale * zoom;
                float h = MathF.Max(1f, canvas.Height) * scale * zoom;
                var center = origin + viewportSize * 0.5f + new NumVector2(canvas.SceneViewPanX, canvas.SceneViewPanY);
                var min = center - new NumVector2(Math.Clamp(canvas.PivotX, 0f, 1f) * w, Math.Clamp(canvas.PivotY, 0f, 1f) * h);
                if (pixelPerfect)
                {
                    min.X = MathF.Round(min.X);
                    min.Y = MathF.Round(min.Y);
                    w = MathF.Round(w);
                    h = MathF.Round(h);
                }
                return new UiRect(min, min + new NumVector2(w, h));
            }

            return new UiRect(origin, origin + viewportSize);
        }

        if (canvas.RenderMode == 1)
        {
            if (canvas.ResizeCanvas)
                return new UiRect(origin, origin + viewportSize);

            float w = MathF.Max(1f, canvas.Width * scale);
            float h = MathF.Max(1f, canvas.Height * scale);
            float planeScale = ComputeCameraPlanePixelScale(canvas, viewportSize);
            w *= planeScale;
            h *= planeScale;

            float cx = origin.X + viewportSize.X * 0.5f + canvas.PosX * scale;
            float cy = origin.Y + viewportSize.Y * 0.5f + canvas.PosY * scale;
            float x = cx - Math.Clamp(canvas.PivotX, 0f, 1f) * w;
            float y = cy - Math.Clamp(canvas.PivotY, 0f, 1f) * h;
            if (pixelPerfect)
            {
                x = MathF.Round(x); y = MathF.Round(y); w = MathF.Round(w); h = MathF.Round(h);
            }
            return new UiRect(new NumVector2(x, y), new NumVector2(x + w, y + h));
        }

        if (TryResolveWorldSpaceCanvasRect(canvas, origin, viewportSize, scale, pixelPerfect, out var worldRect))
            return worldRect;

        return new UiRect(origin, origin);
    }

    private float ComputeCameraPlanePixelScale(Canvas canvas, NumVector2 viewportSize)
    {
        var cam = ResolveCanvasCameraState(canvas, forEvents: false) ?? activeUiCameraState;
        float distance = MathF.Max(0.01f, canvas.PlaneDistance);
        if (cam.Orthographic)
        {
            float visibleWorldHeight = Math.Max(0.01f, cam.OrthoSize * 2f);
            return Math.Clamp((viewportSize.Y / visibleWorldHeight) / MathF.Max(1f, canvas.ReferencePixelsPerUnit), 0.05f, 8f);
        }

        float fov = TkMathHelper.DegreesToRadians(Math.Clamp(cam.FOV, 5f, 170f));
        float visibleWorldHeightPersp = 2f * distance * MathF.Tan(fov * 0.5f);
        if (visibleWorldHeightPersp <= 0.0001f)
            return 1f;
        return Math.Clamp((viewportSize.Y / visibleWorldHeightPersp) / MathF.Max(1f, canvas.ReferencePixelsPerUnit), 0.05f, 8f);
    }

    private bool TryResolveWorldSpaceCanvasRect(Canvas canvas, NumVector2 origin, NumVector2 viewportSize, float scale, bool pixelPerfect, out UiRect rect)
    {
        rect = new UiRect(origin, origin);
        if (canvas.gameObject == null)
            return false;

        var cam = ResolveCanvasCameraState(canvas, forEvents: false) ?? activeUiCameraState;
        float ppu = MathF.Max(1f, canvas.ReferencePixelsPerUnit);
        float wWorld = MathF.Max(0.0001f, canvas.Width / ppu) * MathF.Max(0.0001f, canvas.DynamicPixelsPerUnit) * scale;
        float hWorld = MathF.Max(0.0001f, canvas.Height / ppu) * MathF.Max(0.0001f, canvas.DynamicPixelsPerUnit) * scale;

        GetCanvasWorldBasis(canvas, cam, out var center, out var right, out var up);
        center += right * (canvas.PosX / ppu) + up * (canvas.PosY / ppu);

        float pivotX = Math.Clamp(canvas.PivotX, 0f, 1f);
        float pivotY = Math.Clamp(canvas.PivotY, 0f, 1f);
        float left = -pivotX * wWorld;
        float rightExtent = (1f - pivotX) * wWorld;
        float bottom = -pivotY * hWorld;
        float top = (1f - pivotY) * hWorld;

        var p0 = center + right * left + up * bottom;
        var p1 = center + right * rightExtent + up * bottom;
        var p2 = center + right * rightExtent + up * top;
        var p3 = center + right * left + up * top;

        if (!TryWorldToScreen(p0, cam, origin, viewportSize, out var s0) ||
            !TryWorldToScreen(p1, cam, origin, viewportSize, out var s1) ||
            !TryWorldToScreen(p2, cam, origin, viewportSize, out var s2) ||
            !TryWorldToScreen(p3, cam, origin, viewportSize, out var s3))
        {
            return !canvas.HideWhenBehindCamera;
        }

        float minX = MathF.Min(MathF.Min(s0.X, s1.X), MathF.Min(s2.X, s3.X));
        float minY = MathF.Min(MathF.Min(s0.Y, s1.Y), MathF.Min(s2.Y, s3.Y));
        float maxX = MathF.Max(MathF.Max(s0.X, s1.X), MathF.Max(s2.X, s3.X));
        float maxY = MathF.Max(MathF.Max(s0.Y, s1.Y), MathF.Max(s2.Y, s3.Y));

        if (pixelPerfect)
        {
            minX = MathF.Round(minX); minY = MathF.Round(minY); maxX = MathF.Round(maxX); maxY = MathF.Round(maxY);
        }

        rect = new UiRect(new NumVector2(minX, minY), new NumVector2(maxX, maxY));
        return rect.IsValid;
    }

    private void GetCanvasWorldBasis(Canvas canvas, EditorCameraState cam, out TkVector3 center, out TkVector3 right, out TkVector3 up)
    {
        var go = canvas.gameObject!;
        var world = go.WorldMatrix;
        center = new TkVector3(world.M41, world.M42, world.M43);

        if (canvas.WorldSpaceBillboard)
        {
            var camForward = ToTkVector(cam.Front).Normalized();
            right = TkVector3.Cross(ToTkVector(cam.Up), camForward).Normalized();
            if (right.LengthSquared < 0.0001f) right = TkVector3.UnitX;
            up = TkVector3.Cross(camForward, right).Normalized();
            return;
        }

        right = new TkVector3(world.M11, world.M12, world.M13);
        up = new TkVector3(world.M21, world.M22, world.M23);
        if (right.LengthSquared < 0.0001f) right = TkVector3.UnitX;
        if (up.LengthSquared < 0.0001f) up = TkVector3.UnitY;
        right.Normalize();
        up.Normalize();
    }

    private bool TryWorldToScreen(TkVector3 world, EditorCameraState cam, NumVector2 origin, NumVector2 viewportSize, out NumVector2 screen)
    {
        screen = NumVector2.Zero;
        if (viewportSize.X <= 1f || viewportSize.Y <= 1f)
            return false;

        float nearClip = Math.Max(0.001f, cam.NearClip);
        float farClip = Math.Max(nearClip + 0.001f, cam.FarClip);
        TkMatrix4 projection = cam.Orthographic
            ? TkMatrix4.CreateOrthographic(Math.Max(0.01f, cam.OrthoSize) * 2f * viewportSize.X / Math.Max(1f, viewportSize.Y), Math.Max(0.01f, cam.OrthoSize) * 2f, nearClip, farClip)
            : TkMatrix4.CreatePerspectiveFieldOfView(TkMathHelper.DegreesToRadians(Math.Clamp(cam.FOV, 5f, 170f)), viewportSize.X / Math.Max(1f, viewportSize.Y), nearClip, farClip);

        var eye = ToTkVector(cam.Position);
        var view = TkMatrix4.LookAt(eye, eye + ToTkVector(cam.Front), ToTkVector(cam.Up));
        var clip = TkVector4.TransformRow(new TkVector4(world, 1f), view * projection);
        if (clip.W <= 0.0001f)
            return false;

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        float ndcZ = clip.Z / clip.W;
        if (ndcZ < -1f || ndcZ > 1f)
            return false;

        screen = new NumVector2(
            origin.X + (ndcX * 0.5f + 0.5f) * viewportSize.X,
            origin.Y + (1f - (ndcY * 0.5f + 0.5f)) * viewportSize.Y);
        return float.IsFinite(screen.X) && float.IsFinite(screen.Y);
    }

    private EditorCameraState? ResolveCanvasCameraState(Canvas canvas, bool forEvents)
    {
        string id = forEvents ? canvas.EventCameraId : canvas.RenderCameraId;
        string name = forEvents ? canvas.EventCameraName : canvas.RenderCameraName;
        var go = FindCameraObject(id, name);
        return go != null ? ComputeGameCameraState(go) : null;
    }

    private GameObject? FindCameraObject(string? id, string? name)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var byId = FindGameObjectByEditorId(objects, id);
            if (byId != null && (byId.IsCamera || byId.GetComponent<Camera>() != null))
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = FindGameObjectByName(objects, name);
            if (byName != null && (byName.IsCamera || byName.GetComponent<Camera>() != null))
                return byName;
        }

        return FindGameCamera(objects);
    }

    private static GameObject? FindGameObjectByEditorId(IEnumerable<GameObject> source, string id)
    {
        foreach (var go in source)
        {
            if (string.Equals(go.EditorId, id, StringComparison.OrdinalIgnoreCase))
                return go;
            var child = FindGameObjectByEditorId(go.Children, id);
            if (child != null) return child;
        }
        return null;
    }

    private static GameObject? FindGameObjectByName(IEnumerable<GameObject> source, string name)
    {
        foreach (var go in source)
        {
            if (string.Equals(go.Name, name, StringComparison.OrdinalIgnoreCase))
                return go;
            var child = FindGameObjectByName(go.Children, name);
            if (child != null) return child;
        }
        return null;
    }

    private static TkVector3 ToTkVector(CoreVector3 v) => new(v.X, v.Y, v.Z);

    private static void CollectCanvases(GameObject go, List<Canvas> result)
    {
        var c = go.GetComponent<Canvas>();
        if (c != null) result.Add(c);
        foreach (var child in go.Children)
            CollectCanvases(child, result);
    }

    private void ProcessGraphicRaycaster(List<(Canvas Canvas, UiRect Rect, float Scale)> canvasData)
    {
        uiHitScratch.Clear();
        foreach (var item in canvasData)
        {
            var canvas = item.Canvas;
            if (!canvas.BlocksRaycasts || !canvas.Interactable || canvas.Alpha <= 0.001f || canvas.gameObject == null)
                continue;
            CollectUiHits(canvas.gameObject, canvas, item.Rect, item.Rect, item.Scale, canvas.PixelPerfect, uiHitScratch);
        }

        var mouse = new NumVector2(MouseState.X, MouseState.Y);
        UIElement? hovered = null;
        for (int i = uiHitScratch.Count - 1; i >= 0; i--)
        {
            var h = uiHitScratch[i];
            if (!Contains(h.ClipRect, mouse))
                continue;
            if (h.Canvas.ClipToCanvas && !Contains(CanvasRectOf(canvasData, h.Canvas), mouse))
                continue;
            if (Contains(h.Rect, mouse))
            {
                hovered = h.Element;
                break;
            }
        }

        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        bool leftPressedThisFrame = leftDown && !previousUiLeftMouseDown;
        bool leftReleasedThisFrame = !leftDown && previousUiLeftMouseDown;

        UISelectable? clicked = null;
        if (leftPressedThisFrame)
        {
            pressedUiGraphic = hovered;
            pressedUiSelectable = hovered as UISelectable;
        }

        if (leftDown && pressedUiSelectable != null)
            UpdateContinuousSelectable(pressedUiSelectable, mouse, uiHitScratch);

        if (leftReleasedThisFrame)
        {
            if (pressedUiSelectable != null && hovered == pressedUiSelectable && pressedUiSelectable.Interactable)
            {
                clicked = pressedUiSelectable;
                ApplySelectableClick(clicked, mouse, uiHitScratch);
            }
            pressedUiGraphic = null;
            pressedUiSelectable = null;
        }

        hoveredUiGraphic = hovered;
        previousUiLeftMouseDown = leftDown;

        UpdateSelectablePointerStates(canvasData, hovered, pressedUiSelectable, leftDown, clicked);
        UIRaycast.SetPointerState(hovered, pressedUiGraphic, clicked);
    }

    private static UiRect CanvasRectOf(List<(Canvas Canvas, UiRect Rect, float Scale)> canvasData, Canvas canvas)
    {
        foreach (var item in canvasData)
            if (ReferenceEquals(item.Canvas, canvas))
                return item.Rect;
        return new UiRect(NumVector2.Zero, NumVector2.Zero);
    }

    private void ApplySelectableClick(UISelectable clicked, NumVector2 mouse, List<UiHitCandidate> candidates)
    {
        switch (clicked)
        {
            case UIToggle toggle:
                if (toggle.AllowSwitchOff || !toggle.IsOn)
                    toggle.IsOn = !toggle.IsOn;
                ApplyToggleGroup(toggle);
                break;
            case UIDropdown dropdown:
                var hit = candidates.FirstOrDefault(c => ReferenceEquals(c.Element, dropdown) && c.Rect.Height <= MathF.Max(2f, dropdown.Height * 1.5f));
                if (hit.Element != null)
                {
                    var popup = GetDropdownPopupRect(dropdown, hit.Rect);
                    if (dropdown.IsOpen && Contains(popup, mouse))
                    {
                        int count = GetDropdownOptions(dropdown).Length;
                        int index = (int)MathF.Floor((mouse.Y - popup.Min.Y) / MathF.Max(1f, dropdown.Height));
                        dropdown.Value = Math.Clamp(index, 0, Math.Max(0, count - 1));
                        dropdown.IsOpen = false;
                    }
                    else
                    {
                        dropdown.IsOpen = !dropdown.IsOpen;
                    }
                }
                break;
            case UIInputField input:
                UIEventSystem.SetSelected(input);
                break;
            default:
                UIEventSystem.SetSelected(clicked);
                break;
        }
    }

    private void UpdateContinuousSelectable(UISelectable selectable, NumVector2 mouse, List<UiHitCandidate> candidates)
    {
        var hit = candidates.LastOrDefault(c => ReferenceEquals(c.Element, selectable));
        if (hit.Element == null || !hit.Rect.IsValid)
            return;

        if (selectable is UISlider slider)
        {
            float t = slider.Direction <= 1
                ? (mouse.X - hit.Rect.Min.X) / Math.Max(1f, hit.Rect.Width)
                : (mouse.Y - hit.Rect.Min.Y) / Math.Max(1f, hit.Rect.Height);
            if (slider.Direction == 1 || slider.Direction == 3) t = 1f - t;
            t = Math.Clamp(t, 0f, 1f);
            float value = slider.MinValue + (slider.MaxValue - slider.MinValue) * t;
            slider.Value = slider.WholeNumbers ? MathF.Round(value) : value;
        }
        else if (selectable is UIScrollbar scrollbar)
        {
            float t = scrollbar.Direction == 0
                ? (mouse.X - hit.Rect.Min.X) / Math.Max(1f, hit.Rect.Width)
                : (mouse.Y - hit.Rect.Min.Y) / Math.Max(1f, hit.Rect.Height);
            scrollbar.Value = Math.Clamp(t, 0f, 1f);
        }
    }

    private void ApplyToggleGroup(UIToggle toggle)
    {
        if (!toggle.IsOn || string.IsNullOrWhiteSpace(toggle.GroupName))
            return;
        foreach (var root in objects)
            ApplyToggleGroupRecursive(root, toggle);
    }

    private static void ApplyToggleGroupRecursive(GameObject go, UIToggle active)
    {
        foreach (var comp in go.Components)
        {
            if (comp is UIToggle t && !ReferenceEquals(t, active) && string.Equals(t.GroupName, active.GroupName, StringComparison.OrdinalIgnoreCase))
                t.IsOn = false;
        }
        foreach (var child in go.Children)
            ApplyToggleGroupRecursive(child, active);
    }

    private void UpdateSelectablePointerStates(List<(Canvas Canvas, UiRect Rect, float Scale)> canvasData, UIElement? hovered, UISelectable? pressed, bool leftDown, UISelectable? clicked)
    {
        foreach (var item in canvasData)
            UpdateSelectablePointerStatesRecursive(item.Canvas.gameObject, hovered, pressed, leftDown, clicked);
    }

    private static void UpdateSelectablePointerStatesRecursive(GameObject go, UIElement? hovered, UISelectable? pressed, bool leftDown, UISelectable? clicked)
    {
        foreach (var comp in go.Components)
        {
            if (comp is UISelectable s)
            {
                bool isHovered = ReferenceEquals(hovered, s) && s.Interactable;
                bool isPressed = ReferenceEquals(pressed, s) && leftDown && s.Interactable;
                bool isClicked = ReferenceEquals(clicked, s) && s.Interactable;
                s.SetPointerState(isHovered, isPressed, isClicked);
            }
        }
        foreach (var child in go.Children)
            UpdateSelectablePointerStatesRecursive(child, hovered, pressed, leftDown, clicked);
    }

    private void ResetUiRaycastState(UISelectable? clicked)
    {
        hoveredUiGraphic = null;
        pressedUiGraphic = null;
        pressedUiSelectable = null;
        previousUiLeftMouseDown = false;
        UIRaycast.SetPointerState(null, null, clicked);
        // Sin interacción (edición o sin canvas): suelta la selección estática para no
        // mantener viva una referencia a un GameObject de una sesión de Play anterior.
        if (clicked == null)
        {
            UIEventSystem.ClearSelection();
            UIRaycast.ConsumeClick(out UISelectable? _);
        }
    }

    private static void CollectUiHits(GameObject go, Canvas canvas, UiRect parentRect, UiRect clipRect, float scale, bool pixelPerfect, List<UiHitCandidate> result)
    {
        if (!go.IsActive)
            return;

        UiRect childParentRect = parentRect;
        UiRect childClipRect = clipRect;
        bool foundElement = false;

        foreach (var comp in go.Components.OrderBy(c => c is UIElement e ? e.SortOrder : 0))
        {
            if (comp is UIElement el && comp.Enabled)
            {
                var rect = ResolveElementRect(el, parentRect, scale, pixelPerfect);
                if (el is UIScrollView scroll)
                    childParentRect = GetScrollContentRect(scroll, rect, scale, pixelPerfect);
                else if (!foundElement)
                    childParentRect = rect;

                if (el is UIMask or UIScrollView)
                    childClipRect = IntersectRects(childClipRect, rect);

                if (el.RaycastTarget && el.Alpha > 0.001f)
                {
                    result.Add(new UiHitCandidate(el, GetRaycastRect(el, rect, scale), childClipRect, canvas));
                    if (el is UIDropdown dropdown && dropdown.IsOpen)
                        result.Add(new UiHitCandidate(el, GetDropdownPopupRect(dropdown, rect), childClipRect, canvas));
                }

                foundElement = true;
            }
        }

        foreach (var child in go.Children)
            CollectUiHits(child, canvas, childParentRect, childClipRect, scale, pixelPerfect, result);
    }

    private static bool Contains(UiRect rect, NumVector2 point)
        => point.X >= rect.Min.X && point.X <= rect.Max.X && point.Y >= rect.Min.Y && point.Y <= rect.Max.Y;

    private static UiRect IntersectRects(UiRect a, UiRect b)
    {
        var min = new NumVector2(MathF.Max(a.Min.X, b.Min.X), MathF.Max(a.Min.Y, b.Min.Y));
        var max = new NumVector2(MathF.Min(a.Max.X, b.Max.X), MathF.Min(a.Max.Y, b.Max.Y));
        return new UiRect(min, max);
    }

    private static UiRect GetRaycastRect(UIElement el, UiRect rect, float scale)
    {
        if (el is UIImage img)
            return new UiRect(new NumVector2(rect.Min.X - img.RaycastPadLeft * scale, rect.Min.Y - img.RaycastPadTop * scale), new NumVector2(rect.Max.X + img.RaycastPadRight * scale, rect.Max.Y + img.RaycastPadBottom * scale));
        return rect;
    }

    private void HandleSelectedUiRectTool(List<(Canvas Canvas, UiRect Rect, float Scale)> canvasData)
    {
        if (selected == null || isPlaying || gameMode || gameViewActive)
            return;

        var selectedUi = selected.GetComponent<UIElement>();
        if (selectedUi == null)
        {
            activeUiRectToolObject = null;
            activeUiRectToolHandle = 0;
            HandleSelectedCanvasScenePreview(canvasData);
            return;
        }

        if (!TryGetSelectedUiRect(canvasData, selectedUi, out var rect, out _, out var scale))
            return;

        var io = ImGui.GetIO();
        var mouse = new NumVector2(MouseState.X, MouseState.Y);
        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        bool leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        if (leftReleased || !leftDown)
        {
            activeUiRectToolObject = null;
            activeUiRectToolHandle = 0;
        }

        if (leftClicked)
        {
            int handle = GetUiRectToolHandle(rect, mouse);
            if (handle != 0 || Contains(rect, mouse))
            {
                activeUiRectToolObject = selected;
                activeUiRectToolHandle = handle == 0 ? 1 : handle;
                activeUiRectToolStartMouse = mouse;
                activeUiStartPosX = selectedUi.PosX;
                activeUiStartPosY = selectedUi.PosY;
                activeUiStartWidth = selectedUi.Width;
                activeUiStartHeight = selectedUi.Height;
                activeUiStartPivotX = Math.Clamp(selectedUi.PivotX, 0f, 1f);
                activeUiStartPivotY = Math.Clamp(selectedUi.PivotY, 0f, 1f);
            }
        }

        if (!leftDown || activeUiRectToolObject == null || !ReferenceEquals(activeUiRectToolObject, selected))
            return;

        var delta = (mouse - activeUiRectToolStartMouse) / Math.Max(0.0001f, scale);
        if (io.KeyShift && activeUiRectToolHandle >= 2)
            delta = PreserveAspectDelta(delta, activeUiRectToolHandle, activeUiStartWidth, activeUiStartHeight);

        ApplyUiRectToolDelta(selectedUi, activeUiRectToolHandle, delta, io.KeyCtrl);
    }

    private void HandleSelectedCanvasScenePreview(List<(Canvas Canvas, UiRect Rect, float Scale)> canvasData)
    {
        if (selected == null)
            return;

        var canvas = selected.GetComponent<Canvas>();
        if (canvas == null || canvas.RenderMode != 0 || !canvas.SceneViewCanvasPreview)
            return;

        var io = ImGui.GetIO();
        var mouse = new NumVector2(MouseState.X, MouseState.Y);
        var item = canvasData.FirstOrDefault(c => ReferenceEquals(c.Canvas, canvas));
        if (item.Canvas == null || !Contains(item.Rect, mouse))
            return;

        if (io.KeyCtrl && MathF.Abs(io.MouseWheel) > 0.001f)
            canvas.SceneViewZoom = Math.Clamp(canvas.SceneViewZoom + io.MouseWheel * 0.04f, 0.05f, 4f);

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            canvas.SceneViewPanX += io.MouseDelta.X;
            canvas.SceneViewPanY += io.MouseDelta.Y;
        }
    }

    private static NumVector2 PreserveAspectDelta(NumVector2 delta, int handle, float startW, float startH)
    {
        float aspect = MathF.Abs(startH) < 0.001f ? 1f : startW / startH;
        if (aspect <= 0.0001f)
            return delta;

        bool horizontal = handle is 2 or 3 or 6 or 7 or 8 or 9;
        bool vertical = handle is 4 or 5 or 6 or 7 or 8 or 9;
        if (!horizontal || !vertical)
            return delta;

        float ax = MathF.Abs(delta.X);
        float ay = MathF.Abs(delta.Y * aspect);
        if (ax > ay)
            delta.Y = MathF.CopySign(MathF.Abs(delta.X / aspect), delta.Y == 0f ? delta.X : delta.Y);
        else
            delta.X = MathF.CopySign(MathF.Abs(delta.Y * aspect), delta.X == 0f ? delta.Y : delta.X);
        return delta;
    }

    private void ApplyUiRectToolDelta(UIElement el, int handle, NumVector2 d, bool expandFromCenter)
    {
        float px = activeUiStartPivotX;
        float py = activeUiStartPivotY;
        float dx = d.X;
        float dy = d.Y;

        if (handle == 1)
        {
            el.PosX = activeUiStartPosX + dx;
            el.PosY = activeUiStartPosY + dy;
            return;
        }

        float w = activeUiStartWidth;
        float h = activeUiStartHeight;
        float posX = activeUiStartPosX;
        float posY = activeUiStartPosY;

        void Left()
        {
            if (expandFromCenter) w = activeUiStartWidth - dx * 2f;
            else { w = activeUiStartWidth - dx; posX = activeUiStartPosX + dx * (1f - px); }
        }

        void Right()
        {
            if (expandFromCenter) w = activeUiStartWidth + dx * 2f;
            else { w = activeUiStartWidth + dx; posX = activeUiStartPosX + dx * px; }
        }

        void Top()
        {
            if (expandFromCenter) h = activeUiStartHeight - dy * 2f;
            else { h = activeUiStartHeight - dy; posY = activeUiStartPosY + dy * (1f - py); }
        }

        void Bottom()
        {
            if (expandFromCenter) h = activeUiStartHeight + dy * 2f;
            else { h = activeUiStartHeight + dy; posY = activeUiStartPosY + dy * py; }
        }

        switch (handle)
        {
            case 2: Left(); break;
            case 3: Right(); break;
            case 4: Top(); break;
            case 5: Bottom(); break;
            case 6: Left(); Top(); break;
            case 7: Right(); Top(); break;
            case 8: Left(); Bottom(); break;
            case 9: Right(); Bottom(); break;
        }

        el.Width = Math.Max(1f, w);
        el.Height = Math.Max(1f, h);
        el.PosX = posX;
        el.PosY = posY;
    }

    private static int GetUiRectToolHandle(UiRect rect, NumVector2 mouse)
    {
        const float s = 8f;
        bool Hit(NumVector2 p) => MathF.Abs(mouse.X - p.X) <= s && MathF.Abs(mouse.Y - p.Y) <= s;
        var tl = rect.Min;
        var tr = new NumVector2(rect.Max.X, rect.Min.Y);
        var bl = new NumVector2(rect.Min.X, rect.Max.Y);
        var br = rect.Max;
        var l = new NumVector2(rect.Min.X, rect.Center.Y);
        var r = new NumVector2(rect.Max.X, rect.Center.Y);
        var t = new NumVector2(rect.Center.X, rect.Min.Y);
        var b = new NumVector2(rect.Center.X, rect.Max.Y);
        if (Hit(tl)) return 6;
        if (Hit(tr)) return 7;
        if (Hit(bl)) return 8;
        if (Hit(br)) return 9;
        if (Hit(l)) return 2;
        if (Hit(r)) return 3;
        if (Hit(t)) return 4;
        if (Hit(b)) return 5;
        return 0;
    }

    private bool TryGetSelectedUiRect(List<(Canvas Canvas, UiRect Rect, float Scale)> canvasData, UIElement selectedUi, out UiRect rect, out UiRect parentRect, out float scale)
    {
        rect = default;
        parentRect = default;
        scale = 1f;
        foreach (var item in canvasData)
        {
            if (selectedUi.gameObject == null || item.Canvas.gameObject == null || !IsDescendantOf(selectedUi.gameObject, item.Canvas.gameObject))
                continue;
            if (TryFindUiElementRect(item.Canvas.gameObject, selectedUi, item.Rect, item.Rect, item.Scale, item.Canvas.PixelPerfect, out rect, out parentRect))
            {
                scale = item.Scale;
                return true;
            }
        }
        return false;
    }

    private bool TryFindUiElementRect(GameObject go, UIElement target, UiRect parentRect, UiRect clipRect, float scale, bool pixelPerfect, out UiRect rect, out UiRect foundParent)
    {
        rect = default;
        foundParent = default;
        if (!go.IsActive)
            return false;

        UiRect childParentRect = parentRect;
        UiRect childClipRect = clipRect;
        bool foundElement = false;

        foreach (var comp in go.Components.OrderBy(c => c is UIElement e ? e.SortOrder : 0))
        {
            if (comp is UIElement el && comp.Enabled)
            {
                var r = ResolveElementRect(el, parentRect, scale, pixelPerfect);
                if (ReferenceEquals(el, target))
                {
                    rect = r;
                    foundParent = parentRect;
                    return true;
                }

                if (el is UIScrollView scroll)
                    childParentRect = GetScrollContentRect(scroll, r, scale, pixelPerfect);
                else if (!foundElement)
                    childParentRect = r;

                if (el is UIMask or UIScrollView)
                    childClipRect = IntersectRects(childClipRect, r);
                foundElement = true;
            }
        }

        foreach (var child in go.Children)
            if (TryFindUiElementRect(child, target, childParentRect, childClipRect, scale, pixelPerfect, out rect, out foundParent))
                return true;
        return false;
    }

    private static bool IsDescendantOf(GameObject obj, GameObject root)
    {
        var cur = obj;
        while (cur != null)
        {
            if (ReferenceEquals(cur, root)) return true;
            cur = cur.Parent;
        }
        return false;
    }

    private void DrawUINodeGizmosOnly(ImDrawListPtr dl, GameObject go, UiRect parentRect, UiRect clipRect, float scale, bool pixelPerfect)
    {
        if (!go.IsActive)
            return;

        UiRect childParentRect = parentRect;
        UiRect childClipRect = clipRect;
        bool foundElement = false;

        foreach (var comp in go.Components.OrderBy(c => c is UIElement e ? e.SortOrder : 0))
        {
            if (comp is UIElement el && comp.Enabled)
            {
                var rect = ResolveElementRect(el, parentRect, scale, pixelPerfect);
                DrawUIElementEditorRect(dl, el, parentRect, rect, true);

                if (el is UIScrollView scroll)
                    childParentRect = GetScrollContentRect(scroll, rect, scale, pixelPerfect);
                else if (!foundElement)
                    childParentRect = rect;

                if (el is UIMask or UIScrollView)
                    childClipRect = IntersectRects(childClipRect, rect);
                foundElement = true;
            }
        }

        foreach (var child in go.Children)
            DrawUINodeGizmosOnly(dl, child, childParentRect, childClipRect, scale, pixelPerfect);
    }

    private void DrawUINode(ImDrawListPtr dl, GameObject go, UiRect parentRect, UiRect clipRect, float scale, bool pixelPerfect, float inheritedAlpha, bool drawEditorGizmos)
    {
        if (!go.IsActive)
            return;

        UiRect childParentRect = parentRect;
        UiRect childClipRect = clipRect;
        float childAlpha = inheritedAlpha;
        bool foundElement = false;
        bool pushedClip = false;

        foreach (var comp in go.Components.OrderBy(c => c is UIElement e ? e.SortOrder : 0))
        {
            if (comp is UIElement el && comp.Enabled)
            {
                var rect = DrawUIElement(dl, el, parentRect, scale, pixelPerfect, inheritedAlpha, drawEditorGizmos);
                if (el is UIScrollView scroll)
                    childParentRect = GetScrollContentRect(scroll, rect, scale, pixelPerfect);
                else if (!foundElement)
                    childParentRect = rect;

                childAlpha = Math.Clamp(inheritedAlpha * el.Alpha, 0f, 1f);
                if (el is UIMask or UIScrollView)
                {
                    childClipRect = IntersectRects(childClipRect, rect);
                    dl.PushClipRect(childClipRect.Min, childClipRect.Max, true);
                    pushedClip = true;
                }
                foundElement = true;
            }
        }

        foreach (var child in go.Children)
            DrawUINode(dl, child, childParentRect, childClipRect, scale, pixelPerfect, childAlpha, drawEditorGizmos);

        if (pushedClip)
            dl.PopClipRect();
    }

    private UiRect DrawUIElement(ImDrawListPtr dl, UIElement el, UiRect parentRect, float scale, bool pixelPerfect, float inheritedAlpha, bool drawEditorGizmos)
    {
        var rect = ResolveElementRect(el, parentRect, scale, pixelPerfect);
        float alpha = Math.Clamp(inheritedAlpha * el.Alpha, 0f, 1f);

        switch (el)
        {
            case UIMask mask:
                DrawUIMask(dl, mask, rect, scale, alpha);
                break;
            case UIScrollView scroll:
                DrawUIScrollView(dl, scroll, rect, scale, alpha);
                break;
            case UIImage img:
                DrawUIImage(dl, img, rect, scale, alpha);
                break;
            case UIButton button:
                DrawUIButton(dl, button, rect, scale, alpha);
                break;
            case UIToggle toggle:
                DrawUIToggle(dl, toggle, rect, scale, alpha);
                break;
            case UISlider slider:
                DrawUISlider(dl, slider, rect, scale, alpha);
                break;
            case UIScrollbar scrollbar:
                DrawUIScrollbar(dl, scrollbar, rect, scale, alpha);
                break;
            case UIDropdown dropdown:
                DrawUIDropdown(dl, dropdown, rect, scale, alpha);
                break;
            case UIInputField input:
                DrawUIInputField(dl, input, rect, scale, alpha);
                break;
            case UIBar bar:
                DrawUIBar(dl, bar, rect, scale, alpha);
                break;
            case UIText txt:
                DrawUIText(dl, txt, rect, scale, alpha);
                break;
        }

        if (drawEditorGizmos)
            DrawUIElementGizmo(dl, el, parentRect, rect);
        return rect;
    }

    private static UiRect ResolveElementRect(UIElement el, UiRect parentRect, float scale, bool pixelPerfect)
    {
        float parentW = parentRect.Width;
        float parentH = parentRect.Height;
        float minAnchorX = parentRect.Min.X + Math.Clamp(el.AnchorMinX, 0f, 1f) * parentW;
        float minAnchorY = parentRect.Min.Y + Math.Clamp(el.AnchorMinY, 0f, 1f) * parentH;
        float maxAnchorX = parentRect.Min.X + Math.Clamp(el.AnchorMaxX, 0f, 1f) * parentW;
        float maxAnchorY = parentRect.Min.Y + Math.Clamp(el.AnchorMaxY, 0f, 1f) * parentH;

        float anchorW = maxAnchorX - minAnchorX;
        float anchorH = maxAnchorY - minAnchorY;
        float x, y, w, h;

        if (el.UseOffsets && (MathF.Abs(anchorW) > 0.001f || MathF.Abs(anchorH) > 0.001f))
        {
            x = minAnchorX + el.Left * scale;
            y = minAnchorY + el.Top * scale;
            w = MathF.Max(0f, anchorW - (el.Left + el.Right) * scale);
            h = MathF.Max(0f, anchorH - (el.Top + el.Bottom) * scale);
        }
        else
        {
            w = MathF.Max(0f, anchorW + el.Width * scale * MathF.Max(0.0001f, el.ScaleX));
            h = MathF.Max(0f, anchorH + el.Height * scale * MathF.Max(0.0001f, el.ScaleY));
            if (MathF.Abs(anchorW) < 0.001f) w = MathF.Max(0f, el.Width * scale * MathF.Max(0.0001f, el.ScaleX));
            if (MathF.Abs(anchorH) < 0.001f) h = MathF.Max(0f, el.Height * scale * MathF.Max(0.0001f, el.ScaleY));

            float pivotX = Math.Clamp(el.PivotX, 0f, 1f);
            float pivotY = Math.Clamp(el.PivotY, 0f, 1f);
            float refX = minAnchorX + anchorW * pivotX;
            float refY = minAnchorY + anchorH * pivotY;
            x = refX + el.PosX * scale - pivotX * w;
            y = refY + el.PosY * scale - pivotY * h;
        }

        if (pixelPerfect)
        {
            x = MathF.Round(x); y = MathF.Round(y); w = MathF.Round(w); h = MathF.Round(h);
        }
        return new UiRect(new NumVector2(x, y), new NumVector2(x + w, y + h));
    }

    private static UiRect GetScrollContentRect(UIScrollView scroll, UiRect rect, float scale, bool pixelPerfect)
    {
        float maxX = MathF.Max(0f, scroll.ContentWidth - rect.Width / Math.Max(0.0001f, scale));
        float maxY = MathF.Max(0f, scroll.ContentHeight - rect.Height / Math.Max(0.0001f, scale));
        scroll.ScrollX = Math.Clamp(scroll.ScrollX, 0f, maxX);
        scroll.ScrollY = Math.Clamp(scroll.ScrollY, 0f, maxY);
        var min = new NumVector2(rect.Min.X - scroll.ScrollX * scale, rect.Min.Y - scroll.ScrollY * scale);
        var max = min + new NumVector2(scroll.ContentWidth * scale, scroll.ContentHeight * scale);
        if (pixelPerfect)
        {
            min = new NumVector2(MathF.Round(min.X), MathF.Round(min.Y));
            max = new NumVector2(MathF.Round(max.X), MathF.Round(max.Y));
        }
        return new UiRect(min, max);
    }

    private void ApplyUILayouts(GameObject go, UiRect parentRect, float scale, bool pixelPerfect)
    {
        if (!go.IsActive) return;
        UiRect currentRect = parentRect;
        foreach (var comp in go.Components)
        {
            if (comp is UIElement el && comp.Enabled)
            {
                ApplySelfFitters(go, el);
                currentRect = ResolveElementRect(el, parentRect, scale, pixelPerfect);
                break;
            }
        }

        ApplyLayoutGroupToChildren(go, currentRect, scale);
        foreach (var child in go.Children)
            ApplyUILayouts(child, currentRect, scale, pixelPerfect);
    }

    private static void ApplySelfFitters(GameObject go, UIElement el)
    {
        var aspect = go.GetComponent<UIAspectRatioFitter>();
        if (aspect != null && aspect.Enabled && aspect.AspectMode != 0 && aspect.AspectRatio > 0.001f)
        {
            if (aspect.AspectMode == 1)
                el.Height = Math.Max(1f, el.Width / aspect.AspectRatio);
            else if (aspect.AspectMode == 2)
                el.Width = Math.Max(1f, el.Height * aspect.AspectRatio);
        }

        var fitter = go.GetComponent<UIContentSizeFitter>();
        if (fitter != null && fitter.Enabled)
        {
            float maxW = 0f, maxH = 0f;
            foreach (var child in go.Children)
            {
                var childEl = child.GetComponent<UIElement>();
                if (childEl == null) continue;
                maxW = MathF.Max(maxW, childEl.Width);
                maxH += childEl.Height;
            }
            if (fitter.HorizontalFit != 0 && maxW > 0f) el.Width = maxW;
            if (fitter.VerticalFit != 0 && maxH > 0f) el.Height = maxH;
        }
    }

    private void ApplyLayoutGroupToChildren(GameObject go, UiRect rect, float scale)
    {
        var h = go.GetComponent<UIHorizontalLayoutGroup>();
        var v = go.GetComponent<UIVerticalLayoutGroup>();
        var g = go.GetComponent<UIGridLayoutGroup>();
        if (h != null && h.Enabled) ApplyHorizontalLayout(go, h, rect, scale);
        else if (v != null && v.Enabled) ApplyVerticalLayout(go, v, rect, scale);
        else if (g != null && g.Enabled) ApplyGridLayout(go, g, rect, scale);
    }

    // Rellena un buffer reutilizable en vez de asignar una List nueva por cada grupo y frame.
    // El resultado se consume de inmediato (sin recursión intermedia), así que compartir el
    // buffer es seguro mientras el render de UI sea de un solo hilo.
    private List<UIElement> GetLayoutChildren(GameObject go)
    {
        layoutChildrenScratch.Clear();
        foreach (var child in go.Children)
        {
            var el = child.GetComponent<UIElement>();
            var le = child.GetComponent<UILayoutElement>();
            if (el != null && (le == null || !le.IgnoreLayout))
                layoutChildrenScratch.Add(el);
        }
        return layoutChildrenScratch;
    }

    private void ApplyHorizontalLayout(GameObject go, UIHorizontalLayoutGroup layout, UiRect rect, float scale)
    {
        var children = GetLayoutChildren(go);
        if (layout.ReverseArrangement) children.Reverse();
        float x = layout.PaddingLeft;
        float availH = MathF.Max(1f, rect.Height / Math.Max(0.0001f, scale) - layout.PaddingTop - layout.PaddingBottom);
        foreach (var child in children)
        {
            float w = child.Width;
            float h = layout.ControlChildHeight ? availH : child.Height;
            child.AnchorMinX = child.AnchorMaxX = 0f;
            child.AnchorMinY = child.AnchorMaxY = 0f;
            child.PivotX = 0f;
            child.PivotY = 0f;
            child.PosX = x;
            child.PosY = layout.PaddingTop;
            if (layout.ControlChildWidth) child.Width = w;
            if (layout.ControlChildHeight) child.Height = h;
            x += w + layout.Spacing;
        }
    }

    private void ApplyVerticalLayout(GameObject go, UIVerticalLayoutGroup layout, UiRect rect, float scale)
    {
        var children = GetLayoutChildren(go);
        if (layout.ReverseArrangement) children.Reverse();
        float y = layout.PaddingTop;
        float availW = MathF.Max(1f, rect.Width / Math.Max(0.0001f, scale) - layout.PaddingLeft - layout.PaddingRight);
        foreach (var child in children)
        {
            float h = child.Height;
            child.AnchorMinX = child.AnchorMaxX = 0f;
            child.AnchorMinY = child.AnchorMaxY = 0f;
            child.PivotX = 0f;
            child.PivotY = 0f;
            child.PosX = layout.PaddingLeft;
            child.PosY = y;
            if (layout.ControlChildWidth) child.Width = availW;
            if (layout.ControlChildHeight) child.Height = h;
            y += h + layout.Spacing;
        }
    }

    private void ApplyGridLayout(GameObject go, UIGridLayoutGroup grid, UiRect rect, float scale)
    {
        var children = GetLayoutChildren(go);
        int columns;
        if (grid.Constraint == 1)            // FixedColumnCount
            columns = Math.Max(1, grid.ConstraintCount);
        else if (grid.Constraint == 2)       // FixedRowCount → columnas = ceil(n / filas)
        {
            int rows = Math.Max(1, grid.ConstraintCount);
            columns = Math.Max(1, (children.Count + rows - 1) / rows);
        }
        else                                 // Flexible → cuántas caben por ancho
            columns = Math.Max(1, (int)MathF.Floor((rect.Width / Math.Max(0.0001f, scale) - grid.PaddingLeft - grid.PaddingRight + grid.SpacingX) / Math.Max(1f, grid.CellWidth + grid.SpacingX)));
        for (int i = 0; i < children.Count; i++)
        {
            int col = i % columns;
            int row = i / columns;
            var child = children[i];
            child.AnchorMinX = child.AnchorMaxX = 0f;
            child.AnchorMinY = child.AnchorMaxY = 0f;
            child.PivotX = 0f;
            child.PivotY = 0f;
            child.PosX = grid.PaddingLeft + col * (grid.CellWidth + grid.SpacingX);
            child.PosY = grid.PaddingTop + row * (grid.CellHeight + grid.SpacingY);
            child.Width = grid.CellWidth;
            child.Height = grid.CellHeight;
        }
    }

    private void DrawUIImage(ImDrawListPtr dl, UIImage img, UiRect rect, float scale, float alpha)
    {
        float a = Math.Clamp(img.A * alpha, 0f, 1f);
        if (a <= 0f && img.CullTransparentMesh) return;
        uint col = ImGui.ColorConvertFloat4ToU32(new NumVector4(img.R, img.G, img.B, a));
        var info = GetUiTextureInfo(img.SpritePath);
        int tex = info.Tex;
        var dMin = rect.Min;
        var dMax = rect.Max;
        if (tex != 0 && img.PreserveAspect && info.W > 0 && info.H > 0)
            FitPreserveAspect(ref dMin, ref dMax, info.W, info.H);
        NumVector2 uvMin = NumVector2.Zero, uvMax = NumVector2.One;
        if (img.ImageType == 3 && tex != 0)
            ApplyFilledImage(img, ref dMin, ref dMax, ref uvMin, ref uvMax);
        float rounding = MathF.Max(0f, img.CornerRadius * scale);

        if (tex != 0 && img.ImageType == 1)
            DrawSlicedImage(dl, tex, dMin, dMax, info.W, info.H, img, col, scale);
        else if (tex != 0 && img.ImageType == 2)
            DrawTiledImage(dl, tex, dMin, dMax, img, col, scale);
        else if (MathF.Abs(img.RotZ) > 0.001f)
            DrawRotatedImageOrQuad(dl, tex, dMin, dMax, img.RotZ, uvMin, uvMax, col);
        else if (tex != 0)
            dl.AddImage((IntPtr)tex, dMin, dMax, uvMin, uvMax, col);
        else
            dl.AddRectFilled(dMin, dMax, col, rounding);

        DrawOutline(dl, dMin, dMax, rounding, img.OutlineThickness * scale, new NumVector4(img.OutlineR, img.OutlineG, img.OutlineB, img.OutlineA * alpha));
    }

    private static void ApplyFilledImage(UIImage img, ref NumVector2 dMin, ref NumVector2 dMax, ref NumVector2 uvMin, ref NumVector2 uvMax)
    {
        float amt = Math.Clamp(img.FillAmount, 0f, 1f);
        if (img.FillMethod == 0)
        {
            if (img.FillOrigin == 1) { dMin.X = dMax.X - (dMax.X - dMin.X) * amt; uvMin.X = 1f - amt; }
            else { dMax.X = dMin.X + (dMax.X - dMin.X) * amt; uvMax.X = amt; }
        }
        else if (img.FillMethod == 1)
        {
            if (img.FillOrigin == 1) { dMax.Y = dMin.Y + (dMax.Y - dMin.Y) * amt; uvMax.Y = amt; }
            else { dMin.Y = dMax.Y - (dMax.Y - dMin.Y) * amt; uvMin.Y = 1f - amt; }
        }
    }

    private static void DrawSlicedImage(ImDrawListPtr dl, int tex, NumVector2 min, NumVector2 max, int texW, int texH, UIImage img, uint col, float scale)
    {
        float l = MathF.Min(img.BorderLeft * scale, (max.X - min.X) * 0.45f);
        float r = MathF.Min(img.BorderRight * scale, (max.X - min.X) * 0.45f);
        float t = MathF.Min(img.BorderTop * scale, (max.Y - min.Y) * 0.45f);
        float b = MathF.Min(img.BorderBottom * scale, (max.Y - min.Y) * 0.45f);
        float ul = img.BorderLeft / Math.Max(1, texW);
        float ur = 1f - img.BorderRight / Math.Max(1, texW);
        float vt = img.BorderTop / Math.Max(1, texH);
        float vb = 1f - img.BorderBottom / Math.Max(1, texH);
        float[] xs = { min.X, min.X + l, max.X - r, max.X };
        float[] ys = { min.Y, min.Y + t, max.Y - b, max.Y };
        float[] us = { 0f, ul, ur, 1f };
        float[] vs = { 0f, vt, vb, 1f };
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            if (!img.FillCenter && x == 1 && y == 1) continue;
            if (xs[x + 1] <= xs[x] || ys[y + 1] <= ys[y]) continue;
            dl.AddImage((IntPtr)tex, new NumVector2(xs[x], ys[y]), new NumVector2(xs[x + 1], ys[y + 1]), new NumVector2(us[x], vs[y]), new NumVector2(us[x + 1], vs[y + 1]), col);
        }
    }

    private static void DrawTiledImage(ImDrawListPtr dl, int tex, NumVector2 min, NumVector2 max, UIImage img, uint col, float scale)
    {
        float tile = MathF.Max(4f, 64f * MathF.Max(0.01f, img.TileScale) * scale);
        int xCount = Math.Min(128, (int)MathF.Ceiling((max.X - min.X) / tile));
        int yCount = Math.Min(128, (int)MathF.Ceiling((max.Y - min.Y) / tile));
        for (int y = 0; y < yCount; y++)
        for (int x = 0; x < xCount; x++)
        {
            var a = new NumVector2(min.X + x * tile, min.Y + y * tile);
            var b = new NumVector2(MathF.Min(a.X + tile, max.X), MathF.Min(a.Y + tile, max.Y));
            var uvMax = new NumVector2((b.X - a.X) / tile, (b.Y - a.Y) / tile);
            dl.AddImage((IntPtr)tex, a, b, NumVector2.Zero, uvMax, col);
        }
    }

    private void DrawUIButton(ImDrawListPtr dl, UIButton button, UiRect rect, float scale, float alpha)
    {
        DrawSelectableBackground(dl, button, rect, scale, alpha, button.CornerRadius, button.OutlineThickness, new NumVector4(button.OutlineR, button.OutlineG, button.OutlineB, button.OutlineA));
        DrawTextInside(dl, button.Text ?? "", rect, MathF.Max(1f, button.FontSize * scale), new NumVector4(button.TextR, button.TextG, button.TextB, button.TextA * alpha), 1, 1, true, false, default, false, default, 0f);
    }

    private static void DrawSelectableBackground(ImDrawListPtr dl, UISelectable s, UiRect rect, float scale, float alpha, float radius = 4f, float outline = 0f, NumVector4 outlineColor = default)
    {
        NumVector4 bg = !s.Interactable
            ? new NumVector4(s.DisabledR, s.DisabledG, s.DisabledB, s.DisabledA)
            : s.Transition == 0
                ? new NumVector4(s.NormalR, s.NormalG, s.NormalB, s.NormalA)
                : s.IsPressed
                    ? new NumVector4(s.PressedR, s.PressedG, s.PressedB, s.PressedA)
                    : s.IsHovered
                        ? new NumVector4(s.HighlightedR, s.HighlightedG, s.HighlightedB, s.HighlightedA)
                        : s.IsSelected
                            ? new NumVector4(s.SelectedR, s.SelectedG, s.SelectedB, s.SelectedA)
                            : new NumVector4(s.NormalR, s.NormalG, s.NormalB, s.NormalA);
        bg.W *= alpha;
        float rounding = MathF.Max(0f, radius * scale);
        dl.AddRectFilled(rect.Min, rect.Max, ImGui.ColorConvertFloat4ToU32(bg), rounding);
        if (outline > 0f)
            DrawOutline(dl, rect.Min, rect.Max, rounding, outline * scale, new NumVector4(outlineColor.X, outlineColor.Y, outlineColor.Z, outlineColor.W * alpha));
    }

    private static void DrawUIToggle(ImDrawListPtr dl, UIToggle toggle, UiRect rect, float scale, float alpha)
    {
        float box = MathF.Min(rect.Height, 22f * scale);
        var boxRect = new UiRect(rect.Min + new NumVector2(0f, (rect.Height - box) * 0.5f), rect.Min + new NumVector2(box, (rect.Height + box) * 0.5f));
        DrawSelectableBackground(dl, toggle, boxRect, scale, alpha, 3f, 1f, new NumVector4(0f, 0f, 0f, 0.6f));
        if (toggle.IsOn)
        {
            var p1 = boxRect.Min + new NumVector2(box * 0.22f, box * 0.52f);
            var p2 = boxRect.Min + new NumVector2(box * 0.42f, box * 0.72f);
            var p3 = boxRect.Min + new NumVector2(box * 0.78f, box * 0.28f);
            uint c = ImGui.ColorConvertFloat4ToU32(new NumVector4(toggle.CheckmarkR, toggle.CheckmarkG, toggle.CheckmarkB, toggle.CheckmarkA * alpha));
            dl.AddLine(p1, p2, c, 2.2f * scale);
            dl.AddLine(p2, p3, c, 2.2f * scale);
        }
        var textRect = new UiRect(new NumVector2(rect.Min.X + box + 8f * scale, rect.Min.Y), rect.Max);
        DrawTextInside(dl, toggle.Label ?? "", textRect, MathF.Max(1f, toggle.FontSize * scale), new NumVector4(toggle.TextR, toggle.TextG, toggle.TextB, toggle.TextA * alpha), 0, 1, true, false, default, false, default, 0f);
    }

    private static void DrawUISlider(ImDrawListPtr dl, UISlider slider, UiRect rect, float scale, float alpha)
    {
        DrawSelectableBackground(dl, slider, rect, scale, alpha, 4f);
        float t = Math.Clamp((slider.Value - slider.MinValue) / Math.Max(0.0001f, slider.MaxValue - slider.MinValue), 0f, 1f);
        if (slider.Direction == 1 || slider.Direction == 3) t = 1f - t;
        bool horizontal = slider.Direction <= 1;
        var trackMin = horizontal ? new NumVector2(rect.Min.X, rect.Center.Y - 3f * scale) : new NumVector2(rect.Center.X - 3f * scale, rect.Min.Y);
        var trackMax = horizontal ? new NumVector2(rect.Max.X, rect.Center.Y + 3f * scale) : new NumVector2(rect.Center.X + 3f * scale, rect.Max.Y);
        dl.AddRectFilled(trackMin, trackMax, ImGui.ColorConvertFloat4ToU32(new NumVector4(slider.TrackR, slider.TrackG, slider.TrackB, slider.TrackA * alpha)), 3f * scale);
        if (horizontal)
        {
            var fillMax = new NumVector2(trackMin.X + (trackMax.X - trackMin.X) * t, trackMax.Y);
            dl.AddRectFilled(trackMin, fillMax, ImGui.ColorConvertFloat4ToU32(new NumVector4(slider.FillR, slider.FillG, slider.FillB, slider.FillA * alpha)), 3f * scale);
            float hx = trackMin.X + (trackMax.X - trackMin.X) * t;
            dl.AddCircleFilled(new NumVector2(hx, rect.Center.Y), slider.HandleSize * scale * 0.5f, ImGui.ColorConvertFloat4ToU32(new NumVector4(slider.HandleR, slider.HandleG, slider.HandleB, slider.HandleA * alpha)), 20);
        }
        else
        {
            var fillMax = new NumVector2(trackMax.X, trackMin.Y + (trackMax.Y - trackMin.Y) * t);
            dl.AddRectFilled(trackMin, fillMax, ImGui.ColorConvertFloat4ToU32(new NumVector4(slider.FillR, slider.FillG, slider.FillB, slider.FillA * alpha)), 3f * scale);
            float hy = trackMin.Y + (trackMax.Y - trackMin.Y) * t;
            dl.AddCircleFilled(new NumVector2(rect.Center.X, hy), slider.HandleSize * scale * 0.5f, ImGui.ColorConvertFloat4ToU32(new NumVector4(slider.HandleR, slider.HandleG, slider.HandleB, slider.HandleA * alpha)), 20);
        }
    }

    private static void DrawUIScrollbar(ImDrawListPtr dl, UIScrollbar bar, UiRect rect, float scale, float alpha)
    {
        DrawSelectableBackground(dl, bar, rect, scale, alpha, 4f);
        dl.AddRectFilled(rect.Min, rect.Max, ImGui.ColorConvertFloat4ToU32(new NumVector4(bar.TrackR, bar.TrackG, bar.TrackB, bar.TrackA * alpha)), 4f * scale);
        float value = Math.Clamp(bar.Value, 0f, 1f);
        float size = Math.Clamp(bar.Size, 0.02f, 1f);
        if (bar.Direction == 0)
        {
            float w = rect.Width * size;
            float x = rect.Min.X + (rect.Width - w) * value;
            dl.AddRectFilled(new NumVector2(x, rect.Min.Y), new NumVector2(x + w, rect.Max.Y), ImGui.ColorConvertFloat4ToU32(new NumVector4(bar.HandleR, bar.HandleG, bar.HandleB, bar.HandleA * alpha)), 4f * scale);
        }
        else
        {
            float h = rect.Height * size;
            float y = rect.Min.Y + (rect.Height - h) * value;
            dl.AddRectFilled(new NumVector2(rect.Min.X, y), new NumVector2(rect.Max.X, y + h), ImGui.ColorConvertFloat4ToU32(new NumVector4(bar.HandleR, bar.HandleG, bar.HandleB, bar.HandleA * alpha)), 4f * scale);
        }
    }

    private void DrawUIDropdown(ImDrawListPtr dl, UIDropdown dd, UiRect rect, float scale, float alpha)
    {
        DrawSelectableBackground(dl, dd, rect, scale, alpha, 4f, 1f, new NumVector4(0f, 0f, 0f, 0.5f));
        var options = GetDropdownOptions(dd);
        string current = options.Length == 0 ? "" : options[Math.Clamp(dd.Value, 0, options.Length - 1)];
        DrawTextInside(dl, current, new UiRect(rect.Min + new NumVector2(8f * scale, 0f), rect.Max - new NumVector2(22f * scale, 0f)), MathF.Max(1f, dd.FontSize * scale), new NumVector4(dd.TextR, dd.TextG, dd.TextB, dd.TextA * alpha), 0, 1, true, false, default, false, default, 0f);
        uint arrow = ImGui.ColorConvertFloat4ToU32(new NumVector4(dd.TextR, dd.TextG, dd.TextB, dd.TextA * alpha));
        var c = new NumVector2(rect.Max.X - 12f * scale, rect.Center.Y);
        dl.AddTriangleFilled(c + new NumVector2(-5f, -2f) * scale, c + new NumVector2(5f, -2f) * scale, c + new NumVector2(0f, 4f) * scale, arrow);
        if (dd.IsOpen)
        {
            var popup = GetDropdownPopupRect(dd, rect);
            dl.AddRectFilled(popup.Min, popup.Max, ImGui.ColorConvertFloat4ToU32(new NumVector4(0.08f, 0.08f, 0.09f, 0.96f * alpha)), 4f * scale);
            dl.AddRect(popup.Min, popup.Max, ImGui.ColorConvertFloat4ToU32(new NumVector4(0f, 0f, 0f, 0.65f * alpha)), 4f * scale);
            for (int i = 0; i < options.Length; i++)
            {
                var r = new UiRect(new NumVector2(popup.Min.X, popup.Min.Y + i * dd.Height * scale), new NumVector2(popup.Max.X, popup.Min.Y + (i + 1) * dd.Height * scale));
                if (i == dd.Value)
                    dl.AddRectFilled(r.Min, r.Max, ImGui.ColorConvertFloat4ToU32(new NumVector4(0.18f, 0.36f, 0.62f, 0.65f * alpha)));
                DrawTextInside(dl, options[i], new UiRect(r.Min + new NumVector2(8f * scale, 0f), r.Max), MathF.Max(1f, dd.FontSize * scale), new NumVector4(dd.TextR, dd.TextG, dd.TextB, dd.TextA * alpha), 0, 1, true, false, default, false, default, 0f);
            }
        }
    }

    private static string[] GetDropdownOptions(UIDropdown dd)
        => (dd.Options ?? "").Split(new[] { '\n', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static UiRect GetDropdownPopupRect(UIDropdown dd, UiRect rect)
    {
        int count = Math.Max(1, GetDropdownOptions(dd).Length);
        float h = MathF.Min(dd.PopupHeight, count * dd.Height);
        return new UiRect(new NumVector2(rect.Min.X, rect.Max.Y + 2f), new NumVector2(rect.Max.X, rect.Max.Y + 2f + h));
    }

    private static void DrawUIInputField(ImDrawListPtr dl, UIInputField input, UiRect rect, float scale, float alpha)
    {
        DrawSelectableBackground(dl, input, rect, scale, alpha, 4f, 1f, new NumVector4(0f, 0f, 0f, 0.55f));
        bool hasText = !string.IsNullOrEmpty(input.Text);
        string text = hasText ? input.Text : input.Placeholder;
        if (input.ContentType == 3 && hasText)
            text = new string('•', input.Text.Length);
        var col = hasText
            ? new NumVector4(input.TextR, input.TextG, input.TextB, input.TextA * alpha)
            : new NumVector4(input.PlaceholderR, input.PlaceholderG, input.PlaceholderB, input.PlaceholderA * alpha);
        DrawTextInside(dl, text, new UiRect(rect.Min + new NumVector2(8f * scale, 0f), rect.Max - new NumVector2(8f * scale, 0f)), MathF.Max(1f, input.FontSize * scale), col, 0, 1, true, false, default, false, default, 0f);
        if (input.IsFocused)
        {
            float x = rect.Min.X + 8f * scale + MeasureText(text, input.FontSize * scale).X + 1f;
            dl.AddLine(new NumVector2(x, rect.Min.Y + 6f * scale), new NumVector2(x, rect.Max.Y - 6f * scale), ImGui.ColorConvertFloat4ToU32(col), 1f);

            // Input real usando ImGui como backend de texto. Visualmente se mantiene el campo propio de Groko,
            // pero recibe teclado, backspace, paste y selección de texto del editor.
            string edit = input.Text ?? "";
            ImGui.SetCursorScreenPos(rect.Min + new NumVector2(8f * scale, MathF.Max(1f, (rect.Height - input.FontSize * scale) * 0.5f)));
            ImGui.SetNextItemWidth(MathF.Max(1f, rect.Width - 16f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.01f);
            if (ImGui.InputText("##groko_ui_input_" + input.GetHashCode(), ref edit, (uint)Math.Max(32, input.CharacterLimit <= 0 ? 4096 : input.CharacterLimit + 1)))
            {
                if (input.CharacterLimit > 0 && edit.Length > input.CharacterLimit)
                    edit = edit[..input.CharacterLimit];
                input.Text = edit;
            }
            ImGui.PopStyleVar();
        }
    }

    private static void DrawUIScrollView(ImDrawListPtr dl, UIScrollView scroll, UiRect rect, float scale, float alpha)
    {
        if (scroll.ShowBackground)
            dl.AddRectFilled(rect.Min, rect.Max, ImGui.ColorConvertFloat4ToU32(new NumVector4(scroll.BackR, scroll.BackG, scroll.BackB, scroll.BackA * alpha)), MathF.Max(0f, scroll.CornerRadius * scale));
        dl.AddRect(rect.Min, rect.Max, ImGui.ColorConvertFloat4ToU32(new NumVector4(1f, 1f, 1f, 0.10f * alpha)), MathF.Max(0f, scroll.CornerRadius * scale));
    }

    private static void DrawUIMask(ImDrawListPtr dl, UIMask mask, UiRect rect, float scale, float alpha)
    {
        if (mask.ShowMaskGraphic && mask.A * alpha > 0.001f)
            dl.AddRectFilled(rect.Min, rect.Max, ImGui.ColorConvertFloat4ToU32(new NumVector4(mask.R, mask.G, mask.B, mask.A * alpha)), 0f);
    }

    private static void DrawUIBar(ImDrawListPtr dl, UIBar bar, UiRect rect, float scale, float alpha)
    {
        float rounding = MathF.Max(0f, bar.CornerRadius * scale);
        uint back = ImGui.ColorConvertFloat4ToU32(new NumVector4(bar.BackR, bar.BackG, bar.BackB, bar.BackA * alpha));
        uint fill = ImGui.ColorConvertFloat4ToU32(new NumVector4(bar.FillR, bar.FillG, bar.FillB, bar.FillA * alpha));
        dl.AddRectFilled(rect.Min, rect.Max, back, rounding);
        float b = Math.Max(0f, bar.Border) * scale;
        var fMin = new NumVector2(rect.Min.X + b, rect.Min.Y + b);
        float innerW = Math.Max(0f, rect.Width - 2f * b);
        float innerH = Math.Max(0f, rect.Height - 2f * b);
        float v = Math.Clamp(bar.Value, 0f, 1f);
        var fMax = new NumVector2(fMin.X + innerW * v, fMin.Y + innerH);
        if (innerW * v > 0f && innerH > 0f)
            dl.AddRectFilled(fMin, fMax, fill, MathF.Max(0f, rounding - b));
        if (bar.ShowValueText)
            DrawTextInside(dl, $"{v * 100f:0}%", rect, MathF.Max(10f, 13f * scale), new NumVector4(bar.TextR, bar.TextG, bar.TextB, bar.TextA * alpha), 1, 1, true, false, default, false, default, 0f);
    }

    private static void DrawUIText(ImDrawListPtr dl, UIText txt, UiRect rect, float scale, float alpha)
    {
        float fontSize = Math.Max(1f, txt.FontSize * scale);
        if (txt.BestFit)
        {
            var textSize = MeasureText(txt.Text ?? "", fontSize);
            if (textSize.X > rect.Width && textSize.X > 0f)
            {
                float fitted = fontSize * (rect.Width / textSize.X);
                fontSize = Math.Clamp(fitted, Math.Max(1f, txt.MinFontSize * scale), Math.Max(1f, txt.MaxFontSize * scale));
            }
        }
        DrawTextInside(dl, txt.Text ?? "", rect, fontSize, new NumVector4(txt.R, txt.G, txt.B, txt.A * alpha), txt.Align, txt.VerticalAlign, txt.Overflow == 0, txt.Shadow, new NumVector4(txt.ShadowR, txt.ShadowG, txt.ShadowB, txt.ShadowA * alpha), txt.Outline, new NumVector4(txt.OutlineR, txt.OutlineG, txt.OutlineB, txt.OutlineA * alpha), txt.OutlineThickness * scale, txt.ShadowOffsetX * scale, txt.ShadowOffsetY * scale);
    }

    private static void FitPreserveAspect(ref NumVector2 min, ref NumVector2 max, int imageW, int imageH)
    {
        float rw = max.X - min.X;
        float rh = max.Y - min.Y;
        float spriteAspect = imageW / (float)Math.Max(1, imageH);
        float rectAspect = rw / Math.Max(0.0001f, rh);
        float dw = rw, dh = rh;
        if (spriteAspect > rectAspect) dh = rw / spriteAspect; else dw = rh * spriteAspect;
        float cx = (min.X + max.X) * 0.5f;
        float cy = (min.Y + max.Y) * 0.5f;
        min = new NumVector2(cx - dw * 0.5f, cy - dh * 0.5f);
        max = new NumVector2(cx + dw * 0.5f, cy + dh * 0.5f);
    }

    private static void DrawRotatedImageOrQuad(ImDrawListPtr dl, int tex, NumVector2 min, NumVector2 max, float rotZ, NumVector2 uvMin, NumVector2 uvMax, uint col)
    {
        var center = new NumVector2((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f);
        float a = rotZ * (MathF.PI / 180f);
        float cosA = MathF.Cos(a), sinA = MathF.Sin(a);
        NumVector2 Rot(NumVector2 p)
        {
            float dx = p.X - center.X, dy = p.Y - center.Y;
            return new NumVector2(center.X + dx * cosA - dy * sinA, center.Y + dx * sinA + dy * cosA);
        }
        var p1 = Rot(min);
        var p2 = Rot(new NumVector2(max.X, min.Y));
        var p3 = Rot(max);
        var p4 = Rot(new NumVector2(min.X, max.Y));
        if (tex != 0)
            dl.AddImageQuad((IntPtr)tex, p1, p2, p3, p4, uvMin, new NumVector2(uvMax.X, uvMin.Y), uvMax, new NumVector2(uvMin.X, uvMax.Y), col);
        else
            dl.AddQuadFilled(p1, p2, p3, p4, col);
    }

    private static NumVector2 MeasureText(string text, float fontSize)
    {
        float baseSize = ImGui.GetFontSize();
        var textSize = ImGui.CalcTextSize(text ?? "");
        return baseSize > 0f ? textSize * (fontSize / baseSize) : textSize;
    }

    private static void DrawTextInside(ImDrawListPtr dl, string text, UiRect rect, float fontSize, NumVector4 color, int align, int verticalAlign, bool strongClip, bool shadow, NumVector4 shadowColor, bool outline, NumVector4 outlineColor, float outlineThickness, float shadowOffsetX = 1f, float shadowOffsetY = 1f)
    {
        color.W = Math.Clamp(color.W, 0f, 1f);
        if (color.W <= 0f || string.IsNullOrEmpty(text)) return;
        var textSize = MeasureText(text, fontSize);
        float tx = rect.Min.X;
        if (align == 1) tx = rect.Min.X + (rect.Width - textSize.X) * 0.5f;
        else if (align == 2) tx = rect.Max.X - textSize.X;
        float ty = rect.Min.Y;
        if (verticalAlign == 1) ty = rect.Min.Y + (rect.Height - textSize.Y) * 0.5f;
        else if (verticalAlign == 2) ty = rect.Max.Y - textSize.Y;
        if (strongClip) dl.PushClipRect(rect.Min, rect.Max, true);
        if (shadow && shadowColor.W > 0f)
            dl.AddText(ImGui.GetFont(), fontSize, new NumVector2(tx + shadowOffsetX, ty + shadowOffsetY), ImGui.ColorConvertFloat4ToU32(shadowColor), text);
        if (outline && outlineColor.W > 0f && outlineThickness > 0f)
        {
            uint o = ImGui.ColorConvertFloat4ToU32(outlineColor);
            float t = MathF.Max(1f, outlineThickness);
            dl.AddText(ImGui.GetFont(), fontSize, new NumVector2(tx - t, ty), o, text);
            dl.AddText(ImGui.GetFont(), fontSize, new NumVector2(tx + t, ty), o, text);
            dl.AddText(ImGui.GetFont(), fontSize, new NumVector2(tx, ty - t), o, text);
            dl.AddText(ImGui.GetFont(), fontSize, new NumVector2(tx, ty + t), o, text);
        }
        dl.AddText(ImGui.GetFont(), fontSize, new NumVector2(tx, ty), ImGui.ColorConvertFloat4ToU32(color), text);
        if (strongClip) dl.PopClipRect();
    }

    private static void DrawOutline(ImDrawListPtr dl, NumVector2 min, NumVector2 max, float rounding, float thickness, NumVector4 color)
    {
        if (thickness <= 0f || color.W <= 0f) return;
        dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(color), rounding, ImDrawFlags.None, thickness);
    }

    private void DrawCanvasGizmo(ImDrawListPtr dl, Canvas canvas, UiRect rect)
    {
        bool selectedCanvas = selected == canvas.gameObject;
        uint col = ImGui.GetColorU32(selectedCanvas ? new NumVector4(1f, 0.78f, 0.22f, 0.95f) : canvas.RenderMode == 0 ? new NumVector4(0.28f, 0.55f, 0.95f, 0.32f) : canvas.RenderMode == 1 ? new NumVector4(0.35f, 0.85f, 0.65f, 0.40f) : new NumVector4(0.85f, 0.45f, 0.95f, 0.45f));
        dl.AddRect(rect.Min, rect.Max, col, 0f, ImDrawFlags.None, selectedCanvas ? 2f : 1f);
        if (selectedCanvas)
        {
            uint labelCol = ImGui.GetColorU32(new NumVector4(1f, 1f, 1f, 0.75f));
            string mode = canvas.RenderMode == 0 ? "Overlay" : canvas.RenderMode == 1 ? "Screen Space - Camera" : "World Space";
            dl.AddText(rect.Min + new NumVector2(6f, 5f), labelCol, "Canvas  " + mode);
        }
    }

    private void DrawUIElementGizmo(ImDrawListPtr dl, UIElement el, UiRect parentRect, UiRect rect)
        => DrawUIElementEditorRect(dl, el, parentRect, rect, false);

    private void DrawUIElementEditorRect(ImDrawListPtr dl, UIElement el, UiRect parentRect, UiRect rect, bool force)
    {
        bool selectedElement = selected == el.gameObject;
        if (!selectedElement && !force)
            return;

        uint selectedCol = ImGui.GetColorU32(new NumVector4(1f, 0.78f, 0.22f, 0.95f));
        uint passiveCol = ImGui.GetColorU32(new NumVector4(0.42f, 0.74f, 1f, force ? 0.32f : 0.18f));
        uint anchorCol = ImGui.GetColorU32(new NumVector4(0.20f, 0.55f, 1f, 0.75f));
        uint mutedCol = ImGui.GetColorU32(new NumVector4(1f, 1f, 1f, 0.25f));
        uint rectCol = selectedElement ? selectedCol : passiveCol;
        dl.AddRect(rect.Min, rect.Max, rectCol, 0f, ImDrawFlags.None, selectedElement ? 1.6f : 1f);

        var pivot = new NumVector2(rect.Min.X + rect.Width * Math.Clamp(el.PivotX, 0f, 1f), rect.Min.Y + rect.Height * Math.Clamp(el.PivotY, 0f, 1f));
        dl.AddCircleFilled(pivot, selectedElement ? 3.8f : 2.6f, rectCol, 12);

        var aMin = new NumVector2(parentRect.Min.X + Math.Clamp(el.AnchorMinX, 0f, 1f) * parentRect.Width, parentRect.Min.Y + Math.Clamp(el.AnchorMinY, 0f, 1f) * parentRect.Height);
        var aMax = new NumVector2(parentRect.Min.X + Math.Clamp(el.AnchorMaxX, 0f, 1f) * parentRect.Width, parentRect.Min.Y + Math.Clamp(el.AnchorMaxY, 0f, 1f) * parentRect.Height);
        if (selectedElement)
        {
            dl.AddRect(aMin - new NumVector2(4f, 4f), aMin + new NumVector2(4f, 4f), anchorCol, 1f, ImDrawFlags.None, 1.2f);
            dl.AddRect(aMax - new NumVector2(4f, 4f), aMax + new NumVector2(4f, 4f), anchorCol, 1f, ImDrawFlags.None, 1.2f);
            dl.AddLine(aMin, pivot, mutedCol, 1f);
            if ((aMax - aMin).LengthSquared() > 0.5f)
                dl.AddLine(aMax, pivot, mutedCol, 1f);

            DrawUiRectHandle(dl, rect.Min, selectedCol);
            DrawUiRectHandle(dl, new NumVector2(rect.Max.X, rect.Min.Y), selectedCol);
            DrawUiRectHandle(dl, new NumVector2(rect.Min.X, rect.Max.Y), selectedCol);
            DrawUiRectHandle(dl, rect.Max, selectedCol);
            DrawUiRectHandle(dl, new NumVector2(rect.Min.X, rect.Center.Y), selectedCol);
            DrawUiRectHandle(dl, new NumVector2(rect.Max.X, rect.Center.Y), selectedCol);
            DrawUiRectHandle(dl, new NumVector2(rect.Center.X, rect.Min.Y), selectedCol);
            DrawUiRectHandle(dl, new NumVector2(rect.Center.X, rect.Max.Y), selectedCol);
        }
    }

    private static void DrawUiRectHandle(ImDrawListPtr dl, NumVector2 center, uint color)
    {
        var s = new NumVector2(4f, 4f);
        dl.AddRectFilled(center - s, center + s, ImGui.GetColorU32(new NumVector4(0.05f, 0.06f, 0.07f, 0.96f)), 1.5f);
        dl.AddRect(center - s, center + s, color, 1.5f, ImDrawFlags.None, 1.2f);
    }

    private int GetUiTexture(string? path) => GetUiTextureInfo(path).Tex;

    private (int Tex, int W, int H) GetUiTextureInfo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (0, 0, 0);
        if (uiTextureCache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) { uiTextureCache[path] = (0, 0, 0); return (0, 0, 0); }
        try
        {
            using var original = new System.Drawing.Bitmap(path);
            using var bitmap = original.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb
                ? new System.Drawing.Bitmap(original)
                : original.Clone(new System.Drawing.Rectangle(0, 0, original.Width, original.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bitmap.Width, bitmap.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            }
            finally { bitmap.UnlockBits(data); }
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            var info = (texture, bitmap.Width, bitmap.Height);
            uiTextureCache[path] = info;
            return info;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("UI sprite load failed (" + path + "): " + ex.Message);
            uiTextureCache[path] = (0, 0, 0);
            return (0, 0, 0);
        }
    }
}
