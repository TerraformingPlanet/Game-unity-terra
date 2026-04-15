using UnityEngine;
using System;

// =============================================================================
// Modificateurs sur HexPhysicalState — appliqués par TerraformSystem
// =============================================================================

/// <summary>
/// Décrit les deltas appliqués à HexPhysicalState lors d'une action de terraformation.
/// Tous les champs sont additifs (positif = augmente, négatif = diminue).
/// La modification est appliquée par tick tant que l'action est active.
/// </summary>
[Serializable]
public struct HexStateModifier
{
    [Tooltip("Variation de température locale (°C) par tick")]
    public float tempDelta;

    [Tooltip("Variation de waterRatio [0–1] par tick")]
    public float waterDelta;

    [Tooltip("Variation de toxinLevel [0–1] par tick (négatif = dépollution)")]
    public float toxinDelta;

    [Tooltip("Variation de organicContent [0–1] par tick")]
    public float organicDelta;

    [Tooltip("Variation de rockHardness [0–1] par tick (négatif = ramollissement / extraction)")]
    public float hardnessDelta;

    [Tooltip("Variation de mineralDensity [0–1] par tick (négatif = extraction/épuisement)")]
    public float mineralDelta;
}

// =============================================================================
// TerraformActionData — ScriptableObject paramétrant une action
// =============================================================================

/// <summary>
/// ScriptableObject décrivant une action de terraformation.
/// Créer via Assets > Create > Terraformation > TerraformActionData.
///
/// Exemple de paramétrage :
///   Heat      : cost=50, durationTicks=3, tempDelta=+8
///   Irrigate  : cost=30, durationTicks=5, waterDelta=+0.15, hardnessDelta=-0.05
///   Plant     : cost=20, durationTicks=10, organicDelta=+0.08
///   Mine      : cost=10, durationTicks=1, mineralDelta=-0.10, hardnessDelta=-0.05
///   Detoxify  : cost=80, durationTicks=4, toxinDelta=-0.20
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/TerraformActionData", fileName = "NewTerraformAction")]
public class TerraformActionData : ScriptableObject
{
    [Header("Identité")]
    public TerraformAction actionType;
    public string displayName = "Action";
    [TextArea] public string description;

    [Header("Coût & Durée")]
    [Tooltip("Coût en crédits pour déclencher l'action")]
    [Min(0)]
    public int cost;

    [Tooltip("Nombre de ticks pendant lesquels les modificateurs sont appliqués")]
    [Min(1)]
    public int durationTicks = 1;

    [Header("Effets sur HexPhysicalState")]
    public HexStateModifier modifier;

    // =========================================================
    // Validation
    // =========================================================

    /// <summary>
    /// Vérifie si ce hex satisfait les pré-conditions de l'action.
    /// Retourne false si l'action ne peut pas être appliquée (avec raison loguée).
    /// </summary>
    public bool CanApply(HexCell cell)
    {
        switch (actionType)
        {
            case TerraformAction.Plant:
                if (cell.state.waterRatio < 0.1f)
                {
                    Debug.Log("[TerraformAction] Plant : eau insuffisante (waterRatio < 0.1)");
                    return false;
                }
                if (cell.state.tempLocale < -30f)
                {
                    Debug.Log("[TerraformAction] Plant : trop froid (< -30°C)");
                    return false;
                }
                break;

            case TerraformAction.Mine:
                if (cell.state.soil.rockHardness < 0.05f)
                {
                    Debug.Log("[TerraformAction] Mine : sol trop mou pour miner");
                    return false;
                }
                break;

            case TerraformAction.Irrigate:
                // Toujours possible (eau importée de l'espace si besoin)
                break;
        }
        return true;
    }
}
