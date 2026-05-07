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
        // Ignorer tout collider autre que le mesh terrain (WaterSphere, etc.)
        if (hit.collider != _meshCollider) return;

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

        // Lazy-build la map faceId → GoldbergTileState (H3-exact — no nearest-neighbour).
        if (_faceToTile == null)
            _faceToTile = BuildH3FaceToTileMap();

        if (!_faceToTile.TryGetValue(faceId, out GoldbergTileState tile)) return;

        // Assembler le texte du tooltip : terrain + tileId court
        string shortId = !string.IsNullOrEmpty(tile.tileId) && tile.tileId.Length > 8
            ? tile.tileId[..8] + "..."
            : tile.tileId ?? "?";
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{tile.terrainType}</b>  <size=9>[{shortId}]</size>");

        // Altitude debug : valeur absolue + position relative au sea level
        float seaLvl   = ServerWaterLevel;
        float relAlt   = tile.altitude - seaLvl;
        string relStr  = relAlt >= 0f ? $"+{relAlt:F3}" : $"{relAlt:F3}";
        string isOcean = tile.altitude < seaLvl ? " 🌊" : "";
        sb.Append($"\n<size=9>alt={tile.altitude:F3}  sea={seaLvl:F3}  Δ={relStr}{isOcean}</size>");

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

    private Dictionary<int, GoldbergTileState> BuildH3FaceToTileMap()
    {
        if (_h3Result.faceToTileId != null && _cachedTileById != null)
        {
            var map = new Dictionary<int, GoldbergTileState>(_h3Result.faceToTileId.Length);
            for (int fi = 0; fi < _h3Result.faceToTileId.Length; fi++)
                if (_cachedTileById.TryGetValue(_h3Result.faceToTileId[fi], out var tile))
                    map[fi] = tile;
            return map;
        }
        // Pre-H3 fallback (GP placeholder mesh, before first tile fetch).
        return GoldbergFaceColorizer.BuildFaceToTileMap(_sphereData.faces, _cachedServerTiles);
    }

    private void TintFace(Color[] meshColors, int faceId)
    {
        if (faceId < 0 || _sphereData.faceVertexIndices == null
            || faceId >= _sphereData.faceVertexIndices.Length) return;
        foreach (int i in _sphereData.faceVertexIndices[faceId])
            meshColors[i] = Color.Lerp(_cachedMeshColors[i], Color.white, hoverTintColor.a);
    }

    private void RestoreFace(Color[] meshColors, int faceId)
    {
        if (faceId < 0 || _sphereData.faceVertexIndices == null
            || faceId >= _sphereData.faceVertexIndices.Length) return;
        foreach (int i in _sphereData.faceVertexIndices[faceId])
            meshColors[i] = _cachedMeshColors[i];
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
        InvalidateLodHoverState();

        var nextData = level == 1 ? _sphereDataHi : _sphereDataLo;
        UpdateLodColorization(nextData, level);

        _sphereData       = nextData;
        _hoveredFaceId    = -1;
        _cachedMeshColors = (Color[])nextData.mesh.colors.Clone();

        ApplyTopographicOnLodSwap(nextData, level);

        _meshFilter.sharedMesh   = nextData.mesh;
        _meshCollider.sharedMesh = nextData.mesh;

        string label = level == 1 ? "HAUT" : "BAS";
        Debug.Log($"[PlanetSphereGoldberg] LOD \u2192 {label} ({nextData.faces.Length} faces) dist={cameraController?.OrbitDistance:F1}");

        if (debugLodVerbose)
            Debug.Log($"[LOD] ApplyLodLevel | level={level} | cachedTiles={_cachedServerTiles?.Length ?? 0} | ownershipTints={_ownershipTints?.Count ?? 0}");

        RebuildBorderLoops();
        if (level == 1) TryStartHiLodFetch();
    }

    private void InvalidateLodHoverState()
    {
        _faceToTile         = null;
        _hoverFaceCandidate = -1;
        _hoverStartTime     = -1f;
        if (_hoverTooltipFired)
        {
            _hoverTooltipFired = false;
            OnTileHoverCancelled?.Invoke();
        }
    }

    private void UpdateLodColorization(GoldbergSphereGenerator.GoldbergMeshData nextData, int level)
    {
        if (level == 1 && !_lodHiBaseColored && !_lodHiColored && _cachedServerTiles != null)
        {
            ColorizeH3Exact(nextData.faces, _cachedServerTiles, ActiveWaterLevel);
            ReapplyOverlays(nextData, _cachedServerTiles);
            _lodHiBaseColored = true;
        }
        else if (level == 1 && _lodHiBaseColored)
        {
            ReapplyOverlays(nextData, _cachedServerTiles);
        }
        else if (level == 0 && _cachedServerTiles != null)
        {
            ReapplyOverlays(nextData, _cachedServerTiles);
        }
    }

    private void ApplyTopographicOnLodSwap(GoldbergSphereGenerator.GoldbergMeshData nextData, int level)
    {
        if (!enableTopographicRelief) return;
        // H3 mode: altitude is baked into the mesh by Build() — post-hoc displacement would double-apply.
        if (_h3Result.faceToTileId != null) return;
        float[] altitudes = level == 1 ? _cachedFaceAltitudesHi : _cachedFaceAltitudesLo;
        if (altitudes != null)
            GoldbergSphereGenerator.ApplyTopographicDisplacement(
                nextData.mesh, nextData.vertexFaceId, altitudes,
                topographicDisplacementScale, nextData.vertexCornerGroup, ActiveWaterLevel);
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

    // =========================================================
    // Update — lens toggle + LOD + tooltip timer
    // =========================================================

    private void Update()
    {
        // Touche L : cycle lens Normal ↔ Elevation (debug)
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            CycleLens();

        if (!enableLod) return;
        if (cameraController == null) return;
        // Le swap LOD doit rester possible même avant la réception complète des tuiles H3 serveur.
        if (_sphereDataLo.faces == null || _sphereDataHi.faces == null) return;

        float dist = cameraController.OrbitDistance;

        // Hysteresis : deux seuils pour éviter le flickering
        int targetLod;
        if (_currentLodLevel <= 0)
            targetLod = dist < lodNearDistance ? 1 : 0;
        else
            targetLod = dist > lodFarDistance  ? 0 : 1;

        if (targetLod != _currentLodLevel)
        {
            _currentLodLevel = targetLod;
            ApplyLodLevel(_currentLodLevel);
        }

        // Tooltip timer (LOD 1 seulement)
        if (!_hoverTooltipFired && _hoverFaceCandidate >= 0 && _currentLodLevel == 1
            && Time.time - _hoverStartTime >= hoverTooltipDelay)
        {
            FireTileTooltip(_hoverFaceCandidate);
        }

    }

    // =========================================================
    // Face utilities (used by Input, Overlay, and TangentView)
    // =========================================================

    /// <summary>Retourne le rayon de la face GP (distance max centroïde→vertex, espace monde).</summary>
    public float GetFaceRadius(int faceId)
    {
        if (faceId < 0 || _sphereData.faces == null || faceId >= _sphereData.faces.Length
            || _sphereData.mesh == null) return 1f;
        Vector3 centroidWorld = transform.TransformPoint(_sphereData.faces[faceId].centroid3D * GoldbergSphereGenerator.VisualRadius);
        Vector3[] verts  = _sphereData.mesh.vertices;
        float     maxDist = 0f;
        if (_sphereData.faceVertexIndices != null && faceId < _sphereData.faceVertexIndices.Length)
        {
            foreach (int i in _sphereData.faceVertexIndices[faceId])
            {
                float d = Vector3.Distance(centroidWorld, transform.TransformPoint(verts[i]));
                if (d > maxDist) maxDist = d;
            }
        }
        return maxDist > 0.0001f ? maxDist : 1f;
    }

    /// <summary>Masque une face GP (alpha=0) pour la remplacer visuellement par la grille hex.</summary>
    public void HideFaceOnSphere(int faceId)
    {
        if (faceId < 0 || _sphereData.faces == null || faceId >= _sphereData.faces.Length
            || _sphereData.mesh == null) return;
        Color[] meshColors = _sphereData.mesh.colors;
        if (_sphereData.faceVertexIndices != null && faceId < _sphereData.faceVertexIndices.Length)
        {
            foreach (int i in _sphereData.faceVertexIndices[faceId])
            {
                Color c = meshColors[i]; c.a = 0f; meshColors[i] = c;
            }
        }
        _sphereData.mesh.colors = meshColors;
    }

    /// <summary>Restaure les couleurs originales d'une face GP après fermeture de la vue locale.</summary>
    public void RestoreFaceOnSphere(int faceId)
    {
        if (faceId < 0 || _cachedMeshColors == null || _sphereData.mesh == null) return;
        Color[] meshColors = _sphereData.mesh.colors;
        RestoreFace(meshColors, faceId);
        _sphereData.mesh.colors = meshColors;
    }
}
