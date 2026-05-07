using UnityEngine;

public static class SimulationContractFactory
{
    public static bool TryBuildProjectionState(ViewManager viewManager, out ProjectionState state)
    {
        state = default;

        if (viewManager == null || viewManager.ActivePlanetSphere == null)
            return false;

        if (!viewManager.ActivePlanetSphere.TryBuildProjectionSummary(out PlanetaryHexGrid.ProjectionDebugSummary summary))
            return false;

        state.isValid = true;
        state.planetName = viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune";
        state.projectionOverride = viewManager.ActiveProjectionOverride;
        state.projectionWaterLevel = viewManager.ActiveProjectionWaterLevel;
        state.summary = summary;
        return true;
    }

    public static bool TryBuildRegionState(ViewManager viewManager,
                                           TerraformHUD terraformHUD,
                                           TerraformProgressTracker progressTracker,
                                           out RegionState state)
    {
        state = default;

        if (viewManager == null || viewManager.ActiveHexGrid == null || !viewManager.ActiveHexGrid.HasCells())
            return false;

        if (!viewManager.ActiveHexGrid.TryBuildDebugSummary(out HexGridDebugSummary summary))
            return false;

        GenerationContext generationContext = terraformHUD != null ? terraformHUD.RegionContext : null;
        MapRegion currentRegion = viewManager.ActiveHexGrid.CurrentRegion;
        HexCell selectedCell = terraformHUD != null ? terraformHUD.SelectedCell : null;

        state.isValid = true;
        state.seed = generationContext != null ? generationContext.seed : 0;
        state.planetName = viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune";
        state.coordinates = new SimulationCoordinates
        {
            latitude = currentRegion != null ? currentRegion.latitude : 0f,
            longitude = currentRegion != null ? currentRegion.longitude : 0f
        };
        state.terraformationProgress = progressTracker != null ? progressTracker.Progress : 0f;
        state.summary = summary;

        FillRegionCells(viewManager, ref state);
        FillRegionCoherence(generationContext, currentRegion, ref state);

        if (selectedCell != null)
        {
            state.hasSelectedCell = true;
            state.selectedCell = BuildCellState(selectedCell);
        }

        return true;
    }

    private static void FillRegionCells(ViewManager viewManager, ref RegionState state)
    {
        HexCell[] cells = viewManager.ActiveHexGrid.GetCells();
        if (cells == null || cells.Length == 0) return;

        state.cells = new SimulationCellState[cells.Length];
        for (int index = 0; index < cells.Length; index++)
            state.cells[index] = BuildCellState(cells[index]);
    }

    private static void FillRegionCoherence(GenerationContext generationContext, MapRegion currentRegion, ref RegionState state)
    {
        if (generationContext != null)
        {
            state.coherence = BuildCoherenceState(generationContext.coherence);
            state.weather   = BuildWeatherState(generationContext.weather);
        }
        else if (currentRegion != null)
        {
            state.coherence.projectedWaterRatio = currentRegion.projectedWaterRatio;
        }
    }

    public static WorldState BuildWorldState(ViewManager viewManager,
                                             TerraformHUD terraformHUD,
                                             TerraformProgressTracker progressTracker,
                                             ITickSource tickSource)
    {
        var state = new WorldState
        {
            isValid = viewManager != null,
            tickCount = tickSource != null ? tickSource.TickCount : 0,
            tickRunning = tickSource != null && tickSource.IsRunning,
            activePlanetName = viewManager != null && viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune",
            projectionOverride = viewManager != null ? viewManager.ActiveProjectionOverride : DebugCoherenceOverride.None,
            projectionWaterLevel = viewManager != null ? viewManager.ActiveProjectionWaterLevel : 0f
        };

        if (TryBuildProjectionState(viewManager, out ProjectionState projection))
        {
            state.hasProjection = true;
            state.projection = projection;
        }

        if (TryBuildRegionState(viewManager, terraformHUD, progressTracker, out RegionState region))
        {
            state.hasRegion = true;
            state.region = region;
        }

        return state;
    }

