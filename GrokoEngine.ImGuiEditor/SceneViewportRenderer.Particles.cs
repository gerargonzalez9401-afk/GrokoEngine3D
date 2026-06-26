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
private void TrackParticleDraw(int vertexCount)
    {
        _statsDrawCalls++;
        _statsParticleDrawCalls++;
        _statsTriangles += Math.Max(0, vertexCount / 3);
    }

private void DrawParticles(IReadOnlyList<GameObject> objects, ref Matrix4 view, ref Matrix4 mvp, Vector3 cameraPosition)
    {
        var camRight = new Vector3(view.M11, view.M21, view.M31);
        var camUp = new Vector3(view.M12, view.M22, view.M32);

        _particleVerts.Clear();
        _particleRanges.Clear();
        CollectParticleVerts(objects, camRight, camUp, cameraPosition);

        // ── Trails ────────────────────────────────────────────────
        _trailVerts.Clear();
        UpdateAndRenderTrails(objects, camRight, camUp);

        if (_particleVerts.Count == 0 && _trailVerts.Count == 0) return;

        bool needsSoftParticles = _particleRanges.Any(r => r.Soft);
        if (needsSoftParticles)
            CaptureDepthBuffer();
        int w = needsSoftParticles ? _depthFboWidth : 0;
        int h = needsSoftParticles ? _depthFboHeight : 0;

        // Upload
        if (_particleVerts.Count > 0)
        {
            int required = _particleVerts.Count * Unsafe.SizeOf<ParticleVertex>();
            if (required > _particleBufferCapacity)
            {
                _particleBufferCapacity = Math.Max(required, _particleBufferCapacity == 0 ? 65536 : _particleBufferCapacity * 2);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, _particleBufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }
            var arr = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_particleVerts));
            GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, arr.Length, ref arr[0]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        // Render con alpha blending
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One); // additive (fuego/magia)
        GL.DepthMask(false);

        // ── Dibujar trails primero (debajo de las partículas) ─────
        if (_trailVerts.Count > 0)
        {
            ApplyParticleBlendMode(ParticleBlendMode.Alpha);
            int trailBytes = _trailVerts.Count * Unsafe.SizeOf<ParticleVertex>();
            if (trailBytes > _trailBufferCap)
            {
                _trailBufferCap = Math.Max(trailBytes, _trailBufferCap == 0 ? 32768 : _trailBufferCap * 2);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _trailVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, _trailBufferCap, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }
            var trailSpan = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_trailVerts));
            GL.BindBuffer(BufferTarget.ArrayBuffer, _trailVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, trailSpan.Length, ref trailSpan[0]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            GL.UseProgram(_particleShader);
            GL.UniformMatrix4(_particleMvpLoc, true, ref mvp);
            GL.Uniform3(_particleRightLoc, 0f, 0f, 0f); // trails ya tienen posiciones absolutas
            GL.Uniform3(_particleUpLoc, 0f, 0f, 0f);
            GL.Uniform1(_particleTexLoc, 0);
            GL.Uniform1(_particleHasTexLoc, 0);
            GL.Uniform1(_particleSoftLoc, 0); // trails no tienen soft
            GL.BindVertexArray(_trailVao);
            TrackParticleDraw(_trailVerts.Count);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _trailVerts.Count);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        // ── Dibujar partículas con soft particles ─────────────────
        if (_particleVerts.Count > 0)
        {
            GL.UseProgram(_particleShader);
            GL.UniformMatrix4(_particleMvpLoc, true, ref mvp);
            GL.Uniform3(_particleRightLoc, camRight.X, camRight.Y, camRight.Z);
            GL.Uniform3(_particleUpLoc, camUp.X, camUp.Y, camUp.Z);
            GL.Uniform1(_particleTexLoc, 0);
            GL.Uniform2(_particleScreenSizeLoc, (float)w, (float)h);
            GL.Uniform1(_particleSoftRangeLoc, 0.15f);

            // Bind depth texture en unit 1
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _depthTex);
            GL.Uniform1(_particleDepthTexLoc, 1);
            GL.Uniform1(_particleSoftLoc, w > 0 && h > 0 ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture0);

            GL.BindVertexArray(_particleVao);

            _particleRanges.Sort(static (a, b) =>
            {
                int cmp = a.Queue.CompareTo(b.Queue);
                if (cmp != 0) return cmp;
                cmp = a.SortingLayer.CompareTo(b.SortingLayer);
                if (cmp != 0) return cmp;
                cmp = a.OrderInLayer.CompareTo(b.OrderInLayer);
                if (cmp != 0) return cmp;
                return a.SortingFudge.CompareTo(b.SortingFudge);
            });

            foreach (var range in _particleRanges)
            {
                ApplyParticleBlendMode(range.Blend);
                GL.Uniform1(_particleSoftRangeLoc, range.SoftRange);
                GL.Uniform1(_particleSoftLoc, range.Soft && w > 0 && h > 0 ? 1 : 0);
                string? texPath = range.TexturePath;
                bool hasTex = !string.IsNullOrWhiteSpace(texPath) && System.IO.File.Exists(texPath);
                int texId = hasTex ? GetTexture(texPath!) : 0;
                GL.Uniform1(_particleHasTexLoc, hasTex ? 1 : 0);
                GL.BindTexture(TextureTarget.Texture2D, texId);
                TrackParticleDraw(range.Count);
                GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
            }

            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        GL.DepthMask(true);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.Blend);
    }

