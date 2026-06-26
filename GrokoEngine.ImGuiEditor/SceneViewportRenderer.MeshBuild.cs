using GrokoEngine;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private void BuildMeshBounds(GameObject obj, MeshFilter filter, ParsedMesh mesh, bool selected, Matrix4 transform)
    {
        if (mesh == null || string.IsNullOrWhiteSpace(filter.MeshPath)) return;

        Vector4 fallbackColor = new Vector4(0.62f, 0.72f, 1f, 1f);
        Vector4 color = GetObjectColor(obj, fallbackColor);
        Vector4 lineColor = selected ? new Vector4(1f, 0.78f, 0.12f, 1f) : color;

        float scale = filter.ImportScale <= 0f ? 1f : filter.ImportScale;
        var min = new Vector3(mesh.BoundsMin.X * scale, mesh.BoundsMin.Y * scale, mesh.BoundsMin.Z * scale);
        var max = new Vector3(mesh.BoundsMax.X * scale, mesh.BoundsMax.Y * scale, mesh.BoundsMax.Z * scale);

        // Hijo de un FBX importado "con hijos": la caja/selección es la de SU parte, no la del modelo entero.
        if (filter.SubmeshIndex >= 0 && filter.SubmeshIndex < mesh.Submeshes.Count)
        {
            var sb = mesh.Submeshes[filter.SubmeshIndex];
            min = new Vector3(sb.MinX * scale, sb.MinY * scale, sb.MinZ * scale);
            max = new Vector3(sb.MaxX * scale, sb.MaxY * scale, sb.MaxZ * scale);
        }

        string meshPath = NormalizeExistingAssetPath(filter.MeshPath) ?? filter.MeshPath;

        var animator = obj.GetComponent<Animator>();
        bool skinned = animator != null && mesh.HasSkin && mesh.BoneNames.Count > 0
            && mesh.BoneIndices.Length == (mesh.Positions.Length / 3) * 4;
        System.Numerics.Matrix4x4[]? skinMats = null;
        if (skinned)
        {
            try { skinMats = animator!.ComputeSkinMatrices(mesh.BoneNames, mesh.BoneOffsets); }
            catch { skinned = false; skinMats = null; }
        }

        bool useSkinnedBounds = false;
        Vector3 skinnedMin = default, skinnedMax = default;
        if (skinned && skinMats != null && (ShowObjectWireframes || selected))
            useSkinnedBounds = TryComputeSkinnedWorldBounds(mesh, skinMats, out skinnedMin, out skinnedMax);

        int skinKey = (skinned && animator != null)
            ? HashCode.Combine(RuntimeHelpers.GetHashCode(animator), animator.PoseVersion)
            : 0;

        if (mesh.Submeshes.Count > 1)
        {
            for (int i = 0; i < mesh.Submeshes.Count; i++)
            {
                if (filter.SubmeshIndex >= 0 && i != filter.SubmeshIndex) continue;
                var sub = mesh.Submeshes[i];
                string? slotPath = i < filter.MaterialSlots.Count ? filter.MaterialSlots[i] : null;
                if (!string.IsNullOrWhiteSpace(PreviewMaterialAssetPath) && PreviewMaterialObjectId == obj.EditorId && PreviewMaterialSubmeshIndex == i)
                    slotPath = PreviewMaterialAssetPath;
                var surf = GetSubmeshSurface(slotPath, sub, fallbackColor);
                if (skinned && TryQueueSkinnedMeshDraw(mesh, skinMats!, transform, scale, surf.Color, meshPath, surf.Material, surf.Emission, sub.VertexStart, sub.VertexCount, surf.TexturePath, surf.Maps.NormalMapPath, surf.Maps.RoughnessMapPath, surf.Maps.MetallicMapPath, surf.ShaderGraphPath, surf.ShaderGraphProperties, surf.ShaderGraphTextures))
                {
                    // GPU skinning path.
                }
                else if (skinned)
                {
                    AddSolidRange(surf.TexturePath, surf.Maps.NormalMapPath, surf.Maps.RoughnessMapPath, surf.Maps.MetallicMapPath, surf.ShaderGraphPath, surf.ShaderGraphProperties, surf.ShaderGraphTextures,
                        () => AddSkinnedMeshTriangles(mesh, skinMats!, transform, surf.Color, surf.Material, surf.Emission, sub.VertexStart, sub.VertexCount, skinKey));
                }
                else if (!TryQueueDynamicMeshDraw(mesh, transform, scale, surf.Color, meshPath, surf.Material, surf.Emission, sub.VertexStart, sub.VertexCount, surf.TexturePath, surf.Maps.NormalMapPath, surf.Maps.RoughnessMapPath, surf.Maps.MetallicMapPath, surf.ShaderGraphPath, surf.ShaderGraphProperties, surf.ShaderGraphTextures))
                {
                    AddSolidRange(surf.TexturePath, surf.Maps.NormalMapPath, surf.Maps.RoughnessMapPath, surf.Maps.MetallicMapPath, surf.ShaderGraphPath, surf.ShaderGraphProperties, surf.ShaderGraphTextures,
                        () => AddMeshTriangles(mesh, transform, scale, surf.Color, meshPath, surf.Material, surf.Emission, sub.VertexStart, sub.VertexCount));
                }
            }
        }
        else
        {
            string? texturePath = GetObjectTexturePath(obj);
            var (material, emission) = GetObjectSurface(obj);
            var maps = GetObjectSurfaceMaps(obj);
            string? shaderGraphPath = GetObjectShaderGraphPath(obj);
            var shaderGraphProperties = GetObjectShaderGraphProperties(obj);
            var shaderGraphTextures = GetObjectShaderGraphTextures(obj);
            if (skinned && TryQueueSkinnedMeshDraw(mesh, skinMats!, transform, scale, color, meshPath, material, emission, 0, mesh.Positions.Length / 3, texturePath, maps.NormalMapPath, maps.RoughnessMapPath, maps.MetallicMapPath, shaderGraphPath, shaderGraphProperties, shaderGraphTextures))
            {
                // GPU skinning path.
            }
            else if (skinned)
            {
                AddSolidRange(texturePath, maps.NormalMapPath, maps.RoughnessMapPath, maps.MetallicMapPath, shaderGraphPath, shaderGraphProperties, shaderGraphTextures,
                    () => AddSkinnedMeshTriangles(mesh, skinMats!, transform, color, material, emission, 0, mesh.Positions.Length / 3, skinKey));
            }
            else if (!TryQueueDynamicMeshDraw(mesh, transform, scale, color, meshPath, material, emission, 0, mesh.Positions.Length / 3, texturePath, maps.NormalMapPath, maps.RoughnessMapPath, maps.MetallicMapPath, shaderGraphPath, shaderGraphProperties, shaderGraphTextures))
            {
                AddSolidRange(texturePath, maps.NormalMapPath, maps.RoughnessMapPath, maps.MetallicMapPath, shaderGraphPath, shaderGraphProperties, shaderGraphTextures,
                    () => AddMeshTriangles(mesh, transform, scale, color, meshPath, material, emission, 0, mesh.Positions.Length / 3));
            }
        }

        var bmin = useSkinnedBounds ? skinnedMin : min;
        var bmax = useSkinnedBounds ? skinnedMax : max;
        Span<Vector3> corners = stackalloc Vector3[]
        {
            new(bmin.X, bmin.Y, bmin.Z), new(bmax.X, bmin.Y, bmin.Z),
            new(bmax.X, bmax.Y, bmin.Z), new(bmin.X, bmax.Y, bmin.Z),
            new(bmin.X, bmin.Y, bmax.Z), new(bmax.X, bmin.Y, bmax.Z),
            new(bmax.X, bmax.Y, bmax.Z), new(bmin.X, bmax.Y, bmax.Z)
        };

        if (!useSkinnedBounds)
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

    private void AddMeshTriangles(ParsedMesh mesh, Matrix4 transform, float importScale, Vector4 color, string meshPath, Vector4 material, Vector4 emission, int vertexStart, int vertexCount)
    {
        var fileTime = System.IO.File.Exists(meshPath) ? System.IO.File.GetLastWriteTimeUtc(meshPath) : DateTime.MinValue;
        string cacheKey = meshPath + "|" + importScale.ToString("F6");

        if (!meshCache.TryGetValue(cacheKey, out var cached) || cached.FileTime != fileTime)
        {
            if (meshCache.Count > 128)
                meshCache.Clear();

            int count = mesh.TriangleCount * 3;
            var verts = new MeshVertData[count];
            int actual = 0;
            for (int v = 0; v < count; v++)
            {
                int pi = v * 3;
                if (mesh.Positions.Length <= pi + 2) break;
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
                verts[actual++] = new MeshVertData(pos, norm, uv);
            }
            cached = new CachedMeshVerts(verts, actual, fileTime);
            meshCache[cacheKey] = cached;
        }

        var v2 = cached.Verts;
        int from = Math.Clamp(vertexStart, 0, cached.Count);
        int to = Math.Clamp(vertexStart + vertexCount, 0, cached.Count);
        for (int i = from; i < to; i++)
        {
            ref readonly var src = ref v2[i];
            ActiveSolidVertices.Add(new SolidVertex(Vector3.TransformPosition(src.Position, transform), Vector3.TransformNormal(src.Normal, transform).Normalized(), color, src.Uv, material, emission));
        }
    }

    private static bool TryComputeSkinnedWorldBounds(ParsedMesh mesh, System.Numerics.Matrix4x4[] skin, out Vector3 min, out Vector3 max)
    {
        min = default; max = default;
        int total = mesh.Positions.Length / 3;
        float mnx = float.MaxValue, mny = float.MaxValue, mnz = float.MaxValue;
        float mxx = float.MinValue, mxy = float.MinValue, mxz = float.MinValue;
        bool any = false;
        for (int v = 0; v < total; v++)
        {
            int pi = v * 3, bo = v * 4;
            var p = new System.Numerics.Vector3(mesh.Positions[pi], mesh.Positions[pi + 1], mesh.Positions[pi + 2]);
            var sp = System.Numerics.Vector3.Zero; float w = 0f;
            for (int k = 0; k < 4; k++)
            {
                int bi = mesh.BoneIndices[bo + k]; float ww = mesh.BoneWeights[bo + k];
                if (bi < 0 || bi >= skin.Length || ww <= 0f) continue;
                sp += ww * System.Numerics.Vector3.Transform(p, skin[bi]); w += ww;
            }
            if (w <= 0.0001f || !float.IsFinite(sp.X) || !float.IsFinite(sp.Y) || !float.IsFinite(sp.Z)) continue;
            any = true;
            if (sp.X < mnx) mnx = sp.X; if (sp.Y < mny) mny = sp.Y; if (sp.Z < mnz) mnz = sp.Z;
            if (sp.X > mxx) mxx = sp.X; if (sp.Y > mxy) mxy = sp.Y; if (sp.Z > mxz) mxz = sp.Z;
        }
        if (!any) return false;
        min = new Vector3(mnx, mny, mnz); max = new Vector3(mxx, mxy, mxz);
        return true;
    }

    private void AddSkinnedMeshTriangles(ParsedMesh mesh, System.Numerics.Matrix4x4[] skin, Matrix4 transform,
        Vector4 color, Vector4 material, Vector4 emission, int vertexStart, int vertexCount, int skinKey)
    {
        int total = mesh.Positions.Length / 3;
        int from = Math.Clamp(vertexStart, 0, total);
        int to = Math.Clamp(vertexStart + vertexCount, 0, total);
        int count = Math.Max(0, to - from);
        if (count <= 0)
            return;

        string cacheKey = BuildSkinnedRangeCacheKey(mesh, skinKey, transform, color, material, emission, from, count);
        if (skinnedRangeCache.TryGetValue(cacheKey, out var cached))
        {
            ActiveSolidVertices.AddRange(cached.Vertices);
            return;
        }

        if (skinnedRangeCache.Count > 96)
            skinnedRangeCache.Clear();

        bool hasNormals = mesh.Normals.Length >= mesh.Positions.Length;
        bool hasUv = mesh.UVs.Length >= total * 2;
        var vertices = new SolidVertex[count];
        int actual = 0;

        for (int v = from; v < to; v++)
        {
            int pi = v * 3;
            var p = new System.Numerics.Vector3(mesh.Positions[pi], mesh.Positions[pi + 1], mesh.Positions[pi + 2]);
            var nrm = hasNormals
                ? new System.Numerics.Vector3(mesh.Normals[pi], mesh.Normals[pi + 1], mesh.Normals[pi + 2])
                : new System.Numerics.Vector3(0, 1, 0);

            System.Numerics.Vector3 sp = System.Numerics.Vector3.Zero;
            System.Numerics.Vector3 sn = System.Numerics.Vector3.Zero;
            float wsum = 0f;
            int bo = v * 4;
            for (int k = 0; k < 4; k++)
            {
                int bi = mesh.BoneIndices[bo + k];
                float w = mesh.BoneWeights[bo + k];
                if (bi < 0 || bi >= skin.Length || w <= 0f) continue;
                var m = skin[bi];
                sp += w * System.Numerics.Vector3.Transform(p, m);
                sn += w * System.Numerics.Vector3.TransformNormal(nrm, m);
                wsum += w;
            }

            Vector3 worldPos, worldNrm;
            bool finite = float.IsFinite(sp.X) && float.IsFinite(sp.Y) && float.IsFinite(sp.Z);
            if (wsum > 0.0001f && finite)
            {
                worldPos = new Vector3(sp.X, sp.Y, sp.Z);
                var n = sn.LengthSquared() > 1e-12f ? System.Numerics.Vector3.Normalize(sn) : new System.Numerics.Vector3(0, 1, 0);
                worldNrm = new Vector3(n.X, n.Y, n.Z);
            }
            else
            {
                worldPos = Vector3.TransformPosition(new Vector3(p.X, p.Y, p.Z), transform);
                worldNrm = Vector3.TransformNormal(new Vector3(nrm.X, nrm.Y, nrm.Z), transform).Normalized();
            }

            var uv = hasUv ? new Vector2(mesh.UVs[v * 2], mesh.UVs[v * 2 + 1]) : new Vector2(0.5f, 0.5f);
            vertices[actual++] = new SolidVertex(worldPos, worldNrm, color, uv, material, emission);
        }

        if (actual != vertices.Length)
            Array.Resize(ref vertices, actual);

        skinnedRangeCache[cacheKey] = new CachedSkinnedRange(vertices);
        ActiveSolidVertices.AddRange(vertices);
    }

    private static string BuildSkinnedRangeCacheKey(ParsedMesh mesh, int skinKey, Matrix4 transform,
        Vector4 color, Vector4 material, Vector4 emission, int start, int count)
    {
        var hash = new HashCode();
        hash.Add(RuntimeHelpers.GetHashCode(mesh));
        hash.Add(skinKey);
        hash.Add(start);
        hash.Add(count);
        AddMatrixHash(ref hash, transform);
        AddVectorHash(ref hash, color);
        AddVectorHash(ref hash, material);
        AddVectorHash(ref hash, emission);
        return hash.ToHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddMatrixHash(ref HashCode hash, Matrix4 m)
    {
        hash.Add(m.M11); hash.Add(m.M12); hash.Add(m.M13); hash.Add(m.M14);
        hash.Add(m.M21); hash.Add(m.M22); hash.Add(m.M23); hash.Add(m.M24);
        hash.Add(m.M31); hash.Add(m.M32); hash.Add(m.M33); hash.Add(m.M34);
        hash.Add(m.M41); hash.Add(m.M42); hash.Add(m.M43); hash.Add(m.M44);
    }

    private static void AddVectorHash(ref HashCode hash, Vector4 v)
    {
        hash.Add(v.X); hash.Add(v.Y); hash.Add(v.Z); hash.Add(v.W);
    }

    private bool TryQueueDynamicMeshDraw(
        ParsedMesh mesh,
        Matrix4 transform,
        float importScale,
        Vector4 color,
        string meshPath,
        Vector4 material,
        Vector4 emission,
        int vertexStart,
        int vertexCount,
        string? texturePath,
        string? normalMapPath,
        string? roughnessMapPath,
        string? metallicMapPath,
        string? shaderGraphPath,
        IReadOnlyDictionary<string, float[]>? shaderGraphProperties,
        IReadOnlyDictionary<string, string>? shaderGraphTextures,
        bool castShadows = true,
        bool receiveShadows = true)
    {
        if (_buildingStatic)
            return false;

        var gpuMesh = GetGpuMesh(mesh, meshPath, importScale);
        if (gpuMesh == null || gpuMesh.Count <= 0)
            return false;

        int start = Math.Clamp(vertexStart, 0, gpuMesh.Count);
        int count = Math.Clamp(vertexCount, 0, gpuMesh.Count - start);
        if (count <= 0)
            return false;

        shaderGraphPath = NormalizeExistingAssetPath(shaderGraphPath);
        if (!string.IsNullOrWhiteSpace(shaderGraphPath))
        {
            shaderGraphDynamicMeshDraws.Add(new ShaderGraphDynamicMeshDraw(
                gpuMesh,
                transform,
                start,
                count,
                shaderGraphPath,
                shaderGraphProperties,
                shaderGraphTextures));
            return true;
        }

        dynamicMeshDraws.Add(new DynamicMeshDraw(
            gpuMesh,
            transform,
            start,
            count,
            NormalizeTexturePath(texturePath),
            NormalizeTexturePath(normalMapPath),
            NormalizeTexturePath(roughnessMapPath),
            NormalizeTexturePath(metallicMapPath),
            color,
            material,
            emission,
            castShadows,
            receiveShadows));
        return true;
    }

    private bool TryQueueSkinnedMeshDraw(
        ParsedMesh mesh,
        System.Numerics.Matrix4x4[] skin,
        Matrix4 transform,
        float importScale,
        Vector4 color,
        string meshPath,
        Vector4 material,
        Vector4 emission,
        int vertexStart,
        int vertexCount,
        string? texturePath,
        string? normalMapPath,
        string? roughnessMapPath,
        string? metallicMapPath,
        string? shaderGraphPath,
        IReadOnlyDictionary<string, float[]>? shaderGraphProperties,
        IReadOnlyDictionary<string, string>? shaderGraphTextures)
    {
        if (_buildingStatic || skin.Length > MaxGpuBones)
            return false;

        var gpuMesh = GetSkinnedGpuMesh(mesh, meshPath, importScale);
        if (gpuMesh == null || gpuMesh.Count <= 0)
            return false;

        int start = Math.Clamp(vertexStart, 0, gpuMesh.Count);
        int count = Math.Clamp(vertexCount, 0, gpuMesh.Count - start);
        if (count <= 0)
            return false;

        shaderGraphPath = NormalizeExistingAssetPath(shaderGraphPath);
        if (!string.IsNullOrWhiteSpace(shaderGraphPath))
        {
            shaderGraphSkinnedMeshDraws.Add(new ShaderGraphSkinnedMeshDraw(
                gpuMesh,
                transform,
                skin,
                start,
                count,
                shaderGraphPath,
                shaderGraphProperties,
                shaderGraphTextures));
            return true;
        }

        skinnedMeshDraws.Add(new SkinnedMeshDraw(
            gpuMesh,
            transform,
            skin,
            start,
            count,
            NormalizeTexturePath(texturePath),
            NormalizeTexturePath(normalMapPath),
            NormalizeTexturePath(roughnessMapPath),
            NormalizeTexturePath(metallicMapPath),
            color,
            material,
            emission));
        return true;
    }
}
