using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

public partial class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // Détection de survol (hover highlight)
    // =========================================================

    private void OnMouseOver()
    {
        if (_sphereData.faces == null || _cachedMeshColors == null) return;
        if (Camera.main == null || Mouse.current == null) return;
        if (UIEventSystemUtility.IsPointerOverUI()) return;

        // Tooltip uniquement au LOD haut (LOD 1)
        if (_currentLodLevel != 1)
        {
            CancelTooltip();
        }

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        Vector3 dir     = (hit.point - transform.position).normalized;
        int     newFace = GoldbergSphereGenerator.FindNearestFaceId(_sphereData.faces, dir);

        if (newFace == _hoveredFaceId) return;

        // Restaure l'ancienne face et tinte la nouvelle
        Color[] meshColors = _sphereData.mesh.colors;
        RestoreFace(meshColors, _hoveredFaceId);
        TintFace(meshColors, newFace);
        _sphereData.mesh.colors = meshColors;
        _hoveredFaceId          = newFace;

        // Reset tooltip timer à chaque changement de face
        CancelTooltip();
        if (_currentLodLevel == 1)
        {
            _hoverFaceCandidate = newFace;
            _hoverStartTime     = Time.time;
        }
    }

    private void OnMouseExit()
    {
        CancelTooltip();
        if (_hoveredFaceId < 0 || _cachedMeshColors == null) return;
        Color[] meshColors = _sphereData.mesh.colors;
        RestoreFace(meshColors, _hoveredFaceId);
        _sphereData.mesh.colors = meshColors;
        _hoveredFaceId          = -1;
    }

    private void CancelTooltip()
    {
        _hoverFaceCandidate = -1;
        _hoverStartTime     = -1f;
        if (_hoverTooltipFired)
        {
            _hoverTooltipFired = false;
            OnTileHoverCancelled?.Invoke();
        }
    }

    private void FireTileTooltip(int faceId)
    {
        if (_cachedServerTiles == null || _cachedServerTiles.Length == 0) return;
        if (_sphereData.faces == null || faceId < 0 || faceId >= _sphereData.faces.Length) return;

        // Lazy-build la map faceId → GoldbergTileState
        if (_faceToTile == null)
            _faceToTile = GoldbergFaceColorizer.BuildFaceToTileMap(_sphereData.faces, _cachedServerTiles);

        if (!_faceToTile.TryGetValue(faceId, out GoldbergTileState tile)) return;

        // Assembler le texte du tooltip : terrain + tileId court
        string shortId = !string.IsNullOrEmpty(tile.tileId) && tile.tileId.Length > 8
            ? tile.tileId[..8] + "..."
            : tile.tileId ?? "?";
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{tile.terrainType}</b>  <size=9>[{shortId}]</size>");

        // Ajouter le nom de la corp si la tuile est revendiquée
        if (_tileToCorpId != null && _tileToCorpId.TryGetValue(tile.tileId, out string corpId))
            sb.Append($"\nCorp: {corpId[..Mathf.Min(8, corpId.Length)]}");

        // Ajouter icône construction si en cours
        if (_constructionTileIds != null && _constructionTileIds.Contains(tile.tileId))
            sb.Append("  \u2699");

        _hoverTooltipFired = true;
        Vector2 mousePos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : Vector2.zero;
        OnTileHoverReady?.Invoke(sb.ToString(), mousePos);
    }

    private void TintFace(Color[] meshColors, int faceId)
    {
        if (faceId < 0 || faceId >= _sphereData.faces.Length) return;
        for (int i = 0; i < _sphereData.vertexFaceId.Length; i++)
        {
            if (_sphereData.vertexFaceId[i] == faceId)
                meshColors[i] = Color.Lerp(_cachedMeshColors[i], Color.white, hoverTintColor.a);
        }
    }

    private void RestoreFace(Color[] meshColors, int faceId)
    {
        if (faceId < 0 || faceId >= _sphereData.faces.Length) return;
        for (int i = 0; i < _sphereData.vertexFaceId.Length; i++)
        {
            if (_sphereData.vertexFaceId[i] == faceId)
                meshColors[i] = _cachedMeshColors[i];
        }
    }

    // =========================================================
    // Détection de clic (sphérique, sans UV)
    // =========================================================

    private void OnMouseDown()
    {
        if (_sphereData.faces == null || _cachedMeshColors == null) return;
        if (Camera.main == null || Mouse.current == null) return;
        if (UIEventSystemUtility.IsPointerOverUI()) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        // Direction brute du raycast (utilisée uniquement pour le fallback)
        Vector3 rawDir = (hit.point - transform.position).normalized;

        // Utilise le centroïde de la face survolée (hover) si disponible,
        // sinon fallback sur la face la plus proche du point cliqué.
        // Garantit la cohérence entre la tuile highlightée et la tuile H3 résolue.
        int faceId = _hoveredFaceId >= 0
            ? _hoveredFaceId
            : GoldbergSphereGenerator.FindNearestFaceId(_sphereData.faces, rawDir);

        if (faceId < 0) return;

        Vector3 dir = _sphereData.faces[faceId].centroid3D; // direction normalisée depuis le centroïde

        float latDeg  = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lonDeg  = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
        float latNorm = (latDeg + 90f)  / 180f;
        float lonNorm = (lonDeg + 180f) / 360f;

        HexCell cell = GetProjectedCell(latNorm, lonNorm);

        LastClickedFaceId       = faceId;
        LastClickedFaceCentroid = dir * GoldbergSphereGenerator.VisualRadius;

        Debug.Log($"[PlanetSphereGoldberg] Clic → face={faceId} lat={latNorm:F3} lon={lonNorm:F3} (H3 en cours de résolution...)");

        // Résout la tuile H3 précise de façon asynchrone (non-bloquant)
        if (!string.IsNullOrEmpty(_activeBodyId))
            StartCoroutine(LookupH3TileAtClick(latDeg, lonDeg));

        OnRegionClicked?.Invoke(latNorm, lonNorm);
    }

    /// <summary>
    /// Swap vers le niveau LOD demandé (0 = bas, 1 = haut).
    /// Colorise le mesh haut avec res=2 si res=3 pas encore disponible.
    /// MeshCollider reste toujours sur LOD bas (492 faces, sous la limite convex hull 256 polys).
    /// </summary>
    private void ApplyLodLevel(int level)
    {
        // Invalider l'état tooltip au changement LOD
        _faceToTile         = null;
        _hoverFaceCandidate = -1;
        _hoverStartTime     = -1f;
        if (_hoverTooltipFired)
        {
            _hoverTooltipFired = false;
            OnTileHoverCancelled?.Invoke();
        }

        var nextData = level == 1 ? _sphereDataHi : _sphereDataLo;

        // LOD haut : colorise avec res=2 seulement si les biomes n'ont pas encore été appliqués
        // (_lodHiBaseColored est mis à true dans FetchAndColorizeFromServer dès que les tuiles sont appliquées)
        if (level == 1 && !_lodHiBaseColored && !_lodHiColored && _cachedServerTiles != null && _cachedColorByType != null)
        {
            GoldbergFaceColorizer.ColorizeFromServerTiles(nextData.faces, _cachedServerTiles, _cachedColorByType);
            ReapplyOverlays(nextData, _cachedServerTiles);
            _lodHiBaseColored = true;
        }
        else if (level == 1 && _lodHiBaseColored)
        {
            // Re-appliquer les overlays (ownership, states) sur les couleurs déjà présentes.
            // IMPORTANT : pas de condition !_lodHiColored — si FetchAndColorizeHiLod s'est terminé
            // avant FetchOwnershipOverlay/FetchStateOverlay (race condition), les overlays auraient
            // été appliqués avec des données null. On doit re-passer ici une fois les données reçues.
            ReapplyOverlays(nextData, _cachedServerTiles);
        }
        else if (level == 0 && _cachedServerTiles != null)
        {
            // Reapply overlays sur LOD bas — garantit que les tints récents (corp/état)
            // sont présents même si RefreshOwnershipOverlay s'est fait pendant LOD haut.
            ReapplyOverlays(nextData, _cachedServerTiles);
        }

        _sphereData       = nextData;
        _hoveredFaceId    = -1;
        _cachedMeshColors = (Color[])nextData.mesh.colors.Clone();

        _meshFilter.sharedMesh   = nextData.mesh;
        // Collider toujours sur LOD bas (492 faces → convex OK; 1962 faces → KO)
        _meshCollider.sharedMesh = _sphereDataLo.mesh;

        string label = level == 1 ? "HAUT" : "BAS";
        Debug.Log($"[PlanetSphereGoldberg] LOD → {label} ({nextData.faces.Length} faces) dist={cameraController?.OrbitDistance:F1}");

        if (debugLodVerbose)
            Debug.Log($"[LOD] ApplyLodLevel | level={level} | cachedTiles={_cachedServerTiles?.Length ?? 0} | ownershipTints={_ownershipTints?.Count ?? 0}");

        // Recalculer les frontières avec le nouveau mesh actif pour éviter le mismatch LOD.
        RebuildBorderLoops();

        // Lance le fetch res=3 en arrière-plan (no-op si déjà fait ou en cours)
        if (level == 1) TryStartHiLodFetch();
    }

    // =========================================================
    // Lookup H3 au clic
    // =========================================================

    private IEnumerator LookupH3TileAtClick(float latDeg, float lonDeg)
    {
        string url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/bodies/{1}/tiles/at?lat={2}&lon={3}",
            BaseUrl, _activeBodyId, latDeg, lonDeg);

        using UnityWebRequest req = UnityWebRequest.Get(url);
        req.timeout = TimeoutSec;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            yield break;

        GoldbergTileState tile;
        try   { tile = JsonUtility.FromJson<GoldbergTileState>(req.downloadHandler.text); }
        catch { yield break; }

        if (string.IsNullOrEmpty(tile.tileId))
            yield break;

        LastClickedH3TileId = tile.tileId;

        // Enrichir avec les infos d'état (overlay politique)
        if (_tileToStateId != null && _tileToStateId.TryGetValue(tile.tileId, out string sid))
            tile.stateId = sid;
        if (_tileToStateName != null && _tileToStateName.TryGetValue(tile.tileId, out string sname))
            tile.stateName = sname;

        Debug.Log($"[PlanetSphereGoldberg] H3 tile : {tile.tileId} | {tile.terrainType} | eau={tile.waterRatio:F2} | t={tile.temperature:F1}°C | state={tile.stateName}");
        OnH3TileResolved?.Invoke(tile);
    }
}
