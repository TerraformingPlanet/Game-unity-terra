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
/// Mirrors Python SocialClass enum (Phase 7.3).
/// </summary>
public enum SocialClass : int { Poor = 0, Middle = 1, Rich = 2 }

/// <summary>
/// Mirrors Python PopulationTier (Phase 7.3).
/// </summary>
[Serializable]
public struct PopulationTier
{
    public SocialClass socialClass;
    public int         count;
}

/// <summary>
/// Mirrors Python ClaimedTile. Represents one hex tile claimed by a corporation.
/// </summary>
[Serializable]
public struct ClaimedTile
{
    public string          bodyId;
    public string          tileId;
    public PopulationTier[] population;  // Phase 7.3
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
    Road        = 4,   // Phase 9.1
    SeaPort     = 5,   // Phase 9.1
    Spaceport   = 6,   // Phase 9.1
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
    public float         globalReputation;  // Phase 7.5
}

/// <summary>
/// Mirrors Python ResourceType (tradable subset). Prefixed Corp* to avoid collision (Phase 7.3).
/// Phase 9.5 extended with Iron, Oxygen, Water, Tech.
/// </summary>
public enum CorpResourceType : int { Minerals = 0, Food = 1, Energy = 2, ResearchPoints = 3, Waste = 4, Iron = 5, Oxygen = 6, Water = 7, Tech = 8 }

/// <summary>
/// Mirrors Python ResourceListing (Phase 7.3).
/// </summary>
[Serializable]
public struct ResourceListing
{
    public CorpResourceType resourceType;
    public float            price;
    public float            supply;
    public float            demand;
    public float            priceVelocity;              // Phase 9.4 — fractional change per tick
    public float[]          priceHistory;              // Phase 9.4 — last 10 prices
}

/// <summary>
/// Mirrors Python LocalMarketState (Phase 7.3). Deserialised from GET /game/market/{corp_id}.
/// </summary>
[Serializable]
public struct LocalMarketState
{
    public string            corpId;
    public ResourceListing[] listings;
    public float             taxRate;
    public int               tickComputed;
}

/// <summary>
/// Mirrors Python GlobalMarketState (Phase 9.5). Aggregated market across a system.
/// Deserialised from GET /game/market/global/{system_id}.
/// </summary>
[Serializable]
public struct GlobalMarketState
{
    public string            systemId;
    public ResourceListing[] listings;
    public int               tick;
    public int               marketCount;
}

/// <summary>Wrapper for JSON deserialization of GlobalMarketState.</summary>
[Serializable]
public class GlobalMarketStateWrapper
{
    public GlobalMarketState item;
}

// ── Contract layer (Phase 7.4) ───────────────────────────────────────────────

/// <summary>Mirrors Python ContractStatus enum.</summary>
public enum CorpContractStatus : int
{
    Proposed  = 0,
    Active    = 1,
    Completed = 2,
    Broken    = 3,
    Expired   = 4,
}

/// <summary>Mirrors Python ContractVisibility enum.</summary>
public enum CorpContractVisibility : int
{
    Public  = 0,
    Private = 1,
}

/// <summary>
/// Mirrors Python ContractData (Phase 7.4). Deserialised from GET /game/contracts/{id}.
/// </summary>
[Serializable]
public struct CorpContractData
{
    public string                 id;
    public CorpContractStatus     status;
    public CorpContractVisibility visibility;
    public string                 proposerId;
    public string                 targetId;
    public string                 acceptorId;
    public string[]               candidates;
    public CorpResourceType       resourceType;
    public float                  resourceAmount;
    public float                  deliveredAmount;
    public float                  rewardCredits;
    public float                  penaltyCredits;
    public float                  knowledgeBonus;
    public int                    durationTicks;
    public int                    startTick;
    public int                    expiresAtTick;
    public int                    biddingWindowTicks;
    public int                    biddingCloseTick;
    public int                    tickCreated;
}

// ───────────────────────────────────────────────────────────────────
// Phase 7.5 — States & Reputation
// ───────────────────────────────────────────────────────────────────

/// <summary>Mirrors Python StateType (Phase 7.5).</summary>
public enum CorpStateType : int { Capitalist = 0, Nationalist = 1 }

/// <summary>Mirrors Python ReputationEventReason (Phase 7.5).</summary>
public enum CorpReputationEventReason : int
{
    ContractCompleted        = 0,
    ContractBroken           = 1,
    NationalizationTriggered = 2,
    NationalizationCancelled = 3,
    CorruptionDetected       = 4,
}

/// <summary>Mirrors Python StateData (Phase 7.5). Prefixed with Sim to avoid collision with Engine types.</summary>
[Serializable]
public struct SimStateData
{
    public string       id;
    public string       name;
    public CorpStateType stateType;
    public string[]     tileIds;
    public float        bureaucracy;
    public float        corruptionRate;
    public float        toleranceThreshold;
}

