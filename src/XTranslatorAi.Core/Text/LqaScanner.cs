using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text.Lqa.Internal;
using XTranslatorAi.Core.Text.Lqa.Internal.Rules;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Text;

public readonly record struct LqaScanEntry(
    long Id,
    int OrderIndex,
    string? Edid,
    string? Rec,
    StringEntryStatus Status,
    string SourceText,
    string DestText
);

public readonly record struct LqaIssue(
    long Id,
    int OrderIndex,
    string? Edid,
    string? Rec,
    string Severity,
    string Code,
    string Message,
    string SourceText,
    string DestText
);

public static class LqaScanner
{
    private static readonly Regex UiTagTokenRegex = new(
        pattern: @"[+-]?<\s*[^>]+\s*>|\[page ?break\]|__XT_[A-Za-z0-9_]+__",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex EnglishResidueRegex = new(
        pattern: @"[A-Za-z]{2,}",
        options: RegexOptions.CultureInvariant
    );

    // A whole run of Latin letters and digits, so "05000A6E" in [ARMO:05000A6E] is one identifier.
    private static readonly Regex LatinWordRegex = new(
        pattern: @"[A-Za-z0-9]*[A-Za-z][A-Za-z0-9]*",
        options: RegexOptions.CultureInvariant
    );

    public static async Task<List<LqaIssue>> ScanAsync(
        IReadOnlyList<LqaScanEntry> entries,
        string targetLang,
        IReadOnlyList<GlossaryEntry> forceTokenGlossary,
        Action<int>? onProgress = null,
        IReadOnlyDictionary<long, string>? tmFallbackNotes = null
    )
    {
        var issues = new List<LqaIssue>();
        var context = new LqaScanContext(entries, targetLang, forceTokenGlossary, onProgress, tmFallbackNotes);
        await ApplyExtractedRulePipelineAsync(context, issues);

        LqaIssueSorter.Sort(issues);

        return issues;
    }

    private static async Task ApplyExtractedRulePipelineAsync(LqaScanContext context, List<LqaIssue> issues)
    {
        var entries = context.Entries;
        var targetLang = context.TargetLang;
        var forceTokenGlossary = context.ForceTokenGlossary;
        var onProgress = context.OnProgress;
        var tmFallbackNotes = context.TmFallbackNotes;

        var isKorean = IsKoreanLanguage(targetLang);
        var particleTerms = LqaHeuristics.BuildParticleCheckTerms(forceTokenGlossary);
        var strongDialogueMajority = DialogueToneConsistencyRule.BuildDialogueGroupMajorities(entries);
        var fieldToneMajority = RecToneRule.BuildFieldMajorities(entries);

        var total = entries.Count;
        for (var i = 0; i < total; i++)
        {
            var entry = entries[i];
            if (entry.Status != StringEntryStatus.Done && entry.Status != StringEntryStatus.Edited)
            {
                continue;
            }

            if ((i % 2000) == 0)
            {
                var pct = total == 0 ? 100 : (int)Math.Round(100.0 * i / total);
                onProgress?.Invoke(pct);
                await Task.Yield();
            }

            var source = entry.SourceText ?? "";
            var dest = entry.DestText ?? "";
            ApplyExtractedRulesForEntry(
                entry,
                source,
                dest,
                isKorean,
                forceTokenGlossary,
                particleTerms,
                tmFallbackNotes,
                strongDialogueMajority,
                fieldToneMajority,
                issues
            );
        }
    }

    private static void ApplyExtractedRulesForEntry(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        bool isKorean,
        IReadOnlyList<GlossaryEntry> forceTokenGlossary,
        IReadOnlyList<string> particleTerms,
        IReadOnlyDictionary<long, string>? tmFallbackNotes,
        IReadOnlyDictionary<string, ToneKind> strongDialogueMajority,
        IReadOnlyDictionary<string, ToneKind> fieldToneMajority,
        List<LqaIssue> issues
    )
    {
        TmFallbackRule.Apply(entry, sourceText, destText, tmFallbackNotes, issues);

        // Even an otherwise untranslated row can have lost markup or line breaks.
        // Integrity failures must not disappear behind the untranslated heuristic.
        TokenMismatchRule.Apply(entry, sourceText, destText, issues);

        if (UntranslatedRule.ApplyAndShouldShortCircuit(entry, sourceText, destText, isKorean, issues))
        {
            return;
        }

        GlossaryMissingRule.Apply(entry, sourceText, destText, isKorean, forceTokenGlossary, issues);

        if (TryAddLengthRiskIssue(entry, sourceText, destText, out var lengthIssue))
        {
            issues.Add(lengthIssue);
        }

        RecToneRule.Apply(entry, sourceText, destText, fieldToneMajority, issues);

        ParticleRules.Apply(entry, sourceText, destText, isKorean, particleTerms, issues);

        LegacyPostEditDamageRule.Apply(entry, sourceText, destText, isKorean, issues);

        BracketMismatchRule.Apply(entry, sourceText, destText, issues);

        EnglishResidueRule.Apply(entry, sourceText, destText, isKorean, issues);

        DialogueToneConsistencyRule.Apply(entry, strongDialogueMajority, issues);

        BookStructureRules.Apply(entry, sourceText, destText, issues);

        NameVerbEndingRule.Apply(entry, destText, isKorean, issues);
    }

    private static bool TryAddLengthRiskIssue(LqaScanEntry entry, string sourceText, string destText, out LqaIssue issue)
    {
        issue = default;

        var recBase = GetRecBase(entry.Rec);
        if (recBase is not ("QUST" or "MESG"))
        {
            return false;
        }

        var sourceClean = StripUiTokens(sourceText).Trim();
        var destClean = StripUiTokens(destText).Trim();
        if (sourceClean.Length <= 0 || destClean.Length <= 0)
        {
            return false;
        }

        var ratio = (double)destClean.Length / sourceClean.Length;

        var (absThreshold, ratioThreshold) = recBase == "MESG"
            ? (absThreshold: 160, ratioThreshold: 2.5)
            : (absThreshold: 300, ratioThreshold: 2.2);

        // Conservative: require both absolute and relative growth to reduce false positives.
        var isTooLong = destClean.Length >= absThreshold && ratio >= ratioThreshold;
        if (!isTooLong)
        {
            return false;
        }

        issue = new LqaIssue(
            Id: entry.Id,
            OrderIndex: entry.OrderIndex,
            Edid: entry.Edid,
            Rec: entry.Rec,
            Severity: "Warn",
            Code: "length_risk",
            Message: $"길이 위험: src={sourceClean.Length}, dst={destClean.Length}, x{ratio:0.00}",
            SourceText: sourceText,
            DestText: destText
        );
        return true;
    }

    private static bool IsKoreanLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var s = lang.Trim();
        if (string.Equals(s, "korean", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ko", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.StartsWith("ko-", StringComparison.OrdinalIgnoreCase) || s.StartsWith("ko_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return string.Equals(s, "한국어", StringComparison.OrdinalIgnoreCase)
               || s.IndexOf("한국", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool HasEnglishResidue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = StripUiTokens(text);
        return EnglishResidueRegex.IsMatch(cleaned);
    }

    /// <summary>
    /// The first Latin word left in a translation, or null. Acronyms and identifiers copied from the
    /// source (NPC, MCO, WASD, Lv3, BloodSword01) are meant to stay and do not count; ordinary words
    /// such as an untranslated item name do.
    /// </summary>
    internal static string? FindEnglishResidue(string destText, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        foreach (Match m in LatinWordRegex.Matches(StripUiTokens(destText)))
        {
            var word = m.Value;
            if (word.Count(char.IsAsciiLetter) >= 2 && !IsAcronymOrIdentifierFromSource(word, sourceText))
            {
                return word;
            }
        }

        return null;
    }

    private static bool IsAcronymOrIdentifierFromSource(string word, string sourceText)
    {
        var hasLower = word.Any(char.IsLower);
        var isAcronymOrIdentifier = !hasLower || word.Any(char.IsDigit) || word.Skip(1).Any(char.IsUpper);
        if (!isAcronymOrIdentifier)
        {
            return false;
        }

        // An all-caps acronym may be cased differently in the source ("npc addition" → "NPC 추가").
        var comparison = hasLower ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var idx = sourceText.IndexOf(word, comparison); idx >= 0; idx = sourceText.IndexOf(word, idx + 1, comparison))
        {
            var end = idx + word.Length;
            if ((idx == 0 || !char.IsAsciiLetterOrDigit(sourceText[idx - 1]))
                && (end >= sourceText.Length || !char.IsAsciiLetterOrDigit(sourceText[end])))
            {
                return true;
            }
        }

        return false;
    }

    internal static string StripUiTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return UiTagTokenRegex.Replace(text, "");
    }

    internal static bool HasBracketMismatch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return CountChar(text, '(') != CountChar(text, ')')
               || CountChar(text, '[') != CountChar(text, ']');
    }

    private static int CountChar(string text, char c)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == c)
            {
                count++;
            }
        }

        return count;
    }

    internal static string GetRecBase(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return "";
        }

        var trimmed = rec.Trim();
        var idx = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (idx > 0)
        {
            trimmed = trimmed[..idx];
        }

        return trimmed.ToUpperInvariant();
    }

    internal static string NormalizeEdidStem(string? edid)
    {
        if (string.IsNullOrWhiteSpace(edid))
        {
            return "";
        }

        var value = edid.Trim();

        var end = value.Length;
        while (end > 0 && char.IsDigit(value[end - 1]))
        {
            end--;
        }

        if (end <= 0)
        {
            return "";
        }

        value = value[..end].TrimEnd('_', '-', ' ');
        return value;
    }

    /// <summary>Uses the same protected-text contract as translation and plugin export.</summary>
    public static bool HasTokenMismatch(string sourceText, string destText)
    {
        try
        {
            TokenValidator.ValidateFinalTextIntegrity(sourceText, destText, "LQA");
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>
    /// Whether clearing this source would remove protected content. This deliberately
    /// includes placeholders and line breaks, not only visible angle-bracket tags.
    /// </summary>
    public static bool HasProtectedText(string text) => HasTokenMismatch(text, "");
}
