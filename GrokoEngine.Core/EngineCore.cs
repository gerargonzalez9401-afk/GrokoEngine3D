using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MiMotor.Mathematics;
using Quaternion = MiMotor.Mathematics.Quaternion;
using Transform = MiMotor.Mathematics.Transform;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine
{
    // =====================================================
    // DEBUG
    // =====================================================
    public static class Debug
    {
        public static event Action<string, string>? OnLogMessage;
        public static void Log(object? message) => Write(message, "Info");
        public static void LogWarning(object? message) => Write(message, "Warning");
        public static void LogError(object? message) => Write(message, "Error");

        private static void Write(object? message, string severity)
        {
            OnLogMessage?.Invoke(message?.ToString() ?? "null", severity);
        }
    }

    // =====================================================
    // COMPONENT BASE
    // =====================================================
    public abstract class Component
    {
        public GameObject gameObject { get; set; } = null!;

        public Transform transform =>
            gameObject.transform;

        private bool _enabled = true;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;

                if (gameObject != null)
                {
                    gameObject.SetComponentEnabled(this, value);
                    return;
                }

                _enabled = value;
            }
        }

        public bool HasAwaken { get; internal set; }
        public bool HasStarted { get; internal set; }

        public virtual void Awake() { }
        public virtual void OnEnable() { }
        public virtual void Start() { }
        public virtual void Update(double dt) { }
        public virtual void OnDisable() { }
        public virtual void OnDestroy() { }

        internal void SetEnabledInternal(bool value)
        {
            _enabled = value;
        }

        internal void InternalAwake()
        {
            if (HasAwaken)
                return;

            HasAwaken = true;
            Awake();
        }

        internal void InternalStart()
        {
            if (HasStarted)
                return;

            HasStarted = true;
            Start();
        }

        internal void InternalEnable()
        {
            if (_enabled)
                OnEnable();
        }

        internal void InternalDisable()
        {
            OnDisable();
        }
    }

    // =====================================================
    // MONOBEHAVIOUR
    // =====================================================
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeFieldAttribute : Attribute
    {
    }

    public abstract class MonoBehaviour : Component
    {
        public GameObject Instantiate(GameObject prefab) =>
            RuntimeScene.Instantiate(prefab, prefab.Position, Quaternion.identity);

        public GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation) =>
            RuntimeScene.Instantiate(prefab, position, rotation);

        public GameObject Instantiate(string prefabPath, Vector3 position, Vector3 rotation) =>
            RuntimeScene.Instantiate(prefabPath, position, rotation);

        public void SetParent(GameObject obj, GameObject? parent, bool worldPositionStays = true) =>
            RuntimeScene.SetParent(obj, parent, worldPositionStays);

        public void Unparent(GameObject obj, bool worldPositionStays = true) =>
            RuntimeScene.Unparent(obj, worldPositionStays);

        public void Destroy(GameObject obj, float delay = 0f) =>
            RuntimeScene.Destroy(obj, delay);

        // Estilo Unity: DontDestroyOnLoad(gameObject). Solo tiene efecto sobre objetos RAIZ
        // (Parent == null); SceneManager.ProcessPendingLoad lo respeta al cambiar de escena.
        public void DontDestroyOnLoad(GameObject obj)
        {
            if (obj.Parent != null)
            {
                Debug.LogWarning($"DontDestroyOnLoad ignorado: '{obj.Name}' no es un objeto raiz.");
                return;
            }
            obj.DontDestroyOnLoad = true;
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, bool includeTriggers = false) =>
            Physics.Raycast(origin, direction, maxDistance, out hit, includeTriggers);

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, int layerMask, bool includeTriggers = false) =>
            Physics.Raycast(origin, direction, maxDistance, out hit, layerMask, includeTriggers);

        public virtual void OnCollisionEnter(Collision collision) { }
        public virtual void OnCollisionStay(Collision collision) { }
        public virtual void OnCollisionExit(Collision collision) { }
        public virtual void OnTriggerEnter(Collider other) { }
        public virtual void OnTriggerStay(Collider other) { }
        public virtual void OnTriggerExit(Collider other) { }
    }

    // =====================================================
    // SCRIPTABLE OBJECT
    // =====================================================
    // Datos reutilizables independientes de un GameObject, persistidos como assets ".asset".
    public abstract class ScriptableObject
    {
        public string Name { get; set; } = "";
        public string AssetPath { get; set; } = "";

        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();

        public static ScriptableObject? CreateInstance(Type type)
        {
            if (type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type))
                return null;
            return (ScriptableObject?)Activator.CreateInstance(type);
        }
    }

    // =====================================================
    // MATERIAL
    // =====================================================
    public enum MaterialSurfaceType
    {
        Opaque,
        Cutout,
        Transparent
    }

    public class Material : Component
    {
        public float R { get; set; } = 1f;
        public float G { get; set; } = 1f;
        public float B { get; set; } = 1f;
        public string AssetPath { get; set; } = "";
        public string TexturePath { get; set; } = "";
        public string NormalMapPath { get; set; } = "";
        public string RoughnessMapPath { get; set; } = "";
        public string MetallicMapPath { get; set; } = "";
        public string ShaderGraphPath { get; set; } = "";
        public Dictionary<string, float[]> ShaderGraphProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ShaderGraphTextures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public float Roughness { get; set; } = 0.5f;
        public float Metallic { get; set; } = 0f;
        public MaterialSurfaceType SurfaceType { get; set; } = MaterialSurfaceType.Opaque;
        public float Alpha { get; set; } = 1f;
        public float AlphaClipThreshold { get; set; } = 0.5f;
        public float RefractionStrength { get; set; } = 0f;
        public float DistortionStrength { get; set; } = 0f;
        public bool SpecularHighlights { get; set; } = true;
        public bool EnvironmentReflections { get; set; } = true;
        // Iluminación de transmisión artística para piel, tela fina y personajes.
        // 0 mantiene exactamente el PBR estándar; valores bajos dejan pasar luz de
        // contra por los bordes sin necesitar luces auxiliares en la escena.
        public float SubsurfaceScattering { get; set; } = 0f;
        public float EmissionR { get; set; } = 0f;
        public float EmissionG { get; set; } = 0f;
        public float EmissionB { get; set; } = 0f;
        public float EmissionIntensity { get; set; } = 0f;
        public bool IsInstance { get; set; } = false;
    }

    // =====================================================
    // CAMERA
    // =====================================================
    public enum CameraClearFlags { Skybox, SolidColor }
    public enum CameraProjection { Perspective, Orthographic }
    public enum CameraFieldOfViewAxis { Vertical, Horizontal }
    public enum CameraRenderingPath { UseProjectSettings, Forward, ForwardPlus, Deferred }
    public enum CameraAntiAliasingMode { None, FXAA, SMAA, TAA }
    public enum CameraTextureMode { Pipeline, On, Off }

    public class Camera : Component
    {
        private float _fov = 60f;
        private float _nearClip = 0.1f;
        private float _farClip = 1000f;
        private float _orthographicSize = 5f;
        private int _antiAliasingSamples = 4;
        private float _focalLengthMm = 50f;
        private float _sensorHeightMm = 24f;

        public float FOV
        {
            get => _fov;
            set => _fov = Math.Clamp(value, 5f, 170f);
        }

        // Camara fisica estilo Unity: cuando PhysicalCamera esta activo, el FOV vertical
        // efectivo se deriva de estos dos valores en vez de leerse directo de FOV.
        public float FocalLengthMm
        {
            get => _focalLengthMm;
            set => _focalLengthMm = Math.Clamp(value, 1f, 1000f);
        }

        public float SensorHeightMm
        {
            get => _sensorHeightMm;
            set => _sensorHeightMm = Math.Clamp(value, 1f, 200f);
        }

        // FOV vertical efectivo en grados, ya resuelto por PhysicalCamera (focal/sensor)
        // o por FieldOfViewAxis (convierte horizontal->vertical con el aspect dado).
        public float GetEffectiveVerticalFov(float aspect)
        {
            if (PhysicalCamera)
            {
                float sensor = Math.Max(0.1f, SensorHeightMm);
                float focal = Math.Max(0.1f, FocalLengthMm);
                float fovRad = 2f * MathF.Atan(sensor / (2f * focal));
                return Math.Clamp(fovRad * (180f / MathF.PI), 5f, 170f);
            }

            if (FieldOfViewAxis == CameraFieldOfViewAxis.Horizontal)
            {
                float safeAspect = Math.Max(0.0001f, aspect);
                float horizRad = FOV * (MathF.PI / 180f);
                float vertRad = 2f * MathF.Atan(MathF.Tan(horizRad / 2f) / safeAspect);
                return Math.Clamp(vertRad * (180f / MathF.PI), 5f, 170f);
            }

            return FOV;
        }

        public float NearClip
        {
            get => _nearClip;
            set
            {
                float v = Math.Max(0.001f, value);
                // NearClip siempre menor que FarClip
                _nearClip = Math.Min(v, _farClip - 0.001f);
            }
        }

        public float FarClip
        {
            get => _farClip;
            set
            {
                float v = Math.Max(0.01f, value);
                // FarClip siempre mayor que NearClip
                _farClip = Math.Max(v, _nearClip + 0.001f);
            }
        }

        public CameraClearFlags ClearFlags = CameraClearFlags.Skybox;
        public CameraProjection Projection { get; set; } = CameraProjection.Perspective;
        public CameraFieldOfViewAxis FieldOfViewAxis { get; set; } = CameraFieldOfViewAxis.Vertical;
        public bool PhysicalCamera { get; set; } = false;
        public float OrthographicSize
        {
            get => _orthographicSize;
            set => _orthographicSize = Math.Clamp(value, 0.001f, 100000f);
        }
        public float BackgroundR = 0.075f;
        public float BackgroundG = 0.095f;
        public float BackgroundB = 0.120f;
        public float BackgroundA = 1f;
        public bool PostProcessing { get; set; } = true;
        public CameraRenderingPath RenderingPath { get; set; } = CameraRenderingPath.UseProjectSettings;
        public bool AntiAliasing { get; set; } = true;
        public CameraAntiAliasingMode AntiAliasingMode { get; set; } = CameraAntiAliasingMode.FXAA;

        public int AntiAliasingSamples
        {
            get => _antiAliasingSamples;
            set => _antiAliasingSamples = value <= 1 ? 1 : value <= 2 ? 2 : value <= 4 ? 4 : 8;
        }

        public bool FrustumCulling { get; set; } = true;
        public bool OcclusionCulling { get; set; } = false;
        public bool StopNaNs { get; set; } = false;
        public bool Dithering { get; set; } = false;
        public bool RenderShadows { get; set; } = true;
        public int Priority { get; set; } = -1;
        public CameraTextureMode OpaqueTexture { get; set; } = CameraTextureMode.Pipeline;
        public CameraTextureMode DepthTexture { get; set; } = CameraTextureMode.Pipeline;
        public string CullingMask { get; set; } = "Everything";
        public CameraClearFlags BackgroundType
        {
            get => ClearFlags;
            set => ClearFlags = value;
        }
    }

    // =====================================================
    // MESH FILTER
    // =====================================================
    public class MeshFilter : Component
    {
        public string MeshPath = "";
        public float ImportScale = 1f;

        // Ruta a un .mat por cada sub-malla del modelo importado (mismo orden que ParsedMesh.Submeshes).
        // Vacío para modelos de un solo material (se usa el componente Material del objeto).
        public List<string> MaterialSlots = new();

        // Cuando un FBX se importa "con hijos" (Preserve Hierarchy), cada hijo dibuja UNA sola parte
        // del modelo. -1 = dibujar todas las submallas (comportamiento normal); >=0 = solo esa submalla.
        public int SubmeshIndex = -1;
    }

    // Componente de renderizado de malla, estilo Unity (separado del MeshFilter, que solo lleva la malla).
    // Los materiales reales se asignan vía MeshFilter.MaterialSlots / componente Material (la lista
    // "Materials" del inspector edita eso). El resto de campos replican el inspector de Unity; las sombras
    // del motor son globales por ahora, así que los ajustes de Lighting/Ray Tracing son de presentación.
    public class MeshRenderer : Component
    {
        // ── Lighting ──
        public int CastShadows = 1;                 // 0 Off, 1 On, 2 Two Sided, 3 Shadows Only
        public bool StaticShadowCaster = false;
        public bool ContributeGlobalIllumination = false;
        public int ReceiveGlobalIllumination = 0;   // 0 Light Probes, 1 Lightmaps
        // Ruta del lightmap horneado asignado por el baker. Se conserva con la escena para que
        // el renderer pueda usar la iluminación indirecta también después de reiniciar el editor.
        public string LightmapPath = "";
        public string ShadowmaskPath = "";
        public float LightmapIntensity = 1f;
        // Equivalente a Unity Renderer.lightmapScaleOffset. El renderer usa UV2 generadas
        // para muestrear la región correcta dentro de un atlas compartido.
        public float LightmapScaleX = 1f;
        public float LightmapScaleY = 1f;
        public float LightmapOffsetX = 0f;
        public float LightmapOffsetY = 0f;
        public bool UseGeneratedLightmapUv = false;
        public bool ReceiveShadows = true;

        // ── Probes ──
        public int LightProbes = 0;                 // 0 Blend Probes, 1 Use Proxy Volume, 2 Off
        public int ReflectionProbes = 0;            // 0 Blend Probes, 1 Blend & Skybox, 2 Simple, 3 Off

        // ── Ray Tracing ──
        public int RayTracingMode = 1;              // 0 Off, 1 Dynamic Transform, 2 Dynamic Geometry, 3 Static
        public bool ProceduralGeometry = false;
        public int AccelerationStructure = 0;       // 0 Prefer Fast Trace, 1 Prefer Fast Build

        // ── Additional Settings ──
        public int MotionVectors = 1;               // 0 Camera Motion Only, 1 Per Object Motion, 2 Force No Motion
        public bool DynamicOcclusion = true;
        public string RenderingLayerMask = "Default";
        public int Priority = 0;
    }

    public enum ReflectionProbeMode
    {
        Baked = 0,
        Realtime = 1,
        Custom = 2
    }

    public enum ReflectionProbeRefreshMode
    {
        OnAwake = 0,
        EveryFrame = 1,
        ViaScripting = 2
    }

    public enum ReflectionProbeTimeSlicing
    {
        NoTimeSlicing = 0,
        IndividualFaces = 1,
        AllFacesAtOnce = 2
    }

    // Reflection Probe estilo Unity. Por ahora define el volumen, el modo y los assets
    // de cubemap que usara el renderer cuando conectemos la captura/aplicacion real.
    public class ReflectionProbe : Component
    {
        public ReflectionProbeMode Mode = ReflectionProbeMode.Baked;
        public string BakedCubemapPath = "";
        public string CustomCubemapPath = "";

        public float Intensity = 1f;
        public float BlendDistance = 1f;
        public float SizeX = 10f;
        public float SizeY = 10f;
        public float SizeZ = 10f;
        public float CenterX = 0f;
        public float CenterY = 0f;
        public float CenterZ = 0f;

        public int Resolution = 128;
        public float NearClip = 0.1f;
        public float FarClip = 100f;
        public int Importance = 1;
        public bool BoxProjection = true;
        public bool HDR = true;

        public ReflectionProbeRefreshMode RefreshMode = ReflectionProbeRefreshMode.OnAwake;
        public ReflectionProbeTimeSlicing TimeSlicing = ReflectionProbeTimeSlicing.IndividualFaces;

        public string EffectiveCubemapPath =>
            Mode == ReflectionProbeMode.Custom ? CustomCubemapPath : BakedCubemapPath;

        public bool NeedsBake =>
            Mode == ReflectionProbeMode.Baked && string.IsNullOrWhiteSpace(BakedCubemapPath);
    }

    // =====================================================
    // LOD GROUP
    // =====================================================
    // Como el LOD Group de Unity: controla a qué "tamaño relativo en pantalla" (radio del objeto
    // / distancia a la cámara, × Bias) cambia de nivel la malla. Aplica al objeto y a TODOS sus
    // descendientes (el LodGroup más cercano en la jerarquía gana). Los niveles de geometría son
    // las submallas "X_LOD0/X_LOD1/..." que el modelo trae de fábrica; si el modelo no trae LODs,
    // solo aplica el ocultado por distancia (CulledBelow).
    public class LodGroup : Component
    {
        public float Lod0 = 0.12f;         // por encima → nivel 0 (máximo detalle)
        public float Lod1 = 0.055f;        // entre Lod0 y Lod1 → nivel 1
        public float Lod2 = 0.025f;        // entre Lod1 y Lod2 → nivel 2; por debajo → último nivel
        public float CulledBelow = 0.005f; // más pequeño que esto → no se dibuja (0 = nunca ocultar)
        public float Bias = 1f;            // >1 mantiene el detalle a más distancia
    }

    // =====================================================
    // BONE ATTACHMENT / SOCKET
    // =====================================================
    // Hace que un objeto rígido siga un hueso de un Animator sin tener que parentarlo
    // dentro del esqueleto. Ideal para armas, props, cascos o accesorios: se renderizan
    // como meshes normales y evitan ensuciar el mapa de huesos/skinning del personaje.
    public class BoneAttachment : Component
    {
        public string TargetObjectId = "";
        public string TargetObjectName = "";
        public string BoneName = "";

        public float PositionOffsetX = 0f;
        public float PositionOffsetY = 0f;
        public float PositionOffsetZ = 0f;

        public float RotationOffsetX = 0f;
        public float RotationOffsetY = 0f;
        public float RotationOffsetZ = 0f;

        public float ScaleOffsetX = 1f;
        public float ScaleOffsetY = 1f;
        public float ScaleOffsetZ = 1f;

        public bool FollowPosition = true;
        public bool FollowRotation = true;
        public bool FollowScale = false;
    }

    // =====================================================
    // SKELETAL ANIMATOR (reproduce animaciones de huesos de un FBX)
    // =====================================================

    // =====================================================
    // BOUNDS
    // =====================================================
    public class Bounds
    {
        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }

        public Bounds(Vector3 min, Vector3 max) { Min = min; Max = max; }

        public bool Intersects(Bounds other) =>
            Min.X <= other.Max.X && Max.X >= other.Min.X &&
            Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
            Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    }

    public readonly struct Collision
    {
        public Collision(Collider collider, Collider otherCollider, Vector3 point, Vector3 normal)
        {
            Collider = collider;
            OtherCollider = otherCollider;
            Point = point;
            Normal = normal;
        }

        public Collider Collider { get; }
        public Collider OtherCollider { get; }
        /// <summary>El GameObject que RECIBE esta colisión (el propio).</summary>
        public GameObject GameObject => Collider.gameObject;
        /// <summary>El GameObject con el que colisiona (el rival).</summary>
        public GameObject OtherGameObject => OtherCollider.gameObject;
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
    }

    public readonly struct PhysicsRaycastHit
    {
        public PhysicsRaycastHit(Collider collider, Vector3 point, Vector3 normal, float distance)
        {
            Collider = collider;
            Point = point;
            Normal = normal;
            Distance = distance;
        }

        public Collider Collider { get; }
        public GameObject GameObject => Collider.gameObject;
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public float Distance { get; }
    }

    public static class Physics
    {
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, bool includeTriggers = false) =>
            RuntimeScene.Raycast(origin, direction, maxDistance, out hit, includeTriggers);

        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out PhysicsRaycastHit hit, int layerMask, bool includeTriggers = false) =>
            RuntimeScene.Raycast(origin, direction, maxDistance, out hit, layerMask, includeTriggers);

        public static List<Collider> OverlapBox(Vector3 center, Vector3 size, bool includeTriggers = true) =>
            RuntimeScene.OverlapBox(center, size, includeTriggers);
    }

    public static class LayerMask
    {
        public const int Default = 0;
        public const int TransparentFX = 1;
        public const int IgnoreRaycast = 2;
        public const int Water = 4;
        public const int UI = 5;
        public const int Nothing = 0;
        public const int Everything = ~0;

        public static readonly string[] BuiltinLayerNames =
        {
            "Default",
            "TransparentFX",
            "Ignore Raycast",
            "Water",
            "UI",
            "Player",
            "Enemy",
            "Ground",
            "Pickup",
            "Interactable"
        };

        public static readonly string[] CullingMaskOptions =
        {
            "Everything",
            "Nothing",
            "Default",
            "TransparentFX",
            "Ignore Raycast",
            "Water",
            "UI",
            "Player",
            "Enemy",
            "Ground",
            "Pickup",
            "Interactable"
        };

        private static readonly Dictionary<string, int> builtinLayers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = Default,
            ["TransparentFX"] = TransparentFX,
            ["Ignore Raycast"] = IgnoreRaycast,
            ["IgnoreRaycast"] = IgnoreRaycast,
            ["Water"] = Water,
            ["UI"] = UI,
            ["Player"] = 8,
            ["Enemy"] = 9,
            ["Ground"] = 10,
            ["Pickup"] = 11,
            ["Interactable"] = 12
        };

        public static int NameToLayer(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Default;

            return builtinLayers.TryGetValue(name.Trim(), out int layer) ? layer : Default;
        }

        public static string LayerToName(int layer) =>
            layer switch
            {
                Default => "Default",
                TransparentFX => "TransparentFX",
                IgnoreRaycast => "Ignore Raycast",
                Water => "Water",
                UI => "UI",
                8 => "Player",
                9 => "Enemy",
                10 => "Ground",
                11 => "Pickup",
                12 => "Interactable",
                _ => "Layer " + Math.Clamp(layer, 0, 31).ToString()
            };

        public static int GetMask(params string[] layerNames)
        {
            if (layerNames == null || layerNames.Length == 0)
                return 0;

            int mask = 0;
            foreach (string name in layerNames)
            {
                int layer = NameToLayer(name);
                mask |= 1 << Math.Clamp(layer, 0, 31);
            }

            return mask;
        }

        public static int ToMask(int layer) => 1 << Math.Clamp(layer, 0, 31);
        public static bool Contains(int layerMask, int layer) => (layerMask & ToMask(layer)) != 0;

        public static int ParseMask(string? mask)
        {
            if (string.IsNullOrWhiteSpace(mask))
                return Everything;

            string trimmed = mask.Trim();
            if (string.Equals(trimmed, "Everything", StringComparison.OrdinalIgnoreCase))
                return Everything;
            if (string.Equals(trimmed, "Nothing", StringComparison.OrdinalIgnoreCase))
                return Nothing;

            char[] separators = { ',', '|', ';' };
            string[] parts = trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
                return GetMask(parts);

            return GetMask(trimmed);
        }
    }
}
