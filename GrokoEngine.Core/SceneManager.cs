using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GrokoEngine
{
    // Sistema minimo de carga de escenas en Play Mode, estilo Unity (SceneManager.LoadScene).
    // Solo tiene sentido dentro de Play Mode: no existe concepto de "escenas" en Edit Mode
    // (el editor siempre trabaja sobre una unica escena cargada). El intercambio real
    // (ProcessPendingLoad) vive en Core para ser testeable sin ImGui/OpenGL; el host (el editor)
    // solo debe llamarlo una vez por frame tras el Update normal de Play Mode.
    public static class SceneManager
    {
        // Escena actualmente activa (la fija el host al entrar en Play Mode / tras cada carga).
        public static string? CurrentScenePath { get; set; }

        // Carga pendiente solicitada por un script vía LoadScene. Se resuelve en ProcessPendingLoad.
        public static string? RequestedLoadPath { get; private set; }

        public static void LoadScene(string sceneNameOrPath)
        {
            if (!RuntimeScene.IsReady)
            {
                Debug.LogWarning("SceneManager.LoadScene solo funciona en Play Mode; llamada ignorada.");
                return;
            }
            RequestedLoadPath = sceneNameOrPath;
        }

        // Descarta una carga pendiente sin procesarla (p.ej. al salir de Play Mode antes de
        // que StepRuntime la resolviera).
        public static void CancelPendingLoad() => RequestedLoadPath = null;

        // Ejecuta el intercambio de escena si hay una carga pendiente. Devuelve true si se
        // realizo un intercambio. Los objetos marcados DontDestroyOnLoad (solo raiz) sobreviven,
        // el resto recibe OnDisable/OnDestroy antes de deserializar la nueva escena.
        public static bool ProcessPendingLoad(IList<GameObject> currentRoots, PhysicsEngine physics, ScriptCompiler scriptCompiler)
        {
            if (RequestedLoadPath == null)
                return false;

            string requested = RequestedLoadPath;
            RequestedLoadPath = null;

            string resolvedPath = ResolveScenePath(requested, CurrentScenePath);

            var survivors = new List<GameObject>();
            var toDestroy = new List<GameObject>();
            foreach (var root in currentRoots)
            {
                if (root.DontDestroyOnLoad)
                    survivors.Add(root);
                else
                    toDestroy.Add(root);
            }

            DestroyRoots(toDestroy);

            // Deserializar ANTES de tocar currentRoots: si falla (escena corrupta/no encontrada),
            // la escena actual queda intacta (misma propiedad de "clear despues de exito" que
            // usa RestorePlayModeSnapshot en el editor).
            var newSceneRoots = SceneSerializer.Load(resolvedPath, physics, scriptCompiler);

            currentRoots.Clear();
            foreach (var survivor in survivors)
                currentRoots.Add(survivor);
            foreach (var created in newSceneRoots)
                currentRoots.Add(created);

            physics.SyncPhysicsComponents(currentRoots);

            AwakeRoots(newSceneRoots);
            StartRoots(newSceneRoots);

            CurrentScenePath = resolvedPath;
            return true;
        }

        private static void AwakeRoots(IEnumerable<GameObject> roots)
        {
            foreach (var obj in roots)
            {
                foreach (var component in obj.Components.ToList())
                {
                    component.InternalAwake();
                    if (obj.IsActive && component.Enabled)
                        component.InternalEnable();
                }
                AwakeRoots(obj.Children);
            }
        }

        private static void StartRoots(IEnumerable<GameObject> roots)
        {
            foreach (var obj in roots)
            {
                foreach (var component in obj.Components.ToList())
                    component.InternalStart();
                StartRoots(obj.Children);
            }
        }

        private static void DestroyRoots(IEnumerable<GameObject> roots)
        {
            foreach (var obj in roots)
            {
                DestroyRoots(obj.Children);
                foreach (var component in obj.Components.ToList())
                {
                    if (!component.HasAwaken)
                        continue;
                    try
                    {
                        component.InternalDisable();
                        component.OnDestroy();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Excepcion en OnDestroy de '{obj.Name}': {ex.Message}");
                    }
                }
            }
        }

        // Testeable por separado: acepta una ruta directa, o un nombre/ruta relativa que se
        // busca dentro de Assets/**.gscene (el mismo Assets de la escena actualmente cargada).
        internal static string ResolveScenePath(string sceneNameOrPath, string? currentScenePath)
        {
            if (File.Exists(sceneNameOrPath))
                return sceneNameOrPath;

            string? assetsRoot = currentScenePath != null ? FindAssetsRoot(currentScenePath) : null;
            if (assetsRoot != null)
            {
                string wantedName = Path.GetFileNameWithoutExtension(sceneNameOrPath);
                string? match = Directory.EnumerateFiles(assetsRoot, "*.gscene", SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), wantedName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            throw new FileNotFoundException($"Scene not found: {sceneNameOrPath}");
        }

        private static string? FindAssetsRoot(string scenePath)
        {
            try
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(scenePath)) ?? ".");
                while (dir != null)
                {
                    if (string.Equals(dir.Name, "Assets", StringComparison.OrdinalIgnoreCase))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }
    }
}
