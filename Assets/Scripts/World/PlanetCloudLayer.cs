using UnityEngine;

/// <summary>
/// Couche de nuages orbitale non volumétrique pour la vue planète.
///
/// Le rendu repose sur une sphère légèrement plus grande que la surface, avec un shader
/// procédural animé. L'objectif est un rendu orbital crédible et léger, distinct du halo
/// atmosphérique et compatible avec le pipeline Goldberg existant.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class PlanetCloudLayer : MonoBehaviour
{
    [Header("Matériau")]
    [Tooltip("Matériau utilisant Terraformation/PlanetClouds. Si null, le shader est cherché par nom.")]
    [SerializeField] private Material cloudMaterial;

    [Header("Taille")]
    [Tooltip("Échelle relative de la couche primaire par rapport à la planète.")]
    [SerializeField] private float relativeScale = 1.045f;

    [Tooltip("Échelle relative additionnelle de la seconde couche.")]
    [SerializeField] private float secondaryScaleOffset = 0.012f;

    [Header("Heuristiques")]
    [Tooltip("Forcer l'affichage des nuages même si la planète n'est pas jugée favorable.")]
    [SerializeField] private bool forceVisible;

    [Tooltip("Seuil minimal d'atmosphère pour afficher des nuages.")]
    [SerializeField] private float minAtmosphereDensity = 0.12f;

    [Tooltip("Seuil minimal d'eau pour afficher des nuages.")]
    [SerializeField] private float minWaterAbundance = 0.08f;

    [Header("Réglages visuels")]
    [SerializeField] private float coverage = 0.58f;
    [SerializeField] private float opacity = 0.82f;
    [SerializeField] private float softness = 0.16f;
    [SerializeField] private float fresnelStrength = 0.35f;
    [SerializeField] private float rotationSpeed = 0.015f;
    [SerializeField] private float secondaryRotationSpeed = -0.024f;
    [SerializeField] private float secondaryLayerBlend = 0.42f;

    private MeshRenderer _primaryRenderer;
    private Material _primaryRuntimeMaterial;
    private Transform _secondaryTransform;
    private MeshRenderer _secondaryRenderer;
    private Material _secondaryRuntimeMaterial;
    private float _primaryRotation;
    private float _secondaryRotation;

    private void Awake()
    {
        _primaryRenderer = GetComponent<MeshRenderer>();
        _primaryRuntimeMaterial = CreateRuntimeMaterial();
        if (_primaryRuntimeMaterial != null)
            _primaryRenderer.sharedMaterial = _primaryRuntimeMaterial;

        transform.localScale = Vector3.one * relativeScale;
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        EnsureSecondaryLayer();
    }

    private void Update()
    {
        _primaryRotation += rotationSpeed * Time.deltaTime * 360f;
        transform.localRotation = Quaternion.Euler(0f, _primaryRotation, 0f);

        if (_secondaryTransform != null)
        {
            _secondaryRotation += secondaryRotationSpeed * Time.deltaTime * 360f;
            _secondaryTransform.localRotation = Quaternion.Euler(0f, _secondaryRotation, 0f);
        }
    }

    private void OnDestroy()
    {
        if (_primaryRuntimeMaterial != null)
            Destroy(_primaryRuntimeMaterial);
        if (_secondaryRuntimeMaterial != null)
            Destroy(_secondaryRuntimeMaterial);
    }

    public void ApplyBodyData(OrbitalBody body)
    {
        if (body == null)
        {
            gameObject.SetActive(false);
            return;
        }

        bool visible = forceVisible || ShouldShowClouds(body);
        gameObject.SetActive(visible);
        if (_secondaryTransform != null)
            _secondaryTransform.gameObject.SetActive(visible);

        if (!visible)
            return;

        Color cloudColor = DeriveCloudColor(body);
        float bodyCoverage = Mathf.Clamp01(coverage * Mathf.Lerp(0.55f, 1.2f, body.geology.waterAbundance));
        float bodyOpacity = Mathf.Clamp01(opacity * Mathf.Lerp(0.4f, 1f, body.atmosphere.density));
        float bodySoftness = Mathf.Lerp(softness * 1.4f, softness * 0.7f, body.atmosphere.density);
        float bodyFresnel = Mathf.Lerp(fresnelStrength * 0.7f, fresnelStrength * 1.25f, body.atmosphere.density);

        ApplyMaterial(_primaryRuntimeMaterial, cloudColor, bodyCoverage, bodyOpacity, bodySoftness, bodyFresnel, 2.1f, 5.8f, 0.18f);
        ApplyMaterial(_secondaryRuntimeMaterial, cloudColor, Mathf.Clamp01(bodyCoverage + 0.08f), bodyOpacity * secondaryLayerBlend, bodySoftness * 1.2f, bodyFresnel * 0.9f, 3.8f, 9.7f, 0.11f);

        transform.localScale = Vector3.one * relativeScale;
        if (_secondaryTransform != null)
            _secondaryTransform.localScale = Vector3.one * (relativeScale + secondaryScaleOffset);
    }

    private void EnsureSecondaryLayer()
    {
        Transform existing = transform.Find("CloudLayerSecondary");
        if (existing == null)
        {
            GameObject secondary = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            secondary.name = "CloudLayerSecondary";
            secondary.transform.SetParent(transform, false);
            Collider collider = secondary.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            existing = secondary.transform;
        }

        _secondaryTransform = existing;
        _secondaryTransform.localPosition = Vector3.zero;
        _secondaryTransform.localRotation = Quaternion.identity;
        _secondaryTransform.localScale = Vector3.one * (relativeScale + secondaryScaleOffset);

        _secondaryRenderer = _secondaryTransform.GetComponent<MeshRenderer>();
        if (_secondaryRenderer == null)
            _secondaryRenderer = _secondaryTransform.gameObject.AddComponent<MeshRenderer>();

        if (_secondaryTransform.GetComponent<MeshFilter>() == null)
            _secondaryTransform.gameObject.AddComponent<MeshFilter>();

        _secondaryRuntimeMaterial = CreateRuntimeMaterial();
        if (_secondaryRuntimeMaterial != null)
            _secondaryRenderer.sharedMaterial = _secondaryRuntimeMaterial;
    }

    private Material CreateRuntimeMaterial()
    {
        if (cloudMaterial != null)
            return new Material(cloudMaterial);

        Shader shader = Shader.Find("Terraformation/PlanetClouds");
        if (shader == null)
        {
            Debug.LogWarning("[PlanetCloudLayer] Shader 'Terraformation/PlanetClouds' introuvable.");
            return null;
        }

        return new Material(shader);
    }

    private static void ApplyMaterial(
        Material material,
        Color cloudColor,
        float layerCoverage,
        float layerOpacity,
        float layerSoftness,
        float layerFresnel,
        float primaryTiling,
        float secondaryTiling,
        float detailStrength)
    {
        if (material == null)
            return;

        material.SetColor("_CloudColor", cloudColor);
        material.SetFloat("_Coverage", layerCoverage);
        material.SetFloat("_Opacity", layerOpacity);
        material.SetFloat("_Softness", Mathf.Max(0.001f, layerSoftness));
        material.SetFloat("_FresnelStrength", layerFresnel);
        material.SetFloat("_PrimaryTiling", primaryTiling);
        material.SetFloat("_SecondaryTiling", secondaryTiling);
        material.SetFloat("_DetailStrength", detailStrength);
    }

    private bool ShouldShowClouds(OrbitalBody body)
    {
        return body.atmosphere.density >= minAtmosphereDensity
            && body.geology.waterAbundance >= minWaterAbundance
            && body.atmosphere.toxinRatio < 0.85f;
    }

    private static Color DeriveCloudColor(OrbitalBody body)
    {
        Color baseColor = Color.white;
        float iceHint = Mathf.Clamp01(body.geology.waterAbundance * Mathf.Max(0f, 0.2f - body.physics.baseEquatorTemperature / 100f));
        float toxicTint = Mathf.Clamp01(body.atmosphere.toxinRatio * 0.25f);

        baseColor = Color.Lerp(baseColor, new Color(0.82f, 0.9f, 1f, 1f), iceHint);
        baseColor = Color.Lerp(baseColor, new Color(0.93f, 0.88f, 0.78f, 1f), toxicTint);
        return baseColor;
    }
}