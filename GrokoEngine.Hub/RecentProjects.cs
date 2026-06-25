using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GrokoEngine.Hub;

/// <summary>Una entrada de proyecto en la lista de recientes del Hub.</summary>
public sealed class ProjectEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }

    /// <summary>True si la carpeta del proyecto todavía existe en disco.</summary>
    public bool Exists => Directory.Exists(Path);
}

/// <summary>
/// Lista persistente de proyectos recientes del Hub, guardada en
/// %AppData%\GrokoEngine\hub_projects.json.
/// </summary>
public static class RecentProjects
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string StorePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GrokoEngine");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "hub_projects.json");
        }
    }

    public static List<ProjectEntry> Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var list = JsonSerializer.Deserialize<List<ProjectEntry>>(File.ReadAllText(StorePath));
                if (list != null)
                    return list.OrderByDescending(p => p.LastOpened).ToList();
            }
        }
        catch { /* archivo corrupto: empezar limpio */ }
        return new List<ProjectEntry>();
    }

    public static void Save(List<ProjectEntry> projects)
    {
        try { File.WriteAllText(StorePath, JsonSerializer.Serialize(projects, JsonOptions)); }
        catch { /* sin permisos de escritura: ignorar */ }
    }

    /// <summary>Añade el proyecto o refresca su fecha si ya estaba. Persiste y devuelve la lista ordenada.</summary>
    public static List<ProjectEntry> AddOrTouch(List<ProjectEntry> projects, string projectPath)
    {
        string full = Path.GetFullPath(projectPath);
        var existing = projects.FirstOrDefault(p =>
            string.Equals(Path.GetFullPath(p.Path), full, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.LastOpened = DateTime.Now;
        else
            projects.Add(new ProjectEntry
            {
                Name = new DirectoryInfo(full).Name,
                Path = full,
                LastOpened = DateTime.Now
            });

        var ordered = projects.OrderByDescending(p => p.LastOpened).ToList();
        Save(ordered);
        return ordered;
    }

    public static List<ProjectEntry> Remove(List<ProjectEntry> projects, ProjectEntry entry)
    {
        projects.Remove(entry);
        Save(projects);
        return projects;
    }
}
