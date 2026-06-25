using System;

namespace GrokoEngine
{
    /// <summary>
    /// Tiempo global del motor, estilo Unity. El bucle de juego lo avanza una vez por
    /// frame con el delta real; los componentes (Animator) y scripts lo leen.
    /// </summary>
    public static class Time
    {
        /// <summary>Escala del tiempo: 1 = normal, 0 = pausa, 0.5 = cámara lenta, 2 = x2.</summary>
        public static float TimeScale = 1f;

        /// <summary>Delta del frame en segundos, ya escalado por <see cref="TimeScale"/>.</summary>
        public static float DeltaTime { get; private set; }

        /// <summary>Delta del frame real, SIN escalar (ignora TimeScale). Para UI/animación en pausa.</summary>
        public static float UnscaledDeltaTime { get; private set; }

        /// <summary>Tiempo total acumulado (escalado) desde que arrancó el Play, en segundos.</summary>
        public static float TimeSinceStart { get; private set; }

        /// <summary>Número de frames simulados desde el arranque del Play.</summary>
        public static long FrameCount { get; private set; }

        /// <summary>Lo llama el bucle de juego una vez por frame con el delta REAL (sin escalar).</summary>
        public static void Advance(double rawDeltaSeconds)
        {
            UnscaledDeltaTime = (float)Math.Max(0.0, rawDeltaSeconds);
            DeltaTime = UnscaledDeltaTime * MathF.Max(0f, TimeScale);
            TimeSinceStart += DeltaTime;
            FrameCount++;
        }

        /// <summary>Reinicia los contadores (al entrar a Play Mode).</summary>
        public static void Reset()
        {
            DeltaTime = 0f;
            UnscaledDeltaTime = 0f;
            TimeSinceStart = 0f;
            FrameCount = 0;
        }
    }
}
