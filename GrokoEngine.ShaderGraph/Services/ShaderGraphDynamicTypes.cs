using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

public static class ShaderGraphDynamicTypes
{
    public static bool Synchronize(ShaderGraphModel graph)
    {
        graph.Normalize();
        bool changed = false;

        for (int i = 0; i < 6; i++)
        {
            bool passChanged = false;
            foreach (var node in graph.Nodes)
                passChanged |= SynchronizeNode(graph, node);

            changed |= passChanged;
            if (!passChanged)
                break;
        }

        return changed;
    }

    private static bool SynchronizeNode(ShaderGraphModel graph, ShaderNode node)
    {
        return node.Kind switch
        {
            NodeKind.Add or NodeKind.Subtract or NodeKind.Multiply or NodeKind.Divide
                or NodeKind.Min or NodeKind.Max => SyncBinary(graph, node),
            NodeKind.Lerp => SyncBinary(graph, node, keepTFloat: true),
            NodeKind.Remap => SyncRemap(graph, node),
            NodeKind.Sin or NodeKind.Cos or NodeKind.OneMinus or NodeKind.Saturate
                or NodeKind.Abs or NodeKind.Floor or NodeKind.Ceil or NodeKind.Fraction
                or NodeKind.Negate or NodeKind.Reciprocal => SyncUnary(graph, node),
            NodeKind.Step => SyncStep(graph, node),
            NodeKind.MultiplyAdd => SyncMultiplyAdd(graph, node),
            NodeKind.Length => SyncLength(graph, node),
            NodeKind.Normalize => SyncNormalize(graph, node),
            NodeKind.Dot => SyncDot(graph, node),
            _ => false
        };
    }

    private static bool SyncBinary(ShaderGraphModel graph, ShaderNode node, bool keepTFloat = false)
    {
        var a = node.Input("A");
        var b = node.Input("B");
        if (a is null || b is null)
            return false;

        var type = HighestNumericType(ResolveInputType(graph, a), ResolveInputType(graph, b));
        bool changed = SetPinType(a, type);
        changed |= SetPinType(b, type);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, type);

        if (keepTFloat && node.Input("T") is { } t)
            changed |= SetPinType(t, PinType.Float);

        return changed;
    }

    private static bool SyncRemap(ShaderGraphModel graph, ShaderNode node)
    {
        var input = node.Input("In") ?? node.Input("X");
        var inRange = node.Input("In Min Max") ?? node.Input("From Min") ?? node.Input("From Max");
        var outRange = node.Input("Out Min Max") ?? node.Input("To Min") ?? node.Input("To Max");
        if (input is null || inRange is null || outRange is null)
            return false;

        bool changed = SetPinType(input, PinType.Float);
        changed |= SetPinType(inRange, PinType.Vec2);
        changed |= SetPinType(outRange, PinType.Vec2);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, PinType.Float);
        return changed;
    }

    private static bool SyncUnary(ShaderGraphModel graph, ShaderNode node)
    {
        var x = InputAny(node, "In", "X");
        if (x is null)
            return false;

        var type = ResolveInputType(graph, x);
        bool changed = SetPinType(x, type);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, type);
        return changed;
    }

    private static bool SyncStep(ShaderGraphModel graph, ShaderNode node)
    {
        var edge = node.Input("Edge");
        var x = InputAny(node, "In", "X");
        if (edge is null || x is null)
            return false;

        var type = HighestNumericType(ResolveInputType(graph, edge), ResolveInputType(graph, x));
        bool changed = SetPinType(edge, type);
        changed |= SetPinType(x, type);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, type);
        return changed;
    }

    private static bool SyncMultiplyAdd(ShaderGraphModel graph, ShaderNode node)
    {
        var a = node.Input("A");
        var b = node.Input("B");
        var c = node.Input("C");
        if (a is null || b is null || c is null)
            return false;

        var type = HighestNumericType(ResolveInputType(graph, a), ResolveInputType(graph, b), ResolveInputType(graph, c));
        bool changed = SetPinType(a, type);
        changed |= SetPinType(b, type);
        changed |= SetPinType(c, type);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, type);
        return changed;
    }

    private static bool SyncLength(ShaderGraphModel graph, ShaderNode node)
    {
        var vector = InputAny(node, "In", "Vector");
        if (vector is null)
            return false;

        var type = ResolveInputType(graph, vector);
        bool changed = SetPinType(vector, type);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, PinType.Float);
        return changed;
    }

    private static bool SyncNormalize(ShaderGraphModel graph, ShaderNode node)
    {
        var vector = InputAny(node, "In", "Vector");
        if (vector is null)
            return false;

        var type = ResolveInputType(graph, vector);
        if (type == PinType.Float)
            type = PinType.Vec3;

        bool changed = SetPinType(vector, type);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, type);
        return changed;
    }

    private static bool SyncDot(ShaderGraphModel graph, ShaderNode node)
    {
        var a = node.Input("A");
        var b = node.Input("B");
        if (a is null || b is null)
            return false;

        var type = HighestNumericType(ResolveInputType(graph, a), ResolveInputType(graph, b));
        if (type == PinType.Float)
            type = PinType.Vec3;

        bool changed = SetPinType(a, type);
        changed |= SetPinType(b, type);
        foreach (var output in node.Outputs)
            changed |= SetPinType(output, PinType.Float);
        return changed;
    }

    private static PinType ResolveInputType(ShaderGraphModel graph, GraphPin input)
    {
        var connection = graph.FindConnectionToInput(input.Id);
        if (connection is null)
            return input.Type;

        var sourcePin = graph.FindPin(connection.FromPinId);
        return sourcePin?.Type ?? input.Type;
    }

    private static GraphPin? InputAny(ShaderNode node, params string[] names)
    {
        foreach (var name in names)
        {
            var pin = node.Input(name);
            if (pin is not null)
                return pin;
        }

        return null;
    }

    private static PinType HighestNumericType(params PinType[] types)
    {
        var rank = types.Select(t => t switch
        {
            PinType.Vec4 => 4,
            PinType.Vec3 => 3,
            PinType.Vec2 => 2,
            _ => 1
        }).Max();

        return rank switch
        {
            4 => PinType.Vec4,
            3 => PinType.Vec3,
            2 => PinType.Vec2,
            _ => PinType.Float
        };
    }

    private static bool SetPinType(GraphPin pin, PinType type)
    {
        if (pin.Type == type)
            return false;

        pin.Type = type;
        if (pin.Direction == PinDirection.Input)
            pin.DefaultValue = DefaultForType(type, pin.DefaultValue);
        return true;
    }

    private static string DefaultForType(PinType type, string currentDefault)
    {
        string scalar = currentDefault.Contains("1.0", StringComparison.OrdinalIgnoreCase) ||
                        currentDefault.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
            ? "1.0"
            : "0.0";

        return type switch
        {
            PinType.Vec2 => $"vec2({scalar})",
            PinType.Vec3 => $"vec3({scalar})",
            PinType.Vec4 => $"vec4({scalar})",
            PinType.Texture2D => "u_MainTex",
            _ => scalar
        };
    }
}
