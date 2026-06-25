using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vector2 = System.Numerics.Vector2;
using NumVector4 = System.Numerics.Vector4;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using GlfwKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace GrokoEngine.ImGuiEditor;

internal sealed class ImGuiController : IDisposable
{
    private readonly IntPtr context;
    private int vertexArray;
    private int vertexBuffer;
    private int indexBuffer;
    private int fontTexture;
    private int shader;
    private int attribLocationTex;
    private int attribLocationProjMtx;
    private int attribLocationVtxPos;
    private int attribLocationVtxUv;
    private int attribLocationVtxColor;
    private int vertexBufferSize = 10000;
    private int indexBufferSize = 2000;
    private int windowWidth;
    private int windowHeight;

    public ImGuiDrawStats LastDrawStats { get; private set; } = ImGuiDrawStats.Empty;

    public ImGuiController(int width, int height)
    {
        windowWidth = width;
        windowHeight = height;
        context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        string segoe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (File.Exists(segoe))
            io.Fonts.AddFontFromFileTTF(segoe, 16f);
        else
            io.Fonts.AddFontDefault();
        ApplyUnityStyle();

        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);
    }

    private static void ApplyUnityStyle()
    {
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        // ── Espaciado / forma (GrokoEngine Premium Dark, estilo editor moderno) ──
        style.WindowPadding     = new Vector2(5f, 5f);
        style.FramePadding      = new Vector2(6f, 3f);
        style.CellPadding       = new Vector2(5f, 3f);
        style.ItemSpacing       = new Vector2(7f, 6f);
        style.ItemInnerSpacing  = new Vector2(4f, 3f);
        style.IndentSpacing     = 14f;
        style.ScrollbarSize     = 11f;
        style.GrabMinSize       = 8f;
        style.WindowBorderSize  = 1f;
        style.ChildBorderSize   = 1f;
        style.PopupBorderSize   = 1f;
        style.FrameBorderSize   = 0f;
        style.TabBorderSize     = 0f;
        style.WindowRounding    = 0f;
        style.ChildRounding     = 0f;
        style.FrameRounding     = 3f;
        style.PopupRounding     = 3f;
        style.ScrollbarRounding = 3f;
        style.GrabRounding      = 3f;
        style.TabRounding       = 2f;

        // ── Paleta (acento azul Unity ≈ #4A90D9) ──
        var blue       = new NumVector4(0.18f, 0.40f, 0.64f, 1f);
        var blueBright = new NumVector4(0.28f, 0.58f, 0.86f, 1f);
        var c = style.Colors;
        c[(int)ImGuiCol.Text]                 = new NumVector4(0.78f, 0.80f, 0.83f, 1f);
        c[(int)ImGuiCol.TextDisabled]         = new NumVector4(0.50f, 0.54f, 0.58f, 1f);
        c[(int)ImGuiCol.WindowBg]             = new NumVector4(0.070f, 0.078f, 0.088f, 1f);
        c[(int)ImGuiCol.ChildBg]              = new NumVector4(0.095f, 0.105f, 0.118f, 1f);
        c[(int)ImGuiCol.PopupBg]              = new NumVector4(0.080f, 0.088f, 0.100f, 0.98f);
        c[(int)ImGuiCol.Border]               = new NumVector4(0.135f, 0.150f, 0.168f, 1f);
        c[(int)ImGuiCol.BorderShadow]         = new NumVector4(0f, 0f, 0f, 0f);
        c[(int)ImGuiCol.FrameBg]              = new NumVector4(0.060f, 0.066f, 0.075f, 1f);
        c[(int)ImGuiCol.FrameBgHovered]       = new NumVector4(0.105f, 0.122f, 0.140f, 1f);
        c[(int)ImGuiCol.FrameBgActive]        = new NumVector4(0.130f, 0.200f, 0.280f, 1f);
        c[(int)ImGuiCol.TitleBg]              = new NumVector4(0.055f, 0.061f, 0.070f, 1f);
        c[(int)ImGuiCol.TitleBgActive]        = new NumVector4(0.075f, 0.084f, 0.096f, 1f);
        c[(int)ImGuiCol.TitleBgCollapsed]     = new NumVector4(0.10f, 0.10f, 0.10f, 1f);
        c[(int)ImGuiCol.MenuBarBg]            = new NumVector4(0.040f, 0.046f, 0.054f, 1f);
        c[(int)ImGuiCol.ScrollbarBg]          = new NumVector4(0.070f, 0.078f, 0.088f, 1f);
        c[(int)ImGuiCol.ScrollbarGrab]        = new NumVector4(0.230f, 0.255f, 0.285f, 1f);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = new NumVector4(0.42f, 0.42f, 0.42f, 1f);
        c[(int)ImGuiCol.ScrollbarGrabActive]  = new NumVector4(0.52f, 0.52f, 0.52f, 1f);
        c[(int)ImGuiCol.CheckMark]            = blueBright;
        c[(int)ImGuiCol.SliderGrab]           = blueBright;
        c[(int)ImGuiCol.SliderGrabActive]     = new NumVector4(0.38f, 0.68f, 0.94f, 1f);
        c[(int)ImGuiCol.Button]               = new NumVector4(0.095f, 0.105f, 0.118f, 1f);
        c[(int)ImGuiCol.ButtonHovered]        = new NumVector4(0.150f, 0.180f, 0.210f, 1f);
        c[(int)ImGuiCol.ButtonActive]         = blue;
        c[(int)ImGuiCol.Header]               = new NumVector4(0.120f, 0.150f, 0.180f, 1f);
        c[(int)ImGuiCol.HeaderHovered]        = new NumVector4(0.165f, 0.210f, 0.255f, 1f);
        c[(int)ImGuiCol.HeaderActive]         = blue;
        c[(int)ImGuiCol.Separator]            = new NumVector4(0.115f, 0.128f, 0.145f, 1f);
        c[(int)ImGuiCol.SeparatorHovered]     = new NumVector4(0.25f, 0.52f, 0.80f, 1f);
        c[(int)ImGuiCol.SeparatorActive]      = blueBright;
        c[(int)ImGuiCol.ResizeGrip]           = new NumVector4(0.30f, 0.30f, 0.30f, 0.36f);
        c[(int)ImGuiCol.ResizeGripHovered]    = new NumVector4(0.25f, 0.52f, 0.80f, 0.70f);
        c[(int)ImGuiCol.ResizeGripActive]     = blueBright;
        // Pestañas estilo Unity: la activa se funde con el panel (mismo gris que el contenido),
        // las inactivas más oscuras; la ventana sin foco, atenuada. (Antes la activa usaba el
        // azul por defecto de ImGui, que no se parece a Unity.)
        c[(int)ImGuiCol.Tab]                  = new NumVector4(0.070f, 0.078f, 0.088f, 1f);
        c[(int)ImGuiCol.TabHovered]           = new NumVector4(0.130f, 0.165f, 0.200f, 1f);
        c[(int)ImGuiCol.TabSelected]          = new NumVector4(0.095f, 0.105f, 0.118f, 1f);
        c[(int)ImGuiCol.TabDimmed]            = new NumVector4(0.055f, 0.061f, 0.070f, 1f);
        c[(int)ImGuiCol.TabDimmedSelected]    = new NumVector4(0.080f, 0.088f, 0.100f, 1f);
        c[(int)ImGuiCol.DockingPreview]       = new NumVector4(0.32f, 0.62f, 0.92f, 0.70f);
        c[(int)ImGuiCol.TableHeaderBg]        = new NumVector4(0.17f, 0.17f, 0.17f, 1f);
        c[(int)ImGuiCol.TableBorderStrong]    = new NumVector4(0.12f, 0.12f, 0.12f, 1f);
        c[(int)ImGuiCol.TableBorderLight]     = new NumVector4(0.14f, 0.14f, 0.14f, 1f);
        c[(int)ImGuiCol.TextSelectedBg]       = new NumVector4(0.18f, 0.45f, 0.72f, 0.62f);
        c[(int)ImGuiCol.DragDropTarget]       = new NumVector4(0.95f, 0.74f, 0.25f, 0.90f);
    }

    public void WindowResized(int width, int height)
    {
        windowWidth = width;
        windowHeight = height;
    }

    public void Update(GameWindow window, float deltaSeconds)
    {
        ImGui.SetCurrentContext(context);
        SetPerFrameImGuiData(deltaSeconds);
        UpdateInput(window);
        ImGui.NewFrame();
    }

    public void Render()
    {
        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    public void AddInputText(string text)
    {
        var io = ImGui.GetIO();
        foreach (char c in text)
            io.AddInputCharacter(c);
    }

    private void CreateDeviceResources()
    {
        vertexArray = GL.GenVertexArray();
        vertexBuffer = GL.GenBuffer();
        indexBuffer = GL.GenBuffer();

        GL.BindVertexArray(vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        shader = CreateShader();
        attribLocationTex = GL.GetUniformLocation(shader, "Texture");
        attribLocationProjMtx = GL.GetUniformLocation(shader, "ProjMtx");
        attribLocationVtxPos = GL.GetAttribLocation(shader, "Position");
        attribLocationVtxUv = GL.GetAttribLocation(shader, "UV");
        attribLocationVtxColor = GL.GetAttribLocation(shader, "Color");

        int stride = Unsafe.SizeOf<ImDrawVert>();
        GL.EnableVertexAttribArray(attribLocationVtxPos);
        GL.VertexAttribPointer(attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(attribLocationVtxUv);
        GL.VertexAttribPointer(attribLocationVtxUv, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.EnableVertexAttribArray(attribLocationVtxColor);
        GL.VertexAttribPointer(attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        CreateFontTexture();

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
    }

    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, fontTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        io.Fonts.SetTexID((IntPtr)fontTexture);
        io.Fonts.ClearTexData();
    }

    private static int CreateShader()
    {
        const string vertexSource = """
            #version 330 core
            uniform mat4 ProjMtx;
            layout (location = 0) in vec2 Position;
            layout (location = 1) in vec2 UV;
            layout (location = 2) in vec4 Color;
            out vec2 Frag_UV;
            out vec4 Frag_Color;
            void main()
            {
                Frag_UV = UV;
                Frag_Color = Color;
                gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
            }
            """;

        const string fragmentSource = """
            #version 330 core
            uniform sampler2D Texture;
            in vec2 Frag_UV;
            in vec4 Frag_Color;
            out vec4 Out_Color;
            void main()
            {
                Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
            }
            """;

        int vertex = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertex, vertexSource);
        GL.CompileShader(vertex);
        CheckShader(vertex);

        int fragment = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragment, fragmentSource);
        GL.CompileShader(fragment);
        CheckShader(fragment);

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

    private static void CheckShader(int shader)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
    }

    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(windowWidth, windowHeight);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = deltaSeconds > 0f ? deltaSeconds : 1f / 60f;
    }

    private static readonly GlfwKeys[] MappedKeys =
    [
        GlfwKeys.Tab,
        GlfwKeys.Left,
        GlfwKeys.Right,
        GlfwKeys.Up,
        GlfwKeys.Down,
        GlfwKeys.PageUp,
        GlfwKeys.PageDown,
        GlfwKeys.Home,
        GlfwKeys.End,
        GlfwKeys.Insert,
        GlfwKeys.Delete,
        GlfwKeys.Backspace,
        GlfwKeys.Space,
        GlfwKeys.Enter,
        GlfwKeys.Escape,
        GlfwKeys.A,
        GlfwKeys.C,
        GlfwKeys.V,
        GlfwKeys.P,
        GlfwKeys.X,
        GlfwKeys.Y,
        GlfwKeys.Z
    ];

    private static void UpdateInput(GameWindow window)
    {
        var io = ImGui.GetIO();
        var mouse = window.MouseState;
        var keyboard = window.KeyboardState;

        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.IsButtonDown(GlfwMouseButton.Left));
        io.AddMouseButtonEvent(1, mouse.IsButtonDown(GlfwMouseButton.Right));
        io.AddMouseButtonEvent(2, mouse.IsButtonDown(GlfwMouseButton.Middle));
        io.AddMouseWheelEvent(mouse.ScrollDelta.X, mouse.ScrollDelta.Y);

        foreach (GlfwKeys key in MappedKeys)
            io.AddKeyEvent(MapKey(key), keyboard.IsKeyDown(key));

        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(GlfwKeys.LeftControl) || keyboard.IsKeyDown(GlfwKeys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(GlfwKeys.LeftAlt) || keyboard.IsKeyDown(GlfwKeys.RightAlt));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(GlfwKeys.LeftShift) || keyboard.IsKeyDown(GlfwKeys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(GlfwKeys.LeftSuper) || keyboard.IsKeyDown(GlfwKeys.RightSuper));
    }

    private static ImGuiKey MapKey(GlfwKeys key) => key switch
    {
        GlfwKeys.Tab => ImGuiKey.Tab,
        GlfwKeys.Left => ImGuiKey.LeftArrow,
        GlfwKeys.Right => ImGuiKey.RightArrow,
        GlfwKeys.Up => ImGuiKey.UpArrow,
        GlfwKeys.Down => ImGuiKey.DownArrow,
        GlfwKeys.PageUp => ImGuiKey.PageUp,
        GlfwKeys.PageDown => ImGuiKey.PageDown,
        GlfwKeys.Home => ImGuiKey.Home,
        GlfwKeys.End => ImGuiKey.End,
        GlfwKeys.Insert => ImGuiKey.Insert,
        GlfwKeys.Delete => ImGuiKey.Delete,
        GlfwKeys.Backspace => ImGuiKey.Backspace,
        GlfwKeys.Space => ImGuiKey.Space,
        GlfwKeys.Enter => ImGuiKey.Enter,
        GlfwKeys.Escape => ImGuiKey.Escape,
        GlfwKeys.A => ImGuiKey.A,
        GlfwKeys.C => ImGuiKey.C,
        GlfwKeys.V => ImGuiKey.V,
        GlfwKeys.P => ImGuiKey.P,
        GlfwKeys.X => ImGuiKey.X,
        GlfwKeys.Y => ImGuiKey.Y,
        GlfwKeys.Z => ImGuiKey.Z,
        _ => ImGuiKey.None
    };

    private void RenderImDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0)
        {
            LastDrawStats = ImGuiDrawStats.Empty;
            return;
        }

        GL.ActiveTexture(TextureUnit.Texture0);

        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);
        GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        GL.UseProgram(shader);
        GL.Uniform1(attribLocationTex, 0);
        Matrix4 projection = Matrix4.CreateOrthographicOffCenter(0f, windowWidth, windowHeight, 0f, -1f, 1f);
        GL.UniformMatrix4(attribLocationProjMtx, false, ref projection);
        GL.BindVertexArray(vertexArray);

        int drawCalls = 0;
        int textureBinds = 0;
        int boundTexture = -1;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];
            int vertexSize = cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
            if (vertexSize > vertexBufferSize)
            {
                vertexBufferSize = (int)(vertexSize * 1.5f);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);
                GL.BufferData(BufferTarget.ArrayBuffer, vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }

            int indexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
            if (indexSize > indexBufferSize)
            {
                indexBufferSize = (int)(indexSize * 1.5f);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffer);
                GL.BufferData(BufferTarget.ElementArrayBuffer, indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertexSize, cmdList.VtxBuffer.Data);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffer);
            GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, indexSize, cmdList.IdxBuffer.Data);

            for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.UserCallback != IntPtr.Zero)
                    continue;

                var clip = pcmd.ClipRect;
                int clipX = (int)clip.X;
                int clipY = (int)(windowHeight - clip.W);
                int clipW = (int)(clip.Z - clip.X);
                int clipH = (int)(clip.W - clip.Y);
                if (clipW <= 0 || clipH <= 0)
                    continue;

                int texture = (int)pcmd.TextureId;
                if (texture != boundTexture)
                {
                    GL.BindTexture(TextureTarget.Texture2D, texture);
                    boundTexture = texture;
                    textureBinds++;
                }

                GL.Scissor(
                    clipX,
                    clipY,
                    clipW,
                    clipH);

                GL.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    (int)pcmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (IntPtr)(pcmd.IdxOffset * sizeof(ushort)),
                    (int)pcmd.VtxOffset);
                drawCalls++;
            }
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.ActiveTexture(TextureUnit.Texture0);
        LastDrawStats = new ImGuiDrawStats(drawData.CmdListsCount, drawCalls, drawData.TotalVtxCount, drawData.TotalIdxCount, textureBinds);
    }

    public void Dispose()
    {
        GL.DeleteBuffer(vertexBuffer);
        GL.DeleteBuffer(indexBuffer);
        GL.DeleteVertexArray(vertexArray);
        GL.DeleteTexture(fontTexture);
        GL.DeleteProgram(shader);
        ImGui.DestroyContext(context);
    }
}

internal readonly struct ImGuiDrawStats
{
    public static readonly ImGuiDrawStats Empty = new(0, 0, 0, 0, 0);

    public ImGuiDrawStats(int commandLists, int drawCalls, int vertices, int indices, int textureBinds)
    {
        CommandLists = commandLists;
        DrawCalls = drawCalls;
        Vertices = vertices;
        Indices = indices;
        TextureBinds = textureBinds;
    }

    public int CommandLists { get; }
    public int DrawCalls { get; }
    public int Vertices { get; }
    public int Indices { get; }
    public int TextureBinds { get; }
}
