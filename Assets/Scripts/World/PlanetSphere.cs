using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Représente la vue projetée d'une planète en vue planétaire.
///
/// Responsabilités :
///   - Recevoir un CelestialBodyData via LoadPlanet()
///   - Générer la grille planétaire basse résolution (PlanetaryHexGrid)
///   - Générer la texture équirectangulaire (PlanetTextureGenerator)
///   - Appliquer la texture au MeshRenderer
///   - Détecter les clics sur la carte projetée → convertir textureCoord UV → lat/lon normalisés
///     → émettre OnRegionClicked
///
/// Prérequis Unity :
///   - GameObject avec un MeshFilter + MeshRenderer + Collider
///   - Le composant remplace le mesh par une projection rectangulaire 2:1 sur le plan XZ.
///   - La caméra principale doit avoir un PhysicsRaycaster pour que OnMouseDown fonctionne.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Collider))]
public class PlanetSphere : MonoBehaviour
{
    private readonly struct ProjectionCacheKey
    {
        public ProjectionCacheKey(int bodyId, DebugCoherenceOverride coherenceOverride)
        {
            BodyId = bodyId;
            CoherenceOverride = coherenceOverride;
            WaterLevelKey = 0;
        }

        public ProjectionCacheKey(int bodyId, DebugCoherenceOverride coherenceOverride, int waterLevelKey)
        {
            BodyId = bodyId;
            CoherenceOverride = coherenceOverride;
            WaterLevelKey = waterLevelKey;
        }

        public int BodyId { get; }
        public DebugCoherenceOverride CoherenceOverride { get; }
        public int WaterLevelKey { get; }
    }

    private sealed class CachedProjection
    {
        public CachedProjection(PlanetaryHexGrid.GridData grid, Texture2D texture)
        {
            Grid = grid;
            Texture = texture;
        }

        public PlanetaryHexGrid.GridData Grid { get; }
        public Texture2D Texture { get; }
    }

    private static readonly Dictionary<ProjectionCacheKey, CachedProjection> ProjectionCache = new Dictionary<ProjectionCacheKey, CachedProjection>();

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

    [Header("Projection")]
    [Tooltip("Largeur world de la carte projetée (aspect 2:1)")]
    [SerializeField] private float projectionWidth = 120f;
    [SerializeField] private bool cacheGeneratedProjections = true;

    [Header("Overlay méridiens")]
    [SerializeField] private bool showMeridians = true;
    [SerializeField] private int meridianCount = 12;
    [SerializeField] private float meridianLineWidth = 0.2f;
    [SerializeField] private float meridianOverlayHeight = 0.05f;
    [SerializeField] private Color meridianColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private Color primeMeridianColor = new Color(1f, 0.95f, 0.6f, 0.75f);

    [Header("Overlay parallèles")]
    [SerializeField] private bool showParallels = true;
    [SerializeField] private int parallelCount = 7;
    [SerializeField] private float parallelLineWidth = 0.16f;
    [SerializeField] private Color parallelColor = new Color(0.85f, 0.95f, 1f, 0.28f);
    [SerializeField] private Color equatorColor = new Color(1f, 0.7f, 0.55f, 0.7f);

    // =========================================================
    // Runtime
    // =========================================================

    private MeshRenderer _meshRenderer;
    private MeshFilter   _meshFilter;
    private MeshCollider _meshCollider;
    private Texture2D    _currentTexture;
    private PlanetaryHexGrid.GridData _planetGrid;
    private Mesh         _projectionMesh;
    private Transform    _meridianRoot;
    private Material     _overlayMaterial;
    private bool         _currentTextureOwnedLocally;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        _meshFilter = GetComponent<MeshFilter>();
        _meshCollider = GetComponent<MeshCollider>();

        if (_meshCollider == null)
        {
            Collider existingCollider = GetComponent<Collider>();
            if (existingCollider != null)
                Destroy(existingCollider);

            _meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        BuildProjectionMesh();
    }

    // =========================================================
    // API publique — appelée par ViewManager
    // =========================================================

