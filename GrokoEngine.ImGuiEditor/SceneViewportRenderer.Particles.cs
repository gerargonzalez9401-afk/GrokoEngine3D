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

        _particleInstances.Clear();
        _particleStretchedInstances.Clear();
        _particleRanges.Clear();
        _particleSortSlices.Clear();
        CollectParticleVerts(objects, camRight, camUp, cameraPosition);
        SortParticleSlicesForTransparentPass();

        // ── Trails ────────────────────────────────────────────────
        _trailVerts.Clear();
        UpdateAndRenderTrails(objects, camRight, camUp);

        if (_particleInstances.Count == 0 && _particleStretchedInstances.Count == 0 && _trailVerts.Count == 0) return;

        bool needsSoftParticles = _particleRanges.Any(r => r.Soft);
        bool needsDistortion = _particleRanges.Any(r => r.Distortion);
        if (needsDistortion)
            ResolveAndCaptureOpaqueTexture?.Invoke();
        if (needsSoftParticles)
            CaptureDepthBuffer();
        int w = needsSoftParticles ? _depthFboWidth : 0;
        int h = needsSoftParticles ? _depthFboHeight : 0;
        int[] viewport = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport);
        int screenW = Math.Max(w, viewport[2]);
        int screenH = Math.Max(h, viewport[3]);

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
            GL.Uniform1(_particleDistortionLoc, 0);
            GL.BindVertexArray(_trailVao);
            TrackParticleDraw(_trailVerts.Count);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _trailVerts.Count);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        // ── Dibujar partículas con soft particles ─────────────────
        // Cada rango es o bien billboard normal instanciado, o bien stretched-billboard
        // instanciado (su propio shader, porque el eje perpendicular depende de la velocidad
        // de cada partícula además de la cámara) — se activa el programa/VAO correcto solo
        // cuando cambia respecto al rango anterior.
        if (_particleInstances.Count > 0 || _particleStretchedInstances.Count > 0)
        {
            int activeProgram = 0;
            Matrix4 mvpLocal = mvp;

            void ActivateInstanced()
            {
                if (activeProgram == _particleInstancedShader) return;
                activeProgram = _particleInstancedShader;
                GL.UseProgram(_particleInstancedShader);
                GL.BindVertexArray(_particleInstancedVao);
                GL.UniformMatrix4(_particleInstMvpLoc, true, ref mvpLocal);
                GL.Uniform3(_particleInstRightLoc, camRight.X, camRight.Y, camRight.Z);
                GL.Uniform3(_particleInstUpLoc, camUp.X, camUp.Y, camUp.Z);
                GL.Uniform1(_particleInstTexLoc, 0);
                GL.Uniform2(_particleInstScreenSizeLoc, (float)screenW, (float)screenH);
                GL.Uniform1(_particleInstDepthTexLoc, 1);
                GL.Uniform1(_particleInstOpaqueTexLoc, 2);
            }

            void ActivateStretched()
            {
                if (activeProgram == _particleStretchedShader) return;
                activeProgram = _particleStretchedShader;
                GL.UseProgram(_particleStretchedShader);
                GL.BindVertexArray(_particleStretchedVao);
                GL.UniformMatrix4(_particleStrMvpLoc, true, ref mvpLocal);
                GL.Uniform3(_particleStrRightLoc, camRight.X, camRight.Y, camRight.Z);
                GL.Uniform1(_particleStrTexLoc, 0);
                GL.Uniform2(_particleStrScreenSizeLoc, (float)screenW, (float)screenH);
                GL.Uniform1(_particleStrDepthTexLoc, 1);
                GL.Uniform1(_particleStrOpaqueTexLoc, 2);
            }

            // Depth/opaque texture van en las mismas unidades para ambos programas
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _depthTex);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, OpaqueTextureHandle);
            GL.ActiveTexture(TextureUnit.Texture0);

            foreach (var range in _particleRanges)
            {
                int softLoc, softRangeLoc, distortionLoc, distortionStrengthLoc, refractionStrengthLoc, hasTexLoc;
                if (range.Kind == ParticleDrawKind.StretchedInstanced)
                {
                    ActivateStretched();
                    softLoc = _particleStrSoftLoc; softRangeLoc = _particleStrSoftRangeLoc;
                    distortionLoc = _particleStrDistortionLoc;
                    distortionStrengthLoc = _particleStrDistortionStrengthLoc;
                    refractionStrengthLoc = _particleStrRefractionStrengthLoc;
                    hasTexLoc = _particleStrHasTexLoc;
                }
                else
                {
                    ActivateInstanced();
                    softLoc = _particleInstSoftLoc; softRangeLoc = _particleInstSoftRangeLoc;
                    distortionLoc = _particleInstDistortionLoc;
                    distortionStrengthLoc = _particleInstDistortionStrengthLoc;
                    refractionStrengthLoc = _particleInstRefractionStrengthLoc;
                    hasTexLoc = _particleInstHasTexLoc;
                }

                ApplyParticleBlendMode(range.Blend);
                GL.Uniform1(softRangeLoc, range.SoftRange);
                GL.Uniform1(softLoc, range.Soft && w > 0 && h > 0 ? 1 : 0);
                bool distortionEnabled = range.Distortion && OpaqueTextureHandle != 0 && screenW > 0 && screenH > 0;
                GL.Uniform1(distortionLoc, distortionEnabled ? 1 : 0);
                GL.Uniform1(distortionStrengthLoc, distortionEnabled ? range.DistortionStrength : 0f);
                GL.Uniform1(refractionStrengthLoc, distortionEnabled ? range.RefractionStrength : 0f);
                string? texPath = range.TexturePath;
                bool hasTex = !string.IsNullOrWhiteSpace(texPath) && System.IO.File.Exists(texPath);
                int texId = hasTex ? GetTexture(texPath!) : 0;
                GL.Uniform1(hasTexLoc, hasTex ? 1 : 0);
                GL.BindTexture(TextureTarget.Texture2D, texId);

                if (range.Kind == ParticleDrawKind.StretchedInstanced)
                {
                    UploadParticleStretchedInstanceRange(range.Start, range.Count);
                    TrackParticleDraw(range.Count * 6);
                    GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, range.Count);
                }
                else
                {
                    UploadParticleInstanceRange(range.Start, range.Count);
                    TrackParticleDraw(range.Count * 6);
                    GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, range.Count);
                }
            }

            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, 0);
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

