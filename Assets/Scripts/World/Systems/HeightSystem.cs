using UnityEngine;

/// <summary>
/// Passe 1 du pipeline — calcul de l'altitude et de la couche WorldLayer.
///
/// Lit  : ctx.heightOffset, ctx.genParams, cell.center
/// Écrit: cell.state.altitude, cell.layer
///
/// L'altitude [0–1] est calculée par bruit de Perlin fractal (fBm).
/// La couche WorldLayer est déduite via CelestialBodyData.GetLayerForHeight().
/// </summary>
public class HeightSystem : IHexSystem
{
    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        MapGenParameters p  = ctx.genParams;
        float            ox = ctx.heightOffset.x;
        float            oz = ctx.heightOffset.y;

        foreach (HexCell cell in cells)
        {
            float hx = cell.center.x / p.heightScale + ox;
            float hz = cell.center.z / p.heightScale + oz;

            float altitude = GenerationContext.FractalNoise(hx, hz, p.octaves, p.persistence, p.lacunarity);

            // Érosion légère : légèrement aplatir les valeurs médianes
            altitude = ApplyErosion(altitude);

            cell.state.altitude = altitude;

            // Déduire la couche WorldLayer depuis les zones définies sur le corps
            LayerZone zone = ctx.body.GetLayerForHeight(altitude);
            cell.layer = zone != null ? zone.layer : WorldLayer.Surface;
            cell.world = ctx.body;
        }
    }

    /// <summary>
    /// Érosion légère : réduit les pics extrêmes et remonte légèrement les creux.
    /// Formule : altitude^(1/1.15) lissée vers 0.5 pour les valeurs proches du médian.
    /// </summary>
    private static float ApplyErosion(float altitude)
    {
        // Redistribution douce : compresse les extrêmes vers le centre
        float eroded = Mathf.Pow(altitude, 0.87f);
        // Mélange léger avec l'original pour ne pas trop aplatir
        return Mathf.Lerp(altitude, eroded, 0.25f);
    }
}
