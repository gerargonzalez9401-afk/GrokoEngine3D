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
private void EnsureEnvironment()
    {
        _envDirty = false;

        if (_envTexture != 0) { GL.DeleteTexture(_envTexture); _envTexture = 0; }
        if (_envPrefilterCube != 0) { GL.DeleteTexture(_envPrefilterCube); _envPrefilterCube = 0; }
        _envLoaded = false;
        _envMaxLod = 0f;
        _envPrefilterMaxLod = 0f;
        _envDiffuseColor = new Vector3(0.3f, 0.32f, 0.36f);
        Array.Clear(_envSh, 0, _envSh.Length);

        if (string.IsNullOrWhiteSpace(_envPath) || !HdrLoader.IsHdr(_envPath))
            return;

        var img = HdrLoader.Load(_envPath);
        if (img == null || img.Width <= 0 || img.Height <= 0)
        {
            GrokoEngine.Debug.LogWarning($"No se pudo cargar el HDRI '{_envPath}': {HdrLoader.LastError}");
            return;
        }

        NormalizeEnvironmentForIbl(img);

        _envTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _envTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f,
            img.Width, img.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rgb, PixelType.Float, img.Pixels);
        // Longitud (X) repite; latitud (Y) se recorta para no sangrar en los polos.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        _envMaxLod = MathF.Floor(MathF.Log2(Math.Max(img.Width, img.Height)));
        _envDiffuseColor = ComputeEnvironmentDiffuseColor(img);
        ComputeEnvironmentSphericalHarmonics(img, _envSh);
        CreateEnvironmentPrefilterCube(img);
        _envLoaded = true;
        GrokoEngine.Debug.Log($"HDRI cargado: {System.IO.Path.GetFileName(_envPath)} ({img.Width}x{img.Height})");
    }

    private static void NormalizeEnvironmentForIbl(HdrImage img)
    {
        if (img.Pixels.Length < img.Width * img.Height * 3)
            return;

        double luminanceSum = 0.0;
        double weightSum = 0.0;
        for (int y = 0; y < img.Height; y++)
        {
            float theta = MathF.PI * ((y + 0.5f) / Math.Max(1f, img.Height));
            double rowWeight = Math.Max(0.0001, Math.Sin(theta));
            int row = y * img.Width * 3;

            for (int x = 0; x < img.Width; x++)
            {
                int i = row + x * 3;
                float r = SanitizeHdrPixel(img.Pixels[i]);
                float g = SanitizeHdrPixel(img.Pixels[i + 1]);
                float b = SanitizeHdrPixel(img.Pixels[i + 2]);
                double lum = Math.Max(0.0, r * 0.2126 + g * 0.7152 + b * 0.0722);
                luminanceSum += lum * rowWeight;
                weightSum += rowWeight;
            }
        }

        float averageLuminance = weightSum > double.Epsilon ? (float)(luminanceSum / weightSum) : 1f;
        float scale = Math.Clamp(0.55f / MathF.Max(averageLuminance, 0.001f), 0.02f, 1.25f);
        const float maxIblRadiance = 3.5f;

        for (int i = 0; i < img.Pixels.Length; i++)
            img.Pixels[i] = MathF.Min(SanitizeHdrPixel(img.Pixels[i]) * scale, maxIblRadiance);
    }

    private void CreateEnvironmentPrefilterCube(HdrImage img)
    {
        const int baseSize = 96;
        const int mipCount = 6;

        _envPrefilterCube = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, _envPrefilterCube);

        for (int mip = 0; mip < mipCount; mip++)
        {
            int size = Math.Max(1, baseSize >> mip);
            float roughness = mip / (float)(mipCount - 1);
            int sampleCount = mip == 0 ? 1 : 24 + mip * 8;

            for (int face = 0; face < 6; face++)
            {
                float[] pixels = BuildPrefilterFace(img, face, size, roughness, sampleCount);
                GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, mip, PixelInternalFormat.Rgb16f,
                    size, size, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rgb, PixelType.Float, pixels);
            }
        }

        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, mipCount - 1);
        GL.BindTexture(TextureTarget.TextureCubeMap, 0);

        _envPrefilterMaxLod = mipCount - 1;
    }

    private static float[] BuildPrefilterFace(HdrImage img, int face, int size, float roughness, int sampleCount)
    {
        float[] pixels = new float[size * size * 3];
        float exponent = MathHelper.Lerp(384f, 1.5f, roughness * roughness);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = ((x + 0.5f) / size) * 2f - 1f;
                float v = ((y + 0.5f) / size) * 2f - 1f;
                Vector3 dir = CubeFaceDirection(face, u, v).Normalized();
                Vector3 color = roughness <= 0.001f
                    ? SampleHdrEquirect(img, dir)
                    : PrefilterDirection(img, dir, exponent, sampleCount);

                int i = (y * size + x) * 3;
                pixels[i] = color.X;
                pixels[i + 1] = color.Y;
                pixels[i + 2] = color.Z;
            }
        }

        return pixels;
    }

    private static Vector3 PrefilterDirection(HdrImage img, Vector3 dir, float exponent, int sampleCount)
    {
        Vector3 up = MathF.Abs(dir.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tangent = Vector3.Cross(up, dir).Normalized();
        Vector3 bitangent = Vector3.Cross(dir, tangent).Normalized();
        Vector3 sum = Vector3.Zero;
        float weightSum = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float u = (i + 0.5f) / sampleCount;
            float v = RadicalInverseVdc((uint)i);
            float cosTheta = MathF.Pow(u, 1f / (exponent + 1f));
            float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
            float phi = MathF.Tau * v;

            Vector3 sampleDir = (tangent * (MathF.Cos(phi) * sinTheta) +
                                 bitangent * (MathF.Sin(phi) * sinTheta) +
                                 dir * cosTheta).Normalized();
            float weight = MathF.Max(0.0001f, cosTheta);
            sum += SampleHdrEquirect(img, sampleDir) * weight;
            weightSum += weight;
        }

        return weightSum > 0f ? sum / weightSum : SampleHdrEquirect(img, dir);
    }

    private static Vector3 CubeFaceDirection(int face, float u, float v) => face switch
    {
        0 => new Vector3(1f, -v, -u),
        1 => new Vector3(-1f, -v, u),
        2 => new Vector3(u, 1f, v),
        3 => new Vector3(u, -1f, -v),
        4 => new Vector3(u, -v, 1f),
        _ => new Vector3(-u, -v, -1f)
    };

    private static Vector3 SampleHdrEquirect(HdrImage img, Vector3 dir)
    {
        const float invPi = 1f / MathF.PI;
        const float invTau = 1f / MathF.Tau;
        float uf = MathF.Atan2(dir.Z, dir.X) * invTau + 0.5f;
        float vf = MathF.Acos(Math.Clamp(dir.Y, -1f, 1f)) * invPi;

        uf -= MathF.Floor(uf);
        vf = Math.Clamp(vf, 0f, 1f);

        float x = uf * (img.Width - 1);
        float y = vf * (img.Height - 1);
        int x0 = Math.Clamp((int)MathF.Floor(x), 0, img.Width - 1);
        int y0 = Math.Clamp((int)MathF.Floor(y), 0, img.Height - 1);
        int x1 = (x0 + 1) % img.Width;
        int y1 = Math.Clamp(y0 + 1, 0, img.Height - 1);
        float tx = x - x0;
        float ty = y - y0;

        Vector3 c00 = HdrPixel(img, x0, y0);
        Vector3 c10 = HdrPixel(img, x1, y0);
        Vector3 c01 = HdrPixel(img, x0, y1);
        Vector3 c11 = HdrPixel(img, x1, y1);
        return Vector3.Lerp(Vector3.Lerp(c00, c10, tx), Vector3.Lerp(c01, c11, tx), ty);
    }

    private static Vector3 HdrPixel(HdrImage img, int x, int y)
    {
        int i = (y * img.Width + x) * 3;
        return new Vector3(SanitizeHdrPixel(img.Pixels[i]), SanitizeHdrPixel(img.Pixels[i + 1]), SanitizeHdrPixel(img.Pixels[i + 2]));
    }

    private static float SanitizeHdrPixel(float value) =>
        float.IsFinite(value) ? MathF.Max(0f, value) : 0f;

    private static float RadicalInverseVdc(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10f;
    }

    private static int CreateBrdfLutTexture()
    {
        const int size = 128;
        const int samples = 256;
        float[] pixels = new float[size * size * 2];

        for (int y = 0; y < size; y++)
        {
            float roughness = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float noV = (x + 0.5f) / size;
                Vector2 integrated = IntegrateBrdf(noV, roughness, samples);
                int i = (y * size + x) * 2;
                pixels[i] = integrated.X;
                pixels[i + 1] = integrated.Y;
            }
        }

        int texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rg16f,
            size, size, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rg, PixelType.Float, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }

    private static Vector2 IntegrateBrdf(float noV, float roughness, int sampleCount)
    {
        Vector3 v = new(MathF.Sqrt(MathF.Max(0f, 1f - noV * noV)), 0f, noV);
        float a = 0f;
        float b = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            Vector2 xi = new((i + 0.5f) / sampleCount, RadicalInverseVdc((uint)i));
            Vector3 h = ImportanceSampleGgx(xi, roughness);
            Vector3 l = (2f * Vector3.Dot(v, h) * h - v).Normalized();
            float noL = MathF.Max(l.Z, 0f);
            float noH = MathF.Max(h.Z, 0f);
            float voH = MathF.Max(Vector3.Dot(v, h), 0f);

            if (noL <= 0f)
                continue;

            float g = GeometrySmithIbl(noV, noL, roughness);
            float gVis = (g * voH) / MathF.Max(noH * noV, 0.0001f);
            float fc = MathF.Pow(1f - voH, 5f);
            a += (1f - fc) * gVis;
            b += fc * gVis;
        }

        return new Vector2(a / sampleCount, b / sampleCount);
    }

    private static Vector3 ImportanceSampleGgx(Vector2 xi, float roughness)
    {
        float a = roughness * roughness;
        float phi = MathF.Tau * xi.X;
        float cosTheta = MathF.Sqrt((1f - xi.Y) / MathF.Max(1f + (a * a - 1f) * xi.Y, 0.0001f));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        return new Vector3(MathF.Cos(phi) * sinTheta, MathF.Sin(phi) * sinTheta, cosTheta);
    }

    private static float GeometrySmithIbl(float noV, float noL, float roughness)
    {
        float a = roughness * roughness;
        float gv = noL * MathF.Sqrt(MathF.Max(noV * noV * (1f - a) + a, 0f));
        float gl = noV * MathF.Sqrt(MathF.Max(noL * noL * (1f - a) + a, 0f));
        return 0.5f / MathF.Max(gv + gl, 0.0001f);
    }

    private static Vector3 ComputeEnvironmentDiffuseColor(HdrImage img)
    {
        if (img.Pixels.Length < img.Width * img.Height * 3)
            return new Vector3(0.3f, 0.32f, 0.36f);

        double r = 0.0;
        double g = 0.0;
        double b = 0.0;
        double weightSum = 0.0;

        for (int y = 0; y < img.Height; y++)
        {
            float theta = MathF.PI * ((y + 0.5f) / Math.Max(1f, img.Height));
            double rowWeight = Math.Max(0.0, Math.Sin(theta));
            int row = y * img.Width * 3;

            for (int x = 0; x < img.Width; x++)
            {
                int i = row + x * 3;
                float pr = img.Pixels[i];
                float pg = img.Pixels[i + 1];
                float pb = img.Pixels[i + 2];
                if (!float.IsFinite(pr) || !float.IsFinite(pg) || !float.IsFinite(pb))
                    continue;

                r += pr * rowWeight;
                g += pg * rowWeight;
                b += pb * rowWeight;
                weightSum += rowWeight;
            }
        }

        if (weightSum <= double.Epsilon)
            return new Vector3(0.3f, 0.32f, 0.36f);

        return new Vector3(
            MathF.Max(0f, (float)(r / weightSum)),
            MathF.Max(0f, (float)(g / weightSum)),
            MathF.Max(0f, (float)(b / weightSum)));
    }

    private static void ComputeEnvironmentSphericalHarmonics(HdrImage img, Vector3[] sh)
    {
        Array.Clear(sh, 0, sh.Length);

        if (sh.Length < 9 || img.Pixels.Length < img.Width * img.Height * 3)
            return;

        float dTheta = MathF.PI / Math.Max(1, img.Height);
        float dPhi = MathF.Tau / Math.Max(1, img.Width);
        Span<float> basis = stackalloc float[9];

        for (int y = 0; y < img.Height; y++)
        {
            float theta = MathF.PI * ((y + 0.5f) / Math.Max(1f, img.Height));
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);
            float solidAngle = sinTheta * dTheta * dPhi;
            int row = y * img.Width * 3;

            for (int x = 0; x < img.Width; x++)
            {
                int i = row + x * 3;
                var color = new Vector3(
                    SanitizeHdr(img.Pixels[i]),
                    SanitizeHdr(img.Pixels[i + 1]),
                    SanitizeHdr(img.Pixels[i + 2]));

                float phi = (((x + 0.5f) / Math.Max(1f, img.Width)) - 0.5f) * MathF.Tau;
                float sx = MathF.Cos(phi) * sinTheta;
                float sy = cosTheta;
                float sz = MathF.Sin(phi) * sinTheta;

                EvaluateShBasis(sx, sy, sz, basis);

                for (int c = 0; c < 9; c++)
                    sh[c] += color * (basis[c] * solidAngle);
            }
        }

        static float SanitizeHdr(float value) =>
            float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    private static void EvaluateShBasis(float x, float y, float z, Span<float> basis)
    {
        basis[0] = 0.282095f;
        basis[1] = -0.488603f * y;
        basis[2] = 0.488603f * z;
        basis[3] = -0.488603f * x;
        basis[4] = 1.092548f * x * y;
        basis[5] = -1.092548f * y * z;
        basis[6] = 0.315392f * (3f * z * z - 1f);
        basis[7] = -1.092548f * x * z;
        basis[8] = 0.546274f * (x * x - y * y);
    }

