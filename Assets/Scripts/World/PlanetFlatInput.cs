using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestion de l'input (survol + clic) pour la vue planétaire plate (Mercator).
///
/// Détecte les cellules via Physics.Raycast contre le MeshCollider de PlanetFlatMesh.
/// Convertit la position world XZ en coordonnées lat/lon normalisées [0–1]
/// et déclenche OnRegionClicked, même interface que PlanetSphereGoldberg.
/// </summary>
[RequireComponent(typeof(PlanetFlatView))]
public class PlanetFlatInput : MonoBehaviour
{
    // =========================================================
    // Event — même signature que PlanetSphereGoldberg.OnRegionClicked
    // =========================================================

    /// <summary>Déclenché lors d'un clic : latNorm [0–1], lonNorm [0–1].</summary>
    public event Action<float, float> OnRegionClicked;

    // =========================================================
    // Inspector
    // =========================================================

    [Tooltip("HUD pour afficher les infos de la tuile survolée. Assigner en Inspector.")]
    [SerializeField] private TerraformHUD terraformHUD;

    // =========================================================
    // Runtime
    // =========================================================

    private PlanetFlatView _flatView;
    private Camera         _cam;

    private int _hoveredGridIndex = -1;

    private void Awake()
    {
        _flatView = GetComponent<PlanetFlatView>();
        _cam      = Camera.main;

        if (terraformHUD == null)
            terraformHUD = FindFirstObjectByType<TerraformHUD>();
    }

    private void Update()
    {
        if (_cam == null || !_flatView.IsLoaded || Mouse.current == null) return;
        if (UIEventSystemUtility.IsPointerOverUI()) return;

        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            ClearHover();
            return;
        }

        // S'assure que le raycast touche bien le mesh de la vue plate
        if (hit.collider == null || hit.collider.gameObject != _flatView.MeshObject)
        {
            ClearHover();
            return;
        }

        int gridIndex = _flatView.GetGridIndexFromTriangle(hit.triangleIndex);
        if (gridIndex < 0)
        {
            ClearHover();
            return;
        }

        // Hover
        if (gridIndex != _hoveredGridIndex)
        {
            _flatView.SetHover(gridIndex);
            _hoveredGridIndex = gridIndex;

            // Tooltip au survol — données H3 authoritatives
            GoldbergTileState? h3Tile = _flatView.GetH3Tile(gridIndex);
            if (h3Tile.HasValue)
                terraformHUD?.ShowH3TileInfo(h3Tile.Value);
        }

        // Clic gauche
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            (float latNorm, float lonNorm) = _flatView.GridIndexToLatLon(gridIndex);
            GoldbergTileState? tile = _flatView.GetH3Tile(gridIndex);
            string terrain = tile.HasValue ? tile.Value.terrainType.ToString() : "?";
            Debug.Log($"[PlanetFlatInput] Clic tuile {gridIndex} | lat={latNorm:F3} lon={lonNorm:F3} | {terrain}");
            OnRegionClicked?.Invoke(latNorm, lonNorm);
        }
    }

    private void ClearHover()
    {
        if (_hoveredGridIndex >= 0)
        {
            _flatView.ClearHover(_hoveredGridIndex);
            _hoveredGridIndex = -1;
            terraformHUD?.HideHexPanel();
        }
    }
}
