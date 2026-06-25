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
    [Flags]
    public enum CollisionFlags
    {
        None = 0,
        Sides = 1,
        Above = 2,
        Below = 4
    }

    // =====================================================
    // GAMEOBJECT
    // =====================================================
    public class GameObject : INotifyPropertyChanged
    {
        public string EditorId { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";

        private bool _isActive = true;

        public bool IsActive
        {
            get => _isActive;
            set => SetActive(value);
        }

        public bool IsCamera { get; set; }
        public bool IsStatic { get; set; } = false;
        public int Layer { get; set; } = LayerMask.Default;
        public int Type { get; set; }

        private GameObject? _parent;

        public GameObject? Parent
        {
            get => _parent;
            set
            {
                if (_parent == value)
                    return;

                if (value != null)
                {
                    GameObject? check = value;

                    while (check != null)
                    {
                        if (check == this)
                            throw new InvalidOperationException("No puedes asignar un hijo como padre del mismo GameObject.");

                        check = check.Parent;
                    }
                }

                var oldParent = _parent;
                var worldPosition = Position;

                // Quitar este objeto del padre viejo.
                if (oldParent != null)
                    oldParent.Children.RemoveAll(c => c == this);

                _parent = value;

                // Añadir al padre nuevo sin duplicados.
                if (_parent != null)
                {
                    _parent.Children.RemoveAll(c => c == this);
                    _parent.Children.Add(this);
                }

                MarkTransformDirtyRecursive();
                Position = worldPosition;
                RuntimeScene.NotifyParentChanged(this, oldParent, _parent);
                OnPropertyChanged(nameof(Parent));
            }
        }

        public void SetParent(GameObject? parent, bool worldPositionStays = true)
        {
            if (RuntimeScene.IsReady)
            {
                RuntimeScene.SetParent(this, parent, worldPositionStays);
                return;
            }

            Vector3 localPosition = new Vector3(PosX, PosY, PosZ);
            Parent = parent;

            if (!worldPositionStays)
            {
                PosX = localPosition.X;
                PosY = localPosition.Y;
                PosZ = localPosition.Z;
            }
        }

        public void Unparent(bool worldPositionStays = true)
        {
            SetParent(null, worldPositionStays);
        }

        public List<GameObject> Children { get; set; } = new List<GameObject>();
        public Transform transform = new Transform();
        public Vector3 GlobalPosition { get; set; } = Vector3.Zero;
        public string? PrefabAssetPath { get; set; }
        public List<Component> Components { get; set; } = new List<Component>();

        private readonly Dictionary<Type, List<Component>> componentCache = new();
        private bool componentCacheDirty = true;

        private System.Numerics.Matrix4x4 localMatrixCache = System.Numerics.Matrix4x4.Identity;
        private System.Numerics.Matrix4x4 worldMatrixCache = System.Numerics.Matrix4x4.Identity;
        private bool _transformDirty = true; // si true, hay que recalcular local/world matrices

        public System.Numerics.Matrix4x4 LocalMatrix
        {
            get
            {
                RebuildMatricesIfNeeded();
                return localMatrixCache;
            }
        }

        public System.Numerics.Matrix4x4 WorldMatrix
        {
            get
            {
                RebuildMatricesIfNeeded();
                return worldMatrixCache;
            }
        }

        public Vector3 WorldPosition
        {
            get
            {
                var m = WorldMatrix;
                return new Vector3(m.M41, m.M42, m.M43);
            }
        }

        // Equivalente a transform.position de Unity: lectura/escritura en espacio mundial,
        // independiente de si el objeto tiene padre (convierte automáticamente a local al escribir).
        public Vector3 Position
        {
            get => WorldPosition;
            set
            {
                if (Parent == null)
                {
                    PosX = value.X;
                    PosY = value.Y;
                    PosZ = value.Z;
                    return;
                }

                if (System.Numerics.Matrix4x4.Invert(Parent.WorldMatrix, out var inv))
                {
                    var local = System.Numerics.Vector3.Transform(
                        new System.Numerics.Vector3(value.X, value.Y, value.Z), inv);

                    PosX = local.X;
                    PosY = local.Y;
                    PosZ = local.Z;
                }
            }
        }

        public Vector3 WorldScale
        {
            get
            {
                var m = WorldMatrix;

                float sx = MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13);
                float sy = MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23);
                float sz = MathF.Sqrt(m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33);

                return new Vector3(sx, sy, sz);
            }
        }

        // Si true, la matriz local usa transform.Rotation (cuaternión) en vez de los ángulos
        // Euler (_rx/_ry/_rz). Lo activan los huesos animados para una rotación exacta.
        public bool UseQuaternionRotation;

        // Asigna el transform local completo (posición, rotación cuaternión, escala) de golpe;
        // pensado para animación esqueletal en runtime (rotación exacta, sin pasar por Euler).
        public void SetLocalTRS(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            transform.Position = position;
            transform.Rotation = rotation;
            transform.Scale = scale;
            UseQuaternionRotation = true;
            MarkTransformDirtyRecursive();
        }

        public void MarkTransformDirtyRecursive()
        {
            // Invariante: un nodo sucio implica que todos sus descendientes están sucios
            // (al ensuciarse propaga hacia abajo; al limpiarse solo se limpia él y sus ancestros).
            // Por eso, si ya está sucio, su subárbol también lo está → no hace falta recorrerlo.
            if (_transformDirty)
                return;

            _transformDirty = true;
            foreach (var child in Children)
                child.MarkTransformDirtyRecursive();
        }

        private void RebuildMatricesIfNeeded()
        {
            if (!_transformDirty)
                return;

            float rx = _rx * MathF.PI / 180f;
            float ry = _ry * MathF.PI / 180f;
            float rz = _rz * MathF.PI / 180f;

            var scale = System.Numerics.Matrix4x4.CreateScale(ScaleX, ScaleY, ScaleZ);
            // Los huesos animados usan el cuaternión directo (rotación exacta); el resto, euler.
            var rotation = UseQuaternionRotation
                ? System.Numerics.Matrix4x4.CreateFromQuaternion(new System.Numerics.Quaternion(
                    transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W))
                // Unity-style Euler order: Z, then X, then Y. The renderer, gizmos and
                // lightmap baker already use this order, so Core must match them too.
                : System.Numerics.Matrix4x4.CreateRotationZ(rz) *
                  System.Numerics.Matrix4x4.CreateRotationX(rx) *
                  System.Numerics.Matrix4x4.CreateRotationY(ry);
            var translation = System.Numerics.Matrix4x4.CreateTranslation(PosX, PosY, PosZ);

            localMatrixCache = scale * rotation * translation;
            if (Parent == null)
            {
                worldMatrixCache = localMatrixCache;
            }
            else
            {
                // IMPORTANTE: leer Parent.WorldMatrix UNA sola vez. Como WorldMatrix no cachea
                // (recalcula en cada acceso), acceder dos veces hacía el coste O(2^profundidad):
                // imperceptible en escenas planas, pero colgaba con esqueletos FBX muy anidados.
                var parentWorld = Parent.WorldMatrix;
                worldMatrixCache = localMatrixCache * parentWorld;
                var parentNoScale = RemoveScale(parentWorld);
                var worldPos = System.Numerics.Vector3.Transform(
                    new System.Numerics.Vector3(PosX, PosY, PosZ),
                    parentNoScale);
                worldMatrixCache.M41 = worldPos.X;
                worldMatrixCache.M42 = worldPos.Y;
                worldMatrixCache.M43 = worldPos.Z;
            }

            GlobalPosition = new Vector3(worldMatrixCache.M41, worldMatrixCache.M42, worldMatrixCache.M43);
            _transformDirty = false;
        }

        private static System.Numerics.Matrix4x4 RemoveScale(System.Numerics.Matrix4x4 matrix)
        {
            NormalizeRow(ref matrix.M11, ref matrix.M12, ref matrix.M13);
            NormalizeRow(ref matrix.M21, ref matrix.M22, ref matrix.M23);
            NormalizeRow(ref matrix.M31, ref matrix.M32, ref matrix.M33);
            return matrix;
        }

        private static void NormalizeRow(ref float x, ref float y, ref float z)
        {
            float length = MathF.Sqrt(x * x + y * y + z * z);
            if (length <= 0.000001f)
                return;

            x /= length;
            y /= length;
            z /= length;
        }

        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T();
            AddComponentInstance(component, null);
            return component;
        }

        public T AddComponentWithEngine<T>(PhysicsEngine physics) where T : Component, new()
        {
            T component = new T();
            AddComponentInstance(component, physics);
            return component;
        }

        public Component AddComponent(Type type, PhysicsEngine? physics = null)
        {
            if (!typeof(Component).IsAssignableFrom(type))
                throw new InvalidOperationException($"{type.Name} no hereda de Component.");

            var instance = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"No se pudo crear instancia de {type.Name}.");

            var component = (Component)instance;
            AddComponentInstance(component, physics);
            return component;
        }

        private void AddComponentInstance(Component component, PhysicsEngine? physics)
        {
            component.gameObject = this;
            Components.Add(component);
            componentCacheDirty = true;

            if (physics != null)
            {
                if (component is Collider collider)
                    physics.RegisterCollider(collider);

                if (component is Rigidbody rigidbody)
                    rigidbody.Physics = physics;

                if (component is CharacterController characterController)
                    characterController.Physics = physics;

                if (component is ParticleSystem particleSystem)
                    particleSystem.Physics = physics;
            }

            // Awake/OnEnable de scripts de USUARIO (MonoBehaviour) solo corren en Play (como Unity).
            // En modo edición (cargar escena / añadir componente) NO se ejecutan: así un Awake que use
            // Instantiate/Destroy/Physics.Raycast (válidos solo en runtime) no tumba la carga de la
            // escena (antes la vaciaba). Los componentes del motor (Collider/Rigidbody/…) sí inicializan
            // siempre. El try/catch evita que un Awake que lance arrastre toda la deserialización.
            if (component is not MonoBehaviour || RuntimeScene.IsReady)
            {
                try
                {
                    component.InternalAwake();

                    if (IsActive && component.Enabled)
                        component.InternalEnable();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddComponent] {component.GetType().Name}.Awake/OnEnable lanzó: {ex.Message}");
                }
            }
        }

        private void RebuildComponentCacheIfNeeded()
        {
            if (!componentCacheDirty)
                return;

            componentCache.Clear();

            foreach (var component in Components)
            {
                RegisterComponentType(component.GetType(), component);

                Type? baseType = component.GetType().BaseType;
                while (baseType != null && typeof(Component).IsAssignableFrom(baseType))
                {
                    RegisterComponentType(baseType, component);
                    baseType = baseType.BaseType;
                }

                foreach (Type iface in component.GetType().GetInterfaces())
                    RegisterComponentType(iface, component);
            }

            componentCacheDirty = false;
        }

        private void RegisterComponentType(Type type, Component component)
        {
            if (!componentCache.TryGetValue(type, out var list))
            {
                list = new List<Component>();
                componentCache[type] = list;
            }

            if (!list.Contains(component))
                list.Add(component);
        }

        public T? GetComponent<T>() where T : Component
        {
            RebuildComponentCacheIfNeeded();

            if (componentCache.TryGetValue(typeof(T), out var list))
            {
                foreach (var component in list)
                    if (component is T typed)
                        return typed;
            }

            return null;
        }

        public bool TryGetComponent<T>(out T? component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }

        public IReadOnlyList<T> GetComponents<T>() where T : Component
        {
            RebuildComponentCacheIfNeeded();

            var result = new List<T>();

            if (componentCache.TryGetValue(typeof(T), out var list))
            {
                foreach (var component in list)
                    if (component is T typed)
                        result.Add(typed);
            }

            return result;
        }

        public bool RemoveComponent<T>(PhysicsEngine? physics = null) where T : Component
        {
            var component = GetComponent<T>();
            return component != null && RemoveComponent(component, physics);
        }

        public bool RemoveComponent(Component component, PhysicsEngine? physics = null)
        {
            if (component == null)
                return false;

            if (!Components.Remove(component))
                return false;

            if (component.Enabled)
                component.InternalDisable();

            if (physics != null)
            {
                if (component is Collider collider)
                    physics.UnregisterCollider(collider);

                if (component is Rigidbody rigidbody && rigidbody.Physics == physics)
                    rigidbody.Physics = null;

                if (component is CharacterController characterController && characterController.Physics == physics)
                    characterController.Physics = null;

                if (component is ParticleSystem particleSystem && particleSystem.Physics == physics)
                    particleSystem.Physics = null;
            }

            component.OnDestroy();
            component.gameObject = null!;
            componentCacheDirty = true;
            return true;
        }

        public void SetActive(bool active)
        {
            if (_isActive == active)
                return;

            _isActive = active;

            var snapshot = new List<Component>(Components);
            foreach (var component in snapshot)
            {
                if (!component.Enabled)
                    continue;

                if (active)
                    component.InternalEnable();
                else
                    component.InternalDisable();
            }

            foreach (var child in Children)
                child.SetActive(active);

            OnPropertyChanged(nameof(IsActive));
        }

        public void SetComponentEnabled(Component component, bool enabled)
        {
            if (!Components.Contains(component))
                return;

            if (component.Enabled == enabled)
                return;

            bool wasEnabled = component.Enabled;
            component.SetEnabledInternal(enabled);

            if (!IsActive)
                return;

            if (!wasEnabled && enabled)
                component.InternalEnable();
            else if (wasEnabled && !enabled)
                component.InternalDisable();
        }

        public float PosX
        {
            get => transform.Position.X;
            set { transform.Position.X = value; MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(PosX)); }
        }

        public float PosY
        {
            get => transform.Position.Y;
            set { transform.Position.Y = value; MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(PosY)); }
        }

        public float PosZ
        {
            get => transform.Position.Z;
            set { transform.Position.Z = value; MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(PosZ)); }
        }

        private float _rx, _ry, _rz;

        public float RotX
        {
            get => _rx;
            set { _rx = value; transform.Rotation = Quaternion.Euler(_rx, _ry, _rz); MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(RotX)); }
        }

        public float RotY
        {
            get => _ry;
            set { _ry = value; transform.Rotation = Quaternion.Euler(_rx, _ry, _rz); MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(RotY)); }
        }

        public float RotZ
        {
            get => _rz;
            set { _rz = value; transform.Rotation = Quaternion.Euler(_rx, _ry, _rz); MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(RotZ)); }
        }

        public float ScaleX
        {
            get => transform.Scale.X;
            set { transform.Scale.X = value; MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(ScaleX)); }
        }

        public float ScaleY
        {
            get => transform.Scale.Y;
            set { transform.Scale.Y = value; MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(ScaleY)); }
        }

        public float ScaleZ
        {
            get => transform.Scale.Z;
            set { transform.Scale.Z = value; MarkTransformDirtyRecursive(); OnPropertyChanged(nameof(ScaleZ)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
