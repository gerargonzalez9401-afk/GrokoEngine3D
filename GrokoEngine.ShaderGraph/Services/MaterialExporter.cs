using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

/// <summary>Exports a material pack (GLSL, HLSL, manifests, preview PNG) to a folder.</summary>
public static class MaterialExporter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly HashSet<NodeKind> TextureNodeKinds =
        [NodeKind.TextureSample, NodeKind.NormalMap, NodeKind.Triplanar, NodeKind.MetallicMap, NodeKind.SmoothnessMap, NodeKind.AmbientOcclusionMap, NodeKind.PropertyTexture2D];

    private static readonly HashSet<NodeKind> PropertyNodeKinds =
        [NodeKind.PropertyFloat, NodeKind.PropertyColor,
         NodeKind.PropertyVector2, NodeKind.PropertyVector3, NodeKind.PropertyVector4, NodeKind.PropertyTexture2D];

    public static void Export(
        string folderPath,
        ShaderGraphModel graph,
        string glsl,
        string hlsl,
        BitmapSource? preview = null,
        string? vertexGlsl = null,
        string? vertexHlsl = null)
    {
        graph.Normalize();
        ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);
        Directory.CreateDirectory(folderPath);

        var safeName = MakeSafeName(graph.Name);
        var paths = BuildPaths(folderPath, safeName);

        File.WriteAllText(paths.VertexGlsl, vertexGlsl ?? DefaultVertexShader(), Encoding.UTF8);
        File.WriteAllText(paths.FragmentGlsl, glsl, Encoding.UTF8);
        File.WriteAllText(paths.PixelHlsl, hlsl, Encoding.UTF8);
        File.WriteAllText(paths.VertexHlsl, vertexHlsl ?? DefaultVertexHlsl(), Encoding.UTF8);

        GraphSerializer.Save(paths.Graph, graph);
        if (preview is not null)
            SavePreviewPng(paths.Preview, preview);

        File.WriteAllText(paths.Material, JsonSerializer.Serialize(BuildManifest(graph, paths, preview is not null), JsonOpts), Encoding.UTF8);
        File.WriteAllText(paths.Dependencies, JsonSerializer.Serialize(BuildDependencies(graph, paths), JsonOpts), Encoding.UTF8);
        File.WriteAllText(paths.Readme, BuildReadme(graph, paths), Encoding.UTF8);
    }

    public static void Export(string folderPath, ShaderGraphModel graph, string glsl)
        => Export(folderPath, graph, glsl, "// HLSL not generated.");

    private record ExportPaths(
        string VertexGlsl,
        string FragmentGlsl,
        string VertexHlsl,
        string PixelHlsl,
        string Material,
        string Dependencies,
        string Graph,
        string Readme,
        string Preview);

    private static ExportPaths BuildPaths(string folder, string safeName) => new(
        VertexGlsl: Path.Combine(folder, safeName + ".vert.glsl"),
        FragmentGlsl: Path.Combine(folder, safeName + ".frag.glsl"),
        VertexHlsl: Path.Combine(folder, safeName + ".vertex.hlsl"),
        PixelHlsl: Path.Combine(folder, safeName + ".pixel.hlsl"),
        Material: Path.Combine(folder, safeName + ".gmaterial.json"),
        Dependencies: Path.Combine(folder, safeName + ".dependencies.json"),
        Graph: Path.Combine(folder, safeName + ".graph.json"),
        Readme: Path.Combine(folder, safeName + "_README.txt"),
        Preview: Path.Combine(folder, safeName + ".preview.png"));

    private static object BuildManifest(ShaderGraphModel graph, ExportPaths paths, bool hasPreview)
    {
        var textureNodes = graph.Nodes
            .Where(n => TextureNodeKinds.Contains(n.Kind) && (!string.IsNullOrWhiteSpace(n.TextValue) || !string.IsNullOrWhiteSpace(n.TexturePath)))
            .Select(n => new
            {
                node = n.Title,
                uniform = MakeTextureUniformName(n),
                texturePath = n.TexturePath,
                value = n.TextValue,
                kind = n.Kind.ToString(),
                textureSettings = new
                {
                    sRgb = n.TextureSettings.SRgb,
                    isNormalMap = n.TextureSettings.IsNormalMap || n.Kind == NodeKind.NormalMap,
                    generateMipMaps = n.TextureSettings.GenerateMipMaps,
                    wrapMode = n.TextureSettings.WrapMode,
                    filterMode = n.TextureSettings.FilterMode,
                    anisotropy = n.TextureSettings.Anisotropy,
                    defaultTexturePolicy = n.TextureSettings.DefaultTexturePolicy
                }
            })
            .ToArray();

        var properties = graph.Properties
            .Select(p => new
            {
                name = p.Name,
                displayName = p.DisplayName,
                uniform = p.UniformName,
                type = p.Type.ToString(),
                defaultValue = p.DefaultValue,
                color = p.ColorHex,
                intensity = p.ColorIntensity,
                texturePath = p.TexturePath,
                exposed = p.Exposed,
                tooltip = p.Tooltip,
                textureSettings = new
                {
                    sRgb = p.TextureSettings.SRgb,
                    isNormalMap = p.TextureSettings.IsNormalMap,
                    generateMipMaps = p.TextureSettings.GenerateMipMaps,
                    wrapMode = p.TextureSettings.WrapMode,
                    filterMode = p.TextureSettings.FilterMode,
                    anisotropy = p.TextureSettings.Anisotropy,
                    defaultTexturePolicy = p.TextureSettings.DefaultTexturePolicy
                }
            })
            .Concat(graph.Nodes
                .Where(n => PropertyNodeKinds.Contains(n.Kind))
                .Select(n => new
                {
                    name = string.IsNullOrWhiteSpace(n.TextValue) ? n.Title : n.TextValue,
                    displayName = n.Title,
                    uniform = MakeTextureUniformName(string.IsNullOrWhiteSpace(n.TextValue) ? n.Title : n.TextValue),
                    type = n.Kind.ToString(),
                    defaultValue = $"{n.FloatValue},{n.FloatValue2},{n.FloatValue3}",
                    color = n.ColorHex,
                    intensity = n.ColorIntensity,
                    texturePath = n.TexturePath,
                    exposed = true,
                    tooltip = n.Comment,
                    textureSettings = new
                    {
                        sRgb = n.TextureSettings.SRgb,
                        isNormalMap = n.TextureSettings.IsNormalMap || n.Kind == NodeKind.NormalMap,
                        generateMipMaps = n.TextureSettings.GenerateMipMaps,
                        wrapMode = n.TextureSettings.WrapMode,
                        filterMode = n.TextureSettings.FilterMode,
                        anisotropy = n.TextureSettings.Anisotropy,
                        defaultTexturePolicy = n.TextureSettings.DefaultTexturePolicy
                    }
                }))
            .ToArray();

        return new
        {
            materialVersion = "2.1",
            name = graph.Name,
            surface = graph.Surface,
            blendMode = graph.BlendMode,
            cullMode = graph.CullMode,
            depthWrite = graph.DepthWrite,
            depthTest = graph.DepthTest,
            doubleSided = graph.DoubleSided,
            zTest = graph.ZTest,
            renderQueue = graph.RenderQueue,
            shaders = new
            {
                glslVertex = Path.GetFileName(paths.VertexGlsl),
                glslFragment = Path.GetFileName(paths.FragmentGlsl),
                hlslVertex = Path.GetFileName(paths.VertexHlsl),
                hlslPixel = Path.GetFileName(paths.PixelHlsl)
            },
            graph = Path.GetFileName(paths.Graph),
            preview = hasPreview ? Path.GetFileName(paths.Preview) : null,
            dependencies = Path.GetFileName(paths.Dependencies),
            textures = textureNodes,
            properties = properties,
            subGraphs = graph.SubGraphs.Select(s => new { s.Name, s.Description, nodes = s.Nodes.Count, connections = s.Connections.Count }).ToArray(),
            engineUniforms = new[]
            {
                new { name = "u_Time", type = "float", source = "engine_time" },
                new { name = "u_Resolution", type = "vec2", source = "viewport_resolution" },
                new { name = "u_CameraPos", type = "vec3", source = "camera_position" },
                new { name = "u_LightDir", type = "vec3", source = "main_directional_light" }
            },
            renderState = new
            {
                surface = graph.Surface,
                blend = graph.Surface.Equals("Transparent", StringComparison.OrdinalIgnoreCase),
                blendMode = graph.BlendMode,
                depthTest = graph.DepthTest,
                depthWrite = graph.DepthWrite,
                zTest = graph.ZTest,
                cull = graph.CullMode,
                doubleSided = graph.DoubleSided,
                renderQueue = graph.RenderQueue
            },
            stats = new
            {
                nodes = graph.Nodes.Count,
                connections = graph.Connections.Count,
                textureBindings = textureNodes.Length,
                subGraphs = graph.SubGraphs.Count
            },
            exportedUtc = DateTime.UtcNow
        };
    }

    private static object BuildDependencies(ShaderGraphModel graph, ExportPaths paths)
    {
        var nodeTextures = graph.Nodes
            .Where(n => TextureNodeKinds.Contains(n.Kind))
            .Select(n => string.IsNullOrWhiteSpace(n.TexturePath) ? n.TextValue : n.TexturePath)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var propertyTextures = graph.Properties
            .Where(p => p.Type == PinType.Texture2D && !string.IsNullOrWhiteSpace(p.TexturePath))
            .Select(p => p.TexturePath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return new
        {
            graph = Path.GetFileName(paths.Graph),
            shaders = new[]
            {
                Path.GetFileName(paths.VertexGlsl),
                Path.GetFileName(paths.FragmentGlsl),
                Path.GetFileName(paths.VertexHlsl),
                Path.GetFileName(paths.PixelHlsl)
            },
            material = Path.GetFileName(paths.Material),
            preview = File.Exists(paths.Preview) ? Path.GetFileName(paths.Preview) : null,
            textures = nodeTextures.Concat(propertyTextures).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            textureSettings = graph.Nodes
                .Where(n => TextureNodeKinds.Contains(n.Kind) && (!string.IsNullOrWhiteSpace(n.TextValue) || !string.IsNullOrWhiteSpace(n.TexturePath)))
                .Select(n => new
                {
                    binding = MakeTextureUniformName(n),
                    path = string.IsNullOrWhiteSpace(n.TexturePath) ? n.TextValue : n.TexturePath,
                    sRgb = n.TextureSettings.SRgb,
                    isNormalMap = n.TextureSettings.IsNormalMap || n.Kind == NodeKind.NormalMap,
                    generateMipMaps = n.TextureSettings.GenerateMipMaps,
                    wrapMode = n.TextureSettings.WrapMode,
                    filterMode = n.TextureSettings.FilterMode,
                    anisotropy = n.TextureSettings.Anisotropy
                }).ToArray(),
            subGraphs = graph.SubGraphs.Select(s => new { s.Name, s.Description }).DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            generatedUtc = DateTime.UtcNow
        };
    }

    private static string BuildReadme(ShaderGraphModel graph, ExportPaths paths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Groko Material Export");
        sb.AppendLine("=====================");
        sb.AppendLine($"Material      : {graph.Name}");
        sb.AppendLine($"Surface       : {graph.Surface}");
        sb.AppendLine($"Blend         : {graph.BlendMode}");
        sb.AppendLine($"Cull          : {graph.CullMode}");
        sb.AppendLine($"DepthWrite    : {graph.DepthWrite}");
        sb.AppendLine($"DepthTest     : {graph.DepthTest}");
        sb.AppendLine($"RenderQueue   : {graph.RenderQueue}");
        sb.AppendLine();
        sb.AppendLine("Files:");
        sb.AppendLine($"  {Path.GetFileName(paths.VertexGlsl)}");
        sb.AppendLine($"  {Path.GetFileName(paths.FragmentGlsl)}");
        sb.AppendLine($"  {Path.GetFileName(paths.VertexHlsl)}");
        sb.AppendLine($"  {Path.GetFileName(paths.PixelHlsl)}");
        sb.AppendLine($"  {Path.GetFileName(paths.Material)}");
        sb.AppendLine($"  {Path.GetFileName(paths.Dependencies)}");
        sb.AppendLine($"  {Path.GetFileName(paths.Graph)}");
        if (File.Exists(paths.Preview))
            sb.AppendLine($"  {Path.GetFileName(paths.Preview)}");
        sb.AppendLine();
        sb.AppendLine("Integration notes:");
        sb.AppendLine("- Load the .gmaterial.json in GrokoEngine to bind material properties.");
        sb.AppendLine("- Bind engine uniforms: u_Time, u_Resolution, u_CameraPos, u_LightDir.");
        sb.AppendLine("- Respect texture settings from *.dependencies.json (sRGB, normal-map, mipmaps).");
        sb.AppendLine("- Use the GLSL files for OpenGL runtime and the HLSL pair for a future DirectX backend.");
        return sb.ToString();
    }

    private static string DefaultVertexShader() => """
#version 330 core
layout(location = 0) in vec3 a_Position;
layout(location = 1) in vec3 a_Normal;
layout(location = 2) in vec2 a_UV;
uniform mat4 u_Model;
uniform mat4 u_View;
uniform mat4 u_Projection;
out vec2 v_UV;
out vec3 v_NormalWS;
out vec3 v_WorldPos;
out vec3 v_ObjectPos;
out vec3 v_TangentWS;
out vec4 v_ScreenPos;
void main()
{
    vec4 world = u_Model * vec4(a_Position, 1.0);
    v_WorldPos = world.xyz;
    v_ObjectPos = a_Position;
    v_NormalWS = normalize(mat3(u_Model) * a_Normal);
    vec3 tangentOS = abs(a_Normal.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : normalize(cross(vec3(0.0, 1.0, 0.0), a_Normal));
    v_TangentWS = normalize(mat3(u_Model) * tangentOS);
    v_UV = a_UV;
    gl_Position = u_Projection * u_View * world;
    v_ScreenPos = gl_Position;
}
""";

    private static string DefaultVertexHlsl() => """
cbuffer GrokoObject : register(b1)
{
    float4x4 u_Model;
    float4x4 u_View;
    float4x4 u_Projection;
};

struct VSInput
{
    float3 PositionOS : POSITION;
    float3 NormalOS : NORMAL;
    float2 UV : TEXCOORD0;
};

struct VSOutput
{
    float4 PositionCS : SV_POSITION;
    float2 v_UV : TEXCOORD0;
    float3 v_NormalWS : TEXCOORD1;
    float3 v_WorldPos : TEXCOORD2;
    float3 v_ObjectPos : TEXCOORD3;
    float3 v_TangentWS : TEXCOORD4;
    float4 v_ScreenPos : TEXCOORD5;
};

VSOutput VSMain(VSInput input)
{
    VSOutput o;
    float4 world = mul(float4(input.PositionOS, 1.0), u_Model);
    o.v_WorldPos = world.xyz;
    o.v_ObjectPos = input.PositionOS;
    o.v_NormalWS = normalize(mul(float4(input.NormalOS, 0.0), u_Model).xyz);
    float3 tangentOS = abs(input.NormalOS.y) > 0.99 ? float3(1.0, 0.0, 0.0) : normalize(cross(float3(0.0, 1.0, 0.0), input.NormalOS));
    o.v_TangentWS = normalize(mul(float4(tangentOS, 0.0), u_Model).xyz);
    o.v_UV = input.UV;
    o.PositionCS = mul(mul(world, u_View), u_Projection);
    o.v_ScreenPos = o.PositionCS;
    return o;
}
""";

    private static void SavePreviewPng(string path, BitmapSource preview)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(preview));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string MakeTextureUniformName(string value)
    {
        value = value.Trim();
        var fileName = Path.GetFileNameWithoutExtension(value);
        var raw = !string.IsNullOrWhiteSpace(fileName)
               && (value.Contains('\\') || value.Contains('/') || value.Contains('.'))
            ? "u_" + fileName
            : value;

        var chars = raw.Select((c, i) =>
            char.IsLetter(c) || c == '_' || (i > 0 && char.IsDigit(c)) ? c : '_').ToArray();

        var clean = new string(chars);
        return string.IsNullOrWhiteSpace(clean) ? "u_MainTex" : clean;
    }

    private static string MakeTextureUniformName(ShaderNode node)
    {
        var value = string.IsNullOrWhiteSpace(node.TextValue) ? string.Empty : node.TextValue.Trim();
        if (!string.IsNullOrWhiteSpace(node.TexturePath) && IsDefaultTextureUniformName(value))
            return LocalTextureUniformName(node);

        return MakeTextureUniformName(string.IsNullOrWhiteSpace(value) ? node.TexturePath : value);
    }

    private static bool IsDefaultTextureUniformName(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v))
            return true;

        return v.Equals("MainTex", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_MainTex", StringComparison.OrdinalIgnoreCase)
            || v.Equals("NormalMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_NormalMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("MetallicMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_MetallicMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("SmoothnessMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_SmoothnessMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("AOMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_AOMap", StringComparison.OrdinalIgnoreCase);
    }

    private static string LocalTextureUniformName(ShaderNode node)
    {
        var prefix = node.Kind switch
        {
            NodeKind.NormalMap => "u_NormalMap_",
            NodeKind.MetallicMap => "u_MetallicMap_",
            NodeKind.SmoothnessMap => "u_SmoothnessMap_",
            NodeKind.AmbientOcclusionMap => "u_AOMap_",
            NodeKind.Triplanar => "u_Triplanar_",
            _ => "u_Texture_"
        };

        return prefix + node.Id.ToString("N")[..8];
    }

    private static string MakeSafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "GrokoMaterial" : clean;
    }
}
