using OpenTK.Graphics.OpenGL4;
using System;

namespace GrokoEngine.ImGuiEditor;

/// <summary>
/// Antialiasing morfológico por imagen para el camino Deferred.
///
/// El trabajo se divide en dos pases deliberadamente: primero se calcula un mapa de
/// bordes con contraste local y después se hace una búsqueda direccional antes de
/// mezclar. Separarlo evita el desenfoque de cinco taps que tenía el antiguo FXAA.
/// Es una variante SMAA 1x (sin temporal history), por lo que es estable en el editor
/// y no necesita motion vectors.
/// </summary>
internal sealed partial class SceneRenderTarget
{
    private int uSmaaEdgeSceneLocation;
    private int uSmaaEdgeTexelLocation;
    private int uSmaaResolveSceneLocation;
    private int uSmaaResolveEdgesLocation;
    private int uSmaaResolveTexelLocation;

    private void CreateSmaaShaders()
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

        // Color-edge detection. R contiene contornos verticales (la mezcla ocurrirá
        // horizontalmente) y G contornos horizontales (la mezcla ocurrirá verticalmente).
        const string edgeFragment = @"#version 330 core
            in vec2 vUv;
            uniform sampler2D uScene;
            uniform vec2 uTexel;
            out vec4 outEdges;

            float luma(vec3 color)
            {
                return dot(color, vec3(0.2126, 0.7152, 0.0722));
            }

            vec2 safeUv(vec2 uv)
            {
                return clamp(uv, uTexel * 0.5, vec2(1.0) - uTexel * 0.5);
            }

            void main()
            {
                float center = luma(texture(uScene, vUv).rgb);
                float left = luma(texture(uScene, safeUv(vUv - vec2(uTexel.x, 0.0))).rgb);
                float top = luma(texture(uScene, safeUv(vUv - vec2(0.0, uTexel.y))).rgb);
                float right = luma(texture(uScene, safeUv(vUv + vec2(uTexel.x, 0.0))).rgb);
                float bottom = luma(texture(uScene, safeUv(vUv + vec2(0.0, uTexel.y))).rgb);
                vec2 delta = abs(vec2(center - left, center - top));
                float threshold = max(0.035, max(max(center, left), max(top, max(right, bottom))) * 0.075);
                vec2 edges = step(vec2(threshold), delta);
                vec2 opposite = abs(vec2(center - right, center - bottom));
                edges *= step(delta * 0.45, max(delta, opposite));
                outEdges = vec4(edges, 0.0, 1.0);
            }
            ";

        // Neighborhood blend con búsqueda en la dirección del borde. La longitud del
        // patrón decide el peso: líneas largas reciben una corrección un poco mayor,
        // mientras que detalle fino mantiene el color original.
        const string resolveFragment = @"#version 330 core
            in vec2 vUv;
            uniform sampler2D uScene;
            uniform sampler2D uEdges;
            uniform vec2 uTexel;
            out vec4 outColor;

            vec2 safeUv(vec2 uv)
            {
                return clamp(uv, uTexel * 0.5, vec2(1.0) - uTexel * 0.5);
            }

            float searchVertical(vec2 uv, float direction)
            {
                float span = 0.0;
                for (int i = 1; i <= 12; ++i)
                {
                    float edge = texture(uEdges, safeUv(uv + vec2(0.0, direction * float(i)) * uTexel)).r;
                    if (edge < 0.025) break;
                    span += 1.0;
                }
                return span;
            }

            float searchHorizontal(vec2 uv, float direction)
            {
                float span = 0.0;
                for (int i = 1; i <= 12; ++i)
                {
                    float edge = texture(uEdges, safeUv(uv + vec2(direction * float(i), 0.0) * uTexel)).g;
                    if (edge < 0.025) break;
                    span += 1.0;
                }
                return span;
            }

