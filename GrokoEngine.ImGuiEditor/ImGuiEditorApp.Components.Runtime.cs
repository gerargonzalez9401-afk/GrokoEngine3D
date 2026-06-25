using GrokoEngine;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using Vec4 = System.Numerics.Vector4;
using ShaderGraphModel = GrokoShaderGraphPro.Models.ShaderGraphModel;
using ShaderGraphTemplates = GrokoShaderGraphPro.Services.GraphTemplates;
using ShaderCodeGenerator = GrokoShaderGraphPro.Services.ShaderCodeGenerator;
using ShaderGraphValidator = GrokoShaderGraphPro.Services.GraphValidator;
using GraphPin = GrokoShaderGraphPro.Models.GraphPin;
using GraphConnection = GrokoShaderGraphPro.Models.GraphConnection;
using NodeKind = GrokoShaderGraphPro.Models.NodeKind;
using PinType = GrokoShaderGraphPro.Models.PinType;
using PinDirection = GrokoShaderGraphPro.Models.PinDirection;
using GraphProperty = GrokoShaderGraphPro.Models.GraphProperty;
using PropertyAttribute = GrokoShaderGraphPro.Models.PropertyAttribute;
using PropertyColorMode = GrokoShaderGraphPro.Models.PropertyColorMode;
using GlfwKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
private void TogglePlayMode()
    {
        if (isPlaying)
            ExitPlayMode();
        else
            QueueEditorProgressTask("Entering Play Mode", "Preparing runtime", EnterPlayMode);
    }

private void EnterPlayMode()
    {
        if (isPlaying) return;

        playModeSnapshot = SceneSerializer.Serialize(objects);
        playModeSelectionIds = selection.CaptureSelectedIds();
        ShowEditorProgress("Entering Play Mode", "Compiling scripts", 0.16f, running: true);
        var result = scriptCompiler.Compile();
        if (!result.Success)
        {
            CompleteEditorProgress("Entering Play Mode", "Script compile failed");
            statusMessage = "Play blocked: script compile failed";
            if (!string.IsNullOrWhiteSpace(result.ErrorLog))
                statusMessage = result.ErrorLog;
            return;
        }

        UpdateEditorProgress("Initializing runtime scene", 0.62f);
        ResetScriptStartState(objects);
        BepuBackend.Reset(); // si el backend Bepu está activo, se reconstruye fresco para esta sesión de Play
        RuntimeScene.SetContext(objects, physicsEngine, scriptCompiler);
        // Awake corre AHORA (en Play, con RuntimeScene listo), no al cargar la escena. Así los
        // Awake que usan Instantiate/Destroy/Physics.Raycast funcionan, y los singletons fijan su
        // Instance al entrar en Play (no al cargar en el editor).
        if (!ExecuteAwake(objects))
        {
            CompleteEditorProgress("Entering Play Mode", "Awake failed");
            RestorePlayModeSnapshot("Play blocked: " + statusMessage);
            return;
        }
        if (!ExecuteStart(objects))
        {
            CompleteEditorProgress("Entering Play Mode", "Start failed");
            RestorePlayModeSnapshot("Play blocked: " + statusMessage);
            return;
        }

        isPlaying = true;
        playPaused = false;
        simulatePhysics = false;
        previousRightMouseDown = false;
        previousLeftMouseDown = false;
        // Durante el juego pedimos al GC que difiera las recolecciones gen2 (las que más pausan):
        // reduce los "tirones". Se restaura a Interactive al salir de Play.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        GrokoEngine.Time.Reset();   // reinicia el tiempo global al arrancar el Play
        CompleteEditorProgress("Entering Play Mode", "Play mode ready");
        statusMessage = "Play mode";
    }

private void ExitPlayMode()
    {
        if (!isPlaying) return;

        RestorePlayModeSnapshot("Stopped: scene restored");
    }

