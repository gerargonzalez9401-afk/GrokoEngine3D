using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

public sealed class ShaderGraphSchemaRepairReport
{
    public int AddedPins { get; internal set; }
    public int UpdatedPins { get; internal set; }
    public int RemovedDuplicatePins { get; internal set; }
    public int RedirectedConnections { get; internal set; }
    public int RemovedDuplicateConnections { get; internal set; }

    public bool Changed =>
        AddedPins > 0 ||
        UpdatedPins > 0 ||
        RemovedDuplicatePins > 0 ||
        RedirectedConnections > 0 ||
        RemovedDuplicateConnections > 0;
}

public static class ShaderGraphSchemaRepair
{
    public static ShaderGraphSchemaRepairReport Repair(ShaderGraphModel graph)
    {
        graph.Normalize();
        var report = new ShaderGraphSchemaRepairReport();

        foreach (var node in graph.Nodes)
            RepairNode(graph, node, report);

        RemoveBrokenConnections(graph);
        RemoveDuplicateInputConnections(graph, report);
        return report;
    }

    private static void RepairNode(ShaderGraphModel graph, ShaderNode node, ShaderGraphSchemaRepairReport report)
    {
        var canonical = NodeFactory.Create(node.Kind, node.X, node.Y);
        if (string.IsNullOrWhiteSpace(node.Title) || node.Title.Equals("Node", StringComparison.OrdinalIgnoreCase))
            node.Title = canonical.Title;

        RepairPinSet(graph, node, node.Inputs, canonical.Inputs, PinDirection.Input, report);
        RepairPinSet(graph, node, node.Outputs, canonical.Outputs, PinDirection.Output, report);
    }

    private static void RepairPinSet(
        ShaderGraphModel graph,
        ShaderNode node,
        List<GraphPin> pins,
        List<GraphPin> canonicalPins,
        PinDirection direction,
        ShaderGraphSchemaRepairReport report)
    {
        foreach (var canonical in canonicalPins)
        {
            var candidates = pins
                .Where(pin => IsSameSemanticPin(node.Kind, direction, pin.Name, canonical.Name))
                .DistinctBy(pin => pin.Id)
                .ToList();

            GraphPin target;
            if (candidates.Count == 0)
            {
                target = ClonePin(canonical, node.Id);
                pins.Add(target);
                report.AddedPins++;
            }
            else
            {
                target = ChooseBestCandidate(graph, candidates, direction);
            }

            foreach (var duplicate in candidates.Where(pin => pin.Id != target.Id).ToList())
            {
                RedirectConnections(graph, duplicate.Id, target.Id, direction, report);
                pins.Remove(duplicate);
                report.RemovedDuplicatePins++;
            }

            if (ApplyCanonicalPin(target, canonical, node.Id))
                report.UpdatedPins++;
        }
    }

    private static GraphPin ChooseBestCandidate(ShaderGraphModel graph, List<GraphPin> candidates, PinDirection direction)
    {
        return candidates.FirstOrDefault(pin => IsPinConnected(graph, pin.Id, direction))
            ?? candidates.First();
    }

    private static bool ApplyCanonicalPin(GraphPin pin, GraphPin canonical, Guid nodeId)
    {
        bool changed = false;
        if (pin.NodeId != nodeId)
        {
            pin.NodeId = nodeId;
            changed = true;
        }

        if (!string.Equals(pin.Name, canonical.Name, StringComparison.Ordinal))
        {
            pin.Name = canonical.Name;
            changed = true;
        }

        if (pin.Direction != canonical.Direction)
        {
            pin.Direction = canonical.Direction;
            changed = true;
        }

        if (pin.Type != canonical.Type)
        {
            pin.Type = canonical.Type;
            if (pin.Direction == PinDirection.Input)
                pin.DefaultValue = canonical.DefaultValue;
            changed = true;
        }
        else if (pin.Direction == PinDirection.Input && string.IsNullOrWhiteSpace(pin.DefaultValue))
        {
            pin.DefaultValue = canonical.DefaultValue;
            changed = true;
        }

        return changed;
    }

    private static GraphPin ClonePin(GraphPin canonical, Guid nodeId) => new()
    {
        NodeId = nodeId,
        Name = canonical.Name,
        Direction = canonical.Direction,
        Type = canonical.Type,
        DefaultValue = canonical.DefaultValue
    };

    private static bool IsPinConnected(ShaderGraphModel graph, Guid pinId, PinDirection direction) =>
        direction == PinDirection.Input
            ? graph.Connections.Any(c => c.ToPinId == pinId)
            : graph.Connections.Any(c => c.FromPinId == pinId);

