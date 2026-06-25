namespace GrokoShaderGraphPro.Models;

/// <summary>Import/runtime metadata for Texture2D assets used by nodes or blackboard properties.</summary>
public sealed class TextureImportSettings
{
    public bool SRgb { get; set; } = true;
    public bool IsNormalMap { get; set; } = false;
    public string NormalSpace { get; set; } = "Tangent";
    public bool GenerateMipMaps { get; set; } = true;
    public string WrapMode { get; set; } = "Repeat";
    public string FilterMode { get; set; } = "Linear";
    public int Anisotropy { get; set; } = 1;
    public string DefaultTexturePolicy { get; set; } = "CheckerMagenta";

    public void Normalize()
    {
        NormalSpace = string.IsNullOrWhiteSpace(NormalSpace) ? "Tangent" : NormalSpace;
        WrapMode = string.IsNullOrWhiteSpace(WrapMode) ? "Repeat" : WrapMode;
        FilterMode = string.IsNullOrWhiteSpace(FilterMode) ? "Linear" : FilterMode;
        DefaultTexturePolicy = string.IsNullOrWhiteSpace(DefaultTexturePolicy) ? "CheckerMagenta" : DefaultTexturePolicy;
        Anisotropy = System.Math.Max(1, Anisotropy);
    }
}
