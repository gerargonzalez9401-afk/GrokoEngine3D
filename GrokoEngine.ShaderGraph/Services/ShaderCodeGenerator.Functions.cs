using System;
using System.Linq;
using System.Text;
using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

public sealed partial class ShaderCodeGenerator
{
    private static string ShadowReceiveFunctions() => """
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
        float ShadowEdgeFade(vec2 uv)
        {
            const float edge = 0.06;
            float fx = smoothstep(0.0, edge, uv.x) * smoothstep(1.0, 1.0 - edge, uv.x);
            float fy = smoothstep(0.0, edge, uv.y) * smoothstep(1.0, 1.0 - edge, uv.y);
            return fx * fy;
        }
        float ReceiveShadow(vec3 N, vec3 lightDir)
        {
            if (u_ShadowEnabled == 0) return 1.0;
            float viewDistance = length(v_WorldPos - u_ShadowCameraPos);
            int cascade = max(u_CascadeCount - 1, 0);
            for (int i = 0; i < 5; i++)
            {
                if (i >= u_CascadeCount) break;
                if (viewDistance <= u_CascadeSplit[i]) { cascade = i; break; }
            }
            vec4 lightPos = vec4(v_WorldPos, 1.0) * u_CascadeLightMvp[cascade];
            vec3 proj = lightPos.xyz / max(lightPos.w, 0.0001);
            proj = proj * 0.5 + 0.5;
            if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0) return 1.0;
            float bias = max(0.0035 * (1.0 - dot(N, normalize(-lightDir))), 0.0012) * u_ShadowBiasScale;
            float shadow = PcfShadowArray(u_ShadowMap, proj.xy, cascade, proj.z, bias, u_ShadowPcfRadius);
            shadow *= ShadowEdgeFade(proj.xy);
            float visualStrength = clamp(u_ShadowStrength, 0.0, 0.95);
            return mix(1.0, 1.0 - shadow, visualStrength);
        }
        """;

    private void AppendFeatureFunctions(StringBuilder sb)
    {
        sb.AppendLine(PbrFunctions());
        sb.AppendLine(ShadowReceiveFunctions());

        if (_features.Contains("normalmap") || _graph.Nodes.Any(n => n.Kind == NodeKind.NormalMap))
            sb.AppendLine(NormalMapFunctions());
        if (_features.Contains("noise") || _features.Contains("voronoi") || _features.Contains("gradientnoise"))
            sb.AppendLine(CommonHashFunctions());

        if (_features.Contains("noise"))
            sb.AppendLine(NoiseFunction());

        if (_features.Contains("gradientnoise"))
            sb.AppendLine(GradientNoiseFunction());

        if (_features.Contains("checkerboard"))
            sb.AppendLine(CheckerboardFunction());

        if (_features.Contains("voronoi"))
            sb.AppendLine(VoronoiFunction());

        if (_features.Contains("triplanar"))
            sb.AppendLine(TriplanarFunction());

        if (_features.Contains("scenedepth"))
            sb.AppendLine(SceneDepthFunctions());

        if (_graph.Nodes.Any(n => n.Kind == NodeKind.Flipbook))
            sb.AppendLine(FlipbookFunction());

        if (_graph.Nodes.Any(n => n.Kind == NodeKind.Rotator))
            sb.AppendLine(RotateFunction());

        if (_graph.Nodes.Any(n => n.Kind == NodeKind.Twirl))
            sb.AppendLine(TwirlFunction());

        if (_graph.Nodes.Any(n => n.Kind == NodeKind.PolarCoordinates))
            sb.AppendLine(PolarFunction());

        if (HasOverlayBlend())
            sb.AppendLine("vec3 blendOverlay(vec3 a, vec3 b) { return mix(2.0 * a * b, vec3(1.0) - 2.0 * (vec3(1.0) - a) * (vec3(1.0) - b), step(vec3(0.5), a)); }\n");

        if (_features.Contains("dissolve"))
        {
            sb.AppendLine("float dissolveAlpha(float mask, float amount, float edgeWidth) { float ew = max(edgeWidth, 0.0001); return smoothstep(amount, amount + ew, mask); }");
            sb.AppendLine("float dissolveEdge(float mask, float amount, float edgeWidth) { float ew = max(edgeWidth, 0.0001); return (1.0 - smoothstep(amount + ew, amount + ew * 2.0, mask)) * smoothstep(amount, amount + ew, mask); }");
            sb.AppendLine();
        }
    }

    private bool HasOverlayBlend()
        => _graph.Nodes.Any(n => n.Kind == NodeKind.Blend && (n.TextValue ?? "Add").Trim().Equals("Overlay", StringComparison.OrdinalIgnoreCase));

    private static string PbrFunctions() => """
vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(1.0 - clamp(cosTheta, 0.0, 1.0), 5.0);
}

float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    return a2 / max(3.14159265 * denom * denom, 0.0001);
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / max(NdotV * (1.0 - k) + k, 0.0001);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float ggx2 = GeometrySchlickGGX(max(dot(N, V), 0.0), roughness);
    float ggx1 = GeometrySchlickGGX(max(dot(N, L), 0.0), roughness);
    return ggx1 * ggx2;
}

""";

