using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Génère un mesh 2D en projection Mercator (grille hex flat) pour toute la surface d'une planète.
///
/// Chaque cellule HexCell de la PlanetaryHexGrid est rendue comme un hexagone flat-top
/// dans le plan XZ (Y=0), positionné selon ses coordonnées Mercator normalisées [0–1].
/// Les couleurs de vertex sont identiques à celles de HexMesh.cs (logique GetVisualColor partagée).
///
/// Utilisé par PlanetFlatView comme couche de rendu de la vue Mercator togglable.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class PlanetFlatMesh : MonoBehaviour
{
    // Nombre de vertices par hex : 6 triangles × 3 sommets = 18
    public const int VerticesPerHex = 18;

    // Echelle du mesh dans le plan XZ (largeur totale = FlatMapWidth, hauteur = FlatMapHeight)
    public const float FlatMapWidth  = 192f;   // cols × cellWidth ≈ 96 × 2 = 192 unités
    public const float FlatMapHeight = 96f;    // rows × cellHeight ≈ 48 × 2 = 96 unités

    private Mesh         _mesh;
    private MeshCollider _meshCollider;

    private int _lastCols;
    private int _lastRows;

    private readonly List<Vector3> _vertices  = new();
    private readonly List<int>     _triangles = new();
    private readonly List<Color>   _colors    = new();

    // Tableau triangleIndex → gridIndex (pour GetCellFromTriangleIndex)
    private int[] _triangleToCell;

    private void Awake()
    {
        _mesh = new Mesh { name = "Planet Flat Mesh" };
        GetComponent<MeshFilter>().mesh = _mesh;
        _meshCollider = GetComponent<MeshCollider>();
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>Construit ou reconstruit le mesh complet depuis la grille planétaire.</summary>
    public void Triangulate(HexCell[] cells, int cols, int rows)
    {
        _mesh.Clear();
        _vertices.Clear();
        _triangles.Clear();
        _colors.Clear();

        _lastCols = cols;
        _lastRows = rows;

        int totalTris = cells.Length * 6;   // 6 triangles par hex
        _triangleToCell = new int[totalTris];

        foreach (HexCell cell in cells)
            TriangulateCell(cell, cols, rows);

        _mesh.vertices  = _vertices.ToArray();
        _mesh.triangles = _triangles.ToArray();
        _mesh.colors    = _colors.ToArray();
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        _meshCollider.sharedMesh = _mesh;
    }

    /// <summary>
    /// Met à jour uniquement les couleurs de vertex sans retrianguler.
    /// Peut être appelé chaque frame si la simulation modifie des cellules.
    /// </summary>
    public void RefreshColors(HexCell[] cells)
    {
        if (_mesh == null || cells == null) return;
        Color[] colors = _mesh.colors;
        if (colors == null || colors.Length != cells.Length * VerticesPerHex) return;

        foreach (HexCell cell in cells)
        {
            Color c     = GetVisualColor(cell);
            int   start = cell.gridIndex * VerticesPerHex;
            for (int i = start; i < start + VerticesPerHex; i++)
                colors[i] = c;
        }

        _mesh.colors = colors;
    }

    /// <summary>
    /// Retourne le gridIndex de la cellule correspondant à un triangleIndex de Physics.Raycast.
    /// </summary>
    public int GetGridIndexFromTriangle(int triangleIndex)
    {
        int triArrayIndex = triangleIndex / 3;   // chaque triangle = 3 indices
        if (_triangleToCell == null || triArrayIndex < 0 || triArrayIndex >= _triangleToCell.Length)
            return -1;
        return _triangleToCell[triArrayIndex];
    }

    /// <summary>Bounds du mesh dans l'espace local (pour centrer la caméra).</summary>
    public Bounds GetBounds() => _mesh != null ? _mesh.bounds : new Bounds(Vector3.zero, Vector3.zero);

    /// <summary>
    /// Construit ou reconstruit le mesh plat depuis des tuiles H3 serveur.
    /// Chaque tuile est positionnée à (lonNorm×FlatMapWidth, latNorm×FlatMapHeight).
    /// La taille des hexagones est déduite automatiquement du nombre de tuiles.
    /// </summary>
    public void TriangulateH3(GoldbergTileState[] tiles, System.Collections.Generic.Dictionary<TerrainType, Color> colorByType)
    {
        _mesh.Clear();
        _vertices.Clear();
        _triangles.Clear();
        _colors.Clear();
        if (tiles == null || tiles.Length == 0) return;

        // Rayon de l'hexagone déduit de la surface couverte par tuile
        float hexArea = (FlatMapWidth * FlatMapHeight) / tiles.Length;
        float r       = Mathf.Sqrt(2f * hexArea / (3f * 1.7320508f)) * 1.05f; // légère superposition anti-gaps

        int totalTris = tiles.Length * 6;
        _triangleToCell = new int[totalTris];

        for (int idx = 0; idx < tiles.Length; idx++)
        {
            GoldbergTileState tile = tiles[idx];
            float x = tile.lonNorm * FlatMapWidth  - FlatMapWidth  * 0.5f;
            float z = tile.latNorm * FlatMapHeight - FlatMapHeight * 0.5f;
            Vector3 center = new Vector3(x, 0f, z);

            Color color = colorByType != null && colorByType.TryGetValue(tile.terrainType, out Color c)
                ? c : Color.black;

            int triStart = _triangles.Count / 3;
            for (int i = 0; i < 6; i++)
            {
                float a0 = (30f + i       * 60f) * Mathf.Deg2Rad;
                float a1 = (30f + (i + 1) * 60f) * Mathf.Deg2Rad;
                Vector3 v0 = center + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                Vector3 v1 = center + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);

                int vIdx = _vertices.Count;
                _vertices.Add(center); _vertices.Add(v0); _vertices.Add(v1);
                _triangles.Add(vIdx); _triangles.Add(vIdx + 1); _triangles.Add(vIdx + 2);
                _colors.Add(color); _colors.Add(color); _colors.Add(color);
                _triangleToCell[triStart + i] = idx;
            }
        }

        _mesh.vertices  = _vertices.ToArray();
        _mesh.triangles = _triangles.ToArray();
        _mesh.colors    = _colors.ToArray();
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _meshCollider.sharedMesh = _mesh;
    }

    // =========================================================
    // Géométrie Mercator
    // =========================================================

    private void TriangulateCell(HexCell cell, int cols, int rows)
    {
        Vector3 center = MercatorCenter(cell.Q, cell.R, cols, rows);
        float   hexW   = FlatMapWidth  / cols;
        // Pointy-top : r = hexW/√3 (largeur = r×√3 = hexW).
        // Facteur 1.0 exact : les 3 tuiles d'une jonction partagent le même sommet → zéro gap.
        float   r      = hexW / 1.7320508f;

        Color color      = GetVisualColor(cell);
        int   triStart   = _triangles.Count / 3;

        for (int i = 0; i < 6; i++)
        {
            // 30° d'offset → pointy-top (sommet vers le nord/sud)
            // compatible avec le décalage de rangée (row stagger) de MercatorCenter
            float a0 = (30f + i       * 60f) * Mathf.Deg2Rad;
            float a1 = (30f + (i + 1) * 60f) * Mathf.Deg2Rad;

            Vector3 v0 = center + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
            Vector3 v1 = center + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);

            int idx = _vertices.Count;
            _vertices.Add(center);
            _vertices.Add(v0);
            _vertices.Add(v1);

            _triangles.Add(idx);
            _triangles.Add(idx + 1);
            _triangles.Add(idx + 2);

            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);

            _triangleToCell[triStart + i] = cell.gridIndex;
        }
    }

    /// <summary>
    /// Convertit les coordonnées (col, row) de la grille Mercator en position world XZ.
    /// Axe X : longitude (ouest→est). Axe Z : latitude (sud→nord).
    /// Espacement Z = r×1.5 (géométrie exacte hex pointy-top).
    /// Rangées impaires décalées de hexW/2 en X (row stagger).
    /// </summary>
    public static Vector3 MercatorCenter(int col, int row, int cols, int rows)
    {
        float hexW       = FlatMapWidth / cols;
        float r          = hexW / 1.7320508f;          // circumradius : width = r×√3
        float rowSpacing = r * 1.5f;                   // espacement centre→centre entre rangées
        float stagger    = (row % 2 == 1) ? hexW * 0.5f : 0f;
        float x          = (col + 0.5f) * hexW + stagger;
        float z          = row * rowSpacing + r;       // rangée 0 centrée à r du bas
        return new Vector3(x, 0f, z);
    }

    // =========================================================
    // Couleur visuelle (identique à HexMesh.GetVisualColor)
    // =========================================================

    public static Color GetVisualColor(HexCell cell)
    {
        Color baseColor = cell.terrain != null ? cell.terrain.color : Color.white;
        HexPhysicalState state = cell.state;

        Color waterTint = state.waterClassification switch
        {
            WaterClassification.OpenOcean   => new Color(0.10f, 0.32f, 0.58f, 1f),
            WaterClassification.InlandWater => new Color(0.16f, 0.52f, 0.72f, 1f),
            WaterClassification.Coast       => new Color(0.84f, 0.78f, 0.52f, 1f),
            WaterClassification.FrozenWater => new Color(0.78f, 0.92f, 1.00f, 1f),
            _                               => baseColor
        };

        float waterBlend = state.waterClassification switch
        {
            WaterClassification.OpenOcean   => 0.42f,
            WaterClassification.InlandWater => 0.32f,
            WaterClassification.Coast       => 0.22f,
            WaterClassification.FrozenWater => 0.38f,
            _                               => 0f
        };

        Color color = Color.Lerp(baseColor, waterTint, waterBlend);

        float reliefBrightness = state.terrainClass switch
        {
            TerrainClass.Ridge   => 1.10f,
            TerrainClass.Basin   => 0.92f,
            TerrainClass.Channel => 0.96f,
            TerrainClass.Source  => 1.04f,
            _                    => 1f
        };

        color *= reliefBrightness;
        color.r = Mathf.Clamp01(color.r);
        color.g = Mathf.Clamp01(color.g);
        color.b = Mathf.Clamp01(color.b);
        color.a = 1f;

        return color;
    }
}
