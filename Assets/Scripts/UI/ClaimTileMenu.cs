using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Menu temporaire de debug — apparaît au clic sur une tuile Globe (vue Planet/Globe).
/// Permet de claim ou unclaim la tuile au nom d'une corporation existante.
///
/// Setup :
///   - Attacher ce script à n'importe quel GameObject de la scène (ex: HUD ou ViewManager).
///   - Assigner planetSphere et renseigner simulationServerUrl / corpId en Inspector.
///   - Le Canvas est construit dynamiquement (aucun asset UI requis).
/// </summary>
public class ClaimTileMenu : MonoBehaviour
{
    // =========================================================
    // Inspector
    // =========================================================

    [Header("Références")]
    [SerializeField] private PlanetSphereGoldberg planetSphere;
    [SerializeField] private ViewManager          viewManager;

    [Header("Serveur")]
    [SerializeField] private string simulationServerUrl        = "http://127.0.0.1:8080";
    [SerializeField] private float  simulationServerTimeoutSeconds = 5f;

    // =========================================================
    // Runtime
    // =========================================================

    private Canvas          _canvas;
    private GameObject      _panel;
    private TextMeshProUGUI _titleLabel;
    private TextMeshProUGUI _statusLabel;
    private Button          _claimBtn;
    private Button          _unclaimBtn;
    private Button          _closeBtn;
    private TMP_Dropdown    _corpDropdown;

    private GoldbergTileState   _currentTile;
    private bool                _isOpen;

