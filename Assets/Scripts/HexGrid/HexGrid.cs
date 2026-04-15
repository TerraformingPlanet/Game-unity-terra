using UnityEngine;

/// <summary>
/// Generates the hexagonal planet grid using axial coordinates.
/// Stores all HexCells, then delegates mesh generation to HexMesh.
/// </summary>
public class HexGrid : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] private int radius = 5;

    [Header("Terrain Data (assign in Inspector)")]
    [SerializeField] private TerrainData[] terrainDataPool;

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
        Debug.Log("[HexGrid] Start — terrainDataPool count: " + (terrainDataPool != null ? terrainDataPool.Length : 0));

        if (terrainDataPool == null || terrainDataPool.Length == 0)
            Debug.LogWarning("[HexGrid] No TerrainData assigned — cells will be white.");

        CreateCells();
        Debug.Log("[HexGrid] Cells created: " + _cells.Length);

        if (_hexMesh == null) { Debug.LogError("[HexGrid] Cannot triangulate — HexMesh is null!"); return; }
        _hexMesh.Triangulate(_cells);
        Debug.Log("[HexGrid] Triangulate done.");
    }

    private void CreateCells()
    {
        // Count cells in a hex ring of given radius: 3r²+3r+1
        int count = 3 * radius * radius + 3 * radius + 1;
        _cells = new HexCell[count];
        int index = 0;

        for (int q = -radius; q <= radius; q++)
        {
            int rMin = Mathf.Max(-radius, -q - radius);
            int rMax = Mathf.Min(radius, -q + radius);
            for (int r = rMin; r <= rMax; r++)
            {
                TerrainData terrain = PickRandomTerrain();
                _cells[index++] = new HexCell(q, r, terrain);
            }
        }
    }

    private TerrainData PickRandomTerrain()
    {
        if (terrainDataPool == null || terrainDataPool.Length == 0) return null;
        return terrainDataPool[Random.Range(0, terrainDataPool.Length)];
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
