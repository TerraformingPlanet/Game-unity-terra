using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dessine les frontières de territoire via GL.Lines dans OnRenderObject.
/// Bypass complet du pipeline URP/depth : les frontières s'affichent toujours
/// au-dessus de la surface quelle que soit l'altitude des tuiles (topographic relief).
/// Attaché au même GameObject que PlanetSphereGoldberg.
/// </summary>
[DisallowMultipleComponent]
public class OwnershipBorderRenderer : MonoBehaviour
{
    [Tooltip("Décalage radial pour placer les lignes au-dessus de la surface (fraction du rayon).")]
    [SerializeField] private float radialOffset = 0.006f;

    [Tooltip("Nombre de sous-segments par arête pour suivre la courbure de la sphère.")]
    [SerializeField] private int subdivisionSteps = 5;

    // Loops subdivisées prêtes à dessiner (espace local, magnitude ≈ VisualRadius*1.006).
    private readonly List<(Vector3[] pts, Color col)> _loops = new();

    private Material _mat;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        // Hidden/Internal-Colored respecte SetInt("_ZTest") même en URP.
        // ZTest=Always : les frontières passent toujours le depth test,
        // indépendamment de l'altitude des tuiles sous-jacentes.
        Shader sh = Shader.Find("Hidden/Internal-Colored");
        _mat = new Material(sh != null ? sh : Shader.Find("Unlit/Color"));
        _mat.SetInt("_ZTest",     (int)UnityEngine.Rendering.CompareFunction.Always);
        _mat.SetInt("_ZWrite",    0);
        _mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Reçoit les boucles (vecteurs unitaires, magnitude=1) et les subdivise
    /// sur la sphère. Les positions stockées sont en espace local du transform.
    /// </summary>
    public void UpdateBorders(List<(Vector3[] pts, Color col)> loops)
    {
        _loops.Clear();
        float targetRadius = GoldbergSphereGenerator.VisualRadius * (1f + radialOffset);

        foreach (var (pts, col) in loops)
        {
            if (pts == null || pts.Length < 2) continue;

            int nPts     = pts.Length;
            int totalPts = nPts * subdivisionSteps;
            var subdiv   = new Vector3[totalPts];

            for (int j = 0; j < nPts; j++)
            {
                Vector3 dirA = pts[j].normalized;
                Vector3 dirB = pts[(j + 1) % nPts].normalized;
                for (int s = 0; s < subdivisionSteps; s++)
                {
                    float t = s / (float)subdivisionSteps;
                    subdiv[j * subdivisionSteps + s] = Vector3.Slerp(dirA, dirB, t) * targetRadius;
                }
            }

            _loops.Add((subdiv, col));
        }
    }

    /// <summary>Efface toutes les frontières.</summary>
    public void ClearBorders() => _loops.Clear();

    // =========================================================
    // Rendu GL
    // =========================================================

    /// <summary>
    /// Dessin des frontières via GL.Lines. Appelé par Unity après la caméra courante.
    /// GL.MultMatrix(localToWorld) : les positions stockées sont en espace local.
    /// ZTest=Always sur le material garantit la visibilité malgré le relief.
    /// </summary>
    private void OnRenderObject()
    {
        if (_loops.Count == 0 || _mat == null) return;

        _mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(transform.localToWorldMatrix);
        GL.Begin(GL.LINES);

        foreach (var (pts, col) in _loops)
        {
            GL.Color(col);
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                GL.Vertex(pts[i]);
                GL.Vertex(pts[(i + 1) % n]);
            }
        }

        GL.End();
        GL.PopMatrix();
    }
}