private static void ApplyParticleBlendMode(ParticleBlendMode mode)
    {
        switch (mode)
        {
            case ParticleBlendMode.Alpha:
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
            case ParticleBlendMode.Multiply:
                GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha);
                break;
            default:
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;
        }
    }

private void CollectParticleVerts(IReadOnlyList<GameObject> objects, Vector3 right, Vector3 up, Vector3 cameraPosition)
    {
        // Primero procesamos sub-emisores de todos los sistemas
        foreach (var obj in objects)
            ProcessSubEmitters(obj, objects);

        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;   // GameObject (y su rama) inactivo: sin partículas, como Unity
            var ps = obj.GetComponent<GrokoEngine.ParticleSystem>();
            if (ps == null || !ps.Enabled || ps.Particles.Count == 0) { CollectParticleVerts(obj.Children, right, up, cameraPosition); continue; }
            if (!ps.RendererModuleEnabled) { CollectParticleVerts(obj.Children, right, up, cameraPosition); continue; }
            if (ps.RenderMode is ParticleRenderMode.Mesh or ParticleRenderMode.Prefab) { CollectParticleVerts(obj.Children, right, up, cameraPosition); continue; }

            bool isLocal = ps.SimulationSpace == ParticleSimulationSpace.Local;
            var objPos = isLocal ? new MiMotor.Mathematics.Vector3(obj.PosX, obj.PosY, obj.PosZ) : MiMotor.Mathematics.Vector3.Zero;
            var objRot = isLocal ? new MiMotor.Mathematics.Vector3(obj.RotX, obj.RotY, obj.RotZ) : MiMotor.Mathematics.Vector3.Zero;
            Vector3 renderRight = right;
            Vector3 renderUp = up;
            if (ps.RenderMode == ParticleRenderMode.HorizontalBillboard)
            {
                renderRight = Vector3.UnitX;
                renderUp = Vector3.UnitZ;
            }
            else if (ps.RenderMode == ParticleRenderMode.VerticalBillboard)
            {
                renderUp = Vector3.UnitY;
            }

            // ── LOD: reducir efectivamente el número a renderizar por distancia ──
            var camPos = new MiMotor.Mathematics.Vector3(cameraPosition.X, cameraPosition.Y, cameraPosition.Z);
            float lodDist = Vector3Dist(new MiMotor.Mathematics.Vector3(obj.PosX, obj.PosY, obj.PosZ), camPos);
            float lodRatio = ps.LODDistanceMax > ps.LODDistance
                ? 1f - Math.Clamp((lodDist - ps.LODDistance) / (ps.LODDistanceMax - ps.LODDistance), 0f, 1f)
                : lodDist > ps.LODDistanceMax ? 0f : 1f;
            int maxRender = ApplyRendererMaxParticles(ps, (int)(ps.Particles.Count * lodRatio));
            if (maxRender == 0) { CollectParticleVerts(obj.Children, right, up, cameraPosition); continue; }

            // ── Ordenar back-to-front ──────────────────────────────
            _sortBuf.Clear();
            int renderCount = 0;
            foreach (var p in ps.Particles)
            {
                if (++renderCount > maxRender) break;
                var wp = GetWorldPos(p.Position, isLocal, objPos, objRot);
                float dx = wp.X - cameraPosition.X;
                float dy = wp.Y - cameraPosition.Y;
                float dz = wp.Z - cameraPosition.Z;
                _sortBuf.Add((p, dx * dx + dy * dy + dz * dz));
            }
            if (ps.SortParticles && ps.SortMode != ParticleSortMode.None)
            {
                _sortBuf.Sort(ps.SortMode switch
                {
                    ParticleSortMode.OldestInFront => (a, b) => a.P.Age.CompareTo(b.P.Age),
                    ParticleSortMode.YoungestInFront => (a, b) => b.P.Age.CompareTo(a.P.Age),
                    _ => (a, b) => b.Dist.CompareTo(a.Dist)
                });
            }

            bool useGradient = ps.ColorKeyCount >= 2;
            var surface = GetParticleRenderSurface(ps);
            float hdrIntensity = Math.Max(0f, ps.HdrIntensity);
            int start = _particleVerts.Count;

            foreach (var (p, _) in _sortBuf)
            {
                var wp = GetWorldPos(p.Position, isLocal, objPos, objRot);

                // Gradiente de color multi-parada
                Vector4 color;
                if (!ps.ColorOverLifetimeModuleEnabled)
                    color = new Vector4(ps.ColorStartR, ps.ColorStartG, ps.ColorStartB, ps.ColorStartA);
                else if (useGradient)
                {
                    var (r, g, b, a) = ps.SampleGradient(p.NormalizedAge);
                    color = new Vector4(r, g, b, a);
                }
                else
                    color = new Vector4(p.CurrentR, p.CurrentG, p.CurrentB, p.CurrentA);

                color = ApplyParticleColorLook(ps, p, color, surface, hdrIntensity);

                float sizeMultiplier = ps.SizeOverLifetimeModuleEnabled
                    ? GrokoEngine.ParticleSystem.EvaluateParticleCurve(ps.SizeOverLifetimeCurve, p.NormalizedAge, ps.SizeCurveMid, ps.SizeCurveMidValue)
                    : 1f;

                if (ps.StretchedBillboard || ps.RenderMode == ParticleRenderMode.StretchedBillboard)
                    AddStretchedQuad(p, wp, renderRight, renderUp, color, ps.StretchSpeedScale, ps.StretchLengthScale,
                                     ps.SheetColumns, ps.SheetRows, ps.SheetFrameRate, sizeMultiplier,
                                     ps.AllowRoll, ps.FlipU, ps.FlipV, ps.PivotX, ps.PivotY);
                else
                    AddParticleQuad(p, wp, renderRight, renderUp, ps.SheetColumns, ps.SheetRows, ps.SheetFrameRate,
                                    color, sizeMultiplier, ps.AllowRoll, ps.FlipU, ps.FlipV, ps.PivotX, ps.PivotY);
            }

            int cnt = _particleVerts.Count - start;
            if (cnt > 0)
                _particleRanges.Add(new ParticleRenderRange(
                    surface.TexturePath,
                    start,
                    cnt,
                    ps.BlendMode,
                    ps.SoftParticles,
                    Math.Max(0.001f, ps.SoftParticleRange),
                    EffectiveParticleRenderQueue(ps),
                    ps.SortingLayer,
                    ps.OrderInLayer,
                    ps.SortingFudge));

            CollectParticleVerts(obj.Children, right, up, cameraPosition);
        }
    }

