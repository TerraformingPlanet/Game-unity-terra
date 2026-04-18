using UnityEngine;

/// <summary>
/// Colorise les tuiles d'une sphère Goldberg depuis une grille planétaire Mercator.
///
/// Pour chaque tuile GP, on cherche la HexCell Mercator dont les coordonnées
/// lat/lon normalisées correspondent au centroïde de la tuile, puis on
/// assigne la couleur du terrain de cette cellule.
///
/// Appelé après GoldbergSphereGenerator.Generate() et avant
/// GoldbergSphereGenerator.ApplyFaceColors().
/// </summary>
public static class GoldbergFaceColorizer
{
    /// <summary>
    /// Remplit faces[i].color depuis la grille planétaire fournie par PlanetaryHexGrid.Generate().
    /// </summary>
    public static void Colorize(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        PlanetaryHexGrid.GridData grid)
    {
        if (grid.Cells == null || grid.Cells.Length == 0) return;

        for (int i = 0; i < faces.Length; i++)
        {
            HexCell cell = PlanetaryHexGrid.GetCellAt(
                grid.Cells, grid.Cols, grid.Rows,
                faces[i].latNorm, faces[i].lonNorm);

            faces[i].color = (cell?.terrain != null) ? cell.terrain.color : Color.black;
        }
    }
}
