using UnityEngine;
using System;

// =============================================================================
// Structs physiques — données permanentes des corps orbitaux, éditables en Inspector
// =============================================================================

/// <summary>
/// Propriétés de rotation et d'axe d'un corps céleste.
/// Note : solarIntensity et tidallyLocked sont calculés depuis SolarSystemData,
///        ne pas les saisir ici.
/// </summary>
[Serializable]
public struct PlanetaryPhysics
{
    [Tooltip("Température de base à l'équateur en surface (°C). Mars ≈ -60, Terre ≈ +15, Volcanique ≈ +300")]
    public float baseEquatorTemperature;

    [Tooltip("Vitesse de rotation [0–1]. 0 = synchrone, 1 = rotation rapide (force de Coriolis forte).\n" +
             "Valeur dérivée automatiquement si tidallyLocked = true dans SolarSystemData.")]
    [Range(0f, 1f)]
    public float rotationSpeed;

    [Tooltip("Inclinaison axiale (°). > 10° = saisons marquées. Terre = 23.4°, Uranus ≈ 98°")]
    [Range(0f, 180f)]
    public float axialTilt;
}

/// <summary>
/// Composition gazeuse de l'atmosphère.
/// Les ratios sont approximatifs (somme ≈ 1). L'oxygène et le CO₂ évoluent
/// via la terraformation.
/// </summary>
[Serializable]
public struct AtmosphericComposition
{
    [Tooltip("Densité atmosphérique globale. 0 = vide (astéroïde), 1 = très dense (Vénus)")]
    [Range(0f, 1f)]
    public float density;

    [Tooltip("Azote N₂ — gaz tampon, neutre. Base d'une atmosphère respirable.")]
    [Range(0f, 1f)]
    public float n2Ratio;

    [Tooltip("Oxygène O₂ — requis pour la respiration et la végétation sans infra. < 0.05 = impossible sans serre.")]
    [Range(0f, 1f)]
    public float o2Ratio;

    [Tooltip("CO₂ — effet de serre. > 0.50 → +20°C sur tempLocale. Utile pour terraformer une planète froide.")]
    [Range(0f, 1f)]
    public float co2Ratio;

    [Tooltip("Méthane CH₄ — effet de serre fort (+15°C si > 0.10). Toxique. Exploitable comme carburant.")]
    [Range(0f, 1f)]
    public float ch4Ratio;

    [Tooltip("Gaz toxiques (SO₂, NH₃…). > 0.30 → force biome ATMOSPHÈRE TOXIQUE sur les hexes exposés.")]
    [Range(0f, 1f)]
    public float toxinRatio;

    // --- Profils prédéfinis ---

    public static AtmosphericComposition Mars => new AtmosphericComposition
        { density = 0.01f, n2Ratio = 0.02f, o2Ratio = 0f, co2Ratio = 0.95f, ch4Ratio = 0f, toxinRatio = 0.10f };

    public static AtmosphericComposition EarthLike => new AtmosphericComposition
        { density = 0.80f, n2Ratio = 0.78f, o2Ratio = 0.21f, co2Ratio = 0.01f, ch4Ratio = 0f, toxinRatio = 0f };

    public static AtmosphericComposition Volcanic => new AtmosphericComposition
        { density = 0.60f, n2Ratio = 0.10f, o2Ratio = 0f, co2Ratio = 0.40f, ch4Ratio = 0.10f, toxinRatio = 0.60f };

    public static AtmosphericComposition IcyMoon => new AtmosphericComposition
        { density = 0.05f, n2Ratio = 0.90f, o2Ratio = 0f, co2Ratio = 0.05f, ch4Ratio = 0.05f, toxinRatio = 0f };
}

/// <summary>
/// Profil géologique et hydrologique global du corps.
/// </summary>
[Serializable]
public struct GeologicalProfile
{
    [Tooltip("Quantité totale d'eau disponible sur la planète [0–1]. 0 = désert total, 1 = monde océan.")]
    [Range(0f, 1f)]
    public float waterAbundance;

    [Tooltip("Activité volcanique et géothermique [0–1]. > 0.6 = géothermie exploitable.")]
    [Range(0f, 1f)]
    public float geologicalActivity;

    [Tooltip("Richesse minérale globale [0–1]. Multiplie le rendement des mines.")]
    [Range(0f, 1f)]
    public float mineralRichness;

    [Tooltip("Présence d'un champ magnétique. Réduit les dommages des éruptions solaires.")]
    public bool magneticField;
}

// =============================================================================
// Couches de génération procédurale
// =============================================================================

/// <summary>
/// Une tranche verticale du corps céleste : une couche (WorldLayer) avec ses biomes possibles
/// et le seuil de bruit maximal qui la déclenche (entre 0 et 1, tri croissant).
/// </summary>
[Serializable]
public class LayerZone
{
    public WorldLayer layer;

    [Range(0f, 1f)]
    [Tooltip("Seuil cumulatif de bruit de hauteur (0 → 1). Les zones doivent être triées par maxHeight croissant.")]
    public float maxHeight = 1f;

    [Tooltip("Biomes pouvant apparaître dans cette couche.")]
    public TerrainData[] biomes;
}

// CelestialBodyData.cs — structs partagées par tous les OrbitalBody.
// La classe CelestialBodyData a été remplacée par la hiérarchie OrbitalBody (World/Cosmos/).
// PlanetaryPhysics, AtmosphericComposition, GeologicalProfile, LayerZone restent ici
// pour être accessibles globalement sans import de namespace.
