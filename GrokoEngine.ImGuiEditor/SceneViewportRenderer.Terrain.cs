using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GrokoShaderGraphPro.Models;
using GrokoShaderGraphPro.Services;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
private void BuildTerrainGeometry(GameObject obj, Terrain terrain, ParsedMesh mesh, string meshPath, bool selected, Matrix4 transform)
    {
        Vector4 fallbackColor = new Vector4(0.62f, 0.72f, 1f, 1f);
        Vector4 color = GetObjectColor(obj, fallbackColor);
        Vector4 lineColor = selected ? new Vector4(1f, 0.78f, 0.12f, 1f) : color;

        var min = mesh.BoundsMin;
        var max = mesh.BoundsMax;

        int start = ActiveSolidVertices.Count;
        AddMeshTriangles(mesh, transform, 1f, color, meshPath, new Vector4(0f, 0.9f, 0f, 0f), Vector4.Zero, 0, mesh.Positions.Length / 3);
        int count = ActiveSolidVertices.Count - start;
        if (count > 0)
            ActiveSolidRanges.Add(new SolidRange(start, count, null, null, null, null, null, null, null, obj));

        Span<Vector3> corners = stackalloc Vector3[]
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
        };

        for (int i = 0; i < corners.Length; i++)
            corners[i] = Vector3.TransformPosition(corners[i], transform);

        if (ShowObjectWireframes || selected)
        {
            AddLine(corners[0], corners[1], lineColor); AddLine(corners[1], corners[2], lineColor);
            AddLine(corners[2], corners[3], lineColor); AddLine(corners[3], corners[0], lineColor);
            AddLine(corners[4], corners[5], lineColor); AddLine(corners[5], corners[6], lineColor);
            AddLine(corners[6], corners[7], lineColor); AddLine(corners[7], corners[4], lineColor);
            AddLine(corners[0], corners[4], lineColor); AddLine(corners[1], corners[5], lineColor);
            AddLine(corners[2], corners[6], lineColor); AddLine(corners[3], corners[7], lineColor);
        }
    }

// Arboles plantados (Terrain.Trees): SIEMPRE por el pase dinámico (ver BuildObjectRecursive),
// nunca horneados en el batch estático — se encolan vía TryQueueDynamicMeshDraw, la misma cola
// que usa cualquier malla dinámica. Eso los hace elegibles para el instancing por GPU que ya
// existe ahí (CanInstanceTogether agrupa por mesh+textura+color+material; todos los árboles del
// mismo prototipo comparten esos cuatro, así que terminan en una sola llamada
// glDrawArraysInstanced por prototipo en vez de un draw/triángulos sueltos por árbol).
// La altura de cada árbol se resamplea del heightmap actual en cada frame, así que si esculpís
// el terreno después de plantar, los árboles ya plantados "flotan" solos hasta la nueva altura.
private readonly List<(int Index, float DistanceSq)> _terrainTreeSortScratch = new(256);

