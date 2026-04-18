using System;
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
        public PlanetaryHexGrid.GridData             Grid;
        public GoldbergSphereGenerator.GoldbergMeshData SphereData;

        public CachedSphere(
            PlanetaryHexGrid.GridData grid,
            GoldbergSphereGenerator.GoldbergMeshData sphereData)
        {
            Grid       = grid;
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

    [Header("Cache")]
    [SerializeField] private bool cacheGeneratedProjections = true;

    // =========================================================
    // Runtime
    // =========================================================

    [Header("Hover")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 1f, 1f, 0.35f);

    private MeshFilter    _meshFilter;
    private MeshRenderer  _meshRenderer;
    private MeshCollider  _meshCollider;

    private PlanetaryHexGrid.GridData                _planetGrid;
    private GoldbergSphereGenerator.GoldbergMeshData _sphereData;

    // Hover state
    private int     _hoveredFaceId   = -1;
    private Color[] _cachedMeshColors;  // couleurs sans highlight, resync à chaque LoadPlanet

    // =========================================================
    // Propriétés pour PlanetTangentView
    // =========================================================

    /// <summary>Index de la dernière face GP cliquée (-1 si aucune).</summary>
    public int LastClickedFaceId { get; private set; } = -1;

    /// <summary>Centroïde 3D de la dernière tuile cliquée (magnitude = VisualRadius).</summary>
    public Vector3 LastClickedFaceCentroid { get; private set; } = Vector3.up * GoldbergSphereGenerator.VisualRadius;

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
        _meshCollider.convex = true;

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

        // Abonnement au bus de données planétaire (sync temps réel)
        PlanetaryHexGrid.OnPlanetDataChanged += OnPlanetDataChanged;
    }

    private void OnDestroy()
    {
        PlanetaryHexGrid.OnPlanetDataChanged -= OnPlanetDataChanged;
    }

    private void OnPlanetDataChanged(PlanetaryHexGrid.GridData grid)
    {
        if (!gameObject.activeInHierarchy) return;
        if (_sphereData.faces == null) return;
        RefreshColors(grid);
    }

    /// <summary>
    /// Met à jour les couleurs des faces GP depuis une grille planétaire mise à jour.
    /// Appelé automatiquement par OnPlanetDataChanged ou manuellement par ViewManager.
    /// </summary>
    public void RefreshColors(PlanetaryHexGrid.GridData grid)
    {
        if (_sphereData.faces == null || _sphereData.mesh == null) return;

        _planetGrid = grid;
        GoldbergFaceColorizer.Colorize(_sphereData.faces, _planetGrid);
        GoldbergSphereGenerator.ApplyFaceColors(_sphereData.mesh, _sphereData.faces, _sphereData.vertexFaceId);

        // Resync snapshot hover
        _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
        _hoveredFaceId    = -1;
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

        int      waterLevelKey = Mathf.RoundToInt(Mathf.Clamp(waterLevelOffset, -0.45f, 0.45f) * 1000f);
        CacheKey cacheKey      = new CacheKey(body.GetInstanceID(), coherenceOverride, waterLevelKey);

        if (cacheGeneratedProjections && SphereCache.TryGetValue(cacheKey, out CachedSphere cached))
        {
            _planetGrid = cached.Grid;
            _sphereData = cached.SphereData;
            Debug.Log($"[PlanetSphereGoldberg] Cache : {body.bodyName} | {_sphereData.faces.Length} tuiles");
        }
        else
        {
            _planetGrid = PlanetaryHexGrid.Generate(body, coherenceOverride, waterLevelOffset);
            _sphereData = GoldbergSphereGenerator.Generate(body);
            GoldbergFaceColorizer.Colorize(_sphereData.faces, _planetGrid);
            GoldbergSphereGenerator.ApplyFaceColors(_sphereData.mesh, _sphereData.faces, _sphereData.vertexFaceId);

            if (cacheGeneratedProjections)
                SphereCache[cacheKey] = new CachedSphere(_planetGrid, _sphereData);

            Debug.Log($"[PlanetSphereGoldberg] Généré : {body.bodyName} | {_sphereData.faces.Length} tuiles "
                    + $"| override={coherenceOverride} | eau={waterLevelOffset:+0.00;-0.00;0.00}");
        }

        _meshFilter.sharedMesh   = _sphereData.mesh;
        _meshCollider.sharedMesh = _sphereData.mesh;

        // Snapshot des couleurs de base (avant hover)
        _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
        _hoveredFaceId    = -1;

        // Atmosphère
        atmosphere?.ApplyBodyData(body);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale    = Vector3.one;
    }

    /// <summary>
    /// Retourne la HexCell Mercator correspondant aux coordonnées lat/lon normalisées [0–1].
    /// Délègue directement à PlanetaryHexGrid (aucune dépendance au mesh GP).
    /// </summary>
    public HexCell GetProjectedCell(float latitude, float longitude)
    {
        if (_planetGrid.Cells == null || _planetGrid.Cells.Length == 0)
            return null;
        return PlanetaryHexGrid.GetCellAt(
            _planetGrid.Cells, _planetGrid.Cols, _planetGrid.Rows, latitude, longitude);
    }

    public bool TryBuildProjectionSummary(out PlanetaryHexGrid.ProjectionDebugSummary summary)
    {
        return PlanetaryHexGrid.TryBuildSummary(_planetGrid, out summary);
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

        // Direction du centre de la sphère vers le point cliqué → coordonnées sphériques
        Vector3 dir = (hit.point - transform.position).normalized;

        float latDeg  = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lonDeg  = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
        float latNorm = (latDeg + 90f)  / 180f;
        float lonNorm = (lonDeg + 180f) / 360f;

        HexCell cell        = GetProjectedCell(latNorm, lonNorm);
        string  terrainName = cell?.terrain != null ? cell.terrain.displayName : "?";
        float   waterRatio  = cell != null ? cell.state.waterRatio : 0f;

        // Mémorise le centroïde de la tuile la plus proche pour PlanetTangentView
        int nearestFace = GoldbergSphereGenerator.FindNearestFaceId(_sphereData.faces, dir);
        if (nearestFace >= 0)
        {
            LastClickedFaceId       = nearestFace;
            LastClickedFaceCentroid = _sphereData.faces[nearestFace].centroid3D
                                      * GoldbergSphereGenerator.VisualRadius;
        }

        Debug.Log($"[PlanetSphereGoldberg] Clic → lat={latNorm:F3} lon={lonNorm:F3} "
                + $"| terrain={terrainName} | eau={waterRatio:F2}");

        OnRegionClicked?.Invoke(latNorm, lonNorm);
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
