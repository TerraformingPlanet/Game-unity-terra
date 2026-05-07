using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Port C# de hexasphere.js par arscan — https://github.com/arscan/hexasphere.js
// Adapté depuis Em3rgencyLT/Hexasphere (MIT License)
namespace Code.Hexasphere
{
    public class Point
    {
        private readonly string      _id;
        private readonly Vector3     _position;
        private readonly List<Face>  _faces;

        private const float PointComparisonAccuracy = 0.0001f;

        public Point(Vector3 position)
        {
            _id       = Guid.NewGuid().ToString();
            _position = position;
            _faces    = new List<Face>();
        }

        private Point(Vector3 position, string id, List<Face> faces)
        {
            _id       = id;
            _position = position;
            _faces    = faces;
        }

        public Vector3    Position => _position;
        public string     ID       => _id;
        public List<Face> Faces    => _faces;

        public void AssignFace(Face face) => _faces.Add(face);

        public List<Point> Subdivide(Point target, int count, Func<Point, Point> findDuplicateIfExists)
        {
            List<Point> segments = new List<Point> { this };
            for (int i = 1; i <= count; i++)
            {
                float t = (float)i / count;
                float x = _position.x * (1f - t) + target.Position.x * t;
                float y = _position.y * (1f - t) + target.Position.y * t;
                float z = _position.z * (1f - t) + target.Position.z * t;
                segments.Add(findDuplicateIfExists(new Point(new Vector3(x, y, z))));
            }
            segments.Add(target);
            return segments;
        }

        public Point ProjectToSphere(float radius, float t)
        {
            float projFactor = radius / _position.magnitude;
            return new Point(
                new Vector3(_position.x * projFactor * t,
                            _position.y * projFactor * t,
                            _position.z * projFactor * t),
                _id, _faces);
        }

        public List<Face> GetOrderedFaces()
        {
            if (_faces.Count == 0) return _faces;
            var ordered = new List<Face>(_faces.Count) { _faces[0] };
            Face current = ordered[0];
            var seen = new HashSet<string> { current.ID };
            while (ordered.Count < _faces.Count)
            {
                Face next = null;
                foreach (Face f in _faces)
                {
                    if (seen.Contains(f.ID)) continue;
                    if (f.IsAdjacentToFace(current)) { next = f; break; }
                }
                if (next == null) break; // guard: incomplete adjacency graph
                current = next;
                ordered.Add(current);
                seen.Add(current.ID);
            }
            return ordered;
        }

        public static bool IsOverlapping(Point a, Point b) =>
            Mathf.Abs(a.Position.x - b.Position.x) <= PointComparisonAccuracy &&
            Mathf.Abs(a.Position.y - b.Position.y) <= PointComparisonAccuracy &&
            Mathf.Abs(a.Position.z - b.Position.z) <= PointComparisonAccuracy;

        public string ToJson() =>
            $"{{\"x\":{_position.x},\"y\":{_position.y},\"z\":{_position.z},\"guid\":\"{_id}\"}}";

        public override string ToString() =>
            $"{_position.x},{_position.y},{_position.z}";
    }
}
