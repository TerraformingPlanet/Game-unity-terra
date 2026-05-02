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
    /// Génère le mesh de caps d'eau pour toutes les tiles ocean.
    /// Les rim-vertices répliquent le corner-averaging du terrain pour éviter
    /// les artefacts aux côtes (coins partagés avec des tiles land surlevés).
    /// </summary>
    public static Mesh BuildCaps(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        float[]  faceAltitudes,
        float    seaLevel,
        float    displacementScale)
    {
        if (faces == null || faceAltitudes == null) return null;

        // Rayon de base des caps (centroïdes ocean) + offset anti-Z-fight.
        // Les vertices ocean sont clampés à seaLevel par corner-averaging →
        // rayon terrain ocean = VisualRadius + seaLevel * scale exactement.
        float waterR = GoldbergSphereGenerator.VisualRadius + seaLevel * displacementScale + 0.002f;

        // ── Map coin → faces partageant ce coin (pour répliquer le corner averaging) ──
        // Clé = position arrondie à 0.1mm pour regrouper les copies géométriques identiques.
        var cornerFaces = new Dictionary<Vector3Int, List<int>>(faces.Length * 7);
        for (int fi = 0; fi < faces.Length; fi++)
        {
            Vector3[] bv = faces[fi].boundaryVertices;
            if (bv == null) continue;
            for (int k = 0; k < bv.Length; k++)
            {
                var key = RoundKey(bv[k]);
                if (!cornerFaces.TryGetValue(key, out var list))
                    cornerFaces[key] = list = new List<int>(3);
                list.Add(fi);
            }
        }

        var verts   = new List<Vector3>(faces.Length * 7);
        var tris    = new List<int>(faces.Length * 12);
        var normals = new List<Vector3>(faces.Length * 7);

        for (int i = 0; i < faces.Length; i++)
        {
            if (i >= faceAltitudes.Length || faceAltitudes[i] >= seaLevel) continue;

            Vector3[] bv = faces[i].boundaryVertices;
            if (bv == null || bv.Length < 3) continue;

            int rimCount = bv.Length;

            // Centre : centroïde normalisé à waterR
            Vector3 center    = faces[i].centroid3D.normalized * waterR;
            Vector3 normalDir = faces[i].centroid3D.normalized;

            // Coins : appliquer le même corner averaging que le terrain,
            // puis prendre max(waterR, cornerR) pour englober les coins surlevés.
            var rim = new Vector3[rimCount];
            for (int k = 0; k < rimCount; k++)
            {
                float cornerR = waterR; // fallback = seaLevel
                var key = RoundKey(bv[k]);
                if (cornerFaces.TryGetValue(key, out var neighbors) && neighbors.Count > 0)
                {
                    float sum = 0f;
                    foreach (int fi in neighbors)
                    {
                        float alt = (fi >= 0 && fi < faceAltitudes.Length) ? faceAltitudes[fi] : seaLevel;
                        sum += alt;  // altitude brute, même logique que ApplyTopographicDisplacement
                    }
                    float avgAlt = sum / neighbors.Count;
                    float terrainR = GoldbergSphereGenerator.VisualRadius + avgAlt * displacementScale;
                    // max : on ne descend jamais sous waterR (pas de trou dans le cap)
                    cornerR = Mathf.Max(waterR, terrainR + 0.002f);
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
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Arrondi à 0.1mm pour clé de regroupement des coins géométriquement identiques.
    private static Vector3Int RoundKey(Vector3 v) => new Vector3Int(
        Mathf.RoundToInt(v.x * 10000),
        Mathf.RoundToInt(v.y * 10000),
        Mathf.RoundToInt(v.z * 10000));
}
