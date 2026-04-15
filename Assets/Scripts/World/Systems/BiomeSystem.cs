using UnityEngine;

/// <summary>
/// Passe 6 du pipeline — sélection du biome par système de scoring.
///
/// Lit  : cell.state (altitude, tempLocale, waterRatio, toxinLevel, soil)
///        cell.layer, ctx.body, ctx.biomeOffset
/// Écrit: cell.terrain (TerrainData)
///
/// Pour chaque hex, un score est calculé pour chaque TerrainType possible.
/// Le TerrainType gagnant (score max) est ensuite résolu en TerrainData depuis
/// le pool de biomes de la LayerZone correspondante (CelestialBodyData.GetLayerForHeight).
///
/// Scoring par TerrainType :
///   Glace           : temp basse + eau présente
///   Eau             : waterRatio élevé + temp > seuil gel
///   Vegetation      : temp tempérée + eau modérée + matière organique possible
///   Roche           : altitude élevée OU sol très dur OU peu d'eau
///   Metal           : forte concentration minérale
///   AtmosphereToxique : toxines élevées
///
/// Extension : ajouter des biomes dans TerrainType suffit à étendre ce système.
/// Ajouter des propriétés de seuil dans TerrainData permettra un scoring data-driven futur.
/// </summary>
public class BiomeSystem : IHexSystem
{
    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        foreach (HexCell cell in cells)
        {
            LayerZone zone = ctx.body.GetLayerForHeight(cell.state.altitude);
            if (zone == null || zone.biomes == null || zone.biomes.Length == 0)
                continue;

            TerrainType winner = ScoreBiome(cell.state);
            cell.terrain = ResolveFromPool(zone, winner);
        }
    }

    // =========================================================
    // Scoring
    // =========================================================

    private static TerrainType ScoreBiome(HexPhysicalState s)
    {
        float temp   = s.tempLocale;
        float water  = s.waterRatio;
        float toxin  = s.toxinLevel;
        float alt    = s.altitude;
        float hard   = s.soil.rockHardness;
        float mineral = s.soil.mineralDensity;

        // Calcul des scores (plages normalisées [0–1])
        float scoreGlace    = ScoreGlace(temp, water);
        float scoreEau      = ScoreEau(temp, water);
        float scoreVeg      = ScoreVeg(temp, water, hard);
        float scoreRoche    = ScoreRoche(alt, water, hard);
        float scoreMetal    = ScoreMetal(mineral, hard);
        float scoreToxic    = ScoreToxic(toxin);

        // Priorité absolue : toxines très élevées → AtmosphereToxique
        if (scoreToxic >= 0.8f) return TerrainType.AtmosphereToxique;

        // Picker du score max
        TerrainType winner  = TerrainType.Roche;
        float       best    = scoreRoche;

        if (scoreGlace > best) { best = scoreGlace; winner = TerrainType.Glace; }
        if (scoreEau   > best) { best = scoreEau;   winner = TerrainType.Eau; }
        if (scoreVeg   > best) { best = scoreVeg;   winner = TerrainType.Vegetation; }
        if (scoreMetal > best) { best = scoreMetal; winner = TerrainType.Metal; }
        if (scoreToxic > best) {                    winner = TerrainType.AtmosphereToxique; }

        return winner;
    }

    // --- Fonctions de score individuelles ---

    private static float ScoreGlace(float temp, float water)
    {
        float coldScore  = Mathf.InverseLerp(0f, -50f, temp);   // max score à -50°C
        float waterScore = Mathf.Clamp01(water * 1.5f);         // eau nécessaire pour la glace
        return coldScore * waterScore;
    }

    private static float ScoreEau(float temp, float water)
    {
        float notFrozen  = Mathf.InverseLerp(-10f, 5f, temp);   // favorisé au-dessus de 5°C
        float notBoiling = Mathf.InverseLerp(120f, 80f, temp);  // réduit si > 80°C
        return water * notFrozen * notBoiling;
    }

    private static float ScoreVeg(float temp, float water, float rockHardness)
    {
        float tempScore  = TriangleScore(temp, -10f, 20f, 50f); // idéal 20°C, impossible < -10 ou > 50
        float waterScore = TriangleScore(water, 0.1f, 0.4f, 0.9f);
        float softSoil   = 1f - rockHardness * 0.4f;            // sol très dur pénalise légèrement
        return tempScore * waterScore * softSoil;
    }

    private static float ScoreRoche(float altitude, float water, float rockHardness)
    {
        float altScore   = Mathf.Clamp01(altitude * 1.5f - 0.2f);
        float dryScore   = 1f - water;
        float hardScore  = rockHardness;
        return Mathf.Max(altScore, dryScore * 0.6f) * hardScore;
    }

    private static float ScoreMetal(float mineralDensity, float rockHardness)
    {
        // Métal nécessite une forte concentration minérale ET un sol dur
        return Mathf.Clamp01(mineralDensity * rockHardness * 1.5f - 0.3f);
    }

    private static float ScoreToxic(float toxinLevel)
    {
        // Score exponentiel : les faibles toxines ne comptent pas
        return Mathf.Clamp01((toxinLevel - 0.35f) / 0.65f);
    }

    /// <summary>Score en cloche : monte de minVal à peakVal, descend jusqu'à maxVal.</summary>
    private static float TriangleScore(float value, float minVal, float peakVal, float maxVal)
    {
        if (value <= minVal || value >= maxVal) return 0f;
        if (value <= peakVal) return Mathf.InverseLerp(minVal, peakVal, value);
        return Mathf.InverseLerp(maxVal, peakVal, value);
    }

    // =========================================================
    // Résolution TerrainType → TerrainData depuis le pool de zone
    // =========================================================

    private static TerrainData ResolveFromPool(LayerZone zone, TerrainType target)
    {
        // Cherche d'abord une TerrainData correspondant exactement au type cible
        foreach (TerrainData td in zone.biomes)
        {
            if (td != null && td.terrainType == target)
                return td;
        }

        // Fallback : premier élément non null du pool
        foreach (TerrainData td in zone.biomes)
        {
            if (td != null) return td;
        }

        return null;
    }
}
