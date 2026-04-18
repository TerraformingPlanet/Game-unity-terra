using System;

[Serializable]
public enum SimulationCommandType
{
    None = 0,
    LoadProjection = 1,
    OpenRegion = 2,
    QueueTerraformAction = 3,
    ApplyDirectCellDelta = 4,
    PauseTick = 5,
    ResumeTick = 6,
    CaptureSnapshot = 7
}

[Serializable]
public enum SimulationEventType
{
    None = 0,
    ProjectionLoaded = 1,
    RegionLoaded = 2,
    ActionQueued = 3,
    ActionRejected = 4,
    CellUpdated = 5,
    TickAdvanced = 6,
    SnapshotCaptured = 7,
    Error = 8
}

[Serializable]
public struct SimulationVector2State
{
    public float x;
    public float y;

    public UnityEngine.Vector2 ToVector2() => new UnityEngine.Vector2(x, y);
}

[Serializable]
public struct SimulationCoordinates
{
    public float latitude;
    public float longitude;
}

[Serializable]
public struct SimulationCellAddress
{
    public int q;
    public int r;
}

[Serializable]
public struct SimulationSoilState
{
    public float rockHardness;
    public float organicContent;
    public float porosity;
    public float mineralDensity;
    public bool toxicSoil;
    public float thermalConductivity;
}

[Serializable]
public struct SimulationCellState
{
    public SimulationCellAddress address;
    public string terrainName;
    public TerrainType terrainType;
    public WorldLayer layer;
    public float altitude;
    public float temperature;
    public float waterRatio;
    public float toxinLevel;
    public SimulationVector2State windVector;
    public float windSpeed;
    public bool rainShadow;
    public bool hasRiver;
    public int flowAccumulation;
    public TerrainClass terrainClass;
    public WaterClassification waterClassification;
    public bool hasDownstream;
    public SimulationCellAddress downstream;
    public bool hasOverflowOutlet;
    public SimulationCellAddress overflowOutlet;
    public SimulationSoilState soil;
    /// <summary>Position sur la sphère planétaire (normalisée [0,1]). Renseigné uniquement en mode overlay globe.</summary>
    public float latOnSphere;
    public float lonOnSphere;
}

[Serializable]
public struct SimulationWeatherState
{
    public SimulationVector2State prevailingWindDirection;
    public float prevailingWindSpeed;
    public float precipitationRate;
    public float temperatureOffset;
    public float seasonalModifier;
}

[Serializable]
public struct SimulationCoherenceState
{
    public TerrainType dominantTerrainType;
    public float projectedWaterRatio;
    public float oceanicity;
    public float deserticity;
    public float frigidity;
    public bool isExtremeOcean;
    public bool isExtremeArid;
    public bool isExtremeFrozen;
}

// =========================================================
// Méthodes de conversion vers les types Unity runtime
// =========================================================

public static class SimulationContractExtensions
{
    public static PlanetaryWeatherState ToPlanetaryWeatherState(this SimulationWeatherState simWeather)
    {
        return new PlanetaryWeatherState
        {
            prevailingWindDir = simWeather.prevailingWindDirection.ToVector2(),
            prevailingWindSpeed = simWeather.prevailingWindSpeed,
            precipitationRate = simWeather.precipitationRate,
            temperatureOffset = simWeather.temperatureOffset,
            seasonalModifier = simWeather.seasonalModifier
        };
    }

    public static MapRegion.CoherenceConstraint ToCoherenceConstraint(this SimulationCoherenceState simCoherence)
    {
        return new MapRegion.CoherenceConstraint
        {
            dominantTerrainType = simCoherence.dominantTerrainType,
            projectedWaterRatio = simCoherence.projectedWaterRatio,
            oceanicity = simCoherence.oceanicity,
            deserticity = simCoherence.deserticity,
            frigidity = simCoherence.frigidity,
            isExtremeOcean = simCoherence.isExtremeOcean,
            isExtremeArid = simCoherence.isExtremeArid,
            isExtremeFrozen = simCoherence.isExtremeFrozen
        };
    }
}

[Serializable]
public struct ProjectionState
{
    public bool isValid;
    public string planetName;
    public DebugCoherenceOverride projectionOverride;
    public float projectionWaterLevel;
    public PlanetaryHexGrid.ProjectionDebugSummary summary;
}

[Serializable]
public struct RegionState
{
    public bool isValid;
    public int seed;
    public string planetName;
    public SimulationCoordinates coordinates;
    public float terraformationProgress;
    public SimulationWeatherState weather;
    public SimulationCoherenceState coherence;
    public HexGridDebugSummary summary;
    public bool hasSelectedCell;
    public SimulationCellState selectedCell;
    public SimulationCellState[] cells;
}

[Serializable]
public struct WorldState
{
    public bool isValid;
    public int tickCount;
    public bool tickRunning;
    public string activePlanetName;
    public DebugCoherenceOverride projectionOverride;
    public float projectionWaterLevel;
    public bool hasProjection;
    public ProjectionState projection;
    public bool hasRegion;
    public RegionState region;
}

[Serializable]
public struct ClientSnapshot
{
    public bool isValid;
    public string currentView;
    public string activePlanetName;
    public int tickCount;
    public bool tickRunning;
    public float terraformationProgress;
    public bool hasProjection;
    public ProjectionState projection;
    public bool hasRegion;
    public RegionState region;
}

[Serializable]
public struct SimulationCommand
{
    public string commandId;
    public SimulationCommandType type;
    public string planetName;
    public SimulationCoordinates coordinates;
    public SimulationCellAddress cell;
    public TerraformAction actionType;
    public float waterDelta;
    public float temperatureDelta;
}

[Serializable]
public struct SimulationActionDefinition
{
    public TerraformAction actionType;
    public string displayName;
    public int durationTicks;
    public HexStateModifier modifier;
}

[Serializable]
public struct SimulationActionCatalog
{
    public SimulationActionDefinition[] actions;
}

[Serializable]
public struct SimulationEvent
{
    public string eventId;
    public SimulationEventType type;
    public int tickCount;
    public string message;
    public bool hasRegion;
    public SimulationCoordinates coordinates;
    public bool hasCell;
    public SimulationCellAddress cell;
}