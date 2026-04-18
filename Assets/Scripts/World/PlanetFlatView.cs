using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vue planétaire plate — layout H3 de toute la surface d'une planète.
///
/// Crée un enfant GameObject portant PlanetFlatMesh pour le rendu et le collider.
/// Le mesh est construit à partir des tuiles H3 fournies par le serveur de simulation.
///
/// Interface publique :
///   - LoadPlanetFromH3(tiles, colorByType) : construit et affiche le mesh H3
///   - GetH3Tile(gridIndex)                 : tuile H3 à l'index cliqué
///   - GridIndexToLatLon(gridIndex)         : lat/lon pour OnRegionClicked
///   - event OnRegionClicked               : déclenché par PlanetFlatInput
///
/// Prérequis Unity :
///   - Ajouter ce composant sur un GameObject enfant de PlanetRoot.
///   - Assigner un matériau vertex color (Terraformation/HexVertexColor) en Inspector.
/// </summary>
public class PlanetFlatView : MonoBehaviour
{
    // =========================================================
    // Event — même signature que PlanetSphereGoldberg
    // =========================================================

    /// <summary>latNorm [0–1], lonNorm [0–1].</summary>
    public event Action<float, float> OnRegionClicked;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Rendu")]
    [Tooltip("Matériau utilisant le shader Terraformation/HexVertexColor.")]
    [SerializeField] private Material flatMaterial;

    [Header("Hover")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("Minimap")]
    [SerializeField] private MinimapController minimapController;

    // =========================================================
    // Runtime
    // =========================================================

    private PlanetFlatMesh  _flatMesh;
    private PlanetFlatInput _flatInput;
    private GameObject      _meshObject;
    private bool            _initialized;

    private GoldbergTileState[]              _h3Tiles;
    private Dictionary<TerrainType, Color>   _colorByType;
    private Color[]                          _cachedColors;   // snapshot sans hover

    public bool IsLoaded => _h3Tiles != null && _h3Tiles.Length > 0;

    /// <summary>Référence au GameObject portant le MeshCollider (pour PlanetFlatInput).</summary>
    public GameObject MeshObject => _meshObject;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        EnsureInitialized();
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Construit et affiche le mesh plat depuis les tuiles H3 du serveur.
    /// Appelé par ViewManager après réception de l'événement OnH3TilesReady.
    /// </summary>
    public void LoadPlanetFromH3(GoldbergTileState[] tiles, Dictionary<TerrainType, Color> colorByType)
    {
        EnsureInitialized();

        if (tiles == null || tiles.Length == 0)
        {
            Debug.LogWarning("[PlanetFlatView] Aucune tuile H3 fournie.");
            return;
        }

        _h3Tiles     = tiles;
        _colorByType = colorByType;
        _flatMesh.TriangulateH3(tiles, colorByType);

        // Snapshot des couleurs de base (avant hover)
        var mf = _meshObject.GetComponent<MeshFilter>();
        _cachedColors = mf != null && mf.sharedMesh != null
            ? (Color[])mf.sharedMesh.colors.Clone()
            : null;

        // Centre le pivot du mesh
        Bounds b = _flatMesh.GetBounds();
        _meshObject.transform.localPosition = new Vector3(-b.center.x, 0f, -b.center.z);

        // Initialise la minimap
        if (minimapController != null)
            minimapController.Setup(_flatMesh, _meshObject.transform);

        Debug.Log($"[PlanetFlatView] Chargé H3 : {tiles.Length} tuiles");
    }

    // =========================================================
    // Hover (appelé par PlanetFlatInput)
    // =========================================================

    public void SetHover(int gridIndex)
    {
        EnsureInitialized();
        if (_cachedColors == null || gridIndex < 0) return;
        var mf = _meshObject.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        Color[] colors = mf.sharedMesh.colors;
        int start = gridIndex * PlanetFlatMesh.VerticesPerHex;
        int end   = start + PlanetFlatMesh.VerticesPerHex;
        if (end > colors.Length) return;

        for (int i = start; i < end; i++)
            colors[i] = Color.Lerp(_cachedColors[i], Color.white, hoverTintColor.a);

        mf.sharedMesh.colors = colors;
    }

    public void ClearHover(int gridIndex)
    {
        EnsureInitialized();
        if (_cachedColors == null || gridIndex < 0) return;
        var mf = _meshObject.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        Color[] colors = mf.sharedMesh.colors;
        int start = gridIndex * PlanetFlatMesh.VerticesPerHex;
        int end   = start + PlanetFlatMesh.VerticesPerHex;
        if (end > colors.Length) return;

        for (int i = start; i < end; i++)
            colors[i] = _cachedColors[i];

        mf.sharedMesh.colors = colors;
    }

    // =========================================================
    // Utilitaires pour PlanetFlatInput
    // =========================================================

    public int GetGridIndexFromTriangle(int triangleIndex)
    {
        EnsureInitialized();
        return _flatMesh != null ? _flatMesh.GetGridIndexFromTriangle(triangleIndex) : -1;
    }

    /// <summary>Retourne les coordonnées lat/lon normalisées [0–1] de la tuile H3 à cet index.</summary>
    public (float latNorm, float lonNorm) GridIndexToLatLon(int gridIndex)
    {
        if (!IsLoaded || gridIndex < 0 || gridIndex >= _h3Tiles.Length)
            return (0.5f, 0.5f);
        return (_h3Tiles[gridIndex].latNorm, _h3Tiles[gridIndex].lonNorm);
    }

    /// <summary>Retourne la tuile H3 à l'index fourni (null si invalide).</summary>
    public GoldbergTileState? GetH3Tile(int gridIndex)
    {
        if (!IsLoaded || gridIndex < 0 || gridIndex >= _h3Tiles.Length)
            return null;
        return _h3Tiles[gridIndex];
    }

    /// <summary>Méthode de compatibilité — toujours null après migration H3.</summary>
    public HexCell GetCell(int gridIndex) => null;

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        if (_meshObject == null)
        {
            Transform existing = transform.Find("FlatMeshRenderer");
            _meshObject = existing != null ? existing.gameObject : new GameObject("FlatMeshRenderer");
            _meshObject.transform.SetParent(transform, false);
        }

        int minimapLayer = LayerMask.NameToLayer("MinimapOnly");
        if (minimapLayer >= 0)
            _meshObject.layer = minimapLayer;

        _flatMesh = _meshObject.GetComponent<PlanetFlatMesh>();
        if (_flatMesh == null)
            _flatMesh = _meshObject.AddComponent<PlanetFlatMesh>();

        var meshRenderer = _meshObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            if (flatMaterial != null)
            {
                meshRenderer.sharedMaterial = flatMaterial;
            }
            else if (meshRenderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Terraformation/HexVertexColor");
                if (shader != null)
                    meshRenderer.material = new Material(shader);
                else
                    Debug.LogWarning("[PlanetFlatView] Shader 'Terraformation/HexVertexColor' introuvable.");
            }
        }

        _flatInput = gameObject.GetComponent<PlanetFlatInput>();
        if (_flatInput == null)
            _flatInput = gameObject.AddComponent<PlanetFlatInput>();

        _flatInput.OnRegionClicked -= HandleRegionClicked;
        _flatInput.OnRegionClicked += HandleRegionClicked;

        _initialized = true;
    }

    private void HandleRegionClicked(float lat, float lon)
    {
        OnRegionClicked?.Invoke(lat, lon);
    }
}
