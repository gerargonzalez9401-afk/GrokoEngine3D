using System.Collections.Generic;
using System.IO;
using MiMotor.Mathematics;

namespace GrokoEngine.ImGuiEditor;

internal static class DirectionalShadowVisualScene
{
    public static string CreateProject()
    {
        string project = Path.Combine(Path.GetTempPath(), "GrokoEngineVisualScenes", "DirectionalShadows");
        string assets = Path.Combine(project, "Assets");
        string scenes = Path.Combine(assets, "Scenes");
        Directory.CreateDirectory(scenes);

        string cube = PrimitiveMeshFactory.EnsurePrimitiveMesh(assets, EditorPrimitiveMeshKind.Cube);
        string plane = PrimitiveMeshFactory.EnsurePrimitiveMesh(assets, EditorPrimitiveMeshKind.Plane);
        string sphere = PrimitiveMeshFactory.EnsurePrimitiveMesh(assets, EditorPrimitiveMeshKind.Sphere);
        // Habitación cerrada: la pared frontal queda detrás de la cámara para conservar una vista interior clara.
        var floor = Mesh("Room Floor", cube, new Vector3(0, -0.15f, 6), new Vector3(15, 0.3f, 18), 0.035f, 0.045f, 0.065f, 0.45f, 0.22f);
        var ceiling = Mesh("Room Ceiling", cube, new Vector3(0, 7.1f, 6), new Vector3(15, 0.25f, 18), 0.025f, 0.03f, 0.05f, 0.25f, 0.45f);
        var leftWall = Mesh("Room Left Wall", cube, new Vector3(-7.5f, 3.5f, 6), new Vector3(0.3f, 7f, 18), 0.055f, 0.075f, 0.115f, 0.18f, 0.52f);
        var rightWall = Mesh("Room Right Wall", cube, new Vector3(7.5f, 3.5f, 6), new Vector3(0.3f, 7f, 18), 0.07f, 0.035f, 0.025f, 0.25f, 0.48f);
        var rearWall = Mesh("Room Rear Wall", cube, new Vector3(0, 3.5f, 14.8f), new Vector3(15, 7f, 0.3f), 0.025f, 0.04f, 0.075f, 0.35f, 0.38f);
        var frontWall = Mesh("Room Front Wall", cube, new Vector3(0, 3.5f, -2.8f), new Vector3(15, 7f, 0.3f), 0.03f, 0.04f, 0.06f, 0.32f, 0.4f);
        var hero = Mesh("Hero Sphere", sphere, new Vector3(0, 1.65f, 6.8f), new Vector3(2.4f, 2.4f, 2.4f), 0.055f, 0.35f, 0.56f, 0.55f, 0.18f);
        var near = Mesh("Warm Sculpture Cube", cube, new Vector3(-3.9f, 1.15f, 5.2f), new Vector3(1.7f, 2.3f, 1.7f), 0.42f, 0.055f, 0.02f, 0.62f, 0.12f);
        var far = Mesh("Cool Pedestal", cube, new Vector3(3.8f, 0.9f, 11.6f), new Vector3(1.5f, 1.8f, 1.5f), 0.02f, 0.12f, 0.42f, 0.5f, 0.2f);
        var accentA = Mesh("Warm Accent Sphere", sphere, new Vector3(-4.7f, 0.78f, 10.3f), new Vector3(0.85f, 0.85f, 0.85f), 0.8f, 0.04f, 0.015f, 0.25f, 0.22f);
        var accentB = Mesh("Blue Accent Sphere", sphere, new Vector3(4.8f, 0.78f, 8.9f), new Vector3(0.85f, 0.85f, 0.85f), 0.01f, 0.22f, 0.95f, 0.2f, 0.28f);
        var warmStrip = EmissiveMesh("Warm Emissive Strip", cube, new Vector3(-7.15f, 3.6f, 6), new Vector3(0.08f, 0.8f, 8.5f), 1f, 0.09f, 0.015f, 6f);
        var cyanStrip = EmissiveMesh("Cyan Emissive Strip", cube, new Vector3(7.15f, 4.2f, 9.5f), new Vector3(0.08f, 0.55f, 4.2f), 0.01f, 0.45f, 1f, 5f);
        var ceilingEmitter = EmissiveMesh("Ceiling Area Emitter", cube, new Vector3(0, 6.78f, 6), new Vector3(4.2f, 0.05f, 1.2f), 1f, 0.5f, 0.18f, 3.5f);
        // La habitación es mate: consultar el cubemap en cada píxel de las paredes no añade
        // detalle visible, pero sí desperdicia el budget de reflejos. El probe queda reservado
        // a la esfera hero y los props metálicos, donde sí aporta información visual.
        ConfigureMatteSurfaces(floor, ceiling, leftWall, rightWall, rearWall, frontWall, warmStrip, cyanStrip, ceilingEmitter);
        ConfigureStaticGi(floor, ceiling, leftWall, rightWall, rearWall, frontWall, near, far, accentA, accentB, warmStrip, cyanStrip, ceilingEmitter);
        var sunObject = new GameObject { Name = "Directional Sun" };
        var sun = sunObject.AddComponent<DirectionalLight>();
        sun.Direction = new Vector3(-0.45f, -1f, -0.35f);
        sun.Shadows = true;
        sun.ShadowStrength = 1f;
        sun.ContactShadows = true;
        sun.ContactShadowLength = 0.025f;
        sun.ContactShadowSamples = 16;
        sun.UseTemperature = true;
        sun.Temperature = 4800;
        sun.Intensity = 0.18f;
        sun.GodRays = false;

        var ambientObject = new GameObject { Name = "Cool Ambient Fill" };
        var ambient = ambientObject.AddComponent<AmbientLight>();
        ambient.R = 0.07f; ambient.G = 0.11f; ambient.B = 0.2f; ambient.Intensity = 0.16f; ambient.SkyStrength = 0.08f;
        var warmLight = Light("Warm Ceiling Key", new Vector3(-3.8f, 5.7f, 4.5f), 11f, 8f, 1f, 0.18f, 0.035f);
        var blueLight = Light("Blue Rim Light", new Vector3(4.5f, 4.6f, 10.8f), 9f, 7.5f, 0.02f, 0.26f, 1f);
        var fillLight = Light("Neutral Fill", new Vector3(0, 4.8f, 7.5f), 5.5f, 6f, 1f, 0.55f, 0.28f);
        var warmArea = AreaLight("Warm Emissive Area Light", new Vector3(-6.9f, 3.6f, 6), 6.5f, 7f, 0.35f, 3.2f, 1f, 0.1f, 0.02f);
        var cyanArea = AreaLight("Cyan Emissive Area Light", new Vector3(6.9f, 4.2f, 9.5f), 5.5f, 6f, 0.3f, 2.5f, 0.02f, 0.42f, 1f);
        var heroSpot = SpotLight("Hero Soft Spotlight", new Vector3(0, 6.5f, 4.8f), new Vector3(0, -1f, 0.35f), 8f, 12f, 42f, 1f, 0.42f, 0.16f);
        var probeObject = new GameObject { Name = "Interior Reflection and Lighting Probe", Position = new Vector3(0, 3.2f, 6) };
        var probe = probeObject.AddComponent<ReflectionProbe>();
        // 128 es el punto de equilibrio para un interior: el prefiltrado por rugosidad oculta
        // el texelado, mientras que el cubemap ocupa ~4x menos memoria que 256 y deja margen
        // estable para 120 FPS en GPUs integradas y de gama media.
        probe.SizeX = 14f; probe.SizeY = 6.5f; probe.SizeZ = 17f; probe.Intensity = 1.2f; probe.BlendDistance = 2.5f; probe.Resolution = 128; probe.BoxProjection = true; probe.HDR = true;
        var postObject = new GameObject { Name = "Interior Cinematic Post Process" };
        var post = postObject.AddComponent<PostProcessSettings>();
        post.PostProcessEnabled = true; post.Exposure = 1.04f; post.ToneMapping = true; post.Bloom = true; post.BloomStrength = 0.34f; post.BloomThreshold = 0.85f; post.BloomHighQualityFiltering = true;
        post.AmbientOcclusion = true; post.AmbientOcclusionStrength = 0.22f; post.Fog = true; post.FogDensity = 0.007f; post.FogR = 0.055f; post.FogG = 0.075f; post.FogB = 0.14f; post.VolumetricLightStrength = 0.16f;
        var camera = new GameObject { Name = "Deferred Interior Camera", IsCamera = true, Position = new Vector3(0, 2.7f, -0.9f) };
        var cameraSettings = camera.AddComponent<Camera>();
        cameraSettings.FOV = 58f; cameraSettings.NearClip = 0.05f; cameraSettings.FarClip = 50f; cameraSettings.RenderingPath = CameraRenderingPath.Deferred; cameraSettings.PostProcessing = true; cameraSettings.AntiAliasingMode = CameraAntiAliasingMode.FXAA;
        SceneSerializer.Save(Path.Combine(scenes, "Main.gscene"), new List<GameObject> { floor, ceiling, leftWall, rightWall, rearWall, frontWall, hero, near, far, accentA, accentB, warmStrip, cyanStrip, ceilingEmitter, sunObject, ambientObject, warmLight, blueLight, fillLight, warmArea, cyanArea, heroSpot, probeObject, postObject, camera });
        // Petición explícita y opt-in para esta escena temporal: el editor hornea el probe al abrir.
        File.WriteAllText(Path.Combine(project, ".groko-auto-bake-probes"), probeObject.Name);
        File.WriteAllText(Path.Combine(project, ".groko-auto-bake-lightmaps"), "256");
        return project;
    }

