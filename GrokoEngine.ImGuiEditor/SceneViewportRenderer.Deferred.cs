using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private int _deferredWidth;
    private int _deferredHeight;
    private int _deferredSamples = 1;
    private int _gbufferFbo;
    private int _gbufferAlbedo;
    private int _gbufferNormalRoughness;
    private int _gbufferMaterial;
    private int _gbufferEmission;
    private int _gbufferShadowmask;
    private int _gbufferDepth;
    // En Deferred se dibuja el G-buffer multisample y se resuelve a las texturas
    // normales antes de iluminar. Un sampler2D no puede leer directamente un MSAA
    // renderbuffer, pero resolverlo aquí conserva el coverage de los bordes.
    private int _gbufferMsaaFbo;
    private int _gbufferMsaaAlbedo;
    private int _gbufferMsaaNormalRoughness;
    private int _gbufferMsaaMaterial;
    private int _gbufferMsaaEmission;
    private int _gbufferMsaaShadowmask;
    private int _gbufferMsaaDepth;
    private int _gbufferShader;
    private int _deferredLightingShader;
    private int _deferredQuadVao;
    private int _deferredQuadVbo;

    private int _gbMvpLoc, _gbModelLoc, _gbUseInstancingLoc, _gbUseSkinningLoc, _gbUseSurfaceUniformsLoc;
    private int _gbSurfaceColorLoc, _gbSurfaceMaterialLoc, _gbSurfaceEmissionLoc;
    private int _gbTextureLoc, _gbHasTextureLoc, _gbNormalMapLoc, _gbHasNormalMapLoc, _gbRoughnessMapLoc, _gbHasRoughnessMapLoc, _gbMetallicMapLoc, _gbHasMetallicMapLoc, _gbLightmapLoc, _gbHasLightmapLoc, _gbLightmapIntensityLoc, _gbLightmapScaleOffsetLoc, _gbShadowmaskLoc, _gbHasShadowmaskLoc;
    private int _gbAlphaClipEnabledLoc, _gbAlphaClipThresholdLoc, _gbColorSpaceLinearLoc;
    private readonly int[] _gbBoneLocs = new int[MaxGpuBones];

    private int _dlAlbedoLoc, _dlNormalLoc, _dlMaterialLoc, _dlEmissionLoc, _dlShadowmaskLoc, _dlDepthLoc;
    private int _dlInvViewProjLoc, _dlCameraPosLoc, _dlAmbientColorLoc, _dlAmbientIntensityLoc, _dlSkyStrengthLoc;
    private int _dlDirDirLoc, _dlDirColorLoc, _dlDirIntensityLoc;
    private int _dlDirShadowmaskChannelLoc;
    private int _dlMixedLightingModeLoc, _dlDistanceShadowmaskLoc, _dlShadowmaskDistanceLoc, _dlShadowmaskDistanceFadeLoc;
    private int _dlPointCountLoc, _dlSpotCountLoc, _dlAreaCountLoc;
    private readonly int[] _dlPointPosLoc = new int[MaxPointLights];
    private readonly int[] _dlPointColorLoc = new int[MaxPointLights];
    private readonly int[] _dlPointIntensityLoc = new int[MaxPointLights];
    private readonly int[] _dlPointRangeLoc = new int[MaxPointLights];
    private readonly int[] _dlPointShadowmaskChannelLoc = new int[MaxPointLights];
    private readonly int[] _dlSpotPosLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotDirLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotColorLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotIntensityLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotRangeLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotAngleLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotShadowmaskChannelLoc = new int[MaxSpotLights];
    private readonly int[] _dlAreaPosLoc = new int[MaxAreaLights];
    private readonly int[] _dlAreaDirLoc = new int[MaxAreaLights];
    private readonly int[] _dlAreaColorLoc = new int[MaxAreaLights];
    private readonly int[] _dlAreaIntensityLoc = new int[MaxAreaLights];
    private readonly int[] _dlAreaRangeLoc = new int[MaxAreaLights];
    private readonly int[] _dlAreaSizeLoc = new int[MaxAreaLights];
    private int _dlUseIblLoc, _dlEnvMapLoc, _dlHasEnvMapLoc, _dlEnvMaxLodLoc;
    private int _dlEnvDiffuseColorLoc, _dlUseEnvShLoc, _dlEnvPrefilterMapLoc, _dlHasEnvPrefilterMapLoc, _dlEnvPrefilterMaxLodLoc;
    private readonly int[] _dlEnvShLoc = new int[9];
    private int _dlBrdfLutLoc, _dlHasBrdfLutLoc, _dlIblDiffuseStrengthLoc, _dlIblSpecularStrengthLoc;
    private int _dlActiveReflectionProbeMapLoc, _dlHasActiveReflectionProbeMapLoc, _dlActiveReflectionProbeMaxLodLoc;
    private int _dlActiveReflectionProbeCenterLoc, _dlActiveReflectionProbeHalfSizeLoc, _dlActiveReflectionProbeBlendDistanceLoc, _dlActiveReflectionProbeIntensityLoc, _dlActiveReflectionProbeBoxProjectionLoc;
    private int _dlSecondaryReflectionProbeMapLoc, _dlHasSecondaryReflectionProbeMapLoc, _dlSecondaryReflectionProbeMaxLodLoc;
    private int _dlSecondaryReflectionProbeCenterLoc, _dlSecondaryReflectionProbeHalfSizeLoc, _dlSecondaryReflectionProbeBlendDistanceLoc, _dlSecondaryReflectionProbeIntensityLoc, _dlSecondaryReflectionProbeBoxProjectionLoc;
    private int _dlShadowMapLoc, _dlShadowEnabledLoc, _dlShadowStrengthLoc, _dlShadowPcfRadiusLoc, _dlShadowBiasScaleLoc, _dlShadowNormalBiasLoc, _dlCascadeCountLoc, _dlShadowCameraPosLoc;
    // Deferred comparte los mapas ya generados por el sistema de sombras. Los spots usan un
    // atlas; la point local seleccionada por importancia usa un cubemap, manteniendo el coste
    // estable sin depender del orden de la jerarquía.
    private int _dlSpotShadowMapLoc;
    private readonly int[] _dlSpotLightMvpLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotShadowEnabledLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotShadowStrengthLoc = new int[MaxSpotLights];
    private readonly int[] _dlSpotShadowSlotLoc = new int[MaxSpotLights];
    private int _dlPointShadowCubeLoc, _dlPointShadowEnabledLoc, _dlPointShadowStrengthLoc, _dlPointShadowPosLoc, _dlPointShadowFarLoc, _dlPointShadowTexelSizeLoc, _dlPointShadowLightIndexLoc;

    // Bias de sombra efectivo (escala del shader). Toma el Bias del DirectionalLight primario del
    // frame (Unity: 0.05 → escala 1.0); si no hay luz, usa el bias global del renderer.
    private float EffectiveShadowBiasScale() =>
        _frameDir != null
            ? Math.Clamp(_frameDir.ShadowBias * 20f, 0.1f, 4f)
            : Math.Clamp(ShadowBias, 0.1f, 4f);
    private readonly int[] _dlCascadeLightMvpLoc = new int[MaxDirectionalShadowCascades];
    private readonly int[] _dlCascadeSplitLoc = new int[MaxDirectionalShadowCascades];
    private int _dlFogDensityLoc, _dlFogColorLoc, _dlFogHeightLoc, _dlFogHeightFalloffLoc, _dlVolumetricStrengthLoc, _dlDebugViewLoc;

    private bool UseDeferredOpaquePath =>
        RenderingPath == RendererRenderingPath.Deferred;

    private void EnsureDeferredResources(int width, int height, int requestedSamples)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        int samples = requestedSamples <= 1 ? 1 : requestedSamples <= 2 ? 2 : requestedSamples <= 4 ? 4 : 8;

        if (_gbufferShader == 0)
        {
            _gbufferShader = CreateDeferredGBufferShader();
            CacheDeferredGBufferUniforms();
        }

        if (_deferredLightingShader == 0)
        {
            _deferredLightingShader = CreateDeferredLightingShader();
            CacheDeferredLightingUniforms();
        }

        if (_deferredQuadVao == 0)
            CreateDeferredFullscreenQuad();

        if (_gbufferFbo != 0 && _deferredWidth == width && _deferredHeight == height && _deferredSamples == samples)
            return;

        ReleaseDeferredTargets();

        _deferredWidth = width;
        _deferredHeight = height;
        _deferredSamples = samples;
        _gbufferFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufferFbo);

        _gbufferAlbedo = CreateGBufferTexture(width, height, PixelInternalFormat.Rgba8);
        _gbufferNormalRoughness = CreateGBufferTexture(width, height, PixelInternalFormat.Rgba16f);
        _gbufferMaterial = CreateGBufferTexture(width, height, PixelInternalFormat.Rgba8);
        _gbufferEmission = CreateGBufferTexture(width, height, PixelInternalFormat.Rgba16f);
        _gbufferShadowmask = CreateGBufferTexture(width, height, PixelInternalFormat.Rgba8);
        _gbufferDepth = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _gbufferDepth);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, width, height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _gbufferAlbedo, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, _gbufferNormalRoughness, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2, TextureTarget.Texture2D, _gbufferMaterial, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3, TextureTarget.Texture2D, _gbufferEmission, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment4, TextureTarget.Texture2D, _gbufferShadowmask, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _gbufferDepth, 0);
        GL.DrawBuffers(5, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3, DrawBuffersEnum.ColorAttachment4 });

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Deferred G-buffer incompleto: {status}");

        if (samples > 1)
            CreateDeferredMsaaGBuffer(width, height, samples);
    }

    private void CreateDeferredMsaaGBuffer(int width, int height, int samples)
    {
        _gbufferMsaaFbo = GL.GenFramebuffer();
        _gbufferMsaaAlbedo = CreateDeferredMsaaRenderbuffer(width, height, samples, RenderbufferStorage.Rgba8);
        _gbufferMsaaNormalRoughness = CreateDeferredMsaaRenderbuffer(width, height, samples, RenderbufferStorage.Rgba16f);
        _gbufferMsaaMaterial = CreateDeferredMsaaRenderbuffer(width, height, samples, RenderbufferStorage.Rgba8);
        _gbufferMsaaEmission = CreateDeferredMsaaRenderbuffer(width, height, samples, RenderbufferStorage.Rgba16f);
        _gbufferMsaaShadowmask = CreateDeferredMsaaRenderbuffer(width, height, samples, RenderbufferStorage.Rgba8);
        _gbufferMsaaDepth = CreateDeferredMsaaRenderbuffer(width, height, samples, RenderbufferStorage.DepthComponent24);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufferMsaaFbo);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _gbufferMsaaAlbedo);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, RenderbufferTarget.Renderbuffer, _gbufferMsaaNormalRoughness);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2, RenderbufferTarget.Renderbuffer, _gbufferMsaaMaterial);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3, RenderbufferTarget.Renderbuffer, _gbufferMsaaEmission);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment4, RenderbufferTarget.Renderbuffer, _gbufferMsaaShadowmask);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _gbufferMsaaDepth);
        GL.DrawBuffers(5, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3, DrawBuffersEnum.ColorAttachment4 });

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Deferred MSAA G-buffer incompleto: {status}");
    }

    private static int CreateDeferredMsaaRenderbuffer(int width, int height, int samples, RenderbufferStorage format)
    {
        int renderbuffer = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, format, width, height);
        return renderbuffer;
    }

    private static int CreateGBufferTexture(int width, int height, PixelInternalFormat format)
    {
        int texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, texture);
        var type = format == PixelInternalFormat.Rgba8 ? PixelType.UnsignedByte : PixelType.Float;
        GL.TexImage2D(TextureTarget.Texture2D, 0, format, width, height, 0, PixelFormat.Rgba, type, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return texture;
    }

    private void RenderDeferredOpaque(
        ref Matrix4 mvp,
        ref Matrix4 view,
        ref Matrix4 projection,
        int width,
        int height,
        IReadOnlyList<GameObject> objects,
        Vector3 cameraPosition,
        ShadowInfo shadowInfo,
        ShadowInfo spotShadowInfo,
        PointShadowInfo pointShadowInfo,
        int targetFramebuffer,
        int targetSamples)
    {
        targetSamples = targetSamples <= 1 ? 1 : targetSamples <= 2 ? 2 : targetSamples <= 4 ? 4 : 8;
        EnsureDeferredResources(width, height, targetSamples);

        // glGet(FrameBufferBinding) fuerza sincronización con el driver en algunos
        // controladores. SceneRenderTarget ya conoce el FBO que acaba de enlazar; los
        // renderizados auxiliares sin ese dato conservan el fallback seguro.
        if (targetFramebuffer < 0)
            GL.GetInteger(GetPName.FramebufferBinding, out targetFramebuffer);

        long gBufferStart = RenderTimestamp();
        int geometryFramebuffer = _deferredSamples > 1 ? _gbufferMsaaFbo : _gbufferFbo;
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, geometryFramebuffer);
        GL.Viewport(0, 0, width, height);
        GL.DrawBuffers(4, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3 });
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);

        GL.UseProgram(_gbufferShader);
        GL.UniformMatrix4(_gbMvpLoc, true, ref mvp);
        GL.Uniform1(_gbTextureLoc, 0);
        GL.Uniform1(_gbNormalMapLoc, 2);
        GL.Uniform1(_gbRoughnessMapLoc, 3);
        GL.Uniform1(_gbMetallicMapLoc, 4);
        GL.Uniform1(_gbColorSpaceLinearLoc, ColorSpace == ColorSpace.Linear ? 1 : 0);
        DrawDeferredStaticRanges();
        DrawDeferredDynamicMeshes();
        DrawDeferredSkinnedMeshes();
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
        ResolveDeferredGBuffer(width, height);
        _statsDeferredGBufferMs = RenderElapsedMs(gBufferStart);

        long lightingStart = RenderTimestamp();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
        GL.DrawBuffer(targetFramebuffer == 0 ? DrawBufferMode.Back : DrawBufferMode.ColorAttachment0);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.Disable(EnableCap.Blend);

        GL.UseProgram(_deferredLightingShader);
        BindDeferredLightingUniforms(objects, cameraPosition, shadowInfo, spotShadowInfo, pointShadowInfo, ref mvp, ref view, ref projection);
        GL.BindVertexArray(_deferredQuadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
        _statsDeferredLightingMs = RenderElapsedMs(lightingStart);

        long depthCopyStart = RenderTimestamp();
        // Si el destino es MSAA, la profundidad debe conservar exactamente el mismo
        // número de muestras. Antes se intentaba copiar una profundidad simple a un
        // FBO multisample: OpenGL la rechaza y el MSAA de Deferred quedaba inactivo.
        int depthSourceFramebuffer = _deferredSamples > 1 ? _gbufferMsaaFbo : _gbufferFbo;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, depthSourceFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, targetFramebuffer);
        GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
        _statsDeferredDepthCopyMs = RenderElapsedMs(depthCopyStart);

        // Rebote difuso de color en pantalla. Se ejecuta antes de SSR para que los reflejos
        // puedan incluir la iluminación indirecta recién compuesta.
        long ssgiStart = RenderTimestamp();
        RenderScreenSpaceGlobalIllumination(ref mvp, cameraPosition, width, height, targetFramebuffer);
        _statsDeferredSsgiMs = RenderElapsedMs(ssgiStart);

        // SSR: reflejos de la escena real en pantalla sobre la geometría deferred ya iluminada.
        long ssrStart = RenderTimestamp();
        RenderScreenSpaceReflections(ref mvp, cameraPosition, width, height, targetFramebuffer);
        _statsDeferredSsrMs = RenderElapsedMs(ssrStart);
    }

    private void ResolveDeferredGBuffer(int width, int height)
    {
        if (_deferredSamples <= 1)
            return;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _gbufferMsaaFbo);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _gbufferFbo);
        ResolveDeferredGBufferColorAttachment(ReadBufferMode.ColorAttachment0, DrawBufferMode.ColorAttachment0, width, height);
        ResolveDeferredGBufferColorAttachment(ReadBufferMode.ColorAttachment1, DrawBufferMode.ColorAttachment1, width, height);
        ResolveDeferredGBufferColorAttachment(ReadBufferMode.ColorAttachment2, DrawBufferMode.ColorAttachment2, width, height);
        ResolveDeferredGBufferColorAttachment(ReadBufferMode.ColorAttachment3, DrawBufferMode.ColorAttachment3, width, height);
        ResolveDeferredGBufferColorAttachment(ReadBufferMode.ColorAttachment4, DrawBufferMode.ColorAttachment4, width, height);
        GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
    }

    private static void ResolveDeferredGBufferColorAttachment(ReadBufferMode readAttachment, DrawBufferMode drawAttachment, int width, int height)
    {
        GL.ReadBuffer(readAttachment);
        GL.DrawBuffer(drawAttachment);
        GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
    }

    private void DrawDeferredStaticRanges()
    {
        if (_staticVertexCount <= 0)
            return;

        ApplyDeferredBakedMode();
        GL.BindVertexArray(_staticVertexArray);
        GL.ActiveTexture(TextureUnit.Texture0);
        foreach (var range in _staticRanges)
        {
            // ShaderGraph sí se excluye (lo pinta el pase forward custom). El alpha-clip NO se
            // excluye: es opaco con recorte (discard), lo maneja el propio shader del G-buffer
            // (uAlphaClipEnabled), y omitirlo hacía que follaje/rejas desaparecieran en deferred.
            if (range.ShaderGraphPath != null)
                continue;
            ApplyDeferredRangeTextures(range);
            TrackMainDraw(range.Count);
            GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
        }
    }

    private void DrawDeferredDynamicMeshes()
    {
        if (dynamicMeshDraws.Count == 0)
            return;

        bool hasBoundSurface = false;
        string? boundTexture = null;
        string? boundNormal = null;
        string? boundRoughness = null;
        string? boundMetallic = null;
        int boundVao = 0;
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

            // Alpha-clip SÍ entra al G-buffer (opaco con discard). Se saltan solo transparentes,
            // refractivos y distorsión (van al pase forward), y los shadows-only.
            if (draw.ShadowsOnly || draw.Alpha < 0.999f || draw.RefractionStrength > 0.001f || draw.DistortionStrength > 0.001f)
            {
                i = end;
                continue;
            }

            ApplyDeferredDynamicSurfaceIfChanged(draw, ref boundColor, ref boundMaterial, ref boundEmission);
            ApplyDeferredSurfaceTexturesIfChanged(draw.TexturePath, draw.NormalMapPath, draw.RoughnessMapPath, draw.MetallicMapPath, ref hasBoundSurface, ref boundTexture, ref boundNormal, ref boundRoughness, ref boundMetallic);

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
                SetIntUniform(_gbUseInstancingLoc, 1);
                boundInstancing = 1;
                TrackMainDraw(draw.Count, instanceCount);
                GL.DrawArraysInstanced(PrimitiveType.Triangles, draw.Start, draw.Count, instanceCount);
            }
            else
            {
                Matrix4 world = draw.World;
                SetMatrixUniform(_gbModelLoc, ref world);
                if (boundInstancing != 0)
                {
                    SetIntUniform(_gbUseInstancingLoc, 0);
                    boundInstancing = 0;
                }
                TrackMainDraw(draw.Count);
                GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
            }

            i = end;
        }
    }

    private void DrawDeferredSkinnedMeshes()
    {
        if (skinnedMeshDraws.Count == 0)
            return;

        bool hasBoundSurface = false;
        string? boundTexture = null;
        string? boundNormal = null;
        string? boundRoughness = null;
        string? boundMetallic = null;
        int boundVao = 0;
        Vector4 boundColor = new(float.NaN);
        Vector4 boundMaterial = new(float.NaN);
        Vector4 boundEmission = new(float.NaN);

        SetIntUniform(_gbUseInstancingLoc, 0);
        SetIntUniform(_gbUseSkinningLoc, 1);
        foreach (var draw in skinnedMeshDraws)
        {
            // Alpha-clip SÍ entra al G-buffer (opaco con discard); solo se saltan transparentes.
            if (draw.ShadowsOnly || draw.Alpha < 0.999f)
                continue;

            ApplyDeferredSkinnedSurfaceIfChanged(draw, ref boundColor, ref boundMaterial, ref boundEmission);
            ApplyDeferredSurfaceTexturesIfChanged(draw.TexturePath, draw.NormalMapPath, draw.RoughnessMapPath, draw.MetallicMapPath, ref hasBoundSurface, ref boundTexture, ref boundNormal, ref boundRoughness, ref boundMetallic);
            UploadBoneMatrices(_gbBoneLocs, draw.Skin);

            if (boundVao != draw.Mesh.Vao)
            {
                GL.BindVertexArray(draw.Mesh.Vao);
                boundVao = draw.Mesh.Vao;
            }

            TrackMainDraw(draw.Count);
            GL.DrawArrays(PrimitiveType.Triangles, draw.Start, draw.Count);
        }
        SetIntUniform(_gbUseSkinningLoc, 0);
    }

    private void BindDeferredLightingUniforms(IReadOnlyList<GameObject> objects, Vector3 cameraPosition, ShadowInfo shadowInfo, ShadowInfo spotShadowInfo, PointShadowInfo pointShadowInfo, ref Matrix4 mvp, ref Matrix4 view, ref Matrix4 projection)
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferAlbedo);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferNormalRoughness);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferMaterial);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferEmission);
        // 0..15 ya están ocupadas por G-buffer base, sombras, IBL, probes, cookies
        // e IES. Shadowmask/depth se dejan en 16/17: nunca pueden ser sobrescritas
        // por ApplyDeferredShadowUniforms (la causa de texturas "transparentes").
        GL.ActiveTexture(TextureUnit.Texture16);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferShadowmask);
        GL.ActiveTexture(TextureUnit.Texture17);
        GL.BindTexture(TextureTarget.Texture2D, _gbufferDepth);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(_dlAlbedoLoc, 0);
        GL.Uniform1(_dlNormalLoc, 1);
        GL.Uniform1(_dlMaterialLoc, 2);
        GL.Uniform1(_dlEmissionLoc, 3);
        GL.Uniform1(_dlShadowmaskLoc, 16);
        GL.Uniform1(_dlDepthLoc, 17);

        Matrix4.Invert(mvp, out Matrix4 invViewProj);
        GL.UniformMatrix4(_dlInvViewProjLoc, true, ref invViewProj);
        GL.Uniform3(_dlCameraPosLoc, cameraPosition);
        GL.Uniform1(_dlMixedLightingModeLoc, (int)MixedLightingMode);
        GL.Uniform1(_dlDistanceShadowmaskLoc, DistanceShadowmask ? 1 : 0);
        GL.Uniform1(_dlShadowmaskDistanceLoc, Math.Clamp(ShadowmaskDistance, 1f, 500f));
        GL.Uniform1(_dlShadowmaskDistanceFadeLoc, Math.Clamp(ShadowmaskDistanceFade, 0.01f, Math.Max(ShadowmaskDistance, 0.01f)));

        CollectFrameLighting(objects, cameraPosition);
        var ambient = _frameAmbient;
        // Sin Ambient Light, HDRI ni cielo procedural no se inventa iluminación global.
        // Así una casa cerrada queda oscura al apagar sus luminarias, como en un motor físico.
        var ambColor = ambient != null ? new Vector3(ambient.R, ambient.G, ambient.B) : Vector3.Zero;
        float ambIntensity = ambient?.Intensity ?? 0f;
        if (_frameDir != null) ambIntensity *= _frameDir.IndirectMultiplier;
        GL.Uniform3(_dlAmbientColorLoc, ambColor);
        GL.Uniform1(_dlAmbientIntensityLoc, ambIntensity);
        GL.Uniform1(_dlSkyStrengthLoc, ambient?.SkyStrength ?? 0f);
        ApplyDeferredIblUniforms(cameraPosition);
        ApplyDeferredShadowUniforms(shadowInfo, spotShadowInfo, pointShadowInfo);

        var dir = _frameDir;
        if (dir != null && dir.gameObject != null)
        {
            GL.Uniform3(_dlDirDirLoc, ToTk(dir.GetNormalizedDirection()).Normalized());
            GL.Uniform3(_dlDirColorLoc, ToTk(dir.GetEffectiveColor()));
            GL.Uniform1(_dlDirIntensityLoc, dir.Intensity);
            GL.Uniform1(_dlDirShadowmaskChannelLoc, dir.Mode == LightMode.Mixed ? dir.ShadowmaskChannel : -1);
        }
        else
        {
            GL.Uniform3(_dlDirDirLoc, new Vector3(0f, -1f, 0f));
            GL.Uniform3(_dlDirColorLoc, Vector3.Zero);
            GL.Uniform1(_dlDirIntensityLoc, 0f);
            GL.Uniform1(_dlDirShadowmaskChannelLoc, -1);
        }

        GL.Uniform1(_dlPointCountLoc, _framePointLights.Count);
        for (int i = 0; i < _framePointLights.Count; i++)
        {
            var pl = _framePointLights[i];
            GL.Uniform3(_dlPointPosLoc[i], pl.gameObject != null ? ComputeWorldPosition(pl.gameObject) : Vector3.Zero);
            GL.Uniform3(_dlPointColorLoc[i], ToTk(pl.GetEffectiveColor()));
            GL.Uniform1(_dlPointIntensityLoc[i], pl.Intensity);
            GL.Uniform1(_dlPointRangeLoc[i], pl.Range);
            GL.Uniform1(_dlPointShadowmaskChannelLoc[i], pl.Mode == LightMode.Mixed ? pl.ShadowmaskChannel : -1);
        }

        GL.Uniform1(_dlSpotCountLoc, _frameSpotLights.Count);
        for (int i = 0; i < _frameSpotLights.Count; i++)
        {
            var sl = _frameSpotLights[i];
            GL.Uniform3(_dlSpotPosLoc[i], sl.gameObject != null ? ComputeWorldPosition(sl.gameObject) : Vector3.Zero);
            GL.Uniform3(_dlSpotDirLoc[i], ResolveSpotDirection(sl));
            GL.Uniform3(_dlSpotColorLoc[i], ToTk(sl.GetEffectiveColor()));
            GL.Uniform1(_dlSpotIntensityLoc[i], sl.Intensity);
            GL.Uniform1(_dlSpotRangeLoc[i], sl.Range);
            GL.Uniform1(_dlSpotAngleLoc[i], MathHelper.DegreesToRadians(sl.Angle));
            GL.Uniform1(_dlSpotShadowmaskChannelLoc[i], sl.Mode == LightMode.Mixed ? sl.ShadowmaskChannel : -1);
            int shadowSlot = -1;
            for (int slot = 0; slot < MaxSpotLights; slot++)
            {
                if (ReferenceEquals(_cachedSpotShadowLights[slot], sl))
                {
                    shadowSlot = slot;
                    break;
                }
            }
            GL.Uniform1(_dlSpotShadowSlotLoc[i], shadowSlot);
        }

        int areaCount = Math.Min(MaxAreaLights, _frameAreaLights.Count + _frameRectLights.Count);
        GL.Uniform1(_dlAreaCountLoc, areaCount);
        int areaIndex = 0;
        foreach (var area in _frameAreaLights)
        {
            if (areaIndex >= MaxAreaLights) break;
            UploadDeferredAreaLight(areaIndex++, area.gameObject, area.R, area.G, area.B, area.Intensity, area.Range, area.Width, area.Height);
        }
        foreach (var rect in _frameRectLights)
        {
            if (areaIndex >= MaxAreaLights) break;
            UploadDeferredAreaLight(areaIndex++, rect.gameObject, rect.R, rect.G, rect.B, rect.Intensity, rect.Range, rect.Width, rect.Height);
        }

        var pp = _framePostProcess;
        bool ppEnabled = pp?.Enabled ?? false;
        GL.Uniform1(_dlFogDensityLoc, ppEnabled && pp!.Fog ? pp.FogDensity : 0f);
        GL.Uniform3(_dlFogColorLoc, ppEnabled ? new Vector3(pp!.FogR, pp.FogG, pp.FogB) : new Vector3(0.48f, 0.58f, 0.68f));
        GL.Uniform1(_dlFogHeightLoc, ppEnabled && pp!.Fog ? pp.FogHeight : 0f);
        GL.Uniform1(_dlFogHeightFalloffLoc, ppEnabled && pp!.Fog ? Math.Clamp(pp.FogHeightFalloff, 0f, 4f) : 0f);
        GL.Uniform1(_dlVolumetricStrengthLoc, ppEnabled ? pp!.VolumetricLightStrength : 0f);
        GL.Uniform1(_dlDebugViewLoc, DebugView);
    }

    private void ApplyDeferredIblUniforms(Vector3 cameraPosition)
    {
        GL.Uniform1(_dlUseIblLoc, ImageBasedLighting ? 1 : 0);
        GL.Uniform1(_dlIblDiffuseStrengthLoc, Math.Clamp(IblDiffuseStrength, 0f, 1.5f));
        GL.Uniform1(_dlIblSpecularStrengthLoc, Math.Clamp(IblSpecularStrength, 0f, 0.35f));

        GL.ActiveTexture(TextureUnit.Texture9);
        GL.BindTexture(TextureTarget.Texture2D, _brdfLutTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(_dlBrdfLutLoc, 9);
        GL.Uniform1(_dlHasBrdfLutLoc, _brdfLutTexture != 0 ? 1 : 0);

        ApplyDeferredActiveReflectionProbe(cameraPosition);

        if (_envLoaded)
        {
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.Texture2D, _envTexture);
            GL.ActiveTexture(TextureUnit.Texture8);
            GL.BindTexture(TextureTarget.TextureCubeMap, _envPrefilterCube);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(_dlEnvMapLoc, 7);
            GL.Uniform1(_dlHasEnvMapLoc, 1);
            GL.Uniform1(_dlEnvMaxLodLoc, _envMaxLod);
            GL.Uniform3(_dlEnvDiffuseColorLoc, _envDiffuseColor);
            GL.Uniform1(_dlUseEnvShLoc, 1);
            for (int i = 0; i < _envSh.Length; i++)
                GL.Uniform3(_dlEnvShLoc[i], _envSh[i]);
            GL.Uniform1(_dlEnvPrefilterMapLoc, 8);
            GL.Uniform1(_dlHasEnvPrefilterMapLoc, _envPrefilterCube != 0 ? 1 : 0);
            GL.Uniform1(_dlEnvPrefilterMaxLodLoc, _envPrefilterMaxLod);
        }
        else
        {
            if (EnvironmentMode == EnvironmentMode.ProceduralSky)
                UpdateProceduralSkyAmbient(_frameDir);
            else
                _flatAmbientColor = Vector3.Zero;

            GL.Uniform1(_dlHasEnvMapLoc, 0);
            GL.Uniform3(_dlEnvDiffuseColorLoc, _flatAmbientColor);
            GL.Uniform1(_dlUseEnvShLoc, 0);
            GL.Uniform1(_dlHasEnvPrefilterMapLoc, 0);
            GL.Uniform1(_dlEnvPrefilterMaxLodLoc, 0f);
        }
    }

    private void ApplyDeferredActiveReflectionProbe(Vector3 cameraPosition)
    {
        _activeReflectionProbeCubemapPath = null;
        _activeReflectionProbeTexture = 0;
        _activeReflectionProbeMaxLod = 0f;
        var (primary, secondary) = SelectReflectionProbes(cameraPosition);
        if (primary is { } probe)
        {
            _activeReflectionProbeCubemapPath = NormalizeExistingAssetPath(probe.Probe.EffectiveCubemapPath) ?? probe.Probe.EffectiveCubemapPath;
            _activeReflectionProbeTexture = probe.Texture;
            _activeReflectionProbeMaxLod = probe.MaxLod;
        }

        UploadReflectionProbe(primary, 10,
            _dlActiveReflectionProbeMapLoc, _dlHasActiveReflectionProbeMapLoc, _dlActiveReflectionProbeMaxLodLoc,
            _dlActiveReflectionProbeCenterLoc, _dlActiveReflectionProbeHalfSizeLoc, _dlActiveReflectionProbeBlendDistanceLoc,
            _dlActiveReflectionProbeIntensityLoc, _dlActiveReflectionProbeBoxProjectionLoc);
        UploadReflectionProbe(secondary, 14,
            _dlSecondaryReflectionProbeMapLoc, _dlHasSecondaryReflectionProbeMapLoc, _dlSecondaryReflectionProbeMaxLodLoc,
            _dlSecondaryReflectionProbeCenterLoc, _dlSecondaryReflectionProbeHalfSizeLoc, _dlSecondaryReflectionProbeBlendDistanceLoc,
            _dlSecondaryReflectionProbeIntensityLoc, _dlSecondaryReflectionProbeBoxProjectionLoc);
    }

    private void ApplyDeferredShadowUniforms(ShadowInfo shadow, ShadowInfo spotShadow, PointShadowInfo pointShadow)
    {
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2DArray, shadow.Enabled ? _shadowArrayTex : 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(_dlShadowMapLoc, 5);
        GL.Uniform1(_dlShadowEnabledLoc, shadow.Enabled ? 1 : 0);
        GL.Uniform1(_dlShadowStrengthLoc, shadow.Strength);
        GL.Uniform1(_dlShadowPcfRadiusLoc, ShadowPcfRadiusFor(_shadowQuality));
        // Bias por-luz (estilo Unity): el Bias del DirectionalLight (0.05 por defecto) se mapea a la
        // escala del shader (1.0 = look actual). Si no hay luz, cae al bias global del renderer.
        GL.Uniform1(_dlShadowBiasScaleLoc, EffectiveShadowBiasScale());
        GL.Uniform1(_dlShadowNormalBiasLoc, _frameDir != null ? _frameDir.ShadowNormalBias : 0f);
        GL.Uniform1(_dlCascadeCountLoc, shadow.CascadeCount);
        GL.Uniform3(_dlShadowCameraPosLoc, shadow.CameraPosition);
        for (int i = 0; i < MaxDirectionalShadowCascades; i++)
        {
            var cascadeMvp = shadow.Cascades[i];
            GL.UniformMatrix4(_dlCascadeLightMvpLoc[i], true, ref cascadeMvp);
            GL.Uniform1(_dlCascadeSplitLoc[i], shadow.Splits[i]);
        }

        GL.ActiveTexture(TextureUnit.Texture15);
        GL.BindTexture(TextureTarget.Texture2DArray, _spotShadowArrayTex);
        GL.Uniform1(_dlSpotShadowMapLoc, 15);
        for (int i = 0; i < MaxSpotLights; i++)
        {
            var info = _cachedSpotShadows[i];
            var spotMvp = info.LightMvp;
            GL.UniformMatrix4(_dlSpotLightMvpLoc[i], true, ref spotMvp);
            GL.Uniform1(_dlSpotShadowEnabledLoc[i], info.Enabled ? 1 : 0);
            GL.Uniform1(_dlSpotShadowStrengthLoc[i], info.Strength);
        }

        GL.Uniform1(_dlPointShadowEnabledLoc, pointShadow.Enabled ? 1 : 0);
        GL.Uniform1(_dlPointShadowStrengthLoc, pointShadow.Strength);
        GL.Uniform3(_dlPointShadowPosLoc, pointShadow.Position);
        GL.Uniform1(_dlPointShadowFarLoc, pointShadow.FarPlane);
        GL.Uniform1(_dlPointShadowTexelSizeLoc, 1f / Math.Max(1, _pointShadowSize));
        GL.Uniform1(_dlPointShadowLightIndexLoc, ResolvePointShadowLightIndex(pointShadow));
        GL.ActiveTexture(TextureUnit.Texture6);
        GL.BindTexture(TextureTarget.TextureCubeMap, pointShadow.Enabled ? _pointShadowCube : 0);
        GL.Uniform1(_dlPointShadowCubeLoc, 6);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private void UploadDeferredAreaLight(int index, GameObject? obj, float r, float g, float b, float intensity, float range, float width, float height)
    {
        Vector3 pos = obj != null ? ComputeWorldPosition(obj) : Vector3.Zero;
        Vector3 dir = new(0f, -1f, 0f);
        GL.Uniform3(_dlAreaPosLoc[index], pos);
        GL.Uniform3(_dlAreaDirLoc[index], dir);
        GL.Uniform3(_dlAreaColorLoc[index], new Vector3(r, g, b));
        GL.Uniform1(_dlAreaIntensityLoc[index], intensity);
        GL.Uniform1(_dlAreaRangeLoc[index], range);
        GL.Uniform2(_dlAreaSizeLoc[index], new Vector2(width, height));
    }

    private void ApplyDeferredBakedMode()
    {
        Matrix4 identity = Matrix4.Identity;
        SetMatrixUniform(_gbModelLoc, ref identity);
        SetIntUniform(_gbUseInstancingLoc, 0);
        SetIntUniform(_gbUseSkinningLoc, 0);
        SetIntUniform(_gbUseSurfaceUniformsLoc, 0);
        ApplyDeferredAlphaClip(false, 0.5f);
    }

    private void ApplyDeferredRangeTextures(SolidRange range)
    {
        ApplyDeferredAlphaClip(range.AlphaClip, range.AlphaClipThreshold);
        ApplyDeferredSurfaceTextures(range.TexturePath, range.NormalMapPath, range.RoughnessMapPath, range.MetallicMapPath);
        BindDeferredOptionalTexture(TextureUnit.Texture5, _gbLightmapLoc, _gbHasLightmapLoc, range.LightmapPath);
        BindDeferredOptionalTexture(TextureUnit.Texture6, _gbShadowmaskLoc, _gbHasShadowmaskLoc, range.ShadowmaskPath);
        GL.Uniform1(_gbLightmapIntensityLoc, range.LightmapIntensity);
        GL.Uniform4(_gbLightmapScaleOffsetLoc, range.LightmapScaleOffset == default ? new Vector4(1f, 1f, 0f, 0f) : range.LightmapScaleOffset);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private void ApplyDeferredSurfaceTexturesIfChanged(string? texturePath, string? normalMapPath, string? roughnessMapPath, string? metallicMapPath, ref bool bound, ref string? boundTexture, ref string? boundNormal, ref string? boundRoughness, ref string? boundMetallic)
    {
        if (bound &&
            string.Equals(boundTexture, texturePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(boundNormal, normalMapPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(boundRoughness, roughnessMapPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(boundMetallic, metallicMapPath, StringComparison.OrdinalIgnoreCase))
            return;

        ApplyDeferredSurfaceTextures(texturePath, normalMapPath, roughnessMapPath, metallicMapPath);
        bound = true;
        boundTexture = texturePath;
        boundNormal = normalMapPath;
        boundRoughness = roughnessMapPath;
        boundMetallic = metallicMapPath;
    }

    private void ApplyDeferredSurfaceTextures(string? texturePath, string? normalMapPath, string? roughnessMapPath, string? metallicMapPath)
    {
        BindDeferredOptionalTexture(TextureUnit.Texture0, _gbTextureLoc, _gbHasTextureLoc, texturePath);
        BindDeferredOptionalTexture(TextureUnit.Texture2, _gbNormalMapLoc, _gbHasNormalMapLoc, normalMapPath);
        BindDeferredOptionalTexture(TextureUnit.Texture3, _gbRoughnessMapLoc, _gbHasRoughnessMapLoc, roughnessMapPath);
        BindDeferredOptionalTexture(TextureUnit.Texture4, _gbMetallicMapLoc, _gbHasMetallicMapLoc, metallicMapPath);
        // Los draws dinámicos no llevan UV de lightmap asignado; se desactiva explícitamente
        // para que no hereden el mapa del último rango estático.
        BindDeferredOptionalTexture(TextureUnit.Texture5, _gbLightmapLoc, _gbHasLightmapLoc, null);
        BindDeferredOptionalTexture(TextureUnit.Texture6, _gbShadowmaskLoc, _gbHasShadowmaskLoc, null);
        GL.Uniform1(_gbLightmapIntensityLoc, 1f);
        GL.Uniform4(_gbLightmapScaleOffsetLoc, new Vector4(1f, 1f, 0f, 0f));
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private void BindDeferredOptionalTexture(TextureUnit unit, int samplerLocation, int hasLocation, string? path)
    {
        if (unit == TextureUnit.Texture2 && !string.IsNullOrWhiteSpace(path))
            TextureImportSettingsAsset.EnsureNormalMap(path);

        int texture = string.IsNullOrWhiteSpace(path) ? 0 : GetTexture(path);
        GL.Uniform1(samplerLocation, (int)unit - (int)TextureUnit.Texture0);
        GL.Uniform1(hasLocation, texture != 0 ? 1 : 0);
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, texture);
    }

    private void ApplyDeferredDynamicSurfaceIfChanged(DynamicMeshDraw draw, ref Vector4 boundColor, ref Vector4 boundMaterial, ref Vector4 boundEmission)
    {
        SetIntUniform(_gbUseSurfaceUniformsLoc, 1);
        if (boundColor != draw.Color) { GL.Uniform4(_gbSurfaceColorLoc, draw.Color); boundColor = draw.Color; }
        if (boundMaterial != draw.Material) { GL.Uniform4(_gbSurfaceMaterialLoc, draw.Material); boundMaterial = draw.Material; }
        if (boundEmission != draw.Emission) { GL.Uniform4(_gbSurfaceEmissionLoc, draw.Emission); boundEmission = draw.Emission; }
        ApplyDeferredAlphaClip(draw.AlphaClip, draw.AlphaClipThreshold);
    }

    private void ApplyDeferredSkinnedSurfaceIfChanged(SkinnedMeshDraw draw, ref Vector4 boundColor, ref Vector4 boundMaterial, ref Vector4 boundEmission)
    {
        SetIntUniform(_gbUseSurfaceUniformsLoc, 1);
        if (boundColor != draw.Color) { GL.Uniform4(_gbSurfaceColorLoc, draw.Color); boundColor = draw.Color; }
        if (boundMaterial != draw.Material) { GL.Uniform4(_gbSurfaceMaterialLoc, draw.Material); boundMaterial = draw.Material; }
        if (boundEmission != draw.Emission) { GL.Uniform4(_gbSurfaceEmissionLoc, draw.Emission); boundEmission = draw.Emission; }
        ApplyDeferredAlphaClip(draw.AlphaClip, draw.AlphaClipThreshold);
    }

    private void ApplyDeferredAlphaClip(bool enabled, float threshold)
    {
        SetIntUniform(_gbAlphaClipEnabledLoc, enabled ? 1 : 0);
        if (_gbAlphaClipThresholdLoc >= 0)
            GL.Uniform1(_gbAlphaClipThresholdLoc, Math.Clamp(threshold, 0f, 1f));
    }

    private void ReleaseDeferredTargets()
    {
        if (_gbufferMsaaDepth != 0) { GL.DeleteRenderbuffer(_gbufferMsaaDepth); _gbufferMsaaDepth = 0; }
        if (_gbufferMsaaEmission != 0) { GL.DeleteRenderbuffer(_gbufferMsaaEmission); _gbufferMsaaEmission = 0; }
        if (_gbufferMsaaShadowmask != 0) { GL.DeleteRenderbuffer(_gbufferMsaaShadowmask); _gbufferMsaaShadowmask = 0; }
        if (_gbufferMsaaMaterial != 0) { GL.DeleteRenderbuffer(_gbufferMsaaMaterial); _gbufferMsaaMaterial = 0; }
        if (_gbufferMsaaNormalRoughness != 0) { GL.DeleteRenderbuffer(_gbufferMsaaNormalRoughness); _gbufferMsaaNormalRoughness = 0; }
        if (_gbufferMsaaAlbedo != 0) { GL.DeleteRenderbuffer(_gbufferMsaaAlbedo); _gbufferMsaaAlbedo = 0; }
        if (_gbufferMsaaFbo != 0) { GL.DeleteFramebuffer(_gbufferMsaaFbo); _gbufferMsaaFbo = 0; }
        if (_gbufferAlbedo != 0) { GL.DeleteTexture(_gbufferAlbedo); _gbufferAlbedo = 0; }
        if (_gbufferNormalRoughness != 0) { GL.DeleteTexture(_gbufferNormalRoughness); _gbufferNormalRoughness = 0; }
        if (_gbufferMaterial != 0) { GL.DeleteTexture(_gbufferMaterial); _gbufferMaterial = 0; }
        if (_gbufferEmission != 0) { GL.DeleteTexture(_gbufferEmission); _gbufferEmission = 0; }
        if (_gbufferShadowmask != 0) { GL.DeleteTexture(_gbufferShadowmask); _gbufferShadowmask = 0; }
        if (_gbufferDepth != 0) { GL.DeleteTexture(_gbufferDepth); _gbufferDepth = 0; }
        if (_gbufferFbo != 0) { GL.DeleteFramebuffer(_gbufferFbo); _gbufferFbo = 0; }
        _deferredWidth = 0;
        _deferredHeight = 0;
        _deferredSamples = 1;
    }

    private void CacheDeferredGBufferUniforms()
    {
        _gbMvpLoc = GL.GetUniformLocation(_gbufferShader, "uMvp");
        _gbModelLoc = GL.GetUniformLocation(_gbufferShader, "uModel");
        _gbUseInstancingLoc = GL.GetUniformLocation(_gbufferShader, "uUseInstancing");
        _gbUseSkinningLoc = GL.GetUniformLocation(_gbufferShader, "uUseSkinning");
        _gbUseSurfaceUniformsLoc = GL.GetUniformLocation(_gbufferShader, "uUseSurfaceUniforms");
        _gbSurfaceColorLoc = GL.GetUniformLocation(_gbufferShader, "uSurfaceColor");
        _gbSurfaceMaterialLoc = GL.GetUniformLocation(_gbufferShader, "uSurfaceMaterial");
        _gbSurfaceEmissionLoc = GL.GetUniformLocation(_gbufferShader, "uSurfaceEmission");
        _gbTextureLoc = GL.GetUniformLocation(_gbufferShader, "uTexture");
        _gbHasTextureLoc = GL.GetUniformLocation(_gbufferShader, "uHasTexture");
        _gbNormalMapLoc = GL.GetUniformLocation(_gbufferShader, "uNormalMap");
        _gbHasNormalMapLoc = GL.GetUniformLocation(_gbufferShader, "uHasNormalMap");
        _gbRoughnessMapLoc = GL.GetUniformLocation(_gbufferShader, "uRoughnessMap");
        _gbHasRoughnessMapLoc = GL.GetUniformLocation(_gbufferShader, "uHasRoughnessMap");
        _gbMetallicMapLoc = GL.GetUniformLocation(_gbufferShader, "uMetallicMap");
        _gbHasMetallicMapLoc = GL.GetUniformLocation(_gbufferShader, "uHasMetallicMap");
        _gbLightmapLoc = GL.GetUniformLocation(_gbufferShader, "uLightmap");
        _gbHasLightmapLoc = GL.GetUniformLocation(_gbufferShader, "uHasLightmap");
        _gbLightmapIntensityLoc = GL.GetUniformLocation(_gbufferShader, "uLightmapIntensity");
        _gbLightmapScaleOffsetLoc = GL.GetUniformLocation(_gbufferShader, "uLightmapScaleOffset");
        _gbShadowmaskLoc = GL.GetUniformLocation(_gbufferShader, "uShadowmask");
        _gbHasShadowmaskLoc = GL.GetUniformLocation(_gbufferShader, "uHasShadowmask");
        _gbAlphaClipEnabledLoc = GL.GetUniformLocation(_gbufferShader, "uAlphaClipEnabled");
        _gbAlphaClipThresholdLoc = GL.GetUniformLocation(_gbufferShader, "uAlphaClipThreshold");
        _gbColorSpaceLinearLoc = GL.GetUniformLocation(_gbufferShader, "uColorSpaceLinear");
        for (int i = 0; i < MaxGpuBones; i++)
            _gbBoneLocs[i] = GL.GetUniformLocation(_gbufferShader, $"uBones[{i}]");
    }

    private void CacheDeferredLightingUniforms()
    {
        _dlAlbedoLoc = GL.GetUniformLocation(_deferredLightingShader, "uGAlbedo");
        _dlNormalLoc = GL.GetUniformLocation(_deferredLightingShader, "uGNormalRoughness");
        _dlMaterialLoc = GL.GetUniformLocation(_deferredLightingShader, "uGMaterial");
        _dlEmissionLoc = GL.GetUniformLocation(_deferredLightingShader, "uGEmission");
        _dlShadowmaskLoc = GL.GetUniformLocation(_deferredLightingShader, "uGShadowmask");
        _dlDepthLoc = GL.GetUniformLocation(_deferredLightingShader, "uGDepth");
        _dlInvViewProjLoc = GL.GetUniformLocation(_deferredLightingShader, "uInvViewProj");
        _dlCameraPosLoc = GL.GetUniformLocation(_deferredLightingShader, "uCameraPos");
        _dlAmbientColorLoc = GL.GetUniformLocation(_deferredLightingShader, "uAmbientColor");
        _dlAmbientIntensityLoc = GL.GetUniformLocation(_deferredLightingShader, "uAmbientIntensity");
        _dlSkyStrengthLoc = GL.GetUniformLocation(_deferredLightingShader, "uSkyStrength");
        _dlUseIblLoc = GL.GetUniformLocation(_deferredLightingShader, "uUseIBL");
        _dlEnvMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uEnvMap");
        _dlHasEnvMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uHasEnvMap");
        _dlEnvMaxLodLoc = GL.GetUniformLocation(_deferredLightingShader, "uEnvMaxLod");
        _dlEnvDiffuseColorLoc = GL.GetUniformLocation(_deferredLightingShader, "uEnvDiffuseColor");
        _dlUseEnvShLoc = GL.GetUniformLocation(_deferredLightingShader, "uUseEnvSh");
        for (int i = 0; i < _dlEnvShLoc.Length; i++)
            _dlEnvShLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uEnvSh[{i}]");
        _dlEnvPrefilterMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uEnvPrefilterMap");
        _dlHasEnvPrefilterMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uHasEnvPrefilterMap");
        _dlEnvPrefilterMaxLodLoc = GL.GetUniformLocation(_deferredLightingShader, "uEnvPrefilterMaxLod");
        _dlBrdfLutLoc = GL.GetUniformLocation(_deferredLightingShader, "uBrdfLut");
        _dlHasBrdfLutLoc = GL.GetUniformLocation(_deferredLightingShader, "uHasBrdfLut");
        _dlIblDiffuseStrengthLoc = GL.GetUniformLocation(_deferredLightingShader, "uIblDiffuseStrength");
        _dlIblSpecularStrengthLoc = GL.GetUniformLocation(_deferredLightingShader, "uIblSpecularStrength");
        _dlActiveReflectionProbeMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uActiveReflectionProbeMap");
        _dlHasActiveReflectionProbeMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uHasActiveReflectionProbeMap");
        _dlActiveReflectionProbeMaxLodLoc = GL.GetUniformLocation(_deferredLightingShader, "uActiveReflectionProbeMaxLod");
        _dlActiveReflectionProbeCenterLoc = GL.GetUniformLocation(_deferredLightingShader, "uActiveReflectionProbeCenter");
        _dlActiveReflectionProbeHalfSizeLoc = GL.GetUniformLocation(_deferredLightingShader, "uActiveReflectionProbeHalfSize");
        _dlActiveReflectionProbeBlendDistanceLoc = GL.GetUniformLocation(_deferredLightingShader, "uActiveReflectionProbeBlendDistance");
        _dlActiveReflectionProbeIntensityLoc = GL.GetUniformLocation(_deferredLightingShader, "uActiveReflectionProbeIntensity");
        _dlActiveReflectionProbeBoxProjectionLoc = GL.GetUniformLocation(_deferredLightingShader, "uActiveReflectionProbeBoxProjection");
        _dlSecondaryReflectionProbeMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uSecondaryReflectionProbeMap");
        _dlHasSecondaryReflectionProbeMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uHasSecondaryReflectionProbeMap");
        _dlSecondaryReflectionProbeMaxLodLoc = GL.GetUniformLocation(_deferredLightingShader, "uSecondaryReflectionProbeMaxLod");
        _dlSecondaryReflectionProbeCenterLoc = GL.GetUniformLocation(_deferredLightingShader, "uSecondaryReflectionProbeCenter");
        _dlSecondaryReflectionProbeHalfSizeLoc = GL.GetUniformLocation(_deferredLightingShader, "uSecondaryReflectionProbeHalfSize");
        _dlSecondaryReflectionProbeBlendDistanceLoc = GL.GetUniformLocation(_deferredLightingShader, "uSecondaryReflectionProbeBlendDistance");
        _dlSecondaryReflectionProbeIntensityLoc = GL.GetUniformLocation(_deferredLightingShader, "uSecondaryReflectionProbeIntensity");
        _dlSecondaryReflectionProbeBoxProjectionLoc = GL.GetUniformLocation(_deferredLightingShader, "uSecondaryReflectionProbeBoxProjection");
        _dlShadowMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowMap");
        _dlShadowEnabledLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowEnabled");
        _dlShadowStrengthLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowStrength");
        _dlShadowPcfRadiusLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowPcfRadius");
        _dlShadowBiasScaleLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowBiasScale");
        _dlShadowNormalBiasLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowNormalBias");
        _dlCascadeCountLoc = GL.GetUniformLocation(_deferredLightingShader, "uCascadeCount");
        _dlShadowCameraPosLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowCameraPos");
        _dlSpotShadowMapLoc = GL.GetUniformLocation(_deferredLightingShader, "uSpotShadowMap");
        for (int i = 0; i < MaxSpotLights; i++)
        {
            _dlSpotLightMvpLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotLightMvp[{i}]");
            _dlSpotShadowEnabledLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotShadowEnabled[{i}]");
            _dlSpotShadowStrengthLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotShadowStrength[{i}]");
            _dlSpotShadowSlotLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotShadowSlot[{i}]");
        }
        _dlPointShadowCubeLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointShadowCube");
        _dlPointShadowEnabledLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointShadowEnabled");
        _dlPointShadowStrengthLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointShadowStrength");
        _dlPointShadowPosLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointShadowPos");
        _dlPointShadowFarLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointShadowFar");
        _dlPointShadowTexelSizeLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointShadowTexelSize");
        _dlPointShadowLightIndexLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointShadowLightIndex");
        for (int i = 0; i < MaxDirectionalShadowCascades; i++)
        {
            _dlCascadeLightMvpLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uCascadeLightMvp[{i}]");
            _dlCascadeSplitLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uCascadeSplit[{i}]");
        }
        _dlDirDirLoc = GL.GetUniformLocation(_deferredLightingShader, "uDirDir");
        _dlDirColorLoc = GL.GetUniformLocation(_deferredLightingShader, "uDirColor");
        _dlDirIntensityLoc = GL.GetUniformLocation(_deferredLightingShader, "uDirIntensity");
        _dlDirShadowmaskChannelLoc = GL.GetUniformLocation(_deferredLightingShader, "uDirShadowmaskChannel");
        _dlMixedLightingModeLoc = GL.GetUniformLocation(_deferredLightingShader, "uMixedLightingMode");
        _dlDistanceShadowmaskLoc = GL.GetUniformLocation(_deferredLightingShader, "uDistanceShadowmask");
        _dlShadowmaskDistanceLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowmaskDistance");
        _dlShadowmaskDistanceFadeLoc = GL.GetUniformLocation(_deferredLightingShader, "uShadowmaskDistanceFade");
        _dlPointCountLoc = GL.GetUniformLocation(_deferredLightingShader, "uPointCount");
        for (int i = 0; i < MaxPointLights; i++)
        {
            _dlPointPosLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uPointPos[{i}]");
            _dlPointColorLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uPointColor[{i}]");
            _dlPointIntensityLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uPointIntensity[{i}]");
            _dlPointRangeLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uPointRange[{i}]");
            _dlPointShadowmaskChannelLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uPointShadowmaskChannel[{i}]");
        }
        _dlSpotCountLoc = GL.GetUniformLocation(_deferredLightingShader, "uSpotCount");
        for (int i = 0; i < MaxSpotLights; i++)
        {
            _dlSpotPosLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotPos[{i}]");
            _dlSpotDirLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotDir[{i}]");
            _dlSpotColorLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotColor[{i}]");
            _dlSpotIntensityLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotIntensity[{i}]");
            _dlSpotRangeLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotRange[{i}]");
            _dlSpotAngleLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotAngle[{i}]");
            _dlSpotShadowmaskChannelLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uSpotShadowmaskChannel[{i}]");
        }
        _dlAreaCountLoc = GL.GetUniformLocation(_deferredLightingShader, "uAreaCount");
        for (int i = 0; i < MaxAreaLights; i++)
        {
            _dlAreaPosLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uAreaPos[{i}]");
            _dlAreaDirLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uAreaDir[{i}]");
            _dlAreaColorLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uAreaColor[{i}]");
            _dlAreaIntensityLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uAreaIntensity[{i}]");
            _dlAreaRangeLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uAreaRange[{i}]");
            _dlAreaSizeLoc[i] = GL.GetUniformLocation(_deferredLightingShader, $"uAreaSize[{i}]");
        }
        _dlFogDensityLoc = GL.GetUniformLocation(_deferredLightingShader, "uFogDensity");
        _dlFogColorLoc = GL.GetUniformLocation(_deferredLightingShader, "uFogColor");
        _dlFogHeightLoc = GL.GetUniformLocation(_deferredLightingShader, "uFogHeight");
        _dlFogHeightFalloffLoc = GL.GetUniformLocation(_deferredLightingShader, "uFogHeightFalloff");
        _dlVolumetricStrengthLoc = GL.GetUniformLocation(_deferredLightingShader, "uVolumetricStrength");
        _dlDebugViewLoc = GL.GetUniformLocation(_deferredLightingShader, "uDebugView");
    }

    private void CreateDeferredFullscreenQuad()
    {
        float[] tri =
        {
            -1f, -1f,
             3f, -1f,
            -1f,  3f
        };
        _deferredQuadVao = GL.GenVertexArray();
        _deferredQuadVbo = GL.GenBuffer();
        GL.BindVertexArray(_deferredQuadVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _deferredQuadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, tri.Length * sizeof(float), tri, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    private static int CreateDeferredGBufferShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aPosition;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in vec4 aColor;
            layout (location = 3) in vec2 aUv;
            layout (location = 4) in vec4 aMaterial;
            layout (location = 5) in vec4 aEmission;
            layout (location = 12) in vec2 aLightmapUv;
            layout (location = 6) in mat4 aInstanceModel;
            layout (location = 10) in vec4 aBoneIndices;
            layout (location = 11) in vec4 aBoneWeights;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            uniform int  uUseInstancing;
            uniform int  uUseSkinning;
            uniform mat4 uBones[128];
            uniform int  uUseSurfaceUniforms;
            uniform vec4 uSurfaceColor;
            uniform vec4 uSurfaceMaterial;
            uniform vec4 uSurfaceEmission;
            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec4 vColor;
            out vec2 vUv;
            out vec4 vMaterial;
            out vec4 vEmission;
            out vec2 vLightmapUv;
            void main()
            {
                mat4 model = uUseInstancing != 0 ? aInstanceModel : uModel;
                vec4 world = vec4(aPosition, 1.0) * model;
                vec3 normalWs = (vec4(aNormal, 0.0) * model).xyz;
                if (uUseSkinning != 0)
                {
                    vec4 skinnedPos = vec4(0.0);
                    vec3 skinnedNormal = vec3(0.0);
                    float totalWeight = 0.0;
                    for (int i = 0; i < 4; i++)
                    {
                        int boneIndex = int(aBoneIndices[i] + 0.5);
                        float weight = aBoneWeights[i];
                        if (boneIndex >= 0 && boneIndex < 128 && weight > 0.0)
                        {
                            mat4 bone = uBones[boneIndex];
                            skinnedPos += (vec4(aPosition, 1.0) * bone) * weight;
                            skinnedNormal += (vec4(aNormal, 0.0) * bone).xyz * weight;
                            totalWeight += weight;
                        }
                    }
                    if (totalWeight > 0.0001)
                    {
                        world = skinnedPos;
                        normalWs = skinnedNormal;
                    }
                }
                vWorldPos = world.xyz;
                vNormal = length(normalWs) > 0.0001 ? normalize(normalWs) : vec3(0.0, 1.0, 0.0);
                vColor = uUseSurfaceUniforms != 0 ? uSurfaceColor : aColor;
                vUv = aUv;
                vMaterial = uUseSurfaceUniforms != 0 ? uSurfaceMaterial : aMaterial;
                vEmission = uUseSurfaceUniforms != 0 ? uSurfaceEmission : aEmission;
                vLightmapUv = aLightmapUv;
                gl_Position = world * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec3 vWorldPos;
            in vec3 vNormal;
            in vec4 vColor;
            in vec2 vUv;
            in vec4 vMaterial;
            in vec4 vEmission;
            in vec2 vLightmapUv;
            uniform sampler2D uTexture;
            uniform bool uHasTexture;
            uniform sampler2D uNormalMap;
            uniform bool uHasNormalMap;
            uniform sampler2D uRoughnessMap;
            uniform bool uHasRoughnessMap;
            uniform sampler2D uMetallicMap;
            uniform bool uHasMetallicMap;
            uniform sampler2D uLightmap;
            uniform bool uHasLightmap;
            uniform float uLightmapIntensity;
            uniform vec4 uLightmapScaleOffset;
            uniform sampler2D uShadowmask;
            uniform bool uHasShadowmask;
            uniform int uAlphaClipEnabled;
            uniform float uAlphaClipThreshold;
            uniform int uColorSpaceLinear;
            layout (location = 0) out vec4 gAlbedo;
            layout (location = 1) out vec4 gNormalRoughness;
            layout (location = 2) out vec4 gMaterial;
            layout (location = 3) out vec4 gEmission;
            layout (location = 4) out vec4 gShadowmask;

            vec3 ApplyNormalMap(vec3 n)
            {
                if (!uHasNormalMap)
                    return n;
                vec3 map = texture(uNormalMap, vUv).xyz * 2.0 - 1.0;
                vec3 dp1 = dFdx(vWorldPos);
                vec3 dp2 = dFdy(vWorldPos);
                vec2 duv1 = dFdx(vUv);
                vec2 duv2 = dFdy(vUv);
                float det = duv1.x * duv2.y - duv1.y * duv2.x;
                vec3 tangent;
                float handedness = 1.0;
                if (abs(det) > 1e-8)
                {
                    tangent = (dp1 * duv2.y - dp2 * duv1.y) / det;
                    handedness = det < 0.0 ? -1.0 : 1.0;
                }
                else
                {
                    tangent = abs(n.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : cross(vec3(0.0, 1.0, 0.0), n);
                }
                tangent = normalize(tangent - n * dot(n, tangent));
                vec3 bitangent = normalize(cross(n, tangent)) * handedness;
                return normalize(tangent * map.x + bitangent * map.y + n * map.z);
            }

            void main()
            {
                vec4 tex = uHasTexture ? texture(uTexture, vUv) : vec4(1.0);
                float alpha = clamp(vColor.a * tex.a, 0.0, 1.0);
                if (uAlphaClipEnabled == 1 && alpha < clamp(uAlphaClipThreshold, 0.0, 1.0))
                    discard;

                vec3 rawColor = max(vColor.rgb * tex.rgb, vec3(0.0));
                vec3 base = uColorSpaceLinear == 1 ? pow(rawColor, vec3(2.2)) : rawColor;
                vec3 normal = ApplyNormalMap(normalize(vNormal));
                float metallic = clamp(uHasMetallicMap ? texture(uMetallicMap, vUv).r : vMaterial.x, 0.0, 1.0);
                float roughness = clamp(uHasRoughnessMap ? texture(uRoughnessMap, vUv).r : vMaterial.y, 0.04, 1.0);
                float materialFlags = floor(vMaterial.z + 0.5);
                float specularHighlights = mod(materialFlags, 2.0) >= 1.0 ? 1.0 : 0.0;
                float environmentReflections = materialFlags >= 2.0 ? 1.0 : 0.0;
                float subsurface = clamp(vMaterial.w, 0.0, 1.0);
                gAlbedo = vec4(base, alpha);
                gNormalRoughness = vec4(normal * 0.5 + 0.5, roughness);
                gMaterial = vec4(metallic, specularHighlights, environmentReflections, subsurface);
                // El baker guarda irradiancia en espacio sRGB. Se convierte antes de mezclarla
                // y se modula por el albedo: así queda como luz indirecta, no como un "emission"
                // plano que lave los materiales.
                vec2 lightmapUv = vLightmapUv * uLightmapScaleOffset.xy + uLightmapScaleOffset.zw;
                vec3 bakedIrradiance = uHasLightmap ? texture(uLightmap, lightmapUv).rgb : vec3(0.0);
                if (uColorSpaceLinear == 1)
                    bakedIrradiance = pow(max(bakedIrradiance, vec3(0.0)), vec3(2.2));
                gEmission = vec4(vEmission.rgb + base * bakedIrradiance * max(uLightmapIntensity, 0.0), vEmission.a);
                gShadowmask = uHasShadowmask ? texture(uShadowmask, lightmapUv) : vec4(1.0);
            }
            """;

        return LinkProgram(vertexSource, fragmentSource);
    }

    private static int CreateDeferredLightingShader()
    {
        const string vertexSource = """
            #version 330 core
            layout(location = 0) in vec2 aPos;
            out vec2 vUv;
            void main()
            {
                vUv = aPos * 0.5 + 0.5;
                gl_Position = vec4(aPos, 0.0, 1.0);
            }
            """;

        const string fragmentSource = """
            #version 330 core
            #define MAX_POINT_LIGHTS 8
            #define MAX_SPOT_LIGHTS 4
            #define MAX_AREA_LIGHTS 4
            in vec2 vUv;
            out vec4 outColor;
            uniform sampler2D uGAlbedo;
            uniform sampler2D uGNormalRoughness;
            uniform sampler2D uGMaterial;
            uniform sampler2D uGEmission;
            uniform sampler2D uGShadowmask;
            uniform sampler2D uGDepth;
            uniform mat4 uInvViewProj;
            uniform vec3 uCameraPos;
            uniform vec3 uAmbientColor;
            uniform float uAmbientIntensity;
            uniform float uSkyStrength;
            uniform int   uUseIBL;
            uniform sampler2D uEnvMap;
            uniform int       uHasEnvMap;
            uniform float     uEnvMaxLod;
            uniform vec3      uEnvDiffuseColor;
            uniform int       uUseEnvSh;
            uniform vec3      uEnvSh[9];
            uniform samplerCube uEnvPrefilterMap;
            uniform int       uHasEnvPrefilterMap;
            uniform float     uEnvPrefilterMaxLod;
            uniform samplerCube uActiveReflectionProbeMap;
            uniform int       uHasActiveReflectionProbeMap;
            uniform float     uActiveReflectionProbeMaxLod;
            uniform vec3      uActiveReflectionProbeCenter;
            uniform vec3      uActiveReflectionProbeHalfSize;
            uniform float     uActiveReflectionProbeBlendDistance;
            uniform float     uActiveReflectionProbeIntensity;
            uniform int       uActiveReflectionProbeBoxProjection;
            uniform samplerCube uSecondaryReflectionProbeMap;
            uniform int       uHasSecondaryReflectionProbeMap;
            uniform float     uSecondaryReflectionProbeMaxLod;
            uniform vec3      uSecondaryReflectionProbeCenter;
            uniform vec3      uSecondaryReflectionProbeHalfSize;
            uniform float     uSecondaryReflectionProbeBlendDistance;
            uniform float     uSecondaryReflectionProbeIntensity;
            uniform int       uSecondaryReflectionProbeBoxProjection;
            uniform sampler2D uBrdfLut;
            uniform int       uHasBrdfLut;
            uniform float     uIblDiffuseStrength;
            uniform float     uIblSpecularStrength;
            uniform sampler2DArray uShadowMap;
            uniform int   uShadowEnabled;
            uniform float uShadowStrength;
            uniform int   uShadowPcfRadius;
            uniform float uShadowBiasScale;
            uniform float uShadowNormalBias;
            uniform int   uCascadeCount;
            uniform mat4  uCascadeLightMvp[5];
            uniform float uCascadeSplit[5];
            uniform vec3  uShadowCameraPos;
            uniform sampler2DArray uSpotShadowMap;
            uniform mat4      uSpotLightMvp[MAX_SPOT_LIGHTS];
            uniform int       uSpotShadowEnabled[MAX_SPOT_LIGHTS];
            uniform float     uSpotShadowStrength[MAX_SPOT_LIGHTS];
            uniform samplerCube uPointShadowCube;
            uniform int       uPointShadowEnabled;
            uniform float     uPointShadowStrength;
            uniform vec3      uPointShadowPos;
            uniform float     uPointShadowFar;
            uniform float     uPointShadowTexelSize;
            uniform int       uPointShadowLightIndex;
            uniform vec3 uDirDir;
            uniform vec3 uDirColor;
            uniform float uDirIntensity;
            uniform int uDirShadowmaskChannel;
            uniform int uMixedLightingMode;
            uniform int uDistanceShadowmask;
            uniform float uShadowmaskDistance;
            uniform float uShadowmaskDistanceFade;
            uniform int uPointCount;
            uniform vec3 uPointPos[MAX_POINT_LIGHTS];
            uniform vec3 uPointColor[MAX_POINT_LIGHTS];
            uniform float uPointIntensity[MAX_POINT_LIGHTS];
            uniform float uPointRange[MAX_POINT_LIGHTS];
            uniform int uPointShadowmaskChannel[MAX_POINT_LIGHTS];
            uniform int uSpotCount;
            uniform vec3 uSpotPos[MAX_SPOT_LIGHTS];
            uniform vec3 uSpotDir[MAX_SPOT_LIGHTS];
            uniform vec3 uSpotColor[MAX_SPOT_LIGHTS];
            uniform float uSpotIntensity[MAX_SPOT_LIGHTS];
            uniform float uSpotRange[MAX_SPOT_LIGHTS];
            uniform float uSpotAngle[MAX_SPOT_LIGHTS];
            uniform int uSpotShadowSlot[MAX_SPOT_LIGHTS];
            uniform int uSpotShadowmaskChannel[MAX_SPOT_LIGHTS];
            uniform int uAreaCount;
            uniform vec3 uAreaPos[MAX_AREA_LIGHTS];
            uniform vec3 uAreaDir[MAX_AREA_LIGHTS];
            uniform vec3 uAreaColor[MAX_AREA_LIGHTS];
            uniform float uAreaIntensity[MAX_AREA_LIGHTS];
            uniform float uAreaRange[MAX_AREA_LIGHTS];
            uniform vec2 uAreaSize[MAX_AREA_LIGHTS];
            uniform float uFogDensity;
            uniform vec3 uFogColor;
            uniform float uFogHeight;
            uniform float uFogHeightFalloff;
            uniform float uVolumetricStrength;
            uniform int uDebugView;

            const float PI = 3.14159265359;

            float RangeAttenuation(float dist, float range)
            {
                float x = clamp(1.0 - dist / max(range, 0.001), 0.0, 1.0);
                return x * x * (3.0 - 2.0 * x);
            }

            float DistributionGGX(vec3 N, vec3 H, float roughness)
            {
                float a = roughness * roughness;
                float a2 = a * a;
                float nh = max(dot(N, H), 0.0);
                float nh2 = nh * nh;
                float denom = (nh2 * (a2 - 1.0) + 1.0);
                return a2 / max(PI * denom * denom, 0.0001);
            }

            vec3 FresnelSchlick(float cosTheta, vec3 F0)
            {
                return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
            }

            vec2 DirToEquirectUV(vec3 d)
            {
                float u = atan(d.z, d.x) / (2.0 * PI) + 0.5;
                float v = acos(clamp(d.y, -1.0, 1.0)) / PI;
                return vec2(u, v);
            }

            vec3 EnvBRDFApprox(vec3 specColor, float roughness, float NoV)
            {
                const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
                const vec4 c1 = vec4( 1.0,  0.0425,  1.040, -0.040);
                vec4 r = roughness * c0 + c1;
                float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
                vec2 AB = vec2(-1.04, 1.04) * a004 + r.zw;
                return specColor * AB.x + AB.y;
            }

            vec3 EnvBRDF(vec3 specColor, float roughness, float NoV)
            {
                if (uHasBrdfLut == 1)
                {
                    vec2 ab = texture(uBrdfLut, vec2(clamp(NoV, 0.0, 1.0), clamp(roughness, 0.0, 1.0))).rg;
                    return specColor * ab.x + ab.y;
                }
                return EnvBRDFApprox(specColor, roughness, NoV);
            }

            vec3 SampleEnv(vec3 dir)
            {
                float up = clamp(dir.y, -1.0, 1.0);
                vec3 skyCol = uAmbientColor * (uAmbientIntensity + uSkyStrength * 0.48);
                vec3 horizonCol = uAmbientColor * uAmbientIntensity;
                vec3 groundCol = uAmbientColor * uAmbientIntensity * 0.35;
                return up >= 0.0 ? mix(horizonCol, skyCol, up) : mix(horizonCol, groundCol, -up);
            }

            vec3 IrradianceFromSH(vec3 n)
            {
                if (uUseEnvSh == 0)
                    return uEnvDiffuseColor;
                const float A0 = 3.14159265359;
                const float A1 = 2.09439510239;
                const float A2 = 0.78539816339;
                float x = n.x;
                float y = n.y;
                float z = n.z;
                vec3 e = vec3(0.0);
                e += uEnvSh[0] * (A0 * 0.282095);
                e += uEnvSh[1] * (A1 * -0.488603 * y);
                e += uEnvSh[2] * (A1 *  0.488603 * z);
                e += uEnvSh[3] * (A1 * -0.488603 * x);
                e += uEnvSh[4] * (A2 *  1.092548 * x * y);
                e += uEnvSh[5] * (A2 * -1.092548 * y * z);
                e += uEnvSh[6] * (A2 *  0.315392 * (3.0 * z * z - 1.0));
                e += uEnvSh[7] * (A2 * -1.092548 * x * z);
                e += uEnvSh[8] * (A2 *  0.546274 * (x * x - y * y));
                return max(e / PI, vec3(0.0));
            }

            float ReflectionProbeInfluence(vec3 worldPos)
            {
                if (uHasActiveReflectionProbeMap == 0)
                    return 0.0;
                vec3 halfSize = max(uActiveReflectionProbeHalfSize, vec3(0.001));
                vec3 local = abs(worldPos - uActiveReflectionProbeCenter);
                vec3 normalized = local / halfSize;
                float boxDistance = max(normalized.x, max(normalized.y, normalized.z));
                if (boxDistance >= 1.0)
                    return 0.0;
                float blend = max(uActiveReflectionProbeBlendDistance, 0.0001);
                float minHalf = max(min(halfSize.x, min(halfSize.y, halfSize.z)), 0.001);
                float blendWidth = clamp(blend / minHalf, 0.001, 0.95);
                float edgeStart = max(0.0, 1.0 - blendWidth);
                float fade = 1.0 - smoothstep(edgeStart, 1.0, boxDistance);
                return clamp(fade * uActiveReflectionProbeIntensity, 0.0, 1.0);
            }

            vec3 ReflectionProbeDirection(vec3 worldPos, vec3 reflectionDir)
            {
                if (uActiveReflectionProbeBoxProjection == 0)
                    return reflectionDir;
                vec3 halfSize = max(uActiveReflectionProbeHalfSize, vec3(0.001));
                vec3 safeDir = mix(vec3(-0.0001), vec3(0.0001), greaterThanEqual(reflectionDir, vec3(0.0)));
                safeDir = mix(safeDir, reflectionDir, greaterThanEqual(abs(reflectionDir), vec3(0.0001)));
                vec3 boxMin = uActiveReflectionProbeCenter - halfSize;
                vec3 boxMax = uActiveReflectionProbeCenter + halfSize;
                vec3 toMax = (boxMax - worldPos) / safeDir;
                vec3 toMin = (boxMin - worldPos) / safeDir;
                vec3 exitDistance = max(toMax, toMin);
                float distanceToBox = min(exitDistance.x, min(exitDistance.y, exitDistance.z));
                if (distanceToBox <= 0.0)
                    return reflectionDir;
                return normalize(worldPos + reflectionDir * distanceToBox - uActiveReflectionProbeCenter);
            }

            float SecondaryReflectionProbeInfluence(vec3 worldPos)
            {
                if (uHasSecondaryReflectionProbeMap == 0) return 0.0;
                vec3 halfSize = max(uSecondaryReflectionProbeHalfSize, vec3(0.001));
                vec3 normalized = abs(worldPos - uSecondaryReflectionProbeCenter) / halfSize;
                float boxDistance = max(normalized.x, max(normalized.y, normalized.z));
                if (boxDistance >= 1.0) return 0.0;
                float blendWidth = clamp(max(uSecondaryReflectionProbeBlendDistance, 0.0001) /
                    max(min(halfSize.x, min(halfSize.y, halfSize.z)), 0.001), 0.001, 0.95);
                return clamp((1.0 - smoothstep(max(0.0, 1.0 - blendWidth), 1.0, boxDistance)) *
                    uSecondaryReflectionProbeIntensity, 0.0, 1.0);
            }

            vec3 SecondaryReflectionProbeDirection(vec3 worldPos, vec3 reflectionDir)
            {
                if (uSecondaryReflectionProbeBoxProjection == 0) return reflectionDir;
                vec3 halfSize = max(uSecondaryReflectionProbeHalfSize, vec3(0.001));
                vec3 safeDir = mix(vec3(-0.0001), vec3(0.0001), greaterThanEqual(reflectionDir, vec3(0.0)));
                safeDir = mix(safeDir, reflectionDir, greaterThanEqual(abs(reflectionDir), vec3(0.0001)));
                vec3 toMax = (uSecondaryReflectionProbeCenter + halfSize - worldPos) / safeDir;
                vec3 toMin = (uSecondaryReflectionProbeCenter - halfSize - worldPos) / safeDir;
                float distanceToBox = min(max(toMax, toMin).x, min(max(toMax, toMin).y, max(toMax, toMin).z));
                return distanceToBox <= 0.0 ? reflectionDir :
                    normalize(worldPos + reflectionDir * distanceToBox - uSecondaryReflectionProbeCenter);
            }

            float PcfShadowArray(sampler2DArray shadowMap, vec2 uv, int layer, float currentDepth, float bias, int radius)
            {
                vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0).xy);
                float shadow = 0.0;
                float taps = 0.0;
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                    {
                        float closest = texture(shadowMap, vec3(uv + vec2(x, y) * texel, float(layer))).r;
                        shadow += currentDepth - bias > closest ? 1.0 : 0.0;
                        taps += 1.0;
                    }
                return shadow / max(taps, 1.0);
            }

            float ShadowEdgeFade(vec2 uv)
            {
                const float edge = 0.06;
                float fx = smoothstep(0.0, edge, uv.x) * smoothstep(1.0, 1.0 - edge, uv.x);
                float fy = smoothstep(0.0, edge, uv.y) * smoothstep(1.0, 1.0 - edge, uv.y);
                return fx * fy;
            }

            float ShadowFactor(vec3 N, vec3 worldPos, vec3 lightDir)
            {
                if (uShadowEnabled == 0) return 1.0;
                float viewDistance = length(worldPos - uShadowCameraPos);
                int cascade = max(uCascadeCount - 1, 0);
                for (int i = 0; i < 5; i++)
                {
                    if (i >= uCascadeCount) break;
                    if (viewDistance <= uCascadeSplit[i])
                    {
                        cascade = i;
                        break;
                    }
                }
                // Normal Bias (estilo Unity): desplaza la posición muestreada a lo largo de la normal
                // → reduce el "shadow acne" en superficies inclinadas. Escala con la distancia/cascada.
                float nbScale = uShadowNormalBias * (0.02 + 0.03 * float(cascade));
                vec3 biasedPos = worldPos + N * nbScale;
                vec4 lightPos = vec4(biasedPos, 1.0) * uCascadeLightMvp[cascade];
                vec3 proj = lightPos.xyz / max(lightPos.w, 0.0001);
                proj = proj * 0.5 + 0.5;
                if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
                    return 1.0;
                float bias = max(0.0035 * (1.0 - dot(N, normalize(-lightDir))), 0.0012) * uShadowBiasScale;
                // PCF limitado para interiores. Un filtro PCSS de radio grande produjo halos
                // luminosos alrededor de las ventanas y sombras inestables al cambiar el sol.
                float shadow = uShadowPcfRadius > 0
                    ? PcfShadowArray(uShadowMap, proj.xy, cascade, proj.z, bias, min(uShadowPcfRadius, 2))
                    : ((proj.z - bias > texture(uShadowMap, vec3(proj.xy, float(cascade))).r) ? 1.0 : 0.0);
                shadow *= ShadowEdgeFade(proj.xy);
                float visualStrength = clamp(uShadowStrength + clamp(uSkyStrength, 0.0, 1.0) * 0.18, 0.0, 0.95);
                return mix(1.0, 1.0 - shadow, visualStrength);
            }

            // Los mapas se construyen para el primer Spot/Point con Shadows. Aplicarlos
            // también en Deferred elimina la luz que atravesaba paredes en interiores.
            float SpotShadowFactor(int lightIndex, vec3 N, vec3 worldPos, vec3 lightDir)
            {
                int slot = uSpotShadowSlot[lightIndex];
                if (slot < 0 || slot >= MAX_SPOT_LIGHTS || uSpotShadowEnabled[slot] == 0) return 1.0;
                vec4 lp = vec4(worldPos, 1.0) * uSpotLightMvp[slot];
                vec3 proj = lp.xyz / max(lp.w, 0.0001);
                proj = proj * 0.5 + 0.5;
                if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
                    return 1.0;
                float bias = max(0.004 * (1.0 - dot(N, normalize(lightDir))), 0.0014) * uShadowBiasScale;
                float shadow = PcfShadowArray(uSpotShadowMap, proj.xy, slot, proj.z, bias, uShadowPcfRadius);
                shadow *= ShadowEdgeFade(proj.xy);
                return mix(1.0, 1.0 - shadow, uSpotShadowStrength[slot]);
            }

            float PointShadowFactor(vec3 N, vec3 worldPos)
            {
                if (uPointShadowEnabled == 0) return 1.0;
                vec3 toFrag = worldPos - uPointShadowPos;
                float current = length(toFrag);
                vec3 dir = normalize(toFrag);
                float bias = clamp(0.018 * (1.0 - dot(N, -dir)), 0.004, 0.025) * uShadowBiasScale;
                if (uShadowPcfRadius <= 0)
                {
                    float closest = texture(uPointShadowCube, dir).r * uPointShadowFar;
                    float hard = current - bias > closest ? 1.0 : 0.0;
                    return mix(1.0, 1.0 - hard, uPointShadowStrength);
                }
                vec3 up = abs(dir.y) > 0.95 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
                vec3 right = normalize(cross(up, dir));
                up = cross(dir, right);
                // El filtro de cubemap se expresa en texels, no en distancia de mundo.
                // Esta amplitud da una penumbra suave sin reconstruir el disco de alcance.
                float diskRadius = uPointShadowTexelSize * (1.50 + 1.50 * float(uShadowPcfRadius));
                float shadow = 0.0;
                float taps = 0.0;
                int kernelRadius = min(uShadowPcfRadius, 2);
                for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                    {
                        if (abs(x) > kernelRadius || abs(y) > kernelRadius) continue;
                        vec3 sampleDir = dir + (right * float(x) + up * float(y)) * diskRadius;
                        float closest = texture(uPointShadowCube, sampleDir).r * uPointShadowFar;
                        shadow += current - bias > closest ? 1.0 : 0.0;
                        taps += 1.0;
                    }
                return mix(1.0, 1.0 - shadow / max(taps, 1.0), uPointShadowStrength);
            }

            vec3 CascadeDebugColor(float viewDistance)
            {
                int cascade = max(uCascadeCount - 1, 0);
                for (int i = 0; i < 5; i++)
                {
                    if (i >= uCascadeCount) break;
                    if (viewDistance <= uCascadeSplit[i])
                    {
                        cascade = i;
                        break;
                    }
                }
                if (cascade == 0) return vec3(0.25, 0.95, 0.35);
                if (cascade == 1) return vec3(0.20, 0.55, 1.00);
                if (cascade == 2) return vec3(1.00, 0.85, 0.20);
                if (cascade == 3) return vec3(1.00, 0.35, 0.85);
                return vec3(1.00, 0.45, 0.20);
            }

            vec3 MaterialHealthDebug(vec3 albedoLinear, float metallic, float roughness)
            {
                float luminance = dot(albedoLinear, vec3(0.2126, 0.7152, 0.0722));
                float overbright = smoothstep(0.86, 1.15, luminance);
                float tooDark = 1.0 - smoothstep(0.015, 0.065, luminance);
                float metalTooBright = metallic * smoothstep(0.62, 0.92, luminance);
                float roughnessExtreme = max(1.0 - smoothstep(0.035, 0.12, roughness), smoothstep(0.96, 1.0, roughness));
                vec3 ok = vec3(0.10, 0.75, 0.35);
                vec3 warn = vec3(1.00, 0.75, 0.15);
                vec3 bad = vec3(1.00, 0.12, 0.08);
                float warning = clamp(overbright + tooDark + roughnessExtreme * 0.55, 0.0, 1.0);
                float error = clamp(metalTooBright + overbright * 0.7, 0.0, 1.0);
                return mix(mix(ok, warn, warning), bad, error);
            }

            vec3 AddDirectLight(vec3 N, vec3 V, vec3 L, vec3 radiance, float intensity, vec3 albedo, float roughness, float metallic, float specularHighlights, float subsurface)
            {
                float ndl = max(dot(N, L), 0.0);
                vec3 H = normalize(L + V);
                vec3 F0 = mix(vec3(0.04), albedo, metallic);
                float D = DistributionGGX(N, H, roughness);
                vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
                float LoH = max(dot(L, H), 0.0);
                float a = roughness * roughness;
                float visF = 1.0 / (4.0 * max(0.1, LoH * LoH) * (a + 0.5));
                vec3 specular = D * visF * F * specularHighlights;
                vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
                vec3 diffuse = kD * albedo / PI;
                vec3 surface = (diffuse + specular) * radiance * intensity * ndl;
                float backLight = pow(max(dot(-N, L), 0.0), 1.35) * pow(max(dot(-L, V), 0.0), 1.5);
                vec3 transmission = albedo * radiance * intensity * (0.42 * clamp(subsurface, 0.0, 1.0) * backLight);
                return surface + transmission;
            }

            vec3 ReconstructWorld(float depth)
            {
                vec4 clip = vec4(vUv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
                vec4 world = clip * uInvViewProj;
                return world.xyz / max(world.w, 0.0001);
            }

            float ShadowmaskFactor(vec4 mask, int channel)
            {
                if (channel < 0 || channel > 3) return 1.0;
                return channel == 0 ? mask.r : channel == 1 ? mask.g : channel == 2 ? mask.b : mask.a;
            }

            float MixedShadowFactor(vec4 mask, float realtimeShadow, int channel, float viewDistance)
            {
                if (uMixedLightingMode != 1 || channel < 0 || channel > 3)
                    return realtimeShadow;

                float bakedShadow = ShadowmaskFactor(mask, channel);
                float nearShadow = min(realtimeShadow, bakedShadow);
                if (uDistanceShadowmask == 0)
                    return nearShadow;

                float fadeWidth = max(uShadowmaskDistanceFade, 0.01);
                float distanceFade = smoothstep(uShadowmaskDistance - fadeWidth, uShadowmaskDistance, viewDistance);
                return mix(nearShadow, bakedShadow, distanceFade);
            }

            void main()
            {
                float depth = texture(uGDepth, vUv).r;
                if (depth >= 0.999999)
                    discard;

                vec4 albedoAlpha = texture(uGAlbedo, vUv);
                vec3 base = albedoAlpha.rgb;
                vec4 nr = texture(uGNormalRoughness, vUv);
                vec3 N = normalize(nr.xyz * 2.0 - 1.0);
                float roughness = clamp(nr.w, 0.04, 1.0);
                vec4 mat = texture(uGMaterial, vUv);
                float metallic = clamp(mat.x, 0.0, 1.0);
                float specularHighlights = mat.y >= 0.5 ? 1.0 : 0.0;
                float environmentReflections = mat.z >= 0.5 ? 1.0 : 0.0;
                float subsurface = clamp(mat.w, 0.0, 1.0);
                vec4 emission = texture(uGEmission, vUv);
                vec4 shadowmask = texture(uGShadowmask, vUv);
                vec3 worldPos = ReconstructWorld(depth);
                vec3 V = normalize(uCameraPos - worldPos);

                {
                    vec3 dNdx = dFdx(N);
                    vec3 dNdy = dFdy(N);
                    float kernelVar = 0.25 * (dot(dNdx, dNdx) + dot(dNdy, dNdy));
                    float alpha = roughness * roughness;
                    float alpha2Filtered = clamp(alpha * alpha + min(2.0 * kernelVar, 0.18), 0.0, 1.0);
                    roughness = sqrt(sqrt(alpha2Filtered));
                }

                vec3 sky = uAmbientColor * (uAmbientIntensity + max(N.y, 0.0) * uSkyStrength * 0.48);
                vec3 ground = uAmbientColor * uAmbientIntensity * 0.35 * max(-N.y, 0.0);
                vec3 ambientLight = sky + ground;
                float viewDistance = length(uCameraPos - worldPos);
                float sunShadow = MixedShadowFactor(shadowmask, ShadowFactor(N, worldPos, uDirDir), uDirShadowmaskChannel, viewDistance);
                vec3 ambient;
                vec3 debugIblSpecular = vec3(0.0);
                if (uUseIBL == 1)
                {
                    vec3 irradiance = uHasEnvMap == 1 ? IrradianceFromSH(N) : ambientLight;
                    float diffuseBlend = clamp(uIblDiffuseStrength, 0.0, 1.5);
                    vec3 safeDiffuse = base * mix(ambientLight, irradiance, clamp(diffuseBlend, 0.0, 1.0));
                    ambient = safeDiffuse * mix(1.0, 0.85, metallic);

                    float specBlend = clamp(uIblSpecularStrength, 0.0, 1.0) * environmentReflections * specularHighlights;
                    if (specBlend > 0.001)
                    {
                        vec3 R = reflect(-V, N);
                        float NoV = max(dot(N, V), 0.001);
                        vec3 F0 = mix(vec3(0.04), base, metallic);
                        float reflectionRoughness = max(roughness, 0.05);
                        vec3 prefiltered;
                        if (uHasEnvPrefilterMap == 1)
                            prefiltered = textureLod(uEnvPrefilterMap, R, reflectionRoughness * uEnvPrefilterMaxLod).rgb;
                        else if (uHasEnvMap == 1)
                            prefiltered = textureLod(uEnvMap, DirToEquirectUV(R), reflectionRoughness * uEnvMaxLod).rgb;
                        else
                            prefiltered = mix(SampleEnv(R), ambientLight, reflectionRoughness);

                        vec3 environmentReflection = prefiltered;
                        float probeInfluence = ReflectionProbeInfluence(worldPos);
                        float secondaryProbeInfluence = SecondaryReflectionProbeInfluence(worldPos);
                        if (probeInfluence + secondaryProbeInfluence > 0.001)
                        {
                            vec3 primaryReflection = environmentReflection;
                            if (probeInfluence > 0.001)
                                primaryReflection = textureLod(uActiveReflectionProbeMap,
                                    ReflectionProbeDirection(worldPos, R), reflectionRoughness * uActiveReflectionProbeMaxLod).rgb;
                            vec3 secondaryReflection = environmentReflection;
                            if (secondaryProbeInfluence > 0.001)
                                secondaryReflection = textureLod(uSecondaryReflectionProbeMap,
                                    SecondaryReflectionProbeDirection(worldPos, R), reflectionRoughness * uSecondaryReflectionProbeMaxLod).rgb;
                            float localWeight = probeInfluence + secondaryProbeInfluence;
                            vec3 localReflection = (primaryReflection * probeInfluence + secondaryReflection * secondaryProbeInfluence) / max(localWeight, 0.0001);
                            prefiltered = mix(environmentReflection, localReflection, min(localWeight, 1.0));
                        }

                        prefiltered = min(prefiltered, vec3(4.0));
                        float grazingFade = smoothstep(0.05, 0.30, NoV);
                        vec3 envBrdf = EnvBRDF(F0, reflectionRoughness, NoV);
                        debugIblSpecular = prefiltered * envBrdf * specBlend * grazingFade;
                        ambient += debugIblSpecular;
                    }
                }
                else
                {
                    ambient = base * ambientLight;
                }
                vec3 hdr = ambient;
                vec3 directLightingDebug = vec3(0.0);

                directLightingDebug += AddDirectLight(N, V, normalize(-uDirDir), uDirColor, uDirIntensity * sunShadow, base, roughness, metallic, specularHighlights, subsurface);

                for (int i = 0; i < MAX_POINT_LIGHTS; i++)
                {
                    if (i >= uPointCount) break;
                    vec3 toLight = uPointPos[i] - worldPos;
                    float dist = length(toLight);
                    if (dist >= uPointRange[i]) continue;
                    float atten = RangeAttenuation(dist, uPointRange[i]);
                    float pointShadow = i == uPointShadowLightIndex ? PointShadowFactor(N, worldPos) : 1.0;
                    float mixedPointShadow = MixedShadowFactor(shadowmask, pointShadow, uPointShadowmaskChannel[i], viewDistance);
                    directLightingDebug += AddDirectLight(N, V, normalize(toLight), uPointColor[i], uPointIntensity[i] * atten * mixedPointShadow, base, roughness, metallic, specularHighlights, subsurface);
                }

                for (int i = 0; i < MAX_SPOT_LIGHTS; i++)
                {
                    if (i >= uSpotCount) break;
                    vec3 toLight = uSpotPos[i] - worldPos;
                    float dist = length(toLight);
                    if (dist >= uSpotRange[i]) continue;
                    vec3 L = normalize(toLight);
                    float cone = dot(normalize(-uSpotDir[i]), L);
                    float outer = cos(uSpotAngle[i] * 0.5);
                    float inner = cos(uSpotAngle[i] * 0.38);
                    float spot = smoothstep(outer, inner, cone);
                    float atten = RangeAttenuation(dist, uSpotRange[i]) * spot;
                    float spotShadow = SpotShadowFactor(i, N, worldPos, -uSpotDir[i]);
                    float mixedSpotShadow = MixedShadowFactor(shadowmask, spotShadow, uSpotShadowmaskChannel[i], viewDistance);
                    directLightingDebug += AddDirectLight(N, V, L, uSpotColor[i], uSpotIntensity[i] * atten * mixedSpotShadow, base, roughness, metallic, specularHighlights, subsurface);
                }

                for (int i = 0; i < MAX_AREA_LIGHTS; i++)
                {
                    if (i >= uAreaCount) break;
                    vec3 toLight = uAreaPos[i] - worldPos;
                    float dist = length(toLight);
                    if (dist >= uAreaRange[i]) continue;
                    vec3 L = normalize(toLight);
                    float facing = clamp(dot(normalize(-uAreaDir[i]), L), 0.0, 1.0);
                    float sizeBoost = clamp((uAreaSize[i].x + uAreaSize[i].y) * 0.18, 0.25, 1.6);
                    float atten = RangeAttenuation(dist, uAreaRange[i]) * (0.35 + 0.65 * facing) * sizeBoost;
                    directLightingDebug += AddDirectLight(N, V, L, uAreaColor[i], uAreaIntensity[i] * atten, base, roughness, metallic, specularHighlights, subsurface);
                }

                hdr += directLightingDebug;

                hdr += emission.rgb * emission.a;

                if (uDebugView == 1) { outColor = vec4(pow(base, vec3(1.0 / 2.2)), albedoAlpha.a); return; }
                if (uDebugView == 2) { outColor = vec4(N * 0.5 + 0.5, 1.0); return; }
                if (uDebugView == 3) { outColor = vec4(vec3(roughness), 1.0); return; }
                if (uDebugView == 4) { outColor = vec4(vec3(metallic), 1.0); return; }
                if (uDebugView == 5) { outColor = vec4(vec3(sunShadow), 1.0); return; }
                if (uDebugView == 6) { outColor = vec4(CascadeDebugColor(viewDistance) * (0.45 + 0.55 * sunShadow), 1.0); return; }
                if (uDebugView == 7) { outColor = vec4(debugIblSpecular / (debugIblSpecular + vec3(1.0)), 1.0); return; }
                if (uDebugView == 8) { outColor = vec4(MaterialHealthDebug(base, metallic, roughness), 1.0); return; }
                if (uDebugView == 9) { outColor = vec4(directLightingDebug / (directLightingDebug + vec3(1.0)), 1.0); return; }
                if (uDebugView == 10) {
                    float probeInfluence = ReflectionProbeInfluence(worldPos);
                    vec3 probeDebug = vec3(probeInfluence);
                    if (uHasActiveReflectionProbeMap == 1 && probeInfluence > 0.001 && environmentReflections > 0.5)
                    {
                        vec3 R = reflect(-V, N);
                        vec3 probeDir = ReflectionProbeDirection(worldPos, R);
                        vec3 probeReflection = textureLod(uActiveReflectionProbeMap, probeDir, max(roughness, 0.05) * uActiveReflectionProbeMaxLod).rgb;
                        probeDebug = probeReflection * clamp(uIblSpecularStrength, 0.0, 1.0) * probeInfluence;
                    }
                    outColor = vec4(probeDebug / (probeDebug + vec3(1.0)), 1.0);
                    return;
                }

                float heightDensity = 1.0;
                if (uFogHeightFalloff > 0.0001)
                {
                    float startDensity = exp(clamp((uFogHeight - uCameraPos.y) * uFogHeightFalloff, -20.0, 20.0));
                    float endDensity = exp(clamp((uFogHeight - worldPos.y) * uFogHeightFalloff, -20.0, 20.0));
                    heightDensity = 0.5 * (startDensity + endDensity);
                }
                float fogAmount = 1.0 - exp(-viewDistance * max(uFogDensity, 0.0) * heightDensity);
                float forwardFog = pow(max(dot(normalize(uCameraPos - worldPos), normalize(-uDirDir)), 0.0), 8.0);
                vec3 fog = uFogColor * (1.0 + forwardFog * uVolumetricStrength);
                hdr = mix(hdr, fog, clamp(fogAmount, 0.0, 0.92));

                outColor = vec4(max(hdr, vec3(0.0)), albedoAlpha.a);
            }
            """;

        return LinkProgram(vertexSource, fragmentSource);
    }

    private static int LinkProgram(string vertexSource, string fragmentSource)
    {
        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException(GL.GetProgramInfoLog(program));
        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }
}
