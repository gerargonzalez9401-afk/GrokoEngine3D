using GrokoEngine;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using Vec4 = System.Numerics.Vector4;
using ShaderGraphModel = GrokoShaderGraphPro.Models.ShaderGraphModel;
using ShaderGraphTemplates = GrokoShaderGraphPro.Services.GraphTemplates;
using ShaderCodeGenerator = GrokoShaderGraphPro.Services.ShaderCodeGenerator;
using ShaderGraphValidator = GrokoShaderGraphPro.Services.GraphValidator;
using GraphPin = GrokoShaderGraphPro.Models.GraphPin;
using GraphConnection = GrokoShaderGraphPro.Models.GraphConnection;
using NodeKind = GrokoShaderGraphPro.Models.NodeKind;
using PinType = GrokoShaderGraphPro.Models.PinType;
using PinDirection = GrokoShaderGraphPro.Models.PinDirection;
using GraphProperty = GrokoShaderGraphPro.Models.GraphProperty;
using PropertyAttribute = GrokoShaderGraphPro.Models.PropertyAttribute;
using PropertyColorMode = GrokoShaderGraphPro.Models.PropertyColorMode;
using GlfwKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace GrokoEngine.ImGuiEditor;
internal sealed partial class ImGuiEditorApp
{
    private static int particleGradientSelectedKey = 1;

    // Opciones de creación de objetos. parent == null → crea en la raíz; si no, como hijo de parent.
    private void DrawObjectCreationMenu(GameObject? parent)
    {
        if (ImGui.MenuItem("Create Empty"))
        {
            if (parent == null) CreateEmpty(); else CreateChild(parent, "GameObject", 0);
        }
        if (ImGui.BeginMenu("3D Object"))
        {
            if (ImGui.MenuItem("Cube")) { if (parent == null) CreateCube(); else CreateChildCube(parent); }
            if (ImGui.MenuItem("Sphere")) { if (parent == null) CreateSphere(); else CreateChildPrimitive(parent, "Sphere", 3); }
            if (ImGui.MenuItem("Cylinder")) { if (parent == null) CreateCylinder(); else CreateChildPrimitive(parent, "Cylinder", 4); }
            if (ImGui.MenuItem("Quad")) { if (parent == null) CreateQuad(); else CreateChildPrimitive(parent, "Quad", 5); }
            if (ImGui.MenuItem("Capsule")) { if (parent == null) CreateCapsule(); else CreateChildPrimitive(parent, "Capsule", 6); }
            if (ImGui.MenuItem("Plane")) { if (parent == null) CreatePlane(); else CreateChildPlane(parent); }
            if (ImGui.MenuItem("Cube With Gravity")) { if (parent == null) CreateCubeWithGravity(); else CreateChildCubeWithGravity(parent); }
            if (ImGui.MenuItem("Terrain")) { if (parent == null) CreateTerrain(); else CreateChildTerrain(parent); }
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Effects"))
        {
            if (ImGui.MenuItem("Particle System")) { if (parent == null) CreateParticleSystem(); else CreateChildParticleSystem(parent); }
            ImGui.EndMenu();
        }
        if (ImGui.MenuItem("Camera")) { if (parent == null) CreateCamera(); else CreateChildCamera(parent); }
        if (ImGui.BeginMenu("Light"))
        {
            if (ImGui.MenuItem("Directional Light")) { if (parent == null) CreateDirectionalLight(); else CreateChildLight<DirectionalLight>(parent, "Directional Light"); }
            if (ImGui.MenuItem("Point Light")) { if (parent == null) CreatePointLight(); else CreateChildLight<PointLight>(parent, "Point Light"); }
            if (ImGui.MenuItem("Spot Light")) { if (parent == null) CreateSpotLight(); else CreateChildLight<SpotLight>(parent, "Spot Light"); }
            if (ImGui.MenuItem("Ambient Light")) { if (parent == null) CreateAmbientLight(); else CreateChildLight<AmbientLight>(parent, "Ambient Light"); }
            if (ImGui.MenuItem("Area Light")) { if (parent == null) CreateAreaLight(); else CreateChildLight<AreaLight>(parent, "Area Light"); }
            if (ImGui.MenuItem("Rectangle Light")) { if (parent == null) CreateRectangleLight(); else CreateChildLight<RectangleLight>(parent, "Rectangle Light"); }
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("UI"))
        {
            // Crean el GameObject ya con su componente (estilo Unity).
            // Si no existe Canvas, se crea automáticamente.
            if (ImGui.MenuItem("Canvas")) CreateCanvas();
            ImGui.Separator();
            if (ImGui.MenuItem("Panel")) CreateUIElement<UIPanel>("Panel");
            if (ImGui.MenuItem("Image")) CreateUIElement<UIImage>("Image");
            if (ImGui.MenuItem("Raw Image")) CreateUIElement<UIRawImage>("Raw Image");
            if (ImGui.MenuItem("Text")) CreateUIElement<UIText>("Text");
            if (ImGui.MenuItem("Button")) CreateUIElement<UIButton>("Button");
            if (ImGui.MenuItem("Toggle")) CreateUIElement<UIToggle>("Toggle");
            if (ImGui.MenuItem("Slider")) CreateUIElement<UISlider>("Slider");
            if (ImGui.MenuItem("Scrollbar")) CreateUIElement<UIScrollbar>("Scrollbar");
            if (ImGui.MenuItem("Dropdown")) CreateUIElement<UIDropdown>("Dropdown");
            if (ImGui.MenuItem("Input Field")) CreateUIElement<UIInputField>("Input Field");
            if (ImGui.MenuItem("Scroll View")) CreateUIElement<UIScrollView>("Scroll View");
            if (ImGui.MenuItem("Mask")) CreateUIElement<UIMask>("Mask");
            if (ImGui.MenuItem("Rect Mask 2D")) CreateUIElement<UIRectMask2D>("Rect Mask 2D");
            if (ImGui.MenuItem("Health Bar")) CreateUIElement<UIBar>("Health Bar");
            ImGui.EndMenu();
        }
    }

    private GameObject CreateCube()
    {
        return CommitSceneMutation("Create Cube", () =>
        {
            var obj = CreateObject("Cube", 1);
            obj.AddComponentWithEngine<BoxCollider>(physicsEngine);
            return obj;
        });
    }

    private GameObject CreatePlane()
    {
        return CommitSceneMutation("Create Plane", () =>
        {
            var obj = CreateObject("Plane", 2);
            obj.ScaleX = 4f;
            obj.ScaleZ = 4f;
            var collider = obj.AddComponentWithEngine<BoxCollider>(physicsEngine);
            collider.Size = new Vector3(1f, 0.05f, 1f);
            return obj;
        });
    }

    private GameObject CreateTerrain()
    {
        return CommitSceneMutation("Create Terrain", () =>
        {
            var obj = CreateObject("Terrain", 0);
            obj.AddComponent<Terrain>();
            obj.AddComponentWithEngine<TerrainCollider>(physicsEngine);
            return obj;
        });
    }

    private GameObject CreateSphere() => CreatePrimitive("Sphere", 3);
    private GameObject CreateCylinder() => CreatePrimitive("Cylinder", 4);
    private GameObject CreateQuad() => CreatePrimitive("Quad", 5);
    private GameObject CreateCapsule() => CreatePrimitive("Capsule", 6);

    private GameObject CreatePrimitive(string name, int type)
    {
        return CommitSceneMutation("Create " + name, () =>
        {
            var obj = CreateObject(name, type);
            AddDefaultPrimitiveCollider(obj);
            return obj;
        });
    }

    


    private GameObject CreateCubeWithGravity()
    {
        return CommitSceneMutation("Create Cube With Gravity", () =>
        {
            var obj = CreateObject("Cube", 1);
            obj.AddComponentWithEngine<BoxCollider>(physicsEngine);
            obj.PosY = 3f;
            obj.AddComponentWithEngine<Rigidbody>(physicsEngine);
            statusMessage = $"Created {obj.Name} with Rigidbody";
            return obj;
        });
    }

    private GameObject CreateCamera()
    {
        return CommitSceneMutation("Create Camera", () =>
        {
            var obj = CreateObject("Main Camera", 0);
            obj.IsCamera = true;
            obj.PosY = 1f;
            obj.PosZ = 5f;
            obj.RotY = 180f;
            obj.AddComponent<Camera>();
            return obj;
        });
    }

    


    


    


    


    


    


    


    private void DuplicateSelected()
    {
        if (selected == null) return;
        DuplicateObject(selected);
    }

    private void DuplicateObject(GameObject source)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot duplicate while playing";
            return;
        }

