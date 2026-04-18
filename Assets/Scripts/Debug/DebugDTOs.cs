using UnityEngine;

[System.Serializable]
public struct ConsoleLogEntry
{
    public string type;
    public string condition;
    public string stackTrace;
    public string timestamp;

    public string FormatSingleLine()
    {
        return $"[{timestamp}] {type}: {condition}";
    }
}

[System.Serializable]
public struct ConsoleSnapshot
{
    public bool isValid;
    public int totalEntries;
    public int logCount;
    public int warningCount;
    public int errorCount;
    public int exceptionCount;
    public ConsoleLogEntry[] entries;

    public string FormatMultiline()
    {
        if (!isValid)
            return "Console: aucune donnee disponible.";

        string header = $"Console: {totalEntries} entrees | logs {logCount} | warnings {warningCount} | erreurs {errorCount} | exceptions {exceptionCount}";
        if (entries == null || entries.Length == 0)
            return header;

        string details = string.Empty;
        for (int i = 0; i < entries.Length; i++)
            details += (i == 0 ? string.Empty : "\n") + entries[i].FormatSingleLine();

        return header + "\n" + details;
    }
}

[System.Serializable]
public struct ScreenshotCaptureResult
{
    public bool success;
    public string filePath;
    public string message;

    public string FormatSingleLine()
    {
        return success
            ? $"Screenshot: {filePath}"
            : $"Screenshot failed: {message}";
    }
}

[System.Serializable]
public struct ViewStateSnapshot
{
    public string currentView;
    public string activePlanetName;
    public string activeProjectionOverride;
    public float activeProjectionWaterLevel;
    public bool hasRegion;
    public float regionLatitude;
    public float regionLongitude;
    public bool hasSelectedCell;
    public int selectedCellQ;
    public int selectedCellR;
    public float terraformationProgress;
    public int localCellCount;

    public string FormatMultiline()
    {
        string regionLine = hasRegion
            ? $"Region: lat {regionLatitude:F2} lon {regionLongitude:F2}"
            : "Region: aucune";
        string selectionLine = hasSelectedCell
            ? $"Selection: ({selectedCellQ}, {selectedCellR})"
            : "Selection: aucune";

        return
            $"Vue: {currentView}\n" +
            $"Planete: {activePlanetName}\n" +
            $"Override: {activeProjectionOverride} | eau proj {activeProjectionWaterLevel:+0.00;-0.00;0.00}\n" +
            regionLine + "\n" +
            selectionLine + "\n" +
            $"Progression: {terraformationProgress * 100f:F1}% | cellules locales: {localCellCount}";
    }
}

[System.Serializable]
public struct PresetLaunchRequest
{
    public TestScenarioPreset preset;
    public bool useLatitudeOverride;
    public float latitudeOverride;
    public bool useLongitudeOverride;
    public float longitudeOverride;
    public bool clearProjectionCache;
}

[System.Serializable]
public struct RegionOpenRequest
{
    public float latitude;
    public float longitude;
}

[System.Serializable]
public struct ProjectionSummary
{
    public bool isValid;
    public string activePlanetName;
    public string activeProjectionOverride;
    public float activeProjectionWaterLevel;
    public PlanetaryHexGrid.ProjectionDebugSummary gridSummary;

    public string FormatMultiline()
    {
        if (!isValid)
            return "Projection: aucune donnee disponible.";

        return
            $"Planete: {activePlanetName}\n" +
            $"Override: {activeProjectionOverride} | eau proj {activeProjectionWaterLevel:+0.00;-0.00;0.00}\n" +
            gridSummary.FormatMultiline();
    }
}

[System.Serializable]
public struct LocalRegionSummary
{
    public bool isValid;
    public string activePlanetName;
    public float regionLatitude;
    public float regionLongitude;
    public float coherenceOceanicity;
    public float coherenceDeserticity;
    public float coherenceFrigidity;
    public float projectedWaterRatio;
    public float terraformationProgress;
    public bool hasSelectedCell;
    public int selectedCellQ;
    public int selectedCellR;
    public HexGridDebugSummary gridSummary;

    public string FormatMultiline()
    {
        if (!isValid)
            return "Region locale: aucune donnee disponible.";

        string selectionLine = hasSelectedCell
            ? $"Selection: ({selectedCellQ}, {selectedCellR})"
            : "Selection: aucune";

        return
            $"Planete: {activePlanetName}\n" +
            $"Region: lat {regionLatitude:F2} lon {regionLongitude:F2} | eau proj {projectedWaterRatio * 100f:F0}%\n" +
            $"Coherence: mer {coherenceOceanicity:F2} | aride {coherenceDeserticity:F2} | gel {coherenceFrigidity:F2}\n" +
            $"Progression: {terraformationProgress * 100f:F1}%\n" +
            selectionLine + "\n" +
            gridSummary.FormatMultiline();
    }
}