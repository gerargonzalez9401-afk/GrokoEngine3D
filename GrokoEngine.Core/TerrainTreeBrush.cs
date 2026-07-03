using System;

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
        public static bool Paint(Terrain terrain, int prototypeIndex, float localX, float localZ, float radius,
            float density, float deltaTime, Random rng,
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

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = radius * MathF.Sqrt((float)rng.NextDouble()); // uniforme en el disco, no solo en el borde
                float x = localX + MathF.Cos(angle) * r;
                float z = localZ + MathF.Sin(angle) * r;

                if (TooCloseToExisting(terrain, x, z, minSpacing))
                    continue;

                terrain.Trees.Add(new TerrainTreeInstance
                {
                    LocalX = x,
                    LocalZ = z,
                    RotationY = (float)(rng.NextDouble() * 360.0),
                    Scale = minScale + (float)rng.NextDouble() * Math.Max(0f, maxScale - minScale),
                    PrototypeIndex = prototypeIndex
                });
                changed = true;
            }

            return changed;
        }

        private static bool TooCloseToExisting(Terrain terrain, float x, float z, float minSpacing)
        {
            float minSpacingSq = minSpacing * minSpacing;
            var trees = terrain.Trees;
            for (int i = 0; i < trees.Count; i++)
            {
                float dx = trees[i].LocalX - x;
                float dz = trees[i].LocalZ - z;
                if (dx * dx + dz * dz < minSpacingSq)
                    return true;
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
