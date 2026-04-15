/// <summary>
/// Enumère les actions de terraformation disponibles sur un hex.
/// Chaque action est paramétrée par un TerraformActionData (ScriptableObject).
/// </summary>
public enum TerraformAction
{
    /// <summary>Chauffe le hex (ex. brûleur atmosphérique). Augmente la température locale.</summary>
    Heat = 0,

    /// <summary>Irrigue le hex. Augmente waterRatio, réduit rockHardness.</summary>
    Irrigate = 1,

    /// <summary>Plante de la végétation. Requiert eau + température minimale. Augmente organicContent.</summary>
    Plant = 2,

    /// <summary>Extrait des minéraux. Augmente mineralDensity restante, réduit rockHardness.</summary>
    Mine = 3,

    /// <summary>Neutralise les toxines. Réduit toxinLevel et toxicSoil.</summary>
    Detoxify = 4
}
