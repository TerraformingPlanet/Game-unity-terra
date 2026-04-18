using UnityEngine;

/// <summary>
/// Classe de base abstraite pour tous les corps célestes.
/// Étoiles, planètes, lunes, astéroïdes et géantes gazeuses en héritent.
/// </summary>
public abstract class CelestialBody : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("Nom affiché dans l'interface et les logs.")]
    public string bodyName = "Corps Inconnu";

    [Header("Visualisation")]
    [Tooltip("Rayon en km. Terre = 6371, Soleil ≈ 696000, Cérès ≈ 473.")]
    [Min(0f)]
    public float radius = 1000f;

    [Tooltip("Couleur d'affichage dans la vue système solaire.")]
    public Color displayColor = Color.gray;
}
