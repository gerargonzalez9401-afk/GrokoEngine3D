using System;
using System.Drawing;
using System.IO;
using System.Linq;
using GrokoShaderGraphPro.Services;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace GrokoEngine.ImGuiEditor;

/// <summary>
/// Optional GPU smoke test. It is intentionally a separate process because CI
/// agents without an OpenGL driver must not invalidate deterministic tests.
/// </summary>
internal static class GpuVisualRegression
{
    /// <summary>
    /// Rinde una escena guardada en el mismo contexto oculto que las regresiones GPU,
    /// pero fuerza Forward+. Es un diagnóstico de proyecto real, no un mock de cubos.
    /// </summary>
    public static int RunForwardPlusProjectScene(string scenePath)
        => RunProjectScene(scenePath, RendererRenderingPath.ForwardPlus, "FORWARD_PLUS");

    public static int RunDeferredProjectScene(string scenePath)
        => RunProjectScene(scenePath, RendererRenderingPath.Deferred, "DEFERRED");

    private static int RunProjectScene(string scenePath, RendererRenderingPath renderingPath, string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
                throw new FileNotFoundException("No se encontró la escena para probar Forward+.", scenePath);

            var settings = new NativeWindowSettings
            {
                StartVisible = false,
                StartFocused = false,
                ClientSize = new OpenTK.Mathematics.Vector2i(512, 288),
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                Title = "GrokoEngine Forward+ Scene QA"
            };
            using var window = new OpenTK.Windowing.Desktop.NativeWindow(settings);
            window.MakeCurrent();

            string assets = GrokoEngine.SceneSerializer.InferAssetsRoot(scenePath) ?? Path.GetDirectoryName(scenePath) ?? "";
            using var scripts = new GrokoEngine.ScriptCompiler(assets);
            var objects = GrokoEngine.SceneSerializer.Load(scenePath, new GrokoEngine.PhysicsEngine(), scripts);
            var cameraObject = Flatten(objects).FirstOrDefault(o => o.IsActive && o.IsCamera);
            var cameraComponent = cameraObject?.GetComponent<GrokoEngine.Camera>();
            if (cameraObject == null || cameraComponent == null)
                throw new InvalidOperationException("La escena no tiene una cámara activa para el diagnóstico Forward+.");

            var camera = BuildSceneCamera(cameraObject, cameraComponent, 512f / 288f);
            var clear = Color.FromArgb(255,
                (int)Math.Clamp(cameraComponent.BackgroundR * 255f, 0f, 255f),
                (int)Math.Clamp(cameraComponent.BackgroundG * 255f, 0f, 255f),
                (int)Math.Clamp(cameraComponent.BackgroundB * 255f, 0f, 255f));
            using var target = new SceneRenderTarget { AntiAliasingMode = cameraComponent.AntiAliasingMode };
            using var renderer = new SceneViewportRenderer
            {
                RenderingPath = renderingPath,
                RenderRealtimeShadows = cameraComponent.RenderShadows,
                ShadowQuality = ShadowQuality.High
            };
            renderer.RenderToTarget(target, objects, null, camera, 512, 288, clear);
            target.ApplyPostProcess(cameraComponent.PostProcessing ? FindFirstComponent<GrokoEngine.PostProcessSettings>(objects) : null);
            using Bitmap image = target.CapturePostProcessBitmap();
            ImageMetrics metrics = ImageMetrics.From(image);
            SaveArtifactIfRequested(image, "project-forward-plus");
            var stats = renderer.LastStats;
            Console.WriteLine($"GPU_PROJECT_STATS={label} draws={stats.DrawCalls} opaqueDynamic={stats.OpaqueDynamicMeshes} transparentDynamic={stats.TransparentDynamicMeshes} staticRanges={stats.StaticRanges} dynamicRanges={stats.DynamicRanges}");
            if (metrics.LitPixels < image.Width * image.Height / 20 || metrics.MaxLuma - metrics.MinLuma < 6)
                throw new InvalidOperationException($"{label} de la escena quedó negro o sin contraste (lit={metrics.LitPixels}, contrast={metrics.MaxLuma - metrics.MinLuma}).");

            Console.WriteLine($"GPU_PROJECT_{label}=PASS scene={Path.GetFileName(scenePath)} lit={metrics.LitPixels} contrast={metrics.MaxLuma - metrics.MinLuma} rgb={metrics.RedMean:F1},{metrics.GreenMean:F1},{metrics.BlueMean:F1}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GPU_PROJECT_{label}=FAIL " + ex.Message);
            return 1;
        }
    }

    public static int Run(bool updateBaselines = false)
    {
        try
        {
            var settings = new NativeWindowSettings
            {
                StartVisible = false,
                StartFocused = false,
                ClientSize = new OpenTK.Mathematics.Vector2i(128, 128),
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                Title = "GrokoEngine GPU Visual Regression"
            };
            using var window = new OpenTK.Windowing.Desktop.NativeWindow(settings);
            window.MakeCurrent();

            var generator = new ShaderCodeGenerator();
            using var preview = new ShaderGraphPreview();
            RunShaderCase(preview, generator, "shadergraph-lava", GraphTemplates.Lava(), warmExpected: true, updateBaselines);
            RunShaderCase(preview, generator, "shadergraph-portal", GraphTemplates.Portal(), warmExpected: false, updateBaselines);
            RunHdrPostProcessCase(updateBaselines);
            RunDirectionalShadowScene(updateBaselines);
            RunLocalLightShadowScene();
            RunForwardLightingScene(RendererRenderingPath.Forward, "forward-lighting");
            RunForwardLightingScene(RendererRenderingPath.ForwardPlus, "forward-plus-lighting");
            Console.WriteLine("GPU_VISUAL_SMOKE=PASS shadergraph-lava shadergraph-portal hdr-postprocess directional-shadows local-light-shadows forward-lighting forward-plus-lighting 128x128");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("GPU_VISUAL_SMOKE=FAIL " + ex.Message);
            return 1;
        }
    }

    private static void RunShaderCase(ShaderGraphPreview preview, ShaderCodeGenerator generator, string name,
        GrokoShaderGraphPro.Models.ShaderGraphModel graph, bool warmExpected, bool updateBaselines)
    {
        string vertex = generator.GenerateVertexShader();
        string fragment = generator.GenerateFragmentShader(graph);
        if (!preview.SetShader(vertex, fragment))
            throw new InvalidOperationException($"{name} did not compile: {preview.CompileError}");

        preview.Resize(128, 128);
        preview.Render(graph, fragment, 0.6f, -0.25f, 1.25f, 128, 128);
        using Bitmap image = preview.CaptureBitmap();
        ImageMetrics metrics = ImageMetrics.From(image);
        if (metrics.LitPixels < image.Width * image.Height / 12)
            throw new InvalidOperationException($"{name} rendered too few non-background pixels ({metrics.LitPixels}).");
        if (warmExpected ? metrics.RedMean <= metrics.BlueMean || metrics.RedMean <= metrics.GreenMean
                         : metrics.BlueMean <= metrics.RedMean)
            throw new InvalidOperationException($"{name} color profile is outside its regression tolerance.");

        SaveArtifactIfRequested(image, name);
        VerifyBaseline(image, name, updateBaselines);
        Console.WriteLine($"GPU_VISUAL_CASE=PASS {name} lit={metrics.LitPixels} rgb={metrics.RedMean:F1},{metrics.GreenMean:F1},{metrics.BlueMean:F1}");
    }

    private static void SaveArtifactIfRequested(Bitmap image, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("GROKOENGINE_GPU_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        image.Save(Path.Combine(directory, name + ".png"));
    }

    private static void RunHdrPostProcessCase(bool updateBaselines)
    {
        using var target = new SceneRenderTarget();
        target.Resize(128, 128);
        target.AntiAliasingMode = GrokoEngine.CameraAntiAliasingMode.SMAA;
        target.Bind();
        OpenTK.Graphics.OpenGL4.GL.ClearColor(8f, 1.2f, 0.15f, 1f);
        OpenTK.Graphics.OpenGL4.GL.Clear(OpenTK.Graphics.OpenGL4.ClearBufferMask.ColorBufferBit | OpenTK.Graphics.OpenGL4.ClearBufferMask.DepthBufferBit);
        target.ApplyPostProcess(new GrokoEngine.PostProcessSettings
        {
            PostProcessEnabled = true,
            ToneMapping = true,
            Exposure = 1.1f,
            Bloom = true,
            BloomStrength = 0.4f,
            BloomThreshold = 0.5f,
            AutoExposure = true,
            AutoExposureMinEV = -4f,
            AutoExposureMaxEV = 4f,
            AutoExposureAdaptationSpeed = 12f
        });
        using Bitmap image = target.CapturePostProcessBitmap();
        ImageMetrics metrics = ImageMetrics.From(image);
        if (metrics.LitPixels < 128 * 128 * 9 / 10 || metrics.RedMean < 80 || metrics.RedMean <= metrics.BlueMean)
            throw new InvalidOperationException("HDR postprocess output is invalid or lost its expected warm exposure.");
        SaveArtifactIfRequested(image, "hdr-postprocess");
        VerifyBaseline(image, "hdr-postprocess", updateBaselines);
        Console.WriteLine($"GPU_VISUAL_CASE=PASS hdr-postprocess lit={metrics.LitPixels} rgb={metrics.RedMean:F1},{metrics.GreenMean:F1},{metrics.BlueMean:F1}");
    }

    private static void RunDirectionalShadowScene(bool updateBaselines)
    {
        string root = Path.Combine(Path.GetTempPath(), "GrokoEngineGpuVisual", Guid.NewGuid().ToString("N"), "Assets");
        try
        {
            string cube = PrimitiveMeshFactory.EnsurePrimitiveMesh(root, EditorPrimitiveMeshKind.Cube);
            string plane = PrimitiveMeshFactory.EnsurePrimitiveMesh(root, EditorPrimitiveMeshKind.Plane);
            var floor = CreateMeshObject("Floor", plane, new MiMotor.Mathematics.Vector3(0, 0, 4), new MiMotor.Mathematics.Vector3(9, 1, 9));
            var nearCube = CreateMeshObject("NearCaster", cube, new MiMotor.Mathematics.Vector3(-1.4f, 1, 3), new MiMotor.Mathematics.Vector3(1.5f, 2, 1.5f));
            var farCube = CreateMeshObject("FarCaster", cube, new MiMotor.Mathematics.Vector3(2.5f, 0.7f, 12), new MiMotor.Mathematics.Vector3(1.1f, 1.4f, 1.1f));
            var sunObject = new GrokoEngine.GameObject { Name = "Sun" };
            var sun = sunObject.AddComponent<GrokoEngine.DirectionalLight>();
            sun.Direction = new MiMotor.Mathematics.Vector3(-0.45f, -1f, -0.35f);
            sun.Shadows = true;
            sun.ShadowStrength = 1f;
            sun.ShadowNearPlane = 0.2f;

            var camera = new ImGuiEditorApp.EditorCameraState
            {
                Position = new MiMotor.Mathematics.Vector3(0, 4.5f, -7),
                Front = new MiMotor.Mathematics.Vector3(0, -0.22f, 1).Normalized(),
                FarClip = 100,
                FOV = 58,
                AntiAliasing = false,
                FrustumCulling = false
            };
            using var target = new SceneRenderTarget();
            using var renderer = new SceneViewportRenderer { RenderRealtimeShadows = true, ShadowQuality = ShadowQuality.High };
            renderer.DrawEnvironmentBackground = false;
            // Ejercita el G-buffer, iluminación deferred y sus shaders de reflexión/fog;
            // la prueba Forward queda cubierta por los casos ShaderGraph de arriba.
            renderer.RenderingPath = RendererRenderingPath.Deferred;
            renderer.RenderToTarget(target, new[] { floor, nearCube, farCube, sunObject }, null, camera, 256, 256, Color.FromArgb(255, 20, 28, 36));
            target.ApplyPostProcess(new GrokoEngine.PostProcessSettings
            {
                PostProcessEnabled = true,
                ToneMapping = true,
                Exposure = 1f,
                AmbientOcclusion = true,
                AmbientOcclusionStrength = 0.35f,
                ScreenSpaceGlobalIllumination = true,
                ScreenSpaceGiIntensity = 0.18f,
                ScreenSpaceGiRadius = 1.2f
            });
            using Bitmap image = target.CapturePostProcessBitmap();
            ImageMetrics metrics = ImageMetrics.From(image);
            SaveArtifactIfRequested(image, "directional-shadows");
            VerifyBaseline(image, "directional-shadows", updateBaselines);
            if (metrics.LitPixels < 256 * 256 / 3 || metrics.MaxLuma - metrics.MinLuma < 12)
                throw new InvalidOperationException($"Directional shadow scene lacks the expected lit/shadow contrast (lit={metrics.LitPixels}, range={metrics.MaxLuma - metrics.MinLuma}).");
            Console.WriteLine($"GPU_VISUAL_CASE=PASS directional-shadows contrast={metrics.MaxLuma - metrics.MinLuma}");
        }
        finally
        {
            string project = Directory.GetParent(root)!.FullName;
            if (Directory.Exists(project)) Directory.Delete(project, recursive: true);
        }
    }

    // Regression específica para el fallo que dejó los mapas Point/Spot vacíos: se compara
    // exactamente la misma escena con la sombra local apagada y encendida. No basta con
    // comprobar que el shader compile; debe oscurecer píxeles reales del suelo tras el cubo.
    private static void RunLocalLightShadowScene()
    {
        string root = Path.Combine(Path.GetTempPath(), "GrokoEngineGpuVisual", Guid.NewGuid().ToString("N"), "Assets");
        try
        {
            string cube = PrimitiveMeshFactory.EnsurePrimitiveMesh(root, EditorPrimitiveMeshKind.Cube);
            string plane = PrimitiveMeshFactory.EnsurePrimitiveMesh(root, EditorPrimitiveMeshKind.Plane);
            var floor = CreateMeshObject("Floor", plane, new MiMotor.Mathematics.Vector3(0, 0, 4), new MiMotor.Mathematics.Vector3(10, 1, 10));
            var caster = CreateMeshObject("Caster", cube, new MiMotor.Mathematics.Vector3(0, 1, 4), new MiMotor.Mathematics.Vector3(1.6f, 2f, 1.6f));
            var lightObject = new GrokoEngine.GameObject { Name = "Point Shadow Test", Position = new MiMotor.Mathematics.Vector3(-2.4f, 4f, 2.2f) };
            var light = lightObject.AddComponent<GrokoEngine.PointLight>();
            light.Intensity = 2.6f; light.Range = 10f; light.ShadowStrength = 1f;

            var camera = new ImGuiEditorApp.EditorCameraState
            {
                Position = new MiMotor.Mathematics.Vector3(0, 3.4f, -6.5f),
                Front = new MiMotor.Mathematics.Vector3(0, -0.18f, 1).Normalized(),
                FarClip = 50, FOV = 56, AntiAliasing = false, FrustumCulling = false
            };

            using var target = new SceneRenderTarget();
            using var renderer = new SceneViewportRenderer { RenderRealtimeShadows = true, ShadowQuality = ShadowQuality.High, RenderingPath = RendererRenderingPath.Deferred };
            renderer.DrawEnvironmentBackground = false;
            light.Shadows = false;
            renderer.RenderToTarget(target, new[] { floor, caster, lightObject }, null, camera, 256, 256, Color.FromArgb(255, 20, 20, 20));
            target.ApplyPostProcess(new GrokoEngine.PostProcessSettings { PostProcessEnabled = true, ToneMapping = true, Exposure = 1f });
            using var unshadowed = target.CapturePostProcessBitmap();
            SaveArtifactIfRequested(unshadowed, "local-light-unshadowed");

            light.Shadows = true;
            renderer.RenderToTarget(target, new[] { floor, caster, lightObject }, null, camera, 256, 256, Color.FromArgb(255, 20, 20, 20));
            var shadowStats = renderer.LastStats;
            target.ApplyPostProcess(new GrokoEngine.PostProcessSettings { PostProcessEnabled = true, ToneMapping = true, Exposure = 1f });
            using var shadowed = target.CapturePostProcessBitmap();
            SaveArtifactIfRequested(shadowed, "local-light-shadows");

            long difference = 0;
            for (int y = 0; y < shadowed.Height; y++)
            for (int x = 0; x < shadowed.Width; x++)
            {
                Color a = unshadowed.GetPixel(x, y), b = shadowed.GetPixel(x, y);
                difference += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            }
            double meanDifference = difference / (double)(shadowed.Width * shadowed.Height * 3);
            if (meanDifference < 0.30)
                throw new InvalidOperationException($"Local point-light shadow map did not darken the receiver (mean RGB delta={meanDifference:F3}, pointMs={shadowStats.PointShadowMs:F3}, shadowDraws={shadowStats.ShadowDrawCalls}).");
            Console.WriteLine($"GPU_VISUAL_CASE=PASS local-light-shadows mean-delta={meanDifference:F3}");
        }
        finally
        {
            string project = Directory.GetParent(root)!.FullName;
            if (Directory.Exists(project)) Directory.Delete(project, recursive: true);
        }
    }

    // Forward y Forward+ deben conservar la misma base de iluminación con MSAA. La escena
    // contiene Point + Spot con sombras: si un atlas de profundidad queda enlazado al terminar
    // las sombras, el resultado se queda en el clear color azul y esta prueba falla.
    private static void RunForwardLightingScene(RendererRenderingPath renderingPath, string caseName)
    {
        string root = Path.Combine(Path.GetTempPath(), "GrokoEngineGpuVisual", Guid.NewGuid().ToString("N"), "Assets");
        try
        {
            string cube = PrimitiveMeshFactory.EnsurePrimitiveMesh(root, EditorPrimitiveMeshKind.Cube);
            string plane = PrimitiveMeshFactory.EnsurePrimitiveMesh(root, EditorPrimitiveMeshKind.Plane);
            var floor = CreateMeshObject("ForwardPlusFloor", plane, new MiMotor.Mathematics.Vector3(0, 0, 4), new MiMotor.Mathematics.Vector3(10, 1, 10));
            var cubeObject = CreateMeshObject("ForwardPlusCaster", cube, new MiMotor.Mathematics.Vector3(0, 1, 4), new MiMotor.Mathematics.Vector3(1.6f, 2f, 1.6f));
            // La casa del proyecto es estática; ejercita el batch estático de Forward+,
            // no solo la ruta dinámica del caso de prueba anterior.
            floor.IsStatic = true;
            cubeObject.IsStatic = true;
            var lightObject = new GrokoEngine.GameObject { Name = "ForwardPlusKey", Position = new MiMotor.Mathematics.Vector3(-2.4f, 4f, 2.2f) };
            var light = lightObject.AddComponent<GrokoEngine.PointLight>();
            light.Intensity = 2.6f; light.Range = 10f; light.Shadows = true; light.ShadowStrength = 0.9f;
            var spotObject = new GrokoEngine.GameObject
            {
                Name = "ForwardSpotShadowState",
                Position = new MiMotor.Mathematics.Vector3(0f, 4.5f, 4f),
                RotX = 90f
            };
            var spot = spotObject.AddComponent<GrokoEngine.SpotLight>();
            spot.Intensity = 0.8f; spot.Range = 8f; spot.Angle = 80f; spot.Shadows = true; spot.ShadowStrength = 0.75f;
            var camera = new ImGuiEditorApp.EditorCameraState
            {
                Position = new MiMotor.Mathematics.Vector3(0, 3.4f, -6.5f),
                Front = new MiMotor.Mathematics.Vector3(0, -0.18f, 1).Normalized(),
                FarClip = 50, FOV = 56, AntiAliasing = true, AntiAliasingSamples = 4, FrustumCulling = false
            };

            using var target = new SceneRenderTarget { AntiAliasingMode = GrokoEngine.CameraAntiAliasingMode.SMAA };
            using var renderer = new SceneViewportRenderer { RenderRealtimeShadows = true, ShadowQuality = ShadowQuality.High, RenderingPath = renderingPath };
            renderer.DrawEnvironmentBackground = false;
            renderer.RenderToTarget(target, new[] { floor, cubeObject, lightObject, spotObject }, null, camera, 256, 256, Color.FromArgb(255, 20, 20, 20));
            target.ApplyPostProcess(new GrokoEngine.PostProcessSettings { PostProcessEnabled = true, ToneMapping = true, Exposure = 1f });
            using var image = target.CapturePostProcessBitmap();
            ImageMetrics metrics = ImageMetrics.From(image);
            SaveArtifactIfRequested(image, caseName);
            if (metrics.LitPixels < 256 * 256 / 4 || metrics.MaxLuma - metrics.MinLuma < 12)
                throw new InvalidOperationException($"{caseName} output is black, blue-clear, or lacks contrast (lit={metrics.LitPixels}, contrast={metrics.MaxLuma - metrics.MinLuma}).");
            Console.WriteLine($"GPU_VISUAL_CASE=PASS {caseName} lit={metrics.LitPixels} contrast={metrics.MaxLuma - metrics.MinLuma}");
        }
        finally
        {
            string project = Directory.GetParent(root)!.FullName;
            if (Directory.Exists(project)) Directory.Delete(project, recursive: true);
        }
    }

    // Forward+ debe conservar la irradiancia horneada de los MeshRenderer estáticos,
    // igual que Deferred. Este caso no pone luces ni ambiente: si el lightmap se pierde,
    // la imagen queda negra y el test falla exactamente como una casa real.
    private static void RunForwardPlusLightmapScene()
    {
        string root = Path.Combine(Path.GetTempPath(), "GrokoEngineGpuVisual", Guid.NewGuid().ToString("N"), "Assets");
        try
        {
            Directory.CreateDirectory(root);
            string lightmap = Path.Combine(root, "forward-plus-lightmap.bmp");
            using (var baked = new Bitmap(16, 16))
            using (var graphics = Graphics.FromImage(baked))
            {
                graphics.Clear(Color.FromArgb(255, 186, 164, 138));
                baked.Save(lightmap);
            }

            // Primitiva directa (sin material importado que pueda tener albedo negro):
            // así el caso mide únicamente si la irradiancia horneada llega al shader.
            var cubeObject = new GrokoEngine.GameObject
            {
                Name = "ForwardPlusLightmappedCube",
                Type = 1,
                Position = new MiMotor.Mathematics.Vector3(0, 1, 4),
                ScaleX = 1.6f,
                ScaleY = 2f,
                ScaleZ = 1.6f
            };
            cubeObject.IsStatic = true;
            var meshRenderer = cubeObject.AddComponent<GrokoEngine.MeshRenderer>();
            meshRenderer.ReceiveGlobalIllumination = 1;
            meshRenderer.LightmapPath = lightmap;
            meshRenderer.LightmapIntensity = 1f;
            var material = cubeObject.AddComponent<GrokoEngine.Material>();
            material.R = material.G = material.B = 1f;
            var camera = new ImGuiEditorApp.EditorCameraState
            {
                Position = new MiMotor.Mathematics.Vector3(0, 3.4f, -6.5f),
                Front = new MiMotor.Mathematics.Vector3(0, -0.18f, 1).Normalized(),
                FarClip = 30, FOV = 56, AntiAliasing = true, AntiAliasingSamples = 4, FrustumCulling = false
            };

            using var target = new SceneRenderTarget { AntiAliasingMode = GrokoEngine.CameraAntiAliasingMode.SMAA };
            using var renderer = new SceneViewportRenderer { RenderingPath = RendererRenderingPath.ForwardPlus, RenderRealtimeShadows = true, ShadowQuality = ShadowQuality.Ultra };
            renderer.DrawEnvironmentBackground = false;
            renderer.RenderToTarget(target, new[] { cubeObject }, null, camera, 128, 128, Color.FromArgb(255, 3, 3, 3));
            target.ApplyPostProcess(new GrokoEngine.PostProcessSettings { PostProcessEnabled = true, ToneMapping = true, Exposure = 1f });
            using Bitmap image = target.CapturePostProcessBitmap();
            ImageMetrics metrics = ImageMetrics.From(image);
            SaveArtifactIfRequested(image, "forward-plus-lightmap");
            if (metrics.LitPixels < image.Width * image.Height / 8 || metrics.RedMean < 28)
                throw new InvalidOperationException($"Forward+ lost baked lightmap irradiance (lit={metrics.LitPixels}, red={metrics.RedMean:F1}, staticRanges={renderer.LastStats.StaticRanges}, textures={renderer.LastStats.TextureCacheCount}).");
            Console.WriteLine($"GPU_VISUAL_CASE=PASS forward-plus-lightmap lit={metrics.LitPixels} red={metrics.RedMean:F1}");
        }
        finally
        {
            string project = Directory.GetParent(root)!.FullName;
            if (Directory.Exists(project)) Directory.Delete(project, recursive: true);
        }
    }

    private static GrokoEngine.GameObject CreateMeshObject(string name, string meshPath, MiMotor.Mathematics.Vector3 position, MiMotor.Mathematics.Vector3 scale)
    {
        var obj = new GrokoEngine.GameObject { Name = name, Position = position, ScaleX = scale.X, ScaleY = scale.Y, ScaleZ = scale.Z };
        obj.AddComponent<GrokoEngine.MeshFilter>().MeshPath = meshPath;
        obj.AddComponent<GrokoEngine.MeshRenderer>();
        obj.AddComponent<GrokoEngine.Material>();
        return obj;
    }

    private static ImGuiEditorApp.EditorCameraState BuildSceneCamera(GrokoEngine.GameObject source, GrokoEngine.Camera component, float aspect)
    {
        var camera = new ImGuiEditorApp.EditorCameraState
        {
            Position = new MiMotor.Mathematics.Vector3(source.PosX, source.PosY, source.PosZ),
            FOV = Math.Clamp(component.GetEffectiveVerticalFov(aspect), 5f, 170f),
            NearClip = component.NearClip,
            FarClip = component.FarClip,
            Orthographic = component.Projection == GrokoEngine.CameraProjection.Orthographic,
            OrthoSize = component.OrthographicSize,
            Yaw = 90f - source.RotY,
            Pitch = Math.Clamp(-source.RotX, -89f, 89f),
            AntiAliasing = component.AntiAliasing && component.AntiAliasingMode != GrokoEngine.CameraAntiAliasingMode.None,
            AntiAliasingSamples = component.AntiAliasingSamples,
            FrustumCulling = component.FrustumCulling,
            OcclusionCulling = component.OcclusionCulling,
            CullingMask = GrokoEngine.LayerMask.ParseMask(component.CullingMask)
        };
        camera.UpdateFront();
        return camera;
    }

    private static T? FindFirstComponent<T>(IReadOnlyList<GrokoEngine.GameObject> objects) where T : GrokoEngine.Component
        => Flatten(objects).Select(o => o.GetComponent<T>()).FirstOrDefault(c => c != null);

    private static IEnumerable<GrokoEngine.GameObject> Flatten(IEnumerable<GrokoEngine.GameObject> objects)
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;
            yield return obj;
            foreach (var child in Flatten(obj.Children))
                yield return child;
        }
    }

    private static void VerifyBaseline(Bitmap actual, string name, bool update)
    {
        string? directory = Environment.GetEnvironmentVariable("GROKOENGINE_GPU_BASELINES");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        string baselinePath = Path.Combine(directory, name + ".png");
        if (update || !File.Exists(baselinePath))
        {
            actual.Save(baselinePath);
            Console.WriteLine($"GPU_VISUAL_BASELINE={(update ? "UPDATED" : "CREATED")} {name}");
            return;
        }

        using var baseline = new Bitmap(baselinePath);
        if (baseline.Width != actual.Width || baseline.Height != actual.Height)
            throw new InvalidOperationException($"Baseline dimensions differ for {name}.");
        long error = 0;
        int pixels = actual.Width * actual.Height;
        for (int y = 0; y < actual.Height; y++)
        for (int x = 0; x < actual.Width; x++)
        {
            Color a = actual.GetPixel(x, y), b = baseline.GetPixel(x, y);
            error += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        }
        double meanError = error / (double)(pixels * 3);
        if (meanError > 18d)
            throw new InvalidOperationException($"Visual baseline mismatch for {name}: mean RGB error {meanError:F2} exceeds 18.00.");
    }

    private readonly record struct ImageMetrics(int LitPixels, double RedMean, double GreenMean, double BlueMean, int MinLuma, int MaxLuma)
    {
        public static ImageMetrics From(Bitmap image)
        {
            long red = 0, green = 0, blue = 0;
            int litPixels = 0;
            int minLuma = 255, maxLuma = 0;
            const int background = 30;
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                Color pixel = image.GetPixel(x, y);
                red += pixel.R;
                green += pixel.G;
                blue += pixel.B;
                if (pixel.R + pixel.G + pixel.B > background * 3 + 20)
                    litPixels++;
                int luma = (pixel.R * 54 + pixel.G * 183 + pixel.B * 19) / 256;
                minLuma = Math.Min(minLuma, luma);
                maxLuma = Math.Max(maxLuma, luma);
            }

            int pixels = image.Width * image.Height;
            return new ImageMetrics(litPixels, red / (double)pixels, green / (double)pixels, blue / (double)pixels, minLuma, maxLuma);
        }
    }
}
