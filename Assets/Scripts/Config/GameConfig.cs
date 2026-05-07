using UnityEngine;

/// <summary>
/// ScriptableObject centralisant la configuration runtime du jeu.
/// Créer via Assets > Create > Terraformation > GameConfig.
/// Assigner l'instance dans GameHUDController (Inspector).
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/GameConfig", fileName = "GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("Serveur de simulation")]
    [Tooltip("URL de base du DedicatedServer (sans slash final)")]
    public string simulationServerUrl = "http://127.0.0.1:8080";

    [Tooltip("Timeout HTTP en secondes (requêtes courtes : /bodies, /at, etc.)")]
    public float simulationServerTimeoutSeconds = 15f;

    [Tooltip("Timeout en secondes pour le fetch des tuiles /tiles/lod (première requête peut déclencher une génération serveur)")]
    public float tilesFetchTimeoutSeconds = 60f;

    [Tooltip("Timeout en secondes pour le polling /world (TerraformSystem). Plus grand que le timeout court car la génération serveur peut tenir le lock quelques secondes.")]
    public float serverPollingTimeoutSeconds = 30f;
}
