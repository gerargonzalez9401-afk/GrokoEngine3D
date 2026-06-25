using GrokoEngine;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Concurrent;
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

internal sealed partial class ImGuiEditorApp : GameWindow
{
    private const float GizmoHitRadius = 8.5f;
    private static ImGuiEditorApp? currentDrawingApp;
    private static readonly System.Numerics.Vector4 UiAccent = new(0.18f, 0.40f, 0.64f, 1f);
    private static readonly System.Numerics.Vector4 UiAccentHover = new(0.24f, 0.48f, 0.72f, 1f);
    private static readonly System.Numerics.Vector4 UiAccentActive = new(0.28f, 0.56f, 0.84f, 1f);
    private static readonly System.Numerics.Vector4 UiPanel = new(0.20f, 0.20f, 0.20f, 1f);
    private static readonly System.Numerics.Vector4 UiPanelSoft = new(0.17f, 0.17f, 0.17f, 1f);
    private static readonly System.Numerics.Vector4 UiText = new(0.74f, 0.74f, 0.74f, 1f);
    private static readonly System.Numerics.Vector4 UiDanger = new(0.56f, 0.16f, 0.14f, 1f);
    private static readonly System.Numerics.Vector4 UiDangerHover = new(0.72f, 0.22f, 0.18f, 1f);
    private static readonly System.Numerics.Vector4 UiIcon = new(0.72f, 0.74f, 0.76f, 1f);
    private static readonly System.Numerics.Vector4 UiIconActive = new(0.90f, 0.94f, 1f, 1f);
    private readonly string projectPath;
    private readonly string rootAssetsPath;
    private readonly string scenePath;
    // Game Mode: cuando el .exe arranca como juego (game.json presente) en vez de editor.
    private readonly bool gameMode;
    private readonly GameLaunchConfig? gameConfig;
    private bool gameModeEscapeHeld;
    private readonly List<GameObject> objects = new();
    private readonly PhysicsEngine physicsEngine = new();
    private readonly ScriptCompiler scriptCompiler;
    private readonly EditorSceneGraph sceneGraph;
    private readonly SelectionService selection;
    // Reutilizado cada frame para evitar selection.Selected.Select(...).ToArray() en el hot path de render.
    private readonly HashSet<string> rendererSelectedObjectIds = new(StringComparer.Ordinal);
    private readonly AssetService assetService;
    private readonly SceneCommandHistory sceneHistory = new(128);
    private readonly Dictionary<string, int> assetPreviewTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> assetPreviewWriteTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> assetPreviewNextValidationUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IntPtr> assetPreviewFrameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> assetPreviewFrameMisses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MaterialPreviewSourceCache> materialPreviewSourceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureMetadata> textureMetadataCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan AssetPreviewValidationInterval = TimeSpan.FromSeconds(1);

    // Project Browser cache + virtualized drawing.
    // Similar philosophy to Unity: the UI draws only visible items, while previews are generated lazily.
    private readonly Dictionary<string, ProjectFolderCache> projectFolderCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> previewGenerationQueue = new();
    private readonly HashSet<string> previewGenerationQueued = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> previewGenerationFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object previewJobLock = new();
    private readonly HashSet<string> previewDiskJobsRunning = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> previewDiskReadyQueue = new();
    private const int PreviewJobsPerFrame = 1;
    private const int PreviewFrameBudgetMs = 2;
    private const int PreviewFrameSkipCount = 20;
    private const int MaxPreviewDiskJobs = 1;
    // Límite de solicitudes NUEVAS de thumbnail por frame. Aunque haya 80 tiles visibles,
    // solo se encolan unas pocas previews; lo demás dibuja icono fallback hasta el siguiente frame.
    private const int MaxPreviewQueueRequestsPerFrame = 2;
    private const int MaxAssetPreviewTextureCache = 192;
    private int previewQueueFrameSkip;
    private int assetPreviewCacheTrimCountdown;
    private int previewQueueRequestsThisFrame;

    // Cache de la vista filtrada/ordenada del Project Browser.
    // Evita ordenar y filtrar toda la carpeta cada frame.
    private string? projectVisibleEntriesCacheKey;
    private IReadOnlyList<ProjectAssetEntry> projectVisibleEntriesCache = Array.Empty<ProjectAssetEntry>();
    private IReadOnlyList<string> assetPickerFileCache = Array.Empty<string>();
    private bool assetPickerFileCacheDirty = true;
    private readonly Dictionary<string, PendingMaterialSave> pendingMaterialSaves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScriptableObject> scriptableObjectCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> pendingScriptableObjectSaves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<EditorIcon, IconAtlasRegion> iconAtlasRegions = new();
    private readonly Dictionary<string, bool> componentFoldoutStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> sectionFoldoutStates = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? scriptWatcher;
    private readonly object scriptCompileGate = new();
    private bool pendingScriptCompile;
    private bool pendingScriptCompileLogged;
    private string pendingScriptCompileReason = "script change";
    private DateTime lastScriptFileChangeUtc = DateTime.MinValue;
    private DateTime lastScriptCompileRequestUtc = DateTime.MinValue;
    private DateTime nextScriptSourceScanUtc = DateTime.MinValue;
    private string lastScriptSourceSignature = string.Empty;
    private const double ScriptCompileDebounceSeconds = 0.85;
    private const double ScriptSourceScanIntervalSeconds = 1.0;
    private const double ScriptCompileMinimumGapSeconds = 0.25;
    private int iconAtlasTexture;
    private bool iconAtlasReady;
    private readonly string imguiLayoutPath;
    private readonly string editorSettingsPath;
    // Bump este número cuando cambies el layout por defecto para forzar reset.
    private const int LayoutVersion = 5;
    private ImGuiController imgui = null!;
    private SceneViewportRenderer sceneRenderer = null!;
    private SceneRenderTarget sceneTarget = null!;
    private GameObject? selected
    {
        get => selection.Current;
        set => selection.SelectSingle(value);
    }
    private EditorCameraState camera = new();
    private bool vsync;
    // Anchos/altos de los paneles — se pueden arrastrar con los splitters
    private const float PremiumToolbarHeight = 34f;
    private float _panelLeftW = 250f;
    private float _panelRightW = 350f;
    private float _panelBottomH = 250f;
    // Pestaña activa del viewport: false = Scene (cámara libre + gizmos), true = Game (cámara del juego).
    private bool gameViewActive;
    private readonly List<ConsoleEntry> consoleLog = new();
    private readonly ConcurrentQueue<ConsoleEntry> pendingConsoleLog = new();
    private string statusMessageValue = "Ready";
    private string statusMessage
    {
        get => statusMessageValue;
        set => Log(value);
    }
    private bool simulatePhysics;
    private bool isPlaying;
    private bool playPaused;
    private string? playModeSnapshot;
    private List<string> playModeSelectionIds = new();
    private bool previousLeftMouseDown;
    private bool previousRightMouseDown;
    private bool previousMiddleMouseDown;
    private bool previousAltOrbitMouseDown;
    private bool selectionBoxActive;
    private Vector2 selectionBoxStart;
    private Vector2 selectionBoxEnd;
    private Vector2 lastMousePosition;
    private Vector3 sceneOrbitTarget;
    private float sceneOrbitDistance;
    private string? selectedAssetPath;
    private bool inspectorLocked;
    private GameObject? lockedInspectorObject;
    private string? lockedInspectorAssetPath;
    private string? draggingAssetPath;
    private string? materialPreviewCachePath;
    private string? materialPreviewCacheObjectId;
    private int materialPreviewCacheSubmeshIndex = -1;
    private double materialPreviewNextPickTime;
    private EditorProgressTask? pendingEditorProgressTask;
    private bool editorProgressVisible;
    private bool editorProgressRunning;
    private string editorProgressTitle = "";
    private string editorProgressDetail = "";
    private float editorProgressValue;
    private double editorProgressHideAfter;
    private string assetSlotSearch = string.Empty;
    private string objectSlotSearch = string.Empty;
    private string addComponentSearch = string.Empty;
    private bool addComponentSearchFocus;
    private string? lastFlashedAssetPath;
    private double lastAssetSelectionFlashTime;
    private string? currentProjectDirectory;
    private bool projectFolderHighlightActive = true; // resaltado de la carpeta actual en el árbol (se quita al clicar en vacío)
    private System.Numerics.Vector2 inspectorPanelMin; // rect del Inspector (para no deseleccionar al clicar en él)
    private System.Numerics.Vector2 inspectorPanelMax;
    private string projectSearch = string.Empty;
    private string? selectedProjectSubAssetKey;
    private readonly HashSet<string> selectedProjectEntryKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> projectAssetBoxSelectionBase = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProjectAssetSelectionRect> projectAssetSelectionRects = new();
    private readonly Dictionary<string, ProjectAssetEntry> projectAssetSelectionEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> pendingDeleteAssetPaths = new();
    private string? projectSelectionAnchorKey;
    private bool projectAssetBoxSelecting;
    private bool projectAssetBoxSelectAdditive;
    private Vector2 projectAssetBoxStart;
    private Vector2 projectAssetBoxCurrent;
    private readonly HashSet<string> expandedMeshAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    private int projectMeshExpansionVersion;
    private const float ProjectTileScaleMin = 0.65f;
    private const float ProjectTileScaleMax = 1.6f;
    private const float ProjectTileScaleWheelStep = 0.10f;
    private const float ProjectTileListThreshold = 0.75f;
    private float projectTileScale = 1f;
    private bool projectListView = false;
    private bool projectHoveredAnyAssetItem;
    private AssetSortMode projectSortMode = AssetSortMode.Name;
    private bool projectSortDescending;
    private bool showEditorSettings;
    private bool showGuiDesigner;
    private bool showShaderGraph;
    private bool showAnimationWindow;
    private bool showAnimatorGraph;
    private bool toolWindowMouseCapture; // true si el ratón está sobre una ventana flotante (Animator/Animation/ShaderGraph/etc.)
    private ShaderGraphModel? shaderGraphModel;
    private string? shaderGraphAssetPath;
    private string shaderGraphCode = string.Empty;
    private string shaderGraphStatus = string.Empty;
    private Vector2 shaderGraphPan = new(40f, 40f);
    private float shaderGraphZoom = 1f;
    private Guid? shaderGraphSelectedNodeId;
    private readonly HashSet<Guid> shaderGraphSelectedNodeIds = new();
    private readonly HashSet<Guid> shaderGraphBoxSelectionBase = new();
    private bool shaderGraphBoxSelecting;
    private bool shaderGraphBoxSelectAdditive;
    private Vector2 shaderGraphBoxSelectStart;
    private Vector2 shaderGraphBoxSelectCurrent;
    private Guid? shaderGraphDragFromPin;
    private Guid? shaderGraphHoverPin;
    private Vector2 shaderGraphCreatePos;
    private string shaderGraphCreateSearch = string.Empty;
    private readonly HashSet<NodeKind> shaderGraphFavoriteNodeKinds = new();
    private readonly List<NodeKind> shaderGraphRecentNodeKinds = new();
    private Guid? shaderGraphContextNodeId;
    private Guid? shaderGraphSelectedGroupId;
    private Guid? shaderGraphContextGroupId;
    private bool shaderGraphCenterPending = true;
    private bool shaderGraphFrameSelectedPending;
    private bool shaderGraphShowMiniMap = true;
    private Guid? shaderGraphSelectedPropertyId;
    private Guid? shaderGraphDraggingPropertyId;
    private bool shaderGraphMaximized;
    private Vector2 shaderGraphRestorePos;
    private Vector2 shaderGraphRestoreSize = new(960f, 680f);
    private float shaderGraphBlackboardWidth = 180f;
    private float shaderGraphInspectorWidth = 240f;
    private float shaderGraphPreviewWidth = 240f;
    private bool showShaderGraphInspector = true;
    private bool showShaderGraphPreview = true;
    private bool shaderGraphInspectorFloating;
    private bool shaderGraphPreviewFloating;
    private Vector2 shaderGraphInspectorFloatPos = new(60f, 40f);
    private Vector2 shaderGraphInspectorFloatSize = new(260f, 420f);
    private Vector2 shaderGraphPreviewFloatPos = new(340f, 40f);
    private Vector2 shaderGraphPreviewFloatSize = new(260f, 320f);
    private ShaderGraphPreview? shaderGraphPreview;
    private float shaderGraphPreviewYaw = 0.6f;
    private float shaderGraphPreviewPitch = -0.3f;
    private string shaderGraphPreviewShape = "Sphere";
    private string shaderGraphPreviewCustomMeshPath = string.Empty;
    private string shaderGraphPreviewError = string.Empty;
    private string shaderGraphPreviewedCode = string.Empty;
    private string shaderGraphPreviewRenderKey = string.Empty;
    private double shaderGraphPreviewLastRenderTime;
    private readonly Dictionary<Guid, ShaderGraphNodePreviewRenderer> shaderGraphNodePreviews = new();
    private readonly HashSet<Guid> shaderGraphCollapsedNodePreviews = new();
    private readonly Stack<string> shaderGraphUndoStack = new();
    private readonly Stack<string> shaderGraphRedoStack = new();
    private bool shaderGraphMoveUndoCaptured;
    private ShaderGraphPreview? materialAssetShaderPreview;
    private bool viewportGizmosVisible = true;
    private bool viewportAnimatorDebugVisible;
    private bool viewportShadedMode = true;
    private bool viewportLocalSpace;
    private bool viewportPivotCenter = true;
    private readonly List<ViewportResolutionPreset> viewportResolutionPresets = new();
    private string viewportResolutionPresetKey = "free";
    private float renderScale = 1f;
    private bool fxaaEnabled = true;
    private int bloomQuality = 2;
    private int ambientOcclusionQuality = 2;
    private float shadowBias = 1f;
    private float sceneCameraSpeed = 5f;
    private bool sceneGridVisible = true;
    private bool sceneGridSnapEnabled;
    private float sceneGridSize = 1f;
    private float sceneGridOpacity = 0.55f;
    private SceneGridAxis sceneGridAxis = SceneGridAxis.Y;
    private LightingDebugView lightingDebugView = LightingDebugView.Final;
    private ShadowQuality shadowQuality = ShadowQuality.High;
    private ColorSpace colorSpace = ColorSpace.Linear;
    private bool iblEnabled = true;
    private string hdriPath = "";
    private readonly EditorProfiler profiler = new();
    private bool showProfiler;
    private bool consoleShowInfo = true;
    private bool consoleShowWarnings = true;
    private bool consoleShowErrors = true;
    private string consoleSearch = string.Empty;
    private readonly Dictionary<string, bool> objectVisibility = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> objectLocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> objectActive = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> objectStatic = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> objectTag = new(StringComparer.OrdinalIgnoreCase);
    private double lastStatusFlashTime;
    private ConsoleSeverity lastStatusSeverity = ConsoleSeverity.Info;
    private int cachedObjectCount;
    private double lastObjectCountRefresh;
    private string? renameAssetPath;
    private string renameAssetName = string.Empty;
    private string? inlineRenameAssetPath;
    private string inlineRenameAssetName = string.Empty;
    private bool inlineRenameFocusPending;
    private string? lastAssetClickPath;
    private double lastAssetClickTime;
    private string renameObjectName = string.Empty;
    private string? pendingDeleteAssetPath;
    private bool viewportAssetDropArmed;
    private bool inspectorAssetDropArmed;
    private string hierarchyFilter = string.Empty;
    private string? hierarchyDragObjectId;
    private string? inlineRenameObjectId;
    private string inlineRenameObjectName = string.Empty;
    private bool inlineRenameObjectFocusPending;
    private string? lastObjectClickId;
    private double lastObjectClickTime;
    private Vector2 viewportMin;
    private Vector2 viewportMax;
    private Vector2 viewportPanelContentMin;
    private Vector2 viewportPanelContentSize = new(800, 450);
    private Vector2 viewportContentMin;
    private Vector2 viewportContentSize = new(800, 450);
    private bool viewportReady;
    private TransformTool currentTool = TransformTool.Move;
    private bool gizmoDragging;
    private bool gizmoMouseCaptured;
    private bool viewGizmoMouseCaptured; // ratón sobre el view gizmo (cubo de ejes) de la esquina
    private int gizmoAxis = -1;
    private Vector2 gizmoDragStartMouse;
    private float gizmoDragStartAxisDistance;
    private Vector3 gizmoDragStartWorldCenter;
    private TransformSnapshot gizmoDragStartTransform;
    private SceneStateSnapshot? gizmoUndoStart;
    private string? colliderEditObjectId;
    private int colliderEditHandle = -1;
    private bool colliderEditDragging;
    private bool colliderEditMouseCaptured;
    private Vector2 colliderEditDragStartMouse;
    private float colliderEditDragStartAxisDistance;
    private Vector3 colliderEditDragStartCenter;
    private Vector3 colliderEditDragStartSize;
    private SceneStateSnapshot? colliderEditUndoStart;
    private SceneStateSnapshot? pendingInspectorUndoStart;
    private string? pendingInspectorUndoObjectId;
    private SceneStateSnapshot? pendingNameUndoStart;
    private string? pendingNameUndoObjectId;
    private SceneStateSnapshot? pendingMaterialUndoStart;
    private string? pendingMaterialUndoObjectId;
    private bool previousGizmoLeftMouseDown;
    private bool suppressHistory;
    private static readonly TimeSpan MaterialSaveDelay = TimeSpan.FromMilliseconds(550);
    private float designerPanelPadding = 4f;
    private float designerRowHeight = 20f;
    private float designerSplitterSize = 3f;
    private float designerPanelAlpha = 1f;
    private float designerLabelRatio = 0.34f;
    private bool designerShowGuides;
    private bool guiInspectMode;
    private GuiStyleClass guiSelectedStyleClass = GuiStyleClass.Button;
    private GuiElementPickFilter guiElementPickFilter = GuiElementPickFilter.EditableOnly;
    private float guiFontScale = 1f;
    private float guiButtonHeight = 24f;
    private float guiButtonRounding = 3f;
    private float guiCheckboxSize = 12f;
    private float guiSliderHeight = 20f;
    private float guiAssetSlotHeight = 20f;
    private float guiLabelBrightness = 0.58f;

