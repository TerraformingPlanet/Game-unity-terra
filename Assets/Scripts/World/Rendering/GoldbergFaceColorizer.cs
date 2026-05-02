using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Colorise les tuiles d'une sphère Goldberg depuis les données H3 du serveur de simulation.
/// Appelé après GoldbergSphereGenerator.Generate() et avant GoldbergSphereGenerator.ApplyFaceColors().
/// </summary>
public static class GoldbergFaceColorizer
{
    // ── Gradient altitude ─────────────────────────────────────────────────────

    /// <summary>
    /// Colore chaque face selon l'altitude relative à waterLevel (altitude - waterLevel).
    ///
    /// relAlt ∈ [-1, 0[  → fond marin (immergé, caché par la WaterSphere)
    /// relAlt ∈ [0, 0.25] → plaines / côtes
    /// relAlt ∈ [0.25, 0.6] → collines
    /// relAlt ∈ [0.6, 1]   → montagnes → neige au sommet
    ///
    /// La WaterSphere couvre tout ce qui est en-dessous de waterLevel — les tuiles
    /// immergées reçoivent quand même une couleur fond marin au cas où la sphère
    /// deviendrait transparente ou absente.
    /// </summary>
    public static void ColorizeFromAltitude(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles,
        float waterLevel = 0f)
    {
        if (faces == null || serverTiles == null || serverTiles.Length == 0)
            return;

        for (int i = 0; i < faces.Length; i++)
        {
            int bestIdx = FindNearestTile(faces[i].latNorm, faces[i].lonNorm, serverTiles);
            float relAlt = serverTiles[bestIdx].altitude - waterLevel;  // hauteur par rapport à la mer
            faces[i].color = AltitudeToColor(relAlt);
        }
    }

    /// <summary>
    /// Gradient altitude → couleur. relAlt = altitude - waterLevel ∈ [-2, 2] (clampé [-1,1]).
    /// Palette "planète terrestre" — peut être remplacée par une ScriptableObject palette plus tard.
    /// </summary>
    public static Color AltitudeToColor(float relAlt)
    {
        relAlt = Mathf.Clamp(relAlt, -1f, 1f);

        if (relAlt < 0f)
        {
            // Fond marin : abysses (bleu très sombre) → plateau continental (bleu-gris)
            float t = (relAlt + 1f);  // [0,1] — 0=abysse, 1=juste sous la surface
            return Color.Lerp(
                new Color(0.05f, 0.07f, 0.12f),   // abysse
                new Color(0.12f, 0.16f, 0.22f),   // fond côtier
                t);
        }

        // Émergé : 4 bandes
        if (relAlt < 0.25f)
        {
            float t = relAlt / 0.25f;
            return Color.Lerp(
                new Color(0.72f, 0.68f, 0.50f),   // sable côtier
                new Color(0.40f, 0.56f, 0.28f),   // plaines herbeuses
                t);
        }
        if (relAlt < 0.55f)
        {
            float t = (relAlt - 0.25f) / 0.30f;
            return Color.Lerp(
                new Color(0.40f, 0.56f, 0.28f),   // plaines
                new Color(0.38f, 0.30f, 0.20f),   // collines rocheuses
                t);
        }
        if (relAlt < 0.80f)
        {
            float t = (relAlt - 0.55f) / 0.25f;
            return Color.Lerp(
                new Color(0.38f, 0.30f, 0.20f),   // collines
                new Color(0.55f, 0.52f, 0.50f),   // roche alpine
                t);
        }
        {
            float t = (relAlt - 0.80f) / 0.20f;
            return Color.Lerp(
                new Color(0.55f, 0.52f, 0.50f),   // roche alpine
                new Color(0.92f, 0.94f, 0.96f),   // neige
                t);
        }
    }

    // ── Lens Élévation (debug) ────────────────────────────────────────────────

    /// <summary>
    /// Lens dénivelé : colore chaque face selon l'altitude absolue [-1, +1] sans offset waterLevel.
    /// Palette scientifique type DEM — ignorée par la WaterSphere (elle est cachée dans ce mode).
    ///
    ///  -1.0  → bleu abyssal profond
    ///  -0.5  → bleu océan
    ///   0.0  → cyan (référence niveau zéro)
    ///  +0.3  → vert tendre
    ///  +0.6  → jaune/orange
    ///  +0.85 → brun rocheux
    ///  +1.0  → blanc neige
    /// </summary>
    public static void ColorizeElevationLens(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles)
    {
        if (faces == null || serverTiles == null || serverTiles.Length == 0)
            return;

        for (int i = 0; i < faces.Length; i++)
        {
            int   bestIdx = FindNearestTile(faces[i].latNorm, faces[i].lonNorm, serverTiles);
            float alt     = serverTiles[bestIdx].altitude;  // [-1, +1] absolu
            faces[i].color = ElevationLensColor(alt);
        }
    }

