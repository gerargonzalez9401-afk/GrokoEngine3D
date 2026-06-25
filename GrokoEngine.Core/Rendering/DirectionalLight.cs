using MiMotor.Mathematics;
using System;

namespace GrokoEngine
{
    // =====================================================
    // DIRECTIONAL LIGHT
    // =====================================================
    public class DirectionalLight : Component
    {
        public Vector3 Direction     { get; set; } = new Vector3(0.5f, -1.0f, 0.5f);
        public float   Intensity     { get; set; } = 1.0f;
        // Color canónico via R, G, B — un solo set de propiedades, sin redundancia
        public float   R             { get; set; } = 1.0f;
        public float   G             { get; set; } = 1.0f;
        public float   B             { get; set; } = 1.0f;
        public bool    Shadows       { get; set; } = false;
        public float   ShadowStrength{ get; set; } = 0.55f;

        // ── Light Appearance (estilo Unity): Filter (R,G,B) + Temperatura de color ──
        public bool    UseTemperature { get; set; } = false;
        private float  _temperature = 6500f;
        /// <summary>Temperatura de color en Kelvin (1500=naranja calido … 6500=blanco … 20000=azul frio).</summary>
        public float   Temperature   { get => _temperature; set => _temperature = Math.Clamp(value, 1500f, 20000f); }

        // ── Indirect Multiplier: escala la luz indirecta/rebote (ambiente) aportada por el sol ──
        private float  _indirectMultiplier = 1f;
        public float   IndirectMultiplier { get => _indirectMultiplier; set => _indirectMultiplier = Math.Clamp(value, 0f, 4f); }

        // ── Celestial Body: disco solar visible en el cielo + halo (estilo Unity) ──
        public bool    ShowSunDisk   { get; set; } = true;
        private float  _angularDiameter = 1.5f;
        /// <summary>Diametro angular del disco solar en grados (Unity: ~0.5 real, mayor = sol mas grande).</summary>
        public float   AngularDiameter { get => _angularDiameter; set => _angularDiameter = Math.Clamp(value, 0.1f, 30f); }

        // ── Volumetric / God Rays (light shafts screen-space desde el sol) ──
        public bool    GodRays       { get; set; } = false;
        private float  _godRaysStrength = 1f;
        public float   GodRaysStrength { get => _godRaysStrength; set => _godRaysStrength = Math.Clamp(value, 0f, 5f); }

        /// <summary>Color final del sol = Filter(R,G,B) * tinte de temperatura (si UseTemperature).</summary>
        public Vector3 GetEffectiveColor()
        {
            var c = new Vector3(R, G, B);
            if (UseTemperature)
            {
                var k = KelvinToRgb(_temperature);
                c = new Vector3(c.X * k.X, c.Y * k.Y, c.Z * k.Z);
            }
            return c;
        }

        /// <summary>Kelvin -> RGB normalizado [0,1] (aproximacion de Tanner Helland), igual que Unity.</summary>
        public static Vector3 KelvinToRgb(float kelvin)
        {
            float t = Math.Clamp(kelvin, 1000f, 40000f) / 100f;
            float r, g, b;
            if (t <= 66f) r = 255f;
            else r = 329.698727446f * MathF.Pow(t - 60f, -0.1332047592f);
            if (t <= 66f) g = 99.4708025861f * MathF.Log(t) - 161.1195681661f;
            else g = 288.1221695283f * MathF.Pow(t - 60f, -0.0755148492f);
            if (t >= 66f) b = 255f;
            else if (t <= 19f) b = 0f;
            else b = 138.5177312231f * MathF.Log(t - 10f) - 305.0447927307f;
            return new Vector3(
                Math.Clamp(r, 0f, 255f) / 255f,
                Math.Clamp(g, 0f, 255f) / 255f,
                Math.Clamp(b, 0f, 255f) / 255f);
        }

        /// <summary>Devuelve la dirección normalizada. Si es vector cero retorna (0,-1,0).</summary>
        public Vector3 GetNormalizedDirection()
        {
            float len = MathF.Sqrt(
                Direction.X * Direction.X +
                Direction.Y * Direction.Y +
                Direction.Z * Direction.Z);
            return len < 0.0001f
                ? new Vector3(0f, -1f, 0f)
                : Direction.Normalized();
        }
    }

    // =====================================================
    // POINT LIGHT
    // =====================================================
    public class PointLight : Component
    {
        public float Intensity      { get; set; } = 1.0f;
        private float _range = 6.0f;
        public float Range
        {
            get => _range;
            set => _range = Math.Max(0.01f, value);
        }
        public float R              { get; set; } = 1.0f;
        public float G              { get; set; } = 0.95f;
        public float B              { get; set; } = 0.82f;
        public bool  Shadows        { get; set; } = false;
        public float ShadowStrength { get; set; } = 0.55f;

