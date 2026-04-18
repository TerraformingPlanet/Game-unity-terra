using UnityEngine;

/// <summary>Type de lune — influence les biomes et les conditions de surface.</summary>
public enum MoonType
{
    Icy,        // lune glacée (Europa, Encelade)
    Rocky,      // lune rocheuse (Lune terrestre, Io sans volcan dominant)
    Volcanic,   // lune à activité volcanique intense (Io)
    Oceanic,    // lune avec océan sous-glaciaire ou de surface
}

/// <summary>
/// Lune en orbite autour d'une planète ou d'une géante gazeuse.
/// Colonisable — pipeline de génération procédurale complet identique aux planètes.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Celestial Bodies/Moon", fileName = "NewMoon")]
public class Moon : OrbitalBody
{
    [Header("Type de lune")]
    public MoonType moonType = MoonType.Rocky;
}
