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
    Error = 8,
    ThermalEquilibrium = 9,
    HabitabilityThreshold = 10,
    AtmosphereFormed = 11
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
public struct AtmosphericState
{
    public float co2Ratio;
    public float o2Ratio;
    public float atmosphericPressure;
    public float averageTemperature;
    public float toxinRatio;
    public float habitabilityScore;
}

[Serializable]
public struct RegionState
{
    public bool isValid;
    public int seed;
    public string planetName;
    public SimulationCoordinates coordinates;
    public float terraformationProgress;
    public AtmosphericState atmosphericState;
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

/// <summary>
/// Tuile de surface d'un corps sphérique (planète, lune, astéroïde).
/// Miroir de GoldbergTileState (Python) — utiliser GET /bodies/{id}/tiles.
/// tileId est un index H3 hexagonal (ex: "820007fffffffff"), pas un entier.
/// neighborIds liste les H3 voisins directs (jusqu'à 6, ou 5 pour les 12 pentagones).
/// Champs physiques (sprint D) : altitude, albedo, solarIrradiance, végétation, vie,
/// et deltas atmosphériques CO₂/O₂ produits ou consommés par tick par ce tile.
/// </summary>
[Serializable]
public struct GoldbergTileState
{
    public string tileId;
    public string[] neighborIds;
    public float latNorm;
    public float lonNorm;
    public float latDeg;
    public float lonDeg;
    public TerrainType terrainType;
    public WaterClassification waterClassification;
    public TerrainClass terrainClass;
    public float waterRatio;
    public float temperature;
    public float toxinLevel;
    public bool isHabitable;
    // Physical simulation fields (Sprint D)
    public float altitude;             // normalised height relative to sea level [-1, 1]
    public float albedo;               // surface reflectivity [0, 1]
    public float solarIrradiance;      // W/m² at tile surface
    public float vegetationDensity;    // [0, 1]
    public float wildlifeDensity;      // [0, 1]
    public float atmosphereDeltaCo2;   // CO₂ volume fraction delta per tick
    public float atmosphereDeltaO2;    // O₂ volume fraction delta per tick
}

/// <summary>
/// One gas species in a planetary atmosphere.
/// Mirrors Python AtmosphericGas.
/// greenhouseCoeff is game-balanced: CO₂=1.0, CH₄=28.0, H₂O=0.5, N₂=O₂=0.
/// </summary>
[Serializable]
public struct SimulationAtmosphericGas
{
    public string name;
    public float fraction;          // volume fraction [0..1]
    public float greenhouseCoeff;   // relative greenhouse warming factor
    public float molarMass;         // g/mol
}

/// <summary>
/// Full atmospheric composition for a spherical body.
/// Mirrors Python AtmosphericComposition.
/// totalPressureKpa: Earth≈101.3, Mars≈0.6, vacuum=0.
/// </summary>
[Serializable]
public struct SimulationAtmosphericComposition
{
    public SimulationAtmosphericGas[] gases;
    public float totalPressureKpa;

    /// <summary>Backward-compat density [0,1] ≈ totalPressureKpa / 101.3.</summary>
    public float atmosphereDensity => Math.Min(1f, totalPressureKpa / 101.3f);
}

/// <summary>
/// Simplified planetary wind pattern (MVP).
/// dominantWindDeg: direction FROM which prevailing wind blows [0, 360).
/// windIntensity: normalised [0, 1].
/// Mirrors Python GlobalWindPattern.
/// </summary>
[Serializable]
public struct SimulationGlobalWindPattern
{
    public float dominantWindDeg;
    public float windIntensity;
}

/// <summary>
/// Entrée minimale d'un corps (planet, moon…) dans la liste GET /bodies.
/// Utilisée pour résoudre le bodyId avant de paginer les tuiles.
/// </summary>
[Serializable]
public struct SimulationBodyListEntry
{
    public string bodyId;
    public string name;
    public string surfaceType;
}

// ── Corporation layer (Phase 7.1) ─────────────────────────────────────────

/// <summary>
/// Mirrors Python ClaimedTile. Represents one hex tile claimed by a corporation.
/// </summary>
[Serializable]
public struct ClaimedTile
{
    public string bodyId;
    public string tileId;
}

/// <summary>
/// Mirrors Python BuildingType enum (Phase 7.2). Prefixed Corp* to avoid conflict with Economy/BuildingData.
/// </summary>
public enum CorpBuildingType
{
    Mine        = 0,
    Farm        = 1,
    EnergyPlant = 2,
    Research    = 3,
}

/// <summary>
/// Mirrors Python BuildingData (Phase 7.2). Network contract, distinct from Economy/BuildingData ScriptableObject.
/// </summary>
[Serializable]
public struct CorpBuilding
{
    public string         id;
    public CorpBuildingType buildingType;
    public string         tileId;
    public string         bodyId;
    public string         corpId;
    public float          workerRatio;
    public int            ticksActive;
}

/// <summary>
/// Wrapper for deserializing JSON arrays of CorpBuilding.
/// </summary>
[Serializable]
public class CorpBuildingArray { public CorpBuilding[] items; }

/// <summary>
/// Mirrors Python CorporationData. Deserialised from GET /game/corporations/{id}.
/// </summary>
[Serializable]
public struct CorporationData
{
    public string        id;
    public string        name;
    public float         credits;
    public ClaimedTile[] claimedTiles;
    public float         score;
    public bool          isAI;
    public CorpBuilding[] buildings;
}