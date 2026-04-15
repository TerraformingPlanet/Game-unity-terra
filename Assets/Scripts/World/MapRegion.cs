using UnityEngine;

/// <summary>
/// Localise une carte hexagonale sur la surface d'un corps céleste dans son système solaire.
/// Un MapRegion par zone jouable — relie SolarSystemData + CelestialBodyData + MapGenParameters
/// avec les coordonnées planétaires (latitude/longitude).
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Map Region", fileName = "NewMapRegion")]
public class MapRegion : ScriptableObject
{
    [Header("Références")]
    [Tooltip("Le système solaire contenant ce corps (fournit l'étoile + les orbites)")]
    public SolarSystemData solarSystem;

    [Tooltip("Le corps céleste sur lequel se trouve cette carte")]
    public CelestialBodyData planet;

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
}
