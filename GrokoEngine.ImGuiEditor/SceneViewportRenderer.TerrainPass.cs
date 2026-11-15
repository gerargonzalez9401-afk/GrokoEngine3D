using GrokoEngine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;

namespace GrokoEngine.ImGuiEditor;

// ── Terrain CDLOD ──────────────────────────────────────────────────────────────
// Render de terreno estilo AAA (CDLOD, Strugar 2009): el heightmap vive como textura R32F en la
// GPU y el vertex shader desplaza UNA rejilla indexada compartida de 32x32 quads, una draw call
// por nodo del quadtree seleccionado por distancia. Ventajas sobre la ruta vieja (malla completa
// no indexada regenerada en CPU):
//   - Esculpir sube solo el rect tocado con glTexSubImage2D (KBs), no 1.5M de vértices (~54 MB).
//   - Densidad de triángulos ~constante en pantalla a cualquier distancia (morph continuo entre
//     niveles, sin popping ni grietas).
//   - Resoluciones hasta 2049 sin reconstrucción de malla.
// Las normales se hornean en CPU a una textura RGB8 (con stitching de vecinos, igual que hacía
// TerrainMeshGenerator) y el fragment shader las perturba con normal maps por capa, height-based
// blending, triplanar opcional y anti-tiling por distancia. Recibe sombras direccionales en
// cascada (mismo esquema que el solid shader) y proyecta sombra vía un programa depth-only.
internal sealed partial class SceneViewportRenderer
{
    private const int TerrainGridQuads = 32;                                   // = TerrainQuadTree.LeafQuads
    private const int TerrainQuadrantIndexCount = (TerrainGridQuads / 2) * (TerrainGridQuads / 2) * 6;
    private const int TerrainSkirtIndexStart = TerrainQuadrantIndexCount * 4;
    private const int TerrainSkirtIndexCount = TerrainGridQuads * 4 * 6;

    private sealed class TerrainGpuData
    {
        public int HeightTex;
        public int NormalTex;
        public int AllocatedResolution;
        public int HeightVersion = int.MinValue;
        public int NeighborNormalVersion = int.MinValue;
        public byte[] NormalPixels = Array.Empty<byte>();
        public readonly TerrainQuadTree Tree = new();
        // Undo/recarga de escena reemplazan el Terrain por una INSTANCIA nueva con el mismo
        // EditorId y contadores reiniciados — que por casualidad pueden coincidir con los
        // cacheados. Si cambió la instancia, se fuerza re-subida completa.
        public WeakReference<Terrain>? Owner;
    }

    private readonly Dictionary<string, TerrainGpuData> _terrainGpu = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(GameObject Obj, Terrain Terrain, Matrix4 World)> _frameTerrainDraws = new();
    private readonly List<SelectedTerrainNode> _terrainNodeScratch = new(256);
    private readonly TerrainClipPlane[] _terrainPlaneScratch = new TerrainClipPlane[6];

    private int _terrainGridVao;
    private int _terrainGridVbo;
    private int _terrainGridIbo;

    private int _terrainCdlodShader;
    private int _tpMvpLoc, _tpModelLoc, _tpHeightmapLoc, _tpNormalTexLoc, _tpSplatLoc;
    private int _tpNodeOffsetScaleLoc, _tpMorphConstsLoc, _tpTerrainSizeLoc, _tpResolutionLoc;
    private int _tpLocalCameraPosLoc, _tpSkirtDepthLoc, _tpUvToTexelLoc;
    private int _tpBlendSharpnessLoc, _tpTriplanarLoc, _tpBreakupDistanceLoc;
    private readonly int[] _tpLayerLoc = new int[4];
    private readonly int[] _tpHasLayerLoc = new int[4];
    private readonly int[] _tpTilingLoc = new int[4];
    private readonly int[] _tpLayerNormLoc = new int[4];
    private readonly int[] _tpHasLayerNormLoc = new int[4];
    private int _tpShadowMapLoc, _tpShadowEnabledLoc, _tpShadowStrengthLoc, _tpCascadeCountLoc;
    private int _tpShadowCameraPosLoc, _tpShadowPcfRadiusLoc, _tpShadowBiasScaleLoc, _tpShadowNormalBiasLoc;
    private readonly int[] _tpCascadeLightMvpLoc = new int[MaxDirectionalShadowCascades];
    private readonly int[] _tpCascadeSplitLoc = new int[MaxDirectionalShadowCascades];

    private int _terrainDepthShader;
    private int _tdMvpLoc, _tdModelLoc, _tdHeightmapLoc, _tdNodeOffsetScaleLoc, _tdTerrainSizeLoc, _tdResolutionLoc;
    private Matrix4 _currentDepthMvp = Matrix4.Identity;