    private static void RedirectConnections(
        ShaderGraphModel graph,
        Guid fromPinId,
        Guid toPinId,
        PinDirection direction,
        ShaderGraphSchemaRepairReport report)
    {
        foreach (var connection in graph.Connections)
        {
            if (direction == PinDirection.Input && connection.ToPinId == fromPinId)
            {
                connection.ToPinId = toPinId;
                report.RedirectedConnections++;
            }
            else if (direction == PinDirection.Output && connection.FromPinId == fromPinId)
            {
                connection.FromPinId = toPinId;
                report.RedirectedConnections++;
            }
        }
    }

    private static void RemoveBrokenConnections(ShaderGraphModel graph)
    {
        var pinIds = graph.Nodes
            .SelectMany(node => node.Inputs.Concat(node.Outputs))
            .Select(pin => pin.Id)
            .ToHashSet();

        graph.Connections.RemoveAll(c => !pinIds.Contains(c.FromPinId) || !pinIds.Contains(c.ToPinId));
    }

    private static void RemoveDuplicateInputConnections(ShaderGraphModel graph, ShaderGraphSchemaRepairReport report)
    {
        var seenInputs = new HashSet<Guid>();
        report.RemovedDuplicateConnections += graph.Connections.RemoveAll(c => !seenInputs.Add(c.ToPinId));
    }

    private static bool IsSameSemanticPin(NodeKind kind, PinDirection direction, string? actualName, string canonicalName)
    {
        if (NameEquals(actualName, canonicalName))
            return true;

        foreach (var alias in Aliases(kind, direction, canonicalName))
            if (NameEquals(actualName, alias))
                return true;

        return false;
    }

    private static bool NameEquals(string? a, string b) =>
        string.Equals((a ?? string.Empty).Trim(), b, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Aliases(NodeKind kind, PinDirection direction, string canonicalName)
    {
        if (direction == PinDirection.Input)
        {
            if (kind == NodeKind.Output && canonicalName == "Base Color")
                yield return "Color";

            if (kind == NodeKind.Voronoi && canonicalName == "CellDensity")
            {
                yield return "Scale";
                yield return "Density";
            }

            if (kind == NodeKind.Voronoi && canonicalName == "Cell Density")
            {
                yield return "CellDensity";
                yield return "Scale";
                yield return "Density";
            }

            if (kind == NodeKind.Voronoi && canonicalName == "Angle Offset")
                yield return "AngleOffset";

            if (kind == NodeKind.EmissionPulse && canonicalName == "Intensity")
            {
                yield return "Pulse";
                yield return "Max";
            }

            if (kind == NodeKind.SubGraph && canonicalName == "Float In")
                yield return "In";

            if (canonicalName == "In")
            {
                yield return "X";
                yield return "Input";
                yield return "Value";
            }

            if (canonicalName == "In" && kind is NodeKind.Length or NodeKind.Normalize)
                yield return "Vector";

            if (kind == NodeKind.Power && canonicalName == "A")
                yield return "X";

            if (kind == NodeKind.Power && canonicalName == "B")
                yield return "Power";

            if (kind == NodeKind.Smoothstep && canonicalName == "Edge1")
                yield return "Edge A";

            if (kind == NodeKind.Smoothstep && canonicalName == "Edge2")
                yield return "Edge B";

            if (kind == NodeKind.Remap)
            {
                if (canonicalName == "In")
                {
                    yield return "X";
                    yield return "Input";
                }

                if (canonicalName == "In Min Max")
                {
                    yield return "From Min";
                    yield return "From Max";
                    yield return "FromMinMax";
                    yield return "From Range";
                }

                if (canonicalName == "Out Min Max")
                {
                    yield return "To Min";
                    yield return "To Max";
                    yield return "ToMinMax";
                    yield return "To Range";
                }
            }
        }
        else
        {
            if (canonicalName is "UV" or "Vector" or "Color" or "Normal" or "Out")
            {
                yield return "Value";
                yield return "Result";
                yield return "Out";
            }

            if (canonicalName == "Out")
            {
                yield return "Normal";
                yield return "View Dir";
                yield return "Position";
                yield return "Vector";
                yield return "Length";
                yield return "Distance";
                yield return "UV";
            }

            if (canonicalName == "A")
                yield return "Alpha";

            if (canonicalName == "Alpha")
                yield return "A";
        }
    }
}
