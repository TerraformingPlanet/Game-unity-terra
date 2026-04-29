using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the BottomActionBar UI component.
/// Handles tab navigation for Territory, Construction, Market, Contracts, Terraform.
/// Territory tab: regent-approval-gated actions (corrupt, charity, donation).
/// Construction tab: building type list + tile-placement mode.
/// </summary>
public class BottomActionBarController : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset bottomActionBarTemplate;

    // ── Tab bar ──────────────────────────────────────────────────────────────
    private VisualElement _bottomBar;
    private Button _tabTerritoire;
    private Button _tabConstruction;
    private Button _tabMarche;
    private Button _tabContrats;
    private Button _tabTerraform;
    private Label _corpStatusLabel;
    private int _activeTabIndex = -1;

    // ── Action panel (floats above the tab bar) ───────────────────────────────
    private VisualElement _actionPanel;
    private VisualElement _territoirePanel;
    private VisualElement _constructionPanel;

    // Territoire panel elements
    private Label  _territoireStatusLabel;
    private Button _btnCorruptRegent;
    private Button _btnFundCharity;
    private Button _btnDonation;

    // Construction panel elements
    private Label _constructionStatusLabel;

    // ── State ─────────────────────────────────────────────────────────────────
    private string _currentStateId   = "";
    private string _currentStateName = "";
    private bool   _isApproved       = false;   // TODO: fetch regent-approval from server
    private bool   _isPlacementMode  = false;
    private int    _pendingBuildingType = -1;

    /// <summary>True while the player has selected a building type and must click a tile.</summary>
    public bool IsPlacementMode => _isPlacementMode;

    private static readonly string[] _tabNames       = { "TERRITOIRE", "CONSTRUCTION", "MARCHÉ", "CONTRATS", "TERRAFORM" };
    private static readonly string[] _tabButtonNames  = { "tab-territoire", "tab-construction", "tab-marche", "tab-contrats", "tab-terraform" };
    private static readonly string[] _tabModifiers    = { "territoire", "construction", "marche", "contrats", "terraform" };

    private static readonly string[] _buildingTypeNames =
    {
        "Mine", "Ferme", "Centrale", "Recherche", "Route", "Port maritime", "Cosmodrome", "Scierie"
    };

    public void Initialize(VisualElement root, VisualTreeAsset injectedTemplate = null)
    {
        if (injectedTemplate != null) bottomActionBarTemplate = injectedTemplate;
        BuildBottomActionBar(root);
        BuildActionPanel(root);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Build UI
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildBottomActionBar(VisualElement root)
    {
        VisualTreeAsset asset = bottomActionBarTemplate
            ?? Resources.Load<VisualTreeAsset>("UI/Templates/BottomActionBar");

        if (asset != null)
        {
            asset.CloneTree(root);
            _bottomBar = root.Q<VisualElement>("bottom-action-bar");
        }

        if (_bottomBar == null)
        {
            _bottomBar = BuildBottomActionBarProcedural();
            root.Add(_bottomBar);
        }

        // Grab element refs
        _corpStatusLabel  = _bottomBar.Q<Label>("label-corp-status");
        _tabTerritoire    = _bottomBar.Q<Button>("tab-territoire");
        _tabConstruction  = _bottomBar.Q<Button>("tab-construction");
        _tabMarche        = _bottomBar.Q<Button>("tab-marche");
        _tabContrats      = _bottomBar.Q<Button>("tab-contrats");
        _tabTerraform     = _bottomBar.Q<Button>("tab-terraform");

        // Wire tab clicks
        Button[] tabs = { _tabTerritoire, _tabConstruction, _tabMarche, _tabContrats, _tabTerraform };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            tabs[i]?.RegisterCallback<ClickEvent>(_ => SetBottomTab(idx));
        }

        // Force critical layout styles inline (CloneTree does not apply <Style> src)
        _bottomBar.style.position         = Position.Absolute;
        _bottomBar.style.bottom           = new StyleLength(0f);
        _bottomBar.style.left             = new StyleLength(0f);
        _bottomBar.style.right            = new StyleLength(0f);
        _bottomBar.style.height           = new StyleLength(52f);
        _bottomBar.style.flexDirection    = FlexDirection.Row;
        _bottomBar.style.alignItems       = Align.Center;
        _bottomBar.style.justifyContent   = Justify.Center;
        _bottomBar.style.backgroundColor  = new StyleColor(new Color(0.031f, 0.031f, 0.055f, 0.92f));
        _bottomBar.style.borderTopWidth   = 1f;
        _bottomBar.style.borderTopColor   = new StyleColor(new Color(1f, 1f, 1f, 0.08f));

        _bottomBar.style.display = DisplayStyle.None;
    }

    private void BuildActionPanel(VisualElement root)
    {
        _actionPanel = new VisualElement { name = "bottom-action-panel" };
        _actionPanel.style.position        = Position.Absolute;
        _actionPanel.style.bottom          = new StyleLength(52f);
        _actionPanel.style.left            = new StyleLength(0f);
        _actionPanel.style.right           = new StyleLength(0f);
        _actionPanel.style.backgroundColor = new StyleColor(new Color(0.02f, 0.02f, 0.04f, 0.97f));
        _actionPanel.style.borderTopWidth  = 1f;
        _actionPanel.style.borderTopColor  = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
        _actionPanel.style.display         = DisplayStyle.None;

        _territoirePanel   = BuildTerritoirePanel();
        _constructionPanel = BuildConstructionPanel();
        _actionPanel.Add(_territoirePanel);
        _actionPanel.Add(_constructionPanel);

        root.Add(_actionPanel);
    }

    private VisualElement BuildTerritoirePanel()
    {
        var panel = new VisualElement { name = "territoire-action-panel" };
        panel.style.flexDirection  = FlexDirection.Column;
        panel.style.paddingLeft    = 16f;
        panel.style.paddingRight   = 16f;
        panel.style.paddingTop     = 10f;
        panel.style.paddingBottom  = 10f;
        panel.style.display        = DisplayStyle.None;

        _territoireStatusLabel = new Label { text = "Sélectionnez une tuile avec un territoire" };
        _territoireStatusLabel.style.fontSize     = 11;
        _territoireStatusLabel.style.color        = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
        _territoireStatusLabel.style.marginBottom = 8f;
        _territoireStatusLabel.style.whiteSpace   = WhiteSpace.Normal;
        panel.Add(_territoireStatusLabel);

        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.flexWrap     = Wrap.Wrap;

        _btnCorruptRegent = MakeActionButton("Corrompre le régent", OnCorruptRegent);
        _btnFundCharity   = MakeActionButton("Financer la charité",  OnFundCharity);
        _btnDonation      = MakeActionButton("Don aux collectivités", OnDonation);

        btnRow.Add(_btnCorruptRegent);
        btnRow.Add(_btnFundCharity);
        btnRow.Add(_btnDonation);
        panel.Add(btnRow);

        return panel;
    }

    private VisualElement BuildConstructionPanel()
    {
        var panel = new VisualElement { name = "construction-action-panel" };
        panel.style.flexDirection  = FlexDirection.Column;
        panel.style.paddingLeft    = 16f;
        panel.style.paddingRight   = 16f;
        panel.style.paddingTop     = 10f;
        panel.style.paddingBottom  = 10f;
        panel.style.display        = DisplayStyle.None;

        _constructionStatusLabel = new Label { text = "Sélectionnez un type de bâtiment, puis cliquez sur une tuile" };
        _constructionStatusLabel.style.fontSize     = 11;
        _constructionStatusLabel.style.color        = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
        _constructionStatusLabel.style.marginBottom = 8f;
        _constructionStatusLabel.style.whiteSpace   = WhiteSpace.Normal;
        panel.Add(_constructionStatusLabel);

        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.flexWrap     = Wrap.Wrap;

        for (int i = 0; i < _buildingTypeNames.Length; i++)
        {
            int buildType = i;
            var btn = MakeActionButton(_buildingTypeNames[i], () => OnSelectBuildingType(buildType));
            btn.name = $"btn-build-{buildType}";
            btnRow.Add(btn);
        }
        panel.Add(btnRow);

        return panel;
    }

    private static Button MakeActionButton(string label, System.Action onClick)
    {
        var btn = new Button { text = label };
        btn.AddToClassList("bottom-action-bar__action-btn");
        btn.style.marginRight        = 6f;
        btn.style.marginBottom       = 4f;
        btn.style.paddingLeft        = 10f;
        btn.style.paddingRight       = 10f;
        btn.style.paddingTop         = 5f;
        btn.style.paddingBottom      = 5f;
        btn.style.fontSize           = 11;
        btn.style.borderTopLeftRadius     = new StyleLength(3f);
        btn.style.borderTopRightRadius    = new StyleLength(3f);
        btn.style.borderBottomLeftRadius  = new StyleLength(3f);
        btn.style.borderBottomRightRadius = new StyleLength(3f);
        btn.style.backgroundColor    = new StyleColor(new Color(0.12f, 0.14f, 0.22f, 1f));
        btn.style.borderTopWidth     = 1f;
        btn.style.borderBottomWidth  = 1f;
        btn.style.borderLeftWidth    = 1f;
        btn.style.borderRightWidth   = 1f;
        btn.style.borderTopColor     = new StyleColor(new Color(1f, 1f, 1f, 0.12f));
        btn.style.borderBottomColor  = new StyleColor(new Color(1f, 1f, 1f, 0.12f));
        btn.style.borderLeftColor    = new StyleColor(new Color(1f, 1f, 1f, 0.12f));
        btn.style.borderRightColor   = new StyleColor(new Color(1f, 1f, 1f, 0.12f));
        btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
        return btn;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tab switching
    // ─────────────────────────────────────────────────────────────────────────

    private void SetBottomTab(int idx)
    {
        Button[] tabs = { _tabTerritoire, _tabConstruction, _tabMarche, _tabContrats, _tabTerraform };
        const string active = "bottom-action-bar__tab--active";

        bool wasActive  = (_activeTabIndex == idx);
        _activeTabIndex = wasActive ? -1 : idx;

        for (int i = 0; i < tabs.Length; i++)
            tabs[i]?.EnableInClassList(active, i == _activeTabIndex);

        // Cancel placement mode if leaving construction tab
        if (_activeTabIndex != 1 && _isPlacementMode)
            CancelPlacementMode();

        // Show/hide action panel and sub-panels
        bool showPanel = (_activeTabIndex == 0 || _activeTabIndex == 1);
        if (_actionPanel != null)
            _actionPanel.style.display = showPanel ? DisplayStyle.Flex : DisplayStyle.None;

        if (_territoirePanel != null)
            _territoirePanel.style.display = _activeTabIndex == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (_constructionPanel != null)
            _constructionPanel.style.display = _activeTabIndex == 1 ? DisplayStyle.Flex : DisplayStyle.None;

        if (_activeTabIndex == 0) RefreshTerritoirePanel();

        Debug.Log($"[BottomActionBarController] BottomTab → {(_activeTabIndex >= 0 && _activeTabIndex < _tabNames.Length ? _tabNames[_activeTabIndex] : "none")}");
    }

    public void SetActiveTab(int tabIndex) => SetBottomTab(tabIndex);

    // ─────────────────────────────────────────────────────────────────────────
    // Tile state
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Called by GameHUDController when a tile is resolved/selected.</summary>
    public void SetSelectedTile(GoldbergTileState tile)
    {
        _currentStateId   = tile.stateId   ?? "";
        _currentStateName = tile.stateName ?? _currentStateId;
        _isApproved       = false;   // TODO: fetch regent-approval status from server
        if (_activeTabIndex == 0) RefreshTerritoirePanel();
    }

    private void RefreshTerritoirePanel()
    {
        if (_territoireStatusLabel == null) return;

        if (string.IsNullOrEmpty(_currentStateId))
        {
            _territoireStatusLabel.text = "Sélectionnez une tuile avec un territoire";
            SetTerritoireActionsEnabled(false);
            return;
        }

        if (_isApproved)
        {
            _territoireStatusLabel.text = $"Territoire : {_currentStateName}  •  Aval du régent accordé";
            SetTerritoireActionsEnabled(true);
        }
        else
        {
            _territoireStatusLabel.text = $"Territoire : {_currentStateName}  •  En attente de l'aval du régent";
            SetTerritoireActionsEnabled(false);
        }
    }

    private void SetTerritoireActionsEnabled(bool enabled)
    {
        if (_btnCorruptRegent != null) _btnCorruptRegent.SetEnabled(enabled);
        if (_btnFundCharity   != null) _btnFundCharity.SetEnabled(enabled);
        if (_btnDonation      != null) _btnDonation.SetEnabled(enabled);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Territory action handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void OnCorruptRegent()
    {
        // TODO: send corrupt-regent request to server
        Debug.Log($"[BottomActionBarController] Corrompre régent — état {_currentStateId}");
    }

    private void OnFundCharity()
    {
        // TODO: send fund-charity request to server
        Debug.Log($"[BottomActionBarController] Financer la charité — état {_currentStateId}");
    }

    private void OnDonation()
    {
        // TODO: send donation request to server
        Debug.Log($"[BottomActionBarController] Don aux collectivités — état {_currentStateId}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construction placement
    // ─────────────────────────────────────────────────────────────────────────

    private void OnSelectBuildingType(int buildType)
    {
        _isPlacementMode     = true;
        _pendingBuildingType = buildType;
        string name = buildType < _buildingTypeNames.Length ? _buildingTypeNames[buildType] : buildType.ToString();
        if (_constructionStatusLabel != null)
            _constructionStatusLabel.text = $"Cliquez sur une tuile pour demander la construction : {name}";
        Debug.Log($"[BottomActionBarController] Mode placement → {name}");
    }

    private void CancelPlacementMode()
    {
        _isPlacementMode     = false;
        _pendingBuildingType = -1;
        if (_constructionStatusLabel != null)
            _constructionStatusLabel.text = "Sélectionnez un type de bâtiment, puis cliquez sur une tuile";
    }

    /// <summary>
    /// Called when a tile is clicked while in placement mode.
    /// Returns the building type to request, then exits placement mode.
    /// Returns -1 if not in placement mode.
    /// </summary>
    public int TryConsumePlacementBuildingType()
    {
        if (!_isPlacementMode) return -1;
        int bt = _pendingBuildingType;
        CancelPlacementMode();
        return bt;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Updates the corp status label in the bottom bar.</summary>
    public void SetCorpStatus(string corpName, float credits, int tileCount)
    {
        if (_corpStatusLabel == null) return;
        _corpStatusLabel.text = string.IsNullOrEmpty(corpName)
            ? ""
            : $"{corpName}   {credits:N0} ¢   {tileCount} tuiles";
    }

    public void SetVisible(bool visible)
    {
        if (_bottomBar != null)
            _bottomBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        if (!visible && _actionPanel != null)
            _actionPanel.style.display = DisplayStyle.None;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Procedural fallback
    // ─────────────────────────────────────────────────────────────────────────

    private VisualElement BuildBottomActionBarProcedural()
    {
        var bar = new VisualElement { name = "bottom-action-bar" };
        bar.AddToClassList("bottom-action-bar");

        _corpStatusLabel = new Label { name = "label-corp-status", text = "" };
        _corpStatusLabel.AddToClassList("bottom-action-bar__status");
        _corpStatusLabel.AddToClassList("hud-label--secondary");
        bar.Add(_corpStatusLabel);

        Button[] refs = new Button[5];
        for (int i = 0; i < _tabNames.Length; i++)
        {
            var btn = new Button { name = _tabButtonNames[i], text = _tabNames[i] };
            btn.AddToClassList("bottom-action-bar__tab");
            btn.AddToClassList($"bottom-action-bar__tab--{_tabModifiers[i]}");
            bar.Add(btn);
            refs[i] = btn;
        }

        _tabTerritoire   = refs[0];
        _tabConstruction = refs[1];
        _tabMarche       = refs[2];
        _tabContrats     = refs[3];
        _tabTerraform    = refs[4];

        return bar;
    }
}
