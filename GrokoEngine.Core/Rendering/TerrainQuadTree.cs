using System;
using System.Collections.Generic;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    // Plano en espacio LOCAL del terreno (normal + distancia, forma ax+by+cz+d>=0 = adentro).
    // Se define acá (y no se reusa el Frustum del editor) para que el quadtree viva en Core sin
    // dependencias de render y sea testeable: el editor convierte su frustum a estos planos.
    public readonly struct TerrainClipPlane
    {
        public readonly float Nx, Ny, Nz, D;

        public TerrainClipPlane(float nx, float ny, float nz, float d)
        {
            Nx = nx; Ny = ny; Nz = nz; D = d;
        }
    }

    // Un nodo del quadtree elegido para dibujar este frame: cubre Size x Size celdas del heightmap
    // a partir de (X, Z) (en unidades de celda/quad), con la densidad de malla y constantes de
    // morph del nivel LodLevel. QuadrantMask indica qué cuadrantes del nodo dibujar (bit i = 1 =
    // dibujar; 0=SW, 1=SE, 2=NW, 3=NE): cuando un hijo se seleccionó a un nivel más fino, el padre
    // solo dibuja los cuadrantes restantes (esquema clásico de CDLOD, Strugar 2009).
    public readonly struct SelectedTerrainNode
    {
        public readonly int X;
        public readonly int Z;
        public readonly int Size;
        public readonly int LodLevel;
        public readonly int QuadrantMask;

        public SelectedTerrainNode(int x, int z, int size, int lodLevel, int quadrantMask)
        {
            X = x; Z = z; Size = size; LodLevel = lodLevel; QuadrantMask = quadrantMask;
        }
    }

    // Quadtree de selección de LOD para el terreno (CDLOD): guarda una pirámide min/max de alturas
    // por nodo (para AABB de culling) y elige por frame qué nodos dibujar según distancia a cámara,
    // de modo que la densidad de triángulos en pantalla sea ~constante a cualquier distancia.
    // Trabaja en coordenadas de celda del heightmap y espacio local del terreno; no toca GL.
    public sealed class TerrainQuadTree
    {
        // Celdas (quads) que cubre una hoja. El nodo raíz cubre la menor potencia de 2 múltiplo de
        // esto que alcance a (Resolution-1); si sobra (resoluciones no potencia-de-2), el vertex
        // shader clampea las celdas fuera del heightmap contra el borde (triángulos degenerados,
        // costo ~nulo), así la selección no necesita casos especiales.
        public const int LeafQuads = 32;

        public int RootQuads { get; private set; }
        public int LodLevels { get; private set; }
        public int LeavesPerSide { get; private set; }

        private int _resolution;
        // Pirámide min/max en alturas CRUDAS del heightmap (-2..2): nivel 0 = hojas, último = raíz.
        // Se guarda sin escalar para no tener que reconstruir si solo cambia HeightScale.
        private float[][] _minPyramid = Array.Empty<float[]>();
        private float[][] _maxPyramid = Array.Empty<float[]>();

        private readonly float[] _lodRanges = new float[16];

        public void Build(Terrain terrain)
        {
            int res = Math.Max(2, terrain.Resolution);
            int quads = res - 1;

            int rootQuads = LeafQuads;
            while (rootQuads < quads)
                rootQuads *= 2;

            _resolution = res;
            RootQuads = rootQuads;
            LeavesPerSide = rootQuads / LeafQuads;
            LodLevels = 1;
            for (int side = LeavesPerSide; side > 1; side /= 2)
                LodLevels++;

            _minPyramid = new float[LodLevels][];
            _maxPyramid = new float[LodLevels][];
            for (int level = 0; level < LodLevels; level++)
            {
                int side = LeavesPerSide >> level;
                _minPyramid[level] = new float[side * side];
                _maxPyramid[level] = new float[side * side];
            }

            RebuildLeaves(terrain, 0, 0, LeavesPerSide - 1, LeavesPerSide - 1);
            PropagateUp(0, 0, LeavesPerSide - 1, LeavesPerSide - 1);
        }

        public bool MatchesTerrain(Terrain terrain) => _resolution == Math.Max(2, terrain.Resolution);

        // Actualiza min/max solo para la región de celdas tocada (rect inclusivo en coordenadas de
        // celda del heightmap), típicamente el dirty-rect de una pincelada, y propaga hacia arriba.
        public void UpdateRegion(Terrain terrain, int minCellX, int minCellZ, int maxCellX, int maxCellZ)
        {
            if (_minPyramid.Length == 0 || !MatchesTerrain(terrain))
            {
                Build(terrain);
                return;
            }

            // Una hoja L cubre las muestras [L*LeafQuads, L*LeafQuads + LeafQuads], así que una
            // muestra en x afecta a las hojas L con L*LeafQuads <= x <= L*LeafQuads + LeafQuads.
            int leafMinX = Math.Max(0, (int)Math.Ceiling((minCellX - LeafQuads) / (double)LeafQuads));
            int leafMinZ = Math.Max(0, (int)Math.Ceiling((minCellZ - LeafQuads) / (double)LeafQuads));
            int leafMaxX = Math.Min(LeavesPerSide - 1, Math.Max(0, maxCellX) / LeafQuads);
            int leafMaxZ = Math.Min(LeavesPerSide - 1, Math.Max(0, maxCellZ) / LeafQuads);
            if (leafMinX > leafMaxX || leafMinZ > leafMaxZ)
                return;

            RebuildLeaves(terrain, leafMinX, leafMinZ, leafMaxX, leafMaxZ);
            PropagateUp(leafMinX, leafMinZ, leafMaxX, leafMaxZ);
        }

        public void GetNodeHeightBounds(int level, int nodeX, int nodeZ, out float rawMin, out float rawMax)
        {
            int side = LeavesPerSide >> level;
            int idx = nodeZ * side + nodeX;
            rawMin = _minPyramid[level][idx];
            rawMax = _maxPyramid[level][idx];
        }

        public void GetRootHeightBounds(out float rawMin, out float rawMax)
            => GetNodeHeightBounds(LodLevels - 1, 0, 0, out rawMin, out rawMax);

        // Distancia (en unidades de mundo/local) a la que termina el nivel 0 (hojas a máxima
        // densidad). Cada nivel siguiente duplica el rango. LodDetailBalance del terreno lo escala.
        public static float ComputeLodRange0(Terrain terrain)
        {
            int res = Math.Max(2, terrain.Resolution);
            float cellX = terrain.SizeX / (res - 1);
            float cellZ = terrain.SizeZ / (res - 1);
            float cell = MathF.Max(cellX, cellZ);
            return MathF.Max(1f, 4f * LeafQuads * cell * MathF.Max(0.1f, terrain.LodDetailBalance));
        }

        // Constantes de morph del nivel: los vértices "impares" de la rejilla se deslizan hacia los
        // pares entre morphStart y morphEnd, de modo que al cruzar al nivel más grueso la geometría
        // ya coincide exactamente (sin popping ni grietas). El morph ocupa el último 30% del anillo
        // de distancias que cubre el nivel.
        public void GetMorphConsts(int level, float lodRange0, out float morphStart, out float morphEnd)
        {
            float rangeEnd = lodRange0 * (1 << level);
            float rangeStart = level == 0 ? 0f : lodRange0 * (1 << (level - 1));
            morphEnd = rangeEnd * 0.98f;
            morphStart = rangeStart + (rangeEnd - rangeStart) * 0.70f;
            if (morphStart >= morphEnd)
                morphStart = morphEnd - 0.01f;
        }

        // Selección CDLOD clásica: recorre el árbol de grueso a fino; un nodo entra completo si su
        // AABB no alcanza el rango del nivel más fino; si lo alcanza, recurre a los hijos y dibuja
        // él mismo solo los cuadrantes cuyos hijos quedaron fuera de rango. Culling por frustum con
        // los planos en espacio local (null = sin culling, p.ej. tests o pase de sombras).
        public void SelectNodes(Terrain terrain, Vector3 localCameraPos, float lodRange0,
            TerrainClipPlane[]? frustumPlanes, List<SelectedTerrainNode> output)
        {
            if (_minPyramid.Length == 0 || !MatchesTerrain(terrain))
                Build(terrain);

            for (int i = 0; i < LodLevels && i < _lodRanges.Length; i++)
                _lodRanges[i] = lodRange0 * (1 << i);

            var ctx = new SelectContext
            {
                Terrain = terrain,
                CameraPos = localCameraPos,
                Planes = frustumPlanes,
                Output = output,
                HalfX = terrain.SizeX * 0.5f,
                HalfZ = terrain.SizeZ * 0.5f,
                CellX = terrain.SizeX / (_resolution - 1),
                CellZ = terrain.SizeZ / (_resolution - 1),
                HeightScale = terrain.HeightScale,
            };

            SelectNode(ref ctx, LodLevels - 1, 0, 0);
        }

        private struct SelectContext
        {
            public Terrain Terrain;
            public Vector3 CameraPos;
            public TerrainClipPlane[]? Planes;
            public List<SelectedTerrainNode> Output;
            public float HalfX, HalfZ, CellX, CellZ, HeightScale;
        }

        // Devuelve true si el área del nodo quedó "resuelta" (dibujada o descartada por culling);
        // false si el nodo está fuera del rango de su nivel y el padre debe cubrir el área.
        private bool SelectNode(ref SelectContext ctx, int level, int nodeX, int nodeZ)
        {
            int sizeQuads = LeafQuads << level;
            int cellX0 = nodeX * sizeQuads;
            int cellZ0 = nodeZ * sizeQuads;

            GetNodeHeightBounds(level, nodeX, nodeZ, out float rawMin, out float rawMax);
            ComputeNodeAabb(ref ctx, cellX0, cellZ0, sizeQuads, rawMin, rawMax,
                out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);

            // El nivel raíz no tiene rango superior: siempre está "en rango" (el terreno se dibuja
            // completo aunque la cámara esté lejísimos, solo que al LOD más grueso).
            bool isRoot = level == LodLevels - 1;
            if (!isRoot && !AabbIntersectsSphere(ctx.CameraPos, _lodRanges[level], minX, minY, minZ, maxX, maxY, maxZ))
                return false;

            if (!AabbIntersectsFrustum(ctx.Planes, minX, minY, minZ, maxX, maxY, maxZ))
                return true; // culled: área resuelta, no hay nada que dibujar

            if (level == 0)
            {
                ctx.Output.Add(new SelectedTerrainNode(cellX0, cellZ0, sizeQuads, 0, 0b1111));
                return true;
            }

            // ¿Alguna parte del nodo cae dentro del rango del nivel más fino? Si no, se dibuja entero acá.
            if (!AabbIntersectsSphere(ctx.CameraPos, _lodRanges[level - 1], minX, minY, minZ, maxX, maxY, maxZ))
            {
                ctx.Output.Add(new SelectedTerrainNode(cellX0, cellZ0, sizeQuads, level, 0b1111));
                return true;
            }

            int mask = 0b1111;
            for (int cz = 0; cz < 2; cz++)
            {
                for (int cx = 0; cx < 2; cx++)
                {
                    int quadrant = cz * 2 + cx;
                    if (SelectNode(ref ctx, level - 1, nodeX * 2 + cx, nodeZ * 2 + cz))
                        mask &= ~(1 << quadrant);
                }
            }

            if (mask != 0)
                ctx.Output.Add(new SelectedTerrainNode(cellX0, cellZ0, sizeQuads, level, mask));
            return true;
        }

        private void ComputeNodeAabb(ref SelectContext ctx, int cellX0, int cellZ0, int sizeQuads,
            float rawMin, float rawMax,
            out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            // Las celdas fuera del heightmap se clampean en el shader contra el borde, así que el
            // AABB también se clampea: el nodo no genera geometría más allá de Resolution-1.
            int cellX1 = Math.Min(cellX0 + sizeQuads, _resolution - 1);
            int cellZ1 = Math.Min(cellZ0 + sizeQuads, _resolution - 1);
            int cx0 = Math.Min(cellX0, _resolution - 1);
            int cz0 = Math.Min(cellZ0, _resolution - 1);

            minX = cx0 * ctx.CellX - ctx.HalfX;
            maxX = cellX1 * ctx.CellX - ctx.HalfX;
            minZ = cz0 * ctx.CellZ - ctx.HalfZ;
            maxZ = cellZ1 * ctx.CellZ - ctx.HalfZ;

            float y0 = rawMin * ctx.HeightScale;
            float y1 = rawMax * ctx.HeightScale;
            minY = MathF.Min(y0, y1);
            maxY = MathF.Max(y0, y1);
        }

        private static bool AabbIntersectsSphere(Vector3 center, float radius,
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            float dx = center.X < minX ? minX - center.X : center.X > maxX ? center.X - maxX : 0f;
            float dy = center.Y < minY ? minY - center.Y : center.Y > maxY ? center.Y - maxY : 0f;
            float dz = center.Z < minZ ? minZ - center.Z : center.Z > maxZ ? center.Z - maxZ : 0f;
            return dx * dx + dy * dy + dz * dz <= radius * radius;
        }

        private static bool AabbIntersectsFrustum(TerrainClipPlane[]? planes,
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            if (planes == null)
                return true;

            // Test p-vertex: si el vértice del AABB más "adentro" según la normal del plano queda
            // afuera, todo el AABB queda afuera.
            for (int i = 0; i < planes.Length; i++)
            {
                var p = planes[i];
                float px = p.Nx >= 0f ? maxX : minX;
                float py = p.Ny >= 0f ? maxY : minY;
                float pz = p.Nz >= 0f ? maxZ : minZ;
                if (p.Nx * px + p.Ny * py + p.Nz * pz + p.D < 0f)
                    return false;
            }
            return true;
        }

        private void RebuildLeaves(Terrain terrain, int leafMinX, int leafMinZ, int leafMaxX, int leafMaxZ)
        {
            var mins = _minPyramid[0];
            var maxs = _maxPyramid[0];

            for (int lz = leafMinZ; lz <= leafMaxZ; lz++)
            {
                for (int lx = leafMinX; lx <= leafMaxX; lx++)
                {
                    // La hoja cubre las muestras [lx*32, lx*32+32] inclusive; fuera del heightmap se
                    // clampea contra el borde (igual que el shader), así el AABB sigue siendo válido.
                    int x0 = Math.Min(lx * LeafQuads, _resolution - 1);
                    int z0 = Math.Min(lz * LeafQuads, _resolution - 1);
                    int x1 = Math.Min(lx * LeafQuads + LeafQuads, _resolution - 1);
                    int z1 = Math.Min(lz * LeafQuads + LeafQuads, _resolution - 1);

                    float min = float.MaxValue;
                    float max = float.MinValue;
                    for (int z = z0; z <= z1; z++)
                    {
                        int row = z * _resolution;
                        for (int x = x0; x <= x1; x++)
                        {
                            float h = terrain.Heightmap[row + x];
                            if (h < min) min = h;
                            if (h > max) max = h;
                        }
                    }

                    int idx = lz * LeavesPerSide + lx;
                    mins[idx] = min;
                    maxs[idx] = max;
                }
            }
        }

        private void PropagateUp(int leafMinX, int leafMinZ, int leafMaxX, int leafMaxZ)
        {
            int minX = leafMinX, minZ = leafMinZ, maxX = leafMaxX, maxZ = leafMaxZ;
            for (int level = 1; level < LodLevels; level++)
            {
                minX /= 2; minZ /= 2; maxX /= 2; maxZ /= 2;
                int side = LeavesPerSide >> level;
                int childSide = LeavesPerSide >> (level - 1);
                var mins = _minPyramid[level];
                var maxs = _maxPyramid[level];
                var childMins = _minPyramid[level - 1];
                var childMaxs = _maxPyramid[level - 1];

                for (int z = minZ; z <= maxZ && z < side; z++)
                {
                    for (int x = minX; x <= maxX && x < side; x++)
                    {
                        float min = float.MaxValue;
                        float max = float.MinValue;
                        for (int cz = 0; cz < 2; cz++)
                        {
                            for (int cx = 0; cx < 2; cx++)
                            {
                                int ci = (z * 2 + cz) * childSide + (x * 2 + cx);
                                if (childMins[ci] < min) min = childMins[ci];
                                if (childMaxs[ci] > max) max = childMaxs[ci];
                            }
                        }
                        mins[z * side + x] = min;
                        maxs[z * side + x] = max;
                    }
                }
            }
        }
    }
}