private void CollectParticleMeshDraws(IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive)
                continue;

            var ps = obj.GetComponent<GrokoEngine.ParticleSystem>();
            if (ps is { Enabled: true, RendererModuleEnabled: true } &&
                ps.RenderMode is ParticleRenderMode.Mesh or ParticleRenderMode.Prefab &&
                ps.Particles.Count > 0)
            {
                QueueParticleMeshDraws(obj, ps, cameraPosition);
            }

            CollectParticleMeshDraws(obj.Children, cameraPosition);
        }
    }

private void QueueParticleMeshDraws(GameObject obj, GrokoEngine.ParticleSystem ps, Vector3 cameraPosition)
    {
        if (!TryResolveParticleVisualMesh(ps, out var visual))
            return;

        string meshPath = visual.MeshPath;
        var mesh = GetParsedMesh(meshPath);
        if (mesh == null || mesh.TriangleCount <= 0)
            return;

        bool isLocal = ps.SimulationSpace == ParticleSimulationSpace.Local;
        var objPos = isLocal ? new MiMotor.Mathematics.Vector3(obj.PosX, obj.PosY, obj.PosZ) : MiMotor.Mathematics.Vector3.Zero;
        var objRot = isLocal ? new MiMotor.Mathematics.Vector3(obj.RotX, obj.RotY, obj.RotZ) : MiMotor.Mathematics.Vector3.Zero;
        Matrix4 emitterRotation = isLocal ? BuildEulerRotation(obj.RotX, obj.RotY, obj.RotZ) : Matrix4.Identity;

        bool useGradient = ps.ColorKeyCount >= 2;
        var surface = visual.Surface;
        float hdrIntensity = Math.Max(0f, ps.HdrIntensity);
        float meshScale = Math.Max(0.001f, ps.ParticleMeshScale);
        Vector4 material = visual.Material;
        Vector4 emission = visual.Emission;

        int maxRender = ApplyRendererMaxParticles(ps, ps.Particles.Count);
        int queued = 0;
        foreach (var p in ps.Particles)
        {
            if (queued++ >= maxRender)
                break;

            var wp = GetWorldPos(p.Position, isLocal, objPos, objRot);

            Vector4 color;
            if (!ps.ColorOverLifetimeModuleEnabled)
                color = new Vector4(ps.ColorStartR, ps.ColorStartG, ps.ColorStartB, ps.ColorStartA);
            else if (useGradient)
            {
                var (r, g, b, a) = ps.SampleGradient(p.NormalizedAge);
                color = new Vector4(r, g, b, a);
            }
            else
                color = new Vector4(p.CurrentR, p.CurrentG, p.CurrentB, p.CurrentA);

            color = ApplyParticleColorLook(ps, p, color, surface, hdrIntensity);

            float sizeMultiplier = ps.SizeOverLifetimeModuleEnabled
                ? GrokoEngine.ParticleSystem.EvaluateParticleCurve(ps.SizeOverLifetimeCurve, p.NormalizedAge, ps.SizeCurveMid, ps.SizeCurveMidValue)
                : 1f;
            float scale = Math.Max(0.0001f, p.CurrentSize * sizeMultiplier * meshScale);

            Matrix4 orientation = ResolveParticleMeshOrientation(ps, p, wp, cameraPosition, emitterRotation);
            Matrix4 world =
                Matrix4.CreateScale(scale) *
                orientation *
                Matrix4.CreateTranslation(wp.X, wp.Y, wp.Z);

            TryQueueDynamicMeshDraw(
                mesh,
                world,
                1f,
                color,
                meshPath,
                material,
                emission,
                0,
                mesh.Positions.Length / 3,
                surface.TexturePath,
                visual.Maps.NormalMapPath,
                visual.Maps.RoughnessMapPath,
                visual.Maps.MetallicMapPath,
                null,
                null,
                null,
                ps.ParticleCastShadows,
                ps.ParticleReceiveShadows);
        }
    }

