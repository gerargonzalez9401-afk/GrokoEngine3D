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
        public bool BakeDirect;
        public bool BakeIndirect;
        public int ShadowmaskChannel;
    }

    public struct BakePointLight
    {
        public Vector3 Position;
        public float R, G, B, Intensity, Range;
        public bool BakeDirect;
        public bool BakeIndirect;
        public int ShadowmaskChannel;
    }

    public struct BakeSpotLight
    {
        public Vector3 Position, Direction;
        public float R, G, B, Intensity, Range, OuterAngleDegrees;
        public bool BakeDirect;
        public bool BakeIndirect;
        public int ShadowmaskChannel;
    }

    public struct BakeAmbientLight
    {
        public float R, G, B, Intensity, SkyStrength;
    }

    /// <summary>Datos de iluminación para el lightmap baker, independientes de plataforma.</summary>
    public class BakeLightingInfo
    {
        public BakeDirectionalLight Directional = new()
            { X = 0.5f, Y = -1f, Z = 0.3f, R = 1f, G = 1f, B = 1f, Intensity = 0f };
        public BakeAmbientLight Ambient = new()
            { R = 0.3f, G = 0.3f, B = 0.35f, Intensity = 0.4f, SkyStrength = 0.2f };
        public List<BakePointLight> PointLights = new();
        public List<BakeSpotLight> SpotLights = new();
    }

    /// <summary>Opciones que el baker implementa realmente al generar cada chart UV.</summary>
    public readonly record struct LightmapBakeSettings(
        int TexelsPerUnit,
        int MaxLightmapSize,
        int Padding,
        float IndirectIntensity,
        float AlbedoBoost,
        int IndirectSamples = 2,
        int Bounces = 1)
    {
        public static LightmapBakeSettings ProductionDefault => new(20, 1024, 2, 1f, 1f, 16, 2);

        /// <summary>
        /// El contador de la UI es un presupuesto de calidad, no una orden de disparar esa
        /// cantidad literal de rayos por texel. Una escena interior con 200×2 paths por texel
        /// tarda decenas de minutos en CPU. La raíz cuadrada conserva una progresión de calidad
        /// útil (16→4, 64→8, 256→16) y limita el preset de producción a 32 paths reales.
        /// </summary>
        public int EffectiveIndirectSamples => Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(IndirectSamples)), 1, 32);

        public LightmapBakeSettings Normalize() => new(
            Math.Clamp(TexelsPerUnit, 1, 200),
            NormalizeMaxSize(MaxLightmapSize),
            Math.Clamp(Padding, 1, 64),
            Math.Clamp(IndirectIntensity, 0f, 4f),
            Math.Clamp(AlbedoBoost, 0f, 4f),
            Math.Clamp(IndirectSamples, 1, 64),
            Math.Clamp(Bounces, 0, 4));

        private static int NormalizeMaxSize(int value) => value switch
        {
            <= 256 => 256,
            <= 512 => 512,
            <= 1024 => 1024,
            <= 2048 => 2048,
            _ => 4096
        };
    }

    /// <summary>
    /// Bakeador de lightmaps offline — núcleo sin dependencias de plataforma.
    /// Recibe la malla ya cargada y los datos de iluminación.
    /// El proyecto editor (WPF/ImGui) construye BakeLightingInfo y llama BakeAsync.
    /// </summary>
    public class LightmapBaker
    {
        public const int    DefaultResolution = 256;
        public const string LightmapSuffix   = "_light.bmp";
        public const string ShadowmaskSuffix = "_shadowmask.bmp";

        private readonly string _outputDir;
        public event Action<string>? OnLog;
        public event Action<float>?  OnProgress;
        public Dictionary<string, string> BakedPaths { get; } = new();
        public Dictionary<string, BakedLightmapLink> BakedLinks { get; } = new();
        public string LightingDataPath { get; private set; } = "";

        public readonly record struct BakedLightmapLink(
            string Path, string ShadowmaskPath, float ScaleX, float ScaleY, float OffsetX, float OffsetY);

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
        // VertexStart/VertexCount identifican la submalla dentro de los buffers del FBX.
        // Un FBX importado con hijos tiene un MeshFilter por submalla; hornear todos los
        // buffers para cada hijo duplica la casa entera en cada lightmap y contamina UVs.
        public record BakedMeshData(
            float[] Positions,
            float[] Normals,
            float[] UVs,
            int TriangleCount,
            int VertexStart = 0,
            int VertexCount = -1);

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
            int resolution = DefaultResolution,
            LightmapBakeSettings? settingsOverride = null) =>
            await BakeAsync(roots, lighting, (path, _) => meshLoader(path), resolution, settingsOverride);

        /// <summary>
        /// Variante que recibe el índice de submalla solicitado por cada MeshFilter. El editor
        /// la usa para no hornear el FBX completo una vez por cada hijo importado.
        /// </summary>
        public async Task BakeAsync(
            IReadOnlyList<GameObject> roots,
            BakeLightingInfo lighting,
            Func<string, int, BakedMeshData?> meshLoader,
            int resolution = DefaultResolution,
            LightmapBakeSettings? settingsOverride = null)
        {
            var settings = (settingsOverride ?? new LightmapBakeSettings(20, resolution, 2, 1f, 1f)).Normalize();
            BakedPaths.Clear();
            BakedLinks.Clear();
            LightingDataPath = "";
            var statics = new List<GameObject>();
            CollectStaticMeshes(roots, statics);
            OnLog?.Invoke($"[Lightmap] {statics.Count} objetos estáticos — {settings.TexelsPerUnit} texels/u, máximo {settings.MaxLightmapSize}px, {settings.EffectiveIndirectSamples} paths indirectos × {settings.Bounces} rebotes.");
            OnProgress?.Invoke(0.01f);

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
                var mesh = meshLoader(mf.MeshPath, mf.SubmeshIndex);
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
            OnProgress?.Invoke(0.05f);

            if (jobs.Count == 0)
            {
                OnProgress?.Invoke(1f);
                OnLog?.Invoke("[Lightmap] Completado - 0 lightmaps.");
                return;
            }

            // Unity no deja que la luz horneada atraviese paredes: antes de rasterizar
            // construimos una escena de triángulos estáticos para las pruebas de visibilidad.
            // Se hace sobre el snapshot, por lo que sigue siendo seguro para el hilo de fondo.
            var occluders = BuildOccluderScene(jobs);
            OnLog?.Invoke($"[Lightmap] Visibilidad: {occluders.TriangleCount} triángulos oclusores.");
            OnProgress?.Invoke(0.08f);

            // ── Fase 2 (HILO DE FONDO) ───────────────────────────────────────
            // Baking puro de CPU sobre el snapshot: no toca GameObject, ObjLoader
            // ni OpenGL, así que es seguro en el thread pool.
            int done = 0;
            var charts = new List<BakedChart>(jobs.Count);
            foreach (var job in jobs)
            {
                int completedBeforeJob = done;
                float lastReported = -1f;
                charts.Add(await Task.Run(() => BakeJobToChart(job, lighting, settings, occluders, jobProgress =>
                {
                    // 8..92% es rasterizado/iluminación. Limitamos las notificaciones a
                    // 0.2% para que el progreso sea fluido sin saturar la UI.
                    float overall = 0.08f + 0.84f * ((completedBeforeJob + jobProgress) / jobs.Count);
                    if (overall - lastReported >= 0.002f || jobProgress >= 0.999f)
                    {
                        lastReported = overall;
                        OnProgress?.Invoke(overall);
                    }
                })));
                done++;
                OnProgress?.Invoke(0.08f + 0.84f * done / jobs.Count);
            }
            OnProgress?.Invoke(0.95f);
            PackAndWriteAtlases(charts, settings);
            OnProgress?.Invoke(1f);
            OnLog?.Invoke($"[Lightmap] Completado — {BakedPaths.Count} lightmaps.");
        }

        private readonly record struct BakedChart(string EditorId, string MeshName, byte[] Pixels, byte[] Shadowmask, int Width, int Height);

        private BakedChart BakeJobToChart(BakeJob job, BakeLightingInfo lighting, LightmapBakeSettings settings, BakeRayScene occluders, Action<float>? progress = null)
        {
            var mesh = job.Mesh;
            int res = CalculateChartResolution(job, settings);
            float s = job.ImportScale;
            var worldPos = job.WorldPos;
            var worldScale = job.WorldScale;
            var worldRot = job.WorldRot;

            // Buffer acumulado RGBA. El baker anterior escribía solo los tres vértices de
            // cada cara; el chart resultaba prácticamente vacío. Rasterizamos el triángulo
            // completo en UV para que cada texel represente iluminación real de la superficie.
            var pixels = new byte[res * res * 4];
            var sums = new float[res * res * 3];
            var shadowSums = new float[res * res * 4];
            var coverage = new int[res * res];

            var (firstVertex, vertexCount) = GetBakeVertexRange(mesh);
            int triangleCount = vertexCount / 3;
            progress?.Invoke(0f);
            for (int t = 0; t < triangleCount; t++)
            {
                int vertex = firstVertex + t * 3;
                if (!TryGetBakeVertex(job, vertex, out var a) ||
                    !TryGetBakeVertex(job, vertex + 1, out var b) ||
                    !TryGetBakeVertex(job, vertex + 2, out var c))
                    continue;

                float triangleStart = t / (float)Math.Max(1, triangleCount);
                float triangleWeight = 1f / Math.Max(1, triangleCount);
                RasterizeTriangle(a, b, c, res, lighting, settings, occluders, sums, shadowSums, coverage,
                    rowProgress => progress?.Invoke(triangleStart + triangleWeight * rowProgress));
            }

            var shadowmask = new byte[res * res * 4];
            for (int pixel = 0; pixel < coverage.Length; pixel++)
            {
                int count = coverage[pixel];
                for (int channel = 0; channel < 4; channel++)
                    shadowmask[pixel * 4 + channel] = count == 0 ? (byte)255 : ToByte(shadowSums[pixel * 4 + channel] / count);
            }

            for (int pixel = 0; pixel < coverage.Length; pixel++)
            {
                int count = coverage[pixel];
                if (count == 0) continue;
                int rgb = pixel * 3;
                int rgba = pixel * 4;
                pixels[rgba] = ToByte(sums[rgb] / count);
                pixels[rgba + 1] = ToByte(sums[rgb + 1] / count);
                pixels[rgba + 2] = ToByte(sums[rgb + 2] / count);
                pixels[rgba + 3] = 255;
            }

            Dilate(pixels, res, settings.Padding);
            Dilate(shadowmask, res, settings.Padding);

            progress?.Invoke(1f);
            OnLog?.Invoke($"[Lightmap]   ✔ chart {job.MeshName} ({res}×{res})");
            return new BakedChart(job.EditorId, job.MeshName, pixels, shadowmask, res, res);
        }

        private readonly record struct BakeVertex(Vector3 Position, Vector3 Normal, float U, float V);

        private static bool TryGetBakeVertex(BakeJob job, int vertex, out BakeVertex result)
        {
            int pi = vertex * 3;
            var mesh = job.Mesh;
            if (pi + 2 >= mesh.Positions.Length)
            {
                result = default;
                return false;
            }

            float s = job.ImportScale;
            var scale = job.WorldScale;
            var local = new Vector3(mesh.Positions[pi] * s * scale.X, mesh.Positions[pi + 1] * s * scale.Y, mesh.Positions[pi + 2] * s * scale.Z);
            var worldPosition = job.WorldPos + RotVec(local, job.WorldRot);
            var normal = pi + 2 < mesh.Normals.Length
                ? new Vector3(mesh.Normals[pi], mesh.Normals[pi + 1], mesh.Normals[pi + 2])
                : new Vector3(0f, 1f, 0f);
            var range = GetBakeVertexRange(mesh);
            var lightmapUv = LightmapUvUtility.GetTriangleChartUv(vertex, range.FirstVertex, range.VertexCount);
            result = new BakeVertex(
                worldPosition,
                NormalizeSafe(RotVec(normal, job.WorldRot)),
                lightmapUv.U,
                lightmapUv.V);
            return true;
        }

        private static void RasterizeTriangle(BakeVertex a, BakeVertex b, BakeVertex c, int res,
            BakeLightingInfo lighting, LightmapBakeSettings settings, BakeRayScene occluders, float[] sums, float[] shadowSums, int[] coverage,
            Action<float>? progress)
        {
            float ax = a.U * (res - 1), ay = a.V * (res - 1);
            float bx = b.U * (res - 1), by = b.V * (res - 1);
            float cx = c.U * (res - 1), cy = c.V * (res - 1);
            float area = Edge(ax, ay, bx, by, cx, cy);
            if (MathF.Abs(area) < 0.00001f) return;

            int minX = Math.Clamp((int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))), 0, res - 1);
            int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))), 0, res - 1);
            int minY = Math.Clamp((int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))), 0, res - 1);
            int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))), 0, res - 1);
            const float epsilon = -0.0001f;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float wa = Edge(bx, by, cx, cy, px, py) / area;
                    float wb = Edge(cx, cy, ax, ay, px, py) / area;
                    float wc = 1f - wa - wb;
                    if (wa < epsilon || wb < epsilon || wc < epsilon) continue;

                    var position = new Vector3(
                        a.Position.X * wa + b.Position.X * wb + c.Position.X * wc,
                        a.Position.Y * wa + b.Position.Y * wb + c.Position.Y * wc,
                        a.Position.Z * wa + b.Position.Z * wb + c.Position.Z * wc);
                    var normal = NormalizeSafe(new Vector3(
                        a.Normal.X * wa + b.Normal.X * wb + c.Normal.X * wc,
                        a.Normal.Y * wa + b.Normal.Y * wb + c.Normal.Y * wc,
                        a.Normal.Z * wa + b.Normal.Z * wb + c.Normal.Z * wc));
                    SampleLight(position, normal, lighting, settings, occluders, out float r, out float g, out float bl);
                    int pixel = y * res + x;
                    int rgb = pixel * 3;
                    sums[rgb] += r; sums[rgb + 1] += g; sums[rgb + 2] += bl;
                    SampleShadowmask(position, normal, lighting, occluders, shadowSums, pixel * 4);
                    coverage[pixel]++;
                }
                // Un triángulo grande (pared/suelo) puede ocupar un chart entero. Informar
                // por filas evita que la ventana parezca congelada en 0% durante ese chart.
                if (((y - minY) & 15) == 0 || y == maxY)
                    progress?.Invoke((y - minY + 1f) / Math.Max(1f, maxY - minY + 1f));
            }
        }

        private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
            (px - ax) * (by - ay) - (py - ay) * (bx - ax);

        private static int CalculateChartResolution(BakeJob job, LightmapBakeSettings settings)
        {
            var mesh = job.Mesh;
            var (firstVertex, vertexCount) = GetBakeVertexRange(mesh);
            if (vertexCount < 3) return Math.Min(32, settings.MaxLightmapSize);
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int vertex = firstVertex; vertex < firstVertex + vertexCount; vertex++)
            {
                int i = vertex * 3;
                float x = mesh.Positions[i] * job.ImportScale * MathF.Abs(job.WorldScale.X);
                float y = mesh.Positions[i + 1] * job.ImportScale * MathF.Abs(job.WorldScale.Y);
                float z = mesh.Positions[i + 2] * job.ImportScale * MathF.Abs(job.WorldScale.Z);
                minX = MathF.Min(minX, x); maxX = MathF.Max(maxX, x);
                minY = MathF.Min(minY, y); maxY = MathF.Max(maxY, y);
                minZ = MathF.Min(minZ, z); maxZ = MathF.Max(maxZ, z);
            }
            float largestExtent = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));
            int requested = Math.Max(32, (int)MathF.Ceiling(largestExtent * settings.TexelsPerUnit));
            int resolution = 1;
            while (resolution < requested && resolution < settings.MaxLightmapSize) resolution <<= 1;
            return Math.Clamp(resolution, 32, settings.MaxLightmapSize);
        }

        private static (int FirstVertex, int VertexCount) GetBakeVertexRange(BakedMeshData mesh)
        {
            int available = mesh.Positions.Length / 3;
            int first = Math.Clamp(mesh.VertexStart, 0, available);
            int requested = mesh.VertexCount < 0 ? mesh.TriangleCount * 3 : mesh.VertexCount;
            int count = Math.Clamp(requested, 0, available - first);
            return (first, count - count % 3);
        }

        private static void SampleLight(Vector3 wp, Vector3 wn, BakeLightingInfo l, LightmapBakeSettings settings, BakeRayScene occluders,
                                         out float r, out float g, out float b)
        {
            float sky = Math.Max(0f, wn.Y) * l.Ambient.SkyStrength;
            float indirect = settings.IndirectIntensity * settings.AlbedoBoost;
            r = l.Ambient.R * (l.Ambient.Intensity + sky) * indirect;
            g = l.Ambient.G * (l.Ambient.Intensity + sky) * indirect;
            b = l.Ambient.B * (l.Ambient.Intensity + sky) * indirect;

            var sunDir = NormalizeSafe(new Vector3(l.Directional.X, l.Directional.Y, l.Directional.Z));
            float ndl = Math.Max(0f, wn.X*sunDir.X + wn.Y*sunDir.Y + wn.Z*sunDir.Z);
            bool bakeLegacyDirectionalDirect = l.Directional.BakeDirect || (!l.Directional.BakeDirect && !l.Directional.BakeIndirect);
            if (bakeLegacyDirectionalDirect && ndl > 0f && !occluders.IsOccluded(wp + wn * 0.0125f, sunDir, 10000f))
            {
                r += l.Directional.R * l.Directional.Intensity * ndl;
                g += l.Directional.G * l.Directional.Intensity * ndl;
                b += l.Directional.B * l.Directional.Intensity * ndl;
            }

            foreach (var pl in l.PointLights)
            {
                if (!pl.BakeDirect && pl.BakeIndirect) continue;
                var toL = pl.Position - wp;
                float d = MathF.Sqrt(toL.X*toL.X + toL.Y*toL.Y + toL.Z*toL.Z);
                if (d <= 0.0001f || d > pl.Range) continue;
                var dir = new Vector3(toL.X/d, toL.Y/d, toL.Z/d);
                float n   = Math.Max(0f, wn.X*dir.X + wn.Y*dir.Y + wn.Z*dir.Z);
                if (n <= 0f || occluders.IsOccluded(wp + wn * 0.0125f, dir, d - 0.025f)) continue;
                float att = (1f - d/pl.Range) * (1f - d/pl.Range);
                r += pl.R * pl.Intensity * att * n;
                g += pl.G * pl.Intensity * att * n;
                b += pl.B * pl.Intensity * att * n;
            }

            foreach (var sl in l.SpotLights)
            {
                if (!sl.BakeDirect && sl.BakeIndirect) continue;
                var toL = sl.Position - wp;
                float d = MathF.Sqrt(toL.X * toL.X + toL.Y * toL.Y + toL.Z * toL.Z);
                if (d <= 0.0001f || d > sl.Range) continue;
                var directionToLight = new Vector3(toL.X / d, toL.Y / d, toL.Z / d);
                var fromLight = new Vector3(-directionToLight.X, -directionToLight.Y, -directionToLight.Z);
                var coneDirection = NormalizeSafe(sl.Direction);
                float cone = fromLight.X * coneDirection.X + fromLight.Y * coneDirection.Y + fromLight.Z * coneDirection.Z;
                float outer = MathF.Cos(Math.Clamp(sl.OuterAngleDegrees, 1f, 179f) * MathF.PI / 360f);
                if (cone <= outer) continue;
                float coneFalloff = (cone - outer) / Math.Max(0.0001f, 1f - outer);
                if (occluders.IsOccluded(wp + wn * 0.0125f, directionToLight, d - 0.025f)) continue;
                float attenuation = (1f - d / sl.Range) * (1f - d / sl.Range) * coneFalloff * coneFalloff;
                float lambert = Math.Max(0f, wn.X * directionToLight.X + wn.Y * directionToLight.Y + wn.Z * directionToLight.Z);
                r += sl.R * sl.Intensity * attenuation * lambert;
                g += sl.G * sl.Intensity * attenuation * lambert;
                b += sl.B * sl.Intensity * attenuation * lambert;
            }

            // Primer rebote difuso para luces Baked y Mixed. En Mixed no se guarda la
            // contribución directa: el shader la calcula en vivo; aquí sólo queda la luz
            // rebotada, igual al modo Baked Indirect de Unity.
            AddFirstBounce(wp, wn, l, settings, occluders, ref r, ref g, ref b);
        }

        // Shadowmask no almacena color: cada canal es la visibilidad estática de UNA
        // luz Mixed. 1 = la luz alcanza el texel; 0 = una malla estática la bloquea.
        // Los cuatro canales siguen el mismo límite de Unity (RGBA / cuatro luces).
        private static void SampleShadowmask(Vector3 wp, Vector3 wn, BakeLightingInfo lighting,
            BakeRayScene occluders, float[] sums, int offset)
        {
            // Un canal que no pertenece a una luz se deja blanco: nunca apaga luz.
            for (int channel = 0; channel < 4; channel++)
                sums[offset + channel] += 1f;

            void Store(int channel, bool visible)
            {
                if ((uint)channel >= 4) return;
                // Ya sumamos 1 para el valor por defecto; lo reemplazamos por visibilidad.
                sums[offset + channel] += (visible ? 1f : 0f) - 1f;
            }

            var sunDirection = NormalizeSafe(new Vector3(lighting.Directional.X, lighting.Directional.Y, lighting.Directional.Z));
            if (lighting.Directional.ShadowmaskChannel >= 0)
                Store(lighting.Directional.ShadowmaskChannel,
                    Vector3.Dot(wn, sunDirection) > 0f && !occluders.IsOccluded(wp + wn * 0.0125f, sunDirection, 10000f));

            foreach (var light in lighting.PointLights)
            {
                if (light.ShadowmaskChannel < 0) continue;
                var toLight = light.Position - wp;
                float distance = MathF.Sqrt(Vector3.Dot(toLight, toLight));
                bool visible = distance > 0.0001f && distance <= light.Range &&
                    !occluders.IsOccluded(wp + wn * 0.0125f, toLight / distance, distance - 0.025f);
                Store(light.ShadowmaskChannel, visible);
            }

            foreach (var light in lighting.SpotLights)
            {
                if (light.ShadowmaskChannel < 0) continue;
                var toLight = light.Position - wp;
                float distance = MathF.Sqrt(Vector3.Dot(toLight, toLight));
                bool visible = false;
                if (distance > 0.0001f && distance <= light.Range)
                {
                    var direction = toLight / distance;
                    float cone = Vector3.Dot(-direction, NormalizeSafe(light.Direction));
                    float outer = MathF.Cos(Math.Clamp(light.OuterAngleDegrees, 1f, 179f) * MathF.PI / 360f);
                    visible = cone > outer && !occluders.IsOccluded(wp + wn * 0.0125f, direction, distance - 0.025f);
                }
                Store(light.ShadowmaskChannel, visible);
            }
        }

        private static void AddFirstBounce(Vector3 wp, Vector3 wn, BakeLightingInfo lighting, LightmapBakeSettings settings,
            BakeRayScene occluders, ref float r, ref float g, ref float b)
        {
            if (!lighting.Directional.BakeIndirect && !lighting.PointLights.Any(light => light.BakeIndirect) && !lighting.SpotLights.Any(light => light.BakeIndirect))
                return;

            int samples = settings.EffectiveIndirectSamples;
            for (int i = 0; i < samples; i++)
            {
                var rayOrigin = wp + wn * 0.0125f;
                var rayNormal = wn;
                float throughput = 1f;
                for (int bounce = 0; bounce < settings.Bounces; bounce++)
                {
                    // Semilla diferente por rebote: evita repetir el mismo rayo al aumentar
                    // Bounces y mantiene el resultado determinista entre bakes.
                    var direction = CosineHemisphere(rayNormal, rayOrigin, i + bounce * 977);
                    if (!occluders.Raycast(rayOrigin, direction, 50f, out var hit))
                        break;
                    var hitNormal = hit.Normal;
                    if (Vector3.Dot(hitNormal, -direction) < 0f) hitNormal = -hitNormal;
                    EvaluateIndirectSources(hit.Position + hitNormal * 0.0125f, hitNormal, lighting, occluders, out float lr, out float lg, out float lb);
                    float cosine = Math.Max(0f, Vector3.Dot(rayNormal, direction));
                    float weight = throughput * 0.65f * 2f * cosine * settings.IndirectIntensity * settings.AlbedoBoost / samples;
                    r += lr * weight;
                    g += lg * weight;
                    b += lb * weight;
                    throughput *= 0.55f;
                    rayOrigin = hit.Position + hitNormal * 0.0125f;
                    rayNormal = hitNormal;
                }
            }
        }

        private static void EvaluateIndirectSources(Vector3 wp, Vector3 wn, BakeLightingInfo lighting, BakeRayScene occluders,
            out float r, out float g, out float b)
        {
            r = g = b = 0f;
            var sun = NormalizeSafe(new Vector3(lighting.Directional.X, lighting.Directional.Y, lighting.Directional.Z));
            float sunNdl = Math.Max(0f, Vector3.Dot(wn, sun));
            if (lighting.Directional.BakeIndirect && sunNdl > 0f && !occluders.IsOccluded(wp, sun, 10000f))
            {
                r += lighting.Directional.R * lighting.Directional.Intensity * sunNdl;
                g += lighting.Directional.G * lighting.Directional.Intensity * sunNdl;
                b += lighting.Directional.B * lighting.Directional.Intensity * sunNdl;
            }
            foreach (var light in lighting.PointLights)
            {
                if (!light.BakeIndirect) continue;
                var toLight = light.Position - wp;
                float distance = MathF.Sqrt(Vector3.Dot(toLight, toLight));
                if (distance <= 0.0001f || distance > light.Range) continue;
                var direction = toLight / distance;
                float ndl = Math.Max(0f, Vector3.Dot(wn, direction));
                if (ndl <= 0f || occluders.IsOccluded(wp, direction, distance - 0.025f)) continue;
                float attenuation = (1f - distance / light.Range) * (1f - distance / light.Range);
                r += light.R * light.Intensity * attenuation * ndl;
                g += light.G * light.Intensity * attenuation * ndl;
                b += light.B * light.Intensity * attenuation * ndl;
            }
            foreach (var light in lighting.SpotLights)
            {
                if (!light.BakeIndirect) continue;
                var toLight = light.Position - wp;
                float distance = MathF.Sqrt(Vector3.Dot(toLight, toLight));
                if (distance <= 0.0001f || distance > light.Range) continue;
                var direction = toLight / distance;
                var fromLight = -direction;
                float cone = Vector3.Dot(fromLight, NormalizeSafe(light.Direction));
                float outer = MathF.Cos(Math.Clamp(light.OuterAngleDegrees, 1f, 179f) * MathF.PI / 360f);
                float ndl = Math.Max(0f, Vector3.Dot(wn, direction));
                if (cone <= outer || ndl <= 0f || occluders.IsOccluded(wp, direction, distance - 0.025f)) continue;
                float coneFalloff = (cone - outer) / Math.Max(0.0001f, 1f - outer);
                float attenuation = (1f - distance / light.Range) * (1f - distance / light.Range) * coneFalloff * coneFalloff;
                r += light.R * light.Intensity * attenuation * ndl;
                g += light.G * light.Intensity * attenuation * ndl;
                b += light.B * light.Intensity * attenuation * ndl;
            }
        }

        private static Vector3 CosineHemisphere(Vector3 normal, Vector3 position, int sample)
        {
            float seed = position.X * 12.9898f + position.Y * 78.233f + position.Z * 37.719f + sample * 19.19f;
            float u = Frac(MathF.Sin(seed) * 43758.5453f);
            float v = Frac(MathF.Sin(seed * 1.371f + 11.17f) * 15731.743f);
            float radius = MathF.Sqrt(u);
            float phi = MathF.PI * 2f * v;
            var tangent = MathF.Abs(normal.Y) < 0.999f ? NormalizeSafe(Vector3.Cross(new Vector3(0f, 1f, 0f), normal)) : new Vector3(1f, 0f, 0f);
            var bitangent = NormalizeSafe(Vector3.Cross(normal, tangent));
            return NormalizeSafe(tangent * (MathF.Cos(phi) * radius) + bitangent * (MathF.Sin(phi) * radius) + normal * MathF.Sqrt(Math.Max(0f, 1f - u)));
        }

        private static float Frac(float value) => value - MathF.Floor(value);

        // Empaquetador determinista de charts. Cada renderer recibe el mismo atlas que sus
        // vecinos más un Scale/Offset propio, igual que Unity LightmapSettings.lightmaps.
        private void PackAndWriteAtlases(List<BakedChart> charts, LightmapBakeSettings settings)
        {
            int atlasSize = settings.MaxLightmapSize;
            int gutter = Math.Max(1, settings.Padding);
            int atlasIndex = 0;
            int cursorX = gutter, cursorY = gutter, rowHeight = 0;
            byte[] atlas = NewAtlas(atlasSize);
            byte[] shadowmaskAtlas = NewAtlas(atlasSize);
            var atlasCharts = new List<string>();
            var manifest = new List<string>();

            void FlushAtlas()
            {
                if (atlasCharts.Count == 0) return;
                string path = Path.Combine(_outputDir, $"Lightmap-{atlasIndex}{LightmapSuffix}");
                string shadowmaskPath = Path.Combine(_outputDir, $"Lightmap-{atlasIndex}{ShadowmaskSuffix}");
                WriteBmp(atlas, atlasSize, atlasSize, path);
                WriteRgbaBmp(shadowmaskAtlas, atlasSize, atlasSize, shadowmaskPath);
                OnLog?.Invoke($"[Lightmap]   atlas {atlasIndex}: {atlasCharts.Count} charts ({atlasSize}×{atlasSize})");
                atlasIndex++;
                atlas = NewAtlas(atlasSize);
                shadowmaskAtlas = NewAtlas(atlasSize);
                cursorX = gutter;
                cursorY = gutter;
                rowHeight = 0;
                atlasCharts.Clear();
            }

            foreach (var chart in charts.OrderByDescending(c => c.Height).ThenBy(c => c.EditorId, StringComparer.Ordinal))
            {
                int width = Math.Min(chart.Width, atlasSize - gutter * 2);
                int height = Math.Min(chart.Height, atlasSize - gutter * 2);
                // cursor ya comienza después de un gutter; sólo reservamos otro hacia
                // la derecha/abajo para separar el siguiente chart.
                int requiredWidth = width + gutter;
                int requiredHeight = height + gutter;
                if (cursorX + requiredWidth > atlasSize)
                {
                    cursorX = gutter;
                    cursorY += rowHeight + gutter;
                    rowHeight = 0;
                }
                if (cursorY + requiredHeight > atlasSize && atlasCharts.Count > 0)
                {
                    FlushAtlas();
                }

                CopyChart(chart.Pixels, chart.Width, chart.Height, atlas, atlasSize, cursorX, cursorY, width, height);
                CopyChart(chart.Shadowmask, chart.Width, chart.Height, shadowmaskAtlas, atlasSize, cursorX, cursorY, width, height);
                string path = Path.Combine(_outputDir, $"Lightmap-{atlasIndex}{LightmapSuffix}");
                string shadowmaskPath = Path.Combine(_outputDir, $"Lightmap-{atlasIndex}{ShadowmaskSuffix}");
                var link = new BakedLightmapLink(
                    path,
                    shadowmaskPath,
                    width / (float)atlasSize,
                    height / (float)atlasSize,
                    cursorX / (float)atlasSize,
                    cursorY / (float)atlasSize);
                BakedPaths[chart.EditorId] = path;
                BakedLinks[chart.EditorId] = link;
                atlasCharts.Add(chart.EditorId);
                manifest.Add($"    {{ \"renderer\": \"{EscapeJson(chart.EditorId)}\", \"atlas\": {atlasIndex}, \"lightmap\": \"{Path.GetFileName(link.Path)}\", \"shadowmask\": \"{Path.GetFileName(link.ShadowmaskPath)}\", \"scaleOffset\": [{link.ScaleX:0.######}, {link.ScaleY:0.######}, {link.OffsetX:0.######}, {link.OffsetY:0.######}] }}");
                cursorX += requiredWidth;
                rowHeight = Math.Max(rowHeight, requiredHeight);
            }
            FlushAtlas();

            // El manifiesto es el equivalente legible de LightingData.asset: permite cargar
            // vínculos persistentes sin recalcular el bake y es útil para diagnóstico.
            LightingDataPath = Path.Combine(_outputDir, "LightingData.gasset");
            File.WriteAllText(LightingDataPath,
                "{\n  \"format\": \"GrokoLightingData\",\n  \"version\": 1,\n  \"uvChannel\": \"GeneratedUV2\",\n  \"renderers\": [\n" +
                string.Join(",\n", manifest) + "\n  ]\n}\n");
        }

        private static byte[] NewAtlas(int size)
        {
            var pixels = new byte[size * size * 4];
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return pixels;
        }

        private static void CopyChart(byte[] source, int sourceWidth, int sourceHeight, byte[] destination, int destinationWidth,
            int destinationX, int destinationY, int copyWidth, int copyHeight)
        {
            for (int y = 0; y < copyHeight; y++)
            for (int x = 0; x < copyWidth; x++)
            {
                int sx = Math.Min(sourceWidth - 1, x);
                int sy = Math.Min(sourceHeight - 1, y);
                int sourcePixel = (sy * sourceWidth + sx) * 4;
                int destinationPixel = ((destinationY + y) * destinationWidth + destinationX + x) * 4;
                destination[destinationPixel] = source[sourcePixel];
                destination[destinationPixel + 1] = source[sourcePixel + 1];
                destination[destinationPixel + 2] = source[sourcePixel + 2];
                destination[destinationPixel + 3] = source[sourcePixel + 3];
            }
        }

        private static string EscapeJson(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static BakeRayScene BuildOccluderScene(IReadOnlyList<BakeJob> jobs)
        {
            var triangles = new List<BakeTriangle>();
            foreach (var job in jobs)
            {
                var (first, count) = GetBakeVertexRange(job.Mesh);
                for (int offset = 0; offset < count; offset += 3)
                {
                    if (!TryGetBakeVertex(job, first + offset, out var a) ||
                        !TryGetBakeVertex(job, first + offset + 1, out var b) ||
                        !TryGetBakeVertex(job, first + offset + 2, out var c))
                        continue;
                    var ab = b.Position - a.Position;
                    var ac = c.Position - a.Position;
                    var cross = Vector3.Cross(ab, ac);
                    if (cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z <= 0.0000001f)
                        continue;
                    triangles.Add(new BakeTriangle(a.Position, b.Position, c.Position));
                }
            }
            return new BakeRayScene(triangles);
        }

        private readonly record struct BakeTriangle(Vector3 A, Vector3 B, Vector3 C)
        {
            public Vector3 Min => new(MathF.Min(A.X, MathF.Min(B.X, C.X)), MathF.Min(A.Y, MathF.Min(B.Y, C.Y)), MathF.Min(A.Z, MathF.Min(B.Z, C.Z)));
            public Vector3 Max => new(MathF.Max(A.X, MathF.Max(B.X, C.X)), MathF.Max(A.Y, MathF.Max(B.Y, C.Y)), MathF.Max(A.Z, MathF.Max(B.Z, C.Z)));
            public Vector3 Center => (A + B + C) / 3f;
        }

        // BVH CPU compacta: evita probar cada pared contra cada texel de luz. Con hojas de
        // ocho triángulos el coste crece logarítmicamente incluso para una casa completa.
        private sealed class BakeRayScene
        {
            private readonly BakeTriangle[] _triangles;
            private readonly BakeBvhNode? _root;
            public int TriangleCount => _triangles.Length;

            public BakeRayScene(List<BakeTriangle> triangles)
            {
                _triangles = triangles.ToArray();
                _root = _triangles.Length == 0 ? null : Build(0, _triangles.Length);
            }

            public bool IsOccluded(Vector3 origin, Vector3 direction, float maxDistance)
            {
                return Raycast(origin, direction, maxDistance, out _);
            }

            public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out BakeRayHit hit)
            {
                hit = default;
                if (_root == null || maxDistance <= 0.0001f) return false;
                float closest = maxDistance;
                return IntersectsNode(_root, origin, direction, ref closest, ref hit);
            }

            private BakeBvhNode Build(int start, int count)
            {
                GetBounds(start, count, out var min, out var max);
                if (count <= 8) return new BakeBvhNode(min, max, start, count, null, null);
                var extent = max - min;
                int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
                Array.Sort(_triangles, start, count, Comparer<BakeTriangle>.Create((a, b) => Axis(a.Center, axis).CompareTo(Axis(b.Center, axis))));
                int leftCount = count / 2;
                return new BakeBvhNode(min, max, 0, 0, Build(start, leftCount), Build(start + leftCount, count - leftCount));
            }

            private void GetBounds(int start, int count, out Vector3 min, out Vector3 max)
            {
                min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = start; i < start + count; i++)
                {
                    var tmin = _triangles[i].Min; var tmax = _triangles[i].Max;
                    min = new Vector3(MathF.Min(min.X, tmin.X), MathF.Min(min.Y, tmin.Y), MathF.Min(min.Z, tmin.Z));
                    max = new Vector3(MathF.Max(max.X, tmax.X), MathF.Max(max.Y, tmax.Y), MathF.Max(max.Z, tmax.Z));
                }
            }

            private bool IntersectsNode(BakeBvhNode node, Vector3 origin, Vector3 direction, ref float closest, ref BakeRayHit hit)
            {
                if (!RayBox(origin, direction, node.Min, node.Max, closest)) return false;
                if (node.Left != null)
                {
                    bool left = IntersectsNode(node.Left, origin, direction, ref closest, ref hit);
                    bool right = IntersectsNode(node.Right!, origin, direction, ref closest, ref hit);
                    return left || right;
                }
                bool any = false;
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    if (!RayTriangle(origin, direction, _triangles[i], closest, out float distance)) continue;
                    closest = distance;
                    var triangle = _triangles[i];
                    hit = new BakeRayHit(origin + direction * distance, NormalizeSafe(Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A)));
                    any = true;
                }
                return any;
            }

            private static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float maxDistance)
            {
                float tMin = 0f, tMax = maxDistance;
                return Clip(o.X, d.X, min.X, max.X, ref tMin, ref tMax) &&
                       Clip(o.Y, d.Y, min.Y, max.Y, ref tMin, ref tMax) &&
                       Clip(o.Z, d.Z, min.Z, max.Z, ref tMin, ref tMax);
            }

            private static bool Clip(float o, float d, float min, float max, ref float tMin, ref float tMax)
            {
                if (MathF.Abs(d) < 0.000001f) return o >= min && o <= max;
                float inv = 1f / d;
                float a = (min - o) * inv, b = (max - o) * inv;
                if (a > b) (a, b) = (b, a);
                tMin = MathF.Max(tMin, a); tMax = MathF.Min(tMax, b);
                return tMax >= tMin;
            }

            private static bool RayTriangle(Vector3 o, Vector3 d, BakeTriangle triangle, float maxDistance, out float distance)
            {
                distance = 0f;
                var edge1 = triangle.B - triangle.A;
                var edge2 = triangle.C - triangle.A;
                var p = Vector3.Cross(d, edge2);
                float det = Vector3.Dot(edge1, p);
                if (MathF.Abs(det) < 0.0000001f) return false;
                float inv = 1f / det;
                var t = o - triangle.A;
                float u = Vector3.Dot(t, p) * inv;
                if (u < 0f || u > 1f) return false;
                var q = Vector3.Cross(t, edge1);
                float v = Vector3.Dot(d, q) * inv;
                if (v < 0f || u + v > 1f) return false;
                distance = Vector3.Dot(edge2, q) * inv;
                return distance > 0.0001f && distance < maxDistance;
            }

            private static float Axis(Vector3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
        }

        private readonly record struct BakeRayHit(Vector3 Position, Vector3 Normal);

        private sealed record BakeBvhNode(Vector3 Min, Vector3 Max, int Start, int Count, BakeBvhNode? Left, BakeBvhNode? Right);

        private static void Dilate(byte[] px, int res, int iterations)
        {
            for (int p = 0; p < iterations; p++)
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

        // La máscara necesita cuatro canales; BMP 24-bit descartaría el canal A y
        // limitaría el sistema a tres luces. El loader del editor ya consume BMP32
        // como Format32bppArgb, así que mantenemos el formato simple y portable.
        private static void WriteRgbaBmp(byte[] rgba, int w, int h, string path)
        {
            const int bytesPerPixel = 4;
            int dataSize = w * h * bytesPerPixel;
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write((ushort)0x4D42);
            bw.Write(54 + dataSize);
            bw.Write(0);
            bw.Write(54);
            bw.Write(40); bw.Write(w); bw.Write(h);
            bw.Write((ushort)1); bw.Write((ushort)32);
            bw.Write(0); bw.Write(dataSize);
            bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bw.Write(rgba[i + 2]); bw.Write(rgba[i + 1]); bw.Write(rgba[i]); bw.Write(rgba[i + 3]);
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
