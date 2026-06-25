using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using GrokoShaderGraphPro.Models;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace GrokoEngine.ImGuiEditor;

/// <summary>Offscreen sphere-preview renderer for the Shader Graph editor: compiles the generated GLSL and renders it on a lit sphere.</summary>
internal sealed class ShaderGraphPreview : IDisposable
{
    private int fbo;
    private int colorTex;
    private int depthRbo;
    private int width;
    private int height;
    private int program;
    private int vao;
    private int vbo;
    private int ebo;
    private int indexCount;
    private int whiteTex;
    private string previewShape = "Sphere";
    private readonly System.Collections.Generic.Dictionary<string, int> textureCache = new(StringComparer.OrdinalIgnoreCase);

    public IntPtr TextureId => (IntPtr)colorTex;
    public string? CompileError { get; private set; }

    public ShaderGraphPreview()
    {
        fbo = GL.GenFramebuffer();
        colorTex = GL.GenTexture();
        depthRbo = GL.GenRenderbuffer();
        Resize(384, 384);
        SetShape("Sphere");
        whiteTex = CreateWhiteTexture();
    }

    public void SetShape(string shape)
    {
        shape = string.IsNullOrWhiteSpace(shape) ? "Sphere" : shape.Trim();
        if (string.Equals(previewShape, shape, StringComparison.OrdinalIgnoreCase) && vao != 0 && indexCount > 0)
            return;

        previewShape = shape;
        switch (shape.ToLowerInvariant())
        {
            case "cube":
                BuildCube();
                break;
            case "quad":
            case "sprite":
                BuildQuad();
                break;
            case "cylinder":
                BuildCylinder();
                break;
            case "capsule":
                BuildCapsule();
                break;
            default:
                BuildSphere();
                break;
        }
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

    public bool SetShader(string vertexSrc, string fragmentSrc)
    {
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vertexSrc);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int vsOk);

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fragmentSrc);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out int fsOk);

        if (vsOk == 0 || fsOk == 0)
        {
            var vsLog = GL.GetShaderInfoLog(vs);
            var fsLog = GL.GetShaderInfoLog(fs);
            CompileError = (vsOk == 0 ? "Vertex shader:\n" + vsLog : "") + (fsOk == 0 ? "\nFragment shader:\n" + fsLog : "");
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
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
            CompileError = "Link error:\n" + GL.GetProgramInfoLog(newProgram);
            GL.DeleteProgram(newProgram);
            return false;
        }

        if (program != 0)
            GL.DeleteProgram(program);
        program = newProgram;
        CompileError = null;
        return true;
    }

    public void Render(ShaderGraphModel model, string fragmentSrc, float yaw, float pitch, float time, int restoreViewportW, int restoreViewportH)
        => Render(model, fragmentSrc, yaw, pitch, time, restoreViewportW, restoreViewportH, null, null);

    public void Render(ShaderGraphModel model, string fragmentSrc, float yaw, float pitch, float time, int restoreViewportW, int restoreViewportH,
        System.Collections.Generic.IReadOnlyDictionary<string, float[]>? propertyOverrides,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? samplerOverrides)
    {
        if (program == 0) return;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.Viewport(0, 0, width, height);
        GL.ClearColor(0.10f, 0.10f, 0.11f, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        GL.UseProgram(program);

        var model4 = Matrix4.CreateRotationY(yaw) * Matrix4.CreateRotationX(pitch);
        var eye = new Vector3(0f, 0f, 2.6f);
        var view = Matrix4.LookAt(eye, Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(40f), 1f, 0.1f, 100f);

        SetMatrix("u_Model", model4);
        SetMatrix("u_View", view);
        SetMatrix("u_Projection", proj);
        SetFloat("u_Time", time);
        SetVec2("u_Resolution", width, height);
        SetVec3("u_CameraPos", eye.X, eye.Y, eye.Z);
        var lightDir = Vector3.Normalize(new Vector3(-0.4f, -0.8f, -0.5f));
        SetVec3("u_LightDir", lightDir.X, lightDir.Y, lightDir.Z);
        SetVec3("u_LightColor", 1f, 1f, 1f);
        SetFloat("u_LightIntensity", 1f);
        SetInt("u_ColorSpaceLinear", 1);
        SetFloat("u_CameraNear", 0.1f);
        SetFloat("u_CameraFar", 100f);

        BindSamplerUniforms(model, fragmentSrc, samplerOverrides);
        BindPropertyDefaults(model, fragmentSrc, propertyOverrides);

        GL.BindVertexArray(vao);
        GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, restoreViewportW, restoreViewportH);
    }

    /// <summary>Lee el framebuffer offscreen como un Bitmap (con flip vertical), para generar miniaturas estáticas de assets.</summary>
    public System.Drawing.Bitmap CaptureBitmap()
    {
        var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        bitmap.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipY);
        return bitmap;
    }

    // ── Uniforms ─────────────────────────────────────────────────

    private void BindSamplerUniforms(ShaderGraphModel model, string fragmentSrc, System.Collections.Generic.IReadOnlyDictionary<string, string>? overrides = null)
    {
        var samplers = Regex.Matches(fragmentSrc, @"uniform\s+sampler2D\s+(\w+)\s*;")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        var samplerDefaults = BuildSamplerDefaults(model);

        for (int i = 0; i < samplers.Count; i++)
        {
            int loc = GL.GetUniformLocation(program, samplers[i]);
            if (loc < 0) continue;

            string? path = overrides != null && overrides.TryGetValue(samplers[i], out var ov) && !string.IsNullOrWhiteSpace(ov) ? ov
                : samplerDefaults.TryGetValue(samplers[i], out var def) && !string.IsNullOrWhiteSpace(def) ? def
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
                    OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
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

    private static System.Collections.Generic.Dictionary<string, string> BuildSamplerDefaults(ShaderGraphModel model)
    {
        var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

    private void BindPropertyDefaults(ShaderGraphModel model, string fragmentSrc, System.Collections.Generic.IReadOnlyDictionary<string, float[]>? overrides = null)
    {
        var defaults = BuildPropertyDefaults(model);

        foreach (Match m in Regex.Matches(fragmentSrc, @"uniform\s+(float|vec2|vec3|vec4)\s+(\w+)\s*;"))
        {
            var type = m.Groups[1].Value;
            var name = m.Groups[2].Value;
            if (name is "u_Time" or "u_Resolution" or "u_CameraPos" or "u_LightDir" or "u_LightColor" or "u_LightIntensity" or "u_CameraNear" or "u_CameraFar") continue;

            int loc = GL.GetUniformLocation(program, name);
            if (loc < 0) continue;

            var values = overrides != null && overrides.TryGetValue(name, out var ov) ? ov
                : defaults.TryGetValue(name, out var v) ? v
                : DefaultFor(type);
            switch (type)
            {
                case "float": GL.Uniform1(loc, values[0]); break;
                case "vec2": GL.Uniform2(loc, values[0], values[1]); break;
                case "vec3":
                    // vec3 HDR: [r,g,b,intensity] (4 elementos) -> rgb * intensidad.
                    if (values.Length >= 4)
                        GL.Uniform3(loc, values[0] * values[3], values[1] * values[3], values[2] * values[3]);
                    else
                        GL.Uniform3(loc, values[0], values[1], values[2]);
                    break;
                case "vec4":
                    // vec4 HDR: [r,g,b,a,intensity] (5 elementos) -> rgb * intensidad, alpha sin tocar.
                    if (values.Length >= 5)
                        GL.Uniform4(loc, values[0] * values[4], values[1] * values[4], values[2] * values[4], values[3]);
                    else
                        GL.Uniform4(loc, values[0], values[1], values[2], values[3]);
                    break;
            }
        }
    }

    private static float[] DefaultFor(string type) => type switch
    {
        "float" => new[] { 0.5f },
        "vec2" => new[] { 0f, 0f },
        "vec3" => new[] { 1f, 1f, 1f },
        "vec4" => new[] { 1f, 1f, 1f, 1f },
        _ => new[] { 0f }
    };

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
            // Si el nodo Property* referencia una propiedad del Blackboard, esta ya
            // dejó su valor por defecto en el dict y tiene prioridad: no sobrescribir
            // con el valor (potencialmente desactualizado) guardado en el nodo.
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

    private static float ParseFloat(string? s, float fallback)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static float[] ParseVec(string? s, int n)
    {
        var parts = (s ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new float[n];
        for (int i = 0; i < n; i++)
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

    private void SetMatrix(string name, Matrix4 m)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.UniformMatrix4(loc, false, ref m);
    }

    private void SetFloat(string name, float v)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform1(loc, v);
    }

    private void SetInt(string name, int v)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc >= 0) GL.Uniform1(loc, v);
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

    // ── Geometry ─────────────────────────────────────────────────

    private void UploadGeometry(System.Collections.Generic.List<float> vertices, System.Collections.Generic.List<uint> indices)
    {
        if (vao != 0) GL.DeleteVertexArray(vao);
        if (vbo != 0) GL.DeleteBuffer(vbo);
        if (ebo != 0) GL.DeleteBuffer(ebo);

        indexCount = indices.Count;

        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);

        const int stride = 8 * sizeof(float);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));

        GL.BindVertexArray(0);
    }

    private void BuildSphere()
    {
        const int stacks = 28;
        const int slices = 40;
        var vertices = new System.Collections.Generic.List<float>();
        var indices = new System.Collections.Generic.List<uint>();

        for (int i = 0; i <= stacks; i++)
        {
            float v = (float)i / stacks;
            float phi = v * MathF.PI;
            for (int j = 0; j <= slices; j++)
            {
                float u = (float)j / slices;
                float theta = u * MathF.PI * 2f;

                float x = MathF.Sin(phi) * MathF.Cos(theta);
                float y = MathF.Cos(phi);
                float z = MathF.Sin(phi) * MathF.Sin(theta);

                vertices.AddRange(new[] { x * 0.8f, y * 0.8f, z * 0.8f, x, y, z, u, v });
            }
        }

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(i * (slices + 1) + j);
                uint b = (uint)(a + slices + 1);
                indices.Add(a); indices.Add(b); indices.Add(a + 1);
                indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
            }
        }

        UploadGeometry(vertices, indices);
    }

    private void BuildQuad()
    {
        var vertices = new System.Collections.Generic.List<float>
        {
            -0.8f, -0.8f, 0f, 0f, 0f, 1f, 0f, 0f,
             0.8f, -0.8f, 0f, 0f, 0f, 1f, 1f, 0f,
             0.8f,  0.8f, 0f, 0f, 0f, 1f, 1f, 1f,
            -0.8f,  0.8f, 0f, 0f, 0f, 1f, 0f, 1f
        };
        var indices = new System.Collections.Generic.List<uint> { 0, 1, 2, 0, 2, 3 };
        UploadGeometry(vertices, indices);
    }

    private void BuildCube()
    {
        var vertices = new System.Collections.Generic.List<float>();
        var indices = new System.Collections.Generic.List<uint>();

        void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            uint start = (uint)(vertices.Count / 8);
            AddVertex(vertices, a, normal, 0f, 0f);
            AddVertex(vertices, b, normal, 1f, 0f);
            AddVertex(vertices, c, normal, 1f, 1f);
            AddVertex(vertices, d, normal, 0f, 1f);
            indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        const float s = 0.72f;
        Face(Vector3.UnitZ, new(-s, -s, s), new(s, -s, s), new(s, s, s), new(-s, s, s));
        Face(-Vector3.UnitZ, new(s, -s, -s), new(-s, -s, -s), new(-s, s, -s), new(s, s, -s));
        Face(Vector3.UnitX, new(s, -s, s), new(s, -s, -s), new(s, s, -s), new(s, s, s));
        Face(-Vector3.UnitX, new(-s, -s, -s), new(-s, -s, s), new(-s, s, s), new(-s, s, -s));
        Face(Vector3.UnitY, new(-s, s, s), new(s, s, s), new(s, s, -s), new(-s, s, -s));
        Face(-Vector3.UnitY, new(-s, -s, -s), new(s, -s, -s), new(s, -s, s), new(-s, -s, s));
        UploadGeometry(vertices, indices);
    }

    private void BuildCylinder()
    {
        BuildLathedShape(32, 1, v =>
        {
            float y = MathHelper.Lerp(-0.75f, 0.75f, v);
            return (0.55f, y, 0.55f, 0f);
        }, capEnds: true);
    }

    private void BuildCapsule()
    {
        const float radius = 0.46f;
        const float half = 0.36f;
        BuildLathedShape(36, 24, v =>
        {
            float t = v * 2f - 1f;
            if (t < -0.001f)
            {
                float a = t * MathF.PI * 0.5f;
                return (MathF.Cos(a) * radius, -half + MathF.Sin(a) * radius, radius, 0f);
            }
            if (t > 0.001f)
            {
                float a = t * MathF.PI * 0.5f;
                return (MathF.Cos(a) * radius, half + MathF.Sin(a) * radius, radius, 0f);
            }
            return (radius, 0f, radius, 0f);
        }, capEnds: false);
    }

    private void BuildLathedShape(int slices, int rings, Func<float, (float Radius, float Y, float NormalScale, float NormalY)> profile, bool capEnds)
    {
        var vertices = new System.Collections.Generic.List<float>();
        var indices = new System.Collections.Generic.List<uint>();

        for (int i = 0; i <= rings; i++)
        {
            float v = (float)i / rings;
            var p = profile(v);
            for (int j = 0; j <= slices; j++)
            {
                float u = (float)j / slices;
                float theta = u * MathF.PI * 2f;
                float x = MathF.Cos(theta);
                float z = MathF.Sin(theta);
                var normal = Vector3.Normalize(new Vector3(x * p.NormalScale, p.NormalY, z * p.NormalScale));
                AddVertex(vertices, new Vector3(x * p.Radius, p.Y, z * p.Radius), normal, u, v);
            }
        }

        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(i * (slices + 1) + j);
                uint b = (uint)(a + slices + 1);
                indices.Add(a); indices.Add(b); indices.Add(a + 1);
                indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
            }
        }

        if (capEnds)
        {
            AddCap(vertices, indices, slices, -0.75f, -Vector3.UnitY);
            AddCap(vertices, indices, slices, 0.75f, Vector3.UnitY);
        }

        UploadGeometry(vertices, indices);
    }

    private static void AddCap(System.Collections.Generic.List<float> vertices, System.Collections.Generic.List<uint> indices, int slices, float y, Vector3 normal)
    {
        uint center = (uint)(vertices.Count / 8);
        AddVertex(vertices, new Vector3(0f, y, 0f), normal, 0.5f, 0.5f);
        const float radius = 0.55f;
        for (int j = 0; j <= slices; j++)
        {
            float u = (float)j / slices;
            float theta = u * MathF.PI * 2f;
            AddVertex(vertices, new Vector3(MathF.Cos(theta) * radius, y, MathF.Sin(theta) * radius), normal, MathF.Cos(theta) * 0.5f + 0.5f, MathF.Sin(theta) * 0.5f + 0.5f);
        }
        for (int j = 0; j < slices; j++)
        {
            if (normal.Y > 0f)
                indices.AddRange(new[] { center, center + (uint)j + 1, center + (uint)j + 2 });
            else
                indices.AddRange(new[] { center, center + (uint)j + 2, center + (uint)j + 1 });
        }
    }

    private static void AddVertex(System.Collections.Generic.List<float> vertices, Vector3 pos, Vector3 normal, float u, float v)
    {
        vertices.Add(pos.X); vertices.Add(pos.Y); vertices.Add(pos.Z);
        vertices.Add(normal.X); vertices.Add(normal.Y); vertices.Add(normal.Z);
        vertices.Add(u); vertices.Add(v);
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
        GL.DeleteVertexArray(vao);
        GL.DeleteBuffer(vbo);
        GL.DeleteBuffer(ebo);
        GL.DeleteTexture(colorTex);
        GL.DeleteTexture(whiteTex);
        foreach (var tex in textureCache.Values) GL.DeleteTexture(tex);
        GL.DeleteRenderbuffer(depthRbo);
        GL.DeleteFramebuffer(fbo);
    }
}
