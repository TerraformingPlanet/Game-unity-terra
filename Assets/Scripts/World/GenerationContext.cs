using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Contexte partagé injecté dans chaque IHexSystem.Execute().
/// Créé une seule fois par MapGenerator.Populate() avant l'entrée dans le pipeline.
///
/// Contient :
/// - Références aux ScriptableObjects (SolarSystem, Body, Region, Params)
/// - Météo régionale pré-calculée (PlanetaryWeatherState)
/// - Générateur aléatoire + seed reproductible
/// - Offsets de bruit décorrélés par passe (height / biome / geo)
/// - Lookup spatial (q,r) → HexCell pour accès aux voisins
/// </summary>
public class GenerationContext
{
    // =========================================================
    // Données de référence
    // =========================================================

    public SolarSystemData       solarSystem;
    public CelestialBodyData     body;
    public MapRegion             region;
    public PlanetaryWeatherState weather;
    public MapGenParameters      genParams;

    // =========================================================
    // Reproductibilité
    // =========================================================

    public int           seed;
    public System.Random rng;

    // =========================================================
    // Offsets de bruit fractal (décorrélés → 3 passes indépendantes)
    // =========================================================

    /// <summary>Offset pour la passe de hauteur (altitude).</summary>
    public Vector2 heightOffset;

    /// <summary>Offset pour la passe de biome (répartition des types de terrain).</summary>
    public Vector2 biomeOffset;

    /// <summary>Offset pour la passe géologique (sol, minéraux, toxines).</summary>
    public Vector2 geoOffset;

    // =========================================================
    // Lookup spatial pour accès aux voisins
    // =========================================================

    /// <summary>
    /// Dictionnaire (q, r) → HexCell.
    /// Construit lors du Build() — permet à tout système de trouver
    /// les voisins d'une cellule sans stocker de tableau dans HexCell.
    /// </summary>
    public Dictionary<(int q, int r), HexCell> cellLookup;

    // Directions axiales des 6 voisins d'un hex (coordonnées cube/axiales flat-top)
    private static readonly (int dq, int dr)[] NeighborDirs =
    {
        ( 1,  0), ( 1, -1), ( 0, -1),
        (-1,  0), (-1,  1), ( 0,  1)
    };

    // =========================================================
    // Factory
    // =========================================================

    /// <summary>
    /// Construit un GenerationContext complet à partir d'un MapRegion.
    /// Calcule la météo, détermine le seed, décorrèle les offsets de bruit, construit le lookup.
    /// </summary>
    public static GenerationContext Build(HexCell[] cells, MapRegion region)
    {
        CelestialBodyData body     = region.planet;
        MapGenParameters  p        = region.genParams != null ? region.genParams : body.genParams;
        int               seed     = p.randomSeedOnPlay ? Random.Range(0, 100000) : p.seed;
        System.Random     rng      = new System.Random(seed);
        PlanetaryWeatherState wx   = PlanetaryWeatherState.Compute(body, region);

        var ctx = new GenerationContext
        {
            solarSystem  = region.solarSystem,
            body         = body,
            region       = region,
            weather      = wx,
            genParams    = p,
            seed         = seed,
            rng          = rng,
            heightOffset = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000)),
            biomeOffset  = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000)),
            geoOffset    = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000)),
        };

        ctx.cellLookup = BuildLookup(cells);

        Debug.Log($"[GenerationContext] seed={seed} | corps='{body.bodyName}'" +
                  $" | Toffset={wx.temperatureOffset:F1}°C | précip={wx.precipitationRate:F2}");

        return ctx;
    }

    // =========================================================
    // Helpers voisins
    // =========================================================

    /// <summary>Retourne les voisins existants d'une cellule (0 à 6 résultats).</summary>
    public HexCell[] GetNeighbors(HexCell cell)
    {
        var result = new System.Collections.Generic.List<HexCell>(6);
        foreach (var (dq, dr) in NeighborDirs)
        {
            if (cellLookup.TryGetValue((cell.Q + dq, cell.R + dr), out HexCell neighbor))
                result.Add(neighbor);
        }
        return result.ToArray();
    }

    // =========================================================
    // Bruit de Perlin fractal (fBm) — retourne [0, 1]
    // Déplacé ici depuis MapGenerator pour être accessible à tous les systèmes.
    // =========================================================

    public static float FractalNoise(float x, float z, int octaves,
                                     float persistence, float lacunarity)
    {
        float total     = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue  = 0f;

        for (int i = 0; i < octaves; i++)
        {
            total     += UnityEngine.Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
            maxValue  += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return total / maxValue;
    }

    // =========================================================
    // Internals
    // =========================================================

    private static Dictionary<(int, int), HexCell> BuildLookup(HexCell[] cells)
    {
        var dict = new Dictionary<(int, int), HexCell>(cells.Length);
        foreach (var cell in cells)
            dict[(cell.Q, cell.R)] = cell;
        return dict;
    }
}
