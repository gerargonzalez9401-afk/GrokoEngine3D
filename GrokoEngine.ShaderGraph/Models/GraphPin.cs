using System.Text.Json.Serialization;

namespace GrokoShaderGraphPro.Models;

/// <summary>
/// A connection point on a <see cref="ShaderNode"/>.
/// Every pin must have a unique <see cref="Id"/> — never <see cref="Guid.Empty"/>.
/// </summary>
public sealed class GraphPin
{
    [JsonInclude]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PinDirection Direction { get; set; }
    public PinType Type { get; set; }

    /// <summary>GLSL expression used when no cable is connected to this input pin.</summary>
    public string DefaultValue { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => $"{Name} : {Type}";

    public void Normalize()
    {
        Name ??= string.Empty;
        DefaultValue ??= string.Empty;
    }
}
