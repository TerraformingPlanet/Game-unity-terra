using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Partial TerraformHUD — synchronisation serveur.
/// RequestAction, SyncActionDefinitionsFromServer, handlers TerraformSystem.
/// </summary>
public partial class TerraformHUD
{
    // =========================================================
    // Handlers boutons (relier depuis l'Inspector via OnClick())
    // =========================================================

    /// <summary>
    /// Appelé par les boutons action en Inspector.
    /// Passer l'index dans le tableau actions[] (0=Heat, 1=Irrigate…).
    /// </summary>
    public void RequestAction(int actionIndex)
    {
        if (_selectedCell == null)
        {
            Debug.Log("[TerraformHUD] Aucun hex sélectionné.");
            return;
        }
        if (actionIndex < 0 || actionIndex >= actions.Length)
        {
            Debug.LogWarning("[TerraformHUD] Index d'action invalide : " + actionIndex);
            return;
        }

        bool ok = terraformSystem.ApplyAction(_selectedCell, actions[actionIndex]);
        if (ok)
            Debug.Log($"[TerraformHUD] Action {actions[actionIndex].displayName} soumise.");
        else
            Debug.Log($"[TerraformHUD] Action {actions[actionIndex].displayName} refusée (pré-conditions non remplies).");
    }

    private IEnumerator SyncActionDefinitionsFromServer()
    {
        if (actions == null || actions.Length == 0)
            yield break;

        string requestUrl = $"{SimUrl.TrimEnd('/')}/actions/catalog";
        using UnityWebRequest request = UnityWebRequest.Get(requestUrl);
        request.timeout = Mathf.Max(1, Mathf.CeilToInt(SimTimeout));

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[TerraformHUD] Catalogue d'actions serveur indisponible ({request.error}). Fallback local conserve.");
            yield break;
        }

        SimulationActionCatalog catalog;
        try
        {
            catalog = JsonUtility.FromJson<SimulationActionCatalog>(request.downloadHandler.text);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TerraformHUD] Catalogue d'actions serveur invalide ({ex.Message}). Fallback local conserve.");
            yield break;
        }

        if (catalog.actions == null || catalog.actions.Length == 0)
        {
            Debug.LogWarning("[TerraformHUD] Catalogue d'actions serveur vide. Fallback local conserve.");
            yield break;
        }

        int appliedCount = 0;
        for (int index = 0; index < actions.Length; index++)
        {
            TerraformActionData action = actions[index];
            if (action == null)
                continue;

            for (int definitionIndex = 0; definitionIndex < catalog.actions.Length; definitionIndex++)
            {
                SimulationActionDefinition definition = catalog.actions[definitionIndex];
                if (definition.actionType != action.actionType)
                    continue;

                action.ApplyAuthoritativeDefinition(definition);
                appliedCount++;
                break;
            }
        }

        _serverActionCatalogLoaded = appliedCount > 0;
        if (_serverActionCatalogLoaded)
            Debug.Log($"[TerraformHUD] Catalogue d'actions serveur synchronise ({appliedCount} action(s)).");
    }

    private void HandleAuthoritativeCellSynchronized(HexCell cell)
    {
        if (cell == null || _selectedCell == null)
            return;

        if (_selectedCell.Q != cell.Q || _selectedCell.R != cell.R)
            return;

        ShowHexPanel(cell);
    }

    private void HandleAuthoritativeWorldStateSynchronized(WorldState worldState)
    {
        if (!worldState.hasRegion)
            return;

        SetAuthoritativeRegionState(worldState.region);
    }
}
