using UnityEngine;

/// <summary>
/// Génère procéduralement les biomes et les couches d'une grille hexagonale
/// à partir d'un CelestialBodyData et de ses MapGenParameters.
///
/// Deux bruits fractals indépendants :
///   - heightNoise → détermine la WorldLayer (underground, surface, atmosphere…)
///   - biomeNoise  → choisit le biome dans le pool de cette couche
/// </summary>
public static class MapGenerator
{
    /// <summary>
    /// Remplit le tableau de cellules avec terrain + layer en fonction du corps céleste.
    /// </summary>
    public static void Populate(HexCell[] cells, CelestialBodyData body)
    {
        if (body == null)
        {
            Debug.LogWarning("[MapGenerator] Aucun CelestialBodyData assigné.");
            return;
        }
        if (body.genParams == null)
        {
            Debug.LogWarning($"[MapGenerator] {body.bodyName} : genParams manquant.");
            return;
        }
        if (body.layers == null || body.layers.Length == 0)
        {
            Debug.LogWarning($"[MapGenerator] {body.bodyName} : aucune LayerZone définie.");
            return;
        }

        MapGenParameters p = body.genParams;
        int seed = p.randomSeedOnPlay ? Random.Range(0, 100000) : p.seed;
        System.Random rng = new System.Random(seed);

        // Offsets aléatoires pour décorréler les deux bruits
        Vector2 hOff = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));
        Vector2 bOff = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));

        Debug.Log($"[MapGenerator] Génération '{body.bodyName}' — seed={seed}, couches={body.layers.Length}");

        foreach (HexCell cell in cells)
        {
            // --- Bruit de hauteur ---
            float hx = cell.center.x / p.heightScale + hOff.x;
            float hz = cell.center.z / p.heightScale + hOff.y;
            float heightVal = FractalNoise(hx, hz, p.octaves, p.persistence, p.lacunarity);

            // --- Bruit de biome ---
            float bx = cell.center.x / p.biomeScale + bOff.x;
            float bz = cell.center.z / p.biomeScale + bOff.y;
            float biomeVal = FractalNoise(bx, bz, p.octaves, p.persistence, p.lacunarity);

            // --- Assignation couche + biome ---
            LayerZone zone = body.GetLayerForHeight(heightVal);
            cell.layer = zone.layer;
            cell.world = body;

            if (zone.biomes != null && zone.biomes.Length > 0)
            {
                int idx = Mathf.FloorToInt(biomeVal * zone.biomes.Length) % zone.biomes.Length;
                cell.terrain = zone.biomes[idx];
            }
            else
            {
                cell.terrain = null;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Bruit de Perlin fractal (fBm) — retourne une valeur normalisée [0, 1]
    // -----------------------------------------------------------------------
    private static float FractalNoise(float x, float z, int octaves, float persistence, float lacunarity)
    {
        float total    = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue  = 0f;

        for (int i = 0; i < octaves; i++)
        {
            total     += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
            maxValue  += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return total / maxValue; // normalisé [0, 1]
    }
}
