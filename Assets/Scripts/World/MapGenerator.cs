using UnityEngine;

/// <summary>
/// Génère procéduralement les biomes et l'état physique de la grille hexagonale
/// à partir d'un MapRegion (qui reférence SolarSystemData + CelestialBodyData + MapGenParameters).
///
/// Pipeline :
///   1. PlanetaryWeatherState.Compute()  → météo régionale (vent, temp offset, précipitations)
///   2. Par hex : bruit fractal → altitude, tempLocale, waterRatio, toxines, sol, vent local
///   3. Arbre de décision → biome + couche (WorldLayer)
///
/// Rétro-compatibilité : l'ancienne surcharge Populate(cells, CelestialBodyData) est conservée
/// mais délègue vers un MapRegion minimal créé à la volée.
/// </summary>
public static class MapGenerator
{
    // =========================================================
    // API principale
    // =========================================================

    /// <summary>
    /// Remplit le tableau de cellules depuis un MapRegion complet.
    /// C'est la méthode à appeler dans le code nouveau.
    /// </summary>
    public static void Populate(HexCell[] cells, MapRegion region)
    {
        if (region == null)
        {
            Debug.LogWarning("[MapGenerator] MapRegion manquant.");
            return;
        }
        if (region.planet == null)
        {
            Debug.LogWarning("[MapGenerator] MapRegion.planet manquant.");
            return;
        }

        CelestialBodyData body = region.planet;

        if (!ValidateBody(body)) return;

        MapGenParameters p    = region.genParams != null ? region.genParams : body.genParams;
        int              seed = p.randomSeedOnPlay ? Random.Range(0, 100000) : p.seed;
        System.Random    rng  = new System.Random(seed);

        // Décorréler les trois passes de bruit
        Vector2 hOff = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));
        Vector2 bOff = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));
        Vector2 gOff = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));

        // --- Météo régionale (calculée une fois pour toute la carte) ---
        PlanetaryWeatherState weather = PlanetaryWeatherState.Compute(body, region);

        Debug.Log($"[MapGenerator] Région '{region.name}' | Corps '{body.bodyName}' | seed={seed}" +
                  $" | TempOffset={weather.temperatureOffset:F1}°C" +
                  $" | Précip={weather.precipitationRate:F2}" +
                  $" | Vent={weather.prevailingWindDir} ×{weather.prevailingWindSpeed:F2}");

        foreach (HexCell cell in cells)
        {
            float hx = cell.center.x / p.heightScale + hOff.x;
            float hz = cell.center.z / p.heightScale + hOff.y;
            float bx = cell.center.x / p.biomeScale  + bOff.x;
            float bz = cell.center.z / p.biomeScale  + bOff.y;
            float gx = cell.center.x / p.heightScale + gOff.x;
            float gz = cell.center.z / p.heightScale + gOff.y;

            float altitude     = FractalNoise(hx, hz, p.octaves, p.persistence, p.lacunarity);
            float biomeNoise   = FractalNoise(bx, bz, p.octaves, p.persistence, p.lacunarity);
            float geoNoise     = FractalNoise(gx, gz, p.octaves, p.persistence, p.lacunarity);

            // --- Température locale ---
            float tempLocale = ComputeTemperature(body, weather, altitude);

            // --- Ratio d'eau ---
            float waterRatio = ComputeWaterRatio(body, weather, biomeNoise, tempLocale);

            // --- Toxines ---
            float toxinLevel = ComputeToxinLevel(body, geoNoise);

            // --- Vent local ---
            Vector2 windVec   = weather.WindVectorForHex(altitude);
            float   windSpeed = windVec.magnitude;

            // --- Sol ---
            SoilProfile soil = ComputeSoil(body, altitude, geoNoise, biomeNoise, tempLocale, toxinLevel);

            // --- Stocker l'état physique ---
            cell.state = new HexPhysicalState
            {
                altitude   = altitude,
                tempLocale = tempLocale,
                waterRatio = waterRatio,
                toxinLevel = toxinLevel,
                windVector = windVec,
                windSpeed  = windSpeed,
                rainShadow = false, // calculé en passe post (propagation ombre pluviométrique)
                soil       = soil
            };

            cell.world = body;

            // --- Couche + biome (arbre de décision) ---
            LayerZone zone = body.GetLayerForHeight(altitude);
            cell.layer   = zone.layer;
            cell.terrain = PickBiome(zone, biomeNoise, cell.state, body);
        }
    }

    /// <summary>
    /// Surcharge de rétro-compatibilité : accepte un CelestialBodyData directement
    /// (génère une région équatoriale par défaut, sans système solaire).
    /// Utilisée par HexGrid jusqu'à migration complète vers MapRegion.
    /// </summary>
    public static void Populate(HexCell[] cells, CelestialBodyData body)
    {
        if (!ValidateBody(body)) return;

        // Crée un MapRegion minimal à la volée (équateur, pas de système solaire)
        MapRegion tempRegion = ScriptableObject.CreateInstance<MapRegion>();
        tempRegion.planet    = body;
        tempRegion.genParams = body.genParams;
        tempRegion.latitude  = 0.5f;
        tempRegion.longitude = 0.5f;

        Populate(cells, tempRegion);

        Object.DestroyImmediate(tempRegion);
    }

    // =========================================================
    // Calcul des propriétés physiques par hex
    // =========================================================

    private static float ComputeTemperature(CelestialBodyData body,
                                            PlanetaryWeatherState weather,
                                            float altitude)
    {
        float temp = body.physics.baseEquatorTemperature;
        temp += weather.temperatureOffset;                  // latitude + tidal + serre
        temp -= altitude * 60f;                             // -60°C max au sommet
        return temp;
    }

    private static float ComputeWaterRatio(CelestialBodyData body,
                                           PlanetaryWeatherState weather,
                                           float biomeNoise,
                                           float tempLocale)
    {
        float w = body.geology.waterAbundance * biomeNoise * body.atmosphere.density;

        // Gel : eau présente mais sous forme de glace en-dessous de -20°C
        if (tempLocale < -20f) w *= 0.5f;
        // Évaporation intense au-dessus de 80°C
        if (tempLocale > 80f)  w *= Mathf.Lerp(1f, 0f, (tempLocale - 80f) / 120f);

        return Mathf.Clamp01(w);
    }

    private static float ComputeToxinLevel(CelestialBodyData body, float geoNoise)
    {
        return Mathf.Clamp01(body.atmosphere.toxinRatio * (0.5f + geoNoise * 0.5f));
    }

    private static SoilProfile ComputeSoil(CelestialBodyData body,
                                           float altitude, float geoNoise, float biomeNoise,
                                           float tempLocale, float toxinLevel)
    {
        float hardness     = Mathf.Clamp01(altitude * 0.5f + geoNoise * 0.5f);
        float porosity     = Mathf.Clamp01(biomeNoise * (1f - body.geology.geologicalActivity * 0.4f));
        float mineralDens  = Mathf.Clamp01(body.geology.mineralRichness * (geoNoise + 0.2f));
        float thermalCond  = Mathf.Clamp01(body.geology.geologicalActivity * 0.7f + altitude * 0.3f);
        bool  toxic        = toxinLevel > 0.4f && body.atmosphere.toxinRatio > 0.3f;

        return new SoilProfile
        {
            rockHardness        = hardness,
            organicContent      = 0f,          // commence à 0, augmente avec la végétation
            porosity            = porosity,
            mineralDensity      = mineralDens,
            toxicSoil           = toxic,
            thermalConductivity = thermalCond
        };
    }

    // =========================================================
    // Sélection du biome (arbre de décision)
    // =========================================================

    private static TerrainData PickBiome(LayerZone zone, float biomeNoise,
                                         HexPhysicalState state, CelestialBodyData body)
    {
        if (zone == null || zone.biomes == null || zone.biomes.Length == 0)
            return null;

        // TODO Phase 3 : remplacer ici par l'arbre de décision complet (§4 du doc règles)
        // Pour l'instant : sélection par index dans le pool de la zone
        int idx = Mathf.FloorToInt(biomeNoise * zone.biomes.Length) % zone.biomes.Length;
        return zone.biomes[idx];
    }

    // =========================================================
    // Validation
    // =========================================================

    private static bool ValidateBody(CelestialBodyData body)
    {
        if (body == null)
        { Debug.LogWarning("[MapGenerator] CelestialBodyData manquant."); return false; }
        if (body.genParams == null)
        { Debug.LogWarning($"[MapGenerator] {body.bodyName} : genParams manquant."); return false; }
        if (body.layers == null || body.layers.Length == 0)
        { Debug.LogWarning($"[MapGenerator] {body.bodyName} : aucune LayerZone définie."); return false; }
        return true;
    }

    // =========================================================
    // Bruit de Perlin fractal (fBm) — retourne [0, 1]
    // =========================================================
    private static float FractalNoise(float x, float z, int octaves, float persistence, float lacunarity)
    {
        float total     = 0f;
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

        return total / maxValue;
    }
}

