using UnityEngine;
using System;

/// <summary>
/// Calcule et suit la progression globale de la terraformation.
///
/// Définition de "hex habitable" :
///   - biome = Vegetation (objectif principal)
///   - OU biome = Eau hors-gel (accès à l'eau liquide)
///   - OU température dans la plage [−10°C, +50°C] et waterRatio > 0.05
///
/// La progression est un ratio [0–1] : nbHexesHabitables / nbHexesTotal.
///
/// Le tracker se met à jour automatiquement sur chaque tick (via TickManager)
/// et émet OnProgressChanged quand la valeur change de plus de 0.001.
/// </summary>
public class TerraformProgressTracker : MonoBehaviour
{
    // =========================================================
    // Events
    // =========================================================

    /// <summary>Déclenché quand la progression change. Paramètre : ratio [0–1].</summary>
    public event Action<float> OnProgressChanged;

    // =========================================================
    // Inspector
    // =========================================================

    [SerializeField] private HexGrid hexGrid;

    // =========================================================
    // Runtime
    // =========================================================

    private float _lastProgress = -1f;

    // =========================================================
    // Propriété publique
    // =========================================================

    /// <summary>Progression actuelle [0–1].</summary>
    public float Progress { get; private set; }

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Start()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick += HandleTick;
    }

    private void OnDestroy()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick -= HandleTick;
    }

    // =========================================================
    // Tick handler
    // =========================================================

    private void HandleTick(int _)
    {
        Refresh();
    }

    // =========================================================
    // Calcul
    // =========================================================

    /// <summary>Force un recalcul immédiat (utile à l'ouverture d'une région).</summary>
    public void Refresh()
    {
        HexCell[] cells = hexGrid?.GetCells();
        if (cells == null || cells.Length == 0) return;

        int habitable = 0;
        foreach (HexCell cell in cells)
        {
            if (IsHabitable(cell))
                habitable++;
        }

        float newProgress = (float)habitable / cells.Length;
        Progress = newProgress;

        // N'émet l'event que si la valeur a varié de façon significative
        if (Mathf.Abs(newProgress - _lastProgress) > 0.001f)
        {
            _lastProgress = newProgress;
            OnProgressChanged?.Invoke(newProgress);
        }
    }

    // =========================================================
    // Critère d'habitabilité
    // =========================================================

    private static bool IsHabitable(HexCell cell)
    {
        if (cell == null) return false;

        // Biomes directement habitables
        if (cell.terrain != null)
        {
            if (cell.terrain.terrainType == TerrainType.Vegetation) return true;
            if (cell.terrain.terrainType == TerrainType.Eau) return true;
        }

        // Critère physique : température viable et eau minimale
        float t = cell.state.tempLocale;
        float w = cell.state.waterRatio;
        return (t >= -10f && t <= 50f && w >= 0.05f);
    }
}
