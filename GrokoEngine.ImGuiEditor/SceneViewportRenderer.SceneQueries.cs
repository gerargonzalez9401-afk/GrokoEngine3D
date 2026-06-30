using GrokoEngine;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private static Vector3[] GetCameraFrustumCorners(
        ImGuiEditorApp.EditorCameraState camera,
        Matrix4 view,
        float aspect,
        float nearClip,
        float farClip)
    {
        Matrix4 projection = camera.Orthographic
            ? Matrix4.CreateOrthographic(Math.Max(0.01f, camera.OrthoSize) * 2f * aspect, Math.Max(0.01f, camera.OrthoSize) * 2f, nearClip, farClip)
            : Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(camera.FOV), aspect, nearClip, farClip);
        Matrix4.Invert(view * projection, out var invViewProj);

        var corners = new Vector3[8];
        int idx = 0;
        for (int z = 0; z < 2; z++)
        {
            float cz = z == 0 ? -1f : 1f;
            for (int y = 0; y < 2; y++)
            {
                float cy = y == 0 ? -1f : 1f;
                for (int x = 0; x < 2; x++)
                {
                    float cx = x == 0 ? -1f : 1f;
                    var world = Vector4.TransformRow(new Vector4(cx, cy, cz, 1f), invViewProj);
                    corners[idx++] = new Vector3(world.X, world.Y, world.Z) / MathF.Max(world.W, 0.0001f);
                }
            }
        }

        return corners;
    }

    // Mismos criterios que BuildObjectRecursive/DrawDepthGeometry para decidir si un objeto
    // aporta geometría real al render y al shadow map.
    private (Vector3 Center, float Radius) EstimateObjectBound(GameObject obj)
    {
        var center = ComputeWorldPosition(obj);
        float radius;
        if (obj.GetComponent<MeshFilter>() is { } mf && !string.IsNullOrWhiteSpace(mf.MeshPath) && GetParsedMesh(mf.MeshPath) is { } mesh)
        {
            float s = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
            var min = new Vector3(mesh.BoundsMin.X * s, mesh.BoundsMin.Y * s, mesh.BoundsMin.Z * s);
            var max = new Vector3(mesh.BoundsMax.X * s, mesh.BoundsMax.Y * s, mesh.BoundsMax.Z * s);
            radius = (max - min).Length * 0.5f * MathF.Max(MathF.Abs(obj.ScaleX), MathF.Max(MathF.Abs(obj.ScaleY), MathF.Abs(obj.ScaleZ)));
        }
        else if (obj.Type == 2)
        {
            radius = MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleZ * obj.ScaleZ) * 0.75f;
        }
        else if (obj.Type is 3 or 4 or 5 or 6)
        {
            radius = MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleY * obj.ScaleY + obj.ScaleZ * obj.ScaleZ) * (obj.Type == 6 ? 0.85f : 0.65f);
        }
        else
        {
            radius = MathF.Sqrt(obj.ScaleX * obj.ScaleX + obj.ScaleY * obj.ScaleY + obj.ScaleZ * obj.ScaleZ) * 0.75f;
        }
        return (center, MathF.Max(radius, 0.5f));
    }

    private static T? FindComponent<T>(IReadOnlyList<GameObject> objects) where T : Component
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;
            var c = obj.GetComponent<T>();
            if (c != null && c.Enabled) return c;
            var found = FindComponent<T>(obj.Children);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectComponents<T>(IReadOnlyList<GameObject> objects, List<T> result, int max)
        where T : Component
    {
        foreach (var obj in objects)
        {
            if (result.Count >= max) return;
            if (!obj.IsActive) continue;
            var c = obj.GetComponent<T>();
            if (c != null && c.Enabled) result.Add(c);
            CollectComponents(obj.Children, result, max);
        }
    }

    private void TrimLightsByImportance<T>(
        List<T> lights,
        int max,
        Vector3 cameraPosition,
        Func<T, float> intensity,
        Func<T, float> range)
        where T : Component
    {
        if (max <= 0)
        {
            lights.Clear();
            return;
        }

        if (lights.Count <= max)
            return;

        lights.Sort((a, b) => LightImportance(b, cameraPosition, intensity(b), range(b))
            .CompareTo(LightImportance(a, cameraPosition, intensity(a), range(a))));
        lights.RemoveRange(max, lights.Count - max);
    }

    private float LightImportance(Component light, Vector3 cameraPosition, float intensity, float range)
    {
        Vector3 position = light.gameObject != null ? ComputeWorldPosition(light.gameObject) : cameraPosition;
        float distance = (position - cameraPosition).Length;
        float safeRange = MathF.Max(range, 0.01f);
        float distanceWeight = 1f / (1f + (distance / safeRange) * (distance / safeRange));
        return MathF.Max(0f, intensity) * MathF.Max(0.01f, safeRange) * distanceWeight;
    }

    private static T? FindShadowCaster<T>(IReadOnlyList<GameObject> objects, Func<T, bool> predicate)
        where T : Component
    {
        foreach (var obj in objects)
        {
            if (!obj.IsActive) continue;
            if (obj.GetComponent<T>() is { Enabled: true } component && predicate(component))
                return component;
            var found = FindShadowCaster(obj.Children, predicate);
            if (found != null) return found;
        }
        return null;
    }

    private static Vector3 ToTk(MiMotor.Mathematics.Vector3 value) =>
        new(value.X, value.Y, value.Z);
}
