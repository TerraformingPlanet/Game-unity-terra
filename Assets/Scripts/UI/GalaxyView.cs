using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// Vue Galaxie simplifiée — affiche les systèmes solaires connus depuis GET /galaxy/systems.
///
/// Comportement :
///   - Au démarrage, interroge le serveur.  Si la liste est vide, crée un système par défaut
///     via POST /galaxy/systems?name=...  puis re-fetch.
///   - Affiche chaque système comme un marqueur sphérique (sprite étoile) avec un label TMP.
///   - Émet OnSystemClicked(systemName) quand l'utilisateur clique sur un marqueur.
///
/// Prérequis Unity :
///   - Attacher sur le GalaxyRoot GameObject (activé/désactivé par ViewManager).
///   - Assigner les textures en Inspector (galaxyBackgroundTexture, starTexture) si disponibles.
/// </summary>
public class GalaxyView : MonoBehaviour
{
    // =========================================================
    // Events
    // =========================================================

    /// <summary>Déclenché quand l'utilisateur clique sur un système solaire.</summary>
    public event Action<string> OnSystemClicked;

    // =========================================================
    // Inspector
    // =========================================================

    [Header("Serveur")]
    [SerializeField] private GameConfig config;
    private string SimUrl     => config != null ? config.simulationServerUrl           : "http://127.0.0.1:8080";
    private float  SimTimeout => config != null ? config.simulationServerTimeoutSeconds : 15f;

    [Header("Données par défaut")]
    [Tooltip("Nom du système solaire créé automatiquement si le serveur est vide.")]
    [SerializeField] private string defaultSystemName = "Kepler-442";

    [Header("Affichage")]
    [Tooltip("Facteur de mise à l'échelle : 1 ly → X unités Unity.")]
    [SerializeField] private float systemDisplayScale = 10f;
    [Tooltip("Rayon d'affichage d'un marqueur étoile (unités Unity).")]
    [SerializeField] private float starDisplayRadius = 2.0f;

    [Header("Textures (optionnel)")]
    [Tooltip("Texture de fond de la galaxie.")]
    [SerializeField] private Texture2D galaxyBackgroundTexture;
    [Tooltip("Texture sprite de l'étoile (utilisée pour chaque système).")]
    [SerializeField] private Texture2D starTexture;

    // =========================================================
    // DTOs internes (JSON serveur)
    // =========================================================

    [Serializable]
    private class GalacticPositionDto
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    private class SolarSystemStateDto
    {
        public string systemId;
        public string name;
        public GalacticPositionDto position;
        public bool isDiscovered = true;
    }

    [Serializable]
    private class SolarSystemStateArray { public SolarSystemStateDto[] items; }

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Start()
    {
        StartCoroutine(FetchAndBuildGalaxy());
    }

    // =========================================================
    // Fetch + Bootstrap
    // =========================================================

    private IEnumerator FetchAndBuildGalaxy()
    {
        string baseUrl = SimUrl.TrimEnd('/');

        SolarSystemStateDto[] systems = null;
        yield return FetchSystems(baseUrl, result => systems = result);

        // Si liste vide → créer un système par défaut
        if (systems == null || systems.Length == 0)
        {
            Debug.Log($"[GalaxyView] Aucun système — création de '{defaultSystemName}'.");
            string escaped = UnityWebRequest.EscapeURL(defaultSystemName);
            string postUrl = $"{baseUrl}/galaxy/systems?name={escaped}&x=0&y=0&z=0";

            using (UnityWebRequest postReq = UnityWebRequest.PostWwwForm(postUrl, ""))
            {
                postReq.timeout = Mathf.Max(1, Mathf.CeilToInt(SimTimeout * 2f));
                yield return postReq.SendWebRequest();

                if (postReq.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning($"[GalaxyView] Création système échouée : {postReq.error}");
                else
                    Debug.Log("[GalaxyView] Système créé — re-fetch.");
            }

            yield return FetchSystems(baseUrl, result => systems = result);
        }

        if (systems == null || systems.Length == 0)
        {
            Debug.LogWarning("[GalaxyView] Aucun système disponible après tentative de création.");
            yield break;
        }

        BuildGalaxy(systems);
    }

    /// <summary>Interroge GET /galaxy/systems et invoque <paramref name="onResult"/> avec le tableau parsé.</summary>
    private IEnumerator FetchSystems(string baseUrl, Action<SolarSystemStateDto[]> onResult)
    {
        using UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/galaxy/systems");
        req.timeout = Mathf.Max(1, Mathf.CeilToInt(SimTimeout));
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[GalaxyView] Serveur inaccessible ({req.error}).");
            onResult?.Invoke(null);
            yield break;
        }

        string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
        SolarSystemStateArray list = null;
        try   { list = JsonUtility.FromJson<SolarSystemStateArray>(wrapped); }
        catch { Debug.LogWarning("[GalaxyView] Parse /galaxy/systems invalide."); }

        onResult?.Invoke(list?.items);
    }

