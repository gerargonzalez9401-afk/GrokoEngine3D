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
using GrokoShaderGraphPro.Models;
using GrokoShaderGraphPro.Services;
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
    private readonly List<SkinnedMeshDraw> skinnedMeshDraws = new(64);
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

    /// <summary>
    /// Llama esto cuando cualquier objeto IsStatic cambia, se añaden/quitan
    /// objetos estáticos, o se modifica su mesh/material.
    /// </summary>
    public void InvalidateStaticBatch()
    {
        _staticDirty = true;
        _staticBatchSignature = 0;
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
    private readonly Dictionary<string, (DateTime FileTime, MaterialAssetData Data)> _particleMaterialCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Occlusion Culling ──────────────────────────────────────────
    /// <summary>Estado de culling de la cámara activa para el frame actual (ver Camera.FrustumCulling / OcclusionCulling).</summary>
    private bool _frustumCullingEnabled = true;
    private bool _occlusionCullingEnabled = false;
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

    // ── Entorno HDRI (IBL Fase 2) ─────────────────────────────────────────
    private int _envTexture;          // textura equirectangular RGB16F (con mips)
    private bool _envLoaded;
    private float _envMaxLod;
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
    

    

    private static long RenderTimestamp() => Stopwatch.GetTimestamp();

    private static float RenderElapsedMs(long start) =>
        (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private void ResetRenderStats()
    {
        _statsDrawCalls = 0;
        _statsShadowDrawCalls = 0;
        _statsInstancedDrawCalls = 0;
        _statsInstances = 0;
        _statsTriangles = 0;
        _statsLineDrawCalls = 0;
        _statsParticleDrawCalls = 0;
        _statsBuildSceneMs = 0f;
        _statsShadowMs = 0f;
        _statsSkyboxMs = 0f;
        _statsStaticOpaqueMs = 0f;
        _statsDynamicOpaqueMs = 0f;
        _statsShaderGraphMs = 0f;
        _statsTerrainMs = 0f;
        _statsLinesGizmosMs = 0f;
        _statsParticlesMs = 0f;
        _statsOcclusionMs = 0f;
        _statsDirectionalShadowMs = 0f;
        _statsSpotShadowMs = 0f;
        _statsPointShadowMs = 0f;
        _statsRenderOtherMs = 0f;
    }

    private void TrackMainDraw(int vertexCount, int instanceCount = 1)
    {
        _statsDrawCalls++;
        if (instanceCount > 1)
        {
            _statsInstancedDrawCalls++;
            _statsInstances += instanceCount;
        }

        _statsTriangles += Math.Max(0, vertexCount / 3) * Math.Max(1, instanceCount);
    }

    


    private void TrackLineDraw()
    {
        _statsDrawCalls++;
        _statsLineDrawCalls++;
    }

    


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
            dynamicMeshDraws.Count + skinnedMeshDraws.Count + shaderGraphDynamicMeshDraws.Count + shaderGraphSkinnedMeshDraws.Count,
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

    public SceneViewportRenderer()
    {
        // Sin esto, las consultas a samplerCube en los bordes de cada cara filtran
        // contra el borde de ESA cara únicamente — los texels de la cara vecina no
        // participan, así que el PCF de las point lights (que muestrea un disco
        // alrededor de la dirección luz→fragmento y a veces cruza de una cara a
        // otra) ve una discontinuidad justo en la costura entre caras del cubemap.
        // Eso dibuja arcos/curvas visibles en las sombras de point lights al subir
        // la calidad — exactamente el patrón de "lentes" reportado. Habilitar el
        // filtrado "seamless" hace que OpenGL interpole correctamente a través de
        // las costuras, igual que hacen los motores de producción (Unity/Unreal).
        GL.Enable(EnableCap.TextureCubeMapSeamless);

        vertexArray = GL.GenVertexArray();
        vertexBuffer = GL.GenBuffer();
        shader = CreateShader();
        mvpLocation = GL.GetUniformLocation(shader, "uMvp");

        solidVertexArray = GL.GenVertexArray();
        solidVertexBuffer = GL.GenBuffer();
        dynamicInstanceBuffer = GL.GenBuffer();
        InitializeInstanceBuffer();
        solidShader = CreateSolidShader();
        _shaderGraphDepthPrepassShader = CreateShaderGraphDepthPrepassShader();
        _shaderGraphDepthPrepassViewProjLocation = GL.GetUniformLocation(_shaderGraphDepthPrepassShader, "u_ViewProj");
        solidMvpLocation = GL.GetUniformLocation(solidShader, "uMvp");
        solidModelLocation = GL.GetUniformLocation(solidShader, "uModel");
        solidUseInstancingLocation = GL.GetUniformLocation(solidShader, "uUseInstancing");
        solidUseSkinningLocation = GL.GetUniformLocation(solidShader, "uUseSkinning");
        solidUseSurfaceUniformsLocation = GL.GetUniformLocation(solidShader, "uUseSurfaceUniforms");
        solidSurfaceColorLocation = GL.GetUniformLocation(solidShader, "uSurfaceColor");
        solidSurfaceMaterialLocation = GL.GetUniformLocation(solidShader, "uSurfaceMaterial");
        solidSurfaceEmissionLocation = GL.GetUniformLocation(solidShader, "uSurfaceEmission");
        solidTextureLocation = GL.GetUniformLocation(solidShader, "uTexture");
        solidHasTextureLocation = GL.GetUniformLocation(solidShader, "uHasTexture");
        solidNormalMapLocation = GL.GetUniformLocation(solidShader, "uNormalMap");
        solidHasNormalMapLocation = GL.GetUniformLocation(solidShader, "uHasNormalMap");
        solidRoughnessMapLocation = GL.GetUniformLocation(solidShader, "uRoughnessMap");
        solidHasRoughnessMapLocation = GL.GetUniformLocation(solidShader, "uHasRoughnessMap");
        solidMetallicMapLocation = GL.GetUniformLocation(solidShader, "uMetallicMap");
        solidHasMetallicMapLocation = GL.GetUniformLocation(solidShader, "uHasMetallicMap");
        solidLightMvpLocation = GL.GetUniformLocation(solidShader, "uLightMvp");
        solidCascadeCountLocation = GL.GetUniformLocation(solidShader, "uCascadeCount");
        solidCameraPositionLocation = GL.GetUniformLocation(solidShader, "uShadowCameraPos");
        for (int i = 0; i < MaxDirectionalShadowCascades; i++)
        {
            solidCascadeLightMvpLocations[i] = GL.GetUniformLocation(solidShader, $"uCascadeLightMvp[{i}]");
            solidCascadeSplitLocations[i] = GL.GetUniformLocation(solidShader, $"uCascadeSplit[{i}]");
        }
        solidShadowMapLocation = GL.GetUniformLocation(solidShader, "uShadowMap");
        solidShadowEnabledLocation = GL.GetUniformLocation(solidShader, "uShadowEnabled");
        solidShadowStrengthLocation = GL.GetUniformLocation(solidShader, "uShadowStrength");
        solidShadowPcfRadiusLocation = GL.GetUniformLocation(solidShader, "uShadowPcfRadius");
        solidSpotShadowMapLocation = GL.GetUniformLocation(solidShader, "uSpotShadowMap");
        solidSpotLightMvpLocation = GL.GetUniformLocation(solidShader, "uSpotLightMvp");
        solidSpotShadowEnabledLocation = GL.GetUniformLocation(solidShader, "uSpotShadowEnabled");
        solidSpotShadowStrengthLocation = GL.GetUniformLocation(solidShader, "uSpotShadowStrength");
        solidPointShadowCubeLocation = GL.GetUniformLocation(solidShader, "uPointShadowCube");
        solidPointShadowEnabledLocation = GL.GetUniformLocation(solidShader, "uPointShadowEnabled");
        solidPointShadowStrengthLocation = GL.GetUniformLocation(solidShader, "uPointShadowStrength");
        solidPointShadowPosLocation = GL.GetUniformLocation(solidShader, "uPointShadowPos");
        solidPointShadowFarLocation = GL.GetUniformLocation(solidShader, "uPointShadowFar");
        solidShadowBiasScaleLocation = GL.GetUniformLocation(solidShader, "uShadowBiasScale");
        for (int i = 0; i < MaxGpuBones; i++)
            solidBoneMatrixLocations[i] = GL.GetUniformLocation(solidShader, $"uBones[{i}]");
        uAmbientColor = GL.GetUniformLocation(solidShader, "uAmbientColor");
        uAmbientIntensity = GL.GetUniformLocation(solidShader, "uAmbientIntensity");
        uSkyStrength = GL.GetUniformLocation(solidShader, "uSkyStrength");
        uDirDir = GL.GetUniformLocation(solidShader, "uDirDir");
        uDirColor = GL.GetUniformLocation(solidShader, "uDirColor");
        uDirIntensity = GL.GetUniformLocation(solidShader, "uDirIntensity");
        uCameraPos = GL.GetUniformLocation(solidShader, "uCameraPos");
        uPointCount = GL.GetUniformLocation(solidShader, "uPointCount");
        for (int i = 0; i < MaxPointLights; i++)
        {
            uPointPos[i] = GL.GetUniformLocation(solidShader, $"uPointPos[{i}]");
            uPointColor[i] = GL.GetUniformLocation(solidShader, $"uPointColor[{i}]");
            uPointIntensity[i] = GL.GetUniformLocation(solidShader, $"uPointIntensity[{i}]");
            uPointRange[i] = GL.GetUniformLocation(solidShader, $"uPointRange[{i}]");
        }
        uSpotCount = GL.GetUniformLocation(solidShader, "uSpotCount");
        for (int i = 0; i < MaxSpotLights; i++)
        {
            uSpotPos[i] = GL.GetUniformLocation(solidShader, $"uSpotPos[{i}]");
            uSpotDir[i] = GL.GetUniformLocation(solidShader, $"uSpotDir[{i}]");
            uSpotColor[i] = GL.GetUniformLocation(solidShader, $"uSpotColor[{i}]");
            uSpotIntensity[i] = GL.GetUniformLocation(solidShader, $"uSpotIntensity[{i}]");
            uSpotRange[i] = GL.GetUniformLocation(solidShader, $"uSpotRange[{i}]");
            uSpotAngle[i] = GL.GetUniformLocation(solidShader, $"uSpotAngle[{i}]");
        }
        uAreaCount = GL.GetUniformLocation(solidShader, "uAreaCount");
        for (int i = 0; i < MaxAreaLights; i++)
        {
            uAreaPos[i] = GL.GetUniformLocation(solidShader, $"uAreaPos[{i}]");
            uAreaDir[i] = GL.GetUniformLocation(solidShader, $"uAreaDir[{i}]");
            uAreaColor[i] = GL.GetUniformLocation(solidShader, $"uAreaColor[{i}]");
            uAreaIntensity[i] = GL.GetUniformLocation(solidShader, $"uAreaIntensity[{i}]");
            uAreaRange[i] = GL.GetUniformLocation(solidShader, $"uAreaRange[{i}]");
            uAreaSize[i] = GL.GetUniformLocation(solidShader, $"uAreaSize[{i}]");
        }
        uColorSpaceLinear = GL.GetUniformLocation(solidShader, "uColorSpaceLinear");
        uUseIBL = GL.GetUniformLocation(solidShader, "uUseIBL");
        uEnvMap = GL.GetUniformLocation(solidShader, "uEnvMap");
        uHasEnvMap = GL.GetUniformLocation(solidShader, "uHasEnvMap");
        uEnvMaxLod = GL.GetUniformLocation(solidShader, "uEnvMaxLod");
        uAoStrength = GL.GetUniformLocation(solidShader, "uAoStrength");
        uFogDensity = GL.GetUniformLocation(solidShader, "uFogDensity");
        uFogColor = GL.GetUniformLocation(solidShader, "uFogColor");
        uVolumetricStrength = GL.GetUniformLocation(solidShader, "uVolumetricStrength");
        uDebugView = GL.GetUniformLocation(solidShader, "uDebugView");

        terrainShader = CreateTerrainShader();
        terrainMvpLocation = GL.GetUniformLocation(terrainShader, "uMvp");
        terrainModelLocation = GL.GetUniformLocation(terrainShader, "uModel");
        terrainSplatMapLocation = GL.GetUniformLocation(terrainShader, "uSplatMap");
        for (int i = 0; i < 4; i++)
        {
            terrainLayerLocations[i] = GL.GetUniformLocation(terrainShader, $"uLayer{i}");
            terrainHasLayerLocations[i] = GL.GetUniformLocation(terrainShader, $"uHasLayer{i}");
            terrainTilingLocations[i] = GL.GetUniformLocation(terrainShader, $"uTiling{i}");
        }
        terrainAmbientColorLocation = GL.GetUniformLocation(terrainShader, "uAmbientColor");
        terrainAmbientIntensityLocation = GL.GetUniformLocation(terrainShader, "uAmbientIntensity");
        terrainSkyStrengthLocation = GL.GetUniformLocation(terrainShader, "uSkyStrength");
        terrainDirDirLocation = GL.GetUniformLocation(terrainShader, "uDirDir");
        terrainDirColorLocation = GL.GetUniformLocation(terrainShader, "uDirColor");
        terrainDirIntensityLocation = GL.GetUniformLocation(terrainShader, "uDirIntensity");

        GL.BindVertexArray(vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);

        int stride = Unsafe.SizeOf<LineVertex>();
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 12);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        GL.BindVertexArray(solidVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, solidVertexBuffer);

        int solidStride = Unsafe.SizeOf<SolidVertex>();
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, solidStride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, solidStride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, solidStride, 24);
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, solidStride, 40);
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, solidStride, 48);
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, solidStride, 64);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // VAO/VBO para el batch estático (misma configuración de atributos)
        _staticVertexArray = GL.GenVertexArray();
        _staticVertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_staticVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _staticVertexBuffer);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, solidStride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, solidStride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, solidStride, 24);
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, solidStride, 40);
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, solidStride, 48);
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, solidStride, 64);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // ── Particle shader + VAO ──────────────────────────────────
        _particleShader = CreateParticleShader();
        _particleMvpLoc = GL.GetUniformLocation(_particleShader, "uMvp");
        _particleRightLoc = GL.GetUniformLocation(_particleShader, "uCamRight");
        _particleUpLoc = GL.GetUniformLocation(_particleShader, "uCamUp");
        _particleTexLoc = GL.GetUniformLocation(_particleShader, "uTexture");
        _particleHasTexLoc = GL.GetUniformLocation(_particleShader, "uHasTexture");
        _particleSoftLoc = GL.GetUniformLocation(_particleShader, "uSoftParticles");
        _particleDepthTexLoc = GL.GetUniformLocation(_particleShader, "uDepthTex");
        _particleScreenSizeLoc = GL.GetUniformLocation(_particleShader, "uScreenSize");
        _particleSoftRangeLoc = GL.GetUniformLocation(_particleShader, "uSoftRange");

        // Trail VAO (misma estructura que partículas)
        _trailVao = GL.GenVertexArray();
        _trailVbo = GL.GenBuffer();
        GL.BindVertexArray(_trailVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _trailVbo);
        int ps2 = Unsafe.SizeOf<ParticleVertex>();
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, ps2, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, ps2, 12);
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, ps2, 20);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, ps2, 36);
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, ps2, 40);
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, ps2, 48);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // ── Skybox HDRI (IBL Fase 2) ───────────────────────────────
        _skyboxShader = CreateSkyboxShader();
        _skyboxVao = GL.GenVertexArray(); // VAO vacío: el triángulo fullscreen se genera con gl_VertexID
        _skyEnvLoc = GL.GetUniformLocation(_skyboxShader, "uEnvMap");
        _skyCamFrontLoc = GL.GetUniformLocation(_skyboxShader, "uCamFront");
        _skyCamUpLoc = GL.GetUniformLocation(_skyboxShader, "uCamUp");
        _skyTanHalfFovLoc = GL.GetUniformLocation(_skyboxShader, "uTanHalfFov");
        _skyAspectLoc = GL.GetUniformLocation(_skyboxShader, "uAspect");
        _skySunDirLoc = GL.GetUniformLocation(_skyboxShader, "uSunDir");
        _skySunColorLoc = GL.GetUniformLocation(_skyboxShader, "uSunColor");
        _skySunAngularLoc = GL.GetUniformLocation(_skyboxShader, "uSunAngular");
        _skySunEnabledLoc = GL.GetUniformLocation(_skyboxShader, "uSunEnabled");

        // VAO/VBO dedicado para occlusion query bounding boxes
        _occVao = GL.GenVertexArray();
        _occVbo = GL.GenBuffer();
        GL.BindVertexArray(_occVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _occVbo);
        int occStride = Unsafe.SizeOf<SolidVertex>();
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, occStride, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, occStride, 12);
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, occStride, 24);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, occStride, 40);
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, occStride, 48);
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, occStride, 64);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // Depth FBO — se dimensiona en el primer frame
        _depthFbo = GL.GenFramebuffer();
        _depthTex = GL.GenTexture();
        _shadowShader = CreateShadowShader();
        _shadowMvpLocation = GL.GetUniformLocation(_shadowShader, "uMvp");
        _shadowModelLocation = GL.GetUniformLocation(_shadowShader, "uModel");
        _shadowUseInstancingLocation = GL.GetUniformLocation(_shadowShader, "uUseInstancing");
        _shadowUseSkinningLocation = GL.GetUniformLocation(_shadowShader, "uUseSkinning");
        for (int i = 0; i < MaxGpuBones; i++)
            _shadowBoneMatrixLocations[i] = GL.GetUniformLocation(_shadowShader, $"uBones[{i}]");
        _pointShadowShader = CreatePointShadowShader();
        _pointShadowMvpLocation = GL.GetUniformLocation(_pointShadowShader, "uMvp");
        _pointShadowModelLocation = GL.GetUniformLocation(_pointShadowShader, "uModel");
        _pointShadowUseInstancingLocation = GL.GetUniformLocation(_pointShadowShader, "uUseInstancing");
        _pointShadowUseSkinningLocation = GL.GetUniformLocation(_pointShadowShader, "uUseSkinning");
        _pointShadowLightPosLocation = GL.GetUniformLocation(_pointShadowShader, "uLightPos");
        _pointShadowFarLocation = GL.GetUniformLocation(_pointShadowShader, "uFarPlane");
        for (int i = 0; i < MaxGpuBones; i++)
            _pointShadowBoneMatrixLocations[i] = GL.GetUniformLocation(_pointShadowShader, $"uBones[{i}]");
        EnsureShadowResources();

        _particleVao = GL.GenVertexArray();
        _particleVbo = GL.GenBuffer();
        GL.BindVertexArray(_particleVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
        int ps = Unsafe.SizeOf<ParticleVertex>();
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, ps, 0);   // center
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, ps, 12);  // offset
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, ps, 20);  // color
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, ps, 36);  // rotation
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, ps, 40);  // uv
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, ps, 48);  // absolute vertices
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
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
        long buildPhaseStart = renderFrameStart;

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

        if (AutoInvalidateStaticBatch)
        {
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
        }

        // ── Construir vértices dinámicos (excluye objetos estáticos) ──
        vertices.Clear();
        solidVertices.Clear();
        solidRanges.Clear();
        dynamicMeshDraws.Clear();
        skinnedMeshDraws.Clear();
        shaderGraphDynamicMeshDraws.Clear();
        shaderGraphSkinnedMeshDraws.Clear();
        _pendingOcclusionObjects.Clear();   // reset occlusion gather list each frame
        if (showGrid)
            BuildGrid();
        foreach (var obj in objects)
            if (obj.Parent == null)
                BuildObjectRecursive(obj, selected, frustum, Matrix4.Identity);
        CollectParticleMeshDraws(objects, ToTk(camera.Position));

        if (vertices.Count == 0 && solidVertices.Count == 0 && dynamicMeshDraws.Count == 0 && skinnedMeshDraws.Count == 0 &&
            shaderGraphDynamicMeshDraws.Count == 0 && shaderGraphSkinnedMeshDraws.Count == 0 && _staticVertexCount == 0)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);
            _statsBuildSceneMs = RenderElapsedMs(buildPhaseStart);
            if (_envLoaded && DrawEnvironmentBackground)
            {
                long skyPhaseStart = RenderTimestamp();
                DrawSkybox(ToTk(camera.Front), ToTk(camera.Up), camera.FOV, width / (float)Math.Max(1, height), FindPrimaryDirectionalLight(objects));
                _statsSkyboxMs = RenderElapsedMs(skyPhaseStart);
            }
            long particlesPhaseStarts = RenderTimestamp();
            DrawParticles(objects, ref view, ref mvp, eye);
            _statsParticlesMs = RenderElapsedMs(particlesPhaseStarts);
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
        _statsBuildSceneMs = RenderElapsedMs(buildPhaseStart);

        var (shadowInfo, spotShadowInfo, pointShadowInfo) = PrepareRealtimeShadows(objects, camera, width, height);
        _statsShadowMs = _statsDirectionalShadowMs + _statsSpotShadowMs + _statsPointShadowMs;

        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);

        // ── Skybox HDRI de fondo (solo si hay HDRI cargado) ─────────
        if (_envLoaded && DrawEnvironmentBackground)
        {
            long skyPhaseStart = RenderTimestamp();
            DrawSkybox(ToTk(camera.Front), ToTk(camera.Up), camera.FOV, width / (float)Math.Max(1, height), FindPrimaryDirectionalLight(objects));
            _statsSkyboxMs = RenderElapsedMs(skyPhaseStart);
        }

        long staticPhaseStart = RenderTimestamp();
        // ── Dibujar static batch (1 setup, N draw calls por textura) ─
        if (_staticVertexCount > 0)
        {
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            ApplyBakedSolidVertexMode();
            ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
            GL.Uniform1(solidTextureLocation, 0);
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

        _statsStaticOpaqueMs = RenderElapsedMs(staticPhaseStart);

        long dynamicPhaseStart = RenderTimestamp();
        // ── Dibujar objetos dinámicos ─────────────────────────────
        if (dynamicMeshDraws.Count > 0)
            DrawDynamicMeshDraws(ref mvp, shadowInfo, spotShadowInfo, pointShadowInfo, objects, ToTk(camera.Position));

        if (skinnedMeshDraws.Count > 0)
            DrawSkinnedMeshDraws(ref mvp, shadowInfo, spotShadowInfo, pointShadowInfo, objects, ToTk(camera.Position));

        if (solidVertices.Count > 0)
        {
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            ApplyBakedSolidVertexMode();
            ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
            GL.Uniform1(solidTextureLocation, 0);
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
        _statsDynamicOpaqueMs = RenderElapsedMs(dynamicPhaseStart);

        long shaderGraphPhaseStart = RenderTimestamp();
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
        _statsShaderGraphMs = RenderElapsedMs(shaderGraphPhaseStart);

        long terrainPhaseStart = RenderTimestamp();
        // ── Dibujar Terrain (pintura de capas vía splat map) ────────────
        if (_staticVertexCount > 0)
            RenderTerrainRanges(_staticRanges, _staticVertexArray, ref mvp, objects);
        if (solidVertices.Count > 0)
            RenderTerrainRanges(solidRanges, solidVertexArray, ref mvp, objects);
        _statsTerrainMs = RenderElapsedMs(terrainPhaseStart);

        GL.Disable(EnableCap.CullFace);

        // ── Issue occlusion queries after opaque geometry is drawn ─
        if (_occlusionCullingEnabled && _pendingOcclusionObjects.Count > 0)
        {
            long occlusionPhaseStart = RenderTimestamp();
            ProcessOcclusionQueries(ref mvp, objects);
            _statsOcclusionMs = RenderElapsedMs(occlusionPhaseStart);
        }

        long linesPhaseStart = RenderTimestamp();
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
        _statsLinesGizmosMs = RenderElapsedMs(linesPhaseStart);

        // ── Partículas billboard ───────────────────────────────────
        long particlesPhaseStart = RenderTimestamp();
        DrawParticles(objects, ref view, ref mvp, eye);
        _statsParticlesMs = RenderElapsedMs(particlesPhaseStart);
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

    


    


    


    private void BuildGrid()
    {
        float step = Math.Clamp(SceneGridSize, 0.01f, 1024f);
        float opacity = Math.Clamp(SceneGridOpacity, 0f, 1f);
        if (opacity <= 0f)
            return;

        int lines = 24;
        if (cachedGridVertices.Length == 0 ||
            cachedGridAxis != SceneGridAxis ||
            MathF.Abs(cachedGridSize - step) > 0.0001f ||
            MathF.Abs(cachedGridOpacity - opacity) > 0.0001f)
        {
            int start = vertices.Count;
            if (SceneGridAxis == GridAxis.All)
            {
                BuildGridPlane(GridAxis.X, step, opacity * 0.55f, lines);
                BuildGridPlane(GridAxis.Y, step, opacity, lines);
                BuildGridPlane(GridAxis.Z, step, opacity * 0.55f, lines);
            }
            else
            {
                BuildGridPlane(SceneGridAxis, step, opacity, lines);
            }

            int count = vertices.Count - start;
            cachedGridVertices = new LineVertex[count];
            for (int i = 0; i < count; i++)
                cachedGridVertices[i] = vertices[start + i];
            vertices.RemoveRange(start, count);
            cachedGridAxis = SceneGridAxis;
            cachedGridSize = step;
            cachedGridOpacity = opacity;
        }

        vertices.AddRange(cachedGridVertices);
    }

    private void BuildGridPlane(GridAxis axis, float step, float opacity, int lines)
    {
        float span = lines * step;
        for (int i = -lines; i <= lines; i++)
        {
            float p = i * step;
            Vector4 minor = new(0.24f, 0.28f, 0.32f, 0.45f * opacity);
            Vector4 major = new(0.38f, 0.43f, 0.48f, 0.65f * opacity);
            Vector4 xAxis = new(0.72f, 0.28f, 0.24f, 0.95f * opacity);
            Vector4 yAxis = new(0.38f, 0.72f, 0.30f, 0.95f * opacity);
            Vector4 zAxis = new(0.25f, 0.48f, 0.82f, 0.95f * opacity);

            Vector4 aColor = i == 0 ? AxisColor(axis, true, xAxis, yAxis, zAxis) : i % 5 == 0 ? major : minor;
            Vector4 bColor = i == 0 ? AxisColor(axis, false, xAxis, yAxis, zAxis) : i % 5 == 0 ? major : minor;

            switch (axis)
            {
                case GridAxis.X:
                    AddLine(new Vector3(0f, p, -span), new Vector3(0f, p, span), aColor);
                    AddLine(new Vector3(0f, -span, p), new Vector3(0f, span, p), bColor);
                    break;
                case GridAxis.Z:
                    AddLine(new Vector3(p, -span, 0f), new Vector3(p, span, 0f), aColor);
                    AddLine(new Vector3(-span, p, 0f), new Vector3(span, p, 0f), bColor);
                    break;
                default:
                    AddLine(new Vector3(p, 0f, -span), new Vector3(p, 0f, span), aColor);
                    AddLine(new Vector3(-span, 0f, p), new Vector3(span, 0f, p), bColor);
                    break;
            }
        }
    }

    private static Vector4 AxisColor(GridAxis plane, bool first, Vector4 xAxis, Vector4 yAxis, Vector4 zAxis) =>
        plane switch
        {
            GridAxis.X => first ? yAxis : zAxis,
            GridAxis.Z => first ? xAxis : yAxis,
            _ => first ? zAxis : xAxis
        };

    private void AddQuadPrimitive(Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission)
    {
        var a = new Vector3(-0.5f, -0.5f, 0f);
        var b = new Vector3(0.5f, -0.5f, 0f);
        var c = new Vector3(0.5f, 0.5f, 0f);
        var d = new Vector3(-0.5f, 0.5f, 0f);
        AddQuadPrimitive(a, b, c, d, transform, color, material, emission);
    }

    private void AddCapsulePrimitive(Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission, int slices, int hemiRings)
    {
        float cylinderHalf = 0.5f;
        for (int i = 0; i < slices; i++)
        {
            float u0 = i / (float)slices;
            float u1 = (i + 1) / (float)slices;
            float a0 = u0 * MathF.PI * 2f;
            float a1 = u1 * MathF.PI * 2f;
            var b0 = new Vector3(MathF.Cos(a0) * 0.5f, -cylinderHalf, MathF.Sin(a0) * 0.5f);
            var b1 = new Vector3(MathF.Cos(a1) * 0.5f, -cylinderHalf, MathF.Sin(a1) * 0.5f);
            var t0 = new Vector3(b0.X, cylinderHalf, b0.Z);
            var t1 = new Vector3(b1.X, cylinderHalf, b1.Z);
            var n0 = new Vector3(MathF.Cos(a0), 0f, MathF.Sin(a0)).Normalized();
            var n1 = new Vector3(MathF.Cos(a1), 0f, MathF.Sin(a1)).Normalized();
            AddTrianglePrimitiveSmooth(b0, b1, t1, n0, n1, n1, transform, color, material, emission);
            AddTrianglePrimitiveSmooth(b0, t1, t0, n0, n1, n0, transform, color, material, emission);
        }

        AddCapsuleHemisphere(transform, color, material, emission, slices, hemiRings, cylinderHalf, top: true);
        AddCapsuleHemisphere(transform, color, material, emission, slices, hemiRings, -cylinderHalf, top: false);
    }

    private void AddCapsuleHemisphere(Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission, int slices, int rings, float yOffset, bool top)
    {
        for (int y = 0; y < rings; y++)
        {
            float t0 = y == 0 ? 0.0001f : y / (float)rings;
            float t1 = (y + 1) / (float)rings;
            float phi0 = t0 * MathF.PI * 0.5f;
            float phi1 = t1 * MathF.PI * 0.5f;
            if (!top) { phi0 = -phi0; phi1 = -phi1; }
            for (int x = 0; x < slices; x++)
            {
                float u0 = x / (float)slices;
                float u1 = (x + 1) / (float)slices;
                var p00 = CapsulePoint(u0, phi0, yOffset);
                var p10 = CapsulePoint(u1, phi0, yOffset);
                var p11 = CapsulePoint(u1, phi1, yOffset);
                var p01 = CapsulePoint(u0, phi1, yOffset);
                AddTrianglePrimitiveSmooth(p00, p10, p11, CapsuleNormal(p00, yOffset), CapsuleNormal(p10, yOffset), CapsuleNormal(p11, yOffset), transform, color, material, emission);
                AddTrianglePrimitiveSmooth(p00, p11, p01, CapsuleNormal(p00, yOffset), CapsuleNormal(p11, yOffset), CapsuleNormal(p01, yOffset), transform, color, material, emission);
            }
        }
    }

    private static Vector3 CapsulePoint(float u, float phi, float yOffset)
    {
        float theta = u * MathF.PI * 2f;
        float cp = MathF.Cos(phi);
        return new Vector3(MathF.Cos(theta) * cp * 0.5f, yOffset + MathF.Sin(phi) * 0.5f, MathF.Sin(theta) * cp * 0.5f);
    }

    private static Vector3 CapsuleNormal(Vector3 p, float yOffset)
    {
        var normal = new Vector3(p.X, p.Y - yOffset, p.Z);
        return normal.Length > 0.0001f ? normal.Normalized() : Vector3.UnitY;
    }

    private void AddQuadPrimitive(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission)
    {
        AddTrianglePrimitive(a, b, c, transform, color, material, emission, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
        AddTrianglePrimitive(a, c, d, transform, color, material, emission, new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f));
    }

    private void AddTrianglePrimitive(Vector3 a, Vector3 b, Vector3 c, Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission)
        => AddTrianglePrimitive(a, b, c, transform, color, material, emission, Vector2.Zero, Vector2.UnitX, Vector2.UnitY);

    private void AddTrianglePrimitive(Vector3 a, Vector3 b, Vector3 c, Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission, Vector2 uva, Vector2 uvb, Vector2 uvc)
    {
        var normal = Vector3.Cross(b - a, c - a).Normalized();
        ActiveSolidVertices.Add(new SolidVertex(Vector3.TransformPosition(a, transform), Vector3.TransformNormal(normal, transform).Normalized(), color, uva, material, emission));
        ActiveSolidVertices.Add(new SolidVertex(Vector3.TransformPosition(b, transform), Vector3.TransformNormal(normal, transform).Normalized(), color, uvb, material, emission));
        ActiveSolidVertices.Add(new SolidVertex(Vector3.TransformPosition(c, transform), Vector3.TransformNormal(normal, transform).Normalized(), color, uvc, material, emission));
    }

    private void AddTrianglePrimitiveSmooth(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission)
        => AddTrianglePrimitiveSmooth(a, b, c, na, nb, nc, transform, color, material, emission, Vector2.Zero, Vector2.UnitX, Vector2.UnitY);

    private void AddTrianglePrimitiveSmooth(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission, Vector2 uva, Vector2 uvb, Vector2 uvc)
    {
        var geometric = Vector3.Cross(b - a, c - a);
        var desired = na + nb + nc;
        if (geometric.Length > 0.0001f && desired.Length > 0.0001f && Vector3.Dot(geometric, desired) < 0f)
        {
            (b, c) = (c, b);
            (nb, nc) = (nc, nb);
            (uvb, uvc) = (uvc, uvb);
        }

        ActiveSolidVertices.Add(new SolidVertex(Vector3.TransformPosition(a, transform), Vector3.TransformNormal(na, transform).Normalized(), color, uva, material, emission));
        ActiveSolidVertices.Add(new SolidVertex(Vector3.TransformPosition(b, transform), Vector3.TransformNormal(nb, transform).Normalized(), color, uvb, material, emission));
        ActiveSolidVertices.Add(new SolidVertex(Vector3.TransformPosition(c, transform), Vector3.TransformNormal(nc, transform).Normalized(), color, uvc, material, emission));
    }

    private void DrawPrimitiveWire(GameObject obj, Matrix4 transform, Vector4 lineColor)
    {
        if (obj.Type == 5)
        {
            var a = Vector3.TransformPosition(new Vector3(-0.5f, -0.5f, 0f), transform);
            var b = Vector3.TransformPosition(new Vector3(0.5f, -0.5f, 0f), transform);
            var c = Vector3.TransformPosition(new Vector3(0.5f, 0.5f, 0f), transform);
            var d = Vector3.TransformPosition(new Vector3(-0.5f, 0.5f, 0f), transform);
            AddLine(a, b, lineColor); AddLine(b, c, lineColor); AddLine(c, d, lineColor); AddLine(d, a, lineColor);
            return;
        }

        int slices = 48;
        float yMin = obj.Type is 4 or 6 ? -1f : -0.5f;
        float yMax = obj.Type is 4 or 6 ? 1f : 0.5f;
        for (int i = 0; i < slices; i++)
        {
            float a0 = i / (float)slices * MathF.PI * 2f;
            float a1 = (i + 1) / (float)slices * MathF.PI * 2f;
            AddLine(Vector3.TransformPosition(new Vector3(MathF.Cos(a0) * 0.5f, 0f, MathF.Sin(a0) * 0.5f), transform), Vector3.TransformPosition(new Vector3(MathF.Cos(a1) * 0.5f, 0f, MathF.Sin(a1) * 0.5f), transform), lineColor);
            if (obj.Type is 4 or 6)
            {
                AddLine(Vector3.TransformPosition(new Vector3(MathF.Cos(a0) * 0.5f, yMin, MathF.Sin(a0) * 0.5f), transform), Vector3.TransformPosition(new Vector3(MathF.Cos(a1) * 0.5f, yMin, MathF.Sin(a1) * 0.5f), transform), lineColor);
                AddLine(Vector3.TransformPosition(new Vector3(MathF.Cos(a0) * 0.5f, yMax, MathF.Sin(a0) * 0.5f), transform), Vector3.TransformPosition(new Vector3(MathF.Cos(a1) * 0.5f, yMax, MathF.Sin(a1) * 0.5f), transform), lineColor);
            }
        }
    }

    // Listas activas: según _buildingStatic, apuntan al buffer estático o dinámico
    // ── Render del Terrain (Fase 1/3) ──────────────────────────────
    // Igual que AddMeshTriangles + AddSolidRange, pero el rango se marca con
    // TerrainObject para que el draw loop principal lo omita y se dibuje en
    // un pase aparte con terrainShader (mezcla de capas vía splat map).
    


    // ── Render path para materiales con Shader Graph asignado ──────
    // Cada .shadergraph compila su propio programa GLSL (vertex fijo +
    // fragment generado por ShaderCodeGenerator) y se dibuja en un pase
    // aparte, reutilizando el VAO/VBO ya subido del batch sólido.
    /// <summary>Metadatos de una propiedad expuesta del Shader Graph para el Inspector (estilo Unity).</summary>
    


    internal sealed class ShaderGraphMaterialEntry
    {
        public int Program;
        public DateTime FileTime;
        public ShaderGraphModel? Model;
        public string FragmentSource = string.Empty;
        public List<string> Samplers = new();
        public bool NeedsSceneDepth;
        public int ViewProjLocation = -1;
        public int TimeLocation = -1;
        public int ResolutionLocation = -1;
        public int CameraPosLocation = -1;
        public int LightDirLocation = -1;
        public int LightColorLocation = -1;
        public int LightIntensityLocation = -1;
        public int ColorSpaceLinearLocation = -1;
        public int CameraNearLocation = -1;
        public int CameraFarLocation = -1;
        // Sombras direccionales por cascadas (recepción).
        public int ShadowMapLocation = -1;
        public int ShadowEnabledLocation = -1;
        public int ShadowStrengthLocation = -1;
        public int CascadeCountLocation = -1;
        public int ShadowPcfRadiusLocation = -1;
        public int ShadowBiasScaleLocation = -1;
        public int ShadowCameraPosLocation = -1;
        public int[] CascadeLightMvpLocations = new int[5];
        public int[] CascadeSplitLocations = new int[5];
        public int ModelLocation = -1;
        public int UseModelLocation = -1;
        public int UseSkinningLocation = -1;
        public int[] BoneMatrixLocations = new int[MaxGpuBones];
        public int SceneDepthSamplerIndex = -1;
        public List<ShaderGraphSamplerBinding> SamplerBindings = new();
        public List<ShaderGraphPropertyBinding> PropertyBindings = new();

        /// <summary>Valores por defecto de cada uniform. Para colores HDR (vec3/vec4 con ColorMode=Hdr)
        /// el RGB se guarda SIN multiplicar por la intensidad, y se añade un elemento extra al final
        /// con la intensidad: vec3 HDR -> [r,g,b,intensity] (4), vec4 HDR -> [r,g,b,a,intensity] (5).</summary>
        public Dictionary<string, float[]> PropertyDefaults = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Solo las propiedades del Blackboard, para "Exposed Properties" en el Inspector.</summary>
        public Dictionary<string, ShaderGraphExposedProperty> ExposedProperties = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ruta de textura por defecto (Blackboard) para cada uniform sampler2D.</summary>
        public Dictionary<string, string> SamplerDefaults = new(StringComparer.OrdinalIgnoreCase);
    }

    

    


    private const string ShaderGraphVertexSource = """
#version 330 core
layout(location = 0) in vec3 a_Position;
layout(location = 1) in vec3 a_Normal;
layout(location = 3) in vec2 a_UV;
layout(location = 10) in vec4 a_BoneIndices;
layout(location = 11) in vec4 a_BoneWeights;

uniform mat4 u_ViewProj;
uniform mat4 u_Model;
uniform int u_UseModel;
uniform int u_UseSkinning;
uniform mat4 u_Bones[128];

out vec2 v_UV;
out vec3 v_NormalWS;
out vec3 v_WorldPos;
out vec3 v_ObjectPos;
out vec3 v_TangentWS;
out vec4 v_ScreenPos;

void main()
{
    mat4 model = u_UseModel != 0 ? u_Model : mat4(1.0);
    vec4 world = vec4(a_Position, 1.0) * model;
    vec3 normalWS = (vec4(a_Normal, 0.0) * model).xyz;

    if (u_UseSkinning != 0)
    {
        vec4 skinnedPos = vec4(0.0);
        vec3 skinnedNormal = vec3(0.0);
        float totalWeight = 0.0;
        for (int i = 0; i < 4; i++)
        {
            int boneIndex = int(a_BoneIndices[i] + 0.5);
            float weight = a_BoneWeights[i];
            if (boneIndex >= 0 && boneIndex < 128 && weight > 0.0)
            {
                mat4 bone = u_Bones[boneIndex];
                skinnedPos += (vec4(a_Position, 1.0) * bone) * weight;
                skinnedNormal += (vec4(a_Normal, 0.0) * bone).xyz * weight;
                totalWeight += weight;
            }
        }
        if (totalWeight > 0.0001)
        {
            world = skinnedPos;
            normalWS = skinnedNormal;
        }
    }

    v_WorldPos = world.xyz;
    v_ObjectPos = a_Position;
    v_NormalWS = length(normalWS) > 0.0001 ? normalize(normalWS) : vec3(0.0, 1.0, 0.0);
    vec3 tangentOS = abs(a_Normal.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : normalize(cross(vec3(0.0, 1.0, 0.0), a_Normal));
    v_TangentWS = normalize((vec4(tangentOS, 0.0) * model).xyz);
    v_UV = a_UV;
    gl_Position = world * u_ViewProj;
    v_ScreenPos = gl_Position;
}
""";

    private static bool HasShaderGraphRange(List<SolidRange> ranges)
        => ranges.Any(r => r.ShaderGraphPath != null);

    


    


    


    


    


    


    


    


    


    


    // Pasa al shader del graph la sombra direccional por cascadas (la misma que usa el material estándar),
    // para que los objetos con material de Shader Graph reciban sombras.
    


    


    


    

    


    


    private static bool ShaderGraphModelNeedsSceneDepth(ShaderGraphModel? model)
        => model?.Nodes?.Any(n => n.Kind is NodeKind.SceneDepth or NodeKind.DepthFade) == true;

    


    


    


    


    

    


    /// <summary>Solo las propiedades declaradas en el Blackboard, para mostrar en "Exposed Properties" del Inspector (estilo Unity).</summary>
    


    


    


    


    


    


    


    private static float ShaderGraphParseFloat(string? s, float fallback)
        => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    


    


    


    


    


    


    


    


    


    // ── Vista previa de material en arrastre (estilo Unity): mientras se arrastra un .mat
    // sobre el viewport, el objeto/sub-malla bajo el cursor muestra ese material sin soltarlo. ──
    public string? PreviewMaterialObjectId;
    public int PreviewMaterialSubmeshIndex = -1; // -1 = objeto de un solo material (componente Material)
    public string? PreviewMaterialAssetPath;
    private string? previewMaterialDataPath;
    private DateTime previewMaterialDataFileTime;
    private MaterialAssetData? previewMaterialData;

    private MaterialAssetData? GetPreviewOverrideData(GameObject obj, int submeshIndex)
    {
        if (string.IsNullOrWhiteSpace(PreviewMaterialAssetPath) ||
            PreviewMaterialObjectId != obj.EditorId ||
            PreviewMaterialSubmeshIndex != submeshIndex ||
            !System.IO.File.Exists(PreviewMaterialAssetPath))
            return null;
        return GetPreviewMaterialData(PreviewMaterialAssetPath);
    }

    private MaterialAssetData? GetPreviewMaterialData(string path)
    {
        DateTime fileTime;
        try
        {
            fileTime = System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }

        if (previewMaterialData != null &&
            previewMaterialDataFileTime == fileTime &&
            string.Equals(previewMaterialDataPath, path, StringComparison.OrdinalIgnoreCase))
            return previewMaterialData;

        previewMaterialDataPath = path;
        previewMaterialDataFileTime = fileTime;
        previewMaterialData = MaterialAsset.Load(path);
        return previewMaterialData;
    }

    private Vector4 GetObjectColor(GameObject obj, Vector4 fallback)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return new Vector4(Math.Clamp(over.R, 0f, 1f), Math.Clamp(over.G, 0f, 1f), Math.Clamp(over.B, 0f, 1f), 1f);

        var material = obj.GetComponent<Material>();
        return material == null
            ? fallback
            : new Vector4(material.R, material.G, material.B, 1f);
    }

    private string? GetObjectTexturePath(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return string.IsNullOrWhiteSpace(MaterialAsset.GetAlbedo(over)) ? null : MaterialAsset.GetAlbedo(over);

        var material = obj.GetComponent<Material>();
        return material?.TexturePath;
    }

    


    


    


    private (Vector4 Material, Vector4 Emission) GetObjectSurface(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return (
                new Vector4(Math.Clamp(over.Metallic, 0f, 1f), Math.Clamp(over.Roughness, 0.04f, 1f), 0f, 0f),
                new Vector4(Math.Max(0f, over.EmissionR), Math.Max(0f, over.EmissionG), Math.Max(0f, over.EmissionB), Math.Max(0f, over.EmissionIntensity)));

        var material = obj.GetComponent<Material>();
        if (material == null)
            return (new Vector4(0f, 0.5f, 0f, 0f), Vector4.Zero);

        return (
            new Vector4(Math.Clamp(material.Metallic, 0f, 1f), Math.Clamp(material.Roughness, 0.04f, 1f), 0f, 0f),
            new Vector4(Math.Max(0f, material.EmissionR), Math.Max(0f, material.EmissionG), Math.Max(0f, material.EmissionB), Math.Max(0f, material.EmissionIntensity)));
    }

    private SurfaceMaps GetObjectSurfaceMaps(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return new SurfaceMaps(over.NormalMapPath, over.RoughnessMapPath, over.MetallicMapPath);

        var material = obj.GetComponent<Material>();
        return material == null
            ? new SurfaceMaps(null, null, null)
            : new SurfaceMaps(material.NormalMapPath, material.RoughnessMapPath, material.MetallicMapPath);
    }

    private readonly record struct SubmeshSurface(
        Vector4 Color,
        string? TexturePath,
        SurfaceMaps Maps,
        Vector4 Material,
        Vector4 Emission,
        string? ShaderGraphPath,
        IReadOnlyDictionary<string, float[]>? ShaderGraphProperties,
        IReadOnlyDictionary<string, string>? ShaderGraphTextures);

    // Resuelve el material de una sub-malla: si tiene un .mat asignado en MaterialSlots
    // se usa ese; si no, se usan los datos embebidos del modelo importado (color/textura del FBX/OBJ).
    private static SubmeshSurface GetSubmeshSurface(string? materialAssetPath, MeshSubmesh submesh, Vector4 fallbackColor)
    {
        if (!string.IsNullOrWhiteSpace(materialAssetPath) && System.IO.File.Exists(materialAssetPath))
        {
            var data = MaterialAsset.Load(materialAssetPath);
            string? albedo = MaterialAsset.GetAlbedo(data);
            return new SubmeshSurface(
                new Vector4(Math.Clamp(data.R, 0f, 1f), Math.Clamp(data.G, 0f, 1f), Math.Clamp(data.B, 0f, 1f), 1f),
                string.IsNullOrWhiteSpace(albedo) ? null : albedo,
                new SurfaceMaps(data.NormalMapPath, data.RoughnessMapPath, data.MetallicMapPath),
                new Vector4(Math.Clamp(data.Metallic, 0f, 1f), Math.Clamp(data.Roughness, 0.04f, 1f), 0f, 0f),
                new Vector4(Math.Max(0f, data.EmissionR), Math.Max(0f, data.EmissionG), Math.Max(0f, data.EmissionB), Math.Max(0f, data.EmissionIntensity)),
                string.IsNullOrWhiteSpace(data.ShaderGraphPath) ? null : data.ShaderGraphPath,
                data.ShaderGraphProperties.Count > 0 ? data.ShaderGraphProperties : null,
                data.ShaderGraphTextures.Count > 0 ? data.ShaderGraphTextures : null);
        }

        Vector4 color = string.IsNullOrWhiteSpace(submesh.TexturePath) && submesh.DiffuseR == 0.8f && submesh.DiffuseG == 0.8f && submesh.DiffuseB == 0.8f
            ? fallbackColor
            : new Vector4(submesh.DiffuseR, submesh.DiffuseG, submesh.DiffuseB, 1f);

        return new SubmeshSurface(
            color,
            submesh.TexturePath,
            new SurfaceMaps(null, null, null),
            new Vector4(0f, 0.5f, 0f, 0f),
            Vector4.Zero,
            null, null, null);
    }

    private static string? NormalizeTexturePath(string? path) => NormalizeExistingAssetPath(path);

    internal static string? NormalizeExistingAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            string fullPath = System.IO.Path.GetFullPath(path);
            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private void LogAssetWarning(string message)
    {
        if (assetWarningLog.Add(message))
            GrokoEngine.Debug.LogWarning(message);
    }

    // ── Recolección de luces de escena ───────────────────────────────────────
    // Réplica mínima de la parte ambiente + direccional de ApplySceneLighting,
    // pero apuntando a las uniform locations de terrainShader.
    


    // Sube/actualiza la textura RGBA del splat map del Terrain a la GPU,
    // re-subiendo solo cuando SplatVersion cambia (pintura) o la primera vez.
    


    // Pase de dibujo dedicado para los rangos de Terrain: mezcla hasta 4
    // texturas de capa según el splat map, con iluminación simplificada
    // (ambiente + direccional), siguiendo el patrón de RenderShaderGraphRanges.
    


    // Recolecta luces + ajustes una sola vez por Render (memoizado por _frameCount). Esto evita
    // recorrer el árbol de escena ~7 veces por cada pase sólido (antes: estático+dinámico+skinned).
    // ── God rays: posicion del sol en pantalla (para el pase radial del post-proceso) ──
    public Vector2 SunScreenUv { get; private set; }
    public bool    SunGodRaysActive { get; private set; }
    public float   SunGodRaysStrength { get; private set; }
    public Vector3 SunGodRayColor { get; private set; }

    private void ComputeSunScreenPosition(IReadOnlyList<GameObject> objects, Vector3 eye, Matrix4 mvp, Vector3 camFront)
    {
        SunGodRaysActive = false;
        var sun = FindPrimaryDirectionalLight(objects);
        if (sun == null || !sun.GodRays || sun.Intensity <= 0.0001f)
            return;
        // El sol en el cielo esta en la direccion OPUESTA a la que viaja la luz; debe estar delante.
        Vector3 sunSky = (ToTk(sun.GetNormalizedDirection()).Normalized() * -1f);
        if (Vector3.Dot(sunSky, camFront.Normalized()) <= 0.05f)
            return;
        var clip = new Vector4(eye + sunSky * 100000f, 1f) * mvp;
        if (clip.W <= 0.0001f)
            return;
        SunScreenUv = new Vector2((clip.X / clip.W) * 0.5f + 0.5f, (clip.Y / clip.W) * 0.5f + 0.5f);
        SunGodRaysActive = true;
        SunGodRaysStrength = sun.GodRaysStrength;
        SunGodRayColor = ToTk(sun.GetEffectiveColor());
    }

    private void CollectFrameLighting(IReadOnlyList<GameObject> objects)
    {
        if (_frameLightsCollectedFor == _frameCount)
            return;
        _frameLightsCollectedFor = _frameCount;

        _frameAmbient = FindComponent<AmbientLight>(objects);
        _framePostProcess = FindComponent<PostProcessSettings>(objects);
        _frameDir = FindPrimaryDirectionalLight(objects);

        _framePointLights.Clear();
        CollectComponents(objects, _framePointLights, MaxPointLights);
        _frameSpotLights.Clear();
        CollectComponents(objects, _frameSpotLights, MaxSpotLights);
        _frameAreaLights.Clear();
        CollectComponents(objects, _frameAreaLights, MaxAreaLights);
        _frameRectLights.Clear();
        CollectComponents(objects, _frameRectLights, Math.Max(0, MaxAreaLights - _frameAreaLights.Count));
    }

    private void ApplySceneLighting(IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        CollectFrameLighting(objects);   // recolección 1x por Render; aquí solo se SUBEN los uniforms

        // Ambient
        var ambient = _frameAmbient;
        var ambColor = ambient != null
            ? new Vector3(ambient.R, ambient.G, ambient.B)
            : new Vector3(0.3f, 0.32f, 0.36f);
        float ambIntensity = ambient?.Intensity ?? 0.22f;
        // Indirect Multiplier del sol: su luz de rebote escala el fill ambiente (aprox. de GI).
        if (_frameDir != null) ambIntensity *= _frameDir.IndirectMultiplier;
        GL.Uniform3(uAmbientColor, ambColor);
        GL.Uniform1(uAmbientIntensity, ambIntensity);
        GL.Uniform1(uSkyStrength, ambient?.SkyStrength ?? 0.08f);
        GL.Uniform3(uCameraPos, cameraPosition);

        var pp = _framePostProcess;
        bool ppEnabled = pp?.Enabled ?? false;
        // Modo PRO: espacio de color seleccionable desde Settings → Lighting.
        // El shader de objetos sólo lo usa para decidir si decodifica sRGB->linear
        // a la ENTRADA (albedo/texturas). La exposición, el tonemap ACES y la
        // codificación final a sRGB viven en el post-proceso (un único display
        // transform), por eso uExposure/uGamma/uToneMapping/uBloomStrength ya no
        // se suben aquí: hacerlo además rompía el bloom y duplicaba la gamma.
        GL.Uniform1(uColorSpaceLinear, ColorSpace == ColorSpace.Linear ? 1 : 0);
        GL.Uniform1(uUseIBL, ImageBasedLighting ? 1 : 0);
        // HDRI de entorno en la unidad 7 (libre: 0-6 las usan albedo/sombras/mapas).
        if (_envLoaded)
        {
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.Texture2D, _envTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(uEnvMap, 7);
            GL.Uniform1(uHasEnvMap, 1);
            GL.Uniform1(uEnvMaxLod, _envMaxLod);
        }
        else
        {
            GL.Uniform1(uHasEnvMap, 0);
        }
        GL.Uniform1(uAoStrength, 0f);
        GL.Uniform1(uFogDensity, ppEnabled && pp!.Fog ? pp.FogDensity : 0f);
        GL.Uniform3(uFogColor, ppEnabled ? new Vector3(pp!.FogR, pp.FogG, pp.FogB) : new Vector3(0.48f, 0.58f, 0.68f));
        GL.Uniform1(uVolumetricStrength, ppEnabled ? pp!.VolumetricLightStrength : 0f);
        GL.Uniform1(uDebugView, DebugView);

        // Directional light — dirección calculada desde la rotación del GameObject,
        // igual que la cámara: RotY=0 → +Z, RotX positivo → apunta hacia abajo.
        var dir = _frameDir;
        if (dir != null && dir.gameObject != null)
        {
            GL.Uniform3(uDirDir, ToTk(dir.GetNormalizedDirection()).Normalized());
            GL.Uniform3(uDirColor, ToTk(dir.GetEffectiveColor()));   // Filter * temperatura (Kelvin)
            GL.Uniform1(uDirIntensity, dir.Intensity);
        }
        else
        {
            // Sin DirectionalLight en escena: sin sol de relleno (iluminación limpia, solo ambiente).
            GL.Uniform3(uDirDir, new Vector3(0f, -1f, 0f));
            GL.Uniform3(uDirColor, Vector3.Zero);
            GL.Uniform1(uDirIntensity, 0f);
        }

        // Point lights (ya recolectadas en CollectFrameLighting)
        GL.Uniform1(uPointCount, _framePointLights.Count);
        for (int i = 0; i < _framePointLights.Count; i++)
        {
            var pl = _framePointLights[i];
            var pos = pl.gameObject != null ? ComputeWorldPosition(pl.gameObject) : Vector3.Zero;
            GL.Uniform3(uPointPos[i], pos);
            GL.Uniform3(uPointColor[i], ToTk(pl.GetEffectiveColor()));   // Filter * temperatura
            GL.Uniform1(uPointIntensity[i], pl.Intensity);
            GL.Uniform1(uPointRange[i], pl.Range);
        }

        GL.Uniform1(uSpotCount, _frameSpotLights.Count);
        for (int i = 0; i < _frameSpotLights.Count; i++)
        {
            var sl = _frameSpotLights[i];
            var pos = sl.gameObject != null ? ComputeWorldPosition(sl.gameObject) : Vector3.Zero;
            var spotDirection = ResolveSpotDirection(sl);
            GL.Uniform3(uSpotPos[i], pos);
            GL.Uniform3(uSpotDir[i], spotDirection);
            GL.Uniform3(uSpotColor[i], ToTk(sl.GetEffectiveColor()));   // Filter * temperatura
            GL.Uniform1(uSpotIntensity[i], sl.Intensity);
            GL.Uniform1(uSpotRange[i], sl.Range);
            GL.Uniform1(uSpotAngle[i], MathHelper.DegreesToRadians(sl.Angle));
        }

        int areaCount = Math.Min(MaxAreaLights, _frameAreaLights.Count + _frameRectLights.Count);
        GL.Uniform1(uAreaCount, areaCount);
        int areaIndex = 0;
        foreach (var area in _frameAreaLights)
        {
            if (areaIndex >= MaxAreaLights) break;
            UploadAreaLight(areaIndex++, area.gameObject, area.R, area.G, area.B, area.Intensity, area.Range, area.Width, area.Height);
        }
        foreach (var rect in _frameRectLights)
        {
            if (areaIndex >= MaxAreaLights) break;
            UploadAreaLight(areaIndex++, rect.gameObject, rect.R, rect.G, rect.B, rect.Intensity, rect.Range, rect.Width, rect.Height);
        }
    }

    private void UploadAreaLight(int index, GameObject? obj, float r, float g, float b, float intensity, float range, float width, float height)
    {
        var pos = obj != null ? ComputeWorldPosition(obj) : Vector3.Zero;
        var dir = obj != null ? ForwardFromGameObject(obj) : new Vector3(0f, -1f, 0f);
        GL.Uniform3(uAreaPos[index], pos);
        GL.Uniform3(uAreaDir[index], dir);
        GL.Uniform3(uAreaColor[index], new Vector3(r, g, b));
        GL.Uniform1(uAreaIntensity[index], intensity);
        GL.Uniform1(uAreaRange[index], range);
        GL.Uniform2(uAreaSize[index], new Vector2(width, height));
    }

    


    


    


    


    

    

    


    private static Vector3[] GetCameraFrustumCorners(
        ImGuiEditorApp.EditorCameraState camera,
        Matrix4 view,
        float aspect,
        float nearClip,
        float farClip)
    {
        Matrix4 projection = camera.Orthographic
            ? Matrix4.CreateOrthographic(Math.Max(0.01f, camera.OrthoSize) * 2f * aspect, Math.Max(0.01f, camera.OrthoSize) * 2f, nearClip, farClip)
            : Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(camera.FOV), aspect, nearClip, farClip);
        Matrix4.Invert(view * projection, out var invViewProj);

        var corners = new Vector3[8];
        int idx = 0;
        for (int z = 0; z < 2; z++)
        {
            float cz = z == 0 ? -1f : 1f;
            for (int y = 0; y < 2; y++)
            {
                float cy = y == 0 ? -1f : 1f;
                for (int x = 0; x < 2; x++)
                {
                    float cx = x == 0 ? -1f : 1f;
                    var world = Vector4.TransformRow(new Vector4(cx, cy, cz, 1f), invViewProj);
                    corners[idx++] = new Vector3(world.X, world.Y, world.Z) / MathF.Max(world.W, 0.0001f);
                }
            }
        }

        return corners;
    }

    


    


    


    


    


    private static bool CanDepthInstanceTogether(DynamicMeshDraw a, DynamicMeshDraw b) =>
        a.CastShadows &&
        b.CastShadows &&
        ReferenceEquals(a.Mesh, b.Mesh) &&
        a.Start == b.Start &&
        a.Count == b.Count;

    


    


    


    


    


    


    


    // Mismos criterios que BuildObjectRecursive/DrawDepthGeometry para decidir si un objeto
    // aporta geometría real al render (y por tanto al shadow map): cubo, plano o malla cargada.
    // Luces, cámaras y GameObjects vacíos no dibujan geometría y no deben influir en el
    // encuadre de la cámara de sombra direccional.
    

    private (Vector3 Center, float Radius) EstimateObjectBound(GameObject obj)
    {
        var center = ComputeWorldPosition(obj);
        float radius;
        if (obj.GetComponent<MeshFilter>() is { } mf && !string.IsNullOrWhiteSpace(mf.MeshPath) && GetParsedMesh(mf.MeshPath) is { } mesh)
        {
            float s = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
            var min = new Vector3(mesh.BoundsMin.X * s, mesh.BoundsMin.Y * s, mesh.BoundsMin.Z * s);
            var max = new Vector3(mesh.BoundsMax.X * s, mesh.BoundsMax.Y * s, mesh.BoundsMax.Z * s);
            radius = (max - min).Length * 0.5f * MathF.Max(MathF.Abs(obj.ScaleX), MathF.Max(MathF.Abs(obj.ScaleY), MathF.Abs(obj.ScaleZ)));
        }
        else if (obj.Type == 2)
        {
            radius = MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleZ * obj.ScaleZ) * 0.75f;
        }
        else if (obj.Type is 3 or 4 or 5 or 6)
        {
            radius = MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleY * obj.ScaleY + obj.ScaleZ * obj.ScaleZ) * (obj.Type == 6 ? 0.85f : 0.65f);
        }
        else
        {
            radius = MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleY * obj.ScaleY + obj.ScaleZ * obj.ScaleZ) * 0.75f;
        }
        return (center, MathF.Max(radius, 0.5f));
    }

    


    /// <summary>
    /// Convierte la rotación (RotX, RotY) de un GameObject en su vector "forward" mundo.
    /// Misma convención que la cámara: RotY=0 → +Z, RotX positivo → apunta abajo.
    /// </summary>
    


    


    // Selecciona "la" luz direccional usada tanto para iluminar como para proyectar sombra.
    // Antes se usaba FindComponent<DirectionalLight> (la primera encontrada en el árbol)
    // tanto para los uniforms de iluminación como para decidir si se genera el shadow map:
    // si la escena tenía varias DirectionalLight, bastaba con que la PRIMERA encontrada
    // tuviera Shadows = false para que las sombras de OTRA luz (con Shadows = true)
    // desaparecieran del todo. Igual que con Spot/Point (FindShadowCaster), priorizamos
    // la que realmente proyecta sombra; así la dirección de iluminación y la del shadow
    // map siempre coinciden y añadir una segunda luz no "borra" las sombras existentes.
    


    private static T? FindComponent<T>(IReadOnlyList<GameObject> objects) where T : Component
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;
            var c = obj.GetComponent<T>();
            if (c != null && c.Enabled) return c;   // respeta el checkbox Enabled del componente
            var found = FindComponent<T>(obj.Children);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectComponents<T>(IReadOnlyList<GameObject> objects, List<T> result, int max)
        where T : Component
    {
        foreach (var obj in objects)
        {
            if (result.Count >= max) return;
            if (!obj.IsActive) continue;
            var c = obj.GetComponent<T>();
            if (c != null && c.Enabled) result.Add(c);   // respeta el checkbox Enabled del componente
            CollectComponents(obj.Children, result, max);
        }
    }

    private static T? FindShadowCaster<T>(IReadOnlyList<GameObject> objects, Func<T, bool> predicate)
        where T : Component
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;
            if (obj.GetComponent<T>() is { Enabled: true } component && predicate(component))
                return component;
            var found = FindShadowCaster(obj.Children, predicate);
            if (found != null) return found;
        }
        return null;
    }

    private static Vector3 ToTk(MiMotor.Mathematics.Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private readonly List<SolidVertex> _bboxScratch = new(36);
    private SolidVertex[] _bboxUploadBuffer = Array.Empty<SolidVertex>();

    /// <summary>
    /// Dibuja la AABB del mesh usando el VAO/VBO dedicado (_occVao/_occVbo),
    /// completamente independiente del solidVertexBuffer principal.
    /// </summary>
    


    /// <summary>Direct quad push to an explicit list (bypasses the ActiveSolidVertices indirection).</summary>
    


    /// <summary>
    /// Remove occlusion query state for objects that no longer exist in the scene.
    /// Call this after the object list changes (scene load, object deletion).
    /// </summary>
    


    


    public void Dispose()
    {
        foreach (int texture in textureCache.Values)
            if (texture != 0)
                GL.DeleteTexture(texture);
        textureCache.Clear();
        textureFileTimeCache.Clear();
        parsedMeshCache.Clear();
        ClearGpuMeshCache();
        ClearSkinnedGpuMeshCache();
        assetWarningLog.Clear();
        GL.DeleteBuffer(vertexBuffer);
        GL.DeleteBuffer(solidVertexBuffer);
        GL.DeleteBuffer(_staticVertexBuffer);
        GL.DeleteBuffer(_particleVbo);
        GL.DeleteBuffer(_trailVbo);
        if (dynamicInstanceBuffer != 0) GL.DeleteBuffer(dynamicInstanceBuffer);
        GL.DeleteVertexArray(vertexArray);
        GL.DeleteVertexArray(solidVertexArray);
        GL.DeleteVertexArray(_staticVertexArray);
        GL.DeleteVertexArray(_particleVao);
        GL.DeleteVertexArray(_trailVao);
        if (_depthTex != 0) GL.DeleteTexture(_depthTex);
        if (_depthFbo != 0) GL.DeleteFramebuffer(_depthFbo);
        if (_occVbo != 0) GL.DeleteBuffer(_occVbo);
        if (_occVao != 0) GL.DeleteVertexArray(_occVao);
        if (_shadowArrayTex != 0) GL.DeleteTexture(_shadowArrayTex);
        if (_shadowFbo != 0) GL.DeleteFramebuffer(_shadowFbo);
        if (_spotShadowTex != 0) GL.DeleteTexture(_spotShadowTex);
        if (_spotShadowFbo != 0) GL.DeleteFramebuffer(_spotShadowFbo);
        if (_pointShadowCube != 0) GL.DeleteTexture(_pointShadowCube);
        if (_pointShadowFbo != 0) GL.DeleteFramebuffer(_pointShadowFbo);
        if (_shaderGraphWhiteTex != 0) GL.DeleteTexture(_shaderGraphWhiteTex);
        foreach (var entry in _shaderGraphCache.Values)
            if (entry.Program != 0)
                GL.DeleteProgram(entry.Program);
        _shaderGraphCache.Clear();
        GL.DeleteProgram(shader);
        GL.DeleteProgram(solidShader);
        if (_shaderGraphDepthPrepassShader != 0) GL.DeleteProgram(_shaderGraphDepthPrepassShader);
        if (_shadowShader != 0) GL.DeleteProgram(_shadowShader);
        if (_pointShadowShader != 0) GL.DeleteProgram(_pointShadowShader);
        GL.DeleteProgram(_particleShader);
        if (_skyboxShader != 0) GL.DeleteProgram(_skyboxShader);
        if (_skyboxVao != 0) GL.DeleteVertexArray(_skyboxVao);
        if (_envTexture != 0) GL.DeleteTexture(_envTexture);

        // Delete any in-flight occlusion query objects
        foreach (var qid in _occlusionQueries.Values)
            GL.DeleteQuery(qid);
        _occlusionQueries.Clear();
        _occlusionVisible.Clear();
        _occlusionQueryFrame.Clear();
    }

    private readonly record struct LineVertex(Vector3 Position, Vector4 Color);
    private readonly record struct SolidVertex
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector4 Color;
        public readonly Vector2 Uv;
        public readonly Vector4 Material;
        public readonly Vector4 Emission;

        public SolidVertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv)
            : this(position, normal, color, uv, new Vector4(0f, 0.5f, 0f, 0f), Vector4.Zero)
        {
        }

        public SolidVertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv, Vector4 material, Vector4 emission)
        {
            Position = position;
            Normal = normal;
            Color = color;
            Uv = uv;
            Material = material;
            Emission = emission;
        }
    }
    private readonly record struct SolidRange(int Start, int Count, string? TexturePath, string? NormalMapPath, string? RoughnessMapPath, string? MetallicMapPath, string? ShaderGraphPath = null, IReadOnlyDictionary<string, float[]>? ShaderGraphProperties = null, IReadOnlyDictionary<string, string>? ShaderGraphTextures = null, GameObject? TerrainObject = null);
    private readonly record struct DynamicMeshDraw(CachedGpuMesh Mesh, Matrix4 World, int Start, int Count, string? TexturePath, string? NormalMapPath, string? RoughnessMapPath, string? MetallicMapPath, Vector4 Color, Vector4 Material, Vector4 Emission, bool CastShadows = true, bool ReceiveShadows = true);
    private readonly record struct SkinnedMeshDraw(CachedSkinnedGpuMesh Mesh, Matrix4 World, System.Numerics.Matrix4x4[] Skin, int Start, int Count, string? TexturePath, string? NormalMapPath, string? RoughnessMapPath, string? MetallicMapPath, Vector4 Color, Vector4 Material, Vector4 Emission);
    
    private readonly record struct SurfaceMaps(string? NormalMapPath, string? RoughnessMapPath, string? MetallicMapPath);
    private readonly record struct MeshVertData(Vector3 Position, Vector3 Normal, Vector2 Uv);
    private readonly struct SkinnedVertex
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector4 Color;
        public readonly Vector2 Uv;
        public readonly Vector4 Material;
        public readonly Vector4 Emission;
        public readonly Vector4 BoneIndices;
        public readonly Vector4 BoneWeights;

        public SkinnedVertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv, Vector4 material, Vector4 emission, Vector4 boneIndices, Vector4 boneWeights)
        {
            Position = position;
            Normal = normal;
            Color = color;
            Uv = uv;
            Material = material;
            Emission = emission;
            BoneIndices = boneIndices;
            BoneWeights = boneWeights;
        }
    }
    private sealed class CachedSkinnedRange
    {
        public CachedSkinnedRange(SolidVertex[] vertices) => Vertices = vertices;
        public SolidVertex[] Vertices { get; }
    }
    private readonly record struct CachedParsedMesh(ParsedMesh Mesh, DateTime FileTime);
    private sealed class CachedGpuMesh
    {
        public CachedGpuMesh(int vao, int vbo, int count, DateTime fileTime)
        {
            Vao = vao;
            Vbo = vbo;
            Count = count;
            FileTime = fileTime;
        }

        public int Vao { get; }
        public int Vbo { get; }
        public int Count { get; }
        public DateTime FileTime { get; }
    }

    private sealed class CachedSkinnedGpuMesh
    {
        public CachedSkinnedGpuMesh(int vao, int vbo, int count, DateTime fileTime)
        {
            Vao = vao;
            Vbo = vbo;
            Count = count;
            FileTime = fileTime;
        }

        public int Vao { get; }
        public int Vbo { get; }
        public int Count { get; }
        public DateTime FileTime { get; }
    }

    // center(3) + offset(2) + color(4) + rotation(1) + uv(2) + absolute(1) = 13 floats = 52 bytes
    

    

    private readonly struct CachedMeshVerts(MeshVertData[] verts, int count, DateTime fileTime)
    {
        public readonly MeshVertData[] Verts = verts;
        public readonly int Count = count;
        public readonly DateTime FileTime = fileTime;
    }

    /// <summary>Frustum de 6 planos extraídos de la matriz VP (Gribb-Hartmann).</summary>
    private readonly struct Frustum
    {
        private readonly Vector4 _left, _right, _bottom, _top, _near, _far;

        public Frustum(Matrix4 vp)
        {
            var r0 = vp.Row0; var r1 = vp.Row1; var r2 = vp.Row2; var r3 = vp.Row3;
            _left = Norm(r3 + r0);
            _right = Norm(r3 - r0);
            _bottom = Norm(r3 + r1);
            _top = Norm(r3 - r1);
            _near = Norm(r3 + r2);
            _far = Norm(r3 - r2);
        }

        /// <summary>True si la esfera es potencialmente visible (no completamente fuera de ningún plano).</summary>
        public bool ContainsSphere(Vector3 c, float r)
        {
            var p = new Vector4(c.X, c.Y, c.Z, 1f);
            return Vector4.Dot(_left, p) >= -r &&
                   Vector4.Dot(_right, p) >= -r &&
                   Vector4.Dot(_bottom, p) >= -r &&
                   Vector4.Dot(_top, p) >= -r &&
                   Vector4.Dot(_near, p) >= -r &&
                   Vector4.Dot(_far, p) >= -r;
        }

        private static Vector4 Norm(Vector4 p)
        {
            float len = MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            return len > 1e-6f ? p / len : p;
        }
    }
}
