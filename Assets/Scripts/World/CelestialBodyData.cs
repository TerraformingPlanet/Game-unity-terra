using UnityEngine;
using System;

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

/// <summary>
/// ScriptableObject décrivant un corps céleste (planète, lune, astéroïde…).
/// Définit les couches de génération procédurale et les biomes associés.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/CelestialBody", fileName = "NewCelestialBody")]
public class CelestialBodyData : ScriptableObject
{
    [Header("Identité")]
    public string bodyName = "Nouvelle Planète";
    public CelestialBodyType bodyType = CelestialBodyType.Rocky;

    [Header("Génération procédurale")]
    public MapGenParameters genParams;

    [Header("Couches (triées par maxHeight croissant, somme = 1)")]
    public LayerZone[] layers;

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
