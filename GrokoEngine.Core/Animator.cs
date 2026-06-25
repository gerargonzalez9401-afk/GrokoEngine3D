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
    // ANIMATOR
    // =====================================================
    public enum AnimatorUpdateMode { Normal, AnimatePhysics, UnscaledTime }
    public enum AnimatorCullingMode { AlwaysAnimate, CullUpdateTransforms, CullCompletely }

    public readonly record struct AnimatorRuntimeInfo(
        string StateName,
        string MotionType,
        string ClipName,
        string ClipPath,
        bool IsPlaying,
        bool IsVisible,
        float Time,
        float Length,
        float NormalizedTime,
        float EffectiveSpeed,
        int BlendChildCount);

    public readonly record struct AnimatorBlendWeightInfo(
        string MotionPath,
        string DisplayName,
        float Weight,
        float PosX,
        float PosY,
        float Threshold);

    public partial class Animator : Component
    {
        // Asset state machine estilo Unity. Si está asignado, la reproducción usa el clip
        // del estado por defecto del controller; si no, se usa ClipPath (modo standalone).
        public string ControllerPath = "";
        public string AvatarPath = "";
        public string ModelPath = "";
        public bool ApplyRootMotion;
        public AnimatorUpdateMode UpdateMode = AnimatorUpdateMode.Normal;
        public AnimatorCullingMode CullingMode = AnimatorCullingMode.CullUpdateTransforms;

        // Visibilidad para el frame actual (la pone el renderer según el frustum de la cámara).
        // La usa CullingMode: no se serializa, es estado de runtime. Por defecto visible.
        public bool IsVisible = true;

        public string ClipPath = "";
        public List<string> AnimationSources = new();
        public string CurrentClipName = "";
        public bool Loop = true;
        public bool PlayOnAwake = true;
        public float Speed = 1f;
        public float CrossFadeDuration = 0.2f; // fundido por defecto al cambiar de clip/estado por código o inspector

        public bool IsPlaying;
        public float Time;

        private AnimationClipData? clip;
        private string loadedClipPath = "";
        private AnimatorControllerData? controller;
        private string loadedControllerPath = "";
        private List<(string Name, SkeletalClip Clip)>? skeletalClips;
        private string skeletalClipKey = "\0";
        private Dictionary<string, GameObject>? boneMap;
        private Dictionary<GameObject, BoneRestPose>? boneRestPoses;
        // Nombres de los nodos que SÍ son huesos del rig del modelo (de ReadHierarchy).
        // Solo estos se tratan como huesos: así un GameObject vacío que el usuario
        // parente dentro del personaje NO lo gestiona el Animator y se puede mover.
        private HashSet<string>? boneNameWhitelist;
        // CrossFade (transición suave entre estados/clips, estilo Unity).
        private float skelFadeTimer;
        private float skelFadeDuration;
        private Dictionary<GameObject, (Vector3 P, Quaternion R, Vector3 S)>? skelFadeFrom;
        private Dictionary<string, BoneRestPose>? sourceRestPoseMap;
        private string sourceRestPoseKey = "";
        private AnimationClipData? playbackSettings;
        private string loadedPlaybackSettingsPath = "";
        private RootMotionFrame lastRootMotionFrame;
        private string rootMotionKey = "";
        private bool rootMotionInitialized;

        private readonly struct RootMotionFrame
        {
            public RootMotionFrame(bool valid, Vector3 position, float yawRadians)
            {
                Valid = valid;
                Position = position;
                YawRadians = yawRadians;
            }

            public bool Valid { get; }
            public Vector3 Position { get; }
            public float YawRadians { get; }
        }

        private readonly struct BoneRestPose
        {
            public BoneRestPose(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
        }

        private enum FbxChannelKind
        {
            Transform,
            Translation,
            PreRotation,
            Rotation,
            Scaling
        }

        private sealed class BonePoseAccumulator
        {
            public GameObject Bone = null!;
            public Vector3 RestPosition;
            public Quaternion RestRotation;
            public Vector3 RestScale;
            public Vector3 Position;
            public Vector3 FirstPosition;
            public Vector3 Scale;
            public Quaternion? TransformRotation;
            public Quaternion? FirstTransformRotation;
            public Quaternion? PreRotation;
            public Quaternion? FirstPreRotation;
            public Quaternion? Rotation;
            public Quaternion? FirstRotation;
            public bool HasPosition;
            public bool HasScale;
            public bool IsRoot;
            public bool MirrorPose;
            public BoneRestPose SourceRestPose;
            public bool HasSourceRestPose;
        }

        private readonly struct BonePoseSample
        {
            public BonePoseSample(GameObject bone, Vector3 position, Quaternion rotation, Vector3 scale,
                bool isRoot, Vector3 firstPosition, Quaternion firstRotation, bool humanoid)
            {
                Bone = bone;
                Position = position;
                Rotation = rotation;
                Scale = scale;
                IsRoot = isRoot;
                FirstPosition = firstPosition;
                FirstRotation = firstRotation;
                Humanoid = humanoid;
            }

            public GameObject Bone { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
            public bool IsRoot { get; }
            public Vector3 FirstPosition { get; }
            public Quaternion FirstRotation { get; }
            public bool Humanoid { get; }
        }

        // Valores de parámetros en runtime (bool/trigger como 0/1, float/int como su valor).
        private readonly Dictionary<string, float> paramValues = new(StringComparer.OrdinalIgnoreCase);
        private bool paramsInitialized;
        // Parámetros con suavizado (damping) en curso: nombre → (objetivo, tiempo de amortiguación).
        private readonly Dictionary<string, (float Target, float DampTime)> dampedParams = new(StringComparer.OrdinalIgnoreCase);

        // Devuelve el controller cacheado (recarga si ControllerPath cambió). null si no hay.
        public AnimatorControllerData? GetController()
        {
            if (string.IsNullOrWhiteSpace(ControllerPath))
            {
                controller = null;
                loadedControllerPath = "";
                return null;
            }

            if (controller == null || loadedControllerPath != ControllerPath)
            {
                controller = AnimatorControllerAsset.Load(ControllerPath);
                loadedControllerPath = ControllerPath;
            }

            return controller;
        }

        // Fuerza la recarga del controller/clip cacheados (tras editar el asset en el editor).
        public void InvalidateCache()
        {
            controller = null;
            loadedControllerPath = "";
            clip = null;
            loadedClipPath = "";
            skeletalClips = null;
            skeletalClipKey = "\0";
            boneMap = null;
            boneNameWhitelist = null;
            boneRestPoses = null;
            sourceRestPoseMap = null;
            sourceRestPoseKey = "";
            playbackSettings = null;
            loadedPlaybackSettingsPath = "";
            ResetRootMotion();
            paramsInitialized = false;
        }

        // Inicializa los valores de parámetros con los defaults del controller (una vez).
        private void EnsureParamsInitialized()
        {
            if (paramsInitialized) return;
            paramsInitialized = true;
            var ctrl = GetController();
            if (ctrl == null) return;
            foreach (var p in ctrl.Parameters)
                if (!paramValues.ContainsKey(p.Name))
                    paramValues[p.Name] = p.DefaultValue;
        }

        // Estado activo en runtime (cambiado por Play(stateName)); vacío = usar el estado por defecto.
        public string CurrentState = "";

        // Devuelve el estado activo del controller (CurrentState si existe, si no el por defecto).
        public AnimatorStateData? GetActiveState()
        {
            var ctrl = GetController();
            if (ctrl == null) return null;
            if (!string.IsNullOrWhiteSpace(CurrentState))
            {
                foreach (var s in ctrl.States)
                    if (string.Equals(s.Name, CurrentState, StringComparison.OrdinalIgnoreCase))
                        return s;
            }
            return ctrl.GetDefaultState();
        }

        // Ruta de clip efectiva: la del estado activo del controller, o ClipPath si no hay controller.
        public string EffectiveClipPath()
        {
            var ctrl = GetController();
            if (ctrl != null)
            {
                var state = GetActiveState();
                if (state?.MotionType == AnimatorMotionType.BlendTree)
                    return "";
                return state?.ClipPath ?? "";
            }
            return ClipPath;
        }

        public AnimationClipData? GetClip()
        {
            string path = EffectiveClipPath();
            if (string.IsNullOrWhiteSpace(path) || !AnimationClipAsset.IsAnimationPath(path))
            {
                clip = null;
                loadedClipPath = "";
                return null;
            }

            if (clip == null || loadedClipPath != path)
            {
                clip = AnimationClipAsset.Load(path);
                loadedClipPath = path;
            }

            return clip;
        }

        private AnimationClipData GetPlaybackSettings()
        {
            string path = EffectiveClipPath();
            if (string.IsNullOrWhiteSpace(path))
                return new AnimationClipData();

            if (playbackSettings != null && string.Equals(loadedPlaybackSettingsPath, path, StringComparison.OrdinalIgnoreCase))
                return playbackSettings;

            loadedPlaybackSettingsPath = path;
            if (AnimationClipAsset.IsAnimationPath(path) && File.Exists(path))
            {
                playbackSettings = AnimationClipAsset.Load(path);
                return playbackSettings;
            }

            if (ObjLoader.IsSupportedMesh(path) && File.Exists(path))
            {
                playbackSettings = FromImportSettings(ModelImportSettingsAsset.Load(path));
                return playbackSettings;
            }

            playbackSettings = new AnimationClipData();
            return playbackSettings;
        }

        private static AnimationClipData FromImportSettings(ModelImportSettings settings)
        {
            settings.Normalize();
            return new AnimationClipData
            {
                Loop = settings.LoopTime,
                LoopPose = settings.LoopPose,
                CycleOffset = settings.CycleOffset,
                BakeRootRotationIntoPose = settings.BakeRootRotationIntoPose,
                RootRotationBasedUpon = settings.RootRotationBasedUpon,
                RootRotationOffset = settings.RootRotationOffset,
                BakeRootPositionYIntoPose = settings.BakeRootPositionYIntoPose,
                RootPositionYBasedUpon = settings.RootPositionYBasedUpon,
                RootPositionYOffset = settings.RootPositionYOffset,
                BakeRootPositionXZIntoPose = settings.BakeRootPositionXZIntoPose,
                RootPositionXZBasedUpon = settings.RootPositionXZBasedUpon,
                Mirror = settings.Mirror,
                AdditiveReferencePose = settings.AdditiveReferencePose
            };
        }

        public List<(string Name, SkeletalClip Clip)> GetSkeletalClipList()
        {
            string stateClip = EffectiveClipPath();
            string blendKey = "";
            var activeState = GetActiveState();
            if (activeState?.MotionType == AnimatorMotionType.BlendTree)
                blendKey = string.Join("|", activeState.BlendTree.Children.Select(c => c.MotionPath));
            string key = ModelPath + "|" + stateClip + "|" + blendKey + "|" + string.Join("|", AnimationSources);
            if (skeletalClips == null || skeletalClipKey != key)
            {
                skeletalClips = new List<(string, SkeletalClip)>();
                AddSkeletalClipsFrom(ModelPath);
                if (activeState?.MotionType == AnimatorMotionType.BlendTree)
                {
                    foreach (var child in activeState.BlendTree.Children)
                    {
                        if (AnimationClipAsset.IsAnimationPath(child.MotionPath))
                            AddSkeletalClipsFromAnimationAsset(child.MotionPath);
                        else
                            AddSkeletalClipsFrom(child.MotionPath);
                    }
                }
                if (!string.IsNullOrWhiteSpace(stateClip))
                {
                    if (AnimationClipAsset.IsAnimationPath(stateClip))
                        AddSkeletalClipsFromAnimationAsset(stateClip);
                    else
                        AddSkeletalClipsFrom(stateClip);
                }
                foreach (var src in AnimationSources)
                {
                    if (AnimationClipAsset.IsAnimationPath(src))
                        AddSkeletalClipsFromAnimationAsset(src);
                    else
                        AddSkeletalClipsFrom(src);
                }
                skeletalClipKey = key;
            }
            return skeletalClips;
        }

        private void AddSkeletalClipsFrom(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var mesh = ObjLoader.Load(path);
            if (mesh?.Clips == null || mesh.Clips.Count == 0) return;
            string fn = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < mesh.Clips.Count; i++)
            {
                string nm = mesh.Clips.Count == 1 ? fn : $"{fn} {i + 1}";
                if (!skeletalClips!.Any(c => string.Equals(c.Name, nm, StringComparison.OrdinalIgnoreCase)))
                    skeletalClips!.Add((nm, mesh.Clips[i]));
            }
        }

        private void AddSkeletalClipsFromAnimationAsset(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var data = AnimationClipAsset.Load(path);
            if (string.IsNullOrWhiteSpace(data.SourceModelPath) || !File.Exists(data.SourceModelPath)) return;

            var mesh = ObjLoader.Load(data.SourceModelPath);
            if (mesh?.Clips == null || mesh.Clips.Count == 0) return;

            int index = -1;
            if (!string.IsNullOrWhiteSpace(data.SourceClipName))
            {
                for (int i = 0; i < mesh.Clips.Count; i++)
                {
                    if (string.Equals(mesh.Clips[i].Name, data.SourceClipName, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }

            if (index < 0 && mesh.Clips.Count == 1)
                index = 0;
            if (index < 0)
                index = 0;

            string nm = string.IsNullOrWhiteSpace(data.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : data.Name;
            if (!skeletalClips!.Any(c => string.Equals(c.Name, nm, StringComparison.OrdinalIgnoreCase)))
                skeletalClips!.Add((nm, mesh.Clips[index]));
        }

        public SkeletalClip? CurrentSkeletalClip()
        {
            var activeState = GetActiveState();
            if (activeState?.MotionType == AnimatorMotionType.BlendTree)
                return GetDominantBlendTreeClip(activeState.BlendTree)?.Clip;

            var list = GetSkeletalClipList();
            if (list.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(CurrentClipName))
            {
                foreach (var (name, clip) in list)
                    if (string.Equals(name, CurrentClipName, StringComparison.OrdinalIgnoreCase))
                        return clip;
            }

            string stateClip = EffectiveClipPath();
            if (!string.IsNullOrWhiteSpace(stateClip))
            {
                string stateName = Path.GetFileNameWithoutExtension(stateClip);
                foreach (var (name, clip) in list)
                    if (string.Equals(name, stateName, StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith(stateName + " ", StringComparison.OrdinalIgnoreCase))
                        return clip;
            }

            return list[0].Clip;
        }

        private float CurrentBlendTreeDuration()
        {
            var activeState = GetActiveState();
            if (activeState?.MotionType != AnimatorMotionType.BlendTree)
                return 0f;

            var tree = activeState.BlendTree;
            tree.Normalize();

            // Hijos válidos (con clip), igual que en SampleBlendTree.
            var valid = tree.Children
                .Where(c => !string.IsNullOrWhiteSpace(c.MotionPath))
                .Select(c => (Child: c, Runtime: LoadBlendClip(c.MotionPath)))
                .Where(x => x.Runtime != null && x.Runtime.Clip.Duration > 0f)
                .Select(x => (x.Child, Runtime: x.Runtime!))
                .ToList();
            if (valid.Count == 0)
                return 0f;

            var weights = ComputeBlendWeights(tree, valid.Select(v => v.Child).ToList());

            // Duración mezclada = media ponderada de las duraciones de los clips activos.
            // (Unity escala el tiempo así para que walk/run queden en fase al mezclar.)
            float weighted = 0f, wsum = 0f, maxDur = 0f;
            for (int i = 0; i < valid.Count; i++)
            {
                float d = valid[i].Runtime.Clip.Duration;
                if (d > maxDur) maxDur = d;
                if (weights[i] > 0.0001f) { weighted += weights[i] * d; wsum += weights[i]; }
            }
            return wsum > 0f ? weighted / wsum : maxDur;
        }

        private void EnsureBoneMap()
        {
            if (boneMap != null) return;
            boneMap = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            boneRestPoses ??= new Dictionary<GameObject, BoneRestPose>();
            BuildBoneNameWhitelist();
            CollectBones(gameObject);
        }

        // Reúne los nombres de TODOS los nodos del rig del modelo (de ReadHierarchy).
        // Si no hay modelo/jerarquía, queda en null y CollectBones recoge todos los
        // hijos (comportamiento previo, para clips/esqueletos no provenientes de FBX).
        private void BuildBoneNameWhitelist()
        {
            boneNameWhitelist = null;
            if (string.IsNullOrWhiteSpace(ModelPath)) return;
            try
            {
                var rig = ObjLoader.ReadHierarchy(ModelPath);
                if (rig == null) return;
                var set = new HashSet<string>(StringComparer.Ordinal);
                CollectNodeNames(rig, set);
                if (set.Count > 0) boneNameWhitelist = set;
            }
            catch { boneNameWhitelist = null; }
        }

        private static void CollectNodeNames(ModelNode node, HashSet<string> set)
        {
            if (!string.IsNullOrEmpty(node.Name)) set.Add(node.Name);
            foreach (var c in node.Children) CollectNodeNames(c, set);
        }

        private void CollectBones(GameObject go)
        {
            foreach (var child in go.Children)
            {
                // Solo es hueso si su nombre pertenece al rig del modelo. Un GameObject
                // que el usuario parente dentro del personaje (para organizar/adjuntar)
                // no entra al boneMap y el Animator no le toca el transform.
                bool isBone = boneNameWhitelist == null ||
                              (!string.IsNullOrEmpty(child.Name) && boneNameWhitelist.Contains(child.Name));
                if (isBone)
                {
                    if (!string.IsNullOrEmpty(child.Name) && !boneMap!.ContainsKey(child.Name))
                        boneMap[child.Name] = child;
                    CaptureRestPose(child);
                }
                CollectBones(child);
            }
        }

        private BoneRestPose CaptureRestPose(GameObject bone)
        {
            boneRestPoses ??= new Dictionary<GameObject, BoneRestPose>();
            if (!boneRestPoses.TryGetValue(bone, out var pose))
            {
                pose = new BoneRestPose(
                    new Vector3(bone.PosX, bone.PosY, bone.PosZ),
                    bone.transform.Rotation,
                    new Vector3(bone.ScaleX, bone.ScaleY, bone.ScaleZ));
                boneRestPoses[bone] = pose;
            }

            return pose;
        }

        public void Sample(float time)
        {
            var c = GetClip();
            if (c == null || c.Keyframes.Count == 0)
            {
                SampleSkeletal(time);
                return;
            }

            var keyframes = c.Keyframes;
            if (keyframes.Count == 1)
            {
                ApplyKeyframe(keyframes[0]);
                return;
            }

            AnimationKeyframe k0 = keyframes[0];
            AnimationKeyframe k1 = keyframes[^1];

            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                if (time >= keyframes[i].Time && time <= keyframes[i + 1].Time)
                {
                    k0 = keyframes[i];
                    k1 = keyframes[i + 1];
                    break;
                }
            }

            if (time <= keyframes[0].Time)
            {
                ApplyKeyframe(keyframes[0]);
                return;
            }

            if (time >= keyframes[^1].Time)
            {
                ApplyKeyframe(keyframes[^1]);
                return;
            }

            float span = k1.Time - k0.Time;
            float t = span > 0f ? (time - k0.Time) / span : 0f;
            t = AnimationEase.Apply(c.Easing, t); // suavizado (preset del clip)

            gameObject.PosX = Lerp(k0.PosX, k1.PosX, t);
            gameObject.PosY = Lerp(k0.PosY, k1.PosY, t);
            gameObject.PosZ = Lerp(k0.PosZ, k1.PosZ, t);
            gameObject.RotX = LerpAngle(k0.RotX, k1.RotX, t);
            gameObject.RotY = LerpAngle(k0.RotY, k1.RotY, t);
            gameObject.RotZ = LerpAngle(k0.RotZ, k1.RotZ, t);
            gameObject.ScaleX = Lerp(k0.ScaleX, k1.ScaleX, t);
            gameObject.ScaleY = Lerp(k0.ScaleY, k1.ScaleY, t);
            gameObject.ScaleZ = Lerp(k0.ScaleZ, k1.ScaleZ, t);
        }

        private void ApplyKeyframe(AnimationKeyframe k)
        {
            gameObject.PosX = k.PosX;
            gameObject.PosY = k.PosY;
            gameObject.PosZ = k.PosZ;
            gameObject.RotX = k.RotX;
            gameObject.RotY = k.RotY;
            gameObject.RotZ = k.RotZ;
            gameObject.ScaleX = k.ScaleX;
            gameObject.ScaleY = k.ScaleY;
            gameObject.ScaleZ = k.ScaleZ;
        }

        // Captura la pose actual de TODOS los huesos para fundir (crossfade) hacia la nueva.
        private void BeginSkeletalCrossFade(float duration)
        {
            if (duration <= 0.0001f) { skelFadeTimer = 0f; skelFadeFrom = null; return; }
            EnsureBoneMap();
            skelFadeFrom = new Dictionary<GameObject, (Vector3, Quaternion, Vector3)>();
            foreach (var bone in boneMap!.Values)
                skelFadeFrom[bone] = (bone.transform.Position, bone.transform.Rotation, bone.transform.Scale);
            skelFadeDuration = duration;
            skelFadeTimer = duration;
        }

        // Mezcla la pose recién aplicada con la pose capturada al cambiar de estado.
        // weight 1→0 a lo largo de la duración: empieza en la pose vieja y funde a la nueva.
        private void ApplySkeletalCrossFade(float dt)
        {
            if (skelFadeTimer <= 0f || skelFadeFrom == null) return;
            float weight = skelFadeDuration > 0f ? skelFadeTimer / skelFadeDuration : 0f;
            foreach (var (bone, from) in skelFadeFrom)
            {
                var np = bone.transform.Position;
                var nr = bone.transform.Rotation;
                var ns = bone.transform.Scale;
                var fr = from.R;
                if (QuatDotA(nr, fr) < 0f) fr = new Quaternion(-fr.X, -fr.Y, -fr.Z, -fr.W);
                var p = Vector3.Lerp(np, from.P, weight);
                var r = Quaternion.Slerp(nr, fr, weight).Normalized();
                var s = Vector3.Lerp(ns, from.S, weight);
                bone.SetLocalTRS(p, r, s);
            }
            skelFadeTimer -= dt;
            if (skelFadeTimer <= 0f) skelFadeFrom = null;
        }

        private static float QuatDotA(Quaternion a, Quaternion b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        private float CurrentSkeletalOrBlendDuration()
        {
            var st = GetActiveState();
            if (st?.MotionType == AnimatorMotionType.BlendTree)
                return CurrentBlendTreeDuration();
            return CurrentSkeletalClip()?.Duration ?? 0f;
        }

        // Sube cada vez que se (re)muestrea la pose esqueletal. El renderer la usa como
        // clave barata del caché de skinning por CPU (en vez de hashear todas las matrices):
        // misma versión ⟺ misma pose ⟺ mismas matrices, así el caché acierta con pose quieta.
        public int PoseVersion { get; private set; }

        public void SampleSkeletal(float time)
        {
            PoseVersion++;
            var activeState = GetActiveState();
            if (activeState?.MotionType == AnimatorMotionType.BlendTree && SampleBlendTree(activeState.BlendTree, time))
                return;

            var skeletal = CurrentSkeletalClip();
            if (skeletal == null) return;
            EnsureBoneMap();
            var settings = GetPlaybackSettings();
            float sampleTime = ApplyCycleOffset(time, skeletal.Duration, settings.CycleOffset);

            foreach (var pose in SampleSkeletalClipPoses(skeletal, settings, sampleTime))
            {
                var pos = pose.Position;
                var rot = pose.Rotation;

                if (ApplyRootMotion && pose.IsRoot)
                    RemoveExtractedRootMotionFromPose(settings, pose.FirstPosition, pose.FirstRotation, ref pos, ref rot);

                pose.Bone.SetLocalTRS(pos, rot, pose.Scale);
            }
        }

        public override void Start()
        {
            EnsureParamsInitialized();
            IsPlaying = PlayOnAwake || GetController() != null || !string.IsNullOrWhiteSpace(ClipPath);
        }

        public override void Update(double dt)
        {
            if (!IsPlaying)
                return;

            // ── Culling Mode (estilo Unity) ──
            // CullCompletely: si no es visible, se congela del todo (no avanza tiempo ni lógica).
            // CullUpdateTransforms: si no es visible, la máquina de estados y el tiempo siguen,
            //   pero NO se escribe la pose en los huesos (ahorra el coste de muestreo/skinning).
            // AlwaysAnimate: siempre se actualiza completo.
            if (CullingMode == AnimatorCullingMode.CullCompletely && !IsVisible)
                return;
            bool writePose = !(CullingMode == AnimatorCullingMode.CullUpdateTransforms && !IsVisible);

            EnsureParamsInitialized();
            UpdateDampedParams((float)dt);   // suaviza parámetros (damping) del blend tree

            var c = GetClip();
            if (c == null || c.Keyframes.Count == 0)
            {
                var activeState = GetActiveState();
                bool isBlendTree = activeState?.MotionType == AnimatorMotionType.BlendTree;
                var skeletal = isBlendTree ? null : CurrentSkeletalClip();
                float duration = isBlendTree ? CurrentBlendTreeDuration() : skeletal?.Duration ?? 0f;
                if (duration <= 0f)
                {
                    // Empty controller states must be able to leave through their transitions.
                    // Otherwise an Entry -> empty default state gets stuck forever.
                    EvaluateTransitions(1f);
                    return;
                }

                float previousTime = Time;
                Time += (float)dt * EffectiveSpeed();
                float normalizedSkeletal = Time / duration;
                bool wrapped = false;
                if (Time >= duration)
                {
                    var playback = GetPlaybackSettings();
                    bool controllerDriven = GetController() != null;
                    bool shouldLoop = playback.Loop || GetActiveState()?.Loop == true || (!controllerDriven && Loop);
                    if (shouldLoop)
                    {
                        Time %= duration;
                        wrapped = true;
                    }
                    else
                    {
                        Time = duration;
                        // Con un Animator Controller NO se detiene la reproducción: el estado
                        // se queda en el último frame y la máquina de estados sigue evaluando
                        // transiciones (exit time, AnyState, triggers tardíos). Si paramos
                        // (IsPlaying=false) Update saldría arriba y el animator quedaría congelado.
                        if (!controllerDriven) IsPlaying = false;
                    }
                }

                if (writePose)
                {
                    if (skeletal != null)
                        ApplySkeletalRootMotion(skeletal, previousTime, Time, wrapped);
                    SampleSkeletal(Time);
                    ApplySkeletalCrossFade((float)dt);   // funde desde la pose del estado anterior
                }
                EvaluateTransitions(normalizedSkeletal);
                return;
            }

            Time += (float)dt * EffectiveSpeed();
            float length = c.Keyframes[^1].Time;
            if (length <= 0f)
                return;

            float normalized = Time / length;

            if (Time >= length)
            {
                if (c.Loop)
                    Time %= length;
                else
                {
                    Time = length;
                    // Igual que la rama esqueletal: con controller, el estado se queda en el
                    // último frame y se siguen evaluando transiciones en vez de congelarse.
                    if (GetController() == null) IsPlaying = false;
                }
            }

            if (writePose)
                Sample(Time);

            EvaluateTransitions(normalized);
        }

        // Evalúa las transiciones del estado activo; si alguna se cumple, cambia de estado.
        private void EvaluateTransitions(float normalizedTime)
        {
            var ctrl = GetController();
            var state = GetActiveState();
            if (ctrl == null || state == null)
                return;

            if (EvaluateTransitionList(ctrl.AnyStateTransitions, normalizedTime, fromAnyState: true))
                return;

            EvaluateTransitionList(state.Transitions, normalizedTime, fromAnyState: false);
        }

        private bool EvaluateTransitionList(List<AnimatorTransition> transitions, float normalizedTime, bool fromAnyState)
        {
            if (transitions.Count == 0)
                return false;

            bool anySolo = transitions.Any(t => t.Solo && !t.Mute);

            foreach (var t in transitions)
            {
                if (string.IsNullOrWhiteSpace(t.ToState))
                    continue;
                if (fromAnyState && string.Equals(t.ToState, GetActiveState()?.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (t.Mute || (anySolo && !t.Solo))
                    continue;

                bool exitOk = fromAnyState || !t.HasExitTime || normalizedTime >= t.ExitTime;
                if (!exitOk)
                    continue;

                if (!ConditionsMet(t.Conditions))
                    continue;

                ConsumeTriggers(t.Conditions);
                // CrossFade: captura la pose actual (estado viejo) para fundir a la nueva.
                // (Si !FixedDuration, TransitionDuration es normalizado; lo escalamos por la duración del estado actual.)
                float fadeSecs = t.FixedDuration ? t.TransitionDuration
                    : t.TransitionDuration * MathF.Max(0.0001f, CurrentSkeletalOrBlendDuration());
                BeginSkeletalCrossFade(fadeSecs);
                CurrentState = t.ToState;
                CurrentClipName = t.ToState;
                clip = null;        // fuerza recargar el clip del nuevo estado
                loadedClipPath = "";
                skeletalClips = null;
                skeletalClipKey = "\0";
                ResetRootMotion();
                Time = 0f;
                IsPlaying = true;
                Sample(0f);
                return true;
            }

            return false;
        }

        private bool ConditionsMet(List<AnimatorCondition> conditions)
        {
            // Sin condiciones: solo se cruza por exit time (ya verificado por el caller).
            foreach (var cond in conditions)
            {
                float v = GetFloat(cond.Parameter);
                bool ok = cond.Mode switch
                {
                    AnimatorConditionMode.If => v != 0f,
                    AnimatorConditionMode.IfNot => v == 0f,
                    AnimatorConditionMode.Greater => v > cond.Threshold,
                    AnimatorConditionMode.Less => v < cond.Threshold,
                    AnimatorConditionMode.Equals => (int)MathF.Round(v) == (int)MathF.Round(cond.Threshold),
                    AnimatorConditionMode.NotEquals => (int)MathF.Round(v) != (int)MathF.Round(cond.Threshold),
                    _ => false
                };
                if (!ok) return false;
            }
            return true;
        }

        private void ConsumeTriggers(List<AnimatorCondition> conditions)
        {
            var ctrl = GetController();
            if (ctrl == null) return;
            foreach (var cond in conditions)
            {
                var p = ctrl.Parameters.FirstOrDefault(x => string.Equals(x.Name, cond.Parameter, StringComparison.OrdinalIgnoreCase));
                if (p != null && p.Type == AnimatorParameterType.Trigger)
                    paramValues[cond.Parameter] = 0f;
            }
        }

        private float EffectiveSpeed()
        {
            float stateSpeed = GetActiveState()?.Speed ?? 1f;
            return Speed * stateSpeed;
        }

        // =====================================================
        // API DE SCRIPTING (estilo Unity)
        // =====================================================

        /// <summary>Duración del clip activo en segundos (0 si no hay clip).</summary>
        public float Length
        {
            get
            {
                var c = GetClip();
                if (c != null && c.Keyframes.Count > 0)
                    return c.Keyframes[^1].Time;
                return CurrentSkeletalClip()?.Duration ?? 0f;
            }
        }

        /// <summary>Progreso normalizado 0..1 dentro del clip activo.</summary>
        public float NormalizedTime
        {
            get { float len = Length; return len > 0f ? Time / len : 0f; }
            set { float len = Length; Time = len > 0f ? value * len : 0f; if (!float.IsNaN(Time)) Sample(Time); }
        }

        // ── Parámetros (estilo Unity: SetBool/SetFloat/SetInteger/SetTrigger) ──
        public void SetBool(string name, bool value) => paramValues[name] = value ? 1f : 0f;
        public void SetFloat(string name, float value)
        {
            paramValues[name] = value;
            dampedParams.Remove(name); // un set instantáneo cancela el suavizado en curso
        }

        /// <summary>Suaviza el parámetro hacia 'value' en ~dampTime segundos (como Unity:
        /// animator.SetFloat(name, value, dampTime)). Ideal para que el Blend Tree no pegue tirones.</summary>
        public void SetFloat(string name, float value, float dampTime)
        {
            if (dampTime <= 0.0001f) { SetFloat(name, value); return; }
            if (!paramValues.ContainsKey(name)) paramValues[name] = value; // primer valor sin rampa
            dampedParams[name] = (value, dampTime);
        }

        // Avanza el suavizado de parámetros (llamar cada frame antes de muestrear).
        private void UpdateDampedParams(float dt)
        {
            if (dampedParams.Count == 0 || dt <= 0f) return;
            List<string>? done = null;
            foreach (var kv in dampedParams)
            {
                float cur = paramValues.TryGetValue(kv.Key, out var v) ? v : 0f;
                float k = 1f - MathF.Exp(-dt / MathF.Max(kv.Value.DampTime, 1e-4f));
                cur += (kv.Value.Target - cur) * k;
                if (MathF.Abs(cur - kv.Value.Target) < 1e-4f) { cur = kv.Value.Target; (done ??= new()).Add(kv.Key); }
                paramValues[kv.Key] = cur;
            }
            if (done != null) foreach (var n in done) dampedParams.Remove(n);
        }
        public void SetInteger(string name, int value) => paramValues[name] = value;
        public void SetTrigger(string name) => paramValues[name] = 1f;
        public void ResetTrigger(string name) => paramValues[name] = 0f;
        public bool GetBool(string name) => paramValues.TryGetValue(name, out var v) && v != 0f;
        public float GetFloat(string name) => paramValues.TryGetValue(name, out var v) ? v : 0f;
        public int GetInteger(string name) => (int)MathF.Round(GetFloat(name));

        /// <summary>Reanuda/inicia la reproducción del estado actual.</summary>
        public void Play() => IsPlaying = true;

        /// <summary>Reproduce el estado indicado del Animator Controller desde el principio.</summary>
        public void Play(string stateName)
        {
            if (!string.IsNullOrWhiteSpace(stateName))
            {
                CurrentState = stateName;
                CurrentClipName = stateName;
            }
            InvalidateCache();
            Time = 0f;
            ResetRootMotion();
            IsPlaying = true;
            Sample(0f);
        }

        /// <summary>Funde suavemente (crossfade) hacia el estado indicado en 'duration' segundos.</summary>
        public void CrossFade(string stateName, float duration = 0.2f)
        {
            if (!string.IsNullOrWhiteSpace(stateName))
            {
                BeginSkeletalCrossFade(duration);   // captura la pose actual antes de cambiar
                CurrentState = stateName;
                CurrentClipName = stateName;
            }
            InvalidateCache();
            Time = 0f;
            ResetRootMotion();
            IsPlaying = true;
        }

        /// <summary>Cambia el estado activo sin reiniciar el tiempo.</summary>
        public void SetState(string stateName)
        {
            CurrentState = stateName ?? "";
            CurrentClipName = stateName ?? "";
            InvalidateCache();
            ResetRootMotion();
        }

        /// <summary>Pausa la reproducción manteniendo el tiempo actual.</summary>
        public void Pause() => IsPlaying = false;

        /// <summary>Detiene la reproducción y vuelve al inicio.</summary>
        public void Stop()
        {
            IsPlaying = false;
            Time = 0f;
            ResetRootMotion();
            Sample(0f);
        }

        /// <summary>Reinicia el clip actual desde el principio y reproduce.</summary>
        public void Replay()
        {
            Time = 0f;
            IsPlaying = true;
            Sample(0f);
        }

        /// <summary>True si el clip activo no está en bucle y ya llegó al final.</summary>
        public bool IsFinished
        {
            get
            {
                var c = GetClip();
                if (c != null && c.Loop) return false;
                if (c == null && Loop) return false;
                return !IsPlaying && Time >= Length && Length > 0f;
            }
        }

        // Memo reutilizable de matrices de mundo por nodo, válido durante UNA llamada a
        // ComputeSkinMatrices. Evita recalcular la cadena de padres por cada hueso
        // (los huesos comparten ancestros): pasa de O(huesos×profundidad) a O(nodos).
        public AnimatorRuntimeInfo GetRuntimeInfo()
        {
            var activeState = GetActiveState();
            string clipPath = EffectiveClipPath();
            string clipName = !string.IsNullOrWhiteSpace(CurrentClipName)
                ? CurrentClipName
                : Path.GetFileNameWithoutExtension(clipPath);

            if (string.IsNullOrWhiteSpace(clipName))
                clipName = CurrentSkeletalClip()?.Name ?? "";

            return new AnimatorRuntimeInfo(
                activeState?.Name ?? (string.IsNullOrWhiteSpace(CurrentState) ? "(Standalone)" : CurrentState),
                activeState?.MotionType.ToString() ?? "Clip",
                clipName,
                clipPath,
                IsPlaying,
                IsVisible,
                Time,
                Length,
                NormalizedTime,
                EffectiveSpeed(),
                activeState?.MotionType == AnimatorMotionType.BlendTree ? activeState.BlendTree.Children.Count : 0);
        }

        private readonly Dictionary<GameObject, System.Numerics.Matrix4x4> _worldMemo = new();

        public System.Numerics.Matrix4x4[] ComputeSkinMatrices(List<string> boneNames, List<System.Numerics.Matrix4x4> boneOffsets)
        {
            EnsureBoneMap();
            _worldMemo.Clear();
            var result = new System.Numerics.Matrix4x4[boneNames.Count];
            for (int i = 0; i < boneNames.Count; i++)
            {
                if (boneMap!.TryGetValue(boneNames[i], out var go))
                    result[i] = boneOffsets[i] * CleanWorldMatrix(go, _worldMemo);
                else
                    result[i] = System.Numerics.Matrix4x4.Identity;
            }
            return result;
        }

        // Mundo limpio (con escala) = Local * MundoPadre, memoizado por nodo.
        private static System.Numerics.Matrix4x4 CleanWorldMatrix(GameObject go, Dictionary<GameObject, System.Numerics.Matrix4x4> memo)
        {
            if (memo.TryGetValue(go, out var cached))
                return cached;
            var m = go.Parent == null
                ? go.LocalMatrix
                : go.LocalMatrix * CleanWorldMatrix(go.Parent, memo);
            memo[go] = m;
            return m;
        }
    }
}
