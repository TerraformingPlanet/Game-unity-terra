using UnityEngine;

/// <summary>
/// Grille hexagonale basse résolution en projection Mercator couvrant toute la surface
/// d'une planète. Sa résolution dépend du rayon du corps céleste, avec un plafond
/// défini par un rayon de référence maximal.
/// Le pipeline IHexSystem tourne sur cette grille exactement comme pour une région locale,
/// mais coordonnées (q, r) = (colonne_lon, ligne_lat).
///
/// Utilisé par PlanetSphere via PlanetTextureGenerator pour générer la texture équirectangulaire.
/// </summary>
public static class PlanetaryHexGrid
{
    public const int MinCols = 24;
    public const int MinRows = 12;
    public const int MaxCols = 96;
    public const int MaxRows = 48;
    public const float MaxReferenceRadiusKm = 69911f;

    public readonly struct GridData
    {
        public GridData(HexCell[] cells, int cols, int rows)
        {
            Cells = cells;
            Cols = cols;
            Rows = rows;
        }

        public HexCell[] Cells { get; }
        public int Cols { get; }
        public int Rows { get; }
    }

    private const float OceanProjectionThreshold = 0.46f;
    private const float AridProjectionThreshold = 0.68f;
    private const float FrozenProjectionThreshold = 0.62f;

    /// <summary>
    /// Génère et peuple la grille planétaire pour un corps céleste donné.
    /// Retourne une grille Mercator complète avec dimensions calculées depuis le rayon.
    /// </summary>
    public static GridData Generate(CelestialBodyData body,
                                    DebugCoherenceOverride coherenceOverride = DebugCoherenceOverride.None,
                                    float waterLevelOffset = 0f)
    {
        if (body == null)
        {
            Debug.LogError("[PlanetaryHexGrid] CelestialBodyData manquant.");
            return default;
        }

        GetDimensions(body, out int cols, out int rows);
        HexCell[] cells = CreateCells(cols, rows);
        MapGenerator.Populate(cells, body);
        ApplyProjectionWaterLevel(cells, body, waterLevelOffset);
        ApplyProjectionOverride(cells, cols, rows, body, coherenceOverride);
        return new GridData(cells, cols, rows);
    }

    // =========================================================
    // Interne
    // =========================================================

    private static HexCell[] CreateCells(int cols, int rows)
    {
        HexCell[] cells = new HexCell[cols * rows];

        for (int col = 0; col < cols; col++)
        {
            for (int row = 0; row < rows; row++)
            {
                // On réutilise les coordonnées axiales (q=col, r=row) comme identifiants.
                // Le centre world n'est utilisé que pour les calculs de hauteur par distance ;
                // on le place sur une grille régulière normalisée [0-1].
                var cell = new HexCell(col, row) { gridIndex = col * rows + row };

                // Override du centre pour placer les cellules dans un espace [0,1]²
                // (PlanetTextureGenerator utilise UV normalisés, pas les coords world HexMetrics)
                float u = (col + 0.5f) / cols;
                float v = (row + 0.5f) / rows;
                cell.center = new UnityEngine.Vector3(u, 0f, v);

                cells[col * rows + row] = cell;
            }
        }

        return cells;
    }

    public static void GetDimensions(CelestialBodyData body, out int cols, out int rows)
    {
        if (body == null || body.radius <= 0f)
        {
            cols = MinCols;
            rows = MinRows;
            return;
        }

        float normalizedRadius = Mathf.Clamp01(body.radius / MaxReferenceRadiusKm);
        cols = Mathf.RoundToInt(Mathf.Lerp(MinCols, MaxCols, normalizedRadius));
        rows = Mathf.RoundToInt(Mathf.Lerp(MinRows, MaxRows, normalizedRadius));

        cols = Mathf.Clamp(cols, MinCols, MaxCols);
        rows = Mathf.Clamp(rows, MinRows, MaxRows);

        if ((cols & 1) != 0)
            cols += cols < MaxCols ? 1 : -1;

        rows = Mathf.Max(2, rows);
    }

    // =========================================================
    // Utilitaires publics
    // =========================================================

    /// <summary>
    /// Retourne la cellule correspondant à des coordonnées lat/lon normalisées [0–1].
    /// </summary>
    public static HexCell GetCellAt(HexCell[] cells, int cols, int rows, float latNorm, float lonNorm)
    {
        int col = Mathf.Clamp(Mathf.FloorToInt(lonNorm * cols), 0, cols - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt(latNorm * rows), 0, rows - 1);
        return cells[col * rows + row];
    }

