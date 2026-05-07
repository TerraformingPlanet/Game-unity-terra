using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// TileInspectorController — Buildings domain:
/// construct, demolish, refresh buildings list, building type name helper.
/// </summary>
public partial class TileInspectorController
{
    private void OnConstructButtonClicked()
    {
        if (_dropdownBuildType == null || string.IsNullOrEmpty(_selectedCorpId)
            || string.IsNullOrEmpty(_currentTile.tileId) || string.IsNullOrEmpty(_activeBodyId))
        {
            if (_tileStatusLabel != null)
                _tileStatusLabel.text = string.IsNullOrEmpty(_selectedCorpId)
                    ? "Sélectionnez une corporation d'abord."
                    : "Corps céleste non résolu.";
            return;
        }

        int buildingType = _dropdownBuildType.index;
        _btnConstruct?.SetEnabled(false);
        StartCoroutine(ConstructBuilding(_selectedCorpId, _activeBodyId, _currentTile.tileId, buildingType));
    }

    private IEnumerator RefreshBuildingsForTile(string corpId, string tileId)
    {
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/buildings";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                RebuildBuildingList(null);
                yield break;
            }

            BuildingListDto wrapper;
            try { wrapper = JsonUtility.FromJson<BuildingListDto>("{\"items\":" + req.downloadHandler.text + "}"); }
            catch { RebuildBuildingList(null); yield break; }

            var tileBuildings = new List<BuildingItem>();
            if (wrapper?.items != null)
                foreach (var b in wrapper.items)
                    if (b.tileId == tileId) tileBuildings.Add(b);

            RebuildBuildingList(tileBuildings);
        }
    }

    private void RebuildBuildingList(List<BuildingItem> buildings)
    {
        if (_buildingListContainer == null) return;
        _buildingListContainer.Clear();
        if (buildings == null) return;

        foreach (var b in buildings)
        {
            var item = new VisualElement();
            item.AddToClassList("building-item");
            var label = new Label($"{GetBuildingTypeName(b.buildingType)} (Niv.{b.level}) - Prod:{b.production:F1}");
            item.Add(label);

            var demolishBtn = new Button { text = "Démolir" };
            demolishBtn.clicked += () => StartCoroutine(DoDemolishBuilding(_selectedCorpId, b.id));
            item.Add(demolishBtn);

            _buildingListContainer.Add(item);
        }
    }

    private string GetBuildingTypeName(int type)
    {
        switch (type)
        {
            case 0: return "Mine";
            case 1: return "Ferme";
            case 2: return "Centrale";
            case 3: return "Recherche";
            case 4: return "Route";
            case 5: return "Port";
            case 6: return "Cosmodrome";
            case 7: return "Scierie";
            default: return $"Type {type}";
        }
    }

    private IEnumerator ConstructBuilding(string corpId, string bodyId, string tileId, int buildingType)
    {
        // Server expects query params, not form body
        string base64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{}"));
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/buildings"
                   + $"?body_id={UnityWebRequest.EscapeURL(bodyId)}"
                   + $"&tile_id={UnityWebRequest.EscapeURL(tileId)}"
                   + $"&building_type={buildingType}";
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler   = new UploadHandlerRaw(new byte[0]) { contentType = "application/json" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                if (_tileStatusLabel != null) _tileStatusLabel.text = "Construction planifiée.";
                // Refresh queue + buildings
                yield return RefreshTerritoryQueue(corpId, bodyId, tileId);
                yield return RefreshBuildingsForTile(corpId, tileId);
            }
            else
            {
                string detail = req.downloadHandler?.text ?? req.error;
                if (_tileStatusLabel != null) _tileStatusLabel.text = $"Erreur: {detail}";
            }
        }
        _btnConstruct?.SetEnabled(true);
    }

    private IEnumerator DoDemolishBuilding(string corpId, string buildingId)
    {
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/buildings/{buildingId}";
        using (var req = UnityWebRequest.Delete(url))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                if (_tileStatusLabel != null) _tileStatusLabel.text = "Bâtiment démoli.";
                if (!string.IsNullOrEmpty(_currentTile.tileId))
                    yield return RefreshBuildingsForTile(corpId, _currentTile.tileId);
            }
            else
            {
                if (_tileStatusLabel != null) _tileStatusLabel.text = $"Erreur démolition: {req.error}";
            }
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    [System.Serializable]
    private class BuildingItem
    {
        public string id;
        public string tileId;
        public int buildingType;
        public int level;
        public float production;
        public float efficiency;
    }

    [System.Serializable]
    private class BuildingListDto
    {
        public BuildingItem[] items;
    }
}
