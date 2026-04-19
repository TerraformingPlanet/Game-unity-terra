using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Colorise les tuiles d'une sphère Goldberg depuis les données H3 du serveur de simulation.
/// Appelé après GoldbergSphereGenerator.Generate() et avant GoldbergSphereGenerator.ApplyFaceColors().
/// </summary>
public static class GoldbergFaceColorizer
{
    /// <summary>
    /// Recolorise les tuiles GP depuis un tableau de GoldbergTileState serveur.
    /// Nearest-neighbor lat/lon (distance² normalisée, wrap-around longitude).
    /// Seules les faces avec un terrainType connu dans colorByType sont modifiées.
    /// </summary>
    public static void ColorizeFromServerTiles(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles,
        Dictionary<TerrainType, Color> colorByType)
    {
        if (faces == null || serverTiles == null || serverTiles.Length == 0 || colorByType == null)
            return;

        for (int i = 0; i < faces.Length; i++)
        {
            float fLat = faces[i].latNorm;
            float fLon = faces[i].lonNorm;

            float bestDist2 = float.MaxValue;
            int   bestIdx   = 0;

            for (int j = 0; j < serverTiles.Length; j++)
            {
                float dLat = fLat - serverTiles[j].latNorm;
                float dLon = fLon - serverTiles[j].lonNorm;
                // wrap-around longitude [0,1]
                if (dLon >  0.5f) dLon -= 1f;
                if (dLon < -0.5f) dLon += 1f;
                float dist2 = dLat * dLat + dLon * dLon;
                if (dist2 < bestDist2)
                {
                    bestDist2 = dist2;
                    bestIdx   = j;
                }
            }

            if (colorByType.TryGetValue(serverTiles[bestIdx].terrainType, out Color c))
                faces[i].color = c;
        }
    }

    // ── Ownership overlay (Phase 7.1) ────────────────────────────────────────────

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
        var edgeMap = new Dictionary<string, (int f1, int f2, Vector3 va, Vector3 vb)>(faces.Length * 7);
        for (int fi = 0; fi < faces.Length; fi++)
        {
            Vector3[] bv = faces[fi].boundaryVertices;
            if (bv == null || bv.Length < 3) continue;
            int n = bv.Length;
            for (int k = 0; k < n; k++)
            {
                Vector3 va = bv[k];
                Vector3 vb = bv[(k + 1) % n];
                string key = MakeEdgeKey(va, vb);
                if (edgeMap.TryGetValue(key, out var entry))
                    edgeMap[key] = (entry.f1, fi, entry.va, entry.vb);
                else
                    edgeMap[key] = (fi, -1, va, vb);
            }
        }

        // ── B. tileId → lat/lon (lookup rapide) ───────────────────────────────
        var latLonByTile = new Dictionary<string, (float lat, float lon)>(serverTiles.Length);
        for (int j = 0; j < serverTiles.Length; j++)
        {
            string tid = serverTiles[j].tileId;
            if (!string.IsNullOrEmpty(tid) && !latLonByTile.ContainsKey(tid))
                latLonByTile[tid] = (serverTiles[j].latNorm, serverTiles[j].lonNorm);
        }

        // ── C. Marquer les faces Goldberg comme owned ──────────────────────────
        // Uniquement pour les tuiles H3 claimées (ownershipTints).  Nearest-neighbor lat/lon.
        var faceOwner = new Dictionary<int, string>();
        var faceColor = new Dictionary<int, Color>();

