using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// TileInspectorController — State / Corps domain:
/// badge, initials, corps + state API fetches, class distribution text.
/// </summary>
public partial class TileInspectorController
{
    private void OnBadgeClicked()
    {
        if (string.IsNullOrEmpty(_currentTile.stateId)) return;
        _gameHUDController.ShowTerritoryPanel(_currentTile.stateId,
            _currentTile.stateName);
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(new char[]{ ' ', '-', '_' },
            System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpperInvariant();
        return name.Length >= 2
            ? name.Substring(0, 2).ToUpperInvariant()
            : name.ToUpperInvariant();
    }

    private IEnumerator RefreshStateRelationForTile()
    {
        if (_tileStatusLabel != null) _tileStatusLabel.text = "";

        yield return StartCoroutine(FetchCorpsForTile());
        yield return StartCoroutine(FetchStateDataForTile());

        // ── 3. Refresh tile population (tile-centric) ──────────────────────
        if (!string.IsNullOrEmpty(_activeBodyId))
            yield return FetchTilePopulation(_activeBodyId, _currentTile.tileId);

        // ── 3b. Refresh territory queue (fixed section, above tabs) ────────
        if (!string.IsNullOrEmpty(_selectedCorpId) && !string.IsNullOrEmpty(_activeBodyId))
        {
            yield return RefreshTerritoryQueue(_selectedCorpId, _activeBodyId, _currentTile.tileId);
            _displayedStateId = _currentTile.stateId ?? "";
        }
        else
        {
            RebuildQueueDisplay(null, null);
            _displayedStateId = "";
        }

        // ── 4. Refresh buildings for selected corp on this tile ───────────
        if (!string.IsNullOrEmpty(_selectedCorpId))
            yield return RefreshBuildingsForTile(_selectedCorpId, _currentTile.tileId);
        else
            RebuildBuildingList(null);

        // ── 5. Refresh market tab (bio + local) ────────────────────────────
        yield return RefreshMarketData(_currentTile.tileId);
    }

    private IEnumerator FetchCorpsForTile()
    {
        string corpsUrl = _gameHUDController.GetSimulationServerUrl().TrimEnd('/') + "/game/corporations";
        using (var req = UnityWebRequest.Get(corpsUrl))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                CorpListDto wrapper;
                try { wrapper = JsonUtility.FromJson<CorpListDto>("{\"items\":" + req.downloadHandler.text + "}"); }
                catch { wrapper = null; }

                if (wrapper?.items != null)
                {
                    _cachedCorpItems = wrapper.items;
                    _corpIds.Clear();
                    foreach (var c in wrapper.items)
                        _corpIds.Add(c.id);

                    string _sessionCorp = PlayerSession.Instance?.CorpId ?? "";
                    _selectedCorpId = (!string.IsNullOrEmpty(_sessionCorp) && _corpIds.Contains(_sessionCorp))
                        ? _sessionCorp
                        : (_corpIds.Count > 0 ? _corpIds[0] : "");

                    if (_corpListContainer != null)
                    {
                        _corpListContainer.Clear();
                        foreach (var c in wrapper.items)
                        {
                            var row = new VisualElement();
                            row.AddToClassList("corp-list-row");
                            row.style.flexDirection  = FlexDirection.Row;
                            row.style.alignItems     = Align.Center;
                            row.style.paddingTop     = 3f;
                            row.style.paddingBottom  = 3f;
                            var lbl = new Label(c.name);
                            lbl.AddToClassList("tile-inspector__info-label");
                            lbl.style.flexGrow = 1f;
                            row.Add(lbl);
                            _corpListContainer.Add(row);
                        }
                    }
                }
            }
        }
    }

    private IEnumerator FetchStateDataForTile()
    {
        if (string.IsNullOrEmpty(_currentTile.stateId)) yield break;

        string stateUrl = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/states/{_currentTile.stateId}";
        using (var req = UnityWebRequest.Get(stateUrl))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                StateDto stateDto;
                try { stateDto = JsonUtility.FromJson<StateDto>(req.downloadHandler.text); }
                catch { stateDto = null; }

                if (stateDto != null)
                    ApplyStateDtoToUI(stateDto);
            }
        }
    }

    private void ApplyStateDtoToUI(StateDto stateDto)
    {
        if (_btnTerritoryBadge != null)
        {
            string initials = GetInitials(stateDto.name ?? _currentTile.stateId);
            _btnTerritoryBadge.text    = initials;
            _btnTerritoryBadge.tooltip = stateDto.name ?? "Territoire";
        }

        if (stateDto.isVassal && !string.IsNullOrEmpty(stateDto.vassalCorpId))
            _tileStatusLabel.text = $"Vassal de : {stateDto.vassalCorpId}";
        else if (stateDto.isVassal)
            _tileStatusLabel.text = !string.IsNullOrEmpty(stateDto.name) ? stateDto.name : "Vassal";
        else
            _tileStatusLabel.text = !string.IsNullOrEmpty(stateDto.name) ? stateDto.name : "État indépendant";

        if (_popSummaryLabel != null)
        {
            string stateTypeStr = stateDto.stateType == 0 ? "Capitaliste" : "Nationaliste";
            string literacyStr = $"Alphabétisation : {stateDto.literacyRate * 100f:F0}%\n";
            string profileStr = $"Profil : {stateDto.profileKey}\n";
            string classDist = GetClassDistributionText(stateDto.profileKey);
            string typeStr = $"Type : {stateTypeStr}";
            _popSummaryLabel.text = literacyStr + profileStr + classDist + typeStr;
        }

        if (_territoryLabel != null)
        {
            int tileCount = stateDto.tileIds != null ? stateDto.tileIds.Length : 0;
            _territoryLabel.text = $"Tuiles contrôlées : {tileCount}";
        }
    }

    private string GetClassDistributionText(string profileKey)
    {
        switch (profileKey)
        {
            case "Standard": return "Classes : Pauvre 40%, Moyen 59%, Riche 1%\n";
            case "RicheUtopique": return "Classes : Pauvre 1%, Moyen 98%, Riche 1%\n";
            case "EnDeveloppement": return "Classes : Pauvre 70%, Moyen 28%, Riche 2%\n";
            case "Pauvre": return "Classes : Pauvre 85%, Moyen 14%, Riche 1%\n";
            case "Autoritaire": return "Classes : Pauvre 60%, Moyen 35%, Riche 5%\n";
            default: return "Classes : Inconnues\n";
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    [System.Serializable]
    private class CorpItem
    {
        public string id;
        public string name;
        public int credits;
        public float score;
    }

    [System.Serializable]
    private class CorpListDto
    {
        public CorpItem[] items;
    }

    [System.Serializable]
    private class StateDto
    {
        public string id;
        public string name;
        public bool isVassal;
        public string vassalCorpId;
        public float literacyRate;
        public string profileKey;
        public int stateType;
        public string[] tileIds;
    }
}
