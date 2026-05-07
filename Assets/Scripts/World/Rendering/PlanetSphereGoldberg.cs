using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
public partial class PlanetSphereGoldberg : MonoBehaviour
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
    [Tooltip("Utilise GET /tiles/adaptive (hémisphère visible res=2, hémisphère caché res=1) "
           + "au lieu de GET /tiles/lod?h3_resolution=2. Réduit le nombre de tuiles d'environ 40 %.")]
    [SerializeField] private bool useAdaptiveFetch = true;
    [SerializeField] private GameConfig config;

    // =========================================================
    // Runtime
    // =========================================================

    [Header("Hover")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("Délai en secondes avant l'apparition du tooltip au survol (LOD 1 seulement).")]
    [SerializeField] private float hoverTooltipDelay = 0.6f;

    [Header("Relief topographique")]
    [Tooltip("Déforme le mesh sphère selon l'altitude des tuiles H3 (données serveur requises).")]
    [SerializeField] private bool enableTopographicRelief = false;
    [Tooltip("Amplitude du déplacement en unités monde (VisualRadius=10). 0.5 = 5% de relief max.")]
    [SerializeField, Range(0f, 2f)] private float topographicDisplacementScale = 0.5f;
    [Tooltip("Affiche les murs latéraux des prismes océaniques : comble l'espace entre le fond et la surface d'eau, avec la couleur de chaque tile.")]
    [SerializeField] private bool enableWaterPrisms = true;
    [Tooltip("Intensité du gradient de rim entre tuiles adjacentes. 0 = tuiles plates, 1 = fusion totale.")]
    [SerializeField, Range(0f, 1f)] public float rimBlendStrength = 0.40f;
    [Tooltip("Distance RGB max pour qu'un voisin participe au blend. 0 = illimité. 0.45 = bloque eau↔terre, autorise terre↔terre.")]
    [SerializeField, Range(0f, 2f)] public float rimBlendMaxDelta = 0.45f;
    [Tooltip("Delta altitude (land - seaLevel) au-dessus duquel une arête côtière devient une falaise. Espace [-1,+1].")]
    [SerializeField, Range(0f, 1f)] private float cliffThreshold = 0.15f;

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

    // H3-exact mapping (populated after H3SphereBuilder.Build — replaces nearest-neighbour).
    private H3SphereBuilder.H3BuildResult        _h3Result;      // face↔tile exact mapping
    private Dictionary<string, GoldbergTileState> _cachedTileById; // tileId → tile for O(1) click lookup
    private int   _hoverFaceCandidate = -1;
    private float _hoverStartTime     = -1f;
    private bool  _hoverTooltipFired  = false;

    // H3 state
    private string      _activeBodyId = "";  // bodyId H3 du corps chargé — résolu par FetchAndColorizeFromServer
    private OrbitalBody _activeBody;         // référence conservée pour colorByType complet
    private DebugCoherenceOverride _activeCoherenceOverride;  // conservé pour bootstrap auto
    private float _activeWaterLevelOffset;                    // conservé pour bootstrap auto
    internal float ActiveWaterLevel { get; private set; } = 0f;  // waterLevel serveur [-1,1]
    internal float ServerWaterLevel { get; private set; } = 0f;  // waterLevel serveur brut (pour colorisation, indépendant du relief)

    internal void SetWaterLevel(float waterLevel)
    {
        ActiveWaterLevel = Mathf.Clamp(waterLevel, -1f, 1f);
    }

    private float[] _cachedFaceAltitudesLo;          // altitudes par face GP (LOD bas)
    private float[] _cachedFaceAltitudesHi;          // altitudes par face GP (LOD haut)
    private bool[]  _cachedFaceIsOceanLo;            // masque terrainType ocean (LOD bas)
    private bool[]  _cachedFaceIsInlandWaterLo;      // masque InlandWater / lacs (LOD bas)
    private bool[]  _cachedFaceIsOceanHi;            // masque terrainType ocean (LOD haut)
    private bool[]  _cachedFaceIsInlandWaterHi;      // masque InlandWater / lacs (LOD haut)
    private GameObject    _tilePrisms;               // mesh faces latérales des prismes hexagonaux (terres)
    private GameObject _lakeCaps;                 // mesh water caps pour les lacs intérieurs
    private GameObject _waterPrisms;              // mesh faces latérales des prismes océaniques (profondeur)

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

    // Sea-level server sync — debounce coroutine
    private Coroutine _waterLevelSyncCoroutine;

    // Zone overlay — multi-dimensional lens (6 dimensions)
    private Dictionary<string, Color>  _zoneTints;      // tileId → zone color for active dimension
    private Dictionary<string, string> _tileToZoneId;   // tileId → zoneId for active dimension
    private string                     _activeDimension; // "bio"|"admin"|"eco"|"military"|"cultural"|"scientific"
    private bool                       _zoneOverlayFetched;

    // =========================================================
    // Lens (filtres carte debug)
    // =========================================================

    /// <summary>
    /// Modes de visualisation en superposition sur la sphère.
    /// Normal = biomes standard. Elevation = gradient dénivelé [-1,+1], eau cachée.
    /// Zone* = overlay coloration par dimension de zone (multi-dimensionnel).
    /// </summary>
    public enum PlanetLensMode { Normal, Elevation, ZoneBio, ZoneAdmin, ZoneEco, ZoneMilitary, ZoneCultural, ZoneScientific }

    // Maps lens mode → API dimension string
    private static readonly Dictionary<PlanetLensMode, string> _lensToDimension = new()
    {
        { PlanetLensMode.ZoneBio,        "bio"        },
        { PlanetLensMode.ZoneAdmin,       "admin"      },
        { PlanetLensMode.ZoneEco,         "eco"        },
        { PlanetLensMode.ZoneMilitary,    "military"   },
        { PlanetLensMode.ZoneCultural,    "cultural"   },
        { PlanetLensMode.ZoneScientific,  "scientific" },
    };

    // Palette de couleurs canoniques par dimension (teinte de base + saturation)
    private static readonly Dictionary<string, Color> _dimensionBaseColor = new()
    {
        { "bio",        new Color(0.20f, 0.75f, 0.25f, 1f) }, // vert
        { "admin",      new Color(0.25f, 0.50f, 0.90f, 1f) }, // bleu
        { "eco",        new Color(0.95f, 0.80f, 0.10f, 1f) }, // jaune
        { "military",   new Color(0.85f, 0.20f, 0.20f, 1f) }, // rouge
        { "cultural",   new Color(0.65f, 0.20f, 0.85f, 1f) }, // violet
        { "scientific", new Color(0.15f, 0.85f, 0.90f, 1f) }, // cyan
    };

    /// <summary>Lens actif.</summary>
    public PlanetLensMode ActiveLens { get; private set; } = PlanetLensMode.Normal;

    /// <summary>
    /// Active ou désactive un lens de visualisation.
    /// Recolorise immédiatement tous les meshes depuis le cache.
    /// Sans effet si les tuiles ne sont pas encore chargées.
    /// Pour les modes Zone*, fetche les données serveur si nécessaire.
    /// </summary>
    public void SetLens(PlanetLensMode mode)
    {
        ActiveLens = mode;
        if (_cachedServerTiles == null || _cachedServerTiles.Length == 0) return;

        // Si c'est un lens de dimension zone, lancer le fetch (ou réutiliser le cache)
        if (_lensToDimension.TryGetValue(mode, out string dimension))
        {
            SetZoneLens(dimension);
            return;
        }

        // Pour Normal/Elevation : effacer le zone overlay
        if (mode == PlanetLensMode.Normal || mode == PlanetLensMode.Elevation)
        {
            _zoneTints = null;
            _tileToZoneId = null;
            _activeDimension = null;
            _zoneOverlayFetched = false;
        }

        var meshesToRefresh = new[]
        {
            (enableLod && _sphereDataLo.faces != null) ? _sphereDataLo : _sphereData,
            _sphereDataHi,
        };

        foreach (var data in meshesToRefresh)
        {
            if (data.faces == null || data.mesh == null) continue;
            ReapplyOverlays(data, _cachedServerTiles);
        }

        // Lens Elevation : masque les prismes océaniques pour lire les profondeurs
        if (_tilePrisms   != null) _tilePrisms.SetActive(mode != PlanetLensMode.Elevation);
        if (_waterPrisms  != null) _waterPrisms.SetActive(mode != PlanetLensMode.Elevation);

        if (_cachedMeshColors != null && _sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();

        Debug.Log($"[PlanetSphereGoldberg] Lens → {mode}");
    }

    /// <summary>
    /// Active un lens de dimension de zone. Fetche la data depuis le serveur si nécessaire,
    /// puis recolorise les tuiles par zoneId (même zone = même teinte).
    /// dimension: "bio"|"admin"|"eco"|"military"|"cultural"|"scientific"
    /// </summary>
    public void SetZoneLens(string dimension)
    {
        if (string.IsNullOrEmpty(_activeBodyId)) return;
        if (_activeDimension == dimension && _zoneOverlayFetched)
        {
            // Déjà chargé — juste réappliquer
            ApplyZoneOverlayToMeshes();
            return;
        }
        _activeDimension = dimension;
        _zoneOverlayFetched = false;
        _zoneTints = null;
        _tileToZoneId = null;
        StartCoroutine(FetchZoneOverlay(dimension));
    }

    /// <summary>Cycle Normal → Elevation → Normal. Retourne le mode actif.</summary>
    public PlanetLensMode CycleLens()
    {
        SetLens(ActiveLens == PlanetLensMode.Normal ? PlanetLensMode.Elevation : PlanetLensMode.Normal);
        return ActiveLens;
    }

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

    /// <summary>bodyId du corps H3 actuellement chargé (vide avant le premier fetch).</summary>
    public string ActiveBodyId => _activeBodyId;

    /// <summary>Données mesh GP courant (faces, vertexFaceId, mesh).</summary>
    public GoldbergSphereGenerator.GoldbergMeshData SphereData => _sphereData;

    // =========================================================
    // API publique — même interface que PlanetSphere
    // =========================================================

    // (Awake, OnDestroy → PlanetSphereGoldbergSetup.cs)
    // (CreateWaterCaps, depth prism visibility → PlanetSphereGoldbergWater.cs)
    // (Update, GetFaceRadius, HideFaceOnSphere, RestoreFaceOnSphere → PlanetSphereGoldbergInput.cs)
    // (EnsureCloudLayer, ClearSphereCache → PlanetSphereGoldbergSetup.cs)

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

        _stateOverlayFetched = false;
        _ownershipOverlayFetched = false;

#pragma warning disable CS0618
        CacheKey cacheKey = new CacheKey(body.GetInstanceID(), coherenceOverride, 0);
#pragma warning restore CS0618

        BuildOrFetchSphereData(body, cacheKey);

        Color baseColorForLod = ExtractBaseColor(body);
        if (enableLod)
            InitializeLodMeshes(body, baseColorForLod);

        ApplyMeshToRenderer();

        _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
        _hoveredFaceId    = -1;

        atmosphere?.ApplyBodyData(body);
        cloudLayer?.ApplyBodyData(body);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale    = Vector3.one;

        _activeBody = body;
        _activeCoherenceOverride = coherenceOverride;
        _activeWaterLevelOffset  = waterLevelOffset;
        if (fetchServerTilesOnLoad)
            StartCoroutine(FetchAndColorizeFromServer(body.bodyName, coherenceOverride, waterLevelOffset));
    }

    private void BuildOrFetchSphereData(OrbitalBody body, CacheKey cacheKey)
    {
        if (cacheGeneratedProjections && SphereCache.TryGetValue(cacheKey, out CachedSphere cached))
        {
            _sphereData = cached.SphereData;
            Debug.Log($"[PlanetSphereGoldberg] Cache GP : {body.bodyName} | {_sphereData.faces.Length} tuiles");
            return;
        }

        _sphereData = GoldbergSphereGenerator.Generate(body);

        Color baseColor = ExtractBaseColor(_activeBody ?? body);
        for (int fi = 0; fi < _sphereData.faces.Length; fi++)
            _sphereData.faces[fi].color = baseColor;
        GoldbergSphereGenerator.ApplyFaceColors(_sphereData.mesh, _sphereData.faces, _sphereData.vertexFaceId);

        if (cacheGeneratedProjections)
            SphereCache[cacheKey] = new CachedSphere(_sphereData);

        Debug.Log($"[PlanetSphereGoldberg] Généré GP : {body.bodyName} | {_sphereData.faces.Length} tuiles");
    }

    private static Color ExtractBaseColor(OrbitalBody body)
    {
        if (body == null) return Color.white;
        Color c = body.displayColor;
        if (body.layers != null)
        {
            foreach (LayerZone layer in body.layers)
                if (layer?.biomes != null && layer.biomes.Length > 0 && layer.biomes[0] != null)
                { c = layer.biomes[0].color; break; }
        }
        return c;
    }

    private void InitializeLodMeshes(OrbitalBody body, Color baseColor)
    {
        _lodLoDivisions = GoldbergSphereGenerator.ComputeDivisions(body.radius);
        _lodHiDivisions = lodNearExtraDivisions > 0
            ? _lodLoDivisions + lodNearExtraDivisions
            : Mathf.Min(_lodLoDivisions * 2, 15);

        _sphereDataLo = _sphereData;
        _sphereDataHi = GoldbergSphereGenerator.GenerateWithDivisions(_lodHiDivisions);
        _currentLodLevel = -1;
        _lodHiColored     = false;
        _lodHiFetching    = false;
        _lodHiBaseColored = false;

        for (int fi = 0; fi < _sphereDataHi.faces.Length; fi++)
            _sphereDataHi.faces[fi].color = baseColor;
        GoldbergSphereGenerator.ApplyFaceColors(_sphereDataHi.mesh, _sphereDataHi.faces, _sphereDataHi.vertexFaceId);

        Debug.Log($"[PlanetSphereGoldberg] LOD : bas={_lodLoDivisions} ({_sphereDataLo.faces.Length} faces) | haut={_lodHiDivisions} ({_sphereDataHi.faces.Length} faces)");
    }

    private void ApplyMeshToRenderer()
    {
        if (enableLod && startAtMaxResolution)
        {
            _currentLodLevel = 1;
            _sphereData = _sphereDataHi;
            _meshFilter.sharedMesh   = _sphereDataHi.mesh;
            _meshCollider.sharedMesh = _sphereDataHi.mesh;
        }
        else
        {
            _meshFilter.sharedMesh   = _sphereData.mesh;
            _meshCollider.sharedMesh = _sphereData.mesh;
        }
    }

    /// <summary>
    /// Ré-interroge le serveur pour mettre à jour les altitudes, la colorisation et les water caps.
    /// À appeler quand la simulation a modifié des altitudes côté serveur (tick terraforming,
    /// changement de niveau de mer, etc.).
    /// Sans effet si aucune planète n'est chargée.
    /// </summary>
    public void RefreshTilesFromServer()
    {
        if (_activeBody == null) return;
        StartCoroutine(FetchAndColorizeFromServer(
            _activeBody.bodyName,
            _activeCoherenceOverride,
            _activeWaterLevelOffset));
    }
}
