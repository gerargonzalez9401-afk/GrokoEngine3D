using System;

namespace GrokoEngine
{
    /// <summary>
    /// Configuración de iluminación de una escena/proyecto. Se guarda como .asset y no
    /// depende de la ventana del editor, de modo que un bake es reproducible en otro equipo.
    /// Los nombres siguen el flujo de Lighting Settings de Unity, pero las opciones que aún
    /// no tienen integrador (GI en tiempo real y shadowmask baked) se mantienen explícitas.
    /// </summary>
    public sealed class LightingSettingsAsset : ScriptableObject
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public bool RealtimeGlobalIllumination = false;
        public bool BakedGlobalIllumination = true;
        public int MixedLightingMode = 1; // 0=Baked Indirect, 1=Shadowmask, 2=Subtractive
        // Igual que "Distance Shadowmask" de Unity: cerca se conserva la sombra realtime;
        // lejos se usa la oclusión baked para que el coste y el parpadeo de cascadas no crezcan.
        public bool DistanceShadowmask = true;
        public float ShadowmaskDistance = 40f;
        public float ShadowmaskDistanceFade = 8f;
        public int Lightmapper = 0;       // 0=Progressive CPU, 1=GPU preview
        public bool ProgressiveUpdates = true;
        public bool MultipleImportanceSampling = true;
        public int DirectSamples = 32;
        public int IndirectSamples = 200;
        public int EnvironmentSamples = 200;
        public int Bounces = 2;
        public int Filtering = 0;         // 0=Auto, 1=None, 2=Advanced
        public int IndirectResolution = 2;
        public int LightmapResolution = 20;
        public int LightmapPadding = 2;
        public int MaxLightmapSize = 1024;
        public bool CompressLightmaps = true;
        public bool AmbientOcclusion = false;
        public int DirectionalMode = 0;   // 0=Directional, 1=Non-directional
        public float AlbedoBoost = 1f;
        public float IndirectIntensity = 1f;
        public bool AutoGenerate = false;

        public void Normalize()
        {
            Version = CurrentVersion;
            MixedLightingMode = Math.Clamp(MixedLightingMode, 0, 2);
            ShadowmaskDistance = Math.Clamp(ShadowmaskDistance, 1f, 500f);
            ShadowmaskDistanceFade = Math.Clamp(ShadowmaskDistanceFade, 0.01f, ShadowmaskDistance);
            Lightmapper = Math.Clamp(Lightmapper, 0, 1);
            DirectSamples = Math.Clamp(DirectSamples, 1, 4096);
            IndirectSamples = Math.Clamp(IndirectSamples, 1, 8192);
            EnvironmentSamples = Math.Clamp(EnvironmentSamples, 1, 8192);
            Bounces = Math.Clamp(Bounces, 0, 16);
            Filtering = Math.Clamp(Filtering, 0, 2);
            IndirectResolution = Math.Clamp(IndirectResolution, 1, 100);
            LightmapResolution = Math.Clamp(LightmapResolution, 1, 200);
            LightmapPadding = Math.Clamp(LightmapPadding, 1, 64);
            MaxLightmapSize = NormalizeSize(MaxLightmapSize);
            DirectionalMode = Math.Clamp(DirectionalMode, 0, 1);
            AlbedoBoost = Math.Clamp(AlbedoBoost, 0f, 4f);
            IndirectIntensity = Math.Clamp(IndirectIntensity, 0f, 4f);
        }

        public static int NormalizeSize(int value) => value switch
        {
            <= 256 => 256,
            <= 512 => 512,
            <= 1024 => 1024,
            <= 2048 => 2048,
            _ => 4096
        };
    }
}
