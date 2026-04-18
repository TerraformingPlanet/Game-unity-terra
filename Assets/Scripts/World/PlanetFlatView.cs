using System;
using UnityEngine;

/// <summary>
/// Vue planétaire plate — projection Mercator de toute la surface d'une planète.
///
/// Crée un enfant GameObject portant PlanetFlatMesh pour le rendu et le collider.
/// S'abonne à PlanetaryHexGrid.OnPlanetDataChanged pour se rafraîchir en temps réel
/// quand la simulation modifie des cellules.
///
/// Interface publique identique à PlanetSphereGoldberg pour le ViewManager :
///   - LoadPlanet(grid)        : charge et rend la grille
///   - RefreshColors(grid)     : met à jour les couleurs sans retrianguler
///   - event OnRegionClicked   : déclenché par PlanetFlatInput
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

    private PlanetFlatMesh _flatMesh;
    private PlanetFlatInput _flatInput;
    private GameObject _meshObject;

    private PlanetaryHexGrid.GridData _currentGrid;
    private Color[] _cachedColors;     // snapshot des couleurs sans hover

    public bool IsLoaded  => _currentGrid.Cells != null && _currentGrid.Cells.Length > 0;

    /// <summary>Référence au GameObject portant le MeshCollider (pour PlanetFlatInput).</summary>
    public GameObject MeshObject => _meshObject;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        // Crée un enfant dédié au mesh (pour isolation du collider)
        _meshObject = new GameObject("FlatMeshRenderer");
        _meshObject.transform.SetParent(transform, false);

        // Isole le mesh Mercator sur le layer MinimapOnly (invisible à la Main Camera)
        int minimapLayer = LayerMask.NameToLayer("MinimapOnly");
        if (minimapLayer >= 0) _meshObject.layer = minimapLayer;

        _flatMesh = _meshObject.AddComponent<PlanetFlatMesh>();

        // Matériau
        var mr = _meshObject.GetComponent<UnityEngine.Renderer>();
        if (flatMaterial != null)
        {
            mr.sharedMaterial = flatMaterial;
        }
        else
        {
            Shader s = Shader.Find("Terraformation/HexVertexColor");
            if (s != null)
                mr.material = new Material(s);
            else
                Debug.LogWarning("[PlanetFlatView] Shader 'Terraformation/HexVertexColor' introuvable.");
        }

        // Input — sur ce même GameObject (pas sur _meshObject)
        _flatInput = gameObject.GetComponent<PlanetFlatInput>();
        if (_flatInput == null)
            _flatInput = gameObject.AddComponent<PlanetFlatInput>();

        _flatInput.OnRegionClicked += (lat, lon) => OnRegionClicked?.Invoke(lat, lon);

        // Abonnement au bus de données planétaire
        PlanetaryHexGrid.OnPlanetDataChanged += OnPlanetDataChanged;
    }

    private void OnDestroy()
    {
        PlanetaryHexGrid.OnPlanetDataChanged -= OnPlanetDataChanged;
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Charge et affiche la grille planétaire en vue Mercator.
    /// Appelé par ViewManager.ShowProjectedPlanet() ou TogglePlanetView().
    /// </summary>
    public void LoadPlanet(PlanetaryHexGrid.GridData grid)
    {
        if (grid.Cells == null || grid.Cells.Length == 0)
        {
            Debug.LogWarning("[PlanetFlatView] GridData vide.");
            return;
        }

        _currentGrid = grid;
        _flatMesh.Triangulate(grid.Cells, grid.Cols, grid.Rows);

        // Snapshot des couleurs de base (avant hover)
        var mf = _meshObject.GetComponent<MeshFilter>();
        _cachedColors = mf != null && mf.sharedMesh != null
            ? (Color[])mf.sharedMesh.colors.Clone()
            : null;

        // Centrer le pivot du mesh
        CenterMesh(grid.Cols, grid.Rows);

        // Initialise la minimap après centrage (bounds correctes)
        if (minimapController != null)
            minimapController.Setup(_flatMesh, _meshObject.transform);

        Debug.Log($"[PlanetFlatView] Chargé : {grid.Cols}×{grid.Rows} = {grid.Cells.Length} tuiles");
    }

    /// <summary>
    /// Met à jour les couleurs de vertex depuis une grille mise à jour (simulation tick).
    /// Appelé par le bus OnPlanetDataChanged si la vue est active.
    /// </summary>
    public void RefreshColors(PlanetaryHexGrid.GridData grid)
    {
        if (!IsLoaded) return;
        _currentGrid = grid;
        _flatMesh.RefreshColors(grid.Cells);

        // Resync du snapshot hover
        var mf = _meshObject.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            _cachedColors = (Color[])mf.sharedMesh.colors.Clone();
    }

    // =========================================================
    // Hover (appelé par PlanetFlatInput)
    // =========================================================

    public void SetHover(int gridIndex)
    {
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
        => _flatMesh != null ? _flatMesh.GetGridIndexFromTriangle(triangleIndex) : -1;

    /// <summary>Convertit un gridIndex en coordonnées lat/lon normalisées [0–1].</summary>
    public (float latNorm, float lonNorm) GridIndexToLatLon(int gridIndex)
    {
        if (!IsLoaded || gridIndex < 0 || gridIndex >= _currentGrid.Cells.Length)
            return (0.5f, 0.5f);

        HexCell cell = _currentGrid.Cells[gridIndex];
        float lonNorm = (cell.Q + 0.5f) / _currentGrid.Cols;
        float latNorm = (cell.R + 0.5f) / _currentGrid.Rows;
        return (latNorm, lonNorm);
    }

    public HexCell GetCell(int gridIndex)
    {
        if (!IsLoaded || gridIndex < 0 || gridIndex >= _currentGrid.Cells.Length)
            return null;
        return _currentGrid.Cells[gridIndex];
    }

    // =========================================================
    // Bus de données planétaire
    // =========================================================

    private void OnPlanetDataChanged(PlanetaryHexGrid.GridData grid)
    {
        // Ne rafraîchit que si la vue est active et que la grille correspond
        if (!gameObject.activeInHierarchy) return;
        if (grid.Cols != _currentGrid.Cols || grid.Rows != _currentGrid.Rows) return;
        RefreshColors(grid);
    }

    // =========================================================
    // Helpers
    // =========================================================

    private void CenterMesh(int cols, int rows)
    {
        // Utilise les bounds réelles du mesh (la hauteur varie selon la géométrie hex)
        Bounds b = _flatMesh.GetBounds();
        _meshObject.transform.localPosition = new Vector3(-b.center.x, 0f, -b.center.z);
    }
}