private static Matrix4 ResolveParticleMeshOrientation(
    GrokoEngine.ParticleSystem ps,
    Particle p,
    MiMotor.Mathematics.Vector3 worldPosition,
    Vector3 cameraPosition,
    Matrix4 emitterRotation)
    {
        Matrix4 particleRoll = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(p.Rotation));

        return ps.RenderAlignment switch
        {
            ParticleRenderAlignment.Local => particleRoll * emitterRotation,
            ParticleRenderAlignment.View => YawRotationToward(
                new Vector3(cameraPosition.X - worldPosition.X, 0f, cameraPosition.Z - worldPosition.Z),
                particleRoll),
            ParticleRenderAlignment.Velocity => YawRotationToward(
                new Vector3(p.Velocity.X, 0f, p.Velocity.Z),
                particleRoll),
            _ => particleRoll
        };
    }

private static Matrix4 YawRotationToward(Vector3 direction, Matrix4 fallback)
    {
        if (direction.LengthSquared < 0.000001f)
            return fallback;

        direction.Normalize();
        float yaw = MathF.Atan2(direction.X, direction.Z);
        return Matrix4.CreateRotationY(yaw);
    }

private bool TryResolveParticleVisualMesh(GrokoEngine.ParticleSystem ps, out ParticleVisualMesh visual)
    {
        visual = default;

        if (ps.RenderMode == ParticleRenderMode.Mesh)
        {
            string meshPath = NormalizeExistingAssetPath(ps.ParticleMeshPath) ?? ps.ParticleMeshPath;
            if (string.IsNullOrWhiteSpace(meshPath) || !ObjLoader.IsSupportedMesh(meshPath))
                return false;

            var surface = GetParticleRenderSurface(ps);
            visual = new ParticleVisualMesh(
                meshPath,
                surface,
                new SurfaceMaps(null, null, null),
                new Vector4(0f, 0.5f, 0f, 0f),
                Vector4.Zero);
            return true;
        }

        if (ps.RenderMode == ParticleRenderMode.Prefab)
        {
            string prefabPath = NormalizeExistingAssetPath(ps.ParticlePrefabPath) ?? ps.ParticlePrefabPath;
            if (string.IsNullOrWhiteSpace(prefabPath) || !System.IO.File.Exists(prefabPath))
                return false;

            return TryGetPrefabParticleVisual(prefabPath, ps, out visual);
        }

        return false;
    }

private bool TryGetPrefabParticleVisual(string prefabPath, GrokoEngine.ParticleSystem ps, out ParticleVisualMesh visual)
    {
        visual = default;
        DateTime writeTime = System.IO.File.GetLastWriteTimeUtc(prefabPath);
        if (_particlePrefabVisualCache.TryGetValue(prefabPath, out var cached) && cached.WriteTime == writeTime)
        {
            visual = cached.Visual.WithFallbackSurface(GetParticleRenderSurface(ps));
            return !string.IsNullOrWhiteSpace(visual.MeshPath);
        }

        try
        {
            var loaded = SceneSerializer.LoadPrefab(prefabPath, new PhysicsEngine(), new ScriptCompiler(System.IO.Path.GetDirectoryName(prefabPath) ?? string.Empty));
            if (!TryFindFirstPrefabMesh(loaded, out var meshFilter, out var meshObject))
                return false;

            string meshPath = NormalizeExistingAssetPath(meshFilter.MeshPath) ?? meshFilter.MeshPath;
            if (string.IsNullOrWhiteSpace(meshPath) || !ObjLoader.IsSupportedMesh(meshPath))
                return false;

            var tint = GetObjectColor(meshObject, new Vector4(1f, 1f, 1f, 1f));
            string? texturePath = GetObjectTexturePath(meshObject);
            var (material, emission) = GetObjectSurface(meshObject);
            var maps = GetObjectSurfaceMaps(meshObject);

            visual = new ParticleVisualMesh(
                meshPath,
                new ParticleRenderSurface(tint, NormalizeTexturePath(texturePath)),
                maps,
                material,
                emission);
            _particlePrefabVisualCache[prefabPath] = (writeTime, visual);
            visual = visual.WithFallbackSurface(GetParticleRenderSurface(ps));
            return true;
        }
        catch (Exception ex)
        {
            LogAssetWarning($"Particle prefab visual failed: {System.IO.Path.GetFileName(prefabPath)} ({ex.Message})");
            return false;
        }
    }

private static bool TryFindFirstPrefabMesh(GameObject obj, out MeshFilter meshFilter, out GameObject meshObject)
    {
        if (obj.GetComponent<MeshFilter>() is { } mf && !string.IsNullOrWhiteSpace(mf.MeshPath))
        {
            meshFilter = mf;
            meshObject = obj;
            return true;
        }

        foreach (var child in obj.Children)
        {
            if (TryFindFirstPrefabMesh(child, out meshFilter, out meshObject))
                return true;
        }

        meshFilter = null!;
        meshObject = null!;
        return false;
    }

private static Matrix4 BuildEulerRotation(float rotX, float rotY, float rotZ)
    {
        return Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotZ)) *
               Matrix4.CreateRotationX(MathHelper.DegreesToRadians(rotX)) *
               Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotY));
    }

private static MiMotor.Mathematics.Vector3 GetWorldPos(
        MiMotor.Mathematics.Vector3 localPos, bool isLocal,
        MiMotor.Mathematics.Vector3 objPos, MiMotor.Mathematics.Vector3 objRot)
        => isLocal ? objPos + RotateVec3(localPos, objRot) : localPos;

