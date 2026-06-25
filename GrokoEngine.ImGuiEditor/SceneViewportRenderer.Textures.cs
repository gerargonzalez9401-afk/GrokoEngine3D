using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
        => ApplySurfaceTextures(range.TexturePath, range.NormalMapPath, range.RoughnessMapPath, range.MetallicMapPath);

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
    }

private int GetTexture(string path)
    {
        string? fullPath = NormalizeTexturePath(path);
        if (fullPath == null)
        {
            LogAssetWarning($"[SceneViewportRenderer] Textura no encontrada: {path}");
            return 0;
        }

        DateTime fileTime = System.IO.File.GetLastWriteTimeUtc(fullPath);
        if (textureCache.TryGetValue(fullPath, out int cached) &&
            textureFileTimeCache.TryGetValue(fullPath, out DateTime cachedTime) &&
            cachedTime == fileTime)
            return cached;

        if (textureCache.TryGetValue(fullPath, out int oldTexture) && oldTexture != 0)
        {
            GL.DeleteTexture(oldTexture);
            textureCache.Remove(fullPath);
            textureFileTimeCache.Remove(fullPath);
        }

        try
        {
            var importSettings = TextureImportSettingsAsset.Load(fullPath);
            using var original = new Bitmap(fullPath);
            using var bitmap = original.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb
                ? new Bitmap(original)
                : original.Clone(new Rectangle(0, 0, original.Width, original.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            ApplyTextureImportParameters(importSettings);

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    bitmap.Width,
                    bitmap.Height,
                    0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                    PixelType.UnsignedByte,
                    data.Scan0);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            textureCache[fullPath] = texture;
            textureFileTimeCache[fullPath] = fileTime;
            return texture;
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[SceneViewportRenderer] Error cargando textura '{fullPath}': {ex.Message}");
            return 0;
        }
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
