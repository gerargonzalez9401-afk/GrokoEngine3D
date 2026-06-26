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
private static int ShadowPcfRadiusFor(ShadowQuality quality) => quality switch
    {
        ShadowQuality.Low    => 0,
        ShadowQuality.Medium => 1,
        ShadowQuality.High   => 1,
        ShadowQuality.Ultra  => 1,
        _ => 1
    };

private static (int Directional, int Spot, int Point) ShadowMapSizesFor(ShadowQuality quality) => quality switch
    {
        ShadowQuality.Low    => (768,   512,  256),
        ShadowQuality.Medium => (1024,  768,  384),
        ShadowQuality.High   => (1536, 1024,  512),
        ShadowQuality.Ultra  => (2048, 1536,  768),
        _                    => (1024,  768,  384)
    };

private void TrackShadowDraw()
    {
        _statsShadowDrawCalls++;
    }

private (ShadowInfo Directional, ShadowInfo Spot, PointShadowInfo Point) PrepareRealtimeShadows(
        IReadOnlyList<GameObject> objects,
        ImGuiEditorApp.EditorCameraState camera,
        int width,
        int height)
    {
        _statsDirectionalShadowMs = 0f;
        _statsSpotShadowMs = 0f;
        _statsPointShadowMs = 0f;

        if (!RenderRealtimeShadows)
            return (ShadowInfo.Disabled,
                new ShadowInfo(false, Matrix4.Identity, 0f),
                new PointShadowInfo(false, Vector3.Zero, 1f, 0f));

        int interval = Math.Max(1, ShadowUpdateIntervalFrames);

        var directional = FindPrimaryDirectionalLight(objects);
        var lightDir = directional?.gameObject != null && directional.Shadows
            ? ToTk(directional.GetNormalizedDirection()).Normalized()
            : Vector3.Zero;

        bool sameDirectionalResources =
            _cachedRealtimeShadowsValid &&
            _cachedShadowWidth == width &&
            _cachedShadowHeight == height &&
            _cachedShadowQuality == _shadowQuality &&
            SameDirection(_cachedShadowLightDirection, lightDir);

        // La sombra direccional se regenera según ShadowUpdateIntervalFrames (default 1 = cada
        // frame). Antes se reutilizaba mientras la CÁMARA no se moviese, pero eso ignoraba el
        // movimiento de los OBJETOS: con la cámara quieta, la sombra de un personaje caminando se
        // quedaba congelada ("no sigue") y daba un salto al refrescarse ("tiembla").
        bool reuseDirectional = sameDirectionalResources &&
            _frameCount - _cachedRealtimeShadowFrame < interval;

        if (!reuseDirectional)
        {
            long start = RenderTimestamp();
            _cachedDirectionalShadow = PrepareDirectionalShadow(objects, camera, width, height, directional, lightDir);
            _statsDirectionalShadowMs = RenderElapsedMs(start);
            _cachedRealtimeShadowFrame = _frameCount;
            _cachedRealtimeShadowsValid = true;
            _cachedShadowLightDirection = lightDir;
            _cachedShadowWidth = width;
            _cachedShadowHeight = height;
            _cachedShadowQuality = _shadowQuality;
        }

        // Spot y point se regeneran según ShadowUpdateIntervalFrames (default 1 = cada frame),
        // igual que la direccional, para que SIGAN a los objetos en movimiento. Antes solo se
        // refrescaban si la LUZ cambiaba (firma), así que con la luz quieta la sombra de un objeto
        // que se movía quedaba congelada.
        bool reuseSpot = _cachedRealtimeShadowsValid && _frameCount - _cachedSpotShadowFrame < interval;
        if (!reuseSpot)
        {
            long start = RenderTimestamp();
            _cachedSpotShadow = PrepareSpotShadow(objects);
            _statsSpotShadowMs = RenderElapsedMs(start);
            _cachedSpotShadowFrame = _frameCount;
        }

        bool reusePoint = _cachedRealtimeShadowsValid && _frameCount - _cachedPointShadowFrame < interval;
        if (!reusePoint)
        {
            long start = RenderTimestamp();
            _cachedPointShadow = PreparePointShadow(objects);
            _statsPointShadowMs = RenderElapsedMs(start);
            _cachedPointShadowFrame = _frameCount;
        }

        return (_cachedDirectionalShadow, _cachedSpotShadow, _cachedPointShadow);
    }

