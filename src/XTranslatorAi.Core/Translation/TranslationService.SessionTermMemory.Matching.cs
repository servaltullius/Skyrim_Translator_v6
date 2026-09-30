using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    // Automatic suggestions are for names, not standalone mechanics or UI verbs.
    // Reviewed/preloaded terms bypass this conservative filter.
    private static readonly HashSet<string> GenericSessionTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Trigger", "Activate", "Cast", "Gain", "Increase", "Decrease", "Enable", "Disable",
        "Attack", "Block", "Dodge", "Roll", "Jump", "Run", "Sprint", "Move", "Use",
        "Damage", "Health", "Magicka", "Stamina", "Chance", "Duration", "Effect", "Impact",
        "Bonus", "Skill", "Spell", "Perk", "Power", "Level", "Target", "Player", "Enemy",
        "Art", "Weapon", "Armor", "Item", "Quest", "Book", "Menu", "Settings", "Default",
        "Enabled", "Disabled", "On", "Off", "None", "Yes", "No", "True", "False",
    };

    private static bool IsAutomaticSessionTermCandidate(string source)
    {
        if (!IsSessionTermDefinitionText(source)) return false;
        var key = NormalizeSessionTermKey(source);
        if (GenericSessionTerms.Contains(key)
            || (key.EndsWith('s') && GenericSessionTerms.Contains(key[..^1]))) return false;
        foreach (var prefix in new[] { "Enable ", "Disable ", "Activate ", "Deactivate ", "Toggle ", "Set ", "Reset " })
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool IsSessionTermWordChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static bool ContainsSessionTerm(string text, string source)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(source)) return false;
        var offset = 0;
        while (offset <= text.Length - source.Length)
        {
            var index = text.IndexOf(source, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            var end = index + source.Length;
            if ((index == 0 || !IsSessionTermWordChar(text[index - 1]))
                && (end == text.Length || !IsSessionTermWordChar(text[end]))) return true;
            offset = end;
        }
        return false;
    }

    private static string? GetSessionTermPluralHint(string source)
    {
        var space = source.LastIndexOf(' ');
        if (space < 0) return null;
        var last = source[(space + 1)..];
        return last.ToLowerInvariant() is "art" or "skill" or "spell" or "spear" or "sword" or "rune" or "attack"
            ? source + "s" : null;
    }
}
