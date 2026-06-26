using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private void SortDynamicMeshDrawsForStateReuse()
    {
        if (dynamicMeshDraws.Count <= 1)
            return;

        dynamicMeshDraws.Sort(static (a, b) =>
        {
            int cmp = StringComparer.OrdinalIgnoreCase.Compare(a.TexturePath, b.TexturePath);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(a.NormalMapPath, b.NormalMapPath);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(a.RoughnessMapPath, b.RoughnessMapPath);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(a.MetallicMapPath, b.MetallicMapPath);
            if (cmp != 0) return cmp;
            cmp = a.Mesh.Vao.CompareTo(b.Mesh.Vao);
            if (cmp != 0) return cmp;
            cmp = a.Start.CompareTo(b.Start);
            if (cmp != 0) return cmp;
            cmp = CompareVector4(a.Color, b.Color);
            if (cmp != 0) return cmp;
            cmp = CompareVector4(a.Material, b.Material);
            if (cmp != 0) return cmp;
            return CompareVector4(a.Emission, b.Emission);
        });
    }

    private static int CompareVector4(Vector4 a, Vector4 b)
    {
        int cmp = a.X.CompareTo(b.X);
        if (cmp != 0) return cmp;
        cmp = a.Y.CompareTo(b.Y);
        if (cmp != 0) return cmp;
        cmp = a.Z.CompareTo(b.Z);
        if (cmp != 0) return cmp;
        return a.W.CompareTo(b.W);
    }

    private void DrawDynamicMeshDraws(ref Matrix4 mvp, ShadowInfo shadowInfo, ShadowInfo spotShadowInfo, PointShadowInfo pointShadowInfo, IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        GL.UseProgram(solidShader);
        GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
        GL.Uniform1(solidTextureLocation, 0);
        ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
        ApplySceneLighting(objects, cameraPosition);
        GL.ActiveTexture(TextureUnit.Texture0);

        bool hasBoundSurface = false;
        string? boundTexture = null;
        string? boundNormal = null;
        string? boundRoughness = null;
        string? boundMetallic = null;
        int boundVao = 0;

        for (int i = 0; i < dynamicMeshDraws.Count;)
        {
            var draw = dynamicMeshDraws[i];
            int end = i + 1;
            while (end < dynamicMeshDraws.Count && CanInstanceTogether(draw, dynamicMeshDraws[end]))
                end++;

            ApplyShadowUniforms(
                draw.ReceiveShadows ? shadowInfo : ShadowInfo.Disabled,
                draw.ReceiveShadows ? spotShadowInfo : new ShadowInfo(false, Matrix4.Identity, 0f),
                draw.ReceiveShadows ? pointShadowInfo : new PointShadowInfo(false, Vector3.Zero, 1f, 0f));
            ApplyDynamicSurfaceUniforms(draw);
            ApplySurfaceTexturesIfChanged(
                draw.TexturePath,
                draw.NormalMapPath,
                draw.RoughnessMapPath,
                draw.MetallicMapPath,
                ref hasBoundSurface,
                ref boundTexture,
                ref boundNormal,
                ref boundRoughness,
                ref boundMetallic);

            if (boundVao != draw.Mesh.Vao)
            {
                GL.BindVertexArray(draw.Mesh.Vao);
                boundVao = draw.Mesh.Vao;
            }

            int instanceCount = end - i;
            if (instanceCount > 1)
            {
                UploadInstanceMatrices(i, instanceCount);
                SetIntUniform(solidUseInstancingLocation, 1);
                TrackMainDraw(draw.Count, instanceCount);
                GL.DrawArraysInstanced(PrimitiveType.Triangles, draw.Start, draw.Count, instanceCount);
                SetIntUniform(solidUseInstancingLocation, 0);
            }
            else
            {
                ApplyDynamicModelUniform(draw);
                SetIntUniform(solidUseInstancingLocation, 0);
                TrackMainDraw(draw.Count);
                GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
            }

            i = end;
        }

        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindVertexArray(0);
        ApplyBakedSolidVertexMode();
        GL.UseProgram(0);
    }

    private void DrawSkinnedMeshDraws(ref Matrix4 mvp, ShadowInfo shadowInfo, ShadowInfo spotShadowInfo, PointShadowInfo pointShadowInfo, IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        GL.UseProgram(solidShader);
        GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
        GL.Uniform1(solidTextureLocation, 0);
        ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
        ApplySceneLighting(objects, cameraPosition);
        GL.ActiveTexture(TextureUnit.Texture0);

        bool hasBoundSurface = false;
        string? boundTexture = null;
        string? boundNormal = null;
        string? boundRoughness = null;
        string? boundMetallic = null;
        int boundVao = 0;
        SetIntUniform(solidUseInstancingLocation, 0);
        SetIntUniform(solidUseSkinningLocation, 1);

        foreach (var draw in skinnedMeshDraws)
        {
            var world = draw.World;
            SetMatrixUniform(solidModelLocation, ref world);
            ApplySkinnedSurfaceUniforms(draw);
            UploadBoneMatrices(solidBoneMatrixLocations, draw.Skin);
            ApplySurfaceTexturesIfChanged(
                draw.TexturePath,
                draw.NormalMapPath,
                draw.RoughnessMapPath,
                draw.MetallicMapPath,
                ref hasBoundSurface,
                ref boundTexture,
                ref boundNormal,
                ref boundRoughness,
                ref boundMetallic);

            if (boundVao != draw.Mesh.Vao)
            {
                GL.BindVertexArray(draw.Mesh.Vao);
                boundVao = draw.Mesh.Vao;
            }

            TrackMainDraw(draw.Count);
            GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
        }

        SetIntUniform(solidUseSkinningLocation, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindVertexArray(0);
        ApplyBakedSolidVertexMode();
        GL.UseProgram(0);
    }

    private static bool CanInstanceTogether(DynamicMeshDraw a, DynamicMeshDraw b) =>
        ReferenceEquals(a.Mesh, b.Mesh) &&
        a.Start == b.Start &&
        a.Count == b.Count &&
        string.Equals(a.TexturePath, b.TexturePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.NormalMapPath, b.NormalMapPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.RoughnessMapPath, b.RoughnessMapPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.MetallicMapPath, b.MetallicMapPath, StringComparison.OrdinalIgnoreCase) &&
        a.Color == b.Color &&
        a.Material == b.Material &&
        a.Emission == b.Emission &&
        a.ReceiveShadows == b.ReceiveShadows;

    private void ApplyBakedSolidVertexMode()
    {
        var identity = Matrix4.Identity;
        SetMatrixUniform(solidModelLocation, ref identity);
        SetIntUniform(solidUseInstancingLocation, 0);
        SetIntUniform(solidUseSkinningLocation, 0);
        if (solidUseSurfaceUniformsLocation >= 0)
            GL.Uniform1(solidUseSurfaceUniformsLocation, 0);
    }

    private void ApplyDynamicModelUniform(DynamicMeshDraw draw)
    {
        var world = draw.World;
        SetMatrixUniform(solidModelLocation, ref world);
    }

    private void ApplyDynamicSurfaceUniforms(DynamicMeshDraw draw)
    {
        if (solidUseSurfaceUniformsLocation >= 0)
            GL.Uniform1(solidUseSurfaceUniformsLocation, 1);
        SetVector4Uniform(solidSurfaceColorLocation, draw.Color);
        SetVector4Uniform(solidSurfaceMaterialLocation, draw.Material);
        SetVector4Uniform(solidSurfaceEmissionLocation, draw.Emission);
    }

    private void ApplySkinnedSurfaceUniforms(SkinnedMeshDraw draw)
    {
        if (solidUseSurfaceUniformsLocation >= 0)
            GL.Uniform1(solidUseSurfaceUniformsLocation, 1);
        SetVector4Uniform(solidSurfaceColorLocation, draw.Color);
        SetVector4Uniform(solidSurfaceMaterialLocation, draw.Material);
        SetVector4Uniform(solidSurfaceEmissionLocation, draw.Emission);
    }

    private static void SetMatrixUniform(int location, ref Matrix4 value)
    {
        if (location >= 0)
            GL.UniformMatrix4(location, true, ref value);
    }

    private static void UploadBoneMatrices(int[] locations, System.Numerics.Matrix4x4[] skin)
    {
        int count = Math.Min(Math.Min(locations.Length, skin.Length), MaxGpuBones);
        for (int i = 0; i < count; i++)
        {
            if (locations[i] < 0)
                continue;
            Matrix4 m = ToTkMatrix(skin[i]);
            GL.UniformMatrix4(locations[i], true, ref m);
        }
    }

    private static Matrix4 ToTkMatrix(System.Numerics.Matrix4x4 m) =>
        new(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

    private static void SetIntUniform(int location, int value)
    {
        if (location >= 0)
            GL.Uniform1(location, value);
    }

    private void UploadInstanceMatrices(int start, int count)
    {
        if (count <= 0)
            return;

        EnsureInstanceCapacity(count);
        if (instanceUploadBuffer.Length < count)
            Array.Resize(ref instanceUploadBuffer, count);

        for (int i = 0; i < count; i++)
            instanceUploadBuffer[i] = Matrix4.Transpose(dynamicMeshDraws[start + i].World);

        GL.BindBuffer(BufferTarget.ArrayBuffer, dynamicInstanceBuffer);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * Unsafe.SizeOf<Matrix4>(), instanceUploadBuffer);
    }

    private void EnsureInstanceCapacity(int count)
    {
        int requiredBytes = count * Unsafe.SizeOf<Matrix4>();
        if (dynamicInstanceBufferCapacity >= requiredBytes)
            return;

        dynamicInstanceBufferCapacity = Math.Max(requiredBytes, dynamicInstanceBufferCapacity == 0 ? 65536 : dynamicInstanceBufferCapacity * 2);
        GL.BindBuffer(BufferTarget.ArrayBuffer, dynamicInstanceBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, dynamicInstanceBufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
    }

    private static void SetVector4Uniform(int location, Vector4 value)
    {
        if (location >= 0)
            GL.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }
}
