using System;

namespace GrokoEngine
{
    // Importa una imagen en escala de grises como heightmap completo del Terrain (estilo Unity
    // "Import Raw/Heightmap"). Recibe los pixeles ya extraidos (grises 0..255) para no acoplar
    // GrokoEngine.Core a System.Drawing/GDI+; el editor (ImGuiEditorApp.TerrainTools.cs) hace la
    // carga de la imagen y llama a Apply() con el resultado.
    public static class TerrainHeightmapImport
    {
        // Remuestrea bilinealmente 'gray' (srcWidth x srcHeight, row-major, 0..255) a la
        // resolucion ACTUAL del terrain y reemplaza terrain.Heightmap entero. Negro (0) -> minHeight,
        // blanco (255) -> maxHeight, en el mismo rango -2..2 que ya usan las brochas de sculpt
        // (ver TerrainBrush.cs). El llamador debe incrementar terrain.Version tras aplicar.
        public static void Apply(Terrain terrain, byte[] gray, int srcWidth, int srcHeight,
            float minHeight = -1f, float maxHeight = 1f)
        {
            if (terrain == null || gray == null || gray.Length == 0 || srcWidth <= 0 || srcHeight <= 0)
                return;

            int resolution = terrain.Resolution;
            var result = new float[resolution * resolution];

            for (int z = 0; z < resolution; z++)
            {
                float v = resolution <= 1 ? 0f : (float)z / (resolution - 1) * (srcHeight - 1);
                int z0 = Math.Clamp((int)MathF.Floor(v), 0, srcHeight - 1);
                int z1 = Math.Min(z0 + 1, srcHeight - 1);
                float tz = v - z0;

                for (int x = 0; x < resolution; x++)
                {
                    float u = resolution <= 1 ? 0f : (float)x / (resolution - 1) * (srcWidth - 1);
                    int x0 = Math.Clamp((int)MathF.Floor(u), 0, srcWidth - 1);
                    int x1 = Math.Min(x0 + 1, srcWidth - 1);
                    float tx = u - x0;

                    float g00 = gray[z0 * srcWidth + x0];
                    float g10 = gray[z0 * srcWidth + x1];
                    float g01 = gray[z1 * srcWidth + x0];
                    float g11 = gray[z1 * srcWidth + x1];

                    float g0 = g00 + (g10 - g00) * tx;
                    float g1 = g01 + (g11 - g01) * tx;
                    float g = (g0 + (g1 - g0) * tz) / 255f; // 0..1

                    result[z * resolution + x] = Math.Clamp(minHeight + g * (maxHeight - minHeight), -2f, 2f);
                }
            }

            terrain.Heightmap = result;
        }
    }
}