    // =========================================================
    // Construction des visuels
    // =========================================================

    private void BuildGalaxy(SolarSystemStateDto[] systems)
    {
        BuildBackground();

        foreach (SolarSystemStateDto sys in systems)
        {
            if (!sys.isDiscovered) continue;

            float px = sys.position != null ? sys.position.x : 0f;
            float pz = sys.position != null ? sys.position.z : 0f;
            Vector3 pos = new Vector3(px * systemDisplayScale, 0f, pz * systemDisplayScale);
            CreateSystemMarker(sys, pos);
        }
    }

    private void BuildBackground()
    {
        if (galaxyBackgroundTexture == null) return;

        // Grand plan toujours rendu EN FOND (renderQueue=Background, avant la géométrie)
        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "GalaxyBackground";
        bg.transform.SetParent(transform, false);
        bg.transform.localPosition = new Vector3(0f, -2f, 0f);  // en-dessous des étoiles (y=0)
        bg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        bg.transform.localScale    = new Vector3(500f, 500f, 1f);  // couvre toute vue

        Shader unlitShader = Shader.Find("Unlit/Texture");
        Material bgMat = new Material(unlitShader != null ? unlitShader : Shader.Find("Standard"));
        bgMat.mainTexture  = galaxyBackgroundTexture;
        bgMat.renderQueue  = (int)UnityEngine.Rendering.RenderQueue.Background;  // 1000
        bg.GetComponent<Renderer>().material = bgMat;

        Destroy(bg.GetComponent<Collider>());
    }

    private void CreateSystemMarker(SolarSystemStateDto sys, Vector3 pos)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = sys.name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * (starDisplayRadius * 2f);

        // Matériau étoile
        Shader unlitShader = Shader.Find("Unlit/Texture");
        Material starMat = new Material(unlitShader != null ? unlitShader : Shader.Find("Standard"));
        if (starTexture != null)
            starMat.mainTexture = starTexture;
        else
            starMat.color = Color.yellow;
        go.GetComponent<Renderer>().material = starMat;

        // Label TMP — enfant direct de GalaxyRoot (pas de la sphère) pour éviter la mise à l'échelle
        GameObject labelGO = new GameObject("Label_" + sys.name);
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.localPosition = pos + Vector3.up * (starDisplayRadius * 2f + 2.5f);
        labelGO.AddComponent<BillboardLabel>();  // toujours face à la caméra
        TextMeshPro tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text      = sys.name;
        tmp.fontSize  = 3f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;

        // Handler de clic
        SystemClickHandler handler = go.AddComponent<SystemClickHandler>();
        handler.Init(sys.name, OnSystemClicked);
    }
}

// =============================================================================
// Billboard — fait toujours face à la caméra (débug labels)
// =============================================================================

internal class BillboardLabel : MonoBehaviour
{
    private void LateUpdate()
    {
        if (Camera.main == null) return;
        transform.LookAt(Camera.main.transform.position);
        // Compenser le flip vertical que LookAt peut introduire
        transform.Rotate(0f, 180f, 0f, Space.Self);
    }
}

// =============================================================================
// Composant auxiliaire — gère le clic sur un marqueur système
// =============================================================================

/// <summary>
/// Composant léger ajouté dynamiquement sur chaque marqueur de système solaire.
/// </summary>
internal class SystemClickHandler : MonoBehaviour
{
    private string                _systemName;
    private Action<string>        _callback;

    public void Init(string systemName, Action<string> callback)
    {
        _systemName = systemName;
        _callback   = callback;
    }

    private void OnMouseDown()
    {
        if (UIEventSystemUtility.IsPointerOverUI())
            return;

        Debug.Log($"[GalaxyView] Clic → {_systemName}");
        _callback?.Invoke(_systemName);
    }
}
