using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenTK.Audio.OpenAL;
using GrokoEngine;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine.ImGuiEditor
{
    /// <summary>
    /// Backend de audio con OpenAL Soft. Todas las llamadas AL corren en el hilo principal del
    /// editor (OnUpdateFrame). Cachea los clips (.wav) como buffers y mantiene una fuente OpenAL
    /// por cada <see cref="AudioSource"/> vivo. La DLL nativa (OpenAL32.dll) la copia el .csproj.
    /// </summary>
    internal sealed class OpenALAudioBackend : IAudioBackend, IDisposable
    {
        private ALDevice device;
        private ALContext context;
        private bool ok;

        // clipPath (absoluto) -> buffer AL. Se cachea incluso el fallo (0) para no reintentar en bucle.
        private readonly Dictionary<string, int> clipBuffers = new(StringComparer.OrdinalIgnoreCase);
        // AudioSource -> fuente AL persistente.
        private readonly Dictionary<AudioSource, int> sourceHandles = new();
        // Fuentes "one-shot" (fire and forget); se limpian cuando terminan.
        private readonly List<int> oneShots = new();

        public bool Available => ok;

        public OpenALAudioBackend()
        {
            try
            {
                device = ALC.OpenDevice(null);
                if (device == ALDevice.Null)
                {
                    EditorDiagnostics.Log("OpenAL: no se encontró dispositivo de audio.", "Warning");
                    return;
                }
                context = ALC.CreateContext(device, (int[])null!);
                if (!ALC.MakeContextCurrent(context))
                {
                    EditorDiagnostics.Log("OpenAL: MakeContextCurrent falló.", "Warning");
                    return;
                }
                // Atenuación lineal con recorte a MaxDistance (predecible, estilo motor de juego).
                AL.DistanceModel(ALDistanceModel.LinearDistanceClamped);
                ok = true;
                EditorDiagnostics.Log("OpenAL listo: " + AL.Get(ALGetString.Renderer));
            }
            catch (Exception ex)
            {
                EditorDiagnostics.Log("OpenAL init falló: " + ex.Message, "Error");
                ok = false;
            }
        }

        // ─────────────────────────── API IAudioBackend ───────────────────────────

        public void Play(AudioSource s)
        {
            if (!ok || s == null) return;
            int buffer = GetClipBuffer(s.ClipPath);
            if (buffer == 0) return;

            int src = GetOrCreateSource(s);
            AL.SourceStop(src);
            AL.Source(src, ALSourcei.Buffer, buffer);
            ApplyParams(s, src);
            AL.SourcePlay(src);
        }

        public void Stop(AudioSource s)
        {
            if (ok && s != null && sourceHandles.TryGetValue(s, out var src))
                AL.SourceStop(src);
        }

        public void Pause(AudioSource s)
        {
            if (ok && s != null && sourceHandles.TryGetValue(s, out var src))
                AL.SourcePause(src);
        }

        public bool IsPlaying(AudioSource s)
        {
            if (!ok || s == null || !sourceHandles.TryGetValue(s, out var src)) return false;
            return GetState(src) == ALSourceState.Playing;
        }

        public void PlayOneShot(string clipPath, float volume)
        {
            if (!ok) return;
            int buffer = GetClipBuffer(clipPath);
            if (buffer == 0) return;

            int src = AL.GenSource();
            AL.Source(src, ALSourcei.Buffer, buffer);
            AL.Source(src, ALSourcef.Gain, Math.Clamp(volume, 0f, 1f));
            AL.Source(src, ALSourceb.SourceRelative, true);   // 2D: pegado al listener
            AL.Source(src, ALSource3f.Position, 0f, 0f, 0f);
            AL.SourcePlay(src);
            oneShots.Add(src);
        }

        // ─────────────────────────── Conducción por frame ───────────────────────────

        public void SetListener(Vector3 pos, Vector3 forward, Vector3 up)
        {
            if (!ok) return;
            AL.Listener(ALListener3f.Position, pos.X, pos.Y, pos.Z);
            var ori = new[] { forward.X, forward.Y, forward.Z, up.X, up.Y, up.Z };
            AL.Listener(ALListenerfv.Orientation, ori);
        }

        // Sincroniza volumen/pitch/loop/posición de una fuente viva con su componente (por si el
        // usuario los cambió en el Inspector o el objeto se movió).
        public void Sync(AudioSource s)
        {
            if (!ok || s == null || !sourceHandles.TryGetValue(s, out var src)) return;
            if (GetState(src) != ALSourceState.Playing &&
                GetState(src) != ALSourceState.Paused) return;

            AL.Source(src, ALSourcef.Gain, Math.Clamp(s.Volume, 0f, 1f));
            AL.Source(src, ALSourcef.Pitch, Math.Clamp(s.Pitch, 0.1f, 4f));
            AL.Source(src, ALSourceb.Looping, s.Loop);
            if (s.Spatial)
            {
                var p = s.WorldPosition;
                AL.Source(src, ALSource3f.Position, p.X, p.Y, p.Z);
            }
        }

        // Limpieza de one-shots terminadas (una vez por frame).
        public void Update()
        {
            if (!ok || oneShots.Count == 0) return;
            for (int i = oneShots.Count - 1; i >= 0; i--)
            {
                int src = oneShots[i];
                if (!AL.IsSource(src) || GetState(src) == ALSourceState.Stopped)
                {
                    if (AL.IsSource(src)) AL.DeleteSource(src);
                    oneShots.RemoveAt(i);
                }
            }
        }

        // Detiene y libera TODAS las fuentes (al salir de Play: la escena se reconstruye y los
        // AudioSource actuales quedan obsoletos). Los buffers de clips se conservan en caché.
        public void ReleaseAllSources()
        {
            if (!ok) return;
            foreach (var src in sourceHandles.Values)
            {
                if (AL.IsSource(src)) { AL.SourceStop(src); AL.DeleteSource(src); }
            }
            sourceHandles.Clear();
            foreach (var src in oneShots)
                if (AL.IsSource(src)) { AL.SourceStop(src); AL.DeleteSource(src); }
            oneShots.Clear();
        }

        // ─────────────────────────── Preview (inspector / doble-clic) ───────────────────────────
        // Una única fuente 2D reutilizable para escuchar clips desde el editor, con Play/Stop.
        private int previewSource;

        public void PreviewPlay(string clipPath, float volume = 1f)
        {
            if (!ok) return;
            int buffer = GetClipBuffer(clipPath);
            if (buffer == 0) return;
            if (previewSource == 0 || !AL.IsSource(previewSource)) previewSource = AL.GenSource();
            AL.SourceStop(previewSource);
            AL.Source(previewSource, ALSourcei.Buffer, buffer);
            AL.Source(previewSource, ALSourcef.Gain, Math.Clamp(volume, 0f, 1f));
            AL.Source(previewSource, ALSourceb.Looping, false);
            AL.Source(previewSource, ALSourceb.SourceRelative, true);
            AL.Source(previewSource, ALSource3f.Position, 0f, 0f, 0f);
            AL.SourcePlay(previewSource);
        }

        public void PreviewStop()
        {
            if (ok && previewSource != 0 && AL.IsSource(previewSource)) AL.SourceStop(previewSource);
        }

        public bool IsPreviewPlaying()
            => ok && previewSource != 0 && AL.IsSource(previewSource) && GetState(previewSource) == ALSourceState.Playing;

        // ─────────────────────────── Interno ───────────────────────────

        private static ALSourceState GetState(int src)
        {
            AL.GetSource(src, ALGetSourcei.SourceState, out int state);
            return (ALSourceState)state;
        }

        private int GetOrCreateSource(AudioSource s)
        {
            if (sourceHandles.TryGetValue(s, out var src) && AL.IsSource(src))
                return src;

            src = AL.GenSource();
            sourceHandles[s] = src;
            s.BackendSource = src;
            return src;
        }

        private void ApplyParams(AudioSource s, int src)
        {
            AL.Source(src, ALSourcef.Gain, Math.Clamp(s.Volume, 0f, 1f));
            AL.Source(src, ALSourcef.Pitch, Math.Clamp(s.Pitch, 0.1f, 4f));
            AL.Source(src, ALSourceb.Looping, s.Loop);

            if (s.Spatial)
            {
                AL.Source(src, ALSourceb.SourceRelative, false);
                AL.Source(src, ALSourcef.ReferenceDistance, Math.Max(0.01f, s.MinDistance));
                AL.Source(src, ALSourcef.MaxDistance, Math.Max(s.MinDistance + 0.01f, s.MaxDistance));
                AL.Source(src, ALSourcef.RolloffFactor, 1f);
                var p = s.WorldPosition;
                AL.Source(src, ALSource3f.Position, p.X, p.Y, p.Z);
            }
            else
            {
                // 2D: fuente relativa al listener en el origen → sin atenuación ni paneo por distancia.
                AL.Source(src, ALSourceb.SourceRelative, true);
                AL.Source(src, ALSourcef.RolloffFactor, 0f);
                AL.Source(src, ALSource3f.Position, 0f, 0f, 0f);
            }
        }

        private int GetClipBuffer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;
            if (clipBuffers.TryGetValue(path, out var cached)) return cached;

            int buffer = 0;
            try
            {
                if (File.Exists(path) && LoadWav(path, out var data, out var format, out var rate) && data.Length > 0)
                {
                    buffer = AL.GenBuffer();
                    AL.BufferData(buffer, format, data, rate);
                    if (AL.GetError() != ALError.NoError)
                    {
                        AL.DeleteBuffer(buffer);
                        buffer = 0;
                    }
                }
                else
                {
                    EditorDiagnostics.Log($"Audio: no se pudo leer WAV '{path}' (¿formato PCM 8/16-bit?).", "Warning");
                }
            }
            catch (Exception ex)
            {
                EditorDiagnostics.Log($"Audio: fallo cargando '{path}': {ex.Message}", "Warning");
                buffer = 0;
            }

            clipBuffers[path] = buffer;   // cachear el fallo evita reintentos por frame
            return buffer;
        }

        // Invalida el buffer cacheado de un clip (p. ej. si el archivo cambió en disco).
        public void InvalidateClip(string path)
        {
            if (!ok || string.IsNullOrWhiteSpace(path)) return;
            if (clipBuffers.TryGetValue(path, out var buffer))
            {
                if (buffer != 0 && AL.IsBuffer(buffer)) AL.DeleteBuffer(buffer);
                clipBuffers.Remove(path);
            }
        }

        // ─────────────────────────── Loader de WAV (PCM 8/16-bit, mono/estéreo) ───────────────────────────

        private static bool LoadWav(string path, out byte[] data, out ALFormat format, out int sampleRate)
        {
            data = Array.Empty<byte>();
            format = ALFormat.Mono16;
            sampleRate = 44100;

            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            if (ReadTag(br) != "RIFF") return false;
            br.ReadInt32();                       // tamaño RIFF (ignorado)
            if (ReadTag(br) != "WAVE") return false;

            short channels = 0, bits = 0, audioFormat = 0;
            byte[]? pcm = null;

            while (fs.Position + 8 <= fs.Length)
            {
                string chunkId = ReadTag(br);
                int chunkSize = br.ReadInt32();
                if (chunkSize < 0) break;

                if (chunkId == "fmt ")
                {
                    audioFormat = br.ReadInt16();
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32();               // byte rate
                    br.ReadInt16();               // block align
                    bits = br.ReadInt16();
                    int rest = chunkSize - 16;
                    if (rest > 0) br.ReadBytes(rest);
                }
                else if (chunkId == "data")
                {
                    pcm = br.ReadBytes(chunkSize);
                }
                else
                {
                    fs.Seek(chunkSize, SeekOrigin.Current);
                }

                if ((chunkSize & 1) == 1 && fs.Position < fs.Length)
                    fs.Seek(1, SeekOrigin.Current);   // los chunks se alinean a 2 bytes
            }

            if (pcm == null || pcm.Length == 0 || channels == 0 || bits == 0) return false;
            if (audioFormat != 1) return false;       // solo PCM sin comprimir
            if (channels > 2 || (bits != 8 && bits != 16)) return false;

            format = (channels, bits) switch
            {
                (1, 8) => ALFormat.Mono8,
                (1, 16) => ALFormat.Mono16,
                (2, 8) => ALFormat.Stereo8,
                (2, 16) => ALFormat.Stereo16,
                _ => ALFormat.Mono16
            };
            data = pcm;
            return true;
        }

        private static string ReadTag(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : "";
        }

        public void Dispose()
        {
            if (!ok) return;
            ok = false;
            try
            {
                ReleaseAllSources();
                if (previewSource != 0 && AL.IsSource(previewSource)) { AL.SourceStop(previewSource); AL.DeleteSource(previewSource); }
                foreach (var buffer in clipBuffers.Values)
                    if (buffer != 0 && AL.IsBuffer(buffer)) AL.DeleteBuffer(buffer);
                clipBuffers.Clear();

                ALC.MakeContextCurrent(ALContext.Null);
                if (context != ALContext.Null) ALC.DestroyContext(context);
                if (device != ALDevice.Null) ALC.CloseDevice(device);
            }
            catch (Exception ex)
            {
                EditorDiagnostics.Log("OpenAL dispose falló: " + ex.Message, "Warning");
            }
        }
    }
}
