using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GrokoEngine;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private void EnsureCapacity(int requiredBytes)
    {
        if (requiredBytes <= bufferCapacity) return;
        bufferCapacity = Math.Max(requiredBytes, bufferCapacity == 0 ? 65536 : bufferCapacity * 2);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, bufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
    }

    private void EnsureLineUploadBuffer()
    {
        if (lineUploadBuffer.Length >= vertices.Count) return;
        lineUploadBuffer = new LineVertex[Math.Max(vertices.Count, lineUploadBuffer.Length == 0 ? 4096 : lineUploadBuffer.Length * 2)];
    }

    private void UploadStaticBatch()
    {
        _staticVertexCount = _staticBuildList.Count;
        if (_staticVertexCount == 0) return;

        int required = _staticVertexCount * Unsafe.SizeOf<SolidVertex>();

        // Reservar buffer GPU con StaticDraw (driver optimiza para lectura frecuente)
        if (required > _staticBufferCapacity)
        {
            _staticBufferCapacity = Math.Max(required, _staticBufferCapacity == 0 ? 65536 : _staticBufferCapacity * 2);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _staticVertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, _staticBufferCapacity, IntPtr.Zero, BufferUsageHint.StaticDraw);
        }

        // Preparar buffer de upload
        if (_staticUploadBuf.Length < _staticVertexCount)
            _staticUploadBuf = new SolidVertex[Math.Max(_staticVertexCount, _staticUploadBuf.Length == 0 ? 4096 : _staticUploadBuf.Length * 2)];

        _staticBuildList.CopyTo(_staticUploadBuf);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _staticVertexBuffer);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
            _staticVertexCount * Unsafe.SizeOf<SolidVertex>(), _staticUploadBuf);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    private void EnsureSolidCapacity(int requiredBytes)
    {
        if (requiredBytes <= solidBufferCapacity) return;
        solidBufferCapacity = Math.Max(requiredBytes, solidBufferCapacity == 0 ? 65536 : solidBufferCapacity * 2);
        GL.BindBuffer(BufferTarget.ArrayBuffer, solidVertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, solidBufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
    }

    private void EnsureSolidUploadBuffer()
    {
        if (solidUploadBuffer.Length >= solidVertices.Count) return;
        solidUploadBuffer = new SolidVertex[Math.Max(solidVertices.Count, solidUploadBuffer.Length == 0 ? 4096 : solidUploadBuffer.Length * 2)];
    }

    private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector4 color)
        => AddQuad(a, b, c, d, normal, color, new Vector4(0f, 0.5f, 3f, 0f), Vector4.Zero);

    private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector4 color, Vector4 material, Vector4 emission)
    {
        ActiveSolidVertices.Add(new SolidVertex(a, normal, color, new Vector2(0f, 1f), material, emission));
        ActiveSolidVertices.Add(new SolidVertex(b, normal, color, new Vector2(1f, 1f), material, emission));
        ActiveSolidVertices.Add(new SolidVertex(c, normal, color, new Vector2(1f, 0f), material, emission));
        ActiveSolidVertices.Add(new SolidVertex(a, normal, color, new Vector2(0f, 1f), material, emission));
        ActiveSolidVertices.Add(new SolidVertex(c, normal, color, new Vector2(1f, 0f), material, emission));
        ActiveSolidVertices.Add(new SolidVertex(d, normal, color, new Vector2(0f, 0f), material, emission));
    }

    // Listas activas: según _buildingStatic, apuntan al buffer estático o dinámico.
    private List<SolidVertex> ActiveSolidVertices => _buildingStatic ? _staticBuildList : solidVertices;
    private List<SolidRange> ActiveSolidRanges => _buildingStatic ? _staticRanges : solidRanges;

    // El batch combina muchas mallas en un VBO; este contexto por objeto conserva el lightmap
    // que corresponde a cada rango de triángulos del batch.
    private string? _activeLightmapPath;
    private string? _activeShadowmaskPath;
    private float _activeLightmapIntensity = 1f;
    private Vector4 _activeLightmapScaleOffset = new(1f, 1f, 0f, 0f);
    private int _activeLightmapVertexStart;
    private int _activeLightmapVertexCount;

    private void SetActiveLightmap(MeshRenderer? renderer) => SetActiveLightmap(renderer, 0, 0);

    private void SetActiveLightmap(MeshRenderer? renderer, int vertexStart, int vertexCount)
    {
        _activeLightmapPath = null;
        _activeShadowmaskPath = null;
        _activeLightmapIntensity = 0f;
        _activeLightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
        _activeLightmapVertexStart = Math.Max(0, vertexStart);
        _activeLightmapVertexCount = Math.Max(0, vertexCount);
        if (renderer == null || renderer.ReceiveGlobalIllumination != 1 ||
            !renderer.UseGeneratedLightmapUv || string.IsNullOrWhiteSpace(renderer.LightmapPath))
            return;

        string? path = NormalizeExistingAssetPath(renderer.LightmapPath);
        if (path == null || !System.IO.File.Exists(path))
            return;

        _activeLightmapPath = path;
        _activeShadowmaskPath = NormalizeExistingAssetPath(renderer.ShadowmaskPath);
        if (_activeShadowmaskPath == null || !System.IO.File.Exists(_activeShadowmaskPath))
            _activeShadowmaskPath = null;
        _activeLightmapIntensity = Math.Clamp(renderer.LightmapIntensity, 0f, 8f);
        _activeLightmapScaleOffset = new Vector4(
            Math.Clamp(renderer.LightmapScaleX, 0.00001f, 1f),
            Math.Clamp(renderer.LightmapScaleY, 0.00001f, 1f),
            Math.Clamp(renderer.LightmapOffsetX, 0f, 1f),
            Math.Clamp(renderer.LightmapOffsetY, 0f, 1f));
    }

    private void AddSolidRange(string? texturePath, Action build)
    {
        int start = ActiveSolidVertices.Count;
        build();
        AddSolidRange(start, texturePath);
    }

    private void AddSolidRange(string? texturePath, string? normalMapPath, string? roughnessMapPath, string? metallicMapPath, Action build)
        => AddSolidRange(texturePath, normalMapPath, roughnessMapPath, metallicMapPath, null, null, null, false, 0.5f, build);

    private void AddSolidRange(string? texturePath, string? normalMapPath, string? roughnessMapPath, string? metallicMapPath, string? shaderGraphPath, IReadOnlyDictionary<string, float[]>? shaderGraphProperties, IReadOnlyDictionary<string, string>? shaderGraphTextures, Action build)
        => AddSolidRange(texturePath, normalMapPath, roughnessMapPath, metallicMapPath, shaderGraphPath, shaderGraphProperties, shaderGraphTextures, false, 0.5f, build);

    private void AddSolidRange(string? texturePath, string? normalMapPath, string? roughnessMapPath, string? metallicMapPath, string? shaderGraphPath, IReadOnlyDictionary<string, float[]>? shaderGraphProperties, IReadOnlyDictionary<string, string>? shaderGraphTextures, bool alphaClip, float alphaClipThreshold, Action build)
    {
        int start = ActiveSolidVertices.Count;
        build();
        AddSolidRange(start, texturePath, normalMapPath, roughnessMapPath, metallicMapPath, shaderGraphPath, shaderGraphProperties, shaderGraphTextures, alphaClip, alphaClipThreshold);
    }

    private void AddSolidRange(int start, string? texturePath)
    {
        int count = ActiveSolidVertices.Count - start;
        if (count > 0)
            ActiveSolidRanges.Add(new SolidRange(start, count, NormalizeTexturePath(texturePath), null, null, null,
                LightmapPath: _activeLightmapPath, ShadowmaskPath: _activeShadowmaskPath, LightmapIntensity: _activeLightmapIntensity,
                LightmapScaleOffset: _activeLightmapScaleOffset));
    }

    private void AddSolidRange(int start, string? texturePath, string? normalMapPath, string? roughnessMapPath, string? metallicMapPath, string? shaderGraphPath = null, IReadOnlyDictionary<string, float[]>? shaderGraphProperties = null, IReadOnlyDictionary<string, string>? shaderGraphTextures = null, bool alphaClip = false, float alphaClipThreshold = 0.5f)
    {
        int count = ActiveSolidVertices.Count - start;
        if (count > 0)
            ActiveSolidRanges.Add(new SolidRange(
                start,
                count,
                NormalizeTexturePath(texturePath),
                NormalizeTexturePath(normalMapPath),
                NormalizeTexturePath(roughnessMapPath),
                NormalizeTexturePath(metallicMapPath),
                NormalizeExistingAssetPath(shaderGraphPath),
                shaderGraphProperties,
                shaderGraphTextures,
                alphaClip,
                Math.Clamp(alphaClipThreshold, 0f, 1f),
                _activeLightmapPath,
                _activeShadowmaskPath,
                _activeLightmapIntensity,
                _activeLightmapScaleOffset));
    }

    private void AddLine(Vector3 a, Vector3 b, Vector4 color)
    {
        vertices.Add(new LineVertex(a, color));
        vertices.Add(new LineVertex(b, color));
    }
}
