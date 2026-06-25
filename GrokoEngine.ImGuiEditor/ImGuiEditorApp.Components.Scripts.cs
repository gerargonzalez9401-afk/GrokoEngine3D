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
private void CompileScripts()
    {
        QueueEditorProgressTask("Compiling scripts", "Building C# assemblies", CompileScriptsNow);
    }

private void CompileScriptsNow()
    {
        FlushPendingScriptableObjectSaves();
        scriptableObjectCache.Clear();
        SceneStateSnapshot? sceneSnapshot = isPlaying ? null : CaptureSceneState();
        string? lockedInspectorObjectId = inspectorLocked ? lockedInspectorObject?.EditorId : null;
        UpdateEditorProgress("Running Roslyn compiler", 0.38f);
        var result = scriptCompiler.Compile();
        if (result.Success && sceneSnapshot.HasValue)
            RefreshSceneAfterScriptCompile(sceneSnapshot.Value, lockedInspectorObjectId);
        lock (scriptCompileGate)
        {
            pendingScriptCompile = false;
            pendingScriptCompileLogged = false;
            pendingScriptCompileReason = "script change";
            lastScriptCompileRequestUtc = DateTime.UtcNow;
            lastScriptSourceSignature = ComputeProjectScriptSignature();
            nextScriptSourceScanUtc = DateTime.UtcNow.AddSeconds(ScriptSourceScanIntervalSeconds);
        }
        statusMessage = result.Success
            ? $"Scripts compiled: {scriptCompiler.CompiledTypes.Count}"
            : "Script compile failed";
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorLog))
            statusMessage = result.ErrorLog;
    }

private void RefreshSceneAfterScriptCompile(SceneStateSnapshot snapshot, string? lockedInspectorObjectId)
    {
        try
        {
            RestoreSceneState(snapshot);

            if (inspectorLocked && !string.IsNullOrWhiteSpace(lockedInspectorObjectId))
                lockedInspectorObject = sceneGraph.FindById(lockedInspectorObjectId);

            RuntimeScene.SetContext(objects, physicsEngine, scriptCompiler);
            sceneRenderer.InvalidateStaticBatch();
            sceneRenderer.InvalidateCullingState();
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("[ScriptCompiler] Scripts compilados, pero no se pudo refrescar la escena: " + ex.Message);
            statusMessage = "Scripts compiled; scene refresh failed";
        }
    }

private void StartScriptWatcher()
    {
        StopScriptWatcher();

        try
        {
            Directory.CreateDirectory(rootAssetsPath);
            lock (scriptCompileGate)
            {
                lastScriptSourceSignature = ComputeProjectScriptSignature();
                nextScriptSourceScanUtc = DateTime.UtcNow.AddSeconds(ScriptSourceScanIntervalSeconds);
            }

            scriptWatcher = new FileSystemWatcher(rootAssetsPath, "*.cs")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024
            };
            scriptWatcher.Changed += OnScriptFileChanged;
            scriptWatcher.Created += OnScriptFileChanged;
            scriptWatcher.Deleted += OnScriptFileChanged;
            scriptWatcher.Renamed += OnScriptFileRenamed;
            scriptWatcher.Error += OnScriptWatcherError;
            scriptWatcher.EnableRaisingEvents = true;
            Debug.Log("[ScriptCompiler] Auto compile activo para scripts .cs.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ScriptCompiler] No se pudo activar auto compile: " + ex.Message);
        }
    }

private void StopScriptWatcher()
    {
        if (scriptWatcher == null)
            return;

        scriptWatcher.EnableRaisingEvents = false;
        scriptWatcher.Changed -= OnScriptFileChanged;
        scriptWatcher.Created -= OnScriptFileChanged;
        scriptWatcher.Deleted -= OnScriptFileChanged;
        scriptWatcher.Renamed -= OnScriptFileRenamed;
        scriptWatcher.Error -= OnScriptWatcherError;
        scriptWatcher.Dispose();
        scriptWatcher = null;
    }

private void OnScriptFileChanged(object sender, FileSystemEventArgs e)
    {
        QueueScriptAutoCompile(e.FullPath);
    }

private void OnScriptFileRenamed(object sender, RenamedEventArgs e)
    {
        QueueScriptAutoCompile(e.FullPath);
    }

