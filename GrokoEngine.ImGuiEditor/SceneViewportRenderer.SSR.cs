using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    // Screen-Space Reflections. Corre tras el pase deferred de opacos: ray-march en espacio de
    // pantalla contra la profundidad del G-buffer y muestrea el COLOR ya iluminado de la escena
    // (OpaqueTextureHandle). Donde el rayo choca, mezcla el reflejo real de pantalla sobre la
    // superficie (según metalicidad/fresnel); donde no, deja el reflejo del probe/IBL intacto.
    // Off por defecto: sin acumulación temporal (TAA) el SSR de una pasada tiene grano/huecos
    // inherentes. Los reflejos limpios los da el reflection probe (GGX). Queda como toggle por si
    // en el futuro se añade TAA/HiZ para dejarlo a nivel Unreal.
    public bool ScreenSpaceReflections { get; set; }

    private int _ssrShader;
    private int _ssrSceneLoc, _ssrDepthLoc, _ssrNormalLoc, _ssrMaterialLoc;
    private int _ssrInvViewProjLoc, _ssrViewProjLoc, _ssrCameraPosLoc, _ssrTexelLoc;
    private int _ssrMaxDistanceLoc, _ssrThicknessLoc, _ssrStepsLoc, _ssrStrengthLoc;

    private void EnsureSsrResources()
    {
        if (_ssrShader != 0) return;
        _ssrShader = LinkProgram(SsrVertexSource, SsrFragmentSource);
        _ssrSceneLoc = GL.GetUniformLocation(_ssrShader, "uScene");
        _ssrDepthLoc = GL.GetUniformLocation(_ssrShader, "uDepth");
        _ssrNormalLoc = GL.GetUniformLocation(_ssrShader, "uNormalRough");
        _ssrMaterialLoc = GL.GetUniformLocation(_ssrShader, "uMaterial");
        _ssrInvViewProjLoc = GL.GetUniformLocation(_ssrShader, "uInvViewProj");
        _ssrViewProjLoc = GL.GetUniformLocation(_ssrShader, "uViewProj");
        _ssrCameraPosLoc = GL.GetUniformLocation(_ssrShader, "uCameraPos");
        _ssrTexelLoc = GL.GetUniformLocation(_ssrShader, "uTexel");
        _ssrMaxDistanceLoc = GL.GetUniformLocation(_ssrShader, "uMaxDistance");
        _ssrThicknessLoc = GL.GetUniformLocation(_ssrShader, "uThickness");
        _ssrStepsLoc = GL.GetUniformLocation(_ssrShader, "uSteps");
        _ssrStrengthLoc = GL.GetUniformLocation(_ssrShader, "uStrength");
    }

    private void RenderScreenSpaceReflections(ref Matrix4 viewProj, Vector3 cameraPosition, int width, int height, int targetFramebuffer)
    {
        if (!ScreenSpaceReflections || ResolveAndCaptureOpaqueTexture == null)
            return;

        // Capturar el color ya iluminado de la escena (deferred opaco) para reflejarlo.
        ResolveAndCaptureOpaqueTexture.Invoke();
        if (OpaqueTextureHandle == 0)
            return;

        EnsureSsrResources();
        if (_deferredQuadVao == 0)
            CreateDeferredFullscreenQuad();

        Matrix4 invViewProj = viewProj;
        invViewProj.Invert();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
        GL.DrawBuffer(targetFramebuffer == 0 ? DrawBufferMode.Back : DrawBufferMode.ColorAttachment0);
        GL.Viewport(0, 0, width, height);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        // Mezcla el reflejo sobre la superficie según su alpha (metalicidad·fresnel·fade).
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        GL.UseProgram(_ssrShader);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, OpaqueTextureHandle);
        GL.Uniform1(_ssrSceneLoc, 0);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferDepth);
        GL.Uniform1(_ssrDepthLoc, 1);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferNormalRoughness);
        GL.Uniform1(_ssrNormalLoc, 2);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferMaterial);
        GL.Uniform1(_ssrMaterialLoc, 3);

        GL.UniformMatrix4(_ssrInvViewProjLoc, true, ref invViewProj);
        GL.UniformMatrix4(_ssrViewProjLoc, true, ref viewProj);
        GL.Uniform3(_ssrCameraPosLoc, cameraPosition);
        GL.Uniform2(_ssrTexelLoc, 1f / Math.Max(1, width), 1f / Math.Max(1, height));
        // Pasos más finos (menor distancia + más pasos) → reflejo sin escalonado/"picado".
        GL.Uniform1(_ssrMaxDistanceLoc, 30f);
        GL.Uniform1(_ssrThicknessLoc, 0.8f);
        GL.Uniform1(_ssrStepsLoc, 140);        // marcheo muy fino → sin escalón ni necesidad de grano
        GL.Uniform1(_ssrStrengthLoc, 1f);

        GL.BindVertexArray(_deferredQuadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.UseProgram(0);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
    }

    private const string SsrVertexSource = """
        #version 330 core
        out vec2 vUv;
        void main()
        {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string SsrFragmentSource = """
        #version 330 core
        in vec2 vUv;
        out vec4 outColor;

        uniform sampler2D uScene;
        uniform sampler2D uDepth;
        uniform sampler2D uNormalRough;
        uniform sampler2D uMaterial;
        uniform mat4 uInvViewProj;
        uniform mat4 uViewProj;
        uniform vec3 uCameraPos;
        uniform vec2 uTexel;
        uniform float uMaxDistance;
        uniform float uThickness;
        uniform int uSteps;
        uniform float uStrength;

        vec3 WorldFromDepth(vec2 uv, float depth)
        {
            vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            vec4 w = clip * uInvViewProj;
            return w.xyz / max(w.w, 1e-5);
        }

        void main()
        {
            outColor = vec4(0.0);
            float depth = texture(uDepth, vUv).r;
            if (depth >= 0.9999) return;                 // cielo

            vec4 mat = texture(uMaterial, vUv);
            float metallic = clamp(mat.x, 0.0, 1.0);
            float envRefl = mat.z >= 0.5 ? 1.0 : 0.0;
            vec4 nr = texture(uNormalRough, vUv);
            float roughness = clamp(nr.w, 0.0, 1.0);

            // SSR SOLO en superficies claramente metálicas y lisas (espejos/pisos pulidos).
            // Antes daba un 0.15 base a TODAS las superficies → toda la escena reflejaba en pantalla
            // y se veía embarrada. Ahora exige metálico alto + rugosidad baja.
            float reflectivity = envRefl
                * smoothstep(0.45, 0.85, metallic)
                * (1.0 - smoothstep(0.18, 0.45, roughness));
            if (reflectivity < 0.02) return;

            vec3 N = normalize(nr.xyz * 2.0 - 1.0);
            vec3 worldPos = WorldFromDepth(vUv, depth);
            vec3 V = normalize(worldPos - uCameraPos);
            vec3 R = reflect(V, N);
            if (dot(R, N) < 0.0) return;

            // Ray-march en mundo, proyectando cada paso a pantalla.
            float stepLen = uMaxDistance / float(uSteps);
            // Jitter MUY leve solo para suavizar el escalón, sin generar grano visible (antes 0..1
            // metía demasiado ruido). Con muchos pasos finos, el marcheo ya es liso.
            float jitter = fract(sin(dot(vUv, vec2(12.9898, 78.233))) * 43758.5453) * 0.35;
            vec3 rayPos = worldPos + N * 0.02 + R * stepLen * jitter;
            vec2 hitUv = vec2(-1.0);
            float hitConfidence = 0.0;
            float hitDist = 0.0;

            for (int i = 0; i < uSteps; i++)
            {
                rayPos += R * stepLen;
                vec4 clip = vec4(rayPos, 1.0) * uViewProj;
                if (clip.w <= 0.0) break;
                vec3 ndc = clip.xyz / clip.w;
                vec2 suv = ndc.xy * 0.5 + 0.5;
                if (suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0) break;

                float sceneDepth = texture(uDepth, suv).r;
                if (sceneDepth >= 0.9999) continue;      // cielo: sin choque
                vec3 sceneWorld = WorldFromDepth(suv, sceneDepth);

                float rayDist = length(rayPos - uCameraPos);
                float sceneDist = length(sceneWorld - uCameraPos);
                float d = rayDist - sceneDist;            // >0 : el rayo pasó DETRÁS de la superficie
                if (d > 0.0 && d < uThickness)
                {
                    // Refinamiento binario para afinar el punto de choque.
                    vec3 a = rayPos - R * stepLen;
                    vec3 b = rayPos;
                    for (int k = 0; k < 8; k++)
                    {
                        vec3 mid = (a + b) * 0.5;
                        vec4 mc = vec4(mid, 1.0) * uViewProj;
                        vec2 muv = (mc.xy / mc.w) * 0.5 + 0.5;
                        float sd = texture(uDepth, muv).r;
                        vec3 sw = WorldFromDepth(muv, sd);
                        if (length(mid - uCameraPos) - length(sw - uCameraPos) > 0.0) b = mid; else a = mid;
                        suv = muv;
                    }
                    hitUv = suv;
                    hitConfidence = 1.0;
                    hitDist = distance(rayPos, worldPos);
                    break;
                }
            }

            if (hitConfidence <= 0.0) return;

            // Fades: bordes de pantalla, fresnel y distancia.
            vec2 edge = smoothstep(vec2(0.0), vec2(0.08), hitUv) * (1.0 - smoothstep(vec2(0.92), vec2(1.0), hitUv));
            float edgeFade = edge.x * edge.y;
            float fresnel = pow(1.0 - max(dot(-V, N), 0.0), 3.0);
            float fresnelBoost = mix(0.25, 1.0, fresnel);

            // Los choques lejanos son poco fiables (estiran/embarran) → se desvanecen.
            float distFade = 1.0 - smoothstep(0.5, 1.0, hitDist / uMaxDistance);

            // Blur GLOSSY: el radio crece con la distancia del reflejo y la rugosidad. Da el look de
            // piso pulido real (nítido cerca del objeto, suave lejos) y disimula el escalón residual.
            float blurRadius = (hitDist * 0.5 + 0.5) + roughness * 22.0;
            vec3 reflColor = vec3(0.0);
            float wsum = 0.0;
            for (int bx = -2; bx <= 2; bx++)
            for (int by = -2; by <= 2; by++)
            {
                vec2 off = vec2(float(bx), float(by)) * uTexel * blurRadius;
                float w = 1.0 - 0.18 * float(abs(bx) + abs(by));
                reflColor += texture(uScene, hitUv + off).rgb * w;
                wsum += w;
            }
            reflColor /= max(wsum, 1e-4);

            float alpha = clamp(reflectivity * edgeFade * fresnelBoost * distFade * uStrength, 0.0, 1.0);
            outColor = vec4(reflColor, alpha);
        }
        """;
}