        // ── Light Appearance: Filter (RGB) + Temperatura de color (Kelvin), como Unity ──
        public bool  UseTemperature { get; set; } = false;
        private float _temperature = 6500f;
        public float Temperature    { get => _temperature; set => _temperature = Math.Clamp(value, 1500f, 20000f); }
        public Vector3 GetEffectiveColor()
        {
            var c = new Vector3(R, G, B);
            if (UseTemperature)
            {
                var k = DirectionalLight.KelvinToRgb(_temperature);
                c = new Vector3(c.X * k.X, c.Y * k.Y, c.Z * k.Z);
            }
            return c;
        }
    }

    // =====================================================
    // SPOT LIGHT
    // =====================================================
    public class SpotLight : Component
    {
        public float   Intensity     { get; set; } = 1.0f;
        private float _range = 8.0f;
        public float Range
        {
            get => _range;
            set => _range = Math.Max(0.01f, value);
        }
        private float _angle = 35.0f;
        public float Angle
        {
            get => _angle;
            set => _angle = Math.Clamp(value, 1f, 179f);
        }
        /// <summary>Dirección en espacio local hacia donde apunta el foco. (0,0,1) = frente del objeto.</summary>
        public Vector3 Direction     { get; set; } = new Vector3(0f, 0f, 1f);
        public float   R             { get; set; } = 1.0f;
        public float   G             { get; set; } = 0.94f;
        public float   B             { get; set; } = 0.78f;
        public bool    Shadows       { get; set; } = false;
        public float   ShadowStrength{ get; set; } = 0.55f;

        // ── Light Appearance: Filter (RGB) + Temperatura de color (Kelvin), como Unity ──
        public bool  UseTemperature { get; set; } = false;
        private float _temperature = 6500f;
        public float Temperature    { get => _temperature; set => _temperature = Math.Clamp(value, 1500f, 20000f); }
        public Vector3 GetEffectiveColor()
        {
            var c = new Vector3(R, G, B);
            if (UseTemperature)
            {
                var k = DirectionalLight.KelvinToRgb(_temperature);
                c = new Vector3(c.X * k.X, c.Y * k.Y, c.Z * k.Z);
            }
            return c;
        }
    }

    // =====================================================
    // AMBIENT LIGHT
    // =====================================================
    public class AmbientLight : Component
    {
        public float Intensity  { get; set; } = 0.18f;
        public float SkyStrength{ get; set; } = 0.08f;
        public float R          { get; set; } = 0.72f;
        public float G          { get; set; } = 0.78f;
        public float B          { get; set; } = 0.86f;
    }

    // =====================================================
    // AREA LIGHT
    // =====================================================
    public class AreaLight : Component
    {
        public float Intensity      { get; set; } = 1.0f;
        private float _range = 8.0f;
        public float Range
        {
            get => _range;
            set => _range = Math.Max(0.01f, value);
        }
        private float _width = 2.0f;
        public float Width
        {
            get => _width;
            set => _width = Math.Max(0.01f, value);
        }
        private float _height = 2.0f;
        public float Height
        {
            get => _height;
            set => _height = Math.Max(0.01f, value);
        }
        /// <summary>Propiedad heredada para compatibilidad — asigna Width y Height al mismo valor.</summary>
        public float Size
        {
            get => (_width + _height) * 0.5f;
            set { Width = value; Height = value; }
        }
        public float R              { get; set; } = 1.0f;
        public float G              { get; set; } = 0.93f;
        public float B              { get; set; } = 0.82f;
        public bool  Shadows        { get; set; } = false;
        public float ShadowStrength { get; set; } = 0.45f;
    }

    // =====================================================
    // RECTANGLE LIGHT
    // =====================================================
    public class RectangleLight : Component
    {
        public float Intensity      { get; set; } = 1.0f;
        private float _range = 8.0f;
        public float Range
        {
            get => _range;
            set => _range = Math.Max(0.01f, value);
        }
        private float _width = 3.0f;
        public float Width
        {
            get => _width;
            set => _width = Math.Max(0.01f, value);
        }
        private float _height = 1.5f;
        public float Height
        {
            get => _height;
            set => _height = Math.Max(0.01f, value);
        }
        public float R              { get; set; } = 1.0f;
        public float G              { get; set; } = 0.93f;
        public float B              { get; set; } = 0.82f;
        public bool  Shadows        { get; set; } = false;
        public float ShadowStrength { get; set; } = 0.45f;
    }

    // =====================================================
    // POST PROCESS SETTINGS
    // =====================================================
    public class PostProcessSettings : Component
    {
        public bool PostProcessEnabled { get; set; } = true;

        // Compatibilidad con escenas viejas y código existente: ahora no oculta Component.Enabled.
        // El componente se desactiva con Component.Enabled; el efecto se apaga con PostProcessEnabled.
        public bool EnabledFx
        {
            get => PostProcessEnabled;
            set => PostProcessEnabled = value;
        }

        private float _exposure = 1.0f;
        public float Exposure
        {
            get => _exposure;
            set => _exposure = Math.Max(0.001f, value);
        }

        private float _gamma = 2.2f;
        public float Gamma
        {
            get => _gamma;
            set => _gamma = Math.Max(0.1f, value);
        }

