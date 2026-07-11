using System;
using System.Collections.Generic;

namespace GrokoEngine
{
    // Matematica pura del pincel de arboles (plantar/borrar), extraida del editor igual que
    // TerrainBrush.cs, para que sea testeable sin ImGui/OpenGL.
    public static class TerrainTreeBrush
    {
        // Esparce arboles al azar dentro del disco del pincel. La cantidad esperada por llamada
        // es density * area * deltaTime (igual idea que un emisor de particulas), asi que llamar
        // esto cada frame mientras se mantiene el click da una densidad estable sin importar el
        // framerate. Evita apilar arboles pegados unos con otros (minSpacing).
        //
        // El chequeo de "muy cerca de uno existente" usa una grilla espacial (celda = minSpacing)
        // en vez de comparar contra TODOS los arboles ya plantados: con miles de arboles en el
        // terreno, un escaneo lineal por cada candidato se vuelve carísimo (candidatos x N
        // arboles existentes, se multiplica con cada trazo). La grilla se construye una vez por
        // llamada a Paint (O(N) en el peor caso) y cada candidato solo mira su celda + las 8
        // vecinas (O(1) amortizado), sin importar cuántos arboles haya en el resto del terreno.
        public static bool Paint(Terrain terrain, int prototypeIndex, float localX, float localZ, float radius,
            float density, float deltaTime, System.Random rng,
            float minScale = 0.85f, float maxScale = 1.15f, float minSpacing = 0.5f)
        {
            if (terrain == null || rng == null) return false;
            if (terrain.TreePrototypes.Count == 0 || prototypeIndex < 0 || prototypeIndex >= terrain.TreePrototypes.Count)
                return false;
            if (radius <= 0f || density <= 0f || deltaTime <= 0f)
                return false;

            float area = MathF.PI * radius * radius;
            float expected = density * area * deltaTime;
            int count = (int)expected;
            float frac = expected - count;
            if (rng.NextDouble() < frac)
                count++;
            if (count <= 0)
                return false;

            float cellSize = Math.Max(0.01f, minSpacing);
            var grid = BuildSpatialGrid(terrain.Trees, cellSize);

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = radius * MathF.Sqrt((float)rng.NextDouble()); // uniforme en el disco, no solo en el borde
                float x = localX + MathF.Cos(angle) * r;
                float z = localZ + MathF.Sin(angle) * r;

                if (IsTooCloseToExisting(terrain.Trees, grid, cellSize, x, z, minSpacing))
                    continue;

                terrain.Trees.Add(new TerrainTreeInstance
                {
                    LocalX = x,
                    LocalZ = z,
                    RotationY = (float)(rng.NextDouble() * 360.0),
                    Scale = minScale + (float)rng.NextDouble() * Math.Max(0f, maxScale - minScale),
                    PrototypeIndex = prototypeIndex
                });
                // El arbol recien plantado tambien cuenta para el resto de este mismo trazo,
                // si no dos candidatos del mismo Paint() podrian quedar pegados entre si.
                AddToGrid(grid, cellSize, x, z, terrain.Trees.Count - 1);
                changed = true;
            }

            return changed;
        }

        private static (int X, int Z) CellOf(float x, float z, float cellSize) =>
            ((int)MathF.Floor(x / cellSize), (int)MathF.Floor(z / cellSize));

        private static Dictionary<(int, int), List<int>> BuildSpatialGrid(List<TerrainTreeInstance> trees, float cellSize)
        {
            var grid = new Dictionary<(int, int), List<int>>(trees.Count);
            for (int i = 0; i < trees.Count; i++)
                AddToGrid(grid, cellSize, trees[i].LocalX, trees[i].LocalZ, i);
            return grid;
        }

        private static void AddToGrid(Dictionary<(int, int), List<int>> grid, float cellSize, float x, float z, int treeIndex)
        {
            var cell = CellOf(x, z, cellSize);
            if (!grid.TryGetValue(cell, out var list))
            {
                list = new List<int>();
                grid[cell] = list;
            }
            list.Add(treeIndex);
        }

        private static bool IsTooCloseToExisting(List<TerrainTreeInstance> trees, Dictionary<(int, int), List<int>> grid,
            float cellSize, float x, float z, float minSpacing)
        {
            float minSpacingSq = minSpacing * minSpacing;
            var (cx, cz) = CellOf(x, z, cellSize);

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!grid.TryGetValue((cx + dx, cz + dz), out var indices))
                        continue;

                    foreach (int idx in indices)
                    {
                        float ddx = trees[idx].LocalX - x;
                        float ddz = trees[idx].LocalZ - z;
                        if (ddx * ddx + ddz * ddz < minSpacingSq)
                            return true;
                    }
                }
            }

            return false;
        }

        // Borra todos los arboles dentro del radio del pincel (Shift+click en el editor).
        public static bool Erase(Terrain terrain, float localX, float localZ, float radius)
        {
            if (terrain == null || radius <= 0f)
                return false;

            float radiusSq = radius * radius;
            int removed = terrain.Trees.RemoveAll(t =>
            {
                float dx = t.LocalX - localX;
                float dz = t.LocalZ - localZ;
                return dx * dx + dz * dz <= radiusSq;
            });
            return removed > 0;
        }
    }
}
