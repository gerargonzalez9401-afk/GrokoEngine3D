using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    // ── Estructuras de datos para el bake (accesibles desde cualquier proyecto) ──

    public struct BakeDirectionalLight
    {
        public float X, Y, Z, R, G, B, Intensity;
    }

    public struct BakePointLight
    {
        public Vector3 Position;
        public float R, G, B, Intensity, Range;
    }

    public struct BakeAmbientLight
    {
        public float R, G, B, Intensity, SkyStrength;
    }

    /// <summary>Datos de iluminación para el lightmap baker, independientes de plataforma.</summary>
    public class BakeLightingInfo
    {
        public BakeDirectionalLight Directional = new()
            { X = 0.5f, Y = -1f, Z = 0.3f, R = 1f, G = 1f, B = 1f, Intensity = 1f };
        public BakeAmbientLight Ambient = new()
            { R = 0.3f, G = 0.3f, B = 0.35f, Intensity = 0.4f, SkyStrength = 0.2f };
        public List<BakePointLight> PointLights = new();
    }

    /// <summary>
    /// Bakeador de lightmaps offline — núcleo sin dependencias de plataforma.
    /// Recibe la malla ya cargada y los datos de iluminación.
    /// El proyecto editor (WPF/ImGui) construye BakeLightingInfo y llama BakeAsync.
    /// </summary>
    public class LightmapBaker
    {
        public const int    DefaultResolution = 256;
        public const string LightmapSuffix   = "_lm.bmp";

        private readonly string _outputDir;
        public event Action<string>? OnLog;
        public event Action<float>?  OnProgress;
        public Dictionary<string, string> BakedPaths { get; } = new();

        public LightmapBaker(string projectPath)
        {
            _outputDir = Path.Combine(projectPath, "Assets", "Lightmaps");
            Directory.CreateDirectory(_outputDir);
        }

        /// <summary>
        /// Bake de todos los objetos estáticos con MeshFilter.
        /// meshLoader: función que carga un ParsedMesh dado su path.
        /// </summary>
        // BakedMeshData: datos mínimos que necesitamos del mesh
        // (el editor lo llena desde ParsedMesh o cualquier otra fuente)
        public record BakedMeshData(float[] Positions, float[] Normals, float[] UVs, int TriangleCount);

        // Snapshot inmutable de todo lo que el bake necesita de un GameObject.
        // Se captura en el hilo principal para que el baking en segundo plano NO
        // toque el GameObject (cuyo cache de transform NO es thread-safe) ni el
        // cargador de mallas mientras el editor sigue renderizando en otro hilo.
        private readonly record struct BakeJob(
            string EditorId,
            string MeshName,
            BakedMeshData Mesh,
            float ImportScale,
            Vector3 WorldPos,
            Vector3 WorldScale,
            Vector3 WorldRot);

        public async Task BakeAsync(
            IReadOnlyList<GameObject> roots,
            BakeLightingInfo lighting,
            Func<string, BakedMeshData?> meshLoader,
            int resolution = DefaultResolution)
        {
            resolution = Math.Max(1, resolution);
            BakedPaths.Clear();
            var statics = new List<GameObject>();
            CollectStaticMeshes(roots, statics);
            OnLog?.Invoke($"[Lightmap] {statics.Count} objetos estáticos — {resolution}×{resolution}px");

            // ── Fase 1 (HILO PRINCIPAL) ──────────────────────────────────────
            // Cargamos las mallas y capturamos el transform de cada objeto AQUÍ,
            // antes de cualquier 'await Task.Run'. Así toda lectura del GameObject
            // y del cache de ObjLoader ocurre en el hilo de la UI (sin carreras
            // con el bucle de render). Es una operación corta porque las mallas
            // normalmente ya están en cache por el render.
            var jobs = new List<BakeJob>(statics.Count);
            foreach (var obj in statics)
            {
                var mf = obj.GetComponent<MeshFilter>();
                if (mf == null || string.IsNullOrEmpty(mf.MeshPath)) continue;
                var mesh = meshLoader(mf.MeshPath);
                if (mesh == null || mesh.TriangleCount == 0) continue;

                jobs.Add(new BakeJob(
                    obj.EditorId,
                    Path.GetFileName(mf.MeshPath),
                    mesh,
                    mf.ImportScale > 0f ? mf.ImportScale : 1f,
                    obj.WorldPosition,
                    obj.WorldScale,
                    new Vector3(obj.RotX, obj.RotY, obj.RotZ)));
            }

            if (jobs.Count == 0)
            {
                OnProgress?.Invoke(1f);
                OnLog?.Invoke("[Lightmap] Completado - 0 lightmaps.");
                return;
            }

            // ── Fase 2 (HILO DE FONDO) ───────────────────────────────────────
            // Baking puro de CPU sobre el snapshot: no toca GameObject, ObjLoader
            // ni OpenGL, así que es seguro en el thread pool.
            int done = 0;
            foreach (var job in jobs)
            {
                await Task.Run(() => BakeJobToFile(job, lighting, resolution));
                done++;
                OnProgress?.Invoke((float)done / jobs.Count);
            }
            OnLog?.Invoke($"[Lightmap] Completado — {BakedPaths.Count} lightmaps.");
        }

        private void BakeJobToFile(BakeJob job, BakeLightingInfo lighting, int res)
        {
            var mesh = job.Mesh;
            float s = job.ImportScale;
            var worldPos = job.WorldPos;
            var worldScale = job.WorldScale;
            var worldRot = job.WorldRot;

            // Buffer RGBA (R,G,B,A) en memoria — sin System.Drawing
            var pixels = new byte[res * res * 4];

            for (int t = 0; t < mesh.TriangleCount; t++)
            for (int v = 0; v < 3; v++)
            {
                int vi = t * 3 + v, pi = vi * 3, ui = vi * 2;
                if (pi + 2 >= mesh.Positions.Length) continue;

                var local = new Vector3(
                    mesh.Positions[pi]     * s * worldScale.X,
                    mesh.Positions[pi + 1] * s * worldScale.Y,
                    mesh.Positions[pi + 2] * s * worldScale.Z);
                var wp = worldPos + RotVec(local, worldRot);

                float nx = pi + 2 < mesh.Normals.Length ? mesh.Normals[pi]     : 0f;
                float ny = pi + 2 < mesh.Normals.Length ? mesh.Normals[pi + 1] : 1f;
                float nz = pi + 2 < mesh.Normals.Length ? mesh.Normals[pi + 2] : 0f;
                var wn = NormalizeSafe(RotVec(new Vector3(nx, ny, nz), worldRot));

                SampleLight(wp, wn, lighting, out float r, out float g, out float b);

                if (ui + 1 >= mesh.UVs.Length) continue;
                int px = (int)(Math.Clamp(mesh.UVs[ui],     0f, 1f) * (res - 1));
                int py = (int)((1f - Math.Clamp(mesh.UVs[ui+1], 0f, 1f)) * (res - 1));
                int idx = (py * res + px) * 4;
                pixels[idx]     = ToByte(r);
                pixels[idx + 1] = ToByte(g);
                pixels[idx + 2] = ToByte(b);
                pixels[idx + 3] = 255;
            }

            Dilate(pixels, res);

            string outPath = Path.Combine(_outputDir, Safe(job.EditorId) + LightmapSuffix);
            WriteBmp(pixels, res, res, outPath);
            lock (BakedPaths) BakedPaths[job.EditorId] = outPath;
            OnLog?.Invoke($"[Lightmap]   ✔ {job.MeshName}");
        }

        private static void SampleLight(Vector3 wp, Vector3 wn, BakeLightingInfo l,
                                         out float r, out float g, out float b)
        {
            float sky = Math.Max(0f, wn.Y) * l.Ambient.SkyStrength;
            r = l.Ambient.R * (l.Ambient.Intensity + sky);
            g = l.Ambient.G * (l.Ambient.Intensity + sky);
            b = l.Ambient.B * (l.Ambient.Intensity + sky);

            float ndl = Math.Max(0f, wn.X*l.Directional.X + wn.Y*l.Directional.Y + wn.Z*l.Directional.Z);
            r += l.Directional.R * l.Directional.Intensity * ndl;
            g += l.Directional.G * l.Directional.Intensity * ndl;
            b += l.Directional.B * l.Directional.Intensity * ndl;

            foreach (var pl in l.PointLights)
            {
                var toL = pl.Position - wp;
                float d = MathF.Sqrt(toL.X*toL.X + toL.Y*toL.Y + toL.Z*toL.Z);
                if (d <= 0.0001f || d > pl.Range) continue;
                var dir = new Vector3(toL.X/d, toL.Y/d, toL.Z/d);
                float n   = Math.Max(0f, wn.X*dir.X + wn.Y*dir.Y + wn.Z*dir.Z);
                float att = (1f - d/pl.Range) * (1f - d/pl.Range);
                r += pl.R * pl.Intensity * att * n;
                g += pl.G * pl.Intensity * att * n;
                b += pl.B * pl.Intensity * att * n;
            }
        }

        private static void Dilate(byte[] px, int res)
        {
            for (int p = 0; p < 3; p++)
            for (int y = 1; y < res-1; y++)
            for (int x = 1; x < res-1; x++)
            {
                int i = (y*res+x)*4;
                if (px[i+3] != 0) continue;
                foreach (var (dx,dy) in new[]{(0,-1),(0,1),(-1,0),(1,0)})
                { int n=(( y+dy)*res+(x+dx))*4; if(px[n+3]==0) continue; px[i]=px[n]; px[i+1]=px[n+1]; px[i+2]=px[n+2]; px[i+3]=255; break; }
            }
        }

        // BMP 24bpp — soportado por todos los cargadores de imagen de OpenGL
        private static void WriteBmp(byte[] rgba, int w, int h, string path)
        {
            int rowBytes = ((w * 3 + 3) / 4) * 4; // filas alineadas a 4 bytes
            int dataSize = rowBytes * h;
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new System.IO.BinaryWriter(fs);
            bw.Write((ushort)0x4D42);
            bw.Write(54 + dataSize);
            bw.Write((int)0);
            bw.Write(54);
            bw.Write(40); bw.Write(w); bw.Write(h);
            bw.Write((ushort)1); bw.Write((ushort)24);
            bw.Write(0); bw.Write(dataSize);
            bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
            // BMP: bottom-up, BGR
            for (int y = h-1; y >= 0; y--)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y*w+x)*4;
                    bw.Write(rgba[i+2]); bw.Write(rgba[i+1]); bw.Write(rgba[i]);
                }
                for (int p = 0; p < rowBytes - w*3; p++) bw.Write((byte)0);
            }
        }

        private static byte ToByte(float v) => (byte)(Math.Clamp(v,0f,1f)*255);

        private static void CollectStaticMeshes(IEnumerable<GameObject> objs, List<GameObject> r)
        { foreach (var o in objs) { if (o.IsStatic && o.GetComponent<MeshFilter>()!=null) r.Add(o); CollectStaticMeshes(o.Children, r); } }

        private static string Safe(string id) => string.Concat(id.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

        private static Vector3 NormalizeSafe(Vector3 v)
        {
            float length = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            return length <= 0.00001f
                ? new Vector3(0f, 1f, 0f)
                : new Vector3(v.X / length, v.Y / length, v.Z / length);
        }

        private static Vector3 RotVec(Vector3 v, Vector3 rot)
        {
            float z=rot.Z*(MathF.PI/180f),cz=MathF.Cos(z),sz=MathF.Sin(z);
            v=new Vector3(v.X*cz-v.Y*sz,v.X*sz+v.Y*cz,v.Z);
            float x=rot.X*(MathF.PI/180f),cx=MathF.Cos(x),sx=MathF.Sin(x);
            v=new Vector3(v.X,v.Y*cx-v.Z*sx,v.Y*sx+v.Z*cx);
            float y=rot.Y*(MathF.PI/180f),cy=MathF.Cos(y),sy=MathF.Sin(y);
            return new Vector3(v.X*cy+v.Z*sy,v.Y,-v.X*sy+v.Z*cy);
        }
    }
}
