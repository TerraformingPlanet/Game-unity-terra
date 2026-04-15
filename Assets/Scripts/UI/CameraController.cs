using UnityEngine;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// Contrôleur de caméra multi-mode pour le système de vues 3 niveaux.
///
/// Modes disponibles (configurés par ViewManager) :
///   OrthoTopDown    — pan XZ (right-drag) + zoom (orthographicSize). Vue Solaire + Locale.
///   OrbitPerspective — orbite autour d'un pivot 3D (right-drag = azimut/élévation, scroll = distance). Vue Planétaire.
///
/// Events :
///   OnZoomedToMin — déclenché quand le zoom atteint sa borne inférieure (scroll in max)
///   OnZoomedToMax — déclenché quand le zoom atteint sa borne supérieure (scroll out max)
/// </summary>
public class CameraController : MonoBehaviour
{
    public enum CameraMode { OrthoTopDown, OrbitPerspective }

    // =========================================================
    // Events
    // =========================================================

    /// <summary>Scroll in au max — ViewManager l'utilise pour descendre d'un niveau de vue.</summary>
    public event Action OnZoomedToMin;

    /// <summary>Scroll out au max — ViewManager l'utilise pour remonter d'un niveau de vue.</summary>
    public event Action OnZoomedToMax;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Mode")]
    [SerializeField] private CameraMode mode = CameraMode.OrthoTopDown;

    [Header("Pan (OrthoTopDown)")]
    [SerializeField] private float panSpeed = 0.5f;

    [Header("Zoom (OrthoTopDown)")]
    [SerializeField] private float zoomSpeed = 8f;
    [SerializeField] private float minZoom   = 5f;
    [SerializeField] private float maxZoom   = 80f;

    [Header("Orbit (OrbitPerspective)")]
    [SerializeField] private float orbitSensitivity = 0.3f;
    [SerializeField] private float orbitMinDistance = 5f;
    [SerializeField] private float orbitMaxDistance = 200f;
    [SerializeField] private float orbitScrollSpeed = 10f;
    [SerializeField] private float orbitMinElevation = 5f;   // degrés min au-dessus de l'horizon
    [SerializeField] private float orbitMaxElevation = 89f;  // degrés max (quasi zénith)

    // =========================================================
    // Runtime
    // =========================================================

    private Camera _cam;

    // --- OrthoTopDown ---
    private bool    _isPanning;
    private Vector3 _dragOriginWorld;
    private bool    _minEventFired;
    private bool    _maxEventFired;

    // --- OrbitPerspective ---
    private Vector3 _orbitPivot    = Vector3.zero;
    private float   _orbitDistance = 50f;
    private float   _orbitAzimuth  = 0f;    // degrés horizontale
    private float   _orbitElevation = 45f;  // degrés depuis l'horizon
    private bool    _isOrbiting;
    private Vector2 _lastMousePos;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    private void Update()
    {
        if (mode == CameraMode.OrthoTopDown)
        {
            HandlePan();
            HandleZoomOrtho();
        }
        else
        {
            HandleOrbit();
            HandleZoomOrbit();
        }
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Change le mode caméra. Adapte projection et bounds.
    /// </summary>
    public void SetMode(CameraMode newMode,
                        float minBound, float maxBound,
                        Vector3 orbitPivot = default)
    {
        mode    = newMode;
        minZoom = minBound;
        maxZoom = maxBound;
        _minEventFired = false;
        _maxEventFired = false;

        if (newMode == CameraMode.OrthoTopDown)
        {
            _cam.orthographic = true;
            _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize, minBound, maxBound);
        }
        else
        {
            _cam.orthographic = false;
            _orbitPivot    = orbitPivot;
            _orbitDistance = Mathf.Clamp(_orbitDistance, orbitMinDistance, orbitMaxDistance);
            ApplyOrbitTransform();
        }
    }

    /// <summary>Repositionne la caméra ortho sur un point XZ donné.</summary>
    public void FocusOn(Vector3 worldPos, float? zoom = null)
    {
        transform.position = new Vector3(worldPos.x, transform.position.y, worldPos.z);
        if (zoom.HasValue)
            _cam.orthographicSize = Mathf.Clamp(zoom.Value, minZoom, maxZoom);
    }

    /// <summary>Place l'orbite sur un nouveau pivot (vue planétaire).</summary>
    public void SetOrbitPivot(Vector3 pivot, float distance)
    {
        _orbitPivot    = pivot;
        _orbitDistance = Mathf.Clamp(distance, orbitMinDistance, orbitMaxDistance);
        ApplyOrbitTransform();
    }

