using UnityEngine;

[CreateAssetMenu(menuName = "Terraformation/TerrainData", fileName = "NewTerrainData")]
public class TerrainData : ScriptableObject
{
    public TerrainType terrainType;
    public string displayName;
    public Color color = Color.white;
    [TextArea] public string description;
}
