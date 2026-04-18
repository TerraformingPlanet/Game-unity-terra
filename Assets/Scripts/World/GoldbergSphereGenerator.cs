using System.Collections.Generic;
using Code.Hexasphere;
using UnityEngine;

/// <summary>
/// Génère le mesh d'une sphère de Goldberg à partir de la lib Hexasphere.
///
/// Architecture :
///   - VisualRadius = 10f : rayon visuel des tuiles en unités monde.
///   - HexasphereConstructorRadius = VisualRadius * 2f : compensé par
///     ProjectToSphere(t=0.5f) interne à la lib, qui produit des sommets à radius*0.5.
///   - Chaque tuile génère ses propres sommets indépendants (pas de partage
///     entre tuiles) pour permettre les vertex colors par tuile.
///   - vertexFaceId[i] → index de la tuile propriétaire du sommet i.
/// </summary>
public static class GoldbergSphereGenerator
{
    /// <summary>Rayon visuel de la sphère (unités monde). Immuable — utilisez localScale pour ajuster.</summary>
    public const float VisualRadius = 10f;

    // Compensé par le t=0.5f de ProjectToSphere dans la lib Hexasphere.
    private const float HexasphereConstructorRadius = VisualRadius * 2f;

    // =========================================================
    // Types publics
    // =========================================================

    public struct GoldbergFace
    {
        /// <summary>Index dans le tableau faces[].</summary>
        public int faceId;
        /// <summary>Direction normalisée du centroïde (sphère unité).</summary>
        public Vector3 centroid3D;
        /// <summary>Latitude en degrés [-90, +90].</summary>
        public float latDeg;
        /// <summary>Longitude en degrés [-180, +180].</summary>
        public float lonDeg;
        /// <summary>Latitude normalisée [0, 1] (0 = pôle sud).</summary>
        public float latNorm;
        /// <summary>Longitude normalisée [0, 1].</summary>
        public float lonNorm;
        /// <summary>Couleur de la tuile (vertex color). Modifiable par GoldbergFaceColorizer.</summary>
        public Color color;
    }

    public struct GoldbergMeshData
    {
        /// <summary>Mesh Unity avec vertex colors. Chaque tuile a ses propres sommets.</summary>
        public Mesh mesh;
        /// <summary>Une entrée par tuile.</summary>
        public GoldbergFace[] faces;
        /// <summary>vertexFaceId[i] = index de la tuile propriétaire du sommet i.</summary>
        public int[] vertexFaceId;
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Génère la sphère GP pour un corps céleste donné.
    /// La fréquence de subdivision est dérivée du rayon du corps.
    /// </summary>
    public static GoldbergMeshData Generate(OrbitalBody body)
    {
        int divisions = ComputeDivisions(body != null ? body.radius : 6371f);
        return GenerateWithDivisions(divisions);
    }

    /// <summary>Génère avec un nombre de subdivisions explicite.</summary>
    public static GoldbergMeshData GenerateWithDivisions(int divisions)
    {
        divisions = Mathf.Clamp(divisions, 2, 15);

        var sphere = new Hexasphere(HexasphereConstructorRadius, divisions, 1.0f);
        List<Tile> tiles = sphere.Tiles;

        var faces           = new GoldbergFace[tiles.Count];
        var vertices        = new List<Vector3>(tiles.Count * 12);
        var triangles       = new List<int>(tiles.Count * 12);
        var colors          = new List<Color>(tiles.Count * 12);
        var vertexFaceIdList = new List<int>(tiles.Count * 12);

        for (int i = 0; i < tiles.Count; i++)
        {
            Tile tile = tiles[i];

            // ---- Centroïde : moyenne des sommets de la tuile ----
            Vector3 centroid = Vector3.zero;
            foreach (Point pt in tile.Points)
                centroid += pt.Position;
            centroid /= tile.Points.Count;
            Vector3 centroidDir = centroid.normalized;

            float latDeg  = Mathf.Asin(Mathf.Clamp(centroidDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float lonDeg  = Mathf.Atan2(centroidDir.z, centroidDir.x) * Mathf.Rad2Deg;
            float latNorm = (latDeg + 90f)  / 180f;
            float lonNorm = (lonDeg + 180f) / 360f;

            faces[i] = new GoldbergFace
            {
                faceId     = i,
                centroid3D = centroidDir,
                latDeg     = latDeg,
                lonDeg     = lonDeg,
                latNorm    = latNorm,
                lonNorm    = lonNorm,
                color      = Color.gray
            };

            // ---- Triangles depuis les faces déjà tessélées de la lib ----
            // Chaque tileFace.Points a 3 sommets correctement orientés.
            // On ajoute des sommets indépendants (non partagés entre tuiles)
            // pour pouvoir leur assigner des couleurs distinctes.
            foreach (Face tileFace in tile.Faces)
            {
                int startIdx = vertices.Count;
                foreach (Point pt in tileFace.Points)
                {
                    vertices.Add(pt.Position);
                    colors.Add(Color.gray);
                    vertexFaceIdList.Add(i);
                }
                triangles.Add(startIdx);
                triangles.Add(startIdx + 1);
                triangles.Add(startIdx + 2);
            }
        }

        Mesh mesh = new Mesh { name = "GoldbergSphere" };
        mesh.vertices  = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors    = colors.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return new GoldbergMeshData
        {
            mesh         = mesh,
            faces        = faces,
            vertexFaceId = vertexFaceIdList.ToArray()
        };
    }

    /// <summary>
    /// Calcule le nombre de subdivisions N tel que 10·N²+2 ≈ cols×rows de la grille
    /// Mercator plate équivalente. Garantit la même résolution visuelle entre les deux vues.
    /// Cap à 15 (2252 tuiles) pour des performances acceptables.
    /// </summary>
    public static int ComputeDivisions(float radiusKm)
    {
        float norm = Mathf.Clamp01(radiusKm / PlanetaryHexGrid.MaxReferenceRadiusKm);
        // Même normalisation que PlanetaryHexGrid.GetDimensions
        float cols = Mathf.Lerp(PlanetaryHexGrid.MinCols, PlanetaryHexGrid.MaxCols, norm);
        float rows = Mathf.Lerp(PlanetaryHexGrid.MinRows, PlanetaryHexGrid.MaxRows, norm);
        // N tel que 10·N²+2 ≈ cols×rows
        int n = Mathf.RoundToInt(Mathf.Sqrt(cols * rows / 10f));
        return Mathf.Clamp(n, 2, 15);
    }

    /// <summary>
    /// Trouve l'index de la tuile dont le centroïde est le plus proche
    /// de la direction donnée (distance angulaire minimale).
    /// </summary>
    public static int FindNearestFaceId(GoldbergFace[] faces, Vector3 direction)
    {
        direction = direction.normalized;
        int   nearest = 0;
        float best    = float.MinValue;

        for (int i = 0; i < faces.Length; i++)
        {
            float dot = Vector3.Dot(faces[i].centroid3D, direction);
            if (dot > best)
            {
                best    = dot;
                nearest = i;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Applique le tableau colors[] des faces sur le mesh via les vertex colors.
    /// Appeler après avoir modifié faces[i].color via GoldbergFaceColorizer.
    /// </summary>
    public static void ApplyFaceColors(Mesh mesh, GoldbergFace[] faces, int[] vertexFaceId)
    {
        if (mesh == null) return;
        Color[] meshColors = new Color[mesh.vertexCount];
        for (int i = 0; i < meshColors.Length; i++)
            meshColors[i] = faces[vertexFaceId[i]].color;
        mesh.colors = meshColors;
    }
}