private static float Vector3Dist(MiMotor.Mathematics.Vector3 a, MiMotor.Mathematics.Vector3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

private ParticleRenderSurface GetParticleRenderSurface(GrokoEngine.ParticleSystem ps)
    {
        string? texturePath = string.IsNullOrWhiteSpace(ps.TexturePath) ? null : ps.TexturePath;
        var tint = new Vector4(1f, 1f, 1f, 1f);

        if (TryGetParticleMaterial(ps.MaterialPath, out var material))
        {
            tint = new Vector4(
                Math.Clamp(material.R, 0f, 1f),
                Math.Clamp(material.G, 0f, 1f),
                Math.Clamp(material.B, 0f, 1f),
                1f);

            string albedo = MaterialAsset.GetAlbedo(material);
            if (!string.IsNullOrWhiteSpace(albedo))
                texturePath = albedo;
        }

        return new ParticleRenderSurface(tint, NormalizeTexturePath(texturePath));
    }

private static Vector4 ApplyParticleColorLook(GrokoEngine.ParticleSystem ps, Particle p, Vector4 color, ParticleRenderSurface surface, float hdrIntensity)
    {
        float r = color.X * surface.Tint.X;
        float g = color.Y * surface.Tint.Y;
        float b = color.Z * surface.Tint.Z;
        float a = Math.Clamp(color.W * surface.Tint.W, 0f, 1f);

        float lum = r * 0.2126f + g * 0.7152f + b * 0.0722f;
        float saturation = Math.Clamp(ps.ColorSaturation, 0f, 4f);
        r = lum + (r - lum) * saturation;
        g = lum + (g - lum) * saturation;
        b = lum + (b - lum) * saturation;

        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float colorfulness = max <= 0.0001f ? 0f : Math.Clamp((max - min) / max, 0f, 1f);
        float vibrance = Math.Clamp(ps.ColorVibrance, 0f, 4f);
        float vibranceBoost = 1f + vibrance * (1f - colorfulness) * 0.65f;
        r = lum + (r - lum) * vibranceBoost;
        g = lum + (g - lum) * vibranceBoost;
        b = lum + (b - lum) * vibranceBoost;

        float variation = Math.Clamp(ps.ColorVariation, 0f, 1f);
        if (variation > 0f)
        {
            float random = ParticleStableRandom01(p.Id);
            float intensity = 1f + (random * 2f - 1f) * variation * 0.28f;
            r *= intensity;
            g *= intensity;
            b *= intensity;
        }

        float alphaPower = Math.Clamp(ps.AlphaPower, 0.05f, 4f);
        a = MathF.Pow(a, alphaPower);

        return new Vector4(
            Math.Max(0f, r) * hdrIntensity,
            Math.Max(0f, g) * hdrIntensity,
            Math.Max(0f, b) * hdrIntensity,
            a);
    }

private static float ParticleStableRandom01(int id)
    {
        unchecked
        {
            uint x = (uint)id;
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return (x & 0x00FFFFFF) / 16777215f;
        }
    }

private bool TryGetParticleMaterial(string? materialPath, out MaterialAssetData data)
    {
        data = default!;
        string? fullPath = NormalizeExistingAssetPath(materialPath);
        if (fullPath == null || !MaterialAsset.IsMaterialPath(fullPath))
            return false;

        DateTime fileTime;
        try
        {
            fileTime = System.IO.File.GetLastWriteTimeUtc(fullPath);
        }
        catch
        {
            return false;
        }

        if (_particleMaterialCache.TryGetValue(fullPath, out var cached) && cached.FileTime == fileTime)
        {
            data = cached.Data;
            return true;
        }

        try
        {
            data = MaterialAsset.Load(fullPath);
            _particleMaterialCache[fullPath] = (fileTime, data);
            return true;
        }
        catch
        {
            return false;
        }
    }

private static GameObject? FindByEditorId(IReadOnlyList<GameObject> objs, string id)
    {
        foreach (var obj in objs)
        {
            if (obj.EditorId == id) return obj;
            var found = FindByEditorId(obj.Children, id);
            if (found != null) return found;
        }
        return null;
    }

private static int ApplyRendererMaxParticles(GrokoEngine.ParticleSystem ps, int requested)
    {
        int max = Math.Max(0, requested);
        if (ps.MaxRenderedParticles > 0)
            max = Math.Min(max, ps.MaxRenderedParticles);
        return Math.Min(max, Math.Max(0, ps.MaxParticles));
    }

private static int EffectiveParticleRenderQueue(GrokoEngine.ParticleSystem ps)
    {
        return ps.RenderQueue switch
        {
            ParticleRenderQueue.Opaque => 2000,
            ParticleRenderQueue.Transparent => 3000,
            ParticleRenderQueue.Overlay => 4000,
            _ => ps.BlendMode == ParticleBlendMode.Alpha ? 3000 : 3100
        };
    }

private static MiMotor.Mathematics.Vector3 RotateVec3(MiMotor.Mathematics.Vector3 v, MiMotor.Mathematics.Vector3 rot)
    {
        float z = rot.Z * (MathF.PI / 180f), cz = MathF.Cos(z), sz = MathF.Sin(z);
        v = new MiMotor.Mathematics.Vector3(v.X * cz - v.Y * sz, v.X * sz + v.Y * cz, v.Z);
        float x = rot.X * (MathF.PI / 180f), cx = MathF.Cos(x), sx = MathF.Sin(x);
        v = new MiMotor.Mathematics.Vector3(v.X, v.Y * cx - v.Z * sx, v.Y * sx + v.Z * cx);
        float y = rot.Y * (MathF.PI / 180f), cy = MathF.Cos(y), sy = MathF.Sin(y);
        return new MiMotor.Mathematics.Vector3(v.X * cy + v.Z * sy, v.Y, -v.X * sy + v.Z * cy);
    }

private void CollectParticleVerts(List<GameObject> objects, Vector3 right, Vector3 up, Vector3 cameraPosition)
        => CollectParticleVerts((IReadOnlyList<GameObject>)objects, right, up, cameraPosition);

private void AddParticleQuad(Particle p, MiMotor.Mathematics.Vector3 worldPos, Vector3 right, Vector3 up,
                                   int sheetCols, int sheetRows, float sheetFps,
                                   Vector4 colorOverride = default, float sizeMultiplier = 1f,
                                   bool allowRoll = true, bool flipU = false, bool flipV = false,
                                   float pivotX = 0f, float pivotY = 0f)
    {
        var color = colorOverride == default
            ? new Vector4(p.CurrentR, p.CurrentG, p.CurrentB, p.CurrentA)
            : colorOverride;
        float size = p.CurrentSize * sizeMultiplier;
        float half = size * 0.5f;
        float px = Math.Clamp(pivotX, -1f, 1f) * size;
        float py = Math.Clamp(pivotY, -1f, 1f) * size;
        float x0 = -half - px;
        float x1 = half - px;
        float y0 = -half - py;
        float y1 = half - py;
        float rot = allowRoll ? p.Rotation : 0f;

        // Texture sheet: calcular UVs del fotograma actual
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
        if (sheetCols > 1 || sheetRows > 1)
        {
            int totalFrames = sheetCols * sheetRows;
            int frame = (int)(p.Age * sheetFps) % Math.Max(1, totalFrames);
            int col = frame % sheetCols;
            int row = frame / sheetCols;
            float fw = 1f / sheetCols;
            float fh = 1f / sheetRows;
            u0 = col * fw; u1 = u0 + fw;
            v0 = row * fh; v1 = v0 + fh;
        }
        if (flipU) (u0, u1) = (u1, u0);
        if (flipV) (v0, v1) = (v1, v0);

        var wp2 = new Vector3(worldPos.X, worldPos.Y, worldPos.Z);
        _particleVerts.Add(new ParticleVertex(wp2, new Vector2(x0, y0), color, rot, new Vector2(u0, v1), 0f));
        _particleVerts.Add(new ParticleVertex(wp2, new Vector2(x1, y0), color, rot, new Vector2(u1, v1), 0f));
        _particleVerts.Add(new ParticleVertex(wp2, new Vector2(x1, y1), color, rot, new Vector2(u1, v0), 0f));
        _particleVerts.Add(new ParticleVertex(wp2, new Vector2(x0, y0), color, rot, new Vector2(u0, v1), 0f));
        _particleVerts.Add(new ParticleVertex(wp2, new Vector2(x1, y1), color, rot, new Vector2(u1, v0), 0f));
        _particleVerts.Add(new ParticleVertex(wp2, new Vector2(x0, y1), color, rot, new Vector2(u0, v0), 0f));
    }

private void BuildParticleShapeGizmo(GrokoEngine.ParticleSystem ps, Matrix4 world)
    {
        var color = new Vector4(0.25f, 0.78f, 1f, 0.95f);
        var origin = Vector3.TransformPosition(Vector3.Zero, world);
        int segments = 48;
        float radius = Math.Max(0.01f, ps.ShapeRadius);

        if (ps.Shape == ParticleShape.Box)
        {
            var min = new Vector3(-ps.ShapeBoxSizeX, -ps.ShapeBoxSizeY, -ps.ShapeBoxSizeZ) * 0.5f;
            var max = new Vector3(ps.ShapeBoxSizeX, ps.ShapeBoxSizeY, ps.ShapeBoxSizeZ) * 0.5f;
            Span<Vector3> c = stackalloc Vector3[8];
            c[0] = Vector3.TransformPosition(new Vector3(min.X, min.Y, min.Z), world);
            c[1] = Vector3.TransformPosition(new Vector3(max.X, min.Y, min.Z), world);
            c[2] = Vector3.TransformPosition(new Vector3(max.X, max.Y, min.Z), world);
            c[3] = Vector3.TransformPosition(new Vector3(min.X, max.Y, min.Z), world);
            c[4] = Vector3.TransformPosition(new Vector3(min.X, min.Y, max.Z), world);
            c[5] = Vector3.TransformPosition(new Vector3(max.X, min.Y, max.Z), world);
            c[6] = Vector3.TransformPosition(new Vector3(max.X, max.Y, max.Z), world);
            c[7] = Vector3.TransformPosition(new Vector3(min.X, max.Y, max.Z), world);
            AddLine(c[0], c[1], color); AddLine(c[1], c[2], color); AddLine(c[2], c[3], color); AddLine(c[3], c[0], color);
            AddLine(c[4], c[5], color); AddLine(c[5], c[6], color); AddLine(c[6], c[7], color); AddLine(c[7], c[4], color);
            AddLine(c[0], c[4], color); AddLine(c[1], c[5], color); AddLine(c[2], c[6], color); AddLine(c[3], c[7], color);
            return;
        }

        float arc = MathHelper.DegreesToRadians(Math.Clamp(ps.ShapeArc, 0f, 360f));
        Vector3 prev = origin + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float t = arc * i / segments;
            var p = Vector3.TransformPosition(new Vector3(MathF.Cos(t) * radius, 0f, MathF.Sin(t) * radius), world);
            AddLine(prev, p, color);
            prev = p;
        }

        if (ps.Shape == ParticleShape.Sphere)
        {
            prev = origin + new Vector3(0f, radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = MathHelper.TwoPi * i / segments;
                var p = Vector3.TransformPosition(new Vector3(0f, MathF.Cos(t) * radius, MathF.Sin(t) * radius), world);
                AddLine(prev, p, color);
                prev = p;
            }
        }
        else if (ps.Shape == ParticleShape.Cone)
        {
            float h = Math.Max(0.05f, radius / MathF.Max(0.05f, MathF.Tan(MathHelper.DegreesToRadians(Math.Max(1f, ps.ShapeAngle)))));
            var tip = Vector3.TransformPosition(new Vector3(0f, h, 0f), world);
            AddLine(origin + new Vector3(radius, 0f, 0f), tip, color);
            AddLine(origin + new Vector3(-radius, 0f, 0f), tip, color);
            AddLine(origin + new Vector3(0f, 0f, radius), tip, color);
            AddLine(origin + new Vector3(0f, 0f, -radius), tip, color);
        }
    }

