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

    /// <summary>Ferme le panel hex.</summary>
    public void HideHexPanel()
    {
        _selectedCell = null;
        if (selectedHexPanel != null) selectedHexPanel.SetActive(false);
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

        hexInfoLabel.text =
            $"<b>{terrain}</b>\n" +
            $"Temp : {s.tempLocale:F1}°C\n" +
            $"Eau  : {s.waterRatio * 100f:F0}%\n" +
            $"Toxines : {s.toxinLevel * 100f:F0}%\n" +
            $"Dureté  : {s.soil.rockHardness:F2}\n" +
            $"Minéraux : {s.soil.mineralDensity:F2}";
    }
}
