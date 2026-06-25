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
private GameObject CreateParticleSystem()
    {
        return CommitSceneMutation("Create Particle System", () =>
        {
            var obj = CreateObject("Particle System", 0);
            AddPreviewParticleSystem(obj);
            return obj;
        });
    }

private void StepEditorParticles(double deltaTime)
    {
        try
        {
            StepEditorParticlesRecursive(objects, deltaTime);
        }
        catch (Exception ex)
        {
            statusMessage = "Particle preview stopped: " + ex.Message;
        }
    }

private void StepEditorParticlesRecursive(IEnumerable<GameObject> roots, double deltaTime)
    {
        foreach (var obj in roots)
        {
            var snapshot = new List<Component>(obj.Components);
            foreach (var component in snapshot)
            {
                if (component is not GrokoEngine.ParticleSystem ps) continue;
                ps.Physics = physicsEngine;
                if (!ps.HasStarted)
                {
                    ps.Start();
                    ps.HasStarted = true;
                    if (ps.IsPlaying && ps.Particles.Count == 0)
                        ps.EmitOne();
                }
                ps.Update(deltaTime);
            }

            StepEditorParticlesRecursive(obj.Children, deltaTime);
        }
    }

private GrokoEngine.ParticleSystem AddPreviewParticleSystem(GameObject obj)
    {
        var ps = obj.AddComponent<GrokoEngine.ParticleSystem>();
        ps.Physics = physicsEngine;
        ConfigureProfessionalParticleDefaults(ps);
        ps.Play();
        ps.EmitOne();
        return ps;
    }

private static void ConfigureProfessionalParticleDefaults(GrokoEngine.ParticleSystem ps)
    {
        ps.MainModuleEnabled = true;
        ps.EmissionModuleEnabled = true;
        ps.ShapeModuleEnabled = true;
        ps.ColorOverLifetimeModuleEnabled = true;
        ps.SizeOverLifetimeModuleEnabled = true;
        ps.RotationOverLifetimeModuleEnabled = true;
        ps.RendererModuleEnabled = true;

        ps.EmitRate = 35f;
        ps.MaxParticles = 800;
        ps.Duration = 3f;
        ps.Looping = true;
        ps.PlayOnAwake = true;
        ps.SimulationSpeed = 1f;
        ps.LifetimeMin = 0.65f;
        ps.LifetimeMax = 1.45f;
        ps.SpeedMin = 1.2f;
        ps.SpeedMax = 3.4f;
        ps.SizeStart = 0.12f;
        ps.SizeEnd = 0.0f;
        ps.LifetimeCurveMid = 0.5f;
        ps.LifetimeCurveMidValue = 1f;
        ps.SpeedCurveMid = 0.5f;
        ps.SpeedCurveMidValue = 1f;
        ps.StartSizeCurveMid = 0.5f;
        ps.StartSizeCurveMidValue = 1f;
        ps.SizeCurveMid = 0.35f;
        ps.SizeCurveMidValue = 0.9f;
        ps.RotationSpeedMin = -120f;
        ps.RotationSpeedMax = 120f;

        ps.Shape = ParticleShape.Cone;
        ps.ShapeRadius = 0.18f;
        ps.ShapeAngle = 18f;
        ps.ShapeArc = 360f;
        ps.ShapeRadiusThickness = 1f;
        ps.ShapeRandomDirectionAmount = 0.12f;

        ps.ColorStartR = 1.0f; ps.ColorStartG = 0.62f; ps.ColorStartB = 0.20f; ps.ColorStartA = 0.95f;
        ps.ColorEndR = 0.95f; ps.ColorEndG = 0.10f; ps.ColorEndB = 0.02f; ps.ColorEndA = 0.0f;
        ps.ColorKeyCount = 3;
        ps.CK1T = 0.0f; ps.CK1R = 1.0f; ps.CK1G = 0.80f; ps.CK1B = 0.28f; ps.CK1A = 0.95f;
        ps.CK2T = 0.45f; ps.CK2R = 1.0f; ps.CK2G = 0.25f; ps.CK2B = 0.08f; ps.CK2A = 0.65f;
        ps.CK3T = 1.0f; ps.CK3R = 0.25f; ps.CK3G = 0.04f; ps.CK3B = 0.02f; ps.CK3A = 0.0f;

        ps.BlendMode = ParticleBlendMode.Additive;
        ps.RenderMode = ParticleRenderMode.Billboard;
        ps.SortParticles = true;
        ps.SoftParticles = true;
        ps.SoftParticleRange = 0.25f;
        ps.HdrIntensity = 1.75f;
        ps.TrailsModuleEnabled = false;
        ps.TrailEnabled = false;
        ps.ParticleCollision = false;
        ps.SyncLegacyFieldsToModules();
    }