    private static string NormalMapFunctions() => """
vec3 applyNormalMap(vec3 normalWS, vec3 tangentWS, vec3 sampleRGB, float strength)
{
    vec3 n = normalize(normalWS);
    vec3 tsn = normalize(sampleRGB * 2.0 - 1.0);
    tsn.xy *= strength;

    vec3 dp1 = dFdx(v_WorldPos);
    vec3 dp2 = dFdy(v_WorldPos);
    vec2 duv1 = dFdx(v_UV);
    vec2 duv2 = dFdy(v_UV);
    float det = duv1.x * duv2.y - duv1.y * duv2.x;

    vec3 t;
    float handedness = 1.0;
    if (abs(det) > 1e-8)
    {
        t = (dp1 * duv2.y - dp2 * duv1.y) / det;
        handedness = det < 0.0 ? -1.0 : 1.0;
    }
    else
    {
        t = tangentWS;
    }

    t = t - n * dot(n, t);
    if (dot(t, t) < 1e-8)
        t = abs(n.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : cross(vec3(0.0, 1.0, 0.0), n);
    t = normalize(t);

    vec3 b = normalize(cross(n, t)) * handedness;
    return normalize(t * tsn.x + b * tsn.y + n * tsn.z);
}

""";

    private static string CommonHashFunctions() => """
float hash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

vec2 hash2(vec2 p)
{
    return fract(sin(vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)))) * 43758.5453);
}

""";

    private static string NoiseFunction() => """
float noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

""";

    private static string VoronoiFunction() => """
float voronoi(vec2 p, float angleOffset)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float minDist = 8.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            vec2 neighbor = vec2(float(x), float(y));
            vec2 point = hash2(i + neighbor);
            point = 0.5 + 0.5 * sin(angleOffset + 6.2831 * point);
            vec2 diff = neighbor + point - f;
            minDist = min(minDist, dot(diff, diff));
        }
    }
    return sqrt(minDist);
}

float voronoiCells(vec2 p, float angleOffset)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float minDist = 8.0;
    vec2 minCell = vec2(0.0);
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            vec2 neighbor = vec2(float(x), float(y));
            vec2 point = hash2(i + neighbor);
            point = 0.5 + 0.5 * sin(angleOffset + 6.2831 * point);
            vec2 diff = neighbor + point - f;
            float dist = dot(diff, diff);
            if (dist < minDist) { minDist = dist; minCell = i + neighbor; }
        }
    }
    return hash(minCell);
}

""";

    private static string GradientNoiseFunction() => """
float gradientNoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    vec2 ga = hash2(i) * 2.0 - 1.0;
    vec2 gb = hash2(i + vec2(1.0, 0.0)) * 2.0 - 1.0;
    vec2 gc = hash2(i + vec2(0.0, 1.0)) * 2.0 - 1.0;
    vec2 gd = hash2(i + vec2(1.0, 1.0)) * 2.0 - 1.0;
    float va = dot(ga, f - vec2(0.0, 0.0));
    float vb = dot(gb, f - vec2(1.0, 0.0));
    float vc = dot(gc, f - vec2(0.0, 1.0));
    float vd = dot(gd, f - vec2(1.0, 1.0));
    return 0.5 + mix(mix(va, vb, u.x), mix(vc, vd, u.x), u.y);
}

""";

    private static string CheckerboardFunction() => """
float checkerboard(vec2 uv, vec2 frequency)
{
    vec2 c = floor(uv * frequency);
    return mod(c.x + c.y, 2.0);
}

""";

    private static string TriplanarFunction() => """
vec3 triplanar(sampler2D tex, vec3 worldPos, vec3 normalWS, float scale, float blendSharpness)
{
    vec3 blend = pow(abs(normalWS), vec3(blendSharpness));
    blend /= max(blend.x + blend.y + blend.z, 0.0001);
    vec3 x = texture(tex, worldPos.yz * scale).rgb;
    vec3 y = texture(tex, worldPos.xz * scale).rgb;
    vec3 z = texture(tex, worldPos.xy * scale).rgb;
    return x * blend.x + y * blend.y + z * blend.z;
}

""";

    private static string SceneDepthFunctions() => """
float sceneDepthLinearEye(float raw)
{
    float ndcZ = raw * 2.0 - 1.0;
    return (2.0 * u_CameraNear * u_CameraFar) / (u_CameraFar + u_CameraNear - ndcZ * (u_CameraFar - u_CameraNear));
}

float sceneDepthLinear01(float raw)
{
    return (sceneDepthLinearEye(raw) - u_CameraNear) / (u_CameraFar - u_CameraNear);
}

float depthFade(vec2 screenUV, float fadeDistance, float fragDepth)
{
    float sceneEye = sceneDepthLinearEye(texture(u_SceneDepth, screenUV).r);
    float surfaceEye = sceneDepthLinearEye(fragDepth);
    return clamp((sceneEye - surfaceEye) / max(fadeDistance, 0.0001), 0.0, 1.0);
}

""";

    private static string FlipbookFunction() => """
vec2 flipbookUV(vec2 uv, float columns, float rows, float frame)
{
    float total = max(columns * rows, 1.0);
    float f = mod(floor(frame), total);
    float x = mod(f, columns);
    float y = floor(f / columns);
    return (uv + vec2(x, rows - 1.0 - y)) / vec2(columns, rows);
}

""";

    private static string RotateFunction() => """
vec2 rotateUV(vec2 uv, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    uv -= vec2(0.5);
    mat2 r = mat2(c, -s, s, c);
    uv = r * uv;
    return uv + vec2(0.5);
}

""";

    private static string TwirlFunction() => """
vec2 twirlUV(vec2 uv, vec2 center, float strength)
{
    vec2 delta = uv - center;
    float dist = length(delta);
    float angle = strength * (1.0 - smoothstep(0.0, 1.0, dist));
    float s = sin(angle);
    float c = cos(angle);
    return center + mat2(c, -s, s, c) * delta;
}

""";

    private static string PolarFunction() => """
vec2 polarUV(vec2 uv, vec2 center, float radialScale, float angularScale)
{
    vec2 d = uv - center;
    float r = length(d) * radialScale;
    float a = atan(d.y, d.x) / 6.2831853 + 0.5;
    return vec2(r, fract(a * angularScale));
}

""";
}
