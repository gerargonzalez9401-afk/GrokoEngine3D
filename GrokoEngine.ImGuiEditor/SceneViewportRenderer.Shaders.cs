using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using Vector2 = System.Numerics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;
using Matrix4 = OpenTK.Mathematics.Matrix4;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private static int CreateShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aPosition;
            layout (location = 1) in vec4 aColor;
            uniform mat4 uMvp;
            out vec4 vColor;
            void main()
            {
                vColor = aColor;
                gl_Position = vec4(aPosition, 1.0) * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec4 vColor;
            out vec4 outColor;
            void main()
            {
                outColor = vColor;
            }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }

    


    private static int CreateSolidShader()
    {
        // Los vértices ya están en espacio mundo (pre-transformados en CPU)
        // Pasamos la posición mundo al fragment para calcular point lights
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aPosition;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in vec4 aColor;
            layout (location = 3) in vec2 aUv;
            layout (location = 4) in vec4 aMaterial;
            layout (location = 5) in vec4 aEmission;
            layout (location = 6) in mat4 aInstanceModel;
            layout (location = 10) in vec4 aBoneIndices;
            layout (location = 11) in vec4 aBoneWeights;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            uniform int  uUseInstancing;
            uniform int  uUseSkinning;
            uniform mat4 uBones[128];
            uniform mat4 uLightMvp;
            uniform int  uUseSurfaceUniforms;
            uniform vec4 uSurfaceColor;
            uniform vec4 uSurfaceMaterial;
            uniform vec4 uSurfaceEmission;
            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec4 vColor;
            out vec2 vUv;
            out vec4 vMaterial;
            out vec4 vEmission;
            out vec4 vLightPos;
            void main()
            {
                mat4 model = uUseInstancing != 0 ? aInstanceModel : uModel;
                vec4 world = vec4(aPosition, 1.0) * model;
                vec3 normalWs = (vec4(aNormal, 0.0) * model).xyz;
                if (uUseSkinning != 0)
                {
                    vec4 skinnedPos = vec4(0.0);
                    vec3 skinnedNormal = vec3(0.0);
                    float totalWeight = 0.0;
                    for (int i = 0; i < 4; i++)
                    {
                        int boneIndex = int(aBoneIndices[i] + 0.5);
                        float weight = aBoneWeights[i];
                        if (boneIndex >= 0 && boneIndex < 128 && weight > 0.0)
                        {
                            mat4 bone = uBones[boneIndex];
                            skinnedPos += (vec4(aPosition, 1.0) * bone) * weight;
                            skinnedNormal += (vec4(aNormal, 0.0) * bone).xyz * weight;
                            totalWeight += weight;
                        }
                    }
                    if (totalWeight > 0.0001)
                    {
                        world = skinnedPos;
                        normalWs = skinnedNormal;
                    }
                }
                vWorldPos = world.xyz;
                vNormal   = length(normalWs) > 0.0001 ? normalize(normalWs) : vec3(0.0, 1.0, 0.0);
                vColor    = uUseSurfaceUniforms != 0 ? uSurfaceColor : aColor;
                vUv       = aUv;
                vMaterial = uUseSurfaceUniforms != 0 ? uSurfaceMaterial : aMaterial;
                vEmission = uUseSurfaceUniforms != 0 ? uSurfaceEmission : aEmission;
                vLightPos = world * uLightMvp;
                gl_Position = world * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            #define MAX_POINT_LIGHTS 8
            #define MAX_SPOT_LIGHTS 4
            #define MAX_AREA_LIGHTS 4

            in vec3 vWorldPos;
            in vec3 vNormal;
            in vec4 vColor;
            in vec2 vUv;
            in vec4 vMaterial;
            in vec4 vEmission;
            in vec4 vLightPos;

            // Textura
            uniform sampler2D uTexture;
            uniform bool      uHasTexture;
            uniform sampler2D uNormalMap;
            uniform bool      uHasNormalMap;
            uniform sampler2D uRoughnessMap;
            uniform bool      uHasRoughnessMap;
            uniform sampler2D uMetallicMap;
            uniform bool      uHasMetallicMap;
            uniform sampler2DArray uShadowMap;
            uniform int       uShadowEnabled;
            uniform float     uShadowStrength;
            uniform int       uCascadeCount;
            uniform mat4      uCascadeLightMvp[5];
            uniform float     uCascadeSplit[5];
            uniform vec3      uShadowCameraPos;
            // Radio del kernel PCF compartido por las sombras direccionales y de foco —
            // controlado desde el editor vía Settings > Lighting > Shadow Quality
            // (Low = 0 → 1 muestra "dura"; Medium/High = 1 → 3x3; Ultra = 2 → 5x5),
            // emulando el efecto de "Soft Shadows" / Shadow Resolution de Unity:
            // más muestras = penumbra más suave y bordes con menos aliasing.
            uniform int       uShadowPcfRadius;
            uniform float     uShadowBiasScale;
            uniform sampler2D uSpotShadowMap;
            uniform mat4      uSpotLightMvp;
            uniform int       uSpotShadowEnabled;
            uniform float     uSpotShadowStrength;
            uniform samplerCube uPointShadowCube;
            uniform int       uPointShadowEnabled;
            uniform float     uPointShadowStrength;
            uniform vec3      uPointShadowPos;
            uniform float     uPointShadowFar;

            // Luz ambiental
            uniform vec3  uAmbientColor;
            uniform float uAmbientIntensity;
            uniform float uSkyStrength;

            // Luz direccional (sol)
            uniform vec3  uDirDir;
            uniform vec3  uDirColor;
            uniform float uDirIntensity;
            uniform vec3  uCameraPos;

            // Point lights
            uniform int   uPointCount;
            uniform vec3  uPointPos[MAX_POINT_LIGHTS];
            uniform vec3  uPointColor[MAX_POINT_LIGHTS];
            uniform float uPointIntensity[MAX_POINT_LIGHTS];
            uniform float uPointRange[MAX_POINT_LIGHTS];

            // Spot lights
            uniform int   uSpotCount;
            uniform vec3  uSpotPos[MAX_SPOT_LIGHTS];
            uniform vec3  uSpotDir[MAX_SPOT_LIGHTS];
            uniform vec3  uSpotColor[MAX_SPOT_LIGHTS];
            uniform float uSpotIntensity[MAX_SPOT_LIGHTS];
            uniform float uSpotRange[MAX_SPOT_LIGHTS];
            uniform float uSpotAngle[MAX_SPOT_LIGHTS];

            // Area / rectangle lights, approximated as soft forward-facing emitters
            uniform int   uAreaCount;
            uniform vec3  uAreaPos[MAX_AREA_LIGHTS];
            uniform vec3  uAreaDir[MAX_AREA_LIGHTS];
            uniform vec3  uAreaColor[MAX_AREA_LIGHTS];
            uniform float uAreaIntensity[MAX_AREA_LIGHTS];
            uniform float uAreaRange[MAX_AREA_LIGHTS];
            uniform vec2  uAreaSize[MAX_AREA_LIGHTS];

            // Modo PRO: 1 = flujo de color "Linear" (decodifica sRGB->linear en
            // las texturas/albedo e ilumina en lineal, como el pipeline por
            // defecto de Unity con color space "Linear"); 0 = flujo "Gamma"
            // clásico, sin conversión, para comparar in situ cuál look queda
            // mejor. La codificación final linear->sRGB la hace el post-proceso.
            uniform int   uColorSpaceLinear;
            // Procedural IBL (Image-Based Lighting): 1 = surfaces reflect the
            // environment (sky/ground from the ambient) plus energy-conserving
            // diffuse; 0 = flat classic ambient. Togglable for A/B comparison,
            // like the color space.
            uniform int   uUseIBL;
            // HDRI de entorno (IBL Fase 2): equirectangular con mips. uHasEnvMap=1
            // cuando hay HDRI cargado; uEnvMaxLod = nivel de mip mas alto.
            uniform sampler2D uEnvMap;
            uniform int       uHasEnvMap;
            uniform float     uEnvMaxLod;
            uniform float uAoStrength;
            uniform float uFogDensity;
            uniform vec3  uFogColor;
            uniform float uVolumetricStrength;
            uniform int   uDebugView;

            out vec4 outColor;

            float RangeAttenuation(float dist, float range)
            {
                float x = clamp(1.0 - dist / max(range, 0.001), 0.0, 1.0);
                return x * x * (3.0 - 2.0 * x);
            }

            const float PI = 3.14159265359;

            float DistributionGGX(vec3 N, vec3 H, float roughness)
            {
                float a = roughness * roughness;
                float a2 = a * a;
                float nh = max(dot(N, H), 0.0);
                float nh2 = nh * nh;
                float denom = (nh2 * (a2 - 1.0) + 1.0);
                return a2 / max(PI * denom * denom, 0.0001);
            }

            float GeometrySchlickGGX(float ndv, float roughness)
            {
                float r = roughness + 1.0;
                float k = (r * r) / 8.0;
                return ndv / max(ndv * (1.0 - k) + k, 0.0001);
            }

            float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
            {
                return GeometrySchlickGGX(max(dot(N, V), 0.0), roughness) *
                       GeometrySchlickGGX(max(dot(N, L), 0.0), roughness);
            }

            vec3 FresnelSchlick(float cosTheta, vec3 F0)
            {
                return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
            }

            vec3 AddDirectLight(vec3 N, vec3 V, vec3 L, vec3 radiance, float intensity, vec3 albedo, float roughness, float metallic)
            {
                float ndl = max(dot(N, L), 0.0);
                if (ndl <= 0.0) return vec3(0.0);
                vec3 H = normalize(L + V);
                vec3 F0 = mix(vec3(0.04), albedo, metallic);
                float D = DistributionGGX(N, H, roughness);
                float G = GeometrySmith(N, V, L, roughness);
                vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
                vec3 specular = (D * G * F) / max(4.0 * max(dot(N, V), 0.0) * ndl, 0.0001);
                vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
                vec3 diffuse = kD * albedo / PI;
                return (diffuse + specular) * radiance * intensity * ndl;
            }

            // PCF (Percentage-Closer Filtering) de radio variable: muestrea un kernel
            // cuadrado de (2*radius+1)^2 texels alrededor del punto proyectado y
            // promedia cuántos están en sombra. Radio 0 = una sola muestra (sombra
            // "dura", la más barata); radio creciente = penumbra más suave y bordes
            // graduales — el mismo efecto que activar "Soft Shadows" en Unity. El
            // radio lo decide uShadowPcfRadius según el nivel de Shadow Quality.
            float PcfShadow(sampler2D shadowMap, vec2 uv, float currentDepth, float bias, int radius)
            {
                vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0));
                float shadow = 0.0;
                float taps = 0.0;
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                    {
                        float closest = texture(shadowMap, uv + vec2(x, y) * texel).r;
                        shadow += currentDepth - bias > closest ? 1.0 : 0.0;
                        taps += 1.0;
                    }
                return shadow / max(taps, 1.0);
            }

            float PcfShadowArray(sampler2DArray shadowMap, vec2 uv, int layer, float currentDepth, float bias, int radius)
            {
                vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0).xy);
                float shadow = 0.0;
                float taps = 0.0;
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                    {
                        float closest = texture(shadowMap, vec3(uv + vec2(x, y) * texel, float(layer))).r;
                        shadow += currentDepth - bias > closest ? 1.0 : 0.0;
                        taps += 1.0;
                    }
                return shadow / max(taps, 1.0);
            }

            // PCSS (Percentage-Closer Soft Shadows) para la sombra DIRECCIONAL: proyección
            // ortográfica → profundidad LINEAL, el caso ideal. La penumbra crece con la distancia
            // entre el oclusor y el receptor → contacto nítido y sombra lejana difusa, como en
            // Unreal. lightSize controla el "tamaño" de la luz (cuánta penumbra máxima).
            float PcssShadowArray(sampler2DArray shadowMap, vec2 uv, int layer, float currentDepth, float bias, float lightSize)
            {
                vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0).xy);

                // 1) Búsqueda de bloqueadores: media de profundidad de los oclusores cercanos.
                float blockerSum = 0.0;
                float blockerCount = 0.0;
                for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                    {
                        float d = texture(shadowMap, vec3(uv + vec2(x, y) * texel * lightSize, float(layer))).r;
                        if (d < currentDepth - bias)
                        {
                            blockerSum += d;
                            blockerCount += 1.0;
                        }
                    }
                if (blockerCount < 0.5)
                    return 0.0;   // sin oclusores → totalmente iluminado

                float avgBlocker = blockerSum / blockerCount;

                // 2) Penumbra ∝ (receptor − oclusor) / oclusor, escalada por el tamaño de la luz.
                float penumbra = (currentDepth - avgBlocker) / max(avgBlocker, 1e-4) * lightSize;
                float spread = clamp(penumbra, 1.0, lightSize * 2.5) / 2.0;

                // 3) PCF de kernel fijo (5×5) ESPARCIDO según la penumbra estimada.
                float shadow = 0.0;
                for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                    {
                        float d = texture(shadowMap, vec3(uv + vec2(x, y) * texel * spread, float(layer))).r;
                        shadow += currentDepth - bias > d ? 1.0 : 0.0;
                    }
                return shadow / 25.0;
            }

            // El shadow map solo cubre un rectángulo/cono finito de la escena. Justo
            // dentro de ese límite, el kernel PCF mezcla texels "en sombra" con
            // texels del borde fijo (ClampToBorder = blanco = "sin ocluir"), lo que
            // dibuja un anillo de semisombra perfectamente visible — un óvalo/lente
            // suave pegado al borde del frustum de sombra, justo lo que se ve en
            // las paredes al subir la calidad (con 1 sola muestra el anillo es tan
            // fino que pasa desapercibido; con PCF se vuelve una franja ancha y
            // nítida). Igual que el "Shadow Distance"/fade de Unity, atenuamos la
            // sombra suavemente a cero cerca de los bordes del shadow map para que
            // la transición sea imperceptible en lugar de un corte/anillo visible.
            float ShadowEdgeFade(vec2 uv)
            {
                const float edge = 0.06;
                float fx = smoothstep(0.0, edge, uv.x) * smoothstep(1.0, 1.0 - edge, uv.x);
                float fy = smoothstep(0.0, edge, uv.y) * smoothstep(1.0, 1.0 - edge, uv.y);
                return fx * fy;
            }

            float ShadowFactor(vec3 N, vec3 lightDir)
            {
                if (uShadowEnabled == 0) return 1.0;
                float viewDistance = length(vWorldPos - uShadowCameraPos);
                int cascade = max(uCascadeCount - 1, 0);
                for (int i = 0; i < 5; i++)
                {
                    if (i >= uCascadeCount) break;
                    if (viewDistance <= uCascadeSplit[i])
                    {
                        cascade = i;
                        break;
                    }
                }

                vec4 lightPos = vec4(vWorldPos, 1.0) * uCascadeLightMvp[cascade];
                vec3 proj = lightPos.xyz / max(lightPos.w, 0.0001);
                proj = proj * 0.5 + 0.5;
                if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
                    return 1.0;

                // Sesgo dependiente de la pendiente (slope-scaled bias): cuanto más
                // de canto mira la superficie respecto a la luz, mayor es el error
                // de cuantización del shadow map y más sesgo hace falta para evitar
                // "shadow acne" (auto-sombreado ruidoso). El suelo/techo aumentado
                // respecto al valor original — con PCF de varias muestras (Medium+)
                // ese ruido deja de ser un parpadeo aleatorio y se vuelve un patrón
                // de anillos/Moiré muy visible si el sesgo es demasiado pequeño.
                float bias = max(0.0035 * (1.0 - dot(N, normalize(-lightDir))), 0.0012) * uShadowBiasScale;
                // Penumbra suave (PCSS) en Medium+; en Low (radius 0) sombra dura de 1 muestra.
                float shadow;
                if (uShadowPcfRadius > 0)
                    shadow = PcssShadowArray(uShadowMap, proj.xy, cascade, proj.z, bias, float(uShadowPcfRadius) * 2.5 + 2.0);
                else
                    shadow = (proj.z - bias > texture(uShadowMap, vec3(proj.xy, float(cascade))).r) ? 1.0 : 0.0;
                shadow *= ShadowEdgeFade(proj.xy);
                float visualStrength = clamp(uShadowStrength + clamp(uSkyStrength, 0.0, 1.0) * 0.18, 0.0, 0.95);
                return mix(1.0, 1.0 - shadow, visualStrength);
            }

            float SpotShadowFactor(vec3 N, vec3 lightDir)
            {
                if (uSpotShadowEnabled == 0) return 1.0;
                vec4 lp = vec4(vWorldPos, 1.0) * uSpotLightMvp;
                vec3 proj = lp.xyz / max(lp.w, 0.0001);
                proj = proj * 0.5 + 0.5;
                if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
                    return 1.0;
                float bias = max(0.004 * (1.0 - dot(N, normalize(lightDir))), 0.0014) * uShadowBiasScale;
                float shadow = PcfShadow(uSpotShadowMap, proj.xy, proj.z, bias, uShadowPcfRadius);
                shadow *= ShadowEdgeFade(proj.xy);
                return mix(1.0, 1.0 - shadow, uSpotShadowStrength);
            }

            float PointShadowFactor(vec3 N, vec3 worldPos)
            {
                if (uPointShadowEnabled == 0) return 1.0;
                vec3 toFrag = worldPos - uPointShadowPos;
                float current = length(toFrag);
                vec3  dir    = normalize(toFrag);
                // Igual que en direccional/spot: sesgo escalado por la pendiente en
                // vez de un valor fijo. El antiguo 0.035 servía para una sola muestra
                // dura, pero al promediar 9-25 muestras (PCF, calidad Medium+) el
                // ruido de auto-sombreado deja de cancelarse por aleatoriedad y
                // aparece como anillos concéntricos sobre las paredes/suelo.
                float bias = clamp(0.05 * (1.0 - dot(N, -dir)), 0.02, 0.12) * uShadowBiasScale;

                if (uShadowPcfRadius <= 0)
                {
                    // Calidad "Low": una sola muestra — sombra dura y barata, igual
                    // que desactivar "Soft Shadows" en Unity.
                    float closest = texture(uPointShadowCube, dir).r * uPointShadowFar;
                    float hard = current - bias > closest ? 1.0 : 0.0;
                    return mix(1.0, 1.0 - hard, uPointShadowStrength);
                }

                // Las cubemaps no tienen un plano de texels único, así que en vez del
                // kernel cuadrado del PCF 2D muestreamos un disco perpendicular a la
                // dirección luz→fragmento (3x3 offsets), creciendo el radio del disco
                // con uShadowPcfRadius — el mismo resultado visual: bordes suaves y
                // graduales en vez de un corte duro "de aliasing".
                vec3 up    = abs(dir.y) > 0.95 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
                vec3 right = normalize(cross(up, dir));
                up = cross(dir, right);
                float diskRadius = (0.0025 + 0.0025 * float(uShadowPcfRadius)) * uPointShadowFar;

                float shadow = 0.0;
                float taps = 0.0;
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                    {
                        vec3 sampleDir = dir + (right * float(x) + up * float(y)) * diskRadius;
                        float closest = texture(uPointShadowCube, sampleDir).r * uPointShadowFar;
                        shadow += current - bias > closest ? 1.0 : 0.0;
                        taps += 1.0;
                    }
                shadow /= max(taps, 1.0);
                return mix(1.0, 1.0 - shadow, uPointShadowStrength);
            }

            vec3 ApplyNormalMap(vec3 normal)
            {
                if (!uHasNormalMap) return normal;
                vec3 map = normalize(texture(uNormalMap, vUv).xyz * 2.0 - 1.0);
                vec3 n = normalize(normal);

                vec3 dp1 = dFdx(vWorldPos);
                vec3 dp2 = dFdy(vWorldPos);
                vec2 duv1 = dFdx(vUv);
                vec2 duv2 = dFdy(vUv);
                float det = duv1.x * duv2.y - duv1.y * duv2.x;

                vec3 tangent;
                float handedness = 1.0;
                if (abs(det) > 1e-8)
                {
                    tangent = (dp1 * duv2.y - dp2 * duv1.y) / det;
                    handedness = det < 0.0 ? -1.0 : 1.0;
                }
                else
                {
                    tangent = abs(n.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : cross(vec3(0.0, 1.0, 0.0), n);
                }

                tangent = tangent - n * dot(n, tangent);
                if (dot(tangent, tangent) < 1e-8)
                    tangent = abs(n.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : cross(vec3(0.0, 1.0, 0.0), n);
                tangent = normalize(tangent);

                vec3 bitangent = normalize(cross(n, tangent)) * handedness;
                return normalize(tangent * map.x + bitangent * map.y + n * map.z);
            }

            // ---- Procedural IBL ----
            // Analytic environment (no precomputed textures): a sky/horizon/ground
            // gradient derived from the same ambient colors. Returns the
            // environment radiance in a given direction.
            vec3 SampleEnv(vec3 dir)
            {
                float up = clamp(dir.y, -1.0, 1.0);
                const float SKY_DIFFUSE_FILL = 0.48;
                vec3 skyCol     = uAmbientColor * (uAmbientIntensity + uSkyStrength * SKY_DIFFUSE_FILL);
                vec3 horizonCol = uAmbientColor *  uAmbientIntensity;
                vec3 groundCol  = uAmbientColor *  uAmbientIntensity * 0.35;
                return up >= 0.0 ? mix(horizonCol, skyCol, up)
                                 : mix(horizonCol, groundCol, -up);
            }

            // Maps a world-space direction to equirectangular UV for the HDRI
            // lookup. u from atan(z,x) (longitude), v from acos(y) (latitude).
            vec2 DirToEquirectUV(vec3 d)
            {
                const float PI = 3.14159265359;
                float u = atan(d.z, d.x) / (2.0 * PI) + 0.5;
                float v = acos(clamp(d.y, -1.0, 1.0)) / PI;
                return vec2(u, v);
            }

            // Analytic "environment BRDF" approximation (Karis split-sum, mobile
            // version): replaces the BRDF LUT texture with a few ALU ops. Returns
            // the specular color scaled/biased for (roughness, NoV).
            vec3 EnvBRDFApprox(vec3 specColor, float roughness, float NoV)
            {
                const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
                const vec4 c1 = vec4( 1.0,  0.0425,  1.040, -0.040);
                vec4 r = roughness * c0 + c1;
                float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
                vec2 AB = vec2(-1.04, 1.04) * a004 + r.zw;
                return specColor * AB.x + AB.y;
            }

            void main()
            {
                vec3  N    = ApplyNormalMap(normalize(vNormal));
                vec3  V    = normalize(uCameraPos - vWorldPos);
                vec4  tex  = uHasTexture ? texture(uTexture, vUv) : vec4(1.0);
                vec3  rawColor = max(vColor.rgb * tex.rgb, vec3(0.0));
                // Espacio "Linear" (PRO/PBR): las texturas y colores de vértice
                // llegan codificados en sRGB, así que se decodifican a lineal
                // antes de iluminar -- exactamente el comportamiento de Unity con
                // "Color Space: Linear". Espacio "Gamma": se usa el valor tal
                // cual, sin decodificar, como en el flujo clásico/legacy.
                vec3  base = uColorSpaceLinear == 1 ? pow(rawColor, vec3(2.2)) : rawColor;
                float metallic = clamp(uHasMetallicMap ? texture(uMetallicMap, vUv).r : vMaterial.x, 0.0, 1.0);
                float roughness = clamp(uHasRoughnessMap ? texture(uRoughnessMap, vUv).r : vMaterial.y, 0.04, 1.0);

                const float SKY_DIFFUSE_FILL = 0.48;
                vec3 sky = uAmbientColor * (uAmbientIntensity + max(N.y, 0.0) * uSkyStrength * SKY_DIFFUSE_FILL);
                vec3 ground = uAmbientColor * uAmbientIntensity * 0.35 * max(-N.y, 0.0);
                vec3 ambientLight = sky + ground;
                float viewDistance = length(uCameraPos - vWorldPos);

                // Direccional
                vec3 sunL = normalize(-uDirDir);
                float sunShadow = ShadowFactor(N, uDirDir);
                vec3 directLight = AddDirectLight(N, V, sunL, uDirColor, uDirIntensity * sunShadow, base, roughness, metallic);

                // Point lights
                for (int i = 0; i < MAX_POINT_LIGHTS; i++)
                {
                    if (i >= uPointCount) break;
                    vec3  toLight = uPointPos[i] - vWorldPos;
                    float dist    = length(toLight);
                    if (dist >= uPointRange[i]) continue;
                    float atten   = RangeAttenuation(dist, uPointRange[i]);
                    float pshadow = i == 0 ? PointShadowFactor(N, vWorldPos) : 1.0;
                    directLight  += AddDirectLight(N, V, normalize(toLight), uPointColor[i], uPointIntensity[i] * atten * pshadow, base, roughness, metallic);
                }

                // Spot lights
                for (int i = 0; i < MAX_SPOT_LIGHTS; i++)
                {
                    if (i >= uSpotCount) break;
                    vec3 toLight = uSpotPos[i] - vWorldPos;
                    float dist = length(toLight);
                    if (dist >= uSpotRange[i]) continue;
                    vec3 L = normalize(toLight);
                    float cone = dot(normalize(-uSpotDir[i]), L);
                    float outer = cos(uSpotAngle[i] * 0.5);
                    float inner = cos(uSpotAngle[i] * 0.38);
                    float spot = smoothstep(outer, inner, cone);
                    float atten = RangeAttenuation(dist, uSpotRange[i]) * spot;
                    float sshadow = i == 0 ? SpotShadowFactor(N, -uSpotDir[i]) : 1.0;
                    directLight += AddDirectLight(N, V, L, uSpotColor[i], uSpotIntensity[i] * atten * sshadow, base, roughness, metallic);
                }

                // Area lights, soft approximation.
                for (int i = 0; i < MAX_AREA_LIGHTS; i++)
                {
                    if (i >= uAreaCount) break;
                    vec3 toLight = uAreaPos[i] - vWorldPos;
                    float dist = length(toLight);
                    if (dist >= uAreaRange[i]) continue;
                    vec3 L = normalize(toLight);
                    float facing = clamp(dot(normalize(-uAreaDir[i]), L), 0.0, 1.0);
                    float sizeBoost = clamp((uAreaSize[i].x + uAreaSize[i].y) * 0.18, 0.25, 1.6);
                    float atten = RangeAttenuation(dist, uAreaRange[i]) * (0.35 + 0.65 * facing) * sizeBoost;
                    directLight += AddDirectLight(N, V, L, uAreaColor[i], uAreaIntensity[i] * atten, base, roughness, metallic);
                }

                float ao = 1.0 - uAoStrength * clamp(1.0 - N.y, 0.0, 1.0) * 0.45;
                // Ambient term. Default: classic albedo * hemispheric irradiance.
                // With IBL: energy-conserving diffuse (metals lose diffuse) PLUS
                // the environment specular -> the surface reflects the sky/ground
                // by roughness (Karis split-sum, no precomputed textures). This is
                // what stops metals from looking flat.
                vec3 ambientTerm;
                if (uUseIBL == 1)
                {
                    vec3  R       = reflect(-V, N);
                    float NoV     = max(dot(N, V), 1e-4);
                    vec3  F0      = mix(vec3(0.04), base, metallic);
                    vec3  envBRDF = EnvBRDFApprox(F0, roughness, NoV);

                    vec3 irradiance;
                    vec3 prefiltered;
                    if (uHasEnvMap == 1)
                    {
                        // Real HDRI: diffuse irradiance ~ a high mip sampled at N;
                        // specular ~ mip selected by roughness sampled at R (the
                        // mip chain approximates prefiltering).
                        irradiance  = textureLod(uEnvMap, DirToEquirectUV(N), max(uEnvMaxLod - 1.0, 0.0)).rgb * 0.58;
                        prefiltered = textureLod(uEnvMap, DirToEquirectUV(R), roughness * uEnvMaxLod).rgb * 0.82;
                    }
                    else
                    {
                        // Procedural environment (no HDRI loaded). Sharp when
                        // smooth, blended toward the average as roughness rises.
                        vec3 avgEnv = uAmbientColor * uAmbientIntensity * 0.75;
                        irradiance  = ambientLight;
                        prefiltered = mix(SampleEnv(R), avgEnv, roughness);
                    }
                    vec3 diffuseIBL  = base * irradiance * (1.0 - metallic);
                    vec3 specularIBL = prefiltered * envBRDF;
                    ambientTerm = diffuseIBL + specularIBL;
                }
                else
                {
                    ambientTerm = base * ambientLight;
                }
                ambientTerm *= mix(0.62, 1.0, sunShadow);
                vec3 hdr = (ambientTerm + directLight) * ao;
                hdr += vEmission.rgb * vEmission.a;

                if (uDebugView == 1) { outColor = vec4(pow(base, vec3(1.0 / 2.2)), vColor.a * tex.a); return; }
                if (uDebugView == 2) { outColor = vec4(N * 0.5 + 0.5, 1.0); return; }
                if (uDebugView == 3) { outColor = vec4(vec3(roughness), 1.0); return; }
                if (uDebugView == 4) { outColor = vec4(vec3(metallic), 1.0); return; }
                if (uDebugView == 5) { outColor = vec4(vec3(sunShadow), 1.0); return; }

                float fogAmount = 1.0 - exp(-viewDistance * max(uFogDensity, 0.0));
                float forwardFog = pow(max(dot(normalize(uCameraPos - vWorldPos), normalize(-uDirDir)), 0.0), 8.0);
                vec3 fog = uFogColor * (1.0 + forwardFog * uVolumetricStrength);
                hdr = mix(hdr, fog, clamp(fogAmount, 0.0, 0.92));

                // Salida en HDR LINEAL (sin tonemap ni gamma encode aquí).
                // El display transform completo (exposición -> bloom -> AO ->
                // tonemap ACES -> codificación sRGB) se hace UNA SOLA VEZ al
                // final, en el paso de post-proceso (SceneRenderTarget). Hacerlo
                // aquí además rompía el bloom (recortaba el HDR a [0,1] antes de
                // extraer las altas luces) y aplicaba doble corrección de gamma
                // (lavaba la imagen). Mantenerlo en HDR aquí también permite que
                // el MSAA se resuelva en lineal y que las texturas de color del
                // framebuffer (Rgba16f) conserven el rango dinámico para el bloom.
                outColor = vec4(max(hdr, vec3(0.0)), vColor.a * tex.a);
            }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }

    // Shader simplificado para Terrain (Fase 3): mezcla hasta 4 texturas de
    // capa según un splat map RGBA, con iluminación ambiente (cielo/suelo) +
    // direccional difusa únicamente (sin PBR completo / sombras), siguiendo
    // las mismas fórmulas que solidShader para que el look sea consistente.
    


    // (Re)carga el HDRI equirectangular a una textura RGB16F con mips. Se llama
    // solo cuando cambia la ruta (_envDirty), no por frame. Si no hay ruta o falla,
    // el IBL cae al entorno procedural (uHasEnvMap = 0).
    


    // Dibuja el HDRI como fondo (skybox). Triángulo fullscreen; por cada píxel
    // reconstruye el rayo de vista desde la base de la cámara (sin matrices
    // inversas) y muestrea el equirectangular. Se dibuja ANTES de la geometría,
    // sin escribir profundidad, así todo lo demás queda por delante.
    


    


    


    


    


    


    


    


    


    


    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        source = StripShaderLineComments(source);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"{type} shader compile failed: {log}");
        }
        return shader;
    }

    private static string StripShaderLineComments(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                lines[i] = lines[i][..comment];
        }

        return string.Join('\n', lines);
    }

    


    



    // ── Occlusion Culling helpers ──────────────────────────────────

    /// <summary>
    /// GPU pass: harvest ready query results from last frame(s), then issue new
    /// bounding-box queries for objects that are due for a re-test.
    /// Must be called AFTER opaque geometry has been drawn so the depth buffer is populated.
    /// </summary>
}
