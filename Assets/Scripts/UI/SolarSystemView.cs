using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

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

    [Header("Mini Goldberg (Biomes)")]
    [Tooltip("URL du serveur pour fetcher les tuiles biome des mini-planètes.")]
    [SerializeField] private string simulationServerUrl = "http://127.0.0.1:8080";

    [Tooltip("Subdivisions du mini mesh Goldberg. 2 = 12 faces, 3 = 32 faces.")]
    [SerializeField] private int miniGoldbergDivisions = 2;

    [Tooltip("Matériau vertex-color (Terraformation/HexVertexColor). Si null, utilise la sphère Unity standard.")]
    [SerializeField] private Material goldbergMaterial;

    [Tooltip("Nombre maximum de corps affichés en mini Goldberg dans la vue système.")]
    [SerializeField] private int maxMiniGoldbergBodies = 8;

    [Tooltip("Rayon visuel minimum pour utiliser un mini Goldberg plutôt qu'une sphère simple.")]
    [SerializeField] private float miniGoldbergMinDisplayRadius = 1.1f;

    // =========================================================
    // Runtime
    // =========================================================

    private readonly List<GameObject> _planetObjects = new List<GameObject>();

    // bodyName → GoldbergMeshData pour les planètes avec mini-mesh
    private readonly Dictionary<string, GoldbergSphereGenerator.GoldbergMeshData> _miniMeshes
        = new Dictionary<string, GoldbergSphereGenerator.GoldbergMeshData>();

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

        if (goldbergMaterial != null && _miniMeshes.Count > 0)
            StartCoroutine(FetchAndColorizeMiniPlanets());
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
        float displayRadius = ComputeDisplayRadius(slot.body);
        GameObject go;

        bool useMiniGoldberg = goldbergMaterial != null
                               && !(slot.body is Moon)
                               && _miniMeshes.Count < maxMiniGoldbergBodies
                               && displayRadius >= miniGoldbergMinDisplayRadius;

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
                GoldbergSphereGenerator.GenerateWithDivisions(miniGoldbergDivisions);

            // Couleur uniforme displayColor en fallback immédiat (remplacée async par biomes serveur)
            for (int i = 0; i < md.faces.Length; i++)
                md.faces[i].color = slot.body.displayColor;
            GoldbergSphereGenerator.ApplyFaceColors(md.mesh, md.faces, md.vertexFaceId);

            mf.sharedMesh = md.mesh;
            mr.material   = goldbergMaterial;
            mc.sharedMesh = md.mesh;

            _miniMeshes[slot.body.bodyName] = md;
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
        }

        // Composant de clic
        PlanetClickHandler handler = go.AddComponent<PlanetClickHandler>();
        handler.Init(slot.body, worldPos, OnPlanetClicked);

        // Label débug
        CreateDebugLabel(slot.body.bodyName, go.transform.position, displayRadius + 1.2f);

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

        // Label débug
        string starName = !string.IsNullOrEmpty(solarSystem.primaryStar.name)
            ? solarSystem.primaryStar.name
            : "Étoile";
        CreateDebugLabel(starName, Vector3.zero, 1.5f + 1.2f);
    }

    private void CreateDebugLabel(string text, Vector3 worldPos, float offsetY)
    {
        var labelGO = new GameObject("Label_" + text);
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.position = worldPos + Vector3.up * offsetY;
        labelGO.AddComponent<BillboardLabel>();
        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text         = text;
        tmp.fontSize     = 2.5f;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.color        = Color.white;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;
        _planetObjects.Add(labelGO);
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
        _miniMeshes.Clear();

        solarSystem = data;
        BuildSystem();

        if (goldbergMaterial != null && _miniMeshes.Count > 0)
            StartCoroutine(FetchAndColorizeMiniPlanets());
    }

    // =========================================================
    // Chargement dynamique depuis le serveur
    // =========================================================

    [Serializable] private class OrbitalParamsDto  { public float semiMajorAxisAU; public float eccentricity; public float initialPhaseDeg; public int periodTicks; }
    [Serializable] private class BodyDto           { public string bodyId; public string name; public string bodyType; public float radiusKm; public float waterLevel; public OrbitalParamsDto orbitalParams; }
    [Serializable] private class SystemDto         { public string systemId; public string name; public string rootBodyId; public string[] bodyIds; }
    [Serializable] private class BodyListWrapper   { public BodyDto[] items; }
    [Serializable] private class SystemListWrapper { public SystemDto[] items; }

    /// <summary>
    /// Fetch le serveur, reconstruit un SolarSystemData temporaire en mémoire
    /// et recharge la vue. Appeler depuis une coroutine (StartCoroutine).
    /// </summary>
    public IEnumerator LoadFromServer(string serverUrl, float timeoutSeconds = 2f)
    {
        string baseUrl = serverUrl.TrimEnd('/');
        int timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));

        // 1. Fetch le système actif depuis /galaxy/systems
        SystemDto activeSystem = null;
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

        // 2. Fetch tous les corps depuis /bodies
        BodyDto[] bodies = null;
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

        if (bodies == null || bodies.Length == 0)
        {
            Debug.LogWarning("[SolarSystemView] Aucun corps reçu du serveur.");
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
            : System.Array.Find(bodies, b => b.bodyType == "Star");
        if (rootBody != null)
        {
            StarBody star = ScriptableObject.CreateInstance<StarBody>();
            star.bodyName  = rootBody.name;
            star.radius    = rootBody.radiusKm;
            star.luminosity = 1f;
            data.primaryStar = star;
        }

        // Planètes et lunes (tous les corps non-étoiles)
        var slots = new List<OrbitalSlot>();
        foreach (BodyDto b in bodies)
        {
            if (b.bodyType == "Star") continue;

            OrbitalBody body;
            if (b.bodyType == "GasGiant")
            {
                GasGiant gg = ScriptableObject.CreateInstance<GasGiant>();
                gg.bodyName = b.name;
                gg.radius   = b.radiusKm;
                gg.displayColor = new Color(0.8f, 0.6f, 0.3f); // ocre
                body = gg;
            }
            else if (b.bodyType == "Moon")
            {
                Moon m = ScriptableObject.CreateInstance<Moon>();
                m.bodyName = b.name;
                m.radius   = b.radiusKm;
                m.displayColor = Color.gray;
                body = m;
            }
            else
            {
                Planet p = ScriptableObject.CreateInstance<Planet>();
                p.bodyName = b.name;
                p.radius   = b.radiusKm;
                p.displayColor = WaterLevelToColor(b.waterLevel);
                body = p;
            }

            OrbitalParameters orbit = new OrbitalParameters();
            if (b.orbitalParams != null)
            {
                orbit.semiMajorAxis = b.orbitalParams.semiMajorAxisAU;
                orbit.eccentricity  = b.orbitalParams.eccentricity;
                orbit.currentOrbitalPosition = b.orbitalParams.initialPhaseDeg / 360f;
            }
            slots.Add(new OrbitalSlot { body = body, orbit = orbit, moons = new OrbitalSlot[0] });
        }

        // Tri par demi-grand axe croissant
        slots.Sort((a, b) => a.orbit.semiMajorAxis.CompareTo(b.orbit.semiMajorAxis));
        data.orbitalSlots = slots.ToArray();

        LoadSystem(data);
        Debug.Log($"[SolarSystemView] Système chargé depuis serveur : {data.systemName} ({slots.Count} corps)");
    }

    // =========================================================
    // Mini Goldberg — colorisation biome depuis le serveur
    // =========================================================

    [Serializable] private struct MiniBodyEntry { public string bodyId; public string name; }
    [Serializable] private struct MiniBodyList  { public MiniBodyEntry[] items; }
    [Serializable] private struct MiniTileList  { public GoldbergTileState[] items; }

    private IEnumerator FetchAndColorizeMiniPlanets()
    {
        string baseUrl = simulationServerUrl.TrimEnd('/');

        // 1) Résoudre name → bodyId
        Dictionary<string, string> nameToId = new Dictionary<string, string>();
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/bodies"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
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

        Dictionary<TerrainType, Color> colorByType = BuildDefaultColorByType();

        // 2) Fetcher les tuiles de chaque planète et coloriser son mini-mesh
        foreach (var kv in _miniMeshes)
        {
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

            GoldbergFaceColorizer.ColorizeFromServerTiles(md.faces, allTiles.ToArray(), colorByType);
            GoldbergSphereGenerator.ApplyFaceColors(md.mesh, md.faces, md.vertexFaceId);

            Debug.Log($"[SolarSystemView] {bodyName} : {allTiles.Count} tuiles → {md.faces.Length} faces colorisées.");

            // Étale légèrement le travail sur plusieurs frames pour éviter les pics au lancement.
            yield return null;
        }
    }

    private static Dictionary<TerrainType, Color> BuildDefaultColorByType()
    {
        return new Dictionary<TerrainType, Color>
        {
            { TerrainType.Roche,             new Color(0.45f, 0.38f, 0.30f) },
            { TerrainType.Eau,               new Color(0.10f, 0.35f, 0.65f) },
            { TerrainType.Glace,             new Color(0.88f, 0.93f, 0.98f) },
            { TerrainType.Vegetation,        new Color(0.25f, 0.52f, 0.20f) },
            { TerrainType.AtmosphereToxique, new Color(0.55f, 0.50f, 0.15f) },
            { TerrainType.Metal,             new Color(0.60f, 0.60f, 0.65f) },
        };
    }

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
