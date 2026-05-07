using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds an H3-exact sphere mesh from server GoldbergTileState data.
///
/// Replaces GoldbergSphereGenerator's nearest-neighbour approach:
/// each mesh face corresponds exactly to one H3 tile via its boundaryLatLonFlat vertices
/// (interleaved [lat0, lon0, lat1, lon1, ...], precomputed server-side by Python H3).
///
/// Output: GoldbergSphereGenerator.GoldbergMeshData-compatible struct so all existing
/// consumers (WaterCapsBuilder, GoldbergFaceColorizer, TilePrismsBuilder, etc.) work unchanged.
/// H3BuildResult also exposes faceToTileId[] and tileIdToFace for O(1) lookups (no more
/// nearest-neighbour approximation for clicks / overlay tints).
/// </summary>
public static class H3SphereBuilder
{
    // =========================================================
    // Result type
    // =========================================================

    /// <summary>
    /// Full result from Build(). meshData is GoldbergMeshData-compatible.
    /// faceToTileId[faceIndex] → server tileId string.
    /// tileIdToFace[tileId]    → faceIndex (O(1) lookup for click / overlay).
    /// </summary>
    public struct H3BuildResult
    {
        public GoldbergSphereGenerator.GoldbergMeshData meshData;
        public string[]                faceToTileId;
        public Dictionary<string, int> tileIdToFace;
    }

    // =========================================================
    // Public API
    // =========================================================

