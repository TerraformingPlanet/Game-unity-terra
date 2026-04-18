using System;
using System.Collections.Generic;

// =============================================================================
// Ownership — appartenance politique d'un hex
// =============================================================================

/// <summary>Type de propriétaire d'un hex.</summary>
public enum OwnerType { Neutral, Corporation, Nation }

/// <summary>
/// Appartenance politique d'un hex. Sérialisable pour transport JSON serveur-client.
/// </summary>
[Serializable]
public struct HexOwnership
{
    public OwnerType ownerType;
    /// <summary>Identifiant de l'entité propriétaire. Vide si Neutral.</summary>
    public string ownerId;
}

// =============================================================================
// Entités politiques — runtime [Serializable], transportées par JSON depuis le serveur
// =============================================================================

public enum CorporationStrategy { Expansionist, Economist, Militarist }
public enum GovernmentType       { Democracy, Autocracy, Technocracy, Military }

/// <summary>
/// État d'une relation diplomatique entre deux entités politiques.
/// </summary>
[Serializable]
public class DiplomacyRelation
{
    /// <summary>Identifiant de l'entité cible.</summary>
    public string targetEntityId;
    /// <summary>Valeur de relation [−100 = guerre totale, +100 = alliance forte].</summary>
    public float  relationScore;
    public bool   hasTradeAgreement;
    public bool   isAtWar;
}

/// <summary>
/// Classe de base abstraite pour toutes les entités politiques du jeu.
/// Transportée par JSON depuis le serveur autoritaire — ne pas dériver de ScriptableObject.
/// </summary>
[Serializable]
public abstract class PoliticalEntity
{
    public string entityId;
    public string entityName;
    /// <summary>Corps célestes (OrbitalBody.bodyName) sous contrôle de cette entité.</summary>
    public List<string> controlledBodyIds = new();
    public List<DiplomacyRelation> diplomacy = new();
}

/// <summary>
/// Corporation joueur : entreprise privée aux motivations économiques/expansionnistes.
/// </summary>
[Serializable]
public class Corporation : PoliticalEntity
{
    public CorporationStrategy strategy = CorporationStrategy.Economist;
    /// <summary>Crédits disponibles.</summary>
    public float credits;
    /// <summary>Points de Recherche & Développement accumulés.</summary>
    public float rdPoints;
}

/// <summary>
/// État-Nation : entité gouvernementale avec population, armée et politique étrangère.
/// </summary>
[Serializable]
public class NationState : PoliticalEntity
{
    public GovernmentType governmentType = GovernmentType.Democracy;
    public long   population;
    /// <summary>Puissance militaire normalisée [0–1000].</summary>
    public float  militaryStrength;
    /// <summary>Stabilité interne [0–1]. < 0.25 → risque de coup d'état.</summary>
    public float  stability = 1f;
}
