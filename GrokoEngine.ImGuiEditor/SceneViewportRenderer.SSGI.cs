using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    // Screen-Space Diffuse Global Illumination: toma radiancia HDR de superficies vecinas
    // visibles y la rebota sobre el albedo. Es deliberadamente determinista (sin ruido de
    // Monte Carlo) para que sea util antes de contar con un historial temporal completo.
    private int _ssgiShader;
    private int _ssgiSceneLoc, _ssgiDepthLoc, _ssgiNormalLoc, _ssgiMaterialLoc, _ssgiAlbedoLoc;
    private int _ssgiInvViewProjLoc, _ssgiCameraPosLoc, _ssgiTexelLoc, _ssgiRadiusLoc, _ssgiIntensityLoc;

    private void EnsureSsgiResources()
    {
        if (_ssgiShader != 0) return;
        _ssgiShader = LinkProgram(SsgiVertexSource, SsgiFragmentSource);
        _ssgiSceneLoc = GL.GetUniformLocation(_ssgiShader, "uScene");
        _ssgiDepthLoc = GL.GetUniformLocation(_ssgiShader, "uDepth");
        _ssgiNormalLoc = GL.GetUniformLocation(_ssgiShader, "uNormalRough");
        _ssgiMaterialLoc = GL.GetUniformLocation(_ssgiShader, "uMaterial");
        _ssgiAlbedoLoc = GL.GetUniformLocation(_ssgiShader, "uAlbedo");
        _ssgiInvViewProjLoc = GL.GetUniformLocation(_ssgiShader, "uInvViewProj");
        _ssgiCameraPosLoc = GL.GetUniformLocation(_ssgiShader, "uCameraPos");
        _ssgiTexelLoc = GL.GetUniformLocation(_ssgiShader, "uTexel");
        _ssgiRadiusLoc = GL.GetUniformLocation(_ssgiShader, "uRadius");
        _ssgiIntensityLoc = GL.GetUniformLocation(_ssgiShader, "uIntensity");
    }

    private void RenderScreenSpaceGlobalIllumination(ref Matrix4 viewProj, Vector3 cameraPosition, int width, int height, int targetFramebuffer)
    {
        _statsDeferredSsgiActive = false;
        var settings = _framePostProcess;
        if (settings?.PostProcessEnabled != true || !settings.ScreenSpaceGlobalIllumination ||
            settings.ScreenSpaceGiIntensity <= 0f || ResolveAndCaptureOpaqueTexture == null)
            return;

        // Nunca leer/escribir el mismo color attachment. La copia opaca es el origen HDR estable,
        // igual que SSR; después se suma únicamente la luz de rebote al framebuffer actual.
        ResolveAndCaptureOpaqueTexture.Invoke();
        if (OpaqueTextureHandle == 0)
            return;

        EnsureSsgiResources();
        if (_deferredQuadVao == 0)
            CreateDeferredFullscreenQuad();

        Matrix4 invViewProj = viewProj;
        invViewProj.Invert();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
        GL.DrawBuffer(targetFramebuffer == 0 ? DrawBufferMode.Back : DrawBufferMode.ColorAttachment0);
        GL.Viewport(0, 0, width, height);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        GL.UseProgram(_ssgiShader);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, OpaqueTextureHandle); GL.Uniform1(_ssgiSceneLoc, 0);
        GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, _gbufferDepth); GL.Uniform1(_ssgiDepthLoc, 1);
        GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, _gbufferNormalRoughness); GL.Uniform1(_ssgiNormalLoc, 2);
        GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D, _gbufferMaterial); GL.Uniform1(_ssgiMaterialLoc, 3);
        GL.ActiveTexture(TextureUnit.Texture4); GL.BindTexture(TextureTarget.Texture2D, _gbufferAlbedo); GL.Uniform1(_ssgiAlbedoLoc, 4);
        GL.UniformMatrix4(_ssgiInvViewProjLoc, true, ref invViewProj);
        GL.Uniform3(_ssgiCameraPosLoc, cameraPosition);
        GL.Uniform2(_ssgiTexelLoc, 1f / Math.Max(1, width), 1f / Math.Max(1, height));
        GL.Uniform1(_ssgiRadiusLoc, Math.Clamp(settings.ScreenSpaceGiRadius, 0.1f, 8f));
        GL.Uniform1(_ssgiIntensityLoc, Math.Clamp(settings.ScreenSpaceGiIntensity, 0f, 2f));
        GL.BindVertexArray(_deferredQuadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        _statsDeferredSsgiActive = true;

        for (int unit = 0; unit <= 4; unit++)
        {
            GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + unit));
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
    }

    private const string SsgiVertexSource = """
        #version 330 core
        out vec2 vUv;
        void main()
        {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string SsgiFragmentSource = """
        #version 330 core
        in vec2 vUv;
        out vec4 outColor;

        uniform sampler2D uScene;
        uniform sampler2D uDepth;
        uniform sampler2D uNormalRough;
        uniform sampler2D uMaterial;
        uniform sampler2D uAlbedo;
        uniform mat4 uInvViewProj;
        uniform vec3 uCameraPos;
        uniform vec2 uTexel;
        uniform float uRadius;
        uniform float uIntensity;

        vec3 WorldFromDepth(vec2 uv, float depth)
        {
            vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            vec4 world = clip * uInvViewProj;
            return world.xyz / max(world.w, 1e-5);
        }

        float Hash12(vec2 p)
        {
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        void main()
        {
            float depth = texture(uDepth, vUv).r;
            if (depth >= 0.9999) { outColor = vec4(0.0); return; }
            vec4 material = texture(uMaterial, vUv);
            float metallic = clamp(material.x, 0.0, 1.0);
            // Los metales no reciben rebote difuso; ese canal ya lo resuelven IBL/SSR/probes.
            if (metallic > 0.95) { outColor = vec4(0.0); return; }

            vec3 worldPos = WorldFromDepth(vUv, depth);
            vec3 N = normalize(texture(uNormalRough, vUv).xyz * 2.0 - 1.0);
            vec3 albedo = max(texture(uAlbedo, vUv).rgb, vec3(0.0));
            float rotation = Hash12(gl_FragCoord.xy) * 6.2831853;
            float pixelRadius = uRadius * mix(7.0, 22.0, clamp(distance(worldPos, uCameraPos) / 25.0, 0.0, 1.0));
            vec3 accumulated = vec3(0.0);
            float weights = 0.0;

            // Ocho direcciones fijas rotadas por píxel: estable sin grano temporal y bastante
            // barato. Solo aceptamos superficies mutuamente orientadas, evitando el "halo" de
            // color sobre un plano liso.
            for (int i = 0; i < 8; i++)
            {
                float angle = rotation + 6.2831853 * (float(i) / 8.0);
                float ring = mix(0.32, 1.0, (float(i) + 0.5) / 8.0);
                vec2 suv = vUv + vec2(cos(angle), sin(angle)) * uTexel * pixelRadius * ring;
                if (any(lessThan(suv, vec2(0.001))) || any(greaterThan(suv, vec2(0.999)))) continue;
                float sampleDepth = texture(uDepth, suv).r;
                if (sampleDepth >= 0.9999) continue;
                vec3 samplePos = WorldFromDepth(suv, sampleDepth);
                vec3 delta = samplePos - worldPos;
                float d = length(delta);
                if (d < 0.025 || d > uRadius) continue;
                vec3 L = delta / d;
                vec3 sampleNormal = normalize(texture(uNormalRough, suv).xyz * 2.0 - 1.0);
                float geometry = max(dot(N, L), 0.0) * max(dot(sampleNormal, -L), 0.0);
                float falloff = (1.0 - d / uRadius);
                float weight = geometry * falloff * falloff;
                vec3 radiance = min(max(texture(uScene, suv).rgb, vec3(0.0)), vec3(5.0));
                accumulated += radiance * weight;
                weights += weight;
            }

            vec3 indirect = weights > 0.0001 ? accumulated / weights : vec3(0.0);
            // 1/pi y el límite HDR mantienen energía razonable incluso cerca de una pared blanca.
            vec3 bounce = min(indirect * albedo * (1.0 - metallic) * (uIntensity / 3.14159265), vec3(1.5));
            outColor = vec4(bounce, 1.0);
        }
        """;
}
