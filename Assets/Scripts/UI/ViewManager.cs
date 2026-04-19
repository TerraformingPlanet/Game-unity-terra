using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using System;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

/// <summary>
/// Machine d'état gérant les niveaux de vue du jeu.
///
/// États :
///   SolarSystem → active SolarSystemRoot, caméra OrbitPerspective
///   Planet      → active PlanetRoot, sous-vue Globe (Goldberg 3D) ou Flat (Mercator)
///   Local       → [obsolète] conservé pour compatibilité ascendante
///
/// Transitions déclenchées par :
///   - SolarSystemView.OnPlanetClicked : SolarSystem → Planet
///   - TogglePlanetView()              : Globe ↔ Flat au sein de Planet
/// </summary>
public class ViewManager : MonoBehaviour, IClientSnapshotSource
{
    public enum ViewState { Galaxy, SolarSystem, Planet, Local }

    /// <summary>Sous-vue active dans l'état Planet.</summary>
    public enum PlanetSubView { Globe, Flat }

    // =========================================================
    // Events (pour HUD, UI, analytics)
    // =========================================================

    public static event Action<ViewState> OnViewChanged;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Scene Roots")]
    [Tooltip("Parent de tous les objets de la vue galaxie")]
    [SerializeField] private GameObject galaxyRoot;

    [Tooltip("Parent de tous les objets de la vue système solaire")]
    [SerializeField] private GameObject solarSystemRoot;

    [Tooltip("Parent de tous les objets de la vue planétaire (sphère)")]
    [SerializeField] private GameObject planetRoot;

    [Tooltip("Parent de tous les objets de la grille hex locale")]
    [SerializeField] private GameObject hexGridRoot;

    [Header("Références")]
    [SerializeField] private GalaxyView           galaxyView;
    [SerializeField] private CameraController     cameraController;
    [SerializeField] private SolarSystemView      solarSystemView;
    [SerializeField] private PlanetSphereGoldberg planetSphere;
    [SerializeField] private PlanetFlatView       planetFlatView;
    [SerializeField] private PlanetTangentView    planetTangentView;
    [SerializeField] private MinimapController    minimapController;
    [SerializeField] private HexGrid              hexGrid;
    [SerializeField] private TerraformHUD     terraformHUD;
    [SerializeField] private TerraformSystem  terraformSystem;
    [SerializeField] private TerraformProgressTracker progressTracker;

    [Header("Vue locale en overlay sur le Globe")]
    [Tooltip("Distance caméra pour orbiter vers la face cliquée.")]
    [SerializeField] private float localOverlayOrbitDistance  = 14f;
    [Tooltip("Durée de l'animation caméra vers la face (secondes).")]
    [SerializeField] private float localOverlayOrbitDuration  = 0.5f;
    [Tooltip("Décalage radial anti z-fighting entre globe et grille hex. Réduire si on voit un jeu, augmenter si z-fighting.")]
    [SerializeField] private float localOverlaySurfaceOffset  = 0.01f;

    // =========================================================
    // Runtime state
    // =========================================================

    private bool _isContextAuthoritative;

    [Header("Vue système solaire")]
    [SerializeField] private float solarOrbitMinDistance = 20f;
    [SerializeField] private float solarOrbitMaxDistance = 120f;
    [SerializeField] private float solarOrbitStartDistance = 55f;

    [Header("Zooms par vue (OrthoTopDown)")]
    [SerializeField] private float localMinZoom  = 5f;
    [SerializeField] private float localMaxZoom  = 100000f;
    [SerializeField] private float localStartZoom = 360f;

    [Header("Vue Galaxie (OrthoTopDown)")]
    [Tooltip("Taille orthographique initiale de la cam\u00e9ra en vue Galaxie.")]
    [SerializeField] private float galaxyStartZoom = 60f;

    [Header("Vue planète sphère GP (OrbitPerspective)")]
    [SerializeField] private float planetOrbitMinDistance   = 12f;
    [SerializeField] private float planetOrbitMaxDistance   = 40f;
    [SerializeField] private float planetOrbitStartDistance = 22f;

    [Header("Vue planète H3 / plan tangent")]
    [Tooltip("Zoom orthographique minimum pendant la transition vers le plan tangent / H3.")]
    [SerializeField] private float planetH3MinZoom = 5f;

