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
        if (cell.state.waterRatio < SourceWaterThreshold) return false;

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

        for (int step = 0; step < MaxRiverLength; step++)
        {
            current.state.hasRiver = true;

            // Mer atteinte → arrêt
            if (current.state.waterRatio >= OceanWaterThreshold && step > 0)
                break;

            HexCell next = LowestNeighbor(current, ctx);

            // Dépression sans sortie ou voisin introuvable → arrêt
            if (next == null || next.state.altitude >= current.state.altitude)
                break;

            // La rivière assèche les hexes en ombre pluviométrique intense
            if (next.state.rainShadow && next.state.waterRatio < 0.1f)
                break;

            current = next;
        }
    }

    private static HexCell LowestNeighbor(HexCell cell, GenerationContext ctx)
    {
        HexCell lowest    = null;
        float   lowestAlt = cell.state.altitude;

        foreach (HexCell nb in ctx.GetNeighbors(cell))
        {
            if (nb.state.altitude < lowestAlt)
            {
                lowestAlt = nb.state.altitude;
                lowest    = nb;
            }
        }
        return lowest;
    }
}
