using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace GrokoEngine.ImGuiEditor;

internal readonly record struct RgbaImage(int Width, int Height, byte[] Pixels);

internal static class EditorImageLoader
{
    public static bool TryLoadRgba(string path, out RgbaImage image)
    {
        image = default;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        string ext = Path.GetExtension(path);
        try
        {
            if (ext.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                image = LoadTga(path);
                return true;
            }

            using var bitmap = LoadBitmapWithGdi(path);
            image = BitmapToRgba(bitmap);
            return true;
        }
        catch
        {
            image = default;
            return false;
        }
    }

    public static Bitmap? LoadBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                return RgbaToBitmap(LoadTga(path));

            return LoadBitmapWithGdi(path);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap LoadBitmapWithGdi(string path)
    {
        using var original = new Bitmap(path);
        return original.PixelFormat == PixelFormat.Format32bppArgb
            ? new Bitmap(original)
            : original.Clone(new Rectangle(0, 0, original.Width, original.Height), PixelFormat.Format32bppArgb);
    }

    private static RgbaImage BitmapToRgba(Bitmap source)
    {
        using var bitmap = source.PixelFormat == PixelFormat.Format32bppArgb
            ? new Bitmap(source)
            : source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int length = bitmap.Width * bitmap.Height * 4;
            byte[] bgra = new byte[length];
            Marshal.Copy(data.Scan0, bgra, 0, length);

            byte[] rgba = new byte[length];
            for (int i = 0; i < length; i += 4)
            {
                rgba[i + 0] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i + 0];
                rgba[i + 3] = bgra[i + 3];
            }

            return new RgbaImage(bitmap.Width, bitmap.Height, rgba);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap RgbaToBitmap(RgbaImage image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, image.Width, image.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[image.Pixels.Length];
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                bgra[i + 0] = image.Pixels[i + 2];
                bgra[i + 1] = image.Pixels[i + 1];
                bgra[i + 2] = image.Pixels[i + 0];
                bgra[i + 3] = image.Pixels[i + 3];
            }

            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            return bitmap;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static RgbaImage LoadTga(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 18)
            throw new InvalidDataException("TGA header incompleto.");

        int idLength = bytes[0];
        int colorMapType = bytes[1];
        int imageType = bytes[2];
        int colorMapLength = bytes[5] | (bytes[6] << 8);
        int colorMapDepth = bytes[7];
        int width = bytes[12] | (bytes[13] << 8);
        int height = bytes[14] | (bytes[15] << 8);
        int bitsPerPixel = bytes[16];
        int descriptor = bytes[17];

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("TGA con dimensiones invalidas.");

        bool supportedType = imageType is 2 or 3 or 10 or 11;
        if (!supportedType)
            throw new NotSupportedException($"TGA type {imageType} no soportado.");

        int bytesPerPixel = bitsPerPixel / 8;
        if (bytesPerPixel is not (1 or 3 or 4))
            throw new NotSupportedException($"TGA {bitsPerPixel} bpp no soportado.");

        int colorMapBytes = colorMapType == 0 ? 0 : colorMapLength * Math.Max(1, colorMapDepth / 8);
        int offset = 18 + idLength + colorMapBytes;
        if (offset >= bytes.Length)
            throw new InvalidDataException("TGA sin datos de imagen.");

        bool topOrigin = (descriptor & 0x20) != 0;
        bool rightOrigin = (descriptor & 0x10) != 0;
        byte[] pixels = new byte[checked(width * height * 4)];

        int pixelIndex = 0;
        int totalPixels = width * height;
        bool rle = imageType is 10 or 11;

        if (rle)
        {
            while (pixelIndex < totalPixels && offset < bytes.Length)
            {
                int packet = bytes[offset++];
                int count = (packet & 0x7F) + 1;
                bool repeated = (packet & 0x80) != 0;

                if (repeated)
                {
                    ReadTgaPixel(bytes, ref offset, bytesPerPixel, out byte r, out byte g, out byte b, out byte a);
                    for (int i = 0; i < count && pixelIndex < totalPixels; i++)
                        WriteTgaPixel(pixels, width, height, pixelIndex++, topOrigin, rightOrigin, r, g, b, a);
                }
                else
                {
                    for (int i = 0; i < count && pixelIndex < totalPixels; i++)
                    {
                        ReadTgaPixel(bytes, ref offset, bytesPerPixel, out byte r, out byte g, out byte b, out byte a);
                        WriteTgaPixel(pixels, width, height, pixelIndex++, topOrigin, rightOrigin, r, g, b, a);
                    }
                }
            }
        }
        else
        {
            while (pixelIndex < totalPixels)
            {
                ReadTgaPixel(bytes, ref offset, bytesPerPixel, out byte r, out byte g, out byte b, out byte a);
                WriteTgaPixel(pixels, width, height, pixelIndex++, topOrigin, rightOrigin, r, g, b, a);
            }
        }

        return new RgbaImage(width, height, pixels);
    }

    private static void ReadTgaPixel(byte[] bytes, ref int offset, int bytesPerPixel, out byte r, out byte g, out byte b, out byte a)
    {
        if (offset + bytesPerPixel > bytes.Length)
            throw new EndOfStreamException("TGA pixel data incompleto.");

        if (bytesPerPixel == 1)
        {
            r = g = b = bytes[offset++];
            a = 255;
            return;
        }

        b = bytes[offset++];
        g = bytes[offset++];
        r = bytes[offset++];
        a = bytesPerPixel == 4 ? bytes[offset++] : (byte)255;
    }

    private static void WriteTgaPixel(byte[] pixels, int width, int height, int sourceIndex, bool topOrigin, bool rightOrigin, byte r, byte g, byte b, byte a)
    {
        int sx = sourceIndex % width;
        int sy = sourceIndex / width;
        int dx = rightOrigin ? width - 1 - sx : sx;
        int dy = topOrigin ? sy : height - 1 - sy;
        int dst = ((dy * width) + dx) * 4;

        pixels[dst + 0] = r;
        pixels[dst + 1] = g;
        pixels[dst + 2] = b;
        pixels[dst + 3] = a;
    }
}
