using UnityEngine;
using System;
using UnityEngine.InputSystem;

/// <summary>
/// Machine d'état gérant les 3 niveaux de vue du jeu.
///
/// États :
///   SolarSystem → active SolarSystemRoot, caméra OrbitPerspective
///   Planet      → active PlanetRoot, caméra OrthoTopDown sur carte projetée
///   Local       → active HexGridRoot, caméra OrthoTopDown
///
/// Transitions déclenchées par :
///   - SolarSystemView.OnPlanetClicked : SolarSystem → Local (ou Planet si désactivé)
///   - PlanetSphere.OnRegionClicked    : Planet → Local (avec MapRegion correspondant)
/// </summary>
public class ViewManager : MonoBehaviour
{
    public enum ViewState { SolarSystem, Planet, Local }

    // =========================================================
    // Events (pour HUD, UI, analytics)
    // =========================================================

    public static event Action<ViewState> OnViewChanged;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Scene Roots")]
    [Tooltip("Parent de tous les objets de la vue système solaire")]
    [SerializeField] private GameObject solarSystemRoot;

    [Tooltip("Parent de tous les objets de la vue planétaire (sphère)")]
    [SerializeField] private GameObject planetRoot;

    [Tooltip("Parent de tous les objets de la grille hex locale")]
    [SerializeField] private GameObject hexGridRoot;

    [Header("Références")]
    [SerializeField] private CameraController cameraController;
    [SerializeField] private SolarSystemView  solarSystemView;
    [SerializeField] private PlanetSphere     planetSphere;
    [SerializeField] private HexGrid          hexGrid;
    [SerializeField] private TerraformHUD     terraformHUD;
    [SerializeField] private TerraformSystem  terraformSystem;
    [SerializeField] private TerraformProgressTracker progressTracker;

    [Header("Vue système solaire")]
    [SerializeField] private float solarOrbitMinDistance = 20f;
    [SerializeField] private float solarOrbitMaxDistance = 120f;
    [SerializeField] private float solarOrbitStartDistance = 55f;

    [Header("Zooms par vue (OrthoTopDown)")]
    [SerializeField] private float localMinZoom  = 5f;
    [SerializeField] private float localMaxZoom  = 100000f;
    [SerializeField] private float localStartZoom = 360f;

    [Header("Vue planète projetée")]
    [SerializeField] private float planetMinZoom = 18f;
    [SerializeField] private float planetMaxZoom = 80f;
    [SerializeField] private float planetStartZoom = 30f;

    [Header("Navigation")]
    [SerializeField] private bool directPlanetClickToLocal = false;

    // =========================================================
    // Runtime
    // =========================================================

    private ViewState _state = ViewState.SolarSystem;
    private ViewState _previousStateBeforeLocal = ViewState.SolarSystem;
    private DebugCoherenceOverride _activeProjectionOverride = DebugCoherenceOverride.None;
    private float _activeProjectionWaterLevel;

    // Corps actuellement affiché en vue planétaire
    private CelestialBodyData _activePlanet;