        foreach (var kv in ownershipTints)
        {
            string tileId = kv.Key;
            if (!tileToCorpId.TryGetValue(tileId, out string corpId)) continue;
            if (!latLonByTile.TryGetValue(tileId, out var ll)) continue;

            float tLat = ll.lat, tLon = ll.lon;
            float best = float.MaxValue; int bestF = 0;
            for (int i = 0; i < faces.Length; i++)
            {
                float dLat = faces[i].latNorm - tLat;
                float dLon = faces[i].lonNorm - tLon;
                if (dLon >  0.5f) dLon -= 1f;
                if (dLon < -0.5f) dLon += 1f;
                float d = dLat * dLat + dLon * dLon;
                if (d < best) { best = d; bestF = i; }
            }
            // Plusieurs tuiles H3 peuvent mapper vers la même face Goldberg — première gagne
            if (!faceOwner.ContainsKey(bestF))
            {
                faceOwner[bestF] = corpId;
                faceColor[bestF] = kv.Value;
            }
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

    // Clé canonique d'arête (indépendante de la direction de parcours).
    // Positions arrondies à 3 décimales — rayon ≈ 10u → précision 0.001u suffisante.
    private static string MakeEdgeKey(Vector3 va, Vector3 vb)
    {
        int ax = Mathf.RoundToInt(va.x * 1000), ay = Mathf.RoundToInt(va.y * 1000), az = Mathf.RoundToInt(va.z * 1000);
        int bx = Mathf.RoundToInt(vb.x * 1000), by = Mathf.RoundToInt(vb.y * 1000), bz = Mathf.RoundToInt(vb.z * 1000);
        if (ax > bx || (ax == bx && ay > by) || (ax == bx && ay == by && az > bz))
        { int t; t=ax;ax=bx;bx=t; t=ay;ay=by;by=t; t=az;az=bz;bz=t; }
        return $"{ax},{ay},{az}|{bx},{by},{bz}";
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

    /// <summary>
    /// Derives a stable, vibrant HSV color from a corporation UUID.
    /// The first 8 non-dash characters drive the hue; saturation and value are fixed.
    /// </summary>
    public static Color CorpColorFromId(string corpId)
    {
        if (string.IsNullOrEmpty(corpId)) return Color.white;
        int hash = 17;
        int count = 0;
        for (int i = 0; i < corpId.Length && count < 8; i++)
        {
            if (corpId[i] == '-') continue;
            hash = hash * 31 + corpId[i];
            count++;
        }
        float hue = (hash & 0x7FFFFFFF) % 360 / 360f;
        return Color.HSVToRGB(hue, 0.85f, 0.90f);
    }

    /// <summary>
    /// Applies ownership tint to corporation territory.
    /// Border tiles (at least one neighbor unowned or belonging to a different corp) receive
    /// a strong tint (borderBlend). Interior tiles receive a very faint tint (interiorBlend)
    /// so the underlying biome color and future buildings remain visible.
    ///
    /// ownershipTints : tileId → corp color (only tiles on the current body).
    /// tileToCorpId   : tileId → corpId, used to compare neighbors.
    /// borderBlend   : 0→1 lerp toward corp color for border tiles. 0.85f recommended.
    /// interiorBlend : 0→1 lerp toward corp color for interior tiles. 0.07f keeps biome visible.
    /// </summary>
    public static void ApplyOwnershipTint(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles,
        Dictionary<string, Color> ownershipTints,
        Dictionary<string, string> tileToCorpId,
        float borderBlend   = 0.85f,
        float interiorBlend = 0.07f)
    {
        if (faces == null || serverTiles == null || serverTiles.Length == 0
            || ownershipTints == null || ownershipTints.Count == 0
            || tileToCorpId == null)
            return;

        // Build tileId → server tile index for fast lookup
        var tileLookup = new Dictionary<string, int>(serverTiles.Length);
        for (int j = 0; j < serverTiles.Length; j++)
        {
            string tid = serverTiles[j].tileId;
            if (!string.IsNullOrEmpty(tid) && !tileLookup.ContainsKey(tid))
                tileLookup[tid] = j;
        }

        foreach (var kv in ownershipTints)
        {
            string tileId = kv.Key;
            if (!tileLookup.TryGetValue(tileId, out int tileIdx)) continue;

            // Border detection: is any neighbor unowned or owned by a different corp?
            bool isBorder = true;
            GoldbergTileState tile = serverTiles[tileIdx];
            tileToCorpId.TryGetValue(tileId, out string corpId);
            if (tile.neighborIds != null && tile.neighborIds.Length > 0)
            {
                isBorder = false;
                foreach (string nId in tile.neighborIds)
                {
                    if (!tileToCorpId.TryGetValue(nId, out string nCorpId) || nCorpId != corpId)
                    { isBorder = true; break; }
                }
            }

            float blendAmount = isBorder ? borderBlend : interiorBlend;
            float tLat        = tile.latNorm;
            float tLon        = tile.lonNorm;
            Color corpColor   = kv.Value;

            // Nearest-neighbor face search
            float bestDist2 = float.MaxValue;
            int   bestFace  = -1;
            for (int i = 0; i < faces.Length; i++)
            {
                float dLat = faces[i].latNorm - tLat;
                float dLon = faces[i].lonNorm - tLon;
                if (dLon >  0.5f) dLon -= 1f;
                if (dLon < -0.5f) dLon += 1f;
                float dist2 = dLat * dLat + dLon * dLon;
                if (dist2 < bestDist2) { bestDist2 = dist2; bestFace = i; }
            }

            if (bestFace >= 0)
                faces[bestFace].color = Color.Lerp(faces[bestFace].color, corpColor, blendAmount);
        }
    }
}
