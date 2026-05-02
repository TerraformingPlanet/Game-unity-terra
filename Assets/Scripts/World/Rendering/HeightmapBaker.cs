using UnityEngine;

/// <summary>
/// Bake les altitudes H3 (GoldbergTileState[]) dans une Texture2D en projection
/// équirectangulaire (longitude → U, latitude → V).
///
/// Format : RFloat (canal R = altitude remappée [−1,1] → [0,1]).
/// Résolution configurable via width/height (défaut recommandé : 512×256).
///
/// Algorithme :
///   1. Scatter — chaque tile écrit son altitude sur le pixel le plus proche.
///   2. Blur 3×3 — comble les gaps entre tiles et lisse les transitions.
///   3. Apply() — upload sur GPU.
/// </summary>
public static class HeightmapBaker
{
    /// <summary>
    /// Crée et retourne une Texture2D contenant la heightmap baked depuis les tiles serveur.
    /// width × height doit être une puissance de deux pour une meilleure compatibilité GPU.
    /// seaLevel : altitude absolue du niveau de la mer (ex: 0.1020 pour Kepler-442b).
    ///   Après le blur, les pixels ocean (altitude originale < seaLevel) sont clamped à seaLevel
    ///   pour éviter que l'interpolation côtière ne pousse des vertices ocean au-dessus de la
    ///   water sphere dans le shader de displacement.
    /// </summary>
    public static Texture2D BakeFromTiles(GoldbergTileState[] tiles, float seaLevel = 0f, int width = 512, int height = 256)
    {
        if (tiles == null || tiles.Length == 0)
            return CreateFallback(width, height);

        // Scatter : remplir les pixels depuis les tiles (lonNorm→U, latNorm→V)
        // On travaille en float[] pour performance (évite Color struct overhead).
        float[] raw      = new float[width * height];
        int[]   count    = new int[width * height];   // pour moyenner si plusieurs tiles → même pixel
        bool[]  isOcean  = new bool[width * height];  // masque : pixel appartient à une tuile ocean

        foreach (var tile in tiles)
        {
            int u = Mathf.Clamp(Mathf.RoundToInt(tile.lonNorm * (width  - 1)), 0, width  - 1);
            int v = Mathf.Clamp(Mathf.RoundToInt(tile.latNorm * (height - 1)), 0, height - 1);
            int idx = v * width + u;
            raw[idx]   += tile.altitude;
            count[idx] += 1;
            // Si au moins une tuile ocean contribue à ce pixel → pixel ocean
            if (tile.altitude < seaLevel) isOcean[idx] = true;
        }

        // Moyenne sur les pixels multi-occupés
        for (int i = 0; i < raw.Length; i++)
            if (count[i] > 1) raw[i] /= count[i];

        // Blur 3×3 — comble les pixels sans tile et lisse les bords
        float[] blurred = Blur3x3(raw, width, height);

        // Clamp ocean : le blur peut interpoler des pixels ocean vers des valeurs > seaLevel
        // (coin côtier avec voisins land). Garantit qu'aucun pixel ocean ne génère un vertex
        // au-dessus de la water sphere dans le shader de displacement.
        for (int i = 0; i < blurred.Length; i++)
            if (isOcean[i]) blurred[i] = Mathf.Min(blurred[i], seaLevel);

        // Remapper [−1, 1] → [0, 1] pour le canal R de la texture
        var pixels = new Color[width * height];
        for (int i = 0; i < blurred.Length; i++)
            pixels[i] = new Color(blurred[i] * 0.5f + 0.5f, 0f, 0f, 1f);

        var tex = new Texture2D(width, height, TextureFormat.RFloat, false, linear: true)
        {
            name       = "PlanetHeightmap",
            wrapMode   = TextureWrapMode.Repeat,   // longitude wrap
            filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false);
        return tex;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Blur 3×3 simple — propage les altitudes connues dans les zones vides.</summary>
    private static float[] Blur3x3(float[] src, int w, int h)
    {
        float[] dst = new float[src.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                int   n   = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int sy = Mathf.Clamp(y + dy, 0, h - 1);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        // Wrap longitude
                        int sx = (x + dx + w) % w;
                        sum += src[sy * w + sx];
                        n++;
                    }
                }
                dst[y * w + x] = sum / n;
            }
        }
        return dst;
    }

    /// <summary>Texture neutre (altitude = 0 partout) pour fallback si pas de tiles.</summary>
    private static Texture2D CreateFallback(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RFloat, false, linear: true)
        {
            name = "PlanetHeightmap_Fallback"
        };
        var pixels = new Color[width * height];
        // altitude 0 → remappé à 0.5
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(0.5f, 0f, 0f, 1f);
        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false);
        return tex;
    }
}
