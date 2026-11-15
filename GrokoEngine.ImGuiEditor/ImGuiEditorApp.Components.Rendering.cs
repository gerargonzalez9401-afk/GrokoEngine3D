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
private GameObject CreateDirectionalLight()
    {
        return CommitSceneMutation("Create Directional Light", () =>
        {
            var obj = CreateObject("Directional Light", 0);
            obj.RotX = 45f;
            obj.RotY = -35f;
            obj.AddComponent<DirectionalLight>();
            return obj;
        });
    }

private GameObject CreatePointLight()
    {
        return CommitSceneMutation("Create Point Light", () =>
        {
            var obj = CreateObject("Point Light", 0);
            obj.PosY = 2f;
            obj.AddComponent<PointLight>();
            return obj;
        });
    }

private GameObject CreateSpotLight()
    {
        return CommitSceneMutation("Create Spot Light", () =>
        {
            var obj = CreateObject("Spot Light", 0);
            obj.PosY = 2f;
            obj.RotX = 35f;
            obj.AddComponent<SpotLight>();
            return obj;
        });
    }

private GameObject CreateAmbientLight()
    {
        return CommitSceneMutation("Create Ambient Light", () =>
        {
            var obj = CreateObject("Ambient Light", 0);
            obj.AddComponent<AmbientLight>();
            return obj;
        });
    }

private GameObject CreateAreaLight()
    {
        return CommitSceneMutation("Create Area Light", () =>
        {
            var obj = CreateObject("Area Light", 0);
            obj.PosY = 2f;
            obj.AddComponent<AreaLight>();
            return obj;
        });
    }

private GameObject CreateRectangleLight()
    {
        return CommitSceneMutation("Create Rectangle Light", () =>
        {
            var obj = CreateObject("Rectangle Light", 0);
            obj.PosY = 2f;
            obj.AddComponent<RectangleLight>();
            return obj;
        });
    }

private async Task BakeLightmapsAsync(int? resolutionOverride = null)
    {
        if (_isBaking) return;

        if (!lightmapBakedGlobalIllumination)
        {
            statusMessage = "Bake cancelado: Baked Global Illumination está desactivado.";
            lightmapLastLog = statusMessage;
            lightmapLastProgress = 0f;
            return;
        }

        // Verificar que hay objetos estáticos con mesh
        int staticCount = CountStaticMeshes(objects);
        if (staticCount == 0)
        {
            statusMessage = "Bake cancelado: no hay objetos con Static=true y MeshFilter en la escena.";
            lightmapLastLog = statusMessage;
            lightmapLastProgress = 0f;
            return;
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            statusMessage = "Bake cancelado: projectPath vacío. Abre un proyecto primero.";
            lightmapLastLog = statusMessage;
            lightmapLastProgress = 0f;
            return;
        }

        // Bug fix: set _isBaking inside the try/finally so it is always reset even if
        // the code between the flag and the try block throws unexpectedly.
        try
        {
            _isBaking = true;
            int bakeResolution = Math.Clamp(resolutionOverride ?? CurrentLightmapResolution, 256, 4096);
            lightmapLastProgress = 0f;
            lightmapLastLog = $"Preparando bake de {staticCount} objetos estáticos a {lightmapResolution} texels/u (máximo {bakeResolution}px).";
            statusMessage = lightmapLastLog;

            string outputDir = System.IO.Path.Combine(projectPath, "Assets", "Lightmaps");

            var baker = new LightmapBaker(projectPath);
            // El baker carga las mallas y captura los transforms en el hilo
            // principal (fase 1) y solo el cálculo puro de CPU corre en Task.Run
            // (fase 2), así que ya no hay carreras con el bucle de render.
            // statusMessage es un string (asignación atómica), seguro de escribir.
            baker.OnLog += msg =>
            {
                lightmapLastLog = msg;
                statusMessage = msg;
            };
            baker.OnProgress += p =>
            {
                lightmapLastProgress = Math.Clamp(p, 0f, 1f);
                statusMessage = $"Baking... {(int)(lightmapLastProgress * 100)}%";
            };

            var lighting = BuildBakeLighting();
            var bakeSettings = new LightmapBakeSettings(
                lightmapResolution,
                bakeResolution,
                lightmapPadding,
                lightmapIndirectIntensity,
                lightmapAlbedoBoost,
                lightmapIndirectSamples,
                lightmapBounces).Normalize();
            await baker.BakeAsync(objects, lighting, (path, submeshIndex) =>
            {
                var m = ObjLoader.Load(path);
                if (m == null) return null;
                if (submeshIndex >= 0 && submeshIndex < m.Submeshes.Count)
                {
                    var submesh = m.Submeshes[submeshIndex];
                    return new LightmapBaker.BakedMeshData(
                        m.Positions, m.Normals, m.UVs, submesh.VertexCount / 3,
                        submesh.VertexStart, submesh.VertexCount);
                }
                return new LightmapBaker.BakedMeshData(m.Positions, m.Normals, m.UVs, m.TriangleCount);
            }, bakeResolution, bakeSettings);

            foreach (var kv in baker.BakedLinks)
            {
                _lightmapPaths[kv.Key] = kv.Value.Path;
                var bakedObject = FindGameObjectByEditorId(objects, kv.Key);
                var meshRenderer = bakedObject?.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.LightmapPath = kv.Value.Path;
                    meshRenderer.ShadowmaskPath = kv.Value.ShadowmaskPath;
                    meshRenderer.ReceiveGlobalIllumination = 1;
                    meshRenderer.LightmapScaleX = kv.Value.ScaleX;
                    meshRenderer.LightmapScaleY = kv.Value.ScaleY;
                    meshRenderer.LightmapOffsetX = kv.Value.OffsetX;
                    meshRenderer.LightmapOffsetY = kv.Value.OffsetY;
                    meshRenderer.UseGeneratedLightmapUv = true;
                }
            }

            // Los rangos estáticos guardan sus texturas por rango; reconstruirlos hace que la
            // asignación recién horneada se refleje en el siguiente frame, sin reiniciar la escena.
            sceneRenderer.InvalidateStaticBatch();
            sceneDirty = true;

            lightmapLastProgress = 1f;
            statusMessage = $"Bake completado — {baker.BakedPaths.Count} renderers enlazados a atlas en: {outputDir}. Shadowmask usa hasta cuatro luces Mixed.";
            lightmapLastLog = statusMessage;
        }
        catch (Exception ex)
        {
            statusMessage = $"Error en bake: {ex.Message}";
            lightmapLastLog = statusMessage;
        }
        finally
        {
            _isBaking = false;
        }
}