/// <summary>Sube (a offset 0, siguiendo el mismo patrón que UploadInstanceMatrices para mallas) el
/// subrango [start,start+count) de _particleInstances al VBO de instancias, listo para
/// glDrawArraysInstanced.</summary>
private void UploadParticleInstanceRange(int start, int count)
    {
        if (count <= 0) return;
        if (_particleInstanceUploadBuffer.Length < count)
            Array.Resize(ref _particleInstanceUploadBuffer, count);
        for (int i = 0; i < count; i++)
            _particleInstanceUploadBuffer[i] = _particleInstances[start + i];

        int bytes = count * Unsafe.SizeOf<ParticleInstance>();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleInstanceVbo);
        if (bytes > _particleInstanceBufferCapacity)
        {
            _particleInstanceBufferCapacity = Math.Max(bytes, _particleInstanceBufferCapacity == 0 ? 16384 : _particleInstanceBufferCapacity * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, _particleInstanceBufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, _particleInstanceUploadBuffer);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

private void UploadParticleStretchedInstanceRange(int start, int count)
    {
        if (count <= 0) return;
        if (_particleStretchedInstanceUploadBuffer.Length < count)
            Array.Resize(ref _particleStretchedInstanceUploadBuffer, count);
        for (int i = 0; i < count; i++)
            _particleStretchedInstanceUploadBuffer[i] = _particleStretchedInstances[start + i];

        int bytes = count * Unsafe.SizeOf<ParticleStretchedInstance>();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleStretchedInstanceVbo);
        if (bytes > _particleStretchedInstanceBufferCapacity)
        {
            _particleStretchedInstanceBufferCapacity = Math.Max(bytes, _particleStretchedInstanceBufferCapacity == 0 ? 16384 : _particleStretchedInstanceBufferCapacity * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, _particleStretchedInstanceBufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, _particleStretchedInstanceUploadBuffer);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
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
            if (!LayerMask.Contains(_activeCullingMask, obj.Layer)) { CollectParticleVerts(obj.Children, right, up, cameraPosition); continue; }
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
            // OJO: siempre posición MUNDIAL (obj.Position), no PosX/Y/Z local. Si el sistema de
            // partículas es hijo de otro objeto (p.ej. el escape de un avión que vuela lejos del
            // origen), PosX/Y/Z local queda cerca de (0,0,0) sin importar dónde esté el avión de
            // verdad -> el LOD lo trataba como si estuviera siempre en el origen del mundo, así
            // que solo se veía (lodRatio>0) cuando la cámara se acercaba a ESE punto, no al avión.
            var camPos = new MiMotor.Mathematics.Vector3(cameraPosition.X, cameraPosition.Y, cameraPosition.Z);
            float lodDist = Vector3Dist(obj.Position, camPos);
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
            int queue = EffectiveParticleRenderQueue(ps);
            bool distortion = ps.DistortionEnabled && (Math.Abs(ps.DistortionStrength) > 0.0001f || Math.Abs(ps.RefractionStrength) > 0.0001f);
            float distortionStrength = Math.Clamp(ps.DistortionStrength, -2f, 2f);
            float refractionStrength = Math.Clamp(ps.RefractionStrength, 0f, 2f);
            int insertion = _particleSortSlices.Count;

            foreach (var (p, dist) in _sortBuf)
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

                bool stretched = ps.StretchedBillboard || ps.RenderMode == ParticleRenderMode.StretchedBillboard;
                Vector3 velocityDir = default;
                float speed = 0f;
                bool stretchable = stretched && TryComputeStretchAxes(p, renderRight, out velocityDir, out speed);

                if (stretchable)
                {
                    var uvRect = ComputeSheetUvRect(p, ps.SheetColumns, ps.SheetRows, ps.SheetFrameRate, ps.FlipU, ps.FlipV);
                    float halfWidth = p.CurrentSize * sizeMultiplier * 0.5f;
                    int instStart = _particleStretchedInstances.Count;
                    _particleStretchedInstances.Add(new ParticleStretchedInstance(
                        new Vector3(wp.X, wp.Y, wp.Z),
                        halfWidth,
                        velocityDir,
                        halfWidth + speed * ps.StretchSpeedScale * ps.StretchLengthScale,
                        color,
                        uvRect,
                        new Vector2(ps.PivotX, ps.PivotY)));
                    _particleSortSlices.Add(new ParticleSortSlice(
                        surface.TexturePath, instStart, 1, ps.BlendMode, ps.SoftParticles,
                        Math.Max(0.001f, ps.SoftParticleRange), queue, ps.SortingLayer, ps.OrderInLayer, ps.SortingFudge,
                        ps.SortParticles ? ps.SortMode : ParticleSortMode.None, dist, p.Age, insertion++,
                        distortion, distortionStrength, refractionStrength, ParticleDrawKind.StretchedInstanced));
                }
                else
                {
                    var uvRect = ComputeSheetUvRect(p, ps.SheetColumns, ps.SheetRows, ps.SheetFrameRate, ps.FlipU, ps.FlipV);
                    float rot = ps.AllowRoll ? p.Rotation : 0f;
                    int instStart = _particleInstances.Count;
                    _particleInstances.Add(new ParticleInstance(
                        new Vector3(wp.X, wp.Y, wp.Z),
                        p.CurrentSize * sizeMultiplier,
                        color,
                        rot,
                        uvRect,
                        new Vector2(ps.PivotX, ps.PivotY)));
                    _particleSortSlices.Add(new ParticleSortSlice(
                        surface.TexturePath, instStart, 1, ps.BlendMode, ps.SoftParticles,
                        Math.Max(0.001f, ps.SoftParticleRange), queue, ps.SortingLayer, ps.OrderInLayer, ps.SortingFudge,
                        ps.SortParticles ? ps.SortMode : ParticleSortMode.None, dist, p.Age, insertion++,
                        distortion, distortionStrength, refractionStrength, ParticleDrawKind.Instanced));
                }
            }

            CollectParticleVerts(obj.Children, right, up, cameraPosition);
        }
    }