    /// <summary>
    /// Charge et affiche la planète : génère la grille planétaire + texture.
    /// </summary>
    public void LoadPlanet(OrbitalBody body,
                           DebugCoherenceOverride coherenceOverride = DebugCoherenceOverride.None,
                           float waterLevelOffset = 0f)
    {
        if (body == null)
        {
            Debug.LogError("[PlanetSphere] OrbitalBody manquant.");
            return;
        }

        ReleaseCurrentTexture();

        int waterLevelKey = Mathf.RoundToInt(Mathf.Clamp(waterLevelOffset, -0.45f, 0.45f) * 1000f);
        ProjectionCacheKey cacheKey = new ProjectionCacheKey(body.GetInstanceID(), coherenceOverride, waterLevelKey);
        if (cacheGeneratedProjections && ProjectionCache.TryGetValue(cacheKey, out CachedProjection cachedProjection))
        {
            _planetGrid = cachedProjection.Grid;
            _currentTexture = cachedProjection.Texture;
            _currentTextureOwnedLocally = false;
            Debug.Log($"[PlanetSphere] Projection chargée depuis le cache : {body.bodyName} | override={coherenceOverride} | eau={waterLevelOffset:+0.00;-0.00;0.00}");
        }
        else
        {
            _planetGrid = PlanetaryHexGrid.Generate(body, coherenceOverride, waterLevelOffset);
            _currentTexture = PlanetTextureGenerator.Generate(_planetGrid);
            _currentTextureOwnedLocally = true;

            if (cacheGeneratedProjections && _currentTexture != null)
            {
                ProjectionCache[cacheKey] = new CachedProjection(_planetGrid, _currentTexture);
                _currentTextureOwnedLocally = false;
            }

            Debug.Log($"[PlanetSphere] Projection générée : {body.bodyName} | override={coherenceOverride} | eau={waterLevelOffset:+0.00;-0.00;0.00}");
        }

        // Application au matériau (instance, pas l'original partagé)
        if (_currentTexture != null)
            _currentTexture.filterMode = FilterMode.Point;

        _meshRenderer.material.SetTexture(textureProperty, _currentTexture);
        if (_meshRenderer.material.HasProperty("_BaseColor"))
            _meshRenderer.material.SetColor("_BaseColor", Color.white);
        if (_meshRenderer.material.HasProperty("_Color"))
            _meshRenderer.material.SetColor("_Color", Color.white);

        transform.localRotation = Quaternion.identity;
        transform.localPosition = Vector3.zero;
        transform.localScale = Vector3.one;

        Debug.Log($"[PlanetSphere] Planète affichée : {body.bodyName} | override={coherenceOverride} | eau={waterLevelOffset:+0.00;-0.00;0.00}");
    }

    public HexCell GetProjectedCell(float latitude, float longitude)
    {
        if (_planetGrid.Cells == null || _planetGrid.Cells.Length == 0)
            return null;

        return PlanetaryHexGrid.GetCellAt(_planetGrid.Cells, _planetGrid.Cols, _planetGrid.Rows, latitude, longitude);
    }

    public bool TryBuildProjectionSummary(out PlanetaryHexGrid.ProjectionDebugSummary summary)
    {
        return PlanetaryHexGrid.TryBuildSummary(_planetGrid, out summary);
    }

    [ContextMenu("Clear Projection Cache")]
    public void ClearProjectionCache()
    {
        ClearProjectionCacheInternal();
    }

    // =========================================================
    // Détection de clic
    // =========================================================

    private void OnMouseDown()
    {
        if (Camera.main == null || Mouse.current == null)
            return;

        if (UIEventSystemUtility.IsPointerOverUI())
            return;

        // Raycast depuis la souris vers la sphère pour récupérer les UV
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (!Physics.Raycast(ray, out RaycastHit hit))
            return;

        // hit.textureCoord : UV de la sphère Unity (x = longitude, y = latitude)
        float lonNorm = hit.textureCoord.x;
        float latNorm = hit.textureCoord.y;
        HexCell projectedCell = GetProjectedCell(latNorm, lonNorm);
        string terrainName = projectedCell?.terrain != null ? projectedCell.terrain.displayName : "?";
        float waterRatio = projectedCell != null ? projectedCell.state.waterRatio : 0f;

        Debug.Log($"[PlanetSphere] Clic → lat={latNorm:F3} lon={lonNorm:F3} | terrain={terrainName} | eau={waterRatio:F2}");
        OnRegionClicked?.Invoke(latNorm, lonNorm);
    }

    // =========================================================
    // Cleanup
    // =========================================================

    private void OnDestroy()
    {
        ReleaseCurrentTexture();

        if (_projectionMesh != null)
            Destroy(_projectionMesh);

        if (_overlayMaterial != null)
            Destroy(_overlayMaterial);
    }

    private void ReleaseCurrentTexture()
    {
        if (_currentTexture != null && _currentTextureOwnedLocally)
            Destroy(_currentTexture);

        _currentTexture = null;
        _currentTextureOwnedLocally = false;
    }

