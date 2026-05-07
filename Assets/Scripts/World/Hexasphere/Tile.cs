using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Port C# de hexasphere.js par arscan — https://github.com/arscan/hexasphere.js
// Adapté depuis Em3rgencyLT/Hexasphere (MIT License)
namespace Code.Hexasphere
{
    public class Tile
    {
        private readonly Point       _center;
        private readonly float       _radius;
        private readonly float       _size;
        private readonly List<Face>  _faces;
        private readonly List<Point> _points;
        private readonly List<Point> _neighbourCenters;
        private List<Tile>           _neighbours;

        public Tile(Point center, float radius, float size)
        {
            _points           = new List<Point>();
            _faces            = new List<Face>();
            _neighbourCenters = new List<Point>();
            _neighbours       = new List<Tile>();

            _center = center;
            _radius = radius;
            _size   = Mathf.Max(0.01f, Mathf.Min(1f, size));

            List<Face> icoFaces = center.GetOrderedFaces();
            StoreNeighbourCenters(icoFaces);
            BuildFaces(icoFaces);
        }

        public List<Point> Points     => _points;
        public List<Face>  Faces      => _faces;
        public List<Tile>  Neighbours => _neighbours;
        public string      CenterId   => _center.ID;

        /// <summary>Position 3D du centre de la tuile (sur l'icosaèdre non projeté).</summary>
        public Vector3 CenterPosition => _center.Position;

        public void ResolveNeighbourTiles(Dictionary<string, Tile> tileLookup)
        {
            _neighbours = new List<Tile>(_neighbourCenters.Count);
            foreach (var nc in _neighbourCenters)
            {
                if (tileLookup.TryGetValue(nc.ID, out Tile t))
                    _neighbours.Add(t);
            }
        }

        public string ToJson() =>
            $"{{\"centerPoint\":{_center.ToJson()},\"boundary\":[{string.Join(",", _points.Select(p => p.ToJson()))}]}}";

        public override string ToString() =>
            $"{_center.Position.x},{_center.Position.y},{_center.Position.z}";

        private void StoreNeighbourCenters(List<Face> icoFaces)
        {
            icoFaces.ForEach(face =>
            {
                face.GetOtherPoints(_center).ForEach(pt =>
                {
                    if (_neighbourCenters.All(nc => nc.ID != pt.ID))
                        _neighbourCenters.Add(pt);
                });
            });
        }

        private void BuildFaces(List<Face> icoFaces)
        {
            List<Vector3> polyPoints = icoFaces
                .Select(f => Vector3.Lerp(_center.Position, f.GetCenter().Position, _size))
                .ToList();

            // Centroïde réel de la tuile (milieu géométrique de tous les coins).
            // Utilisé comme apex du fan de triangles → chaque tile a un vertex "centre"
            // qui reçoit l'altitude complète de la tile lors du déplacement topographique.
            // Les vertices aux coins reçoivent la moyenne des tiles voisines (corner averaging).
            Vector3 centroid = Vector3.zero;
            foreach (var p in polyPoints) centroid += p;
            centroid /= polyPoints.Count;

            // pts[0] = centroïde de la tile (NON partagé avec les voisins)
            // pts[1..N] = coins du polygone (partagés géométriquement avec les voisins)
            _points.Add(new Point(centroid).ProjectToSphere(_radius, 0.5f));
            polyPoints.ForEach(pos => _points.Add(new Point(pos).ProjectToSphere(_radius, 0.5f)));

            // Fan depuis le centroïde : (centroid, rim[i], rim[i+1])
            int rimCount = polyPoints.Count;
            for (int i = 0; i < rimCount; i++)
                _faces.Add(new Face(_points[0], _points[1 + i], _points[1 + (i + 1) % rimCount]));
        }
    }
}
