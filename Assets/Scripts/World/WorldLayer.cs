/// <summary>
/// Couche verticale d'un corps céleste, de l'intérieur vers l'extérieur.
/// Chaque case de la grille appartient à une couche qui détermine son pool de biomes.
/// </summary>
public enum WorldLayer
{
    Underground = 0,   // sous-sol, cavernes, ressources profondes
    OceanFloor  = 1,   // fond marin
    Ocean       = 2,   // eaux libres
    Surface     = 3,   // sol habitable
    Atmosphere  = 4,   // couche gazeuse, nuages, toxines
    Space       = 5    // orbite basse, vide
}

// CelestialBodyType a été remplacé par les classes concrètes PlanetType, MoonType, AsteroidType
// dans World/Cosmos/Planet.cs, Moon.cs, Asteroid.cs.
