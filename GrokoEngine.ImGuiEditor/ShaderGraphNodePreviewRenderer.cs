using System;
using System.Globalization;
using System.Linq;
using GrokoShaderGraphPro.Models;
using OpenTK.Graphics.OpenGL4;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace GrokoEngine.ImGuiEditor;

internal sealed class ShaderGraphNodePreviewRenderer : IDisposable
{
    private int fbo;
    private int colorTex;
    private int depthRbo;
    private int vao;
    private int vbo;
    private int ebo;
    private int program;
    private int whiteTex;
    private int width;
    private int height;
    private string compiledFragment = string.Empty;
    private readonly Dictionary<string, int> textureCache = new(StringComparer.OrdinalIgnoreCase);

    public IntPtr TextureId => (IntPtr)colorTex;
    public string? CompileError { get; private set; }

    public ShaderGraphNodePreviewRenderer()
    {
        fbo = GL.GenFramebuffer();
        colorTex = GL.GenTexture();
        depthRbo = GL.GenRenderbuffer();
        BuildQuad();
        whiteTex = CreateWhiteTexture();
        Resize(160, 112);
    }

    public void Resize(int w, int h)
    {
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        if (w == width && h == height) return;
        width = w;
        height = h;

        GL.BindTexture(TextureTarget.Texture2D, colorTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colorTex, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, depthRbo);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public bool Render(ShaderGraphModel model, string fragmentSrc, float time, int restoreViewportW, int restoreViewportH)
    {
        if (!EnsureProgram(fragmentSrc))
            return false;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.Viewport(0, 0, width, height);
        GL.Disable(EnableCap.DepthTest);
        GL.ClearColor(0.10f, 0.10f, 0.10f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        GL.UseProgram(program);
        SetFloat("u_Time", time);
        SetVec2("u_Resolution", width, height);
        SetVec3("u_CameraPos", 0f, 0f, 2.5f);
        SetVec3("u_LightDir", -0.4f, -0.8f, -0.5f);
        SetVec3("u_LightColor", 1f, 1f, 1f);
        SetFloat("u_LightIntensity", 1f);
        SetInt("u_ColorSpaceLinear", 1);
        SetFloat("u_CameraNear", 0.1f);
        SetFloat("u_CameraFar", 100f);
        BindSamplerUniforms(model, fragmentSrc);
        BindPropertyDefaults(model, fragmentSrc);

        GL.BindVertexArray(vao);
        GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, restoreViewportW, restoreViewportH);
        GL.Enable(EnableCap.DepthTest);
        return true;
    }

    private bool EnsureProgram(string fragmentSrc)
    {
        if (program != 0 && string.Equals(fragmentSrc, compiledFragment, StringComparison.Ordinal))
            return true;

        var vertexSrc = """
#version 330 core
layout(location = 0) in vec3 a_Position;
layout(location = 1) in vec3 a_Normal;
layout(location = 2) in vec2 a_UV;
out vec2 v_UV;
out vec3 v_NormalWS;
out vec3 v_WorldPos;
out vec3 v_ObjectPos;
out vec3 v_TangentWS;
out vec4 v_ScreenPos;
void main()
{
    v_UV = a_UV;
    v_NormalWS = a_Normal;
    v_WorldPos = a_Position;
    v_ObjectPos = a_Position;
    v_TangentWS = vec3(1.0, 0.0, 0.0);
    gl_Position = vec4(a_Position.xy, 0.0, 1.0);
    v_ScreenPos = gl_Position;
}
""";

        int vs = CompileShader(ShaderType.VertexShader, vertexSrc);
        int fs = CompileShader(ShaderType.FragmentShader, fragmentSrc);
        if (vs == 0 || fs == 0)
        {
            if (vs != 0) GL.DeleteShader(vs);
            if (fs != 0) GL.DeleteShader(fs);
            return false;
        }

        int newProgram = GL.CreateProgram();
        GL.AttachShader(newProgram, vs);
        GL.AttachShader(newProgram, fs);
        GL.LinkProgram(newProgram);
        GL.GetProgram(newProgram, GetProgramParameterName.LinkStatus, out int linkOk);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);

        if (linkOk == 0)
        {
            CompileError = GL.GetProgramInfoLog(newProgram);
            GL.DeleteProgram(newProgram);
            return false;
        }

        if (program != 0)
            GL.DeleteProgram(program);
        program = newProgram;
        compiledFragment = fragmentSrc;
        CompileError = null;
        return true;
    }