private void DrawParticleSystemInspector(GrokoEngine.ParticleSystem ps)
    {
        ps.Physics ??= physicsEngine;

        DrawParticleToolbar(ps);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
        ImGui.Indent(10f);

        if (DrawParticleSection("Main", "timing, space and limits"))
        {
            bool enabled = ps.MainModuleEnabled;
            if (DrawParticleModuleToggle("main", ref enabled)) ps.MainModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.MainModuleEnabled);
            DrawFloat("Duration##psmain", ps.Duration, v => ps.Duration = Math.Max(0.01f, v), 0.05f, 0.01f, 600f);
            DrawFloat("Start Delay##psmain", ps.StartDelay, v => ps.StartDelay = Math.Max(0f, v), 0.05f, 0f, 600f);
            DrawFloat("Simulation Speed##psmain", ps.SimulationSpeed, v => ps.SimulationSpeed = Math.Clamp(v, 0f, 10f), 0.01f, 0f, 10f);
            DrawFloat("Max Particles##psmain", ps.MaxParticles, v => ps.MaxParticles = Math.Max(1, (int)v), 10f, 1f, 50000f);
            bool looping = ps.Looping;
            if (SmallCheckbox("Looping##psmain", ref looping)) ps.Looping = looping;
            bool playOnAwake = ps.PlayOnAwake;
            if (SmallCheckbox("Play On Awake##psmain", ref playOnAwake)) ps.PlayOnAwake = playOnAwake;
            bool prewarm = ps.Prewarm;
            if (SmallCheckbox("Prewarm##psmain", ref prewarm)) ps.Prewarm = prewarm;
            bool autoSeed = ps.AutoRandomSeed;
            if (SmallCheckbox("Auto Random Seed##psmain", ref autoSeed)) ps.AutoRandomSeed = autoSeed;
            if (!ps.AutoRandomSeed)
                DrawFloat("Random Seed##psmain", ps.RandomSeed, v => ps.RandomSeed = Math.Max(0, (int)v), 1f, 0f, int.MaxValue);
            DrawEnumCombo("Simulation Space##psmain", ps.SimulationSpace, v => ps.SimulationSpace = v);
            DrawEnumCombo("Scaling Mode##psmain", ps.ScalingMode, v => ps.ScalingMode = v);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Emission", "rate and bursts"))
        {
            bool enabled = ps.EmissionModuleEnabled;
            if (DrawParticleModuleToggle("emission", ref enabled)) ps.EmissionModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.EmissionModuleEnabled);
            DrawFloat("Rate over Time##psemit", ps.EmitRate, v => ps.EmitRate = Math.Max(0f, v), 1f, 0f, 5000f);
            bool rateOverDistance = ps.RateOverDistanceEnabled;
            if (SmallCheckbox("Rate over Distance##psemit", ref rateOverDistance)) ps.RateOverDistanceEnabled = rateOverDistance;
            if (ps.RateOverDistanceEnabled)
                DrawFloat("Distance Rate##psemit", ps.RateOverDistance, v => ps.RateOverDistance = Math.Max(0f, v), 0.1f, 0f, 1000f);

            bool burst = ps.BurstEnabled;
            if (SmallCheckbox("Burst##psemit", ref burst)) ps.BurstEnabled = burst;
            if (ps.BurstEnabled)
            {
                DrawFloat("Burst Time##psemit", ps.BurstTime, v => ps.BurstTime = Math.Max(0f, v), 0.05f, 0f, 600f);
                DrawFloat("Burst Count##psemit", ps.BurstCount, v => ps.BurstCount = Math.Max(1, (int)v), 1f, 1f, 10000f);
                DrawFloat("Burst Probability##psemit", ps.BurstProbability, v => ps.BurstProbability = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
            }

            if (ImGui.CollapsingHeader($"Extra Bursts ({ps.ExtraBursts.Count})##psextrabursts"))
            {
                if (ImGui.Button("+ Add Burst##psextraburst", new Vector2(112f, 0f)))
                    ps.ExtraBursts.Add(new BurstEvent { Time = 1f, Count = 20, Cycles = 1, Interval = 1f });

                if (ps.ExtraBursts.Count > 0 && ImGui.BeginTable("##psextrabursttable", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
                {
                    ImGui.TableSetupColumn("Time");
                    ImGui.TableSetupColumn("Count");
                    ImGui.TableSetupColumn("Cycles");
                    ImGui.TableSetupColumn("Interval");
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30f);
                    ImGui.TableHeadersRow();

                    for (int i = 0; i < ps.ExtraBursts.Count; i++)
                    {
                        var b = ps.ExtraBursts[i];
                        ImGui.PushID(i);
                        ImGui.TableNextRow();

                        ImGui.TableSetColumnIndex(0);
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.DragFloat("##time", ref b.Time, 0.05f, 0f, 600f)) b.Time = Math.Max(0f, b.Time);

                        ImGui.TableSetColumnIndex(1);
                        ImGui.SetNextItemWidth(-1f);
                        int count = b.Count;
                        if (ImGui.DragInt("##count", ref count, 1f, 1, 10000)) b.Count = Math.Max(1, count);

                        ImGui.TableSetColumnIndex(2);
                        ImGui.SetNextItemWidth(-1f);
                        int cycles = b.Cycles;
                        if (ImGui.DragInt("##cycles", ref cycles, 1f, 0, 1000)) b.Cycles = Math.Max(0, cycles);

                        ImGui.TableSetColumnIndex(3);
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.DragFloat("##interval", ref b.Interval, 0.05f, 0.01f, 600f)) b.Interval = Math.Max(0.01f, b.Interval);

                        ImGui.TableSetColumnIndex(4);
                        bool remove = ImGui.SmallButton("X##removeburst");
                        ps.ExtraBursts[i] = b;
                        ImGui.PopID();

                        if (remove)
                        {
                            ps.ExtraBursts.RemoveAt(i);
                            i--;
                        }
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Shape", "spawn volume"))
        {
            bool enabled = ps.ShapeModuleEnabled;
            if (DrawParticleModuleToggle("shape", ref enabled)) ps.ShapeModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.ShapeModuleEnabled);
            DrawEnumCombo("Shape##psshape", ps.Shape, v => ps.Shape = v);
            DrawFloat("Radius##psshape", ps.ShapeRadius, v => ps.ShapeRadius = Math.Max(0f, v), 0.05f, 0f, 100f);
            DrawFloat("Arc##psshape", ps.ShapeArc, v => ps.ShapeArc = Math.Clamp(v, 0f, 360f), 1f, 0f, 360f);
            DrawFloat("Radius Thickness##psshape", ps.ShapeRadiusThickness, v => ps.ShapeRadiusThickness = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
            bool shell = ps.ShapeEmitFromShell;
            if (SmallCheckbox("Emit From Shell##psshape", ref shell)) ps.ShapeEmitFromShell = shell;
            DrawFloat("Random Direction##psshape", ps.ShapeRandomDirectionAmount, v => ps.ShapeRandomDirectionAmount = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
            if (ps.Shape == ParticleShape.Cone)
                DrawFloat("Angle##psshape", ps.ShapeAngle, v => ps.ShapeAngle = Math.Clamp(v, 0f, 180f), 1f, 0f, 180f);
            if (ps.Shape == ParticleShape.Box)
            {
                DrawFloat("Box Size X##psshape", ps.ShapeBoxSizeX, v => ps.ShapeBoxSizeX = Math.Max(0f, v), 0.05f, 0f, 100f);
                DrawFloat("Box Size Y##psshape", ps.ShapeBoxSizeY, v => ps.ShapeBoxSizeY = Math.Max(0f, v), 0.05f, 0f, 100f);
                DrawFloat("Box Size Z##psshape", ps.ShapeBoxSizeZ, v => ps.ShapeBoxSizeZ = Math.Max(0f, v), 0.05f, 0f, 100f);
            }
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Particle", "lifetime, size, speed"))
        {
            DrawParticleScalarMode("lifetime", "Lifetime Mode", ps.LifetimeMode, v => ps.LifetimeMode = v,
                "Lifetime", ps.LifetimeMin, ps.LifetimeMax,
                (a, b) => { ps.LifetimeMin = Math.Max(0.01f, Math.Min(a, b)); ps.LifetimeMax = Math.Max(ps.LifetimeMin, b); },
                ps.LifetimeCurveMid, ps.LifetimeCurveMidValue,
                (mid, value) => { ps.LifetimeCurveMid = Math.Clamp(mid, 0f, 1f); ps.LifetimeCurveMidValue = Math.Max(0f, value); },
                0.05f, 0.01f, 100f);

            DrawParticleScalarMode("speed", "Speed Mode", ps.SpeedMode, v => ps.SpeedMode = v,
                "Speed", ps.SpeedMin, ps.SpeedMax,
                (a, b) => { ps.SpeedMin = Math.Max(0f, Math.Min(a, b)); ps.SpeedMax = Math.Max(ps.SpeedMin, b); },
                ps.SpeedCurveMid, ps.SpeedCurveMidValue,
                (mid, value) => { ps.SpeedCurveMid = Math.Clamp(mid, 0f, 1f); ps.SpeedCurveMidValue = Math.Max(0f, value); },
                0.1f, 0f, 500f);

            DrawParticleValueModeCombo("Size Mode##psparticle", ps.SizeMode, v => ps.SizeMode = v);
            bool size3d = ps.StartSize3D;
            if (SmallCheckbox("Start Size 3D##psparticle", ref size3d)) ps.StartSize3D = size3d;
            if (ps.SizeMode is ParticleValueMode.RandomBetweenTwoConstants or ParticleValueMode.RandomBetweenTwoCurves)
            {
                DrawFloat2("Size Min / Max##psparticle", ps.SizeStart, ps.SizeEnd,
                    (a, b) => { ps.SizeStart = Math.Max(0f, Math.Min(a, b)); ps.SizeEnd = Math.Max(ps.SizeStart, b); }, 0.01f, 0f, 50f);
            }
            else
            {
                DrawFloat("Start Size##psparticle", ps.SizeStart, v => ps.SizeStart = Math.Max(0f, v), 0.01f, 0f, 50f);
                if (ps.SizeOverLifetimeModuleEnabled)
                    DrawFloat("End Size##psparticle", ps.SizeEnd, v => ps.SizeEnd = Math.Max(0f, v), 0.01f, 0f, 50f);
            }
            if (ps.SizeMode is ParticleValueMode.Curve or ParticleValueMode.RandomBetweenTwoCurves)
            {
                DrawParticleCurveControls("Start Size Curve", "psstartsizecurve",
                    ps.StartSizeCurveMid, ps.StartSizeCurveMidValue,
                    (mid, value) => { ps.StartSizeCurveMid = Math.Clamp(mid, 0f, 1f); ps.StartSizeCurveMidValue = Math.Max(0f, value); });
            }
            if (ps.StartSize3D)
            {
                DrawFloat("Size X Start##psparticle", ps.SizeStartX, v => ps.SizeStartX = Math.Max(0f, v), 0.01f, 0f, 50f);
                DrawFloat("Size Y Start##psparticle", ps.SizeStartY, v => ps.SizeStartY = Math.Max(0f, v), 0.01f, 0f, 50f);
                DrawFloat("Size Z Start##psparticle", ps.SizeStartZ, v => ps.SizeStartZ = Math.Max(0f, v), 0.01f, 0f, 50f);
                DrawFloat("Size X End##psparticle", ps.SizeEndX, v => ps.SizeEndX = Math.Max(0f, v), 0.01f, 0f, 50f);
                DrawFloat("Size Y End##psparticle", ps.SizeEndY, v => ps.SizeEndY = Math.Max(0f, v), 0.01f, 0f, 50f);
                DrawFloat("Size Z End##psparticle", ps.SizeEndZ, v => ps.SizeEndZ = Math.Max(0f, v), 0.01f, 0f, 50f);
            }

            bool sizeEnabled = ps.SizeOverLifetimeModuleEnabled;
            if (SmallCheckbox("Size over Lifetime##psparticle", ref sizeEnabled)) ps.SizeOverLifetimeModuleEnabled = sizeEnabled;
            if (ps.SizeOverLifetimeModuleEnabled)
            {
                DrawParticleCurveControls("Size Lifetime Curve", "pssizecurve",
                    ps.SizeCurveMid, ps.SizeCurveMidValue,
                    (mid, value) => { ps.SizeCurveMid = Math.Clamp(mid, 0f, 1f); ps.SizeCurveMidValue = Math.Max(0f, value); });
            }

            DrawEnumCombo("Gravity Mode##psparticle", ps.GravityMode, v => ps.GravityMode = v);
            DrawFloat("Gravity Scale##psparticle", ps.GravityScale, v => ps.GravityScale = Math.Clamp(v, -10f, 10f), 0.05f, -10f, 10f);
            DrawFloat2("Rotation Min / Max##psparticle", ps.RotationSpeedMin, ps.RotationSpeedMax,
                (a, b) => { ps.RotationSpeedMin = a; ps.RotationSpeedMax = b; }, 1f, -720f, 720f);
        }

        if (DrawParticleSection("Color over Lifetime", "gradient and alpha"))
        {
            bool enabled = ps.ColorOverLifetimeModuleEnabled;
            if (DrawParticleModuleToggle("color", ref enabled)) ps.ColorOverLifetimeModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.ColorOverLifetimeModuleEnabled);
            DrawParticleColor255Row("Start Color##pscolor", ps.ColorStartR, ps.ColorStartG, ps.ColorStartB, ps.ColorStartA,
                (r, g, b, a) => SetParticleGradientEndpoint(ps, true, r, g, b, a));
            DrawParticleColor255Row("End Color##pscolor", ps.ColorEndR, ps.ColorEndG, ps.ColorEndB, ps.ColorEndA,
                (r, g, b, a) => SetParticleGradientEndpoint(ps, false, r, g, b, a));
            DrawParticleGradientField(ps);
            DrawParticleGradientEditorPopup(ps);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Velocity over Lifetime", "wind and drift", false))
        {
            bool enabled = ps.VelocityOverLifetimeModuleEnabled;
            if (DrawParticleModuleToggle("velocity", ref enabled)) ps.VelocityOverLifetimeModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.VelocityOverLifetimeModuleEnabled);
            DrawFloat("Velocity X##psvel", ps.VelOverLifeX, v => ps.VelOverLifeX = v, 0.05f, -50f, 50f);
            DrawFloat("Velocity Y##psvel", ps.VelOverLifeY, v => ps.VelOverLifeY = v, 0.05f, -50f, 50f);
            DrawFloat("Velocity Z##psvel", ps.VelOverLifeZ, v => ps.VelOverLifeZ = v, 0.05f, -50f, 50f);
            DrawFloat("Inherit Velocity##psvel", ps.InheritVelocity, v => ps.InheritVelocity = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Force over Lifetime", "constant forces", false))
        {
            bool enabled = ps.ForceOverLifetimeModuleEnabled;
            if (DrawParticleModuleToggle("force", ref enabled)) ps.ForceOverLifetimeModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.ForceOverLifetimeModuleEnabled);
            DrawFloat("Force X##psforce", ps.ForceOverLifeX, v => ps.ForceOverLifeX = v, 0.05f, -100f, 100f);
            DrawFloat("Force Y##psforce", ps.ForceOverLifeY, v => ps.ForceOverLifeY = v, 0.05f, -100f, 100f);
            DrawFloat("Force Z##psforce", ps.ForceOverLifeZ, v => ps.ForceOverLifeZ = v, 0.05f, -100f, 100f);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Limit Velocity over Lifetime", "speed cap", false))
        {
            bool enabled = ps.LimitVelocityOverLifetimeModuleEnabled;
            if (DrawParticleModuleToggle("limitvelocity", ref enabled)) ps.LimitVelocityOverLifetimeModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.LimitVelocityOverLifetimeModuleEnabled);
            DrawFloat("Limit##pslimitvel", ps.LimitVelocity, v => ps.LimitVelocity = Math.Max(0.001f, v), 0.05f, 0.001f, 500f);
            DrawFloat("Dampen##pslimitvel", ps.LimitVelocityDampen, v => ps.LimitVelocityDampen = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Noise", "organic motion", false))
        {
            bool enabled = ps.NoiseModuleEnabled;
            if (DrawParticleModuleToggle("noise", ref enabled)) ps.NoiseModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.NoiseModuleEnabled);
            DrawFloat("Strength##psnoise", ps.TurbulenceStrength, v => ps.TurbulenceStrength = Math.Max(0f, v), 0.05f, 0f, 20f);
            DrawFloat("Frequency##psnoise", ps.TurbulenceFrequency, v => ps.TurbulenceFrequency = Math.Max(0.01f, v), 0.05f, 0.01f, 20f);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Texture Sheet Animation", "sprite atlas", false))
        {
            DrawAssetSlot("Texture##pstexture", ps.TexturePath, "None (Texture)", path => ps.TexturePath = path ?? "", MaterialAsset.IsTexturePath);
            if (!string.IsNullOrEmpty(ps.TexturePath))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear##pstexture")) ps.TexturePath = "";
            }

            bool enabled = ps.TextureSheetAnimationModuleEnabled;
            if (DrawParticleModuleToggle("sheet", ref enabled)) ps.TextureSheetAnimationModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.TextureSheetAnimationModuleEnabled);
            DrawFloat("Columns##pssheet", ps.SheetColumns, v => ps.SheetColumns = Math.Max(1, (int)v), 1f, 1f, 64f);
            DrawFloat("Rows##pssheet", ps.SheetRows, v => ps.SheetRows = Math.Max(1, (int)v), 1f, 1f, 64f);
            DrawFloat("Frame Rate##pssheet", ps.SheetFrameRate, v => ps.SheetFrameRate = Math.Max(0.01f, v), 0.5f, 0.01f, 120f);
            ImGui.TextDisabled($"{Math.Max(1, ps.SheetColumns * ps.SheetRows)} frames");
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Renderer", "billboard, blend and sorting"))
        {
            bool enabled = ps.RendererModuleEnabled;
            if (DrawParticleModuleToggle("renderer", ref enabled)) ps.RendererModuleEnabled = enabled;
            ImGui.BeginDisabled(!ps.RendererModuleEnabled);
            DrawAssetSlot("Material##psrenderer", ps.MaterialPath, "None (Material)", path => ps.MaterialPath = path ?? "", MaterialAsset.IsMaterialPath);
            if (!string.IsNullOrWhiteSpace(ps.MaterialPath))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear##psmat")) ps.MaterialPath = "";
            }
            DrawEnumCombo("Render Mode##psrenderer", ps.RenderMode, v =>
            {
                ps.RenderMode = v;
                ps.StretchedBillboard = v == ParticleRenderMode.StretchedBillboard;
            });
            DrawEnumCombo("Blend Mode##psrenderer", ps.BlendMode, v => ps.BlendMode = v);
            DrawFloat("HDR Intensity##psrenderer", ps.HdrIntensity, v => ps.HdrIntensity = Math.Clamp(v, 0f, 32f), 0.05f, 0f, 32f);
            bool sort = ps.SortParticles;
            if (SmallCheckbox("Sort Particles##psrenderer", ref sort)) ps.SortParticles = sort;
            DrawEnumCombo("Sort Mode##psrenderer", ps.SortMode, v => ps.SortMode = v);
            bool allowRoll = ps.AllowRoll;
            if (SmallCheckbox("Allow Roll##psrenderer", ref allowRoll)) ps.AllowRoll = allowRoll;
            bool flipU = ps.FlipU;
            if (SmallCheckbox("Flip U##psrenderer", ref flipU)) ps.FlipU = flipU;
            bool flipV = ps.FlipV;
            if (SmallCheckbox("Flip V##psrenderer", ref flipV)) ps.FlipV = flipV;
            DrawFloat("Pivot X##psrenderer", ps.PivotX, v => ps.PivotX = Math.Clamp(v, -1f, 1f), 0.01f, -1f, 1f);
            DrawFloat("Pivot Y##psrenderer", ps.PivotY, v => ps.PivotY = Math.Clamp(v, -1f, 1f), 0.01f, -1f, 1f);
            bool soft = ps.SoftParticles;
            if (SmallCheckbox("Soft Particles##psrenderer", ref soft)) ps.SoftParticles = soft;
            if (ps.SoftParticles)
                DrawFloat("Soft Range##psrenderer", ps.SoftParticleRange, v => ps.SoftParticleRange = Math.Max(0.001f, v), 0.01f, 0.001f, 10f);
            DrawFloat("Sorting Fudge##psrenderer", ps.SortingFudge, v => ps.SortingFudge = (int)v, 1f, -1000f, 1000f);
            if (ps.RenderMode == ParticleRenderMode.StretchedBillboard || ps.StretchedBillboard)
            {
                DrawFloat("Speed Scale##psstretched", ps.StretchSpeedScale, v => ps.StretchSpeedScale = Math.Max(0f, v), 0.01f, 0f, 5f);
                DrawFloat("Length Scale##psstretched", ps.StretchLengthScale, v => ps.StretchLengthScale = Math.Max(0f, v), 0.05f, 0f, 10f);
            }
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Trails", "ribbons behind particles", false))
        {
            bool enabled = ps.TrailsModuleEnabled || ps.TrailEnabled;
            if (DrawParticleModuleToggle("trails", ref enabled))
            {
                ps.TrailsModuleEnabled = enabled;
                ps.TrailEnabled = enabled;
            }
            ImGui.BeginDisabled(!enabled);
            DrawFloat("Lifetime##pstrails", ps.TrailLifetime, v => ps.TrailLifetime = Math.Max(0.01f, v), 0.01f, 0.01f, 30f);
            DrawFloat("Width Start##pstrails", ps.TrailWidthStart, v => ps.TrailWidthStart = Math.Max(0f, v), 0.005f, 0f, 10f);
            DrawFloat("Width End##pstrails", ps.TrailWidthEnd, v => ps.TrailWidthEnd = Math.Max(0f, v), 0.005f, 0f, 10f);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Collision", "scene interaction", false))
        {
            bool enabled = ps.ParticleCollision;
            if (DrawParticleModuleToggle("collision", ref enabled)) ps.ParticleCollision = enabled;
            ImGui.BeginDisabled(!ps.ParticleCollision);
            DrawFloat("Bounciness##pscollision", ps.CollisionBounciness, v =>
            {
                ps.CollisionBounciness = Math.Clamp(v, 0f, 1f);
                ps.Collision.Bounciness = ps.CollisionBounciness;
            }, 0.01f, 0f, 1f);
            DrawFloat("Dampen##pscollision", ps.CollisionDampen, v =>
            {
                ps.CollisionDampen = Math.Clamp(v, 0f, 1f);
                ps.Collision.Dampen = ps.CollisionDampen;
            }, 0.01f, 0f, 1f);
            ImGui.EndDisabled();
        }

        if (DrawParticleSection("Sub Emitters", "birth and death events", false))
        {
            DrawString("Birth System Id##pssub", ps.SubEmitterBirth, v => ps.SubEmitterBirth = v);
            DrawString("Death System Id##pssub", ps.SubEmitterDeath, v => ps.SubEmitterDeath = v);
            DrawFloat("Emit Count##pssub", ps.SubEmitterCount, v => ps.SubEmitterCount = Math.Max(1, (int)v), 1f, 1f, 500f);
        }

        if (DrawParticleSection("LOD / Stop Action", "performance and finish", false))
        {
            DrawFloat("LOD Start Distance##pslod", ps.LODDistance, v => ps.LODDistance = Math.Max(0f, v), 1f, 0f, 5000f);
            DrawFloat("LOD Max Distance##pslod", ps.LODDistanceMax, v => ps.LODDistanceMax = Math.Max(ps.LODDistance, v), 1f, 0f, 5000f);
            DrawEnumCombo("Stop Action##psstop", ps.StopAction, v => ps.StopAction = v);
        }

        ImGui.Unindent(10f);
        ps.SyncLegacyFieldsToModules();
        ImGui.PopStyleVar();
    }

private void DrawParticleToolbar(GrokoEngine.ParticleSystem ps)
    {
        float width = ImGui.GetContentRegionAvail().X;
        float fill = ps.MaxParticles <= 0 ? 0f : Math.Clamp(ps.Particles.Count / (float)ps.MaxParticles, 0f, 1f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vec4(0.125f, 0.13f, 0.14f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vec4(0.26f, 0.28f, 0.30f, 1f));
        ImGui.BeginChild("##particleToolbar", new Vector2(width, 104f), ImGuiChildFlags.None);
        ImGui.Indent(10f);

        var statusColor = ps.IsPlaying ? new Vec4(0.22f, 0.88f, 0.50f, 1f) : new Vec4(0.68f, 0.68f, 0.68f, 1f);
        ImGui.TextColored(statusColor, ps.IsPlaying ? "Playing" : "Stopped");
        ImGui.SameLine();
        ImGui.TextDisabled($"{ps.Particles.Count}/{ps.MaxParticles}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Time {ps.Time:0.00}s");

        ImGui.ProgressBar(fill, new Vector2(-1f, 7f), "");
        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 5f));
        if (ImGui.Button(ps.IsPlaying ? "Stop##psplay" : "Play##psplay", new Vector2(76f, 0f)))
        {
            if (ps.IsPlaying) ps.Stop(); else ps.Play();
        }
        ImGui.SameLine();
        if (ImGui.Button("Restart##psplay", new Vector2(76f, 0f))) ps.Restart();
        ImGui.SameLine();
        if (ImGui.Button("Emit 1##psplay", new Vector2(76f, 0f))) ps.EmitOne();
        ImGui.SameLine();
        if (ImGui.Button("Sim +1s##psplay", new Vector2(82f, 0f))) ps.Simulate(1.0, restart: false);
        ImGui.PopStyleVar();

        ImGui.Spacing();
        DrawParticleMiniStat("Space", ps.SimulationSpace.ToString());
        ImGui.SameLine();
        DrawParticleMiniStat("Shape", ps.Shape.ToString());
        ImGui.SameLine();
        DrawParticleMiniStat("Render", ps.RenderMode.ToString());

        ImGui.Unindent(10f);
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.Spacing();
    }

private static bool DrawParticleSection(string title, string hint, bool defaultOpen = true)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 6f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vec4(0.17f, 0.18f, 0.19f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vec4(0.23f, 0.25f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vec4(0.28f, 0.31f, 0.34f, 1f));
        bool open = ImGui.CollapsingHeader($"{title}##pssection{title}", defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        if (open)
        {
            ImGui.PushStyleColor(ImGuiCol.Separator, new Vec4(0.26f, 0.28f, 0.30f, 1f));
            ImGui.Separator();
            ImGui.PopStyleColor();
        }
        return open;
    }

private static bool DrawParticleModuleToggle(string id, ref bool enabled)
    {
        bool value = enabled;
        FieldRow("Active");
        ImGui.PushStyleColor(ImGuiCol.CheckMark, value ? new Vec4(0.28f, 0.72f, 1f, 1f) : new Vec4(0.45f, 0.45f, 0.45f, 1f));
        bool changed = ImGui.Checkbox("##psmodule" + id, ref value);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextDisabled(value ? "Enabled" : "Disabled");
        if (changed) enabled = value;
        ImGui.Spacing();
        return changed;
    }

private static void DrawParticleMiniStat(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

private static void DrawParticleScalarMode(
        string id,
        string modeLabel,
        ParticleValueMode mode,
        Action<ParticleValueMode> setMode,
        string valueLabel,
        float minValue,
        float maxValue,
        Action<float, float> setValues,
        float curveMid,
        float curveMidValue,
        Action<float, float> setCurve,
        float speed,
        float min,
        float max)
    {
        DrawParticleValueModeCombo(modeLabel + "##" + id, mode, setMode);
        if (mode is ParticleValueMode.RandomBetweenTwoConstants or ParticleValueMode.RandomBetweenTwoCurves)
        {
            DrawFloat2(valueLabel + " Min / Max##" + id, minValue, maxValue,
                (a, b) => setValues(Math.Clamp(Math.Min(a, b), min, max), Math.Clamp(Math.Max(a, b), min, max)),
                speed, min, max);
        }
        else
        {
            DrawFloat(valueLabel + "##" + id, maxValue,
                v => setValues(v, v), speed, min, max);
        }

        if (mode is ParticleValueMode.Curve or ParticleValueMode.RandomBetweenTwoCurves)
            DrawParticleCurveControls(valueLabel + " Curve", id + "curve", curveMid, curveMidValue, setCurve);
    }

private static void DrawParticleValueModeCombo(string label, ParticleValueMode value, Action<ParticleValueMode> set)
    {
        FieldRow(label);
        string[] names =
        {
            "Constant",
            "Random Between Two Constants",
            "Curve",
            "Random Between Two Curves"
        };
        int index = Math.Clamp((int)value, 0, names.Length - 1);
        if (ImGui.Combo("##" + label, ref index, names, names.Length))
            set((ParticleValueMode)index);
    }

private static void DrawParticleCurveControls(string label, string id, float mid, float midValue, Action<float, float> set)
    {
        DrawFloat("Curve Time##" + id, mid, v => set(v, midValue), 0.01f, 0f, 1f);
        DrawFloat("Curve Value##" + id, midValue, v => set(mid, v), 0.01f, 0f, 4f);
        ImGui.Indent(8f);
        DrawMiniCurve(mid, midValue, id + "preview");
        ImGui.Unindent(8f);
    }

private static void PrepareParticleGradientForInspector(GrokoEngine.ParticleSystem ps)
    {
        ps.ColorKeyCount = Math.Clamp(ps.ColorKeyCount, 2, 4);
        if (ps.ColorKeyCount == 2)
        {
            ps.CK1T = 0f;
            ps.CK1R = ps.ColorStartR; ps.CK1G = ps.ColorStartG; ps.CK1B = ps.ColorStartB; ps.CK1A = ps.ColorStartA;
            ps.CK2T = 1f;
            ps.CK2R = ps.ColorEndR; ps.CK2G = ps.ColorEndG; ps.CK2B = ps.ColorEndB; ps.CK2A = ps.ColorEndA;
        }
        else
        {
            SyncParticleColorsFromGradientEdges(ps);
        }

        particleGradientSelectedKey = Math.Clamp(particleGradientSelectedKey, 1, ps.ColorKeyCount);
    }

private static void DrawParticleColor255Row(string label, float r, float g, float b, float a, Action<float, float, float, float> set)
    {
        FieldRow(label);
        ImGui.PushID(label);
        int ri = ToByte(r);
        int gi = ToByte(g);
        int bi = ToByte(b);
        int ai = ToByte(a);
        bool changed = false;

        float available = ImGui.GetContentRegionAvail().X;
        float swatchW = 24f;
        float channelW = Math.Clamp((available - swatchW - 58f) / 4f, 34f, 52f);
        changed |= DrawParticleByteField("R", ref ri, channelW);
        ImGui.SameLine(0f, 4f);
        changed |= DrawParticleByteField("G", ref gi, channelW);
        ImGui.SameLine(0f, 4f);
        changed |= DrawParticleByteField("B", ref bi, channelW);
        ImGui.SameLine(0f, 4f);
        changed |= DrawParticleByteField("A", ref ai, channelW);
        ImGui.SameLine(0f, 6f);

        var color = new Vec4(ri / 255f, gi / 255f, bi / 255f, ai / 255f);
        if (ImGui.ColorButton("##swatch", color, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoTooltip, new Vector2(swatchW, 20f)))
            ImGui.OpenPopup("##picker");
        if (ImGui.BeginPopup("##picker"))
        {
            if (ImGui.ColorPicker4("##color", ref color,
                    ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs))
            {
                ri = ToByte(color.X);
                gi = ToByte(color.Y);
                bi = ToByte(color.Z);
                ai = ToByte(color.W);
                changed = true;
            }
            ImGui.EndPopup();
        }

        if (changed)
            set(ri / 255f, gi / 255f, bi / 255f, ai / 255f);
        ImGui.PopID();
    }

private static bool DrawParticleByteField(string channel, ref int value, float width)
    {
        bool changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(channel);
        ImGui.SameLine(0f, 2f);
        ImGui.SetNextItemWidth(width);
        changed |= ImGui.DragInt("##" + channel, ref value, 1f, 0, 255);
        value = Math.Clamp(value, 0, 255);
        return changed;
    }

private static int ToByte(float value) => (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

private static void DrawParticleGradientField(GrokoEngine.ParticleSystem ps)
    {
        PrepareParticleGradientForInspector(ps);
        FieldRow("Gradient");
        float width = Math.Max(132f, ImGui.GetContentRegionAvail().X - 8f);
        if (DrawParticleGradientBarControl(ps, "##psgradientfield", new Vector2(width, 24f), showMarkers: false, editable: false))
            ImGui.OpenPopup("Gradient Editor##pscolor");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click para abrir el Gradient Editor");
    }

private static void DrawParticleGradientEditorPopup(GrokoEngine.ParticleSystem ps)
    {
        bool open = true;
        ImGui.SetNextWindowSize(new Vector2(370f, 430f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Gradient Editor##pscolor", ref open, ImGuiWindowFlags.NoSavedSettings))
            return;

        string[] modes = { "Blend (Classic)" };
        int mode = 0;
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Mode");
        ImGui.SameLine(86f);
        ImGui.SetNextItemWidth(-1f);
        ImGui.Combo("##psgradientmode", ref mode, modes, modes.Length);

        ImGui.Spacing();
        float width = Math.Max(260f, ImGui.GetContentRegionAvail().X);
        DrawParticleGradientBarControl(ps, "##psgradienteditorbar", new Vector2(width, 70f), showMarkers: true, editable: true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click para seleccionar una key. Arrastra para moverla. Doble click para agregar.");

        ImGui.Separator();
        particleGradientSelectedKey = Math.Clamp(particleGradientSelectedKey, 1, ps.ColorKeyCount);
        var key = GetParticleGradientKey(ps, particleGradientSelectedKey);
        ImGui.TextDisabled($"Key {particleGradientSelectedKey} / {ps.ColorKeyCount}");

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Color");
        ImGui.SameLine(86f);
        var color = key.Color;
        if (ColorField4("##psgradientselectedcolor", ref color))
        {
            key.R = color.X; key.G = color.Y; key.B = color.Z; key.A = color.W;
            SetParticleGradientKey(ps, particleGradientSelectedKey, key);
            SyncParticleColorsFromGradientEdges(ps);
        }

        float location = key.T * 100f;
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Location");
        ImGui.SameLine(86f);
        ImGui.SetNextItemWidth(88f);
        if (ImGui.DragFloat("##psgradientlocation", ref location, 0.1f, 0f, 100f, "%.1f"))
        {
            key.T = Math.Clamp(location / 100f, 0f, 1f);
            SetParticleGradientKey(ps, particleGradientSelectedKey, key);
            SyncParticleColorsFromGradientEdges(ps);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(ps.ColorKeyCount >= 4);
        if (ImGui.Button("+ Key##psgradientadd", new Vector2(74f, 0f)))
            AddParticleGradientKey(ps, Math.Clamp(key.T + 0.15f, 0f, 1f));
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(ps.ColorKeyCount <= 2);
        if (ImGui.Button("- Key##psgradientremove", new Vector2(74f, 0f)))
            RemoveParticleGradientKey(ps, particleGradientSelectedKey);
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.SeparatorText("Presets");
        DrawParticleGradientPresetButton("Fire##pspreset", new[]
        {
            new ParticleGradientKeyData(0f, 1f, 0.75f, 0.12f, 1f),
            new ParticleGradientKeyData(0.45f, 1f, 0.18f, 0.02f, 0.75f),
            new ParticleGradientKeyData(1f, 0.15f, 0.02f, 0.0f, 0f)
        }, ps);
        ImGui.SameLine();
        DrawParticleGradientPresetButton("White Red##pspreset", new[]
        {
            new ParticleGradientKeyData(0f, 1f, 1f, 1f, 1f),
            new ParticleGradientKeyData(0.45f, 1f, 0.75f, 0.78f, 1f),
            new ParticleGradientKeyData(1f, 1f, 0f, 0f, 1f)
        }, ps);
        ImGui.SameLine();
        DrawParticleGradientPresetButton("Fade##pspreset", new[]
        {
            new ParticleGradientKeyData(0f, 1f, 1f, 1f, 1f),
            new ParticleGradientKeyData(1f, 1f, 1f, 1f, 0f)
        }, ps);
        ImGui.SameLine();
        DrawParticleGradientPresetButton("Smoke##pspreset", new[]
        {
            new ParticleGradientKeyData(0f, 0.22f, 0.22f, 0.22f, 0.75f),
            new ParticleGradientKeyData(0.55f, 0.55f, 0.55f, 0.55f, 0.35f),
            new ParticleGradientKeyData(1f, 0.12f, 0.12f, 0.12f, 0f)
        }, ps);

        ImGui.Spacing();
        if (ImGui.Button("Close##psgradientclose", new Vector2(-1f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

private static bool DrawParticleGradientBarControl(GrokoEngine.ParticleSystem ps, string id, Vector2 size, bool showMarkers, bool editable)
    {
        size.X = Math.Max(80f, size.X);
        size.Y = Math.Max(showMarkers ? 44f : 18f, size.Y);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        float markerH = showMarkers ? 10f : 0f;
        var barMin = new Vector2(min.X, min.Y + markerH);
        var barMax = new Vector2(max.X, max.Y - markerH);
        var draw = ImGui.GetWindowDrawList();

        DrawParticleChecker(draw, barMin, barMax);
        const int steps = 80;
        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;
            var c = ps.SampleGradient((t0 + t1) * 0.5f);
            uint col = ImGui.GetColorU32(new Vec4(c.r, c.g, c.b, c.a));
            draw.AddRectFilled(
                new Vector2(barMin.X + (barMax.X - barMin.X) * t0, barMin.Y),
                new Vector2(barMin.X + (barMax.X - barMin.X) * t1 + 1f, barMax.Y),
                col);
        }
        draw.AddRect(barMin, barMax, ImGui.GetColorU32(new Vec4(0f, 0f, 0f, 0.8f)), 2f);

        if (showMarkers)
            DrawParticleGradientMarkers(ps, draw, barMin, barMax);

        ImGui.InvisibleButton(id, size);
        bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (editable)
        {
            var io = ImGui.GetIO();
            float t = Math.Clamp((io.MousePos.X - barMin.X) / Math.Max(1f, barMax.X - barMin.X), 0f, 1f);
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ps.ColorKeyCount < 4)
            {
                AddParticleGradientKey(ps, t);
            }
            else if (clicked)
            {
                particleGradientSelectedKey = FindNearestParticleGradientKey(ps, t);
            }

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                particleGradientSelectedKey = Math.Clamp(particleGradientSelectedKey, 1, ps.ColorKeyCount);
                var key = GetParticleGradientKey(ps, particleGradientSelectedKey);
                key.T = t;
                SetParticleGradientKey(ps, particleGradientSelectedKey, key);
                SyncParticleColorsFromGradientEdges(ps);
            }
        }
        return clicked;
    }

private static void DrawParticleChecker(ImDrawListPtr draw, Vector2 min, Vector2 max)
    {
        uint a = ImGui.GetColorU32(new Vec4(0.18f, 0.18f, 0.18f, 1f));
        uint b = ImGui.GetColorU32(new Vec4(0.28f, 0.28f, 0.28f, 1f));
        const float cell = 6f;
        int yIndex = 0;
        for (float y = min.Y; y < max.Y; y += cell, yIndex++)
        {
            int xIndex = 0;
            for (float x = min.X; x < max.X; x += cell, xIndex++)
            {
                var c = ((xIndex + yIndex) & 1) == 0 ? a : b;
                draw.AddRectFilled(new Vector2(x, y), new Vector2(Math.Min(x + cell, max.X), Math.Min(y + cell, max.Y)), c);
            }
        }
    }

private static void DrawParticleGradientMarkers(GrokoEngine.ParticleSystem ps, ImDrawListPtr draw, Vector2 barMin, Vector2 barMax)
    {
        int count = Math.Clamp(ps.ColorKeyCount, 2, 4);
        for (int i = 1; i <= count; i++)
        {
            var key = GetParticleGradientKey(ps, i);
            float x = barMin.X + (barMax.X - barMin.X) * Math.Clamp(key.T, 0f, 1f);
            uint fill = ImGui.GetColorU32(new Vec4(key.R, key.G, key.B, 1f));
            uint border = ImGui.GetColorU32(i == particleGradientSelectedKey
                ? new Vec4(0.28f, 0.72f, 1f, 1f)
                : new Vec4(0.04f, 0.04f, 0.04f, 1f));
            var topA = new Vector2(x - 4f, barMin.Y - 8f);
            var topB = new Vector2(x + 4f, barMin.Y - 8f);
            var topC = new Vector2(x, barMin.Y - 1f);
            draw.AddTriangleFilled(topA, topB, topC, fill);
            draw.AddTriangle(topA, topB, topC, border, 1.4f);

            var botA = new Vector2(x - 4f, barMax.Y + 8f);
            var botB = new Vector2(x + 4f, barMax.Y + 8f);
            var botC = new Vector2(x, barMax.Y + 1f);
            uint alphaFill = ImGui.GetColorU32(new Vec4(1f, 1f, 1f, Math.Clamp(key.A, 0.2f, 1f)));
            draw.AddTriangleFilled(botA, botB, botC, alphaFill);
            draw.AddTriangle(botA, botB, botC, border, 1.4f);
        }
    }

private static void DrawParticleGradientPresetButton(string label, ParticleGradientKeyData[] keys, GrokoEngine.ParticleSystem ps)
    {
        ImGui.PushID(label);
        var size = new Vector2(74f, 16f);
        var min = ImGui.GetCursorScreenPos();
        DrawParticleGradientPresetPreview(min, min + size, keys);
        if (ImGui.InvisibleButton("##preset", size))
        {
            ApplyParticleGradientPreset(ps, keys);
            particleGradientSelectedKey = 1;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(VisibleLabel(label));
        ImGui.PopID();
    }

private static void DrawParticleGradientPresetPreview(Vector2 min, Vector2 max, ParticleGradientKeyData[] keys)
    {
        var draw = ImGui.GetWindowDrawList();
        DrawParticleChecker(draw, min, max);
        const int steps = 24;
        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;
            var c = SampleParticlePreset(keys, (t0 + t1) * 0.5f);
            draw.AddRectFilled(
                new Vector2(min.X + (max.X - min.X) * t0, min.Y),
                new Vector2(min.X + (max.X - min.X) * t1 + 1f, max.Y),
                ImGui.GetColorU32(c));
        }
        draw.AddRect(min, max, ImGui.GetColorU32(new Vec4(0f, 0f, 0f, 0.85f)));
    }

private static Vec4 SampleParticlePreset(ParticleGradientKeyData[] keys, float t)
    {
        if (keys.Length == 0) return new Vec4(1f, 1f, 1f, 1f);
        var ordered = keys.OrderBy(k => k.T).ToArray();
        if (t <= ordered[0].T) return ordered[0].Color;
        if (t >= ordered[^1].T) return ordered[^1].Color;
        for (int i = 0; i < ordered.Length - 1; i++)
        {
            if (t >= ordered[i].T && t <= ordered[i + 1].T)
            {
                float f = (t - ordered[i].T) / Math.Max(0.0001f, ordered[i + 1].T - ordered[i].T);
                return new Vec4(
                    Lerp01(ordered[i].R, ordered[i + 1].R, f),
                    Lerp01(ordered[i].G, ordered[i + 1].G, f),
                    Lerp01(ordered[i].B, ordered[i + 1].B, f),
                    Lerp01(ordered[i].A, ordered[i + 1].A, f));
            }
        }
        return ordered[^1].Color;
    }

private static float Lerp01(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

private static void ApplyParticleGradientPreset(GrokoEngine.ParticleSystem ps, ParticleGradientKeyData[] keys)
    {
        int count = Math.Clamp(keys.Length, 2, 4);
        ps.ColorKeyCount = count;
        for (int i = 0; i < count; i++)
            SetParticleGradientKey(ps, i + 1, keys[i]);
        SyncParticleColorsFromGradientEdges(ps);
    }

private static int FindNearestParticleGradientKey(GrokoEngine.ParticleSystem ps, float t)
    {
        int count = Math.Clamp(ps.ColorKeyCount, 2, 4);
        int nearest = 1;
        float best = float.MaxValue;
        for (int i = 1; i <= count; i++)
        {
            float d = Math.Abs(GetParticleGradientKey(ps, i).T - t);
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }
        return nearest;
    }

private static void AddParticleGradientKey(GrokoEngine.ParticleSystem ps, float t)
    {
        if (ps.ColorKeyCount >= 4) return;
        var c = ps.SampleGradient(t);
        ps.ColorKeyCount++;
        particleGradientSelectedKey = ps.ColorKeyCount;
        SetParticleGradientKey(ps, particleGradientSelectedKey, new ParticleGradientKeyData(t, c.r, c.g, c.b, c.a));
        SyncParticleColorsFromGradientEdges(ps);
    }

private static void RemoveParticleGradientKey(GrokoEngine.ParticleSystem ps, int keyIndex)
    {
        if (ps.ColorKeyCount <= 2) return;
        int count = Math.Clamp(ps.ColorKeyCount, 2, 4);
        keyIndex = Math.Clamp(keyIndex, 1, count);
        for (int i = keyIndex; i < count; i++)
            SetParticleGradientKey(ps, i, GetParticleGradientKey(ps, i + 1));
        ps.ColorKeyCount = count - 1;
        particleGradientSelectedKey = Math.Clamp(keyIndex, 1, ps.ColorKeyCount);
        SyncParticleColorsFromGradientEdges(ps);
    }

private static void SetParticleGradientEndpoint(GrokoEngine.ParticleSystem ps, bool start, float r, float g, float b, float a)
    {
        if (start)
        {
            ps.ColorStartR = r; ps.ColorStartG = g; ps.ColorStartB = b; ps.ColorStartA = a;
        }
        else
        {
            ps.ColorEndR = r; ps.ColorEndG = g; ps.ColorEndB = b; ps.ColorEndA = a;
        }

        int keyIndex = FindGradientEdgeKey(ps, start);
        var key = GetParticleGradientKey(ps, keyIndex);
        key.T = start ? 0f : 1f;
        key.R = r; key.G = g; key.B = b; key.A = a;
        SetParticleGradientKey(ps, keyIndex, key);
        SyncParticleColorsFromGradientEdges(ps);
    }

private static int FindGradientEdgeKey(GrokoEngine.ParticleSystem ps, bool start)
    {
        int count = Math.Clamp(ps.ColorKeyCount, 2, 4);
        int index = 1;
        float edge = GetParticleGradientKey(ps, 1).T;
        for (int i = 2; i <= count; i++)
        {
            float t = GetParticleGradientKey(ps, i).T;
            if ((start && t < edge) || (!start && t > edge))
            {
                edge = t;
                index = i;
            }
        }
        return index;
    }

private static void SyncParticleColorsFromGradientEdges(GrokoEngine.ParticleSystem ps)
    {
        int startIndex = FindGradientEdgeKey(ps, true);
        int endIndex = FindGradientEdgeKey(ps, false);
        var start = GetParticleGradientKey(ps, startIndex);
        var end = GetParticleGradientKey(ps, endIndex);
        ps.ColorStartR = start.R; ps.ColorStartG = start.G; ps.ColorStartB = start.B; ps.ColorStartA = start.A;
        ps.ColorEndR = end.R; ps.ColorEndG = end.G; ps.ColorEndB = end.B; ps.ColorEndA = end.A;
    }

private static ParticleGradientKeyData GetParticleGradientKey(GrokoEngine.ParticleSystem ps, int key)
    {
        return key switch
        {
            1 => new ParticleGradientKeyData(ps.CK1T, ps.CK1R, ps.CK1G, ps.CK1B, ps.CK1A),
            2 => new ParticleGradientKeyData(ps.CK2T, ps.CK2R, ps.CK2G, ps.CK2B, ps.CK2A),
            3 => new ParticleGradientKeyData(ps.CK3T, ps.CK3R, ps.CK3G, ps.CK3B, ps.CK3A),
            4 => new ParticleGradientKeyData(ps.CK4T, ps.CK4R, ps.CK4G, ps.CK4B, ps.CK4A),
            _ => new ParticleGradientKeyData(0f, 1f, 1f, 1f, 1f)
        };
    }

private static void SetParticleGradientKey(GrokoEngine.ParticleSystem ps, int key, ParticleGradientKeyData data)
    {
        data = new ParticleGradientKeyData(data.T, data.R, data.G, data.B, data.A);
        switch (key)
        {
            case 1:
                ps.CK1T = data.T; ps.CK1R = data.R; ps.CK1G = data.G; ps.CK1B = data.B; ps.CK1A = data.A;
                break;
            case 2:
                ps.CK2T = data.T; ps.CK2R = data.R; ps.CK2G = data.G; ps.CK2B = data.B; ps.CK2A = data.A;
                break;
            case 3:
                ps.CK3T = data.T; ps.CK3R = data.R; ps.CK3G = data.G; ps.CK3B = data.B; ps.CK3A = data.A;
                break;
            case 4:
                ps.CK4T = data.T; ps.CK4R = data.R; ps.CK4G = data.G; ps.CK4B = data.B; ps.CK4A = data.A;
                break;
        }
    }

private static void DrawParticleGradientKey(GrokoEngine.ParticleSystem ps, int key)
    {
        ImGui.PushID(key);
        ImGui.TextDisabled($"Key {key}");
        switch (key)
        {
            case 1:
                DrawFloat("Time##key", ps.CK1T, v => ps.CK1T = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
                DrawParticleColor("Color##key", ps.CK1R, ps.CK1G, ps.CK1B, ps.CK1A, (r, g, b, a) => { ps.CK1R = r; ps.CK1G = g; ps.CK1B = b; ps.CK1A = a; });
                break;
            case 2:
                DrawFloat("Time##key", ps.CK2T, v => ps.CK2T = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
                DrawParticleColor("Color##key", ps.CK2R, ps.CK2G, ps.CK2B, ps.CK2A, (r, g, b, a) => { ps.CK2R = r; ps.CK2G = g; ps.CK2B = b; ps.CK2A = a; });
                break;
            case 3:
                DrawFloat("Time##key", ps.CK3T, v => ps.CK3T = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
                DrawParticleColor("Color##key", ps.CK3R, ps.CK3G, ps.CK3B, ps.CK3A, (r, g, b, a) => { ps.CK3R = r; ps.CK3G = g; ps.CK3B = b; ps.CK3A = a; });
                break;
            case 4:
                DrawFloat("Time##key", ps.CK4T, v => ps.CK4T = Math.Clamp(v, 0f, 1f), 0.01f, 0f, 1f);
                DrawParticleColor("Color##key", ps.CK4R, ps.CK4G, ps.CK4B, ps.CK4A, (r, g, b, a) => { ps.CK4R = r; ps.CK4G = g; ps.CK4B = b; ps.CK4A = a; });
                break;
        }
        ImGui.Separator();
        ImGui.PopID();
    }

private static void DrawParticleColor(string label, float r, float g, float b, float alpha,
                                           Action<float, float, float, float> set)
    {
        FieldRow(label);
        var color = new System.Numerics.Vector4(r, g, b, alpha);
        if (ImGui.ColorEdit4("##" + label, ref color))
            set(color.X, color.Y, color.Z, color.W);
    }

private static void DrawParticleGradientPreview(GrokoEngine.ParticleSystem ps)
    {
        var size = new Vector2(Math.Max(120f, ImGui.GetContentRegionAvail().X), 18f);
        var min = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        int steps = 48;
        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;
            var c = ps.SampleGradient(t0);
            uint col = ImGui.GetColorU32(new System.Numerics.Vector4(c.r, c.g, c.b, c.a));
            draw.AddRectFilled(
                min + new Vector2(size.X * t0, 0f),
                min + new Vector2(size.X * t1 + 1f, size.Y),
                col);
        }
        draw.AddRect(min, min + size, ImGui.GetColorU32(new System.Numerics.Vector4(0f, 0f, 0f, 0.75f)));
        ImGui.Dummy(size);
    }
}
