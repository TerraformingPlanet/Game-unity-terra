using UnityEngine;

/// <summary>
/// Passe 2 du pipeline — calcul de la température locale.
///
/// Lit  : cell.state.altitude, ctx.weather, ctx.body.physics, ctx.body.atmosphere
/// Écrit: cell.state.tempLocale
///
/// Formule :
///   tempLocale = baseEquatorTemp
///              + weather.temperatureOffset   (latitude + tidal lock + effet de serre)
///              + weather.seasonalModifier × amplitudeSaisonnière
///              - altitude × pente altitudinale
/// </summary>
public class TemperatureSystem : IHexSystem
{
    // -5°C par 100m équivalent — mappé sur altitude [0–1]
    private const float AltitudePenalty = 60f;

    // Amplitude de la variation saisonnière max (°C) à 90° d'inclinaison axiale
    private const float MaxSeasonalAmplitude = 40f;

    public void Execute(HexCell[] cells, GenerationContext ctx)
    {
        float baseTemp        = ctx.body.physics.baseEquatorTemperature;
        float weatherOffset   = ctx.weather.temperatureOffset;
        float seasonalMod     = ctx.weather.seasonalModifier;
        float axialTilt       = ctx.body.physics.axialTilt;

        // L'amplitude saisonnière est proportionnelle à l'inclinaison axiale
        float seasonalAmplitude = (axialTilt / 90f) * MaxSeasonalAmplitude;

        foreach (HexCell cell in cells)
        {
            float temp = baseTemp
                       + weatherOffset
                       + seasonalMod * seasonalAmplitude
                       - cell.state.altitude * AltitudePenalty;

            cell.state.tempLocale = temp;
        }
    }
}