private void SortParticleSlicesForTransparentPass()
    {
        if (_particleSortSlices.Count == 0)
            return;

        _particleSortSlices.Sort(CompareParticleSortSlices);
        _particleSortedInstances.Clear();
        _particleSortedStretchedInstances.Clear();
        _particleRanges.Clear();

        ParticleRenderRange? current = null;
        foreach (var slice in _particleSortSlices)
        {
            int start;
            if (slice.Kind == ParticleDrawKind.StretchedInstanced)
            {
                start = _particleSortedStretchedInstances.Count;
                for (int i = 0; i < slice.Count; i++)
                    _particleSortedStretchedInstances.Add(_particleStretchedInstances[slice.Start + i]);
            }
            else
            {
                start = _particleSortedInstances.Count;
                for (int i = 0; i < slice.Count; i++)
                    _particleSortedInstances.Add(_particleInstances[slice.Start + i]);
            }

            if (current.HasValue && CanMergeParticleRange(current.Value, slice))
            {
                var c = current.Value;
                current = c with { Count = c.Count + slice.Count };
                _particleRanges[^1] = current.Value;
            }
            else
            {
                var range = new ParticleRenderRange(
                    slice.TexturePath,
                    start,
                    slice.Count,
                    slice.Blend,
                    slice.Soft,
                    slice.SoftRange,
                    slice.Queue,
                    slice.SortingLayer,
                    slice.OrderInLayer,
                    slice.SortingFudge,
                    slice.Distortion,
                    slice.DistortionStrength,
                    slice.RefractionStrength,
                    slice.Kind);
                _particleRanges.Add(range);
                current = range;
            }
        }

        _particleInstances.Clear();
        _particleInstances.AddRange(_particleSortedInstances);
        _particleStretchedInstances.Clear();
        _particleStretchedInstances.AddRange(_particleSortedStretchedInstances);
    }

