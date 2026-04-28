using UnityEngine;

/// <summary>
/// État météorologique régional calculé une seule fois au début de la génération.
/// Dérivé de CelestialBodyData + MapRegion — N'EST PAS un ScriptableObject,
/// c'est un objet runtime créé par MapGenerator.
///
/// Fournit les modificateurs météo qui s'appliquent uniformément à toute la carte,
/// avant les variations locales par hex.
/// </summary>
public class PlanetaryWeatherState
{
    // --- Résultats exposés au MapGenerator ---

    /// <summary>Direction du vent dominant (normalisée sur le plan XZ). </summary>
    public Vector2 prevailingWindDir;

    /// <summary>Force du vent régional de base [0–1]. Amplifiée localement par l'altitude de chaque hex.</summary>
    public float prevailingWindSpeed;

    /// <summary>
    /// Taux de précipitations potentielles [0–1].
    /// Modulé localement par le waterRatio et l'ombre pluviométrique de chaque hex.
    /// </summary>
    public float precipitationRate;

    /// <summary>
    /// Décalage de température régional total : latitude + tidallyLocked + effet de serre.
    /// S'ajoute à CelestialBodyData.physics.baseEquatorTemperature par hex.
    /// </summary>
    public float temperatureOffset;

    /// <summary>
    /// Modificateur saisonnier [-1, +1].
    /// Actif uniquement si axialTilt > 10°.
    /// Multiplié par une amplitude en °C au moment du calcul tempLocale par hex.
    /// </summary>
    public float seasonalModifier;

    // =========================================================
    // Calcul principal
    // =========================================================

    /// <summary>
    /// Calcule la météo régionale depuis la planète et la position de la carte.
    /// Appelé une seule fois dans MapGenerator.Populate().
    /// </summary>
    public static PlanetaryWeatherState Compute(OrbitalBody body, MapRegion region)
    {
        var weather = new PlanetaryWeatherState();

        // --- Température régionale ---
        weather.temperatureOffset = region.TotalTemperatureOffset
                                  + body.GreenhouseTemperatureOffset;

        // --- Précipitations = eau × densité atmo ---
        weather.precipitationRate = body.geology.waterAbundance * body.atmosphere.density;

        // --- Saisons ---
        weather.seasonalModifier = body.physics.axialTilt > 10f
            ? Mathf.Sin(region.latitude * Mathf.PI) // amplitude maximale aux latitudes moyennes
            : 0f;

        // --- Vent dominant ---
        ComputeWind(body, region, out weather.prevailingWindDir, out weather.prevailingWindSpeed);

        return weather;
    }

    // =========================================================
    // Vent dominant
    // =========================================================

    private static void ComputeWind(OrbitalBody body, MapRegion region,
                                    out Vector2 windDir, out float windSpeed)
    {
        float lat = region.latitude;   // 0 = pôle sud, 0.5 = équateur, 1 = pôle nord
        float lon = region.longitude;  // 0–1 est-ouest
        bool locked = region.IsTidallyLocked;
        float rotSpeed = body.physics.rotationSpeed;

        if (locked)
        {
            // Corps en verrouillage tidal :
            // Le vent souffle du point subsolaire (lon=0.5) vers la face nuit.
            float lonDir = (lon < 0.5f) ? -1f : 1f; // vers l'est ou l'ouest selon le côté
            windDir   = new Vector2(lonDir, 0f);
            windSpeed = Mathf.Lerp(0.3f, 1.0f, Mathf.Abs(lon - 0.5f) * 2f); // plus fort en zone crépusculaire
        }
        else
        {
            // Planète en rotation normale — cellules de Hadley simplifiées
            windDir   = ComputeNormalWindDir(lat, rotSpeed);
            windSpeed = ComputeNormalWindSpeed(lat);
        }

        // Atmosphère ténue → vent très faible
        windSpeed *= body.atmosphere.density;
        windSpeed = Mathf.Clamp01(windSpeed);
    }

    /// <summary>
    /// Direction du vent dominant en fonction de la latitude pour une planète en rotation normale.
    /// Basé sur les cellules de Hadley / Ferrel / cellules polaires.
    /// </summary>
    private static Vector2 ComputeNormalWindDir(float latitude, float rotationSpeed)
    {
        // Force de Coriolis (déviation) : forte rotation → vents est-ouest prononcés
        float coriolis = rotationSpeed;

        if (latitude < 0.2f || latitude > 0.8f)
            // Cellules polaires : vents d'est (→ -X dans l'espace carte)
            return new Vector2(-1f, Mathf.Lerp(0f, 0.3f * coriolis, Mathf.Abs(latitude - 0.5f))).normalized;

        if (latitude < 0.35f || (latitude > 0.65f && latitude < 0.8f))
            // Zone subpolaire / tempérée : vents d'ouest dominants
            return new Vector2(1f, Mathf.Lerp(0f, 0.2f * coriolis, 1f - Mathf.Abs(latitude - 0.5f))).normalized;

        if (latitude >= 0.35f && latitude <= 0.45f || latitude >= 0.55f && latitude <= 0.65f)
            // Zone subtropicale : alizés → direction vers l'équateur + dérive est
            return new Vector2(-0.7f + coriolis * 0.3f, (latitude < 0.5f ? 1f : -1f) * 0.7f).normalized;

        // Équateur (0.45–0.55) : convergence ITCZ, convection verticale, vent faible orienté N/S
        return new Vector2(0f, latitude < 0.5f ? 1f : -1f).normalized;
    }

    /// <summary>
    /// Vitesse de base du vent selon la latitude (0–1).
    /// Voir tableau §8.5 de MapGeneration_rule.md.
    /// </summary>
    private static float ComputeNormalWindSpeed(float latitude)
    {
        float absLat = Mathf.Abs(latitude - 0.5f) * 2f; // 0 = équateur, 1 = pôle

        if (absLat < 0.1f)  return Random.Range(0.15f, 0.25f); // équateur : alizés doux ou calme
        if (absLat < 0.3f)  return Random.Range(0.45f, 0.65f); // subtropical
        if (absLat < 0.6f)  return Random.Range(0.55f, 0.75f); // tempéré : vents d'ouest forts
        if (absLat < 0.8f)  return Random.Range(0.70f, 0.90f); // subpolaire : dépressionnaire
        return Random.Range(0.25f, 0.50f);                      // polaire : calme relatif
    }

    // =========================================================
    // Application locale par hex
    // =========================================================

    /// <summary>
    /// Calcule le vecteur de vent pour un hex donné en amplifiant selon son altitude.
    /// </summary>
    public Vector2 WindVectorForHex(float altitude)
    {
        float localSpeed = prevailingWindSpeed * (1f + altitude * 0.8f);
        return prevailingWindDir * localSpeed;
    }

    /// <summary>
    /// Retourne les précipitations locales pour un hex.
    /// Réduit de moitié pour les hexes en ombre pluviométrique (barlovento).
    /// </summary>
    public float PrecipitationForHex(float hexWaterRatio, bool rainShadow)
    {
        float local = precipitationRate * hexWaterRatio;
        return rainShadow ? local * 0.5f : local;
    }
}