    public ViewState CurrentState => _state;
    public CelestialBodyData ActivePlanet => _activePlanet;
    public HexGrid ActiveHexGrid => hexGrid;
    public TerraformHUD ActiveTerraformHUD => terraformHUD;
    public TerraformSystem ActiveTerraformSystem => terraformSystem;
    public PlanetSphere ActivePlanetSphere => planetSphere;
    public float ActiveProjectionWaterLevel => _activeProjectionWaterLevel;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        // Souscriptions aux events des vues
        if (solarSystemView != null) solarSystemView.OnPlanetClicked += OpenPlanet;
        if (planetSphere    != null) planetSphere.OnRegionClicked    += OpenRegion;
    }

    private void Start()
    {
        EnterSolarSystem();
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        GoBackOneLevel();
    }

    private void OnDestroy()
    {
        if (solarSystemView != null) solarSystemView.OnPlanetClicked -= OpenPlanet;
        if (planetSphere    != null) planetSphere.OnRegionClicked    -= OpenRegion;
    }

    // =========================================================
    // Transitions publiques
    // =========================================================

    /// <summary>Entre dans la vue système solaire.</summary>
    public void EnterSolarSystem()
    {
        SetActiveRoot(solarSystemRoot);
        Vector3 solarPivot = solarSystemRoot != null ? solarSystemRoot.transform.position : Vector3.zero;
        cameraController.SetMode(CameraController.CameraMode.OrbitPerspective,
                                 solarOrbitMinDistance, solarOrbitMaxDistance,
                                 solarPivot);
        cameraController.SetOrbitPivot(solarPivot, solarOrbitStartDistance);
        _state = ViewState.SolarSystem;
        OnViewChanged?.Invoke(_state);
        Debug.Log("[ViewManager] → Vue Système Solaire");
    }

    /// <summary>
    /// Ouvre la vue demandée à partir d'un clic sur une planète du système solaire.
    /// Appelé par SolarSystemView.OnPlanetClicked.
    /// </summary>
    public void OpenPlanet(CelestialBodyData body, Vector3 planetWorldPos)
    {
        _activePlanet = body;

        if (directPlanetClickToLocal)
        {
            OpenRegion(0.5f, 0.5f);
            return;
        }

        ShowProjectedPlanet(body, DebugCoherenceOverride.None, _activeProjectionWaterLevel);
    }

    private void ShowProjectedPlanet(CelestialBodyData body, DebugCoherenceOverride coherenceOverride, float waterLevelOffset)
    {
        _activePlanet = body;
        _activeProjectionOverride = coherenceOverride;
        _activeProjectionWaterLevel = Mathf.Clamp(waterLevelOffset, -0.45f, 0.45f);

        SetActiveRoot(planetRoot);

        // Génère la texture et configure la sphère
        if (planetSphere != null)
            planetSphere.LoadPlanet(body, coherenceOverride, _activeProjectionWaterLevel);

        // Vue planète = projection plane en top-down, distincte de la vue espace.
        cameraController.SetMode(CameraController.CameraMode.OrthoTopDown,
                     planetMinZoom, planetMaxZoom);
        cameraController.FocusOn(Vector3.zero, planetStartZoom);

        _state = ViewState.Planet;
        OnViewChanged?.Invoke(_state);
        Debug.Log($"[ViewManager] → Vue Planétaire : {body.bodyName} | override={coherenceOverride} | eau={_activeProjectionWaterLevel:+0.00;-0.00;0.00}");
    }

    /// <summary>
    /// Ouvre la vue hex locale pour une région donnée (lat/lon).
    /// Appelé par PlanetSphere.OnRegionClicked.
    /// </summary>
    public void OpenRegion(float latitude, float longitude)
    {
        if (_activePlanet == null) return;

        _previousStateBeforeLocal = _state;

        MapRegion region = BuildRegion(latitude, longitude, _activeProjectionOverride);

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
        Debug.Log($"[ViewManager] → Vue Locale | lat={latitude:F2} lon={longitude:F2} | override={_activeProjectionOverride}");
    }

    public bool TryOpenRegionNormalized(float latitude, float longitude)
    {
        if (_activePlanet == null)
            return false;

        OpenRegion(Mathf.Clamp01(latitude), Mathf.Clamp01(longitude));
        return true;
    }

    public bool ReloadCurrentProjection()
    {
        if (_activePlanet == null || planetSphere == null)
            return false;

        if (_state == ViewState.Planet)
            ShowProjectedPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);
        else
            planetSphere.LoadPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);

        return true;
    }

    public bool ClearAndReloadCurrentProjection()
    {
        if (_activePlanet == null || planetSphere == null)
            return false;

        planetSphere.ClearProjectionCache();
        return ReloadCurrentProjection();
    }

    public bool SetProjectionWaterLevel(float waterLevel)
    {
        if (_activePlanet == null || planetSphere == null)
            return false;

        _activeProjectionWaterLevel = Mathf.Clamp(waterLevel, -0.45f, 0.45f);
        return ReloadCurrentProjection();
    }

    public bool ResetProjectionWaterLevel()
    {
        return SetProjectionWaterLevel(0f);
    }

    public bool RegenerateCurrentLocalRegion()
    {
        if (hexGrid == null || hexGrid.CurrentRegion == null)
            return false;

        hexGrid.Regenerate();
        ApplyLocalRuntimeContext(hexGrid.CurrentRegion);
        terraformHUD?.RefreshSelectedHexInfo();
        return true;
    }

    public bool OpenPlanetDebug(CelestialBodyData body)
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

        return true;
    }

    private MapRegion BuildRegion(float latitude, float longitude, DebugCoherenceOverride coherenceOverride)
    {
        HexCell projectedCell = planetSphere != null ? planetSphere.GetProjectedCell(latitude, longitude) : null;
        MapRegion region = ScriptableObject.CreateInstance<MapRegion>();
        region.solarSystem = solarSystemView != null ? solarSystemView.CurrentSystem : null;
        region.planet = _activePlanet;
        region.genParams = _activePlanet.genParams;
        region.latitude = latitude;
        region.longitude = longitude;
        region.projectedTerrain = projectedCell?.terrain;
        region.projectedWaterRatio = projectedCell != null ? projectedCell.state.waterRatio : 0f;

        bool projectedOpenWater = projectedCell != null &&
                                  (projectedCell.state.waterClassification == WaterClassification.OpenOcean ||
                                   (projectedCell.terrain != null &&
                                    projectedCell.terrain.terrainType == TerrainType.Eau &&
                                    projectedCell.state.waterRatio >= 0.95f));

        bool projectedFrozenWater = projectedCell != null &&
                                    (projectedCell.state.waterClassification == WaterClassification.FrozenWater ||
                                     (projectedCell.terrain != null &&
                                      projectedCell.terrain.terrainType == TerrainType.Glace &&
                                      projectedCell.state.waterRatio >= 0.5f));

        bool projectedArid = projectedCell != null &&
                             projectedCell.state.waterClassification == WaterClassification.Dry &&
                             projectedCell.state.waterRatio <= 0.06f;

        region.forceOpenWaterRegion = projectedOpenWater ||
                                      (projectedCell == null && coherenceOverride == DebugCoherenceOverride.Ocean);
        region.forceAridRegion = projectedArid ||
                                 (projectedCell == null && coherenceOverride == DebugCoherenceOverride.Arid);
        region.forceFrozenRegion = projectedFrozenWater ||
                                   (projectedCell == null && coherenceOverride == DebugCoherenceOverride.Frozen);
        return region;
    }

    private void GoBackOneLevel()
    {
        switch (_state)
        {
            case ViewState.SolarSystem:
                // Déjà au niveau le plus haut
                break;
            case ViewState.Planet:
                // Retour depuis planète → système solaire
                EnterSolarSystem();
                break;
            case ViewState.Local:
                if (_previousStateBeforeLocal == ViewState.Planet && _activePlanet != null)
                    ShowProjectedPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);
                else
                    EnterSolarSystem();
                break;
        }
    }

    // =========================================================
    // Helpers
    // =========================================================

    /// <summary>
    /// Appelé par HexInput quand l'utilisateur clique sur une cellule en vue locale.
    /// Affiche le panel d'info dans le HUD.
    /// </summary>
    public void NotifyCellClicked(HexCell cell)
    {
        if (_state != ViewState.Local) return;
        Debug.Log($"[ViewManager] Cellule cliquée en vue locale : ({cell.Q}, {cell.R})");
        terraformHUD?.ShowHexPanel(cell);
    }

    private void SetActiveRoot(GameObject active)
    {
        if (solarSystemRoot != null) solarSystemRoot.SetActive(solarSystemRoot == active);
        if (planetRoot      != null) planetRoot.SetActive(planetRoot == active);
        if (hexGridRoot     != null) hexGridRoot.SetActive(hexGridRoot == active);
    }

    private void ApplyLocalRuntimeContext(MapRegion region)
    {
        if (terraformSystem != null)
        {
            GenerationContext ctx = GenerationContext.Build(hexGrid.GetCells(), region);
            terraformSystem.SetContext(ctx);
            terraformHUD?.SetRegionContext(ctx);
        }

        progressTracker?.Refresh();
    }
}
