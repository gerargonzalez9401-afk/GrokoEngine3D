using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

public static class GraphTemplates
{
    public static ShaderGraphModel Empty()
    {
        var graph = new ShaderGraphModel { Name = "Untitled Shader", Version = "2.0" };
        graph.Nodes.Add(NodeFactory.Create(NodeKind.Output, 780, 260));
        return graph;
    }

    public static ShaderGraphModel Lava()
    {
        var graph = new ShaderGraphModel { Name = "Material_Lava_Pro", Surface = "Opaque", Version = "2.0" };

        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 140);
        var panner = NodeFactory.Create(NodeKind.Panner, 280, 130);
        var noise = NodeFactory.Create(NodeKind.Noise, 520, 120);
        var power = NodeFactory.Create(NodeKind.Power, 720, 110);
        var colorA = NodeFactory.Create(NodeKind.ConstantColor, 350, 340);
        colorA.ColorHex = "#FF2A0500";
        var colorB = NodeFactory.Create(NodeKind.ConstantColor, 350, 500);
        colorB.ColorHex = "#FFFFD54A";
        var mix = NodeFactory.Create(NodeKind.Mix, 930, 290);
        var output = NodeFactory.Create(NodeKind.Output, 1190, 330);

        graph.Nodes.AddRange(new[] { uv, panner, noise, power, colorA, colorB, mix, output });
        Connect(graph, uv.Output("UV")!, panner.Input("UV")!);
        Connect(graph, panner.Output("UV")!, noise.Input("UV")!);
        Connect(graph, noise.Output("Out")!, power.Input("A")!);
        Connect(graph, colorA.Output("Color")!, mix.Input("A")!);
        Connect(graph, colorB.Output("Color")!, mix.Input("B")!);
        Connect(graph, power.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mix.Output("Color")!, output.Input("Emission")!);
        return graph;
    }

    public static ShaderGraphModel Portal()
    {
        var graph = new ShaderGraphModel { Name = "Material_Portal_Pro", Surface = "Transparent", BlendMode = "Additive", Version = "2.0" };

        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 110);
        var rotator = NodeFactory.Create(NodeKind.Rotator, 290, 110);
        var voronoi = NodeFactory.Create(NodeKind.Voronoi, 520, 110);
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 520, 330);
        var gradient = NodeFactory.Create(NodeKind.Gradient, 760, 130);
        gradient.ColorHex = "#FF4527FF";
        gradient.ColorHex2 = "#FF00E5FF";
        var mix = NodeFactory.Create(NodeKind.Mix, 990, 230);
        var output = NodeFactory.Create(NodeKind.Output, 1240, 260);

        graph.Nodes.AddRange(new[] { uv, rotator, voronoi, fresnel, gradient, mix, output });
        Connect(graph, uv.Output("UV")!, rotator.Input("UV")!);
        Connect(graph, rotator.Output("UV")!, voronoi.Input("UV")!);
        Connect(graph, voronoi.Output("Out")!, gradient.Input("T")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("A")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mix.Output("Color")!, output.Input("Emission")!);
        return graph;
    }

    public static ShaderGraphModel Hologram()
    {
        var graph = new ShaderGraphModel { Name = "Material_Hologram_Pro", Surface = "Transparent", BlendMode = "Alpha", CullMode = "Back", DepthWrite = false, Version = "2.0" };

        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 120);
        var tiling = NodeFactory.Create(NodeKind.TilingOffset, 300, 120);
        var gradient = NodeFactory.Create(NodeKind.Gradient, 540, 120);
        gradient.ColorHex = "#FF02111F";
        gradient.ColorHex2 = "#FF38F8FF";
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 540, 360);
        var mix = NodeFactory.Create(NodeKind.Mix, 790, 220);
        var output = NodeFactory.Create(NodeKind.Output, 1040, 260);

        graph.Nodes.AddRange(new[] { uv, tiling, gradient, fresnel, mix, output });
        Connect(graph, uv.Output("UV")!, tiling.Input("UV")!);
        Connect(graph, tiling.Output("Out")!, gradient.Input("T")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("A")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mix.Output("Color")!, output.Input("Emission")!);
        Connect(graph, fresnel.Output("Out")!, output.Input("Alpha")!);
        return graph;
    }


    public static ShaderGraphModel Water()
    {
        var graph = new ShaderGraphModel { Name = "Material_Water_Pro", Surface = "Transparent", BlendMode = "Alpha", CullMode = "Back", DepthWrite = false, Version = "2.1" };
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 120);
        var panner = NodeFactory.Create(NodeKind.Panner, 300, 115);
        var noise = NodeFactory.Create(NodeKind.Noise, 535, 115);
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 535, 330);
        var gradient = NodeFactory.Create(NodeKind.Gradient, 760, 135);
        gradient.ColorHex = "#FF06243D";
        gradient.ColorHex2 = "#FF38BDF8";
        var mix = NodeFactory.Create(NodeKind.Mix, 1000, 230);
        var output = NodeFactory.Create(NodeKind.Output, 1260, 270);
        graph.Nodes.AddRange(new[] { uv, panner, noise, fresnel, gradient, mix, output });
        graph.Groups.Add(new GraphGroup { Title = "Water surface", X = 45, Y = 45, Width = 1200, Height = 455, ColorHex = "#5538BDF8" });
        Connect(graph, uv.Output("UV")!, panner.Input("UV")!);
        Connect(graph, panner.Output("UV")!, noise.Input("UV")!);
        Connect(graph, noise.Output("Out")!, gradient.Input("T")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("A")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mix.Output("Color")!, output.Input("Emission")!);
        Connect(graph, fresnel.Output("Out")!, output.Input("Alpha")!);
        return graph;
    }

    public static ShaderGraphModel WaterDeep()
    {
        var graph = new ShaderGraphModel { Name = "Material_WaterDeep_Pro", Surface = "Transparent", BlendMode = "Alpha", CullMode = "Back", DepthWrite = false, Version = "2.1" };
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 120);
        var panner = NodeFactory.Create(NodeKind.Panner, 300, 115);
        var noise = NodeFactory.Create(NodeKind.Noise, 535, 115);
        var gradient = NodeFactory.Create(NodeKind.Gradient, 760, 115);
        gradient.ColorHex = "#FF38BDF8";
        gradient.ColorHex2 = "#FFE0F7FA";
        var deepColor = NodeFactory.Create(NodeKind.ConstantColor, 760, 460);
        deepColor.ColorHex = "#FF06243D";
        var depthFade = NodeFactory.Create(NodeKind.DepthFade, 300, 460);
        depthFade.Input("Distance")!.DefaultValue = "3.0";
        var mixDepth = NodeFactory.Create(NodeKind.Mix, 1000, 230);
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 1000, 520);
        var foamColor = NodeFactory.Create(NodeKind.ConstantColor, 760, 650);
        foamColor.ColorHex = "#FFFFFFFF";
        var mixFoam = NodeFactory.Create(NodeKind.Mix, 1240, 360);
        var output = NodeFactory.Create(NodeKind.Output, 1480, 380);
        graph.Nodes.AddRange(new[] { uv, panner, noise, gradient, deepColor, depthFade, mixDepth, fresnel, foamColor, mixFoam, output });
        graph.Groups.Add(new GraphGroup { Title = "Water deep (Depth Fade)", X = 45, Y = 45, Width = 1480, Height = 670, ColorHex = "#5538BDF8" });
        Connect(graph, uv.Output("UV")!, panner.Input("UV")!);
        Connect(graph, panner.Output("UV")!, noise.Input("UV")!);
        Connect(graph, noise.Output("Out")!, gradient.Input("T")!);
        Connect(graph, gradient.Output("Color")!, mixDepth.Input("A")!);
        Connect(graph, deepColor.Output("Color")!, mixDepth.Input("B")!);
        Connect(graph, depthFade.Output("Fade")!, mixDepth.Input("T")!);
        Connect(graph, mixDepth.Output("Color")!, mixFoam.Input("A")!);
        Connect(graph, foamColor.Output("Color")!, mixFoam.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mixFoam.Input("T")!);
        Connect(graph, mixFoam.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mixFoam.Output("Color")!, output.Input("Emission")!);
        Connect(graph, depthFade.Output("Fade")!, output.Input("Alpha")!);
        return graph;
    }

    public static ShaderGraphModel Ice()
    {
        var graph = new ShaderGraphModel { Name = "Material_Ice_Pro", Surface = "Transparent", BlendMode = "Alpha", CullMode = "Back", DepthWrite = false, Version = "2.1" };
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 120);
        var noise = NodeFactory.Create(NodeKind.Noise, 310, 120);
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 310, 330);
        var gradient = NodeFactory.Create(NodeKind.Gradient, 560, 135);
        gradient.ColorHex = "#FFBFEFFF";
        gradient.ColorHex2 = "#FF1D4ED8";
        var mix = NodeFactory.Create(NodeKind.Mix, 800, 230);
        var output = NodeFactory.Create(NodeKind.Output, 1060, 270);
        graph.Nodes.AddRange(new[] { uv, noise, fresnel, gradient, mix, output });
        graph.Groups.Add(new GraphGroup { Title = "Ice / crystal", X = 45, Y = 55, Width = 1020, Height = 430, ColorHex = "#557DD3FC" });
        Connect(graph, uv.Output("UV")!, noise.Input("UV")!);
        Connect(graph, noise.Output("Out")!, gradient.Input("T")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("A")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mix.Output("Color")!, output.Input("Emission")!);
        Connect(graph, fresnel.Output("Out")!, output.Input("Alpha")!);
        return graph;
    }

    public static ShaderGraphModel Toon()
    {
        var graph = new ShaderGraphModel { Name = "Material_Toon_Pro", Surface = "Opaque", Version = "2.1" };
        var colorA = NodeFactory.Create(NodeKind.ConstantColor, 100, 160);
        colorA.ColorHex = "#FF1E293B";
        var colorB = NodeFactory.Create(NodeKind.ConstantColor, 100, 330);
        colorB.ColorHex = "#FFFFD166";
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 360, 250);
        var posterize = NodeFactory.Create(NodeKind.Posterize, 590, 250);
        var mix = NodeFactory.Create(NodeKind.Mix, 820, 250);
        var output = NodeFactory.Create(NodeKind.Output, 1080, 285);
        graph.Nodes.AddRange(new[] { colorA, colorB, fresnel, posterize, mix, output });
        graph.Groups.Add(new GraphGroup { Title = "Toon ramp", X = 55, Y = 92, Width = 1010, Height = 430, ColorHex = "#55F59E0B" });
        Connect(graph, fresnel.Output("Out")!, posterize.Input("In")!);
        Connect(graph, colorA.Output("Color")!, mix.Input("A")!);
        Connect(graph, colorB.Output("Color")!, mix.Input("B")!);
        Connect(graph, posterize.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        return graph;
    }

    public static ShaderGraphModel Dissolve()
    {
        var graph = new ShaderGraphModel { Name = "Material_Dissolve_Pro", Surface = "Transparent", BlendMode = "Alpha", DepthWrite = false, Version = "2.1" };
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 130);
        var noise = NodeFactory.Create(NodeKind.Noise, 310, 130);
        var threshold = NodeFactory.Create(NodeKind.ConstantFloat, 310, 340);
        threshold.Title = "Dissolve Amount";
        threshold.FloatValue = 0.42f;
        var step = NodeFactory.Create(NodeKind.Step, 560, 220);
        var color = NodeFactory.Create(NodeKind.ConstantColor, 560, 420);
        color.ColorHex = "#FFFF6A00";
        var output = NodeFactory.Create(NodeKind.Output, 840, 270);
        graph.Nodes.AddRange(new[] { uv, noise, threshold, step, color, output });
        graph.Groups.Add(new GraphGroup { Title = "Dissolve mask", X = 45, Y = 65, Width = 785, Height = 505, ColorHex = "#55EF4444" });
        Connect(graph, uv.Output("UV")!, noise.Input("UV")!);
        Connect(graph, threshold.Output("Value")!, step.Input("Edge")!);
        Connect(graph, noise.Output("Out")!, step.Input("In")!);
        Connect(graph, color.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, color.Output("Color")!, output.Input("Emission")!);
        Connect(graph, step.Output("Out")!, output.Input("Alpha")!);
        return graph;
    }

    public static ShaderGraphModel Glass()
    {
        var graph = new ShaderGraphModel { Name = "Material_Glass_Pro", Surface = "Transparent", BlendMode = "Alpha", CullMode = "Back", DepthWrite = false, Version = "2.1" };
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 120, 150);
        var color = NodeFactory.Create(NodeKind.ConstantColor, 120, 360);
        color.ColorHex = "#FF9BE7FF";
        var mix = NodeFactory.Create(NodeKind.Mix, 390, 250);
        var output = NodeFactory.Create(NodeKind.Output, 650, 285);
        graph.Nodes.AddRange(new[] { fresnel, color, mix, output });
        graph.Groups.Add(new GraphGroup { Title = "Glass fresnel", X = 65, Y = 90, Width = 570, Height = 420, ColorHex = "#5567E8F9" });
        Connect(graph, color.Output("Color")!, mix.Input("A")!);
        Connect(graph, color.Output("Color")!, mix.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mix.Output("Color")!, output.Input("Emission")!);
        Connect(graph, fresnel.Output("Out")!, output.Input("Alpha")!);
        return graph;
    }

    public static ShaderGraphModel EnergyShield()
    {
        var graph = new ShaderGraphModel { Name = "Material_EnergyShield_Pro", Surface = "Transparent", BlendMode = "Additive", CullMode = "Back", DepthWrite = false, Version = "2.1" };
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 120);
        var rotator = NodeFactory.Create(NodeKind.Rotator, 310, 120);
        var noise = NodeFactory.Create(NodeKind.Noise, 540, 120);
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 540, 330);
        var gradient = NodeFactory.Create(NodeKind.Gradient, 780, 165);
        gradient.ColorHex = "#FF172554";
        gradient.ColorHex2 = "#FF00F5FF";
        var mix = NodeFactory.Create(NodeKind.Mix, 1030, 250);
        var output = NodeFactory.Create(NodeKind.Output, 1290, 285);
        graph.Nodes.AddRange(new[] { uv, rotator, noise, fresnel, gradient, mix, output });
        graph.Groups.Add(new GraphGroup { Title = "Energy shield", X = 45, Y = 55, Width = 1225, Height = 480, ColorHex = "#555B21B6" });
        Connect(graph, uv.Output("UV")!, rotator.Input("UV")!);
        Connect(graph, rotator.Output("UV")!, noise.Input("UV")!);
        Connect(graph, noise.Output("Out")!, gradient.Input("T")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("A")!);
        Connect(graph, gradient.Output("Color")!, mix.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, mix.Output("Color")!, output.Input("Emission")!);
        Connect(graph, fresnel.Output("Out")!, output.Input("Alpha")!);
        return graph;
    }


    public static ShaderGraphModel GlowMask()
    {
        var graph = new ShaderGraphModel { Name = "Material_GlowMask_Pro", Surface = "Opaque", Version = "2.2" };
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 150);
        var tex = NodeFactory.Create(NodeKind.TextureSample, 310, 130);
        tex.TextValue = "u_MainTex";
        var smooth = NodeFactory.Create(NodeKind.Smoothstep, 560, 285);
        smooth.Input("Edge1")!.DefaultValue = "0.65";
        smooth.Input("Edge2")!.DefaultValue = "1.0";
        var ramp = NodeFactory.Create(NodeKind.ColorRamp, 800, 260);
        ramp.ColorHex = "#FF01040A";
        ramp.ColorHex2 = "#FF22D3EE";
        ramp.ColorIntensity2 = 6f;
        var output = NodeFactory.Create(NodeKind.Output, 1070, 210);

        graph.Nodes.AddRange(new[] { uv, tex, smooth, ramp, output });
        graph.Groups.Add(new GraphGroup { Title = "White mask → emission glow", X = 45, Y = 75, Width = 1015, Height = 430, ColorHex = "#5538BDF8" });
        Connect(graph, uv.Output("UV")!, tex.Input("UV")!);
        Connect(graph, tex.Output("RGB")!, output.Input("Base Color")!);
        Connect(graph, tex.Output("R")!, smooth.Input("In")!);
        Connect(graph, smooth.Output("Out")!, ramp.Input("T")!);
        Connect(graph, ramp.Output("Color")!, output.Input("Emission")!);
        return graph;
    }

    public static ShaderGraphModel FirePro()
    {
        var graph = new ShaderGraphModel { Name = "Material_Fire_Twirl_Pro", Surface = "Transparent", BlendMode = "Additive", CullMode = "Back", DepthWrite = false, Version = "2.3" };

        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 130);
        var panner = NodeFactory.Create(NodeKind.Panner, 300, 130);
        panner.FloatValue = 0.0f;
        panner.FloatValue2 = 1.25f;
        var twirl = NodeFactory.Create(NodeKind.Twirl, 535, 120);
        twirl.FloatValue = 2.1f;
        var noise = NodeFactory.Create(NodeKind.Noise, 780, 120);
        noise.FloatValue = 11.0f;
        var ramp = NodeFactory.Create(NodeKind.ColorRamp, 1015, 155);
        ramp.ColorHex = "#FF160400";
        ramp.ColorHex2 = "#FFFFB020";
        ramp.ColorIntensity2 = 5.5f;
        var fresnel = NodeFactory.Create(NodeKind.Fresnel, 780, 360);
        fresnel.FloatValue = 3.6f;
        var mix = NodeFactory.Create(NodeKind.Mix, 1255, 260);
        var output = NodeFactory.Create(NodeKind.Output, 1515, 285);

        graph.Nodes.AddRange(new[] { uv, panner, twirl, noise, ramp, fresnel, mix, output });
        graph.Groups.Add(new GraphGroup { Title = "Animated fire: UV pan + twirl + noise + HDR emission", X = 45, Y = 65, Width = 1445, Height = 500, ColorHex = "#55FF6A00" });

        Connect(graph, uv.Output("UV")!, panner.Input("UV")!);
        Connect(graph, panner.Output("UV")!, twirl.Input("UV")!);
        Connect(graph, twirl.Output("UV")!, noise.Input("UV")!);
        Connect(graph, noise.Output("Out")!, ramp.Input("T")!);
        Connect(graph, ramp.Output("Color")!, mix.Input("A")!);
        Connect(graph, ramp.Output("Color")!, mix.Input("B")!);
        Connect(graph, fresnel.Output("Out")!, mix.Input("T")!);
        Connect(graph, mix.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, ramp.Output("Color")!, output.Input("Emission")!);
        Connect(graph, noise.Output("Out")!, output.Input("Alpha")!);
        return graph;
    }


    public static ShaderGraphModel RimLightPro()
    {
        var graph = new ShaderGraphModel { Name = "Material_RimLight_Pro", Surface = "Opaque", Version = "2.4" };
        var color = NodeFactory.Create(NodeKind.ConstantColor, 90, 150);
        color.ColorHex = "#FF111827";
        var rim = NodeFactory.Create(NodeKind.RimLight, 340, 150);
        rim.ColorHex = "#FF38BDF8";
        rim.ColorIntensity = 4.0f;
        var output = NodeFactory.Create(NodeKind.Output, 640, 180);
        graph.Nodes.AddRange(new[] { color, rim, output });
        graph.Groups.Add(new GraphGroup { Title = "Rim light HDR emission", X = 45, Y = 75, Width = 595, Height = 310, ColorHex = "#5538BDF8" });
        Connect(graph, color.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, rim.Output("Color")!, output.Input("Emission")!);
        return graph;
    }

    public static ShaderGraphModel EmissionPulsePro()
    {
        var graph = new ShaderGraphModel { Name = "Material_EmissionPulse_Pro", Surface = "Transparent", BlendMode = "Additive", DepthWrite = false, Version = "2.4" };
        var color = NodeFactory.Create(NodeKind.ConstantColor, 90, 160);
        color.ColorHex = "#FFFF7A18";
        color.ColorIntensity = 2.0f;
        var pulse = NodeFactory.Create(NodeKind.EmissionPulse, 340, 155);
        var fresnel = NodeFactory.Create(NodeKind.FresnelPro, 340, 360);
        var output = NodeFactory.Create(NodeKind.Output, 690, 245);
        graph.Nodes.AddRange(new[] { color, pulse, fresnel, output });
        graph.Groups.Add(new GraphGroup { Title = "Animated HDR emission pulse", X = 45, Y = 75, Width = 655, Height = 465, ColorHex = "#55F97316" });
        Connect(graph, color.Output("Color")!, pulse.Input("Color")!);
        Connect(graph, pulse.Output("Color")!, output.Input("Emission")!);
        Connect(graph, color.Output("Color")!, output.Input("Base Color")!);
        Connect(graph, fresnel.Output("Value")!, output.Input("Alpha")!);
        return graph;
    }

    private static void Connect(ShaderGraphModel graph, GraphPin from, GraphPin to)
    {
        graph.Connections.RemoveAll(c => c.ToPinId == to.Id);
        graph.Connections.Add(new GraphConnection
        {
            FromPinId = from.Id,
            ToPinId = to.Id
        });
    }
}