private async Task BakeRequestedLightmapsOnLoadAsync()
{
    string requestPath = System.IO.Path.Combine(projectPath, ".groko-auto-bake-lightmaps");
    if (!System.IO.File.Exists(requestPath))
        return;

    try
    {
        string text = (await System.IO.File.ReadAllTextAsync(requestPath)).Trim();
        int resolution = int.TryParse(text, out int parsed) ? Math.Clamp(parsed, 32, 4096) : 256;
        await BakeLightmapsAsync(resolution);
    }
    finally
    {
        try { System.IO.File.Delete(requestPath); } catch { }
    }
}

private BakeLightingInfo BuildBakeLighting()
    {
        // Un sol Realtime se resuelve exclusivamente en el shadow map. Meterlo además en
        // lightmaps sin trazado de oclusión deja luz horneada atravesando paredes al rotarlo.
        // El integrador baked de sol llegará cuando el baker tenga visibilidad por rayos.
        var info = new BakeLightingInfo
        {
            Directional = new BakeDirectionalLight { Intensity = 0f }
        };
        // Unity Shadowmask reserva cuatro canales RGBA POR TEXEL, no cuatro luces para
        // toda la escena. Luces locales que no se solapan pueden reutilizar el canal.
        bool useShadowmask = lightmapMixedModeIndex == 1;
        var shadowmaskChannels = BuildShadowmaskChannels(objects, useShadowmask);
        int ReserveShadowmaskChannel(Component light, LightMode mode)
        {
            int channel = useShadowmask && mode == LightMode.Mixed && shadowmaskChannels.TryGetValue(light, out int assigned)
                ? assigned : -1;
            bool fallback = useShadowmask && mode == LightMode.Mixed && channel < 0;
            switch (light)
            {
                case DirectionalLight directional:
                    directional.ShadowmaskChannel = channel;
                    directional.ShadowmaskBakedFallback = fallback;
                    break;
                case PointLight point:
                    point.ShadowmaskChannel = channel;
                    point.ShadowmaskBakedFallback = fallback;
                    break;
                case SpotLight spot:
                    spot.ShadowmaskChannel = channel;
                    spot.ShadowmaskBakedFallback = fallback;
                    break;
            }
            return channel;
        }
        void Visit(IEnumerable<GameObject> objs)
        {
            foreach (var obj in objs)
            {
                if (obj.GetComponent<DirectionalLight>() is { } dl && dl.Mode != LightMode.Realtime)
                {
                    var direction = dl.GetNormalizedDirection();
                    info.Directional = new BakeDirectionalLight
                    {
                        X = direction.X,
                        Y = direction.Y,
                        Z = direction.Z,
                        R = dl.R,
                        G = dl.G,
                        B = dl.B,
                        Intensity = dl.Intensity,
                        BakeDirect = dl.Mode == LightMode.Baked || (useShadowmask && dl.Mode == LightMode.Mixed && !shadowmaskChannels.ContainsKey(dl)),
                        BakeIndirect = true,
                        ShadowmaskChannel = ReserveShadowmaskChannel(dl, dl.Mode)
                    };
                }
                if (obj.GetComponent<AmbientLight>() is { } al)
                    info.Ambient = new BakeAmbientLight
                    { R = al.R, G = al.G, B = al.B, Intensity = al.Intensity, SkyStrength = al.SkyStrength };
                if (obj.GetComponent<PointLight>() is { } pl && pl.Mode != LightMode.Realtime)
                    info.PointLights.Add(new BakePointLight
                    {
                        Position = obj.WorldPosition,
                        R = pl.GetEffectiveColor().X,
                        G = pl.GetEffectiveColor().Y,
                        B = pl.GetEffectiveColor().Z,
                        Intensity = pl.Intensity,
                        Range = pl.Range,
                        BakeDirect = pl.Mode == LightMode.Baked || (useShadowmask && pl.Mode == LightMode.Mixed && !shadowmaskChannels.ContainsKey(pl)),
                        BakeIndirect = true,
                        ShadowmaskChannel = ReserveShadowmaskChannel(pl, pl.Mode)
                    });
                if (obj.GetComponent<SpotLight>() is { } sl && sl.Mode != LightMode.Realtime)
                    info.SpotLights.Add(new BakeSpotLight
                    {
                        Position = obj.WorldPosition,
                        Direction = ResolveBakeSpotDirection(sl),
                        R = sl.GetEffectiveColor().X,
                        G = sl.GetEffectiveColor().Y,
                        B = sl.GetEffectiveColor().Z,
                        Intensity = sl.Intensity,
                        Range = sl.Range,
                        OuterAngleDegrees = sl.Angle,
                        BakeDirect = sl.Mode == LightMode.Baked || (useShadowmask && sl.Mode == LightMode.Mixed && !shadowmaskChannels.ContainsKey(sl)),
                        BakeIndirect = true,
                        ShadowmaskChannel = ReserveShadowmaskChannel(sl, sl.Mode)
                    });
                Visit(obj.Children);
            }
        }
        Visit(objects);
        return info;
    }

private readonly record struct ShadowmaskLightCandidate(Component Light, Vector3 Position, float Range, bool Directional);

