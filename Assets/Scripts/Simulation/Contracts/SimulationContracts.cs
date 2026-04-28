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
    AtmosphereFormed = 11,
    ExpeditionLost = 12,
    ExpeditionDelayed = 13,
    TradeRouteEstablished = 14
}

[Serializable]
public enum TradeRouteType : int
{
    Land = 0,
    Maritime = 1,
    Orbital = 2
}

[Serializable]
public enum TradeRouteActivityStatus : int
{
    Active = 0,
    Suspended = 1
}

[Serializable]
public enum ExpeditionStatus : int
{
    InTransit = 0,
    Success = 1,
    Failed = 2
}

[Serializable]
public enum TravelStatus : int
{
    InTransit = 0,
    Arrived = 1,
    Cancelled = 2
}

[Serializable]
public enum AgentActionType : int
{
    NoOp = 0,
    ProposeContract = 1,
    SetTolerance = 2,
    TriggerNationalization = 3,
    ClaimTile = 10,
    ConstructBuilding = 11,
    UpdateFsmThresholds = 12,
    ReorderConstructionQueue = 13
}

[Serializable]
public enum CorpProfile : int
{
    Economiste = 0,
    Expansionniste = 1,
    Militariste = 2
}

[Serializable]
public enum BotFSMState : int
{
    Idle = 0,
    Expanding = 1,
    Building = 2,
    Trading = 3,
    Raiding = 4
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
    public float humidity;             // atmospheric humidity noise [0, 1] — generation-time
    public float vegetationDensity;    // [0, 1]
    public float wildlifeDensity;      // [0, 1]
    public float atmosphereDeltaCo2;   // CO₂ volume fraction delta per tick
    public float atmosphereDeltaO2;    // O₂ volume fraction delta per tick
    // Ecology fields (Phase 11.5)
    public SpeciesData[] species;      // active species populations on this tile
    // State/territory fields (Phase Colonisation) — enriched client-side after overlay fetch
    public string stateId;             // ID of the state owning this tile (empty if none)
    public string stateName;           // Display name of the state (empty if none)
}

/// <summary>
/// One species population on a surface tile.
/// Mirrors Python SpeciesData (Phase 11.5).
/// </summary>
[Serializable]
public struct SpeciesData
{
    public string speciesId;       // e.g. "cyanobacteria", "grass"
    public float  density;         // [0, 1]
    public float  minTemp;
    public float  maxTemp;
    public float  minO2;
    public float  maxO2;
    public float  growthRate;
    public float  minVegetation;   // required vegetation density for animal species
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
    Sawmill     = 4,
}

/// <summary>
/// Mirrors Python ConstructionStatus enum (Phase 10.5).
/// </summary>
public enum CorpConstructionStatus : int
{
    Pending    = 0,
    InProgress = 1,
    Done       = 2,
}

/// <summary>
/// Mirrors Python ConstructionItem (Phase 10.5). Returned by POST /game/corporations/{id}/buildings.
/// </summary>
[Serializable]
public struct CorpConstructionItem
{
    public string                 id;
    public CorpBuildingType       buildingType;
    public string                 tileId;
    public string                 bodyId;
    public string                 corpId;
    public CorpConstructionStatus status;
    public int                    ticksRemaining;
    public int                    totalCostPts;
    public int                    pointsAccumulated;
}

/// <summary>
/// Wrapper for deserializing JSON arrays of CorpConstructionItem.
/// </summary>
[Serializable]
public class CorpConstructionItemArray { public CorpConstructionItem[] items; }

/// <summary>
/// Mirrors Python BuildingData (Phase 7.2 / 12). Network contract, distinct from Economy/BuildingData ScriptableObject.
/// </summary>
[Serializable]
public struct CorpBuilding
{
    public string           id;
    public CorpBuildingType buildingType;
    public string           tileId;
    public string           bodyId;
    public string           corpId;
    public float            workerRatio;
    public int              ticksActive;
    public int              level;   // Phase 12 — building level [1–5]; production × level, workers × level
}

/// <summary>
/// Wrapper for deserializing JSON arrays of CorpBuilding.
/// </summary>
[Serializable]
public class CorpBuildingArray { public CorpBuilding[] items; }

