using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Assimp;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    public class ParsedMesh
    {
        public float[] Positions = Array.Empty<float>(); // xyz por vértice (3 floats por vértice)
        public float[] Normals   = Array.Empty<float>(); // xyz por vértice
        public float[] UVs = Array.Empty<float>();
        public int TriangleCount;
        public Vector3 BoundsMin = Vector3.Zero;
        public Vector3 BoundsMax = Vector3.Zero;

        // Sub-mallas: cada una corresponde a un material distinto del modelo importado.
        // VertexStart/VertexCount son índices dentro de Positions/Normals/UVs (en vértices, no floats).
        public List<MeshSubmesh> Submeshes = new();

        // Nº de NODOS con malla del FBX = nº de objetos separados de verdad (no submallas por material).
        // >1 ⇒ el modelo trae varios objetos → se importa "con hijos" automáticamente (como Unity).
        public int SeparateObjectCount;

        // Jerarquía de huesos (armature/metarig). null si el modelo no tiene esqueleto.
        public ModelNode? Hierarchy;

        // Animaciones embebidas (resumen, vacío si no hay).
        public List<ModelAnimationInfo> Animations = new();

        // Clips de animación esqueletal con canales por hueso (para reproducir).
        public List<SkeletalClip> Clips = new();

        // Escala recomendada al importar (estilo Unity: lleva el modelo a metros, 1u=1m).
        // p.ej. FBX de Mixamo en cm → 0.01. OBJ y modelos sin metadatos → 1.
        public float RecommendedScale = 1f;

        // ── Datos de skinning (deformación de la malla por huesos) ──
        public bool HasSkin;
        public int[] BoneIndices = Array.Empty<int>();   // 4 por vértice expandido (índice en BoneNames; -1 = sin hueso)
        public float[] BoneWeights = Array.Empty<float>(); // 4 por vértice expandido (suman ~1)
        public List<string> BoneNames = new();           // tabla global de huesos
        public List<System.Numerics.Matrix4x4> BoneOffsets = new(); // matriz offset (bind inverse) por hueso, espacio malla→bind
    }

    // Datos de un material embebido en el modelo importado (FBX/OBJ), usado para
    // generar automáticamente un .mat por cada sub-malla la primera vez que se asigna.
    public class MeshSubmesh
    {
        public string Name = "";      // nombre mostrado (puede ser el del material)
        public string MeshName = "";  // nombre de la malla/objeto en el FBX (para nombrar el hijo al partir)
        public int VertexStart;
        public int VertexCount;
        public float DiffuseR = 0.8f;
        public float DiffuseG = 0.8f;
        public float DiffuseB = 0.8f;
        public string? TexturePath;

        // Bounds locales de ESTA submalla (en unidades nativas del modelo, sin ImportScale).
        // Se usan para la caja de selección/gizmo cuando un hijo dibuja solo esta parte.
        public float MinX, MinY, MinZ;
        public float MaxX = 1f, MaxY = 1f, MaxZ = 1f;
    }

    // Información de una animación embebida en un modelo importado (FBX, etc.).
    public class ModelAnimationInfo
    {
        public string Name = "";
        public double DurationSeconds;
        public int ChannelCount; // nº de huesos/nodos animados
    }

    // ── Datos de animación esqueletal (canales por hueso) ──
    public struct BoneVecKey { public float Time; public Vector3 Value; }
    public struct BoneQuatKey { public float Time; public MiMotor.Mathematics.Quaternion Value; }

    public class BoneChannel
    {
        public string NodeName = "";
        public List<BoneVecKey> Positions = new();
        public List<BoneQuatKey> Rotations = new();
        public List<BoneVecKey> Scales = new();
    }

    public class SkeletalClip
    {
        public string Name = "";
        public float Duration; // segundos
        public List<BoneChannel> Channels = new();
    }

    // Nodo del esqueleto/jerarquía de un modelo (armature "metarig" + huesos), con su
    // transform local ya descompuesto (posición, rotación en grados, escala).
    public class ModelNode
    {
        public string Name = "";
        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ; // grados (aprox, para serializar/inspector)
        public float Qx, Qy, Qz, Qw = 1f; // rotación EXACTA (cuaternión) del FBX, para el bind
        public float ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f;
        public List<ModelNode> Children = new();
    }

    public static class ObjLoader
    {
        // Thread-safe: el cache es estático y compartido. Aunque el flujo normal
        // carga mallas desde el hilo principal, exponemos una API pública estática
        // que podría llamarse desde varios hilos (p. ej. un bake en segundo plano),
        // así que usamos ConcurrentDictionary para evitar corrupción/excepciones.
        // Tick = orden de inserción, usado para la evicción FIFO (ver EvictIfNeeded).
        private static readonly ConcurrentDictionary<string, (ParsedMesh Mesh, DateTime LastWrite, long Tick)> _cache = new();

        // Tope de mallas cacheadas. Evita que el cache crezca sin límite al navegar
        // muchos assets distintos en una sesión larga. Al superarlo, se descarta la
        // entrada más antigua (FIFO). El "working set" real de una escena es pequeño,
        // así que cualquier malla evictada se recarga al instante si vuelve a usarse.
        private const int MaxCachedMeshes = 256;
        private static long _insertTick;
        public static string LastError { get; private set; } = "";

        public static bool IsSupportedMesh(string path) =>
            path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        // Lee (vía Assimp) las animaciones embebidas en un modelo (p. ej. un personaje FBX).
        // Devuelve lista vacía si no hay animaciones o el formato no las soporta.
        // Reutiliza la malla ya parseada/cacheada (sin re-importar con Assimp).
        public static List<ModelAnimationInfo> ReadAnimations(string path) =>
            Load(path)?.Animations ?? new List<ModelAnimationInfo>();

        // Lee la jerarquía de nodos (armature/metarig + huesos) de un modelo con esqueleto.
        // Devuelve el nodo raíz (sus Children son la jerarquía real). null si el modelo no
        // tiene huesos (p. ej. un OBJ o un FBX estático) o no se puede leer.
        // Reutiliza la malla ya parseada/cacheada (NO re-importa con Assimp, para evitar
        // una segunda importación nativa en paralelo que puede colgar el proceso).
        public static ModelNode? ReadHierarchy(string path) => Load(path)?.Hierarchy;

        private static ModelNode ConvertNode(Node node, ref int budget)
        {
            node.Transform.Decompose(out var scale, out var rot, out var pos);
            var (rx, ry, rz) = QuatToEulerDeg(rot.X, rot.Y, rot.Z, rot.W);

            var mn = new ModelNode
            {
                Name = string.IsNullOrWhiteSpace(node.Name) ? "Node" : node.Name,
                PosX = Safe(pos.X), PosY = Safe(pos.Y), PosZ = Safe(pos.Z),
                RotX = Safe(rx), RotY = Safe(ry), RotZ = Safe(rz),
                Qx = Safe(rot.X), Qy = Safe(rot.Y), Qz = Safe(rot.Z), Qw = Safe(rot.W, 1f),
                ScaleX = Safe(scale.X, 1f), ScaleY = Safe(scale.Y, 1f), ScaleZ = Safe(scale.Z, 1f)
            };

            foreach (var child in node.Children)
            {
                if (budget-- <= 0) break;
                mn.Children.Add(ConvertNode(child, ref budget));
            }
            return mn;
        }

        // Evita NaN/Infinity (romperían la serialización JSON de la escena → crash).
        private static float Safe(float v, float fallback = 0f) => float.IsFinite(v) ? v : fallback;

        // Añade las 4 influencias de hueso (mayores pesos, normalizadas) del vértice 'vid'.
        private static void AppendInfluences(Dictionary<int, List<(int bi, float w)>>? influences, int vid, List<int> boneIdx, List<float> boneWt)
        {
            int b0 = -1, b1 = -1, b2 = -1, b3 = -1;
            float w0 = 0, w1 = 0, w2 = 0, w3 = 0;
            if (influences != null && influences.TryGetValue(vid, out var list) && list.Count > 0)
            {
                list.Sort((a, b) => b.w.CompareTo(a.w));
                int n = Math.Min(4, list.Count);
                float sum = 0f;
                for (int k = 0; k < n; k++) sum += list[k].w;
                if (sum <= 1e-6f) sum = 1f;
                if (n > 0) { b0 = list[0].bi; w0 = list[0].w / sum; }
                if (n > 1) { b1 = list[1].bi; w1 = list[1].w / sum; }
                if (n > 2) { b2 = list[2].bi; w2 = list[2].w / sum; }
                if (n > 3) { b3 = list[3].bi; w3 = list[3].w / sum; }
            }
            boneIdx.Add(b0); boneIdx.Add(b1); boneIdx.Add(b2); boneIdx.Add(b3);
            boneWt.Add(w0); boneWt.Add(w1); boneWt.Add(w2); boneWt.Add(w3);
        }

        // Convierte una matriz de Assimp (row-major, convención column-vector) a System.Numerics
        // (row-major, convención row-vector) → transpuesta.
        private static System.Numerics.Matrix4x4 AssimpToNumerics(Assimp.Matrix4x4 m) => new System.Numerics.Matrix4x4(
            m.A1, m.B1, m.C1, m.D1,
            m.A2, m.B2, m.C2, m.D2,
            m.A3, m.B3, m.C3, m.D3,
            m.A4, m.B4, m.C4, m.D4);

        // Conversión cuaternión→Euler (grados) Tait-Bryan estándar (roll=X, pitch=Y, yaw=Z).
        private static (float x, float y, float z) QuatToEulerDeg(float x, float y, float z, float w)
        {
            float sinrCosp = 2f * (w * x + y * z);
            float cosrCosp = 1f - 2f * (x * x + y * y);
            float roll = MathF.Atan2(sinrCosp, cosrCosp);

            float sinp = 2f * (w * y - z * x);
            float pitch = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);

            float sinyCosp = 2f * (w * z + x * y);
            float cosyCosp = 1f - 2f * (y * y + z * z);
            float yaw = MathF.Atan2(sinyCosp, cosyCosp);

            const float r2d = 180f / MathF.PI;
            return (roll * r2d, pitch * r2d, yaw * r2d);
        }

        public static ParsedMesh? Load(string path)
        {
            LastError = "";
            if (!File.Exists(path))
            {
                LastError = "Mesh file not found: " + path;
                return null;
            }
            if (!IsSupportedMesh(path))
            {
                LastError = "Unsupported mesh format: " + Path.GetExtension(path);
                return null;
            }

            var diskTime = File.GetLastWriteTimeUtc(path);
            if (_cache.TryGetValue(path, out var cached))
            {
                if (cached.LastWrite == diskTime) return cached.Mesh;
                _cache.TryRemove(path, out _);
            }

            try
            {
                var mesh = path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                    ? ParseFbx(path)
                    : ParseObj(path);
                if (mesh == null)
                    return null;
                EvictIfNeeded();
                _cache[path] = (mesh, diskTime, Interlocked.Increment(ref _insertTick));
                return mesh;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        public static void InvalidateCache(string path) => _cache.TryRemove(path, out _);

        private static ParsedMesh? ParseFbx(string path)
        {
            Exception? assimpException = null;
            try
            {
                var mesh = ParseWithAssimp(path);
                if (mesh != null)
                    return mesh;
            }
            catch (Exception ex)
            {
                assimpException = ex;
            }

            try
            {
                return ParseFbxAscii(path);
            }
            catch (Exception ex)
            {
                LastError = assimpException == null
                    ? ex.Message
                    : $"Assimp: {assimpException.Message}; ASCII fallback: {ex.Message}";
                return null;
            }
        }

        // Evicción FIFO: si el cache está lleno, descarta la(s) entrada(s) con el
        // tick de inserción más antiguo hasta dejar hueco para una nueva. O(n) por
        // evicción, pero n está acotado por MaxCachedMeshes y solo ocurre al llenar.
        private static void EvictIfNeeded()
        {
            while (_cache.Count >= MaxCachedMeshes)
            {
                string? oldestKey = null;
                long oldest = long.MaxValue;
                foreach (var kv in _cache)
                {
                    if (kv.Value.Tick < oldest)
                    {
                        oldest = kv.Value.Tick;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null || !_cache.TryRemove(oldestKey, out _))
                    break; // nada que evictar o ya lo quitó otro hilo
            }
        }

        // ── Parser ──────────────────────────────────────────
        private static ParsedMesh ParseObj(string path)
        {
            var vList  = new List<(float x, float y, float z)>();
            var vnList = new List<(float x, float y, float z)>();
            var vtList = new List<(float u, float v)>();

            var positions  = new List<float>();
            var normals    = new List<float>();
            var uvs        = new List<float>();

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            string? objDir = Path.GetDirectoryName(Path.GetFullPath(path));
            var materials = new Dictionary<string, MtlMaterial>(StringComparer.OrdinalIgnoreCase);
            var submeshes = new List<MeshSubmesh>();

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (StartsWithToken(line, "mtllib"))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && objDir != null)
                    {
                        string mtlPath = Path.Combine(objDir, parts[1]);
                        if (File.Exists(mtlPath))
                            foreach (var kv in ParseMtl(mtlPath))
                                materials[kv.Key] = kv.Value;
                    }
                }
                else if (StartsWithToken(line, "usemtl"))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    string name = parts.Length >= 2 ? parts[1] : "";

                    // Cerrar la sub-malla anterior y abrir una nueva para este material
                    if (submeshes.Count > 0)
                        submeshes[^1].VertexCount = positions.Count / 3 - submeshes[^1].VertexStart;

                    var submesh = new MeshSubmesh { Name = name, VertexStart = positions.Count / 3 };
                    if (materials.TryGetValue(name, out var mtl))
                    {
                        submesh.DiffuseR = mtl.R; submesh.DiffuseG = mtl.G; submesh.DiffuseB = mtl.B;
                        submesh.TexturePath = mtl.TexturePath;
                    }
                    submeshes.Add(submesh);
                }
                else if (StartsWithToken(line, "vn"))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                        vnList.Add((ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3])));
                }
                else if (StartsWithToken(line, "vt"))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                        vtList.Add((ParseF(parts[1]), 1f - ParseF(parts[2])));
                }
                else if (StartsWithToken(line, "v"))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                        vList.Add((ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3])));
                }
                else if (StartsWithToken(line, "f"))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    var poly = new List<(int p, int t, int n)>();
                    for (int i = 1; i < parts.Length; i++)
                        poly.Add(ParseFaceVert(parts[i], vList.Count, vtList.Count, vnList.Count));

                    // Triangulación en abanico
                    for (int i = 1; i < poly.Count - 1; i++)
                    {
                        var tri = new[] { poly[0], poly[i], poly[i + 1] };

                        // Normal de cara (para vértices sin normal)
                        var p0 = GetV(vList, tri[0].p);
                        var p1 = GetV(vList, tri[1].p);
                        var p2 = GetV(vList, tri[2].p);
                        float ex1 = p1.x - p0.x, ey1 = p1.y - p0.y, ez1 = p1.z - p0.z;
                        float ex2 = p2.x - p0.x, ey2 = p2.y - p0.y, ez2 = p2.z - p0.z;
                        float fnx = ey1 * ez2 - ez1 * ey2;
                        float fny = ez1 * ex2 - ex1 * ez2;
                        float fnz = ex1 * ey2 - ey1 * ex2;
                        float fl  = MathF.Sqrt(fnx * fnx + fny * fny + fnz * fnz);
                        if (fl > 0.0001f) { fnx /= fl; fny /= fl; fnz /= fl; }

                        foreach (var (pi, ti, ni) in tri)
                        {
                            var (px, py, pz) = GetV(vList, pi);
                            positions.Add(px); positions.Add(py); positions.Add(pz);
                            var (u, v) = ti >= 0 && ti < vtList.Count ? vtList[ti] : (0f, 0f);
                            uvs.Add(u); uvs.Add(v);

                            // Actualizar bounds
                            if (px < minX) minX = px; if (px > maxX) maxX = px;
                            if (py < minY) minY = py; if (py > maxY) maxY = py;
                            if (pz < minZ) minZ = pz; if (pz > maxZ) maxZ = pz;

                            if (ni >= 0 && ni < vnList.Count)
                            {
                                var (nx, ny, nz) = vnList[ni];
                                normals.Add(nx); normals.Add(ny); normals.Add(nz);
                            }
                            else
                            {
                                normals.Add(fnx); normals.Add(fny); normals.Add(fnz);
                            }
                        }
                    }
                }
            }

            if (submeshes.Count > 0)
                submeshes[^1].VertexCount = positions.Count / 3 - submeshes[^1].VertexStart;
            submeshes.RemoveAll(s => s.VertexCount <= 0);
            ComputeSubmeshBounds(positions, submeshes);

            bool empty = vList.Count == 0;
            return new ParsedMesh
            {
                Positions    = positions.ToArray(),
                Normals      = normals.ToArray(),
                UVs          = uvs.ToArray(),
                TriangleCount = positions.Count / 9,
                BoundsMin    = empty ? Vector3.Zero : new Vector3(minX, minY, minZ),
                BoundsMax    = empty ? Vector3.Zero : new Vector3(maxX, maxY, maxZ),
                Submeshes    = submeshes.Count > 1 ? submeshes : new()
            };
        }

        private readonly struct MtlMaterial
        {
            public readonly float R, G, B;
            public readonly string? TexturePath;
            public MtlMaterial(float r, float g, float b, string? texturePath)
            {
                R = r; G = g; B = b; TexturePath = texturePath;
            }
        }

        private static Dictionary<string, MtlMaterial> ParseMtl(string mtlPath)
        {
            var result = new Dictionary<string, MtlMaterial>(StringComparer.OrdinalIgnoreCase);
            string? mtlDir = Path.GetDirectoryName(Path.GetFullPath(mtlPath));
            string? name = null;
            float r = 0.8f, g = 0.8f, b = 0.8f;
            string? texturePath = null;

            void Flush()
            {
                if (name != null)
                    result[name] = new MtlMaterial(r, g, b, texturePath);
            }

            foreach (var rawLine in File.ReadLines(mtlPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                if (StartsWithToken(line, "newmtl") && parts.Length >= 2)
                {
                    Flush();
                    name = parts[1];
                    r = g = b = 0.8f;
                    texturePath = null;
                }
                else if (StartsWithToken(line, "Kd") && parts.Length >= 4)
                {
                    r = ParseF(parts[1]); g = ParseF(parts[2]); b = ParseF(parts[3]);
                }
                else if (StartsWithToken(line, "map_Kd") && parts.Length >= 2 && mtlDir != null)
                {
                    string candidate = Path.Combine(mtlDir, parts[^1]);
                    if (File.Exists(candidate))
                        texturePath = candidate;
                }
            }
            Flush();
            return result;
        }

        private static bool StartsWithToken(string line, string token)
        {
            if (!line.StartsWith(token)) return false;
            int len = token.Length;
            return line.Length > len && (line[len] == ' ' || line[len] == '\t');
        }

        private static ParsedMesh? ParseWithAssimp(string path)
        {
            using var importer = new AssimpContext();
            var scene = importer.ImportFile(path,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateSmoothNormals |
                PostProcessSteps.JoinIdenticalVertices |
                PostProcessSteps.FlipUVs);

            if (scene == null) return null;
            if (scene.MeshCount == 0 && !scene.HasAnimations) return null;

            var positions = new List<float>();
            var normals = new List<float>();
            var uvs = new List<float>();
            var submeshes = new List<MeshSubmesh>();
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            // ── Skinning: tabla global de huesos + influencias por vértice expandido ──
            var boneIdx = new List<int>();
            var boneWt = new List<float>();
            var boneNames = new List<string>();
            var boneOffsets = new List<System.Numerics.Matrix4x4>();
            var boneTable = new Dictionary<string, int>(StringComparer.Ordinal);

            // Nombre del NODO (objeto) que referencia cada malla → para nombrar los hijos al partir
            // (como Unity: el hijo se llama como el objeto, no como la malla/material).
            var meshNodeNames = new Dictionary<int, string>();
            if (scene.RootNode != null)
                MapMeshNodeNames(scene.RootNode, meshNodeNames);

            int meshOrdinal = -1;
            foreach (var mesh in scene.Meshes)
            {
                meshOrdinal++;
                int vertexStart = positions.Count / 3;

                // Mapa vértice-original → influencias (índice global de hueso, peso).
                Dictionary<int, List<(int bi, float w)>>? influences = null;
                if (mesh.HasBones)
                {
                    influences = new();
                    foreach (var bone in mesh.Bones)
                    {
                        if (!boneTable.TryGetValue(bone.Name, out int gi))
                        {
                            gi = boneNames.Count;
                            boneTable[bone.Name] = gi;
                            boneNames.Add(bone.Name);
                            boneOffsets.Add(AssimpToNumerics(bone.OffsetMatrix));
                        }
                        foreach (var vw in bone.VertexWeights)
                        {
                            if (!influences.TryGetValue(vw.VertexID, out var list)) { list = new(); influences[vw.VertexID] = list; }
                            list.Add((gi, vw.Weight));
                        }
                    }
                }

                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount < 3) continue;
                    for (int i = 1; i < face.IndexCount - 1; i++)
                    {
                        AddAssimpVertex(mesh, face.Indices[0], positions, normals, uvs,
                            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                        AppendInfluences(influences, face.Indices[0], boneIdx, boneWt);
                        AddAssimpVertex(mesh, face.Indices[i], positions, normals, uvs,
                            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                        AppendInfluences(influences, face.Indices[i], boneIdx, boneWt);
                        AddAssimpVertex(mesh, face.Indices[i + 1], positions, normals, uvs,
                            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                        AppendInfluences(influences, face.Indices[i + 1], boneIdx, boneWt);
                    }
                }

                int vertexCount = positions.Count / 3 - vertexStart;
                if (vertexCount <= 0) continue;

                string objectName = meshNodeNames.TryGetValue(meshOrdinal, out var nn) && !string.IsNullOrWhiteSpace(nn)
                    ? nn
                    : (mesh.Name ?? "");
                var submesh = new MeshSubmesh
                {
                    Name = string.IsNullOrWhiteSpace(mesh.Name) ? $"Material {submeshes.Count + 1}" : mesh.Name,
                    MeshName = objectName,
                    VertexStart = vertexStart,
                    VertexCount = vertexCount
                };

                if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
                    ApplyAssimpMaterial(scene.Materials[mesh.MaterialIndex], scene, path, submesh);

                submeshes.Add(submesh);
            }

            bool hasGeometry = positions.Count > 0;

            // Escala recomendada (estilo Unity, 1u=1m). AssimpNet 4.1.0 no expone el
            // UnitScaleFactor del FBX, así que usamos una heurística por tamaño: los modelos
            // en centímetros (Mixamo/Blender) salen enormes (decenas/cientos de unidades) →
            // se reducen ×0.01 para quedar en metros. Modelos ya en metros se dejan igual.
            float recommendedScale = 1f;
            if (hasGeometry)
            {
                float maxDim = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));
                if (maxDim > 8f)
                    recommendedScale = 0.01f;
            }

            // Jerarquía de huesos (solo si el modelo tiene esqueleto/animaciones).
            ModelNode? hierarchy = null;
            try
            {
                bool hasBones = scene.HasMeshes && scene.Meshes.Any(m => m.HasBones);
                if (scene.RootNode != null && (hasBones || scene.HasAnimations))
                {
                    int budget = 1024;
                    hierarchy = ConvertNode(scene.RootNode, ref budget);
                }
            }
            catch { hierarchy = null; }

            var animations = new List<ModelAnimationInfo>();
            var clips = new List<SkeletalClip>();
            try
            {
                if (scene.HasAnimations)
                {
                    int n = 0;
                    foreach (var anim in scene.Animations)
                    {
                        double tps = anim.TicksPerSecond > 0.0001 ? anim.TicksPerSecond : 25.0;
                        string clipName = string.IsNullOrWhiteSpace(anim.Name) ? $"Animation {++n}" : anim.Name;
                        float dur = (float)(anim.DurationInTicks / tps);

                        animations.Add(new ModelAnimationInfo
                        {
                            Name = clipName,
                            DurationSeconds = dur,
                            ChannelCount = anim.NodeAnimationChannelCount
                        });

                        var clip = new SkeletalClip { Name = clipName, Duration = dur };
                        foreach (var ch in anim.NodeAnimationChannels)
                        {
                            var bc = new BoneChannel { NodeName = ch.NodeName ?? "" };
                            foreach (var k in ch.PositionKeys)
                                bc.Positions.Add(new BoneVecKey { Time = (float)(k.Time / tps), Value = new Vector3(k.Value.X, k.Value.Y, k.Value.Z) });
                            foreach (var k in ch.RotationKeys)
                                bc.Rotations.Add(new BoneQuatKey { Time = (float)(k.Time / tps), Value = new MiMotor.Mathematics.Quaternion(k.Value.X, k.Value.Y, k.Value.Z, k.Value.W) });
                            foreach (var k in ch.ScalingKeys)
                                bc.Scales.Add(new BoneVecKey { Time = (float)(k.Time / tps), Value = new Vector3(k.Value.X, k.Value.Y, k.Value.Z) });
                            clip.Channels.Add(bc);
                        }
                        clips.Add(clip);
                    }
                }
            }
            catch { animations.Clear(); clips.Clear(); }

            ComputeSubmeshBounds(positions, submeshes);
            int separateObjects = scene.RootNode != null ? CountMeshNodes(scene.RootNode) : 0;

            return new ParsedMesh
            {
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                UVs = uvs.ToArray(),
                TriangleCount = positions.Count / 9,
                BoundsMin = hasGeometry ? new Vector3(minX, minY, minZ) : Vector3.Zero,
                BoundsMax = hasGeometry ? new Vector3(maxX, maxY, maxZ) : Vector3.Zero,
                Submeshes = submeshes.Count > 1 ? submeshes : new(),
                SeparateObjectCount = separateObjects,
                Hierarchy = hierarchy,
                Animations = animations,
                Clips = clips,
                RecommendedScale = recommendedScale,
                HasSkin = boneNames.Count > 0 && boneIdx.Count == (positions.Count / 3) * 4,
                BoneIndices = boneIdx.ToArray(),
                BoneWeights = boneWt.ToArray(),
                BoneNames = boneNames,
                BoneOffsets = boneOffsets
            };
        }

        // Vuelca el color difuso y la textura (si la hay) de un material de Assimp en
        // la sub-malla, para poder crear un .mat con un punto de partida razonable.
        private static void ApplyAssimpMaterial(Assimp.Material mat, Scene scene, string modelPath, MeshSubmesh submesh)
        {
            if (!string.IsNullOrWhiteSpace(mat.Name))
                submesh.Name = mat.Name;

            if (mat.HasColorDiffuse)
            {
                submesh.DiffuseR = Math.Clamp(mat.ColorDiffuse.R, 0f, 1f);
                submesh.DiffuseG = Math.Clamp(mat.ColorDiffuse.G, 0f, 1f);
                submesh.DiffuseB = Math.Clamp(mat.ColorDiffuse.B, 0f, 1f);
            }

            if (!mat.GetMaterialTexture(TextureType.Diffuse, 0, out var slot) || string.IsNullOrWhiteSpace(slot.FilePath))
                return;

            string filePath = slot.FilePath;
            if (filePath.StartsWith("*", StringComparison.Ordinal))
            {
                submesh.TexturePath = ExtractEmbeddedTexture(scene, modelPath, filePath, submesh.Name);
                return;
            }

            string? modelDir = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            if (modelDir == null) return;

            string candidate = Path.GetFullPath(Path.Combine(modelDir, filePath.Replace('\\', '/')));
            if (File.Exists(candidate))
                submesh.TexturePath = candidate;
            else
            {
                // Algunos exportadores guardan solo el nombre de archivo aunque la ruta original sea distinta.
                string byName = Path.Combine(modelDir, Path.GetFileName(filePath));
                if (File.Exists(byName))
                    submesh.TexturePath = byName;
            }
        }

        // Extrae una textura embebida (FBX/glb) a un archivo junto al modelo, para
        // que el resto del pipeline (que trabaja con rutas de archivo) pueda usarla.
        private static string? ExtractEmbeddedTexture(Scene scene, string modelPath, string reference, string materialName)
        {
            if (!int.TryParse(reference.TrimStart('*'), out int index) || index < 0 || index >= scene.TextureCount)
                return null;

            var tex = scene.Textures[index];
            string? modelDir = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            if (modelDir == null) return null;

            string ext = string.IsNullOrWhiteSpace(tex.CompressedFormatHint) ? "png" : tex.CompressedFormatHint.ToLowerInvariant();
            string safeName = string.Concat((Path.GetFileNameWithoutExtension(modelPath) + "_" + materialName)
                .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
            string outPath = Path.Combine(modelDir, $"{safeName}_embedded{index}.{ext}");

            try
            {
                if (!File.Exists(outPath))
                {
                    if (tex.IsCompressed && tex.CompressedData != null)
                        File.WriteAllBytes(outPath, tex.CompressedData);
                    else
                        return null; // datos sin comprimir (raw RGBA) no soportados por el visor de assets actual
                }
                return outPath;
            }
            catch
            {
                return null;
            }
        }

        private static void AddAssimpVertex(
            Mesh mesh,
            int index,
            List<float> positions,
            List<float> normals,
            List<float> uvs,
            ref float minX, ref float minY, ref float minZ,
            ref float maxX, ref float maxY, ref float maxZ)
        {
            var v = mesh.Vertices[index];
            positions.Add(v.X); positions.Add(v.Y); positions.Add(v.Z);
            if (mesh.HasTextureCoords(0))
            {
                var uv = mesh.TextureCoordinateChannels[0][index];
                uvs.Add(uv.X); uvs.Add(uv.Y);
            }
            else
            {
                uvs.Add(0f); uvs.Add(0f);
            }

            if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
            if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
            if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;

            if (mesh.HasNormals)
            {
                var n = mesh.Normals[index];
                normals.Add(n.X); normals.Add(n.Y); normals.Add(n.Z);
            }
            else
            {
                normals.Add(0f); normals.Add(1f); normals.Add(0f);
            }
        }

        private static ParsedMesh ParseFbxAscii(string path)
        {
            string text = File.ReadAllText(path);
            if (text.StartsWith("Kaydara FBX Binary", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FBX binario no soportado por el importador simple.");

            var verticesRaw = ExtractFbxFloatArray(text, "Vertices");
            var indicesRaw = ExtractFbxIntArray(text, "PolygonVertexIndex");
            if (verticesRaw.Count < 9 || indicesRaw.Count < 3)
                return EmptyMesh();

            var vList = new List<(float x, float y, float z)>();
            for (int i = 0; i + 2 < verticesRaw.Count; i += 3)
                vList.Add((verticesRaw[i], verticesRaw[i + 1], verticesRaw[i + 2]));

            var positions = new List<float>();
            var normals = new List<float>();
            var uvs = new List<float>();
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            var polygon = new List<int>();
            foreach (int raw in indicesRaw)
            {
                bool end = raw < 0;
                int index = end ? -raw - 1 : raw;
                polygon.Add(index);

                if (!end) continue;
                if (polygon.Count >= 3)
                {
                    for (int i = 1; i < polygon.Count - 1; i++)
                    {
                        var tri = new[] { polygon[0], polygon[i], polygon[i + 1] };
                        AddTriangle(vList, tri, positions, normals, uvs,
                            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                    }
                }
                polygon.Clear();
            }

            if (positions.Count == 0) return EmptyMesh();
            return new ParsedMesh
            {
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                UVs = uvs.ToArray(),
                TriangleCount = positions.Count / 9,
                BoundsMin = new Vector3(minX, minY, minZ),
                BoundsMax = new Vector3(maxX, maxY, maxZ)
            };
        }

        private static void AddTriangle(
            List<(float x, float y, float z)> vertices,
            int[] tri,
            List<float> positions,
            List<float> normals,
            List<float> uvs,
            ref float minX, ref float minY, ref float minZ,
            ref float maxX, ref float maxY, ref float maxZ)
        {
            var p0 = GetV(vertices, tri[0]);
            var p1 = GetV(vertices, tri[1]);
            var p2 = GetV(vertices, tri[2]);

            float ex1 = p1.x - p0.x, ey1 = p1.y - p0.y, ez1 = p1.z - p0.z;
            float ex2 = p2.x - p0.x, ey2 = p2.y - p0.y, ez2 = p2.z - p0.z;
            float fnx = ey1 * ez2 - ez1 * ey2;
            float fny = ez1 * ex2 - ex1 * ez2;
            float fnz = ex1 * ey2 - ey1 * ex2;
            float fl = MathF.Sqrt(fnx * fnx + fny * fny + fnz * fnz);
            if (fl > 0.0001f) { fnx /= fl; fny /= fl; fnz /= fl; }

            foreach (int pi in tri)
            {
                var (px, py, pz) = GetV(vertices, pi);
                positions.Add(px); positions.Add(py); positions.Add(pz);
                normals.Add(fnx); normals.Add(fny); normals.Add(fnz);
                uvs.Add(0f); uvs.Add(0f);

                if (px < minX) minX = px; if (px > maxX) maxX = px;
                if (py < minY) minY = py; if (py > maxY) maxY = py;
                if (pz < minZ) minZ = pz; if (pz > maxZ) maxZ = pz;
            }
        }

        private static List<float> ExtractFbxFloatArray(string text, string name) =>
            ExtractFbxArrayText(text, name).Select(ParseF).ToList();

        private static List<int> ExtractFbxIntArray(string text, string name) =>
            ExtractFbxArrayText(text, name)
                .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0)
                .ToList();

        private static IEnumerable<string> ExtractFbxArrayText(string text, string name)
        {
            int start = text.IndexOf(name + ":", StringComparison.OrdinalIgnoreCase);
            if (start < 0) yield break;

            int arrayStart = text.IndexOf("a:", start, StringComparison.OrdinalIgnoreCase);
            if (arrayStart < 0) yield break;
            arrayStart += 2;

            int arrayEnd = text.IndexOf('}', arrayStart);
            if (arrayEnd < 0) arrayEnd = text.Length;

            string arrayText = text.Substring(arrayStart, arrayEnd - arrayStart);
            foreach (Match match in Regex.Matches(arrayText, @"[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?"))
                yield return match.Value;
        }

        // Mapea cada índice de malla → nombre del nodo (objeto) que la referencia en el FBX.
        private static void MapMeshNodeNames(Node node, Dictionary<int, string> map)
        {
            if (node.HasMeshes)
                foreach (int mi in node.MeshIndices)
                    if (!map.ContainsKey(mi))
                        map[mi] = node.Name ?? "";

            foreach (var child in node.Children)
                MapMeshNodeNames(child, map);
        }

        // Cuenta los nodos que tienen al menos una malla = nº de objetos separados de verdad.
        // (Un objeto con varios materiales es UN nodo con varias mallas → cuenta 1.)
        private static int CountMeshNodes(Node node)
        {
            int count = node.HasMeshes && node.MeshCount > 0 ? 1 : 0;
            foreach (var child in node.Children)
                count += CountMeshNodes(child);
            return count;
        }

        // Calcula los bounds locales de cada submalla escaneando su rango de vértices.
        private static void ComputeSubmeshBounds(List<float> positions, List<MeshSubmesh> submeshes)
        {
            foreach (var sub in submeshes)
            {
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                int end = sub.VertexStart + sub.VertexCount;
                for (int v = sub.VertexStart; v < end; v++)
                {
                    int p = v * 3;
                    if (p + 2 >= positions.Count) break;
                    float x = positions[p], y = positions[p + 1], z = positions[p + 2];
                    if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
                    if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
                }
                if (minX <= maxX)
                {
                    sub.MinX = minX; sub.MinY = minY; sub.MinZ = minZ;
                    sub.MaxX = maxX; sub.MaxY = maxY; sub.MaxZ = maxZ;
                }
            }
        }

        private static ParsedMesh EmptyMesh() => new ParsedMesh
        {
            Positions = Array.Empty<float>(),
            Normals = Array.Empty<float>(),
            UVs = Array.Empty<float>(),
            TriangleCount = 0,
            BoundsMin = Vector3.Zero,
            BoundsMax = Vector3.Zero
        };

        private static (int p, int t, int n) ParseFaceVert(string token, int vCount, int vtCount, int vnCount)
        {
            var parts = token.Split('/');
            int p = ParseIdx(parts[0], vCount);
            int t = parts.Length >= 2 && parts[1].Length > 0
                ? ParseIdx(parts[1], vtCount) : -1;
            int n = parts.Length >= 3 && parts[2].Length > 0
                ? ParseIdx(parts[2], vnCount) : -1;
            return (p, t, n);
        }

        private static int ParseIdx(string s, int count)
        {
            if (!int.TryParse(s, out int idx)) return 0;
            return idx < 0 ? count + idx : idx - 1; // OBJ es 1-indexado
        }

        private static float ParseF(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static (float x, float y, float z) GetV(List<(float x, float y, float z)> list, int idx)
        {
            if (idx < 0 || idx >= list.Count) return (0, 0, 0);
            return list[idx];
        }
    }
}