private void QueueTerrainTreeInstances(Terrain terrain, Matrix4 transform, Frustum frustum, Vector3 cameraPosition)
    {
        if (terrain.Trees.Count == 0 || terrain.TreePrototypes.Count == 0)
            return;

        int maxMeshTrees = Math.Max(0, terrain.MaxMeshTrees);

        // Estilo Unity "Tree Distance"/"Billboard Start"/"Max Mesh Trees": más allá de
        // TreeDistance un árbol ni se procesa. De los que quedan, un árbol es malla 3D solo si
        // está más cerca que BillboardStart Y entra en el ranking de los maxMeshTrees más
        // cercanos a cámara; el resto es billboard (quad plano que rota en Y hacia la cámara).
        float treeDistance = Math.Max(0.01f, terrain.TreeDistance);
        float treeDistanceSq = treeDistance * treeDistance;
        float billboardStart = Math.Max(0f, terrain.BillboardStart);
        float billboardStartSq = billboardStart * billboardStart;

        _terrainTreeSortScratch.Clear();
        for (int i = 0; i < terrain.Trees.Count; i++)
        {
            var instance = terrain.Trees[i];
            if (instance.PrototypeIndex < 0 || instance.PrototypeIndex >= terrain.TreePrototypes.Count)
                continue;

            float localHeight = terrain.GetHeightLocal(instance.LocalX, instance.LocalZ);
            var worldPos = Vector3.TransformPosition(new Vector3(instance.LocalX, localHeight, instance.LocalZ), transform);
            float dx = worldPos.X - cameraPosition.X, dy = worldPos.Y - cameraPosition.Y, dz = worldPos.Z - cameraPosition.Z;
            float distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > treeDistanceSq)
                continue;

            _terrainTreeSortScratch.Add((i, distSq));
        }

        if (_terrainTreeSortScratch.Count == 0)
            return;

        // El ranking por distancia decide qué entra como malla 3D (los maxMeshTrees más cercanos)
        // — necesario aunque no hubiera límite de billboard, porque también define el orden.
        _terrainTreeSortScratch.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));

        var tint = new Vector4(1f, 1f, 1f, 1f);
        var material = new Vector4(0f, 0.5f, 1f, 1f); // metallic 0, roughness 0.5 por defecto

        for (int rank = 0; rank < _terrainTreeSortScratch.Count; rank++)
        {
            var (treeIndex, distSq) = _terrainTreeSortScratch[rank];
            var instance = terrain.Trees[treeIndex];
            string protoPath = terrain.TreePrototypes[instance.PrototypeIndex];
            protoPath = NormalizeExistingAssetPath(protoPath) ?? protoPath;
            if (string.IsNullOrWhiteSpace(protoPath) || !ObjLoader.IsSupportedMesh(protoPath))
                continue;

            var mesh = GetParsedMesh(protoPath);
            if (mesh == null || mesh.Positions.Length == 0)
                continue;

            float localHeight = terrain.GetHeightLocal(instance.LocalX, instance.LocalZ);
            var localPos = new Vector3(instance.LocalX, localHeight, instance.LocalZ);

            var worldPos = Vector3.TransformPosition(localPos, transform);
            var extent = (mesh.BoundsMax - mesh.BoundsMin) * Math.Max(0.0001f, instance.Scale);
            float boundRadius = Math.Max(0.1f, extent.Length * 0.5f);
            if (!frustum.ContainsSphere(worldPos, boundRadius))
                continue;

            bool isBillboard = rank >= maxMeshTrees || distSq > billboardStartSq;

            if (!isBillboard)
            {
                Matrix4 instanceWorld =
                    Matrix4.CreateScale(Math.Max(0.0001f, instance.Scale)) *
                    Matrix4.CreateRotationY(MathHelper.DegreesToRadians(instance.RotationY)) *
                    Matrix4.CreateTranslation(localPos.X, localPos.Y, localPos.Z) *
                    transform;

                if (mesh.Submeshes.Count > 1)
                {
                    foreach (var sub in mesh.Submeshes)
                    {
                        TryQueueDynamicMeshDraw(mesh, instanceWorld, 1f, tint, protoPath, material, Vector4.Zero,
                            sub.VertexStart, sub.VertexCount, sub.TexturePath, null, null, null, null, null, null);
                    }
                }
                else
                {
                    string? texturePath = mesh.Submeshes.Count > 0 ? mesh.Submeshes[0].TexturePath : null;
                    TryQueueDynamicMeshDraw(mesh, instanceWorld, 1f, tint, protoPath, material, Vector4.Zero,
                        0, mesh.Positions.Length / 3, texturePath, null, null, null, null, null, null);
                }
            }
            else
            {
                QueueTreeBillboard(worldPos, extent, cameraPosition, mesh);
            }
        }
    }

// Detalles/pasto (Terrain.Details): siempre billboard, nunca malla — Unity's clásico "Detail
// Texture" tampoco tiene LOD de malla para pasto. Density Scale reduce cuántos de los plantados
// se dibujan de verdad SIN borrar los datos pintados, usando un hash estable por índice (no un
// módulo, para que el recorte no se vea como un patrón en grilla).
private static float StableUnitHash(int i)
    {
        uint x = (uint)i * 2654435761u;
        x ^= x >> 15;
        return (x & 0xFFFFFF) / (float)0xFFFFFF;
    }

