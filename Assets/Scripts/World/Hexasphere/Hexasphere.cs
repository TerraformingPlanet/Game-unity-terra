using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Port C# de hexasphere.js par arscan — https://github.com/arscan/hexasphere.js
// Adapté depuis Em3rgencyLT/Hexasphere (MIT License)
namespace Code.Hexasphere
{
    /// <summary>
    /// Génère un géoïde de Goldberg : 12 pentagones + N hexagones,
    /// structurés comme un icosaèdre subdivisé puis projeté sur une sphère.
    ///
    /// Paramètres :
    ///   radius    — rayon du constructeur. Les sommets des tuiles se retrouvent
    ///               à radius * 0.5f du centre (effet de ProjectToSphere(t=0.5)).
    ///               Passer radius * 2f pour obtenir une sphère visuelle de radius.
    ///   divisions — nombre de subdivisions par face de l'icosaèdre.
    ///               Tiles totales ≈ 10 * divisions² + 2.
    ///   hexSize   — [0.01–1]. 1 = tuiles jointives, < 1 = espacement visible.
    /// </summary>
    public class Hexasphere
    {
        private readonly float         _radius;
        private readonly int           _divisions;
        private          MeshDetails   _meshDetails;
        private          bool          _meshDetailsBuilt;
        private readonly List<Tile>    _tiles;
        private readonly List<Point>   _points;
        private readonly List<Face>    _icosahedronFaces;
        // Snap-grid cache for O(1) point deduplication during subdivision.
        // Resolution matches Point.PointComparisonAccuracy (0.0001 units → snap = 10 000).
        private const    float                              SnapFactor   = 10000f;
        private readonly Dictionary<(int, int, int), Point> _pointLookup =
            new Dictionary<(int, int, int), Point>();

        public Hexasphere(float radius, int divisions, float hexSize)
        {
            _radius    = radius;
            _divisions = divisions;
            _tiles     = new List<Tile>();
            _points    = new List<Point>();

            _icosahedronFaces = ConstructIcosahedron();
            SubdivideIcosahedron();
            ConstructTiles(hexSize);
        }

        public List<Tile>  Tiles       => _tiles;
        public MeshDetails MeshDetails
        {
            get
            {
                if (!_meshDetailsBuilt) { _meshDetails = StoreMeshDetails(); _meshDetailsBuilt = true; }
                return _meshDetails;
            }
        }

        // =====================================================================
        // Construction de l'icosaèdre
        // =====================================================================

        private List<Face> ConstructIcosahedron()
        {
            const float tao  = Mathf.PI / 2f;
            const float size = 100f;

            List<Point> c = new List<Point>
            {
                new Point(new Vector3( size,  tao * size, 0)),   // 0
                new Point(new Vector3(-size,  tao * size, 0)),   // 1
                new Point(new Vector3( size, -tao * size, 0)),   // 2
                new Point(new Vector3(-size, -tao * size, 0)),   // 3
                new Point(new Vector3(0,  size,  tao * size)),   // 4
                new Point(new Vector3(0, -size,  tao * size)),   // 5
                new Point(new Vector3(0,  size, -tao * size)),   // 6
                new Point(new Vector3(0, -size, -tao * size)),   // 7
                new Point(new Vector3( tao * size, 0,  size)),   // 8
                new Point(new Vector3(-tao * size, 0,  size)),   // 9
                new Point(new Vector3( tao * size, 0, -size)),   // 10
                new Point(new Vector3(-tao * size, 0, -size)),   // 11
            };
            c.ForEach(p => CachePoint(p));

            return new List<Face>
            {
                new Face(c[0],  c[1],  c[4],  false),
                new Face(c[1],  c[9],  c[4],  false),
                new Face(c[4],  c[9],  c[5],  false),
                new Face(c[5],  c[9],  c[3],  false),
                new Face(c[2],  c[3],  c[7],  false),
                new Face(c[3],  c[2],  c[5],  false),
                new Face(c[7],  c[10], c[2],  false),
                new Face(c[0],  c[8],  c[10], false),
                new Face(c[0],  c[4],  c[8],  false),
                new Face(c[8],  c[2],  c[10], false),
                new Face(c[8],  c[4],  c[5],  false),
                new Face(c[8],  c[5],  c[2],  false),
                new Face(c[1],  c[0],  c[6],  false),
                new Face(c[3],  c[9],  c[11], false),
                new Face(c[6],  c[10], c[7],  false),
                new Face(c[3],  c[11], c[7],  false),
                new Face(c[11], c[6],  c[7],  false),
                new Face(c[6],  c[0],  c[10], false),
                new Face(c[11], c[1],  c[6],  false),
                new Face(c[9],  c[1],  c[11], false),
            };
        }

        // =====================================================================
        // Subdivision
        // =====================================================================

        private void SubdivideIcosahedron()
        {
            _icosahedronFaces.ForEach(icoFace =>
            {
                List<Point> facePoints = icoFace.Points;
                List<Point> bottomSide = new List<Point> { facePoints[0] };
                List<Point> leftSide   = facePoints[0].Subdivide(facePoints[1], _divisions, CachePoint);
                List<Point> rightSide  = facePoints[0].Subdivide(facePoints[2], _divisions, CachePoint);

                for (int i = 1; i <= _divisions; i++)
                {
                    List<Point> prevBottom = bottomSide;
                    bottomSide = leftSide[i].Subdivide(rightSide[i], i, CachePoint);

                    for (int j = 0; j < i; j++)
                    {
                        new Face(prevBottom[j], bottomSide[j], bottomSide[j + 1]);
                        if (j > 0)
                            new Face(prevBottom[j - 1], prevBottom[j], bottomSide[j]);
                    }
                }
            });
        }

        // =====================================================================
        // Tuiles
        // =====================================================================

        private void ConstructTiles(float hexSize)
        {
            _points.ForEach(pt => _tiles.Add(new Tile(pt, _radius, hexSize)));
            // Build lookup once O(n) so each tile's ResolveNeighbourTiles is O(k) not O(n).
            var tileLookup = new Dictionary<string, Tile>(_tiles.Count);
            foreach (var t in _tiles) tileLookup[t.CenterId] = t;
            _tiles.ForEach(t => t.ResolveNeighbourTiles(tileLookup));
        }

        // =====================================================================
        // MeshDetails (mesh combiné, pour usage de référence)
        // =====================================================================

        private MeshDetails StoreMeshDetails()
        {
            List<Point> vertices  = new List<Point>();
            List<int>   triangles = new List<int>();

            _tiles.ForEach(tile =>
            {
                tile.Points.ForEach(pt => vertices.Add(pt));
                tile.Faces.ForEach(face =>
                    face.Points.ForEach(pt =>
                        triangles.Add(vertices.FindIndex(v => v.ID == pt.ID))));
            });

            return new MeshDetails(vertices.Select(p => p.Position).ToList(), triangles);
        }

        // =====================================================================
        // Cache de points (déduplique les sommets proches)
        // =====================================================================

        private Point CachePoint(Point point)
        {
            var key = (
                Mathf.RoundToInt(point.Position.x * SnapFactor),
                Mathf.RoundToInt(point.Position.y * SnapFactor),
                Mathf.RoundToInt(point.Position.z * SnapFactor));
            if (_pointLookup.TryGetValue(key, out Point existing)) return existing;
            _points.Add(point);
            _pointLookup[key] = point;
            return point;
        }
    }
}
