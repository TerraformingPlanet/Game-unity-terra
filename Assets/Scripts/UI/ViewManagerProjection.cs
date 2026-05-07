using UnityEngine;

/// <summary>
/// Partial ViewManager — Gestion de la projection planétaire :
/// rechargement, niveau d'eau, construction du contexte de région (BuildRegion).
/// Extrait de ViewManager.cs pour alléger le fichier principal.
/// </summary>
public partial class ViewManager
{
    // =========================================================
    // API publique — projection
    // =========================================================

    public bool ReloadCurrentProjection()
    {
        if (_activePlanet == null || planetSphere == null)
            return false;

        if (_state == ViewState.Planet)
            ShowProjectedPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);
        else
            planetSphere.LoadPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);

        return true;
    }

    public bool ClearAndReloadCurrentProjection()
    {
        if (_activePlanet == null || planetSphere == null)
            return false;

        planetSphere.ClearProjectionCache();
        return ReloadCurrentProjection();
    }

    public bool SetProjectionWaterLevel(float waterLevel)
    {
        if (_activePlanet == null || planetSphere == null)
            return false;

        _activeProjectionWaterLevel = Mathf.Clamp(waterLevel, -0.45f, 0.45f);
        return ReloadCurrentProjection();
    }

    public bool ResetProjectionWaterLevel()
    {
        return SetProjectionWaterLevel(0f);
    }

    public bool RegenerateCurrentLocalRegion()
    {
        if (hexGrid == null || hexGrid.CurrentRegion == null)
            return false;

        hexGrid.Regenerate();
        ApplyLocalRuntimeContext(hexGrid.CurrentRegion);
        terraformHUD?.RefreshSelectedHexInfo();
        return true;
    }

    // =========================================================
    // Helpers privés — construction du contexte région
    // =========================================================

    /// <summary>
    /// Construit le MapRegion utilisé lors de l'ouverture d'une région locale.
    /// Déduit les contraintes de cohérence (eau, aride, gel) depuis la cellule projetée.
    /// </summary>
    private MapRegion BuildRegion(float latitude, float longitude, DebugCoherenceOverride coherenceOverride)
    {
        HexCell projectedCell = planetSphere != null ? planetSphere.GetProjectedCell(latitude, longitude) : null;
        MapRegion region = ScriptableObject.CreateInstance<MapRegion>();
        region.solarSystem = solarSystemView != null ? solarSystemView.CurrentSystem : null;
        region.planet = _activePlanet;
        region.genParams = _activePlanet.genParams;
        region.latitude = latitude;
        region.longitude = longitude;
        region.projectedTerrain = projectedCell?.terrain;
        region.projectedWaterRatio = projectedCell != null ? projectedCell.state.waterRatio : 0f;

        bool projectedOpenWater = projectedCell != null &&
                                  (projectedCell.state.waterClassification == WaterClassification.OpenOcean ||
                                   (projectedCell.terrain != null &&
                                    projectedCell.terrain.terrainType == TerrainType.Eau &&
                                    projectedCell.state.waterRatio >= 0.95f));

        bool projectedFrozenWater = projectedCell != null &&
                                    (projectedCell.state.waterClassification == WaterClassification.FrozenWater ||
                                     (projectedCell.terrain != null &&
                                      projectedCell.terrain.terrainType == TerrainType.Glace &&
                                      projectedCell.state.waterRatio >= 0.5f));

        bool projectedArid = projectedCell != null &&
                             projectedCell.state.waterClassification == WaterClassification.Dry &&
                             projectedCell.state.waterRatio <= 0.06f;

        region.forceOpenWaterRegion = projectedOpenWater ||
                                      (projectedCell == null && coherenceOverride == DebugCoherenceOverride.Ocean);
        region.forceAridRegion = projectedArid ||
                                 (projectedCell == null && coherenceOverride == DebugCoherenceOverride.Arid);
        region.forceFrozenRegion = projectedFrozenWater ||
                                   (projectedCell == null && coherenceOverride == DebugCoherenceOverride.Frozen);
        return region;
    }
}