private static bool SameDirection(Vector3 a, Vector3 b)
    {
        if (a.LengthSquared < 0.0001f && b.LengthSquared < 0.0001f)
            return true;
        if (a.LengthSquared < 0.0001f || b.LengthSquared < 0.0001f)
            return false;
        return Vector3.Dot(a.Normalized(), b.Normalized()) > 0.9999f;
    }

private ShadowInfo PrepareDirectionalShadow(
        IReadOnlyList<GameObject> objects,
        ImGuiEditorApp.EditorCameraState camera,
        int width,
        int height,
        DirectionalLight? directional,
        Vector3 lightDir)
    {
        if (directional?.gameObject == null || !directional.Shadows)
            return ShadowInfo.Disabled;

        var up = MathF.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.92f ? Vector3.UnitZ : Vector3.UnitY;

        int cascadeCount = DirectionalCascadeCountFor(_shadowQuality);
        var cascades = new Matrix4[MaxDirectionalShadowCascades];
        var splits = new float[MaxDirectionalShadowCascades];
        ComputeDirectionalCascades(camera, width, height, lightDir, up, cascadeCount, cascades, splits);

        GL.GetInteger(GetPName.FramebufferBinding, out int previousFramebuffer);
        int[] previousViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, previousViewport);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        GL.Viewport(0, 0, _directionalShadowSize, _directionalShadowSize);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Front);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(1.5f, 2.5f);
        GL.ColorMask(false, false, false, false);

        GL.UseProgram(_shadowShader);
        for (int i = 0; i < cascadeCount; i++)
        {
            GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, _shadowArrayTex, 0, i);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            var lightMvp = cascades[i];
            GL.UniformMatrix4(_shadowMvpLocation, true, ref lightMvp);
            DrawDepthGeometry(_shadowModelLocation);
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.ColorMask(true, true, true, true);
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.CullFace(TriangleFace.Back);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
        GL.Viewport(previousViewport[0], previousViewport[1], previousViewport[2], previousViewport[3]);

        return new ShadowInfo(true, cascades[0], Math.Clamp(directional.ShadowStrength, 0f, 1f), cascadeCount, cascades, splits, ToTk(camera.Position));
    }

private static void SnapShadowCameraToTexels(
        ref Vector3 sceneCenter,
        ref Vector3 lightPos,
        ref Matrix4 lightView,
        Vector3 lightDir,
        Vector3 up,
        float shadowExtent,
        int shadowMapSize)
    {
        float texelWorldSize = shadowExtent / Math.Max(1, shadowMapSize);
        if (texelWorldSize <= 0f)
            return;

        var lightSpaceCenter = Vector3.TransformPosition(sceneCenter, lightView);
        lightSpaceCenter.X = MathF.Round(lightSpaceCenter.X / texelWorldSize) * texelWorldSize;
        lightSpaceCenter.Y = MathF.Round(lightSpaceCenter.Y / texelWorldSize) * texelWorldSize;

        Matrix4.Invert(lightView, out var invLightView);
        sceneCenter = Vector3.TransformPosition(lightSpaceCenter, invLightView);
        lightPos = sceneCenter - lightDir * shadowExtent;
        lightView = Matrix4.LookAt(lightPos, sceneCenter, up);
    }

private static int DirectionalCascadeCountFor(ShadowQuality quality) => quality switch
    {
        ShadowQuality.Low => 1,
        ShadowQuality.Medium => 2,
        ShadowQuality.High => 3,
        ShadowQuality.Ultra => 4,
        _ => 2
    };

private static float DirectionalShadowDistanceFor(ShadowQuality quality) => quality switch
    {
        // Distancia de sombra MÁS CORTA = más texeles del shadow map por objeto cercano = sombras
        // mucho más nítidas. 180 repartía la resolución en lo lejano y lo cercano salía borroso.
        ShadowQuality.Low => 35f,
        ShadowQuality.Medium => 50f,
        ShadowQuality.High => 70f,
        ShadowQuality.Ultra => 95f,
        _ => 50f
    };

