using ImGuiNET;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private void DrawCameraInspector(Camera cam)
    {
        if (SectionHeader("Projection"))
        {
            DrawEnumCombo("Projection", cam.Projection, v => cam.Projection = v);
            if (cam.Projection == CameraProjection.Perspective)
            {
                DrawCheckRow("Physical Camera", cam.PhysicalCamera, v => cam.PhysicalCamera = v);
                if (cam.PhysicalCamera)
                {
                    DrawFloat("Focal Length (mm)", cam.FocalLengthMm, v => cam.FocalLengthMm = v, 0.5f, 1f, 1000f);
                    DrawFloat("Sensor Height (mm)", cam.SensorHeightMm, v => cam.SensorHeightMm = v, 0.2f, 1f, 200f);
                }
                else
                {
                    DrawEnumCombo("Field of View Axis", cam.FieldOfViewAxis, v => cam.FieldOfViewAxis = v);
                    DrawFloat("Field of View", cam.FOV, v => cam.FOV = v, 0.5f, 1f, 179f);
                }
            }
            else
            {
                DrawFloat("Size", cam.OrthographicSize, v => cam.OrthographicSize = v, 0.05f, 0.001f, 100000f);
            }

            DrawFloat("Near", cam.NearClip, v => cam.NearClip = v, 0.01f, 0.001f, 100f);
            DrawFloat("Far", cam.FarClip, v => cam.FarClip = v, 1f, 1f, 100000f);
        }

        if (SectionHeader("Rendering"))
        {
            DrawReadOnlyComboRow("Renderer", "Default Renderer (Groko Renderer)");
            DrawCameraRenderingPathCombo(cam);
            DrawCameraDebugViewCombo();
            DrawCheckRow("Post Processing", cam.PostProcessing, v => cam.PostProcessing = v);
            DrawCameraAntiAliasingCombo(cam);
            bool deferredCamera = ResolveRendererRenderingPath(cam) == RendererRenderingPath.Deferred;
            using (new DisabledScope(cam.AntiAliasingMode == CameraAntiAliasingMode.None || deferredCamera))
            {
                int samples = cam.AntiAliasingSamples;
                FieldRow("Samples");
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("##cameraaa", $"{samples}x"))
                {
                    foreach (int option in new[] { 2, 4, 8 })
                    {
                        bool selectedSample = samples == option;
                        if (ImGui.Selectable($"{option}x", selectedSample))
                            cam.AntiAliasingSamples = option;
                        if (selectedSample) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            if (deferredCamera)
                ImGui.TextDisabled("Deferred usa SMAA de postproceso; MSAA solo funciona en Forward/Forward+.");
            DrawCheckRow("Stop NaNs", cam.StopNaNs, v => cam.StopNaNs = v);
            DrawCheckRow("Dithering", cam.Dithering, v => cam.Dithering = v);
            DrawCheckRow("Render Shadows", cam.RenderShadows, v => cam.RenderShadows = v);
            DrawInt("Priority", cam.Priority, v => cam.Priority = v, 1f, -100, 100);
            DrawEnumCombo("Opaque Texture", cam.OpaqueTexture, v => cam.OpaqueTexture = v);
            DrawEnumCombo("Depth Texture", cam.DepthTexture, v => cam.DepthTexture = v);
            DrawCameraMaskCombo(cam);
            DrawCheckRow("Occlusion Culling", cam.OcclusionCulling, v => cam.OcclusionCulling = v);
            DrawCheckRow("Frustum Culling", cam.FrustumCulling, v => cam.FrustumCulling = v);
        }

        if (SectionHeader("Environment"))
        {
            DrawEnumCombo("Background Type", cam.ClearFlags, v => cam.ClearFlags = v);
            if (cam.ClearFlags == CameraClearFlags.SolidColor)
            {
                FieldRow("Background");
                var background = new System.Numerics.Vector4(cam.BackgroundR, cam.BackgroundG, cam.BackgroundB, cam.BackgroundA);
                if (ColorField4("##camera_background", ref background))
                {
                    cam.BackgroundR = Math.Clamp(background.X, 0f, 1f);
                    cam.BackgroundG = Math.Clamp(background.Y, 0f, 1f);
                    cam.BackgroundB = Math.Clamp(background.Z, 0f, 1f);
                    cam.BackgroundA = Math.Clamp(background.W, 0f, 1f);
                }
            }
        }
    }

    private void DrawCameraRenderingPathCombo(Camera cam)
    {
        FieldRow("Rendering Path");
        ImGui.SetNextItemWidth(-1f);

        string preview = cam.RenderingPath switch
        {
            CameraRenderingPath.Forward => "Forward",
            CameraRenderingPath.ForwardPlus => "Forward+",
            CameraRenderingPath.Deferred => "Deferred",
            _ => "Use Project Settings"
        };

        if (ImGui.BeginCombo("##camera_rendering_path", preview))
        {
            DrawCameraRenderingPathOption(cam, CameraRenderingPath.UseProjectSettings, "Use Project Settings", "Usa el modo global de Project Settings > Render.");
            DrawCameraRenderingPathOption(cam, CameraRenderingPath.Forward, "Forward", "Ruta estable y rapida para escenas pequenas/medianas.");
            DrawCameraRenderingPathOption(cam, CameraRenderingPath.ForwardPlus, "Forward+", "Recomendado para muchas luces. Preparado para clustered lighting.");
            DrawCameraRenderingPathOption(cam, CameraRenderingPath.Deferred, "Deferred", "G-buffer para opacos + lighting fullscreen. Usa SMAA; MSAA se reserva para Forward/Forward+.");
            ImGui.EndCombo();
        }

        string help = cam.RenderingPath switch
        {
            CameraRenderingPath.Forward => "Forward: camino clasico, simple y estable.",
            CameraRenderingPath.ForwardPlus => "Forward+: modo moderno para escalar mejor con muchas luces.",
            CameraRenderingPath.Deferred => "Deferred: opacos en G-buffer; usa SMAA. MSAA esta disponible en Forward/Forward+.",
            _ => "Usa el Rendering Path global del proyecto."
        };
        ImGui.TextDisabled(help);
    }

    private static void DrawCameraRenderingPathOption(Camera cam, CameraRenderingPath value, string label, string tooltip)
    {
        bool selected = cam.RenderingPath == value;
        if (ImGui.Selectable(label, selected))
        {
            cam.RenderingPath = value;
            if (value == CameraRenderingPath.Deferred)
            {
                cam.AntiAliasing = true;
                cam.AntiAliasingMode = CameraAntiAliasingMode.SMAA;
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        if (selected)
            ImGui.SetItemDefaultFocus();
    }

    private static void DrawCameraAntiAliasingCombo(Camera cam)
    {
        // TAA queda fuera hasta que el renderer exponga motion vectors e historial por
        // cámara. Mostrarlo antes solo daría una opción que no puede ser correcta.
        CameraAntiAliasingMode[] options =
            [CameraAntiAliasingMode.None, CameraAntiAliasingMode.FXAA, CameraAntiAliasingMode.SMAA];
        if (!options.Contains(cam.AntiAliasingMode))
            cam.AntiAliasingMode = CameraAntiAliasingMode.SMAA;

        FieldRow("Anti-aliasing");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##camera_antialiasing", cam.AntiAliasingMode.ToString()))
        {
            foreach (CameraAntiAliasingMode option in options)
            {
                bool selectedOption = cam.AntiAliasingMode == option;
                if (ImGui.Selectable(option.ToString(), selectedOption))
                {
                    cam.AntiAliasingMode = option;
                    cam.AntiAliasing = option != CameraAntiAliasingMode.None;
                }
                if (selectedOption) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    private void DrawCameraDebugViewCombo()
    {
        FieldRow("Debug View");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##camera_debug_view", LightingDebugViewLabel(lightingDebugView)))
        {
            foreach (LightingDebugView view in Enum.GetValues<LightingDebugView>())
            {
                bool selected = lightingDebugView == view;
                if (ImGui.Selectable(LightingDebugViewLabel(view), selected))
                    lightingDebugView = view;
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Preview del viewport; no modifica la escena.");
    }

    private static void DrawCameraMaskCombo(Camera cam)
    {
        string[] masks = LayerMask.CullingMaskOptions;
        int index = Array.IndexOf(masks, cam.CullingMask);
        if (index < 0) index = 0;
        FieldRow("Culling Mask");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##CameraCullingMask", ref index, masks, masks.Length))
            cam.CullingMask = masks[index];
    }
}
