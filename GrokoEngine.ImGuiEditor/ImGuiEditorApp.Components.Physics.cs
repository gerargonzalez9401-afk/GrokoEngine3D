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
private void AddDefaultPrimitiveCollider(GameObject obj)
    {
        switch (obj.Type)
        {
            case 3:
                obj.AddComponentWithEngine<SphereCollider>(physicsEngine).Radius = 0.5f;
                break;
            case 6:
                var capsule = obj.AddComponentWithEngine<CapsuleCollider>(physicsEngine);
                capsule.Radius = 0.5f;
                capsule.Height = 2f;
                capsule.Axis = CapsuleAxis.Y;
                break;
            default:
                var box = obj.AddComponentWithEngine<BoxCollider>(physicsEngine);
                box.Size = obj.Type switch
                {
                    4 => new Vector3(1f, 2f, 1f),
                    5 => new Vector3(1f, 0.02f, 1f),
                    _ => new Vector3(1f, 1f, 1f)
                };
                break;
        }
    }

private CharacterController AddCharacterController(GameObject obj)
    {
        var controller = obj.AddComponentWithEngine<CharacterController>(physicsEngine);
        controller.EnsureCollider();
        physicsEngine.MarkSpatialHashDirty();
        return controller;
    }

private void DrawBoxColliderInspector(BoxCollider box)
    {
        if (ImGui.Button("Edit Collider", new Vector2(-1f, 24f)))
        {
            selected = box.gameObject;
            colliderEditObjectId = colliderEditObjectId == box.gameObject.EditorId ? null : box.gameObject.EditorId;
            colliderEditDragging = false;
            colliderEditHandle = -1;
            viewportGizmosVisible = true;
            statusMessage = colliderEditObjectId != null ? "Edit Collider enabled" : "Edit Collider disabled";
        }

        if (ImGui.Button("Auto Fit", new Vector2(-1f, 22f)))
            FitBoxColliderToObject(box);

        ImGui.Spacing();

        DrawCheckRow("Is Trigger", box.IsTrigger, v =>
        {
            box.IsTrigger = v;
            physicsEngine.MarkSpatialHashDirty();
        });

        DrawColliderPhysicMaterial(box);

        DrawVector3("Center", box.Center, v =>
        {
            box.Center = v;
            physicsEngine.MarkSpatialHashDirty();
        }, 0.05f);

        DrawVector3("Size", box.Size, v =>
        {
            box.Size = new Vector3(Math.Max(0.0001f, Math.Abs(v.X)), Math.Max(0.0001f, Math.Abs(v.Y)), Math.Max(0.0001f, Math.Abs(v.Z)));
            physicsEngine.MarkSpatialHashDirty();
        }, 0.05f);

        DrawFloat("Friction", box.Friction, v =>
        {
            box.Friction = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(box, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);

        DrawFloat("Bounciness", box.Bounciness, v =>
        {
            box.Bounciness = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(box, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);

        ImGui.Spacing();
        if (ImGui.Button("Reset Box Collider", new Vector2(-1f, 22f)))
        {
            box.Center = Vector3.Zero;
            box.Size = new Vector3(1f, 1f, 1f);
            box.IsTrigger = false;
            ApplyColliderMaterialPreset(box, "Default");
            physicsEngine.MarkSpatialHashDirty();
        }
    }

private void FitBoxColliderToObject(BoxCollider box)
    {
        var obj = box.gameObject;
        if (obj.GetComponent<MeshFilter>() is { } mf &&
            !string.IsNullOrWhiteSpace(mf.MeshPath) &&
            ObjLoader.Load(mf.MeshPath) is { } mesh)
        {
            float scale = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
            var min = new Vector3(mesh.BoundsMin.X * scale, mesh.BoundsMin.Y * scale, mesh.BoundsMin.Z * scale);
            var max = new Vector3(mesh.BoundsMax.X * scale, mesh.BoundsMax.Y * scale, mesh.BoundsMax.Z * scale);
            box.Center = (min + max) * 0.5f;
            var size = max - min;
            box.Size = new Vector3(Math.Max(0.0001f, Math.Abs(size.X)), Math.Max(0.0001f, Math.Abs(size.Y)), Math.Max(0.0001f, Math.Abs(size.Z)));
            statusMessage = "Box Collider fitted to mesh bounds";
        }
        else
        {
            box.Center = Vector3.Zero;
            box.Size = new Vector3(1f, 1f, 1f);
            statusMessage = "Box Collider fitted to default unit box";
        }

        physicsEngine.MarkSpatialHashDirty();
    }

private void DrawSphereColliderInspector(SphereCollider sphere)
    {
        if (ImGui.Button("Edit Collider", new Vector2(-1f, 24f)))
        {
            selected = sphere.gameObject;
            colliderEditObjectId = colliderEditObjectId == sphere.gameObject.EditorId ? null : sphere.gameObject.EditorId;
            colliderEditDragging = false;
            colliderEditHandle = -1;
            viewportGizmosVisible = true;
            statusMessage = colliderEditObjectId != null ? "Edit Sphere Collider enabled" : "Edit Sphere Collider disabled";
        }

        ImGui.Spacing();

        DrawCheckRow("Is Trigger", sphere.IsTrigger, v =>
        {
            sphere.IsTrigger = v;
            physicsEngine.MarkSpatialHashDirty();
        });

        DrawColliderPhysicMaterial(sphere);

        DrawVector3("Center", sphere.Center, v =>
        {
            sphere.Center = v;
            physicsEngine.MarkSpatialHashDirty();
        }, 0.05f);

        DrawFloat("Radius", sphere.Radius, v =>
        {
            sphere.Radius = Math.Max(0.0001f, Math.Abs(v));
            physicsEngine.MarkSpatialHashDirty();
        }, 0.01f, 0.0001f, 10000f);

        DrawFloat("Friction", sphere.Friction, v =>
        {
            sphere.Friction = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(sphere, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);

        DrawFloat("Bounciness", sphere.Bounciness, v =>
        {
            sphere.Bounciness = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(sphere, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);
    }

private void DrawCapsuleColliderInspector(CapsuleCollider capsule)
    {
        if (ImGui.Button("Edit Collider", new Vector2(-1f, 24f)))
        {
            selected = capsule.gameObject;
            colliderEditObjectId = colliderEditObjectId == capsule.gameObject.EditorId ? null : capsule.gameObject.EditorId;
            colliderEditDragging = false;
            colliderEditHandle = -1;
            viewportGizmosVisible = true;
            statusMessage = colliderEditObjectId != null ? "Edit Capsule Collider enabled" : "Edit Capsule Collider disabled";
        }

        ImGui.Spacing();

        DrawCheckRow("Is Trigger", capsule.IsTrigger, v =>
        {
            capsule.IsTrigger = v;
            physicsEngine.MarkSpatialHashDirty();
        });

        DrawColliderPhysicMaterial(capsule);

        DrawVector3("Center", capsule.Center, v =>
        {
            capsule.Center = v;
            physicsEngine.MarkSpatialHashDirty();
        }, 0.05f);

        DrawFloat("Radius", capsule.Radius, v =>
        {
            capsule.Radius = Math.Max(0.0001f, Math.Abs(v));
            physicsEngine.MarkSpatialHashDirty();
        }, 0.01f, 0.0001f, 10000f);

        DrawFloat("Height", capsule.Height, v =>
        {
            capsule.Height = Math.Max(capsule.Radius * 2f, Math.Abs(v));
            physicsEngine.MarkSpatialHashDirty();
        }, 0.05f, 0.0001f, 10000f);

        DrawEnumCombo("Axis", capsule.Axis, v =>
        {
            capsule.Axis = v;
            physicsEngine.MarkSpatialHashDirty();
        });

        DrawFloat("Friction", capsule.Friction, v =>
        {
            capsule.Friction = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(capsule, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);

        DrawFloat("Bounciness", capsule.Bounciness, v =>
        {
            capsule.Bounciness = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(capsule, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);
    }

private void DrawMeshColliderInspector(MeshCollider meshCollider)
    {
        DrawCheckRow("Is Trigger", meshCollider.IsTrigger, v =>
        {
            meshCollider.IsTrigger = v;
            physicsEngine.MarkSpatialHashDirty();
        });

        DrawCheckRow("Use Mesh Bounds", meshCollider.UseMeshBounds, v =>
        {
            meshCollider.UseMeshBounds = v;
            physicsEngine.MarkSpatialHashDirty();
        });

        DrawColliderPhysicMaterial(meshCollider);

        DrawVector3("Center", meshCollider.Center, v =>
        {
            meshCollider.Center = v;
            physicsEngine.MarkSpatialHashDirty();
        }, 0.05f);

        using (new DisabledScope(meshCollider.UseMeshBounds))
        {
            DrawVector3("Fallback Size", meshCollider.Size, v =>
            {
                meshCollider.Size = new Vector3(Math.Max(0.0001f, Math.Abs(v.X)), Math.Max(0.0001f, Math.Abs(v.Y)), Math.Max(0.0001f, Math.Abs(v.Z)));
                physicsEngine.MarkSpatialHashDirty();
            }, 0.05f);
        }

        DrawFloat("Friction", meshCollider.Friction, v =>
        {
            meshCollider.Friction = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(meshCollider, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);

        DrawFloat("Bounciness", meshCollider.Bounciness, v =>
        {
            meshCollider.Bounciness = Math.Clamp(v, 0f, 1f);
            ApplyColliderMaterialPreset(meshCollider, "Custom", preserveValues: true);
        }, 0.01f, 0f, 1f);
    }

private void DrawColliderPhysicMaterial(Collider box)
    {
        FieldRow("Material");
        int index = Array.FindIndex(ColliderPhysicMaterialPresets, p => p.Equals(box.PhysicMaterial, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = ColliderPhysicMaterialPresets.Length - 1;

        if (ImGui.Combo("##ColliderMaterial", ref index, ColliderPhysicMaterialPresets, ColliderPhysicMaterialPresets.Length))
        {
            ApplyColliderMaterialPreset(box, ColliderPhysicMaterialPresets[index]);
            physicsEngine.MarkSpatialHashDirty();
        }
    }

private static void ApplyColliderMaterialPreset(Collider box, string preset, bool preserveValues = false)
    {
        box.PhysicMaterial = preset;
        if (preserveValues)
            return;

        switch (preset)
        {
            case "Custom":
                box.PhysicMaterial = "Custom";
                break;
            case "Bouncy":
                box.Friction = 0.25f;
                box.Bounciness = 0.85f;
                break;
            case "Ice":
                box.Friction = 0.02f;
                box.Bounciness = 0f;
                break;
            case "Rubber":
                box.Friction = 0.85f;
                box.Bounciness = 0.65f;
                break;
            case "Metal":
                box.Friction = 0.35f;
                box.Bounciness = 0.05f;
                break;
            default:
                box.PhysicMaterial = "Default";
                box.Friction = 0.5f;
                box.Bounciness = 0f;
                break;
        }
    }

private static bool SmallCheckbox(string label, ref bool value)
    {
        string visible = label;
        int idIndex = label.IndexOf("##", StringComparison.Ordinal);
        if (idIndex >= 0)
            visible = label[..idIndex];

        ImGui.PushID(label);
        ImGui.AlignTextToFramePadding();
        var start = ImGui.GetCursorScreenPos();
        float size = currentDrawingApp?.guiCheckboxSize ?? 12f;
        float y = start.Y + Math.Max(0f, (ImGui.GetFrameHeight() - size) * 0.5f);
        ImGui.SetCursorScreenPos(new System.Numerics.Vector2(start.X, y));

        bool changed = false;
        if (ImGui.InvisibleButton("##smallCheckbox", new Vector2(size, size)))
        {
            value = !value;
            changed = true;
        }
        RegisterGuiElement(GuiStyleClass.Checkbox, label);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(value
            ? new System.Numerics.Vector4(0.23f, 0.23f, 0.23f, 1f)
            : new System.Numerics.Vector4(0.12f, 0.12f, 0.12f, 1f)), 1f);
        drawList.AddRect(min, max, ImGui.GetColorU32(ImGui.IsItemHovered()
            ? new System.Numerics.Vector4(0.58f, 0.58f, 0.58f, 1f)
            : new System.Numerics.Vector4(0.05f, 0.05f, 0.05f, 1f)), 1f);

        if (value)
        {
            uint check = ImGui.GetColorU32(new System.Numerics.Vector4(0.78f, 0.78f, 0.78f, 1f));
            drawList.AddLine(min + new Vector2(size * 0.20f, size * 0.52f), min + new Vector2(size * 0.42f, size * 0.74f), check, 1.4f);
            drawList.AddLine(min + new Vector2(size * 0.42f, size * 0.74f), min + new Vector2(size * 0.82f, size * 0.24f), check, 1.4f);
        }

        ImGui.SetCursorScreenPos(new System.Numerics.Vector2(start.X + size, start.Y));
        if (!string.IsNullOrWhiteSpace(visible))
        {
            ImGui.SameLine(0f, 6f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(visible);
        }

        ImGui.PopID();
        return changed;
    }
}
