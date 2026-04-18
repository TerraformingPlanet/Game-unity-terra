using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Overlay de debug pour la vue plate Mercator.
///
/// Deux modes :
///   - Labels toujours visibles (toggle) : un TextMeshPro world-space par tuile,
///     centré sur la tuile, affichant l'id et les coordonnées (Q, R).
///   - Tooltip au survol : géré par PlanetFlatInput → TerraformHUD (non géré ici).
///
/// Performance :
///   Les labels sont créés à la demande (ShowLabels) et détruits (HideLabels).
///   Pour des grilles de 4000+ tuiles, les labels peuvent être lents à créer.
///   Le système batch la création par frames pour éviter les freezes.
///
/// Setup Unity :
///   1. Ajouter ce composant sur le GO PlanetFlatView.
///   2. Assigner debugFont (TMP_FontAsset) en Inspector.
///   3. Assigner via DebugTileToggleButton.
/// </summary>
public class FlatDebugOverlay : MonoBehaviour
{
    // =========================================================
    // Inspector
    // =========================================================

    [Header("Rendu labels")]
    [Tooltip("Police TMP utilisée pour les labels. Si null, cherche la police par défaut TMP.")]
    [SerializeField] private TMP_FontAsset debugFont;

    [Tooltip("Taille des labels en unités monde.")]
    [SerializeField] private float labelWorldSize = 0.35f;

    [Tooltip("Couleur du texte de debug.")]
    [SerializeField] private Color labelColor = new Color(1f, 1f, 1f, 0.85f);

    [Tooltip("Couleur du fond des labels (0 alpha = pas de fond).")]
    [SerializeField] private Color bgColor = new Color(0f, 0f, 0f, 0.5f);

    [Tooltip("Nombre de labels créés par frame (évite les freezes sur grandes grilles).")]
    [SerializeField] private int labelsPerFrame = 50;

    // =========================================================
    // Runtime
    // =========================================================

    private readonly List<GameObject> _labelObjects = new List<GameObject>();
    private bool   _isVisible;
    private bool   _isBuilding;

    private PlanetFlatView _flatView;
    private GameObject     _meshObject;    // parent des labels (FlatMeshRenderer)

    public bool IsVisible => _isVisible;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _flatView = GetComponent<PlanetFlatView>();
    }

    private void OnDestroy()
    {
        ClearLabels();
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>Affiche les labels de debug sur toutes les tuiles.</summary>
    public void ShowLabels()
    {
        // Labels non disponibles après migration H3 (pas de données Q/R)
        Debug.LogWarning("[FlatDebugOverlay] Labels de debug non disponibles en mode H3.");
    }

    /// <summary>Cache et détruit tous les labels de debug.</summary>
    public void HideLabels()
    {
        _isVisible = false;
        StopAllCoroutines();
        _isBuilding = false;
        ClearLabels();
    }

    /// <summary>Bascule la visibilité des labels.</summary>
    public void Toggle()
    {
        if (_isVisible) HideLabels();
        else            ShowLabels();
    }

    // =========================================================
    // Internals
    // =========================================================

    private void OnPlanetDataChanged_Unused(PlanetaryHexGrid.GridData grid) { }

    private void BuildLabels(PlanetaryHexGrid.GridData grid)
    {
        // No-op après migration H3
    }

    private System.Collections.IEnumerator BuildLabelsCoroutine(PlanetaryHexGrid.GridData grid)
    {
        // No-op après migration H3
        _isBuilding = false;
        yield break;
    }

    private void CreateLabel_Unused(HexCell cell, int cols, int rows)
    {
        // Ancienne méthode Mercator — désactivée après migration H3.
    }

    private void ClearLabels()
    {
        foreach (var go in _labelObjects)
        {
            if (go != null)
                Destroy(go);
        }
        _labelObjects.Clear();
    }
}
