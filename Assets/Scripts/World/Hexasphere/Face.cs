using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Port C# de hexasphere.js par arscan — https://github.com/arscan/hexasphere.js
// Adapté depuis Em3rgencyLT/Hexasphere (MIT License)
namespace Code.Hexasphere
{
    public class Face
    {
        private readonly string _id;
        private readonly List<Point> _points;

        public Face(Point point1, Point point2, Point point3, bool trackFaceInPoints = true)
        {
            _id = Guid.NewGuid().ToString();

            float centerX = (point1.Position.x + point2.Position.x + point3.Position.x) / 3f;
            float centerY = (point1.Position.y + point2.Position.y + point3.Position.y) / 3f;
            float centerZ = (point1.Position.z + point2.Position.z + point3.Position.z) / 3f;
            Vector3 center = new Vector3(centerX, centerY, centerZ);

            Vector3 normal = GetNormal(point1, point2, point3);
            _points = IsNormalPointingAwayFromOrigin(center, normal)
                ? new List<Point> { point1, point2, point3 }
                : new List<Point> { point1, point3, point2 };

            if (trackFaceInPoints)
                _points.ForEach(p => p.AssignFace(this));
        }

        public string ID => _id;
        public List<Point> Points => _points;

        public List<Point> GetOtherPoints(Point point)
        {
            if (!IsPointPartOfFace(point))
                throw new ArgumentException("Given point must be one of the points on the face!");
            return _points.Where(fp => fp.ID != point.ID).ToList();
        }

        public bool IsAdjacentToFace(Face face)
        {
            List<string> thisIds  = _points.Select(p => p.ID).ToList();
            List<string> otherIds = face.Points.Select(p => p.ID).ToList();
            return thisIds.Intersect(otherIds).Count() == 2;
        }

        public Point GetCenter()
        {
            float cx = (_points[0].Position.x + _points[1].Position.x + _points[2].Position.x) / 3f;
            float cy = (_points[0].Position.y + _points[1].Position.y + _points[2].Position.y) / 3f;
            float cz = (_points[0].Position.z + _points[1].Position.z + _points[2].Position.z) / 3f;
            return new Point(new Vector3(cx, cy, cz));
        }

        private bool IsPointPartOfFace(Point point) =>
            _points.Any(fp => fp.ID == point.ID);

        private static Vector3 GetNormal(Point p1, Point p2, Point p3)
        {
            Vector3 side1 = p2.Position - p1.Position;
            Vector3 side2 = p3.Position - p1.Position;
            Vector3 cross = Vector3.Cross(side1, side2);
            return cross / cross.magnitude;
        }

        private static bool IsNormalPointingAwayFromOrigin(Vector3 surface, Vector3 normal) =>
            Vector3.Distance(Vector3.zero, surface) < Vector3.Distance(Vector3.zero, surface + normal);
    }
}
