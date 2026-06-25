using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GrokoEngine.Hub;

/// <summary>Crea la estructura mínima de un proyecto GrokoEngine en disco.</summary>
public static class ProjectFactory
{
    /// <summary>
    /// Crea &lt;parentDir&gt;\&lt;name&gt;\Assets\Scenes\Main.gscene (escena vacía válida) y devuelve la
    /// ruta raíz del proyecto. Lanza si el nombre es inválido o la carpeta ya tiene contenido.
    /// </summary>
    public static string CreateProject(string parentDir, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre del proyecto no puede estar vacío.");
        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("El nombre del proyecto contiene caracteres inválidos.");
        if (string.IsNullOrWhiteSpace(parentDir))
            throw new ArgumentException("Debes indicar una ubicación.");

        Directory.CreateDirectory(parentDir);

        string projectPath = Path.Combine(parentDir, name);
        if (Directory.Exists(projectPath) && Directory.EnumerateFileSystemEntries(projectPath).Any())
            throw new IOException($"Ya existe una carpeta con contenido en:\n{projectPath}");

        // Estructura tipo Unity dentro de Assets: Scenes / Scripts / Prefabs.
        string assetsDir = Path.Combine(projectPath, "Assets");
        string scenesDir = Path.Combine(assetsDir, "Scenes");
        Directory.CreateDirectory(scenesDir);
        Directory.CreateDirectory(Path.Combine(assetsDir, "Scripts"));
        Directory.CreateDirectory(Path.Combine(assetsDir, "Prefabs"));

        // Escena vacía válida usando el serializador del motor (GrokoEngine.Core).
        // El GrokoScripts.csproj y bin/obj los genera luego el ScriptCompiler del editor;
        // quedan ocultos en el panel Project (ver ShouldShowProjectPath en el editor).
        string scenePath = Path.Combine(scenesDir, "Main.gscene");
        SceneSerializer.Save(scenePath, CreateDefaultSceneObjects());

        return projectPath;
    }

    // Objetos por defecto de una escena nueva (estilo Unity): Main Camera + Directional Light.
    private static List<GameObject> CreateDefaultSceneObjects()
    {
        var mainCamera = new GameObject { Name = "Main Camera", IsCamera = true, PosY = 1f, PosZ = -10f };
        mainCamera.AddComponent<Camera>();

        var sun = new GameObject { Name = "Directional Light", PosY = 3f, RotX = 50f, RotY = -30f };
        sun.AddComponent<DirectionalLight>();

        return new List<GameObject> { mainCamera, sun };
    }
}
