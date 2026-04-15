using UnityEngine;
using System;

/// <summary>
/// Machine d'état gérant les 3 niveaux de vue du jeu.
///
/// États :
///   SolarSystem → active SolarSystemRoot, caméra OrthoTopDown
///   Planet      → active PlanetRoot, caméra OrbitPerspective
///   Local       → active HexGridRoot, caméra OrthoTopDown
///
/// Transitions déclenchées par :
///   - CameraController.OnZoomedToMin : descend d'un niveau (SolarSystem → Planet → Local)
///   - CameraController.OnZoomedToMax : remonte d'un niveau (Local → Planet → SolarSystem)
///   - SolarSystemView.OnPlanetClicked : SolarSystem → Planet (avec focus)
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

    [Header("Zooms par vue (OrthoTopDown)")]
    [SerializeField] private float solarMinZoom  = 20f;
    [SerializeField] private float solarMaxZoom  = 500f;
    [SerializeField] private float localMinZoom  = 5f;
    [SerializeField] private float localMaxZoom  = 80f;

    [Header("Orbite planétaire")]
    [SerializeField] private float planetOrbitMinDist = 5f;
    [SerializeField] private float planetOrbitMaxDist = 120f;

    // =========================================================
    // Runtime
    // =========================================================

    private ViewState _state = ViewState.SolarSystem;

    // Corps actuellement affiché en vue planétaire
    private CelestialBodyData _activePlanet;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        // Souscriptions aux events caméra
        cameraController.OnZoomedToMin += HandleZoomedToMin;
        cameraController.OnZoomedToMax += HandleZoomedToMax;

        // Souscriptions aux events des vues
        if (solarSystemView != null) solarSystemView.OnPlanetClicked += OpenPlanet;
        if (planetSphere    != null) planetSphere.OnRegionClicked    += OpenRegion;
    }

    private void Start()
    {
        EnterSolarSystem();
    }

    private void OnDestroy()
    {
        cameraController.OnZoomedToMin -= HandleZoomedToMin;
        cameraController.OnZoomedToMax -= HandleZoomedToMax;

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
        cameraController.SetMode(CameraController.CameraMode.OrthoTopDown,
                                 solarMinZoom, solarMaxZoom);
        _state = ViewState.SolarSystem;
        OnViewChanged?.Invoke(_state);
        Debug.Log("[ViewManager] → Vue Système Solaire");
    }

    /// <summary>
    /// Ouvre la vue planétaire sur un corps céleste donné.
    /// Appelé par SolarSystemView.OnPlanetClicked.
    /// </summary>
    public void OpenPlanet(CelestialBodyData body, Vector3 planetWorldPos)
    {
        _activePlanet = body;

        SetActiveRoot(planetRoot);

        // Génère la texture et configure la sphère
        if (planetSphere != null)
            planetSphere.LoadPlanet(body);

        // La caméra orbite autour de la sphère (centrée en planetWorldPos)
        cameraController.SetMode(CameraController.CameraMode.OrbitPerspective,
                                 planetOrbitMinDist, planetOrbitMaxDist,
                                 orbitPivot: planetWorldPos);

        _state = ViewState.Planet;
        OnViewChanged?.Invoke(_state);
        Debug.Log($"[ViewManager] → Vue Planétaire : {body.bodyName}");
    }

    /// <summary>
    /// Ouvre la vue hex locale pour une région donnée (lat/lon).
    /// Appelé par PlanetSphere.OnRegionClicked.
    /// </summary>
    public void OpenRegion(float latitude, float longitude)
    {
        if (_activePlanet == null) return;

        // Crée un MapRegion dynamique pour la zone cliquée
        MapRegion region = ScriptableObject.CreateInstance<MapRegion>();
        region.planet    = _activePlanet;
        region.genParams = _activePlanet.genParams;
        region.latitude  = latitude;
        region.longitude = longitude;

        SetActiveRoot(hexGridRoot);
        hexGrid.LoadRegion(region);

        // Fournit le contexte biome à TerraformSystem pour la réévaluation locale
        if (terraformSystem != null)
        {
            GenerationContext ctx = GenerationContext.Build(hexGrid.GetCells(), region);
            terraformSystem.SetContext(ctx);
        }

        // Actualise la progression initiale de la région
        progressTracker?.Refresh();

        cameraController.SetMode(CameraController.CameraMode.OrthoTopDown,
                                 localMinZoom, localMaxZoom);
        cameraController.FocusOn(Vector3.zero, (localMinZoom + localMaxZoom) * 0.4f);

        _state = ViewState.Local;
        OnViewChanged?.Invoke(_state);
        Debug.Log($"[ViewManager] → Vue Locale | lat={latitude:F2} lon={longitude:F2}");
    }

    // =========================================================
    // Handlers events caméra
    // =========================================================

    private void HandleZoomedToMin()
    {
        switch (_state)
        {
            case ViewState.SolarSystem:
                // Zoomer IN depuis le solaire = on a déjà cliqué un planète → rien ici
                break;
            case ViewState.Planet:
                // Scroll in max en vue planétaire → passer en Local au centre de la sphère
                OpenRegion(0.5f, 0.5f);
                break;
            case ViewState.Local:
                // Déjà au niveau le plus bas
                break;
        }
    }

    private void HandleZoomedToMax()
    {
        switch (_state)
        {
            case ViewState.SolarSystem:
                // Déjà au niveau le plus haut
                break;
            case ViewState.Planet:
                // Scroll out max depuis planète → retour système solaire
                EnterSolarSystem();
                break;
            case ViewState.Local:
                // Scroll out max depuis local → retour planète
                if (_activePlanet != null)
                    OpenPlanet(_activePlanet, Vector3.zero);
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
}