private void QueueTerrainDetailInstances(Terrain terrain, Matrix4 transform, Frustum frustum, Vector3 cameraPosition)
    {
        if (terrain.Details.Count == 0 || terrain.DetailPrototypes.Count == 0)
            return;

        float densityScale = Math.Clamp(terrain.DetailDensityScale, 0f, 1f);
        if (densityScale <= 0f)
            return;

        float detailDistance = Math.Max(0.01f, terrain.DetailDistance);
        float detailDistanceSq = detailDistance * detailDistance;

        for (int i = 0; i < terrain.Details.Count; i++)
        {
            if (StableUnitHash(i) > densityScale)
                continue;

            var instance = terrain.Details[i];
            if (instance.PrototypeIndex < 0 || instance.PrototypeIndex >= terrain.DetailPrototypes.Count)
                continue;

            var proto = terrain.DetailPrototypes[instance.PrototypeIndex];
            if (string.IsNullOrWhiteSpace(proto.TexturePath))
                continue;

            float localHeight = terrain.GetHeightLocal(instance.LocalX, instance.LocalZ);
            var localPos = new Vector3(instance.LocalX, localHeight, instance.LocalZ);
            var worldPos = Vector3.TransformPosition(localPos, transform);

            float dx = worldPos.X - cameraPosition.X, dy = worldPos.Y - cameraPosition.Y, dz = worldPos.Z - cameraPosition.Z;
            float distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > detailDistanceSq)
                continue;

            float scale = Math.Max(0.0001f, instance.Scale);
            float halfWidth = Math.Max(0.02f, proto.Width * scale * 0.5f);
            float height = Math.Max(0.02f, proto.Height * scale);
            float boundRadius = MathF.Max(halfWidth, height * 0.5f) * 1.5f;
            if (!frustum.ContainsSphere(worldPos, boundRadius))
                continue;

            string texturePath = NormalizeExistingAssetPath(proto.TexturePath) ?? proto.TexturePath;
            float camDx = cameraPosition.X - worldPos.X;
            float camDz = cameraPosition.Z - worldPos.Z;
            float yaw = (MathF.Abs(camDx) > 1e-5f || MathF.Abs(camDz) > 1e-5f) ? MathF.Atan2(camDx, camDz) : 0f;

            if (!_detailBillboardsByTexture.TryGetValue(texturePath, out var list))
            {
                list = new List<TreeBillboardInstance>();
                _detailBillboardsByTexture[texturePath] = list;
            }
            list.Add(new TreeBillboardInstance(worldPos, halfWidth, height, yaw, new Vector4(1f, 1f, 1f, 1f)));
        }
    }

// Encola un árbol como billboard: quad plano, ancho X/Z tomado del bounding box de la malla real
// (escalado por Scale de la instancia), anclado al piso, que rota SOLO en Y para mirar siempre
// a cámara (no billboard esférico como las partículas). Simplificación deliberada respecto a
// Unity: usa la MISMA textura del prototipo 3D directo sobre el quad, en vez de un snapshot
// recortado horneado — más barato de implementar, algo menos fiel visualmente de cerca (que es
// justo donde no se usa: los billboards solo aparecen lejos o fuera del ranking de malla).
private void QueueTreeBillboard(Vector3 worldPos, MiMotor.Mathematics.Vector3 extent, Vector3 cameraPosition, ParsedMesh mesh)
    {
        string texturePath = mesh.Submeshes.Count > 0 ? (mesh.Submeshes[0].TexturePath ?? "") : "";
        if (!string.IsNullOrWhiteSpace(texturePath))
            texturePath = NormalizeExistingAssetPath(texturePath) ?? texturePath;

        float halfWidth = Math.Max(0.05f, MathF.Max(extent.X, extent.Z) * 0.5f);
        float height = Math.Max(0.05f, extent.Y);

        float dx = cameraPosition.X - worldPos.X;
        float dz = cameraPosition.Z - worldPos.Z;
        float yaw = (MathF.Abs(dx) > 1e-5f || MathF.Abs(dz) > 1e-5f) ? MathF.Atan2(dx, dz) : 0f;

        if (!_treeBillboardsByTexture.TryGetValue(texturePath, out var list))
        {
            list = new List<TreeBillboardInstance>();
            _treeBillboardsByTexture[texturePath] = list;
        }
        list.Add(new TreeBillboardInstance(worldPos, halfWidth, height, yaw, new Vector4(1f, 1f, 1f, 1f)));
    }

