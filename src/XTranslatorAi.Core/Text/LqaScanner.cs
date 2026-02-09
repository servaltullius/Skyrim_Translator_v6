using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text.Lqa.Internal;
using XTranslatorAi.Core.Text.Lqa.Internal.Rules;

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
        pattern: @"[+-]?<\s*[^>]+\s*>|\[pagebreak\]|__XT_[A-Za-z0-9_]+__",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex EnglishResidueRegex = new(
        pattern: @"[A-Za-z]{2,}",
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
        var strongDialogueMajority = DialogueToneConsistencyRule.BuildDialogueGroupMajorities(entries);

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
                await Task.Delay(1);
            }

            var source = entry.SourceText ?? "";
            var dest = entry.DestText ?? "";
            ApplyExtractedRulesForEntry(
                entry,
                source,
                dest,
                isKorean,
                forceTokenGlossary,
                tmFallbackNotes,
                strongDialogueMajority,
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
        IReadOnlyDictionary<long, string>? tmFallbackNotes,
        IReadOnlyDictionary<string, ToneKind> strongDialogueMajority,
        List<LqaIssue> issues
    )
    {
        TmFallbackRule.Apply(entry, sourceText, destText, tmFallbackNotes, issues);

        if (UntranslatedRule.ApplyAndShouldShortCircuit(entry, sourceText, destText, isKorean, issues))
        {
            return;
        }

        TokenMismatchRule.Apply(entry, sourceText, destText, issues);

        GlossaryMissingRule.Apply(entry, sourceText, destText, isKorean, forceTokenGlossary, issues);

        if (TryAddLengthRiskIssue(entry, sourceText, destText, out var lengthIssue))
        {
            issues.Add(lengthIssue);
        }

        RecToneRule.Apply(entry, sourceText, destText, issues);

        ParticleRules.Apply(entry, sourceText, destText, isKorean, issues);

        BracketMismatchRule.Apply(entry, sourceText, destText, issues);

        EnglishResidueRule.Apply(entry, sourceText, destText, isKorean, issues);

        DialogueToneConsistencyRule.Apply(entry, strongDialogueMajority, issues);
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

    internal static bool HasTokenMismatch(string sourceText, string destText)
    {
        var sourceTokens = CollectUiTokens(sourceText);
        if (sourceTokens.Count == 0)
        {
            return false;
        }

        var destTokens = CollectUiTokens(destText);
        if (sourceTokens.Count != destTokens.Count)
        {
            return true;
        }

        foreach (var kvp in sourceTokens)
        {
            if (!destTokens.TryGetValue(kvp.Key, out var otherCount) || otherCount != kvp.Value)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, int> CollectUiTokens(string text)
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return dict;
        }

        foreach (Match m in UiTagTokenRegex.Matches(text))
        {
            var raw = m.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var token = NormalizeUiToken(raw);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (dict.TryGetValue(token, out var count))
            {
                dict[token] = count + 1;
            }
            else
            {
                dict[token] = 1;
            }
        }

        return dict;
    }

    private static string NormalizeUiToken(string token)
    {
        var s = token.Trim();
        if (s.Length == 0)
        {
            return "";
        }

        if (s[0] == '<')
        {
            s = Regex.Replace(s, @"\s+", "", RegexOptions.CultureInvariant);
            return s.ToLowerInvariant();
        }

        if (s[0] == '[')
        {
            return s.ToLowerInvariant();
        }

        if (s.StartsWith("__XT_", StringComparison.OrdinalIgnoreCase))
        {
            return s.ToUpperInvariant();
        }

        return s;
    }
}