private static Dictionary<Component, int> BuildShadowmaskChannels(IEnumerable<GameObject> roots, bool enabled)
{
    var result = new Dictionary<Component, int>();
    if (!enabled)
        return result;

    var candidates = new List<ShadowmaskLightCandidate>();
    void Visit(IEnumerable<GameObject> items)
    {
        foreach (var obj in items)
        {
            if (obj.GetComponent<DirectionalLight>() is { Mode: LightMode.Mixed } directional)
                candidates.Add(new ShadowmaskLightCandidate(directional, obj.WorldPosition, float.PositiveInfinity, true));
            if (obj.GetComponent<PointLight>() is { Mode: LightMode.Mixed } point)
                candidates.Add(new ShadowmaskLightCandidate(point, obj.WorldPosition, point.Range, false));
            if (obj.GetComponent<SpotLight>() is { Mode: LightMode.Mixed } spot)
                candidates.Add(new ShadowmaskLightCandidate(spot, obj.WorldPosition, spot.Range, false));
            Visit(obj.Children);
        }
    }

    Visit(roots);
    // Primero las direccionales y después los volúmenes más grandes: reduce el riesgo de
    // dejar sin canal una luz dominante. El desempate por EditorId hace el bake reproducible.
    candidates.Sort((a, b) =>
    {
        int directional = b.Directional.CompareTo(a.Directional);
        if (directional != 0) return directional;
        int range = b.Range.CompareTo(a.Range);
        if (range != 0) return range;
        return string.CompareOrdinal(a.Light.gameObject?.EditorId, b.Light.gameObject?.EditorId);
    });

    var assigned = new List<(ShadowmaskLightCandidate Candidate, int Channel)>();
    foreach (var candidate in candidates)
    {
        for (int channel = 0; channel < 4; channel++)
        {
            bool conflict = assigned.Any(other => other.Channel == channel && ShadowmaskVolumesOverlap(candidate, other.Candidate));
            if (conflict) continue;
            result[candidate.Light] = channel;
            assigned.Add((candidate, channel));
            break;
        }
    }
    return result;
}

private static bool ShadowmaskVolumesOverlap(ShadowmaskLightCandidate a, ShadowmaskLightCandidate b)
{
    if (a.Directional || b.Directional)
        return true;
    var delta = a.Position - b.Position;
    float reach = a.Range + b.Range;
    return delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z <= reach * reach;
}

private static Vector3 ResolveBakeSpotDirection(SpotLight spot)
    {
        var local = spot.Direction;
        if (local.X * local.X + local.Y * local.Y + local.Z * local.Z <= 0.00000001f)
            local = new Vector3(0f, 0f, 1f);

        var obj = spot.gameObject;
        if (obj == null)
            return local.Normalized();

        return RotateBakeVector(local, new Vector3(obj.RotX, obj.RotY, obj.RotZ)).Normalized();
    }

private static Vector3 RotateBakeVector(Vector3 v, Vector3 rot)
    {
        float z = rot.Z * (MathF.PI / 180f), cz = MathF.Cos(z), sz = MathF.Sin(z);
        v = new Vector3(v.X * cz - v.Y * sz, v.X * sz + v.Y * cz, v.Z);
        float x = rot.X * (MathF.PI / 180f), cx = MathF.Cos(x), sx = MathF.Sin(x);
        v = new Vector3(v.X, v.Y * cx - v.Z * sx, v.Y * sx + v.Z * cx);
        float y = rot.Y * (MathF.PI / 180f), cy = MathF.Cos(y), sy = MathF.Sin(y);
        return new Vector3(v.X * cy + v.Z * sy, v.Y, -v.X * sy + v.Z * cy);
    }

private static void DrawComponentSummary(Component component)
    {
        string summary = component switch
        {
            MeshFilter mf => string.IsNullOrWhiteSpace(mf.MeshPath) ? "No mesh assigned" : $"Mesh: {Path.GetFileName(mf.MeshPath)}",
            Material mat => string.IsNullOrWhiteSpace(mat.AssetPath) ? $"Color: {mat.R:F2}, {mat.G:F2}, {mat.B:F2}" : $"Material: {Path.GetFileName(mat.AssetPath)}",
            Camera cam => $"FOV {cam.FOV:F0}  Clip {cam.NearClip:F2}-{cam.FarClip:F0}",
            Rigidbody rb => $"Mass {rb.Mass:F2}  Gravity {(rb.UseGravity ? "On" : "Off")}  Kinematic {(rb.IsKinematic ? "On" : "Off")}",
            CharacterController cc => $"Height {cc.Height:F2}  Radius {cc.Radius:F2}  Grounded {(cc.IsGrounded ? "Yes" : "No")}",
            BoxCollider box => $"{(box.IsTrigger ? "Trigger" : "Collider")}  Size {box.Size.X:F2}, {box.Size.Y:F2}, {box.Size.Z:F2}  {box.PhysicMaterial}",
            SphereCollider sphere => $"{(sphere.IsTrigger ? "Trigger" : "Collider")}  Radius {sphere.Radius:F2}  {sphere.PhysicMaterial}",
            CapsuleCollider capsule => $"{(capsule.IsTrigger ? "Trigger" : "Collider")}  Radius {capsule.Radius:F2} Height {capsule.Height:F2}  {capsule.Axis}",
            MeshCollider mesh => $"{(mesh.IsTrigger ? "Trigger" : "Collider")}  Mesh bounds {(mesh.UseMeshBounds ? "On" : "Off")}  {mesh.PhysicMaterial}",
            DirectionalLight dl => $"Intensity {dl.Intensity:F2}  Shadows {(dl.Shadows ? "On" : "Off")}",
            PointLight pl => $"Intensity {pl.Intensity:F2}  Range {pl.Range:F1}",
            SpotLight sl => $"Intensity {sl.Intensity:F2}  Range {sl.Range:F1}  Angle {sl.Angle:F0}",
            AmbientLight al => $"Intensity {al.Intensity:F2}  Sky {al.SkyStrength:F2}",
            AreaLight area => $"Intensity {area.Intensity:F2}  Size {area.Size:F1}",
            RectangleLight rect => $"Intensity {rect.Intensity:F2}  {rect.Width:F1}x{rect.Height:F1}",
            ReflectionProbe rp => $"{rp.Mode}  Size {rp.SizeX:F1}, {rp.SizeY:F1}, {rp.SizeZ:F1}",
            PostProcessSettings pp => $"Post FX {(pp.PostProcessEnabled ? "Enabled" : "Disabled")}",
            _ => component.GetType().Name
        };

        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.55f, 0.60f, 0.65f, 1f));
        ImGui.TextWrapped(summary);
        ImGui.PopStyleColor();
    }