// Dibuja los billboards acumulados este frame (árboles fuera de rango de malla, o detalles/
// pasto — siempre billboard), agrupados por textura — una llamada glDrawArraysInstanced por
// textura distinta. Método compartido: mismo shader/VAO para árboles-billboard y detalles.
private void DrawTreeBillboardInstances(ref Matrix4 mvp, Dictionary<string, List<TreeBillboardInstance>> billboardsByTexture)
    {
        if (billboardsByTexture.Count == 0 || _treeBillboardShader == 0)
            return;

        GL.Enable(EnableCap.CullFace);
        GL.UseProgram(_treeBillboardShader);
        GL.UniformMatrix4(_treeBillboardMvpLoc, true, ref mvp);
        GL.Uniform1(_treeBillboardTexLoc, 0);
        GL.BindVertexArray(_treeBillboardVao);
        GL.ActiveTexture(TextureUnit.Texture0);

        foreach (var (texturePath, list) in billboardsByTexture)
        {
            if (list.Count == 0)
                continue;

            bool hasTex = !string.IsNullOrWhiteSpace(texturePath) && File.Exists(texturePath);
            int texId = hasTex ? GetTexture(texturePath) : 0;
            GL.Uniform1(_treeBillboardHasTexLoc, hasTex ? 1 : 0);
            GL.BindTexture(TextureTarget.Texture2D, texId);

            if (_treeBillboardUploadBuffer.Length < list.Count)
                Array.Resize(ref _treeBillboardUploadBuffer, list.Count);
            list.CopyTo(_treeBillboardUploadBuffer);

            int bytes = list.Count * Unsafe.SizeOf<TreeBillboardInstance>();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _treeBillboardInstanceVbo);
            if (bytes > _treeBillboardInstanceBufferCapacity)
            {
                _treeBillboardInstanceBufferCapacity = Math.Max(bytes, _treeBillboardInstanceBufferCapacity == 0 ? 8192 : _treeBillboardInstanceBufferCapacity * 2);
                GL.BufferData(BufferTarget.ArrayBuffer, _treeBillboardInstanceBufferCapacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, _treeBillboardUploadBuffer);

            TrackMainDraw(6, list.Count);
            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, list.Count);

            list.Clear();
        }

        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

// Resuelve un vecino por EditorId contra el índice armado una vez por frame en Render(). Null si
// no hay ID configurado o si el GameObject referenciado ya no existe/no tiene Terrain.
private Terrain? ResolveTerrainNeighbor(string? neighborId) =>
    !string.IsNullOrEmpty(neighborId) && _terrainsByEditorId.TryGetValue(neighborId, out var neighbor) ? neighbor : null;

private (ParsedMesh Mesh, string MeshPath) GetTerrainMesh(GameObject obj, Terrain terrain)
    {
        var left = ResolveTerrainNeighbor(terrain.LeftNeighborId);
        var right = ResolveTerrainNeighbor(terrain.RightNeighborId);
        var top = ResolveTerrainNeighbor(terrain.TopNeighborId);
        var bottom = ResolveTerrainNeighbor(terrain.BottomNeighborId);

        // Las normales del borde de ESTE terreno dependen de la altura del vecino, así que si el
        // vecino cambió su Heightmap (su propio Version subió) hay que regenerar esta malla
        // también, no solo cuando cambia el propio Version.
        int combinedVersion = HashCode.Combine(terrain.Version, left?.Version ?? -1, right?.Version ?? -1, top?.Version ?? -1, bottom?.Version ?? -1);

        if (terrainMeshCache.TryGetValue(obj.EditorId, out var cached) && cached.Version == combinedVersion)
            return (cached.Mesh, $"terrain://{obj.EditorId}/v{cached.Version}");

        var mesh = TerrainMeshGenerator.Generate(terrain, left, right, top, bottom);
        terrainMeshCache[obj.EditorId] = (mesh, combinedVersion);
        return (mesh, $"terrain://{obj.EditorId}/v{combinedVersion}");
    }

