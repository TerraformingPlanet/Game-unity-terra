using UnityEngine;

/// <summary>
/// Génère une Texture2D équirectangulaire (512×256) depuis une grille planétaire (PlanetaryHexGrid).
///
/// Algorithme :
///   Pour chaque pixel (px, py) :
///     uv.x = px / width  → longitude normalisée [0–1]
///     uv.y = py / height → latitude normalisée [0–1]
///     → cellule Mercator correspondante → terrain.color
///
/// La texture est appliquée au matériau de PlanetSphere.
/// </summary>
public static class PlanetTextureGenerator
{
    public const int TEX_WIDTH  = 512;
    public const int TEX_HEIGHT = 256;

    /// <summary>
    /// Génère et retourne la Texture2D à partir d'une grille planétaire.
    /// La texture est non compressée (RGBA32) sans mipmaps — adaptée à un usage runtime.
    /// </summary>
    public static Texture2D Generate(PlanetaryHexGrid.GridData grid)
    {
        if (grid.Cells == null || grid.Cells.Length == 0)
        {
            Debug.LogError("[PlanetTextureGenerator] Grille planétaire vide.");
            return Texture2D.blackTexture;
        }

        Texture2D tex = new Texture2D(TEX_WIDTH, TEX_HEIGHT, TextureFormat.RGBA32, false);

        Color[] pixels = new Color[TEX_WIDTH * TEX_HEIGHT];

        for (int py = 0; py < TEX_HEIGHT; py++)
        {
            float latNorm = (float)py / TEX_HEIGHT;

            for (int px = 0; px < TEX_WIDTH; px++)
            {
                float lonNorm = (float)px / TEX_WIDTH;

                HexCell cell = PlanetaryHexGrid.GetCellAt(grid.Cells, grid.Cols, grid.Rows, latNorm, lonNorm);

                Color c = (cell?.terrain != null) ? cell.terrain.color : Color.black;
                pixels[py * TEX_WIDTH + px] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat; // boucle est-ouest fluide

        return tex;
    }
}