    private static GameObject Mesh(string name, string path, Vector3 position, Vector3 scale, float r, float g, float b, float metallic, float roughness)
    {
        var obj = new GameObject { Name = name, Position = position, ScaleX = scale.X, ScaleY = scale.Y, ScaleZ = scale.Z };
        obj.AddComponent<MeshFilter>().MeshPath = path;
        obj.AddComponent<MeshRenderer>();
        var material = obj.AddComponent<Material>();
        material.R = r; material.G = g; material.B = b; material.Metallic = metallic; material.Roughness = roughness;
        return obj;
    }

    private static GameObject Light(string name, Vector3 position, float intensity, float range, float r, float g, float b)
    {
        var obj = new GameObject { Name = name, Position = position };
        var light = obj.AddComponent<PointLight>();
        light.Intensity = intensity; light.Range = range; light.R = r; light.G = g; light.B = b; light.Shadows = true; light.ShadowStrength = 0.65f;
        return obj;
    }

    private static GameObject EmissiveMesh(string name, string path, Vector3 position, Vector3 scale, float r, float g, float b, float intensity)
    {
        var obj = Mesh(name, path, position, scale, r, g, b, 0.1f, 0.22f);
        var material = obj.GetComponent<Material>()!;
        material.EmissionR = r; material.EmissionG = g; material.EmissionB = b; material.EmissionIntensity = intensity;
        return obj;
    }