    private static void ApplyProjectionOverride(HexCell[] cells, int cols, int rows, CelestialBodyData body, DebugCoherenceOverride coherenceOverride)
    {
        if (coherenceOverride == DebugCoherenceOverride.None || cells == null || body == null)
            return;

        TerrainData waterTerrain = FindTerrain(body, TerrainType.Eau);
        TerrainData rockTerrain = FindTerrain(body, TerrainType.Roche);
        TerrainData iceTerrain = FindTerrain(body, TerrainType.Glace);
        TerrainData metalTerrain = FindTerrain(body, TerrainType.Metal);

        foreach (HexCell cell in cells)
        {
            float u = cols > 0 ? (cell.Q + 0.5f) / cols : 0.5f;
            float v = rows > 0 ? (cell.R + 0.5f) / rows : 0.5f;
            float equatorBias = 1f - Mathf.Abs(v - 0.5f) * 2f;
            float pseudoNoise = Mathf.PerlinNoise(u * 4.1f + 13.7f, v * 3.3f + 2.9f);
            float oceanMask = pseudoNoise * 0.55f + equatorBias * 0.3f + (1f - cell.state.altitude) * 0.15f;

            HexPhysicalState state = cell.state;
            switch (coherenceOverride)
            {
                case DebugCoherenceOverride.Ocean:
                    if (oceanMask >= OceanProjectionThreshold)
                    {
                        state.waterRatio = Mathf.Max(state.waterRatio, 0.96f);
                        state.waterClassification = WaterClassification.OpenOcean;
                        state.terrainClass = TerrainClass.Basin;
                        state.tempLocale = Mathf.Max(state.tempLocale, -4f);
                        cell.terrain = waterTerrain ?? cell.terrain;
                    }
                    else
                    {
                        state.waterRatio = Mathf.Min(state.waterRatio, 0.28f);
                        if (state.waterClassification == WaterClassification.OpenOcean)
                            state.waterClassification = WaterClassification.Dry;
                        cell.terrain = rockTerrain ?? cell.terrain;
                    }
                    break;

                case DebugCoherenceOverride.Arid:
                    state.waterRatio = Mathf.Min(state.waterRatio, Mathf.Lerp(0.01f, 0.08f, 1f - oceanMask));
                    state.waterClassification = WaterClassification.Dry;

                    if ((state.tempLocale <= -18f && v <= 0.08f) || v >= 0.92f)
                    {
                        state.tempLocale = Mathf.Min(state.tempLocale, -18f);
                        cell.terrain = iceTerrain ?? rockTerrain ?? cell.terrain;
                    }
                    else if (oceanMask >= AridProjectionThreshold)
                    {
                        cell.terrain = metalTerrain ?? rockTerrain ?? cell.terrain;
                    }
                    else
                    {
                        cell.terrain = rockTerrain ?? metalTerrain ?? cell.terrain;
                    }
                    break;

                case DebugCoherenceOverride.Frozen:
                    if (oceanMask >= FrozenProjectionThreshold)
                    {
                        state.waterRatio = Mathf.Max(state.waterRatio, 0.7f);
                        state.tempLocale = Mathf.Min(state.tempLocale, -20f);
                        state.waterClassification = WaterClassification.FrozenWater;
                        state.terrainClass = TerrainClass.Basin;
                        cell.terrain = iceTerrain ?? cell.terrain;
                    }
                    break;
            }

            cell.state = state;
        }
    }

    private static void ApplyProjectionWaterLevel(HexCell[] cells, CelestialBodyData body, float waterLevelOffset)
    {
        if (cells == null || body == null || Mathf.Abs(waterLevelOffset) < 0.0001f)
            return;

        float clampedOffset = Mathf.Clamp(waterLevelOffset, -0.45f, 0.45f);
        TerrainData waterTerrain = FindTerrain(body, TerrainType.Eau);
        TerrainData rockTerrain = FindTerrain(body, TerrainType.Roche);
        TerrainData iceTerrain = FindTerrain(body, TerrainType.Glace);

        foreach (HexCell cell in cells)
        {
            HexPhysicalState state = cell.state;
            float floodScore = (1f - state.altitude) * 0.72f + state.waterRatio * 0.28f;

            if (clampedOffset > 0f)
            {
                float threshold = Mathf.Lerp(0.84f, 0.22f, Mathf.InverseLerp(0f, 0.45f, clampedOffset));
                if (floodScore >= threshold)
                {
                    float targetWater = Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(threshold, 1f, floodScore));
                    state.waterRatio = Mathf.Max(state.waterRatio, targetWater);
                    state.terrainClass = TerrainClass.Basin;

                    if (state.tempLocale <= -8f)
                    {
                        state.waterClassification = WaterClassification.FrozenWater;
                        cell.terrain = iceTerrain ?? waterTerrain ?? cell.terrain;
                    }
                    else if (targetWater >= 0.9f)
                    {
                        state.waterClassification = WaterClassification.OpenOcean;
                        cell.terrain = waterTerrain ?? cell.terrain;
                    }
                    else
                    {
                        state.waterClassification = WaterClassification.InlandWater;
                        cell.terrain = waterTerrain ?? cell.terrain;
                    }
                }
            }
            else
            {
                float dryness = Mathf.InverseLerp(0f, -0.45f, clampedOffset);
                state.waterRatio = Mathf.Clamp01(state.waterRatio - dryness * 0.55f);

                if (state.waterRatio <= 0.08f)
                {
                    state.waterClassification = WaterClassification.Dry;
                    if (cell.terrain != null && (cell.terrain.terrainType == TerrainType.Eau || cell.terrain.terrainType == TerrainType.Glace))
                        cell.terrain = rockTerrain ?? cell.terrain;
                }
            }

            cell.state = state;
        }
    }

    private static TerrainData FindTerrain(CelestialBodyData body, TerrainType target)
    {
        if (body.layers == null)
            return null;

        foreach (LayerZone layer in body.layers)
        {
            if (layer == null || layer.biomes == null)
                continue;

            foreach (TerrainData terrain in layer.biomes)
            {
                if (terrain != null && terrain.terrainType == target)
                    return terrain;
            }
        }

        return null;
    }
}
