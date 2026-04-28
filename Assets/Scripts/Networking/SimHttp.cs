using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Helpers coroutine pour les appels HTTP vers le DedicatedServer.
/// Gère automatiquement : timeout, injection JWT (PlayerSession), log d'erreur optionnel.
///
/// Usage GET :
///   string json = null;
///   yield return SimHttp.Get(url, timeout, r => json = r);
///   if (json == null) yield break; // erreur déjà traitée via onError
///
/// Usage POST (sans corps) :
///   string resp = null;
///   yield return SimHttp.Post(url, timeout, r => resp = r);
///   if (resp == null) yield break;
/// </summary>
public static class SimHttp
{
    /// <summary>
    /// GET authentifié.
    /// onSuccess reçoit le corps de la réponse si HTTP 2xx.
    /// onError   reçoit le message d'erreur sinon (peut être null).
    /// </summary>
    public static IEnumerator Get(
        string url,
        float  timeoutSeconds,
        Action<string> onSuccess,
        Action<string> onError = null)
    {
        using var req = UnityWebRequest.Get(url);
        req.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));
        PlayerSession.Instance?.InjectAuth(req);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        onSuccess?.Invoke(req.downloadHandler.text);
    }

    /// <summary>
    /// POST sans corps JSON (commandes serveur, fire-and-forget).
    /// onSuccess reçoit la réponse JSON si HTTP 2xx.
    /// onError   reçoit le message d'erreur sinon (peut être null).
    /// </summary>
    public static IEnumerator Post(
        string url,
        float  timeoutSeconds,
        Action<string> onSuccess = null,
        Action<string> onError   = null)
    {
        using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            downloadHandler = new DownloadHandlerBuffer(),
            uploadHandler   = new UploadHandlerRaw(Array.Empty<byte>()),
            timeout         = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds))
        };
        req.SetRequestHeader("Content-Type", "application/json");
        PlayerSession.Instance?.InjectAuth(req);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        onSuccess?.Invoke(req.downloadHandler.text);
    }
}
