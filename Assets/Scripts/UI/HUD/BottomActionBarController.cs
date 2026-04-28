using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the BottomActionBar UI component.
/// Handles tab navigation for Territory, Construction, Market, Contracts, Terraform.
/// </summary>
public class BottomActionBarController : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset bottomActionBarTemplate;

    private VisualElement _bottomBar;
    private Button _tabTerritoire;
    private Button _tabConstruction;
    private Button _tabMarche;
    private Button _tabContrats;
    private Button _tabTerraform;
    private Label _corpStatusLabel;
    private int _activeTabIndex = -1;

    private static readonly string[] _tabNames = { "TERRITOIRE", "CONSTRUCTION", "MARCHÉ", "CONTRATS", "TERRAFORM" };
    private static readonly string[] _tabButtonNames = { "tab-territoire", "tab-construction", "tab-marche", "tab-contrats", "tab-terraform" };
    private static readonly string[] _tabModifiers = { "territoire", "construction", "marche", "contrats", "terraform" };

    public void Initialize(VisualElement root, VisualTreeAsset injectedTemplate = null)
    {
        if (injectedTemplate != null) bottomActionBarTemplate = injectedTemplate;
        BuildBottomActionBar(root);
    }

    private void BuildBottomActionBar(VisualElement root)
    {
        VisualTreeAsset asset = bottomActionBarTemplate
            ?? Resources.Load<VisualTreeAsset>("UI/Templates/BottomActionBar");

        if (asset != null)
        {
            asset.CloneTree(root);
            _bottomBar = root.Q<VisualElement>("bottom-action-bar");
        }

        if (_bottomBar == null)
        {
            _bottomBar = BuildBottomActionBarProcedural();
            root.Add(_bottomBar);
        }

        // Grab element refs
        _corpStatusLabel  = _bottomBar.Q<Label>("label-corp-status");
        _tabTerritoire    = _bottomBar.Q<Button>("tab-territoire");
        _tabConstruction  = _bottomBar.Q<Button>("tab-construction");
        _tabMarche        = _bottomBar.Q<Button>("tab-marche");
        _tabContrats      = _bottomBar.Q<Button>("tab-contrats");
        _tabTerraform     = _bottomBar.Q<Button>("tab-terraform");

        // Wire tab clicks
        Button[] tabs = { _tabTerritoire, _tabConstruction, _tabMarche, _tabContrats, _tabTerraform };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i; // capture
            tabs[i]?.RegisterCallback<ClickEvent>(_ => SetBottomTab(idx));
        }

        // Force critical layout styles inline (CloneTree ne garantit pas l'application
        // des <Style> src dans le UXML quand on clone dans un container existant).
        _bottomBar.style.position         = Position.Absolute;
        _bottomBar.style.bottom           = new StyleLength(0f);
        _bottomBar.style.left             = new StyleLength(0f);
        _bottomBar.style.right            = new StyleLength(0f);
        _bottomBar.style.height           = new StyleLength(52f);
        _bottomBar.style.flexDirection    = FlexDirection.Row;
        _bottomBar.style.alignItems       = Align.Center;
        _bottomBar.style.justifyContent   = Justify.Center;
        _bottomBar.style.backgroundColor  = new StyleColor(new Color(0.031f, 0.031f, 0.055f, 0.92f));
        _bottomBar.style.borderTopWidth   = 1f;
        _bottomBar.style.borderTopColor   = new StyleColor(new Color(1f, 1f, 1f, 0.08f));

        // Start hidden; shown by OnViewChanged when entering Planet view
        _bottomBar.style.display = DisplayStyle.None;
    }

    /// <summary>Activates the tab at <paramref name="idx"/> (0–4), deactivates others.</summary>
    private void SetBottomTab(int idx)
    {
        Button[] tabs    = { _tabTerritoire, _tabConstruction, _tabMarche, _tabContrats, _tabTerraform };
        string[] mods    = _tabModifiers;
        string   active  = "bottom-action-bar__tab--active";

        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == null) continue;
            bool isActive = (i == idx);
            tabs[i].EnableInClassList(active, isActive);
        }

        _activeTabIndex = (_activeTabIndex == idx) ? -1 : idx; // toggle off if already active
        if (_activeTabIndex == -1)
            for (int i = 0; i < tabs.Length; i++)
                tabs[i]?.EnableInClassList(active, false);

        Debug.Log($"[BottomActionBarController] BottomTab → {(idx < _tabNames.Length ? _tabNames[idx] : idx.ToString())}");
    }

    public void SetActiveTab(int tabIndex)
    {
        SetBottomTab(tabIndex);
    }

    /// <summary>Updates the corp status label in the bottom bar (name, credits, tile count).</summary>
    public void SetCorpStatus(string corpName, float credits, int tileCount)
    {
        if (_corpStatusLabel == null) return;
        _corpStatusLabel.text = string.IsNullOrEmpty(corpName)
            ? ""
            : $"{corpName}   {credits:N0} ¢   {tileCount} tuiles";
    }

    public void SetVisible(bool visible)
    {
        if (_bottomBar != null)
            _bottomBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private VisualElement BuildBottomActionBarProcedural()
    {
        var bar = new VisualElement { name = "bottom-action-bar" };
        bar.AddToClassList("bottom-action-bar");

        _corpStatusLabel = new Label { name = "label-corp-status", text = "" };
        _corpStatusLabel.AddToClassList("bottom-action-bar__status");
        _corpStatusLabel.AddToClassList("hud-label--secondary");
        bar.Add(_corpStatusLabel);

        string[] names = _tabNames;
        string[] bnames = _tabButtonNames;
        string[] mods = _tabModifiers;
        Button[] refs = new Button[5];

        for (int i = 0; i < names.Length; i++)
        {
            var btn = new Button { name = bnames[i], text = names[i] };
            btn.AddToClassList("bottom-action-bar__tab");
            btn.AddToClassList($"bottom-action-bar__tab--{mods[i]}");
            bar.Add(btn);
            refs[i] = btn;
        }

        _tabTerritoire   = refs[0];
        _tabConstruction = refs[1];
        _tabMarche       = refs[2];
        _tabContrats     = refs[3];
        _tabTerraform    = refs[4];

        return bar;
    }
}