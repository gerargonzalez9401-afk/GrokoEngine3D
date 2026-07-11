using GrokoEngine;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class SceneViewportRenderer
{
    // â”€â”€ LOD automÃ¡tico por nombre (estilo Unity) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Muchos modelos (SpeedTree, kits de assets) traen la cadena LOD completa como
    // submallas "X_LOD0", "X_LOD1", ... Este mÃ³dulo la detecta por nombre y elige la
    // submalla segÃºn el TAMAÃ‘O EN PANTALLA (radio/distancia) en el build del frame:
    // el pase de color, las sombras y el instancing usan automÃ¡ticamente la versiÃ³n
    // elegida (un Ã¡rbol lejano dibuja 1.600 tris en vez de 13.000, tambiÃ©n en sombras).

    /// <summary>Multiplicador de detalle LOD global: &gt;1 mantiene el detalle a mÃ¡s distancia.</summary>
    public float LodBias = 1f;

    // LodGroup activo durante el recorrido de la jerarquÃ­a (el mÃ¡s cercano al objeto gana,
    // como en Unity). Lo establece BuildObjectRecursive con guardar/restaurar.
    private LodGroup? _activeLodGroup;

    // LOD jerarquico: algunos assets traen los niveles como hijos separados
    // (Ash_LOD0, Ash_LOD1, Ash_LOD2...), no solo como submallas. Bajo un LodGroup
    // se dibuja un solo nivel y nunca se hornea en el static batch, porque cambia
    // con la camara igual que en Unity.
    private HierarchyLodContext? _activeHierarchyLod;

    // Camino rapido historico para bosques/props Static: no calcula bounds/LOD por frame
    // y mantiene los hijos LOD en el batch estatico, igual que antes de introducir LOD Pro.
    private bool _activeStaticHierarchyLodFastPath;

    private readonly record struct HierarchyLodContext(LodGroup Group, int SelectedLevel, bool Culled, Vector3 WorldCenter, float WorldRadius);
    private readonly record struct HierarchyLodBoundsCacheEntry(Vector3 LocalMin, Vector3 LocalMax, bool HasBounds);

    // Bounds local del LOD jerarquico. Antes se recalculaba recorriendo todos los hijos
    // LOD0 de cada arbol cada frame; con cientos de arboles eso castigaba Build/Collect.
    // Se invalida junto con el static batch cuando cambian mallas/materiales/jerarquia.
    private readonly Dictionary<GameObject, HierarchyLodBoundsCacheEntry> _hierarchyLodBoundsCache = new();

    // Cache de la decisiÃ³n "objeto estÃ¡tico â†’ pase dinÃ¡mico" (transparencia/tamaÃ±o), por objeto
    // con TTL. Ver BuildObjectRecursive. Se limpia en InvalidateStaticBatch.
    private readonly Dictionary<GameObject, (long NextCheckTick, bool NeedsDynamicPass)> _staticRouteCache = new();

    // Nodos que son hijos-variante LOD (Ash1_LOD1, Leaves2_LOD3, ...): un FBX importado "con
    // hijos" trae uno por variante y NUNCA dibujan. Con muchos Ã¡rboles son cientos de nodos;
    // sin este cache pagaban transform+componentes+ruta cada frame solo para descartarse
    // (medido: ~4 ms con ~600 variantes). Se limpia en InvalidateStaticBatch.
    private readonly Dictionary<GameObject, (long NextCheckTick, bool IsVariant)> _lodVariantNodeCache = new();

    // Perfil por defecto para bosques/props Static: prioriza 120 FPS. En LOD Pro se usan
    // exactamente los umbrales del componente para depurar comportamiento visual.
    private const float StaticHierarchyPerformanceBiasMultiplier = 0.75f;
    private const float StaticHierarchyPerformanceMinCull = 0.010f;

    private bool IsLodVariantNode(GameObject obj)
    {
        // Si estamos dentro de un LodGroup jerarquico, ShouldSkipHierarchyLodNode ya eligio
        // que nivel se dibuja y corto los otros niveles antes de recorrer sus hijos.
        // No apliques aqui la regla vieja de "submesh LOD1+ nunca dibuja": un LOD elegido
        // puede traer varias partes/materiales (tronco + hojas) y todas deben renderizarse.
        if (_activeHierarchyLod.HasValue)
            return false;

        // El modo profesional actual usa el componente LodGroup para decidir que nivel
        // se dibuja. El detector legacy por nombres de submalla queda disponible solo
        // cuando hay un LodGroup activo, o si se habilita explicitamente el modo automatico.
        if (!ShouldUseSubmeshLodSelection())
            return false;

        long now = Environment.TickCount64;
        if (_lodVariantNodeCache.TryGetValue(obj, out var cached) && now < cached.NextCheckTick)
            return cached.IsVariant;

        bool isVariant = false;
        if (obj.Children.Count == 0 &&
            obj.GetComponent<MeshFilter>() is { } mf &&
            mf.SubmeshIndex >= 0 &&
            !string.IsNullOrWhiteSpace(mf.MeshPath) &&
            GetParsedMesh(mf.MeshPath) is { } mesh)
        {
            isVariant = IsLodVariantSubmesh(mesh, mf.SubmeshIndex);
        }

        _lodVariantNodeCache[obj] = (now + 3000 + (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj) & 1023), isVariant);
        return isVariant;
    }

    private bool UsesStaticHierarchyLodFastPath(GameObject obj) => false;

    private static bool TryGetHierarchyLodLevel(string? name, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var match = LodNameRegex.Match(name);
        return match.Success && int.TryParse(match.Groups[2].Value, out level);
    }

    private static bool HasHierarchyLodChildren(GameObject obj)
    {
        for (int i = 0; i < obj.Children.Count; i++)
            if (TryGetHierarchyLodLevel(obj.Children[i].Name, out _))
                return true;
        return false;
    }

    private static int GetHighestHierarchyLodLevel(GameObject obj)
    {
        int highest = 0;
        for (int i = 0; i < obj.Children.Count; i++)
            if (TryGetHierarchyLodLevel(obj.Children[i].Name, out int level))
                highest = Math.Max(highest, level);
        return highest;
    }

    private bool ShouldSkipHierarchyLodNode(GameObject obj)
    {
        if (_activeHierarchyLod is not { } context ||
            !TryGetHierarchyLodLevel(obj.Name, out int level))
            return false;

        bool skip = context.Culled || level != context.SelectedLevel;
        if (skip)
            _statsHierarchyLodSkipped++;
        return skip;
    }

    private HierarchyLodContext? CreateHierarchyLodContext(GameObject obj, LodGroup lod, in Matrix4 world)
    {
        if (!HasHierarchyLodChildren(obj))
            return null;

        EstimateHierarchyLodBounds(obj, world, out var center, out float radius);
        int highestAvailableLevel = GetHighestHierarchyLodLevel(obj);
        float effectiveBias = MathF.Max(0.01f, lod.Bias);
        float effectiveCullBelow = lod.CulledBelow;
        if (obj.IsStatic && StaticHierarchyLodMode == StaticHierarchyLodMode.Performance120)
        {
            effectiveBias *= StaticHierarchyPerformanceBiasMultiplier;
            effectiveCullBelow = MathF.Max(effectiveCullBelow, StaticHierarchyPerformanceMinCull);
        }

        float relative = ComputeLodRelativeHeight(center, radius) * MathF.Max(0.01f, LodBias) * effectiveBias;

        if (!_buildingStatic && effectiveCullBelow > 0f && relative < effectiveCullBelow)
        {
            _statsHierarchyLodCulled++;
            return new HierarchyLodContext(lod, 3, true, center, radius);
        }

        int selected = relative > lod.Lod0 ? 0 :
                       relative > lod.Lod1 ? 1 :
                       relative > lod.Lod2 ? 2 : 3;
        selected = Math.Min(selected, highestAvailableLevel);

        if (!_buildingStatic)
        {
            switch (selected)
            {
                case 0: _statsHierarchyLod0++; break;
                case 1: _statsHierarchyLod1++; break;
                case 2: _statsHierarchyLod2++; break;
                default: _statsHierarchyLod3++; break;
            }
        }

        return new HierarchyLodContext(lod, selected, false, center, radius);
    }

    private void EstimateHierarchyLodBounds(GameObject root, in Matrix4 rootWorld, out Vector3 center, out float radius)
    {
        var cached = GetHierarchyLodLocalBounds(root);
        if (!cached.HasBounds)
        {
            center = Vector3.TransformPosition(Vector3.Zero, rootWorld);
            radius = 1f;
            return;
        }

        bool any = false;
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        AccumulateTransformedAabb(
            rootWorld,
            cached.LocalMin.X, cached.LocalMin.Y, cached.LocalMin.Z,
            cached.LocalMax.X, cached.LocalMax.Y, cached.LocalMax.Z,
            ref min, ref max, ref any);

        if (!any)
        {
            center = Vector3.TransformPosition(Vector3.Zero, rootWorld);
            radius = 1f;
            return;
        }

        center = (min + max) * 0.5f;
        radius = MathF.Max(0.0001f, (max - center).Length);
    }

    private HierarchyLodBoundsCacheEntry GetHierarchyLodLocalBounds(GameObject root)
    {
        if (_hierarchyLodBoundsCache.TryGetValue(root, out var cached))
            return cached;

        bool any = false;
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        for (int i = 0; i < root.Children.Count; i++)
            AccumulateHierarchyLodLocalBounds(root.Children[i], Matrix4.Identity, ref min, ref max, ref any);

        cached = any
            ? new HierarchyLodBoundsCacheEntry(min, max, true)
            : new HierarchyLodBoundsCacheEntry(Vector3.Zero, Vector3.Zero, false);
        _hierarchyLodBoundsCache[root] = cached;
        return cached;
    }

    private void AccumulateHierarchyLodLocalBounds(GameObject obj, in Matrix4 parentLocal, ref Vector3 min, ref Vector3 max, ref bool any)
    {
        if (!obj.IsActive)
            return;

        Matrix4 local = GetLocalMatrix(obj) * parentLocal;
        bool isLodNode = TryGetHierarchyLodLevel(obj.Name, out int level);

        if ((!isLodNode || level == 0) &&
            obj.GetComponent<MeshFilter>() is { } mf &&
            !string.IsNullOrWhiteSpace(mf.MeshPath) &&
            GetParsedMesh(mf.MeshPath) is { } mesh)
        {
            float scale = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
            AccumulateTransformedAabb(
                local,
                mesh.BoundsMin.X * scale, mesh.BoundsMin.Y * scale, mesh.BoundsMin.Z * scale,
                mesh.BoundsMax.X * scale, mesh.BoundsMax.Y * scale, mesh.BoundsMax.Z * scale,
                ref min, ref max, ref any);
        }

        for (int i = 0; i < obj.Children.Count; i++)
            AccumulateHierarchyLodLocalBounds(obj.Children[i], local, ref min, ref max, ref any);
    }

    private void AccumulateHierarchyLodBounds(GameObject obj, in Matrix4 parentWorld, ref Vector3 min, ref Vector3 max, ref bool any)
    {
        if (!obj.IsActive)
            return;

        Matrix4 world = GetLocalMatrix(obj) * parentWorld;
        bool isLodNode = TryGetHierarchyLodLevel(obj.Name, out int level);

        if ((!isLodNode || level == 0) &&
            obj.GetComponent<MeshFilter>() is { } mf &&
            !string.IsNullOrWhiteSpace(mf.MeshPath) &&
            GetParsedMesh(mf.MeshPath) is { } mesh)
        {
            float scale = mf.ImportScale <= 0f ? 1f : mf.ImportScale;
            AccumulateTransformedAabb(
                world,
                mesh.BoundsMin.X * scale, mesh.BoundsMin.Y * scale, mesh.BoundsMin.Z * scale,
                mesh.BoundsMax.X * scale, mesh.BoundsMax.Y * scale, mesh.BoundsMax.Z * scale,
                ref min, ref max, ref any);
        }

        for (int i = 0; i < obj.Children.Count; i++)
            AccumulateHierarchyLodBounds(obj.Children[i], world, ref min, ref max, ref any);
    }

    private static void AccumulateTransformedAabb(
        in Matrix4 world,
        float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ,
        ref Vector3 min,
        ref Vector3 max,
        ref bool any)
    {
        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(minX, minY, minZ), new(maxX, minY, minZ),
            new(minX, maxY, minZ), new(maxX, maxY, minZ),
            new(minX, minY, maxZ), new(maxX, minY, maxZ),
            new(minX, maxY, maxZ), new(maxX, maxY, maxZ)
        };

        for (int i = 0; i < corners.Length; i++)
        {
            var p = Vector3.TransformPosition(corners[i], world);
            min = Vector3.ComponentMin(min, p);
            max = Vector3.ComponentMax(max, p);
            any = true;
        }
    }

    /// <summary>true si la submalla del MeshFilter es una variante LOD1+ (nunca se dibuja directa).</summary>
    private static bool IsLodVariantSubmesh(ParsedMesh mesh, int submeshIndex)
    {
        if (submeshIndex < 0)
            return false;
        var info = GetMeshLodInfo(mesh);
        return info.HasChains && submeshIndex < info.IsLodVariant.Length && info.IsLodVariant[submeshIndex];
    }

    // â”€â”€ Cache de draws por objeto (graba-y-reproduce) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Reconstruir el draw de cada malla desde cero costaba ~30 Âµs por objeto y frame
    // (materiales, slots, texturas, structs) aunque NADA hubiera cambiado â€” con cientos
    // de props era el mayor coste de CPU del frame (medido: ~7 ms con 237 hijos de Ã¡rbol).
    // Mientras no cambien transform, nivel LOD ni materiales (TTL + invalidaciÃ³n explÃ­cita),
    // se reutilizan los draws grabados: el coste por objeto cae a un lookup + appends.
    private readonly Dictionary<GameObject, MeshDrawCacheEntry> _meshDrawCache = new();

    private sealed class MeshDrawCacheEntry
    {
        public Matrix4 World;
        public int LodKey;
        public long ExpiresAt;
        public DynamicMeshDraw[] Dynamic = Array.Empty<DynamicMeshDraw>();
        public DynamicMeshDraw[] Refractive = Array.Empty<DynamicMeshDraw>();
        public ShaderGraphDynamicMeshDraw[] ShaderGraph = Array.Empty<ShaderGraphDynamicMeshDraw>();
    }

    // Clave del estado LOD actual del objeto: si la distancia cruza un umbral, cambia y
    // el cache se regenera con la submalla del nivel nuevo.
    private int ComputeLodKey(ParsedMesh mesh, MeshFilter filter, in Matrix4 world, float importScale)
    {
        if (!ShouldUseSubmeshLodSelection())
            return 0;

        var info = GetMeshLodInfo(mesh);
        if (!info.HasChains)
            return 0;

        if (filter.SubmeshIndex >= 0)
            return filter.SubmeshIndex < info.Chains.Length && info.Chains[filter.SubmeshIndex] != null
                ? SelectLodSubmesh(mesh, info, filter.SubmeshIndex, world, importScale)
                : 0;

        var hash = new HashCode();
        for (int i = 0; i < info.Chains.Length; i++)
            if (info.Chains[i] != null)
                hash.Add(SelectLodSubmesh(mesh, info, i, world, importScale));
        return hash.ToHashCode();
    }

    private bool ShouldUseSubmeshLodSelection() =>
        AutomaticSubmeshLodEnabled || _activeLodGroup != null;

    private void BuildMeshBoundsCached(GameObject obj, MeshFilter filter, ParsedMesh mesh, bool selected, Matrix4 transform)
    {
        // Casos que quedan FUERA del cache (siguen el camino completo): pase estÃ¡tico,
        // objetos seleccionados (wireframe), mallas con esqueleto, y el preview de arrastre.
        bool cacheable = !_buildingStatic &&
                         !selected &&
                         !mesh.HasSkin &&
                         (PreviewMaterialObjectId == null || PreviewMaterialObjectId != obj.EditorId);
        if (!cacheable)
        {
            BuildMeshBounds(obj, filter, mesh, selected, transform);
            return;
        }

        float importScale = filter.ImportScale <= 0f ? 1f : filter.ImportScale;
        int lodKey = ComputeLodKey(mesh, filter, transform, importScale);
        if (IsLodGroupCulled(mesh, filter, transform, importScale))
            lodKey = HashCode.Combine(lodKey, -1);   // el estado "culled" tambiÃ©n invalida el cache
        long now = Environment.TickCount64;

        if (_meshDrawCache.TryGetValue(obj, out var entry) &&
            now < entry.ExpiresAt &&
            entry.LodKey == lodKey &&
            entry.World == transform)
        {
            // Hit: reproducir los draws grabados.
            for (int i = 0; i < entry.Dynamic.Length; i++)
                dynamicMeshDraws.Add(entry.Dynamic[i]);
            for (int i = 0; i < entry.Refractive.Length; i++)
                refractiveMeshDraws.Add(entry.Refractive[i]);
            for (int i = 0; i < entry.ShaderGraph.Length; i++)
                shaderGraphDynamicMeshDraws.Add(entry.ShaderGraph[i]);
            return;
        }

        // Miss: grabar lo que BuildMeshBounds encola. Solo se cachea si TODO fue a las tres
        // listas de draws GPU; si tocÃ³ cualquier otro sink (vÃ©rtices CPU, skinned, lÃ­neas),
        // el objeto no es cacheable este frame y se deja sin entrada.
        int dyn0 = dynamicMeshDraws.Count;
        int refr0 = refractiveMeshDraws.Count;
        int sg0 = shaderGraphDynamicMeshDraws.Count;
        int solid0 = solidVertices.Count;
        int lines0 = vertices.Count;
        int skin0 = skinnedMeshDraws.Count;
        int refrSkin0 = refractiveSkinnedMeshDraws.Count;
        int sgSkin0 = shaderGraphSkinnedMeshDraws.Count;

        BuildMeshBounds(obj, filter, mesh, selected, transform);

        bool pureGpuDraws = solidVertices.Count == solid0 &&
                            vertices.Count == lines0 &&
                            skinnedMeshDraws.Count == skin0 &&
                            refractiveSkinnedMeshDraws.Count == refrSkin0 &&
                            shaderGraphSkinnedMeshDraws.Count == sgSkin0;
        if (!pureGpuDraws)
        {
            _meshDrawCache.Remove(obj);
            return;
        }

        entry = new MeshDrawCacheEntry
        {
            World = transform,
            LodKey = lodKey,
            // TTL corto + jitter: recoge cambios de material no invalidados explÃ­citamente
            // sin que todos los objetos regraben en el mismo frame.
            ExpiresAt = now + (obj.IsStatic ? 60000 : 1000) + (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj) & 511),
            Dynamic = CopyRange(dynamicMeshDraws, dyn0),
            Refractive = CopyRange(refractiveMeshDraws, refr0),
            ShaderGraph = CopyRange(shaderGraphDynamicMeshDraws, sg0)
        };
        _meshDrawCache[obj] = entry;
    }

    private static T[] CopyRange<T>(List<T> list, int start)
    {
        int count = list.Count - start;
        if (count <= 0)
            return Array.Empty<T>();
        var result = new T[count];
        list.CopyTo(start, result, 0, count);
        return result;
    }

    private Vector3 _lodCameraPosition;
    private float _lodCameraTanHalfFov = MathF.Tan(MathHelper.DegreesToRadians(60f) * 0.5f);
    private bool _lodCameraOrthographic;
    private float _lodCameraOrthoHalfHeight = 5f;

    private static readonly Regex LodNameRegex = new(@"^(.*?)[_\s]?LOD(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Cacheado por instancia de malla: si el ObjLoader recarga el archivo, la instancia
    // nueva trae su propia entrada y la vieja se recolecta sola.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ParsedMesh, MeshLodInfo> meshLodInfos = new();

    private sealed class MeshLodInfo
    {
        public static readonly MeshLodInfo Empty = new();

        // Chains[i] = Ã­ndices de submalla (nivel 0 primero) si la submalla i es un LOD0 con variantes.
        public int[]?[] Chains = Array.Empty<int[]?>();
        // true si la submalla es LOD1+ de alguna cadena: al dibujar el modelo ENTERO no se
        // dibuja de base (antes se pintaban los 4 niveles superpuestos).
        public bool[] IsLodVariant = Array.Empty<bool>();
        public bool HasChains;
    }

    private static MeshLodInfo GetMeshLodInfo(ParsedMesh mesh) =>
        meshLodInfos.GetValue(mesh, BuildMeshLodInfo);

    private static MeshLodInfo BuildMeshLodInfo(ParsedMesh mesh)
    {
        if (mesh.Submeshes.Count < 2)
            return MeshLodInfo.Empty;

        Dictionary<string, List<(int Lod, int Index)>>? groups = null;
        for (int i = 0; i < mesh.Submeshes.Count; i++)
        {
            string name = mesh.Submeshes[i].MeshName;
            if (string.IsNullOrEmpty(name))
                continue;
            var match = LodNameRegex.Match(name);
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out int lod))
                continue;

            groups ??= new Dictionary<string, List<(int, int)>>(StringComparer.OrdinalIgnoreCase);
            string baseName = match.Groups[1].Value;
            if (!groups.TryGetValue(baseName, out var list))
                groups[baseName] = list = new List<(int, int)>();
            list.Add((lod, i));
        }

        if (groups == null)
            return MeshLodInfo.Empty;

        MeshLodInfo? info = null;
        foreach (var list in groups.Values)
        {
            if (list.Count < 2)
                continue;
            list.Sort(static (a, b) => a.Lod.CompareTo(b.Lod));
            if (list[0].Lod != 0)
                continue;

            info ??= new MeshLodInfo
            {
                Chains = new int[]?[mesh.Submeshes.Count],
                IsLodVariant = new bool[mesh.Submeshes.Count]
            };

            var chain = new int[list.Count];
            for (int k = 0; k < list.Count; k++)
                chain[k] = list[k].Index;
            info.Chains[list[0].Index] = chain;
            for (int k = 1; k < list.Count; k++)
                info.IsLodVariant[list[k].Index] = true;
            info.HasChains = true;
        }

        return info ?? MeshLodInfo.Empty;
    }

    // Umbrales de tamaÃ±o relativo en pantalla (radio/distancia), estilo LOD Group:
    // por encima del primero se usa LOD0; el Ãºltimo nivel de la cadena cubre el resto.
    private static readonly float[] LodRelativeCutoffs = { 0.12f, 0.055f, 0.025f };

    private int SelectLodSubmesh(ParsedMesh mesh, MeshLodInfo info, int submeshIndex, in Matrix4 world, float importScale)
    {
        var chain = info.Chains[submeshIndex];
        if (chain == null || chain.Length <= 1)
            return submeshIndex;

        var sub = mesh.Submeshes[submeshIndex];
        var localCenter = new Vector3(
            (sub.MinX + sub.MaxX) * 0.5f * importScale,
            (sub.MinY + sub.MaxY) * 0.5f * importScale,
            (sub.MinZ + sub.MaxZ) * 0.5f * importScale);
        var worldCenter = Vector3.TransformPosition(localCenter, world);

        float ex = (sub.MaxX - sub.MinX) * 0.5f * importScale;
        float ey = (sub.MaxY - sub.MinY) * 0.5f * importScale;
        float ez = (sub.MaxZ - sub.MinZ) * 0.5f * importScale;
        float localRadius = MathF.Sqrt(ex * ex + ey * ey + ez * ez);
        float worldScale = MathF.Max(world.Row0.Xyz.Length, MathF.Max(world.Row1.Xyz.Length, world.Row2.Xyz.Length));
        float radius = MathF.Max(0.0001f, localRadius * worldScale);

        float relative = ComputeLodRelativeHeight(worldCenter, radius);
        if (float.IsPositiveInfinity(relative))
            return chain[0];

        relative *= MathF.Max(0.01f, LodBias);

        // Umbrales: los del LodGroup activo (componente, configurable en el inspector)
        // o los por defecto del motor si el objeto no tiene ninguno en su jerarquÃ­a.
        float t0 = LodRelativeCutoffs[0], t1 = LodRelativeCutoffs[1], t2 = LodRelativeCutoffs[2];
        float culledBelow = 0f;
        if (_activeLodGroup is { } group)
        {
            relative *= MathF.Max(0.01f, group.Bias);
            t0 = group.Lod0;
            t1 = group.Lod1;
            t2 = group.Lod2;
            culledBelow = group.CulledBelow;
        }

        // -1 = culled: el objeto es tan pequeÃ±o en pantalla que no se dibuja (en el pase
        // estÃ¡tico no se aplica â€” el batch se hornea una vez y el objeto desaparecerÃ­a).
        if (culledBelow > 0f && relative < culledBelow && !_buildingStatic)
            return -1;

        int level = relative > t0 ? 0 : relative > t1 ? 1 : relative > t2 ? 2 : 3;
        return chain[Math.Min(level, chain.Length - 1)];
    }

    // Cull por LodGroup para mallas SIN cadena LOD (el componente tambiÃ©n sirve para ocultar
    // props pequeÃ±os a distancia, como el "Culled" de Unity). Usa los bounds de la submalla
    // dibujada (o del modelo entero) con la misma mÃ©trica que SelectLodSubmesh.
    private bool IsLodGroupCulled(ParsedMesh mesh, MeshFilter filter, in Matrix4 world, float importScale)
    {
        // En LOD jerarquico el culling se decide una sola vez en la raiz del LodGroup.
        // No culles tronco/hojas por separado: eso produce arboles incompletos y no imita Unity.
        if (_activeHierarchyLod.HasValue)
            return false;

        if (_buildingStatic || _activeLodGroup is not { } group || group.CulledBelow <= 0f)
            return false;

        float minX = mesh.BoundsMin.X, minY = mesh.BoundsMin.Y, minZ = mesh.BoundsMin.Z;
        float maxX = mesh.BoundsMax.X, maxY = mesh.BoundsMax.Y, maxZ = mesh.BoundsMax.Z;
        if (filter.SubmeshIndex >= 0 && filter.SubmeshIndex < mesh.Submeshes.Count)
        {
            var sub = mesh.Submeshes[filter.SubmeshIndex];
            minX = sub.MinX; minY = sub.MinY; minZ = sub.MinZ;
            maxX = sub.MaxX; maxY = sub.MaxY; maxZ = sub.MaxZ;
        }

        var localCenter = new Vector3(
            (minX + maxX) * 0.5f * importScale,
            (minY + maxY) * 0.5f * importScale,
            (minZ + maxZ) * 0.5f * importScale);
        var worldCenter = Vector3.TransformPosition(localCenter, world);

        float ex = (maxX - minX) * 0.5f * importScale;
        float ey = (maxY - minY) * 0.5f * importScale;
        float ez = (maxZ - minZ) * 0.5f * importScale;
        float worldScale = MathF.Max(world.Row0.Xyz.Length, MathF.Max(world.Row1.Xyz.Length, world.Row2.Xyz.Length));
        float radius = MathF.Max(0.0001f, MathF.Sqrt(ex * ex + ey * ey + ez * ez) * worldScale);

        float relative = ComputeLodRelativeHeight(worldCenter, radius);
        if (float.IsPositiveInfinity(relative))
            return false;

        relative *= MathF.Max(0.01f, LodBias) * MathF.Max(0.01f, group.Bias);
        return relative < group.CulledBelow;
    }

    // Aproximacion Unity-style: LODGroup trabaja con altura relativa en pantalla.
    // En perspectiva: radio / (distancia * tan(FOV/2)); en ortografica: radio / halfHeight.
    private float ComputeLodRelativeHeight(Vector3 worldCenter, float worldRadius)
    {
        worldRadius = MathF.Max(0.0001f, worldRadius);
        if (_lodCameraOrthographic)
            return worldRadius / MathF.Max(0.0001f, _lodCameraOrthoHalfHeight);

        float distance = (worldCenter - _lodCameraPosition).Length;
        if (distance <= worldRadius)
            return float.PositiveInfinity;

        return worldRadius / (MathF.Max(0.0001f, distance) * MathF.Max(0.01f, _lodCameraTanHalfFov));
    }
}


