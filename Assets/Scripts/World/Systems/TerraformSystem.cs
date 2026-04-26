using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Applique des actions de terraformation sur des hexagones, fait évoluer
/// HexPhysicalState sur plusieurs ticks, et rejoue BiomeSystem localement.
///
/// Architecture :
///   - Les actions sont soumises via ApplyAction(cell, actionData).
///   - Chaque action est stockée comme une ActionPending (cell + data + ticks restants).
///   - Sur OnTick, TerraformSystem applique un tick de modificateur à chaque action pending,
///     rejoue BiomeSystem sur la cellule, et notifie HexGrid de rafraîchir la couleur.
///   - Une action terminée est retirée de la liste.
///
/// Prérequis :
///   - TerraformSystem doit s'abonner à TickManager.Instance.OnTick dans Awake/Start.
///   - HexGrid doit être assigné en Inspector ou via Init().
/// </summary>
public class TerraformSystem : MonoBehaviour
{
    // =========================================================
    // Events
    // =========================================================

    /// <summary>Déclenché quand le biome d'une cellule change suite à une action.</summary>
    public event Action<HexCell> OnCellBiomeChanged;
    public event Action<HexCell> OnAuthoritativeCellSynchronized;
    public event Action<WorldState> OnAuthoritativeWorldStateSynchronized;

    // =========================================================
    // Inspector
    // =========================================================

    [SerializeField] private HexGrid hexGrid;
    [SerializeField] private bool preferServerCommands = true;
    [SerializeField] private bool fallbackToLocalSimulationOnServerFailure = true;
    [SerializeField] private string simulationServerUrl = "http://127.0.0.1:8080";
    [SerializeField] private float simulationServerTimeoutSeconds = 15f;
    [SerializeField] private float serverPollingIntervalSeconds = 5f;

    private ITickSource _tickSource;
    private IHexCellStore _cellStore;
    private IGridRefreshSink _refreshSink;

    // =========================================================
    // Runtime
    // =========================================================

    private readonly TerraformSimulationSession _simulationSession = new TerraformSimulationSession();
    private bool _hasAuthoritativeWorldState;
    private WorldState _lastAuthoritativeWorldState;
    private int _lastServerTickCount = -1;

    public bool HasAuthoritativeWorldState => _hasAuthoritativeWorldState;
    public WorldState LastAuthoritativeWorldState => _lastAuthoritativeWorldState;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Start()
    {
        if (_cellStore == null)
            _cellStore = hexGrid;
        if (_refreshSink == null)
            _refreshSink = hexGrid;
        if (_tickSource == null)
            _tickSource = TickManager.Instance;

        if (_tickSource != null)
            _tickSource.OnTick += HandleTick;
        else
            Debug.LogWarning("[TerraformSystem] TickManager introuvable — souscription différée.");

        if (preferServerCommands)
            StartCoroutine(PollServerWorldState());
    }

