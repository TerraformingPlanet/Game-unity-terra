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
    private VisualElement _corpBadgeEl;
    private Label _corpOwnerLabelEl;
    private DropdownField _corpDropdown;
    private TextField _corpNameInput;
    private Button _btnCreateCorp;

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

    // UI Elements - Building Tab
    private DropdownField _dropdownBuildType;
    private Button _btnConstruct;
    private VisualElement _buildingListContainer;
    private VisualElement _constructionQueueContainer;

    // UI Elements - Additional Sections
    private VisualElement _corpListContainer;
    private Label _nationalisationLabel;
    private Button _btnCorrupt;
    private Button _btnCancelNationalisation;
    private Label _ecologyLabel;
    private VisualElement _marketBioContainer;
    private VisualElement _marketFinancialContainer;
    private VisualElement _publicContractsContainer;
    private VisualElement _myContractsContainer;

    // Data
    private List<string> _corpIds = new List<string>();
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
        _corpBadgeEl             = _tileInspector.Q<VisualElement>("corp-badge");
        _corpOwnerLabelEl        = _tileInspector.Q<Label>("corp-owner-label");
        _corpDropdown            = _tileInspector.Q<DropdownField>("corp-dropdown");
        _corpNameInput           = _tileInspector.Q<TextField>("corp-name-input");
        _btnCreateCorp           = _tileInspector.Q<Button>("btn-create-corp");
        _buildingListContainer   = _tileInspector.Q<VisualElement>("building-list-container");
        _tileStatusLabel         = _tileInspector.Q<Label>("tile-status-label");
        _terrainInfoLabel        = _tileInspector.Q<Label>("terrain-info-label");

        // New elements for additional sections
        _corpListContainer       = _tileInspector.Q<VisualElement>("corp-list-container");
        _nationalisationLabel    = _tileInspector.Q<Label>("nationalisation-label");
        _btnCorrupt              = _tileInspector.Q<Button>("btn-corrupt");
        _btnCancelNationalisation = _tileInspector.Q<Button>("btn-cancel-nationalisation");
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
        _btnCreateCorp?.RegisterCallback<ClickEvent>(_ => OnInspectorCreateCorpClicked());
        _btnConstruct?.RegisterCallback<ClickEvent>(_ => OnConstructButtonClicked());
        _btnCorrupt?.RegisterCallback<ClickEvent>(_ => OnCorruptClicked());
        _btnCancelNationalisation?.RegisterCallback<ClickEvent>(_ => OnCancelNationalisationClicked());
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

    public void ShowTile(GoldbergTileState tile)
    {
        _currentTile = tile;
        _hasTile = true;
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

        // Refresh corps and state data
        StartCoroutine(RefreshStateRelationForTile());
    }

    private void OnConstructButtonClicked()
    {
        if (_dropdownBuildType == null || _corpDropdown == null || string.IsNullOrEmpty(_currentTile.tileId)) return;

        string selectedCorpId = _corpIds.Count > 0 && _corpDropdown.index >= 0 ? _corpIds[_corpDropdown.index] : null;
        if (string.IsNullOrEmpty(selectedCorpId)) return;

        int buildingType = _dropdownBuildType.index;
        StartCoroutine(ConstructBuilding(selectedCorpId, _currentTile.tileId, buildingType));
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

    private void OnInspectorCreateCorpClicked()
    {
        // TODO: Implement corp creation
        Debug.Log("[TileInspector] Create corp clicked");
    }

    private void OnCorruptClicked()
    {
        // TODO: Implement corruption
        Debug.Log("[TileInspector] Corrupt clicked");
    }

    private void OnCancelNationalisationClicked()
    {
        // TODO: Implement cancel nationalisation
        Debug.Log("[TileInspector] Cancel nationalisation clicked");
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
                    _corpIds.Clear();
                    var choices = new List<string>();
                    foreach (var c in wrapper.items)
                    {
                        _corpIds.Add(c.id);
                        choices.Add(c.name);
                    }
                    if (_corpDropdown != null)
                    {
                        _corpDropdown.choices = choices;
                        if (_corpDropdown.index < 0 || _corpDropdown.index >= choices.Count)
                            _corpDropdown.index = 0;
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

        if (_corpOwnerLabelEl != null)
            _corpOwnerLabelEl.text = !string.IsNullOrEmpty(relationLabel) ? relationLabel : "—";
        if (_corpBadgeEl != null)
            _corpBadgeEl.style.backgroundColor = Color.clear;

        // ── 3. Refresh buildings for selected corp on this tile ───────────
        string selectedCorpId = _corpIds.Count > 0 && _corpDropdown != null && _corpDropdown.index >= 0
            ? _corpIds[_corpDropdown.index]
            : null;
        if (selectedCorpId != null)
            yield return RefreshBuildingsForTile(selectedCorpId, _currentTile.tileId);
        else
            RebuildBuildingList(null, null);
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

    private IEnumerator RefreshBuildingsForTile(string corpId, string tileId)
    {
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/buildings";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                RebuildBuildingList(null, null);
                yield break;
            }

            BuildingListDto wrapper;
            try { wrapper = JsonUtility.FromJson<BuildingListDto>("{\"items\":" + req.downloadHandler.text + "}"); }
            catch { RebuildBuildingList(null, null); yield break; }

            var tileBuildings = new List<BuildingItem>();
            if (wrapper?.items != null)
                foreach (var b in wrapper.items)
                    if (b.tileId == tileId) tileBuildings.Add(b);

            // Fetch construction queue
            var qUrl = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/construction-queue";
            var tileConstr = new List<ConstrItem>();
            using (var qreq = UnityWebRequest.Get(qUrl))
            {
                qreq.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
                yield return qreq.SendWebRequest();

                if (qreq.result == UnityWebRequest.Result.Success)
                {
                    ConstrListDto qWrapper;
                    try { qWrapper = JsonUtility.FromJson<ConstrListDto>("{\"items\":" + qreq.downloadHandler.text + "}"); }
                    catch { qWrapper = null; }

                    if (qWrapper?.items != null)
                        foreach (var c in qWrapper.items)
                            if (c.tileId == tileId) tileConstr.Add(c);
                }
            }

            RebuildBuildingList(tileBuildings, tileConstr);
        }
    }

    private void RebuildBuildingList(List<BuildingItem> buildings, List<ConstrItem> constructions)
    {
        if (_buildingListContainer == null) return;

        _buildingListContainer.Clear();

        // Add buildings
        if (buildings != null)
        {
            foreach (var b in buildings)
            {
                var item = new VisualElement();
                item.AddToClassList("building-item");

                var label = new Label($"{GetBuildingTypeName(b.buildingType)} (Niv.{b.level}) - Prod:{b.production:F1}");
                item.Add(label);

                var demolishBtn = new Button { text = "Démolir" };
                demolishBtn.clicked += () => StartCoroutine(DoDemolishBuilding(_corpIds[_corpDropdown.index], b.id));
                item.Add(demolishBtn);

                _buildingListContainer.Add(item);
            }
        }

        // Add construction queue
        if (constructions != null && _constructionQueueContainer != null)
        {
            _constructionQueueContainer.Clear();
            foreach (var c in constructions)
            {
                var item = new VisualElement();
                item.AddToClassList("construction-item");

                var label = new Label($"{GetBuildingTypeName(c.buildingType)} - {c.remainingTicks} ticks restants");
                item.Add(label);

                _constructionQueueContainer.Add(item);
            }
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

    private IEnumerator ConstructBuilding(string corpId, string tileId, int buildingType)
    {
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/game/corporations/{corpId}/buildings";
        var form = new WWWForm();
        form.AddField("building_type", buildingType);
        form.AddField("tile_id", tileId);
        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                if (_tileStatusLabel != null) _tileStatusLabel.text = "Construction enqueued.";
                yield return RefreshBuildingsForTile(corpId, tileId);
            }
            else
            {
                if (_tileStatusLabel != null) _tileStatusLabel.text = $"Erreur construction: {req.error}";
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
        public int buildingType;
        public int remainingTicks;
    }

    [System.Serializable]
    private class ConstrListDto
    {
        public ConstrItem[] items;
    }
}