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
    /// Tints only the border faces of each corporation territory.
    /// A tile is a border tile when at least one neighbor is unowned or owned by a different corp.
    /// Interior tiles (fully surrounded by the same corp) are left in their biome color.
    /// ownershipTints : tileId → corp color (only tiles on the current body).
    /// tileToCorpId   : tileId → corpId, used to compare neighbors.
    /// blend: 0 = full biome color, 1 = full corp color. 0.80f is recommended for borders.
    /// </summary>
    public static void ApplyOwnershipTint(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        GoldbergTileState[] serverTiles,
        Dictionary<string, Color> ownershipTints,
        Dictionary<string, string> tileToCorpId,
        float blend)
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
            if (!isBorder) continue;

            float tLat      = tile.latNorm;
            float tLon      = tile.lonNorm;
            Color corpColor = kv.Value;

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
                faces[bestFace].color = Color.Lerp(faces[bestFace].color, corpColor, blend);
        }
    }
}
