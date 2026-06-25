using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text;

namespace GrokoEngine
{
    public class CompilationResult
    {
        public bool         Success      { get; set; }
        public List<Type>   CompiledTypes{ get; set; } = new List<Type>();
        public string       ErrorLog     { get; set; } = "";
    }

    public class ScriptCompiler : IDisposable
    {
        public Assembly? UltimoEnsamblado { get; private set; }
        private readonly string assetsPath;
        private List<Type> tiposCompilados = new List<Type>();
        private List<Type> tiposScriptableObjects = new List<Type>();
        private AssemblyLoadContext? scriptLoadContext;
        private string? successfulScriptSignature;

        // Cache de referencias Roslyn — se recalcula solo cuando hay nuevos ensamblados
        private List<MetadataReference>? cachedReferences;
        private int cachedAssemblyCount;

        public event Action<string, bool>? OnLog;
        public IReadOnlyList<Type> CompiledTypes => tiposCompilados;
        public IReadOnlyList<Type> ScriptableObjectTypes => tiposScriptableObjects;

        // Nombres canónicos del DLL de matemáticas en orden de prioridad
        private static readonly string[] MathDllNames =
        {
            "MiMotor.Mathematics.dll",
            "MiMotor.Math.dll",
            "Mathematics.dll"
        };

        public ScriptCompiler(string assetsPath)
        {
            this.assetsPath = assetsPath;
        }

        // =====================================================
        // INITIALIZE
        // =====================================================
        public void Initialize()
        {
            GenerarCsproj();

            string csprojPath = Path.Combine(assetsPath, "GrokoScripts.csproj");
            string slnPath    = Path.Combine(
                Directory.GetParent(assetsPath)!.FullName, "GrokoProject.sln");
            GenerarSln(slnPath, csprojPath);
        }

