using UnityEngine;

/// <summary>
/// Grille hexagonale basse résolution en projection Mercator couvrant toute la surface
/// d'une planète (48 colonnes × 24 lignes = 1152 cellules).
///
/// Chaque cellule couvre environ 7,5° de longitude × 7,5° de latitude.
/// Le pipeline IHexSystem tourne sur cette grille exactement comme pour une région locale,
/// mais coordonnées (q, r) = (colonne_lon, ligne_lat).
///
/// Utilisé par PlanetSphere via PlanetTextureGenerator pour générer la texture équirectangulaire.
/// </summary>
public static class PlanetaryHexGrid
{
    public const int COLS = 48; // longitude
    public const int ROWS = 24; // latitude

    /// <summary>
    /// Génère et peuple la grille planétaire pour un corps céleste donné.
    /// Retourne un tableau [col * ROWS + row] → HexCell.
    /// </summary>
    public static HexCell[] Generate(CelestialBodyData body)
    {
        if (body == null)
        {
            Debug.LogError("[PlanetaryHexGrid] CelestialBodyData manquant.");
            return null;
        }

        HexCell[] cells = CreateCells();
        MapGenerator.Populate(cells, body);
        return cells;
    }

    // =========================================================
    // Interne
    // =========================================================

    private static HexCell[] CreateCells()
    {
        HexCell[] cells = new HexCell[COLS * ROWS];

        for (int col = 0; col < COLS; col++)
        {
            for (int row = 0; row < ROWS; row++)
            {
                // On réutilise les coordonnées axiales (q=col, r=row) comme identifiants.
                // Le centre world n'est utilisé que pour les calculs de hauteur par distance ;
                // on le place sur une grille régulière normalisée [0-1].
                var cell = new HexCell(col, row) { gridIndex = col * ROWS + row };

                // Override du centre pour placer les cellules dans un espace [0,1]²
                // (PlanetTextureGenerator utilise UV normalisés, pas les coords world HexMetrics)
                float u = (col + 0.5f) / COLS;
                float v = (row + 0.5f) / ROWS;
                cell.center = new UnityEngine.Vector3(u, 0f, v);

                cells[col * ROWS + row] = cell;
            }
        }

        return cells;
    }

    // =========================================================
    // Utilitaires publics
    // =========================================================

    /// <summary>
    /// Retourne la cellule correspondant à des coordonnées lat/lon normalisées [0–1].
    /// </summary>
    public static HexCell GetCellAt(HexCell[] cells, float latNorm, float lonNorm)
    {
        int col = Mathf.Clamp(Mathf.FloorToInt(lonNorm * COLS), 0, COLS - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt(latNorm * ROWS), 0, ROWS - 1);
        return cells[col * ROWS + row];
    }
}
