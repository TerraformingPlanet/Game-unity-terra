using UnityEngine;

[CreateAssetMenu(menuName = "Terraformation/TerrainData", fileName = "NewTerrainData")]
public class TerrainData : ScriptableObject
{
    public TerrainType terrainType;
    public string displayName;
    public Color color = Color.white;
    [TextArea] public string description;

    [Header("Couches compatibles")]
    [Tooltip("WorldLayers où ce biome peut apparaître (utilisé par l'éditeur et la génération).")]
    public WorldLayer[] compatibleLayers = { WorldLayer.Surface };
}