    private int CompileShader(ShaderType type, string src)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, src);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok != 0)
            return shader;

        CompileError = GL.GetShaderInfoLog(shader);
        GL.DeleteShader(shader);
        return 0;
    }

    private void BindSamplerUniforms(ShaderGraphModel model, string fragmentSrc)
    {
        var samplers = System.Text.RegularExpressions.Regex.Matches(fragmentSrc, @"uniform\s+sampler2D\s+(\w+)\s*;")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        var samplerDefaults = BuildSamplerDefaults(model);

        for (int i = 0; i < samplers.Count; i++)
        {
            int loc = GL.GetUniformLocation(program, samplers[i]);
            if (loc < 0) continue;

            string? path = samplerDefaults.TryGetValue(samplers[i], out var def) && !string.IsNullOrWhiteSpace(def)
                ? def
                : null;

            int tex = path != null ? GetTexture(path) : 0;
            if (tex == 0) tex = whiteTex;

            GL.ActiveTexture(TextureUnit.Texture0 + i);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.Uniform1(loc, i);
        }

        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private int GetTexture(string path)
    {
        string? fullPath = SceneViewportRenderer.NormalizeExistingAssetPath(path);
        if (fullPath == null) return 0;

        if (textureCache.TryGetValue(fullPath, out int cached))
            return cached;

        try
        {
            using var original = new System.Drawing.Bitmap(fullPath);
            using var bitmap = original.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb
                ? new System.Drawing.Bitmap(original)
                : original.Clone(new System.Drawing.Rectangle(0, 0, original.Width, original.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    bitmap.Width,
                    bitmap.Height,
                    0,
                    PixelFormat.Bgra,
                    PixelType.UnsignedByte,
                    data.Scan0);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            textureCache[fullPath] = texture;
            return texture;
        }
        catch
        {
            return 0;
        }
    }

    private static Dictionary<string, string> BuildSamplerDefaults(ShaderGraphModel model)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in model.Properties)
        {
            if (p.Type != PinType.Texture2D) continue;
            dict[SanitizeIdentifier(p.UniformName)] = p.TexturePath;
        }

        foreach (var n in model.Nodes)
        {
            if (n.Kind != NodeKind.PropertyTexture2D) continue;
            var uniformName = SanitizeIdentifier(PropertyUniformName(n));
            if (!dict.ContainsKey(uniformName))
                dict[uniformName] = n.TexturePath;
        }

        foreach (var n in model.Nodes)
        {
            if (n.Kind != NodeKind.TextureSample && n.Kind != NodeKind.NormalMap && n.Kind != NodeKind.Triplanar &&
                n.Kind != NodeKind.MetallicMap && n.Kind != NodeKind.SmoothnessMap && n.Kind != NodeKind.AmbientOcclusionMap) continue;
            var uniformName = SanitizeIdentifier(TextureUniformName(n));
            if (!dict.ContainsKey(uniformName))
                dict[uniformName] = n.TexturePath;
        }

        return dict;
    }

    private static string TextureUniformName(ShaderNode node)
    {
        var value = string.IsNullOrWhiteSpace(node.TextValue) ? "u_MainTex" : node.TextValue.Trim();
        var fileName = System.IO.Path.GetFileNameWithoutExtension(value);
        if (!string.IsNullOrWhiteSpace(fileName) && (value.Contains('\\') || value.Contains('/') || value.Contains('.')))
            return "u_" + fileName;
        return value;
    }

    private void BindPropertyDefaults(ShaderGraphModel model, string fragmentSrc)
    {
        var defaults = BuildPropertyDefaults(model);

        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(fragmentSrc, @"uniform\s+(float|vec2|vec3|vec4)\s+(\w+)\s*;"))
        {
            var type = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            if (name is "u_Time" or "u_Resolution" or "u_CameraPos" or "u_LightDir" or "u_LightColor" or "u_LightIntensity" or "u_CameraNear" or "u_CameraFar")
                continue;

            int loc = GL.GetUniformLocation(program, name);
            if (loc < 0) continue;

            var values = defaults.TryGetValue(name, out var found) ? found : DefaultFor(type);
            switch (type)
            {
                case "float":
                    GL.Uniform1(loc, values[0]);
                    break;
                case "vec2":
                    GL.Uniform2(loc, values[0], values[1]);
                    break;
                case "vec3":
                    if (values.Length >= 4)
                        GL.Uniform3(loc, values[0] * values[3], values[1] * values[3], values[2] * values[3]);
                    else
                        GL.Uniform3(loc, values[0], values[1], values[2]);
                    break;
                case "vec4":
                    if (values.Length >= 5)
                        GL.Uniform4(loc, values[0] * values[4], values[1] * values[4], values[2] * values[4], values[3]);
                    else
                        GL.Uniform4(loc, values[0], values[1], values[2], values[3]);
                    break;
            }
        }
    }

    private static System.Collections.Generic.Dictionary<string, float[]> BuildPropertyDefaults(ShaderGraphModel model)
    {
        var dict = new System.Collections.Generic.Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in model.Properties)
        {
            var name = SanitizeIdentifier(p.UniformName);
            switch (p.Type)
            {
                case PinType.Float:
                    dict[name] = new[] { ParseFloat(p.DefaultValue, 0f) };
                    break;
                case PinType.Vec2:
                    dict[name] = ParseVec(p.DefaultValue, 2);
                    break;
                case PinType.Vec3:
                    dict[name] = ColorToVec3(p.ColorHex, p.ColorIntensity);
                    break;
                case PinType.Vec4:
                    var c = ColorToVec3(p.ColorHex, p.ColorIntensity);
                    dict[name] = new[] { c[0], c[1], c[2], ColorAlpha(p.ColorHex) };
                    break;
            }
        }

        foreach (var n in model.Nodes)
        {
            var uniformName = SanitizeIdentifier(PropertyUniformName(n));
            if (dict.ContainsKey(uniformName)) continue;

            switch (n.Kind)
            {
                case NodeKind.PropertyFloat:
                    dict[uniformName] = new[] { n.FloatValue };
                    break;
                case NodeKind.PropertyColor:
                    dict[uniformName] = ColorToVec3(n.ColorHex, n.ColorIntensity);
                    break;
                case NodeKind.PropertyVector2:
                    dict[uniformName] = ParseVec(n.TextValue, 2);
                    break;
                case NodeKind.PropertyVector3:
                    dict[uniformName] = ParseVec(n.TextValue, 3);
                    break;
                case NodeKind.PropertyVector4:
                    dict[uniformName] = ParseVec(n.TextValue, 4);
                    break;
            }
        }

        return dict;
    }

    private static float[] DefaultFor(string type) => type switch
    {
        "float" => new[] { 0.5f },
        "vec2" => new[] { 0f, 0f },
        "vec3" => new[] { 1f, 1f, 1f },
        "vec4" => new[] { 1f, 1f, 1f, 1f },
        _ => new[] { 0f }
    };

    private static string PropertyUniformName(ShaderNode node)
    {
        var value = string.IsNullOrWhiteSpace(node.TextValue) ? node.Title : node.TextValue.Trim();
        return value.StartsWith("u_", StringComparison.OrdinalIgnoreCase) ? value : "u_" + value;
    }

    private static string SanitizeIdentifier(string value)
    {
        var chars = value.Select((c, i) => (char.IsLetter(c) || c == '_' || (i > 0 && char.IsDigit(c))) ? c : '_').ToArray();
        var result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? "u_MainTex" : result;
    }

    private static float ParseFloat(string? value, float fallback)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static float[] ParseVec(string? value, int count)
    {
        var parts = (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new float[count];
        for (int i = 0; i < count; i++)
            result[i] = i < parts.Length ? ParseFloat(parts[i], 0f) : 0f;
        return result;
    }

    private static float[] ColorToVec3(string colorHex, float intensity)
    {
        try
        {
            if (MediaColorConverter.ConvertFromString(colorHex) is MediaColor c)
                return new[] { c.R / 255f * intensity, c.G / 255f * intensity, c.B / 255f * intensity };
        }
        catch
        {
            // fall through
        }
        return new[] { 1f, 0f, 1f };
    }

    private static float ColorAlpha(string colorHex)
    {
        try
        {
            if (MediaColorConverter.ConvertFromString(colorHex) is MediaColor c)
                return c.A / 255f;
        }
        catch
        {
            // fall through
        }
        return 1f;
    }

    private void SetFloat(string name, float value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private void SetInt(string name, int value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private void SetVec2(string name, float x, float y)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform2(loc, x, y);
    }

    private void SetVec3(string name, float x, float y, float z)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform3(loc, x, y, z);
    }

    private void BuildQuad()
    {
        float[] vertices =
        {
            -1f, -1f, 0f, 0f, 0f, 1f, 0f, 0f,
             1f, -1f, 0f, 0f, 0f, 1f, 1f, 0f,
             1f,  1f, 0f, 0f, 0f, 1f, 1f, 1f,
            -1f,  1f, 0f, 0f, 0f, 1f, 0f, 1f
        };
        uint[] indices = { 0, 1, 2, 0, 2, 3 };

        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        const int stride = 8 * sizeof(float);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.BindVertexArray(0);
    }

    private static int CreateWhiteTexture()
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        var pixel = new byte[] { 255, 255, 255, 255 };
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        return tex;
    }

    public void Dispose()
    {
        if (program != 0) GL.DeleteProgram(program);
        if (vao != 0) GL.DeleteVertexArray(vao);
        if (vbo != 0) GL.DeleteBuffer(vbo);
        if (ebo != 0) GL.DeleteBuffer(ebo);
        if (colorTex != 0) GL.DeleteTexture(colorTex);
        if (whiteTex != 0) GL.DeleteTexture(whiteTex);
        foreach (var tex in textureCache.Values)
            if (tex != 0) GL.DeleteTexture(tex);
        if (depthRbo != 0) GL.DeleteRenderbuffer(depthRbo);
        if (fbo != 0) GL.DeleteFramebuffer(fbo);
    }
}
