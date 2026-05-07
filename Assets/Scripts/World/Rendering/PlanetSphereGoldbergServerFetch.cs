using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public partial class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // Re-colorisation instantanée (debug, slider niveau mer)
    // =========================================================

    /// <summary>
    /// Re-colorise toutes les faces depuis le cache serveur sans re-fetch.
    /// Utilisé par le slider debug pour ajuster le niveau de la mer en temps réel.
    /// Sans effet si les tuiles ne sont pas encore chargées.
    /// </summary>
    public void RefreshAltitudeColorization(float waterLevel)
    {
        if (_cachedServerTiles == null || _cachedServerTiles.Length == 0)
            return;

        SetWaterLevel(waterLevel);

        // Full geometry + color + depth prism rebuild with the new seaLevel.
        RebuildWaterCapsGeometry();
        _hoveredFaceId = -1;

        // Debounce: send new sea level to the server 0.5s after the user stops dragging.
        ScheduleServerWaterLevelSync(waterLevel);
    }

    // =========================================================
    // Sea-level → server sync (debounced PATCH + re-fetch)
    // =========================================================

    private void ScheduleServerWaterLevelSync(float value)
    {
        if (string.IsNullOrEmpty(_activeBodyId)) return;
        if (_waterLevelSyncCoroutine != null)
            StopCoroutine(_waterLevelSyncCoroutine);
        _waterLevelSyncCoroutine = StartCoroutine(SyncWaterLevelToServer(value));
    }

    private IEnumerator SyncWaterLevelToServer(float value)
    {
        yield return new WaitForSeconds(0.5f);

        string url = $"{BaseUrl}/bodies/{_activeBodyId}/sea-level-altitude?value={value:F4}";
        using var req = new UnityWebRequest(url, "PATCH");
        req.downloadHandler = new DownloadHandlerBuffer();
        req.timeout = TimeoutSec;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            ServerWaterLevel = value;
            yield return StartCoroutine(ReFetchTilesAfterSeaLevelChange());
        }
        else
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] Sea-level sync failed: {req.error}");
        }
        _waterLevelSyncCoroutine = null;
    }

    private IEnumerator ReFetchTilesAfterSeaLevelChange()
    {
        if (string.IsNullOrEmpty(_activeBodyId)) yield break;

        GoldbergTileState[] tilesArray = null;
        string tilesError = null;
        yield return StartCoroutine(FetchPlanetTilesForLoad(
            _activeBodyId, TilesFetchTimeoutSec,
            tiles => tilesArray = tiles,
            err   => tilesError = err));

        if (tilesError != null || tilesArray == null || tilesArray.Length == 0)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] Re-fetch après sea-level échoué: {tilesError}");
            yield break;
        }

        _cachedServerTiles = tilesArray;
        ColorizeLodFaces(tilesArray, ActiveWaterLevel);
        ApplyTopographicReliefFromTiles(tilesArray);
        yield return StartCoroutine(ApplyOverlaysAndNotify(tilesArray));
    }

    // =========================================================
    // LOD fetch haute résolution
    // =========================================================

    private void TryStartHiLodFetch()
    {
        if (_currentLodLevel != 1 || _lodHiColored || _lodHiFetching) return;
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedServerTiles == null) return;
        _lodHiFetching = true;
        StartCoroutine(FetchAndColorizeHiLod());
    }

    private IEnumerator FetchAndColorizeHiLod()
    {
        const int lodTimeoutSec = 60;  // première génération res=3 peut prendre ~2s + réseau

        GoldbergTileState[] tilesArray = null;
        string tilesError = null;
        yield return StartCoroutine(FetchTilesPages(_activeBodyId, 3, 5000, lodTimeoutSec,
            tiles => tilesArray = tiles,
            err   => tilesError = err));

        if (tilesError != null)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] LOD haut fetch échoué ({tilesError}).");
            _lodHiFetching = false;
            yield break;
        }
        if (tilesArray == null || tilesArray.Length == 0) { _lodHiFetching = false; yield break; }

        // NE PAS recoloriser le terrain avec les tuiles res=3.
        // Les couleurs terrain (res=2) sont déjà appliquées sur _sphereDataHi.faces dans
        // FetchAndColorizeFromServer. Recoloriser avec res=3 provoque un décalage :
        // H3 res=3 découpe l'espace différemment (un hex res=2 "Eau" peut avoir des
        // sous-tuiles res=3 "Roche" aux bordures), ce qui désynchronise les couleurs
        // des faces avec les borders de territoire dessinées depuis les tileIds res=2.
        //
        // Les tuiles res=3 sont stockées pour un usage futur (tooltip précis au LOD haut).
        _cachedServerTilesHi = tilesArray;

        // Réappliquer les overlays (corp, construction) sur les couleurs terrain déjà présentes.
        // Utiliser res=2 pour les tints — les dicts ownershipTints/stateTints sont indexés par tileId res=2.
        if (_cachedServerTiles != null)
            ReapplyOverlays(_sphereDataHi, _cachedServerTiles);

        // Si le LOD haut est affiché, resync snapshot hover pour refléter les nouvelles couleurs
        if (_currentLodLevel == 1)
        {
            _cachedMeshColors = (Color[])_sphereDataHi.mesh.colors.Clone();
            _hoveredFaceId    = -1;
        }

        if (debugLodVerbose)
            Debug.Log($"[PlanetSphereGoldberg] LOD haut — {tilesArray.Length} tuiles res=3 appliquées sur {_sphereDataHi.faces.Length} faces.");

        if (debugLodVerbose)
            Debug.Log($"[LOD] FetchAndColorizeHiLod | tiles={tilesArray.Length} | hiFaces={_sphereDataHi.faces.Length} | ownershipTints={_ownershipTints?.Count ?? 0}");

        _lodHiColored  = true;
        _lodHiFetching = false;
    }

    // =========================================================
    // HTTP helpers
    // =========================================================

    /// <summary>Timeout en secondes pour les requêtes courtes (/bodies, /at…).</summary>
    private int TimeoutSec => Mathf.Max(1, Mathf.CeilToInt(config != null ? config.simulationServerTimeoutSeconds : 15f));

    /// <summary>Timeout en secondes pour le fetch paginé des tuiles (peut déclencher une génération serveur).</summary>
    private int TilesFetchTimeoutSec => Mathf.Max(TimeoutSec, Mathf.CeilToInt(config != null ? config.tilesFetchTimeoutSeconds : 60f));

    /// <summary>URL de base du serveur (sans slash final).</summary>
    private string BaseUrl => (config != null ? config.simulationServerUrl : "http://127.0.0.1:8080").TrimEnd('/');

    /// <summary>
    /// Coroutine utilitaire : récupère toutes les pages de tuiles H3 depuis
    /// GET /bodies/{bodyId}/tiles/lod?h3_resolution={res}&amp;page=N&amp;size={pageSize}.
    /// Appelle <paramref name="onComplete"/> avec le tableau complet en cas de succès,
    /// ou <paramref name="onError"/> (nullable) avec le message d'erreur en cas d'échec.
    /// </summary>
    private IEnumerator FetchTilesPages(
        string bodyId, int h3Resolution, int pageSize, int timeoutSec,
        Action<GoldbergTileState[]> onComplete, Action<string> onError = null)
    {
        var allTiles = new List<GoldbergTileState>();
        int page     = 0;

        while (true)
        {
            string url = $"{BaseUrl}/bodies/{bodyId}/tiles/lod?h3_resolution={h3Resolution}&page={page}&size={pageSize}";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = timeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(req.error);
                yield break;
            }

            GoldbergTileArray batch;
            try   { batch = JsonUtility.FromJson<GoldbergTileArray>("{\"items\":" + req.downloadHandler.text + "}"); }
            catch { onError?.Invoke($"parse error page {page}"); yield break; }

            if (batch?.items == null || batch.items.Length == 0) break;
            allTiles.AddRange(batch.items);
            if (batch.items.Length < pageSize) break;
            page++;
        }

        onComplete(allTiles.ToArray());
    }

    // ── DTO internes ─────────────────────────────────────────

    [Serializable]
    private class BodyListEntryArray   { public SimulationBodyListEntry[] items; }
    [Serializable]
    private class GoldbergTileArray    { public GoldbergTileState[] items; }
    [Serializable]
    private class CorporationDataArray { public CorporationData[] items; }

    [Serializable]
    private struct StateTileColorDto
    {
        public string tileId;
        public string stateId;
        public string stateName;
        public string profileKey;
        public float  colorR;
        public float  colorG;
        public float  colorB;
    }
    [Serializable]
    private class StateTileColorArray  { public StateTileColorDto[] items; }

    private IEnumerator FetchAndColorizeFromServer(string planetName,
        DebugCoherenceOverride coherenceOverride = DebugCoherenceOverride.None,
        float waterLevelOffset = 0f)
    {
        string bodyId = null;
        float  serverWaterLevel = 0f;
        yield return StartCoroutine(FetchBodyId(planetName, (id, wl) => { bodyId = id; serverWaterLevel = wl; }));
        if (string.IsNullOrEmpty(bodyId))
            yield break;

        _activeBodyId = bodyId;
        float seaLevelAltitude = serverWaterLevel + waterLevelOffset;
        SetWaterLevel(enableTopographicRelief ? seaLevelAltitude : -1.0f);
        ServerWaterLevel = seaLevelAltitude;
        float colorizationWaterLevel = seaLevelAltitude;

        // 2) Récupérer les tuiles — mode adaptatif (hémisphère visible) ou full res=2
        GoldbergTileState[] tilesArray = null;
        string tilesError = null;
        yield return StartCoroutine(FetchPlanetTilesForLoad(bodyId, TilesFetchTimeoutSec,
            tiles => tilesArray = tiles,
            err   => tilesError = err));

        if (tilesError != null)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] /tiles/lod indisponible ({tilesError}).");
            yield break;
        }
        if (tilesArray == null || tilesArray.Length == 0)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] Aucune tuile reçue du serveur pour '{planetName}'.");
            yield break;
        }

        float updatedSea = seaLevelAltitude;
        yield return StartCoroutine(RefetchSeaLevelAltitude(bodyId, waterLevelOffset, v => updatedSea = v));
        if (updatedSea != seaLevelAltitude)
        {
            seaLevelAltitude       = updatedSea;
            colorizationWaterLevel = updatedSea;
            SetWaterLevel(enableTopographicRelief ? updatedSea : -1.0f);
            ServerWaterLevel = updatedSea;
        }

        ColorizeLodFaces(tilesArray, colorizationWaterLevel);
        ApplyTopographicReliefFromTiles(tilesArray);
        yield return StartCoroutine(ApplyOverlaysAndNotify(tilesArray));
    }

    // =========================================================
    // Adaptive fetch helpers
    // =========================================================

    /// <summary>
    /// Coroutine de sélection du mode de fetch tuiles : full res=2.
    /// L'adaptive fetch (visible_res=2 / hidden_res=1) sera activé en Phase D une fois
    /// que le re-fetch dynamique à la rotation sera implémenté. En attendant, utiliser
    /// res=1 pour l'hémisphère caché crée des gaps irréparables (boundaries H3 res=1 ≠ res=2).
    /// Centralisée ici pour garder FetchAndColorizeFromServer dans les 50 lignes.
    /// </summary>
    private IEnumerator FetchPlanetTilesForLoad(
        string bodyId, int timeoutSec,
        Action<GoldbergTileState[]> onComplete, Action<string> onError)
    {
        // Phase D : activer adaptive + rotation-triggered re-fetch quand Phase D est implémentée.
        // if (useAdaptiveFetch) { ... }
        yield return StartCoroutine(FetchTilesPages(bodyId, 2, 6000, timeoutSec, onComplete, onError));
    }

    /// <summary>
    /// Calcule la latitude/longitude (degrés) de la direction caméra → centre planète.
    /// Utilisé pour déterminer l'hémisphère visible lors du fetch adaptatif.
    /// </summary>
    private void GetCameraLatLon(out float lat, out float lon)
    {
        Camera cam = Camera.main;
        if (cam == null) { lat = 0f; lon = 0f; return; }

        // Direction du centre de la sphère vers la caméra, espace monde
        Vector3 dir = (cam.transform.position - transform.position).normalized;

        // Conversion XYZ → lat/lon (convention Y = axe polaire)
        lat = Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
        lon = Mathf.Rad2Deg * Mathf.Atan2(dir.z, dir.x);
    }

    /// <summary>
    /// Coroutine : récupère les tuiles adaptatives depuis
    /// GET /bodies/{bodyId}/tiles/adaptive?cam_lat=&amp;cam_lon=&amp;visible_res=&amp;hidden_res=.
    /// L'hémisphère visible est retourné à visibleRes, l'hémisphère caché à hiddenRes.
    /// </summary>
    private IEnumerator FetchAdaptiveTilesPages(
        string bodyId, float camLat, float camLon,
        int visibleRes, int hiddenRes, int timeoutSec,
        Action<GoldbergTileState[]> onComplete, Action<string> onError = null)
    {
        string url = $"{BaseUrl}/bodies/{bodyId}/tiles/adaptive"
                   + $"?cam_lat={camLat.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"
                   + $"&cam_lon={camLon.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"
                   + $"&visible_res={visibleRes}&hidden_res={hiddenRes}";

        using UnityWebRequest req = UnityWebRequest.Get(url);
        req.timeout = timeoutSec;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            // Fallback gracieux : si l'endpoint est indisponible on repasse en full res=2
            Debug.LogWarning($"[PlanetSphereGoldberg] /tiles/adaptive indisponible ({req.error}) — fallback full res=2.");
            yield return StartCoroutine(FetchTilesPages(bodyId, visibleRes, 6000, timeoutSec, onComplete, onError));
            yield break;
        }

        GoldbergTileArray batch;
        try   { batch = JsonUtility.FromJson<GoldbergTileArray>("{\"items\":" + req.downloadHandler.text + "}"); }
        catch { onError?.Invoke("parse error adaptive"); yield break; }

        if (batch?.items == null) { onError?.Invoke("empty response adaptive"); yield break; }
        onComplete(batch.items);
    }

    private IEnumerator FetchBodyId(string planetName, System.Action<string, float> onResult)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + "/bodies"))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] Serveur inaccessible ({req.error}) — skip recolorisation.");
                onResult(null, 0f); yield break;
            }
            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            BodyListEntryArray list;
            try   { list = JsonUtility.FromJson<BodyListEntryArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] Parse /bodies invalide."); onResult(null, 0f); yield break; }
            if (list?.items != null)
                foreach (SimulationBodyListEntry e in list.items)
                    if (e.name == planetName && e.surfaceType == "goldberg")
                    { onResult(e.bodyId, e.seaLevelAltitude); yield break; }
            Debug.LogWarning($"[PlanetSphereGoldberg] Corps '{planetName}' introuvable sur le serveur.");
            onResult(null, 0f);
        }
    }

    private IEnumerator RefetchSeaLevelAltitude(string bodyId, float waterLevelOffset, System.Action<float> onUpdated)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + "/bodies"))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;
            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            try
            {
                BodyListEntryArray list = JsonUtility.FromJson<BodyListEntryArray>(wrapped);
                if (list?.items != null)
                    foreach (SimulationBodyListEntry e in list.items)
                        if (e.bodyId == bodyId) { onUpdated(e.seaLevelAltitude + waterLevelOffset); yield break; }
            }
            catch { }
        }
    }

    private void ColorizeLodFaces(GoldbergTileState[] tilesArray, float colorizationWaterLevel)
    {
        // Build H3-exact mesh — 1 face == 1 H3 tile, exact geometry from server boundaryLatLonFlat.
        RebuildH3MeshFromTiles(tilesArray);
        ColorizeH3Exact(_sphereData.faces, tilesArray, colorizationWaterLevel);
        ReapplyOverlays(_sphereData, tilesArray);
        _cachedServerTiles = tilesArray;
        _cachedColorByType = terrainPalette != null ? terrainPalette.ToDictionary() : TerrainColorPalette.DefaultDictionary();
    }

    private void ApplyTopographicReliefFromTiles(GoldbergTileState[] tilesArray)
    {
        if (!enableTopographicRelief) return;

        // H3-exact: face i IS tile i — no nearest-neighbour required.
        var activeData = (enableLod && _sphereDataLo.faces != null) ? _sphereDataLo : _sphereData;
        int faceCount  = activeData.faces.Length;
        _cachedFaceAltitudesLo     = BuildH3FaceAltitudes(tilesArray, faceCount);
        _cachedFaceIsOceanLo       = BuildH3FaceIsOcean(tilesArray, faceCount, ActiveWaterLevel);
        _cachedFaceIsInlandWaterLo = BuildH3FaceIsInlandWater(tilesArray, faceCount);

        // H3 mode: altitude is baked into the mesh by H3SphereBuilder.Build() —
        // skip post-hoc ApplyTopographicDisplacement to avoid double-applying.
        // Legacy GP path (pre-H3 placeholder) still needs the post-hoc displacement.
        if (_h3Result.faceToTileId == null)
        {
            GoldbergSphereGenerator.ApplyTopographicDisplacement(
                activeData.mesh, activeData.vertexFaceId, _cachedFaceAltitudesLo,
                topographicDisplacementScale, activeData.vertexCornerGroup, ActiveWaterLevel);
            if (enableLod && _sphereDataHi.faces != null)
                ApplyHiLodTopographic(tilesArray, activeData.mesh);
        }
        else
        {
            // H3: all LOD slots share the same mesh (Phase C). Share altitude arrays.
            _cachedFaceAltitudesHi     = _cachedFaceAltitudesLo;
            _cachedFaceIsOceanHi       = _cachedFaceIsOceanLo;
            _cachedFaceIsInlandWaterHi = _cachedFaceIsInlandWaterLo;
        }

        BuildWaterCapsFromCache();
    }

    private IEnumerator ApplyOverlaysAndNotify(GoldbergTileState[] tilesArray)
    {
        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
        Debug.Log($"[PlanetSphereGoldberg] Tuiles serveur appliquées : {tilesArray.Length} tuiles → {_sphereData.faces.Length} faces.");
        if (debugLodVerbose)
            Debug.Log($"[LOD] FetchAndColorizeFromServer | tiles={tilesArray.Length} | enableLod={enableLod} | hiLodReady={_sphereDataHi.faces != null} | ownershipTints={_ownershipTints?.Count ?? 0}");
        _ownershipTints  = null;
        _tileToCorpId    = null;
        yield return StartCoroutine(FetchOwnershipOverlay());
        _stateTints      = null;
        _tileToStateId   = null;
        _tileToStateName = null;
        yield return StartCoroutine(FetchStateOverlay());
        if (_currentLodLevel == 1) ApplyLodLevel(1);
        OnH3TilesReady?.Invoke(tilesArray, _cachedColorByType);
    }

    /// <summary>Aucune projection Mercator après migration H3 — toujours null.</summary>
    public HexCell GetProjectedCell(float latitude, float longitude) => null;

    /// <summary>Aucune projection disponible après migration H3.</summary>
    public bool TryBuildProjectionSummary(out PlanetaryHexGrid.ProjectionDebugSummary summary)
    {
        summary = default;
        return false;
    }

    [ContextMenu("Clear Sphere Cache")]
    public void ClearProjectionCache()
    {
        ClearSphereCache();
    }

    /// <summary>
    /// Snapshot debug : affiche dans la console un histogramme des couleurs actuelles
    /// des faces du mesh actif (_sphereData). Utiliser avant/après un switch LOD pour
    /// vérifier que les couleurs terrain ne changent pas.
    /// </summary>
    [ContextMenu("Debug — Snapshot couleurs faces")]
    public void DebugSnapshotFaceColors()
    {
        if (_sphereData.faces == null || _sphereData.faces.Length == 0)
        {
            Debug.LogWarning("[Snapshot] Aucune face — charger une planète d'abord.");
            return;
        }

        // Histogramme par couleur arrondie (R/G/B à 2 décimales)
        var hist = new Dictionary<string, int>();
        for (int i = 0; i < _sphereData.faces.Length; i++)
        {
            Color c = _sphereData.faces[i].color;
            string key = $"({c.r:F2},{c.g:F2},{c.b:F2})";
            hist[key] = hist.TryGetValue(key, out int v) ? v + 1 : 1;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Snapshot] LOD={_currentLodLevel} | faces={_sphereData.faces.Length} | body={_activeBodyId}");
        sb.AppendLine($"  res2_tiles={_cachedServerTiles?.Length ?? 0} | res3_tiles={_cachedServerTilesHi?.Length ?? 0}");
        sb.AppendLine($"  stateTints={_allStateTints?.Count ?? 0} | corpTints={_ownershipTints?.Count ?? 0}");
        sb.AppendLine("  Histogramme couleurs (top 10) :");
        int rank = 0;
        foreach (var kv in hist.OrderByDescending(x => x.Value))
        {
            sb.AppendLine($"    #{rank + 1}: {kv.Key} → {kv.Value} faces");
            if (++rank >= 10) break;
        }
        Debug.Log(sb.ToString());
    }
}
