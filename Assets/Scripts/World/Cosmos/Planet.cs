using UnityEngine;

/// <summary>Type de planète rocheuse ou liquide — gouverne les biomes disponibles et les profils de génération.</summary>
public enum PlanetType
{
    Rocky,      // planète rocheuse classique (Mars, Kepler-442b)
    OceanWorld, // monde entièrement océanique
    Desert,     // désert aride, très peu d'eau
    Volcanic,   // activité volcanique dominante
}

/// <summary>
/// Planète colonisable : rocheuse, océanique, désertique ou volcanique.
/// Pipeline de génération procédurale complet disponible.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Celestial Bodies/Planet", fileName = "NewPlanet")]
public class Planet : OrbitalBody
{
    [Header("Type planétaire")]
    public PlanetType planetType = PlanetType.Rocky;
}
