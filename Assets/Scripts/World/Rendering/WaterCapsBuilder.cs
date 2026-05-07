using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Construit un mesh "water caps" : une nappe triangulée épousant exactement la topologie
/// du polyèdre Goldberg pour les tiles ocean.
///
/// Principe :
///   Pour chaque tile ocean, on génère un fan depuis le centroïde.
///   Le centroïde est projeté à waterR (seaLevel * scale + offset anti-Z-fight).
///   Chaque coin de rim est projeté à max(waterR, rayon_corner_averaged) pour
///   que les caps épousent exactement la topologie du terrain même aux côtes
///   (évite que les coins surelevés par averaging avec des tiles land percent à travers).
///
/// Résultat : 1 seul Mesh combiné, 1 draw call avec le water material.
/// </summary>
public static class WaterCapsBuilder
{
    /// <summary>
    /// Génère le mesh de caps d'eau pour toutes les tiles sélectionnées par <c>isFaceOcean</c>.
    /// Les rim-vertices répliquent le corner-averaging du terrain pour éviter
    /// les artefacts aux côtes (coins partagés avec des tiles land surlevés).
    /// <param name="isFaceOcean">
    /// Masque des faces à couvrir (océan ou lac).
    /// </param>
    /// <param name="clampRimToFaceAlt">
    /// Si true, le rim de chaque face est plafonné à <c>faceAlt + kRimFaceTolerance</c>.
    /// Utiliser pour les lacs : empêche les voisins montagnards d'élever le rim
    /// en tente au-dessus du terrain. Ne pas utiliser pour l'océan (les rims doivent
    /// suivre la côte terrestre pour éviter les lacunes).
    /// </param>
    /// </summary>
    public static Mesh BuildCaps(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        float[]  faceAltitudes,
        float    seaLevel,
        float    displacementScale,
        bool[]   isFaceOcean = null,
        bool     clampRimToFaceAlt = false)
    {
        if (faces == null || faceAltitudes == null) return null;

        // Rayon de base des caps (centroïdes ocean) + offset anti-Z-fight.
        float waterR = GoldbergSphereGenerator.VisualRadius + seaLevel * displacementScale + 0.002f;
        // Tolérance de rim pour le mode lac : les coins d'un lac ne montent pas plus haut
        // que l'altitude de la face + cette tolérance (empêche la « tente » montagneuse).
        const float kRimFaceTolerance = 0.05f;

        // ── Map coin → faces partageant ce coin (pour répliquer le corner averaging) ──
        var cornerFaces = BuildCornerFacesMap(faces);

        var verts   = new List<Vector3>(faces.Length * 7);
        var tris    = new List<int>(faces.Length * 12);
        var normals = new List<Vector3>(faces.Length * 7);
        var colors  = new List<Color>(faces.Length * 7);   // gradient profondeur → côtier

        for (int i = 0; i < faces.Length; i++)
        {
            // Double condition : classification serveur OU altitude sous seaLevel.
            // Le vote k=3 peut échouer aux frontières terre/mer (GP face à cheval sur plusieurs H3)
            // → l'altitude sert de filet de sécurité pour éviter les trous dans les water caps.
            bool byClassification = isFaceOcean != null && i < isFaceOcean.Length && isFaceOcean[i];
            bool byAltitude       = i < faceAltitudes.Length && faceAltitudes[i] < seaLevel;
            bool isOcean = byClassification || byAltitude;
            if (!isOcean) continue;

            Vector3[] bv = faces[i].boundaryVertices;
            if (bv == null || bv.Length < 3) continue;

            int rimCount = bv.Length;

            // Pour les lacs (clampRimToFaceAlt), le cap est à l'altitude de la tile elle-même,
            // pas au sea level global — sinon le cap est en-dessous de la surface de la tile
            // et ses bords percent en oblique à travers les gaps entre tiles.
            float tileWaterR = waterR;
            if (clampRimToFaceAlt && i < faceAltitudes.Length)
                tileWaterR = GoldbergSphereGenerator.VisualRadius
                           + faceAltitudes[i] * displacementScale + 0.002f;

            // Centre : centroïde normalisé à tileWaterR
            Vector3 center    = faces[i].centroid3D.normalized * tileWaterR;
            Vector3 normalDir = faces[i].centroid3D.normalized;

            // Coins : appliquer le même corner averaging que le terrain,
            // puis prendre max(waterR, cornerR) pour englober les coins surlevés.
            var rim = new Vector3[rimCount];
            for (int k = 0; k < rimCount; k++)
            {
                float cornerR = tileWaterR;
                // Mode lac : plafond = altitude de la face (le cap ne monte pas au-delà de la surface)
                if (clampRimToFaceAlt && i < faceAltitudes.Length)
                {
                    float faceR = GoldbergSphereGenerator.VisualRadius
                                + faceAltitudes[i] * displacementScale + kRimFaceTolerance;
                    cornerR = Mathf.Clamp(cornerR, tileWaterR, faceR);
                }
                rim[k] = bv[k].normalized * cornerR;
            }

            int baseIdx = verts.Count;
            verts.Add(center);
            normals.Add(normalDir);
            for (int k = 0; k < rimCount; k++)
            {
                verts.Add(rim[k]);
                normals.Add(bv[k].normalized);
            }

            // Couleur de profondeur uniforme par face (centre + rim).
            // Visible si le matériau water supporte les vertex colors (Sprites/Default, Particles/Unlit...).
            Color depthCol = WaterDepthColor(i < faceAltitudes.Length ? faceAltitudes[i] : seaLevel, seaLevel);
            for (int k = 0; k <= rimCount; k++)
                colors.Add(depthCol);

            for (int k = 0; k < rimCount; k++)
            {
                tris.Add(baseIdx);
                tris.Add(baseIdx + 1 + k);
                tris.Add(baseIdx + 1 + (k + 1) % rimCount);
            }
        }

        if (verts.Count == 0) return null;

        var mesh = new Mesh { name = "WaterCaps" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = verts.ToArray();
        mesh.normals   = normals.ToArray();
        mesh.colors    = colors.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Arrondi à 0.1mm pour clé de regroupement des coins géométriquement identiques.
    internal static Vector3Int RoundKey(Vector3 v) => new Vector3Int(
        Mathf.RoundToInt(v.x * 10000),
        Mathf.RoundToInt(v.y * 10000),
        Mathf.RoundToInt(v.z * 10000));

    /// <summary>
    /// Construit la map coin → faces partageant ce coin.
    /// Clé = position arrondie à 0.1mm. Chaque coin géométrique est partagé par exactement 3 faces.
    /// Partagée avec BeachBuilder pour éviter la duplication du calcul de corner averaging.
    /// </summary>
    internal static Dictionary<Vector3Int, List<int>> BuildCornerFacesMap(
        GoldbergSphereGenerator.GoldbergFace[] faces)
    {
        var map = new Dictionary<Vector3Int, List<int>>(faces.Length * 7);
        for (int fi = 0; fi < faces.Length; fi++)
        {
            Vector3[] bv = faces[fi].boundaryVertices;
            if (bv == null) continue;
            for (int k = 0; k < bv.Length; k++)
            {
                var key = RoundKey(bv[k]);
                if (!map.TryGetValue(key, out var list))
                    map[key] = list = new List<int>(3);
                list.Add(fi);
            }
        }
        return map;
    }

    /// <summary>
    /// Calcule l'altitude corner-averaged pour un coin donné (même logique que ApplyTopographicDisplacement).
    /// </summary>
    internal static float CornerAverageAlt(Vector3Int key, Dictionary<Vector3Int, List<int>> cornerFaces,
        float[] faceAltitudes, float fallbackAlt)
    {
        if (!cornerFaces.TryGetValue(key, out var neighbors) || neighbors.Count == 0)
            return fallbackAlt;
        float sum = 0f;
        foreach (int fi in neighbors)
            sum += (fi >= 0 && fi < faceAltitudes.Length) ? faceAltitudes[fi] : fallbackAlt;
        return sum / neighbors.Count;
    }

    /// <summary>
    /// Couleur de profondeur pour une face océanique.
    /// Côtier (proche du niveau de mer) = turquoise lumineux.
    /// Profond (altitude très basse) = bleu marine sombre.
    /// Curve quadratique : beaucoup de surface paraît "côtière" même à profondeur modérée.
    /// </summary>
    internal static Color WaterDepthColor(float faceAlt, float seaLevel)
    {
        float depth = Mathf.Clamp01(seaLevel - faceAlt);
        float t = Mathf.Sqrt(depth);
        return Color.Lerp(
            new Color(0.024f, 0.259f, 0.451f, 0.90f),  // côtier : bleu WaterSphere (cohérence visuelle)
            new Color(0.02f,  0.10f,  0.28f,  0.97f),  // profond : bleu marine très sombre
            t);
    }
}