/// <summary>
/// Mirrors Python StateData. Deserialised from GET /game/states/{id}.
/// </summary>
[Serializable]
public struct StateData
{
    public string   id;
    public string   name;
    public int      stateType;        // StateType enum value
    public string[] tileIds;
    public string[] territoryIds;
    public float    bureaucracy;
    public float    corruptionRate;
    public float    toleranceThreshold;
    public float    taxRate;
    public float    literacyRate;
    public string   profileKey;
    public bool     isAiControlled;
    // Vassal / soumission system
    public bool     isVassal;
    public string   vassalCorpId;     // null / "" if independent
    // loyalty serialized as flat array of LoyaltyEntry (dict<str,float> → JSON object not supported by JsonUtility)
    public LoyaltyEntry[] loyalty;
}

/// <summary>
/// One entry in StateData.loyalty — bilatéral corp→état loyalty score.
/// </summary>
[Serializable]
public struct LoyaltyEntry
{
    public string corpId;
    public float  value;   // 0..1
}

/// <summary>
/// Mirrors Python CorporationData. Deserialised from GET /game/corporations/{id}.
/// </summary>
[Serializable]
public struct CorporationData
{
    public string        id;
    public string        name;
    public float         credits;
    public float         score;
    public bool          isAI;
    public CorpBuilding[] buildings;
    public float         globalReputation;  // Phase 7.5
    public float         colorR;
    public float         colorG;
    public float         colorB;
}

/// <summary>
/// Mirrors Python StateTileColorDto. Deserialised from GET /game/bodies/{body_id}/state-tile-colors.
/// </summary>
[Serializable]
public struct StateTileColorDto
{
    public string tileId;
    public string stateId;
    public string stateName;
    public string profileKey;
    public float  colorR;
    public float  colorG;
    public float  colorB;
}

/// <summary>
/// Wrapper for deserializing JSON arrays of StateTileColorDto.
/// </summary>
[Serializable]
public class StateTileColorArray { public StateTileColorDto[] items; }

/// <summary>
/// Mirrors Python OwnershipTileDto. Deserialised from GET /bodies/{body_id}/ownership-tiles.
/// </summary>
[Serializable]
public struct OwnershipTileDto
{
    public string tileId;
    public string corpId;
    public float  colorR;
    public float  colorG;
    public float  colorB;
}

/// <summary>
/// Wrapper for deserializing JSON arrays of OwnershipTileDto.
/// </summary>
[Serializable]
public class OwnershipTileDtoArray { public OwnershipTileDto[] items; }

/// <summary>
/// Mirrors Python ResourceType (tradable subset). Prefixed Corp* to avoid collision (Phase 7.3).
/// </summary>
public enum CorpResourceType : int { Minerals = 0, Food = 1, Energy = 2, ResearchPoints = 3, Waste = 4 }

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
    public float            priceVelocity;     // Phase 9.4
    public float[]          priceHistory;      // Phase 9.4 — last 10 prices
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
    public string[]     territoryIds;   // Phase Colonisation
    public float        bureaucracy;
    public float        corruptionRate;
    public float        toleranceThreshold;
    public float        taxRate;
    public float        literacyRate;   // Phase Colonisation
    public string       profileKey;     // Phase Colonisation
    public bool         isAiControlled;
}

/// <summary>Mirrors Python PopDistribution (Phase Colonisation).</summary>
[Serializable]
public struct PopDistribution
{
    public float poor;
    public float middle;
    public float rich;
}

