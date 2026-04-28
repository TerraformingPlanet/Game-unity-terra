using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.InputSystem;

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
public partial class SolarSystemView : MonoBehaviour
{
    private enum ServerBodyType
    {
        Star = 0,
        Planet = 1,
        Moon = 2,
        Asteroid = 3,
        GasGiant = 4,
        SpaceStation = 5,
    }

    // =========================================================
    // Events
    // =========================================================

    /// <summary>
    /// Déclenché quand l'utilisateur clique sur une planète.
    /// body      : le OrbitalBody de la planète cliquée.
    /// worldPos  : position world de la sphère (centre d'orbite pour la caméra).
    /// </summary>
    public event Action<OrbitalBody, Vector3> OnPlanetClicked;

    /// <summary>Déclenché quand l'utilisateur clique sur l'étoile primaire pour recentrer la caméra.</summary>
    public event Action<Vector3> OnPrimaryStarClicked;

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

    [Header("Mini Goldberg (Biomes)")]
    [Tooltip("Palette de couleurs de terrain partagée.")]
    [SerializeField] private TerrainColorPalette terrainPalette;

    [Tooltip("URL du serveur pour fetcher les tuiles biome des mini-planètes.")]
    [SerializeField] private GameConfig config;
    private string SimUrl => config != null ? config.simulationServerUrl : "http://127.0.0.1:8080";

    [Tooltip("Subdivisions de base du mini mesh Goldberg. Coût réel ~= 10*N^2+2 tuiles/faces.")]
    [SerializeField] private int miniGoldbergDivisions = 4;

    [Tooltip("Subdivisions utilisées pour les plus gros astres visibles en vue système.")]
    [SerializeField] private int miniGoldbergHighDivisions = 5;

    [Tooltip("Au-dessus de ce rayon visuel, les gros astres passent au niveau de détail élevé.")]
    [SerializeField] private float miniGoldbergHighDetailRadius = 2.0f;

    [Tooltip("Matériau vertex-color (Terraformation/HexVertexColor). Si null, utilise la sphère Unity standard.")]
    [SerializeField] private Material goldbergMaterial;

    [Tooltip("Nombre maximum de corps affichés en mini Goldberg dans la vue système.")]
    [SerializeField] private int maxMiniGoldbergBodies = 8;

    [Tooltip("Rayon visuel minimum pour utiliser un mini Goldberg plutôt qu'une sphère simple.")]
    [SerializeField] private float miniGoldbergMinDisplayRadius = 1.1f;

    [Header("Lisibilité système")]
    [Tooltip("Rayon visuel minimum des lunes pour éviter qu'elles ne paraissent aussi grosses que des planètes.")]
    [SerializeField] private float minMoonRadius = 0.35f;

    [Tooltip("Rayon visuel maximum des lunes.")]
    [SerializeField] private float maxMoonRadius = 0.9f;

    [Tooltip("Multiplicateur visuel appliqué aux orbites de lunes pour les rendre lisibles en vue système.")]
    [SerializeField] private float moonOrbitScaleMultiplier = 120f;

    [Tooltip("Rayon visuel de l'étoile primaire dans la vue système.")]
    [SerializeField] private float starDisplayRadius = 2.4f;

    [Header("Debug système")]
    [Tooltip("Affiche des labels détaillés avec type, parent et orbite.")]
    [SerializeField] private bool showDetailedDebugLabels;

    [Tooltip("Touche de bascule des labels détaillés en vue système.")]
    [SerializeField] private Key debugLabelToggleKey = Key.F9;

    [Tooltip("Log automatiquement un résumé de validation après chargement serveur.")]
    [SerializeField] private bool logSystemValidationOnLoad = true;

    // =========================================================
    // Runtime
    // =========================================================

    private readonly List<GameObject> _planetObjects = new List<GameObject>();

    // bodyName → GoldbergMeshData pour les planètes avec mini-mesh
    private readonly Dictionary<string, GoldbergSphereGenerator.GoldbergMeshData> _miniMeshes
        = new Dictionary<string, GoldbergSphereGenerator.GoldbergMeshData>();

    private readonly Dictionary<OrbitalBody, BodyDebugInfo> _debugInfoByBody
        = new Dictionary<OrbitalBody, BodyDebugInfo>();

    private readonly List<TextMeshPro> _debugLabels = new List<TextMeshPro>();
    private Coroutine _miniPlanetColorizeCoroutine;
    private int _miniMeshRevision;
    private int _serverLoadGeneration;