private void ComputeDirectionalCascades(
        ImGuiEditorApp.EditorCameraState camera,
        int width,
        int height,
        Vector3 lightDir,
        Vector3 up,
        int cascadeCount,
        Matrix4[] cascades,
        float[] splits)
    {
        float nearClip = Math.Max(0.01f, camera.NearClip);
        float farClip = Math.Min(Math.Max(nearClip + 1f, camera.FarClip), DirectionalShadowDistanceFor(_shadowQuality));
        float aspect = width / (float)Math.Max(1, height);
        Matrix4 view = Matrix4.LookAt(ToTk(camera.Position), ToTk(camera.Position) + ToTk(camera.Front), ToTk(camera.Up));

        float previousSplit = nearClip;
        for (int i = 0; i < cascadeCount; i++)
        {
            float t = (i + 1) / (float)cascadeCount;
            float log = nearClip * MathF.Pow(farClip / nearClip, t);
            float linear = nearClip + (farClip - nearClip) * t;
            float split = MathHelper.Lerp(linear, log, 0.55f);
            if (i == cascadeCount - 1)
                split = farClip;

            var corners = GetCameraFrustumCorners(camera, view, aspect, previousSplit, split);
            cascades[i] = BuildCascadeLightMatrix(corners, lightDir, up, i);
            splits[i] = split;
            previousSplit = split;
        }

        for (int i = cascadeCount; i < MaxDirectionalShadowCascades; i++)
        {
            cascades[i] = cascades[cascadeCount - 1];
            splits[i] = farClip;
        }
    }

private Matrix4 BuildCascadeLightMatrix(Vector3[] corners, Vector3 lightDir, Vector3 up, int cascadeIndex)
    {
        Vector3 center = Vector3.Zero;
        foreach (var corner in corners)
            center += corner;
        center /= corners.Length;

        float radius = 0f;
        foreach (var corner in corners)
            radius = MathF.Max(radius, (corner - center).Length);
        radius = MathF.Ceiling(radius * 16f) / 16f;

        float padding = _shadowQuality == ShadowQuality.Ultra ? 1.08f : 1.15f;
        float extent = MathF.Max(1f, radius * 2f * padding);
        var lightPos = center - lightDir * extent;
        Matrix4 lightView = Matrix4.LookAt(lightPos, center, up);
        SnapShadowCameraToTexels(ref center, ref lightPos, ref lightView, lightDir, up, extent, _directionalShadowSize);

        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        foreach (var corner in corners)
        {
            var ls = Vector4.TransformRow(new Vector4(corner, 1f), lightView);
            minZ = MathF.Min(minZ, ls.Z);
            maxZ = MathF.Max(maxZ, ls.Z);
        }

        float depthPadding = MathF.Max(20f, extent * (cascadeIndex == 0 ? 1.2f : 1.8f));
        Matrix4 lightProjection = Matrix4.CreateOrthographic(
            extent,
            extent,
            MathF.Max(0.01f, -maxZ - depthPadding),
            MathF.Max(0.02f, -minZ + depthPadding));
        return lightView * lightProjection;
    }

private ShadowInfo PrepareSpotShadow(IReadOnlyList<GameObject> objects)
    {
        var spot = FindShadowCaster<SpotLight>(objects, s => s.Shadows && s.gameObject != null);
        if (spot?.gameObject == null)
            return new ShadowInfo(false, Matrix4.Identity, 0f);

        var pos = ComputeWorldPosition(spot.gameObject);
        var dir = ResolveSpotDirection(spot);
        var up = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.92f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4 view = Matrix4.LookAt(pos, pos + dir, up);
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(Math.Clamp(spot.Angle, 1f, 170f)),
            1f,
            0.05f,
            Math.Max(0.1f, spot.Range));
        Matrix4 lightMvp = view * projection;
        RenderDepthMap(_spotShadowFbo, _spotShadowSize, _shadowShader, _shadowMvpLocation, _shadowModelLocation, lightMvp);
        return new ShadowInfo(true, lightMvp, Math.Clamp(spot.ShadowStrength, 0f, 1f));
    }

