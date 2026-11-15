using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    public Vector2 SunScreenUv { get; private set; }
    public bool SunGodRaysActive { get; private set; }
    public float SunGodRaysStrength { get; private set; }
    public Vector3 SunGodRayColor { get; private set; }
    // Contact Shadows (screen-space): reutiliza la MISMA posicion en pantalla del sol que God
    // Rays (ya calculada con la MVP real) como direccion de marcha hacia la luz, asi que no hace
    // falta reconstruir matrices dentro del shader de post-proceso.
    public bool SunContactShadowsActive { get; private set; }
    public float SunContactShadowLength { get; private set; }
    public int SunContactShadowSamples { get; private set; }

    private void ComputeSunScreenPosition(IReadOnlyList<GameObject> objects, Vector3 eye, Matrix4 mvp, Vector3 camFront, float farClip)
    {
        SunGodRaysActive = false;
        SunContactShadowsActive = false;
        var sun = FindPrimaryDirectionalLight(objects);
        if (sun == null || sun.Intensity <= 0.0001f)
            return;
        // El sol en el cielo esta en la direccion OPUESTA a la que viaja la luz; debe estar delante.
        Vector3 sunSky = (ToTk(sun.GetNormalizedDirection()).Normalized() * -1f);
        if (Vector3.Dot(sunSky, camFront.Normalized()) <= 0.05f)
            return;
        // No proyectar el sol a una distancia arbitraria de 100 km: con una cámara de far clip
        // corto se sale de precisión y el origen de los rayos se desfasaba respecto al viewport.
        float sunProjectionDistance = Math.Clamp(Math.Max(1f, farClip) * 0.5f, 10f, 5000f);
        var clip = new Vector4(eye + sunSky * sunProjectionDistance, 1f) * mvp;
        if (clip.W <= 0.0001f)
            return;
        SunScreenUv = new Vector2((clip.X / clip.W) * 0.5f + 0.5f, (clip.Y / clip.W) * 0.5f + 0.5f);
        bool rayOriginVisible = SunScreenUv.X >= -0.05f && SunScreenUv.X <= 1.05f && SunScreenUv.Y >= -0.05f && SunScreenUv.Y <= 1.05f;
        if (sun.GodRays && rayOriginVisible)
        {
            SunGodRaysActive = true;
            SunGodRaysStrength = sun.GodRaysStrength;
            SunGodRayColor = ToTk(sun.GetEffectiveColor());
        }
        if (sun.ContactShadows)
        {
            SunContactShadowsActive = true;
            SunContactShadowLength = sun.ContactShadowLength;
            SunContactShadowSamples = sun.ContactShadowSamples;
        }
    }

    private void CollectFrameLighting(IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        if (_frameLightsCollectedFor == _frameCount)
            return;
        _frameLightsCollectedFor = _frameCount;

        _frameAmbient = FindComponent<AmbientLight>(objects);
        _framePostProcess = FindComponent<PostProcessSettings>(objects);
        _frameDir = FindPrimaryDirectionalLight(objects);
        if (_frameDir is { Mode: LightMode.Mixed, ShadowmaskBakedFallback: true })
            _frameDir = null;

        _framePointLights.Clear();
        CollectComponents(objects, _framePointLights, int.MaxValue);
        _framePointLights.RemoveAll(light => light.Mode == LightMode.Baked || (light.Mode == LightMode.Mixed && light.ShadowmaskBakedFallback));
        bool highLightBudget = RenderingPath == RendererRenderingPath.ForwardPlus || RenderingPath == RendererRenderingPath.Deferred;
        int pointBudget = highLightBudget ? MaxPointLights : Math.Min(4, MaxPointLights);
        int spotBudget = highLightBudget ? MaxSpotLights : Math.Min(2, MaxSpotLights);
        int areaBudget = highLightBudget ? MaxAreaLights : Math.Min(2, MaxAreaLights);
        TrimLightsByImportance(_framePointLights, pointBudget, cameraPosition, l => l.Intensity, l => l.Range);
        _frameSpotLights.Clear();
        CollectComponents(objects, _frameSpotLights, int.MaxValue);
        _frameSpotLights.RemoveAll(light => light.Mode == LightMode.Baked || (light.Mode == LightMode.Mixed && light.ShadowmaskBakedFallback));
        TrimLightsByImportance(_frameSpotLights, spotBudget, cameraPosition, l => l.Intensity, l => l.Range);
        _frameAreaLights.Clear();
        CollectComponents(objects, _frameAreaLights, int.MaxValue);
        TrimLightsByImportance(_frameAreaLights, areaBudget, cameraPosition, l => l.Intensity, l => l.Range);
        _frameRectLights.Clear();
        CollectComponents(objects, _frameRectLights, int.MaxValue);
        TrimLightsByImportance(_frameRectLights, Math.Max(0, areaBudget - _frameAreaLights.Count), cameraPosition, l => l.Intensity, l => l.Range);
        _frameReflectionProbes.Clear();
        // Durante el BAKE de un probe no se recolectan probes: así la captura no incluye reflejos
        // de probes (ni del propio probe reflejándose a sí mismo). Sin esto, cada re-bake captura
        // el reflejo anterior y se degrada hasta perderse (recursión), como evita Unity.
        if (!SuppressReflectionProbes)
            CollectComponents(objects, _frameReflectionProbes, int.MaxValue);
    }

    private void ApplySceneLighting(IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        CollectFrameLighting(objects, cameraPosition);   // recolección 1x por Render; aquí solo se SUBEN los uniforms

        // Ambient
        var ambient = _frameAmbient;
        // Un entorno sin Ambient Light, HDRI ni cielo no aporta iluminación. El relleno
        // gris anterior hacía que una habitación cerrada siguiese visible al apagar luces.
        var ambColor = ambient != null
            ? new Vector3(ambient.R, ambient.G, ambient.B)
            : Vector3.Zero;
        float ambIntensity = ambient?.Intensity ?? 0f;
        // Indirect Multiplier del sol: su luz de rebote escala el fill ambiente (aprox. de GI).
        if (_frameDir != null) ambIntensity *= _frameDir.IndirectMultiplier;
        GL.Uniform3(uAmbientColor, ambColor);
        GL.Uniform1(uAmbientIntensity, ambIntensity);
        GL.Uniform1(uSkyStrength, ambient?.SkyStrength ?? 0f);
        GL.Uniform3(uCameraPos, cameraPosition);
        GL.Uniform1(solidMixedLightingModeLocation, (int)MixedLightingMode);
        GL.Uniform1(solidDistanceShadowmaskLocation, DistanceShadowmask ? 1 : 0);
        GL.Uniform1(solidShadowmaskDistanceLocation, Math.Clamp(ShadowmaskDistance, 1f, 500f));
        GL.Uniform1(solidShadowmaskDistanceFadeLocation, Math.Clamp(ShadowmaskDistanceFade, 0.01f, Math.Max(ShadowmaskDistance, 0.01f)));

        var pp = _framePostProcess;
        bool ppEnabled = pp?.Enabled ?? false;
        // Modo PRO: espacio de color seleccionable desde Settings -> Lighting.
        // El shader de objetos solo lo usa para decidir si decodifica sRGB->linear
        // a la ENTRADA (albedo/texturas). La exposición, el tonemap ACES y la
        // codificación final a sRGB viven en el post-proceso (un único display
        // transform), por eso uExposure/uGamma/uToneMapping/uBloomStrength ya no
        // se suben aquí: hacerlo además rompía el bloom y duplicaba la gamma.
        GL.Uniform1(uColorSpaceLinear, ColorSpace == ColorSpace.Linear ? 1 : 0);
        // IBL pro seguro: diffuse irradiance + reflections controladas por material.
        // El shader suaviza automaticamente el specular cuando hay normal maps para
        // evitar artefactos blancos en bordes y costuras.
        GL.Uniform1(uUseIBL, ImageBasedLighting ? 1 : 0);
        GL.Uniform1(uIblDiffuseStrength, Math.Clamp(IblDiffuseStrength, 0f, 1.5f));
        GL.Uniform1(uIblSpecularStrength, Math.Clamp(IblSpecularStrength, 0f, 0.35f));
        GL.ActiveTexture(TextureUnit.Texture9);
        GL.BindTexture(TextureTarget.Texture2D, _brdfLutTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(uBrdfLut, 9);
        GL.Uniform1(uHasBrdfLut, _brdfLutTexture != 0 ? 1 : 0);
        ApplyActiveReflectionProbe(cameraPosition);
        // HDRI de entorno en la unidad 7 (libre: 0-6 las usan albedo/sombras/mapas).
        if (_envLoaded)
        {
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.Texture2D, _envTexture);
            GL.ActiveTexture(TextureUnit.Texture8);
            GL.BindTexture(TextureTarget.TextureCubeMap, _envPrefilterCube);
            GL.ActiveTexture(TextureUnit.Texture9);
            GL.BindTexture(TextureTarget.Texture2D, _brdfLutTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(uEnvMap, 7);
            GL.Uniform1(uHasEnvMap, 1);
            GL.Uniform1(uEnvMaxLod, _envMaxLod);
            GL.Uniform3(uEnvDiffuseColor, _envDiffuseColor);
            GL.Uniform1(uUseEnvSh, 1);
            for (int i = 0; i < _envSh.Length; i++)
                GL.Uniform3(uEnvSh[i], _envSh[i]);
            GL.Uniform1(uEnvPrefilterMap, 8);
            GL.Uniform1(uHasEnvPrefilterMap, _envPrefilterCube != 0 ? 1 : 0);
            GL.Uniform1(uEnvPrefilterMaxLod, _envPrefilterMaxLod);
        }
        else
        {
            // Sin HDRI: ambiente plano de siempre, salvo que el modo sea ProceduralSky, en cuyo
            // caso se tine con el promedio barato del cielo analitico (ver DrawProceduralSky).
            if (EnvironmentMode == EnvironmentMode.ProceduralSky)
                UpdateProceduralSkyAmbient(_frameDir);
            else
                _flatAmbientColor = Vector3.Zero;
            GL.Uniform1(uHasEnvMap, 0);
            GL.Uniform3(uEnvDiffuseColor, _flatAmbientColor);
            GL.Uniform1(uUseEnvSh, 0);
            GL.Uniform1(uHasEnvPrefilterMap, 0);
        }
        GL.Uniform1(uAoStrength, 0f);
        GL.Uniform1(uFogDensity, ppEnabled && pp!.Fog ? pp.FogDensity : 0f);
        GL.Uniform3(uFogColor, ppEnabled ? new Vector3(pp!.FogR, pp.FogG, pp.FogB) : new Vector3(0.48f, 0.58f, 0.68f));
        GL.Uniform1(uFogHeight, ppEnabled && pp!.Fog ? pp.FogHeight : 0f);
        GL.Uniform1(uFogHeightFalloff, ppEnabled && pp!.Fog ? Math.Clamp(pp.FogHeightFalloff, 0f, 4f) : 0f);
        GL.Uniform1(uVolumetricStrength, ppEnabled ? pp!.VolumetricLightStrength : 0f);
        GL.Uniform1(uDebugView, DebugView);

        // Directional light: dirección calculada desde la rotación del GameObject.
        var dir = _frameDir;
        if (dir != null && dir.gameObject != null)
        {
            GL.Uniform3(uDirDir, ToTk(dir.GetNormalizedDirection()).Normalized());
            GL.Uniform3(uDirColor, ToTk(dir.GetEffectiveColor()));   // Filter * temperatura (Kelvin)
            GL.Uniform1(uDirIntensity, dir.Intensity);
            GL.Uniform1(solidDirShadowmaskChannelLocation, dir.Mode == LightMode.Mixed ? dir.ShadowmaskChannel : -1);
        }
        else
        {
            // Sin DirectionalLight en escena: sin sol de relleno (iluminación limpia, solo ambiente).
            GL.Uniform3(uDirDir, new Vector3(0f, -1f, 0f));
            GL.Uniform3(uDirColor, Vector3.Zero);
            GL.Uniform1(uDirIntensity, 0f);
            GL.Uniform1(solidDirShadowmaskChannelLocation, -1);
        }

        GL.Uniform1(uPointCount, _framePointLights.Count);
        for (int i = 0; i < _framePointLights.Count; i++)
        {
            var pl = _framePointLights[i];
            var pos = pl.gameObject != null ? ComputeWorldPosition(pl.gameObject) : Vector3.Zero;
            GL.Uniform3(uPointPos[i], pos);
            GL.Uniform3(uPointColor[i], ToTk(pl.GetEffectiveColor()));
            GL.Uniform1(uPointIntensity[i], pl.Intensity);
            GL.Uniform1(uPointRange[i], pl.Range);
            GL.Uniform1(solidPointShadowmaskChannelLocations[i], pl.Mode == LightMode.Mixed ? pl.ShadowmaskChannel : -1);
        }

        GL.Uniform1(uSpotCount, _frameSpotLights.Count);
        for (int i = 0; i < _frameSpotLights.Count; i++)
        {
            var sl = _frameSpotLights[i];
            var pos = sl.gameObject != null ? ComputeWorldPosition(sl.gameObject) : Vector3.Zero;
            var spotDirection = ResolveSpotDirection(sl);
            GL.Uniform3(uSpotPos[i], pos);
            GL.Uniform3(uSpotDir[i], spotDirection);
            GL.Uniform3(uSpotColor[i], ToTk(sl.GetEffectiveColor()));
            GL.Uniform1(uSpotIntensity[i], sl.Intensity);
            GL.Uniform1(uSpotRange[i], sl.Range);
            GL.Uniform1(uSpotAngle[i], MathHelper.DegreesToRadians(sl.Angle));
            GL.Uniform1(solidSpotShadowmaskChannelLocations[i], sl.Mode == LightMode.Mixed ? sl.ShadowmaskChannel : -1);
            int shadowSlot = -1;
            for (int slot = 0; slot < MaxSpotLights; slot++)
            {
                if (ReferenceEquals(_cachedSpotShadowLights[slot], sl))
                {
                    shadowSlot = slot;
                    break;
                }
            }
            GL.Uniform1(uSpotShadowSlotLocations[i], shadowSlot);
        }
        ApplySpotCookiesAndIes();
        ApplyPointIesArray();

        int areaCount = Math.Min(MaxAreaLights, _frameAreaLights.Count + _frameRectLights.Count);
        GL.Uniform1(uAreaCount, areaCount);
        int areaIndex = 0;
        foreach (var area in _frameAreaLights)
        {
            if (areaIndex >= MaxAreaLights) break;
            UploadAreaLight(areaIndex++, area.gameObject, area.R, area.G, area.B, area.Intensity, area.Range, area.Width, area.Height);
        }
        foreach (var rect in _frameRectLights)
        {
            if (areaIndex >= MaxAreaLights) break;
            UploadAreaLight(areaIndex++, rect.gameObject, rect.R, rect.G, rect.B, rect.Intensity, rect.Range, rect.Width, rect.Height);
        }
    }

    private int ResolvePointShadowLightIndex(PointShadowInfo pointShadow)
    {
        if (!pointShadow.Enabled || pointShadow.Light == null)
            return -1;

        return _framePointLights.IndexOf(pointShadow.Light);
    }

    private void ApplyActiveReflectionProbe(Vector3 cameraPosition)
    {
        _activeReflectionProbeCubemapPath = null;
        _activeReflectionProbeTexture = 0;
        _activeReflectionProbeMaxLod = 0f;
        var (primary, secondary) = SelectReflectionProbes(cameraPosition);
        if (primary is { } probe)
        {
            _activeReflectionProbeCubemapPath = NormalizeExistingAssetPath(probe.Probe.EffectiveCubemapPath) ?? probe.Probe.EffectiveCubemapPath;
            _activeReflectionProbeTexture = probe.Texture;
            _activeReflectionProbeMaxLod = probe.MaxLod;
        }

        UploadReflectionProbe(primary, 10,
            uActiveReflectionProbeMap, uHasActiveReflectionProbeMap, uActiveReflectionProbeMaxLod,
            uActiveReflectionProbeCenter, uActiveReflectionProbeHalfSize, uActiveReflectionProbeBlendDistance,
            uActiveReflectionProbeIntensity, uActiveReflectionProbeBoxProjection);
        UploadReflectionProbe(secondary, 14,
            uSecondaryReflectionProbeMap, uHasSecondaryReflectionProbeMap, uSecondaryReflectionProbeMaxLod,
            uSecondaryReflectionProbeCenter, uSecondaryReflectionProbeHalfSize, uSecondaryReflectionProbeBlendDistance,
            uSecondaryReflectionProbeIntensity, uSecondaryReflectionProbeBoxProjection);
    }

    private readonly record struct ReflectionProbeBinding(ReflectionProbe Probe, Vector3 Center, Vector3 HalfSize, int Texture, float MaxLod);

    // Selección CPU estable (importance y proximidad) + mezcla GPU por píxel. Dos cubemaps son
    // suficientes para cruzar puertas y solapamientos sin el coste/limite de samplers de una lista
    // arbitraria; el peso final sigue dependiendo de cada volumen, no de la cámara.
    private (ReflectionProbeBinding? Primary, ReflectionProbeBinding? Secondary) SelectReflectionProbes(Vector3 cameraPosition)
    {
        ReflectionProbeBinding? primary = null, secondary = null;
        float primaryScore = float.NegativeInfinity, secondaryScore = float.NegativeInfinity;

        foreach (var probe in _frameReflectionProbes)
        {
            if (probe == null || !probe.Enabled || probe.gameObject == null || string.IsNullOrWhiteSpace(probe.EffectiveCubemapPath))
                continue;
            if (!TryGetReflectionProbeCubeTexture(probe.EffectiveCubemapPath, out int texture, out float maxLod))
                continue;

            Vector3 position = ComputeWorldPosition(probe.gameObject);
            Vector3 center = new(position.X + probe.CenterX, position.Y + probe.CenterY, position.Z + probe.CenterZ);
            Vector3 halfSize = new(
                MathF.Max(0.01f, probe.SizeX) * 0.5f,
                MathF.Max(0.01f, probe.SizeY) * 0.5f,
                MathF.Max(0.01f, probe.SizeZ) * 0.5f);
            float score = probe.Importance * 1000f - (cameraPosition - center).Length * 0.01f;
            var candidate = new ReflectionProbeBinding(probe, center, halfSize, texture, maxLod);
            if (score > primaryScore)
            {
                secondary = primary;
                secondaryScore = primaryScore;
                primary = candidate;
                primaryScore = score;
            }
            else if (score > secondaryScore)
            {
                secondary = candidate;
                secondaryScore = score;
            }
        }
        return (primary, secondary);
    }

    private static void UploadReflectionProbe(ReflectionProbeBinding? binding, int textureUnit,
        int mapLocation, int hasMapLocation, int maxLodLocation, int centerLocation, int halfSizeLocation,
        int blendDistanceLocation, int intensityLocation, int boxProjectionLocation)
    {
        GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + textureUnit));
        GL.BindTexture(TextureTarget.TextureCubeMap, binding?.Texture ?? 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(mapLocation, textureUnit);
        GL.Uniform1(hasMapLocation, binding.HasValue ? 1 : 0);
        GL.Uniform1(maxLodLocation, binding?.MaxLod ?? 0f);
        GL.Uniform3(centerLocation, binding?.Center ?? Vector3.Zero);
        GL.Uniform3(halfSizeLocation, binding?.HalfSize ?? Vector3.One);
        GL.Uniform1(blendDistanceLocation, binding.HasValue ? MathF.Max(0.001f, binding.Value.Probe.BlendDistance) : 0.001f);
        GL.Uniform1(intensityLocation, binding.HasValue ? Math.Clamp(binding.Value.Probe.Intensity, 0f, 2f) : 0f);
        GL.Uniform1(boxProjectionLocation, binding.HasValue && binding.Value.Probe.BoxProjection ? 1 : 0);
    }

    private void UploadAreaLight(int index, GameObject? obj, float r, float g, float b, float intensity, float range, float width, float height)
    {
        var pos = obj != null ? ComputeWorldPosition(obj) : Vector3.Zero;
        var dir = obj != null ? ForwardFromGameObject(obj) : new Vector3(0f, -1f, 0f);
        GL.Uniform3(uAreaPos[index], pos);
        GL.Uniform3(uAreaDir[index], dir);
        GL.Uniform3(uAreaColor[index], new Vector3(r, g, b));
        GL.Uniform1(uAreaIntensity[index], intensity);
        GL.Uniform1(uAreaRange[index], range);
        GL.Uniform2(uAreaSize[index], new Vector2(width, height));
    }

    // Cookie + IES POR LUZ: hasta MaxSpotLights spots con cookie/IES propios, y hasta
    // MaxPointLights points con IES propio, todos a la vez (ya no solo la luz [0]).
    private void ApplySpotCookiesAndIes()
    {
        EnsureSpotCookieArray();
        EnsureIesArray(ref _spotIesArrayTex, MaxSpotLights);

        for (int i = 0; i < MaxSpotLights; i++)
        {
            var sl = i < _frameSpotLights.Count ? _frameSpotLights[i] : null;
            string cookiePath = sl?.CookiePath ?? "";
            UpdateSpotCookieLayer(i, cookiePath);
            bool hasCookie = sl != null && !string.IsNullOrWhiteSpace(cookiePath);
            GL.Uniform1(uSpotHasCookieLoc[i], hasCookie ? 1 : 0);
            if (hasCookie)
            {
                _spotCookieMvpScratch = ComputeSpotCookieMatrix(sl!);
                GL.UniformMatrix4(uSpotCookieMvpLoc[i], false, ref _spotCookieMvpScratch);
            }

            string iesPath = sl?.IesProfilePath ?? "";
            UpdateIesLayer(_spotIesArrayTex, _spotIesPathPerSlot, i, iesPath);
            bool hasIes = sl != null && !string.IsNullOrWhiteSpace(iesPath);
            GL.Uniform1(uSpotHasIesLoc[i], hasIes ? 1 : 0);
        }

        GL.ActiveTexture(TextureUnit.Texture11);
        GL.BindTexture(TextureTarget.Texture2DArray, _spotCookieArrayTex);
        GL.ActiveTexture(TextureUnit.Texture12);
        GL.BindTexture(TextureTarget.Texture2DArray, _spotIesArrayTex);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(uSpotCookieLocation, 11);
        GL.Uniform1(uSpotIesLutLocation, 12);
    }

    private Matrix4 _spotCookieMvpScratch;

    private Matrix4 ComputeSpotCookieMatrix(SpotLight spot)
    {
        var pos = spot.gameObject != null ? ComputeWorldPosition(spot.gameObject) : Vector3.Zero;
        var dir = ResolveSpotDirection(spot);
        var up = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.92f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4 view = Matrix4.LookAt(pos, pos + dir, up);
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(Math.Clamp(spot.Angle, 1f, 170f)),
            1f,
            0.05f,
            Math.Max(0.1f, spot.Range));
        return view * projection;
    }

    private void ApplyPointIesArray()
    {
        EnsureIesArray(ref _pointIesArrayTex, MaxPointLights);

        for (int i = 0; i < MaxPointLights; i++)
        {
            var pl = i < _framePointLights.Count ? _framePointLights[i] : null;
            string iesPath = pl?.IesProfilePath ?? "";
            UpdateIesLayer(_pointIesArrayTex, _pointIesPathPerSlot, i, iesPath);
            bool hasIes = pl != null && !string.IsNullOrWhiteSpace(iesPath);
            GL.Uniform1(uPointHasIesLoc[i], hasIes ? 1 : 0);
        }

        GL.ActiveTexture(TextureUnit.Texture13);
        GL.BindTexture(TextureTarget.Texture2DArray, _pointIesArrayTex);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(uPointIesLutLocation, 13);
    }

    private void EnsureSpotCookieArray()
    {
        if (_spotCookieArrayTex != 0)
            return;

        _spotCookieArrayTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, _spotCookieArrayTex);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba,
            CookieArraySize, CookieArraySize, MaxSpotLights, 0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Blanco = sin cookie (multiplica x1) en todas las capas por defecto.
        byte[] white = new byte[CookieArraySize * CookieArraySize * 4];
        for (int i = 0; i < white.Length; i++) white[i] = 255;
        for (int layer = 0; layer < MaxSpotLights; layer++)
            GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, layer, CookieArraySize, CookieArraySize, 1,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, white);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static void EnsureIesArray(ref int texId, int layers)
    {
        if (texId != 0)
            return;

        texId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, texId);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.R16f,
            IesProfile.Resolution, 1, layers, 0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // 1.0 = sin IES (sin modulacion angular) en todas las capas por defecto.
        float[] ones = new float[IesProfile.Resolution];
        for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
        for (int layer = 0; layer < layers; layer++)
            GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, layer, IesProfile.Resolution, 1, 1,
                OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.Float, ones);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    // Hornea el perfil IES (parseado en Core, sin dependencias de GL) a la capa `slot` del array.
    // Cache simple por ruta (no por fecha de archivo: los .ies no suelen editarse en vivo).
    private void UpdateIesLayer(int arrayTex, string[] pathPerSlot, int slot, string path)
    {
        if (pathPerSlot[slot] == path)
            return;
        pathPerSlot[slot] = path;

        float[] falloff;
        if (string.IsNullOrWhiteSpace(path))
        {
            falloff = new float[IesProfile.Resolution];
            for (int i = 0; i < falloff.Length; i++) falloff[i] = 1f;
        }
        else
        {
            string? fullPath = NormalizeExistingAssetPath(path);
            var profile = fullPath != null ? IesProfile.Parse(fullPath) : null;
            if (profile == null)
            {
                falloff = new float[IesProfile.Resolution];
                for (int i = 0; i < falloff.Length; i++) falloff[i] = 1f;
            }
            else
            {
                falloff = profile.AngularFalloff;
            }
        }

        GL.BindTexture(TextureTarget.Texture2DArray, arrayTex);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, slot, IesProfile.Resolution, 1, 1,
            OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.Float, falloff);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

}