    [Tooltip("Zoom orthographique maximum pendant la transition vers le plan tangent / H3.")]
    [SerializeField] private float planetH3MaxZoom = 80f;

    [Tooltip("Zoom de départ quand on bascule de la sphère vers le plan tangent / H3. Augmenter = caméra plus haute.")]
    [SerializeField] private float planetH3StartZoom = 20f;

    [Header("Navigation")]
    [SerializeField] private bool directPlanetClickToLocal = false;
    [Tooltip("Vue affichée au démarrage. Galaxy = normal ; SolarSystem = debug preset Sol.")]
    [SerializeField] private ViewState startingView = ViewState.Galaxy;

    [Header("Sync serveur de simulation")]
    [SerializeField] private bool preferServerRegionSync = true;
    [SerializeField] private string simulationServerUrl = "http://127.0.0.1:8080";
    [SerializeField] private float simulationServerTimeoutSeconds = 2f;

    // =========================================================
    // Runtime
    // =========================================================

    private ViewState _state = ViewState.SolarSystem;
    private ViewState _previousStateBeforeLocal = ViewState.SolarSystem;
    private PlanetSubView _previousSubViewBeforeLocal = PlanetSubView.Globe;
    private PlanetSubView _planetSubView = PlanetSubView.Globe;

    // Dernière tuile sélectionnée en vue Globe (pour bouton "Voir en local")
    private float _selectedGlobeLat = 0.5f;
    private float _selectedGlobeLon = 0.5f;
    private bool  _hasGlobeSelection = false;
    private DebugCoherenceOverride _activeProjectionOverride = DebugCoherenceOverride.None;
    private float _activeProjectionWaterLevel;

    // Corps actuellement affiché en vue planétaire
    private OrbitalBody _activePlanet;