private void StepRuntime(double deltaTime)
    {
        long runtimeStart = EditorProfiler.Timestamp();
        try
        {
            long contextStart = EditorProfiler.Timestamp();
            RuntimeScene.SetContext(objects, physicsEngine, scriptCompiler);
            profiler.SampleRuntimeContext(EditorProfiler.ElapsedMs(contextStart));

            // La fachada PhysicsEngine garantiza BEPU antes de ExecuteUpdate.
            // El CharacterController llama a sweeps durante Update(); si la simulación
            // no existe, podría moverse libre y caer atravesando el piso.
            physicsEngine.EnsureSimulationBuilt(objects);

            // Avanza el tiempo global (escalado/no escalado) para Animator y scripts.
            GrokoEngine.Time.Advance(deltaTime);

            long scriptsStart = EditorProfiler.Timestamp();
            ExecuteUpdate(objects, (float)deltaTime);
            profiler.SampleScriptUpdate(EditorProfiler.ElapsedMs(scriptsStart));

            // Los Animator son componentes del motor (no scripts de usuario), así que
            // ExecuteUpdate no los toca: se actualizan en su propio pase, tras los scripts
            // (para que SetBool/SetTrigger del frame ya estén aplicados al evaluar transiciones).
            // Los de modo AnimatePhysics se saltan aquí y se actualizan junto a las físicas.
            StepAnimators(objects, deltaTime);

            // Animator en modo AnimatePhysics: se actualizan justo antes del paso de físicas
            // (sincronizados con ellas, como en Unity), con el delta escalado por TimeScale.
            StepPhysicsAnimators(objects, GrokoEngine.Time.DeltaTime);

            long physicsStart = EditorProfiler.Timestamp();
            physicsEngine.Step(objects, deltaTime);            // camino único: BEPU + broadphase legacy + eventos
            profiler.SamplePhysics(EditorProfiler.ElapsedMs(physicsStart));

            // Las particulas son componentes del MOTOR (no user scripts), asi que ExecuteUpdate no
            // las toca. Sin este pase, al darle Play se congelaban (solo avanzaban en edicion).
            // Tras physics.Step para que la colision High (raycast a BEPU) use el estado del frame.
            StepEditorParticles(deltaTime);

            long runtimeSceneStart = EditorProfiler.Timestamp();
            RuntimeScene.Tick(deltaTime);
            profiler.SampleRuntimeScene(EditorProfiler.ElapsedMs(runtimeSceneStart));
        }
        catch (Exception ex)
        {
            RestorePlayModeSnapshot("Runtime stopped: " + ex.Message);
        }
        finally
        {
            profiler.SampleRuntime(EditorProfiler.ElapsedMs(runtimeStart));
        }
    }

private void ExecuteUpdate(IEnumerable<GameObject> roots, float dt)
    {
        foreach (var obj in roots.ToList())
        {
            // GameObject inactivo: ni sus scripts ni los de sus hijos se actualizan (como Unity).
            if (!obj.IsActive) continue;
            var snapshot = new List<Component>(obj.Components);
            foreach (var component in snapshot)
            {
                if (!IsUserScript(component, out var script) || !script.HasStarted) continue;
                if (!script.Enabled) continue;   // script desactivado: no recibe Update
                try { script.Update(dt); }
                catch (Exception ex)
                {
                    ReportScriptException("Update", obj, script, ex);
                }
            }
            ExecuteUpdate(obj.Children.ToList(), dt);
        }
    }

private void RestorePlayModeSnapshot(string message)
    {
        isPlaying = false;
        playPaused = false;
        simulatePhysics = false;
        // Ciclo de vida completo: al parar Play llamamos OnDisable + OnDestroy a los scripts que
        // estuvieron vivos (Awoke), para que limpien (desuscribir eventos, guardar estado…) ANTES
        // de descartar la escena de juego. Se hace mientras RuntimeScene SIGUE listo (antes de
        // ClearContext) por si algún OnDestroy usa Instantiate/Destroy/Raycast.
        if (RuntimeScene.IsReady)
            ExecuteDestroy(objects);
        // Fuera de Play volvemos al modo normal del GC (no diferir, no acumular memoria).
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive;
        RuntimeScene.ClearContext();
        if (!string.IsNullOrWhiteSpace(playModeSnapshot))
        {
            // Deserializa PRIMERO en una lista temporal. Si la reconstrucción falla (p.ej. un
            // singleton que dejó referencias raras), la escena viva NO se toca y se conserva.
            // Antes se hacía objects.Clear() ANTES de deserializar → si deserializar lanzaba,
            // la escena quedaba VACÍA (el bug que vaciaba la escena al parar con singletons).
            List<GameObject>? restored = null;
            try
            {
                restored = SceneSerializer.Deserialize(playModeSnapshot, physicsEngine, scriptCompiler).ToList();
            }
            catch (Exception ex)
            {
                statusMessage = "Stop: restore falló (escena conservada): " + ex.Message;
            }

            if (restored != null)
            {
                physicsEngine.ClearColliders();
                objects.Clear();
                objects.AddRange(restored);
                physicsEngine.SyncPhysicsComponents(objects); // re-registra colliders de la escena restaurada
                selection.RestoreSelectedIds(playModeSelectionIds);
                statusMessage = message;
                sceneRenderer.InvalidateStaticBatch(); // objetos restaurados pueden tener IsStatic
            }
        }
        else
        {
            statusMessage = message;
        }
    }

