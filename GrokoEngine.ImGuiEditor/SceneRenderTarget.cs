using OpenTK.Graphics.OpenGL4;
using GrokoEngine;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneRenderTarget : IDisposable
{
    private int framebuffer;
    private int colorTexture;
    private int depthTexture;
    private int opaqueTexture;
    private int opaqueFramebuffer;
    private int depthRenderbuffer;
    private int msaaFramebuffer;
    private int msaaColorRenderbuffer;
    private int msaaDepthRenderbuffer;
    private int postFramebuffer;
    private int postTexture;
    private int postShader;
    private int postVertexArray;
    private int postVertexBuffer;
    private int samples = 1;
    private int maxSupportedSamples = -1;
    private int uSceneLocation;
    private int uDepthLocation;
    private int uExposureLocation;
    private int uGammaLocation;
    private int uToneMappingLocation;
    private int uBloomStrengthLocation;
    private int uBloomThresholdLocation;
    private int uBloomScatterLocation;
    private int uBloomClampLocation;
    private int uBloomTintLocation;
    private int uBloomHighQualityLocation;
    private int uPostExposureLocation;
    private int uContrastLocation;
    private int uHueShiftLocation;
    private int uSaturationLocation;
    private int uColorFilterLocation;
    private int uVignetteIntensityLocation;
    private int uVignetteSmoothnessLocation;
    private int uVignetteCenterLocation;
    private int uVignetteColorLocation;
    private int uVignetteRoundedLocation;
    private int uChromaticAberrationLocation;
    private int uAoStrengthLocation;
    private int uTexelLocation;
    private int uFxaaEnabledLocation;
    private int uBloomQualityLocation;
    private int uAoQualityLocation;
    private int uBloomTexLocation;
    private int uToneMappingModeLocation, uWbTempLocation, uWbTintLocation, uFilmGrainLocation, uFilmGrainIntensityLocation, uTimeLocation;
    private int uStopNaNsLocation, uDitheringLocation;
    private int uLggLiftLocation, uLggGammaLocation, uLggGainLocation;

    // ── Pirámide de bloom dedicada (downsample/upsample, estilo Unity HDRP / Call of Duty) ──
    private const int MaxBloomMips = 7;
    private readonly int[] bloomMipTex = new int[MaxBloomMips];
    private readonly int[] bloomMipFbo = new int[MaxBloomMips];
    private readonly int[] bloomMipW = new int[MaxBloomMips];
    private readonly int[] bloomMipH = new int[MaxBloomMips];
    private int bloomMipCount;
    private int bloomDownShader;
    private int bloomUpShader;
    private int uDownSourceLoc, uDownTexelLoc, uDownPrefilterLoc, uDownThresholdLoc, uDownKneeLoc, uDownClampLoc;
    private int uUpSourceLoc, uUpTexelLoc;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public IntPtr TextureId => (IntPtr)(postTexture != 0 ? postTexture : colorTexture);
    public IntPtr ColorTextureId => (IntPtr)colorTexture;
    public IntPtr DepthTextureId => (IntPtr)depthTexture;
    public IntPtr OpaqueTextureId => (IntPtr)opaqueTexture;
    public int OpaqueTextureHandle => opaqueTexture;
    public bool DepthTextureEnabled { get; set; } = true;
    public bool OpaqueTextureEnabled { get; set; } = true;
    public bool HasOpaqueTexture { get; private set; }
    public bool FxaaEnabled { get; set; } = true;
    // Ajustes de salida por-cámara (Camera.StopNaNs/Dithering): el app los copia antes de ApplyPostProcess.
    public bool StopNaNsEnabled { get; set; } = false;
    public bool DitheringEnabled { get; set; } = false;
    public int BloomQuality { get; set; } = 2;
    public int AmbientOcclusionQuality { get; set; } = 2;

    // God rays (light shafts del sol): el renderer/app rellena esto antes de ApplyPostProcess.
    public bool GodRaysEnabled { get; set; } = false;
    public float GodRaySunU { get; set; }
    public float GodRaySunV { get; set; }
    public float GodRayStrength { get; set; } = 1f;
    private int uGodRaysEnabledLocation, uGodRaySunLocation, uGodRayStrengthLocation;

    public SceneRenderTarget()
    {
        framebuffer = GL.GenFramebuffer();
        colorTexture = GL.GenTexture();
        depthTexture = GL.GenTexture();
        opaqueTexture = GL.GenTexture();
        opaqueFramebuffer = GL.GenFramebuffer();
        depthRenderbuffer = GL.GenRenderbuffer();
        postFramebuffer = GL.GenFramebuffer();
        postTexture = GL.GenTexture();
        postShader = CreatePostShader();
        CachePostUniformLocations();
        CreatePostQuad();
        CreateBloomShaders();
        Resize(1, 1);
    }

    public void Resize(int width, int height, int requestedSamples = 1)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        requestedSamples = ClampSamplesToDevice(NormalizeSamples(requestedSamples));
        if (width == Width && height == Height && requestedSamples == samples) return;

        Width = width;
        Height = height;
        samples = requestedSamples;

        GL.BindTexture(TextureTarget.Texture2D, colorTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, Width, Height, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, depthTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, Width, Height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colorTexture, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTexture, 0);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Scene framebuffer incomplete: {status}");

        GL.BindTexture(TextureTarget.Texture2D, postTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, postFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, postTexture, 0);
        status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Scene postprocess framebuffer incomplete: {status}");

        GL.BindTexture(TextureTarget.Texture2D, opaqueTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, Width, Height, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, opaqueTexture, 0);
        status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Scene opaque framebuffer incomplete: {status}");
        HasOpaqueTexture = false;

        if (samples > 1)
            EnsureMsaaResources();
        else
            ReleaseMsaaResources();

        CreateBloomMips();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
    }
}