private static void DrawLightColor(float r, float g, float b, Action<float, float, float> set)
    {
        FieldRow("Color");
        var color = new System.Numerics.Vector3(r, g, b);
        if (ColorField("##Color", ref color))
            set(color.X, color.Y, color.Z);
    }

private static string? BrowseForHdri()
    {
        try
        {
            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select HDRI",
                Filter = "Radiance HDR (*.hdr;*.pic)|*.hdr;*.pic|All files (*.*)|*.*",
                CheckFileExists = true
            };
            return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : null;
        }
        catch (Exception ex)
        {
            GrokoEngine.Debug.LogWarning("No se pudo abrir el diálogo de archivos: " + ex.Message);
            return null;
        }
    }

/// <summary>Resuelve lo que se soltó en un slot "HDRI Environment": puede ser un .hdr directo,
/// o un asset Cubemap (Create > Cubemap) que a su vez apunta a un .hdr.</summary>
private static bool TryResolveDroppedHdri(string? droppedPath, out string resolvedHdriPath)
    {
        resolvedHdriPath = "";
        if (string.IsNullOrWhiteSpace(droppedPath) || !File.Exists(droppedPath))
            return false;

        if (GrokoEngine.HdrLoader.IsHdr(droppedPath))
        {
            resolvedHdriPath = droppedPath;
            return true;
        }

        if (GrokoEngine.CubemapAsset.IsCubemapPath(droppedPath))
        {
            var data = GrokoEngine.CubemapAsset.Load(droppedPath);
            if (!string.IsNullOrWhiteSpace(data.SourceTexturePath) && File.Exists(data.SourceTexturePath) && GrokoEngine.HdrLoader.IsHdr(data.SourceTexturePath))
            {
                resolvedHdriPath = data.SourceTexturePath;
                return true;
            }
        }

        return false;
    }

