using UnityEngine;

/// <summary>
/// Partial ViewManager — Outils debug, scénarios de test, contrats IClientSnapshotSource.
/// LaunchDebugScenario, OpenPlanetDebug, BuildClientSnapshot, BuildWorldState, TryBuild*.
/// </summary>
public partial class ViewManager
{
    public bool TryBuildProjectionState(out ProjectionState state)
    {
        return SimulationContractFactory.TryBuildProjectionState(this, out state);
    }

    public bool TryBuildRegionState(out RegionState state)
    {
        return SimulationContractFactory.TryBuildRegionState(this, terraformHUD, progressTracker, out state);
    }

    public ClientSnapshot BuildClientSnapshot()
    {
        return SimulationContractFactory.BuildClientSnapshot(this, terraformHUD, progressTracker, TickManager.Instance);
    }

    public WorldState BuildWorldState()
    {
        return SimulationContractFactory.BuildWorldState(this, terraformHUD, progressTracker, TickManager.Instance);
    }

    public bool OpenPlanetDebug(OrbitalBody body)
    {
        if (body == null)
            return false;

        OpenPlanet(body, Vector3.zero);
        return true;
    }

    public bool LaunchDebugScenario(TestScenarioPreset preset, float latitude, float longitude)
    {
        if (preset == null || preset.body == null)
            return false;

        ShowProjectedPlanet(preset.body, preset.coherenceOverride, _activeProjectionWaterLevel);

        if (!preset.openLocalView)
            return true;

        _previousStateBeforeLocal = _state;
        MapRegion region = BuildRegion(Mathf.Clamp01(latitude), Mathf.Clamp01(longitude), preset.coherenceOverride);

        SetActiveRoot(hexGridRoot);
        hexGrid.LoadRegion(region);
        ApplyLocalRuntimeContext(region);

        Bounds gridBounds = hexGrid.GetWorldBounds();
        float fittedZoom = Mathf.Max(gridBounds.size.x, gridBounds.size.z) * 1.35f;
        float appliedZoom = Mathf.Max(localStartZoom, fittedZoom);

        cameraController.SetMode(CameraController.CameraMode.OrthoTopDown,
                                 localMinZoom, localMaxZoom);
        cameraController.FocusOn(gridBounds.center, appliedZoom);

        _state = ViewState.Local;
        OnViewChanged?.Invoke(_state);
        Debug.Log($"[ViewManager] → Debug Scenario {preset.displayName} | lat={latitude:F2} lon={longitude:F2} | override={preset.coherenceOverride}");
        RequestAuthoritativeRegionSync(latitude, longitude);

        return true;
    }
}
