using System.Collections.Generic;
using UnityEngine;

// Boundary loop computation extracted to keep GoldbergFaceColorizer.cs under 500 lines.
public static partial class GoldbergFaceColorizer
{
    // ── Ownership overlay — boundary loops (Phase 7.1) ───────────────────────

    /// <summary>
    /// Calcule les boucles de frontière à dessiner pour chaque territoire de corporation.
    ///
    /// Algorithme — travaille entièrement en espace Goldberg (jamais en espace H3) :
    ///   A. Edge map : pour chaque face Goldberg, indexe ses arêtes → (faceId1, faceId2).
    ///      Sur une sphère fermée, chaque arête est partagée par exactement 2 faces.
    ///   B. Mappe chaque tuile H3 claimée vers la face Goldberg la plus proche (lat/lon).
    ///   C. Arête frontière = ses 2 faces n'appartiennent pas au même corp (null = non claimé).
    ///   D. Chaîne les arêtes frontières en boucles continues par corporation.
    ///
    /// Retourne une liste de (vertices, couleur) prête pour OwnershipBorderRenderer.
    /// </summary>
    public static List<(Vector3[] pts, Color col)> GetBoundaryLoops(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles,
        Dictionary<string, Color> ownershipTints,
        Dictionary<string, string> tileToCorpId)
    {
        var result = new List<(Vector3[], Color)>();
        if (faces == null || serverTiles == null || ownershipTints == null || tileToCorpId == null
            || faces.Length == 0 || serverTiles.Length == 0 || ownershipTints.Count == 0)
            return result;

        // ── A. Edge map : arête canonique → (faceId1, faceId2, va, vb) ────────
        var edgeMap = new Dictionary<EdgeKey, (int f1, int f2, Vector3 va, Vector3 vb)>(faces.Length * 7);
        for (int fi = 0; fi < faces.Length; fi++)
        {
            Vector3[] bv = faces[fi].boundaryVertices;
            if (bv == null || bv.Length < 3) continue;
            int n = bv.Length;
            for (int k = 0; k < n; k++)
            {
                Vector3 va = bv[k];
                Vector3 vb = bv[(k + 1) % n];
                var key = new EdgeKey(va, vb);
                if (edgeMap.TryGetValue(key, out var entry))
                    edgeMap[key] = (entry.f1, fi, entry.va, entry.vb);
                else
                    edgeMap[key] = (fi, -1, va, vb);
            }
        }

        // ── B. (inchangé — latLonByTile supprimé, non nécessaire dans le nouvel algo) ──

        // ── C. Marquer les faces Goldberg comme owned ──────────────────────────
        // Pour chaque face Goldberg, trouve la tuile H3 la plus proche (nearest-neighbor).
        // Si cette tuile est claimée, la face hérite de la propriété.
        // Cela garantit que TOUTES les faces d'un territoire sont marquées —
        // pas seulement les faces correspondant au centroïde d'une tuile H3.
        var faceOwner = new Dictionary<int, string>(faces.Length);
        var faceColor = new Dictionary<int, Color>(faces.Length);

        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int fi = 0; fi < faces.Length; fi++)
        {
            int bestJ = FindNearestTile(faces[fi].centroid3D, tileDirs);

            string tileId = serverTiles[bestJ].tileId;
            if (string.IsNullOrEmpty(tileId)) continue;
            if (!tileToCorpId.TryGetValue(tileId, out string corpId)) continue;
            if (!ownershipTints.TryGetValue(tileId, out Color col)) continue;

            faceOwner[fi] = corpId;
            faceColor[fi] = col;
        }

        // ── D. Arêtes frontières ───────────────────────────────────────────────
        var edgesByCorp = new Dictionary<string, List<(Vector3, Vector3)>>();
        var colorByCorp = new Dictionary<string, Color>();

