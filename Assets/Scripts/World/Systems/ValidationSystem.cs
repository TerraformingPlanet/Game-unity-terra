using UnityEngine;

/// <summary>
/// Passe 8 du pipeline — validation et normalisation des valeurs finales.
///
/// Lit  : cell.state (tous les champs)
/// Écrit: cell.state (corrections et clamps)
///
/// Garantit que tous les champs de HexPhysicalState sont dans leurs plages valides
/// avant que le résultat ne soit consommé par le rendu et le gameplay.
/// Journalise un warning si un hex est dans un état incohérent.
/// </summary>
public class ValidationSystem : IHexSystem
{
    // Température physiquement plausible pour un corps planétaire
    private const float TempMin = -300f;
    private const float TempMax =  600f;

    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        int warnings = 0;

        foreach (HexCell cell in cells)
        {
            ref HexPhysicalState s = ref cell.state;

            // Altitude
            s.altitude = Mathf.Clamp01(s.altitude);

            // Température
            if (s.tempLocale < TempMin || s.tempLocale > TempMax)
            {
                s.tempLocale = Mathf.Clamp(s.tempLocale, TempMin, TempMax);
                warnings++;
            }

            // Ratios [0–1]
            s.waterRatio = Mathf.Clamp01(s.waterRatio);
            s.toxinLevel = Mathf.Clamp01(s.toxinLevel);
            s.windSpeed  = Mathf.Clamp01(s.windSpeed);

            // Sol
            s.soil.rockHardness        = Mathf.Clamp01(s.soil.rockHardness);
            s.soil.organicContent      = Mathf.Clamp01(s.soil.organicContent);
            s.soil.porosity            = Mathf.Clamp01(s.soil.porosity);
            s.soil.mineralDensity      = Mathf.Clamp01(s.soil.mineralDensity);
            s.soil.thermalConductivity = Mathf.Clamp01(s.soil.thermalConductivity);

            // Cohérence biome / état physique
            if (cell.terrain == null)
            {
                warnings++;
                // Pas de terrain → log silencieux, le renderer utilisera une couleur de fallback
            }

            // Rivière ne peut pas coexister avec un hex en haute altitude sans eau
            if (s.hasRiver && s.waterRatio < 0.05f)
                s.hasRiver = false;
        }

        if (warnings > 0)
            UnityEngine.Debug.LogWarning($"[ValidationSystem] {warnings} avertissements sur {cells.Length} hexes.");
    }
}