    private static void ClearProjectionCacheInternal()
    {
        foreach (CachedProjection cachedProjection in ProjectionCache.Values)
        {
            if (cachedProjection?.Texture != null)
                Destroy(cachedProjection.Texture);
        }

        ProjectionCache.Clear();
        Debug.Log("[PlanetSphere] Cache des projections vidé.");
    }

    private void BuildProjectionMesh()
    {
        float halfWidth = projectionWidth * 0.5f;
        float halfHeight = projectionWidth * 0.25f;

        if (_projectionMesh == null)
            _projectionMesh = new Mesh { name = "Planet Projection Mesh" };
        else
            _projectionMesh.Clear();

        _projectionMesh.vertices = new[]
        {
            new Vector3(-halfWidth, 0f, -halfHeight),
            new Vector3( halfWidth, 0f, -halfHeight),
            new Vector3(-halfWidth, 0f,  halfHeight),
            new Vector3( halfWidth, 0f,  halfHeight)
        };
        _projectionMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        _projectionMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        _projectionMesh.RecalculateNormals();
        _projectionMesh.RecalculateBounds();

        _meshFilter.sharedMesh = _projectionMesh;
        _meshCollider.sharedMesh = null;
        _meshCollider.sharedMesh = _projectionMesh;

        BuildMeridianOverlay(halfWidth, halfHeight);
    }

    private void BuildMeridianOverlay(float halfWidth, float halfHeight)
    {
        if (_meridianRoot == null)
        {
            GameObject root = new GameObject("MeridianOverlay");
            root.transform.SetParent(transform, false);
            _meridianRoot = root.transform;
        }

        for (int i = _meridianRoot.childCount - 1; i >= 0; i--)
            Destroy(_meridianRoot.GetChild(i).gameObject);

        if (!showMeridians || meridianCount < 2)
        {
            BuildParallelOverlay(halfWidth, halfHeight);
            return;
        }

        if (_overlayMaterial == null)
            _overlayMaterial = new Material(Shader.Find("Sprites/Default"));

        int divisions = meridianCount;
        for (int i = 0; i < divisions; i++)
        {
            float t = divisions == 1 ? 0.5f : (float)i / (divisions - 1);
            float x = Mathf.Lerp(-halfWidth, halfWidth, t);
            bool isPrimeMeridian = Mathf.Abs(t - 0.5f) < 0.0001f;

            GameObject lineObject = new GameObject(isPrimeMeridian ? "PrimeMeridian" : $"Meridian_{i}");
            lineObject.transform.SetParent(_meridianRoot, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 2;
            line.startWidth = meridianLineWidth;
            line.endWidth = meridianLineWidth;
            line.numCapVertices = 2;
            line.material = _overlayMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;

            Color lineColor = isPrimeMeridian ? primeMeridianColor : meridianColor;
            line.startColor = lineColor;
            line.endColor = lineColor;

            line.SetPosition(0, new Vector3(x, meridianOverlayHeight, -halfHeight));
            line.SetPosition(1, new Vector3(x, meridianOverlayHeight, halfHeight));
        }

        BuildParallelOverlay(halfWidth, halfHeight);
    }

    private void BuildParallelOverlay(float halfWidth, float halfHeight)
    {
        if (!showParallels || parallelCount < 2 || _meridianRoot == null)
            return;

        if (_overlayMaterial == null)
            _overlayMaterial = new Material(Shader.Find("Sprites/Default"));

        int divisions = parallelCount;
        for (int i = 0; i < divisions; i++)
        {
            float t = divisions == 1 ? 0.5f : (float)i / (divisions - 1);
            float z = Mathf.Lerp(-halfHeight, halfHeight, t);
            bool isEquator = Mathf.Abs(t - 0.5f) < 0.0001f;

            GameObject lineObject = new GameObject(isEquator ? "Equator" : $"Parallel_{i}");
            lineObject.transform.SetParent(_meridianRoot, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 2;
            line.startWidth = parallelLineWidth;
            line.endWidth = parallelLineWidth;
            line.numCapVertices = 2;
            line.material = _overlayMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;

            Color lineColor = isEquator ? equatorColor : parallelColor;
            line.startColor = lineColor;
            line.endColor = lineColor;

            line.SetPosition(0, new Vector3(-halfWidth, meridianOverlayHeight, z));
            line.SetPosition(1, new Vector3(halfWidth, meridianOverlayHeight, z));
        }
    }
}
