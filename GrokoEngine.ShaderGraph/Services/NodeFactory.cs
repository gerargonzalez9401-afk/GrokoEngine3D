using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

/// <summary>Creates fully wired <see cref="ShaderNode"/> instances by <see cref="NodeKind"/>.</summary>
public static class NodeFactory
{
    // ── Category lookup ──────────────────────────────────────────

    private static readonly HashSet<NodeKind> ConstantKinds =
        [NodeKind.ConstantFloat, NodeKind.ConstantColor, NodeKind.ConstantVector2, NodeKind.ConstantVector3];

    private static readonly HashSet<NodeKind> InputKinds =
        [NodeKind.Time, NodeKind.TextureCoord, NodeKind.NormalVector, NodeKind.TangentVector, NodeKind.ViewDirection,
         NodeKind.CameraVector, NodeKind.WorldPosition, NodeKind.ObjectPosition, NodeKind.ScreenPosition];

    private static readonly HashSet<NodeKind> PropertyKinds =
        [NodeKind.PropertyFloat, NodeKind.PropertyColor, NodeKind.PropertyVector2, NodeKind.PropertyVector3, NodeKind.PropertyVector4, NodeKind.PropertyTexture2D];

    private static readonly HashSet<NodeKind> MathKinds =
        [NodeKind.Add, NodeKind.Subtract, NodeKind.Multiply, NodeKind.Divide, NodeKind.Power,
         NodeKind.Sin, NodeKind.Cos, NodeKind.Clamp, NodeKind.Smoothstep, NodeKind.Remap,
         NodeKind.OneMinus, NodeKind.Posterize, NodeKind.Length, NodeKind.Saturate, NodeKind.Step,
         NodeKind.Abs, NodeKind.Floor, NodeKind.Ceil, NodeKind.Fraction, NodeKind.Min, NodeKind.Max, NodeKind.Lerp,
         NodeKind.Negate, NodeKind.Reciprocal, NodeKind.MultiplyAdd];

    private static readonly HashSet<NodeKind> VectorKinds =
        [NodeKind.Normalize, NodeKind.Dot, NodeKind.Cross];

    private static readonly HashSet<NodeKind> TextureKinds =
        [NodeKind.TextureSample, NodeKind.NormalMap, NodeKind.NormalBlend, NodeKind.NormalStrength, NodeKind.MetallicMap,
         NodeKind.SmoothnessMap, NodeKind.AmbientOcclusionMap, NodeKind.Flipbook, NodeKind.Triplanar];

    private static readonly HashSet<NodeKind> ColorKinds =
        [NodeKind.InvertColor, NodeKind.BrightnessContrast, NodeKind.ColorRamp,
         NodeKind.ChannelSplit, NodeKind.ChannelCombine, NodeKind.ChannelMask, NodeKind.Split, NodeKind.Combine,
         NodeKind.Swizzle, NodeKind.Append, NodeKind.Blend];

    private static readonly HashSet<NodeKind> ProceduralKinds =
        [NodeKind.Noise, NodeKind.GradientNoise, NodeKind.Checkerboard, NodeKind.Voronoi, NodeKind.SceneDepth, NodeKind.DepthFade, NodeKind.Fresnel, NodeKind.FresnelPro, NodeKind.RimLight, NodeKind.EmissionPulse, NodeKind.Dissolve, NodeKind.Gradient, NodeKind.Twirl,
         NodeKind.PolarCoordinates, NodeKind.RadialGradient, NodeKind.Distance, NodeKind.SphereMask,
         NodeKind.UVScroll, NodeKind.Panner, NodeKind.TilingOffset, NodeKind.Rotator, NodeKind.ParallaxOffset, NodeKind.Mix];

    // ── Public API ───────────────────────────────────────────────

    public static ShaderNode Create(NodeKind kind, double x, double y)
    {
        var node = new ShaderNode
        {
            Kind = kind,
            X = x,
            Y = y,
            Title = GetTitle(kind),
            FloatValue = 0f,
            FloatValue2 = 0f,
            FloatValue3 = 0f,
            ColorHex = "#FFFFFFFF",
            ColorHex2 = "#FF000000",
            ColorIntensity = 1f,
            ColorIntensity2 = 1f
        };

        WirePins(node);
        return node;
    }

