using GrokoEngine;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private sealed class HierarchyLodGpuBatchTemplate
    {
        public readonly List<HierarchyLodGpuBatchPart>[] Levels =
        {
            new(), new(), new(), new()
        };

        public bool IsValid;
    }

    private readonly record struct HierarchyLodGpuBatchPart(
        GameObject Object,
        MeshFilter Filter,
        ParsedMesh Mesh,
        string MeshPath,
        int SubmeshIndex,
        Matrix4 LocalToRoot);

    private sealed class HierarchyLodGpuDrawCacheEntry
    {
        public Matrix4 RootWorld;
        public int Level;
        public long ExpiresAt;
        public DynamicMeshDraw[] Dynamic = Array.Empty<DynamicMeshDraw>();
        public DynamicMeshDraw[] Refractive = Array.Empty<DynamicMeshDraw>();
        public ShaderGraphDynamicMeshDraw[] ShaderGraph = Array.Empty<ShaderGraphDynamicMeshDraw>();
    }

    private readonly Dictionary<GameObject, HierarchyLodGpuBatchTemplate> _hierarchyLodGpuBatchCache = new();
    private readonly Dictionary<GameObject, HierarchyLodGpuDrawCacheEntry> _hierarchyLodGpuDrawCache = new();

    private bool TryQueueHierarchyLodGpuBatch(
        GameObject root,
        in HierarchyLodContext context,
        in Frustum frustum,
        in Matrix4 rootWorld)
    {
        if (_buildingStatic)
            return true;

        if (context.Culled)
            return true;

        if (PreviewMaterialObjectId != null)
            return false;

        if (_frustumCullingEnabled && !frustum.ContainsSphere(context.WorldCenter, context.WorldRadius * 1.25f + 0.05f))
        {
            _statsHierarchyLodGpuFrustumCulled++;
            return true;
        }

        var template = GetHierarchyLodGpuBatchTemplate(root);
        if (!template.IsValid)
            return false;

        int level = Math.Clamp(context.SelectedLevel, 0, template.Levels.Length - 1);
        var parts = template.Levels[level];
        if (parts.Count == 0)
            return true;
        _statsHierarchyLodGpuParts += parts.Count;

        long now = Environment.TickCount64;
        if (_hierarchyLodGpuDrawCache.TryGetValue(root, out var cached) &&
            now < cached.ExpiresAt &&
            cached.Level == level &&
            cached.RootWorld == rootWorld)
        {
            _statsHierarchyLodGpuReplays++;
            ReplayHierarchyLodGpuDrawCache(cached);
            return true;
        }
        _statsHierarchyLodGpuMisses++;

        int dyn0 = dynamicMeshDraws.Count;
        int refr0 = refractiveMeshDraws.Count;
        int sg0 = shaderGraphDynamicMeshDraws.Count;

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Filter.SubmeshIndex < 0 || part.Filter.SubmeshIndex >= part.Mesh.Submeshes.Count)
            {
                RollbackHierarchyLodGpuDraws(dyn0, refr0, sg0);
                return false;
            }

            var meshRenderer = part.Object.GetComponent<MeshRenderer>();
            if (meshRenderer is { Enabled: false })
                continue;

            Matrix4 world = part.LocalToRoot * rootWorld;
            var sub = part.Mesh.Submeshes[part.SubmeshIndex];
            string? slotPath = part.SubmeshIndex < part.Filter.MaterialSlots.Count
                ? part.Filter.MaterialSlots[part.SubmeshIndex]
                : null;

            var surf = GetSubmeshSurface(slotPath, sub, new Vector4(0.62f, 0.72f, 1f, 1f));
            var shadowMode = ResolveMeshRendererShadowMode(meshRenderer);
            if (!TryQueueDynamicMeshDraw(
                    part.Mesh,
                    world,
                    part.Filter.ImportScale <= 0f ? 1f : part.Filter.ImportScale,
                    surf.Color,
                    part.MeshPath,
                    surf.Material,
                    surf.Emission,
                    sub.VertexStart,
                    sub.VertexCount,
                    surf.TexturePath,
                    surf.Maps.NormalMapPath,
                    surf.Maps.RoughnessMapPath,
                    surf.Maps.MetallicMapPath,
                    surf.ShaderGraphPath,
                    surf.ShaderGraphProperties,
                    surf.ShaderGraphTextures,
                    transparent: surf.Transparency.IsTransparent,
                    alpha: surf.Transparency.Alpha,
                    refractionStrength: surf.Transparency.RefractionStrength,
                    distortionStrength: surf.Transparency.DistortionStrength,
                    alphaClip: surf.Transparency.IsCutout,
                    alphaClipThreshold: surf.Transparency.AlphaClipThreshold,
                    castShadows: shadowMode.Cast,
                    receiveShadows: shadowMode.Receive,
                    twoSidedShadows: shadowMode.TwoSided,
                    shadowsOnly: shadowMode.ShadowsOnly))
            {
                RollbackHierarchyLodGpuDraws(dyn0, refr0, sg0);
                return false;
            }
        }

        _hierarchyLodGpuDrawCache[root] = new HierarchyLodGpuDrawCacheEntry
        {
            RootWorld = rootWorld,
            Level = level,
            ExpiresAt = now + 60000 + (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(root) & 1023),
            Dynamic = CopyRange(dynamicMeshDraws, dyn0),
            Refractive = CopyRange(refractiveMeshDraws, refr0),
            ShaderGraph = CopyRange(shaderGraphDynamicMeshDraws, sg0)
        };

        return true;
    }

    private void ReplayHierarchyLodGpuDrawCache(HierarchyLodGpuDrawCacheEntry cached)
    {
        for (int i = 0; i < cached.Dynamic.Length; i++)
        {
            dynamicMeshDraws.Add(cached.Dynamic[i]);
            _statsOpaqueDynamicMeshes++;
        }

        for (int i = 0; i < cached.Refractive.Length; i++)
        {
            refractiveMeshDraws.Add(cached.Refractive[i]);
            _statsTransparentDynamicMeshes++;
        }

        for (int i = 0; i < cached.ShaderGraph.Length; i++)
            shaderGraphDynamicMeshDraws.Add(cached.ShaderGraph[i]);
    }

    private void RollbackHierarchyLodGpuDraws(int dynStart, int refrStart, int shaderGraphStart)
    {
        if (dynamicMeshDraws.Count > dynStart)
            dynamicMeshDraws.RemoveRange(dynStart, dynamicMeshDraws.Count - dynStart);
        if (refractiveMeshDraws.Count > refrStart)
            refractiveMeshDraws.RemoveRange(refrStart, refractiveMeshDraws.Count - refrStart);
        if (shaderGraphDynamicMeshDraws.Count > shaderGraphStart)
            shaderGraphDynamicMeshDraws.RemoveRange(shaderGraphStart, shaderGraphDynamicMeshDraws.Count - shaderGraphStart);
    }

    private HierarchyLodGpuBatchTemplate GetHierarchyLodGpuBatchTemplate(GameObject root)
    {
        if (_hierarchyLodGpuBatchCache.TryGetValue(root, out var cached))
            return cached;

        cached = new HierarchyLodGpuBatchTemplate();
        _hierarchyLodGpuBatchCache[root] = cached;

        for (int i = 0; i < root.Children.Count; i++)
        {
            var child = root.Children[i];
            if (!TryGetHierarchyLodLevel(child.Name, out int level))
                continue;

            level = Math.Clamp(level, 0, cached.Levels.Length - 1);
            CollectHierarchyLodGpuBatchParts(child, Matrix4.Identity, level, cached);
        }

        cached.IsValid = true;
        return cached;
    }

    private void CollectHierarchyLodGpuBatchParts(
        GameObject obj,
        in Matrix4 parentLocal,
        int level,
        HierarchyLodGpuBatchTemplate template)
    {
        if (!obj.IsActive)
            return;

        Matrix4 local = GetLocalMatrix(obj) * parentLocal;

        if (obj.GetComponent<MeshFilter>() is { } mf &&
            !string.IsNullOrWhiteSpace(mf.MeshPath) &&
            mf.SubmeshIndex >= 0 &&
            GetParsedMesh(mf.MeshPath) is { } mesh)
        {
            string meshPath = NormalizeExistingAssetPath(mf.MeshPath) ?? mf.MeshPath;
            template.Levels[level].Add(new HierarchyLodGpuBatchPart(
                obj,
                mf,
                mesh,
                meshPath,
                mf.SubmeshIndex,
                local));
        }

        for (int i = 0; i < obj.Children.Count; i++)
            CollectHierarchyLodGpuBatchParts(obj.Children[i], local, level, template);
    }
}
