using System;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    // Hornea el normal map del terreno en CPU (RGB8, una normal por texel del heightmap, codificada
    // n*0.5+0.5). El fragment shader lee este mapa en vez de calcular diferencias finitas por píxel,
    // y como el bake usa la MISMA lógica de stitching con vecinos que usaba TerrainMeshGenerator,
    // la iluminación queda continua entre dos Terrain pegados en mosaico.
    public static class TerrainNormalBaker
    {
        public const int BytesPerTexel = 3;

        public static byte[] BakeAll(Terrain terrain, Terrain? left = null, Terrain? right = null,
            Terrain? top = null, Terrain? bottom = null)
        {
            int res = Math.Max(2, terrain.Resolution);
            var dest = new byte[res * res * BytesPerTexel];
            BakeRegion(terrain, left, right, top, bottom, 0, 0, res - 1, res - 1, dest);
            return dest;
        }

        // Rehornea solo el rect [minX..maxX]x[minZ..maxZ] (inclusivo, coordenadas de celda) dentro
        // de 'dest' (buffer completo res*res*3). Para una pincelada, pasar el dirty-rect inflado
        // 1 texel: la normal de un texel depende de sus 4 vecinos.
        public static void BakeRegion(Terrain terrain, Terrain? left, Terrain? right, Terrain? top, Terrain? bottom,
            int minX, int minZ, int maxX, int maxZ, byte[] dest)
        {
            int res = Math.Max(2, terrain.Resolution);
            if (dest.Length != res * res * BytesPerTexel)
                throw new ArgumentException($"Buffer de normales de tamaño incorrecto: {dest.Length}, esperado {res * res * BytesPerTexel}.");

            minX = Math.Clamp(minX, 0, res - 1);
            minZ = Math.Clamp(minZ, 0, res - 1);
            maxX = Math.Clamp(maxX, 0, res - 1);
            maxZ = Math.Clamp(maxZ, 0, res - 1);

            float stepX = res > 1 ? terrain.SizeX / (res - 1) : 1f;
            float stepZ = res > 1 ? terrain.SizeZ / (res - 1) : 1f;
            float scale = terrain.HeightScale;
            var heightmap = terrain.Heightmap;

            // Misma lógica de borde que tenía TerrainMeshGenerator: si hay un vecino conectado de la
            // MISMA Resolution, se lee su altura real más allá del borde en vez de clampear contra la
            // propia (que duplicaba el valor del borde y producía costura de iluminación visible).
            float HeightAt(int hx, int hz)
            {
                if (hx < 0 && left != null && left.Resolution == res)
                    return left.GetHeight(left.Resolution - 1, ClampIndex(hz, res)) * left.HeightScale;
                if (hx >= res && right != null && right.Resolution == res)
                    return right.GetHeight(0, ClampIndex(hz, res)) * right.HeightScale;
                if (hz < 0 && bottom != null && bottom.Resolution == res)
                    return bottom.GetHeight(ClampIndex(hx, res), bottom.Resolution - 1) * bottom.HeightScale;
                if (hz >= res && top != null && top.Resolution == res)
                    return top.GetHeight(ClampIndex(hx, res), 0) * top.HeightScale;

                hx = ClampIndex(hx, res);
                hz = ClampIndex(hz, res);
                return heightmap[hz * res + hx] * scale;
            }

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float hL = HeightAt(x - 1, z);
                    float hR = HeightAt(x + 1, z);
                    float hD = HeightAt(x, z - 1);
                    float hU = HeightAt(x, z + 1);

                    var tangentX = new Vector3(2f * stepX, hR - hL, 0f);
                    var tangentZ = new Vector3(0f, hU - hD, 2f * stepZ);
                    var normal = Vector3.Cross(tangentZ, tangentX).Normalized();

                    int o = (z * res + x) * BytesPerTexel;
                    dest[o + 0] = Encode(normal.X);
                    dest[o + 1] = Encode(normal.Y);
                    dest[o + 2] = Encode(normal.Z);
                }
            }
        }

        private static byte Encode(float component)
            => (byte)Math.Clamp((int)MathF.Round((component * 0.5f + 0.5f) * 255f), 0, 255);

        private static int ClampIndex(int v, int res) => v < 0 ? 0 : v >= res ? res - 1 : v;
    }
}
