using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// TileInspectorController — Population domain:
/// tile population fetch, tier display, class distribution.
/// </summary>
public partial class TileInspectorController
{
    private IEnumerator FetchTilePopulation(string bodyId, string tileId)
    {
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/bodies/{bodyId}/tiles/{tileId}/population";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                UpdatePopulationLabels(null);
                yield break;
            }

            // JSON array → wrap for JsonUtility
            string json = "{\"items\":" + req.downloadHandler.text + "}";
            PopulationTierListWrapper wrapper;
            try { wrapper = JsonUtility.FromJson<PopulationTierListWrapper>(json); }
            catch { UpdatePopulationLabels(null); yield break; }

            UpdatePopulationLabels(wrapper.items);
        }
    }

    private void UpdatePopulationLabels(PopulationTierDto[] tiers)
    {
        if (_popPoorLabel == null) return;

        if (tiers == null || tiers.Length == 0)
        {
            _popPoorLabel.text   = "Pauvres : –";
            _popMiddleLabel.text = "Classe moyenne : –";
            _popRichLabel.text   = "Riches : –";
            _popTotalLabel.text  = "Total : –";
            return;
        }

        int poor = 0, middle = 0, rich = 0;
        foreach (var t in tiers)
        {
            switch (t.socialClass)
            {
                case 0: poor   += t.count; break;
                case 1: middle += t.count; break;
                case 2: rich   += t.count; break;
            }
        }
        int total = poor + middle + rich;
        _popPoorLabel.text   = $"Pauvres : {poor.ToString("N0")}";
        _popMiddleLabel.text = $"Classe moyenne : {middle.ToString("N0")}";
        _popRichLabel.text   = $"Riches : {rich.ToString("N0")}";
        _popTotalLabel.text  = $"Total : {total.ToString("N0")}";
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    [System.Serializable]
    private class PopulationTierDto
    {
        public int   socialClass;  // 0=Poor 1=Middle 2=Rich
        public int   count;
        public float avgIncome;
    }

    [System.Serializable]
    private class PopulationTierListWrapper { public PopulationTierDto[] items; }
}
