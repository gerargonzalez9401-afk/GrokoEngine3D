using OpenTK.Graphics.OpenGL4;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    // Prefiltrado GGX en GPU para el cubemap del reflection probe. Convierte el cubemap base
    // (mips de caja) en un cubemap RGBA16F con una cadena de mips por RUGOSIDAD, usando importance
    // sampling GGX (split-sum de Karis), igual que el HDRI. Con esto los reflejos rugosos tienen
    // la forma correcta (no un simple blur de caja) → calidad de reflejo al nivel del HDRI.
    private int _probePrefilterShader;
    private int _probePrefilterFbo;
    private int _probePrefilterVao;
    private int _ppRoughnessLoc, _ppFaceLoc, _ppEnvMapLoc, _ppSrcResLoc;

    private void EnsureProbePrefilterResources()
    {
        if (_probePrefilterShader == 0)
        {
            _probePrefilterShader = LinkProgram(ProbePrefilterVertexSource, ProbePrefilterFragmentSource);
            _ppRoughnessLoc = GL.GetUniformLocation(_probePrefilterShader, "uRoughness");
            _ppFaceLoc = GL.GetUniformLocation(_probePrefilterShader, "uFace");
            _ppEnvMapLoc = GL.GetUniformLocation(_probePrefilterShader, "uEnvMap");
            _ppSrcResLoc = GL.GetUniformLocation(_probePrefilterShader, "uSrcRes");
        }
        if (_probePrefilterVao == 0)
            _probePrefilterVao = GL.GenVertexArray();
        if (_probePrefilterFbo == 0)
            _probePrefilterFbo = GL.GenFramebuffer();
    }

    /// <summary>
    /// Genera un cubemap prefiltrado GGX (RGBA16F, cadena de mips por rugosidad) a partir de un
    /// cubemap fuente que YA tiene mips (para reducir fireflies). Devuelve el nuevo cubemap; el
    /// llamador es dueño de liberarlo. maxLod = índice del último mip.
    /// </summary>
    private int PrefilterProbeCubemap(int srcCube, int srcSize, out float maxLod)
    {
        EnsureProbePrefilterResources();

        // mip0 a resolución alta (512) → el reflejo liso queda NÍTIDO. RGBA16F a 512 pesa menos que
        // el RGBA8 a 1024 del box previo, así que no hay coste extra de memoria.
        int baseSize = Math.Clamp(srcSize, 8, 512);
        int mipCount = Math.Clamp((int)MathF.Floor(MathF.Log2(baseSize)) + 1, 1, 7);
        maxLod = mipCount - 1;

        int dst = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, dst);
        for (int face = 0; face < 6; face++)
            for (int mip = 0; mip < mipCount; mip++)
            {
                int s = Math.Max(1, baseSize >> mip);
                GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, mip, PixelInternalFormat.Rgba16f,
                    s, s, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            }
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, mipCount - 1);

        // Guardar estado para restaurarlo (esto corre en medio de la carga de assets).
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
        int[] prevVp = new int[4];
        GL.GetInteger(GetPName.Viewport, prevVp);
        bool depthWasOn = GL.IsEnabled(EnableCap.DepthTest);
        bool cullWasOn = GL.IsEnabled(EnableCap.CullFace);
        bool blendWasOn = GL.IsEnabled(EnableCap.Blend);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _probePrefilterFbo);
        GL.UseProgram(_probePrefilterShader);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.TextureCubeMap, srcCube);
        GL.Uniform1(_ppEnvMapLoc, 0);
        GL.Uniform1(_ppSrcResLoc, (float)Math.Max(1, srcSize));
        GL.BindVertexArray(_probePrefilterVao);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

        for (int mip = 0; mip < mipCount; mip++)
        {
            int s = Math.Max(1, baseSize >> mip);
            float roughness = mipCount <= 1 ? 0f : (float)mip / (mipCount - 1);
            GL.Viewport(0, 0, s, s);
            GL.Uniform1(_ppRoughnessLoc, roughness);
            for (int face = 0; face < 6; face++)
            {
                GL.Uniform1(_ppFaceLoc, face);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + face, dst, mip);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }

        // Restaurar.
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.BindTexture(TextureTarget.TextureCubeMap, 0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
        GL.Viewport(prevVp[0], prevVp[1], prevVp[2], prevVp[3]);
        if (depthWasOn) GL.Enable(EnableCap.DepthTest);
        if (cullWasOn) GL.Enable(EnableCap.CullFace);
        if (blendWasOn) GL.Enable(EnableCap.Blend);

        return dst;
    }

    private const string ProbePrefilterVertexSource = """
        #version 330 core
        out vec2 vUv;
        void main()
        {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string ProbePrefilterFragmentSource = """
        #version 330 core
        in vec2 vUv;
        out vec4 outColor;
        uniform samplerCube uEnvMap;
        uniform float uRoughness;
        uniform int uFace;
        uniform float uSrcRes;
        const float PI = 3.14159265359;

        // Dirección de mundo para (cara, uv) según la convención de cubemap de OpenGL.
        vec3 DirForFace(int face, vec2 uv)
        {
            vec2 st = uv * 2.0 - 1.0;
            if (face == 0) return normalize(vec3( 1.0, -st.y, -st.x));
            if (face == 1) return normalize(vec3(-1.0, -st.y,  st.x));
            if (face == 2) return normalize(vec3( st.x,  1.0,  st.y));
            if (face == 3) return normalize(vec3( st.x, -1.0, -st.y));
            if (face == 4) return normalize(vec3( st.x, -st.y,  1.0));
            return              normalize(vec3(-st.x, -st.y, -1.0));
        }

        float RadicalInverse_VdC(uint bits)
        {
            bits = (bits << 16u) | (bits >> 16u);
            bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
            bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
            bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
            bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
            return float(bits) * 2.3283064365386963e-10;
        }
        vec2 Hammersley(uint i, uint n) { return vec2(float(i) / float(n), RadicalInverse_VdC(i)); }

        vec3 ImportanceSampleGGX(vec2 Xi, vec3 N, float roughness)
        {
            float a = roughness * roughness;
            float phi = 2.0 * PI * Xi.x;
            float cosT = sqrt((1.0 - Xi.y) / (1.0 + (a * a - 1.0) * Xi.y));
            float sinT = sqrt(1.0 - cosT * cosT);
            vec3 H = vec3(cos(phi) * sinT, sin(phi) * sinT, cosT);
            vec3 up = abs(N.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
            vec3 tx = normalize(cross(up, N));
            vec3 ty = cross(N, tx);
            return normalize(tx * H.x + ty * H.y + N * H.z);
        }

        void main()
        {
            vec3 N = DirForFace(uFace, vUv);
            // mip0 (rugosidad 0): copia directa nítida.
            if (uRoughness <= 0.001) { outColor = vec4(textureLod(uEnvMap, N, 0.0).rgb, 1.0); return; }

            vec3 R = N;
            vec3 V = R;
            const uint SAMPLE_COUNT = 64u;
            float totalWeight = 0.0;
            vec3 prefiltered = vec3(0.0);
            float saTexel = 4.0 * PI / (6.0 * uSrcRes * uSrcRes);

            for (uint i = 0u; i < SAMPLE_COUNT; i++)
            {
                vec2 Xi = Hammersley(i, SAMPLE_COUNT);
                vec3 H = ImportanceSampleGGX(Xi, N, uRoughness);
                vec3 L = normalize(2.0 * dot(V, H) * H - V);
                float NdotL = max(dot(N, L), 0.0);
                if (NdotL <= 0.0) continue;

                // Selección de mip (Chetan Jags) para evitar fireflies en rugosidad alta.
                float NdotH = max(dot(N, H), 0.0);
                float HdotV = max(dot(H, V), 0.0);
                float a = uRoughness * uRoughness;
                float d = (NdotH * a - NdotH) * NdotH + 1.0;
                float D = (a * a) / max(PI * d * d, 1e-6);
                float pdf = (D * NdotH / max(4.0 * HdotV, 1e-6)) + 1e-4;
                float saSample = 1.0 / (float(SAMPLE_COUNT) * pdf);
                float mipLevel = 0.5 * log2(saSample / saTexel);

                prefiltered += textureLod(uEnvMap, L, max(mipLevel, 0.0)).rgb * NdotL;
                totalWeight += NdotL;
            }

            outColor = vec4(prefiltered / max(totalWeight, 1e-3), 1.0);
        }
        """;
}
