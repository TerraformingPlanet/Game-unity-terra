using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

/// <summary>
/// Sphère de Goldberg interactive en Vue 2 (Vue Planétaire).
///
/// Remplace PlanetSphere.cs. Interface publique identique :
///   - LoadPlanet(body, override, waterLevelOffset)
///   - GetProjectedCell(latNorm, lonNorm) → HexCell
///   - TryBuildProjectionSummary(out summary) → bool
///   - ClearProjectionCache()
///   - event OnRegionClicked(latNorm, lonNorm)
///
/// Rendu :
///   - Mesh GP coloré en vertex colors (shader Terraformation/HexVertexColor).
///   - Si aucun matériau n'est assigné en Inspector, le shader est cherché
///     par nom au démarrage.
///   - Clic détecté via Physics.Raycast + coordonnées sphériques (pas UV).
///
/// Prérequis Unity :
///   - MeshFilter + MeshRenderer sur le même GameObject.
///   - Un MeshCollider est ajouté automatiquement si absent.
///   - Assigner un matériau basé sur Terraformation/HexVertexColor en Inspector.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // Cache interne
    // =========================================================

    private readonly struct CacheKey : IEquatable<CacheKey>
    {
        public readonly int                    BodyId;
        public readonly DebugCoherenceOverride CoherenceOverride;
        public readonly int                    WaterLevelKey;

        public CacheKey(int bodyId, DebugCoherenceOverride coherenceOverride, int waterLevelKey)
        {
            BodyId             = bodyId;
            CoherenceOverride  = coherenceOverride;
            WaterLevelKey      = waterLevelKey;
        }

        public bool Equals(CacheKey other) =>
            BodyId == other.BodyId &&
            CoherenceOverride == other.CoherenceOverride &&
            WaterLevelKey == other.WaterLevelKey;

        public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + BodyId;
                h = h * 31 + (int)CoherenceOverride;
                h = h * 31 + WaterLevelKey;
                return h;
            }
        }
    }

    private sealed class CachedSphere
    {
        public GoldbergSphereGenerator.GoldbergMeshData SphereData;

        public CachedSphere(GoldbergSphereGenerator.GoldbergMeshData sphereData)
        {
            SphereData = sphereData;
        }
    }

    private static readonly Dictionary<CacheKey, CachedSphere> SphereCache =
        new Dictionary<CacheKey, CachedSphere>();

    // =========================================================
    // Events
    // =========================================================

    /// <summary>
    /// Déclenché quand l'utilisateur clique sur une tuile.
    /// latNorm [0–1] : 0 = pôle sud. lonNorm [0–1] : ouest→est.
    /// </summary>
    public event Action<float, float> OnRegionClicked;

    /// <summary>
    /// Déclenché après résolution asynchrone de la tuile H3 au clic (GET /bodies/{id}/tiles/at).
    /// Fournit les données authoritatives du serveur pour l'affichage HUD.
    /// </summary>
    public event Action<GoldbergTileState> OnH3TileResolved;

    /// <summary>
    /// Déclenché quand les tuiles H3 sont toutes chargées et appliquées à la sphère.
    /// Fournit le tableau complet + la table de couleurs pour alimenter FlatView et TangentView.
    /// </summary>
    public event Action<GoldbergTileState[], Dictionary<TerrainType, Color>> OnH3TilesReady;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Rendu")]
    [Tooltip("Matériau utilisant le shader Terraformation/HexVertexColor. "
           + "Si null, le shader est cherché par nom au démarrage.")]
    [SerializeField] private Material sphereMaterial;

    [Header("Atmosphère")]
    [Tooltip("GoldbergAtmosphere enfant à piloter automatiquement au LoadPlanet. Optionnel.")]
    [SerializeField] private GoldbergAtmosphere atmosphere;

    [Header("Nuages")]
    [Tooltip("Couche de nuages orbitale non volumétrique. Si absente, elle sera créée automatiquement.")]
    [SerializeField] private PlanetCloudLayer cloudLayer;

    [Header("Cache")]
    [SerializeField] private bool cacheGeneratedProjections = true;

    [Header("LOD Planétaire")]
    [Tooltip("Active le changement de résolution du mesh selon la distance caméra.")]
    [SerializeField] private bool enableLod = true;
    [Tooltip("Si activé, la vue planète démarre directement sur le LOD haut et lance le fetch H3 max dès l'ouverture.")]
    [SerializeField] private bool startAtMaxResolution = true;
    [Tooltip("Distance d'orbite au-delà de laquelle on utilise le LOD bas.")]
    [SerializeField] private float lodFarDistance = 26f;
    [Tooltip("Distance d'orbite en-deçà de laquelle on utilise le LOD haut.")]
    [SerializeField] private float lodNearDistance = 20f;
    [Tooltip("Subdivisions supplémentaires pour le LOD haut (0 = doublé auto).")]
    [SerializeField] private int lodNearExtraDivisions = 0;
    [Tooltip("CameraController de la scène — auto-détecté si null.")]
    [SerializeField] private CameraController cameraController;

    [Header("Serveur")]
    [Tooltip("Recolorise les tuiles GP depuis GET /bodies/{id}/tiles après LoadPlanet.")]
    [SerializeField] private bool fetchServerTilesOnLoad = true;
    [SerializeField] private string simulationServerUrl = "http://127.0.0.1:8080";
    [SerializeField] private float simulationServerTimeoutSeconds = 2f;

    // =========================================================
    // Runtime
    // =========================================================

    [Header("Hover")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 1f, 1f, 0.35f);

    private MeshFilter    _meshFilter;
    private MeshRenderer  _meshRenderer;
    private MeshCollider  _meshCollider;

    private GoldbergSphereGenerator.GoldbergMeshData _sphereData;

    // Hover state
    private int     _hoveredFaceId   = -1;
    private Color[] _cachedMeshColors;  // couleurs sans highlight, resync à chaque LoadPlanet

    // H3 state
    private string      _activeBodyId = "";  // bodyId H3 du corps chargé — résolu par FetchAndColorizeFromServer
    private OrbitalBody _activeBody;         // référence conservée pour colorByType complet
    private DebugCoherenceOverride _activeCoherenceOverride;  // conservé pour bootstrap auto
    private float _activeWaterLevelOffset;                    // conservé pour bootstrap auto

    // LOD state
    private int           _currentLodLevel = -1;       // -1 = non initialisé, 0 = far, 1 = near
    private int           _lodLoDivisions;              // subdivisions du LOD bas (from Generate)
    private int           _lodHiDivisions;              // subdivisions du LOD haut
    private GoldbergSphereGenerator.GoldbergMeshData _sphereDataLo; // cache LOD bas
    private GoldbergSphereGenerator.GoldbergMeshData _sphereDataHi; // cache LOD haut
    private GoldbergTileState[]           _cachedServerTiles;  // tuiles du serveur (pour re-coloriser)
    private Dictionary<TerrainType, Color> _cachedColorByType; // couleurs (pour re-coloriser)
    private Dictionary<string, Color>      _ownershipTints;    // tileId → corp color (Phase 7.1)
    private Dictionary<string, string>     _tileToCorpId;      // tileId → corpId, for border detection (Phase 7.1)
    private OwnershipBorderRenderer        _borderRenderer;    // dessine les frontières en LineRenderer
    private bool          _lodHiColored   = false;     // tuiles res=3 déjà appliquées au LOD haut
    private bool          _lodHiFetching  = false;     // fetch en cours

    // =========================================================
    // Propriétés pour PlanetTangentView
    // =========================================================

    /// <summary>Index de la dernière face GP cliquée (-1 si aucune).</summary>
    public int LastClickedFaceId { get; private set; } = -1;

    /// <summary>Centroïde 3D de la dernière tuile cliquée (magnitude = VisualRadius).</summary>
    public Vector3 LastClickedFaceCentroid { get; private set; } = Vector3.up * GoldbergSphereGenerator.VisualRadius;

    /// <summary>
    /// Index H3 de la tuile cliquée (ex : "820007fffffffff").
    /// Résolu de façon asynchrone via GET /bodies/{bodyId}/tiles/at après chaque clic.
    /// Vide si le serveur est indisponible ou si aucun clic n'a encore eu lieu.
    /// </summary>
    public string LastClickedH3TileId { get; private set; } = "";

    /// <summary>Données mesh GP courant (faces, vertexFaceId, mesh).</summary>
    public GoldbergSphereGenerator.GoldbergMeshData SphereData => _sphereData;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _meshFilter   = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        // Ajoute MeshCollider si absent (nécessaire pour OnMouseDown)
        _meshCollider = GetComponent<MeshCollider>();
        if (_meshCollider == null)
        {
            Collider existing = GetComponent<Collider>();
            if (existing != null) Destroy(existing);
            _meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        _meshCollider.convex = false;

        // CameraController auto-détection
        if (cameraController == null)
            cameraController = FindAnyObjectByType<CameraController>();

        // Matériau vertex color
        if (sphereMaterial != null)
        {
            _meshRenderer.sharedMaterial = sphereMaterial;
        }
        else
        {
            Shader s = Shader.Find("Terraformation/HexVertexColor");
            if (s != null)
                _meshRenderer.material = new Material(s);
            else
                Debug.LogWarning("[PlanetSphereGoldberg] Shader 'Terraformation/HexVertexColor' introuvable. "
                               + "Assigner un matériau en Inspector.");
        }

        if (cloudLayer == null)
            cloudLayer = EnsureCloudLayer();

        _borderRenderer = gameObject.AddComponent<OwnershipBorderRenderer>();
    }

    private void OnDestroy() { }

    private void Update()
    {
        if (!enableLod) return;
        if (cameraController == null) return;
        // Le swap LOD doit rester possible même avant la réception complète des tuiles H3 serveur.
        if (_sphereDataLo.faces == null || _sphereDataHi.faces == null) return;

        float dist = cameraController.OrbitDistance;

        // Hysteresis : deux seuils pour éviter le flickering
        int targetLod;
        if (_currentLodLevel <= 0)
            targetLod = dist < lodNearDistance ? 1 : 0;
        else
            targetLod = dist > lodFarDistance  ? 0 : 1;

        if (targetLod == _currentLodLevel) return;

        _currentLodLevel = targetLod;
        ApplyLodLevel(_currentLodLevel);
    }

    private void TryStartHiLodFetch()
    {
        if (_currentLodLevel != 1 || _lodHiColored || _lodHiFetching) return;
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedColorByType == null) return;
        _lodHiFetching = true;
        StartCoroutine(FetchAndColorizeHiLod());
    }

    private IEnumerator FetchAndColorizeHiLod()
    {
        string baseUrl = simulationServerUrl.TrimEnd('/');
        var allTiles   = new List<GoldbergTileState>();
        int page       = 0;
        const int pageSize = 5000;
        int lodTimeoutSec = 60;  // première génération res=3 peut prendre ~2s + réseau

        while (true)
        {
            string url = $"{baseUrl}/bodies/{_activeBodyId}/tiles/lod?h3_resolution=3&page={page}&size={pageSize}";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = lodTimeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] LOD haut fetch échoué ({req.error}).");
                _lodHiFetching = false;
                yield break;
            }

            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            GoldbergTileArray batch;
            try   { batch = JsonUtility.FromJson<GoldbergTileArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] LOD haut parse invalide."); _lodHiFetching = false; yield break; }

            if (batch?.items == null || batch.items.Length == 0) break;
            allTiles.AddRange(batch.items);
            if (batch.items.Length < pageSize) break;
            page++;
        }

        if (allTiles.Count == 0) { _lodHiFetching = false; yield break; }

        // Applique les tuiles res=3 sur le mesh haut (indépendamment du LOD actif)
        GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereDataHi.faces, allTiles.ToArray(), _cachedColorByType);
        GoldbergSphereGenerator.ApplyFaceColors(_sphereDataHi.mesh, _sphereDataHi.faces, _sphereDataHi.vertexFaceId);

        // Si le LOD haut est affiché, resync snapshot hover pour refléter les nouvelles couleurs
        if (_currentLodLevel == 1)
        {
            _cachedMeshColors = (Color[])_sphereDataHi.mesh.colors.Clone();
            _hoveredFaceId    = -1;
        }

        Debug.Log($"[PlanetSphereGoldberg] LOD haut — {allTiles.Count} tuiles res=3 appliquées sur {_sphereDataHi.faces.Length} faces.");

        _lodHiColored  = true;
        _lodHiFetching = false;
    }

    // =========================================================
    // Ownership overlay (Phase 7.1)
    // =========================================================

    /// <summary>
    /// Re-fetches the ownership overlay from the server and reapplies it.
    /// Called externally (e.g. after claim/unclaim) to refresh the visualization.
    /// </summary>
    public void RefreshOwnershipOverlay()
    {
        _ownershipTints = null;
        _tileToCorpId   = null;
        _borderRenderer?.ClearBorders();
        StartCoroutine(FetchOwnershipOverlay());
    }

    /// <summary>
    /// Fetches GET /game/corporations and tints each claimed tile on the current body
    /// with the owning corporation's derived color. Called after biome colorization.
    /// No-op if no tiles are claimed on this body or if the server is unreachable.
    /// </summary>
    private IEnumerator FetchOwnershipOverlay()
    {
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedServerTiles == null) yield break;

        string baseUrl = simulationServerUrl.TrimEnd('/');
        CorporationDataArray corps = null;
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/game/corporations"))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] Ownership fetch échoué ({req.error}) — overlay ignoré.");
                yield break;
            }

            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            try   { corps = JsonUtility.FromJson<CorporationDataArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] Ownership parse invalide."); yield break; }
        }
        if (corps?.items == null || corps.items.Length == 0) yield break;

        // Build tileId → corp color + tileId → corpId maps (filtered to tiles on the current body)
        var tints     = new Dictionary<string, Color>();
        var toCorpId  = new Dictionary<string, string>();
        Debug.Log($"[PlanetSphereGoldberg] Ownership: {corps.items.Length} corp(s) reçue(s), activeBodyId='{_activeBodyId}'");
        foreach (CorporationData corp in corps.items)
        {
            int tileLen = corp.claimedTiles?.Length ?? -1;
            Debug.Log($"[PlanetSphereGoldberg]   corp='{corp.name}' id='{corp.id}' claimedTiles.Length={tileLen}");
            if (corp.claimedTiles == null) continue;
            Color corpColor = GoldbergFaceColorizer.CorpColorFromId(corp.id);
            foreach (ClaimedTile ct in corp.claimedTiles)
            {
                Debug.Log($"[PlanetSphereGoldberg]     tile bodyId='{ct.bodyId}' tileId='{ct.tileId}' match={ct.bodyId == _activeBodyId}");
                if (ct.bodyId == _activeBodyId && !string.IsNullOrEmpty(ct.tileId))
                {
                    tints[ct.tileId]    = corpColor;
                    toCorpId[ct.tileId] = corp.id;
                }
            }
        }
        Debug.Log($"[PlanetSphereGoldberg] Ownership: {tints.Count} tuile(s) à teinter sur ce corps.");

        if (tints.Count == 0) { _borderRenderer?.ClearBorders(); yield break; }
        _ownershipTints  = tints;
        _tileToCorpId    = toCorpId;

        // Dessiner les frontières en LineRenderer (pas de modification des vertex colors)
        var activeFaces = (_sphereDataHi.faces != null && _sphereDataHi.faces.Length > 0)
            ? _sphereDataHi.faces : _sphereData.faces;
        var loops = GoldbergFaceColorizer.GetBoundaryLoops(
            activeFaces, _cachedServerTiles, _ownershipTints, _tileToCorpId);
        _borderRenderer?.UpdateBorders(loops);

        Debug.Log($"[PlanetSphereGoldberg] Ownership overlay : {tints.Count} tuile(s), {loops.Count} boucle(s) de frontière ({corps.items.Length} corpo(s)).");
    }

    // =========================================================
    // API publique — même interface que PlanetSphere
    // =========================================================

    /// <summary>
    /// Charge et affiche la planète sous forme de sphère Goldberg colorée par biomes.
    /// </summary>
    public void LoadPlanet(OrbitalBody body,
                           DebugCoherenceOverride coherenceOverride = DebugCoherenceOverride.None,
                           float waterLevelOffset = 0f)
    {
        if (body == null)
        {
            Debug.LogError("[PlanetSphereGoldberg] OrbitalBody manquant.");
            return;
        }

#pragma warning disable CS0618
        CacheKey cacheKey = new CacheKey(body.GetInstanceID(), coherenceOverride, 0);
#pragma warning restore CS0618

        if (cacheGeneratedProjections && SphereCache.TryGetValue(cacheKey, out CachedSphere cached))
        {
            _sphereData = cached.SphereData;
            Debug.Log($"[PlanetSphereGoldberg] Cache GP : {body.bodyName} | {_sphereData.faces.Length} tuiles");
        }
        else
        {
            _sphereData = GoldbergSphereGenerator.Generate(body);

            // Coloration initiale depuis les biomes du corps (avant réception des tuiles H3)
            Color baseColor = body.displayColor;
            if (_activeBody?.layers != null)
            {
                foreach (LayerZone layer in _activeBody.layers)
                    if (layer?.biomes != null && layer.biomes.Length > 0 && layer.biomes[0] != null)
                    { baseColor = layer.biomes[0].color; break; }
            }
            for (int _fi = 0; _fi < _sphereData.faces.Length; _fi++)
                _sphereData.faces[_fi].color = baseColor;
            GoldbergSphereGenerator.ApplyFaceColors(_sphereData.mesh, _sphereData.faces, _sphereData.vertexFaceId);

            if (cacheGeneratedProjections)
                SphereCache[cacheKey] = new CachedSphere(_sphereData);

            Debug.Log($"[PlanetSphereGoldberg] Généré GP : {body.bodyName} | {_sphereData.faces.Length} tuiles");
        }

        Color baseColorForLod = body.displayColor;
        if (body.layers != null)
        {
            foreach (LayerZone layer in body.layers)
                if (layer?.biomes != null && layer.biomes.Length > 0 && layer.biomes[0] != null)
                { baseColorForLod = layer.biomes[0].color; break; }
        }

        // ── LOD : mesh BAS (492 faces) et mesh HAUT (1962 faces) ───────────
        // Les deux sont colorisés avec les données res=2 (5882 tuiles H3).
        // LOD haut donne des hexagones visuellement ~2x plus petits au zoom.
        if (enableLod)
        {
            _lodLoDivisions = GoldbergSphereGenerator.ComputeDivisions(body.radius);
            _lodHiDivisions = lodNearExtraDivisions > 0
                ? _lodLoDivisions + lodNearExtraDivisions
                : Mathf.Min(_lodLoDivisions * 2, 15);

            _sphereDataLo = _sphereData;  // LOD bas  = mesh déjà généré (ex: 492 faces)
            _sphereDataHi = GoldbergSphereGenerator.GenerateWithDivisions(_lodHiDivisions);  // ex: 1962 faces
            _currentLodLevel = -1;
            _lodHiColored  = false;
            _lodHiFetching = false;

            for (int faceIndex = 0; faceIndex < _sphereDataHi.faces.Length; faceIndex++)
                _sphereDataHi.faces[faceIndex].color = baseColorForLod;
            GoldbergSphereGenerator.ApplyFaceColors(_sphereDataHi.mesh, _sphereDataHi.faces, _sphereDataHi.vertexFaceId);

            Debug.Log($"[PlanetSphereGoldberg] LOD : bas={_lodLoDivisions} ({_sphereDataLo.faces.Length} faces) | haut={_lodHiDivisions} ({_sphereDataHi.faces.Length} faces)");
        }

        if (enableLod && startAtMaxResolution)
        {
            _currentLodLevel = 1;
            _sphereData = _sphereDataHi;
            _meshFilter.sharedMesh = _sphereDataHi.mesh;
            _meshCollider.sharedMesh = _sphereDataLo.mesh;
        }
        else
        {
            _meshFilter.sharedMesh   = _sphereData.mesh;
            _meshCollider.sharedMesh = _sphereData.mesh;
        }

        // Snapshot des couleurs de base (avant hover)
        _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
        _hoveredFaceId    = -1;

        // Atmosphère
        atmosphere?.ApplyBodyData(body);
        cloudLayer?.ApplyBodyData(body);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale    = Vector3.one;

        _activeBody = body;
        _activeCoherenceOverride  = coherenceOverride;
        _activeWaterLevelOffset   = waterLevelOffset;
        if (fetchServerTilesOnLoad)
            StartCoroutine(FetchAndColorizeFromServer(body.bodyName, coherenceOverride, waterLevelOffset));

        if (enableLod && startAtMaxResolution)
            TryStartHiLodFetch();
    }

    private PlanetCloudLayer EnsureCloudLayer()
    {
        Transform existing = transform.Find("CloudLayer");
        if (existing != null)
        {
            PlanetCloudLayer existingLayer = existing.GetComponent<PlanetCloudLayer>();
            if (existingLayer != null)
                return existingLayer;
        }

        GameObject cloudObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cloudObject.name = "CloudLayer";
        cloudObject.transform.SetParent(transform, false);
        cloudObject.transform.localPosition = Vector3.zero;
        cloudObject.transform.localRotation = Quaternion.identity;
        cloudObject.transform.localScale = Vector3.one * 1.045f;

        Collider collider = cloudObject.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        return cloudObject.AddComponent<PlanetCloudLayer>();
    }

    // =========================================================
    // Recolorisation depuis le serveur §4.4
    // =========================================================

    [Serializable]
    private class BodyListEntryArray   { public SimulationBodyListEntry[] items; }
    [Serializable]
    private class GoldbergTileArray    { public GoldbergTileState[] items; }
    [Serializable]
    private class CorporationDataArray { public CorporationData[] items; }

    private IEnumerator FetchAndColorizeFromServer(string planetName,
        DebugCoherenceOverride coherenceOverride = DebugCoherenceOverride.None,
        float waterLevelOffset = 0f)
    {
        // Construire colorByType depuis les biomes déclarés sur le corps (couverture complète)
        var colorByType = new Dictionary<TerrainType, Color>();
        if (_activeBody?.layers != null)
        {
            foreach (LayerZone layer in _activeBody.layers)
                if (layer?.biomes != null)
                    foreach (TerrainData td in layer.biomes)
                        if (td != null && !colorByType.ContainsKey(td.terrainType))
                            colorByType[td.terrainType] = td.color;
        }
        // Fallback couleurs réalistes pour les types non couverts par les biomes
        if (!colorByType.ContainsKey(TerrainType.Roche))             colorByType[TerrainType.Roche]             = new Color(0.45f, 0.38f, 0.30f); // brun rocheux
        if (!colorByType.ContainsKey(TerrainType.Eau))               colorByType[TerrainType.Eau]               = new Color(0.10f, 0.35f, 0.65f); // bleu océan
        if (!colorByType.ContainsKey(TerrainType.Glace))             colorByType[TerrainType.Glace]             = new Color(0.88f, 0.93f, 0.98f); // blanc-bleuté glace
        if (!colorByType.ContainsKey(TerrainType.Vegetation))        colorByType[TerrainType.Vegetation]        = new Color(0.25f, 0.52f, 0.20f); // vert végétation
        if (!colorByType.ContainsKey(TerrainType.AtmosphereToxique)) colorByType[TerrainType.AtmosphereToxique] = new Color(0.55f, 0.50f, 0.15f); // jaune-brun toxique
        if (!colorByType.ContainsKey(TerrainType.Metal))             colorByType[TerrainType.Metal]             = new Color(0.60f, 0.60f, 0.65f); // gris métal

        // 1) Récupérer la liste des corps pour trouver le bodyId
        string baseUrl = simulationServerUrl.TrimEnd('/');
        string bodyId  = null;

        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/bodies"))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] Serveur inaccessible ({req.error}) — skip recolorisation.");
                yield break;
            }

            // JsonUtility ne parse pas les tableaux root — wrapping
            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            BodyListEntryArray list;
            try   { list = JsonUtility.FromJson<BodyListEntryArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] Parse /bodies invalide."); yield break; }

            if (list?.items != null)
            {
                foreach (SimulationBodyListEntry entry in list.items)
                {
                    if (entry.name == planetName && entry.surfaceType == "goldberg")
                    {
                        bodyId = entry.bodyId;
                        break;
                    }
                }
            }
        }

        // Si le serveur est vide (démarrage frais), lancer le bootstrap automatique
        if (string.IsNullOrEmpty(bodyId))
        {
            Debug.Log($"[PlanetSphereGoldberg] Corps '{planetName}' absent — bootstrap automatique.");
            // DebugCoherenceOverride est un IntEnum côté Python → envoyer la valeur entière
            int overrideInt    = (int)coherenceOverride;
            string escapedName = UnityWebRequest.EscapeURL(planetName);
            string waterStr    = waterLevelOffset.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            string bootstrapUrl = $"{baseUrl}/commands/bootstrap-demo?planet_name={escapedName}&projection_override={overrideInt}&projection_water_level={waterStr}";

            using (UnityWebRequest bsReq = UnityWebRequest.PostWwwForm(bootstrapUrl, ""))
            {
                bsReq.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds * 3f));
                yield return bsReq.SendWebRequest();
                if (bsReq.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[PlanetSphereGoldberg] Bootstrap échoué ({bsReq.error}) — skip recolorisation.");
                    yield break;
                }
                Debug.Log($"[PlanetSphereGoldberg] Bootstrap OK → re-fetch /bodies.");
            }

            // Re-fetch /bodies après bootstrap
            using (UnityWebRequest req2 = UnityWebRequest.Get(baseUrl + "/bodies"))
            {
                req2.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
                yield return req2.SendWebRequest();
                if (req2.result == UnityWebRequest.Result.Success)
                {
                    string wrapped2 = "{\"items\":" + req2.downloadHandler.text + "}";
                    BodyListEntryArray list2;
                    try   { list2 = JsonUtility.FromJson<BodyListEntryArray>(wrapped2); }
                    catch { list2 = null; }
                    if (list2?.items != null)
                    {
                        foreach (SimulationBodyListEntry entry in list2.items)
                        {
                            if (entry.name == planetName && entry.surfaceType == "goldberg")
                            {
                                bodyId = entry.bodyId;
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(bodyId))
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] Corps '{planetName}' toujours introuvable après bootstrap.");
                yield break;
            }
        }

        // Mémorise le bodyId pour les lookups H3 au clic
        _activeBodyId = bodyId;

        // 2) Paginer les tuiles : GET /bodies/{bodyId}/tiles?page=N&size=200
        var allTiles = new List<GoldbergTileState>();
        int page     = 0;
        const int pageSize = 200;

        while (true)
        {
            string url = $"{baseUrl}/bodies/{bodyId}/tiles?page={page}&size={pageSize}";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] /tiles page {page} indisponible ({req.error}).");
                break;
            }

            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            GoldbergTileArray batch;
            try   { batch = JsonUtility.FromJson<GoldbergTileArray>(wrapped); }
            catch { Debug.LogWarning($"[PlanetSphereGoldberg] Parse /tiles page {page} invalide."); break; }

            if (batch?.items == null || batch.items.Length == 0)
                break;

            allTiles.AddRange(batch.items);

            if (batch.items.Length < pageSize)
                break;

            page++;
        }

        if (allTiles.Count == 0)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] Aucune tuile reçue du serveur pour '{planetName}'.");
            yield break;
        }

        // 3) Recoloriser les faces GP
        GoldbergTileState[] tilesArray = allTiles.ToArray();
        if (enableLod && _sphereDataLo.faces != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereDataLo.faces, tilesArray, colorByType);
            GoldbergSphereGenerator.ApplyFaceColors(_sphereDataLo.mesh, _sphereDataLo.faces, _sphereDataLo.vertexFaceId);
        }
        else
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereData.faces, tilesArray, colorByType);
            GoldbergSphereGenerator.ApplyFaceColors(_sphereData.mesh, _sphereData.faces, _sphereData.vertexFaceId);
        }

        // Cache pour re-colorisation LOD
        _cachedServerTiles  = tilesArray;
        _cachedColorByType  = colorByType;

        // Coloriser aussi le LOD haut avec les tuiles res=2 en attendant le fetch res=3.
        if (enableLod && _sphereDataHi.faces != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereDataHi.faces, tilesArray, colorByType);
            GoldbergSphereGenerator.ApplyFaceColors(_sphereDataHi.mesh, _sphereDataHi.faces, _sphereDataHi.vertexFaceId);
        }

        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone(); // resync snapshot hover

        Debug.Log($"[PlanetSphereGoldberg] Tuiles serveur appliquées : {tilesArray.Length} tuiles → {_sphereData.faces.Length} faces.");

        // Ownership overlay (Phase 7.1) — tint des hexes claimés sur ce corps
        _ownershipTints  = null;
        _tileToCorpId    = null;
        yield return StartCoroutine(FetchOwnershipOverlay());

        // Si on était déjà en LOD haut avant la fin du fetch, swap maintenant que _cachedServerTiles est prêt
        if (_currentLodLevel == 1) ApplyLodLevel(1);

        // Notifie les autres vues (Flat, Tangent)
        OnH3TilesReady?.Invoke(tilesArray, colorByType);
    }

    /// <summary>Aucune projection Mercator après migration H3 — toujours null.</summary>
    public HexCell GetProjectedCell(float latitude, float longitude) => null;

    /// <summary>Aucune projection disponible après migration H3.</summary>
    public bool TryBuildProjectionSummary(out PlanetaryHexGrid.ProjectionDebugSummary summary)
    {
        summary = default;
        return false;
    }

    [ContextMenu("Clear Sphere Cache")]
    public void ClearProjectionCache()
    {
        ClearSphereCache();
    }

    // =========================================================
    // Détection de survol (hover highlight)
    // =========================================================

    private void OnMouseOver()
    {
        if (_sphereData.faces == null || _cachedMeshColors == null) return;
        if (Camera.main == null || Mouse.current == null) return;
        if (UIEventSystemUtility.IsPointerOverUI()) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        Vector3 dir     = (hit.point - transform.position).normalized;
        int     newFace = GoldbergSphereGenerator.FindNearestFaceId(_sphereData.faces, dir);

        if (newFace == _hoveredFaceId) return;

        // Restaure l'ancienne face et tinte la nouvelle
        Color[] meshColors = _sphereData.mesh.colors;
        RestoreFace(meshColors, _hoveredFaceId);
        TintFace(meshColors, newFace);
        _sphereData.mesh.colors = meshColors;
        _hoveredFaceId          = newFace;
    }

    private void OnMouseExit()
    {
        if (_hoveredFaceId < 0 || _cachedMeshColors == null) return;
        Color[] meshColors = _sphereData.mesh.colors;
        RestoreFace(meshColors, _hoveredFaceId);
        _sphereData.mesh.colors = meshColors;
        _hoveredFaceId          = -1;
    }

    private void TintFace(Color[] meshColors, int faceId)
    {
        if (faceId < 0 || faceId >= _sphereData.faces.Length) return;
        for (int i = 0; i < _sphereData.vertexFaceId.Length; i++)
        {
            if (_sphereData.vertexFaceId[i] == faceId)
                meshColors[i] = Color.Lerp(_cachedMeshColors[i], Color.white, hoverTintColor.a);
        }
    }

    private void RestoreFace(Color[] meshColors, int faceId)
    {
        if (faceId < 0 || faceId >= _sphereData.faces.Length) return;
        for (int i = 0; i < _sphereData.vertexFaceId.Length; i++)
        {
            if (_sphereData.vertexFaceId[i] == faceId)
                meshColors[i] = _cachedMeshColors[i];
        }
    }

    // =========================================================
    // Détection de clic (sphérique, sans UV)
    // =========================================================

    private void OnMouseDown()
    {
        if (_sphereData.faces == null || _cachedMeshColors == null) return;
        if (Camera.main == null || Mouse.current == null) return;
        if (UIEventSystemUtility.IsPointerOverUI()) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        // Direction brute du raycast (utilisée uniquement pour le fallback)
        Vector3 rawDir = (hit.point - transform.position).normalized;

        // Utilise le centroïde de la face survolée (hover) si disponible,
        // sinon fallback sur la face la plus proche du point cliqué.
        // Garantit la cohérence entre la tuile highlightée et la tuile H3 résolue.
        int faceId = _hoveredFaceId >= 0
            ? _hoveredFaceId
            : GoldbergSphereGenerator.FindNearestFaceId(_sphereData.faces, rawDir);

        if (faceId < 0) return;

        Vector3 dir = _sphereData.faces[faceId].centroid3D; // direction normalisée depuis le centroïde

        float latDeg  = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lonDeg  = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
        float latNorm = (latDeg + 90f)  / 180f;
        float lonNorm = (lonDeg + 180f) / 360f;

        HexCell cell = GetProjectedCell(latNorm, lonNorm);

        LastClickedFaceId       = faceId;
        LastClickedFaceCentroid = dir * GoldbergSphereGenerator.VisualRadius;

        Debug.Log($"[PlanetSphereGoldberg] Clic → face={faceId} lat={latNorm:F3} lon={lonNorm:F3} (H3 en cours de résolution...)");

        // Résout la tuile H3 précise de façon asynchrone (non-bloquant)
        if (!string.IsNullOrEmpty(_activeBodyId))
            StartCoroutine(LookupH3TileAtClick(latDeg, lonDeg));

        OnRegionClicked?.Invoke(latNorm, lonNorm);
    }

    /// <summary>
    /// Swap vers le niveau LOD demandé (0 = bas, 1 = haut).
    /// Colorise le mesh haut avec res=2 si res=3 pas encore disponible.
    /// MeshCollider reste toujours sur LOD bas (492 faces, sous la limite convex hull 256 polys).
    /// </summary>
    private void ApplyLodLevel(int level)
    {
        var nextData = level == 1 ? _sphereDataHi : _sphereDataLo;

        // LOD haut : colorise avec res=2 si res=3 pas encore prêt
        if (level == 1 && !_lodHiColored && _cachedServerTiles != null && _cachedColorByType != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(nextData.faces, _cachedServerTiles, _cachedColorByType);
            GoldbergSphereGenerator.ApplyFaceColors(nextData.mesh, nextData.faces, nextData.vertexFaceId);
        }

        _sphereData       = nextData;
        _hoveredFaceId    = -1;
        _cachedMeshColors = (Color[])nextData.mesh.colors.Clone();

        _meshFilter.sharedMesh   = nextData.mesh;
        // Collider toujours sur LOD bas (492 faces → convex OK; 1962 faces → KO)
        _meshCollider.sharedMesh = _sphereDataLo.mesh;

        string label = level == 1 ? "HAUT" : "BAS";
        Debug.Log($"[PlanetSphereGoldberg] LOD → {label} ({nextData.faces.Length} faces) dist={cameraController?.OrbitDistance:F1}");

        // Lance le fetch res=3 en arrière-plan (no-op si déjà fait ou en cours)
        if (level == 1) TryStartHiLodFetch();
    }

    // =========================================================
    // Lookup H3 au clic
    // =========================================================

    private IEnumerator LookupH3TileAtClick(float latDeg, float lonDeg)
    {
        string url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/bodies/{1}/tiles/at?lat={2}&lon={3}",
            simulationServerUrl.TrimEnd('/'), _activeBodyId, latDeg, lonDeg);

        using UnityWebRequest req = UnityWebRequest.Get(url);
        req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            yield break;

        GoldbergTileState tile;
        try   { tile = JsonUtility.FromJson<GoldbergTileState>(req.downloadHandler.text); }
        catch { yield break; }

        if (string.IsNullOrEmpty(tile.tileId))
            yield break;

        LastClickedH3TileId = tile.tileId;
        Debug.Log($"[PlanetSphereGoldberg] H3 tile : {tile.tileId} | {tile.terrainType} | eau={tile.waterRatio:F2} | t={tile.temperature:F1}°C");
        OnH3TileResolved?.Invoke(tile);
    }

    // =========================================================
    // Cleanup
    // =========================================================

    private static void ClearSphereCache()
    {
        foreach (CachedSphere cached in SphereCache.Values)
        {
            if (cached?.SphereData.mesh != null)
                Destroy(cached.SphereData.mesh);
        }
        SphereCache.Clear();
        Debug.Log("[PlanetSphereGoldberg] Cache sphères vidé.");
    }


    /// <summary>Retourne le rayon de la face GP (distance max centroïde→vertex, espace monde).</summary>
    public float GetFaceRadius(int faceId)
    {
        if (faceId < 0 || faceId >= _sphereData.faces.Length || _sphereData.mesh == null) return 1f;
        Vector3 centroidWorld = transform.TransformPoint(_sphereData.faces[faceId].centroid3D * GoldbergSphereGenerator.VisualRadius);
        Vector3[] verts = _sphereData.mesh.vertices;
        float maxDist = 0f;
        for (int i = 0; i < _sphereData.vertexFaceId.Length; i++)
        {
            if (_sphereData.vertexFaceId[i] == faceId)
            {
                float d = Vector3.Distance(centroidWorld, transform.TransformPoint(verts[i]));
                if (d > maxDist) maxDist = d;
            }
        }
        return maxDist > 0.0001f ? maxDist : 1f;
    }

    /// <summary>Masque une face GP (alpha=0) pour la remplacer visuellement par la grille hex.</summary>
    public void HideFaceOnSphere(int faceId)
    {
        if (faceId < 0 || faceId >= _sphereData.faces.Length || _sphereData.mesh == null) return;
        Color[] meshColors = _sphereData.mesh.colors;
        for (int i = 0; i < _sphereData.vertexFaceId.Length; i++)
        {
            if (_sphereData.vertexFaceId[i] == faceId)
            {
                Color c = meshColors[i]; c.a = 0f; meshColors[i] = c;
            }
        }
        _sphereData.mesh.colors = meshColors;
    }

    /// <summary>Restaure les couleurs originales d'une face GP après fermeture de la vue locale.</summary>
    public void RestoreFaceOnSphere(int faceId)
    {
        if (faceId < 0 || _cachedMeshColors == null || _sphereData.mesh == null) return;
        Color[] meshColors = _sphereData.mesh.colors;
        RestoreFace(meshColors, faceId);
        _sphereData.mesh.colors = meshColors;
    }
}
