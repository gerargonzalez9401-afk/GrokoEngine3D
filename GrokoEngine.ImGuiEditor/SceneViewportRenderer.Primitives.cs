using GrokoEngine;
using OpenTK.Mathematics;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private void BuildCube(GameObject obj, bool selected, Matrix4 transform)
    {
        Vector4 color = GetObjectColor(obj, new Vector4(0.72f, 0.76f, 0.82f, 1f));
        Vector4 lineColor = selected ? new Vector4(1f, 0.78f, 0.12f, 1f) : color;
        string? texturePath = GetObjectTexturePath(obj);
        var (material, emission) = GetObjectSurface(obj);
        var maps = GetObjectSurfaceMaps(obj);

        Span<Vector3> corners = stackalloc Vector3[]
        {
            new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f),
            new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
            new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f),
            new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)
        };

        for (int i = 0; i < corners.Length; i++)
            corners[i] = Vector3.TransformPosition(corners[i], transform);

        Vector3 RN(Vector3 n) => Vector3.TransformNormal(n, transform).Normalized();

        int cubeStart = ActiveSolidVertices.Count;
        AddQuad(corners[4], corners[5], corners[6], corners[7], RN(new Vector3(0f, 0f, 1f)), color, material, emission);
        AddQuad(corners[1], corners[0], corners[3], corners[2], RN(new Vector3(0f, 0f, -1f)), color, material, emission);
        AddQuad(corners[3], corners[7], corners[6], corners[2], RN(new Vector3(0f, 1f, 0f)), color, material, emission);
        AddQuad(corners[0], corners[1], corners[5], corners[4], RN(new Vector3(0f, -1f, 0f)), color, material, emission);
        AddQuad(corners[1], corners[2], corners[6], corners[5], RN(new Vector3(1f, 0f, 0f)), color, material, emission);
        AddQuad(corners[0], corners[4], corners[7], corners[3], RN(new Vector3(-1f, 0f, 0f)), color, material, emission);
        AddSolidRange(cubeStart, texturePath, maps.NormalMapPath, maps.RoughnessMapPath, maps.MetallicMapPath, GetObjectShaderGraphPath(obj), GetObjectShaderGraphProperties(obj), GetObjectShaderGraphTextures(obj));

        if (ShowObjectWireframes || selected)
        {
            AddLine(corners[0], corners[1], lineColor); AddLine(corners[1], corners[2], lineColor);
            AddLine(corners[2], corners[3], lineColor); AddLine(corners[3], corners[0], lineColor);
            AddLine(corners[4], corners[5], lineColor); AddLine(corners[5], corners[6], lineColor);
            AddLine(corners[6], corners[7], lineColor); AddLine(corners[7], corners[4], lineColor);
            AddLine(corners[0], corners[4], lineColor); AddLine(corners[1], corners[5], lineColor);
            AddLine(corners[2], corners[6], lineColor); AddLine(corners[3], corners[7], lineColor);
        }
    }

    private void BuildPlane(GameObject obj, bool selected, Matrix4 transform)
    {
        Vector4 color = GetObjectColor(obj, new Vector4(0.58f, 0.72f, 0.64f, 1f));
        Vector4 lineColor = selected ? new Vector4(1f, 0.78f, 0.12f, 1f) : color;
        string? texturePath = GetObjectTexturePath(obj);
        var (material, emission) = GetObjectSurface(obj);
        var maps = GetObjectSurfaceMaps(obj);

        var a = Vector3.TransformPosition(new Vector3(-0.5f, 0f, -0.5f), transform);
        var b = Vector3.TransformPosition(new Vector3(0.5f, 0f, -0.5f), transform);
        var c = Vector3.TransformPosition(new Vector3(0.5f, 0f, 0.5f), transform);
        var d = Vector3.TransformPosition(new Vector3(-0.5f, 0f, 0.5f), transform);
        var center = Vector3.TransformPosition(Vector3.Zero, transform);
        var normal = Vector3.TransformNormal(new Vector3(0f, 1f, 0f), transform).Normalized();
        AddSolidRange(texturePath, maps.NormalMapPath, maps.RoughnessMapPath, maps.MetallicMapPath, GetObjectShaderGraphPath(obj), GetObjectShaderGraphProperties(obj), GetObjectShaderGraphTextures(obj), () => AddQuad(a, d, c, b, normal, color, material, emission));
        if (ShowObjectWireframes || selected)
        {
            AddLine(a, b, lineColor);
            AddLine(b, c, lineColor);
            AddLine(c, d, lineColor);
            AddLine(d, a, lineColor);
            AddLine(Vector3.TransformPosition(new Vector3(-0.5f, 0f, 0f), transform), Vector3.TransformPosition(new Vector3(0.5f, 0f, 0f), transform), lineColor);
            AddLine(Vector3.TransformPosition(new Vector3(0f, 0f, -0.5f), transform), Vector3.TransformPosition(new Vector3(0f, 0f, 0.5f), transform), lineColor);
        }
    }

    private void BuildPrimitive(GameObject obj, bool selected, Matrix4 transform)
    {
        Vector4 color = GetObjectColor(obj, obj.Type switch
        {
            3 => new Vector4(0.62f, 0.72f, 0.95f, 1f),
            4 => new Vector4(0.72f, 0.68f, 0.86f, 1f),
            5 => new Vector4(0.62f, 0.80f, 0.72f, 1f),
            6 => new Vector4(0.76f, 0.70f, 0.58f, 1f),
            _ => new Vector4(0.72f, 0.76f, 0.82f, 1f)
        });
        Vector4 lineColor = selected ? new Vector4(1f, 0.78f, 0.12f, 1f) : color;
        string? texturePath = GetObjectTexturePath(obj);
        var (material, emission) = GetObjectSurface(obj);
        var maps = GetObjectSurfaceMaps(obj);

        AddSolidRange(texturePath, maps.NormalMapPath, maps.RoughnessMapPath, maps.MetallicMapPath, GetObjectShaderGraphPath(obj), GetObjectShaderGraphProperties(obj), GetObjectShaderGraphTextures(obj), () =>
        {
            switch (obj.Type)
            {
                case 3:
                    AddSpherePrimitive(transform, color, material, emission, 48, 24);
                    break;
                case 4:
                    AddCylinderPrimitive(transform, color, material, emission, 48);
                    break;
                case 5:
                    AddQuadPrimitive(transform, color, material, emission);
                    break;
                case 6:
                    AddCapsulePrimitive(transform, color, material, emission, 48, 12);
                    break;
            }
        });

        if (ShowObjectWireframes || selected)
            DrawPrimitiveWire(obj, transform, lineColor);
    }

    private void AddSpherePrimitive(Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission, int slices, int rings)
    {
        for (int y = 0; y < rings; y++)
        {
            float v0 = y / (float)rings;
            float v1 = (y + 1) / (float)rings;
            float phi0 = -MathF.PI * 0.5f + v0 * MathF.PI;
            float phi1 = -MathF.PI * 0.5f + v1 * MathF.PI;
            for (int x = 0; x < slices; x++)
            {
                float u0 = x / (float)slices;
                float u1 = (x + 1) / (float)slices;
                var p00 = SpherePoint(u0, phi0);
                var p10 = SpherePoint(u1, phi0);
                var p11 = SpherePoint(u1, phi1);
                var p01 = SpherePoint(u0, phi1);
                AddTrianglePrimitiveSmooth(p00, p10, p11, SphereNormal(p00), SphereNormal(p10), SphereNormal(p11), transform, color, material, emission, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1));
                AddTrianglePrimitiveSmooth(p00, p11, p01, SphereNormal(p00), SphereNormal(p11), SphereNormal(p01), transform, color, material, emission, new Vector2(u0, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        }
    }

    private static Vector3 SpherePoint(float u, float phi)
    {
        float theta = u * MathF.PI * 2f;
        float cp = MathF.Cos(phi);
        return new Vector3(MathF.Cos(theta) * cp * 0.5f, MathF.Sin(phi) * 0.5f, MathF.Sin(theta) * cp * 0.5f);
    }

    private static Vector3 SphereNormal(Vector3 p) => p.Length > 0.0001f ? p.Normalized() : Vector3.UnitY;

    private void AddCylinderPrimitive(Matrix4 transform, Vector4 color, Vector4 material, Vector4 emission, int slices)
    {
        for (int i = 0; i < slices; i++)
        {
            float u0 = i / (float)slices;
            float u1 = (i + 1) / (float)slices;
            float a0 = u0 * MathF.PI * 2f;
            float a1 = u1 * MathF.PI * 2f;
            var b0 = new Vector3(MathF.Cos(a0) * 0.5f, -1f, MathF.Sin(a0) * 0.5f);
            var b1 = new Vector3(MathF.Cos(a1) * 0.5f, -1f, MathF.Sin(a1) * 0.5f);
            var t0 = new Vector3(b0.X, 1f, b0.Z);
            var t1 = new Vector3(b1.X, 1f, b1.Z);
            var n0 = new Vector3(MathF.Cos(a0), 0f, MathF.Sin(a0)).Normalized();
            var n1 = new Vector3(MathF.Cos(a1), 0f, MathF.Sin(a1)).Normalized();
            AddTrianglePrimitiveSmooth(b0, b1, t1, n0, n1, n1, transform, color, material, emission, new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f));
            AddTrianglePrimitiveSmooth(b0, t1, t0, n0, n1, n0, transform, color, material, emission, new Vector2(u0, 1f), new Vector2(u1, 0f), new Vector2(u0, 0f));
            AddTrianglePrimitive(new Vector3(0f, 0.5f, 0f), t1, t0, transform, color, material, emission);
            AddTrianglePrimitive(new Vector3(0f, -0.5f, 0f), b0, b1, transform, color, material, emission);
        }
    }


    private void BuildCameraIcon(GameObject obj, bool selected, Matrix4 world)
    {
        Vector4 color = selected
            ? new Vector4(1f, 0.78f, 0.12f, 1f)
            : new Vector4(0.42f, 0.70f, 1f, 1f);

        var p = Vector3.TransformPosition(Vector3.Zero, world);
        float s = 0.35f;
        AddLine(p + new Vector3(-s, -s * 0.6f, 0f), p + new Vector3(s, -s * 0.6f, 0f), color);
        AddLine(p + new Vector3(s, -s * 0.6f, 0f), p + new Vector3(s, s * 0.6f, 0f), color);
        AddLine(p + new Vector3(s, s * 0.6f, 0f), p + new Vector3(-s, s * 0.6f, 0f), color);
        AddLine(p + new Vector3(-s, s * 0.6f, 0f), p + new Vector3(-s, -s * 0.6f, 0f), color);
        AddLine(p + new Vector3(s, 0f, 0f), p + new Vector3(s * 1.6f, s * 0.45f, 0f), color);
        AddLine(p + new Vector3(s, 0f, 0f), p + new Vector3(s * 1.6f, -s * 0.45f, 0f), color);
    }

    private void BuildLightIcon(GameObject obj, bool selected, Matrix4 world)
    {
        Vector4 color = selected
            ? new Vector4(1f, 0.78f, 0.12f, 1f)
            : new Vector4(1f, 0.9f, 0.45f, 1f);

        var p = Vector3.TransformPosition(Vector3.Zero, world);
        float s = 0.35f;
        AddLine(p + new Vector3(-s, 0f, 0f), p + new Vector3(s, 0f, 0f), color);
        AddLine(p + new Vector3(0f, -s, 0f), p + new Vector3(0f, s, 0f), color);
        AddLine(p + new Vector3(0f, 0f, -s), p + new Vector3(0f, 0f, s), color);
        AddLine(p + new Vector3(-s * 0.7f, -s * 0.7f, 0f), p + new Vector3(s * 0.7f, s * 0.7f, 0f), color);
        AddLine(p + new Vector3(-s * 0.7f, s * 0.7f, 0f), p + new Vector3(s * 0.7f, -s * 0.7f, 0f), color);

        if (!selected)
            return;

        var guide = color;
        if (obj.GetComponent<PointLight>() is { } point)
        {
            AddCircle(p, Vector3.UnitY, Math.Min(point.Range, 100f), guide, 64);
            AddCircle(p, Vector3.UnitX, Math.Min(point.Range, 100f), guide, 48);
        }
        if (obj.GetComponent<SpotLight>() is { } spot)
        {
            var dir = ResolveSpotDirection(spot);
            float range = Math.Min(spot.Range, 100f);
            float radius = MathF.Tan(MathHelper.DegreesToRadians(spot.Angle) * 0.5f) * range;
            var end = p + dir * range;
            AddCircle(end, dir, radius, guide, 64);
            var right = Vector3.Cross(dir, Vector3.UnitY);
            if (right.LengthSquared < 0.001f) right = Vector3.UnitX;
            right.Normalize();
            var up = Vector3.Cross(right, dir).Normalized();
            AddLine(p, end + right * radius, guide);
            AddLine(p, end - right * radius, guide);
            AddLine(p, end + up * radius, guide);
            AddLine(p, end - up * radius, guide);
        }
        if (obj.GetComponent<DirectionalLight>() != null)
        {
            var dir = ToTk(obj.GetComponent<DirectionalLight>()!.GetNormalizedDirection()).Normalized();
            AddLine(p, p + dir * 2.2f, guide);
            AddLine(p + dir * 2.2f, p + dir * 1.65f + Vector3.UnitY * 0.28f, guide);
            AddLine(p + dir * 2.2f, p + dir * 1.65f - Vector3.UnitY * 0.28f, guide);
        }
        if (obj.GetComponent<AreaLight>() is { } area)
            AddAreaGuide(p, ForwardFromGameObject(obj), area.Width, area.Height, guide);
        if (obj.GetComponent<RectangleLight>() is { } rect)
            AddAreaGuide(p, ForwardFromGameObject(obj), rect.Width, rect.Height, guide);
    }

    private void AddAreaGuide(Vector3 center, Vector3 normal, float width, float height, Vector4 color)
    {
        var right = Vector3.Cross(normal, Vector3.UnitY);
        if (right.LengthSquared < 0.001f) right = Vector3.UnitX;
        right.Normalize();
        var up = Vector3.Cross(right, normal).Normalized();
        right *= width * 0.5f;
        up *= height * 0.5f;
        var a = center - right - up;
        var b = center + right - up;
        var c = center + right + up;
        var d = center - right + up;
        AddLine(a, b, color); AddLine(b, c, color); AddLine(c, d, color); AddLine(d, a, color);
        AddLine(center, center + normal * 0.85f, color);
    }

    private void AddCircle(Vector3 center, Vector3 normal, float radius, Vector4 color, int segments)
    {
        if (radius <= 0.001f) return;
        normal.Normalize();
        var right = Vector3.Cross(normal, Vector3.UnitY);
        if (right.LengthSquared < 0.001f) right = Vector3.UnitX;
        right.Normalize();
        var up = Vector3.Cross(right, normal).Normalized();
        var prev = center + right * radius;
        for (int i = 1; i <= segments; i++)
        {
            float t = MathHelper.TwoPi * i / segments;
            var p = center + (right * MathF.Cos(t) + up * MathF.Sin(t)) * radius;
            AddLine(prev, p, color);
            prev = p;
        }
    }

}