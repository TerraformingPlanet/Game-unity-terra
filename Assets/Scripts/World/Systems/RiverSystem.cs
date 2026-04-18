using UnityEngine;

/// <summary>
/// Passe 7 du pipeline — génération des rivières par propagation de flux.
///
/// Lit  : cell.state.altitude, cell.state.waterRatio, ctx.GetNeighbors()
/// Écrit: cell.state.hasRiver
///
/// Algorithme :
///   1. Identification des sources : hex avec waterRatio > seuil ET pente nette vers un voisin bas
///   2. Propagation : à partir de chaque source, descendre vers le voisin le plus bas
///      jusqu'à atteindre la mer (waterRatio ≥ 1 dans la zone Eau/Ocean) ou une dépression
///   3. Stop si la rivière atteint un hex en ombre pluviométrique intense (assèchement)
///
/// Le champ hasRiver est ensuite lu par BiomeSystem (peut surcharger le biome)
/// et par le mesh renderer pour afficher visuellement la rivière.
/// </summary>
public class RiverSystem : IHexSystem
{
    // Un hex est candidat source si son waterRatio dépasse ce seuil
    private const float SourceWaterThreshold = 0.55f;

    // La rivière s'arrête si le hex de destination a waterRatio > 0.9 (mer atteinte)
    private const float OceanWaterThreshold = 0.9f;

    // Nombre max de pas de propagation par rivière (évite les boucles infinies)
    private const int MaxRiverLength = 64;

    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        // Réinitialiser
        foreach (HexCell cell in cells)
            cell.state.hasRiver = false;

        foreach (HexCell cell in cells)
        {
            if (!IsRiverSource(cell, ctx)) continue;

            PropagateRiver(cell, ctx);
        }
    }

    // =========================================================
    // Identification de la source
    // =========================================================

    private static bool IsRiverSource(HexCell cell, GenerationContext ctx)
    {
        MapRegion.CoherenceConstraint coherence = ctx.coherence;
        float sourceThreshold = ComputeSourceThreshold(coherence, ctx.genParams);

        if (cell.state.waterRatio < sourceThreshold)
            return false;

        if (cell.state.terrainClass == TerrainClass.Basin)
            return false;

        if (coherence.isExtremeArid && cell.state.waterRatio < 0.85f)
            return false;

        if (coherence.deserticity > 0.8f &&
            cell.state.waterRatio < 0.7f &&
            cell.state.terrainClass != TerrainClass.Source &&
            cell.state.terrainClass != TerrainClass.Channel)
        {
            return false;
        }

        if (coherence.frigidity > 0.75f &&
            cell.state.tempLocale < -12f &&
            cell.state.waterClassification == WaterClassification.FrozenWater)
        {
            return false;
        }

        if (cell.state.terrainClass == TerrainClass.Source || cell.state.terrainClass == TerrainClass.Channel)
            return cell.state.hasDownstream;

        if (cell.state.flowAccumulation <= 1 && coherence.deserticity > 0.55f)
            return false;

        // Doit avoir au moins un voisin plus bas (pente réelle)
        foreach (HexCell nb in ctx.GetNeighbors(cell))
        {
            if (nb.state.altitude < cell.state.altitude - 0.05f)
                return true;
        }
        return false;
    }

    // =========================================================
    // Propagation descendante
    // =========================================================

    private static void PropagateRiver(HexCell start, GenerationContext ctx)
    {
        HexCell current = start;
        MapRegion.CoherenceConstraint coherence = ctx.coherence;

        for (int step = 0; step < MaxRiverLength; step++)
        {
            current.state.hasRiver = true;

            // Mer atteinte → arrêt
            if (current.state.waterRatio >= OceanWaterThreshold && step > 0)
                break;

            HexCell next = GetDownstream(current, ctx);

            // Dépression sans sortie ou voisin introuvable → arrêt
            if (next == null || next.state.altitude >= current.state.altitude)
                break;

            if (!IsPlausibleRiverStep(current, next, coherence, ctx.genParams))
                break;

            // La rivière assèche les hexes en ombre pluviométrique intense
            if (next.state.rainShadow && next.state.waterRatio < 0.1f)
                break;

            current = next;
        }
    }

    private static HexCell GetDownstream(HexCell cell, GenerationContext ctx)
    {
        if (!cell.state.hasDownstream)
            return null;

        ctx.cellLookup.TryGetValue((cell.state.downstreamQ, cell.state.downstreamR), out HexCell downstream);
        return downstream;
    }

    private static float ComputeSourceThreshold(MapRegion.CoherenceConstraint coherence, MapGenParameters parameters)
    {
        float climateThreshold = SourceWaterThreshold;
        climateThreshold = Mathf.Lerp(climateThreshold, 0.85f, coherence.deserticity);
        climateThreshold = Mathf.Lerp(climateThreshold, 0.45f, coherence.oceanicity * 0.35f);
        climateThreshold = Mathf.Lerp(climateThreshold, 0.7f, coherence.frigidity * 0.25f);
        return Mathf.Lerp(SourceWaterThreshold, climateThreshold, parameters.coherenceRiverBias);
    }

    private static bool IsPlausibleRiverStep(HexCell current,
                                             HexCell next,
                                             MapRegion.CoherenceConstraint coherence,
                                             MapGenParameters parameters)
    {
        float strength = parameters.coherenceRiverBias;
        float minChannelWater = Mathf.Lerp(0.03f, 0.14f, strength);

        if (coherence.deserticity > 0.7f &&
            next.state.waterClassification == WaterClassification.Dry &&
            next.state.waterRatio < minChannelWater)
        {
            return false;
        }

        if (coherence.frigidity > 0.7f &&
            next.state.waterClassification == WaterClassification.FrozenWater &&
            next.state.tempLocale < -10f)
        {
            return false;
        }

        if (coherence.isExtremeArid && next.state.flowAccumulation < 3 && next.state.waterRatio < 0.2f)
            return false;

        if (current.state.flowAccumulation > 0 &&
            next.state.flowAccumulation + 2 < current.state.flowAccumulation &&
            next.state.waterClassification == WaterClassification.Dry)
        {
            return false;
        }

        return true;
    }
}
