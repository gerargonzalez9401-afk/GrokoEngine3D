using GrokoEngine;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private void BuildObjectRecursive(GameObject obj, GameObject? selected, in Frustum frustum, Matrix4 parentWorld)
    {
        if (!obj.IsActive)
            return;

        bool sel = ReferenceEquals(obj, selected) || SelectedObjectIds.Contains(obj.EditorId);
        Matrix4 world = GetLocalMatrix(obj) * parentWorld;
        var center = Vector3.TransformPosition(Vector3.Zero, world);

        // Solo actualizar GlobalPosition en el pase dinámico (no en el static build)
        if (!_buildingStatic)
            obj.GlobalPosition = new MiMotor.Mathematics.Vector3(center.X, center.Y, center.Z);

        // ── Enrutar según modo de construcción y flag IsStatic ────
        bool shouldBuildGeometry = _buildingStatic == obj.IsStatic;

        // Mesh Renderer desactivado (como Unity): se oculta la malla, PERO el collider/física sigue
        // funcionando (la física es independiente del render). Terrain no tiene MeshRenderer → no le afecta.
        bool meshRendererHidden = obj.GetComponent<MeshRenderer>() is { Enabled: false };

        if (shouldBuildGeometry && !meshRendererHidden)
        {
            // IMPORTANTE:
            // En el pase de static batch (_buildingStatic == true) NO hacemos frustum culling.
            // Si se cullea aquí, un objeto estático que no esté visible en el momento de reconstruir
            // el batch nunca entra al VBO estático y parece que "desaparece" al mover la cámara.
            bool skipFrustumCullingForStaticBatch = _buildingStatic || !_frustumCullingEnabled;

            if (obj.Type == 1)
            {
                float r = MathF.Max(0.05f, MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleY * obj.ScaleY + obj.ScaleZ * obj.ScaleZ) * 0.5f);
                if (skipFrustumCullingForStaticBatch || frustum.ContainsSphere(center, r))
                    BuildCube(obj, sel, world);
            }
            else if (obj.Type == 2)
            {
                float r = MathF.Max(0.05f, MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleZ * obj.ScaleZ) * 0.5f);
                if (skipFrustumCullingForStaticBatch || frustum.ContainsSphere(center, r))
                    BuildPlane(obj, sel, world);
            }
            else if (obj.Type is 3 or 4 or 5 or 6)
            {
                float r = MathF.Max(0.05f, MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleY * obj.ScaleY + obj.ScaleZ * obj.ScaleZ) * 0.6f);
                if (skipFrustumCullingForStaticBatch || frustum.ContainsSphere(center, r))
                    BuildPrimitive(obj, sel, world);
            }
            else if (obj.GetComponent<Terrain>() is { } terrain)
            {
                var (mesh, meshPath) = GetTerrainMesh(obj, terrain);
                var terrainFilter = new MeshFilter { MeshPath = meshPath, ImportScale = 1f };
                bool frustumVisible = skipFrustumCullingForStaticBatch || FrustumContainsMesh(frustum, obj, terrainFilter, mesh, world);
                if (frustumVisible)
                    BuildTerrainGeometry(obj, terrain, mesh, meshPath, sel, world);
            }
            else if (obj.GetComponent<MeshFilter>() is { } mf && !string.IsNullOrWhiteSpace(mf.MeshPath))
            {
                var mesh = GetParsedMesh(mf.MeshPath);
                bool frustumVisible = skipFrustumCullingForStaticBatch || mesh == null || FrustumContainsMesh(frustum, obj, mf, mesh, world);
                // Alimenta el Culling Mode del Animator con la visibilidad real (frustum de la cámara).
                if (!_buildingStatic && obj.GetComponent<Animator>() is { } cullAnim)
                    cullAnim.IsVisible = frustumVisible;
                if (frustumVisible)
                {
                    // ── Occlusion culling: skip mesh if last query returned invisible ──
                    // The actual query is issued in a GPU pass after opaque geometry is drawn.
                    bool occVisible = true;
                    if (!_buildingStatic && _occlusionCullingEnabled && mesh != null)
                    {
                        // Conservative: visible on first frame or when no result yet
                        occVisible = !_occlusionVisible.TryGetValue(obj.EditorId, out bool lastResult) || lastResult;
                    }

                    if (occVisible && mesh != null)
                        BuildMeshBounds(obj, mf, mesh, sel, world);

                    // Collect objects that need an occlusion query this frame (done after draw)
                    if (!_buildingStatic && _occlusionCullingEnabled && mesh != null && frustumVisible)
                        _pendingOcclusionObjects.Add((obj.EditorId, world, mf, mesh));
                }
            }
        }

        // Iconos solo en pase dinámico (se dibujan siempre, no en batch estático)
        if (!_buildingStatic && sel)
        {
            if (obj.IsCamera || obj.GetComponent<Camera>() != null)
                BuildCameraIcon(obj, sel, world);
            if (obj.GetComponent<DirectionalLight>() != null ||
                obj.GetComponent<PointLight>() != null ||
                obj.GetComponent<SpotLight>() != null ||
                obj.GetComponent<AmbientLight>() != null ||
                obj.GetComponent<AreaLight>() != null ||
                obj.GetComponent<RectangleLight>() != null)
                BuildLightIcon(obj, sel, world);
            if (obj.GetComponent<GrokoEngine.ParticleSystem>() is { } ps)
                BuildParticleShapeGizmo(ps, world);
        }

        foreach (var child in obj.Children)
            BuildObjectRecursive(child, selected, frustum, world);
    }

    private int ComputeStaticBatchSignature(IReadOnlyList<GameObject> objects)
    {
        var hash = new HashCode();
        foreach (var obj in objects)
            AddStaticSignatureRecursive(obj, 17, ref hash);
        return hash.ToHashCode();
    }

    private void AddStaticSignatureRecursive(GameObject obj, int parentSignature, ref HashCode hash)
    {
        int nodeSignature = ComputeNodeSignature(obj, parentSignature);

        if (obj.IsActive && obj.IsStatic)
        {
            hash.Add(nodeSignature);
            hash.Add(obj.EditorId);
            hash.Add(obj.Type);
            hash.Add(obj.IsActive);
            hash.Add(obj.IsStatic);

            if (obj.GetComponent<MeshFilter>() is { } mf)
            {
                hash.Add(NormalizeExistingAssetPath(mf.MeshPath) ?? mf.MeshPath ?? string.Empty);
                hash.Add(mf.ImportScale);
                string? meshPath = NormalizeExistingAssetPath(mf.MeshPath);
                if (meshPath != null)
                    hash.Add(GetStaticSignatureFileTime(meshPath));
            }

            if (obj.GetComponent<Material>() is { } material)
            {
                hash.Add(material.AssetPath ?? string.Empty);
                string? materialAssetPath = NormalizeExistingAssetPath(material.AssetPath);
                if (materialAssetPath != null)
                    hash.Add(GetStaticSignatureFileTime(materialAssetPath));
                hash.Add(material.TexturePath ?? string.Empty);
                hash.Add(material.NormalMapPath ?? string.Empty);
                hash.Add(material.RoughnessMapPath ?? string.Empty);
                hash.Add(material.MetallicMapPath ?? string.Empty);
                hash.Add(material.R); hash.Add(material.G); hash.Add(material.B);
                hash.Add(material.Roughness); hash.Add(material.Metallic);
                hash.Add(material.EmissionR); hash.Add(material.EmissionG); hash.Add(material.EmissionB); hash.Add(material.EmissionIntensity);
                hash.Add(material.ShaderGraphPath ?? string.Empty);
                string? shaderGraphPath = NormalizeExistingAssetPath(material.ShaderGraphPath);
                if (shaderGraphPath != null)
                    hash.Add(GetStaticSignatureFileTime(shaderGraphPath));
                foreach (var kv in material.ShaderGraphProperties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    hash.Add(kv.Key);
                    foreach (var f in kv.Value)
                        hash.Add(f);
                }
            }
        }

        foreach (var child in obj.Children)
            AddStaticSignatureRecursive(child, nodeSignature, ref hash);
    }

    private DateTime GetStaticSignatureFileTime(string path)
    {
        const int refreshEveryFrames = 30;
        if (staticSignatureFileTimeCache.TryGetValue(path, out var cached) &&
            _frameCount - cached.Frame < refreshEveryFrames)
            return cached.WriteTime;

        DateTime writeTime = System.IO.File.Exists(path)
            ? System.IO.File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
        staticSignatureFileTimeCache[path] = (writeTime, _frameCount);
        return writeTime;
    }

    private static int ComputeNodeSignature(GameObject obj, int parentSignature)
    {
        var hash = new HashCode();
        hash.Add(parentSignature);
        hash.Add(obj.EditorId);
        hash.Add(obj.PosX); hash.Add(obj.PosY); hash.Add(obj.PosZ);
        hash.Add(obj.RotX); hash.Add(obj.RotY); hash.Add(obj.RotZ);
        hash.Add(obj.ScaleX); hash.Add(obj.ScaleY); hash.Add(obj.ScaleZ);
        return hash.ToHashCode();
    }

    private static Matrix4 GetLocalMatrix(GameObject obj)
    {
        // Honra UseQuaternionRotation (lo activan la animación esqueletal y el backend Bepu):
        // con él activo se usa el cuaternión exacto; si no, los ángulos Euler (orden Z*X*Y).
        Matrix4 rotation = obj.UseQuaternionRotation
            ? Matrix4.CreateFromQuaternion(new OpenTK.Mathematics.Quaternion(
                obj.transform.Rotation.X, obj.transform.Rotation.Y, obj.transform.Rotation.Z, obj.transform.Rotation.W))
            : Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(obj.RotZ)) *
              Matrix4.CreateRotationX(MathHelper.DegreesToRadians(obj.RotX)) *
              Matrix4.CreateRotationY(MathHelper.DegreesToRadians(obj.RotY));

        return Matrix4.CreateScale(obj.ScaleX, obj.ScaleY, obj.ScaleZ) *
               rotation *
               Matrix4.CreateTranslation(obj.PosX, obj.PosY, obj.PosZ);
    }

    private static Vector3 ComputeWorldPosition(GameObject obj) =>
        Vector3.TransformPosition(Vector3.Zero, ComputeWorldMatrix(obj));

    private static Matrix4 ComputeWorldMatrix(GameObject obj)
    {
        Matrix4 world = GetLocalMatrix(obj);
        var parent = obj.Parent;
        while (parent != null)
        {
            world *= GetLocalMatrix(parent);
            parent = parent.Parent;
        }

        return world;
    }

    /// <summary>
    /// Raycast a nivel de triángulo contra la malla del objeto (en espacio mundial).
    /// Devuelve el índice de sub-malla (-1 si la malla tiene un solo material) golpeado más cerca,
    /// o null si el rayo no impacta la malla.
    /// </summary>
    public int? PickMeshSubmesh(GameObject obj, Vector3 rayOrigin, Vector3 rayDir)
    {
        if (obj.GetComponent<MeshFilter>() is not { } mf || string.IsNullOrWhiteSpace(mf.MeshPath))
            return null;
        var mesh = GetParsedMesh(mf.MeshPath);
        if (mesh == null) return null;

        Matrix4 world = ComputeWorldMatrix(obj);
        float scale = mf.ImportScale <= 0f ? 1f : mf.ImportScale;

        float bestT = float.MaxValue;
        int bestSub = -1;
        bool hitAny = false;

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int pi = t * 9;
            if (mesh.Positions.Length < pi + 9) break;

            var a = Vector3.TransformPosition(new Vector3(mesh.Positions[pi], mesh.Positions[pi + 1], mesh.Positions[pi + 2]) * scale, world);
            var b = Vector3.TransformPosition(new Vector3(mesh.Positions[pi + 3], mesh.Positions[pi + 4], mesh.Positions[pi + 5]) * scale, world);
            var c = Vector3.TransformPosition(new Vector3(mesh.Positions[pi + 6], mesh.Positions[pi + 7], mesh.Positions[pi + 8]) * scale, world);

            if (RayIntersectsTriangle(rayOrigin, rayDir, a, b, c, out float hit) && hit < bestT)
            {
                bestT = hit;
                bestSub = mesh.Submeshes.Count <= 1 ? -1 : FindSubmeshForVertex(mesh, t * 3);
                hitAny = true;
            }
        }

        return hitAny ? bestSub : null;
    }

    private static int FindSubmeshForVertex(ParsedMesh mesh, int vertexIndex)
    {
        for (int s = 0; s < mesh.Submeshes.Count; s++)
        {
            var sub = mesh.Submeshes[s];
            if (vertexIndex >= sub.VertexStart && vertexIndex < sub.VertexStart + sub.VertexCount)
                return s;
        }
        return -1;
    }

    // Möller–Trumbore
    private static bool RayIntersectsTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        var edge1 = b - a;
        var edge2 = c - a;
        var pVec = Vector3.Cross(dir, edge2);
        float det = Vector3.Dot(edge1, pVec);
        if (MathF.Abs(det) < 1e-8f) return false;

        float invDet = 1f / det;
        var tVec = origin - a;
        float u = Vector3.Dot(tVec, pVec) * invDet;
        if (u < 0f || u > 1f) return false;

        var qVec = Vector3.Cross(tVec, edge1);
        float v = Vector3.Dot(dir, qVec) * invDet;
        if (v < 0f || u + v > 1f) return false;

        float candidate = Vector3.Dot(edge2, qVec) * invDet;
        if (candidate < 0.0001f) return false;
        t = candidate;
        return true;
    }

    private static bool FrustumContainsMesh(in Frustum frustum, GameObject obj, MeshFilter mf, ParsedMesh mesh, Matrix4 world)
    {
        float s = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
        var minB = new Vector3(mesh.BoundsMin.X * s, mesh.BoundsMin.Y * s, mesh.BoundsMin.Z * s);
        var maxB = new Vector3(mesh.BoundsMax.X * s, mesh.BoundsMax.Y * s, mesh.BoundsMax.Z * s);
        var localCenter = (minB + maxB) * 0.5f;
        var worldCenter = Vector3.TransformPosition(localCenter, world);
        float diag = (maxB - minB).Length * 0.5f;
        float maxScale = MathF.Max(MathF.Abs(obj.ScaleX), MathF.Max(MathF.Abs(obj.ScaleY), MathF.Abs(obj.ScaleZ)));
        // Margen extra para evitar que la malla se "corte" al acercar mucho la cámara
        // (la esfera de bounds puede quedar por detrás de algún plano del frustum por error de precisión).
        float radius = diag * maxScale * 1.25f + 0.05f;
        return frustum.ContainsSphere(worldCenter, radius);
    }
}
