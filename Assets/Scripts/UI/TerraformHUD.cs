using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
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
public class TerraformHUD : MonoBehaviour
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
    [SerializeField] private string simulationServerUrl = "http://127.0.0.1:8080";
    [SerializeField] private float simulationServerTimeoutSeconds = 2f;

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

    // =========================================================
    // Handlers boutons (relier depuis l'Inspector via OnClick())
    // =========================================================

    /// <summary>
    /// Appelé par les boutons action en Inspector.
    /// Passer l'index dans le tableau actions[] (0=Heat, 1=Irrigate…).
    /// </summary>
    public void RequestAction(int actionIndex)
    {
        if (_selectedCell == null)
        {
            Debug.Log("[TerraformHUD] Aucun hex sélectionné.");
            return;
        }
        if (actionIndex < 0 || actionIndex >= actions.Length)
        {
            Debug.LogWarning("[TerraformHUD] Index d'action invalide : " + actionIndex);
            return;
        }

        bool ok = terraformSystem.ApplyAction(_selectedCell, actions[actionIndex]);
        if (ok)
            Debug.Log($"[TerraformHUD] Action {actions[actionIndex].displayName} soumise.");
        else
            Debug.Log($"[TerraformHUD] Action {actions[actionIndex].displayName} refusée (pré-conditions non remplies).");
    }

    private IEnumerator SyncActionDefinitionsFromServer()
    {
        if (actions == null || actions.Length == 0)
            yield break;

        string requestUrl = $"{simulationServerUrl.TrimEnd('/')}/actions/catalog";
        using UnityWebRequest request = UnityWebRequest.Get(requestUrl);
        request.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[TerraformHUD] Catalogue d'actions serveur indisponible ({request.error}). Fallback local conserve.");
            yield break;
        }

        SimulationActionCatalog catalog;
        try
        {
            catalog = JsonUtility.FromJson<SimulationActionCatalog>(request.downloadHandler.text);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TerraformHUD] Catalogue d'actions serveur invalide ({ex.Message}). Fallback local conserve.");
            yield break;
        }

        if (catalog.actions == null || catalog.actions.Length == 0)
        {
            Debug.LogWarning("[TerraformHUD] Catalogue d'actions serveur vide. Fallback local conserve.");
            yield break;
        }

        int appliedCount = 0;
        for (int index = 0; index < actions.Length; index++)
        {
            TerraformActionData action = actions[index];
            if (action == null)
                continue;

            for (int definitionIndex = 0; definitionIndex < catalog.actions.Length; definitionIndex++)
            {
                SimulationActionDefinition definition = catalog.actions[definitionIndex];
                if (definition.actionType != action.actionType)
                    continue;

                action.ApplyAuthoritativeDefinition(definition);
                appliedCount++;
                break;
            }
        }

        _serverActionCatalogLoaded = appliedCount > 0;
        if (_serverActionCatalogLoaded)
            Debug.Log($"[TerraformHUD] Catalogue d'actions serveur synchronise ({appliedCount} action(s)).");
    }

    private void HandleAuthoritativeCellSynchronized(HexCell cell)
    {
        if (cell == null || _selectedCell == null)
            return;

        if (_selectedCell.Q != cell.Q || _selectedCell.R != cell.R)
            return;

        ShowHexPanel(cell);
    }

    private void HandleAuthoritativeWorldStateSynchronized(WorldState worldState)
    {
        if (!worldState.hasRegion)
            return;

        SetAuthoritativeRegionState(worldState.region);
    }

    // =========================================================
    // Mise à jour de l'affichage
    // =========================================================

    private void UpdateProgress(float ratio)
    {
        if (progressSlider != null)
            progressSlider.value = ratio;

        if (progressLabel != null)
            progressLabel.text = $"{ratio * 100f:F1}% Terraform.";

        OnProgressUpdated?.Invoke(ratio);
    }

    private void RefreshHexInfo()
    {
        if (hexInfoLabel == null || _selectedCell == null) return;

        HexPhysicalState s = _selectedCell.state;
        string terrain = _selectedCell.terrain != null ? _selectedCell.terrain.displayName : "?";
        string regionInfo = string.Empty;

        if (_regionContext != null)
        {
            if (_hasAuthoritativeRegionState && _authoritativeRegionState.isValid)
            {
                string bodyName = !string.IsNullOrEmpty(_authoritativeRegionState.planetName)
                    ? _authoritativeRegionState.planetName
                    : (_regionContext.body != null ? _regionContext.body.bodyName : "?");

                AtmosphericState atm = _authoritativeRegionState.atmosphericState;
                                string atmLine = atm.habitabilityScore > 0f
                                        ? $"Atmosphere : O2 {atm.o2Ratio * 100f:F1}% | CO2 {atm.co2Ratio * 100f:F2}% | {atm.atmosphericPressure:F1} kPa | T {atm.averageTemperature:F1}°C\n" +
                      $"Habitabilité : {atm.habitabilityScore * 100f:F1}% | Toxines {atm.toxinRatio * 100f:F0}%\n"
                    : string.Empty;
                regionInfo =
                    $"Astre : {bodyName}\n" +
                    $"Région : lat {_authoritativeRegionState.coordinates.latitude:F2} | lon {_authoritativeRegionState.coordinates.longitude:F2}\n" +
                    $"Projection : {_authoritativeRegionState.coherence.dominantTerrainType} | eau {_authoritativeRegionState.coherence.projectedWaterRatio * 100f:F0}%\n" +
                    $"Climat : dT {_authoritativeRegionState.weather.temperatureOffset:+0.0;-0.0;0.0}°C | pluie {_authoritativeRegionState.weather.precipitationRate * 100f:F0}%\n" +
                    $"Vent : {_authoritativeRegionState.weather.prevailingWindSpeed:F2} ({_authoritativeRegionState.weather.prevailingWindDirection.x:F1}, {_authoritativeRegionState.weather.prevailingWindDirection.y:F1})\n" +
                    $"Cohérence : mer {_authoritativeRegionState.coherence.oceanicity:F2} | aride {_authoritativeRegionState.coherence.deserticity:F2} | gel {_authoritativeRegionState.coherence.frigidity:F2}\n" +
                    atmLine + "\n";
            }
            else
            {
                MapRegion region = _regionContext.region;
                PlanetaryWeatherState weather = _regionContext.weather;
                MapRegion.CoherenceConstraint coherence = _regionContext.coherence;
                string bodyName = _regionContext.body != null ? _regionContext.body.bodyName : "?";
                float solarIntensity = region != null ? region.SolarIntensity : 1f;
                bool tidalLock = region != null && region.IsTidallyLocked;
                string projectedTerrain = region != null && region.projectedTerrain != null
                    ? region.projectedTerrain.displayName
                    : "?";

                regionInfo =
                    $"Astre : {bodyName}\n" +
                    $"Région : lat {region.latitude:F2} | lon {region.longitude:F2}\n" +
                    $"Projection : {projectedTerrain} | eau {region.projectedWaterRatio * 100f:F0}%\n" +
                    $"Solaire : {solarIntensity:F2} | Tidal lock : {(tidalLock ? "Oui" : "Non")}\n" +
                    $"Climat : dT {weather.temperatureOffset:+0.0;-0.0;0.0}°C | pluie {weather.precipitationRate * 100f:F0}%\n" +
                    $"Vent : {weather.prevailingWindSpeed:F2} ({weather.prevailingWindDir.x:F1}, {weather.prevailingWindDir.y:F1})\n" +
                    $"Cohérence : mer {coherence.oceanicity:F2} | aride {coherence.deserticity:F2} | gel {coherence.frigidity:F2}\n\n";
            }
        }

        hexInfoLabel.text =
            regionInfo +
            $"<b>{terrain}</b>\n" +
            $"Temp : {s.tempLocale:F1}°C\n" +
            $"Eau  : {s.waterRatio * 100f:F0}%\n" +
            $"Hydro : {FormatWaterClassification(s.waterClassification)} | relief {FormatTerrainClass(s.terrainClass)}\n" +
            $"Flux : {s.flowAccumulation} | aval : {FormatDownstream(s)}\n" +
            $"Exutoire : {FormatOverflowOutlet(s)}\n" +
            $"Toxines : {s.toxinLevel * 100f:F0}%\n" +
            $"Dureté  : {s.soil.rockHardness:F2}\n" +
            $"Minéraux : {s.soil.mineralDensity:F2}";
    }

    private static string FormatWaterClassification(WaterClassification classification)
    {
        return classification switch
        {
            WaterClassification.OpenOcean => "Océan",
            WaterClassification.InlandWater => "Eau intérieure",
            WaterClassification.Coast => "Côte",
            WaterClassification.FrozenWater => "Eau gelée",
            _ => "Sec"
        };
    }

    private static string FormatTerrainClass(TerrainClass terrainClass)
    {
        return terrainClass switch
        {
            TerrainClass.Ridge => "Crête",
            TerrainClass.Basin => "Bassin",
            TerrainClass.Channel => "Chenal",
            TerrainClass.Source => "Source",
            _ => "Pente"
        };
    }

    private static string FormatDownstream(HexPhysicalState state)
    {
        return state.hasDownstream
            ? $"({state.downstreamQ}, {state.downstreamR})"
            : "Aucun";
    }

    private static string FormatOverflowOutlet(HexPhysicalState state)
    {
        return state.hasOverflowOutlet
            ? $"({state.overflowQ}, {state.overflowR})"
            : "Aucun";
    }
}
