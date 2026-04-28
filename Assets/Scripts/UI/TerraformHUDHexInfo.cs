using UnityEngine;

/// <summary>
/// Partial TerraformHUD — affichage hex et progression de terraformation.
/// UpdateProgress, RefreshHexInfo, formatters statiques.
/// </summary>
public partial class TerraformHUD
{
    // =========================================================
    // Mise à jour de l'affichage
    // =========================================================

    private void UpdateProgress(float ratio)
    {
        if (progressSlider != null)
            progressSlider.value = ratio;

        if (progressLabel != null)
            progressLabel.text = $"{ratio * 100f:F1}% Terraform.";

        OnProgressUpdated?.Invoke(ratio);
    }

    private void RefreshHexInfo()
    {
        if (hexInfoLabel == null || _selectedCell == null) return;

        HexPhysicalState s = _selectedCell.state;
        string terrain = _selectedCell.terrain != null ? _selectedCell.terrain.displayName : "?";
        string regionInfo = string.Empty;

        if (_regionContext != null)
        {
            if (_hasAuthoritativeRegionState && _authoritativeRegionState.isValid)
            {
                string bodyName = !string.IsNullOrEmpty(_authoritativeRegionState.planetName)
                    ? _authoritativeRegionState.planetName
                    : (_regionContext.body != null ? _regionContext.body.bodyName : "?");

                AtmosphericState atm = _authoritativeRegionState.atmosphericState;
                string atmLine = atm.habitabilityScore > 0f
                    ? $"Atmosphere : O2 {atm.o2Ratio * 100f:F1}% | CO2 {atm.co2Ratio * 100f:F2}% | {atm.atmosphericPressure:F1} kPa | T {atm.averageTemperature:F1}°C\n" +
                      $"Habitabilité : {atm.habitabilityScore * 100f:F1}% | Toxines {atm.toxinRatio * 100f:F0}%\n"
                    : string.Empty;
                regionInfo =
                    $"Astre : {bodyName}\n" +
                    $"Région : lat {_authoritativeRegionState.coordinates.latitude:F2} | lon {_authoritativeRegionState.coordinates.longitude:F2}\n" +
                    $"Projection : {_authoritativeRegionState.coherence.dominantTerrainType} | eau {_authoritativeRegionState.coherence.projectedWaterRatio * 100f:F0}%\n" +
                    $"Climat : dT {_authoritativeRegionState.weather.temperatureOffset:+0.0;-0.0;0.0}°C | pluie {_authoritativeRegionState.weather.precipitationRate * 100f:F0}%\n" +
                    $"Vent : {_authoritativeRegionState.weather.prevailingWindSpeed:F2} ({_authoritativeRegionState.weather.prevailingWindDirection.x:F1}, {_authoritativeRegionState.weather.prevailingWindDirection.y:F1})\n" +
                    $"Cohérence : mer {_authoritativeRegionState.coherence.oceanicity:F2} | aride {_authoritativeRegionState.coherence.deserticity:F2} | gel {_authoritativeRegionState.coherence.frigidity:F2}\n" +
                    atmLine + "\n";
            }
            else
            {
                MapRegion region = _regionContext.region;
                PlanetaryWeatherState weather = _regionContext.weather;
                MapRegion.CoherenceConstraint coherence = _regionContext.coherence;
                string bodyName = _regionContext.body != null ? _regionContext.body.bodyName : "?";
                float solarIntensity = region != null ? region.SolarIntensity : 1f;
                bool tidalLock = region != null && region.IsTidallyLocked;
                string projectedTerrain = region != null && region.projectedTerrain != null
                    ? region.projectedTerrain.displayName
                    : "?";

                regionInfo =
                    $"Astre : {bodyName}\n" +
                    $"Région : lat {region.latitude:F2} | lon {region.longitude:F2}\n" +
                    $"Projection : {projectedTerrain} | eau {region.projectedWaterRatio * 100f:F0}%\n" +
                    $"Solaire : {solarIntensity:F2} | Tidal lock : {(tidalLock ? "Oui" : "Non")}\n" +
                    $"Climat : dT {weather.temperatureOffset:+0.0;-0.0;0.0}°C | pluie {weather.precipitationRate * 100f:F0}%\n" +
                    $"Vent : {weather.prevailingWindSpeed:F2} ({weather.prevailingWindDir.x:F1}, {weather.prevailingWindDir.y:F1})\n" +
                    $"Cohérence : mer {coherence.oceanicity:F2} | aride {coherence.deserticity:F2} | gel {coherence.frigidity:F2}\n\n";
            }
        }

        hexInfoLabel.text =
            regionInfo +
            $"<b>{terrain}</b>\n" +
            $"Temp : {s.tempLocale:F1}°C\n" +
            $"Eau  : {s.waterRatio * 100f:F0}%\n" +
            $"Hydro : {FormatWaterClassification(s.waterClassification)} | relief {FormatTerrainClass(s.terrainClass)}\n" +
            $"Flux : {s.flowAccumulation} | aval : {FormatDownstream(s)}\n" +
            $"Exutoire : {FormatOverflowOutlet(s)}\n" +
            $"Toxines : {s.toxinLevel * 100f:F0}%\n" +
            $"Dureté  : {s.soil.rockHardness:F2}\n" +
            $"Minéraux : {s.soil.mineralDensity:F2}";
    }

    private static string FormatWaterClassification(WaterClassification classification)
    {
        return classification switch
        {
            WaterClassification.OpenOcean   => "Océan",
            WaterClassification.InlandWater => "Eau intérieure",
            WaterClassification.Coast       => "Côte",
            WaterClassification.FrozenWater => "Eau gelée",
            _                               => "Sec"
        };
    }

    private static string FormatTerrainClass(TerrainClass terrainClass)
    {
        return terrainClass switch
        {
            TerrainClass.Ridge   => "Crête",
            TerrainClass.Basin   => "Bassin",
            TerrainClass.Channel => "Chenal",
            TerrainClass.Source  => "Source",
            _                    => "Pente"
        };
    }

    private static string FormatDownstream(HexPhysicalState state)
    {
        return state.hasDownstream
            ? $"({state.downstreamQ}, {state.downstreamR})"
            : "Aucun";
    }

    private static string FormatOverflowOutlet(HexPhysicalState state)
    {
        return state.hasOverflowOutlet
            ? $"({state.overflowQ}, {state.overflowR})"
            : "Aucun";
    }
}
