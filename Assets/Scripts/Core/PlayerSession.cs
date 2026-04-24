using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Singleton persistant stockant la session authentifiée du joueur courant.
/// Conservé entre les scènes (DontDestroyOnLoad).
///
/// Utilisation :
///   PlayerSession.Instance.Token       → JWT Bearer
///   PlayerSession.Instance.PlayerId    → UUID joueur
///   PlayerSession.Instance.CorpId      → UUID corpo liée (vide si pas encore liée)
///   PlayerSession.Instance.InjectAuth(req)  → ajoute le header Authorization
///   PlayerSession.Instance.IsLoggedIn  → true si token non vide
/// </summary>
public class PlayerSession : MonoBehaviour
{
    // =========================================================
    // Singleton
    // =========================================================

    public static PlayerSession Instance { get; private set; }

    // =========================================================
    // Données de session
    // =========================================================

    public string Token    { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Username { get; set; } = "";
    public string CorpId   { get; set; } = "";

    public bool IsLoggedIn => !string.IsNullOrEmpty(Token);

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

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Ajoute le header Authorization: Bearer {Token} à la requête si le joueur est connecté.
    /// Appeler juste avant req.SendWebRequest().
    /// </summary>
    public void InjectAuth(UnityWebRequest req)
    {
        if (IsLoggedIn)
            req.SetRequestHeader("Authorization", "Bearer " + Token);
    }

    /// <summary>
    /// Construit l'URL WebSocket pour le push serveur en incluant le token en query param.
    /// </summary>
    public string BuildWebSocketUrl(string serverBaseUrl)
    {
        string wsBase = serverBaseUrl
            .Replace("https://", "wss://")
            .Replace("http://", "ws://")
            .TrimEnd('/');
        string url = wsBase + "/game/ws/events";
        if (IsLoggedIn)
            url += "?token=" + UnityWebRequest.EscapeURL(Token);
        return url;
    }

    /// <summary>
    /// Réinitialise la session (déconnexion).
    /// </summary>
    public void Logout()
    {
        Token    = "";
        PlayerId = "";
        Username = "";
        CorpId   = "";
    }
}