private void OnScriptWatcherError(object sender, ErrorEventArgs e)
    {
        lock (scriptCompileGate)
        {
            pendingScriptCompile = true;
            pendingScriptCompileLogged = false;
            pendingScriptCompileReason = "watcher resync";
            lastScriptFileChangeUtc = DateTime.UtcNow;
            nextScriptSourceScanUtc = DateTime.MinValue;
        }

        Debug.LogWarning("[ScriptCompiler] Auto compile hizo resync: " + e.GetException().Message);
    }

private void QueueScriptAutoCompile(string path)
    {
        if (!IsProjectScriptPath(path))
            return;

        lock (scriptCompileGate)
        {
            pendingScriptCompile = true;
            pendingScriptCompileLogged = false;
            pendingScriptCompileReason = Path.GetFileName(path);
            lastScriptFileChangeUtc = DateTime.UtcNow;
            nextScriptSourceScanUtc = DateTime.UtcNow.AddSeconds(ScriptSourceScanIntervalSeconds);
        }
    }

private bool IsProjectScriptPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            string full = Path.GetFullPath(path);
            string assets = Path.GetFullPath(rootAssetsPath);
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                return false;

            string name = Path.GetFileName(full);
            if (name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                return false;

            string objSegment = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
            string binSegment = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
            return !full.Contains(objSegment, StringComparison.OrdinalIgnoreCase) &&
                   !full.Contains(binSegment, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

private void ProcessScriptAutoCompile()
    {
        ScanScriptSourcesForAutoCompile();

        string reason;
        lock (scriptCompileGate)
        {
            if (!pendingScriptCompile)
                return;

            double elapsed = (DateTime.UtcNow - lastScriptFileChangeUtc).TotalSeconds;
            if (elapsed < ScriptCompileDebounceSeconds)
                return;

            if (isPlaying)
            {
                if (!pendingScriptCompileLogged)
                {
                    pendingScriptCompileLogged = true;
                    statusMessage = "Scripts changed: auto compile pending until Play stops";
                }
                return;
            }

            if (pendingEditorProgressTask != null || editorProgressRunning)
            {
                lastScriptFileChangeUtc = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - lastScriptCompileRequestUtc).TotalSeconds < ScriptCompileMinimumGapSeconds)
            {
                lastScriptFileChangeUtc = DateTime.UtcNow;
                return;
            }

            reason = pendingScriptCompileReason;
        }

        statusMessage = $"Scripts changed ({reason}): compiling...";
        CompileScripts();
    }

private void ScanScriptSourcesForAutoCompile()
    {
        DateTime now = DateTime.UtcNow;
        lock (scriptCompileGate)
        {
            if (pendingScriptCompile || now < nextScriptSourceScanUtc)
                return;

            nextScriptSourceScanUtc = now.AddSeconds(ScriptSourceScanIntervalSeconds);
        }

        string signature = ComputeProjectScriptSignature();
        lock (scriptCompileGate)
        {
            if (string.Equals(signature, lastScriptSourceSignature, StringComparison.Ordinal))
                return;

            lastScriptSourceSignature = signature;
            pendingScriptCompile = true;
            pendingScriptCompileLogged = false;
            pendingScriptCompileReason = "disk scan";
            lastScriptFileChangeUtc = now;
        }
    }

private string ComputeProjectScriptSignature()
    {
        try
        {
            if (!Directory.Exists(rootAssetsPath))
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (string path in Directory
                         .EnumerateFiles(rootAssetsPath, "*.cs", SearchOption.AllDirectories)
                         .Where(IsProjectScriptPath)
                         .OrderBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    continue;

                sb.Append(Path.GetFullPath(path).ToLowerInvariant())
                  .Append('|')
                  .Append(info.Length)
                  .Append('|')
                  .Append(info.LastWriteTimeUtc.Ticks)
                  .Append('\n');
            }

            return sb.ToString();
        }
        catch
        {
            return lastScriptSourceSignature;
        }
    }

private void CreateScript()
    {
        CreateScript(rootAssetsPath);
    }

private void CreateScript(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !IsInsideAssets(targetDirectory))
            targetDirectory = rootAssetsPath;

        Directory.CreateDirectory(targetDirectory);
        string file = scriptCompiler.CreateScript(targetDirectory);
        selectedAssetPath = file;
        currentProjectDirectory = targetDirectory;
        CompileScripts();
        statusMessage = $"Created {Path.GetFileName(file)}";
    }

private void CreatePlayerControllerScript(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !IsInsideAssets(targetDirectory))
            targetDirectory = rootAssetsPath;

        Directory.CreateDirectory(targetDirectory);
        string file = scriptCompiler.CreatePlayerControllerScript(targetDirectory);
        selectedAssetPath = file;
        currentProjectDirectory = targetDirectory;
        CompileScripts();
        statusMessage = $"Created {Path.GetFileName(file)}";
    }