private List<ModelAnimationInfo> GetModelAnimations(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new List<ModelAnimationInfo>();

        DateTime stamp = File.GetLastWriteTimeUtc(path);
        if (fbxAnimCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Anims;

        var anims = ObjLoader.ReadAnimations(path);
        fbxAnimCache[path] = (stamp, anims);
        return anims;
    }

private void DrawFbxAnimationInfo(string meshPath)
    {
        if (string.IsNullOrWhiteSpace(meshPath) || !meshPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            return;

        var anims = GetModelAnimations(meshPath);
        ImGui.Dummy(new Vector2(0f, 4f));
        ImGui.Separator();
        if (anims.Count == 0)
        {
            ImGui.TextDisabled("Animaciones: ninguna (el FBX no trae)");
            return;
        }

        ImGui.TextColored(new System.Numerics.Vector4(0.45f, 0.78f, 0.95f, 1f), $"Animaciones embebidas: {anims.Count}");
        for (int i = 0; i < anims.Count; i++)
        {
            var a = anims[i];
            ImGui.BulletText($"{a.Name}  —  {a.DurationSeconds:0.00}s, {a.ChannelCount} huesos");
        }
    }

private static void EnsureMeshRenderer(GameObject obj)
    {
        if (obj.GetComponent<MeshRenderer>() != null) return;
        bool renderable = obj.GetComponent<MeshFilter>() != null || (obj.Type >= 1 && obj.Type <= 6);
        if (renderable)
            obj.AddComponent<MeshRenderer>();
    }

private void DrawMeshRendererInspector(GameObject obj, MeshRenderer mr)
    {
        // ── Materials ──
        if (Section("Materials"))
        {
            var mf = obj.GetComponent<MeshFilter>();
            if (mf != null && !string.IsNullOrWhiteSpace(mf.MeshPath) && GetInspectorParsedMesh(mf.MeshPath) is { } mesh && mesh.Submeshes.Count > 0)
            {
                DrawMaterialHealthWarnings(obj, mesh);
                DrawRendererMaterialsHeader(mf, mesh);

                if (mf.SubmeshIndex >= 0 && mf.SubmeshIndex < mesh.Submeshes.Count)
                {
                    if (NeedsMaterialSlotSync(mf, mesh))
                        EnsureMaterialSlots(mf, mesh);
                    int idx = mf.SubmeshIndex;
                    DrawUnityMaterialSlot(obj, mf, mesh, idx, "Element 0", mesh.Submeshes[idx].Name);
                }
                else
                {
                    if (NeedsMaterialSlotSync(mf, mesh))
                        EnsureMaterialSlots(mf, mesh);
                    for (int i = 0; i < mesh.Submeshes.Count; i++)
                    {
                        DrawUnityMaterialSlot(obj, mf, mesh, i, $"Element {i}", mesh.Submeshes[i].Name);
                    }
                }
            }
            else
            {
                // Objeto de un solo material (primitiva o malla sin submallas): componente Material.
                string current = obj.GetComponent<Material>()?.AssetPath ?? "";
                DrawSingleMaterialSlot(obj, current);
            }
        }

        // ── Lighting ──
        if (Section("Lighting"))
        {
            DrawComboRow("Cast Shadows", new[] { "Off", "On", "Two Sided", "Shadows Only" }, mr.CastShadows, v => mr.CastShadows = v);
            if (mr.CastShadows == 1)
                DrawCheckRow("Static Shadow Caster", mr.StaticShadowCaster, v => mr.StaticShadowCaster = v);
            DrawCheckRow("Contribute Global Illumination", mr.ContributeGlobalIllumination, v => mr.ContributeGlobalIllumination = v);
            ImGui.BeginDisabled(!mr.ContributeGlobalIllumination);
            DrawComboRow("Receive Global Illumination", new[] { "Light Probes", "Lightmaps" }, mr.ReceiveGlobalIllumination, v => mr.ReceiveGlobalIllumination = v);
            ImGui.EndDisabled();
            DrawCheckRow("Receive Shadows", mr.ReceiveShadows, v => mr.ReceiveShadows = v);
        }

        // ── Probes ──
        if (Section("Probes"))
        {
            DrawComboRow("Light Probes", new[] { "Blend Probes", "Use Proxy Volume", "Off" }, mr.LightProbes, v => mr.LightProbes = v);
            DrawComboRow("Reflection Probes", new[] { "Blend Probes", "Blend Probes And Skybox", "Simple", "Off" }, mr.ReflectionProbes, v => mr.ReflectionProbes = v);
        }

        // ── Ray Tracing ──
        if (Section("Ray Tracing"))
        {
            DrawComboRow("Ray Tracing Mode", new[] { "Off", "Dynamic Transform", "Dynamic Geometry", "Static" }, mr.RayTracingMode, v => mr.RayTracingMode = v);
            DrawCheckRow("Procedural Geometry", mr.ProceduralGeometry, v => mr.ProceduralGeometry = v);
            DrawComboRow("Acceleration Structure", new[] { "Prefer Fast Trace", "Prefer Fast Build" }, mr.AccelerationStructure, v => mr.AccelerationStructure = v);
        }

        // ── Additional Settings ──
        if (Section("Additional Settings"))
        {
            DrawComboRow("Motion Vectors", new[] { "Camera Motion Only", "Per Object Motion", "Force No Motion" }, mr.MotionVectors, v => mr.MotionVectors = v);
            DrawCheckRow("Dynamic Occlusion", mr.DynamicOcclusion, v => mr.DynamicOcclusion = v);
            DrawComboRow("Rendering Layer Mask", new[] { "Default" }, 0, _ => { });
            DrawFloat("Priority", mr.Priority, v => mr.Priority = (int)v, 1f, -100f, 100f);
            ImGui.Dummy(new Vector2(0f, 2f));
            ImGui.TextDisabled("Lighting/Ray Tracing: ajustes visuales (las sombras del motor son globales).");
        }
    }

private void DrawReflectionProbeInspector(GameObject obj, ReflectionProbe probe)
    {
        if (Section("Type"))
        {
            string[] modes = { "Baked", "Realtime", "Custom" };
            int mode = Math.Clamp((int)probe.Mode, 0, modes.Length - 1);
            DrawComboRow("Type", modes, mode, v => probe.Mode = (ReflectionProbeMode)v);

            switch (probe.Mode)
            {
                case ReflectionProbeMode.Baked:
                    ImGui.TextDisabled("Baked: captura reflejos en editor. Es el modo mas rapido para escenarios estaticos.");
                    DrawAssetSlot("Baked Cubemap", probe.BakedCubemapPath, "None (.cubemap/.hdr)",
                        p => probe.BakedCubemapPath = p ?? "", IsReflectionProbeCubemapPath);
                    FieldRow("");
                    if (ImGui.Button("Bake Reflection Probe", new Vector2(-1f, 0f)))
                    {
                        BakeReflectionProbe(obj, probe);
                    }
                    break;

                case ReflectionProbeMode.Realtime:
                    ImGui.TextDisabled("Realtime: actualiza durante el juego. Usalo solo donde haga falta porque cuesta rendimiento.");
                    DrawComboRow("Refresh Mode", new[] { "On Awake", "Every Frame", "Via Scripting" },
                        (int)probe.RefreshMode, v => probe.RefreshMode = (ReflectionProbeRefreshMode)v);
                    DrawComboRow("Time Slicing", new[] { "No Time Slicing", "Individual Faces", "All Faces At Once" },
                        (int)probe.TimeSlicing, v => probe.TimeSlicing = (ReflectionProbeTimeSlicing)v);
                    break;

                case ReflectionProbeMode.Custom:
                    ImGui.TextDisabled("Custom: usa un Cubemap creado o seleccionado manualmente.");
                    DrawAssetSlot("Custom Cubemap", probe.CustomCubemapPath, "None (.cubemap/.hdr)",
                        p => probe.CustomCubemapPath = p ?? "", IsReflectionProbeCubemapPath);
                    break;
            }
        }

        if (Section("Runtime Settings"))
        {
            float intensity = probe.Intensity;
            if (DrawUnitySliderFloat("Intensity", ref intensity, 0f, 4f, 0.01f))
                probe.Intensity = intensity;
            DrawFloat("Blend Distance", probe.BlendDistance, v => probe.BlendDistance = Math.Max(0f, v), 0.05f, 0f, 1000f);
            DrawInt("Resolution", probe.Resolution, v => probe.Resolution = ClampReflectionProbeResolution(v), 1f, 16, 2048);
            DrawInt("Importance", probe.Importance, v => probe.Importance = Math.Clamp(v, -1000, 1000), 1f, -1000, 1000);
            DrawCheckRow("Box Projection", probe.BoxProjection, v => probe.BoxProjection = v);
            DrawCheckRow("HDR", probe.HDR, v => probe.HDR = v);
        }

        if (Section("Box Projection"))
        {
            DrawFloat("Size X", probe.SizeX, v => probe.SizeX = Math.Max(0.01f, v), 0.05f, 0.01f, 10000f);
            DrawFloat("Size Y", probe.SizeY, v => probe.SizeY = Math.Max(0.01f, v), 0.05f, 0.01f, 10000f);
            DrawFloat("Size Z", probe.SizeZ, v => probe.SizeZ = Math.Max(0.01f, v), 0.05f, 0.01f, 10000f);
            DrawFloat("Center X", probe.CenterX, v => probe.CenterX = v, 0.05f, -10000f, 10000f);
            DrawFloat("Center Y", probe.CenterY, v => probe.CenterY = v, 0.05f, -10000f, 10000f);
            DrawFloat("Center Z", probe.CenterZ, v => probe.CenterZ = v, 0.05f, -10000f, 10000f);
        }

        if (Section("Clipping Planes"))
        {
            DrawFloat("Near", probe.NearClip, v => probe.NearClip = Math.Max(0.001f, v), 0.01f, 0.001f, 1000f);
            DrawFloat("Far", probe.FarClip, v => probe.FarClip = Math.Max(probe.NearClip + 0.01f, v), 0.5f, 0.01f, 100000f);
        }
    }

private static int ClampReflectionProbeResolution(int value)
    {
        int[] allowed = { 16, 32, 64, 128, 256, 512, 1024, 2048 };
        int best = allowed[0];
        int bestDelta = Math.Abs(value - best);
        for (int i = 1; i < allowed.Length; i++)
        {
            int delta = Math.Abs(value - allowed[i]);
            if (delta < bestDelta)
            {
                best = allowed[i];
                bestDelta = delta;
            }
        }
        return best;
    }

private static bool IsReflectionProbeCubemapPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;
        return CubemapAsset.IsCubemapPath(path) || HdrLoader.IsHdr(path);
    }

private void DrawMaterialHealthWarnings(GameObject obj, ParsedMesh mesh)
    {
        bool suspiciousLooseMaterial = mesh.Submeshes.Count > 0 &&
                                       obj.GetComponent<Material>() != null &&
                                       obj.GetComponent<Animator>() != null;

        if (!suspiciousLooseMaterial)
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1.00f, 0.78f, 0.34f, 1f));
        ImGui.TextWrapped("Material Health: este personaje tiene un Material suelto en el root, pero la malla usa Materials/Element. Puede provocar materiales incorrectos o bajones de FPS.");
        ImGui.PopStyleColor();

        ImGui.PushID("MaterialHealthRootMaterial");
        if (ImGui.SmallButton("Remove root Material"))
        {
            obj.RemoveComponent<Material>(physicsEngine);
            statusMessage = "Removed suspicious root Material from " + obj.Name;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Keep"))
            statusMessage = "Root Material kept intentionally.";
        ImGui.PopID();
        ImGui.Separator();
    }

