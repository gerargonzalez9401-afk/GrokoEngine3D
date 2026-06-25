using System.Text;
using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

public sealed class GraphProfileReport
{
    public int NodeCount { get; init; }
    public int ConnectionCount { get; init; }
    public int TextureCount { get; init; }
    public int ProceduralCount { get; init; }
    public int MathCount { get; init; }
    public int OutputCount { get; init; }
    public int EstimatedInstructionCost { get; init; }
    public string PerformanceTier { get; init; } = "Unknown";
    public List<string> Warnings { get; init; } = [];
    public List<string> HeavyNodes { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public static class GraphProfiler
{
    private static readonly HashSet<NodeKind> TextureNodes =
        [NodeKind.TextureSample, NodeKind.NormalMap, NodeKind.Triplanar, NodeKind.MetallicMap, NodeKind.SmoothnessMap, NodeKind.AmbientOcclusionMap, NodeKind.PropertyTexture2D];
    private static readonly HashSet<NodeKind> ProceduralNodes = [NodeKind.Noise, NodeKind.GradientNoise, NodeKind.Voronoi, NodeKind.Twirl, NodeKind.Dissolve, NodeKind.Fresnel, NodeKind.FresnelPro, NodeKind.RimLight, NodeKind.EmissionPulse, NodeKind.Gradient, NodeKind.ColorRamp];
    private static readonly HashSet<NodeKind> MathNodes = [NodeKind.Add, NodeKind.Subtract, NodeKind.Multiply, NodeKind.Divide, NodeKind.Power, NodeKind.Sin, NodeKind.Cos, NodeKind.Clamp, NodeKind.Smoothstep, NodeKind.Remap, NodeKind.OneMinus, NodeKind.Posterize, NodeKind.Length, NodeKind.Saturate, NodeKind.Step, NodeKind.Abs, NodeKind.Floor, NodeKind.Ceil, NodeKind.Fraction, NodeKind.Min, NodeKind.Max, NodeKind.Lerp, NodeKind.Negate, NodeKind.Reciprocal, NodeKind.MultiplyAdd, NodeKind.Normalize, NodeKind.Dot, NodeKind.Cross, NodeKind.Distance, NodeKind.Mix];

    public static GraphProfileReport Analyze(ShaderGraphModel graph)
    {
        graph.Normalize();
        ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);
        var nodes = graph.Nodes ?? [];
        var connections = graph.Connections ?? [];
        var warnings = new List<string>();
        var heavy = new List<string>();

        int textureCount = nodes.Count(n => TextureNodes.Contains(n.Kind));
        int proceduralCount = nodes.Count(n => ProceduralNodes.Contains(n.Kind));
        int mathCount = nodes.Count(n => MathNodes.Contains(n.Kind));
        int outputCount = nodes.Count(n => n.Kind == NodeKind.Output);
        int cost = 0;

        foreach (var n in nodes)
        {
            var c = EstimateNodeCost(n.Kind);
            cost += c;
            if (c >= 18)
                heavy.Add($"{n.Title}  cost≈{c}");
        }

        if (outputCount == 0)
            warnings.Add("No hay Master Output. El shader no tendrá salida final.");
        if (outputCount > 1)
            warnings.Add("Hay más de un Master Output. Usa solo uno para export final.");
        if (textureCount > 4)
            warnings.Add($"Usa {textureCount} nodos de textura. Puede ser pesado en GPU baja o móvil.");
        if (proceduralCount > 6)
            warnings.Add($"Usa {proceduralCount} nodos procedural. Noise/Voronoi/Twirl pueden subir el coste.");
        if (nodes.Count > 90)
            warnings.Add("Graph grande. Considera crear SubGraphs para orden y reutilización.");
        if (connections.Count > nodes.Count * 2.5)
            warnings.Add("Muchas conexiones. Revisa si hay nodos duplicados o ramas innecesarias.");

        var tier = cost switch
        {
            < 80 => "Fast / Liviano",
            < 170 => "Normal / Bueno para PC",
            < 280 => "Heavy / Cuidado con móviles",
            _ => "Very Heavy / Optimizar antes de producción"
        };

        return new GraphProfileReport
        {
            NodeCount = nodes.Count,
            ConnectionCount = connections.Count,
            TextureCount = textureCount,
            ProceduralCount = proceduralCount,
            MathCount = mathCount,
            OutputCount = outputCount,
            EstimatedInstructionCost = cost,
            PerformanceTier = tier,
            Warnings = warnings,
            HeavyNodes = heavy,
            Summary = BuildSummary(graph, nodes.Count, connections.Count, textureCount, proceduralCount, mathCount, cost, tier, warnings, heavy)
        };
    }

    private static int EstimateNodeCost(NodeKind kind) => kind switch
    {
        NodeKind.TextureSample => 24,
        NodeKind.Triplanar => 42,
        NodeKind.Flipbook => 30,
        NodeKind.NormalBlend => 8,
        NodeKind.NormalStrength => 5,
        NodeKind.Noise => 28,
        NodeKind.GradientNoise => 24,
        NodeKind.Voronoi => 34,
        NodeKind.Twirl => 16,
        NodeKind.Dissolve => 18,
        NodeKind.FresnelPro => 14,
        NodeKind.RimLight => 16,
        NodeKind.EmissionPulse => 12,
        NodeKind.Power => 8,
        NodeKind.Sin or NodeKind.Cos => 7,
        NodeKind.Smoothstep or NodeKind.Lerp or NodeKind.Remap => 5,
        NodeKind.MultiplyAdd => 4,
        NodeKind.Reciprocal => 4,
        NodeKind.ParallaxOffset => 18,
        NodeKind.NormalMap => 12,
        NodeKind.Output => 2,
        _ => 2
    };

    private static string BuildSummary(ShaderGraphModel graph, int nodes, int connections, int textures, int procedural, int math, int cost, string tier, List<string> warnings, List<string> heavy)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Graph: {graph.Name}");
        sb.AppendLine($"Surface: {graph.Surface} | Blend: {graph.BlendMode} | Cull: {graph.CullMode}");
        sb.AppendLine();
        sb.AppendLine("Resumen:");
        sb.AppendLine($"  Nodos: {nodes}");
        sb.AppendLine($"  Conexiones: {connections}");
        sb.AppendLine($"  Texturas: {textures}");
        sb.AppendLine($"  Procedural: {procedural}");
        sb.AppendLine($"  Math/Vector: {math}");
        sb.AppendLine($"  Coste estimado: {cost}");
        sb.AppendLine($"  Tier: {tier}");
        sb.AppendLine();
        sb.AppendLine("Nodos pesados:");
        if (heavy.Count == 0) sb.AppendLine("  Ninguno importante.");
        else foreach (var h in heavy.Take(20)) sb.AppendLine("  - " + h);
        sb.AppendLine();
        sb.AppendLine("Warnings de rendimiento:");
        if (warnings.Count == 0) sb.AppendLine("  Sin warnings de rendimiento.");
        else foreach (var w in warnings) sb.AppendLine("  - " + w);
        return sb.ToString();
    }
}
