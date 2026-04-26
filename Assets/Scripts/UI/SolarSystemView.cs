using UnityEngine;
using UnityEngine.Networking;
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
public class SolarSystemView : MonoBehaviour
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
    [SerializeField] private string simulationServerUrl = "http://127.0.0.1:8080";

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

    private void StopMiniPlanetColorization()
    {
        if (_miniPlanetColorizeCoroutine == null)
            return;

        StopCoroutine(_miniPlanetColorizeCoroutine);
        _miniPlanetColorizeCoroutine = null;
    }

    private void RestartMiniPlanetColorization()
    {
        StopMiniPlanetColorization();

        if (goldbergMaterial == null || _miniMeshes.Count == 0 || !isActiveAndEnabled)
            return;

        _miniPlanetColorizeCoroutine = StartCoroutine(FetchAndColorizeMiniPlanets());
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

    // =========================================================
    // Chargement dynamique depuis le serveur
    // =========================================================

    [Serializable] private class OrbitalParamsDto  { public float semiMajorAxisAU; public float eccentricity; public float initialPhaseDeg; public int periodTicks; }
    [Serializable] private class BodyDto           { public string bodyId; public string name; public int bodyType; public string parentId; public string spectralType; public float radiusKm; public float waterLevel; public OrbitalParamsDto orbitalParams; }
    [Serializable] private class SystemDto         { public string systemId; public string name; public string rootBodyId; public string[] bodyIds; }
    [Serializable] private class BodyListWrapper   { public BodyDto[] items; }
    [Serializable] private class SystemListWrapper { public SystemDto[] items; }

    private static StarType SpectralTypeFromServer(string spectralType)
    {
        if (string.IsNullOrWhiteSpace(spectralType)) return StarType.G;
        switch (char.ToUpperInvariant(spectralType[0]))
        {
            case 'M': return StarType.M;
            case 'K': return StarType.K;
            case 'G': return StarType.G;
            case 'F': return StarType.F;
            case 'A': return StarType.A;
            default: return StarType.G;
        }
    }

    private static float StableOrbitalPhase01(string bodyId, float currentPhase)
    {
        if (currentPhase > 0.0001f) return Mathf.Repeat(currentPhase, 1f);
        if (string.IsNullOrEmpty(bodyId)) return 0f;

        unchecked
        {
            int hash = 17;
            for (int i = 0; i < bodyId.Length; i++)
                hash = hash * 31 + bodyId[i];
            int positiveHash = hash & 0x7fffffff;
            return (positiveHash % 360) / 360f;
        }
    }

    private static PlanetaryPhysics InferPhysics(BodyDto dto)
    {
        float waterLevel = Mathf.Clamp01(dto.waterLevel);
        float baseTemperature = Mathf.Lerp(-55f, 18f, waterLevel);

        return new PlanetaryPhysics
        {
            baseEquatorTemperature = baseTemperature,
            rotationSpeed = Mathf.Lerp(0.35f, 0.78f, 1f - Mathf.Abs(waterLevel - 0.5f)),
            axialTilt = Mathf.Lerp(6f, 26f, Mathf.Clamp01(dto.radiusKm / 7000f)),
        };
    }

    private static GeologicalProfile InferGeology(BodyDto dto, ServerBodyType bodyType)
    {
        float clampedWater = Mathf.Clamp01(dto.waterLevel);
        float geologicalActivity = bodyType == ServerBodyType.Moon
            ? Mathf.Lerp(0.15f, 0.55f, clampedWater)
            : Mathf.Lerp(0.2f, 0.65f, 1f - clampedWater * 0.4f);

        if (bodyType == ServerBodyType.GasGiant)
            geologicalActivity = 0f;

        return new GeologicalProfile
        {
            waterAbundance = clampedWater,
            geologicalActivity = geologicalActivity,
            mineralRichness = Mathf.Lerp(0.35f, 0.75f, Mathf.Clamp01(dto.radiusKm / 9000f)),
            magneticField = dto.radiusKm >= 3000f,
        };
    }

    private static AtmosphericComposition InferAtmosphere(BodyDto dto, ServerBodyType bodyType)
    {
        float clampedWater = Mathf.Clamp01(dto.waterLevel);

        if (bodyType == ServerBodyType.GasGiant)
        {
            return new AtmosphericComposition
            {
                density = 1f,
                n2Ratio = 0.05f,
                o2Ratio = 0f,
                co2Ratio = 0.05f,
                ch4Ratio = 0.12f,
                toxinRatio = 0.08f,
            };
        }

        if (bodyType == ServerBodyType.Moon)
        {
            return clampedWater >= 0.35f
                ? AtmosphericComposition.IcyMoon
                : new AtmosphericComposition
                {
                    density = 0.03f,
                    n2Ratio = 0.82f,
                    o2Ratio = 0f,
                    co2Ratio = 0.08f,
                    ch4Ratio = 0.02f,
                    toxinRatio = 0f,
                };
        }

        if (clampedWater >= 0.45f)
        {
            AtmosphericComposition atmosphere = AtmosphericComposition.EarthLike;
            atmosphere.density = Mathf.Lerp(0.55f, 0.92f, clampedWater);
            return atmosphere;
        }

        if (clampedWater <= 0.08f)
            return AtmosphericComposition.Mars;

        return new AtmosphericComposition
        {
            density = Mathf.Lerp(0.15f, 0.55f, clampedWater),
            n2Ratio = 0.62f,
            o2Ratio = Mathf.Lerp(0.01f, 0.12f, clampedWater),
            co2Ratio = Mathf.Lerp(0.28f, 0.08f, clampedWater),
            ch4Ratio = 0.01f,
            toxinRatio = Mathf.Lerp(0.08f, 0.01f, clampedWater),
        };
    }

    private static void PopulateOrbitalBodyProfile(OrbitalBody body, BodyDto dto, ServerBodyType bodyType)
    {
        if (body == null || dto == null)
            return;

        body.physics = InferPhysics(dto);
        body.geology = InferGeology(dto, bodyType);
        body.atmosphere = InferAtmosphere(dto, bodyType);
    }

    private static OrbitalBody BuildOrbitalBodyFromDto(BodyDto dto)
    {
        ServerBodyType bodyType = (ServerBodyType)dto.bodyType;

        if (bodyType == ServerBodyType.GasGiant)
        {
            GasGiant gasGiant = ScriptableObject.CreateInstance<GasGiant>();
            gasGiant.bodyName = dto.name;
            gasGiant.radius = dto.radiusKm;
            gasGiant.displayColor = new Color(0.8f, 0.6f, 0.3f);
            PopulateOrbitalBodyProfile(gasGiant, dto, bodyType);
            return gasGiant;
        }

        if (bodyType == ServerBodyType.Moon)
        {
            Moon moon = ScriptableObject.CreateInstance<Moon>();
            moon.bodyName = dto.name;
            moon.radius = dto.radiusKm;
            moon.displayColor = Color.gray;
            moon.moonType = dto.waterLevel >= 0.35f ? MoonType.Oceanic : MoonType.Rocky;
            PopulateOrbitalBodyProfile(moon, dto, bodyType);
            return moon;
        }

        Planet planet = ScriptableObject.CreateInstance<Planet>();
        planet.bodyName = dto.name;
        planet.radius = dto.radiusKm;
        planet.displayColor = WaterLevelToColor(dto.waterLevel);
        planet.planetType = dto.waterLevel >= 0.75f
            ? PlanetType.OceanWorld
            : dto.waterLevel <= 0.08f
                ? PlanetType.Desert
                : PlanetType.Rocky;
        PopulateOrbitalBodyProfile(planet, dto, bodyType);
        return planet;
    }

    private void RegisterDebugInfo(OrbitalBody body, BodyDto dto, string parentName)
    {
        if (body == null || dto == null)
            return;

        _debugInfoByBody[body] = new BodyDebugInfo
        {
            typeName = ((ServerBodyType)dto.bodyType).ToString(),
            parentName = parentName,
            semiMajorAxisAu = dto.orbitalParams != null ? dto.orbitalParams.semiMajorAxisAU : 0f,
            orbitalPeriodDays = dto.orbitalParams != null ? Mathf.Max(1f, dto.orbitalParams.periodTicks) : 0f,
        };
    }

    private void LogSystemValidation(SolarSystemData data)
    {
        if (data == null || !logSystemValidationOnLoad)
            return;

        Debug.Log($"[SolarSystemView] Validation système: {data.BuildDebugSummary()}");
        SolarSystemData.ValidationIssue[] issues = data.ValidateHierarchy();
        foreach (SolarSystemData.ValidationIssue issue in issues)
        {
            if (string.Equals(issue.severity, "error", StringComparison.OrdinalIgnoreCase))
                Debug.LogError("[SolarSystemView] Validation système: " + issue.message);
            else
                Debug.LogWarning("[SolarSystemView] Validation système: " + issue.message);
        }
    }

    /// <summary>
    /// Fetch le serveur, reconstruit un SolarSystemData temporaire en mémoire
    /// et recharge la vue. Appeler depuis une coroutine (StartCoroutine).
    /// </summary>
    public IEnumerator LoadFromServer(string serverUrl, float timeoutSeconds = 2f)
    {
        int requestGeneration = ++_serverLoadGeneration;
        string baseUrl = serverUrl.TrimEnd('/');
        int timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));
        const int maxAttempts = 3;
        const float retryDelaySeconds = 0.25f;

        SystemDto activeSystem = null;
        BodyDto[] bodies = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            activeSystem = null;
            bodies = null;

            // 1. Fetch le système actif depuis /galaxy/systems
            using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/galaxy/systems"))
            {
                req.timeout = timeout;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
                    SystemListWrapper list = JsonUtility.FromJson<SystemListWrapper>(wrapped);
                    if (list?.items != null && list.items.Length > 0)
                        activeSystem = list.items[0]; // premier système = système actif
                }
            }

            if (requestGeneration != _serverLoadGeneration || !isActiveAndEnabled || !gameObject.activeInHierarchy)
                yield break;

            // 2. Fetch tous les corps depuis /bodies
            using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/bodies"))
            {
                req.timeout = timeout;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
                    BodyListWrapper list = JsonUtility.FromJson<BodyListWrapper>(wrapped);
                    bodies = list?.items;
                }
            }

            if (requestGeneration != _serverLoadGeneration || !isActiveAndEnabled || !gameObject.activeInHierarchy)
                yield break;

            if (bodies != null && bodies.Length > 0)
                break;

            if (attempt < maxAttempts)
                yield return new WaitForSeconds(retryDelaySeconds);
        }

        if (bodies == null || bodies.Length == 0)
        {
            Debug.Log($"[SolarSystemView] Chargement serveur ignoré: aucun corps reçu après {maxAttempts} tentatives.");
            yield break;
        }

        // Filtrer les corps appartenant au système actif uniquement
        if (activeSystem?.bodyIds != null && activeSystem.bodyIds.Length > 0)
        {
            var idSet = new HashSet<string>(activeSystem.bodyIds);
            var filtered = new List<BodyDto>();
            foreach (var b in bodies)
                if (idSet.Contains(b.bodyId)) filtered.Add(b);
            bodies = filtered.ToArray();
        }

        if (bodies.Length == 0)
        {
            Debug.LogWarning("[SolarSystemView] Aucun corps dans le système actif.");
            yield break;
        }

        // 3. Construire SolarSystemData en mémoire
        SolarSystemData data = ScriptableObject.CreateInstance<SolarSystemData>();
        data.systemName = activeSystem?.name ?? "Système";

        // Étoile racine
        BodyDto rootBody = activeSystem != null
            ? System.Array.Find(bodies, b => b.bodyId == activeSystem.rootBodyId)
            : System.Array.Find(bodies, b => b.bodyType == (int)ServerBodyType.Star);
        if (rootBody != null)
        {
            StarBody star = ScriptableObject.CreateInstance<StarBody>();
            star.spectralType = SpectralTypeFromServer(rootBody.spectralType);
            star.ApplySpectralDefaults();
            star.bodyName = rootBody.name;
            star.radius = rootBody.radiusKm;
            data.primaryStar = star;
        }

        // Planètes et lunes: reconstruire la hiérarchie parent → enfant au lieu de tout aplatir.
        var slotsByBodyId = new Dictionary<string, OrbitalSlot>();
        foreach (BodyDto b in bodies)
        {
            if (b.bodyType == (int)ServerBodyType.Star) continue;

            OrbitalBody body = BuildOrbitalBodyFromDto(b);

            OrbitalParameters orbit = new OrbitalParameters();
            if (b.orbitalParams != null)
            {
                orbit.semiMajorAxis = b.orbitalParams.semiMajorAxisAU;
                orbit.eccentricity  = b.orbitalParams.eccentricity;
                orbit.currentOrbitalPosition = StableOrbitalPhase01(b.bodyId, b.orbitalParams.initialPhaseDeg / 360f);
                orbit.orbitalPeriodDays = Mathf.Max(1f, b.orbitalParams.periodTicks);
            }
            else
            {
                orbit.currentOrbitalPosition = StableOrbitalPhase01(b.bodyId, 0f);
            }

            slotsByBodyId[b.bodyId] = new OrbitalSlot { body = body, orbit = orbit, moons = new OrbitalSlot[0] };
        }

        var topLevelSlots = new List<OrbitalSlot>();
        foreach (BodyDto b in bodies)
        {
            if (b.bodyType == (int)ServerBodyType.Star) continue;
            if (!slotsByBodyId.TryGetValue(b.bodyId, out OrbitalSlot slot)) continue;

            if (!string.IsNullOrEmpty(b.parentId) && slotsByBodyId.TryGetValue(b.parentId, out OrbitalSlot parentSlot))
            {
                var moonSlots = new List<OrbitalSlot>(parentSlot.moons ?? new OrbitalSlot[0]) { slot };
                moonSlots.Sort((left, right) => left.orbit.semiMajorAxis.CompareTo(right.orbit.semiMajorAxis));
                parentSlot.moons = moonSlots.ToArray();
                RegisterDebugInfo(slot.body, b, parentSlot.body != null ? parentSlot.body.bodyName : null);
            }
            else
            {
                topLevelSlots.Add(slot);
                RegisterDebugInfo(slot.body, b, null);
            }
        }

        topLevelSlots.Sort((left, right) => left.orbit.semiMajorAxis.CompareTo(right.orbit.semiMajorAxis));
        data.orbitalSlots = topLevelSlots.ToArray();

        LoadSystem(data);
        LogSystemValidation(data);
        Debug.Log($"[SolarSystemView] Système chargé depuis serveur : {data.systemName} ({topLevelSlots.Count} corps top-level)");
    }

    // =========================================================
    // Mini Goldberg — colorisation biome depuis le serveur
    // =========================================================

    [Serializable] private struct MiniBodyEntry { public string bodyId; public string name; }
    [Serializable] private struct MiniBodyList  { public MiniBodyEntry[] items; }
    [Serializable] private struct MiniTileList  { public GoldbergTileState[] items; }

    private IEnumerator FetchAndColorizeMiniPlanets()
    {
        int revision = _miniMeshRevision;
        string baseUrl = simulationServerUrl.TrimEnd('/');
        var meshEntries = new List<KeyValuePair<string, GoldbergSphereGenerator.GoldbergMeshData>>(_miniMeshes);

        // 1) Résoudre name → bodyId
        Dictionary<string, string> nameToId = new Dictionary<string, string>();
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/bodies"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (revision != _miniMeshRevision || !isActiveAndEnabled)
                yield break;

            if (req.result == UnityWebRequest.Result.Success)
            {
                MiniBodyList list = JsonUtility.FromJson<MiniBodyList>("{\"items\":" + req.downloadHandler.text + "}");
                if (list.items != null)
                    foreach (MiniBodyEntry e in list.items)
                        if (!string.IsNullOrEmpty(e.name) && !string.IsNullOrEmpty(e.bodyId))
                            nameToId[e.name] = e.bodyId;
            }
        }

        if (nameToId.Count == 0)
        {
            Debug.LogWarning("[SolarSystemView] Aucun bodyId résolu depuis le serveur.");
            yield break;
        }

        Dictionary<TerrainType, Color> colorByType = terrainPalette != null
            ? terrainPalette.ToDictionary()
            : TerrainColorPalette.DefaultDictionary();

        // 2) Fetcher les tuiles de chaque planète et coloriser son mini-mesh
        foreach (KeyValuePair<string, GoldbergSphereGenerator.GoldbergMeshData> kv in meshEntries)
        {
            if (revision != _miniMeshRevision || !isActiveAndEnabled)
                yield break;

            string bodyName = kv.Key;
            GoldbergSphereGenerator.GoldbergMeshData md = kv.Value;

            if (!nameToId.TryGetValue(bodyName, out string bodyId)) continue;

            var allTiles = new List<GoldbergTileState>();
            int page = 0;
            const int pageSize = 200;

            while (true)
            {
                string url = $"{baseUrl}/bodies/{bodyId}/tiles?page={page}&size={pageSize}";
                using UnityWebRequest req = UnityWebRequest.Get(url);
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (revision != _miniMeshRevision || !isActiveAndEnabled)
                    yield break;

                if (req.result != UnityWebRequest.Result.Success) break;

                MiniTileList batch;
                try   { batch = JsonUtility.FromJson<MiniTileList>("{\"items\":" + req.downloadHandler.text + "}"); }
                catch { break; }
                if (batch.items == null || batch.items.Length == 0) break;
                allTiles.AddRange(batch.items);
                if (batch.items.Length < pageSize) break;
                page++;
            }

            if (allTiles.Count == 0) continue;

            if (!_miniMeshes.TryGetValue(bodyName, out GoldbergSphereGenerator.GoldbergMeshData currentMesh) || !ReferenceEquals(currentMesh.mesh, md.mesh))
                continue;

            GoldbergFaceColorizer.ColorizeFromServerTiles(currentMesh.faces, allTiles.ToArray(), colorByType);
            GoldbergSphereGenerator.ApplyFaceColors(currentMesh.mesh, currentMesh.faces, currentMesh.vertexFaceId);

            Debug.Log($"[SolarSystemView] {bodyName} : {allTiles.Count} tuiles → {currentMesh.faces.Length} faces colorisées.");

            // Étale légèrement le travail sur plusieurs frames pour éviter les pics au lancement.
            yield return null;
        }

        if (revision == _miniMeshRevision)
            _miniPlanetColorizeCoroutine = null;
    }

    // BuildDefaultColorByType() supprimé — remplacer par TerrainColorPalette.DefaultDictionary()

    private static Color WaterLevelToColor(float w)
    {
        if (w > 0.6f) return new Color(0.2f, 0.4f, 0.9f);   // océan
        if (w > 0.3f) return new Color(0.3f, 0.7f, 0.4f);   // côtier
        if (w > 0.05f) return new Color(0.7f, 0.6f, 0.3f);  // aride
        return new Color(0.6f, 0.5f, 0.4f);                  // rocheux/désert
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