    public static string GetTitle(NodeKind kind) => kind switch
    {
        NodeKind.ConstantFloat       => "Float",
        NodeKind.ConstantColor       => "Color",
        NodeKind.ConstantVector2     => "Vector2",
        NodeKind.ConstantVector3     => "Vector3",
        NodeKind.Time                => "Time",
        NodeKind.TextureCoord        => "Texture Coord",
        NodeKind.TangentVector       => "Tangent Vector",
        NodeKind.CameraVector        => "Camera Vector",
        NodeKind.ObjectPosition      => "Object Position",
        NodeKind.ScreenPosition      => "Screen Position",
        NodeKind.PropertyFloat       => "Property Float",
        NodeKind.PropertyColor       => "Property Color",
        NodeKind.PropertyVector2     => "Property Vector2",
        NodeKind.PropertyVector3     => "Property Vector3",
        NodeKind.PropertyVector4     => "Property Vector4",
        NodeKind.PropertyTexture2D   => "Property Texture2D",
        NodeKind.TextureSample       => "Sample Texture 2D",
        NodeKind.Flipbook            => "Flipbook",
        NodeKind.Triplanar           => "Triplanar",
        NodeKind.NormalVector        => "Normal Vector",
        NodeKind.ViewDirection       => "View Direction",
        NodeKind.WorldPosition       => "World Position",
        NodeKind.Add                 => "Add",
        NodeKind.Subtract            => "Subtract",
        NodeKind.Multiply            => "Multiply",
        NodeKind.Divide              => "Divide",
        NodeKind.Power               => "Power",
        NodeKind.Sin                 => "Sine",
        NodeKind.Cos                 => "Cosine",
        NodeKind.Clamp               => "Clamp",
        NodeKind.Smoothstep          => "Smoothstep",
        NodeKind.Remap               => "Remap",
        NodeKind.OneMinus            => "One Minus",
        NodeKind.Posterize           => "Posterize",
        NodeKind.Length              => "Length",
        NodeKind.Saturate            => "Saturate",
        NodeKind.Step                => "Step",
        NodeKind.Abs                 => "Abs",
        NodeKind.Floor               => "Floor",
        NodeKind.Ceil                => "Ceil",
        NodeKind.Fraction            => "Fraction",
        NodeKind.Min                 => "Minimum",
        NodeKind.Max                 => "Maximum",
        NodeKind.Lerp                => "Lerp",
        NodeKind.Negate              => "Negate",
        NodeKind.Reciprocal          => "Reciprocal",
        NodeKind.MultiplyAdd         => "Multiply Add",
        NodeKind.Normalize           => "Normalize",
        NodeKind.Dot                 => "Dot Product",
        NodeKind.Cross               => "Cross Product",
        NodeKind.Noise               => "Noise",
        NodeKind.GradientNoise       => "Gradient Noise",
        NodeKind.Checkerboard        => "Checkerboard",
        NodeKind.Voronoi             => "Voronoi",
        NodeKind.SceneDepth          => "Scene Depth",
        NodeKind.DepthFade           => "Depth Fade",
        NodeKind.Fresnel             => "Fresnel",
        NodeKind.FresnelPro          => "Fresnel Pro",
        NodeKind.RimLight            => "Rim Light",
        NodeKind.EmissionPulse       => "Emission Pulse",
        NodeKind.Dissolve            => "Dissolve",
        NodeKind.Gradient            => "Gradient",
        NodeKind.Twirl               => "Twirl",
        NodeKind.NormalMap           => "Normal Map",
        NodeKind.NormalBlend         => "Normal Blend",
        NodeKind.NormalStrength      => "Normal Strength",
        NodeKind.MetallicMap         => "Metallic Map",
        NodeKind.SmoothnessMap       => "Smoothness Map",
        NodeKind.AmbientOcclusionMap => "Ambient Occlusion Map",
        NodeKind.ChannelSplit        => "Channel Split",
        NodeKind.ChannelCombine      => "Channel Combine",
        NodeKind.ChannelMask         => "Channel Mask",
        NodeKind.Split               => "Split",
        NodeKind.Combine             => "Combine",
        NodeKind.Swizzle             => "Swizzle",
        NodeKind.Append              => "Append",
        NodeKind.InvertColor         => "Invert Color",
        NodeKind.BrightnessContrast  => "Brightness / Contrast",
        NodeKind.ColorRamp           => "Color Ramp",
        NodeKind.PolarCoordinates    => "Polar Coordinates",
        NodeKind.RadialGradient      => "Radial Gradient",
        NodeKind.Distance            => "Distance",
        NodeKind.SphereMask          => "Sphere Mask",
        NodeKind.Blend               => "Blend",
        NodeKind.UVScroll            => "UV Scroll",
        NodeKind.Panner              => "Panner",
        NodeKind.TilingOffset        => "Tiling And Offset",
        NodeKind.Rotator             => "Rotator",
        NodeKind.ParallaxOffset      => "Parallax Offset",
        NodeKind.Mix                 => "Mix / Lerp",
        NodeKind.SubGraph            => "Sub Graph",
        NodeKind.Output              => "Master Output",
        _                            => "Node"
    };

