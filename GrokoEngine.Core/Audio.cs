using System;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine
{
    // =====================================================
    // AUDIO
    // =====================================================
    // Core no referencia OpenTK, así que la implementación real (OpenAL) vive en el editor/host.
    // Componentes y scripts hablan solo con AudioEngine; el host asigna AudioEngine.Backend.
    public interface IAudioBackend
    {
        void Play(AudioSource source);
        void Stop(AudioSource source);
        void Pause(AudioSource source);
        bool IsPlaying(AudioSource source);
        // Dispara un clip suelto (2D) sin necesitar un AudioSource; útil desde scripts (ej. un click).
        void PlayOneShot(string clipPath, float volume);
    }

    // Punto de acceso estático. Si no hay backend (p. ej. tests headless), todo es no-op silencioso.
    public static class AudioEngine
    {
        public static IAudioBackend? Backend;

        public static void Play(AudioSource s) => Backend?.Play(s);
        public static void Stop(AudioSource s) => Backend?.Stop(s);
        public static void Pause(AudioSource s) => Backend?.Pause(s);
        public static bool IsPlaying(AudioSource s) => Backend?.IsPlaying(s) ?? false;
        public static void PlayOneShot(string clipPath, float volume = 1f) => Backend?.PlayOneShot(clipPath, volume);
    }

    /// <summary>
    /// Referencia a un clip de audio (el archivo .wav importado), estilo Unity AudioClip.
    /// Se puede declarar como campo público en un script (<c>public AudioClip disparo;</c>),
    /// arrastrarle un .wav en el Inspector y usarlo por código: <c>source.PlayOneShot(disparo);</c>
    /// </summary>
    public sealed class AudioClip
    {
        // Ruta al .wav. Termina en "Path" no, pero el serializador lo trata aparte (referencia de asset).
        public string Path = "";

        public AudioClip() { }
        public AudioClip(string path) { Path = path ?? ""; }

        public bool IsValid => !string.IsNullOrWhiteSpace(Path);
        public string Name => IsValid ? System.IO.Path.GetFileNameWithoutExtension(Path) : "None";
        public override string ToString() => Name;
    }

    /// <summary>
    /// Fuente de audio estilo Unity. Reproduce un clip (.wav) de forma posicional 3D (según la
    /// posición del GameObject respecto al AudioListener) o en 2D (igual en todo el mapa).
    /// </summary>
    public class AudioSource : Component
    {
        // Ruta del clip (.wav). El nombre termina en "Path" a propósito: el serializador lo guarda
        // como referencia relativa al proyecto (portátil si se mueve la carpeta).
        public string ClipPath = "";

        public float Volume = 1f;        // 0..1
        public float Pitch = 1f;         // 1 = normal; <1 grave, >1 agudo
        public bool Loop = false;
        public bool PlayOnAwake = true;  // arranca solo al entrar en Play
        public bool Spatial = true;      // true = 3D posicional (clip mono); false = 2D

        public float MinDistance = 1f;   // dentro de este radio suena a volumen pleno
        public float MaxDistance = 50f;  // más allá deja de oírse (atenuación lineal)

        // Handle de la fuente OpenAL viva. Lo gestiona el backend; no se serializa.
        [NonSerialized] public int BackendSource;

        // Clip asignado, estilo Unity (source.clip = miClip). Envuelve/expone ClipPath.
        public AudioClip Clip
        {
            get => new AudioClip(ClipPath);
            set => ClipPath = value?.Path ?? "";
        }

        public void Play() => AudioEngine.Play(this);
        public void Stop() => AudioEngine.Stop(this);
        public void Pause() => AudioEngine.Pause(this);
        public bool IsPlaying => AudioEngine.IsPlaying(this);

        // Dispara un clip puntual encima del que ya suena, sin interrumpirlo (como Unity).
        public void PlayOneShot(AudioClip clip, float volumeScale = 1f)
        {
            if (clip != null && clip.IsValid)
                AudioEngine.PlayOneShot(clip.Path, Math.Clamp(volumeScale, 0f, 1f) * Volume);
        }

        // Posición mundial de la fuente (la del GameObject). El backend la lee cada frame.
        public Vector3 WorldPosition => gameObject != null ? gameObject.WorldPosition : Vector3.Zero;
    }
}
