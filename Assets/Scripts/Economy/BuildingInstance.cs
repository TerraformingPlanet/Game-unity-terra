using System;

/// <summary>
/// Instance runtime d'un bâtiment placé sur un hex par une entité politique.
/// Transportée par JSON depuis le serveur autoritaire.
/// </summary>
[Serializable]
public class BuildingInstance
{
    /// <summary>Référence à la définition du bâtiment (via buildingType pour JSON).</summary>
    public BuildingType buildingType;
    /// <summary>Identifiant axial de l'hex (format "q,r").</summary>
    public string       hexId;
    /// <summary>Identifiant de l'entité politique propriétaire.</summary>
    public string       ownerId;
    /// <summary>Ticks de construction restants. 0 = construction terminée.</summary>
    public int          constructionTicksRemaining;
    public bool         isOperational;
}