    /// <summary>
    /// Gradient DEM : altitude absolue [-1, +1] → couleur.
    /// Palette inspirée du standard cartographique SRTM.
    /// </summary>
    public static Color ElevationLensColor(float alt)
    {
        alt = Mathf.Clamp(alt, -1f, 1f);

        // Zone sous zéro — bleu abyssal → bleu-cyan surface
        if (alt < -0.5f)
        {
            float t = (alt + 1f) / 0.5f;  // [0,1]
            return Color.Lerp(
                new Color(0.04f, 0.06f, 0.20f),   // abysse
                new Color(0.10f, 0.30f, 0.65f),   // bleu profond
                t);
        }
        if (alt < 0f)
        {
            float t = (alt + 0.5f) / 0.5f;
            return Color.Lerp(
                new Color(0.10f, 0.30f, 0.65f),   // bleu profond
                new Color(0.30f, 0.70f, 0.85f),   // bleu-cyan (proche surface)
                t);
        }
        // Zone émergée
        if (alt < 0.3f)
        {
            float t = alt / 0.3f;
            return Color.Lerp(
                new Color(0.30f, 0.70f, 0.85f),   // cyan (niveau 0)
                new Color(0.45f, 0.78f, 0.35f),   // vert
                t);
        }
        if (alt < 0.6f)
        {
            float t = (alt - 0.3f) / 0.3f;
            return Color.Lerp(
                new Color(0.45f, 0.78f, 0.35f),   // vert
                new Color(0.95f, 0.78f, 0.20f),   // jaune
                t);
        }
        if (alt < 0.85f)
        {
            float t = (alt - 0.6f) / 0.25f;
            return Color.Lerp(
                new Color(0.95f, 0.78f, 0.20f),   // jaune
                new Color(0.55f, 0.32f, 0.12f),   // brun
                t);
        }
        {
            float t = (alt - 0.85f) / 0.15f;
            return Color.Lerp(
                new Color(0.55f, 0.32f, 0.12f),   // brun
                new Color(0.95f, 0.96f, 0.98f),   // blanc neige
                t);
        }
    }

    // ── Legacy (TerrainType palette) ─────────────────────────────────────────

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
            int bestIdx = FindNearestTile(faces[i].latNorm, faces[i].lonNorm, serverTiles);
            if (colorByType.TryGetValue(serverTiles[bestIdx].terrainType, out Color c))
                faces[i].color = c;
        }
    }

    /// <summary>
    /// Construit un dictionnaire faceId → GoldbergTileState (nearest-neighbor lat/lon).
    /// Utilisé pour résoudre la tuile serveur d'une face au survol (tooltip).
    /// </summary>
    public static Dictionary<int, GoldbergTileState> BuildFaceToTileMap(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles)
    {
        var map = new Dictionary<int, GoldbergTileState>(faces?.Length ?? 0);
        if (faces == null || serverTiles == null || serverTiles.Length == 0)
            return map;

        for (int i = 0; i < faces.Length; i++)
            map[i] = serverTiles[FindNearestTile(faces[i].latNorm, faces[i].lonNorm, serverTiles)];

        return map;
    }

    /// <summary>
    /// Construit un tableau d'altitudes par face GP (nearest-neighbor lat/lon).
    /// altitudes[i] = GoldbergTileState.altitude de la tuile H3 la plus proche de la face i.
    /// </summary>
    public static float[] BuildFaceAltitudes(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles)
    {
        var altitudes = new float[faces?.Length ?? 0];
        if (faces == null || serverTiles == null || serverTiles.Length == 0)
            return altitudes;

        for (int i = 0; i < faces.Length; i++)
            altitudes[i] = serverTiles[FindNearestTile(faces[i].latNorm, faces[i].lonNorm, serverTiles)].altitude;

        return altitudes;
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

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>Nearest-neighbor lookup : retourne l'index de la tuile la plus proche (lat/lon, wrap longitude).</summary>
    private static int FindNearestTile(float fLat, float fLon, GoldbergTileState[] tiles)
    {
        float best = float.MaxValue;
        int   idx  = 0;
        for (int j = 0; j < tiles.Length; j++)
        {
            float dLat = fLat - tiles[j].latNorm;
            float dLon = fLon - tiles[j].lonNorm;
            if (dLon >  0.5f) dLon -= 1f;
            if (dLon < -0.5f) dLon += 1f;
            float d2 = dLat * dLat + dLon * dLon;
            if (d2 < best) { best = d2; idx = j; }
        }
        return idx;
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