private void OpenScriptInEditor(string className)
    {
        try
        {
            string scriptPath = Directory
                .GetFiles(rootAssetsPath, className + ".cs", SearchOption.AllDirectories)
                .FirstOrDefault() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath))
                scriptCompiler.OpenInEditor(scriptPath);
            else
                statusMessage = $"No se encontró {className}.cs";
        }
        catch (Exception ex) { statusMessage = "Error al abrir el script: " + ex.Message; }
    }

private readonly Dictionary<Type, System.Reflection.FieldInfo[]> inspectorScriptFieldCache = new();

private System.Reflection.FieldInfo[] GetInspectorScriptFields(Type type)
    {
        if (inspectorScriptFieldCache.TryGetValue(type, out var fields))
            return fields;

        fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        inspectorScriptFieldCache[type] = fields;
        return fields;
    }

private void DrawScriptFields(object script, Action? onChanged = null)
    {
        var fields = GetInspectorScriptFields(script.GetType());

        // Los ScriptableObject son assets persistentes (se guardan como .asset en disco):
        // no pueden mantener referencias a objetos/componentes de la escena, ya que esos
        // viven solo en memoria mientras la escena está cargada y no son serializables como
        // JSON plano (provocan ciclos/objetos no serializables que rompían el guardado).
        bool isAssetScript = script is ScriptableObject;

        bool any = false;
        foreach (var field in fields)
        {
            if (field.IsInitOnly || field.IsLiteral) continue;
            Type ft = field.FieldType;
            string id = "##" + field.Name;
            any = true;

            if (ft == typeof(float))
            {
                DrawFloat(field.Name, (float)field.GetValue(script)!, v => { field.SetValue(script, v); onChanged?.Invoke(); },
                    0.05f, float.MinValue, float.MaxValue);
            }
            else if (ft == typeof(int))
            {
                FieldRow(field.Name);
                int cur = (int)field.GetValue(script)!;
                if (ImGui.DragInt(id, ref cur, 0.2f)) { field.SetValue(script, cur); onChanged?.Invoke(); }
            }
            else if (ft == typeof(bool))
            {
                DrawCheckRow(field.Name, (bool)field.GetValue(script)!, v => { field.SetValue(script, v); onChanged?.Invoke(); });
            }
            else if (ft == typeof(string))
            {
                FieldRow(field.Name);
                string cur = (string?)field.GetValue(script) ?? "";
                if (ImGui.InputText(id, ref cur, 512)) { field.SetValue(script, cur); onChanged?.Invoke(); }
            }
            else if (ft == typeof(double))
            {
                DrawFloat(field.Name, (float)(double)field.GetValue(script)!, v => { field.SetValue(script, (double)v); onChanged?.Invoke(); },
                    0.05f, float.MinValue, float.MaxValue);
            }
            else if (ft.IsEnum)
            {
                FieldRow(field.Name);
                string[] names = Enum.GetNames(ft);
                Array values = Enum.GetValues(ft);
                int idx = Math.Max(0, Array.IndexOf(values, field.GetValue(script)));
                if (ImGui.Combo(id, ref idx, names, names.Length))
                { field.SetValue(script, values.GetValue(idx)); onChanged?.Invoke(); }
            }
            else if (ft == typeof(MiMotor.Mathematics.Vector3))
            {
                FieldRow(field.Name);
                var mv = (MiMotor.Mathematics.Vector3)field.GetValue(script)!;
                var edit = new System.Numerics.Vector3(mv.X, mv.Y, mv.Z);
                if (ImGui.DragFloat3(id, ref edit, 0.05f))
                { field.SetValue(script, new MiMotor.Mathematics.Vector3(edit.X, edit.Y, edit.Z)); onChanged?.Invoke(); }
            }
            else if (typeof(GameObject).IsAssignableFrom(ft))
            {
                FieldRow(field.Name);
                if (isAssetScript)
                {
                    DrawUnsupportedAssetReferenceField(field.Name, "GameObject");
                }
                else
                {
                    var cur = field.GetValue(script) as GameObject;
                    DrawObjectSlot(field.Name, ft, cur?.Name ?? "",
                        go => { field.SetValue(script, go); onChanged?.Invoke(); },
                        () => { field.SetValue(script, null); onChanged?.Invoke(); });
                }
            }
            else if (ft == typeof(MiMotor.Mathematics.Transform))
            {
                FieldRow(field.Name);
                if (isAssetScript)
                {
                    DrawUnsupportedAssetReferenceField(field.Name, "Transform");
                }
                else
                {
                    DrawObjectSlot(field.Name, ft,
                        RefTransformName(field.GetValue(script) as MiMotor.Mathematics.Transform),
                        go => { field.SetValue(script, go.transform); onChanged?.Invoke(); },
                        () => { field.SetValue(script, null); onChanged?.Invoke(); });
                }
            }
            else if (typeof(ScriptableObject).IsAssignableFrom(ft))
            {
                FieldRow(field.Name);
                var cur = field.GetValue(script) as ScriptableObject;
                DrawScriptableObjectSlot(field.Name, ft, cur,
                    so => { field.SetValue(script, so); onChanged?.Invoke(); });
            }
            else if (typeof(Component).IsAssignableFrom(ft))
            {
                FieldRow(field.Name);
                if (isAssetScript)
                {
                    DrawUnsupportedAssetReferenceField(field.Name, ft.Name);
                }
                else
                {
                    var cur = field.GetValue(script) as Component;
                    DrawObjectSlot(field.Name, ft,
                        cur != null ? $"{cur.gameObject?.Name} ({ft.Name})" : "",
                        go =>
                        {
                            var comp = go.Components.FirstOrDefault(c => ft.IsInstanceOfType(c));
                            if (comp != null) { field.SetValue(script, comp); onChanged?.Invoke(); }
                            else statusMessage = $"{go.Name} no tiene un componente {ft.Name}";
                        },
                        () => { field.SetValue(script, null); onChanged?.Invoke(); });
                }
            }
            else
            {
                FieldRow(field.Name);
                ImGui.TextDisabled($"({ft.Name})");
            }
        }

        if (!any)
            ImGui.TextDisabled("Sin campos públicos");
    }

