using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bouton de bascule Globe ↔ Carte pour la Vue Planétaire.
///
/// Ajouter ce composant sur un GameObject avec un Button UI.
/// Le bouton est automatiquement visible seulement en Vue Planétaire.
///
/// Setup Unity :
///   1. Créer un bouton UI dans le Canvas principal.
///   2. Ajouter ce composant sur ce même GameObject.
///   3. Assigner viewManager en Inspector (ou auto-trouvé au Start).
///   4. Assigner le Text/TMP enfant optionnel pour le libellé dynamique.
/// </summary>
[RequireComponent(typeof(Button))]
public class PlanetViewToggleButton : MonoBehaviour
{
    [SerializeField] private ViewManager viewManager;

    [Tooltip("Texte optionnel à mettre à jour selon la sous-vue active.")]
    [SerializeField] private TMPro.TMP_Text label;

    private Button _button;

    private const string LabelGlobe = "Vue Carte";
    private const string LabelFlat  = "Vue Globe";

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnClick);
    }

    private void Start()
    {
        if (viewManager == null)
            viewManager = FindObjectOfType<ViewManager>();

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
        viewManager?.TogglePlanetView();
        Refresh();
    }

    private void HandleViewChanged(ViewManager.ViewState state) => Refresh();

    private void Refresh()
    {
        if (viewManager == null) return;

        // Visible seulement en Vue Planétaire
        bool isVisible = viewManager.CurrentState == ViewManager.ViewState.Planet;
        gameObject.SetActive(isVisible);

        if (!isVisible) return;

        // Met à jour le libellé selon la sous-vue active
        if (label != null)
            label.text = viewManager.CurrentPlanetSubView == ViewManager.PlanetSubView.Globe
                ? LabelGlobe
                : LabelFlat;
    }
}
