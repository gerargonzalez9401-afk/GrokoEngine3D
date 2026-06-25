using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GrokoEngine.ImGuiEditor;

// Configuración de arranque de un juego exportado. Se guarda como game.json junto al .exe.
// Si Program.Main encuentra un game.json al lado del ejecutable, arranca en "Game Mode"
// (juego a pantalla, sin UI de editor) en vez de abrir el editor.
internal sealed class GameLaunchConfig
{
    public string StartupScene { get; set; } = "Assets/Scenes/Main.gscene";
    public string Title { get; set; } = "Groko Game";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; } = false;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public const string FileName = "game.json";

    // Devuelve la config si existe un game.json junto al ejecutable; null si no (= modo editor).
    public static GameLaunchConfig? TryLoadBesideExecutable()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path))
                return null;
            var cfg = JsonSerializer.Deserialize<GameLaunchConfig>(File.ReadAllText(path));
            cfg?.Normalize();
            return cfg;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudo leer game.json: " + ex.Message);
            return null;
        }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(StartupScene)) StartupScene = "Assets/Scenes/Main.gscene";
        if (string.IsNullOrWhiteSpace(Title)) Title = "Groko Game";
        if (Width < 320) Width = 1280;
        if (Height < 240) Height = 720;
    }
}

internal sealed partial class ImGuiEditorApp
{
    // Diálogo de carpeta para elegir dónde exportar el juego (mismo patrón que BrowseForHdri).
    private static string? BrowseForExportFolder()
    {
        try
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Carpeta de destino para el juego exportado",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudo abrir el diálogo de carpeta: " + ex.Message);
            return null;
        }
    }

    // Empaqueta un juego ejecutable: copia los binarios del editor en ejecución + los Assets
    // del proyecto + un game.json. El mismo .exe, al encontrar game.json, arranca como juego.
    private void ExportGame(string outputDir)
    {
        try
        {
            outputDir = Path.GetFullPath(outputDir);
            string baseDir = Path.GetFullPath(AppContext.BaseDirectory);

            if (string.Equals(outputDir.TrimEnd('\\', '/'), baseDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                statusMessage = "Export cancelado: elige una carpeta distinta a la del editor.";
                return;
            }

            statusMessage = "Exportando juego… copiando binarios";
            Directory.CreateDirectory(outputDir);

            // Asegura que los ajustes de render actuales (HDRI/skybox, IBL, color space,
            // sombras, exposición) estén volcados a disco antes de copiarlos al juego.
            SaveEditorSettings();

            // 1) Binarios del editor en ejecución (DLLs, .exe, assimp.dll, Mimotor.Math.dll).
            // Se excluyen ficheros solo-editor; se evita descender en la propia carpeta de salida.
            var skipTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".last_project", ".groko-imgui-layout.ini", GameLaunchConfig.FileName
            };
            CopyDirectory(baseDir, outputDir, skipTop, outputDir);

            // 2) Assets del proyecto → outputDir/Assets
            statusMessage = "Exportando juego… copiando Assets";
            string assetsOut = Path.Combine(outputDir, "Assets");
            CopyDirectory(rootAssetsPath, assetsOut, null, outputDir);

            // 2.5) Ajustes de render del editor (HDRI/skybox, IBL, color space, sombras).
            // El juego (Game Mode) los lee desde <carpeta>/.groko-editor-settings.json,
            // si no, arrancaría sin skybox ni iluminación de entorno.
            if (File.Exists(editorSettingsPath))
                File.Copy(editorSettingsPath, Path.Combine(outputDir, ".groko-editor-settings.json"), overwrite: true);

            // 2.6) HDRI: si el path es absoluto y el fichero existe, copiarlo a la carpeta del
            // juego y reescribir la ruta en el settings copiado, para que sea portable y no
            // dependa de la ruta original. (Si ya está bajo Assets, igualmente se copió arriba.)
            BundleHdriIntoExport(outputDir);

            // 3) game.json (escena actual como escena de inicio)
            string relScene = Path.GetRelativePath(projectPath, scenePath).Replace('\\', '/');
            var cfg = new GameLaunchConfig
            {
                StartupScene = relScene,
                Title = Path.GetFileName(projectPath),
                Width = 1280,
                Height = 720,
                Fullscreen = false
            };
            cfg.Save(Path.Combine(outputDir, GameLaunchConfig.FileName));

            statusMessage = "Juego exportado en: " + outputDir;
            GrokoEngine.Debug.Log("Juego exportado en: " + outputDir);
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("Falló la exportación: " + ex.Message);
            statusMessage = "Falló la exportación: " + ex.Message;
        }
    }

    // Copia el HDRI usado por la escena a la carpeta del juego y deja su ruta como RELATIVA
    // (portable) en el settings copiado. El Game Mode la resuelve contra la carpeta del juego.
    private void BundleHdriIntoExport(string outputDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hdriPath) || !File.Exists(hdriPath))
                return;

            string envDir = Path.Combine(outputDir, "Assets", "_Environment");
            Directory.CreateDirectory(envDir);
            string fileName = Path.GetFileName(hdriPath);
            File.Copy(hdriPath, Path.Combine(envDir, fileName), overwrite: true);

            string settingsCopy = Path.Combine(outputDir, ".groko-editor-settings.json");
            if (File.Exists(settingsCopy))
            {
                var s = JsonSerializer.Deserialize<EditorSettingsData>(File.ReadAllText(settingsCopy));
                if (s != null)
                {
                    s.HdriPath = "Assets/_Environment/" + fileName;
                    File.WriteAllText(settingsCopy, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudo empaquetar el HDRI: " + ex.Message);
        }
    }

    // Copia recursiva. skipTopNames solo aplica al nivel raíz; skipFullPath nunca se desciende
    // (evita copiarse a sí misma si la salida está dentro del origen).
    private static void CopyDirectory(string source, string dest, HashSet<string>? skipTopNames, string? skipFullPath)
    {
        Directory.CreateDirectory(dest);
        var src = new DirectoryInfo(source);

        foreach (var file in src.GetFiles())
        {
            if (skipTopNames != null && skipTopNames.Contains(file.Name))
                continue;
            file.CopyTo(Path.Combine(dest, file.Name), overwrite: true);
        }

        foreach (var dir in src.GetDirectories())
        {
            if (skipFullPath != null &&
                string.Equals(dir.FullName.TrimEnd('\\', '/'), skipFullPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectory(dir.FullName, Path.Combine(dest, dir.Name), null, skipFullPath);
        }
    }
}
