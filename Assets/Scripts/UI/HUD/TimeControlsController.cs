using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the TimeControls UI component.
/// Handles tick speed controls (1x, 2x, 10x, 50x, 100x).
/// </summary>
public class TimeControlsController : MonoBehaviour
{
    private VisualElement _timeControlsBar;
    private Button _btnSpeed1x;
    private Button _btnSpeed2x;
    private Button _btnSpeed10x;
    private Button _btnSpeed50x;
    private Button _btnSpeed100x;
    private Label _labelTickProgress;
    private int _currentSpeedMultiplier = 1;

    private GameHUDController _gameHUDController;

    public void Initialize(VisualElement root)
    {
        _gameHUDController = GetComponent<GameHUDController>();
        BuildTimeControls(root);
    }

    private void BuildTimeControls(VisualElement root)
    {
        _timeControlsBar = new VisualElement { name = "time-controls-bar" };
        _timeControlsBar.AddToClassList("time-controls-bar");

        // Speed buttons
        _btnSpeed1x = new Button { name = "btn-speed-1x", text = "1×" };
        _btnSpeed1x.AddToClassList("time-controls__btn");
        _btnSpeed1x.AddToClassList("time-controls__btn--active"); // default active
        _timeControlsBar.Add(_btnSpeed1x);

        _btnSpeed2x = new Button { name = "btn-speed-2x", text = "2×" };
        _btnSpeed2x.AddToClassList("time-controls__btn");
        _timeControlsBar.Add(_btnSpeed2x);

        _btnSpeed10x = new Button { name = "btn-speed-10x", text = "10×" };
        _btnSpeed10x.AddToClassList("time-controls__btn");
        _timeControlsBar.Add(_btnSpeed10x);

        _btnSpeed50x = new Button { name = "btn-speed-50x", text = "50×" };
        _btnSpeed50x.AddToClassList("time-controls__btn");
        _timeControlsBar.Add(_btnSpeed50x);

        _btnSpeed100x = new Button { name = "btn-speed-100x", text = "100×" };
        _btnSpeed100x.AddToClassList("time-controls__btn");
        _timeControlsBar.Add(_btnSpeed100x);

        // Progress label
        _labelTickProgress = new Label { name = "label-tick-progress", text = "Tick —" };
        _labelTickProgress.AddToClassList("time-controls__progress");
        _timeControlsBar.Add(_labelTickProgress);

        // Layout styles
        _timeControlsBar.style.position = Position.Absolute;
        _timeControlsBar.style.top = new StyleLength(60f); // below TopBar
        _timeControlsBar.style.right = new StyleLength(12f);
        _timeControlsBar.style.flexDirection = FlexDirection.Row;
        _timeControlsBar.style.alignItems = Align.Center;
        _timeControlsBar.style.backgroundColor = new StyleColor(new Color(0.031f, 0.031f, 0.055f, 0.92f));
        _timeControlsBar.style.borderTopWidth    = 1f;
        _timeControlsBar.style.borderRightWidth  = 1f;
        _timeControlsBar.style.borderBottomWidth = 1f;
        _timeControlsBar.style.borderLeftWidth   = 1f;
        var _borderCol = new StyleColor(new Color(1f, 1, 1f, 0.08f));
        _timeControlsBar.style.borderTopColor    = _borderCol;
        _timeControlsBar.style.borderRightColor  = _borderCol;
        _timeControlsBar.style.borderBottomColor = _borderCol;
        _timeControlsBar.style.borderLeftColor   = _borderCol;
        _timeControlsBar.style.paddingLeft = 8f;
        _timeControlsBar.style.paddingRight = 8f;
        _timeControlsBar.style.paddingTop = 4f;
        _timeControlsBar.style.paddingBottom = 4f;

        // Wire button clicks
        _btnSpeed1x.RegisterCallback<ClickEvent>(_ => SetTickSpeed(1));
        _btnSpeed2x.RegisterCallback<ClickEvent>(_ => SetTickSpeed(2));
        _btnSpeed10x.RegisterCallback<ClickEvent>(_ => SetTickSpeed(10));
        _btnSpeed50x.RegisterCallback<ClickEvent>(_ => SetTickSpeed(50));
        _btnSpeed100x.RegisterCallback<ClickEvent>(_ => SetTickSpeed(100));

        root.Add(_timeControlsBar);
    }

    private void SetTickSpeed(int multiplier)
    {
        if (_currentSpeedMultiplier == multiplier) return; // no-op if same

        StartCoroutine(DoSetTickSpeed(multiplier));
    }

    private IEnumerator DoSetTickSpeed(int multiplier)
    {
        string url = $"{_gameHUDController.GetSimulationServerUrl().TrimEnd('/')}/tick/set-speed?multiplier={multiplier}";
        using (var req = UnityWebRequest.PostWwwForm(url, ""))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                _currentSpeedMultiplier = multiplier;
                UpdateSpeedButtonStyles();
                Debug.Log($"[TimeControlsController] Speed set to {multiplier}x");
            }
            else
            {
                Debug.LogWarning($"[TimeControlsController] Failed to set speed: {req.error}");
            }
        }
    }

    private void UpdateSpeedButtonStyles()
    {
        var buttons = new[] { _btnSpeed1x, _btnSpeed2x, _btnSpeed10x, _btnSpeed50x, _btnSpeed100x };
        var multipliers = new[] { 1, 2, 10, 50, 100 };

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
            {
                buttons[i].EnableInClassList("time-controls__btn--active", multipliers[i] == _currentSpeedMultiplier);
            }
        }
    }

    public void SetTickProgress(int currentTick, int totalTicks)
    {
        if (_labelTickProgress != null)
        {
            _labelTickProgress.text = totalTicks > 0
                ? $"Tick {currentTick}/{totalTicks}"
                : $"Tick {currentTick}";
        }
    }

    public void SetSpeedMultiplier(int multiplier)
    {
        _currentSpeedMultiplier = multiplier;
        UpdateSpeedButtonStyles();
    }
}