using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrokoShaderGraphPro.Models;

namespace GrokoShaderGraphPro.Services;

/// <summary>Serializes and deserializes <see cref="ShaderGraphModel"/> to/from JSON.</summary>
public static class GraphSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── Sync API (used by the WPF window) ────────────────────────

    public static void Save(string path, ShaderGraphModel graph)
    {
        graph.Normalize();
        ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);
        graph.ModifiedUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(graph, Options);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static ShaderGraphModel Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var graph = JsonSerializer.Deserialize<ShaderGraphModel>(json, Options)
            ?? throw new InvalidOperationException("No se pudo leer el Shader Graph.");
        graph.Normalize();
        ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);
        return graph;
    }

    // ── Async API (for future use / background saves) ────────────

    public static async Task SaveAsync(string path, ShaderGraphModel graph, CancellationToken ct = default)
    {
        graph.Normalize();
        ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);
        graph.ModifiedUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(graph, Options);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct).ConfigureAwait(false);
    }

    public static async Task<ShaderGraphModel> LoadAsync(string path, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        var graph = JsonSerializer.Deserialize<ShaderGraphModel>(json, Options)
            ?? throw new InvalidOperationException("No se pudo leer el Shader Graph.");
        graph.Normalize();
        ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);
        return graph;
    }
}
