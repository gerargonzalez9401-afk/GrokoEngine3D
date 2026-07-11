using System;
using System.Collections.Generic;
using MiMotor.Mathematics;

namespace GrokoEngine.Rendering;

public enum TerrainTreeRenderMode
{
    Culled,
    Mesh,
    Billboard
}

public readonly record struct TerrainTreeLodSettings(
    float TreeDistance,
    float BillboardStart,
    int MaxMeshTrees,
    float CellSize)
{
    public const float DefaultCellSize = 32f;

    public static TerrainTreeLodSettings FromTerrain(Terrain terrain) => new(
        MathF.Max(0.01f, terrain.TreeDistance),
        MathF.Max(0f, terrain.BillboardStart),
        Math.Max(0, terrain.MaxMeshTrees),
        MathF.Max(1f, terrain.TreeCellSize));
}

public readonly record struct TerrainTreeLodDecision(
    int TreeIndex,
    TerrainTreeRenderMode Mode,
    int CellX,
    int CellZ,
    float DistanceSq);

public sealed class TerrainTreeLodResult
{
    private readonly List<TerrainTreeLodDecision> _decisions = new();

    public IReadOnlyList<TerrainTreeLodDecision> Decisions => _decisions;
    public int MeshCount { get; private set; }
    public int BillboardCount { get; private set; }
    public int CulledCount { get; private set; }
    public int VisibleCellCount { get; private set; }

    internal void Clear()
    {
        _decisions.Clear();
        MeshCount = 0;
        BillboardCount = 0;
        CulledCount = 0;
        VisibleCellCount = 0;
    }

    internal void Add(TerrainTreeLodDecision decision)
    {
        _decisions.Add(decision);
        switch (decision.Mode)
        {
            case TerrainTreeRenderMode.Mesh:
                MeshCount++;
                break;
            case TerrainTreeRenderMode.Billboard:
                BillboardCount++;
                break;
            default:
                CulledCount++;
                break;
        }
    }

    internal void SetVisibleCellCount(int value) => VisibleCellCount = value;
    internal void AddCulled(int count = 1) => CulledCount += Math.Max(0, count);
}

public sealed class TerrainTreeLodScratch
{
    internal readonly List<TerrainTreeLod.Candidate> Visible = new(1024);
    internal readonly HashSet<(int X, int Z)> VisibleCells = new();

    public TerrainTreeLodResult Result { get; } = new();

    internal void Clear()
    {
        Visible.Clear();
        VisibleCells.Clear();
        Result.Clear();
    }
}

public static class TerrainTreeLod
{
    internal readonly record struct Candidate(int Index, float DistanceSq, int CellX, int CellZ);

    public static TerrainTreeLodResult Classify(Terrain terrain, Vector3 cameraLocal, TerrainTreeLodSettings? settingsOverride = null)
    {
        var scratch = new TerrainTreeLodScratch();
        return ClassifyInto(terrain, cameraLocal, scratch, settingsOverride);
    }

    public static TerrainTreeLodResult ClassifyInto(
        Terrain terrain,
        Vector3 cameraLocal,
        TerrainTreeLodScratch scratch,
        TerrainTreeLodSettings? settingsOverride = null)
    {
        var settings = settingsOverride ?? TerrainTreeLodSettings.FromTerrain(terrain);
        float treeDistanceSq = settings.TreeDistance * settings.TreeDistance;
        float billboardStartSq = settings.BillboardStart * settings.BillboardStart;

        scratch.Clear();
        var result = scratch.Result;
        if (terrain.Trees.Count == 0 || terrain.TreePrototypes.Count == 0)
            return result;

        for (int i = 0; i < terrain.Trees.Count; i++)
        {
            var tree = terrain.Trees[i];

            if (tree.PrototypeIndex < 0 || tree.PrototypeIndex >= terrain.TreePrototypes.Count)
            {
                result.AddCulled();
                continue;
            }

            float dx = tree.LocalX - cameraLocal.X;
            float dz = tree.LocalZ - cameraLocal.Z;
            float distanceSq = dx * dx + dz * dz;
            if (distanceSq > treeDistanceSq)
            {
                result.AddCulled();
                continue;
            }

            var (cellX, cellZ) = ComputeCell(tree.LocalX, tree.LocalZ, settings.CellSize);
            scratch.Visible.Add(new Candidate(i, distanceSq, cellX, cellZ));
            scratch.VisibleCells.Add((cellX, cellZ));
        }

        scratch.Visible.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));

        for (int rank = 0; rank < scratch.Visible.Count; rank++)
        {
            var candidate = scratch.Visible[rank];
            var mode = rank < settings.MaxMeshTrees && candidate.DistanceSq <= billboardStartSq
                ? TerrainTreeRenderMode.Mesh
                : TerrainTreeRenderMode.Billboard;
            result.Add(new TerrainTreeLodDecision(candidate.Index, mode, candidate.CellX, candidate.CellZ, candidate.DistanceSq));
        }

        result.SetVisibleCellCount(scratch.VisibleCells.Count);
        return result;
    }

    public static (int X, int Z) ComputeCell(float localX, float localZ, float cellSize)
    {
        cellSize = MathF.Max(1f, cellSize);
        return ((int)MathF.Floor(localX / cellSize), (int)MathF.Floor(localZ / cellSize));
    }

    public static Vector3 ComputeCellCenter(int cellX, int cellZ, float cellSize, float heightScale)
    {
        cellSize = MathF.Max(1f, cellSize);
        return new Vector3(
            cellX * cellSize + cellSize * 0.5f,
            MathF.Max(0f, heightScale) * 0.5f,
            cellZ * cellSize + cellSize * 0.5f);
    }

    public static float ComputeConservativeCellRadius(float cellSize, float heightScale)
    {
        cellSize = MathF.Max(1f, cellSize);
        float vertical = MathF.Max(MathF.Max(0f, heightScale), cellSize) + 16f;
        return MathF.Sqrt(cellSize * cellSize * 0.5f + vertical * vertical);
    }
}