    private void OnDestroy()
    {
        if (_tickSource != null)
            _tickSource.OnTick -= HandleTick;
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Fournit le contexte de génération (corps céleste + région) nécessaire
    /// pour rejouer BiomeSystem. Appelé par ViewManager après OpenRegion().
    /// </summary>
    public void SetContext(GenerationContext ctx)
    {
        _simulationSession.SetContext(ctx);
    }

    /// <summary>
    /// Applique un contexte injecté depuis l'état serveur autoritatif,
    /// sans recalculer la météo et la cohérence localement.
    /// </summary>
    public void ApplyAuthoritativeContext(RegionState regionState, MapRegion region)
    {
        if (_cellStore == null)
            return;

        var injectedWeather = regionState.weather.ToPlanetaryWeatherState();
        var injectedCoherence = regionState.coherence.ToCoherenceConstraint();

        var cells = _cellStore.GetCells();
        var ctx = GenerationContext.BuildWithInjected(cells, region, injectedWeather, injectedCoherence);

        _simulationSession.SetContext(ctx);
    }

    public void ConfigureRuntime(ITickSource tickSource, IHexCellStore cellStore, IGridRefreshSink refreshSink)
    {
        if (_tickSource != null)
            _tickSource.OnTick -= HandleTick;

        _tickSource = tickSource;
        _cellStore = cellStore;
        _refreshSink = refreshSink;

        if (_tickSource != null && isActiveAndEnabled)
            _tickSource.OnTick += HandleTick;
    }

    /// <summary>
    /// Soumet une action de terraformation sur un hex.
    /// Si action.CanApply() échoue, l'action est ignorée.
    /// </summary>
    public bool ApplyAction(HexCell cell, TerraformActionData action)
    {
        if (ShouldUseServerCommands(cell, action))
        {
            StartCoroutine(QueueActionOnServer(cell, action));
            return true;
        }

        bool queued = _simulationSession.EnqueueAction(cell, action);

        if (queued)
            Debug.Log($"[TerraformSystem] Action ajoutee : {action.displayName} sur ({cell.Q},{cell.R})");

        return queued;
    }

    public bool DebugApplyDirectState(HexCell cell, float waterDelta, float temperatureDelta)
    {
        if (cell == null)
            return false;

        if (ShouldUseServerCommands(cell, null))
        {
            StartCoroutine(ApplyDirectCellDeltaOnServer(cell, waterDelta, temperatureDelta));
            return true;
        }

        if (!_simulationSession.HasContext)
        {
            Debug.LogWarning("[TerraformSystem] DebugApplyDirectState impossible sans GenerationContext.");
            return false;
        }

        return _simulationSession.DebugApplyDirectState(cell, waterDelta, temperatureDelta, _cellStore, _refreshSink, cellChanged => OnCellBiomeChanged?.Invoke(cellChanged));
    }

    /// <summary>Nombre d'actions de terraformation en cours.</summary>
    public int PendingCount => _simulationSession.PendingCount;
    public bool HasContext => _simulationSession.HasContext;
    public GenerationContext CurrentContext => _simulationSession.CurrentContext;

    // =========================================================
    // Tick handler
    // =========================================================

    private void HandleTick(int tickNumber)
    {
        if (_hasAuthoritativeWorldState) return; // serveur autoritatif → tick local inhibé
        _simulationSession.ProcessTick(_refreshSink, cellChanged => OnCellBiomeChanged?.Invoke(cellChanged));
    }

    // =========================================================
    // Polling serveur §4.1
    // =========================================================

    private IEnumerator PollServerWorldState()
    {
        var wait = new WaitForSeconds(serverPollingIntervalSeconds);
        while (true)
        {
            yield return wait;
            if (isActiveAndEnabled)
                yield return FetchServerWorldState();
        }
    }

    private IEnumerator FetchServerWorldState()
    {
        string url = simulationServerUrl.TrimEnd('/') + "/world";
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[TerraformSystem] Polling serveur indisponible ({request.error}).");
            yield break;
        }

        WorldState worldState;
        try
        {
            worldState = JsonUtility.FromJson<WorldState>(request.downloadHandler.text);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TerraformSystem] Réponse polling invalide ({ex.Message}).");
            yield break;
        }

        if (!worldState.isValid)
            yield break;

        if (worldState.tickCount <= _lastServerTickCount)
            yield break;

