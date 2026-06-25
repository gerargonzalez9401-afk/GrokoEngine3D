using System;

namespace GrokoEngine
{
    // =====================================================
    // UI IN-GAME (estilo Unity uGUI, implementación propia)
    // Canvas + CanvasScaler + GraphicRaycaster + EventSystem
    // RectTransform + Image/Text/Selectable/Layout/Mask/Scroll.
    // =====================================================

    public enum CanvasRenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }
    public enum CanvasScaleMode { ConstantPixelSize = 0, ScaleWithScreenSize = 1, ConstantPhysicalSize = 2 }
    public enum CanvasScreenMatchMode { MatchWidthOrHeight = 0, Expand = 1, Shrink = 2 }
    public enum UITransition { None = 0, ColorTint = 1 }
    public enum UIImageType { Simple = 0, Sliced = 1, Tiled = 2, Filled = 3 }
    public enum UIFillMethod { Horizontal = 0, Vertical = 1, Radial90 = 2, Radial180 = 3, Radial360 = 4 }
    public enum UILayoutChildControl { None = 0, Width = 1, Height = 2, Both = 3 }
    public enum UIContentFitMode { Unconstrained = 0, MinSize = 1, PreferredSize = 2 }
    public enum UIAspectMode { None = 0, WidthControlsHeight = 1, HeightControlsWidth = 2, FitInParent = 3, EnvelopeParent = 4 }

    /// <summary>
    /// Raíz de UI. Equivalente práctico al Canvas de Unity:
    /// Canvas + Canvas Scaler + Graphic Raycaster + Canvas Group.
    /// </summary>
    public class Canvas : Component
    {
        // ── Rect Transform del Canvas ──
        public float PosX = 0f, PosY = 0f;
        public float Width = 1920f, Height = 1080f;
        public float PivotX = 0.5f, PivotY = 0.5f;

        // ── Canvas ──
        public int RenderMode = 0;          // 0 Overlay · 1 Screen Space-Camera · 2 World Space
        public bool PixelPerfect = false;
        public int SortOrder = 0;
        public int TargetDisplay = 0;
        public float PlaneDistance = 100f;
        public bool ResizeCanvas = false;

        public string RenderCameraName = "";
        public string RenderCameraId = "";
        public string EventCameraName = "";
        public string EventCameraId = "";

        public bool WorldSpaceBillboard = false;
        public bool HideWhenBehindCamera = true;

        public string SortingLayer = "Default";
        public int OrderInLayer = 0;
        public bool OverrideSorting = false;
        public int AdditionalShaderChannels = 0;
        public bool VertexColorAlwaysGammaSpace = true;

        public bool ClipToCanvas = true;
        public bool ShowGizmos = true;
        public int EditorPreviewMode = 2;   // 0 Always · 1 GameOnly · 2 SceneGizmoOnly

        // Scene View editing preview. Overlay keeps full-screen behavior in Game View,
        // but appears as an editable canvas plane in Scene View, like Unity.
        public bool SceneViewCanvasPreview = true;
        public float SceneViewZoom = 0.35f;
        public float SceneViewPanX = 0f;
        public float SceneViewPanY = 0f;

        // ── Canvas Scaler ──
        public int UIScaleMode = 0;
        public float ScaleFactor = 1f;
        public float ReferencePixelsPerUnit = 100f;
        public int ReferenceWidth = 1920;
        public int ReferenceHeight = 1080;
        public int ScreenMatchMode = 0;
        public float MatchWidthOrHeight = 0f;
        public int PhysicalUnit = 3;
        public float FallbackScreenDPI = 96f;
        public float DefaultSpriteDPI = 96f;
        public float DynamicPixelsPerUnit = 1f;

        // ── Graphic Raycaster ──
        public bool IgnoreReversedGraphics = true;
        public int BlockingObjects = 0;
        public int BlockingMask = -1;

        // ── Canvas Group ──
        public float Alpha = 1f;
        public bool Interactable = true;
        public bool BlocksRaycasts = true;
    }

    /// <summary>
    /// RectTransform estilo Unity. Origen de UI arriba-izquierda, anclas/pivote en 0..1.
    /// Width/Height funcionan como SizeDelta; PosX/PosY como AnchoredPosition.
    /// </summary>
    public abstract class UIElement : Component
    {
        public float PosX = 0f, PosY = 0f, PosZ = 0f;
        public float Width = 100f, Height = 100f;
        public float AnchorMinX = 0.5f, AnchorMinY = 0.5f;
        public float AnchorMaxX = 0.5f, AnchorMaxY = 0.5f;
        public float PivotX = 0.5f, PivotY = 0.5f;
        public float RotX = 0f, RotY = 0f, RotZ = 0f;
        public float ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f;

        // Offsets tipo Unity cuando el RectTransform está en stretch.
        public float Left = 0f, Right = 0f, Top = 0f, Bottom = 0f;
        public bool UseOffsets = false;

        // Canvas Renderer común.
        public float Alpha = 1f;
        public bool RaycastTarget = true;
        public bool Maskable = true;
        public int SortOrder = 0;
        public int LayoutPriority = 0;
    }

    public class UILayoutElement : Component
    {
        public bool IgnoreLayout = false;
        public float MinWidth = -1f, MinHeight = -1f;
        public float PreferredWidth = -1f, PreferredHeight = -1f;
        public float FlexibleWidth = -1f, FlexibleHeight = -1f;
        public int LayoutPriority = 1;
    }

    public abstract class UILayoutGroup : Component
    {
        public float PaddingLeft = 0f, PaddingRight = 0f, PaddingTop = 0f, PaddingBottom = 0f;
        public float Spacing = 4f;
        public bool ChildForceExpandWidth = false;
        public bool ChildForceExpandHeight = false;
        public bool ControlChildWidth = true;
        public bool ControlChildHeight = true;
        public int ChildAlignment = 0; // 0 UpperLeft · 1 UpperCenter · 2 UpperRight · 3 MiddleLeft · 4 MiddleCenter · 5 MiddleRight · 6 LowerLeft · 7 LowerCenter · 8 LowerRight
        public bool ReverseArrangement = false;
    }

    public sealed class UIHorizontalLayoutGroup : UILayoutGroup { }
    public sealed class UIVerticalLayoutGroup : UILayoutGroup { }

    public sealed class UIGridLayoutGroup : UILayoutGroup
    {
        public float CellWidth = 100f;
        public float CellHeight = 30f;
        public float SpacingX = 4f;
        public float SpacingY = 4f;
        public int Constraint = 0; // 0 Flexible · 1 FixedColumnCount · 2 FixedRowCount
        public int ConstraintCount = 2;
        public int StartCorner = 0; // 0 UpperLeft · 1 UpperRight · 2 LowerLeft · 3 LowerRight
        public int StartAxis = 0;   // 0 Horizontal · 1 Vertical
    }

    public sealed class UIContentSizeFitter : Component
    {
        public int HorizontalFit = 0;
        public int VerticalFit = 0;
    }

    public sealed class UIAspectRatioFitter : Component
    {
        public int AspectMode = 0;
        public float AspectRatio = 1.7777778f;
    }

    public class UIMask : UIElement
    {
        public bool ShowMaskGraphic = false;
        public float R = 0f, G = 0f, B = 0f, A = 0.15f;

        public UIMask()
        {
            Width = 240f;
            Height = 160f;
            AnchorMinX = AnchorMaxX = 0.5f;
            AnchorMinY = AnchorMaxY = 0.5f;
        }
    }

    public sealed class UIRectMask2D : UIMask { }

    public class UIImage : UIElement
    {
        public string SpritePath = "";
        public float R = 1f, G = 1f, B = 1f, A = 1f;
        public string MaterialPath = "";
        public float RaycastPadLeft = 0f, RaycastPadBottom = 0f, RaycastPadRight = 0f, RaycastPadTop = 0f;

        public int ImageType = 0;
        public bool UseSpriteMesh = false;
        public bool PreserveAspect = false;
        public bool FillCenter = true;
        public float PixelsPerUnitMultiplier = 1f;
        public int FillMethod = 0;
        public int FillOrigin = 0;
        public float FillAmount = 1f;
        public bool Clockwise = true;

        // 9-slice / sprite border.
        public float BorderLeft = 8f, BorderRight = 8f, BorderTop = 8f, BorderBottom = 8f;
        public float TileScale = 1f;

        public float CornerRadius = 0f;
        public float OutlineThickness = 0f;
        public float OutlineR = 0f, OutlineG = 0f, OutlineB = 0f, OutlineA = 1f;

        public bool CullTransparentMesh = false;
    }

    public sealed class UIPanel : UIImage
    {
        public UIPanel()
        {
            Width = 320f;
            Height = 180f;
            R = 0.12f; G = 0.12f; B = 0.14f; A = 0.92f;
            CornerRadius = 6f;
            OutlineThickness = 1f;
            OutlineA = 0.35f;
        }
    }

    public sealed class UIRawImage : UIImage
    {
        public string TexturePath
        {
            get => SpritePath;
            set => SpritePath = value;
        }
    }

    public class UIText : UIElement
    {
        public string Text = "Text";
        public float R = 1f, G = 1f, B = 1f, A = 1f;
        public float FontSize = 24f;
        public int Align = 0;
        public int VerticalAlign = 0;
        public bool BestFit = false;
        public float MinFontSize = 10f;
        public float MaxFontSize = 40f;
        public bool RichText = false;
        public bool WordWrap = true;
        public int Overflow = 0; // 0 Truncate/Clip · 1 Overflow
        public bool Bold = false;
        public bool Italic = false;
        public bool Underline = false;
        public float LineSpacing = 1f;

        public bool Shadow = false;
        public float ShadowOffsetX = 1f, ShadowOffsetY = 1f;
        public float ShadowR = 0f, ShadowG = 0f, ShadowB = 0f, ShadowA = 0.55f;

        public bool Outline = false;
        public float OutlineThickness = 1f;
        public float OutlineR = 0f, OutlineG = 0f, OutlineB = 0f, OutlineA = 0.75f;

        public UIText()
        {
            Width = 240f;
            Height = 50f;
            AnchorMinX = AnchorMaxX = 0.5f;
            AnchorMinY = AnchorMaxY = 0.5f;
        }
    }

    public abstract class UISelectable : UIElement
    {
        public bool Interactable = true;
        public string TargetGraphic = "Self";
        public int Transition = 1;
        public int Navigation = 0; // 0 None · 1 Automatic · 2 Explicit

        public float NormalR = 0.23f, NormalG = 0.23f, NormalB = 0.25f, NormalA = 1f;
        public float HighlightedR = 0.32f, HighlightedG = 0.32f, HighlightedB = 0.36f, HighlightedA = 1f;
        public float PressedR = 0.18f, PressedG = 0.38f, PressedB = 0.65f, PressedA = 1f;
        public float SelectedR = 0.22f, SelectedG = 0.44f, SelectedB = 0.74f, SelectedA = 1f;
        public float DisabledR = 0.18f, DisabledG = 0.18f, DisabledB = 0.18f, DisabledA = 0.55f;

        public bool IsHovered { get; private set; }
        public bool IsPressed { get; private set; }
        public bool IsSelected { get; private set; }
        public bool WasClicked { get; private set; }
        public int ClickCount { get; private set; }

        public event Action<UISelectable>? Clicked;

        public virtual void SetPointerState(bool hovered, bool pressed, bool clicked)
        {
            IsHovered = hovered;
            IsPressed = pressed;
            WasClicked = clicked;
            IsSelected = UIEventSystem.CurrentSelectedSelectable == this;
            if (clicked)
            {
                ClickCount++;
                UIEventSystem.SetSelected(this);
                Clicked?.Invoke(this);
            }
        }

        public bool ConsumeClick()
        {
            bool clicked = WasClicked;
            WasClicked = false;
            return clicked;
        }
    }

    public class UIButton : UISelectable
    {
        public string Text = "Button";
        public float FontSize = 18f;
        public float TextR = 1f, TextG = 1f, TextB = 1f, TextA = 1f;
        public float CornerRadius = 4f;
        public float OutlineThickness = 1f;
        public float OutlineR = 0f, OutlineG = 0f, OutlineB = 0f, OutlineA = 0.45f;

        // Para reaccionar al click, suscríbete al evento base UISelectable.Clicked
        // (Action<UISelectable>) y castea a UIButton si lo necesitas. Antes había aquí un
        // evento `new Clicked` que OCULTABA al de la base: existían dos eventos distintos y,
        // según el tipo por el que te suscribías, recibías uno u otro. Eliminado.

        public UIButton()
        {
            Width = 160f;
            Height = 36f;
        }
    }

    public sealed class UIToggle : UISelectable
    {
        public bool IsOn = false;
        public string Label = "Toggle";
        public float FontSize = 16f;
        public float CheckmarkR = 0.25f, CheckmarkG = 0.65f, CheckmarkB = 1f, CheckmarkA = 1f;
        public float TextR = 1f, TextG = 1f, TextB = 1f, TextA = 1f;
        public string GroupName = "";
        public bool AllowSwitchOff = true;

        public UIToggle()
        {
            Width = 160f;
            Height = 28f;
        }
    }

    public sealed class UISlider : UISelectable
    {
        public float MinValue = 0f;
        public float MaxValue = 1f;
        public float Value = 0.5f;
        public bool WholeNumbers = false;
        public int Direction = 0; // 0 LeftToRight · 1 RightToLeft · 2 BottomToTop · 3 TopToBottom
        public float FillR = 0.20f, FillG = 0.55f, FillB = 0.95f, FillA = 1f;
        public float TrackR = 0.12f, TrackG = 0.12f, TrackB = 0.14f, TrackA = 1f;
        public float HandleR = 0.90f, HandleG = 0.90f, HandleB = 0.92f, HandleA = 1f;
        public float HandleSize = 14f;

        public UISlider()
        {
            Width = 220f;
            Height = 22f;
        }
    }

    public sealed class UIScrollbar : UISelectable
    {
        public float Value = 0f;
        public float Size = 0.2f;
        public int Direction = 1; // 0 LeftToRight · 1 TopToBottom
        public float TrackR = 0.09f, TrackG = 0.09f, TrackB = 0.10f, TrackA = 1f;
        public float HandleR = 0.35f, HandleG = 0.35f, HandleB = 0.38f, HandleA = 1f;

        public UIScrollbar()
        {
            Width = 16f;
            Height = 180f;
        }
    }

    public sealed class UIDropdown : UISelectable
    {
        public string Options = "Option A\nOption B\nOption C";
        public int Value = 0;
        public bool IsOpen = false;
        public float FontSize = 16f;
        public float TextR = 1f, TextG = 1f, TextB = 1f, TextA = 1f;
        public float PopupHeight = 120f;

        public UIDropdown()
        {
            Width = 220f;
            Height = 32f;
        }
    }

    public sealed class UIInputField : UISelectable
    {
        public string Text = "";
        public string Placeholder = "Enter text...";
        public int CharacterLimit = 0;
        public int ContentType = 0; // 0 Standard · 1 Integer · 2 Decimal · 3 Password
        public float FontSize = 16f;
        public float TextR = 1f, TextG = 1f, TextB = 1f, TextA = 1f;
        public float PlaceholderR = 0.6f, PlaceholderG = 0.6f, PlaceholderB = 0.6f, PlaceholderA = 0.85f;
        public bool IsFocused => UIEventSystem.CurrentSelectedSelectable == this;

        public UIInputField()
        {
            Width = 240f;
            Height = 34f;
        }
    }

    public sealed class UIScrollView : UIElement
    {
        public float ContentWidth = 400f;
        public float ContentHeight = 600f;
        public float ScrollX = 0f;
        public float ScrollY = 0f;
        public bool Horizontal = false;
        public bool Vertical = true;
        public float ScrollSensitivity = 24f;
        public bool ShowBackground = true;
        public float BackR = 0.08f, BackG = 0.08f, BackB = 0.09f, BackA = 0.78f;
        public float CornerRadius = 4f;

        public UIScrollView()
        {
            Width = 320f;
            Height = 220f;
        }
    }

    public class UIBar : UIElement
    {
        public float Value = 1f;
        public float FillR = 0.85f, FillG = 0.20f, FillB = 0.20f, FillA = 1f;
        public float BackR = 0.10f, BackG = 0.10f, BackB = 0.10f, BackA = 0.85f;
        public float Border = 2f;
        public float CornerRadius = 4f;
        public bool ShowValueText = false;
        public float TextR = 1f, TextG = 1f, TextB = 1f, TextA = 1f;

        public UIBar()
        {
            Width = 240f;
            Height = 22f;
        }
    }

    public static class UIEventSystem
    {
        public static GameObject? CurrentSelectedObject { get; private set; }
        public static UISelectable? CurrentSelectedSelectable { get; private set; }
        public static GameObject? PointerEnterObject { get; internal set; }
        public static GameObject? PointerPressObject { get; internal set; }
        public static bool SendNavigationEvents = true;
        public static bool PointerOverGameObject => UIRaycast.PointerOverUI;

        public static void SetSelected(UISelectable? selectable)
        {
            CurrentSelectedSelectable = selectable;
            CurrentSelectedObject = selectable?.gameObject;
        }

        public static void ClearSelection() => SetSelected(null);
    }

    public static class UIRaycast
    {
        public static GameObject? HoveredObject { get; private set; }
        public static GameObject? PressedObject { get; private set; }
        public static GameObject? LastClickedObject { get; private set; }
        public static UIElement? HoveredGraphic { get; private set; }
        public static UIElement? PressedGraphic { get; private set; }
        public static UISelectable? HoveredSelectable { get; private set; }
        public static UISelectable? PressedSelectable { get; private set; }
        public static UISelectable? LastClickedSelectable { get; private set; }
        public static UIButton? HoveredButton => HoveredSelectable as UIButton;
        public static UIButton? PressedButton => PressedSelectable as UIButton;
        public static UIButton? LastClickedButton => LastClickedSelectable as UIButton;
        public static bool PointerOverUI => HoveredGraphic != null;

        public static void SetPointerState(UIElement? hovered, UIElement? pressed, UISelectable? clicked)
        {
            HoveredGraphic = hovered;
            PressedGraphic = pressed;
            HoveredObject = hovered?.gameObject;
            PressedObject = pressed?.gameObject;
            HoveredSelectable = hovered as UISelectable;
            PressedSelectable = pressed as UISelectable;
            UIEventSystem.PointerEnterObject = HoveredObject;
            UIEventSystem.PointerPressObject = PressedObject;

            if (clicked != null)
            {
                LastClickedSelectable = clicked;
                LastClickedObject = clicked.gameObject;
            }
        }

        public static bool ConsumeClick(out UISelectable? selectable)
        {
            selectable = LastClickedSelectable;
            if (selectable == null)
                return false;
            LastClickedSelectable = null;
            LastClickedObject = null;
            return true;
        }

        public static bool ConsumeClick(out UIButton? button)
        {
            button = LastClickedSelectable as UIButton;
            if (button == null)
                return false;
            LastClickedSelectable = null;
            LastClickedObject = null;
            return true;
        }
    }
}
