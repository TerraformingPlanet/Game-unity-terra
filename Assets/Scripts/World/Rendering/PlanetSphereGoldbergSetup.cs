using UnityEngine;

// Lifecycle (Awake, OnDestroy), cloud layer setup, and sphere cache management —
// extracted from PlanetSphereGoldberg.cs to keep it under 500 lines.
public partial class PlanetSphereGoldberg : MonoBehaviour
{
    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _meshFilter   = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        // Ajoute MeshCollider si absent (nécessaire pour OnMouseDown)
        _meshCollider = GetComponent<MeshCollider>();
        if (_meshCollider == null)
        {
            Collider existing = GetComponent<Collider>();
            if (existing != null) Destroy(existing);
            _meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        _meshCollider.convex = false;

        // CameraController auto-détection
        if (cameraController == null)
            cameraController = FindAnyObjectByType<CameraController>();

        // Matériau vertex color
        if (sphereMaterial != null)
        {
            _meshRenderer.sharedMaterial = sphereMaterial;
        }
        else
        {
            Shader s = Shader.Find("Terraformation/HexVertexColor");
            if (s != null)
                _meshRenderer.material = new Material(s);
            else
                Debug.LogWarning("[PlanetSphereGoldberg] Shader 'Terraformation/HexVertexColor' introuvable. "
                               + "Assigner un matériau en Inspector.");
        }

        if (cloudLayer == null)
            cloudLayer = EnsureCloudLayer();

        _borderRenderer = gameObject.AddComponent<OwnershipBorderRenderer>();
    }

    private void OnDestroy()
    {
        if (_tilePrisms  != null) Destroy(_tilePrisms);
        if (_lakeCaps    != null) Destroy(_lakeCaps);
        if (_waterPrisms != null) Destroy(_waterPrisms);
    }

    private PlanetCloudLayer EnsureCloudLayer()
    {
        Transform existing = transform.Find("CloudLayer");
        if (existing != null)
        {
            PlanetCloudLayer existingLayer = existing.GetComponent<PlanetCloudLayer>();
            if (existingLayer != null)
                return existingLayer;
        }

        GameObject cloudObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cloudObject.name = "CloudLayer";
        cloudObject.transform.SetParent(transform, false);
        cloudObject.transform.localPosition = Vector3.zero;
        cloudObject.transform.localRotation = Quaternion.identity;
        cloudObject.transform.localScale = Vector3.one * 1.045f;

        Collider collider = cloudObject.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        return cloudObject.AddComponent<PlanetCloudLayer>();
    }

    // =========================================================
    // Cleanup
    // =========================================================

    private static void ClearSphereCache()
    {
        foreach (CachedSphere cached in SphereCache.Values)
        {
            if (cached?.SphereData.mesh != null)
                Destroy(cached.SphereData.mesh);
        }
        SphereCache.Clear();
        Debug.Log("[PlanetSphereGoldberg] Cache sphères vidé.");
    }
}