    private List<string> _corpIds   = new List<string>();
    private List<string> _corpNames = new List<string>();
    private string       _activeBodyId = "";

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        BuildCanvas();
        HidePanel();
    }

    private void Start()
    {
        // Auto-find si non assigné en Inspector
        if (viewManager == null)
            viewManager = FindFirstObjectByType<ViewManager>();
        if (planetSphere == null)
            planetSphere = FindFirstObjectByType<PlanetSphereGoldberg>();

        if (planetSphere != null)
            planetSphere.OnH3TileResolved += OnTileClicked;
    }

    private void OnDestroy()
    {
        if (planetSphere != null)
            planetSphere.OnH3TileResolved -= OnTileClicked;
    }

    // =========================================================
    // Event handler
    // =========================================================

    private void OnTileClicked(GoldbergTileState tile)
    {
        // Seulement en vue Globe
        if (viewManager != null
            && (viewManager.CurrentState != ViewManager.ViewState.Planet
                || viewManager.CurrentPlanetSubView != ViewManager.PlanetSubView.Globe))
            return;

        _currentTile = tile;
        _statusLabel.text = "";
        ShowPanel(tile);

        // Fetch active body id + corps list
        StartCoroutine(RefreshCorpList());
    }

    // =========================================================
    // UI: build
    // =========================================================

    private void BuildCanvas()
    {
        // Canvas overlay (Screen Space)
        GameObject canvasGo = new GameObject("ClaimTileMenuCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel principal
        _panel = new GameObject("ClaimTilePanel");
        _panel.transform.SetParent(_canvas.transform, false);
        var panelRect = _panel.AddComponent<RectTransform>();
        panelRect.sizeDelta      = new Vector2(340, 220);
        panelRect.anchorMin      = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax      = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        var bg = _panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.12f, 0.94f);

        var vLayout = _panel.AddComponent<VerticalLayoutGroup>();
        vLayout.padding           = new RectOffset(12, 12, 10, 10);
        vLayout.spacing           = 8;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = false;
        vLayout.childForceExpandWidth  = true;
        vLayout.childForceExpandHeight = false;

        // Titre
        _titleLabel  = MakeLabel(_panel, "— Tuile —", 16, true);
        _titleLabel.GetComponent<LayoutElement>().preferredHeight = 22;

        // Dropdown corps
        GameObject ddGo = new GameObject("CorpDropdown", typeof(RectTransform));
        ddGo.transform.SetParent(_panel.transform, false);
        var ddLayout = ddGo.AddComponent<LayoutElement>();
        ddLayout.preferredHeight = 30;
        // Placeholder image requis pour TMP_Dropdown — on crée un child Template minimal
        _corpDropdown = ddGo.AddComponent<TMP_Dropdown>();
        BuildMinimalDropdown(_corpDropdown);

        // Status
        _statusLabel = MakeLabel(_panel, "", 12, false);
        _statusLabel.color = new Color(0.6f, 1f, 0.6f);
        _statusLabel.GetComponent<LayoutElement>().preferredHeight = 18;

        // Boutons
        GameObject btnRow = new GameObject("BtnRow", typeof(RectTransform));
        btnRow.transform.SetParent(_panel.transform, false);
        btnRow.AddComponent<HorizontalLayoutGroup>().spacing = 8;
        var rowLayout = btnRow.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 32;

        _claimBtn   = MakeButton(btnRow, "Claim",   new Color(0.2f, 0.7f, 0.3f));
        _unclaimBtn = MakeButton(btnRow, "Unclaim", new Color(0.7f, 0.3f, 0.2f));
        _closeBtn   = MakeButton(btnRow, "×",       new Color(0.3f, 0.3f, 0.35f));

        _claimBtn.onClick.AddListener(OnClaimClicked);
        _unclaimBtn.onClick.AddListener(OnUnclaimClicked);
        _closeBtn.onClick.AddListener(HidePanel);
    }

    private TextMeshProUGUI MakeLabel(GameObject parent, string text, float size, bool bold)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>();
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = text;
        tmp.fontSize = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.color    = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    private Button MakeButton(GameObject parent, string label, Color bg)
    {
        GameObject go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().flexibleWidth = 1;

        var img = go.AddComponent<Image>();
        img.color = bg;

        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = bg * 1.3f;
        colors.pressedColor     = bg * 0.7f;
        btn.colors = colors;

        GameObject txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        var rt = txtGo.GetComponent<RectTransform>();
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;

        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 14;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        return btn;
    }

    /// <summary>
    /// TMP_Dropdown nécessite un Template child pour fonctionner.
    /// On construit le minimum fonctionnel programmatiquement.
    /// </summary>
    private void BuildMinimalDropdown(TMP_Dropdown dd)
    {
        // Background du dropdown header
        var ddRect = dd.GetComponent<RectTransform>();
        ddRect.sizeDelta = new Vector2(0, 30);

        var bgImg = dd.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0.15f, 0.15f, 0.22f);

        // Label
        GameObject lblGo = new GameObject("Label", typeof(RectTransform));
        lblGo.transform.SetParent(dd.transform, false);
        var lblRect = lblGo.GetComponent<RectTransform>();
        lblRect.anchorMin = new Vector2(0, 0);
        lblRect.anchorMax = new Vector2(1, 1);
        lblRect.offsetMin = new Vector2(8, 0);
        lblRect.offsetMax = new Vector2(-8, 0);
        var lbl = lblGo.AddComponent<TextMeshProUGUI>();
        lbl.fontSize  = 13;
        lbl.color     = Color.white;
        lbl.alignment = TextAlignmentOptions.MidpointLeft;
        dd.captionText = lbl;

        // Arrow (optionnel mais propre)
        GameObject arrowGo = new GameObject("Arrow", typeof(RectTransform));
        arrowGo.transform.SetParent(dd.transform, false);
        var arrowRect = arrowGo.GetComponent<RectTransform>();
        arrowRect.anchorMin        = new Vector2(1, 0.5f);
        arrowRect.anchorMax        = new Vector2(1, 0.5f);
        arrowRect.anchoredPosition = new Vector2(-15, 0);
        arrowRect.sizeDelta        = new Vector2(20, 20);
        var arrowTxt = arrowGo.AddComponent<TextMeshProUGUI>();
        arrowTxt.text      = "▼";
        arrowTxt.fontSize  = 10;
        arrowTxt.color     = Color.white;
        arrowTxt.alignment = TextAlignmentOptions.Center;

        // Template (requis par TMP_Dropdown mais on le cache)
        GameObject templateGo = new GameObject("Template", typeof(RectTransform));
        templateGo.transform.SetParent(dd.transform, false);
        templateGo.SetActive(false);
        var tmplRect = templateGo.GetComponent<RectTransform>();
        tmplRect.anchorMin        = new Vector2(0, 0);
        tmplRect.anchorMax        = new Vector2(1, 0);
        tmplRect.pivot            = new Vector2(0.5f, 1);
        tmplRect.anchoredPosition = new Vector2(0, 2);
        tmplRect.sizeDelta        = new Vector2(0, 150);
        templateGo.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f);
        var templateScroll = templateGo.AddComponent<ScrollRect>();

        // Viewport inside template
        GameObject vpGo = new GameObject("Viewport", typeof(RectTransform));
        vpGo.transform.SetParent(templateGo.transform, false);
        var vpRect = vpGo.GetComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;
        vpGo.AddComponent<Image>().color = Color.clear;
        var mask = vpGo.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        templateScroll.viewport = vpRect;

        // Content
        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(vpGo.transform, false);
        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin        = new Vector2(0, 1);
        contentRect.anchorMax        = new Vector2(1, 1);
        contentRect.pivot            = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta        = new Vector2(0, 28);
        templateScroll.content       = contentRect;

        // Item prototype inside Content
        GameObject itemGo = new GameObject("Item", typeof(RectTransform));
        itemGo.transform.SetParent(contentGo.transform, false);
        var itemRect = itemGo.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 28);
        var itemToggle = itemGo.AddComponent<Toggle>();
        itemGo.AddComponent<Image>().color = Color.clear;

        GameObject itemLblGo = new GameObject("Item Label", typeof(RectTransform));
        itemLblGo.transform.SetParent(itemGo.transform, false);
        var itemLblRect = itemLblGo.GetComponent<RectTransform>();
        itemLblRect.anchorMin = new Vector2(0, 0);
        itemLblRect.anchorMax = new Vector2(1, 1);
        itemLblRect.offsetMin = new Vector2(8, 0);
        itemLblRect.offsetMax = new Vector2(0, 0);
        var itemLbl = itemLblGo.AddComponent<TextMeshProUGUI>();
        itemLbl.fontSize  = 13;
        itemLbl.color     = Color.white;
        itemLbl.alignment = TextAlignmentOptions.MidpointLeft;
        itemToggle.targetGraphic = itemGo.GetComponent<Image>();
        itemToggle.graphic       = itemLbl;

        dd.itemText = itemLbl;
        dd.template = tmplRect;
    }

    // =========================================================
    // Show / Hide
    // =========================================================

    private void ShowPanel(GoldbergTileState tile)
    {
        string tileShort = tile.tileId.Length > 12 ? tile.tileId[..12] + "…" : tile.tileId;
        _titleLabel.text = $"<b>Tuile</b>  {tileShort}\n<size=11>{tile.terrainType}</size>";
        _panel.SetActive(true);
        _isOpen = true;
    }

    private void HidePanel()
    {
        _panel.SetActive(false);
        _isOpen = false;
    }

    // =========================================================
    // Backend calls
    // =========================================================

    private IEnumerator RefreshCorpList()
    {
        string url = simulationServerUrl.TrimEnd('/') + "/game/corporations";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                _statusLabel.text = $"Erreur : {req.error}";
                yield break;
            }

            var wrapper = JsonUtility.FromJson<CorpListWrapper>("{\"items\":" + req.downloadHandler.text + "}");
            if (wrapper?.items == null) { _statusLabel.text = "Aucune corporation."; yield break; }

            _corpIds.Clear();
            _corpNames.Clear();
            var ddOptions = new List<TMP_Dropdown.OptionData>();
            foreach (var c in wrapper.items)
            {
                _corpIds.Add(c.id);
                _corpNames.Add(c.name);
                ddOptions.Add(new TMP_Dropdown.OptionData(c.name));
            }
            _corpDropdown.ClearOptions();
            _corpDropdown.AddOptions(ddOptions);
            _corpDropdown.value = 0;
        }

        // Récupérer l'activeBodyId depuis PlanetSphereGoldberg
        if (planetSphere != null)
        {
            // On lit le champ via la propriété publique (déjà exposée comme ActiveBodyId)
            var field = typeof(PlanetSphereGoldberg)
                .GetField("_activeBodyId",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                _activeBodyId = (string)field.GetValue(planetSphere) ?? "";
        }
    }

    private void OnClaimClicked()
    {
        if (_corpIds.Count == 0) { _statusLabel.text = "Aucune corp chargée."; return; }
        int idx = _corpDropdown.value;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoClaim(_corpIds[idx], _currentTile.tileId));
    }

    private void OnUnclaimClicked()
    {
        if (_corpIds.Count == 0) { _statusLabel.text = "Aucune corp chargée."; return; }
        int idx = _corpDropdown.value;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoUnclaim(_corpIds[idx], _currentTile.tileId));
    }

    private IEnumerator DoClaim(string corpId, string tileId)
    {
        _statusLabel.text = "Claim en cours…";
        string url = simulationServerUrl.TrimEnd('/')
            + $"/game/corporations/{corpId}/claim-hex"
            + $"?body_id={UnityWebRequest.EscapeURL(_activeBodyId)}"
            + $"&tile_id={UnityWebRequest.EscapeURL(tileId)}";
        using (UnityWebRequest req = UnityWebRequest.PostWwwForm(url, ""))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                _statusLabel.text = $"✓ Tuile claimée pour {_corpNames[_corpDropdown.value]}";
                if (planetSphere != null) yield return StartCoroutine(RefreshOverlay());
            }
            else
            {
                string detail = req.downloadHandler?.text ?? req.error;
                _statusLabel.text = $"Erreur {req.responseCode}: {detail}";
            }
        }
    }

    private IEnumerator DoUnclaim(string corpId, string tileId)
    {
        _statusLabel.text = "Unclaim en cours…";
        string url = simulationServerUrl.TrimEnd('/')
            + $"/game/corporations/{corpId}/claim-hex"
            + $"?body_id={UnityWebRequest.EscapeURL(_activeBodyId)}"
            + $"&tile_id={UnityWebRequest.EscapeURL(tileId)}";
        UnityWebRequest req = UnityWebRequest.Delete(url);
        req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success)
        {
            _statusLabel.text = $"✓ Tuile libérée.";
            if (planetSphere != null) yield return StartCoroutine(RefreshOverlay());
        }
        else
        {
            string detail = req.downloadHandler?.text ?? req.error;
            _statusLabel.text = $"Erreur {req.responseCode}: {detail}";
        }
        req.Dispose();
    }

    /// <summary>
    /// Déclenche un refresh de l'ownership overlay sur la sphère après claim/unclaim.
    /// </summary>
    private IEnumerator RefreshOverlay()
    {
        planetSphere.RefreshOwnershipOverlay();
        yield break;
    }

    // =========================================================
    // Serialization helpers
    // =========================================================

    [System.Serializable]
    private class CorpListWrapper
    {
        public CorpEntry[] items;
    }

    [System.Serializable]
    private class CorpEntry
    {
        public string id;
        public string name;
    }
}
