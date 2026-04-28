using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the EventFeed UI component.
/// Handles game events display, actions journal.
/// </summary>
public class EventFeedController : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset eventFeedTemplate;

    private VisualElement _eventFeed;
    private VisualElement _eventFeedList;
    private VisualElement _eventFeedListActions;
    private int _activeEventTab = 0;
    private const int MaxFeedEntries = 8;

    private GameHUDController _gameHUDController;

    public void Initialize(VisualElement root, VisualTreeAsset injectedTemplate = null)
    {
        if (injectedTemplate != null) eventFeedTemplate = injectedTemplate;
        _gameHUDController = GetComponent<GameHUDController>();
        BuildEventFeed(root);
        StartCoroutine(PollEventFeed());
    }

    private void BuildEventFeed(VisualElement root)
    {
        if (eventFeedTemplate != null)
        {
            eventFeedTemplate.CloneTree(root);
            _eventFeed            = root.Q<VisualElement>("event-feed");
            _eventFeedList        = root.Q<VisualElement>("event-feed-list");
            _eventFeedListActions = root.Q<VisualElement>("event-feed-actions");

            var tabEvents  = root.Q<Label>("tab-events");
            var tabActions = root.Q<Label>("tab-actions");
            if (tabEvents  != null) tabEvents .RegisterCallback<ClickEvent>(_ => SetEventTab(0, tabEvents, tabActions));
            if (tabActions != null) tabActions.RegisterCallback<ClickEvent>(_ => SetEventTab(1, tabEvents, tabActions));
        }
        else
        {
            Debug.LogWarning("[EventFeedController] eventFeedTemplate not assigned — fallback procedural.");
            BuildEventFeedProcedural(root);
        }
        if (_eventFeed != null)
            _eventFeed.style.display = DisplayStyle.None;
    }

    private void BuildEventFeedProcedural(VisualElement root)
    {
        _eventFeed = new VisualElement { name = "event-feed" };
        _eventFeed.AddToClassList("event-feed");
        _eventFeed.pickingMode = PickingMode.Position;

        var tabRow = new VisualElement { name = "event-feed-tabs" };
        tabRow.AddToClassList("event-feed__tabs");
        var tabEvents  = new Label { name = "tab-events",  text = "ÉVÉNEMENTS" };
        var tabActions = new Label { name = "tab-actions", text = "ACTIONS" };
        tabEvents.AddToClassList("event-feed__tab");
        tabEvents.AddToClassList("event-feed__tab--active");
        tabActions.AddToClassList("event-feed__tab");
        tabRow.Add(tabEvents);
        tabRow.Add(tabActions);
        _eventFeed.Add(tabRow);

        _eventFeedList = new VisualElement { name = "event-feed-list" };
        _eventFeedList.AddToClassList("event-feed__pane");
        _eventFeedList.AddToClassList("event-feed__list");
        _eventFeed.Add(_eventFeedList);

        _eventFeedListActions = new VisualElement { name = "event-feed-actions" };
        _eventFeedListActions.AddToClassList("event-feed__pane");
        _eventFeedListActions.AddToClassList("event-feed__list");
        _eventFeedListActions.style.display = DisplayStyle.None;
        _eventFeed.Add(_eventFeedListActions);

        tabEvents .RegisterCallback<ClickEvent>(_ => SetEventTab(0, tabEvents, tabActions));
        tabActions.RegisterCallback<ClickEvent>(_ => SetEventTab(1, tabEvents, tabActions));

        _eventFeed.style.position        = Position.Absolute;
        _eventFeed.style.bottom          = new StyleLength(64f);
        _eventFeed.style.left            = new StyleLength(12f);
        _eventFeed.style.width           = new StyleLength(280f);
        _eventFeed.style.flexDirection   = FlexDirection.Column;
        root.Add(_eventFeed);
    }

    private void SetEventTab(int idx, Label tabEvents, Label tabActions)
    {
        _activeEventTab = idx;
        bool showEvents = idx == 0;
        if (_eventFeedList        != null) _eventFeedList.style.display        = showEvents ? DisplayStyle.Flex : DisplayStyle.None;
        if (_eventFeedListActions != null) _eventFeedListActions.style.display = showEvents ? DisplayStyle.None : DisplayStyle.Flex;
        tabEvents .EnableInClassList("event-feed__tab--active",  showEvents);
        tabActions.EnableInClassList("event-feed__tab--active", !showEvents);
    }

    /// <summary>Push a local action entry to the ACTIONS tab (shows immediately).</summary>
    public void PushFeedEntry(string message)
    {
        if (_eventFeedListActions == null) return;
        var row = new Button { text = message };
        row.AddToClassList("event-feed__entry");
        row.AddToClassList("event-feed__entry--local");
        row.RegisterCallback<ClickEvent>(_ => Debug.Log($"[EventFeed] Action: {message}"));
        _eventFeedListActions.Insert(0, row);
        while (_eventFeedListActions.childCount > MaxFeedEntries)
            _eventFeedListActions.RemoveAt(_eventFeedListActions.childCount - 1);
    }

    private IEnumerator PollEventFeed()
    {
        while (true)
        {
            string url = _gameHUDController.GetSimulationServerUrl().TrimEnd('/') + "/game/events?limit=" + MaxFeedEntries;
            string json = null;
            yield return SimHttp.Get(url, _gameHUDController.GetSimulationServerTimeout(), r => json = r);

            if (json != null && _eventFeedList != null)
            {
                EventDto[] events;
                try
                {
                    string wrapped = $"{{\"items\":{json}}}";
                    events = JsonUtility.FromJson<EventDtoList>(wrapped).items;
                }
                catch { events = null; }

                if (events != null && events.Length > 0)
                {
                    _eventFeedList.Clear();
                    foreach (var e in events)
                    {
                        string label = string.IsNullOrEmpty(e.name)
                            ? $"[T{e.tick}] {e.eventType}"
                            : $"[T{e.tick}] {e.name}";
                        var eventId = e.id;
                        var row = new Button { text = label };
                        row.AddToClassList("event-feed__entry");
                        row.RegisterCallback<ClickEvent>(_ =>
                            Debug.Log($"[EventFeed] Event selected: {eventId}"));
                        _eventFeedList.Add(row);
                    }
                }
            }
            yield return new WaitForSeconds(60f);
        }
    }

    public void AddEvent(string message)
    {
        PushFeedEntry(message);
    }

    /// <summary>Inject a server event received via WebSocket immediately into the ÉVÉNEMENTS tab.</summary>
    public void PushServerEvent(GameEventPush evt)
    {
        if (_eventFeedList == null || evt == null) return;
        string label = string.IsNullOrEmpty(evt.message)
            ? $"[T{evt.tickCount}] {evt.type}"
            : $"[T{evt.tickCount}] {evt.message}";
        var row = new Button { text = label };
        row.AddToClassList("event-feed__entry");
        row.RegisterCallback<ClickEvent>(_ => Debug.Log($"[EventFeed] WS Event: {evt.eventId}"));
        _eventFeedList.Insert(0, row);
        while (_eventFeedList.childCount > MaxFeedEntries)
            _eventFeedList.RemoveAt(_eventFeedList.childCount - 1);
    }

    public void SetVisible(bool visible)
    {
        if (_eventFeed != null)
            _eventFeed.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ── DTO Classes ──────────────────────────────────────────────────────

    [System.Serializable]
    private class EventDto
    {
        public string id;
        public string eventType;
        public string name;
        public string description;
        public int tick;
        public string affectedEntityId;
    }

    [System.Serializable]
    private class EventDtoList
    {
        public EventDto[] items;
    }
}