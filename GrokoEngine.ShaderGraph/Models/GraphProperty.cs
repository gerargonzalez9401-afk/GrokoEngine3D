namespace GrokoShaderGraphPro.Models;

/// <summary>
/// A named, exposed material property that appears in the Blackboard panel
/// and is exported as a GLSL uniform.
/// </summary>
public sealed class GraphProperty
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Internal identifier used as the GLSL uniform base name, e.g. "FireSpeed".</summary>
    public string Name { get; set; } = "MyProperty";

    public PinType Type { get; set; } = PinType.Float;
    public string DisplayName { get; set; } = "My Property";
    public string DefaultValue { get; set; } = "1.0";

    /// <summary>ARGB hex color used for Vec3/Vec4 properties.</summary>
    public string ColorHex { get; set; } = "#FFFFFFFF";
    public float ColorIntensity { get; set; } = 1f;

    /// <summary>Disk path of the texture file for Texture2D properties.</summary>
    public string TexturePath { get; set; } = string.Empty;

    /// <summary>Import/runtime settings for the bound texture.</summary>
    public TextureImportSettings TextureSettings { get; set; } = new();

    /// <summary>Whether this property is visible to GrokoEngine at runtime.</summary>
    public bool Exposed { get; set; } = true;
    public string Tooltip { get; set; } = string.Empty;

    /// <summary>GLSL precision hint shown in the Graph Inspector (Inherit/Single/Half).</summary>
    public PropertyPrecision Precision { get; set; } = PropertyPrecision.Inherit;

    /// <summary>Where this property's value comes from at runtime (Per Material/Hybrid/Global).</summary>
    public PropertyScope Scope { get; set; } = PropertyScope.PerMaterial;

    /// <summary>When true, the property is exposed for inspection but cannot be edited on material instances.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Editing mode for Color-typed properties (Default/HDR).</summary>
    public PropertyColorMode ColorMode { get; set; } = PropertyColorMode.Default;

    /// <summary>Extra name/value metadata pairs, e.g. for custom material editor hooks.</summary>
    public List<PropertyAttribute> CustomAttributes { get; set; } = new();

    /// <summary>Ready-to-use GLSL uniform name, e.g. "u_FireSpeed".</summary>
    public string UniformName =>
        Name.StartsWith("u_", StringComparison.OrdinalIgnoreCase) ? Name : "u_" + Name;

    public void Normalize()
    {
        Name ??= "MyProperty";
        DisplayName ??= Name;
        DefaultValue ??= "1.0";
        ColorHex ??= "#FFFFFFFF";
        TexturePath ??= string.Empty;
        TextureSettings ??= new TextureImportSettings();
        TextureSettings.Normalize();
        Tooltip ??= string.Empty;
        CustomAttributes ??= new List<PropertyAttribute>();

        foreach (var attribute in CustomAttributes)
            attribute.Normalize();
    }
}

/// <summary>A single name/value pair shown under a property's "Custom Attributes" list.</summary>
public sealed class PropertyAttribute
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public void Normalize()
    {
        Name ??= string.Empty;
        Value ??= string.Empty;
    }
}