private void ApplyTerrainLighting(IReadOnlyList<GameObject> objects)
    {
        var ambient = FindComponent<AmbientLight>(objects);
        var ambColor = ambient != null
            ? new Vector3(ambient.R, ambient.G, ambient.B)
            : new Vector3(0.3f, 0.32f, 0.36f);
        float ambIntensity = ambient?.Intensity ?? 0.22f;
        GL.Uniform3(terrainAmbientColorLocation, ambColor);
        GL.Uniform1(terrainAmbientIntensityLocation, ambIntensity);
        GL.Uniform1(terrainSkyStrengthLocation, ambient?.SkyStrength ?? 0.08f);

        var dir = FindPrimaryDirectionalLight(objects);
        if (dir != null && dir.gameObject != null)
        {
            GL.Uniform3(terrainDirDirLocation, ToTk(dir.GetNormalizedDirection()).Normalized());
            GL.Uniform3(terrainDirColorLocation, new Vector3(dir.R, dir.G, dir.B));
            GL.Uniform1(terrainDirIntensityLocation, dir.Intensity);
        }
        else
        {
            GL.Uniform3(terrainDirDirLocation, new Vector3(0f, -1f, 0f));
            GL.Uniform3(terrainDirColorLocation, Vector3.Zero);
            GL.Uniform1(terrainDirIntensityLocation, 0f);
        }
    }

private int GetOrUpdateSplatTexture(GameObject obj, Terrain terrain)
    {
        if (terrainSplatTextureCache.TryGetValue(obj.EditorId, out var cached) && cached.Version == terrain.SplatVersion)
            return cached.TextureId;

        terrain.EnsureSplatMapSize();

        int textureId = cached.TextureId;
        if (textureId == 0)
            textureId = GL.GenTexture();

        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            terrain.Resolution,
            terrain.Resolution,
            0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
            PixelType.UnsignedByte,
            terrain.SplatMap);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        terrainSplatTextureCache[obj.EditorId] = (textureId, terrain.SplatVersion);
        return textureId;
    }

private void RenderTerrainRanges(List<SolidRange> ranges, int vao, ref Matrix4 mvp, IReadOnlyList<GameObject> objects)
    {
        if (terrainShader == 0)
            return;

        bool used = false;
        foreach (var range in ranges)
        {
            if (range.TerrainObject is not { } obj) continue;
            var terrain = obj.GetComponent<Terrain>();
            if (terrain == null) continue;

            if (!used)
            {
                GL.UseProgram(terrainShader);
                GL.UniformMatrix4(terrainMvpLocation, true, ref mvp);
                var identity = Matrix4.Identity;
                GL.UniformMatrix4(terrainModelLocation, true, ref identity);
                ApplyTerrainLighting(objects);
                GL.BindVertexArray(vao);
                used = true;
            }

            int splatTexture = GetOrUpdateSplatTexture(obj, terrain);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, splatTexture);
            GL.Uniform1(terrainSplatMapLocation, 0);

            for (int i = 0; i < 4; i++)
            {
                string path = terrain.LayerTextures[i];
                int texUnit = i + 1;
                GL.ActiveTexture(TextureUnit.Texture0 + texUnit);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    int texId = GetTexture(path);
                    GL.BindTexture(TextureTarget.Texture2D, texId);
                    GL.Uniform1(terrainLayerLocations[i], texUnit);
                    GL.Uniform1(terrainHasLayerLocations[i], texId != 0 ? 1 : 0);
                }
                else
                {
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    GL.Uniform1(terrainLayerLocations[i], texUnit);
                    GL.Uniform1(terrainHasLayerLocations[i], 0);
                }
                GL.Uniform1(terrainTilingLocations[i], terrain.LayerTiling[i]);
            }

            TrackMainDraw(range.Count);
            GL.DrawArrays(PrimitiveType.Triangles, range.Start, range.Count);
        }

        if (used)
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
    }

