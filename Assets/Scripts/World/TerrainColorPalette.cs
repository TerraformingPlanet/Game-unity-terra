using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Palette de couleurs de terrain définissable par le joueur.
/// Chaque entrée correspond à la valeur int d'un <see cref="TerrainType"/>.
/// Le serveur envoie uniquement le numéro — Unity décide de la couleur.
/// </summary>
[CreateAssetMenu(menuName = "Terraformation/TerrainColorPalette", fileName = "TerrainColorPalette")]
public class TerrainColorPalette : ScriptableObject
{
    [Tooltip("Couleur par type de terrain. L'index correspond à la valeur int de TerrainType.")]
    public Color[] colors = new Color[]
    {
        new Color(0.45f, 0.38f, 0.30f), // 0  Roche
        new Color(0.88f, 0.93f, 0.98f), // 1  Glace
        new Color(0.55f, 0.50f, 0.15f), // 2  AtmosphereToxique
        new Color(0.10f, 0.35f, 0.65f), // 3  Eau
        new Color(0.25f, 0.52f, 0.20f), // 4  Vegetation
        new Color(0.60f, 0.60f, 0.65f), // 5  Metal
    };

    /// <summary>Retourne la couleur pour un type de terrain.</summary>
    public Color GetColor(TerrainType type)
    {
        int idx = (int)type;
        if (idx >= 0 && idx < colors.Length) return colors[idx];
        return Color.magenta; // type inconnu → magenta visible en debug
    }

    /// <summary>Construit un Dictionary utilisable par GoldbergFaceColorizer.</summary>
    public Dictionary<TerrainType, Color> ToDictionary()
    {
        var dict = new Dictionary<TerrainType, Color>(colors.Length);
        for (int i = 0; i < colors.Length; i++)
            dict[(TerrainType)i] = colors[i];
        return dict;
    }

    /// <summary>Palette par défaut (fallback si aucun asset assigné en Inspector).</summary>
    public static Dictionary<TerrainType, Color> DefaultDictionary() => new Dictionary<TerrainType, Color>
    {
        { TerrainType.Roche,             new Color(0.45f, 0.38f, 0.30f) },
        { TerrainType.Glace,             new Color(0.88f, 0.93f, 0.98f) },
        { TerrainType.AtmosphereToxique, new Color(0.55f, 0.50f, 0.15f) },
        { TerrainType.Eau,               new Color(0.10f, 0.35f, 0.65f) },
        { TerrainType.Vegetation,        new Color(0.25f, 0.52f, 0.20f) },
        { TerrainType.Metal,             new Color(0.60f, 0.60f, 0.65f) },
    };
}
