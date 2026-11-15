using OpenTK.Graphics.OpenGL4;
using System;

namespace GrokoEngine.ImGuiEditor;

/// <summary>
/// Medición de luminancia y adaptación de exposición enteramente en GPU. Se mide la media
/// LOGARÍTMICA en una cuadrícula de la pirámide HDR: una luz o píxel especular no puede arrastrar
/// toda la exposición, como ocurría con la media lineal. Un ping-pong 1x1 conserva la exposición
/// anterior para que la adaptación no salte entre fotogramas.
/// </summary>
internal sealed partial class SceneRenderTarget
{
    private int uAutoExposureSceneLocation;
    private int uAutoExposureHistoryLocation;
    private int uAutoExposureMipLevelLocation;
    private int uAutoExposureMinEvLocation;
    private int uAutoExposureMaxEvLocation;
    private int uAutoExposureCompensationLocation;
    private int uAutoExposureBlendLocation;

    private void CreateAutoExposureResources()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec2 aPosition;
            layout (location = 1) in vec2 aUv;
            out vec2 vUv;
            void main() { vUv = aUv; gl_Position = vec4(aPosition, 0.0, 1.0); }
            """;
        const string fragmentSource = @"#version 330 core
            in vec2 vUv;
            uniform sampler2D uScene;
            uniform sampler2D uHistory;
            uniform float uMipLevel;
            uniform float uMinEv;
            uniform float uMaxEv;
            uniform float uCompensation;
            uniform float uAdaptationBlend;
            out vec4 outExposure;

            void main()
            {
                // Medición logarítmica 4x4 en un mip muy pequeño. Es un fotómetro robusto:
                // los highlights ocupan poca área y no oscurecen artificialmente toda la toma.
                float logLuminance = 0.0;
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    vec2 uv = (vec2(float(x), float(y)) + 0.5) * 0.25;
                    vec3 color = textureLod(uScene, uv, uMipLevel).rgb;
                    float luminance = max(dot(color, vec3(0.2126, 0.7152, 0.0722)), 0.0001);
                    logLuminance += log(luminance);
                }
                float averageLuminance = max(exp(logLuminance * (1.0 / 16.0)), 0.0001);
                float targetExposure = 0.18 / averageLuminance;
                targetExposure *= exp2(uCompensation);
                targetExposure = clamp(targetExposure, exp2(uMinEv), exp2(uMaxEv));
                float history = max(texture(uHistory, vec2(0.5)).r, 0.0001);
                float exposure = mix(history, targetExposure, clamp(uAdaptationBlend, 0.0, 1.0));
                outExposure = vec4(exposure, 0.0, 0.0, 1.0);
            }
            ";

        autoExposureShader = LinkProgram(vertexSource, fragmentSource);
        uAutoExposureSceneLocation = GL.GetUniformLocation(autoExposureShader, "uScene");
        uAutoExposureHistoryLocation = GL.GetUniformLocation(autoExposureShader, "uHistory");
        uAutoExposureMipLevelLocation = GL.GetUniformLocation(autoExposureShader, "uMipLevel");
        uAutoExposureMinEvLocation = GL.GetUniformLocation(autoExposureShader, "uMinEv");
        uAutoExposureMaxEvLocation = GL.GetUniformLocation(autoExposureShader, "uMaxEv");
        uAutoExposureCompensationLocation = GL.GetUniformLocation(autoExposureShader, "uCompensation");
        uAutoExposureBlendLocation = GL.GetUniformLocation(autoExposureShader, "uAdaptationBlend");

        for (int i = 0; i < 2; i++)
        {
            GL.BindTexture(TextureTarget.Texture2D, autoExposureTextures[i]);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R16f, 1, 1, 0,
                PixelFormat.Red, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, autoExposureFramebuffers[i]);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, autoExposureTextures[i], 0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Auto exposure framebuffer incomplete.");
        }

        ResetAutoExposure();
    }

    private void ResetAutoExposure()
    {
        for (int i = 0; i < 2; i++)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, autoExposureFramebuffers[i]);
            GL.Viewport(0, 0, 1, 1);
            GL.ClearColor(1f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }
        autoExposureReadIndex = 0;
        autoExposureLastUpdateTicks = 0;
    }

    private void ApplyAutoExposure(float minEv, float maxEv, float compensationEv, float adaptationSpeed)
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, colorTexture);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

        long now = Environment.TickCount64;
        float deltaSeconds = autoExposureLastUpdateTicks == 0
            ? 1f / 60f
            : Math.Clamp((now - autoExposureLastUpdateTicks) / 1000f, 1f / 240f, 0.1f);
        autoExposureLastUpdateTicks = now;
        float blend = 1f - MathF.Exp(-Math.Clamp(adaptationSpeed, 0.05f, 12f) * deltaSeconds);
        int writeIndex = 1 - autoExposureReadIndex;
        int maxMip = Math.Max(0, (int)MathF.Floor(MathF.Log2(Math.Max(Width, Height))));
        // Dejamos un mip 4x4 (aprox.) para medir varias regiones en vez de una sola media 1x1.
        int meterMip = Math.Max(0, maxMip - 2);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, autoExposureFramebuffers[writeIndex]);
        GL.Viewport(0, 0, 1, 1);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.UseProgram(autoExposureShader);
        SetUniform(uAutoExposureSceneLocation, 0);
        SetUniform(uAutoExposureHistoryLocation, 1);
        SetUniform(uAutoExposureMipLevelLocation, (float)meterMip);
        SetUniform(uAutoExposureMinEvLocation, Math.Min(minEv, maxEv));
        SetUniform(uAutoExposureMaxEvLocation, Math.Max(minEv, maxEv));
        SetUniform(uAutoExposureCompensationLocation, compensationEv);
        SetUniform(uAutoExposureBlendLocation, blend);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, colorTexture);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, autoExposureTextures[autoExposureReadIndex]);
        GL.BindVertexArray(postVertexArray);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
        GL.DepthMask(true);
        autoExposureReadIndex = writeIndex;
    }

    private void ReleaseAutoExposureResources()
    {
        if (autoExposureShader != 0) { GL.DeleteProgram(autoExposureShader); autoExposureShader = 0; }
        for (int i = 0; i < 2; i++)
        {
            if (autoExposureTextures[i] != 0) { GL.DeleteTexture(autoExposureTextures[i]); autoExposureTextures[i] = 0; }
            if (autoExposureFramebuffers[i] != 0) { GL.DeleteFramebuffer(autoExposureFramebuffers[i]); autoExposureFramebuffers[i] = 0; }
        }
    }
}
