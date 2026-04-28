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

    /// <summary>
    /// Construit un dictionnaire faceId → GoldbergTileState (nearest-neighbor lat/lon).
    /// Utilisé pour résoudre la tuile serveur d'une face au survol (tooltip).
    /// Même algorithme que ColorizeFromServerTiles.
    /// </summary>
    public static Dictionary<int, GoldbergTileState> BuildFaceToTileMap(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles)
    {
        var map = new Dictionary<int, GoldbergTileState>(faces?.Length ?? 0);
        if (faces == null || serverTiles == null || serverTiles.Length == 0)
            return map;

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
                if (dLon >  0.5f) dLon -= 1f;
                if (dLon < -0.5f) dLon += 1f;
                float dist2 = dLat * dLat + dLon * dLon;
                if (dist2 < bestDist2)
                {
                    bestDist2 = dist2;
                    bestIdx   = j;
                }
            }

            map[i] = serverTiles[bestIdx];
        }

        return map;
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

        // ── B. (inchangé — latLonByTile supprimé, non nécessaire dans le nouvel algo) ──

        // ── C. Marquer les faces Goldberg comme owned ──────────────────────────
        // Pour chaque face Goldberg, trouve la tuile H3 la plus proche (nearest-neighbor).
        // Si cette tuile est claimée, la face hérite de la propriété.
        // Cela garantit que TOUTES les faces d'un territoire sont marquées —
        // pas seulement les faces correspondant au centroïde d'une tuile H3.
        var faceOwner = new Dictionary<int, string>(faces.Length);
        var faceColor = new Dictionary<int, Color>(faces.Length);

        for (int fi = 0; fi < faces.Length; fi++)
        {
            float fLat = faces[fi].latNorm;
            float fLon = faces[fi].lonNorm;

            float bestDist = float.MaxValue;
            int   bestJ    = 0;
            for (int j = 0; j < serverTiles.Length; j++)
            {
                float dLat = fLat - serverTiles[j].latNorm;
                float dLon = fLon - serverTiles[j].lonNorm;
                if (dLon >  0.5f) dLon -= 1f;
                if (dLon < -0.5f) dLon += 1f;
                float d = dLat * dLat + dLon * dLon;
                if (d < bestDist) { bestDist = d; bestJ = j; }
            }

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

        // Itère sur chaque face Goldberg (même sens que GetBoundaryLoops : Goldberg→H3).
        // Garantit que face teintée = face incluse dans la frontière (pas de décalage).
        for (int fi = 0; fi < faces.Length; fi++)
        {
            float fLat = faces[fi].latNorm;
            float fLon = faces[fi].lonNorm;

            // Nearest-neighbor H3 tile depuis la face
            float bestDist2 = float.MaxValue;
            int   bestJ     = -1;
            for (int j = 0; j < serverTiles.Length; j++)
            {
                float dLat = fLat - serverTiles[j].latNorm;
                float dLon = fLon - serverTiles[j].lonNorm;
                if (dLon >  0.5f) dLon -= 1f;
                if (dLon < -0.5f) dLon += 1f;
                float d = dLat * dLat + dLon * dLon;
                if (d < bestDist2) { bestDist2 = d; bestJ = j; }
            }

            if (bestJ < 0) continue;

            string tileId = serverTiles[bestJ].tileId;
            if (!ownershipTints.TryGetValue(tileId, out Color corpColor)) continue;
            if (!tileToCorpId.TryGetValue(tileId, out string corpId)) continue;

            // Border detection: au moins un voisin H3 n'appartient pas au même corp
            GoldbergTileState tile = serverTiles[bestJ];
            bool isBorder = true;
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
            faces[fi].color = Color.Lerp(faces[fi].color, corpColor, blendAmount);
        }
    }
}
