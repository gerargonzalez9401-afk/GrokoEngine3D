using OpenTK.Graphics.OpenGL4;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneRenderTarget
{
    private void CreateBloomMips()
    {
        ReleaseBloomMips();
        int w = Math.Max(1, Width / 2);
        int h = Math.Max(1, Height / 2);
        bloomMipCount = 0;
        for (int i = 0; i < MaxBloomMips && w >= 4 && h >= 4; i++)
        {
            bloomMipTex[i] = GL.GenTexture();
            bloomMipFbo[i] = GL.GenFramebuffer();
            bloomMipW[i] = w;
            bloomMipH[i] = h;
            GL.BindTexture(TextureTarget.Texture2D, bloomMipTex[i]);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, w, h, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, bloomMipFbo[i]);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, bloomMipTex[i], 0);
            bloomMipCount++;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    private void ReleaseBloomMips()
    {
        for (int i = 0; i < bloomMipCount; i++)
        {
            if (bloomMipTex[i] != 0) { GL.DeleteTexture(bloomMipTex[i]); bloomMipTex[i] = 0; }
            if (bloomMipFbo[i] != 0) { GL.DeleteFramebuffer(bloomMipFbo[i]); bloomMipFbo[i] = 0; }
        }
        bloomMipCount = 0;
    }

    // Pirámide de bloom: prefiltra+downsamplea la escena en una cadena de mips, luego upsamplea
    // con upsample tent. El resultado queda en bloomMipTex[0] como glow amplio.
    private void RenderBloom(float threshold, float scatter, float clampVal)
    {
        if (bloomMipCount == 0)
            return;

        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);
        GL.BindVertexArray(postVertexArray);
        GL.ActiveTexture(TextureUnit.Texture0);

        float knee = Math.Max(threshold * Math.Clamp(scatter, 0f, 1f) * 0.5f, 0.0001f);

        // 1) Prefiltro (umbral) + downsample: escena (full-res) -> mip0.
        GL.UseProgram(bloomDownShader);
        GL.Uniform1(uDownSourceLoc, 0);
        GL.Uniform1(uDownPrefilterLoc, 1);
        GL.Uniform1(uDownThresholdLoc, threshold);
        GL.Uniform1(uDownKneeLoc, knee);
        GL.Uniform1(uDownClampLoc, clampVal);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, bloomMipFbo[0]);
        GL.Viewport(0, 0, bloomMipW[0], bloomMipH[0]);
        GL.Uniform2(uDownTexelLoc, 1f / Math.Max(1, Width), 1f / Math.Max(1, Height));
        GL.BindTexture(TextureTarget.Texture2D, colorTexture);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // 2) Cadena de downsample.
        GL.Uniform1(uDownPrefilterLoc, 0);
        for (int i = 1; i < bloomMipCount; i++)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, bloomMipFbo[i]);
            GL.Viewport(0, 0, bloomMipW[i], bloomMipH[i]);
            GL.Uniform2(uDownTexelLoc, 1f / bloomMipW[i - 1], 1f / bloomMipH[i - 1]);
            GL.BindTexture(TextureTarget.Texture2D, bloomMipTex[i - 1]);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        // 3) Cadena de upsample aditiva: cada mip recibe el siguiente más pequeño.
        GL.UseProgram(bloomUpShader);
        GL.Uniform1(uUpSourceLoc, 0);
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        for (int i = bloomMipCount - 2; i >= 0; i--)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, bloomMipFbo[i]);
            GL.Viewport(0, 0, bloomMipW[i], bloomMipH[i]);
            GL.Uniform2(uUpTexelLoc, 1f / bloomMipW[i + 1], 1f / bloomMipH[i + 1]);
            GL.BindTexture(TextureTarget.Texture2D, bloomMipTex[i + 1]);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
        GL.Disable(EnableCap.Blend);

        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
    }

    private void CreateBloomShaders()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec2 aPosition;
            layout (location = 1) in vec2 aUv;
            out vec2 vUv;
            void main() { vUv = aUv; gl_Position = vec4(aPosition, 0.0, 1.0); }
            """;

        const string downSource = """
            #version 330 core
            in vec2 vUv;
            out vec4 frag;
            uniform sampler2D uSource;
            uniform vec2 uTexel;
            uniform int uPrefilter;
            uniform float uThreshold;
            uniform float uKnee;
            uniform float uClamp;
            vec3 Prefilter(vec3 c) {
                c = min(c, vec3(max(uClamp, 0.0)));
                float br = max(max(c.r, c.g), c.b);
                float soft = clamp(br - uThreshold + uKnee, 0.0, 2.0 * uKnee);
                soft = soft * soft / (4.0 * uKnee + 0.0001);
                float contrib = max(soft, br - uThreshold) / max(br, 0.0001);
                return c * contrib;
            }
            void main() {
                vec2 t = uTexel;
                vec3 s = texture(uSource, vUv + t * vec2(-1.0, -1.0)).rgb;
                s += texture(uSource, vUv + t * vec2( 1.0, -1.0)).rgb;
                s += texture(uSource, vUv + t * vec2(-1.0,  1.0)).rgb;
                s += texture(uSource, vUv + t * vec2( 1.0,  1.0)).rgb;
                s *= 0.25;
                if (uPrefilter == 1) s = Prefilter(s);
                frag = vec4(s, 1.0);
            }
            """;

        const string upSource = """
            #version 330 core
            in vec2 vUv;
            out vec4 frag;
            uniform sampler2D uSource;
            uniform vec2 uTexel;
            void main() {
                vec2 t = uTexel;
                vec3 s = texture(uSource, vUv + t * vec2(-1.0,  1.0)).rgb;
                s += texture(uSource, vUv + t * vec2( 0.0,  1.0)).rgb * 2.0;
                s += texture(uSource, vUv + t * vec2( 1.0,  1.0)).rgb;
                s += texture(uSource, vUv + t * vec2(-1.0,  0.0)).rgb * 2.0;
                s += texture(uSource, vUv).rgb * 4.0;
                s += texture(uSource, vUv + t * vec2( 1.0,  0.0)).rgb * 2.0;
                s += texture(uSource, vUv + t * vec2(-1.0, -1.0)).rgb;
                s += texture(uSource, vUv + t * vec2( 0.0, -1.0)).rgb * 2.0;
                s += texture(uSource, vUv + t * vec2( 1.0, -1.0)).rgb;
                frag = vec4(s / 16.0, 1.0);
            }
            """;

        bloomDownShader = LinkProgram(vertexSource, downSource);
        bloomUpShader = LinkProgram(vertexSource, upSource);

        uDownSourceLoc = GL.GetUniformLocation(bloomDownShader, "uSource");
        uDownTexelLoc = GL.GetUniformLocation(bloomDownShader, "uTexel");
        uDownPrefilterLoc = GL.GetUniformLocation(bloomDownShader, "uPrefilter");
        uDownThresholdLoc = GL.GetUniformLocation(bloomDownShader, "uThreshold");
        uDownKneeLoc = GL.GetUniformLocation(bloomDownShader, "uKnee");
        uDownClampLoc = GL.GetUniformLocation(bloomDownShader, "uClamp");
        uUpSourceLoc = GL.GetUniformLocation(bloomUpShader, "uSource");
        uUpTexelLoc = GL.GetUniformLocation(bloomUpShader, "uTexel");
    }
}