    private void InitializeTerrainPass()
    {
        _terrainCdlodShader = CreateTerrainCdlodShader();
        _tpMvpLoc = GL.GetUniformLocation(_terrainCdlodShader, "uMvp");
        _tpModelLoc = GL.GetUniformLocation(_terrainCdlodShader, "uModel");
        _tpHeightmapLoc = GL.GetUniformLocation(_terrainCdlodShader, "uHeightmap");
        _tpNormalTexLoc = GL.GetUniformLocation(_terrainCdlodShader, "uNormalTex");
        _tpSplatLoc = GL.GetUniformLocation(_terrainCdlodShader, "uSplatMap");
        _tpNodeOffsetScaleLoc = GL.GetUniformLocation(_terrainCdlodShader, "uNodeOffsetScale");
        _tpMorphConstsLoc = GL.GetUniformLocation(_terrainCdlodShader, "uMorphConsts");
        _tpTerrainSizeLoc = GL.GetUniformLocation(_terrainCdlodShader, "uTerrainSize");
        _tpResolutionLoc = GL.GetUniformLocation(_terrainCdlodShader, "uResolution");
        _tpLocalCameraPosLoc = GL.GetUniformLocation(_terrainCdlodShader, "uLocalCameraPos");
        _tpSkirtDepthLoc = GL.GetUniformLocation(_terrainCdlodShader, "uSkirtDepth");
        _tpUvToTexelLoc = GL.GetUniformLocation(_terrainCdlodShader, "uUvToTexel");
        _tpBlendSharpnessLoc = GL.GetUniformLocation(_terrainCdlodShader, "uBlendSharpness");
        _tpTriplanarLoc = GL.GetUniformLocation(_terrainCdlodShader, "uTriplanar");
        _tpBreakupDistanceLoc = GL.GetUniformLocation(_terrainCdlodShader, "uBreakupDistance");
        for (int i = 0; i < 4; i++)
        {
            _tpLayerLoc[i] = GL.GetUniformLocation(_terrainCdlodShader, $"uLayer{i}");
            _tpHasLayerLoc[i] = GL.GetUniformLocation(_terrainCdlodShader, $"uHasLayer{i}");
            _tpTilingLoc[i] = GL.GetUniformLocation(_terrainCdlodShader, $"uTiling{i}");
            _tpLayerNormLoc[i] = GL.GetUniformLocation(_terrainCdlodShader, $"uLayerNorm{i}");
            _tpHasLayerNormLoc[i] = GL.GetUniformLocation(_terrainCdlodShader, $"uHasLayerNorm{i}");
        }
        _tpShadowMapLoc = GL.GetUniformLocation(_terrainCdlodShader, "uShadowMap");
        _tpShadowEnabledLoc = GL.GetUniformLocation(_terrainCdlodShader, "uShadowEnabled");
        _tpShadowStrengthLoc = GL.GetUniformLocation(_terrainCdlodShader, "uShadowStrength");
        _tpCascadeCountLoc = GL.GetUniformLocation(_terrainCdlodShader, "uCascadeCount");
        _tpShadowCameraPosLoc = GL.GetUniformLocation(_terrainCdlodShader, "uShadowCameraPos");
        _tpShadowPcfRadiusLoc = GL.GetUniformLocation(_terrainCdlodShader, "uShadowPcfRadius");
        _tpShadowBiasScaleLoc = GL.GetUniformLocation(_terrainCdlodShader, "uShadowBiasScale");
        _tpShadowNormalBiasLoc = GL.GetUniformLocation(_terrainCdlodShader, "uShadowNormalBias");
        for (int i = 0; i < MaxDirectionalShadowCascades; i++)
        {
            _tpCascadeLightMvpLoc[i] = GL.GetUniformLocation(_terrainCdlodShader, $"uCascadeLightMvp[{i}]");
            _tpCascadeSplitLoc[i] = GL.GetUniformLocation(_terrainCdlodShader, $"uCascadeSplit[{i}]");
        }

        // La iluminación del terreno la aplica ApplyTerrainLighting sobre estas locations
        // compartidas (antes apuntaban al shader viejo de splat plano).
        terrainAmbientColorLocation = GL.GetUniformLocation(_terrainCdlodShader, "uAmbientColor");
        terrainAmbientIntensityLocation = GL.GetUniformLocation(_terrainCdlodShader, "uAmbientIntensity");
        terrainSkyStrengthLocation = GL.GetUniformLocation(_terrainCdlodShader, "uSkyStrength");
        terrainDirDirLocation = GL.GetUniformLocation(_terrainCdlodShader, "uDirDir");
        terrainDirColorLocation = GL.GetUniformLocation(_terrainCdlodShader, "uDirColor");
        terrainDirIntensityLocation = GL.GetUniformLocation(_terrainCdlodShader, "uDirIntensity");

        _terrainDepthShader = CreateTerrainDepthShader();
        _tdMvpLoc = GL.GetUniformLocation(_terrainDepthShader, "uMvp");
        _tdModelLoc = GL.GetUniformLocation(_terrainDepthShader, "uModel");
        _tdHeightmapLoc = GL.GetUniformLocation(_terrainDepthShader, "uHeightmap");
        _tdNodeOffsetScaleLoc = GL.GetUniformLocation(_terrainDepthShader, "uNodeOffsetScale");
        _tdTerrainSizeLoc = GL.GetUniformLocation(_terrainDepthShader, "uTerrainSize");
        _tdResolutionLoc = GL.GetUniformLocation(_terrainDepthShader, "uResolution");

        CreateTerrainGridMesh();
    }

    // Rejilla compartida de 33x33 vértices (vec3: gridX, gridZ, flagFalda). Los índices van
    // agrupados en 4 bloques contiguos por cuadrante (SW, SE, NW, NE) para poder dibujar
    // subconjuntos cuando el quadtree selecciona un nodo parcialmente (QuadrantMask), más un 5º
    // bloque con la falda perimetral (vértices duplicados del borde que el shader hunde).
    private void CreateTerrainGridMesh()
    {
        const int n = TerrainGridQuads;          // 32 quads por lado
        const int vertsPerSide = n + 1;          // 33
        int mainVerts = vertsPerSide * vertsPerSide;
        int skirtBase = mainVerts;

        var verts = new float[(mainVerts + vertsPerSide * 4) * 3];
        int v = 0;
        for (int z = 0; z <= n; z++)
        {
            for (int x = 0; x <= n; x++)
            {
                verts[v++] = x; verts[v++] = z; verts[v++] = 0f;
            }
        }
        // Faldas: sur (z=0), norte (z=n), oeste (x=0), este (x=n) — mismo XZ, flag=1.
        for (int i = 0; i <= n; i++) { verts[v++] = i; verts[v++] = 0; verts[v++] = 1f; }
        for (int i = 0; i <= n; i++) { verts[v++] = i; verts[v++] = n; verts[v++] = 1f; }
        for (int i = 0; i <= n; i++) { verts[v++] = 0; verts[v++] = i; verts[v++] = 1f; }
        for (int i = 0; i <= n; i++) { verts[v++] = n; verts[v++] = i; verts[v++] = 1f; }

        var indices = new ushort[TerrainQuadrantIndexCount * 4 + TerrainSkirtIndexCount];
        int idx = 0;
        for (int q = 0; q < 4; q++)
        {
            int qx0 = (q & 1) * (n / 2);
            int qz0 = (q >> 1) * (n / 2);
            for (int z = qz0; z < qz0 + n / 2; z++)
            {
                for (int x = qx0; x < qx0 + n / 2; x++)
                {
                    ushort i00 = (ushort)(z * vertsPerSide + x);
                    ushort i10 = (ushort)(z * vertsPerSide + x + 1);
                    ushort i01 = (ushort)((z + 1) * vertsPerSide + x);
                    ushort i11 = (ushort)((z + 1) * vertsPerSide + x + 1);
                    // Misma diagonal/winding que usaba TerrainMeshGenerator.
                    indices[idx++] = i00; indices[idx++] = i11; indices[idx++] = i10;
                    indices[idx++] = i00; indices[idx++] = i01; indices[idx++] = i11;
                }
            }
        }

        void SkirtEdge(Func<int, ushort> border, int skirtRowBase)
        {
            for (int i = 0; i < n; i++)
            {
                ushort b0 = border(i);
                ushort b1 = border(i + 1);
                ushort s0 = (ushort)(skirtRowBase + i);
                ushort s1 = (ushort)(skirtRowBase + i + 1);
                // La falda se dibuja con culling desactivado, así que el winding no importa.
                indices[idx++] = b0; indices[idx++] = b1; indices[idx++] = s1;
                indices[idx++] = b0; indices[idx++] = s1; indices[idx++] = s0;
            }
        }
        SkirtEdge(i => (ushort)i, skirtBase);                                       // sur
        SkirtEdge(i => (ushort)(n * vertsPerSide + i), skirtBase + vertsPerSide);   // norte
        SkirtEdge(i => (ushort)(i * vertsPerSide), skirtBase + vertsPerSide * 2);   // oeste
        SkirtEdge(i => (ushort)(i * vertsPerSide + n), skirtBase + vertsPerSide * 3); // este

        _terrainGridVao = GL.GenVertexArray();
        _terrainGridVbo = GL.GenBuffer();
        _terrainGridIbo = GL.GenBuffer();
        GL.BindVertexArray(_terrainGridVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _terrainGridVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _terrainGridIbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(ushort), indices, BufferUsageHint.StaticDraw);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
    }

