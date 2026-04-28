using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Orchestrateur HUD - point d'entree UI Toolkit.
/// Initialise les 7 sous-controleurs, cable les evenements scene,
/// gere tooltip + event-popup (utilitaires partages).
/// Toute logique metier est deleguee aux sous-controleurs.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class GameHUDController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ViewManager          viewManager;
    [SerializeField] private TerraformHUD         terraformHUD;
    [SerializeField] private PlanetSphereGoldberg planetSphere;
    [SerializeField] private DebugHydrologyPanel  debugHydrologyPanel;
    [SerializeField] private TestLaunchMenu       testLaunchMenu;

    [Header("Config")]
    [SerializeField] private GameConfig config;


    [Header("UXML Templates")]
    [SerializeField] private VisualTreeAsset tooltipTemplate;
    [SerializeField] private VisualTreeAsset eventPopupTemplate;

    [Header("UXML Templates — sub-controllers")]
    [SerializeField] private VisualTreeAsset tileInspectorTemplate;
    [SerializeField] private VisualTreeAsset eventFeedTemplate;
    [SerializeField] private VisualTreeAsset debugDrawerTemplate;
    [SerializeField] private VisualTreeAsset bottomActionBarTemplate;

    [Header("Style Sheets")]
    [SerializeField] private StyleSheet variablesStyleSheet;
    [SerializeField] private StyleSheet baseStyleSheet;
    [SerializeField] private StyleSheet debugDrawerStyleSheet;

    [Header("HUD Controllers")]
    [SerializeField] private TopBarController          topBarController;
    [SerializeField] private LeftPanelController       leftPanelController;
    [SerializeField] private TileInspectorController   tileInspectorController;
    [SerializeField] private EventFeedController       eventFeedController;
    [SerializeField] private DebugDrawerController     debugDrawerController;
    [SerializeField] private BottomActionBarController bottomActionBarController;
    [SerializeField] private TimeControlsController    timeControlsController;
    [SerializeField] private TerritoryPanelController  territoryPanelController;

    private UIDocument    _doc;
    private VisualElement _root;
    private ViewManager.ViewState _viewState = ViewManager.ViewState.Galaxy;

    private VisualElement _tooltip;
    private Label         _tooltipLabel;

    private VisualElement _eventPopup;
    private Label         _eventPopupTitle;
    private Label         _eventPopupBody;
    private Coroutine     _autoHidePopupCoroutine;

    private void InitializeHUDControllers()
    {
        if (topBarController          == null) topBarController          = gameObject.AddComponent<TopBarController>();
        if (leftPanelController       == null) leftPanelController       = gameObject.AddComponent<LeftPanelController>();
        if (tileInspectorController   == null) tileInspectorController   = gameObject.AddComponent<TileInspectorController>();
        if (eventFeedController       == null) eventFeedController       = gameObject.AddComponent<EventFeedController>();
        if (debugDrawerController     == null) debugDrawerController     = gameObject.AddComponent<DebugDrawerController>();
        if (bottomActionBarController == null) bottomActionBarController = gameObject.AddComponent<BottomActionBarController>();
        if (timeControlsController    == null) timeControlsController    = gameObject.AddComponent<TimeControlsController>();
        if (territoryPanelController  == null) territoryPanelController  = gameObject.AddComponent<TerritoryPanelController>();
    }

    private void Awake()
    {
        _doc  = GetComponent<UIDocument>();
        _root = new VisualElement { name = "hud-root" };
        _root.style.width    = new StyleLength(UnityEngine.UIElements.Length.Percent(100));
        _root.style.height   = new StyleLength(UnityEngine.UIElements.Length.Percent(100));
        _root.style.position = Position.Absolute;
        _root.pickingMode    = PickingMode.Ignore;

        if (variablesStyleSheet != null) _root.styleSheets.Add(variablesStyleSheet);
        else UnityEngine.Debug.LogWarning("[GameHUDController] variablesStyleSheet not assigned.");
        if (baseStyleSheet != null)      _root.styleSheets.Add(baseStyleSheet);
        else UnityEngine.Debug.LogWarning("[GameHUDController] baseStyleSheet not assigned.");

        if (_doc.rootVisualElement != null) _doc.rootVisualElement.Add(_root);
        else UnityEngine.Debug.LogWarning("[GameHUDController] UIDocument.rootVisualElement null in Awake.");
    }

    private void Start()
    {
        if (viewManager         == null) viewManager         = FindAnyObjectByType<ViewManager>(FindObjectsInactive.Include);
        if (terraformHUD        == null) terraformHUD        = FindAnyObjectByType<TerraformHUD>(FindObjectsInactive.Include);
        if (planetSphere        == null) planetSphere        = FindAnyObjectByType<PlanetSphereGoldberg>(FindObjectsInactive.Include);
        if (debugHydrologyPanel == null) debugHydrologyPanel = FindAnyObjectByType<DebugHydrologyPanel>(FindObjectsInactive.Include);
        if (testLaunchMenu      == null) testLaunchMenu      = FindAnyObjectByType<TestLaunchMenu>(FindObjectsInactive.Include);

        InitializeHUDControllers();

        if (_doc.rootVisualElement != null && !_doc.rootVisualElement.Contains(_root))
            _doc.rootVisualElement.Add(_root);

        topBarController.Initialize(_root);
        leftPanelController.Initialize(_root);
        tileInspectorController.Initialize(_root, tileInspectorTemplate);
        eventFeedController.Initialize(_root, eventFeedTemplate);
        debugDrawerController.Initialize(_root, debugDrawerTemplate, debugDrawerStyleSheet);
        bottomActionBarController.Initialize(_root, bottomActionBarTemplate);
        timeControlsController.Initialize(_root);
        territoryPanelController.Initialize(_root);

        BuildTooltip();
        BuildEventPopup();

        leftPanelController.StartPolling();
        SimulationWebSocketClient.OnServerTickAdvanced += OnWsTickAdvanced;
        SimulationWebSocketClient.OnServerGameEvent    += OnWsGameEvent;
        StartCoroutine(FetchInitialTickStatus());

        ViewManager.OnViewChanged += OnViewChanged;

        if (planetSphere != null)
        {
            planetSphere.OnH3TileResolved     += OnTileResolved;
            planetSphere.OnTileHoverReady     += OnTileHoverReady;
            planetSphere.OnTileHoverCancelled += OnTileHoverCancelled;
        }
        else
            UnityEngine.Debug.LogWarning("[GameHUDController] planetSphere NULL.");

        if (terraformHUD != null)
        {
            terraformHUD.OnProgressUpdated    += OnProgressUpdated;
            terraformHUD.OnRegionStateChanged += OnRegionStateChanged;
        }

        OnViewChanged(_viewState);
        UnityEngine.Debug.Log($"[GameHUDController] Ready. planetSphere={(planetSphere != null ? planetSphere.name : "NULL")}");
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

        SimulationWebSocketClient.OnServerTickAdvanced -= OnWsTickAdvanced;
        SimulationWebSocketClient.OnServerGameEvent    -= OnWsGameEvent;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)  ToggleDebugDrawer();
        if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame) debugHydrologyPanel?.TogglePanel();
    }

    private void OnViewChanged(ViewManager.ViewState state)
    {
        _viewState = state;
        bool inPlanet = state == ViewManager.ViewState.Planet;
        topBarController?.OnViewChanged(state);
        leftPanelController?.SetVisible(inPlanet);
        eventFeedController?.SetVisible(inPlanet);
        bottomActionBarController?.SetVisible(inPlanet);
        if (!inPlanet) debugDrawerController?.SetVisible(false);
        if (!inPlanet) territoryPanelController?.Hide();
    }

    private void OnTileResolved(GoldbergTileState tile)      => tileInspectorController?.ShowTile(tile);
    private void OnProgressUpdated(float progress)
    {
        leftPanelController?.SetProgress(progress);
        topBarController?.SetTerraformProgress(progress);
    }
    private void OnRegionStateChanged(RegionState regionState)
    {
        leftPanelController?.SetAtmosphericState(
            regionState.isValid ? regionState.atmosphericState : default);
    }

    private void OnTileHoverReady(string text, UnityEngine.Vector2 screenPos)
    {
        if (_tooltip == null) return;
        _tooltipLabel.text = text;
        _tooltip.style.display = DisplayStyle.Flex;
        float px = screenPos.x + 16f;
        float py = UnityEngine.Screen.height - screenPos.y + 16f;
        if (px + 240f > UnityEngine.Screen.width)  px = screenPos.x - 256f;
        if (py + 50f  > UnityEngine.Screen.height) py = UnityEngine.Screen.height - screenPos.y - 60f;
        _tooltip.style.left = px;
        _tooltip.style.top  = py;
    }

    private void OnTileHoverCancelled()
    {
        if (_tooltip != null) _tooltip.style.display = DisplayStyle.None;
    }

    // ── WebSocket handlers ────────────────────────────────────────────────────

    private void OnWsTickAdvanced(int tick)
    {
        timeControlsController?.SetTickProgress(tick, 0);
        leftPanelController?.RefreshScoreboardNow();
    }

    private void OnWsGameEvent(GameEventPush evt)
    {
        eventFeedController?.PushServerEvent(evt);
    }

    // ── Tick status (initial fetch + fallback) ─────────────────────────────────

    private IEnumerator FetchInitialTickStatus()
    {
        string url = GetSimulationServerUrl().TrimEnd('/') + "/tick/status";
        yield return SimHttp.Get(url, GetSimulationServerTimeout(), json =>
        {
            try
            {
                var dto = UnityEngine.JsonUtility.FromJson<TickStatusDto>(json);
                if (dto != null)
                {
                    timeControlsController?.SetSpeedMultiplier(dto.speedMultiplier);
                    timeControlsController?.SetTickProgress(dto.tickCount, 0);
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[FetchInitialTickStatus] Parse error: {e.Message}");
            }
        });
    }

    private IEnumerator PollTickStatus()
    {
        while (true)
        {
            string url = GetSimulationServerUrl().TrimEnd('/') + "/tick/status";
            yield return SimHttp.Get(url, GetSimulationServerTimeout(), json =>
            {
                try
                {
                    var dto = UnityEngine.JsonUtility.FromJson<TickStatusDto>(json);
                    if (dto != null)
                    {
                        timeControlsController?.SetSpeedMultiplier(dto.speedMultiplier);
                        timeControlsController?.SetTickProgress(dto.tickCount, 0);
                    }
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[PollTickStatus] Parse error: {e.Message}");
                }
            });
            yield return new WaitForSeconds(1f);
        }
    }

    [System.Serializable]
    private class TickStatusDto
    {
        public int   tickCount;
        public bool  tickRunning;
        public float tickIntervalSeconds;
        public int   speedMultiplier;
    }

    private void BuildTooltip()
    {
        if (tooltipTemplate != null)
        {
            tooltipTemplate.CloneTree(_root);
            _tooltip      = _root.Q<VisualElement>("hud-tooltip");
            _tooltipLabel = _root.Q<Label>("hud-tooltip-label");
        }
        else
        {
            _tooltip = new VisualElement { name = "hud-tooltip" };
            _tooltip.AddToClassList("hud-tooltip");
            _tooltipLabel = new Label { name = "hud-tooltip-label" };
            _tooltipLabel.AddToClassList("hud-tooltip__label");
            _tooltip.Add(_tooltipLabel);
            _root.Add(_tooltip);
        }
        if (_tooltip != null) { _tooltip.pickingMode = PickingMode.Ignore; _tooltip.style.display = DisplayStyle.None; }
    }

    private void BuildEventPopup()
    {
        if (eventPopupTemplate != null)
        {
            eventPopupTemplate.CloneTree(_root);
            _eventPopup      = _root.Q<VisualElement>("event-popup");
            _eventPopupTitle = _root.Q<Label>("event-popup-title");
            _eventPopupBody  = _root.Q<Label>("event-popup-body");
        }
        else
        {
            _eventPopup = new VisualElement { name = "event-popup" };
            _eventPopup.AddToClassList("event-popup");
            _eventPopupTitle = new Label { name = "event-popup-title" };
            _eventPopupTitle.AddToClassList("event-popup__title");
            _eventPopupBody  = new Label { name = "event-popup-body" };
            _eventPopupBody.AddToClassList("event-popup__body");
            _eventPopup.Add(_eventPopupTitle);
            _eventPopup.Add(_eventPopupBody);
            _root.Add(_eventPopup);
        }
        if (_eventPopup != null)
        {
            _eventPopup.pickingMode      = PickingMode.Ignore;
            _eventPopup.style.position   = Position.Absolute;
            _eventPopup.style.top        = new StyleLength(56f);
            _eventPopup.style.left       = new StyleLength(UnityEngine.UIElements.Length.Percent(50));
            _eventPopup.style.marginLeft = new StyleLength(-170f);
            _eventPopup.style.display    = DisplayStyle.None;
        }
    }

    public void ShowEventPopup(string title, string body = "", float autoHideSeconds = 4f)
    {
        if (_eventPopup == null) return;
        if (_eventPopupTitle != null) _eventPopupTitle.text = title;
        if (_eventPopupBody  != null)
        {
            _eventPopupBody.text = body;
            _eventPopupBody.style.display = string.IsNullOrEmpty(body) ? DisplayStyle.None : DisplayStyle.Flex;
        }
        _eventPopup.style.display = DisplayStyle.Flex;
        if (_autoHidePopupCoroutine != null) StopCoroutine(_autoHidePopupCoroutine);
        _autoHidePopupCoroutine = StartCoroutine(AutoHidePopup(autoHideSeconds));
    }

    public void HideEventPopup()
    {
        if (_autoHidePopupCoroutine != null) { StopCoroutine(_autoHidePopupCoroutine); _autoHidePopupCoroutine = null; }
        if (_eventPopup != null) _eventPopup.style.display = DisplayStyle.None;
    }

    private IEnumerator AutoHidePopup(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_eventPopup != null) _eventPopup.style.display = DisplayStyle.None;
        _autoHidePopupCoroutine = null;
    }

    public string GetSimulationServerUrl()     => config != null ? config.simulationServerUrl     : "http://127.0.0.1:8080";
    public float  GetSimulationServerTimeout() => config != null ? config.simulationServerTimeoutSeconds : 5f;

    public void SetPlanetName(string name)              => topBarController?.SetPlanetName(name);
    public void SetTickCredits(int tick, float credits) => topBarController?.SetTickCredits(tick, credits);
    public void SetToggleViewLabel(string label)        => topBarController?.SetToggleViewLabel(label);
    public void ShowTerritoryPanel(string stateId, string stateName = null)
                                                    => territoryPanelController?.ShowTerritory(stateId, stateName);

    public void ToggleDebugDrawer()
    {
        debugDrawerController?.SetVisible(!debugDrawerController.GetVisible());
        testLaunchMenu?.ToggleMenu();
    }
    public void SetActiveTab(int tabIndex)              => bottomActionBarController?.SetActiveTab(tabIndex);
    public void PushFeedEntry(string message)           => eventFeedController?.PushFeedEntry(message);

    public VisualElement Root => _root;
}

internal static class VisualElementExt
{
    public static T Do<T>(this T el, System.Action<T> action) where T : VisualElement
    {
        if (el != null) action(el);
        return el;
    }
}
