using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Menu d'accueil de test pour lancer rapidement des scénarios de validation.
/// Conçu pour rester simple : choix d'un preset, éventuellement override lat/lon, puis lancement via ViewManager.
/// </summary>
public class TestLaunchMenu : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private ViewManager viewManager;
    [SerializeField] private GameObject menuRoot;

    [Header("Presets")]
    [SerializeField] private TestScenarioPreset[] presets;
    [SerializeField] private TMP_Dropdown presetDropdown;
    [SerializeField] private TextMeshProUGUI descriptionLabel;

    [Header("Overrides")]
    [SerializeField] private TMP_InputField latitudeInput;
    [SerializeField] private TMP_InputField longitudeInput;

    [Header("Toggle")]
    [SerializeField] private bool visibleOnStart = true;
    [SerializeField] private Key toggleKey = Key.F9;

    private Button _launchButton;
    private bool _listenersBound;
    private int _selectedPresetIndex;
    private bool _hasDirectPresetButtons;
    private CanvasGroup _menuCanvasGroup;

    public TestScenarioPreset[] Presets => presets;

    private void Awake()
    {
        UIEventSystemUtility.EnsureEventSystem();

        if (viewManager == null)
            viewManager = FindFirstObjectByType<ViewManager>();

        if (menuRoot == null)
            menuRoot = gameObject;

        ResolveControls();
    }

    private void Start()
    {
        EnsureCanvasGroup();
        SetMenuVisible(visibleOnStart);
        BindListeners();
        RebuildDropdown();
        ApplyRuntimeLayout();
        RefreshMenuState();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            ToggleMenu();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && IsMenuVisible())
            SetMenuVisible(false);
    }

    public void ToggleMenu()
    {
        SetMenuVisible(!IsMenuVisible());
        if (IsMenuVisible())
            RefreshMenuState();
    }

    public void RefreshSelectionInfo()
    {
        TestScenarioPreset preset = GetSelectedPreset();
        if (descriptionLabel == null)
            return;

        if (preset == null)
        {
            descriptionLabel.text = "Aucun preset sélectionné.";
            return;
        }

        string bodyName = preset.body != null ? preset.body.bodyName : "Aucun corps";
        descriptionLabel.text =
            $"{preset.displayName}\n" +
            $"Corps : {bodyName}\n" +
            $"Vue locale : {(preset.openLocalView ? "Oui" : "Non")}\n" +
            $"Lat/Lon : {preset.latitude:F2} / {preset.longitude:F2}\n\n" +
            preset.description;
    }

    public void LaunchSelectedPreset()
    {
        if (viewManager == null)
            return;

        TestScenarioPreset preset = GetSelectedPreset();
        if (preset == null || preset.body == null)
            return;

        float latitude = preset.latitude;
        float longitude = preset.longitude;

        if (TryParseNormalized(latitudeInput, out float latitudeOverride))
            latitude = latitudeOverride;
        if (TryParseNormalized(longitudeInput, out float longitudeOverride))
            longitude = longitudeOverride;

        if (preset.clearProjectionCacheBeforeLaunch)
            viewManager.ActivePlanetSphere?.ClearProjectionCache();

        if (viewManager.LaunchDebugScenario(preset, latitude, longitude))
            SetMenuVisible(false);
    }

    public void SelectPreset(int index)
    {
        _selectedPresetIndex = Mathf.Clamp(index, 0, Mathf.Max(0, presets.Length - 1));

        if (presetDropdown != null && presetDropdown.options != null && presetDropdown.options.Count > _selectedPresetIndex)
            presetDropdown.SetValueWithoutNotify(_selectedPresetIndex);

        SyncOverridesFromPreset();
        RefreshSelectionInfo();
    }

    public void LaunchPreset(int index)
    {
        SelectPreset(index);
        LaunchSelectedPreset();
    }

    private void RebuildDropdown()
    {
        ResolveControls();

        if (presetDropdown == null)
            return;

        presetDropdown.ClearOptions();
        List<string> options = new List<string>();

        foreach (TestScenarioPreset preset in presets)
            options.Add(preset != null ? preset.displayName : "Preset manquant");

        if (options.Count == 0)
            options.Add("Aucun preset");

        presetDropdown.AddOptions(options);
        presetDropdown.value = Mathf.Clamp(_selectedPresetIndex, 0, Mathf.Max(0, options.Count - 1));
        presetDropdown.RefreshShownValue();
    }

    private void RefreshMenuState()
    {
        ResolveControls();
        ApplyRuntimeLayout();
        EnsureInteractiveRaycasts();
        BringMenuToFront();
        SyncOverridesFromPreset();
        RefreshSelectionInfo();
    }

    private void SyncOverridesFromPreset()
    {
        TestScenarioPreset preset = GetSelectedPreset();
        if (preset == null)
            return;

        if (latitudeInput != null)
            latitudeInput.SetTextWithoutNotify(preset.latitude.ToString("0.00"));

        if (longitudeInput != null)
            longitudeInput.SetTextWithoutNotify(preset.longitude.ToString("0.00"));
    }

    private void ApplyRuntimeLayout()
    {
        RectTransform rootRect = menuRoot != null ? menuRoot.GetComponent<RectTransform>() : null;
        if (rootRect != null)
            rootRect.sizeDelta = new Vector2(420f, _hasDirectPresetButtons ? 360f : 320f);

        if (_hasDirectPresetButtons && presetDropdown != null)
            presetDropdown.gameObject.SetActive(false);

        SetRectTransform("LatitudeInput", new Vector2(16f, -126f), new Vector2(180f, 30f));
        SetRectTransform("LongitudeInput", new Vector2(208f, -126f), new Vector2(180f, 30f));
        SetRectTransform("DescriptionLabel", new Vector2(16f, -166f), new Vector2(372f, 120f));
        SetRectTransform("LaunchPresetButton", new Vector2(16f, -300f), new Vector2(372f, 30f));
    }

    private TestScenarioPreset GetSelectedPreset()
    {
        if (presets == null || presets.Length == 0)
            return null;

        int index = presetDropdown != null ? presetDropdown.value : 0;
        if (presetDropdown == null || presetDropdown.options == null || presetDropdown.options.Count == 0)
            index = _selectedPresetIndex;

        if (index < 0 || index >= presets.Length)
            return null;

        return presets[index];
    }

    private static bool TryParseNormalized(TMP_InputField input, out float value)
    {
        value = 0f;
        if (input == null || string.IsNullOrWhiteSpace(input.text))
            return false;

        if (!float.TryParse(input.text, out value))
            return false;

        value = Mathf.Clamp01(value);
        return true;
    }

    private void ResolveControls()
    {
        if (menuRoot == null)
            menuRoot = gameObject;

        presetDropdown ??= FindChild<TMP_Dropdown>("PresetDropdown");
        descriptionLabel ??= FindChild<TextMeshProUGUI>("DescriptionLabel");
        latitudeInput ??= FindChild<TMP_InputField>("LatitudeInput");
        longitudeInput ??= FindChild<TMP_InputField>("LongitudeInput");
        _launchButton ??= FindChild<Button>("LaunchPresetButton");
    }

    private void BindListeners()
    {
        if (_listenersBound)
            return;

        if (presetDropdown != null)
            presetDropdown.onValueChanged.AddListener(index =>
            {
                _selectedPresetIndex = index;
                SyncOverridesFromPreset();
                RefreshSelectionInfo();
            });

        _launchButton?.onClick.AddListener(LaunchSelectedPreset);

        for (int i = 0; i < 16; i++)
        {
            Button button = FindChild<Button>($"PresetButton_{i}");
            if (button == null)
                continue;

            _hasDirectPresetButtons = true;
            int index = i;
            button.onClick.AddListener(() => LaunchPreset(index));
        }

        _listenersBound = true;
    }

    private void EnsureInteractiveRaycasts()
    {
        if (menuRoot == null)
            return;

        foreach (Graphic graphic in menuRoot.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null)
                continue;

            graphic.raycastTarget = ShouldReceiveRaycast(graphic);
        }
    }

    private static bool ShouldReceiveRaycast(Graphic graphic)
    {
        if (graphic == null)
            return false;

        if (graphic.GetComponent<Button>() != null)
            return true;

        if (graphic.GetComponentInParent<Button>() != null)
            return graphic.GetComponent<TextMeshProUGUI>() == null;

        if (graphic.GetComponent<TMP_Dropdown>() != null)
            return true;

        if (graphic.GetComponentInParent<TMP_Dropdown>() != null)
            return graphic.GetComponent<TextMeshProUGUI>() == null;

        if (graphic.GetComponent<TMP_InputField>() != null)
            return true;

        if (graphic.GetComponentInParent<TMP_InputField>() != null)
            return graphic.GetComponent<TextMeshProUGUI>() == null;

        return false;
    }

    private void BringMenuToFront()
    {
        if (menuRoot != null)
            menuRoot.transform.SetAsLastSibling();
    }

    private T FindChild<T>(string name) where T : Component
    {
        if (menuRoot == null)
            return null;

        foreach (Transform descendant in menuRoot.GetComponentsInChildren<Transform>(true))
        {
            if (descendant.name == name)
                return descendant.GetComponent<T>();
        }

        return null;
    }

    private void SetRectTransform(string childName, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        RectTransform rect = FindChild<RectTransform>(childName);
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private void EnsureCanvasGroup()
    {
        if (menuRoot == null)
            return;

        _menuCanvasGroup = menuRoot.GetComponent<CanvasGroup>();
        if (_menuCanvasGroup == null)
            _menuCanvasGroup = menuRoot.AddComponent<CanvasGroup>();
    }

    private bool IsMenuVisible()
    {
        if (menuRoot == null)
            return false;

        if (_menuCanvasGroup == null)
            EnsureCanvasGroup();

        return _menuCanvasGroup != null ? _menuCanvasGroup.alpha > 0.001f : menuRoot.activeSelf;
    }

    private void SetMenuVisible(bool visible)
    {
        if (menuRoot == null)
            return;

        if (_menuCanvasGroup == null)
            EnsureCanvasGroup();

        if (_menuCanvasGroup != null)
        {
            _menuCanvasGroup.alpha = visible ? 1f : 0f;
            _menuCanvasGroup.interactable = visible;
            _menuCanvasGroup.blocksRaycasts = visible;
        }

        if (!menuRoot.activeSelf)
            menuRoot.SetActive(true);
    }
}