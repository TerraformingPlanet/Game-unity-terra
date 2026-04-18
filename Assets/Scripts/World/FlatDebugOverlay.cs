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
        PlanetaryHexGrid.OnPlanetDataChanged += OnPlanetDataChanged;
    }

    private void OnDestroy()
    {
        PlanetaryHexGrid.OnPlanetDataChanged -= OnPlanetDataChanged;
        ClearLabels();
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>Affiche les labels de debug sur toutes les tuiles.</summary>
    public void ShowLabels()
    {
        if (!_flatView.IsLoaded) return;
        _isVisible = true;
        BuildLabels(PlanetaryHexGrid.ActiveGrid);
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

    private void OnPlanetDataChanged(PlanetaryHexGrid.GridData grid)
    {
        if (!gameObject.activeInHierarchy || !_isVisible) return;
        // Rebuild labels si la grille change (résolution différente)
        HideLabels();
        _isVisible = true;
        BuildLabels(grid);
    }

    private void BuildLabels(PlanetaryHexGrid.GridData grid)
    {
        if (grid.Cells == null || grid.Cells.Length == 0) return;

        // Récupère le GO du mesh (parent des labels) depuis PlanetFlatView
        if (_meshObject == null)
            _meshObject = _flatView.MeshObject;
        if (_meshObject == null) return;

        ClearLabels();
        StartCoroutine(BuildLabelsCoroutine(grid));
    }

    private System.Collections.IEnumerator BuildLabelsCoroutine(PlanetaryHexGrid.GridData grid)
    {
        _isBuilding = true;
        int count = 0;

        for (int i = 0; i < grid.Cells.Length; i++)
        {
            if (!_isVisible) yield break;

            HexCell cell = grid.Cells[i];
            CreateLabel(cell, grid.Cols, grid.Rows);
            count++;

            if (count >= labelsPerFrame)
            {
                count = 0;
                yield return null;  // attend la frame suivante
            }
        }

        _isBuilding = false;
    }

    private void CreateLabel(HexCell cell, int cols, int rows)
    {
        // Position world du centre de la tuile (même formule que PlanetFlatMesh.MercatorCenter)
        Vector3 localPos  = PlanetFlatMesh.MercatorCenter(cell.Q, cell.R, cols, rows);
        // Décale de l'offset du meshObject
        Vector3 worldPos  = _meshObject.transform.TransformPoint(localPos);
        // Légèrement au-dessus du plan pour éviter le z-fighting
        worldPos.y += 0.05f;

        var go  = new GameObject($"DebugLabel_{cell.gridIndex}");
        go.transform.SetParent(_meshObject.transform, false);
        go.transform.localPosition = localPos + Vector3.up * 0.05f;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);  // face caméra top-down
        go.transform.localScale    = Vector3.one * labelWorldSize;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text               = $"<b>{cell.gridIndex}</b>\n{cell.Q},{cell.R}";
        tmp.fontSize           = 2f;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.color              = labelColor;
        tmp.enableWordWrapping = false;
        tmp.overflowMode       = TextOverflowModes.Overflow;
        tmp.sortingOrder       = 1;

        if (debugFont != null)
            tmp.font = debugFont;

        _labelObjects.Add(go);
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
