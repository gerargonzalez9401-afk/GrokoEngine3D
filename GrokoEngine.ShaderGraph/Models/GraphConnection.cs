namespace GrokoShaderGraphPro.Models;

/// <summary>A directed edge connecting an output pin to an input pin.</summary>
public sealed class GraphConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromPinId { get; set; }
    public Guid ToPinId { get; set; }
}
