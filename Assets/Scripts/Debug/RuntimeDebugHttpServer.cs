using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

/// <summary>
/// Bridge HTTP local minimal au-dessus de RuntimeDebugFacade.
/// Tous les accès Unity sont traités sur le thread principal via Update().
/// </summary>
public class RuntimeDebugHttpServer : MonoBehaviour
{
    [Serializable]
    private struct ActionResult
    {
        public bool success;
        public string message;
        public ViewStateSnapshot state;
    }

    private static RuntimeDebugHttpServer _instance;

    [Header("Server")]
    [SerializeField] private bool startOnAwake = true;
    [SerializeField] private int port = 48621;
    [SerializeField] private bool forceRunInBackground = true;

    private readonly ConcurrentQueue<HttpListenerContext> _pendingContexts = new ConcurrentQueue<HttpListenerContext>();
    private HttpListener _listener;
    private Thread _listenerThread;
    private volatile bool _isRunning;
    private bool _previousRunInBackground;
    private bool _hasSavedRunInBackground;

    public static RuntimeDebugHttpServer Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindAnyObjectByType<RuntimeDebugHttpServer>();

            if (_instance == null)
            {
                GameObject serverObject = new GameObject("RuntimeDebugHttpServer");
                _instance = serverObject.AddComponent<RuntimeDebugHttpServer>();
            }

            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        RuntimeDebugHttpServer _ = Instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (forceRunInBackground)
            EnableRunInBackground();

