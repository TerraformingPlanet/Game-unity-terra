using UnityEngine;

/// <summary>
/// Generates the hexagonal planet grid using axial coordinates.
/// Stores all HexCells, then delegates mesh generation to HexMesh.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] private int radius = 5;

    [Header("Corps Céleste")]
    [SerializeField] private CelestialBodyData celestialBody;

    private HexCell[] _cells;
    private HexMesh _hexMesh;

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
        if (celestialBody == null)
            Debug.LogWarning("[HexGrid] Aucun CelestialBodyData assigné — les cellules seront blanches.");

        CreateCells();
        Debug.Log($"[HexGrid] {_cells.Length} cellules créées.");

        MapGenerator.Populate(_cells, celestialBody);

        if (_hexMesh == null) { Debug.LogError("[HexGrid] HexMesh introuvable !"); return; }
        _hexMesh.Triangulate(_cells);
        Debug.Log("[HexGrid] Triangulation terminée.");
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
                _cells[index++] = new HexCell(q, r);
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
}
