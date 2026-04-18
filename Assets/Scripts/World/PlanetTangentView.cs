using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Vue plan tangent local dynamique (Vue 2 — alternative à la sphère).
///
/// Crée un enfant GameObject portant PlanetTangentMesh. Quand SetFocusAndEnter()
/// est appelé, projette les tuiles GP visibles sur un plan plat centré sur la
/// dernière tuile cliquée et anime la transition sphère → plan.
///
/// En LateUpdate, le plan se re-centre automatiquement si la caméra s'éloigne
/// du focus initial de plus de refocusAngleThreshold degrés.
///
/// Interface publique :
///   - LoadPlanet(grid, sphereData)        : stocke les données (pas de build immédiat)
///   - SetFocusAndEnter(focus, onComplete) : build + transition animée
///   - UpdateFocus(direction)              : re-centre manuellement
///   - event OnRegionClicked              : délégué depuis PlanetTangentInput
/// </summary>
[RequireComponent(typeof(PlanetTangentInput))]
public class PlanetTangentView : MonoBehaviour
{
    // =========================================================
    // Event
    // =========================================================

    /// <summary>latNorm [0–1], lonNorm [0–1]. Délégué depuis PlanetTangentInput.</summary>
    public event Action<float, float> OnRegionClicked;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Rendu")]
    [Tooltip("Matériau vertex color (Terraformation/HexVertexColor).")]
    [SerializeField] private Material flatMaterial;

    [Header("Hover")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("Projection")]
    [Tooltip("Seuil de visibilité (produit scalaire). 0.75 ≈ ±41°.")]
    [SerializeField] private float dotThreshold = 0.75f;

    [Tooltip("Facteur d'échelle pour la projection plane (espace local).")]
    [SerializeField] private float projectionScale = 15f;

    [Header("Transition")]
    [Tooltip("Durée de la transition sphère → plan (secondes).")]
    [SerializeField] private float transitionDuration = 0.6f;

    [Header("Re-centrage dynamique")]
    [Tooltip("Distance (unités monde) à parcourir avant de re-centrer le plan tangent.")]
    [SerializeField] private float refocusTriggerDistance = 3f;

    // Non sérialisé — toujours actif (évite les conflits avec l'ancienne valeur sérialisée)
    private bool enableDynamicRecentering = true;

    [Tooltip("Seuil angulaire minimal (degrés) pour accepter un UpdateFocus manuel.")]
    [SerializeField] private float refocusAngleThreshold = 0.5f;

    // =========================================================
    // Runtime
    // =========================================================

    private PlanetTangentMesh  _tangentMesh;
    private PlanetTangentInput _tangentInput;
    private LocalProjection    _localProj;

    private GoldbergSphereGenerator.GoldbergMeshData _sphereData;

    private bool _isReady        = false;
    private bool _isTransitioning = false;

    private GameObject _meshObject;

    // Ancre pour le re-centrage par distance (évite la boucle de feedback)
    private Vector2 _lastRecenteredCamPos;
    private bool    _recenteredPosValid = false;

    // =========================================================
    // Propriétés
    // =========================================================

    /// <summary>Vrai quand les données sphère ont été chargées.</summary>
    public bool IsLoaded => _sphereData.faces != null;

    /// <summary>Référence au composant PlanetTangentMesh (pour PlanetTangentInput).</summary>
    public PlanetTangentMesh TangentMesh => _tangentMesh;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _localProj = new LocalProjection(GoldbergSphereGenerator.VisualRadius);

        // Crée un enfant dédié au mesh
        _meshObject = new GameObject("TangentMeshRenderer");
        _meshObject.transform.SetParent(transform, false);

        _tangentMesh = _meshObject.AddComponent<PlanetTangentMesh>();
        _tangentMesh.dotThreshold    = dotThreshold;
        _tangentMesh.projectionScale = projectionScale;

        // Matériau
        var mr = _meshObject.GetComponent<Renderer>();
        if (flatMaterial != null)
        {
            mr.sharedMaterial = flatMaterial;
        }
        else
        {
            Shader s = Shader.Find("Terraformation/HexVertexColor");
            if (s != null)
                mr.material = new Material(s);
            else
                Debug.LogWarning("[PlanetTangentView] Shader 'Terraformation/HexVertexColor' introuvable.");
        }

