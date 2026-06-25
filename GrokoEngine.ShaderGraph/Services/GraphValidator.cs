using System.IO;
using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

public enum ValidationSeverity { Info, Warning, Error }

public sealed class GraphValidationIssue
{
    public ValidationSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? NodeId { get; init; }
    public string? NodeTitle { get; init; }
    public List<Guid> PinIds { get; init; } = [];
    public string Code { get; init; } = string.Empty;

    public string DisplayText
    {
        get
        {
            var prefix = Severity switch
            {
                ValidationSeverity.Error => "❌",
                ValidationSeverity.Warning => "⚠️",
                _ => "ℹ️"
            };
            var node = string.IsNullOrWhiteSpace(NodeTitle) ? string.Empty : $" [{NodeTitle}]";
            var code = string.IsNullOrWhiteSpace(Code) ? string.Empty : $" {Code}:";
            return $"{prefix}{node}{code} {Message}";
        }
    }

    public override string ToString() => DisplayText;
}

/// <summary>Validates a <see cref="ShaderGraphModel"/> and returns a list of issues.</summary>
public static class GraphValidator
{
    // ── Property node kinds ──────────────────────────────────────
    private static readonly HashSet<NodeKind> PropertyKinds =
    [
        NodeKind.PropertyFloat, NodeKind.PropertyColor,
        NodeKind.PropertyVector2, NodeKind.PropertyVector3, NodeKind.PropertyVector4,
        NodeKind.PropertyTexture2D
    ];

    private static readonly HashSet<NodeKind> TextureKinds =
    [
        NodeKind.TextureSample, NodeKind.NormalMap, NodeKind.Triplanar,
        NodeKind.MetallicMap, NodeKind.SmoothnessMap, NodeKind.AmbientOcclusionMap,
        NodeKind.PropertyTexture2D
    ];

    private static readonly HashSet<string> ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tif", ".tiff"];

    // ── Public API ───────────────────────────────────────────────

    public static List<GraphValidationIssue> Validate(ShaderGraphModel graph)
    {
        graph.Normalize();
        var repair = ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);
        var issues = new List<GraphValidationIssue>();

        if (repair.Changed)
        {
            issues.Add(Info(
                $"Schema reparado: +{repair.AddedPins} pins, {repair.UpdatedPins} actualizados, {repair.RemovedDuplicatePins} duplicados removidos.",
                null,
                null,
                "SCHEMA_REPAIR"));
        }

        var pins = graph.Nodes.SelectMany(n => n.Inputs.Concat(n.Outputs)).ToList();
        var pinIds = pins.Select(p => p.Id).ToHashSet();
        var nodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();

        ValidateStructure(graph, issues, nodeIds, pinIds);
        ValidatePinIntegrity(graph, issues, nodeIds);
        ValidateConnections(graph, issues, pinIds);
        ValidateProperties(graph, issues);
        ValidateRenderState(graph, issues);
        ValidateNodeSemantics(graph, issues);
        ValidateUnusedNodes(graph, issues);
        ValidateOutput(graph, issues);

        if (issues.Count == 0)
            issues.Add(Info("Graph limpio: sin errores."));