private PointShadowInfo PreparePointShadow(IReadOnlyList<GameObject> objects)
    {
        var point = FindShadowCaster<PointLight>(objects, p => p.Shadows && p.gameObject != null);
        if (point?.gameObject == null)
            return new PointShadowInfo(false, Vector3.Zero, 1f, 0f);

        var pos = ComputeWorldPosition(point.gameObject);
        float farPlane = Math.Max(0.1f, point.Range);
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1f, 0.05f, farPlane);
        (TextureTarget Face, Vector3 Dir, Vector3 Up)[] faces =
        {
            (TextureTarget.TextureCubeMapPositiveX,  Vector3.UnitX,  -Vector3.UnitY),
            (TextureTarget.TextureCubeMapNegativeX, -Vector3.UnitX,  -Vector3.UnitY),
            (TextureTarget.TextureCubeMapPositiveY,  Vector3.UnitY,   Vector3.UnitZ),
            (TextureTarget.TextureCubeMapNegativeY, -Vector3.UnitY,  -Vector3.UnitZ),
            (TextureTarget.TextureCubeMapPositiveZ,  Vector3.UnitZ,  -Vector3.UnitY),
            (TextureTarget.TextureCubeMapNegativeZ, -Vector3.UnitZ,  -Vector3.UnitY)
        };

        GL.GetInteger(GetPName.FramebufferBinding, out int previousFramebuffer);
        int[] previousViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, previousViewport);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pointShadowFbo);
        GL.Viewport(0, 0, _pointShadowSize, _pointShadowSize);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(1.5f, 2.5f);
        GL.ColorMask(false, false, false, false);
        GL.UseProgram(_pointShadowShader);
        GL.Uniform3(_pointShadowLightPosLocation, pos);
        GL.Uniform1(_pointShadowFarLocation, farPlane);

        foreach (var face in faces)
        {
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, face.Face, _pointShadowCube, 0);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            Matrix4 view = Matrix4.LookAt(pos, pos + face.Dir, face.Up);
            Matrix4 mvp = view * projection;
            GL.UniformMatrix4(_pointShadowMvpLocation, true, ref mvp);
            DrawDepthGeometry(_pointShadowModelLocation);
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.ColorMask(true, true, true, true);
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
        GL.Viewport(previousViewport[0], previousViewport[1], previousViewport[2], previousViewport[3]);

        return new PointShadowInfo(true, pos, farPlane, Math.Clamp(point.ShadowStrength, 0f, 1f));
    }

private void RenderDepthMap(int framebuffer, int size, int program, int mvpLocation, int modelLocation, Matrix4 mvp)
    {
        GL.GetInteger(GetPName.FramebufferBinding, out int previousFramebuffer);
        int[] previousViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, previousViewport);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.Viewport(0, 0, size, size);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Front);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(1.5f, 2.5f);
        GL.ColorMask(false, false, false, false);
        GL.UseProgram(program);
        GL.UniformMatrix4(mvpLocation, true, ref mvp);
        DrawDepthGeometry(modelLocation);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.ColorMask(true, true, true, true);
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.CullFace(TriangleFace.Back);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
        GL.Viewport(previousViewport[0], previousViewport[1], previousViewport[2], previousViewport[3]);
    }

