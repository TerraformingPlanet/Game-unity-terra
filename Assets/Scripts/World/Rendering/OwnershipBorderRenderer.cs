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
    [SerializeField] private float radialOffset = 0.006f;

    [Tooltip("Nombre de sous-segments par arête pour suivre la courbure de la sphère (évite l'effet de corde).")]
    [SerializeField] private int subdivisionSteps = 5;

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
        // ZTest Always : les frontières s'affichent même si la surface de la sphère
        // dépasse r=10.06 avec le relief topographique (altitude * displacementScale > 0.006).
        // Sans ça, les lineRenderers à r=10.06 sont sous la géométrie des tuiles en altitude
        // et échouent le test de profondeur → invisibles.
        _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
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

            // Si le premier et dernier points sont quasi-identiques (boucle fermée explicitement),
            // retirer le point redondant — lr.loop = true se charge de la fermeture.
            if (pts.Length > 2 && (pts[0] - pts[pts.Length - 1]).sqrMagnitude < 1e-5f)
                System.Array.Resize(ref pts, pts.Length - 1);

            LineRenderer lr = GetOrCreate(i);
            lr.enabled           = true;
            lr.loop              = true;
            lr.useWorldSpace     = false;
            lr.widthMultiplier   = lineWidth;
            lr.startColor        = col;
            lr.endColor          = col;
            lr.material          = _mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;

            // Rayon cible = rayon visuel de la sphère × (1 + décalage radial).
            // On utilise GoldbergSphereGenerator.VisualRadius (constante) plutôt que pts[0].magnitude
            // car les vertices de boucle sont des vecteurs normalisés (direction unitaire) depuis
            // GoldbergFaceColorizerBoundary — la magnitude serait 1 au lieu de ~10.
            float targetRadius = GoldbergSphereGenerator.VisualRadius * (1f + radialOffset);

            // Subdiviser chaque arête pour projeter la ligne sur la sphère (évite les cordes).
            // Vector3.Slerp entre deux directions unitaires interpole sur le grand cercle.
            int totalPts = pts.Length * subdivisionSteps;
            lr.positionCount = totalPts;

            for (int j = 0; j < pts.Length; j++)
            {
                Vector3 dirA = pts[j].normalized;
                Vector3 dirB = pts[(j + 1) % pts.Length].normalized;
                for (int s = 0; s < subdivisionSteps; s++)
                {
                    float t = s / (float)subdivisionSteps;
                    lr.SetPosition(j * subdivisionSteps + s,
                                   Vector3.Slerp(dirA, dirB, t) * targetRadius);
                }
            }
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
