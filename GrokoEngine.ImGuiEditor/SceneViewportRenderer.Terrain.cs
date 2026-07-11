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
using GrokoEngine.Rendering;
using GrokoShaderGraphPro.Models;
using GrokoShaderGraphPro.Services;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
private readonly record struct TerrainTreePrototypeRenderData(string Path, ParsedMesh? Mesh)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Path) && Mesh != null && Mesh.Positions.Length > 0;
}

// Arboles plantados (Terrain.Trees): SIEMPRE por el pase dinÃ¡mico (ver BuildObjectRecursive),
// nunca horneados en el batch estÃ¡tico â€” se encolan vÃ­a TryQueueDynamicMeshDraw, la misma cola
// que usa cualquier malla dinÃ¡mica. Eso los hace elegibles para el instancing por GPU que ya
// existe ahÃ­ (CanInstanceTogether agrupa por mesh+textura+color+material; todos los Ã¡rboles del
// mismo prototipo comparten esos cuatro, asÃ­ que terminan en una sola llamada
// glDrawArraysInstanced por prototipo en vez de un draw/triÃ¡ngulos sueltos por Ã¡rbol).
// La altura de cada Ã¡rbol se resamplea del heightmap actual en cada frame, asÃ­ que si esculpÃ­s
// el terreno despuÃ©s de plantar, los Ã¡rboles ya plantados "flotan" solos hasta la nueva altura.
// CachÃ© de alturas muestreadas por instancia (Ã¡rboles y pasto): GetHeightLocal hace una
// interpolaciÃ³n bilineal por llamada, y hacerla por Ã¡rbol/mata POR FRAME era el costo CPU
// dominante con miles de instancias. La cachÃ© se invalida sola cuando cambia el heightmap
// (Version) o la lista de instancias (Tree/DetailVersion) â€” durante una pincelada se recalcula
// una vez por frame, que es exactamente lo que hacÃ­a antes en el peor caso.
private readonly Dictionary<string, (float[] Heights, int Version)> _terrainTreeHeightCache = new(StringComparer.OrdinalIgnoreCase);
private readonly Dictionary<string, (float[] Heights, int Version)> _terrainDetailHeightCache = new(StringComparer.OrdinalIgnoreCase);

private float[] GetTreeHeights(Terrain terrain)
{
    string id = terrain.gameObject?.EditorId ?? string.Empty;
    int version = HashCode.Combine(terrain.Version, terrain.TreeVersion, terrain.Trees.Count);
    if (_terrainTreeHeightCache.TryGetValue(id, out var cached) && cached.Version == version)
        return cached.Heights;

    var heights = cached.Heights != null && cached.Heights.Length == terrain.Trees.Count
        ? cached.Heights
        : new float[terrain.Trees.Count];
    for (int i = 0; i < terrain.Trees.Count; i++)
        heights[i] = terrain.GetHeightLocal(terrain.Trees[i].LocalX, terrain.Trees[i].LocalZ);
    _terrainTreeHeightCache[id] = (heights, version);
    return heights;
}

private float[] GetDetailHeights(Terrain terrain)
{
    string id = terrain.gameObject?.EditorId ?? string.Empty;
    int version = HashCode.Combine(terrain.Version, terrain.DetailVersion, terrain.Details.Count);
    if (_terrainDetailHeightCache.TryGetValue(id, out var cached) && cached.Version == version)
        return cached.Heights;

    var heights = cached.Heights != null && cached.Heights.Length == terrain.Details.Count
        ? cached.Heights
        : new float[terrain.Details.Count];
    for (int i = 0; i < terrain.Details.Count; i++)
        heights[i] = terrain.GetHeightLocal(terrain.Details[i].LocalX, terrain.Details[i].LocalZ);
    _terrainDetailHeightCache[id] = (heights, version);
    return heights;
}

private static bool TerrainTreeCellIntersectsFrustum(Terrain terrain, Matrix4 transform, Frustum frustum, int cellX, int cellZ)
{
    float cellSize = MathF.Max(1f, terrain.TreeCellSize);
    var localCenter = TerrainTreeLod.ComputeCellCenter(cellX, cellZ, cellSize, terrain.HeightScale);
    float localRadius = TerrainTreeLod.ComputeConservativeCellRadius(cellSize, terrain.HeightScale);
    float sx = new Vector3(transform.M11, transform.M12, transform.M13).Length;
    float sy = new Vector3(transform.M21, transform.M22, transform.M23).Length;
    float sz = new Vector3(transform.M31, transform.M32, transform.M33).Length;
    float worldRadius = localRadius * MathF.Max(0.0001f, MathF.Max(sx, MathF.Max(sy, sz)));

    var worldCenter = Vector3.TransformPosition(new Vector3(localCenter.X, localCenter.Y, localCenter.Z), transform);
    return frustum.ContainsSphere(worldCenter, worldRadius);
}

