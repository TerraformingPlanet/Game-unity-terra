using UnityEngine;

/// <summary>
/// Localise une carte hexagonale sur la surface d'un corps céleste dans son système solaire.
/// Un MapRegion par zone jouable — relie SolarSystemData + CelestialBodyData + MapGenParameters
/// avec les coordonnées planétaires (latitude/longitude).
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Map Region", fileName = "NewMapRegion")]
public class MapRegion : ScriptableObject
{
    public struct CoherenceConstraint
    {
        public TerrainType dominantTerrainType;
        public float projectedWaterRatio;
        public float oceanicity;
        public float deserticity;
        public float frigidity;
        public bool isExtremeOcean;
        public bool isExtremeArid;
        public bool isExtremeFrozen;
        /// <summary>Signal de rugosité [0..1] — fort = région montagneuse/rocheuse, drainage actif.</summary>
        public float rugosity;
        /// <summary>Indice d'accumulation hydrique [0..1] — fort = eau froide abondante, bassins probables.</summary>
        public float accumulationIndex;
        /// <summary>Contraste de relief [0..1] — écart entre oceanicity et deserticity.</summary>
        public float reliefContrast;
    }

    [Header("Références")]
    [Tooltip("Le système solaire contenant ce corps (fournit l'étoile + les orbites)")]
    public SolarSystemData solarSystem;

    [Tooltip("Le corps céleste sur lequel se trouve cette carte")]
    public OrbitalBody planet;

    [Tooltip("Paramètres du bruit de Perlin pour cette région")]
    public MapGenParameters genParams;

