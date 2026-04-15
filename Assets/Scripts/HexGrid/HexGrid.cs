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

    public MapRegion CurrentRegion => _currentRegion;

    private void Awake()
    {
        _hexMesh = GetComponentInChildren<HexMesh>();
        if (_hexMesh == null)
            Debug.LogError("[HexGrid] HexMesh child not found!");
    }

    private void Start()
    {
        // L'initialisation est déléguée à ViewManager via LoadRegion().
        // Si aucun ViewManager n'est présent (ex. scène de test),
        // on utilise le fallback celestialBody pour un comportement standalone.
        if (celestialBody != null)
            Regenerate();
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

        if (_currentRegion?.planet != null)
            MapGenerator.Populate(_cells, _currentRegion);
        else
            MapGenerator.Populate(_cells, celestialBody);

        _hexMesh.Triangulate(_cells);
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
        if (_cells == null || _cells.Length == 0)
            return null;

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

    public HexCell GetCellFromTriangleIndex(int triangleIndex)
    {
        if (_cells == null || _cells.Length == 0 || triangleIndex < 0)
            return null;

        const int trianglesPerHex = 6;
        int cellIndex = triangleIndex / trianglesPerHex;
        if (cellIndex < 0 || cellIndex >= _cells.Length)
            return null;

        return _cells[cellIndex];
    }

    /// <summary>
    /// Met à jour la couleur visuelle d'un seul hex sans retrianguler tout le mesh.
    /// Appelé par TerraformSystem après modification du biome d'une cellule.
    /// </summary>
    public void RefreshCell(HexCell cell)
    {
        _hexMesh?.RefreshCell(cell);
    }

    public void RefreshAllCells()
    {
        if (_cells == null || _cells.Length == 0)
            return;

        foreach (HexCell cell in _cells)
            _hexMesh?.RefreshCell(cell);
    }

    /// <summary>Expose le tableau de cellules actuel (lecture seule, pour TerraformProgressTracker).</summary>
    public HexCell[] GetCells() => _cells;

    public bool HasCells() => _cells != null && _cells.Length > 0;

    public HexCell GetCell(int q, int r)
    {
        if (_cells == null)
            return null;

        foreach (HexCell cell in _cells)
        {
            if (cell.Q == q && cell.R == r)
                return cell;
        }

        return null;
    }

    public void DebugDumpCellState(HexCell cell)
    {
        if (cell == null)
        {
            Debug.LogWarning("[HexGrid] DebugDumpCellState: cellule nulle.");
            return;
        }

        HexPhysicalState state = cell.state;
        string terrainName = cell.terrain != null ? cell.terrain.displayName : "?";
        Debug.Log($"[HexGrid] ({cell.Q},{cell.R}) terrain={terrainName} alt={state.altitude:F2} temp={state.tempLocale:F1} eau={state.waterRatio:F2} hydro={state.waterClassification} relief={state.terrainClass} flux={state.flowAccumulation} aval=({state.downstreamQ},{state.downstreamR}) exutoire=({state.overflowQ},{state.overflowR})");
    }

    public Bounds GetWorldBounds()
    {
        if (_cells == null || _cells.Length == 0)
            return new Bounds(transform.position, Vector3.zero);

        Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, 0f, float.MinValue);

        foreach (HexCell cell in _cells)
        {
            Vector3 center = cell.center;
            min.x = Mathf.Min(min.x, center.x - HexMetrics.outerRadius);
            min.z = Mathf.Min(min.z, center.z - HexMetrics.verticalSpacing * 0.5f);
            max.x = Mathf.Max(max.x, center.x + HexMetrics.outerRadius);
            max.z = Mathf.Max(max.z, center.z + HexMetrics.verticalSpacing * 0.5f);
        }

        Bounds bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }
}