    private void DisposeTerrainPass()
    {
        foreach (var data in _terrainGpu.Values)
        {
            if (data.HeightTex != 0) GL.DeleteTexture(data.HeightTex);
            if (data.NormalTex != 0) GL.DeleteTexture(data.NormalTex);
        }
        _terrainGpu.Clear();
        foreach (var (textureId, _, _, _) in terrainSplatTextureCache.Values)
            if (textureId != 0)
                GL.DeleteTexture(textureId);
        terrainSplatTextureCache.Clear();
        if (_terrainGridVbo != 0) GL.DeleteBuffer(_terrainGridVbo);
        if (_terrainGridIbo != 0) GL.DeleteBuffer(_terrainGridIbo);
        if (_terrainGridVao != 0) GL.DeleteVertexArray(_terrainGridVao);
        if (_terrainCdlodShader != 0) GL.DeleteProgram(_terrainCdlodShader);
        if (_terrainDepthShader != 0) GL.DeleteProgram(_terrainDepthShader);
    }

    // Registrado por BuildObjectRecursive en el pase dinámico (el terreno ya no vive en el batch
    // estático: con CDLOD la geometría es virtual y cuesta ~nada por frame, y así esculpir un
    // terreno Static se ve en vivo sin reconstruir el batch).
    private void CollectTerrainDraw(GameObject obj, Terrain terrain, Matrix4 world, bool selected)
    {
        _frameTerrainDraws.Add((obj, terrain, world));

        if (ShowObjectWireframes || selected)
        {
            Vector4 fallbackColor = new Vector4(0.62f, 0.72f, 1f, 1f);
            Vector4 lineColor = selected ? new Vector4(1f, 0.78f, 0.12f, 1f) : GetObjectColor(obj, fallbackColor);
            float halfX = terrain.SizeX * 0.5f;
            float halfZ = terrain.SizeZ * 0.5f;
            float minY = -2f * MathF.Abs(terrain.HeightScale);
            float maxY = 2f * MathF.Abs(terrain.HeightScale);
            if (_terrainGpu.TryGetValue(obj.EditorId, out var data) && data.Tree.LodLevels > 0)
            {
                data.Tree.GetRootHeightBounds(out float rawMin, out float rawMax);
                minY = rawMin * terrain.HeightScale;
                maxY = rawMax * terrain.HeightScale;
                if (minY > maxY) (minY, maxY) = (maxY, minY);
            }

            Span<Vector3> corners = stackalloc Vector3[]
            {
                new(-halfX, minY, -halfZ), new(halfX, minY, -halfZ),
                new(halfX, maxY, -halfZ), new(-halfX, maxY, -halfZ),
                new(-halfX, minY, halfZ), new(halfX, minY, halfZ),
                new(halfX, maxY, halfZ), new(-halfX, maxY, halfZ)
            };
            for (int i = 0; i < corners.Length; i++)
                corners[i] = Vector3.TransformPosition(corners[i], world);

            AddLine(corners[0], corners[1], lineColor); AddLine(corners[1], corners[2], lineColor);
            AddLine(corners[2], corners[3], lineColor); AddLine(corners[3], corners[0], lineColor);
            AddLine(corners[4], corners[5], lineColor); AddLine(corners[5], corners[6], lineColor);
            AddLine(corners[6], corners[7], lineColor); AddLine(corners[7], corners[4], lineColor);
            AddLine(corners[0], corners[4], lineColor); AddLine(corners[1], corners[5], lineColor);
            AddLine(corners[2], corners[6], lineColor); AddLine(corners[3], corners[7], lineColor);
        }
    }

