using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public readonly struct GameHUDBuildingIconDefinition
{
    public GameHUDBuildingIconDefinition(
        string displayName,
        string fontAwesomeUnicode,
        string fallbackGlyph,
        Color tint,
        string tooltipText = "")
    {
        DisplayName = displayName;
        FontAwesomeUnicode = fontAwesomeUnicode;
        FallbackGlyph = fallbackGlyph;
        Tint = tint;
        TooltipText = tooltipText;
    }

    public string DisplayName { get; }
    public string FontAwesomeUnicode { get; }
    public string FallbackGlyph { get; }
    public Color Tint { get; }
    public string TooltipText { get; }

    public string GetFontAwesomeGlyph()
    {
        if (int.TryParse(FontAwesomeUnicode, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
            return char.ConvertFromUtf32(codePoint);

        return FallbackGlyph;
    }
}

public static class GameHUDBuildingIcons
{
    private static readonly Dictionary<CorpBuildingType, GameHUDBuildingIconDefinition> Definitions =
        new Dictionary<CorpBuildingType, GameHUDBuildingIconDefinition>
        {
            {
                CorpBuildingType.Mine,
                new GameHUDBuildingIconDefinition(
                    "Mine",
                    "f275",
                    "M",
                    new Color(0.83f, 0.64f, 0.34f),
                    "Extrait des Minerais (+2/tick) et génère des Déchets (+0.5/tick).\nCoût: 50 pts. Ouvriers: 50 Poor + 10 Middle.")
            },
            {
                CorpBuildingType.Farm,
                new GameHUDBuildingIconDefinition(
                    "Ferme",
                    "e2cd",
                    "F",
                    new Color(0.42f, 0.78f, 0.40f),
                    "Produit de la Nourriture (+3/tick).\nCoût: 40 pts. Ouvriers: 30 Poor + 5 Middle.")
            },
            {
                CorpBuildingType.EnergyPlant,
                new GameHUDBuildingIconDefinition(
                    "Centrale énergétique",
                    "f0e7",
                    "E",
                    new Color(0.95f, 0.81f, 0.26f),
                    "Génère de l'Énergie (+5/tick) et des Déchets (+1/tick).\nCoût: 90 pts. Ouvriers: 20 Middle + 5 Rich.")
            },
            {
                CorpBuildingType.Research,
                new GameHUDBuildingIconDefinition(
                    "Recherche",
                    "e4f3",
                    "R",
                    new Color(0.45f, 0.74f, 0.95f),
                    "Consomme de l'Énergie (\u22121/tick), produit des Points de Recherche (+1/tick).\nCoût: 90 pts. Ouvriers: 10 Middle + 15 Rich.")
            },
        };

    public static GameHUDBuildingIconDefinition Get(CorpBuildingType buildingType)
    {
        if (Definitions.TryGetValue(buildingType, out GameHUDBuildingIconDefinition definition))
            return definition;

        return new GameHUDBuildingIconDefinition(
            buildingType.ToString(),
            string.Empty,
            "?",
            Color.white);
    }

    public static GameHUDBuildingIconDefinition Get(int rawBuildingType)
    {
        if (Enum.IsDefined(typeof(CorpBuildingType), rawBuildingType))
            return Get((CorpBuildingType)rawBuildingType);

        return new GameHUDBuildingIconDefinition(
            $"Type {rawBuildingType}",
            string.Empty,
            "?",
            Color.white);
    }

    public static string GetAllFontAwesomeGlyphs()
    {
        string glyphs = string.Empty;
        foreach (GameHUDBuildingIconDefinition definition in Definitions.Values)
            glyphs += definition.GetFontAwesomeGlyph();

        return glyphs;
    }
}