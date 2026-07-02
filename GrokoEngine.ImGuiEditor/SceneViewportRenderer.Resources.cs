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
        parsedMeshCache.Clear();
        ClearGpuMeshCache();
        ClearSkinnedGpuMeshCache();
        assetWarningLog.Clear();

        GL.DeleteBuffer(vertexBuffer);
        GL.DeleteBuffer(solidVertexBuffer);
        GL.DeleteBuffer(_staticVertexBuffer);
        GL.DeleteBuffer(_particleVbo);
        GL.DeleteBuffer(_trailVbo);
        if (_particleUnitQuadVbo != 0) GL.DeleteBuffer(_particleUnitQuadVbo);
        if (_particleInstanceVbo != 0) GL.DeleteBuffer(_particleInstanceVbo);
        if (dynamicInstanceBuffer != 0) GL.DeleteBuffer(dynamicInstanceBuffer);

        GL.DeleteVertexArray(vertexArray);
        GL.DeleteVertexArray(solidVertexArray);
        GL.DeleteVertexArray(_staticVertexArray);
        GL.DeleteVertexArray(_particleVao);
        GL.DeleteVertexArray(_trailVao);
        if (_particleInstancedVao != 0) GL.DeleteVertexArray(_particleInstancedVao);
        if (_particleInstancedShader != 0) GL.DeleteProgram(_particleInstancedShader);

        if (_depthTex != 0) GL.DeleteTexture(_depthTex);
        if (_depthFbo != 0) GL.DeleteFramebuffer(_depthFbo);
        if (_occVbo != 0) GL.DeleteBuffer(_occVbo);
        if (_occVao != 0) GL.DeleteVertexArray(_occVao);
        if (_shadowArrayTex != 0) GL.DeleteTexture(_shadowArrayTex);
        if (_shadowFbo != 0) GL.DeleteFramebuffer(_shadowFbo);
        if (_spotShadowTex != 0) GL.DeleteTexture(_spotShadowTex);
        if (_spotShadowFbo != 0) GL.DeleteFramebuffer(_spotShadowFbo);
        if (_pointShadowCube != 0) GL.DeleteTexture(_pointShadowCube);
        if (_pointShadowFbo != 0) GL.DeleteFramebuffer(_pointShadowFbo);
        if (_shaderGraphWhiteTex != 0) GL.DeleteTexture(_shaderGraphWhiteTex);

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

        foreach (var qid in _occlusionQueries.Values)
            GL.DeleteQuery(qid);
        _occlusionQueries.Clear();
        _occlusionVisible.Clear();
        _occlusionQueryFrame.Clear();
    }
}
