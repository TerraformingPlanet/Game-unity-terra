using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Singleton gérant les ticks de simulation du jeu.
///
/// Un tick = unité de temps de jeu. Tous les systèmes de gameplay (terraformation,
/// production de ressources, événements) s'abonnent à OnTick plutôt que d'utiliser
/// Update() ou InvokeRepeating().
///
/// Utilisation :
///   TickManager.Instance.OnTick += MonHandler;
///   TickManager.Instance.Pause();
///   TickManager.Instance.Resume();
/// </summary>
public class TickManager : MonoBehaviour, ITickSource
{
    // =========================================================
    // Singleton
    // =========================================================

    public static TickManager Instance { get; private set; }

    // =========================================================
    // Events
    // =========================================================

    /// <summary>
    /// Déclenché à chaque tick. Le paramètre est le numéro de tick (commence à 1).
    /// </summary>
    public event Action<int> OnTick;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Timing")]
    [Tooltip("Durée d'un tick en secondes (temps réel).")]
    [Min(0.1f)]
    [SerializeField] private float tickInterval = 5f;

    [Tooltip("Lancer le tick automatiquement au démarrage.")]
    [SerializeField] private bool autoStart = true;

    // =========================================================
    // Runtime
    // =========================================================

    private int  _tickCount;
    private bool _running;
    private Coroutine _tickCoroutine;

    // =========================================================
    // Propriété publique
    // =========================================================

    /// <summary>Numéro du dernier tick déclenché.</summary>
    public int TickCount => _tickCount;

    /// <summary>Le TickManager est-il en cours d'exécution ?</summary>
    public bool IsRunning => _running;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Si le client WebSocket est présent, c'est lui qui pilote les ticks (Phase 10)
        if (SimulationWebSocketClient.Instance != null)
        {
            SimulationWebSocketClient.OnServerTickAdvanced += SyncFromServerTick;
            Debug.Log("[TickManager] Mode serveur — ticks pilotés par WebSocket.");
        }
        else if (autoStart)
        {
            Resume();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>Démarre ou reprend les ticks.</summary>
    public void Resume()
    {
        if (_running) return;
        _running = true;
        _tickCoroutine = StartCoroutine(TickLoop());
        Debug.Log("[TickManager] Démarré (interval=" + tickInterval + "s)");
    }

    /// <summary>Met les ticks en pause.</summary>
    public void Pause()
    {
        if (!_running) return;
        _running = false;
        if (_tickCoroutine != null)
        {
            StopCoroutine(_tickCoroutine);
            _tickCoroutine = null;
        }
        Debug.Log("[TickManager] En pause.");
    }

    /// <summary>Change l'intervalle de tick sans interrompre le cycle en cours.</summary>
    public void SetInterval(float seconds)
    {
        tickInterval = Mathf.Max(0.1f, seconds);
    }

    /// <summary>
    /// Synchronise le tick local depuis le serveur (Phase 10).
    /// Appelé par SimulationWebSocketClient.OnServerTickAdvanced.
    /// </summary>
    public void SyncFromServerTick(int serverTick)
    {
        _tickCount = serverTick;
        OnTick?.Invoke(_tickCount);
    }

    // =========================================================
    // Boucle interne
    // =========================================================

    private IEnumerator TickLoop()
    {
        while (_running)
        {
            yield return new WaitForSeconds(tickInterval);
            _tickCount++;
            OnTick?.Invoke(_tickCount);
        }
    }
}
