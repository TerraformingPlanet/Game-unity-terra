using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the TopBar UI component.
/// Handles planet name, tick/credits display, back button, toggle view button.
/// </summary>
public class TopBarController : MonoBehaviour
{
    private VisualElement _topBar;
    private Button _btnBack;
    private Label _labelPlanet;
    private Label _labelTickCredits;
    private Button _btnToggleView;
    private Button _btnDebug;

    // Terraform progress (inline in TopBar)
    private VisualElement _terraformBar;
    private VisualElement _terraformFill;
    private Label         _terraformLabel;

    private VisualTreeAsset _topBarTemplate;

    public void Initialize(VisualElement root)
    {
        // Try to load template from Resources or use procedural build
        _topBarTemplate = Resources.Load<VisualTreeAsset>("UI/Templates/TopBar");

        if (_topBarTemplate != null)
        {
            _topBar = _topBarTemplate.Instantiate();
        }
        else
        {
            _topBar = BuildTopBarProcedural();
        }

        root.Add(_topBar);

        // Grab named elements
        _btnBack = _topBar.Q<Button>("btn-back");
        _labelPlanet = _topBar.Q<Label>("label-planet");
        _labelTickCredits = _topBar.Q<Label>("label-tick-credits");
        _btnToggleView = _topBar.Q<Button>("btn-toggle-view");
        _btnDebug = _topBar.Q<Button>("btn-debug");

        // Wire button callbacks
        if (_btnBack != null)
            _btnBack.RegisterCallback<ClickEvent>(_ => OnBackClicked());
        if (_btnToggleView != null)
            _btnToggleView.RegisterCallback<ClickEvent>(_ => OnToggleViewClicked());
        if (_btnDebug != null)
            _btnDebug.RegisterCallback<ClickEvent>(_ => OnDebugClicked());
    }

    private void OnBackClicked()
    {
        var viewManager = FindAnyObjectByType<ViewManager>();
        viewManager?.GoBackOneLevel();
    }

    private void OnToggleViewClicked()
    {
        var viewManager = FindAnyObjectByType<ViewManager>();
        viewManager?.TogglePlanetView();
    }

    private void OnDebugClicked()
    {
        var gameHUD = FindAnyObjectByType<GameHUDController>();
        gameHUD?.ToggleDebugDrawer();
    }

    public void SetPlanetName(string name)
    {
        if (_labelPlanet != null) _labelPlanet.text = name;
    }

    private int   _lastTick    = 0;
    private float _lastCredits = float.NaN;

    public void SetTickCredits(int tick, float credits)
    {
        if (_labelTickCredits == null) return;
        _lastTick    = tick;
        _lastCredits = credits;
        string credStr = float.IsNaN(credits) ? "—" : credits.ToString("N0") + " ¢";
        _labelTickCredits.text = $"Tick {tick} | {credStr}";
    }

    /// <summary>Met à jour uniquement la date/tick sans changer les crédits affichés.</summary>
    public void SetCurrentTick(int tick)
    {
        _lastTick = tick;
        SetTickCredits(tick, _lastCredits);
    }

    public void SetToggleViewLabel(string label)
    {
        if (_btnToggleView != null) _btnToggleView.text = label;
    }

    public void OnViewChanged(ViewManager.ViewState state)
    {
        // Show/hide back button
        bool showBack = state != ViewManager.ViewState.Galaxy;
        if (_btnBack != null)
        {
            _btnBack.style.display = showBack ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Update toggle button text
        if (_btnToggleView != null)
        {
            _btnToggleView.text = state == ViewManager.ViewState.Planet ? "Vue Carte" : "Vue Globe";
        }
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

        // ── Terraform progress bar (inline) ───────────────────────────────
        _terraformBar = new VisualElement { name = "terraform-bar" };
        _terraformBar.style.flexDirection  = FlexDirection.Row;
        _terraformBar.style.alignItems     = Align.Center;
        _terraformBar.style.marginLeft     = 8f;
        _terraformBar.style.marginRight    = 8f;

        var terraLabel = new Label { text = "TERRA" };
        terraLabel.style.color    = new StyleColor(new UnityEngine.Color(0.55f, 0.85f, 0.55f));
        terraLabel.style.fontSize = 10;
        terraLabel.style.marginRight = 4f;
        _terraformBar.Add(terraLabel);

        var terraTrack = new VisualElement();
        terraTrack.style.width           = 80f;
        terraTrack.style.height          = 5f;
        terraTrack.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.15f, 0.15f, 0.15f));
        terraTrack.style.borderTopLeftRadius     = 3f;
        terraTrack.style.borderTopRightRadius    = 3f;
        terraTrack.style.borderBottomLeftRadius  = 3f;
        terraTrack.style.borderBottomRightRadius = 3f;
        terraTrack.style.overflow = Overflow.Hidden;
        _terraformFill = new VisualElement { name = "terraform-fill" };
        _terraformFill.style.height          = new StyleLength(Length.Percent(100));
        _terraformFill.style.width           = new StyleLength(Length.Percent(0));
        _terraformFill.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.3f, 0.75f, 0.3f));
        terraTrack.Add(_terraformFill);
        _terraformBar.Add(terraTrack);

        _terraformLabel = new Label { name = "terraform-label", text = "0%" };
        _terraformLabel.style.color    = new StyleColor(new UnityEngine.Color(0.75f, 0.75f, 0.75f));
        _terraformLabel.style.fontSize = 10;
        _terraformLabel.style.marginLeft = 4f;
        _terraformBar.Add(_terraformLabel);

        bar.Add(_terraformBar);
        // ─────────────────────────────────────────────────────────────────────

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

    /// <summary>Update terraformation progress in TopBar. Hides bar at 100%.</summary>
    public void SetTerraformProgress(float progress)
    {
        if (_terraformBar == null) return;
        if (progress >= 1f)
        {
            _terraformBar.style.display = DisplayStyle.None;
            return;
        }
        _terraformBar.style.display = DisplayStyle.Flex;
        float pct = UnityEngine.Mathf.Clamp01(progress) * 100f;
        if (_terraformFill  != null) _terraformFill.style.width = new StyleLength(Length.Percent(pct));
        if (_terraformLabel != null) _terraformLabel.text = $"{pct:F0}%";
    }
}