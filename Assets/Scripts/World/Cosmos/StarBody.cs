using UnityEngine;

/// <summary>
/// Classe spectrale d'une étoile — détermine la luminosité, la zone habitable
/// et la probabilité de verrouillage tidal des corps proches.
/// </summary>
public enum StarType
{
    M,          // Naine rouge   — très commune, longévité extrême, UV faible
    K,          // Naine orange  — stable, longévité très longue
    G,          // Naine jaune   — type Soleil
    F,          // Sous-géante   — chaude, UV intense
    A,          // Blanche       — courte vie, rayonnement fort
    Neutron,    // Étoile à neutrons — exotique, rayonnement X intense
    Binary      // Système binaire — zones habitables complexes
}

/// <summary>
/// ScriptableObject décrivant une étoile.
/// Remplace la struct StarData — peut désormais être référencée depuis SolarSystemData
/// et devenir un objet jouable (stations solaires, extraction d'énergie).
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Celestial Bodies/Star", fileName = "NewStar")]
public class StarBody : CelestialBody
{
    [Header("Type spectral")]
    public StarType spectralType = StarType.G;

    [Header("Physique stellaire")]
    [Tooltip("Luminosité relative au Soleil. Soleil = 1.0, naine rouge M ≈ 0.04, géante F ≈ 3.0.")]
    [Range(0.0001f, 50f)]
    public float luminosity = 1f;

    [Tooltip("Masse en masses solaires. Influe sur le seuil de verrouillage tidal.")]
    [Range(0.08f, 15f)]
    public float mass = 1f;

    // =========================================================
    // Propriétés calculées (jamais saisies manuellement)
    // =========================================================

    /// <summary>Distance minimale de la zone habitable (AU). Calculée depuis la luminosité.</summary>
    public float HabitableZoneMin => Mathf.Sqrt(luminosity / 1.1f);

    /// <summary>Distance maximale de la zone habitable (AU). Calculée depuis la luminosité.</summary>
    public float HabitableZoneMax => Mathf.Sqrt(luminosity / 0.53f);

    /// <summary>
    /// Seuil de verrouillage tidal (AU). Un corps en-dessous est probablement synchrone.
    /// Approximation : 0.5 × cbrt(masse_stellaire)
    /// </summary>
    public float TidalLockThresholdAU => 0.5f * Mathf.Pow(mass, 1f / 3f);

    // =========================================================
    // Préréglages par type spectral
    // =========================================================

    /// <summary>Applique des valeurs typiques selon le type spectral. Utile pour initialiser en Inspector.</summary>
    public void ApplySpectralDefaults()
    {
        switch (spectralType)
        {
            case StarType.M:
                bodyName = "Étoile M"; radius = 200000f;
                luminosity = 0.04f; mass = 0.3f; displayColor = new Color(1f, 0.35f, 0.1f);
                break;
            case StarType.K:
                bodyName = "Étoile K"; radius = 500000f;
                luminosity = 0.21f; mass = 0.75f; displayColor = new Color(1f, 0.65f, 0.2f);
                break;
            case StarType.G:
                bodyName = "Sol"; radius = 696000f;
                luminosity = 1.0f; mass = 1.0f; displayColor = new Color(1f, 0.95f, 0.5f);
                break;
            case StarType.F:
                bodyName = "Étoile F"; radius = 900000f;
                luminosity = 3.0f; mass = 1.4f; displayColor = new Color(0.9f, 0.9f, 1f);
                break;
            case StarType.A:
                bodyName = "Étoile A"; radius = 1200000f;
                luminosity = 12f; mass = 2.1f; displayColor = new Color(0.7f, 0.8f, 1f);
                break;
            case StarType.Neutron:
                bodyName = "Pulsar"; radius = 10f;
                luminosity = 0.001f; mass = 1.4f; displayColor = new Color(0.5f, 0.9f, 1f);
                break;
        }
    }
}
