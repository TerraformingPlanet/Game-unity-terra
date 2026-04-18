using UnityEngine;

/// <summary>Composition d'un astéroïde — détermine les ressources extractibles prioritaires.</summary>
public enum AsteroidType
{
    Rocky,          // silicates, peu de métaux
    Metallic,       // riche en fer, nickel, métaux rares
    Icy,            // eau glacée, méthane, ammoniaque
    Carbonaceous,   // carbone organique, eau, minéraux
}

/// <summary>
/// Corps mineur : astéroïde ou planétoïde.
/// Colonisable sous forme de station minière.
/// Atmosphère quasi nulle, gravité faible, focus sur l'extraction de ressources.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Celestial Bodies/Asteroid", fileName = "NewAsteroid")]
public class Asteroid : OrbitalBody
{
    [Header("Type d'astéroïde")]
    public AsteroidType asteroidType = AsteroidType.Metallic;
}