/// <summary>Mirrors Python NationalizationProcess (Phase 7.5).</summary>
[Serializable]
public struct NationalizationProcess
{
    public string id;
    public string stateId;
    public string corpId;
    public string tileId;
    public int    startTick;
    public int    completionTick;
    public bool   cancelled;
}

/// <summary>Mirrors Python ScoreboardEntry (Phase 7.5).</summary>
[Serializable]
public struct ScoreboardEntry
{
    public string corpId;
    public string corpName;
    public float  credits;
    public int    tileCount;
    public float  globalReputation;
    public float  score;
}

// ── Phase 9.1 — Routes commerciales & Expéditions ──────────────────────────────

/// <summary>Mirrors Python TradeRouteType.</summary>
public enum CorpTradeRouteType
{
    Land     = 0,
    Maritime = 1,
    Orbital  = 2,
}

/// <summary>Mirrors Python TradeRouteActivityStatus.</summary>
public enum CorpTradeRouteStatus
{
    Active    = 0,
    Suspended = 1,
}

/// <summary>Mirrors Python ExpeditionStatus.</summary>
public enum CorpExpeditionStatus
{
    InTransit = 0,
    Success   = 1,
    Failed    = 2,
}

/// <summary>Mirrors Python TradeRoute (Phase 9.1).</summary>
[Serializable]
public struct CorpTradeRoute
{
    public string              id;
    public CorpTradeRouteType  routeType;
    public string              fromTileId;
    public string              toTileId;
    public string              bodyId;
    public string              ownerCorpId;
    public CorpTradeRouteStatus status;
    public float               baseEfficiency;
    public float               currentEfficiency;
    public float               portMalusFrom;
    public float               portMalusTo;
    public int                 tickCreated;
    public int                 knowledgeTransferTicks;
}

/// <summary>Wrapper for deserializing JSON arrays of CorpTradeRoute.</summary>
[Serializable]
public class CorpTradeRouteList { public CorpTradeRoute[] items; }

/// <summary>Mirrors Python ExpeditionUnit (Phase 9.1).</summary>
[Serializable]
public struct CorpExpeditionUnit
{
    public string              id;
    public string              ownerCorpId;
    public string              fromPortTileId;
    public string              toPortTileId;
    public string              bodyId;
    public CorpTradeRouteType  routeType;
    public int                 ticksRemaining;
    public int                 totalTicks;
    public CorpExpeditionStatus status;
    public bool                isPhantom;
}

/// <summary>Wrapper for deserializing JSON arrays of CorpExpeditionUnit.</summary>
[Serializable]
public class CorpExpeditionList { public CorpExpeditionUnit[] items; }

// ── Phase 8 — Gameplay Events ─────────────────────────────────────────────────

/// <summary>Mirrors Python EventType (Phase 8). Narrative gameplay events.</summary>
public enum GameEventType
{
    RencontreAlienne    = 0,
    TempeteSolaire      = 1,
    DecouverteMiniere   = 2,
    CriseEconomique     = 3,
    SabotageCorpo       = 4,
    Rebellion           = 5,
    MigrationPopulation = 6,
}

/// <summary>Mirrors Python EventEffect (Phase 8).</summary>
[Serializable]
public struct GameEventEffect
{
    public string resourceType;
    public float  resourceDelta;
    public float  creditsDelta;
    public float  reputationDelta;
    public float  populationDelta;
}

/// <summary>Mirrors Python EventData (Phase 8).</summary>
[Serializable]
public struct GameEventData
{
    public string         id;
    public GameEventType  eventType;
    public string         name;
    public string         description;
    public int            tick;
    public string         affectedEntityId;
    public string         affectedEntityType;
    public GameEventEffect effect;
    public bool           isResolved;
}

/// <summary>Wrapper for deserializing JSON arrays of GameEventData.</summary>
[Serializable]
public class GameEventList { public GameEventData[] items; }

// ── Agent LLM (Phase 8.5) ────────────────────────────────────────────────────

/// <summary>Mirrors Python AgentActionType (Phase 8.5).</summary>
public enum AgentActionType
{
    NoOp                   = 0,
    ProposeContract        = 1,
    SetTolerance           = 2,
    TriggerNationalization = 3,
}

/// <summary>Mirrors Python AgentAction (Phase 8.5) — single LLM decision.</summary>
[Serializable]
public struct AgentAction
{
    public string          entityId;
    public AgentActionType actionType;
    public string          paramsJson;   // serialized params dict (JSON string)
    public string          reasoning;
}

/// <summary>Mirrors Python AgentMemory (Phase 8.5) — per-entity rolling memory.</summary>
[Serializable]
public struct AgentMemory
{
    public string   entityId;
    public string   entityType;
    public string[] recentDecisions;
    public int      lastTickActed;
}