        // =====================================================
        // GENERAR .CSPROJ
        // =====================================================
        private void GenerarCsproj()
        {
            try
            {
                if (!Directory.Exists(assetsPath))
                {
                    Directory.CreateDirectory(assetsPath);
                    Log($"[ScriptCompiler] Carpeta Assets creada en: {assetsPath}", false);
                }

                string engineDir  = AppDomain.CurrentDomain.BaseDirectory;
                string csprojPath = Path.Combine(assetsPath, "GrokoScripts.csproj");

                Log($"[ScriptCompiler] Generando .csproj en: {csprojPath}", false);

                string? mathDll = BuscarMathDll(engineDir);
                string  engineDll = ResolverEngineDll(engineDir);

                Log($"[ScriptCompiler] engineDll: {engineDll}", false);
                Log($"[ScriptCompiler] mathDll: {mathDll ?? "NO ENCONTRADO"}", false);

                string tfm = ResolverTargetFramework();

                string mathRef = mathDll == null
                    ? "    <!-- DLL de matemáticas no encontrado -->"
                    : $"    <Reference Include=\"MiMotor.Mathematics\">\n      <HintPath>{mathDll}</HintPath>\n      <Private>true</Private>\n    </Reference>";

                string contenido =
$@"<!-- Auto-generado por GrokoEngine - no editar manualmente -->
<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>{tfm}</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <UseWindowsForms>true</UseWindowsForms>
    <NoWarn>$(NoWarn);CA1416;CS2008</NoWarn>
    <OutputPath>bin\ScriptsIntelliSense\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include=""GrokoEngine.Core"">
      <HintPath>{engineDll}</HintPath>
      <Private>true</Private>
    </Reference>
{mathRef}
  </ItemGroup>

  <ItemGroup>
    <Compile Include=""**\*.cs"" Exclude=""obj\**;bin\**;**\*.Designer.cs""/>
  </ItemGroup>

</Project>";

                // Solo reescribir si cambió el contenido
                if (!File.Exists(csprojPath) || File.ReadAllText(csprojPath) != contenido)
                {
                    File.WriteAllText(csprojPath, contenido);
                    Log($"[ScriptCompiler] GrokoScripts.csproj generado.", false);
                }
                else
                {
                    Log("[ScriptCompiler] GrokoScripts.csproj ya estaba actualizado.", false);
                }
            }
            catch (Exception ex)
            {
                Log($"[ScriptCompiler] ERROR al generar .csproj: {ex.Message}", true);
            }
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private static string? BuscarMathDll(string engineDir)
        {
            foreach (var nombre in MathDllNames)
            {
                string ruta = Path.Combine(engineDir, nombre);
                if (File.Exists(ruta)) return ruta;
            }
            return null;
        }

        private static string ResolverEngineDll(string engineDir)
        {
            string dll = Path.Combine(engineDir, "GrokoEngine.Core.dll");
            if (File.Exists(dll)) return dll;

            // Fallback al ejecutable actual
            dll = Assembly.GetExecutingAssembly().Location;
            string exe = Path.ChangeExtension(dll, ".exe");
            return File.Exists(exe) ? exe : dll;
        }

        private static string ResolverTargetFramework()
        {
            // Detectar versión de .NET correctamente vía Version, no via string
            var version = Environment.Version;
            string moniker = version.Major switch
            {
                >= 10 => "net10.0",
                9     => "net9.0",
                8     => "net8.0",
                _     => "net8.0"
            };
            return $"{moniker}-windows10.0.17763.0";
        }

        // =====================================================
        // GENERAR .SLN
        // =====================================================
        private void GenerarSln(string slnPath, string csprojPath)
        {
            try
            {
                string csprojRel = "Assets\\GrokoScripts.csproj";
                string proyGuid  = "{8D4D3CC0-5B6A-4A45-B0A0-12345678ABCD}";
                string slnGuid   = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

                var sb = new StringBuilder();
                sb.Append("\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n");
                sb.Append("# Visual Studio Version 17\r\n");
                sb.Append("VisualStudioVersion = 17.0.31903.59\r\n");
                sb.Append("MinimumVisualStudioVersion = 10.0.40219.1\r\n");
                sb.Append($"Project(\"{slnGuid}\") = \"GrokoScripts\", \"{csprojRel}\", \"{proyGuid}\"\r\n");
                sb.Append("EndProject\r\n");
                sb.Append("Global\r\n");
                sb.Append("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n");
                sb.Append("\t\tDebug|Any CPU = Debug|Any CPU\r\n");
                sb.Append("\t\tRelease|Any CPU = Release|Any CPU\r\n");
                sb.Append("\tEndGlobalSection\r\n");
                sb.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n");
                sb.Append($"\t\t{proyGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n");
                sb.Append($"\t\t{proyGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n");
                sb.Append($"\t\t{proyGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU\r\n");
                sb.Append($"\t\t{proyGuid}.Release|Any CPU.Build.0 = Release|Any CPU\r\n");
                sb.Append("\tEndGlobalSection\r\n");
                sb.Append("EndGlobal\r\n");
                string contenido = sb.ToString();

                if (!File.Exists(slnPath) || File.ReadAllText(slnPath) != contenido)
                {
                    File.WriteAllText(slnPath, contenido);
                    Log($"[ScriptCompiler] GrokoProject.sln generado.", false);
                }
            }
            catch (Exception ex)
            {
                Log($"[ScriptCompiler] No se pudo generar el .sln: {ex.Message}", true);
            }
        }

        // =====================================================
        // ABRIR EN EDITOR
        // =====================================================
        public void OpenInEditor(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Log($"[ScriptCompiler] Archivo no encontrado: {filePath}", true);
                return;
            }

            try
            {
                string slnPath = Path.Combine(
                    Directory.GetParent(assetsPath)!.FullName, "GrokoProject.sln");

                GenerarSln(slnPath, Path.Combine(assetsPath, "GrokoScripts.csproj"));

                if (AbrirConDTE(filePath, slnPath))
                {
                    Log($"[ScriptCompiler] Abriendo en VS existente: {Path.GetFileName(filePath)}", false);
                    return;
                }

                string? devenv = BuscarDevenv();
                if (devenv != null && File.Exists(slnPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = devenv,
                        Arguments       = $"\"{slnPath}\" \"{filePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow  = true
                    });
                    Log($"[ScriptCompiler] Abriendo nueva instancia VS con proyecto.", false);
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
                    Log($"[ScriptCompiler] Abriendo con editor predeterminado.", false);
                }
            }
            catch (Exception ex)
            {
                Log($"[ScriptCompiler] No se pudo abrir el editor: {ex.Message}", true);
            }
        }

