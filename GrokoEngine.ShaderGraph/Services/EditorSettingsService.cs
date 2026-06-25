using System.IO;
using System.Text.Json;

namespace GrokoShaderGraphPro.Services;

public sealed class EditorSettingsModel
{
    public List<string> RecentFiles { get; set; } = [];
    public string LastGraphPath { get; set; } = string.Empty;
    public string Theme { get; set; } = "Dark";
    public double DefaultZoom { get; set; } = 1.0;
    public double GridSize { get; set; } = 24.0;
    public string PreviewShape { get; set; } = "Sphere";
    public string PreviewMode { get; set; } = "Lit";
}

public static class EditorSettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GrokoShaderGraphPro");
    public static string SettingsPath => Path.Combine(SettingsDirectory, "editor.settings.json");

    public static EditorSettingsModel Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new EditorSettingsModel();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<EditorSettingsModel>(json, Options) ?? new EditorSettingsModel();
        }
        catch
        {
            return new EditorSettingsModel();
        }
    }

    public static void Save(EditorSettingsModel settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
    }

    public static void AddRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var settings = Load();
        settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        settings.RecentFiles.Insert(0, path);
        settings.RecentFiles = settings.RecentFiles.Where(File.Exists).Take(10).ToList();
        settings.LastGraphPath = path;
        Save(settings);
    }
}
