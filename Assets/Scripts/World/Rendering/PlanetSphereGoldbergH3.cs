using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Partial class — H3-exact mesh building and colorization helpers.
/// Replaces the nearest-neighbour Goldberg polyhedron approach after tiles arrive from server.
/// Face i in every method corresponds to tile i (order preserved by H3SphereBuilder.Build).
/// </summary>
public partial class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // H3 mesh building
    // =========================================================

    /// <summary>Builds the H3-exact sphere mesh and wires it to all LOD slots and renderer components.</summary>
    private void RebuildH3MeshFromTiles(GoldbergTileState[] tiles)
    {
        // Bake altitude into the mesh when topographic relief is enabled.
        // Each tile's top face is at its own altitude → prism walls fill inter-tile gaps,
        // no vertex snapping needed, no post-hoc ApplyTopographicDisplacement required.
        float altScale = enableTopographicRelief ? topographicDisplacementScale : 0f;
        float seaLevel = altScale > 0f ? ActiveWaterLevel : float.NegativeInfinity;
        var result = H3SphereBuilder.Build(tiles, GoldbergSphereGenerator.VisualRadius, altScale, seaLevel);
        _h3Result  = result;

        // Snapshot old GP placeholder meshes before replacing references.
        var oldMeshes = new[] { _sphereData.mesh, _sphereDataLo.mesh, _sphereDataHi.mesh };

        // All LOD slots point to the same H3 mesh. Phase D will restore res=1/res=2 LODs.
        _sphereData   = result.meshData;
        _sphereDataLo = result.meshData;
        _sphereDataHi = result.meshData;

        _meshFilter.sharedMesh   = _sphereData.mesh;
        _meshCollider.sharedMesh = _sphereData.mesh;
        _lodHiBaseColored = true;   // H3 mesh IS the hi-lod mesh — skip redundant re-colorize.
        _lodHiColored     = false;
        _cachedMeshColors = null;   // Rebuilt by ApplyOverlaysAndNotify.
        _faceToTile       = null;   // Invalidated — rebuilt lazily from _h3Result.

        // Free stale GP placeholder meshes (new H3 mesh is now wired to the filter).
        foreach (var m in oldMeshes)
            if (m != null && m != _sphereData.mesh) Destroy(m);

        // Build tileId → tile lookup for O(1) click and tooltip resolution.
        _cachedTileById = new Dictionary<string, GoldbergTileState>(tiles.Length);
        foreach (var t in tiles) _cachedTileById[t.tileId] = t;
    }

    private void ApplyHiLodTopographic(GoldbergTileState[] tilesArray, UnityEngine.Mesh activeMesh)
    {
        bool sameMesh = _sphereDataHi.mesh == activeMesh;
        _cachedFaceAltitudesHi     = sameMesh ? _cachedFaceAltitudesLo : BuildH3FaceAltitudes(tilesArray, _sphereDataHi.faces.Length);
        _cachedFaceIsOceanHi       = sameMesh ? _cachedFaceIsOceanLo   : BuildH3FaceIsOcean(tilesArray, _sphereDataHi.faces.Length, ActiveWaterLevel);
        _cachedFaceIsInlandWaterHi = sameMesh ? _cachedFaceIsInlandWaterLo : BuildH3FaceIsInlandWater(tilesArray, _sphereDataHi.faces.Length);
        if (!sameMesh)
            GoldbergSphereGenerator.ApplyTopographicDisplacement(
                _sphereDataHi.mesh, _sphereDataHi.vertexFaceId, _cachedFaceAltitudesHi,
                topographicDisplacementScale, _sphereDataHi.vertexCornerGroup, ActiveWaterLevel);
    }

    private void BuildWaterCapsFromCache()
    {
        bool useHi = enableLod && _sphereDataHi.faces != null && _cachedFaceAltitudesHi != null;
        if (useHi)
            CreateWaterCaps(_sphereDataHi.faces, _cachedFaceAltitudesHi, ActiveWaterLevel, _cachedFaceIsOceanHi, _cachedFaceIsInlandWaterHi);
        else
        {
            var loFaces = enableLod && _sphereDataLo.faces != null ? _sphereDataLo.faces : _sphereData.faces;
            if (_cachedFaceAltitudesLo != null)
                CreateWaterCaps(loFaces, _cachedFaceAltitudesLo, ActiveWaterLevel, _cachedFaceIsOceanLo, _cachedFaceIsInlandWaterLo);
        }
    }

    /// <summary>
    /// Rebuilds the H3 mesh geometry (ocean tile top faces clamped to ActiveWaterLevel),
    /// recolorizes, and regenerates depth prisms. Called when the simulation seaLevel changes.
    /// </summary>
    internal void RebuildWaterCapsGeometry()
    {
        if (_cachedServerTiles == null) return;
        RebuildH3MeshFromTiles(_cachedServerTiles);
        ReapplyOverlays(_sphereData, _cachedServerTiles);
        if (_sphereData.mesh != null)
            _cachedMeshColors = (Color[])_sphereData.mesh.colors.Clone();
        ApplyTopographicReliefFromTiles(_cachedServerTiles);
    }

    // =========================================================
    // H3-exact colorization helpers (no nearest-neighbour)
    // =========================================================

    private static void ColorizeH3Exact(
        GoldbergSphereGenerator.GoldbergFace[] faces, GoldbergTileState[] tiles, float waterLevel)
    {
        int n = Mathf.Min(faces.Length, tiles.Length);
        for (int i = 0; i < n; i++)
        {
            float relAlt = tiles[i].altitude - waterLevel;
            // Ocean tiles: their top face is at seaLevel in the mesh — show water depth color.
            faces[i].color = relAlt < 0f
                ? WaterCapsBuilder.WaterDepthColor(tiles[i].altitude, waterLevel)
                : GoldbergFaceColorizer.AltitudeToColor(relAlt);
        }
    }

    private static void ColorizeH3ElevationLens(
        GoldbergSphereGenerator.GoldbergFace[] faces, GoldbergTileState[] tiles)
    {
        int n = Mathf.Min(faces.Length, tiles.Length);
        for (int i = 0; i < n; i++)
            faces[i].color = GoldbergFaceColorizer.ElevationLensColor(tiles[i].altitude);
    }

    // =========================================================
    // H3-exact altitude / water classification arrays
    // =========================================================

    private static float[] BuildH3FaceAltitudes(GoldbergTileState[] tiles, int faceCount)
    {
        var a = new float[faceCount];
        for (int i = 0; i < Mathf.Min(faceCount, tiles.Length); i++) a[i] = tiles[i].altitude;
        return a;
    }

    private static bool[] BuildH3FaceIsOcean(GoldbergTileState[] tiles, int faceCount, float waterLevel)
    {
        var m = new bool[faceCount];
        for (int i = 0; i < Mathf.Min(faceCount, tiles.Length); i++)
        {
            // Garde pure précision float : une tile est ocean ssi altitude <= seaLevel.
            // L'epsilon 0.0002 couvre uniquement les erreurs d'arrondi float32 (< 1 palier).
            // La WaterSphere utilise le même kEpsilonAlt * scale → même frontière visuelle.
            const float kAltEps = 0.0002f;
            if (tiles[i].altitude > waterLevel + kAltEps) continue;
            var wc = tiles[i].waterClassification;
            m[i] = wc == WaterClassification.OpenOcean
                || wc == WaterClassification.FrozenWater
                || (tiles[i].terrainType == TerrainType.Eau
                    && wc != WaterClassification.InlandWater
                    && wc != WaterClassification.InlandSea);
        }
        return m;
    }

    private static bool[] BuildH3FaceIsInlandWater(GoldbergTileState[] tiles, int faceCount)
    {
        var m = new bool[faceCount];
        for (int i = 0; i < Mathf.Min(faceCount, tiles.Length); i++)
            m[i] = tiles[i].waterClassification == WaterClassification.InlandWater
                || tiles[i].waterClassification == WaterClassification.InlandSea
                || tiles[i].lakeVolume > 0f;
        return m;
    }

    // =========================================================
    // Seuils discrets du niveau d'eau
    // =========================================================

    /// <summary>
    /// Retourne les altitudes triées uniques de toutes les tiles chargées.
    ///
    /// Utilisation : slider de terraforming peut appeler SnapToWaterThreshold()
    /// pour placer le sea level exactement à la frontière d'un palier de tile.
    /// Garantit que les tiles océaniques sont recouverte entièrement par le seaLevel, jamais à moitié.
    ///
    /// Les valeurs sont en espace altitude serveur [-1, +1].
    /// </summary>
    public float[] ComputeWaterThresholds()
    {
        if (_cachedServerTiles == null || _cachedServerTiles.Length == 0)
            return new float[] { -1f, 0f, 1f };

        // Collecter les altitudes uniques (arrondi à 4 décimales pour dédupliquer les flottants proches).
        var set = new System.Collections.Generic.HashSet<float>();
        foreach (var t in _cachedServerTiles)
            set.Add(Mathf.Round(t.altitude * 10000f) / 10000f);

        var arr = new float[set.Count];
        set.CopyTo(arr);
        System.Array.Sort(arr);
        return arr;
    }

    /// <summary>
    /// Snap le niveau d'eau demandé vers le seuil de tile le plus proche par en-dessous.
    /// Le sea level snappé garantit : toutes les tiles ≤ snapped sont sous l'eau,
    /// toutes les tiles > snapped sont émergées.
    /// </summary>
    public float SnapToWaterThreshold(float requestedLevel)
    {
        float[] thresholds = ComputeWaterThresholds();
        if (thresholds.Length == 0) return requestedLevel;

        // Trouver le plus grand seuil ≤ requestedLevel.
        float snapped = thresholds[0];
        foreach (float t in thresholds)
        {
            if (t <= requestedLevel) snapped = t;
            else break;
        }
        return snapped;
    }
}
