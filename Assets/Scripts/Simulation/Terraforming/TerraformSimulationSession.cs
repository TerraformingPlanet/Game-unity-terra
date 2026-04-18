using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TerraformSimulationSession
{
    private struct ActionPending
    {
        public HexCell cell;
        public TerraformActionData action;
        public int ticksRemaining;
    }

    private readonly List<ActionPending> _pending = new List<ActionPending>();
    private readonly BiomeSystem _biomeSystem = new BiomeSystem();
    private readonly HydrologySystem _hydrologySystem = new HydrologySystem();
    private readonly WaterClassificationSystem _waterClassificationSystem = new WaterClassificationSystem();
    private readonly RiverSystem _riverSystem = new RiverSystem();

    public GenerationContext CurrentContext { get; private set; }
    public int PendingCount => _pending.Count;
    public bool HasContext => CurrentContext != null;

    public void SetContext(GenerationContext context)
    {
        CurrentContext = context;
    }

    public bool EnqueueAction(HexCell cell, TerraformActionData action)
    {
        if (cell == null || action == null)
            return false;

        if (!action.CanApply(cell))
            return false;

        _pending.Add(new ActionPending
        {
            cell = cell,
            action = action,
            ticksRemaining = action.durationTicks
        });

        return true;
    }

    public bool DebugApplyDirectState(HexCell cell,
                                      float waterDelta,
                                      float temperatureDelta,
                                      IHexCellStore cellStore,
                                      IGridRefreshSink refreshSink,
                                      Action<HexCell> onCellBiomeChanged)
    {
        if (cell == null || CurrentContext == null || cellStore == null)
            return false;

        HexPhysicalState state = cell.state;
        state.waterRatio = Mathf.Clamp01(state.waterRatio + waterDelta);
        state.tempLocale += temperatureDelta;
        cell.state = state;

        TerrainData previousTerrain = cell.terrain;
        RecomputeLocalState(cellStore);
        refreshSink?.RefreshAllCells();

        if (cell.terrain != previousTerrain)
            onCellBiomeChanged?.Invoke(cell);

        return true;
    }

    public void ProcessTick(IGridRefreshSink refreshSink, Action<HexCell> onCellBiomeChanged)
    {
        if (_pending.Count == 0)
            return;

        for (int index = _pending.Count - 1; index >= 0; index--)
        {
            ActionPending entry = _pending[index];

            ApplyModifier(entry.cell, entry.action.modifier);

            TerrainData previousTerrain = entry.cell.terrain;
            ReevaluateBiome(entry.cell);

            if (entry.cell.terrain != previousTerrain)
            {
                refreshSink?.RefreshCell(entry.cell);
                onCellBiomeChanged?.Invoke(entry.cell);
                Debug.Log($"[TerraformSimulationSession] Biome modifie ({entry.cell.Q},{entry.cell.R}) -> {entry.cell.terrain?.displayName}");
            }

            entry.ticksRemaining--;
            if (entry.ticksRemaining <= 0)
            {
                Debug.Log($"[TerraformSimulationSession] Action terminee : {entry.action.displayName} sur ({entry.cell.Q},{entry.cell.R})");
                _pending.RemoveAt(index);
                continue;
            }

            _pending[index] = entry;
        }
    }

    private static void ApplyModifier(HexCell cell, HexStateModifier modifier)
    {
        HexPhysicalState state = cell.state;

        state.tempLocale += modifier.tempDelta;
        state.waterRatio = Mathf.Clamp01(state.waterRatio + modifier.waterDelta);
        state.toxinLevel = Mathf.Clamp01(state.toxinLevel + modifier.toxinDelta);
        state.soil.organicContent = Mathf.Clamp01(state.soil.organicContent + modifier.organicDelta);
        state.soil.rockHardness = Mathf.Clamp01(state.soil.rockHardness + modifier.hardnessDelta);
        state.soil.mineralDensity = Mathf.Clamp01(state.soil.mineralDensity + modifier.mineralDelta);

        if (state.toxinLevel <= 0f)
            state.soil.toxicSoil = false;

        cell.state = state;
    }

    private void ReevaluateBiome(HexCell cell)
    {
        if (CurrentContext == null)
            return;

        _biomeSystem.Execute(new[] { cell }, CurrentContext);
    }

    private void RecomputeLocalState(IHexCellStore cellStore)
    {
        if (CurrentContext == null || cellStore == null || !cellStore.HasCells())
            return;

        HexCell[] cells = cellStore.GetCells();
        _hydrologySystem.Execute(cells, CurrentContext);
        _waterClassificationSystem.Execute(cells, CurrentContext);
        _biomeSystem.Execute(cells, CurrentContext);
        _riverSystem.Execute(cells, CurrentContext);
    }
}