private static int CompareParticleSortSlices(ParticleSortSlice a, ParticleSortSlice b)
    {
        int cmp = a.Queue.CompareTo(b.Queue);
        if (cmp != 0) return cmp;
        cmp = a.SortingLayer.CompareTo(b.SortingLayer);
        if (cmp != 0) return cmp;
        cmp = a.OrderInLayer.CompareTo(b.OrderInLayer);
        if (cmp != 0) return cmp;
        cmp = a.SortingFudge.CompareTo(b.SortingFudge);
        if (cmp != 0) return cmp;
        cmp = a.SortMode.CompareTo(b.SortMode);
        if (cmp != 0) return cmp;

        return a.SortMode switch
        {
            ParticleSortMode.OldestInFront => a.Age.CompareTo(b.Age),
            ParticleSortMode.YoungestInFront => b.Age.CompareTo(a.Age),
            ParticleSortMode.Distance => b.Distance.CompareTo(a.Distance),
            _ => a.Insertion.CompareTo(b.Insertion)
        };
    }

private static bool CanMergeParticleRange(ParticleRenderRange range, ParticleSortSlice slice)
    {
        return range.Kind == slice.Kind &&
               string.Equals(range.TexturePath, slice.TexturePath, StringComparison.OrdinalIgnoreCase) &&
               range.Blend == slice.Blend &&
               range.Soft == slice.Soft &&
               Math.Abs(range.SoftRange - slice.SoftRange) < 0.0001f &&
               range.Queue == slice.Queue &&
               range.SortingLayer == slice.SortingLayer &&
               range.OrderInLayer == slice.OrderInLayer &&
               range.SortingFudge == slice.SortingFudge &&
               range.Distortion == slice.Distortion &&
               Math.Abs(range.DistortionStrength - slice.DistortionStrength) < 0.0001f &&
               Math.Abs(range.RefractionStrength - slice.RefractionStrength) < 0.0001f;
    }

private void CollectParticleMeshDraws(IReadOnlyList<GameObject> objects, Vector3 cameraPosition)
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive)
                continue;

            if (obj.GetComponent<LodGroup>() is { Enabled: true } &&
                HasHierarchyLodChildren(obj))
                continue;

            if (!LayerMask.Contains(_activeCullingMask, obj.Layer))
            {
                CollectParticleMeshDraws(obj.Children, cameraPosition);
                continue;
            }

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
                castShadows: ps.ParticleCastShadows,
                receiveShadows: ps.ParticleReceiveShadows);
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
                new Vector4(0f, 0.5f, 3f, 0f),
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

/// <summary>Replica el chequeo de degeneración que hacía AddStretchedQuad en CPU (velocidad casi
/// nula, o casi paralela al eje derecho de cámara -> el eje perpendicular del quad estirado
/// colapsa). En esos casos la partícula cae al billboard instanciado normal, igual que antes.</summary>
private static bool TryComputeStretchAxes(Particle p, Vector3 right, out Vector3 velocityDir, out float speed)
    {
        speed = MathF.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y + p.Velocity.Z * p.Velocity.Z);
        if (speed < 0.001f)
        {
            velocityDir = default;
            return false;
        }

        var vel = new Vector3(p.Velocity.X / speed, p.Velocity.Y / speed, p.Velocity.Z / speed);
        float cx = vel.Y * right.Z - vel.Z * right.Y;
        float cy = vel.Z * right.X - vel.X * right.Z;
        float cz = vel.X * right.Y - vel.Y * right.X;
        if (MathF.Sqrt(cx * cx + cy * cy + cz * cz) < 0.001f)
        {
            velocityDir = default;
            return false;
        }

        velocityDir = vel;
        return true;
    }

