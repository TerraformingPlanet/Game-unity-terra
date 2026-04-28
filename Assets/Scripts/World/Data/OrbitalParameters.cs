using UnityEngine;
using System;

/// <summary>
/// Paramètres orbitaux d'un corps autour de son étoile (ou d'une lune autour d'une planète).
/// Toutes les propriétés physiques dérivées (intensité solaire, verrouillage tidal,
/// distance actuelle) sont calculées depuis ces paramètres — jamais saisies.
/// </summary>
[Serializable]
public struct OrbitalParameters
{
    [Tooltip("Demi-grand axe en Unités Astronomiques (1.0 AU = distance Terre-Soleil).")]
    [Min(0.001f)]
    public float semiMajorAxis;

    [Tooltip("Excentricité : 0 = cercle parfait, 0.9 = très elliptique (comète).\nTerre ≈ 0.017 | Mars ≈ 0.093 | Pluton ≈ 0.25")]
    [Range(0f, 0.95f)]
    public float eccentricity;

    [Tooltip("Durée d'une orbite complète en jours terrestres.")]
    [Min(0.1f)]
    public float orbitalPeriodDays;

    [Tooltip("Inclinaison orbitale en degrés par rapport au plan écliptique.")]
    [Range(0f, 180f)]
    public float orbitalInclination;

    [Tooltip("Position actuelle sur l'orbite [0–1] : 0 = périhélie (plus proche), 0.5 = aphélie.")]
    [Range(0f, 1f)]
    public float currentOrbitalPosition;

    // ----- Méthodes de calcul -----

    /// <summary>
    /// Distance instantanée à l'étoile (AU) en tenant compte de l'excentricité.
    /// Formule : r = a(1−e²) / (1 + e·cos(θ))
    /// </summary>
    public float CurrentDistanceAU()
    {
        float theta = currentOrbitalPosition * 2f * Mathf.PI;
        return semiMajorAxis * (1f - eccentricity * eccentricity)
               / (1f + eccentricity * Mathf.Cos(theta));
    }

    /// <summary>
    /// Intensité solaire reçue à la distance actuelle (loi inverse du carré).
    /// 1.0 = équivalent Terre (étoile G à 1 AU).
    /// </summary>
    public float SolarIntensityAt(float starLuminosity)
    {
        float dist = CurrentDistanceAU();
        return starLuminosity / (dist * dist);
    }

    /// <summary>
    /// Intensité solaire reçue au demi-grand axe (valeur moyenne sur une orbite).
    /// Utilisée pour la génération de carte (condition climatique de base).
    /// </summary>
    public float AverageSolarIntensity(float starLuminosity)
    {
        return starLuminosity / (semiMajorAxis * semiMajorAxis);
    }

    /// <summary>
    /// Calcule le temps de transit de Hohmann simplifié entre deux orbites (en jours).
    /// Utile pour estimer le coût temporel d'une mission de colonisation.
    /// techMultiplier : 1.0 = propulsion initiale, peut être réduit par la R&D.
    /// </summary>
    public static float HohmannTransitDays(float originAU, float targetAU, float starMass, float techMultiplier = 1f)
    {
        float semiTransfer = (originAU + targetAU) * 0.5f;
        // T_transfer = π × sqrt(a³ / GM) — en unités AU/années solaires
        float transferYears = Mathf.PI * Mathf.Sqrt(semiTransfer * semiTransfer * semiTransfer / starMass);
        return transferYears * 365.25f / techMultiplier;
    }

    // ----- Préréglages typiques -----

    public static OrbitalParameters EarthLike => new OrbitalParameters
    {
        semiMajorAxis        = 1.00f,
        eccentricity         = 0.017f,
        orbitalPeriodDays    = 365.25f,
        orbitalInclination   = 0f,
        currentOrbitalPosition = 0f
    };

    public static OrbitalParameters MarsLike => new OrbitalParameters
    {
        semiMajorAxis        = 1.52f,
        eccentricity         = 0.093f,
        orbitalPeriodDays    = 686.97f,
        orbitalInclination   = 1.85f,
        currentOrbitalPosition = 0f
    };

    public static OrbitalParameters Kepler442b => new OrbitalParameters
    {
        semiMajorAxis        = 0.409f,
        eccentricity         = 0.04f,
        orbitalPeriodDays    = 112.3f,
        orbitalInclination   = 89.0f,
        currentOrbitalPosition = 0f
    };
}
