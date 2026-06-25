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
private void BuildTerrainGeometry(GameObject obj, Terrain terrain, ParsedMesh mesh, string meshPath, bool selected, Matrix4 transform)
    {
        Vector4 fallbackColor = new Vector4(0.62f, 0.72f, 1f, 1f);
        Vector4 color = GetObjectColor(obj, fallbackColor);
        Vector4 lineColor = selected ? new Vector4(1f, 0.78f, 0.12f, 1f) : color;

        var min = mesh.BoundsMin;
        var max = mesh.BoundsMax;

        int start = ActiveSolidVertices.Count;
        AddMeshTriangles(mesh, transform, 1f, color, meshPath, new Vector4(0f, 0.9f, 0f, 0f), Vector4.Zero, 0, mesh.Positions.Length / 3);
        int count = ActiveSolidVertices.Count - start;
        if (count > 0)
            ActiveSolidRanges.Add(new SolidRange(start, count, null, null, null, null, null, null, null, obj));

        Span<Vector3> corners = stackalloc Vector3[]
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
        };

        for (int i = 0; i < corners.Length; i++)
            corners[i] = Vector3.TransformPosition(corners[i], transform);

        if (ShowObjectWireframes || selected)
        {
            AddLine(corners[0], corners[1], lineColor); AddLine(corners[1], corners[2], lineColor);
            AddLine(corners[2], corners[3], lineColor); AddLine(corners[3], corners[0], lineColor);
            AddLine(corners[4], corners[5], lineColor); AddLine(corners[5], corners[6], lineColor);
            AddLine(corners[6], corners[7], lineColor); AddLine(corners[7], corners[4], lineColor);
            AddLine(corners[0], corners[4], lineColor); AddLine(corners[1], corners[5], lineColor);
            AddLine(corners[2], corners[6], lineColor); AddLine(corners[3], corners[7], lineColor);
        }
    }

private (ParsedMesh Mesh, string MeshPath) GetTerrainMesh(GameObject obj, Terrain terrain)
    {
        if (terrainMeshCache.TryGetValue(obj.EditorId, out var cached) && cached.Version == terrain.Version)
            return (cached.Mesh, $"terrain://{obj.EditorId}/v{cached.Version}");

        var mesh = TerrainMeshGenerator.Generate(terrain);
        terrainMeshCache[obj.EditorId] = (mesh, terrain.Version);
        return (mesh, $"terrain://{obj.EditorId}/v{terrain.Version}");
    }

private void ApplyTerrainLighting(IReadOnlyList<GameObject> objects)
    {
        var ambient = FindComponent<AmbientLight>(objects);
        var ambColor = ambient != null
            ? new Vector3(ambient.R, ambient.G, ambient.B)
            : new Vector3(0.3f, 0.32f, 0.36f);
        float ambIntensity = ambient?.Intensity ?? 0.22f;
        GL.Uniform3(terrainAmbientColorLocation, ambColor);
        GL.Uniform1(terrainAmbientIntensityLocation, ambIntensity);
        GL.Uniform1(terrainSkyStrengthLocation, ambient?.SkyStrength ?? 0.08f);

        var dir = FindPrimaryDirectionalLight(objects);
        if (dir != null && dir.gameObject != null)
        {
            GL.Uniform3(terrainDirDirLocation, ToTk(dir.GetNormalizedDirection()).Normalized());
            GL.Uniform3(terrainDirColorLocation, new Vector3(dir.R, dir.G, dir.B));
            GL.Uniform1(terrainDirIntensityLocation, dir.Intensity);
        }
        else
        {
            GL.Uniform3(terrainDirDirLocation, new Vector3(0f, -1f, 0f));
            GL.Uniform3(terrainDirColorLocation, Vector3.Zero);
            GL.Uniform1(terrainDirIntensityLocation, 0f);
        }
    }

private int GetOrUpdateSplatTexture(GameObject obj, Terrain terrain)
    {
        if (terrainSplatTextureCache.TryGetValue(obj.EditorId, out var cached) && cached.Version == terrain.SplatVersion)
            return cached.TextureId;

        terrain.EnsureSplatMapSize();

        int textureId = cached.TextureId;
        if (textureId == 0)
            textureId = GL.GenTexture();

        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            terrain.Resolution,
            terrain.Resolution,
            0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
            PixelType.UnsignedByte,
            terrain.SplatMap);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        terrainSplatTextureCache[obj.EditorId] = (textureId, terrain.SplatVersion);
        return textureId;
    }