    public ViewState CurrentState => _state;
    public PlanetSubView CurrentPlanetSubView => _planetSubView;
    public OrbitalBody ActivePlanet => _activePlanet;
    public HexGrid ActiveHexGrid => hexGrid;
    public TerraformHUD ActiveTerraformHUD => terraformHUD;
    public TerraformSystem     ActiveTerraformSystem => terraformSystem;
    public PlanetSphereGoldberg ActivePlanetSphere     => planetSphere;
    public PlanetFlatView       ActivePlanetFlatView   => planetFlatView;
    public float ActiveProjectionWaterLevel => _activeProjectionWaterLevel;
    public DebugCoherenceOverride ActiveProjectionOverride => _activeProjectionOverride;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        // Souscriptions aux events des vues
        if (galaxyView        != null) galaxyView.OnSystemClicked         += OpenSystem;
        if (solarSystemView   != null) solarSystemView.OnPlanetClicked    += OpenPlanet;
        if (solarSystemView   != null) solarSystemView.OnPrimaryStarClicked += FocusSolarCameraOnPrimaryStar;
        if (planetSphere      != null) planetSphere.OnRegionClicked       += OnGlobeRegionClicked;
        if (planetSphere      != null) planetSphere.OnH3TileResolved      += OnGlobeH3TileResolved;
        if (planetSphere      != null) planetSphere.OnH3TilesReady        += OnGlobeH3TilesReady;
        if (planetFlatView    != null) planetFlatView.OnRegionClicked     += OnFlatRegionClicked;
        if (planetTangentView != null) planetTangentView.OnRegionClicked  += OnFlatRegionClicked;
    }

    private void Start()
    {
        switch (startingView)
        {
            case ViewState.SolarSystem: EnterSolarSystem(); break;
            case ViewState.Planet:      EnterSolarSystem(); break;  // fallback raisonnable
            default:                   EnterGalaxy();      break;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        GoBackOneLevel();
    }

    private void OnDestroy()
    {
        if (galaxyView        != null) galaxyView.OnSystemClicked         -= OpenSystem;
        if (solarSystemView   != null) solarSystemView.OnPlanetClicked    -= OpenPlanet;
        if (solarSystemView   != null) solarSystemView.OnPrimaryStarClicked -= FocusSolarCameraOnPrimaryStar;
        if (planetSphere      != null) planetSphere.OnRegionClicked       -= OnGlobeRegionClicked;
        if (planetSphere      != null) planetSphere.OnH3TileResolved      -= OnGlobeH3TileResolved;
        if (planetSphere      != null) planetSphere.OnH3TilesReady        -= OnGlobeH3TilesReady;
        if (planetFlatView    != null) planetFlatView.OnRegionClicked     -= OnFlatRegionClicked;
        if (planetTangentView != null) planetTangentView.OnRegionClicked  -= OnFlatRegionClicked;
    }

    // =========================================================
    // Transitions publiques
    // =========================================================

    /// <summary>Entre dans la vue galaxie (niveau de navigation le plus haut).</summary>
    public void EnterGalaxy()
    {
        ResetPlanetVisuals();
        cameraController?.SetOrbitKeyboardPanEnabled(false);
        SetActiveRoot(galaxyRoot);
        Vector3 galaxyPivot = galaxyRoot != null ? galaxyRoot.transform.position : Vector3.zero;
        cameraController.SetMode(CameraController.CameraMode.OrthoTopDown,
                                 localMinZoom, localMaxZoom);
        cameraController.FocusOn(galaxyPivot, galaxyStartZoom);
        _state = ViewState.Galaxy;
        OnViewChanged?.Invoke(_state);
        Debug.Log("[ViewManager] \u2192 Vue Galaxie");
    }

    /// <summary>Ouvre le syst\u00e8me solaire demand\u00e9 depuis la vue Galaxie.</summary>
    public void OpenSystem(string systemName)
    {
        Debug.Log($"[ViewManager] Ouverture syst\u00e8me : {systemName}");
        EnterSolarSystem();
    }

    /// <summary>Entre dans la vue système solaire.</summary>
    public void EnterSolarSystem()
    {
        ResetPlanetVisuals();
        SetActiveRoot(solarSystemRoot);
        Vector3 solarPivot = solarSystemRoot != null ? solarSystemRoot.transform.position : Vector3.zero;
        cameraController.SetMode(CameraController.CameraMode.OrbitPerspective,
                                 solarOrbitMinDistance, solarOrbitMaxDistance,
                                 solarPivot);
        cameraController.SetOrbitPivot(solarPivot, solarOrbitStartDistance);
        cameraController.SetOrbitKeyboardPanEnabled(true);
        _state = ViewState.SolarSystem;
        OnViewChanged?.Invoke(_state);
        Debug.Log("[ViewManager] → Vue Système Solaire");

        // Recharge dynamiquement le système depuis le serveur
        if (solarSystemView != null)
            StartCoroutine(solarSystemView.LoadFromServer(simulationServerUrl, simulationServerTimeoutSeconds));
    }

    /// <summary>
    /// Ouvre la vue demandée à partir d'un clic sur une planète du système solaire.
    /// Appelé par SolarSystemView.OnPlanetClicked.
    /// </summary>
    public void OpenPlanet(OrbitalBody body, Vector3 planetWorldPos)
    {
        _activePlanet = body;

        if (directPlanetClickToLocal)
        {
            OpenRegion(0.5f, 0.5f);
            return;
        }

        ShowProjectedPlanet(body, DebugCoherenceOverride.None, _activeProjectionWaterLevel);
    }

    private void ShowProjectedPlanet(OrbitalBody body, DebugCoherenceOverride coherenceOverride, float waterLevelOffset)
    {
        _activePlanet = body;
        _activeProjectionOverride = coherenceOverride;
        _activeProjectionWaterLevel = Mathf.Clamp(waterLevelOffset, -0.45f, 0.45f);

        ResetPlanetVisuals();
        SetActiveRoot(planetRoot);

        // Charge la sphère GP (les tuiles H3 arriveront via OnH3TilesReady)
        if (planetSphere != null)
            planetSphere.LoadPlanet(body, coherenceOverride, _activeProjectionWaterLevel);

        // Pré-charge la vue tangente avec les données GP (mesh sans grille Mercator)
        if (planetTangentView != null && planetSphere != null)
            planetTangentView.LoadPlanet(planetSphere.SphereData);

        // Démarre en sous-vue Globe
        _planetSubView = PlanetSubView.Globe;
        ApplyPlanetSubView();
        cameraController?.SetOrbitKeyboardPanEnabled(false);

        _state = ViewState.Planet;
        OnViewChanged?.Invoke(_state);
        Debug.Log($"[ViewManager] → Vue Planétaire : {body.bodyName} | override={coherenceOverride} | eau={_activeProjectionWaterLevel:+0.00;-0.00;0.00}");
    }

    /// <summary>
    /// Bascule entre sous-vue Globe (Goldberg 3D) et Flat (Mercator) dans la Vue Planétaire.
    /// </summary>
    public void TogglePlanetView()
    {
        if (_state != ViewState.Planet) return;
        _planetSubView = _planetSubView == PlanetSubView.Globe ? PlanetSubView.Flat : PlanetSubView.Globe;
        ApplyPlanetSubView();
        Debug.Log($"[ViewManager] Toggle vue planète → {_planetSubView}");
    }

    private void ApplyPlanetSubView()
    {
        bool isGlobe = _planetSubView == PlanetSubView.Globe;

        if (isGlobe)
        {
            if (planetSphere      != null) planetSphere.gameObject.SetActive(true);
            if (planetTangentView != null) planetTangentView.gameObject.SetActive(false);
            if (planetFlatView    != null) planetFlatView.gameObject.SetActive(false);
            if (minimapController != null) minimapController.gameObject.SetActive(false);

            Vector3 pivot = planetSphere != null ? planetSphere.transform.position : Vector3.zero;
            cameraController.SetMode(CameraController.CameraMode.OrbitPerspective,
                                     planetOrbitMinDistance, planetOrbitMaxDistance, pivot);
            cameraController.SetOrbitPivot(pivot, planetOrbitStartDistance);
        }
        else
        {
            if (planetTangentView != null) planetTangentView.gameObject.SetActive(true);
            if (planetFlatView    != null) planetFlatView.gameObject.SetActive(true);  // minimap
            if (minimapController != null) minimapController.gameObject.SetActive(true);
            // La sphère reste active pendant la transition, sera désactivée dans le callback

            Vector3 focus = planetSphere != null
                ? planetSphere.LastClickedFaceCentroid
                : Vector3.up * GoldbergSphereGenerator.VisualRadius;

            planetTangentView?.SetFocusAndEnter(focus, onComplete: () =>
            {
                if (planetSphere != null) planetSphere.gameObject.SetActive(false);
            });

            cameraController.SetMode(CameraController.CameraMode.OrthoTopDown, planetH3MinZoom, planetH3MaxZoom);
            cameraController.FocusOn(Vector3.zero, planetH3StartZoom);
        }
    }

    /// <summary>
    /// Notifie le HUD qu'une tuile planétaire a été sélectionnée (Globe ou Flat).
    /// Ne change plus de vue — sélectionne simplement la tuile.
    /// </summary>
    public void OpenRegion(float latitude, float longitude)
    {
        if (_activePlanet == null) return;
        // En mode H3, le HUD est mis à jour par OnH3TileResolved (~1–2s après le clic).
        Debug.Log($"[ViewManager] Clic globe lat={latitude:F2} lon={longitude:F2} — H3 en résolution...");
    }

    private void OnGlobeRegionClicked(float lat, float lon)
    {
        _selectedGlobeLat  = lat;
        _selectedGlobeLon  = lon;
        _hasGlobeSelection = true;
        OpenRegion(lat, lon);
    }

    // Mise à jour HUD avec données H3 authoritatives (1–2s après le clic)
    private void OnGlobeH3TileResolved(GoldbergTileState tile)
    {
        if (_state == ViewState.Planet && _planetSubView == PlanetSubView.Globe)
            terraformHUD?.ShowH3TileInfo(tile);
    }

    // Distribue les tuiles H3 aux vues Plate et Tangente
    private void OnGlobeH3TilesReady(GoldbergTileState[] tiles, Dictionary<TerrainType, Color> colorByType)
    {
        int tileCount = tiles?.Length ?? 0;
        bool flatAssigned = planetFlatView != null;
        bool tangentAssigned = planetTangentView != null;
        bool flatActiveInHierarchy = flatAssigned && planetFlatView.gameObject.activeInHierarchy;
        bool tangentActiveInHierarchy = tangentAssigned && planetTangentView.gameObject.activeInHierarchy;
        bool flatLoadedBeforeDispatch = flatAssigned && planetFlatView.IsLoaded;
        bool tangentLoadedBeforeDispatch = tangentAssigned && planetTangentView.IsLoaded;

        Debug.Log(
            $"[ViewManager] OnGlobeH3TilesReady | state={_state} | subView={_planetSubView} | tiles={tileCount} | " +
            $"flat(assigned={flatAssigned}, active={flatActiveInHierarchy}, loaded={flatLoadedBeforeDispatch}) | " +
            $"tangent(assigned={tangentAssigned}, active={tangentActiveInHierarchy}, loaded={tangentLoadedBeforeDispatch})");

        planetFlatView?.LoadPlanetFromH3(tiles, colorByType);
        planetTangentView?.RefreshColorsFromH3(tiles, colorByType);

        bool flatLoadedAfterDispatch = flatAssigned && planetFlatView.IsLoaded;
        bool tangentLoadedAfterDispatch = tangentAssigned && planetTangentView.IsLoaded;

        Debug.Log(
            $"[ViewManager] OnGlobeH3TilesReady complete | flatLoaded={flatLoadedAfterDispatch} | " +
            $"tangentLoaded={tangentLoadedAfterDispatch}");
    }
    private void OnFlatRegionClicked(float lat, float lon)  => ShowLocalView(lat, lon);

    private void FocusSolarCameraOnPrimaryStar(Vector3 worldPos)
    {
        if (_state != ViewState.SolarSystem || cameraController == null)
            return;

        cameraController.SetOrbitPivot(worldPos, cameraController.OrbitDistance);
        Debug.Log("[ViewManager] Recentrage caméra sur l'étoile primaire");
    }

    /// <summary>
    /// Ouvre la Vue Locale sur la région à la lat/lon donnée.
    /// Appelé depuis un clic en sous-vue Flat ou Plan Tangent.
    /// </summary>
    public void ShowLocalView(float latitude, float longitude)
    {
        if (_activePlanet == null) return;

        cameraController?.SetOrbitKeyboardPanEnabled(false);

        _previousStateBeforeLocal   = _state;
        _previousSubViewBeforeLocal = _planetSubView;

        MapRegion region = BuildRegion(
            Mathf.Clamp01(latitude),
            Mathf.Clamp01(longitude),
            _activeProjectionOverride);

        SetActiveRoot(hexGridRoot);
        hexGrid.LoadRegion(region);
        ApplyLocalRuntimeContext(region);

        Bounds gridBounds = hexGrid.GetWorldBounds();
        float fittedZoom  = Mathf.Max(gridBounds.size.x, gridBounds.size.z) * 1.35f;
        float appliedZoom = Mathf.Max(localStartZoom, fittedZoom);

        cameraController.SetMode(CameraController.CameraMode.OrthoTopDown, localMinZoom, localMaxZoom);
        cameraController.FocusOn(gridBounds.center, appliedZoom);

        _state = ViewState.Local;
        OnViewChanged?.Invoke(_state);
        Debug.Log($"[ViewManager] → Vue Locale | lat={latitude:F3} lon={longitude:F3} | {_activePlanet.bodyName}");
        RequestAuthoritativeRegionSync(latitude, longitude);
    }

    /// <summary>
    /// Ouvre la Vue Locale pour la dernière tuile sélectionnée en vue Globe.
    /// Appelé par le bouton "Voir en local" du HUD.
    /// Mode overlay : le globe reste visible, la grille hex se superpose à la face cliquée.
    /// </summary>
    public void EnterLocalFromSelection()
    {
        if (!_hasGlobeSelection || _activePlanet == null || planetSphere == null) return;
        if (_state != ViewState.Planet) return;

        _previousSubViewBeforeLocal = _planetSubView;

        // Activer hexGridRoot EN PARALLÈLE de planetRoot (pas SetActiveRoot qui cache le globe)
        if (hexGridRoot != null)
        {
            hexGridRoot.SetActive(true);
            hexGrid?.LoadRegion(BuildRegion(_selectedGlobeLat, _selectedGlobeLon, _activeProjectionOverride));
            PlaceHexGridOnGlobe();
        }

        // Orbiter vers la face
        cameraController?.OrbitToFace(
            planetSphere.LastClickedFaceCentroid.normalized,
            localOverlayOrbitDistance,
            localOverlayOrbitDuration);

        Debug.Log($"[ViewManager] Vue locale overlay → face {planetSphere.LastClickedFaceId}");
    }

    /// <summary>
    /// Place et dimensionne hexGridRoot exactement sur la face GP cliquée.
    /// Appelé depuis EnterLocalFromSelection après activation de hexGridRoot.
    /// </summary>
    private void PlaceHexGridOnGlobe()
    {
        if (hexGridRoot == null || planetSphere == null) return;

        Vector3 dir = planetSphere.LastClickedFaceCentroid.normalized;

        // ── Scale : rayon englobant réel de la grille flat-top en espace local XZ ──
        // L'étendue Z (R * verticalSpacing + innerRadius) est plus grande que X
        // et représente le vrai rayon du cercle englobant la grille.
        float faceRadius = planetSphere.GetFaceRadius(planetSphere.LastClickedFaceId);
        int   gridRadius = hexGrid != null ? hexGrid.Radius : 5;
        float xBound     = gridRadius * HexMetrics.horizontalSpacing + HexMetrics.outerRadius;
        float zBound     = gridRadius * HexMetrics.verticalSpacing   + HexMetrics.innerRadius;
        float hexBoundRadius = Mathf.Max(xBound, zBound);
        float scale = faceRadius / hexBoundRadius;
        hexGridRoot.transform.localScale = Vector3.one * scale;

        // ── Orientation : local Y = outward, local Z = nord, local X = est ──
        // LookRotation(forward=northTangent, up=dir) → Z→nord, Y→outward, X→est.
        // La caméra (LookAt sphere center) a aussi northTangent comme viewport-up → alignement garanti.
        Vector3 worldRef     = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        Vector3 northTangent = Vector3.ProjectOnPlane(worldRef, dir).normalized;
        hexGridRoot.transform.rotation = Quaternion.LookRotation(northTangent, dir);

        // ── Position : collé à la surface (offset minimal anti z-fighting) ──
        hexGridRoot.transform.position = dir * (GoldbergSphereGenerator.VisualRadius + localOverlaySurfaceOffset);

        // ── Coordonnées sphériques par cellule (nécessaire pour le serveur dédié) ──
        if (hexGrid != null)
        {
            HexCell[] cells = hexGrid.GetCells();
            if (cells != null)
            {
                foreach (HexCell cell in cells)
                {
                    Vector3 worldPos  = hexGridRoot.transform.TransformPoint(cell.center);
                    Vector3 sphereDir = worldPos.normalized;
                    float latDeg = Mathf.Asin(Mathf.Clamp(sphereDir.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float lonDeg = Mathf.Atan2(sphereDir.z, sphereDir.x) * Mathf.Rad2Deg;
                    cell.latOnSphere = (latDeg + 90f)  / 180f;
                    cell.lonOnSphere = (lonDeg + 180f) / 360f;
                }
            }
        }

        // ── Masquer la face GP remplacée visuellement ──
        planetSphere.HideFaceOnSphere(planetSphere.LastClickedFaceId);

        Debug.Log($"[ViewManager] PlaceHexGridOnGlobe | faceR={faceRadius:F3} | hexBound={hexBoundRadius:F1} | scale={scale:F5}");
    }

    /// <summary>
    /// Ferme la vue locale (overlay globe ou vue locale standard) et retourne au Globe.
    /// Appelé par le bouton "Fermer" du HUD.
    /// </summary>
    public void CloseLocalOverlay()
    {
        // Restaure la face GP masquée si nécessaire
        if (planetSphere != null && planetSphere.LastClickedFaceId >= 0)
            planetSphere.RestoreFaceOnSphere(planetSphere.LastClickedFaceId);

        // Cas overlay globe : hexGridRoot visible par-dessus planetRoot (state = Planet)
        if (_state == ViewState.Planet && hexGridRoot != null && hexGridRoot.activeSelf)
        {
            hexGridRoot.SetActive(false);
            Debug.Log("[ViewManager] Vue locale overlay fermée → retour Globe");
            return;
        }

        // Cas standard : on était en ViewState.Local
        if (_state == ViewState.Local && _activePlanet != null)
        {
            ShowProjectedPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);
            if (_previousSubViewBeforeLocal == PlanetSubView.Flat)
            {
                _planetSubView = PlanetSubView.Flat;
                ApplyPlanetSubView();
            }
            Debug.Log("[ViewManager] Vue locale fermée → retour Planet");
        }
    }

    public bool TryOpenRegionNormalized(float latitude, float longitude)
    {
        if (_activePlanet == null)
            return false;

        ShowLocalView(Mathf.Clamp01(latitude), Mathf.Clamp01(longitude));
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

    public void GoBackOneLevel()
    {
        // Overlay globe actif (hexGridRoot visible par-dessus planetRoot) → fermer l'overlay d'abord
        if (_state == ViewState.Planet && hexGridRoot != null && hexGridRoot.activeSelf)
        {
            CloseLocalOverlay();
            return;
        }

        switch (_state)
        {
            case ViewState.Galaxy:
                break;  // déjà au niveau le plus haut
            case ViewState.SolarSystem:
                EnterGalaxy();
                break;
            case ViewState.Planet:
                EnterSolarSystem();
                break;
            case ViewState.Local:
                if (_activePlanet != null)
                {
                    ShowProjectedPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);
                    if (_previousSubViewBeforeLocal == PlanetSubView.Flat)
                    {
                        _planetSubView = PlanetSubView.Flat;
                        ApplyPlanetSubView();
                    }
                }
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
        // Désactivation stricte avant réactivation de la cible pour éviter tout état mixte pendant une transition.
        if (galaxyRoot      != null) galaxyRoot.SetActive(false);
        if (solarSystemRoot != null) solarSystemRoot.SetActive(false);
        if (planetRoot      != null) planetRoot.SetActive(false);
        if (hexGridRoot     != null) hexGridRoot.SetActive(false);

        if (active != null)
            active.SetActive(true);
    }

    private void ResetPlanetVisuals()
    {
        if (planetSphere != null)      planetSphere.gameObject.SetActive(true);
        if (planetFlatView != null)    planetFlatView.gameObject.SetActive(false);
        if (planetTangentView != null) planetTangentView.gameObject.SetActive(false);
        if (minimapController != null) minimapController.gameObject.SetActive(false);

        if (hexGridRoot != null && _state != ViewState.Local)
            hexGridRoot.SetActive(false);

        if (planetSphere != null && planetSphere.LastClickedFaceId >= 0)
            planetSphere.RestoreFaceOnSphere(planetSphere.LastClickedFaceId);
    }

    private void ApplyLocalRuntimeContext(MapRegion region)
    {
        progressTracker?.ClearAuthoritativeProgress();
        terraformHUD?.ClearAuthoritativeRegionState();

        if (terraformSystem != null)
        {
            GenerationContext ctx;
            if (_isContextAuthoritative && terraformHUD != null && terraformHUD.HasAuthoritativeRegionState)
            {
                // Injection depuis l'état serveur autoritatif
                ctx = GenerationContext.BuildWithInjected(
                    hexGrid.GetCells(),
                    region,
                    terraformHUD.AuthoritativeRegionState.weather.ToPlanetaryWeatherState(),
                    terraformHUD.AuthoritativeRegionState.coherence.ToCoherenceConstraint()
                );
            }
            else
            {
                // Calcul local classique
                ctx = GenerationContext.Build(hexGrid.GetCells(), region);
            }

            terraformSystem.SetContext(ctx);
            terraformHUD?.SetRegionContext(ctx);
        }

        progressTracker?.Refresh();
    }

    private void RequestAuthoritativeRegionSync(float latitude, float longitude)
    {
        if (!preferServerRegionSync || !isActiveAndEnabled)
            return;

        StartCoroutine(SynchronizeRegionStateFromServer(latitude, longitude));
    }

    private IEnumerator SynchronizeRegionStateFromServer(float latitude, float longitude)
    {
        string url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/commands/open-region?latitude={1}&longitude={2}",
            simulationServerUrl.TrimEnd('/'),
            latitude,
            longitude);

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
            Debug.LogWarning($"[ViewManager] Sync région serveur indisponible ({request.error}). Fallback local conserve.");
            yield break;
        }

        RegionState regionState;
        try
        {
            regionState = JsonUtility.FromJson<RegionState>(request.downloadHandler.text);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ViewManager] Sync région serveur invalide ({ex.Message}). Fallback local conserve.");
            yield break;
        }

        if (!regionState.isValid)
            yield break;

        terraformHUD?.SetAuthoritativeRegionState(regionState);
        terraformSystem?.SynchronizeAuthoritativeRegionState(regionState);

        // Marquer le contexte comme autoritatif pour les prochains appels à ApplyLocalRuntimeContext
        _isContextAuthoritative = true;
    }
}
