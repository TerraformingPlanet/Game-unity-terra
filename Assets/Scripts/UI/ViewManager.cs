using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using System;
using UnityEngine.InputSystem;

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
public partial class ViewManager : MonoBehaviour, IClientSnapshotSource
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
    [SerializeField] private GameConfig config;
    private string SimUrl     => config != null ? config.simulationServerUrl           : "http://127.0.0.1:8080";
    private float  SimTimeout => config != null ? config.simulationServerTimeoutSeconds : 15f;

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
            StartCoroutine(solarSystemView.LoadFromServer(SimUrl, SimTimeout));
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

    // ── Sous-vue planétaire (Globe ↔ Flat) : voir ViewManagerPlanetSubView.cs ─────────────

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

    // ── Callbacks tuiles H3 : voir ViewManagerTileCallbacks.cs ─────────────────────────────

    private void FocusSolarCameraOnPrimaryStar(Vector3 worldPos)
    {
        if (_state != ViewState.SolarSystem || cameraController == null)
            return;

        cameraController.SetOrbitPivot(worldPos, cameraController.OrbitDistance);
        Debug.Log("[ViewManager] Recentrage caméra sur l'étoile primaire");
    }

    public bool TryOpenRegionNormalized(float latitude, float longitude)
    {
        if (_activePlanet == null)
            return false;

        ShowLocalView(Mathf.Clamp01(latitude), Mathf.Clamp01(longitude));
        return true;
    }

    // ── Projection : voir ViewManagerProjection.cs ───────────────────────────

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

    // ── Helpers visuels planète : voir ViewManagerPlanetSubView.cs ──────────────────────────

}

