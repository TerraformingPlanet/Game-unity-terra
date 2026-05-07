using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// TileInspectorController — Market domain:
/// bio-market and local financial market fetch + sparkline rebuild.
/// </summary>
public partial class TileInspectorController
{
    private static readonly Color _bioSparklineColor       = new Color(0.31f, 0.86f, 0.55f); // green
    private static readonly Color _financialSparklineColor = new Color(0.98f, 0.76f, 0.24f); // amber

    private IEnumerator RefreshMarketData(string tileId)
    {
        string baseUrl = _gameHUDController.GetSimulationServerUrl().TrimEnd('/');
        int timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));

        // Bio market
        TileBioMarketStateDto bioDto = null;
        string bioUrl = $"{baseUrl}/game/tiles/{tileId}/bio-market";
        using (var req = UnityWebRequest.Get(bioUrl))
        {
            req.timeout = timeout;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { bioDto = JsonUtility.FromJson<TileBioMarketStateDto>(req.downloadHandler.text); }
                catch { bioDto = null; }
            }
        }

        // Local financial market
        LocalMarketStateDto localDto = null;
        string localUrl = $"{baseUrl}/game/market/by-tile/{tileId}";
        using (var req = UnityWebRequest.Get(localUrl))
        {
            req.timeout = timeout;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { localDto = JsonUtility.FromJson<LocalMarketStateDto>(req.downloadHandler.text); }
                catch { localDto = null; }
            }
        }

        RebuildBioMarketList(bioDto);
        RebuildFinancialMarketList(localDto);
    }

    private void RebuildBioMarketList(TileBioMarketStateDto dto)
    {
        if (_marketBioContainer == null) return;
        _marketBioContainer.Clear();

        if (dto?.listings == null || dto.listings.Length == 0)
        {
            _marketBioContainer.Add(new Label("Aucune donnée bio-marché."));
            return;
        }

        foreach (var listing in dto.listings)
        {
            var row = new VisualElement();
            row.AddToClassList("market-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 4f;

            var nameLabel = new Label($"{listing.resource}");
            nameLabel.AddToClassList("tile-inspector__info-label");
            nameLabel.style.width = new StyleLength(80f);

            var abundLabel = new Label($"{listing.abundance:F2}");
            abundLabel.AddToClassList("tile-inspector__value-label");
            abundLabel.style.width = new StyleLength(48f);

            var sparkline = new SparklineElement();
            sparkline.lineColor = _bioSparklineColor;
            sparkline.AddToClassList("market-sparkline-curve");
            if (listing.abundanceHistory != null && listing.abundanceHistory.Length > 1)
                sparkline.SetData(listing.abundanceHistory);

            row.Add(nameLabel);
            row.Add(abundLabel);
            row.Add(sparkline);
            _marketBioContainer.Add(row);
        }
    }

    private void RebuildFinancialMarketList(LocalMarketStateDto dto)
    {
        if (_marketFinancialContainer == null) return;
        _marketFinancialContainer.Clear();

        if (dto?.listings == null || dto.listings.Length == 0)
        {
            _marketFinancialContainer.Add(new Label("Aucune donnée marché."));
            return;
        }

        foreach (var listing in dto.listings)
        {
            var row = new VisualElement();
            row.AddToClassList("market-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 4f;

            string velocitySign = listing.priceVelocity >= 0f ? "+" : "";
            var nameLabel = new Label($"{listing.resourceType}");
            nameLabel.AddToClassList("tile-inspector__info-label");
            nameLabel.style.width = new StyleLength(80f);

            var priceLabel = new Label($"{listing.price:F1} ({velocitySign}{listing.priceVelocity:F2})");
            priceLabel.AddToClassList("tile-inspector__value-label");
            priceLabel.style.width = new StyleLength(80f);

            var sparkline = new SparklineElement();
            sparkline.lineColor = _financialSparklineColor;
            sparkline.AddToClassList("market-sparkline-curve");
            if (listing.priceHistory != null && listing.priceHistory.Length > 1)
                sparkline.SetData(listing.priceHistory);

            row.Add(nameLabel);
            row.Add(priceLabel);
            row.Add(sparkline);
            _marketFinancialContainer.Add(row);
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    [System.Serializable]
    private class TileBioListingDto
    {
        public string resource;
        public string speciesId;
        public float  abundance;
        public float[] abundanceHistory;
    }

    [System.Serializable]
    private class TileBioMarketStateDto
    {
        public string tileId;
        public TileBioListingDto[] listings;
        public int tickComputed;
    }

    [System.Serializable]
    private class ResourceListingDto
    {
        public string resourceType;
        public float  price;
        public float  supply;
        public float  demand;
        public float  priceVelocity;
        public float[] priceHistory;
    }

    [System.Serializable]
    private class LocalMarketStateDto
    {
        public string ownerEntityId;
        public ResourceListingDto[] listings;
        public int tickComputed;
    }
}
