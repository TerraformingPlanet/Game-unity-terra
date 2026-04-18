using UnityEngine;
using System;

// =============================================================================
// Profil de sol — propriétés physiques du substrat par hex
// =============================================================================

/// <summary>
/// Composition et état du sol d'un hex de surface.
/// Calculé à la génération par MapGenerator, évolue via les actions de terraformation.
/// </summary>
[Serializable]
public struct SoilProfile
{
    [Tooltip("Dureté du substrat [0–1]. 0 = sable/sédiment, 1 = roche massive. > 0.8 → défrichage +50%")]
    public float rockHardness;

    [Tooltip("Matière organique [0–1]. Augmente avec la végétation voisine (pédogenèse). < 0.05 → végétation impossible sans substrat artificiel")]
    public float organicContent;

    [Tooltip("Capacité de rétention d'eau [0–1]. > 0.6 → eau perdue (irrigation +30%). < 0.2 → ruissellement vers voisins bas")]
    public float porosity;

    [Tooltip("Concentration en minéraux extractibles [0–1]. > 0.7 → rendement minier ×2")]
    public float mineralDensity;

    [Tooltip("Sol contaminé (SO₂, retombées toxiques…). Nécessite dépollution avant construction")]
    public bool toxicSoil;

    [Tooltip("Conductivité thermique [0–1]. > 0.6 → centrale géothermique rentable. Basse sous la glace")]
    public float thermalConductivity;
}

// =============================================================================
// État physique local — calculé par hex à la génération
// =============================================================================

/// <summary>
/// Ensemble des propriétés physiques d'un hex calculées lors de la génération.
/// C'est ce que les règles de biome lisent, et ce que la terraformation modifie.
/// </summary>
[Serializable]
public struct HexPhysicalState
{
    [Tooltip("Altitude [0–1] issue du bruit de hauteur fractal")]
    public float altitude;

    [Tooltip("Température locale calculée (°C) : baseTemp + offsets latitude/greenhouse/altitude")]
    public float tempLocale;

    [Tooltip("Ratio d'eau disponible [0–1] : 0 = aride, 1 = saturé (ocean)")]
    public float waterRatio;

    [Tooltip("Niveau de toxines [0–1] : > 0.5 → biome ATMOSPHÈRE TOXIQUE forcé")]
    public float toxinLevel;

    [Tooltip("Vecteur vent local (direction + magnitude) sur le plan XZ")]
    public Vector2 windVector;

    [Tooltip("Vitesse du vent local [0–1], amplifiée par l'altitude")]
    public float windSpeed;

    [Tooltip("En ombre pluviométrique (sous le vent d'un relief) : précipitations réduites")]
    public bool rainShadow;

    [Tooltip("Hex traversé par une rivière (calculé par RiverSystem)")]
    public bool hasRiver;

    [Tooltip("Nombre de cellules qui drainent vers cet hex.")]
    public int flowAccumulation;

    [Tooltip("Classe topographique locale : pente, crête, bassin, chenal, source.")]
    public TerrainClass terrainClass;

    [Tooltip("Classe hydrologique locale : océan, côte, eau intérieure, sec, gelé.")]
    public WaterClassification waterClassification;

    [Tooltip("L'hex possède-t-il un voisin aval déterminé par le système hydrologique ?")]
    public bool hasDownstream;

    [Tooltip("Coordonnée Q du voisin aval si hasDownstream = true.")]
    public int downstreamQ;

    [Tooltip("Coordonnée R du voisin aval si hasDownstream = true.")]
    public int downstreamR;

    [Tooltip("Le bassin dispose-t-il d'un exutoire potentiel si le niveau d'eau monte ?")]
    public bool hasOverflowOutlet;

    [Tooltip("Coordonnée Q de l'exutoire potentiel du bassin.")]
    public int overflowQ;

    [Tooltip("Coordonnée R de l'exutoire potentiel du bassin.")]
    public int overflowR;

    public SoilProfile soil;
}

// =============================================================================
// HexCell — conteneur principal d'une case hexagonale
// =============================================================================

/// <summary>
/// Conteneur léger pour une case hexagonale.
/// Coordonnées axiales + biome + couche + état physique complet.
/// </summary>
public class HexCell
{
    public int Q { get; private set; }
    public int R { get; private set; }

    /// <summary>Index du hex dans le tableau HexCell[] de la grille (assigné par HexGrid/PlanetaryHexGrid).</summary>
    public int gridIndex;

    public TerrainData       terrain;
    public Vector3           center;
    public WorldLayer        layer  = WorldLayer.Surface;
    public OrbitalBody world;

    /// <summary>Appartenance politique de cet hex (Corporation, Nation ou Neutral).</summary>
    public HexOwnership ownership;

    /// <summary>État physique complet calculé par MapGenerator à la génération.</summary>
    public HexPhysicalState  state;

    public HexCell(int q, int r)
    {
        Q = q;
        R = r;
        center = HexMetrics.AxialToWorld(q, r);
    }

    /// <summary>
    /// Coordonnées sur la sphère planétaire, normalisées [0,1].
    /// Calculées par ViewManager.PlaceHexGridOnGlobe() lors de l'affichage overlay.
    /// Utilisées par le serveur dédié pour connaître la position globale de chaque tuile.
    /// </summary>
    public float latOnSphere;
    public float lonOnSphere;
}
