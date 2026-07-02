using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer : IDisposable
{
    private const int MaxDirectionalShadowCascades = 5;

    public enum GridAxis
    {
        X,
        Y,
        Z,
        All
    }

    public GridAxis SceneGridAxis { get; set; } = GridAxis.Y;
    public float SceneGridSize { get; set; } = 1f;
    public float SceneGridOpacity { get; set; } = 0.55f;
    public IReadOnlyCollection<string> SelectedObjectIds { get; set; } = Array.Empty<string>();

    /// <summary>Cuando está en false, solo se dibujan gizmos/selección; mejora la vista visual del editor.</summary>
    public bool ShowObjectWireframes { get; set; } = false;

    /// <summary>Recalcula el static batch cuando detecta cambios en objetos estáticos o sus padres.</summary>
    public bool AutoInvalidateStaticBatch { get; set; } = true;
    public bool RenderRealtimeShadows { get; set; } = true;
    public int OpaqueTextureHandle { get; set; }
    public Action? ResolveAndCaptureOpaqueTexture { get; set; }
    public int ShadowUpdateIntervalFrames { get; set; } = 1;
    public SceneRenderStats LastStats { get; private set; } = SceneRenderStats.Empty;

    private readonly List<LineVertex> vertices = new(4096);
    private LineVertex[] cachedGridVertices = Array.Empty<LineVertex>();
    private GridAxis cachedGridAxis;
    private float cachedGridSize = float.NaN;
    private float cachedGridOpacity = float.NaN;
    private readonly List<SolidVertex> solidVertices = new(4096);
    private readonly List<SolidRange> solidRanges = new(128);
    private readonly List<DynamicMeshDraw> dynamicMeshDraws = new(256);
    private readonly List<DynamicMeshDraw> refractiveMeshDraws = new(64);
    private readonly List<SkinnedMeshDraw> skinnedMeshDraws = new(64);
    private readonly List<SkinnedMeshDraw> refractiveSkinnedMeshDraws = new(32);
    private readonly List<ShaderGraphDynamicMeshDraw> shaderGraphDynamicMeshDraws = new(128);
    private readonly List<ShaderGraphSkinnedMeshDraw> shaderGraphSkinnedMeshDraws = new(32);
    // Buffers reutilizables para ApplySceneLighting(). Antes se creaban listas nuevas
    // varias veces por frame (static, dynamic, shader graph, terrain, occlusion).
    // En escenas grandes eso mete GC y picos pequeños.
    private readonly List<PointLight> _framePointLights = new(MaxPointLights);
    private readonly List<SpotLight> _frameSpotLights = new(MaxSpotLights);
    private readonly List<AreaLight> _frameAreaLights = new(MaxAreaLights);
    private readonly List<RectangleLight> _frameRectLights = new(MaxAreaLights);
    // Iluminación recolectada UNA vez por Render (memoizada por _frameCount): ApplySceneLighting
    // se llama en cada pase sólido (estático/dinámico/skinned) pero las luces no cambian dentro
    // del Render, así que se recolectan una sola vez y los pases reutilizan estos resultados.
    private int _frameLightsCollectedFor = -1;
    private AmbientLight? _frameAmbient;
    private PostProcessSettings? _framePostProcess;
    private DirectionalLight? _frameDir;
    private int _statsDrawCalls;
    private int _statsShadowDrawCalls;
    private int _statsInstancedDrawCalls;
    private int _statsInstances;
    private long _statsTriangles;
    private int _statsLineDrawCalls;
    private int _statsParticleDrawCalls;
    private float _statsBuildSceneMs;
    private float _statsShadowMs;
    private float _statsSkyboxMs;
    private float _statsStaticOpaqueMs;
    private float _statsDynamicOpaqueMs;
    private float _statsShaderGraphMs;
    private float _statsTerrainMs;
    private float _statsLinesGizmosMs;
    private float _statsParticlesMs;
    private float _statsOcclusionMs;
    private float _statsDirectionalShadowMs;
    private float _statsSpotShadowMs;
    private float _statsPointShadowMs;
    private float _statsRenderOtherMs;
    private readonly Dictionary<string, int> textureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> textureFileTimeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime WriteTime, int Frame)> staticSignatureFileTimeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedParsedMesh> parsedMeshCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedGpuMesh> gpuMeshCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedSkinnedGpuMesh> skinnedGpuMeshCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedSkinnedRange> skinnedRangeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (ParsedMesh Mesh, int Version)> terrainMeshCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> assetWarningLog = new(StringComparer.OrdinalIgnoreCase);
    private LineVertex[] lineUploadBuffer = Array.Empty<LineVertex>();
    private SolidVertex[] solidUploadBuffer = Array.Empty<SolidVertex>();
    private Matrix4[] instanceUploadBuffer = Array.Empty<Matrix4>();
    private int vertexArray;
    private int vertexBuffer;
    private int solidVertexArray;
    private int solidVertexBuffer;
    private int dynamicInstanceBuffer;
    private int dynamicInstanceBufferCapacity;
    private int shader;
    private int solidShader;
    private readonly Dictionary<string, ShaderGraphMaterialEntry> _shaderGraphCache = new(StringComparer.OrdinalIgnoreCase);
    private int _shaderGraphWhiteTex;
    private int _shaderGraphDepthPrepassShader;
    private int _shaderGraphDepthPrepassViewProjLocation;
    private int mvpLocation;
    private int solidMvpLocation;
    private int solidModelLocation;
    private int solidUseInstancingLocation;
    private int solidUseSkinningLocation;
    private int solidUseSurfaceUniformsLocation;
    private int solidSurfaceColorLocation;
    private int solidSurfaceMaterialLocation;
    private int solidSurfaceEmissionLocation;
    private int solidTextureLocation;
    private int solidHasTextureLocation;
    private int solidNormalMapLocation;
    private int solidHasNormalMapLocation;
    private int solidRoughnessMapLocation;
    private int solidHasRoughnessMapLocation;
    private int solidMetallicMapLocation;
    private int solidHasMetallicMapLocation;
    private int solidRefractivePassLocation;
    private int solidOpaqueTextureLocation;
    private int solidScreenTexelLocation;
    private int solidRefractionStrengthLocation;
    private int solidDistortionStrengthLocation;
    private int solidLightMvpLocation;
    private readonly int[] solidCascadeLightMvpLocations = new int[MaxDirectionalShadowCascades];
    private readonly int[] solidCascadeSplitLocations = new int[MaxDirectionalShadowCascades];
    private int solidCascadeCountLocation;
    private int solidCameraPositionLocation;
    private int solidShadowMapLocation;
    private int solidShadowEnabledLocation;
    private int solidShadowStrengthLocation;
    private int solidSpotShadowMapLocation;
    private int solidSpotLightMvpLocation;
    private int solidSpotShadowEnabledLocation;
    private int solidSpotShadowStrengthLocation;
    private int solidPointShadowCubeLocation;
    private int solidPointShadowEnabledLocation;
    private int solidPointShadowStrengthLocation;
    private int solidPointShadowPosLocation;
    private int solidPointShadowFarLocation;
    private int solidShadowPcfRadiusLocation;
    private int solidShadowBiasScaleLocation;
    private readonly int[] solidBoneMatrixLocations = new int[MaxGpuBones];

    // Lighting uniforms
    private int uAmbientColor;
    private int uAmbientIntensity;
    private int uSkyStrength;
    private int uDirDir;
    private int uDirColor;
    private int uDirIntensity;
    private int uCameraPos;
    private int uPointCount;
    private readonly int[] uPointPos = new int[MaxPointLights];
    private readonly int[] uPointColor = new int[MaxPointLights];
    private readonly int[] uPointIntensity = new int[MaxPointLights];
    private readonly int[] uPointRange = new int[MaxPointLights];
    private int uSpotCount;
    private readonly int[] uSpotPos = new int[MaxSpotLights];
    private readonly int[] uSpotDir = new int[MaxSpotLights];
    private readonly int[] uSpotColor = new int[MaxSpotLights];
    private readonly int[] uSpotIntensity = new int[MaxSpotLights];
    private readonly int[] uSpotRange = new int[MaxSpotLights];
    private readonly int[] uSpotAngle = new int[MaxSpotLights];
    private int uSpotCookieLocation, uSpotHasCookieLocation, uSpotCookieMvpLocation;
    private int uSpotIesLutLocation, uSpotHasIesLocation, uPointIesLutLocation, uPointHasIesLocation;
    private int _spotIesTex, _pointIesTex;
    private string _spotIesPathCached = "", _pointIesPathCached = "";
    private int uAreaCount;
    private readonly int[] uAreaPos = new int[MaxAreaLights];
    private readonly int[] uAreaDir = new int[MaxAreaLights];
    private readonly int[] uAreaColor = new int[MaxAreaLights];
    private readonly int[] uAreaIntensity = new int[MaxAreaLights];
    private readonly int[] uAreaRange = new int[MaxAreaLights];
    private readonly int[] uAreaSize = new int[MaxAreaLights];
    private int uColorSpaceLinear;
    private int uUseIBL;
    private int uEnvMap;
    private int uHasEnvMap;
    private int uEnvMaxLod;
    private int uEnvDiffuseColor;
    private int uUseEnvSh;
    private readonly int[] uEnvSh = new int[9];
    private int uEnvPrefilterMap;
    private int uHasEnvPrefilterMap;
    private int uEnvPrefilterMaxLod;
    private int uBrdfLut;
    private int uHasBrdfLut;
    private int uIblDiffuseStrength;
    private int uIblSpecularStrength;
    private int uAoStrength;
    private int uFogDensity;
    private int uFogColor;
    private int uVolumetricStrength;
    private int uDebugView;

    // ── Terrain shader (Fase 3: pintura de capas vía splat map) ────
    private int terrainShader;
    private int terrainMvpLocation;
    private int terrainModelLocation;
    private int terrainSplatMapLocation;
    private readonly int[] terrainLayerLocations = new int[4];
    private readonly int[] terrainHasLayerLocations = new int[4];
    private readonly int[] terrainTilingLocations = new int[4];
    private int terrainAmbientColorLocation;
    private int terrainAmbientIntensityLocation;
    private int terrainSkyStrengthLocation;
    private int terrainDirDirLocation;
    private int terrainDirColorLocation;
    private int terrainDirIntensityLocation;
    private readonly Dictionary<string, (int TextureId, int Version)> terrainSplatTextureCache = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxPointLights = 8;
    private const int MaxSpotLights = 4;
    private const int MaxAreaLights = 4;
    private const int MaxGpuBones = 128;
    private int bufferCapacity;
    private int solidBufferCapacity;
    // Cache de vértices de mesh en espacio objeto (pos/normal/uv pre-escalados por importScale)
    private readonly Dictionary<string, CachedMeshVerts> meshCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Static Batch ───────────────────────────────────────────────
    // VBO dedicado para objetos estáticos. Se construye UNA VEZ y se
    // reutiliza cada frame sin subir datos a la GPU.
    private int _staticVertexArray;
    private int _staticVertexBuffer;
    private int _staticBufferCapacity;
    private int _staticVertexCount;
    private readonly List<SolidVertex> _staticBuildList = new(8192);
    private readonly List<SolidRange> _staticRanges = new(64);
    private SolidVertex[] _staticUploadBuf = Array.Empty<SolidVertex>();
    private bool _staticDirty = true;
    private bool _buildingStatic = false;
    private int _staticBatchSignature;
    private int _nextStaticSignatureCheckFrame;
    private const int StaticSignatureCheckIntervalFrames = 30;

    /// <summary>
    /// Llama esto cuando cualquier objeto IsStatic cambia, se añaden/quitan
    /// objetos estáticos, o se modifica su mesh/material.
    /// </summary>
    public void InvalidateStaticBatch()
    {
        _staticDirty = true;
        _staticBatchSignature = 0;
        _nextStaticSignatureCheckFrame = 0;
    }

    // ── Particle Renderer ──────────────────────────────────────────
    // Shader billboard + VBO dinámico para todas las partículas de la escena.
    // Cada partícula = quad billboard (6 vértices) orientado hacia la cámara.
    private int _particleShader;
    private int _particleVao, _particleVbo;
    private int _particleBufferCapacity;
    private int _particleMvpLoc, _particleRightLoc, _particleUpLoc;
    private int _particleTexLoc, _particleHasTexLoc;
    private int _particleSoftLoc, _particleDepthTexLoc, _particleScreenSizeLoc;
    private int _particleSoftRangeLoc;
    private int _particleOpaqueTexLoc, _particleDistortionLoc, _particleDistortionStrengthLoc, _particleRefractionStrengthLoc;

    // ── Depth FBO para soft particles ─────────────────────────────
    private int _depthFbo, _depthTex;
    private int _depthFboWidth, _depthFboHeight;

    // ── Trail buffers ──────────────────────────────────────────────
    // Keyed by particle ID → lista de (worldPos, age, maxAge)
    private readonly Dictionary<int, List<(MiMotor.Mathematics.Vector3 Pos, float Age, float MaxAge)>>
        _trailPoints = new();
    private int _trailVbo, _trailVao;
    private int _trailBufferCap;
    private readonly List<ParticleVertex> _trailVerts = new(2048);
    private readonly List<ParticleVertex> _particleVerts = new(4096);
    private readonly List<ParticleVertex> _particleSortedVerts = new(4096);
    private readonly List<ParticleSortSlice> _particleSortSlices = new(4096);
    private readonly Dictionary<string, (DateTime FileTime, MaterialAssetData Data)> _particleMaterialCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Occlusion Culling ──────────────────────────────────────────
    /// <summary>Estado de culling de la cámara activa para el frame actual (ver Camera.FrustumCulling / OcclusionCulling).</summary>
    private bool _frustumCullingEnabled = true;
    private bool _occlusionCullingEnabled = false;
    private int _activeCullingMask = LayerMask.Everything;
    private int _staticBatchCullingMask = LayerMask.Everything;
    public int DebugView { get; set; }
    private readonly Dictionary<string, int> _occlusionQueries = new();
    private readonly Dictionary<string, bool> _occlusionVisible = new();
    private readonly Dictionary<string, int> _occlusionQueryFrame = new();
    private int _frameCount;
    private const int OcclusionQueryInterval = 4;
    private readonly List<(string Id, Matrix4 World, MeshFilter Mf, ParsedMesh Mesh)> _pendingOcclusionObjects = new();

    // VBO/VAO dedicado para las bounding boxes de occlusion queries
    // — completamente independiente del solidVertexBuffer para no interferir
    private int _occVao, _occVbo;
    private int _occVboCapacity;

    private int _shadowFbo;
    private int _shadowArrayTex;
    private int _spotShadowFbo;
    private int _spotShadowTex;
    private int _pointShadowFbo;
    private int _pointShadowCube;
    private int _shadowShader;
    private int _pointShadowShader;
    private int _shadowMvpLocation;
    private int _shadowModelLocation;
    private int _shadowUseInstancingLocation;
    private int _shadowUseSkinningLocation;
    private int _pointShadowMvpLocation;
    private int _pointShadowModelLocation;
    private int _pointShadowUseInstancingLocation;
    private int _pointShadowUseSkinningLocation;
    private int _pointShadowLightPosLocation;
    private int _pointShadowFarLocation;
    private readonly int[] _shadowBoneMatrixLocations = new int[MaxGpuBones];
    private readonly int[] _pointShadowBoneMatrixLocations = new int[MaxGpuBones];

    // ── Calidad de sombras (estilo Unity: Quality Settings → Shadow Resolution) ──
    // En vez de un tamaño fijo de shadow map, el usuario puede subir/bajar la
    // calidad desde el editor; esto reasigna la resolución de las texturas de
    // profundidad (direccional, spot y cubemap de point) sin recrear el motor.
    private ShadowQuality _shadowQuality = ShadowQuality.High;
    private bool _shadowResourcesDirty = true;
    private int _directionalShadowSize = 2048;
    private int _spotShadowSize = 1536;
    private int _pointShadowSize = 1024;
    private bool _cachedRealtimeShadowsValid;
    private int _cachedRealtimeShadowFrame = int.MinValue;
    private ShadowInfo _cachedDirectionalShadow = new(false, Matrix4.Identity, 0f);
    private ShadowInfo _cachedSpotShadow = new(false, Matrix4.Identity, 0f);
    private PointShadowInfo _cachedPointShadow = new(false, Vector3.Zero, 1f, 0f);
    private Vector3 _cachedShadowLightDirection;
    private int _cachedShadowWidth;
    private int _cachedShadowHeight;
    private ShadowQuality _cachedShadowQuality;
    private int _cachedSpotShadowFrame = int.MinValue;
    private int _cachedPointShadowFrame = int.MinValue;

    /// <summary>
    /// Espacio de color del pipeline de render (ver <see cref="GrokoEngine.ImGuiEditor.ColorSpace"/>).
    /// Solo controla un uniform — no requiere reasignar recursos como las sombras,
    /// así que el cambio se nota en pantalla en el frame siguiente.
    /// </summary>
    public ColorSpace ColorSpace { get; set; } = ColorSpace.Linear;
    public float ShadowBias { get; set; } = 1f;

    /// <summary>
    /// Image-Based Lighting procedural. Cuando está activo, las superficies
    /// reflejan el entorno (cielo/suelo derivado del ambiente) y los metales
    /// dejan de verse planos. Solo controla un uniform; el cambio se ve al frame
    /// siguiente. Sin coste de recursos (es analítico, sin texturas precalculadas).
    /// </summary>
    public bool ImageBasedLighting { get; set; } = true;
    public float IblDiffuseStrength { get; set; } = 0.65f;
    public float IblSpecularStrength { get; set; } = 0f;

    // ── Entorno HDRI (IBL Fase 2) ─────────────────────────────────────────
    private int _envTexture;          // textura equirectangular RGB16F (con mips)
    private int _envPrefilterCube;     // cubemap RGB16F prefiltrado para reflejos PBR
    private int _brdfLutTexture;       // LUT split-sum para IBL especular
    private bool _envLoaded;
    private float _envMaxLod;
    private float _envPrefilterMaxLod;
    private Vector3 _envDiffuseColor = new(0.3f, 0.32f, 0.36f);
    private readonly Vector3[] _envSh = new Vector3[9];
    private string _envPath = "";
    private bool _envDirty;

    // Skybox (dibuja el HDRI de fondo)
    private int _skyboxShader;
    private int _skyboxVao;
    private int _skyEnvLoc, _skyCamFrontLoc, _skyCamUpLoc, _skyTanHalfFovLoc, _skyAspectLoc;
    private int _skySunDirLoc, _skySunColorLoc, _skySunAngularLoc, _skySunEnabledLoc;

    /// <summary>
    /// Ruta a un HDRI equirectangular (.hdr). Cuando está cargado, el IBL refleja
    /// esta imagen real en vez del entorno procedural. Vacío = entorno procedural.
    /// </summary>
    public string EnvironmentHdriPath
    {
        get => _envPath;
        set
        {
            string v = value ?? "";
            if (_envPath == v) return;
            _envPath = v;
            _envDirty = true;
        }
    }

    /// <summary>True si hay un HDRI cargado correctamente en GPU.</summary>
    public bool EnvironmentLoaded => _envLoaded;
    public bool DrawEnvironmentBackground { get; set; } = true;

    public ShadowQuality ShadowQuality
    {
        get => _shadowQuality;
        set
        {
            if (_shadowQuality == value) return;
            _shadowQuality = value;
            _shadowResourcesDirty = true;
            _cachedRealtimeShadowsValid = false;
        }
    }

    /// <summary>
    /// Resoluciones de shadow map por nivel de calidad. "Medium" conserva los
    /// valores que tenía el motor antes de exponer esta opción, así que un
    /// proyecto existente no cambia de aspecto si el usuario no toca el ajuste.
    /// </summary>
    /// <summary>
    /// Radio del kernel PCF (sombras suaves) por nivel de calidad — el equivalente
    /// a alternar entre "Hard Shadows" y "Soft Shadows" en Unity, con más muestras
    /// cuanto mayor es la calidad: 0 → 1 muestra (dura/barata), 1 → 3x3 (9
    /// muestras, el comportamiento que tenía el motor por defecto), 2 → 5x5
    /// (25 muestras, penumbra muy suave para escenas "hero").
    /// </summary>

    private void FinalizeRenderOther(long renderFrameStart)
    {
        float totalMs = RenderElapsedMs(renderFrameStart);
        float trackedMs =
            _statsBuildSceneMs +
            _statsShadowMs +
            _statsSkyboxMs +
            _statsStaticOpaqueMs +
            _statsDynamicOpaqueMs +
            _statsShaderGraphMs +
            _statsTerrainMs +
            _statsLinesGizmosMs +
            _statsParticlesMs +
            _statsOcclusionMs;

        // Todo lo que no tenga fase propia (state setup, FBO/viewport restores, GL driver stalls,
        // shader/texture warmup y pequeños blits internos) cae aquí. Si este número sube,
        // ya no se confunde con sombras o dynamic opaque.
        _statsRenderOtherMs = Math.Max(0f, totalMs - trackedMs);
    }

    private void PublishRenderStats(int width, int height)
    {
        LastStats = new SceneRenderStats(
            _statsDrawCalls,
            _statsShadowDrawCalls,
            _statsInstancedDrawCalls,
            _statsInstances,
            _statsTriangles,
            _staticRanges.Count,
            solidRanges.Count,
            dynamicMeshDraws.Count + refractiveMeshDraws.Count + skinnedMeshDraws.Count + refractiveSkinnedMeshDraws.Count + shaderGraphDynamicMeshDraws.Count + shaderGraphSkinnedMeshDraws.Count,
            _statsLineDrawCalls,
            _statsParticleDrawCalls,
            textureCache.Count,
            gpuMeshCache.Count,
            parsedMeshCache.Count,
            _shaderGraphCache.Count,
            width,
            height,
            _statsBuildSceneMs,
            _statsShadowMs,
            _statsSkyboxMs,
            _statsStaticOpaqueMs,
            _statsDynamicOpaqueMs,
            _statsShaderGraphMs,
            _statsTerrainMs,
            _statsLinesGizmosMs,
            _statsParticlesMs,
            _statsOcclusionMs,
            _statsDirectionalShadowMs,
            _statsSpotShadowMs,
            _statsPointShadowMs,
            _statsRenderOtherMs);
    }

    public void Render(
        IReadOnlyList<GameObject> objects,
        GameObject? selected,
        ImGuiEditorApp.EditorCameraState camera,
        int width,
        int height,
        bool showGrid = true)
    {
        _frameCount++;
        ResetRenderStats();
        long renderFrameStart = RenderTimestamp();
        long buildPhaseStart = BeginRenderPass(SceneRenderPass.BuildScene);

        // El usuario cambió la calidad de sombras desde el editor (Settings → Lighting):
        // reasignamos las texturas de profundidad al nuevo tamaño antes de generarlas
        // este frame. No se hace en cada frame — solo cuando realmente cambia el ajuste.
        if (_shadowResourcesDirty)
            EnsureShadowResources();

        if (_envDirty)
            EnsureEnvironment();

        float nearClip = Math.Max(0.001f, camera.NearClip);
        float farClip = Math.Max(nearClip + 0.001f, camera.FarClip);
        Matrix4 projection;
        if (camera.Orthographic)
        {
            // Modo 2D: proyección ortográfica (sin perspectiva), como el botón 2D de Unity.
            float aspect = width / (float)Math.Max(1, height);
            float halfH = Math.Max(0.01f, camera.OrthoSize);
            projection = Matrix4.CreateOrthographic(halfH * 2f * aspect, halfH * 2f, nearClip, farClip);
        }
        else
        {
            projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(camera.FOV),
                width / (float)Math.Max(1, height),
                nearClip, farClip);
        }
        var eye = ToTk(camera.Position);
        Matrix4 view = Matrix4.LookAt(eye, eye + ToTk(camera.Front), ToTk(camera.Up));
        Matrix4 mvp = view * projection;
        ComputeSunScreenPosition(objects, eye, mvp, ToTk(camera.Front));
        var frustum = new Frustum(mvp);
        _frustumCullingEnabled = camera.FrustumCulling;
        _occlusionCullingEnabled = camera.OcclusionCulling;
        _activeCullingMask = camera.CullingMask;
        if (_staticBatchCullingMask != _activeCullingMask)
            _staticDirty = true;

        if (AutoInvalidateStaticBatch && !_staticDirty && _frameCount >= _nextStaticSignatureCheckFrame)
        {
            _nextStaticSignatureCheckFrame = _frameCount + StaticSignatureCheckIntervalFrames;
            int staticSignature = ComputeStaticBatchSignature(objects);
            if (staticSignature != _staticBatchSignature)
            {
                _staticBatchSignature = staticSignature;
                _staticDirty = true;
            }
        }

        // ── Reconstruir static batch si está sucio ─────────────────
        if (_staticDirty)
        {
            _staticBuildList.Clear();
            _staticRanges.Clear();
            _buildingStatic = true;
            try
            {
                foreach (var obj in objects)
                    if (obj.Parent == null)
                        BuildObjectRecursive(obj, selected, frustum, Matrix4.Identity);
            }
            finally
            {
                _buildingStatic = false;
            }

            // Subir a GPU con StaticDraw (optimizado para lectura frecuente, escritura rara)
            UploadStaticBatch();
            _staticDirty = false;
            _staticBatchCullingMask = _activeCullingMask;
            if (AutoInvalidateStaticBatch)
            {
                _staticBatchSignature = ComputeStaticBatchSignature(objects);
                _nextStaticSignatureCheckFrame = _frameCount + StaticSignatureCheckIntervalFrames;
            }
        }

        // ── Construir vértices dinámicos (excluye objetos estáticos) ──
        vertices.Clear();
        solidVertices.Clear();
        solidRanges.Clear();
        dynamicMeshDraws.Clear();
        refractiveMeshDraws.Clear();
        skinnedMeshDraws.Clear();
        refractiveSkinnedMeshDraws.Clear();
        shaderGraphDynamicMeshDraws.Clear();
        shaderGraphSkinnedMeshDraws.Clear();
        _pendingOcclusionObjects.Clear();   // reset occlusion gather list each frame
        if (showGrid)
            BuildGrid();
        foreach (var obj in objects)
            if (obj.Parent == null)
                BuildObjectRecursive(obj, selected, frustum, Matrix4.Identity);
        CollectParticleMeshDraws(objects, ToTk(camera.Position));

        if (vertices.Count == 0 && solidVertices.Count == 0 && dynamicMeshDraws.Count == 0 && refractiveMeshDraws.Count == 0 && skinnedMeshDraws.Count == 0 && refractiveSkinnedMeshDraws.Count == 0 &&
            shaderGraphDynamicMeshDraws.Count == 0 && shaderGraphSkinnedMeshDraws.Count == 0 && _staticVertexCount == 0)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);
            EndRenderPass(SceneRenderPass.BuildScene, buildPhaseStart);
            if (_envLoaded && DrawEnvironmentBackground)
            {
                long skyPhaseStart = BeginRenderPass(SceneRenderPass.Skybox);
                DrawSkybox(ToTk(camera.Front), ToTk(camera.Up), camera.FOV, width / (float)Math.Max(1, height), FindPrimaryDirectionalLight(objects));
                EndRenderPass(SceneRenderPass.Skybox, skyPhaseStart);
            }
            long particlesPhaseStarts = BeginRenderPass(SceneRenderPass.Particles);
            DrawParticles(objects, ref view, ref mvp, eye);
            EndRenderPass(SceneRenderPass.Particles, particlesPhaseStarts);
            FinalizeRenderOther(renderFrameStart);
            PublishRenderStats(width, height);
            return;
        }

        if (solidVertices.Count > 0)
        {
            EnsureSolidCapacity(solidVertices.Count * Unsafe.SizeOf<SolidVertex>());
            GL.BindBuffer(BufferTarget.ArrayBuffer, solidVertexBuffer);
            EnsureSolidUploadBuffer();
            solidVertices.CopyTo(solidUploadBuffer);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, solidVertices.Count * Unsafe.SizeOf<SolidVertex>(), solidUploadBuffer);
        }

        SortDynamicMeshDrawsForStateReuse();
        EndRenderPass(SceneRenderPass.BuildScene, buildPhaseStart);

        long shadowPhaseStart = BeginRenderPass(SceneRenderPass.Shadows);
        var (shadowInfo, spotShadowInfo, pointShadowInfo) = PrepareRealtimeShadows(objects, camera, width, height);
        EndRenderPass(SceneRenderPass.Shadows, shadowPhaseStart);

        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);

        // ── Skybox HDRI de fondo (solo si hay HDRI cargado) ─────────
        if (_envLoaded && DrawEnvironmentBackground)
        {
            long skyPhaseStart = BeginRenderPass(SceneRenderPass.Skybox);
            DrawSkybox(ToTk(camera.Front), ToTk(camera.Up), camera.FOV, width / (float)Math.Max(1, height), FindPrimaryDirectionalLight(objects));
            EndRenderPass(SceneRenderPass.Skybox, skyPhaseStart);
        }

        long staticPhaseStart = BeginRenderPass(SceneRenderPass.StaticOpaque);
        // ── Dibujar static batch (1 setup, N draw calls por textura) ─
        if (_staticVertexCount > 0)
        {
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            ApplyBakedSolidVertexMode();
            ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
            GL.Uniform1(solidTextureLocation, 0);
            SetIntUniform(solidRefractivePassLocation, 0);
            ApplySceneLighting(objects, ToTk(camera.Position));
            GL.BindVertexArray(_staticVertexArray);
            GL.ActiveTexture(TextureUnit.Texture0);
            foreach (var range in _staticRanges)
            {
                if (range.ShaderGraphPath != null || range.TerrainObject != null) continue;
                ApplyRangeTextures(range);
                TrackMainDraw(range.Count);
                GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
            }
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        EndRenderPass(SceneRenderPass.StaticOpaque, staticPhaseStart);

        long dynamicPhaseStart = BeginRenderPass(SceneRenderPass.DynamicOpaque);
        // ── Dibujar objetos dinámicos ─────────────────────────────
        if (dynamicMeshDraws.Count > 0 || skinnedMeshDraws.Count > 0)
        {
            Vector3 cameraPosTk = ToTk(camera.Position);
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            GL.Uniform1(solidTextureLocation, 0);
            ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
            SetIntUniform(solidRefractivePassLocation, 0);
            ApplySceneLighting(objects, cameraPosTk);
            GL.ActiveTexture(TextureUnit.Texture0);

            if (dynamicMeshDraws.Count > 0)
                DrawDynamicMeshDraws(ref mvp, shadowInfo, spotShadowInfo, pointShadowInfo, objects, cameraPosTk, manageShaderState: false);

            if (skinnedMeshDraws.Count > 0)
                DrawSkinnedMeshDraws(ref mvp, shadowInfo, spotShadowInfo, pointShadowInfo, objects, cameraPosTk, manageShaderState: false);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            ApplyBakedSolidVertexMode();
            GL.UseProgram(0);
        }

        if (solidVertices.Count > 0)
        {
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            ApplyBakedSolidVertexMode();
            ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
            GL.Uniform1(solidTextureLocation, 0);
            SetIntUniform(solidRefractivePassLocation, 0);
            ApplySceneLighting(objects, ToTk(camera.Position));
            GL.BindVertexArray(solidVertexArray);
            GL.ActiveTexture(TextureUnit.Texture0);
            foreach (var range in solidRanges)
            {
                if (range.ShaderGraphPath != null || range.TerrainObject != null) continue;
                ApplyRangeTextures(range);
                TrackMainDraw(range.Count);
                GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
            }
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
        EndRenderPass(SceneRenderPass.DynamicOpaque, dynamicPhaseStart);

        long shaderGraphPhaseStart = BeginRenderPass(SceneRenderPass.ShaderGraph);
        // ── Dibujar objetos con material Shader Graph (shaders custom) ──
        bool needsShaderGraphSceneDepth =
            ShaderGraphRangesNeedSceneDepth(_staticRanges) ||
            ShaderGraphRangesNeedSceneDepth(solidRanges) ||
            ShaderGraphDynamicDrawsNeedSceneDepth() ||
            ShaderGraphSkinnedDrawsNeedSceneDepth();

        if (needsShaderGraphSceneDepth)
        {
            RenderShaderGraphDepthPrepass(_staticRanges, _staticVertexArray, ref mvp);
            RenderShaderGraphDepthPrepass(solidRanges, solidVertexArray, ref mvp);
            CaptureDepthBuffer();
        }

        if (_staticVertexCount > 0)
            RenderShaderGraphRanges(_staticRanges, _staticVertexArray, ref mvp, ToTk(camera.Position), width, height, objects, camera.NearClip, camera.FarClip);
        if (solidVertices.Count > 0)
            RenderShaderGraphRanges(solidRanges, solidVertexArray, ref mvp, ToTk(camera.Position), width, height, objects, camera.NearClip, camera.FarClip);
        if (shaderGraphDynamicMeshDraws.Count > 0)
            RenderShaderGraphDynamicMeshDraws(ref mvp, ToTk(camera.Position), width, height, objects, camera.NearClip, camera.FarClip);
        if (shaderGraphSkinnedMeshDraws.Count > 0)
            RenderShaderGraphSkinnedMeshDraws(ref mvp, ToTk(camera.Position), width, height, objects, camera.NearClip, camera.FarClip);
        EndRenderPass(SceneRenderPass.ShaderGraph, shaderGraphPhaseStart);

        long terrainPhaseStart = BeginRenderPass(SceneRenderPass.Terrain);
        // ── Dibujar Terrain (pintura de capas vía splat map) ────────────
        if (_staticVertexCount > 0)
            RenderTerrainRanges(_staticRanges, _staticVertexArray, ref mvp, objects);
        if (solidVertices.Count > 0)
            RenderTerrainRanges(solidRanges, solidVertexArray, ref mvp, objects);
        EndRenderPass(SceneRenderPass.Terrain, terrainPhaseStart);

        if (refractiveMeshDraws.Count > 0 || refractiveSkinnedMeshDraws.Count > 0)
        {
            ResolveAndCaptureOpaqueTexture?.Invoke();
            DrawRefractiveMeshDraws(ref mvp, shadowInfo, spotShadowInfo, pointShadowInfo, objects, ToTk(camera.Position), width, height);
        }

        GL.Disable(EnableCap.CullFace);

        // ── Issue occlusion queries after opaque geometry is drawn ─
        if (_occlusionCullingEnabled && _pendingOcclusionObjects.Count > 0)
        {
            long occlusionPhaseStart = BeginRenderPass(SceneRenderPass.Occlusion);
            ProcessOcclusionQueries(ref mvp, objects);
            EndRenderPass(SceneRenderPass.Occlusion, occlusionPhaseStart);
        }

        long linesPhaseStart = BeginRenderPass(SceneRenderPass.LinesGizmos);
        if (vertices.Count > 0)
        {
            EnsureCapacity(vertices.Count * Unsafe.SizeOf<LineVertex>());
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);
            EnsureLineUploadBuffer();
            vertices.CopyTo(lineUploadBuffer);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Count * Unsafe.SizeOf<LineVertex>(), lineUploadBuffer);

            GL.UseProgram(shader);
            GL.UniformMatrix4(mvpLocation, true, ref mvp);
            GL.BindVertexArray(vertexArray);
            GL.LineWidth(1f);
            TrackLineDraw();
            GL.DrawArrays(PrimitiveType.Lines, 0, vertices.Count);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
        EndRenderPass(SceneRenderPass.LinesGizmos, linesPhaseStart);

        // ── Partículas billboard ───────────────────────────────────
        long particlesPhaseStart = BeginRenderPass(SceneRenderPass.Particles);
        DrawParticles(objects, ref view, ref mvp, eye);
        EndRenderPass(SceneRenderPass.Particles, particlesPhaseStart);
        FinalizeRenderOther(renderFrameStart);
        PublishRenderStats(width, height);
    }

    private readonly List<ParticleRenderRange> _particleRanges = new();

    // Buffer temporal para ordenar partículas por distancia (back-to-front)
    private readonly List<(Particle P, float Dist)> _sortBuf = new(512);

    private void ProcessSubEmitters(GameObject obj, IReadOnlyList<GameObject> allObjects)
    {
        var ps = obj.GetComponent<GrokoEngine.ParticleSystem>();
        if (ps == null) return;

        void TriggerSubEmitter(string editorId, IEnumerable<MiMotor.Mathematics.Vector3> positions)
        {
            if (string.IsNullOrEmpty(editorId)) return;
            var target = FindByEditorId(allObjects, editorId);
            var targetPs = target?.GetComponent<GrokoEngine.ParticleSystem>();
            if (targetPs == null) return;
            foreach (var pos in positions)
            {
                targetPs.gameObject.transform.Position = new MiMotor.Mathematics.Vector3(pos.X, pos.Y, pos.Z);
                targetPs.gameObject.PosX = pos.X;
                targetPs.gameObject.PosY = pos.Y;
                targetPs.gameObject.PosZ = pos.Z;
                for (int i = 0; i < ps.SubEmitterCount; i++)
                    targetPs.EmitOne();
            }
        }

        TriggerSubEmitter(ps.SubEmitterDeath, ps._deathPositions);
        TriggerSubEmitter(ps.SubEmitterBirth, ps._birthPositions);
        ps._birthPositions.Clear();

        foreach (var child in obj.Children) ProcessSubEmitters(child, allObjects);
    }

    /// <summary>Quad estirado en la dirección de la velocidad (lluvia, chispas rápidas).</summary>
    private void AddStretchedQuad(Particle p, MiMotor.Mathematics.Vector3 worldPos, Vector3 right, Vector3 up,
                                    Vector4 color, float speedScale, float lengthScale,
                                    int sheetCols, int sheetRows, float sheetFps, float sizeMultiplier = 1f,
                                    bool allowRoll = true, bool flipU = false, bool flipV = false,
                                    float pivotX = 0f, float pivotY = 0f)
    {
        float speed = MathF.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y + p.Velocity.Z * p.Velocity.Z);
        if (speed < 0.001f)
        {
            AddParticleQuad(p, worldPos, right, up, sheetCols, sheetRows, sheetFps, color, sizeMultiplier,
                            allowRoll, flipU, flipV, pivotX, pivotY);
            return;
        }

        // Dirección de movimiento → eje del estiramiento (OpenTK Vector3)
        var vel = new Vector3(p.Velocity.X / speed, p.Velocity.Y / speed, p.Velocity.Z / speed);
        var wpos = new Vector3(worldPos.X, worldPos.Y, worldPos.Z);
        float halfW = p.CurrentSize * sizeMultiplier * 0.5f;
        float halfH = halfW + speed * speedScale * lengthScale;

        // Eje perpendicular al movimiento en el plano de la cámara
        float cx = vel.Y * right.Z - vel.Z * right.Y;
        float cy = vel.Z * right.X - vel.X * right.Z;
        float cz = vel.X * right.Y - vel.Y * right.X;
        float cl = MathF.Sqrt(cx * cx + cy * cy + cz * cz);
        if (cl < 0.001f) { AddParticleQuad(p, worldPos, right, up, sheetCols, sheetRows, sheetFps, color, sizeMultiplier); return; }
        var perp = new Vector3(cx / cl * halfW, cy / cl * halfW, cz / cl * halfW);
        var fwd = new Vector3(vel.X * halfH, vel.Y * halfH, vel.Z * halfH);

        // Calcular UVs de sheet
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
        if (sheetCols > 1 || sheetRows > 1)
        {
            int tf = sheetCols * sheetRows, frame = (int)(p.Age * sheetFps) % Math.Max(1, tf);
            int col = frame % sheetCols, row = frame / sheetCols;
            float fw = 1f / sheetCols, fh = 1f / sheetRows;
            u0 = col * fw; u1 = u0 + fw; v0 = row * fh; v1 = v0 + fh;
        }
        if (flipU) (u0, u1) = (u1, u0);
        if (flipV) (v0, v1) = (v1, v0);

        float px = Math.Clamp(pivotX, -1f, 1f);
        float py = Math.Clamp(pivotY, -1f, 1f);
        var pivotOffset = new Vector3(perp.X * px + fwd.X * py, perp.Y * px + fwd.Y * py, perp.Z * px + fwd.Z * py);
        wpos -= pivotOffset;

        var a2 = new Vector3(wpos.X - perp.X + fwd.X, wpos.Y - perp.Y + fwd.Y, wpos.Z - perp.Z + fwd.Z);
        var b2 = new Vector3(wpos.X + perp.X + fwd.X, wpos.Y + perp.Y + fwd.Y, wpos.Z + perp.Z + fwd.Z);
        var c2 = new Vector3(wpos.X + perp.X - fwd.X, wpos.Y + perp.Y - fwd.Y, wpos.Z + perp.Z - fwd.Z);
        var d2 = new Vector3(wpos.X - perp.X - fwd.X, wpos.Y - perp.Y - fwd.Y, wpos.Z - perp.Z - fwd.Z);

        _particleVerts.Add(new ParticleVertex(a2, Vector2.Zero, color, 0f, new Vector2(u0, v1), 1f));
        _particleVerts.Add(new ParticleVertex(b2, Vector2.Zero, color, 0f, new Vector2(u1, v1), 1f));
        _particleVerts.Add(new ParticleVertex(c2, Vector2.Zero, color, 0f, new Vector2(u1, v0), 1f));
        _particleVerts.Add(new ParticleVertex(a2, Vector2.Zero, color, 0f, new Vector2(u0, v1), 1f));
        _particleVerts.Add(new ParticleVertex(c2, Vector2.Zero, color, 0f, new Vector2(u1, v0), 1f));
        _particleVerts.Add(new ParticleVertex(d2, Vector2.Zero, color, 0f, new Vector2(u0, v0), 1f));
    }

    // Depth/shadow batching helpers kept close to renderer type definitions.

    private static bool CanDepthInstanceTogether(DynamicMeshDraw a, DynamicMeshDraw b) =>
        a.CastShadows &&
        b.CastShadows &&
        ReferenceEquals(a.Mesh, b.Mesh) &&
        a.Start == b.Start &&
        a.Count == b.Count;

    // Dedicated scratch buffers for bounds and occlusion debug drawing.

    private readonly List<SolidVertex> _bboxScratch = new(36);
    private SolidVertex[] _bboxUploadBuffer = Array.Empty<SolidVertex>();
}
