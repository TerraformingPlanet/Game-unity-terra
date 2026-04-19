using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD unifié — remplace ClaimTileMenu + boutons de vue éparpillés.
///
/// Crée son propre Canvas code-driven (aucun asset UI requis).
/// Panels :
///   TopBar         — navigation (Retour, nom planète, Globe/Carte, bouton Debug)
///   LeftPanel      — progression terraformation + stats atmosphériques
///   RightPanel     — inspecteur de tuile + claim/unclaim corporation
///   DebugDrawer    — liste des corporations + lien vers DebugHydrologyPanel (F10)
///
/// Ajouter ce composant sur un GameObject vide dans la scène.
/// Relier viewManager, terraformHUD, planetSphere en Inspector (ou auto-trouvés).
/// </summary>
public class GameHUD : MonoBehaviour
{
    // =========================================================
    // Inspector
    // =========================================================

    [Header("Références")]
    [SerializeField] private ViewManager          viewManager;
    [SerializeField] private TerraformHUD         terraformHUD;
    [SerializeField] private PlanetSphereGoldberg planetSphere;
    [SerializeField] private DebugHydrologyPanel  debugHydrologyPanel;

    [Header("Serveur")]
    [SerializeField] private string simulationServerUrl            = "http://127.0.0.1:8080";
    [SerializeField] private float  simulationServerTimeoutSeconds = 5f;

    // =========================================================
    // Canvas hierarchy
    // =========================================================

    private Canvas _canvas;

    // TopBar
    private GameObject      _topBar;
    private Button          _backBtn;
    private TextMeshProUGUI _planetLabel;
    private Button          _toggleViewBtn;
    private TextMeshProUGUI _toggleViewBtnLabel;
    private Button          _debugBtn;

    // LeftPanel
    private GameObject      _leftPanel;
    private Slider          _progressSlider;
    private TextMeshProUGUI _progressLabel;
    private TextMeshProUGUI _atmoLabel;

    // RightPanel (tile inspector)
    private GameObject      _rightPanel;
    private TextMeshProUGUI _tileHeader;
    private Image           _corpBadge;
    private TextMeshProUGUI _corpOwnerLabel;
    private TMP_Dropdown    _corpDropdown;
    private Button          _claimBtn;
    private Button          _unclaimBtn;
    private TMP_InputField  _corpNameInput;
    private Button          _createCorpBtn;
    private TextMeshProUGUI _tileStatus;

    // Building section (Phase 7.2)
    private TMP_Dropdown    _buildTypeDropdown;
    private Button          _buildBtn;
    private TextMeshProUGUI _buildingListLabel;

    // DebugDrawer
    private GameObject      _debugDrawer;
    private GameObject      _corpListContent;
    private TextMeshProUGUI _debugStatus;
    private bool            _debugOpen;

    // =========================================================
    // State
    // =========================================================

    private GoldbergTileState _currentTile;
    private string            _activeBodyId = "";
    private readonly List<string> _corpIds   = new List<string>();
    private readonly List<string> _corpNames = new List<string>();

    // =========================================================
    // Lifecycle
    // =========================================================

    private void Awake()
    {
        UIEventSystemUtility.EnsureEventSystem();
        BuildCanvas();
    }

    private void Start()
    {
        if (viewManager          == null) viewManager          = FindFirstObjectByType<ViewManager>(FindObjectsInactive.Include);
        if (terraformHUD         == null) terraformHUD         = FindFirstObjectByType<TerraformHUD>(FindObjectsInactive.Include);
        if (planetSphere         == null) planetSphere         = FindFirstObjectByType<PlanetSphereGoldberg>(FindObjectsInactive.Include);
        if (debugHydrologyPanel  == null) debugHydrologyPanel  = FindFirstObjectByType<DebugHydrologyPanel>(FindObjectsInactive.Include);

        ViewManager.OnViewChanged += HandleViewChanged;

        Debug.Log($"[GameHUD] Start — planetSphere={(planetSphere != null ? planetSphere.name : "NULL")} viewManager={(viewManager != null ? viewManager.name : "NULL")}");
        if (planetSphere != null)
            planetSphere.OnH3TileResolved += OnH3TileResolved;
        else
            Debug.LogWarning("[GameHUD] planetSphere NULL → OnH3TileResolved non connecté !");

        if (terraformHUD != null)
        {
            terraformHUD.OnProgressUpdated    += UpdateProgressBar;
            terraformHUD.OnRegionStateChanged += UpdateAtmoStats;
        }

        HandleViewChanged(viewManager != null ? viewManager.CurrentState : ViewManager.ViewState.Galaxy);
    }

