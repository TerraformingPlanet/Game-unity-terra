using UnityEngine;

/// <summary>
/// Passe de cohérence macro → micro. Vérifie que la région locale reste compatible
/// avec la case cliquée sur la projection et corrige seulement les cas extrêmes.
/// </summary>
public class CoherenceValidationSystem : IHexSystem
{
    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        if (cells == null || cells.Length == 0)
            return;

        MapRegion.CoherenceConstraint coherence = ctx.coherence;

        if (coherence.isExtremeOcean)
            EnsureOpenWaterRegion(cells, ctx);

        if (coherence.isExtremeArid)
            EnsureAridRegion(cells, ctx);

        if (coherence.isExtremeFrozen)
            EnsureFrozenRegion(cells, ctx);
    }

    private static void EnsureOpenWaterRegion(HexCell[] cells, GenerationContext ctx)
    {
        foreach (HexCell cell in cells)
        {
            HexPhysicalState state = cell.state;
            state.waterRatio = 1f;
            state.altitude = Mathf.Min(state.altitude, 0.1f);
            state.tempLocale = Mathf.Max(state.tempLocale, -4f);
            state.hasRiver = false;
            state.rainShadow = false;
            state.waterClassification = WaterClassification.OpenOcean;
            state.terrainClass = TerrainClass.Basin;
            state.soil.organicContent = Mathf.Min(state.soil.organicContent, 0.05f);
            cell.state = state;
        }
    }

    private static void EnsureAridRegion(HexCell[] cells, GenerationContext ctx)
    {
        int maxWaterCells = Mathf.FloorToInt(cells.Length * 0.08f);
        int waterCount = CountWetCells(cells);
        if (waterCount <= maxWaterCells)
            return;

        foreach (HexCell cell in cells)
        {
            if (waterCount <= maxWaterCells)
                break;

            if (!IsWetCell(cell))
                continue;

            HexPhysicalState state = cell.state;
            state.waterRatio = Mathf.Min(state.waterRatio, 0.03f);
            state.hasRiver = false;
            state.waterClassification = WaterClassification.Dry;
            cell.state = state;
            waterCount--;
        }
    }

    private static void EnsureFrozenRegion(HexCell[] cells, GenerationContext ctx)
    {
        int iceCount = CountFrozenCells(cells);
        int targetCount = Mathf.CeilToInt(cells.Length * 0.6f);
        if (iceCount >= targetCount)
            return;

        foreach (HexCell cell in cells)
        {
            if (iceCount >= targetCount)
                break;

            if (cell.state.waterClassification == WaterClassification.FrozenWater)
                continue;

            HexPhysicalState state = cell.state;
            state.tempLocale = Mathf.Min(state.tempLocale, -20f);
            state.waterRatio = Mathf.Max(state.waterRatio, 0.65f);
            state.hasRiver = false;
            state.waterClassification = WaterClassification.FrozenWater;
            cell.state = state;
            iceCount++;
        }
    }

    private static int CountWetCells(HexCell[] cells)
    {
        int count = 0;
        foreach (HexCell cell in cells)
        {
            if (IsWetCell(cell))
                count++;
        }
        return count;
    }

    private static int CountFrozenCells(HexCell[] cells)
    {
        int count = 0;
        foreach (HexCell cell in cells)
        {
            if (cell.state.waterClassification == WaterClassification.FrozenWater ||
                (cell.state.tempLocale < -15f && cell.state.waterRatio >= 0.5f))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsWetCell(HexCell cell)
    {
        return cell.state.waterClassification == WaterClassification.OpenOcean
            || cell.state.waterClassification == WaterClassification.InlandWater
            || cell.state.waterClassification == WaterClassification.FrozenWater
            || cell.state.waterRatio >= 0.6f;
    }
}