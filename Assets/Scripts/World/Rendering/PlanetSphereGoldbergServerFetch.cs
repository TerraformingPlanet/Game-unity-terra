using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public partial class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // LOD fetch haute résolution
    // =========================================================

    private void TryStartHiLodFetch()
    {
        if (_currentLodLevel != 1 || _lodHiColored || _lodHiFetching) return;
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedColorByType == null) return;
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

    /// <summary>Timeout en secondes, au minimum 1.</summary>
    private int TimeoutSec => Mathf.Max(1, Mathf.CeilToInt(config != null ? config.simulationServerTimeoutSeconds : 15f));

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
        // Couleurs terrain : palette Unity (source de vérité), le serveur envoie seulement l'index int.
        var colorByType = terrainPalette != null
            ? terrainPalette.ToDictionary()
            : TerrainColorPalette.DefaultDictionary();

        // 1) Récupérer la liste des corps pour trouver le bodyId
        string bodyId  = null;

        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + "/bodies"))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] Serveur inaccessible ({req.error}) — skip recolorisation.");
                yield break;
            }

            // JsonUtility ne parse pas les tableaux root — wrapping
            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            BodyListEntryArray list;
            try   { list = JsonUtility.FromJson<BodyListEntryArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] Parse /bodies invalide."); yield break; }

            if (list?.items != null)
            {
                foreach (SimulationBodyListEntry entry in list.items)
                {
                    if (entry.name == planetName && entry.surfaceType == "goldberg")
                    {
                        bodyId = entry.bodyId;
                        break;
                    }
                }
            }
        }

        // Si le serveur n'a pas encore de corps, on attend — le bootstrap est géré côté serveur.
        if (string.IsNullOrEmpty(bodyId))
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] Corps '{planetName}' introuvable sur le serveur. Le serveur n'est peut-être pas encore prêt.");
            yield break;
        }

        // Mémorise le bodyId pour les lookups H3 au clic
        _activeBodyId = bodyId;

        // 2) Récupérer TOUTES les tuiles via /tiles/lod?h3_resolution=2 (pagination)
        GoldbergTileState[] tilesArray = null;
        string tilesError = null;
        yield return StartCoroutine(FetchTilesPages(bodyId, 2, 6000, TimeoutSec,
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

        // 3) Recoloriser les faces GP
        if (enableLod && _sphereDataLo.faces != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereDataLo.faces, tilesArray, colorByType);
            ReapplyOverlays(_sphereDataLo, tilesArray);
        }
        else
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereData.faces, tilesArray, colorByType);
            ReapplyOverlays(_sphereData, tilesArray);
        }

        // Cache pour re-colorisation LOD
        _cachedServerTiles  = tilesArray;
        _cachedColorByType  = colorByType;

        // Coloriser aussi le LOD haut avec les tuiles res=2 en attendant le fetch res=3.
        if (enableLod && _sphereDataHi.faces != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(_sphereDataHi.faces, tilesArray, colorByType);
            ReapplyOverlays(_sphereDataHi, tilesArray);
            _lodHiBaseColored = true;  // couleurs biomes déjà appliquées — ApplyLodLevel n'a pas besoin de refaire
        }

        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone(); // resync snapshot hover

        Debug.Log($"[PlanetSphereGoldberg] Tuiles serveur appliquées : {tilesArray.Length} tuiles → {_sphereData.faces.Length} faces.");

        if (debugLodVerbose)
            Debug.Log($"[LOD] FetchAndColorizeFromServer | tiles={tilesArray.Length} | enableLod={enableLod} | hiLodReady={_sphereDataHi.faces != null} | ownershipTints={_ownershipTints?.Count ?? 0}");

        // Ownership overlay (Phase 7.1) — tint des hexes claimés sur ce corps
        _ownershipTints  = null;
        _tileToCorpId    = null;
        yield return StartCoroutine(FetchOwnershipOverlay());

        // State overlay (Phase colonisation) — carte politique
        _stateTints      = null;
        _tileToStateId   = null;
        _tileToStateName = null;
        yield return StartCoroutine(FetchStateOverlay());

        // Si on était déjà en LOD haut avant la fin du fetch, swap maintenant que _cachedServerTiles est prêt
        if (_currentLodLevel == 1) ApplyLodLevel(1);

        // Notifie les autres vues (Flat, Tangent)
        OnH3TilesReady?.Invoke(tilesArray, colorByType);
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
