/// <summary>
/// Interface commune à tous les systèmes du pipeline de génération hexagonale.
/// Chaque système lit les champs remplis par les systèmes précédents et écrit
/// ses propres champs dans HexCell.state — jamais en sens inverse.
///
/// Ordre d'exécution garanti par MapGenerator :
///   HeightSystem → TemperatureSystem → WaterSystem → WindSystem
///   → SoilSystem → BiomeSystem → RiverSystem → ValidationSystem
/// </summary>
public interface IHexSystem
{
    /// <summary>
    /// Exécute ce système sur l'ensemble du tableau de cellules.
    /// Le contexte partagé fournit body, region, weather, genParams, rng et les
    /// offsets de bruit décorrélés.
    /// </summary>
    void Execute(HexCell[] cells, GenerationContext ctx);
}
