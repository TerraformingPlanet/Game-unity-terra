using System;

// =============================================================================
// Ressources — inventaires, coûts, production
// =============================================================================

/// <summary>Types de ressources fondamentales du jeu.</summary>
public enum ResourceType
{
    Iron,
    Oxygen,
    Water,
    Energy,
    Tech,
    Food,
    RareMetal,
    Fuel,
    Credits
}

/// <summary>
/// Quantité d'une ressource donnée. Utilisée pour les inventaires des entités politiques,
/// les coûts de projets et les productions/consommations de bâtiments.
/// </summary>
[Serializable]
public struct ResourceStack
{
    public ResourceType type;
    public float        quantity;

    public ResourceStack(ResourceType type, float quantity)
    {
        this.type     = type;
        this.quantity = quantity;
    }
}
