using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Construit un mesh de faces latérales ("prisme") pour chaque tile terrestre.
///
/// Principe :
///   Chaque tile émergée au-dessus du seaLevel génère un anneau de quads verticaux
///   reliant le bord supérieur (topR = altitude de la tile) au bord inférieur
///   (baseR = seaLevel). L'effet visuel est un prisme hexagonal à la Civ 6 :
///   la face supérieure est plate, les murs latéraux descendent jusqu'à l'eau.
///
/// Les tiles ocean sont exclues (leurs côtés sont cachés sous le water cap).
/// La couleur des murs = faces[i].color (cohérence avec le top).
///
/// Résultat : 1 seul Mesh combiné, 1 draw call.
/// </summary>
public static class TilePrismsBuilder
{
    /// <summary>
    /// Génère le mesh de murs latéraux pour toutes les tiles terrestres émergées.
    /// </summary>
    /// <param name="faces">Faces du polyèdre Goldberg (centroid3D, boundaryVertices, color).</param>
    /// <param name="faceAltitudes">Altitude par face [-1, +1] (même tableau que ApplyTopographicDisplacement).</param>
    /// <param name="seaLevel">Niveau de la mer dans l'espace altitude [-1, +1].</param>
    /// <param name="displacementScale">Amplitude du relief (ex: 0.5).</param>
    /// <param name="isFaceOcean">
    /// Masque ocean/lac : les faces marquées true ne génèrent pas de murs.
    /// Si null, la comparaison altitude vs seaLevel est utilisée.
    /// </param>
    public static Mesh BuildPrisms(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        float[]  faceAltitudes,
        float    seaLevel,
        float    displacementScale,
        bool[]   isFaceOcean = null)
    {
        if (faces == null || faceAltitudes == null) return null;

        // Base of each prism at altitude = -1 (absolute planetary floor).
        // Each land tile extrudes as a full column from the floor to its own altitude,
        // giving the Civ 6 prism look. The -0.002 offset avoids Z-fighting with any
        // inner sphere if present.
        float baseR = GoldbergSphereGenerator.VisualRadius - displacementScale - 0.002f;

        // Hauteur minimale sous laquelle on ne génère pas de quad (tile rase-mer → mur invisible).
        const float kMinWallHeight = 0.003f;

        // Map coin → faces voisines (même logique que ApplyTopographicDisplacement / WaterCapsBuilder).
        // Permet d'aligner le haut du mur prisme sur le corner-averaging réel du mesh terrain,
        // éliminant les gaps visuels entre tuiles adjacentes à altitudes différentes.
        var cornerFaces = WaterCapsBuilder.BuildCornerFacesMap(faces);

        var verts   = new List<Vector3>(faces.Length * 24);
        var tris    = new List<int>(faces.Length * 36);
        var normals = new List<Vector3>(faces.Length * 24);
        var colors  = new List<Color>(faces.Length * 24);

        for (int i = 0; i < faces.Length; i++)
        {
            // Exclure les tiles ocean/lac : par classification serveur OU par altitude.
            // Cohérence avec WaterCapsBuilder : une face couverte par un water cap ne doit pas
            // avoir de mur prisme (double coverage → artefacts).
            bool isWater = (isFaceOcean != null && i < isFaceOcean.Length && isFaceOcean[i])
                        || (i < faceAltitudes.Length && faceAltitudes[i] < seaLevel);
            if (isWater) continue;

            float alt  = (i < faceAltitudes.Length) ? faceAltitudes[i] : seaLevel;
            float topR = GoldbergSphereGenerator.VisualRadius + alt * displacementScale;

            // Skip tiles dont le haut est à peine au-dessus de la base
            if (topR - baseR < kMinWallHeight) continue;

            Vector3[] bv = faces[i].boundaryVertices;
            if (bv == null || bv.Length < 3) continue;

            int  rimCount  = bv.Length;
            Color faceColor = faces[i].color;

            for (int k = 0; k < rimCount; k++)
            {
                Vector3 c1 = bv[k];
                Vector3 c2 = bv[(k + 1) % rimCount];

                // Top corner radius : corner-averaged altitude (identique à ApplyTopographicDisplacement).
                // Aligne le haut du mur sur les vertices réels du mesh → supprime les gaps inter-tuiles.
                float avgAlt1 = WaterCapsBuilder.CornerAverageAlt(WaterCapsBuilder.RoundKey(c1), cornerFaces, faceAltitudes, alt);
                float avgAlt2 = WaterCapsBuilder.CornerAverageAlt(WaterCapsBuilder.RoundKey(c2), cornerFaces, faceAltitudes, alt);
                float topR1 = Mathf.Max(baseR + kMinWallHeight,
                    GoldbergSphereGenerator.VisualRadius + avgAlt1 * displacementScale);
                float topR2 = Mathf.Max(baseR + kMinWallHeight,
                    GoldbergSphereGenerator.VisualRadius + avgAlt2 * displacementScale);

                // 4 coins du quad mural (top corners corner-averaged, bas à seaLevel)
                Vector3 v0 = c1.normalized * topR1;  // haut gauche
                Vector3 v1 = c2.normalized * topR2;  // haut droit
                Vector3 v2 = c2.normalized * baseR;  // bas droit
                Vector3 v3 = c1.normalized * baseR;  // bas gauche

                // Normale brute via produit vectoriel, puis vérification outward.
                Vector3 edge = (v1 - v0).normalized;
                Vector3 down = (v3 - v0).normalized;
                Vector3 raw  = Vector3.Cross(edge, down);
                Vector3 quadCenter = (v0 + v1 + v2 + v3) * 0.25f;

                // outward = true  → winding actuel est déjà CCW vu de l'extérieur
                // outward = false → le winding est CW : il faut retourner les deux à la fois
                bool outward = Vector3.Dot(raw, quadCenter) >= 0f;
                Vector3 normal = outward ? raw.normalized : -raw.normalized;

                int baseIdx = verts.Count;
                verts.Add(v0);  normals.Add(normal); colors.Add(faceColor);
                verts.Add(v1);  normals.Add(normal); colors.Add(faceColor);
                verts.Add(v2);  normals.Add(normal); colors.Add(faceColor);
                verts.Add(v3);  normals.Add(normal); colors.Add(faceColor);

                if (outward)
                {
                    // CCW vu de l'extérieur — winding standard
                    tris.Add(baseIdx);     tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    tris.Add(baseIdx);     tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                }
                else
                {
                    // CW vu de l'extérieur → renverser pour rester visible (CCW après flip)
                    tris.Add(baseIdx + 2); tris.Add(baseIdx + 1); tris.Add(baseIdx);
                    tris.Add(baseIdx + 3); tris.Add(baseIdx + 2); tris.Add(baseIdx);
                }
            }
        }

        if (verts.Count == 0) return null;

        var mesh = new Mesh { name = "TilePrisms" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = verts.ToArray();
        mesh.normals   = normals.ToArray();
        mesh.colors    = colors.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Génère des prismes pour les tiles océan : murs latéraux descendant du seaLevel
    /// jusqu'au plancher océanique de chaque tile. Produit un effet de profondeur 3D
    /// visible à travers la surface d'eau semi-transparente.
    /// </summary>
    /// <param name="faces">Faces du polyèdre Goldberg.</param>
    /// <param name="faceAltitudes">Altitude par face [-1, +1].</param>
    /// <param name="seaLevel">Niveau de la mer [-1, +1].</param>
    /// <param name="displacementScale">Amplitude du relief (ex: 0.5).</param>
    /// <param name="isFaceOcean">Masque ocean ; si null, altitude &lt; seaLevel est utilisé.</param>
    public static Mesh BuildWaterPrisms(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        float[]  faceAltitudes,
        float    seaLevel,
        float    displacementScale,
        bool[]   isFaceOcean = null)
    {
        if (faces == null || faceAltitudes == null) return null;

        // Même plancher absolu que BuildPrisms() pour les terres.
        // Les murs vont du haut de la tile (fond océanique) jusqu'ici.
        // La surface d'eau (WaterCaps) est une couche séparée au-dessus.
        float baseR = GoldbergSphereGenerator.VisualRadius - displacementScale - 0.002f;
        const float kMinWallHeight = 0.003f;

        var cornerFaces = WaterCapsBuilder.BuildCornerFacesMap(faces);

        var verts   = new List<Vector3>(faces.Length * 24);
        var tris    = new List<int>(faces.Length * 36);
        var normals = new List<Vector3>(faces.Length * 24);
        var colors  = new List<Color>(faces.Length * 24);

        for (int i = 0; i < faces.Length; i++)
        {
            // Uniquement les tiles ocean.
            bool isOcean = (isFaceOcean != null && i < isFaceOcean.Length && isFaceOcean[i])
                        || (isFaceOcean == null && i < faceAltitudes.Length && faceAltitudes[i] < seaLevel);
            if (!isOcean) continue;

            float alt  = (i < faceAltitudes.Length) ? faceAltitudes[i] : seaLevel;
            float topR = GoldbergSphereGenerator.VisualRadius + alt * displacementScale;

            // Pas de mur si trop proche du plancher.
            if (topR - baseR < kMinWallHeight) continue;

            Vector3[] bv = faces[i].boundaryVertices;
            if (bv == null || bv.Length < 3) continue;

            int rimCount = bv.Length;
            Color fc = faces[i].color;

            for (int k = 0; k < rimCount; k++)
            {
                Vector3 c1 = bv[k];
                Vector3 c2 = bv[(k + 1) % rimCount];

                // Haut corner-averaged : colle exactement au mesh H3 (identique à BuildPrisms).
                float avgAlt1 = WaterCapsBuilder.CornerAverageAlt(WaterCapsBuilder.RoundKey(c1), cornerFaces, faceAltitudes, alt);
                float avgAlt2 = WaterCapsBuilder.CornerAverageAlt(WaterCapsBuilder.RoundKey(c2), cornerFaces, faceAltitudes, alt);
                float topR1 = Mathf.Max(baseR + kMinWallHeight,
                    GoldbergSphereGenerator.VisualRadius + avgAlt1 * displacementScale);
                float topR2 = Mathf.Max(baseR + kMinWallHeight,
                    GoldbergSphereGenerator.VisualRadius + avgAlt2 * displacementScale);

                Vector3 v0 = c1.normalized * topR1;  // haut gauche (fond tile)
                Vector3 v1 = c2.normalized * topR2;  // haut droit  (fond tile)
                Vector3 v2 = c2.normalized * baseR;  // bas droit   (plancher absolu)
                Vector3 v3 = c1.normalized * baseR;  // bas gauche  (plancher absolu)

                Vector3 edge = (v1 - v0).normalized;
                Vector3 down = (v3 - v0).normalized;
                Vector3 raw  = Vector3.Cross(edge, down);
                Vector3 quadCenter = (v0 + v1 + v2 + v3) * 0.25f;

                bool outward = Vector3.Dot(raw, quadCenter) >= 0f;
                Vector3 normal = outward ? raw.normalized : -raw.normalized;

                int baseIdx = verts.Count;
                verts.Add(v0);  normals.Add(normal); colors.Add(fc);
                verts.Add(v1);  normals.Add(normal); colors.Add(fc);
                verts.Add(v2);  normals.Add(normal); colors.Add(fc);
                verts.Add(v3);  normals.Add(normal); colors.Add(fc);

                if (outward)
                {
                    tris.Add(baseIdx);     tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    tris.Add(baseIdx);     tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                }
                else
                {
                    tris.Add(baseIdx + 2); tris.Add(baseIdx + 1); tris.Add(baseIdx);
                    tris.Add(baseIdx + 3); tris.Add(baseIdx + 2); tris.Add(baseIdx);
                }
            }
        }

        if (verts.Count == 0) return null;

        var mesh = new Mesh { name = "WaterPrisms" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = verts.ToArray();
        mesh.normals   = normals.ToArray();
        mesh.colors    = colors.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Nouvelle approche unifiée : chaque tile (land ET ocean) est prolongée vers le bas
    /// jusqu'à un plancher fixe, formant un prisme hexagonal descendant.
    ///
    /// Différences avec BuildPrisms / BuildWaterPrisms :
    ///   - Couvre TOUTES les tiles (land + ocean + coast).
    ///   - Pas de corner-averaging : le haut du mur utilise directement les boundary vertices
    ///     du mesh H3 (déjà déplacés à l'altitude correcte par H3SphereBuilder).
    ///   - Chaque tile est indépendante → pas de calcul de voisins.
    ///   - Visuellement : colonnes hexagonales avec profondeur, style Civ 6 pour toutes les tiles.
    ///
    /// La surface de l'eau est la face top des tiles océan, bakée à seaLevel par H3SphereBuilder.
    /// </summary>
    /// <param name="faces">Faces du polyèdre H3 (boundaryVertices déjà à l'altitude finale).</param>
    /// <param name="faceAltitudes">Altitude par face [-1, +1].</param>
    /// <param name="displacementScale">Amplitude du relief (ex: 0.5).</param>
    /// <param name="seaLevel">Niveau de la mer en altitude [-1,+1]. Sert à calculer le plancher des murs.</param>
    /// <param name="isFaceOcean">Masque booléen par face : true = tile océan (top face baked at seaLevel).
    /// Plus fiable qu'une comparaison d'altitude flottante pour éviter les cônes bleus.</param>
    public static Mesh BuildDepthPrisms(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        float[]  faceAltitudes,
        float    displacementScale,
        float    seaLevel  = -2f,
        bool[]   isFaceOcean = null)
    {
        if (faces == null || faceAltitudes == null) return null;

        // Plancher absolu (pas d'eau, ou fallback de sécurité).
        float baseR = GoldbergSphereGenerator.VisualRadius - displacementScale - 0.002f;
        const float kMinWallHeight = 0.003f;

        // Plancher profond : les murs descendent jusqu'au plancher absolu pour couvrir
        // tous les gaps entre tiles côtières (jonction terre/océan).
        // Les tiles océan ont leur top face baked à seaLevel par H3SphereBuilder.
        float floorR = baseR;

        var verts   = new List<Vector3>(faces.Length * 24);
        var tris    = new List<int>(faces.Length * 36);
        var normals = new List<Vector3>(faces.Length * 24);
        var colors  = new List<Color>(faces.Length * 24);

        for (int i = 0; i < faces.Length; i++)
        {
            Vector3[] bv = faces[i].boundaryVertices;
            if (bv == null || bv.Length < 3) continue;

            float alt  = (i < faceAltitudes.Length) ? faceAltitudes[i] : 0f;
            bool isOcean = (isFaceOcean != null && i < isFaceOcean.Length)
                ? isFaceOcean[i]
                : alt <= seaLevel;
            // Ocean tiles: top face is clamped to seaLevel by H3SphereBuilder.
            // Walls descend from seaLevel (matching the tile face) down to the floor.
            float effectiveTopAlt = isOcean ? seaLevel : alt;
            float topR = GoldbergSphereGenerator.VisualRadius + effectiveTopAlt * displacementScale;
            if (topR - floorR < kMinWallHeight) continue;

            int   rimCount  = bv.Length;
            // Ocean tiles: water depth gradient; land tiles: terrain face color.
            Color faceColor = isOcean
                ? WaterCapsBuilder.WaterDepthColor(alt, seaLevel)
                : faces[i].color;

            for (int k = 0; k < rimCount; k++)
            {
                // Top : boundary vertex déjà à l'altitude du tile (H3SphereBuilder).
                // Bottom : plancher absolu pour couvrir les gaps côtiers.
                Vector3 v0 = bv[k];                            // haut gauche
                Vector3 v1 = bv[(k + 1) % rimCount];           // haut droit
                Vector3 v2 = v1.normalized * floorR;            // bas droit
                Vector3 v3 = v0.normalized * floorR;            // bas gauche

                Vector3 edge       = (v1 - v0).normalized;
                Vector3 down       = (v3 - v0).normalized;
                Vector3 raw        = Vector3.Cross(edge, down);
                Vector3 quadCenter = (v0 + v1 + v2 + v3) * 0.25f;

                bool    outward = Vector3.Dot(raw, quadCenter) >= 0f;
                Vector3 normal  = outward ? raw.normalized : -raw.normalized;

                int baseIdx = verts.Count;
                verts.Add(v0);  normals.Add(normal);  colors.Add(faceColor);
                verts.Add(v1);  normals.Add(normal);  colors.Add(faceColor);
                verts.Add(v2);  normals.Add(normal);  colors.Add(faceColor);
                verts.Add(v3);  normals.Add(normal);  colors.Add(faceColor);

                if (outward)
                {
                    tris.Add(baseIdx);     tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    tris.Add(baseIdx);     tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                }
                else
                {
                    tris.Add(baseIdx + 2); tris.Add(baseIdx + 1); tris.Add(baseIdx);
                    tris.Add(baseIdx + 3); tris.Add(baseIdx + 2); tris.Add(baseIdx);
                }
            }
        }

        if (verts.Count == 0) return null;

        var mesh = new Mesh { name = "TileDepthPrisms" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = verts.ToArray();
        mesh.normals   = normals.ToArray();
        mesh.colors    = colors.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }
}
