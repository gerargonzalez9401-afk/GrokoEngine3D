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
        public float EmissionR { get; set; } = 0f;
        public float EmissionG { get; set; } = 0f;
        public float EmissionB { get; set; } = 0f;
        public float EmissionIntensity { get; set; } = 0f;
        public bool IsInstance { get; set; } = false;
    }

    // =====================================================
    // CAMERA
    // =====================================================
    public class Camera : Component
    {
        private float _fov = 60f;
        private float _nearClip = 0.1f;
        private float _farClip = 1000f;
        private int _antiAliasingSamples = 4;

        public float FOV
        {
            get => _fov;
            set => _fov = Math.Clamp(value, 5f, 170f);
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

        public bool AntiAliasing { get; set; } = true;

        public int AntiAliasingSamples
        {
            get => _antiAliasingSamples;
            set => _antiAliasingSamples = value <= 1 ? 1 : value <= 2 ? 2 : value <= 4 ? 4 : 8;
        }

        public bool FrustumCulling { get; set; } = true;
        public bool OcclusionCulling { get; set; } = false;
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
        public const int Everything = ~0;

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
    }
}