private static void FieldRow(string name)
    {
        const float leftPad = 4f;   // margen izquierdo (el nombre no pegado al borde)
        float labelW = InspectorLabelWidth();
        string label = Ellipsize(Nicify(name), Math.Max(4, (int)(labelW / 7.2f)));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4f);   // separación vertical entre campos
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + leftPad);
        ImGui.AlignTextToFramePadding();
        float brightness = currentDrawingApp?.guiLabelBrightness ?? 0.58f;
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(brightness, brightness, brightness, 1f));
        ImGui.TextUnformatted(label);
        RegisterGuiElement(GuiStyleClass.InspectorLabel, name);
        ImGui.PopStyleColor();
        ImGui.SameLine(labelW + leftPad, 6f);
        ImGui.SetNextItemWidth(-8f);   // margen derecho (el control no pegado al borde)
    }

private void DrawScriptableObjectSlot(string id, Type fieldType, ScriptableObject? current, Action<ScriptableObject?> onAssign)
    {
        ImGui.PushID(id);
        bool empty = current == null;
        string text = empty ? $"None ({TipoLegible(fieldType)})" : current!.Name;
        float avail = ImGui.GetContentRegionAvail().X;
        float clearW = empty ? 0f : 20f;
        float boxW = Math.Max(34f, avail - clearW - (clearW > 0f ? 4f : 0f));
        string displayText = Ellipsize(text, Math.Max(6, (int)(boxW / 7.5f)));

        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.17f, 0.20f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, empty
            ? new System.Numerics.Vector4(0.52f, 0.55f, 0.60f, 1f)
            : new System.Numerics.Vector4(0.85f, 0.87f, 0.90f, 1f));
        ImGui.Button(displayText + "##box", new Vector2(boxW, 22f));
        ImGui.PopStyleColor(3);
        bool boxHovered = ImGui.IsItemHovered();

        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) && ScriptableObjectAsset.IsAssetPath(draggingAssetPath))
            {
                var loaded = ScriptableObjectAsset.Load(draggingAssetPath, physicsEngine, scriptCompiler);
                if (loaded != null && fieldType.IsInstanceOfType(loaded))
                {
                    onAssign(loaded);
                    statusMessage = "Assigned " + Path.GetFileName(draggingAssetPath);
                }
                else
                {
                    statusMessage = $"El asset no es un {TipoLegible(fieldType)}";
                }
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }

        if (!empty)
        {
            ImGui.SameLine(0f, 4f);
            if (ImGui.Button("x##clear", new Vector2(clearW, 22f)))
                onAssign(null);
        }

        if (boxHovered && !empty)
            DrawTooltip(current!.AssetPath);

        ImGui.PopID();
    }
}
