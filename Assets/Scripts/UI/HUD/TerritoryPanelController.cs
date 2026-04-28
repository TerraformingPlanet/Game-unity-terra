using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// Right-side panel — Territory / State detail.
/// Opened when the player clicks the territory badge in the TileInspector header.
/// Hierarchy: Tuile → Territoire → État (future).
/// </summary>
public class TerritoryPanelController : MonoBehaviour
{
    private VisualElement _panel;
    private Button        _btnBadge;
    private Label         _titleLabel;
    private Label         _breadcrumbLabel;
    private Label         _stateSection;
    private Label         _territorySection;
    private Label         _populationSection;
    private Label         _statusLabel;

    private string _currentStateId;
    private GameHUDController _gameHUDController;

    // ─────────────────────────────────────────────────────────────────────────

    public void Initialize(VisualElement root)
    {
        _gameHUDController = GetComponent<GameHUDController>();
        BuildPanel(root);
    }

    private void BuildPanel(VisualElement root)
    {
        _panel = new VisualElement { name = "territory-panel" };
        _panel.AddToClassList("territory-panel");

        // ── Header ────────────────────────────────────────────────────────────
        var header = new VisualElement();
        header.AddToClassList("tile-inspector__header");

        _btnBadge = new Button { name = "territory-badge", text = "?" };
        _btnBadge.AddToClassList("tile-inspector__badge-btn");
        _btnBadge.pickingMode = PickingMode.Ignore; // read-only in this panel
        header.Add(_btnBadge);

        _titleLabel = new Label { name = "territory-title", text = "Territoire" };
        _titleLabel.AddToClassList("tile-inspector__header-title");
        header.Add(_titleLabel);

        var closeBtn = new Button { text = "✕" };
        closeBtn.AddToClassList("tile-inspector__close-btn");
        closeBtn.RegisterCallback<ClickEvent>(_ => Hide());
        header.Add(closeBtn);

        _panel.Add(header);

        // ── Breadcrumb ────────────────────────────────────────────────────────
        _breadcrumbLabel = new Label { text = "Tuile  ›  Territoire" };
        _breadcrumbLabel.style.fontSize    = 9;
        _breadcrumbLabel.style.color       = new StyleColor(new Color(0.45f, 0.45f, 0.45f));
        _breadcrumbLabel.style.paddingLeft  = 12f;
        _breadcrumbLabel.style.paddingTop   = 5f;
        _breadcrumbLabel.style.paddingBottom = 4f;
        _panel.Add(_breadcrumbLabel);

        var sep0 = new VisualElement();
        sep0.AddToClassList("hud-separator");
        _panel.Add(sep0);

        // ── Scrollable content ────────────────────────────────────────────────
        var scroll = new ScrollView { name = "territory-scroll" };
        scroll.AddToClassList("tile-inspector__scroll");
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.verticalScrollerVisibility   = ScrollerVisibility.Auto;

        // Status (loading / error)
        _statusLabel = new Label { name = "territory-status", text = "" };
        _statusLabel.style.fontSize      = 11;
        _statusLabel.style.color         = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
        _statusLabel.style.paddingLeft   = 12f;
        _statusLabel.style.paddingRight  = 12f;
        _statusLabel.style.paddingTop    = 12f;
        _statusLabel.style.paddingBottom = 12f;
        _statusLabel.style.whiteSpace    = WhiteSpace.Normal;
        scroll.Add(_statusLabel);

        // ÉTAT section
        var stateHeader = MakeSectionTitle("ÉTAT");
        scroll.Add(stateHeader);
        _stateSection = MakeSectionBody();
        scroll.Add(_stateSection);

        var sep1 = new VisualElement();
        sep1.AddToClassList("hud-separator");
        scroll.Add(sep1);

        // TERRITOIRE section
        var terrHeader = MakeSectionTitle("TERRITOIRE");
        scroll.Add(terrHeader);
        _territorySection = MakeSectionBody();
        scroll.Add(_territorySection);

        var sep2 = new VisualElement();
        sep2.AddToClassList("hud-separator");
        scroll.Add(sep2);

        // POPULATION section
        var popHeader = MakeSectionTitle("POPULATION");
        scroll.Add(popHeader);
        _populationSection = MakeSectionBody();
        scroll.Add(_populationSection);

        _panel.Add(scroll);
        root.Add(_panel);

        _panel.style.display = DisplayStyle.None;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public void ShowTerritory(string stateId, string stateName = null)
    {
        if (string.IsNullOrEmpty(stateId)) return;

        _currentStateId = stateId;
        _panel.style.display = DisplayStyle.Flex;

        // Populate immediately from available data
        string initials = GetInitials(stateName ?? stateId);
        _btnBadge.text    = initials;
        _titleLabel.text  = string.IsNullOrEmpty(stateName) ? stateId : stateName;

        _statusLabel.text       = "Chargement…";
        _stateSection.text      = "";
        _territorySection.text  = "";
        _populationSection.text = "";

        StartCoroutine(FetchAndRender(stateId));
    }

    public void Hide()
    {
        _panel.style.display = DisplayStyle.None;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal fetch + render
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator FetchAndRender(string stateId)
    {
        string url = _gameHUDController.GetSimulationServerUrl().TrimEnd('/') +
                     $"/game/states/{stateId}";

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(_gameHUDController.GetSimulationServerTimeout()));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                _statusLabel.text = $"Erreur : {req.error}";
                yield break;
            }

            StateDto dto;
            try { dto = JsonUtility.FromJson<StateDto>(req.downloadHandler.text); }
            catch { _statusLabel.text = "Réponse invalide."; yield break; }

            _statusLabel.text = "";
            RenderState(dto);
        }
    }

    private void RenderState(StateDto dto)
    {
        // Update header badge + title with canonical name
        if (!string.IsNullOrEmpty(dto.name))
        {
            _btnBadge.text   = GetInitials(dto.name);
            _titleLabel.text = dto.name;
        }

        // ── ÉTAT ──────────────────────────────────────────────────────────────
        string stateTypeStr = dto.stateType == 0 ? "Capitaliste" : "Nationaliste";
        string vassalStr;
        if (dto.isVassal)
            vassalStr = string.IsNullOrEmpty(dto.vassalCorpId)
                ? "Vassal (corp inconnue)"
                : $"Vassal de {dto.vassalCorpId}";
        else
            vassalStr = "Indépendant";

        _stateSection.text = $"Type : {stateTypeStr}\nStatut : {vassalStr}";

        // ── TERRITOIRE ────────────────────────────────────────────────────────
        int tileCount = dto.tileIds != null ? dto.tileIds.Length : 0;
        _territorySection.text = $"Tuiles contrôlées : {tileCount}";

        // ── POPULATION ────────────────────────────────────────────────────────
        string literacy  = $"Alphabétisation : {dto.literacyRate * 100f:F0}%";
        string profile   = $"Profil : {(string.IsNullOrEmpty(dto.profileKey) ? "Inconnu" : dto.profileKey)}";
        string classDist = GetClassDistributionText(dto.profileKey);
        _populationSection.text = $"{literacy}\n{profile}\n{classDist}".TrimEnd('\n');
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static VisualElement MakeSectionTitle(string text)
    {
        var wrapper = new VisualElement();
        wrapper.AddToClassList("tile-inspector__section");

        var lbl = new Label { text = text };
        lbl.AddToClassList("tile-inspector__section-title");
        wrapper.Add(lbl);
        return wrapper;
    }

    private static Label MakeSectionBody()
    {
        var lbl = new Label { text = "" };
        lbl.AddToClassList("tile-inspector__info-label");
        lbl.style.paddingLeft   = 12f;
        lbl.style.paddingRight  = 12f;
        lbl.style.paddingBottom = 8f;
        lbl.style.whiteSpace    = WhiteSpace.Normal;
        return lbl;
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(new char[] { ' ', '-', '_' },
            System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpperInvariant();
        return name.Length >= 2
            ? name.Substring(0, 2).ToUpperInvariant()
            : name.ToUpperInvariant();
    }

    private static string GetClassDistributionText(string profileKey)
    {
        switch (profileKey)
        {
            case "Standard":      return "Classes : Pauvre 40%, Moyen 59%, Riche 1%";
            case "RicheUtopique": return "Classes : Pauvre 1%, Moyen 98%, Riche 1%";
            case "EnDeveloppement": return "Classes : Pauvre 70%, Moyen 28%, Riche 2%";
            case "Pauvre":        return "Classes : Pauvre 85%, Moyen 14%, Riche 1%";
            case "Autoritaire":   return "Classes : Pauvre 60%, Moyen 35%, Riche 5%";
            default:              return "Classes : Inconnues";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DTO
    // ─────────────────────────────────────────────────────────────────────────

    [System.Serializable]
    private class StateDto
    {
        public string   id;
        public string   name;
        public bool     isVassal;
        public string   vassalCorpId;
        public float    literacyRate;
        public string   profileKey;
        public int      stateType;
        public string[] tileIds;
    }
}