private void UpdateAndRenderTrails(IReadOnlyList<GameObject> objects, Vector3 right, Vector3 up)
    {
        CollectTrails(objects);

        // Actualizar ages y eliminar puntos viejos
        var toRemove = new List<int>();
        foreach (var (id, pts) in _trailPoints)
        {
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                var (pos, age, maxAge) = pts[i];
                float newAge = age + 0.016f; // ~1 frame
                if (newAge >= maxAge) { pts.RemoveAt(i); continue; }
                pts[i] = (pos, newAge, maxAge);
            }
            if (pts.Count == 0) toRemove.Add(id);
        }
        foreach (var id in toRemove) _trailPoints.Remove(id);

        // Renderizar cada trail
        foreach (var (_, pts) in _trailPoints)
        {
            if (pts.Count < 2) continue;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var (p0, a0, m0) = pts[i];
                var (p1, a1, m1) = pts[i + 1];
                float t0 = 1f - a0 / Math.Max(0.001f, m0);
                float t1 = 1f - a1 / Math.Max(0.001f, m1);
                float alpha0 = t0;
                float alpha1 = t1;

                // Dirección del segmento
                float dx = p1.X - p0.X, dy = p1.Y - p0.Y, dz = p1.Z - p0.Z;
                float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 0.0001f) continue;

                // Perpendicular en plano cámara
                float prx = right.Y * dz / len - right.Z * dy / len;
                float pry = right.Z * dx / len - right.X * dz / len;
                float prz = right.X * dy / len - right.Y * dx / len;
                float pl = MathF.Sqrt(prx * prx + pry * pry + prz * prz);
                if (pl < 0.0001f) continue;
                prx /= pl; pry /= pl; prz /= pl;

                // Ancho interpolado (se obtiene del ps, usamos valores por defecto si no hay ref)
                float w0 = 0.05f * t0;
                float w1 = 0.05f * t1;

                var c0 = new Vector4(1f, 1f, 1f, alpha0);
                var c1 = new Vector4(1f, 1f, 1f, alpha1);

                var a = new Vector3(p0.X + prx * w0, p0.Y + pry * w0, p0.Z + prz * w0);
                var b = new Vector3(p0.X - prx * w0, p0.Y - pry * w0, p0.Z - prz * w0);
                var c = new Vector3(p1.X - prx * w1, p1.Y - pry * w1, p1.Z - prz * w1);
                var d = new Vector3(p1.X + prx * w1, p1.Y + pry * w1, p1.Z + prz * w1);

                _trailVerts.Add(new ParticleVertex(a, Vector2.Zero, c0, 0f, new Vector2(0, 0), 1f));
                _trailVerts.Add(new ParticleVertex(b, Vector2.Zero, c0, 0f, new Vector2(1, 0), 1f));
                _trailVerts.Add(new ParticleVertex(c, Vector2.Zero, c1, 0f, new Vector2(1, 1), 1f));
                _trailVerts.Add(new ParticleVertex(a, Vector2.Zero, c0, 0f, new Vector2(0, 0), 1f));
                _trailVerts.Add(new ParticleVertex(c, Vector2.Zero, c1, 0f, new Vector2(1, 1), 1f));
                _trailVerts.Add(new ParticleVertex(d, Vector2.Zero, c1, 0f, new Vector2(0, 1), 1f));
            }
        }
    }

