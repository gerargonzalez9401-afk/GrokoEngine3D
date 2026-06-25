using System;
using System.IO;
using System.Text;

namespace GrokoEngine
{
    /// <summary>
    /// Imagen HDR equirectangular decodificada a float lineal (RGB por píxel).
    /// </summary>
    public sealed class HdrImage
    {
        public int Width;
        public int Height;
        /// <summary>RGB lineal, 3 floats por píxel, fila por fila de arriba hacia abajo.</summary>
        public float[] Pixels = Array.Empty<float>();
    }

    /// <summary>
    /// Cargador de imágenes Radiance .hdr (RGBE), sin dependencias externas.
    /// Soporta el header ASCII, la resolución "-Y alto +X ancho" y tanto el
    /// formato plano como el RLE adaptativo nuevo (el que usan casi todos los
    /// HDRIs modernos, p. ej. polyhaven). Decodifica a RGB float lineal.
    /// </summary>
    public static class HdrLoader
    {
        public static string LastError { get; private set; } = "";

        public static bool IsHdr(string path) =>
            path.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".pic", StringComparison.OrdinalIgnoreCase);

        public static HdrImage? Load(string path)
        {
            LastError = "";
            if (!File.Exists(path)) { LastError = "HDR no encontrado: " + path; return null; }
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                return Parse(fs);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        public static HdrImage Parse(Stream stream)
        {
            // ── Header ASCII (termina en línea en blanco) ──
            string magic = ReadLine(stream);
            if (!magic.StartsWith("#?", StringComparison.Ordinal))
                throw new InvalidDataException("No es un archivo Radiance HDR (falta '#?').");

            string format = "";
            while (true)
            {
                string line = ReadLine(stream);
                if (line.Length == 0) break;                 // línea en blanco = fin del header
                if (line.StartsWith("FORMAT=", StringComparison.OrdinalIgnoreCase))
                    format = line.Substring(7).Trim();
                // EXPOSURE/GAMMA/comentarios se ignoran.
            }
            if (format.Length != 0 &&
                !format.Equals("32-bit_rle_rgbe", StringComparison.OrdinalIgnoreCase) &&
                !format.Equals("32-bit_rle_xyze", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FORMAT HDR no soportado: " + format);

            // ── Resolución: "-Y alto +X ancho" (orientación estándar) ──
            string res = ReadLine(stream);
            var parts = res.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4)
                throw new InvalidDataException("Línea de resolución HDR inválida: " + res);
            // Aceptamos -Y H +X W (lo habitual). Otras orientaciones se tratan igual
            // en cuanto a tamaño; el volteo fino no es crítico para un entorno.
            int height = int.Parse(parts[1]);
            int width  = int.Parse(parts[3]);
            if (width <= 0 || height <= 0 || (long)width * height > 200_000_000L)
                throw new InvalidDataException($"Dimensiones HDR inválidas: {width}x{height}");

            var pixels = new float[width * height * 3];
            var scan = new byte[width * 4]; // RGBE de una fila

            for (int y = 0; y < height; y++)
            {
                ReadScanline(stream, scan, width);
                int row = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    byte r = scan[x * 4 + 0];
                    byte g = scan[x * 4 + 1];
                    byte b = scan[x * 4 + 2];
                    byte e = scan[x * 4 + 3];
                    int o = row + x * 3;
                    if (e == 0)
                    {
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = 0f;
                    }
                    else
                    {
                        // RGBE -> float lineal: f = 2^(E-128) / 256
                        float f = MathF.Pow(2f, e - 128) / 256f;
                        pixels[o]     = r * f;
                        pixels[o + 1] = g * f;
                        pixels[o + 2] = b * f;
                    }
                }
            }

            return new HdrImage { Width = width, Height = height, Pixels = pixels };
        }

        // Lee una scanline RGBE (formato plano o RLE adaptativo nuevo) en 'scan'
        // como [R,G,B,E] intercalado por píxel.
        private static void ReadScanline(Stream s, byte[] scan, int width)
        {
            // Las primeras 4 bytes deciden el formato de la fila.
            int b0 = s.ReadByte();
            int b1 = s.ReadByte();
            int b2 = s.ReadByte();
            int b3 = s.ReadByte();
            if (b0 < 0 || b1 < 0 || b2 < 0 || b3 < 0)
                throw new EndOfStreamException("HDR truncado en la cabecera de scanline.");

            bool newRle = b0 == 2 && b1 == 2 && (b2 & 0x80) == 0 && ((b2 << 8) | b3) == width;
            if (!newRle || width < 8 || width > 32767)
            {
                // Formato plano (o RLE antiguo): los 4 bytes leídos son el primer
                // píxel; el resto se lee crudo. (No manejamos el RLE antiguo con
                // repeticiones; es rarísimo en HDRIs modernos.)
                scan[0] = (byte)b0; scan[1] = (byte)b1; scan[2] = (byte)b2; scan[3] = (byte)b3;
                ReadFull(s, scan, 4, width * 4 - 4);
                return;
            }

            // ── RLE adaptativo nuevo: 4 canales, cada uno RLE por separado ──
            for (int ch = 0; ch < 4; ch++)
            {
                int x = 0;
                while (x < width)
                {
                    int count = s.ReadByte();
                    if (count < 0) throw new EndOfStreamException("HDR truncado en RLE.");
                    if (count > 128)
                    {
                        // Run: (count-128) repeticiones del siguiente byte.
                        int run = count - 128;
                        int val = s.ReadByte();
                        if (val < 0) throw new EndOfStreamException("HDR truncado en run RLE.");
                        if (x + run > width) throw new InvalidDataException("Run RLE fuera de rango.");
                        for (int i = 0; i < run; i++)
                            scan[(x++) * 4 + ch] = (byte)val;
                    }
                    else
                    {
                        // Literal: 'count' bytes tal cual.
                        if (count == 0 || x + count > width) throw new InvalidDataException("Literal RLE inválido.");
                        for (int i = 0; i < count; i++)
                        {
                            int val = s.ReadByte();
                            if (val < 0) throw new EndOfStreamException("HDR truncado en literal RLE.");
                            scan[(x++) * 4 + ch] = (byte)val;
                        }
                    }
                }
            }
        }

        private static void ReadFull(Stream s, byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buffer, offset + read, count - read);
                if (n <= 0) throw new EndOfStreamException("HDR truncado.");
                read += n;
            }
        }

        private static string ReadLine(Stream s)
        {
            var sb = new StringBuilder(64);
            int c;
            while ((c = s.ReadByte()) >= 0)
            {
                if (c == '\n') break;
                if (c == '\r') continue;
                sb.Append((char)c);
            }
            return sb.ToString();
        }
    }
}
