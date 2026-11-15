using OpenTK.Graphics.OpenGL4;
using System;
using System.Runtime.CompilerServices;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    public SceneViewportRenderer()
    {
        // Sin esto, las consultas a samplerCube en los bordes de cada cara filtran
        // contra el borde de ESA cara únicamente — los texels de la cara vecina no
        // participan, así que el PCF de las point lights (que muestrea un disco
        // alrededor de la dirección luz→fragmento y a veces cruza de una cara a
        // otra) ve una discontinuidad justo en la costura entre caras del cubemap.
        // Eso dibuja arcos/curvas visibles en las sombras de point lights al subir
        // la calidad — exactamente el patrón de "lentes" reportado. Habilitar el
        // filtrado "seamless" hace que OpenGL interpole correctamente a través de
        // las costuras, igual que hacen los motores de producción (Unity/Unreal).
        GL.Enable(EnableCap.TextureCubeMapSeamless);

        vertexArray = GL.GenVertexArray();
        vertexBuffer = GL.GenBuffer();
        shader = CreateShader();
        mvpLocation = GL.GetUniformLocation(shader, "uMvp");

        solidVertexArray = GL.GenVertexArray();
        solidVertexBuffer = GL.GenBuffer();
        dynamicInstanceBuffer = GL.GenBuffer();
        InitializeInstanceBuffer();
        solidShader = CreateSolidShader();
        _shaderGraphDepthPrepassShader = CreateShaderGraphDepthPrepassShader();
        _shaderGraphDepthPrepassViewProjLocation = GL.GetUniformLocation(_shaderGraphDepthPrepassShader, "u_ViewProj");
        solidMvpLocation = GL.GetUniformLocation(solidShader, "uMvp");
        solidModelLocation = GL.GetUniformLocation(solidShader, "uModel");
        solidUseInstancingLocation = GL.GetUniformLocation(solidShader, "uUseInstancing");
        solidUseSkinningLocation = GL.GetUniformLocation(solidShader, "uUseSkinning");
        solidUseSurfaceUniformsLocation = GL.GetUniformLocation(solidShader, "uUseSurfaceUniforms");
        solidSurfaceColorLocation = GL.GetUniformLocation(solidShader, "uSurfaceColor");
        solidSurfaceMaterialLocation = GL.GetUniformLocation(solidShader, "uSurfaceMaterial");
        solidSurfaceEmissionLocation = GL.GetUniformLocation(solidShader, "uSurfaceEmission");
        solidTextureLocation = GL.GetUniformLocation(solidShader, "uTexture");
        solidHasTextureLocation = GL.GetUniformLocation(solidShader, "uHasTexture");
        solidNormalMapLocation = GL.GetUniformLocation(solidShader, "uNormalMap");
        solidHasNormalMapLocation = GL.GetUniformLocation(solidShader, "uHasNormalMap");
        solidRoughnessMapLocation = GL.GetUniformLocation(solidShader, "uRoughnessMap");
        solidHasRoughnessMapLocation = GL.GetUniformLocation(solidShader, "uHasRoughnessMap");
        solidMetallicMapLocation = GL.GetUniformLocation(solidShader, "uMetallicMap");
        solidHasMetallicMapLocation = GL.GetUniformLocation(solidShader, "uHasMetallicMap");
        solidLightmapLocation = GL.GetUniformLocation(solidShader, "uLightmap");
        solidHasLightmapLocation = GL.GetUniformLocation(solidShader, "uHasLightmap");
        solidLightmapIntensityLocation = GL.GetUniformLocation(solidShader, "uLightmapIntensity");
        solidLightmapScaleOffsetLocation = GL.GetUniformLocation(solidShader, "uLightmapScaleOffset");
        solidShadowmaskLocation = GL.GetUniformLocation(solidShader, "uShadowmask");
        solidHasShadowmaskLocation = GL.GetUniformLocation(solidShader, "uHasShadowmask");
        solidDirShadowmaskChannelLocation = GL.GetUniformLocation(solidShader, "uDirShadowmaskChannel");
        solidMixedLightingModeLocation = GL.GetUniformLocation(solidShader, "uMixedLightingMode");
        solidDistanceShadowmaskLocation = GL.GetUniformLocation(solidShader, "uDistanceShadowmask");
        solidShadowmaskDistanceLocation = GL.GetUniformLocation(solidShader, "uShadowmaskDistance");
        solidShadowmaskDistanceFadeLocation = GL.GetUniformLocation(solidShader, "uShadowmaskDistanceFade");
        solidRefractivePassLocation = GL.GetUniformLocation(solidShader, "uRefractivePass");
        solidOpaqueTextureLocation = GL.GetUniformLocation(solidShader, "uOpaqueTexture");
        solidScreenTexelLocation = GL.GetUniformLocation(solidShader, "uScreenTexel");
        solidRefractionStrengthLocation = GL.GetUniformLocation(solidShader, "uRefractionStrength");
        solidDistortionStrengthLocation = GL.GetUniformLocation(solidShader, "uDistortionStrength");
        solidAlphaClipEnabledLocation = GL.GetUniformLocation(solidShader, "uAlphaClipEnabled");
        solidAlphaClipThresholdLocation = GL.GetUniformLocation(solidShader, "uAlphaClipThreshold");
        solidLightMvpLocation = GL.GetUniformLocation(solidShader, "uLightMvp");
        solidCascadeCountLocation = GL.GetUniformLocation(solidShader, "uCascadeCount");
        solidCameraPositionLocation = GL.GetUniformLocation(solidShader, "uShadowCameraPos");
        for (int i = 0; i < MaxDirectionalShadowCascades; i++)
        {
            solidCascadeLightMvpLocations[i] = GL.GetUniformLocation(solidShader, $"uCascadeLightMvp[{i}]");
            solidCascadeSplitLocations[i] = GL.GetUniformLocation(solidShader, $"uCascadeSplit[{i}]");
        }
        solidShadowMapLocation = GL.GetUniformLocation(solidShader, "uShadowMap");
        solidShadowEnabledLocation = GL.GetUniformLocation(solidShader, "uShadowEnabled");
        solidShadowStrengthLocation = GL.GetUniformLocation(solidShader, "uShadowStrength");
        solidShadowPcfRadiusLocation = GL.GetUniformLocation(solidShader, "uShadowPcfRadius");
        solidSpotShadowMapLocation = GL.GetUniformLocation(solidShader, "uSpotShadowMap");
        for (int i = 0; i < MaxSpotLights; i++)
        {
            solidSpotLightMvpLocations[i] = GL.GetUniformLocation(solidShader, $"uSpotLightMvp[{i}]");
            solidSpotShadowEnabledLocations[i] = GL.GetUniformLocation(solidShader, $"uSpotShadowEnabled[{i}]");
            solidSpotShadowStrengthLocations[i] = GL.GetUniformLocation(solidShader, $"uSpotShadowStrength[{i}]");
        }
        solidPointShadowCubeLocation = GL.GetUniformLocation(solidShader, "uPointShadowCube");
        solidPointShadowEnabledLocation = GL.GetUniformLocation(solidShader, "uPointShadowEnabled");
        solidPointShadowStrengthLocation = GL.GetUniformLocation(solidShader, "uPointShadowStrength");
        solidPointShadowPosLocation = GL.GetUniformLocation(solidShader, "uPointShadowPos");
        solidPointShadowFarLocation = GL.GetUniformLocation(solidShader, "uPointShadowFar");
        solidPointShadowTexelSizeLocation = GL.GetUniformLocation(solidShader, "uPointShadowTexelSize");
        solidPointShadowLightIndexLocation = GL.GetUniformLocation(solidShader, "uPointShadowLightIndex");
        solidShadowBiasScaleLocation = GL.GetUniformLocation(solidShader, "uShadowBiasScale");
        solidShadowNormalBiasLocation = GL.GetUniformLocation(solidShader, "uShadowNormalBias");
        for (int i = 0; i < MaxGpuBones; i++)
            solidBoneMatrixLocations[i] = GL.GetUniformLocation(solidShader, $"uBones[{i}]");
        uAmbientColor = GL.GetUniformLocation(solidShader, "uAmbientColor");
        uAmbientIntensity = GL.GetUniformLocation(solidShader, "uAmbientIntensity");
        uSkyStrength = GL.GetUniformLocation(solidShader, "uSkyStrength");
        uDirDir = GL.GetUniformLocation(solidShader, "uDirDir");
        uDirColor = GL.GetUniformLocation(solidShader, "uDirColor");
        uDirIntensity = GL.GetUniformLocation(solidShader, "uDirIntensity");
        uCameraPos = GL.GetUniformLocation(solidShader, "uCameraPos");
        uPointCount = GL.GetUniformLocation(solidShader, "uPointCount");
        for (int i = 0; i < MaxPointLights; i++)
        {
            uPointPos[i] = GL.GetUniformLocation(solidShader, $"uPointPos[{i}]");
            uPointColor[i] = GL.GetUniformLocation(solidShader, $"uPointColor[{i}]");
            uPointIntensity[i] = GL.GetUniformLocation(solidShader, $"uPointIntensity[{i}]");
            uPointRange[i] = GL.GetUniformLocation(solidShader, $"uPointRange[{i}]");
            solidPointShadowmaskChannelLocations[i] = GL.GetUniformLocation(solidShader, $"uPointShadowmaskChannel[{i}]");
        }
        uSpotCount = GL.GetUniformLocation(solidShader, "uSpotCount");
        for (int i = 0; i < MaxSpotLights; i++)
        {
            uSpotPos[i] = GL.GetUniformLocation(solidShader, $"uSpotPos[{i}]");
            uSpotDir[i] = GL.GetUniformLocation(solidShader, $"uSpotDir[{i}]");
            uSpotColor[i] = GL.GetUniformLocation(solidShader, $"uSpotColor[{i}]");
            uSpotIntensity[i] = GL.GetUniformLocation(solidShader, $"uSpotIntensity[{i}]");
            uSpotRange[i] = GL.GetUniformLocation(solidShader, $"uSpotRange[{i}]");
            uSpotAngle[i] = GL.GetUniformLocation(solidShader, $"uSpotAngle[{i}]");
            uSpotShadowSlotLocations[i] = GL.GetUniformLocation(solidShader, $"uSpotShadowSlot[{i}]");
            solidSpotShadowmaskChannelLocations[i] = GL.GetUniformLocation(solidShader, $"uSpotShadowmaskChannel[{i}]");
        }
        uSpotCookieLocation = GL.GetUniformLocation(solidShader, "uSpotCookieArray");
        uSpotIesLutLocation = GL.GetUniformLocation(solidShader, "uSpotIesArray");
        uPointIesLutLocation = GL.GetUniformLocation(solidShader, "uPointIesArray");
        for (int i = 0; i < MaxSpotLights; i++)
        {
            uSpotHasCookieLoc[i] = GL.GetUniformLocation(solidShader, $"uSpotHasCookie[{i}]");
            uSpotCookieMvpLoc[i] = GL.GetUniformLocation(solidShader, $"uSpotCookieMvp[{i}]");
            uSpotHasIesLoc[i] = GL.GetUniformLocation(solidShader, $"uSpotHasIes[{i}]");
        }
        for (int i = 0; i < MaxPointLights; i++)
            uPointHasIesLoc[i] = GL.GetUniformLocation(solidShader, $"uPointHasIes[{i}]");
        uAreaCount = GL.GetUniformLocation(solidShader, "uAreaCount");
        for (int i = 0; i < MaxAreaLights; i++)
        {
            uAreaPos[i] = GL.GetUniformLocation(solidShader, $"uAreaPos[{i}]");
            uAreaDir[i] = GL.GetUniformLocation(solidShader, $"uAreaDir[{i}]");
            uAreaColor[i] = GL.GetUniformLocation(solidShader, $"uAreaColor[{i}]");
            uAreaIntensity[i] = GL.GetUniformLocation(solidShader, $"uAreaIntensity[{i}]");
            uAreaRange[i] = GL.GetUniformLocation(solidShader, $"uAreaRange[{i}]");
            uAreaSize[i] = GL.GetUniformLocation(solidShader, $"uAreaSize[{i}]");
        }
        uColorSpaceLinear = GL.GetUniformLocation(solidShader, "uColorSpaceLinear");
        uUseIBL = GL.GetUniformLocation(solidShader, "uUseIBL");
        uEnvMap = GL.GetUniformLocation(solidShader, "uEnvMap");
        uHasEnvMap = GL.GetUniformLocation(solidShader, "uHasEnvMap");
        uEnvMaxLod = GL.GetUniformLocation(solidShader, "uEnvMaxLod");
        uEnvDiffuseColor = GL.GetUniformLocation(solidShader, "uEnvDiffuseColor");
        uUseEnvSh = GL.GetUniformLocation(solidShader, "uUseEnvSh");
        for (int i = 0; i < uEnvSh.Length; i++)
            uEnvSh[i] = GL.GetUniformLocation(solidShader, $"uEnvSh[{i}]");
        uEnvPrefilterMap = GL.GetUniformLocation(solidShader, "uEnvPrefilterMap");
        uHasEnvPrefilterMap = GL.GetUniformLocation(solidShader, "uHasEnvPrefilterMap");
        uEnvPrefilterMaxLod = GL.GetUniformLocation(solidShader, "uEnvPrefilterMaxLod");
        uActiveReflectionProbeMap = GL.GetUniformLocation(solidShader, "uActiveReflectionProbeMap");
        uHasActiveReflectionProbeMap = GL.GetUniformLocation(solidShader, "uHasActiveReflectionProbeMap");
        uActiveReflectionProbeMaxLod = GL.GetUniformLocation(solidShader, "uActiveReflectionProbeMaxLod");
        uActiveReflectionProbeCenter = GL.GetUniformLocation(solidShader, "uActiveReflectionProbeCenter");
        uActiveReflectionProbeHalfSize = GL.GetUniformLocation(solidShader, "uActiveReflectionProbeHalfSize");
        uActiveReflectionProbeBlendDistance = GL.GetUniformLocation(solidShader, "uActiveReflectionProbeBlendDistance");
        uActiveReflectionProbeIntensity = GL.GetUniformLocation(solidShader, "uActiveReflectionProbeIntensity");
        uActiveReflectionProbeBoxProjection = GL.GetUniformLocation(solidShader, "uActiveReflectionProbeBoxProjection");
        uSecondaryReflectionProbeMap = GL.GetUniformLocation(solidShader, "uSecondaryReflectionProbeMap");
        uHasSecondaryReflectionProbeMap = GL.GetUniformLocation(solidShader, "uHasSecondaryReflectionProbeMap");
        uSecondaryReflectionProbeMaxLod = GL.GetUniformLocation(solidShader, "uSecondaryReflectionProbeMaxLod");
        uSecondaryReflectionProbeCenter = GL.GetUniformLocation(solidShader, "uSecondaryReflectionProbeCenter");
        uSecondaryReflectionProbeHalfSize = GL.GetUniformLocation(solidShader, "uSecondaryReflectionProbeHalfSize");
        uSecondaryReflectionProbeBlendDistance = GL.GetUniformLocation(solidShader, "uSecondaryReflectionProbeBlendDistance");
        uSecondaryReflectionProbeIntensity = GL.GetUniformLocation(solidShader, "uSecondaryReflectionProbeIntensity");
        uSecondaryReflectionProbeBoxProjection = GL.GetUniformLocation(solidShader, "uSecondaryReflectionProbeBoxProjection");
        uBrdfLut = GL.GetUniformLocation(solidShader, "uBrdfLut");
        uHasBrdfLut = GL.GetUniformLocation(solidShader, "uHasBrdfLut");
        uIblDiffuseStrength = GL.GetUniformLocation(solidShader, "uIblDiffuseStrength");
        uIblSpecularStrength = GL.GetUniformLocation(solidShader, "uIblSpecularStrength");
        uAoStrength = GL.GetUniformLocation(solidShader, "uAoStrength");
        uFogDensity = GL.GetUniformLocation(solidShader, "uFogDensity");
        uFogColor = GL.GetUniformLocation(solidShader, "uFogColor");
        uFogHeight = GL.GetUniformLocation(solidShader, "uFogHeight");
        uFogHeightFalloff = GL.GetUniformLocation(solidShader, "uFogHeightFalloff");
        uVolumetricStrength = GL.GetUniformLocation(solidShader, "uVolumetricStrength");
        uDebugView = GL.GetUniformLocation(solidShader, "uDebugView");

        // Terrain CDLOD: shader, rejilla compartida y locations (incluidas las de iluminación
        // que consume ApplyTerrainLighting) viven en SceneViewportRenderer.TerrainPass.cs.
        InitializeTerrainPass();

        GL.BindVertexArray(vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);

        int stride = Unsafe.SizeOf<LineVertex>();
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 12);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        GL.BindVertexArray(solidVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, solidVertexBuffer);

        int solidStride = Unsafe.SizeOf<SolidVertex>();
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, solidStride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, solidStride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, solidStride, 24);
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, solidStride, 40);
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, solidStride, 48);
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, solidStride, 64);
        GL.EnableVertexAttribArray(12);
        GL.VertexAttribPointer(12, 2, VertexAttribPointerType.Float, false, solidStride, 80);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // VAO/VBO para el batch estático (misma configuración de atributos)
        _staticVertexArray = GL.GenVertexArray();
        _staticVertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_staticVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _staticVertexBuffer);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, solidStride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, solidStride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, solidStride, 24);
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, solidStride, 40);
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, solidStride, 48);
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, solidStride, 64);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // ── Particle shader + VAO ──────────────────────────────────
        _particleShader = CreateParticleShader();
        _particleMvpLoc = GL.GetUniformLocation(_particleShader, "uMvp");
        _particleRightLoc = GL.GetUniformLocation(_particleShader, "uCamRight");
        _particleUpLoc = GL.GetUniformLocation(_particleShader, "uCamUp");
        _particleTexLoc = GL.GetUniformLocation(_particleShader, "uTexture");
        _particleHasTexLoc = GL.GetUniformLocation(_particleShader, "uHasTexture");
        _particleSoftLoc = GL.GetUniformLocation(_particleShader, "uSoftParticles");
        _particleDepthTexLoc = GL.GetUniformLocation(_particleShader, "uDepthTex");
        _particleScreenSizeLoc = GL.GetUniformLocation(_particleShader, "uScreenSize");
        _particleSoftRangeLoc = GL.GetUniformLocation(_particleShader, "uSoftRange");
        _particleOpaqueTexLoc = GL.GetUniformLocation(_particleShader, "uOpaqueTexture");
        _particleDistortionLoc = GL.GetUniformLocation(_particleShader, "uDistortionEnabled");
        _particleDistortionStrengthLoc = GL.GetUniformLocation(_particleShader, "uDistortionStrength");
        _particleRefractionStrengthLoc = GL.GetUniformLocation(_particleShader, "uRefractionStrength");

        // ── Particle GPU instancing: shader + quad unitario + VBO de instancias ────
        _particleInstancedShader = CreateParticleInstancedShader();
        _particleInstMvpLoc = GL.GetUniformLocation(_particleInstancedShader, "uMvp");
        _particleInstRightLoc = GL.GetUniformLocation(_particleInstancedShader, "uCamRight");
        _particleInstUpLoc = GL.GetUniformLocation(_particleInstancedShader, "uCamUp");
        _particleInstTexLoc = GL.GetUniformLocation(_particleInstancedShader, "uTexture");
        _particleInstHasTexLoc = GL.GetUniformLocation(_particleInstancedShader, "uHasTexture");
        _particleInstSoftLoc = GL.GetUniformLocation(_particleInstancedShader, "uSoftParticles");
        _particleInstDepthTexLoc = GL.GetUniformLocation(_particleInstancedShader, "uDepthTex");
        _particleInstScreenSizeLoc = GL.GetUniformLocation(_particleInstancedShader, "uScreenSize");
        _particleInstSoftRangeLoc = GL.GetUniformLocation(_particleInstancedShader, "uSoftRange");
        _particleInstOpaqueTexLoc = GL.GetUniformLocation(_particleInstancedShader, "uOpaqueTexture");
        _particleInstDistortionLoc = GL.GetUniformLocation(_particleInstancedShader, "uDistortionEnabled");
        _particleInstDistortionStrengthLoc = GL.GetUniformLocation(_particleInstancedShader, "uDistortionStrength");
        _particleInstRefractionStrengthLoc = GL.GetUniformLocation(_particleInstancedShader, "uRefractionStrength");

        float[] particleUnitQuad =
        {
            -0.5f, -0.5f, 0f, 1f,
             0.5f, -0.5f, 1f, 1f,
             0.5f,  0.5f, 1f, 0f,
            -0.5f, -0.5f, 0f, 1f,
             0.5f,  0.5f, 1f, 0f,
            -0.5f,  0.5f, 0f, 0f,
        };
        _particleUnitQuadVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleUnitQuadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, particleUnitQuad.Length * sizeof(float), particleUnitQuad, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        _particleInstanceVbo = GL.GenBuffer();

        _particleInstancedVao = GL.GenVertexArray();
        GL.BindVertexArray(_particleInstancedVao);

        // Atributos por-vértice (divisor 0): offset unitario + uv unitaria del quad compartido
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleUnitQuadVbo);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 16, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 16, 8);

        // Atributos por-instancia (divisor 1): un registro chico por partícula
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleInstanceVbo);
        int pis = Unsafe.SizeOf<ParticleInstance>();
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, pis, 0);   // center
        GL.VertexAttribDivisor(2, 1);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, pis, 12);  // size
        GL.VertexAttribDivisor(3, 1);
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, pis, 16);  // color
        GL.VertexAttribDivisor(4, 1);
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, pis, 32);  // rotation
        GL.VertexAttribDivisor(5, 1);
        GL.EnableVertexAttribArray(6); GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, pis, 36);  // uvRect
        GL.VertexAttribDivisor(6, 1);
        GL.EnableVertexAttribArray(7); GL.VertexAttribPointer(7, 2, VertexAttribPointerType.Float, false, pis, 52);  // pivot
        GL.VertexAttribDivisor(7, 1);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // Trail VAO (misma estructura que partículas)
        _trailVao = GL.GenVertexArray();
        _trailVbo = GL.GenBuffer();
        GL.BindVertexArray(_trailVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _trailVbo);
        int ps2 = Unsafe.SizeOf<ParticleVertex>();
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, ps2, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, ps2, 12);
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, ps2, 20);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, ps2, 36);
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, ps2, 40);
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, ps2, 48);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // ── Skybox HDRI (IBL Fase 2) ───────────────────────────────
        _skyboxShader = CreateSkyboxShader();
        _skyboxVao = GL.GenVertexArray(); // VAO vacío: el triángulo fullscreen se genera con gl_VertexID
        _skyEnvLoc = GL.GetUniformLocation(_skyboxShader, "uEnvMap");
        _skyCamFrontLoc = GL.GetUniformLocation(_skyboxShader, "uCamFront");
        _skyCamUpLoc = GL.GetUniformLocation(_skyboxShader, "uCamUp");
        _skyTanHalfFovLoc = GL.GetUniformLocation(_skyboxShader, "uTanHalfFov");
        _skyAspectLoc = GL.GetUniformLocation(_skyboxShader, "uAspect");
        _skySunDirLoc = GL.GetUniformLocation(_skyboxShader, "uSunDir");
        _skySunColorLoc = GL.GetUniformLocation(_skyboxShader, "uSunColor");
        _skySunAngularLoc = GL.GetUniformLocation(_skyboxShader, "uSunAngular");
        _skySunEnabledLoc = GL.GetUniformLocation(_skyboxShader, "uSunEnabled");
        _brdfLutTexture = CreateBrdfLutTexture();

        // ── Physically Based Sky (analitico, alternativa al HDRI) ──
        _proceduralSkyShader = CreateProceduralSkyShader();
        _proSkyCamFrontLoc = GL.GetUniformLocation(_proceduralSkyShader, "uCamFront");
        _proSkyCamUpLoc = GL.GetUniformLocation(_proceduralSkyShader, "uCamUp");
        _proSkyTanHalfFovLoc = GL.GetUniformLocation(_proceduralSkyShader, "uTanHalfFov");
        _proSkyAspectLoc = GL.GetUniformLocation(_proceduralSkyShader, "uAspect");
        _proSkySunDirLoc = GL.GetUniformLocation(_proceduralSkyShader, "uSunDir");
        _proSkySunColorLoc = GL.GetUniformLocation(_proceduralSkyShader, "uSunColor");
        _proSkySunAngularLoc = GL.GetUniformLocation(_proceduralSkyShader, "uSunAngular");
        _proSkySunEnabledLoc = GL.GetUniformLocation(_proceduralSkyShader, "uSunEnabled");
        _skyTurbidityLoc = GL.GetUniformLocation(_proceduralSkyShader, "uTurbidity");
        _skyExposureLoc = GL.GetUniformLocation(_proceduralSkyShader, "uSkyExposure");

        // VAO/VBO dedicado para occlusion query bounding boxes
        _occVao = GL.GenVertexArray();
        _occVbo = GL.GenBuffer();
        GL.BindVertexArray(_occVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _occVbo);
        int occStride = Unsafe.SizeOf<SolidVertex>();
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, occStride, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, occStride, 12);
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, occStride, 24);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, occStride, 40);
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, occStride, 48);
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, occStride, 64);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // Depth FBO — se dimensiona en el primer frame
        _depthFbo = GL.GenFramebuffer();
        _depthTex = GL.GenTexture();
        _shadowShader = CreateShadowShader();
        _shadowMvpLocation = GL.GetUniformLocation(_shadowShader, "uMvp");
        _shadowModelLocation = GL.GetUniformLocation(_shadowShader, "uModel");
        _shadowUseInstancingLocation = GL.GetUniformLocation(_shadowShader, "uUseInstancing");
        _shadowUseSkinningLocation = GL.GetUniformLocation(_shadowShader, "uUseSkinning");
        _shadowTextureLocation = GL.GetUniformLocation(_shadowShader, "uTexture");
        _shadowHasTextureLocation = GL.GetUniformLocation(_shadowShader, "uHasTexture");
        _shadowAlphaClipLocation = GL.GetUniformLocation(_shadowShader, "uAlphaClip");
        _shadowAlphaClipThresholdLocation = GL.GetUniformLocation(_shadowShader, "uAlphaClipThreshold");
        for (int i = 0; i < MaxGpuBones; i++)
            _shadowBoneMatrixLocations[i] = GL.GetUniformLocation(_shadowShader, $"uBones[{i}]");
        _pointShadowShader = CreatePointShadowShader();
        _pointShadowMvpLocation = GL.GetUniformLocation(_pointShadowShader, "uMvp");
        _pointShadowModelLocation = GL.GetUniformLocation(_pointShadowShader, "uModel");
        _pointShadowUseInstancingLocation = GL.GetUniformLocation(_pointShadowShader, "uUseInstancing");
        _pointShadowUseSkinningLocation = GL.GetUniformLocation(_pointShadowShader, "uUseSkinning");
        _pointShadowTextureLocation = GL.GetUniformLocation(_pointShadowShader, "uTexture");
        _pointShadowHasTextureLocation = GL.GetUniformLocation(_pointShadowShader, "uHasTexture");
        _pointShadowAlphaClipLocation = GL.GetUniformLocation(_pointShadowShader, "uAlphaClip");
        _pointShadowAlphaClipThresholdLocation = GL.GetUniformLocation(_pointShadowShader, "uAlphaClipThreshold");
        _pointShadowLightPosLocation = GL.GetUniformLocation(_pointShadowShader, "uLightPos");
        _pointShadowFarLocation = GL.GetUniformLocation(_pointShadowShader, "uFarPlane");
        for (int i = 0; i < MaxGpuBones; i++)
            _pointShadowBoneMatrixLocations[i] = GL.GetUniformLocation(_pointShadowShader, $"uBones[{i}]");
        EnsureShadowResources();

        // ── Stretched billboard GPU instancing: shader + quad unitario + VBO de instancias ──
        _particleStretchedShader = CreateParticleStretchedShader();
        _particleStrMvpLoc = GL.GetUniformLocation(_particleStretchedShader, "uMvp");
        _particleStrRightLoc = GL.GetUniformLocation(_particleStretchedShader, "uCamRight");
        _particleStrTexLoc = GL.GetUniformLocation(_particleStretchedShader, "uTexture");
        _particleStrHasTexLoc = GL.GetUniformLocation(_particleStretchedShader, "uHasTexture");
        _particleStrSoftLoc = GL.GetUniformLocation(_particleStretchedShader, "uSoftParticles");
        _particleStrDepthTexLoc = GL.GetUniformLocation(_particleStretchedShader, "uDepthTex");
        _particleStrScreenSizeLoc = GL.GetUniformLocation(_particleStretchedShader, "uScreenSize");
        _particleStrSoftRangeLoc = GL.GetUniformLocation(_particleStretchedShader, "uSoftRange");
        _particleStrOpaqueTexLoc = GL.GetUniformLocation(_particleStretchedShader, "uOpaqueTexture");
        _particleStrDistortionLoc = GL.GetUniformLocation(_particleStretchedShader, "uDistortionEnabled");
        _particleStrDistortionStrengthLoc = GL.GetUniformLocation(_particleStretchedShader, "uDistortionStrength");
        _particleStrRefractionStrengthLoc = GL.GetUniformLocation(_particleStretchedShader, "uRefractionStrength");

        // Quad unitario en espacio (perp, fwd): offset.x=eje perpendicular, offset.y=eje de
        // velocidad. Mismo orden de triángulos/UV que el AddStretchedQuad original en CPU.
        float[] particleStretchedUnitQuad =
        {
            -1f,  1f, 0f, 1f,
             1f,  1f, 1f, 1f,
             1f, -1f, 1f, 0f,
            -1f,  1f, 0f, 1f,
             1f, -1f, 1f, 0f,
            -1f, -1f, 0f, 0f,
        };
        _particleStretchedUnitQuadVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleStretchedUnitQuadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, particleStretchedUnitQuad.Length * sizeof(float), particleStretchedUnitQuad, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        _particleStretchedInstanceVbo = GL.GenBuffer();

        _particleStretchedVao = GL.GenVertexArray();
        GL.BindVertexArray(_particleStretchedVao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleStretchedUnitQuadVbo);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 16, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 16, 8);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleStretchedInstanceVbo);
        int psi = Unsafe.SizeOf<ParticleStretchedInstance>();
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, psi, 0);   // center
        GL.VertexAttribDivisor(2, 1);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, psi, 12);  // halfWidth
        GL.VertexAttribDivisor(3, 1);
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, psi, 16);  // velocityDir
        GL.VertexAttribDivisor(4, 1);
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, psi, 28);  // halfHeight
        GL.VertexAttribDivisor(5, 1);
        GL.EnableVertexAttribArray(6); GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, psi, 32);  // color
        GL.VertexAttribDivisor(6, 1);
        GL.EnableVertexAttribArray(7); GL.VertexAttribPointer(7, 4, VertexAttribPointerType.Float, false, psi, 48);  // uvRect
        GL.VertexAttribDivisor(7, 1);
        GL.EnableVertexAttribArray(8); GL.VertexAttribPointer(8, 2, VertexAttribPointerType.Float, false, psi, 64);  // pivot
        GL.VertexAttribDivisor(8, 1);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        // ── Billboards de árbol de Terrain: shader + quad unitario + VBO de instancias ──
        _treeBillboardShader = CreateTreeBillboardShader();
        _treeBillboardMvpLoc = GL.GetUniformLocation(_treeBillboardShader, "uMvp");
        _treeBillboardTimeLoc = GL.GetUniformLocation(_treeBillboardShader, "uTime");
        _treeBillboardWindLoc = GL.GetUniformLocation(_treeBillboardShader, "uWind");
        _treeBillboardTexLoc = GL.GetUniformLocation(_treeBillboardShader, "uTexture");
        _treeBillboardHasTexLoc = GL.GetUniformLocation(_treeBillboardShader, "uHasTexture");

        // Quad unitario anclado al piso: X en -0.5..0.5 (ancho), Y en 0..1 (0 = base, 1 = copa).
        float[] treeBillboardUnitQuad =
        {
            -0.5f, 0f, 0f, 1f,
             0.5f, 0f, 1f, 1f,
             0.5f, 1f, 1f, 0f,
            -0.5f, 0f, 0f, 1f,
             0.5f, 1f, 1f, 0f,
            -0.5f, 1f, 0f, 0f,
        };
        _treeBillboardUnitQuadVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _treeBillboardUnitQuadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, treeBillboardUnitQuad.Length * sizeof(float), treeBillboardUnitQuad, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        _treeBillboardInstanceVbo = GL.GenBuffer();

        _treeBillboardVao = GL.GenVertexArray();
        GL.BindVertexArray(_treeBillboardVao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _treeBillboardUnitQuadVbo);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 16, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 16, 8);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _treeBillboardInstanceVbo);
        int tbi = Unsafe.SizeOf<TreeBillboardInstance>();
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, tbi, 0);   // center
        GL.VertexAttribDivisor(2, 1);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, tbi, 12);  // halfWidth
        GL.VertexAttribDivisor(3, 1);
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, tbi, 16);  // height
        GL.VertexAttribDivisor(4, 1);
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, tbi, 20);  // yaw
        GL.VertexAttribDivisor(5, 1);
        GL.EnableVertexAttribArray(6); GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, tbi, 24);  // color
        GL.VertexAttribDivisor(6, 1);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

}
