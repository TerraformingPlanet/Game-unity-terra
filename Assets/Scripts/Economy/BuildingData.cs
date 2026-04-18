using UnityEngine;

// =============================================================================
// Bâtiments — définition (asset Inspector) + instance runtime
// =============================================================================

/// <summary>Types de bâtiments construisibles dans le jeu.</summary>
public enum BuildingType
{
    Mine,
    Greenhouse,
    Refinery,
    PowerPlant,
    Laboratory,
    HQ,
    SpacePort,
    SolarCollector,
    OrbitalStation
}

/// <summary>
/// Définition statique d'un type de bâtiment, éditée dans l'Inspector.
/// Une instance par type de bâtiment défini dans le design.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Building Data", fileName = "NewBuilding")]
public class BuildingData : ScriptableObject
{
    [Header("Identité")]
    public BuildingType buildingType;
    public string       displayName = "Nouveau Bâtiment";

    [Header("Coût de construction")]
    public ResourceStack[] buildCost;

    [Header("Production par tick")]
    public ResourceStack[] productionPerTick;

    [Header("Consommation par tick")]
    public ResourceStack[] consumptionPerTick;

    [Header("Couches de pose valides")]
    [Tooltip("WorldLayer(s) sur lesquels ce bâtiment peut être construit.")]
    public WorldLayer[] validLayers;
}