    private readonly struct TextureMetadata
    {
        public TextureMetadata(DateTime writeTime, int width, int height, string format)
        {
            WriteTime = writeTime;
            Width = width;
            Height = height;
            Format = format;
        }

        public DateTime WriteTime { get; }
        public int Width { get; }
        public int Height { get; }
        public string Format { get; }
    }

    private sealed class ProjectAssetEntry
    {
        public required string Path { get; init; }
        public required string Name { get; init; }
        public required string Kind { get; init; }
        public bool IsDirectory { get; init; }
        public long SizeBytes { get; init; }
        public DateTime ModifiedUtc { get; init; }
        public bool IsVirtualSubAsset { get; init; }
        public string? ParentPath { get; init; }
        public int SubmeshIndex { get; init; } = -1;
        public int AnimationClipIndex { get; init; } = -1;
        public string? SourceMaterialPath { get; init; }
        public string? SourceAvatarPath { get; init; }
    }

    private readonly struct ProjectAssetSelectionRect
    {
        public ProjectAssetSelectionRect(string key, Vector2 min, Vector2 max)
        {
            Key = key;
            Min = min;
            Max = max;
        }

        public string Key { get; }
        public Vector2 Min { get; }
        public Vector2 Max { get; }
    }

    private sealed class ProjectFolderCache
    {
        public DateTime DirectoryWriteUtc { get; set; }
        public DateTime NextValidationUtc { get; set; }
        public IReadOnlyList<ProjectAssetEntry>? Directories { get; set; }
        public required List<ProjectAssetEntry> Entries { get; init; }
    }

    private sealed class MaterialPreviewSourceCache
    {
        public DateTime MaterialWriteUtc { get; init; }
        public bool HasShaderGraph { get; init; }
        public MaterialAssetData Data { get; init; } = new();
        public string ShaderGraphPath { get; init; } = string.Empty;
        public DateTime ShaderGraphWriteUtc { get; init; }
        public DateTime DependencyStamp { get; init; }
        public DateTime NextValidationUtc { get; init; }
    }


    public ImGuiEditorApp(string projectPath)
        : base(
            GameWindowSettings.Default,
            new NativeWindowSettings
            {
                Title = "GrokoEngine — " + Path.GetFileName(projectPath),
                ClientSize = new Vector2i(1600, 900),
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                WindowState = OpenTK.Windowing.Common.WindowState.Maximized
            })
    {
        this.projectPath = projectPath;
        rootAssetsPath = Path.Combine(projectPath, "Assets");
        scenePath = Path.Combine(rootAssetsPath, "Scenes", "Main.gscene");
        imguiLayoutPath = Path.Combine(projectPath, ".groko-imgui-layout.ini");
        editorSettingsPath = Path.Combine(projectPath, ".groko-editor-settings.json");
        scriptCompiler = new ScriptCompiler(rootAssetsPath);
        sceneGraph = new EditorSceneGraph(objects, physicsEngine);
        selection = new SelectionService(sceneGraph);
        assetService = new AssetService(rootAssetsPath);
    }

    // Constructor de JUEGO (Game Mode): arranca como juego a pantalla, sin UI de editor.
    // Lo usa Program.Main cuando hay un game.json junto al ejecutable.
    public ImGuiEditorApp(string projectPath, GameLaunchConfig game)
        : base(
            GameWindowSettings.Default,
            new NativeWindowSettings
            {
                Title = game.Title,
                ClientSize = new Vector2i(game.Width, game.Height),
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                WindowState = game.Fullscreen
                    ? OpenTK.Windowing.Common.WindowState.Fullscreen
                    : OpenTK.Windowing.Common.WindowState.Normal
            })
    {
        gameMode = true;
        gameConfig = game;
        this.projectPath = projectPath;
        rootAssetsPath = Path.Combine(projectPath, "Assets");
        // Escena de inicio definida por el juego exportado (relativa a la carpeta del juego).
        scenePath = Path.GetFullPath(Path.Combine(projectPath, game.StartupScene));
        imguiLayoutPath = Path.Combine(projectPath, ".groko-imgui-layout.ini");
        editorSettingsPath = Path.Combine(projectPath, ".groko-editor-settings.json");
        scriptCompiler = new ScriptCompiler(rootAssetsPath);
        sceneGraph = new EditorSceneGraph(objects, physicsEngine);
        selection = new SelectionService(sceneGraph);
        assetService = new AssetService(rootAssetsPath);
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        Program.UpdateSplash("Initializing editor window", 0.08f);
        VSync = VSyncMode.Off;
        GL.ClearColor(0.105f, 0.115f, 0.125f, 1f);
        imgui = new ImGuiController(ClientSize.X, ClientSize.Y);

        // Comportamiento tipo Unity Pro: paneles no se despegan con un click accidental.
        // Solo se pueden desacoplar manteniendo Shift + arrastrar el título.
        var io = ImGui.GetIO();
        io.ConfigWindowsMoveFromTitleBarOnly = true;   // solo arrastrar desde el título
        io.ConfigDockingWithShift = true;              // Shift requerido para desacoplar

        BuildViewportResolutionPresets();
        LoadEditorSettings();

        // Game Mode: el HDRI exportado se guarda con ruta relativa (portable); resolverla
        // contra la carpeta del juego para que el skybox/IBL cargue.
        if (gameMode && !string.IsNullOrWhiteSpace(hdriPath) && !Path.IsPathRooted(hdriPath))
            hdriPath = Path.GetFullPath(Path.Combine(projectPath, hdriPath));
        Program.UpdateSplash("Creating editor icons", 0.18f);
        CreateIconAtlas();
        LoadImGuiLayout();
        Program.UpdateSplash("Initializing renderer", 0.32f);
        sceneRenderer = new SceneViewportRenderer();
        sceneTarget = new SceneRenderTarget();
        camera.SetLookDirection(camera.Front);
        GrokoEngine.Debug.OnLogMessage += HandleEngineLogMessage;
        scriptCompiler.OnLog += (message, isError) => Log(message, isError ? ConsoleSeverity.Error : ConsoleSeverity.Info);
        statusMessage = "Editor loaded";
        Program.UpdateSplash("Generating script project", 0.48f);
        scriptCompiler.Initialize();
        Program.UpdateSplash("Compiling scripts", 0.62f);
        scriptCompiler.Compile();
        Program.UpdateSplash("Indexing asset database", 0.72f);
        assetService.AssetDatabase.Refresh(createMissingMeta: true);
        Program.UpdateSplash("Loading scene", 0.78f);
        LoadScene();
        Program.UpdateSplash("Prewarming scene materials", 0.82f);
        PrewarmSceneRenderableAssetsDuringLoad();
        Program.UpdateSplash("Loading textures and materials", 0.86f);
        BuildProjectPreviewCacheDuringLoad();
        Program.UpdateSplash("Starting asset watchers", 0.94f);
        StartScriptWatcher();
        Program.UpdateSplash("Ready", 1f);
        Program.CloseSplash();

        // Game Mode: arrancar directamente en Play (sin interacción del editor).
        if (gameMode)
            EnterPlayMode();
    }

