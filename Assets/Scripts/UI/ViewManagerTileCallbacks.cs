using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Partial ViewManager — callbacks tuiles H3 (Globe, Flat, Tangent).
/// Méthodes : OnGlobeRegionClicked, OnGlobeH3TileResolved, OnGlobeH3TilesReady, OnFlatRegionClicked.
/// </summary>
public partial class ViewManager
{
    private void OnGlobeRegionClicked(float lat, float lon)
    {
        _selectedGlobeLat  = lat;
        _selectedGlobeLon  = lon;
        _hasGlobeSelection = true;
        OpenRegion(lat, lon);
    }

    /// <summary>Mise à jour HUD avec données H3 authoritatives (1–2 s après le clic).</summary>
    private void OnGlobeH3TileResolved(GoldbergTileState tile)
    {
        if (_state == ViewState.Planet && _planetSubView == PlanetSubView.Globe)
            terraformHUD?.ShowH3TileInfo(tile);
    }

    /// <summary>Distribue les tuiles H3 aux vues Plate et Tangente.</summary>
    private void OnGlobeH3TilesReady(GoldbergTileState[] tiles, Dictionary<TerrainType, Color> colorByType)
    {
        int  tileCount               = tiles?.Length ?? 0;
        bool flatAssigned            = planetFlatView    != null;
        bool tangentAssigned         = planetTangentView != null;
        bool flatActiveInHierarchy   = flatAssigned   && planetFlatView.gameObject.activeInHierarchy;
        bool tangentActiveInHierarchy = tangentAssigned && planetTangentView.gameObject.activeInHierarchy;
        bool flatLoadedBefore        = flatAssigned   && planetFlatView.IsLoaded;
        bool tangentLoadedBefore     = tangentAssigned && planetTangentView.IsLoaded;

        Debug.Log(
            $"[ViewManager] OnGlobeH3TilesReady | state={_state} | subView={_planetSubView} | tiles={tileCount} | " +
            $"flat(assigned={flatAssigned}, active={flatActiveInHierarchy}, loaded={flatLoadedBefore}) | " +
            $"tangent(assigned={tangentAssigned}, active={tangentActiveInHierarchy}, loaded={tangentLoadedBefore})");

        planetFlatView?.LoadPlanetFromH3(tiles, colorByType);
        planetTangentView?.RefreshColorsFromH3(tiles, colorByType);

        Debug.Log(
            $"[ViewManager] OnGlobeH3TilesReady complete | " +
            $"flatLoaded={flatAssigned   && planetFlatView.IsLoaded} | " +
            $"tangentLoaded={tangentAssigned && planetTangentView.IsLoaded}");
    }

    private void OnFlatRegionClicked(float lat, float lon) => ShowLocalView(lat, lon);
}