        return issues;
    }

    // ── Private validation sections ──────────────────────────────

    private static void ValidateStructure(
        ShaderGraphModel graph,
        List<GraphValidationIssue> issues,
        HashSet<Guid> nodeIds,
        HashSet<Guid> pinIds)
    {
        if (graph.Nodes.Count == 0)
        {
            issues.Add(Error("El graph no tiene nodos."));
            return;
        }

        var outputs = graph.Nodes.Where(n => n.Kind == NodeKind.Output).ToList();

        if (outputs.Count == 0)
            issues.Add(Error("Falta un Master Output."));
        else if (outputs.Count > 1)
            issues.Add(Warning("Hay más de un Master Output. Solo se usará el primero."));

        if (graph.Nodes.GroupBy(n => n.Id).Any(g => g.Count() > 1))
            issues.Add(Error("Hay nodos con ID duplicado. Usa Repair/Load para regenerarlos."));

        var allPins = graph.Nodes.SelectMany(n => n.Inputs.Concat(n.Outputs));
        if (allPins.GroupBy(p => p.Id).Any(g => g.Count() > 1))
            issues.Add(Error("Hay pins con ID duplicado. Las conexiones pueden romperse."));
    }

    private static void ValidatePinIntegrity(
        ShaderGraphModel graph,
        List<GraphValidationIssue> issues,
        HashSet<Guid> nodeIds)
    {
        foreach (var node in graph.Nodes)
        {
            foreach (var pin in node.Inputs.Concat(node.Outputs))
            {
                if (pin.NodeId != node.Id || !nodeIds.Contains(pin.NodeId))
                    issues.Add(Error($"Pin '{pin.Name}' tiene NodeId incorrecto.", node.Id, node.Title, "PIN_NODEID"));
            }
        }
    }

    private static void ValidateConnections(
        ShaderGraphModel graph,
        List<GraphValidationIssue> issues,
        HashSet<Guid> pinIds)
    {
        foreach (var connection in graph.Connections)
        {
            if (!pinIds.Contains(connection.FromPinId) || !pinIds.Contains(connection.ToPinId))
            {
                issues.Add(Error("Hay una conexión rota apuntando a un pin inexistente."));
                continue;
            }

            var from = graph.FindPin(connection.FromPinId)!;
            var to = graph.FindPin(connection.ToPinId)!;
            var fromNode = graph.FindNodeByPin(connection.FromPinId);
            var toNode = graph.FindNodeByPin(connection.ToPinId);

            if (from.Direction != PinDirection.Output || to.Direction != PinDirection.Input)
                issues.Add(Error($"Conexión inválida: {from.DisplayName} → {to.DisplayName}."));

            if (fromNode is not null && toNode is not null && fromNode.Id == toNode.Id)
                issues.Add(Error("El nodo está conectado consigo mismo.", fromNode.Id, fromNode.Title, "SELF_CONNECTION"));

            if (from.Type != to.Type)
            {
                if (IsSafeAutoCast(from.Type, to.Type))
                    issues.Add(Info($"Conversión automática: {from.Type} → {to.Type}.", toNode?.Id, toNode?.Title, "AUTO_CAST"));
                else
                    issues.Add(Error($"Pin incompatible: {from.Type} → {to.Type}.", toNode?.Id, toNode?.Title, "TYPE_MISMATCH"));
            }

            if (WouldCreateCycle(graph, fromNode, toNode))
                issues.Add(Error($"Ciclo detectado desde '{fromNode?.Title}'.", toNode?.Id, toNode?.Title, "CYCLE"));
        }
    }

    private static void ValidateProperties(ShaderGraphModel graph, List<GraphValidationIssue> issues)
    {
        var propertyNames = graph.Nodes
            .Where(n => PropertyKinds.Contains(n.Kind))
            .Select(n => string.IsNullOrWhiteSpace(n.TextValue) ? n.Title : n.TextValue.Trim())
            .ToList();

        foreach (var group in propertyNames.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(Warning($"Blackboard property duplicada: '{group.Key}'. Usa nombres únicos para uniforms."));

        ValidateUniformNameCollisions(graph, issues);
    }

    // Un nombre de uniform no puede usarse a la vez para un sampler (Texture2D) y para
    // una property numérica (Float/Color/Vector2/Vector3): el shader generado declararía
    // el mismo identificador dos veces con tipos distintos y no compilaría.
    private static void ValidateUniformNameCollisions(ShaderGraphModel graph, List<GraphValidationIssue> issues)
    {
        var numericKinds = new HashSet<NodeKind>
        {
            NodeKind.PropertyFloat, NodeKind.PropertyColor,
            NodeKind.PropertyVector2, NodeKind.PropertyVector3, NodeKind.PropertyVector4
        };

        var samplerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Nodes.Where(n => TextureKinds.Contains(n.Kind)))
            samplerNames.Add(SanitizeIdentifier(UniformName(node, isTexture: true)));
        foreach (var prop in (graph.Properties ?? []).Where(p => p.Type == PinType.Texture2D))
            samplerNames.Add(SanitizeIdentifier(prop.UniformName));

        foreach (var node in graph.Nodes.Where(n => numericKinds.Contains(n.Kind)))
        {
            var name = SanitizeIdentifier(UniformName(node, isTexture: false));
            if (samplerNames.Contains(name))
                issues.Add(Error($"El uniform '{name}' colisiona con un sampler de textura. Usa un nombre distinto para esta property.", node.Id, node.Title, "UNIFORM_COLLISION"));
        }

        foreach (var prop in (graph.Properties ?? []).Where(p => p.Type != PinType.Texture2D))
        {
            var name = SanitizeIdentifier(prop.UniformName);
            if (samplerNames.Contains(name))
                issues.Add(Error($"El uniform '{name}' colisiona con un sampler de textura. Usa un nombre distinto para esta property.", null, null, "UNIFORM_COLLISION"));
        }
    }

    private static string UniformName(ShaderNode node, bool isTexture)
    {
        if (isTexture)
        {
            var value = string.IsNullOrWhiteSpace(node.TextValue) ? string.Empty : node.TextValue.Trim();
            if (!string.IsNullOrWhiteSpace(node.TexturePath) && IsDefaultTextureUniformName(value))
                return LocalTextureUniformName(node);

            if (string.IsNullOrWhiteSpace(value))
                value = "u_MainTex";

            var fileName = Path.GetFileNameWithoutExtension(value);
            if (!string.IsNullOrWhiteSpace(fileName) && (value.Contains('\\') || value.Contains('/') || value.Contains('.')))
                return "u_" + fileName;
            return value;
        }

        var raw = string.IsNullOrWhiteSpace(node.TextValue) ? node.Title : node.TextValue.Trim();
        return raw.StartsWith("u_", StringComparison.OrdinalIgnoreCase) ? raw : "u_" + raw;
    }

    private static bool IsDefaultTextureUniformName(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v))
            return true;

        return v.Equals("MainTex", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_MainTex", StringComparison.OrdinalIgnoreCase)
            || v.Equals("NormalMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_NormalMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("MetallicMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_MetallicMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("SmoothnessMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_SmoothnessMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("AOMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_AOMap", StringComparison.OrdinalIgnoreCase);
    }

    private static string LocalTextureUniformName(ShaderNode node)
    {
        var prefix = node.Kind switch
        {
            NodeKind.NormalMap => "u_NormalMap_",
            NodeKind.MetallicMap => "u_MetallicMap_",
            NodeKind.SmoothnessMap => "u_SmoothnessMap_",
            NodeKind.AmbientOcclusionMap => "u_AOMap_",
            NodeKind.Triplanar => "u_Triplanar_",
            _ => "u_Texture_"
        };

        return prefix + node.Id.ToString("N")[..8];
    }

    private static string SanitizeIdentifier(string value)
    {
        var chars = value.Select((c, i) => (char.IsLetter(c) || c == '_' || (i > 0 && char.IsDigit(c))) ? c : '_').ToArray();
        var result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? "u_MainTex" : result;
    }


    private static void ValidateRenderState(ShaderGraphModel graph, List<GraphValidationIssue> issues)
    {
        var validSurfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Opaque", "Transparent", "AlphaClip", "Cutout" };
        var validBlend = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alpha", "Additive", "Multiply", "Premultiply", "Opaque" };
        var validCull = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Back", "Front", "Off" };
        var validZ = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LEqual", "Less", "Equal", "Always", "Greater", "GEqual" };

        if (!validSurfaces.Contains(graph.Surface))
            issues.Add(Warning($"Surface desconocido '{graph.Surface}'. Se exportará como Opaque fallback.", null, null, "SURFACE_MODE"));
        if (!validBlend.Contains(graph.BlendMode))
            issues.Add(Warning($"BlendMode desconocido '{graph.BlendMode}'.", null, null, "BLEND_MODE"));
        if (!validCull.Contains(graph.CullMode))
            issues.Add(Warning($"CullMode desconocido '{graph.CullMode}'.", null, null, "CULL_MODE"));
        if (!validZ.Contains(graph.ZTest))
            issues.Add(Warning($"ZTest desconocido '{graph.ZTest}'.", null, null, "ZTEST_MODE"));
        if (graph.Surface.Equals("Transparent", StringComparison.OrdinalIgnoreCase) && graph.DepthWrite)
            issues.Add(Warning("Material transparente con DepthWrite activo puede producir sorting incorrecto.", null, null, "TRANSPARENT_ZWRITE"));
    }

    private static void ValidateNodeSemantics(ShaderGraphModel graph, List<GraphValidationIssue> issues)
    {
        foreach (var node in graph.Nodes)
        {
            switch (node.Kind)
            {
                case NodeKind.TextureSample:
                    if (node.Input("UV") is { } uv && graph.FindConnectionToInput(uv.Id) is null)
                        issues.Add(Info("Texture Sample usará UV0 porque no tiene UV conectado.", node.Id, node.Title, "UV_DEFAULT"));
                    if (string.IsNullOrWhiteSpace(node.TexturePath) && string.IsNullOrWhiteSpace(node.TextValue))
                        issues.Add(Warning("Texture Sample no tiene textura ni uniform; el preview usará checker fallback.", node.Id, node.Title, "TEXTURE_EMPTY"));
                    break;

                case NodeKind.NormalMap:
                    if (node.Input("Texture") is { } tex && graph.FindConnectionToInput(tex.Id) is null && string.IsNullOrWhiteSpace(node.TexturePath))
                        issues.Add(Warning("Normal Map no tiene textura conectada; usará normal geométrica.", node.Id, node.Title, "NORMAL_EMPTY"));
                    break;

                case NodeKind.SubGraph:
                    if (string.IsNullOrWhiteSpace(node.TextValue))
                        issues.Add(Warning("SubGraph sin ruta asignada; funciona como placeholder/passthrough.", node.Id, node.Title, "SUBGRAPH_EMPTY"));
                    break;

                case NodeKind.Output:
                    var baseColor = node.Input("Base Color");
                    if (baseColor is not null && graph.FindConnectionToInput(baseColor.Id) is null)
                        issues.Add(Warning("Master Output no tiene Base Color conectado; usa magenta fallback.", node.Id, node.Title, "OUTPUT_BASECOLOR"));
                    break;
            }

            foreach (var input in node.Inputs)
            {
                var connected = graph.FindConnectionToInput(input.Id) is not null;
                if (!connected && string.IsNullOrWhiteSpace(input.DefaultValue) && node.Kind != NodeKind.Output)
                    issues.Add(Info($"Entrada '{input.Name}' usará valor default del nodo.", node.Id, node.Title, "INPUT_DEFAULT"));
            }
        }
    }

    private static void ValidateUnusedNodes(ShaderGraphModel graph, List<GraphValidationIssue> issues)
    {
        foreach (var node in graph.Nodes.Where(n => n.Kind != NodeKind.Output))
        {
            var isUsed = node.Outputs.Any(o => graph.Connections.Any(c => c.FromPinId == o.Id));
            if (!isUsed)
                issues.Add(Warning("Nodo sin uso; no llega al Master Output.", node.Id, node.Title, "UNUSED_NODE"));

            if (PropertyKinds.Contains(node.Kind) && string.IsNullOrWhiteSpace(node.TextValue))
                issues.Add(Warning("Property sin nombre de uniform asignado.", node.Id, node.Title, "PROPERTY_NAME"));

            if (TextureKinds.Contains(node.Kind))
                ValidateTextureNode(node, issues);
        }
    }

    private static void ValidateTextureNode(ShaderNode node, List<GraphValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(node.TextValue))
        {
            issues.Add(Warning("Texture2D no tiene uniform/ruta asignada; usará checker de preview.", node.Id, node.Title, "TEXTURE_EMPTY"));
            return;
        }

        if (LooksLikeFilePath(node.TextValue))
        {
            var resolved = Environment.ExpandEnvironmentVariables(node.TextValue.Trim().Trim('"'));
            if (!File.Exists(resolved))
                issues.Add(Warning("Texture2D apunta a una imagen que no existe en esta PC.", node.Id, node.Title, "TEXTURE_MISSING"));
        }
    }

    private static void ValidateOutput(ShaderGraphModel graph, List<GraphValidationIssue> issues)
    {
        var output = graph.Nodes.FirstOrDefault(n => n.Kind == NodeKind.Output);
        if (output is null)
            return;

        var alpha = output.Input("Alpha");
        if (alpha is not null && graph.Surface.Equals("Transparent", StringComparison.OrdinalIgnoreCase) && graph.FindConnectionToInput(alpha.Id) is null)
            issues.Add(Info("Surface Transparent sin Alpha conectado; usará alpha 1.0.", output.Id, output.Title, "ALPHA_DEFAULT"));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static bool IsSafeAutoCast(PinType from, PinType to)
    {
        if (from == to)
            return true;
        if (from == PinType.Texture2D || to == PinType.Texture2D)
            return false;

        // Sistema de tipos pro: scalar/vector se puede promocionar o extraer componentes.
        // Texture2D nunca se castea a numérico.
        return true;
    }

    private static bool WouldCreateCycle(ShaderGraphModel graph, ShaderNode? fromNode, ShaderNode? toNode)
    {
        if (fromNode is null || toNode is null || fromNode.Id == toNode.Id)
            return false;

        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(toNode.Id);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
                continue;
            if (current == fromNode.Id)
                return true;

            var node = graph.FindNode(current);
            if (node is null)
                continue;

            foreach (var output in node.Outputs)
            foreach (var c in graph.FindConnectionsFromOutput(output.Id))
            {
                var next = graph.FindNodeByPin(c.ToPinId);
                if (next is not null)
                    stack.Push(next.Id);
            }
        }

        return false;
    }

    private static bool LooksLikeFilePath(string value)
    {
        value = value.Trim();
        return value.Contains('\\') || value.Contains('/')
            || ImageExtensions.Any(ext => value.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static GraphValidationIssue Error(string message, Guid? nodeId = null, string? nodeTitle = null, string code = "")
        => new() { Severity = ValidationSeverity.Error, Message = message, NodeId = nodeId, NodeTitle = nodeTitle, Code = code };

    private static GraphValidationIssue Warning(string message, Guid? nodeId = null, string? nodeTitle = null, string code = "")
        => new() { Severity = ValidationSeverity.Warning, Message = message, NodeId = nodeId, NodeTitle = nodeTitle, Code = code };

    private static GraphValidationIssue Info(string message, Guid? nodeId = null, string? nodeTitle = null, string code = "")
        => new() { Severity = ValidationSeverity.Info, Message = message, NodeId = nodeId, NodeTitle = nodeTitle, Code = code };
}
