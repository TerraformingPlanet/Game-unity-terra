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

        var loops = new System.Collections.Generic.List<(Vector3[], Color)>();

        // Corp borders — couleur de la corp (teinte ownership)
        if (_ownershipTints != null && _ownershipTints.Count > 0 && _tileToCorpId != null)
            loops.AddRange(GoldbergFaceColorizer.GetBoundaryLoops(
                _sphereData.faces, _cachedServerTiles, _ownershipTints, _tileToCorpId));

        // State borders (political map) — couleur de l'état (pas de recoloration des tuiles)
        if (_allStateTints != null && _allStateTints.Count > 0 && _tileToStateId != null)
        {
            var stateLoops = GoldbergFaceColorizer.GetBoundaryLoops(
                _sphereData.faces, _cachedServerTiles, _allStateTints, _tileToStateId);
            // Conserver la couleur de l'état retournée par GetBoundaryLoops
            loops.AddRange(stateLoops);
        }

        if (loops.Count > 0)
            _borderRenderer?.UpdateBorders(loops);
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
    /// Utilisé après chaque recolorisation biomes pour maintenir les teintes.
    /// </summary>
    private void ReapplyOverlays(GoldbergSphereGenerator.GoldbergMeshData meshData, GoldbergTileState[] serverTiles)
    {
        if (serverTiles == null) return;

        // Teintes état (political map) — PAS de recoloration des tuiles.
        // Les frontières d'état sont dessinées uniquement via OwnershipBorderRenderer (RebuildBorderLoops).
        // Les couleurs terrain (TerrainColorPalette) restent intactes.

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

        // Appliquer les couleurs finales au mesh
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

        // Build tileId → corp color + tileId → corpId maps
        var tints     = new Dictionary<string, Color>();
        var toCorpId  = new Dictionary<string, string>();
        foreach (OwnershipTileDto dto in tiles.items)
        {
            Color corpColor = new Color(dto.colorR, dto.colorG, dto.colorB, 1f);
            tints[dto.tileId]    = corpColor;
            toCorpId[dto.tileId] = dto.corpId;
        }
        Debug.Log($"[PlanetSphereGoldberg] Ownership: {tints.Count} tuile(s) à teinter sur ce corps.");

        if (tints.Count == 0) { _borderRenderer?.ClearBorders(); yield break; }
        _ownershipTints  = tints;
        _tileToCorpId    = toCorpId;

        // Dessiner les frontières depuis le mesh actif (LOD courant) — pas _sphereDataHi
        // pour éviter le mismatch « bordures trop petites au dezoom ».
        RebuildBorderLoops();

        string rendererStatus = _borderRenderer != null ? "OK" : "no renderer";

        if (debugLodVerbose)
            Debug.Log($"[OVERLAY] FetchOwnershipOverlay | tints={tints.Count} | renderer={rendererStatus}");

        Debug.Log($"[PlanetSphereGoldberg] Ownership overlay : {tints.Count} tuile(s), {rendererStatus}.");

        _ownershipOverlayFetched = true;
        ReapplyOverlays(_sphereData, _cachedServerTiles);
        // Resync snapshot hover — évite le flash vers couleurs pre-overlay au survol
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

            // Éviter fetch multiple (cause de couleurs changeantes)
            if (_stateOverlayFetched) yield break;

            // Map tileId → terrainType pour exclure précisément les tuiles d'eau du coloris
            var tileTerrain = new Dictionary<string, TerrainType>();
            if (_cachedServerTiles != null)
                foreach (GoldbergTileState t in _cachedServerTiles)
                    if (!string.IsNullOrEmpty(t.tileId))
                        tileTerrain[t.tileId] = t.terrainType;

            var stateColorCache = new Dictionary<string, Color>();
            var stateTints    = new Dictionary<string, Color>();  // coloris terre seulement
            var allStateTints = new Dictionary<string, Color>();  // tous tiles — pour GetBoundaryLoops
            var tileToStateId   = new Dictionary<string, string>(); // tous tiles (terre + eau)
            var tileToStateName = new Dictionary<string, string>(); // tous tiles — nom d'état
            foreach (StateTileColorDto entry in data.items)
            {
                if (string.IsNullOrEmpty(entry.tileId)) continue;
                Color col = new Color(entry.colorR, entry.colorG, entry.colorB, 1f);
                tileToStateId[entry.tileId]   = entry.stateId;
                tileToStateName[entry.tileId] = entry.stateName;
                allStateTints[entry.tileId] = col;                // TOUS les tiles
                // Exclure les tuiles d'eau du coloris (mais garder pour bordures)
                if (!tileTerrain.TryGetValue(entry.tileId, out TerrainType tt) || tt != TerrainType.Eau)
                    stateTints[entry.tileId] = col;               // coloris terre uniquement
            }

            _stateTints      = stateTints;
            _allStateTints   = allStateTints;
            _tileToStateId   = tileToStateId;
            _tileToStateName = tileToStateName;

            // Draw state boundary lines — pas de teinte de couleur, couleurs biomes conservées
            RebuildBorderLoops();

            _stateOverlayFetched = true;

            // Appliquer les couleurs d'état au mesh actif (comme FetchOwnershipOverlay le fait pour corp)
            ReapplyOverlays(_sphereData, _cachedServerTiles);
            // Resync snapshot hover — évite le flash vers couleurs pre-overlay au survol
            if (_sphereData.mesh != null)
                _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();

            Debug.Log($"[PlanetSphereGoldberg] State overlay : {stateTints.Count} tuile(s) colorées + bordures d'État.");

            if (debugLodVerbose)
                Debug.Log($"[OVERLAY] FetchStateOverlay | stateTints={stateTints.Count} | allStateTints={allStateTints.Count} | states={stateColorCache.Count} | tileToStateId={tileToStateId.Count}");
        }
    }
}
