using GrokoEngine.ImGuiEditor;          // ImGuiController (archivo enlazado)
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace GrokoEngine.Hub;

/// <summary>
/// Ventana del Groko Engine Hub: lista de proyectos recientes + crear / abrir,
/// que lanza el editor (GrokoEngine.ImGuiEditor) sobre el proyecto elegido.
/// </summary>
internal sealed class HubApp : GameWindow
{
    private ImGuiController imgui = null!;
    private List<ProjectEntry> projects = new();

    // Estado del popup "Nuevo proyecto"
    private bool newPopupOpen;
    private string newName = "MiProyecto";
    private string newLocation = "";
    private string statusMessage = "";

    public HubApp()
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            Title = "GrokoEngine Hub",
            ClientSize = new Vector2i(1100, 680),
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core
        })
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        VSync = VSyncMode.On;
        GL.ClearColor(0.105f, 0.115f, 0.125f, 1f);
        imgui = new ImGuiController(ClientSize.X, ClientSize.Y);
        projects = RecentProjects.Load();
        newLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GrokoProjects");
    }

    protected override void OnUnload()
    {
        imgui.Dispose();
        base.OnUnload();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        imgui.WindowResized(ClientSize.X, ClientSize.Y);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        imgui.AddInputText(e.AsString);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        imgui.Update(this, (float)args.Time);
        DrawHubUI();
        imgui.Render();

        SwapBuffers();
    }

    private void DrawHubUI()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(io.DisplaySize);
        ImGui.Begin("##GrokoHub",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus);

        // ── Cabecera ──
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.94f, 0.96f, 1f));
        ImGui.SetWindowFontScale(1.6f);
        ImGui.TextUnformatted("GrokoEngine Hub");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();
        ImGui.TextDisabled("Gestiona y abre tus proyectos");
        ImGui.Spacing();

        // ── Barra de acciones ──
        if (ImGui.Button("  Nuevo proyecto  "))
        {
            newPopupOpen = true;
            ImGui.OpenPopup("Nuevo proyecto");
        }
        ImGui.SameLine();
        if (ImGui.Button("  Abrir...  "))
            OpenExistingDialog();
        ImGui.SameLine();
        if (ImGui.Button("  Refrescar  "))
            projects = RecentProjects.Load();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Lista de recientes ──
        ImGui.TextDisabled($"Proyectos recientes ({projects.Count})");
        DrawRecentsTable();

        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextWrapped(statusMessage);
        }

        DrawNewProjectPopup();

        ImGui.End();
    }

    private void DrawRecentsTable()
    {
        if (!ImGui.BeginTable("##recents", 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Nombre", ImGuiTableColumnFlags.WidthFixed, 220f);
        ImGui.TableSetupColumn("Ruta", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Última apertura", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("##acc", ImGuiTableColumnFlags.WidthFixed, 34f);
        ImGui.TableHeadersRow();

        ProjectEntry? toLaunch = null;
        ProjectEntry? toRemove = null;

        foreach (var p in projects)
        {
            ImGui.TableNextRow();
            bool exists = p.Exists;

            ImGui.TableSetColumnIndex(0);
            if (!exists) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.45f, 0.45f, 1f));
            bool clicked = ImGui.Selectable(p.Name + "##" + p.Path, false, ImGuiSelectableFlags.AllowDoubleClick);
            if (!exists) ImGui.PopStyleColor();
            if (clicked && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                toLaunch = p;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(exists ? "Doble clic para abrir" : "La carpeta ya no existe");

            ImGui.TableSetColumnIndex(1);
            ImGui.TextDisabled(p.Path + (exists ? "" : "   (no encontrado)"));

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(p.LastOpened.ToString("yyyy-MM-dd HH:mm"));

            ImGui.TableSetColumnIndex(3);
            if (ImGui.SmallButton("X##" + p.Path))
                toRemove = p;
        }

        ImGui.EndTable();

        // Aplicar acciones fuera del bucle de render de la tabla.
        if (toLaunch != null) LaunchProject(toLaunch.Path);
        if (toRemove != null) projects = RecentProjects.Remove(projects, toRemove);
    }

    private void DrawNewProjectPopup()
    {
        Vector2 center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(540f, 0f));

        if (!ImGui.BeginPopupModal("Nuevo proyecto", ref newPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted("Nombre del proyecto");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##name", ref newName, 128);
        ImGui.Spacing();

        ImGui.TextUnformatted("Ubicación");
        ImGui.SetNextItemWidth(-96f);
        ImGui.InputText("##loc", ref newLocation, 512);
        ImGui.SameLine();
        if (ImGui.Button("Examinar", new Vector2(86f, 0f)))
        {
            string? picked = FolderDialog.Pick(newLocation);
            if (picked != null) newLocation = picked;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Se creará: " + Path.Combine(newLocation, newName.Trim()));
        ImGui.Separator();

        if (ImGui.Button("Crear y abrir", new Vector2(140f, 0f)))
        {
            try
            {
                string projectPath = ProjectFactory.CreateProject(newLocation, newName);
                projects = RecentProjects.AddOrTouch(projects, projectPath);
                LaunchProject(projectPath);
                newPopupOpen = false;
                ImGui.CloseCurrentPopup();
            }
            catch (Exception ex)
            {
                statusMessage = "No se pudo crear el proyecto: " + ex.Message;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancelar", new Vector2(100f, 0f)))
        {
            newPopupOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void OpenExistingDialog()
    {
        string? picked = FolderDialog.Pick(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (picked == null) return;

        // El editor espera una subcarpeta Assets/: si no existe, se prepara.
        string assets = Path.Combine(picked, "Assets");
        if (!Directory.Exists(assets))
        {
            try { Directory.CreateDirectory(Path.Combine(assets, "Scenes")); }
            catch (Exception ex) { statusMessage = "No se pudo preparar la carpeta: " + ex.Message; return; }
        }
        LaunchProject(picked);
    }

    private void LaunchProject(string projectPath)
    {
        projects = RecentProjects.AddOrTouch(projects, projectPath);
        statusMessage = EditorLauncher.Launch(projectPath, out string error)
            ? "Abriendo " + Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar)) + "…"
            : error;
    }
}
