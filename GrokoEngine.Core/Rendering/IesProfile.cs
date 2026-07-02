using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GrokoEngine
{
    // Parser real del formato fotometrico IESNA LM-63 (el estandar de la industria: el mismo que
    // exportan DIALux, luminarias reales de fabricantes, etc). Produce una curva de caida angular
    // 1D normalizada (pico=1), pensada para modular la intensidad de un spot/point light real-time
    // segun el angulo respecto al eje del foco (mucho mas fiel que el cono uniforme por defecto).
    //
    // Limite de alcance deliberado: solo soporta TILT=NONE (la inmensa mayoria de archivos .ies de
    // luminarias comerciales), y asume/promedia simetria axial entre angulos horizontales (fotometria
    // tipo C simetrica). Fotometrias con variacion horizontal fuerte (p.ej. luminarias muy asimetricas
    // con TILT=INCLUDE) no se soportan: Parse devuelve null y el llamador cae al cono uniforme normal.
    public sealed class IesProfile
    {
        public const int Resolution = 256;

        // 256 muestras, angulo 0..180 grados desde el eje del foco, normalizado a pico=1.
        public float[] AngularFalloff { get; }

        private IesProfile(float[] falloff)
        {
            AngularFalloff = falloff;
        }

        public static IesProfile? Parse(string path)
        {
            try
            {
                return ParseLines(File.ReadAllLines(path));
            }
            catch
            {
                return null;
            }
        }

        // Testeable sin tocar disco.
        internal static IesProfile? ParseLines(string[] lines)
        {
            int tiltLineIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("TILT=", StringComparison.OrdinalIgnoreCase))
                {
                    tiltLineIndex = i;
                    break;
                }
            }
            if (tiltLineIndex < 0)
                return null;

            string tiltValue = lines[tiltLineIndex].Trim().Substring("TILT=".Length).Trim();
            if (!string.Equals(tiltValue, "NONE", StringComparison.OrdinalIgnoreCase))
                return null; // TILT=INCLUDE / TILT=<archivo>: fuera de alcance

            // El bloque numerico de LM-63 puede partir valores entre saltos de linea sin previo aviso;
            // se tokeniza TODO lo que sigue a la linea TILT como una sola secuencia de numeros.
            string rest = string.Join(" ", lines.Skip(tiltLineIndex + 1));
            string[] tokens = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int idx = 0;

            bool TryNextFloat(out float value)
            {
                value = 0f;
                if (idx >= tokens.Length) return false;
                bool ok = float.TryParse(tokens[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                idx++;
                return ok;
            }

            bool TryNextInt(out int value)
            {
                value = 0;
                if (!TryNextFloat(out float f)) return false;
                value = (int)MathF.Round(f);
                return true;
            }

            if (!TryNextInt(out _)          // numero de lamparas
                || !TryNextFloat(out _)     // lumenes por lampara
                || !TryNextFloat(out float candelaMultiplier)
                || !TryNextInt(out int numVertical)
                || !TryNextInt(out int numHorizontal)
                || !TryNextInt(out _)       // tipo fotometrico
                || !TryNextInt(out _)       // tipo de unidades
                || !TryNextFloat(out _)     // ancho luminoso
                || !TryNextFloat(out _)     // largo luminoso
                || !TryNextFloat(out _))    // alto luminoso
                return null;

            // ballast factor, future use, input watts
            if (!TryNextFloat(out _) || !TryNextFloat(out _) || !TryNextFloat(out _))
                return null;

            if (numVertical <= 0 || numHorizontal <= 0)
                return null;

            var verticalAngles = new float[numVertical];
            for (int i = 0; i < numVertical; i++)
                if (!TryNextFloat(out verticalAngles[i])) return null;

            var horizontalAngles = new float[numHorizontal];
            for (int i = 0; i < numHorizontal; i++)
                if (!TryNextFloat(out horizontalAngles[i])) return null;

            // Grid candela[h][v], promediado en horizontal (simetria axial asumida) -> 1 curva por vertical.
            var avgPerVertical = new float[numVertical];
            for (int h = 0; h < numHorizontal; h++)
            {
                for (int v = 0; v < numVertical; v++)
                {
                    if (!TryNextFloat(out float raw)) return null;
                    avgPerVertical[v] += raw * candelaMultiplier;
                }
            }
            for (int v = 0; v < numVertical; v++)
                avgPerVertical[v] /= numHorizontal;

            float peak = avgPerVertical.Length > 0 ? avgPerVertical.Max() : 0f;
            if (peak <= 0f)
                peak = 1f;

            var falloff = new float[Resolution];
            for (int i = 0; i < Resolution; i++)
            {
                float angle = i / (float)(Resolution - 1) * 180f;
                falloff[i] = SampleAt(verticalAngles, avgPerVertical, angle) / peak;
            }

            return new IesProfile(falloff);
        }

        private static float SampleAt(float[] angles, float[] values, float angle)
        {
            if (angles.Length == 0)
                return 0f;
            if (angle <= angles[0])
                return values[0];
            if (angle >= angles[^1])
                return values[^1];

            for (int i = 0; i < angles.Length - 1; i++)
            {
                if (angle >= angles[i] && angle <= angles[i + 1])
                {
                    float span = angles[i + 1] - angles[i];
                    float t = span > 0f ? (angle - angles[i]) / span : 0f;
                    return values[i] + (values[i + 1] - values[i]) * t;
                }
            }

            return values[^1];
        }
    }
}
