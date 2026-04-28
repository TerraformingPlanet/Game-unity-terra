using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the LeftPanel UI component.
/// Handles terraformation progress, atmospheric state, scoreboard.
/// </summary>
public class LeftPanelController : MonoBehaviour
{
    private VisualElement _leftPanel;
    private Label _atmoLabel;
    private Label _scoreboardLabel;
    private bool _visible;
    private bool _hasAtmoData;   // shown only when real data has arrived

    private VisualTreeAsset _leftPanelTemplate;
    private GameHUDController _gameHUDController;

    public void Initialize(VisualElement root)
    {
        _gameHUDController = GetComponent<GameHUDController>();

        // Try to load template from Resources or use procedural build
        _leftPanelTemplate = Resources.Load<VisualTreeAsset>("UI/Templates/LeftPanel");

        if (_leftPanelTemplate != null)
        {
            _leftPanel = _leftPanelTemplate.Instantiate();
        }
        else
        {
            _leftPanel = BuildLeftPanelProcedural();
        }

        root.Add(_leftPanel);

        // Grab named elements (atmo + scoreboard only — progress is now in TopBar)
        _atmoLabel = _leftPanel.Q<Label>("atmo-label");
        _scoreboardLabel = _leftPanel.Q<Label>("scoreboard-label");

        // Start hidden; shown by SetVisible when entering Planet view
        _leftPanel.style.display = DisplayStyle.None;
    }

    public void StartPolling()
    {
        StartCoroutine(PollScoreboard());
    }

    /// <summary>Trigger a single scoreboard fetch (called on WS tick advance).</summary>
    public void RefreshScoreboardNow()
    {
        StartCoroutine(FetchScoreboardOnce());
    }

    private IEnumerator FetchScoreboardOnce()
    {
        string url = _gameHUDController.GetSimulationServerUrl().TrimEnd('/') + "/game/scoreboard";
        yield return SimHttp.Get(url, _gameHUDController.GetSimulationServerTimeout(),
            _ => SetScoreboard("Scoreboard: [TODO]"));
    }

    private IEnumerator PollScoreboard()
    {
        while (true)
        {
            string url = _gameHUDController.GetSimulationServerUrl().TrimEnd('/') + "/game/scoreboard";
            yield return SimHttp.Get(url, _gameHUDController.GetSimulationServerTimeout(),
                _ => SetScoreboard("Scoreboard: [TODO]"));
            yield return new WaitForSeconds(90f); // Fallback poll — WS handles real-time updates
        }
    }

    /// <summary>No-op — progress is now displayed in TopBar.</summary>
    public void SetProgress(float progress) { }

    public void SetAtmosphericState(AtmosphericState state)
    {
        if (_atmoLabel == null) return;
        if (state.habitabilityScore > 0f)
        {
            _atmoLabel.text = $"O2 {state.o2Ratio * 100f:F1}%   CO2 {state.co2Ratio * 100f:F3}%\n" +
                             $"T° {state.averageTemperature:F1}°C   {state.atmosphericPressure:F1} kPa\n" +
                             $"Habitabilité {state.habitabilityScore * 100f:F0}%";
            if (!_hasAtmoData)
            {
                _hasAtmoData = true;
                if (_visible) _leftPanel.style.display = DisplayStyle.Flex;
            }
        }
        else
        {
            _atmoLabel.text = "";
        }
    }

    public void SetScoreboard(string scoreboardText)
    {
        if (_scoreboardLabel != null) _scoreboardLabel.text = scoreboardText;
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_leftPanel == null) return;
        // Only actually show the panel when there is real atmospheric data
        _leftPanel.style.display = (visible && _hasAtmoData) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private VisualElement BuildLeftPanelProcedural()
    {
        var panel = new VisualElement { name = "left-panel" };
        panel.AddToClassList("left-panel");

        _atmoLabel = new Label { name = "atmo-label", text = "" };
        _atmoLabel.AddToClassList("left-panel__atmo");
        panel.Add(_atmoLabel);

        _scoreboardLabel = new Label { name = "scoreboard-label", text = "" };
        _scoreboardLabel.AddToClassList("left-panel__scoreboard");
        panel.Add(_scoreboardLabel);

        return panel;
    }
}