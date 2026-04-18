using System;
using System.Collections.Generic;

// =============================================================================
// Projets — pipeline d'actions longues-durée pour entités politiques
// =============================================================================

/// <summary>État d'avancement d'un projet.</summary>
public enum ProjectStatus { Pending, Active, Completed, Failed, Cancelled }

/// <summary>Catégorie de projet.</summary>
public enum ProjectType { Terraformation, Infrastructure, Exploration, Diplomatic, Research }

/// <summary>Sous-type pour les projets diplomatiques.</summary>
public enum DiplomacyType { Alliance, Trade, War, Annexation }

/// <summary>
/// Classe de base abstraite pour tous les projets du jeu.
/// Transportée par JSON depuis le serveur autoritaire.
/// </summary>
[Serializable]
public abstract class Project
{
    public string            projectId;
    public string            projectName;
    public ProjectType       projectType;
    public ProjectStatus     status        = ProjectStatus.Pending;
    /// <summary>Identifiant de l'entité politique propriétaire.</summary>
    public string            ownerId;
    public List<ResourceStack> requiredResources = new();
    public int               ticksElapsed;
    public int               ticksDuration;

    /// <summary>
    /// Évalue si les conditions de complétion sont remplies.
    /// Appelé par le serveur à chaque tick pendant que status == Active.
    /// </summary>
    public abstract bool EvaluateCompletion();
}

/// <summary>
/// Projet de terraformation d'un hex spécifique.
/// </summary>
[Serializable]
public class TerraformationProject : Project
{
    /// <summary>Identifiant axial de l'hex cible (format "q,r").</summary>
    public string          targetHexId;
    public TerraformAction terraformAction;

    public override bool EvaluateCompletion()
        => ticksElapsed >= ticksDuration;
}

/// <summary>
/// Projet de construction d'un bâtiment sur un hex.
/// </summary>
[Serializable]
public class InfrastructureProject : Project
{
    public string       targetHexId;
    public BuildingType buildingType;

    public override bool EvaluateCompletion()
        => ticksElapsed >= ticksDuration;
}

/// <summary>
/// Projet d'exploration d'un corps céleste dans un système solaire.
/// </summary>
[Serializable]
public class ExplorationProject : Project
{
    /// <summary>Nom du corps céleste cible (OrbitalBody.bodyName).</summary>
    public string targetBodyId;
    /// <summary>Nom du système solaire cible (SolarSystemData.systemName).</summary>
    public string targetSystemId;

    public override bool EvaluateCompletion()
        => ticksElapsed >= ticksDuration;
}

/// <summary>
/// Projet diplomatique entre deux entités politiques.
/// </summary>
[Serializable]
public class DiplomaticProject : Project
{
    /// <summary>Identifiant de l'entité politique cible.</summary>
    public string        targetEntityId;
    public DiplomacyType diplomacyType;

    public override bool EvaluateCompletion()
        => ticksElapsed >= ticksDuration;
}

/// <summary>
/// Projet de recherche avançant dans l'arbre technologique.
/// </summary>
[Serializable]
public class ResearchProject : Project
{
    /// <summary>Identifiant du nœud technologique à débloquer.</summary>
    public string techNodeId;

    public override bool EvaluateCompletion()
        => ticksElapsed >= ticksDuration;
}