    private void OnDestroy()
    {
        ViewManager.OnViewChanged -= HandleViewChanged;

        if (planetSphere != null)
            planetSphere.OnH3TileResolved -= OnH3TileResolved;

        if (terraformHUD != null)
        {
            terraformHUD.OnProgressUpdated    -= UpdateProgressBar;
            terraformHUD.OnRegionStateChanged -= UpdateAtmoStats;
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
            ToggleDebug();
    }

    // =========================================================
    // Event handlers
    // =========================================================

    private void HandleViewChanged(ViewManager.ViewState state)
    {
        bool inPlanet = state == ViewManager.ViewState.Planet;
        bool inGlobe  = inPlanet
                     && viewManager != null
                     && viewManager.CurrentPlanetSubView == ViewManager.PlanetSubView.Globe;

        _planetLabel.text = viewManager?.ActivePlanet?.bodyName ?? "";
        _backBtn.gameObject.SetActive(state != ViewManager.ViewState.Galaxy);
        _toggleViewBtn.gameObject.SetActive(inPlanet);
        _leftPanel.SetActive(inPlanet);

        if (inPlanet && _toggleViewBtnLabel != null)
        {
            bool isGlobe = viewManager?.CurrentPlanetSubView == ViewManager.PlanetSubView.Globe;
            _toggleViewBtnLabel.text = isGlobe ? "Vue Carte" : "Vue Globe";
        }

        if (!inGlobe)
            _rightPanel.SetActive(false);
    }

    private void OnH3TileResolved(GoldbergTileState tile)
    {
        Debug.Log($"[GameHUD] OnH3TileResolved — tileId={tile.tileId} | state={viewManager?.CurrentState}");
        // Guard : on accepte l'event depuis n'importe quel sous-vue planétaire,
        // car ViewManager.OpenRegion peut changer CurrentPlanetSubView avant que
        // le coroutine H3 se termine (~1s async).
        if (viewManager == null
            || viewManager.CurrentState != ViewManager.ViewState.Planet)
            return;

        _currentTile     = tile;
        _tileStatus.text = "";
        RefreshBodyId();
        RefreshTileHeader(tile);
        _rightPanel.SetActive(true);
        StartCoroutine(RefreshCorpListForTile());
    }

    private void UpdateProgressBar(float ratio)
    {
        if (_progressSlider != null) _progressSlider.value = ratio;
        if (_progressLabel  != null) _progressLabel.text   = $"{ratio * 100f:F1}% Terraform.";
    }

    private void UpdateAtmoStats(RegionState region)
    {
        if (!region.isValid || _atmoLabel == null) return;
        AtmosphericState atm = region.atmosphericState;
        _atmoLabel.text = atm.habitabilityScore > 0f
            ? $"O₂ {atm.o2Ratio * 100f:F1}%   CO₂ {atm.co2Ratio * 100f:F3}%\n"
            + $"T° {atm.averageTemperature:F1}°C   {atm.atmosphericPressure:F1} kPa\n"
            + $"Habitabilité {atm.habitabilityScore * 100f:F0}%"
            : "";
    }

    // =========================================================
    // Right panel
    // =========================================================

    private void RefreshTileHeader(GoldbergTileState tile)
    {
        string tileShort = !string.IsNullOrEmpty(tile.tileId) && tile.tileId.Length > 14
            ? tile.tileId[..14] + "…" : tile.tileId;
        _tileHeader.text =
            $"<b>{tile.terrainType}</b>  <size=10>[H3]</size>\n"
            + $"<size=10><color=#aaa>{tileShort}</color></size>";
        _corpOwnerLabel.text = "Non revendiquée";
        _corpBadge.color     = new Color(0f, 0f, 0f, 0f);
    }

    // =========================================================
    // Debug drawer
    // =========================================================

    private void ToggleDebug()
    {
        _debugOpen = !_debugOpen;
        _debugDrawer.SetActive(_debugOpen);
        if (_debugOpen) StartCoroutine(RefreshCorpsForDebug());
    }

    // =========================================================
    // Helper : activeBodyId via reflection
    // =========================================================

    private void RefreshBodyId()
    {
        if (planetSphere == null) return;
        var f = typeof(PlanetSphereGoldberg).GetField("_activeBodyId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) _activeBodyId = (string)f.GetValue(planetSphere) ?? "";
    }

    // =========================================================
    // Corp HTTP — RightPanel list
    // =========================================================

    private IEnumerator RefreshCorpListForTile()
    {
        string url = simulationServerUrl.TrimEnd('/') + "/game/corporations";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                _tileStatus.text = $"<color=red>{req.error}</color>";
                yield break;
            }

            var wrapper = JsonUtility.FromJson<CorpListWrapper>(
                "{\"items\":" + req.downloadHandler.text + "}");
            if (wrapper?.items == null) yield break;

            _corpIds.Clear();
            _corpNames.Clear();
            var opts = new List<TMP_Dropdown.OptionData>();
            foreach (var c in wrapper.items)
            {
                _corpIds.Add(c.id);
                _corpNames.Add(c.name);
                opts.Add(new TMP_Dropdown.OptionData(c.name));
            }
            _corpDropdown.ClearOptions();
            _corpDropdown.AddOptions(opts);
            _corpDropdown.value = 0;

            // Détecte si la tuile courante est déjà claimée
            string ownerCorpId   = null;
            string ownerCorpName = null;
            if (_currentTile.tileId != null)
            {
                foreach (var c in wrapper.items)
                {
                    if (c.claimedTiles == null) continue;
                    foreach (var t in c.claimedTiles)
                    {
                        if (t.tileId == _currentTile.tileId)
                        {
                            ownerCorpId   = c.id;
                            ownerCorpName = c.name;
                            break;
                        }
                    }
                    if (ownerCorpId != null) break;
                }
            }

            if (ownerCorpId != null)
            {
                _corpOwnerLabel.text = ownerCorpName;
                Color col = GoldbergFaceColorizer.CorpColorFromId(ownerCorpId);
                _corpBadge.color     = col;
                // Sélectionner la corpo propriétaire dans le dropdown
                int idx = _corpIds.IndexOf(ownerCorpId);
                if (idx >= 0) _corpDropdown.value = idx;
                // Afficher les bâtiments de la tuile (Phase 7.2)
                yield return RefreshBuildingsForTile(ownerCorpId, _currentTile.tileId);
            }
            else
            {
                _corpOwnerLabel.text = "Non revendiquée";
                _corpBadge.color     = new Color(0f, 0f, 0f, 0f);
                _buildingListLabel.text = "";
            }
        }
    }

    // =========================================================
    // Corp HTTP — DebugDrawer list
    // =========================================================

