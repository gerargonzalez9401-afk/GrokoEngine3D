using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GrokoShaderGraphPro.Models;
using GrokoShaderGraphPro.Services;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
private void ApplyRangeTextures(SolidRange range)
{
        ApplyAlphaClipUniforms(range.AlphaClip, range.AlphaClipThreshold);
        ApplySurfaceTextures(range.TexturePath, range.NormalMapPath, range.RoughnessMapPath, range.MetallicMapPath);
        ApplyForwardLightmap(range.LightmapPath, range.ShadowmaskPath, range.LightmapIntensity, range.LightmapScaleOffset);
}

private void ApplySurfaceTexturesIfChanged(
        string? texturePath,
        string? normalMapPath,
        string? roughnessMapPath,
        string? metallicMapPath,
        ref bool hasBoundSurface,
        ref string? boundTexture,
        ref string? boundNormal,
        ref string? boundRoughness,
        ref string? boundMetallic)
    {
        if (hasBoundSurface &&
            string.Equals(boundTexture, texturePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(boundNormal, normalMapPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(boundRoughness, roughnessMapPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(boundMetallic, metallicMapPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplySurfaceTextures(texturePath, normalMapPath, roughnessMapPath, metallicMapPath);
        // Los lightmaps pertenecen únicamente a los rangos estáticos. Sin este reset un
        // mesh dinámico podría heredar el último lightmap del batch estático anterior.
        DisableForwardLightmap();
        hasBoundSurface = true;
        boundTexture = texturePath;
        boundNormal = normalMapPath;
        boundRoughness = roughnessMapPath;
        boundMetallic = metallicMapPath;
    }

private void ApplySurfaceTextures(string? texturePath, string? normalMapPath, string? roughnessMapPath, string? metallicMapPath)
    {
        BindOptionalTexture(TextureUnit.Texture0, solidTextureLocation, solidHasTextureLocation, texturePath);
        BindOptionalTexture(TextureUnit.Texture2, solidNormalMapLocation, solidHasNormalMapLocation, normalMapPath);
        BindOptionalTexture(TextureUnit.Texture3, solidRoughnessMapLocation, solidHasRoughnessMapLocation, roughnessMapPath);
        BindOptionalTexture(TextureUnit.Texture4, solidMetallicMapLocation, solidHasMetallicMapLocation, metallicMapPath);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

// Deferred ya aplicaba la irradiancia horneada en el G-buffer. Forward y Forward+
// tenían el dato en SolidRange pero nunca lo enlazaban: una casa estática quedaba sin
// su rebote indirecto al cambiar de ruta. Usamos Texture15 sólo para opacos; el pase
// refractivo la reserva después para OpaqueTexture y resetea este estado antes de usarla.
private void ApplyForwardLightmap(string? lightmapPath, string? shadowmaskPath, float intensity, Vector4 scaleOffset = default)
    {
        int texture = string.IsNullOrWhiteSpace(lightmapPath) ? 0 : GetTexture(lightmapPath);
        GL.Uniform1(solidLightmapLocation, 15);
        GL.Uniform1(solidHasLightmapLocation, texture != 0 ? 1 : 0);
        GL.Uniform1(solidLightmapIntensityLocation, Math.Clamp(intensity, 0f, 8f));
        GL.Uniform4(solidLightmapScaleOffsetLocation, scaleOffset == default ? new Vector4(1f, 1f, 0f, 0f) : scaleOffset);
        GL.ActiveTexture(TextureUnit.Texture15);
        GL.BindTexture(TextureTarget.Texture2D, texture);
        int shadowmask = string.IsNullOrWhiteSpace(shadowmaskPath) ? 0 : GetTexture(shadowmaskPath);
        GL.Uniform1(solidShadowmaskLocation, 16);
        GL.Uniform1(solidHasShadowmaskLocation, shadowmask != 0 ? 1 : 0);
        GL.ActiveTexture(TextureUnit.Texture16);
        GL.BindTexture(TextureTarget.Texture2D, shadowmask);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

private void DisableForwardLightmap()
    {
        GL.Uniform1(solidHasLightmapLocation, 0);
        GL.Uniform1(solidLightmapIntensityLocation, 0f);
        GL.Uniform4(solidLightmapScaleOffsetLocation, new Vector4(1f, 1f, 0f, 0f));
        GL.Uniform1(solidHasShadowmaskLocation, 0);
    }

private void BindOptionalTexture(TextureUnit unit, int samplerLocation, int hasLocation, string? path)
    {
        if (unit == TextureUnit.Texture2 && !string.IsNullOrWhiteSpace(path))
            TextureImportSettingsAsset.EnsureNormalMap(path);

        int texture = string.IsNullOrWhiteSpace(path) ? 0 : GetTexture(path);
        GL.Uniform1(samplerLocation, (int)unit - (int)TextureUnit.Texture0);
        GL.Uniform1(hasLocation, texture != 0 ? 1 : 0);
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, texture);
    }

private void PrewarmMaterialAsset(string? materialPath)
    {
        string? fullPath = NormalizeExistingAssetPath(materialPath);
        if (fullPath == null || !MaterialAsset.IsMaterialPath(fullPath))
            return;

        MaterialAssetData data;
        try
        {
            data = MaterialAsset.Load(fullPath);
        }
        catch
        {
            return;
        }

        PrewarmTexturePath(MaterialAsset.GetAlbedo(data));
        PrewarmTexturePath(data.NormalMapPath);
        PrewarmTexturePath(data.RoughnessMapPath);
        PrewarmTexturePath(data.MetallicMapPath);
        foreach (string texturePath in data.ShaderGraphTextures.Values)
            PrewarmTexturePath(texturePath);
    }

private void PrewarmTexturePath(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return;

        string? fullPath = NormalizeExistingAssetPath(texturePath);
        if (fullPath != null && MaterialAsset.IsTexturePath(fullPath))
            _ = GetTexture(fullPath);
    }

public void InvalidateTexture(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return;

        string? fullPath = NormalizeExistingAssetPath(texturePath);
        if (fullPath == null)
            return;

        if (textureCache.TryGetValue(fullPath, out int texture) && texture != 0)
            GL.DeleteTexture(texture);

        textureCache.Remove(fullPath);
        textureFileTimeCache.Remove(fullPath);
        textureNextValidationTick.Remove(fullPath);
    }

private int GetTexture(string path)
    {
        string? fullPath = NormalizeTexturePath(path);
        if (fullPath == null)
        {
            LogAssetWarning($"[SceneViewportRenderer] Textura no encontrada: {path}");
            return 0;
        }

        if (textureCache.TryGetValue(fullPath, out int cached) &&
            textureFileTimeCache.TryGetValue(fullPath, out DateTime cachedTime))
        {
            // Las invalidaciones desde el Inspector/importador llaman InvalidateTexture y
            // siguen siendo inmediatas. Este chequeo es solo el respaldo para cambios
            // externos, y evita hacer stat de cada material cada frame.
            long nowTick = Environment.TickCount64;
            if (textureNextValidationTick.TryGetValue(fullPath, out long nextTick) && nowTick < nextTick)
                return cached;

            DateTime fileTime = System.IO.File.GetLastWriteTimeUtc(fullPath);
            if (cachedTime == fileTime)
            {
                textureNextValidationTick[fullPath] = nowTick + 5000;
                return cached;
            }
        }

        if (textureCache.TryGetValue(fullPath, out int oldTexture) && oldTexture != 0)
        {
            GL.DeleteTexture(oldTexture);
            textureCache.Remove(fullPath);
            textureFileTimeCache.Remove(fullPath);
            textureNextValidationTick.Remove(fullPath);
        }

        try
        {
            DateTime fileTime = System.IO.File.GetLastWriteTimeUtc(fullPath);
            var importSettings = TextureImportSettingsAsset.Load(fullPath);
            if (!EditorImageLoader.TryLoadRgba(fullPath, out var image))
                throw new InvalidDataException("Formato de textura no soportado o archivo corrupto.");

            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            ApplyTextureImportParameters(importSettings);

            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                image.Width,
                image.Height,
                0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                PixelType.UnsignedByte,
                image.Pixels);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            textureCache[fullPath] = texture;
            textureFileTimeCache[fullPath] = fileTime;
            textureNextValidationTick[fullPath] = Environment.TickCount64 + 5000;
            return texture;
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[SceneViewportRenderer] Error cargando textura '{fullPath}': {ex.Message}");
            return 0;
        }
    }

private bool TryGetReflectionProbeCubeTexture(string? cubemapPath, out int textureId, out float maxLod)
    {
        textureId = 0;
        maxLod = 0f;

        string? fullPath = NormalizeExistingAssetPath(cubemapPath);
        if (fullPath == null || !CubemapAsset.IsCubemapPath(fullPath))
            return false;

        // Un probe Baked es inmutable durante el render normal. Antes se parseaba el .cubemap,
        // se comprobaban seis PNG y se consultaban siete fechas de archivo CADA frame. En Windows
        // eso puede bloquear el hilo de render por el antivirus/cache de disco y convertir una
        // escena de 120 FPS en ~40 FPS. Conservamos hot-reload, pero validamos el asset una vez
        // cada dos segundos; al cambiarlo se recarga y prefiltra como siempre.
        const int validationIntervalFrames = 120;
        if (_reflectionProbeCubeCache.TryGetValue(fullPath, out var resident) &&
            resident.TextureId != 0 &&
            _frameCount - resident.LastValidationFrame < validationIntervalFrames)
        {
            textureId = resident.TextureId;
            maxLod = resident.MaxLod;
            return true;
        }

        CubemapAssetData data;
        try
        {
            data = CubemapAsset.Load(fullPath);
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[SceneViewportRenderer] Reflection Probe cubemap invalido '{fullPath}': {ex.Message}");
            return false;
        }

        string[] faces =
        {
            data.PositiveXPath,
            data.NegativeXPath,
            data.PositiveYPath,
            data.NegativeYPath,
            data.PositiveZPath,
            data.NegativeZPath
        };

        if (faces.Any(string.IsNullOrWhiteSpace) || faces.Any(p => !System.IO.File.Exists(p)))
            return false;

        DateTime cubemapTime = System.IO.File.GetLastWriteTimeUtc(fullPath);
        DateTime faceTime = faces.Select(System.IO.File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if (_reflectionProbeCubeCache.TryGetValue(fullPath, out var cached) &&
            cached.TextureId != 0 &&
            cached.CubemapWriteTime == cubemapTime &&
            cached.FaceWriteTime == faceTime)
        {
            cached = cached with { LastValidationFrame = _frameCount };
            _reflectionProbeCubeCache[fullPath] = cached;
            textureId = cached.TextureId;
            maxLod = cached.MaxLod;
            return true;
        }

        if (_reflectionProbeCubeCache.TryGetValue(fullPath, out var old) && old.TextureId != 0)
            GL.DeleteTexture(old.TextureId);

        try
        {
            var images = new RgbaImage[6];
            for (int i = 0; i < faces.Length; i++)
            {
                if (!EditorImageLoader.TryLoadRgba(faces[i], out images[i]))
                    throw new InvalidOperationException($"No se pudo cargar cara {i}: {faces[i]}");
            }

            int size = images[0].Width;
            if (size <= 0 || images[0].Height != size)
                throw new InvalidOperationException("La cara +X del cubemap no es cuadrada.");

            for (int i = 1; i < images.Length; i++)
            {
                if (images[i].Width != size || images[i].Height != size)
                    throw new InvalidOperationException("Todas las caras del cubemap deben tener la misma resolucion.");
            }

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.TextureCubeMap, tex);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            for (int i = 0; i < images.Length; i++)
            {
                GL.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + i,
                    0,
                    PixelInternalFormat.Rgba,
                    size,
                    size,
                    0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    images[i].Pixels);
            }

            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            // Mips de caja en la FUENTE: los usa el importance sampling GGX para evitar fireflies.
            GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);
            GL.BindTexture(TextureTarget.TextureCubeMap, 0);

            // Prefiltrado GGX (mip0 nítido a 512 + cadena por rugosidad, split-sum) → reflejos
            // lisos nítidos y rugosos con la forma correcta, al nivel del HDRI.
            int prefiltered = PrefilterProbeCubemap(tex, size, out float prefMaxLod);
            GL.DeleteTexture(tex);

            _reflectionProbeCubeCache[fullPath] = new ReflectionProbeCubeCacheEntry(prefiltered, prefMaxLod, cubemapTime, faceTime, _frameCount);
            textureId = prefiltered;
            maxLod = prefMaxLod;
            return true;
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[SceneViewportRenderer] Error cargando Reflection Probe cubemap '{fullPath}': {ex.Message}");
            return false;
        }
    }

// Sube la cookie asignada al spot `slot` a esa capa del array (todas las capas comparten el
// mismo tamaño fijo CookieArraySize, asi que se redimensiona la imagen de origen al cargarla).
// Blanco (sin cookie) si el path esta vacio, no existe, o falla la carga.
private void UpdateSpotCookieLayer(int slot, string path)
{
    if (_spotCookiePathPerSlot[slot] == path)
        return;
    _spotCookiePathPerSlot[slot] = path;

    byte[] pixels = new byte[CookieArraySize * CookieArraySize * 4];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = 255; // blanco = sin cookie

    if (!string.IsNullOrWhiteSpace(path))
    {
        string? fullPath = NormalizeExistingAssetPath(path);
        if (fullPath != null)
        {
            try
            {
                using var original = EditorImageLoader.LoadBitmap(fullPath) ?? throw new InvalidDataException("Formato de cookie no soportado.");
                using var resized = new Bitmap(original, new Size(CookieArraySize, CookieArraySize));
                using var bitmap = resized.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb
                    ? resized
                    : resized.Clone(new Rectangle(0, 0, CookieArraySize, CookieArraySize), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, CookieArraySize, CookieArraySize);
                var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            catch (Exception ex)
            {
                LogAssetWarning($"[SceneViewportRenderer] Error cargando cookie '{fullPath}': {ex.Message}");
            }
        }
    }

    GL.BindTexture(TextureTarget.Texture2DArray, _spotCookieArrayTex);
    GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, slot, CookieArraySize, CookieArraySize, 1,
        OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
    GL.BindTexture(TextureTarget.Texture2DArray, 0);
}

private static void ApplyTextureImportParameters(GrokoEngine.TextureImportSettings settings)
    {
        var wrap = settings.WrapMode.Equals("Clamp", StringComparison.OrdinalIgnoreCase) ||
                   settings.WrapMode.Equals("Clamp To Edge", StringComparison.OrdinalIgnoreCase)
            ? TextureWrapMode.ClampToEdge
            : settings.WrapMode.Equals("Mirror", StringComparison.OrdinalIgnoreCase) ||
              settings.WrapMode.Equals("Mirrored Repeat", StringComparison.OrdinalIgnoreCase)
                ? TextureWrapMode.MirroredRepeat
                : TextureWrapMode.Repeat;

        TextureMinFilter minFilter;
        TextureMagFilter magFilter;
        if (settings.FilterMode.Equals("Point", StringComparison.OrdinalIgnoreCase))
        {
            minFilter = TextureMinFilter.NearestMipmapNearest;
            magFilter = TextureMagFilter.Nearest;
        }
        else if (settings.FilterMode.Equals("Trilinear", StringComparison.OrdinalIgnoreCase))
        {
            minFilter = TextureMinFilter.LinearMipmapLinear;
            magFilter = TextureMagFilter.Linear;
        }
        else
        {
            minFilter = TextureMinFilter.LinearMipmapNearest;
            magFilter = TextureMagFilter.Linear;
        }

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minFilter);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magFilter);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);

        if (settings.AnisoLevel > 1)
        {
            float maxAniso = GL.GetFloat((GetPName)All.MaxTextureMaxAnisotropy);
            if (maxAniso > 1f)
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)All.TextureMaxAnisotropy, MathF.Min(settings.AnisoLevel, maxAniso));
        }
    }
}
