using System;

namespace GrokoEngine
{
    public enum TerrainBrushTool
    {
        Raise,
        Lower,
        Smooth,
        Flatten,
        Noise,
        Extrude
    }

    // Forma del pincel: Soft degrada hacia el borde (colinas redondeadas, el comportamiento de
    // siempre). Hard empuja parejo en todo el radio, sin degradado -> con Raise/Extrude repetido
    // da una meseta de arriba plana y borde filoso en vez de una colina.
    public enum TerrainBrushFalloff
    {
        Soft,
        Hard
    }

    // Mascara de pincel cargada de una imagen (blanco/alfa = fuerza, 0..1). Cuando esta presente,
    // la FORMA del pincel es la de la imagen y reemplaza al circulo con falloff, como las brochas
    // de Unity. El editor la carga de EditorIcons/Brushes (o de cualquier PNG del proyecto).
    public sealed class TerrainBrushMask
    {
        public readonly float[] Samples;
        public readonly int Size;

        public TerrainBrushMask(float[] samples, int size)
        {
            if (samples.Length != size * size)
                throw new ArgumentException("La mascara debe ser cuadrada (size*size muestras).");
            Samples = samples;
            Size = size;
        }

        // Bilineal con clamp; u/v en 0..1 sobre el cuadrado del pincel.
        public float Sample(float u, float v)
        {
            float fx = Math.Clamp(u, 0f, 1f) * (Size - 1);
            float fy = Math.Clamp(v, 0f, 1f) * (Size - 1);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Math.Min(x0 + 1, Size - 1), y1 = Math.Min(y0 + 1, Size - 1);
            float tx = fx - x0, ty = fy - y0;
            float top = Samples[y0 * Size + x0] + (Samples[y0 * Size + x1] - Samples[y0 * Size + x0]) * tx;
            float bottom = Samples[y1 * Size + x0] + (Samples[y1 * Size + x1] - Samples[y1 * Size + x0]) * tx;
            return top + (bottom - top) * ty;
        }
    }

    // Matematica pura de las brochas de Terrain (sculpt/paint), extraida del editor para que sea
    // testeable desde GrokoEngine.Tests sin ImGui/OpenGL. El editor (ImGuiEditorApp.TerrainTools.cs)
    // solo se encarga de raycast/mouse/undo y llama a estos metodos con el mismo resultado de antes.
    public static class TerrainBrush
    {
        // Calcula la celda central (gx/gz) y el rango de celdas afectadas por el pincel.
        // Devuelve false si el pincel no toca la grilla.
        public static bool ComputeBrushCells(Terrain terrain, float localX, float localZ, float brushRadius,
            out float gx, out float gz, out float stepX, out float stepZ,
            out int minX, out int maxX, out int minZ, out int maxZ)
        {
            int resolution = terrain.Resolution;
            stepX = terrain.SizeX / (resolution - 1);
            stepZ = terrain.SizeZ / (resolution - 1);
            gx = 0f;
            gz = 0f;
            minX = maxX = minZ = maxZ = 0;
            if (stepX <= 0f || stepZ <= 0f)
                return false;

            float halfX = terrain.SizeX * 0.5f;
            float halfZ = terrain.SizeZ * 0.5f;

            gx = (localX + halfX) / stepX;
            gz = (localZ + halfZ) / stepZ;

            float radiusCellsX = brushRadius / stepX;
            float radiusCellsZ = brushRadius / stepZ;

            if (gx < -radiusCellsX || gx > resolution - 1 + radiusCellsX ||
                gz < -radiusCellsZ || gz > resolution - 1 + radiusCellsZ)
                return false;

            minX = Math.Max(0, (int)MathF.Floor(gx - radiusCellsX));
            maxX = Math.Min(resolution - 1, (int)MathF.Ceiling(gx + radiusCellsX));
            minZ = Math.Max(0, (int)MathF.Floor(gz - radiusCellsZ));
            maxZ = Math.Min(resolution - 1, (int)MathF.Ceiling(gz + radiusCellsZ));
            return true;
        }

        public static float HashNoise(int x, int z, int seed)
        {
            int n = x * 73856093 ^ z * 19349663 ^ seed * 83492791;
            n = (n << 13) ^ n;
            int nn = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
            return 1f - (nn / 1073741824f); // -1..1
        }

        // Aplica el pincel de sculpt (Raise/Lower/Smooth/Flatten/Noise/Extrude) sobre terrain.Heightmap.
        // flattenHeight: el llamador la captura al primer click del trazo (null = capturar aqui esta vez).
        // Devuelve true si modifico alguna celda (el llamador debe incrementar terrain.Version en ese caso).
        public static bool ApplySculpt(Terrain terrain, TerrainBrushTool tool, float localX, float localZ,
            float radius, float strength, float deltaTime, int noiseSeed, ref float? flattenHeight,
            TerrainBrushFalloff falloffMode = TerrainBrushFalloff.Soft, TerrainBrushMask? mask = null)
        {
            if (!ComputeBrushCells(terrain, localX, localZ, radius, out float gx, out float gz, out float stepX, out float stepZ,
                out int minX, out int maxX, out int minZ, out int maxZ))
                return false;

            int resolution = terrain.Resolution;

            if (tool == TerrainBrushTool.Flatten && flattenHeight == null)
            {
                int centerX = Math.Clamp((int)MathF.Round(gx), 0, resolution - 1);
                int centerZ = Math.Clamp((int)MathF.Round(gz), 0, resolution - 1);
                flattenHeight = terrain.GetHeight(centerX, centerZ);
            }

            bool changed = false;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - gx) * stepX;
                    float dz = (z - gz) * stepZ;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);