/// <summary>Mirrors Python TerritoryData (Phase Colonisation).</summary>
[Serializable]
public struct TerritoryData
{
    public string   id;
    public string   name;
    public string   stateId;
    public string   bodyId;
    public string[] tileIds;
    public int      populationBase;
    public string   profileKey;
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

// ── Phase 8 — Events ─────────────────────────────────────────────────────────

/// <summary>Mirrors Python EventType enum (Phase 8).</summary>
public enum EventType
{
    RencontreAlienne        = 0,
    TempeteSolaire          = 1,
    DecouverteMiniere       = 2,
    CriseEconomique         = 3,
    SabotageCorpo           = 4,
    Rebellion               = 5,
    MigrationPopulation     = 6,
    DecouverteMegastructure = 7,
    EmpireGalactique        = 8,
    Piraterie               = 9,
    Panne                   = 10,
    Decouverte              = 11,
}

/// <summary>Mirrors Python EventEffect (Phase 8).</summary>
[Serializable]
public struct EventEffect
{
    public string resourceType;
    public float  resourceDelta;
    public float  creditsDelta;
    public float  reputationDelta;
    public float  populationDelta;
}

/// <summary>Mirrors Python EventData (Phase 8).</summary>
[Serializable]
public struct EventData
{
    public string      id;
    public EventType   eventType;
    public string      name;
    public string      description;
    public int         tick;
    public string      affectedEntityId;
    public string      affectedEntityType;
    public EventEffect effect;
    public bool        isResolved;
}

// ── Phase 9.1 — Trade Routes & Expeditions ───────────────────────────────────

/// <summary>Wrapper for dict[str, float] in ExpeditionUnit.cargo.</summary>
[Serializable]
public struct CargoEntry
{
    public string key;
    public float value;
}

/// <summary>Mirrors Python TradeRoute.</summary>
[Serializable]
public struct TradeRoute
{
    public string id;
    public TradeRouteType routeType;
    public string fromTileId;
    public string toTileId;
    public string bodyId;
    public string[] pathTileIds;
    public string ownerCorpId;
    public string[] knownByEntityIds;
    public TradeRouteActivityStatus status;
    public float baseEfficiency;
    public float currentEfficiency;
    public float portMalusFrom;
    public float portMalusTo;
    public int tickCreated;
    public int knowledgeTransferTicks;
}

/// <summary>Mirrors Python ExpeditionUnit.</summary>
[Serializable]
public struct ExpeditionUnit
{
    public string id;
    public string ownerCorpId;
    public string fromPortTileId;
    public string toPortTileId;
    public string bodyId;
    public TradeRouteType routeType;
    public int ticksRemaining;
    public int totalTicks;
    public string[] pathTileIds;
    public ExpeditionStatus status;
    public bool isPhantom;
    public CargoEntry[] cargo;
}

// ── Phase 9.5 — Global Market ───────────────────────────────────────────────

/// <summary>Mirrors Python GlobalMarketState.</summary>
[Serializable]
public struct GlobalMarketState
{
    public string systemId;
    public ResourceListing[] listings;
    public int tick;
    public int marketCount;
}

// ── Phase 8.5 — LLM Agent ───────────────────────────────────────────────────

/// <summary>Wrapper for dict in AgentAction.params.</summary>
[Serializable]
public struct ActionParamEntry
{
    public string key;
    public string value;  // JSON string for complex values
}

/// <summary>Mirrors Python AgentAction.</summary>
[Serializable]
public struct AgentAction
{
    public string entityId;
    public AgentActionType actionType;
    public ActionParamEntry[] parameters;
    public string reasoning;
}

/// <summary>Wrapper for dict[str, str] in AgentMemory.relationshipNotes.</summary>
[Serializable]
public struct RelationshipNote
{
    public string entityId;
    public string note;
}

/// <summary>Mirrors Python AgentMemory.</summary>
[Serializable]
public struct AgentMemory
{
    public string entityId;
    public string entityType;
    public string[] recentDecisions;
    public RelationshipNote[] relationshipNotes;
    public int lastTickActed;
}

// ── Phase 9 — Space Travel ──────────────────────────────────────────────────

/// <summary>Mirrors Python SpaceTravel.</summary>
[Serializable]
public struct SpaceTravel
{
    public string travelId;
    public string factionId;
    public string fromSystemId;
    public string toSystemId;
    public string routeId;
    public float distanceLy;
    public int departedAtTick;
    public int arrivalTick;
    public TravelStatus status;
}

// ── Phase 11.2 — Corporation FSM ───────────────────────────────────────────

/// <summary>Mirrors Python CorpProfile.</summary>
[Serializable]
public struct CorpProfileData
{
    public CorpProfile profile;
}

/// <summary>Mirrors Python BotFSMState.</summary>
[Serializable]
public struct CorpFSMState
{
    public BotFSMState state;
}