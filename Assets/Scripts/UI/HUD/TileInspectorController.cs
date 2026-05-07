using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the TileInspector UI component — core: fields, wiring, show/hide, tabs, display.
/// Domain logic split across partial class files:
///   .State.cs · .Population.cs · .Market.cs · .Queue.cs · .Buildings.cs
/// </summary>
public partial class TileInspectorController : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset tileInspectorTemplate;
    [SerializeField] private StyleSheet variablesStyleSheet;
    [SerializeField] private StyleSheet baseStyleSheet;

    private VisualElement _tileInspector;
    private VisualElement _hudRoot;          // root HUD container (for floating panels)
    private GoldbergTileState _currentTile;
    private bool _hasTile;

    // UI Elements - Header
    private Label _tileHeaderLabel;
    private Button _btnTerritoryBadge;   // ← badge with state initials in header

    // Zone dimension badges (6 dimensions) — created code-driven after template clone
    private VisualElement _zoneBadgesContainer;
    private Button[] _zoneBadges; // index: 0=Bio, 1=Admin, 2=Éco, 3=Mil, 4=Cult, 5=Sci
    private static readonly string[] _zoneDimensions   = { "bio", "admin", "eco", "military", "cultural", "scientific" };
    private static readonly string[] _zoneBadgeLabels  = { "BIO", "ADM", "ÉCO", "MIL", "CULT", "SCI" };
    private static readonly Color[]  _zoneBadgeColors  = {
        new Color(0.20f, 0.75f, 0.25f, 1f), // vert     bio
        new Color(0.25f, 0.50f, 0.90f, 1f), // bleu     admin
        new Color(0.85f, 0.72f, 0.10f, 1f), // jaune    eco
        new Color(0.85f, 0.20f, 0.20f, 1f), // rouge    military
        new Color(0.65f, 0.20f, 0.85f, 1f), // violet   cultural
        new Color(0.15f, 0.80f, 0.90f, 1f), // cyan     scientific
    };

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
    private Label         _queueTrackEtatSlots;
    private Label         _queueTrackInvestorSlots;

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
    private string _displayedStateId = "";           // stateId whose queue is currently shown
    private GameHUDController _gameHUDController;

    public void Initialize(VisualElement root, VisualTreeAsset injectedTemplate = null)
    {
        if (injectedTemplate != null) tileInspectorTemplate = injectedTemplate;
        _gameHUDController = GetComponent<GameHUDController>();
        BuildTileInspector(root);
    }

    private void BuildTileInspector(VisualElement root)
    {
        _hudRoot = root;
        VisualTreeAsset asset = tileInspectorTemplate
            ?? Resources.Load<VisualTreeAsset>("UI/Templates/TileInspector");

        if (asset == null)
        {
            Debug.LogWarning("[TileInspectorController] TileInspector template not found.");
            return;
        }

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

        // Zone badges — inject code-driven container after territory badge
        BuildZoneBadges();

        // Territory queue section — reparented to HUD root as floating right panel
        BindQueueElements();
        SetupQueueSectionFloating(root);

        BindTabAndSectionElements(_tileInspector);
        WireTileInspectorCallbacks(_tileInspector);

        SwitchInspectorTab(0);
        _tileInspector.style.display = DisplayStyle.None;
    }

    private void BindQueueElements()
    {
        _queueSection             = _tileInspector.Q<VisualElement>("territory-queue-section");
        _queuePendingContainer    = _tileInspector.Q<VisualElement>("queue-pending-container");
        _queueCorpAllContainer    = _tileInspector.Q<VisualElement>("queue-corp-all-container");
        _queueCorpAllTitle        = _tileInspector.Q<Label>("queue-corp-all-title");
        _queueCorpCapitalLabel    = _tileInspector.Q<Label>("queue-corp-capital-label");
        _queueTrackEtatFill       = _tileInspector.Q<VisualElement>("queue-track-etat-fill");
        _queueTrackInvestorFill   = _tileInspector.Q<VisualElement>("queue-track-investor-fill");
        _queueTrackEtatName       = _tileInspector.Q<Label>("queue-track-etat-name");
        _queueTrackInvestorName   = _tileInspector.Q<Label>("queue-track-investor-name");
        _queueTrackEtatSlots      = _tileInspector.Q<Label>("queue-track-etat-slots");
        _queueTrackInvestorSlots  = _tileInspector.Q<Label>("queue-track-investor-slots");
    }

    private void BuildZoneBadges()
    {
        // Find the header container (parent of btn-territory-badge)
        VisualElement headerRow = _btnTerritoryBadge?.parent ?? _tileInspector.Q<VisualElement>("tile-header");
        if (headerRow == null) return;

        _zoneBadgesContainer = new VisualElement();
        _zoneBadgesContainer.name = "zone-badges-container";
        _zoneBadgesContainer.style.flexDirection = FlexDirection.Row;
        _zoneBadgesContainer.style.flexWrap = Wrap.Wrap;
        _zoneBadgesContainer.style.marginTop = 4f;
        _zoneBadgesContainer.style.display = DisplayStyle.None;
        headerRow.Add(_zoneBadgesContainer);

        _zoneBadges = new Button[_zoneDimensions.Length];
        for (int i = 0; i < _zoneDimensions.Length; i++)
        {
            int dimIndex = i;
            var btn = new Button();
            btn.name = $"zone-badge-{_zoneDimensions[i]}";
            btn.text = _zoneBadgeLabels[i];
            btn.AddToClassList("zone-badge");
            btn.style.backgroundColor = _zoneBadgeColors[i];
            btn.style.color = Color.white;
            btn.style.fontSize = 9;
            btn.style.paddingLeft = 4f; btn.style.paddingRight = 4f;
            btn.style.paddingTop = 2f; btn.style.paddingBottom = 2f;
            btn.style.marginRight = 2f;
            btn.style.borderTopLeftRadius = 3f; btn.style.borderTopRightRadius = 3f;
            btn.style.borderBottomLeftRadius = 3f; btn.style.borderBottomRightRadius = 3f;
            btn.style.display = DisplayStyle.None;
            btn.RegisterCallback<ClickEvent>(_ => OnZoneBadgeClicked(dimIndex));
            _zoneBadgesContainer.Add(btn);
            _zoneBadges[i] = btn;
        }
    }

    private void UpdateZoneBadges()
    {
        if (_zoneBadges == null || string.IsNullOrEmpty(_currentTile.tileId)) return;

        string[] zoneIds = {
            _currentTile.bioZoneId,
            _currentTile.adminZoneId,
            _currentTile.ecoZoneId,
            _currentTile.militaryZoneId,
            _currentTile.culturalZoneId,
            _currentTile.scientificZoneId,
        };

        bool anyVisible = false;
        for (int i = 0; i < _zoneBadges.Length; i++)
        {
            bool hasZone = !string.IsNullOrEmpty(zoneIds[i]);
            _zoneBadges[i].style.display = hasZone ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasZone)
            {
                // Show short form of zone ID (last 4 chars) as tooltip-like text
                string shortId = zoneIds[i].Length > 4 ? zoneIds[i][^4..] : zoneIds[i];
                _zoneBadges[i].tooltip = $"{_zoneDimensions[i]}: {zoneIds[i]}";
                anyVisible = true;
            }
        }
        if (_zoneBadgesContainer != null)
            _zoneBadgesContainer.style.display = anyVisible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnZoneBadgeClicked(int dimensionIndex)
    {
        if (dimensionIndex < 0 || dimensionIndex >= _zoneDimensions.Length) return;
        string dimension = _zoneDimensions[dimensionIndex];
        // Switch planet globe to zone lens for this dimension
        var globe = FindAnyObjectByType<PlanetSphereGoldberg>();
        globe?.SetZoneLens(dimension);
    }

    private void SetupQueueSectionFloating(VisualElement root)
    {
        if (_queueSection == null) return;
        // Extract from tile-inspector and float on the right side, below time controls
        _queueSection.RemoveFromHierarchy();
        _queueSection.AddToClassList("territory-queue-section--floating");
        // Inline fallback: position absolute, top-right below time-controls-bar
        _queueSection.style.position         = Position.Absolute;
        _queueSection.style.right            = new StyleLength(12f);
        _queueSection.style.top              = new StyleLength(104f);
        _queueSection.style.width            = new StyleLength(260f);
        _queueSection.style.backgroundColor  = new StyleColor(new Color(0.031f, 0.031f, 0.055f, 0.92f));
        var borderCol = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
        _queueSection.style.borderTopColor    = borderCol;
        _queueSection.style.borderRightColor  = borderCol;
        _queueSection.style.borderBottomColor = borderCol;
        _queueSection.style.borderLeftColor   = borderCol;
        _queueSection.style.borderTopWidth    = 1f;
        _queueSection.style.borderRightWidth  = 1f;
        _queueSection.style.borderBottomWidth = 1f;
        _queueSection.style.borderLeftWidth   = 1f;
        _queueSection.style.borderTopLeftRadius     = 4f;
        _queueSection.style.borderTopRightRadius    = 4f;
        _queueSection.style.borderBottomLeftRadius  = 4f;
        _queueSection.style.borderBottomRightRadius = 4f;
        root.Add(_queueSection);
        _queueSection.style.display = DisplayStyle.None;
    }

    private void BindTabAndSectionElements(VisualElement inspector)
    {
        _corpListContainer       = inspector.Q<VisualElement>("corp-list-container");
        _ecologyLabel             = inspector.Q<Label>("ecology-label");
        _marketBioContainer       = inspector.Q<VisualElement>("market-bio-container");
        _marketFinancialContainer = inspector.Q<VisualElement>("market-financial-container");
        _publicContractsContainer = inspector.Q<VisualElement>("public-contracts-container");
        _myContractsContainer    = inspector.Q<VisualElement>("my-contracts-container");

        _tabButtons = new Button[]
        {
            inspector.Q<Button>("tab-resume"),
            inspector.Q<Button>("tab-population"),
            inspector.Q<Button>("tab-batiment"),
            inspector.Q<Button>("tab-marche"),
            inspector.Q<Button>("tab-contrats")
        };
        _tabContents = new VisualElement[]
        {
            inspector.Q<VisualElement>("tab-content-resume"),
            inspector.Q<VisualElement>("tab-content-population"),
            inspector.Q<VisualElement>("tab-content-batiment"),
            inspector.Q<VisualElement>("tab-content-marche"),
            inspector.Q<VisualElement>("tab-content-contrats")
        };

        _popSummaryLabel = inspector.Q<Label>("pop-summary-label");
        _territoryLabel  = inspector.Q<Label>("territory-label");
        _popPoorLabel    = inspector.Q<Label>("pop-poor-label");
        _popMiddleLabel  = inspector.Q<Label>("pop-middle-label");
        _popRichLabel    = inspector.Q<Label>("pop-rich-label");
        _popTotalLabel   = inspector.Q<Label>("pop-total-label");

        _dropdownBuildType = inspector.Q<DropdownField>("dropdown-build-type");
        _btnConstruct      = inspector.Q<Button>("btn-construct");
        _constructionQueueContainer = inspector.Q<VisualElement>("construction-queue-container");

        InitBuildTypeDropdown();
    }

    private void InitBuildTypeDropdown()
    {
        if (_dropdownBuildType != null)
        {
            _dropdownBuildType.choices = new List<string>
            {
                "Mine", "Farm", "EnergyPlant", "Research", "Road", "SeaPort", "Spaceport", "Sawmill"
            };
            _dropdownBuildType.index = 0;
        }
    }

    private void WireTileInspectorCallbacks(VisualElement inspector)
    {
        // Wire tab buttons
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            int tabIndex = i; // capture for closure
            _tabButtons[i]?.RegisterCallback<ClickEvent>(_ => SwitchInspectorTab(tabIndex));
        }

        // Wire buttons
        _btnTerritoryBadge?.RegisterCallback<ClickEvent>(_ => OnBadgeClicked());
        _btnConstruct?.RegisterCallback<ClickEvent>(_ => OnConstructButtonClicked());
        inspector.Q<Button>("btn-close-inspector")
            ?.RegisterCallback<ClickEvent>(_ => Hide());

        // ── Bouton copie tileId (debug) ────────────────────────────────
        var header = inspector.Q<VisualElement>(className: "tile-inspector__header");
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
        bool sameTerritory = !string.IsNullOrEmpty(tile.stateId)
                          && tile.stateId == _displayedStateId;

        _currentTile = tile;
        _hasTile = true;
        if (!string.IsNullOrEmpty(bodyId))
            _activeBodyId = bodyId;
        if (_tileInspector != null)
        {
            _tileInspector.style.display = DisplayStyle.Flex;
            if (sameTerritory)
                UpdateTileDisplaySameTerritory(); // only header + terrain + ecology, queue stays
            else
                UpdateTileDisplay();              // full refresh including queue
        }
    }

    public void Hide()
    {
        if (_tileInspector != null)
            _tileInspector.style.display = DisplayStyle.None;
        if (_queueSection != null)
            _queueSection.style.display = DisplayStyle.None;
        _displayedStateId = "";
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

        UpdateZoneBadges();
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

        // Full refresh — state changed
        _displayedStateId = ""; // reset so coroutine sets it after fetch
        StartCoroutine(RefreshStateRelationForTile());
    }

    /// <summary>Light refresh for same-territory tile click: updates tile-specific info
    /// but leaves the territory queue panel untouched.</summary>
    private void UpdateTileDisplaySameTerritory()
    {
        if (!_hasTile || _tileInspector == null) return;

        if (_tileHeaderLabel != null)
            _tileHeaderLabel.text = $"Tuile {_currentTile.tileId}";

        if (_btnTerritoryBadge != null)
        {
            string initials = GetInitials(_currentTile.stateName ?? _currentTile.stateId);
            _btnTerritoryBadge.text    = initials;
            _btnTerritoryBadge.tooltip = _currentTile.stateName ?? _currentTile.stateId ?? "Territoire";
            _btnTerritoryBadge.style.display = string.IsNullOrEmpty(_currentTile.stateId)
                ? DisplayStyle.None : DisplayStyle.Flex;
        }

        UpdateZoneBadges();

        if (_terrainInfoLabel != null)
        {
            string terrainText = $"Terrain: {_currentTile.terrainType}\n";
            terrainText += $"Température: {_currentTile.temperature:F1}°C\n";
            terrainText += $"Eau: {_currentTile.waterRatio:F2}\n";
            terrainText += $"Habitable: {(_currentTile.isHabitable ? "Oui" : "Non")}";
            _terrainInfoLabel.text = terrainText;
        }

        UpdateEcologyDisplay();
        // No StartCoroutine(RefreshStateRelationForTile()) — queue stays as-is
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
                    _                    => "Lac"
                };
                break;
            case WaterClassification.InlandSea:
                waterFeature = "Mer intérieure";
                break;
        }
        if (waterFeature != null)
            sb.AppendLine(waterFeature);

        AppendSpeciesDisplay(sb);

        _ecologyLabel.text = sb.ToString().TrimEnd('\n', '\r');
    }

    private void AppendSpeciesDisplay(System.Text.StringBuilder sb)
    {
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
    }

}