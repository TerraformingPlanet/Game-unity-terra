using UnityEngine;

public enum DebugCoherenceOverride
{
    None = 0,
    Ocean = 1,
    Arid = 2,
    Frozen = 3
}

/// <summary>
/// Preset de scénario de test pour lancer rapidement une combinaison planète/vue/région.
/// Les overrides de simulation avancés viendront plus tard; cette première version cadre le point d'entrée reproductible.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/Test Scenario Preset", fileName = "TestScenarioPreset")]
public class TestScenarioPreset : ScriptableObject
{
    public string displayName = "Nouveau preset";
    [TextArea] public string description;

    [Header("Cible")]
    public CelestialBodyData body;
    public bool openLocalView = true;

    [Header("Région")]
    [Range(0f, 1f)] public float latitude = 0.5f;
    [Range(0f, 1f)] public float longitude = 0.5f;

    [Header("Projection")]
    public bool clearProjectionCacheBeforeLaunch;

    [Header("Debug")]
    public DebugCoherenceOverride coherenceOverride;
}