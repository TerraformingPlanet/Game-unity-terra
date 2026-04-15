using UnityEngine;

/// <summary>
/// Lightweight data container for a single hex cell.
/// Not a MonoBehaviour — no GameObject overhead.
/// </summary>
public class HexCell
{
    public int Q { get; private set; }
    public int R { get; private set; }

    public TerrainData terrain;
    public Vector3 center;

    public HexCell(int q, int r, TerrainData terrainData)
    {
        Q = q;
        R = r;
        terrain = terrainData;
        center = HexMetrics.AxialToWorld(q, r);
    }
}
