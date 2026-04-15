using UnityEngine;

/// <summary>
/// Passe 3 du pipeline — calcul du ratio d'eau et de l'ombre pluviométrique.
///
/// Lit  : cell.state.altitude, cell.state.tempLocale, ctx.weather, ctx.body.geology/atmosphere
/// Écrit: cell.state.waterRatio, cell.state.rainShadow
///
/// Phase A — ratio initial par hex :
///   waterRatio = waterAbundance × biomeNoise × atmosphereDensity
///   Modulé par gel (< −20°C) et évaporation (> 80°C)
///
/// Phase B — accumulation topographique :
///   Pour chaque hex, les voisins plus hauts transfèrent une partie de leur eau
///   (ruissellement de pente). Nécessite ctx.cellLookup.
///
/// Phase C — ombre pluviométrique :
///   Un hex est en ombre pluviométrique si le vent dominant arrive d'un relief
///   plus élevé. Simplifié ici : hex sous le vent d'un voisin haute altitude.
/// </summary>
public class WaterSystem : IHexSystem
{
    // Fraction d'eau transférée par unité d'écart d'altitude entre deux voisins
    private const float RunoffFactor = 0.18f;

    // Seuil d'altitude pour générer une ombre pluviométrique
    private const float RainShadowAltitudeThreshold = 0.6f;

    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        MapGenParameters p  = ctx.genParams;
        float            ox = ctx.biomeOffset.x;
        float            oz = ctx.biomeOffset.y;

        // --- Phase A : ratio d'eau initial par hex ---
        foreach (HexCell cell in cells)
        {
            float bx = cell.center.x / p.biomeScale + ox;
            float bz = cell.center.z / p.biomeScale + oz;
            float biomeNoise = GenerationContext.FractalNoise(bx, bz, p.octaves, p.persistence, p.lacunarity);

            float w = ctx.body.geology.waterAbundance
                    * biomeNoise
                    * ctx.body.atmosphere.density;

            float temp = cell.state.tempLocale;

            // Gel : eau présente sous forme de glace (réduit la mobilité)
            if (temp < -20f) w *= 0.5f;
            // Évaporation thermique intense
            if (temp > 80f)  w *= Mathf.Lerp(1f, 0f, (temp - 80f) / 120f);

            // Précipitations ajoutées par la météo régionale
            w += ctx.weather.precipitationRate * 0.2f;

            cell.state.waterRatio = Mathf.Clamp01(w);
            cell.state.rainShadow = false;
        }

        // --- Phase B : ruissellement topographique (flux vers voisins bas) ---
        // Deux passes pour propager l'accumulation sans ordre dépendant
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (HexCell cell in cells)
            {
                HexCell[] neighbors = ctx.GetNeighbors(cell);
                foreach (HexCell nb in neighbors)
                {
                    float altDiff = cell.state.altitude - nb.state.altitude;
                    if (altDiff > 0f)
                    {
                        // Eau qui s'écoule du hex courant vers son voisin plus bas
                        float flow = altDiff * RunoffFactor * cell.state.waterRatio
                                   * cell.state.soil.porosity; // infiltration réduit le flux
                        flow = Mathf.Min(flow, cell.state.waterRatio * 0.3f); // cap

                        cell.state.waterRatio -= flow;
                        nb.state.waterRatio   += flow;
                    }
                }
                cell.state.waterRatio = Mathf.Clamp01(cell.state.waterRatio);
            }
        }

        // --- Phase C : ombre pluviométrique ---
        Vector2 windDir = ctx.weather.prevailingWindDir.normalized;

        foreach (HexCell cell in cells)
        {
            HexCell[] neighbors = ctx.GetNeighbors(cell);
            foreach (HexCell nb in neighbors)
            {
                // Vecteur du voisin vers le hex courant
                Vector2 toCell = new Vector2(
                    cell.center.x - nb.center.x,
                    cell.center.z - nb.center.z).normalized;

                // Le voisin est "au vent" si le vent souffle de lui vers le hex courant
                float alignment = Vector2.Dot(windDir, toCell);
                if (alignment > 0.5f && nb.state.altitude > RainShadowAltitudeThreshold)
                {
                    cell.state.rainShadow = true;
                    cell.state.waterRatio = Mathf.Max(0f, cell.state.waterRatio - 0.25f);
                    break;
                }
            }
        }
    }
}
