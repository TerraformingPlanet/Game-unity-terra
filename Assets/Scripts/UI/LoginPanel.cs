using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Panel de connexion / inscription.
/// Fait POST /auth/login ou POST /auth/register vers le DedicatedServer,
/// stocke le JWT dans PlayerSession, puis masque le panel.
///
/// Attacher sur un GameObject dans la scène de démarrage.
/// Configurer simulationServerUrl dans l'Inspector.
/// </summary>
public class LoginPanel : MonoBehaviour
{
    // =========================================================
    // Inspector
    // =========================================================

    [Header("Serveur")]
    [SerializeField] private GameConfig config;
    private string SimUrl => config != null ? config.simulationServerUrl : "http://127.0.0.1:8080";

    [Header("Références UI")]
    [SerializeField] private TMP_InputField  usernameField;
    [SerializeField] private TMP_InputField  passwordField;
    [SerializeField] private TMP_InputField  corpNameField;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button          loginButton;
    [SerializeField] private Button          registerButton;

    [Header("Démarrage")]
    [Tooltip("Objets à masquer jusqu'à connexion réussie (ex: TestLaunchMenu, ViewManager GameObjects)")]
    [SerializeField] private GameObject[] hideUntilLogin;

    // =========================================================
    // Événements
    // =========================================================

    /// <summary>Déclenché quand le login / register réussit.</summary>
    public event Action OnLoginSuccess;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Start()
    {
        // Si déjà connecté, ignorer ce panel
        var existing = PlayerSession.Instance;
        if (existing != null && existing.IsLoggedIn)
        {
            gameObject.SetActive(false);
            OnLoginSuccess?.Invoke();
            return;
        }

        // Assurer la visibilité du panel
        gameObject.SetActive(true);

        // Masquer les autres objets de jeu jusqu'à connexion
        foreach (var obj in hideUntilLogin)
            if (obj != null) obj.SetActive(false);

        loginButton?.onClick.AddListener(OnLoginClicked);
        registerButton?.onClick.AddListener(OnRegisterClicked);
        SetStatus("");
    }

    // =========================================================
    // Handlers
    // =========================================================

    private void OnLoginClicked()    => StartCoroutine(DoAuth("/auth/login", false));
    private void OnRegisterClicked() => StartCoroutine(DoAuth("/auth/register", true));

    // =========================================================
    // Auth coroutine
    // =========================================================

    private IEnumerator DoAuth(string endpoint, bool isRegister)
    {
        string username = usernameField != null ? usernameField.text.Trim() : "";
        string password = passwordField != null ? passwordField.text : "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        { SetStatus("<color=red>Nom d'utilisateur et mot de passe requis.</color>"); yield break; }

        SetStatus("Connexion…");
        SetButtonsInteractable(false);

        string url  = SimUrl.TrimEnd('/') + endpoint;
        string json = JsonUtility.ToJson(new _LoginRequest { username = username, password = password });
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var resp = JsonUtility.FromJson<_AuthResponse>(req.downloadHandler.text);
                if (resp != null && !string.IsNullOrEmpty(resp.token))
                    yield return OnAuthSuccess(resp, isRegister);
                else
                    SetStatus("<color=red>Réponse invalide du serveur.</color>");
            }
            else
            {
                string detail = TryExtractDetail(req.downloadHandler?.text) ?? req.error;
                SetStatus($"<color=red>Erreur : {detail}</color>");
            }
        }

        SetButtonsInteractable(true);
    }

    private IEnumerator OnAuthSuccess(_AuthResponse resp, bool isRegister)
    {
        var session = PlayerSession.Instance;
        if (session == null) { var go = new GameObject("PlayerSession"); go.AddComponent<PlayerSession>(); session = PlayerSession.Instance; }
        session.Token    = resp.token;
        session.PlayerId = resp.playerId;
        session.Username = resp.username;
        session.CorpId   = resp.corpId ?? "";

        if (isRegister && string.IsNullOrEmpty(session.CorpId))
        {
            string corpName = corpNameField != null ? corpNameField.text.Trim() : "";
            if (!string.IsNullOrEmpty(corpName)) { SetStatus("Création de la corporation…"); yield return CreateCorpCoroutine(corpName); }
        }

        SetStatus($"<color=green>Connecté en tant que {resp.username}</color>");
        foreach (var obj in hideUntilLogin) if (obj != null) obj.SetActive(true);
        gameObject.SetActive(false);
        OnLoginSuccess?.Invoke();
    }

    // =========================================================
    // Helpers
    // =========================================================

    private void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    private void SetButtonsInteractable(bool value)
    {
        if (loginButton   != null) loginButton.interactable   = value;
        if (registerButton != null) registerButton.interactable = value;
    }

    [System.Serializable] private class _LoginRequest  { public string username; public string password; }
    [System.Serializable] private class _CreateCorpRequest { public string name; public bool is_ai; public string owner_id; }

    private static string TryExtractDetail(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var wrapper = JsonUtility.FromJson<_ErrorResponse>(json);
        return wrapper?.detail;
    }

    private IEnumerator CreateCorpCoroutine(string corpName)
    {
        string url  = SimUrl.TrimEnd('/') + "/game/corporations";
        string ownerId = PlayerSession.Instance?.PlayerId ?? "";
        string json = JsonUtility.ToJson(new _CreateCorpRequest { name = corpName, is_ai = false, owner_id = ownerId });
        byte[] body = Encoding.UTF8.GetBytes(json);
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;
            PlayerSession.Instance?.InjectAuth(req);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var corp = JsonUtility.FromJson<_CorpResponse>(req.downloadHandler.text);
                if (corp != null && !string.IsNullOrEmpty(corp.id))
                    if (PlayerSession.Instance != null)
                        PlayerSession.Instance.CorpId = corp.id;
            }
            else
            {
                Debug.LogWarning($"[LoginPanel] Création corpo échouée : {req.downloadHandler?.text ?? req.error}");
            }
        }
    }

    // =========================================================
    // Response DTOs
    // =========================================================

    [Serializable]
    private class _AuthResponse
    {
        public string token;
        public string playerId;
        public string username;
        public string corpId;
    }

    [Serializable]
    private class _CorpResponse
    {
        public string id;
        public string name;
    }

    [Serializable]
    private class _ErrorResponse
    {
        public string detail;
    }
}
