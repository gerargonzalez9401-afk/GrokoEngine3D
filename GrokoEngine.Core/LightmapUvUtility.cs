using System;

namespace GrokoEngine
{
    /// <summary>
    /// UV secundarias deterministas para lightmapping. Los importadores actuales de Groko
    /// conservan UV0, pero no UV2; por eso cada triángulo recibe un chart exclusivo dentro
    /// de una cuadrícula. Es independiente de las UV de texturas y evita que el bake pinte
    /// sobre islas UV que se superponen.
    /// </summary>
    public static class LightmapUvUtility
    {
        public static (float U, float V) GetTriangleChartUv(int vertexIndex, int vertexStart, int vertexCount)
        {
            int relative = Math.Max(0, vertexIndex - vertexStart);
            int triangleCount = Math.Max(1, vertexCount / 3);
            int triangle = Math.Min(triangleCount - 1, relative / 3);
            int corner = relative % 3;
            int columns = (int)Math.Ceiling(Math.Sqrt(triangleCount));
            int rows = (int)Math.Ceiling(triangleCount / (double)columns);
            int column = triangle % columns;
            int row = triangle / columns;

            // Margen dentro de cada chart: protege mipmaps y el filtro bilinear.
            const float inset = 0.075f;
            float localU = corner == 1 ? 1f - inset : inset;
            float localV = corner == 2 ? 1f - inset : inset;
            return ((column + localU) / columns, (row + localV) / rows);
        }
    }
}
