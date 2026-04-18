using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Facade runtime additive pour centraliser les lectures et actions debug utiles.
/// Elle sert de base a un futur bridge MCP ou API locale, sans dependre du panneau UI.
/// </summary>
public class RuntimeDebugFacade : MonoBehaviour
{
    private static RuntimeDebugFacade _instance;
    private const int MaxConsoleEntries = 200;

    private readonly Queue<ConsoleLogEntry> _consoleEntries = new Queue<ConsoleLogEntry>(MaxConsoleEntries);

    [Header("References")]
    [SerializeField] private ViewManager viewManager;
    [SerializeField] private TerraformHUD terraformHUD;
    [SerializeField] private TerraformProgressTracker terraformProgressTracker;
    [SerializeField] private TestLaunchMenu testLaunchMenu;

    private ITickSource _tickSource;
    private IClientSnapshotSource _clientSnapshotSource;

    public static RuntimeDebugFacade Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindAnyObjectByType<RuntimeDebugFacade>();

            if (_instance == null)
            {
                GameObject facadeObject = new GameObject("RuntimeDebugFacade");
                _instance = facadeObject.AddComponent<RuntimeDebugFacade>();
            }

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        ResolveReferences();
        Application.logMessageReceived += HandleLogMessageReceived;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        Application.logMessageReceived -= HandleLogMessageReceived;
    }

    public ViewStateSnapshot GetCurrentViewState()
    {
        ResolveReferences();

        var snapshot = new ViewStateSnapshot
        {
            currentView = viewManager != null ? viewManager.CurrentState.ToString() : "Unavailable",
            activePlanetName = viewManager != null && viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune",
            activeProjectionOverride = viewManager != null ? viewManager.ActiveProjectionOverride.ToString() : DebugCoherenceOverride.None.ToString(),
            activeProjectionWaterLevel = viewManager != null ? viewManager.ActiveProjectionWaterLevel : 0f,
            terraformationProgress = terraformProgressTracker != null ? terraformProgressTracker.Progress : 0f,
            localCellCount = viewManager != null && viewManager.ActiveHexGrid != null && viewManager.ActiveHexGrid.HasCells()
                ? viewManager.ActiveHexGrid.GetCells().Length
                : 0
        };

        MapRegion currentRegion = viewManager != null && viewManager.ActiveHexGrid != null
            ? viewManager.ActiveHexGrid.CurrentRegion
            : null;

        if (currentRegion != null)
        {
            snapshot.hasRegion = true;
            snapshot.regionLatitude = currentRegion.latitude;
            snapshot.regionLongitude = currentRegion.longitude;
        }

        HexCell selectedCell = terraformHUD != null ? terraformHUD.SelectedCell : null;
        if (selectedCell != null)
        {
            snapshot.hasSelectedCell = true;
            snapshot.selectedCellQ = selectedCell.Q;
            snapshot.selectedCellR = selectedCell.R;
        }

        return snapshot;
    }

    public bool LaunchPreset(PresetLaunchRequest request)
    {
        ResolveReferences();

        if (viewManager == null || request.preset == null || request.preset.body == null)
            return false;

        float latitude = request.useLatitudeOverride
            ? Mathf.Clamp01(request.latitudeOverride)
            : request.preset.latitude;
        float longitude = request.useLongitudeOverride
            ? Mathf.Clamp01(request.longitudeOverride)
            : request.preset.longitude;

        if (request.clearProjectionCache || request.preset.clearProjectionCacheBeforeLaunch)
            viewManager.ActivePlanetSphere?.ClearProjectionCache();

        return viewManager.LaunchDebugScenario(request.preset, latitude, longitude);
    }

    public bool LaunchPresetByName(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return false;

        TestScenarioPreset preset = FindPresetByName(presetName);
        if (preset == null)
            return false;

        return LaunchPreset(new PresetLaunchRequest { preset = preset });
    }

    public bool OpenRegion(RegionOpenRequest request)
    {
        ResolveReferences();

        return viewManager != null &&
               viewManager.TryOpenRegionNormalized(Mathf.Clamp01(request.latitude), Mathf.Clamp01(request.longitude));
    }

    public ProjectionSummary GetProjectionSummary()
    {
        ResolveReferences();

        var summary = new ProjectionSummary();
        if (viewManager == null || viewManager.ActivePlanetSphere == null)
            return summary;

        if (!viewManager.ActivePlanetSphere.TryBuildProjectionSummary(out PlanetaryHexGrid.ProjectionDebugSummary gridSummary))
            return summary;

        summary.isValid = true;
        summary.activePlanetName = viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune";
        summary.activeProjectionOverride = viewManager.ActiveProjectionOverride.ToString();
        summary.activeProjectionWaterLevel = viewManager.ActiveProjectionWaterLevel;
        summary.gridSummary = gridSummary;
        return summary;
    }

    public ProjectionState GetProjectionState()
    {
        ResolveReferences();
        return _clientSnapshotSource != null && _clientSnapshotSource.TryBuildProjectionState(out ProjectionState state)
            ? state
            : default;
    }

    public LocalRegionSummary GetLocalSummary()
    {
        ResolveReferences();

        var summary = new LocalRegionSummary();
        if (viewManager == null || viewManager.ActiveHexGrid == null || !viewManager.ActiveHexGrid.HasCells())
            return summary;

        if (!viewManager.ActiveHexGrid.TryBuildDebugSummary(out HexGridDebugSummary gridSummary))
            return summary;

        GenerationContext regionContext = terraformHUD != null ? terraformHUD.RegionContext : null;
        MapRegion currentRegion = viewManager.ActiveHexGrid.CurrentRegion;
        HexCell selectedCell = terraformHUD != null ? terraformHUD.SelectedCell : null;

        summary.isValid = true;
        summary.gridSummary = gridSummary;
        summary.activePlanetName = viewManager.ActivePlanet != null ? viewManager.ActivePlanet.bodyName : "Aucune";
        summary.regionLatitude = currentRegion != null ? currentRegion.latitude : 0f;
        summary.regionLongitude = currentRegion != null ? currentRegion.longitude : 0f;
        summary.terraformationProgress = terraformProgressTracker != null ? terraformProgressTracker.Progress : 0f;

        if (regionContext != null)
        {
            summary.coherenceOceanicity = regionContext.coherence.oceanicity;
            summary.coherenceDeserticity = regionContext.coherence.deserticity;
            summary.coherenceFrigidity = regionContext.coherence.frigidity;
            summary.projectedWaterRatio = regionContext.coherence.projectedWaterRatio;
        }
        else if (currentRegion != null)
        {
            summary.projectedWaterRatio = currentRegion.projectedWaterRatio;
        }

        if (selectedCell != null)
        {
            summary.hasSelectedCell = true;
            summary.selectedCellQ = selectedCell.Q;
            summary.selectedCellR = selectedCell.R;
        }

        return summary;
    }

    public RegionState GetRegionState()
    {
        ResolveReferences();
        return _clientSnapshotSource != null && _clientSnapshotSource.TryBuildRegionState(out RegionState state)
            ? state
            : default;
    }

    public WorldState GetWorldState()
    {
        ResolveReferences();
        return _clientSnapshotSource != null
            ? _clientSnapshotSource.BuildWorldState()
            : SimulationContractFactory.BuildWorldState(viewManager, terraformHUD, terraformProgressTracker, _tickSource);
    }

    public ClientSnapshot GetClientSnapshot()
    {
        ResolveReferences();
        return _clientSnapshotSource != null
            ? _clientSnapshotSource.BuildClientSnapshot()
            : SimulationContractFactory.BuildClientSnapshot(viewManager, terraformHUD, terraformProgressTracker, _tickSource);
    }

    public ConsoleSnapshot GetRecentConsoleErrors(int maxEntries = 20, LogType minimumSeverity = LogType.Warning)
    {
        ConsoleLogEntry[] allEntries = _consoleEntries.ToArray();
        var filteredEntries = new List<ConsoleLogEntry>(Mathf.Clamp(maxEntries, 0, MaxConsoleEntries));

        int logCount = 0;
        int warningCount = 0;
        int errorCount = 0;
        int exceptionCount = 0;

        for (int i = allEntries.Length - 1; i >= 0; i--)
        {
            ConsoleLogEntry entry = allEntries[i];
            LogType entryType = ParseLogType(entry.type);

            switch (entryType)
            {
                case LogType.Warning:
                    warningCount++;
                    break;
                case LogType.Error:
                case LogType.Assert:
                    errorCount++;
                    break;
                case LogType.Exception:
                    exceptionCount++;
                    break;
                default:
                    logCount++;
                    break;
            }

            if (!MeetsMinimumSeverity(entryType, minimumSeverity))
                continue;

            if (filteredEntries.Count >= maxEntries)
                continue;

            filteredEntries.Add(entry);
        }

        filteredEntries.Reverse();

        return new ConsoleSnapshot
        {
            isValid = true,
            totalEntries = allEntries.Length,
            logCount = logCount,
            warningCount = warningCount,
            errorCount = errorCount,
            exceptionCount = exceptionCount,
            entries = filteredEntries.ToArray()
        };
    }

    public ScreenshotCaptureResult CaptureSceneScreenshot(string fileName = null, int superSize = 1)
    {
        try
        {
            string sanitizedName = string.IsNullOrWhiteSpace(fileName)
                ? $"terraformation_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                : SanitizeScreenshotFileName(fileName);
            if (!sanitizedName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                sanitizedName += ".png";

            string directoryPath = Path.Combine(Application.persistentDataPath, "DebugCaptures");
            Directory.CreateDirectory(directoryPath);

            string fullPath = Path.Combine(directoryPath, sanitizedName);
            ScreenCapture.CaptureScreenshot(fullPath, Mathf.Max(1, superSize));

            return new ScreenshotCaptureResult
            {
                success = true,
                filePath = fullPath,
                message = "Capture demandee. Le fichier sera ecrit par Unity en fin de frame."
            };
        }
        catch (Exception ex)
        {
            return new ScreenshotCaptureResult
            {
                success = false,
                filePath = string.Empty,
                message = ex.Message
            };
        }
    }

    private void ResolveReferences()
    {
        if (viewManager == null)
            viewManager = FindFirstObjectByType<ViewManager>();
        if (_clientSnapshotSource == null)
            _clientSnapshotSource = viewManager;
        if (terraformHUD == null)
            terraformHUD = FindFirstObjectByType<TerraformHUD>();
        if (terraformProgressTracker == null)
            terraformProgressTracker = FindFirstObjectByType<TerraformProgressTracker>();
        if (testLaunchMenu == null)
            testLaunchMenu = FindFirstObjectByType<TestLaunchMenu>();
        if (_tickSource == null)
            _tickSource = TickManager.Instance;
    }

    private TestScenarioPreset FindPresetByName(string presetName)
    {
        ResolveReferences();

        TestScenarioPreset[] presets = testLaunchMenu != null ? testLaunchMenu.Presets : null;
        if (presets == null)
            return null;

        foreach (TestScenarioPreset preset in presets)
        {
            if (preset == null)
                continue;

            if (string.Equals(preset.displayName, presetName, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset.name, presetName, System.StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }

    // Unity internal allocator warnings — not actionable, suppress from MCP snapshots.
    private static readonly string[] _suppressedPrefixes =
    {
        "TLS Allocator ALLOC_TEMP_TLS",
        "Internal: Stack allocator ALLOC_TEMP_MAIN",
    };

    private void HandleLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        foreach (string prefix in _suppressedPrefixes)
        {
            if (condition.StartsWith(prefix, System.StringComparison.Ordinal))
                return;
        }

        if (_consoleEntries.Count >= MaxConsoleEntries)
            _consoleEntries.Dequeue();

        _consoleEntries.Enqueue(new ConsoleLogEntry
        {
            type = type.ToString(),
            condition = condition,
            stackTrace = stackTrace,
            timestamp = DateTime.Now.ToString("HH:mm:ss")
        });
    }

    private static bool MeetsMinimumSeverity(LogType entryType, LogType minimumSeverity)
    {
        return GetSeverityRank(entryType) >= GetSeverityRank(minimumSeverity);
    }

    private static int GetSeverityRank(LogType logType)
    {
        return logType switch
        {
            LogType.Exception => 4,
            LogType.Error => 3,
            LogType.Assert => 3,
            LogType.Warning => 2,
            _ => 1,
        };
    }

    private static LogType ParseLogType(string type)
    {
        return Enum.TryParse(type, out LogType parsedType) ? parsedType : LogType.Log;
    }

    private static string SanitizeScreenshotFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = fileName;

        foreach (char invalidChar in invalidChars)
            sanitized = sanitized.Replace(invalidChar, '_');

        return sanitized;
    }
}