    // =========================================================
    // OrthoTopDown — Pan
    // =========================================================

    private void HandlePan()
    {
        var mouse = Mouse.current;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            _isPanning       = true;
            _dragOriginWorld = ScreenToXZPlane(mouse.position.ReadValue());
        }

        if (mouse.rightButton.wasReleasedThisFrame)
            _isPanning = false;

        if (_isPanning && mouse.rightButton.isPressed)
        {
            Vector3 currentWorld = ScreenToXZPlane(mouse.position.ReadValue());
            Vector3 delta        = _dragOriginWorld - currentWorld;
            transform.position  += delta;
            _dragOriginWorld     = ScreenToXZPlane(mouse.position.ReadValue());
        }
    }

    // =========================================================
    // OrthoTopDown — Zoom + events
    // =========================================================

    private void HandleZoomOrtho()
    {
        float scroll = ReadScroll();
        if (!Mathf.Approximately(scroll, 0f))
        {
            float newSize = Mathf.Clamp(
                _cam.orthographicSize - scroll * zoomSpeed,
                minZoom, maxZoom);
            _cam.orthographicSize = newSize;
        }

        // Events bornes
        if (_cam.orthographicSize <= minZoom + 0.01f && !_minEventFired)
        {
            _minEventFired = true;
            _maxEventFired = false;
            OnZoomedToMin?.Invoke();
        }
        else if (_cam.orthographicSize >= maxZoom - 0.01f && !_maxEventFired)
        {
            _maxEventFired = true;
            _minEventFired = false;
            OnZoomedToMax?.Invoke();
        }
        else if (_cam.orthographicSize > minZoom + 1f && _cam.orthographicSize < maxZoom - 1f)
        {
            // Réinitialise si on s'éloigne des bornes (permet de re-déclencher)
            _minEventFired = false;
            _maxEventFired = false;
        }
    }

    // =========================================================
    // OrbitPerspective — Rotation
    // =========================================================

    private void HandleOrbit()
    {
        var mouse = Mouse.current;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            _isOrbiting   = true;
            _lastMousePos = mouse.position.ReadValue();
        }

        if (mouse.rightButton.wasReleasedThisFrame)
            _isOrbiting = false;

        if (_isOrbiting)
        {
            Vector2 currentPos = mouse.position.ReadValue();
            Vector2 delta      = currentPos - _lastMousePos;
            _lastMousePos      = currentPos;

            _orbitAzimuth   -= delta.x * orbitSensitivity;
            _orbitElevation  = Mathf.Clamp(
                _orbitElevation + delta.y * orbitSensitivity,
                orbitMinElevation, orbitMaxElevation);

            ApplyOrbitTransform();
        }
    }

    // =========================================================
    // OrbitPerspective — Distance (zoom) + events
    // =========================================================

    private void HandleZoomOrbit()
    {
        float scroll = ReadScroll();
        if (!Mathf.Approximately(scroll, 0f))
        {
            _orbitDistance = Mathf.Clamp(
                _orbitDistance - scroll * orbitScrollSpeed,
                orbitMinDistance, orbitMaxDistance);
            ApplyOrbitTransform();
        }

        if (_orbitDistance <= orbitMinDistance + 0.1f && !_minEventFired)
        {
            _minEventFired = true;
            _maxEventFired = false;
            OnZoomedToMin?.Invoke();
        }
        else if (_orbitDistance >= orbitMaxDistance - 0.1f && !_maxEventFired)
        {
            _maxEventFired = true;
            _minEventFired = false;
            OnZoomedToMax?.Invoke();
        }
        else if (_orbitDistance > orbitMinDistance + 1f && _orbitDistance < orbitMaxDistance - 1f)
        {
            _minEventFired = false;
            _maxEventFired = false;
        }
    }

    private void ApplyOrbitTransform()
    {
        float azRad  = _orbitAzimuth   * Mathf.Deg2Rad;
        float elRad  = _orbitElevation * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Cos(elRad) * Mathf.Sin(azRad),
            Mathf.Sin(elRad),
            Mathf.Cos(elRad) * Mathf.Cos(azRad)
        ) * _orbitDistance;

        transform.position = _orbitPivot + offset;
        transform.LookAt(_orbitPivot);
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static float ReadScroll()
    {
        float s = Mouse.current.scroll.ReadValue().y;
        return Mathf.Abs(s) > 1f ? s / 120f : s;
    }

    private Vector3 ScreenToXZPlane(Vector2 screenPos)
    {
        Ray   ray = _cam.ScreenPointToRay(screenPos);
        float t   = -ray.origin.y / ray.direction.y;
        return ray.origin + t * ray.direction;
    }
}

