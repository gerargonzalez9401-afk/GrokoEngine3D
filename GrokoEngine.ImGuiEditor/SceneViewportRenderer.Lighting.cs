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

    private void ComputeSunScreenPosition(IReadOnlyList<GameObject> objects, Vector3 eye, Matrix4 mvp, Vector3 camFront)
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
        var clip = new Vector4(eye + sunSky * 100000f, 1f) * mvp;
        if (clip.W <= 0.0001f)
            return;
        SunScreenUv = new Vector2((clip.X / clip.W) * 0.5f + 0.5f, (clip.Y / clip.W) * 0.5f + 0.5f);
        if (sun.GodRays)
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

        _framePointLights.Clear();
        CollectComponents(objects, _framePointLights, int.MaxValue);
        TrimLightsByImportance(_framePointLights, MaxPointLights, cameraPosition, l => l.Intensity, l => l.Range);
        _frameSpotLights.Clear();
        CollectComponents(objects, _frameSpotLights, int.MaxValue);
        TrimLightsByImportance(_frameSpotLights, MaxSpotLights, cameraPosition, l => l.Intensity, l => l.Range);
        _frameAreaLights.Clear();
        CollectComponents(objects, _frameAreaLights, int.MaxValue);
        TrimLightsByImportance(_frameAreaLights, MaxAreaLights, cameraPosition, l => l.Intensity, l => l.Range);
        _frameRectLights.Clear();
        CollectComponents(objects, _frameRectLights, int.MaxValue);
        TrimLightsByImportance(_frameRectLights, Math.Max(0, MaxAreaLights - _frameAreaLights.Count), cameraPosition, l => l.Intensity, l => l.Range);
    }

    private void ApplySceneLighting(IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        CollectFrameLighting(objects, cameraPosition);   // recolección 1x por Render; aquí solo se SUBEN los uniforms

        // Ambient
        var ambient = _frameAmbient;
        var ambColor = ambient != null
            ? new Vector3(ambient.R, ambient.G, ambient.B)
            : new Vector3(0.3f, 0.32f, 0.36f);
        float ambIntensity = ambient?.Intensity ?? 0.22f;
        // Indirect Multiplier del sol: su luz de rebote escala el fill ambiente (aprox. de GI).
        if (_frameDir != null) ambIntensity *= _frameDir.IndirectMultiplier;
        GL.Uniform3(uAmbientColor, ambColor);
        GL.Uniform1(uAmbientIntensity, ambIntensity);
        GL.Uniform1(uSkyStrength, ambient?.SkyStrength ?? 0.08f);
        GL.Uniform3(uCameraPos, cameraPosition);

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
        GL.Uniform1(uUseIBL, ImageBasedLighting && IblDiffuseStrength > 0.0001f ? 1 : 0);
        GL.Uniform1(uIblDiffuseStrength, Math.Clamp(IblDiffuseStrength, 0f, 1.5f));
        GL.Uniform1(uIblSpecularStrength, Math.Clamp(IblSpecularStrength, 0f, 0.35f));
        GL.ActiveTexture(TextureUnit.Texture9);
        GL.BindTexture(TextureTarget.Texture2D, _brdfLutTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(uBrdfLut, 9);
        GL.Uniform1(uHasBrdfLut, _brdfLutTexture != 0 ? 1 : 0);
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
            GL.Uniform1(uHasEnvMap, 0);
            GL.Uniform3(uEnvDiffuseColor, new Vector3(0.3f, 0.32f, 0.36f));
            GL.Uniform1(uUseEnvSh, 0);
            GL.Uniform1(uHasEnvPrefilterMap, 0);
        }
        GL.Uniform1(uAoStrength, 0f);
        GL.Uniform1(uFogDensity, ppEnabled && pp!.Fog ? pp.FogDensity : 0f);
        GL.Uniform3(uFogColor, ppEnabled ? new Vector3(pp!.FogR, pp.FogG, pp.FogB) : new Vector3(0.48f, 0.58f, 0.68f));
        GL.Uniform1(uVolumetricStrength, ppEnabled ? pp!.VolumetricLightStrength : 0f);
        GL.Uniform1(uDebugView, DebugView);

        // Directional light: dirección calculada desde la rotación del GameObject.
        var dir = _frameDir;
        if (dir != null && dir.gameObject != null)
        {
            GL.Uniform3(uDirDir, ToTk(dir.GetNormalizedDirection()).Normalized());
            GL.Uniform3(uDirColor, ToTk(dir.GetEffectiveColor()));   // Filter * temperatura (Kelvin)
            GL.Uniform1(uDirIntensity, dir.Intensity);
        }
        else
        {
            // Sin DirectionalLight en escena: sin sol de relleno (iluminación limpia, solo ambiente).
            GL.Uniform3(uDirDir, new Vector3(0f, -1f, 0f));
            GL.Uniform3(uDirColor, Vector3.Zero);
            GL.Uniform1(uDirIntensity, 0f);
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
        }
        ApplySpotCookieAndIes(_frameSpotLights.Count > 0 ? _frameSpotLights[0] : null);
        ApplyPointIes(_framePointLights.Count > 0 ? _framePointLights[0] : null);

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

    // Cookie + IES del spot[0] (mismo limite que la sombra realtime: solo una luz de cada tipo
    // lleva el detalle caro). Reutiliza GetTexture() para el cookie (misma cache/hot-reload que
    // las texturas de material) y una cache propia (por ruta) para el LUT de IES horneado.
    private void ApplySpotCookieAndIes(SpotLight? spot)
    {
        bool hasCookie = spot != null && !string.IsNullOrWhiteSpace(spot.CookiePath);
        if (hasCookie)
        {
            int tex = GetTexture(spot!.CookiePath);
            hasCookie = tex != 0;
            if (hasCookie)
            {
                GL.ActiveTexture(TextureUnit.Texture11);
                GL.BindTexture(TextureTarget.Texture2D, tex);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.Uniform1(uSpotCookieLocation, 11);
                _spotCookieMvpScratch = ComputeSpotCookieMatrix(spot);
                GL.UniformMatrix4(uSpotCookieMvpLocation, false, ref _spotCookieMvpScratch);
            }
        }
        GL.Uniform1(uSpotHasCookieLocation, hasCookie ? 1 : 0);

        bool hasIes = spot != null && !string.IsNullOrWhiteSpace(spot.IesProfilePath) &&
            GetOrCreateIesLutTexture(spot.IesProfilePath, ref _spotIesTex, ref _spotIesPathCached);
        if (hasIes)
        {
            GL.ActiveTexture(TextureUnit.Texture12);
            GL.BindTexture(TextureTarget.Texture2D, _spotIesTex);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(uSpotIesLutLocation, 12);
        }
        GL.Uniform1(uSpotHasIesLocation, hasIes ? 1 : 0);
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

    private void ApplyPointIes(PointLight? point)
    {
        bool hasIes = point != null && !string.IsNullOrWhiteSpace(point.IesProfilePath) &&
            GetOrCreateIesLutTexture(point.IesProfilePath, ref _pointIesTex, ref _pointIesPathCached);
        if (hasIes)
        {
            GL.ActiveTexture(TextureUnit.Texture13);
            GL.BindTexture(TextureTarget.Texture2D, _pointIesTex);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(uPointIesLutLocation, 13);
        }
        GL.Uniform1(uPointHasIesLocation, hasIes ? 1 : 0);
    }

    // Hornea el perfil IES (parseado en Core, sin dependencias de GL) a una textura 1D (256x1, R16F)
    // de caida angular normalizada. Cache simple por ruta (no por fecha de archivo: los .ies no
    // suelen editarse en vivo como las texturas de material).
    private bool GetOrCreateIesLutTexture(string path, ref int texId, ref string cachedPath)
    {
        if (texId != 0 && cachedPath == path)
            return true;

        if (texId != 0)
        {
            GL.DeleteTexture(texId);
            texId = 0;
            cachedPath = "";
        }

        string? fullPath = NormalizeExistingAssetPath(path);
        var profile = fullPath != null ? IesProfile.Parse(fullPath) : null;
        if (profile == null)
            return false;

        texId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, texId);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R16f, profile.AngularFalloff.Length, 1, 0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.Float, profile.AngularFalloff);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        cachedPath = path;
        return true;
    }
}
