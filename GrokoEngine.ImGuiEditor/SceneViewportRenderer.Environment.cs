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
        _envLoaded = false;
        _envMaxLod = 0f;

        if (string.IsNullOrWhiteSpace(_envPath) || !HdrLoader.IsHdr(_envPath))
            return;

        var img = HdrLoader.Load(_envPath);
        if (img == null || img.Width <= 0 || img.Height <= 0)
        {
            GrokoEngine.Debug.LogWarning($"No se pudo cargar el HDRI '{_envPath}': {HdrLoader.LastError}");
            return;
        }

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
        _envLoaded = true;
        GrokoEngine.Debug.Log($"HDRI cargado: {System.IO.Path.GetFileName(_envPath)} ({img.Width}x{img.Height})");
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