                    float falloff;
                    if (mask != null)
                    {
                        // Con mascara, la FORMA la pone la imagen: se muestrea el cuadrado
                        // completo del pincel (las esquinas cuentan, p. ej. una estrella)
                        // y el falloff circular/duro deja de aplicar.
                        float u = dx / radius * 0.5f + 0.5f;
                        float v = dz / radius * 0.5f + 0.5f;
                        if (u < 0f || u > 1f || v < 0f || v > 1f)
                            continue;
                        falloff = mask.Sample(u, v);
                        if (falloff <= 0.004f)
                            continue;
                    }
                    else if (dist > radius)
                    {
                        continue;
                    }
                    else if (falloffMode == TerrainBrushFalloff.Hard)
                    {
                        falloff = 1f;
                    }
                    else
                    {
                        float t = 1f - dist / radius;
                        falloff = t * t * (3f - 2f * t);
                    }
                    int index = z * resolution + x;

                    switch (tool)
                    {
                        case TerrainBrushTool.Raise:
                            terrain.Heightmap[index] = Math.Clamp(terrain.Heightmap[index] + falloff * strength * deltaTime, -2f, 2f);
                            changed = true;
                            break;
                        case TerrainBrushTool.Lower:
                            terrain.Heightmap[index] = Math.Clamp(terrain.Heightmap[index] - falloff * strength * deltaTime, -2f, 2f);
                            changed = true;
                            break;
                        case TerrainBrushTool.Smooth:
                        {
                            float avg = (terrain.GetHeight(x - 1, z) + terrain.GetHeight(x + 1, z) +
                                          terrain.GetHeight(x, z - 1) + terrain.GetHeight(x, z + 1)) * 0.25f;
                            terrain.Heightmap[index] += (avg - terrain.Heightmap[index]) * falloff * strength * deltaTime;
                            changed = true;
                            break;
                        }
                        case TerrainBrushTool.Flatten:
                        {
                            float target = flattenHeight ?? terrain.Heightmap[index];
                            terrain.Heightmap[index] += (target - terrain.Heightmap[index]) * falloff * strength * deltaTime;
                            changed = true;
                            break;
                        }
                        case TerrainBrushTool.Noise:
                        {
                            float n = HashNoise(x, z, noiseSeed);
                            terrain.Heightmap[index] = Math.Clamp(terrain.Heightmap[index] + n * falloff * strength * deltaTime, -2f, 2f);
                            changed = true;
                            break;
                        }
                        case TerrainBrushTool.Extrude:
                            // Sin mascara empuja parejo (meseta); con mascara respeta su forma →
                            // Extrude + mascara = herramienta de SELLO (estampa la imagen).
                            terrain.Heightmap[index] = Math.Clamp(
                                terrain.Heightmap[index] + strength * deltaTime * (mask != null ? falloff : 1f), -2f, 2f);
                            changed = true;
                            break;
                    }
                }
            }

            return changed;
        }

        // Aplica el pincel de pintura de capas sobre terrain.SplatMap (sube activeLayer, baja las
        // otras 3 proporcionalmente para mantener la suma en ~255). Devuelve true si cambio algo
        // (el llamador debe incrementar terrain.SplatVersion en ese caso).
        public static bool ApplyPaint(Terrain terrain, int activeLayer, float localX, float localZ,
            float radius, float strength, float deltaTime, TerrainBrushMask? mask = null)
        {
            if (!ComputeBrushCells(terrain, localX, localZ, radius, out float gx, out float gz, out float stepX, out float stepZ,
                out int minX, out int maxX, out int minZ, out int maxZ))
                return false;

            int resolution = terrain.Resolution;
            activeLayer = Math.Clamp(activeLayer, 0, 3);
            bool changed = false;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - gx) * stepX;
                    float dz = (z - gz) * stepZ;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);

                    float falloff;
                    if (mask != null)
                    {
                        float u = dx / radius * 0.5f + 0.5f;
                        float v = dz / radius * 0.5f + 0.5f;
                        if (u < 0f || u > 1f || v < 0f || v > 1f)
                            continue;
                        falloff = mask.Sample(u, v);
                        if (falloff <= 0.004f)
                            continue;
                    }
                    else if (dist > radius)
                    {
                        continue;
                    }
                    else
                    {
                        float t = 1f - dist / radius;
                        falloff = t * t * (3f - 2f * t);
                    }
                    int pixel = (z * resolution + x) * 4;

                    float increase = falloff * strength * deltaTime * 255f;
                    if (increase <= 0f)
                        continue;

                    float current = terrain.SplatMap[pixel + activeLayer];
                    float newValue = Math.Min(255f, current + increase);
                    float delta = newValue - current;
                    if (delta <= 0f)
                        continue;

                    float otherSum = 0f;
                    for (int c = 0; c < 4; c++)
                    {
                        if (c != activeLayer)
                            otherSum += terrain.SplatMap[pixel + c];
                    }

                    if (otherSum > 0f)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            if (c == activeLayer) continue;
                            float share = terrain.SplatMap[pixel + c] / otherSum;
                            float reduced = Math.Max(0f, terrain.SplatMap[pixel + c] - delta * share);
                            terrain.SplatMap[pixel + c] = (byte)Math.Round(reduced);
                        }
                    }

                    terrain.SplatMap[pixel + activeLayer] = (byte)Math.Round(newValue);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
