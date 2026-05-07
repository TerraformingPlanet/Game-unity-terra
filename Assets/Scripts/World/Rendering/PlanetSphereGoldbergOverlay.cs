using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public partial class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // Ownership overlay (Phase 7.1)
    // =========================================================

    /// <summary>
    /// Re-fetches the ownership overlay from the server and reapplies it.
    /// Called externally (e.g. after claim/unclaim) to refresh the visualization.
    /// </summary>
    public void RefreshOwnershipOverlay()
    {
        _ownershipTints = null;
        _tileToCorpId   = null;
        _ownershipOverlayFetched = false;
        _borderRenderer?.ClearBorders();
        StartCoroutine(FetchOwnershipOverlay());
    }

    // =========================================================
    // Construction overlay (Phase 10.5)
    // =========================================================

    // Orange pulsing tint applied to tiles that have in-progress construction items.
    private HashSet<string> _constructionTileIds = new HashSet<string>();

    /// <summary>
    /// Recompute and re-apply border loops using the current active LOD mesh (_sphereData).
    /// No-op if ownership data is not yet available.
    /// </summary>
    private void RebuildBorderLoops()
    {
        if (_cachedServerTiles == null || _sphereData.faces == null) return;

        // Utiliser le mesh actif courant (_sphereData) pour les bordures.
        // _sphereData est déjà mis à jour par ApplyLodLevel avant cet appel.
        // Utiliser les faces Lo (492) au LOD haut (1962 faces visibles) créait un décalage :
        // les bordures Lo coupaient les tuiles Hi en diagonale (chaque Lo-face ≈ 4 Hi-faces).
        // Avec le mesh actif, les bordures s'alignent toujours avec les tuiles affichées.
        var borderFaces = _sphereData.faces;

        var loops = new System.Collections.Generic.List<(Vector3[], Color)>();

        // Toujours filtrer les tuiles Eau des bordures, que le relief soit actif ou non.
        // Les WaterCaps couvrent les faces océaniques dans tous les cas.
        // Sans ce filtre, les LineRenderers des bordures (renderQueue=3001) passent
        // au-dessus des WaterCaps → boucles colorées visibles dans l'eau.
        var underwaterIds = new HashSet<string>();
        foreach (var t in _cachedServerTiles)
            if (t.terrainType == TerrainType.Eau)
                underwaterIds.Add(t.tileId);

        // Corp borders — couleur de la corp (teinte ownership)
        if (_ownershipTints != null && _ownershipTints.Count > 0 && _tileToCorpId != null)
        {
            var corpId = underwaterIds != null
                ? FilterUnderwaterTiles(_tileToCorpId, underwaterIds)
                : _tileToCorpId;
            loops.AddRange(GoldbergFaceColorizer.GetBoundaryLoops(
                borderFaces, _cachedServerTiles, _ownershipTints, corpId));
        }

        // State borders (political map) — couleur de l'état (pas de recoloration des tuiles)
        if (_allStateTints != null && _allStateTints.Count > 0 && _tileToStateId != null)
        {
            var stateId = underwaterIds != null
                ? FilterUnderwaterTiles(_tileToStateId, underwaterIds)
                : _tileToStateId;
            var stateLoops = GoldbergFaceColorizer.GetBoundaryLoops(
                borderFaces, _cachedServerTiles, _allStateTints, stateId);
            loops.AddRange(stateLoops);
        }

        if (loops.Count > 0)
            _borderRenderer?.UpdateBorders(loops);
    }

    private static Dictionary<string, string> FilterUnderwaterTiles(
        Dictionary<string, string> source, HashSet<string> underwaterIds)
    {
        var result = new Dictionary<string, string>(source.Count);
        foreach (var kv in source)
            if (!underwaterIds.Contains(kv.Key))
                result[kv.Key] = kv.Value;
        return result;
    }

    /// <summary>
    /// Updates the set of tiles under construction and applies an orange tint overlay.
    /// Call from GameHUD's PollConstructionQueue whenever the queue changes.
    /// Pass an empty set (or null) to clear the overlay.
    /// </summary>
    public void SetConstructionTiles(HashSet<string> tileIds)
    {
        _constructionTileIds = tileIds ?? new HashSet<string>();

        RebuildBorderLoops();
        ReapplyOverlays(_sphereData, _cachedServerTiles);
    }

    /// <summary>
    /// Applique les teintes overlays (corp) sur un mesh donné, puis ApplyFaceColors.
    /// Reset systématique des couleurs de face depuis l'altitude avant d'appliquer les teintes,
    /// pour éviter l'accumulation (double-tint) lors de multiple appels successifs.
    /// </summary>
    private void ReapplyOverlays(GoldbergSphereGenerator.GoldbergMeshData meshData, GoldbergTileState[] serverTiles)
    {
        if (serverTiles == null || meshData.faces == null) return;

        // Reset des couleurs de face depuis l'altitude AVANT d'appliquer les tints.
        // Évite l'accumulation : ApplyOwnershipTint modifie faces[i].color en place,
        // donc deux appels successifs doubleraient la contribution de la couleur corp.
        // Dispatch to H3-exact or pre-H3 colorization (extracted to keep ReapplyOverlays concise).
        ApplyBaseColors(meshData.faces, serverTiles);

        // Teintes corp (ownership) — par-dessus les couleurs terrain
        if (_ownershipTints != null && _ownershipTints.Count > 0 && _tileToCorpId != null)
            GoldbergFaceColorizer.ApplyOwnershipTint(
                meshData.faces, serverTiles, _ownershipTints, _tileToCorpId,
                borderBlend: 0.25f, interiorBlend: 0.10f);

        // Teintes construction (orange pulsing)
        if (_constructionTileIds != null && _constructionTileIds.Count > 0)
        {
            var constructionTints = new Dictionary<string, Color>();
            Color constructionColor = new Color(1f, 0.55f, 0f, 1f); // orange
            foreach (string tileId in _constructionTileIds)
                constructionTints[tileId] = constructionColor;
            GoldbergFaceColorizer.ApplyOwnershipTint(
                meshData.faces, serverTiles, constructionTints, null, // no corpId for borders
                borderBlend: 0.25f, interiorBlend: 0.10f);
        }

        // Zone overlay (multi-dimensional) — remplace ownership si un zone lens est actif
        if (_zoneTints != null && _zoneTints.Count > 0)
            GoldbergFaceColorizer.ApplyOwnershipTint(
                meshData.faces, serverTiles, _zoneTints, _tileToZoneId,
                borderBlend: 0.35f, interiorBlend: 0.55f);

        // Appliquer les couleurs finales au mesh (avec gradient de rim si activé).
        if (rimBlendStrength > 0f && meshData.vertexCornerGroup != null)
            GoldbergSphereGenerator.ApplyFaceColorsBlended(
                meshData.mesh, meshData.faces,
                meshData.vertexFaceId, meshData.vertexCornerGroup,
                rimBlendStrength, rimBlendMaxDelta);
        else
            GoldbergSphereGenerator.ApplyFaceColors(meshData.mesh, meshData.faces, meshData.vertexFaceId);

        if (debugLodVerbose)
            Debug.Log($"[OVERLAY] ReapplyOverlays | faces={meshData.faces.Length} | stateTints={_stateTints?.Count ?? 0} | corpTints={_ownershipTints?.Count ?? 0} | constructionTints={_constructionTileIds?.Count ?? 0}");
    }

    /// <summary>
    /// Fetches GET /bodies/{body_id}/ownership-tiles and tints each claimed tile on the current body
    /// with the owning corporation's color from server. Called after biome colorization.
    /// No-op if no tiles are claimed on this body or if the server is unreachable.
    /// </summary>
    private IEnumerator FetchOwnershipOverlay()
    {
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedServerTiles == null) yield break;
        if (_ownershipOverlayFetched) yield break;

        OwnershipTileDtoArray tiles = null;
        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + "/bodies/" + _activeBodyId + "/ownership-tiles"))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] Ownership fetch échoué ({req.error}) — overlay ignoré.");
                yield break;
            }

            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            try   { tiles = JsonUtility.FromJson<OwnershipTileDtoArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] Ownership parse invalide."); yield break; }
        }
        if (tiles?.items == null || tiles.items.Length == 0) { _borderRenderer?.ClearBorders(); yield break; }

        ApplyOwnershipTints(tiles.items);
    }

    private void ApplyOwnershipTints(OwnershipTileDto[] dtoItems)
    {
        var tints    = new Dictionary<string, Color>();
        var toCorpId = new Dictionary<string, string>();
        foreach (OwnershipTileDto dto in dtoItems)
        {
            Color corpColor = new Color(dto.colorR, dto.colorG, dto.colorB, 1f);
            tints[dto.tileId]    = corpColor;
            toCorpId[dto.tileId] = dto.corpId;
        }
        Debug.Log($"[PlanetSphereGoldberg] Ownership: {tints.Count} tuile(s) à teinter sur ce corps.");
        if (tints.Count == 0) { _borderRenderer?.ClearBorders(); return; }
        _ownershipTints  = tints;
        _tileToCorpId    = toCorpId;
        RebuildBorderLoops();
        string rendererStatus = _borderRenderer != null ? "OK" : "no renderer";
        if (debugLodVerbose)
            Debug.Log($"[OVERLAY] FetchOwnershipOverlay | tints={tints.Count} | renderer={rendererStatus}");
        Debug.Log($"[PlanetSphereGoldberg] Ownership overlay : {tints.Count} tuile(s), {rendererStatus}.");
        _ownershipOverlayFetched = true;
        ReapplyOverlays(_sphereData, _cachedServerTiles);
        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
    }

    private IEnumerator FetchStateOverlay()
    {
        if (string.IsNullOrEmpty(_activeBodyId) || _cachedServerTiles == null) yield break;

        string url = BaseUrl + $"/game/bodies/{_activeBodyId}/state-tile-colors";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] State overlay fetch échoué ({req.error}) — ignoré.");
                yield break;
            }

            StateTileColorArray data;
            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            try   { data = JsonUtility.FromJson<StateTileColorArray>(wrapped); }
            catch { Debug.LogWarning("[PlanetSphereGoldberg] State overlay parse invalide."); yield break; }

            if (data?.items == null || data.items.Length == 0) yield break;

            if (_stateOverlayFetched) yield break;

            ApplyStateTints(data.items);
        }
    }

    private void ApplyStateTints(StateTileColorDto[] items)
    {
        var tileTerrain = new Dictionary<string, TerrainType>();
        if (_cachedServerTiles != null)
            foreach (GoldbergTileState t in _cachedServerTiles)
                if (!string.IsNullOrEmpty(t.tileId))
                    tileTerrain[t.tileId] = t.terrainType;

        var stateColorCache = new Dictionary<string, Color>();
        var stateTints      = new Dictionary<string, Color>();
        var allStateTints   = new Dictionary<string, Color>();
        var tileToStateId   = new Dictionary<string, string>();
        var tileToStateName = new Dictionary<string, string>();
        foreach (StateTileColorDto entry in items)
        {
            if (string.IsNullOrEmpty(entry.tileId)) continue;
            Color col = new Color(entry.colorR, entry.colorG, entry.colorB, 1f);
            tileToStateId[entry.tileId]   = entry.stateId;
            tileToStateName[entry.tileId] = entry.stateName;
            allStateTints[entry.tileId]   = col;
            if (!tileTerrain.TryGetValue(entry.tileId, out TerrainType tt) || tt != TerrainType.Eau)
                stateTints[entry.tileId] = col;
        }
        _stateTints      = stateTints;
        _allStateTints   = allStateTints;
        _tileToStateId   = tileToStateId;
        _tileToStateName = tileToStateName;
        RebuildBorderLoops();
        _stateOverlayFetched = true;
        ReapplyOverlays(_sphereData, _cachedServerTiles);
        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
        Debug.Log($"[PlanetSphereGoldberg] State overlay : {stateTints.Count} tuile(s) colorées + bordures d'État.");
        if (debugLodVerbose)
            Debug.Log($"[OVERLAY] FetchStateOverlay | stateTints={stateTints.Count} | allStateTints={allStateTints.Count} | states={stateColorCache.Count} | tileToStateId={tileToStateId.Count}");
    }
    /// <summary>
    /// Applies terrain base colors to faces using the H3-exact or pre-H3 path depending on
    /// whether the H3 mesh has been built. Extracted to keep ReapplyOverlays under 50 lines.
    /// </summary>
    private void ApplyBaseColors(
        GoldbergSphereGenerator.GoldbergFace[] faces, GoldbergTileState[] serverTiles)
    {
        if (_h3Result.faceToTileId != null)
        {
            if (ActiveLens == PlanetLensMode.Elevation)
                ColorizeH3ElevationLens(faces, serverTiles);
            else
                ColorizeH3Exact(faces, serverTiles, ActiveWaterLevel);
        }
        else
        {
            if (ActiveLens == PlanetLensMode.Elevation)
                GoldbergFaceColorizer.ColorizeElevationLens(faces, serverTiles);
            else
                GoldbergFaceColorizer.ColorizeFromAltitude(faces, serverTiles, ActiveWaterLevel);
        }
    }

    // =========================================================
    // Zone overlay — 6-dimension lens (fetch + apply)
    // =========================================================

    [System.Serializable]
    private class ZoneTilesDto { public string dimension; public ZoneTilesEntry[] tiles; }
    [System.Serializable]
    private class ZoneTilesEntry { public string key; public string value; }
    // Note: JsonUtility doesn't support Dictionary<> directly; we use a wrapper DTO with manual parse.

    /// <summary>
    /// Fetches GET /bodies/{body_id}/zone-tiles/{dimension} and builds zone tints.
    /// One coroutine per dimension change; aborts if _activeDimension changed mid-flight.
    /// </summary>
    private IEnumerator FetchZoneOverlay(string dimension)
    {
        if (string.IsNullOrEmpty(_activeBodyId)) yield break;

        string responseText = null;
        string url = BaseUrl + "/bodies/" + _activeBodyId + "/zone-tiles/" + dimension;
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();

            if (_activeDimension != dimension) yield break; // superseded
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlanetSphereGoldberg] ZoneOverlay fetch échoué ({req.error}) pour '{dimension}'.");
                yield break;
            }
            responseText = req.downloadHandler.text;
        }

        var tileToZone = ParseZoneTilesJson(responseText);
        if (tileToZone == null || _activeDimension != dimension) yield break;

        _zoneTints      = BuildZoneTints(tileToZone, dimension, out int zoneCount);
        _tileToZoneId   = tileToZone;
        _zoneOverlayFetched = true;
        Debug.Log($"[PlanetSphereGoldberg] ZoneOverlay '{dimension}': {_zoneTints.Count} tuile(s), {zoneCount} zone(s).");
        ApplyZoneOverlayToMeshes();
    }

    /// <summary>Parses {"dimension":"x","tiles":{"tileId":"zoneId",...}} response.</summary>
    private static Dictionary<string, string> ParseZoneTilesJson(string json)
    {
        var result = new Dictionary<string, string>();
        try
        {
            int start = json.IndexOf("\"tiles\"");
            if (start < 0) return result;
            int braceStart = json.IndexOf('{', start + 7);
            int braceEnd   = json.LastIndexOf('}');
            if (braceStart < 0 || braceEnd <= braceStart) return result;
            string inner = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
            foreach (var pair in inner.Split(','))
            {
                var kv = pair.Split(':');
                if (kv.Length < 2) continue;
                string k = kv[0].Trim().Trim('"');
                string v = kv[1].Trim().Trim('"');
                if (!string.IsNullOrEmpty(k)) result[k] = v;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlanetSphereGoldberg] ZoneOverlay parse error: {e.Message}");
            return null;
        }
        return result;
    }

    /// <summary>Assigns per-zone colors derived from the dimension base color.</summary>
    private static Dictionary<string, Color> BuildZoneTints(
        Dictionary<string, string> tileToZone, string dimension, out int zoneCount)
    {
        if (!_dimensionBaseColor.TryGetValue(dimension, out Color baseColor))
            baseColor = Color.white;
        var tints  = new Dictionary<string, Color>();
        var cache  = new Dictionary<string, Color>();
        foreach (var kv in tileToZone)
        {
            if (!cache.TryGetValue(kv.Value, out Color zoneColor))
            {
                float hueOffset = (Mathf.Abs(kv.Value.GetHashCode()) % 100) / 100f * 0.25f - 0.125f;
                Color.RGBToHSV(baseColor, out float h, out float s, out float v);
                zoneColor = Color.HSVToRGB(Mathf.Repeat(h + hueOffset, 1f), s, v);
                cache[kv.Value] = zoneColor;
            }
            tints[kv.Key] = zoneColor;
        }
        zoneCount = cache.Count;
        return tints;
    }

    private void ApplyZoneOverlayToMeshes()
    {
        var meshesToRefresh = new[]
        {
            (enableLod && _sphereDataLo.faces != null) ? _sphereDataLo : _sphereData,
            _sphereDataHi,
        };
        foreach (var data in meshesToRefresh)
        {
            if (data.faces == null || data.mesh == null) continue;
            ReapplyOverlays(data, _cachedServerTiles);
        }
        if (_cachedMeshColors != null && _sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
    }
}
