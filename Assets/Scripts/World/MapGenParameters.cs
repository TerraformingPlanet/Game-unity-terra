using UnityEngine;

/// <summary>
/// Paramètres de génération procédurale par corps céleste.
/// Deux bruits fractals indépendants : hauteur (→ couche) + biome (→ variation dans la couche).
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/MapGenParameters", fileName = "MapGenParams")]
public class MapGenParameters : ScriptableObject
{
    [Header("Graine")]
    public int seed = 42;
    public bool randomSeedOnPlay = true;

    [Header("Bruit de hauteur  (détermine la couche WorldLayer)")]
    [Min(0.1f)] public float heightScale = 4f;
    [Range(1, 8)] public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    [Range(1f, 4f)] public float lacunarity = 2f;

    [Header("Bruit de biome  (variation au sein d'une couche)")]
    [Min(0.1f)] public float biomeScale = 2.5f;
}