private static int CreateTerrainShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aPosition;
            layout (location = 1) in vec3 aNormal;
            layout (location = 3) in vec2 aUv;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec2 vUv;
            void main()
            {
                vec4 world = vec4(aPosition, 1.0) * uModel;
                vec3 normalWs = (vec4(aNormal, 0.0) * uModel).xyz;
                vWorldPos = world.xyz;
                vNormal   = length(normalWs) > 0.0001 ? normalize(normalWs) : vec3(0.0, 1.0, 0.0);
                vUv = aUv;
                gl_Position = world * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec3 vWorldPos;
            in vec3 vNormal;
            in vec2 vUv;

            uniform sampler2D uSplatMap;
            uniform sampler2D uLayer0;
            uniform sampler2D uLayer1;
            uniform sampler2D uLayer2;
            uniform sampler2D uLayer3;
            uniform int uHasLayer0;
            uniform int uHasLayer1;
            uniform int uHasLayer2;
            uniform int uHasLayer3;
            uniform float uTiling0;
            uniform float uTiling1;
            uniform float uTiling2;
            uniform float uTiling3;

            uniform vec3  uAmbientColor;
            uniform float uAmbientIntensity;
            uniform float uSkyStrength;
            uniform vec3  uDirDir;
            uniform vec3  uDirColor;
            uniform float uDirIntensity;

            out vec4 outColor;

            vec3 LayerColor(sampler2D layerTex, int hasLayer, float tiling)
            {
                return hasLayer != 0 ? texture(layerTex, vUv * tiling).rgb : vec3(0.5);
            }

            void main()
            {
                vec3 N = normalize(vNormal);
                vec4 splat = texture(uSplatMap, vUv);

                vec3 albedo = LayerColor(uLayer0, uHasLayer0, uTiling0) * splat.r
                            + LayerColor(uLayer1, uHasLayer1, uTiling1) * splat.g
                            + LayerColor(uLayer2, uHasLayer2, uTiling2) * splat.b
                            + LayerColor(uLayer3, uHasLayer3, uTiling3) * splat.a;

                const float SKY_DIFFUSE_FILL = 0.48;
                vec3 sky = uAmbientColor * (uAmbientIntensity + max(N.y, 0.0) * uSkyStrength * SKY_DIFFUSE_FILL);
                vec3 ground = uAmbientColor * uAmbientIntensity * 0.35 * max(-N.y, 0.0);
                vec3 ambientLight = sky + ground;

                vec3 sunL = normalize(-uDirDir);
                vec3 diffuse = uDirColor * uDirIntensity * max(dot(N, sunL), 0.0);

                vec3 hdr = albedo * (ambientLight + diffuse);
                outColor = vec4(max(hdr, vec3(0.0)), 1.0);
            }
            """;

        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        return program;
    }

private readonly record struct TreeBillboardInstance(Vector3 Center, float HalfWidth, float Height, float Yaw, Vector4 Color);

// Shader instanciado para billboards de árbol: quad unitario compartido (offset -0.5..0.5 en X,
// 0..1 en Y anclado al piso), rotado SOLO en Y para mirar hacia la cámara (no billboard esférico
// como las partículas) usando el yaw ya calculado en CPU por instancia.
private static int CreateTreeBillboardShader()
    {
        const string vert = """
            #version 330 core
            layout(location=0) in vec2  aUnitOffset;   // por-vértice (divisor 0): x en -0.5..0.5, y en 0..1
            layout(location=1) in vec2  aUnitUv;        // por-vértice (divisor 0): 0..1
            layout(location=2) in vec3  iCenter;        // por-instancia (divisor 1): base del árbol, a nivel de piso
            layout(location=3) in float iHalfWidth;
            layout(location=4) in float iHeight;
            layout(location=5) in float iYaw;
            layout(location=6) in vec4  iColor;
            uniform mat4 uMvp;
            out vec4 vColor;
            out vec2 vUv;
            void main()
            {
                float c = cos(iYaw);
                float s = sin(iYaw);
                vec3 right = vec3(c, 0.0, -s);
                vec3 world = iCenter + right * (aUnitOffset.x * iHalfWidth * 2.0) + vec3(0.0, aUnitOffset.y * iHeight, 0.0);
                vColor = iColor;
                vUv = aUnitUv;
                gl_Position = vec4(world, 1.0) * uMvp;
            }
            """;
        const string frag = """
            #version 330 core
            in  vec4 vColor;
            in  vec2 vUv;
            out vec4 outColor;
            uniform sampler2D uTexture;
            uniform int       uHasTexture;
            void main()
            {
                vec4 tex = uHasTexture != 0 ? texture(uTexture, vUv) : vec4(1.0);
                outColor = tex * vColor;
                if (outColor.a < 0.3) discard;
            }
            """;
        int v = CompileShader(ShaderType.VertexShader, vert);
        int f = CompileShader(ShaderType.FragmentShader, frag);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(p));
        GL.DetachShader(p, v); GL.DetachShader(p, f);
        GL.DeleteShader(v); GL.DeleteShader(f);
        return p;
    }
}