private static Vector4 ComputeSheetUvRect(Particle p, int sheetCols, int sheetRows, float sheetFps, bool flipU, bool flipV)
    {
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
        return new Vector4(u0, v0, u1, v1);
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
        int v = CompileShader(ShaderType.VertexShader, vert);
        int f = CompileShader(ShaderType.FragmentShader, ParticleFragmentShaderSource);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(p));
        GL.DetachShader(p, v); GL.DetachShader(p, f);
        GL.DeleteShader(v); GL.DeleteShader(f);
        return p;
    }

private const string ParticleFragmentShaderSource = """
        #version 330 core
        in  vec4 vColor;
        in  vec2 vUv;
        out vec4 outColor;
        uniform sampler2D uTexture;
        uniform int       uHasTexture;
        uniform int       uSoftParticles;
        uniform sampler2D uDepthTex;
        uniform sampler2D uOpaqueTexture;
        uniform vec2      uScreenSize;
        uniform float     uSoftRange;
        uniform int       uDistortionEnabled;
        uniform float     uDistortionStrength;
        uniform float     uRefractionStrength;

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

            if (uDistortionEnabled != 0 && uScreenSize.x > 0.0 && outColor.a > 0.001)
            {
                vec2 screenUv = gl_FragCoord.xy / uScreenSize;
                vec2 centered = vUv * 2.0 - 1.0;
                float radial = clamp(1.0 - length(centered), 0.0, 1.0);
                vec2 texNoise = texColor.rg * 2.0 - 1.0;
                vec2 swirl = vec2(-centered.y, centered.x) * radial;
                vec2 offset = (texNoise * 0.65 + swirl * 0.35) * uDistortionStrength * 0.025 * outColor.a;
                vec3 sceneColor = texture(uOpaqueTexture, clamp(screenUv + offset, vec2(0.001), vec2(0.999))).rgb;
                float refract = clamp(uRefractionStrength, 0.0, 2.0) * outColor.a;
                outColor.rgb = mix(outColor.rgb, sceneColor + outColor.rgb * 0.18, clamp(refract, 0.0, 1.0));
                outColor.a = max(outColor.a * 0.35, radial * clamp(abs(uDistortionStrength), 0.0, 1.0) * 0.08);
            }

            if (outColor.a < 0.01) discard;
        }
        """;

/// <summary>
/// Shader instanciado para partículas billboard estándar: la CPU sube 1 registro chico por
/// partícula (centro, tamaño, color, rotación, uv-rect, pivot) y la GPU repite un quad unitario
/// compartido (2 atributos por-vertice, divisor 0) multiplicado por instancia (divisor 1) via
/// glDrawArraysInstanced. Reemplaza escribir 6 vértices duplicados por partícula desde la CPU.
/// </summary>
private static int CreateParticleInstancedShader()
    {
        const string vert = """
            #version 330 core
            layout(location=0) in vec2  aUnitOffset;   // por-vertice (divisor 0): -0.5..0.5
            layout(location=1) in vec2  aUnitUv;        // por-vertice (divisor 0): 0..1
            layout(location=2) in vec3  iCenter;        // por-instancia (divisor 1)
            layout(location=3) in float iSize;
            layout(location=4) in vec4  iColor;
            layout(location=5) in float iRotation;
            layout(location=6) in vec4  iUvRect;        // u0,v0,u1,v1
            layout(location=7) in vec2  iPivot;
            uniform mat4 uMvp;
            uniform vec3 uCamRight;
            uniform vec3 uCamUp;
            out vec4 vColor;
            out vec2 vUv;
            void main()
            {
                vec2 local = aUnitOffset * iSize - iPivot * iSize;
                float c = cos(iRotation);
                float s = sin(iRotation);
                vec2 rotOff = vec2(c * local.x - s * local.y,
                                   s * local.x + c * local.y);
                vec3 world = iCenter + uCamRight * rotOff.x + uCamUp * rotOff.y;
                vColor      = iColor;
                vUv         = vec2(mix(iUvRect.x, iUvRect.z, aUnitUv.x), mix(iUvRect.y, iUvRect.w, aUnitUv.y));
                gl_Position = vec4(world, 1.0) * uMvp;
            }
            """;
        int v = CompileShader(ShaderType.VertexShader, vert);
        int f = CompileShader(ShaderType.FragmentShader, ParticleFragmentShaderSource);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(p));
        GL.DetachShader(p, v); GL.DetachShader(p, f);
        GL.DeleteShader(v); GL.DeleteShader(f);
        return p;
    }

