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

    [Header("Hydrologie relief")]
    [Range(0.5f, 1f)] public float basinCapacity = 0.8f;
    [Range(0.6f, 0.95f)] public float lakeWaterThreshold = 0.75f;
    public Vector2 coastalWaterThreshold = new Vector2(0.55f, 0.85f);

    [Header("Cohérence macro vers micro")]
    [Range(0f, 1f)] public float coherenceWaterBlend = 0.55f;
    [Range(0f, 1f)] public float coherenceRetentionBias = 0.45f;
    [Range(0f, 1f)] public float coherenceRunoffBias = 0.35f;
    [Range(0f, 1f)] public float coherenceBiomeBias = 0.5f;
    [Range(0f, 1f)] public float coherenceRiverBias = 0.45f;
}
