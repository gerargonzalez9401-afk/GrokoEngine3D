using System.Text.Json;

namespace GrokoEngine;

/// <summary>
/// Stores the user's explicit decision to execute project-owned C# code.
/// Script assemblies run inside the editor process, so an untrusted project is
/// never compiled or loaded automatically.
/// </summary>
internal sealed class ProjectScriptTrust
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int Version { get; set; } = CurrentVersion;
    public bool Trusted { get; set; }

    public static ProjectScriptTrust Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new ProjectScriptTrust();

            var trust = JsonSerializer.Deserialize<ProjectScriptTrust>(File.ReadAllText(path));
            return trust is { Version: CurrentVersion } ? trust : new ProjectScriptTrust();
        }
        catch
        {
            // A malformed trust file must fail closed, never grant execution.
            return new ProjectScriptTrust();
        }
    }

    public void Save(string path) => AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
}