    private IEnumerator RefreshCorpsForDebug()
    {
        if (_debugStatus != null) _debugStatus.text = "Chargement…";
        string url = simulationServerUrl.TrimEnd('/') + "/game/corporations";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (_debugStatus != null) _debugStatus.text = $"<color=red>{req.error}</color>";
                yield break;
            }

            var wrapper = JsonUtility.FromJson<CorpListWrapper>(
                "{\"items\":" + req.downloadHandler.text + "}");
            if (_debugStatus != null) _debugStatus.text = "";
            BuildCorpListUI(wrapper?.items);
        }
    }

    private void BuildCorpListUI(CorporationData[] corps)
    {
        if (_corpListContent == null) return;
        foreach (Transform child in _corpListContent.transform)
            Destroy(child.gameObject);

        if (corps == null || corps.Length == 0)
        {
            MakeLabel(_corpListContent, "Aucune corporation.", 12, false, 22, new Color(0.6f, 0.6f, 0.6f));
            return;
        }

        foreach (var corp in corps)
        {
            int tileCount = 0;
            if (corp.claimedTiles != null)
            {
                foreach (var t in corp.claimedTiles)
                    if (string.IsNullOrEmpty(_activeBodyId) || t.bodyId == _activeBodyId)
                        tileCount++;
            }

            Color corpColor = GoldbergFaceColorizer.CorpColorFromId(corp.id);

            // Row: badge + info
            GameObject row = new GameObject("CorpRow", typeof(RectTransform));
            row.transform.SetParent(_corpListContent.transform, false);
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing              = 6;
            hl.childControlHeight   = true;
            hl.childControlWidth    = false;
            hl.childForceExpandWidth = false;
            row.AddComponent<LayoutElement>().preferredHeight = 24;

            // Badge carré coloré
            GameObject badge = new GameObject("Badge", typeof(RectTransform));
            badge.transform.SetParent(row.transform, false);
            var badgeLe = badge.AddComponent<LayoutElement>();
            badgeLe.preferredWidth  = 14;
            badgeLe.preferredHeight = 14;
            badge.AddComponent<Image>().color = corpColor;

            string idShort = corp.id != null && corp.id.Length > 8 ? corp.id[..8] + "…" : corp.id ?? "?";
            int localTiles = tileCount;
            string txt = $"<b>{corp.name}</b>  "
                       + $"<size=10><color=#aaa>{localTiles} tuile{(localTiles > 1 ? "s" : "")}  {idShort}</color></size>";
            var lbl = MakeLabel(row, txt, 12, false, 22);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            var lblLe = lbl.gameObject.GetComponent<LayoutElement>()
                     ?? lbl.gameObject.AddComponent<LayoutElement>();
            lblLe.flexibleWidth = 1;
        }

        // Resize content to fit children
        var fitter = _corpListContent.GetComponent<ContentSizeFitter>()
                  ?? _corpListContent.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vl = _corpListContent.GetComponent<VerticalLayoutGroup>()
              ?? _corpListContent.AddComponent<VerticalLayoutGroup>();
        vl.spacing            = 4;
        vl.childControlHeight = false;
        vl.childControlWidth  = true;
        vl.childForceExpandWidth = true;
    }

    // =========================================================
    // Corp HTTP — Claim / Unclaim / Create
    // =========================================================

    private void OnClaimClicked()
    {
        if (_currentTile.tileId == null) { _tileStatus.text = "Aucune tuile sélectionnée."; return; }
        if (_corpIds.Count == 0)         { _tileStatus.text = "Aucune corporation.";         return; }
        int idx = _corpDropdown.value;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoClaim(_corpIds[idx], _currentTile.tileId));
    }

    private IEnumerator DoClaim(string corpId, string tileId)
    {
        _tileStatus.text = "Claim en cours…";
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
                _tileStatus.text = "<color=#8f8>✓ Tuile claimée</color>";
                planetSphere?.RefreshOwnershipOverlay();
                if (_debugOpen) StartCoroutine(RefreshCorpsForDebug());
            }
            else
            {
                _tileStatus.text = $"<color=red>{req.downloadHandler?.text ?? req.error}</color>";
            }
        }
    }

    private void OnUnclaimClicked()
    {
        if (_currentTile.tileId == null) { _tileStatus.text = "Aucune tuile sélectionnée."; return; }
        if (_corpIds.Count == 0)         { _tileStatus.text = "Aucune corporation.";         return; }
        int idx = _corpDropdown.value;
        if (idx < 0 || idx >= _corpIds.Count) return;
        StartCoroutine(DoUnclaim(_corpIds[idx], _currentTile.tileId));
    }

    private IEnumerator DoUnclaim(string corpId, string tileId)
    {
        _tileStatus.text = "Unclaim en cours…";
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
                _tileStatus.text = "<color=#8f8>✓ Tuile libérée</color>";
                planetSphere?.RefreshOwnershipOverlay();
                if (_debugOpen) StartCoroutine(RefreshCorpsForDebug());
            }
            else
            {
                _tileStatus.text = $"<color=red>{req.downloadHandler?.text ?? req.error}</color>";
            }
        }
    }

    private void OnCreateCorpClicked()
    {
        string corpName = _corpNameInput.text.Trim();
        if (string.IsNullOrEmpty(corpName))
        {
            _tileStatus.text = "<color=orange>Entrez un nom de corporation.</color>";
            return;
        }
        StartCoroutine(DoCreateCorp(corpName));
    }

    private void OnBuildClicked()
    {
        if (_currentTile.tileId == null) return;
        if (_corpIds.Count == 0)
        {
            _tileStatus.text = "<color=orange>Sélectionnez une corporation.</color>";
            return;
        }
        int corpIdx    = Mathf.Clamp(_corpDropdown.value, 0, _corpIds.Count - 1);
        string corpId  = _corpIds[corpIdx];
        int buildType  = _buildTypeDropdown.value; // 0=Mine, 1=Farm, 2=EnergyPlant, 3=Research
        StartCoroutine(DoConstructBuilding(corpId, _activeBodyId, _currentTile.tileId, buildType));
    }

    private IEnumerator DoConstructBuilding(string corpId, string bodyId, string tileId, int buildingType)
    {
        _tileStatus.text = "Construction en cours…";
        string url = $"{simulationServerUrl.TrimEnd('/')}/game/corporations/{corpId}/buildings"
                   + $"?body_id={UnityWebRequest.EscapeURL(bodyId)}"
                   + $"&tile_id={UnityWebRequest.EscapeURL(tileId)}"
                   + $"&building_type={buildingType}";
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                _tileStatus.text = "<color=#8f8>✓ Bâtiment construit</color>";
                yield return RefreshBuildingsForTile(corpId, tileId);
            }
            else
            {
                _tileStatus.text = $"<color=red>{req.downloadHandler?.text ?? req.error}</color>";
            }
        }
    }

    private IEnumerator RefreshBuildingsForTile(string corpId, string tileId)
    {
        string url = $"{simulationServerUrl.TrimEnd('/')}/game/corporations/{corpId}/buildings";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(simulationServerTimeoutSeconds));
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var wrapper = JsonUtility.FromJson<BuildingDataArrayWrapper>(
                "{\"items\":" + req.downloadHandler.text + "}");
            if (wrapper?.items == null) { _buildingListLabel.text = ""; yield break; }

            var sb = new StringBuilder();
            foreach (var b in wrapper.items)
            {
                if (b.tileId != tileId) continue;
                string typeName = b.buildingType switch
                {
                    0 => "Mine",
                    1 => "Farm",
                    2 => "Centrale",
                    3 => "Recherche",
                    _ => b.buildingType.ToString(),
                };
                sb.AppendLine($"• {typeName}  (tick {b.ticksActive})");
            }
            _buildingListLabel.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "Aucun bâtiment";
        }
    }

    private IEnumerator DoCreateCorp(string corpName)
    {
        _tileStatus.text = "Création en cours…";
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
                _tileStatus.text    = "<color=#8f8>✓ Corporation créée</color>";
                _corpNameInput.text = "";
                yield return RefreshCorpListForTile();
                if (_debugOpen) StartCoroutine(RefreshCorpsForDebug());
            }
            else
            {
                _tileStatus.text = $"<color=red>{req.downloadHandler?.text ?? req.error}</color>";
            }
        }
    }

    private static string EscapeJson(string s) =>
        s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

    // =========================================================
    // Canvas construction
    // =========================================================

    private void BuildCanvas()
    {
        GameObject canvasGo = new GameObject("GameHUDCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        BuildTopBar();
        BuildLeftPanel();
        BuildRightPanel();
        BuildDebugDrawer();
    }

    // ── TopBar ───────────────────────────────────────────────────────────────

    private void BuildTopBar()
    {
        _topBar = new GameObject("TopBar", typeof(RectTransform));
        _topBar.transform.SetParent(_canvas.transform, false);
        var rt = _topBar.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0, 1);
        rt.anchorMax        = new Vector2(1, 1);
        rt.pivot            = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0, 44);

        _topBar.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.92f);

        var hl = _topBar.AddComponent<HorizontalLayoutGroup>();
        hl.padding              = new RectOffset(10, 10, 6, 6);
        hl.spacing              = 10;
        hl.childControlHeight   = true;
        hl.childControlWidth    = false;
        hl.childForceExpandWidth = false;
        hl.childAlignment       = TextAnchor.MiddleLeft;

        // ← Retour
        _backBtn = MakeTopBarButton("← Retour", new Color(0.22f, 0.22f, 0.28f), 100);
        _backBtn.onClick.AddListener(() => viewManager?.GoBackOneLevel());

        // Label planète (flexible, centré)
        GameObject lblGo = new GameObject("PlanetLabel", typeof(RectTransform));
        lblGo.transform.SetParent(_topBar.transform, false);
        var le = lblGo.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        _planetLabel = lblGo.AddComponent<TextMeshProUGUI>();
        _planetLabel.fontSize  = 15;
        _planetLabel.fontStyle = FontStyles.Bold;
        _planetLabel.color     = Color.white;
        _planetLabel.alignment = TextAlignmentOptions.Center;

        // Globe / Carte toggle
        _toggleViewBtn = MakeTopBarButton("Vue Carte", new Color(0.22f, 0.38f, 0.62f), 130);
        _toggleViewBtn.onClick.AddListener(() => viewManager?.TogglePlanetView());
        // Récupérer le label enfant pour pouvoir le mettre à jour
        _toggleViewBtnLabel = _toggleViewBtn.GetComponentInChildren<TextMeshProUGUI>();

        // ⚙ Debug
        _debugBtn = MakeTopBarButton("⚙ Debug", new Color(0.28f, 0.28f, 0.35f), 100);
        _debugBtn.onClick.AddListener(ToggleDebug);
    }

    // ── LeftPanel ─────────────────────────────────────────────────────────────

    private void BuildLeftPanel()
    {
        _leftPanel = new GameObject("LeftPanel", typeof(RectTransform));
        _leftPanel.transform.SetParent(_canvas.transform, false);
        var rt = _leftPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot     = new Vector2(0, 0.5f);
        rt.offsetMin = new Vector2(0, 0);
        rt.offsetMax = new Vector2(220, -44);  // 220px wide, below TopBar

        _leftPanel.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.85f);

        var vl = _leftPanel.AddComponent<VerticalLayoutGroup>();
        vl.padding            = new RectOffset(12, 12, 14, 14);
        vl.spacing            = 10;
        vl.childControlWidth  = true;
        vl.childControlHeight = false;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;

        MakeLabel(_leftPanel, "TERRAFORMATION", 11, true, 18, new Color(0.7f, 0.7f, 0.7f));

        // Progress slider
        GameObject sliderGo = new GameObject("ProgressSlider", typeof(RectTransform));
        sliderGo.transform.SetParent(_leftPanel.transform, false);
        sliderGo.AddComponent<LayoutElement>().preferredHeight = 12;
        _progressSlider = sliderGo.AddComponent<Slider>();
        _progressSlider.minValue = 0; _progressSlider.maxValue = 1;
        _progressSlider.interactable = false;
        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(sliderGo.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        var bgImg = bg.AddComponent<Image>(); bgImg.color = new Color(0.25f, 0.25f, 0.28f);
        var fill = new GameObject("Fill Area", typeof(RectTransform));
        fill.transform.SetParent(sliderGo.transform, false);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        fillRt.sizeDelta = Vector2.zero;
        var fillImg = new GameObject("Fill", typeof(RectTransform));
        fillImg.transform.SetParent(fill.transform, false);
        var fi = fillImg.GetComponent<RectTransform>();
        fi.anchorMin = Vector2.zero; fi.anchorMax = Vector2.one;
        fi.sizeDelta = Vector2.zero;
        var fImg = fillImg.AddComponent<Image>(); fImg.color = new Color(0.25f, 0.72f, 0.35f);
        _progressSlider.fillRect = fi;
        _progressSlider.targetGraphic = bgImg;

        _progressLabel = MakeLabel(_leftPanel, "—% Terraform.", 13, true, 22);

        MakeSeparator(_leftPanel);

        _atmoLabel = MakeLabel(_leftPanel, "", 12, false, 58, new Color(0.85f, 0.85f, 0.85f));
        _atmoLabel.alignment = TextAlignmentOptions.TopLeft;
        _atmoLabel.lineSpacing = 2;

        _leftPanel.SetActive(false);
    }

    // ── RightPanel ───────────────────────────────────────────────────────────

    private void BuildRightPanel()
    {
        // Outer panel — stretches full height below TopBar, fixed width 300
        _rightPanel = new GameObject("RightPanel", typeof(RectTransform));
        _rightPanel.transform.SetParent(_canvas.transform, false);
        var rt = _rightPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(1, 1);
        rt.offsetMin = new Vector2(-300, 0);
        rt.offsetMax = new Vector2(0, -44);  // juste sous le TopBar

        _rightPanel.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.10f, 0.95f);

        // ScrollRect — permet de scroller si le contenu dépasse la hauteur d'écran
        var scrollRect = _rightPanel.AddComponent<ScrollRect>();
        scrollRect.horizontal        = false;
        scrollRect.vertical          = true;
        scrollRect.scrollSensitivity = 30f;

        // Viewport
        GameObject vpGo = new GameObject("Viewport", typeof(RectTransform));
        vpGo.transform.SetParent(_rightPanel.transform, false);
        var vpRect = vpGo.GetComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;
        vpGo.AddComponent<Image>().color = Color.clear;
        vpGo.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = vpRect;

        // Content — VerticalLayoutGroup + ContentSizeFitter
        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(vpGo.transform, false);
        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin        = new Vector2(0, 1);
        contentRect.anchorMax        = new Vector2(1, 1);
        contentRect.pivot            = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta        = Vector2.zero;
        scrollRect.content = contentRect;

        var vl = contentGo.AddComponent<VerticalLayoutGroup>();
        vl.padding            = new RectOffset(14, 14, 12, 12);
        vl.spacing            = 8;
        vl.childControlWidth  = true;
        vl.childControlHeight = false;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Alias local : tous les enfants vont dans contentGo
        GameObject rp = contentGo;

        // Header tuile + bouton fermer
        GameObject headerRow = new GameObject("HeaderRow", typeof(RectTransform));
        headerRow.transform.SetParent(rp.transform, false);
        var hrl = headerRow.AddComponent<HorizontalLayoutGroup>();
        hrl.spacing              = 6;
        hrl.childControlHeight   = true;
        hrl.childControlWidth    = false;
        hrl.childForceExpandWidth = false;
        headerRow.AddComponent<LayoutElement>().preferredHeight = 46;

        GameObject tileHdrGo = new GameObject("TileHeader", typeof(RectTransform));
        tileHdrGo.transform.SetParent(headerRow.transform, false);
        tileHdrGo.AddComponent<LayoutElement>().flexibleWidth = 1;
        _tileHeader = tileHdrGo.AddComponent<TextMeshProUGUI>();
        _tileHeader.fontSize  = 13;
        _tileHeader.fontStyle = FontStyles.Bold;
        _tileHeader.color     = Color.white;
        _tileHeader.alignment = TextAlignmentOptions.TopLeft;

        Button closeBtn = MakeSmallButton(headerRow, "✕", new Color(0.40f, 0.18f, 0.18f), 28);
        closeBtn.onClick.AddListener(() => _rightPanel.SetActive(false));

        MakeSeparator(rp);

        // Corp owner badge + label
        MakeLabel(rp, "Propriétaire", 10, false, 14, new Color(0.55f, 0.55f, 0.55f));
        GameObject ownerRow = new GameObject("OwnerRow", typeof(RectTransform));
        ownerRow.transform.SetParent(rp.transform, false);
        var orl = ownerRow.AddComponent<HorizontalLayoutGroup>();
        orl.spacing              = 8;
        orl.childControlHeight   = true;
        orl.childControlWidth    = false;
        orl.childForceExpandWidth = false;
        ownerRow.AddComponent<LayoutElement>().preferredHeight = 22;

        GameObject badgeGo = new GameObject("CorpBadge", typeof(RectTransform));
        badgeGo.transform.SetParent(ownerRow.transform, false);
        var badgeLe = badgeGo.AddComponent<LayoutElement>();
        badgeLe.preferredWidth = 18; badgeLe.preferredHeight = 18;
        _corpBadge = badgeGo.AddComponent<Image>();
        _corpBadge.color = new Color(0f, 0f, 0f, 0f);

        GameObject ownerLblGo = new GameObject("OwnerLabel", typeof(RectTransform));
        ownerLblGo.transform.SetParent(ownerRow.transform, false);
        ownerLblGo.AddComponent<LayoutElement>().flexibleWidth = 1;
        _corpOwnerLabel = ownerLblGo.AddComponent<TextMeshProUGUI>();
        _corpOwnerLabel.fontSize  = 13;
        _corpOwnerLabel.color     = Color.white;
        _corpOwnerLabel.alignment = TextAlignmentOptions.MidlineLeft;

        MakeSeparator(rp);

        // Corp dropdown
        MakeLabel(rp, "Corporation", 10, false, 14, new Color(0.55f, 0.55f, 0.55f));
        GameObject ddGo = new GameObject("CorpDropdown", typeof(RectTransform));
        ddGo.transform.SetParent(rp.transform, false);
        ddGo.AddComponent<LayoutElement>().preferredHeight = 32;
        _corpDropdown = ddGo.AddComponent<TMP_Dropdown>();
        BuildMinimalDropdown(_corpDropdown, ddGo);

        // Claim / Unclaim buttons
        GameObject claimRow = new GameObject("ClaimRow", typeof(RectTransform));
        claimRow.transform.SetParent(rp.transform, false);
        var clrl = claimRow.AddComponent<HorizontalLayoutGroup>();
        clrl.spacing              = 8;
        clrl.childControlWidth    = true;
        clrl.childForceExpandWidth = true;
        claimRow.AddComponent<LayoutElement>().preferredHeight = 34;

        _claimBtn = MakeButton(claimRow, "Claim",   new Color(0.18f, 0.62f, 0.28f));
        _claimBtn.onClick.AddListener(OnClaimClicked);
        _unclaimBtn = MakeButton(claimRow, "Unclaim", new Color(0.70f, 0.22f, 0.16f));
        _unclaimBtn.onClick.AddListener(OnUnclaimClicked);

        MakeSeparator(rp);

        // Create corp
        MakeLabel(rp, "Nouvelle corporation", 10, false, 14, new Color(0.55f, 0.55f, 0.55f));
        GameObject inputGo = new GameObject("CorpNameInput", typeof(RectTransform));
        inputGo.transform.SetParent(rp.transform, false);
        inputGo.AddComponent<LayoutElement>().preferredHeight = 32;
        _corpNameInput = BuildInputField(inputGo, "Nom de la corporation…");

        GameObject createRow = new GameObject("CreateRow", typeof(RectTransform));
        createRow.transform.SetParent(rp.transform, false);
        createRow.AddComponent<HorizontalLayoutGroup>();
        createRow.AddComponent<LayoutElement>().preferredHeight = 34;
        _createCorpBtn = MakeButton(createRow, "Créer la corporation", new Color(0.18f, 0.40f, 0.70f));
        _createCorpBtn.onClick.AddListener(OnCreateCorpClicked);

        MakeSeparator(rp);

        // ── Section Bâtiments (Phase 7.2) ─────────────────────────
        MakeLabel(rp, "Construire un bâtiment", 10, false, 14, new Color(0.55f, 0.55f, 0.55f));

        // Dropdown type de bâtiment
        GameObject buildTypeGo = new GameObject("BuildTypeDropdown", typeof(RectTransform));
        buildTypeGo.transform.SetParent(rp.transform, false);
        buildTypeGo.AddComponent<LayoutElement>().preferredHeight = 32;
        _buildTypeDropdown = buildTypeGo.AddComponent<TMP_Dropdown>();
        BuildMinimalDropdown(_buildTypeDropdown, buildTypeGo);
        _buildTypeDropdown.ClearOptions();
        _buildTypeDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("Mine"),
            new TMP_Dropdown.OptionData("Farm"),
            new TMP_Dropdown.OptionData("Centrale énergétique"),
            new TMP_Dropdown.OptionData("Recherche"),
        });

        // Bouton Construire
        GameObject buildRow = new GameObject("BuildRow", typeof(RectTransform));
        buildRow.transform.SetParent(rp.transform, false);
        buildRow.AddComponent<HorizontalLayoutGroup>();
        buildRow.AddComponent<LayoutElement>().preferredHeight = 34;
        _buildBtn = MakeButton(buildRow, "Construire", new Color(0.25f, 0.45f, 0.70f));
        _buildBtn.onClick.AddListener(OnBuildClicked);

        // Liste des bâtiments sur cette tuile (lecture seule)
        MakeLabel(rp, "Bâtiments sur cette tuile", 10, false, 14, new Color(0.55f, 0.55f, 0.55f));
        _buildingListLabel = MakeLabel(rp, "", 11, false, 0, Color.white);
        _buildingListLabel.GetComponent<LayoutElement>().preferredHeight = 60;

        // Status label
        _tileStatus = MakeLabel(rp, "", 12, false, 20, new Color(0.5f, 1f, 0.55f));
        _tileStatus.alignment = TextAlignmentOptions.Center;

        _rightPanel.SetActive(false);
    }

    // ── DebugDrawer ──────────────────────────────────────────────────────────

    private void BuildDebugDrawer()
    {
        _debugDrawer = new GameObject("DebugDrawer", typeof(RectTransform));
        _debugDrawer.transform.SetParent(_canvas.transform, false);
        var rt = _debugDrawer.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot     = new Vector2(0.5f, 0);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0, 0);

        _debugDrawer.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.08f, 0.95f);

        var vl = _debugDrawer.AddComponent<VerticalLayoutGroup>();
        vl.padding            = new RectOffset(14, 14, 10, 10);
        vl.spacing            = 8;
        vl.childControlWidth  = true;
        vl.childControlHeight = false;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;

        var fitter = _debugDrawer.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Titre
        MakeLabel(_debugDrawer, "── DEBUG ──", 11, true, 18, new Color(0.5f, 0.5f, 0.7f));

        // Actions row: Debug Projection + Refresh Ownership
        GameObject actionsRow = new GameObject("ActionsRow", typeof(RectTransform));
        actionsRow.transform.SetParent(_debugDrawer.transform, false);
        var arl = actionsRow.AddComponent<HorizontalLayoutGroup>();
        arl.spacing              = 8;
        arl.childControlWidth    = true;
        arl.childForceExpandWidth = true;
        actionsRow.AddComponent<LayoutElement>().preferredHeight = 30;

        Button debugProjBtn = MakeButton(actionsRow, "Debug Projection (F10)", new Color(0.22f, 0.22f, 0.32f));
        debugProjBtn.onClick.AddListener(() => debugHydrologyPanel?.TogglePanel());

        Button refreshOwnershipBtn = MakeButton(actionsRow, "Refresh Ownership", new Color(0.22f, 0.32f, 0.22f));
        refreshOwnershipBtn.onClick.AddListener(() => planetSphere?.RefreshOwnershipOverlay());

        MakeSeparator(_debugDrawer);

        // Corporations header row
        GameObject corpHeader = new GameObject("CorpHeader", typeof(RectTransform));
        corpHeader.transform.SetParent(_debugDrawer.transform, false);
        var chl = corpHeader.AddComponent<HorizontalLayoutGroup>();
        chl.spacing              = 8;
        chl.childControlWidth    = false;
        chl.childForceExpandWidth = false;
        corpHeader.AddComponent<LayoutElement>().preferredHeight = 24;

        var corpTitleLbl = MakeLabel(corpHeader, "── Corporations ──", 11, true, 24, new Color(0.6f, 0.6f, 0.8f));
        corpTitleLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        Button refreshCorpsBtn = MakeSmallButton(corpHeader, "↻ Rafraîchir", new Color(0.22f, 0.30f, 0.45f), 100);
        refreshCorpsBtn.onClick.AddListener(() => StartCoroutine(RefreshCorpsForDebug()));

        // Status label
        _debugStatus = MakeLabel(_debugDrawer, "", 11, false, 16, new Color(0.6f, 0.6f, 0.6f));

        // ScrollView for corps
        GameObject scrollGo = new GameObject("CorpsScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(_debugDrawer.transform, false);
        scrollGo.AddComponent<LayoutElement>().preferredHeight = 180;

        scrollGo.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.10f);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical   = true;

        // Viewport
        GameObject vpGo = new GameObject("Viewport", typeof(RectTransform));
        vpGo.transform.SetParent(scrollGo.transform, false);
        var vpRt = vpGo.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
        vpRt.sizeDelta = Vector2.zero;
        vpGo.AddComponent<Image>().color = Color.clear;
        vpGo.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = vpRt;

        // Content
        _corpListContent = new GameObject("Content", typeof(RectTransform));
        _corpListContent.transform.SetParent(vpGo.transform, false);
        var contentRt = _corpListContent.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot     = new Vector2(0.5f, 1);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta        = new Vector2(0, 0);

        var contentVL = _corpListContent.AddComponent<VerticalLayoutGroup>();
        contentVL.padding            = new RectOffset(8, 8, 6, 6);
        contentVL.spacing            = 4;
        contentVL.childControlWidth  = true;
        contentVL.childControlHeight = false;
        contentVL.childForceExpandWidth = true;
        scroll.content = contentRt;

        var contentFitter = _corpListContent.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _debugDrawer.SetActive(false);
    }

    // =========================================================
    // UI helpers
    // =========================================================

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
        go.AddComponent<LayoutElement>().preferredHeight = 1;
        go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
    }

    private Button MakeButton(GameObject parent, string label, Color bg)
    {
        GameObject go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().flexibleWidth = 1;
        return SetupButton(go, label, bg, 13);
    }

    // Button with fixed width (for TopBar)
    private Button MakeTopBarButton(string label, Color bg, float width)
    {
        GameObject go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(_topBar.transform, false);
        go.AddComponent<LayoutElement>().preferredWidth = width;
        return SetupButton(go, label, bg, 13);
    }

    // Small fixed-width button
    private Button MakeSmallButton(GameObject parent, string label, Color bg, float width)
    {
        GameObject go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().preferredWidth = width;
        return SetupButton(go, label, bg, 11);
    }

    private static Button SetupButton(GameObject go, string label, Color bg, float fontSize)
    {
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn    = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = bg * 1.3f;
        colors.pressedColor     = bg * 0.65f;
        btn.colors = colors;

        GameObject txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4, 0); trt.offsetMax = new Vector2(-4, 0);
        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text            = label;
        tmp.fontSize        = fontSize;
        tmp.fontStyle       = FontStyles.Bold;
        tmp.color           = Color.white;
        tmp.alignment       = TextAlignmentOptions.Center;
        tmp.overflowMode    = TextOverflowModes.Ellipsis;
        return btn;
    }

    private static TMP_InputField BuildInputField(GameObject go, string placeholder)
    {
        go.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.20f);
        var field = go.AddComponent<TMP_InputField>();

        GameObject taGo = new GameObject("Text Area", typeof(RectTransform));
        taGo.transform.SetParent(go.transform, false);
        var taRt = taGo.GetComponent<RectTransform>();
        taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
        taRt.offsetMin = new Vector2(8, 2); taRt.offsetMax = new Vector2(-8, -2);
        taGo.AddComponent<RectMask2D>();

        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(taGo.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;
        var textTmp = textGo.AddComponent<TextMeshProUGUI>();
        textTmp.fontSize = 13; textTmp.color = Color.white;
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject phGo = new GameObject("Placeholder", typeof(RectTransform));
        phGo.transform.SetParent(taGo.transform, false);
        var phRt = phGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.sizeDelta = Vector2.zero;
        var phTmp = phGo.AddComponent<TextMeshProUGUI>();
        phTmp.text = placeholder; phTmp.fontSize = 13;
        phTmp.color = new Color(0.5f, 0.5f, 0.5f);
        phTmp.alignment = TextAlignmentOptions.MidlineLeft;
        phTmp.fontStyle = FontStyles.Italic;

        field.textViewport   = taRt;
        field.textComponent  = textTmp;
        field.placeholder    = phTmp;
        field.characterLimit = 40;
        return field;
    }

    private static void BuildMinimalDropdown(TMP_Dropdown dd, GameObject ddGo)
    {
        ddGo.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.22f);

        // Caption
        GameObject lblGo = new GameObject("Label", typeof(RectTransform));
        lblGo.transform.SetParent(ddGo.transform, false);
        var lblRt = lblGo.GetComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0, 0); lblRt.anchorMax = new Vector2(1, 1);
        lblRt.offsetMin = new Vector2(8, 0); lblRt.offsetMax = new Vector2(-28, 0);
        var lbl = lblGo.AddComponent<TextMeshProUGUI>();
        lbl.fontSize = 13; lbl.color = Color.white;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        dd.captionText = lbl;

        // Arrow
        GameObject arGo = new GameObject("Arrow", typeof(RectTransform));
        arGo.transform.SetParent(ddGo.transform, false);
        var arRt = arGo.GetComponent<RectTransform>();
        arRt.anchorMin = new Vector2(1, 0.5f); arRt.anchorMax = new Vector2(1, 0.5f);
        arRt.anchoredPosition = new Vector2(-14, 0); arRt.sizeDelta = new Vector2(20, 20);
        var arTxt = arGo.AddComponent<TextMeshProUGUI>();
        arTxt.text = "▼"; arTxt.fontSize = 10; arTxt.color = Color.white;
        arTxt.alignment = TextAlignmentOptions.Center;

        // Template
        GameObject tmplGo = new GameObject("Template", typeof(RectTransform));
        tmplGo.transform.SetParent(ddGo.transform, false);
        tmplGo.SetActive(false);
        var tmplRt = tmplGo.GetComponent<RectTransform>();
        tmplRt.anchorMin = new Vector2(0, 0); tmplRt.anchorMax = new Vector2(1, 0);
        tmplRt.pivot     = new Vector2(0.5f, 1);
        tmplRt.anchoredPosition = new Vector2(0, 2);
        tmplRt.sizeDelta = new Vector2(0, 150);
        tmplGo.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.16f);
        var scroll = tmplGo.AddComponent<ScrollRect>();

        // Viewport
        GameObject vpGo = new GameObject("Viewport", typeof(RectTransform));
        vpGo.transform.SetParent(tmplGo.transform, false);
        var vpRt = vpGo.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
        vpRt.sizeDelta = Vector2.zero;
        vpGo.AddComponent<Image>().color = Color.clear;
        vpGo.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = vpRt;

        // Content
        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(vpGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1);
        contentRt.sizeDelta = new Vector2(0, 28);
        scroll.content = contentRt;

        // Item template
        GameObject itemGo = new GameObject("Item", typeof(RectTransform));
        itemGo.transform.SetParent(contentGo.transform, false);
        var itemRt = itemGo.GetComponent<RectTransform>();
        itemRt.anchorMin = new Vector2(0, 0.5f); itemRt.anchorMax = new Vector2(1, 0.5f);
        itemRt.sizeDelta = new Vector2(0, 28);
        var toggle = itemGo.AddComponent<Toggle>();
        var itemBg = itemGo.AddComponent<Image>(); itemBg.color = Color.clear;

        GameObject itemLblGo = new GameObject("Item Label", typeof(RectTransform));
        itemLblGo.transform.SetParent(itemGo.transform, false);
        var ilRt = itemLblGo.GetComponent<RectTransform>();
        ilRt.anchorMin = Vector2.zero; ilRt.anchorMax = Vector2.one;
        ilRt.offsetMin = new Vector2(8, 0); ilRt.offsetMax = Vector2.zero;
        var itemLbl = itemLblGo.AddComponent<TextMeshProUGUI>();
        itemLbl.fontSize = 13; itemLbl.color = Color.white;
        itemLbl.alignment = TextAlignmentOptions.MidlineLeft;

        toggle.targetGraphic = itemBg;
        toggle.graphic       = itemLbl;
        dd.itemText          = itemLbl;
        dd.template          = tmplRt;
    }

    // =========================================================
    // Internal serializable wrappers (same as ClaimTileMenu)
    // =========================================================

    [Serializable] private class CorpListWrapper { public CorporationData[] items; }
    [Serializable] private class BuildingDataArrayWrapper { public BuildingDataItem[] items; }
    [Serializable] private class BuildingDataItem
    {
        public string id;
        public int    buildingType;
        public string tileId;
        public string bodyId;
        public string corpId;
        public float  workerRatio;
        public int    ticksActive;
    }
}
