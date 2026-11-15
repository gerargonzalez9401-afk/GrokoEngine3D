using GrokoEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace GrokoEngine.ImGuiEditor;

internal enum EditorPrimitiveMeshKind
{
    Cube,
    Plane,
    Quad,
    Sphere,
    Cylinder,
    Capsule
}

internal static class PrimitiveMeshFactory
{
    private readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector2 Uv);

    public static string EnsurePrimitiveMesh(string assetsRoot, EditorPrimitiveMeshKind kind)
    {
        string directory = Path.Combine(assetsRoot, "_EnginePrimitives");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, kind + ".obj");
        string obj = BuildObj(kind);
        if (!File.Exists(path) || File.ReadAllText(path) != obj)
            AtomicFile.WriteAllText(path, obj);

        AssetDatabase.Get(assetsRoot).GetOrCreateGuid(path);
        ObjLoader.InvalidateCache(path);
        return path;
    }

    public static void EnsureAllPrimitiveMeshes(string assetsRoot)
    {
        foreach (EditorPrimitiveMeshKind kind in Enum.GetValues<EditorPrimitiveMeshKind>())
            EnsurePrimitiveMesh(assetsRoot, kind);
    }

    private static string BuildObj(EditorPrimitiveMeshKind kind)
    {
        var triangles = new List<Vertex>(4096);
        switch (kind)
        {
            case EditorPrimitiveMeshKind.Cube:
                AddCube(triangles);
                break;
            case EditorPrimitiveMeshKind.Plane:
                AddPlane(triangles);
                break;
            case EditorPrimitiveMeshKind.Quad:
                AddQuadBillboard(triangles);
                break;
            case EditorPrimitiveMeshKind.Sphere:
                AddSphere(triangles, 64, 32);
                break;
            case EditorPrimitiveMeshKind.Cylinder:
                AddCylinder(triangles, 64);
                break;
            case EditorPrimitiveMeshKind.Capsule:
                AddCapsule(triangles, 64, 16);
                break;
        }

        var sb = new StringBuilder(triangles.Count * 96);
        sb.AppendLine("# GrokoEngine built-in primitive mesh");
        sb.AppendLine("o " + kind);
        sb.AppendLine("g " + kind);
        sb.AppendLine("usemtl Default");
        var ci = CultureInfo.InvariantCulture;

        foreach (var v in triangles)
            sb.Append("v ").Append(v.Position.X.ToString("0.######", ci)).Append(' ')
                .Append(v.Position.Y.ToString("0.######", ci)).Append(' ')
                .Append(v.Position.Z.ToString("0.######", ci)).AppendLine();
        foreach (var v in triangles)
            sb.Append("vt ").Append(v.Uv.X.ToString("0.######", ci)).Append(' ')
                .Append((1f - v.Uv.Y).ToString("0.######", ci)).AppendLine();
        foreach (var v in triangles)
            sb.Append("vn ").Append(v.Normal.X.ToString("0.######", ci)).Append(' ')
                .Append(v.Normal.Y.ToString("0.######", ci)).Append(' ')
                .Append(v.Normal.Z.ToString("0.######", ci)).AppendLine();

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = i + 1, b = i + 2, c = i + 3;
            sb.Append("f ")
                .Append(a).Append('/').Append(a).Append('/').Append(a).Append(' ')
                .Append(b).Append('/').Append(b).Append('/').Append(b).Append(' ')
                .Append(c).Append('/').Append(c).Append('/').Append(c).AppendLine();
        }

        return sb.ToString();
    }

    private static void AddCube(List<Vertex> t)
    {
        AddFace(t, new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), Vector3.UnitZ);
        AddFace(t, new(0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), -Vector3.UnitZ);
        AddFace(t, new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), Vector3.UnitY);
        AddFace(t, new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f), -Vector3.UnitY);
        AddFace(t, new(0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f), Vector3.UnitX);
        AddFace(t, new(-0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, -0.5f), -Vector3.UnitX);
    }

    private static void AddPlane(List<Vertex> t)
        => AddFace(t, new(-0.5f, 0f, -0.5f), new(-0.5f, 0f, 0.5f), new(0.5f, 0f, 0.5f), new(0.5f, 0f, -0.5f), Vector3.UnitY);

    private static void AddQuadBillboard(List<Vertex> t)
        => AddFace(t, new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f), Vector3.UnitZ);

    private static void AddSphere(List<Vertex> t, int slices, int rings)
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
                AddSmoothQuad(t, SpherePoint(u0, phi0), SpherePoint(u1, phi0), SpherePoint(u1, phi1), SpherePoint(u0, phi1), new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1));
            }
        }
    }

    private static Vector3 SpherePoint(float u, float phi)
    {
        float theta = u * MathF.PI * 2f;
        float cp = MathF.Cos(phi);
        return new Vector3(MathF.Cos(theta) * cp * 0.5f, MathF.Sin(phi) * 0.5f, MathF.Sin(theta) * cp * 0.5f);
    }

    private static void AddCylinder(List<Vertex> t, int slices)
    {
        for (int i = 0; i < slices; i++)
        {
            float u0 = i / (float)slices;
            float u1 = (i + 1) / (float)slices;
            float a0 = u0 * MathF.PI * 2f;
            float a1 = u1 * MathF.PI * 2f;
            var b0 = new Vector3(MathF.Cos(a0) * 0.5f, -0.5f, MathF.Sin(a0) * 0.5f);
            var b1 = new Vector3(MathF.Cos(a1) * 0.5f, -0.5f, MathF.Sin(a1) * 0.5f);
            var t0 = new Vector3(b0.X, 0.5f, b0.Z);
            var t1 = new Vector3(b1.X, 0.5f, b1.Z);
            var n0 = Vector3.Normalize(new Vector3(b0.X, 0f, b0.Z));
            var n1 = Vector3.Normalize(new Vector3(b1.X, 0f, b1.Z));

            AddTri(t, b0, n0, new(u0, 1f), b1, n1, new(u1, 1f), t1, n1, new(u1, 0f));
            AddTri(t, b0, n0, new(u0, 1f), t1, n1, new(u1, 0f), t0, n0, new(u0, 0f));
            AddTri(t, new(0f, 0.5f, 0f), Vector3.UnitY, new(0.5f, 0.5f), t1, Vector3.UnitY, RingUv(t1), t0, Vector3.UnitY, RingUv(t0));
            AddTri(t, new(0f, -0.5f, 0f), -Vector3.UnitY, new(0.5f, 0.5f), b0, -Vector3.UnitY, RingUv(b0), b1, -Vector3.UnitY, RingUv(b1));
        }
    }

    private static void AddCapsule(List<Vertex> t, int slices, int hemiRings)
    {
        const float radius = 0.5f;
        const float cylinderHalf = 0.5f;
        for (int i = 0; i < slices; i++)
        {
            float u0 = i / (float)slices;
            float u1 = (i + 1) / (float)slices;
            float a0 = u0 * MathF.PI * 2f;
            float a1 = u1 * MathF.PI * 2f;
            var b0 = new Vector3(MathF.Cos(a0) * radius, -cylinderHalf, MathF.Sin(a0) * radius);
            var b1 = new Vector3(MathF.Cos(a1) * radius, -cylinderHalf, MathF.Sin(a1) * radius);
            var top0 = new Vector3(b0.X, cylinderHalf, b0.Z);
            var top1 = new Vector3(b1.X, cylinderHalf, b1.Z);
            var n0 = Vector3.Normalize(new Vector3(b0.X, 0f, b0.Z));
            var n1 = Vector3.Normalize(new Vector3(b1.X, 0f, b1.Z));
            AddTri(t, b0, n0, new(u0, 0.75f), b1, n1, new(u1, 0.75f), top1, n1, new(u1, 0.25f));
            AddTri(t, b0, n0, new(u0, 0.75f), top1, n1, new(u1, 0.25f), top0, n0, new(u0, 0.25f));
        }

        AddCapsuleHemisphere(t, slices, hemiRings, cylinderHalf, true);
        AddCapsuleHemisphere(t, slices, hemiRings, -cylinderHalf, false);
    }

    private static void AddCapsuleHemisphere(List<Vertex> t, int slices, int rings, float yOffset, bool top)
    {
        for (int y = 0; y < rings; y++)
        {
            float a = y / (float)rings;
            float b = (y + 1) / (float)rings;
            float phi0 = a * MathF.PI * 0.5f;
            float phi1 = b * MathF.PI * 0.5f;
            if (!top) { phi0 = -phi0; phi1 = -phi1; }

            float v0 = top ? 0.25f * (1f - a) : 0.75f + 0.25f * a;
            float v1 = top ? 0.25f * (1f - b) : 0.75f + 0.25f * b;
            for (int x = 0; x < slices; x++)
            {
                float u0 = x / (float)slices;
                float u1 = (x + 1) / (float)slices;
                var p00 = CapsulePoint(u0, phi0, yOffset);
                var p10 = CapsulePoint(u1, phi0, yOffset);
                var p11 = CapsulePoint(u1, phi1, yOffset);
                var p01 = CapsulePoint(u0, phi1, yOffset);
                AddSmoothQuad(t, p00, p10, p11, p01, new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1), p => Vector3.Normalize(new Vector3(p.X, p.Y - yOffset, p.Z)));
            }
        }
    }

    private static Vector3 CapsulePoint(float u, float phi, float yOffset)
    {
        float theta = u * MathF.PI * 2f;
        float cp = MathF.Cos(phi);
        return new Vector3(MathF.Cos(theta) * cp * 0.5f, yOffset + MathF.Sin(phi) * 0.5f, MathF.Sin(theta) * cp * 0.5f);
    }

    private static void AddFace(List<Vertex> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        AddTri(t, a, n, new(0f, 1f), b, n, new(1f, 1f), c, n, new(1f, 0f));
        AddTri(t, a, n, new(0f, 1f), c, n, new(1f, 0f), d, n, new(0f, 0f));
    }

    private static void AddSmoothQuad(List<Vertex> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uva, Vector2 uvb, Vector2 uvc, Vector2 uvd, Func<Vector3, Vector3>? normal = null)
    {
        normal ??= p => Vector3.Normalize(p);
        AddTri(t, a, normal(a), uva, b, normal(b), uvb, c, normal(c), uvc);
        AddTri(t, a, normal(a), uva, c, normal(c), uvc, d, normal(d), uvd);
    }

    private static void AddTri(List<Vertex> t, Vector3 a, Vector3 na, Vector2 uva, Vector3 b, Vector3 nb, Vector2 uvb, Vector3 c, Vector3 nc, Vector2 uvc)
    {
        var geometric = Vector3.Cross(b - a, c - a);
        var desired = na + nb + nc;
        if (geometric.LengthSquared() > 0.000001f &&
            desired.LengthSquared() > 0.000001f &&
            Vector3.Dot(geometric, desired) < 0f)
        {
            (b, c) = (c, b);
            (nb, nc) = (nc, nb);
            (uvb, uvc) = (uvc, uvb);
        }

        t.Add(new Vertex(a, SafeNormal(na), uva));
        t.Add(new Vertex(b, SafeNormal(nb), uvb));
        t.Add(new Vertex(c, SafeNormal(nc), uvc));
    }

    private static Vector2 RingUv(Vector3 p) => new(p.X + 0.5f, p.Z + 0.5f);
    private static Vector3 SafeNormal(Vector3 n) => n.LengthSquared() > 0.000001f ? Vector3.Normalize(n) : Vector3.UnitY;
}
