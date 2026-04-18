using UnityEngine;

/// <summary>
/// Géante gazeuse — non atterrissable directement.
/// La grille hex représente la couche orbitale (stations, extraction atmosphérique).
/// isLandable est forcé à false.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Celestial Bodies/Gas Giant", fileName = "NewGasGiant")]
public class GasGiant : OrbitalBody
{
    [Header("Composition atmosphérique — géante")]
    [Tooltip("Concentration en hydrogène gazeux [0–1]. Ressource exploitable (carburant H₂).")]
    [Range(0f, 1f)]
    public float atmosphericHydrogen = 0.75f;

    [Tooltip("Concentration en hélium-3 [0–1]. Ressource rare à haute valeur énergétique.")]
    [Range(0f, 1f)]
    public float helium3Abundance = 0.15f;

    [Tooltip("Vitesse des vents atmosphériques [0–1]. Influe sur la difficulté de récolte.")]
    [Range(0f, 1f)]
    public float windIntensity = 0.6f;

    // isLandable est forcé false — override la valeur de l'Inspector
    private void OnValidate()
    {
        isLandable = false;
    }

    // Garantit que isLandable reste false même si modifié en code
    public new bool isLandable
    {
        get => false;
        set { /* non modifiable */ }
    }
}
