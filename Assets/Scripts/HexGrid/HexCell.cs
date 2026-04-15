using UnityEngine;

/// <summary>
/// Conteneur léger pour une case hexagonale.
/// Contient ses coordonnées axiales, son biome, sa couche et le corps céleste auquel elle appartient.
/// </summary>
public class HexCell
{
    public int Q { get; private set; }
    public int R { get; private set; }

    public TerrainData      terrain;
    public Vector3          center;
    public WorldLayer       layer = WorldLayer.Surface;
    public CelestialBodyData world;

    public HexCell(int q, int r)
    {
        Q = q;
        R = r;
        center = HexMetrics.AxialToWorld(q, r);
    }
}