private void DrawDepthGeometry(int modelLocation)
    {
        var identity = Matrix4.Identity;
        SetMatrixUniform(modelLocation, ref identity);
        SetDepthInstancing(modelLocation, false);
        SetDepthSkinning(modelLocation, false);
        if (_staticVertexCount > 0)
        {
            GL.BindVertexArray(_staticVertexArray);
            TrackShadowDraw();
            GL.DrawArrays(PrimitiveType.Triangles, 0, _staticVertexCount);
        }
        if (solidVertices.Count > 0)
        {
            GL.BindVertexArray(solidVertexArray);
            TrackShadowDraw();
            GL.DrawArrays(PrimitiveType.Triangles, 0, solidVertices.Count);
        }

        int boundVao = 0;
        for (int i = 0; i < dynamicMeshDraws.Count;)
        {
            var draw = dynamicMeshDraws[i];
            if (!draw.CastShadows)
            {
                i++;
                continue;
            }

            int end = i + 1;
            while (end < dynamicMeshDraws.Count && CanDepthInstanceTogether(draw, dynamicMeshDraws[end]))
                end++;

            var world = draw.World;
            if (boundVao != draw.Mesh.Vao)
            {
                GL.BindVertexArray(draw.Mesh.Vao);
                boundVao = draw.Mesh.Vao;
            }

            int instanceCount = end - i;
            if (instanceCount > 1)
            {
                UploadInstanceMatrices(i, instanceCount);
                SetDepthInstancing(modelLocation, true);
                TrackShadowDraw();
                GL.DrawArraysInstanced(PrimitiveType.Triangles, draw.Start, draw.Count, instanceCount);
                SetDepthInstancing(modelLocation, false);
            }
            else
            {
                SetMatrixUniform(modelLocation, ref world);
                SetDepthInstancing(modelLocation, false);
                TrackShadowDraw();
                GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
            }

            i = end;
        }

        foreach (var draw in shaderGraphDynamicMeshDraws)
        {
            var world = draw.World;
            SetMatrixUniform(modelLocation, ref world);
            SetDepthInstancing(modelLocation, false);
            SetDepthSkinning(modelLocation, false);
            GL.BindVertexArray(draw.Mesh.Vao);
            TrackShadowDraw();
            GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
        }

        foreach (var draw in skinnedMeshDraws)
        {
            var world = draw.World;
            SetMatrixUniform(modelLocation, ref world);
            SetDepthInstancing(modelLocation, false);
            SetDepthSkinning(modelLocation, true);
            UploadBoneMatrices(GetDepthBoneLocations(modelLocation), draw.Skin);
            GL.BindVertexArray(draw.Mesh.Vao);
            TrackShadowDraw();
            GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
        }

        foreach (var draw in shaderGraphSkinnedMeshDraws)
        {
            var world = draw.World;
            SetMatrixUniform(modelLocation, ref world);
            SetDepthInstancing(modelLocation, false);
            SetDepthSkinning(modelLocation, true);
            UploadBoneMatrices(GetDepthBoneLocations(modelLocation), draw.Skin);
            GL.BindVertexArray(draw.Mesh.Vao);
            TrackShadowDraw();
            GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
        }

        SetMatrixUniform(modelLocation, ref identity);
        SetDepthInstancing(modelLocation, false);
        SetDepthSkinning(modelLocation, false);
    }

private void SetDepthInstancing(int modelLocation, bool enabled)
    {
        int location = modelLocation == _pointShadowModelLocation
            ? _pointShadowUseInstancingLocation
            : _shadowUseInstancingLocation;
        SetIntUniform(location, enabled ? 1 : 0);
    }

private void SetDepthSkinning(int modelLocation, bool enabled)
    {
        int location = modelLocation == _pointShadowModelLocation
            ? _pointShadowUseSkinningLocation
            : _shadowUseSkinningLocation;
        SetIntUniform(location, enabled ? 1 : 0);
    }

private int[] GetDepthBoneLocations(int modelLocation) =>
        modelLocation == _pointShadowModelLocation ? _pointShadowBoneMatrixLocations : _shadowBoneMatrixLocations;

private void ApplyShadowUniforms(ShadowInfo shadow, ShadowInfo spotShadow, PointShadowInfo pointShadow)
    {
        var lightMvp = shadow.LightMvp;
        GL.UniformMatrix4(solidLightMvpLocation, true, ref lightMvp);
        GL.Uniform1(solidCascadeCountLocation, shadow.CascadeCount);
        GL.Uniform3(solidCameraPositionLocation, shadow.CameraPosition);
        for (int i = 0; i < MaxDirectionalShadowCascades; i++)
        {
            var cascadeMvp = shadow.Cascades[i];
            GL.UniformMatrix4(solidCascadeLightMvpLocations[i], true, ref cascadeMvp);
            GL.Uniform1(solidCascadeSplitLocations[i], shadow.Splits[i]);
        }
        GL.Uniform1(solidShadowEnabledLocation, shadow.Enabled ? 1 : 0);
        GL.Uniform1(solidShadowStrengthLocation, shadow.Strength);
        GL.Uniform1(solidShadowPcfRadiusLocation, ShadowPcfRadiusFor(_shadowQuality));
        GL.Uniform1(solidShadowBiasScaleLocation, Math.Clamp(ShadowBias, 0.1f, 4f));
        GL.Uniform1(solidShadowMapLocation, 1);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2DArray, shadow.Enabled ? _shadowArrayTex : 0);
        var spotMvp = spotShadow.LightMvp;
        GL.UniformMatrix4(solidSpotLightMvpLocation, true, ref spotMvp);
        GL.Uniform1(solidSpotShadowEnabledLocation, spotShadow.Enabled ? 1 : 0);
        GL.Uniform1(solidSpotShadowStrengthLocation, spotShadow.Strength);
        GL.Uniform1(solidSpotShadowMapLocation, 5);
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2D, spotShadow.Enabled ? _spotShadowTex : 0);
        GL.Uniform1(solidPointShadowEnabledLocation, pointShadow.Enabled ? 1 : 0);
        GL.Uniform1(solidPointShadowStrengthLocation, pointShadow.Strength);
        GL.Uniform3(solidPointShadowPosLocation, pointShadow.Position);
        GL.Uniform1(solidPointShadowFarLocation, pointShadow.FarPlane);
        GL.Uniform1(solidPointShadowCubeLocation, 6);
        GL.ActiveTexture(TextureUnit.Texture6);
        GL.BindTexture(TextureTarget.TextureCubeMap, pointShadow.Enabled ? _pointShadowCube : 0);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

