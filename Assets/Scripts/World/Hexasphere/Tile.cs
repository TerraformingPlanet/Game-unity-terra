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

        /// <summary>Position 3D du centre de la tuile (sur l'icosaèdre non projeté).</summary>
        public Vector3 CenterPosition => _center.Position;

        public void ResolveNeighbourTiles(List<Tile> allTiles)
        {
            List<string> neighbourIds = _neighbourCenters.Select(c => c.ID).ToList();
            _neighbours = allTiles.Where(t => neighbourIds.Contains(t._center.ID)).ToList();
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

            polyPoints.ForEach(pos => _points.Add(new Point(pos).ProjectToSphere(_radius, 0.5f)));

            _faces.Add(new Face(_points[0], _points[1], _points[2]));
            _faces.Add(new Face(_points[0], _points[2], _points[3]));
            _faces.Add(new Face(_points[0], _points[3], _points[4]));
            if (_points.Count > 5)
                _faces.Add(new Face(_points[0], _points[4], _points[5]));
        }
    }
}
