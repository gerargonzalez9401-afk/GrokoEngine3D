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
private void ProcessOcclusionQueries(ref Matrix4 mvp, IReadOnlyList<GameObject> objects)
    {
        // ── 1. Harvest: leer resultados disponibles sin bloquear el frame ──
        var harvested = new List<string>();
        foreach (var (id, qid) in _occlusionQueries)
        {
            GL.GetQueryObject(qid, GetQueryObjectParam.QueryResultAvailable, out int avail);
            if (avail == 0)
                continue;

            // Usar long para no truncar conteos grandes en GPUs/resoluciones altas.
            GL.GetQueryObject(qid, GetQueryObjectParam.QueryResult, out long samples);
            _occlusionVisible[id] = samples > 0;
            GL.DeleteQuery(qid);
            harvested.Add(id);
        }

        foreach (var id in harvested)
            _occlusionQueries.Remove(id);

        // ── 2. Emitir nuevas queries usando VAO/VBO dedicado ──
        GL.Enable(EnableCap.DepthTest);
        GL.ColorMask(false, false, false, false);
        GL.DepthMask(false);

        try
        {
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            GL.Uniform1(solidHasTextureLocation, 0);
            GL.Uniform1(solidTextureLocation, 0);
            ApplySceneLighting(objects, Vector3.Zero);

            GL.BindVertexArray(_occVao);

            foreach (var (id, world, mf, mesh) in _pendingOcclusionObjects)
            {
                if (_occlusionQueries.ContainsKey(id))
                    continue;

                if (_occlusionQueryFrame.TryGetValue(id, out int lastFrame) &&
                    (_frameCount - lastFrame) < OcclusionQueryInterval)
                    continue;

                GL.GenQueries(1, out int qid);
                GL.BeginQuery(QueryTarget.SamplesPassed, qid);
                DrawOcclusionBBox(world, mf, mesh);
                GL.EndQuery(QueryTarget.SamplesPassed);

                _occlusionQueries[id] = qid;
                _occlusionQueryFrame[id] = _frameCount;
            }
        }
        finally
        {
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
        }
    }

private void DrawOcclusionBBox(Matrix4 world, MeshFilter mf, ParsedMesh mesh)
    {
        float s = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
        // Margen del 5% para reducir falsos negativos en bordes sin encoger bounds positivos.
        float margin = 0.05f;
        var rawMin = new Vector3(mesh.BoundsMin.X * s, mesh.BoundsMin.Y * s, mesh.BoundsMin.Z * s);
        var rawMax = new Vector3(mesh.BoundsMax.X * s, mesh.BoundsMax.Y * s, mesh.BoundsMax.Z * s);
        var bboxCenter = (rawMin + rawMax) * 0.5f;
        var bboxExtent = (rawMax - rawMin) * 0.5f * (1f + margin);
        var minB = bboxCenter - bboxExtent;
        var maxB = bboxCenter + bboxExtent;

        Span<Vector3> c = stackalloc Vector3[8];
        c[0] = Vector3.TransformPosition(new Vector3(minB.X, minB.Y, minB.Z), world);
        c[1] = Vector3.TransformPosition(new Vector3(maxB.X, minB.Y, minB.Z), world);
        c[2] = Vector3.TransformPosition(new Vector3(maxB.X, maxB.Y, minB.Z), world);
        c[3] = Vector3.TransformPosition(new Vector3(minB.X, maxB.Y, minB.Z), world);
        c[4] = Vector3.TransformPosition(new Vector3(minB.X, minB.Y, maxB.Z), world);
        c[5] = Vector3.TransformPosition(new Vector3(maxB.X, minB.Y, maxB.Z), world);
        c[6] = Vector3.TransformPosition(new Vector3(maxB.X, maxB.Y, maxB.Z), world);
        c[7] = Vector3.TransformPosition(new Vector3(minB.X, maxB.Y, maxB.Z), world);

        var white = new Vector4(1f, 1f, 1f, 1f);
        var normal = Vector3.UnitY;

        _bboxScratch.Clear();
        AddQuadDirect(_bboxScratch, c[4], c[5], c[6], c[7], normal, white); // +Z
        AddQuadDirect(_bboxScratch, c[1], c[0], c[3], c[2], normal, white); // -Z
        AddQuadDirect(_bboxScratch, c[3], c[7], c[6], c[2], normal, white); // +Y
        AddQuadDirect(_bboxScratch, c[0], c[1], c[5], c[4], normal, white); // -Y
        AddQuadDirect(_bboxScratch, c[1], c[2], c[6], c[5], normal, white); // +X
        AddQuadDirect(_bboxScratch, c[0], c[4], c[7], c[3], normal, white); // -X

        int count = _bboxScratch.Count;
        if (count == 0) return;

        int requiredBytes = count * Unsafe.SizeOf<SolidVertex>();

        // Bug fix: usar VBO dedicado — NO tocar solidVertexBuffer principal
        GL.BindBuffer(BufferTarget.ArrayBuffer, _occVbo);
        if (requiredBytes > _occVboCapacity)
        {
            _occVboCapacity = Math.Max(requiredBytes, _occVboCapacity == 0 ? 4096 : _occVboCapacity * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, _occVboCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }

        // Upload bbox verts sin crear arrays nuevos cada frame.
        if (_bboxUploadBuffer.Length < count)
            _bboxUploadBuffer = new SolidVertex[Math.Max(count, _bboxUploadBuffer.Length == 0 ? 64 : _bboxUploadBuffer.Length * 2)];
        _bboxScratch.CopyTo(_bboxUploadBuffer);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, requiredBytes, _bboxUploadBuffer);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        GL.DrawArrays(PrimitiveType.Triangles, 0, count);
    }

private static void AddQuadDirect(List<SolidVertex> list, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector4 col)
    {
        list.Add(new SolidVertex(a, n, col, new Vector2(0f, 1f)));
        list.Add(new SolidVertex(b, n, col, new Vector2(1f, 1f)));
        list.Add(new SolidVertex(c, n, col, new Vector2(1f, 0f)));
        list.Add(new SolidVertex(a, n, col, new Vector2(0f, 1f)));
        list.Add(new SolidVertex(c, n, col, new Vector2(1f, 0f)));
        list.Add(new SolidVertex(d, n, col, new Vector2(0f, 0f)));
    }

public void CleanupOcclusionQueries(IReadOnlyList<string> liveObjectIds)
    {
        var liveSet = new HashSet<string>(liveObjectIds, StringComparer.Ordinal);

        var toDelete = new List<string>();
        foreach (var kv in _occlusionQueries)
        {
            if (!liveSet.Contains(kv.Key))
            {
                GL.DeleteQuery(kv.Value);
                toDelete.Add(kv.Key);
            }
        }
        foreach (var key in toDelete)
        {
            _occlusionQueries.Remove(key);
            _occlusionVisible.Remove(key);
            _occlusionQueryFrame.Remove(key);
        }
    }

public void InvalidateCullingState()
    {
        foreach (var qid in _occlusionQueries.Values)
            GL.DeleteQuery(qid);
        _occlusionQueries.Clear();
        _occlusionVisible.Clear();
        _occlusionQueryFrame.Clear();
        _pendingOcclusionObjects.Clear();
    }
}
