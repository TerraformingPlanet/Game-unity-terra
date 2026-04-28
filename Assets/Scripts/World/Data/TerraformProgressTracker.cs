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

    private ITickSource _tickSource;
    private IHexCellStore _cellStore;

    // =========================================================
    // Runtime
    // =========================================================

    private float _lastProgress = -1f;
    private bool _useAuthoritativeProgress;

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
        if (_tickSource == null)
            _tickSource = TickManager.Instance;
        if (_cellStore == null)
            _cellStore = hexGrid;

        if (_tickSource != null)
            _tickSource.OnTick += HandleTick;
    }

    private void OnDestroy()
    {
        if (_tickSource != null)
            _tickSource.OnTick -= HandleTick;
    }

    public void ConfigureRuntime(ITickSource tickSource, IHexCellStore cellStore)
    {
        if (_tickSource != null)
            _tickSource.OnTick -= HandleTick;

        _tickSource = tickSource;
        _cellStore = cellStore;

        if (_tickSource != null && isActiveAndEnabled)
            _tickSource.OnTick += HandleTick;
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
        if (_useAuthoritativeProgress)
            return;

        HexCell[] cells = _cellStore?.GetCells();
        if (cells == null || cells.Length == 0) return;

        int habitable = 0;
        foreach (HexCell cell in cells)
        {
            if (TerraformHabitabilityEvaluator.IsHabitable(cell))
                habitable++;
        }

        ApplyProgress((float)habitable / cells.Length);
    }

    public void SetAuthoritativeProgress(float progress)
    {
        _useAuthoritativeProgress = true;
        ApplyProgress(Mathf.Clamp01(progress));
    }

    public void ClearAuthoritativeProgress()
    {
        _useAuthoritativeProgress = false;
    }

    private void ApplyProgress(float newProgress)
    {
        Progress = newProgress;

        if (Mathf.Abs(newProgress - _lastProgress) > 0.001f)
        {
            _lastProgress = newProgress;
            OnProgressChanged?.Invoke(newProgress);
        }
    }

}
