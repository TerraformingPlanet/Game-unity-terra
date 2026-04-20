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
        Color tint)
    {
        DisplayName = displayName;
        FontAwesomeUnicode = fontAwesomeUnicode;
        FallbackGlyph = fallbackGlyph;
        Tint = tint;
    }

    public string DisplayName { get; }
    public string FontAwesomeUnicode { get; }
    public string FallbackGlyph { get; }
    public Color Tint { get; }

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
                    new Color(0.83f, 0.64f, 0.34f))
            },
            {
                CorpBuildingType.Farm,
                new GameHUDBuildingIconDefinition(
                    "Ferme",
                    "e2cd",
                    "F",
                    new Color(0.42f, 0.78f, 0.40f))
            },
            {
                CorpBuildingType.EnergyPlant,
                new GameHUDBuildingIconDefinition(
                    "Centrale énergétique",
                    "f0e7",
                    "E",
                    new Color(0.95f, 0.81f, 0.26f))
            },
            {
                CorpBuildingType.Research,
                new GameHUDBuildingIconDefinition(
                    "Recherche",
                    "e4f3",
                    "R",
                    new Color(0.45f, 0.74f, 0.95f))
            },
            {
                CorpBuildingType.Road,
                new GameHUDBuildingIconDefinition(
                    "Route",
                    "f018",
                    "Rt",
                    new Color(0.70f, 0.70f, 0.70f))
            },
            {
                CorpBuildingType.SeaPort,
                new GameHUDBuildingIconDefinition(
                    "Port maritime",
                    "f21a",
                    "Po",
                    new Color(0.30f, 0.60f, 0.90f))
            },
            {
                CorpBuildingType.Spaceport,
                new GameHUDBuildingIconDefinition(
                    "Spatioport",
                    "f135",
                    "Sp",
                    new Color(0.78f, 0.56f, 0.95f))
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