        CommitSceneMutation("Duplicate " + source.Name, () =>
        {
            var clone = SceneSerializer.DeserializeObject(SceneSerializer.SerializeObject(source), physicsEngine, scriptCompiler);
            ResetIds(clone);
            clone.Name = source.Parent != null
                ? GetUniqueChildObjectName(source.Parent, source.Name + "_Copy")
                : GetUniqueObjectName(source.Name + "_Copy");
            clone.Parent = source.Parent;
            if (source.Parent == null)
                objects.Add(clone);
            selected = clone;
            statusMessage = $"Duplicated {clone.Name}";
            return clone;
        });
    }

    private void DeleteSelected()
    {
        if (selected == null) return;
        DeleteObject(selected);
    }

    private void DeleteObject(GameObject target)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot delete while playing";
            return;
        }

        string name = target.Name;
        CommitSceneMutation("Delete " + name, () =>
        {
            UnregisterCollidersRecursive(target);
            if (target.Parent != null)
                target.Parent.Children.Remove(target);
            else
                objects.Remove(target);
            statusMessage = $"Deleted {name}";
            selected = objects.FirstOrDefault();
            return true;
        });
    }

    private void ReparentObject(GameObject obj, GameObject? newParent, int newIndex)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot reparent while playing";
            return;
        }

        if (obj == newParent || (newParent != null && sceneGraph.IsDescendantOf(newParent, obj)))
        {
            statusMessage = "Cannot parent an object under itself";
            return;
        }

        int targetIndex = newIndex;

        if (obj.Parent == newParent && sceneGraph.IndexOf(obj) == targetIndex)
            return;

        CommitSceneMutation("Reparent " + obj.Name, () =>
        {
            // Preserva la posición de MUNDO al emparentar (como Unity), para que no "salte".
            var worldPos = obj.Position;
            sceneGraph.Attach(obj, newParent, targetIndex);
            obj.Position = worldPos;
            selected = obj;
            statusMessage = newParent == null
                ? $"Moved {obj.Name} to root"
                : $"Moved {obj.Name} under {newParent.Name}";
            return obj;
        });
    }

    private void ApplySelectedPrefab()
    {
        if (selected != null)
            ApplyPrefab(selected);
    }

    private void ApplyPrefab(GameObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.PrefabAssetPath) || !File.Exists(obj.PrefabAssetPath))
        {
            statusMessage = "Prefab asset not found";
            return;
        }

        try
        {
            SceneSerializer.SavePrefab(obj.PrefabAssetPath, obj);
            statusMessage = "Prefab applied: " + Path.GetFileName(obj.PrefabAssetPath);
        }
        catch (Exception ex)
        {
            statusMessage = "Prefab apply failed: " + ex.Message;
        }
    }

    


    


    // ── Lightmap Baking ───────────────────────────────────────────
    


    


    


    


    


    


    


    


    


    


    


    private bool _isBaking;
    private readonly Dictionary<string, string> _lightmapPaths = new();

    


    private static int CountStaticMeshes(IReadOnlyList<GameObject> objs)
    {
        int count = 0;
        foreach (var obj in objs)
        {
            if (obj.IsStatic && obj.GetComponent<MeshFilter>() != null) count++;
            count += CountStaticMeshes(obj.Children);
        }
        return count;
    }

    private static int CountStaticMeshes(List<GameObject> objs)
        => CountStaticMeshes((IReadOnlyList<GameObject>)objs);


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    private void DrawAddComponentPopup(GameObject obj)
    {
        if (isPlaying)
        {
            if (ImGui.BeginPopup("AddComponentPopup"))
            {
                ImGui.TextDisabled("Stop Play mode to add components");
                ImGui.EndPopup();
            }
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(330f, 390f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("AddComponentPopup")) return;

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new System.Numerics.Vector4(0.135f, 0.135f, 0.135f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.095f, 0.095f, 0.100f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new System.Numerics.Vector4(0.125f, 0.125f, 0.130f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new System.Numerics.Vector4(0.145f, 0.145f, 0.150f, 1f));

        DrawAddComponentCenteredText("Add Component", 0.82f);
        if (addComponentSearchFocus)
        {
            ImGui.SetKeyboardFocusHere();
            addComponentSearchFocus = false;
        }

        float contentW = AddComponentContentWidth();
        CenterNextItem(contentW);
        ImGui.SetNextItemWidth(contentW);
        ImGui.InputTextWithHint("##AddComponentSearch", "Search", ref addComponentSearch, 96);
        ImGui.Dummy(new Vector2(0f, 2f));

        var entries = BuildAddComponentEntries(obj);
        string lastCategory = string.Empty;
        int visibleCount = 0;
        string filter = addComponentSearch.Trim();

        ImGui.BeginChild("##AddComponentList", new Vector2(0f, 286f), ImGuiChildFlags.None);
        foreach (var entry in entries)
        {
            if (!MatchesAddComponentSearch(entry, filter))
                continue;

            if (!string.Equals(lastCategory, entry.Category, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(lastCategory))
                    ImGui.Spacing();
                DrawAddComponentCategory(entry.Category, AddComponentContentWidth());
                lastCategory = entry.Category;
            }

            visibleCount++;
            DrawAddComponentEntry(obj, entry);
        }

        if (visibleCount == 0)
        {
            ImGui.Dummy(new Vector2(0f, 24f));
            DrawAddComponentCenteredText("No components found", 0.58f);
            if (scriptCompiler.CompiledTypes.Count == 0)
            {
                ImGui.Spacing();
                DrawAddComponentCenteredText("Compile scripts to show custom components", 0.50f);
            }
        }
        ImGui.EndChild();

        ImGui.Dummy(new Vector2(0f, 2f));
        CenterNextItem(120f);
        if (ImGui.Button("Compile Scripts", new Vector2(120f, 22f)))
            CompileScripts();

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(3);

        ImGui.EndPopup();
    }

    private List<AddComponentEntry> BuildAddComponentEntries(GameObject obj)
    {
        var entries = new List<AddComponentEntry>
        {
            new("Physics", "Rigidbody", "Physics body with mass, drag and gravity.", true, () => obj.AddComponentWithEngine<Rigidbody>(physicsEngine)),
            new("Physics", "Box Collider", "Box shape used for collisions and triggers.", true, () => obj.AddComponentWithEngine<BoxCollider>(physicsEngine)),
            new("Physics", "Sphere Collider", "Sphere shape used for radial collisions and triggers.", true, () => obj.AddComponentWithEngine<SphereCollider>(physicsEngine)),
            new("Physics", "Capsule Collider", "Capsule shape for characters and rounded volumes.", true, () => obj.AddComponentWithEngine<CapsuleCollider>(physicsEngine)),
            new("Physics", "Mesh Collider", "Collider fitted from the object's mesh bounds.", true, () => obj.AddComponentWithEngine<MeshCollider>(physicsEngine)),
            new("Physics", "Character Controller", "Kinematic player movement with step offset, gravity and grounded state.", obj.GetComponent<CharacterController>() == null, () => AddCharacterController(obj)),
            new("Rendering", "Material", "Editable material instance for this object.", true, () => obj.AddComponent<Material>()),
            new("Rendering", "Mesh Filter", "Mesh source used by the renderer.", obj.GetComponent<MeshFilter>() == null, () => obj.AddComponent<MeshFilter>()),
            new("Rendering", "Mesh Renderer", "Renderer settings and materials (Unity-style).", obj.GetComponent<MeshRenderer>() == null, () => obj.AddComponent<MeshRenderer>()),
            new("Rendering", "Post Process Settings", "Camera and scene render finishing controls.", true, () => obj.AddComponent<PostProcessSettings>()),
            new("Camera", "Camera", "Render view camera for play mode.", obj.GetComponent<Camera>() == null, () =>
            {
                obj.IsCamera = true;
                return obj.AddComponent<Camera>();
            }),
            new("Lighting", "Directional Light", "Sun-like light with parallel rays.", true, () => obj.AddComponent<DirectionalLight>()),
            new("Lighting", "Point Light", "Omnidirectional local light.", true, () => obj.AddComponent<PointLight>()),
            new("Lighting", "Spot Light", "Cone light for focused beams.", true, () => obj.AddComponent<SpotLight>()),
            new("Lighting", "Ambient Light", "Global fill light for the scene.", true, () => obj.AddComponent<AmbientLight>()),
            new("Lighting", "Area Light", "Soft rectangular or planar light.", true, () => obj.AddComponent<AreaLight>()),
            new("Lighting", "Rectangle Light", "Rectangular light for panels and windows.", true, () => obj.AddComponent<RectangleLight>()),
            new("Effects", "Particle System", "Preview particle emitter.", obj.GetComponent<GrokoEngine.ParticleSystem>() == null, () => AddPreviewParticleSystem(obj)),
            new("Animation", "Animator", "Unity-style controller, avatar and animation playback.", obj.GetComponent<Animator>() == null, () => obj.AddComponent<Animator>()),
            new("UI", "Canvas", "Root for in-game UI / HUD.", obj.GetComponent<Canvas>() == null, () => obj.AddComponent<Canvas>()),
            new("UI", "Panel", "Window/panel background for UI containers.", true, () => obj.AddComponent<UIPanel>()),
            new("UI", "Image", "Colored rectangle or sprite on screen.", true, () => obj.AddComponent<UIImage>()),
            new("UI", "Raw Image", "Raw texture display.", true, () => obj.AddComponent<UIRawImage>()),
            new("UI", "Text", "On-screen text label.", true, () => obj.AddComponent<UIText>()),
            new("UI", "Button", "Clickable visual button prepared for UI events.", true, () => obj.AddComponent<UIButton>()),
            new("UI", "Toggle", "Checkbox/toggle selectable.", true, () => obj.AddComponent<UIToggle>()),
            new("UI", "Slider", "Value slider selectable.", true, () => obj.AddComponent<UISlider>()),
            new("UI", "Scrollbar", "Scrollbar selectable.", true, () => obj.AddComponent<UIScrollbar>()),
            new("UI", "Dropdown", "Dropdown menu selectable.", true, () => obj.AddComponent<UIDropdown>()),
            new("UI", "Input Field", "Focusable text input field.", true, () => obj.AddComponent<UIInputField>()),
            new("UI", "Scroll View", "Masked scrollable viewport.", true, () => obj.AddComponent<UIScrollView>()),
            new("UI", "Mask", "UI clipping mask.", true, () => obj.AddComponent<UIMask>()),
            new("UI", "Rect Mask 2D", "Rectangular UI clipping mask.", true, () => obj.AddComponent<UIRectMask2D>()),
            new("UI", "Horizontal Layout Group", "Automatic horizontal UI layout.", true, () => obj.AddComponent<UIHorizontalLayoutGroup>()),
            new("UI", "Vertical Layout Group", "Automatic vertical UI layout.", true, () => obj.AddComponent<UIVerticalLayoutGroup>()),
            new("UI", "Grid Layout Group", "Automatic grid UI layout.", true, () => obj.AddComponent<UIGridLayoutGroup>()),
            new("UI", "Layout Element", "Per-child layout constraints.", true, () => obj.AddComponent<UILayoutElement>()),
            new("UI", "Content Size Fitter", "Auto-size from children.", true, () => obj.AddComponent<UIContentSizeFitter>()),
            new("UI", "Aspect Ratio Fitter", "Keep UI aspect ratio.", true, () => obj.AddComponent<UIAspectRatioFitter>()),
            new("UI", "Health Bar", "Filled progress / health bar.", true, () => obj.AddComponent<UIBar>())
        };

        foreach (Type type in scriptCompiler.CompiledTypes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            Type capturedType = type;
            entries.Add(new AddComponentEntry("Scripts", capturedType.Name, "Compiled project script.", true, () => obj.AddComponent(capturedType, physicsEngine)));
        }

        return entries;
    }

    private static bool MatchesAddComponentSearch(AddComponentEntry entry, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static float AddComponentContentWidth()
    {
        return MathF.Min(292f, MathF.Max(180f, ImGui.GetContentRegionAvail().X - 2f));
    }

    private static void CenterNextItem(float width)
    {
        float available = ImGui.GetContentRegionAvail().X;
        float offset = MathF.Max(0f, (available - width) * 0.5f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
    }

    private static void DrawAddComponentCenteredText(string text, float brightness)
    {
        float width = ImGui.CalcTextSize(text).X;
        CenterNextItem(width);
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(brightness, brightness, brightness, 1f));
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void DrawAddComponentCategory(string category, float width)
    {
        CenterNextItem(width);
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.56f, 0.56f, 0.56f, 1f));
        ImGui.TextUnformatted(category);
        ImGui.PopStyleColor();
    }

    private void DrawAddComponentEntry(GameObject obj, AddComponentEntry entry)
    {
        ImGui.PushID(entry.Name);
        float rowW = AddComponentContentWidth();
        float rowH = 23f;
        CenterNextItem(rowW);
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##AddComponentRow", new Vector2(rowW, rowH));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        Vector2 rowMax = rowMin + new Vector2(rowW, rowH);
        var drawList = ImGui.GetWindowDrawList();
        System.Numerics.Vector4 fill = hovered && entry.Enabled
            ? new System.Numerics.Vector4(0.235f, 0.235f, 0.240f, 1f)
            : new System.Numerics.Vector4(0.170f, 0.170f, 0.175f, 0.72f);
        drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(fill), 2f);

        var textColor = entry.Enabled
            ? new System.Numerics.Vector4(0.76f, 0.76f, 0.76f, 1f)
            : new System.Numerics.Vector4(0.43f, 0.43f, 0.43f, 1f);
        drawList.AddText(rowMin + new Vector2(10f, 4f), ImGui.GetColorU32(textColor), entry.Name);

        if (hovered)
            DrawTooltip(entry.Enabled ? entry.Description : "This object already has this component.");

        if (clicked && entry.Enabled)
        {
            AddComponentToObject(obj, entry.Name, entry.Add);
            ImGui.CloseCurrentPopup();
        }

        ImGui.PopID();
    }

    private void AddComponentToObject(GameObject obj, string componentName, Func<Component> add)
    {
        CommitSceneMutation("Add " + componentName, () =>
        {
            var component = add();
            selected = obj;
            statusMessage = $"Added {component.GetType().Name}";
            return component;
        });
    }

    


    


    


    


    private void DrawComponent(GameObject obj, Component component)
    {
        ImGui.PushID(component.GetHashCode());
        bool isScript = component is MonoBehaviour;
        string title = isScript ? component.GetType().Name + " (Script)" : component.GetType().Name;
        bool open = DrawComponentFrame(title, obj.EditorId + ":" + component.GetHashCode(), component, true, isScript, out bool menuRequested, out bool openScriptRequested);

        if (isScript && openScriptRequested)
            OpenScriptInEditor(component.GetType().Name);

        if (menuRequested)
            ImGui.OpenPopup("ComponentMenu");
        if (ImGui.BeginPopup("ComponentMenu"))
        {
            if (ImGui.MenuItem("Reset Component"))
                ResetComponent(obj, component);
            if (ImGui.MenuItem("Copy Component"))
                CopyComponentToClipboard(component);
            ImGui.Separator();
            int componentIndex = obj.Components.IndexOf(component);
            if (ImGui.MenuItem("Move Up", "", false, componentIndex > 0))
                MoveComponent(obj, component, -1);
            if (ImGui.MenuItem("Move Down", "", false, componentIndex >= 0 && componentIndex < obj.Components.Count - 1))
                MoveComponent(obj, component, 1);
            ImGui.Separator();
            if (ImGui.MenuItem("Remove Component"))
                RemoveComponent(obj, component);
            ImGui.EndPopup();
        }

        if (open)
        {
            switch (component)
            {
                case BoxCollider box:
                    DrawBoxColliderInspector(box);
                    break;
                case SphereCollider sphere:
                    DrawSphereColliderInspector(sphere);
                    break;
                case CapsuleCollider capsule:
                    DrawCapsuleColliderInspector(capsule);
                    break;
                case MeshCollider meshCollider:
                    DrawMeshColliderInspector(meshCollider);
                    break;
                case Rigidbody rb:
                    DrawFloat("Mass", rb.Mass, v => rb.Mass = v, 0.05f, 0.001f, 10000f);
                    DrawFloat("Drag", rb.Drag, v => rb.Drag = v, 0.01f, 0f, 100f);
                    DrawFloat("Angular Drag", rb.AngularDrag, v => rb.AngularDrag = v, 0.01f, 0f, 100f);
                    DrawFloat("Bounciness", rb.Bounciness, v => rb.Bounciness = v, 0.01f, 0f, 1f);
                    DrawFloat("Friction", rb.Friction, v => rb.Friction = v, 0.01f, 0f, 1f);
                    DrawCheckRow("Use Gravity", rb.UseGravity, v => rb.UseGravity = v);
                    DrawCheckRow("Is Kinematic", rb.IsKinematic, v => rb.IsKinematic = v);
                    ImGui.Spacing();
                    ImGui.TextDisabled("Constraints");
                    DrawCheckRow("Freeze Position X", rb.FreezePositionX, v => rb.FreezePositionX = v);
                    DrawCheckRow("Freeze Position Y", rb.FreezePositionY, v => rb.FreezePositionY = v);
                    DrawCheckRow("Freeze Position Z", rb.FreezePositionZ, v => rb.FreezePositionZ = v);
                    DrawCheckRow("Freeze Rotation X", rb.FreezeRotationX, v => rb.FreezeRotationX = v);
                    DrawCheckRow("Freeze Rotation Y", rb.FreezeRotationY, v => rb.FreezeRotationY = v);
                    DrawCheckRow("Freeze Rotation Z", rb.FreezeRotationZ, v => rb.FreezeRotationZ = v);
                    break;
                case CharacterController cc:
                    DrawCheckRow("Auto Center", cc.AutoCenter, v =>
                    {
                        cc.AutoCenter = v;
                        cc.EnsureCollider();
                        physicsEngine.MarkSpatialHashDirty();
                    });
                    DrawFloat("Height", cc.Height, v =>
                    {
                        cc.Height = Math.Max(0.001f, v);
                        cc.EnsureCollider();
                        physicsEngine.MarkSpatialHashDirty();
                    }, 0.05f, 0.001f, 100f);
                    DrawFloat("Radius", cc.Radius, v =>
                    {
                        cc.Radius = Math.Max(0.001f, v);
                        cc.EnsureCollider();
                        physicsEngine.MarkSpatialHashDirty();
                    }, 0.01f, 0.001f, 50f);
                    DrawVector3("Center", cc.Center, v =>
                    {
                        cc.Center = v;
                        if (cc.AutoCenter)
                            cc.Center = new Vector3(cc.Center.X, Math.Max(cc.Radius * 2f, cc.Height) * 0.5f, cc.Center.Z);
                        cc.EnsureCollider();
                        physicsEngine.MarkSpatialHashDirty();
                    }, 0.05f);
                    DrawFloat("Skin Width", cc.SkinWidth, v => cc.SkinWidth = Math.Clamp(v, 0f, 0.5f), 0.005f, 0f, 0.5f);
                    DrawFloat("Step Offset", cc.StepOffset, v => cc.StepOffset = Math.Max(0f, v), 0.025f, 0f, 5f);
                    DrawFloat("Slope Limit", cc.SlopeLimit, v => cc.SlopeLimit = Math.Clamp(v, 0f, 89f), 1f, 0f, 89f);
                    DrawCheckRow("Use Gravity", cc.UseGravity, v => cc.UseGravity = v);
                    DrawFloat("Gravity", cc.Gravity, v => cc.Gravity = Math.Max(0f, v), 0.05f, 0f, 100f);
                    DrawFloat("Jump Speed", cc.JumpSpeed, v => cc.JumpSpeed = Math.Max(0f, v), 0.05f, 0f, 100f);
                    DrawFloat("Max Fall Speed", cc.MaxFallSpeed, v => cc.MaxFallSpeed = Math.Max(0f, v), 0.25f, 0f, 500f);
                    DrawFloat("Push Power", cc.PushPower, v => cc.PushPower = Math.Max(0f, v), 0.05f, 0f, 100f);
                    ImGui.TextDisabled($"Grounded: {(cc.IsGrounded ? "Yes" : "No")}  Flags: {cc.CollisionFlags}");
                    break;
                case Material mat:
                    DrawAssetSlot("Shader", mat.ShaderGraphPath, "Standard", path =>
                    {
                        if (string.IsNullOrWhiteSpace(path) || path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                        {
                            CommitSceneMutation("Edit Material Shader Graph", () =>
                            {
                                mat.ShaderGraphPath = path;
                                sceneRenderer.InvalidateStaticBatch();
                                return mat;
                            });
                        }
                    }, p => p.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(mat.ShaderGraphPath))
                    {
                        FieldRow("");
                        if (ImGui.Button("Edit...##mat_shader_edit", new Vector2(-1f, 22f)))
                        {
                            string? sgPath = SceneViewportRenderer.NormalizeExistingAssetPath(mat.ShaderGraphPath);
                            if (sgPath != null) OpenShaderGraphAsset(sgPath);
                        }
                    }
                    DrawShaderGraphPropertyFields(obj, mat);
                    bool hasShaderGraph = !string.IsNullOrWhiteSpace(mat.ShaderGraphPath);
                    if (!hasShaderGraph && SectionHeader("Surface Inputs"))
                    {
                        var baseCol = new System.Numerics.Vector3(mat.R, mat.G, mat.B);
                        FieldRow("Base Color");
                        if (ColorField("##matbase", ref baseCol))
                        {
                            BeginMaterialEdit(obj);
                            mat.R = baseCol.X; mat.G = baseCol.Y; mat.B = baseCol.Z;
                            sceneRenderer.InvalidateStaticBatch();
                            SaveMaterialComponentToAsset(mat);
                        }
                        EndMaterialEdit(obj, mat);
                        DrawTextureMapSlot("Base Map", mat.TexturePath, "Drop texture", path =>
                        {
                            if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                            {
                                CommitSceneMutation("Edit Material Albedo", () =>
                                {
                                    mat.TexturePath = path;
                                    sceneRenderer.InvalidateStaticBatch();
                                    return mat;
                                });
                                SaveMaterialComponentToAsset(mat);
                            }
                        });
                        DrawTextureMapSlot("Normal Map", mat.NormalMapPath, "Drop normal map", path =>
                        {
                            if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                            {
                                if (!string.IsNullOrWhiteSpace(path))
                                {
                                    TextureImportSettingsAsset.EnsureNormalMap(path);
                                    sceneRenderer.InvalidateTexture(path);
                                }

                                CommitSceneMutation("Edit Material Normal", () =>
                                {
                                    mat.NormalMapPath = path;
                                    sceneRenderer.InvalidateStaticBatch();
                                    return mat;
                                });
                                SaveMaterialComponentToAsset(mat);
                            }
                        });
                        DrawTextureMapSlot("Roughness Map", mat.RoughnessMapPath, "Drop roughness map", path =>
                        {
                            if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                            {
                                CommitSceneMutation("Edit Material Roughness Map", () =>
                                {
                                    mat.RoughnessMapPath = path;
                                    sceneRenderer.InvalidateStaticBatch();
                                    return mat;
                                });
                                SaveMaterialComponentToAsset(mat);
                            }
                        });
                        DrawTextureMapSlot("Metallic Map", mat.MetallicMapPath, "Drop metallic map", path =>
                        {
                            if (string.IsNullOrWhiteSpace(path) || MaterialAsset.IsTexturePath(path))
                            {
                                CommitSceneMutation("Edit Material Metallic Map", () =>
                                {
                                    mat.MetallicMapPath = path;
                                    sceneRenderer.InvalidateStaticBatch();
                                    return mat;
                                });
                                SaveMaterialComponentToAsset(mat);
                            }
                        });
                        ImGui.Dummy(new Vector2(0f, 3f));
                    }
                    if (!hasShaderGraph && SectionHeader("Surface"))
                    {
                        float matMetallic = mat.Metallic;
                        if (DrawUnitySliderFloat("Metallic##matmetallic", ref matMetallic, 0f, 1f))
                        {
                            BeginMaterialEdit(obj);
                            mat.Metallic = matMetallic;
                            sceneRenderer.InvalidateStaticBatch();
                            SaveMaterialComponentToAsset(mat);
                        }
                        EndMaterialEdit(obj, mat);
                        float matRough = mat.Roughness;
                        if (DrawUnitySliderFloat("Roughness##matrough", ref matRough, 0f, 1f))
                        {
                            BeginMaterialEdit(obj);
                            mat.Roughness = matRough;
                            sceneRenderer.InvalidateStaticBatch();
                            SaveMaterialComponentToAsset(mat);
                        }
                        EndMaterialEdit(obj, mat);
                        ImGui.Dummy(new Vector2(0f, 3f));
                    }
                    if (!hasShaderGraph && SectionHeader("Emission"))
                    {
                        // HDR Color picker estilo Unity: color + intensidad juntos.
                        var emisCol = new System.Numerics.Vector3(mat.EmissionR, mat.EmissionG, mat.EmissionB);
                        float matEmisInt = mat.EmissionIntensity;
                        FieldRow("Emission (HDR)");
                        if (HdrColorField("##matemis", ref emisCol, ref matEmisInt))
                        {
                            BeginMaterialEdit(obj);
                            mat.EmissionR = emisCol.X; mat.EmissionG = emisCol.Y; mat.EmissionB = emisCol.Z;
                            mat.EmissionIntensity = Math.Max(0f, matEmisInt);
                            sceneRenderer.InvalidateStaticBatch();
                            SaveMaterialComponentToAsset(mat);
                        }
                        EndMaterialEdit(obj, mat);
                        ImGui.Dummy(new Vector2(0f, 3f));
                    }
                    break;
                case Camera cam:
                    DrawFloat("FOV", cam.FOV, v => cam.FOV = v, 0.5f, 1f, 179f);
                    DrawFloat("Near Clip", cam.NearClip, v => cam.NearClip = v, 0.01f, 0.001f, 100f);
                    DrawFloat("Far Clip", cam.FarClip, v => cam.FarClip = v, 1f, 1f, 100000f);
                    DrawCheckRow("Anti-aliasing", cam.AntiAliasing, v => cam.AntiAliasing = v);
                    using (new DisabledScope(!cam.AntiAliasing))
                    {
                        int samples = cam.AntiAliasingSamples;
                        FieldRow("Samples");
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.BeginCombo("##cameraaa", $"{samples}x"))
                        {
                            foreach (int option in new[] { 2, 4, 8 })
                            {
                                bool selectedSample = samples == option;
                                if (ImGui.Selectable($"{option}x", selectedSample))
                                    cam.AntiAliasingSamples = option;
                                if (selectedSample) ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }
                    }
                    DrawCheckRow("Frustum Culling", cam.FrustumCulling, v => cam.FrustumCulling = v);
                    DrawCheckRow("Occlusion Culling", cam.OcclusionCulling, v => cam.OcclusionCulling = v);
                    break;
                case Terrain terrain:
                    FieldRow("Resolution");
                    {
                        int resolution = terrain.Resolution;
                        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
                        if (ImGui.DragInt("##Resolution", ref resolution, 1f, 2, 256))
                            terrain.Resolution = resolution;
                        ImGui.PopStyleVar();
                    }
                    DrawFloat("Size X", terrain.SizeX, v => { terrain.SizeX = v; terrain.Version++; }, 0.1f, 1f, 10000f);
                    DrawFloat("Size Z", terrain.SizeZ, v => { terrain.SizeZ = v; terrain.Version++; }, 0.1f, 1f, 10000f);
                    DrawFloat("Height Scale", terrain.HeightScale, v => { terrain.HeightScale = v; terrain.Version++; }, 0.1f, 0f, 1000f);

                    FieldRow("Edit Mode");
                    {
                        var modeSize = new Vector2(90f, 0f);
                        if (DrawToggleButton("Sculpt", terrainEditMode == TerrainEditMode.Sculpt, modeSize))
                            terrainEditMode = TerrainEditMode.Sculpt;
                        ImGui.SameLine();
                        if (DrawToggleButton("Paint", terrainEditMode == TerrainEditMode.Paint, modeSize))
                            terrainEditMode = TerrainEditMode.Paint;
                    }

                    if (terrainEditMode == TerrainEditMode.Sculpt)
                    {
                        FieldRow("Sculpt Tool");
                        {
                            var brushSize = new Vector2(70f, 0f);
                            if (DrawToggleButton("Raise", terrainBrushTool == TerrainBrushTool.Raise, brushSize))
                                terrainBrushTool = TerrainBrushTool.Raise;
                            ImGui.SameLine();
                            if (DrawToggleButton("Lower", terrainBrushTool == TerrainBrushTool.Lower, brushSize))
                                terrainBrushTool = TerrainBrushTool.Lower;
                            ImGui.SameLine();
                            if (DrawToggleButton("Smooth", terrainBrushTool == TerrainBrushTool.Smooth, brushSize))
                                terrainBrushTool = TerrainBrushTool.Smooth;
                            ImGui.SameLine();
                            if (DrawToggleButton("Flatten", terrainBrushTool == TerrainBrushTool.Flatten, brushSize))
                                terrainBrushTool = TerrainBrushTool.Flatten;
                            ImGui.SameLine();
                            if (DrawToggleButton("Noise", terrainBrushTool == TerrainBrushTool.Noise, brushSize))
                                terrainBrushTool = TerrainBrushTool.Noise;
                            ImGui.SameLine();
                            if (DrawToggleButton("Extrude", terrainBrushTool == TerrainBrushTool.Extrude, brushSize))
                                terrainBrushTool = TerrainBrushTool.Extrude;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            int layerIndex = i;
                            DrawAssetSlot($"Layer {layerIndex + 1}", terrain.LayerTextures[layerIndex], "Drop texture",
                                path => terrain.LayerTextures[layerIndex] = path,
                                path => MaterialAsset.IsTexturePath(path));
                            FieldRow("");
                            if (DrawToggleButton("Paint this layer##layer" + layerIndex, terrainPaintLayer == layerIndex, new Vector2(-1f, 0f)))
                                terrainPaintLayer = layerIndex;
                            DrawFloat($"Tiling {layerIndex + 1}", terrain.LayerTiling[layerIndex],
                                v => terrain.LayerTiling[layerIndex] = v, 0.1f, 0.01f, 100f);
                        }
                    }

                    DrawFloat("Brush Radius", terrainBrushRadius, v => terrainBrushRadius = v, 0.1f, 0.5f, 100f);
                    DrawFloat("Brush Strength", terrainBrushStrength, v => terrainBrushStrength = v, 0.01f, 0.05f, 5f);

                    if (terrainEditMode == TerrainEditMode.Sculpt && terrainBrushTool == TerrainBrushTool.Noise)
                    {
                        FieldRow("");
                        if (ImGui.Button("Randomize Seed", new Vector2(-1f, 0f)))
                            terrainNoiseSeed = Environment.TickCount;
                    }
                    break;
                case MeshRenderer mr:
                    DrawMeshRendererInspector(obj, mr);
                    break;
                case MeshFilter mf:
                    DrawAssetSlot("Mesh", mf.MeshPath, "Drop mesh", path =>
                    {
                        if (string.IsNullOrWhiteSpace(path) || ObjLoader.IsSupportedMesh(path))
                        {
                            mf.MeshPath = path;
                            mf.MaterialSlots.Clear();
                        }
                    }, ObjLoader.IsSupportedMesh);
                    DrawFloat("Import Scale", mf.ImportScale, v => mf.ImportScale = v, 0.01f, 0.001f, 1000f);
                    // Los materiales se editan en el componente Mesh Renderer (como Unity).
                    DrawFbxAnimationInfo(mf.MeshPath);
                    break;
                case DirectionalLight dl:
                    DrawVector3("Direction", dl.Direction, v => dl.Direction = v, 0.05f);
                    DrawLightColor(dl.R, dl.G, dl.B, (r, g, b) => { dl.R = r; dl.G = g; dl.B = b; });
                    DrawFloat("Intensity", dl.Intensity, v => dl.Intensity = v, 0.05f, 0f, 100f);
                    DrawCheckRow("Use Temperature", dl.UseTemperature, v => dl.UseTemperature = v);
                    if (dl.UseTemperature)
                        DrawFloat("Temperature (K)", dl.Temperature, v => dl.Temperature = v, 25f, 1500f, 20000f);
                    DrawFloat("Indirect Multiplier", dl.IndirectMultiplier, v => dl.IndirectMultiplier = v, 0.01f, 0f, 4f);
                    DrawCheckRow("Sun Disk", dl.ShowSunDisk, v => dl.ShowSunDisk = v);
                    if (dl.ShowSunDisk)
                        DrawFloat("Angular Diameter", dl.AngularDiameter, v => dl.AngularDiameter = v, 0.05f, 0.1f, 30f);
                    DrawCheckRow("God Rays", dl.GodRays, v => dl.GodRays = v);
                    if (dl.GodRays)
                        DrawFloat("God Rays Strength", dl.GodRaysStrength, v => dl.GodRaysStrength = v, 0.01f, 0f, 5f);
                    DrawCheckRow("Shadows", dl.Shadows, v => dl.Shadows = v);
                    DrawFloat("Shadow Strength", dl.ShadowStrength, v => dl.ShadowStrength = v, 0.01f, 0f, 1f);
                    break;
                case PointLight pl:
                    DrawLightColor(pl.R, pl.G, pl.B, (r, g, b) => { pl.R = r; pl.G = g; pl.B = b; });
                    DrawFloat("Intensity", pl.Intensity, v => pl.Intensity = v, 0.05f, 0f, 100f);
                    DrawCheckRow("Use Temperature", pl.UseTemperature, v => pl.UseTemperature = v);
                    if (pl.UseTemperature)
                        DrawFloat("Temperature (K)", pl.Temperature, v => pl.Temperature = v, 25f, 1500f, 20000f);
                    DrawFloat("Range", pl.Range, v => pl.Range = v, 0.05f, 0f, 10000f);
                    DrawCheckRow("Shadows", pl.Shadows, v => pl.Shadows = v);
                    DrawFloat("Shadow Strength", pl.ShadowStrength, v => pl.ShadowStrength = v, 0.01f, 0f, 1f);
                    break;
                case SpotLight sl:
                    DrawVector3("Direction", sl.Direction, v => sl.Direction = v, 0.05f);
                    DrawLightColor(sl.R, sl.G, sl.B, (r, g, b) => { sl.R = r; sl.G = g; sl.B = b; });
                    DrawFloat("Intensity", sl.Intensity, v => sl.Intensity = v, 0.05f, 0f, 100f);
                    DrawCheckRow("Use Temperature", sl.UseTemperature, v => sl.UseTemperature = v);
                    if (sl.UseTemperature)
                        DrawFloat("Temperature (K)", sl.Temperature, v => sl.Temperature = v, 25f, 1500f, 20000f);
                    DrawFloat("Range", sl.Range, v => sl.Range = v, 0.05f, 0f, 10000f);
                    DrawFloat("Angle", sl.Angle, v => sl.Angle = v, 0.5f, 1f, 179f);
                    DrawCheckRow("Shadows", sl.Shadows, v => sl.Shadows = v);
                    DrawFloat("Shadow Strength", sl.ShadowStrength, v => sl.ShadowStrength = v, 0.01f, 0f, 1f);
                    break;
                case AmbientLight al:
                    DrawLightColor(al.R, al.G, al.B, (r, g, b) => { al.R = r; al.G = g; al.B = b; });
                    DrawFloat("Intensity", al.Intensity, v => al.Intensity = v, 0.05f, 0f, 100f);
                    DrawFloat("Sky Strength", al.SkyStrength, v => al.SkyStrength = v, 0.05f, 0f, 100f);
                    break;
                case AreaLight area:
                    DrawLightColor(area.R, area.G, area.B, (r, g, b) => { area.R = r; area.G = g; area.B = b; });
                    DrawFloat("Intensity", area.Intensity, v => area.Intensity = v, 0.05f, 0f, 100f);
                    DrawFloat("Range", area.Range, v => area.Range = v, 0.05f, 0f, 10000f);
                    DrawFloat("Size", area.Size, v => area.Size = v, 0.05f, 0f, 10000f);
                    break;
                case RectangleLight rect:
                    DrawLightColor(rect.R, rect.G, rect.B, (r, g, b) => { rect.R = r; rect.G = g; rect.B = b; });
                    DrawFloat("Intensity", rect.Intensity, v => rect.Intensity = v, 0.05f, 0f, 100f);
                    DrawFloat("Range", rect.Range, v => rect.Range = v, 0.05f, 0f, 10000f);
                    DrawFloat("Width", rect.Width, v => rect.Width = v, 0.05f, 0f, 10000f);
                    DrawFloat("Height", rect.Height, v => rect.Height = v, 0.05f, 0f, 10000f);
                    break;
                case PostProcessSettings pp:
                    if (Section("Global"))
                    {
                        DrawCheckRow("Enabled", pp.PostProcessEnabled, v => pp.PostProcessEnabled = v);
                        DrawFloat("Exposure", pp.Exposure, v => pp.Exposure = v, 0.01f, 0f, 100f);
                        DrawFloat("Gamma", pp.Gamma, v => pp.Gamma = v, 0.01f, 0.01f, 10f);
                        DrawCheckRow("Tone Mapping", pp.ToneMapping, v => pp.ToneMapping = v);
                        DrawCheckRow("AgX (en vez de ACES)", pp.ToneMappingMode == 1, v => pp.ToneMappingMode = v ? 1 : 0);
                    }

                    if (Section("White Balance"))
                    {
                        DrawFloat("Temperature", pp.WhiteBalanceTemperature, v => pp.WhiteBalanceTemperature = v, 0.5f, -100f, 100f);
                        DrawFloat("Tint", pp.WhiteBalanceTint, v => pp.WhiteBalanceTint = v, 0.5f, -100f, 100f);
                    }

                    if (Section("Film Grain"))
                    {
                        DrawCheckRow("Active", pp.FilmGrain, v => pp.FilmGrain = v);
                        DrawFloat("Intensity", pp.FilmGrainIntensity, v => pp.FilmGrainIntensity = v, 0.01f, 0f, 1f);
                    }

                    if (Section("Bloom"))
                    {
                        DrawCheckRow("Active", pp.Bloom, v => pp.Bloom = v);
                        DrawFloat("Intensity", pp.BloomStrength, v => pp.BloomStrength = v, 0.01f, 0f, 10f);
                        DrawFloat("Threshold", pp.BloomThreshold, v => pp.BloomThreshold = v, 0.01f, 0f, 32f);
                        DrawFloat("Scatter", pp.BloomScatter, v => pp.BloomScatter = v, 0.01f, 0f, 1f);
                        DrawFloat("Clamp", pp.BloomClamp, v => pp.BloomClamp = v, 1f, 0f, 65472f);
                        DrawParticleColor("Tint", pp.BloomTintR, pp.BloomTintG, pp.BloomTintB, 1f,
                            (r, g, b, _) => { pp.BloomTintR = r; pp.BloomTintG = g; pp.BloomTintB = b; });
                        DrawCheckRow("High Quality", pp.BloomHighQualityFiltering, v => pp.BloomHighQualityFiltering = v);
                    }

                    if (Section("Color Adjustments"))
                    {
                        DrawCheckRow("Active", pp.ColorAdjustments, v => pp.ColorAdjustments = v);
                        DrawFloat("Post Exposure", pp.PostExposure, v => pp.PostExposure = v, 0.01f, -10f, 10f);
                        DrawFloat("Contrast", pp.Contrast, v => pp.Contrast = v, 0.5f, -100f, 100f);
                        DrawParticleColor("Color Filter", pp.ColorFilterR, pp.ColorFilterG, pp.ColorFilterB, 1f,
                            (r, g, b, _) => { pp.ColorFilterR = r; pp.ColorFilterG = g; pp.ColorFilterB = b; });
                        DrawFloat("Hue Shift", pp.HueShift, v => pp.HueShift = v, 0.5f, -180f, 180f);
                        DrawFloat("Saturation", pp.Saturation, v => pp.Saturation = v, 0.5f, -100f, 100f);
                        DrawParticleColor("Lift (sombras)", pp.LiftR, pp.LiftG, pp.LiftB, 1f,
                            (r, g, b, _) => { pp.LiftR = r; pp.LiftG = g; pp.LiftB = b; });
                        DrawParticleColor("Gamma (medios)", pp.GammaR, pp.GammaG, pp.GammaB, 1f,
                            (r, g, b, _) => { pp.GammaR = r; pp.GammaG = g; pp.GammaB = b; });
                        DrawParticleColor("Gain (altas)", pp.GainR, pp.GainG, pp.GainB, 1f,
                            (r, g, b, _) => { pp.GainR = r; pp.GainG = g; pp.GainB = b; });
                    }

                    if (Section("Vignette"))
                    {
                        DrawCheckRow("Active", pp.Vignette, v => pp.Vignette = v);
                        DrawParticleColor("Color", pp.VignetteColorR, pp.VignetteColorG, pp.VignetteColorB, 1f,
                            (r, g, b, _) => { pp.VignetteColorR = r; pp.VignetteColorG = g; pp.VignetteColorB = b; });
                        var center = new System.Numerics.Vector2(pp.VignetteCenterX, pp.VignetteCenterY);
                        FieldRow("Center");
                        if (ImGui.DragFloat2("##VignetteCenter", ref center, 0.005f, 0f, 1f))
                        {
                            pp.VignetteCenterX = Math.Clamp(center.X, 0f, 1f);
                            pp.VignetteCenterY = Math.Clamp(center.Y, 0f, 1f);
                        }
                        DrawFloat("Intensity", pp.VignetteIntensity, v => pp.VignetteIntensity = v, 0.01f, 0f, 1f);
                        DrawFloat("Smoothness", pp.VignetteSmoothness, v => pp.VignetteSmoothness = v, 0.01f, 0.01f, 1f);
                        DrawCheckRow("Rounded", pp.VignetteRounded, v => pp.VignetteRounded = v);
                    }

                    if (Section("Chromatic Aberration"))
                    {
                        DrawCheckRow("Active", pp.ChromaticAberration, v => pp.ChromaticAberration = v);
                        DrawFloat("Intensity", pp.ChromaticAberrationIntensity, v => pp.ChromaticAberrationIntensity = v, 0.01f, 0f, 1f);
                    }

                    if (Section("Environment"))
                    {
                        DrawCheckRow("Ambient Occlusion", pp.AmbientOcclusion, v => pp.AmbientOcclusion = v);
                        DrawFloat("AO Strength", pp.AmbientOcclusionStrength, v => pp.AmbientOcclusionStrength = v, 0.01f, 0f, 1f);
                        DrawCheckRow("Fog", pp.Fog, v => pp.Fog = v);
                        DrawFloat("Fog Density", pp.FogDensity, v => pp.FogDensity = v, 0.001f, 0f, 2f);
                        var fogColor = new System.Numerics.Vector3(pp.FogR, pp.FogG, pp.FogB);
                        FieldRow("Fog Color");
                        if (ColorField("##fogcolor", ref fogColor)) { pp.FogR = fogColor.X; pp.FogG = fogColor.Y; pp.FogB = fogColor.Z; }
                        DrawFloat("Volumetric Light", pp.VolumetricLightStrength, v => pp.VolumetricLightStrength = v, 0.01f, 0f, 10f);
                    }
                    break;
                case Canvas canvas:
                    DrawCanvasInspector(canvas);
                    break;
                case UIImage uiImg:
                {
                    // Inspector réplica del Image de Unity (Canvas Renderer + Image).
                    // El "Rect Transform" se dibuja arriba, en lugar del Transform normal (DrawTransform).
                    if (Section("Canvas Renderer"))
                    {
                    DrawFloat("Alpha", uiImg.Alpha, v => uiImg.Alpha = v, 0.01f, 0f, 1f);
                    DrawCheckRow("Raycast Target", uiImg.RaycastTarget, v => uiImg.RaycastTarget = v);
                    DrawCheckRow("Maskable", uiImg.Maskable, v => uiImg.Maskable = v);
                    DrawCheckRow("Cull Transparent Mesh", uiImg.CullTransparentMesh, v => uiImg.CullTransparentMesh = v);
                    }

                    if (Section("Image"))
                    {
                    DrawAssetSlot("Source Image", uiImg.SpritePath, "None (Sprite)", p => uiImg.SpritePath = p, IsUiSpritePath);
                    DrawParticleColor("Color", uiImg.R, uiImg.G, uiImg.B, uiImg.A, (r, g, b, a) => { uiImg.R = r; uiImg.G = g; uiImg.B = b; uiImg.A = a; });
                    DrawAssetSlot("Material", uiImg.MaterialPath, "None (Material)", p => uiImg.MaterialPath = p, _ => true);
                    if (ImGui.TreeNode("Raycast Padding"))
                    {
                        var pad = new System.Numerics.Vector4(uiImg.RaycastPadLeft, uiImg.RaycastPadBottom, uiImg.RaycastPadRight, uiImg.RaycastPadTop);
                        if (ImGui.DragFloat4("Left / Bottom / Right / Top", ref pad, 1f))
                        { uiImg.RaycastPadLeft = pad.X; uiImg.RaycastPadBottom = pad.Y; uiImg.RaycastPadRight = pad.Z; uiImg.RaycastPadTop = pad.W; }
                        ImGui.TreePop();
                    }
                    DrawFloat("Corner Radius", uiImg.CornerRadius, v => uiImg.CornerRadius = v, 0.25f, 0f, 256f);
                    if (ImGui.TreeNode("Outline"))
                    {
                        DrawFloat("Thickness", uiImg.OutlineThickness, v => uiImg.OutlineThickness = v, 0.25f, 0f, 64f);
                        DrawParticleColor("Color", uiImg.OutlineR, uiImg.OutlineG, uiImg.OutlineB, uiImg.OutlineA, (r, g, b, a) => { uiImg.OutlineR = r; uiImg.OutlineG = g; uiImg.OutlineB = b; uiImg.OutlineA = a; });
                        ImGui.TreePop();
                    }

                    // ── Campos que aparecen al asignar una Source Image (como Unity) ──
                    if (!string.IsNullOrWhiteSpace(uiImg.SpritePath))
                    {
                        DrawComboRow("Image Type", new[] { "Simple", "Sliced", "Tiled", "Filled" }, uiImg.ImageType, v => uiImg.ImageType = v);
                        switch (uiImg.ImageType)
                        {
                            case 0: // Simple
                                DrawCheckRow("Use Sprite Mesh", uiImg.UseSpriteMesh, v => uiImg.UseSpriteMesh = v);
                                DrawCheckRow("Preserve Aspect", uiImg.PreserveAspect, v => uiImg.PreserveAspect = v);
                                break;
                            case 1:
                            case 2: // Sliced / Tiled
                                DrawCheckRow("Fill Center", uiImg.FillCenter, v => uiImg.FillCenter = v);
                                DrawFloat("Pixels Per Unit Multiplier", uiImg.PixelsPerUnitMultiplier, v => uiImg.PixelsPerUnitMultiplier = v, 0.1f, 0.01f, 100f);
                                break;
                            case 3: // Filled
                                DrawComboRow("Fill Method", new[] { "Horizontal", "Vertical", "Radial 90", "Radial 180", "Radial 360" }, uiImg.FillMethod, v => uiImg.FillMethod = v);
                                DrawFloat("Fill Origin", uiImg.FillOrigin, v => uiImg.FillOrigin = (int)v, 1f, 0f, 3f);
                                DrawSliderRow("Fill Amount", uiImg.FillAmount, 0f, 1f, v => uiImg.FillAmount = v);
                                if (uiImg.FillMethod >= 2) DrawCheckRow("Clockwise", uiImg.Clockwise, v => uiImg.Clockwise = v);
                                DrawCheckRow("Preserve Aspect", uiImg.PreserveAspect, v => uiImg.PreserveAspect = v);
                                break;
                        }

                        var spriteInfo = GetUiTextureInfo(uiImg.SpritePath);
                        if (ImGui.Button("Set Native Size", new Vector2(-8f, 0f)) && spriteInfo.W > 0)
                        { uiImg.Width = spriteInfo.W; uiImg.Height = spriteInfo.H; }

                        if (spriteInfo.Tex != 0)
                        {
                            ImGui.Spacing();
                            float pw = Math.Min(96f, spriteInfo.W);
                            float ph = pw * (spriteInfo.H / (float)Math.Max(1, spriteInfo.W));
                            ImGui.Image((IntPtr)spriteInfo.Tex, new Vector2(pw, ph));
                            ImGui.SameLine();
                            ImGui.TextDisabled($"Image Size: {spriteInfo.W}x{spriteInfo.H}");
                        }
                    }
                    }
                    break;
                }
                case UIText uiTxt:
                {
                    if (Section("Canvas Renderer"))
                    {
                    DrawFloat("Alpha", uiTxt.Alpha, v => uiTxt.Alpha = v, 0.01f, 0f, 1f);
                    DrawCheckRow("Raycast Target", uiTxt.RaycastTarget, v => uiTxt.RaycastTarget = v);
                    DrawCheckRow("Maskable", uiTxt.Maskable, v => uiTxt.Maskable = v);
                    }

                    if (Section("Text"))
                    {
                    FieldRow("Text");
                    string textValue = uiTxt.Text ?? "";
                    if (ImGui.InputTextMultiline("##uitext", ref textValue, 2048, new Vector2(-8f, 72f))) uiTxt.Text = textValue;
                    DrawParticleColor("Color", uiTxt.R, uiTxt.G, uiTxt.B, uiTxt.A, (r, g, b, a) => { uiTxt.R = r; uiTxt.G = g; uiTxt.B = b; uiTxt.A = a; });
                    DrawFloat("Font Size", uiTxt.FontSize, v => uiTxt.FontSize = v, 0.5f, 1f, 400f);
                    DrawComboRow("Horizontal Align", new[] { "Left", "Center", "Right" }, uiTxt.Align, v => uiTxt.Align = v);
                    DrawComboRow("Vertical Align", new[] { "Top", "Middle", "Bottom" }, uiTxt.VerticalAlign, v => uiTxt.VerticalAlign = v);
                    DrawCheckRow("Best Fit", uiTxt.BestFit, v => uiTxt.BestFit = v);
                    if (uiTxt.BestFit)
                    {
                        DrawFloat("Min Size", uiTxt.MinFontSize, v => uiTxt.MinFontSize = v, 0.5f, 1f, 400f);
                        DrawFloat("Max Size", uiTxt.MaxFontSize, v => uiTxt.MaxFontSize = v, 0.5f, 1f, 400f);
                    }
                    DrawCheckRow("Rich Text", uiTxt.RichText, v => uiTxt.RichText = v);
                    }
                    break;
                }
                case UIButton uiButton:
                {
                    if (Section("Canvas Renderer"))
                    {
                    DrawFloat("Alpha", uiButton.Alpha, v => uiButton.Alpha = v, 0.01f, 0f, 1f);
                    DrawCheckRow("Raycast Target", uiButton.RaycastTarget, v => uiButton.RaycastTarget = v);
                    DrawCheckRow("Maskable", uiButton.Maskable, v => uiButton.Maskable = v);
                    }

                    if (Section("Button"))
                    {
                    FieldRow("Text");
                    string btnText = uiButton.Text ?? "";
                    if (ImGui.InputText("##uibuttontext", ref btnText, 512)) uiButton.Text = btnText;
                    DrawCheckRow("Interactable", uiButton.Interactable, v => uiButton.Interactable = v);
                    DrawComboRow("Transition", new[] { "None", "Color Tint" }, uiButton.Transition, v => uiButton.Transition = v);
                    DrawTextRightOfLabel("Target Graphic", uiButton.TargetGraphic);
                    DrawTextRightOfLabel("Runtime State", uiButton.IsPressed ? "Pressed" : uiButton.IsHovered ? "Highlighted" : uiButton.WasClicked ? "Clicked" : "Normal");
                    DrawTextRightOfLabel("Click Count", uiButton.ClickCount.ToString());
                    DrawFloat("Font Size", uiButton.FontSize, v => uiButton.FontSize = v, 0.5f, 1f, 400f);
                    DrawFloat("Corner Radius", uiButton.CornerRadius, v => uiButton.CornerRadius = v, 0.25f, 0f, 256f);
                    DrawParticleColor("Normal", uiButton.NormalR, uiButton.NormalG, uiButton.NormalB, uiButton.NormalA, (r, g, b, a) => { uiButton.NormalR = r; uiButton.NormalG = g; uiButton.NormalB = b; uiButton.NormalA = a; });
                    DrawParticleColor("Highlighted", uiButton.HighlightedR, uiButton.HighlightedG, uiButton.HighlightedB, uiButton.HighlightedA, (r, g, b, a) => { uiButton.HighlightedR = r; uiButton.HighlightedG = g; uiButton.HighlightedB = b; uiButton.HighlightedA = a; });
                    DrawParticleColor("Pressed", uiButton.PressedR, uiButton.PressedG, uiButton.PressedB, uiButton.PressedA, (r, g, b, a) => { uiButton.PressedR = r; uiButton.PressedG = g; uiButton.PressedB = b; uiButton.PressedA = a; });
                    DrawParticleColor("Disabled", uiButton.DisabledR, uiButton.DisabledG, uiButton.DisabledB, uiButton.DisabledA, (r, g, b, a) => { uiButton.DisabledR = r; uiButton.DisabledG = g; uiButton.DisabledB = b; uiButton.DisabledA = a; });
                    DrawParticleColor("Text Color", uiButton.TextR, uiButton.TextG, uiButton.TextB, uiButton.TextA, (r, g, b, a) => { uiButton.TextR = r; uiButton.TextG = g; uiButton.TextB = b; uiButton.TextA = a; });
                    if (ImGui.TreeNode("Outline"))
                    {
                        DrawFloat("Thickness", uiButton.OutlineThickness, v => uiButton.OutlineThickness = v, 0.25f, 0f, 64f);
                        DrawParticleColor("Color", uiButton.OutlineR, uiButton.OutlineG, uiButton.OutlineB, uiButton.OutlineA, (r, g, b, a) => { uiButton.OutlineR = r; uiButton.OutlineG = g; uiButton.OutlineB = b; uiButton.OutlineA = a; });
                        ImGui.TreePop();
                    }
                    }
                    break;
                }
                case UIBar uiBar:
                    if (Section("Canvas Renderer"))
                    {
                    DrawFloat("Alpha", uiBar.Alpha, v => uiBar.Alpha = v, 0.01f, 0f, 1f);
                    DrawCheckRow("Raycast Target", uiBar.RaycastTarget, v => uiBar.RaycastTarget = v);
                    DrawCheckRow("Maskable", uiBar.Maskable, v => uiBar.Maskable = v);
                    }

                    if (Section("Health Bar"))
                    {
                    DrawSliderRow("Value", uiBar.Value, 0f, 1f, v => uiBar.Value = v);
                    DrawParticleColor("Fill Color", uiBar.FillR, uiBar.FillG, uiBar.FillB, uiBar.FillA, (r, g, b, a) => { uiBar.FillR = r; uiBar.FillG = g; uiBar.FillB = b; uiBar.FillA = a; });
                    DrawParticleColor("Back Color", uiBar.BackR, uiBar.BackG, uiBar.BackB, uiBar.BackA, (r, g, b, a) => { uiBar.BackR = r; uiBar.BackG = g; uiBar.BackB = b; uiBar.BackA = a; });
                    DrawFloat("Border", uiBar.Border, v => uiBar.Border = v, 0.5f, 0f, 20f);
                    DrawFloat("Corner Radius", uiBar.CornerRadius, v => uiBar.CornerRadius = v, 0.25f, 0f, 256f);
                    DrawCheckRow("Show Value Text", uiBar.ShowValueText, v => uiBar.ShowValueText = v);
                    if (uiBar.ShowValueText)
                        DrawParticleColor("Text Color", uiBar.TextR, uiBar.TextG, uiBar.TextB, uiBar.TextA, (r, g, b, a) => { uiBar.TextR = r; uiBar.TextG = g; uiBar.TextB = b; uiBar.TextA = a; });
                    }
                    break;
                case Animator animator:
                {
                    // ── Layout estilo Unity ──────────────────────────────
                    DrawAssetSlot("Controller", animator.ControllerPath, "None (Animator Controller)",
                        path => { animator.ControllerPath = path; animator.InvalidateCache(); },
                        AnimatorControllerAsset.IsControllerPath);

                    DrawAssetSlot("Avatar", animator.AvatarPath, "None (Avatar)",
                        path => { animator.AvatarPath = path; animator.InvalidateCache(); },
                        AvatarAsset.IsAvatarPath);

                    DrawCheckRow("Apply Root Motion", animator.ApplyRootMotion, v => animator.ApplyRootMotion = v);
                    // "Animate Physics" ya no es un check aparte: es el valor AnimatePhysics de Update Mode.
                    DrawEnumCombo("Update Mode", animator.UpdateMode, v => animator.UpdateMode = v);
                    DrawEnumCombo("Culling Mode", animator.CullingMode, v => animator.CullingMode = v);

                    var controller = animator.GetController();
                    var clip = animator.GetClip();
                    var skeletalClip = clip == null ? animator.CurrentSkeletalClip() : null;
                    var runtimeInfo = animator.GetRuntimeInfo();

                    // ── Caja de información (estilo Unity) ───────────────
                    int clipCount = controller?.ClipCount() ?? (string.IsNullOrWhiteSpace(animator.ClipPath) ? 0 : 1);
                    int kfCount = clip?.Keyframes.Count ?? 0;
                    int boneClipCount = GetInspectorAnimatorBoneClipCount(animator);
                    int curvesCount = Math.Max(kfCount, boneClipCount);

                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 1f));
                    ImGui.BeginChild("##animInfo", new Vector2(0f, 116f), ImGuiChildFlags.None);
                    ImGui.TextDisabled($"  Clips Count: {clipCount + boneClipCount}");
                    ImGui.TextDisabled($"  Runtime: {runtimeInfo.StateName}  {runtimeInfo.MotionType}  {(runtimeInfo.IsPlaying ? "Playing" : "Paused")}");
                    if (!string.IsNullOrWhiteSpace(runtimeInfo.ClipName))
                        ImGui.TextDisabled($"  Clip: {runtimeInfo.ClipName}  Time: {runtimeInfo.Time:0.00}/{runtimeInfo.Length:0.00}  Norm: {runtimeInfo.NormalizedTime:0.00}");
                    ImGui.TextDisabled($"  Speed: {runtimeInfo.EffectiveSpeed:0.###}  Visible: {(runtimeInfo.IsVisible ? "Yes" : "No")}  Blend Motions: {runtimeInfo.BlendChildCount}");
                    ImGui.TextDisabled("  Curves Pos: 0 Quat: 0 Euler: 0 Scale: 0 Muscles: 0 Generic: 0");
                    ImGui.TextDisabled("  PPtr: 0");
                    ImGui.TextDisabled($"  Curves Count: {curvesCount} Constant: 0 (0.0%) Dense: 0 (0.0%) Stream: 0 (100.0%)");
                    ImGui.EndChild();
                    ImGui.PopStyleColor();

                    if (ImGui.TreeNode("Blend Tree Runtime"))
                    {
                        var blendWeights = animator.GetBlendWeights();
                        foreach (var weight in blendWeights.OrderByDescending(w => w.Weight).Take(6))
                        {
                            ImGui.TextDisabled($"{weight.DisplayName}  {weight.Weight:0.00}  X:{weight.PosX:0.##} Y:{weight.PosY:0.##}");
                            ImGui.SameLine();
                            ImGui.ProgressBar(Math.Clamp(weight.Weight, 0f, 1f), new Vector2(-1f, 0f), "");
                        }
                        if (blendWeights.Count == 0)
                            ImGui.TextDisabled("No hay Blend Tree activo.");
                        ImGui.TreePop();
                    }

                    if (Environment.TickCount != int.MinValue)
                        break;

                    // ── Asignación de clip ───────────────────────────────
                    if (controller != null)
                    {
                        // Puente práctico hasta el editor de state machine: el estado por defecto
                        // del controller toma este clip para reproducirse.
                        var defState = controller.GetDefaultState();
                        DrawAssetSlot("Default Clip", defState?.ClipPath ?? "", "Drop .anim or FBX clip", path =>
                        {
                            var st = controller.GetDefaultState();
                            if (st == null)
                            {
                                st = new AnimatorStateData { Name = "State" };
                                controller.States.Add(st);
                                controller.DefaultState = st.Name;
                            }
                            st.ClipPath = path;
                            AnimatorControllerAsset.Save(animator.ControllerPath, controller);
                            animator.InvalidateCache();
                        }, AnimationClipAsset.IsPlayableAnimationPath);
                    }
                    else
                    {
                        DrawAssetSlot("Clip (standalone)", animator.ClipPath, "Drop .anim or FBX clip",
                            path => { animator.ClipPath = path; animator.InvalidateCache(); }, AnimationClipAsset.IsPlayableAnimationPath);
                    }

                    if (!string.IsNullOrWhiteSpace(animator.AvatarPath) && File.Exists(animator.AvatarPath))
                    {
                        var avatar = AvatarAsset.Load(animator.AvatarPath);
                        ImGui.TextDisabled($"Avatar: {avatar.Name}  Bones: {avatar.BoneNames.Count}");
                    }

                    var skeletalClips = animator.GetSkeletalClipList();
                    if (skeletalClips.Count > 0)
                    {
                        string curName = string.IsNullOrWhiteSpace(animator.CurrentClipName)
                            ? skeletalClips[0].Name
                            : animator.CurrentClipName;
                        FieldRow("Preview Clip");
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.BeginCombo("##animSkeletalClip", curName))
                        {
                            foreach (var (name, c) in skeletalClips)
                            {
                                bool sel = string.Equals(name, animator.CurrentClipName, StringComparison.OrdinalIgnoreCase);
                                if (ImGui.Selectable($"{name}  ({c.Duration:0.00}s)", sel))
                                {
                                    // CrossFade suave si está reproduciendo; si no, cambio directo para previsualizar.
                                    if (animator.IsPlaying || isPlaying)
                                        animator.CrossFade(name, animator.CrossFadeDuration);
                                    else
                                    {
                                        animator.CurrentClipName = name;
                                        animator.Time = 0f;
                                        animator.SampleSkeletal(0f);
                                    }
                                }
                            }
                            ImGui.EndCombo();
                        }
                        DrawCheckRow("Loop", animator.Loop, v => animator.Loop = v);
                        DrawFloat("CrossFade (s)", animator.CrossFadeDuration, v => animator.CrossFadeDuration = MathF.Max(0f, v), 0.01f, 0f, 2f);
                    }

                    // ── Reproducción / keyframes ─────────────────────────
                    string clipSavePath = animator.EffectiveClipPath();
                    if (clip != null && !string.IsNullOrWhiteSpace(clipSavePath))
                    {
                        ImGui.Separator();

                        if (ImGui.SmallButton(animator.IsPlaying ? "Pause##anim" : "Play##anim"))
                            animator.IsPlaying = !animator.IsPlaying;
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Stop##anim"))
                        {
                            animator.IsPlaying = false;
                            animator.Time = 0f;
                            animator.Sample(0f);
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled("Edita la timeline en Window → Animation");

                        float length = clip.Keyframes.Count > 0 ? clip.Keyframes[^1].Time : 0f;
                        DrawFloat("Time", animator.Time, v =>
                        {
                            animator.Time = Math.Clamp(v, 0f, Math.Max(0f, length));
                            if (!isPlaying)
                                animator.Sample(animator.Time);
                        }, 0.01f, 0f, Math.Max(0.01f, length));

                        bool loop = clip.Loop;
                        if (SmallCheckbox("Loop##anim", ref loop))
                        {
                            clip.Loop = loop;
                            AnimationClipAsset.Save(clipSavePath, clip);
                        }
                    }
                    else if (skeletalClip != null)
                    {
                        ImGui.Separator();

                        if (ImGui.SmallButton(animator.IsPlaying ? "Pause##animsk" : "Play##animsk"))
                            animator.IsPlaying = !animator.IsPlaying;
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Stop##animsk"))
                        {
                            animator.IsPlaying = false;
                            animator.Time = 0f;
                            animator.SampleSkeletal(0f);
                        }
                        ImGui.SameLine();
                        ImGui.TextDisabled($"{animator.Time:F2}s / {skeletalClip.Duration:F2}s");

                        DrawFloat("Time", animator.Time, v =>
                        {
                            animator.Time = Math.Clamp(v, 0f, Math.Max(0f, skeletalClip.Duration));
                            if (!isPlaying)
                                animator.SampleSkeletal(animator.Time);
                        }, 0.01f, 0f, Math.Max(0.01f, skeletalClip.Duration));
                    }
                    break;
                }
                case GrokoEngine.ParticleSystem ps:
                    DrawParticleSystemInspector(ps);
                    break;
                case MonoBehaviour script:
                    DrawScriptFields(script);
                    break;

                default:
                    // Editor genérico para componentes propios nuevos (UI Layout, Toggle, Slider, etc.).
                    DrawScriptFields(component);
                    break;
            }
        }

        ImGui.PopID();
    }

    


    


    


    


    


    


    


    


    private struct ParticleGradientKeyData
    {
        public float T;
        public float R;
        public float G;
        public float B;
        public float A;

        public ParticleGradientKeyData(float t, float r, float g, float b, float a)
        {
            T = Math.Clamp(t, 0f, 1f);
            R = Math.Clamp(r, 0f, 1f);
            G = Math.Clamp(g, 0f, 1f);
            B = Math.Clamp(b, 0f, 1f);
            A = Math.Clamp(a, 0f, 1f);
        }

        public Vec4 Color => new(R, G, B, A);
    }

    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    


    // Dibuja los campos públicos editables de un script del usuario (MonoBehaviour) por reflexión.
    // gameObject / HasStarted / Physics son propiedades, no campos, así que no aparecen aquí.
    


    


    private static float InspectorLabelWidth()
    {
        float available = ImGui.GetContentRegionAvail().X;
        float ratio = currentDrawingApp?.designerLabelRatio ?? 0.34f;
        return available < 280f
            ? Math.Clamp(available * Math.Max(ratio, 0.32f), 76f, 108f)
            : Math.Clamp(available * Math.Max(ratio, 0.36f), 100f, 164f);
    }

    private static string VisibleLabel(string label)
    {
        int idIndex = label.IndexOf("##", StringComparison.Ordinal);
        return idIndex >= 0 ? label[..idIndex] : label;
    }

    


    


    // Checkbox alineado: nombre a la izquierda, casilla a la derecha (estilo Unity).
    


    // Filas con etiqueta a la IZQUIERDA y control a la derecha (alineado en columnas, estilo Unity).
    


    


    


    


    


    // Cuadro de presets de anclas (estilo Unity): botón + popup con la rejilla 3×3 de puntos
    // y los presets de "stretch". Al elegir uno, fija AnchorMin/Max y Pivot y resetea Pos.
    private static readonly (string Label, float MinX, float MinY, float MaxX, float MaxY, float PivX, float PivY)[] AnchorPresets =
    {
        ("Top Left",   0f,0f, 0f,0f, 0f,0f),   ("Top Center",   0.5f,0f, 0.5f,0f, 0.5f,0f),   ("Top Right",   1f,0f, 1f,0f, 1f,0f),
        ("Middle Left",0f,0.5f,0f,0.5f,0f,0.5f),("Center",       0.5f,0.5f,0.5f,0.5f,0.5f,0.5f),("Middle Right",1f,0.5f,1f,0.5f,1f,0.5f),
        ("Bottom Left",0f,1f, 0f,1f, 0f,1f),   ("Bottom Center",0.5f,1f, 0.5f,1f, 0.5f,1f),   ("Bottom Right",1f,1f, 1f,1f, 1f,1f),
    };

    


    


    private static readonly string[] ColliderPhysicMaterialPresets =
    [
        "Default",
        "Bouncy",
        "Ice",
        "Rubber",
        "Metal",
        "Custom"
    ];

    


    


    


    


    


    


    


    


    // Casilla de referencia estilo Unity: "None (Tipo)" + ícono selector ◉, con drag&drop.
    // Los ScriptableObject se guardan como archivos .asset (JSON) independientes de la escena;
    // no pueden conservar referencias a GameObjects/Components/Transforms vivos (esos solo
    // existen mientras la escena está cargada y no son serializables). Mostramos el campo
    // deshabilitado con una explicación en lugar de permitir asignarlo (evitaba que el motor
    // se cerrara al intentar guardar el asset con esa referencia).
    private static void DrawUnsupportedAssetReferenceField(string fieldName, string typeName)
    {
        ImGui.PushID("##" + fieldName + "_unsupported");
        ImGui.BeginDisabled();
        ImGui.Button($"No soportado en assets ({typeName})", new Vector2(-8f, 0f));
        ImGui.EndDisabled();
        DrawTooltip("Un ScriptableObject es un asset persistente: no puede guardar referencias " +
                    "a objetos de la escena (GameObject/Component/Transform), porque esos solo " +
                    "existen mientras la escena está cargada. Usa este campo en un MonoBehaviour, " +
                    "o referencia el asset desde el script de la escena.");
        ImGui.PopID();
    }

    private void DrawObjectSlot(string id, Type fieldType, string currentDisplay,
        Action<GameObject> onAssign, Action onClear)
    {
        ImGui.PushID(id);
        bool empty = string.IsNullOrEmpty(currentDisplay);
        string text = empty ? $"None ({TipoLegible(fieldType)})" : currentDisplay;
        float avail = ImGui.GetContentRegionAvail().X;
        float iconW = avail > 56f ? 20f : 0f;
        float boxW = Math.Max(34f, avail - iconW - (iconW > 0f ? 4f : 0f));
        string displayText = Ellipsize(text, Math.Max(6, (int)(boxW / 7.5f)));

        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.17f, 0.20f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, empty
            ? new System.Numerics.Vector4(0.52f, 0.55f, 0.60f, 1f)
            : new System.Numerics.Vector4(0.85f, 0.87f, 0.90f, 1f));
        if (ImGui.Button(displayText + "##box", new Vector2(boxW, 22f)))
        {
            objectSlotSearch = string.Empty;
            ImGui.OpenPopup("ObjectPicker");
        }
        ImGui.PopStyleColor(3);

        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_HIERARCHY_OBJECT");
            if (delivered && hierarchyDragObjectId != null)
            {
                var dragged = sceneGraph.FindById(hierarchyDragObjectId);
                if (dragged != null) onAssign(dragged);
                hierarchyDragObjectId = null;
            }

            // También se puede arrastrar un PREFAB desde el panel de Assets (como en Unity):
            // se carga una plantilla vinculada al .prefab (PrefabAssetPath) y se persiste
            // como @@prefab:<ruta> en la escena (ver SceneSerializer).
            bool assetDelivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (assetDelivered && draggingAssetPath != null && File.Exists(draggingAssetPath) &&
                draggingAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var loaded = SceneSerializer.LoadPrefab(draggingAssetPath, physicsEngine, scriptCompiler);
                    loaded.PrefabAssetPath = draggingAssetPath;
                    onAssign(loaded);
                    statusMessage = "Assigned " + Path.GetFileName(draggingAssetPath);
                }
                catch
                {
                    statusMessage = "No se pudo cargar el prefab";
                }
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }

        bool pickClicked = false;
        if (iconW > 0f)
        {
            ImGui.SameLine(0f, 4f);
            pickClicked = ImGui.Button("##pick", new Vector2(iconW, 22f));
            DrawPickerIcon();
            if (pickClicked)
            {
                objectSlotSearch = string.Empty;
                ImGui.OpenPopup("ObjectPicker");
            }
        }

        DrawObjectPickerPopup(fieldType, picked =>
        {
            if (picked == null) onClear();
            else onAssign(picked);
        });

        ImGui.PopID();
    }

    // Slot para campos de tipo ScriptableObject: acepta arrastrar un asset ".asset" compatible desde el panel de proyecto.
    


    // Slot para asignar un ASSET de prefab (".prefab" del proyecto) a un campo GameObject
    // de un ScriptableObject. A diferencia de DrawObjectSlot (que apunta a instancias vivas
    // de la escena), aquí cada vez que se lee el asset se instancia una copia nueva del
    // prefab — es lo que sí se puede persistir de forma segura en un asset independiente.
    private void DrawPrefabAssetSlot(string id, Type fieldType, GameObject? current, Action<GameObject?> onAssign)
    {
        ImGui.PushID(id);
        bool empty = current == null || string.IsNullOrWhiteSpace(current.PrefabAssetPath);
        string text = empty ? $"None ({TipoLegible(fieldType)})" : current!.Name;
        float avail = ImGui.GetContentRegionAvail().X;
        float clearW = empty ? 0f : 20f;
        float boxW = Math.Max(34f, avail - clearW - (clearW > 0f ? 4f : 0f));
        string displayText = Ellipsize(text, Math.Max(6, (int)(boxW / 7.5f)));

        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.10f, 0.10f, 0.11f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(0.17f, 0.20f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, empty
            ? new System.Numerics.Vector4(0.52f, 0.55f, 0.60f, 1f)
            : new System.Numerics.Vector4(0.85f, 0.87f, 0.90f, 1f));
        ImGui.Button(displayText + "##box", new Vector2(boxW, 22f));
        ImGui.PopStyleColor(3);
        bool boxHovered = ImGui.IsItemHovered();

        if (ImGui.BeginDragDropTarget())
        {
            bool delivered = AcceptDragDropOnRelease("GROKO_ASSET");
            if (delivered && draggingAssetPath != null && File.Exists(draggingAssetPath) &&
                draggingAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var loaded = SceneSerializer.LoadPrefab(draggingAssetPath, physicsEngine, scriptCompiler);
                    onAssign(loaded);
                    statusMessage = "Assigned " + Path.GetFileName(draggingAssetPath);
                }
                catch
                {
                    statusMessage = "No se pudo cargar el prefab";
                }
                draggingAssetPath = null;
            }
            ImGui.EndDragDropTarget();
        }

        if (!empty)
        {
            ImGui.SameLine(0f, 4f);
            if (ImGui.Button("x##clear", new Vector2(clearW, 22f)))
                onAssign(null);
        }

        if (boxHovered)
            DrawTooltip(empty
                ? "Arrastra un asset .prefab desde el Proyecto (las instancias de la escena no se pueden guardar en un asset)"
                : current!.PrefabAssetPath ?? "");

        ImGui.PopID();
    }

    private void DrawObjectPickerPopup(Type fieldType, Action<GameObject?> onPick)
    {
        if (!ImGui.BeginPopup("ObjectPicker")) return;
        ImGui.TextDisabled($"Seleccionar {TipoLegible(fieldType)}");
        ImGui.SetNextItemWidth(240f);
        ImGui.InputTextWithHint("##objsearch", "Buscar...", ref objectSlotSearch, 128);
        ImGui.Separator();

        if (ImGui.Selectable("None"))
        {
            onPick(null);
            ImGui.CloseCurrentPopup();
        }

        foreach (var go in CollectCompatibleObjects(objects, fieldType))
        {
            if (!string.IsNullOrEmpty(objectSlotSearch) &&
                go.Name.IndexOf(objectSlotSearch, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (ImGui.Selectable(go.Name + "##" + go.EditorId))
            {
                onPick(go);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndPopup();
    }

    // Dibuja el ícono ◉ (selector de objeto) sobre el último item.
    private static void DrawPickerIcon()
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var center = new System.Numerics.Vector2((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f);
        var dl = ImGui.GetWindowDrawList();
        uint col = ImGui.GetColorU32(new System.Numerics.Vector4(0.60f, 0.63f, 0.67f, 1f));
        dl.AddCircle(center, 5.5f, col, 16, 1.5f);
        dl.AddCircleFilled(center, 2f, col, 12);
    }

    private static string TipoLegible(Type t)
    {
        if (typeof(GameObject).IsAssignableFrom(t)) return "GameObject";
        if (t == typeof(MiMotor.Mathematics.Transform)) return "Transform";
        return t.Name;
    }

    private static string Nicify(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        name = VisibleLabel(name);
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder();
        sb.Append(char.ToUpper(name[0]));
        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && !char.IsUpper(name[i - 1]) && name[i - 1] != ' ') sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static List<GameObject> CollectCompatibleObjects(IEnumerable<GameObject> roots, Type fieldType)
    {
        var result = new List<GameObject>();
        void Walk(IEnumerable<GameObject> list)
        {
            foreach (var o in list)
            {
                if (IsCompatibleObject(o, fieldType)) result.Add(o);
                Walk(o.Children);
            }
        }
        Walk(roots);
        return result;
    }

    private static bool IsCompatibleObject(GameObject o, Type fieldType)
    {
        if (typeof(GameObject).IsAssignableFrom(fieldType)) return true;
        if (fieldType == typeof(MiMotor.Mathematics.Transform)) return true;
        if (typeof(Component).IsAssignableFrom(fieldType))
            return o.Components.Any(c => fieldType.IsInstanceOfType(c));
        return false;
    }

    private string RefTransformName(MiMotor.Mathematics.Transform? t)
    {
        if (t == null) return "";
        var owner = FindObjectByTransform(objects, t);
        return owner != null ? $"{owner.Name} (Transform)" : "Transform";
    }

    private static GameObject? FindObjectByTransform(IEnumerable<GameObject> roots, MiMotor.Mathematics.Transform t)
    {
        foreach (var o in roots)
        {
            if (ReferenceEquals(o.transform, t)) return o;
            var found = FindObjectByTransform(o.Children, t);
            if (found != null) return found;
        }
        return null;
    }

    


    private void RemoveComponent(GameObject obj, Component component)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot remove components while playing";
            return;
        }

        CommitSceneMutation("Remove " + component.GetType().Name, () =>
        {
            if (component is Collider collider) physicsEngine.UnregisterCollider(collider);
            if (component is Camera) obj.IsCamera = false;
            obj.Components.Remove(component);
            selected = obj;
            statusMessage = $"Removed {component.GetType().Name}";
            return true;
        });
    }

    private void MoveComponent(GameObject obj, Component component, int direction)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot move components while playing";
            return;
        }

        int from = obj.Components.IndexOf(component);
        int to = from + direction;
        if (from < 0 || to < 0 || to >= obj.Components.Count)
            return;

        CommitSceneMutation("Move " + component.GetType().Name, () =>
        {
            obj.Components.RemoveAt(from);
            obj.Components.Insert(to, component);
            selected = obj;
            statusMessage = $"Moved {component.GetType().Name}";
            return component;
        });
    }

    private static bool TryGetComponentEnabled(Component component, out bool enabled)
    {
        var property = component.GetType().GetProperty("Enabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property != null && property.PropertyType == typeof(bool) && property.CanRead && property.CanWrite)
        {
            enabled = (bool)property.GetValue(component)!;
            return true;
        }

        enabled = true;
        return false;
    }

    private void SetComponentEnabled(Component component, bool enabled)
    {
        var property = component.GetType().GetProperty("Enabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property == null || property.PropertyType != typeof(bool) || !property.CanWrite)
            return;

        CommitSceneMutation((enabled ? "Enable " : "Disable ") + component.GetType().Name, () =>
        {
            property.SetValue(component, enabled);
            if (component.gameObject != null)
            {
                sceneRenderer.InvalidateStaticBatch();
                sceneRenderer.InvalidateCullingState();
            }
            return component;
        });
    }

    private void ResetComponent(GameObject obj, Component component)
    {
        if (isPlaying)
        {
            statusMessage = "Cannot reset components while playing";
            return;
        }

        CommitSceneMutation("Reset " + component.GetType().Name, () =>
        {
            Component fresh;
            try
            {
                fresh = (Component)Activator.CreateInstance(component.GetType())!;
            }
            catch (Exception ex)
            {
                statusMessage = $"Reset failed for {component.GetType().Name}: {ex.Message}";
                return false;
            }

            CopyEditableComponentState(fresh, component);
            component.gameObject = obj;
            if (component is Rigidbody rb)
                rb.Physics = physicsEngine;
            if (component is Collider)
                physicsEngine.MarkSpatialHashDirty();
            statusMessage = $"Reset {component.GetType().Name}";
            return true;
        });
    }

    private static void CopyEditableComponentState(Component source, Component target)
    {
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        foreach (var field in target.GetType().GetFields(flags))
        {
            if (field.Name is nameof(Component.gameObject) or "Physics")
                continue;
            if (field.IsInitOnly)
                continue;
            try { field.SetValue(target, field.GetValue(source)); } catch { }
        }

        foreach (var prop in target.GetType().GetProperties(flags))
        {
            if (!prop.CanRead || !prop.CanWrite || prop.Name is nameof(Component.gameObject) or "Physics")
                continue;
            if (prop.GetIndexParameters().Length > 0)
                continue;
            try { prop.SetValue(target, prop.GetValue(source)); } catch { }
        }
    }

    private static void CopyComponentToClipboard(Component component)
    {
        var lines = new List<string> { component.GetType().Name };
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        foreach (var field in component.GetType().GetFields(flags))
        {
            if (field.Name is nameof(Component.gameObject) or "Physics")
                continue;
            lines.Add($"{field.Name}: {field.GetValue(component)}");
        }

        foreach (var prop in component.GetType().GetProperties(flags))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0 || prop.Name is nameof(Component.gameObject) or "Physics")
                continue;
            try { lines.Add($"{prop.Name}: {prop.GetValue(component)}"); } catch { }
        }

        ImGui.SetClipboardText(string.Join(Environment.NewLine, lines));
    }

    private void HandleEditorShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput) return;

        bool ctrl = ImGui.IsKeyDown(ImGuiKey.ModCtrl);
        bool shift = ImGui.IsKeyDown(ImGuiKey.ModShift);
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.S))
            SaveScene();
        if (ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.Z))
            UndoScene();
        if ((ctrl && ImGui.IsKeyPressed(ImGuiKey.Y)) || (ctrl && shift && ImGui.IsKeyPressed(ImGuiKey.Z)))
            RedoScene();
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.D))
        {
            if (!TryDuplicateSelectedProjectAnimationSubAsset())
                DuplicateSelected();
        }
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.P))
            TogglePlayMode();
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.Backslash) && selected != null)
        {
            var before = CaptureSceneState();
            ApplyGridSnapToObject(selected);
            NotifyObjectTransformChanged(selected);
            PushSceneState("Align To Grid " + selected.Name, before, CaptureSceneState());
            statusMessage = "Aligned selected object to grid";
        }
        if (ImGui.IsKeyPressed(ImGuiKey.F))
        {
            if (selected != null) FrameObject(selected);
            else FrameScene();
        }
        if (ImGui.IsKeyPressed(ImGuiKey.F2) && selected != null)
            BeginRenameObject(selected);
        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
            DeleteSelected();
    }

    private static void DrawFloat(string label, float value, Action<float> set, float speed, float min, float max)
    {
        FieldRow(label);
        float edit = value;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.12f, 0.12f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new System.Numerics.Vector4(0.17f, 0.17f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new System.Numerics.Vector4(0.18f, 0.18f, 0.19f, 1f));
        if (ImGui.DragFloat("##" + label, ref edit, speed, min, max))
            set(Math.Clamp(edit, min, max));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
    }

    private static bool DrawUnitySliderFloat(string label, ref float value, float min, float max, float speed = 0.01f)
    {
        FieldRow(label);
        ImGui.PushID(label);

        float available = ImGui.GetContentRegionAvail().X;
        float rowH = currentDrawingApp?.guiSliderHeight ?? currentDrawingApp?.designerRowHeight ?? 20f;
        float numericW = available > 128f ? 54f : Math.Clamp(available * 0.36f, 42f, 54f);
        float sliderW = Math.Max(34f, available - numericW - 4f);
        bool changed = false;

        changed |= DrawUnityTrackSlider("##slider", ref value, min, max, sliderW, rowH);
        RegisterGuiElement(GuiStyleClass.Slider, label);

        ImGui.SameLine(0f, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.12f, 0.12f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new System.Numerics.Vector4(0.17f, 0.17f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new System.Numerics.Vector4(0.18f, 0.18f, 0.19f, 1f));
        ImGui.SetNextItemWidth(numericW);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, Math.Max(1f, (rowH - 16f) * 0.5f)));
        changed |= ImGui.DragFloat("##value", ref value, speed, min, max, "%.3f");
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        value = Math.Clamp(value, min, max);
        ImGui.PopID();
        return changed;
    }

    private static bool DrawUnityTrackSlider(string id, ref float value, float min, float max, float width, float height)
    {
        ImGui.PushID(id);
        float controlH = Math.Max(18f, height);
        float safeWidth = Math.Max(24f, width);
        value = Math.Clamp(value, min, max);
        Vector2 minPos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##track", new Vector2(safeWidth, controlH));

        bool changed = false;
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        if ((hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) || active)
        {
            Vector2 mouse = ImGui.GetMousePos();
            float t = Math.Clamp((mouse.X - minPos.X) / safeWidth, 0f, 1f);
            float next = min + (max - min) * t;
            if (Math.Abs(next - value) > float.Epsilon)
            {
                value = next;
                changed = true;
            }
        }

        var drawList = ImGui.GetWindowDrawList();
        float centerY = minPos.Y + controlH * 0.5f;
        float knobRadius = active ? 6.6f : hovered ? 6.2f : 5.8f;
        float valueT = max <= min ? 0f : Math.Clamp((value - min) / (max - min), 0f, 1f);
        Vector2 lineA = new(minPos.X + knobRadius, centerY);
        Vector2 lineB = new(minPos.X + safeWidth - knobRadius, centerY);
        Vector2 knob = new(lineA.X + (lineB.X - lineA.X) * valueT, centerY);

        uint track = ImGui.GetColorU32(new System.Numerics.Vector4(0.36f, 0.36f, 0.36f, 1f));
        uint trackHot = ImGui.GetColorU32(new System.Numerics.Vector4(0.47f, 0.47f, 0.47f, 1f));
        uint knobFill = ImGui.GetColorU32(active
            ? new System.Numerics.Vector4(0.76f, 0.76f, 0.76f, 1f)
            : new System.Numerics.Vector4(0.62f, 0.62f, 0.62f, 1f));
        uint knobBorder = ImGui.GetColorU32(new System.Numerics.Vector4(0.18f, 0.18f, 0.18f, 1f));

        drawList.AddLine(lineA, lineB, hovered || active ? trackHot : track, 2.2f);
        drawList.AddCircleFilled(knob, knobRadius, knobFill, 18);
        drawList.AddCircle(knob, knobRadius, knobBorder, 18, 1f);

        ImGui.PopID();
        return changed;
    }

    private static void DrawVector3(string label, Vector3 value, Action<Vector3> set, float speed)
    {
        float x = value.X, y = value.Y, z = value.Z;
        if (DrawAxisFloat3(label, ref x, ref y, ref z, speed, -100000f, 100000f))
            set(new Vector3(x, y, z));
    }

    private static void DrawFloat2(string label, float a, float b, Action<float, float> set, float speed, float min, float max)
    {
        FieldRow(label);
        var edit = new System.Numerics.Vector2(a, b);
        if (ImGui.DragFloat2("##" + label, ref edit, speed, min, max))
            set(edit.X, edit.Y);
    }

    


    // Cuerpo del "Rect Transform" estilo Unity (común a todos los elementos de UI).
    // El header lo aporta el frame del componente (DrawRectTransformFrame), por eso aquí no hay SeparatorText.
    


    // Inspector del Canvas, fiel a Unity (Rect Transform + Canvas + Canvas Scaler + Graphic Raycaster),
    // con los campos condicionales por Render Mode y por UI Scale Mode.
    


    


    


    private static bool IsUiSpritePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && (
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase));

    


    private static void DrawMiniCurve(float mid, float midValue, string id)
    {
        var size = new Vector2(Math.Max(120f, ImGui.GetContentRegionAvail().X), 34f);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var draw = ImGui.GetWindowDrawList();
        uint bg = ImGui.GetColorU32(new System.Numerics.Vector4(0.09f, 0.10f, 0.11f, 1f));
        uint line = ImGui.GetColorU32(new System.Numerics.Vector4(0.35f, 0.72f, 1f, 1f));
        draw.AddRectFilled(min, max, bg, 3f);
        System.Numerics.Vector2 ToPoint(float t)
        {
            float v = GrokoEngine.ParticleSystem.EvaluateSimpleCurve(t, mid, midValue);
            float y = max.Y - Math.Clamp(v / Math.Max(1f, Math.Abs(midValue)), 0f, 1f) * size.Y;
            return new System.Numerics.Vector2(min.X + t * size.X, y);
        }
        var prev = ToPoint(0f);
        for (int i = 1; i <= 24; i++)
        {
            var p = ToPoint(i / 24f);
            draw.AddLine(prev, p, line, 2f);
            prev = p;
        }
        draw.AddRect(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0f, 0f, 0f, 0.65f)));
        ImGui.PushID(id);
        ImGui.Dummy(size);
        ImGui.PopID();
    }

    


    private static void DrawString(string label, string value, Action<string> set)
    {
        FieldRow(label);
        string edit = value;
        if (ImGui.InputText("##" + label, ref edit, 512))
            set(edit);
    }

    private static void DrawEnumCombo<T>(string label, T value, Action<T> set) where T : struct, Enum
    {
        FieldRow(label);
        int index = Convert.ToInt32(value);
        string[] names = Enum.GetNames<T>();
        if (ImGui.Combo("##" + label, ref index, names, names.Length) && Enum.IsDefined(typeof(T), index))
            set((T)Enum.ToObject(typeof(T), index));
    }

    // Abre un diálogo de archivos nativo para elegir un HDRI (.hdr). Devuelve la
    // ruta elegida o null si se cancela. Corre en el hilo principal (STA), modal.
    


    // Caché de animaciones leídas por modelo (clave = ruta, invalidado por fecha de escritura).
    private readonly Dictionary<string, (DateTime Stamp, List<ModelAnimationInfo> Anims)> fbxAnimCache = new(StringComparer.OrdinalIgnoreCase);

    // Caché SOLO para el Inspector: evita volver a parsear el FBX/OBJ del personaje
    // cada frame cuando el Mesh Renderer está abierto. Seleccionar un personaje con
    // muchas submallas antes disparaba ObjLoader.Load(...) dentro del Draw del Inspector.
    private readonly Dictionary<string, (DateTime Stamp, ParsedMesh Mesh)> inspectorParsedMeshCache = new(StringComparer.OrdinalIgnoreCase);

    private ParsedMesh? GetInspectorParsedMesh(string path)
    {
        string? fullPath = SceneViewportRenderer.NormalizeExistingAssetPath(path);
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            return null;

        DateTime stamp = File.GetLastWriteTimeUtc(fullPath);
        if (inspectorParsedMeshCache.TryGetValue(fullPath, out var cached) && cached.Stamp == stamp)
            return cached.Mesh;

        var mesh = ObjLoader.Load(fullPath);
        if (mesh == null)
            return null;

        if (inspectorParsedMeshCache.Count > 64)
            inspectorParsedMeshCache.Clear();

        inspectorParsedMeshCache[fullPath] = (stamp, mesh);
        return mesh;
    }

    private readonly Dictionary<Animator, (DateTime NextRefreshUtc, int BoneClipCount)> inspectorAnimatorClipCountCache = new();

    private int GetInspectorAnimatorBoneClipCount(Animator animator)
    {
        DateTime now = DateTime.UtcNow;
        if (inspectorAnimatorClipCountCache.TryGetValue(animator, out var cached) && now < cached.NextRefreshUtc)
            return cached.BoneClipCount;

        int count;
        try
        {
            count = animator.GetSkeletalClipList().Count;
        }
        catch
        {
            count = 0;
        }

        if (inspectorAnimatorClipCountCache.Count > 128)
            inspectorAnimatorClipCountCache.Clear();

        inspectorAnimatorClipCountCache[animator] = (now.AddMilliseconds(250), count);
        return count;
    }

    


    // Muestra en el inspector si el modelo (FBX) trae animaciones embebidas.
    


    // Cabecera de sección colapsable estilo Unity (reemplaza a ImGui.SeparatorText en el inspector).
    // Abierta por defecto; ImGui recuerda el estado abierto/cerrado por su ID. Uso: if (Section("X")) { ... }
    private static bool Section(string label) =>
        ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen);

    // Añade un Mesh Renderer a los objetos renderizables (con MeshFilter o primitivas) que no lo tengan,
    // para que TODOS los objetos lo muestren como en Unity. Idempotente.
    


    // Inspector del Mesh Renderer, replicando el de Unity (Materials + Lighting + Ray Tracing + Additional).
    // "Materials" es funcional (edita MaterialSlots del MeshFilter o el componente Material); el resto de
    // secciones son de presentación (las sombras del motor son globales por ahora).
    


    // Asegura que cada sub-malla del modelo importado tenga un .mat asignado en MaterialSlots,
    // creando uno automáticamente (a partir del color/textura embebidos en el FBX/OBJ) si falta.
    


}
