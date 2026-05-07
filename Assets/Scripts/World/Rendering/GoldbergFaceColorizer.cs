using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Colorise les tuiles d'une sphère Goldberg depuis les données H3 du serveur de simulation.
/// Appelé après GoldbergSphereGenerator.Generate() et avant GoldbergSphereGenerator.ApplyFaceColors().
/// </summary>
public static partial class GoldbergFaceColorizer
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

        // k=3 altitude average : même k que BuildFaceIsOcean/BuildFaceIsInlandWater.
        // Évite la désynchronisation entre la couleur terrain et la classification eau
        // (single NN peut pointer une tile montagne alors que les 2 autres sont ocean).
        const int kAvg = 3;
        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int i = 0; i < faces.Length; i++)
        {
            int[] nearest = FindKNearestTiles(faces[i].centroid3D, tileDirs, kAvg);
            float altSum = 0f;
            foreach (int ti in nearest) altSum += serverTiles[ti].altitude;
            float relAlt = (altSum / kAvg) - waterLevel;
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
            // Fond marin : couleur terrain naturelle (sable/roche immergée).
            // La WaterSphere (opaque, ZWrite ON via material instancié dans CreateWaterCaps)
            // occulte complètement ces faces — leur couleur est invisible en pratique.
            // Quand seaLevel baisse (terraforming), relAlt redevient > 0 → couleur terrain
            // apparaît naturellement sans aucun traitement supplémentaire.
            float t = Mathf.Clamp01(-relAlt * 4f);   // 0=surface, 1=profond (compression rapide)
            return Color.Lerp(
                new Color(0.72f, 0.65f, 0.45f),   // sable côtier immergé
                new Color(0.35f, 0.28f, 0.20f),   // roche/argile profonde
                t);
        }

        // Émergé : 4 bandes
        if (relAlt < 0.025f)
        {
            // Plage / côte immédiate : sable humide, légèrement plus foncé que le sable sec.
            // Zone où la marée arrive et repart (oscillation WaterSphere).
            float t = relAlt / 0.025f;
            return Color.Lerp(
                new Color(0.68f, 0.62f, 0.42f),   // sable humide
                new Color(0.72f, 0.68f, 0.50f),   // sable sec
                t);
        }
        if (relAlt < 0.25f)
        {
            float t = (relAlt - 0.025f) / (0.25f - 0.025f);
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

        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int i = 0; i < faces.Length; i++)
        {
            int   bestIdx = FindNearestTile(faces[i].centroid3D, tileDirs);
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
        var abyssal  = new Color(0.04f, 0.06f, 0.20f);
        var deepBlue = new Color(0.10f, 0.30f, 0.65f);
        var cyan     = new Color(0.30f, 0.70f, 0.85f);
        var green    = new Color(0.45f, 0.78f, 0.35f);
        var yellow   = new Color(0.95f, 0.78f, 0.20f);
        var brown    = new Color(0.55f, 0.32f, 0.12f);
        var snow     = new Color(0.95f, 0.96f, 0.98f);
        if (alt < -0.5f) return _LerpBand(alt, -1.00f, -0.50f, abyssal,  deepBlue);
        if (alt <  0f)   return _LerpBand(alt, -0.50f,  0.00f, deepBlue, cyan);
        if (alt <  0.3f) return _LerpBand(alt,  0.00f,  0.30f, cyan,     green);
        if (alt <  0.6f) return _LerpBand(alt,  0.30f,  0.60f, green,    yellow);
        if (alt <  0.85f)return _LerpBand(alt,  0.60f,  0.85f, yellow,   brown);
        return _LerpBand(alt, 0.85f, 1.00f, brown, snow);
    }

    private static Color _LerpBand(float alt, float lo, float hi, Color from, Color to)
        => Color.Lerp(from, to, (alt - lo) / (hi - lo));

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

        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int i = 0; i < faces.Length; i++)
        {
            int bestIdx = FindNearestTile(faces[i].centroid3D, tileDirs);
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

        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int i = 0; i < faces.Length; i++)
            map[i] = serverTiles[FindNearestTile(faces[i].centroid3D, tileDirs)];

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

        // k=3 average — même stratégie que BuildFaceIsOcean / ColorizeFromAltitude.
        // Cohérence : les trois fonctions utilisent les mêmes 3 tiles pour altitude,
        // couleur et classification. Évite que la couleur (neige) et la classification
        // (ocean) divergent pour une face à la frontière H3/GP → hexagone blanc.
        const int kAvg = 3;
        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int i = 0; i < faces.Length; i++)
        {
            int[] nearest = FindKNearestTiles(faces[i].centroid3D, tileDirs, kAvg);
            float sum = 0f;
            foreach (int ti in nearest) sum += serverTiles[ti].altitude;
            altitudes[i] = sum / kAvg;
        }

        return altitudes;
    }

    /// <summary>
    /// Construit un masque booléen "est-ce que cette face est océan ?" basé sur le TerrainType
    /// de la tuile H3 la plus proche, indépendamment du slider sea level.
    /// <para>
    /// <summary>
    /// Construit un masque boléen "est-ce que cette face est océan ?".
    /// <para>
    /// Règle de classification (source de vérité : données H3 serveur) :
    ///   OpenOcean / FrozenWater → océan toujours.
    ///   TerrainType.Eau + NOT InlandWater + NOT InlandSea → océan (inclut Coast = eaux côtières peu profondes).
    /// </para>
    /// <para>
    /// IMPORTANT — Coast en H3 serveur ≠ Coast en C# local :
    ///   • H3 Python : Coast = TerrainType.Eau, tuile connectée à l'océan ouvert mais proche d'une côte.
    ///   • C# local  : Coast = terrain sec adjacent à l'océan (définition du WaterClassificationSystem).
    /// Ce masque travaille avec les tiles H3 serveur → Coast = eau peu profonde = doit avoir un cap.
    /// </para>
    /// </summary>
    public static bool[] BuildFaceIsOcean(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles)
    {
        var mask = new bool[faces?.Length ?? 0];
        if (faces == null || serverTiles == null || serverTiles.Length == 0)
            return mask;

        // Vote de majorité sur les 3 tiles les plus proches (k=3).
        // Évite les faux-positifs de classification aux bords GP/H3 :
        // un centroïde GP qui tombe juste de l'autre côté d'une frontière H3 peut être
        // mappé sur un tile ocean alors que la face est visuellement sur terre.
        // Avec k=3, les 2e et 3e voisins (réels voisins GP) corrigent le vote.
        const int kVote = 3;
        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int i = 0; i < faces.Length; i++)
        {
            int[] nearest = FindKNearestTiles(faces[i].centroid3D, tileDirs, kVote);
            int oceanVotes = 0;
            foreach (int ti in nearest)
            {
                var t = serverTiles[ti];
                if (t.waterClassification == WaterClassification.OpenOcean
                 || t.waterClassification == WaterClassification.FrozenWater
                 || (t.terrainType == TerrainType.Eau
                     && t.waterClassification != WaterClassification.InlandWater
                     && t.waterClassification != WaterClassification.InlandSea))
                    oceanVotes++;
            }
            // Majorité stricte : au moins 2 tiles sur 3 doivent être ocean.
            // → un seul tile ocean NN erroné ne suffit pas à créer un cap fantôme.
            mask[i] = oceanVotes * 2 > kVote;
        }
        return mask;
    }

    /// <summary>
    /// Construit un masque booléen "est-ce que cette face est un lac (eau intérieure) ?".
    /// <para>
    /// Distinct du masque ocean : InlandWater = bassins fermés non connectés à l'océan.
    /// Garde altitude : on ne génère un lac que si la face est en-dessous ou très proche
    /// du niveau de la mer (<c>faceAltitudes[i] &lt; seaLevel + 0.12</c>).
    /// Évite les artefacts sur les tiles InlandWater mal classifiées en altitude haute
    /// (ex. bassin montagnard dont le centroïde GP tombe sur un tile voisin plus élevé).
    /// </para>
    /// </summary>
    public static bool[] BuildFaceIsInlandWater(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles,
        float[] faceAltitudes = null,
        float seaLevel = 0f)
    {
        var mask = new bool[faces?.Length ?? 0];
        if (faces == null || serverTiles == null || serverTiles.Length == 0)
            return mask;

        // Vote de majorité k=3 identique à BuildFaceIsOcean.
        const int kVote = 3;
        Vector3[] tileDirs = BuildTileDirs(serverTiles);
        for (int i = 0; i < faces.Length; i++)
        {
            int[] nearest = FindKNearestTiles(faces[i].centroid3D, tileDirs, kVote);
            int inlandVotes = 0;
            foreach (int ti in nearest)
            {
                var t = serverTiles[ti];
                if ((t.waterClassification == WaterClassification.InlandWater
                     || t.waterClassification == WaterClassification.InlandSea)
                    && (faceAltitudes == null || i >= faceAltitudes.Length
                        || faceAltitudes[i] <= seaLevel + 0.12f))
                    inlandVotes++;
            }
            mask[i] = inlandVotes * 2 > kVote;
        }
        return mask;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Précompute les directions 3D des tuiles H3 (lat/lon normalisé → vecteur unitaire).
    /// À appeler UNE FOIS avant un lot de FindNearestTile pour éviter O(tiles) trig par face.
    /// </summary>
    private static Vector3[] BuildTileDirs(GoldbergTileState[] tiles)
    {
        var dirs = new Vector3[tiles.Length];
        for (int j = 0; j < tiles.Length; j++)
        {
            float latRad = tiles[j].latNorm * Mathf.PI - Mathf.PI * 0.5f;  // [0,1] → [-π/2, π/2]
            float lonRad = tiles[j].lonNorm * 2f * Mathf.PI - Mathf.PI;    // [0,1] → [-π, π]
            float cosLat = Mathf.Cos(latRad);
            dirs[j] = new Vector3(cosLat * Mathf.Cos(lonRad), Mathf.Sin(latRad), cosLat * Mathf.Sin(lonRad));
        }
        return dirs;
    }

    /// <summary>
    /// Nearest-neighbor sphérique — distance angulaire (dot product 3D).
    /// Correct aux pôles et près des sommets de l'icosaèdre (pénagones H3) contrairement
    /// à la distance Euclidienne 2D en lat/lon normalisé qui se déforme aux hautes latitudes.
    /// </summary>
    private static int FindNearestTile(Vector3 faceDir, Vector3[] tileDirs)
    {
        float best = float.MinValue;
        int   idx  = 0;
        for (int j = 0; j < tileDirs.Length; j++)
        {
            float dot = Vector3.Dot(faceDir, tileDirs[j]);
            if (dot > best) { best = dot; idx = j; }
        }
        return idx;
    }

    /// <summary>
    /// Retourne les indices des <paramref name="k"/> tiles H3 les plus proches (par dot product).
    /// Utilisé pour un vote de majorité afin d'éviter les faux-positifs de classification
    /// aux bords GP/H3 (un centroïde GP peut tomber juste de l'autre côté d'une frontière H3).
    /// </summary>
    private static int[] FindKNearestTiles(Vector3 faceDir, Vector3[] tileDirs, int k)
    {
        // Fast path k=3 : partial sort without bool[] allocation (avoids ~6k allocs per LoadPlanet).
        if (k == 3 && tileDirs.Length >= 3)
        {
            int   i0 = -1, i1 = -1, i2 = -1;
            float b0 = float.MinValue, b1 = float.MinValue, b2 = float.MinValue;
            for (int j = 0; j < tileDirs.Length; j++)
            {
                float dot = Vector3.Dot(faceDir, tileDirs[j]);
                if      (dot > b0) { b2 = b1; i2 = i1; b1 = b0; i1 = i0; b0 = dot; i0 = j; }
                else if (dot > b1) { b2 = b1; i2 = i1;           b1 = dot; i1 = j; }
                else if (dot > b2) {                              b2 = dot; i2 = j; }
            }
            return new[] { i0, i1, i2 };
        }
        // Generic fallback for other k values.
        var result = new int[k];
        var used   = new bool[tileDirs.Length];
        for (int r = 0; r < k; r++)
        {
            float best = float.MinValue;
            int   idx  = 0;
            for (int j = 0; j < tileDirs.Length; j++)
            {
                if (used[j]) continue;
                float dot = Vector3.Dot(faceDir, tileDirs[j]);
                if (dot > best) { best = dot; idx = j; }
            }
            result[r] = idx;
            used[idx]  = true;
        }
        return result;
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

        // Nearest-neighbor 3D (dot product) — cohérent avec BuildFaceAltitudes/ColorizeFromAltitude.
        // Évite la distorsion aux pôles de la distance Euclidienne lat/lon 2D.
        Vector3[] tileDirsOwn = BuildTileDirs(serverTiles);
        for (int fi = 0; fi < faces.Length; fi++)
        {
            int bestJ = FindNearestTile(faces[fi].centroid3D, tileDirsOwn);

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
