using UnityEngine;
using System;
using System.Collections.Generic;

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

    // =========================================================
    // Inspector
    // =========================================================

    [SerializeField] private HexGrid hexGrid;

    // =========================================================
    // Runtime
    // =========================================================

    private readonly List<ActionPending> _pending = new List<ActionPending>();
    private readonly BiomeSystem         _biomeSystem = new BiomeSystem();

    // Contexte biome minimal (corps actif — mis à jour par SetContext)
    private GenerationContext _ctx;

    // =========================================================
    // Struct interne
    // =========================================================

    private struct ActionPending
    {
        public HexCell          cell;
        public TerraformActionData action;
        public int              ticksRemaining;
    }

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Start()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick += HandleTick;
        else
            Debug.LogWarning("[TerraformSystem] TickManager introuvable — souscription différée.");
    }

    private void OnDestroy()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick -= HandleTick;
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
        _ctx = ctx;
    }

    /// <summary>
    /// Soumet une action de terraformation sur un hex.
    /// Si action.CanApply() échoue, l'action est ignorée.
    /// </summary>
    public bool ApplyAction(HexCell cell, TerraformActionData action)
    {
        if (cell == null || action == null) return false;
        if (!action.CanApply(cell)) return false;

        _pending.Add(new ActionPending
        {
            cell          = cell,
            action        = action,
            ticksRemaining = action.durationTicks
        });

        Debug.Log($"[TerraformSystem] Action ajoutée : {action.displayName} sur ({cell.Q},{cell.R})");
        return true;
    }

    /// <summary>Nombre d'actions de terraformation en cours.</summary>
    public int PendingCount => _pending.Count;

    // =========================================================
    // Tick handler
    // =========================================================

    private void HandleTick(int tickNumber)
    {
        if (_pending.Count == 0) return;

        // Traitement en arrière pour supprimer les actions terminées pendant l'itération
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            ActionPending entry = _pending[i];

            // Applique les modificateurs de ce tick
            ApplyModifier(entry.cell, entry.action.modifier);

            // Rejoue le biome localement
            TerrainData previousTerrain = entry.cell.terrain;
            ReevaluateBiome(entry.cell);

            // Notifie si le biome a changé
            if (entry.cell.terrain != previousTerrain)
            {
                hexGrid?.RefreshCell(entry.cell);
                OnCellBiomeChanged?.Invoke(entry.cell);
                Debug.Log($"[TerraformSystem] Biome modifié ({entry.cell.Q},{entry.cell.R}) → {entry.cell.terrain?.displayName}");
            }

            // Décrémente le compteur de ticks
            _pending[i] = new ActionPending
            {
                cell          = entry.cell,
                action        = entry.action,
                ticksRemaining = entry.ticksRemaining - 1
            };

            if (_pending[i].ticksRemaining <= 0)
            {
                Debug.Log($"[TerraformSystem] Action terminée : {entry.action.displayName} sur ({entry.cell.Q},{entry.cell.R})");
                _pending.RemoveAt(i);
            }
        }
    }

    // =========================================================
    // Helpers internes
    // =========================================================

    private static void ApplyModifier(HexCell cell, HexStateModifier mod)
    {
        HexPhysicalState s = cell.state;   // copie de la struct

        s.tempLocale               += mod.tempDelta;
        s.waterRatio                = Mathf.Clamp01(s.waterRatio    + mod.waterDelta);
        s.toxinLevel                = Mathf.Clamp01(s.toxinLevel    + mod.toxinDelta);
        s.soil.organicContent       = Mathf.Clamp01(s.soil.organicContent  + mod.organicDelta);
        s.soil.rockHardness         = Mathf.Clamp01(s.soil.rockHardness    + mod.hardnessDelta);
        s.soil.mineralDensity       = Mathf.Clamp01(s.soil.mineralDensity  + mod.mineralDelta);

        // Si les toxines sont éradiquées, nettoyer toxicSoil
        if (s.toxinLevel <= 0f) s.soil.toxicSoil = false;

        cell.state = s;   // réaffecte la struct modifiée
    }

    private void ReevaluateBiome(HexCell cell)
    {
        if (_ctx == null)
        {
            Debug.LogWarning("[TerraformSystem] Pas de GenerationContext — réévaluation impossible.");
            return;
        }

        // BiomeSystem.Execute() travaille sur un tableau → on l'applique sur ce seul hex
        _biomeSystem.Execute(new HexCell[] { cell }, _ctx);
    }
}
