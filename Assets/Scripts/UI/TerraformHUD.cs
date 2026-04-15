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

    [Header("Systèmes")]
    [SerializeField] private TerraformProgressTracker progressTracker;
    [SerializeField] private TerraformSystem          terraformSystem;

    [Header("Actions disponibles")]
    [SerializeField] private TerraformActionData[] actions;

    // =========================================================
    // Runtime
    // =========================================================

    private HexCell _selectedCell;
    private GenerationContext _regionContext;

    public HexCell SelectedCell => _selectedCell;
    public GenerationContext RegionContext => _regionContext;

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

        if (selectedHexPanel != null)
            selectedHexPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (progressTracker != null)
            progressTracker.OnProgressChanged -= UpdateProgress;
    }

    // =========================================================
    // API publique — appelée par ViewManager
    // =========================================================

    /// <summary>Affiche le panel d'un hex sélectionné.</summary>
    public void ShowHexPanel(HexCell cell)
    {
        _selectedCell = cell;
        if (selectedHexPanel != null) selectedHexPanel.SetActive(true);
        RefreshHexInfo();
    }

    public void SetRegionContext(GenerationContext ctx)
    {
        _regionContext = ctx;

        if (_selectedCell != null)
            RefreshHexInfo();
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

    // =========================================================
    // Mise à jour de l'affichage
    // =========================================================

    private void UpdateProgress(float ratio)
    {
        if (progressSlider != null)
            progressSlider.value = ratio;

        if (progressLabel != null)
            progressLabel.text = $"{ratio * 100f:F1}% Terraform.";
    }

    private void RefreshHexInfo()
    {
        if (hexInfoLabel == null || _selectedCell == null) return;

        HexPhysicalState s = _selectedCell.state;
        string terrain = _selectedCell.terrain != null ? _selectedCell.terrain.displayName : "?";
        string regionInfo = string.Empty;

        if (_regionContext != null)
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
