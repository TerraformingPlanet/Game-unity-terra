using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the DebugDrawer UI component.
/// Handles debug information display and corporation list.
/// </summary>
public class DebugDrawerController : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset debugDrawerTemplate;
    [SerializeField] private StyleSheet debugDrawerStyleSheet;

    private VisualElement _debugDrawer;
    private Label _debugStatus;
    private VisualElement _debugCorpListContainer;
    private bool _visible;

    private GameHUDController _gameHUDController;
    private PlanetSphereGoldberg _planetSphere;
    private DebugHydrologyPanel _debugHydrologyPanel;

    public void Initialize(VisualElement root, VisualTreeAsset injectedTemplate = null, StyleSheet injectedStyleSheet = null)
    {
        if (injectedTemplate    != null) debugDrawerTemplate    = injectedTemplate;
        if (injectedStyleSheet  != null) debugDrawerStyleSheet  = injectedStyleSheet;
        _gameHUDController = GetComponent<GameHUDController>();
        _planetSphere = FindAnyObjectByType<PlanetSphereGoldberg>();
        _debugHydrologyPanel = FindAnyObjectByType<DebugHydrologyPanel>();

        BuildDebugDrawer(root);
    }

    private void BuildDebugDrawer(VisualElement root)
    {
        if (debugDrawerStyleSheet != null)
            root.styleSheets.Add(debugDrawerStyleSheet);
        else
            Debug.LogWarning("[DebugDrawerController] debugDrawerStyleSheet not assigned.");

        if (debugDrawerTemplate != null)
        {
            debugDrawerTemplate.CloneTree(root);
            _debugDrawer            = root.Q<VisualElement>("debug-drawer");
            _debugStatus            = root.Q<Label>("debug-status");
            _debugCorpListContainer = root.Q<VisualElement>("debug-corp-list");

            var btnProj      = root.Q<Button>("btn-debug-projection");
            var btnOwnership = root.Q<Button>("btn-refresh-ownership");
            var btnCorps     = root.Q<Button>("btn-refresh-corps");

            btnProj?.RegisterCallback<ClickEvent>(_      => _debugHydrologyPanel?.TogglePanel());
            btnOwnership?.RegisterCallback<ClickEvent>(_ => _planetSphere?.RefreshOwnershipOverlay());
            btnCorps?.RegisterCallback<ClickEvent>(_     => StartCoroutine(RefreshCorpsForDebug()));
        }
        else
        {
            Debug.LogWarning("[DebugDrawerController] debugDrawerTemplate not assigned — DebugDrawer not available.");
        }

        if (_debugDrawer != null)
            _debugDrawer.style.display = DisplayStyle.None;
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_debugDrawer != null)
            _debugDrawer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        if (visible)
            StartCoroutine(RefreshCorpsForDebug());
    }

    public bool GetVisible() => _visible;

    public void UpdateDebugInfo(string info)
    {
        if (_debugStatus != null) _debugStatus.text = info;
    }

    private IEnumerator RefreshCorpsForDebug()
    {
        if (_debugStatus != null) _debugStatus.text = "Chargement...";
        string url = _gameHUDController.GetSimulationServerUrl().TrimEnd('/') + "/game/corporations";
        string json = null;
        yield return SimHttp.Get(url, _gameHUDController.GetSimulationServerTimeout(),
            r   => json = r,
            err => { if (_debugStatus != null) _debugStatus.text = $"Erreur : {err}"; });
        if (json == null) yield break;

        CorpDto[] corps;
        try
        {
            string wrapped = $"{{\"items\":{json}}}";
            corps = JsonUtility.FromJson<CorpListDto>(wrapped).items;
        }
        catch
        {
            if (_debugStatus != null) _debugStatus.text = "Parse error";
            yield break;
        }

        if (_debugStatus != null) _debugStatus.text = "";
        RebuildDebugCorpList(corps);
    }

    private void RebuildDebugCorpList(CorpDto[] corps)
    {
        if (_debugCorpListContainer == null) return;
        _debugCorpListContainer.Clear();

        if (corps == null || corps.Length == 0)
        {
            var empty = new Label { text = "Aucune corporation." };
            empty.AddToClassList("debug-drawer__status");
            _debugCorpListContainer.Add(empty);
            return;
        }

        foreach (var corp in corps)
        {
            var row = new VisualElement();
            row.AddToClassList("debug-drawer__corp-row");

            // Badge couleur
            var badge = new VisualElement();
            badge.AddToClassList("debug-drawer__corp-badge");
            badge.style.backgroundColor = new StyleColor(new Color(corp.colorR, corp.colorG, corp.colorB, 1f));
            row.Add(badge);

            string idShort = corp.id != null && corp.id.Length > 8 ? corp.id[..8] + "…" : corp.id ?? "?";
            var lbl = new Label { text = $"{corp.name}  {corp.credits:N0} ¢  {idShort}" };
            lbl.AddToClassList("debug-drawer__corp-label");
            row.Add(lbl);

            _debugCorpListContainer.Add(row);
        }
    }

    // ── DTO Classes ──────────────────────────────────────────────────────

    [System.Serializable]
    private class CorpDto
    {
        public string id;
        public string name;
        public float credits;
        public float colorR;
        public float colorG;
        public float colorB;
    }

    [System.Serializable]
    private class CorpListDto
    {
        public CorpDto[] items;
    }
}