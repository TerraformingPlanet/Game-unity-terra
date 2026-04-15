using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-down orthographic camera controller.
/// - Right-click drag  → pan on the XZ plane
/// - Scroll wheel      → zoom (adjust orthographic size)
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Pan")]
    [SerializeField] private float panSpeed = 0.5f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 8f;
    [SerializeField] private float minZoom = 5f;
    [SerializeField] private float maxZoom = 80f;

    private Camera _cam;
    private bool _isPanning;
    private Vector3 _dragOriginWorld;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    private void Update()
    {
        HandlePan();
        HandleZoom();
    }

    private void HandlePan()
    {
        var mouse = Mouse.current;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            _isPanning = true;
            _dragOriginWorld = ScreenToXZPlane(mouse.position.ReadValue());
        }

        if (mouse.rightButton.wasReleasedThisFrame)
            _isPanning = false;

        if (_isPanning && mouse.rightButton.isPressed)
        {
            Vector3 currentWorld = ScreenToXZPlane(mouse.position.ReadValue());
            Vector3 delta = _dragOriginWorld - currentWorld;
            transform.position += delta;
            // Recalculate origin from new camera position so delta doesn't accumulate
            _dragOriginWorld = ScreenToXZPlane(mouse.position.ReadValue());
        }
    }

    private void HandleZoom()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        scroll = Mathf.Abs(scroll) > 1f ? scroll / 120f : scroll; // normalise scroll Windows (120 par cran) ou déjà normalisé (±1)
        if (Mathf.Approximately(scroll, 0f)) return;

        _cam.orthographicSize = Mathf.Clamp(
            _cam.orthographicSize - scroll * zoomSpeed,
            minZoom,
            maxZoom
        );
    }

    /// <summary>
    /// Projects a screen-space point onto the Y=0 XZ world plane.
    /// Works with a top-down orthographic camera regardless of height.
    /// </summary>
    private Vector3 ScreenToXZPlane(Vector2 screenPos)
    {
        Ray ray = _cam.ScreenPointToRay(screenPos);
        // Y=0 plane: t = -ray.origin.y / ray.direction.y
        float t = -ray.origin.y / ray.direction.y;
        return ray.origin + t * ray.direction;
    }
}
