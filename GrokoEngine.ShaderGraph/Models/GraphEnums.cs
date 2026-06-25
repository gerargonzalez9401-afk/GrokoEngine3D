namespace GrokoShaderGraphPro.Models;

/// <summary>All node types available in the shader graph.</summary>
public enum NodeKind
{
    // ── Constants ────────────────────────────────────────────────
    ConstantFloat,
    ConstantColor,
    ConstantVector2,
    ConstantVector3,

    // ── Engine Inputs ────────────────────────────────────────────
    Time,
    TextureCoord,
    NormalVector,
    TangentVector,
    ViewDirection,
    CameraVector,
    WorldPosition,
    ObjectPosition,
    ScreenPosition,

    // ── Blackboard Properties ────────────────────────────────────
    PropertyFloat,
    PropertyColor,
    PropertyVector2,
    PropertyVector3,
    PropertyVector4,
    PropertyTexture2D,

    // ── Texture ──────────────────────────────────────────────────
    TextureSample,
    Flipbook,
    Triplanar,

    // ── Math ─────────────────────────────────────────────────────
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Sin,
    Cos,
    Clamp,
    Smoothstep,
    Remap,
    OneMinus,
    Posterize,
    Length,
    Saturate,
    Step,
    Abs,
    Floor,
    Ceil,
    Fraction,
    Min,
    Max,
    Lerp,
    Negate,
    Reciprocal,
    MultiplyAdd,

    // ── Vector ───────────────────────────────────────────────────
    Normalize,
    Dot,
    Cross,

    // ── Procedural / UV ──────────────────────────────────────────
    Noise,
    GradientNoise,
    Checkerboard,
    Voronoi,
    SceneDepth,
    DepthFade,
    Fresnel,
    FresnelPro,
    RimLight,
    EmissionPulse,
    Dissolve,
    Gradient,
    Twirl,
    PolarCoordinates,
    RadialGradient,
    Distance,
    SphereMask,
    UVScroll,
    Panner,
    TilingOffset,
    Rotator,
    ParallaxOffset,
    Mix,

    // ── Color ────────────────────────────────────────────────────
    NormalMap,
    NormalBlend,
    NormalStrength,
    MetallicMap,
    SmoothnessMap,
    AmbientOcclusionMap,
    ChannelSplit,
    ChannelCombine,
    ChannelMask,
    Split,
    Combine,
    Swizzle,
    Append,
    InvertColor,
    BrightnessContrast,
    ColorRamp,
    Blend,

    // ── Graph Structure ──────────────────────────────────────────
    SubGraph,

    // ── Final Output ─────────────────────────────────────────────
    Output
}

/// <summary>Whether a pin is an input or output connection point.</summary>
public enum PinDirection
{
    Input,
    Output
}

/// <summary>Data type carried by a pin.</summary>
public enum PinType
{
    Float,
    Vec2,
    Vec3,
    Vec4,
    Texture2D
}

/// <summary>GLSL precision hint for an exposed property, mirroring Unity's Graph Inspector.</summary>
public enum PropertyPrecision
{
    Inherit,
    Single,
    Half
}

/// <summary>Where an exposed property's value comes from at runtime, mirroring Unity's Graph Inspector.</summary>
public enum PropertyScope
{
    PerMaterial,
    HybridPerInstance,
    Global
}

/// <summary>Editing mode for Color-typed properties, mirroring Unity's Graph Inspector.</summary>
public enum PropertyColorMode
{
    Default,
    Hdr
}
