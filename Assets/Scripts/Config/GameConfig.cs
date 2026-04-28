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

    [Tooltip("Timeout HTTP en secondes")]
    public float simulationServerTimeoutSeconds = 5f;
}
