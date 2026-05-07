using UnityEngine;

// Water caps, prisms, lake caps — extracted from PlanetSphereGoldberg.cs to keep it under 500 lines.
public partial class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // Water caps
    // =========================================================

    /// <summary>
    /// Crée les prismes de profondeur (murs latéraux land + océan). La WaterSphere
    /// a été supprimée : les faces océaniques du mesh H3 sont baked à seaLevel.
    /// </summary>
    internal void CreateWaterCaps(
        GoldbergSphereGenerator.GoldbergFace[] faces,
        float[] faceAltitudes,
        float seaLevel,
        bool[] isFaceOcean = null,
        bool[] isFaceInlandWater = null)
    {
        if (_tilePrisms  != null) Destroy(_tilePrisms);
        if (_lakeCaps    != null) Destroy(_lakeCaps);
        if (_waterPrisms != null) Destroy(_waterPrisms);
        if (faces == null || faceAltitudes == null) return;

        // ── Depth prisms (land + ocean tiles) ────────────────────────────────────
        // Land tiles: walls from tile altitude down to floor.
        // Ocean tiles: walls from seaLevel (top face in mesh) down to floor, WaterDepthColor.
        if (enableTopographicRelief)
        {
            Mesh depthMesh = TilePrismsBuilder.BuildDepthPrisms(
                faces, faceAltitudes, topographicDisplacementScale, seaLevel, isFaceOcean);
            if (depthMesh != null)
            {
                _tilePrisms = new GameObject("TileDepthPrisms");
                _tilePrisms.transform.SetParent(transform, false);
                _tilePrisms.transform.localPosition = Vector3.zero;
                _tilePrisms.transform.localScale    = Vector3.one;
                _tilePrisms.AddComponent<MeshFilter>().sharedMesh = depthMesh;
                var mr = _tilePrisms.AddComponent<MeshRenderer>();
                Material prismMat = sphereMaterial;
                if (prismMat == null)
                {
                    Shader s = Shader.Find("Terraformation/HexVertexColor");
                    prismMat = s != null ? new Material(s) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                }
                // Instancier pour ne pas modifier l'asset partagé avec les faces tiles.
                // Cull Off : les murs des prisms doivent être visibles des deux côtés
                // (falaises entre tiles de hauteurs différentes → face interne visible).
                // Murs prisme : Cull Off (double-face) + Offset 0,0.
                // Offset 0,0 est critique : avec Cull Off, les back-faces ont un slope négatif.
                // Si Offset=-1,-1 s'appliquait, le biais s'inverserait → depth loin caméra → eau visible.
                var prismMatInst = new Material(prismMat);
                prismMatInst.SetFloat("_Cull",         (float)UnityEngine.Rendering.CullMode.Off);
                prismMatInst.SetFloat("_OffsetFactor", 0f);
                prismMatInst.SetFloat("_OffsetUnits",  0f);
                mr.sharedMaterial = prismMatInst;
            }
        }

        // NOTE : Lake caps désactivées — génèrent des cônes au limbe de la planète.
        // TODO : remplacer par une autre approche (colorisation directe, ou caps uniquement pour InlandSea groupés).
    }

    /// <summary>Indique si les prismes de profondeur sont actuellement visibles.</summary>
    public bool IsWaterSphereVisible => _tilePrisms != null && _tilePrisms.activeSelf;

    /// <summary>Active ou désactive la visibilité des water caps, prismes et lacs.</summary>
    public void SetWaterSphereVisible(bool visible)
    {
        if (_tilePrisms  != null) _tilePrisms.SetActive(visible);
        if (_lakeCaps    != null) _lakeCaps.SetActive(visible);
        if (_waterPrisms != null) _waterPrisms.SetActive(visible);
    }

    /// <summary>Toggle la visibilité des water caps. Retourne le nouvel état.</summary>
    public bool ToggleWaterSphere()
    {
        bool next = !IsWaterSphereVisible;
        SetWaterSphereVisible(next);
        return next;
    }

}