            void main()
            {
                vec3 center = texture(uScene, vUv).rgb;
                vec2 edge = texture(uEdges, vUv).rg;
                float vertical = max(edge.r, texture(uEdges, safeUv(vUv + vec2(uTexel.x, 0.0))).r);
                float horizontal = max(edge.g, texture(uEdges, safeUv(vUv + vec2(0.0, uTexel.y))).g);
                vec3 result = center;

                if (vertical > 0.025)
                {
                    float span = searchVertical(vUv, -1.0) + searchVertical(vUv, 1.0);
                    float weight = vertical * (0.17 + min(span, 10.0) * 0.018);
                    vec3 across = 0.5 * (texture(uScene, safeUv(vUv - vec2(0.5 * uTexel.x, 0.0))).rgb +
                                          texture(uScene, safeUv(vUv + vec2(0.5 * uTexel.x, 0.0))).rgb);
                    result = mix(result, across, clamp(weight, 0.0, 0.36));
                }
                if (horizontal > 0.025)
                {
                    float span = searchHorizontal(vUv, -1.0) + searchHorizontal(vUv, 1.0);
                    float weight = horizontal * (0.17 + min(span, 10.0) * 0.018);
                    vec3 across = 0.5 * (texture(uScene, safeUv(vUv - vec2(0.0, 0.5 * uTexel.y))).rgb +
                                          texture(uScene, safeUv(vUv + vec2(0.0, 0.5 * uTexel.y))).rgb);
                    result = mix(result, across, clamp(weight, 0.0, 0.36));
                }
                outColor = vec4(result, 1.0);
            }
            ";

        smaaEdgeShader = LinkProgram(vertexSource, edgeFragment);
        smaaResolveShader = LinkProgram(vertexSource, resolveFragment);
        uSmaaEdgeSceneLocation = GL.GetUniformLocation(smaaEdgeShader, "uScene");
        uSmaaEdgeTexelLocation = GL.GetUniformLocation(smaaEdgeShader, "uTexel");
        uSmaaResolveSceneLocation = GL.GetUniformLocation(smaaResolveShader, "uScene");
        uSmaaResolveEdgesLocation = GL.GetUniformLocation(smaaResolveShader, "uEdges");
        uSmaaResolveTexelLocation = GL.GetUniformLocation(smaaResolveShader, "uTexel");
    }

    private void ResizeSmaaBuffers()
    {
        AllocateColorTarget(smaaSourceTexture, smaaSourceFramebuffer, "SMAA source");
        AllocateColorTarget(smaaEdgeTexture, smaaEdgeFramebuffer, "SMAA edge");
    }

    private void AllocateColorTarget(int texture, int targetFramebuffer, string label)
    {
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, texture, 0);
        FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"{label} framebuffer incomplete: {status}");
    }

    private void ApplySmaa()
    {
        GL.Viewport(0, 0, Width, Height);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, smaaEdgeFramebuffer);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.UseProgram(smaaEdgeShader);
        SetUniform(uSmaaEdgeSceneLocation, 0);
        if (uSmaaEdgeTexelLocation >= 0)
            GL.Uniform2(uSmaaEdgeTexelLocation, 1f / Math.Max(1, Width), 1f / Math.Max(1, Height));
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, smaaSourceTexture);
        GL.BindVertexArray(postVertexArray);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, postFramebuffer);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.UseProgram(smaaResolveShader);
        SetUniform(uSmaaResolveSceneLocation, 0);
        SetUniform(uSmaaResolveEdgesLocation, 1);
        if (uSmaaResolveTexelLocation >= 0)
            GL.Uniform2(uSmaaResolveTexelLocation, 1f / Math.Max(1, Width), 1f / Math.Max(1, Height));
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, smaaSourceTexture);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, smaaEdgeTexture);
        GL.BindVertexArray(postVertexArray);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.BindVertexArray(0);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
        GL.DepthMask(true);
    }

    private void ReleaseSmaaResources()
    {
        if (smaaEdgeTexture != 0) { GL.DeleteTexture(smaaEdgeTexture); smaaEdgeTexture = 0; }
        if (smaaEdgeFramebuffer != 0) { GL.DeleteFramebuffer(smaaEdgeFramebuffer); smaaEdgeFramebuffer = 0; }
        if (smaaSourceTexture != 0) { GL.DeleteTexture(smaaSourceTexture); smaaSourceTexture = 0; }
        if (smaaSourceFramebuffer != 0) { GL.DeleteFramebuffer(smaaSourceFramebuffer); smaaSourceFramebuffer = 0; }
    }
}
