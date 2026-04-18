using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Visualisation 2D du système solaire en vue OrthoTopDown.
///
/// Responsabilités :
///   - Lire SolarSystemData et créer un GameObject (sphère) par planète dans les OrbitalSlots
///   - Dessiner les orbites via LineRenderer (cercles dans le plan XZ)
///   - Détecter les clics sur les planètes → émettre OnPlanetClicked
///
/// Mise à l'échelle :
///   - Le demi-grand axe (en UA) est multiplié par orbitScale pour obtenir une distance
///     en unités Unity.  Ajuster orbitScale dans l'Inspector selon la taille de la scène.
///   - La taille des sphères est proportionnelle à body.radius (ou planetDisplayScale si nul).
///
/// Prérequis Unity :
///   - Ce MonoBehaviour doit être sur un GameObject actif dans solarSystemRoot.
///   - La caméra doit avoir les Physics Raycasters activés pour OnMouseDown.
/// </summary>
public class SolarSystemView : MonoBehaviour
{
    // =========================================================
    // Events
    // =========================================================

    /// <summary>
    /// Déclenché quand l'utilisateur clique sur une planète.
    /// body      : le OrbitalBody de la planète cliquée.
    /// worldPos  : position world de la sphère (centre d'orbite pour la caméra).
    /// </summary>
    public event Action<OrbitalBody, Vector3> OnPlanetClicked;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Données")]
    [Tooltip("Le système solaire à visualiser")]
    [SerializeField] private SolarSystemData solarSystem;

    [Header("Mise à l'échelle")]
    [Tooltip("1 UA → X unités Unity. Augmenter pour un système plus grand à l'écran.")]
    [SerializeField] private float orbitScale = 10f;

    [Tooltip("Rayon d'affichage par défaut (unités Unity) quand body.radius = 0")]
    [SerializeField] private float defaultPlanetRadius = 0.5f;

    [Tooltip("Rayon d'affichage d'une planète de taille terrestre (6371 km) en unités Unity")]
    [SerializeField] private float planetRadiusScale = 1.25f;

    [Tooltip("Rayon d'affichage minimal pour garder les planètes visibles et cliquables")]
    [SerializeField] private float minPlanetRadius = 0.9f;

    [Tooltip("Rayon d'affichage maximal pour éviter qu'une géante masque tout le système")]
    [SerializeField] private float maxPlanetRadius = 3f;

    [Header("Orbites")]
    [Tooltip("Nombre de segments pour dessiner le cercle d'orbite")]
    [SerializeField] private int orbitSegments = 64;

