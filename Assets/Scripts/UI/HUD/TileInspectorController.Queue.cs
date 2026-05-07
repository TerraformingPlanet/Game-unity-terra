using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// TileInspectorController — Queue domain:
/// territory queue fetch, corp construction queue, two-track display (État / Investisseur).
/// </summary>
public partial class TileInspectorController
{
    private IEnumerator RefreshTerritoryQueue(string corpId, string bodyId, string tileId)
    {
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/territory-queue?body_id={bodyId}&tile_id={tileId}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                RebuildQueueDisplay(null, null);
                yield break;
            }

            TerritoryQueueDto queue;
            try { queue = JsonUtility.FromJson<TerritoryQueueDto>(req.downloadHandler.text); }
            catch { RebuildQueueDisplay(null, null); yield break; }

            // Fetch all corp construction items in parallel
            ConstrItem[] allItems = null;
            float corpCredits    = float.NaN;

            string allUrl = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/construction-queue";
            using (var reqAll = UnityWebRequest.Get(allUrl))
            {
                reqAll.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
                yield return reqAll.SendWebRequest();
                if (reqAll.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var wrapper2 = JsonUtility.FromJson<ConstrItemListDto>("{\"items\":" + reqAll.downloadHandler.text + "}");
                        allItems = wrapper2?.items;
                    }
                    catch { allItems = null; }
                }
            }

            // Fetch corp credits (name, credits from corp list already loaded)
            corpCredits = GetCachedCorpCredits(corpId);

            RebuildQueueDisplay(queue, allItems, corpCredits);
        }
    }

    // Returns cached credits for a corp from the last fetched corps list (-1 if unknown).
    private float GetCachedCorpCredits(string corpId)
    {
        if (_cachedCorpItems == null) return float.NaN;
        foreach (var c in _cachedCorpItems)
            if (c.id == corpId) return c.credits;
        return float.NaN;
    }

    private void RebuildQueueDisplay(TerritoryQueueDto queue, ConstrItem[] allCorpItems, float corpCredits = float.NaN)
    {
        if (_queueSection == null) return;

        bool hasQueue = queue != null;
        _queueSection.style.display = hasQueue ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasQueue) return;

        if (_queueCorpCapitalLabel != null)
            _queueCorpCapitalLabel.text = float.IsNaN(corpCredits) ? "—" : $"{corpCredits:N0} ¤";

        ConstrItem activeItem = null;
        if (queue.items != null)
            foreach (var it in queue.items)
                if (it.status == 1) { activeItem = it; break; }

        float activePct = 0f;
        if (activeItem != null && activeItem.totalCostPts > 0)
            activePct = Mathf.Clamp01((float)activeItem.pointsAccumulated / activeItem.totalCostPts) * 100f;
        string activeName = activeItem != null ? GetBuildingTypeName(activeItem.buildingType) : "—";

        RebuildQueueTracks(queue, activeItem, activePct, activeName);
        RebuildQueuePendingItems(queue);
        RebuildQueueCorpItems(allCorpItems);
    }

    private void RebuildQueueTracks(TerritoryQueueDto queue, ConstrItem activeItem, float activePct, string activeName)
    {
        bool etatActive = queue.isEBDeFortune && activeItem != null;
        if (_queueTrackEtatFill != null)
            _queueTrackEtatFill.style.width = new StyleLength(new Length(etatActive ? activePct : 0f, LengthUnit.Percent));
        if (_queueTrackEtatName != null)
            _queueTrackEtatName.text = etatActive ? activeName : "—";
        if (_queueTrackEtatSlots != null)
            _queueTrackEtatSlots.text = etatActive ? $"{activeItem.pointsAccumulated}/{activeItem.totalCostPts}" : "—";

        bool investorActive = !queue.isEBDeFortune && queue.constructionCapacity > 0f && activeItem != null;
        if (_queueTrackInvestorFill != null)
            _queueTrackInvestorFill.style.width = new StyleLength(new Length(investorActive ? activePct : 0f, LengthUnit.Percent));
        if (_queueTrackInvestorName != null)
            _queueTrackInvestorName.text = investorActive ? activeName : "—";
        if (_queueTrackInvestorSlots != null)
            _queueTrackInvestorSlots.text = investorActive ? $"{activeItem.pointsAccumulated}/{activeItem.totalCostPts}" : "—";
    }

    private void RebuildQueuePendingItems(TerritoryQueueDto queue)
    {
        if (_queuePendingContainer == null) return;
        _queuePendingContainer.Clear();
        if (queue.items == null) return;
        foreach (var item in queue.items)
        {
            if (item.status != 0) continue;
            var row = new VisualElement();
            row.AddToClassList("queue-pending-item");
            var nameLabel = new Label(GetBuildingTypeName(item.buildingType));
            nameLabel.AddToClassList("queue-item-name");
            row.Add(nameLabel);
            var costLabel = new Label($"{item.totalCostPts} pts");
            costLabel.AddToClassList("queue-item-ticks");
            row.Add(costLabel);
            _queuePendingContainer.Add(row);
        }
    }

    private void RebuildQueueCorpItems(ConstrItem[] allCorpItems)
    {
        if (_queueCorpAllContainer == null) return;
        _queueCorpAllContainer.Clear();
        bool anyCorpItem = false;
        if (allCorpItems != null)
        {
            foreach (var item in allCorpItems)
            {
                if (item.status != 1) continue;
                anyCorpItem = true;
                var row = new VisualElement();
                row.AddToClassList("queue-pending-item");
                var nameLabel = new Label(GetBuildingTypeName(item.buildingType));
                nameLabel.AddToClassList("queue-item-name");
                row.Add(nameLabel);
                string shortTile = item.tileId != null && item.tileId.Length > 6
                    ? item.tileId.Substring(0, 6) + "…"
                    : (item.tileId ?? "?");
                var tileLabel = new Label(shortTile);
                tileLabel.AddToClassList("queue-item-ticks");
                row.Add(tileLabel);
                float pct = item.totalCostPts > 0
                    ? Mathf.Clamp01((float)item.pointsAccumulated / item.totalCostPts) * 100f
                    : 0f;
                var barBg = new VisualElement();
                barBg.AddToClassList("queue-track__bar-bg");
                var barFill = new VisualElement();
                barFill.AddToClassList("queue-track__bar-fill");
                barFill.AddToClassList("queue-track__bar-fill--investor");
                barFill.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
                barBg.Add(barFill);
                row.Add(barBg);
                _queueCorpAllContainer.Add(row);
            }
        }
        if (_queueCorpAllTitle != null)
            _queueCorpAllTitle.style.display = anyCorpItem ? DisplayStyle.Flex : DisplayStyle.None;
        _queueCorpAllContainer.style.display = anyCorpItem ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    [System.Serializable]
    private class ConstrItem
    {
        public string id;
        public string tileId;
        public string bodyId;
        public string corpId;
        public int    buildingType;
        public int    status;           // 0=Pending 1=InProgress 2=Done
        public int    ticksRemaining;
        public int    totalCostPts;
        public int    pointsAccumulated;
    }

    [System.Serializable]
    private class ConstrItemListDto
    {
        public ConstrItem[] items;
    }

    [System.Serializable]
    private class TerritoryQueueDto
    {
        public string     territoryId;
        public string     corpId;
        public string     bodyId;
        public ConstrItem[] items;
        public float      constructionCapacity;
        public bool       isEBDeFortune;
    }
}
