using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private CachedGpuMesh? GetGpuMesh(ParsedMesh mesh, string meshPath, float importScale)
    {
        var fileTime = System.IO.File.Exists(meshPath) ? System.IO.File.GetLastWriteTimeUtc(meshPath) : DateTime.MinValue;
        string cacheKey = meshPath + "|" + importScale.ToString("F6");

        if (gpuMeshCache.TryGetValue(cacheKey, out var cached) && cached.FileTime == fileTime)
            return cached;

        if (cached != null)
        {
            DeleteGpuMesh(cached);
            gpuMeshCache.Remove(cacheKey);
        }

        if (gpuMeshCache.Count > 128 && dynamicMeshDraws.Count == 0 && shaderGraphDynamicMeshDraws.Count == 0)
            ClearGpuMeshCache();

        int sourceCount = mesh.TriangleCount * 3;
        if (sourceCount <= 0)
            return null;

        var verts = new SolidVertex[sourceCount];
        int actual = 0;
        for (int v = 0; v < sourceCount; v++)
        {
            int pi = v * 3;
            if (mesh.Positions.Length <= pi + 2)
                break;

            var pos = new Vector3(
                mesh.Positions[pi] * importScale,
                mesh.Positions[pi + 1] * importScale,
                mesh.Positions[pi + 2] * importScale);
            var norm = mesh.Normals.Length > pi + 2
                ? new Vector3(mesh.Normals[pi], mesh.Normals[pi + 1], mesh.Normals[pi + 2]).Normalized()
                : Vector3.UnitY;
            int ui = v * 2;
            var uv = mesh.UVs.Length > ui + 1
                ? new Vector2(mesh.UVs[ui], mesh.UVs[ui + 1])
                : new Vector2(0.5f, 0.5f);

            verts[actual++] = new SolidVertex(pos, norm, Vector4.One, uv, new Vector4(0f, 0.5f, 0f, 0f), Vector4.Zero);
        }

        if (actual <= 0)
            return null;

        if (actual != verts.Length)
            Array.Resize(ref verts, actual);

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, actual * Unsafe.SizeOf<SolidVertex>(), verts, BufferUsageHint.StaticDraw);
        ConfigureGpuMeshVertexAttributes(Unsafe.SizeOf<SolidVertex>());
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        cached = new CachedGpuMesh(vao, vbo, actual, fileTime);
        gpuMeshCache[cacheKey] = cached;
        return cached;
    }

    private CachedSkinnedGpuMesh? GetSkinnedGpuMesh(ParsedMesh mesh, string meshPath, float importScale)
    {
        var fileTime = System.IO.File.Exists(meshPath) ? System.IO.File.GetLastWriteTimeUtc(meshPath) : DateTime.MinValue;
        string cacheKey = meshPath + "|skinned|" + importScale.ToString("F6");

        if (skinnedGpuMeshCache.TryGetValue(cacheKey, out var cached) && cached.FileTime == fileTime)
            return cached;

        if (cached != null)
        {
            DeleteSkinnedGpuMesh(cached);
            skinnedGpuMeshCache.Remove(cacheKey);
        }

        if (skinnedGpuMeshCache.Count > 64 && skinnedMeshDraws.Count == 0 && shaderGraphSkinnedMeshDraws.Count == 0)
            ClearSkinnedGpuMeshCache();

        int sourceCount = mesh.TriangleCount * 3;
        if (sourceCount <= 0 || mesh.BoneIndices.Length < sourceCount * 4 || mesh.BoneWeights.Length < sourceCount * 4)
            return null;

        var verts = new SkinnedVertex[sourceCount];
        int actual = 0;
        for (int v = 0; v < sourceCount; v++)
        {
            int pi = v * 3;
            if (mesh.Positions.Length <= pi + 2)
                break;

            var pos = new Vector3(mesh.Positions[pi], mesh.Positions[pi + 1], mesh.Positions[pi + 2]);
            var norm = mesh.Normals.Length > pi + 2
                ? new Vector3(mesh.Normals[pi], mesh.Normals[pi + 1], mesh.Normals[pi + 2]).Normalized()
                : Vector3.UnitY;
            int ui = v * 2;
            var uv = mesh.UVs.Length > ui + 1
                ? new Vector2(mesh.UVs[ui], mesh.UVs[ui + 1])
                : new Vector2(0.5f, 0.5f);
            int bi = v * 4;
            var indices = new Vector4(mesh.BoneIndices[bi], mesh.BoneIndices[bi + 1], mesh.BoneIndices[bi + 2], mesh.BoneIndices[bi + 3]);
            var weights = new Vector4(mesh.BoneWeights[bi], mesh.BoneWeights[bi + 1], mesh.BoneWeights[bi + 2], mesh.BoneWeights[bi + 3]);

            verts[actual++] = new SkinnedVertex(pos, norm, Vector4.One, uv, new Vector4(0f, 0.5f, 0f, 0f), Vector4.Zero, indices, weights);
        }

        if (actual <= 0)
            return null;

        if (actual != verts.Length)
            Array.Resize(ref verts, actual);

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, actual * Unsafe.SizeOf<SkinnedVertex>(), verts, BufferUsageHint.StaticDraw);
        ConfigureSkinnedVertexAttributes(Unsafe.SizeOf<SkinnedVertex>());
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        cached = new CachedSkinnedGpuMesh(vao, vbo, actual, fileTime);
        skinnedGpuMeshCache[cacheKey] = cached;
        return cached;
    }

    private static void ConfigureSolidVertexAttributes(int stride)
    {
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 24);
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 40);
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, 48);
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, stride, 64);
    }

    private static void DeleteGpuMesh(CachedGpuMesh mesh)
    {
        if (mesh.Vbo != 0) GL.DeleteBuffer(mesh.Vbo);
        if (mesh.Vao != 0) GL.DeleteVertexArray(mesh.Vao);
    }

    private static void DeleteSkinnedGpuMesh(CachedSkinnedGpuMesh mesh)
    {
        if (mesh.Vbo != 0) GL.DeleteBuffer(mesh.Vbo);
        if (mesh.Vao != 0) GL.DeleteVertexArray(mesh.Vao);
    }

    private void ClearGpuMeshCache()
    {
        foreach (var mesh in gpuMeshCache.Values)
            DeleteGpuMesh(mesh);
        gpuMeshCache.Clear();
    }

    private void ClearSkinnedGpuMeshCache()
    {
        foreach (var mesh in skinnedGpuMeshCache.Values)
            DeleteSkinnedGpuMesh(mesh);
        skinnedGpuMeshCache.Clear();
    }

    private void ConfigureGpuMeshVertexAttributes(int stride)
    {
        ConfigureSolidVertexAttributes(stride);

        GL.BindBuffer(BufferTarget.ArrayBuffer, dynamicInstanceBuffer);
        int matrixStride = Unsafe.SizeOf<Matrix4>();
        for (int i = 0; i < 4; i++)
        {
            int location = 6 + i;
            GL.EnableVertexAttribArray(location);
            GL.VertexAttribPointer(location, 4, VertexAttribPointerType.Float, false, matrixStride, i * 16);
            GL.VertexAttribDivisor(location, 1);
        }
    }

    private void ConfigureSkinnedVertexAttributes(int stride)
    {
        ConfigureSolidVertexAttributes(stride);
        GL.EnableVertexAttribArray(10);
        GL.VertexAttribPointer(10, 4, VertexAttribPointerType.Float, false, stride, 80);
        GL.EnableVertexAttribArray(11);
        GL.VertexAttribPointer(11, 4, VertexAttribPointerType.Float, false, stride, 96);
    }

    private void InitializeInstanceBuffer()
    {
        dynamicInstanceBufferCapacity = 65536;
        GL.BindBuffer(BufferTarget.ArrayBuffer, dynamicInstanceBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, dynamicInstanceBufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        var identity = Matrix4.Identity;
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, Unsafe.SizeOf<Matrix4>(), ref identity);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    public void PrewarmMeshAsset(string? path, float importScale = 1f, IReadOnlyList<string>? materialSlots = null)
    {
        string? fullPath = NormalizeExistingAssetPath(path);
        if (fullPath == null)
            return;

        var mesh = GetParsedMesh(fullPath);
        if (mesh == null)
            return;

        float scale = importScale <= 0f ? 1f : importScale;
        _ = GetGpuMesh(mesh, fullPath, scale);

        for (int i = 0; i < mesh.Submeshes.Count; i++)
        {
            string? slotPath = materialSlots != null && i < materialSlots.Count ? materialSlots[i] : null;
            PrewarmMaterialAsset(slotPath);

            string? texturePath = mesh.Submeshes[i].TexturePath;
            if (!string.IsNullOrWhiteSpace(texturePath))
                _ = GetTexture(texturePath);
        }
    }

    private ParsedMesh? GetParsedMesh(string? path)
    {
        string? fullPath = NormalizeExistingAssetPath(path);
        if (fullPath == null)
        {
            LogAssetWarning($"[SceneViewportRenderer] Mesh no encontrado: {path}");
            return null;
        }

        DateTime fileTime = System.IO.File.GetLastWriteTimeUtc(fullPath);
        if (parsedMeshCache.TryGetValue(fullPath, out var cached) && cached.FileTime == fileTime)
            return cached.Mesh;

        try
        {
            var mesh = ObjLoader.Load(fullPath);
            if (mesh == null)
            {
                LogAssetWarning($"[SceneViewportRenderer] No se pudo cargar el mesh: {fullPath}");
                return null;
            }

            if (parsedMeshCache.Count > 128)
                parsedMeshCache.Clear();

            parsedMeshCache[fullPath] = new CachedParsedMesh(mesh, fileTime);
            return mesh;
        }
        catch (Exception ex)
        {
            LogAssetWarning($"[SceneViewportRenderer] Error cargando mesh '{fullPath}': {ex.Message}");
            return null;
        }
    }
}
