namespace GrokoShaderGraphPro.Models;

/// <summary>A single node on the shader graph canvas.</summary>
public sealed class ShaderNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NodeKind Kind { get; set; }
    public string Title { get; set; } = "Node";
    public double X { get; set; }
    public double Y { get; set; }

    // ── Editable values (shared by many node types) ──────────────
    public float FloatValue { get; set; } = 1f;
    public float FloatValue2 { get; set; } = 1f;
    public float FloatValue3 { get; set; } = 0f;

    /// <summary>
    /// Uniform or property name (e.g. "u_MainTex").
    /// Never store file paths here — use <see cref="TexturePath"/> instead.
    /// </summary>
    public string TextValue { get; set; } = string.Empty;

    /// <summary>Absolute or relative path to the texture on disk for preview/import.</summary>
    public string TexturePath { get; set; } = string.Empty;

    /// <summary>Import/runtime settings for TextureSample / NormalMap style nodes.</summary>
    public TextureImportSettings TextureSettings { get; set; } = new();

    /// <summary>Primary ARGB color, e.g. "#FFFF7A18".</summary>
    public string ColorHex { get; set; } = "#FFFFFFFF";

    /// <summary>Secondary ARGB color (used by Gradient, ColorRamp, etc.).</summary>
    public string ColorHex2 { get; set; } = "#FF000000";

    /// <summary>HDR intensity multiplier for the primary color. 1 = LDR, &gt;1 = HDR.</summary>
    public float ColorIntensity { get; set; } = 1f;

    /// <summary>HDR intensity multiplier for the secondary color.</summary>
    public float ColorIntensity2 { get; set; } = 1f;

    public string Comment { get; set; } = string.Empty;

    public List<GraphPin> Inputs { get; set; } = [];
    public List<GraphPin> Outputs { get; set; } = [];

    // ── Helpers ──────────────────────────────────────────────────

    public GraphPin? FindPin(Guid pinId)
        => (Inputs ?? []).Concat(Outputs ?? []).FirstOrDefault(p => p.Id == pinId);

    public GraphPin? Input(string name)
        => (Inputs ?? []).FirstOrDefault(p => (p.Name ?? "").Equals(name, StringComparison.OrdinalIgnoreCase));

    public GraphPin? Output(string name)
        => (Outputs ?? []).FirstOrDefault(p => (p.Name ?? "").Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Normalize()
    {
        Title ??= "Node";
        TextValue ??= string.Empty;
        TexturePath ??= string.Empty;
        TextureSettings ??= new TextureImportSettings();
        TextureSettings.Normalize();
        ColorHex ??= "#FFFFFFFF";
        ColorHex2 ??= "#FF000000";
        Comment ??= string.Empty;
        Inputs ??= [];
        Outputs ??= [];

        foreach (var pin in Inputs)
        {
            pin.NodeId = Id;
            pin.Normalize();
        }

        foreach (var pin in Outputs)
        {
            pin.NodeId = Id;
            pin.Normalize();
        }
    }
}
