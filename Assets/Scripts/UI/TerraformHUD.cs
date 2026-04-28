using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD de terraformation — affiche la progression globale et les infos du hex sélectionné.
///
/// Composants UI attendus (assigner en Inspector) :
///   progressSlider   : Slider (valeur 0–1), représente le % de terraformation
///   progressLabel    : TextMeshProUGUI affichant "XX% Terraform."
///   selectedHexPanel : GameObject activé quand un hex est sélectionné
///   hexInfoLabel     : TextMeshProUGUI avec les infos du hex cliqué
///   actionButtons    : un bouton par TerraformAction (assigner dans Inspector)
///
/// Workflow :
///   1. TerraformProgressTracker.OnProgressChanged → UpdateProgress()
///   2. ViewManager.NotifyCellClicked() → ShowHexPanel(cell)
///   3. Clic bouton action → RequestAction(action)
/// </summary>
public partial class TerraformHUD : MonoBehaviour
{
    // =========================================================
    // Inspector
    // =========================================================

    [Header("Progression globale")]
    [SerializeField] private Slider         progressSlider;
    [SerializeField] private TextMeshProUGUI progressLabel;

    [Header("Panel hex sélectionné")]
    [SerializeField] private GameObject     selectedHexPanel;
    [SerializeField] private TextMeshProUGUI hexInfoLabel;
    [Tooltip("Bouton 'Voir en local' — visible seulement en vue Globe avec une tuile sélectionnée.")]
    [SerializeField] private Button openLocalButton;
    [Tooltip("Bouton 'Fermer' de l'overlay local — dans LocalOverlayPanel.")]
    [SerializeField] private Button closeLocalButton;

    [Header("Systèmes")]
    [SerializeField] private TerraformProgressTracker progressTracker;
    [SerializeField] private TerraformSystem          terraformSystem;
    [SerializeField] private ViewManager              viewManager;

    [Header("Actions disponibles")]
    [SerializeField] private TerraformActionData[] actions;

    [Header("Serveur de simulation")]
    [SerializeField] private bool preferServerActionDefinitions = true;
    [SerializeField] private GameConfig config;
    private string SimUrl     => config != null ? config.simulationServerUrl           : "http://127.0.0.1:8080";
    private float  SimTimeout => config != null ? config.simulationServerTimeoutSeconds : 15f;

    // =========================================================
    // Runtime
    // =========================================================

    private HexCell _selectedCell;
    private GenerationContext _regionContext;
    private bool _serverActionCatalogLoaded;
    private bool _hasAuthoritativeRegionState;
    private RegionState _authoritativeRegionState;

    public HexCell SelectedCell => _selectedCell;
    public GenerationContext RegionContext => _regionContext;
    public bool HasServerActionCatalog => _serverActionCatalogLoaded;
    public bool HasAuthoritativeRegionState => _hasAuthoritativeRegionState;
    public RegionState AuthoritativeRegionState => _authoritativeRegionState;

    // =========================================================
    // Events — consumed by GameHUD
    // =========================================================

    /// <summary>Fired from UpdateProgress() with the new 0–1 ratio.</summary>
    public event Action<float> OnProgressUpdated;

    /// <summary>Fired from SetAuthoritativeRegionState() after each server sync.</summary>
    public event Action<RegionState> OnRegionStateChanged;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Start()
    {
        if (progressTracker != null)
        {
            progressTracker.OnProgressChanged += UpdateProgress;
            progressTracker.Refresh(); // valeur initiale
        }

        if (terraformSystem != null)
        {
            terraformSystem.OnAuthoritativeCellSynchronized += HandleAuthoritativeCellSynchronized;
            terraformSystem.OnAuthoritativeWorldStateSynchronized += HandleAuthoritativeWorldStateSynchronized;
        }

        if (viewManager == null)
            viewManager = FindAnyObjectByType<ViewManager>();

        if (openLocalButton != null)
        {
            openLocalButton.onClick.AddListener(() => viewManager?.EnterLocalFromSelection());
            openLocalButton.gameObject.SetActive(false);
        }

        if (closeLocalButton != null)
            closeLocalButton.onClick.AddListener(() => viewManager?.CloseLocalOverlay());

        if (selectedHexPanel != null)
            selectedHexPanel.SetActive(false);

        if (preferServerActionDefinitions)
            StartCoroutine(SyncActionDefinitionsFromServer());
    }

