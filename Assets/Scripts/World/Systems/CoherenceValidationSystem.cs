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

        // Passes progressives Sprint B — biais de relief avant les corrections extrêmes
        if (coherence.rugosity > 0.4f)
            ApplyRugosityBias(cells, coherence);

        if (coherence.accumulationIndex > 0.5f)
            ApplyAccumulationBias(cells, coherence);

        if (coherence.isExtremeOcean)
            EnsureOpenWaterRegion(cells, ctx);

        if (coherence.isExtremeArid)
            EnsureAridRegion(cells, ctx);

        if (coherence.isExtremeFrozen)
            EnsureFrozenRegion(cells, ctx);
    }

    /// <summary>
    /// Biais de rugosité : rehausse légèrement les cellules rocheuses et limite
    /// le waterRatio dans les zones montagneuses (drainage actif).
    /// </summary>
    private static void ApplyRugosityBias(HexCell[] cells, MapRegion.CoherenceConstraint coherence)
    {
        float strength = Mathf.InverseLerp(0.4f, 1f, coherence.rugosity);
        foreach (HexCell cell in cells)
        {
            if (cell.state.waterClassification == WaterClassification.OpenOcean ||
                cell.state.waterClassification == WaterClassification.InlandWater)
                continue;

            HexPhysicalState state = cell.state;
            // Rehausse l'altitude en zones sèches / rocheuses
            if (state.waterRatio < 0.3f)
                state.altitude = Mathf.Clamp01(state.altitude + strength * 0.15f);

            // Limite le waterRatio progressivement sur les zones hautes
            if (state.altitude > 0.55f)
                state.waterRatio = Mathf.Min(state.waterRatio, Mathf.Lerp(state.waterRatio, 0.1f, strength * 0.5f));

            cell.state = state;
        }
    }

    /// <summary>
    /// Biais d'accumulation : favorise les bassins et l'eau intérieure dans les zones basses
    /// quand l'indice d'accumulation hydrique est fort.
    /// </summary>
    private static void ApplyAccumulationBias(HexCell[] cells, MapRegion.CoherenceConstraint coherence)
    {
        float strength = Mathf.InverseLerp(0.5f, 1f, coherence.accumulationIndex);
        foreach (HexCell cell in cells)
        {
            if (cell.state.waterClassification == WaterClassification.Dry ||
                cell.state.waterClassification == WaterClassification.FrozenWater)
                continue;

            HexPhysicalState state = cell.state;
            // Dans les zones basses humides, renforcer la classification bassin/inland
            if (state.altitude < 0.35f && state.waterRatio > 0.25f)
            {
                state.waterRatio = Mathf.Clamp01(state.waterRatio + strength * 0.1f);
                if (state.waterClassification == WaterClassification.Coast)
                    state.waterClassification = WaterClassification.InlandWater;
            }

            cell.state = state;
        }
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