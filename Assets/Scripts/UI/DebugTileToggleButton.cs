using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Bouton de toggle pour l'overlay de debug des tuiles (vue plate).
///
/// Se cache automatiquement hors de la sous-vue Flat.
///
/// Setup Unity :
///   1. Créer un bouton UI dans le Canvas principal (ou PlanetFlatOverlayCanvas).
///   2. Ajouter ce composant sur ce même GameObject.
///   3. Assigner flatDebugOverlay (FlatDebugOverlay sur PlanetFlatView).
///   4. Assigner le TMP_Text enfant pour le label dynamique.
/// </summary>
[RequireComponent(typeof(Button))]
public class DebugTileToggleButton : MonoBehaviour
{
    [SerializeField] private FlatDebugOverlay flatDebugOverlay;
    [SerializeField] private TMP_Text         label;

    private Button _button;

    private const string LabelOn  = "Debug ON";
    private const string LabelOff = "Debug Tiles";

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnClick);
    }

    private void Start()
    {
        if (flatDebugOverlay == null)
            flatDebugOverlay = FindFirstObjectByType<FlatDebugOverlay>();

        ViewManager.OnViewChanged += HandleViewChanged;
        Refresh();
    }

    private void OnDestroy()
    {
        ViewManager.OnViewChanged -= HandleViewChanged;
        if (_button != null) _button.onClick.RemoveListener(OnClick);
    }

    private void OnClick()
    {
        flatDebugOverlay?.Toggle();
        Refresh();
    }

    private void HandleViewChanged(ViewManager.ViewState state) => Refresh();

    private void Refresh()
    {
        // Visible seulement en sous-vue Flat
        var vm = FindAnyObjectByType<ViewManager>();
        bool isFlat = vm != null
            && vm.CurrentState == ViewManager.ViewState.Planet
            && vm.CurrentPlanetSubView == ViewManager.PlanetSubView.Flat;

        gameObject.SetActive(isFlat);

        if (label == null) return;
        bool isOn = flatDebugOverlay != null && flatDebugOverlay.IsVisible;
        label.text = isOn ? LabelOn : LabelOff;
    }
}
