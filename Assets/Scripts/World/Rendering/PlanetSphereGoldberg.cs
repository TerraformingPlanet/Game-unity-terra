using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    [SerializeField] private GameConfig config;

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
