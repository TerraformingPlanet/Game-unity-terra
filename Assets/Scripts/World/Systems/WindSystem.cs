using UnityEngine;

/// <summary>
/// Passe 4 du pipeline — calcul du vent local par hex.
///
/// Lit  : cell.state.altitude, ctx.weather (vent régional de base)
/// Écrit: cell.state.windVector, cell.state.windSpeed
///
/// Le vent local = vent régional de base
///               + boost altitudinal (plus de vent en altitude)
///               + turbulence topographique (hex entourés de reliefs)
///
/// Le vecteur vent est en 2D (plan XZ, unité normalisée × vitesse).
/// La vitesse [0–1] est utilisée par BiomeSystem (érosion éolienne)
/// et par un futur système de propagation de feu/toxines.
/// </summary>
public class WindSystem : IHexSystem
{
    // Facteur de boost altitude : +80% de vitesse au sommet (altitude=1)
    private const float AltitudeWindBoost = 0.8f;

    // Amplitude de la turbulence terrain : voisin plus haut = vent dévié
    private const float TerrainDisturbance = 0.3f;

    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        Vector2 baseDir   = ctx.weather.prevailingWindDir;
        float   baseSpeed = ctx.weather.prevailingWindSpeed;

        if (baseDir == Vector2.zero) baseDir = Vector2.right; // failsafe

        foreach (HexCell cell in cells)
        {
            float altitude = cell.state.altitude;

            // Boost altitude
            float localSpeed = baseSpeed * (1f + altitude * AltitudeWindBoost);

            // Turbulence : calculée depuis les voisins (si disponibles)
            float turbulence = ComputeTurbulence(cell, ctx);
            localSpeed = Mathf.Clamp01(localSpeed + turbulence * TerrainDisturbance);

            // Direction légèrement perturbée par la turbulence terrain
            Vector2 dir = baseDir;
            if (turbulence > 0.1f)
            {
                float angle = turbulence * 30f * (cell.Q % 2 == 0 ? 1f : -1f); // déviation en °
                dir = RotateVector2(baseDir, angle * Mathf.Deg2Rad);
            }

            cell.state.windVector = dir.normalized * localSpeed;
            cell.state.windSpeed  = localSpeed;
        }
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static float ComputeTurbulence(HexCell cell, GenerationContext ctx)
    {
        HexCell[] neighbors = ctx.GetNeighbors(cell);
        if (neighbors.Length == 0) return 0f;

        float maxAltDiff = 0f;
        foreach (HexCell nb in neighbors)
        {
            float diff = nb.state.altitude - cell.state.altitude;
            if (diff > maxAltDiff) maxAltDiff = diff;
        }
        return Mathf.Clamp01(maxAltDiff * 2f);
    }

    private static Vector2 RotateVector2(Vector2 v, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}
