using System;
using System.Diagnostics;
using System.IO;

namespace GrokoEngine.Hub;

/// <summary>Localiza y lanza el editor (GrokoEngine.ImGuiEditor.exe) sobre un proyecto.</summary>
public static class EditorLauncher
{
    private const string EditorExeName = "GrokoEngine.ImGuiEditor.exe";
    private const string Tfm = "net10.0-windows10.0.17763.0";

    /// <summary>Devuelve la ruta al exe del editor, o null si no se encuentra.</summary>
    public static string? ResolveEditorExe()
    {
        string baseDir = AppContext.BaseDirectory;

        // Candidatos: junto al Hub, y relativo a la estructura de la solución.
        // baseDir = ...\GrokoEngine.Hub\bin\<cfg>\<tfm>\  → subir 4 carpetas = repo raíz.
        string[] candidates =
        {
            Path.Combine(baseDir, EditorExeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "GrokoEngine.ImGuiEditor", "bin", "Debug", Tfm, EditorExeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "GrokoEngine.ImGuiEditor", "bin", "Release", Tfm, EditorExeName),
        };

        foreach (string c in candidates)
        {
            string full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>Lanza el editor sobre el proyecto indicado. Devuelve true si arrancó.</summary>
    public static bool Launch(string projectPath, out string error)
    {
        error = "";
        string? exe = ResolveEditorExe();
        if (exe == null)
        {
            error = $"No se encontró {EditorExeName}. Compila GrokoEngine.ImGuiEditor primero.";
            return false;
        }
        if (!Directory.Exists(projectPath))
        {
            error = $"La carpeta del proyecto no existe:\n{projectPath}";
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"\"{projectPath}\"",
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
