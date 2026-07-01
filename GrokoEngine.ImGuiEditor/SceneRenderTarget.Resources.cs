using OpenTK.Graphics.OpenGL4;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneRenderTarget
{
    public void Bind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, samples > 1 ? msaaFramebuffer : framebuffer);
        GL.Viewport(0, 0, Width, Height);
    }

    public void Resolve()
    {
        if (samples <= 1) return;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, msaaFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, framebuffer);
        GL.BlitFramebuffer(
            0, 0, Width, Height,
            0, 0, Width, Height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        GL.BlitFramebuffer(
            0, 0, Width, Height,
            0, 0, Width, Height,
            ClearBufferMask.DepthBufferBit,
            BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, msaaFramebuffer);
    }

    public void CaptureOpaqueTexture()
    {
        HasOpaqueTexture = false;
        if (!OpaqueTextureEnabled || opaqueFramebuffer == 0 || opaqueTexture == 0)
            return;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, opaqueFramebuffer);
        GL.BlitFramebuffer(
            0, 0, Width, Height,
            0, 0, Width, Height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Linear);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        HasOpaqueTexture = true;
    }

    public static void Unbind(int windowWidth, int windowHeight)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, Math.Max(1, windowWidth), Math.Max(1, windowHeight));
    }

    // Presenta la imagen final post-procesada, ya con gamma, a pantalla completa.
    // Usado por el Game Mode (juego exportado) en vez de mostrarla como ImGui::Image.
    // Es un blit de framebuffer (sin shader): la post-pasada deja el resultado en
    // postFramebuffer/postTexture, que se copia escalado al framebuffer por defecto (0).
    public void PresentToScreen(int windowWidth, int windowHeight)
    {
        int w = Math.Max(1, windowWidth);
        int h = Math.Max(1, windowHeight);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, postFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        GL.BlitFramebuffer(
            0, 0, Width, Height,
            0, 0, w, h,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Linear);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        ReleaseMsaaResources();
        ReleaseBloomMips();
        if (bloomDownShader != 0) GL.DeleteProgram(bloomDownShader);
        if (bloomUpShader != 0) GL.DeleteProgram(bloomUpShader);
        if (postVertexBuffer != 0) GL.DeleteBuffer(postVertexBuffer);
        if (postVertexArray != 0) GL.DeleteVertexArray(postVertexArray);
        if (postShader != 0) GL.DeleteProgram(postShader);
        if (opaqueTexture != 0) GL.DeleteTexture(opaqueTexture);
        if (opaqueFramebuffer != 0) GL.DeleteFramebuffer(opaqueFramebuffer);
        if (postTexture != 0) GL.DeleteTexture(postTexture);
        if (postFramebuffer != 0) GL.DeleteFramebuffer(postFramebuffer);
        GL.DeleteRenderbuffer(depthRenderbuffer);
        GL.DeleteTexture(depthTexture);
        GL.DeleteTexture(colorTexture);
        GL.DeleteFramebuffer(framebuffer);
    }

    private void EnsureMsaaResources()
    {
        if (samples <= 1) return;

        if (msaaFramebuffer == 0) msaaFramebuffer = GL.GenFramebuffer();
        if (msaaColorRenderbuffer == 0) msaaColorRenderbuffer = GL.GenRenderbuffer();
        if (msaaDepthRenderbuffer == 0) msaaDepthRenderbuffer = GL.GenRenderbuffer();

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msaaColorRenderbuffer);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, RenderbufferStorage.Rgba16f, Width, Height);

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msaaDepthRenderbuffer);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, RenderbufferStorage.Depth24Stencil8, Width, Height);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, msaaFramebuffer);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, msaaColorRenderbuffer);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, msaaDepthRenderbuffer);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Scene MSAA framebuffer incomplete: {status}");
    }

    private void ReleaseMsaaResources()
    {
        if (msaaDepthRenderbuffer != 0) { GL.DeleteRenderbuffer(msaaDepthRenderbuffer); msaaDepthRenderbuffer = 0; }
        if (msaaColorRenderbuffer != 0) { GL.DeleteRenderbuffer(msaaColorRenderbuffer); msaaColorRenderbuffer = 0; }
        if (msaaFramebuffer != 0) { GL.DeleteFramebuffer(msaaFramebuffer); msaaFramebuffer = 0; }
    }

    private static int NormalizeSamples(int value)
        => value <= 1 ? 1 : value <= 2 ? 2 : value <= 4 ? 4 : 8;

    private int ClampSamplesToDevice(int requested)
    {
        if (requested <= 1)
            return 1;

        if (maxSupportedSamples < 0)
        {
            GL.GetInteger(GetPName.MaxSamples, out maxSupportedSamples);
            maxSupportedSamples = Math.Max(1, maxSupportedSamples);
        }

        return NormalizeSamples(Math.Min(requested, maxSupportedSamples));
    }
}