private static void ResetScriptStartState(IEnumerable<GameObject> roots)
    {
        foreach (var obj in roots)
        {
            var snapshot = new List<Component>(obj.Components);
            foreach (var component in snapshot)
                if (IsUserScript(component, out var script))
                {
                    script.HasAwaken = false;   // Awake vuelve a correr en cada sesión de Play
                    script.HasStarted = false;
                }
            ResetScriptStartState(obj.Children);
        }
    }

private bool ExecuteAwake(IEnumerable<GameObject> roots)
    {
        foreach (var obj in roots.ToList())
        {
            var snapshot = new List<Component>(obj.Components);
            foreach (var component in snapshot)
            {
                if (!IsUserScript(component, out var script) || script.HasAwaken) continue;
                try
                {
                    script.Awake();
                    script.HasAwaken = true;
                    if (obj.IsActive && script.Enabled)
                        script.OnEnable();
                }
                catch (Exception ex)
                {
                    ReportScriptException("Awake", obj, script, ex);
                    return false;
                }
            }

            if (!ExecuteAwake(obj.Children.ToList()))
                return false;
        }

        return true;
    }

private void ExecuteDestroy(IEnumerable<GameObject> roots)
    {
        foreach (var obj in roots.ToList())
        {
            ExecuteDestroy(obj.Children.ToList());   // hijos primero, luego el padre
            var snapshot = new List<Component>(obj.Components);
            foreach (var component in snapshot)
            {
                // Solo los scripts que llegaron a vivir (Awoke) reciben OnDisable/OnDestroy.
                if (!IsUserScript(component, out var script) || !script.HasAwaken) continue;
                try
                {
                    if (script.Enabled)
                        script.OnDisable();
                    script.OnDestroy();
                }
                catch (Exception ex)
                {
                    // Un OnDestroy que lance NO debe bloquear el Stop: se registra y se sigue.
                    ReportScriptException("OnDestroy", obj, script, ex);
                }
            }
        }
    }

private bool ExecuteStart(IEnumerable<GameObject> roots)
    {
        foreach (var obj in roots.ToList())
        {
            var snapshot = new List<Component>(obj.Components);
            foreach (var component in snapshot)
            {
                if (!IsUserScript(component, out var script) || script.HasStarted) continue;
                try
                {
                    script.Start();
                    script.HasStarted = true;
                }
                catch (Exception ex)
                {
                    ReportScriptException("Start", obj, script, ex);
                    return false;
                }
            }

            if (!ExecuteStart(obj.Children.ToList()))
                return false;
        }

        return true;
    }

private static bool IsUserScript(Component component, out MonoBehaviour script)
    {
        if (component is MonoBehaviour mono &&
            component.GetType().Assembly != typeof(Component).Assembly)
        {
            script = mono;
            return true;
        }

        script = null!;
        return false;
    }

private void ReportScriptException(string phase, GameObject obj, MonoBehaviour script, Exception ex)
    {
        string scriptName = script.GetType().FullName ?? script.GetType().Name;
        string shortMessage = $"{phase} error on {obj.Name}.{script.GetType().Name}: {ex.Message}";
        Log($"{shortMessage}{Environment.NewLine}Script: {scriptName}{Environment.NewLine}{ex}", ConsoleSeverity.Error);
        statusMessageValue = shortMessage;
        lastStatusSeverity = ConsoleSeverity.Error;
        lastStatusFlashTime = GLFW.GetTime();
    }
}
