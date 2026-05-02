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
        /// <summary>Sommets ordonnés du polygone de la tuile (espace local, ~VisualRadius). Utilisé pour dessiner les frontières d'ownership.</summary>
        public Vector3[] boundaryVertices;
    }

    public struct GoldbergMeshData
    {
        /// <summary>Mesh Unity avec vertex colors. Chaque tuile a ses propres sommets.</summary>
        public Mesh mesh;
        /// <summary>Une entrée par tuile.</summary>
        public GoldbergFace[] faces;
        /// <summary>vertexFaceId[i] = index de la tuile propriétaire du sommet i.</summary>
        public int[] vertexFaceId;
        /// <summary>
        /// vertexCornerGroup[i] = id du coin géométrique partagé entre plusieurs tuiles.
        /// Tous les sommets au même coin (de tuiles différentes) ont le même id.
        /// Utilisé par ApplyTopographicDisplacement pour moyenner les altitudes au coin.
        /// </summary>
        public int[] vertexCornerGroup;
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
        var uvs             = new List<Vector2>(tiles.Count * 12);
        var uv1s            = new List<Vector2>(tiles.Count * 12);
        var vertexFaceIdList = new List<int>(tiles.Count * 12);

        BuildTileMeshData(tiles, faces, vertices, triangles, colors, uvs, uv1s, vertexFaceIdList);

        // Corner groups : chaque coin géométrique (partagé par 3 tuiles) reçoit un id unique.
        // Sert à moyenner l'altitude des 3 tuiles au coin → surface qui se déforme naturellement
        // à chaque bord, sans géométrie de remplissage (skirt).
        int[] cornerGroups = BuildCornerGroups(vertices);

        Mesh mesh = new Mesh { name = "GoldbergSphere" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors    = colors.ToArray();
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv1s);  // UV centroïde : displacement uniforme par tuile (anti-mesa)
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return new GoldbergMeshData
        {
            mesh              = mesh,
            faces             = faces,
            vertexFaceId      = vertexFaceIdList.ToArray(),
            vertexCornerGroup = cornerGroups
        };
    }

    /// <summary>
    /// Pass 1 : remplit faces[], vertices, triangles, colors, uvs et vertexFaceIdList
    /// depuis les tuiles de la Hexasphere. Chaque tuile produit des sommets indépendants
    /// (non partagés) pour permettre une coloration distincte par face.
    /// </summary>
    private static void BuildTileMeshData(
        List<Tile>    tiles,
        GoldbergFace[] faces,
        List<Vector3> vertices,
        List<int>     triangles,
        List<Color>   colors,
        List<Vector2> uvs,
        List<Vector2> uv1s,
        List<int>     vertexFaceIdList)
    {
        for (int i = 0; i < tiles.Count; i++)
        {
            Tile tile = tiles[i];

            // tile.Points[0] = centroïde (ajouté en premier par BuildFaces).
            // tile.Points[1..N] = coins du polygone (rim vertices).
            // Calculer la direction depuis les RIM uniquement pour des lat/lon corrects.
            Vector3 centroid = Vector3.zero;
            for (int k = 1; k < tile.Points.Count; k++) centroid += tile.Points[k].Position;
            centroid /= (tile.Points.Count - 1);
            Vector3 centroidDir = centroid.normalized;

            float latDeg  = Mathf.Asin(Mathf.Clamp(centroidDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float lonDeg  = Mathf.Atan2(centroidDir.z, centroidDir.x) * Mathf.Rad2Deg;

            faces[i] = new GoldbergFace
            {
                faceId     = i,
                centroid3D = centroidDir,
                latDeg     = latDeg,
                lonDeg     = lonDeg,
                latNorm    = (latDeg + 90f)  / 180f,
                lonNorm    = (lonDeg + 180f) / 360f,
                color      = Color.gray
            };

            // boundaryVertices = coins du polygone seulement (skip centroïde à l'index 0).
            // Utilisé pour dessiner les frontières d'ownership.
            var bv = new Vector3[tile.Points.Count - 1];
            for (int k = 1; k < tile.Points.Count; k++) bv[k - 1] = tile.Points[k].Position;
            faces[i].boundaryVertices = bv;

            // UV1 = UV du centroïde de la tuile (même pour tous les vertices de la tuile).
            // Utilisé par le shader pour un displacement UNIFORME → pas d'effet mesa.
            Vector2 centroidUV = new Vector2(
                Mathf.Atan2(centroidDir.z, centroidDir.x) / (2f * Mathf.PI) + 0.5f,
                Mathf.Asin(Mathf.Clamp(centroidDir.y, -1f, 1f)) / Mathf.PI + 0.5f);

            foreach (Face tileFace in tile.Faces)
            {
                int startIdx = vertices.Count;
                foreach (Point pt in tileFace.Points)
                {
                    vertices.Add(pt.Position);
                    colors.Add(Color.gray);
                    vertexFaceIdList.Add(i);
                    Vector3 d = pt.Position.normalized;
                    uvs.Add(new Vector2(
                        Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) + 0.5f,
                        Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI + 0.5f));
                    uv1s.Add(centroidUV);
                }
                triangles.Add(startIdx);
                triangles.Add(startIdx + 1);
                triangles.Add(startIdx + 2);
            }
        }
    }

    /// <summary>
    /// Groupe les sommets par position géométrique : tous les sommets (de tuiles
    /// différentes) au même coin physique reçoivent le même group id.
    /// Avec hexSize=1.0, les positions sont exactement identiques entre tuiles voisines
    /// (ProjectToSphere appelé avec le même point d'entrée), donc un snap 0.001 suffit.
    /// </summary>
    private static int[] BuildCornerGroups(List<Vector3> vertices)
    {
        const float SnapInv = 1000f; // grille 0.001 unités
        var keyToGroup = new Dictionary<(int, int, int), int>(vertices.Count);
        var result     = new int[vertices.Count];
        int nextGroup  = 0;

        for (int i = 0; i < vertices.Count; i++)
        {
            var key = (
                Mathf.RoundToInt(vertices[i].x * SnapInv),
                Mathf.RoundToInt(vertices[i].y * SnapInv),
                Mathf.RoundToInt(vertices[i].z * SnapInv)
            );
            if (!keyToGroup.TryGetValue(key, out int grp))
            {
                grp = nextGroup++;
                keyToGroup[key] = grp;
            }
            result[i] = grp;
        }
        return result;
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
    /// Déplace les sommets du mesh selon les altitudes des faces (relief topographique).
    /// Avec vertexCornerGroup : chaque coin géométrique (partagé par 3 tuiles) reçoit
    /// l'altitude moyenne des tuiles qui s'y rejoignent — la surface se déforme aux bords
    /// sans aucun gap entre tuiles voisines à altitudes différentes.
    /// L'altitude est utilisée brute (pas de clamp à seaLevel) : les tiles ocean se
    /// creusent visuellement, les water caps couvrent la surface de l'eau depuis le dessus.
    /// Sans vertexCornerGroup (null) : comportement plat par tuile (legacy).
    /// </summary>
    public static void ApplyTopographicDisplacement(
        Mesh    mesh,
        int[]   vertexFaceId,
        float[] faceAltitudes,
        float   displacementScale,
        int[]   vertexCornerGroup = null,
        float   seaLevelAltitude  = float.NegativeInfinity)
    {
        if (mesh == null || vertexFaceId == null || faceAltitudes == null) return;

        Vector3[] verts = mesh.vertices;
        float[]   vertexAlts;

        if (vertexCornerGroup != null)
        {
            // ── Corner averaging ──────────────────────────────────────────────────
            // Chaque coin géométrique est partagé par 3 tuiles → chaque tuile contribue
            // max(alt, seaLevel) dans la somme. Le centroïde de chaque tile est dans un
            // groupe unique (N copies d'une même tile) → alt = max(tileAlt, seaLevel).
            // Garantit que TOUTES les copies d'un coin ont la MÊME altitude → zéro gap.
            var groupSum   = new float[verts.Length];
            var groupCount = new int[verts.Length];

            for (int i = 0; i < verts.Length; i++)
            {
                int   fid = vertexFaceId[i];
                float alt = (fid >= 0 && fid < faceAltitudes.Length) ? faceAltitudes[fid] : seaLevelAltitude;
                int   grp = vertexCornerGroup[i];
                groupSum[grp]   += alt;  // altitude brute : ocean se creuse, montagne s'élève
                groupCount[grp] += 1;
            }

            vertexAlts = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int grp = vertexCornerGroup[i];
                // Même altitude pour TOUTES les copies du même coin → garanti zéro gap.
                // Pas de 2e clamp par tile : il créait des altitudes différentes entre copies.
                vertexAlts[i] = groupSum[grp] / groupCount[grp];
            }
        }
        else
        {
            // Legacy : altitude plate par tuile
            vertexAlts = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int fid = vertexFaceId[i];
                vertexAlts[i] = (fid >= 0 && fid < faceAltitudes.Length) ? faceAltitudes[fid] : 0f;
            }
        }

        for (int i = 0; i < verts.Length; i++)
            verts[i] = verts[i].normalized * (VisualRadius + vertexAlts[i] * displacementScale);

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Réinitialise tous les sommets du mesh à leur position de sphère parfaite (radius = VisualRadius).
    /// Appelé avant de ré-appliquer un déplacement topographique avec un nouveau scale.
    /// </summary>
    public static void ResetTopographicDisplacement(Mesh mesh)
    {
        if (mesh == null) return;
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
            verts[i] = verts[i].normalized * VisualRadius;
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
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
