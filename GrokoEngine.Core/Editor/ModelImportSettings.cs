using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace GrokoEngine
{
    // Ajustes de importación de un modelo (FBX/OBJ), estilo Unity. Se guardan en un
    // archivo "{modelo}.import" (JSON) junto al modelo.
    public class ModelImportSettings
    {
        // ── Model ──
        public float ScaleFactor { get; set; } = 1f;
        public bool ConvertUnits { get; set; } = true;      // cm (archivo) → m (motor)
        public bool BakeAxisConversion { get; set; } = false;
        public bool ImportBlendShapes { get; set; } = true;
        public bool ImportVisibility { get; set; } = true;
        public bool ImportCameras { get; set; } = true;
        public bool ImportLights { get; set; } = true;
        public bool PreserveHierarchy { get; set; } = false;
        public bool SortHierarchyByName { get; set; } = true;

        // Meshes
        public string MeshCompression { get; set; } = "Off";   // Off/Low/Medium/High
        public bool ReadWrite { get; set; } = false;
        public string OptimizeMesh { get; set; } = "Everything";
        public bool GenerateColliders { get; set; } = false;

        // Geometry
        public bool WeldVertices { get; set; } = true;
        public string Normals { get; set; } = "Import";        // Import/Calculate/None
        public string Tangents { get; set; } = "Calculate Mikktspace";
        public float SmoothingAngle { get; set; } = 60f;

        // ── Rig ──
        public string AnimationType { get; set; } = "Generic";       // None/Generic/Humanoid
        public string AvatarDefinition { get; set; } = "Create From This Model"; // o "Copy From Other Avatar"
        public string AvatarSource { get; set; } = "";               // ruta .avatar cuando se copia
        public string CreatedAvatarPath { get; set; } = "";          // ruta del .avatar creado desde este modelo
        public string SkinWeights { get; set; } = "Standard (4 Bones)";
        public bool StripBones { get; set; } = false;
        public bool OptimizeGameObjects { get; set; } = false;

        // -- Animation --
        public bool ImportAnimation { get; set; } = true;
        public bool ImportAnimatedCustomProperties { get; set; } = false;
        public bool BakeAnimations { get; set; } = false;
        public string AnimationCompression { get; set; } = "Optimal";
        public float RotationError { get; set; } = 0.5f;
        public float PositionError { get; set; } = 0.5f;
        public float ScaleError { get; set; } = 0.5f;
        public bool RemoveConstantScaleCurves { get; set; } = false;
        public bool LoopTime { get; set; } = false;
        public bool LoopPose { get; set; } = false;
        public float CycleOffset { get; set; } = 0f;
        public bool BakeRootRotationIntoPose { get; set; } = false;
        public string RootRotationBasedUpon { get; set; } = "Body Orientation";
        public float RootRotationOffset { get; set; } = 0f;
        public bool BakeRootPositionYIntoPose { get; set; } = false;
        public string RootPositionYBasedUpon { get; set; } = "Original";
        public float RootPositionYOffset { get; set; } = 0f;
        public bool BakeRootPositionXZIntoPose { get; set; } = false;
        public string RootPositionXZBasedUpon { get; set; } = "Center Of Mass";
        public bool Mirror { get; set; } = false;
        public bool AdditiveReferencePose { get; set; } = false;

        public void Normalize()
        {
            if (ScaleFactor <= 0f || !float.IsFinite(ScaleFactor)) ScaleFactor = 1f;
            MeshCompression ??= "Off";
            OptimizeMesh ??= "Everything";
            Normals ??= "Import";
            Tangents ??= "Calculate Mikktspace";
            AnimationType ??= "Generic";
            AvatarDefinition ??= "Create From This Model";
            AvatarSource ??= "";
            CreatedAvatarPath ??= "";
            SkinWeights ??= "Standard (4 Bones)";
            AnimationCompression ??= "Optimal";
            if (!float.IsFinite(RotationError) || RotationError < 0f) RotationError = 0.5f;
            if (!float.IsFinite(PositionError) || PositionError < 0f) PositionError = 0.5f;
            if (!float.IsFinite(ScaleError) || ScaleError < 0f) ScaleError = 0.5f;
            if (!float.IsFinite(CycleOffset)) CycleOffset = 0f;
            RootRotationBasedUpon ??= "Body Orientation";
            RootPositionYBasedUpon ??= "Original";
            RootPositionXZBasedUpon ??= "Center Of Mass";
        }
    }

    public static class ModelImportSettingsAsset
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        // Caché por ruta + fecha de escritura. Evita releer/parsear el .import desde disco
        // en cada frame (p.ej. al mezclar blend trees con hijos FBX directos).
        private static readonly ConcurrentDictionary<string, (ModelImportSettings Data, DateTime Write)> _cache = new();
        private const int MaxCached = 256;

        public static string SettingsPath(string modelPath) => modelPath + ".import";

        public static ModelImportSettings Load(string modelPath)
        {
            try
            {
                string p = SettingsPath(modelPath);
                if (File.Exists(p))
                {
                    DateTime write = File.GetLastWriteTimeUtc(p);
                    if (_cache.TryGetValue(p, out var cached) && cached.Write == write)
                        return cached.Data;

                    var s = JsonSerializer.Deserialize<ModelImportSettings>(File.ReadAllText(p)) ?? new ModelImportSettings();
                    s.Normalize();
                    if (_cache.Count >= MaxCached) _cache.Clear();
                    _cache[p] = (s, write);
                    return s;
                }
            }
            catch { }
            return new ModelImportSettings();
        }

        public static void Save(string modelPath, ModelImportSettings settings)
        {
            settings.Normalize();
            try
            {
                string p = SettingsPath(modelPath);
                File.WriteAllText(p, JsonSerializer.Serialize(settings, Options));
                _cache[p] = (settings, File.GetLastWriteTimeUtc(p)); // mantén el caché coherente
            }
            catch { }
        }

        // Escala efectiva aplicada al importar: ScaleFactor × (ConvertUnits ? escala-a-metros : 1).
        public static float EffectiveScale(ModelImportSettings settings, float recommendedScale)
        {
            float convert = settings.ConvertUnits ? recommendedScale : 1f;
            return settings.ScaleFactor * convert;
        }
    }
}
