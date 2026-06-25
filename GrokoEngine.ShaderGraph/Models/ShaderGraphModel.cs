namespace GrokoShaderGraphPro.Models;

/// <summary>The complete in-memory representation of a shader graph document.</summary>
public sealed class ShaderGraphModel
{
    public string Version { get; set; } = "2.0";
    public string Name { get; set; } = "Untitled Shader";

    // ── Render state ─────────────────────────────────────────────
    public string Surface { get; set; } = "Opaque";
    public string BlendMode { get; set; } = "Alpha";
    public string CullMode { get; set; } = "Back";
    public bool DepthWrite { get; set; } = true;
    public bool DepthTest { get; set; } = true;
    public bool DoubleSided { get; set; } = false;
    public string ZTest { get; set; } = "LEqual";
    public int RenderQueue { get; set; } = 2000;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    // ── Graph data ───────────────────────────────────────────────
    public List<ShaderNode> Nodes { get; set; } = [];
    public List<GraphGroup> Groups { get; set; } = [];
    public List<GraphProperty> Properties { get; set; } = [];
    public List<SubGraphAsset> SubGraphs { get; set; } = [];
    public List<GraphConnection> Connections { get; set; } = [];
    public ShaderGraphEditorState EditorState { get; set; } = new();

    // ── Query helpers ────────────────────────────────────────────

    public ShaderNode? FindNode(Guid nodeId)
        => (Nodes ?? []).FirstOrDefault(n => n.Id == nodeId);

    public GraphPin? FindPin(Guid pinId)
        => (Nodes ?? []).SelectMany(n => (n.Inputs ?? []).Concat(n.Outputs ?? []))
                .FirstOrDefault(p => p.Id == pinId);

    public ShaderNode? FindNodeByPin(Guid pinId)
        => (Nodes ?? []).FirstOrDefault(n =>
            (n.Inputs ?? []).Any(p => p.Id == pinId) ||
            (n.Outputs ?? []).Any(p => p.Id == pinId));

    public GraphConnection? FindConnectionToInput(Guid inputPinId)
        => (Connections ?? []).FirstOrDefault(c => c.ToPinId == inputPinId);

    public IEnumerable<GraphConnection> FindConnectionsFromOutput(Guid outputPinId)
        => (Connections ?? []).Where(c => c.FromPinId == outputPinId);

    public void Normalize()
    {
        Version ??= "2.0";
        Name ??= "Untitled Shader";
        Surface ??= "Opaque";
        BlendMode ??= "Alpha";
        CullMode ??= "Back";
        ZTest ??= "LEqual";

        Nodes ??= [];
        Groups ??= [];
        Properties ??= [];
        SubGraphs ??= [];
        Connections ??= [];
        EditorState ??= new ShaderGraphEditorState();
        EditorState.Normalize();

        foreach (var node in Nodes)
            node.Normalize();

        foreach (var property in Properties)
            property.Normalize();

        foreach (var subGraph in SubGraphs)
            subGraph.Normalize();
    }
}
