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
    /// Une arête est une arête frontière si elle sépare une tuile claimée d'une tuile non-claimée
    /// (ou appartenant à une corpo différente). Les arêtes frontières sont ensuite chaînées
    /// en boucles continues (un ou plusieurs anneaux par corporation).
    ///
    /// Retourne une liste de (vertices_du_polygone, couleur) prête pour OwnershipBorderRenderer.
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

        // ── 1. tileId → index dans serverTiles ────────────────────────────────
        var tileIdxByTileId = new Dictionary<string, int>(serverTiles.Length);
        for (int j = 0; j < serverTiles.Length; j++)
        {
            string tid = serverTiles[j].tileId;
            if (!string.IsNullOrEmpty(tid) && !tileIdxByTileId.ContainsKey(tid))
                tileIdxByTileId[tid] = j;
        }

        // ── 2. tileId → face GP la plus proche (lat/lon nearest-neighbor, O(S × F)) ──
        var faceIdxByTileId = new Dictionary<string, int>(serverTiles.Length);
        for (int j = 0; j < serverTiles.Length; j++)
        {
            string tid = serverTiles[j].tileId;
            if (string.IsNullOrEmpty(tid)) continue;
            float tLat = serverTiles[j].latNorm, tLon = serverTiles[j].lonNorm;
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
            faceIdxByTileId[tid] = bestF;
        }

        // ── 3. Collecte les arêtes frontières groupées par corpId ─────────────
        var edgesByCorp  = new Dictionary<string, List<(Vector3, Vector3)>>();
        var colorByCorp  = new Dictionary<string, Color>();

        foreach (var kv in ownershipTints)
        {
            string tileId = kv.Key;
            if (!tileIdxByTileId.TryGetValue(tileId, out int si)) continue;
            if (!faceIdxByTileId.TryGetValue(tileId, out int fi)) continue;
            if (!tileToCorpId.TryGetValue(tileId, out string corpId)) continue;

            GoldbergTileState tile = serverTiles[si];
            var faceA = faces[fi];
            if (faceA.boundaryVertices == null || faceA.boundaryVertices.Length < 3) continue;

            if (!edgesByCorp.ContainsKey(corpId))
            {
                edgesByCorp[corpId] = new List<(Vector3, Vector3)>();
                colorByCorp[corpId] = kv.Value;
            }
            var edgeList = edgesByCorp[corpId];

            if (tile.neighborIds == null) continue;

            foreach (string nId in tile.neighborIds)
            {
                // Frontière : le voisin n'est pas dans la même corpo
                bool isBoundary = !tileToCorpId.TryGetValue(nId, out string nCorpId)
                               || nCorpId != corpId;
                if (!isBoundary) continue;

                // Trouver la face GP du voisin
                if (!faceIdxByTileId.TryGetValue(nId, out int fB)) continue;
                var faceB = faces[fB];
                if (faceB.boundaryVertices == null || faceB.boundaryVertices.Length < 3) continue;

                // Les 2 sommets partagés entre faceA et faceB = l'arête frontière
                Vector3 s1 = default, s2 = default;
                int found = 0;
                foreach (Vector3 va in faceA.boundaryVertices)
                {
                    foreach (Vector3 vb in faceB.boundaryVertices)
                    {
                        if ((va - vb).sqrMagnitude < 1e-6f)
                        {
                            if (found == 0) s1 = va;
                            else            s2 = va;
                            found++;
                            break;
                        }
                    }
                    if (found == 2) break;
                }
                if (found == 2)
                    edgeList.Add((s1, s2));
            }
        }

        // ── 4. Chaîner les arêtes en boucles continues par corpo ─────────────
        foreach (var kvCorp in edgesByCorp)
        {
            string corpId = kvCorp.Key;
            Color  color  = colorByCorp[corpId];
            var    loops  = ChainEdgesIntoLoops(kvCorp.Value);
            foreach (Vector3[] loop in loops)
                result.Add((loop, color));
        }

        return result;
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
