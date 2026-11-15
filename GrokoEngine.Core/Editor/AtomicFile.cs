using System;
using System.IO;
using System.Text;

namespace GrokoEngine
{
    /// <summary>
    /// Persiste texto sin exponer el archivo de destino a escrituras parciales.
    /// El temporal se crea en el mismo volumen para que el reemplazo sea atómico
    /// en los sistemas de archivos que lo soportan.
    /// </summary>
    public static class AtomicFile
    {
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static void WriteAllText(string path, string contents)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(contents);

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new IOException($"No se pudo determinar el directorio de '{path}'.");

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8WithoutBom))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                ReplaceTemporaryFile(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public static void WriteAllBytes(string path, byte[] contents)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(contents);

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new IOException($"No se pudo determinar el directorio de '{path}'.");

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(flushToDisk: true);
                }

                ReplaceTemporaryFile(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static void ReplaceTemporaryFile(string temporaryPath, string fullPath)
        {
            if (File.Exists(fullPath))
            {
                try
                {
                    File.Replace(temporaryPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, fullPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
    }
}