        // Input
        _tangentInput = GetComponent<PlanetTangentInput>();
        _tangentInput.OnRegionClicked += (lat, lon) => OnRegionClicked?.Invoke(lat, lon);
    }

    private void OnDestroy()
    {
        PlanetaryHexGrid.OnPlanetDataChanged -= OnPlanetDataChanged;
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Stocke les données GP. Ne construit PAS le mesh immédiatement.
    /// Appeler SetFocusAndEnter() pour déclencher le build et la transition.
    /// </summary>
    public void LoadPlanet(
        PlanetaryHexGrid.GridData grid,
        GoldbergSphereGenerator.GoldbergMeshData sphereData)
    {
        _sphereData = sphereData;
        PlanetaryHexGrid.OnPlanetDataChanged -= OnPlanetDataChanged;
        PlanetaryHexGrid.OnPlanetDataChanged += OnPlanetDataChanged;
        Debug.Log($"[PlanetTangentView] Données chargées : {sphereData.faces?.Length ?? 0} faces GP.");
    }

    /// <summary>
    /// Centre le plan tangent sur <paramref name="focusOnSphere"/>, construit le mesh
    /// et démarre la transition animée sphère → plan.
    /// </summary>
    /// <param name="focusOnSphere">Point 3D sur la sphère (magnitude ≈ VisualRadius).</param>
    /// <param name="onComplete">Callback invoqué quand la transition est terminée.</param>
    public void SetFocusAndEnter(Vector3 focusOnSphere, Action onComplete = null)
    {
        if (!IsLoaded)
        {
            Debug.LogWarning("[PlanetTangentView] SetFocusAndEnter appelé sans données chargées.");
            return;
        }

        if (_isTransitioning)
            StopAllCoroutines();

        _localProj.Update(focusOnSphere);
        _tangentMesh.BuildMesh(_sphereData, _localProj);
        _isReady = true;
        _recenteredPosValid = false; // l'ancre sera initialisée au premier LateUpdate post-transition

        StartCoroutine(TransitionIn(onComplete));
    }

    /// <summary>
    /// Re-centre le plan tangent sur une nouvelle direction (depuis LateUpdate ou manuellement).
    /// Ignoré si une transition est en cours ou si l'angle est inférieur au seuil.
    /// </summary>
    public void UpdateFocus(Vector3 newFocusDirection)
    {
        if (!_isReady || _isTransitioning) return;

        float angle = Vector3.Angle(newFocusDirection.normalized, _localProj.Center);
        if (angle < refocusAngleThreshold) return;

        _localProj.Update(newFocusDirection);
        _tangentMesh.BuildMesh(_sphereData, _localProj);
        _tangentMesh.ApplyTransition(1f);
    }

    /// <summary>
    /// Retourne la GoldbergFace correspondant à hit.triangleIndex (depuis Physics.Raycast).
    /// </summary>
    public GoldbergSphereGenerator.GoldbergFace GetFaceFromTriangle(int triangleIndex)
    {
        int faceId = _tangentMesh.GetFaceIdFromTriangle(triangleIndex);
        if (faceId < 0 || _sphereData.faces == null || faceId >= _sphereData.faces.Length)
            return default;
        return _sphereData.faces[faceId];
    }

    // =========================================================
    // Re-centrage dynamique
    // =========================================================

    private void LateUpdate()
    {
        if (!_isReady || _isTransitioning || Camera.main == null) return;

        Vector3 camPos   = Camera.main.transform.position;
        Vector2 camPos2D = new Vector2(camPos.x, camPos.z);

        // Initialise l'ancre au premier frame valide + place le mesh sous la caméra
        if (!_recenteredPosValid)
        {
            _lastRecenteredCamPos = camPos2D;
            _recenteredPosValid   = true;
            _meshObject.transform.position = new Vector3(camPos.x, 0f, camPos.z);
            return;
        }

        if (!enableDynamicRecentering) return;

        float dist = Vector2.Distance(camPos2D, _lastRecenteredCamPos);
        if (dist < refocusTriggerDistance) return;

        // Ancre AVANT le rebuild
        _lastRecenteredCamPos = camPos2D;

        // Offset caméra en coordonnées LOCALES du plan tangent (par rapport au centre mesh)
        Vector3 meshWorldPos = _meshObject.transform.position;
        float localX = Vector3.Dot(new Vector3(camPos.x - meshWorldPos.x, 0f, camPos.z - meshWorldPos.z), _localProj.Right);
        float localZ = Vector3.Dot(new Vector3(camPos.x - meshWorldPos.x, 0f, camPos.z - meshWorldPos.z), _localProj.Forward);

        Vector3 newDir = _localProj.ReverseProject(localX, localZ, projectionScale);
        UpdateFocus(newDir * GoldbergSphereGenerator.VisualRadius);

        // Déplace le mesh sous la caméra après rebuild (sinon la caméra perd le mesh)
        _meshObject.transform.position = new Vector3(camPos.x, 0f, camPos.z);
    }

    // =========================================================
    // Transitions
    // =========================================================

    private IEnumerator TransitionIn(Action onComplete)
    {
        _isTransitioning = true;
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));
            _tangentMesh.ApplyTransition(t);
            yield return null;
        }

        _tangentMesh.ApplyTransition(1f);
        _isTransitioning = false;
        onComplete?.Invoke();
    }

    // =========================================================
    // Sync données planétaires
    // =========================================================

    private void OnPlanetDataChanged(PlanetaryHexGrid.GridData grid)
    {
        if (!gameObject.activeInHierarchy || !_isReady) return;
        if (_sphereData.faces == null) return;

        GoldbergFaceColorizer.Colorize(_sphereData.faces, grid);
        _tangentMesh.RefreshColors(_sphereData.faces);
    }
}
