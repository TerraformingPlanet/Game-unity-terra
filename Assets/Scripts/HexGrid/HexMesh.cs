using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a single procedural mesh for the entire hex grid.
/// Each hex is triangulated individually using 6 triangles (fan from center).
/// Vertex colors drive terrain colour — no textures needed.
/// Requires a MeshFilter + MeshRenderer with a vertex-color URP material.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class HexMesh : MonoBehaviour
{
    private Mesh _mesh;
    private MeshCollider _meshCollider;

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();
    private readonly List<Color> _colors = new();

    private void Awake()
    {
        Debug.Log("[HexMesh] Awake");
        _mesh = new Mesh { name = "Hex Grid Mesh" };
        GetComponent<MeshFilter>().mesh = _mesh;
        _meshCollider = GetComponent<MeshCollider>();
        Debug.Log("[HexMesh] Mesh and collider initialized.");
    }

    /// <summary>Build (or rebuild) the mesh from a list of cells.</summary>
    public void Triangulate(HexCell[] cells)
    {
        _mesh.Clear();
        _vertices.Clear();
        _triangles.Clear();
        _colors.Clear();

        foreach (HexCell cell in cells)
            TriangulateCell(cell);

        _mesh.vertices  = _vertices.ToArray();
        _mesh.triangles = _triangles.ToArray();
        _mesh.colors    = _colors.ToArray();
        _mesh.RecalculateNormals();

        _meshCollider.sharedMesh = _mesh;
    }

    // Nombre de vertices par hex : 6 triangles × 3 sommets = 18
    private const int VerticesPerHex = 18;

    /// <summary>
    /// Met à jour uniquement la couleur de vertex d'un hex sans retrianguler tout le mesh.
    /// La cellule doit avoir été triangulée lors du dernier Triangulate() avec son gridIndex correct.
    /// </summary>
    public void RefreshCell(HexCell cell)
    {
        Color[] colors = _mesh.colors;
        int start = cell.gridIndex * VerticesPerHex;

        if (start + VerticesPerHex > colors.Length)
        {
            Debug.LogWarning("[HexMesh] RefreshCell : gridIndex hors limites, Triangulate() nécessaire.");
            return;
        }

        Color c = cell.terrain != null ? cell.terrain.color : Color.white;
        for (int i = start; i < start + VerticesPerHex; i++)
            colors[i] = c;

        _mesh.colors = colors;
    }

    private void TriangulateCell(HexCell cell)
    {
        Color color = cell.terrain != null ? cell.terrain.color : Color.white;
        Vector3 center = cell.center;

        // 6 triangles fan from center — CW winding from +Y so camera above sees front faces
        for (int i = 0; i < 6; i++)
        {
            AddTriangle(
                center,
                center + HexMetrics.corners[i + 1],
                center + HexMetrics.corners[i]
            );
            AddTriangleColor(color);
        }
    }

    private void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int vertexIndex = _vertices.Count;
        _vertices.Add(v1);
        _vertices.Add(v2);
        _vertices.Add(v3);
        _triangles.Add(vertexIndex);
        _triangles.Add(vertexIndex + 1);
        _triangles.Add(vertexIndex + 2);
    }

    private void AddTriangleColor(Color color)
    {
        _colors.Add(color);
        _colors.Add(color);
        _colors.Add(color);
    }
}