    /// <summary>
    /// Projects a lat/lon (degrees) onto the surface of a sphere of the given radius.
    /// Coordinate convention: lat=0 lon=0 → +X axis, north pole (+lat 90°) → +Y.
    /// </summary>
    public static Vector3 LatLonToSphere(float latDeg, float lonDeg, float radius)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon)
        ) * radius;
    }

    /// <summary>
    /// Builds an H3-exact sphere mesh from server tile data.
    ///
    /// Each tile must have boundaryLatLonFlat populated (interleaved lat,lon per boundary vertex).
    /// Tiles missing boundary data fall back to a degenerate single-point face at the centroid.
    ///
    /// altDisplacementScale: radial offset per altitude unit (0 = flat sphere, no relief).
    /// </summary>
    public static H3BuildResult Build(
        GoldbergTileState[] tiles,
        float radius             = GoldbergSphereGenerator.VisualRadius,
        float altDisplacementScale = 0f,
        float seaLevel           = float.NegativeInfinity)
    {
        int tileCount = tiles.Length;

        // Pre-project all boundaries. Ocean tiles (altitude < seaLevel) have their
        // top face placed at seaLevel so the water surface IS the tile geometry,
        // eliminating Z-fighting by construction.
        var allBoundaries = PreProjectAllBoundaries(tiles, radius, altDisplacementScale, seaLevel);

        // Flat sphere only: snap float32 micro-gaps between co-planar tile boundaries.
        // With relief: each tile is at its own altitude — snapping would deform faces by
        // pulling a high-altitude vertex toward a low-altitude neighbour. The prism walls
        // (TilePrismsBuilder) fill inter-tile gaps, so no snapping is needed.
        if (altDisplacementScale == 0f)
            SnapSharedVertices(allBoundaries);

        var faces            = new GoldbergSphereGenerator.GoldbergFace[tileCount];
        var faceToTileId     = new string[tileCount];
        var tileIdToFace     = new Dictionary<string, int>(tileCount);
        var vertices         = new List<Vector3>(tileCount * 8);
        var triangles        = new List<int>(tileCount * 18);
        var colors           = new List<Color>(tileCount * 8);
        var uvs              = new List<Vector2>(tileCount * 8);
        var uv1s             = new List<Vector2>(tileCount * 8);
        var vertexFaceIdList = new List<int>(tileCount * 8);

        for (int fi = 0; fi < tileCount; fi++)
        {
            faceToTileId[fi]               = tiles[fi].tileId;
            tileIdToFace[tiles[fi].tileId] = fi;
            float effectiveAlt = (altDisplacementScale != 0f && tiles[fi].altitude < seaLevel)
                                 ? seaLevel : tiles[fi].altitude;
            faces[fi] = AddTileMesh(fi, tiles[fi], effectiveAlt, radius, altDisplacementScale, allBoundaries[fi],
                                    vertices, triangles, colors, uvs, uv1s, vertexFaceIdList);
        }

        return AssembleResult(faces, faceToTileId, tileIdToFace,
                              vertices, triangles, colors, uvs, uv1s, vertexFaceIdList, tileCount);
    }

    // ── Per-tile mesh builder ──────────────────────────────────────────────────

    private static GoldbergSphereGenerator.GoldbergFace AddTileMesh(
        int fi, GoldbergTileState tile, float effectiveAlt, float radius, float altScale, Vector3[] boundary,
        List<Vector3> vertices, List<int> triangles, List<Color> colors,
        List<Vector2> uvs, List<Vector2> uv1s, List<int> vertexFaceIdList)
    {
        if (boundary == null || boundary.Length < 3)
            return BuildDegenerateFace(fi, tile, radius);

        int nV = boundary.Length;
        ComputeCentroid(boundary, radius, effectiveAlt, altScale,
                        out Vector3 centroid, out Vector3 centroidDir);
        Vector2 centroidUV = DirToUV(centroidDir);

        AddFanVertices(fi, centroid, boundary, centroidUV,
                       vertices, colors, uvs, uv1s, vertexFaceIdList, out int centroidIdx);
        AddFanTriangles(centroidIdx, nV, triangles);

        return new GoldbergSphereGenerator.GoldbergFace
        {
            faceId = fi, centroid3D = centroidDir,
            latDeg = tile.latDeg, lonDeg = tile.lonDeg,
            latNorm = tile.latNorm, lonNorm = tile.lonNorm,
            color = Color.gray, boundaryVertices = boundary,
        };
    }

    // ── Boundary pre-projection + vertex snapping ──────────────────────────────

    // Snap grid size in world units. At radius=10, 0.002 = 0.02% of sphere circumference.
    // All H3 boundary vertices within 0.001 units of a grid point map to the same position,
    // which covers any float32 rounding from Python→JSON→C# (typically < 0.00001 units).
    private const float kSnapGrid = 0.002f;

    private static Vector3[][] PreProjectAllBoundaries(
        GoldbergTileState[] tiles, float radius, float altScale, float seaLevel)
    {
        var result = new Vector3[tiles.Length][];
        for (int fi = 0; fi < tiles.Length; fi++)
        {
            float[] flat = tiles[fi].boundaryLatLonFlat;
            int nV = (flat != null) ? flat.Length / 2 : 0;
            if (nV < 3) { result[fi] = null; continue; }
            // Ocean tiles: clamp top face to seaLevel — water surface IS the tile geometry.
            float effectiveAlt = (altScale != 0f && tiles[fi].altitude < seaLevel)
                                 ? seaLevel : tiles[fi].altitude;
            result[fi] = ProjectBoundary(flat, nV, radius, effectiveAlt, altScale);
        }
        return result;
    }

    private static void SnapSharedVertices(Vector3[][] boundaries)
    {
        float invGrid = 1f / kSnapGrid;
        var canonical = new Dictionary<(int, int, int), Vector3>(boundaries.Length * 7);

        for (int ti = 0; ti < boundaries.Length; ti++)
        {
            if (boundaries[ti] == null) continue;
            for (int vi = 0; vi < boundaries[ti].Length; vi++)
            {
                Vector3 v = boundaries[ti][vi];
                var key = (
                    Mathf.RoundToInt(v.x * invGrid),
                    Mathf.RoundToInt(v.y * invGrid),
                    Mathf.RoundToInt(v.z * invGrid));
                if (!canonical.TryGetValue(key, out Vector3 snap))
                {
                    snap = v;
                    canonical[key] = snap;
                }
                boundaries[ti][vi] = snap;
            }
        }
    }

    private static Vector3[] ProjectBoundary(
        float[] flat, int nV, float radius, float altitude, float altScale)
    {
        var boundary = new Vector3[nV];
        for (int k = 0; k < nV; k++)
        {
            Vector3 pt = LatLonToSphere(flat[k * 2], flat[k * 2 + 1], radius);
            if (altScale != 0f) pt += pt.normalized * (altitude * altScale);
            boundary[k] = pt;
        }
        return boundary;
    }

    private static void ComputeCentroid(
        Vector3[] boundary, float radius, float altitude, float altScale,
        out Vector3 centroid, out Vector3 centroidDir)
    {
        Vector3 sum = Vector3.zero;
        for (int k = 0; k < boundary.Length; k++) sum += boundary[k];
        centroidDir = (sum / boundary.Length).normalized;
        centroid    = centroidDir * radius;
        if (altScale != 0f) centroid += centroidDir * (altitude * altScale);
    }

    private static void AddFanVertices(
        int fi, Vector3 centroid, Vector3[] boundary, Vector2 centroidUV,
        List<Vector3> vertices, List<Color> colors,
        List<Vector2> uvs, List<Vector2> uv1s, List<int> vertexFaceIdList,
        out int centroidIdx)
    {
        centroidIdx = vertices.Count;
        vertices.Add(centroid);  colors.Add(Color.gray);  vertexFaceIdList.Add(fi);
        uvs.Add(centroidUV);     uv1s.Add(centroidUV);

        for (int k = 0; k < boundary.Length; k++)
        {
            vertices.Add(boundary[k]);  colors.Add(Color.gray);  vertexFaceIdList.Add(fi);
            uvs.Add(DirToUV(boundary[k].normalized));  uv1s.Add(centroidUV);
        }
    }

    private static void AddFanTriangles(int centroidIdx, int nV, List<int> triangles)
    {
        // H3 boundary is CCW from outside; flip to CW for outward Unity normals.
        for (int k = 0; k < nV; k++)
        {
            triangles.Add(centroidIdx);
            triangles.Add(centroidIdx + 1 + (k + 1) % nV);
            triangles.Add(centroidIdx + 1 + k);
        }
    }


    // ── Mesh assembly ──────────────────────────────────────────────────────────

    private static H3BuildResult AssembleResult(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        string[] faceToTileId, Dictionary<string, int> tileIdToFace,
        List<Vector3> vertices, List<int> triangles, List<Color> colors,
        List<Vector2> uvs, List<Vector2> uv1s, List<int> vertexFaceIdList, int tileCount)
    {
        var mesh = new Mesh { name = "H3Sphere" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices    = vertices.ToArray();
        mesh.triangles   = triangles.ToArray();
        mesh.colors      = colors.ToArray();
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv1s);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return new H3BuildResult
        {
            meshData = new GoldbergSphereGenerator.GoldbergMeshData
            {
                mesh              = mesh,
                faces             = faces,
                vertexFaceId      = vertexFaceIdList.ToArray(),
                vertexCornerGroup = BuildCornerGroups(vertices),
                faceVertexIndices = BuildFaceVertexIndices(tileCount, vertexFaceIdList),
            },
            faceToTileId = faceToTileId,
            tileIdToFace = tileIdToFace,
        };
    }

    // ── Degenerate face fallback ───────────────────────────────────────────────

    private static GoldbergSphereGenerator.GoldbergFace BuildDegenerateFace(
        int fi, GoldbergTileState tile, float radius)
    {
        Vector3 c = LatLonToSphere(tile.latDeg, tile.lonDeg, radius);
        return new GoldbergSphereGenerator.GoldbergFace
        {
            faceId           = fi,
            centroid3D       = c.normalized,
            latDeg           = tile.latDeg,
            lonDeg           = tile.lonDeg,
            latNorm          = tile.latNorm,
            lonNorm          = tile.lonNorm,
            color            = Color.gray,
            boundaryVertices = new Vector3[] { c, c, c },
        };
    }

    private static Vector2 DirToUV(Vector3 dir)
    {
        return new Vector2(
            Mathf.Atan2(dir.z, dir.x) / (2f * Mathf.PI) + 0.5f,
            Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI + 0.5f);
    }

    /// <summary>
    /// Groups vertices by geometric position (snap 0.001 units).
    /// Shared corners between adjacent tiles receive the same group id,
    /// enabling smooth topographic displacement at tile edges.
    /// </summary>
    private static int[] BuildCornerGroups(List<Vector3> vertices)
    {
        const float SnapInv = 1000f;
        var keyToGroup = new Dictionary<(int, int, int), int>(vertices.Count);
        var result     = new int[vertices.Count];
        int nextGroup  = 0;

        for (int i = 0; i < vertices.Count; i++)
        {
            var key = (
                Mathf.RoundToInt(vertices[i].x * SnapInv),
                Mathf.RoundToInt(vertices[i].y * SnapInv),
                Mathf.RoundToInt(vertices[i].z * SnapInv));

            if (!keyToGroup.TryGetValue(key, out int grp))
            {
                grp = nextGroup++;
                keyToGroup[key] = grp;
            }
            result[i] = grp;
        }
        return result;
    }

    /// <summary>
    /// Precomputes per-face vertex index lists for O(1) access.
    /// Mirrors GoldbergSphereGenerator.BuildFaceVertexIndices().
    /// </summary>
    private static int[][] BuildFaceVertexIndices(int faceCount, List<int> vertexFaceIdList)
    {
        var counts = new int[faceCount];
        foreach (int fid in vertexFaceIdList) counts[fid]++;

        var result  = new int[faceCount][];
        for (int i = 0; i < faceCount; i++) result[i] = new int[counts[i]];

        var cursors = new int[faceCount];
        for (int vi = 0; vi < vertexFaceIdList.Count; vi++)
        {
            int fid = vertexFaceIdList[vi];
            result[fid][cursors[fid]++] = vi;
        }
        return result;
    }
}
