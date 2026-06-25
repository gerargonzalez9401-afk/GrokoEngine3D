namespace GrokoShaderGraphPro.Models;

/// <summary>A reusable sub-graph asset that can be referenced by <see cref="NodeKind.SubGraph"/> nodes.</summary>
public sealed class SubGraphAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "SubGraph";
    public string Description { get; set; } = string.Empty;
    public List<ShaderNode> Nodes { get; set; } = [];
    public List<GraphConnection> Connections { get; set; } = [];
    public List<GraphPin> Inputs { get; set; } = [];
    public List<GraphPin> Outputs { get; set; } = [];

    public void Normalize()
    {
        Name ??= "SubGraph";
        Description ??= string.Empty;
        Nodes ??= [];
        Connections ??= [];
        Inputs ??= [];
        Outputs ??= [];

        foreach (var node in Nodes)
            node.Normalize();

        foreach (var pin in Inputs.Concat(Outputs))
            pin.Normalize();
    }
}
