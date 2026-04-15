using UnityEngine;
using System;

/// <summary>
/// Représente la sphère 3D d'une planète en vue planétaire.
///
/// Responsabilités :
///   - Recevoir un CelestialBodyData via LoadPlanet()
///   - Générer la grille planétaire basse résolution (PlanetaryHexGrid)
///   - Générer la texture équirectangulaire (PlanetTextureGenerator)
///   - Appliquer la texture au MeshRenderer
///   - Détecter les clics sur la sphère → convertir textureCoord UV → lat/lon normalisés
///     → émettre OnRegionClicked
///
/// Prérequis Unity :
///   - GameObject avec un MeshFilter (UV sphère standard) + MeshRenderer + Collider
///   - La sphère Unity native (GameObject > 3D Object > Sphere) convient directement.
///   - La caméra principale doit avoir un PhysicsRaycaster pour que OnMouseDown fonctionne.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Collider))]
public class PlanetSphere : MonoBehaviour
{
    // =========================================================
    // Events
    // =========================================================

    /// <summary>
    /// Déclenché quand l'utilisateur clique sur la sphère.
    /// latNorm [0–1] : 0 = pôle sud, 0.5 = équateur, 1 = pôle nord.
    /// lonNorm [0–1] : position est-ouest.
    /// </summary>
    public event Action<float, float> OnRegionClicked;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Shader property")]
    [Tooltip("Nom de la propriété texture dans le shader (ex: _BaseMap pour URP Lit, _MainTex pour Standard)")]
    [SerializeField] private string textureProperty = "_BaseMap";

    // =========================================================
    // Runtime
    // =========================================================

    private MeshRenderer _meshRenderer;
    private Texture2D    _currentTexture;
    private HexCell[]    _planetCells;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
    }

    // =========================================================
    // API publique — appelée par ViewManager
    // =========================================================

    /// <summary>
    /// Charge et affiche la planète : génère la grille planétaire + texture.
    /// </summary>
    public void LoadPlanet(CelestialBodyData body)
    {
        if (body == null)
        {
            Debug.LogError("[PlanetSphere] CelestialBodyData manquant.");
            return;
        }

        // Détruit l'ancienne texture pour éviter les fuites mémoire
        if (_currentTexture != null)
            Destroy(_currentTexture);

        // Génération grille + texture
        _planetCells    = PlanetaryHexGrid.Generate(body);
        _currentTexture = PlanetTextureGenerator.Generate(_planetCells);

        // Application au matériau (instance, pas l'original partagé)
        _meshRenderer.material.SetTexture(textureProperty, _currentTexture);

        Debug.Log($"[PlanetSphere] Planète chargée : {body.bodyName}");
    }

    // =========================================================
    // Détection de clic
    // =========================================================

    private void OnMouseDown()
    {
        // Raycast depuis la souris vers la sphère pour récupérer les UV
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit))
            return;

        // hit.textureCoord : UV de la sphère Unity (x = longitude, y = latitude)
        float lonNorm = hit.textureCoord.x;
        float latNorm = hit.textureCoord.y;

        Debug.Log($"[PlanetSphere] Clic → lat={latNorm:F3} lon={lonNorm:F3}");
        OnRegionClicked?.Invoke(latNorm, lonNorm);
    }

    // =========================================================
    // Cleanup
    // =========================================================

    private void OnDestroy()
    {
        if (_currentTexture != null)
            Destroy(_currentTexture);
    }
}
