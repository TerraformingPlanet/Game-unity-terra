using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit entry-point for the Terraformation HUD.
/// Mirrors the SerializeField surface of GameHUD.cs so both components can
/// live on the same (or a sibling) GameObject during the phased migration.
///
/// Phase 0 — scaffolding, event wiring.
/// Phase 1 — TopBar (this file).
/// Phase 2+ — LeftPanel, RightPanel, EventFeed, DebugDrawer (future).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class GameHUDController : MonoBehaviour
{
    // =========================================================
    // Inspector — identical surface to GameHUD.cs
    // =========================================================

    [Header("Références")]
    [SerializeField] private ViewManager          viewManager;
    [SerializeField] private TerraformHUD         terraformHUD;
    [SerializeField] private PlanetSphereGoldberg planetSphere;
    [SerializeField] private DebugHydrologyPanel  debugHydrologyPanel;

    [Header("Serveur")]
    [SerializeField] private string simulationServerUrl            = "http://127.0.0.1:8080";
    [SerializeField] private float  simulationServerTimeoutSeconds = 5f;

    [Header("Icônes UI")]
    [SerializeField] private bool   useFontAwesomeBuildingIcons    = true;
    [SerializeField] private string fontAwesomeSolidResourcePath   = "Fonts/Font Awesome 7 Free-Solid-900";

    [Header("UXML Templates")]
    [SerializeField] private VisualTreeAsset topBarTemplate;
    [SerializeField] private VisualTreeAsset leftPanelTemplate;

    [Header("Global StyleSheets")]
    [SerializeField] private StyleSheet variablesStyleSheet;
    [SerializeField] private StyleSheet baseStyleSheet;

    // =========================================================
    // Runtime
    // =========================================================

    private UIDocument    _doc;
    private VisualElement _root;

    // Current view state
    private ViewManager.ViewState _viewState = ViewManager.ViewState.Galaxy;

    // Cached tile
    private GoldbergTileState _currentTile;

    // ── Phase 1: TopBar element references ──────────────────────────
    private VisualElement _topBar;
    private Button        _btnBack;
    private Label         _labelPlanet;
    private Label         _labelTickCredits;
    private Button        _btnToggleView;
    private Button        _btnDebug;

    // Last known tick / credits for TopBar label
    private int   _lastKnownTick       = -1;
    private float _selectedCorpCredits = float.NaN;

    // ── Phase 2: LeftPanel element references ────────────────────────
    private VisualElement _leftPanel;
    private VisualElement _progressFill;
    private Label         _progressLabel;
    private Label         _atmoLabel;
    private Label         _scoreboardLabel;
    private bool          _leftPanelVisible;

    // ── Phase 4: Tooltip ─────────────────────────────────────────────
    private VisualElement _tooltip;
    private Label         _tooltipLabel;

    // ── Phase 4: EventFeed / Journal ─────────────────────────────────
    private VisualElement _eventFeed;
    private VisualElement _eventFeedList;        // Onglet ÉVÉNEMENTS
    private VisualElement _eventFeedListActions; // Onglet ACTIONS
    private int           _activeEventTab = 0;   // 0 = events, 1 = actions
    private const int     MaxFeedEntries = 8;

    // =========================================================
    // Lifecycle
    // =========================================================

    private void Awake()
    {
        _doc = GetComponent<UIDocument>();
        _root = new VisualElement { name = "hud-root" };
        _root.style.width    = new StyleLength(Length.Percent(100));
        _root.style.height   = new StyleLength(Length.Percent(100));
        _root.style.position = Position.Absolute;
        _root.pickingMode    = PickingMode.Ignore;

        // Apply global stylesheets so CSS variables resolve for all children,
        // including elements created programmatically (not via UXML templates).
        if (variablesStyleSheet != null) _root.styleSheets.Add(variablesStyleSheet);
        else Debug.LogWarning("[GameHUDController] variablesStyleSheet not assigned.");
        if (baseStyleSheet != null) _root.styleSheets.Add(baseStyleSheet);
        else Debug.LogWarning("[GameHUDController] baseStyleSheet not assigned.");

        if (_doc.rootVisualElement != null)
            _doc.rootVisualElement.Add(_root);
        else
            Debug.LogWarning("[GameHUDController] UIDocument.rootVisualElement null in Awake.");
    }

    private void Start()
    {
        if (viewManager         == null) viewManager         = FindAnyObjectByType<ViewManager>(FindObjectsInactive.Include);
        if (terraformHUD        == null) terraformHUD        = FindAnyObjectByType<TerraformHUD>(FindObjectsInactive.Include);
        if (planetSphere        == null) planetSphere        = FindAnyObjectByType<PlanetSphereGoldberg>(FindObjectsInactive.Include);
        if (debugHydrologyPanel == null) debugHydrologyPanel = FindAnyObjectByType<DebugHydrologyPanel>(FindObjectsInactive.Include);

        if (_doc.rootVisualElement != null && !_doc.rootVisualElement.Contains(_root))
            _doc.rootVisualElement.Add(_root);

        // ── Phase 1: Build TopBar ────────────────────────────────────
        BuildTopBar();

        // ── Phase 2: Build LeftPanel ─────────────────────────────────
        BuildLeftPanel();
        StartCoroutine(PollScoreboard());

        // ── Phase 3: Build TileInspector ─────────────────────────────
        BuildTileInspector();

        // ── Phase 4: Build Tooltip + EventFeed ───────────────────────
        BuildTooltip();
        BuildEventFeed();
        StartCoroutine(PollEventFeed());

        // ── Events ───────────────────────────────────────────────────
        ViewManager.OnViewChanged += OnViewChanged;

        if (planetSphere != null)
        {
            planetSphere.OnH3TileResolved     += OnTileResolved;
            planetSphere.OnTileHoverReady     += OnTileHoverReady;
            planetSphere.OnTileHoverCancelled += OnTileHoverCancelled;
        }
        else
        {
            Debug.LogWarning("[GameHUDController] planetSphere NULL — tile events not connected.");
        }

        if (terraformHUD != null)
        {
            terraformHUD.OnProgressUpdated    += OnProgressUpdated;
            terraformHUD.OnRegionStateChanged += OnRegionStateChanged;
        }

        OnViewChanged(_viewState);

        Debug.Log($"[GameHUDController] Ready. planetSphere={(planetSphere != null ? planetSphere.name : "NULL")}");
    }

    private void OnDestroy()
    {
        ViewManager.OnViewChanged -= OnViewChanged;

        if (planetSphere != null)
        {
            planetSphere.OnH3TileResolved     -= OnTileResolved;
            planetSphere.OnTileHoverReady     -= OnTileHoverReady;
            planetSphere.OnTileHoverCancelled -= OnTileHoverCancelled;
        }

        if (terraformHUD != null)
        {
            terraformHUD.OnProgressUpdated    -= OnProgressUpdated;
            terraformHUD.OnRegionStateChanged -= OnRegionStateChanged;
        }
    }

    // =========================================================
    // Phase 1 — TopBar
    // =========================================================

    private void BuildTopBar()
    {
        // Prefer UXML template if wired in Inspector; fall back to procedural.
        if (topBarTemplate != null)
        {
            _topBar = topBarTemplate.Instantiate();
        }
        else
        {
            // Load from Resources-style path as fallback
            var asset = Resources.Load<VisualTreeAsset>("UI/Templates/TopBar");
            _topBar = asset != null ? asset.Instantiate() : BuildTopBarProcedural();
        }

        _root.Add(_topBar);

        // Grab named elements
        _btnBack          = _topBar.Q<Button>("btn-back");
        _labelPlanet      = _topBar.Q<Label>("label-planet");
        _labelTickCredits = _topBar.Q<Label>("label-tick-credits");
        _btnToggleView    = _topBar.Q<Button>("btn-toggle-view");
        _btnDebug         = _topBar.Q<Button>("btn-debug");

        // Wire button callbacks
        _btnBack?.RegisterCallback<ClickEvent>(_ => viewManager?.GoBackOneLevel());
        _btnToggleView?.RegisterCallback<ClickEvent>(_ => viewManager?.TogglePlanetView());
        _btnDebug?.RegisterCallback<ClickEvent>(_ =>
        {
            // Phase 5 will wire Debug drawer; for now log.
            Debug.Log("[GameHUDController] Debug button clicked.");
        });
    }

    /// <summary>Procedural fallback: builds TopBar in code without UXML.</summary>
    private VisualElement BuildTopBarProcedural()
    {
        var bar = new VisualElement { name = "top-bar" };
        bar.AddToClassList("top-bar");

        _btnBack = new Button { name = "btn-back", text = "← Retour" };
        _btnBack.AddToClassList("hud-btn");
        _btnBack.AddToClassList("top-bar__btn");
        bar.Add(_btnBack);

        _labelPlanet = new Label { name = "label-planet", text = "Planète" };
        _labelPlanet.AddToClassList("hud-label");
        _labelPlanet.AddToClassList("hud-label--title");
        _labelPlanet.AddToClassList("top-bar__planet");
        bar.Add(_labelPlanet);

        _labelTickCredits = new Label { name = "label-tick-credits", text = "Tick —" };
        _labelTickCredits.AddToClassList("hud-label");
        _labelTickCredits.AddToClassList("hud-label--secondary");
        _labelTickCredits.AddToClassList("top-bar__tick");
        bar.Add(_labelTickCredits);

        _btnToggleView = new Button { name = "btn-toggle-view", text = "Vue Carte" };
        _btnToggleView.AddToClassList("hud-btn");
        _btnToggleView.AddToClassList("hud-btn--build");
        _btnToggleView.AddToClassList("top-bar__btn");
        bar.Add(_btnToggleView);

        _btnDebug = new Button { name = "btn-debug", text = "Debug" };
        _btnDebug.AddToClassList("hud-btn");
        _btnDebug.AddToClassList("top-bar__btn");
        bar.Add(_btnDebug);

        return bar;
    }

    /// <summary>Updates the TopBar planet label.</summary>
    public void SetPlanetName(string name)
    {
        if (_labelPlanet != null) _labelPlanet.text = name;
    }

    /// <summary>Updates tick + credits label in TopBar.</summary>
    public void SetTickCredits(int tick, float credits)
    {
        _lastKnownTick       = tick;
        _selectedCorpCredits = credits;
        if (_labelTickCredits == null) return;
        string credStr = float.IsNaN(credits) ? "—" : credits.ToString("N0") + " ¢";
        _labelTickCredits.text = $"Tick {tick} | {credStr}";
    }

    /// <summary>Updates the Globe/Carte toggle button label.</summary>
    public void SetToggleViewLabel(string label)
    {
        if (_btnToggleView != null) _btnToggleView.text = label;
    }

    // =========================================================
    // Event handlers
    // =========================================================

    private void OnViewChanged(ViewManager.ViewState state)
    {
        _viewState = state;

        // TopBar: Back button visible only when not in Galaxy view
        bool showBack = state != ViewManager.ViewState.Galaxy;
        if (_btnBack != null)
        {
            _btnBack.style.display = showBack
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        // TopBar: Globe/Carte button text mirrors current view
        if (_btnToggleView != null)
        {
            _btnToggleView.text = state == ViewManager.ViewState.Planet
                ? "Vue Carte"
                : "Vue Globe";
        }

        // Phase 2: show/hide LeftPanel (visible only in Planet view)
        bool inPlanet = state == ViewManager.ViewState.Planet;
        if (_leftPanel != null)
            _leftPanel.style.display = inPlanet ? DisplayStyle.Flex : DisplayStyle.None;
        _leftPanelVisible = inPlanet;

        // Phase 4: EventFeed visible only in Planet view
        if (_eventFeed != null)
            _eventFeed.style.display = inPlanet ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnTileResolved(GoldbergTileState tile)
    {
        _currentTile = tile;
        if (tile.tileId == null) return;

        RefreshBodyId();

        // Open TileInspector, hide LeftPanel
        if (_tileInspector != null)
            _tileInspector.style.display = DisplayStyle.Flex;
        if (_leftPanel != null)
            _leftPanel.style.display = DisplayStyle.None;

        // Update header label
        if (_tileHeaderLabel != null)
        {
            string tileShort = tile.tileId.Length > 12 ? tile.tileId[..12] + "…" : tile.tileId;
            _tileHeaderLabel.text = $"{tile.terrainType} — {tileShort}";
        }

        StartCoroutine(RefreshCorpListForTile());
    }

    private void OnTileHoverReady(string text, Vector2 screenPos)
    {
        if (_tooltip == null) return;
        _tooltipLabel.text = text;
        _tooltip.style.display = DisplayStyle.Flex;

        // screenPos: Y=0 at bottom (Unity) → convert to panel (Y=0 at top)
        float px = screenPos.x + 16f;
        float py = Screen.height - screenPos.y + 16f;
        // Prevent overflow past right/bottom edge (rough clamp, tooltip width ~240)
        if (px + 240f > Screen.width)  px = screenPos.x - 256f;
        if (py + 50f  > Screen.height) py = Screen.height - screenPos.y - 60f;
        _tooltip.style.left = px;
        _tooltip.style.top  = py;
    }

    private void OnTileHoverCancelled()
    {
        if (_tooltip != null)
            _tooltip.style.display = DisplayStyle.None;
    }

    private void OnProgressUpdated(float progress)
    {
        if (_progressFill != null)
            _progressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
        if (_progressLabel != null)
            _progressLabel.text = $"{progress * 100f:F1}% Terraform.";
    }

    private void OnRegionStateChanged(RegionState regionState)
    {
        if (_atmoLabel == null) return;
        if (!regionState.isValid) { _atmoLabel.text = ""; return; }
        AtmosphericState atm = regionState.atmosphericState;
        _atmoLabel.text = atm.habitabilityScore > 0f
            ? $"O2 {atm.o2Ratio * 100f:F1}%   CO2 {atm.co2Ratio * 100f:F3}%\n"
            + $"T° {atm.averageTemperature:F1}°C   {atm.atmosphericPressure:F1} kPa\n"
            + $"Habitabilité {atm.habitabilityScore * 100f:F0}%"
            : "";
    }

    // =========================================================
    // Phase 2 — LeftPanel
    // =========================================================

    private void BuildLeftPanel()
    {
        VisualTreeAsset asset = leftPanelTemplate
            ?? Resources.Load<VisualTreeAsset>("UI/Templates/LeftPanel");

        if (asset != null)
        {
            // CloneTree injects elements directly into _root (no TemplateContainer wrapper),
            // so position:Absolute with bottom:0 resolves against _root's full height.
            asset.CloneTree(_root);
            _leftPanel = _root.Q<VisualElement>("left-panel");
        }
        else
        {
            _leftPanel = BuildLeftPanelProcedural();
            _root.Add(_leftPanel);
        }

        if (_leftPanel == null)
        {
            Debug.LogError("[GameHUDController] BuildLeftPanel: 'left-panel' element not found.");
            return;
        }

        _progressFill    = _leftPanel.Q<VisualElement>("progress-fill");
        _progressLabel   = _leftPanel.Q<Label>("progress-label");
        _atmoLabel       = _leftPanel.Q<Label>("atmo-label");
        _scoreboardLabel = _leftPanel.Q<Label>("scoreboard-label");

        // Start hidden; shown by OnViewChanged when entering Planet view.
        _leftPanel.style.display = DisplayStyle.None;
    }

    private VisualElement BuildLeftPanelProcedural()
    {
        var panel = new VisualElement { name = "left-panel" };
        panel.AddToClassList("left-panel");

        var title = new Label { text = "TERRAFORMATION" };
        title.AddToClassList("left-panel__title");
        panel.Add(title);

        var progContainer = new VisualElement { name = "progress-container" };
        progContainer.AddToClassList("left-panel__progress-container");
        _progressFill = new VisualElement { name = "progress-fill" };
        _progressFill.AddToClassList("left-panel__progress-fill");
        progContainer.Add(_progressFill);
        panel.Add(progContainer);

        _progressLabel = new Label { name = "progress-label", text = "0% Terraform." };
        _progressLabel.AddToClassList("hud-label");
        _progressLabel.AddToClassList("hud-label--secondary");
        panel.Add(_progressLabel);

        var sep = new VisualElement();
        sep.AddToClassList("hud-separator");
        panel.Add(sep);

        _atmoLabel = new Label { name = "atmo-label", text = "" };
        _atmoLabel.AddToClassList("left-panel__atmo");
        panel.Add(_atmoLabel);

        _scoreboardLabel = new Label { name = "scoreboard-label", text = "" };
        _scoreboardLabel.AddToClassList("left-panel__scoreboard");
        panel.Add(_scoreboardLabel);

        return panel;
    }

    // =========================================================
    // Phase 2 — Scoreboard polling
    // =========================================================

    [Serializable]
    private class ScoreboardEntryDto
    {
        public string corpId;
        public string corpName;
        public float  credits;
        public int    tileCount;
        public float  globalReputation;
        public float  score;
    }

    [Serializable]
    private class ScoreboardEntryList { public ScoreboardEntryDto[] items; }

    private IEnumerator PollScoreboard()
    {
        while (true)
        {
            string url = simulationServerUrl.TrimEnd('/') + "/game/scoreboard";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && _scoreboardLabel != null)
                {
                    ScoreboardEntryDto[] entries;
                    try
                    {
                        string wrapped = $"{{\"items\":{req.downloadHandler.text}}}";
                        entries = JsonUtility.FromJson<ScoreboardEntryList>(wrapped).items;
                    }
                    catch { entries = null; }

                    if (entries != null && entries.Length > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        int shown = Mathf.Min(entries.Length, 3);
                        for (int i = 0; i < shown; i++)
                        {
                            if (i > 0) sb.Append("  |  ");
                            var e = entries[i];
                            sb.Append($"#{i + 1} {e.corpName}  ({e.tileCount} tiles)  *{e.score:F2}");
                        }
                        _scoreboardLabel.text = sb.ToString();
                    }
                }
            }
            yield return new WaitForSeconds(30f);
        }
    }

    // =========================================================
    // Phase 3 — TileInspector
    // =========================================================

    // ── Inspector fields (set in Unity Inspector) ────────────
    [SerializeField] private VisualTreeAsset tileInspectorTemplate;
    [SerializeField] private VisualTreeAsset buildingCardTemplate;
    [SerializeField] private VisualTreeAsset constructionCardTemplate;

    // ── Runtime: TileInspector element refs ──────────────────
    private VisualElement _tileInspector;
    private Label         _tileHeaderLabel;
    private VisualElement _corpBadgeEl;
    private Label         _corpOwnerLabelEl;
    private DropdownField _corpDropdown;
    private Button        _btnClaim;
    private Button        _btnUnclaim;
    private TextField     _corpNameInput;
    private Button        _btnCreateCorp;
    private VisualElement _buildingListContainer;
    private VisualElement _constructionListContainer;
    private VisualElement _constructionSection;
    private Label         _tileStatusLabel;
    private Label         _marketPricesLabelEl;
    private Label         _marketPopLabelEl;
    private Label         _contractListLabelEl;
    private Label         _natLabelEl;
    private Label         _ecologyLabelEl;

    // ── State ─────────────────────────────────────────────────
    private string       _activeBodyId  = "";
    private string       _ownerCorpId   = "";
    private List<string> _corpIds       = new();
    private string       _selectedContractId = "";
    private string       _activeNatId        = "";

    // ── DTOs (mirrored from GameHUD.cs) ──────────────────────
    [Serializable] private class CorpDto
    {
        public string          id;
        public string          name;
        public float           credits;
        public ClaimedTileDto[] claimedTiles;
    }
    [Serializable] private class ClaimedTileDto { public string tileId; public string bodyId; }
    [Serializable] private class CorpListDto    { public CorpDto[] items; }

    [Serializable] private class BuildingItem
    {
        public string id;
        public int    buildingType;
        public string tileId;
        public float  workerRatio;
        public int    level;
        public int    ticksActive;
    }
    [Serializable] private class BuildingListDto { public BuildingItem[] items; }

    [Serializable] private class ConstrItem
    {
        public string id;
        public int    buildingType;
        public string tileId;
        public int    status;
        public int    ticksRemaining;
        public int    totalCostPts;
        public int    pointsAccumulated;
    }
    [Serializable] private class ConstrListDto { public ConstrItem[] items; }

    private void BuildTileInspector()
    {
        VisualTreeAsset asset = tileInspectorTemplate
            ?? Resources.Load<VisualTreeAsset>("UI/Templates/TileInspector");

        if (asset == null)
        {
            Debug.LogWarning("[GameHUDController] TileInspector template not found.");
            return;
        }

        // CloneTree injects directly into _root so position:Absolute with bottom:0
        // resolves against _root's full height (not a zero-height TemplateContainer).
        asset.CloneTree(_root);
        _tileInspector = _root.Q<VisualElement>("tile-inspector");

        if (_tileInspector == null)
        {
            Debug.LogError("[GameHUDController] BuildTileInspector: 'tile-inspector' element not found.");
            return;
        }

        // Grab named elements
        _tileHeaderLabel         = _tileInspector.Q<Label>("tile-header-label");
        _corpBadgeEl             = _tileInspector.Q<VisualElement>("corp-badge");
        _corpOwnerLabelEl        = _tileInspector.Q<Label>("corp-owner-label");
        _corpDropdown            = _tileInspector.Q<DropdownField>("corp-dropdown");
        _btnClaim                = _tileInspector.Q<Button>("btn-claim");
        _btnUnclaim              = _tileInspector.Q<Button>("btn-unclaim");
        _corpNameInput           = _tileInspector.Q<TextField>("corp-name-input");
        _btnCreateCorp           = _tileInspector.Q<Button>("btn-create-corp");
        _buildingListContainer   = _tileInspector.Q<VisualElement>("building-list-container");
        _constructionListContainer = _tileInspector.Q<VisualElement>("construction-list-container");
        _constructionSection     = _tileInspector.Q<VisualElement>("construction-section");
        _tileStatusLabel         = _tileInspector.Q<Label>("tile-status-label");
        _marketPricesLabelEl     = _tileInspector.Q<Label>("market-prices-label");
        _marketPopLabelEl        = _tileInspector.Q<Label>("market-pop-label");
        _contractListLabelEl     = _tileInspector.Q<Label>("contract-list-label");
        _natLabelEl              = _tileInspector.Q<Label>("nat-label");
        _ecologyLabelEl          = _tileInspector.Q<Label>("ecology-label");

        // Wire buttons
        _btnClaim?   .RegisterCallback<ClickEvent>(_ => OnInspectorClaimClicked());
        _btnUnclaim?. RegisterCallback<ClickEvent>(_ => OnInspectorUnclaimClicked());
        _btnCreateCorp?.RegisterCallback<ClickEvent>(_ => OnInspectorCreateCorpClicked());
        _tileInspector.Q<Button>("btn-close-inspector")
            ?.RegisterCallback<ClickEvent>(_ => CloseInspector());

        // Collapsible section toggles
        WireCollapsible("market-toggle",        "market-body");
        WireCollapsible("contract-toggle",      "contract-body");
        WireCollapsible("nat-toggle",           "nat-body");
        WireCollapsible("ecology-toggle",       "ecology-body");

        // Start hidden
        _tileInspector.style.display = DisplayStyle.None;
    }

    private void WireCollapsible(string toggleName, string bodyName)
    {
        var toggle = _tileInspector?.Q<Label>(toggleName);
        var body   = _tileInspector?.Q<VisualElement>(bodyName);
        if (toggle == null || body == null) return;
        body.style.display = DisplayStyle.None;  // collapsed by default
        toggle.RegisterCallback<ClickEvent>(_ =>
        {
            bool visible = body.style.display == DisplayStyle.Flex;
            body.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            toggle.text = (visible ? "▶ " : "▼ ") + toggle.text[2..];
        });
    }

    private void CloseInspector()
    {
        if (_tileInspector != null)
            _tileInspector.style.display = DisplayStyle.None;
        // Restore LeftPanel if in Planet view
        if (_leftPanel != null && _viewState == ViewManager.ViewState.Planet)
            _leftPanel.style.display = DisplayStyle.Flex;
    }

    private void RefreshBodyId()
    {
        if (planetSphere == null) return;
        var f = typeof(PlanetSphereGoldberg).GetField(
            "_activeBodyId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) _activeBodyId = (string)f.GetValue(planetSphere) ?? "";
    }

    // ── Tile resolved → open inspector ───────────────────────

    // Updated OnTileResolved: wired in Start(), replaces Phase 1 stub.
    // (Start() already hooks this; we override the stub body here.)

    // ── Corp list refresh ─────────────────────────────────────

    private IEnumerator RefreshCorpListForTile()
    {
        if (_tileStatusLabel != null) _tileStatusLabel.text = "";
        string url = simulationServerUrl.TrimEnd('/') + "/game/corporations";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (_tileStatusLabel != null)
                    _tileStatusLabel.text = $"Erreur: {req.error}";
                yield break;
            }

            CorpListDto wrapper;
            try { wrapper = JsonUtility.FromJson<CorpListDto>("{\"items\":" + req.downloadHandler.text + "}"); }
            catch { yield break; }
            if (wrapper?.items == null) yield break;

            // Populate dropdown
            _corpIds.Clear();
            var choices = new System.Collections.Generic.List<string>();
            foreach (var c in wrapper.items)
            {
                _corpIds.Add(c.id);
                choices.Add(c.name);
            }
            if (_corpDropdown != null)
            {
                _corpDropdown.choices = choices;
                _corpDropdown.index   = 0;
            }

            // Detect owner of current tile
            string ownerCorpId = null;
            string ownerName   = null;
            if (_currentTile.tileId != null)
            {
                foreach (var c in wrapper.items)
                {
                    if (c.claimedTiles == null) continue;
                    foreach (var t in c.claimedTiles)
                    {
                        if (t.tileId == _currentTile.tileId)
                        {
                            ownerCorpId = c.id;
                            ownerName   = c.name;
                            break;
                        }
                    }
                    if (ownerCorpId != null) break;
                }
            }

            if (ownerCorpId != null)
            {
                _ownerCorpId = ownerCorpId;
                if (_corpOwnerLabelEl != null) _corpOwnerLabelEl.text = ownerName;
                if (_corpBadgeEl != null)
                {
                    Color col = GoldbergFaceColorizer.CorpColorFromId(ownerCorpId);
                    _corpBadgeEl.style.backgroundColor = col;
                }
                int ownerIdx = _corpIds.IndexOf(ownerCorpId);
                if (_corpDropdown != null && ownerIdx >= 0) _corpDropdown.index = ownerIdx;
                yield return RefreshBuildingsForTile(ownerCorpId, _currentTile.tileId);
            }
            else
            {
                if (_corpOwnerLabelEl != null) _corpOwnerLabelEl.text = "Non revendiquée";
                if (_corpBadgeEl != null)      _corpBadgeEl.style.backgroundColor = Color.clear;
                RebuildBuildingList(null, null);
            }
        }
    }

    private IEnumerator RefreshBuildingsForTile(string corpId, string tileId)
    {
        string url = $"{simulationServerUrl.TrimEnd('/')}/game/corporations/{corpId}/buildings";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                RebuildBuildingList(null, null);
                yield break;
            }

            BuildingListDto wrapper;
            try { wrapper = JsonUtility.FromJson<BuildingListDto>("{\"items\":" + req.downloadHandler.text + "}"); }
            catch { RebuildBuildingList(null, null); yield break; }

            var tileBuildings = new System.Collections.Generic.List<BuildingItem>();
            if (wrapper?.items != null)
                foreach (var b in wrapper.items)
                    if (b.tileId == tileId) tileBuildings.Add(b);

            // Fetch construction queue
            var qUrl = $"{simulationServerUrl.TrimEnd('/')}/game/corporations/{corpId}/construction-queue";
            var tileConstr = new System.Collections.Generic.List<ConstrItem>();
            using (var qreq = UnityWebRequest.Get(qUrl))
            {
                qreq.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
                yield return qreq.SendWebRequest();
                if (qreq.result == UnityWebRequest.Result.Success)
                {
                    ConstrListDto qw;
                    try { qw = JsonUtility.FromJson<ConstrListDto>("{\"items\":" + qreq.downloadHandler.text + "}"); }
                    catch { qw = null; }
                    if (qw?.items != null)
                        foreach (var ci in qw.items)
                            if (ci.tileId == tileId) tileConstr.Add(ci);
                }
            }

            RebuildBuildingList(tileBuildings, tileConstr);
        }
    }

    private static readonly string[] _buildingGlyphs = { "M", "F", "E", "R", "R", "P", "S" };
    private static readonly string[] _buildingNames  = { "Mine", "Ferme", "Centrale", "Recherche", "Route", "Port mer", "Spatioport" };

    private void RebuildBuildingList(
        System.Collections.Generic.List<BuildingItem> buildings,
        System.Collections.Generic.List<ConstrItem>   queue)
    {
        if (_buildingListContainer == null) return;
        _buildingListContainer.Clear();

        if (buildings == null || buildings.Count == 0)
        {
            var empty = new Label { text = "Aucun bâtiment sur cette tuile." };
            empty.AddToClassList("hud-label");
            empty.AddToClassList("hud-label--secondary");
            _buildingListContainer.Add(empty);
        }
        else
        {
            foreach (var b in buildings)
            {
                VisualElement card = buildingCardTemplate != null
                    ? buildingCardTemplate.Instantiate()
                    : BuildBuildingCardProcedural();

                // Set fields
                string glyph = b.buildingType >= 0 && b.buildingType < _buildingGlyphs.Length
                    ? _buildingGlyphs[b.buildingType] : "?";
                string name = b.buildingType >= 0 && b.buildingType < _buildingNames.Length
                    ? _buildingNames[b.buildingType] : "Bâtiment";

                card.Q<Label>("building-icon")   ?.Do(l => l.text = glyph);
                card.Q<Label>("building-name")   ?.Do(l => l.text = name);
                card.Q<Label>("building-workers")?.Do(l => l.text = $"Workers: {b.workerRatio * 100f:F0}%");
                card.Q<Label>("level-badge")     ?.Do(l => l.text = b.level.ToString());

                // Wire +/- buttons (capture ids in closure)
                string bid    = b.id;
                string corpId = _ownerCorpId;

                card.Q<Button>("btn-upgrade")  ?.RegisterCallback<ClickEvent>(_ =>
                    StartCoroutine(DoUpgradeBuilding(corpId, bid)));
                card.Q<Button>("btn-downgrade")?.RegisterCallback<ClickEvent>(_ =>
                    StartCoroutine(DoDowngradeBuilding(corpId, bid)));

                _buildingListContainer.Add(card);
            }
        }

        // Construction queue
        if (_constructionSection != null)
            _constructionSection.style.display = (queue != null && queue.Count > 0)
                ? DisplayStyle.Flex : DisplayStyle.None;

        if (_constructionListContainer != null)
        {
            _constructionListContainer.Clear();
            if (queue != null)
            {
                foreach (var ci in queue)
                {
                    VisualElement ccard = constructionCardTemplate != null
                        ? constructionCardTemplate.Instantiate()
                        : BuildConstructionCardProcedural();

                    string glyph = ci.buildingType >= 0 && ci.buildingType < _buildingGlyphs.Length
                        ? _buildingGlyphs[ci.buildingType] : "?";
                    string name = ci.buildingType >= 0 && ci.buildingType < _buildingNames.Length
                        ? _buildingNames[ci.buildingType] : "Construction";

                    float pct = ci.totalCostPts > 0
                        ? (float)ci.pointsAccumulated / ci.totalCostPts
                        : 0f;
                    string statusTxt = ci.status == 1
                        ? $"En cours… {ci.ticksRemaining} ticks" : "En file d'attente";

                    ccard.Q<Label>("constr-icon")  ?.Do(l => l.text = glyph);
                    ccard.Q<Label>("constr-name")  ?.Do(l => l.text = name);
                    ccard.Q<Label>("constr-status")?.Do(l => l.text = statusTxt);
                    ccard.Q<VisualElement>("constr-progress-fill")
                        ?.Do(el => el.style.width = new StyleLength(Length.Percent(pct * 100f)));

                    _constructionListContainer.Add(ccard);
                }
            }
        }
    }

    // Procedural fallbacks for BuildingCard / ConstructionCard
    private static VisualElement BuildBuildingCardProcedural()
    {
        var card = new VisualElement { name = "building-card" };
        card.AddToClassList("building-card");
        var icon = new Label { name = "building-icon", text = "?" };
        icon.AddToClassList("building-card__icon");
        card.Add(icon);
        var info = new VisualElement();
        info.AddToClassList("building-card__info");
        var nameL = new Label { name = "building-name", text = "Bâtiment" };
        nameL.AddToClassList("building-card__name");
        var workL = new Label { name = "building-workers", text = "" };
        workL.AddToClassList("building-card__workers");
        info.Add(nameL); info.Add(workL);
        card.Add(info);
        var lvlRow = new VisualElement();
        lvlRow.AddToClassList("building-card__level-row");
        var btnM = new Button { name = "btn-downgrade", text = "-" };
        btnM.AddToClassList("building-card__level-btn");
        var badge = new Label { name = "level-badge", text = "1" };
        badge.AddToClassList("building-card__level-badge");
        var btnP = new Button { name = "btn-upgrade", text = "+" };
        btnP.AddToClassList("building-card__level-btn");
        lvlRow.Add(btnM); lvlRow.Add(badge); lvlRow.Add(btnP);
        card.Add(lvlRow);
        return card;
    }

    private static VisualElement BuildConstructionCardProcedural()
    {
        var card = new VisualElement { name = "construction-card" };
        card.AddToClassList("construction-card");
        var icon = new Label { name = "constr-icon", text = "?" };
        icon.AddToClassList("building-card__icon");
        card.Add(icon);
        var info = new VisualElement();
        info.AddToClassList("building-card__info");
        var nameL = new Label { name = "constr-name", text = "Construction" };
        nameL.AddToClassList("building-card__name");
        var statusL = new Label { name = "constr-status", text = "" };
        statusL.AddToClassList("building-card__workers");
        info.Add(nameL); info.Add(statusL);
        card.Add(info);
        var prog = new VisualElement();
        prog.AddToClassList("constr-progress-container");
        var fill = new VisualElement { name = "constr-progress-fill" };
        fill.AddToClassList("constr-progress-fill");
        prog.Add(fill);
        card.Add(prog);
        return card;
    }

    // ── HTTP: Upgrade / Downgrade ─────────────────────────────

    private IEnumerator DoUpgradeBuilding(string corpId, string buildingId)
    {
        if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(buildingId)) yield break;
        string url = $"{simulationServerUrl.TrimEnd('/')}/game/corporations/{corpId}/buildings/{buildingId}/upgrade";
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                PushFeedEntry($"Upgrade bâtiment {buildingId[..Mathf.Min(8, buildingId.Length)]}");
                yield return RefreshBuildingsForTile(corpId, _currentTile.tileId);
            }
            else if (_tileStatusLabel != null)
                _tileStatusLabel.text = req.downloadHandler?.text ?? req.error;
        }
    }

    private IEnumerator DoDowngradeBuilding(string corpId, string buildingId)
    {
        if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(buildingId)) yield break;
        string url = $"{simulationServerUrl.TrimEnd('/')}/game/corporations/{corpId}/buildings/{buildingId}/downgrade";
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                PushFeedEntry($"Downgrade bâtiment {buildingId[..Mathf.Min(8, buildingId.Length)]}");
                yield return RefreshBuildingsForTile(corpId, _currentTile.tileId);
            }
            else if (_tileStatusLabel != null)
                _tileStatusLabel.text = req.downloadHandler?.text ?? req.error;
        }
    }

    // ── HTTP: Claim / Unclaim / CreateCorp ───────────────────

    private void OnInspectorClaimClicked()
    {
        if (_currentTile.tileId == null || _corpIds.Count == 0 || _corpDropdown == null) return;
        int idx = _corpDropdown.index;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoInspectorClaim(_corpIds[idx], _currentTile.tileId));
    }

    private IEnumerator DoInspectorClaim(string corpId, string tileId)
    {
        if (_tileStatusLabel != null) _tileStatusLabel.text = "Claim en cours…";
        string url = simulationServerUrl.TrimEnd('/')
            + $"/game/corporations/{corpId}/claim-hex"
            + $"?body_id={UnityWebRequest.EscapeURL(_activeBodyId)}"
            + $"&tile_id={UnityWebRequest.EscapeURL(tileId)}";
        using (var req = UnityWebRequest.PostWwwForm(url, ""))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (_tileStatusLabel != null)
                _tileStatusLabel.text = req.result == UnityWebRequest.Result.Success
                    ? "Tuile claimée." : req.downloadHandler?.text ?? req.error;
            if (req.result == UnityWebRequest.Result.Success)
            {
                PushFeedEntry($"Claim → {tileId[..Mathf.Min(8, tileId.Length)]}");
                planetSphere?.RefreshOwnershipOverlay();
                yield return RefreshCorpListForTile();
            }
        }
    }

    private void OnInspectorUnclaimClicked()
    {
        if (_currentTile.tileId == null || _corpIds.Count == 0 || _corpDropdown == null) return;
        int idx = _corpDropdown.index;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoInspectorUnclaim(_corpIds[idx], _currentTile.tileId));
    }

    private IEnumerator DoInspectorUnclaim(string corpId, string tileId)
    {
        if (_tileStatusLabel != null) _tileStatusLabel.text = "Unclaim en cours…";
        string url = simulationServerUrl.TrimEnd('/')
            + $"/game/corporations/{corpId}/claim-hex"
            + $"?body_id={UnityWebRequest.EscapeURL(_activeBodyId)}"
            + $"&tile_id={UnityWebRequest.EscapeURL(tileId)}";
        using (var req = UnityWebRequest.Delete(url))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (_tileStatusLabel != null)
                _tileStatusLabel.text = req.result == UnityWebRequest.Result.Success
                    ? "Tuile libérée." : req.downloadHandler?.text ?? req.error;
            if (req.result == UnityWebRequest.Result.Success)
            {
                PushFeedEntry($"Unclaim → {tileId[..Mathf.Min(8, tileId.Length)]}");
                planetSphere?.RefreshOwnershipOverlay();
                yield return RefreshCorpListForTile();
            }
        }
    }

    private void OnInspectorCreateCorpClicked()
    {
        if (_corpNameInput == null) return;
        string name = _corpNameInput.value?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            if (_tileStatusLabel != null) _tileStatusLabel.text = "Entrez un nom.";
            return;
        }
        StartCoroutine(DoInspectorCreateCorp(name));
    }

    private IEnumerator DoInspectorCreateCorp(string corpName)
    {
        if (_tileStatusLabel != null) _tileStatusLabel.text = "Création en cours…";
        string url  = simulationServerUrl.TrimEnd('/') + "/game/corporations";
        string json = "{\"name\":\"" + EscapeJsonSimple(corpName) + "\",\"is_ai\":false}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                if (_corpNameInput != null) _corpNameInput.value = "";
                if (_tileStatusLabel != null) _tileStatusLabel.text = $"Corporation '{corpName}' créée.";
                PushFeedEntry($"Corp créée : {corpName}");
                yield return RefreshCorpListForTile();
            }
            else
            {
                if (_tileStatusLabel != null)
                    _tileStatusLabel.text = req.downloadHandler?.text ?? req.error;
            }
        }
    }

    private static string EscapeJsonSimple(string s) =>
        s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

    // =========================================================
    // Phase 4 — Tooltip
    // =========================================================

    private void BuildTooltip()
    {
        _tooltip = new VisualElement { name = "hud-tooltip" };
        _tooltip.AddToClassList("hud-tooltip");
        _tooltip.pickingMode = PickingMode.Ignore;

        _tooltipLabel = new Label { name = "hud-tooltip-label" };
        _tooltipLabel.AddToClassList("hud-tooltip__label");
        _tooltip.Add(_tooltipLabel);

        _tooltip.style.display = DisplayStyle.None;
        _root.Add(_tooltip);
    }

    // =========================================================
    // Phase 4 — EventFeed
    // =========================================================

    [Serializable]
    private class EventDto
    {
        public string id;
        public string eventType;
        public string name;
        public string description;
        public int    tick;
        public string affectedEntityId;
    }
    [Serializable]
    private class EventDtoList { public EventDto[] items; }

    private void BuildEventFeed()
    {
        _eventFeed = new VisualElement { name = "event-feed" };
        _eventFeed.AddToClassList("event-feed");
        _eventFeed.pickingMode = PickingMode.Position;

        // ── Tab row ──────────────────────────────────────────────
        var tabRow = new VisualElement { name = "event-feed-tabs" };
        tabRow.AddToClassList("event-feed__tabs");

        var tabEvents  = new Label { name = "tab-events",  text = "ÉVÉNEMENTS" };
        var tabActions = new Label { name = "tab-actions", text = "ACTIONS" };
        tabEvents.AddToClassList("event-feed__tab");
        tabEvents.AddToClassList("event-feed__tab--active");
        tabActions.AddToClassList("event-feed__tab");
        tabRow.Add(tabEvents);
        tabRow.Add(tabActions);
        _eventFeed.Add(tabRow);

        // ── Events pane (serveur) ─────────────────────────────────
        _eventFeedList = new VisualElement { name = "event-feed-list" };
        _eventFeedList.AddToClassList("event-feed__pane");
        _eventFeedList.AddToClassList("event-feed__list");
        _eventFeed.Add(_eventFeedList);

        // ── Actions pane (actions locales) ────────────────────────
        _eventFeedListActions = new VisualElement { name = "event-feed-actions" };
        _eventFeedListActions.AddToClassList("event-feed__pane");
        _eventFeedListActions.AddToClassList("event-feed__list");
        _eventFeedListActions.style.display = DisplayStyle.None;
        _eventFeed.Add(_eventFeedListActions);

        // ── Tab click wiring ──────────────────────────────────────
        tabEvents .RegisterCallback<ClickEvent>(_ => SetEventTab(0, tabEvents, tabActions));
        tabActions.RegisterCallback<ClickEvent>(_ => SetEventTab(1, tabEvents, tabActions));

        _eventFeed.style.display = DisplayStyle.None;
        _root.Add(_eventFeed);
    }

    private void SetEventTab(int idx, Label tabEvents, Label tabActions)
    {
        _activeEventTab = idx;
        bool showEvents = idx == 0;
        if (_eventFeedList        != null) _eventFeedList.style.display        = showEvents ? DisplayStyle.Flex : DisplayStyle.None;
        if (_eventFeedListActions != null) _eventFeedListActions.style.display = showEvents ? DisplayStyle.None : DisplayStyle.Flex;
        tabEvents .EnableInClassList("event-feed__tab--active",  showEvents);
        tabActions.EnableInClassList("event-feed__tab--active", !showEvents);
    }

    /// <summary>Push a local action entry to the ACTIONS tab (shows immediately).</summary>
    public void PushFeedEntry(string message)
    {
        if (_eventFeedListActions == null) return;
        var row = new Label { text = message };
        row.AddToClassList("event-feed__entry");
        row.AddToClassList("event-feed__entry--local");
        _eventFeedListActions.Insert(0, row);
        while (_eventFeedListActions.childCount > MaxFeedEntries)
            _eventFeedListActions.RemoveAt(_eventFeedListActions.childCount - 1);
    }

    private IEnumerator PollEventFeed()
    {
        while (true)
        {
            string url = simulationServerUrl.TrimEnd('/') + "/game/events?limit=" + MaxFeedEntries;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && _eventFeedList != null)
                {
                    EventDto[] events;
                    try
                    {
                        string wrapped = $"{{\"items\":{req.downloadHandler.text}}}";
                        events = JsonUtility.FromJson<EventDtoList>(wrapped).items;
                    }
                    catch { events = null; }

                    if (events != null && events.Length > 0)
                    {
                        _eventFeedList.Clear();
                        foreach (var e in events)
                        {
                            string label = string.IsNullOrEmpty(e.name)
                                ? $"[T{e.tick}] {e.eventType}"
                                : $"[T{e.tick}] {e.name}";
                            var row = new Label { text = label };
                            row.AddToClassList("event-feed__entry");
                            _eventFeedList.Add(row);
                        }
                    }
                }
            }
            yield return new WaitForSeconds(15f);
        }
    }

    // =========================================================
    // Accessors
    // =========================================================

    public VisualElement Root => _root;
}

/// <summary>Null-safe fluent helper for VisualElement queries.</summary>
internal static class VisualElementExt
{
    public static T Do<T>(this T el, System.Action<T> action) where T : VisualElement
    {
        if (el != null) action(el);
        return el;
    }
}
