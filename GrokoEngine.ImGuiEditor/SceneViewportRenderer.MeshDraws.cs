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
        SortShaderGraphMeshDrawsForInstancing();

        if (dynamicMeshDraws.Count <= 1)
        {
            SortSkinnedMeshDrawsForStateReuse();
            return;
        }

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
            cmp = CompareVector4(a.Emission, b.Emission);
            if (cmp != 0) return cmp;
            cmp = a.AlphaClip.CompareTo(b.AlphaClip);
            if (cmp != 0) return cmp;
            return a.AlphaClipThreshold.CompareTo(b.AlphaClipThreshold);
        });

        SortSkinnedMeshDrawsForStateReuse();
    }

    // Agrupa los draws shader-graph idénticos para que queden CONSECUTIVOS: así el pase de
    // color y el de sombras pueden fusionarlos en un DrawArraysInstanced (N árboles con las
    // mismas hojas = 1 draw call, como las mallas con material normal).
    private void SortShaderGraphMeshDrawsForInstancing()
    {
        if (shaderGraphDynamicMeshDraws.Count <= 1)
            return;

        shaderGraphDynamicMeshDraws.Sort(static (a, b) =>
        {
            int cmp = StringComparer.OrdinalIgnoreCase.Compare(a.ShaderGraphPath, b.ShaderGraphPath);
            if (cmp != 0) return cmp;
            cmp = a.Mesh.Vao.CompareTo(b.Mesh.Vao);
            if (cmp != 0) return cmp;
            cmp = a.Start.CompareTo(b.Start);
            if (cmp != 0) return cmp;
            cmp = a.Count.CompareTo(b.Count);
            if (cmp != 0) return cmp;
            // Identidad de los diccionarios de texturas/propiedades: los draws que comparten el
            // mismo .mat comparten la MISMA instancia, así que la identidad basta para agrupar.
            cmp = RuntimeHelpers.GetHashCode(a.ShaderGraphTextures).CompareTo(RuntimeHelpers.GetHashCode(b.ShaderGraphTextures));
            if (cmp != 0) return cmp;
            cmp = RuntimeHelpers.GetHashCode(a.ShaderGraphProperties).CompareTo(RuntimeHelpers.GetHashCode(b.ShaderGraphProperties));
            if (cmp != 0) return cmp;
            cmp = a.ReceiveShadows.CompareTo(b.ReceiveShadows);
            if (cmp != 0) return cmp;
            return a.ShadowsOnly.CompareTo(b.ShadowsOnly);
        });
    }

    private static bool CanInstanceShaderGraphDraws(in ShaderGraphDynamicMeshDraw a, in ShaderGraphDynamicMeshDraw b) =>
        string.Equals(a.ShaderGraphPath, b.ShaderGraphPath, StringComparison.OrdinalIgnoreCase) &&
        ReferenceEquals(a.Mesh, b.Mesh) &&
        a.Start == b.Start &&
        a.Count == b.Count &&
        ReferenceEquals(a.ShaderGraphTextures, b.ShaderGraphTextures) &&
        ReferenceEquals(a.ShaderGraphProperties, b.ShaderGraphProperties) &&
        a.ReceiveShadows == b.ReceiveShadows &&
        a.ShadowsOnly == b.ShadowsOnly;

    private void UploadShaderGraphInstanceMatrices(int start, int count)
    {
        if (count <= 0)
            return;

        EnsureInstanceCapacity(count);
        if (instanceUploadBuffer.Length < count)
            Array.Resize(ref instanceUploadBuffer, count);

        for (int i = 0; i < count; i++)
            instanceUploadBuffer[i] = Matrix4.Transpose(shaderGraphDynamicMeshDraws[start + i].World);

        GL.BindBuffer(BufferTarget.ArrayBuffer, dynamicInstanceBuffer);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * Unsafe.SizeOf<Matrix4>(), instanceUploadBuffer);
    }

    private void SortSkinnedMeshDrawsForStateReuse()
    {
        if (skinnedMeshDraws.Count <= 1)
            return;

        skinnedMeshDraws.Sort(static (a, b) =>
        {
            int cmp = StringComparer.OrdinalIgnoreCase.Compare(a.TexturePath, b.TexturePath);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(a.NormalMapPath, b.NormalMapPath);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(a.RoughnessMapPath, b.RoughnessMapPath);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(a.MetallicMapPath, b.MetallicMapPath);
            if (cmp != 0) return cmp;
            cmp = RuntimeHelpers.GetHashCode(a.Skin).CompareTo(RuntimeHelpers.GetHashCode(b.Skin));
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

    private void DrawDynamicMeshDraws(ref Matrix4 mvp, ShadowInfo shadowInfo, ShadowInfo spotShadowInfo, PointShadowInfo pointShadowInfo, IReadOnlyList<GameObject> objects, Vector3 cameraPosition, bool manageShaderState = true)
    {
        if (manageShaderState)
        {
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            GL.Uniform1(solidTextureLocation, 0);
            ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
            ApplySceneLighting(objects, cameraPosition);
            SetIntUniform(solidRefractivePassLocation, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        bool hasBoundSurface = false;
        string? boundTexture = null;
        string? boundNormal = null;
        string? boundRoughness = null;
        string? boundMetallic = null;
        int boundVao = 0;
        bool? boundReceiveShadows = null;
        Vector4 boundColor = new(float.NaN);
        Vector4 boundMaterial = new(float.NaN);
        Vector4 boundEmission = new(float.NaN);
        int boundInstancing = -1;

        for (int i = 0; i < dynamicMeshDraws.Count;)
        {
            var draw = dynamicMeshDraws[i];
            int end = i + 1;
            while (end < dynamicMeshDraws.Count && CanInstanceTogether(draw, dynamicMeshDraws[end]))
                end++;

            // "Shadows Only" (Cast Shadows = Shadows Only en el Inspector): el objeto sigue en
            // dynamicMeshDraws para que DrawDepthGeometry lo proyecte, pero acá en el pase de
            // color no se dibuja — es invisible, solo se ve su sombra.
            if (draw.ShadowsOnly)
            {
                i = end;
                continue;
            }

            if (boundReceiveShadows != draw.ReceiveShadows)
            {
                ApplyShadowUniforms(
                    draw.ReceiveShadows ? shadowInfo : ShadowInfo.Disabled,
                    draw.ReceiveShadows ? spotShadowInfo : new ShadowInfo(false, Matrix4.Identity, 0f),
                    draw.ReceiveShadows ? pointShadowInfo : new PointShadowInfo(false, Vector3.Zero, 1f, 0f));
                boundReceiveShadows = draw.ReceiveShadows;
            }
            ApplyDynamicSurfaceUniformsIfChanged(draw, ref boundColor, ref boundMaterial, ref boundEmission);
            ApplyAlphaClipUniforms(draw.AlphaClip, draw.AlphaClipThreshold);
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
            TrackDynamicMeshGroup(instanceCount);
            if (instanceCount > 1)
            {
                UploadInstanceMatrices(i, instanceCount);
                if (boundInstancing != 1)
                {
                    SetIntUniform(solidUseInstancingLocation, 1);
                    boundInstancing = 1;
                }
                TrackMainDraw(draw.Count, instanceCount);
                GL.DrawArraysInstanced(PrimitiveType.Triangles, draw.Start, draw.Count, instanceCount);
            }
            else
            {
                ApplyDynamicModelUniform(draw);
                if (boundInstancing != 0)
                {
                    SetIntUniform(solidUseInstancingLocation, 0);
                    boundInstancing = 0;
                }
                TrackMainDraw(draw.Count);
                GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
            }

            i = end;
        }

        if (manageShaderState)
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            ApplyBakedSolidVertexMode();
            GL.UseProgram(0);
        }
    }

    private void DrawSkinnedMeshDraws(ref Matrix4 mvp, ShadowInfo shadowInfo, ShadowInfo spotShadowInfo, PointShadowInfo pointShadowInfo, IReadOnlyList<GameObject> objects, Vector3 cameraPosition, bool manageShaderState = true)
    {
        if (manageShaderState)
        {
            GL.UseProgram(solidShader);
            GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
            GL.Uniform1(solidTextureLocation, 0);
            ApplyShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);
            ApplySceneLighting(objects, cameraPosition);
            SetIntUniform(solidRefractivePassLocation, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        bool hasBoundSurface = false;
        string? boundTexture = null;
        string? boundNormal = null;
        string? boundRoughness = null;
        string? boundMetallic = null;
        int boundVao = 0;
        SetIntUniform(solidUseInstancingLocation, 0);
        SetIntUniform(solidUseSkinningLocation, 1);
        Vector4 boundColor = new(float.NaN);
        Vector4 boundMaterial = new(float.NaN);
        Vector4 boundEmission = new(float.NaN);
        System.Numerics.Matrix4x4[]? boundSkin = null;
        Matrix4 boundWorld = default;
        bool hasBoundWorld = false;
        bool? boundSkinnedReceiveShadows = null;

        foreach (var draw in skinnedMeshDraws)
        {
            // "Shadows Only": el objeto sigue proyectando sombra en el pase de depth, pero acá en
            // el pase de color no se dibuja.
            if (draw.ShadowsOnly)
                continue;

            if (boundSkinnedReceiveShadows != draw.ReceiveShadows)
            {
                ApplyShadowUniforms(
                    draw.ReceiveShadows ? shadowInfo : ShadowInfo.Disabled,
                    draw.ReceiveShadows ? spotShadowInfo : new ShadowInfo(false, Matrix4.Identity, 0f),
                    draw.ReceiveShadows ? pointShadowInfo : new PointShadowInfo(false, Vector3.Zero, 1f, 0f));
                boundSkinnedReceiveShadows = draw.ReceiveShadows;
            }

            var world = draw.World;
            if (!hasBoundWorld || boundWorld != world)
            {
                SetMatrixUniform(solidModelLocation, ref world);
                boundWorld = world;
                hasBoundWorld = true;
            }
            ApplySkinnedSurfaceUniformsIfChanged(draw, ref boundColor, ref boundMaterial, ref boundEmission);
            ApplyAlphaClipUniforms(draw.AlphaClip, draw.AlphaClipThreshold);
            if (!ReferenceEquals(boundSkin, draw.Skin))
            {
                UploadBoneMatrices(solidBoneMatrixLocations, draw.Skin);
                boundSkin = draw.Skin;
            }
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
        if (manageShaderState)
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            ApplyBakedSolidVertexMode();
            GL.UseProgram(0);
        }
    }

    private void DrawRefractiveMeshDraws(ref Matrix4 mvp, ShadowInfo shadowInfo, ShadowInfo spotShadowInfo, PointShadowInfo pointShadowInfo, IReadOnlyList<GameObject> objects, Vector3 cameraPosition, int width, int height)
    {
        if ((refractiveMeshDraws.Count == 0 && refractiveSkinnedMeshDraws.Count == 0) || OpaqueTextureHandle == 0)
            return;

        SortRefractiveDrawsBackToFront(cameraPosition);

        GL.UseProgram(solidShader);
        GL.UniformMatrix4(solidMvpLocation, true, ref mvp);
        ApplyBakedSolidVertexMode();
        ApplyShadowUniforms(ShadowInfo.Disabled, new ShadowInfo(false, Matrix4.Identity, 0f), new PointShadowInfo(false, Vector3.Zero, 1f, 0f));
        ApplySceneLighting(objects, cameraPosition);
        SetIntUniform(solidRefractivePassLocation, 1);
        SetIntUniform(solidOpaqueTextureLocation, 15);
        if (solidScreenTexelLocation >= 0)
            GL.Uniform2(solidScreenTexelLocation, 1f / Math.Max(1, width), 1f / Math.Max(1, height));

        GL.ActiveTexture(TextureUnit.Texture15);
        GL.BindTexture(TextureTarget.Texture2D, OpaqueTextureHandle);
        GL.ActiveTexture(TextureUnit.Texture0);

        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);

        bool hasBoundSurface = false;
        string? boundTexture = null;
        string? boundNormal = null;
        string? boundRoughness = null;
        string? boundMetallic = null;
        int boundVao = 0;
        int boundSkinningMode = -1;
        SetIntUniform(solidUseInstancingLocation, 0);
        SetIntUniform(solidUseSkinningLocation, 0);
        boundSkinningMode = 0;
        System.Numerics.Matrix4x4[]? boundSkin = null;
        Matrix4 boundWorld = default;
        bool hasBoundWorld = false;

        int dynamicIndex = 0;
        int skinnedIndex = 0;
        while (dynamicIndex < refractiveMeshDraws.Count || skinnedIndex < refractiveSkinnedMeshDraws.Count)
        {
            bool useSkinned =
                skinnedIndex < refractiveSkinnedMeshDraws.Count &&
                (dynamicIndex >= refractiveMeshDraws.Count ||
                 RefractiveSortDistanceSq(refractiveSkinnedMeshDraws[skinnedIndex].World, cameraPosition) >
                 RefractiveSortDistanceSq(refractiveMeshDraws[dynamicIndex].World, cameraPosition));

            if (!useSkinned)
            {
                var draw = refractiveMeshDraws[dynamicIndex++];
                if (boundSkinningMode != 0)
                {
                    SetIntUniform(solidUseSkinningLocation, 0);
                    boundSkinningMode = 0;
                    hasBoundWorld = false;
                }

                ApplyDynamicModelUniform(draw);
                ApplyDynamicSurfaceUniforms(draw);
                if (solidRefractionStrengthLocation >= 0)
                    GL.Uniform1(solidRefractionStrengthLocation, draw.RefractionStrength);
                if (solidDistortionStrengthLocation >= 0)
                    GL.Uniform1(solidDistortionStrengthLocation, draw.DistortionStrength);

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
            else
            {
                var draw = refractiveSkinnedMeshDraws[skinnedIndex++];
                if (boundSkinningMode != 1)
                {
                    SetIntUniform(solidUseSkinningLocation, 1);
                    boundSkinningMode = 1;
                    boundSkin = null;
                    hasBoundWorld = false;
                }

                var world = draw.World;
                if (!hasBoundWorld || boundWorld != world)
                {
                    SetMatrixUniform(solidModelLocation, ref world);
                    boundWorld = world;
                    hasBoundWorld = true;
                }

                ApplySkinnedSurfaceUniforms(draw);
                if (solidRefractionStrengthLocation >= 0)
                    GL.Uniform1(solidRefractionStrengthLocation, draw.RefractionStrength);
                if (solidDistortionStrengthLocation >= 0)
                    GL.Uniform1(solidDistortionStrengthLocation, draw.DistortionStrength);

                if (!ReferenceEquals(boundSkin, draw.Skin))
                {
                    UploadBoneMatrices(solidBoneMatrixLocations, draw.Skin);
                    boundSkin = draw.Skin;
                }

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
        }

        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        SetIntUniform(solidUseSkinningLocation, 0);
        SetIntUniform(solidRefractivePassLocation, 0);
        GL.ActiveTexture(TextureUnit.Texture15);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindVertexArray(0);
        ApplyBakedSolidVertexMode();
        GL.UseProgram(0);
    }

    private void SortRefractiveDrawsBackToFront(Vector3 cameraPosition)
    {
        if (refractiveMeshDraws.Count > 1)
        {
            refractiveMeshDraws.Sort((a, b) =>
                RefractiveSortDistanceSq(b.World, cameraPosition).CompareTo(RefractiveSortDistanceSq(a.World, cameraPosition)));
        }

        if (refractiveSkinnedMeshDraws.Count > 1)
        {
            refractiveSkinnedMeshDraws.Sort((a, b) =>
                RefractiveSortDistanceSq(b.World, cameraPosition).CompareTo(RefractiveSortDistanceSq(a.World, cameraPosition)));
        }
    }

    private static float RefractiveSortDistanceSq(Matrix4 world, Vector3 cameraPosition)
    {
        var center = Vector3.TransformPosition(Vector3.Zero, world);
        return (center - cameraPosition).LengthSquared;
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
        Math.Abs(a.Alpha - b.Alpha) < 0.0001f &&
        Math.Abs(a.RefractionStrength - b.RefractionStrength) < 0.0001f &&
        Math.Abs(a.DistortionStrength - b.DistortionStrength) < 0.0001f &&
        a.AlphaClip == b.AlphaClip &&
        Math.Abs(a.AlphaClipThreshold - b.AlphaClipThreshold) < 0.0001f &&
        a.ReceiveShadows == b.ReceiveShadows &&
        a.ShadowsOnly == b.ShadowsOnly;

    private void ApplyBakedSolidVertexMode()
    {
        var identity = Matrix4.Identity;
        SetMatrixUniform(solidModelLocation, ref identity);
        SetIntUniform(solidUseInstancingLocation, 0);
        SetIntUniform(solidUseSkinningLocation, 0);
        if (solidUseSurfaceUniformsLocation >= 0)
            GL.Uniform1(solidUseSurfaceUniformsLocation, 0);
        DisableForwardLightmap();
        ApplyAlphaClipUniforms(false, 0.5f);
    }

    private void ApplyAlphaClipUniforms(bool enabled, float threshold)
    {
        SetIntUniform(solidAlphaClipEnabledLocation, enabled ? 1 : 0);
        if (solidAlphaClipThresholdLocation >= 0)
            GL.Uniform1(solidAlphaClipThresholdLocation, Math.Clamp(threshold, 0f, 1f));
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
        DisableForwardLightmap();
        ApplyAlphaClipUniforms(draw.AlphaClip, draw.AlphaClipThreshold);
    }

    private void ApplyDynamicSurfaceUniformsIfChanged(DynamicMeshDraw draw, ref Vector4 boundColor, ref Vector4 boundMaterial, ref Vector4 boundEmission)
    {
        if (solidUseSurfaceUniformsLocation >= 0)
            GL.Uniform1(solidUseSurfaceUniformsLocation, 1);
        if (boundColor != draw.Color)
        {
            SetVector4Uniform(solidSurfaceColorLocation, draw.Color);
            boundColor = draw.Color;
        }
        if (boundMaterial != draw.Material)
        {
            SetVector4Uniform(solidSurfaceMaterialLocation, draw.Material);
            boundMaterial = draw.Material;
        }
        if (boundEmission != draw.Emission)
        {
            SetVector4Uniform(solidSurfaceEmissionLocation, draw.Emission);
            boundEmission = draw.Emission;
        }
        DisableForwardLightmap();
    }

    private void ApplySkinnedSurfaceUniforms(SkinnedMeshDraw draw)
    {
        if (solidUseSurfaceUniformsLocation >= 0)
            GL.Uniform1(solidUseSurfaceUniformsLocation, 1);
        SetVector4Uniform(solidSurfaceColorLocation, draw.Color);
        SetVector4Uniform(solidSurfaceMaterialLocation, draw.Material);
        SetVector4Uniform(solidSurfaceEmissionLocation, draw.Emission);
        DisableForwardLightmap();
        ApplyAlphaClipUniforms(draw.AlphaClip, draw.AlphaClipThreshold);
    }

    private void ApplySkinnedSurfaceUniformsIfChanged(SkinnedMeshDraw draw, ref Vector4 boundColor, ref Vector4 boundMaterial, ref Vector4 boundEmission)
    {
        if (solidUseSurfaceUniformsLocation >= 0)
            GL.Uniform1(solidUseSurfaceUniformsLocation, 1);
        if (boundColor != draw.Color)
        {
            SetVector4Uniform(solidSurfaceColorLocation, draw.Color);
            boundColor = draw.Color;
        }
        if (boundMaterial != draw.Material)
        {
            SetVector4Uniform(solidSurfaceMaterialLocation, draw.Material);
            boundMaterial = draw.Material;
        }
        if (boundEmission != draw.Emission)
        {
            SetVector4Uniform(solidSurfaceEmissionLocation, draw.Emission);
            boundEmission = draw.Emission;
        }
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