    [Tooltip("Couleur des lignes d'orbite")]
    [SerializeField] private Color orbitColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);

    // =========================================================
    // Runtime
    // =========================================================

    private readonly List<GameObject> _planetObjects = new List<GameObject>();

    public SolarSystemData CurrentSystem => solarSystem;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Start()
    {
        if (solarSystem == null)
        {
            Debug.LogError("[SolarSystemView] SolarSystemData manquant.");
            return;
        }

        BuildSystem();
    }

    private void OnDestroy()
    {
        // Les GOs sont enfants de ce transform → Unity les détruit automatiquement.
        _planetObjects.Clear();
    }

    // =========================================================
    // Construction du système
    // =========================================================

    private void BuildSystem()
    {
        // Étoile centrale (visuelle uniquement, pas cliquable)
        CreateStarMarker();

        if (solarSystem.orbitalSlots == null) return;

        foreach (OrbitalSlot slot in solarSystem.orbitalSlots)
        {
            if (slot?.body == null) continue;

            Vector3 pos = OrbitalPosition(slot.orbit);
            GameObject planetGO = CreatePlanetObject(slot, pos);

            DrawOrbit(slot.orbit.semiMajorAxis);

            _planetObjects.Add(planetGO);
        }
    }

    /// <summary>Calcule la position world d'un corps depuis ses paramètres orbitaux.</summary>
    private Vector3 OrbitalPosition(OrbitalParameters orbit)
    {
        // Position angulaire sur l'orbite : currentOrbitalPosition [0–1] → angle radians
        float angle = orbit.currentOrbitalPosition * 2f * Mathf.PI;

        // Distance au foyer pour une ellipse
        float a = orbit.semiMajorAxis;
        float e = orbit.eccentricity;
        float r = a * (1f - e * e) / (1f + e * Mathf.Cos(angle));

        return new Vector3(
            Mathf.Cos(angle) * r * orbitScale,
            0f,
            Mathf.Sin(angle) * r * orbitScale
        );
    }

    private GameObject CreatePlanetObject(OrbitalSlot slot, Vector3 worldPos)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = slot.body.bodyName;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = worldPos;

        // Convertit les km du rayon physique en rayon d'affichage cohérent à l'écran.
        float displayRadius = ComputeDisplayRadius(slot.body);
        go.transform.localScale = Vector3.one * (displayRadius * 2f);

        // Couleur de surface (biome dominant / atmosphère)
        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = new Material(rend.sharedMaterial);
            rend.material.color = slot.body.displayColor;
        }

        // Composant de clic
        PlanetClickHandler handler = go.AddComponent<PlanetClickHandler>();
        handler.Init(slot.body, worldPos, OnPlanetClicked);

        return go;
    }

    private float ComputeDisplayRadius(OrbitalBody body)
    {
        if (body == null || body.radius <= 0f)
            return defaultPlanetRadius;

        const float EarthRadiusKm = 6371f;
        float earthRadiusRatio = body.radius / EarthRadiusKm;
        float scaledRadius = earthRadiusRatio * planetRadiusScale;
        return Mathf.Clamp(scaledRadius, minPlanetRadius, maxPlanetRadius);
    }

    private void CreateStarMarker()
    {
        if (solarSystem.primaryStar.name == null && solarSystem.primaryStar.luminosity <= 0f) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = (!string.IsNullOrEmpty(solarSystem.primaryStar.name) ? solarSystem.primaryStar.name : "Star") + "_Star";
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one * 1.5f;

        Color starColor = StarColorFromType(solarSystem.primaryStar.spectralType);

        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = new Material(rend.sharedMaterial);
            rend.material.color = starColor;
            rend.material.EnableKeyword("_EMISSION");
            rend.material.SetColor("_EmissionColor", starColor);
        }

        // L'étoile n'est pas cliquable — retirer le collider
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    /// <summary>Retourne une couleur approximative selon la classe spectrale de l'étoile.</summary>
    private static Color StarColorFromType(StarType type)
    {
        switch (type)
        {
            case StarType.M:       return new Color(1.0f, 0.3f, 0.1f); // rouge
            case StarType.K:       return new Color(1.0f, 0.6f, 0.2f); // orange
            case StarType.G:       return new Color(1.0f, 1.0f, 0.4f); // jaune (Soleil)
            case StarType.F:       return new Color(1.0f, 1.0f, 0.8f); // blanc chaud
            case StarType.A:       return new Color(0.8f, 0.9f, 1.0f); // blanc-bleu
            case StarType.Neutron: return new Color(0.7f, 0.7f, 1.0f); // bleu pâle
            default:               return Color.white;
        }
    }

    private void DrawOrbit(float semiMajorAxisAU)
    {
        GameObject lineGO = new GameObject("Orbit_" + semiMajorAxisAU.ToString("F2"));
        lineGO.transform.SetParent(transform, false);

        LineRenderer lr = lineGO.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.positionCount = orbitSegments;
        lr.startWidth  = 0.05f;
        lr.endWidth    = 0.05f;
        lr.useWorldSpace = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = orbitColor;
        lr.endColor   = orbitColor;

        float radius = semiMajorAxisAU * orbitScale;
        for (int i = 0; i < orbitSegments; i++)
        {
            float angle = i * 2f * Mathf.PI / orbitSegments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>Permet de changer le système solaire affiché à chaud.</summary>
    public void LoadSystem(SolarSystemData data)
    {
        // Nettoyer les anciens objets
        foreach (GameObject go in _planetObjects)
            if (go != null) Destroy(go);
        _planetObjects.Clear();

        solarSystem = data;
        BuildSystem();
    }
}

// =============================================================================
// Composant auxiliaire interne — gère le clic sur un objet planète
// =============================================================================

/// <summary>
/// Composant léger ajouté dynamiquement sur chaque sphère planétaire.
/// Évite d'utiliser OnMouseDown dans SolarSystemView directement.
/// </summary>
internal class PlanetClickHandler : MonoBehaviour
{
    private OrbitalBody                             _body;
    private Vector3                                 _worldPos;
    private Action<OrbitalBody, Vector3>            _callback;

    public void Init(OrbitalBody body, Vector3 worldPos,
                     Action<OrbitalBody, Vector3> callback)
    {
        _body     = body;
        _worldPos = worldPos;
        _callback = callback;
    }

    private void OnMouseDown()
    {
        if (UIEventSystemUtility.IsPointerOverUI())
            return;

        Debug.Log($"[SolarSystemView] Clic → {_body.bodyName}");
        _callback?.Invoke(_body, _worldPos);
    }
}