private void PrepareTerrainTreePrototypeScratch(Terrain terrain)
{
    int count = terrain.TreePrototypes.Count;
    if (_terrainTreePrototypeScratch.Length < count)
        Array.Resize(ref _terrainTreePrototypeScratch, count);

    for (int i = 0; i < count; i++)
    {
        string protoPath = terrain.TreePrototypes[i];
        protoPath = NormalizeExistingAssetPath(protoPath) ?? protoPath;

        if (string.IsNullOrWhiteSpace(protoPath) || !ObjLoader.IsSupportedMesh(protoPath))
        {
            _terrainTreePrototypeScratch[i] = default;
            continue;
        }

        var mesh = GetParsedMesh(protoPath);
        _terrainTreePrototypeScratch[i] = mesh != null && mesh.Positions.Length > 0
            ? new TerrainTreePrototypeRenderData(protoPath, mesh)
            : default;
    }
}

private void QueueTerrainTreeInstances(Terrain terrain, Matrix4 transform, Frustum frustum, Vector3 cameraPosition)
    {
        if (terrain.Trees.Count == 0 || terrain.TreePrototypes.Count == 0)
            return;

        Matrix4.Invert(transform, out var inverseTransform);
        var cameraLocalTk = Vector3.TransformPosition(cameraPosition, inverseTransform);
        var lod = TerrainTreeLod.ClassifyInto(
            terrain,
            new MiMotor.Mathematics.Vector3(cameraLocalTk.X, cameraLocalTk.Y, cameraLocalTk.Z),
            _terrainTreeLodScratch);

        _statsTerrainTreeCulled += lod.CulledCount;
        _statsTerrainTreeCells = Math.Max(_statsTerrainTreeCells, lod.VisibleCellCount);
        _terrainTreeCellVisibilityScratch.Clear();

        if (lod.Decisions.Count == 0)
            return;

        PrepareTerrainTreePrototypeScratch(terrain);
        _terrainTreeCellVisibilityScratch.EnsureCapacity(Math.Max(1, lod.VisibleCellCount));
        var treeHeights = GetTreeHeights(terrain);

        var tint = new Vector4(1f, 1f, 1f, 1f);
        var material = new Vector4(0f, 0.5f, 1f, 1f); // metallic 0, roughness 0.5 por defecto

        foreach (var decision in lod.Decisions)
        {
            if (decision.Mode == TerrainTreeRenderMode.Culled)
                continue;

            var cellKey = (decision.CellX, decision.CellZ);
            if (!_terrainTreeCellVisibilityScratch.TryGetValue(cellKey, out bool cellVisible))
            {
                cellVisible = TerrainTreeCellIntersectsFrustum(terrain, transform, frustum, decision.CellX, decision.CellZ);
                _terrainTreeCellVisibilityScratch[cellKey] = cellVisible;
            }

            if (!cellVisible)
            {
                _statsTerrainTreeCulled++;
                continue;
            }

            int treeIndex = decision.TreeIndex;
            var instance = terrain.Trees[treeIndex];
            var proto = _terrainTreePrototypeScratch[instance.PrototypeIndex];
            if (!proto.IsValid || proto.Mesh == null)
            {
                _statsTerrainTreeCulled++;
                continue;
            }

            string protoPath = proto.Path;
            var mesh = proto.Mesh;

            float localHeight = treeHeights[treeIndex];
            var localPos = new Vector3(instance.LocalX, localHeight, instance.LocalZ);

            var worldPos = Vector3.TransformPosition(localPos, transform);
            var extent = (mesh.BoundsMax - mesh.BoundsMin) * Math.Max(0.0001f, instance.Scale);
            float boundRadius = Math.Max(0.1f, extent.Length * 0.5f);
            if (!frustum.ContainsSphere(worldPos, boundRadius))
            {
                _statsTerrainTreeCulled++;
                continue;
            }

            bool isBillboard = decision.Mode == TerrainTreeRenderMode.Billboard;

            if (!isBillboard)
            {
                _statsTerrainTreeMesh++;
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
                _statsTerrainTreeBillboard++;
                QueueTreeBillboard(worldPos, extent, cameraPosition, mesh);
            }
        }
    }

// Detalles/pasto (Terrain.Details): siempre billboard, nunca malla â€” Unity's clÃ¡sico "Detail
// Texture" tampoco tiene LOD de malla para pasto. Density Scale reduce cuÃ¡ntos de los plantados
// se dibujan de verdad SIN borrar los datos pintados, usando un hash estable por Ã­ndice (no un
// mÃ³dulo, para que el recorte no se vea como un patrÃ³n en grilla).
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

        var detailHeights = GetDetailHeights(terrain);

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

            float localHeight = detailHeights[i];
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

// Encola un Ã¡rbol como billboard: quad plano, ancho X/Z tomado del bounding box de la malla real
// (escalado por Scale de la instancia), anclado al piso, que rota SOLO en Y para mirar siempre
// a cÃ¡mara (no billboard esfÃ©rico como las partÃ­culas). SimplificaciÃ³n deliberada respecto a
// Unity: usa la MISMA textura del prototipo 3D directo sobre el quad, en vez de un snapshot
// recortado horneado â€” mÃ¡s barato de implementar, algo menos fiel visualmente de cerca (que es
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

