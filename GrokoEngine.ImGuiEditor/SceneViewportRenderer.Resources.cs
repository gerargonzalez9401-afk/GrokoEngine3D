using OpenTK.Graphics.OpenGL4;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    public void Dispose()
    {
        foreach (int texture in textureCache.Values)
            if (texture != 0)
                GL.DeleteTexture(texture);
        textureCache.Clear();
        textureFileTimeCache.Clear();
        textureNextValidationTick.Clear();
        parsedMeshCache.Clear();
        ClearGpuMeshCache();
        ClearSkinnedGpuMeshCache();
        assetWarningLog.Clear();

        GL.DeleteBuffer(vertexBuffer);
        GL.DeleteBuffer(solidVertexBuffer);
        GL.DeleteBuffer(_staticVertexBuffer);
        GL.DeleteBuffer(_trailVbo);
        if (_particleUnitQuadVbo != 0) GL.DeleteBuffer(_particleUnitQuadVbo);
        if (_particleInstanceVbo != 0) GL.DeleteBuffer(_particleInstanceVbo);
        if (_particleStretchedUnitQuadVbo != 0) GL.DeleteBuffer(_particleStretchedUnitQuadVbo);
        if (_particleStretchedInstanceVbo != 0) GL.DeleteBuffer(_particleStretchedInstanceVbo);
        if (_treeBillboardUnitQuadVbo != 0) GL.DeleteBuffer(_treeBillboardUnitQuadVbo);
        if (_treeBillboardInstanceVbo != 0) GL.DeleteBuffer(_treeBillboardInstanceVbo);
        if (dynamicInstanceBuffer != 0) GL.DeleteBuffer(dynamicInstanceBuffer);

        GL.DeleteVertexArray(vertexArray);
        GL.DeleteVertexArray(solidVertexArray);
        GL.DeleteVertexArray(_staticVertexArray);
        GL.DeleteVertexArray(_trailVao);
        if (_particleInstancedVao != 0) GL.DeleteVertexArray(_particleInstancedVao);
        if (_particleInstancedShader != 0) GL.DeleteProgram(_particleInstancedShader);
        if (_particleStretchedVao != 0) GL.DeleteVertexArray(_particleStretchedVao);
        if (_particleStretchedShader != 0) GL.DeleteProgram(_particleStretchedShader);
        if (_treeBillboardVao != 0) GL.DeleteVertexArray(_treeBillboardVao);
        if (_treeBillboardShader != 0) GL.DeleteProgram(_treeBillboardShader);

        if (_depthTex != 0) GL.DeleteTexture(_depthTex);
        if (_depthFbo != 0) GL.DeleteFramebuffer(_depthFbo);
        if (_occVbo != 0) GL.DeleteBuffer(_occVbo);
        if (_occVao != 0) GL.DeleteVertexArray(_occVao);
        if (_shadowArrayTex != 0) GL.DeleteTexture(_shadowArrayTex);
        if (_shadowFbo != 0) GL.DeleteFramebuffer(_shadowFbo);
        if (_spotShadowArrayTex != 0) GL.DeleteTexture(_spotShadowArrayTex);
        if (_spotShadowFbo != 0) GL.DeleteFramebuffer(_spotShadowFbo);
        if (_pointShadowCube != 0) GL.DeleteTexture(_pointShadowCube);
        if (_pointShadowFbo != 0) GL.DeleteFramebuffer(_pointShadowFbo);
        if (_shaderGraphWhiteTex != 0) GL.DeleteTexture(_shaderGraphWhiteTex);
        ReleaseDeferredTargets();
        if (_gbufferShader != 0) GL.DeleteProgram(_gbufferShader);
        if (_deferredLightingShader != 0) GL.DeleteProgram(_deferredLightingShader);
        if (_ssrShader != 0) GL.DeleteProgram(_ssrShader);
        if (_ssgiShader != 0) GL.DeleteProgram(_ssgiShader);
        if (_deferredQuadVbo != 0) GL.DeleteBuffer(_deferredQuadVbo);
        if (_deferredQuadVao != 0) GL.DeleteVertexArray(_deferredQuadVao);

        foreach (var entry in _shaderGraphCache.Values)
            if (entry.Program != 0)
                GL.DeleteProgram(entry.Program);
        _shaderGraphCache.Clear();

        GL.DeleteProgram(shader);
        GL.DeleteProgram(solidShader);
        if (_shaderGraphDepthPrepassShader != 0) GL.DeleteProgram(_shaderGraphDepthPrepassShader);
        if (_shadowShader != 0) GL.DeleteProgram(_shadowShader);
        if (_pointShadowShader != 0) GL.DeleteProgram(_pointShadowShader);
        GL.DeleteProgram(_particleShader);
        if (_skyboxShader != 0) GL.DeleteProgram(_skyboxShader);
        if (_skyboxVao != 0) GL.DeleteVertexArray(_skyboxVao);
        if (_envTexture != 0) GL.DeleteTexture(_envTexture);
        if (_envPrefilterCube != 0) GL.DeleteTexture(_envPrefilterCube);
        if (_brdfLutTexture != 0) GL.DeleteTexture(_brdfLutTexture);
        foreach (var entry in _reflectionProbeCubeCache.Values)
            if (entry.TextureId != 0)
                GL.DeleteTexture(entry.TextureId);
        _reflectionProbeCubeCache.Clear();

        DisposeTerrainPass();

        foreach (var qid in _occlusionQueries.Values)
            GL.DeleteQuery(qid);
        _occlusionQueries.Clear();
        _occlusionVisible.Clear();
        _occlusionQueryFrame.Clear();
    }
}
