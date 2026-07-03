using System;
using System.Collections.Generic;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    // Un árbol/prop plantado sobre el terreno (estilo Unity "Tree Instance"). La altura NO se
    // guarda: se resamplea del heightmap en cada uso (render, colisión), así que si esculpís
    // el terreno después de plantar, los árboles "flotan" hasta la nueva altura solos.
    public struct TerrainTreeInstance
    {
        public float LocalX;
        public float LocalZ;
        public float RotationY;   // grados
        public float Scale;
        public int PrototypeIndex;
    }

    // Un mechón de pasto/detalle plantado sobre el terreno (estilo Unity "Detail Instance").
    // A diferencia de los árboles, siempre se dibuja como billboard plano (nunca malla 3D) — el
    // pasto de Unity clásico ("Detail Texture") funciona igual, sin LOD de malla.
    public struct TerrainDetailInstance
    {
        public float LocalX;
        public float LocalZ;
        public float RotationY;
        public float Scale;
        public int PrototypeIndex;
    }

    // Textura + tamaño de un tipo de detalle plantable (estilo Unity "Detail Prototype": Width/
    // Height en vez de un mesh, porque el detalle siempre se renderiza como quad plano).
    public struct TerrainDetailPrototype
    {
        public string TexturePath;
        public float Width;
        public float Height;
    }

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

        // Mallas/prefabs plantables (estilo Unity "Tree Prototypes"). El índice en esta lista es
        // el que referencia TerrainTreeInstance.PrototypeIndex.
        public List<string> TreePrototypes = new();
        public List<TerrainTreeInstance> Trees = new();

        // Estilo Unity ("Tree Distance"/"Billboard Start"/"Max Mesh Trees" en Terrain Settings):
        // más allá de TreeDistance un árbol ni se procesa. De los que quedan dentro, un árbol se
        // dibuja como malla 3D solo si está más cerca que BillboardStart Y entra en el ranking de
        // los MaxMeshTrees más cercanos a la cámara; el resto (más lejos que BillboardStart, o
        // dentro pero fuera del ranking) se dibuja como un quad plano ("billboard") que rota para
        // mirar siempre a cámara — mucho más barato que la malla completa a esa distancia.
        public float TreeDistance = 250f;
        public float BillboardStart = 80f;
        public int MaxMeshTrees = 1500;

        // Detalles/pasto plantables (estilo Unity "Detail Prototypes"). Sin límite de cantidad
        // "malla vs billboard" como los árboles porque SIEMPRE son billboard; en cambio Unity
        // limita con "Detail Distance" (no se dibujan más allá de esa distancia) y "Density Scale"
        // (reduce cuántos de los plantados se dibujan sin borrar los datos pintados).
        public List<TerrainDetailPrototype> DetailPrototypes = new();
        public List<TerrainDetailInstance> Details = new();
        public float DetailDistance = 60f;
        public float DetailDensityScale = 1f;

        // Tope duro de instancias de detalle por Terrain: pintar pasto es "denso" por diseño
        // (miles de mechones), pero sin límite un solo trazo de pincel con densidad alta podría
        // generar una lista de tamaño no acotado. Unity acota esto con una grilla de densidad de
        // resolución fija; acá, más simple, directamente se deja de agregar más allá de este tope.
        public const int MaxDetailInstances = 60000;

        // Se incrementa cada vez que cambia algo que afecta al mesh generado
        // (Resolution, SizeX/Z, HeightScale o el propio Heightmap).
        // El renderer usa esto para saber cuándo regenerar/reasubir el mesh a la GPU.
        public int Version;

        // Se incrementa cada vez que cambia el SplatMap (pintura) o el Resolution.
        // El renderer usa esto para saber cuándo re-subir la textura splat a la GPU.
        public int SplatVersion;

        // Se incrementa cada vez que cambia la lista de árboles (plantar/borrar) o los prototipos.
        // El renderer lo usa para saber cuándo reconstruir el batch de instancias.
        public int TreeVersion;

        // Igual que TreeVersion pero para Details/pasto.
        public int DetailVersion;

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