// Dibuja los billboards acumulados este frame (Ã¡rboles fuera de rango de malla, o detalles/
// pasto â€” siempre billboard), agrupados por textura â€” una llamada glDrawArraysInstanced por
// textura distinta. MÃ©todo compartido: mismo shader/VAO para Ã¡rboles-billboard y detalles.
private void DrawTreeBillboardInstances(ref Matrix4 mvp, Dictionary<string, List<TreeBillboardInstance>> billboardsByTexture, float windStrength = 0f)
    {
        if (billboardsByTexture.Count == 0 || _treeBillboardShader == 0)
            return;

        GL.Enable(EnableCap.CullFace);
        GL.UseProgram(_treeBillboardShader);
        GL.UniformMatrix4(_treeBillboardMvpLoc, true, ref mvp);
        GL.Uniform1(_treeBillboardTexLoc, 0);
        // Balanceo por viento (solo pasto): dobla la parte superior del quad con un seno
        // desfasado por posiciÃ³n, para que no ondee todo el campo en fase.
        GL.Uniform1(_treeBillboardWindLoc, windStrength);
        GL.Uniform1(_treeBillboardTimeLoc, (float)(Environment.TickCount64 % 1000000L) / 1000f);
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

// Resuelve un vecino por EditorId contra el Ã­ndice armado una vez por frame en Render(). Null si
// no hay ID configurado o si el GameObject referenciado ya no existe/no tiene Terrain.
private Terrain? ResolveTerrainNeighbor(string? neighborId) =>
    !string.IsNullOrEmpty(neighborId) && _terrainsByEditorId.TryGetValue(neighborId, out var neighbor) ? neighbor : null;

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

// Mismo contrato de dirty-rect que el heightmap: si SplatVersion cambiÃ³ y hay regiÃ³n pendiente,
// se sube solo ese rect con glTexSubImage2D (pintar a resoluciÃ³n 2049 costarÃ­a ~16 MB por frame
// si se re-subiera entera); sin regiÃ³n (undo, carga, cambio de Resolution) se re-sube completa.
private int GetOrUpdateSplatTexture(GameObject obj, Terrain terrain)
    {
        int ownerHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(terrain);
        if (terrainSplatTextureCache.TryGetValue(obj.EditorId, out var cached) &&
            cached.Version == terrain.SplatVersion && cached.OwnerHash == ownerHash)
            return cached.TextureId;

        terrain.EnsureSplatMapSize();
        int res = terrain.Resolution;

        int textureId = cached.TextureId;
        bool hasRect = terrain.ConsumeSplatDirtyRegion(out int minX, out int minZ, out int maxX, out int maxZ);

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        if (textureId != 0 && cached.Resolution == res && hasRect && cached.OwnerHash == ownerHash)
        {
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, res);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, minX);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, minZ);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, minX, minZ, maxX - minX + 1, maxZ - minZ + 1,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, terrain.SplatMap);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
        }
        else
        {
            if (textureId == 0)
                textureId = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                res,
                res,
                0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                PixelType.UnsignedByte,
                terrain.SplatMap);
        }
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        terrainSplatTextureCache[obj.EditorId] = (textureId, terrain.SplatVersion, res, ownerHash);
        return textureId;
    }

private readonly record struct TreeBillboardInstance(Vector3 Center, float HalfWidth, float Height, float Yaw, Vector4 Color);

// Shader instanciado para billboards de Ã¡rbol: quad unitario compartido (offset -0.5..0.5 en X,
// 0..1 en Y anclado al piso), rotado SOLO en Y para mirar hacia la cÃ¡mara (no billboard esfÃ©rico
// como las partÃ­culas) usando el yaw ya calculado en CPU por instancia.
private static int CreateTreeBillboardShader()
    {
        const string vert = """
            #version 330 core
            layout(location=0) in vec2  aUnitOffset;   // por-vÃ©rtice (divisor 0): x en -0.5..0.5, y en 0..1
            layout(location=1) in vec2  aUnitUv;        // por-vÃ©rtice (divisor 0): 0..1
            layout(location=2) in vec3  iCenter;        // por-instancia (divisor 1): base del Ã¡rbol, a nivel de piso
            layout(location=3) in float iHalfWidth;
            layout(location=4) in float iHeight;
            layout(location=5) in float iYaw;
            layout(location=6) in vec4  iColor;
            uniform mat4 uMvp;
            uniform float uTime;
            uniform float uWind;   // 0 = sin viento (Ã¡rboles); >0 dobla la copa del quad (pasto)
            out vec4 vColor;
            out vec2 vUv;
            void main()
            {
                float c = cos(iYaw);
                float s = sin(iYaw);
                vec3 right = vec3(c, 0.0, -s);
                vec3 world = iCenter + right * (aUnitOffset.x * iHalfWidth * 2.0) + vec3(0.0, aUnitOffset.y * iHeight, 0.0);
                if (uWind > 0.0)
                {
                    float sway = sin(uTime * 1.9 + (iCenter.x + iCenter.z) * 0.8) * uWind * aUnitOffset.y * aUnitOffset.y * iHeight;
                    world.x += sway * 0.7;
                    world.z += sway * 0.5;
                }
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