    [Header("Position planétaire")]
    [Range(0f, 1f)]
    [Tooltip("Latitude [0–1] : 0 = pôle sud · 0.5 = équateur · 1 = pôle nord")]
    public float latitude = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Longitude [0–1] : position est-ouest sur la planète.\n" +
             "Crucial pour les corps en verrouillage tidal :\n" +
             "  0.5 = point subsolaire (face jour)\n" +
             "  0.0 / 1.0 = face nuit (froid extrême)")]
    public float longitude = 0.5f;

    [Header("Contexte de projection")]
    [Tooltip("Biome de la case cliquée sur la projection planétaire.")]
    public TerrainData projectedTerrain;

    [Range(0f, 1f)]
    [Tooltip("Taux d'eau de la case cliquée sur la projection planétaire.")]
    public float projectedWaterRatio;

    [Tooltip("Forcer une région locale entièrement marine quand la case source est un océan ouvert.")]
    public bool forceOpenWaterRegion;

    [Tooltip("Forcer une région locale aride indépendamment de la projection.")]
    public bool forceAridRegion;

    [Tooltip("Forcer une région locale gelée indépendamment de la projection.")]
    public bool forceFrozenRegion;

    // =============================================================
    // Propriétés calculées (depuis SolarSystemData + planet)
    // =============================================================

    /// <summary>
    /// Intensité solaire reçue à la distance orbitale de cette planète.
    /// Calculée depuis SolarSystemData — ne jamais saisir manuellement.
    /// </summary>
    public float SolarIntensity
        => solarSystem != null && planet != null
            ? solarSystem.SolarIntensityFor(planet)
            : 1f;

    /// <summary>
    /// Ce corps est-il en verrouillage tidal avec son étoile ?
    /// Calculé depuis l'orbite dans SolarSystemData.
    /// </summary>
    public bool IsTidallyLocked
        => solarSystem != null && planet != null && solarSystem.IsTidallyLockedBody(planet);

    /// <summary>
    /// Inclinaison axiale de la planète (°). Saisons actives si > 10°.
    /// </summary>
    public float AxialTilt
        => planet != null ? planet.physics.axialTilt : 0f;

    // =============================================================
    // Facteurs météo dérivés de la position (utilisés par PlanetaryWeatherState)
    // =============================================================

    /// <summary>
    /// Décalage de température dû à la latitude.
    /// 0° équateur → 0°C de décalage | pôles → jusqu'à -80°C.
    /// </summary>
    public float LatitudeTemperatureOffset
    {
        get
        {
            float latFactor = Mathf.Abs(latitude - 0.5f) * 2f; // 0 = équateur, 1 = pôle
            return -latFactor * 80f;
        }
    }

    /// <summary>
    /// Décalage de température supplémentaire si tidallyLocked,
    /// basé sur la longitude (face jour = chaud, face nuit = froid extrême).
    /// </summary>
    public float TidalLockTemperatureOffset
    {
        get
        {
            if (!IsTidallyLocked) return 0f;
            float lonFactor = Mathf.Abs(longitude - 0.5f) * 2f; // 0 = subsolaire, 1 = nuit
            return Mathf.Lerp(+50f, -120f, lonFactor);
        }
    }

    /// <summary>
    /// Décalage de température total pour cette région (latitude + tidal lock).
    /// </summary>
    public float TotalTemperatureOffset
        => LatitudeTemperatureOffset + TidalLockTemperatureOffset;

    public CoherenceConstraint ComputeCoherence(PlanetaryWeatherState weather)
    {
        TerrainType dominantType = projectedTerrain != null ? projectedTerrain.terrainType : TerrainType.Roche;
        float waterRatio = Mathf.Clamp01(projectedWaterRatio);

        if (forceOpenWaterRegion)
        {
            dominantType = TerrainType.Eau;
            waterRatio = Mathf.Max(waterRatio, 1f);
        }
        else if (forceFrozenRegion)
        {
            dominantType = TerrainType.Glace;
            waterRatio = Mathf.Max(waterRatio, 0.75f);
        }
        else if (forceAridRegion)
        {
            dominantType = TerrainType.Roche;
            waterRatio = Mathf.Min(waterRatio, 0.02f);
        }

        float regionalTemperature = (planet != null ? planet.physics.baseEquatorTemperature : 0f)
                                + (weather != null ? weather.temperatureOffset : TotalTemperatureOffset);

        float desertFromWater = 1f - waterRatio;
        float heatFactor = Mathf.InverseLerp(10f, 70f, regionalTemperature);
        float freezeFactor = Mathf.InverseLerp(5f, -80f, regionalTemperature);

        float oceanicity = dominantType == TerrainType.Eau
            ? Mathf.Max(waterRatio, 0.65f)
            : waterRatio * 0.6f;

        float deserticity = dominantType == TerrainType.Roche || dominantType == TerrainType.Metal
            ? Mathf.Max(desertFromWater, 0.35f + heatFactor * 0.35f)
            : desertFromWater * (0.65f + heatFactor * 0.35f);

        float frigidity = dominantType == TerrainType.Glace
            ? Mathf.Max(freezeFactor, 0.75f)
            : freezeFactor * (waterRatio > 0.2f ? 0.9f : 0.5f);

        // Signaux de relief progressifs (Sprint B)
        float rugosity = Mathf.Clamp01(desertFromWater * (1f - frigidity) * (deserticity * 0.8f + 0.2f));
        float accumulationIndex = Mathf.Clamp01(waterRatio * (1f - heatFactor));
        float reliefContrast = Mathf.Clamp01(Mathf.Abs(oceanicity - deserticity));

        return new CoherenceConstraint
        {
            dominantTerrainType = dominantType,
            projectedWaterRatio = waterRatio,
            oceanicity = Mathf.Clamp01(oceanicity),
            deserticity = Mathf.Clamp01(deserticity),
            frigidity = Mathf.Clamp01(frigidity),
            isExtremeOcean = forceOpenWaterRegion || (dominantType == TerrainType.Eau && waterRatio >= 0.95f),
            isExtremeArid = forceAridRegion || (projectedTerrain != null && dominantType != TerrainType.Glace && waterRatio <= 0.05f),
            isExtremeFrozen = forceFrozenRegion || (dominantType == TerrainType.Glace && regionalTemperature <= -15f),
            rugosity = rugosity,
            accumulationIndex = accumulationIndex,
            reliefContrast = reliefContrast,
        };
    }
}
