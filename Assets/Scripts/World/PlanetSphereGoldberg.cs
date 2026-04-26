using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// Déclenché quand la souris s'immobilise &gt;= hoverTooltipDelay secondes sur une face (LOD 1 uniquement).
    /// Fournit un texte résumé + la position écran pour positionner le tooltip.
    /// </summary>
    public event Action<string, Vector2> OnTileHoverReady;

    /// <summary>
    /// Déclenché quand le tooltip doit être masqué (MouseExit, changement de face, changement LOD).
    /// </summary>
    public event Action OnTileHoverCancelled;

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

    [Header("Palette de couleurs")]
    [Tooltip("Palette de couleurs de terrain. Si null, des couleurs par défaut sont utilisées.")]
    [SerializeField] private TerrainColorPalette terrainPalette;

    [Header("Serveur")]
    [Tooltip("Recolorise les tuiles GP depuis GET /bodies/{id}/tiles après LoadPlanet.")]
    [SerializeField] private bool fetchServerTilesOnLoad = true;
    [SerializeField] private string simulationServerUrl = "http://127.0.0.1:8080";
    [SerializeField] private float simulationServerTimeoutSeconds = 15f;

    // =========================================================
    // Runtime
    // =========================================================

    [Header("Hover")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("Délai en secondes avant l'apparition du tooltip au survol (LOD 1 seulement).")]
    [SerializeField] private float hoverTooltipDelay = 0.6f;

    [Header("Debug")]
    [Tooltip("Active les logs détaillés pour LOD et overlays.")]
    [SerializeField] private bool debugLodVerbose = false;

    private MeshFilter    _meshFilter;
    private MeshRenderer  _meshRenderer;
    private MeshCollider  _meshCollider;

    private GoldbergSphereGenerator.GoldbergMeshData _sphereData;

    // Hover state
    private int     _hoveredFaceId   = -1;
    private Color[] _cachedMeshColors;  // couleurs sans highlight, resync à chaque LoadPlanet

    // Tooltip hover state
    private Dictionary<int, GoldbergTileState> _faceToTile;
    private int   _hoverFaceCandidate = -1;
    private float _hoverStartTime     = -1f;
    private bool  _hoverTooltipFired  = false;

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
    private GoldbergTileState[]           _cachedServerTiles;    // tuiles du serveur res=2 (pour re-coloriser)
    private GoldbergTileState[]           _cachedServerTilesHi;  // tuiles res=3 (pour ReapplyOverlays au LOD haut — évite mismatch H3 res)
    private Dictionary<TerrainType, Color> _cachedColorByType; // couleurs (pour re-coloriser)
    private Dictionary<string, Color>      _ownershipTints;    // tileId → corp color (Phase 7.1)
    private Dictionary<string, string>     _tileToCorpId;      // tileId → corpId, for border detection (Phase 7.1)
    private Dictionary<string, Color>      _stateTints;        // tileId → state color (Phase colonisation) — terres seulement
    private Dictionary<string, Color>      _allStateTints;     // tileId → state color — tous tiles (pour bordures)
    private Dictionary<string, string>     _tileToStateId;     // tileId → stateId
    private Dictionary<string, string>     _tileToStateName;   // tileId → stateName
    private bool                            _stateOverlayFetched; // éviter fetch multiple
    private bool                            _ownershipOverlayFetched; // éviter fetch multiple
    private OwnershipBorderRenderer        _borderRenderer;    // dessine les frontières en LineRenderer
    private bool          _lodHiColored      = false;  // tuiles res=3 déjà appliquées au LOD haut
    private bool          _lodHiFetching     = false;  // fetch en cours
    private bool          _lodHiBaseColored  = false;  // couleurs biomes res=2 déjà appliquées au LOD haut (évite re-colorisation dans ApplyLodLevel)

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

        if (targetLod != _currentLodLevel)
        {
            _currentLodLevel = targetLod;
            ApplyLodLevel(_currentLodLevel);
        }

        // Tooltip timer (LOD 1 seulement)
        if (!_hoverTooltipFired && _hoverFaceCandidate >= 0 && _currentLodLevel == 1
            && Time.time - _hoverStartTime >= hoverTooltipDelay)
        {
            FireTileTooltip(_hoverFaceCandidate);
        }
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
        const int lodTimeoutSec = 60;  // première génération res=3 peut prendre ~2s + réseau

        GoldbergTileState[] tilesArray = null;
        string tilesError = null;
        yield return StartCoroutine(FetchTilesPages(_activeBodyId, 3, 5000, lodTimeoutSec,
            tiles => tilesArray = tiles,
            err   => tilesError = err));

        if (tilesError != null)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] LOD haut fetch échoué ({tilesError}).");
            _lodHiFetching = false;
            yield break;
        }
        if (tilesArray == null || tilesArray.Length == 0) { _lodHiFetching = false; yield break; }

        // NE PAS recoloriser le terrain avec les tuiles res=3.
        // Les couleurs terrain (res=2) sont déjà appliquées sur _sphereDataHi.faces dans
        // FetchAndColorizeFromServer. Recoloriser avec res=3 provoque un décalage :
        // H3 res=3 découpe l'espace différemment (un hex res=2 "Eau" peut avoir des
        // sous-tuiles res=3 "Roche" aux bordures), ce qui désynchronise les couleurs
        // des faces avec les borders de territoire dessinées depuis les tileIds res=2.
        //
        // Les tuiles res=3 sont stockées pour un usage futur (tooltip précis au LOD haut).
        _cachedServerTilesHi = tilesArray;

        // Réappliquer les overlays (corp, construction) sur les couleurs terrain déjà présentes.
        // Utiliser res=2 pour les tints — les dicts ownershipTints/stateTints sont indexés par tileId res=2.
        if (_cachedServerTiles != null)
            ReapplyOverlays(_sphereDataHi, _cachedServerTiles);

        // Si le LOD haut est affiché, resync snapshot hover pour refléter les nouvelles couleurs
        if (_currentLodLevel == 1)
        {
            _cachedMeshColors = (Color[])_sphereDataHi.mesh.colors.Clone();
            _hoveredFaceId    = -1;
        }

        if (debugLodVerbose)
            Debug.Log($"[PlanetSphereGoldberg] LOD haut — {tilesArray.Length} tuiles res=3 appliquées sur {_sphereDataHi.faces.Length} faces.");

        if (debugLodVerbose)
            Debug.Log($"[LOD] FetchAndColorizeHiLod | tiles={tilesArray.Length} | hiFaces={_sphereDataHi.faces.Length} | ownershipTints={_ownershipTints?.Count ?? 0}");

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
        _ownershipOverlayFetched = false;
        _borderRenderer?.ClearBorders();
        StartCoroutine(FetchOwnershipOverlay());
    }

    // =========================================================
    // Construction overlay (Phase 10.5)
    // =========================================================

    // Orange pulsing tint applied to tiles that have in-progress construction items.
    private HashSet<string> _constructionTileIds = new HashSet<string>();

    /// <summary>
    /// Recompute and re-apply border loops using the current active LOD mesh (_sphereData).
    /// No-op if ownership data is not yet available.
    /// </summary>
    private void RebuildBorderLoops()
    {
        if (_cachedServerTiles == null || _sphereData.faces == null) return;

        var loops = new System.Collections.Generic.List<(Vector3[], Color)>();

        // Corp borders — couleur de la corp (teinte ownership)
        if (_ownershipTints != null && _ownershipTints.Count > 0 && _tileToCorpId != null)
            loops.AddRange(GoldbergFaceColorizer.GetBoundaryLoops(
                _sphereData.faces, _cachedServerTiles, _ownershipTints, _tileToCorpId));

        // State borders (political map) — couleur de l'état (pas de recoloration des tuiles)
        if (_allStateTints != null && _allStateTints.Count > 0 && _tileToStateId != null)
        {
            var stateLoops = GoldbergFaceColorizer.GetBoundaryLoops(
                _sphereData.faces, _cachedServerTiles, _allStateTints, _tileToStateId);
            // Conserver la couleur de l'état retournée par GetBoundaryLoops
            loops.AddRange(stateLoops);
        }

        if (loops.Count > 0)
            _borderRenderer?.UpdateBorders(loops);
    }

    /// <summary>
    /// Updates the set of tiles under construction and applies an orange tint overlay.
    /// Call from GameHUD's PollConstructionQueue whenever the queue changes.
    /// Pass an empty set (or null) to clear the overlay.
    /// </summary>
    public void SetConstructionTiles(HashSet<string> tileIds)
    {
        _constructionTileIds = tileIds ?? new HashSet<string>();

        RebuildBorderLoops();
        ReapplyOverlays(_sphereData, _cachedServerTiles);
    }

    /// <summary>
    /// Applique les teintes overlays (corp) sur un mesh donné, puis ApplyFaceColors.
    /// Utilisé après chaque recolorisation biomes pour maintenir les teintes.
    /// </summary>
    private void ReapplyOverlays(GoldbergSphereGenerator.GoldbergMeshData meshData, GoldbergTileState[] serverTiles)
    {
        if (serverTiles == null) return;

        // Teintes état (political map) — PAS de recoloration des tuiles.
        // Les frontières d'état sont dessinées uniquement via OwnershipBorderRenderer (RebuildBorderLoops).
        // Les couleurs terrain (TerrainColorPalette) restent intactes.

        // Teintes corp (ownership) — par-dessus les couleurs terrain
        if (_ownershipTints != null && _ownershipTints.Count > 0 && _tileToCorpId != null)
            GoldbergFaceColorizer.ApplyOwnershipTint(
                meshData.faces, serverTiles, _ownershipTints, _tileToCorpId,
                borderBlend: 0.25f, interiorBlend: 0.10f);

        // Teintes construction (orange pulsing)
        if (_constructionTileIds != null && _constructionTileIds.Count > 0)
        {
            var constructionTints = new Dictionary<string, Color>();
            Color constructionColor = new Color(1f, 0.55f, 0f, 1f); // orange
            foreach (string tileId in _constructionTileIds)
                constructionTints[tileId] = constructionColor;
            GoldbergFaceColorizer.ApplyOwnershipTint(
                meshData.faces, serverTiles, constructionTints, null, // no corpId for borders
                borderBlend: 0.25f, interiorBlend: 0.10f);
        }

        // Appliquer les couleurs finales au mesh
        GoldbergSphereGenerator.ApplyFaceColors(meshData.mesh, meshData.faces, meshData.vertexFaceId);

        if (debugLodVerbose)
            Debug.Log($"[OVERLAY] ReapplyOverlays | faces={meshData.faces.Length} | stateTints={_stateTints?.Count ?? 0} | corpTints={_ownershipTints?.Count ?? 0} | constructionTints={_constructionTileIds?.Count ?? 0}");
    }

    /// <summary>
    /// Fetches GET /bodies/{body_id}/ownership-tiles and tints each claimed tile on the current body
    /// with the owning corporation's color from server. Called after biome colorization.
    /// No-op if no tiles are claimed on this body or if the server is unreachable.
    /// </summary>
    private IEnumerator FetchOwnershipOverlay()
    {
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedServerTiles == null) yield break;
        if (_ownershipOverlayFetched) yield break;

        OwnershipTileDtoArray tiles = null;
        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + "/bodies/" + _activeBodyId + "/ownership-tiles"))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] Ownership fetch échoué ({req.error}) — overlay ignoré.");
                yield break;
            }

            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            try   { tiles = JsonUtility.FromJson<OwnershipTileDtoArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] Ownership parse invalide."); yield break; }
        }
        if (tiles?.items == null || tiles.items.Length == 0) { _borderRenderer?.ClearBorders(); yield break; }

        // Build tileId → corp color + tileId → corpId maps
        var tints     = new Dictionary<string, Color>();
        var toCorpId  = new Dictionary<string, string>();
        foreach (OwnershipTileDto dto in tiles.items)
        {
            Color corpColor = new Color(dto.colorR, dto.colorG, dto.colorB, 1f);
            tints[dto.tileId]    = corpColor;
            toCorpId[dto.tileId] = dto.corpId;
        }
        Debug.Log($"[PlanetSphereGoldberg] Ownership: {tints.Count} tuile(s) à teinter sur ce corps.");

        if (tints.Count == 0) { _borderRenderer?.ClearBorders(); yield break; }
        _ownershipTints  = tints;
        _tileToCorpId    = toCorpId;

        // Dessiner les frontières depuis le mesh actif (LOD courant) — pas _sphereDataHi
        // pour éviter le mismatch « bordures trop petites au dezoom ».
        RebuildBorderLoops();

        string rendererStatus = _borderRenderer != null ? "OK" : "no renderer";

        if (debugLodVerbose)
            Debug.Log($"[OVERLAY] FetchOwnershipOverlay | tints={tints.Count} | renderer={rendererStatus}");

        Debug.Log($"[PlanetSphereGoldberg] Ownership overlay : {tints.Count} tuile(s), {rendererStatus}.");

        _ownershipOverlayFetched = true;
        ReapplyOverlays(_sphereData, _cachedServerTiles);
        // Resync snapshot hover — évite le flash vers couleurs pre-overlay au survol
        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
    }

    private IEnumerator FetchStateOverlay()
    {
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedServerTiles == null) yield break;

        string url = BaseUrl + $"/game/bodies/{_activeBodyId}/state-tile-colors";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] State overlay fetch échoué ({req.error}) — ignoré.");
                yield break;
            }

            StateTileColorArray data;
            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            try   { data = JsonUtility.FromJson<StateTileColorArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] State overlay parse invalide."); yield break; }

            if (data?.items == null || data.items.Length == 0) yield break;

            // Éviter fetch multiple (cause de couleurs changeantes)
            if (_stateOverlayFetched) yield break;

            // Map tileId → terrainType pour exclure précisément les tuiles d'eau du coloris
            var tileTerrain = new Dictionary<string, TerrainType>();
            if (_cachedServerTiles != null)
                foreach (GoldbergTileState t in _cachedServerTiles)
                    if (!string.IsNullOrEmpty(t.tileId))
                        tileTerrain[t.tileId] = t.terrainType;

            var stateColorCache = new Dictionary<string, Color>();
            var stateTints    = new Dictionary<string, Color>();  // coloris terre seulement
            var allStateTints = new Dictionary<string, Color>();  // tous tiles — pour GetBoundaryLoops
            var tileToStateId   = new Dictionary<string, string>(); // tous tiles (terre + eau)
            var tileToStateName = new Dictionary<string, string>(); // tous tiles — nom d'état
            foreach (StateTileColorDto entry in data.items)
            {
                if (string.IsNullOrEmpty(entry.tileId)) continue;
                Color col = new Color(entry.colorR, entry.colorG, entry.colorB, 1f);
                tileToStateId[entry.tileId]   = entry.stateId;
                tileToStateName[entry.tileId] = entry.stateName;
                allStateTints[entry.tileId] = col;                // TOUS les tiles
                // Exclure les tuiles d'eau du coloris (mais garder pour bordures)
                if (!tileTerrain.TryGetValue(entry.tileId, out TerrainType tt) || tt != TerrainType.Eau)
                    stateTints[entry.tileId] = col;               // coloris terre uniquement
            }

            _stateTints      = stateTints;
            _allStateTints   = allStateTints;
            _tileToStateId   = tileToStateId;
            _tileToStateName = tileToStateName;

            // Draw state boundary lines — pas de teinte de couleur, couleurs biomes conservées
            RebuildBorderLoops();

            _stateOverlayFetched = true;

            // Appliquer les couleurs d'état au mesh actif (comme FetchOwnershipOverlay le fait pour corp)
            ReapplyOverlays(_sphereData, _cachedServerTiles);
            // Resync snapshot hover — évite le flash vers couleurs pre-overlay au survol
            if (_sphereData.mesh != null)
                _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();

            Debug.Log($"[PlanetSphereGoldberg] State overlay : {stateTints.Count} tuile(s) colorées + bordures d'État.");

            if (debugLodVerbose)
                Debug.Log($"[OVERLAY] FetchStateOverlay | stateTints={stateTints.Count} | allStateTints={allStateTints.Count} | states={stateColorCache.Count} | tileToStateId={tileToStateId.Count}");
        }
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

        // Reset state overlay flag (nouveau LoadPlanet)
        _stateOverlayFetched = false;
        _ownershipOverlayFetched = false;

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
            _lodHiColored     = false;
            _lodHiFetching    = false;
            _lodHiBaseColored = false;

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

    // ── HTTP helpers ─────────────────────────────────────────

    /// <summary>Timeout en secondes, au minimum 1.</summary>
    private int TimeoutSec => Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));

    /// <summary>URL de base du serveur (sans slash final).</summary>
    private string BaseUrl => simulationServerUrl.TrimEnd('/');

    /// <summary>
    /// Coroutine utilitaire : récupère toutes les pages de tuiles H3 depuis
    /// GET /bodies/{bodyId}/tiles/lod?h3_resolution={res}&amp;page=N&amp;size={pageSize}.
    /// Appelle <paramref name="onComplete"/> avec le tableau complet en cas de succès,
    /// ou <paramref name="onError"/> (nullable) avec le message d'erreur en cas d'échec.
    /// </summary>
    private IEnumerator FetchTilesPages(
        string bodyId, int h3Resolution, int pageSize, int timeoutSec,
        Action<GoldbergTileState[]> onComplete, Action<string> onError = null)
    {
        var allTiles = new List<GoldbergTileState>();
        int page     = 0;

        while (true)
        {
            string url = $"{BaseUrl}/bodies/{bodyId}/tiles/lod?h3_resolution={h3Resolution}&page={page}&size={pageSize}";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = timeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(req.error);
                yield break;
            }

            GoldbergTileArray batch;
            try   { batch = JsonUtility.FromJson<GoldbergTileArray>("{\"items\":" + req.downloadHandler.text + "}"); }
            catch { onError?.Invoke($"parse error page {page}"); yield break; }

            if (batch?.items == null || batch.items.Length == 0) break;
            allTiles.AddRange(batch.items);
            if (batch.items.Length < pageSize) break;
            page++;
        }

        onComplete(allTiles.ToArray());
    }

    // ── DTO internes ─────────────────────────────────────────

    [Serializable]
    private class BodyListEntryArray   { public SimulationBodyListEntry[] items; }
    [Serializable]
    private class GoldbergTileArray    { public GoldbergTileState[] items; }
    [Serializable]
    private class CorporationDataArray { public CorporationData[] items; }

    [Serializable]
    private struct StateTileColorDto
    {
        public string tileId;
        public string stateId;
        public string stateName;
        public string profileKey;
        public float  colorR;
        public float  colorG;
        public float  colorB;
    }
    [Serializable]
    private class StateTileColorArray  { public StateTileColorDto[] items; }

    private IEnumerator FetchAndColorizeFromServer(string planetName,
        DebugCoherenceOverride coherenceOverride = DebugCoherenceOverride.None,
        float waterLevelOffset = 0f)
    {
        // Couleurs terrain : palette Unity (source de vérité), le serveur envoie seulement l'index int.
        var colorByType = terrainPalette != null
            ? terrainPalette.ToDictionary()
            : TerrainColorPalette.DefaultDictionary();

        // 1) Récupérer la liste des corps pour trouver le bodyId
        string bodyId  = null;

        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + "/bodies"))
        {
            req.timeout = TimeoutSec;
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

        // Si le serveur n'a pas encore de corps, on attend — le bootstrap est géré côté serveur.
        if (string.IsNullOrEmpty(bodyId))
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] Corps '{planetName}' introuvable sur le serveur. Le serveur n'est peut-être pas encore prêt.");
            yield break;
        }

        // Mémorise le bodyId pour les lookups H3 au clic
        _activeBodyId = bodyId;

        // 2) Récupérer TOUTES les tuiles via /tiles/lod?h3_resolution=2 (pagination)
        GoldbergTileState[] tilesArray = null;
        string tilesError = null;
        yield return StartCoroutine(FetchTilesPages(bodyId, 2, 6000, TimeoutSec,
            tiles => tilesArray = tiles,
            err   => tilesError = err));

        if (tilesError != null)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] /tiles/lod indisponible ({tilesError}).");
            yield break;
        }
        if (tilesArray == null || tilesArray.Length == 0)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] Aucune tuile reçue du serveur pour '{planetName}'.");
            yield break;
        }

        // 3) Recoloriser les faces GP
        if (enableLod && _sphereDataLo.faces != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereDataLo.faces, tilesArray, colorByType);
            ReapplyOverlays(_sphereDataLo, tilesArray);
        }
        else
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereData.faces, tilesArray, colorByType);
            ReapplyOverlays(_sphereData, tilesArray);
        }

        // Cache pour re-colorisation LOD
        _cachedServerTiles  = tilesArray;
        _cachedColorByType  = colorByType;

        // Coloriser aussi le LOD haut avec les tuiles res=2 en attendant le fetch res=3.
        if (enableLod && _sphereDataHi.faces != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereDataHi.faces, tilesArray, colorByType);
            ReapplyOverlays(_sphereDataHi, tilesArray);
            _lodHiBaseColored = true;  // couleurs biomes déjà appliquées — ApplyLodLevel n'a pas besoin de refaire
        }

        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone(); // resync snapshot hover

        Debug.Log($"[PlanetSphereGoldberg] Tuiles serveur appliquées : {tilesArray.Length} tuiles → {_sphereData.faces.Length} faces.");

        if (debugLodVerbose)
            Debug.Log($"[LOD] FetchAndColorizeFromServer | tiles={tilesArray.Length} | enableLod={enableLod} | hiLodReady={_sphereDataHi.faces != null} | ownershipTints={_ownershipTints?.Count ?? 0}");

        // Ownership overlay (Phase 7.1) — tint des hexes claimés sur ce corps
        _ownershipTints  = null;
        _tileToCorpId    = null;
        yield return StartCoroutine(FetchOwnershipOverlay());

        // State overlay (Phase colonisation) — carte politique
        _stateTints      = null;
        _tileToStateId   = null;
        _tileToStateName = null;
        yield return StartCoroutine(FetchStateOverlay());

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

    /// <summary>
    /// Snapshot debug : affiche dans la console un histogramme des couleurs actuelles
    /// des faces du mesh actif (_sphereData). Utiliser avant/après un switch LOD pour
    /// vérifier que les couleurs terrain ne changent pas.
    /// </summary>
    [ContextMenu("Debug — Snapshot couleurs faces")]
    public void DebugSnapshotFaceColors()
    {
        if (_sphereData.faces == null || _sphereData.faces.Length == 0)
        {
            Debug.LogWarning("[Snapshot] Aucune face — charger une planète d'abord.");
            return;
        }

        // Histogramme par couleur arrondie (R/G/B à 2 décimales)
        var hist = new Dictionary<string, int>();
        for (int i = 0; i < _sphereData.faces.Length; i++)
        {
            Color c = _sphereData.faces[i].color;
            string key = $"({c.r:F2},{c.g:F2},{c.b:F2})";
            hist[key] = hist.TryGetValue(key, out int v) ? v + 1 : 1;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Snapshot] LOD={_currentLodLevel} | faces={_sphereData.faces.Length} | body={_activeBodyId}");
        sb.AppendLine($"  res2_tiles={_cachedServerTiles?.Length ?? 0} | res3_tiles={_cachedServerTilesHi?.Length ?? 0}");
        sb.AppendLine($"  stateTints={_allStateTints?.Count ?? 0} | corpTints={_ownershipTints?.Count ?? 0}");
        sb.AppendLine("  Histogramme couleurs (top 10) :");
        int rank = 0;
        foreach (var kv in hist.OrderByDescending(x => x.Value))
        {
            sb.AppendLine($"    #{rank + 1}: {kv.Key} → {kv.Value} faces");
            if (++rank >= 10) break;
        }
        Debug.Log(sb.ToString());
    }

    // =========================================================
    // Détection de survol (hover highlight)
    // =========================================================

    private void OnMouseOver()
    {
        if (_sphereData.faces == null || _cachedMeshColors == null) return;
        if (Camera.main == null || Mouse.current == null) return;
        if (UIEventSystemUtility.IsPointerOverUI()) return;

        // Tooltip uniquement au LOD haut (LOD 1)
        if (_currentLodLevel != 1)
        {
            CancelTooltip();
        }

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

        // Reset tooltip timer à chaque changement de face
        CancelTooltip();
        if (_currentLodLevel == 1)
        {
            _hoverFaceCandidate = newFace;
            _hoverStartTime     = Time.time;
        }
    }

    private void OnMouseExit()
    {
        CancelTooltip();
        if (_hoveredFaceId < 0 || _cachedMeshColors == null) return;
        Color[] meshColors = _sphereData.mesh.colors;
        RestoreFace(meshColors, _hoveredFaceId);
        _sphereData.mesh.colors = meshColors;
        _hoveredFaceId          = -1;
    }

    private void CancelTooltip()
    {
        _hoverFaceCandidate = -1;
        _hoverStartTime     = -1f;
        if (_hoverTooltipFired)
        {
            _hoverTooltipFired = false;
            OnTileHoverCancelled?.Invoke();
        }
    }

    private void FireTileTooltip(int faceId)
    {
        if (_cachedServerTiles == null || _cachedServerTiles.Length == 0) return;
        if (_sphereData.faces == null || faceId < 0 || faceId >= _sphereData.faces.Length) return;

        // Lazy-build la map faceId → GoldbergTileState
        if (_faceToTile == null)
            _faceToTile = GoldbergFaceColorizer.BuildFaceToTileMap(_sphereData.faces, _cachedServerTiles);

        if (!_faceToTile.TryGetValue(faceId, out GoldbergTileState tile)) return;

        // Assembler le texte du tooltip : terrain + tileId court
        string shortId = !string.IsNullOrEmpty(tile.tileId) && tile.tileId.Length > 8
            ? tile.tileId[..8] + "..."
            : tile.tileId ?? "?";
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{tile.terrainType}</b>  <size=9>[{shortId}]</size>");

        // Ajouter le nom de la corp si la tuile est revendiquée
        if (_tileToCorpId != null && _tileToCorpId.TryGetValue(tile.tileId, out string corpId))
            sb.Append($"\nCorp: {corpId[..Mathf.Min(8, corpId.Length)]}");

        // Ajouter icône construction si en cours
        if (_constructionTileIds != null && _constructionTileIds.Contains(tile.tileId))
            sb.Append("  \u2699");

        _hoverTooltipFired = true;
        Vector2 mousePos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : Vector2.zero;
        OnTileHoverReady?.Invoke(sb.ToString(), mousePos);
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
        // Invalider l'état tooltip au changement LOD
        _faceToTile         = null;
        _hoverFaceCandidate = -1;
        _hoverStartTime     = -1f;
        if (_hoverTooltipFired)
        {
            _hoverTooltipFired = false;
            OnTileHoverCancelled?.Invoke();
        }

        var nextData = level == 1 ? _sphereDataHi : _sphereDataLo;

        // LOD haut : colorise avec res=2 seulement si les biomes n'ont pas encore été appliqués
        // (_lodHiBaseColored est mis à true dans FetchAndColorizeFromServer dès que les tuiles sont appliquées)
        if (level == 1 && !_lodHiBaseColored && !_lodHiColored && _cachedServerTiles != null && _cachedColorByType != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(nextData.faces, _cachedServerTiles, _cachedColorByType);
            ReapplyOverlays(nextData, _cachedServerTiles);
            _lodHiBaseColored = true;
        }
        else if (level == 1 && _lodHiBaseColored)
        {
            // Re-appliquer les overlays (ownership, states) sur les couleurs déjà présentes.
            // IMPORTANT : pas de condition !_lodHiColored — si FetchAndColorizeHiLod s'est terminé
            // avant FetchOwnershipOverlay/FetchStateOverlay (race condition), les overlays auraient
            // été appliqués avec des données null. On doit re-passer ici une fois les données reçues.
            ReapplyOverlays(nextData, _cachedServerTiles);
        }
        else if (level == 0 && _cachedServerTiles != null)
        {
            // Reapply overlays sur LOD bas — garantit que les tints récents (corp/état)
            // sont présents même si RefreshOwnershipOverlay s'est fait pendant LOD haut.
            ReapplyOverlays(nextData, _cachedServerTiles);
        }

        _sphereData       = nextData;
        _hoveredFaceId    = -1;
        _cachedMeshColors = (Color[])nextData.mesh.colors.Clone();

        _meshFilter.sharedMesh   = nextData.mesh;
        // Collider toujours sur LOD bas (492 faces → convex OK; 1962 faces → KO)
        _meshCollider.sharedMesh = _sphereDataLo.mesh;

        string label = level == 1 ? "HAUT" : "BAS";
        Debug.Log($"[PlanetSphereGoldberg] LOD → {label} ({nextData.faces.Length} faces) dist={cameraController?.OrbitDistance:F1}");

        if (debugLodVerbose)
            Debug.Log($"[LOD] ApplyLodLevel | level={level} | cachedTiles={_cachedServerTiles?.Length ?? 0} | ownershipTints={_ownershipTints?.Count ?? 0}");

        // Recalculer les frontières avec le nouveau mesh actif pour éviter le mismatch LOD.
        RebuildBorderLoops();

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
            BaseUrl, _activeBodyId, latDeg, lonDeg);

        using UnityWebRequest req = UnityWebRequest.Get(url);
        req.timeout = TimeoutSec;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            yield break;

        GoldbergTileState tile;
        try   { tile = JsonUtility.FromJson<GoldbergTileState>(req.downloadHandler.text); }
        catch { yield break; }

        if (string.IsNullOrEmpty(tile.tileId))
            yield break;

        LastClickedH3TileId = tile.tileId;

        // Enrichir avec les infos d'état (overlay politique)
        if (_tileToStateId != null && _tileToStateId.TryGetValue(tile.tileId, out string sid))
            tile.stateId = sid;
        if (_tileToStateName != null && _tileToStateName.TryGetValue(tile.tileId, out string sname))
            tile.stateName = sname;

        Debug.Log($"[PlanetSphereGoldberg] H3 tile : {tile.tileId} | {tile.terrainType} | eau={tile.waterRatio:F2} | t={tile.temperature:F1}°C | state={tile.stateName}");
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
