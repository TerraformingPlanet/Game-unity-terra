using System;
using System.Collections;
using System.Text;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// Client WebSocket singleton pour les push serveur (Phase 10).
///
/// Se connecte automatiquement après que PlayerSession est authentifié.
/// Dispatch des événements statiques que d'autres systèmes peuvent écouter.
///
/// Abonnement :
///   SimulationWebSocketClient.OnServerTickAdvanced += MyHandler;
///   SimulationWebSocketClient.OnServerGameEvent    += MyEventHandler;
/// </summary>
public class SimulationWebSocketClient : MonoBehaviour
{
    // =========================================================
    // Singleton
    // =========================================================

    public static SimulationWebSocketClient Instance { get; private set; }

    // =========================================================
    // Événements statiques
    // =========================================================

    /// <summary>Le serveur a avancé au tick N.</summary>
    public static event Action<int> OnServerTickAdvanced;

    /// <summary>Un événement gameplay a été reçu du serveur.</summary>
    public static event Action<GameEventPush> OnServerGameEvent;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Config")]
    [SerializeField] private GameConfig config;

    // =========================================================
    // État interne
    // =========================================================

    private WebSocket _ws;
    private bool      _shouldRun = true;
    private int       _reconnectAttempts = 0;

    private const float ReconnectBaseDelay = 2f;
    private const float ReconnectMaxDelay  = 30f;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(ConnectLoop());
    }

    private void Update()
    {
        // NativeWebSocket requiert DispatchMessageQueue() dans Update sur certaines plateformes
#if !UNITY_WEBGL || UNITY_EDITOR
        _ws?.DispatchMessageQueue();
#endif
    }

    private void OnDestroy()
    {
        _shouldRun = false;
        _ws?.Close();
    }

    // =========================================================
    // Connexion / reconnexion
    // =========================================================

    private IEnumerator ConnectLoop()
    {
        while (_shouldRun)
        {
            // Attendre que le joueur soit authentifié
            while (PlayerSession.Instance == null || !PlayerSession.Instance.IsLoggedIn)
                yield return new WaitForSeconds(0.5f);

            string serverUrl = config != null ? config.simulationServerUrl : "http://localhost:8080";
            string url = PlayerSession.Instance.BuildWebSocketUrl(serverUrl);
            Debug.Log($"[WS] Connexion à {url}");

            _ws = new WebSocket(url);

            _ws.OnOpen    += OnWsOpen;
            _ws.OnMessage += OnWsMessage;
            _ws.OnError   += OnWsError;
            _ws.OnClose   += OnWsClose;

            yield return _ws.Connect();

            // Attendre tant que la connexion est active
            while (_ws.State == WebSocketState.Open)
                yield return null;

            float delay = Mathf.Min(ReconnectBaseDelay * Mathf.Pow(2f, _reconnectAttempts), ReconnectMaxDelay);
            _reconnectAttempts++;
            Debug.Log($"[WS] Connexion fermée — reconnexion dans {delay:F0}s (tentative {_reconnectAttempts})");
            yield return new WaitForSeconds(delay);
        }
    }

    // =========================================================
    // Handlers WebSocket
    // =========================================================

    private void OnWsOpen()
    {
        _reconnectAttempts = 0;
        Debug.Log("[WS] Connexion établie.");
    }

    private void OnWsMessage(byte[] data)
    {
        string json = Encoding.UTF8.GetString(data);
        try
        {
            var envelope = JsonUtility.FromJson<_WsEnvelope>(json);
            if (envelope == null) return;

            switch (envelope.type)
            {
                case "tick_advanced":
                    OnServerTickAdvanced?.Invoke(envelope.tick);
                    break;

                case "game_event":
                    if (envelope.data != null)
                        OnServerGameEvent?.Invoke(envelope.data);
                    break;

                case "pong":
                    // silencieux
                    break;

                default:
                    Debug.Log($"[WS] Message inconnu: {envelope.type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Erreur désérialisation: {ex.Message} — {json}");
        }
    }

    private void OnWsError(string error)
    {
        Debug.LogWarning($"[WS] Erreur: {error}");
    }

    private void OnWsClose(WebSocketCloseCode code)
    {
        Debug.Log($"[WS] Fermé avec code: {code}");
    }

    // =========================================================
    // API publique
    // =========================================================

    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

    // =========================================================
    // DTOs internes
    // =========================================================

    [Serializable]
    private class _WsEnvelope
    {
        public string       type;
        public int          tick;
        public GameEventPush data;
    }
}

/// <summary>Données d'un événement gameplay reçu via WebSocket.</summary>
[Serializable]
public class GameEventPush
{
    public string eventId;
    public string type;
    public int    tickCount;
    public string message;
    public string corpId;
}