    private static GameObject AreaLight(string name, Vector3 position, float intensity, float range, float width, float height, float r, float g, float b)
    {
        var obj = new GameObject { Name = name, Position = position };
        var light = obj.AddComponent<AreaLight>();
        light.Intensity = intensity; light.Range = range; light.Width = width; light.Height = height;
        light.R = r; light.G = g; light.B = b; light.Shadows = true; light.ShadowStrength = 0.35f;
        return obj;
    }

    private static void ConfigureStaticGi(params GameObject[] objects)
    {
        foreach (var obj in objects)
        {
            obj.IsStatic = true;
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            renderer.StaticShadowCaster = true;
            renderer.ContributeGlobalIllumination = true;
            renderer.ReceiveGlobalIllumination = 1; // Lightmaps
        }
    }

    private static void ConfigureMatteSurfaces(params GameObject[] objects)
    {
        foreach (var obj in objects)
        {
            var material = obj.GetComponent<Material>();
            if (material != null)
                material.EnvironmentReflections = false;
        }
    }

    private static GameObject SpotLight(string name, Vector3 position, Vector3 direction, float intensity, float range, float angle, float r, float g, float b)
    {
        var obj = new GameObject { Name = name, Position = position };
        var light = obj.AddComponent<SpotLight>();
        light.Direction = direction.Normalized(); light.Intensity = intensity; light.Range = range; light.Angle = angle;
        light.R = r; light.G = g; light.B = b; light.Shadows = true; light.ShadowStrength = 0.5f;
        return obj;
    }
}
