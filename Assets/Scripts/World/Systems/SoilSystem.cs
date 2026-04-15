using UnityEngine;

/// <summary>
/// Passe 5 du pipeline — calcul du profil de sol et du niveau de toxines.
///
/// Lit  : cell.state.altitude, cell.state.tempLocale, cell.state.waterRatio
///        ctx.body.geology, ctx.body.atmosphere
///        ctx.geoOffset (bruit géologique)
/// Écrit: cell.state.toxinLevel, cell.state.soil (SoilProfile)
///
/// Calcul du sol :
///   - rockHardness    ∝ altitude + activité géologique
///   - porosity        ∝ bruit biome, réduit par l'activité géologique
///   - mineralDensity  ∝ mineralRichness × bruit géologique
///   - thermalConductivity ∝ activité géologique + altitude
///   - organicContent  = 0 à la génération (augmente via terraformation)
///   - toxicSoil       = toxinLevel > seuil ET atmosphere.toxinRatio > seuil
///
/// Interaction eau (dynamique) :
///   Si waterRatio > 0.8, la porosité converge vers 0.4 (saturation = eau stagnante)
/// </summary>
public class SoilSystem : IHexSystem
{
    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        MapGenParameters p   = ctx.genParams;
        float            gox = ctx.geoOffset.x;
        float            goz = ctx.geoOffset.y;
        float            box = ctx.biomeOffset.x;
        float            boz = ctx.biomeOffset.y;

        foreach (HexCell cell in cells)
        {
            float gx = cell.center.x / p.heightScale + gox;
            float gz = cell.center.z / p.heightScale + goz;
            float bx = cell.center.x / p.biomeScale  + box;
            float bz = cell.center.z / p.biomeScale  + boz;

            float geoNoise  = GenerationContext.FractalNoise(gx, gz, p.octaves, p.persistence, p.lacunarity);
            float biomeNoise = GenerationContext.FractalNoise(bx, bz, p.octaves, p.persistence, p.lacunarity);

            float altitude  = cell.state.altitude;
            float water     = cell.state.waterRatio;
            float tempLocal = cell.state.tempLocale;

            // --- Niveau de toxines (atmosphérique + géologique) ---
            float toxinLevel = Mathf.Clamp01(
                ctx.body.atmosphere.toxinRatio * (0.5f + geoNoise * 0.5f));
            cell.state.toxinLevel = toxinLevel;

            // --- Profil de sol ---
            float hardness  = Mathf.Clamp01(altitude * 0.5f + geoNoise * 0.5f);
            float porosity  = Mathf.Clamp01(biomeNoise * (1f - ctx.body.geology.geologicalActivity * 0.4f));
            float mineral   = Mathf.Clamp01(ctx.body.geology.mineralRichness * (geoNoise + 0.2f));
            float thermalC  = Mathf.Clamp01(ctx.body.geology.geologicalActivity * 0.7f + altitude * 0.3f);

            // Interaction eau → saturation converge la porosité vers 0.4
            if (water > 0.8f)
                porosity = Mathf.Lerp(porosity, 0.4f, (water - 0.8f) / 0.2f);

            // Gel intense → sol durci en surface
            if (tempLocal < -60f)
                hardness = Mathf.Min(1f, hardness + 0.2f);

            bool toxicSoil = toxinLevel > 0.4f && ctx.body.atmosphere.toxinRatio > 0.3f;

            cell.state.soil = new SoilProfile
            {
                rockHardness        = hardness,
                organicContent      = 0f,     // augmente via actions de terraformation
                porosity            = porosity,
                mineralDensity      = mineral,
                toxicSoil           = toxicSoil,
                thermalConductivity = thermalC
            };
        }
    }
}