    public static string GetCategory(NodeKind kind)
    {
        if (ConstantKinds.Contains(kind))  return "Constants";
        if (InputKinds.Contains(kind))     return "Inputs";
        if (PropertyKinds.Contains(kind))  return "Blackboard";
        if (MathKinds.Contains(kind))      return "Math";
        if (VectorKinds.Contains(kind))    return "Vector";
        if (TextureKinds.Contains(kind))   return "Textures";
        if (ColorKinds.Contains(kind))     return "Color";
        if (ProceduralKinds.Contains(kind)) return "Procedural";
        if (kind == NodeKind.SubGraph)     return "SubGraphs";
        if (kind == NodeKind.Output)       return "Final";
        return "Other";
    }

    // ── Pin wiring ───────────────────────────────────────────────

    private static void WirePins(ShaderNode node)
    {
        switch (node.Kind)
        {
            case NodeKind.ConstantFloat:
                Out(node, "Value", PinType.Float);
                node.FloatValue = 0f;
                break;

            case NodeKind.ConstantColor:
                Out(node, "Color", PinType.Vec3);
                node.ColorHex = "#FFFFFFFF";
                break;

            case NodeKind.ConstantVector2:
                Out(node, "Vector", PinType.Vec2);
                node.TextValue = "0.0, 0.0";
                break;

            case NodeKind.ConstantVector3:
                Out(node, "Vector", PinType.Vec3);
                node.TextValue = "0.0, 0.0, 0.0";
                break;

            case NodeKind.Time:
                Out(node, "Time", PinType.Float);
                Out(node, "Sine Time", PinType.Float);
                Out(node, "Cosine Time", PinType.Float);
                Out(node, "Delta Time", PinType.Float);
                Out(node, "Smooth Delta", PinType.Float);
                break;

            case NodeKind.TextureCoord:
                Out(node, "UV", PinType.Vec2);
                break;

            case NodeKind.TangentVector:
                Out(node, "Tangent", PinType.Vec3);
                node.Comment = "Vector tangente de superficie. Útil para normal map avanzado y efectos anisotrópicos.";
                break;

            case NodeKind.CameraVector:
                Out(node, "Camera", PinType.Vec3);
                node.Comment = "Posición/dirección de cámara en world space según salida usada.";
                break;

            case NodeKind.ObjectPosition:
                Out(node, "Position", PinType.Vec3);
                node.Comment = "Posición local/object space del vértice. En preview usa v_WorldPos como fallback seguro.";
                break;

            case NodeKind.ScreenPosition:
                Out(node, "Out", PinType.Vec4);
                node.TextValue = "Raw";
                node.Comment = "Coordenada de pantalla normalizada para efectos screen-space básicos.";
                break;

            case NodeKind.PropertyFloat:
                Out(node, "Value", PinType.Float);
                node.TextValue = "FireSpeed";
                node.Comment = "Blackboard property. Se exporta como uniform float.";
                break;

            case NodeKind.PropertyColor:
                Out(node, "Color", PinType.Vec3);
                node.TextValue = "BaseColor";
                node.ColorHex = "#FFFFFFFF";
                node.Comment = "Blackboard property. Se exporta como uniform vec3.";
                break;

            case NodeKind.PropertyVector2:
                Out(node, "Vector", PinType.Vec2);
                node.TextValue = "Tiling";
                node.Comment = "Blackboard property. Default en FloatValue/FloatValue2.";
                break;

            case NodeKind.PropertyVector3:
                Out(node, "Vector", PinType.Vec3);
                node.TextValue = "Direction";
                node.Comment = "Blackboard property. Default en FloatValue/FloatValue2/FloatValue3.";
                break;

            case NodeKind.PropertyVector4:
                Out(node, "Vector", PinType.Vec4);
                node.TextValue = "Vector";
                node.Comment = "Blackboard property. Se exporta como uniform vec4.";
                break;

            case NodeKind.PropertyTexture2D:
                Out(node, "Texture", PinType.Texture2D);
                node.TextValue = "MainTex";
                node.Comment = "Blackboard texture property. Úsala como nombre de uniform para Sample Texture2D.";
                break;

            case NodeKind.TextureSample:
                In(node, "Texture", PinType.Texture2D, "u_MainTex");
                In(node, "UV",      PinType.Vec2,      "v_UV");
                In(node, "Tiling",  PinType.Vec2,      "vec2(1.0, 1.0)");
                In(node, "Offset",  PinType.Vec2,      "vec2(0.0, 0.0)");
                Out(node, "RGB",   PinType.Vec3);
                Out(node, "RGBA",  PinType.Vec4);
                Out(node, "R",     PinType.Float);
                Out(node, "G",     PinType.Float);
                Out(node, "B",     PinType.Float);
                Out(node, "A",     PinType.Float);
                node.TextValue = "u_MainTex";
                break;

            case NodeKind.Flipbook:
                In(node, "UV", PinType.Vec2, "v_UV");
                In(node, "Columns", PinType.Float, "4.0");
                In(node, "Rows", PinType.Float, "4.0");
                In(node, "Frame", PinType.Float, "0.0");
                Out(node, "UV", PinType.Vec2);
                break;

            case NodeKind.Triplanar:
                In(node, "Texture", PinType.Texture2D, "u_MainTex");
                In(node, "Scale",   PinType.Float, "1.0");
                In(node, "Blend",   PinType.Float, "4.0");
                Out(node, "RGB", PinType.Vec3);
                Out(node, "R",   PinType.Float);
                Out(node, "G",   PinType.Float);
                Out(node, "B",   PinType.Float);
                node.TextValue = "u_MainTex";
                node.Comment = "Proyecta la textura desde los 3 ejes usando World Position y Normal. Sin costuras en terrenos/rocas.";
                break;

            case NodeKind.NormalVector:
                Out(node, "Out", PinType.Vec3);
                break;

            case NodeKind.ViewDirection:
                Out(node, "Out", PinType.Vec3);
                break;

            case NodeKind.WorldPosition:
                Out(node, "Out", PinType.Vec3);
                break;

            case NodeKind.Add:
            case NodeKind.Subtract:
                In(node, "A", PinType.Float, "0.0");
                In(node, "B", PinType.Float, "0.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Multiply:
            case NodeKind.Divide:
                In(node, "A", PinType.Float, "1.0");
                In(node, "B", PinType.Float, "1.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Power:
                In(node, "A", PinType.Float, "1.0");
                In(node, "B", PinType.Float, "2.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Sin:
            case NodeKind.Cos:
            case NodeKind.OneMinus:
            case NodeKind.Saturate:
                In(node, "In", PinType.Float, "0.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Clamp:
                In(node, "In",  PinType.Float, "0.0");
                In(node, "Min", PinType.Float, "0.0");
                In(node, "Max", PinType.Float, "1.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Smoothstep:
                In(node, "Edge1", PinType.Float, "0.0");
                In(node, "Edge2", PinType.Float, "1.0");
                In(node, "In",    PinType.Float, "0.5");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Remap:
                In(node, "In",          PinType.Float, "0.5");
                In(node, "In Min Max",  PinType.Vec2,  "vec2(0.0, 1.0)");
                In(node, "Out Min Max", PinType.Vec2,  "vec2(0.0, 1.0)");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Posterize:
                In(node, "In",    PinType.Float, "0.5");
                In(node, "Steps", PinType.Float, "4.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Length:
                In(node, "In", PinType.Vec3, "vec3(0.0)");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Step:
                In(node, "Edge", PinType.Float, "0.5");
                In(node, "In",   PinType.Float, "0.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Abs:
            case NodeKind.Floor:
            case NodeKind.Ceil:
            case NodeKind.Fraction:
            case NodeKind.Negate:
            case NodeKind.Reciprocal:
                In(node, "In", PinType.Float, "0.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Min:
            case NodeKind.Max:
                In(node, "A", PinType.Float, "0.0");
                In(node, "B", PinType.Float, "1.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Lerp:
                In(node, "A", PinType.Vec3, "vec3(0.0)");
                In(node, "B", PinType.Vec3, "vec3(1.0)");
                In(node, "T", PinType.Float, "0.5");
                Out(node, "Out", PinType.Vec3);
                break;

            case NodeKind.MultiplyAdd:
                In(node, "A", PinType.Float, "1.0");
                In(node, "B", PinType.Float, "1.0");
                In(node, "C", PinType.Float, "0.0");
                Out(node, "Out", PinType.Float);
                node.Comment = "Result = A * B + C. Util para ajustar mascaras sin nodos extra.";
                break;

            case NodeKind.Normalize:
                In(node, "In", PinType.Vec3, "vec3(0.0, 0.0, 1.0)");
                Out(node, "Out", PinType.Vec3);
                break;

            case NodeKind.Dot:
                In(node, "A", PinType.Vec3, "vec3(0.0, 0.0, 1.0)");
                In(node, "B", PinType.Vec3, "vec3(0.0, 0.0, 1.0)");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.Cross:
                In(node, "A", PinType.Vec3, "vec3(1.0, 0.0, 0.0)");
                In(node, "B", PinType.Vec3, "vec3(0.0, 1.0, 0.0)");
                Out(node, "Out", PinType.Vec3);
                break;

            case NodeKind.Noise:
                In(node, "UV",    PinType.Vec2,  "v_UV");
                In(node, "Scale", PinType.Float, "8.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.GradientNoise:
                In(node, "UV",    PinType.Vec2,  "v_UV");
                In(node, "Scale", PinType.Float, "8.0");
                Out(node, "Out", PinType.Float);
                node.Comment = "Ruido tipo Perlin, más orgánico que Noise. Ideal para nubes, humo y terrenos.";
                break;

            case NodeKind.Checkerboard:
                In(node, "UV",        PinType.Vec2, "v_UV");
                In(node, "Frequency", PinType.Vec2, "vec2(10.0, 10.0)");
                Out(node, "Value", PinType.Float);
                node.Comment = "Patrón de tablero de ajedrez. Útil para grids de depuración y materiales tipo piso.";
                break;

            case NodeKind.Voronoi:
                In(node, "UV",          PinType.Vec2,  "v_UV");
                In(node, "Angle Offset", PinType.Float, "2.0");
                In(node, "Cell Density", PinType.Float, "5.0");
                Out(node, "Out", PinType.Float);
                Out(node, "Cells", PinType.Float);
                break;

            case NodeKind.SceneDepth:
                In(node, "UV", PinType.Vec4, "v_ScreenPos");
                Out(node, "Out", PinType.Float);
                node.TextValue = "Eye";
                break;

            case NodeKind.DepthFade:
                In(node, "Distance", PinType.Float, "1.0");
                Out(node, "Fade", PinType.Float);
                node.Comment = "Para superficies transparentes (agua, cristal). Da 0 donde la superficie toca un objeto detrás y 1 a 'Distance' unidades de profundidad. Conecta 'Fade' a Alpha o a un Mix con el color de agua para que lo que está debajo se vea degradado.";
                break;

            case NodeKind.Fresnel:
                In(node, "Normal", PinType.Vec3,  "normalize(v_NormalWS)");
                In(node, "Power",  PinType.Float, "3.0");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.FresnelPro:
                In(node, "Normal", PinType.Vec3, "normalize(v_NormalWS)");
                In(node, "View Dir", PinType.Vec3, "normalize(u_CameraPos - v_WorldPos)");
                In(node, "Power", PinType.Float, "4.0");
                In(node, "Bias", PinType.Float, "0.0");
                In(node, "Scale", PinType.Float, "1.0");
                Out(node, "Value", PinType.Float);
                node.Comment = "Fresnel profesional con bias/scale. Ideal para rim-light, vidrio, escudos y hologramas.";
                break;

            case NodeKind.RimLight:
                In(node, "Normal", PinType.Vec3, "normalize(v_NormalWS)");
                In(node, "Power", PinType.Float, "3.0");
                In(node, "Intensity", PinType.Float, "1.0");
                Out(node, "Color", PinType.Vec3);
                Out(node, "Mask", PinType.Float);
                node.ColorHex = "#FF38BDF8";
                node.ColorIntensity = 2.5f;
                node.Comment = "Rim light HDR listo para conectar a Emission.";
                break;

            case NodeKind.EmissionPulse:
                In(node, "Color", PinType.Vec3, "vec3(1.0, 0.35, 0.05)");
                In(node, "Intensity", PinType.Float, "1.0");
                Out(node, "Color", PinType.Vec3);
                Out(node, "Intensity", PinType.Float);
                break;

            case NodeKind.Dissolve:
                In(node, "Mask", PinType.Float, "0.5");
                In(node, "Amount", PinType.Float, "0.5");
                In(node, "Edge Width", PinType.Float, "0.08");
                Out(node, "Alpha", PinType.Float);
                Out(node, "Edge", PinType.Float);
                node.Comment = "Máscara de disolución. Alpha va al Master Alpha y Edge puede ir a emission.";
                break;

            case NodeKind.Gradient:
                In(node, "T", PinType.Float, "v_UV.y");
                Out(node, "Color", PinType.Vec3);
                node.ColorHex  = "#FF111827";
                node.ColorHex2 = "#FF38BDF8";
                node.TextValue = "Vertical";
                break;

            case NodeKind.Twirl:
                In(node, "UV",       PinType.Vec2,  "v_UV");
                In(node, "Strength", PinType.Float, "1.0");
                In(node, "Center",   PinType.Vec2,  "vec2(0.5, 0.5)");
                Out(node, "UV", PinType.Vec2);
                node.Comment = "Distorsiona las UV en forma de remolino. Úsalo suave o con Lerp para no romper la textura.";
                break;

            case NodeKind.NormalMap:
                In(node, "Texture",  PinType.Texture2D, "u_NormalMap");
                In(node, "UV",       PinType.Vec2,      "v_UV");
                In(node, "Strength", PinType.Float,     "1.0");
                Out(node, "Normal", PinType.Vec3);
                node.TextValue = "u_NormalMap";
                node.Comment = "Convierte una textura normal map RGB en normal para el Master Output.";
                break;

            case NodeKind.NormalBlend:
                In(node, "A", PinType.Vec3, "vec3(0.0, 0.0, 1.0)");
                In(node, "B", PinType.Vec3, "vec3(0.0, 0.0, 1.0)");
                In(node, "Strength", PinType.Float, "1.0");
                Out(node, "Normal", PinType.Vec3);
                node.Comment = "Mezcla dos normales tangent-space sin logica interna oculta.";
                break;

            case NodeKind.NormalStrength:
                In(node, "Normal", PinType.Vec3, "vec3(0.0, 0.0, 1.0)");
                In(node, "Strength", PinType.Float, "1.0");
                Out(node, "Normal", PinType.Vec3);
                break;

            case NodeKind.MetallicMap:
                In(node, "Texture", PinType.Texture2D, "u_MetallicMap");
                In(node, "UV", PinType.Vec2, "v_UV");
                In(node, "Strength", PinType.Float, "1.0");
                Out(node, "Metallic", PinType.Float);
                node.TextValue = "u_MetallicMap";
                break;

            case NodeKind.SmoothnessMap:
                In(node, "Texture", PinType.Texture2D, "u_SmoothnessMap");
                In(node, "UV", PinType.Vec2, "v_UV");
                In(node, "Strength", PinType.Float, "1.0");
                Out(node, "Smoothness", PinType.Float);
                node.TextValue = "u_SmoothnessMap";
                break;

            case NodeKind.AmbientOcclusionMap:
                In(node, "Texture", PinType.Texture2D, "u_AOMap");
                In(node, "UV", PinType.Vec2, "v_UV");
                In(node, "Strength", PinType.Float, "1.0");
                Out(node, "AO", PinType.Float);
                node.TextValue = "u_AOMap";
                break;

            case NodeKind.Split:
            case NodeKind.ChannelSplit:
                In(node, "In", PinType.Vec4, "vec4(0.0)");
                Out(node, "R", PinType.Float);
                Out(node, "G", PinType.Float);
                Out(node, "B", PinType.Float);
                Out(node, "A", PinType.Float);
                node.Comment = "Separa canales para crear máscaras de emisión, roughness, alpha, etc.";
                break;

            case NodeKind.Combine:
            case NodeKind.ChannelCombine:
                In(node, "R", PinType.Float, "0.0");
                In(node, "G", PinType.Float, "0.0");
                In(node, "B", PinType.Float, "0.0");
                In(node, "A", PinType.Float, "1.0");
                Out(node, "RGBA", PinType.Vec4);
                Out(node, "RGB",  PinType.Vec3);
                node.Comment = "Combina canales para construir colores o packed maps.";
                break;

            case NodeKind.Swizzle:
                In(node, "In", PinType.Vec4, "vec4(1.0)");
                Out(node, "Out", PinType.Vec4);
                Out(node, "RGB", PinType.Vec3);
                Out(node, "X", PinType.Float);
                node.TextValue = "rgba";
                node.Comment = "TextValue: rgba, rgb, xy, x, y, z, w. Reordena canales.";
                break;

            case NodeKind.Append:
                In(node, "A", PinType.Vec3, "vec3(0.0)");
                In(node, "B", PinType.Float, "1.0");
                Out(node, "Vec4", PinType.Vec4);
                Out(node, "Vec3", PinType.Vec3);
                node.Comment = "Une un vector + float. Útil para construir RGBA o coordenadas.";
                break;

            case NodeKind.ChannelMask:
                In(node, "Color", PinType.Vec3, "vec3(1.0)");
                Out(node, "Mask", PinType.Float);
                node.TextValue = "R";
                node.Comment = "TextValue: R, G, B, A, Luma. Útil para colorear solo las marcas blancas de una textura.";
                break;

            case NodeKind.InvertColor:
                In(node, "In", PinType.Vec3, "vec3(1.0)");
                Out(node, "Out", PinType.Vec3);
                break;

            case NodeKind.BrightnessContrast:
                In(node, "Color",      PinType.Vec3,  "vec3(1.0)");
                In(node, "Brightness", PinType.Float, "0.0");
                In(node, "Contrast",   PinType.Float, "1.0");
                Out(node, "Color", PinType.Vec3);
                break;

            case NodeKind.ColorRamp:
                In(node, "T",        PinType.Float, "0.5");
                In(node, "Contrast", PinType.Float, "1.0");
                Out(node, "Color", PinType.Vec3);
                node.ColorHex  = "#FF050816";
                node.ColorHex2 = "#FF38BDF8";
                node.Comment = "Como Gradient pero pensado para remapear máscaras Noise/Voronoi a colores pro.";
                break;

            case NodeKind.PolarCoordinates:
                In(node, "UV",             PinType.Vec2,  "v_UV");
                In(node, "Center",         PinType.Vec2,  "vec2(0.5, 0.5)");
                In(node, "Radial Scale",   PinType.Float, "1.0");
                In(node, "Angular Scale",  PinType.Float, "1.0");
                Out(node, "UV", PinType.Vec2);
                node.Comment = "Convierte UV a coordenadas polares para portales, ondas circulares y remolinos.";
                break;

            case NodeKind.RadialGradient:
                In(node, "UV",       PinType.Vec2,  "v_UV");
                In(node, "Center",   PinType.Vec2,  "vec2(0.5, 0.5)");
                In(node, "Radius",   PinType.Float, "0.5");
                In(node, "Softness", PinType.Float, "0.1");
                Out(node, "Mask", PinType.Float);
                break;

            case NodeKind.Distance:
                In(node, "A", PinType.Vec3, "vec3(0.0)");
                In(node, "B", PinType.Vec3, "vec3(1.0)");
                Out(node, "Out", PinType.Float);
                break;

            case NodeKind.SphereMask:
                In(node, "Position", PinType.Vec3,  "v_WorldPos");
                In(node, "Center",   PinType.Vec3,  "vec3(0.0)");
                In(node, "Radius",   PinType.Float, "1.0");
                In(node, "Softness", PinType.Float, "0.2");
                Out(node, "Mask", PinType.Float);
                node.Comment = "Máscara suave esférica para shields, impactos, zonas mágicas y disolvencias.";
                break;

            case NodeKind.Blend:
                In(node, "A",       PinType.Vec3,  "vec3(0.0)");
                In(node, "B",       PinType.Vec3,  "vec3(1.0)");
                In(node, "Opacity", PinType.Float, "1.0");
                Out(node, "Color", PinType.Vec3);
                node.TextValue = "Add";
                node.Comment = "TextValue: Add, Multiply, Screen, Overlay, Alpha. Mezcla colores como un editor pro.";
                break;

            case NodeKind.UVScroll:
            case NodeKind.Panner:
                In(node, "UV",    PinType.Vec2, "v_UV");
                In(node, "Speed", PinType.Vec2, "vec2(0.1, 0.0)");
                In(node, "Time",  PinType.Float, "0.0");
                Out(node, "UV", PinType.Vec2);
                break;

            case NodeKind.TilingOffset:
                In(node, "UV",     PinType.Vec2, "v_UV");
                In(node, "Tiling", PinType.Vec2, "vec2(1.0, 1.0)");
                In(node, "Offset", PinType.Vec2, "vec2(0.0, 0.0)");
                Out(node, "Out", PinType.Vec2);
                break;

            case NodeKind.Rotator:
                In(node, "UV",    PinType.Vec2,  "v_UV");
                In(node, "Angle", PinType.Float, "0.0");
                Out(node, "UV", PinType.Vec2);
                break;

            case NodeKind.ParallaxOffset:
                In(node, "UV", PinType.Vec2, "v_UV");
                In(node, "Height", PinType.Float, "0.5");
                In(node, "Scale", PinType.Float, "0.04");
                In(node, "View Dir", PinType.Vec3, "normalize(u_CameraPos - v_WorldPos)");
                Out(node, "UV", PinType.Vec2);
                node.Comment = "Offset parallax básico para materiales con height map.";
                break;

            case NodeKind.Mix:
                In(node, "A", PinType.Vec3,  "vec3(0.0)");
                In(node, "B", PinType.Vec3,  "vec3(1.0)");
                In(node, "T", PinType.Float, "0.5");
                Out(node, "Color", PinType.Vec3);
                break;

            case NodeKind.SubGraph:
                In(node, "Float In",  PinType.Float, "0.0");
                In(node, "Vec2 In",   PinType.Vec2,  "v_UV");
                In(node, "Vec3 In",   PinType.Vec3,  "vec3(0.0)");
                In(node, "Vec4 In",   PinType.Vec4,  "vec4(0.0)");
                Out(node, "Float", PinType.Float);
                Out(node, "Vec2",  PinType.Vec2);
                Out(node, "Vec3",  PinType.Vec3);
                Out(node, "Vec4",  PinType.Vec4);
                node.TextValue = "SubGraphName";
                break;

            case NodeKind.Output:
                In(node, "Base Color",       PinType.Vec3,  "vec3(1.0, 0.0, 1.0)");
                In(node, "Normal",           PinType.Vec3,  "normalize(v_NormalWS)");
                In(node, "Emission",         PinType.Vec3,  "vec3(0.0)");
                In(node, "Alpha",            PinType.Float, "1.0");
                In(node, "Alpha Clip",       PinType.Float, "0.0");
                In(node, "Metallic",         PinType.Float, "0.0");
                In(node, "Roughness",        PinType.Float, "0.5");
                In(node, "Smoothness",       PinType.Float, "0.5");
                In(node, "Ambient Occlusion",PinType.Float, "1.0");
                In(node, "Specular",         PinType.Float, "0.5");
                In(node, "Clear Coat",       PinType.Float, "0.0");
                In(node, "Clear Coat Roughness", PinType.Float, "0.1");
                break;
        }
    }

    // ── Pin helpers ──────────────────────────────────────────────

    private static void In(ShaderNode node, string name, PinType type, string defaultValue)
        => node.Inputs.Add(new GraphPin
        {
            NodeId = node.Id,
            Name = name,
            Direction = PinDirection.Input,
            Type = type,
            DefaultValue = defaultValue
        });

    private static void Out(ShaderNode node, string name, PinType type)
        => node.Outputs.Add(new GraphPin
        {
            NodeId = node.Id,
            Name = name,
            Direction = PinDirection.Output,
            Type = type
        });
}