        _lastServerTickCount = worldState.tickCount;
        ApplyAuthoritativeWorldState(worldState, null);
    }

    private bool ShouldUseServerCommands(HexCell cell, TerraformActionData action)
    {
        if (!preferServerCommands || !isActiveAndEnabled || cell == null)
            return false;

        if (action == null)
            return true;

        return true;
    }

    private IEnumerator QueueActionOnServer(HexCell cell, TerraformActionData action)
    {
        string url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/commands/queue-action?action_type={1}&q={2}&r={3}",
            simulationServerUrl.TrimEnd('/'),
            (int)action.actionType,
            cell.Q,
            cell.R);

        yield return SendWorldStateCommand(
            url,
            $"Action serveur soumise : {action.displayName} sur ({cell.Q},{cell.R})",
            () => FallbackQueueAction(cell, action),
            cell);
    }

    private IEnumerator ApplyDirectCellDeltaOnServer(HexCell cell, float waterDelta, float temperatureDelta)
    {
        string url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/commands/apply-cell-delta?water_delta={1}&temperature_delta={2}&q={3}&r={4}",
            simulationServerUrl.TrimEnd('/'),
            waterDelta,
            temperatureDelta,
            cell.Q,
            cell.R);

        yield return SendWorldStateCommand(
            url,
            $"Delta serveur applique sur ({cell.Q},{cell.R})",
            () => FallbackApplyDirectState(cell, waterDelta, temperatureDelta),
            cell);
    }

    private IEnumerator SendWorldStateCommand(string url, string successMessage, Action fallbackAction, HexCell preferredCell)
    {
        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            downloadHandler = new DownloadHandlerBuffer(),
            uploadHandler = new UploadHandlerRaw(Array.Empty<byte>()),
            timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds))
        };
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[TerraformSystem] Commande serveur indisponible ({request.error}).");
            fallbackAction?.Invoke();
            yield break;
        }

        WorldState worldState;
        try
        {
            worldState = JsonUtility.FromJson<WorldState>(request.downloadHandler.text);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TerraformSystem] Réponse serveur invalide ({ex.Message}).");
            fallbackAction?.Invoke();
            yield break;
        }

        ApplyAuthoritativeWorldState(worldState, preferredCell);
        Debug.Log($"[TerraformSystem] {successMessage}");
    }

    private void FallbackQueueAction(HexCell cell, TerraformActionData action)
    {
        if (!fallbackToLocalSimulationOnServerFailure)
            return;

        bool queued = _simulationSession.EnqueueAction(cell, action);
        if (queued)
            Debug.Log($"[TerraformSystem] Fallback local : action ajoutee {action.displayName} sur ({cell.Q},{cell.R})");
    }

    private void FallbackApplyDirectState(HexCell cell, float waterDelta, float temperatureDelta)
    {
        if (!fallbackToLocalSimulationOnServerFailure || !_simulationSession.HasContext)
            return;

        _simulationSession.DebugApplyDirectState(cell, waterDelta, temperatureDelta, _cellStore, _refreshSink, cellChanged => OnCellBiomeChanged?.Invoke(cellChanged));
    }

    private void ApplyAuthoritativeWorldState(WorldState worldState, HexCell preferredCell)
    {
        _lastAuthoritativeWorldState = worldState;
        _hasAuthoritativeWorldState = worldState.isValid;
        OnAuthoritativeWorldStateSynchronized?.Invoke(worldState);

        if (!worldState.hasRegion)
            return;

        ApplyAuthoritativeRegionState(worldState.region, preferredCell);
    }

    public void SynchronizeAuthoritativeRegionState(RegionState regionState)
    {
        ApplyAuthoritativeRegionState(regionState, null);
    }

    private void ApplyAuthoritativeRegionState(RegionState regionState, HexCell preferredCell)
    {
        if (regionState.cells != null)
        {
            for (int index = 0; index < regionState.cells.Length; index++)
            {
                SimulationCellState cellState = regionState.cells[index];
                HexCell regionCell = ResolveTargetCell(null, cellState.address);
                if (regionCell == null)
                    continue;

                TerrainData previousTerrain = regionCell.terrain;
                ApplySimulationCellState(regionCell, cellState);
                _refreshSink?.RefreshCell(regionCell);

                if (previousTerrain != regionCell.terrain)
                    OnCellBiomeChanged?.Invoke(regionCell);
            }
        }

        if (!regionState.hasSelectedCell)
            return;

        SimulationCellState serverCellState = regionState.selectedCell;

        HexCell targetCell = ResolveTargetCell(preferredCell, serverCellState.address);
        if (targetCell == null)
            return;

        OnAuthoritativeCellSynchronized?.Invoke(targetCell);
    }

    private HexCell ResolveTargetCell(HexCell preferredCell, SimulationCellAddress address)
    {
        if (_cellStore == null)
            return preferredCell;

        HexCell targetCell = _cellStore.GetCell(address.q, address.r);
        return targetCell ?? preferredCell;
    }

    private void ApplySimulationCellState(HexCell cell, SimulationCellState state)
    {
        HexPhysicalState physicalState = cell.state;
        physicalState.altitude = state.altitude;
        physicalState.tempLocale = state.temperature;
        physicalState.waterRatio = state.waterRatio;
        physicalState.toxinLevel = state.toxinLevel;
        physicalState.windVector = new Vector2(state.windVector.x, state.windVector.y);
        physicalState.windSpeed = state.windSpeed;
        physicalState.rainShadow = state.rainShadow;
        physicalState.hasRiver = state.hasRiver;
        physicalState.flowAccumulation = state.flowAccumulation;
        physicalState.terrainClass = state.terrainClass;
        physicalState.waterClassification = state.waterClassification;
        physicalState.hasDownstream = state.hasDownstream;
        physicalState.downstreamQ = state.downstream.q;
        physicalState.downstreamR = state.downstream.r;
        physicalState.hasOverflowOutlet = state.hasOverflowOutlet;
        physicalState.overflowQ = state.overflowOutlet.q;
        physicalState.overflowR = state.overflowOutlet.r;
        physicalState.soil = new SoilProfile
        {
            rockHardness = state.soil.rockHardness,
            organicContent = state.soil.organicContent,
            porosity = state.soil.porosity,
            mineralDensity = state.soil.mineralDensity,
            toxicSoil = state.soil.toxicSoil,
            thermalConductivity = state.soil.thermalConductivity,
        };

        cell.state = physicalState;
        cell.layer = state.layer;

        TerrainData resolvedTerrain = ResolveTerrainData(state.terrainType);
        if (resolvedTerrain != null)
            cell.terrain = resolvedTerrain;
    }

    private TerrainData ResolveTerrainData(TerrainType terrainType)
    {
        OrbitalBody body = CurrentContext != null ? CurrentContext.body : null;
        if (body == null || body.layers == null)
            return null;

        foreach (LayerZone layer in body.layers)
        {
            if (layer == null || layer.biomes == null)
                continue;

            foreach (TerrainData terrain in layer.biomes)
            {
                if (terrain != null && terrain.terrainType == terrainType)
                    return terrain;
            }
        }

        return null;
    }
}
