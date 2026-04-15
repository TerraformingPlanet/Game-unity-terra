using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Passe hydrologique de relief.
///
/// Détermine pour chaque hex son voisin aval, calcule une accumulation simple
/// depuis les hauteurs vers les points bas et assigne une classe topographique.
/// Cette première version pose le champ d'écoulement nécessaire aux passes futures
/// (eau intérieure, côtes, rivières et bassins plus crédibles).
/// </summary>
public class HydrologySystem : IHexSystem
{
    private const float RidgeAltitudeThreshold = 0.65f;
    private const float ChannelAccumulationThreshold = 4f;
    private const float SourceWaterThreshold = 0.55f;
    private const float DownhillEpsilon = 0.0001f;
    private const float OverflowTolerance = 0.02f;

    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        if (cells == null || cells.Length == 0)
            return;

        List<HexCell> sortedCells = new List<HexCell>(cells);
        sortedCells.Sort((left, right) => right.state.altitude.CompareTo(left.state.altitude));

        foreach (HexCell cell in sortedCells)
        {
            ResetHydrologyState(cell);
            HexCell downstream = FindDownstream(cell, ctx);

            if (downstream == null)
            {
                AssignOverflowOutlet(cell, ctx);
                cell.state.terrainClass = TerrainClass.Basin;
                continue;
            }

            cell.state.hasDownstream = true;
            cell.state.downstreamQ = downstream.Q;
            cell.state.downstreamR = downstream.R;
        }

        foreach (HexCell cell in sortedCells)
        {
            if (!cell.state.hasDownstream)
                continue;

            if (!ctx.cellLookup.TryGetValue((cell.state.downstreamQ, cell.state.downstreamR), out HexCell downstream))
                continue;

            HexPhysicalState downstreamState = downstream.state;
            downstreamState.flowAccumulation += cell.state.flowAccumulation;
            downstream.state = downstreamState;
        }

        foreach (HexCell cell in cells)
            ClassifyTerrain(cell, ctx);
    }

    private static void ResetHydrologyState(HexCell cell)
    {
        HexPhysicalState state = cell.state;
        state.flowAccumulation = 1;
        state.terrainClass = TerrainClass.Slope;
        state.waterClassification = WaterClassification.Dry;
        state.hasDownstream = false;
        state.downstreamQ = cell.Q;
        state.downstreamR = cell.R;
        state.hasOverflowOutlet = false;
        state.overflowQ = cell.Q;
        state.overflowR = cell.R;
        cell.state = state;
    }

    private static HexCell FindDownstream(HexCell cell, GenerationContext ctx)
    {
        HexCell bestNeighbor = null;
        float bestAltitude = cell.state.altitude;

        foreach (HexCell neighbor in ctx.GetNeighbors(cell))
        {
            if (neighbor.state.altitude < bestAltitude - DownhillEpsilon)
            {
                bestAltitude = neighbor.state.altitude;
                bestNeighbor = neighbor;
            }
        }

        return bestNeighbor;
    }

    private static void ClassifyTerrain(HexCell cell, GenerationContext ctx)
    {
        HexPhysicalState state = cell.state;

        if (!state.hasDownstream)
        {
            state.terrainClass = TerrainClass.Basin;
        }
        else if (state.flowAccumulation >= ChannelAccumulationThreshold)
        {
            state.terrainClass = TerrainClass.Channel;
        }
        else if (state.altitude >= RidgeAltitudeThreshold && state.flowAccumulation <= 1)
        {
            state.terrainClass = TerrainClass.Ridge;
        }
        else if (state.waterRatio >= SourceWaterThreshold && HasLowerNeighbor(cell, ctx))
        {
            state.terrainClass = TerrainClass.Source;
        }
        else
        {
            state.terrainClass = TerrainClass.Slope;
        }

        cell.state = state;
    }

    private static void AssignOverflowOutlet(HexCell cell, GenerationContext ctx)
    {
        HexCell outlet = FindOverflowOutlet(cell, ctx);
        if (outlet == null)
            return;

        HexPhysicalState state = cell.state;
        state.hasOverflowOutlet = true;
        state.overflowQ = outlet.Q;
        state.overflowR = outlet.R;
        cell.state = state;
    }

    private static HexCell FindOverflowOutlet(HexCell cell, GenerationContext ctx)
    {
        HexCell bestNeighbor = null;
        float bestRimAltitude = float.MaxValue;

        foreach (HexCell neighbor in ctx.GetNeighbors(cell))
        {
            float altitudeDelta = neighbor.state.altitude - cell.state.altitude;
            if (altitudeDelta < -DownhillEpsilon)
                continue;

            if (altitudeDelta <= OverflowTolerance && neighbor.state.altitude < bestRimAltitude)
            {
                bestRimAltitude = neighbor.state.altitude;
                bestNeighbor = neighbor;
            }
        }

        if (bestNeighbor != null)
            return bestNeighbor;

        foreach (HexCell neighbor in ctx.GetNeighbors(cell))
        {
            if (neighbor.state.altitude < bestRimAltitude)
            {
                bestRimAltitude = neighbor.state.altitude;
                bestNeighbor = neighbor;
            }
        }

        return bestNeighbor;
    }

    private static bool HasLowerNeighbor(HexCell cell, GenerationContext ctx)
    {
        foreach (HexCell neighbor in ctx.GetNeighbors(cell))
        {
            if (neighbor.state.altitude < cell.state.altitude - DownhillEpsilon)
                return true;
        }

        return false;
    }
}