namespace GrokoShaderGraphPro.Models;

/// <summary>Editor-only state persisted with a shader graph asset.</summary>
public sealed class ShaderGraphEditorState
{
    public float PanX { get; set; } = 40f;
    public float PanY { get; set; } = 40f;
    public float Zoom { get; set; } = 1f;
    public string PreviewShape { get; set; } = "Sphere";
    public List<Guid> CollapsedPreviewNodeIds { get; set; } = [];

    public void Normalize()
    {
        Zoom = float.IsFinite(Zoom) ? System.Math.Clamp(Zoom, 0.25f, 2.5f) : 1f;
        PreviewShape = string.IsNullOrWhiteSpace(PreviewShape) ? "Sphere" : PreviewShape;
        CollapsedPreviewNodeIds ??= [];
    }
}
