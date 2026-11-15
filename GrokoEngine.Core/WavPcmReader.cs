using System;
using System.IO;
using System.Text;

namespace GrokoEngine
{
    public readonly record struct WavPcmData(byte[] Samples, int SampleRate, short Channels, short BitsPerSample);

    /// <summary>Lee WAV RIFF PCM sin depender de la API de audio de la plataforma.</summary>
    public static class WavPcmReader
    {
        public static bool TryRead(string path, out WavPcmData wav, out string error)
        {
            wav = default;
            error = "";
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
                if (ReadTag(reader) != "RIFF" || stream.Length < 12)
                {
                    error = "Missing RIFF header.";
                    return false;
                }

                _ = reader.ReadInt32();
                if (ReadTag(reader) != "WAVE")
                {
                    error = "Missing WAVE signature.";
                    return false;
                }

                short audioFormat = 0;
                short channels = 0;
                short bits = 0;
                int sampleRate = 0;
                byte[]? samples = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunkId = ReadTag(reader);
                    int chunkSize = reader.ReadInt32();
                    if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                    {
                        error = $"Invalid '{chunkId}' chunk size.";
                        return false;
                    }

                    if (chunkId == "fmt ")
                    {
                        if (chunkSize < 16)
                        {
                            error = "Invalid fmt chunk.";
                            return false;
                        }

                        audioFormat = reader.ReadInt16();
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        _ = reader.ReadInt32();
                        _ = reader.ReadInt16();
                        bits = reader.ReadInt16();
                        Skip(reader, chunkSize - 16);
                    }
                    else if (chunkId == "data")
                    {
                        samples = reader.ReadBytes(chunkSize);
                    }
                    else
                    {
                        Skip(reader, chunkSize);
                    }

                    if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                        stream.Seek(1, SeekOrigin.Current);
                }

                if (audioFormat != 1)
                {
                    error = "Only uncompressed PCM WAV is supported.";
                    return false;
                }
                if (channels is < 1 or > 2 || bits is not (8 or 16) || sampleRate <= 0)
                {
                    error = "Only mono/stereo PCM 8/16-bit WAV is supported.";
                    return false;
                }
                if (samples is null || samples.Length == 0)
                {
                    error = "WAV contains no audio data.";
                    return false;
                }

                wav = new WavPcmData(samples, sampleRate, channels, bits);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void Skip(BinaryReader reader, int bytes)
        {
            if (bytes > 0)
                reader.BaseStream.Seek(bytes, SeekOrigin.Current);
        }

        private static string ReadTag(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : "";
        }
    }
}
