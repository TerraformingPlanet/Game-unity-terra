using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dessine les frontières de territoire des corporations comme des LineRenderers.
/// Attaché au même GameObject que PlanetSphereGoldberg.
/// Chaque boucle (List entry) correspond à un segment continu de frontière.
/// </summary>
[DisallowMultipleComponent]
public class OwnershipBorderRenderer : MonoBehaviour
{
    [Tooltip("Épaisseur des lignes de frontière (unités monde).")]
    [SerializeField] private float lineWidth = 0.06f;

    [Tooltip("Décalage radial pour éviter le Z-fighting avec la sphère (fraction du rayon).")]
    [SerializeField] private float radialOffset = 0.004f;

    private readonly List<LineRenderer> _pool = new();
    private Material _mat;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        // Sprites/Default est non-éclairé et supporte les vertex colors de LineRenderer
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Color");

        _mat = sh != null
            ? new Material(sh)
            : new Material(Shader.Find("Hidden/Internal-Colored"));

        _mat.renderQueue = 3001; // au-dessus de la sphère opaque
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Met à jour les LineRenderers avec les boucles de frontière.
    /// Chaque entrée est un polygone fermé (vertices en espace local) avec sa couleur de corp.
    /// </summary>
    public void UpdateBorders(List<(Vector3[] pts, Color col)> loops)
    {
        for (int i = 0; i < loops.Count; i++)
        {
            (Vector3[] pts, Color col) = loops[i];
            if (pts == null || pts.Length < 2) continue;

            LineRenderer lr = GetOrCreate(i);
            lr.enabled           = true;
            lr.loop              = true;
            lr.useWorldSpace     = false;
            lr.positionCount     = pts.Length;
            lr.widthMultiplier   = lineWidth;
            lr.startColor        = col;
            lr.endColor          = col;
            lr.material          = _mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;

            float scale = 1f + radialOffset;
            for (int j = 0; j < pts.Length; j++)
                lr.SetPosition(j, pts[j] * scale);
        }

        // Désactiver les LineRenderers en excès
        for (int i = loops.Count; i < _pool.Count; i++)
            _pool[i].enabled = false;
    }

    /// <summary>Efface toutes les frontières visibles.</summary>
    public void ClearBorders()
    {
        foreach (LineRenderer lr in _pool)
            lr.enabled = false;
    }

    // =========================================================
    // Pool
    // =========================================================

    private LineRenderer GetOrCreate(int index)
    {
        while (_pool.Count <= index)
        {
            GameObject go = new GameObject($"OwnershipBorder_{_pool.Count}");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.enabled = false;
            _pool.Add(lr);
        }
        return _pool[index];
    }
}
