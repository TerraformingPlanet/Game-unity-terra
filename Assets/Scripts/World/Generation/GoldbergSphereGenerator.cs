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
        /// <summary>
        /// faceVertexIndices[faceId] = indices des sommets appartenant à cette face.
        /// Permet un accès O(1) aux vertices d'une face sans scanner vertexFaceId[].
        /// </summary>
        public int[][] faceVertexIndices;
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
        int[] cornerGroups      = BuildCornerGroups(vertices);
        int[][] faceVertexIdx   = BuildFaceVertexIndices(tiles.Count, vertexFaceIdList);

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
            mesh               = mesh,
            faces              = faces,
            vertexFaceId       = vertexFaceIdList.ToArray(),
            vertexCornerGroup  = cornerGroups,
            faceVertexIndices  = faceVertexIdx
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
    /// Precompute for each face the list of vertex indices that belong to it.
    /// Avoids O(vertexCount) scans in TintFace / HideFaceOnSphere by providing
    /// direct O(1) access to a face’s vertex subset.
    /// </summary>
    private static int[][] BuildFaceVertexIndices(int faceCount, List<int> vertexFaceIdList)
    {
        var counts = new int[faceCount];
        foreach (int fid in vertexFaceIdList) counts[fid]++;
        var result = new int[faceCount][];
        for (int i = 0; i < faceCount; i++) result[i] = new int[counts[i]];
        var cursors = new int[faceCount];
        for (int vi = 0; vi < vertexFaceIdList.Count; vi++)
        {
            int fid = vertexFaceIdList[vi];
            result[fid][cursors[fid]++] = vi;
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

        // Altitude uniforme par tuile (mode prisme) : tous les verts d'une face
        // déplacés au même rayon → top-face parfaitement plate.
        // Les faces latérales (TilePrismsBuilder) gèrent la transition verticale.
        for (int i = 0; i < verts.Length; i++)
        {
            int   fid = vertexFaceId[i];
            float alt = (fid >= 0 && fid < faceAltitudes.Length) ? faceAltitudes[fid] : 0f;
            verts[i] = verts[i].normalized * (VisualRadius + alt * displacementScale);
        }

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

    /// <summary>
    /// Applique les vertex colors avec un gradient de rim : les corners physiquement partagés
    /// entre plusieurs tuiles fondent leur couleur vers la moyenne des voisins.
    /// Le centroïde de chaque tuile (corner n'appartenant qu'à une seule face) garde
    /// sa couleur pure — seuls les bords dégradent.
    /// </summary>
    /// <param name="rimBlend">[0,1]. 0 = identique à ApplyFaceColors. 0.40 = équilibré. 1 = fusion totale.</param>
    /// <param name="maxBlendColorDelta">Distance RGB max pour qu'un voisin soit inclus dans la moyenne.
    /// 0 = illimité. 0.45 = bloque eau↔terre (~0.8), autorise terre↔terre (~0.25).</param>
    public static void ApplyFaceColorsBlended(
        Mesh mesh,
        GoldbergFace[] faces,
        int[] vertexFaceId,
        int[] vertexCornerGroup,
        float rimBlend = 0.40f,
        float maxBlendColorDelta = 0f)
    {
        if (mesh == null) return;
        if (vertexCornerGroup == null || vertexCornerGroup.Length != vertexFaceId.Length)
        {
            ApplyFaceColors(mesh, faces, vertexFaceId);
            return;
        }

        // Pré-calcul : groupId → liste des faceIds distincts qui partagent ce coin.
        // Les centroïdes ont un groupId unique à une seule face.
        var groupFaces = new Dictionary<int, List<int>>(vertexFaceId.Length / 4);
        for (int i = 0; i < vertexFaceId.Length; i++)
        {
            int gid = vertexCornerGroup[i];
            int fid = vertexFaceId[i];
            if (!groupFaces.TryGetValue(gid, out var list))
            {
                list = new List<int>(4);
                groupFaces[gid] = list;
            }
            if (!list.Contains(fid)) list.Add(fid);
        }

        Color[] meshColors = new Color[mesh.vertexCount];
        for (int i = 0; i < meshColors.Length; i++)
        {
            int   fid  = vertexFaceId[i];
            int   gid  = vertexCornerGroup[i];
            Color own  = faces[fid].color;

            if (!groupFaces.TryGetValue(gid, out var neighbors) || neighbors.Count < 2)
            {
                // Centroïde ou coin isolé → couleur pure.
                meshColors[i] = own;
                continue;
            }

            // Moyenne des faces voisines (excluant la face courante).
            // maxBlendColorDelta > 0 : les voisins dont la couleur diffère trop sont exclus
            // (ex. eau bleu foncé vs sable vert : distance ~0.8 >> seuil 0.45 → bloqué).
            float r = 0f, g = 0f, b = 0f, a = 0f;
            int   count = 0;
            float deltaSq = maxBlendColorDelta * maxBlendColorDelta;
            foreach (int nf in neighbors)
            {
                if (nf == fid) continue;
                Color nc = faces[nf].color;
                if (maxBlendColorDelta > 0f)
                {
                    float dr = nc.r - own.r, dg = nc.g - own.g, db = nc.b - own.b;
                    if (dr*dr + dg*dg + db*db > deltaSq) continue;
                }
                r += nc.r; g += nc.g; b += nc.b; a += nc.a;
                count++;
            }

            if (count > 0)
            {
                float inv = 1f / count;
                Color avg = new Color(r * inv, g * inv, b * inv, a * inv);
                meshColors[i] = Color.Lerp(own, avg, rimBlend);
            }
            else
            {
                meshColors[i] = own;
            }
        }

        mesh.colors = meshColors;
    }
}
