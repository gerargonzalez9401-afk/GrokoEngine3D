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

private async Task BakeLightmapsAsync()
    {
        if (_isBaking) return;

        // Verificar que hay objetos estáticos con mesh
        int staticCount = CountStaticMeshes(objects);
        if (staticCount == 0)
        {
            statusMessage = "Bake cancelado: no hay objetos con Static=true y MeshFilter en la escena.";
            return;
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            statusMessage = "Bake cancelado: projectPath vacío. Abre un proyecto primero.";
            return;
        }

        // Bug fix: set _isBaking inside the try/finally so it is always reset even if
        // the code between the flag and the try block throws unexpectedly.
        try
        {
            _isBaking = true;
            statusMessage = $"Baking {staticCount} objetos...";

            string outputDir = System.IO.Path.Combine(projectPath, "Assets", "Lightmaps");

            var baker = new LightmapBaker(projectPath);
            // El baker carga las mallas y captura los transforms en el hilo
            // principal (fase 1) y solo el cálculo puro de CPU corre en Task.Run
            // (fase 2), así que ya no hay carreras con el bucle de render.
            // statusMessage es un string (asignación atómica), seguro de escribir.
            baker.OnLog += msg => statusMessage = msg;
            baker.OnProgress += p => statusMessage = $"Baking... {(int)(p * 100)}%";

            var lighting = BuildBakeLighting();
            await baker.BakeAsync(objects, lighting, path =>
            {
                var m = ObjLoader.Load(path);
                if (m == null) return null;
                return new LightmapBaker.BakedMeshData(m.Positions, m.Normals, m.UVs, m.TriangleCount);
            });

            foreach (var kv in baker.BakedPaths)
                _lightmapPaths[kv.Key] = kv.Value;

            statusMessage = $"✅ Bake completado — {baker.BakedPaths.Count} lightmaps en: {outputDir}";
        }
        catch (Exception ex)
        {
            statusMessage = $"❌ Error en bake: {ex.Message}";
        }
        finally
        {
            _isBaking = false;
        }
    }

private BakeLightingInfo BuildBakeLighting()
    {
        var info = new BakeLightingInfo();
        void Visit(IEnumerable<GameObject> objs)
        {
            foreach (var obj in objs)
            {
                if (obj.GetComponent<DirectionalLight>() is { } dl)
                    info.Directional = new BakeDirectionalLight
                    {
                        X = dl.Direction.X,
                        Y = dl.Direction.Y,
                        Z = dl.Direction.Z,
                        R = dl.R,
                        G = dl.G,
                        B = dl.B,
                        Intensity = dl.Intensity
                    };
                if (obj.GetComponent<AmbientLight>() is { } al)
                    info.Ambient = new BakeAmbientLight
                    { R = al.R, G = al.G, B = al.B, Intensity = al.Intensity, SkyStrength = al.SkyStrength };
                if (obj.GetComponent<PointLight>() is { } pl)
                    info.PointLights.Add(new BakePointLight
                    {
                        Position = new MiMotor.Mathematics.Vector3(obj.PosX, obj.PosY, obj.PosZ),
                        R = pl.R,
                        G = pl.G,
                        B = pl.B,
                        Intensity = pl.Intensity,
                        Range = pl.Range
                    });
                Visit(obj.Children);
            }
        }
        Visit(objects);
        return info;
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
        if (ImGui.ColorEdit3("##Color", ref color))
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
                if (mf.SubmeshIndex >= 0 && mf.SubmeshIndex < mesh.Submeshes.Count)
                {
                    if (NeedsMaterialSlotSync(mf, mesh))
                        EnsureMaterialSlots(mf, mesh);
                    int idx = mf.SubmeshIndex;
                    DrawAssetSlot("Element 0", mf.MaterialSlots[idx], "Default-Material",
                        p => mf.MaterialSlots[idx] = p, MaterialAsset.IsMaterialPath);
                }
                else
                {
                    if (NeedsMaterialSlotSync(mf, mesh))
                        EnsureMaterialSlots(mf, mesh);
                    for (int i = 0; i < mesh.Submeshes.Count; i++)
                    {
                        int slot = i;
                        DrawAssetSlot($"Element {i}", mf.MaterialSlots[slot], "Default-Material",
                            p => mf.MaterialSlots[slot] = p, MaterialAsset.IsMaterialPath);
                    }
                }
            }
            else
            {
                // Objeto de un solo material (primitiva o malla sin submallas): componente Material.
                string current = obj.GetComponent<Material>()?.AssetPath ?? "";
                DrawAssetSlot("Element 0", current, "Default-Material",
                    p => { if (!string.IsNullOrWhiteSpace(p)) ApplyMaterialToSelected(p); }, MaterialAsset.IsMaterialPath);
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
}