        if (startOnAwake)
            StartServer();
    }

    private void Update()
    {
        while (_pendingContexts.TryDequeue(out HttpListenerContext context))
            HandleContextOnMainThread(context);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        StopServer();

        if (forceRunInBackground)
            RestoreRunInBackground();
    }

    public bool StartServer()
    {
        if (_isRunning)
            return true;

        if (forceRunInBackground)
            EnableRunInBackground();

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _isRunning = true;

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "RuntimeDebugHttpServer"
            };
            _listenerThread.Start();
            Debug.Log($"[RuntimeDebugHttpServer] Started on http://127.0.0.1:{port}/");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RuntimeDebugHttpServer] Failed to start: {ex.Message}");
            StopServer();
            return false;
        }
    }

    public void StopServer()
    {
        _isRunning = false;

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
        }

        _listener = null;

        try
        {
            if (_listenerThread != null && _listenerThread.IsAlive)
                _listenerThread.Join(250);
        }
        catch
        {
        }

        _listenerThread = null;
    }

    private void EnableRunInBackground()
    {
        if (!_hasSavedRunInBackground)
        {
            _previousRunInBackground = Application.runInBackground;
            _hasSavedRunInBackground = true;
        }

        Application.runInBackground = true;
    }

    private void RestoreRunInBackground()
    {
        if (!_hasSavedRunInBackground)
            return;

        Application.runInBackground = _previousRunInBackground;
        _hasSavedRunInBackground = false;
    }

    private void ListenLoop()
    {
        while (_isRunning && _listener != null)
        {
            try
            {
                HttpListenerContext context = _listener.GetContext();
                _pendingContexts.Enqueue(context);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeDebugHttpServer] Listener loop warning: {ex.Message}");
            }
        }
    }

    private void HandleContextOnMainThread(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url != null ? context.Request.Url.AbsolutePath.ToLowerInvariant() : string.Empty;
            Dictionary<string, string> query = ParseQuery(context.Request.Url != null ? context.Request.Url.Query : string.Empty);
            RuntimeDebugFacade facade = RuntimeDebugFacade.Instance;

            switch (path)
            {
                case "/debug/state":
                    WriteJson(context, JsonUtility.ToJson(facade.GetCurrentViewState(), true));
                    break;

                case "/debug/projection":
                    WriteJson(context, JsonUtility.ToJson(facade.GetProjectionSummary(), true));
                    break;

                case "/debug/projection-state":
                    WriteJson(context, JsonUtility.ToJson(facade.GetProjectionState(), true));
                    break;

                case "/debug/local":
                    WriteJson(context, JsonUtility.ToJson(facade.GetLocalSummary(), true));
                    break;

                case "/debug/region-state":
                    WriteJson(context, JsonUtility.ToJson(facade.GetRegionState(), true));
                    break;

                case "/debug/world":
                    WriteJson(context, JsonUtility.ToJson(facade.GetWorldState(), true));
                    break;

                case "/debug/client":
                    WriteJson(context, JsonUtility.ToJson(facade.GetClientSnapshot(), true));
                    break;

                case "/debug/console":
                    int maxEntries = TryGetInt(query, "maxEntries", 20);
                    LogType minimumSeverity = TryGetLogType(query, "minimumSeverity", LogType.Warning);
                    WriteJson(context, JsonUtility.ToJson(facade.GetRecentConsoleErrors(maxEntries, minimumSeverity), true));
                    break;

                case "/debug/screenshot":
                    string fileName = TryGetString(query, "fileName", string.Empty);
                    int superSize = TryGetInt(query, "superSize", 1);
                    WriteJson(context, JsonUtility.ToJson(facade.CaptureSceneScreenshot(fileName, superSize), true));
                    break;

                case "/debug/launch-preset":
                    HandleLaunchPreset(context, facade, query);
                    break;

                case "/debug/open-region":
                    HandleOpenRegion(context, facade, query);
                    break;

                default:
                    WriteJson(context, "{\"success\":false,\"message\":\"Unknown endpoint\"}", 404);
                    break;
            }
        }
        catch (Exception ex)
        {
            WriteJson(context, $"{{\"success\":false,\"message\":{JsonUtility.ToJson(new StringWrapper { value = ex.Message })}}}", 500);
        }
    }

    private static void HandleLaunchPreset(HttpListenerContext context, RuntimeDebugFacade facade, Dictionary<string, string> query)
    {
        string presetName = TryGetString(query, "preset", string.Empty);
        bool success = false;
        string message;

        if (string.IsNullOrWhiteSpace(presetName))
        {
            message = "Missing preset parameter.";
        }
        else
        {
            success = facade.LaunchPresetByName(presetName);
            message = success ? "Preset launched." : "Preset launch failed.";
        }

        var result = new ActionResult
        {
            success = success,
            message = message,
            state = facade.GetCurrentViewState()
        };

        WriteJson(context, JsonUtility.ToJson(result, true), success ? 200 : 400);
    }

    private static void HandleOpenRegion(HttpListenerContext context, RuntimeDebugFacade facade, Dictionary<string, string> query)
    {
        float latitude = TryGetFloat(query, "lat", 0.5f);
        float longitude = TryGetFloat(query, "lon", 0.5f);
        bool success = facade.OpenRegion(new RegionOpenRequest
        {
            latitude = latitude,
            longitude = longitude
        });

        var result = new ActionResult
        {
            success = success,
            message = success ? "Region opened." : "Region open failed.",
            state = facade.GetCurrentViewState()
        };

        WriteJson(context, JsonUtility.ToJson(result, true), success ? 200 : 400);
    }

    private static void WriteJson(HttpListenerContext context, string json, int statusCode = 200)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.ContentLength64 = buffer.Length;

        using Stream output = context.Response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        string trimmed = query.StartsWith("?") ? query.Substring(1) : query;
        string[] pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (string pair in pairs)
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0]);
            string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static string TryGetString(Dictionary<string, string> query, string key, string fallback)
    {
        return query.TryGetValue(key, out string value) ? value : fallback;
    }

    private static int TryGetInt(Dictionary<string, string> query, string key, int fallback)
    {
        return query.TryGetValue(key, out string value) && int.TryParse(value, out int parsedValue)
            ? parsedValue
            : fallback;
    }

    private static float TryGetFloat(Dictionary<string, string> query, string key, float fallback)
    {
        return query.TryGetValue(key, out string value) && float.TryParse(value, out float parsedValue)
            ? parsedValue
            : fallback;
    }

    private static LogType TryGetLogType(Dictionary<string, string> query, string key, LogType fallback)
    {
        return query.TryGetValue(key, out string value) && Enum.TryParse(value, true, out LogType parsedType)
            ? parsedType
            : fallback;
    }

    [Serializable]
    private struct StringWrapper
    {
        public string value;
    }
}