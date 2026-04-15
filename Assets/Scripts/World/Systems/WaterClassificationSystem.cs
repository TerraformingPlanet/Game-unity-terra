using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Classe explicitement l'eau locale après la cohérence macro → micro.
///
/// Cette passe distingue les masses d'eau ouvertes, l'eau intérieure, les côtes
/// et l'eau gelée pour que les biomes et le HUD puissent réagir à un état lisible.
/// </summary>
public class WaterClassificationSystem : IHexSystem
{
    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        if (cells == null || cells.Length == 0)
            return;

        MapGenParameters parameters = ctx.genParams;
        float coastMin = Mathf.Min(parameters.coastalWaterThreshold.x, parameters.coastalWaterThreshold.y);
        float coastMax = Mathf.Max(parameters.coastalWaterThreshold.x, parameters.coastalWaterThreshold.y);
        HashSet<HexCell> connectedOpenWater = ResolveConnectedOpenWater(cells, ctx, parameters);

        foreach (HexCell cell in cells)
        {
            HexPhysicalState state = cell.state;
            state.waterClassification = ClassifyPrimary(cell, ctx, parameters, connectedOpenWater.Contains(cell));
            cell.state = state;
        }

        foreach (HexCell cell in cells)
        {
            HexPhysicalState state = cell.state;
            if (state.waterClassification == WaterClassification.Dry &&
                state.waterRatio >= coastMin &&
                state.waterRatio <= coastMax &&
                HasNeighboringOpenWater(cell, ctx) &&
                HasNeighboringDryLand(cell, ctx))
            {
                state.waterClassification = WaterClassification.Coast;
            }

            cell.state = state;
        }
    }

    private static WaterClassification ClassifyPrimary(HexCell cell,
                                                       GenerationContext ctx,
                                                       MapGenParameters parameters,
                                                       bool isConnectedToOpenBoundary)
    {
        HexPhysicalState state = cell.state;

        bool isFrozenWater = state.tempLocale <= -10f && state.waterRatio >= 0.4f;

        if (ctx.coherence.isExtremeOcean)
            return WaterClassification.OpenOcean;

        if (isFrozenWater)
            return WaterClassification.FrozenWater;

        if (isConnectedToOpenBoundary)
            return WaterClassification.OpenOcean;

        if (state.terrainClass == TerrainClass.Basin && state.waterRatio >= parameters.lakeWaterThreshold)
            return WaterClassification.InlandWater;

        if ((state.terrainClass == TerrainClass.Channel || state.terrainClass == TerrainClass.Source) && state.waterRatio >= parameters.lakeWaterThreshold * 0.8f)
            return WaterClassification.InlandWater;

        if (state.waterRatio >= 0.92f)
            return WaterClassification.InlandWater;

        return WaterClassification.Dry;
    }

    private static HashSet<HexCell> ResolveConnectedOpenWater(HexCell[] cells, GenerationContext ctx, MapGenParameters parameters)
    {
        var openWater = new HashSet<HexCell>();
        var queue = new Queue<HexCell>();

        foreach (HexCell cell in cells)
        {
            if (!IsOpenWaterCandidate(cell, parameters))
                continue;

            bool isBoundary = ctx.GetNeighbors(cell).Length < 6;
            if (!ctx.coherence.isExtremeOcean && !isBoundary)
                continue;

            if (openWater.Add(cell))
                queue.Enqueue(cell);
        }

        while (queue.Count > 0)
        {
            HexCell current = queue.Dequeue();
            foreach (HexCell neighbor in ctx.GetNeighbors(current))
            {
                if (!IsOpenWaterCandidate(neighbor, parameters))
                    continue;

                if (openWater.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return openWater;
    }

    private static bool IsOpenWaterCandidate(HexCell cell, MapGenParameters parameters)
    {
        if (cell.state.waterRatio >= 0.92f)
            return true;

        if (cell.state.terrainClass == TerrainClass.Basin && cell.state.waterRatio >= parameters.lakeWaterThreshold)
            return true;

        return false;
    }

    private static bool HasNeighboringOpenWater(HexCell cell, GenerationContext ctx)
    {
        foreach (HexCell neighbor in ctx.GetNeighbors(cell))
        {
            WaterClassification classification = neighbor.state.waterClassification;
            if (classification == WaterClassification.OpenOcean ||
                classification == WaterClassification.FrozenWater)
                return true;
        }

        return false;
    }

    private static bool HasNeighboringDryLand(HexCell cell, GenerationContext ctx)
    {
        foreach (HexCell neighbor in ctx.GetNeighbors(cell))
        {
            if (neighbor.state.waterClassification == WaterClassification.Dry &&
                neighbor.state.terrainClass != TerrainClass.Basin)
            {
                return true;
            }
        }

        return false;
    }
}