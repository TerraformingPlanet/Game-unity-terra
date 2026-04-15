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

/// <summary>
/// Type de corps céleste gouvernant les biomes disponibles et la génération procédurale.
/// </summary>
public enum CelestialBodyType
{
    Rocky,      // planète rocheuse classique
    IcyMoon,    // lune glacée
    OceanWorld, // monde océanique
    Desert,     // désert aride
    Volcanic,   // planète volcanique
    GasGiant,   // géante gazeuse
    Asteroid    // astéroïde / corps mineur
}