        private static string? BuscarDevenv()
        {
            // 1) vswhere: detección robusta de CUALQUIER versión instalada (2019/2022/2026/futuras).
            //    Es la forma canónica y soporta ediciones/rutas no estándar.
            try
            {
                string vswhere = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft Visual Studio", "Installer", "vswhere.exe");
                if (File.Exists(vswhere))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = vswhere,
                        Arguments              = "-latest -prerelease -products * -find Common7\\IDE\\devenv.exe",
                        RedirectStandardOutput = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(5000);
                        string ruta = output
                            .Split('\n')
                            .Select(l => l.Trim())
                            .FirstOrDefault(l => l.EndsWith("devenv.exe", StringComparison.OrdinalIgnoreCase)
                                              && File.Exists(l)) ?? "";
                        if (!string.IsNullOrEmpty(ruta)) return ruta;
                    }
                }
            }
            catch { /* vswhere no disponible o falló: usar el fallback de rutas */ }

            // 2) Fallback a rutas conocidas. "18" = VS 2026, además de 2022/2019.
            string[] versiones = { "18", "2022", "2019" };
            string[] edits     = { "Community", "Professional", "Enterprise" };
            string[] prefijos  =
            {
                @"C:\Program Files\Microsoft Visual Studio",
                @"C:\Program Files (x86)\Microsoft Visual Studio"
            };

            foreach (var prefijo in prefijos)
            foreach (var ver     in versiones)
            foreach (var edit    in edits)
            {
                string ruta = Path.Combine(prefijo, ver, edit, "Common7", "IDE", "devenv.exe");
                if (File.Exists(ruta)) return ruta;
            }
            return null;
        }

        // =====================================================
        // COM DTE
        // =====================================================
        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        private bool AbrirConDTE(string filePath, string slnPath)
        {
            try
            {
                GetRunningObjectTable(0, out IRunningObjectTable rot);
                rot.EnumRunning(out IEnumMoniker enumMoniker);

                IMoniker[] monikers = new IMoniker[1];
                IntPtr     fetched  = IntPtr.Zero;

                EnvDTE.DTE? instanciaVacia    = null;
                EnvDTE.DTE? instanciaCorrecta = null;

                while (enumMoniker.Next(1, monikers, fetched) == 0)
                {
                    CreateBindCtx(0, out IBindCtx ctx);
                    if (monikers[0] == null) continue;
                    monikers[0].GetDisplayName(ctx, null, out string name);

                    if (!name.StartsWith("!VisualStudio.DTE")) continue;

                    rot.GetObject(monikers[0], out object obj);
                    if (obj is not EnvDTE.DTE dte) continue;

                    string dteSln = "";
                    try { dteSln = dte.Solution?.FullName ?? ""; } catch { }

                    if (string.Equals(dteSln, slnPath, StringComparison.OrdinalIgnoreCase))
                    {
                        instanciaCorrecta = dte;
                        break;
                    }
                    else if (string.IsNullOrEmpty(dteSln) && instanciaVacia == null)
                    {
                        instanciaVacia = dte;
                    }
                }

                if (instanciaCorrecta != null)
                {
                    instanciaCorrecta.ItemOperations.OpenFile(filePath);
                    instanciaCorrecta.MainWindow.Activate();
                    return true;
                }

                if (instanciaVacia != null)
                {
                    try
                    {
                        if (instanciaVacia.Solution == null || instanciaVacia.ItemOperations == null)
                            return false;
                        instanciaVacia.Solution.Open(slnPath);
                        instanciaVacia.ItemOperations.OpenFile(filePath);
                        instanciaVacia.MainWindow.Activate();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log($"[DTE] Error al abrir .sln en instancia vacía: {ex.Message}", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[DTE] Error COM: {ex.Message}", true);
            }

            return false;
        }

        // =====================================================
        // COMPILACIÓN PRINCIPAL
        // =====================================================
        public CompilationResult Compile()
        {
            // Filtrar archivos obj/ y bin/ usando normalización de ruta real
            string objSep = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
            string binSep = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;

            string[] archivosScript = Array.Empty<string>();
            try
            {
                archivosScript = Directory
                    .GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        string norm = Path.GetFullPath(f);
                        string name = Path.GetFileName(norm);
                        return !norm.Contains(objSep) &&
                               !norm.Contains(binSep) &&
                               !name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) &&
                               !name.Equals("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
                    })
                    // Verificar que el archivo existe y es legible antes de ParseText
                    .Where(f => { try { return File.Exists(f); } catch { return false; } })
                    .ToArray();
            }
            catch (Exception ex)
            {
                string msg = "[ScriptCompiler] Error al buscar scripts: " + ex.Message;
                Log(msg, true);
                return new CompilationResult { Success = false, ErrorLog = msg };
            }

            string scriptSignature = ComputeScriptSignature(archivosScript);
            if (successfulScriptSignature == scriptSignature)
                return new CompilationResult { Success = true, CompiledTypes = tiposCompilados };

            if (archivosScript.Length == 0)
            {
                tiposCompilados.Clear();
                tiposScriptableObjects.Clear();
                UltimoEnsamblado = null;
                scriptLoadContext?.Unload();
                scriptLoadContext = null;
                successfulScriptSignature = scriptSignature;
                Log("[ScriptCompiler] No se encontraron scripts en Assets.", false);
                return new CompilationResult { Success = true };
            }

            try
            {
                var syntaxTrees = new List<SyntaxTree>(archivosScript.Length);
                foreach (var f in archivosScript)
                {
                    string src;
                    try { src = File.ReadAllText(f); }
                    catch (Exception ex)
                    {
                        Log($"[ScriptCompiler] No se pudo leer {f}: {ex.Message}", true);
                        continue;
                    }
                    string repaired = RepairLegacyPlayerControllerTemplate(src);
                    if (!string.Equals(repaired, src, StringComparison.Ordinal))
                    {
                        src = repaired;
                        try
                        {
                            File.WriteAllText(f, src);
                            Log($"[ScriptCompiler] PlayerControllerPro actualizado para la API actual: {Path.GetFileName(f)}", false);
                        }
                        catch (Exception ex)
                        {
                            Log($"[ScriptCompiler] No se pudo guardar la reparacion de {f}: {ex.Message}", true);
                        }
                    }

                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(src, path: f));
                }

                var references = ObtenerReferencias();

                var compilation = CSharpCompilation.Create(
                    assemblyName: "GrokoScripts_" + Guid.NewGuid().ToString("N"),
                    syntaxTrees: syntaxTrees,
                    references: references,
                    options: new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Debug,
                        allowUnsafe: false));

                using var ms = new MemoryStream();
                var result   = compilation.Emit(ms);

                if (!result.Success)
                {
                    var sb = new StringBuilder("[ScriptCompiler] Errores de compilación:\n");
                    foreach (var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        string loc = diag.Location.IsInSource
                            ? $"Línea {diag.Location.GetLineSpan().StartLinePosition.Line + 1}"
                            : "";
                        sb.Append($"  [{loc}] {diag.GetMessage()}\n");
                    }
                    string errores = sb.ToString();
                    Log(errores, true);
                    return new CompilationResult { Success = false, ErrorLog = errores };
                }

                ms.Seek(0, SeekOrigin.Begin);

                var oldContext = scriptLoadContext;
                scriptLoadContext = new AssemblyLoadContext(
                    "GrokoScripts_" + Guid.NewGuid().ToString("N"),
                    isCollectible: true);
                var assembly = scriptLoadContext.LoadFromStream(ms);
                UltimoEnsamblado = assembly;

                var nuevosTipos = new List<Type>();
                var nuevosScriptableObjects = new List<Type>();
                foreach (Type t in assembly.GetTypes())
                {
                    if (t.IsAbstract) continue;
                    if (t.IsSubclassOf(typeof(MonoBehaviour))) nuevosTipos.Add(t);
                    else if (t.IsSubclassOf(typeof(ScriptableObject))) nuevosScriptableObjects.Add(t);
                }

                tiposCompilados = nuevosTipos;
                tiposScriptableObjects = nuevosScriptableObjects;

                // Descargar contexto anterior después de cargar el nuevo.
                // El GC.Collect + WaitForPendingFinalizers es NECESARIO: un
                // AssemblyLoadContext coleccionable solo libera el ensamblado viejo
                // cuando el GC recoge sus últimas referencias. No es un code smell.
                oldContext?.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Invalidar cache de referencias (nuevo ensamblado disponible)
                cachedReferences    = null;
                cachedAssemblyCount = 0;

                Log($"[ScriptCompiler] Compilación exitosa. Scripts cargados: {tiposCompilados.Count}", false);
                successfulScriptSignature = scriptSignature;
                return new CompilationResult { Success = true, CompiledTypes = tiposCompilados };
            }
            catch (Exception ex)
            {
                string msg = "[ScriptCompiler] Error crítico: " + ex.Message;
                Log(msg, true);
                return new CompilationResult { Success = false, ErrorLog = msg };
            }
        }

        private static string ComputeScriptSignature(IEnumerable<string> files)
        {
            var sb = new StringBuilder();
            foreach (string file in files.OrderBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;
                    sb.Append(Path.GetFullPath(file).ToLowerInvariant())
                      .Append('|')
                      .Append(info.Length)
                      .Append('|')
                      .Append(info.LastWriteTimeUtc.Ticks)
                      .Append('\n');
                }
                catch
                {
                }
            }

            return sb.ToString();
        }

        // =====================================================
        // REFERENCIAS ROSLYN — con cache
        // =====================================================
        private List<MetadataReference> ObtenerReferencias()
        {
            var currentAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            // Usar cache si el número de ensamblados no cambió
            if (cachedReferences != null && cachedAssemblyCount == currentAssemblies.Length)
                return cachedReferences;

            var refs = new List<MetadataReference>(64);
            AñadirRef(refs, typeof(object));
            AñadirRef(refs, typeof(Console));
            AñadirRef(refs, typeof(Enumerable));
            AñadirRef(refs, typeof(List<>));
            AñadirRef(refs, typeof(File));
            AñadirRef(refs, typeof(Math));
            AñadirRef(refs, typeof(Input));
            TryAñadirRefPorNombre(refs, "System.Runtime");
            TryAñadirRefPorNombre(refs, "System.Collections");
            TryAñadirRefPorNombre(refs, "System.IO.FileSystem");
            TryAñadirRefPorNombre(refs, "netstandard");
            refs.Add(MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location));

            string engineDir = AppDomain.CurrentDomain.BaseDirectory;
            string? mathDll  = BuscarMathDll(engineDir);
            if (mathDll != null)
                refs.Add(MetadataReference.CreateFromFile(mathDll));
            else
                Log("[ScriptCompiler] ADVERTENCIA: No se encontró el DLL de matemáticas.", false);

            // Añadir todos los ensamblados cargados (evitar duplicados por ubicación)
            var ubicaciones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in currentAssemblies)
            {
                if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
                if (!ubicaciones.Add(asm.Location)) continue;
                try { refs.Add(MetadataReference.CreateFromFile(asm.Location)); }
                catch (Exception ex) { GrokoEngine.Debug.LogWarning($"[ScriptCompiler] No se pudo referenciar {asm.GetName().Name}: {ex.Message}"); }
            }

            cachedReferences    = refs;
            cachedAssemblyCount = currentAssemblies.Length;
            return refs;
        }

        private static void AñadirRef(List<MetadataReference> refs, Type tipo)
        {
            try
            {
                string loc = tipo.Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                    refs.Add(MetadataReference.CreateFromFile(loc));
            }
            catch (Exception ex)
            {
                GrokoEngine.Debug.LogWarning($"[ScriptCompiler] No se pudo referenciar {tipo.Name}: {ex.Message}");
            }
        }

        private static void TryAñadirRefPorNombre(List<MetadataReference> refs, string nombre)
        {
            try
            {
                var asm = Assembly.Load(nombre);
                if (!string.IsNullOrEmpty(asm.Location))
                    refs.Add(MetadataReference.CreateFromFile(asm.Location));
            }
            catch (Exception ex)
            {
                GrokoEngine.Debug.LogWarning($"[ScriptCompiler] No se pudo cargar la referencia '{nombre}': {ex.Message}");
            }
        }

        private static string RepairLegacyPlayerControllerTemplate(string source)
        {
            if (!source.Contains("class PlayerControllerPro", StringComparison.Ordinal) ||
                !source.Contains("value.Length", StringComparison.Ordinal))
                return source;

            string repaired = source
                .Replace("desiredDirection.Length > 0.0001f", "Magnitude(desiredDirection) > 0.0001f", StringComparison.Ordinal)
                .Replace("float length = value.Length;", "float length = Magnitude(value);", StringComparison.Ordinal);

            if (!repaired.Contains("private static float Magnitude(Vector3 value)", StringComparison.Ordinal))
            {
                const string marker = "        private static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;";
                const string magnitude =
                    "        private static float Magnitude(Vector3 value) => MathF.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);\n\n";
                repaired = repaired.Replace(marker, magnitude + marker, StringComparison.Ordinal);
            }

            return repaired;
        }

        // =====================================================
        // CREAR SCRIPT
        // =====================================================
        public string CreateScript(string directory, string className = "NuevoScript")
        {
            string finalName = className;
            string filePath  = Path.Combine(directory, finalName + ".cs");
            int    counter   = 1;
            while (File.Exists(filePath))
            {
                finalName = $"{className}{counter++}";
                filePath  = Path.Combine(directory, finalName + ".cs");
            }

            string template =
                $"using GrokoEngine;\n" +
                $"using MiMotor.Mathematics;\n\n" +
                $"namespace GrokoEngine\n{{\n" +
                $"    public class {finalName} : MonoBehaviour\n    {{\n" +
                $"        public override void Start()\n        {{\n" +
                $"            // Se ejecuta una vez al dar Play\n        }}\n\n" +
                $"        public override void Update(double dt)\n        {{\n" +
                $"            // Ejemplo: if (Input.GetKeyDown(KeyCode.Space)) {{ }}\n" +
                $"            // dt = tiempo desde el ultimo frame en segundos\n        }}\n" +
                $"    }}\n}}\n";

            File.WriteAllText(filePath, template);
            Log($"[ScriptCompiler] Script creado: {finalName}.cs", false);
            return filePath;
        }

        public string CreatePlayerControllerScript(string directory, string className = "PlayerControllerPro")
        {
            string finalName = className;
            string filePath = Path.Combine(directory, finalName + ".cs");
            int counter = 1;
            while (File.Exists(filePath))
            {
                finalName = $"{className}{counter++}";
                filePath = Path.Combine(directory, finalName + ".cs");
            }

            string template =
$@"using System;
using GrokoEngine;
using MiMotor.Mathematics;

namespace GrokoEngine
{{
    public class {finalName} : MonoBehaviour
    {{
        public GameObject? CameraObject;
        public string CameraName = ""Main Camera"";

        public float WalkSpeed = 3.6f;
        public float SprintSpeed = 6.8f;
        public float Acceleration = 9.5f;
        public float Deceleration = 14f;
        public float MovementDirectionSharpness = 10f;
        public float WallSlideSharpness = 28f;
        public float RotationSharpness = 13f;
        public float JumpHeight = 1.35f;
        public float Gravity = 26f;

        public float CameraDistance = 6.0f;
        public float CameraHeight = 1.55f;
        public float CameraShoulder = -1.05f;
        public float CameraLookAhead = 0.55f;
        public float CameraFollowSharpness = 7f;
        public float CameraLookSharpness = 6f;
        public float CameraOrbitSharpness = 10f;
        public float CameraRecenteringSharpness = 1.7f;
        public float CameraRecenteringDelay = 0.55f;
        public bool AutoAlignCameraToMovement = true;
        public bool HoldRightMouseToRotateCamera = false;
        public float CameraSensitivity = 0.12f;
        public float CameraVerticalSensitivityScale = 0.45f;
        public float CameraPitchMin = -8f;
        public float CameraPitchMax = 32f;
        public bool InvertCameraX = false;
        public bool InvertCameraY = false;
        public bool BloquearMouseAlIniciar = true;
        public bool RebloquearMouseConClick = true;

        private CharacterController? controller;
        private Animator? animator;
        private float verticalVelocity;
        private Vector3 currentHorizontalVelocity = Vector3.Zero;
        private Vector3 smoothedMoveDirection = Vector3.Zero;
        private Vector3 currentLookAheadDirection = Vector3.Zero;
        private Vector3 smoothedCameraTarget = Vector3.Zero;
        private float cameraYaw;
        private float desiredCameraYaw;
        private float cameraPitch = 12f;
        private float desiredCameraPitch = 12f;
        private float timeSinceManualCamera;
        private bool cameraTargetInitialized;

        public override void Start()
        {{
            controller = gameObject.GetComponent<CharacterController>() ?? gameObject.AddComponent<CharacterController>();
            animator = gameObject.GetComponent<Animator>();
            CameraObject ??= RuntimeScene.FindObjectByName(CameraName);

            if (CameraObject != null)
                cameraYaw = CameraObject.RotY;
            desiredCameraYaw = cameraYaw;
            desiredCameraPitch = cameraPitch;
            currentLookAheadDirection = ForwardFromYaw(cameraYaw);

            if (controller != null)
            {{
                controller.UseGravity = false;
                controller.AutoCenter = true;
            }}

            if (BloquearMouseAlIniciar)
                Input.LockCursor();
        }}

        public override void Update(double dt)
        {{
            float delta = Clamp((float)dt, 0f, 0.05f);
            if (delta <= 0f || controller == null)
                return;

            UpdateMouseLock();

            float inputX = Input.GetAxisRaw(""Horizontal"");
            float inputY = Input.GetAxisRaw(""Vertical"");
            float inputMagnitude = Clamp01Magnitude(ref inputX, ref inputY);

            UpdateCameraInput(delta, inputMagnitude);

            Vector3 cameraForward = ForwardFromYaw(cameraYaw);
            Vector3 cameraRight = RightFromYaw(cameraYaw);
            Vector3 desiredDirection = NormalizeSafe(cameraRight * inputX + cameraForward * inputY);

            if (inputMagnitude > 0.001f)
            {{
                float dirBlend = Exp01(MovementDirectionSharpness, delta);
                smoothedMoveDirection = Magnitude(smoothedMoveDirection) < 0.0001f
                    ? desiredDirection
                    : NormalizeSafe(Vector3.Lerp(smoothedMoveDirection, desiredDirection, dirBlend));
            }}
            else
            {{
                smoothedMoveDirection = Vector3.Lerp(smoothedMoveDirection, Vector3.Zero, Exp01(Deceleration, delta));
            }}

            bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float targetSpeed = (sprint ? SprintSpeed : WalkSpeed) * inputMagnitude;
            Vector3 targetVelocity = smoothedMoveDirection * targetSpeed;
            float velocitySharpness = inputMagnitude > 0.001f ? Acceleration : Deceleration;
            currentHorizontalVelocity = Vector3.Lerp(
                currentHorizontalVelocity,
                targetVelocity,
                Exp01(velocitySharpness, delta));

            Vector3 moveDirection = NormalizeSafe(new Vector3(
                currentHorizontalVelocity.X,
                0f,
                currentHorizontalVelocity.Z));
            if (Magnitude(moveDirection) > 0.0001f)
            {{
                currentLookAheadDirection = Vector3.Lerp(
                    currentLookAheadDirection,
                    moveDirection,
                    Exp01(CameraLookSharpness, delta));
            }}

            // Mirar hacia la velocidad suavizada, no hacia el input bruto.
            // Así al rotar cámara + avanzar o invertir izquierda/derecha no pega un salto seco.
            Vector3 facingDirection = moveDirection;

            if (Magnitude(facingDirection) > 0.0001f)
            {{
                float targetYaw = MathF.Atan2(facingDirection.X, facingDirection.Z) * 57.29578f;
                gameObject.RotY = SmoothAngle(gameObject.RotY, targetYaw, RotationSharpness, delta);
            }}

            bool grounded = controller.IsGrounded;
            if (grounded && verticalVelocity < 0f)
                verticalVelocity = -0.5f;

            if (grounded && Input.GetKeyDown(KeyCode.Space))
                verticalVelocity = MathF.Sqrt(2f * Gravity * JumpHeight);

            verticalVelocity -= Gravity * delta;

            var moveFlags = controller.Move(new Vector3(
                currentHorizontalVelocity.X * delta,
                verticalVelocity * delta,
                currentHorizontalVelocity.Z * delta));

            if ((moveFlags & CollisionFlags.Sides) != 0)
                SmoothVelocityAgainstWall(delta);

            UpdateAnimator(inputMagnitude);
            UpdateCameraFollow(delta);
        }}

        private void UpdateCameraInput(float delta, float inputMagnitude)
        {{
            bool canRotateCamera = Input.CursorLocked && (!HoldRightMouseToRotateCamera || Input.GetMouseButton(MouseButton.Right));
            bool manualCamera = canRotateCamera && (MathF.Abs(Input.MouseDelta.X) > 0.001f || MathF.Abs(Input.MouseDelta.Y) > 0.001f);
            if (manualCamera)
            {{
                float yawDirection = InvertCameraX ? 1f : -1f;
                float pitchDirection = InvertCameraY ? -1f : 1f;
                desiredCameraYaw += Input.MouseDelta.X * CameraSensitivity * yawDirection;
                float pitchDelta = Input.MouseDelta.Y * CameraSensitivity * CameraVerticalSensitivityScale * pitchDirection;
                desiredCameraPitch = Clamp(desiredCameraPitch + pitchDelta, CameraPitchMin, CameraPitchMax);
                timeSinceManualCamera = 0f;
            }}
            else
            {{
                timeSinceManualCamera += delta;
            }}

            if (AutoAlignCameraToMovement && inputMagnitude > 0.15f && timeSinceManualCamera >= CameraRecenteringDelay)
            {{
                desiredCameraYaw = SmoothAngle(desiredCameraYaw, gameObject.RotY, CameraRecenteringSharpness, delta);
            }}

            cameraYaw = SmoothAngle(cameraYaw, desiredCameraYaw, CameraOrbitSharpness, delta);
            cameraPitch = Lerp(cameraPitch, desiredCameraPitch, Exp01(CameraOrbitSharpness, delta));
        }}

        private void UpdateMouseLock()
        {{
            if (Input.GetKeyDown(KeyCode.Escape))
            {{
                Input.UnlockCursor();
                return;
            }}

            if (!Input.CursorLocked &&
                RebloquearMouseConClick &&
                (Input.GetMouseButtonDown(MouseButton.Left) || Input.GetMouseButtonDown(MouseButton.Right)))
            {{
                Input.LockCursor();
            }}
        }}


        private void SmoothVelocityAgainstWall(float delta)
        {{
            if (controller == null)
                return;

            Vector3 n = new Vector3(controller.LastHitNormal.X, 0f, controller.LastHitNormal.Z);
            n = NormalizeSafe(n);
            if (Magnitude(n) <= 0.0001f)
                return;

            float intoWall = Dot(currentHorizontalVelocity, n);
            if (intoWall < 0f)
            {{
                Vector3 slideVelocity = currentHorizontalVelocity - n * intoWall;
                currentHorizontalVelocity = Vector3.Lerp(
                    currentHorizontalVelocity,
                    slideVelocity,
                    Exp01(WallSlideSharpness, delta));
            }}
        }}

        private void UpdateAnimator(float inputMagnitude)
        {{
            if (animator == null)
                return;

            float speedBase = MathF.Max(SprintSpeed, 0.001f);
            float normalizedSpeed = Clamp(Magnitude(currentHorizontalVelocity) / speedBase, 0f, 1f);

            animator.SetFloat(""Speed"", normalizedSpeed, 0.08f);
            animator.SetBool(""Grounded"", controller?.IsGrounded ?? false);
            animator.SetFloat(""VerticalSpeed"", verticalVelocity);
        }}

        private void UpdateCameraFollow(float delta)
        {{
            if (CameraObject == null)
                return;

            float yawRad = cameraYaw * 0.017453292f;
            float pitchRad = cameraPitch * 0.017453292f;
            float cosPitch = MathF.Cos(pitchRad);

            Vector3 lookDirection = NormalizeSafe(new Vector3(
                MathF.Sin(yawRad) * cosPitch,
                MathF.Sin(pitchRad),
                MathF.Cos(yawRad) * cosPitch));

            Vector3 lookAhead = Magnitude(currentLookAheadDirection) > 0.0001f
                ? NormalizeSafe(currentLookAheadDirection)
                : ForwardFromYaw(gameObject.RotY);
            Vector3 target = gameObject.Position + new Vector3(0f, CameraHeight, 0f) + lookAhead * CameraLookAhead;
            if (!cameraTargetInitialized)
            {{
                smoothedCameraTarget = target;
                cameraTargetInitialized = true;
            }}

            smoothedCameraTarget = Vector3.Lerp(smoothedCameraTarget, target, Exp01(CameraLookSharpness, delta));

            Vector3 shoulder = RightFromYaw(cameraYaw) * CameraShoulder;
            Vector3 desiredPosition = smoothedCameraTarget + shoulder - lookDirection * CameraDistance;

            CameraObject.Position = Vector3.Lerp(CameraObject.Position, desiredPosition, Exp01(CameraFollowSharpness, delta));
            CameraObject.RotX = -cameraPitch;
            CameraObject.RotY = cameraYaw;
            CameraObject.RotZ = 0f;
        }}

        private static float Clamp01Magnitude(ref float x, ref float y)
        {{
            float length = MathF.Sqrt(x * x + y * y);
            if (length > 1f)
            {{
                x /= length;
                y /= length;
                return 1f;
            }}

            return length;
        }}

        private static Vector3 ForwardFromYaw(float yaw)
        {{
            float r = yaw * 0.017453292f;
            return new Vector3(MathF.Sin(r), 0f, MathF.Cos(r));
        }}

        private static Vector3 RightFromYaw(float yaw)
        {{
            float r = yaw * 0.017453292f;
            return new Vector3(MathF.Cos(r), 0f, -MathF.Sin(r));
        }}

        private static Vector3 NormalizeSafe(Vector3 value)
        {{
            float length = Magnitude(value);
            return length > 0.0001f ? value / length : Vector3.Zero;
        }}

        private static float Magnitude(Vector3 value) => MathF.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

        private static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static float SmoothAngle(float current, float target, float sharpness, float delta)
        {{
            return current + DeltaAngle(current, target) * Exp01(sharpness, delta);
        }}

        private static float Lerp(float current, float target, float t) => current + (target - current) * Clamp(t, 0f, 1f);

        private static float Exp01(float sharpness, float delta) => 1f - MathF.Exp(-MathF.Max(0f, sharpness) * delta);

        private static float DeltaAngle(float current, float target)
        {{
            float delta = Repeat(target - current, 360f);
            if (delta > 180f)
                delta -= 360f;
            return delta;
        }}

        private static float Repeat(float value, float length)
        {{
            return Clamp(value - MathF.Floor(value / length) * length, 0f, length);
        }}

        private static float MoveTowards(float current, float target, float maxDelta)
        {{
            if (MathF.Abs(target - current) <= maxDelta)
                return target;
            return current + MathF.Sign(target - current) * maxDelta;
        }}

        private static float Clamp(float value, float min, float max)
        {{
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }}
    }}
}}
";

            File.WriteAllText(filePath, template);
            Log($"[ScriptCompiler] Player Controller Pro creado: {finalName}.cs", false);
            return filePath;
        }

        // =====================================================
        // BUSCAR TIPO
        // =====================================================
        public Type? FindTypeByName(string className) =>
            tiposCompilados.Find(t =>
                string.Equals(t.FullName, className, StringComparison.Ordinal) ||
                string.Equals(t.Name,     className, StringComparison.Ordinal));

        public Type? FindScriptableObjectType(string className) =>
            tiposScriptableObjects.Find(t =>
                string.Equals(t.FullName, className, StringComparison.Ordinal) ||
                string.Equals(t.Name,     className, StringComparison.Ordinal));

        private void Log(string message, bool isError) =>
            OnLog?.Invoke(message, isError);

        public void Dispose()
        {
            tiposCompilados.Clear();
            tiposScriptableObjects.Clear();
            UltimoEnsamblado    = null;
            cachedReferences    = null;
            cachedAssemblyCount = 0;
            successfulScriptSignature = null;
            scriptLoadContext?.Unload();
            scriptLoadContext = null;
            // Necesario para liberar de verdad el ALC coleccionable (ver Compilar()).
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
