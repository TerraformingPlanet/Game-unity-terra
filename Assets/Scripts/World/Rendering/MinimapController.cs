using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Contrôle la caméra minimap de la vue plate Mercator.
///
/// Architecture :
///   - Placé sur un GO <c>MinimapCamera</c> enfant de <c>PlanetFlatView</c>.
///   - La caméra filme toujours l'intégralité du mesh PlanetFlatMesh depuis le dessus.
///   - La RenderTexture est assignée à un RawImage dans un Canvas overlay UI.
///   - Un indicateur de viewport (petit rectangle) sur le RawImage montre où
///     se trouve la caméra principale dans la carte.
///   - Un clic sur le RawImage téléporte la caméra principale à cet endroit.
///
/// Setup Unity (après compilation) :
///   1. Créer GO <c>MinimapCamera</c> enfant de <c>PlanetFlatView</c>.
///   2. Ajouter ce composant + composant Camera sur ce GO.
///   3. Créer une RenderTexture 512×256 (Assets → Create → Render Texture).
///   4. Assigner la RenderTexture dans l'Inspector et dans la Camera.targetTexture.
///   5. Créer un Canvas Screen Space Overlay avec un Panel + RawImage (source = RT).
///   6. Assigner rawImage et viewportIndicator en Inspector.
///   7. Assigner mainCameraController.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MinimapController : MonoBehaviour
{
    // =========================================================
    // Inspector
    // =========================================================

    [Header("Références")]
    [Tooltip("Caméra principale (CameraController) à téléporter au clic minimap.")]
    [SerializeField] private CameraController mainCameraController;

    [Tooltip("RawImage UI où la RenderTexture est affichée.")]
    [SerializeField] private RawImage rawImage;

    [Tooltip("Image enfant du RawImage représentant le viewport de la caméra principale.")]
    [SerializeField] private RectTransform viewportIndicator;

    [Header("UI Panel")]
    [Tooltip("Root GO du panneau minimap dans le Canvas (MinimapPanel). Caché automatiquement en vue Globe.")]
    [SerializeField] private GameObject minimapPanel;

    [Header("Rendu minimap")]
    [Tooltip("RenderTexture cible (assigner ici ET sur la Camera.targetTexture).")]
    [SerializeField] private RenderTexture minimapRT;

    [Tooltip("Hauteur de la caméra minimap au-dessus du mesh (unités monde).")]
    [SerializeField] private float cameraHeight = 500f;

    [Tooltip("Marge autour des bounds du mesh (% du demi-size).")]
    [SerializeField] private float boundsMargin = 0.05f;

    // =========================================================
    // Runtime
    // =========================================================

    private Camera     _cam;
    private Camera     _mainCam;
    private PlanetFlatMesh _flatMesh;

    // Bounds du mesh en coordonnées monde, calculées à LoadPlanet
    private Bounds _meshBounds;
    private bool   _isReady;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags   = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
        // Masque : tout sauf UI layer (layer 5)
        _cam.cullingMask  = ~(1 << 5);
        _cam.depth        = -2f;    // rendu avant la caméra principale

        if (minimapRT != null)
            _cam.targetTexture = minimapRT;

        if (rawImage != null && minimapRT != null)
            rawImage.texture = minimapRT;

        _mainCam = Camera.main;
    }

    private void LateUpdate()
    {
        if (!_isReady || viewportIndicator == null || rawImage == null) return;
        UpdateViewportIndicator();
    }

    private void OnEnable()
    {
        if (_isReady)
            ApplyPosition();
        if (minimapPanel != null)
            minimapPanel.SetActive(true);
    }

    private void OnDisable()
    {
        if (minimapPanel != null)
            minimapPanel.SetActive(false);
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Initialise la minimap depuis les bounds réelles du mesh Mercator.
    /// Appelé par PlanetFlatView après LoadPlanet.
    /// </summary>
    public void Setup(PlanetFlatMesh flatMesh, Transform meshTransform)
    {
        _flatMesh = flatMesh;
        RefreshBounds(meshTransform);
        ApplyPosition();
        _isReady = true;
    }

    /// <summary>
    /// Recalcule les bounds depuis le mesh et repositionne la caméra.
    /// Appeler après tout changement de grille.
    /// </summary>
    public void RefreshBounds(Transform meshTransform)
    {
        if (_flatMesh == null) return;

        Bounds localBounds = _flatMesh.GetBounds();
        // Convertit les bounds locales → monde
        Vector3 worldCenter = meshTransform.TransformPoint(localBounds.center);
        Vector3 worldSize   = Vector3.Scale(localBounds.size, meshTransform.lossyScale);
        _meshBounds = new Bounds(worldCenter, worldSize);
    }

    /// <summary>
    /// Repositionne et recalibre la caméra minimap pour voir toute la carte.
    /// </summary>
    public void ApplyPosition()
    {
        if (_cam == null) return;

        // Placement au-dessus du centre du mesh
        Vector3 pos = _meshBounds.center;
        pos.y = _meshBounds.center.y + cameraHeight;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // orthographicSize = demi-hauteur du mesh + marge
        float halfH = _meshBounds.extents.z * (1f + boundsMargin);
        float halfW = _meshBounds.extents.x * (1f + boundsMargin);

        // Adapte au ratio de la RT : prend le max pour tout afficher
        float rtRatio = (minimapRT != null)
            ? (float)minimapRT.width / minimapRT.height
            : 2f;

        float sizeFromHeight = halfH;
        float sizeFromWidth  = halfW / rtRatio;
        _cam.orthographicSize = Mathf.Max(sizeFromHeight, sizeFromWidth);
    }

    /// <summary>
    /// Appelé par MinimapClickHandler quand l'utilisateur clique sur le RawImage.
    /// uv : coordonnées normalisées [0,1]² dans la RenderTexture (0,0 = bas-gauche).
    /// </summary>
    public void OnMinimapClicked(Vector2 uv)
    {
        if (!_isReady || mainCameraController == null) return;

        // Convertit UV → world XZ depuis les bounds du mesh
        float worldX = Mathf.Lerp(_meshBounds.min.x, _meshBounds.max.x, uv.x);
        float worldZ = Mathf.Lerp(_meshBounds.min.z, _meshBounds.max.z, uv.y);

        mainCameraController.FocusOn(new Vector3(worldX, 0f, worldZ));
    }

    // =========================================================
    // Viewport indicator
    // =========================================================

    private void UpdateViewportIndicator()
    {
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null || !_mainCam.orthographic) return;

        // Viewport de la caméra principale : rectangle en world XZ
        float orthoSize = _mainCam.orthographicSize;
        float aspect    = _mainCam.aspect;
        Vector3 camPos  = _mainCam.transform.position;

        float viewMinX = camPos.x - orthoSize * aspect;
        float viewMaxX = camPos.x + orthoSize * aspect;
        float viewMinZ = camPos.z - orthoSize;
        float viewMaxZ = camPos.z + orthoSize;

        // Normalise par rapport aux bounds du mesh
        float meshW = _meshBounds.size.x;
        float meshH = _meshBounds.size.z;
        if (meshW < 0.001f || meshH < 0.001f) return;

        float normMinX = (viewMinX - _meshBounds.min.x) / meshW;
        float normMaxX = (viewMaxX - _meshBounds.min.x) / meshW;
        float normMinZ = (viewMinZ - _meshBounds.min.z) / meshH;
        float normMaxZ = (viewMaxZ - _meshBounds.min.z) / meshH;

        // Clampe pour rester dans le RawImage
        normMinX = Mathf.Clamp01(normMinX);
        normMaxX = Mathf.Clamp01(normMaxX);
        normMinZ = Mathf.Clamp01(normMinZ);
        normMaxZ = Mathf.Clamp01(normMaxZ);

        // Taille du RawImage en pixels
        Rect rawRect = rawImage.rectTransform.rect;
        float pxW = rawRect.width;
        float pxH = rawRect.height;

        float indW = (normMaxX - normMinX) * pxW;
        float indH = (normMaxZ - normMinZ) * pxH;
        float indX = normMinX * pxW - pxW * 0.5f;      // ancre centre du RawImage
        float indY = normMinZ * pxH - pxH * 0.5f;

        viewportIndicator.sizeDelta        = new Vector2(Mathf.Max(indW, 4f), Mathf.Max(indH, 4f));
        viewportIndicator.anchoredPosition = new Vector2(indX + indW * 0.5f, indY + indH * 0.5f);
    }
}
