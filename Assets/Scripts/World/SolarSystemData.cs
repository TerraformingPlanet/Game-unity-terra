using UnityEngine;
using System;

/// <summary>
/// Association entre un corps céleste, son orbite, ses lunes et son état de colonisation.
/// Un OrbitalSlot par corps dans le système solaire.
/// </summary>
[Serializable]
public class OrbitalSlot
{
    [Tooltip("Corps céleste (planète, lune, astéroïde…)")]
    public OrbitalBody body;

    [Tooltip("Paramètres orbitaux autour de l'étoile primaire (ou de la planète parente pour une lune)")]
    public OrbitalParameters orbit;

    [Tooltip("Lunes en orbite autour de ce corps (laisser vide si aucune)")]
    [SerializeReference]
    public OrbitalSlot[] moons;

    // --- État de colonisation (mis à jour par le TickManager pendant la partie) ---

    [Tooltip("Le corps a été découvert/scanné par au moins une corpo")]
    public bool isDiscovered;

    [Tooltip("Une colonie permanente est établie sur ce corps")]
    public bool isColonized;

    [Tooltip("Progression de la colonisation initiale [0–100]")]
    [Range(0, 100)]
    public int colonizationProgress;
}

/// <summary>
/// ScriptableObject racine décrivant un système solaire complet.
/// Contient l'étoile, les corps orbitaux et expose les calculs physiques
/// (intensité solaire, verrouillage tidal, temps de transit).
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Solar System", fileName = "NewSolarSystem")]
public class SolarSystemData : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("Nom du système (ex: Kepler-442, Sol)")]
    public string systemName = "Système Inconnu";

    [Tooltip("Distance depuis la Terre en années-lumière (contexte narratif uniquement)")]
    public float distanceLightYears;

    [Header("Étoile(s)")]
    public StarBody primaryStar;

    [Tooltip("Étoile(s) compagnon(s) pour les systèmes binaires ou triples — laisser vide sinon")]
    public StarBody[] companionStars;

    [Header("Corps en orbite (triés par semiMajorAxis croissant)")]
    public OrbitalSlot[] orbitalSlots;

    // =============================================================
    // API physique
    // =============================================================

    /// <summary>
    /// Intensité solaire reçue par un corps situé à <paramref name="semiMajorAxisAU"/> UA
    /// de l'étoile primaire (valeur moyenne sur l'orbite).
    /// 1.0 = équivalent Terre autour d'une étoile G.
    /// </summary>
    public float AverageSolarIntensity(float semiMajorAxisAU)
        => primaryStar.luminosity / (semiMajorAxisAU * semiMajorAxisAU);

    /// <summary>
    /// Détermine si un corps à <paramref name="semiMajorAxisAU"/> UA est probablement
    /// en verrouillage tidal avec l'étoile primaire.
    /// </summary>
    public bool IsTidallyLocked(float semiMajorAxisAU)
        => semiMajorAxisAU < primaryStar.TidalLockThresholdAU;

    /// <summary>
    /// Temps de transit Hohmann simplifié entre deux corps (en jours terrestres).
    /// <paramref name="techMultiplier"/> : 1.0 = propulsion de base,
    /// réduit par l'arbre technologique de propulsion.
    /// </summary>
    public float TransitDays(OrbitalSlot origin, OrbitalSlot destination, float techMultiplier = 1f)
        => OrbitalParameters.HohmannTransitDays(
            origin.orbit.semiMajorAxis,
            destination.orbit.semiMajorAxis,
            primaryStar.mass,
            techMultiplier);

    /// <summary>
    /// Retourne le slot orbitale d'un corps donné (null si non trouvé).
    /// Cherche aussi dans les lunes de chaque slot.
    /// </summary>
    public OrbitalSlot FindSlot(OrbitalBody body)
    {
        if (orbitalSlots == null) return null;
        foreach (var slot in orbitalSlots)
        {
            if (slot.body == body) return slot;
            if (slot.moons != null)
                foreach (var moon in slot.moons)
                    if (moon.body == body) return moon;
        }
        return null;
    }

    /// <summary>
    /// Calcule l'intensité solaire pour un corps donné depuis son OrbitalSlot.
    /// Retourne la valeur moyenne (basée sur semiMajorAxis).
    /// </summary>
    public float SolarIntensityFor(OrbitalBody body)
    {
        OrbitalSlot slot = FindSlot(body);
        if (slot == null)
        {
            Debug.LogWarning($"[SolarSystemData] Corps '{body?.bodyName}' introuvable dans '{systemName}'.");
            return 1f;
        }
        return AverageSolarIntensity(slot.orbit.semiMajorAxis);
    }

    /// <summary>
    /// Vérifie si un corps est tidalement verrouillé, en cherchant son slot.
    /// </summary>
    public bool IsTidallyLockedBody(OrbitalBody body)
    {
        OrbitalSlot slot = FindSlot(body);
        if (slot == null) return false;
        return IsTidallyLocked(slot.orbit.semiMajorAxis);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Affiche un résumé du système dans la console (appel depuis un bouton Inspector custom).
    /// </summary>
    [ContextMenu("Afficher résumé du système")]
    private void LogSystemSummary()
    {
        Debug.Log($"=== Système {systemName} ({distanceLightYears} al) ===");
        Debug.Log($"Étoile : {primaryStar?.bodyName} [{primaryStar?.spectralType}] lum={primaryStar?.luminosity} | " +
                  $"Zone habitable : {primaryStar.HabitableZoneMin:F2}–{primaryStar.HabitableZoneMax:F2} AU | " +
                  $"Seuil tidal : {primaryStar.TidalLockThresholdAU:F2} AU");

        if (orbitalSlots == null) return;
        foreach (var slot in orbitalSlots)
        {
            if (slot?.body == null) continue;
            float intensity = AverageSolarIntensity(slot.orbit.semiMajorAxis);
            bool locked = IsTidallyLocked(slot.orbit.semiMajorAxis);
            Debug.Log($"  [{slot.orbit.semiMajorAxis:F3} AU] {slot.body.bodyName} " +
                      $"| solaire={intensity:F2} | tidal={locked} | période={slot.orbit.orbitalPeriodDays:F1} j");
        }
    }
#endif
}
