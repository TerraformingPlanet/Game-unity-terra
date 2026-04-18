using UnityEngine;

/// <summary>
/// Classe abstraite pour tout corps en orbite autour d'une étoile (ou d'un autre corps) :
/// planètes, lunes, astéroïdes, géantes gazeuses.
///
/// Contient le profil physique complet exploité par le pipeline de génération de carte.
/// Les propriétés calculées depuis l'orbite (solarIntensity, tidallyLocked) ne sont
/// pas stockées ici — elles sont obtenues via SolarSystemData au moment de la génération.
/// </summary>
public abstract class OrbitalBody : CelestialBody
{
    [Header("Accessibilité")]
    [Tooltip("Corps atterrissable et colonisable directement. False = accès orbital uniquement (géante gazeuse).")]
    public bool isLandable = true;

    [Header("Physique planétaire")]
    public PlanetaryPhysics physics;

    [Header("Atmosphère")]
    public AtmosphericComposition atmosphere;

    [Header("Géologie & Hydrologie")]
    public GeologicalProfile geology;

    [Header("Génération procédurale")]
    public MapGenParameters genParams;

    [Header("Couches (triées par maxHeight croissant, somme = 1)")]
    public LayerZone[] layers;

    // =============================================================
    // Propriétés dérivées (depuis les structs)
    // =============================================================

    /// <summary>
    /// Offset de température dû à l'effet de serre de l'atmosphère.
    /// CO₂ > 0.50 → +20°C | CH₄ > 0.10 → +15°C
    /// </summary>
    public float GreenhouseTemperatureOffset
    {
        get
        {
            float offset = 0f;
            if (atmosphere.co2Ratio > 0.50f) offset += 20f;
            if (atmosphere.ch4Ratio > 0.10f) offset += 15f;
            return offset;
        }
    }

    /// <summary>Végétation impossible sans infrastructure pressurisée.</summary>
    public bool RequiresEnclosedVegetation => atmosphere.o2Ratio < 0.05f;

    /// <summary>Hexes de surface exposés → biome ATMOSPHÈRE TOXIQUE forcé.</summary>
    public bool HasToxicSurface => atmosphere.toxinRatio > 0.30f;

    // =============================================================
    // Génération procédurale
    // =============================================================

    /// <summary>Retourne la zone correspondant à une valeur de bruit normalisée [0, 1].</summary>
    public LayerZone GetLayerForHeight(float normalizedHeight)
    {
        if (layers == null || layers.Length == 0) return null;
        for (int i = 0; i < layers.Length; i++)
        {
            if (normalizedHeight <= layers[i].maxHeight || i == layers.Length - 1)
                return layers[i];
        }
        return layers[layers.Length - 1];
    }
}
