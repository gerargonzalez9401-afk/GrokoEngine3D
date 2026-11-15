using GrokoEngine;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private sealed record ParticleLibraryPreset(string Key, string FileName, string TextureName);

    private static readonly ParticleLibraryPreset[] ProfessionalParticlePresets =
    [
        new("fire", "Fire", "groko_particle_flame_flipbook.png"),
        new("smoke", "Smoke", "groko_particle_smoke.png"),
        new("sparks", "Sparks", "groko_particle_spark.png"),
        new("magic", "Magic", "groko_particle_magic_star.png"),
        new("explosion", "Explosion", "groko_particle_flame_flipbook.png"),
        new("dust", "Dust", "groko_particle_smoke.png"),
        new("rain", "Rain", "groko_particle_streak.png"),
        new("snow", "Snow", "groko_particle_soft_circle.png"),
        new("muzzle_flash", "MuzzleFlash", "groko_particle_flash.png"),
        new("hit_impact", "HitImpact", "groko_particle_spark.png"),
        new("energy_orb", "EnergyOrb", "groko_particle_magic_star.png"),
        new("heat_distortion", "HeatDistortion", "groko_particle_noise.png"),
    ];

    private void CreateProfessionalParticleLibrary(string targetDirectory)
    {
        try
        {
            string particleDir = ResolveParticleLibraryDirectory(targetDirectory);
            string textureDir = Path.Combine(particleDir, "Textures");
            Directory.CreateDirectory(particleDir);
            Directory.CreateDirectory(textureDir);

            CreateParticleProceduralTextures(textureDir);

            foreach (var preset in ProfessionalParticlePresets)
                WriteProfessionalParticlePreset(particleDir, textureDir, preset, overwrite: true);

            InvalidateProjectFolderCache(particleDir);
            InvalidateProjectFolderCache(textureDir);
            selectedAssetPath = particleDir;
            statusMessage = "Professional Particle Library creada: presets + texturas internas.";
        }
        catch (Exception ex)
        {
            statusMessage = "No se pudo crear librería de partículas: " + ex.Message;
        }
    }

    private void CreateProfessionalParticlePresetAsset(string targetDirectory, string key)
    {
        try
        {
            var preset = Array.Find(ProfessionalParticlePresets, p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
            {
                statusMessage = "Preset de partículas no encontrado: " + key;
                return;
            }

            string particleDir = ResolveParticleLibraryDirectory(targetDirectory);
            string textureDir = Path.Combine(particleDir, "Textures");
            Directory.CreateDirectory(particleDir);
            Directory.CreateDirectory(textureDir);
            CreateParticleProceduralTextures(textureDir);

            string path = WriteProfessionalParticlePreset(particleDir, textureDir, preset, overwrite: false);
            selectedAssetPath = path;
            particlePresetAssetPath = path;
            BeginRenameAsset(path);
            InvalidateProjectFolderCache(particleDir);
            InvalidateProjectFolderCache(textureDir);
            statusMessage = "Particle preset creado: " + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            statusMessage = "No se pudo crear particle preset: " + ex.Message;
        }
    }

    private void ImportUnityVfxPackFromDefaultFolder(string targetDirectory)
    {
        const string sourceRoot = @"D:\Recursos Motor";
        try
        {
            if (!Directory.Exists(sourceRoot))
            {
                statusMessage = "No existe D:/Recursos Motor.";
                return;
            }

            string particleDir = ResolveParticleLibraryDirectory(targetDirectory);
            string unityDir = Path.Combine(particleDir, "UnityVFX");
            string textureDir = Path.Combine(unityDir, "Textures");
            string audioDir = Path.Combine(unityDir, "Audio");
            Directory.CreateDirectory(unityDir);
            Directory.CreateDirectory(textureDir);
            Directory.CreateDirectory(audioDir);

            int textureCount = CopyFilesByExtensions(sourceRoot, textureDir, [".tga", ".png", ".jpg", ".jpeg"], recursive: false);
            int audioCount = CopyFilesByExtensions(Path.Combine(sourceRoot, "Audio"), audioDir, [".wav"], recursive: false);
            int presetCount = 0;

            string prefabDir = Path.Combine(sourceRoot, "Particles");
            if (Directory.Exists(prefabDir))
            {
                foreach (string prefabPath in Directory.EnumerateFiles(prefabDir, "*.prefab", SearchOption.TopDirectoryOnly))
                {
                    string path = WriteUnityVfxParticlePreset(unityDir, textureDir, audioDir, prefabPath);
                    selectedAssetPath = path;
                    presetCount++;
                }
            }

            InvalidateProjectFolderCache(particleDir);
            InvalidateProjectFolderCache(unityDir);
            InvalidateProjectFolderCache(textureDir);
            InvalidateProjectFolderCache(audioDir);
            selectedAssetPath = unityDir;
            statusMessage = $"Unity VFX importado en {unityDir}: {presetCount} presets, {textureCount} texturas, {audioCount} audios.";
        }
        catch (Exception ex)
        {
            statusMessage = "No se pudo importar Unity VFX: " + ex.Message;
        }
    }

    private static int CopyFilesByExtensions(string sourceDir, string targetDir, string[] extensions, bool recursive)
    {
        if (!Directory.Exists(sourceDir))
            return 0;

        Directory.CreateDirectory(targetDir);
        int count = 0;
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (string path in Directory.EnumerateFiles(sourceDir, "*.*", option))
        {
            string ext = Path.GetExtension(path);
            bool match = false;
            foreach (string wanted in extensions)
            {
                if (ext.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    match = true;
                    break;
                }
            }

            if (!match)
                continue;

            string dest = Path.Combine(targetDir, Path.GetFileName(path));
            File.Copy(path, dest, overwrite: true);
            count++;
        }

        return count;
    }

    private string WriteUnityVfxParticlePreset(string targetDir, string textureDir, string audioDir, string prefabPath)
    {
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string yaml = File.Exists(prefabPath) ? File.ReadAllText(prefabPath) : "";
        var ps = new GrokoEngine.ParticleSystem();
        ApplyUnityVfxPrefabPreset(ps, prefabName, yaml, textureDir);

        string outputName = SanitizeUnityVfxPresetName(prefabName);
        string path = Path.Combine(targetDir, outputName + ".particlepreset");
        var file = new ParticlePresetFile(3, outputName, CopyParticlePresetValues(ps));
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file, ParticleModuleClipboardJsonOptions));

        string fireSize = prefabName.Contains("Small", StringComparison.OrdinalIgnoreCase) ? "Small"
            : prefabName.Contains("Medium", StringComparison.OrdinalIgnoreCase) ? "Medium"
            : prefabName.Contains("Big", StringComparison.OrdinalIgnoreCase) ? "Big"
            : "";
        if (!string.IsNullOrWhiteSpace(fireSize))
        {
            string audio = Path.Combine(audioDir, $"SFX_Fire{fireSize}_L.wav");
            if (File.Exists(audio))
                WriteUnityVfxAudioHint(targetDir, outputName, audio);
        }

        return path;
    }

    private static string SanitizeUnityVfxPresetName(string prefabName)
    {
        string name = prefabName
            .Replace("VFX_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_01", "", StringComparison.OrdinalIgnoreCase)
            .Replace("__", "_", StringComparison.OrdinalIgnoreCase)
            .Trim('_', ' ', '-');
        return string.IsNullOrWhiteSpace(name) ? "UnityVFXParticle" : name;
    }

    private static void WriteUnityVfxAudioHint(string targetDir, string presetName, string audioPath)
    {
        string path = Path.Combine(targetDir, presetName + ".audiohint.json");
        var hint = new Dictionary<string, string>
        {
            ["Preset"] = presetName,
            ["AudioClip"] = audioPath,
            ["Usage"] = "Add AudioSource to the same GameObject and assign this .wav for looping fire ambience."
        };
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(hint, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ApplyUnityVfxPrefabPreset(GrokoEngine.ParticleSystem ps, string prefabName, string yaml, string textureDir)
    {
        bool simple = prefabName.Contains("Simple", StringComparison.OrdinalIgnoreCase);
        bool smoke = prefabName.Contains("Smoke", StringComparison.OrdinalIgnoreCase);
        bool floor = prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase);
        bool big = prefabName.Contains("Big", StringComparison.OrdinalIgnoreCase);
        bool medium = prefabName.Contains("Medium", StringComparison.OrdinalIgnoreCase);
        bool small = prefabName.Contains("Small", StringComparison.OrdinalIgnoreCase);

        ApplyProfessionalParticlePreset(ps, smoke ? "smoke" : "fire");

        ps.Duration = TryReadUnityFloatAfterKey(yaml, "lengthInSec", smoke ? 4.2f : 2.4f);
        ps.Looping = TryReadUnityIntAfterKey(yaml, "looping", 1) != 0;
        ps.Prewarm = TryReadUnityIntAfterKey(yaml, "prewarm", smoke || !simple) != 0;
        ps.PlayOnAwake = true;
        ps.SimulationSpace = ParticleSimulationSpace.World;
        ps.SortMode = ParticleSortMode.Distance;
        ps.SortParticles = true;
        ps.SoftParticles = true;
        ps.SoftParticleRange = smoke ? 0.85f : 0.35f;
        ps.RenderQueue = ParticleRenderQueue.Transparent;
        ps.TextureSheetAnimationModuleEnabled = false;
        ps.SheetColumns = 1;
        ps.SheetRows = 1;

        float sizeMul = big ? 1.65f : medium ? 1.15f : small ? 0.72f : 1f;
        if (floor)
            sizeMul *= 1.35f;

        if (smoke)
        {
            ps.TexturePath = PickUnityVfxTexture(textureDir, "T_VFX_Smoke_01.tga", "T_VFX_Dust_01.tga");
            ps.Duration = MathF.Max(ps.Duration, 4.0f);
            ps.EmitRate = simple ? 14f : 28f;
            ps.MaxParticles = simple ? 350 : 900;
            ps.LifetimeMin = 1.8f * sizeMul;
            ps.LifetimeMax = 4.0f * sizeMul;
            ps.SpeedMin = 0.12f;
            ps.SpeedMax = 0.95f * sizeMul;
            ps.SizeStart = 0.30f * sizeMul;
            ps.SizeEnd = 2.25f * sizeMul;
            ps.Shape = floor ? ParticleShape.Circle : ParticleShape.Cone;
            ps.ShapeRadius = floor ? 1.25f * sizeMul : 0.22f * sizeMul;
            ps.ShapeAngle = floor ? 5f : 18f;
            ps.BlendMode = ParticleBlendMode.Alpha;
            ps.HdrIntensity = 0.55f;
            ps.NoiseModuleEnabled = true;
            ps.TurbulenceStrength = 0.42f;
            ps.TurbulenceFrequency = 0.75f;
            ps.ColorVariation = 0.20f;
            ps.AlphaPower = 1.25f;
            SetParticleGradientKeys(ps,
                (0.00f, 0.42f, 0.38f, 0.32f, 0.00f),
                (0.20f, 0.55f, 0.50f, 0.42f, 0.35f),
                (0.70f, 0.34f, 0.32f, 0.30f, 0.18f),
                (1.00f, 0.18f, 0.18f, 0.18f, 0.00f));
        }
        else
        {
            ps.TexturePath = PickUnityVfxTexture(textureDir, floor ? "T_VFX_Fire_Ground_Mask_01.tga" : "T_VFX_Fire_Mask_01.tga", "T_VFX_Glow_01.tga");
            ps.Duration = MathF.Max(ps.Duration, 2.0f);
            ps.EmitRate = simple ? 35f * sizeMul : 70f * sizeMul;
            ps.MaxParticles = simple ? 500 : 1400;
            ps.LifetimeMin = 0.35f * sizeMul;
            ps.LifetimeMax = 1.25f * sizeMul;
            ps.SpeedMin = floor ? 0.18f : 0.65f;
            ps.SpeedMax = floor ? 0.75f * sizeMul : 2.7f * sizeMul;
            ps.SizeStart = (floor ? 0.42f : 0.22f) * sizeMul;
            ps.SizeEnd = (floor ? 1.10f : 0.04f) * sizeMul;
            ps.Shape = floor ? ParticleShape.Circle : ParticleShape.Cone;
            ps.ShapeRadius = floor ? 1.10f * sizeMul : 0.16f * sizeMul;
            ps.ShapeAngle = floor ? 4f : 15f;
            ps.ShapeRandomDirectionAmount = floor ? 0.45f : 0.28f;
            ps.BlendMode = ParticleBlendMode.Additive;
            ps.HdrIntensity = simple ? 2.2f : 3.4f;
            ps.NoiseModuleEnabled = true;
            ps.TurbulenceStrength = simple ? 0.28f : 0.62f;
            ps.TurbulenceFrequency = 1.25f;
            ps.ColorVariation = 0.22f;
            ps.SizeOverLifetimeCurve = Curve((0f, 0.18f), (0.20f, 1.25f), (0.70f, 0.85f), (1f, 0f));
            SetParticleGradientKeys(ps,
                (0.00f, 1.00f, 0.95f, 0.38f, 0.00f),
                (0.16f, 1.00f, 0.76f, 0.16f, 0.95f),
                (0.55f, 1.00f, 0.22f, 0.04f, 0.58f),
                (1.00f, 0.25f, 0.02f, 0.00f, 0.00f));
        }

        if (!simple && !smoke)
        {
            ps.SubEmitterDeath = "";
            ps.SubEmitterBirth = "";
            ps.SubEmitterCount = 8;
        }

        ps.LODDistance = 55f * MathF.Max(1f, sizeMul);
        ps.LODDistanceMax = 110f * MathF.Max(1f, sizeMul);
        SyncParticleColorsFromGradientEdges(ps);
        ps.SyncLegacyFieldsToModules();
    }

    private static string PickUnityVfxTexture(string textureDir, params string[] names)
    {
        foreach (string name in names)
        {
            string path = Path.Combine(textureDir, name);
            if (File.Exists(path))
                return path;
        }

        return "";
    }

    private static float TryReadUnityFloatAfterKey(string yaml, string key, float fallback)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return fallback;
        using var reader = new StringReader(yaml);
        string? line;
        string prefix = key + ":";
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string value = line[prefix.Length..].Trim();
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                return parsed;
        }
        return fallback;
    }

    private static int TryReadUnityIntAfterKey(string yaml, string key, int fallback)
    {
        float value = TryReadUnityFloatAfterKey(yaml, key, fallback);
        return (int)MathF.Round(value);
    }

    private static int TryReadUnityIntAfterKey(string yaml, string key, bool fallback)
    {
        return TryReadUnityIntAfterKey(yaml, key, fallback ? 1 : 0);
    }

    private string ResolveParticleLibraryDirectory(string targetDirectory)
    {
        string assetsDir = !string.IsNullOrWhiteSpace(rootAssetsPath)
            ? rootAssetsPath
            : Path.Combine(projectPath, "Assets");

        string baseDir = assetsDir;
        if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
        {
            try
            {
                string targetFull = Path.GetFullPath(targetDirectory);
                string assetsFull = Path.GetFullPath(assetsDir);
                bool insideAssets = targetFull.Equals(assetsFull, StringComparison.OrdinalIgnoreCase) ||
                                    targetFull.StartsWith(assetsFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                if (insideAssets)
                    baseDir = targetFull;
            }
            catch
            {
                baseDir = assetsDir;
            }
        }

        string name = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Equals("Particles", StringComparison.OrdinalIgnoreCase)
            ? baseDir
            : Path.Combine(baseDir, "Particles");
    }

    private string WriteProfessionalParticlePreset(string particleDir, string textureDir, ParticleLibraryPreset preset, bool overwrite)
    {
        string path = overwrite
            ? Path.Combine(particleDir, preset.FileName + ".particlepreset")
            : UniqueParticlePresetPath(particleDir, preset.FileName);

        var ps = new GrokoEngine.ParticleSystem();
        ApplyProfessionalParticlePreset(ps, preset.Key);
        string texturePath = Path.Combine(textureDir, preset.TextureName);
        if (File.Exists(texturePath))
            ps.TexturePath = texturePath;

        var file = new ParticlePresetFile(2, preset.FileName, CopyParticlePresetValues(ps));
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file, ParticleModuleClipboardJsonOptions));
        return path;
    }

    private static void ApplyProfessionalParticlePreset(GrokoEngine.ParticleSystem ps, string key)
    {
        ApplyParticlePreset(ps, key switch
        {
            "smoke" => "smoke",
            "sparks" => "sparks",
            "magic" => "magic",
            _ => "fire"
        });

        switch (key)
        {
            case "explosion":
                ps.Duration = 1.35f;
                ps.Looping = false;
                ps.EmitRate = 0f;
                ps.BurstEnabled = true;
                ps.BurstTime = 0f;
                ps.BurstCount = 160;
                ps.MaxParticles = 650;
                ps.LifetimeMin = 0.45f;
                ps.LifetimeMax = 1.55f;
                ps.SpeedMin = 3.5f;
                ps.SpeedMax = 9.0f;
                ps.SizeStart = 0.38f;
                ps.SizeEnd = 0.05f;
                ps.Shape = ParticleShape.Sphere;
                ps.ShapeRadius = 0.35f;
                ps.ShapeRandomDirectionAmount = 0.92f;
                ps.NoiseModuleEnabled = true;
                ps.TurbulenceStrength = 0.8f;
                ps.TurbulenceFrequency = 1.4f;
                ps.SoftParticles = true;
                ps.SoftParticleRange = 0.55f;
                ps.HdrIntensity = 3.2f;
                ps.ColorVariation = 0.35f;
                ps.SizeOverLifetimeCurve = Curve((0f, 0.15f), (0.18f, 1.85f), (0.72f, 0.72f), (1f, 0f));
                SetParticleGradientKeys(ps,
                    (0.00f, 1.00f, 0.95f, 0.42f, 0.00f),
                    (0.12f, 1.00f, 0.70f, 0.16f, 1.00f),
                    (0.48f, 0.95f, 0.18f, 0.04f, 0.62f),
                    (1.00f, 0.07f, 0.04f, 0.02f, 0.00f));
                break;

            case "dust":
                ps.Duration = 5.0f;
                ps.Looping = true;
                ps.EmitRate = 18f;
                ps.MaxParticles = 700;
                ps.LifetimeMin = 1.4f;
                ps.LifetimeMax = 3.4f;
                ps.SpeedMin = 0.18f;
                ps.SpeedMax = 0.85f;
                ps.SizeStart = 0.20f;
                ps.SizeEnd = 1.10f;
                ps.Shape = ParticleShape.Circle;
                ps.ShapeRadius = 0.75f;
                ps.ShapeRandomDirectionAmount = 0.65f;
                ps.BlendMode = ParticleBlendMode.Alpha;
                ps.HdrIntensity = 0.65f;
                ps.ColorSaturation = 0.55f;
                ps.ColorVibrance = 0.08f;
                ps.NoiseModuleEnabled = true;
                ps.TurbulenceStrength = 0.38f;
                ps.TurbulenceFrequency = 0.7f;
                ps.SoftParticles = true;
                ps.SoftParticleRange = 0.75f;
                SetParticleGradientKeys(ps,
                    (0.00f, 0.55f, 0.46f, 0.34f, 0.00f),
                    (0.25f, 0.72f, 0.62f, 0.44f, 0.32f),
                    (0.70f, 0.48f, 0.40f, 0.30f, 0.18f),
                    (1.00f, 0.28f, 0.24f, 0.20f, 0.00f));
                break;

            case "rain":
                ps.Duration = 5.0f;
                ps.Looping = true;
                ps.EmitRate = 520f;
                ps.MaxParticles = 2500;
                ps.LifetimeMin = 0.75f;
                ps.LifetimeMax = 1.35f;
                ps.SpeedMin = 11f;
                ps.SpeedMax = 18f;
                ps.SizeStart = 0.035f;
                ps.SizeEnd = 0.018f;
                ps.Shape = ParticleShape.Box;
                ps.ShapeBoxSizeX = 18f;
                ps.ShapeBoxSizeY = 1f;
                ps.ShapeBoxSizeZ = 18f;
                ps.VelocityOverLifetimeModuleEnabled = true;
                ps.VelOverLifeY = -13f;
                ps.RenderMode = ParticleRenderMode.StretchedBillboard;
                ps.StretchedBillboard = true;
                ps.StretchSpeedScale = 0.075f;
                ps.StretchLengthScale = 2.6f;
                ps.BlendMode = ParticleBlendMode.Alpha;
                ps.HdrIntensity = 1.0f;
                ps.SoftParticles = false;
                SetParticleGradientKeys(ps,
                    (0.00f, 0.65f, 0.84f, 1.00f, 0.00f),
                    (0.12f, 0.70f, 0.88f, 1.00f, 0.46f),
                    (0.82f, 0.55f, 0.76f, 1.00f, 0.34f),
                    (1.00f, 0.45f, 0.65f, 1.00f, 0.00f));
                break;

            case "snow":
                ps.Duration = 6.0f;
                ps.Looping = true;
                ps.EmitRate = 95f;
                ps.MaxParticles = 1800;
                ps.LifetimeMin = 4.0f;
                ps.LifetimeMax = 8.0f;
                ps.SpeedMin = 0.20f;
                ps.SpeedMax = 0.85f;
                ps.SizeStart = 0.045f;
                ps.SizeEnd = 0.035f;
                ps.Shape = ParticleShape.Box;
                ps.ShapeBoxSizeX = 18f;
                ps.ShapeBoxSizeY = 1f;
                ps.ShapeBoxSizeZ = 18f;
                ps.VelocityOverLifetimeModuleEnabled = true;
                ps.VelOverLifeY = -0.55f;
                ps.NoiseModuleEnabled = true;
                ps.TurbulenceStrength = 0.65f;
                ps.TurbulenceFrequency = 0.55f;
                ps.BlendMode = ParticleBlendMode.Alpha;
                ps.HdrIntensity = 1.2f;
                ps.ColorVariation = 0.12f;
                SetParticleGradientKeys(ps,
                    (0.00f, 0.92f, 0.98f, 1.00f, 0.00f),
                    (0.16f, 0.95f, 0.99f, 1.00f, 0.82f),
                    (0.82f, 0.80f, 0.92f, 1.00f, 0.72f),
                    (1.00f, 0.70f, 0.84f, 1.00f, 0.00f));
                break;

            case "muzzle_flash":
                ps.Duration = 0.18f;
                ps.Looping = false;
                ps.EmitRate = 0f;
                ps.BurstEnabled = true;
                ps.BurstCount = 14;
                ps.MaxParticles = 64;
                ps.LifetimeMin = 0.045f;
                ps.LifetimeMax = 0.12f;
                ps.SpeedMin = 1.2f;
                ps.SpeedMax = 3.0f;
                ps.SizeStart = 0.42f;
                ps.SizeEnd = 0f;
                ps.Shape = ParticleShape.Cone;
                ps.ShapeRadius = 0.04f;
                ps.ShapeAngle = 18f;
                ps.BlendMode = ParticleBlendMode.Additive;
                ps.HdrIntensity = 6f;
                ps.ColorVariation = 0.16f;
                SetParticleGradientKeys(ps,
                    (0.00f, 1.00f, 1.00f, 0.72f, 0.00f),
                    (0.08f, 1.00f, 0.84f, 0.22f, 1.00f),
                    (0.42f, 1.00f, 0.26f, 0.04f, 0.38f),
                    (1.00f, 0.25f, 0.04f, 0.00f, 0.00f));
                break;

            case "hit_impact":
                ps.Duration = 0.85f;
                ps.Looping = false;
                ps.EmitRate = 0f;
                ps.BurstEnabled = true;
                ps.BurstCount = 48;
                ps.MaxParticles = 220;
                ps.LifetimeMin = 0.18f;
                ps.LifetimeMax = 0.55f;
                ps.SpeedMin = 3.5f;
                ps.SpeedMax = 8.5f;
                ps.SizeStart = 0.05f;
                ps.SizeEnd = 0f;
                ps.Shape = ParticleShape.Cone;
                ps.ShapeAngle = 52f;
                ps.ShapeRadius = 0.05f;
                ps.RenderMode = ParticleRenderMode.StretchedBillboard;
                ps.StretchedBillboard = true;
                ps.StretchSpeedScale = 0.16f;
                ps.StretchLengthScale = 1.6f;
                ps.BlendMode = ParticleBlendMode.Additive;
                ps.HdrIntensity = 3.0f;
                ps.GravityScale = -0.15f;
                break;

            case "energy_orb":
                ps.Duration = 4.0f;
                ps.Looping = true;
                ps.EmitRate = 64f;
                ps.MaxParticles = 1200;
                ps.LifetimeMin = 0.9f;
                ps.LifetimeMax = 2.2f;
                ps.SpeedMin = 0.15f;
                ps.SpeedMax = 1.2f;
                ps.SizeStart = 0.08f;
                ps.SizeEnd = 0.02f;
                ps.Shape = ParticleShape.Sphere;
                ps.ShapeRadius = 0.85f;
                ps.ShapeRandomDirectionAmount = 1f;
                ps.BlendMode = ParticleBlendMode.Additive;
                ps.HdrIntensity = 3.8f;
                ps.NoiseModuleEnabled = true;
                ps.TurbulenceStrength = 1.25f;
                ps.TurbulenceFrequency = 2.4f;
                ps.TrailsModuleEnabled = true;
                ps.TrailEnabled = true;
                ps.TrailLifetime = 0.22f;
                ps.TrailWidthStart = 0.035f;
                ps.TrailWidthEnd = 0f;
                SetParticleGradientKeys(ps,
                    (0.00f, 0.22f, 0.85f, 1.00f, 0.00f),
                    (0.20f, 0.42f, 1.00f, 0.92f, 1.00f),
                    (0.62f, 0.82f, 0.30f, 1.00f, 0.76f),
                    (1.00f, 0.10f, 0.00f, 0.28f, 0.00f));
                break;

            case "heat_distortion":
                ps.Duration = 3.0f;
                ps.Looping = true;
                ps.EmitRate = 42f;
                ps.MaxParticles = 520;
                ps.LifetimeMin = 0.8f;
                ps.LifetimeMax = 1.8f;
                ps.SpeedMin = 0.25f;
                ps.SpeedMax = 0.95f;
                ps.SizeStart = 0.34f;
                ps.SizeEnd = 0.95f;
                ps.Shape = ParticleShape.Cone;
                ps.ShapeRadius = 0.35f;
                ps.ShapeAngle = 7f;
                ps.BlendMode = ParticleBlendMode.Alpha;
                ps.HdrIntensity = 0.15f;
                ps.ColorSaturation = 0.1f;
                ps.ColorVibrance = 0f;
                ps.SoftParticles = true;
                ps.SoftParticleRange = 0.8f;
                ps.DistortionEnabled = true;
                ps.DistortionStrength = 0.045f;
                ps.RefractionStrength = 0.025f;
                ps.NoiseModuleEnabled = true;
                ps.TurbulenceStrength = 0.22f;
                ps.TurbulenceFrequency = 1.1f;
                SetParticleGradientKeys(ps,
                    (0.00f, 0.65f, 0.72f, 0.78f, 0.00f),
                    (0.30f, 0.70f, 0.80f, 0.95f, 0.12f),
                    (0.78f, 0.55f, 0.65f, 0.78f, 0.08f),
                    (1.00f, 0.40f, 0.45f, 0.52f, 0.00f));
                break;
        }

        ps.TextureSheetAnimationModuleEnabled = key is "fire" or "explosion";
        if (ps.TextureSheetAnimationModuleEnabled)
        {
            ps.SheetColumns = 4;
            ps.SheetRows = 4;
            ps.SheetFrameRate = key == "explosion" ? 18f : 12f;
        }

        SyncParticleColorsFromGradientEdges(ps);
        ps.LifetimeCurve = Curve((0f, 1f), (1f, 1f));
        ps.SpeedCurve = ParticleCurve.FromSimple(ps.SpeedCurveMid, ps.SpeedCurveMidValue);
        ps.StartSizeCurve = ParticleCurve.FromSimple(ps.StartSizeCurveMid, ps.StartSizeCurveMidValue);
        ps.GravityCurve = ParticleCurve.FromSimple(ps.GravityCurveMid, ps.GravityCurveMidValue);
        ps.SyncLegacyFieldsToModules();
    }

    private static ParticleCurve Curve(params (float Time, float Value)[] keys)
    {
        var curve = new ParticleCurve
        {
            Keys = new List<ParticleCurveKey>()
        };

        foreach (var key in keys)
            curve.Keys.Add(new ParticleCurveKey(key.Time, key.Value, ParticleCurveTangentMode.Smooth));
        curve.Normalize();
        return curve;
    }

    private static void CreateParticleProceduralTextures(string textureDir)
    {
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_soft_circle.png"), () => CreateSoftCircleTexture(256, Color.White));
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_smoke.png"), () => CreateSmokeTexture(256));
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_spark.png"), () => CreateSparkTexture(256));
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_magic_star.png"), () => CreateMagicStarTexture(256));
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_streak.png"), () => CreateStreakTexture(256));
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_flash.png"), () => CreateFlashTexture(256));
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_noise.png"), () => CreateNoiseTexture(256));
        WritePngIfMissing(Path.Combine(textureDir, "groko_particle_flame_flipbook.png"), () => CreateFlameFlipbookTexture(512, 4, 4));
    }

    private static void WritePngIfMissing(string path, Func<Bitmap> create)
    {
        if (File.Exists(path))
            return;
        using var bitmap = create();
        bitmap.Save(path, ImageFormat.Png);
    }

    private static Bitmap CreateSoftCircleTexture(int size, Color tint)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float r = MathF.Sqrt(dx * dx + dy * dy);
            float a = Smooth01(1f - r);
            a *= a;
            bitmap.SetPixel(x, y, Color.FromArgb((int)(a * 255f), tint.R, tint.G, tint.B));
        }
        return bitmap;
    }

    private static Bitmap CreateSmokeTexture(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var rng = new System.Random(1729);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float r = MathF.Sqrt(dx * dx + dy * dy);
            float angle = MathF.Atan2(dy, dx);
            float ring = MathF.Sin(angle * 5.0f + r * 12.0f) * 0.18f;
            float noise = ValueNoise(x * 0.055f, y * 0.055f, 10) * 0.40f + ValueNoise(x * 0.018f, y * 0.018f, 24) * 0.35f;
            float a = Smooth01(1.05f - r + ring + noise * 0.25f);
            a = MathF.Pow(Math.Clamp(a, 0f, 1f), 1.55f) * 0.88f;
            int shade = 190 + rng.Next(0, 14);
            bitmap.SetPixel(x, y, Color.FromArgb((int)(a * 255f), shade, shade, shade));
        }
        return bitmap;
    }

    private static Bitmap CreateSparkTexture(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = MathF.Abs((x - c) / c);
            float dy = MathF.Abs((y - c) / c);
            float line = MathF.Exp(-dx * dx * 42f) * MathF.Exp(-dy * 1.8f);
            float core = MathF.Exp(-(dx * dx + dy * dy) * 22f);
            float a = Math.Clamp(line * 0.85f + core, 0f, 1f);
            bitmap.SetPixel(x, y, Color.FromArgb((int)(a * 255f), 255, 235, 160));
        }
        return bitmap;
    }

    private static Bitmap CreateMagicStarTexture(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float r = MathF.Sqrt(dx * dx + dy * dy);
            float cross = MathF.Exp(-MathF.Abs(dx) * 16f) + MathF.Exp(-MathF.Abs(dy) * 16f);
            float diag = MathF.Exp(-MathF.Abs(dx - dy) * 18f) * 0.55f + MathF.Exp(-MathF.Abs(dx + dy) * 18f) * 0.55f;
            float glow = MathF.Pow(Math.Clamp(1f - r, 0f, 1f), 3f);
            float a = Math.Clamp((cross + diag) * MathF.Pow(Math.Clamp(1f - r, 0f, 1f), 1.5f) + glow, 0f, 1f);
            bitmap.SetPixel(x, y, Color.FromArgb((int)(a * 255f), 210, 245, 255));
        }
        return bitmap;
    }

    private static Bitmap CreateStreakTexture(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = MathF.Abs((x - c) / c);
            float dy = MathF.Abs((y - c) / c);
            float a = MathF.Exp(-dx * dx * 85f) * MathF.Exp(-dy * 2.6f);
            bitmap.SetPixel(x, y, Color.FromArgb((int)(a * 255f), 190, 225, 255));
        }
        return bitmap;
    }

    private static Bitmap CreateFlashTexture(int size)
    {
        var bitmap = CreateSoftCircleTexture(size, Color.FromArgb(255, 255, 222, 140));
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(220, 255, 246, 180), Math.Max(2f, size * 0.025f));
        var c = new PointF(size * 0.5f, size * 0.5f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI * 2f / 8f;
            var p = new PointF(c.X + MathF.Cos(a) * size * 0.44f, c.Y + MathF.Sin(a) * size * 0.44f);
            g.DrawLine(pen, c, p);
        }
        return bitmap;
    }

    private static Bitmap CreateNoiseTexture(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n0 = ValueNoise(x * 0.045f, y * 0.045f, 72);
            float n1 = ValueNoise(x * 0.110f, y * 0.110f, 91);
            int r = (int)Math.Clamp((n0 * 0.65f + n1 * 0.35f) * 255f, 0f, 255f);
            int g = (int)Math.Clamp((ValueNoise(x * 0.055f + 18f, y * 0.055f, 37) * 0.7f + n1 * 0.3f) * 255f, 0f, 255f);
            bitmap.SetPixel(x, y, Color.FromArgb(255, r, g, 128));
        }
        return bitmap;
    }

    private static Bitmap CreateFlameFlipbookTexture(int size, int columns, int rows)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        int cell = size / columns;
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int frame = 0; frame < columns * rows; frame++)
        {
            int cx = (frame % columns) * cell;
            int cy = (frame / columns) * cell;
            float t = frame / Math.Max(1f, columns * rows - 1f);
            using var flame = CreateFlameCell(cell, t);
            g.DrawImage(flame, cx, cy, cell, cell);
        }
        return bitmap;
    }

    private static Bitmap CreateFlameCell(int size, float t)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x - c) / c;
            float ny = (y - c) / c;
            float up = 1f - (ny + 1f) * 0.5f;
            float width = 0.18f + 0.45f * up;
            float wave = MathF.Sin((ny * 8f) + t * 8f) * 0.10f + MathF.Sin((ny * 17f) - t * 5f) * 0.045f;
            float body = MathF.Exp(-MathF.Pow((nx + wave) / width, 2f) * 2.2f) * Smooth01(up) * Smooth01(1f - MathF.Abs(ny) * 0.82f);
            float lick = MathF.Exp(-MathF.Pow((nx - 0.23f * MathF.Sin(t * 6f)) / (0.07f + 0.10f * up), 2f) * 2.8f) * Smooth01(up - 0.10f);
            float a = Math.Clamp(body + lick * 0.45f, 0f, 1f);
            int r = 255;
            int g = (int)Math.Clamp(70 + up * 180f + a * 30f, 0f, 255f);
            int b = (int)Math.Clamp(10 + up * 55f, 0f, 255f);
            bitmap.SetPixel(x, y, Color.FromArgb((int)(a * 255f), r, g, b));
        }
        return bitmap;
    }

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int ix = (int)MathF.Floor(x);
        int iy = (int)MathF.Floor(y);
        float fx = x - ix;
        float fy = y - iy;
        float a = Hash01(ix, iy, seed);
        float b = Hash01(ix + 1, iy, seed);
        float c = Hash01(ix, iy + 1, seed);
        float d = Hash01(ix + 1, iy + 1, seed);
        float sx = Smooth01(fx);
        float sy = Smooth01(fy);
        return ParticleLerp(ParticleLerp(a, b, sx), ParticleLerp(c, d, sx), sy);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215f;
        }
    }

    private static float ParticleLerp(float a, float b, float t) => a + (b - a) * t;
}