    // Mantiene las texturas GPU del terreno al día con el estado CPU. Regla clave: si Version
    // cambió Y hay dirty-rect pendiente → subida parcial (glTexSubImage2D del rect) + update
    // incremental del quadtree + re-bake de normales del rect inflado 1 texel. Si Version cambió
    // SIN rect (undo, carga de escena, import, cambio de Resolution) → subida completa.
    private TerrainGpuData GetOrUpdateTerrainGpu(GameObject obj, Terrain terrain,
        Terrain? left, Terrain? right, Terrain? top, Terrain? bottom)
    {
        int res = Math.Max(2, terrain.Resolution);
        if (!_terrainGpu.TryGetValue(obj.EditorId, out var data))
        {
            data = new TerrainGpuData();
            _terrainGpu[obj.EditorId] = data;
        }

        int neighborVersion = HashCode.Combine(left?.Version ?? -1, right?.Version ?? -1, top?.Version ?? -1, bottom?.Version ?? -1);
        bool sameOwner = data.Owner != null && data.Owner.TryGetTarget(out var owner) && ReferenceEquals(owner, terrain);
        bool needsFull = data.AllocatedResolution != res || data.HeightTex == 0 || !sameOwner;
        bool versionChanged = data.HeightVersion != terrain.Version;

        if (needsFull || versionChanged)
        {
            terrain.EnsureHeightmapSize();
            bool hasRect = terrain.ConsumeDirtyRegion(out int minX, out int minZ, out int maxX, out int maxZ);

            if (!needsFull && hasRect)
            {
                GL.BindTexture(TextureTarget.Texture2D, data.HeightTex);
                UploadSubRect(res, minX, minZ, maxX, maxZ, () =>
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, minX, minZ, maxX - minX + 1, maxZ - minZ + 1,
                        OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.Float, terrain.Heightmap));

                data.Tree.UpdateRegion(terrain, minX, minZ, maxX, maxZ);

                // La normal de un texel depende de sus 4 vecinos → inflar el rect 1 texel.
                int nMinX = Math.Max(0, minX - 1);
                int nMinZ = Math.Max(0, minZ - 1);
                int nMaxX = Math.Min(res - 1, maxX + 1);
                int nMaxZ = Math.Min(res - 1, maxZ + 1);
                TerrainNormalBaker.BakeRegion(terrain, left, right, top, bottom, nMinX, nMinZ, nMaxX, nMaxZ, data.NormalPixels);
                GL.BindTexture(TextureTarget.Texture2D, data.NormalTex);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                UploadSubRect(res, nMinX, nMinZ, nMaxX, nMaxZ, () =>
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, nMinX, nMinZ, nMaxX - nMinX + 1, nMaxZ - nMinZ + 1,
                        OpenTK.Graphics.OpenGL4.PixelFormat.Rgb, PixelType.UnsignedByte, data.NormalPixels));
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
            else
            {
                if (data.HeightTex == 0) data.HeightTex = GL.GenTexture();
                if (data.NormalTex == 0) data.NormalTex = GL.GenTexture();

                GL.BindTexture(TextureTarget.Texture2D, data.HeightTex);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, res, res, 0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.Float, terrain.Heightmap);

                data.Tree.Build(terrain);
                data.NormalPixels = TerrainNormalBaker.BakeAll(terrain, left, right, top, bottom);

                GL.BindTexture(TextureTarget.Texture2D, data.NormalTex);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, res, res, 0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Rgb, PixelType.UnsignedByte, data.NormalPixels);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);

                data.AllocatedResolution = res;
            }

            data.HeightVersion = terrain.Version;
            data.NeighborNormalVersion = neighborVersion;
            data.Owner = new WeakReference<Terrain>(terrain);
        }
        else if (data.NeighborNormalVersion != neighborVersion)
        {
            // Solo cambió un vecino: las normales de las tiras de borde (2 texels) dependen de su
            // heightmap; re-hornear y subir solo esas tiras.
            GL.BindTexture(TextureTarget.Texture2D, data.NormalTex);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            void BakeStrip(int x0, int z0, int x1, int z1)
            {
                TerrainNormalBaker.BakeRegion(terrain, left, right, top, bottom, x0, z0, x1, z1, data.NormalPixels);
                UploadSubRect(res, x0, z0, x1, z1, () =>
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, x0, z0, x1 - x0 + 1, z1 - z0 + 1,
                        OpenTK.Graphics.OpenGL4.PixelFormat.Rgb, PixelType.UnsignedByte, data.NormalPixels));
            }
            BakeStrip(0, 0, Math.Min(1, res - 1), res - 1);
            BakeStrip(Math.Max(0, res - 2), 0, res - 1, res - 1);
            BakeStrip(0, 0, res - 1, Math.Min(1, res - 1));
            BakeStrip(0, Math.Max(0, res - 2), res - 1, res - 1);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            data.NeighborNormalVersion = neighborVersion;
        }

        return data;
    }

    // Sube un sub-rect de un array CPU de tamaño completo (res x res) sin copiar a un buffer
    // intermedio: UNPACK_ROW_LENGTH/SKIP_PIXELS/SKIP_ROWS (core en GL 3.3) le dicen a GL dónde
    // empieza el rect dentro de la fila. El callback hace la llamada TexSubImage2D concreta.
    private static void UploadSubRect(int rowLength, int minX, int minZ, int maxX, int maxZ, Action upload)
    {
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, rowLength);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, minX);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, minZ);
        upload();
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
    }

    private void ExtractTerrainClipPlanes(in Matrix4 localClip)
    {
        var r0 = localClip.Row0;
        var r1 = localClip.Row1;
        var r2 = localClip.Row2;
        var r3 = localClip.Row3;
        _terrainPlaneScratch[0] = NormTerrainPlane(r3 + r0);
        _terrainPlaneScratch[1] = NormTerrainPlane(r3 - r0);
        _terrainPlaneScratch[2] = NormTerrainPlane(r3 + r1);
        _terrainPlaneScratch[3] = NormTerrainPlane(r3 - r1);
        _terrainPlaneScratch[4] = NormTerrainPlane(r3 + r2);
        _terrainPlaneScratch[5] = NormTerrainPlane(r3 - r2);
    }

    private static TerrainClipPlane NormTerrainPlane(Vector4 p)
    {
        float len = MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        if (len > 1e-6f) p /= len;
        return new TerrainClipPlane(p.X, p.Y, p.Z, p.W);
    }

    private void ApplyTerrainShadowUniforms(ShadowInfo shadow)
    {
        GL.Uniform1(_tpCascadeCountLoc, shadow.CascadeCount);
        GL.Uniform3(_tpShadowCameraPosLoc, shadow.CameraPosition);
        for (int i = 0; i < MaxDirectionalShadowCascades; i++)
        {
            var cascadeMvp = shadow.Cascades[i];
            GL.UniformMatrix4(_tpCascadeLightMvpLoc[i], true, ref cascadeMvp);
            GL.Uniform1(_tpCascadeSplitLoc[i], shadow.Splits[i]);
        }
        GL.Uniform1(_tpShadowEnabledLoc, shadow.Enabled ? 1 : 0);
        GL.Uniform1(_tpShadowStrengthLoc, shadow.Strength);
        GL.Uniform1(_tpShadowPcfRadiusLoc, ShadowPcfRadiusFor(_shadowQuality));
        GL.Uniform1(_tpShadowBiasScaleLoc, EffectiveShadowBiasScale());
        GL.Uniform1(_tpShadowNormalBiasLoc, _frameDir != null ? _frameDir.ShadowNormalBias : 0f);
        GL.Uniform1(_tpShadowMapLoc, 1);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2DArray, shadow.Enabled ? _shadowArrayTex : 0);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private void RenderTerrainPass(ref Matrix4 mvp, Vector3 cameraWorldPos, IReadOnlyList<GameObject> objects, ShadowInfo shadowInfo)
    {
        if (_frameTerrainDraws.Count == 0 || _terrainCdlodShader == 0)
            return;

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.UseProgram(_terrainCdlodShader);
        GL.UniformMatrix4(_tpMvpLoc, true, ref mvp);
        ApplyTerrainLighting(objects);
        ApplyTerrainShadowUniforms(shadowInfo);
        GL.Uniform1(_tpSplatLoc, 0);
        GL.Uniform1(_tpHeightmapLoc, 2);
        GL.Uniform1(_tpNormalTexLoc, 3);
        for (int i = 0; i < 4; i++)
        {
            GL.Uniform1(_tpLayerLoc[i], 4 + i);
            GL.Uniform1(_tpLayerNormLoc[i], 8 + i);
        }

        foreach (var (obj, terrain, world) in _frameTerrainDraws)
        {
            var left = ResolveTerrainNeighbor(terrain.LeftNeighborId);
            var right = ResolveTerrainNeighbor(terrain.RightNeighborId);
            var top = ResolveTerrainNeighbor(terrain.TopNeighborId);
            var bottom = ResolveTerrainNeighbor(terrain.BottomNeighborId);
            var data = GetOrUpdateTerrainGpu(obj, terrain, left, right, top, bottom);
            int splatTexture = GetOrUpdateSplatTexture(obj, terrain);

            int res = Math.Max(2, terrain.Resolution);
            int quads = res - 1;
            var worldMatrix = world;
            GL.UniformMatrix4(_tpModelLoc, true, ref worldMatrix);
            GL.Uniform3(_tpTerrainSizeLoc, terrain.SizeX, terrain.HeightScale, terrain.SizeZ);
            GL.Uniform1(_tpResolutionLoc, (float)res);
            GL.Uniform2(_tpUvToTexelLoc, (res - 1f) / res, 0.5f / res);
            GL.Uniform1(_tpBlendSharpnessLoc, Math.Clamp(terrain.BlendSharpness, 0f, 1f));
            GL.Uniform1(_tpTriplanarLoc, terrain.TriplanarSteepSlopes ? 1 : 0);
            GL.Uniform1(_tpBreakupDistanceLoc, terrain.DetailTilingBreakupDistance);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, splatTexture);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, data.HeightTex);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, data.NormalTex);
            for (int i = 0; i < 4; i++)
            {
                string albedoPath = terrain.LayerTextures[i];
                GL.ActiveTexture(TextureUnit.Texture4 + i);
                int albedoTex = !string.IsNullOrWhiteSpace(albedoPath) ? GetTexture(albedoPath) : 0;
                GL.BindTexture(TextureTarget.Texture2D, albedoTex);
                GL.Uniform1(_tpHasLayerLoc[i], albedoTex != 0 ? 1 : 0);
                GL.Uniform1(_tpTilingLoc[i], terrain.LayerTiling[i]);

                string normalPath = i < terrain.LayerNormalTextures.Length ? terrain.LayerNormalTextures[i] : "";
                GL.ActiveTexture(TextureUnit.Texture8 + i);
                int normalTex = !string.IsNullOrWhiteSpace(normalPath) ? GetTexture(normalPath) : 0;
                GL.BindTexture(TextureTarget.Texture2D, normalTex);
                GL.Uniform1(_tpHasLayerNormLoc[i], normalTex != 0 ? 1 : 0);
            }

            // Selección CDLOD en espacio local del terreno: cámara y frustum se transforman una
            // vez en CPU y el árbol trabaja en coordenadas de celda.
            Matrix4 localClip = worldMatrix * mvp;
            ExtractTerrainClipPlanes(localClip);
            Matrix4 invWorld = Matrix4.Invert(worldMatrix);
            var camLocalTk = Vector3.TransformPosition(cameraWorldPos, invWorld);
            var camLocal = new MiMotor.Mathematics.Vector3(camLocalTk.X, camLocalTk.Y, camLocalTk.Z);
            float lodRange0 = TerrainQuadTree.ComputeLodRange0(terrain);

            _terrainNodeScratch.Clear();
            data.Tree.SelectNodes(terrain, camLocal, lodRange0,
                _frustumCullingEnabled ? _terrainPlaneScratch : null, _terrainNodeScratch);

            GL.Uniform3(_tpLocalCameraPosLoc, camLocalTk);
            GL.BindVertexArray(_terrainGridVao);

            float cellWorld = terrain.SizeX / Math.Max(1, quads);
            foreach (var node in _terrainNodeScratch)
            {
                data.Tree.GetMorphConsts(node.LodLevel, lodRange0, out float morphStart, out float morphEnd);
                // OJO: los cuatro valores DEBEN ser float — con cuatro int, OpenTK elige la
                // sobrecarga glUniform4i, que no puede escribir un vec4 (GL_INVALID_OPERATION
                // silencioso) y el nodo queda con tamaño 0 → terreno invisible.
                GL.Uniform4(_tpNodeOffsetScaleLoc, (float)node.X, (float)node.Z, (float)node.Size, (float)node.LodLevel);
                GL.Uniform2(_tpMorphConstsLoc, morphStart, morphEnd);

                if (node.QuadrantMask == 0b1111)
                {
                    TrackMainDraw(TerrainQuadrantIndexCount * 4);
                    GL.DrawElements(PrimitiveType.Triangles, TerrainQuadrantIndexCount * 4, DrawElementsType.UnsignedShort, IntPtr.Zero);
                }
                else
                {
                    // Cuadrantes contiguos en el IBO → una draw por corrida de bits consecutivos.
                    int q = 0;
                    while (q < 4)
                    {
                        if ((node.QuadrantMask & (1 << q)) == 0) { q++; continue; }
                        int runStart = q;
                        while (q < 4 && (node.QuadrantMask & (1 << q)) != 0) q++;
                        int count = (q - runStart) * TerrainQuadrantIndexCount;
                        TrackMainDraw(count);
                        GL.DrawElements(PrimitiveType.Triangles, count, DrawElementsType.UnsignedShort,
                            (IntPtr)(runStart * TerrainQuadrantIndexCount * sizeof(ushort)));
                    }
                }

                // Falda solo en nodos que tocan el borde del terreno: dentro de un mismo terreno
                // el morph CDLOD ya garantiza continuidad; el riesgo de grieta está en la unión
                // entre dos Terrain vecinos que eligen LODs distintos.
                bool touchesBorder = node.X == 0 || node.Z == 0 || node.X + node.Size >= quads || node.Z + node.Size >= quads;
                if (terrain.DrawSkirts && touchesBorder)
                {
                    float skirtDepth = MathF.Min(MathF.Abs(terrain.HeightScale),
                        0.35f + node.Size * cellWorld * 0.05f);
                    GL.Uniform1(_tpSkirtDepthLoc, skirtDepth);
                    GL.Disable(EnableCap.CullFace);
                    TrackMainDraw(TerrainSkirtIndexCount);
                    GL.DrawElements(PrimitiveType.Triangles, TerrainSkirtIndexCount, DrawElementsType.UnsignedShort,
                        (IntPtr)(TerrainSkirtIndexStart * sizeof(ushort)));
                    GL.Enable(EnableCap.CullFace);
                }
            }
        }

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    // Proyección de sombra del terreno: mismo desplazamiento por heightmap pero con un programa
    // depth-only y un set fijo de nodos a LOD grueso (el shadow map no necesita el detalle fino y
    // así el pase cuesta pocas draws). Usa las texturas del frame anterior si el terreno recién
    // aparece (1 frame de retraso imperceptible). Solo pases direccional/spot (no point).
    private void DrawTerrainDepth()
    {
        if (_frameTerrainDraws.Count == 0 || _terrainDepthShader == 0)
            return;

        GL.UseProgram(_terrainDepthShader);
        var mvp = _currentDepthMvp;
        GL.UniformMatrix4(_tdMvpLoc, true, ref mvp);
        GL.Uniform1(_tdHeightmapLoc, 2);
        GL.BindVertexArray(_terrainGridVao);
        GL.ActiveTexture(TextureUnit.Texture2);

        foreach (var (obj, terrain, world) in _frameTerrainDraws)
        {
            if (!_terrainGpu.TryGetValue(obj.EditorId, out var data) || data.HeightTex == 0 || data.Tree.LodLevels == 0)
                continue;

            int res = Math.Max(2, terrain.Resolution);
            var worldMatrix = world;
            GL.UniformMatrix4(_tdModelLoc, true, ref worldMatrix);
            GL.Uniform3(_tdTerrainSizeLoc, terrain.SizeX, terrain.HeightScale, terrain.SizeZ);
            GL.Uniform1(_tdResolutionLoc, (float)res);
            GL.BindTexture(TextureTarget.Texture2D, data.HeightTex);

            int level = Math.Min(2, data.Tree.LodLevels - 1);
            int side = data.Tree.LeavesPerSide >> level;
            int size = TerrainQuadTree.LeafQuads << level;
            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    GL.Uniform4(_tdNodeOffsetScaleLoc, (float)(x * size), (float)(z * size), (float)size, 0f);
                    TrackShadowDraw();
                    GL.DrawElements(PrimitiveType.Triangles, TerrainQuadrantIndexCount * 4, DrawElementsType.UnsignedShort, IntPtr.Zero);
                }
            }
        }

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindVertexArray(0);
        GL.UseProgram(_shadowShader); // restaurar el programa del pase (direccional/spot)
    }

    private static int CreateTerrainCdlodShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aGrid;   // x,z en 0..32; z(componente 3)=1 en la falda

            uniform sampler2D uHeightmap;          // R32F, alturas crudas -2..2
            uniform mat4  uMvp;
            uniform mat4  uModel;
            uniform vec4  uNodeOffsetScale;        // x0, z0, tamaño del nodo (celdas), nivel LOD
            uniform vec2  uMorphConsts;            // morphStart, morphEnd (distancias locales)
            uniform vec3  uTerrainSize;            // SizeX, HeightScale, SizeZ
            uniform float uResolution;             // muestras por lado del heightmap
            uniform vec3  uLocalCameraPos;
            uniform float uSkirtDepth;

            out vec3  vWorldPos;
            out vec2  vUv;
            out float vViewDist;

            vec3 CellToLocal(vec2 cell, float rawHeight)
            {
                float inv = 1.0 / (uResolution - 1.0);
                return vec3(cell.x * inv * uTerrainSize.x - uTerrainSize.x * 0.5,
                            rawHeight * uTerrainSize.y,
                            cell.y * inv * uTerrainSize.z - uTerrainSize.z * 0.5);
            }

            float HeightAtCell(vec2 cell)
            {
                // Muestreo en centro de texel; el filtro lineal interpola celdas fraccionales
                // (necesario durante el morph).
                vec2 uv = (min(cell, vec2(uResolution - 1.0)) + 0.5) / uResolution;
                return texture(uHeightmap, uv).r;
            }

            void main()
            {
                float step = uNodeOffsetScale.z / 32.0;
                vec2 cell = uNodeOffsetScale.xy + aGrid.xy * step;

                // Morph CDLOD: los vértices impares de la rejilla se deslizan hacia el vecino par
                // a medida que el nodo se acerca al final de su rango de distancia, de modo que al
                // cambiar al nivel más grueso la malla ya coincide exactamente (sin pop ni grieta).
                vec2 clampedCell = min(cell, vec2(uResolution - 1.0));
                vec3 unmorphed = CellToLocal(clampedCell, HeightAtCell(cell));
                float dist = distance(unmorphed, uLocalCameraPos);
                float morphK = clamp((dist - uMorphConsts.x) / max(uMorphConsts.y - uMorphConsts.x, 0.001), 0.0, 1.0);
                vec2 oddPart = fract(aGrid.xy * 0.5) * 2.0;
                cell -= oddPart * step * morphK;
                cell = min(cell, vec2(uResolution - 1.0));

                float h = HeightAtCell(cell);
                vec3 local = CellToLocal(cell, h);
                if (aGrid.z > 0.5)
                    local.y -= uSkirtDepth;

                vec4 world = vec4(local, 1.0) * uModel;
                vWorldPos = world.xyz;
                vUv = cell / (uResolution - 1.0);
                vViewDist = dist;
                gl_Position = world * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec3  vWorldPos;
            in vec2  vUv;
            in float vViewDist;

            uniform sampler2D uSplatMap;           // unidad 0
            uniform sampler2DArray uShadowMap;     // unidad 1 (cascadas direccionales)
            uniform sampler2D uNormalTex;          // unidad 3 (normales horneadas, ya stitcheadas)
            uniform sampler2D uLayer0;             // unidades 4..7: albedo por capa
            uniform sampler2D uLayer1;
            uniform sampler2D uLayer2;
            uniform sampler2D uLayer3;
            uniform sampler2D uLayerNorm0;         // unidades 8..11: normal map por capa
            uniform sampler2D uLayerNorm1;
            uniform sampler2D uLayerNorm2;
            uniform sampler2D uLayerNorm3;
            uniform int uHasLayer0;  uniform int uHasLayer1;  uniform int uHasLayer2;  uniform int uHasLayer3;
            uniform int uHasLayerNorm0; uniform int uHasLayerNorm1; uniform int uHasLayerNorm2; uniform int uHasLayerNorm3;
            uniform float uTiling0;  uniform float uTiling1;  uniform float uTiling2;  uniform float uTiling3;

            uniform mat4  uModel;
            uniform vec2  uUvToTexel;              // vUv -> uv de centro de texel del normal map
            uniform vec3  uTerrainSize;
            uniform float uBlendSharpness;         // 0 = mezcla lineal, 1 = height-blend duro
            uniform int   uTriplanar;
            uniform float uBreakupDistance;        // 0 = sin anti-tiling por distancia

            uniform vec3  uAmbientColor;
            uniform float uAmbientIntensity;
            uniform float uSkyStrength;
            uniform vec3  uDirDir;
            uniform vec3  uDirColor;
            uniform float uDirIntensity;

            uniform int   uShadowEnabled;
            uniform float uShadowStrength;
            uniform int   uCascadeCount;
            uniform mat4  uCascadeLightMvp[5];
            uniform float uCascadeSplit[5];
            uniform vec3  uShadowCameraPos;
            uniform int   uShadowPcfRadius;
            uniform float uShadowBiasScale;
            uniform float uShadowNormalBias;

            out vec4 outColor;

            // Muestreo de una capa: tiling clásico + mezcla consigo misma a otro tiling pasado
            // uBreakupDistance (rompe la repetición visible de lejos) + proyección triplanar en
            // pendientes empinadas si está activada (evita el estirado de UV en paredes).
            vec4 SampleLayer(sampler2D tex, float tiling, vec3 N)
            {
                vec2 uv = vUv * tiling;
                vec4 c = texture(tex, uv);
                if (uBreakupDistance > 0.0 && vViewDist > uBreakupDistance)
                {
                    float f = clamp((vViewDist - uBreakupDistance) / uBreakupDistance, 0.0, 1.0) * 0.6;
                    c = mix(c, texture(tex, uv * 0.2127), f);
                }
                if (uTriplanar != 0)
                {
                    float sideAmount = clamp((0.72 - N.y) * 3.0, 0.0, 1.0);
                    if (sideAmount > 0.001)
                    {
                        float sideScale = tiling / max(uTerrainSize.x, 0.001);
                        vec3 w = abs(N);
                        w = w * w * w * w;
                        float wSum = max(w.x + w.z, 1e-4);
                        vec4 cx = texture(tex, vWorldPos.zy * sideScale);
                        vec4 cz = texture(tex, vWorldPos.xy * sideScale);
                        vec4 side = (cx * w.x + cz * w.z) / wSum;
                        c = mix(c, side, sideAmount);
                    }
                }
                return c;
            }

            vec3 SampleLayerNormal(sampler2D tex, int hasMap, float tiling)
            {
                if (hasMap == 0) return vec3(0.0, 0.0, 1.0);
                return texture(tex, vUv * tiling).rgb * 2.0 - 1.0;
            }

            float PcfShadowArray(sampler2DArray shadowMap, vec2 uv, int layer, float currentDepth, float bias, int radius)
            {
                vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0).xy);
                float shadow = 0.0;
                float taps = 0.0;
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                    {
                        float closest = texture(shadowMap, vec3(uv + vec2(x, y) * texel, float(layer))).r;
                        shadow += currentDepth - bias > closest ? 1.0 : 0.0;
                        taps += 1.0;
                    }
                return shadow / max(taps, 1.0);
            }

            // Mismo PCSS que el solid shader: penumbra creciente con la distancia oclusor-receptor.
            float PcssShadowArray(sampler2DArray shadowMap, vec2 uv, int layer, float currentDepth, float bias, float lightSize)
            {
                vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0).xy);
                float blockerSum = 0.0;
                float blockerCount = 0.0;
                for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                    {
                        float d = texture(shadowMap, vec3(uv + vec2(x, y) * texel * lightSize, float(layer))).r;
                        if (d < currentDepth - bias)
                        {
                            blockerSum += d;
                            blockerCount += 1.0;
                        }
                    }
                if (blockerCount < 0.5)
                    return 0.0;
                float avgBlocker = blockerSum / blockerCount;
                float penumbra = (currentDepth - avgBlocker) / max(avgBlocker, 1e-4) * lightSize;
                float spread = clamp(penumbra, 1.0, lightSize * 2.5) / 2.0;
                float shadow = 0.0;
                for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                    {
                        float d = texture(shadowMap, vec3(uv + vec2(x, y) * texel * spread, float(layer))).r;
                        shadow += currentDepth - bias > d ? 1.0 : 0.0;
                    }
                return shadow / 25.0;
            }

            float ShadowEdgeFade(vec2 uv)
            {
                const float edge = 0.06;
                float fx = smoothstep(0.0, edge, uv.x) * smoothstep(1.0, 1.0 - edge, uv.x);
                float fy = smoothstep(0.0, edge, uv.y) * smoothstep(1.0, 1.0 - edge, uv.y);
                return fx * fy;
            }

            float ShadowFactor(vec3 N, vec3 lightDir)
            {
                if (uShadowEnabled == 0) return 1.0;
                float viewDistance = length(vWorldPos - uShadowCameraPos);
                int cascade = max(uCascadeCount - 1, 0);
                for (int i = 0; i < 5; i++)
                {
                    if (i >= uCascadeCount) break;
                    if (viewDistance <= uCascadeSplit[i]) { cascade = i; break; }
                }

                vec3 biasedPos = vWorldPos + N * (uShadowNormalBias * (0.02 + 0.03 * float(cascade)));
                vec4 lightPos = vec4(biasedPos, 1.0) * uCascadeLightMvp[cascade];
                vec3 proj = lightPos.xyz / max(lightPos.w, 0.0001);
                proj = proj * 0.5 + 0.5;
                if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
                    return 1.0;

                float bias = max(0.0035 * (1.0 - dot(N, normalize(-lightDir))), 0.0012) * uShadowBiasScale;
                float shadow;
                if (uShadowPcfRadius > 0)
                    shadow = PcssShadowArray(uShadowMap, proj.xy, cascade, proj.z, bias, float(uShadowPcfRadius) * 2.5 + 2.0);
                else
                    shadow = (proj.z - bias > texture(uShadowMap, vec3(proj.xy, float(cascade))).r) ? 1.0 : 0.0;
                shadow *= ShadowEdgeFade(proj.xy);
                float visualStrength = clamp(uShadowStrength + clamp(uSkyStrength, 0.0, 1.0) * 0.18, 0.0, 0.95);
                return mix(1.0, 1.0 - shadow, visualStrength);
            }

            float Luminance(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

            void main()
            {
                // Normal geométrica horneada (ya continua entre terrenos vecinos).
                vec2 nUv = vUv * uUvToTexel.x + uUvToTexel.y;
                vec3 Ngeo = normalize(texture(uNormalTex, nUv).rgb * 2.0 - 1.0);
                Ngeo = normalize((vec4(Ngeo, 0.0) * uModel).xyz);

                // Pesos del splat normalizados.
                vec4 splat = texture(uSplatMap, vUv);
                float total = splat.r + splat.g + splat.b + splat.a;
                vec4 w = total > 1e-4 ? splat / total : vec4(1.0, 0.0, 0.0, 0.0);

                vec4 a0 = uHasLayer0 != 0 ? SampleLayer(uLayer0, uTiling0, Ngeo) : vec4(0.5, 0.5, 0.5, 1.0);
                vec4 a1 = uHasLayer1 != 0 ? SampleLayer(uLayer1, uTiling1, Ngeo) : vec4(0.5, 0.5, 0.5, 1.0);
                vec4 a2 = uHasLayer2 != 0 ? SampleLayer(uLayer2, uTiling2, Ngeo) : vec4(0.5, 0.5, 0.5, 1.0);
                vec4 a3 = uHasLayer3 != 0 ? SampleLayer(uLayer3, uTiling3, Ngeo) : vec4(0.5, 0.5, 0.5, 1.0);

                // Height-based blending: la "altura" aparente de cada capa (luminancia del albedo)
                // decide quién gana en la transición — pasto entre piedras, arena en grietas —
                // en vez del difuminado lineal clásico. uBlendSharpness interpola entre ambos.
                vec4 hgt = vec4(Luminance(a0.rgb), Luminance(a1.rgb), Luminance(a2.rgb), Luminance(a3.rgb)) * w;
                float ma = max(max(hgt.x, hgt.y), max(hgt.z, hgt.w));
                float depth = mix(0.7, 0.05, clamp(uBlendSharpness, 0.0, 1.0));
                vec4 b = max(hgt - (ma - depth), 0.0);
                b /= max(b.x + b.y + b.z + b.w, 1e-4);
                vec4 wts = mix(w, b, clamp(uBlendSharpness, 0.0, 1.0));

                vec3 albedo = a0.rgb * wts.x + a1.rgb * wts.y + a2.rgb * wts.z + a3.rgb * wts.w;

                // Normal maps por capa en espacio tangente del terreno (T≈+X, B≈+Z).
                vec3 nTan = SampleLayerNormal(uLayerNorm0, uHasLayerNorm0, uTiling0) * wts.x
                          + SampleLayerNormal(uLayerNorm1, uHasLayerNorm1, uTiling1) * wts.y
                          + SampleLayerNormal(uLayerNorm2, uHasLayerNorm2, uTiling2) * wts.z
                          + SampleLayerNormal(uLayerNorm3, uHasLayerNorm3, uTiling3) * wts.w;
                vec3 T = normalize(vec3(1.0, 0.0, 0.0) - Ngeo * Ngeo.x);
                vec3 B = cross(Ngeo, T);
                vec3 N = normalize(T * nTan.x + B * nTan.y + Ngeo * max(nTan.z, 0.15));

                // Misma iluminación que el resto del motor (cielo + relleno de suelo + sol) más
                // la sombra direccional en cascada.
                const float SKY_DIFFUSE_FILL = 0.48;
                vec3 sky = uAmbientColor * (uAmbientIntensity + max(N.y, 0.0) * uSkyStrength * SKY_DIFFUSE_FILL);
                vec3 ground = uAmbientColor * uAmbientIntensity * 0.35 * max(-N.y, 0.0);
                vec3 ambientLight = sky + ground;

                vec3 sunL = normalize(-uDirDir);
                float shadow = ShadowFactor(N, uDirDir);
                vec3 diffuse = uDirColor * uDirIntensity * max(dot(N, sunL), 0.0) * shadow;

                vec3 hdr = albedo * (ambientLight + diffuse);
                outColor = vec4(max(hdr, vec3(0.0)), 1.0);
            }
            """;

        return LinkTerrainProgram(vertexSource, fragmentSource);
    }

    // Depth-only para el shadow map: mismo desplazamiento por heightmap, sin morph (el shadow map
    // se dibuja a LOD grueso fijo; una diferencia sub-texel con la malla principal es invisible).
    private static int CreateTerrainDepthShader()
    {
        const string vertexSource = """
            #version 330 core
            layout (location = 0) in vec3 aGrid;
            uniform sampler2D uHeightmap;
            uniform mat4  uMvp;
            uniform mat4  uModel;
            uniform vec4  uNodeOffsetScale;
            uniform vec3  uTerrainSize;
            uniform float uResolution;
            void main()
            {
                float step = uNodeOffsetScale.z / 32.0;
                vec2 cell = min(uNodeOffsetScale.xy + aGrid.xy * step, vec2(uResolution - 1.0));
                vec2 uv = (cell + 0.5) / uResolution;
                float h = texture(uHeightmap, uv).r * uTerrainSize.y;
                float inv = 1.0 / (uResolution - 1.0);
                vec3 local = vec3(cell.x * inv * uTerrainSize.x - uTerrainSize.x * 0.5,
                                  h,
                                  cell.y * inv * uTerrainSize.z - uTerrainSize.z * 0.5);
                gl_Position = (vec4(local, 1.0) * uModel) * uMvp;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            void main() { }
            """;

        return LinkTerrainProgram(vertexSource, fragmentSource);
    }

    private static int LinkTerrainProgram(string vertexSource, string fragmentSource)
    {
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
}