private void CollectTrails(IReadOnlyList<GameObject> objects)
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;   // objeto inactivo: sin trails, como Unity
            var ps = obj.GetComponent<GrokoEngine.ParticleSystem>();
            if (ps != null && ps.Enabled && ps.TrailEnabled)
            {
                bool isLocal = ps.SimulationSpace == ParticleSimulationSpace.Local;
                var objPos = isLocal ? new MiMotor.Mathematics.Vector3(obj.PosX, obj.PosY, obj.PosZ) : MiMotor.Mathematics.Vector3.Zero;
                var objRot = isLocal ? new MiMotor.Mathematics.Vector3(obj.RotX, obj.RotY, obj.RotZ) : MiMotor.Mathematics.Vector3.Zero;

                foreach (var p in ps.Particles)
                {
                    var wp = GetWorldPos(p.Position, isLocal, objPos, objRot);
                    if (!_trailPoints.TryGetValue(p.Id, out var pts))
                    {
                        pts = new List<(MiMotor.Mathematics.Vector3, float, float)>();
                        _trailPoints[p.Id] = pts;
                    }
                    // Solo agregar si el punto es suficientemente diferente al último
                    bool addPoint = pts.Count == 0;
                    if (!addPoint)
                    {
                        var last = pts[pts.Count - 1].Pos;
                        float d2 = (wp.X - last.X) * (wp.X - last.X) + (wp.Y - last.Y) * (wp.Y - last.Y) + (wp.Z - last.Z) * (wp.Z - last.Z);
                        addPoint = d2 > 0.001f;
                    }
                    if (addPoint)
                        pts.Add((wp, 0f, ps.TrailLifetime));
                }
            }
            CollectTrails(obj.Children);
        }
    }

