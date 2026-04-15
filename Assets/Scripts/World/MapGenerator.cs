using UnityEngine;

/// <summary>
/// Orchestre le pipeline de génération hexagonale via une liste ordonnée de IHexSystem.
///
/// Ordre du pipeline :
///   HeightSystem → TemperatureSystem → WaterSystem → HydrologySystem → WindSystem
///   → SoilSystem → CoherenceValidationSystem → WaterClassificationSystem → BiomeSystem
///   → RiverSystem → ValidationSystem
///
/// Pour ajouter ou retirer un système, modifier la liste dans BuildPipeline().
/// Chaque système est indépendant : il ne lit que les champs écrits par les passes précédentes.
///
/// Rétro-compatibilité : l'ancienne surcharge Populate(cells, CelestialBodyData) est conservée.
/// </summary>
public static class MapGenerator
{
    // =========================================================
    // API principale
    // =========================================================

    /// <summary>
    /// Remplit le tableau de cellules depuis un MapRegion complet.
    /// </summary>
    public static void Populate(HexCell[] cells, MapRegion region)
    {
        if (region == null)
        {
            Debug.LogWarning("[MapGenerator] MapRegion manquant.");
            return;
        }
        if (region.planet == null)
        {
            Debug.LogWarning("[MapGenerator] MapRegion.planet manquant.");
            return;
        }

        if (!ValidateBody(region.planet)) return;

        GenerationContext ctx = GenerationContext.Build(cells, region);

        foreach (IHexSystem system in BuildPipeline())
            system.Execute(cells, ctx);
    }

    /// <summary>
    /// Surcharge de rétro-compatibilité : accepte un CelestialBodyData directement.
    /// Crée une région équatoriale par défaut sans système solaire.
    /// </summary>
    public static void Populate(HexCell[] cells, CelestialBodyData body)
    {
        if (!ValidateBody(body)) return;

        MapRegion tempRegion = UnityEngine.ScriptableObject.CreateInstance<MapRegion>();
        tempRegion.planet    = body;
        tempRegion.genParams = body.genParams;
        tempRegion.latitude  = 0.5f;
        tempRegion.longitude = 0.5f;

        Populate(cells, tempRegion);

        UnityEngine.Object.DestroyImmediate(tempRegion);
    }

    // =========================================================
    // Pipeline
    // =========================================================

    private static IHexSystem[] BuildPipeline()
    {
        return new IHexSystem[]
        {
            new HeightSystem(),
            new TemperatureSystem(),
            new WaterSystem(),
            new HydrologySystem(),
            new WindSystem(),
            new SoilSystem(),
            new CoherenceValidationSystem(),
            new WaterClassificationSystem(),
            new BiomeSystem(),
            new RiverSystem(),
            new ValidationSystem()
        };
    }

    // =========================================================
    // Validation
    // =========================================================

    private static bool ValidateBody(CelestialBodyData body)
    {
        if (body == null)
        { Debug.LogWarning("[MapGenerator] CelestialBodyData manquant."); return false; }
        if (body.genParams == null)
        { Debug.LogWarning($"[MapGenerator] {body.bodyName} : genParams manquant."); return false; }
        if (body.layers == null || body.layers.Length == 0)
        { Debug.LogWarning($"[MapGenerator] {body.bodyName} : aucune LayerZone définie."); return false; }
        return true;
    }
}