    protected override void OnUnload()
    {
        FlushPendingMaterialSaves();
        FlushPendingScriptableObjectSaves();
        StopScriptWatcher();
        GrokoEngine.Debug.OnLogMessage -= HandleEngineLogMessage;
        foreach (int texture in assetPreviewTextures.Values)
            if (texture != 0)
                GL.DeleteTexture(texture);
        SaveImGuiLayout();
        SaveEditorSettings();
        if (iconAtlasTexture != 0)
            GL.DeleteTexture(iconAtlasTexture);
        assetPreviewTextures.Clear();
        foreach (var preview in shaderGraphNodePreviews.Values)
            preview.Dispose();
        shaderGraphNodePreviews.Clear();
        shaderGraphCollapsedNodePreviews.Clear();
        assetPreviewWriteTimes.Clear();
        assetPreviewNextValidationUtc.Clear();
        materialPreviewSourceCache.Clear();
        textureMetadataCache.Clear();
        projectFolderCache.Clear();
        previewGenerationQueue.Clear();
        previewGenerationQueued.Clear();
        previewGenerationFailures.Clear();
        iconAtlasRegions.Clear();
        imgui.Dispose();
        sceneRenderer.Dispose();
        sceneTarget.Dispose();
        scriptCompiler.Dispose();
        base.OnUnload();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        imgui.WindowResized(ClientSize.X, ClientSize.Y);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        long updateStart = EditorProfiler.Timestamp();
        base.OnUpdateFrame(args);
        DrainPendingConsoleLog();
        ProcessQueuedEditorProgressTask();

        // Game Mode: solo corre el loop de juego (sin guardado de assets, autocompile,
        // previews ni input de editor). Permite salir con Escape.
        if (gameMode)
        {
            bool escapeDown = KeyboardState.IsKeyDown(GlfwKeys.Escape);
            if (escapeDown && !gameModeEscapeHeld)
            {
                if (Input.CursorLocked)
                {
                    Input.UnlockCursor();
                    ApplyEngineCursorState();
                }
                else
                {
                    Close();
                }
            }
            gameModeEscapeHeld = escapeDown;
            FeedEngineInput();
            StepRuntime(args.Time);
            ApplyEngineCursorState();
            Input.Flush();
            profiler.SampleUpdate(EditorProfiler.ElapsedMs(updateStart));
            return;
        }

        long assetStart = EditorProfiler.Timestamp();
        ProcessPendingMaterialSaves();
        ProcessPendingScriptableObjectSaves();
        profiler.SampleAssetSaves(EditorProfiler.ElapsedMs(assetStart));

        long compileStart = EditorProfiler.Timestamp();
        ProcessScriptAutoCompile();
        profiler.SampleScriptCompile(EditorProfiler.ElapsedMs(compileStart));

        // Genera thumbnails en modo idle, sin bloquear el Project Browser.
        long previewStart = EditorProfiler.Timestamp();
        ProcessPreviewGenerationQueue();
        profiler.SamplePreview(EditorProfiler.ElapsedMs(previewStart));

        long editorInputStart = EditorProfiler.Timestamp();
        HandleCameraInput((float)args.Time);
        HandleViewportSelection();
        HandleTerrainSculpting((float)args.Time);
        profiler.SampleEditorInput(EditorProfiler.ElapsedMs(editorInputStart));

        if ((isPlaying && !playPaused) || simulatePhysics)
        {
            FeedEngineInput();          // pasa el teclado/ratón reales al motor (Input.GetKey, etc.)
            StepRuntime(args.Time);
            ApplyEngineCursorState();
            Input.Flush();              // limpia los estados one-shot (GetKeyDown/Up)
        }
        else
        {
            if (Input.CursorLocked)
            {
                Input.UnlockCursor();
                ApplyEngineCursorState();
            }

            long particleStart = EditorProfiler.Timestamp();
            StepEditorParticles(args.Time);
            profiler.SampleParticles(EditorProfiler.ElapsedMs(particleStart));

            StepAnimationPreview(args.Time);
        }

        profiler.SampleUpdate(EditorProfiler.ElapsedMs(updateStart));
    }

    // Mapa de teclas físicas (GLFW) → KeyCode del motor.
    private static readonly (GlfwKeys Glfw, KeyCode Code)[] EngineKeyMap =
    {
        (GlfwKeys.W, KeyCode.W), (GlfwKeys.A, KeyCode.A), (GlfwKeys.S, KeyCode.S), (GlfwKeys.D, KeyCode.D),
        (GlfwKeys.Q, KeyCode.Q), (GlfwKeys.E, KeyCode.E), (GlfwKeys.R, KeyCode.R), (GlfwKeys.F, KeyCode.F),
        (GlfwKeys.Z, KeyCode.Z), (GlfwKeys.X, KeyCode.X), (GlfwKeys.C, KeyCode.C), (GlfwKeys.V, KeyCode.V),
        (GlfwKeys.Tab, KeyCode.Tab),
        (GlfwKeys.Left, KeyCode.Left), (GlfwKeys.Right, KeyCode.Right), (GlfwKeys.Up, KeyCode.Up), (GlfwKeys.Down, KeyCode.Down),
        (GlfwKeys.Space, KeyCode.Space), (GlfwKeys.Escape, KeyCode.Escape), (GlfwKeys.Enter, KeyCode.Enter), (GlfwKeys.Backspace, KeyCode.Backspace),
        (GlfwKeys.LeftShift, KeyCode.LeftShift), (GlfwKeys.RightShift, KeyCode.RightShift),
        (GlfwKeys.LeftControl, KeyCode.LeftControl), (GlfwKeys.RightControl, KeyCode.RightControl),
        (GlfwKeys.LeftAlt, KeyCode.LeftAlt), (GlfwKeys.RightAlt, KeyCode.RightAlt),
        (GlfwKeys.Delete, KeyCode.Delete), (GlfwKeys.Home, KeyCode.Home), (GlfwKeys.End, KeyCode.End),
    };

    // Vuelca el estado real del teclado/ratón al sistema de Input del motor (solo en Play).
    private void FeedEngineInput()
    {
        var kb = KeyboardState;
        foreach (var (glfw, code) in EngineKeyMap)
            Input.RegisterKeyState(code, kb.IsKeyDown(glfw));

        Input.RegisterMouseState(0, MouseState.IsButtonDown(GlfwMouseButton.Left));
        Input.RegisterMouseState(1, MouseState.IsButtonDown(GlfwMouseButton.Right));
        Input.RegisterMouseState(2, MouseState.IsButtonDown(GlfwMouseButton.Middle));
        if (Input.CursorLocked)
            Input.RegisterMouseDelta(MouseState.Delta.X, MouseState.Delta.Y);
        else
            Input.RegisterMouseMove(MouseState.X, MouseState.Y);
        Input.RegisterMouseScroll(MouseState.ScrollDelta.Y);
    }

    private void ApplyEngineCursorState()
    {
        var desiredState = Input.CursorLocked && (isPlaying || gameMode)
            ? CursorState.Grabbed
            : CursorState.Normal;

        if (CursorState != desiredState)
            CursorState = desiredState;
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        long frameStart = EditorProfiler.Timestamp();
        base.OnRenderFrame(args);
        DrainPendingConsoleLog();
        RenderSceneTexture();

        // Game Mode: presentar el juego a pantalla completa, sin UI de editor.
        if (gameMode)
        {
            sceneTarget.PresentToScreen(ClientSize.X, ClientSize.Y);
            // HUD del juego (Canvas/UI) por encima, vía ImGui (solo la UI, sin paneles de editor).
            imgui.Update(this, (float)args.Time);
            RenderCanvasUI(ImGui.GetForegroundDrawList(), System.Numerics.Vector2.Zero,
                new System.Numerics.Vector2(ClientSize.X, ClientSize.Y));
            imgui.Render();
            SwapBuffers();
            profiler.SampleFrame(EditorProfiler.ElapsedMs(frameStart));
            return;
        }

        long uiStart = EditorProfiler.Timestamp();
        imgui.Update(this, (float)args.Time);
        assetPreviewFrameCache.Clear();
        assetPreviewFrameMisses.Clear();
        previewQueueRequestsThisFrame = 0;
        gizmoMouseCaptured = gizmoDragging;
        HandleEditorShortcuts();
        DrawDockspace();
        profiler.SampleUiBuild(EditorProfiler.ElapsedMs(uiStart));

        long uiDrawStart = EditorProfiler.Timestamp();
        imgui.Render();
        profiler.SampleUiDraw(EditorProfiler.ElapsedMs(uiDrawStart));
        profiler.SampleUi(EditorProfiler.ElapsedMs(uiStart));

        long swapStart = EditorProfiler.Timestamp();
        SwapBuffers();
        profiler.SampleSwap(EditorProfiler.ElapsedMs(swapStart));
        profiler.SampleFrame(EditorProfiler.ElapsedMs(frameStart));
        CaptureProfilerFrameState();
    }

