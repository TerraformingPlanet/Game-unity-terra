using UnityEngine;

/// <summary>
/// Generates the hexagonal planet grid using axial coordinates.
/// Stores all HexCells, then delegates mesh generation to HexMesh.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] private int radius = 5;

    [Header("Corps Céleste (fallback sans ViewManager)")]
    [SerializeField] private CelestialBodyData celestialBody;

    private HexCell[] _cells;
    private HexMesh _hexMesh;
    private MapRegion _currentRegion;

    private void Awake()
    {
        Debug.Log("[HexGrid] Awake");
        _hexMesh = GetComponentInChildren<HexMesh>();
        if (_hexMesh == null)
            Debug.LogError("[HexGrid] HexMesh child not found!");
        else
            Debug.Log("[HexGrid] HexMesh found: " + _hexMesh.name);
    }

    private void Start()
    {
        // L'initialisation est déléguée à ViewManager via LoadRegion().
        // Si aucun ViewManager n'est présent (ex. scène de test),
        // on utilise le fallback celestialBody pour un comportement standalone.
        if (celestialBody != null)
        {
            CreateCells();
            MapGenerator.Populate(_cells, celestialBody);
            _hexMesh.Triangulate(_cells);
            Debug.Log("[HexGrid] Init standalone (pas de ViewManager).");
        }
    }

    // =========================================================
    // API publique — appelée par ViewManager
    // =========================================================

    /// <summary>
    /// Charge une région et regénère la grille.
    /// Si region.planet est défini, il prend le pas sur celestialBody.
    /// </summary>
    public void LoadRegion(MapRegion region)
    {
        _currentRegion = region;
        if (region?.planet != null)
            celestialBody = region.planet;
        Regenerate();
    }

    /// <summary>Regénère les cellules et retrangule le mesh.</summary>
    public void Regenerate()
    {
        if (_hexMesh == null) { Debug.LogError("[HexGrid] HexMesh introuvable !"); return; }
        CreateCells();
        MapGenerator.Populate(_cells, celestialBody);
        _hexMesh.Triangulate(_cells);
        Debug.Log($"[HexGrid] Régénéré — {_cells.Length} cellules.");
    }

    private void CreateCells()
    {
        int count = 3 * radius * radius + 3 * radius + 1;
        _cells = new HexCell[count];
        int index = 0;

        for (int q = -radius; q <= radius; q++)
        {
            int rMin = Mathf.Max(-radius, -q - radius);
            int rMax = Mathf.Min(radius, -q + radius);
            for (int r = rMin; r <= rMax; r++)
            {
                _cells[index] = new HexCell(q, r) { gridIndex = index };
                index++;
            }
        }
    }

    /// <summary>Returns the cell under the given world position (XZ plane), or null.</summary>
    public HexCell GetCellAt(Vector3 worldPos)
    {
        // Inverse of AxialToWorld: find closest cell by brute-force (fine for radius ≤ 50)
        HexCell closest = null;
        float minDist = float.MaxValue;
        foreach (HexCell cell in _cells)
        {
            float dist = Vector3.Distance(
                new Vector3(worldPos.x, 0f, worldPos.z),
                new Vector3(cell.center.x, 0f, cell.center.z)
            );
            if (dist < minDist)
            {
                minDist = dist;
                closest = cell;
            }
        }
        // Only return cell if the point is reasonably inside the hex
        return (minDist <= HexMetrics.outerRadius) ? closest : null;
    }

    /// <summary>
    /// Met à jour la couleur visuelle d'un seul hex sans retrianguler tout le mesh.
    /// Appelé par TerraformSystem après modification du biome d'une cellule.
    /// </summary>
    public void RefreshCell(HexCell cell)
    {
        _hexMesh?.RefreshCell(cell);
    }

    /// <summary>Expose le tableau de cellules actuel (lecture seule, pour TerraformProgressTracker).</summary>
    public HexCell[] GetCells() => _cells;
}