private void DrawRendererMaterialsHeader(MeshFilter mf, ParsedMesh mesh)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.70f, 0.74f, 0.78f, 1f));
        ImGui.Text($"{mesh.Submeshes.Count} material slot{(mesh.Submeshes.Count == 1 ? "" : "s")}");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        if (ImGui.SmallButton("Reset"))
        {
            mf.MaterialSlots.Clear();
            EnsureMaterialSlots(mf, mesh);
            statusMessage = "Renderer material slots reset from imported mesh.";
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear All"))
        {
            for (int i = 0; i < mf.MaterialSlots.Count; i++)
                mf.MaterialSlots[i] = "";
            statusMessage = "Renderer material slots cleared.";
        }

        if (mf.MaterialSlots.Count > mesh.Submeshes.Count)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1.00f, 0.62f, 0.28f, 1f));
            ImGui.TextWrapped("Warning: hay mas materiales que submeshes. Slots extra pueden costar rendimiento y confundir el inspector.");
            ImGui.PopStyleColor();
        }
    }

private void DrawUnityMaterialSlot(GameObject obj, MeshFilter mf, ParsedMesh mesh, int slot, string label, string? submeshName)
    {
        if (slot < 0 || slot >= mf.MaterialSlots.Count)
            return;

        ImGui.PushID($"RendererMaterialSlot_{slot}");
        float rowH = Math.Max(34f, currentDrawingApp?.guiAssetSlotHeight + 12f ?? 34f);
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        float fullW = Math.Max(220f, ImGui.GetContentRegionAvail().X - 8f);
        Vector2 rowMax = rowMin + new Vector2(fullW, rowH);
        var drawList = ImGui.GetWindowDrawList();
        bool hasMaterial = !string.IsNullOrWhiteSpace(mf.MaterialSlots[slot]);

        drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.128f, 0.128f, 0.132f, 0.92f)), 4f);
        drawList.AddRect(rowMin, rowMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.24f, 0.24f, 0.25f, 1f)), 4f);

        Vector2 previewMin = rowMin + new Vector2(6f, 5f);
        DrawTextureMapPreviewBox(mf.MaterialSlots[slot], previewMin, 24f);

        uint labelColor = ImGui.GetColorU32(new System.Numerics.Vector4(0.86f, 0.86f, 0.86f, 1f));
        uint subColor = ImGui.GetColorU32(new System.Numerics.Vector4(0.52f, 0.55f, 0.58f, 1f));
        string materialName = hasMaterial ? Path.GetFileName(mf.MaterialSlots[slot]) : "Default-Material";
        string sub = string.IsNullOrWhiteSpace(submeshName) ? "Submesh" : submeshName!;
        drawList.AddText(rowMin + new Vector2(38f, 5f), labelColor, $"{label}  {Ellipsize(materialName, 30)}");
        drawList.AddText(rowMin + new Vector2(38f, 20f), subColor, Ellipsize(sub, 42));

        ImGui.SetCursorScreenPos(rowMin);
        if (ImGui.InvisibleButton("##MaterialSlotRow", new Vector2(fullW - 92f, rowH)))
        {
            assetSlotSearch = string.Empty;
            ImGui.OpenPopup("AssetSlotPicker");
        }
        if (ImGui.IsItemHovered() && hasMaterial)
            DrawTooltip(mf.MaterialSlots[slot]);

        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) && MaterialAsset.IsMaterialPath(draggingAssetPath))
            {
                mf.MaterialSlots[slot] = draggingAssetPath;
                statusMessage = $"Assigned {Path.GetFileName(draggingAssetPath)} to {obj.Name}/{label}";
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SetCursorScreenPos(rowMin + new Vector2(fullW - 88f, 6f));
        if (ImGui.SmallButton("Select"))
        {
            selectedAssetPath = mf.MaterialSlots[slot];
            statusMessage = hasMaterial ? "Selected " + Path.GetFileName(mf.MaterialSlots[slot]) : "No material assigned.";
        }
        ImGui.SameLine(0f, 4f);
        if (ImGui.SmallButton("+"))
        {
            string created = CreateMaterialForSlot(mf, mesh, slot, obj.Name);
            mf.MaterialSlots[slot] = created;
            selectedAssetPath = created;
            statusMessage = "Created " + Path.GetFileName(created);
        }
        ImGui.SameLine(0f, 4f);
        if (ImGui.SmallButton("x"))
        {
            mf.MaterialSlots[slot] = "";
            statusMessage = $"Cleared {obj.Name}/{label}.";
        }

        DrawAssetSlotPickerPopup(mf.MaterialSlots[slot], path =>
        {
            mf.MaterialSlots[slot] = path;
            statusMessage = string.IsNullOrWhiteSpace(path)
                ? $"Cleared {obj.Name}/{label}."
                : $"Assigned {Path.GetFileName(path)} to {obj.Name}/{label}.";
        }, MaterialAsset.IsMaterialPath);

        ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y + 4f));
        ImGui.PopID();
    }

