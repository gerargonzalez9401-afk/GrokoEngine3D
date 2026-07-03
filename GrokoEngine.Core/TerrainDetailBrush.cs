using System;

namespace GrokoEngine
{
    // Matematica pura del pincel de detalles/pasto (plantar/borrar), igual idea que
    // TerrainTreeBrush.cs pero mas simple: el pasto se pinta MUY denso (miles de mechones) y no
    // le importa superponerse (a diferencia de los arboles, donde minSpacing evita que se
    // apilen troncos), asi que no hace falta grilla espacial ni chequeo de cercania.
    public static class TerrainDetailBrush
    {
        // Esparce detalles al azar dentro del disco del pincel. La cantidad esperada por llamada
        // es density * area * deltaTime (mismo patron que TerrainTreeBrush.Paint), asi que llamar
        // esto cada frame mientras se mantiene el click da una densidad estable sin importar el
        // framerate. Se corta si Terrain.Details ya llego a MaxDetailInstances.
        public static bool Paint(Terrain terrain, int prototypeIndex, float localX, float localZ, float radius,
            float density, float deltaTime, Random rng,
            float minScale = 0.85f, float maxScale = 1.15f)
        {
            if (terrain == null || rng == null) return false;
            if (terrain.DetailPrototypes.Count == 0 || prototypeIndex < 0 || prototypeIndex >= terrain.DetailPrototypes.Count)
                return false;
            if (radius <= 0f || density <= 0f || deltaTime <= 0f)
                return false;
            if (terrain.Details.Count >= Terrain.MaxDetailInstances)
                return false;

            float area = MathF.PI * radius * radius;
            float expected = density * area * deltaTime;
            int count = (int)expected;
            float frac = expected - count;
            if (rng.NextDouble() < frac)
                count++;
            if (count <= 0)
                return false;

            int room = Terrain.MaxDetailInstances - terrain.Details.Count;
            if (count > room)
                count = room;

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = radius * MathF.Sqrt((float)rng.NextDouble()); // uniforme en el disco, no solo en el borde
                float x = localX + MathF.Cos(angle) * r;
                float z = localZ + MathF.Sin(angle) * r;

                terrain.Details.Add(new TerrainDetailInstance
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

        // Borra todos los detalles dentro del radio del pincel (Shift+click en el editor).
        public static bool Erase(Terrain terrain, float localX, float localZ, float radius)
        {
            if (terrain == null || radius <= 0f)
                return false;

            float radiusSq = radius * radius;
            int removed = terrain.Details.RemoveAll(d =>
            {
                float dx = d.LocalX - localX;
                float dz = d.LocalZ - localZ;
                return dx * dx + dz * dz <= radiusSq;
            });
            return removed > 0;
        }
    }
}
