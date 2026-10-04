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
    string DestText,
    // The same field in the earlier translated release, when the project refers to one.
    string? PreviousText = null
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
    // Control keys ("[Sprint]") stay in English on purpose, so they are not English left in a translation.
    private static readonly Regex UiTagTokenRegex = new(
        pattern: @"[+-]?<" + TranslationConstants.StageDirectionGuard + @"\s*[^>]+\s*>|\[page ?break\]|__XT_[A-Za-z0-9_]+__|" + PlaceholderMasker.ControlKeyPattern,
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex EnglishResidueRegex = new(
        pattern: @"[A-Za-z]{2,}",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex CjkRegex = new(
        pattern: @"[\u3040-\u30FF\u3400-\u4DBF\u4E00-\u9FFF]",
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
        IReadOnlyDictionary<long, string>? tmFallbackNotes = null,
        XTranslatorAi.Core.Translation.ReferenceNameIndex? referenceNames = null
    )
    {
        var issues = new List<LqaIssue>();
        var context = new LqaScanContext(entries, targetLang, forceTokenGlossary, onProgress, tmFallbackNotes, referenceNames);
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
        var glossaryLatinWords = BuildGlossaryLatinWords(forceTokenGlossary);
        var strongDialogueMajority = DialogueToneConsistencyRule.BuildDialogueGroupMajorities(entries);
        var nameFindings = NameConsistencyRule.Build(entries, isKorean);
        var loanwordIndex = isKorean ? GlossaryLoanwordRule.Build(forceTokenGlossary) : new Dictionary<string, GlossaryLoanwordRule.Term>();
        var fieldToneMajority = RecToneRule.BuildFieldMajorities(entries);
        var glossarySources = OfficialNameRule.BuildGlossarySources(forceTokenGlossary);
        var sameSourceVariants = SameSourceVariantRule.Build(entries);

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
                glossaryLatinWords,
                tmFallbackNotes,
                strongDialogueMajority,
                fieldToneMajority,
                nameFindings,
                loanwordIndex,
                issues
            );
            OfficialNameRule.Apply(entry, source, dest, isKorean, context.ReferenceNames, glossarySources, issues);
            SameSourceVariantRule.Apply(entry, sameSourceVariants, issues);
        }
    }

    private static void ApplyExtractedRulesForEntry(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        bool isKorean,
        IReadOnlyList<GlossaryEntry> forceTokenGlossary,
        IReadOnlyList<string> particleTerms,
        IReadOnlySet<string> glossaryLatinWords,
        IReadOnlyDictionary<long, string>? tmFallbackNotes,
        IReadOnlyDictionary<string, ToneKind> strongDialogueMajority,
        IReadOnlyDictionary<string, ToneKind> fieldToneMajority,
        IReadOnlyDictionary<long, string> nameFindings,
        IReadOnlyDictionary<string, GlossaryLoanwordRule.Term> loanwordIndex,
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

        GlossaryLoanwordRule.Apply(entry, sourceText, destText, isKorean, loanwordIndex, issues);

        if (TryAddLengthRiskIssue(entry, sourceText, destText, out var lengthIssue))
        {
            issues.Add(lengthIssue);
        }

        RecToneRule.Apply(entry, sourceText, destText, fieldToneMajority, issues);

        ParticleRules.Apply(entry, sourceText, destText, isKorean, particleTerms, issues);

        LegacyPostEditDamageRule.Apply(entry, sourceText, destText, isKorean, issues);

        BracketMismatchRule.Apply(entry, sourceText, destText, issues);

        EnglishResidueRule.Apply(entry, sourceText, destText, isKorean, glossaryLatinWords, issues);

        ForeignScriptResidueRule.Apply(entry, sourceText, destText, isKorean, issues);

        HiddenTopicRule.Apply(entry, sourceText, destText, issues);

        MixedDescriptionToneRule.Apply(entry, destText, isKorean, issues);

        DialogueToneConsistencyRule.Apply(entry, strongDialogueMajority, issues);

        NameConsistencyRule.Apply(entry, nameFindings, issues);

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
    /// source (NPC, MCO, WASD, Lv3, BloodSword01, 30-ex, EldenRim of EldenRimUpdate) are meant to stay
    /// and do not count, nor do words the earlier translated release also kept (an author "The One");
    /// ordinary words such as an untranslated item name do.
    /// </summary>
    internal static string? FindEnglishResidue(string destText, string sourceText, string? previousText = null,
        IReadOnlySet<string>? glossaryLatinWords = null)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        var dest = StripUiTokens(destText);
        // A source written in Chinese or Japanese keeps its few Latin words on purpose ("战技-动作执行-新-Npc").
        var cjkSource = CjkRegex.IsMatch(sourceText);
        foreach (Match m in LatinWordRegex.Matches(dest))
        {
            var word = m.Value;
            // "@thecrimsonfucker": a user handle, never translated.
            var isHandle = m.Index > 0 && dest[m.Index - 1] == '@';
            var keptFromCjkSource = cjkSource && ContainsWholeWord(sourceText, word, StringComparison.Ordinal);
            if (word.Count(char.IsAsciiLetter) >= 2
                && !isHandle
                && !keptFromCjkSource
                && !IsAcronymOrIdentifierFromSource(word, sourceText)
                && !IsPartOfNumberedTokenFromSource(dest, m, sourceText)
                && !ContainsWholeWord(previousText, word, StringComparison.Ordinal)
                && glossaryLatinWords?.Contains(word) != true)
            {
                return word;
            }
        }

        return null;
    }

    // The glossary may keep a term in Latin letters ("Thu'um" => "Thu'um"); the translation follows it.
    private static IReadOnlySet<string> BuildGlossaryLatinWords(IReadOnlyList<GlossaryEntry> glossary)
        => glossary.SelectMany(entry => LatinWordRegex.Matches(entry.TargetTerm ?? "").Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsAcronymOrIdentifierFromSource(string word, string sourceText)
    {
        var hasLower = word.Any(char.IsLower);
        var isAcronymOrIdentifier = !hasLower || word.Any(char.IsDigit) || word.Skip(1).Any(char.IsUpper);
        if (!isAcronymOrIdentifier)
        {
            return false;
        }

        // An all-caps acronym or a word with digits may be cased differently in the source ("npc addition" →
        // "NPC 추가", Elden Rim's "Rune Impact lv2" → "룬 임팩트 Lv2").
        var comparison = hasLower && !word.Any(char.IsDigit) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var idx = sourceText.IndexOf(word, comparison); idx >= 0; idx = sourceText.IndexOf(word, idx + 1, comparison))
        {
            var end = idx + word.Length;
            if (IsIdentifierStart(sourceText, idx, word) && IsIdentifierEnd(sourceText, end, word))
            {
                return true;
            }
        }

        return false;
    }

    // A CamelCase segment counts as a whole part: "EldenRim" of "EldenRimUpdate" → "EldenRim 업데이트".
    private static bool IsIdentifierStart(string text, int idx, string word)
        => idx == 0 || !char.IsAsciiLetterOrDigit(text[idx - 1])
           || (char.IsUpper(word[0]) && (char.IsLower(text[idx - 1]) || char.IsDigit(text[idx - 1])));

    // Also "DLC1" of "DLC1NPCMental…" and "OP" of "SDA_OPReaction2" (an acronym before a capitalized word).
    private static bool IsIdentifierEnd(string text, int end, string word)
        => end >= text.Length || !char.IsAsciiLetterOrDigit(text[end])
           || ((char.IsLower(word[^1]) || char.IsDigit(word[^1])) && char.IsUpper(text[end]))
           || (char.IsUpper(word[^1]) && char.IsUpper(text[end]) && end + 1 < text.Length && char.IsLower(text[end + 1]));

    // A suffix joined to a number by a hyphen, "30-ex" in "Afterglow Qi - 30-ex", is one identifier.
    private static bool IsPartOfNumberedTokenFromSource(string dest, Match word, string sourceText)
    {
        var start = word.Index;
        var end = word.Index + word.Length;
        while (start > 0 && (char.IsAsciiLetterOrDigit(dest[start - 1]) || dest[start - 1] == '-')) start--;
        while (end < dest.Length && (char.IsAsciiLetterOrDigit(dest[end]) || dest[end] == '-')) end++;
        var token = dest[start..end].Trim('-');
        return token.Length > word.Length && token.Contains('-') && token.Any(char.IsAsciiDigit)
               && ContainsWholeWord(sourceText, token, StringComparison.Ordinal);
    }

    private static bool ContainsWholeWord(string? text, string word, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var idx = text.IndexOf(word, comparison); idx >= 0; idx = text.IndexOf(word, idx + 1, comparison))
        {
            var end = idx + word.Length;
            if ((idx == 0 || !char.IsAsciiLetterOrDigit(text[idx - 1]))
                && (end >= text.Length || !char.IsAsciiLetterOrDigit(text[end])))
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

    // An unbalanced translation is reported only when the source is not unbalanced the same way:
    // numbered steps "1) Gather herbs. 2) Grind them." faithfully become "1) 약초를 모은다. 2) 빻는다.".
    internal static bool HasBracketMismatch(string sourceText, string destText)
    {
        if (string.IsNullOrEmpty(destText))
        {
            return false;
        }

        return DiffersFromSource('(', ')') || DiffersFromSource('[', ']');

        bool DiffersFromSource(char open, char close)
        {
            var destBalance = CountChar(destText, open) - CountChar(destText, close);
            return destBalance != 0
                   && destBalance != CountChar(sourceText ?? "", open) - CountChar(sourceText ?? "", close);
        }
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