private void DrawSingleMaterialSlot(GameObject obj, string current)
    {
        DrawAssetSlot("Element 0", current, "Default-Material", p =>
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                obj.RemoveComponent<Material>(physicsEngine);
                statusMessage = "Cleared material on " + obj.Name;
                return;
            }

            MaterialAsset.ApplyTo(obj, p);
            statusMessage = "Assigned " + Path.GetFileName(p) + " to " + obj.Name;
        }, MaterialAsset.IsMaterialPath);

        if (ImGui.SmallButton("+ Create Material"))
        {
            string directory = currentProjectDirectory ?? "";
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                directory = rootAssetsPath;
            string created = MaterialAsset.Create(directory, obj.Name + "_Material");
            MaterialAsset.ApplyTo(obj, created);
            selectedAssetPath = created;
            statusMessage = "Created " + Path.GetFileName(created);
        }
    }

private string CreateMaterialForSlot(MeshFilter mf, ParsedMesh mesh, int slot, string ownerName)
    {
        string directory = rootAssetsPath;
        string? meshFullPath = SceneViewportRenderer.NormalizeExistingAssetPath(mf.MeshPath);
        if (!string.IsNullOrWhiteSpace(meshFullPath) && File.Exists(meshFullPath))
            directory = Path.GetDirectoryName(meshFullPath) ?? rootAssetsPath;
        else if (!string.IsNullOrWhiteSpace(currentProjectDirectory) && Directory.Exists(currentProjectDirectory))
            directory = currentProjectDirectory;

        string subName = slot >= 0 && slot < mesh.Submeshes.Count && !string.IsNullOrWhiteSpace(mesh.Submeshes[slot].Name)
            ? SanitizeAssetName(mesh.Submeshes[slot].Name)
            : $"Element_{slot}";

        string baseName = SanitizeAssetName(ownerName) + "_" + subName;
        return MaterialAsset.Create(directory, baseName);
    }

private static bool NeedsMaterialSlotSync(MeshFilter mf, ParsedMesh mesh)
    {
        if (mf.MaterialSlots.Count != mesh.Submeshes.Count)
            return true;

        for (int i = 0; i < mf.MaterialSlots.Count; i++)
            if (string.IsNullOrWhiteSpace(mf.MaterialSlots[i]))
                return true;

        return false;
    }

private void EnsureMaterialSlots(MeshFilter mf, ParsedMesh mesh)
    {
        while (mf.MaterialSlots.Count < mesh.Submeshes.Count)
            mf.MaterialSlots.Add("");

        if (mf.MaterialSlots.Count > mesh.Submeshes.Count)
            mf.MaterialSlots.RemoveRange(mesh.Submeshes.Count, mf.MaterialSlots.Count - mesh.Submeshes.Count);

        string? meshFullPath = SceneViewportRenderer.NormalizeExistingAssetPath(mf.MeshPath);
        if (meshFullPath == null) return;
        string? meshDir = Path.GetDirectoryName(meshFullPath);
        if (meshDir == null) return;
        string meshBaseName = Path.GetFileNameWithoutExtension(meshFullPath);

        for (int i = 0; i < mesh.Submeshes.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(mf.MaterialSlots[i]) && File.Exists(mf.MaterialSlots[i]))
                continue;

            var sub = mesh.Submeshes[i];
            string baseName = $"{meshBaseName}_{sub.Name}";
            mf.MaterialSlots[i] = MaterialAsset.CreateFromImported(meshDir, baseName, sub.DiffuseR, sub.DiffuseG, sub.DiffuseB, sub.TexturePath);
        }
    }

    // ── LOD Group (estilo Unity) ─────────────────────────────────────────────
    // Barra de niveles con divisores arrastrables: LOD0 | LOD1 | LOD2 | LOD3 | Culled.
    // Los umbrales son "tamaño relativo en pantalla" (radio/distancia); el eje de la barra
    // usa raíz cuadrada para que los umbrales pequeños tengan espacio manejable.
    private const float LodBarDisplayMax = 0.40f;   // umbral mostrado en el extremo izquierdo (40%)

    private static float LodThresholdToX(float threshold, float width) =>
        width * (1f - MathF.Sqrt(Math.Clamp(threshold / LodBarDisplayMax, 0f, 1f)));

    private static float LodXToThreshold(float x, float width)
    {
        float t = 1f - Math.Clamp(x / MathF.Max(1f, width), 0f, 1f);
        return t * t * LodBarDisplayMax;
    }

