using UnityEngine;

/// <summary>
/// Passe 3 du pipeline — calcul du ratio d'eau et de l'ombre pluviométrique.
///
/// Lit  : cell.state.altitude, cell.state.tempLocale, ctx.weather, ctx.body.geology/atmosphere
/// Écrit: cell.state.waterRatio, cell.state.rainShadow
///
/// Phase A — ratio initial par hex :
///   waterRatio = waterAbundance × biomeNoise × atmosphereDensity
///   Modulé par gel (< −20°C) et évaporation (> 80°C)
///
/// Phase B — accumulation topographique :
///   Pour chaque hex, les voisins plus hauts transfèrent une partie de leur eau
///   (ruissellement de pente). Nécessite ctx.cellLookup.
///
/// Phase C — ombre pluviométrique :
///   Un hex est en ombre pluviométrique si le vent dominant arrive d'un relief
///   plus élevé. Simplifié ici : hex sous le vent d'un voisin haute altitude.
/// </summary>
public class WaterSystem : IHexSystem
{
    // Fraction d'eau transférée par unité d'écart d'altitude entre deux voisins
    private const float RunoffFactor = 0.18f;
    private const float BasinOverflowSurfaceScale = 0.35f;
    private const float BasinOverflowFactor = 0.45f;

    // Seuil d'altitude pour générer une ombre pluviométrique
    private const float RainShadowAltitudeThreshold = 0.6f;

    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        MapGenParameters p  = ctx.genParams;
        float            ox = ctx.biomeOffset.x;
        float            oz = ctx.biomeOffset.y;
        MapRegion.CoherenceConstraint coherence = ctx.coherence;
        ExecutePhaseA(cells, ctx, p, ox, oz, coherence);
        ExecutePhaseB(cells, ctx, p, coherence);
        ExecutePhaseC(cells, ctx, p, ctx.weather.prevailingWindDir.normalized, coherence);
    }

    private void ExecutePhaseA(HexCell[] cells, GenerationContext ctx, MapGenParameters p,
                               float ox, float oz, MapRegion.CoherenceConstraint coherence)
    {
        foreach (HexCell cell in cells)
        {
            float bx = cell.center.x / p.biomeScale + ox;
            float bz = cell.center.z / p.biomeScale + oz;
            float biomeNoise = GenerationContext.FractalNoise(bx, bz, p.octaves, p.persistence, p.lacunarity);

            float w = ctx.body.geology.waterAbundance
                    * biomeNoise
                    * ctx.body.atmosphere.density;

            float temp = cell.state.tempLocale;

            // Gel : eau présente sous forme de glace (réduit la mobilité)
            if (temp < -20f) w *= 0.5f;
            // Évaporation thermique intense
            if (temp > 80f)  w *= Mathf.Lerp(1f, 0f, (temp - 80f) / 120f);

            // Précipitations ajoutées par la météo régionale
            w += ctx.weather.precipitationRate * 0.2f;

            w = ApplyCoherenceBias(w, temp, coherence, p);

            cell.state.waterRatio = Mathf.Clamp01(w);
            cell.state.rainShadow = false;
        }
    }

    private void ExecutePhaseB(HexCell[] cells, GenerationContext ctx, MapGenParameters p,
                               MapRegion.CoherenceConstraint coherence)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (HexCell cell in cells)
            {
                if (cell.state.terrainClass == TerrainClass.Basin)
                {
                    ApplyBasinRetention(cell, ctx, p, coherence);
                    continue;
                }
                ProcessCellRunoff(cell, ctx, p, coherence);
            }
        }

        foreach (HexCell cell in cells)
        {
            if (cell.state.terrainClass == TerrainClass.Basin)
                ApplyBasinRetention(cell, ctx, p, coherence);
        }
    }

    private static void ProcessCellRunoff(HexCell cell, GenerationContext ctx,
                                          MapGenParameters p, MapRegion.CoherenceConstraint coherence)
    {
        if (cell.state.hasDownstream &&
            ctx.cellLookup.TryGetValue((cell.state.downstreamQ, cell.state.downstreamR), out HexCell downstream))
        {
            float retainedWater = ComputeRetainedWater(cell, p, coherence);
            float availableWater = Mathf.Max(0f, cell.state.waterRatio - retainedWater);
            float altDiff = cell.state.altitude - downstream.state.altitude;

            if (altDiff > 0f && availableWater > 0f)
            {
                float runoffMultiplier = ComputeRunoffMultiplier(coherence, p);
                float flow = altDiff * RunoffFactor * runoffMultiplier * availableWater;
                flow = Mathf.Min(flow, availableWater * 0.5f);

                cell.state.waterRatio -= flow;
                downstream.state.waterRatio += flow;
            }

            cell.state.waterRatio = ApplyCoherencePostFlow(cell.state.waterRatio, coherence, p);
            return;
        }

        HexCell[] neighbors = ctx.GetNeighbors(cell);
        foreach (HexCell nb in neighbors)
        {
            float altDiff = cell.state.altitude - nb.state.altitude;
            if (altDiff > 0f)
            {
                float runoffMultiplier = ComputeRunoffMultiplier(coherence, p);
                // Eau qui s'écoule du hex courant vers son voisin plus bas
                float flow = altDiff * RunoffFactor * runoffMultiplier * cell.state.waterRatio
                           * cell.state.soil.porosity; // infiltration réduit le flux
                flow = Mathf.Min(flow, cell.state.waterRatio * 0.3f); // cap

                cell.state.waterRatio -= flow;
                nb.state.waterRatio   += flow;
            }
        }
        cell.state.waterRatio = ApplyCoherencePostFlow(cell.state.waterRatio, coherence, p);
    }

    private static void ExecutePhaseC(HexCell[] cells, GenerationContext ctx, MapGenParameters p,
                                      Vector2 windDir, MapRegion.CoherenceConstraint coherence)
    {
        foreach (HexCell cell in cells)
        {
            HexCell[] neighbors = ctx.GetNeighbors(cell);
            foreach (HexCell nb in neighbors)
            {
                // Vecteur du voisin vers le hex courant
                Vector2 toCell = new Vector2(
                    cell.center.x - nb.center.x,
                    cell.center.z - nb.center.z).normalized;

                // Le voisin est "au vent" si le vent souffle de lui vers le hex courant
                float alignment = Vector2.Dot(windDir, toCell);
                if (alignment > 0.5f && nb.state.altitude > RainShadowAltitudeThreshold)
                {
                    cell.state.rainShadow = true;
                    cell.state.waterRatio = Mathf.Max(0f, cell.state.waterRatio - 0.25f);
                    break;
                }
            }

            cell.state.waterRatio = ApplyCoherencePostFlow(cell.state.waterRatio, coherence, p);
        }
    }

    private static void ApplyBasinRetention(HexCell cell,
                                            GenerationContext ctx,
                                            MapGenParameters parameters,
                                            MapRegion.CoherenceConstraint coherence)
    {
        float basinFloor = ComputeBasinFloor(cell, parameters, coherence);
        cell.state.waterRatio = Mathf.Max(cell.state.waterRatio, basinFloor);

        if (cell.state.hasOverflowOutlet &&
            cell.state.waterRatio > parameters.lakeWaterThreshold &&
            ctx.cellLookup.TryGetValue((cell.state.overflowQ, cell.state.overflowR), out HexCell overflowOutlet))
        {
            float waterSurface = cell.state.altitude + Mathf.Max(0f, cell.state.waterRatio - parameters.basinCapacity) * BasinOverflowSurfaceScale;
            float spillThreshold = overflowOutlet.state.altitude;

            if (waterSurface > spillThreshold)
            {
                float surfaceExcess = waterSurface - spillThreshold;
                float waterExcess = Mathf.Max(0f, cell.state.waterRatio - parameters.lakeWaterThreshold);
                float overflow = surfaceExcess * BasinOverflowFactor + waterExcess * 0.5f;
                overflow = Mathf.Min(overflow, waterExcess);

                if (overflow > 0f)
                {
                    cell.state.waterRatio -= overflow;
                    overflowOutlet.state.waterRatio += overflow;
                }
            }
        }

        cell.state.waterRatio = ApplyCoherencePostFlow(cell.state.waterRatio, coherence, parameters);
    }

    private static float ComputeBasinFloor(HexCell cell, MapGenParameters parameters,
                                           MapRegion.CoherenceConstraint coherence)
    {
        float accumulationFactor = Mathf.InverseLerp(1f, 10f, cell.state.flowAccumulation);
        float basinFloor = Mathf.Lerp(parameters.lakeWaterThreshold * 0.35f, parameters.basinCapacity, accumulationFactor);
        float retentionMultiplier = ComputeRetentionMultiplier(coherence, parameters);
        basinFloor *= retentionMultiplier;

        if (coherence.isExtremeOcean)
            basinFloor = Mathf.Max(basinFloor, parameters.basinCapacity);
        else if (coherence.isExtremeArid)
            basinFloor *= 0.2f;
        else
        {
            float projectedFloor = Mathf.Lerp(parameters.lakeWaterThreshold * 0.1f,
                                              parameters.basinCapacity,
                                              Mathf.Clamp01(coherence.projectedWaterRatio));
            float climateFloor = Mathf.Lerp(projectedFloor * 0.4f,
                                            projectedFloor,
                                            Mathf.Clamp01((coherence.oceanicity + coherence.frigidity * 0.5f) - coherence.deserticity * 0.35f));
            basinFloor = Mathf.Max(basinFloor * 0.45f, climateFloor);
        }

        return Mathf.Clamp01(basinFloor);
    }

    private static float ComputeRetainedWater(HexCell cell,
                                              MapGenParameters parameters,
                                              MapRegion.CoherenceConstraint coherence)
    {
        float porosityRetention = Mathf.Lerp(0.05f, 0.25f, cell.state.soil.porosity);
        float retentionMultiplier = ComputeRetentionMultiplier(coherence, parameters);

        float retained = cell.state.terrainClass switch
        {
            TerrainClass.Source => Mathf.Max(parameters.basinCapacity * 0.35f, porosityRetention),
            TerrainClass.Channel => Mathf.Max(parameters.basinCapacity * 0.2f, porosityRetention * 0.6f),
            TerrainClass.Ridge => porosityRetention * 0.35f,
            _ => porosityRetention,
        };

        return Mathf.Clamp01(retained * retentionMultiplier);
    }

    private static float ApplyCoherenceBias(float waterRatio,
                                            float temperature,
                                            MapRegion.CoherenceConstraint coherence,
                                            MapGenParameters parameters)
    {
        float adjusted = waterRatio;

        if (coherence.isExtremeOcean)
            return Mathf.Max(adjusted, 0.92f);

        if (coherence.isExtremeArid)
            return Mathf.Min(adjusted, 0.03f);

        if (coherence.isExtremeFrozen)
            return Mathf.Max(adjusted, 0.65f);

        float targetWater = ComputePreferredWaterTarget(coherence, temperature);
        float influence = Mathf.Clamp01((Mathf.Max(coherence.oceanicity, coherence.deserticity) * 0.5f + coherence.frigidity * 0.3f)
                        * Mathf.Lerp(0.35f, 1f, parameters.coherenceWaterBlend));
        adjusted = Mathf.Lerp(adjusted, targetWater, influence);

        if (coherence.frigidity > 0.55f && temperature < 0f)
            adjusted = Mathf.Max(adjusted, Mathf.Lerp(0.35f, 0.75f, coherence.frigidity));

        return Mathf.Clamp01(adjusted);
    }

    private static float ApplyCoherencePostFlow(float waterRatio,
                                                MapRegion.CoherenceConstraint coherence,
                                                MapGenParameters parameters)
    {
        float adjusted = Mathf.Clamp01(waterRatio);

        if (coherence.isExtremeOcean)
            return Mathf.Max(adjusted, 0.85f);

        if (coherence.isExtremeArid)
            return Mathf.Min(adjusted, 0.05f);

        if (coherence.isExtremeFrozen)
            return Mathf.Max(adjusted, 0.4f);

        float influence = Mathf.Clamp01(Mathf.Max(coherence.oceanicity,
                                                  Mathf.Max(coherence.deserticity, coherence.frigidity)));
        if (influence <= 0.01f)
            return adjusted;

        float targetWater = ComputePreferredWaterTarget(coherence, 5f);
        float tolerance = Mathf.Lerp(0.48f, 0.15f, influence) * Mathf.Lerp(1.1f, 0.75f, parameters.coherenceWaterBlend);
        float minPreferred = Mathf.Clamp01(targetWater - tolerance);
        float maxPreferred = Mathf.Clamp01(targetWater + tolerance);
        float correctionStrength = 0.16f + influence * Mathf.Lerp(0.12f, 0.34f, parameters.coherenceWaterBlend);

        if (adjusted < minPreferred)
            adjusted = Mathf.Lerp(adjusted, minPreferred, correctionStrength);
        else if (adjusted > maxPreferred)
            adjusted = Mathf.Lerp(adjusted, maxPreferred, correctionStrength);

        return Mathf.Clamp01(adjusted);
    }

    private static float ComputePreferredWaterTarget(MapRegion.CoherenceConstraint coherence, float temperature)
    {
        float targetWater = coherence.projectedWaterRatio;
        targetWater = Mathf.Lerp(targetWater, 0.9f, coherence.oceanicity * 0.7f);
        targetWater = Mathf.Lerp(targetWater, 0.08f, coherence.deserticity * 0.75f);

        if (temperature < 0f || coherence.frigidity > 0.45f)
            targetWater = Mathf.Lerp(targetWater, 0.72f, coherence.frigidity * 0.65f);

        return Mathf.Clamp01(targetWater);
    }

    private static float ComputeRetentionMultiplier(MapRegion.CoherenceConstraint coherence, MapGenParameters parameters)
    {
        float climateBias = 1f + coherence.oceanicity * 0.55f + coherence.frigidity * 0.25f - coherence.deserticity * 0.55f;
        float projectedBias = Mathf.Lerp(0.75f, 1.35f, coherence.projectedWaterRatio);
        float strength = Mathf.Lerp(1f, climateBias * projectedBias, parameters.coherenceRetentionBias);
        return Mathf.Clamp(strength, 0.2f, 1.8f);
    }

    private static float ComputeRunoffMultiplier(MapRegion.CoherenceConstraint coherence, MapGenParameters parameters)
    {
        float climateBias = 1f + coherence.deserticity * 0.4f - coherence.oceanicity * 0.22f - coherence.frigidity * 0.12f;
        float projectedBias = Mathf.Lerp(0.82f, 1.18f, 1f - coherence.projectedWaterRatio);
        float strength = Mathf.Lerp(1f, climateBias * projectedBias, parameters.coherenceRunoffBias);
        return Mathf.Clamp(strength, 0.7f, 1.45f);
    }
}
