using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reçoit les clics sur le RawImage de la minimap et les transmet au MinimapController.
///
/// Placer ce composant sur le même GameObject que le RawImage de la minimap.
/// Assigner minimapController en Inspector.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MinimapClickHandler : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private MinimapController minimapController;

    private RectTransform _rect;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();

        if (minimapController == null)
            minimapController = GetComponentInParent<MinimapController>();
    }

    /// <summary>Appelé par le système d'événements Unity au clic sur le RawImage.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (minimapController == null) return;

        // Convertit la position du clic en UV normalisé [0,1]² dans le RawImage
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        // localPoint est en pixels centrés sur le RectTransform (min=-w/2, max=+w/2)
        Rect r = _rect.rect;
        float u = (localPoint.x - r.xMin) / r.width;
        float v = (localPoint.y - r.yMin) / r.height;

        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);

        minimapController.OnMinimapClicked(new Vector2(u, v));
    }
}