    private void RenderSceneTexture()
    {
        long renderStart = EditorProfiler.Timestamp();
        var renderSize = GetViewportRenderSize();
        int width = renderSize.Width;
        int height = renderSize.Height;
        profiler.RenderWidth = width;
        profiler.RenderHeight = height;

        // Vista de juego: en Play o en la pestaña "Game" → cámara del juego, sin gizmos/grid.
        bool gameView = isPlaying || gameViewActive;
        EditorCameraState activeCam;
        bool noGameCamera = false;
        if (gameView)
        {
            var gameCamera = FindGameCamera(objects);
            activeCam = gameCamera != null ? ComputeGameCameraState(gameCamera) : camera;
            // Sin cámara activa (ninguna o todas desactivadas): Game view en negro, como Unity
            // ("No cameras rendering"). No caemos a la cámara del editor.
            noGameCamera = gameCamera == null;
            // Fondo negro como en la Game view de Unity.
            GL.ClearColor(0f, 0f, 0f, 1f);
        }
        else
        {
            activeCam = camera;
            GL.ClearColor(0.075f, 0.095f, 0.120f, 1f);
        }

        // La UI usa esta misma cámara para Screen Space - Camera y World Space.
        activeUiCameraState = activeCam;

        int samples = activeCam.AntiAliasing ? activeCam.AntiAliasingSamples : 1;
        sceneTarget.Resize(width, height, samples);
        sceneTarget.Bind();

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        sceneRenderer.DebugView = (int)lightingDebugView;
        sceneRenderer.ShadowQuality = shadowQuality;
        sceneRenderer.ShadowBias = shadowBias;
        sceneRenderer.ColorSpace = colorSpace;
        sceneRenderer.ImageBasedLighting = iblEnabled;
        sceneRenderer.EnvironmentHdriPath = hdriPath;
        sceneRenderer.RenderRealtimeShadows = gameView;
        sceneRenderer.ShadowUpdateIntervalFrames = gameView ? 3 : 1;
        sceneRenderer.SceneGridAxis = ToRendererGridAxis(sceneGridAxis);
        sceneRenderer.SceneGridSize = sceneGridSize;
        sceneRenderer.SceneGridOpacity = sceneGridOpacity;
        rendererSelectedObjectIds.Clear();
        foreach (var selectedObject in selection.Selected)
            rendererSelectedObjectIds.Add(selectedObject.EditorId);
        sceneRenderer.SelectedObjectIds = rendererSelectedObjectIds;
        long sceneRendererStart = EditorProfiler.Timestamp();
        // noGameCamera: no dibujamos la escena → la Game view queda en el clear negro (como Unity).
        if (!noGameCamera)
            sceneRenderer.Render(objects, gameView ? null : selected, activeCam, width, height, showGrid: !gameView && sceneGridVisible);
        profiler.SampleSceneRenderer(EditorProfiler.ElapsedMs(sceneRendererStart));
        sceneTarget.Resolve();
        var postSettings = FindActiveComponent<PostProcessSettings>(objects);
        bool postEnabled = postSettings?.PostProcessEnabled ?? false;
        sceneTarget.FxaaEnabled = fxaaEnabled;
        // Si no hay PostProcess activo, no envíes calidades altas al shader: evita que
        // una configuración global dispare loops de bloom/AO innecesarios por accidente.
        sceneTarget.BloomQuality = postEnabled ? bloomQuality : 0;
        sceneTarget.AmbientOcclusionQuality = postEnabled ? ambientOcclusionQuality : 0;
        // God rays del sol (independiente del PostProcess): el renderer calculo la pos del sol en pantalla.
        sceneTarget.GodRaysEnabled = sceneRenderer.SunGodRaysActive;
        sceneTarget.GodRaySunU = sceneRenderer.SunScreenUv.X;
        sceneTarget.GodRaySunV = sceneRenderer.SunScreenUv.Y;
        sceneTarget.GodRayStrength = sceneRenderer.SunGodRaysStrength;
        long postProcessStart = EditorProfiler.Timestamp();
        sceneTarget.ApplyPostProcess(postSettings);
        profiler.SamplePostProcess(EditorProfiler.ElapsedMs(postProcessStart));
        SceneRenderTarget.Unbind(ClientSize.X, ClientSize.Y);
        GL.ClearColor(0.105f, 0.115f, 0.125f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        profiler.SampleRenderScene(EditorProfiler.ElapsedMs(renderStart));
    }

    private (int Width, int Height) GetViewportRenderSize()
    {
        // Game Mode: renderiza al tamaño real de la ventana (no hay panel de viewport ImGui).
        if (gameMode)
            return (Math.Max(1, ClientSize.X), Math.Max(1, ClientSize.Y));

        var preset = GetCurrentViewportResolutionPreset();
        if (!preset.Free)
            return (
                Math.Max(1, (int)MathF.Round(preset.Width * renderScale)),
                Math.Max(1, (int)MathF.Round(preset.Height * renderScale)));

        int width = Math.Max(1, (int)MathF.Round(viewportPanelContentSize.X * renderScale));
        int height = Math.Max(1, (int)MathF.Round(viewportPanelContentSize.Y * renderScale));
        return (width, height);
    }

    private ViewportResolutionPreset GetCurrentViewportResolutionPreset()
    {
        if (viewportResolutionPresets.Count == 0)
            return new ViewportResolutionPreset("free", "Free Aspect", 0, 0, true);

        foreach (var preset in viewportResolutionPresets)
            if (preset.Key == viewportResolutionPresetKey)
                return preset;

        viewportResolutionPresetKey = "free";
        return viewportResolutionPresets[0];
    }

    private void BuildViewportResolutionPresets()
    {
        viewportResolutionPresets.Clear();
        viewportResolutionPresets.Add(new ViewportResolutionPreset("free", "Free Aspect", 0, 0, true));

        AddViewportResolutionPreset("common:16-9:1280x720", "16:9 1280x720", 1280, 720);
        AddViewportResolutionPreset("common:16-9:1920x1080", "16:9 1920x1080", 1920, 1080);
        AddViewportResolutionPreset("common:16-9:2560x1440", "16:9 2560x1440", 2560, 1440);
        AddViewportResolutionPreset("common:16-9:3840x2160", "16:9 3840x2160", 3840, 2160);
        AddViewportResolutionPreset("common:16-10:1920x1200", "16:10 1920x1200", 1920, 1200);
        AddViewportResolutionPreset("common:4-3:1024x768", "4:3 1024x768", 1024, 768);
        AddViewportResolutionPreset("common:1-1:1080x1080", "Square 1080x1080", 1080, 1080);
        AddViewportResolutionPreset("common:mobile:720x1280", "Mobile 720x1280", 720, 1280);
        AddViewportResolutionPreset("common:mobile:1080x1920", "Mobile 1080x1920", 1080, 1920);

        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var bounds = screens[i].Bounds;
                AddViewportResolutionPreset($"monitor:{i}:{bounds.Width}x{bounds.Height}", $"Monitor {i + 1} {bounds.Width}x{bounds.Height}", bounds.Width, bounds.Height);
            }
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudieron leer las resoluciones del monitor: " + ex.Message);
        }
    }

    private void AddViewportResolutionPreset(string key, string label, int width, int height)
    {
        if (width <= 0 || height <= 0 || viewportResolutionPresets.Any(p => p.Key == key))
            return;

        viewportResolutionPresets.Add(new ViewportResolutionPreset(key, label, width, height, false));
    }

    private static string ShadowQualityDescription(ShadowQuality quality) => quality switch
    {
        ShadowQuality.Low    => "Low — 1024/512/256 px shadow maps, hard-edged (1 sample). Cheapest option, closest to Unity's \"Hard Shadows Only\".",
        ShadowQuality.Medium => "Medium — 1536/1024/512 px shadow maps, soft 3x3 PCF. Balanced default for most scenes.",
        ShadowQuality.High   => "High — 2048/1536/1024 px shadow maps, 4 directional cascades, soft 3x3 PCF. Sharp, Unity-like shadows for hero scenes.",
        ShadowQuality.Ultra  => "Ultra — 4096/2048/1024 px shadow maps, 5 directional cascades, crisp 3x3 PCF. Maximum fidelity without wasting GPU.",
        _ => string.Empty
    };

    private static string ColorSpaceDescription(ColorSpace space) => space switch
    {
        ColorSpace.Linear => "Linear — physically correct PBR workflow: textures/colors are decoded sRGB→linear before lighting and re-encoded linear→sRGB at the end. Matches Unity's default \"Linear\" color space; lighting falloffs and material response look correct and consistent.",
        ColorSpace.Gamma  => "Gamma — legacy workflow: textures are NOT decoded (lighting runs on the raw values), but the result is still encoded for display so the image stays bright. Matches Unity's old \"Gamma\" color space; brighter, more contrasty/saturated look, though the lighting math is physically less accurate. Only the input decode differs from Linear.",
        _ => string.Empty
    };

    private static SceneViewportRenderer.GridAxis ToRendererGridAxis(SceneGridAxis axis) => axis switch
    {
        SceneGridAxis.X => SceneViewportRenderer.GridAxis.X,
        SceneGridAxis.Z => SceneViewportRenderer.GridAxis.Z,
        SceneGridAxis.All => SceneViewportRenderer.GridAxis.All,
        _ => SceneViewportRenderer.GridAxis.Y
    };

    private static T? FindActiveComponent<T>(IEnumerable<GameObject> source) where T : Component
    {
        foreach (var obj in source)
        {
            if (!obj.IsActive) continue;
            // Respeta el checkbox Enabled del componente (header): desactivarlo lo apaga.
            if (obj.GetComponent<T>() is { Enabled: true } component)
                return component;
            var child = FindActiveComponent<T>(obj.Children);
            if (child != null)
                return child;
        }
        return null;
    }

    private GameObject? FindGameCamera(IEnumerable<GameObject> objs)
    {
        foreach (var obj in objs)
        {
            if (!obj.IsActive) continue;
            // Respeta el checkbox Enabled del componente Camera: una cámara desactivada no renderiza.
            var camComp = obj.GetComponent<Camera>();
            bool usableCamera = camComp != null ? camComp.Enabled : obj.IsCamera;
            if (usableCamera)
                return obj;
            var found = FindGameCamera(obj.Children);
            if (found != null) return found;
        }
        return null;
    }

    private EditorCameraState ComputeGameCameraState(GameObject cam)
    {
        Camera? cameraComponent = cam.GetComponent<Camera>();
        float fov = cameraComponent?.FOV ?? 60f;

        // Posición mundial: raíz usa PosX/Y/Z directamente.
        // Hijo usa GlobalPosition solo si fue actualizado (distinto de cero o la posición local no es cero).
        Vector3 pos;
        if (cam.Parent == null)
        {
            pos = new Vector3(cam.PosX, cam.PosY, cam.PosZ);
        }
        else
        {
            var gp = cam.GlobalPosition;
            bool globalValid = gp.X != 0f || gp.Y != 0f || gp.Z != 0f
                               || (cam.PosX == 0f && cam.PosY == 0f && cam.PosZ == 0f);
            pos = globalValid
                ? new Vector3(gp.X, gp.Y, gp.Z)
                : new Vector3(cam.PosX, cam.PosY, cam.PosZ);
        }

        // Misma convención que el resto del motor (luces, spotlights): RotY=0 => Front=(0,0,1).
        // Yaw = 90 - RotY   → RotY=0 => Front=(0,0,1)
        // Pitch = -RotX     → RotX+ => mira hacia abajo
        var state = new EditorCameraState
        {
            Position = pos,
            FOV = Math.Clamp(fov, 5f, 170f),
            NearClip = cameraComponent?.NearClip ?? 0.01f,
            FarClip = cameraComponent?.FarClip ?? 2000f,
            Yaw = 90f - cam.RotY,
            Pitch = Math.Clamp(-cam.RotX, -89f, 89f),
            Up = new Vector3(0f, 1f, 0f),
            AntiAliasing = cameraComponent?.AntiAliasing ?? true,
            AntiAliasingSamples = cameraComponent?.AntiAliasingSamples ?? 4,
            FrustumCulling = cameraComponent?.FrustumCulling ?? true,
            OcclusionCulling = cameraComponent?.OcclusionCulling ?? false
        };
        state.UpdateFront();
        return state;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        imgui.AddInputText(e.AsString);
    }

    protected override void OnFileDrop(FileDropEventArgs e)
    {
        base.OnFileDrop(e);
        ImportExternalFiles(e.FileNames);
    }

    private void LoadImGuiLayout()
    {
        if (!File.Exists(imguiLayoutPath))
            return;

        // Si el layout es de una versión anterior al número actual, lo descartamos
        // para que BuildInitialDockLayout construya el nuevo layout Unity Pro.
        string versionPath = imguiLayoutPath + ".ver";
        if (!File.Exists(versionPath) ||
            !int.TryParse(File.ReadAllText(versionPath).Trim(), out int savedVersion) ||
            savedVersion < LayoutVersion)
        {
            try { File.Delete(imguiLayoutPath); } catch { }
            try { File.Delete(versionPath); } catch { }
            return;
        }

        try
        {
            typeof(ImGui).GetMethod("LoadIniSettingsFromDisk", new[] { typeof(string) })?.Invoke(null, new object[] { imguiLayoutPath });
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"No se pudo cargar el layout de ImGui: {ex.Message}");
        }
    }

    private void SaveImGuiLayout()
    {
        try
        {
            typeof(ImGui).GetMethod("SaveIniSettingsToDisk", new[] { typeof(string) })?.Invoke(null, new object[] { imguiLayoutPath });
            File.WriteAllText(imguiLayoutPath + ".ver", LayoutVersion.ToString());
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"No se pudo guardar el layout de ImGui: {ex.Message}");
        }
    }

    private void LoadEditorSettings()
    {
        try
        {
            if (!File.Exists(editorSettingsPath))
                return;

            var settings = JsonSerializer.Deserialize<EditorSettingsData>(File.ReadAllText(editorSettingsPath));
            if (settings == null)
                return;

            vsync = settings.VSync;
            VSync = vsync ? VSyncMode.On : VSyncMode.Off;
            viewportGizmosVisible = settings.ViewportGizmosVisible;
            viewportAnimatorDebugVisible = settings.ViewportAnimatorDebugVisible;
            viewportShadedMode = settings.ViewportShadedMode;
            viewportLocalSpace = settings.ViewportLocalSpace;
            viewportPivotCenter = settings.ViewportPivotCenter;
            viewportResolutionPresetKey = viewportResolutionPresets.Any(p => p.Key == settings.ViewportResolutionPreset)
                ? settings.ViewportResolutionPreset
                : "free";
            renderScale = Math.Clamp(settings.RenderScale, 0.25f, 2f);
            fxaaEnabled = settings.FxaaEnabled;
            bloomQuality = Math.Clamp(settings.BloomQuality, 0, 3);
            ambientOcclusionQuality = Math.Clamp(settings.AmbientOcclusionQuality, 0, 3);
            shadowBias = Math.Clamp(settings.ShadowBias, 0.1f, 4f);
            sceneGridVisible = settings.SceneGridVisible;
            sceneGridSnapEnabled = settings.SceneGridSnapEnabled;
            sceneGridSize = Math.Clamp(settings.SceneGridSize, 0.01f, 1024f);
            sceneGridOpacity = Math.Clamp(settings.SceneGridOpacity, 0f, 1f);
            sceneGridAxis = Enum.TryParse<SceneGridAxis>(settings.SceneGridAxis, out var gridAxis) ? gridAxis : SceneGridAxis.Y;
            lightingDebugView = Enum.TryParse<LightingDebugView>(settings.LightingDebugView, out var debugView) ? debugView : LightingDebugView.Final;
            shadowQuality = Enum.TryParse<ShadowQuality>(settings.ShadowQuality, out var shadowQ) ? shadowQ : ShadowQuality.High;
            colorSpace = Enum.TryParse<ColorSpace>(settings.ColorSpace, out var colorSp) ? colorSp : ColorSpace.Linear;
            iblEnabled = settings.ImageBasedLighting;
            hdriPath = settings.HdriPath ?? "";
            projectListView = settings.ProjectListView;
            projectTileScale = Math.Clamp(settings.ProjectTileScale, ProjectTileScaleMin, ProjectTileScaleMax);
            projectSortMode = Enum.TryParse<AssetSortMode>(settings.ProjectSortMode, out var mode) ? mode : AssetSortMode.Name;
            projectSortDescending = settings.ProjectSortDescending;
            _panelLeftW = Math.Clamp(settings.PanelLeftWidth, 120f, 900f);
            _panelRightW = Math.Clamp(settings.PanelRightWidth, 140f, 900f);
            _panelBottomH = Math.Clamp(settings.PanelBottomHeight, 60f, 700f);
            designerPanelPadding = Math.Clamp(settings.GuiPanelPadding, 0f, 14f);
            designerRowHeight = Math.Clamp(settings.GuiRowHeight, 16f, 30f);
            designerSplitterSize = Math.Clamp(settings.GuiSplitterSize, 1f, 10f);
            designerPanelAlpha = Math.Clamp(settings.GuiPanelAlpha, 0.55f, 1f);
            designerLabelRatio = Math.Clamp(settings.GuiLabelRatio, 0.22f, 0.48f);
            designerShowGuides = settings.GuiShowGuides;
            guiSelectedStyleClass = Enum.TryParse<GuiStyleClass>(settings.GuiSelectedStyleClass, out var selectedGuiClass) ? selectedGuiClass : GuiStyleClass.Button;
            guiElementPickFilter = Enum.TryParse<GuiElementPickFilter>(settings.GuiElementPickFilter, out var pickFilter) ? pickFilter : GuiElementPickFilter.EditableOnly;
            guiFontScale = Math.Clamp(settings.GuiFontScale, 0.8f, 1.35f);
            guiButtonHeight = Math.Clamp(settings.GuiButtonHeight, 18f, 38f);
            guiButtonRounding = Math.Clamp(settings.GuiButtonRounding, 0f, 12f);
            guiCheckboxSize = Math.Clamp(settings.GuiCheckboxSize, 8f, 20f);
            guiSliderHeight = Math.Clamp(settings.GuiSliderHeight, 16f, 30f);
            guiAssetSlotHeight = Math.Clamp(settings.GuiAssetSlotHeight, 16f, 30f);
            guiLabelBrightness = Math.Clamp(settings.GuiLabelBrightness, 0.35f, 0.95f);
            consoleShowInfo = settings.ConsoleShowInfo;
            consoleShowWarnings = settings.ConsoleShowWarnings;
            consoleShowErrors = settings.ConsoleShowErrors;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"No se pudieron cargar los ajustes del editor ({editorSettingsPath}): {ex.Message}");
        }
    }

    private void SaveEditorSettings()
    {
        try
        {
            var settings = new EditorSettingsData
            {
                VSync = vsync,
                ViewportGizmosVisible = viewportGizmosVisible,
                ViewportAnimatorDebugVisible = viewportAnimatorDebugVisible,
                ViewportShadedMode = viewportShadedMode,
                ViewportLocalSpace = viewportLocalSpace,
                ViewportPivotCenter = viewportPivotCenter,
                ViewportResolutionPreset = viewportResolutionPresetKey,
                RenderScale = renderScale,
                FxaaEnabled = fxaaEnabled,
                BloomQuality = bloomQuality,
                AmbientOcclusionQuality = ambientOcclusionQuality,
                ShadowBias = shadowBias,
                SceneGridVisible = sceneGridVisible,
                SceneGridSnapEnabled = sceneGridSnapEnabled,
                SceneGridSize = sceneGridSize,
                SceneGridOpacity = sceneGridOpacity,
                SceneGridAxis = sceneGridAxis.ToString(),
                LightingDebugView = lightingDebugView.ToString(),
                ShadowQuality = shadowQuality.ToString(),
                ColorSpace = colorSpace.ToString(),
                ImageBasedLighting = iblEnabled,
                HdriPath = hdriPath,
                ProjectListView = projectListView,
                ProjectTileScale = projectTileScale,
                ProjectSortMode = projectSortMode.ToString(),
                ProjectSortDescending = projectSortDescending,
                PanelLeftWidth = _panelLeftW,
                PanelRightWidth = _panelRightW,
                PanelBottomHeight = _panelBottomH,
                GuiPanelPadding = designerPanelPadding,
                GuiRowHeight = designerRowHeight,
                GuiSplitterSize = designerSplitterSize,
                GuiPanelAlpha = designerPanelAlpha,
                GuiLabelRatio = designerLabelRatio,
                GuiShowGuides = designerShowGuides,
                GuiSelectedStyleClass = guiSelectedStyleClass.ToString(),
                GuiElementPickFilter = guiElementPickFilter.ToString(),
                GuiFontScale = guiFontScale,
                GuiButtonHeight = guiButtonHeight,
                GuiButtonRounding = guiButtonRounding,
                GuiCheckboxSize = guiCheckboxSize,
                GuiSliderHeight = guiSliderHeight,
                GuiAssetSlotHeight = guiAssetSlotHeight,
                GuiLabelBrightness = guiLabelBrightness,
                ConsoleShowInfo = consoleShowInfo,
                ConsoleShowWarnings = consoleShowWarnings,
                ConsoleShowErrors = consoleShowErrors
            };

            File.WriteAllText(editorSettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"No se pudieron guardar los ajustes del editor ({editorSettingsPath}): {ex.Message}");
        }
    }

    private void CreateIconAtlas()
    {
        const int cell = 32;
        var icons = Enum.GetValues<EditorIcon>();
        int columns = 8;
        int rows = (int)Math.Ceiling(icons.Length / (float)columns);
        int width = columns * cell;
        int height = rows * cell;

        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        for (int i = 0; i < icons.Length; i++)
        {
            int x = (i % columns) * cell;
            int y = (i / columns) * cell;
            var cellRect = new System.Drawing.RectangleF(x + 4, y + 4, cell - 8, cell - 8);
            // Si hay un PNG del pack (EditorIcons/<nombre>_64.png) se usa; si no, dibujo procedural.
            if (!TryDrawPackIcon(g, icons[i], cellRect))
                DrawAtlasIcon(g, icons[i], cellRect);
            iconAtlasRegions[icons[i]] = new IconAtlasRegion(
                new Vector2(x / (float)width, y / (float)height),
                new Vector2((x + cell) / (float)width, (y + cell) / (float)height));
        }

        var rect = new System.Drawing.Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            iconAtlasTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, iconAtlasTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            iconAtlasReady = true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void DrawAtlasIcon(System.Drawing.Graphics g, EditorIcon icon, System.Drawing.RectangleF r)
    {
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 205, 214, 224), 2.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(230, 205, 214, 224));
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        switch (icon)
        {
            case EditorIcon.Move:
                g.DrawLine(pen, x + w * .5f, y + h * .5f, x + w, y + h * .5f);
                g.DrawLine(pen, x + w * .5f, y + h * .5f, x + w * .5f, y);
                g.DrawLine(pen, x + w * .5f, y + h * .5f, x + w * .2f, y + h * .8f);
                g.FillPolygon(brush, new[] { new System.Drawing.PointF(x + w, y + h * .5f), new System.Drawing.PointF(x + w * .78f, y + h * .38f), new System.Drawing.PointF(x + w * .78f, y + h * .62f) });
                break;
            case EditorIcon.Rotate:
                g.DrawEllipse(pen, x + w * .14f, y + h * .14f, w * .72f, h * .72f);
                g.FillPolygon(brush, new[] { new System.Drawing.PointF(x + w * .82f, y + h * .18f), new System.Drawing.PointF(x + w, y + h * .24f), new System.Drawing.PointF(x + w * .85f, y + h * .40f) });
                break;
            case EditorIcon.Scale:
                g.DrawRectangle(pen, x + w * .18f, y + h * .18f, w * .64f, h * .64f);
                g.FillRectangle(brush, x + w * .64f, y + h * .64f, w * .24f, h * .24f);
                break;
            case EditorIcon.Play:
                g.FillPolygon(brush, new[] { new System.Drawing.PointF(x + w * .32f, y + h * .18f), new System.Drawing.PointF(x + w * .32f, y + h * .82f), new System.Drawing.PointF(x + w * .82f, y + h * .5f) });
                break;
            case EditorIcon.Stop:
                g.FillRectangle(brush, x + w * .25f, y + h * .25f, w * .50f, h * .50f);
                break;
            case EditorIcon.Pause:
                g.FillRectangle(brush, x + w * .25f, y + h * .18f, w * .18f, h * .64f);
                g.FillRectangle(brush, x + w * .57f, y + h * .18f, w * .18f, h * .64f);
                break;
            case EditorIcon.Step:
                g.FillPolygon(brush, new[] { new System.Drawing.PointF(x + w * .18f, y + h * .22f), new System.Drawing.PointF(x + w * .18f, y + h * .78f), new System.Drawing.PointF(x + w * .58f, y + h * .5f) });
                g.FillRectangle(brush, x + w * .68f, y + h * .22f, w * .12f, h * .56f);
                break;
            case EditorIcon.Camera:
                g.FillRectangle(brush, x + w * .12f, y + h * .34f, w * .52f, h * .36f);
                g.FillPolygon(brush, new[] { new System.Drawing.PointF(x + w * .64f, y + h * .42f), new System.Drawing.PointF(x + w, y + h * .28f), new System.Drawing.PointF(x + w, y + h * .82f) });
                break;
            case EditorIcon.Light:
                g.FillEllipse(brush, x + w * .36f, y + h * .36f, w * .28f, h * .28f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI * .25f;
                    float cx = x + w * .5f, cy = y + h * .5f;
                    g.DrawLine(pen, cx + MathF.Cos(a) * w * .25f, cy + MathF.Sin(a) * h * .25f, cx + MathF.Cos(a) * w * .46f, cy + MathF.Sin(a) * h * .46f);
                }
                break;
            case EditorIcon.Folder:
                g.FillRectangle(brush, x + w * .06f, y + h * .34f, w * .88f, h * .48f);
                g.FillRectangle(brush, x + w * .14f, y + h * .18f, w * .42f, h * .20f);
                break;
            case EditorIcon.Cube:
                g.DrawPolygon(pen, new[] { new System.Drawing.PointF(x + w * .22f, y + h * .42f), new System.Drawing.PointF(x + w * .5f, y + h * .24f), new System.Drawing.PointF(x + w * .78f, y + h * .42f), new System.Drawing.PointF(x + w * .5f, y + h * .60f) });
                g.DrawPolygon(pen, new[] { new System.Drawing.PointF(x + w * .22f, y + h * .42f), new System.Drawing.PointF(x + w * .5f, y + h * .60f), new System.Drawing.PointF(x + w * .5f, y + h * .86f), new System.Drawing.PointF(x + w * .22f, y + h * .68f) });
                g.DrawPolygon(pen, new[] { new System.Drawing.PointF(x + w * .78f, y + h * .42f), new System.Drawing.PointF(x + w * .5f, y + h * .60f), new System.Drawing.PointF(x + w * .5f, y + h * .86f), new System.Drawing.PointF(x + w * .78f, y + h * .68f) });
                break;
            case EditorIcon.Plane:
                g.DrawPolygon(pen, new[] { new System.Drawing.PointF(x + w * .16f, y + h * .62f), new System.Drawing.PointF(x + w * .5f, y + h * .36f), new System.Drawing.PointF(x + w * .86f, y + h * .62f), new System.Drawing.PointF(x + w * .5f, y + h * .84f) });
                break;
            case EditorIcon.Frame:
                g.DrawRectangle(pen, x + w * .18f, y + h * .18f, w * .64f, h * .64f);
                g.DrawEllipse(pen, x + w * .38f, y + h * .38f, w * .24f, h * .24f);
                break;
            case EditorIcon.Settings:
                g.DrawEllipse(pen, x + w * .30f, y + h * .30f, w * .40f, h * .40f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI * .25f;
                    float cx = x + w * .5f, cy = y + h * .5f;
                    g.DrawLine(pen, cx + MathF.Cos(a) * w * .32f, cy + MathF.Sin(a) * h * .32f, cx + MathF.Cos(a) * w * .48f, cy + MathF.Sin(a) * h * .48f);
                }
                break;
            case EditorIcon.ShaderGraph:
                g.DrawLine(pen, x + w * .26f, y + h * .28f, x + w * .74f, y + h * .50f);
                g.DrawLine(pen, x + w * .26f, y + h * .72f, x + w * .74f, y + h * .50f);
                g.FillEllipse(brush, x + w * .12f, y + h * .18f, w * .28f, h * .20f);
                g.FillEllipse(brush, x + w * .12f, y + h * .62f, w * .28f, h * .20f);
                g.FillEllipse(brush, x + w * .60f, y + h * .40f, w * .28f, h * .20f);
                break;
            case EditorIcon.Lock:
            case EditorIcon.Unlock:
                g.DrawArc(pen, x + w * (icon == EditorIcon.Lock ? .28f : .44f), y + h * .12f, w * .44f, h * .46f, 180, icon == EditorIcon.Lock ? 180 : 135);
                g.FillRectangle(brush, x + w * .22f, y + h * .46f, w * .56f, h * .38f);
                break;
            case EditorIcon.Visible:
            case EditorIcon.Hidden:
                g.DrawEllipse(pen, x + w * .10f, y + h * .26f, w * .80f, h * .48f);
                g.FillEllipse(brush, x + w * .42f, y + h * .42f, w * .16f, h * .16f);
                if (icon == EditorIcon.Hidden) g.DrawLine(pen, x + w * .16f, y + h * .84f, x + w * .84f, y + h * .16f);
                break;
            default:
                g.DrawRectangle(pen, x + w * .18f, y + h * .18f, w * .64f, h * .64f);
                g.DrawLine(pen, x + w * .18f, y + h * .18f, x + w * .82f, y + h * .82f);
                break;
        }
    }

    // Mapa EditorIcon -> archivo PNG del pack en EditorIcons/. Transform queda fuera a
    // propósito (lo dibuja DrawTransformIcon de forma procedural con color propio).
    private static readonly Dictionary<EditorIcon, string> PackIconFiles = new()
    {
        [EditorIcon.Move] = "move_64.png",
        [EditorIcon.Rotate] = "rotate_64.png",
        [EditorIcon.Scale] = "scale_64.png",
        [EditorIcon.Play] = "play_64.png",
        [EditorIcon.Stop] = "stop_64.png",
        [EditorIcon.Pause] = "pause_64.png",
        [EditorIcon.Step] = "step_64.png",
        [EditorIcon.Camera] = "camera_64.png",
        [EditorIcon.Light] = "light_64.png",
        [EditorIcon.Mesh] = "mesh_64.png",
        [EditorIcon.Prefab] = "prefab_64.png",
        [EditorIcon.Script] = "script_64.png",
        [EditorIcon.Folder] = "folder_64.png",
        [EditorIcon.Visible] = "eye_64.png",
        [EditorIcon.Hidden] = "eyeoff_64.png",
        [EditorIcon.Lock] = "lock_64.png",
        [EditorIcon.Unlock] = "unlock_64.png",
        [EditorIcon.Console] = "console_64.png",
        [EditorIcon.Asset] = "file-new_64.png",
        [EditorIcon.Cube] = "cube_64.png",
        [EditorIcon.Plane] = "plane_64.png",
        [EditorIcon.Frame] = "crosshair_64.png",
        [EditorIcon.Settings] = "settings_64.png",
        [EditorIcon.ShaderGraph] = "shader_64.png",
        [EditorIcon.CameraGizmo] = "cinecamera_64.png",
    };

    // Bliteo el PNG blanco del pack en la celda del atlas. Devuelve false si no hay archivo
    // (entonces el llamador cae al dibujo procedural). El atlas se tiñe luego en DrawIcon.
    private static bool TryDrawPackIcon(System.Drawing.Graphics g, EditorIcon icon, System.Drawing.RectangleF r)
    {
        if (!PackIconFiles.TryGetValue(icon, out var fileName))
            return false;

        string path = Path.Combine(AppContext.BaseDirectory, "EditorIcons", fileName);
        if (!File.Exists(path))
            return false;

        try
        {
            using var img = new System.Drawing.Bitmap(path);
            var prevInterp = g.InterpolationMode;
            var prevOffset = g.PixelOffsetMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(img, r);
            g.InterpolationMode = prevInterp;
            g.PixelOffsetMode = prevOffset;
            return true;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning($"No se pudo cargar el icono del pack '{fileName}': {ex.Message}");
            return false;
        }
    }

    private void BeginPanel(string id, Vector2 size)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(designerPanelPadding, designerPanelPadding));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.085f, 0.095f, 0.108f, designerPanelAlpha));
        ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(0.135f, 0.150f, 0.168f, 1f));
        ImGui.BeginChild(id, size, ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
    }

    private void DrawDockspace()
    {
        currentDrawingApp = this;
        toolWindowMouseCapture = false;
        ImGui.GetIO().FontGlobalScale = guiFontScale;

        long menuStart = EditorProfiler.Timestamp();
        DrawMainMenu();
        DrawTopToolbar();
        profiler.SampleUiMenu(EditorProfiler.ElapsedMs(menuStart));

        DrawMainLayout();

        long statusStart = EditorProfiler.Timestamp();
        DrawStatusBar();
        DrawStats();
        profiler.SampleUiStatus(EditorProfiler.ElapsedMs(statusStart));

        long toolsStart = EditorProfiler.Timestamp();
        DrawGuiDesignerWindow();
        DrawEditorSettingsWindow();
        DrawShaderGraphWindow();
        DrawAnimationWindow();
        DrawAnimatorGraphWindow();
        profiler.SampleUiTools(EditorProfiler.ElapsedMs(toolsStart));

        long profilerStart = EditorProfiler.Timestamp();
        DrawProfilerWindow();
        profiler.SampleUiProfiler(EditorProfiler.ElapsedMs(profilerStart));

        DrawEditorProgressOverlay();
        currentDrawingApp = null;
    }

    // Marca que el ratón está sobre la ventana de herramienta actual (para bloquear el
    // input del viewport detrás). Llamar justo tras un ImGui.Begin con éxito.
    private void TrackToolWindowMouse()
    {
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
            toolWindowMouseCapture = true;
    }

    private void DrawMainLayout()
    {
        float splitter = Math.Clamp(designerSplitterSize, 1f, 10f);
        float menuH = ImGui.GetFrameHeight() + PremiumToolbarHeight;
        float statusH = 24f;
        float totalW = ClientSize.X;
        float totalH = ClientSize.Y - menuH - statusH;

        _panelLeftW = Math.Clamp(_panelLeftW, 170f, totalW * 0.34f);
        _panelRightW = Math.Clamp(_panelRightW, 240f, totalW * 0.42f);
        _panelBottomH = Math.Clamp(_panelBottomH, 120f, totalH * 0.50f);

        float leftColW = totalW - _panelRightW - splitter;   // columna izq+centro (el Inspector va aparte, a altura completa)
        float centerW = leftColW - _panelLeftW - splitter;
        float topH = totalH - _panelBottomH - splitter;

        ImGui.SetNextWindowPos(new Vector2(0f, menuH), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(totalW, totalH), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.070f, 0.078f, 0.088f, 1f));
        ImGui.Begin("##GrokoLayout",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleColor();
        // StyleVar (WindowPadding + ItemSpacing = 0) permanece activo para que SameLine()
        // no añada spacing entre paneles y el viewport quede en la posición correcta.

        // ── Columna izquierda+centro: arriba Hierarchy | Scene, abajo Project ──
        long hierarchyStart = EditorProfiler.Timestamp();
        DrawHierarchyPanel(new Vector2(_panelLeftW, topH));
        profiler.SampleUiHierarchy(EditorProfiler.ElapsedMs(hierarchyStart));
        ImGui.SameLine();
        DrawVerticalSplitter("##lsplit", ref _panelLeftW, topH, splitter, false);
        ImGui.SameLine();
        long scenePanelStart = EditorProfiler.Timestamp();
        DrawScenePanel(new Vector2(Math.Max(1f, centerW), topH));
        profiler.SampleUiScenePanel(EditorProfiler.ElapsedMs(scenePanelStart));

        // Divisor horizontal + Project SOLO bajo izquierda+centro (no bajo el Inspector).
        DrawHorizontalSplitter("##bsplit", ref _panelBottomH, leftColW, splitter);
        long projectStart = EditorProfiler.Timestamp();
        DrawProjectPanel(new Vector2(leftColW, _panelBottomH));
        profiler.SampleUiProject(EditorProfiler.ElapsedMs(projectStart));

        // ── Columna derecha: Inspector a ALTURA COMPLETA (como Unity) ──
        ImGui.SetCursorPos(new Vector2(leftColW, 0f));
        DrawVerticalSplitter("##rsplit", ref _panelRightW, totalH, splitter, true);
        ImGui.SameLine(0f, 0f);
        long inspectorStart = EditorProfiler.Timestamp();
        DrawInspectorPanel(new Vector2(_panelRightW, totalH));
        profiler.SampleUiInspector(EditorProfiler.ElapsedMs(inspectorStart));
        if (designerShowGuides)
            DrawLayoutGuides(new Vector2(totalW, totalH), menuH);

        ImGui.PopStyleVar(2);
        ImGui.End();
    }

    private static void DrawVerticalSplitter(string id, ref float width, float height, float thickness, bool invertDelta)
    {
        var col = new System.Numerics.Vector4(0.070f, 0.075f, 0.080f, 1f);
        var colHover = new System.Numerics.Vector4(0.25f, 0.52f, 0.80f, 0.9f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleColor(ImGuiCol.Button, col);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, colHover);
        ImGui.Button(id, new Vector2(thickness, height));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        if (ImGui.IsItemActive())
            width += ImGui.GetIO().MouseDelta.X * (invertDelta ? -1f : 1f);
    }

    private static void DrawHorizontalSplitter(string id, ref float height, float width, float thickness)
    {
        var col = new System.Numerics.Vector4(0.070f, 0.075f, 0.080f, 1f);
        var colHover = new System.Numerics.Vector4(0.25f, 0.52f, 0.80f, 0.9f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleColor(ImGuiCol.Button, col);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, colHover);
        ImGui.Button(id, new Vector2(width, thickness));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
        if (ImGui.IsItemActive())
            height -= ImGui.GetIO().MouseDelta.Y;
    }

    private void DrawLayoutGuides(Vector2 totalSize, float menuHeight)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        float splitter = Math.Clamp(designerSplitterSize, 1f, 10f);
        float topH = totalSize.Y - _panelBottomH - splitter;
        uint guide = ImGui.GetColorU32(new System.Numerics.Vector4(0.36f, 0.62f, 0.92f, 0.48f));
        uint fill = ImGui.GetColorU32(new System.Numerics.Vector4(0.36f, 0.62f, 0.92f, 0.06f));

        var left = new Vector2(origin.X, origin.Y);
        var center = new Vector2(origin.X + _panelLeftW + splitter, origin.Y);
        var right = new Vector2(origin.X + totalSize.X - _panelRightW, origin.Y);
        var bottom = new Vector2(origin.X, origin.Y + topH + splitter);

        draw.AddRectFilled(left, left + new Vector2(_panelLeftW, topH), fill);
        draw.AddRectFilled(center, right - new Vector2(splitter, 0f) + new Vector2(0f, topH), fill);
        draw.AddRectFilled(right, right + new Vector2(_panelRightW, topH), fill);
        draw.AddRectFilled(bottom, bottom + new Vector2(totalSize.X, _panelBottomH), fill);
        draw.AddLine(new Vector2(origin.X + _panelLeftW, origin.Y), new Vector2(origin.X + _panelLeftW, origin.Y + topH), guide, 1.5f);
        draw.AddLine(new Vector2(origin.X + totalSize.X - _panelRightW, origin.Y), new Vector2(origin.X + totalSize.X - _panelRightW, origin.Y + topH), guide, 1.5f);
        draw.AddLine(new Vector2(origin.X, origin.Y + topH), new Vector2(origin.X + totalSize.X, origin.Y + topH), guide, 1.5f);
    }

    private void DrawMainMenu()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.36f, 0.62f, 0.88f, 1f));
        ImGui.Text("GrokoEngine");
        ImGui.PopStyleColor();
        ImGui.SameLine();

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Save Scene", "Ctrl+S")) SaveScene();
            if (ImGui.MenuItem("Reload Scene")) LoadScene();
            ImGui.Separator();
            if (ImGui.MenuItem("Build / Export Game…"))
            {
                SaveScene(); // exporta la escena actual tal como está
                string? outDir = BrowseForExportFolder();
                if (!string.IsNullOrWhiteSpace(outDir))
                    ExportGame(outDir);
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) Close();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            if (ImGui.MenuItem("Undo", "Ctrl+Z")) UndoScene();
            if (ImGui.MenuItem("Redo", "Ctrl+Y")) RedoScene();
            ImGui.Separator();
            if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, selected != null)) DuplicateSelected();
            if (ImGui.MenuItem("Delete", "Del", false, selected != null)) DeleteSelected();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Assets"))
        {
            if (ImGui.MenuItem("Create Script")) CreateScript();
            if (ImGui.MenuItem("Create Material") && currentProjectDirectory != null)
            {
                selectedAssetPath = MaterialAsset.Create(currentProjectDirectory);
                statusMessage = "Material created";
            }
            if (ImGui.MenuItem("Create Folder") && currentProjectDirectory != null)
                CreateAssetFolder(currentProjectDirectory);
            ImGui.Separator();
            if (ImGui.MenuItem("Compile Scripts")) CompileScripts();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("GameObject"))
        {
            DrawObjectCreationMenu(null);

            ImGui.Separator();
            if (ImGui.MenuItem("Duplicate Selected", "Ctrl+D", false, selected != null)) DuplicateSelected();
            if (ImGui.MenuItem("Delete Selected", "Del", false, selected != null)) DeleteSelected();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Component"))
        {
            if (selected == null)
                ImGui.TextDisabled("Select an object first");
            else
            {
                if (ImGui.MenuItem("Rigidbody")) AddComponentToObject(selected, "Rigidbody", () => selected.AddComponentWithEngine<Rigidbody>(physicsEngine));
                if (ImGui.MenuItem("Box Collider")) AddComponentToObject(selected, "BoxCollider", () => selected.AddComponentWithEngine<BoxCollider>(physicsEngine));
                if (ImGui.MenuItem("Sphere Collider")) AddComponentToObject(selected, "Sphere Collider", () => selected.AddComponentWithEngine<SphereCollider>(physicsEngine));
                if (ImGui.MenuItem("Capsule Collider")) AddComponentToObject(selected, "Capsule Collider", () => selected.AddComponentWithEngine<CapsuleCollider>(physicsEngine));
                if (ImGui.MenuItem("Mesh Collider")) AddComponentToObject(selected, "Mesh Collider", () => selected.AddComponentWithEngine<MeshCollider>(physicsEngine));
                if (ImGui.MenuItem("Character Controller", "", false, selected.GetComponent<CharacterController>() == null))
                    AddComponentToObject(selected, "Character Controller", () => AddCharacterController(selected));
                if (ImGui.MenuItem("Particle System", "", false, selected.GetComponent<GrokoEngine.ParticleSystem>() == null))
                    AddComponentToObject(selected, "ParticleSystem", () => AddPreviewParticleSystem(selected));
                if (ImGui.MenuItem("Mesh Filter", "", false, selected.GetComponent<MeshFilter>() == null)) AddComponentToObject(selected, "Mesh Filter", () => selected.AddComponent<MeshFilter>());
                if (ImGui.MenuItem("Material")) AddComponentToObject(selected, "Material", () => selected.AddComponent<Material>());
                if (ImGui.MenuItem("Camera", "", false, selected.GetComponent<Camera>() == null))
                    AddComponentToObject(selected, "Camera", () => { selected.IsCamera = true; return selected.AddComponent<Camera>(); });
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Window"))
        {
            if (ImGui.MenuItem("GUI Designer"))
                showGuiDesigner = true;
            if (ImGui.MenuItem("Editor Settings"))
                showEditorSettings = true;
            if (ImGui.MenuItem("Shader Graph"))
                showShaderGraph = true;
            if (ImGui.MenuItem("Animation", "", showAnimationWindow))
                showAnimationWindow = !showAnimationWindow;
            if (ImGui.MenuItem("Animator", "", showAnimatorGraph))
                showAnimatorGraph = !showAnimatorGraph;
            if (ImGui.MenuItem("Profiler", "", showProfiler))
                showProfiler = !showProfiler;
            ImGui.Separator();
            if (ImGui.MenuItem("VSync", "", vsync))
            {
                vsync = !vsync;
                VSync = vsync ? VSyncMode.On : VSyncMode.Off;
            }
            ImGui.Separator();
            if (ImGui.MenuItem(isPlaying ? "Stop" : "Play", "Ctrl+P"))
                TogglePlayMode();
            if (ImGui.MenuItem("Play Physics", "", simulatePhysics))
            {
                simulatePhysics = !simulatePhysics;
                BepuBackend.Reset(); // reconstruye la simulación Bepu desde la pose actual de la escena
                statusMessage = simulatePhysics ? "Physics simulation on" : "Physics simulation off";
            }

            if (ImGui.MenuItem("Step Physics"))
                physicsEngine.Step(objects, 1.0 / 60.0);

            ImGui.Separator();
            ImGui.TextDisabled("Physics: BepuPhysics only");
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Tools"))
        {
            if (ImGui.MenuItem("Bake Lightmaps", "", false, !isPlaying))
                _ = BakeLightmapsAsync();
            if (ImGui.MenuItem("Play Physics", "", simulatePhysics))
            {
                simulatePhysics = !simulatePhysics;
                BepuBackend.Reset();
                statusMessage = simulatePhysics ? "Physics simulation on" : "Physics simulation off";
            }
            if (ImGui.MenuItem("Step Physics"))
                physicsEngine.Step(objects, 1.0 / 60.0);
            ImGui.Separator();
            if (ImGui.MenuItem("Shader Graph"))
                showShaderGraph = true;
            if (ImGui.MenuItem("Animation"))
                showAnimationWindow = true;
            if (ImGui.MenuItem("Animator"))
                showAnimatorGraph = true;
            ImGui.Separator();
            ImGui.TextDisabled("Physics: BepuPhysics only");
            ImGui.TextDisabled("Static objects only for lightmaps");
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            ImGui.TextDisabled("GrokoEngine ImGui Editor");
            ImGui.TextDisabled("Premium dark editor workflow");
            ImGui.EndMenu();
        }

        DrawSceneTitleOnMenuBar();
        ImGui.EndMainMenuBar();
    }


    private void DrawSceneTitleOnMenuBar()
    {
        float width = ImGui.GetWindowWidth();
        string sceneName = Path.GetFileNameWithoutExtension(scenePath);
        string title = string.IsNullOrWhiteSpace(sceneName) ? "Untitled - Scene.gscene*" : sceneName + " - Scene.gscene*";
        float textW = ImGui.CalcTextSize(title).X;
        float targetX = Math.Max(ImGui.GetCursorPosX() + 16f, width * 0.5f - textW * 0.5f);
        ImGui.SameLine(targetX);
        ImGui.TextDisabled(title);
    }

    private void DrawTopToolbar()
    {
        float menuH = ImGui.GetFrameHeight();
        ImGui.SetNextWindowPos(new Vector2(0f, menuH), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(ClientSize.X, PremiumToolbarHeight), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5f, 0f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.055f, 0.061f, 0.070f, 1f));
        ImGui.Begin("##GrokoPremiumToolbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        uint bottomLine = ImGui.GetColorU32(new System.Numerics.Vector4(0.115f, 0.125f, 0.140f, 1f));
        dl.AddLine(new Vector2(min.X, max.Y - 1f), new Vector2(max.X, max.Y - 1f), bottomLine, 1f);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        DrawToolbarIcon(EditorIcon.Asset, "New Scene / Asset", () => { if (currentProjectDirectory != null) CreateAssetFolder(currentProjectDirectory); });
        ImGui.SameLine();
        DrawToolbarIcon(EditorIcon.Save, "Save Scene", SaveScene);
        ImGui.SameLine();
        DrawToolbarIcon(EditorIcon.Refresh, "Reload Scene", LoadScene);
        ImGui.SameLine();
        DrawToolbarIcon(EditorIcon.Prefab, "Create Prefab from selected", () =>
        {
            if (selected != null && currentProjectDirectory != null)
                CreatePrefabFromHierarchyObject(currentProjectDirectory);
        });
        ImGui.SameLine();
        DrawToolbarIcon(EditorIcon.Settings, "Editor Settings", () => showEditorSettings = true);

        float centerX = MathF.Max(360f, ClientSize.X * 0.5f - 56f);
        ImGui.SameLine(centerX);
        if (DrawIconButton(isPlaying ? EditorIcon.Stop : EditorIcon.Play, isPlaying ? "Stop Play Mode" : "Enter Play Mode", isPlaying, new Vector2(30f, 24f)))
            TogglePlayMode();
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Pause, "Pause Play Mode", playPaused, new Vector2(30f, 24f)))
            playPaused = isPlaying && !playPaused;
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Step, "Step one frame", false, new Vector2(30f, 24f)) && isPlaying)
            StepRuntime(1.0 / 60.0);

        float rightX = MathF.Max(ImGui.GetCursorPosX() + 24f, ClientSize.X - 310f);
        ImGui.SameLine(rightX);
        DrawToolbarDropdown("Build Windows", 118f);
        ImGui.SameLine();
        DrawToolbarDropdown("Default", 88f);
        ImGui.SameLine();
        DrawToolbarIcon(EditorIcon.Settings, "Project / Quality Settings", () => showEditorSettings = true);
        ImGui.PopStyleVar();
        ImGui.End();
    }

    private static void DrawToolbarDropdown(string label, float width)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.080f, 0.088f, 0.100f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.125f, 0.145f, 0.165f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(0.160f, 0.205f, 0.250f, 1f));
        ImGui.Button(label + "  v", new Vector2(width, 24f));
        ImGui.PopStyleColor(3);
    }

    private static void DrawToolbarIcon(EditorIcon icon, string tooltip, Action action)
    {
        if (DrawIconButton(icon, tooltip, false, new Vector2(26f, 24f)))
            action();
    }

    private void DrawTopPlayButton()
    {
        float width = ImGui.GetWindowWidth();
        ImGui.SameLine(Math.Max(420f, width * 0.5f - 58f));

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        if (DrawIconButton(isPlaying ? EditorIcon.Stop : EditorIcon.Play, isPlaying ? "Stop Play Mode" : "Enter Play Mode", isPlaying, new Vector2(30f, 24f)))
            TogglePlayMode();

        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Pause, "Pause Play Mode", playPaused, new Vector2(30f, 24f)))
            playPaused = isPlaying && !playPaused;
        ImGui.SameLine();
        if (DrawIconButton(EditorIcon.Step, "Step one frame", false, new Vector2(30f, 24f)) && isPlaying)
            StepRuntime(1.0 / 60.0);
        ImGui.PopStyleVar();
    }


    private void DrawProjectPanel(Vector2 size)
    {
        BeginPanel("##ProjectPanel", size);

        if (ImGui.BeginTabBar("ProjectConsoleTabs"))
        {
            if (ImGui.BeginTabItem("Project"))
            {
                DrawProjectTabContent();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Console"))
            {
                DrawConsoleTabContent();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Animation"))
            {
                DrawEmbeddedToolLauncher("Animation", "Dope Sheet, keyframes, curvas y clips del objeto seleccionado.", () => showAnimationWindow = true);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Animator"))
            {
                DrawEmbeddedToolLauncher("Animator", "Estados, parámetros y transiciones del controlador de animación.", () => showAnimatorGraph = true);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Shader Graph"))
            {
                DrawEmbeddedToolLauncher("Shader Graph", "Blackboard, nodos, preview y generación de shader para materiales.", () => showShaderGraph = true);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawDeleteAssetPopup();
        ImGui.EndChild();
    }


    private static void DrawEmbeddedToolLauncher(string title, string description, Action openAction)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.095f, 0.105f, 0.118f, 1f));
        ImGui.BeginChild("##" + title + "EmbeddedLauncher", Vector2.Zero, ImGuiChildFlags.Borders);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.TextUnformatted(title);
        ImGui.TextDisabled(description);
        ImGui.Spacing();
        ImGui.TextDisabled("Este tab queda integrado en el dock inferior para que el layout visual sea como la referencia.");
        ImGui.TextDisabled("Pulsa Abrir para usar la herramienta completa flotante sin romper tu flujo actual.");
        ImGui.Spacing();
        if (ImGui.Button("Abrir " + title, new Vector2(150f, 26f)))
            openAction();
        ImGui.EndChild();
    }

    private void DrawProjectTabContent()
    {
        if (!Directory.Exists(rootAssetsPath))
        {
            ImGui.TextDisabled("Assets folder not found");
            return;
        }

        currentProjectDirectory ??= rootAssetsPath;
        if (!Directory.Exists(currentProjectDirectory) || !IsInsideAssets(currentProjectDirectory))
            currentProjectDirectory = rootAssetsPath;

        // Rect del panel para deseleccionar al clicar fuera de él (viewport, jerarquía, etc.).
        var projectPanelMin = ImGui.GetWindowPos();
        var projectPanelMax = projectPanelMin + ImGui.GetWindowSize();

        DrawProjectToolbar();
        ImGui.Spacing();

        float folderTreeWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.18f, 155f, 230f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.070f, 0.078f, 0.088f, 1f));
        ImGui.BeginChild("ProjectFolders", new Vector2(folderTreeWidth, 0), ImGuiChildFlags.None);
        DrawProjectRootTreeHeader("Favorites");
        DrawProjectFolderTree(rootAssetsPath);
        DrawProjectRootTreeHeader("Packages");
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.085f, 0.095f, 0.108f, 1f));
        ImGui.BeginChild("ProjectAssets", new Vector2(0, 0), ImGuiChildFlags.None);
        HandleProjectAssetZoom();

        if (inlineRenameAssetPath == null && !ImGui.IsAnyItemActive()
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Backspace))
            {
                string? parent = Path.GetDirectoryName(currentProjectDirectory);
                if (!string.IsNullOrEmpty(parent) && IsInsideAssets(parent))
                    currentProjectDirectory = parent;
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Delete))
            {
                QueueDeleteSelectedProjectAssets();
            }
        }
        Vector2 assetsAreaOrigin = ImGui.GetCursorScreenPos();
        Vector2 assetsAreaSize = ImGui.GetContentRegionAvail();
        projectHoveredAnyAssetItem = false;
        projectAssetSelectionRects.Clear();
        projectAssetSelectionEntries.Clear();
        if (projectListView)
            DrawProjectAssetList(currentProjectDirectory);
        else
            DrawProjectAssetGrid(currentProjectDirectory);
        DrawProjectAssetBackgroundClickCatcher(assetsAreaOrigin, assetsAreaSize, currentProjectDirectory);
        DrawProjectBackgroundContextMenu(currentProjectDirectory);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        HandleProjectDeselectOnOutsideClick(projectPanelMin, projectPanelMax);
    }

    // Si se hace clic izquierdo fuera del panel de Project, deselecciona la carpeta/asset.
    private void HandleProjectDeselectOnOutsideClick(Vector2 panelMin, Vector2 panelMax)
    {
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;
        if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
            return;

        var m = ImGui.GetMousePos();
        bool inside = m.X >= panelMin.X && m.X <= panelMax.X && m.Y >= panelMin.Y && m.Y <= panelMax.Y;
        // Clicar en el Inspector tampoco deselecciona (para poder usar los Import Settings del asset).
        bool inInspector = m.X >= inspectorPanelMin.X && m.X <= inspectorPanelMax.X &&
                           m.Y >= inspectorPanelMin.Y && m.Y <= inspectorPanelMax.Y;
        if (inside || inInspector)
            return;

        if (selectedAssetPath == null && selectedProjectEntryKeys.Count == 0 && !projectFolderHighlightActive)
            return;

        ClearProjectEntrySelection(false); // no tocar la selección de la escena
        projectFolderHighlightActive = false;
    }

    private void DrawStats()
    {
        // Overlay en esquina superior-derecha del viewport (estilo Unity Stats)
        float menuH = ImGui.GetFrameHeight() + PremiumToolbarHeight;
        const float statsW = 188f;
        const float statsH = 100f;
        float statsX = _panelLeftW + (ClientSize.X - _panelLeftW - _panelRightW) - statsW - 8f;
        float statsY = menuH + 36f;
        ImGui.SetNextWindowPos(new Vector2(statsX, statsY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(statsW, statsH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.060f, 0.070f, 0.080f, 0.82f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        ImGui.Begin("Stats", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs);
        ImGui.PopStyleVar();
        float fps = Math.Max(0.001f, ImGui.GetIO().Framerate);
        var fpsColor = fps >= 60f
            ? new System.Numerics.Vector4(0.38f, 0.82f, 0.42f, 1f)
            : fps >= 30f
                ? new System.Numerics.Vector4(0.96f, 0.76f, 0.22f, 1f)
                : new System.Numerics.Vector4(0.95f, 0.34f, 0.30f, 1f);
        ImGui.TextColored(fpsColor, $"FPS  {fps:F0}");
        ImGui.TextDisabled($"     {1000f / fps:F2} ms");
        ImGui.Spacing();
        ImGui.TextDisabled($"Objects  {GetCachedObjectCount()}");
        ImGui.TextDisabled($"Mem  {GC.GetTotalMemory(false) / (1024f * 1024f):F1} MB");
        ImGui.TextDisabled(isPlaying ? (playPaused ? "● Paused" : "▶ Play") : "Edit mode");
        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawStatusBar()
    {
        float height = 24f;
        ImGui.SetNextWindowPos(new Vector2(0f, Math.Max(0f, ClientSize.Y - height)), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(ClientSize.X, height), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 3f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.100f, 0.105f, 0.115f, 1f));
        ImGui.Begin("StatusBar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        double age = GLFW.GetTime() - lastStatusFlashTime;
        bool flash = age < 1.15;
        if (flash)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.72f, 0.86f, 1f, 1f));
        ImGui.Text(statusMessageValue);
        if (flash)
            ImGui.PopStyleColor();

        ImGui.SameLine(360f);
        float fps = Math.Max(0.001f, ImGui.GetIO().Framerate);
        ImGui.TextDisabled($"FPS {fps:F0}");
        ImGui.SameLine();
        ImGui.TextDisabled($"{1000f / fps:F2} ms");
        ImGui.SameLine();
        ImGui.TextDisabled($"Objects {GetCachedObjectCount()}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Mem {GC.GetTotalMemory(false) / (1024f * 1024f):F1} MB");
        ImGui.SameLine();
        ImGui.TextDisabled(isPlaying ? (playPaused ? "Play Paused" : "Play") : "Edit");
        ImGui.SameLine();
        ImGui.TextDisabled(vsync ? "VSync On" : "VSync Off");
        ImGui.SameLine();
        ImGui.TextDisabled(Path.GetFileName(projectPath));

        if (selectedAssetPath != null)
        {
            ImGui.SameLine(Math.Max(620f, ClientSize.X - 420f));
            ImGui.TextDisabled(IsInsideAssets(selectedAssetPath)
                ? Path.GetRelativePath(rootAssetsPath, selectedAssetPath).Replace('\\', '/')
                : Path.GetFileName(selectedAssetPath));
        }

        ImGui.End();
    }



    private void LoadScene()
    {
        objects.Clear();
        physicsEngine.ClearColliders();
        if (File.Exists(scenePath))
            objects.AddRange(SceneSerializer.Load(scenePath, physicsEngine, scriptCompiler));
        selected = objects.FirstOrDefault();
        sceneHistory.Clear();
        sceneRenderer.InvalidateStaticBatch(); // nueva escena = reconstruir batch estático
        int fixedCount = SanitizeExtremeTransforms();
        statusMessage = File.Exists(scenePath)
            ? fixedCount == 0 ? "Scene loaded" : $"Scene loaded, fixed {fixedCount} extreme transform(s)"
            : "No scene file";
        FrameScene();
    }

    private void SaveScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        SceneSerializer.Save(scenePath, objects);
        statusMessage = "Scene saved";
    }

}
