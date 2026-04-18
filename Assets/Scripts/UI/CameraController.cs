using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections;

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
    [SerializeField] private float zoomScaleFactor = 0.15f;

    [Header("Orbit (OrbitPerspective)")]
    [SerializeField] private float orbitSensitivity = 0.3f;
    [SerializeField] private float orbitMinDistance = 5f;
    [SerializeField] private float orbitMaxDistance = 200f;
    [SerializeField] private float orbitScrollSpeed = 3f;
    [SerializeField] private float orbitKeyboardPanSpeed = 20f;
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
    private float   _currentMinZoom = 5f;
    private float   _currentMaxZoom = 80f;

    // --- OrbitPerspective ---
    private Vector3 _orbitPivot    = Vector3.zero;
    private float   _orbitDistance = 50f;
    private float   _orbitAzimuth  = 0f;    // degrés horizontale
    private float   _orbitElevation = 45f;  // degrés depuis l'horizon
    private float   _activeOrbitMinDistance;
    private float   _activeOrbitMaxDistance;
    private bool    _isOrbiting;
    private Vector2 _lastMousePos;
    private bool    _orbitKeyboardPanEnabled;

    // --- Animation ---
    private bool _isAnimating;  // bloque l'input orbit/zoom pendant OrbitToFace

    // =========================================================
    // Propriétés publiques
    // =========================================================

    /// <summary>Distance courante du pivot (mode OrbitPerspective). Utile pour LOD.</summary>
    public float OrbitDistance => _orbitDistance;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        _activeOrbitMinDistance = orbitMinDistance;
        _activeOrbitMaxDistance = orbitMaxDistance;
    }

    private void Update()
    {
        if (_isAnimating) return;

        if (mode == CameraMode.OrthoTopDown)
        {
            HandlePan();
            HandleZoomOrtho();
        }
        else
        {
            HandleOrbit();
            HandleOrbitKeyboardPan();
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
        _currentMinZoom = minBound;
        _currentMaxZoom = maxBound;
        _minEventFired = false;
        _maxEventFired = false;

        if (newMode == CameraMode.OrthoTopDown)
        {
            _cam.orthographic = true;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize, _currentMinZoom, _currentMaxZoom);
        }
        else
        {
            _cam.orthographic = false;
            _activeOrbitMinDistance = minBound;
            _activeOrbitMaxDistance = maxBound;
            _orbitPivot    = orbitPivot;
            _orbitDistance = Mathf.Clamp(_orbitDistance, _activeOrbitMinDistance, _activeOrbitMaxDistance);
            ApplyOrbitTransform();
        }
    }

    /// <summary>Repositionne la caméra ortho sur un point XZ donné.</summary>
    public void FocusOn(Vector3 worldPos, float? zoom = null)
    {
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        float height = transform.position.y;
        if (height < 20f)
            height = 60f;

        transform.position = new Vector3(worldPos.x, height, worldPos.z);
        if (zoom.HasValue)
            _cam.orthographicSize = Mathf.Clamp(zoom.Value, _currentMinZoom, _currentMaxZoom);
    }

    /// <summary>Place l'orbite sur un nouveau pivot (vue planétaire).</summary>
    public void SetOrbitPivot(Vector3 pivot, float distance)
    {
        _orbitPivot    = pivot;
        _orbitDistance = Mathf.Clamp(distance, _activeOrbitMinDistance, _activeOrbitMaxDistance);
        ApplyOrbitTransform();
    }

    public void SetOrbitKeyboardPanEnabled(bool enabled)
    {
        _orbitKeyboardPanEnabled = enabled;
    }

    /// <summary>
    /// Anime l'orbite pour faire face à une direction 3D (centroïde de face GP).
    /// Utilisé pour orienter la caméra vers la face sélectionnée avant d'ouvrir l'overlay local.
    /// </summary>
    public void OrbitToFace(Vector3 worldDir, float targetDistance, float duration = 0.5f, Action onComplete = null)
    {
        StopAllCoroutines();
        StartCoroutine(OrbitToFaceCoroutine(worldDir, targetDistance, duration, onComplete));
    }

    private IEnumerator OrbitToFaceCoroutine(Vector3 worldDir, float targetDistance, float duration, Action onComplete)
    {
        _isAnimating = true;

        // Calcule azimut et élévation cibles depuis worldDir
        Vector3 dir = worldDir.normalized;
        float targetElevation = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        float targetAzimuth   = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // Normalise la différence d'azimut pour prendre le chemin le plus court
        float deltAz = Mathf.DeltaAngle(_orbitAzimuth, targetAzimuth);
        float targetAzNorm = _orbitAzimuth + deltAz;

        float startAz   = _orbitAzimuth;
        float startEl   = _orbitElevation;
        float startDist = _orbitDistance;
        float targetEl  = Mathf.Clamp(targetElevation, orbitMinElevation, orbitMaxElevation);
        float clampedDist = Mathf.Clamp(targetDistance, _activeOrbitMinDistance, _activeOrbitMaxDistance);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            _orbitAzimuth   = Mathf.Lerp(startAz,   targetAzNorm, t);
            _orbitElevation = Mathf.Lerp(startEl,   targetEl,     t);
            _orbitDistance  = Mathf.Lerp(startDist, clampedDist,  t);
            ApplyOrbitTransform();
            yield return null;
        }

        _orbitAzimuth   = targetAzNorm;
        _orbitElevation = targetEl;
        _orbitDistance  = clampedDist;
        ApplyOrbitTransform();

        _isAnimating = false;
        onComplete?.Invoke();
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
            float zoomMultiplier = 1f + (zoomScaleFactor * Mathf.Abs(scroll));
            float newSize = Mathf.Clamp(
                scroll > 0f
                    ? _cam.orthographicSize / zoomMultiplier
                    : _cam.orthographicSize * zoomMultiplier,
                _currentMinZoom, _currentMaxZoom);
            _cam.orthographicSize = newSize;
        }

        // Events bornes
        if (_cam.orthographicSize <= _currentMinZoom + 0.01f && !_minEventFired)
        {
            _minEventFired = true;
            _maxEventFired = false;
            OnZoomedToMin?.Invoke();
        }
        else if (_cam.orthographicSize >= _currentMaxZoom - 0.01f && !_maxEventFired)
        {
            _maxEventFired = true;
            _minEventFired = false;
            OnZoomedToMax?.Invoke();
        }
        else if (_cam.orthographicSize > _currentMinZoom + 1f && _cam.orthographicSize < _currentMaxZoom - 1f)
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
                _activeOrbitMinDistance, _activeOrbitMaxDistance);
            ApplyOrbitTransform();
        }

        if (_orbitDistance <= _activeOrbitMinDistance + 0.1f && !_minEventFired)
        {
            _minEventFired = true;
            _maxEventFired = false;
            OnZoomedToMin?.Invoke();
        }
        else if (_orbitDistance >= _activeOrbitMaxDistance - 0.1f && !_maxEventFired)
        {
            _maxEventFired = true;
            _minEventFired = false;
            OnZoomedToMax?.Invoke();
        }
        else if (_orbitDistance > _activeOrbitMinDistance + 1f && _orbitDistance < _activeOrbitMaxDistance - 1f)
        {
            _minEventFired = false;
            _maxEventFired = false;
        }
    }

    private void HandleOrbitKeyboardPan()
    {
        if (!_orbitKeyboardPanEnabled || Keyboard.current == null)
            return;

        Vector2 move = Vector2.zero;

        if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed || Keyboard.current.qKey.isPressed)
            move.x -= 1f;
        if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed)
            move.x += 1f;
        if (Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed || Keyboard.current.zKey.isPressed)
            move.y += 1f;
        if (Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed)
            move.y -= 1f;

        if (move.sqrMagnitude <= 0f)
            return;

        move = move.normalized;

        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

        float speed = orbitKeyboardPanSpeed * Mathf.Max(1f, _orbitDistance * 0.05f) * Time.deltaTime;
        _orbitPivot += (right * move.x + forward * move.y) * speed;
        ApplyOrbitTransform();
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
        float s = 0f;

        if (Mouse.current != null)
            s = Mouse.current.scroll.ReadValue().y;

        // Raccourcis de test: PageUp / + = zoom in, PageDown / - = zoom out.
        if (Keyboard.current != null)
        {
            if (Keyboard.current.pageUpKey.isPressed)
                s += 240f;
            if (Keyboard.current.pageDownKey.isPressed)
                s -= 360f;
            if (Keyboard.current.numpadPlusKey.isPressed || Keyboard.current.equalsKey.isPressed)
                s += 240f;
            if (Keyboard.current.numpadMinusKey.isPressed || Keyboard.current.minusKey.isPressed)
                s -= 360f;
        }

        return Mathf.Abs(s) > 1f ? s / 120f : s;
    }

    private Vector3 ScreenToXZPlane(Vector2 screenPos)
    {
        Ray   ray = _cam.ScreenPointToRay(screenPos);
        float t   = -ray.origin.y / ray.direction.y;
        return ray.origin + t * ray.direction;
    }
}

