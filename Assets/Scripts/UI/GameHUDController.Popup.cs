using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Partial — Tooltip flottant et Event-Popup centraux du HUD.
/// Extrait de GameHUDController pour alléger le fichier principal.
/// </summary>
public partial class GameHUDController
{
    // ── Tooltip ──────────────────────────────────────────────────────────────

    private VisualElement _tooltip;
    private Label         _tooltipLabel;

    private void BuildTooltip()
    {
        if (tooltipTemplate != null)
        {
            tooltipTemplate.CloneTree(_root);
            _tooltip      = _root.Q<VisualElement>("hud-tooltip");
            _tooltipLabel = _root.Q<Label>("hud-tooltip-label");
        }
        else
        {
            _tooltip = new VisualElement { name = "hud-tooltip" };
            _tooltip.AddToClassList("hud-tooltip");
            _tooltipLabel = new Label { name = "hud-tooltip-label" };
            _tooltipLabel.AddToClassList("hud-tooltip__label");
            _tooltip.Add(_tooltipLabel);
            _root.Add(_tooltip);
        }
        if (_tooltip != null) { _tooltip.pickingMode = PickingMode.Ignore; _tooltip.style.display = DisplayStyle.None; }
    }

    private void OnTileHoverReady(string text, UnityEngine.Vector2 screenPos)
    {
        if (_tooltip == null) return;
        _tooltipLabel.text = text;
        _tooltip.style.display = DisplayStyle.Flex;
        float px = screenPos.x + 16f;
        float py = UnityEngine.Screen.height - screenPos.y + 16f;
        if (px + 240f > UnityEngine.Screen.width)  px = screenPos.x - 256f;
        if (py + 50f  > UnityEngine.Screen.height) py = UnityEngine.Screen.height - screenPos.y - 60f;
        _tooltip.style.left = px;
        _tooltip.style.top  = py;
    }

    private void OnTileHoverCancelled()
    {
        if (_tooltip != null) _tooltip.style.display = DisplayStyle.None;
    }

    // ── Event Popup ──────────────────────────────────────────────────────────

    private VisualElement _eventPopup;
    private Label         _eventPopupTitle;
    private Label         _eventPopupBody;
    private Coroutine     _autoHidePopupCoroutine;

    private void BuildEventPopup()
    {
        if (eventPopupTemplate != null)
        {
            eventPopupTemplate.CloneTree(_root);
            _eventPopup      = _root.Q<VisualElement>("event-popup");
            _eventPopupTitle = _root.Q<Label>("event-popup-title");
            _eventPopupBody  = _root.Q<Label>("event-popup-body");
        }
        else
        {
            _eventPopup = new VisualElement { name = "event-popup" };
            _eventPopup.AddToClassList("event-popup");
            _eventPopupTitle = new Label { name = "event-popup-title" };
            _eventPopupTitle.AddToClassList("event-popup__title");
            _eventPopupBody  = new Label { name = "event-popup-body" };
            _eventPopupBody.AddToClassList("event-popup__body");
            _eventPopup.Add(_eventPopupTitle);
            _eventPopup.Add(_eventPopupBody);
            _root.Add(_eventPopup);
        }
        if (_eventPopup != null)
        {
            _eventPopup.pickingMode      = PickingMode.Ignore;
            _eventPopup.style.position   = Position.Absolute;
            _eventPopup.style.top        = new StyleLength(56f);
            _eventPopup.style.left       = new StyleLength(UnityEngine.UIElements.Length.Percent(50));
            _eventPopup.style.marginLeft = new StyleLength(-170f);
            _eventPopup.style.display    = DisplayStyle.None;
        }
    }

    public void ShowEventPopup(string title, string body = "", float autoHideSeconds = 4f)
    {
        if (_eventPopup == null) return;
        if (_eventPopupTitle != null) _eventPopupTitle.text = title;
        if (_eventPopupBody  != null)
        {
            _eventPopupBody.text = body;
            _eventPopupBody.style.display = string.IsNullOrEmpty(body) ? DisplayStyle.None : DisplayStyle.Flex;
        }
        _eventPopup.style.display = DisplayStyle.Flex;
        if (_autoHidePopupCoroutine != null) StopCoroutine(_autoHidePopupCoroutine);
        _autoHidePopupCoroutine = StartCoroutine(AutoHidePopup(autoHideSeconds));
    }

    public void HideEventPopup()
    {
        if (_autoHidePopupCoroutine != null) { StopCoroutine(_autoHidePopupCoroutine); _autoHidePopupCoroutine = null; }
        if (_eventPopup != null) _eventPopup.style.display = DisplayStyle.None;
    }

    private IEnumerator AutoHidePopup(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_eventPopup != null) _eventPopup.style.display = DisplayStyle.None;
        _autoHidePopupCoroutine = null;
    }
}
