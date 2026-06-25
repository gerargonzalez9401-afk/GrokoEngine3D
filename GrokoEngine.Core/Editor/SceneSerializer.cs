using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GrokoEngine
{
    public static class SceneSerializer
    {
        private const int CurrentSceneVersion = 2;
        private const int CurrentPrefabVersion = 2;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            // Jerarquías profundas (p. ej. esqueletos FBX de Mixamo con ~30 huesos anidados)
            // superan el MaxDepth por defecto (64) y lanzaban JsonException → crash al
            // serializar la escena. Subimos el límite con holgura.
            MaxDepth = 512
        };

        // Prefijo para serializar referencias a otros objetos de la escena (guarda el EditorId del dueño).
        private const string RefPrefix = "@@ref:";

        // Prefijo para serializar referencias a assets ScriptableObject (guarda la ruta del ".asset").
        private const string AssetRefPrefix = "@@asset:";

        // Prefijo para serializar referencias a un ASSET de prefab (guarda la ruta del ".prefab").
        // Se usa cuando un campo GameObject/Transform/Component apunta a una plantilla de prefab
        // arrastrada desde el panel de Assets en vez de a una instancia viva de la escena.
        private const string PrefabRefPrefix = "@@prefab:";

        // Índice temporal Transform/GameObject -> EditorId, vivo solo durante Serialize.
        [ThreadStatic] private static Dictionary<object, string>? _serializeOwnerIndex;
        // Referencias pendientes de resolver en el 2º pase, vivo solo durante Deserialize.
        [ThreadStatic] private static List<(Component comp, FieldInfo field, string targetId)>? _pendingRefs;

        public static void Save(string path, IReadOnlyList<GameObject> objects)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string? assetsRoot = InferAssetsRoot(path);
            File.WriteAllText(path, Serialize(objects, assetsRoot));
            EnsureMetaIfInsideAssets(path, assetsRoot);
        }

        public static List<GameObject> Load(string path, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler)
        {
            var json = File.ReadAllText(path);
            return Deserialize(json, physicsEngine, scriptCompiler, InferAssetsRoot(path));
        }

        public static string Serialize(IReadOnlyList<GameObject> objects) =>
            Serialize(objects, baseAssetsPath: null);

        private static string Serialize(IReadOnlyList<GameObject> objects, string? baseAssetsPath)
        {
            _serializeOwnerIndex = BuildOwnerIndex(objects);
            try
            {
                var scene = new SceneData
                {
                    Version = CurrentSceneVersion,
                    Objects = objects.Select(o => ToData(o, baseAssetsPath)).ToList()
                };
                return JsonSerializer.Serialize(scene, JsonOptions);
            }
            finally { _serializeOwnerIndex = null; }
        }

        public static List<GameObject> Deserialize(string json, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler) =>
            Deserialize(json, physicsEngine, scriptCompiler, baseAssetsPath: null);

        private static List<GameObject> Deserialize(string json, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler, string? baseAssetsPath)
        {
            var scene = JsonSerializer.Deserialize<SceneData>(json, JsonOptions) ?? new SceneData();
            scene.Normalize();
            _pendingRefs = new List<(Component, FieldInfo, string)>();
            try
            {
                var roots = scene.Objects.Select(o => FromData(o, null, physicsEngine, scriptCompiler, baseAssetsPath)).ToList();
                ResolveRefs(roots);
                return roots;
            }
            finally { _pendingRefs = null; }
        }

        public static void SavePrefab(string path, GameObject obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _serializeOwnerIndex = BuildOwnerIndex(new[] { obj });
            try
            {
                var prefab = new PrefabData
                {
                    Version = CurrentPrefabVersion,
                    Root = ToData(obj, InferAssetsRoot(path))
                };
                File.WriteAllText(path, JsonSerializer.Serialize(prefab, JsonOptions));
                EnsureMetaIfInsideAssets(path, InferAssetsRoot(path));
            }
            finally { _serializeOwnerIndex = null; }
        }

        public static GameObject LoadPrefab(string path, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler)
        {
            var json = File.ReadAllText(path);
            var baseAssetsPath = InferAssetsRoot(path);
            var prefab = JsonSerializer.Deserialize<PrefabData>(json, JsonOptions);
            var data = prefab?.Root ?? JsonSerializer.Deserialize<GameObjectData>(json, JsonOptions)
                ?? throw new InvalidDataException("Prefab invalido.");
            data.Normalize();
            _pendingRefs = new List<(Component, FieldInfo, string)>();
            try
            {
                var root = FromData(data, null, physicsEngine, scriptCompiler, baseAssetsPath);
                ResolveRefs(new List<GameObject> { root });
                return root;
            }
            finally { _pendingRefs = null; }
        }

        public static string SerializeObject(GameObject obj)
        {
            _serializeOwnerIndex = BuildOwnerIndex(new[] { obj });
            try
            {
                return JsonSerializer.Serialize(ToData(obj, baseAssetsPath: null), JsonOptions);
            }
            finally { _serializeOwnerIndex = null; }
        }

        public static GameObject DeserializeObject(string json, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler)
        {
            var data = JsonSerializer.Deserialize<GameObjectData>(json, JsonOptions)
                ?? throw new InvalidDataException("Objeto invalido.");
            data.Normalize();
            _pendingRefs = new List<(Component, FieldInfo, string)>();
            try
            {
                var root = FromData(data, null, physicsEngine, scriptCompiler, baseAssetsPath: null);
                ResolveRefs(new List<GameObject> { root });
                return root;
            }
            finally { _pendingRefs = null; }
        }

        private static GameObjectData ToData(GameObject obj, string? baseAssetsPath)
        {
            return new GameObjectData
            {
                Id = obj.EditorId,
                Name = obj.Name,
                Type = obj.Type,
                IsActive = obj.IsActive,
                IsCamera = obj.IsCamera,
                IsStatic = obj.IsStatic,
                Layer = obj.Layer,
                PrefabAssetPath = SerializeAssetPath(obj.PrefabAssetPath, baseAssetsPath),
                Position = VectorData.From(obj.PosX, obj.PosY, obj.PosZ),
                Rotation = VectorData.From(obj.RotX, obj.RotY, obj.RotZ),
                Quat = obj.UseQuaternionRotation
                    ? new[] { obj.transform.Rotation.X, obj.transform.Rotation.Y, obj.transform.Rotation.Z, obj.transform.Rotation.W }
                    : null,
                UseQuat = obj.UseQuaternionRotation,
                Scale = VectorData.From(obj.ScaleX, obj.ScaleY, obj.ScaleZ),
                Components = obj.Components.Select(c => ToComponentData(c, baseAssetsPath)).Where(c => c != null).Cast<ComponentData>().ToList(),
                Children = obj.Children.Select(c => ToData(c, baseAssetsPath)).ToList()
            };
        }

        private static ComponentData? ToComponentData(Component comp, string? baseAssetsPath)
        {
            var data = new ComponentData { TypeName = GetSerializableTypeName(comp.GetType()) };

            switch (comp)
            {
                case Material mat:
                    data.Fields["R"] = mat.R.ToString(CultureInfo.InvariantCulture);
                    data.Fields["G"] = mat.G.ToString(CultureInfo.InvariantCulture);
                    data.Fields["B"] = mat.B.ToString(CultureInfo.InvariantCulture);
                    data.Fields["AssetPath"] = SerializeAssetPath(mat.AssetPath, baseAssetsPath);
                    data.Fields["TexturePath"] = SerializeAssetPath(mat.TexturePath, baseAssetsPath);
                    data.Fields["NormalMapPath"] = SerializeAssetPath(mat.NormalMapPath, baseAssetsPath);
                    data.Fields["RoughnessMapPath"] = SerializeAssetPath(mat.RoughnessMapPath, baseAssetsPath);
                    data.Fields["MetallicMapPath"] = SerializeAssetPath(mat.MetallicMapPath, baseAssetsPath);
                    data.Fields["ShaderGraphPath"] = SerializeAssetPath(mat.ShaderGraphPath, baseAssetsPath);
                    data.Fields["ShaderGraphProperties"] = JsonSerializer.Serialize(mat.ShaderGraphProperties);
                    data.Fields["ShaderGraphTextures"] = JsonSerializer.Serialize(mat.ShaderGraphTextures.ToDictionary(
                        kv => kv.Key,
                        kv => SerializeAssetPath(kv.Value, baseAssetsPath)));
                    data.Fields["Roughness"] = mat.Roughness.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Metallic"] = mat.Metallic.ToString(CultureInfo.InvariantCulture);
                    data.Fields["EmissionR"] = mat.EmissionR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["EmissionG"] = mat.EmissionG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["EmissionB"] = mat.EmissionB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["EmissionIntensity"] = mat.EmissionIntensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["IsInstance"] = mat.IsInstance.ToString();
                    return data;
                case Rigidbody rb:
                    data.Fields["Gravity"] = rb.Gravity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Mass"] = rb.Mass.ToString(CultureInfo.InvariantCulture);
                    data.Fields["UseGravity"] = rb.UseGravity.ToString();
                    data.Fields["IsKinematic"] = rb.IsKinematic.ToString();
                    data.Fields["Drag"] = rb.Drag.ToString(CultureInfo.InvariantCulture);
                    data.Fields["AngularDrag"] = rb.AngularDrag.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Bounciness"] = rb.Bounciness.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Friction"] = rb.Friction.ToString(CultureInfo.InvariantCulture);
                    data.Fields["FreezePositionX"] = rb.FreezePositionX.ToString();
                    data.Fields["FreezePositionY"] = rb.FreezePositionY.ToString();
                    data.Fields["FreezePositionZ"] = rb.FreezePositionZ.ToString();
                    data.Fields["FreezeRotationX"] = rb.FreezeRotationX.ToString();
                    data.Fields["FreezeRotationY"] = rb.FreezeRotationY.ToString();
                    data.Fields["FreezeRotationZ"] = rb.FreezeRotationZ.ToString();
                    return data;
                case BoxCollider bc:
                    data.Fields["SizeX"] = bc.Size.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SizeY"] = bc.Size.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SizeZ"] = bc.Size.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterX"] = bc.Center.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterY"] = bc.Center.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterZ"] = bc.Center.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["IsTrigger"] = bc.IsTrigger.ToString();
                    data.Fields["PhysicMaterial"] = bc.PhysicMaterial;
                    data.Fields["Friction"] = bc.Friction.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Bounciness"] = bc.Bounciness.ToString(CultureInfo.InvariantCulture);
                    return data;
                case SphereCollider sc:
                    data.Fields["Radius"] = sc.Radius.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterX"] = sc.Center.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterY"] = sc.Center.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterZ"] = sc.Center.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["IsTrigger"] = sc.IsTrigger.ToString();
                    data.Fields["PhysicMaterial"] = sc.PhysicMaterial;
                    data.Fields["Friction"] = sc.Friction.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Bounciness"] = sc.Bounciness.ToString(CultureInfo.InvariantCulture);
                    return data;
                case CapsuleCollider cc:
                    data.Fields["Radius"] = cc.Radius.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Height"] = cc.Height.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Axis"] = ((int)cc.Axis).ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterX"] = cc.Center.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterY"] = cc.Center.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterZ"] = cc.Center.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["IsTrigger"] = cc.IsTrigger.ToString();
                    data.Fields["PhysicMaterial"] = cc.PhysicMaterial;
                    data.Fields["Friction"] = cc.Friction.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Bounciness"] = cc.Bounciness.ToString(CultureInfo.InvariantCulture);
                    return data;
                case MeshCollider mc:
                    data.Fields["SizeX"] = mc.Size.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SizeY"] = mc.Size.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SizeZ"] = mc.Size.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterX"] = mc.Center.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterY"] = mc.Center.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterZ"] = mc.Center.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["UseMeshBounds"] = mc.UseMeshBounds.ToString();
                    data.Fields["IsTrigger"] = mc.IsTrigger.ToString();
                    data.Fields["PhysicMaterial"] = mc.PhysicMaterial;
                    data.Fields["Friction"] = mc.Friction.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Bounciness"] = mc.Bounciness.ToString(CultureInfo.InvariantCulture);
                    return data;
                case CharacterController cc:
                    data.Fields["Height"] = cc.Height.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Radius"] = cc.Radius.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterX"] = cc.Center.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterY"] = cc.Center.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["CenterZ"] = cc.Center.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["AutoCenter"] = cc.AutoCenter.ToString();
                    data.Fields["SkinWidth"] = cc.SkinWidth.ToString(CultureInfo.InvariantCulture);
                    data.Fields["StepOffset"] = cc.StepOffset.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SlopeLimit"] = cc.SlopeLimit.ToString(CultureInfo.InvariantCulture);
                    data.Fields["UseGravity"] = cc.UseGravity.ToString();
                    data.Fields["Gravity"] = cc.Gravity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["JumpSpeed"] = cc.JumpSpeed.ToString(CultureInfo.InvariantCulture);
                    data.Fields["MaxFallSpeed"] = cc.MaxFallSpeed.ToString(CultureInfo.InvariantCulture);
                    data.Fields["PushPower"] = cc.PushPower.ToString(CultureInfo.InvariantCulture);
                    return data;
                case Camera cam:
                    data.Fields["FOV"] = cam.FOV.ToString(CultureInfo.InvariantCulture);
                    data.Fields["NearClip"] = cam.NearClip.ToString(CultureInfo.InvariantCulture);
                    data.Fields["FarClip"] = cam.FarClip.ToString(CultureInfo.InvariantCulture);
                    data.Fields["AntiAliasing"] = cam.AntiAliasing.ToString();
                    data.Fields["AntiAliasingSamples"] = cam.AntiAliasingSamples.ToString(CultureInfo.InvariantCulture);
                    data.Fields["FrustumCulling"] = cam.FrustumCulling.ToString();
                    data.Fields["OcclusionCulling"] = cam.OcclusionCulling.ToString();
                    return data;
                case DirectionalLight dl:
                    data.Fields["DirectionX"] = dl.Direction.X.ToString(CultureInfo.InvariantCulture);
                    data.Fields["DirectionY"] = dl.Direction.Y.ToString(CultureInfo.InvariantCulture);
                    data.Fields["DirectionZ"] = dl.Direction.Z.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Intensity"] = dl.Intensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["R"] = dl.R.ToString(CultureInfo.InvariantCulture);
                    data.Fields["G"] = dl.G.ToString(CultureInfo.InvariantCulture);
                    data.Fields["B"] = dl.B.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Shadows"] = dl.Shadows.ToString();
                    data.Fields["ShadowStrength"] = dl.ShadowStrength.ToString(CultureInfo.InvariantCulture);
                    data.Fields["UseTemperature"] = dl.UseTemperature.ToString();
                    data.Fields["Temperature"] = dl.Temperature.ToString(CultureInfo.InvariantCulture);
                    data.Fields["IndirectMultiplier"] = dl.IndirectMultiplier.ToString(CultureInfo.InvariantCulture);
                    data.Fields["ShowSunDisk"] = dl.ShowSunDisk.ToString();
                    data.Fields["AngularDiameter"] = dl.AngularDiameter.ToString(CultureInfo.InvariantCulture);
                    data.Fields["GodRays"] = dl.GodRays.ToString();
                    data.Fields["GodRaysStrength"] = dl.GodRaysStrength.ToString(CultureInfo.InvariantCulture);
                    return data;
                case PointLight pl:
                    data.Fields["Intensity"] = pl.Intensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Range"] = pl.Range.ToString(CultureInfo.InvariantCulture);
                    data.Fields["R"] = pl.R.ToString(CultureInfo.InvariantCulture);
                    data.Fields["G"] = pl.G.ToString(CultureInfo.InvariantCulture);
                    data.Fields["B"] = pl.B.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Shadows"] = pl.Shadows.ToString();
                    data.Fields["ShadowStrength"] = pl.ShadowStrength.ToString(CultureInfo.InvariantCulture);
                    data.Fields["UseTemperature"] = pl.UseTemperature.ToString();
                    data.Fields["Temperature"] = pl.Temperature.ToString(CultureInfo.InvariantCulture);
                    return data;
                case SpotLight sl:
                    data.Fields["Intensity"] = sl.Intensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Range"] = sl.Range.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Angle"] = sl.Angle.ToString(CultureInfo.InvariantCulture);
                    data.Fields["R"] = sl.R.ToString(CultureInfo.InvariantCulture);
                    data.Fields["G"] = sl.G.ToString(CultureInfo.InvariantCulture);
                    data.Fields["B"] = sl.B.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Shadows"] = sl.Shadows.ToString();
                    data.Fields["ShadowStrength"] = sl.ShadowStrength.ToString(CultureInfo.InvariantCulture);
                    data.Fields["UseTemperature"] = sl.UseTemperature.ToString();
                    data.Fields["Temperature"] = sl.Temperature.ToString(CultureInfo.InvariantCulture);
                    return data;
                case AmbientLight al:
                    data.Fields["Intensity"] = al.Intensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SkyStrength"] = al.SkyStrength.ToString(CultureInfo.InvariantCulture);
                    data.Fields["R"] = al.R.ToString(CultureInfo.InvariantCulture);
                    data.Fields["G"] = al.G.ToString(CultureInfo.InvariantCulture);
                    data.Fields["B"] = al.B.ToString(CultureInfo.InvariantCulture);
                    return data;
                case AreaLight area:
                    data.Fields["Intensity"] = area.Intensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Range"] = area.Range.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Size"] = area.Size.ToString(CultureInfo.InvariantCulture);
                    data.Fields["R"] = area.R.ToString(CultureInfo.InvariantCulture);
                    data.Fields["G"] = area.G.ToString(CultureInfo.InvariantCulture);
                    data.Fields["B"] = area.B.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Shadows"] = area.Shadows.ToString();
                    data.Fields["ShadowStrength"] = area.ShadowStrength.ToString(CultureInfo.InvariantCulture);
                    return data;
                case RectangleLight rect:
                    data.Fields["Intensity"] = rect.Intensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Range"] = rect.Range.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Width"] = rect.Width.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Height"] = rect.Height.ToString(CultureInfo.InvariantCulture);
                    data.Fields["R"] = rect.R.ToString(CultureInfo.InvariantCulture);
                    data.Fields["G"] = rect.G.ToString(CultureInfo.InvariantCulture);
                    data.Fields["B"] = rect.B.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Shadows"] = rect.Shadows.ToString();
                    data.Fields["ShadowStrength"] = rect.ShadowStrength.ToString(CultureInfo.InvariantCulture);
                    return data;
                case PostProcessSettings pp:
                    data.Fields["PostProcessEnabled"] = pp.PostProcessEnabled.ToString();
                    data.Fields["Exposure"] = pp.Exposure.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Gamma"] = pp.Gamma.ToString(CultureInfo.InvariantCulture);
                    data.Fields["ToneMapping"] = pp.ToneMapping.ToString();
                    data.Fields["ToneMappingMode"] = pp.ToneMappingMode.ToString(CultureInfo.InvariantCulture);
                    data.Fields["WhiteBalanceTemperature"] = pp.WhiteBalanceTemperature.ToString(CultureInfo.InvariantCulture);
                    data.Fields["WhiteBalanceTint"] = pp.WhiteBalanceTint.ToString(CultureInfo.InvariantCulture);
                    data.Fields["FilmGrain"] = pp.FilmGrain.ToString();
                    data.Fields["FilmGrainIntensity"] = pp.FilmGrainIntensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Bloom"] = pp.Bloom.ToString();
                    data.Fields["BloomStrength"] = pp.BloomStrength.ToString(CultureInfo.InvariantCulture);
                    data.Fields["BloomThreshold"] = pp.BloomThreshold.ToString(CultureInfo.InvariantCulture);
                    data.Fields["BloomScatter"] = pp.BloomScatter.ToString(CultureInfo.InvariantCulture);
                    data.Fields["BloomClamp"] = pp.BloomClamp.ToString(CultureInfo.InvariantCulture);
                    data.Fields["BloomTintR"] = pp.BloomTintR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["BloomTintG"] = pp.BloomTintG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["BloomTintB"] = pp.BloomTintB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["BloomHighQualityFiltering"] = pp.BloomHighQualityFiltering.ToString();
                    data.Fields["ColorAdjustments"] = pp.ColorAdjustments.ToString();
                    data.Fields["PostExposure"] = pp.PostExposure.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Contrast"] = pp.Contrast.ToString(CultureInfo.InvariantCulture);
                    data.Fields["HueShift"] = pp.HueShift.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Saturation"] = pp.Saturation.ToString(CultureInfo.InvariantCulture);
                    data.Fields["ColorFilterR"] = pp.ColorFilterR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["ColorFilterG"] = pp.ColorFilterG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["ColorFilterB"] = pp.ColorFilterB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["LiftR"] = pp.LiftR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["LiftG"] = pp.LiftG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["LiftB"] = pp.LiftB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["GammaR"] = pp.GammaR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["GammaG"] = pp.GammaG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["GammaB"] = pp.GammaB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["GainR"] = pp.GainR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["GainG"] = pp.GainG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["GainB"] = pp.GainB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Vignette"] = pp.Vignette.ToString();
                    data.Fields["VignetteColorR"] = pp.VignetteColorR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VignetteColorG"] = pp.VignetteColorG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VignetteColorB"] = pp.VignetteColorB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VignetteCenterX"] = pp.VignetteCenterX.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VignetteCenterY"] = pp.VignetteCenterY.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VignetteIntensity"] = pp.VignetteIntensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VignetteSmoothness"] = pp.VignetteSmoothness.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VignetteRounded"] = pp.VignetteRounded.ToString();
                    data.Fields["ChromaticAberration"] = pp.ChromaticAberration.ToString();
                    data.Fields["ChromaticAberrationIntensity"] = pp.ChromaticAberrationIntensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["AmbientOcclusion"] = pp.AmbientOcclusion.ToString();
                    data.Fields["AmbientOcclusionStrength"] = pp.AmbientOcclusionStrength.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Fog"] = pp.Fog.ToString();
                    data.Fields["FogDensity"] = pp.FogDensity.ToString(CultureInfo.InvariantCulture);
                    data.Fields["FogR"] = pp.FogR.ToString(CultureInfo.InvariantCulture);
                    data.Fields["FogG"] = pp.FogG.ToString(CultureInfo.InvariantCulture);
                    data.Fields["FogB"] = pp.FogB.ToString(CultureInfo.InvariantCulture);
                    data.Fields["VolumetricLightStrength"] = pp.VolumetricLightStrength.ToString(CultureInfo.InvariantCulture);
                    return data;
                case Terrain terrain:
                    data.Fields["Resolution"] = terrain.Resolution.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SizeX"] = terrain.SizeX.ToString(CultureInfo.InvariantCulture);
                    data.Fields["SizeZ"] = terrain.SizeZ.ToString(CultureInfo.InvariantCulture);
                    data.Fields["HeightScale"] = terrain.HeightScale.ToString(CultureInfo.InvariantCulture);
                    data.Fields["Heightmap"] = JsonSerializer.Serialize(terrain.Heightmap);
                    data.Fields["LayerTextures"] = JsonSerializer.Serialize(
                        terrain.LayerTextures.Select(p => SerializeAssetPath(p, baseAssetsPath)).ToArray());
                    data.Fields["LayerTiling"] = JsonSerializer.Serialize(terrain.LayerTiling);
                    data.Fields["SplatMap"] = Convert.ToBase64String(terrain.SplatMap);
                    return data;
                case Animator animator:
                    data.Fields["ControllerPath"] = SerializeAssetPath(animator.ControllerPath, baseAssetsPath);
                    data.Fields["AvatarPath"] = SerializeAssetPath(animator.AvatarPath, baseAssetsPath);
                    data.Fields["ModelPath"] = SerializeAssetPath(animator.ModelPath, baseAssetsPath);
                    data.Fields["ClipPath"] = SerializeAssetPath(animator.ClipPath, baseAssetsPath);
                    data.Fields["AnimationSources"] = JsonSerializer.Serialize(
                        animator.AnimationSources.Select(p => SerializeAssetPath(p, baseAssetsPath)).ToArray());
                    data.Fields["CurrentClipName"] = animator.CurrentClipName ?? "";
                    data.Fields["Loop"] = animator.Loop.ToString();
                    data.Fields["PlayOnAwake"] = animator.PlayOnAwake.ToString();
                    data.Fields["Speed"] = animator.Speed.ToString(CultureInfo.InvariantCulture);
                    data.Fields["ApplyRootMotion"] = animator.ApplyRootMotion.ToString();
                    data.Fields["UpdateMode"] = ((int)animator.UpdateMode).ToString(CultureInfo.InvariantCulture);
                    data.Fields["CullingMode"] = ((int)animator.CullingMode).ToString(CultureInfo.InvariantCulture);
                    return data;
                case MeshRenderer mr:
                    foreach (var field in mr.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var v = field.GetValue(mr);
                        if (v != null)
                            data.Fields[field.Name] = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                    }
                    return data;
                case MeshFilter mf:
                    data.Fields["MeshPath"] = SerializeAssetPath(mf.MeshPath, baseAssetsPath);
                    data.Fields["ImportScale"] = mf.ImportScale.ToString(CultureInfo.InvariantCulture);
                    if (mf.SubmeshIndex >= 0)
                        data.Fields["SubmeshIndex"] = mf.SubmeshIndex.ToString(CultureInfo.InvariantCulture);
                    if (mf.MaterialSlots.Count > 0)
                        data.Fields["MaterialSlots"] = JsonSerializer.Serialize(
                            mf.MaterialSlots.Select(p => SerializeAssetPath(p, baseAssetsPath)).ToArray());
                    return data;
                case ParticleSystem:
                case Canvas:
                case UIElement:
                case UILayoutElement:
                case UILayoutGroup:
                case UIContentSizeFitter:
                case UIAspectRatioFitter:
                    // Serialización genérica por reflexión (campos públicos simples).
                    // Cubre ParticleSystem y todo el sistema UI propio de GrokoEngine.
                    foreach (var field in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (field.IsDefined(typeof(NonSerializedAttribute), inherit: false) || !IsSimple(field.FieldType)) continue;
                        var value = field.GetValue(comp);
                        if (value == null) continue;
                        string text = field.FieldType == typeof(string) && IsPathFieldName(field.Name)
                            ? SerializeAssetPath((string)value, baseAssetsPath)
                            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                        data.Fields[field.Name] = text;
                    }
                    if (comp is ParticleSystem ps && ps.ExtraBursts.Count > 0)
                        data.Fields["ExtraBursts"] = JsonSerializer.Serialize(ps.ExtraBursts, JsonOptions);
                    return data;
                case MonoBehaviour:
                    foreach (var field in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var value = field.GetValue(comp);
                        if (value == null) continue;
                        if (field.FieldType == typeof(MiMotor.Mathematics.Vector3))
                        {
                            var v = (MiMotor.Mathematics.Vector3)value;
                            data.Fields[field.Name + "X"] = v.X.ToString(CultureInfo.InvariantCulture);
                            data.Fields[field.Name + "Y"] = v.Y.ToString(CultureInfo.InvariantCulture);
                            data.Fields[field.Name + "Z"] = v.Z.ToString(CultureInfo.InvariantCulture);
                        }
                        else if (IsSimple(field.FieldType))
                        {
                            data.Fields[field.Name] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                        }
                        else if (value is ScriptableObject so)
                        {
                            if (!string.IsNullOrWhiteSpace(so.AssetPath))
                                data.Fields[field.Name] = AssetRefPrefix + SerializeAssetPath(so.AssetPath, baseAssetsPath);
                        }
                        else if (IsReferenceType(field.FieldType))
                        {
                            // Instancia viva de la escena -> @@ref:<EditorId>.
                            // Plantilla de prefab (no está en la escena) -> @@prefab:<ruta>.
                            if (IsInSceneReference(value))
                            {
                                string? ownerId = ResolveOwnerId(value);
                                if (!string.IsNullOrEmpty(ownerId))
                                    data.Fields[field.Name] = RefPrefix + ownerId;
                            }
                            else
                            {
                                var prefabGo = ReferencedGameObject(value);
                                if (prefabGo != null && !string.IsNullOrWhiteSpace(prefabGo.PrefabAssetPath))
                                    data.Fields[field.Name] = PrefabRefPrefix + SerializeAssetPath(prefabGo.PrefabAssetPath, baseAssetsPath);
                            }
                        }
                    }
                    return data;
                default:
                    return null;
            }
        }

        private static GameObject FromData(GameObjectData data, GameObject? parent, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler, string? baseAssetsPath)
        {
            var obj = new GameObject
            {
                EditorId = string.IsNullOrWhiteSpace(data.Id) ? Guid.NewGuid().ToString("N") : data.Id,
                Name = data.Name,
                Type = data.Type,
                IsActive = data.IsActive,
                IsCamera = data.IsCamera,
                IsStatic = data.IsStatic,
                Layer = data.Layer,
                PrefabAssetPath = ResolveAssetPath(data.PrefabAssetPath, baseAssetsPath),
                Parent = parent
            };
            obj.PosX = data.Position.X; obj.PosY = data.Position.Y; obj.PosZ = data.Position.Z;
            obj.RotX = data.Rotation.X; obj.RotY = data.Rotation.Y; obj.RotZ = data.Rotation.Z;
            obj.ScaleX = data.Scale.X; obj.ScaleY = data.Scale.Y; obj.ScaleZ = data.Scale.Z;
            // Restaura la rotación exacta por cuaternión (huesos), si se guardó.
            if (data.UseQuat && data.Quat is { Length: 4 })
            {
                obj.transform.Rotation = new MiMotor.Mathematics.Quaternion(data.Quat[0], data.Quat[1], data.Quat[2], data.Quat[3]);
                obj.UseQuaternionRotation = true;
                obj.MarkTransformDirtyRecursive();
            }

            foreach (var component in data.Components)
                AddComponent(obj, component, physicsEngine, scriptCompiler, baseAssetsPath);

            foreach (var childData in data.Children)
                FromData(childData, obj, physicsEngine, scriptCompiler, baseAssetsPath);

            return obj;
        }

        private static void AddComponent(GameObject obj, ComponentData data, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler, string? baseAssetsPath)
        {
            string componentName = GetShortTypeName(data.TypeName);
            Component? comp = componentName switch
            {
                nameof(Material) => obj.AddComponent<Material>(),
                nameof(Rigidbody) => obj.AddComponentWithEngine<Rigidbody>(physicsEngine),
                nameof(BoxCollider) => obj.AddComponentWithEngine<BoxCollider>(physicsEngine),
                nameof(SphereCollider) => obj.AddComponentWithEngine<SphereCollider>(physicsEngine),
                nameof(CapsuleCollider) => obj.AddComponentWithEngine<CapsuleCollider>(physicsEngine),
                nameof(MeshCollider) => obj.AddComponentWithEngine<MeshCollider>(physicsEngine),
                nameof(CharacterController) => obj.AddComponentWithEngine<CharacterController>(physicsEngine),
                nameof(Camera) => obj.AddComponent<Camera>(),
                nameof(DirectionalLight) => obj.AddComponent<DirectionalLight>(),
                nameof(PointLight) => obj.AddComponent<PointLight>(),
                nameof(SpotLight) => obj.AddComponent<SpotLight>(),
                nameof(AmbientLight) => obj.AddComponent<AmbientLight>(),
                nameof(AreaLight) => obj.AddComponent<AreaLight>(),
                nameof(RectangleLight) => obj.AddComponent<RectangleLight>(),
                nameof(PostProcessSettings) => obj.AddComponent<PostProcessSettings>(),
                nameof(MeshFilter) => obj.AddComponent<MeshFilter>(),
                nameof(MeshRenderer) => obj.AddComponent<MeshRenderer>(),
                nameof(Terrain) => obj.AddComponent<Terrain>(),
                nameof(TerrainCollider) => obj.AddComponentWithEngine<TerrainCollider>(physicsEngine),
                nameof(ParticleSystem) => obj.AddComponent<ParticleSystem>(),
                nameof(Canvas) => obj.AddComponent<Canvas>(),
                nameof(UIPanel) => obj.AddComponent<UIPanel>(),
                nameof(UIImage) => obj.AddComponent<UIImage>(),
                nameof(UIRawImage) => obj.AddComponent<UIRawImage>(),
                nameof(UIText) => obj.AddComponent<UIText>(),
                nameof(UIButton) => obj.AddComponent<UIButton>(),
                nameof(UIToggle) => obj.AddComponent<UIToggle>(),
                nameof(UISlider) => obj.AddComponent<UISlider>(),
                nameof(UIScrollbar) => obj.AddComponent<UIScrollbar>(),
                nameof(UIDropdown) => obj.AddComponent<UIDropdown>(),
                nameof(UIInputField) => obj.AddComponent<UIInputField>(),
                nameof(UIScrollView) => obj.AddComponent<UIScrollView>(),
                nameof(UIMask) => obj.AddComponent<UIMask>(),
                nameof(UIRectMask2D) => obj.AddComponent<UIRectMask2D>(),
                nameof(UIHorizontalLayoutGroup) => obj.AddComponent<UIHorizontalLayoutGroup>(),
                nameof(UIVerticalLayoutGroup) => obj.AddComponent<UIVerticalLayoutGroup>(),
                nameof(UIGridLayoutGroup) => obj.AddComponent<UIGridLayoutGroup>(),
                nameof(UILayoutElement) => obj.AddComponent<UILayoutElement>(),
                nameof(UIContentSizeFitter) => obj.AddComponent<UIContentSizeFitter>(),
                nameof(UIAspectRatioFitter) => obj.AddComponent<UIAspectRatioFitter>(),
                nameof(UIBar) => obj.AddComponent<UIBar>(),
                nameof(Animator) => obj.AddComponent<Animator>(),
                // Escenas antiguas: el SkeletalAnimator (legacy, ya eliminado) se carga como
                // Animator; sus campos (ModelPath/AnimationSources/CurrentClipName/Speed/Loop/
                // PlayOnAwake) coinciden y los aplica el bloque de Animator en ApplyFields.
                "SkeletalAnimator" => obj.AddComponent<Animator>(),
                _ => CreateScriptComponent(obj, data.TypeName, physicsEngine, scriptCompiler)
            };

            if (comp == null) return;
            ApplyFields(comp, data.Fields, physicsEngine, scriptCompiler, baseAssetsPath);
        }

        private static Component? CreateScriptComponent(GameObject obj, string typeName, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler)
        {
            var type = scriptCompiler.FindTypeByName(typeName);
            if (type == null || !type.IsSubclassOf(typeof(MonoBehaviour))) return null;
            return obj.AddComponent(type, physicsEngine);
        }

        private static string GetSerializableTypeName(Type type)
        {
            return type.Assembly == typeof(Component).Assembly
                ? type.Name
                : type.FullName ?? type.Name;
        }

        private static string GetShortTypeName(string typeName)
        {
            int lastDot = typeName.LastIndexOf('.');
            return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
        }

        private static void ApplyFields(Component comp, Dictionary<string, string> fields, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler, string? baseAssetsPath)
        {
            if (comp is Terrain terrain)
            {
                terrain.Resolution = GetInt(fields, "Resolution", terrain.Resolution);
                terrain.SizeX = GetFloat(fields, "SizeX", terrain.SizeX);
                terrain.SizeZ = GetFloat(fields, "SizeZ", terrain.SizeZ);
                terrain.HeightScale = GetFloat(fields, "HeightScale", terrain.HeightScale);
                if (fields.TryGetValue("Heightmap", out var heightmapJson) && !string.IsNullOrWhiteSpace(heightmapJson))
                {
                    var heightmap = JsonSerializer.Deserialize<float[]>(heightmapJson);
                    if (heightmap != null && heightmap.Length == terrain.Heightmap.Length)
                        terrain.Heightmap = heightmap;
                }
                if (fields.TryGetValue("LayerTextures", out var layerTexturesJson) && !string.IsNullOrWhiteSpace(layerTexturesJson))
                {
                    var layers = JsonSerializer.Deserialize<string[]>(layerTexturesJson);
                    if (layers != null && layers.Length == terrain.LayerTextures.Length)
                        terrain.LayerTextures = layers.Select(p => ResolveAssetPath(p, baseAssetsPath) ?? "").ToArray();
                }
                if (fields.TryGetValue("LayerTiling", out var layerTilingJson) && !string.IsNullOrWhiteSpace(layerTilingJson))
                {
                    var tiling = JsonSerializer.Deserialize<float[]>(layerTilingJson);
                    if (tiling != null && tiling.Length == terrain.LayerTiling.Length)
                        terrain.LayerTiling = tiling;
                }
                if (fields.TryGetValue("SplatMap", out var splatMapBase64) && !string.IsNullOrWhiteSpace(splatMapBase64))
                {
                    var splatMap = Convert.FromBase64String(splatMapBase64);
                    if (splatMap.Length == terrain.Resolution * terrain.Resolution * 4)
                        terrain.SplatMap = splatMap;
                }
                terrain.EnsureSplatMapSize();
                terrain.Version++;
                terrain.SplatVersion++;
            }

            if (comp is BoxCollider bc)
            {
                bc.Size = new MiMotor.Mathematics.Vector3(GetFloat(fields, "SizeX", bc.Size.X), GetFloat(fields, "SizeY", bc.Size.Y), GetFloat(fields, "SizeZ", bc.Size.Z));
                bc.Center = new MiMotor.Mathematics.Vector3(GetFloat(fields, "CenterX", bc.Center.X), GetFloat(fields, "CenterY", bc.Center.Y), GetFloat(fields, "CenterZ", bc.Center.Z));
                bc.IsTrigger = GetBool(fields, "IsTrigger", bc.IsTrigger);
                bc.PhysicMaterial = fields.TryGetValue("PhysicMaterial", out var physicMaterial) && !string.IsNullOrWhiteSpace(physicMaterial)
                    ? physicMaterial
                    : bc.PhysicMaterial;
                bc.Friction = GetFloat(fields, "Friction", bc.Friction);
                bc.Bounciness = GetFloat(fields, "Bounciness", bc.Bounciness);
            }

            if (comp is SphereCollider sc)
            {
                sc.Radius = GetFloat(fields, "Radius", sc.Radius);
                sc.Center = new MiMotor.Mathematics.Vector3(GetFloat(fields, "CenterX", sc.Center.X), GetFloat(fields, "CenterY", sc.Center.Y), GetFloat(fields, "CenterZ", sc.Center.Z));
                sc.IsTrigger = GetBool(fields, "IsTrigger", sc.IsTrigger);
                sc.PhysicMaterial = fields.TryGetValue("PhysicMaterial", out var physicMaterial) && !string.IsNullOrWhiteSpace(physicMaterial) ? physicMaterial : sc.PhysicMaterial;
                sc.Friction = GetFloat(fields, "Friction", sc.Friction);
                sc.Bounciness = GetFloat(fields, "Bounciness", sc.Bounciness);
            }

            if (comp is CapsuleCollider cap)
            {
                cap.Radius = GetFloat(fields, "Radius", cap.Radius);
                cap.Height = GetFloat(fields, "Height", cap.Height);
                cap.Axis = (CapsuleAxis)GetInt(fields, "Axis", (int)cap.Axis);
                cap.Center = new MiMotor.Mathematics.Vector3(GetFloat(fields, "CenterX", cap.Center.X), GetFloat(fields, "CenterY", cap.Center.Y), GetFloat(fields, "CenterZ", cap.Center.Z));
                cap.IsTrigger = GetBool(fields, "IsTrigger", cap.IsTrigger);
                cap.PhysicMaterial = fields.TryGetValue("PhysicMaterial", out var physicMaterial) && !string.IsNullOrWhiteSpace(physicMaterial) ? physicMaterial : cap.PhysicMaterial;
                cap.Friction = GetFloat(fields, "Friction", cap.Friction);
                cap.Bounciness = GetFloat(fields, "Bounciness", cap.Bounciness);
            }

            if (comp is MeshCollider mc)
            {
                mc.Size = new MiMotor.Mathematics.Vector3(GetFloat(fields, "SizeX", mc.Size.X), GetFloat(fields, "SizeY", mc.Size.Y), GetFloat(fields, "SizeZ", mc.Size.Z));
                mc.Center = new MiMotor.Mathematics.Vector3(GetFloat(fields, "CenterX", mc.Center.X), GetFloat(fields, "CenterY", mc.Center.Y), GetFloat(fields, "CenterZ", mc.Center.Z));
                mc.UseMeshBounds = GetBool(fields, "UseMeshBounds", mc.UseMeshBounds);
                mc.IsTrigger = GetBool(fields, "IsTrigger", mc.IsTrigger);
                mc.PhysicMaterial = fields.TryGetValue("PhysicMaterial", out var physicMaterial) && !string.IsNullOrWhiteSpace(physicMaterial) ? physicMaterial : mc.PhysicMaterial;
                mc.Friction = GetFloat(fields, "Friction", mc.Friction);
                mc.Bounciness = GetFloat(fields, "Bounciness", mc.Bounciness);
            }

            if (comp is CharacterController cc)
            {
                cc.Height = GetFloat(fields, "Height", cc.Height);
                cc.Radius = GetFloat(fields, "Radius", cc.Radius);
                cc.Center = new MiMotor.Mathematics.Vector3(
                    GetFloat(fields, "CenterX", cc.Center.X),
                    GetFloat(fields, "CenterY", cc.Center.Y),
                    GetFloat(fields, "CenterZ", cc.Center.Z));
                cc.AutoCenter = GetBool(fields, "AutoCenter", cc.AutoCenter);
                cc.SkinWidth = GetFloat(fields, "SkinWidth", cc.SkinWidth);
                cc.StepOffset = GetFloat(fields, "StepOffset", cc.StepOffset);
                cc.SlopeLimit = GetFloat(fields, "SlopeLimit", cc.SlopeLimit);
                cc.UseGravity = GetBool(fields, "UseGravity", cc.UseGravity);
                cc.Gravity = GetFloat(fields, "Gravity", cc.Gravity);
                cc.JumpSpeed = GetFloat(fields, "JumpSpeed", cc.JumpSpeed);
                cc.MaxFallSpeed = GetFloat(fields, "MaxFallSpeed", cc.MaxFallSpeed);
                cc.PushPower = GetFloat(fields, "PushPower", cc.PushPower);
            }

            if (comp is PostProcessSettings pp)
            {
                // Compatibilidad: escenas anteriores guardaban el interruptor del efecto como "Enabled".
                pp.PostProcessEnabled = GetBool(fields, "PostProcessEnabled", GetBool(fields, "Enabled", pp.PostProcessEnabled));
            }

            if (comp is Animator animator)
            {
                animator.ControllerPath = ResolveAssetPath(fields.TryGetValue("ControllerPath", out var ctrlPath) ? ctrlPath : "", baseAssetsPath) ?? "";
                animator.AvatarPath = ResolveAssetPath(fields.TryGetValue("AvatarPath", out var avatarPath) ? avatarPath : "", baseAssetsPath) ?? "";
                animator.ModelPath = ResolveAssetPath(fields.TryGetValue("ModelPath", out var modelPath) ? modelPath : "", baseAssetsPath) ?? "";
                animator.ClipPath = ResolveAssetPath(fields.TryGetValue("ClipPath", out var clipPath) ? clipPath : "", baseAssetsPath) ?? "";
                if (fields.TryGetValue("AnimationSources", out var animSourcesRaw) && !string.IsNullOrWhiteSpace(animSourcesRaw))
                {
                    try
                    {
                        var arr = JsonSerializer.Deserialize<string[]>(animSourcesRaw);
                        if (arr != null)
                            animator.AnimationSources = arr.Select(p => ResolveAssetPath(p, baseAssetsPath) ?? "").Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                    }
                    catch { }
                }
                animator.CurrentClipName = fields.TryGetValue("CurrentClipName", out var currentClip) ? currentClip : "";
                animator.Loop = GetBool(fields, "Loop", animator.Loop);
                animator.PlayOnAwake = GetBool(fields, "PlayOnAwake", animator.PlayOnAwake);
                animator.Speed = GetFloat(fields, "Speed", animator.Speed);
                animator.ApplyRootMotion = GetBool(fields, "ApplyRootMotion", animator.ApplyRootMotion);
                animator.UpdateMode = (AnimatorUpdateMode)GetInt(fields, "UpdateMode", (int)animator.UpdateMode);
                // Compat: el antiguo bool AnimatePhysics ahora es un valor de UpdateMode.
                if (GetBool(fields, "AnimatePhysics", false))
                    animator.UpdateMode = AnimatorUpdateMode.AnimatePhysics;
                animator.CullingMode = (AnimatorCullingMode)GetInt(fields, "CullingMode", (int)animator.CullingMode);
            }

            if (comp is DirectionalLight dl)
            {
                dl.Direction = new MiMotor.Mathematics.Vector3(
                    GetFloat(fields, "DirectionX", dl.Direction.X),
                    GetFloat(fields, "DirectionY", dl.Direction.Y),
                    GetFloat(fields, "DirectionZ", dl.Direction.Z));
            }

            if (comp is Material mat && fields.TryGetValue("ShaderGraphProperties", out var sgPropsRaw))
            {
                try
                {
                    var props = JsonSerializer.Deserialize<Dictionary<string, float[]>>(sgPropsRaw);
                    if (props != null)
                        mat.ShaderGraphProperties = new Dictionary<string, float[]>(props, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    GrokoEngine.Debug.LogWarning($"No se pudo restaurar ShaderGraphProperties: {ex.Message}");
                }
            }

            if (comp is Material matTex && fields.TryGetValue("ShaderGraphTextures", out var sgTexRaw))
            {
                try
                {
                    var texs = JsonSerializer.Deserialize<Dictionary<string, string>>(sgTexRaw);
                    if (texs != null)
                        matTex.ShaderGraphTextures = new Dictionary<string, string>(
                            texs.ToDictionary(kv => kv.Key, kv => ResolveAssetPath(kv.Value, baseAssetsPath) ?? string.Empty),
                            StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    GrokoEngine.Debug.LogWarning($"No se pudo restaurar ShaderGraphTextures: {ex.Message}");
                }
            }

            if (comp is MeshFilter mf && fields.TryGetValue("MaterialSlots", out var slotsRaw))
            {
                try
                {
                    var slots = JsonSerializer.Deserialize<string[]>(slotsRaw);
                    if (slots != null)
                        mf.MaterialSlots = slots.Select(p => ResolveAssetPath(p, baseAssetsPath) ?? "").ToList();
                }
                catch (Exception ex)
                {
                    GrokoEngine.Debug.LogWarning($"No se pudo restaurar MaterialSlots: {ex.Message}");
                }
            }

            // Manejo especial ParticleSystem: Shape es enum, restaurar sin play
            if (comp is ParticleSystem ps && fields.TryGetValue("Shape", out var shapeRaw)
                && int.TryParse(shapeRaw, out int shapeInt))
                ps.Shape = (ParticleShape)shapeInt;

            // CollisionQuality es enum (Fast/High): Enum.TryParse acepta nombre o numero.
            if (comp is ParticleSystem psq && fields.TryGetValue("CollisionQuality", out var cqRaw)
                && Enum.TryParse<ParticleCollisionQuality>(cqRaw, out var cq))
            {
                psq.CollisionQuality = cq;
                psq.Collision.Quality = cq;
            }

            if (comp is ParticleSystem psBursts && fields.TryGetValue("ExtraBursts", out var burstsRaw))
            {
                try
                {
                    psBursts.ExtraBursts = JsonSerializer.Deserialize<List<BurstEvent>>(burstsRaw, JsonOptions) ?? new List<BurstEvent>();
                }
                catch (Exception ex)
                {
                    GrokoEngine.Debug.LogWarning($"No se pudo restaurar ExtraBursts: {ex.Message}");
                }
            }

            // Campos Vector3 de scripts (MonoBehaviour): reconstruir desde {Name}X/Y/Z
            if (comp is MonoBehaviour)
            {
                foreach (var field in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType != typeof(MiMotor.Mathematics.Vector3) || field.IsInitOnly) continue;
                    string vx = field.Name + "X", vy = field.Name + "Y", vz = field.Name + "Z";
                    if (!fields.ContainsKey(vx) && !fields.ContainsKey(vy) && !fields.ContainsKey(vz)) continue;
                    var cur = (MiMotor.Mathematics.Vector3)field.GetValue(comp)!;
                    field.SetValue(comp, new MiMotor.Mathematics.Vector3(
                        GetFloat(fields, vx, cur.X), GetFloat(fields, vy, cur.Y), GetFloat(fields, vz, cur.Z)));
                }
            }

            foreach (var field in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!fields.TryGetValue(field.Name, out var raw)) continue;
                if (raw.StartsWith(RefPrefix, StringComparison.Ordinal))
                {
                    // Referencia a otro objeto: se resuelve en el 2º pase (ResolveRefs).
                    _pendingRefs?.Add((comp, field, raw.Substring(RefPrefix.Length)));
                    continue;
                }
                if (raw.StartsWith(PrefabRefPrefix, StringComparison.Ordinal))
                {
                    // Referencia a una plantilla de prefab: se recarga el .prefab.
                    string? prefabPath = ResolveAssetPath(raw.Substring(PrefabRefPrefix.Length), baseAssetsPath);
                    if (!string.IsNullOrWhiteSpace(prefabPath) && File.Exists(prefabPath))
                    {
                        // LoadPrefab reinicia _pendingRefs; lo preservamos para no
                        // perder las referencias pendientes de la escena en curso.
                        var savedPending = _pendingRefs;
                        try
                        {
                            var loadedPrefab = LoadPrefab(prefabPath, physicsEngine, scriptCompiler);
                            loadedPrefab.PrefabAssetPath = prefabPath;
                            object? val = null;
                            if (typeof(GameObject).IsAssignableFrom(field.FieldType)) val = loadedPrefab;
                            else if (field.FieldType == typeof(MiMotor.Mathematics.Transform)) val = loadedPrefab.transform;
                            else if (typeof(Component).IsAssignableFrom(field.FieldType))
                                val = loadedPrefab.Components.FirstOrDefault(c => field.FieldType.IsInstanceOfType(c));
                            if (val != null) field.SetValue(comp, val);
                        }
                        catch (Exception ex)
                        {
                            GrokoEngine.Debug.LogWarning($"No se pudo cargar el prefab para '{field.Name}': {ex.Message}");
                        }
                        finally { _pendingRefs = savedPending; }
                    }
                    continue;
                }
                if (raw.StartsWith(AssetRefPrefix, StringComparison.Ordinal))
                {
                    if (typeof(ScriptableObject).IsAssignableFrom(field.FieldType))
                    {
                        string? assetPath = ResolveAssetPath(raw.Substring(AssetRefPrefix.Length), baseAssetsPath);
                        if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
                        {
                            var loadedAsset = ScriptableObjectAsset.Load(assetPath, physicsEngine, scriptCompiler);
                            if (loadedAsset != null && field.FieldType.IsInstanceOfType(loadedAsset))
                                field.SetValue(comp, loadedAsset);
                        }
                    }
                    continue;
                }
                raw = ResolveFieldValue(field.Name, field.FieldType, raw, baseAssetsPath);
                if (!TryConvert(raw, field.FieldType, out var value)) continue;
                field.SetValue(comp, value);
            }

            foreach (var prop in comp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
            {
                if (!fields.TryGetValue(prop.Name, out var raw)) continue;
                raw = ResolveFieldValue(prop.Name, prop.PropertyType, raw, baseAssetsPath);
                if (!TryConvert(raw, prop.PropertyType, out var value)) continue;
                prop.SetValue(comp, value);
            }
        }

        private static string ResolveFieldValue(string name, Type type, string raw, string? baseAssetsPath)
        {
            return type == typeof(string) && IsPathFieldName(name)
                ? ResolveAssetPath(raw, baseAssetsPath) ?? ""
                : raw;
        }

        private static bool IsPathFieldName(string name) =>
            name.EndsWith("Path", StringComparison.OrdinalIgnoreCase);

        private static void SerializeFloat(ComponentData data, string key, float value)
            => data.Fields[key] = value.ToString(CultureInfo.InvariantCulture);

        internal static string SerializeAssetPath(string? path, string? baseAssetsPath)
            => AssetDatabase.SerializeReference(path, baseAssetsPath);

        internal static string? ResolveAssetPath(string? path, string? baseAssetsPath)
            => AssetDatabase.ResolveReference(path, baseAssetsPath);

        internal static string? InferAssetsRoot(string filePath)
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "");
            while (dir != null)
            {
                if (string.Equals(dir.Name, "Assets", StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }

        private static string NormalizeFullPath(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string NormalizeAssetPath(string path) =>
            path.Replace('\\', '/');

        private static void EnsureMetaIfInsideAssets(string path, string? assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(assetsRoot) || !File.Exists(path))
                return;

            AssetDatabase.Get(assetsRoot).GetOrCreateGuid(path);
        }

        private static float GetFloat(Dictionary<string, string> fields, string name, float fallback)
        {
            return fields.TryGetValue(name, out var raw) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static bool GetBool(Dictionary<string, string> fields, string name, bool fallback)
        {
            return fields.TryGetValue(name, out var raw) && bool.TryParse(raw, out var value)
                ? value
                : fallback;
        }

        private static int GetInt(Dictionary<string, string> fields, string name, int fallback)
        {
            return fields.TryGetValue(name, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static bool TryConvert(string raw, Type type, out object? value)
        {
            value = null;
            try
            {
                if (type == typeof(string)) { value = raw; return true; }
                if (type == typeof(float)) { value = float.Parse(raw, CultureInfo.InvariantCulture); return true; }
                if (type == typeof(double)) { value = double.Parse(raw, CultureInfo.InvariantCulture); return true; }
                if (type == typeof(int)) { value = int.Parse(raw, CultureInfo.InvariantCulture); return true; }
                if (type == typeof(bool)) { value = bool.Parse(raw); return true; }
                if (type.IsEnum)
                {
                    value = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int enumInt)
                        ? Enum.ToObject(type, enumInt)
                        : Enum.Parse(type, raw, ignoreCase: true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                GrokoEngine.Debug.LogWarning($"No se pudo convertir el valor '{raw}' a {type.Name}: {ex.Message}");
            }
            return false;
        }

        private static bool IsSimple(Type type)
        {
            return type == typeof(string) || type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(bool) || type.IsEnum;
        }

        // ── Referencias entre objetos de la escena (campos GameObject/Transform/Component) ──

        private static bool IsReferenceType(Type t) =>
            typeof(GameObject).IsAssignableFrom(t)
            || t == typeof(MiMotor.Mathematics.Transform)
            || typeof(Component).IsAssignableFrom(t);

        private static string? ResolveOwnerId(object value) => value switch
        {
            GameObject go => go.EditorId,
            Component c => c.gameObject?.EditorId,
            MiMotor.Mathematics.Transform tr =>
                _serializeOwnerIndex != null && _serializeOwnerIndex.TryGetValue(tr, out var id) ? id : null,
            _ => null
        };

        // ¿La referencia apunta a un objeto que está REALMENTE en la escena que se está
        // guardando? (Una plantilla de prefab cargada con LoadPrefab no lo está.)
        private static bool IsInSceneReference(object value)
        {
            if (_serializeOwnerIndex == null) return false;
            return value switch
            {
                GameObject go => _serializeOwnerIndex.ContainsKey(go),
                MiMotor.Mathematics.Transform tr => _serializeOwnerIndex.ContainsKey(tr),
                Component c => c.gameObject != null && _serializeOwnerIndex.ContainsKey(c.gameObject),
                _ => false
            };
        }

        // GameObject detrás de una referencia (para leer su PrefabAssetPath).
        private static GameObject? ReferencedGameObject(object value) => value switch
        {
            GameObject go => go,
            Component c => c.gameObject,
            _ => null
        };

        private static Dictionary<object, string> BuildOwnerIndex(IReadOnlyList<GameObject> roots)
        {
            var index = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            void Add(GameObject o)
            {
                index[o] = o.EditorId;
                if (o.transform != null) index[o.transform] = o.EditorId;
                foreach (var c in o.Children) Add(c);
            }
            foreach (var r in roots) Add(r);
            return index;
        }

        private static void ResolveRefs(List<GameObject> roots)
        {
            if (_pendingRefs == null || _pendingRefs.Count == 0) return;

            var index = new Dictionary<string, GameObject>();
            void Index(GameObject o)
            {
                index[o.EditorId] = o;
                foreach (var c in o.Children) Index(c);
            }
            foreach (var r in roots) Index(r);

            foreach (var (comp, field, targetId) in _pendingRefs)
            {
                if (!index.TryGetValue(targetId, out var target)) continue;
                Type ft = field.FieldType;
                object? val = null;
                if (typeof(GameObject).IsAssignableFrom(ft)) val = target;
                else if (ft == typeof(MiMotor.Mathematics.Transform)) val = target.transform;
                else if (typeof(Component).IsAssignableFrom(ft))
                    val = target.Components.FirstOrDefault(c => ft.IsInstanceOfType(c));
                if (val != null)
                {
                    try { field.SetValue(comp, val); }
                    catch (Exception ex) { GrokoEngine.Debug.LogWarning($"No se pudo asignar la referencia '{field.Name}' en {comp.GetType().Name}: {ex.Message}"); }
                }
            }
        }
    }

    public class SceneData
    {
        public int Version { get; set; } = 1;
        public List<GameObjectData> Objects { get; set; } = new List<GameObjectData>();

        public void Normalize()
        {
            Objects ??= new List<GameObjectData>();
            foreach (var obj in Objects)
                obj.Normalize();
        }
    }

    public class PrefabData
    {
        public int Version { get; set; } = 1;
        public GameObjectData? Root { get; set; }
    }

    public class GameObjectData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Type { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsCamera  { get; set; }
        public bool IsStatic  { get; set; }
        public int Layer { get; set; } = LayerMask.Default;
        public string? PrefabAssetPath { get; set; }
        public VectorData Position { get; set; } = new VectorData();
        public VectorData Rotation { get; set; } = new VectorData();
        public VectorData Scale { get; set; } = VectorData.From(1, 1, 1);
        // Rotación exacta por cuaternión (x,y,z,w) para huesos animados; null = usar Euler.
        public float[]? Quat { get; set; }
        public bool UseQuat { get; set; }
        public List<ComponentData> Components { get; set; } = new List<ComponentData>();
        public List<GameObjectData> Children { get; set; } = new List<GameObjectData>();

        public void Normalize()
        {
            Id ??= "";
            Name ??= "";
            Layer = Math.Clamp(Layer, 0, 31);
            Position ??= new VectorData();
            Rotation ??= new VectorData();
            Scale ??= VectorData.From(1, 1, 1);
            Components ??= new List<ComponentData>();
            Children ??= new List<GameObjectData>();

            foreach (var component in Components)
                component.Normalize();

            foreach (var child in Children)
                child.Normalize();
        }
    }

    public class ComponentData
    {
        public string TypeName { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

        public void Normalize()
        {
            TypeName ??= "";
            Fields ??= new Dictionary<string, string>();
        }
    }

    public class VectorData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static VectorData From(float x, float y, float z) => new VectorData { X = x, Y = y, Z = z };
    }
}
