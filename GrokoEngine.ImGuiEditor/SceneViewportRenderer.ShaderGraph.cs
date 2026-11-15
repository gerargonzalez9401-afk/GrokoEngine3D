using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GrokoShaderGraphPro.Models;
using GrokoShaderGraphPro.Services;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    internal readonly record struct ShaderGraphExposedProperty(string DisplayName, PinType Type, bool IsHdr);

    internal readonly record struct ShaderGraphSamplerBinding(string Name, int Location, int TextureUnitIndex);

    internal readonly record struct ShaderGraphPropertyBinding(string Name, string Type, int Location);


    // Render path para materiales con Shader Graph asignado.
    // Cada .shadergraph compila su propio programa GLSL (vertex fijo +
    // fragment generado por ShaderCodeGenerator) y se dibuja en un pase
    // aparte, reutilizando el VAO/VBO ya subido del batch sólido.
    /// <summary>Metadatos de una propiedad expuesta del Shader Graph para el Inspector (estilo Unity).</summary>

    internal sealed class ShaderGraphMaterialEntry
    {
        public int Program;
        public DateTime FileTime;
        public long NextValidationTick;   // TickCount64: hasta entonces no se re-stat el .shadergraph
        public ShaderGraphModel? Model;
        public string FragmentSource = string.Empty;
        public List<string> Samplers = new();
        public bool NeedsSceneDepth;
        public int ViewProjLocation = -1;
        public int TimeLocation = -1;
        public int ResolutionLocation = -1;
        public int CameraPosLocation = -1;
        public int LightDirLocation = -1;
        public int LightColorLocation = -1;
        public int LightIntensityLocation = -1;
        public int ColorSpaceLinearLocation = -1;
        public int CameraNearLocation = -1;
        public int CameraFarLocation = -1;
        // Sombras direccionales por cascadas (recepción).
        public int ShadowMapLocation = -1;
        public int ShadowEnabledLocation = -1;
        public int ShadowStrengthLocation = -1;
        public int CascadeCountLocation = -1;
        public int ShadowPcfRadiusLocation = -1;
        public int ShadowBiasScaleLocation = -1;
        public int ShadowNormalBiasLocation = -1;
        public int ShadowCameraPosLocation = -1;
        public int[] CascadeLightMvpLocations = new int[5];
        public int[] CascadeSplitLocations = new int[5];
        public int ModelLocation = -1;
        public int UseModelLocation = -1;
        public int UseInstancingLocation = -1;
        public int UseSkinningLocation = -1;
        public int[] BoneMatrixLocations = new int[MaxGpuBones];
        public int SceneDepthSamplerIndex = -1;
        public List<ShaderGraphSamplerBinding> SamplerBindings = new();
        public List<ShaderGraphPropertyBinding> PropertyBindings = new();

        /// <summary>Valores por defecto de cada uniform. Para colores HDR (vec3/vec4 con ColorMode=Hdr)
        /// el RGB se guarda SIN multiplicar por la intensidad, y se añade un elemento extra al final
        /// con la intensidad: vec3 HDR -> [r,g,b,intensity] (4), vec4 HDR -> [r,g,b,a,intensity] (5).</summary>
        public Dictionary<string, float[]> PropertyDefaults = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Solo las propiedades del Blackboard, para "Exposed Properties" en el Inspector.</summary>
        public Dictionary<string, ShaderGraphExposedProperty> ExposedProperties = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ruta de textura por defecto (Blackboard) para cada uniform sampler2D.</summary>
        public Dictionary<string, string> SamplerDefaults = new(StringComparer.OrdinalIgnoreCase);
    }

    private const string ShaderGraphVertexSource = """
#version 330 core
layout(location = 0) in vec3 a_Position;
layout(location = 1) in vec3 a_Normal;
layout(location = 3) in vec2 a_UV;
// Matriz de modelo POR INSTANCIA (divisor 1, locations 6-9): la comparten todas las
// mallas GPU (ver ConfigureGpuMeshVertexAttributes). Permite dibujar N copias de la
// misma malla shader-graph en UN draw call, igual que las mallas con material normal.
layout(location = 6) in mat4 a_InstanceModel;
layout(location = 10) in vec4 a_BoneIndices;
layout(location = 11) in vec4 a_BoneWeights;

uniform mat4 u_ViewProj;
uniform mat4 u_Model;
uniform int u_UseModel;
uniform int u_UseInstancing;
uniform int u_UseSkinning;
uniform mat4 u_Bones[128];

out vec2 v_UV;
out vec3 v_NormalWS;
out vec3 v_WorldPos;
out vec3 v_ObjectPos;
out vec3 v_TangentWS;
out vec4 v_ScreenPos;

void main()
{
    mat4 model = u_UseInstancing != 0 ? a_InstanceModel : (u_UseModel != 0 ? u_Model : mat4(1.0));
    vec4 world = vec4(a_Position, 1.0) * model;
    vec3 normalWS = (vec4(a_Normal, 0.0) * model).xyz;

    if (u_UseSkinning != 0)
    {
        vec4 skinnedPos = vec4(0.0);
        vec3 skinnedNormal = vec3(0.0);
        float totalWeight = 0.0;
        for (int i = 0; i < 4; i++)
        {
            int boneIndex = int(a_BoneIndices[i] + 0.5);
            float weight = a_BoneWeights[i];
            if (boneIndex >= 0 && boneIndex < 128 && weight > 0.0)
            {
                mat4 bone = u_Bones[boneIndex];
                skinnedPos += (vec4(a_Position, 1.0) * bone) * weight;
                skinnedNormal += (vec4(a_Normal, 0.0) * bone).xyz * weight;
                totalWeight += weight;
            }
        }
        if (totalWeight > 0.0001)
        {
            world = skinnedPos;
            normalWS = skinnedNormal;
        }
    }

    v_WorldPos = world.xyz;
    v_ObjectPos = a_Position;
    v_NormalWS = length(normalWS) > 0.0001 ? normalize(normalWS) : vec3(0.0, 1.0, 0.0);
    vec3 tangentOS = abs(a_Normal.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : normalize(cross(vec3(0.0, 1.0, 0.0), a_Normal));
    v_TangentWS = normalize((vec4(tangentOS, 0.0) * model).xyz);
    v_UV = a_UV;
    gl_Position = world * u_ViewProj;
    v_ScreenPos = gl_Position;
}
""";

    private static bool HasShaderGraphRange(List<SolidRange> ranges)
        => ranges.Any(r => r.ShaderGraphPath != null);

    // Pasa al shader del graph la sombra direccional por cascadas (la misma que usa el material estándar),
    // para que los objetos con material de Shader Graph reciban sombras.

    private static bool ShaderGraphModelNeedsSceneDepth(ShaderGraphModel? model)
        => model?.Nodes?.Any(n => n.Kind is NodeKind.SceneDepth or NodeKind.DepthFade) == true;

    private static float ShaderGraphParseFloat(string? s, float fallback)
        => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;


    private bool ShaderGraphRangesNeedSceneDepth(List<SolidRange> ranges)
    {
        foreach (var range in ranges)
        {
            if (range.ShaderGraphPath == null)
                continue;

            var entry = GetShaderGraphEntry(range.ShaderGraphPath);
            if (entry is { NeedsSceneDepth: true })
                return true;
        }

        return false;
    }

    private bool ShaderGraphDynamicDrawsNeedSceneDepth()
    {
        foreach (var draw in shaderGraphDynamicMeshDraws)
        {
            var entry = GetShaderGraphEntry(draw.ShaderGraphPath);
            if (entry is { NeedsSceneDepth: true })
                return true;
        }

        return false;
    }

    private bool ShaderGraphSkinnedDrawsNeedSceneDepth()
    {
        foreach (var draw in shaderGraphSkinnedMeshDraws)
        {
            var entry = GetShaderGraphEntry(draw.ShaderGraphPath);
            if (entry is { NeedsSceneDepth: true })
                return true;
        }

        return false;
    }

    private void RenderShaderGraphDepthPrepass(List<SolidRange> ranges, int vao, ref Matrix4 mvp)
    {
        if (_shaderGraphDepthPrepassShader == 0 || ranges.Count == 0)
            return;

        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.ColorMask(false, false, false, false);

        try
        {
            GL.UseProgram(_shaderGraphDepthPrepassShader);
            if (_shaderGraphDepthPrepassViewProjLocation >= 0)
                GL.UniformMatrix4(_shaderGraphDepthPrepassViewProjLocation, true, ref mvp);

            GL.BindVertexArray(vao);
            foreach (var range in ranges)
            {
                if (range.ShaderGraphPath == null) continue;

                var entry = GetShaderGraphEntry(range.ShaderGraphPath);
                if (!ShouldRenderShaderGraphDepthPrepass(entry)) continue;

                TrackMainDraw(range.Count);
                GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
            }
        }
        finally
        {
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.ColorMask(true, true, true, true);
        }
    }

    private static bool ShouldRenderShaderGraphDepthPrepass(ShaderGraphMaterialEntry? entry)
    {
        var model = entry?.Model;
        if (entry == null || entry.Program == 0 || model == null)
            return false;

        if (!model.DepthWrite)
            return false;

        return !entry.NeedsSceneDepth;
    }

    private void RenderShaderGraphRanges(List<SolidRange> ranges, int vao, ref Matrix4 mvp, Vector3 cameraPosition, int viewportWidth, int viewportHeight, IReadOnlyList<GameObject> objects, float cameraNear, float cameraFar)
    {
        ResolveShaderGraphLighting(objects, out var lightDir, out var lightColor, out var lightIntensity);
        var identity = Matrix4.Identity;

        foreach (var range in ranges)
        {
            if (range.ShaderGraphPath == null) continue;

            var entry = GetShaderGraphEntry(range.ShaderGraphPath);
            if (entry == null || entry.Program == 0) continue;

            ApplyShaderGraphRenderState(entry.Model);
            GL.UseProgram(entry.Program);

            ApplyShaderGraphFrameUniforms(entry, ref mvp, cameraPosition, viewportWidth, viewportHeight, lightDir, lightColor, lightIntensity, cameraNear, cameraFar);
            SetMatrixUniform(entry.ModelLocation, ref identity);
            SetIntUniform(entry.UseModelLocation, 0);
            SetIntUniform(entry.UseSkinningLocation, 0);
            BindShaderGraphResources(entry, range.ShaderGraphTextures, range.ShaderGraphProperties);

            GL.BindVertexArray(vao);
            TrackMainDraw(range.Count);
            GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
        ResetShaderGraphRenderState();
    }

    private void RenderShaderGraphDynamicMeshDraws(ref Matrix4 mvp, Vector3 cameraPosition, int viewportWidth, int viewportHeight, IReadOnlyList<GameObject> objects, float cameraNear, float cameraFar)
    {
        ResolveShaderGraphLighting(objects, out var lightDir, out var lightColor, out var lightIntensity);
        int boundVao = 0;

        // La lista viene ORDENADA (SortShaderGraphMeshDrawsForInstancing): los draws idénticos
        // (misma malla+grafo+material) son consecutivos y se fusionan en un DrawArraysInstanced.
        for (int i = 0; i < shaderGraphDynamicMeshDraws.Count;)
        {
            var draw = shaderGraphDynamicMeshDraws[i];
            int end = i + 1;
            while (end < shaderGraphDynamicMeshDraws.Count && CanInstanceShaderGraphDraws(draw, shaderGraphDynamicMeshDraws[end]))
                end++;

            // "Shadows Only": sigue proyectando sombra (depth pass), pero acá en color no se dibuja.
            if (draw.ShadowsOnly)
            {
                i = end;
                continue;
            }

            var entry = GetShaderGraphEntry(draw.ShaderGraphPath);
            if (entry == null || entry.Program == 0)
            {
                i = end;
                continue;
            }

            ApplyShaderGraphRenderState(entry.Model);
            GL.UseProgram(entry.Program);

            ApplyShaderGraphFrameUniforms(entry, ref mvp, cameraPosition, viewportWidth, viewportHeight, lightDir, lightColor, lightIntensity, cameraNear, cameraFar, draw.ReceiveShadows);
            SetIntUniform(entry.UseSkinningLocation, 0);
            BindShaderGraphResources(entry, draw.ShaderGraphTextures, draw.ShaderGraphProperties);

            if (boundVao != draw.Mesh.Vao)
            {
                GL.BindVertexArray(draw.Mesh.Vao);
                boundVao = draw.Mesh.Vao;
            }

            int instanceCount = end - i;
            if (instanceCount > 1)
            {
                UploadShaderGraphInstanceMatrices(i, instanceCount);
                SetIntUniform(entry.UseInstancingLocation, 1);
                SetIntUniform(entry.UseModelLocation, 0);
                TrackMainDraw(draw.Count, instanceCount);
                GL.DrawArraysInstanced(PrimitiveType.Triangles, draw.Start, draw.Count, instanceCount);
                SetIntUniform(entry.UseInstancingLocation, 0);
            }
            else
            {
                var world = draw.World;
                SetMatrixUniform(entry.ModelLocation, ref world);
                SetIntUniform(entry.UseInstancingLocation, 0);
                SetIntUniform(entry.UseModelLocation, 1);
                TrackMainDraw(draw.Count);
                GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
            }

            i = end;
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
        ResetShaderGraphRenderState();
    }

    private void RenderShaderGraphSkinnedMeshDraws(ref Matrix4 mvp, Vector3 cameraPosition, int viewportWidth, int viewportHeight, IReadOnlyList<GameObject> objects, float cameraNear, float cameraFar)
    {
        ResolveShaderGraphLighting(objects, out var lightDir, out var lightColor, out var lightIntensity);
        int boundVao = 0;

        foreach (var draw in shaderGraphSkinnedMeshDraws)
        {
            if (draw.ShadowsOnly)
                continue;

            var entry = GetShaderGraphEntry(draw.ShaderGraphPath);
            if (entry == null || entry.Program == 0) continue;

            ApplyShaderGraphRenderState(entry.Model);
            GL.UseProgram(entry.Program);

            var world = draw.World;
            ApplyShaderGraphFrameUniforms(entry, ref mvp, cameraPosition, viewportWidth, viewportHeight, lightDir, lightColor, lightIntensity, cameraNear, cameraFar, draw.ReceiveShadows);
            SetMatrixUniform(entry.ModelLocation, ref world);
            SetIntUniform(entry.UseModelLocation, 1);
            SetIntUniform(entry.UseSkinningLocation, 1);
            UploadBoneMatrices(entry.BoneMatrixLocations, draw.Skin);
            BindShaderGraphResources(entry, draw.ShaderGraphTextures, draw.ShaderGraphProperties);

            if (boundVao != draw.Mesh.Vao)
            {
                GL.BindVertexArray(draw.Mesh.Vao);
                boundVao = draw.Mesh.Vao;
            }

            TrackMainDraw(draw.Count);
            GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
        ResetShaderGraphRenderState();
    }

    private void ResolveShaderGraphLighting(IReadOnlyList<GameObject> objects, out Vector3 lightDir, out Vector3 lightColor, out float lightIntensity)
    {
        var dirLight = FindPrimaryDirectionalLight(objects);
        if (dirLight != null && dirLight.gameObject != null)
        {
            lightDir = ToTk(dirLight.GetNormalizedDirection()).Normalized();
            lightColor = new Vector3(dirLight.R, dirLight.G, dirLight.B);
            lightIntensity = dirLight.Intensity;
            return;
        }

        lightDir = new Vector3(-0.4f, -0.8f, -0.5f).Normalized();
        lightColor = Vector3.Zero;
        lightIntensity = 0f;
    }

    private void ApplyShaderGraphFrameUniforms(
            ShaderGraphMaterialEntry entry,
            ref Matrix4 mvp,
            Vector3 cameraPosition,
            int viewportWidth,
            int viewportHeight,
            Vector3 lightDir,
            Vector3 lightColor,
            float lightIntensity,
            float cameraNear,
            float cameraFar,
            bool receiveShadows = true)
    {
        if (entry.ViewProjLocation >= 0)
            GL.UniformMatrix4(entry.ViewProjLocation, true, ref mvp);

        SetUniformIfPresent(entry.TimeLocation, (float)(Environment.TickCount64 / 1000.0));
        SetUniformIfPresent(entry.ResolutionLocation, new Vector2(viewportWidth, viewportHeight));
        SetUniformIfPresent(entry.CameraPosLocation, cameraPosition);
        SetUniformIfPresent(entry.LightDirLocation, lightDir);
        SetUniformIfPresent(entry.LightColorLocation, lightColor);
        SetUniformIfPresent(entry.LightIntensityLocation, lightIntensity);
        SetUniformIfPresent(entry.ColorSpaceLinearLocation, ColorSpace == ColorSpace.Linear ? 1 : 0);
        SetUniformIfPresent(entry.CameraNearLocation, Math.Max(0.001f, cameraNear));
        SetUniformIfPresent(entry.CameraFarLocation, Math.Max(cameraNear + 0.001f, cameraFar));
        ApplyShaderGraphShadowUniforms(entry, receiveShadows);
    }

    private void ApplyShaderGraphShadowUniforms(ShaderGraphMaterialEntry entry, bool receiveShadows = true)
    {
        var shadow = _cachedDirectionalShadow;
        bool shadowsEnabled = shadow.Enabled && receiveShadows;
        SetIntUniform(entry.ShadowEnabledLocation, shadowsEnabled ? 1 : 0);
        SetIntUniform(entry.CascadeCountLocation, shadow.CascadeCount);
        SetIntUniform(entry.ShadowPcfRadiusLocation, ShadowPcfRadiusFor(_shadowQuality));
        SetUniformIfPresent(entry.ShadowStrengthLocation, shadow.Strength);
        SetUniformIfPresent(entry.ShadowBiasScaleLocation, EffectiveShadowBiasScale());
        SetUniformIfPresent(entry.ShadowNormalBiasLocation, _frameDir != null ? _frameDir.ShadowNormalBias : 0f);
        if (entry.ShadowCameraPosLocation >= 0)
            GL.Uniform3(entry.ShadowCameraPosLocation, shadow.CameraPosition);

        for (int i = 0; i < 5; i++)
        {
            if (entry.CascadeLightMvpLocations[i] >= 0)
            {
                var m = shadow.Cascades[i];
                GL.UniformMatrix4(entry.CascadeLightMvpLocations[i], true, ref m);
            }
            if (entry.CascadeSplitLocations[i] >= 0)
                GL.Uniform1(entry.CascadeSplitLocations[i], shadow.Splits[i]);
        }

        if (entry.ShadowMapLocation >= 0)
        {
            GL.Uniform1(entry.ShadowMapLocation, 14); // unidad de textura alta para no chocar con los samplers del graph
            GL.ActiveTexture(TextureUnit.Texture14);
            GL.BindTexture(TextureTarget.Texture2DArray, shadowsEnabled ? _shadowArrayTex : 0);
            GL.ActiveTexture(TextureUnit.Texture0);
        }
    }

    private void BindShaderGraphResources(
            ShaderGraphMaterialEntry entry,
            IReadOnlyDictionary<string, string>? textures,
            IReadOnlyDictionary<string, float[]>? properties)
    {
        BindShaderGraphSamplers(entry, textures);
        BindShaderGraphPropertyDefaults(entry, properties);

        if (entry.SceneDepthSamplerIndex >= 0)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + entry.SceneDepthSamplerIndex);
            GL.BindTexture(TextureTarget.Texture2D, _depthTex);
            GL.ActiveTexture(TextureUnit.Texture0);
        }
    }

    private static void ApplyShaderGraphRenderState(ShaderGraphModel? model)
    {
        if (model == null)
        {
            ResetShaderGraphRenderState();
            return;
        }

        bool transparent = model.Surface.Equals("Transparent", StringComparison.OrdinalIgnoreCase);
        if (transparent)
        {
            GL.Enable(EnableCap.Blend);
            switch (model.BlendMode)
            {
                case "Additive":
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                    break;
                case "Multiply":
                    GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);
                    break;
                case "Premultiply":
                    GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                    break;
                default:
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    break;
            }
        }
        else
        {
            GL.Disable(EnableCap.Blend);
        }

        if (model.DepthTest)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(GetShaderGraphDepthFunction(model.ZTest));
        }
        else
        {
            GL.Disable(EnableCap.DepthTest);
        }

        GL.DepthMask(model.DepthWrite);

        if (model.DoubleSided || model.CullMode.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            GL.Disable(EnableCap.CullFace);
        }
        else
        {
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(model.CullMode.Equals("Front", StringComparison.OrdinalIgnoreCase)
                ? TriangleFace.Front
                : TriangleFace.Back);
        }
    }

    private static DepthFunction GetShaderGraphDepthFunction(string? zTest)
            => zTest switch
            {
                "Less" => DepthFunction.Less,
                "Equal" => DepthFunction.Equal,
                "Greater" => DepthFunction.Greater,
                "GEqual" => DepthFunction.Gequal,
                "Always" => DepthFunction.Always,
                _ => DepthFunction.Lequal
            };

    private static void ResetShaderGraphRenderState()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.ColorMask(true, true, true, true);
    }

    /// <summary>Fuerza la revalidación inmediata de un grafo (llamar al guardarlo desde el editor).</summary>
    internal void InvalidateShaderGraphEntry(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (_shaderGraphCache.TryGetValue(path, out var entry))
            entry.NextValidationTick = 0;
        SceneViewportRenderer.InvalidateNormalizedAssetPath(path);
    }

    internal ShaderGraphMaterialEntry? GetShaderGraphEntry(string path)
    {
        // Esto corre VARIAS veces por draw y por frame (prepass, NeedsSceneDepth, draw).
        // Sin la ventana, cada llamada era un GetLastWriteTimeUtc contra disco → con N mallas
        // shader-graph (hojas de árboles, etc.) el coste escalaba por objeto y hundía los FPS
        // (brutal en proyectos OneDrive). El stat solo detecta ediciones EXTERNAS del grafo;
        // guardar desde el editor de Shader Graph invalida por otro camino.
        long now = Environment.TickCount64;
        if (_shaderGraphCache.TryGetValue(path, out var cached))
        {
            if (now < cached.NextValidationTick)
                return cached;

            var cachedFileTime = System.IO.File.GetLastWriteTimeUtc(path);
            if (cached.FileTime == cachedFileTime)
            {
                cached.NextValidationTick = now + 2000;
                return cached;
            }
        }

        var fileTime = System.IO.File.GetLastWriteTimeUtc(path);

        ShaderGraphModel model;
        try
        {
            model = GraphSerializer.Load(path);
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[ShaderGraph] No se pudo cargar '{System.IO.Path.GetFileName(path)}': {ex.Message}");
            return null;
        }

        var generator = new ShaderCodeGenerator();
        string fragmentSrc;
        try
        {
            fragmentSrc = generator.GenerateFragmentShader(model);
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[ShaderGraph] No se pudo generar GLSL para '{System.IO.Path.GetFileName(path)}': {ex.Message}");
            return null;
        }

        var entry = new ShaderGraphMaterialEntry
        {
            FileTime = fileTime,
            NextValidationTick = now + 2000,
            Model = model,
            FragmentSource = fragmentSrc,
            NeedsSceneDepth = ShaderGraphModelNeedsSceneDepth(model),
            Samplers = Regex.Matches(fragmentSrc, @"uniform\s+sampler2D\s+(\w+)\s*;")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList(),
            PropertyDefaults = BuildShaderGraphPropertyDefaults(model),
            ExposedProperties = BuildShaderGraphExposedProperties(model),
            SamplerDefaults = BuildShaderGraphSamplerDefaults(model)
        };

        try
        {
            int vs = CompileShader(ShaderType.VertexShader, ShaderGraphVertexSource);
            int fs = CompileShader(ShaderType.FragmentShader, fragmentSrc);
            int program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkOk);
            GL.DetachShader(program, vs);
            GL.DetachShader(program, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);

            if (linkOk == 0)
            {
                LogAssetWarning($"[ShaderGraph] Link error en '{System.IO.Path.GetFileName(path)}': {GL.GetProgramInfoLog(program)}");
                GL.DeleteProgram(program);
                entry.Program = 0;
            }
            else
            {
                entry.Program = program;
                CacheShaderGraphUniformLocations(entry);
            }
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[ShaderGraph] Error compilando '{System.IO.Path.GetFileName(path)}': {ex.Message}");
            entry.Program = 0;
        }

        if (_shaderGraphCache.TryGetValue(path, out var old) && old.Program != 0)
            GL.DeleteProgram(old.Program);

        _shaderGraphCache[path] = entry;
        return entry;
    }

    private static void CacheShaderGraphUniformLocations(ShaderGraphMaterialEntry entry)
    {
        if (entry.Program == 0)
            return;

        entry.ViewProjLocation = GL.GetUniformLocation(entry.Program, "u_ViewProj");
        entry.TimeLocation = GL.GetUniformLocation(entry.Program, "u_Time");
        entry.ResolutionLocation = GL.GetUniformLocation(entry.Program, "u_Resolution");
        entry.CameraPosLocation = GL.GetUniformLocation(entry.Program, "u_CameraPos");
        entry.LightDirLocation = GL.GetUniformLocation(entry.Program, "u_LightDir");
        entry.LightColorLocation = GL.GetUniformLocation(entry.Program, "u_LightColor");
        entry.LightIntensityLocation = GL.GetUniformLocation(entry.Program, "u_LightIntensity");
        entry.ColorSpaceLinearLocation = GL.GetUniformLocation(entry.Program, "u_ColorSpaceLinear");
        entry.CameraNearLocation = GL.GetUniformLocation(entry.Program, "u_CameraNear");
        entry.CameraFarLocation = GL.GetUniformLocation(entry.Program, "u_CameraFar");
        entry.ShadowMapLocation = GL.GetUniformLocation(entry.Program, "u_ShadowMap");
        entry.ShadowEnabledLocation = GL.GetUniformLocation(entry.Program, "u_ShadowEnabled");
        entry.ShadowStrengthLocation = GL.GetUniformLocation(entry.Program, "u_ShadowStrength");
        entry.CascadeCountLocation = GL.GetUniformLocation(entry.Program, "u_CascadeCount");
        entry.ShadowPcfRadiusLocation = GL.GetUniformLocation(entry.Program, "u_ShadowPcfRadius");
        entry.ShadowBiasScaleLocation = GL.GetUniformLocation(entry.Program, "u_ShadowBiasScale");
        entry.ShadowNormalBiasLocation = GL.GetUniformLocation(entry.Program, "u_ShadowNormalBias");
        entry.ShadowCameraPosLocation = GL.GetUniformLocation(entry.Program, "u_ShadowCameraPos");
        for (int c = 0; c < 5; c++)
        {
            entry.CascadeLightMvpLocations[c] = GL.GetUniformLocation(entry.Program, $"u_CascadeLightMvp[{c}]");
            entry.CascadeSplitLocations[c] = GL.GetUniformLocation(entry.Program, $"u_CascadeSplit[{c}]");
        }
        entry.ModelLocation = GL.GetUniformLocation(entry.Program, "u_Model");
        entry.UseModelLocation = GL.GetUniformLocation(entry.Program, "u_UseModel");
        entry.UseInstancingLocation = GL.GetUniformLocation(entry.Program, "u_UseInstancing");
        entry.UseSkinningLocation = GL.GetUniformLocation(entry.Program, "u_UseSkinning");
        for (int i = 0; i < MaxGpuBones; i++)
            entry.BoneMatrixLocations[i] = GL.GetUniformLocation(entry.Program, $"u_Bones[{i}]");

        entry.SceneDepthSamplerIndex = entry.Samplers.FindIndex(s => s.Equals("u_SceneDepth", StringComparison.OrdinalIgnoreCase));
        entry.SamplerBindings.Clear();
        for (int i = 0; i < entry.Samplers.Count; i++)
        {
            int loc = GL.GetUniformLocation(entry.Program, entry.Samplers[i]);
            if (loc >= 0)
                entry.SamplerBindings.Add(new ShaderGraphSamplerBinding(entry.Samplers[i], loc, i));
        }

        entry.PropertyBindings.Clear();
        foreach (Match m in Regex.Matches(entry.FragmentSource, @"uniform\s+(float|vec2|vec3|vec4)\s+(\w+)\s*;"))
        {
            var type = m.Groups[1].Value;
            var name = m.Groups[2].Value;
            if (IsShaderGraphEngineUniform(name))
                continue;

            int loc = GL.GetUniformLocation(entry.Program, name);
            if (loc >= 0)
                entry.PropertyBindings.Add(new ShaderGraphPropertyBinding(name, type, loc));
        }
    }

    private static bool IsShaderGraphEngineUniform(string name)
            => name is "u_Time" or "u_Resolution" or "u_CameraPos" or "u_LightDir" or "u_LightColor"
                or "u_LightIntensity" or "u_CameraNear" or "u_CameraFar" or "u_ColorSpaceLinear"
                or "u_ShadowStrength" or "u_ShadowBiasScale" or "u_ShadowNormalBias" or "u_ShadowCameraPos";

    private void BindShaderGraphSamplers(ShaderGraphMaterialEntry entry, IReadOnlyDictionary<string, string>? overrides)
    {
        if (_shaderGraphWhiteTex == 0)
            _shaderGraphWhiteTex = CreateShaderGraphWhiteTexture();

        foreach (var binding in entry.SamplerBindings)
        {
            if (binding.Name.Equals("u_SceneDepth", StringComparison.OrdinalIgnoreCase))
            {
                GL.Uniform1(binding.Location, binding.TextureUnitIndex);
                continue;
            }

            string? path = overrides != null && overrides.TryGetValue(binding.Name, out var ov) && !string.IsNullOrWhiteSpace(ov) ? ov
                : entry.SamplerDefaults.TryGetValue(binding.Name, out var def) && !string.IsNullOrWhiteSpace(def) ? def
                : null;

            int tex = path != null ? GetTexture(path) : 0;
            if (tex == 0) tex = _shaderGraphWhiteTex;

            GL.ActiveTexture(TextureUnit.Texture0 + binding.TextureUnitIndex);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.Uniform1(binding.Location, binding.TextureUnitIndex);
        }
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private void BindShaderGraphPropertyDefaults(ShaderGraphMaterialEntry entry, IReadOnlyDictionary<string, float[]>? overrides)
    {
        foreach (var binding in entry.PropertyBindings)
        {
            var values = overrides != null && overrides.TryGetValue(binding.Name, out var ov) ? ov
                : entry.PropertyDefaults.TryGetValue(binding.Name, out var v) ? v : ShaderGraphDefaultFor(binding.Type);
            switch (binding.Type)
            {
                case "float": GL.Uniform1(binding.Location, values[0]); break;
                case "vec2": GL.Uniform2(binding.Location, values[0], values[1]); break;
                case "vec3":
                    // vec3 HDR: [r,g,b,intensity] (4 elementos) -> rgb * intensidad.
                    if (values.Length >= 4)
                        GL.Uniform3(binding.Location, values[0] * values[3], values[1] * values[3], values[2] * values[3]);
                    else
                        GL.Uniform3(binding.Location, values[0], values[1], values[2]);
                    break;
                case "vec4":
                    // vec4 HDR: [r,g,b,a,intensity] (5 elementos) -> rgb * intensidad, alpha sin tocar.
                    if (values.Length >= 5)
                        GL.Uniform4(binding.Location, values[0] * values[4], values[1] * values[4], values[2] * values[4], values[3]);
                    else
                        GL.Uniform4(binding.Location, values[0], values[1], values[2], values[3]);
                    break;
            }
        }
    }

    private static float[] ShaderGraphDefaultFor(string type) => type switch
    {
        "float" => new[] { 0.5f },
        "vec2" => new[] { 0f, 0f },
        "vec3" => new[] { 1f, 1f, 1f },
        "vec4" => new[] { 1f, 1f, 1f, 1f },
        _ => new[] { 0f }
    };

    private static Dictionary<string, float[]> BuildShaderGraphPropertyDefaults(ShaderGraphModel model)
    {
        var dict = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in model.Properties ?? new List<GraphProperty>())
        {
            var name = ShaderGraphSanitizeIdentifier(p.UniformName);
            switch (p.Type)
            {
                case PinType.Float:
                    dict[name] = new[] { ShaderGraphParseFloat(p.DefaultValue, 0f) };
                    break;
                case PinType.Vec2:
                    dict[name] = ShaderGraphParseVec(p.DefaultValue, 2);
                    break;
                case PinType.Vec3:
                    {
                        var c3 = ShaderGraphColorToVec3(p.ColorHex, 1f);
                        dict[name] = p.ColorMode == PropertyColorMode.Hdr
                            ? new[] { c3[0], c3[1], c3[2], p.ColorIntensity }
                            : c3;
                        break;
                    }
                case PinType.Vec4:
                    {
                        var c4 = ShaderGraphColorToVec3(p.ColorHex, 1f);
                        var a4 = ShaderGraphColorAlpha(p.ColorHex);
                        dict[name] = p.ColorMode == PropertyColorMode.Hdr
                            ? new[] { c4[0], c4[1], c4[2], a4, p.ColorIntensity }
                            : new[] { c4[0], c4[1], c4[2], a4 };
                        break;
                    }
            }
        }

        foreach (var n in model.Nodes)
        {
            // Si el nodo Property* referencia una propiedad del Blackboard, esta ya
            // dejó su valor por defecto en el dict y tiene prioridad: no sobrescribir
            // con el valor (potencialmente desactualizado) guardado en el nodo.
            var uniformName = ShaderGraphSanitizeIdentifier(ShaderGraphPropertyUniformName(n));
            if (dict.ContainsKey(uniformName)) continue;

            switch (n.Kind)
            {
                case NodeKind.PropertyFloat:
                    dict[uniformName] = new[] { n.FloatValue };
                    break;
                case NodeKind.PropertyColor:
                    dict[uniformName] = ShaderGraphColorToVec3(n.ColorHex, n.ColorIntensity);
                    break;
                case NodeKind.PropertyVector2:
                    dict[uniformName] = ShaderGraphParseVec(n.TextValue, 2);
                    break;
                case NodeKind.PropertyVector3:
                    dict[uniformName] = ShaderGraphParseVec(n.TextValue, 3);
                    break;
                case NodeKind.PropertyVector4:
                    dict[uniformName] = ShaderGraphParseVec(n.TextValue, 4);
                    break;
            }
        }

        return dict;
    }

    private static Dictionary<string, ShaderGraphExposedProperty> BuildShaderGraphExposedProperties(ShaderGraphModel model)
    {
        var dict = new Dictionary<string, ShaderGraphExposedProperty>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in model.Properties ?? new List<GraphProperty>())
        {
            if (!p.Exposed) continue;
            bool isHdr = (p.Type is PinType.Vec3 or PinType.Vec4) && p.ColorMode == PropertyColorMode.Hdr;
            dict[ShaderGraphSanitizeIdentifier(p.UniformName)] = new ShaderGraphExposedProperty(p.DisplayName, p.Type, isHdr);
        }
        return dict;
    }

    private static Dictionary<string, string> BuildShaderGraphSamplerDefaults(ShaderGraphModel model)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in model.Properties ?? new List<GraphProperty>())
        {
            if (p.Type != PinType.Texture2D) continue;
            dict[ShaderGraphSanitizeIdentifier(p.UniformName)] = p.TexturePath;
        }

        foreach (var n in model.Nodes)
        {
            if (n.Kind != NodeKind.PropertyTexture2D) continue;
            var uniformName = ShaderGraphSanitizeIdentifier(ShaderGraphPropertyUniformName(n));
            if (!dict.ContainsKey(uniformName))
                dict[uniformName] = n.TexturePath;
        }

        foreach (var n in model.Nodes)
        {
            if (n.Kind != NodeKind.TextureSample && n.Kind != NodeKind.NormalMap && n.Kind != NodeKind.Triplanar &&
                n.Kind != NodeKind.MetallicMap && n.Kind != NodeKind.SmoothnessMap && n.Kind != NodeKind.AmbientOcclusionMap) continue;
            var uniformName = ShaderGraphSanitizeIdentifier(ShaderGraphTextureUniformName(n));
            if (!dict.ContainsKey(uniformName))
                dict[uniformName] = n.TexturePath;
        }

        return dict;
    }

    private static string ShaderGraphPropertyUniformName(ShaderNode node)
    {
        var value = string.IsNullOrWhiteSpace(node.TextValue) ? node.Title : node.TextValue.Trim();
        return value.StartsWith("u_", StringComparison.OrdinalIgnoreCase) ? value : "u_" + value;
    }

    private static string ShaderGraphTextureUniformName(ShaderNode node)
    {
        var value = string.IsNullOrWhiteSpace(node.TextValue) ? string.Empty : node.TextValue.Trim();
        if (!string.IsNullOrWhiteSpace(node.TexturePath) && IsDefaultShaderGraphTextureUniformName(value))
            return LocalShaderGraphTextureUniformName(node);

        if (string.IsNullOrWhiteSpace(value))
            value = "u_MainTex";

        var fileName = System.IO.Path.GetFileNameWithoutExtension(value);
        if (!string.IsNullOrWhiteSpace(fileName) && (value.Contains('\\') || value.Contains('/') || value.Contains('.')))
            return "u_" + fileName;
        return value;
    }

    private static bool IsDefaultShaderGraphTextureUniformName(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v))
            return true;

        return v.Equals("MainTex", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_MainTex", StringComparison.OrdinalIgnoreCase)
            || v.Equals("NormalMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_NormalMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("MetallicMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_MetallicMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("SmoothnessMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_SmoothnessMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("AOMap", StringComparison.OrdinalIgnoreCase)
            || v.Equals("u_AOMap", StringComparison.OrdinalIgnoreCase);
    }

    private static string LocalShaderGraphTextureUniformName(ShaderNode node)
    {
        var prefix = node.Kind switch
        {
            NodeKind.NormalMap => "u_NormalMap_",
            NodeKind.MetallicMap => "u_MetallicMap_",
            NodeKind.SmoothnessMap => "u_SmoothnessMap_",
            NodeKind.AmbientOcclusionMap => "u_AOMap_",
            NodeKind.Triplanar => "u_Triplanar_",
            _ => "u_Texture_"
        };

        return prefix + node.Id.ToString("N")[..8];
    }

    private static string ShaderGraphSanitizeIdentifier(string value)
    {
        var chars = value.Select((c, i) => (char.IsLetter(c) || c == '_' || (i > 0 && char.IsDigit(c))) ? c : '_').ToArray();
        var result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? "u_MainTex" : result;
    }

    private static float[] ShaderGraphParseVec(string? s, int n)
    {
        var parts = (s ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new float[n];
        for (int i = 0; i < n; i++)
            result[i] = i < parts.Length ? ShaderGraphParseFloat(parts[i], 0f) : 0f;
        return result;
    }

    private static float ShaderGraphColorAlpha(string colorHex)
    {
        try
        {
            if (MediaColorConverter.ConvertFromString(colorHex) is MediaColor c)
                return c.A / 255f;
        }
        catch
        {
            // fall through
        }
        return 1f;
    }

    private static float[] ShaderGraphColorToVec3(string colorHex, float intensity)
    {
        try
        {
            if (MediaColorConverter.ConvertFromString(colorHex) is MediaColor c)
                return new[] { c.R / 255f * intensity, c.G / 255f * intensity, c.B / 255f * intensity };
        }
        catch
        {
            // fall through
        }
        return new[] { 1f, 0f, 1f };
    }

    private static int CreateShaderGraphWhiteTexture()
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        var pixel = new byte[] { 255, 255, 255, 255 };
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        return tex;
    }

    private static void SetUniformIfPresent(int program, string name, float value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private static void SetUniformIfPresent(int loc, float value)
    {
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private static void SetUniformIfPresent(int program, string name, int value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private static void SetUniformIfPresent(int loc, int value)
    {
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private static void SetUniformIfPresent(int program, string name, Vector2 value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform2(loc, value);
    }

    private static void SetUniformIfPresent(int loc, Vector2 value)
    {
        if (loc >= 0) GL.Uniform2(loc, value);
    }

    private static void SetUniformIfPresent(int program, string name, Vector3 value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform3(loc, value);
    }

    private static void SetUniformIfPresent(int loc, Vector3 value)
    {
        if (loc >= 0) GL.Uniform3(loc, value);
    }

    private string? GetObjectShaderGraphPath(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return string.IsNullOrWhiteSpace(over.ShaderGraphPath) ? null : over.ShaderGraphPath;

        var material = obj.GetComponent<Material>();
        return string.IsNullOrWhiteSpace(material?.ShaderGraphPath) ? null : material.ShaderGraphPath;
    }

    private IReadOnlyDictionary<string, float[]>? GetObjectShaderGraphProperties(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return over.ShaderGraphProperties.Count == 0 ? null : over.ShaderGraphProperties;

        var material = obj.GetComponent<Material>();
        return material == null || material.ShaderGraphProperties.Count == 0 ? null : material.ShaderGraphProperties;
    }

    private IReadOnlyDictionary<string, string>? GetObjectShaderGraphTextures(GameObject obj)
    {
        if (GetPreviewOverrideData(obj, -1) is { } over)
            return over.ShaderGraphTextures.Count == 0 ? null : over.ShaderGraphTextures;

        var material = obj.GetComponent<Material>();
        return material == null || material.ShaderGraphTextures.Count == 0 ? null : material.ShaderGraphTextures;
    }

    private static int CreateShaderGraphDepthPrepassShader()
    {
        const string fragmentSource = """
            #version 330 core
            void main()
            {
            }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, ShaderGraphVertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }

    private readonly record struct ShaderGraphDynamicMeshDraw(CachedGpuMesh Mesh, Matrix4 World, int Start, int Count, string ShaderGraphPath, IReadOnlyDictionary<string, float[]>? ShaderGraphProperties, IReadOnlyDictionary<string, string>? ShaderGraphTextures, bool CastShadows = true, bool ReceiveShadows = true, bool TwoSidedShadows = false, bool ShadowsOnly = false);

    private readonly record struct ShaderGraphSkinnedMeshDraw(CachedSkinnedGpuMesh Mesh, Matrix4 World, System.Numerics.Matrix4x4[] Skin, int Start, int Count, string ShaderGraphPath, IReadOnlyDictionary<string, float[]>? ShaderGraphProperties, IReadOnlyDictionary<string, string>? ShaderGraphTextures, bool CastShadows = true, bool ReceiveShadows = true, bool TwoSidedShadows = false, bool ShadowsOnly = false);
}
