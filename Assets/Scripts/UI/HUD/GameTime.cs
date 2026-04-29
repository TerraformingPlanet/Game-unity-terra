/// <summary>
/// Conversion des ticks en unités de temps lisibles.
/// 1 tick = 1 jour. Calendrier simplifié : 365 jours/an, 30 jours/mois.
/// Le jeu de colonisation spatiale fonctionne sur des centaines d'années.
/// </summary>
public static class GameTime
{
    public const int DaysPerYear  = 365;
    public const int DaysPerMonth = 30;

    /// <summary>
    /// Convertit un tick absolu en chaîne courte pour la TopBar.
    /// Ex : tick 0 → "An 1 J1" ; tick 730 → "An 3 J1"
    /// </summary>
    public static string TickToDateShort(int tick)
    {
        if (tick < 0) return "—";
        int day0  = tick;                          // 0-based
        int year  = day0 / DaysPerYear + 1;
        int dayInYear = day0 % DaysPerYear + 1;
        return $"An {year} J{dayInYear}";
    }

    /// <summary>
    /// Convertit une durée en ticks en chaîne humaine compacte.
    /// Ex : 2 → "2 j" ; 45 → "1 mois" ; 400 → "1 an"
    /// </summary>
    public static string DurationShort(int ticks)
    {
        if (ticks <= 0)  return "—";
        if (ticks == 1)  return "1 j";
        if (ticks < DaysPerMonth) return $"{ticks} j";

        int months = ticks / DaysPerMonth;
        if (months < 12)
        {
            int rem = ticks % DaysPerMonth;
            return rem > 0 ? $"{months} mois {rem} j" : $"{months} mois";
        }

        int years  = ticks / DaysPerYear;
        int remDays = ticks % DaysPerYear;
        if (remDays == 0) return years == 1 ? "1 an" : $"{years} ans";
        int remMonths = remDays / DaysPerMonth;
        return remMonths > 0
            ? $"{years} an{(years > 1 ? "s" : "")} {remMonths} mois"
            : $"{years} an{(years > 1 ? "s" : "")} {remDays} j";
    }
}
