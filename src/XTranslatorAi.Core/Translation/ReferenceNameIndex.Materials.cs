using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Materials of item names the memory does not hold whole. Every vanilla item writes Ebony as 에보니 and
/// Dragonscale as 드래곤 비늘, but Elden Rim's "Pure Ebony" sword came back as 흑단 and 흑연마석, "Ebony Beam"
/// as 흑연마석 광선 and "Dragonscale War Dance" as 용비늘 무답. A material is used only when the memory spells it
/// that way in most item names starting with it. Words that also name a people or an ordinary thing (Daedric
/// Prince, Elven blood, Glass Cannon, Heavy Iron Cavalry) are replaced only before equipment.
/// </summary>
public sealed partial class ReferenceNameIndex
{
    private sealed record Material(string Word, string Target, bool InAnyName, string? NotAfter = null)
    {
        public Regex Pattern { get; } = new(@"(?<![A-Za-z'’\-])" + Regex.Escape(Word) + @"(?![A-Za-z'’\-])", RegexOptions.CultureInvariant);
    }

    private static readonly Material[] Materials =
    {
        new("Ebony", "에보니", InAnyName: true),
        new("Dragonbone", "드래곤 뼈", InAnyName: true),
        new("Dragonscale", "드래곤 비늘", InAnyName: true),
        new("Dragonplate", "드래곤 판금", InAnyName: true),
        new("Stalhrim", "스탈림", InAnyName: true),
        new("Chitin", "키틴", InAnyName: true),
        new("Bonemold", "뼈다귀", InAnyName: true),
        new("Moonstone", "월장석", InAnyName: true),
        new("Malachite", "공작석", InAnyName: true),
        new("Quicksilver", "수은", InAnyName: true),
        new("Corundum", "강옥", InAnyName: true),
        new("Orichalcum", "오리칼쿰", InAnyName: true),
        new("Iron", "철", InAnyName: false),
        new("Steel", "강철", InAnyName: false),
        new("Silver", "은", InAnyName: false),
        new("Glass", "글래스", InAnyName: false),
        new("Scaled", "미늘", InAnyName: false),
        new("Orcish", "오크제", InAnyName: false),
        new("Dwarven", "드워프제", InAnyName: false),
        new("Elven", "엘프제", InAnyName: false, NotAfter: "Snow"), // a Snow Elven staff is a Snow Elf's
        new("Nordic", "노드제", InAnyName: false),
        new("Ancient Nord", "고대 노드", InAnyName: false),
        new("Daedric", "데이드라제", InAnyName: false),
    };

    private static readonly HashSet<string> EquipmentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Armor", "Armour", "Cuirass", "Boots", "Gauntlets", "Gloves", "Bracers", "Helmet", "Helm", "Hood", "Mask",
        "Shield", "Greaves", "Pauldrons", "Sword", "Swords", "Greatsword", "Greatswords", "Longsword", "Shortsword",
        "Dagger", "Daggers", "Mace", "Maces", "Axe", "Axes", "War", "Battleaxe", "Battleaxes", "Warhammer",
        "Warhammers", "Hammer", "Bow", "Bows", "Longbow", "Crossbow", "Crossbows", "Arrow", "Arrows", "Bolt", "Bolts",
        "Staff", "Spear", "Spears", "Halberd", "Halberds", "Pike", "Glaive", "Katana", "Tanto", "Wakizashi", "Scimitar",
        "Rapier", "Claymore", "Javelin", "Javelins", "Quarterstaff", "Club", "Maul", "Scythe", "Sickle", "Trident",
        "Lance", "Knife", "Blade", "Blades", "Cleaver", "Hatchet", "Pickaxe", "Ingot", "Ingots", "Plate", "Weapon",
        "Weapons", "Gear", "Equipment",
    };

    private static Material[] ConfirmedMaterials(IReadOnlyList<(string Source, string Target)> pairs)
        => Materials.Where(material =>
            {
                var items = pairs.Where(pair => pair.Source.StartsWith(material.Word + " ", StringComparison.Ordinal)).ToList();
                var spelled = items.Count(pair => pair.Target.Contains(material.Target, StringComparison.Ordinal));
                return spelled >= 2 && spelled * 5 >= items.Count * 4;
            })
            .ToArray();

    private string ForceMaterials(string text, Dictionary<string, string> tokens, ref int number)
    {
        foreach (var material in _materials)
        {
            var input = text;
            var token = (string?)null;
            var next = number;
            text = material.Pattern.Replace(input, match =>
            {
                if (!IsMaterialUse(input, match.Index, match.Length, material))
                {
                    return match.Value;
                }

                token ??= $"__XT_TERM_N{++next}_0000__";
                tokens[token] = material.Target;
                return token;
            });
            number = next;
        }

        return text;
    }

    // "Ebony Beam", "Pure Ebony" and "Daedric armor" name the material; "Daedric Lord" and "made of Glass." do not.
    private static bool IsMaterialUse(string text, int index, int length, Material material)
    {
        var previous = AdjacentWord(text, index, forward: false);
        if (string.Equals(previous, material.NotAfter, StringComparison.Ordinal))
        {
            return false;
        }

        var next = AdjacentWord(text, index + length, forward: true);
        return EquipmentWords.Contains(next) || material.InAnyName && (IsNameWord(next) || IsNameWord(previous));
    }

    // A capitalized word or a term token (a glossary name) separated by spaces only.
    private static bool IsNameWord(string word)
        => word.Length > 0 && (char.IsAsciiLetterUpper(word[0]) || word.StartsWith("__XT_TERM_", StringComparison.Ordinal));

    private static string AdjacentWord(string text, int position, bool forward)
    {
        var step = forward ? 1 : -1;
        var i = forward ? position : position - 1;
        while (i >= 0 && i < text.Length && text[i] == ' ')
        {
            i += step;
        }

        var edge = i;
        while (i >= 0 && i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i += step;
        }

        return forward ? text[edge..i] : text[(i + 1)..(edge + 1)];
    }
}