private void CollectTrails(List<GameObject> objects) => CollectTrails((IReadOnlyList<GameObject>)objects);

private static int CreateParticleShader()
    {
        const string vert = """
            #version 330 core
            layout(location=0) in vec3  aCenter;
            layout(location=1) in vec2  aOffset;
            layout(location=2) in vec4  aColor;
            layout(location=3) in float aRotation;
            layout(location=4) in vec2  aUv;
            layout(location=5) in float aAbsolute;
            uniform mat4 uMvp;
            uniform vec3 uCamRight;
            uniform vec3 uCamUp;
            out vec4 vColor;
            out vec2 vUv;
            void main()
            {
                vec3 world;
                if (aAbsolute > 0.5)
                {
                    world = aCenter;
                }
                else
                {
                    float c = cos(aRotation);
                    float s = sin(aRotation);
                    vec2 rotOff = vec2(c * aOffset.x - s * aOffset.y,
                                       s * aOffset.x + c * aOffset.y);
                    world = aCenter + uCamRight * rotOff.x + uCamUp * rotOff.y;
                }
                vColor      = aColor;
                vUv         = aUv;
                gl_Position = vec4(world, 1.0) * uMvp;
            }
            """;
        const string frag = """
            #version 330 core
            in  vec4 vColor;
            in  vec2 vUv;
            out vec4 outColor;
            uniform sampler2D uTexture;
            uniform int       uHasTexture;
            uniform int       uSoftParticles;
            uniform sampler2D uDepthTex;
            uniform vec2      uScreenSize;
            uniform float     uSoftRange;

            float LinearizeDepth(float depth)
            {
                float near = 0.01;
                float far  = 2000.0;
                float z    = depth * 2.0 - 1.0;
                return (2.0 * near * far) / (far + near - z * (far - near));
            }

            void main()
            {
                vec4 texColor;
                if (uHasTexture != 0)
                    texColor = texture(uTexture, vUv);
                else
                {
                    // Sin textura: disco suave con glow (en vez de cuadrado duro) — partículas vivas
                    float d = length(vUv - vec2(0.5)) * 2.0;          // 0 centro, 1 borde
                    float soft = smoothstep(1.0, 0.0, d);             // borde difuminado
                    soft *= soft;                                     // núcleo más brillante
                    texColor = vec4(1.0, 1.0, 1.0, soft);
                }
                outColor = vColor * texColor;

                // Soft particles: fade when intersecting geometry
                if (uSoftParticles != 0 && uScreenSize.x > 0.0)
                {
                    vec2 screenUv    = gl_FragCoord.xy / uScreenSize;
                    float sceneDepth = LinearizeDepth(texture(uDepthTex, screenUv).r);
                    float partDepth  = LinearizeDepth(gl_FragCoord.z);
                    float diff       = sceneDepth - partDepth;
                    float fade       = clamp(diff / uSoftRange, 0.0, 1.0);
                    outColor.a      *= fade;
                }

                if (outColor.a < 0.01) discard;
            }
            """;
        int v = CompileShader(ShaderType.VertexShader, vert);
        int f = CompileShader(ShaderType.FragmentShader, frag);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(p));
        GL.DetachShader(p, v); GL.DetachShader(p, f);
        GL.DeleteShader(v); GL.DeleteShader(f);
        return p;
    }

private readonly record struct ParticleVertex(Vector3 Center, Vector2 Offset, Vector4 Color, float Rotation, Vector2 Uv, float Absolute);

private readonly record struct ParticleRenderSurface(Vector4 Tint, string? TexturePath);

private readonly record struct ParticleRenderRange(
    string? TexturePath,
    int Start,
    int Count,
    ParticleBlendMode Blend,
    bool Soft,
    float SoftRange,
    int Queue,
    int SortingLayer,
    int OrderInLayer,
    int SortingFudge);

private readonly record struct ParticleVisualMesh(
    string MeshPath,
    ParticleRenderSurface Surface,
    SurfaceMaps Maps,
    Vector4 Material,
    Vector4 Emission)
{
    public ParticleVisualMesh WithFallbackSurface(ParticleRenderSurface fallback)
    {
        var tint = Surface.Tint == default ? fallback.Tint : Surface.Tint;
        string? texture = string.IsNullOrWhiteSpace(Surface.TexturePath) ? fallback.TexturePath : Surface.TexturePath;
        return this with { Surface = new ParticleRenderSurface(tint, texture) };
    }
}

private readonly Dictionary<string, (DateTime WriteTime, ParticleVisualMesh Visual)> _particlePrefabVisualCache = new(StringComparer.OrdinalIgnoreCase);
}
