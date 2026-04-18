using UnityEngine;

/// <summary>
/// ScriptableObject décrivant une galaxie — conteneur de plusieurs systèmes solaires.
/// Permet le voyage inter-systèmes et les événements galactiques.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Galaxy", fileName = "NewGalaxy")]
public class GalaxyData : ScriptableObject
{
    [Header("Identité")]
    public string galaxyName = "Galaxie Inconnue";

    [TextArea(2, 4)]
    public string description;

    [Header("Systèmes solaires")]
    [Tooltip("Tous les systèmes solaires accessibles dans cette galaxie.")]
    public SolarSystemData[] solarSystems;
}
