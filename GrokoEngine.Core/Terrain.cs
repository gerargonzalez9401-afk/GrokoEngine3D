using System;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    // =====================================================
    // TERRAIN
    // =====================================================
    public class Terrain : Component
    {
        private int _resolution = 33;

        public int Resolution
        {
            get => _resolution;
            set
            {
                int v = Math.Clamp(value, 2, 256);
                if (v == _resolution)
                    return;
                _resolution = v;
                EnsureHeightmapSize();
                EnsureSplatMapSize();
                Version++;
                SplatVersion++;
            }
        }

        public float SizeX = 50f;
        public float SizeZ = 50f;
        public float HeightScale = 10f;

        public float[] Heightmap = new float[33 * 33];

        // Hasta 4 capas de textura pintables (rutas, pueden ser null/"") y su tiling (repeticiones de UV).
        public string[] LayerTextures = { "", "", "", "" };
        public float[] LayerTiling = new float[4] { 1f, 1f, 1f, 1f };

        // Mapa de pesos por capa (RGBA, uno por celda del heightmap, row-major). Por defecto 100% capa 0.
        public byte[] SplatMap = Array.Empty<byte>();

        // Se incrementa cada vez que cambia algo que afecta al mesh generado
        // (Resolution, SizeX/Z, HeightScale o el propio Heightmap).
        // El renderer usa esto para saber cuándo regenerar/reasubir el mesh a la GPU.
        public int Version;

        // Se incrementa cada vez que cambia el SplatMap (pintura) o el Resolution.
        // El renderer usa esto para saber cuándo re-subir la textura splat a la GPU.
        public int SplatVersion;

        public Terrain()
        {
            EnsureHeightmapSize();
            EnsureSplatMapSize();
        }

        public void EnsureHeightmapSize()
        {
            int needed = _resolution * _resolution;
            if (Heightmap.Length != needed)
                Array.Resize(ref Heightmap, needed);
        }

        public void EnsureSplatMapSize()
        {
            int needed = _resolution * _resolution * 4;
            if (SplatMap.Length == needed)
                return;

            var resized = new byte[needed];
            for (int i = 0; i < needed; i += 4)
            {
                resized[i] = 255; // 100% capa 0 por defecto
            }
            SplatMap = resized;
        }

        public float GetHeight(int x, int z)
        {
            x = Math.Clamp(x, 0, _resolution - 1);
            z = Math.Clamp(z, 0, _resolution - 1);
            return Heightmap[z * _resolution + x];
        }

        // Altura del terreno (en espacio local) para una coordenada XZ local (sin transformar por el GameObject),
        // mediante interpolación bilineal sobre el heightmap. Pensado para uso futuro (colisión por altura, sculpting).
        public float GetHeightLocal(float localX, float localZ)
        {
            float halfX = SizeX * 0.5f;
            float halfZ = SizeZ * 0.5f;
            float u = (localX + halfX) / SizeX * (_resolution - 1);
            float v = (localZ + halfZ) / SizeZ * (_resolution - 1);

            u = Math.Clamp(u, 0f, _resolution - 1);
            v = Math.Clamp(v, 0f, _resolution - 1);

            int x0 = (int)MathF.Floor(u);
            int z0 = (int)MathF.Floor(v);
            int x1 = Math.Min(x0 + 1, _resolution - 1);
            int z1 = Math.Min(z0 + 1, _resolution - 1);

            float tx = u - x0;
            float tz = v - z0;

            float h00 = GetHeight(x0, z0);
            float h10 = GetHeight(x1, z0);
            float h01 = GetHeight(x0, z1);
            float h11 = GetHeight(x1, z1);

            float h0 = h00 + (h10 - h00) * tx;
            float h1 = h01 + (h11 - h01) * tx;
            return (h0 + (h1 - h0) * tz) * HeightScale;
        }
    }
}
