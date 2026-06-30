using OpenTK.Mathematics;
using System;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    private void BuildGrid()
    {
        float step = Math.Clamp(SceneGridSize, 0.01f, 1024f);
        float opacity = Math.Clamp(SceneGridOpacity, 0f, 1f);
        if (opacity <= 0f)
            return;

        int lines = 24;
        if (cachedGridVertices.Length == 0 ||
            cachedGridAxis != SceneGridAxis ||
            MathF.Abs(cachedGridSize - step) > 0.0001f ||
            MathF.Abs(cachedGridOpacity - opacity) > 0.0001f)
        {
            int start = vertices.Count;
            if (SceneGridAxis == GridAxis.All)
            {
                BuildGridPlane(GridAxis.X, step, opacity * 0.55f, lines);
                BuildGridPlane(GridAxis.Y, step, opacity, lines);
                BuildGridPlane(GridAxis.Z, step, opacity * 0.55f, lines);
            }
            else
            {
                BuildGridPlane(SceneGridAxis, step, opacity, lines);
            }

            int count = vertices.Count - start;
            cachedGridVertices = new LineVertex[count];
            for (int i = 0; i < count; i++)
                cachedGridVertices[i] = vertices[start + i];
            vertices.RemoveRange(start, count);
            cachedGridAxis = SceneGridAxis;
            cachedGridSize = step;
            cachedGridOpacity = opacity;
        }

        vertices.AddRange(cachedGridVertices);
    }

    private void BuildGridPlane(GridAxis axis, float step, float opacity, int lines)
    {
        float span = lines * step;
        for (int i = -lines; i <= lines; i++)
        {
            float p = i * step;
            Vector4 minor = new(0.24f, 0.28f, 0.32f, 0.45f * opacity);
            Vector4 major = new(0.38f, 0.43f, 0.48f, 0.65f * opacity);
            Vector4 xAxis = new(0.72f, 0.28f, 0.24f, 0.95f * opacity);
            Vector4 yAxis = new(0.38f, 0.72f, 0.30f, 0.95f * opacity);
            Vector4 zAxis = new(0.25f, 0.48f, 0.82f, 0.95f * opacity);

            Vector4 aColor = i == 0 ? AxisColor(axis, true, xAxis, yAxis, zAxis) : i % 5 == 0 ? major : minor;
            Vector4 bColor = i == 0 ? AxisColor(axis, false, xAxis, yAxis, zAxis) : i % 5 == 0 ? major : minor;

            switch (axis)
            {
                case GridAxis.X:
                    AddLine(new Vector3(0f, p, -span), new Vector3(0f, p, span), aColor);
                    AddLine(new Vector3(0f, -span, p), new Vector3(0f, span, p), bColor);
                    break;
                case GridAxis.Z:
                    AddLine(new Vector3(p, -span, 0f), new Vector3(p, span, 0f), aColor);
                    AddLine(new Vector3(-span, p, 0f), new Vector3(span, p, 0f), bColor);
                    break;
                default:
                    AddLine(new Vector3(p, 0f, -span), new Vector3(p, 0f, span), aColor);
                    AddLine(new Vector3(-span, 0f, p), new Vector3(span, 0f, p), bColor);
                    break;
            }
        }
    }

    private static Vector4 AxisColor(GridAxis plane, bool first, Vector4 xAxis, Vector4 yAxis, Vector4 zAxis) =>
        plane switch
        {
            GridAxis.X => first ? yAxis : zAxis,
            GridAxis.Z => first ? xAxis : yAxis,
            _ => first ? zAxis : xAxis
        };

}
