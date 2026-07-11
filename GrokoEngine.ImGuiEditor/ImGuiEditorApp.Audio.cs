using GrokoEngine;
using ImGuiNET;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    // ¿El tipo del campo es AudioClip[] o List<AudioClip>?
    private static bool IsAudioClipCollectionType(Type ft)
    {
        if (ft.IsArray) return ft.GetElementType() == typeof(AudioClip);
        return ft.IsGenericType
            && ft.GetGenericTypeDefinition() == typeof(List<>)
            && ft.GetGenericArguments()[0] == typeof(AudioClip);
    }

    // Dibuja un campo AudioClip[] / List<AudioClip>: lista de slots (arrastra un .wav a cada uno),
    // con botones para añadir/quitar. Igual que un array de AudioClip en Unity.
    private void DrawAudioClipCollectionField(FieldInfo field, object script, Type ft, Action? onChanged)
    {
        var list = new List<AudioClip>();
        if (field.GetValue(script) is IEnumerable existing)
            foreach (var it in existing)
                list.Add(it as AudioClip ?? new AudioClip(""));

        FieldRow(field.Name);
        ImGui.Text($"{list.Count} clip(s)");

        bool changed = false;
        int removeAt = -1;
        for (int i = 0; i < list.Count; i++)
        {
            int idx = i;
            ImGui.PushID(idx);
            DrawAssetSlot($"Element {idx}", list[idx]?.Path ?? "", "None (Audio Clip)",
                path => { list[idx] = new AudioClip(path); changed = true; },
                IsAudioAssetPath);
            ImGui.SameLine();
            if (ImGui.SmallButton("-")) removeAt = idx;
            ImGui.PopID();
        }

        if (removeAt >= 0) { list.RemoveAt(removeAt); changed = true; }
        if (ImGui.SmallButton("+ Add Clip")) { list.Add(new AudioClip("")); changed = true; }

        if (changed)
        {
            if (ft.IsArray) field.SetValue(script, list.ToArray());
            else field.SetValue(script, new List<AudioClip>(list));
            onChanged?.Invoke();
        }
    }
    // Clip reproducible (por ahora solo WAV; mp3/ogg necesitarían un decodificador).
    internal static bool IsAudioAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    // Reconocimiento amplio para el Project panel (icono/kind), aunque solo WAV se reproduzca.
    internal static bool IsAudioFileExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    // Escucha rápida de un clip desde el Project panel (usa la fuente de preview dedicada).
    private void PreviewAudioAsset(string path)
    {
        var backend = EnsureAudioBackend();
        if (backend == null || !backend.Available) return;
        if (!IsAudioAssetPath(path))
        {
            statusMessage = "Audio: por ahora solo se reproduce WAV (" + Path.GetFileName(path) + ")";
            return;
        }
        backend.PreviewPlay(path, 1f);
        statusMessage = "▶ " + Path.GetFileName(path);
    }

    // Inspector del asset AudioClip (al seleccionar un .wav): info + Play/Stop, estilo Unity.
    private void DrawAudioClipInspector(string path)
    {
        bool isWav = IsAudioAssetPath(path);
        if (isWav && TryReadWavInfo(path, out int channels, out int rate, out int bits, out double seconds))
        {
            ImGui.TextDisabled("Audio Clip");
            ImGui.Separator();
            AudioInfoRow("Length", $"{seconds:0.00} s");
            AudioInfoRow("Channels", channels == 1 ? "Mono" : channels == 2 ? "Stereo" : channels.ToString());
            AudioInfoRow("Sample Rate", $"{rate} Hz");
            AudioInfoRow("Bit Depth", $"{bits}-bit");
            if (channels > 1)
                ImGui.TextDisabled("Nota: para sonido 3D el clip debe ser Mono.");
        }
        else
        {
            ImGui.TextDisabled("Audio Clip");
            ImGui.Separator();
            ImGui.TextWrapped(isWav
                ? "No se pudo leer la cabecera WAV."
                : "Formato reconocido pero aún no reproducible (solo WAV por ahora).");
        }

        ImGui.Spacing();
        var backend = audioBackend;
        bool playing = backend != null && backend.Available && backend.IsPreviewPlaying();
        using (new DisabledScope(!isWav))
        {
            if (ImGui.Button(playing ? "■ Stop##clip" : "▶ Play##clip", new System.Numerics.Vector2(90f, 26f)))
            {
                var b = EnsureAudioBackend();
                if (b != null && b.Available)
                {
                    if (playing) b.PreviewStop();
                    else b.PreviewPlay(path, 1f);
                }
            }
        }
    }

    private static void AudioInfoRow(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(130f);
        ImGui.Text(value);
    }

    // Lee solo la cabecera del WAV (fmt + tamaño del chunk data) para sacar duración y formato.
    internal static bool TryReadWavInfo(string path, out int channels, out int sampleRate, out int bits, out double seconds)
    {
        channels = 0; sampleRate = 0; bits = 0; seconds = 0;
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (ReadTag4(br) != "RIFF") return false;
            br.ReadInt32();
            if (ReadTag4(br) != "WAVE") return false;

            int dataBytes = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = ReadTag4(br);
                int sz = br.ReadInt32();
                if (sz < 0) break;
                if (id == "fmt ")
                {
                    br.ReadInt16();                 // audioFormat
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32();                 // byte rate
                    br.ReadInt16();                 // block align
                    bits = br.ReadInt16();
                    int rest = sz - 16;
                    if (rest > 0) br.ReadBytes(rest);
                }
                else if (id == "data")
                {
                    dataBytes = sz;
                    fs.Seek(sz, SeekOrigin.Current);
                }
                else
                {
                    fs.Seek(sz, SeekOrigin.Current);
                }
                if ((sz & 1) == 1 && fs.Position < fs.Length) fs.Seek(1, SeekOrigin.Current);
            }

            if (channels <= 0 || sampleRate <= 0 || bits <= 0) return false;
            int bytesPerSecond = sampleRate * channels * (bits / 8);
            if (bytesPerSecond > 0 && dataBytes > 0) seconds = (double)dataBytes / bytesPerSecond;
            return true;
        }
        catch { return false; }
    }

    private static string ReadTag4(BinaryReader br)
    {
        var b = br.ReadBytes(4);
        return b.Length == 4 ? System.Text.Encoding.ASCII.GetString(b) : "";
    }

    private void DrawAudioSourceInspector(AudioSource audio)
    {
        DrawAssetSlot("Clip", audio.ClipPath, "None (Audio Clip .wav)",
            path => audio.ClipPath = path,
            IsAudioAssetPath);

        DrawSliderRow("Volume", audio.Volume, 0f, 1f, v => audio.Volume = v);
        DrawSliderRow("Pitch", audio.Pitch, 0.1f, 3f, v => audio.Pitch = v);
        DrawCheckRow("Loop", audio.Loop, v => audio.Loop = v);
        DrawCheckRow("Play On Awake", audio.PlayOnAwake, v => audio.PlayOnAwake = v);
        DrawCheckRow("Spatial (3D)", audio.Spatial, v => audio.Spatial = v);
        if (audio.Spatial)
        {
            DrawFloat("Min Distance", audio.MinDistance, v => audio.MinDistance = MathF.Max(0.01f, v), 0.1f, 0.01f, 100000f);
            DrawFloat("Max Distance", audio.MaxDistance, v => audio.MaxDistance = MathF.Max(audio.MinDistance + 0.01f, v), 0.5f, 0.02f, 100000f);
        }

        ImGui.Separator();
        bool hasClip = !string.IsNullOrWhiteSpace(audio.ClipPath);
        var backend = audioBackend;
        bool playing = backend != null && backend.Available && backend.IsPlaying(audio);
        using (new DisabledScope(!hasClip))
        {
            if (ImGui.SmallButton(playing ? "Stop##audioPrev" : "Preview##audioPrev"))
            {
                var b = EnsureAudioBackend();
                if (b != null && b.Available)
                {
                    if (playing) b.Stop(audio);
                    else b.Play(audio);
                }
            }
        }
        ImGui.SameLine();
        ImGui.TextDisabled(playing ? "Playing…" : (hasClip ? "" : "Assign a .wav clip"));
    }

    // Crea el backend OpenAL la primera vez que se necesita y lo publica en AudioEngine.Backend
    // para que scripts y componentes (AudioSource.Play, AudioEngine.PlayOneShot) lo usen.
    private OpenALAudioBackend? EnsureAudioBackend()
    {
        if (audioBackend == null)
        {
            audioBackend = new OpenALAudioBackend();
            if (audioBackend.Available)
                AudioEngine.Backend = audioBackend;
        }
        return audioBackend;
    }

    // Se llama una vez por frame: mueve el listener a la cámara activa y sincroniza cada
    // AudioSource vivo (posición 3D, volumen, pitch) + limpia one-shots terminadas.
    private void UpdateAudio()
    {
        var backend = audioBackend;
        if (backend == null || !backend.Available) return;

        GetListenerTransform(out var pos, out var forward, out var up);
        backend.SetListener(pos, forward, up);

        foreach (var obj in sceneGraph.Flatten())
        {
            if (!obj.IsActive) continue;
            foreach (var audio in obj.GetComponents<AudioSource>())
                backend.Sync(audio);
        }

        backend.Update();
    }

    // El AudioListener es la cámara: la del juego en Play (si existe), la del editor en edición.
    private void GetListenerTransform(out Vector3 pos, out Vector3 forward, out Vector3 up)
    {
        if (isPlaying)
        {
            var gameCamera = FindGameCamera(objects);
            if (gameCamera != null)
            {
                var m = gameCamera.WorldMatrix;   // System.Numerics.Matrix4x4
                pos = gameCamera.WorldPosition;
                forward = new Vector3(m.M31, m.M32, m.M33);   // eje +Z del objeto
                up = new Vector3(m.M21, m.M22, m.M23);        // eje +Y del objeto
                return;
            }
        }

        // Cámara del editor (OpenTK.Mathematics.Vector3 → MiMotor.Vector3).
        pos = new Vector3(camera.Position.X, camera.Position.Y, camera.Position.Z);
        forward = new Vector3(camera.Front.X, camera.Front.Y, camera.Front.Z);
        up = new Vector3(camera.Up.X, camera.Up.Y, camera.Up.Z);
    }

    // Al entrar en Play: arranca los AudioSource con PlayOnAwake (los componentes del motor no
    // reciben Start() del bucle de scripts de usuario, así que se dispara aquí, como en Unity).
    private void TriggerAudioPlayOnAwake()
    {
        var backend = EnsureAudioBackend();
        if (backend == null || !backend.Available) return;

        foreach (var obj in sceneGraph.Flatten())
        {
            if (!obj.IsActive) continue;
            foreach (var audio in obj.GetComponents<AudioSource>())
                if (audio.Enabled && audio.PlayOnAwake && !string.IsNullOrWhiteSpace(audio.ClipPath))
                    backend.Play(audio);
        }
    }

    // Al salir de Play: detiene y libera todas las fuentes (la escena se reconstruye).
    private void StopAllAudio() => audioBackend?.ReleaseAllSources();

    // Arrastrar un clip sobre un objeto: le añade (o reusa) un AudioSource con ese clip.
    private void AddAudioSourceToObject(GameObject obj, string clipPath)
    {
        CommitSceneMutation("Add Audio Source", () =>
        {
            var audio = obj.GetComponent<AudioSource>() ?? obj.AddComponent<AudioSource>();
            audio.ClipPath = clipPath;
            selected = obj;
            statusMessage = $"Audio Source → {obj.Name}: {Path.GetFileName(clipPath)}";
            return obj;
        });
    }

    // Arrastrar un clip a espacio vacío: crea un GameObject con un AudioSource en ese punto.
    private GameObject CreateAudioObjectAt(string clipPath, Vector3 position)
    {
        return CommitSceneMutation("Drop Audio In Viewport", () =>
        {
            var obj = CreateObject(Path.GetFileNameWithoutExtension(clipPath), 0);
            SetObjectPosition(obj, position);
            var audio = obj.AddComponent<AudioSource>();
            audio.ClipPath = clipPath;
            selected = obj;
            statusMessage = $"Audio dropped: {obj.Name}";
            return obj;
        });
    }
}
