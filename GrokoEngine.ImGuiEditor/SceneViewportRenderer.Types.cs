using GrokoEngine;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GrokoEngine.ImGuiEditor;

/// <summary>
/// Espacio de color en el que trabaja el pipeline de render, equivalente a
/// "Color Space" en Unity (Project Settings -> Player -> Other Settings).
/// </summary>
public enum ColorSpace
{
    Linear,
    Gamma
}

/// <summary>
/// Nivel de calidad de las sombras dinámicas, equivalente a "Shadow Resolution"
/// en Unity (Project Settings -> Quality).
/// </summary>
public enum ShadowQuality
{
    Low,
    Medium,
    High,
    Ultra
}

internal readonly record struct SceneRenderStats(
    int DrawCalls,
    int ShadowDrawCalls,
    int InstancedDrawCalls,
    int Instances,
    long Triangles,
    int StaticRanges,
    int SolidRanges,
    int DynamicRanges,
    int LineDrawCalls,
    int ParticleDrawCalls,
    int TextureCacheCount,
    int GpuMeshCacheCount,
    int ParsedMeshCacheCount,
    int ShaderGraphCacheCount,
    int RenderWidth,
    int RenderHeight,
    float BuildSceneMs,
    float ShadowMs,
    float SkyboxMs,
    float StaticOpaqueMs,
    float DynamicOpaqueMs,
    float ShaderGraphMs,
    float TerrainMs,
    float LinesGizmosMs,
    float ParticlesMs,
    float OcclusionMs,
    float DirectionalShadowMs,
    float SpotShadowMs,
    float PointShadowMs,
    float RenderOtherMs)
{
    public static readonly SceneRenderStats Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

internal sealed partial class SceneViewportRenderer
{
    private readonly record struct LineVertex(Vector3 Position, Vector4 Color);

    private readonly record struct SolidVertex
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector4 Color;
        public readonly Vector2 Uv;
        public readonly Vector4 Material;
        public readonly Vector4 Emission;

        public SolidVertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv)
            : this(position, normal, color, uv, new Vector4(0f, 0.5f, 1f, 1f), Vector4.Zero)
        {
        }

        public SolidVertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv, Vector4 material, Vector4 emission)
        {
            Position = position;
            Normal = normal;
            Color = color;
            Uv = uv;
            Material = material;
            Emission = emission;
        }
    }

    private readonly record struct SolidRange(
        int Start,
        int Count,
        string? TexturePath,
        string? NormalMapPath,
        string? RoughnessMapPath,
        string? MetallicMapPath,
        string? ShaderGraphPath = null,
        IReadOnlyDictionary<string, float[]>? ShaderGraphProperties = null,
        IReadOnlyDictionary<string, string>? ShaderGraphTextures = null,
        GameObject? TerrainObject = null);

    private readonly record struct DynamicMeshDraw(
        CachedGpuMesh Mesh,
        Matrix4 World,
        int Start,
        int Count,
        string? TexturePath,
        string? NormalMapPath,
        string? RoughnessMapPath,
        string? MetallicMapPath,
        Vector4 Color,
        Vector4 Material,
        Vector4 Emission,
        float Alpha = 1f,
        float RefractionStrength = 0f,
        float DistortionStrength = 0f,
        bool CastShadows = true,
        bool ReceiveShadows = true);

    private readonly record struct SkinnedMeshDraw(
        CachedSkinnedGpuMesh Mesh,
        Matrix4 World,
        System.Numerics.Matrix4x4[] Skin,
        int Start,
        int Count,
        string? TexturePath,
        string? NormalMapPath,
        string? RoughnessMapPath,
        string? MetallicMapPath,
        Vector4 Color,
        Vector4 Material,
        Vector4 Emission,
        float Alpha = 1f,
        float RefractionStrength = 0f,
        float DistortionStrength = 0f);

    private readonly record struct SurfaceMaps(string? NormalMapPath, string? RoughnessMapPath, string? MetallicMapPath);

    private readonly record struct SurfaceTransparency(
        MaterialSurfaceType SurfaceType,
        float Alpha,
        float RefractionStrength,
        float DistortionStrength)
    {
        public bool IsTransparent =>
            SurfaceType == MaterialSurfaceType.Transparent ||
            Alpha < 0.999f ||
            RefractionStrength > 0.0001f ||
            DistortionStrength > 0.0001f;

        public static readonly SurfaceTransparency Opaque = new(MaterialSurfaceType.Opaque, 1f, 0f, 0f);
    }

    private readonly record struct MeshVertData(Vector3 Position, Vector3 Normal, Vector2 Uv);

    private readonly struct SkinnedVertex
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector4 Color;
        public readonly Vector2 Uv;
        public readonly Vector4 Material;
        public readonly Vector4 Emission;
        public readonly Vector4 BoneIndices;
        public readonly Vector4 BoneWeights;

        public SkinnedVertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv, Vector4 material, Vector4 emission, Vector4 boneIndices, Vector4 boneWeights)
        {
            Position = position;
            Normal = normal;
            Color = color;
            Uv = uv;
            Material = material;
            Emission = emission;
            BoneIndices = boneIndices;
            BoneWeights = boneWeights;
        }
    }

    private sealed class CachedSkinnedRange
    {
        public CachedSkinnedRange(SolidVertex[] vertices) => Vertices = vertices;

        public SolidVertex[] Vertices { get; }
    }

    private readonly record struct CachedParsedMesh(ParsedMesh Mesh, DateTime FileTime);

    private sealed class CachedGpuMesh
    {
        public CachedGpuMesh(int vao, int vbo, int count, DateTime fileTime)
        {
            Vao = vao;
            Vbo = vbo;
            Count = count;
            FileTime = fileTime;
        }

        public int Vao { get; }
        public int Vbo { get; }
        public int Count { get; }
        public DateTime FileTime { get; }
    }

    private sealed class CachedSkinnedGpuMesh
    {
        public CachedSkinnedGpuMesh(int vao, int vbo, int count, DateTime fileTime)
        {
            Vao = vao;
            Vbo = vbo;
            Count = count;
            FileTime = fileTime;
        }

        public int Vao { get; }
        public int Vbo { get; }
        public int Count { get; }
        public DateTime FileTime { get; }
    }

    private readonly struct CachedMeshVerts(MeshVertData[] verts, int count, DateTime fileTime)
    {
        public readonly MeshVertData[] Verts = verts;
        public readonly int Count = count;
        public readonly DateTime FileTime = fileTime;
    }

    /// <summary>Frustum de 6 planos extraídos de la matriz VP (Gribb-Hartmann).</summary>
    private readonly struct Frustum
    {
        private readonly Vector4 _left;
        private readonly Vector4 _right;
        private readonly Vector4 _bottom;
        private readonly Vector4 _top;
        private readonly Vector4 _near;
        private readonly Vector4 _far;

        public Frustum(Matrix4 vp)
        {
            var r0 = vp.Row0;
            var r1 = vp.Row1;
            var r2 = vp.Row2;
            var r3 = vp.Row3;

            _left = Norm(r3 + r0);
            _right = Norm(r3 - r0);
            _bottom = Norm(r3 + r1);
            _top = Norm(r3 - r1);
            _near = Norm(r3 + r2);
            _far = Norm(r3 - r2);
        }

        /// <summary>True si la esfera es potencialmente visible: no está completamente fuera de ningún plano.</summary>
        public bool ContainsSphere(Vector3 c, float r)
        {
            var p = new Vector4(c.X, c.Y, c.Z, 1f);
            return Vector4.Dot(_left, p) >= -r &&
                   Vector4.Dot(_right, p) >= -r &&
                   Vector4.Dot(_bottom, p) >= -r &&
                   Vector4.Dot(_top, p) >= -r &&
                   Vector4.Dot(_near, p) >= -r &&
                   Vector4.Dot(_far, p) >= -r;
        }

        private static Vector4 Norm(Vector4 p)
        {
            float len = MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            return len > 1e-6f ? p / len : p;
        }
    }
}
