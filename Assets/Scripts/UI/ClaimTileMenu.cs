using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Menu debug — apparaît au clic sur une tuile Globe.
/// Permet de claim/unclaim une tuile et de créer une corporation.
/// Canvas construit dynamiquement, aucun asset UI requis.
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
    [SerializeField] private string simulationServerUrl            = "http://127.0.0.1:8080";
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
    private TMP_InputField  _corpNameInput;
    private Button          _createCorpBtn;

    private GoldbergTileState _currentTile;

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
        if (viewManager == null)  viewManager  = FindFirstObjectByType<ViewManager>();
        if (planetSphere == null) planetSphere = FindFirstObjectByType<PlanetSphereGoldberg>();
        if (planetSphere != null) planetSphere.OnH3TileResolved += OnTileClicked;
    }

    private void OnDestroy()
    {
        if (planetSphere != null) planetSphere.OnH3TileResolved -= OnTileClicked;
    }

    // =========================================================
    // Event handler
    // =========================================================

    private void OnTileClicked(GoldbergTileState tile)
    {
        if (viewManager != null
            && (viewManager.CurrentState != ViewManager.ViewState.Planet
                || viewManager.CurrentPlanetSubView != ViewManager.PlanetSubView.Globe))
            return;

        _currentTile = tile;
        _statusLabel.text = "";
        ShowPanel(tile);
        StartCoroutine(RefreshCorpList());
    }

    // =========================================================
    // UI : construction
    // =========================================================

    private void BuildCanvas()
    {
        // Canvas overlay
        GameObject canvasGo = new GameObject("ClaimTileMenuCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel principal — 380 × 340
        _panel = new GameObject("ClaimTilePanel");
        _panel.transform.SetParent(_canvas.transform, false);
        var panelRect = _panel.AddComponent<RectTransform>();
        panelRect.sizeDelta        = new Vector2(380, 340);
        panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0, 0);

        _panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.96f);

        var vl = _panel.AddComponent<VerticalLayoutGroup>();
        vl.padding              = new RectOffset(14, 14, 12, 12);
        vl.spacing              = 8;
        vl.childControlWidth    = true;
        vl.childControlHeight   = false;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;

        // ── Titre tuile ──
        _titleLabel = MakeLabel(_panel, "— Tuile —", 15, true, 42);

        // ── Séparateur visuel ──
        MakeSeparator(_panel);

        // ── Section Claim / Unclaim ──
        MakeLabel(_panel, "Corporation", 11, false, 16, new Color(0.7f, 0.7f, 0.7f));

        // Dropdown corps
        GameObject ddGo = new GameObject("CorpDropdown", typeof(RectTransform));
        ddGo.transform.SetParent(_panel.transform, false);
        ddGo.AddComponent<LayoutElement>().preferredHeight = 32;
        _corpDropdown = ddGo.AddComponent<TMP_Dropdown>();
        BuildMinimalDropdown(_corpDropdown);

        // Boutons Claim / Unclaim / ×
        GameObject btnRow = new GameObject("BtnRow", typeof(RectTransform));
        btnRow.transform.SetParent(_panel.transform, false);
        var hl = btnRow.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8;
        hl.childControlWidth    = true;
        hl.childForceExpandWidth = true;
        btnRow.AddComponent<LayoutElement>().preferredHeight = 34;

        _claimBtn   = MakeButton(btnRow, "Claim",   new Color(0.18f, 0.65f, 0.28f));
        _unclaimBtn = MakeButton(btnRow, "Unclaim", new Color(0.72f, 0.25f, 0.18f));
        _closeBtn   = MakeButton(btnRow, "✕",        new Color(0.28f, 0.28f, 0.32f));

        _claimBtn.onClick.AddListener(OnClaimClicked);
        _unclaimBtn.onClick.AddListener(OnUnclaimClicked);
        _closeBtn.onClick.AddListener(HidePanel);

        // ── Séparateur ──
        MakeSeparator(_panel);

        // ── Section Créer corpo ──
        MakeLabel(_panel, "Nouvelle corporation", 11, false, 16, new Color(0.7f, 0.7f, 0.7f));

        // Input champ nom
        GameObject inputGo = new GameObject("CorpNameInput", typeof(RectTransform));
        inputGo.transform.SetParent(_panel.transform, false);
        inputGo.AddComponent<LayoutElement>().preferredHeight = 32;
        _corpNameInput = BuildInputField(inputGo);

        // Bouton Créer
        GameObject createRow = new GameObject("CreateRow", typeof(RectTransform));
        createRow.transform.SetParent(_panel.transform, false);
        createRow.AddComponent<HorizontalLayoutGroup>().spacing = 0;
        createRow.AddComponent<LayoutElement>().preferredHeight = 34;
        _createCorpBtn = MakeButton(createRow, "Créer la corporation", new Color(0.20f, 0.42f, 0.72f));
        _createCorpBtn.onClick.AddListener(OnCreateCorpClicked);

        // ── Status ──
        _statusLabel = MakeLabel(_panel, "", 12, false, 20, new Color(0.5f, 1f, 0.55f));
        _statusLabel.alignment = TextAlignmentOptions.Center;
    }

    // ─── helpers UI ───────────────────────────────────────────

    private TextMeshProUGUI MakeLabel(GameObject parent, string text, float size, bool bold,
                                       float height, Color? color = null)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.color     = color ?? Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    private void MakeSeparator(GameObject parent)
    {
        GameObject go = new GameObject("Sep", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 2;
        go.AddComponent<Image>().color = new Color(1, 1, 1, 0.12f);
    }

    private Button MakeButton(GameObject parent, string label, Color bg)
    {
        GameObject go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().flexibleWidth = 1;

        var img = go.AddComponent<Image>();
        img.color = bg;

        var btn = go.AddComponent<Button>();
        var colors    = btn.colors;
        colors.highlightedColor = bg * 1.25f;
        colors.pressedColor     = bg * 0.70f;
        btn.colors = colors;

        GameObject txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        var rt = txtGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4, 0); rt.offsetMax = new Vector2(-4, 0);

        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 13;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        return btn;
    }

    private TMP_InputField BuildInputField(GameObject go)
    {
        var bgImg = go.AddComponent<Image>();
        bgImg.color = new Color(0.14f, 0.14f, 0.20f);

        var field = go.AddComponent<TMP_InputField>();

        // Text area
        GameObject textAreaGo = new GameObject("Text Area", typeof(RectTransform));
        textAreaGo.transform.SetParent(go.transform, false);
        var taRect = textAreaGo.GetComponent<RectTransform>();
        taRect.anchorMin = Vector2.zero; taRect.anchorMax = Vector2.one;
        taRect.offsetMin = new Vector2(8, 2); taRect.offsetMax = new Vector2(-8, -2);
        textAreaGo.AddComponent<RectMask2D>();

        // Text
        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(textAreaGo.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero; textRect.offsetMax = Vector2.zero;
        var textTmp = textGo.AddComponent<TextMeshProUGUI>();
        textTmp.fontSize  = 13;
        textTmp.color     = Color.white;
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // Placeholder
        GameObject phGo = new GameObject("Placeholder", typeof(RectTransform));
        phGo.transform.SetParent(textAreaGo.transform, false);
        var phRect = phGo.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero; phRect.anchorMax = Vector2.one;
        phRect.offsetMin = Vector2.zero; phRect.offsetMax = Vector2.zero;
        var phTmp = phGo.AddComponent<TextMeshProUGUI>();
        phTmp.text      = "Nom de la corporation…";
        phTmp.fontSize  = 13;
        phTmp.color     = new Color(0.5f, 0.5f, 0.5f);
        phTmp.alignment = TextAlignmentOptions.MidlineLeft;
        phTmp.fontStyle = FontStyles.Italic;

        field.textViewport   = taRect;
        field.textComponent  = textTmp;
        field.placeholder    = phTmp;
        field.characterLimit = 40;

        return field;
    }

    private void BuildMinimalDropdown(TMP_Dropdown dd)
    {
        var ddRect = dd.GetComponent<RectTransform>();
        ddRect.sizeDelta = new Vector2(0, 32);

        dd.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.22f);

        // Caption label
        GameObject lblGo = new GameObject("Label", typeof(RectTransform));
        lblGo.transform.SetParent(dd.transform, false);
        var lblRect = lblGo.GetComponent<RectTransform>();
        lblRect.anchorMin = new Vector2(0, 0); lblRect.anchorMax = new Vector2(1, 1);
        lblRect.offsetMin = new Vector2(8, 0); lblRect.offsetMax = new Vector2(-28, 0);
        var lbl = lblGo.AddComponent<TextMeshProUGUI>();
        lbl.fontSize  = 13; lbl.color = Color.white;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        dd.captionText = lbl;

        // Arrow
        GameObject arrowGo = new GameObject("Arrow", typeof(RectTransform));
        arrowGo.transform.SetParent(dd.transform, false);
        var ar = arrowGo.GetComponent<RectTransform>();
        ar.anchorMin = new Vector2(1, 0.5f); ar.anchorMax = new Vector2(1, 0.5f);
        ar.anchoredPosition = new Vector2(-14, 0); ar.sizeDelta = new Vector2(20, 20);
        var arTxt = arrowGo.AddComponent<TextMeshProUGUI>();
        arTxt.text = "▼"; arTxt.fontSize = 10; arTxt.color = Color.white;
        arTxt.alignment = TextAlignmentOptions.Center;

        // Template
        GameObject tmplGo = new GameObject("Template", typeof(RectTransform));
        tmplGo.transform.SetParent(dd.transform, false);
        tmplGo.SetActive(false);
        var tmplRect = tmplGo.GetComponent<RectTransform>();
        tmplRect.anchorMin = new Vector2(0, 0); tmplRect.anchorMax = new Vector2(1, 0);
        tmplRect.pivot = new Vector2(0.5f, 1);
        tmplRect.anchoredPosition = new Vector2(0, 2);
        tmplRect.sizeDelta = new Vector2(0, 160);
        tmplGo.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f);
        var scroll = tmplGo.AddComponent<ScrollRect>();

        // Viewport
        GameObject vpGo = new GameObject("Viewport", typeof(RectTransform));
        vpGo.transform.SetParent(tmplGo.transform, false);
        var vpRect = vpGo.GetComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero; vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero; vpRect.offsetMax = Vector2.zero;
        vpGo.AddComponent<Image>().color = Color.clear;
        vpGo.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = vpRect;

        // Content
        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(vpGo.transform, false);
        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = new Vector2(0, 30);
        scroll.content = contentRect;

        // Item
        GameObject itemGo = new GameObject("Item", typeof(RectTransform));
        itemGo.transform.SetParent(contentGo.transform, false);
        var itemRect = itemGo.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f); itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 30);
        var toggle = itemGo.AddComponent<Toggle>();
        itemGo.AddComponent<Image>().color = Color.clear;

        GameObject itemLblGo = new GameObject("Item Label", typeof(RectTransform));
        itemLblGo.transform.SetParent(itemGo.transform, false);
        var ilRect = itemLblGo.GetComponent<RectTransform>();
        ilRect.anchorMin = Vector2.zero; ilRect.anchorMax = Vector2.one;
        ilRect.offsetMin = new Vector2(8, 0); ilRect.offsetMax = Vector2.zero;
        var itemLbl = itemLblGo.AddComponent<TextMeshProUGUI>();
        itemLbl.fontSize = 13; itemLbl.color = Color.white;
        itemLbl.alignment = TextAlignmentOptions.MidlineLeft;
        toggle.targetGraphic = itemGo.GetComponent<Image>();
        toggle.graphic       = itemLbl;

        dd.itemText = itemLbl;
        dd.template = tmplRect;
    }

    // =========================================================
    // Show / Hide
    // =========================================================

    private void ShowPanel(GoldbergTileState tile)
    {
        string tileShort = tile.tileId.Length > 14 ? tile.tileId[..14] + "…" : tile.tileId;
        _titleLabel.text = $"<b>Tuile</b>  {tileShort}\n<size=11><color=#aaa>{tile.terrainType}</color></size>";
        _panel.SetActive(true);
    }

    private void HidePanel()
    {
        _panel.SetActive(false);
    }

    // =========================================================
    // Backend — corps
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
                _statusLabel.text = $"<color=red>Erreur : {req.error}</color>";
                yield break;
            }
            var wrapper = JsonUtility.FromJson<CorpListWrapper>("{\"items\":" + req.downloadHandler.text + "}");
            if (wrapper?.items == null) { _statusLabel.text = "Aucune corporation."; yield break; }

            _corpIds.Clear(); _corpNames.Clear();
            var opts = new List<TMP_Dropdown.OptionData>();
            foreach (var c in wrapper.items)
            {
                _corpIds.Add(c.id); _corpNames.Add(c.name);
                opts.Add(new TMP_Dropdown.OptionData(c.name));
            }
            _corpDropdown.ClearOptions();
            _corpDropdown.AddOptions(opts);
            _corpDropdown.value = 0;
        }

        // Récupérer _activeBodyId par réflexion
        if (planetSphere != null)
        {
            var f = typeof(PlanetSphereGoldberg).GetField("_activeBodyId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) _activeBodyId = (string)f.GetValue(planetSphere) ?? "";
        }
    }

    // ─── Claim ────────────────────────────────────────────────

    private void OnClaimClicked()
    {
        if (_corpIds.Count == 0) { _statusLabel.text = "Aucune corp chargée."; return; }
        int idx = _corpDropdown.value;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoClaim(_corpIds[idx], _currentTile.tileId));
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
                _statusLabel.text = $"<color=#8f8>✓ Tuile claimée</color>";
                planetSphere?.RefreshOwnershipOverlay();
            }
            else
            {
                string detail = req.downloadHandler?.text ?? req.error;
                _statusLabel.text = $"<color=red>Erreur {req.responseCode}: {detail}</color>";
            }
        }
    }

    // ─── Unclaim ──────────────────────────────────────────────

    private void OnUnclaimClicked()
    {
        if (_corpIds.Count == 0) { _statusLabel.text = "Aucune corp chargée."; return; }
        int idx = _corpDropdown.value;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoUnclaim(_corpIds[idx], _currentTile.tileId));
    }

    private IEnumerator DoUnclaim(string corpId, string tileId)
    {
        _statusLabel.text = "Unclaim en cours…";
        string url = simulationServerUrl.TrimEnd('/')
            + $"/game/corporations/{corpId}/claim-hex"
            + $"?body_id={UnityWebRequest.EscapeURL(_activeBodyId)}"
            + $"&tile_id={UnityWebRequest.EscapeURL(tileId)}";
        using (UnityWebRequest req = UnityWebRequest.Delete(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                _statusLabel.text = "<color=#8f8>✓ Tuile libérée</color>";
                planetSphere?.RefreshOwnershipOverlay();
            }
            else
            {
                string detail = req.downloadHandler?.text ?? req.error;
                _statusLabel.text = $"<color=red>Erreur {req.responseCode}: {detail}</color>";
            }
        }
    }

    // ─── Créer corpo ──────────────────────────────────────────

    private void OnCreateCorpClicked()
    {
        string corpName = _corpNameInput.text.Trim();
        if (string.IsNullOrEmpty(corpName))
        {
            _statusLabel.text = "<color=orange>Entrez un nom de corporation.</color>";
            return;
        }
        StartCoroutine(DoCreateCorp(corpName));
    }

    private IEnumerator DoCreateCorp(string corpName)
    {
        _statusLabel.text = "Création en cours…";
        string url  = simulationServerUrl.TrimEnd('/') + "/game/corporations";
        string json = $"{{\"name\":\"{EscapeJson(corpName)}\",\"is_ai\":false}}";
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                _statusLabel.text = $"<color=#8f8>✓ « {corpName} » créée !</color>";
                _corpNameInput.text = "";
                yield return StartCoroutine(RefreshCorpList()); // màj dropdown
            }
            else
            {
                string detail = req.downloadHandler?.text ?? req.error;
                _statusLabel.text = $"<color=red>Erreur {req.responseCode}: {detail}</color>";
            }
        }
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // =========================================================
    // Serialization helpers
    // =========================================================

    [System.Serializable] private class CorpListWrapper { public CorpEntry[] items; }
    [System.Serializable] private class CorpEntry       { public string id; public string name; }
}
