using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the TileInspector UI component.
/// Handles tile information display, claim/unclaim, building list, tabs, contracts.
/// </summary>
public class TileInspectorController : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset tileInspectorTemplate;
    [SerializeField] private StyleSheet variablesStyleSheet;
    [SerializeField] private StyleSheet baseStyleSheet;

    private VisualElement _tileInspector;
    private GoldbergTileState _currentTile;
    private bool _hasTile;

    // UI Elements - Header
    private Label _tileHeaderLabel;
    private Button _btnTerritoryBadge;   // ← badge with state initials in header

    // Selected corp for construction (populated from fetched corps list)
    private string _selectedCorpId = "";

    // UI Elements - Status
    private Label _tileStatusLabel;
    private Label _terrainInfoLabel;

    // UI Elements - Tabs
    private Button[] _tabButtons;
    private VisualElement[] _tabContents;
    private int _activeTabIndex = 0;

    // UI Elements - Population Tab
    private Label _popSummaryLabel;
    private Label _territoryLabel;
    private Label _popPoorLabel;
    private Label _popMiddleLabel;
    private Label _popRichLabel;
    private Label _popTotalLabel;

    // UI Elements - Building Tab
    private DropdownField _dropdownBuildType;
    private Button _btnConstruct;
    private VisualElement _buildingListContainer;
    private VisualElement _constructionQueueContainer;

    // UI Elements - Territory Queue Section (fixed, between header and tabs)
    private VisualElement _queueSection;
    private VisualElement _queuePendingContainer;
    private VisualElement _queueCorpAllContainer;
    private Label         _queueCorpAllTitle;
    private Label         _queueCorpCapitalLabel;
    // Two-track progress: État (EB de fortune) / Investisseur (EB formel)
    private VisualElement _queueTrackEtatFill;
    private VisualElement _queueTrackInvestorFill;
    private Label         _queueTrackEtatName;
    private Label         _queueTrackInvestorName;

    // UI Elements - Additional Sections
    private VisualElement _corpListContainer;
    private Label _ecologyLabel;
    private VisualElement _marketBioContainer;
    private VisualElement _marketFinancialContainer;
    private VisualElement _publicContractsContainer;
    private VisualElement _myContractsContainer;

    // Data
    private List<string> _corpIds = new List<string>();
    private CorpItem[]   _cachedCorpItems;           // cached from last corps fetch
    private string _activeBodyId = "";
    private GameHUDController _gameHUDController;

    public void Initialize(VisualElement root, VisualTreeAsset injectedTemplate = null)
    {
        if (injectedTemplate != null) tileInspectorTemplate = injectedTemplate;
        _gameHUDController = GetComponent<GameHUDController>();
        BuildTileInspector(root);
    }

    private void BuildTileInspector(VisualElement root)
    {
        VisualTreeAsset asset = tileInspectorTemplate
            ?? Resources.Load<VisualTreeAsset>("UI/Templates/TileInspector");

        if (asset == null)
        {
            Debug.LogWarning("[TileInspectorController] TileInspector template not found.");
            return;
        }

        // CloneTree injects directly into root so position:Absolute with bottom:0
        // resolves against root's full height (not a zero-height TemplateContainer).
        asset.CloneTree(root);
        _tileInspector = root.Q<VisualElement>("tile-inspector");

        if (_tileInspector == null)
        {
            Debug.LogError("[TileInspectorController] BuildTileInspector: 'tile-inspector' element not found.");
            return;
        }

        // Grab named elements
        _tileHeaderLabel         = _tileInspector.Q<Label>("tile-header-label");
        _btnTerritoryBadge       = _tileInspector.Q<Button>("btn-territory-badge");
        _buildingListContainer   = _tileInspector.Q<VisualElement>("building-list-container");
        _tileStatusLabel         = _tileInspector.Q<Label>("tile-status-label");
        _terrainInfoLabel        = _tileInspector.Q<Label>("terrain-info-label");

        // Territory queue section
        _queueSection             = _tileInspector.Q<VisualElement>("territory-queue-section");
        _queuePendingContainer    = _tileInspector.Q<VisualElement>("queue-pending-container");
        _queueCorpAllContainer    = _tileInspector.Q<VisualElement>("queue-corp-all-container");
        _queueCorpAllTitle        = _tileInspector.Q<Label>("queue-corp-all-title");
        _queueCorpCapitalLabel    = _tileInspector.Q<Label>("queue-corp-capital-label");
        _queueTrackEtatFill       = _tileInspector.Q<VisualElement>("queue-track-etat-fill");
        _queueTrackInvestorFill   = _tileInspector.Q<VisualElement>("queue-track-investor-fill");
        _queueTrackEtatName       = _tileInspector.Q<Label>("queue-track-etat-name");
        _queueTrackInvestorName   = _tileInspector.Q<Label>("queue-track-investor-name");
        if (_queueSection != null) _queueSection.style.display = DisplayStyle.None;

        // New elements for additional sections
        _corpListContainer       = _tileInspector.Q<VisualElement>("corp-list-container");
        _ecologyLabel             = _tileInspector.Q<Label>("ecology-label");
        _marketBioContainer       = _tileInspector.Q<VisualElement>("market-bio-container");
        _marketFinancialContainer = _tileInspector.Q<VisualElement>("market-financial-container");
        _publicContractsContainer = _tileInspector.Q<VisualElement>("public-contracts-container");
        _myContractsContainer    = _tileInspector.Q<VisualElement>("my-contracts-container");

        // ── Tab elements ───────────────────────────────────────────────
        _tabButtons = new Button[]
        {
            _tileInspector.Q<Button>("tab-resume"),
            _tileInspector.Q<Button>("tab-population"),
            _tileInspector.Q<Button>("tab-batiment"),
            _tileInspector.Q<Button>("tab-marche"),
            _tileInspector.Q<Button>("tab-contrats")
        };
        _tabContents = new VisualElement[]
        {
            _tileInspector.Q<VisualElement>("tab-content-resume"),
            _tileInspector.Q<VisualElement>("tab-content-population"),
            _tileInspector.Q<VisualElement>("tab-content-batiment"),
            _tileInspector.Q<VisualElement>("tab-content-marche"),
            _tileInspector.Q<VisualElement>("tab-content-contrats")
        };

        // Population tab elements
        _popSummaryLabel = _tileInspector.Q<Label>("pop-summary-label");
        _territoryLabel  = _tileInspector.Q<Label>("territory-label");
        _popPoorLabel    = _tileInspector.Q<Label>("pop-poor-label");
        _popMiddleLabel  = _tileInspector.Q<Label>("pop-middle-label");
        _popRichLabel    = _tileInspector.Q<Label>("pop-rich-label");
        _popTotalLabel   = _tileInspector.Q<Label>("pop-total-label");

        // Bâtiment tab elements
        _dropdownBuildType = _tileInspector.Q<DropdownField>("dropdown-build-type");
        _btnConstruct      = _tileInspector.Q<Button>("btn-construct");
        _constructionQueueContainer = _tileInspector.Q<VisualElement>("construction-queue-container");

        // Populate build type dropdown
        if (_dropdownBuildType != null)
        {
            _dropdownBuildType.choices = new List<string>
            {
                "Mine", "Farm", "EnergyPlant", "Research", "Road", "SeaPort", "Spaceport", "Sawmill"
            };
            _dropdownBuildType.index = 0;
        }

        // Wire tab buttons
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            int tabIndex = i; // capture for closure
            _tabButtons[i]?.RegisterCallback<ClickEvent>(_ => SwitchInspectorTab(tabIndex));
        }

        // Wire buttons
        _btnTerritoryBadge?.RegisterCallback<ClickEvent>(_ => OnBadgeClicked());
        _btnConstruct?.RegisterCallback<ClickEvent>(_ => OnConstructButtonClicked());
        _tileInspector.Q<Button>("btn-close-inspector")
            ?.RegisterCallback<ClickEvent>(_ => Hide());

        // ── Bouton copie tileId (debug) ────────────────────────────────
        var header = _tileInspector.Q<VisualElement>(className: "tile-inspector__header");
        if (header != null)
        {
            var btnCopyId = new Button { name = "btn-copy-tile-id", text = "⎘" };
            btnCopyId.tooltip = "Copier le tileId";
            btnCopyId.AddToClassList("tile-inspector__close-btn"); // même style que ✕
            btnCopyId.style.marginRight = new StyleLength(4f);
            btnCopyId.RegisterCallback<ClickEvent>(_ =>
            {
                string tid = _currentTile.tileId ?? "";
                GUIUtility.systemCopyBuffer = tid;
                Debug.Log($"[TileInspector] tileId copié : {tid}");
            });
            // Insert before btn-close-inspector
            var closeBtn = header.Q<Button>("btn-close-inspector");
            if (closeBtn != null) header.Insert(header.IndexOf(closeBtn), btnCopyId);
            else header.Add(btnCopyId);
        }

        // Default to tab 0 (Résumé)
        SwitchInspectorTab(0);

        // Start hidden
        _tileInspector.style.display = DisplayStyle.None;
    }

    private void SwitchInspectorTab(int tabIndex)
    {
        if (_tabButtons == null || _tabContents == null || tabIndex < 0 || tabIndex >= _tabButtons.Length)
            return;

        _activeTabIndex = tabIndex;

        // Update tab button styles
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            if (_tabButtons[i] != null)
            {
                if (i == tabIndex)
                    _tabButtons[i].AddToClassList("tile-inspector__tab--active");
                else
                    _tabButtons[i].RemoveFromClassList("tile-inspector__tab--active");
            }
        }

        // Show/hide content panels
        for (int i = 0; i < _tabContents.Length; i++)
        {
            if (_tabContents[i] != null)
            {
                _tabContents[i].style.display = (i == tabIndex) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }

    public void ShowTile(GoldbergTileState tile, string bodyId = "")
    {
        _currentTile = tile;
        _hasTile = true;
        if (!string.IsNullOrEmpty(bodyId))
            _activeBodyId = bodyId;
        if (_tileInspector != null)
        {
            _tileInspector.style.display = DisplayStyle.Flex;
            UpdateTileDisplay();
        }
    }

    public void Hide()
    {
        if (_tileInspector != null)
            _tileInspector.style.display = DisplayStyle.None;
    }

    private void UpdateTileDisplay()
    {
        if (!_hasTile || _tileInspector == null) return;

        // Update header
        if (_tileHeaderLabel != null)
            _tileHeaderLabel.text = $"Tuile {_currentTile.tileId}";

        // Update badge with initials from stateName already on the tile (may refine after API fetch)
        if (_btnTerritoryBadge != null)
        {
            string initials = GetInitials(_currentTile.stateName ?? _currentTile.stateId);
            _btnTerritoryBadge.text    = initials;
            _btnTerritoryBadge.tooltip = _currentTile.stateName ?? _currentTile.stateId ?? "Territoire";
            _btnTerritoryBadge.style.display = string.IsNullOrEmpty(_currentTile.stateId)
                ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Update terrain info
        if (_terrainInfoLabel != null)
        {
            string terrainText = $"Terrain: {_currentTile.terrainType}\n";
            terrainText += $"Température: {_currentTile.temperature:F1}°C\n";
            terrainText += $"Eau: {_currentTile.waterRatio:F2}\n";
            terrainText += $"Habitable: {(_currentTile.isHabitable ? "Oui" : "Non")}";
            _terrainInfoLabel.text = terrainText;
        }

        // Update ecology info
        UpdateEcologyDisplay();

        // Refresh corps and state data
        StartCoroutine(RefreshStateRelationForTile());
    }

    private void UpdateEcologyDisplay()
    {
        if (_ecologyLabel == null) return;

        var sb = new System.Text.StringBuilder();

        // Couverture végétale (≠ présence d'arbres : 0% = sol nu, des arbres peuvent exister quand même)
        int vegPct = Mathf.RoundToInt(_currentTile.vegetationDensity * 100f);
        sb.AppendLine($"Couverture végétale : {vegPct}%");

        // Hydrologie locale
        string waterFeature = null;
        switch (_currentTile.waterClassification)
        {
            case WaterClassification.Coast:       waterFeature = "Côte"; break;
            case WaterClassification.FrozenWater: waterFeature = "Eau gelée"; break;
            case WaterClassification.InlandWater:
                waterFeature = _currentTile.terrainClass switch
                {
                    TerrainClass.Channel => "Rivière",
                    TerrainClass.Source  => "Source",
                    _                    => "Zone humide"
                };
                break;
        }
        if (waterFeature != null)
            sb.AppendLine(waterFeature);

        // Espèces — flore (minVegetation <= 0) puis faune (minVegetation > 0)
        if (_currentTile.species != null && _currentTile.species.Length > 0)
        {
            bool floraHeaderAdded = false;
            foreach (var sp in _currentTile.species)
            {
                if (sp.minVegetation <= 0f && sp.density > 0f)
                {
                    if (!floraHeaderAdded) { sb.AppendLine("Flore :"); floraHeaderAdded = true; }
                    sb.AppendLine($"  {sp.speciesId} : {Mathf.RoundToInt(sp.density * 100f)}%");
                }
            }

            bool faunaHeaderAdded = false;
            foreach (var sp in _currentTile.species)
            {
                if (sp.minVegetation > 0f && sp.density > 0f)
                {
                    if (!faunaHeaderAdded) { sb.AppendLine("Faune :"); faunaHeaderAdded = true; }
                    sb.AppendLine($"  {sp.speciesId} : {Mathf.RoundToInt(sp.density * 100f)}%");
                }
            }
        }
        else if (_currentTile.wildlifeDensity > 0f)
        {
            // Fallback si pas encore de données espèces
            sb.AppendLine($"Faune : {Mathf.RoundToInt(_currentTile.wildlifeDensity * 100f)}%");
        }

        _ecologyLabel.text = sb.ToString().TrimEnd('\n', '\r');
    }

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
        StartCoroutine(ConstructBuilding(_selectedCorpId, _activeBodyId, _currentTile.tileId, buildingType));
    }

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

        // ── 1. Fetch all corps for the dropdown & building panel ──────────
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
                    _cachedCorpItems = wrapper.items;   // cache for capital lookup
                    _corpIds.Clear();
                    foreach (var c in wrapper.items)
                        _corpIds.Add(c.id);

                    // Prefer the logged-in player's own corp over the first in the list
                    string _sessionCorp = PlayerSession.Instance?.CorpId ?? "";
                    _selectedCorpId = (!string.IsNullOrEmpty(_sessionCorp) && _corpIds.Contains(_sessionCorp))
                        ? _sessionCorp
                        : (_corpIds.Count > 0 ? _corpIds[0] : "");

                    // Populate the corporations list in the Résumé tab
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

        // ── 2. Fetch state vassal status ───────────────────────────────────
        string relationLabel = "";
        if (!string.IsNullOrEmpty(_currentTile.stateId))
        {
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
                    {
                        // Update badge with fetched state name
                        if (_btnTerritoryBadge != null)
                        {
                            string initials = GetInitials(stateDto.name ?? _currentTile.stateId);
                            _btnTerritoryBadge.text    = initials;
                            _btnTerritoryBadge.tooltip = stateDto.name ?? "Territoire";
                        }

                        if (stateDto.isVassal && !string.IsNullOrEmpty(stateDto.vassalCorpId))
                            relationLabel = $"Vassal de : {stateDto.vassalCorpId}";
                        else if (stateDto.isVassal)
                            relationLabel = !string.IsNullOrEmpty(stateDto.name) ? stateDto.name : "Vassal";
                        else
                            relationLabel = !string.IsNullOrEmpty(stateDto.name) ? stateDto.name : "État indépendant";

                        // Fill population tab
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
                }
            }
        }

        // ── 3. Refresh tile population (tile-centric) ──────────────────────
        if (!string.IsNullOrEmpty(_activeBodyId))
            yield return FetchTilePopulation(_activeBodyId, _currentTile.tileId);

        // ── 3b. Refresh territory queue (fixed section, above tabs) ────────
        if (!string.IsNullOrEmpty(_selectedCorpId) && !string.IsNullOrEmpty(_activeBodyId))
            yield return RefreshTerritoryQueue(_selectedCorpId, _activeBodyId, _currentTile.tileId);
        else
            RebuildQueueDisplay(null, null);

        // ── 4. Refresh buildings for selected corp on this tile ───────────
        if (!string.IsNullOrEmpty(_selectedCorpId))
            yield return RefreshBuildingsForTile(_selectedCorpId, _currentTile.tileId);
        else
            RebuildBuildingList(null);

        // ── 5. Refresh market tab (bio + local) ────────────────────────────
        yield return RefreshMarketData(_currentTile.tileId);
    }

    // ── Market DTOs ────────────────────────────────────────────────────────────

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

    // ── Market fetch + rebuild ──────────────────────────────────────────────────

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

    private static readonly Color _bioSparklineColor       = new Color(0.31f, 0.86f, 0.55f); // green
    private static readonly Color _financialSparklineColor = new Color(0.98f, 0.76f, 0.24f); // amber

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

    [System.Serializable]
    private class PopulationTierDto
    {
        public int   socialClass;  // 0=Poor 1=Middle 2=Rich
        public int   count;
        public float avgIncome;
    }

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

    [System.Serializable]
    private class PopulationTierListWrapper { public PopulationTierDto[] items; }

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

        // ── Capital ──
        if (_queueCorpCapitalLabel != null)
        {
            _queueCorpCapitalLabel.text = float.IsNaN(corpCredits) ? "—" : $"{corpCredits:N0} ¤";
        }

        // Find the first InProgress item for this territory (status == 1)
        ConstrItem activeItem = null;
        if (queue.items != null)
            foreach (var it in queue.items)
                if (it.status == 1) { activeItem = it; break; }

        float activePct = 0f;
        if (activeItem != null && activeItem.totalCostPts > 0)
            activePct = Mathf.Clamp01((float)activeItem.pointsAccumulated / activeItem.totalCostPts) * 100f;
        string activeName = activeItem != null ? GetBuildingTypeName(activeItem.buildingType) : "—";

        // Track État : powered by EB de fortune (population's natural capacity)
        bool etatActive = queue.isEBDeFortune && activeItem != null;
        if (_queueTrackEtatFill != null)
            _queueTrackEtatFill.style.width = new StyleLength(new Length(etatActive ? activePct : 0f, LengthUnit.Percent));
        if (_queueTrackEtatName != null)
            _queueTrackEtatName.text = etatActive ? activeName : "—";

        // Track Investisseur : powered by formal EB building (corpo / player)
        bool investorActive = !queue.isEBDeFortune && queue.constructionCapacity > 0f && activeItem != null;
        if (_queueTrackInvestorFill != null)
            _queueTrackInvestorFill.style.width = new StyleLength(new Length(investorActive ? activePct : 0f, LengthUnit.Percent));
        if (_queueTrackInvestorName != null)
            _queueTrackInvestorName.text = investorActive ? activeName : "—";

        // ── Pending items (this territory) ──
        if (_queuePendingContainer != null)
        {
            _queuePendingContainer.Clear();
            if (queue.items != null)
            {
                foreach (var item in queue.items)
                {
                    if (item.status != 0) continue; // Pending == 0
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
        }

        // ── All corp InProgress items ──
        if (_queueCorpAllContainer != null)
        {
            _queueCorpAllContainer.Clear();
            bool anyCorpItem = false;
            if (allCorpItems != null)
            {
                foreach (var item in allCorpItems)
                {
                    if (item.status != 1) continue; // InProgress only
                    anyCorpItem = true;
                    var row = new VisualElement();
                    row.AddToClassList("queue-pending-item");

                    var nameLabel = new Label(GetBuildingTypeName(item.buildingType));
                    nameLabel.AddToClassList("queue-item-name");
                    row.Add(nameLabel);

                    // Show tile short id
                    string shortTile = item.tileId != null && item.tileId.Length > 6
                        ? item.tileId.Substring(0, 6) + "…"
                        : (item.tileId ?? "?");
                    var tileLabel = new Label(shortTile);
                    tileLabel.AddToClassList("queue-item-ticks");
                    row.Add(tileLabel);

                    // Progress
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

    // ── DTO Classes ──────────────────────────────────────────────────────

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
    private class ConstrItemListDto
    {
        public ConstrItem[] items;
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