using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    // Opt-in por proyecto para automatización de escenas de prueba/CI. El archivo contiene un
    // nombre de probe por línea; se consume una vez para no rehornear al abrir normalmente.
    private void BakeRequestedReflectionProbesOnLoad()
    {
        string requestPath = Path.Combine(projectPath, ".groko-auto-bake-probes");
        if (!File.Exists(requestPath) || sceneRenderer == null)
            return;

        try
        {
            var requested = new HashSet<string>(File.ReadAllLines(requestPath), StringComparer.OrdinalIgnoreCase);
            int baked = 0;
            foreach (var owner in objects)
            {
                if (!requested.Contains(owner.Name))
                    continue;
                var probe = owner.GetComponent<ReflectionProbe>();
                if (probe == null)
                    continue;
                BakeReflectionProbe(owner, probe);
                baked++;
            }
            statusMessage = baked > 0 ? $"Reflection probe auto-baked ({baked})." : "Reflection probe auto-bake request found no matching probes.";
        }
        catch (Exception ex)
        {
            statusMessage = "Reflection probe auto-bake failed: " + ex.Message;
        }
        finally
        {
            try { File.Delete(requestPath); } catch { }
        }
    }

    private void BakeReflectionProbe(GameObject owner, ReflectionProbe probe)
    {
        if (sceneRenderer == null)
        {
            statusMessage = "Reflection Probe bake failed: renderer not ready.";
            return;
        }

        int resolution = ClampReflectionProbeResolution(probe.Resolution);
        string dir = !string.IsNullOrWhiteSpace(rootAssetsPath)
            ? Path.Combine(rootAssetsPath, "ReflectionProbes")
            : Path.Combine(Environment.CurrentDirectory, "ReflectionProbes");
        Directory.CreateDirectory(dir);

        string assetName = SanitizeFileName(string.IsNullOrWhiteSpace(owner.Name) ? "ReflectionProbe" : owner.Name + "_ReflectionProbe");
        string cubemapPath = string.IsNullOrWhiteSpace(probe.BakedCubemapPath)
            ? CubemapAsset.Create(dir, assetName)
            : probe.BakedCubemapPath;

        if (!Path.IsPathRooted(cubemapPath))
            cubemapPath = Path.Combine(dir, Path.GetFileName(cubemapPath));

        string cubemapDir = Path.Combine(Path.GetDirectoryName(cubemapPath) ?? dir, Path.GetFileNameWithoutExtension(cubemapPath) + "_Faces");
        Directory.CreateDirectory(cubemapDir);

        var pos = owner.GlobalPosition;
        var center = new Vector3(pos.X + probe.CenterX, pos.Y + probe.CenterY, pos.Z + probe.CenterZ);
        string px = Path.Combine(cubemapDir, "posx.png");
        string nx = Path.Combine(cubemapDir, "negx.png");
        string py = Path.Combine(cubemapDir, "posy.png");
        string ny = Path.Combine(cubemapDir, "negy.png");
        string pz = Path.Combine(cubemapDir, "posz.png");
        string nz = Path.Combine(cubemapDir, "negz.png");

        int previousFramebuffer = GL.GetInteger(GetPName.FramebufferBinding);
        int[] previousViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, previousViewport);

        int fbo = 0;
        int color = 0;
        int depth = 0;
        try
        {
            fbo = GL.GenFramebuffer();
            color = GL.GenTexture();
            depth = GL.GenRenderbuffer();

            GL.BindTexture(TextureTarget.Texture2D, color);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, resolution, resolution, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, resolution, resolution);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException($"Reflection Probe framebuffer incomplete: {status}");

            rendererSelectedObjectIds.Clear();
            sceneRenderer.SelectedObjectIds = rendererSelectedObjectIds;
            sceneRenderer.DebugView = 0;
            sceneRenderer.ShadowQuality = shadowQuality;
            sceneRenderer.ShadowBias = shadowBias;
            sceneRenderer.ColorSpace = colorSpace;
            sceneRenderer.ImageBasedLighting = iblEnabled;
            sceneRenderer.IblDiffuseStrength = iblDiffuseStrength;
            sceneRenderer.IblSpecularStrength = iblSpecularStrength;
            sceneRenderer.EnvironmentHdriPath = hdriPath;
            sceneRenderer.EnvironmentMode = environmentMode;
            sceneRenderer.StaticHierarchyLodMode = staticHierarchyLodMode;
            sceneRenderer.DrawEnvironmentBackground = true;
            sceneRenderer.RenderRealtimeShadows = false;
            // No capturar reflejos de probes durante el bake (evita que el probe se refleje a sí
            // mismo y se degrade en cada re-bake). Se restaura en el finally.
            sceneRenderer.SuppressReflectionProbes = true;
            sceneRenderer.OpaqueTextureHandle = 0;
            sceneRenderer.ResolveAndCaptureOpaqueTexture = null;

            ApplyBoneAttachments();

            float nearClip = Math.Max(0.001f, probe.NearClip);
            float farClip = Math.Max(nearClip + 0.01f, probe.FarClip);
            BakeReflectionProbeFace(fbo, center, new Vector3(1, 0, 0), new Vector3(0, -1, 0), resolution, nearClip, farClip, px);
            BakeReflectionProbeFace(fbo, center, new Vector3(-1, 0, 0), new Vector3(0, -1, 0), resolution, nearClip, farClip, nx);
            BakeReflectionProbeFace(fbo, center, new Vector3(0, 1, 0), new Vector3(0, 0, 1), resolution, nearClip, farClip, py);
            BakeReflectionProbeFace(fbo, center, new Vector3(0, -1, 0), new Vector3(0, 0, -1), resolution, nearClip, farClip, ny);
            BakeReflectionProbeFace(fbo, center, new Vector3(0, 0, 1), new Vector3(0, -1, 0), resolution, nearClip, farClip, pz);
            BakeReflectionProbeFace(fbo, center, new Vector3(0, 0, -1), new Vector3(0, -1, 0), resolution, nearClip, farClip, nz);

            var data = File.Exists(cubemapPath) ? CubemapAsset.Load(cubemapPath) : new CubemapAssetData();
            data.Name = Path.GetFileNameWithoutExtension(cubemapPath);
            data.PositiveXPath = px;
            data.NegativeXPath = nx;
            data.PositiveYPath = py;
            data.NegativeYPath = ny;
            data.PositiveZPath = pz;
            data.NegativeZPath = nz;
            data.PreviewTexturePath = pz;
            data.Resolution = resolution;
            data.GeneratedByReflectionProbe = true;
            CubemapAsset.Save(cubemapPath, data);

            probe.BakedCubemapPath = cubemapPath;
            InvalidateReflectionProbePreviewSource(cubemapPath);
            InvalidateAssetPreview(cubemapPath, deleteTexture: true);
            InvalidateAssetPreview(pz, deleteTexture: true);
            selectedAssetPath = cubemapPath;
            sceneDirty = true;
            statusMessage = $"Reflection Probe baked: {Path.GetFileName(cubemapPath)} ({resolution}x{resolution}).";
        }
        catch (Exception ex)
        {
            statusMessage = "Reflection Probe bake failed: " + ex.Message;
        }
        finally
        {
            sceneRenderer.SuppressReflectionProbes = false;
            if (fbo != 0) GL.DeleteFramebuffer(fbo);
            if (color != 0) GL.DeleteTexture(color);
            if (depth != 0) GL.DeleteRenderbuffer(depth);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
            if (previousViewport.Length >= 4)
                GL.Viewport(previousViewport[0], previousViewport[1], previousViewport[2], previousViewport[3]);
        }
    }

    private void BakeReflectionProbeFace(int framebuffer, Vector3 position, Vector3 forward, Vector3 up, int resolution, float nearClip, float farClip, string outputPath)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.Viewport(0, 0, resolution, resolution);
        GL.ClearColor(0.075f, 0.095f, 0.120f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);

        var bakeCamera = new EditorCameraState
        {
            Position = position,
            Front = forward.Normalized(),
            Up = up.Normalized(),
            FOV = 90f,
            NearClip = Math.Max(0.001f, nearClip),
            FarClip = Math.Max(nearClip + 0.01f, farClip),
            AntiAliasing = false,
            FrustumCulling = camera.FrustumCulling,
            OcclusionCulling = false,
            CullingMask = LayerMask.Everything
        };
        sceneRenderer.Render(objects, null, bakeCamera, resolution, resolution, showGrid: false);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        using Bitmap bitmap = ReadCurrentFramebufferBitmap(resolution, resolution);
        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private static Bitmap ReadCurrentFramebufferBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
        return bitmap;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "ReflectionProbe" : value.Trim();
    }
}
