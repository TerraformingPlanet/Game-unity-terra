using System;
using System.Collections;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Partial ViewManager — Synchronisation serveur et contexte runtime local.
/// NotifyCellClicked, ApplyLocalRuntimeContext, RequestAuthoritativeRegionSync, SynchronizeRegionStateFromServer.
/// </summary>
public partial class ViewManager
{
    /// <summary>
    /// Appelé par HexInput quand l'utilisateur clique sur une cellule en vue locale.
    /// Affiche le panel d'info dans le HUD.
    /// </summary>
    public void NotifyCellClicked(HexCell cell)
    {
        if (_state != ViewState.Local) return;
        Debug.Log($"[ViewManager] Cellule cliquée en vue locale : ({cell.Q}, {cell.R})");
        terraformHUD?.ShowHexPanel(cell);
    }

    private void ApplyLocalRuntimeContext(MapRegion region)
    {
        progressTracker?.ClearAuthoritativeProgress();
        terraformHUD?.ClearAuthoritativeRegionState();

        if (terraformSystem != null)
        {
            GenerationContext ctx;
            if (_isContextAuthoritative && terraformHUD != null && terraformHUD.HasAuthoritativeRegionState)
            {
                // Injection depuis l'état serveur autoritatif
                ctx = GenerationContext.BuildWithInjected(
                    hexGrid.GetCells(),
                    region,
                    terraformHUD.AuthoritativeRegionState.weather.ToPlanetaryWeatherState(),
                    terraformHUD.AuthoritativeRegionState.coherence.ToCoherenceConstraint()
                );
            }
            else
            {
                // Calcul local classique
                ctx = GenerationContext.Build(hexGrid.GetCells(), region);
            }

            terraformSystem.SetContext(ctx);
            terraformHUD?.SetRegionContext(ctx);
        }

        progressTracker?.Refresh();
    }

    private void RequestAuthoritativeRegionSync(float latitude, float longitude)
    {
        if (!preferServerRegionSync || !isActiveAndEnabled)
            return;

        StartCoroutine(SynchronizeRegionStateFromServer(latitude, longitude));
    }

    private IEnumerator SynchronizeRegionStateFromServer(float latitude, float longitude)
    {
        string url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/commands/open-region?latitude={1}&longitude={2}",
            SimUrl.TrimEnd('/'),
            latitude,
            longitude);

        string responseJson = null;
        yield return SimHttp.Post(url, SimTimeout,
            r   => responseJson = r,
            err => Debug.LogWarning($"[ViewManager] Sync région serveur indisponible ({err}). Fallback local conserve."));
        if (responseJson == null) yield break;

        RegionState regionState;
        try
        {
            regionState = JsonUtility.FromJson<RegionState>(responseJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ViewManager] Sync région serveur invalide ({ex.Message}). Fallback local conserve.");
            yield break;
        }

        if (!regionState.isValid)
            yield break;

        terraformHUD?.SetAuthoritativeRegionState(regionState);
        terraformSystem?.SynchronizeAuthoritativeRegionState(regionState);

        // Marquer le contexte comme autoritatif pour les prochains appels à ApplyLocalRuntimeContext
        _isContextAuthoritative = true;
    }
}
