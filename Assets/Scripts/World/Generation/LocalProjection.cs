using UnityEngine;

/// <summary>
/// Projection plane tangent local à la sphère Goldberg.
///
/// Définit un repère orthonormé (Center, Right, Forward) ancré à un point de
/// la sphère. Permet de projeter des points 3D sur un plan 2D (x, z) et de
/// détecter la visibilité d'une face GP depuis ce plan.
///
/// Usage :
///   var proj = new LocalProjection(GoldbergSphereGenerator.VisualRadius);
///   proj.Update(focusOnSphere);
///   Vector3 flat = proj.Project(worldPos, scale);
/// </summary>
public class LocalProjection
{
    // =========================================================
    // Propriétés
    // =========================================================

    public float   Radius  { get; private set; }
    public Vector3 Center  { get; private set; }   // direction normalisée du focus
    public Vector3 Right   { get; private set; }
    public Vector3 Forward { get; private set; }

    // =========================================================
    // Constructeur
    // =========================================================

    public LocalProjection(float radius)
    {
        Radius  = radius;
        Center  = Vector3.up;
        Right   = Vector3.right;
        Forward = Vector3.forward;
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Met à jour le plan tangent centré sur <paramref name="focusOnSphere"/>.
    /// </summary>
    /// <param name="focusOnSphere">
    ///   Point sur la surface de la sphère (magnitude ≈ Radius).
    ///   Sera normalisé en interne.
    /// </param>
    public void Update(Vector3 focusOnSphere)
    {
        Center = focusOnSphere.normalized;

        // Axe droite = produit vectoriel avec Up ; fallback si focus est au pôle
        Vector3 upRef = Vector3.up;
        float   dot   = Mathf.Abs(Vector3.Dot(Center, upRef));
        if (dot > 0.99f)
            upRef = Vector3.forward;

        Right   = Vector3.Cross(upRef, Center).normalized;
        Forward = Vector3.Cross(Center, Right).normalized;
    }

    /// <summary>
    /// Projette <paramref name="worldPos"/> sur le plan tangent.
    /// Retourne un Vector3 (x, 0, z) dans l'espace plan.
    /// </summary>
    /// <param name="worldPos">Position monde à projeter.</param>
    /// <param name="scale">Facteur d'échelle (ex : 15 pour VisualRadius=10).</param>
    public Vector3 Project(Vector3 worldPos, float scale)
    {
        Vector3 offset = worldPos - Center * Radius;
        float   x      = Vector3.Dot(offset, Right)   * scale;
        float   z      = Vector3.Dot(offset, Forward)  * scale;
        return new Vector3(x, 0f, z);
    }

    /// <summary>
    /// Retourne vrai si la direction <paramref name="normalizedDir"/> est dans
    /// le cône de visibilité du plan tangent (dot > <paramref name="threshold"/>).
    /// </summary>
    /// <param name="normalizedDir">Direction normalisée à tester.</param>
    /// <param name="threshold">Seuil du produit scalaire (0.75 ≈ ±41°).</param>
    public bool IsVisible(Vector3 normalizedDir, float threshold = 0.75f)
    {
        return Vector3.Dot(Center, normalizedDir) > threshold;
    }

    /// <summary>
    /// Projette en sens inverse : à partir d'une position 2D (x, z) sur le plan,
    /// retourne la direction normalisée correspondante sur la sphère.
    /// </summary>
    /// <param name="x">Coordonnée x sur le plan.</param>
    /// <param name="z">Coordonnée z sur le plan.</param>
    /// <param name="scale">Le même facteur d'échelle utilisé dans Project().</param>
    public Vector3 ReverseProject(float x, float z, float scale)
    {
        return (Center + Right * (x / scale) + Forward * (z / scale)).normalized;
    }
}