/// <summary>
/// Shader instanciado para partículas "stretched" (estiradas en la dirección de velocidad).
/// El eje perpendicular a cámara (perp) se recalcula acá porque depende de uCamRight, que
/// cambia cada frame; no se puede precomputar en la instancia como con el billboard normal.
/// </summary>
private static int CreateParticleStretchedShader()
    {
        const string vert = """
            #version 330 core
            layout(location=0) in vec2  aUnitOffset;    // por-vertice (divisor 0): (perpSign, fwdSign) en -1..1
            layout(location=1) in vec2  aUnitUv;         // por-vertice (divisor 0): 0..1
            layout(location=2) in vec3  iCenter;         // por-instancia (divisor 1)
            layout(location=3) in float iHalfWidth;
            layout(location=4) in vec3  iVelocityDir;    // normalizada
            layout(location=5) in float iHalfHeight;
            layout(location=6) in vec4  iColor;
            layout(location=7) in vec4  iUvRect;         // u0,v0,u1,v1
            layout(location=8) in vec2  iPivot;
            uniform mat4 uMvp;
            uniform vec3 uCamRight;
            out vec4 vColor;
            out vec2 vUv;
            void main()
            {
                vec3 perp = normalize(cross(iVelocityDir, uCamRight)) * iHalfWidth;
                vec3 fwd = iVelocityDir * iHalfHeight;
                vec3 center = iCenter - perp * iPivot.x - fwd * iPivot.y;
                vec3 world = center + perp * aUnitOffset.x + fwd * aUnitOffset.y;
                vColor      = iColor;
                vUv         = vec2(mix(iUvRect.x, iUvRect.z, aUnitUv.x), mix(iUvRect.y, iUvRect.w, aUnitUv.y));
                gl_Position = vec4(world, 1.0) * uMvp;
            }
            """;
        int v = CompileShader(ShaderType.VertexShader, vert);
        int f = CompileShader(ShaderType.FragmentShader, ParticleFragmentShaderSource);
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

/// <summary>Un registro por partícula para el camino de GPU instancing (billboard estándar, no stretched).</summary>
private readonly record struct ParticleInstance(Vector3 Center, float Size, Vector4 Color, float Rotation, Vector4 UvRect, Vector2 Pivot);

/// <summary>Un registro por partícula para el camino de GPU instancing de billboards "stretched"
/// (estirados en la dirección de velocidad: lluvia, chispas rápidas). HalfWidth/HalfHeight y
/// VelocityDir son los mismos valores que antes calculaba AddStretchedQuad en CPU; el eje
/// perpendicular a cámara (perp = normalize(cross(VelocityDir, uCamRight))) se recalcula en el
/// vertex shader porque depende de la cámara del frame actual.</summary>
private readonly record struct ParticleStretchedInstance(Vector3 Center, float HalfWidth, Vector3 VelocityDir, float HalfHeight, Vector4 Color, Vector4 UvRect, Vector2 Pivot);

private readonly record struct ParticleRenderSurface(Vector4 Tint, string? TexturePath);

private enum ParticleDrawKind { Instanced, StretchedInstanced }

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
    int SortingFudge,
    bool Distortion,
    float DistortionStrength,
    float RefractionStrength,
    ParticleDrawKind Kind);

private readonly record struct ParticleSortSlice(
    string? TexturePath,
    int Start,
    int Count,
    ParticleBlendMode Blend,
    bool Soft,
    float SoftRange,
    int Queue,
    int SortingLayer,
    int OrderInLayer,
    int SortingFudge,
    ParticleSortMode SortMode,
    float Distance,
    float Age,
    int Insertion,
    bool Distortion,
    float DistortionStrength,
    float RefractionStrength,
    ParticleDrawKind Kind);

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
