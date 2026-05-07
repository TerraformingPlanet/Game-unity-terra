using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mesh du plan tangent local : projette les tuiles GP visibles sur un plan plat.
///
/// Maintient deux tableaux de sommets en parallèle :
///   _sphereVerts — positions sphériques originales (t = 0)
///   _flatVerts   — positions projetées sur le plan tangent (t = 1)
///
/// ApplyTransition(t) interpole linéairement entre les deux pour l'animation.
///
/// Prérequis : RequireComponent MeshFilter, MeshRenderer, MeshCollider.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class PlanetTangentMesh : MonoBehaviour
{
    // =========================================================
    // Inspector
    // =========================================================

    [Tooltip("Seuil de produit scalaire pour la visibilité d'une face (0.75 ≈ ±41°).")]
    [SerializeField] public float dotThreshold = 0.75f;

    [Tooltip("Facteur d'échelle de la projection (espace plan).")]
    [SerializeField] public float projectionScale = 15f;

    // =========================================================
    // Runtime
    // =========================================================

    private Mesh         _mesh;
    private MeshCollider _meshCollider;

    private Vector3[] _sphereVerts;  // positions sphère (t=0)
    private Vector3[] _flatVerts;    // positions plan   (t=1)
    private Color[]   _baseColors;
    private int[]     _triToFaceId;  // index : groupe de 3 triangles (triIdx), valeur : faceId GP

    // =========================================================
    // Propriétés
    // =========================================================

    public Bounds GetBounds() => _mesh != null ? _mesh.bounds : default;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _mesh = new Mesh { name = "TangentPlaneMesh" };
        GetComponent<MeshFilter>().mesh = _mesh;
        _meshCollider = GetComponent<MeshCollider>();
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Construit le mesh à partir des données GP filtrées par la projection.
    /// Le mesh est initialisé en position sphère (t = 0).
    /// Le collider n'est PAS mis à jour ici (attendez t = 1 via ApplyTransition).
    /// </summary>
    public void BuildMesh(
        GoldbergSphereGenerator.GoldbergMeshData sphereData,
        LocalProjection proj)
    {
        if (sphereData.mesh == null || sphereData.faces == null || sphereData.vertexFaceId == null)
        {
            Debug.LogWarning("[PlanetTangentMesh] Données sphère incomplètes.");
            return;
        }

        _mesh.Clear();

        if (!ProcessVisibleTriangles(sphereData, proj,
                out var sphereVerts, out var flatVerts, out var colors, out var tris, out var triToFace))
        {
            Debug.LogWarning("[PlanetTangentMesh] Aucun triangle visible.");
            return;
        }

        _sphereVerts = sphereVerts;
        _flatVerts   = flatVerts;
        _baseColors  = colors;
        _triToFaceId = triToFace;

        _mesh.SetVertices(_sphereVerts);
        _mesh.SetTriangles(tris, 0);
        _mesh.SetColors(_baseColors);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    private bool ProcessVisibleTriangles(
        GoldbergSphereGenerator.GoldbergMeshData sphereData,
        LocalProjection proj,
        out Vector3[] sphereVerts,
        out Vector3[] flatVerts,
        out Color[] colors,
        out int[] tris,
        out int[] triToFace)
    {
        Vector3[] srcVerts = sphereData.mesh.vertices;
        int[]     srcTris  = sphereData.mesh.triangles;

        var sphereVertList = new List<Vector3>(srcTris.Length);
        var flatVertList   = new List<Vector3>(srcTris.Length);
        var colorList      = new List<Color>(srcTris.Length);
        var triList        = new List<int>(srcTris.Length);
        var triToFaceList  = new List<int>(srcTris.Length / 3);
        int outIdx = 0;

        for (int t = 0; t < srcTris.Length; t += 3)
        {
            int v0     = srcTris[t];
            int faceId = sphereData.vertexFaceId[v0];
            if (!proj.IsVisible(sphereData.faces[faceId].centroid3D, dotThreshold)) continue;

            Color faceColor = sphereData.faces[faceId].color;
            for (int k = 0; k < 3; k++)
            {
                int vi = srcTris[t + k];
                sphereVertList.Add(srcVerts[vi]);
                flatVertList.Add(proj.Project(srcVerts[vi], projectionScale));
                colorList.Add(faceColor);
                triList.Add(outIdx++);
            }
            triToFaceList.Add(faceId);
        }

        sphereVerts = sphereVertList.ToArray();
        flatVerts   = flatVertList.ToArray();
        colors      = colorList.ToArray();
        tris        = triList.ToArray();
        triToFace   = triToFaceList.ToArray();
        return sphereVertList.Count > 0;
    }

    /// <summary>
    /// Interpole les sommets entre position sphère (t=0) et plan (t=1).
    /// Quand t ≥ 1, met à jour le collider.
    /// </summary>
    public void ApplyTransition(float t)
    {
        if (_sphereVerts == null || _flatVerts == null) return;

        int count   = _sphereVerts.Length;
        var lerped  = new Vector3[count];

        for (int i = 0; i < count; i++)
            lerped[i] = Vector3.Lerp(_sphereVerts[i], _flatVerts[i], t);

        _mesh.vertices = lerped;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        if (t >= 1f)
            _meshCollider.sharedMesh = _mesh;
    }

    /// <summary>
    /// Rafraîchit les couleurs de vertex à partir des données de faces GP mises à jour.
    /// </summary>
    public void RefreshColors(GoldbergSphereGenerator.GoldbergFace[] faces)
    {
        if (_triToFaceId == null || _baseColors == null) return;

        for (int triIdx = 0; triIdx < _triToFaceId.Length; triIdx++)
        {
            Color c      = faces[_triToFaceId[triIdx]].color;
            int   vStart = triIdx * 3;
            _baseColors[vStart]     = c;
            _baseColors[vStart + 1] = c;
            _baseColors[vStart + 2] = c;
        }

        _mesh.SetColors(_baseColors);
    }

    /// <summary>
    /// Retourne le faceId GP correspondant à un triangle Unity (triangleIndex = hit.triangleIndex).
    /// Retourne -1 si le triangle est hors plage.
    /// </summary>
    public int GetFaceIdFromTriangle(int triangleIndex)
    {
        if (_triToFaceId == null) return -1;
        int triIdx = triangleIndex / 3;
        if (triIdx < 0 || triIdx >= _triToFaceId.Length) return -1;
        return _triToFaceId[triIdx];
    }
}
