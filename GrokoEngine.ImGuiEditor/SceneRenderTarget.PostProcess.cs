using OpenTK.Graphics.OpenGL4;
using GrokoEngine;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneRenderTarget
{
    public void ApplyPostProcess(PostProcessSettings? settings)
    {
        // TAA requiere motion vectors e historial. Hasta que ambos existan, degradar de
        // forma explícita a SMAA evita presentar un selector que no hace nada.
        CameraAntiAliasingMode effectiveAa = AntiAliasingMode == CameraAntiAliasingMode.TAA
            ? CameraAntiAliasingMode.SMAA
            : AntiAliasingMode;
        bool useSmaa = effectiveAa == CameraAntiAliasingMode.SMAA;
        bool enabled = settings?.PostProcessEnabled ?? false;
        bool autoExposure = enabled && settings!.AutoExposure;
        float autoExposureMinEv = autoExposure ? settings!.AutoExposureMinEV : -4f;
        float autoExposureMaxEv = autoExposure ? settings!.AutoExposureMaxEV : 4f;
        float autoExposureCompensation = autoExposure ? settings!.AutoExposureCompensation : 0f;
        float autoExposureSpeed = autoExposure ? settings!.AutoExposureAdaptationSpeed : 1.5f;
        float exposure = enabled ? Math.Clamp(settings!.Exposure, 0.001f, 32f) : 1f;
        float gamma = enabled ? Math.Clamp(settings!.Gamma, 0.1f, 4f) : 2.2f;
        int toneMapping = enabled && settings != null && settings.ToneMapping ? 1 : 0;
        int toneMappingMode = enabled ? settings!.ToneMappingMode : 0;
        float wbTemp = enabled ? settings!.WhiteBalanceTemperature : 0f;
        float wbTint = enabled ? settings!.WhiteBalanceTint : 0f;
        int filmGrain = enabled && settings!.FilmGrain ? 1 : 0;
        float filmGrainIntensity = enabled ? settings!.FilmGrainIntensity : 0f;
        float time = (Environment.TickCount64 % 100000L) / 1000f;
        float bloomStrength = enabled && settings!.Bloom ? Math.Clamp(settings.BloomStrength, 0f, 10f) : 0f;
        float bloomThreshold = enabled && settings!.Bloom ? Math.Clamp(settings.BloomThreshold, 0f, 100f) : 1000f;
        float bloomScatter = enabled && settings!.Bloom ? Math.Clamp(settings.BloomScatter, 0f, 1f) : 0.7f;
        float bloomClamp = enabled && settings!.Bloom ? Math.Clamp(settings.BloomClamp, 0f, 65472f) : 65472f;
        float bloomTintR = enabled && settings!.Bloom ? settings.BloomTintR : 1f;
        float bloomTintG = enabled && settings!.Bloom ? settings.BloomTintG : 1f;
        float bloomTintB = enabled && settings!.Bloom ? settings.BloomTintB : 1f;
        int bloomHighQuality = enabled && settings!.Bloom && settings.BloomHighQualityFiltering ? 1 : 0;
        float postExposure = enabled && settings!.ColorAdjustments ? settings.PostExposure : 0f;
        float contrast = enabled && settings!.ColorAdjustments ? settings.Contrast : 0f;
        float hueShift = enabled && settings!.ColorAdjustments ? settings.HueShift : 0f;
        float saturation = enabled && settings!.ColorAdjustments ? settings.Saturation : 0f;
        float colorFilterR = enabled && settings!.ColorAdjustments ? settings.ColorFilterR : 1f;
        float colorFilterG = enabled && settings!.ColorAdjustments ? settings.ColorFilterG : 1f;
        float colorFilterB = enabled && settings!.ColorAdjustments ? settings.ColorFilterB : 1f;
        bool grade = enabled && settings!.ColorAdjustments;
        float liftR = grade ? settings!.LiftR : 0f, liftG = grade ? settings!.LiftG : 0f, liftB = grade ? settings!.LiftB : 0f;
        float gammaR = grade ? settings!.GammaR : 1f, gammaG = grade ? settings!.GammaG : 1f, gammaB = grade ? settings!.GammaB : 1f;
        float gainR = grade ? settings!.GainR : 1f, gainG = grade ? settings!.GainG : 1f, gainB = grade ? settings!.GainB : 1f;
        float vignetteIntensity = enabled && settings!.Vignette ? settings.VignetteIntensity : 0f;
        float vignetteSmoothness = enabled && settings!.Vignette ? settings.VignetteSmoothness : 0.2f;
        float vignetteCenterX = enabled && settings!.Vignette ? settings.VignetteCenterX : 0.5f;
        float vignetteCenterY = enabled && settings!.Vignette ? settings.VignetteCenterY : 0.5f;
        float vignetteColorR = enabled && settings!.Vignette ? settings.VignetteColorR : 0f;
        float vignetteColorG = enabled && settings!.Vignette ? settings.VignetteColorG : 0f;
        float vignetteColorB = enabled && settings!.Vignette ? settings.VignetteColorB : 0f;
        int vignetteRounded = enabled && settings!.Vignette && settings.VignetteRounded ? 1 : 0;
        float chromaticAberration = enabled && settings!.ChromaticAberration ? Math.Clamp(settings.ChromaticAberrationIntensity, 0f, 1f) : 0f;
        float aoStrength = enabled && settings!.AmbientOcclusion ? Math.Clamp(settings.AmbientOcclusionStrength, 0f, 1f) : 0f;
        // Stop NaNs / Dithering son ajustes de salida de la CÁMARA (Camera.StopNaNs/Dithering),
        // independientes de si el PostProcessSettings global está activo o no.
        int stopNaNs = StopNaNsEnabled ? 1 : 0;
        int dithering = DitheringEnabled ? 1 : 0;

        // Pirámide de bloom dedicada: produce el glow en bloomMipTex[0] antes del pase de post.
        if (bloomStrength > 0f)
            RenderBloom(bloomThreshold, bloomScatter, bloomClamp);

        if (autoExposure)
            ApplyAutoExposure(autoExposureMinEv, autoExposureMaxEv, autoExposureCompensation, autoExposureSpeed);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, useSmaa ? smaaSourceFramebuffer : postFramebuffer);
        GL.Viewport(0, 0, Width, Height);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(postShader);
        SetUniform(uSceneLocation, 0);
        SetUniform(uDepthLocation, 1);
        SetUniform(uBloomTexLocation, 2);
        SetUniform(uExposureLocation, exposure);
        SetUniform(uGammaLocation, gamma);
        SetUniform(uToneMappingLocation, toneMapping);
        SetUniform(uToneMappingModeLocation, toneMappingMode);
        SetUniform(uWbTempLocation, wbTemp);
        SetUniform(uWbTintLocation, wbTint);
        SetUniform(uFilmGrainLocation, filmGrain);
        SetUniform(uFilmGrainIntensityLocation, filmGrainIntensity);
        SetUniform(uTimeLocation, time);
        SetUniform(uGodRaysEnabledLocation, GodRaysEnabled && DepthTextureEnabled ? 1 : 0);
        SetUniform(uGodRaySunLocation, GodRaySunU, GodRaySunV);
        SetUniform(uGodRayStrengthLocation, GodRayStrength);
        SetUniform(uContactShadowsEnabledLocation, ContactShadowsEnabled && DepthTextureEnabled ? 1 : 0);
        SetUniform(uContactShadowSunLocation, ContactShadowSunU, ContactShadowSunV);
        SetUniform(uContactShadowLengthLocation, ContactShadowLength);
        SetUniform(uContactShadowSamplesLocation, Math.Clamp(ContactShadowSamples, 4, 24));
        SetUniform(uBloomStrengthLocation, bloomStrength);
        SetUniform(uBloomThresholdLocation, bloomThreshold);
        SetUniform(uBloomScatterLocation, bloomScatter);
        SetUniform(uBloomClampLocation, bloomClamp);
        SetUniform(uBloomTintLocation, bloomTintR, bloomTintG, bloomTintB);
        SetUniform(uBloomHighQualityLocation, bloomHighQuality);
        SetUniform(uPostExposureLocation, postExposure);
        SetUniform(uContrastLocation, contrast);
        SetUniform(uHueShiftLocation, hueShift);
        SetUniform(uSaturationLocation, saturation);
        SetUniform(uColorFilterLocation, colorFilterR, colorFilterG, colorFilterB);
        SetUniform(uLggLiftLocation, liftR, liftG, liftB);
        SetUniform(uLggGammaLocation, gammaR, gammaG, gammaB);
        SetUniform(uLggGainLocation, gainR, gainG, gainB);
        SetUniform(uVignetteIntensityLocation, vignetteIntensity);
        SetUniform(uVignetteSmoothnessLocation, vignetteSmoothness);
        SetUniform(uVignetteCenterLocation, vignetteCenterX, vignetteCenterY);
        SetUniform(uVignetteColorLocation, vignetteColorR, vignetteColorG, vignetteColorB);
        SetUniform(uVignetteRoundedLocation, vignetteRounded);
        SetUniform(uChromaticAberrationLocation, chromaticAberration);
        SetUniform(uAoStrengthLocation, aoStrength);
        SetUniform(uCameraNearLocation, Math.Max(0.001f, CameraNearClip));
        SetUniform(uCameraFarLocation, Math.Max(CameraNearClip + 0.001f, CameraFarClip));
        // SMAA recibe primero una reducción direccional de subpíxel. El segundo pase
        // SMAA conserva detalle y corrige los patrones largos; juntos se acercan mucho
        // más al borde de MSAA de Forward+ que el filtro de cinco taps anterior.
        SetUniform(uFxaaEnabledLocation,
            effectiveAa is CameraAntiAliasingMode.FXAA or CameraAntiAliasingMode.SMAA ? 1 : 0);
        SetUniform(uAutoExposureTextureLocation, 3);
        SetUniform(uAutoExposureEnabledLocation, autoExposure ? 1 : 0);
        SetUniform(uBloomQualityLocation, Math.Clamp(BloomQuality, 0, 3));
        SetUniform(uAoQualityLocation, DepthTextureEnabled ? Math.Clamp(AmbientOcclusionQuality, 0, 3) : 0);
        SetUniform(uStopNaNsLocation, stopNaNs);
        SetUniform(uDitheringLocation, dithering);
        if (uTexelLocation >= 0)
            GL.Uniform2(uTexelLocation, 1f / Math.Max(1, Width), 1f / Math.Max(1, Height));

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, colorTexture);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, depthTexture);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, bloomMipCount > 0 ? bloomMipTex[0] : 0);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, autoExposure ? autoExposureTextures[autoExposureReadIndex] : 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindVertexArray(postVertexArray);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.UseProgram(0);
        GL.DepthMask(true);

        if (useSmaa)
            ApplySmaa();
    }

    private void CachePostUniformLocations()
    {
        uSceneLocation = GL.GetUniformLocation(postShader, "uScene");
        uDepthLocation = GL.GetUniformLocation(postShader, "uDepth");
        uBloomTexLocation = GL.GetUniformLocation(postShader, "uBloomTex");
        uExposureLocation = GL.GetUniformLocation(postShader, "uExposure");
        uGammaLocation = GL.GetUniformLocation(postShader, "uGamma");
        uToneMappingLocation = GL.GetUniformLocation(postShader, "uToneMapping");
        uBloomStrengthLocation = GL.GetUniformLocation(postShader, "uBloomStrength");
        uBloomThresholdLocation = GL.GetUniformLocation(postShader, "uBloomThreshold");
        uBloomScatterLocation = GL.GetUniformLocation(postShader, "uBloomScatter");
        uBloomClampLocation = GL.GetUniformLocation(postShader, "uBloomClamp");
        uBloomTintLocation = GL.GetUniformLocation(postShader, "uBloomTint");
        uBloomHighQualityLocation = GL.GetUniformLocation(postShader, "uBloomHighQuality");
        uPostExposureLocation = GL.GetUniformLocation(postShader, "uPostExposure");
        uContrastLocation = GL.GetUniformLocation(postShader, "uContrast");
        uHueShiftLocation = GL.GetUniformLocation(postShader, "uHueShift");
        uSaturationLocation = GL.GetUniformLocation(postShader, "uSaturation");
        uColorFilterLocation = GL.GetUniformLocation(postShader, "uColorFilter");
        uLggLiftLocation = GL.GetUniformLocation(postShader, "uLggLift");
        uLggGammaLocation = GL.GetUniformLocation(postShader, "uLggGamma");
        uLggGainLocation = GL.GetUniformLocation(postShader, "uLggGain");
        uVignetteIntensityLocation = GL.GetUniformLocation(postShader, "uVignetteIntensity");
        uVignetteSmoothnessLocation = GL.GetUniformLocation(postShader, "uVignetteSmoothness");
        uVignetteCenterLocation = GL.GetUniformLocation(postShader, "uVignetteCenter");
        uVignetteColorLocation = GL.GetUniformLocation(postShader, "uVignetteColor");
        uVignetteRoundedLocation = GL.GetUniformLocation(postShader, "uVignetteRounded");
        uChromaticAberrationLocation = GL.GetUniformLocation(postShader, "uChromaticAberration");
        uAoStrengthLocation = GL.GetUniformLocation(postShader, "uAoStrength");
        uCameraNearLocation = GL.GetUniformLocation(postShader, "uCameraNear");
        uCameraFarLocation = GL.GetUniformLocation(postShader, "uCameraFar");
        uTexelLocation = GL.GetUniformLocation(postShader, "uTexel");
        uFxaaEnabledLocation = GL.GetUniformLocation(postShader, "uFxaaEnabled");
        uAutoExposureTextureLocation = GL.GetUniformLocation(postShader, "uAutoExposureTex");
        uAutoExposureEnabledLocation = GL.GetUniformLocation(postShader, "uAutoExposureEnabled");
        uBloomQualityLocation = GL.GetUniformLocation(postShader, "uBloomQuality");
        uAoQualityLocation = GL.GetUniformLocation(postShader, "uAoQuality");
        uToneMappingModeLocation = GL.GetUniformLocation(postShader, "uToneMappingMode");
        uWbTempLocation = GL.GetUniformLocation(postShader, "uWbTemp");
        uWbTintLocation = GL.GetUniformLocation(postShader, "uWbTint");
        uFilmGrainLocation = GL.GetUniformLocation(postShader, "uFilmGrain");
        uFilmGrainIntensityLocation = GL.GetUniformLocation(postShader, "uFilmGrainIntensity");
        uTimeLocation = GL.GetUniformLocation(postShader, "uTime");
        uGodRaysEnabledLocation = GL.GetUniformLocation(postShader, "uGodRaysEnabled");
        uGodRaySunLocation = GL.GetUniformLocation(postShader, "uGodRaySun");
        uGodRayStrengthLocation = GL.GetUniformLocation(postShader, "uGodRayStrength");
        uStopNaNsLocation = GL.GetUniformLocation(postShader, "uStopNaNs");
        uDitheringLocation = GL.GetUniformLocation(postShader, "uDithering");
        uContactShadowsEnabledLocation = GL.GetUniformLocation(postShader, "uContactShadowsEnabled");
        uContactShadowSunLocation = GL.GetUniformLocation(postShader, "uContactShadowSun");
        uContactShadowLengthLocation = GL.GetUniformLocation(postShader, "uContactShadowLength");
        uContactShadowSamplesLocation = GL.GetUniformLocation(postShader, "uContactShadowSamples");
    }

    private static void SetUniform(int location, int value)
    {
        if (location >= 0) GL.Uniform1(location, value);
    }

    private static void SetUniform(int location, float value)
    {
        if (location >= 0) GL.Uniform1(location, value);
    }

    private static void SetUniform(int location, float x, float y)
    {
        if (location >= 0) GL.Uniform2(location, x, y);
    }

    private static void SetUniform(int location, float x, float y, float z)
    {
        if (location >= 0) GL.Uniform3(location, x, y, z);
    }

    private void CreatePostQuad()
    {
        postVertexArray = GL.GenVertexArray();
        postVertexBuffer = GL.GenBuffer();
        float[] vertices =
        {
            -1f, -1f, 0f, 0f,
             3f, -1f, 2f, 0f,
            -1f,  3f, 0f, 2f
        };
        GL.BindVertexArray(postVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, postVertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    private static int CreatePostShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec2 aPosition;
            layout (location = 1) in vec2 aUv;
            out vec2 vUv;
            void main()
            {
                vUv = aUv;
                gl_Position = vec4(aPosition, 0.0, 1.0);
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec2 vUv;
            uniform sampler2D uScene;
            uniform sampler2D uDepth;
            uniform sampler2D uBloomTex;
            uniform sampler2D uAutoExposureTex;
            uniform float uExposure;
            uniform float uGamma;
            uniform int uToneMapping;
            uniform float uBloomStrength;
            uniform float uBloomThreshold;
            uniform float uBloomScatter;
            uniform float uBloomClamp;
            uniform vec3 uBloomTint;
            uniform int uBloomHighQuality;
            uniform float uPostExposure;
            uniform float uContrast;
            uniform float uHueShift;
            uniform float uSaturation;
            uniform vec3 uColorFilter;
            uniform vec3 uLggLift;
            uniform vec3 uLggGamma;
            uniform vec3 uLggGain;
            uniform float uVignetteIntensity;
            uniform float uVignetteSmoothness;
            uniform vec2 uVignetteCenter;
            uniform vec3 uVignetteColor;
            uniform int uVignetteRounded;
            uniform float uChromaticAberration;
            uniform float uAoStrength;
            uniform float uCameraNear;
            uniform float uCameraFar;
            uniform vec2 uTexel;
            uniform int uFxaaEnabled;
            uniform int uAutoExposureEnabled;
            uniform int uBloomQuality;
            uniform int uAoQuality;
            uniform int uToneMappingMode;
            uniform float uWbTemp;
            uniform float uWbTint;
            uniform int uFilmGrain;
            uniform float uFilmGrainIntensity;
            uniform float uTime;
            uniform int uGodRaysEnabled;
            uniform vec2 uGodRaySun;
            uniform float uGodRayStrength;
            uniform int uStopNaNs;
            uniform int uDithering;
            uniform int uContactShadowsEnabled;
            uniform vec2 uContactShadowSun;
            uniform float uContactShadowLength;
            uniform int uContactShadowSamples;
            out vec4 outColor;

            // Contact Shadows: sombreado de contacto screen-space. Marcha unos pocos texeles desde
            // el pixel HACIA la posicion en pantalla del sol (la misma que usan los God Rays, ya
            // proyectada con la MVP real en C#) y si encuentra geometria mas cerca de la camara que
            // el propio pixel de origen, asume que esa geometria le tapa el sol de cerca (el detalle
            // fino que el shadow map pierde por resolucion/bias: pies, esquinas, objetos pegados al suelo).
            float ContactShadow(vec2 uv)
            {
                if (uContactShadowsEnabled == 0) return 1.0;
                float originRawDepth = texture(uDepth, uv).r;
                if (originRawDepth >= 0.9999) return 1.0; // cielo: no hay a quien hacerle sombra de contacto
                float originDepth = originRawDepth;

                vec2 toSun = uContactShadowSun - uv;
                float toSunLen = length(toSun);
                if (toSunLen < 0.0001) return 1.0;
                vec2 dir = toSun / toSunLen;

                int samples = clamp(uContactShadowSamples, 4, 24);
                float stepLen = uContactShadowLength / float(samples);
                float shadow = 1.0;
                for (int i = 1; i <= samples; i++)
                {
                    vec2 sampleUv = uv + dir * stepLen * float(i);
                    if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
                        break;
                    float rawDepth = texture(uDepth, sampleUv).r;
                    if (rawDepth >= 0.9999) continue;
                    float depthDelta = originDepth - rawDepth;
                    // Sesgo creciente y grosor muy limitado: reduce acne, bandas y el contacto
                    // falso entre objetos separados en la imagen.
                    float t = float(i) / float(samples);
                    float bias = mix(0.0012, 0.0035, t);
                    const float maxThickness = 0.0075;
                    if (depthDelta > bias && depthDelta < maxThickness)
                        shadow = min(shadow, mix(0.65, 1.0, t));
                }
                return shadow;
            }

            // Stop NaNs (Camera.StopNaNs): un solo texel con NaN/Inf (tipico de una division
            // por cero en un shader de material roto) puede propagarse y volver negro/blanco
            // TODO el post-proceso (bloom/AO/tonemap lo esparcen). Lo cortamos apenas se lee la escena.
            vec3 StopNaNsGuard(vec3 c)
            {
                bvec3 bad = bvec3(isnan(c.x) || isinf(c.x), isnan(c.y) || isinf(c.y), isnan(c.z) || isinf(c.z));
                return any(bad) ? vec3(0.0) : c;
            }

            // Dithering (Camera.Dithering): ruido triangular de +-1 LSB antes de cuantizar a 8 bits,
            // rompe el banding visible en degradados suaves (cielos, sombras largas).
            float DitherNoise(vec2 screenXy)
            {
                return fract(sin(dot(screenXy, vec2(12.9898, 78.233))) * 43758.5453123);
            }

            // Filmic ACES curve (Narkowicz 2015 fit): compresses HDR into [0,1]
            // with a cinematic response, just like Unity/Unreal's "ACES" tonemap.
            // This is the single tonemap of the whole pipeline.
            vec3 ACESFilm(vec3 x)
            {
                const float a = 2.51;
                const float b = 0.03;
                const float c = 2.43;
                const float d = 0.59;
                const float e = 0.14;
                return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
            }

            // AgX tonemap (Minimal AgX, Blender/Godot 4). Maneja mucho mejor los colores muy
            // saturados/brillantes que ACES (sin los virajes de tono). Devuelve display lineal;
            // el encode sRGB final del pipeline (pow 1/gamma) lo lleva a pantalla.
            vec3 AgxContrast(vec3 x)
            {
                vec3 x2 = x * x;
                vec3 x4 = x2 * x2;
                return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
            }
            vec3 AgX(vec3 color)
            {
                const mat3 m = mat3(
                    0.842479062253094, 0.0423282422610123, 0.0423756549057051,
                    0.0784335999999992, 0.878468636469772, 0.0784336,
                    0.0792237451477643, 0.0791661274605434, 0.879142973793104);
                const mat3 mInv = mat3(
                    1.19687900512017, -0.0528968517574562, -0.0529716355144438,
                    -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
                    -0.0990297440797205, -0.0989611768448433, 1.15107367264116);
                const float minEv = -12.47393;
                const float maxEv = 4.026069;
                vec3 c = m * max(color, vec3(0.0));
                c = clamp(log2(max(c, vec3(1e-10))), minEv, maxEv);
                c = (c - minEv) / (maxEv - minEv);
                c = AgxContrast(c);
                c = mInv * c;
                c = max(c, vec3(0.0));
                c = pow(c, vec3(2.2));
                return clamp(c, 0.0, 1.0);
            }

            // White balance en HDR: temp (calido/frio), tint (verde/magenta). Aprox por ganancia de canal.
            vec3 WhiteBalance(vec3 color, float temp, float tint)
            {
                float t = temp * 0.1;
                float g = tint * 0.1;
                vec3 balance = vec3(1.0 + t, 1.0 + g * 0.5, 1.0 - t);
                return color * max(balance, vec3(0.0));
            }

            // Film grain animado, modulado por luminancia (mas visible en medios/sombras, como pelicula).
            float GrainHash(vec2 p)
            {
                p = fract(p * vec2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return fract(p.x * p.y);
            }
            vec3 ApplyFilmGrain(vec3 color, vec2 uv)
            {
                if (uFilmGrain == 0) return color;
                float n = GrainHash(uv + fract(uTime) * 13.0) - 0.5;
                float luma = dot(color, vec3(0.299, 0.587, 0.114));
                float amt = uFilmGrainIntensity * 0.12 * (1.0 - luma * 0.6);
                return color + n * amt;
            }

            vec2 SafeUv(vec2 uv)
            {
                return clamp(uv, vec2(0.0), vec2(1.0));
            }

            // God rays / light shafts: marcha radial desde el pixel hacia el sol acumulando la
            // luminancia del CIELO (depth ~1); la geometria (depth < 1) bloquea -> crea los rayos.
            vec3 GodRays(vec2 uv)
            {
                const int SAMPLES = 48;
                vec2 delta = (uv - uGodRaySun) * (0.9 / float(SAMPLES));
                vec2 coord = uv;
                float decay = 1.0;
                vec3 sum = vec3(0.0);
                for (int i = 0; i < SAMPLES; i++)
                {
                    coord -= delta;
                    vec2 c = clamp(coord, vec2(0.0), vec2(1.0));
                    float d = texture(uDepth, c).r;
                    float sky = step(0.9995, d);
                    sum += texture(uScene, c).rgb * sky * decay;
                    decay *= 0.96;
                }
                return sum * (uGodRayStrength * 1.4 / float(SAMPLES));
            }

            vec3 SampleSceneColor(vec2 uv)
            {
                uv = SafeUv(uv);
                float chroma = clamp(uChromaticAberration, 0.0, 1.0);
                if (chroma <= 0.0001)
                    return texture(uScene, uv).rgb;

                vec2 dir = uv - vec2(0.5);
                vec2 offset = dir * chroma * 0.018;
                float r = texture(uScene, SafeUv(uv + offset)).r;
                float g = texture(uScene, uv).g;
                float b = texture(uScene, SafeUv(uv - offset)).b;
                return vec3(r, g, b);
            }

            vec3 ExtractBloom(vec3 color)
            {
                color = min(color, vec3(max(uBloomClamp, 0.0)));
                float brightness = max(max(color.r, color.g), color.b);
                float threshold = max(uBloomThreshold, 0.0);
                float knee = max(threshold * clamp(uBloomScatter, 0.0, 1.0) * 0.5, 0.0001);
                float soft = clamp((brightness - threshold + knee) / (2.0 * knee), 0.0, 1.0);
                soft = soft * soft * knee;
                float contribution = max(brightness - threshold, soft) / max(brightness, 0.0001);
                return color * contribution;
            }

            // El bloom se calcula en una PIRAMIDE dedicada (downsample/upsample con FBOs, estilo
            // Unity HDRP) en RenderBloom, ya prefiltrado (umbral) y muy suavizado (tent). Aqui solo
            // se muestrea ese resultado y se aplica el tinte; la intensidad se aplica al sumarlo.
            vec3 SampleBloom(vec2 uv)
            {
                return texture(uBloomTex, uv).rgb * max(uBloomTint, vec3(0.0));
            }

            float LinearDepth(float d)
            {
                float n = max(uCameraNear, 0.001);
                float f = max(uCameraFar, n + 0.001);
                return (2.0 * n * f) / max(f + n - (d * 2.0 - 1.0) * (f - n), 0.0001);
            }

            float ScreenAO(vec2 uv)
            {
                if (uAoStrength <= 0.0 || uAoQuality <= 0) return 1.0;
                float centerRaw = texture(uDepth, uv).r;
                if (centerRaw >= 0.9999) return 1.0;
                float center = LinearDepth(centerRaw);

                // GTAO-style screen AO: multi-ring, rotated per pixel, with a
                // depth-aware range falloff. It is still a single post pass, but
                // behaves much closer to modern engine AO than the old 4-tap
                // darkening, especially in corners and contact areas.
                float occ = 0.0;
                float weight = 0.0;
                int sampleCount = uAoQuality == 1 ? 8 : (uAoQuality == 2 ? 12 : 16);
                float radius = uAoQuality == 1 ? 1.75 : (uAoQuality == 2 ? 2.75 : 4.0);
                float noise = fract(sin(dot(floor(uv / max(uTexel, vec2(0.000001))), vec2(12.9898, 78.233)) + uTime * 0.37) * 43758.5453);
                float angle = noise * 6.28318530718;
                mat2 rot = mat2(cos(angle), -sin(angle), sin(angle), cos(angle));
                vec2 offsets[16] = vec2[](
                    vec2( 1.000,  0.000), vec2( 0.707,  0.707), vec2( 0.000,  1.000), vec2(-0.707,  0.707),
                    vec2(-1.000,  0.000), vec2(-0.707, -0.707), vec2( 0.000, -1.000), vec2( 0.707, -0.707),
                    vec2( 1.700,  0.300), vec2( 0.300,  1.700), vec2(-1.700,  0.300), vec2(-0.300,  1.700),
                    vec2( 1.350, -1.350), vec2(-1.350, -1.350), vec2( 2.150,  1.050), vec2(-2.150, -1.050)
                );

                for (int i = 0; i < 16; i++)
                {
                    if (i >= sampleCount) break;
                    float ring = 0.45 + float(i % 4) * 0.23;
                    vec2 o = rot * offsets[i] * uTexel * radius * ring;
                    float d = texture(uDepth, SafeUv(uv + o)).r;
                    if (d >= 0.9999) continue;
                    float dz = center - LinearDepth(d);
                    float range = 1.0 - smoothstep(0.08, max(0.35, center * 0.12), abs(dz));
                    float horizon = smoothstep(0.02, max(0.15, center * 0.06), dz);
                    float sampleWeight = range * (1.0 / (1.0 + dot(o / max(uTexel, vec2(0.000001)), o / max(uTexel, vec2(0.000001))) * 0.018));
                    occ += horizon * sampleWeight;
                    weight += sampleWeight;
                }

                float ao = 1.0 - pow(clamp(occ / max(weight, 0.0001), 0.0, 1.0), 0.82) * uAoStrength;
                return clamp(ao, 0.58, 1.0);
            }

            float Luma(vec3 color)
            {
                return dot(color, vec3(0.299, 0.587, 0.114));
            }

            vec3 ApplyFxaa(vec2 uv, vec3 center)
            {
                if (uFxaaEnabled == 0) return center;
                // FXAA 3-style directional resolve: suaviza a lo largo del borde, no
                // en cruz. Evita que costuras y detalles de materiales queden lavados.
                vec3 nw = texture(uScene, SafeUv(uv + vec2(-1.0, -1.0) * uTexel)).rgb;
                vec3 ne = texture(uScene, SafeUv(uv + vec2( 1.0, -1.0) * uTexel)).rgb;
                vec3 sw = texture(uScene, SafeUv(uv + vec2(-1.0,  1.0) * uTexel)).rgb;
                vec3 se = texture(uScene, SafeUv(uv + vec2( 1.0,  1.0) * uTexel)).rgb;
                float lumaNW = Luma(nw);
                float lumaNE = Luma(ne);
                float lumaSW = Luma(sw);
                float lumaSE = Luma(se);
                float lumaM = Luma(center);
                float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
                float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));
                if (lumaMax - lumaMin < max(0.025, lumaMax * 0.075)) return center;

                vec2 direction;
                direction.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
                direction.y =  ((lumaNW + lumaSW) - (lumaNE + lumaSE));
                float reduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * 0.03125, 0.0078125);
                float inverseMin = 1.0 / (min(abs(direction.x), abs(direction.y)) + reduce);
                direction = clamp(direction * inverseMin, vec2(-8.0), vec2(8.0)) * uTexel;

                vec3 sampleA = 0.5 * (
                    texture(uScene, SafeUv(uv + direction * (1.0 / 3.0 - 0.5))).rgb +
                    texture(uScene, SafeUv(uv + direction * (2.0 / 3.0 - 0.5))).rgb);
                vec3 sampleB = sampleA * 0.5 + 0.25 * (
                    texture(uScene, SafeUv(uv + direction * -0.5)).rgb +
                    texture(uScene, SafeUv(uv + direction *  0.5)).rgb);
                float lumaB = Luma(sampleB);
                return (lumaB < lumaMin || lumaB > lumaMax) ? sampleA : sampleB;
            }

            vec3 ApplyHueShift(vec3 color, float degrees)
            {
                float angle = radians(degrees);
                float s = sin(angle);
                float c = cos(angle);
                mat3 rgb2yiq = mat3(
                    0.299,  0.596,  0.211,
                    0.587, -0.274, -0.523,
                    0.114, -0.322,  0.312
                );
                mat3 yiq2rgb = mat3(
                    1.0,  1.0,    1.0,
                    0.956, -0.272, -1.106,
                    0.621, -0.647,  1.703
                );
                vec3 yiq = color * rgb2yiq;
                yiq.yz = mat2(c, -s, s, c) * yiq.yz;
                return max(yiq * yiq2rgb, vec3(0.0));
            }

            // Lift/Gamma/Gain (grading primario tipo DaVinci/Unreal). Lift sube las sombras
            // (offset ponderado a oscuros), Gain multiplica (afecta sobre todo las altas luces),
            // Gamma es la curva de medios. Neutro: lift 0, gamma 1, gain 1 -> identidad.
            vec3 ApplyLiftGammaGain(vec3 color)
            {
                color = uLggGain * (color + uLggLift * (1.0 - color));
                color = pow(max(color, vec3(0.0)), 1.0 / max(uLggGamma, vec3(0.0001)));
                return color;
            }

            vec3 ApplyColorAdjustments(vec3 color)
            {
                color = ApplyLiftGammaGain(color);
                color *= max(uColorFilter, vec3(0.0));
                color = ApplyHueShift(color, uHueShift);
                float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
                color = mix(vec3(luma), color, max(0.0, 1.0 + uSaturation * 0.01));
                color = (color - vec3(0.5)) * max(0.0, 1.0 + uContrast * 0.01) + vec3(0.5);
                return max(color, vec3(0.0));
            }

            vec3 ApplyVignette(vec3 color, vec2 uv)
            {
                float intensity = clamp(uVignetteIntensity, 0.0, 1.0);
                if (intensity <= 0.0001)
                    return color;

                vec2 delta = uv - uVignetteCenter;
                if (uVignetteRounded != 0)
                    delta.x *= max(uTexel.y / max(uTexel.x, 0.000001), 0.000001);

                float dist = length(delta) * 2.0;
                float smoothness = max(uVignetteSmoothness, 0.01);
                float edge = mix(1.25, 0.35, intensity);
                float mask = smoothstep(edge, edge + smoothness, dist);
                return mix(color, uVignetteColor, mask * intensity);
            }

            void main()
            {
                // Scene arrives in HDR (linear in Linear mode). Correct display
                // transform order: chromatic/FXAA -> bloom -> AO -> exposure ->
                // tonemap -> color adjustments -> vignette -> encode.
                // Doing bloom here (not in the object shader) is what lets us
                // actually extract highlights above 1.0.
                vec3 hdr = SampleSceneColor(vUv);
                if (uStopNaNs == 1)
                    hdr = StopNaNsGuard(hdr);
                hdr = ApplyFxaa(vUv, hdr);
                if (uBloomStrength > 0.0001)
                    hdr += SampleBloom(vUv) * uBloomStrength;
                if (uGodRaysEnabled == 1)
                    hdr += GodRays(vUv);
                hdr *= ScreenAO(vUv);
                hdr *= ContactShadow(vUv);
                float cameraExposure = uAutoExposureEnabled == 1
                    ? max(texture(uAutoExposureTex, vec2(0.5)).r, 0.0001)
                    : 1.0;
                hdr *= uExposure * cameraExposure * exp2(uPostExposure);
                hdr = WhiteBalance(hdr, uWbTemp, uWbTint);
                vec3 mapped;
                if (uToneMapping != 0)
                    mapped = uToneMappingMode == 1 ? AgX(hdr) : ACESFilm(hdr);
                else
                    mapped = clamp(hdr, 0.0, 1.0);
                mapped = ApplyColorAdjustments(mapped);
                mapped = ApplyVignette(mapped, vUv);
                mapped = ApplyFilmGrain(mapped, vUv);
                // Final display encode, ALWAYS applied (done exactly once in the
                // whole pipeline). Both color spaces need this linear->sRGB step
                // to look right on an sRGB monitor: Linear decoded its textures up
                // front, Gamma did not, but the lit values still have to be encoded
                // for display. Skipping it (the old "pure gamma" path) wrote raw
                // values to the screen and made everything look very dark.
                mapped = pow(clamp(mapped, 0.0, 1.0), vec3(1.0 / max(uGamma, 0.01)));
                outColor = vec4(mapped, 1.0);
            }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }
}