private (Vector3 Center, float Radius) EstimateSceneBounds(IReadOnlyList<GameObject> objects)
    {
        Vector3 casterSum = Vector3.Zero;
        int casterCount = 0;
        Vector3 receiverSum = Vector3.Zero;
        int receiverCount = 0;
        var positions = new List<Vector3>();
        var receiverPositions = new List<Vector3>();
        foreach (var obj in FlattenActive(objects))
        {
            // Solo objetos con geometría renderizable (cubo/plano/malla) definen el área que
            // recibe/proyecta sombra. Antes se incluía CUALQUIER GameObject activo —luces,
            // cámaras, objetos vacíos—, así que con solo añadir una luz lejos del resto de la
            // escena el centro/radio estimados se desplazaban, el frustum ortográfico de la
            // sombra direccional dejaba de cubrir la geometría real y la sombra "desaparecía"
            // (aunque la luz original siguiera con Shadows = true). Ver también el bug reportado:
            // activar/desactivar "Shadows" en la luz nueva no cambiaba nada — porque el problema
            // no era qué luz proyectaba sombra, sino dónde se colocaba la cámara de sombra.
            if (!HasShadowGeometry(obj)) continue;

            var (p, r) = EstimateObjectBound(obj);
            if (IsLargeShadowReceiver(obj))
            {
                r = MathF.Min(r, 6f);
                AddShadowBoundPoints(receiverPositions, p, r);
                receiverSum += p;
                receiverCount++;
                continue;
            }

            AddShadowBoundPoints(positions, p, r);
            casterSum += p;
            casterCount++;
        }

        bool hasCasters = casterCount > 0;
        if (!hasCasters && receiverCount == 0) return (Vector3.Zero, 12f);

        var center = hasCasters ? casterSum / casterCount : receiverSum / receiverCount;
        var bounds = hasCasters ? positions : receiverPositions;
        float radius = hasCasters ? 4f : 8f;
        foreach (var p in bounds)
            radius = MathF.Max(radius, (p - center).Length + 2.5f);
        return (center, radius);
    }

private static void AddShadowBoundPoints(List<Vector3> points, Vector3 center, float radius)
    {
        points.Add(center + new Vector3(radius, 0f, 0f));
        points.Add(center - new Vector3(radius, 0f, 0f));
        points.Add(center + new Vector3(0f, radius, 0f));
        points.Add(center - new Vector3(0f, radius, 0f));
        points.Add(center + new Vector3(0f, 0f, radius));
        points.Add(center - new Vector3(0f, 0f, radius));
    }

private static bool IsLargeShadowReceiver(GameObject obj) =>
        obj.Type == 2 || obj.GetComponent<Terrain>() != null;

private bool HasShadowGeometry(GameObject obj) =>
    obj.Type == 1 ||
    obj.Type == 2 ||
    obj.Type == 3 ||
    obj.Type == 4 ||
    obj.Type == 5 ||
    obj.Type == 6 ||
    (obj.GetComponent<MeshFilter>() is { } mf && !string.IsNullOrWhiteSpace(mf.MeshPath));
private static IEnumerable<GameObject> FlattenActive(IEnumerable<GameObject> objects)
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;
            yield return obj;
            foreach (var child in FlattenActive(obj.Children))
                yield return child;
        }
    }

private static Vector3 ForwardFromGameObject(GameObject obj)
    {
        float yaw = MathHelper.DegreesToRadians(90f - obj.RotY);
        float pitch = MathHelper.DegreesToRadians(-obj.RotX);
        return new Vector3(
            MathF.Cos(yaw) * MathF.Cos(pitch),
            MathF.Sin(pitch),
            MathF.Sin(yaw) * MathF.Cos(pitch)).Normalized();
    }