        foreach (var edge in edgeMap.Values)
        {
            if (edge.f2 < 0) continue; // bord libre (impossible sur sphère fermée)

            faceOwner.TryGetValue(edge.f1, out string corp1);
            faceOwner.TryGetValue(edge.f2, out string corp2);

            if (corp1 == corp2) continue; // même corp ou les deux unowned

            if (corp1 != null)
            {
                if (!edgesByCorp.ContainsKey(corp1)) { edgesByCorp[corp1] = new List<(Vector3, Vector3)>(); colorByCorp[corp1] = faceColor[edge.f1]; }
                edgesByCorp[corp1].Add((edge.va, edge.vb));
            }
            if (corp2 != null)
            {
                if (!edgesByCorp.ContainsKey(corp2)) { edgesByCorp[corp2] = new List<(Vector3, Vector3)>(); colorByCorp[corp2] = faceColor[edge.f2]; }
                edgesByCorp[corp2].Add((edge.va, edge.vb));
            }
        }

        // ── E. Chaîner en boucles continues ───────────────────────────────────
        foreach (var kvCorp in edgesByCorp)
        {
            Color color = colorByCorp[kvCorp.Key];
            foreach (Vector3[] loop in ChainEdgesIntoLoops(kvCorp.Value))
                result.Add((loop, color));
        }

        return result;
    }

    // Clé canonique d'arête — struct value type évite l'allocation d'une string par arête.
    // Positions arrondies à 3 décimales — rayon ≈ 10u → précision 0.001u suffisante.
    private readonly struct EdgeKey : System.IEquatable<EdgeKey>
    {
        private readonly int _ax, _ay, _az, _bx, _by, _bz;

        public EdgeKey(Vector3 va, Vector3 vb)
        {
            int ax = Mathf.RoundToInt(va.x * 1000), ay = Mathf.RoundToInt(va.y * 1000), az = Mathf.RoundToInt(va.z * 1000);
            int bx = Mathf.RoundToInt(vb.x * 1000), by = Mathf.RoundToInt(vb.y * 1000), bz = Mathf.RoundToInt(vb.z * 1000);
            if (ax > bx || (ax == bx && ay > by) || (ax == bx && ay == by && az > bz))
            { int t; t=ax;ax=bx;bx=t; t=ay;ay=by;by=t; t=az;az=bz;bz=t; }
            _ax=ax; _ay=ay; _az=az; _bx=bx; _by=by; _bz=bz;
        }

        public bool Equals(EdgeKey o) =>
            _ax==o._ax && _ay==o._ay && _az==o._az && _bx==o._bx && _by==o._by && _bz==o._bz;

        public override bool Equals(object obj) => obj is EdgeKey o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + _ax; h = h * 31 + _ay; h = h * 31 + _az;
                h = h * 31 + _bx; h = h * 31 + _by; h = h * 31 + _bz;
                return h;
            }
        }
    }

    /// <summary>
    /// Chaîne une liste d'arêtes non ordonnées en boucles continues.
    /// Utilise un matching greedy avec epsilon de 1e-6 (sqrMagnitude).
    /// </summary>
    private static List<Vector3[]> ChainEdgesIntoLoops(List<(Vector3 a, Vector3 b)> edges)
    {
        var remaining = new List<(Vector3, Vector3)>(edges);
        var loops     = new List<Vector3[]>();

        while (remaining.Count > 0)
        {
            var (startA, startB) = remaining[0];
            remaining.RemoveAt(0);

            var chain = new List<Vector3> { startA, startB };
            Vector3 tail = startB;

            bool extended;
            do
            {
                extended = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    var (ea, eb) = remaining[i];
                    if ((ea - tail).sqrMagnitude < 1e-6f)
                    { chain.Add(eb); tail = eb; remaining.RemoveAt(i); extended = true; break; }
                    if ((eb - tail).sqrMagnitude < 1e-6f)
                    { chain.Add(ea); tail = ea; remaining.RemoveAt(i); extended = true; break; }
                }
            } while (extended);

            if (chain.Count >= 2)
                loops.Add(chain.ToArray());
        }
        return loops;
    }
}
