namespace GrokoShaderGraphPro.Models;

/// <summary>A visual comment-group box on the canvas.</summary>
public sealed class GraphGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Comment Group";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 260;

    /// <summary>ARGB hex color for the group background, e.g. "#552563EB".</summary>
    public string ColorHex { get; set; } = "#552563EB";

    public string Comment { get; set; } = string.Empty;
}