    private void OnDestroy()
    {
        if (progressTracker != null)
            progressTracker.OnProgressChanged -= UpdateProgress;

        if (terraformSystem != null)
        {
            terraformSystem.OnAuthoritativeCellSynchronized -= HandleAuthoritativeCellSynchronized;
            terraformSystem.OnAuthoritativeWorldStateSynchronized -= HandleAuthoritativeWorldStateSynchronized;
        }
    }

    // =========================================================
    // API publique — appelée par ViewManager
    // =========================================================

    /// <summary>Affiche le panel d'un hex sélectionné.</summary>
    public void ShowHexPanel(HexCell cell)
    {
        _selectedCell = cell;
        if (selectedHexPanel != null) selectedHexPanel.SetActive(true);

        // Bouton local visible uniquement en vue Globe (pas en vue Locale)
        bool inGlobeView = viewManager != null &&
                           viewManager.CurrentState == ViewManager.ViewState.Planet &&
                           viewManager.CurrentPlanetSubView == ViewManager.PlanetSubView.Globe;
        if (openLocalButton != null)
            openLocalButton.gameObject.SetActive(inGlobeView);

        RefreshHexInfo();
    }

    /// <summary>
    /// Remplace l'affichage hexInfoLabel avec les données authoritatives H3 du serveur.
    /// Appelé ~1–2s après le clic en vue Globe (asynchrone via GET /bodies/{id}/tiles/at).
    /// </summary>
    public void ShowH3TileInfo(GoldbergTileState tile)
    {
        if (hexInfoLabel == null) return;
        if (selectedHexPanel != null) selectedHexPanel.SetActive(true);

        string waterClass = tile.waterClassification switch
        {
            WaterClassification.OpenOcean   => "Océan",
            WaterClassification.InlandWater => "Eau intérieure",
            WaterClassification.Coast       => "Côte",
            WaterClassification.FrozenWater => "Eau gelée",
            _                               => "Sec"
        };
        string terrainClass = tile.terrainClass switch
        {
            TerrainClass.Ridge   => "Crête",
            TerrainClass.Basin   => "Bassin",
            TerrainClass.Channel => "Chenal",
            TerrainClass.Source  => "Source",
            _                    => "Pente"
        };
        string tileIdShort = !string.IsNullOrEmpty(tile.tileId) && tile.tileId.Length > 10
            ? tile.tileId[..10] + "…" : tile.tileId;

        hexInfoLabel.text =
            $"<b>{tile.terrainType} <size=70%>[H3]</size></b>\n" +
            $"Tuile  : {tileIdShort}\n" +
            $"Temp   : {tile.temperature:F1}°C\n" +
            $"Eau    : {tile.waterRatio * 100f:F0}%\n" +
            $"Hydro  : {waterClass} | relief {terrainClass}\n" +
            $"Toxines : {tile.toxinLevel * 100f:F0}%\n" +
            $"Habitable : {(tile.isHabitable ? "Oui" : "Non")}";
    }

    public void SetRegionContext(GenerationContext ctx)
    {
        _regionContext = ctx;

        if (_selectedCell != null)
            RefreshHexInfo();
    }

    public void SetAuthoritativeRegionState(RegionState regionState)
    {
        _authoritativeRegionState = regionState;
        _hasAuthoritativeRegionState = regionState.isValid;

        if (progressTracker != null && regionState.isValid)
        {
            float progress = regionState.atmosphericState.habitabilityScore > 0f
                ? regionState.atmosphericState.habitabilityScore
                : regionState.terraformationProgress;
            progressTracker.SetAuthoritativeProgress(progress);
        }

        if (_selectedCell != null)
            RefreshHexInfo();

        OnRegionStateChanged?.Invoke(regionState);
    }

    public void ClearAuthoritativeRegionState()
    {
        _hasAuthoritativeRegionState = false;
        _authoritativeRegionState = default;
    }

    /// <summary>Ferme le panel hex.</summary>
    public void HideHexPanel()
    {
        _selectedCell = null;
        if (selectedHexPanel != null) selectedHexPanel.SetActive(false);
    }

    public void RefreshSelectedHexInfo()
    {
        RefreshHexInfo();
    }

}