private void RenderTerrainRanges(List<SolidRange> ranges, int vao, ref Matrix4 mvp, IReadOnlyList<GameObject> objects)
    {
        if (terrainShader == 0)
            return;

        bool used = false;
        foreach (var range in ranges)
        {
            if (range.TerrainObject is not { } obj) continue;
            var terrain = obj.GetComponent<Terrain>();
            if (terrain == null) continue;

            if (!used)
            {
                GL.UseProgram(terrainShader);
                GL.UniformMatrix4(terrainMvpLocation, true, ref mvp);
                var identity = Matrix4.Identity;
                GL.UniformMatrix4(terrainModelLocation, true, ref identity);
                ApplyTerrainLighting(objects);
                GL.BindVertexArray(vao);
                used = true;
            }

            int splatTexture = GetOrUpdateSplatTexture(obj, terrain);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, splatTexture);
            GL.Uniform1(terrainSplatMapLocation, 0);

            for (int i = 0; i < 4; i++)
            {
                string path = terrain.LayerTextures[i];
                int texUnit = i + 1;
                GL.ActiveTexture(TextureUnit.Texture0 + texUnit);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    int texId = GetTexture(path);
                    GL.BindTexture(TextureTarget.Texture2D, texId);
                    GL.Uniform1(terrainLayerLocations[i], texUnit);
                    GL.Uniform1(terrainHasLayerLocations[i], texId != 0 ? 1 : 0);
                }
                else
                {
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    GL.Uniform1(terrainLayerLocations[i], texUnit);
                    GL.Uniform1(terrainHasLayerLocations[i], 0);
                }
                GL.Uniform1(terrainTilingLocations[i], terrain.LayerTiling[i]);
            }

            TrackMainDraw(range.Count);
            GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
        }

        if (used)
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
    }

private static int CreateTerrainShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aPosition;
            layout (location = 1) in vec3 aNormal;
            layout (location = 3) in vec2 aUv;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec2 vUv;
            void main()
            {
                vec4 world = vec4(aPosition, 1.0) * uModel;
                vec3 normalWs = (vec4(aNormal, 0.0) * uModel).xyz;
                vWorldPos = world.xyz;
                vNormal   = length(normalWs) > 0.0001 ? normalize(normalWs) : vec3(0.0, 1.0, 0.0);
                vUv = aUv;
                gl_Position = world * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec3 vWorldPos;
            in vec3 vNormal;
            in vec2 vUv;

            uniform sampler2D uSplatMap;
            uniform sampler2D uLayer0;
            uniform sampler2D uLayer1;
            uniform sampler2D uLayer2;
            uniform sampler2D uLayer3;
            uniform int uHasLayer0;
            uniform int uHasLayer1;
            uniform int uHasLayer2;
            uniform int uHasLayer3;
            uniform float uTiling0;
            uniform float uTiling1;
            uniform float uTiling2;
            uniform float uTiling3;

            uniform vec3  uAmbientColor;
            uniform float uAmbientIntensity;
            uniform float uSkyStrength;
            uniform vec3  uDirDir;
            uniform vec3  uDirColor;
            uniform float uDirIntensity;

            out vec4 outColor;

            vec3 LayerColor(sampler2D layerTex, int hasLayer, float tiling)
            {
                return hasLayer != 0 ? texture(layerTex, vUv * tiling).rgb : vec3(0.5);
            }

            void main()
            {
                vec3 N = normalize(vNormal);
                vec4 splat = texture(uSplatMap, vUv);

                vec3 albedo = LayerColor(uLayer0, uHasLayer0, uTiling0) * splat.r
                            + LayerColor(uLayer1, uHasLayer1, uTiling1) * splat.g
                            + LayerColor(uLayer2, uHasLayer2, uTiling2) * splat.b
                            + LayerColor(uLayer3, uHasLayer3, uTiling3) * splat.a;

                const float SKY_DIFFUSE_FILL = 0.48;
                vec3 sky = uAmbientColor * (uAmbientIntensity + max(N.y, 0.0) * uSkyStrength * SKY_DIFFUSE_FILL);
                vec3 ground = uAmbientColor * uAmbientIntensity * 0.35 * max(-N.y, 0.0);
                vec3 ambientLight = sky + ground;

                vec3 sunL = normalize(-uDirDir);
                vec3 diffuse = uDirColor * uDirIntensity * max(dot(N, sunL), 0.0);

                vec3 hdr = albedo * (ambientLight + diffuse);
                outColor = vec4(max(hdr, vec3(0.0)), 1.0);
            }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
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
}