private void DrawSkybox(Vector3 camFront, Vector3 camUp, float fovDeg, float aspect, DirectionalLight? sun = null)
    {
        GL.DepthMask(false);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        GL.UseProgram(_skyboxShader);
        GL.ActiveTexture(TextureUnit.Texture7);
        GL.BindTexture(TextureTarget.Texture2D, _envTexture);
        GL.Uniform1(_skyEnvLoc, 7);
        GL.Uniform3(_skyCamFrontLoc, camFront);
        GL.Uniform3(_skyCamUpLoc, camUp);
        GL.Uniform1(_skyTanHalfFovLoc, MathF.Tan(MathHelper.DegreesToRadians(fovDeg) * 0.5f));
        GL.Uniform1(_skyAspectLoc, aspect);

        // Disco solar + halo: el sol en el cielo esta en la direccion OPUESTA a la que viaja la luz.
        bool sunDisk = sun != null && sun.ShowSunDisk && sun.Intensity > 0.0001f;
        GL.Uniform1(_skySunEnabledLoc, sunDisk ? 1 : 0);
        if (sunDisk)
        {
            var skyPos = ToTk(sun!.GetNormalizedDirection()).Normalized() * -1f;   // posicion del sol
            var col = ToTk(sun.GetEffectiveColor()) * sun.Intensity;
            float radius = MathHelper.DegreesToRadians(sun.AngularDiameter * 0.5f);
            GL.Uniform3(_skySunDirLoc, skyPos);
            GL.Uniform3(_skySunColorLoc, col);
            GL.Uniform1(_skySunAngularLoc, radius);
        }

        GL.BindVertexArray(_skyboxVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.UseProgram(0);

        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
    }

private static int CreateSkyboxShader()
    {
        const string vert = """
            #version 330 core
            out vec2 vNdc;
            void main()
            {
                vec2 p = vec2((gl_VertexID == 1) ? 3.0 : -1.0,
                              (gl_VertexID == 2) ? 3.0 : -1.0);
                vNdc = p;
                gl_Position = vec4(p, 0.0, 1.0);
            }
            """;
        const string frag = """
            #version 330 core
            in vec2 vNdc;
            uniform sampler2D uEnvMap;
            uniform vec3  uCamFront;
            uniform vec3  uCamUp;
            uniform float uTanHalfFov;
            uniform float uAspect;
            uniform vec3  uSunDir;
            uniform vec3  uSunColor;
            uniform float uSunAngular;
            uniform int   uSunEnabled;
            out vec4 outColor;
            vec2 DirToEquirectUV(vec3 d)
            {
                const float PI = 3.14159265359;
                float u = atan(d.z, d.x) / (2.0 * PI) + 0.5;
                float v = acos(clamp(d.y, -1.0, 1.0)) / PI;
                return vec2(u, v);
            }
            void main()
            {
                vec3 fwd = normalize(uCamFront);
                vec3 right = normalize(cross(fwd, uCamUp));
                vec3 up = cross(right, fwd);
                vec3 dir = normalize(fwd
                    + right * (vNdc.x * uTanHalfFov * uAspect)
                    + up    * (vNdc.y * uTanHalfFov));
                // textureLod(...,0) en vez de texture(): evita la costura vertical
                // del equirectangular (en el wrap de longitud las derivadas de UV
                // se disparan y el auto-LOD coge un mip basura). El fondo va a mip 0.
                vec3 col = textureLod(uEnvMap, DirToEquirectUV(dir), 0.0).rgb;
                if (uSunEnabled == 1)
                {
                    float ang = acos(clamp(dot(dir, normalize(uSunDir)), -1.0, 1.0));
                    // Disco con borde suave (limb darkening simple) + halo/glow exterior.
                    float disk = 1.0 - smoothstep(uSunAngular * 0.75, uSunAngular, ang);
                    float halo = pow(max(0.0, 1.0 - ang / (uSunAngular * 16.0)), 3.0);
                    col += uSunColor * (disk * 8.0 + halo * 0.5);
                }
                outColor = vec4(col, 1.0);
            }
            """;
        int v = CompileShader(ShaderType.VertexShader, vert);
        int f = CompileShader(ShaderType.FragmentShader, frag);
        int program = GL.CreateProgram();
        GL.AttachShader(program, v);
        GL.AttachShader(program, f);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
        GL.DetachShader(program, v); GL.DetachShader(program, f);
        GL.DeleteShader(v); GL.DeleteShader(f);
        return program;
    }
}