    public static ClientSnapshot BuildClientSnapshot(ViewManager viewManager,
                                                     TerraformHUD terraformHUD,
                                                     TerraformProgressTracker progressTracker,
                                                     ITickSource tickSource)
    {
        var snapshot = new ClientSnapshot
        {
            isValid = viewManager != null,
            currentView = viewManager != null ? viewManager.CurrentState.ToString() : "Unavailable",
            activePlanetName = viewManager != null && viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune",
            tickCount = tickSource != null ? tickSource.TickCount : 0,
            tickRunning = tickSource != null && tickSource.IsRunning,
            terraformationProgress = progressTracker != null ? progressTracker.Progress : 0f
        };

        if (TryBuildProjectionState(viewManager, out ProjectionState projection))
        {
            snapshot.hasProjection = true;
            snapshot.projection = projection;
        }

        if (TryBuildRegionState(viewManager, terraformHUD, progressTracker, out RegionState region))
        {
            snapshot.hasRegion = true;
            snapshot.region = region;
        }

        return snapshot;
    }

    public static SimulationCellState BuildCellState(HexCell cell)
    {
        HexPhysicalState physicalState = cell.state;

        return new SimulationCellState
        {
            address = new SimulationCellAddress { q = cell.Q, r = cell.R },
            terrainName = cell.terrain != null ? cell.terrain.displayName : string.Empty,
            terrainType = cell.terrain != null ? cell.terrain.terrainType : TerrainType.Roche,
            layer = cell.layer,
            altitude = physicalState.altitude,
            temperature = physicalState.tempLocale,
            waterRatio = physicalState.waterRatio,
            toxinLevel = physicalState.toxinLevel,
            windVector = BuildVectorState(physicalState.windVector),
            windSpeed = physicalState.windSpeed,
            rainShadow = physicalState.rainShadow,
            hasRiver = physicalState.hasRiver,
            flowAccumulation = physicalState.flowAccumulation,
            terrainClass = physicalState.terrainClass,
            waterClassification = physicalState.waterClassification,
            hasDownstream = physicalState.hasDownstream,
            downstream = new SimulationCellAddress { q = physicalState.downstreamQ, r = physicalState.downstreamR },
            hasOverflowOutlet = physicalState.hasOverflowOutlet,
            overflowOutlet = new SimulationCellAddress { q = physicalState.overflowQ, r = physicalState.overflowR },
            soil = new SimulationSoilState
            {
                rockHardness = physicalState.soil.rockHardness,
                organicContent = physicalState.soil.organicContent,
                porosity = physicalState.soil.porosity,
                mineralDensity = physicalState.soil.mineralDensity,
                toxicSoil = physicalState.soil.toxicSoil,
                thermalConductivity = physicalState.soil.thermalConductivity
            },
            latOnSphere = cell.latOnSphere,
            lonOnSphere = cell.lonOnSphere
        };
    }

    private static SimulationWeatherState BuildWeatherState(PlanetaryWeatherState weather)
    {
        if (weather == null)
            return default;

        return new SimulationWeatherState
        {
            prevailingWindDirection = BuildVectorState(weather.prevailingWindDir),
            prevailingWindSpeed = weather.prevailingWindSpeed,
            precipitationRate = weather.precipitationRate,
            temperatureOffset = weather.temperatureOffset,
            seasonalModifier = weather.seasonalModifier
        };
    }

    private static SimulationCoherenceState BuildCoherenceState(MapRegion.CoherenceConstraint coherence)
    {
        return new SimulationCoherenceState
        {
            dominantTerrainType = coherence.dominantTerrainType,
            projectedWaterRatio = coherence.projectedWaterRatio,
            oceanicity = coherence.oceanicity,
            deserticity = coherence.deserticity,
            frigidity = coherence.frigidity,
            isExtremeOcean = coherence.isExtremeOcean,
            isExtremeArid = coherence.isExtremeArid,
            isExtremeFrozen = coherence.isExtremeFrozen
        };
    }

    private static SimulationVector2State BuildVectorState(Vector2 vector)
    {
        return new SimulationVector2State
        {
            x = vector.x,
            y = vector.y
        };
    }
}