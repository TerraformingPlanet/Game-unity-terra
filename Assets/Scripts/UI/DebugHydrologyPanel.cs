using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Panneau debug in-game pour les tests de projection, cohérence et hydrologie.
/// Le composant reste découplé du HUD joueur et ne fait qu'appeler des APIs runtime dédiées.
/// </summary>
public class DebugHydrologyPanel : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private ViewManager viewManager;
    [SerializeField] private TerraformHUD terraformHUD;
    [SerializeField] private TerraformSystem terraformSystem;

    [Header("Racines UI")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private GameObject projectionSection;
    [SerializeField] private GameObject localSection;

    [Header("Lecture")]
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("Navigation projection")]
    [SerializeField] private TMP_InputField latitudeInput;
    [SerializeField] private TMP_InputField longitudeInput;
    [SerializeField] private Slider projectionWaterLevelSlider;
    [SerializeField] private TextMeshProUGUI projectionWaterLevelLabel;

    [Header("Niveau mer globe (debug instantané)")]
    [SerializeField] private Slider globeSeaLevelSlider;
    [SerializeField] private TextMeshProUGUI globeSeaLevelLabel;

    [Header("Actions locale")]
    [SerializeField] private Slider waterDeltaSlider;
    [SerializeField] private TextMeshProUGUI waterDeltaLabel;
    [SerializeField] private Slider temperatureDeltaSlider;
    [SerializeField] private TextMeshProUGUI temperatureDeltaLabel;

    [Header("Toggle")]
    [SerializeField] private Key toggleKey = Key.F10;
    [SerializeField] private bool visibleOnStart;

    private Button _openRegionButton;
    private Button _reloadProjectionButton;
    private Button _clearCacheReloadButton;
    private Button _applyProjectionWaterButton;
    private Button _resetProjectionWaterButton;
    private Button _applySelectedCellButton;
    private Button _regenerateLocalButton;
    private Button _toggleWaterSphereButton;
    private bool _listenersBound;
    private CanvasGroup _panelCanvasGroup;

    private void Awake()
    {
        UIEventSystemUtility.EnsureEventSystem();

        if (viewManager == null)
            viewManager = FindAnyObjectByType<ViewManager>();
        if (terraformHUD == null)
            terraformHUD = FindAnyObjectByType<TerraformHUD>();
        if (terraformSystem == null)
            terraformSystem = FindAnyObjectByType<TerraformSystem>();

        if (panelRoot == null)
            panelRoot = gameObject;

    ResolveControls();
    }

    private void OnEnable()
    {
        ViewManager.OnViewChanged += HandleViewChanged;
        SubscribePlanetSphereEvents();
    }

    private void Start()
    {
        EnsureProjectionControlsCreated();
        ResolveControls();
        EnsureCanvasGroup();
        SetPanelVisible(visibleOnStart);
        BindListeners();
        UpdateSliderLabels();
        RefreshPanel();
        SubscribePlanetSphereEvents();
    }

    private void Update()
    {
        // F10 est géré centralement par GameHUDController — on gère uniquement Escape ici
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && IsPanelVisible())
            SetPanelVisible(false);
    }

    private void OnDisable()
    {
        ViewManager.OnViewChanged -= HandleViewChanged;
        UnsubscribePlanetSphereEvents();
    }

    private PlanetSphereGoldberg _subscribedSphere;

    private void SubscribePlanetSphereEvents()
    {
        if (viewManager == null) viewManager = FindAnyObjectByType<ViewManager>();
        PlanetSphereGoldberg sphere = viewManager != null ? viewManager.ActivePlanetSphere : null;
        if (sphere == null || sphere == _subscribedSphere) return;
        UnsubscribePlanetSphereEvents();
        _subscribedSphere = sphere;
        sphere.OnH3TilesReady += HandleH3TilesReady;
    }

    private void UnsubscribePlanetSphereEvents()
    {
        if (_subscribedSphere != null)
        {
            _subscribedSphere.OnH3TilesReady -= HandleH3TilesReady;
            _subscribedSphere = null;
        }
    }

    private void HandleH3TilesReady(GoldbergTileState[] _, System.Collections.Generic.Dictionary<TerrainType, Color> __)
    {
        // Le fetch vient de terminer — resync le slider au ServerWaterLevel réel
        if (globeSeaLevelSlider == null) return;
        PlanetSphereGoldberg sphere = viewManager != null ? viewManager.ActivePlanetSphere : null;
        if (sphere == null) return;
        float wl = sphere.ServerWaterLevel;
        globeSeaLevelSlider.SetValueWithoutNotify(wl);
        HandleGlobeSeaLevelChanged(wl);
    }

    public void TogglePanel()
    {
        // Lazy init : si le panel était inactif dans la scène, Awake/Start n'ont pas tourné.
        // On force l'activation + initialisation avant le premier toggle.
        if (panelRoot == null)
        {
            panelRoot = gameObject;
            EnsureProjectionControlsCreated();
            ResolveControls();
            EnsureCanvasGroup();
            BindListeners();
            // Active le GameObject pour que OnEnable + l'UI soient fonctionnels
            if (!panelRoot.activeSelf)
                panelRoot.SetActive(true);
            // Start() sera déclenché par SetActive → il va appeler SetPanelVisible(visibleOnStart=false)
            // puis on impose visible=true juste après
        }

        SetPanelVisible(!IsPanelVisible());
        if (IsPanelVisible())
            RefreshPanel();
    }

    public void RefreshPanel()
    {
        ResolveControls();
        UpdateSectionVisibility();
        UpdateSliderLabels();
        RefreshStatus();
    }

    public void ReloadProjection()
    {
        if (viewManager != null && viewManager.ReloadCurrentProjection())
            RefreshStatus();
    }

    public void ClearCacheAndReloadProjection()
    {
        if (viewManager != null && viewManager.ClearAndReloadCurrentProjection())
            RefreshStatus();
    }

    public void ApplyProjectionWaterLevel()
    {
        if (viewManager == null || projectionWaterLevelSlider == null)
            return;

        if (viewManager.SetProjectionWaterLevel(projectionWaterLevelSlider.value))
            RefreshStatus();
    }

    public void ResetProjectionWaterLevel()
    {
        if (viewManager == null)
            return;

        if (viewManager.ResetProjectionWaterLevel())
        {
            if (projectionWaterLevelSlider != null)
                projectionWaterLevelSlider.SetValueWithoutNotify(0f);

            HandleProjectionWaterLevelChanged(0f);
            RefreshStatus();
        }
    }

    public void OpenRegionFromInputs()
    {
        if (viewManager == null)
            return;

        if (!TryParseNormalized(latitudeInput, out float latitude))
            return;
        if (!TryParseNormalized(longitudeInput, out float longitude))
            return;

        if (viewManager.TryOpenRegionNormalized(latitude, longitude))
            RefreshStatus();
    }

    public void RegenerateLocalRegion()
    {
        if (viewManager != null && viewManager.RegenerateCurrentLocalRegion())
            RefreshStatus();
    }

    public void ApplySelectedCellAdjustments()
    {
        if (terraformHUD == null || terraformSystem == null)
            return;

        HexCell selectedCell = terraformHUD.SelectedCell;
        if (selectedCell == null)
        {
            RefreshStatus("Aucune cellule sélectionnée.");
            return;
        }

        float waterDelta = waterDeltaSlider != null ? waterDeltaSlider.value : 0f;
        float temperatureDelta = temperatureDeltaSlider != null ? temperatureDeltaSlider.value : 0f;

        if (terraformSystem.DebugApplyDirectState(selectedCell, waterDelta, temperatureDelta))
        {
            terraformHUD.ShowHexPanel(selectedCell);
            viewManager?.ActiveHexGrid?.DebugDumpCellState(selectedCell);
            RefreshStatus();
        }
        else
        {
            RefreshStatus("Erreur : contexte de génération absent (SetContext non appelé ?).");
        }
    }

    public void HandleWaterDeltaChanged(float value)
    {
        if (waterDeltaLabel != null)
            waterDeltaLabel.text = $"Eau Δ {value * 100f:+0;-0;0}%";
    }

    public void HandleTemperatureDeltaChanged(float value)
    {
        if (temperatureDeltaLabel != null)
            temperatureDeltaLabel.text = $"Temp Δ {value:+0.0;-0.0;0.0}°C";
    }

    public void HandleProjectionWaterLevelChanged(float value)
    {
        if (projectionWaterLevelLabel != null)
            projectionWaterLevelLabel.text = $"Niveau d'eau {value:+0.00;-0.00;0.00}";

        if (viewManager == null || projectionWaterLevelSlider == null)
            return;

        if (viewManager.CurrentState != ViewManager.ViewState.Planet)
            return;

        if (Mathf.Approximately(viewManager.ActiveProjectionWaterLevel, value))
            return;

        if (viewManager.SetProjectionWaterLevel(value))
            RefreshStatus();
    }

    /// <summary>
    /// Ajuste le niveau de la mer visuellement en temps réel (sans re-fetch serveur).
    /// Utile pour tester l'impact du terraforming sur la colorisation altitude/mer.
    /// </summary>
    public void HandleGlobeSeaLevelChanged(float value)
    {
        if (globeSeaLevelLabel != null)
            globeSeaLevelLabel.text = $"Mer (globe) {value:+0.00;-0.00;0.00}";

        PlanetSphereGoldberg sphere = viewManager != null ? viewManager.ActivePlanetSphere : null;
        sphere?.RefreshAltitudeColorization(value);
    }

    public void HandleToggleWaterSphere()
    {
        PlanetSphereGoldberg sphere = viewManager != null ? viewManager.ActivePlanetSphere : null;
        if (sphere == null) return;

        bool nowVisible = sphere.ToggleWaterSphere();

        if (_toggleWaterSphereButton != null)
        {
            var label = _toggleWaterSphereButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = nowVisible ? "Cacher eau" : "Afficher eau";
        }
    }


    private void HandleViewChanged(ViewManager.ViewState _)
    {
        RefreshPanel();
    }

    private void ResolveControls()
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        if (projectionSection == null) { Transform t = transform.Find("ProjectionSection"); if (t != null) projectionSection = t.gameObject; }
        if (localSection == null)      { Transform t = transform.Find("LocalSection");      if (t != null) localSection      = t.gameObject; }
        EnsureProjectionControlsCreated();
        statusLabel ??= FindChild<TextMeshProUGUI>("StatusLabel");
        latitudeInput ??= FindChild<TMP_InputField>("LatitudeInput");
        longitudeInput ??= FindChild<TMP_InputField>("LongitudeInput");
        projectionWaterLevelSlider ??= FindChild<Slider>("ProjectionWaterLevelSlider");
        projectionWaterLevelLabel ??= FindChild<TextMeshProUGUI>("ProjectionWaterLevelLabel");
        globeSeaLevelSlider ??= FindChild<Slider>("GlobeSeaLevelSlider");
        globeSeaLevelLabel ??= FindChild<TextMeshProUGUI>("GlobeSeaLevelLabel");
        waterDeltaSlider ??= FindChild<Slider>("WaterDeltaSlider");
        waterDeltaLabel ??= FindChild<TextMeshProUGUI>("WaterDeltaLabel");
        temperatureDeltaSlider ??= FindChild<Slider>("TemperatureDeltaSlider");
        temperatureDeltaLabel ??= FindChild<TextMeshProUGUI>("TemperatureDeltaLabel");

        _openRegionButton ??= FindChild<Button>("OpenRegionButton");
        _reloadProjectionButton ??= FindChild<Button>("ReloadProjectionButton");
        _clearCacheReloadButton ??= FindChild<Button>("ClearCacheReloadButton");
        _applyProjectionWaterButton ??= FindChild<Button>("ApplyProjectionWaterButton");
        _resetProjectionWaterButton ??= FindChild<Button>("ResetProjectionWaterButton");
        _applySelectedCellButton ??= FindChild<Button>("ApplySelectedCellButton");
        _regenerateLocalButton ??= FindChild<Button>("RegenerateLocalButton");
        _toggleWaterSphereButton ??= FindChild<Button>("ToggleWaterSphereButton");
        EnsureLocalControlsConfigured();
        EnsureInteractiveRaycasts();
    }

    private void BindListeners()
    {
        if (_listenersBound)
            return;

        _openRegionButton?.onClick.AddListener(OpenRegionFromInputs);
        _reloadProjectionButton?.onClick.AddListener(ReloadProjection);
        _clearCacheReloadButton?.onClick.AddListener(ClearCacheAndReloadProjection);
        _applyProjectionWaterButton?.onClick.AddListener(ApplyProjectionWaterLevel);
        _resetProjectionWaterButton?.onClick.AddListener(ResetProjectionWaterLevel);
        _applySelectedCellButton?.onClick.AddListener(ApplySelectedCellAdjustments);
        _regenerateLocalButton?.onClick.AddListener(RegenerateLocalRegion);
        _toggleWaterSphereButton?.onClick.AddListener(HandleToggleWaterSphere);

        projectionWaterLevelSlider?.onValueChanged.AddListener(HandleProjectionWaterLevelChanged);
        globeSeaLevelSlider?.onValueChanged.AddListener(HandleGlobeSeaLevelChanged);
        waterDeltaSlider?.onValueChanged.AddListener(HandleWaterDeltaChanged);
        temperatureDeltaSlider?.onValueChanged.AddListener(HandleTemperatureDeltaChanged);
        _listenersBound = true;
    }

    private void UpdateSectionVisibility()
    {
        if (viewManager == null)
            return;

        bool isPlanetView = viewManager.CurrentState == ViewManager.ViewState.Planet;
        bool isLocalView = viewManager.CurrentState == ViewManager.ViewState.Local;

        if (projectionSection != null)
            projectionSection.SetActive(isPlanetView);

        if (localSection != null)
        {
            localSection.SetActive(isLocalView);

            RectTransform localRect = localSection.GetComponent<RectTransform>();
            if (localRect != null)
                localRect.anchoredPosition = new Vector2(16f, isLocalView ? -96f : -312f);
        }
    }

    private void UpdateSliderLabels()
    {
        if (projectionWaterLevelSlider != null)
        {
            float projectionLevel = viewManager != null ? viewManager.ActiveProjectionWaterLevel : projectionWaterLevelSlider.value;
            projectionWaterLevelSlider.SetValueWithoutNotify(projectionLevel);
            HandleProjectionWaterLevelChanged(projectionLevel);
        }

        if (globeSeaLevelSlider != null)
        {
            // Initialise le slider sur le waterLevel serveur (pour la colorisation)
            PlanetSphereGoldberg sphere = viewManager != null ? viewManager.ActivePlanetSphere : null;
            float currentSea = sphere != null ? sphere.ServerWaterLevel : 0f;
            globeSeaLevelSlider.SetValueWithoutNotify(currentSea);
            HandleGlobeSeaLevelChanged(currentSea);

            // Synchronise le libellé du bouton toggle
            if (_toggleWaterSphereButton != null)
            {
                var label = _toggleWaterSphereButton.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.text = sphere != null && sphere.IsWaterSphereVisible ? "Cacher eau" : "Afficher eau";
            }
        }

        if (waterDeltaSlider != null)
            HandleWaterDeltaChanged(waterDeltaSlider.value);
        if (temperatureDeltaSlider != null)
            HandleTemperatureDeltaChanged(temperatureDeltaSlider.value);
    }

    private void RefreshStatus(string prefix = null)
    {
        if (statusLabel == null)
            return;

        if (viewManager == null)
        {
            statusLabel.text = "Debug panel: ViewManager introuvable.";
            return;
        }

        string state = viewManager.CurrentState.ToString();
        string planet = viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune";
        string selectedCell = terraformHUD != null && terraformHUD.SelectedCell != null
            ? $"({terraformHUD.SelectedCell.Q}, {terraformHUD.SelectedCell.R})"
            : "Aucune";

        string regionInfo = string.Empty;
        GenerationContext regionContext = terraformHUD != null ? terraformHUD.RegionContext : null;
        if (regionContext != null && regionContext.region != null)
        {
            regionInfo = $" | lat {regionContext.region.latitude:F2} lon {regionContext.region.longitude:F2}";
        }

        string projectionWater = viewManager != null ? $" | niveau eau proj={viewManager.ActiveProjectionWaterLevel:+0.00;-0.00;0.00}" : string.Empty;
        string message = $"Vue={state} | planète={planet}{regionInfo}{projectionWater} | sélection={selectedCell}";

        if (viewManager.CurrentState == ViewManager.ViewState.Local &&
            viewManager.ActiveHexGrid != null &&
            viewManager.ActiveHexGrid.TryBuildDebugSummary(out HexGridDebugSummary summary))
        {
            message += "\n" + summary.FormatMultiline();
        }

        statusLabel.text = string.IsNullOrEmpty(prefix) ? message : prefix + "\n" + message;
    }

    private static bool TryParseNormalized(TMP_InputField input, out float value)
    {
        value = 0f;
        if (input == null)
            return false;

        if (!float.TryParse(input.text, out value))
            return false;

        value = Mathf.Clamp01(value);
        return true;
    }

    private T FindChild<T>(string name) where T : Component
    {
        if (panelRoot == null)
            return null;

        Transform child = panelRoot.transform.Find(name);
        if (child != null)
            return child.GetComponent<T>();

        foreach (Transform descendant in panelRoot.GetComponentsInChildren<Transform>(true))
        {
            if (descendant.name == name)
                return descendant.GetComponent<T>();
        }

        return null;
    }

    private GameObject FindChildObject(string name)
    {
        if (panelRoot == null)
            return null;

        foreach (Transform descendant in panelRoot.GetComponentsInChildren<Transform>(true))
        {
            if (descendant.name == name)
                return descendant.gameObject;
        }

        return null;
    }

    private void EnsureCanvasGroup()
    {
        if (panelRoot == null)
            return;

        _panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
        if (_panelCanvasGroup == null)
            _panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();
    }

    private bool IsPanelVisible()
    {
        if (panelRoot == null)
            return false;

        if (_panelCanvasGroup == null)
            EnsureCanvasGroup();

        return _panelCanvasGroup != null ? _panelCanvasGroup.alpha > 0.001f : panelRoot.activeSelf;
    }

    private void SetPanelVisible(bool visible)
    {
        if (panelRoot == null)
            return;

        if (_panelCanvasGroup == null)
            EnsureCanvasGroup();

        if (_panelCanvasGroup != null)
        {
            _panelCanvasGroup.alpha = visible ? 1f : 0f;
            _panelCanvasGroup.interactable = visible;
            _panelCanvasGroup.blocksRaycasts = visible;
        }

        if (!panelRoot.activeSelf)
            panelRoot.SetActive(true);
    }

    private void EnsureProjectionControlsCreated()
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        // transform.Find fonctionne même si le GO racine est inactif (contrairement à GetComponentsInChildren)
        if (projectionSection == null)
        {
            Transform t = transform.Find("ProjectionSection");
            if (t != null) projectionSection = t.gameObject;
        }
        if (localSection == null)
        {
            Transform t = transform.Find("LocalSection");
            if (t != null) localSection = t.gameObject;
        }
        if (projectionSection == null)
            return;

        RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
        if (panelRect != null)
            panelRect.sizeDelta = new Vector2(360f, 470f);

        RectTransform projectionRect = projectionSection.GetComponent<RectTransform>();
        if (projectionRect != null)
            projectionRect.sizeDelta = new Vector2(-32f, 295f);

        RectTransform localRect = localSection != null ? localSection.GetComponent<RectTransform>() : null;
        if (localRect != null)
            localRect.anchoredPosition = new Vector2(16f, -356f);  // décalé vers le bas

        CreateProjectionSectionControls();
        EnsureLocalControlsConfigured();
    }

    private void CreateProjectionSectionControls()
    {
        CreateLabel(projectionSection.transform,
                    "ProjectionWaterLevelLabel",
                    new Vector2(0f, -146f),
                    new Vector2(310f, 24f),
                    "Niveau d'eau +0.00",
                    16f,
                    TextAlignmentOptions.Left);

        CreateSlider(projectionSection.transform,
                     "ProjectionWaterLevelSlider",
                     new Vector2(0f, -174f),
                     new Vector2(310f, 20f),
                     -0.45f,
                     0.45f,
                     0f);

        CreateButton(projectionSection.transform,
                     "ApplyProjectionWaterButton",
                     new Vector2(0f, -202f),
                     new Vector2(150f, 30f),
                     "Appliquer niveau");

        CreateButton(projectionSection.transform,
                     "ResetProjectionWaterButton",
                     new Vector2(160f, -202f),
                     new Vector2(150f, 30f),
                     "Reset niveau");

        CreateGlobeSeaLevelControls();
    }

    private void CreateGlobeSeaLevelControls()
    {
        CreateLabel(projectionSection.transform,
                    "GlobeSeaLevelLabel",
                    new Vector2(0f, -238f),
                    new Vector2(200f, 24f),
                    "Mer (globe) +0.00",
                    16f,
                    TextAlignmentOptions.Left);

        CreateButton(projectionSection.transform,
                     "ToggleWaterSphereButton",
                     new Vector2(210f, -238f),
                     new Vector2(100f, 24f),
                     "Cacher eau");

        CreateSlider(projectionSection.transform,
                     "GlobeSeaLevelSlider",
                     new Vector2(0f, -262f),
                     new Vector2(310f, 20f),
                     -1f,
                     1f,
                     0f);
    }

    private void EnsureLocalControlsConfigured()
    {
        if (localSection == null)
            return;

        RectTransform localRect = localSection.GetComponent<RectTransform>();
        if (localRect != null)
            localRect.sizeDelta = new Vector2(-32f, 190f);

        ConfigureLabelRect(waterDeltaLabel, new Vector2(0f, -30f), new Vector2(310f, 24f));
        ConfigureSlider(waterDeltaSlider, new Vector2(0f, -58f), new Vector2(310f, 20f), -1f, 1f);

        ConfigureLabelRect(temperatureDeltaLabel, new Vector2(0f, -92f), new Vector2(310f, 24f));
        ConfigureSlider(temperatureDeltaSlider, new Vector2(0f, -120f), new Vector2(310f, 20f), -40f, 40f);

        ConfigureButtonRect(_applySelectedCellButton, new Vector2(0f, -146f), new Vector2(150f, 30f));
        ConfigureButtonRect(_regenerateLocalButton, new Vector2(160f, -146f), new Vector2(150f, 30f));
    }

    private void EnsureInteractiveRaycasts()
    {
        if (panelRoot == null)
            return;

        foreach (Graphic graphic in panelRoot.GetComponentsInChildren<Graphic>(true))
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

        if (graphic.GetComponent<Slider>() != null || graphic.GetComponentInParent<Slider>() != null)
            return true;

        if (graphic.GetComponent<Button>() != null)
            return true;

        if (graphic.GetComponentInParent<Button>() != null)
            return graphic.GetComponent<TextMeshProUGUI>() == null;

        if (graphic.GetComponent<TMP_InputField>() != null)
            return true;

        if (graphic.GetComponentInParent<TMP_InputField>() != null)
            return graphic.GetComponent<TextMeshProUGUI>() == null;

        return false;
    }

    private static void ConfigureLabelRect(TextMeshProUGUI label, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        if (label == null)
            return;

        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.Left;
    }

    private static void ConfigureSlider(Slider slider, Vector2 anchoredPosition, Vector2 sizeDelta, float minValue, float maxValue)
    {
        if (slider == null)
            return;

        RectTransform rect = slider.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.wholeNumbers = false;
    }

    private static void ConfigureButtonRect(Button button, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        if (button == null)
            return;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private static GameObject CreateLabel(Transform parent,
                                          string name,
                                          Vector2 anchoredPosition,
                                          Vector2 sizeDelta,
                                          string textValue,
                                          float fontSize,
                                          TextAlignmentOptions alignment)
    {
        GameObject go = FindOrCreate(parent, name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = textValue;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        return go;
    }

    private static GameObject CreateButton(Transform parent,
                                           string name,
                                           Vector2 anchoredPosition,
                                           Vector2 sizeDelta,
                                           string label)
    {
        GameObject go = FindOrCreate(parent, name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        go.GetComponent<Image>().color = new Color(0.18f, 0.31f, 0.4f, 1f);

        GameObject labelObject = FindOrCreate(go.transform, "Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        return go;
    }

    private static GameObject CreateSlider(Transform parent,
                                           string name,
                                           Vector2 anchoredPosition,
                                           Vector2 sizeDelta,
                                           float minValue,
                                           float maxValue,
                                           float value)
    {
        GameObject go = FindOrCreate(parent, name, typeof(RectTransform), typeof(Slider));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Slider slider = go.GetComponent<Slider>();
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.value = value;
        slider.direction = Slider.Direction.LeftToRight;

        GameObject background = FindOrCreate(go.transform, "Background", typeof(RectTransform), typeof(Image));
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.25f);
        backgroundRect.anchorMax = new Vector2(1f, 0.75f);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = new Color(0.1f, 0.12f, 0.16f, 1f);

        GameObject fillArea = FindOrCreate(go.transform, "Fill Area", typeof(RectTransform));
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRect.offsetMin = new Vector2(10f, 0f);
        fillAreaRect.offsetMax = new Vector2(-10f, 0f);

        GameObject fill = FindOrCreate(fillArea.transform, "Fill", typeof(RectTransform), typeof(Image));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.15f, 0.45f, 0.78f, 1f);

        GameObject handleSlideArea = FindOrCreate(go.transform, "Handle Slide Area", typeof(RectTransform));
        RectTransform handleAreaRect = handleSlideArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10f, 0f);
        handleAreaRect.offsetMax = new Vector2(-10f, 0f);

        GameObject handle = FindOrCreate(handleSlideArea.transform, "Handle", typeof(RectTransform), typeof(Image));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18f, 18f);
        handle.GetComponent<Image>().color = new Color(0.87f, 0.9f, 0.98f, 1f);

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        return go;
    }

    private static GameObject FindOrCreate(Transform parent, string name, params System.Type[] components)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        GameObject go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }
}