        public bool  ToneMapping              { get; set; } = true;
        // 0 = ACES (filmico clasico), 1 = AgX (moderno, mejor con colores saturados/brillantes).
        public int   ToneMappingMode          { get; set; } = 0;

        // ── White Balance (aplicado en HDR antes del tonemap) ──
        private float _whiteBalanceTemp;
        public float WhiteBalanceTemperature { get => _whiteBalanceTemp; set => _whiteBalanceTemp = Math.Clamp(value, -100f, 100f); }
        private float _whiteBalanceTint;
        public float WhiteBalanceTint { get => _whiteBalanceTint; set => _whiteBalanceTint = Math.Clamp(value, -100f, 100f); }

        // ── Film Grain (grano de pelicula animado, en LDR al final) ──
        public bool FilmGrain { get; set; } = false;
        private float _filmGrainIntensity = 0.4f;
        public float FilmGrainIntensity { get => _filmGrainIntensity; set => _filmGrainIntensity = Math.Clamp(value, 0f, 1f); }

        public bool  Bloom                    { get; set; } = false;

        private float _bloomStrength = 0.25f;
        public float BloomStrength
        {
            get => _bloomStrength;
            set => _bloomStrength = Math.Clamp(value, 0f, 10f);
        }

        private float _bloomThreshold = 0.9f;
        public float BloomThreshold
        {
            get => _bloomThreshold;
            set => _bloomThreshold = Math.Max(0f, value);
        }

        private float _bloomScatter = 0.7f;
        public float BloomScatter
        {
            get => _bloomScatter;
            set => _bloomScatter = Math.Clamp(value, 0f, 1f);
        }

        private float _bloomClamp = 65472f;
        public float BloomClamp
        {
            get => _bloomClamp;
            set => _bloomClamp = Math.Max(0f, value);
        }

        public float BloomTintR { get; set; } = 1f;
        public float BloomTintG { get; set; } = 1f;
        public float BloomTintB { get; set; } = 1f;
        public bool BloomHighQualityFiltering { get; set; } = false;

        public bool ColorAdjustments { get; set; } = false;

        // ── Lift/Gamma/Gain (grading primario: sombras/medios/altas). Neutro = lift 0, gamma 1, gain 1 ──
        public float LiftR { get; set; } = 0f;
        public float LiftG { get; set; } = 0f;
        public float LiftB { get; set; } = 0f;
        public float GammaR { get; set; } = 1f;
        public float GammaG { get; set; } = 1f;
        public float GammaB { get; set; } = 1f;
        public float GainR { get; set; } = 1f;
        public float GainG { get; set; } = 1f;
        public float GainB { get; set; } = 1f;

        private float _postExposure;
        public float PostExposure
        {
            get => _postExposure;
            set => _postExposure = Math.Clamp(value, -10f, 10f);
        }

        private float _contrast;
        public float Contrast
        {
            get => _contrast;
            set => _contrast = Math.Clamp(value, -100f, 100f);
        }

        private float _hueShift;
        public float HueShift
        {
            get => _hueShift;
            set => _hueShift = Math.Clamp(value, -180f, 180f);
        }

        private float _saturation;
        public float Saturation
        {
            get => _saturation;
            set => _saturation = Math.Clamp(value, -100f, 100f);
        }

        public float ColorFilterR { get; set; } = 1f;
        public float ColorFilterG { get; set; } = 1f;
        public float ColorFilterB { get; set; } = 1f;

        public bool Vignette { get; set; } = false;
        public float VignetteColorR { get; set; } = 0f;
        public float VignetteColorG { get; set; } = 0f;
        public float VignetteColorB { get; set; } = 0f;
        public float VignetteCenterX { get; set; } = 0.5f;
        public float VignetteCenterY { get; set; } = 0.5f;

        private float _vignetteIntensity;
        public float VignetteIntensity
        {
            get => _vignetteIntensity;
            set => _vignetteIntensity = Math.Clamp(value, 0f, 1f);
        }

        private float _vignetteSmoothness = 0.2f;
        public float VignetteSmoothness
        {
            get => _vignetteSmoothness;
            set => _vignetteSmoothness = Math.Clamp(value, 0.01f, 1f);
        }

        public bool VignetteRounded { get; set; } = false;

        public bool ChromaticAberration { get; set; } = false;

        private float _chromaticAberrationIntensity;
        public float ChromaticAberrationIntensity
        {
            get => _chromaticAberrationIntensity;
            set => _chromaticAberrationIntensity = Math.Clamp(value, 0f, 1f);
        }

        public bool AmbientOcclusion { get; set; } = false;

        private float _aoStrength = 0.35f;
        public float AmbientOcclusionStrength
        {
            get => _aoStrength;
            set => _aoStrength = Math.Clamp(value, 0f, 1f);
        }

        public bool Fog { get; set; } = false;
        public float FogDensity { get; set; } = 0.015f;
        public float FogR { get; set; } = 0.48f;
        public float FogG { get; set; } = 0.58f;
        public float FogB { get; set; } = 0.68f;
        public float VolumetricLightStrength { get; set; } = 0.12f;
    }
}
