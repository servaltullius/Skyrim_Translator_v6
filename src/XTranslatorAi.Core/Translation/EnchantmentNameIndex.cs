using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// The official translation names an enchanted item "base - enchantment": "Imperial Boots of Dwindling Frost" is
/// "제국군 전투화 - 중급 냉기 저항", "Robes of Major Conjuring" is "로브 - 중급 소환마법". Armor and weapon mods reuse
/// these enchantments on their own items (3,984 such names in 21 local mods, e.g. "Vagrant Robes of Major Conjuring"),
/// and the model wrote them as Korean phrases ("감소의 냉기 제국군 부츠"). The suffix translations are learned from the
/// series TM, and an item name ending in a known one gets the official form as a hint.
/// </summary>
public sealed class EnchantmentNameIndex
{
    private const int MinEntries = 2;
    private const double MinAgreement = 0.8;

    private static readonly Regex SourceNamePattern = new(@"^(?<base>.+?) of (?:the )?(?<suffix>[A-Z][\w' ]*\w)$", RegexOptions.CultureInvariant);
    private static readonly Regex TargetNamePattern = new(@"^(?<base>.+?) - (?<suffix>.+)$", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, string> _suffixes;

    private EnchantmentNameIndex(Dictionary<string, string> suffixes) => _suffixes = suffixes;

    public int Count => _suffixes.Count;

    public static EnchantmentNameIndex Build(IEnumerable<(string Source, string Target)> memory)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (source, target) in memory)
        {
            var s = SourceNamePattern.Match((source ?? "").Trim());
            var t = TargetNamePattern.Match((target ?? "").Trim());
            if (!s.Success || !t.Success)
            {
                continue;
            }

            var suffix = s.Groups["suffix"].Value;
            if (!counts.TryGetValue(suffix, out var targets))
            {
                targets = new Dictionary<string, int>(StringComparer.Ordinal);
                counts[suffix] = targets;
            }

            var translated = t.Groups["suffix"].Value.Trim();
            targets[translated] = targets.GetValueOrDefault(translated) + 1;
        }

        // Only a suffix the official translation renders the same way nearly every time.
        var suffixes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (suffix, targets) in counts)
        {
            var total = targets.Values.Sum();
            var (best, bestCount) = targets.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First();
            if (total >= MinEntries && bestCount >= MinAgreement * total)
            {
                suffixes[suffix] = best;
            }
        }

        return new EnchantmentNameIndex(suffixes);
    }

    /// <summary>The hint for an armor, weapon or ammunition name ending in a known enchantment; otherwise null.</summary>
    public string? GetHint(string sourceText, string? rec)
    {
        if (!IsItemName(rec))
        {
            return null;
        }

        var match = SourceNamePattern.Match((sourceText ?? "").Trim());
        if (!match.Success || !_suffixes.TryGetValue(match.Groups["suffix"].Value, out var translated))
        {
            return null;
        }

        var suffix = match.Groups["suffix"].Value;
        return $"Item with a vanilla enchantment: the official Korean form is \"<translated base name> - {translated}\" "
            + $"(\"of {suffix}\" = \" - {translated}\"). Translate only \"{match.Groups["base"].Value}\" as the base name.";
    }

    private static bool IsItemName(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return false;
        }

        var r = rec.Trim().ToUpperInvariant();
        return r is "ARMO:FULL" or "WEAP:FULL" or "AMMO:FULL";
    }
}