private static Vector3 ResolveSpotDirection(SpotLight spot)
    {
        var local = ToTk(spot.Direction);
        if (local.LengthSquared < 0.0001f)
            local = Vector3.UnitZ;
        local.Normalize();

        if (spot.gameObject == null)
            return local;

        Matrix4 rotation =
            Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(spot.gameObject.RotZ)) *
            Matrix4.CreateRotationX(MathHelper.DegreesToRadians(spot.gameObject.RotX)) *
            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(spot.gameObject.RotY));
        var world = Vector3.TransformNormal(local, rotation);
        return world.LengthSquared < 0.0001f ? new Vector3(0f, -1f, 0f) : world.Normalized();
    }

private static DirectionalLight? FindPrimaryDirectionalLight(IReadOnlyList<GameObject> objects) =>
        FindShadowCaster<DirectionalLight>(objects, d => d.Shadows) ?? FindComponent<DirectionalLight>(objects);

private void EnsureShadowResources()
    {
        var (dirSize, spotSize, pointSize) = ShadowMapSizesFor(_shadowQuality);
        _directionalShadowSize = dirSize;
        _spotShadowSize = spotSize;
        _pointShadowSize = pointSize;
        _shadowResourcesDirty = false;
        _cachedRealtimeShadowsValid = false;
        _cachedSpotShadowFrame = int.MinValue;
        _cachedPointShadowFrame = int.MinValue;

        if (_shadowFbo == 0) _shadowFbo = GL.GenFramebuffer();
        if (_shadowArrayTex == 0) _shadowArrayTex = GL.GenTexture();
        if (_spotShadowFbo == 0) _spotShadowFbo = GL.GenFramebuffer();
        if (_spotShadowTex == 0) _spotShadowTex = GL.GenTexture();
        if (_pointShadowFbo == 0) _pointShadowFbo = GL.GenFramebuffer();
        if (_pointShadowCube == 0) _pointShadowCube = GL.GenTexture();

        float[] border = { 1f, 1f, 1f, 1f };
        GL.BindTexture(TextureTarget.Texture2DArray, _shadowArrayTex);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.DepthComponent24,
            _directionalShadowSize, _directionalShadowSize, MaxDirectionalShadowCascades, 0,
            OpenTK.Graphics.OpenGL4.PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, border);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, _shadowArrayTex, 0, 0);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Shadow framebuffer incomplete: {status}");

        GL.BindTexture(TextureTarget.Texture2D, _spotShadowTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24,
            _spotShadowSize, _spotShadowSize, 0, OpenTK.Graphics.OpenGL4.PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, border);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _spotShadowFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _spotShadowTex, 0);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Spot shadow framebuffer incomplete: {status}");

        GL.BindTexture(TextureTarget.TextureCubeMap, _pointShadowCube);
        for (int i = 0; i < 6; i++)
        {
            GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, PixelInternalFormat.DepthComponent24,
                _pointShadowSize, _pointShadowSize, 0, OpenTK.Graphics.OpenGL4.PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        }
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pointShadowFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.TextureCubeMapPositiveX, _pointShadowCube, 0);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Point shadow framebuffer incomplete: {status}");

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        GL.BindTexture(TextureTarget.TextureCubeMap, 0);
    }

private static int CreateShadowShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aPosition;
            layout (location = 6) in mat4 aInstanceModel;
            layout (location = 10) in vec4 aBoneIndices;
            layout (location = 11) in vec4 aBoneWeights;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            uniform int uUseInstancing;
            uniform int uUseSkinning;
            uniform mat4 uBones[128];
            void main()
            {
                mat4 model = uUseInstancing != 0 ? aInstanceModel : uModel;
                vec4 world = vec4(aPosition, 1.0) * model;
                if (uUseSkinning != 0)
                {
                    vec4 skinnedPos = vec4(0.0);
                    float totalWeight = 0.0;
                    for (int i = 0; i < 4; i++)
                    {
                        int boneIndex = int(aBoneIndices[i] + 0.5);
                        float weight = aBoneWeights[i];
                        if (boneIndex >= 0 && boneIndex < 128 && weight > 0.0)
                        {
                            skinnedPos += (vec4(aPosition, 1.0) * uBones[boneIndex]) * weight;
                            totalWeight += weight;
                        }
                    }
                    if (totalWeight > 0.0001)
                        world = skinnedPos;
                }
                gl_Position = world * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            void main() { }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }

private static int CreatePointShadowShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aPosition;
            layout (location = 6) in mat4 aInstanceModel;
            layout (location = 10) in vec4 aBoneIndices;
            layout (location = 11) in vec4 aBoneWeights;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            uniform int uUseInstancing;
            uniform int uUseSkinning;
            uniform mat4 uBones[128];
            out vec3 vWorldPos;
            void main()
            {
                mat4 model = uUseInstancing != 0 ? aInstanceModel : uModel;
                vec4 world = vec4(aPosition, 1.0) * model;
                if (uUseSkinning != 0)
                {
                    vec4 skinnedPos = vec4(0.0);
                    float totalWeight = 0.0;
                    for (int i = 0; i < 4; i++)
                    {
                        int boneIndex = int(aBoneIndices[i] + 0.5);
                        float weight = aBoneWeights[i];
                        if (boneIndex >= 0 && boneIndex < 128 && weight > 0.0)
                        {
                            skinnedPos += (vec4(aPosition, 1.0) * uBones[boneIndex]) * weight;
                            totalWeight += weight;
                        }
                    }
                    if (totalWeight > 0.0001)
                        world = skinnedPos;
                }
                vWorldPos = world.xyz;
                gl_Position = world * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec3 vWorldPos;
            uniform vec3 uLightPos;
            uniform float uFarPlane;
            void main()
            {
                gl_FragDepth = length(vWorldPos - uLightPos) / max(uFarPlane, 0.001);
            }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }

private void CaptureDepthBuffer()
    {
        GL.GetInteger(GetPName.ReadFramebufferBinding, out int previousReadFramebuffer);
        GL.GetInteger(GetPName.DrawFramebufferBinding, out int previousDrawFramebuffer);

        int w = 0, h = 0;
        int[] vp = new int[4];
        GL.GetInteger(GetPName.Viewport, vp);
        w = vp[2]; h = vp[3];
        if (w <= 0 || h <= 0) return;

        // Redimensionar depth texture si hace falta
        if (w != _depthFboWidth || h != _depthFboHeight)
        {
            _depthFboWidth = w;
            _depthFboHeight = h;
            GL.BindTexture(TextureTarget.Texture2D, _depthTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24,
                w, h, 0, OpenTK.Graphics.OpenGL4.PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _depthFbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, _depthTex, 0);
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousReadFramebuffer);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDrawFramebuffer);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        // Copiar depth del framebuffer principal → nuestro FBO
        int sourceFramebuffer = previousDrawFramebuffer != 0 ? previousDrawFramebuffer : previousReadFramebuffer;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _depthFbo);
        GL.BlitFramebuffer(0, 0, w, h, 0, 0, w, h,
            ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousReadFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDrawFramebuffer);
    }

    private readonly struct ShadowInfo
    {
        public static readonly ShadowInfo Disabled = new(false, Matrix4.Identity, 0f);

        public readonly bool Enabled;
        public readonly Matrix4 LightMvp;
        public readonly float Strength;
        public readonly int CascadeCount;
        public readonly Matrix4[] Cascades;
        public readonly float[] Splits;
        public readonly Vector3 CameraPosition;

        public ShadowInfo(bool enabled, Matrix4 lightMvp, float strength)
            : this(enabled, lightMvp, strength, 1, new[] { lightMvp, lightMvp, lightMvp, lightMvp, lightMvp }, new float[MaxDirectionalShadowCascades], Vector3.Zero)
        {
        }

        public ShadowInfo(bool enabled, Matrix4 lightMvp, float strength, int cascadeCount, Matrix4[] cascades, float[] splits, Vector3 cameraPosition)
        {
            Enabled = enabled;
            LightMvp = lightMvp;
            Strength = strength;
            CascadeCount = Math.Clamp(cascadeCount, 0, MaxDirectionalShadowCascades);
            Cascades = cascades.Length >= MaxDirectionalShadowCascades ? cascades : new[] { lightMvp, lightMvp, lightMvp, lightMvp, lightMvp };
            Splits = splits.Length >= MaxDirectionalShadowCascades ? splits : new float[MaxDirectionalShadowCascades];
            CameraPosition = cameraPosition;
        }
    }

    private readonly record struct PointShadowInfo(bool Enabled, Vector3 Position, float FarPlane, float Strength);
}