private void DrawLodGroupInspector(GameObject obj, LodGroup lod)
    {
        var drawList = ImGui.GetWindowDrawList();
        float width = MathF.Max(120f, ImGui.GetContentRegionAvail().X - 4f);
        const float barHeight = 30f;
        var barMin = ImGui.GetCursorScreenPos() + new Vector2(2f, 2f);
        var barMax = barMin + new Vector2(width, barHeight);

        // Reservar el área de la barra en el layout.
        ImGui.Dummy(new Vector2(width, barHeight + 6f));

        // Segmentos (de izquierda=cerca a derecha=lejos). Colores como Unity.
        Span<float> cuts = stackalloc float[4] { lod.Lod0, lod.Lod1, lod.Lod2, lod.CulledBelow };
        var segColors = new uint[]
        {
            ImGui.GetColorU32(new System.Numerics.Vector4(0.38f, 0.49f, 0.10f, 1f)),   // LOD 0 verde oliva
            ImGui.GetColorU32(new System.Numerics.Vector4(0.30f, 0.38f, 0.48f, 1f)),   // LOD 1 azul grisáceo
            ImGui.GetColorU32(new System.Numerics.Vector4(0.26f, 0.44f, 0.44f, 1f)),   // LOD 2 verde azulado
            ImGui.GetColorU32(new System.Numerics.Vector4(0.47f, 0.33f, 0.28f, 1f)),   // LOD 3 marrón
            ImGui.GetColorU32(new System.Numerics.Vector4(0.42f, 0.10f, 0.10f, 1f))    // Culled rojo oscuro
        };
        string[] segNames = { "LOD 0", "LOD 1", "LOD 2", "LOD 3", "Culled" };

        uint textCol = ImGui.GetColorU32(new System.Numerics.Vector4(0.94f, 0.94f, 0.94f, 1f));
        float prevX = 0f;
        for (int s = 0; s < 5; s++)
        {
            float endX = s < 4 ? LodThresholdToX(cuts[s], width) : width;
            if (endX < prevX) endX = prevX;
            var segA = new Vector2(barMin.X + prevX, barMin.Y);
            var segB = new Vector2(barMin.X + endX, barMax.Y);
            if (endX - prevX > 0.5f)
            {
                drawList.AddRectFilled(segA, segB, segColors[s]);
                if (endX - prevX > 46f)
                {
                    string label = s < 4 ? segNames[s] : segNames[4];
                    string pct = s < 4 ? $"{cuts[s] * 100f:0.#}%" : "";
                    drawList.AddText(segA + new Vector2(5f, 3f), textCol, label);
                    if (pct.Length > 0)
                        drawList.AddText(segA + new Vector2(5f, 15f), textCol, pct);
                }
            }
            prevX = endX;
        }
        drawList.AddRect(barMin, barMax, ImGui.GetColorU32(new System.Numerics.Vector4(0.08f, 0.08f, 0.09f, 1f)), 2f);

        // Divisores arrastrables (uno por umbral). El 4º (Culled) puede llevarse a 0 (nunca ocultar).
        bool changed = false;
        for (int d = 0; d < 4; d++)
        {
            float x = LodThresholdToX(cuts[d], width);
            var handleMin = new Vector2(barMin.X + x - 4f, barMin.Y);
            ImGui.SetCursorScreenPos(handleMin);
            ImGui.InvisibleButton($"##lodcut{d}", new Vector2(8f, barHeight));
            bool active = ImGui.IsItemActive();
            if (ImGui.IsItemHovered() || active)
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

            drawList.AddLine(
                new Vector2(barMin.X + x, barMin.Y),
                new Vector2(barMin.X + x, barMax.Y),
                ImGui.GetColorU32(active ? new System.Numerics.Vector4(1f, 1f, 1f, 1f) : new System.Numerics.Vector4(0.85f, 0.85f, 0.85f, 0.55f)),
                active ? 2.5f : 1.5f);

            if (active)
            {
                float newT = LodXToThreshold(ImGui.GetMousePos().X - barMin.X, width);
                float upper = d == 0 ? LodBarDisplayMax : cuts[d - 1] - 0.001f;
                float lower = d == 3 ? 0f : (d < 3 ? cuts[d + 1] + 0.001f : 0f);
                newT = Math.Clamp(newT, lower, MathF.Max(lower, upper));
                if (MathF.Abs(newT - cuts[d]) > 0.00005f)
                {
                    cuts[d] = newT;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            lod.Lod0 = cuts[0];
            lod.Lod1 = cuts[1];
            lod.Lod2 = cuts[2];
            lod.CulledBelow = cuts[3] <= 0.0011f ? 0f : cuts[3];
            sceneRenderer.InvalidateStaticBatch();   // los draws cacheados usan estos umbrales
            sceneDirty = true;
        }

        ImGui.Spacing();
        DrawFloat("Bias", lod.Bias, v => { lod.Bias = MathF.Max(0.01f, v); sceneRenderer.InvalidateStaticBatch(); }, 0.02f, 0.1f, 8f);

        // Info: ¿el modelo trae niveles LOD de fábrica?
        var lodMeshFilter = obj.GetComponent<MeshFilter>() ?? obj.Children
            .Select(c => c.GetComponent<MeshFilter>())
            .FirstOrDefault(c => c != null && !string.IsNullOrWhiteSpace(c.MeshPath));
        int lodLevels = CountModelLodLevels(lodMeshFilter?.MeshPath);
        if (lodLevels > 1)
            ImGui.TextDisabled($"Model provides {lodLevels} LOD levels (auto-detected by name).");
        else
            ImGui.TextDisabled("Model has no baked LOD meshes: only distance culling applies.");
    }

    private static int CountModelLodLevels(string? meshPath)
    {
        if (string.IsNullOrWhiteSpace(meshPath))
            return 0;
        string? full = SceneViewportRenderer.NormalizeExistingAssetPath(meshPath);
        if (full == null || ObjLoader.Load(full) is not { } mesh)
            return 0;

        int maxLevels = 0;
        foreach (var sub in mesh.Submeshes)
        {
            var match = System.Text.RegularExpressions.Regex.Match(sub.MeshName ?? "", @"LOD(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int level))
                maxLevels = Math.Max(maxLevels, level + 1);
        }

        return maxLevels;
    }
}