    public SolarSystemData CurrentSystem => solarSystem;

    private sealed class BodyDebugInfo
    {
        public string typeName;
        public string parentName;
        public float semiMajorAxisAu;
        public float orbitalPeriodDays;
    }

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
        RestartMiniPlanetColorization();
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current[debugLabelToggleKey].wasPressedThisFrame)
            return;

        showDetailedDebugLabels = !showDetailedDebugLabels;
        RefreshDebugLabels();
        Debug.Log($"[SolarSystemView] Labels debug détaillés: {(showDetailedDebugLabels ? "ON" : "OFF")}");
    }

    private void OnDestroy()
    {
        StopMiniPlanetColorization();

        // Les GOs sont enfants de ce transform → Unity les détruit automatiquement.
        _planetObjects.Clear();
        _debugInfoByBody.Clear();
        _debugLabels.Clear();
    }

    // =========================================================
    // Construction du système
    // =========================================================

    private void BuildSystem()
    {
        _debugLabels.Clear();

        // Étoile centrale (visuelle uniquement, pas cliquable)
        // Étoile centrale (visuelle + cliquable pour recentrer la caméra)
        CreateStarMarker();

        if (solarSystem.orbitalSlots == null) return;

        foreach (OrbitalSlot slot in solarSystem.orbitalSlots)
        {
            if (slot?.body == null) continue;
            BuildSlotRecursive(slot, Vector3.zero, false, 0);
        }
    }

    private void BuildSlotRecursive(OrbitalSlot slot, Vector3 parentWorldPos, bool isMoonOrbit, int depth)
    {
        if (slot?.body == null) return;

        Vector3 localOffset = OrbitalPosition(slot.orbit, isMoonOrbit);
        Vector3 worldPos = parentWorldPos + localOffset;
        GameObject bodyGO = CreatePlanetObject(slot, worldPos, depth);
        _planetObjects.Add(bodyGO);

        DrawOrbit(slot.orbit.semiMajorAxis, parentWorldPos, isMoonOrbit);

        if (slot.moons == null) return;
        foreach (OrbitalSlot moonSlot in slot.moons)
            BuildSlotRecursive(moonSlot, worldPos, true, depth + 1);
    }

    /// <summary>Calcule la position world d'un corps depuis ses paramètres orbitaux.</summary>
    private Vector3 OrbitalPosition(OrbitalParameters orbit, bool isMoonOrbit)
    {
        // Position angulaire sur l'orbite : currentOrbitalPosition [0–1] → angle radians
        float angle = orbit.currentOrbitalPosition * 2f * Mathf.PI;

        // Distance au foyer pour une ellipse
        float a = orbit.semiMajorAxis;
        float e = orbit.eccentricity;
        float r = a * (1f - e * e) / (1f + e * Mathf.Cos(angle));

        float effectiveOrbitScale = orbitScale * (isMoonOrbit ? moonOrbitScaleMultiplier : 1f);

        return new Vector3(
            Mathf.Cos(angle) * r * effectiveOrbitScale,
            0f,
            Mathf.Sin(angle) * r * effectiveOrbitScale
        );
    }

    private GameObject CreatePlanetObject(OrbitalSlot slot, Vector3 worldPos, int depth)
    {
        float displayRadius = ComputeDisplayRadius(slot.body, depth);
        GameObject go;
        int goldbergDivisions = ResolveMiniGoldbergDivisions(displayRadius, slot.body);

        bool useMiniGoldberg = goldbergMaterial != null
                               && _miniMeshes.Count < maxMiniGoldbergBodies
                               && goldbergDivisions > 0;

        if (useMiniGoldberg)
        {
            // Mini mesh Goldberg low-poly colorisé par biome
            go = new GameObject(slot.body.bodyName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = worldPos;
            // Le mesh Goldberg est généré avec un rayon monde fixe = VisualRadius (10 unités).
            // On le renormalise pour obtenir le même rayon visuel cible que l'ancienne sphere Unity.
            float goldbergScale = displayRadius / GoldbergSphereGenerator.VisualRadius;
            go.transform.localScale = Vector3.one * goldbergScale;

            MeshFilter   mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            MeshCollider mc = go.AddComponent<MeshCollider>();

            GoldbergSphereGenerator.GoldbergMeshData md =
                GoldbergSphereGenerator.GenerateWithDivisions(goldbergDivisions);

            // Couleur uniforme displayColor en fallback immédiat (remplacée async par biomes serveur)
            for (int i = 0; i < md.faces.Length; i++)
                md.faces[i].color = slot.body.displayColor;
            GoldbergSphereGenerator.ApplyFaceColors(md.mesh, md.faces, md.vertexFaceId);

            mf.sharedMesh = md.mesh;
            mr.material   = goldbergMaterial;
            mc.sharedMesh = md.mesh;

            _miniMeshes[slot.body.bodyName] = md;

            Debug.Log($"[SolarSystemView] Mini Goldberg {slot.body.bodyName} | radius={displayRadius:F2} | div={goldbergDivisions} | faces={md.faces.Length}");
        }
        else
        {
            // Sphère Unity standard (fallback si matériau non assigné)
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = slot.body.bodyName;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = worldPos;
            go.transform.localScale    = Vector3.one * (displayRadius * 2f);

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(rend.sharedMaterial);
                rend.material.color = slot.body.displayColor;
            }

            Debug.Log($"[SolarSystemView] Sphere fallback {slot.body.bodyName} | radius={displayRadius:F2}");
        }

        // Composant de clic
        PlanetClickHandler handler = go.AddComponent<PlanetClickHandler>();
        handler.Init(slot.body, worldPos, OnPlanetClicked);

        // Label débug
        CreateDebugLabel(slot.body, go.transform.position, displayRadius + 1.2f);

        return go;
    }

    private float ComputeDisplayRadius(OrbitalBody body, int depth)
    {
        if (body == null || body.radius <= 0f)
            return defaultPlanetRadius;

        const float EarthRadiusKm = 6371f;
        float earthRadiusRatio = body.radius / EarthRadiusKm;
        float scaledRadius = earthRadiusRatio * planetRadiusScale;

        if (body is Moon || depth > 0)
            return Mathf.Clamp(scaledRadius * 0.65f, minMoonRadius, maxMoonRadius);

        if (body is GasGiant)
            return Mathf.Clamp(scaledRadius, minPlanetRadius, maxPlanetRadius);

        return Mathf.Clamp(scaledRadius, minPlanetRadius, maxPlanetRadius);
    }

    private int ResolveMiniGoldbergDivisions(float displayRadius, OrbitalBody body)
    {
        if (goldbergMaterial == null) return 0;
        if (body is Moon) return 0;
        if (displayRadius < miniGoldbergMinDisplayRadius) return 0;

        int baseDivisions = Mathf.Clamp(miniGoldbergDivisions, 2, 15);
        int highDivisions = Mathf.Clamp(Mathf.Max(baseDivisions, miniGoldbergHighDivisions), 2, 15);

        return displayRadius >= miniGoldbergHighDetailRadius ? highDivisions : baseDivisions;
    }

    private void CreateStarMarker()
    {
        if (solarSystem?.primaryStar == null) return;
        if (string.IsNullOrEmpty(solarSystem.primaryStar.bodyName) && solarSystem.primaryStar.luminosity <= 0f) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        string primaryStarName = !string.IsNullOrEmpty(solarSystem.primaryStar.bodyName)
            ? solarSystem.primaryStar.bodyName
            : "Star";

        go.name = primaryStarName + "_Star";
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one * (starDisplayRadius * 2f);

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
        // L'étoile est cliquable pour recentrer la caméra sur le pivot du système.
        StarClickHandler clickHandler = go.AddComponent<StarClickHandler>();
        clickHandler.Init(go.transform.position, OnPrimaryStarClicked);

        // Label débug
        string starName = !string.IsNullOrEmpty(solarSystem.primaryStar.bodyName)
            ? solarSystem.primaryStar.bodyName
            : "Étoile";
        CreateStarDebugLabel(starName);
        _planetObjects.Add(go);
    }

    private void CreateDebugLabel(OrbitalBody body, Vector3 worldPos, float offsetY)
    {
        string labelName = body != null ? body.bodyName : "Unknown";
        var labelGO = new GameObject("Label_" + labelName);
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.position = worldPos + Vector3.up * offsetY;
        labelGO.AddComponent<BillboardLabel>();
        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text         = BuildDebugLabelText(body);
        tmp.fontSize     = 2.5f;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.color        = Color.white;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;
        _debugLabels.Add(tmp);
        _planetObjects.Add(labelGO);
    }

    private void CreateStarDebugLabel(string starName)
    {
        var labelGO = new GameObject("Label_" + starName);
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.position = Vector3.up * (starDisplayRadius + 1.2f);
        labelGO.AddComponent<BillboardLabel>();
        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text = showDetailedDebugLabels
            ? $"{starName}\nStar | centre du système\n{solarSystem.primaryStar.spectralType} | lum={solarSystem.primaryStar.luminosity:F2}"
            : starName;
        tmp.fontSize = 2.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;
        _debugLabels.Add(tmp);
        _planetObjects.Add(labelGO);
    }

    private string BuildDebugLabelText(OrbitalBody body)
    {
        if (body == null)
            return "Corps inconnu";

        if (!showDetailedDebugLabels)
            return body.bodyName;

        if (!_debugInfoByBody.TryGetValue(body, out BodyDebugInfo info) || info == null)
            return body.bodyName;

        string parentName = string.IsNullOrEmpty(info.parentName) ? "Soleil" : info.parentName;
        return $"{body.bodyName}\n{info.typeName} | parent={parentName}\n{info.semiMajorAxisAu:F3} AU | {info.orbitalPeriodDays:F0} j";
    }

    private void RefreshDebugLabels()
    {
        foreach (TextMeshPro label in _debugLabels)
        {
            if (label == null) continue;

            string objectName = label.gameObject.name;
            if (!objectName.StartsWith("Label_", StringComparison.Ordinal))
                continue;

            string bodyName = objectName.Substring("Label_".Length);
            if (solarSystem?.primaryStar != null && bodyName == solarSystem.primaryStar.bodyName)
            {
                label.text = showDetailedDebugLabels
                    ? $"{bodyName}\nStar | centre du système\n{solarSystem.primaryStar.spectralType} | lum={solarSystem.primaryStar.luminosity:F2}"
                    : bodyName;
                continue;
            }

            OrbitalBody match = null;
            foreach (OrbitalBody body in _debugInfoByBody.Keys)
            {
                if (body != null && body.bodyName == bodyName)
                {
                    match = body;
                    break;
                }
            }

            label.text = BuildDebugLabelText(match);
        }
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

    private void DrawOrbit(float semiMajorAxisAU, Vector3 center, bool isMoonOrbit)
    {
        GameObject lineGO = new GameObject("Orbit_" + semiMajorAxisAU.ToString("F2"));
        lineGO.transform.SetParent(transform, false);
        lineGO.transform.localPosition = center;

        LineRenderer lr = lineGO.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.positionCount = orbitSegments;
        lr.startWidth  = isMoonOrbit ? 0.025f : 0.05f;
        lr.endWidth    = isMoonOrbit ? 0.025f : 0.05f;
        lr.useWorldSpace = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        Color effectiveOrbitColor = isMoonOrbit
            ? new Color(orbitColor.r, orbitColor.g, orbitColor.b, orbitColor.a * 0.6f)
            : orbitColor;
        lr.startColor = effectiveOrbitColor;
        lr.endColor   = effectiveOrbitColor;

        float effectiveOrbitScale = orbitScale * (isMoonOrbit ? moonOrbitScaleMultiplier : 1f);
        float radius = semiMajorAxisAU * effectiveOrbitScale;
        for (int i = 0; i < orbitSegments; i++)
        {
            float angle = i * 2f * Mathf.PI / orbitSegments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        _planetObjects.Add(lineGO);
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>Permet de changer le système solaire affiché à chaud.</summary>
    public void LoadSystem(SolarSystemData data)
    {
        _miniMeshRevision++;
        StopMiniPlanetColorization();

        // Nettoyer les anciens objets
        foreach (GameObject go in _planetObjects)
            if (go != null) Destroy(go);
        _planetObjects.Clear();
        _miniMeshes.Clear();
        _debugInfoByBody.Clear();
        _debugLabels.Clear();

        solarSystem = data;
        BuildSystem();
        RestartMiniPlanetColorization();
    }

}

// =============================================================================
// =============================================================================
// Composant auxiliaire interne — gère le clic sur un objet planète
// =============================================================================

/// <summary>
/// Composant léger ajouté dynamiquement sur chaque sphère planétaire.
/// Évite d'utiliser OnMouseDown dans SolarSystemView directement.
/// </summary>
internal class StarClickHandler : MonoBehaviour
{
    private Vector3 _worldPos;
    private Action<Vector3> _callback;

    public void Init(Vector3 worldPos, Action<Vector3> callback)
    {
        _worldPos = worldPos;
        _callback = callback;
    }

    private void OnMouseDown()
    {
        if (UIEventSystemUtility.IsPointerOverUI())
            return;

        Debug.Log("[SolarSystemView] Clic étoile primaire → recentrage caméra");
        _callback?.Invoke(_worldPos);
    }
}

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
