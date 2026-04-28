using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestion de l'input (survol + clic) pour la vue plan tangent local.
///
/// Détecte les tuiles GP via Physics.Raycast contre le MeshCollider de PlanetTangentMesh.
/// Convertit le triangleIndex en GoldbergFace et déclenche OnRegionClicked,
/// même interface que PlanetSphereGoldberg et PlanetFlatView.
/// </summary>
[RequireComponent(typeof(PlanetTangentView))]
public class PlanetTangentInput : MonoBehaviour
{
    // =========================================================
    // Event — même signature que PlanetSphereGoldberg.OnRegionClicked
    // =========================================================

    /// <summary>Déclenché lors d'un clic : latNorm [0–1], lonNorm [0–1].</summary>
    public event Action<float, float> OnRegionClicked;

    // =========================================================
    // Inspector
    // =========================================================

    [Tooltip("HUD pour afficher les infos de la tuile cliquée/survolée. Assigner en Inspector.")]
    [SerializeField] private TerraformHUD terraformHUD;

    // =========================================================
    // Runtime
    // =========================================================

    private PlanetTangentView _view;
    private Camera            _cam;

    private int _hoveredFaceId = -1;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _view = GetComponent<PlanetTangentView>();
        _cam  = Camera.main;

        if (terraformHUD == null)
            terraformHUD = FindAnyObjectByType<TerraformHUD>();
    }

    private void Update()
    {
        if (_cam == null || !_view.IsLoaded || Mouse.current == null) return;
        if (UIEventSystemUtility.IsPointerOverUI()) return;

        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        // RaycastAll : ignore les autres colliders (sphère GP, flat mesh…) qui pourraient bloquer
        PlanetTangentMesh tangentMesh = _view.TangentMesh;
        if (tangentMesh == null)
        {
            ClearHover();
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(ray);
        RaycastHit hit    = default;
        bool found        = false;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.gameObject == tangentMesh.gameObject)
            {
                hit   = h;
                found = true;
                break;
            }
        }

        if (!found)
        {
            ClearHover();
            return;
        }

        GoldbergSphereGenerator.GoldbergFace face = _view.GetFaceFromTriangle(hit.triangleIndex);
        if (face.faceId < 0)
        {
            ClearHover();
            return;
        }

        // Hover
        if (face.faceId != _hoveredFaceId)
        {
            _hoveredFaceId = face.faceId;

            HexCell hoveredCell = GetCellForFace(face);
            if (hoveredCell != null)
                terraformHUD?.ShowHexPanel(hoveredCell);
        }

        // Clic gauche
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log($"[PlanetTangentInput] Clic | lat={face.latNorm:F3} lon={face.lonNorm:F3}");
            OnRegionClicked?.Invoke(face.latNorm, face.lonNorm);
        }
    }

    // =========================================================
    // Utilitaires
    // =========================================================

    private void ClearHover()
    {
        _hoveredFaceId = -1;
    }

    private HexCell GetCellForFace(GoldbergSphereGenerator.GoldbergFace face)
    {
        PlanetaryHexGrid.GridData grid = PlanetaryHexGrid.ActiveGrid;
        if (grid.Cells == null) return null;
        return PlanetaryHexGrid.GetCellAt(
            grid.Cells, grid.Cols, grid.Rows, face.latNorm, face.lonNorm);
    }
}
