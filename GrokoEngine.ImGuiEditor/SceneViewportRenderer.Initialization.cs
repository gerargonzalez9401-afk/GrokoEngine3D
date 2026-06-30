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
        solidSpotLightMvpLocation = GL.GetUniformLocation(solidShader, "uSpotLightMvp");
        solidSpotShadowEnabledLocation = GL.GetUniformLocation(solidShader, "uSpotShadowEnabled");
        solidSpotShadowStrengthLocation = GL.GetUniformLocation(solidShader, "uSpotShadowStrength");
        solidPointShadowCubeLocation = GL.GetUniformLocation(solidShader, "uPointShadowCube");
        solidPointShadowEnabledLocation = GL.GetUniformLocation(solidShader, "uPointShadowEnabled");
        solidPointShadowStrengthLocation = GL.GetUniformLocation(solidShader, "uPointShadowStrength");
        solidPointShadowPosLocation = GL.GetUniformLocation(solidShader, "uPointShadowPos");
        solidPointShadowFarLocation = GL.GetUniformLocation(solidShader, "uPointShadowFar");
        solidShadowBiasScaleLocation = GL.GetUniformLocation(solidShader, "uShadowBiasScale");
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
        }
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
        uBrdfLut = GL.GetUniformLocation(solidShader, "uBrdfLut");
        uHasBrdfLut = GL.GetUniformLocation(solidShader, "uHasBrdfLut");
        uIblDiffuseStrength = GL.GetUniformLocation(solidShader, "uIblDiffuseStrength");
        uIblSpecularStrength = GL.GetUniformLocation(solidShader, "uIblSpecularStrength");
        uAoStrength = GL.GetUniformLocation(solidShader, "uAoStrength");
        uFogDensity = GL.GetUniformLocation(solidShader, "uFogDensity");
        uFogColor = GL.GetUniformLocation(solidShader, "uFogColor");
        uVolumetricStrength = GL.GetUniformLocation(solidShader, "uVolumetricStrength");
        uDebugView = GL.GetUniformLocation(solidShader, "uDebugView");

        terrainShader = CreateTerrainShader();
        terrainMvpLocation = GL.GetUniformLocation(terrainShader, "uMvp");
        terrainModelLocation = GL.GetUniformLocation(terrainShader, "uModel");
        terrainSplatMapLocation = GL.GetUniformLocation(terrainShader, "uSplatMap");
        for (int i = 0; i < 4; i++)
        {
            terrainLayerLocations[i] = GL.GetUniformLocation(terrainShader, $"uLayer{i}");
            terrainHasLayerLocations[i] = GL.GetUniformLocation(terrainShader, $"uHasLayer{i}");
            terrainTilingLocations[i] = GL.GetUniformLocation(terrainShader, $"uTiling{i}");
        }
        terrainAmbientColorLocation = GL.GetUniformLocation(terrainShader, "uAmbientColor");
        terrainAmbientIntensityLocation = GL.GetUniformLocation(terrainShader, "uAmbientIntensity");
        terrainSkyStrengthLocation = GL.GetUniformLocation(terrainShader, "uSkyStrength");
        terrainDirDirLocation = GL.GetUniformLocation(terrainShader, "uDirDir");
        terrainDirColorLocation = GL.GetUniformLocation(terrainShader, "uDirColor");
        terrainDirIntensityLocation = GL.GetUniformLocation(terrainShader, "uDirIntensity");

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
        for (int i = 0; i < MaxGpuBones; i++)
            _shadowBoneMatrixLocations[i] = GL.GetUniformLocation(_shadowShader, $"uBones[{i}]");
        _pointShadowShader = CreatePointShadowShader();
        _pointShadowMvpLocation = GL.GetUniformLocation(_pointShadowShader, "uMvp");
        _pointShadowModelLocation = GL.GetUniformLocation(_pointShadowShader, "uModel");
        _pointShadowUseInstancingLocation = GL.GetUniformLocation(_pointShadowShader, "uUseInstancing");
        _pointShadowUseSkinningLocation = GL.GetUniformLocation(_pointShadowShader, "uUseSkinning");
        _pointShadowLightPosLocation = GL.GetUniformLocation(_pointShadowShader, "uLightPos");
        _pointShadowFarLocation = GL.GetUniformLocation(_pointShadowShader, "uFarPlane");
        for (int i = 0; i < MaxGpuBones; i++)
            _pointShadowBoneMatrixLocations[i] = GL.GetUniformLocation(_pointShadowShader, $"uBones[{i}]");
        EnsureShadowResources();

        _particleVao = GL.GenVertexArray();
        _particleVbo = GL.GenBuffer();
        GL.BindVertexArray(_particleVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
        int ps = Unsafe.SizeOf<ParticleVertex>();
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, ps, 0);   // center
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, ps, 12);  // offset
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, ps, 20);  // color
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, ps, 36);  // rotation
        GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, ps, 40);  // uv
        GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, ps, 48);  // absolute vertices
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

}
