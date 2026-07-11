using System;
using Vector2 = MiMotor.Mathematics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using Quaternion = MiMotor.Mathematics.Quaternion;

namespace GrokoEngine
{
    /// <summary>
    /// Generador de números aleatorios estilo Unity (<c>UnityEngine.Random</c>). Se usa igual:
    /// <c>Random.Range(0, 10)</c>, <c>Random.value</c>, <c>Random.Range(0f, 1f)</c>, etc.
    ///
    /// Nota: como en Unity, esta clase vive en el namespace del motor. Si en un script usas también
    /// <c>using System;</c>, escribe <c>System.Random</c> cuando quieras el de .NET (aquí casi nunca hace falta).
    /// </summary>
    public static class Random
    {
        private static System.Random _rng = new();

        /// <summary>Fija la semilla para reproducir la misma secuencia (como Unity Random.InitState).</summary>
        public static void InitState(int seed) => _rng = new System.Random(seed);

        /// <summary>
        /// Entero aleatorio en [minInclusive, maxExclusive). El máximo es EXCLUSIVO (igual que Unity).
        /// Ej.: <c>Random.Range(0, efectos.Length)</c> devuelve un índice válido del array.
        /// </summary>
        public static int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return _rng.Next(minInclusive, maxExclusive);
        }

        /// <summary>
        /// Float aleatorio en [minInclusive, maxInclusive]. El máximo es INCLUSIVO (igual que Unity).
        /// </summary>
        public static float Range(float minInclusive, float maxInclusive)
        {
            if (maxInclusive < minInclusive) (minInclusive, maxInclusive) = (maxInclusive, minInclusive);
            return minInclusive + (float)_rng.NextDouble() * (maxInclusive - minInclusive);
        }

        /// <summary>Float aleatorio en [0.0, 1.0] (como Unity Random.value).</summary>
        public static float value => (float)_rng.NextDouble();

        /// <summary>Punto aleatorio dentro de una esfera de radio 1 (como Unity Random.insideUnitSphere).</summary>
        public static Vector3 insideUnitSphere => onUnitSphere * MathF.Cbrt(value);

        /// <summary>Punto aleatorio sobre la superficie de una esfera de radio 1 (como Unity Random.onUnitSphere).</summary>
        public static Vector3 onUnitSphere
        {
            get
            {
                float z = Range(-1f, 1f);
                float t = Range(0f, MathF.PI * 2f);
                float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
                return new Vector3(r * MathF.Cos(t), r * MathF.Sin(t), z);
            }
        }

        /// <summary>Punto aleatorio dentro de un círculo de radio 1 (como Unity Random.insideUnitCircle).</summary>
        public static Vector2 insideUnitCircle
        {
            get
            {
                float t = Range(0f, MathF.PI * 2f);
                float r = MathF.Sqrt(value);
                return new Vector2(MathF.Cos(t) * r, MathF.Sin(t) * r);
            }
        }

        /// <summary>Rotación uniformemente aleatoria (como Unity Random.rotation). Método de Shoemake.</summary>
        public static Quaternion rotation
        {
            get
            {
                float u1 = value, u2 = value, u3 = value;
                float sq1 = MathF.Sqrt(1f - u1), sq2 = MathF.Sqrt(u1);
                float t1 = MathF.PI * 2f * u2, t2 = MathF.PI * 2f * u3;
                return new Quaternion(sq1 * MathF.Sin(t1), sq1 * MathF.Cos(t1), sq2 * MathF.Sin(t2), sq2 * MathF.Cos(t2));
            }
        }
    }
}
