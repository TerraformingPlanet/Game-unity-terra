using UnityEngine;

/// <summary>
/// Halo atmosphérique pour la sphère de Goldberg en Vue Planétaire.
///
/// À attacher sur un GameObject enfant de la sphère GP, portant une sphère Unity
/// légèrement plus grande que la sphère GP (localScale ≈ 1.18).
/// Le shader Terraformation/PlanetAtmosphere doit être assigné au matériau.
///
/// La couleur de l'atmosphère est dérivée automatiquement depuis CelestialBodyData
/// (densité atmosphérique + composition).
///
/// Usage :
///   1. Créer un child "Atmosphere" sur le GameObject de PlanetSphereGoldberg.
///   2. Attacher une sphère primitive (MeshFilter sphere + MeshRenderer).
///   3. Attacher ce composant.
///   4. Assigner un matériau basé sur Terraformation/PlanetAtmosphere.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class GoldbergAtmosphere : MonoBehaviour
{
    [Header("Matériau")]
    [Tooltip("Matériau utilisant Terraformation/PlanetAtmosphere. "
           + "Si null, le shader est cherché par nom.")]
    [SerializeField] private Material atmosphereMaterial;

    [Header("Taille relative")]
    [Tooltip("Scale de la sphère atmo relative à la sphère GP parent. "
           + "1.18 donne un halo visible sans être trop épais.")]
    [SerializeField] private float relativeScale = 1.18f;

    [Header("Override couleur")]
    [Tooltip("Si vrai, utilise atmosphereColorOverride au lieu de la couleur dérivée.")]
    [SerializeField] private bool overrideColor;
    [SerializeField] private Color atmosphereColorOverride = new Color(0.3f, 0.6f, 1f, 1f);

    private MeshRenderer _renderer;
    private Material     _runtimeMaterial;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();

        if (atmosphereMaterial != null)
        {
            _runtimeMaterial          = new Material(atmosphereMaterial);
            _renderer.sharedMaterial  = _runtimeMaterial;
        }
        else
        {
            Shader s = Shader.Find("Terraformation/PlanetAtmosphere");
            if (s != null)
            {
                _runtimeMaterial         = new Material(s);
                _renderer.sharedMaterial = _runtimeMaterial;
            }
            else
            {
                Debug.LogWarning("[GoldbergAtmosphere] Shader 'Terraformation/PlanetAtmosphere' introuvable.");
            }
        }

        // Ajuste l'échelle de cet objet relativement à son parent
        transform.localScale    = Vector3.one * relativeScale;
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }

    // =========================================================
    // API publique
    // =========================================================

    /// <summary>
    /// Adapte la couleur et l'intensité de l'atmosphère depuis les données du corps céleste.
    /// Appeler après PlanetSphereGoldberg.LoadPlanet().
    /// </summary>
    public void ApplyBodyData(OrbitalBody body)
    {
        if (_runtimeMaterial == null || body == null) return;

        Color col = overrideColor ? atmosphereColorOverride : DeriveAtmosphereColor(body);

        float density  = body.atmosphere.density;   // 0–1
        float intensity = Mathf.Lerp(0.3f, 1.6f, density);
        float rimPower  = Mathf.Lerp(3.5f, 1.8f, density); // dense = halo plus diffus

        _runtimeMaterial.SetColor("_AtmosphereColor", col);
        _runtimeMaterial.SetFloat("_RimIntensity",    intensity);
        _runtimeMaterial.SetFloat("_RimPower",        rimPower);

        // Atmo nulle → invisible
        gameObject.SetActive(density > 0.02f);
    }

    // =========================================================
    // Dérivation couleur depuis CelestialBodyData
    // =========================================================

    private static Color DeriveAtmosphereColor(OrbitalBody body)
    {
        // Composition de base : O₂+N₂ → bleu, CO₂ → orange-rouge, toxique → vert-jaune
        float o2    = Mathf.Clamp01(body.atmosphere.o2Ratio);
        float co2   = Mathf.Clamp01(body.atmosphere.co2Ratio);
        float toxic = Mathf.Clamp01(body.atmosphere.toxinRatio);

        // Bleu azur pour atmo respirable
        Color breathable    = new Color(0.25f, 0.55f, 1.0f, 0.85f);
        // Orange pour CO₂ (Mars/Venus)
        Color carbonDioxide = new Color(0.9f, 0.45f, 0.1f, 0.85f);
        // Vert-jaune pour toxique
        Color toxicAtmo     = new Color(0.55f, 0.9f, 0.25f, 0.85f);

        Color result = breathable;
        result = Color.Lerp(result, carbonDioxide, Mathf.Clamp01(co2 * 3f));
        result = Color.Lerp(result, toxicAtmo, toxic * 0.7f);

        // Planète avec eau → légèrement plus bleue
        float waterHint = Mathf.Clamp01(body.geology.waterAbundance);
        result = Color.Lerp(result, new Color(0.1f, 0.4f, 0.95f, 0.9f), waterHint * 0.35f);

        return result;
    }
}
