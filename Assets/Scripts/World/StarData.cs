using UnityEngine;
using System;

/// <summary>
/// Classe spectrale d'une étoile — détermine la luminosité, la zone habitable
/// et la probabilité de verrouillage tidal des corps proches.
/// </summary>
public enum StarType
{
    M,          // Naine rouge  — très commune, longévité extrême, UV faible
    K,          // Naine orange — stable, longévité très longue
    G,          // Naine jaune  — type Soleil
    F,          // Sous-géante  — chaude, UV intense
    A,          // Blanche      — courte vie, rayonnement fort
    Neutron,    // Étoile à neutrons — exotique
    Binary      // Système binaire — two suns, complex habitable zones
}

/// <summary>
/// Données physiques d'une étoile.
/// Toutes les propriétés dérivées (zone habitable, seuil tidal) sont calculées,
/// jamais saisies manuellement.
/// </summary>
[Serializable]
public struct StarData
{
    [Tooltip("Nom de l'étoile (ex: Kepler-442, Sol)")]
    public string name;

    public StarType spectralType;

    [Tooltip("Luminosité relative au Soleil (1.0 = Soleil, 0.3 = K-type typique, 0.04 = M-type)")]
    [Range(0.0001f, 50f)]
    public float luminosity;

    [Tooltip("Masse en masses solaires (influe sur le seuil de verrouillage tidal)")]
    [Range(0.08f, 15f)]
    public float mass;

    // ----- Propriétés calculées (lecture seule en jeu) -----

    /// <summary>Distance minimale de la zone habitable (AU). Calculée depuis la luminosité.</summary>
    public float HabitableZoneMin => Mathf.Sqrt(luminosity / 1.1f);

    /// <summary>Distance maximale de la zone habitable (AU). Calculée depuis la luminosité.</summary>
    public float HabitableZoneMax => Mathf.Sqrt(luminosity / 0.53f);

    /// <summary>
    /// Seuil de verrouillage tidal (AU) : un corps en-dessous de ce seuil
    /// est probablement en rotation synchrone avec l'étoile.
    /// Approximation : 0.5 × cbrt(starMass)
    /// </summary>
    public float TidalLockThresholdAU => 0.5f * Mathf.Pow(mass, 1f / 3f);

    /// <summary>
    /// Retourne des valeurs par défaut typiques selon le type spectral.
    /// Utile pour initialiser rapidement un asset dans l'Inspector.
    /// </summary>
    public static StarData Default(StarType type) => type switch
    {
        StarType.M       => new StarData { name = "Étoile M",  spectralType = StarType.M,  luminosity = 0.04f, mass = 0.3f  },
        StarType.K       => new StarData { name = "Étoile K",  spectralType = StarType.K,  luminosity = 0.21f, mass = 0.75f },
        StarType.G       => new StarData { name = "Sol",        spectralType = StarType.G,  luminosity = 1.0f,  mass = 1.0f  },
        StarType.F       => new StarData { name = "Étoile F",  spectralType = StarType.F,  luminosity = 3.0f,  mass = 1.4f  },
        StarType.A       => new StarData { name = "Étoile A",  spectralType = StarType.A,  luminosity = 12f,   mass = 2.1f  },
        StarType.Neutron => new StarData { name = "Pulsar",     spectralType = StarType.Neutron, luminosity = 0.001f, mass = 1.4f },
        _                => new StarData { name = "Étoile G",  spectralType = StarType.G,  luminosity = 1.0f,  mass = 1.0f  },
    };
}
