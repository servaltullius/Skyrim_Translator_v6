using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Text.ProjectContext;

public sealed class ProjectContextScanner
{
    private sealed record ScanAccumulation(
        Dictionary<string, int> RecCounts,
        Dictionary<string, int> TermCounts,
        List<ProjectContextSample> Samples,
        Dictionary<string, string> EditedByKey
    );

    private static readonly Regex PlaceholderTagRegex = new(
        pattern: @"<\s*(?:mag|dur|\d+)\s*>",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex PagebreakRegex = new(
        pattern: @"\[pagebreak\]",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex EnglishPhraseRegex = new(
        pattern:
            @"\b(?:[A-Z][A-Za-z0-9'’\-]*)(?:\s+(?:[A-Z][A-Za-z0-9'’\-]*|of|the|and|or|to|a|an|in|on|for|with|from|at|by|de|la|le|du|van|von))+\b",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex EnglishTitleWordRegex = new(
        pattern: @"\b[A-Z][A-Za-z0-9'’\-]{2,}\b",
        options: RegexOptions.CultureInvariant
    );

    public async Task<ProjectContextScanReport> ScanAsync(
        ProjectDb db,
        ProjectDb? globalDb,
        ProjectContextScanOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);

        var total = (int)Math.Min(int.MaxValue, await db.GetStringCountAsync(cancellationToken));

        var glossaryByKey = await BuildGlossaryByKeyAsync(db, globalDb, cancellationToken);
        var scan = await ScanStringsAsync(db, total, cancellationToken);

        var topRec = BuildTopRec(scan.RecCounts);
        var topTerms = BuildTopTerms(scan.TermCounts, glossaryByKey, scan.EditedByKey);

        var nexus = string.IsNullOrWhiteSpace(options.NexusContext) ? null : options.NexusContext.Trim();

        return new ProjectContextScanReport(
            AddonName: string.IsNullOrWhiteSpace(options.AddonName) ? null : options.AddonName.Trim(),
            InputFile: string.IsNullOrWhiteSpace(options.InputFile) ? null : options.InputFile.Trim(),
            SourceLang: options.SourceLang?.Trim() ?? "",
            TargetLang: options.TargetLang?.Trim() ?? "",
            TotalStrings: total,
            TopRec: topRec,
            TopTerms: topTerms,
            Samples: scan.Samples,
            NexusContext: nexus
        );
    }

    private static async Task<Dictionary<string, string>> BuildGlossaryByKeyAsync(
        ProjectDb db,
        ProjectDb? globalDb,
        CancellationToken cancellationToken
    )
    {
        var projectGlossary = await db.GetGlossaryAsync(cancellationToken);

        IReadOnlyList<GlossaryEntry>? globalGlossary = null;
        if (globalDb != null)
        {
            try
            {
                globalGlossary = await globalDb.GetGlossaryAsync(cancellationToken);
            }
            catch
            {
                globalGlossary = null;
            }
        }

        var glossary = GlossaryMerger.Merge(projectGlossary, globalGlossary);

        var glossaryByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in glossary)
        {
            if (!g.Enabled)
            {
                continue;
            }

            var key = NormalizeSessionTermKey(g.SourceTerm);
            if (string.IsNullOrWhiteSpace(key) || glossaryByKey.ContainsKey(key))
            {
                continue;
            }

            glossaryByKey[key] = g.TargetTerm?.Trim() ?? "";
        }

        return glossaryByKey;
    }

    private static async Task<ScanAccumulation> ScanStringsAsync(ProjectDb db, int total, CancellationToken cancellationToken)
    {
        var recCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var termCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var editedByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<ProjectContextSample>();
        var seenSamples = new HashSet<string>(StringComparer.Ordinal);

        const int pageSize = 2000;
        for (var offset = 0; offset < total; offset += pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await db.GetStringsAsync(pageSize, offset, cancellationToken);
            foreach (var row in rows)
            {
                AccumulateRecCount(recCounts, row.Rec);
                AccumulateEditedSessionTerm(editedByKey, row);

                var source = row.SourceText ?? "";
                if (string.IsNullOrWhiteSpace(source))
                {
                    continue;
                }

                AccumulateTitleCaseTerms(termCounts, source);
                TryAccumulateSample(samples, seenSamples, row.Rec, source);
            }
        }

        return new ScanAccumulation(recCounts, termCounts, samples, editedByKey);
    }

    private static void AccumulateRecCount(Dictionary<string, int> recCounts, string? recRaw)
    {
        var rec = (recRaw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(rec))
        {
            return;
        }

        if (recCounts.TryGetValue(rec, out var n))
        {
            recCounts[rec] = n + 1;
        }
        else
        {
            recCounts[rec] = 1;
        }
    }

    private static void AccumulateEditedSessionTerm(Dictionary<string, string> editedByKey, StringEntry row)
    {
        if (row.Status != StringEntryStatus.Edited
            || !IsSessionTermRec(row.Rec)
            || !IsSessionTermDefinitionText(row.SourceText ?? "")
            || string.IsNullOrWhiteSpace(row.DestText))
        {
            return;
        }

        var key = NormalizeSessionTermKey(row.SourceText ?? "");
        if (string.IsNullOrWhiteSpace(key) || editedByKey.ContainsKey(key))
        {
            return;
        }

        editedByKey[key] = row.DestText.Trim();
    }

    private static void AccumulateTitleCaseTerms(Dictionary<string, int> termCounts, string source)
    {
        var uniqueTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in ExtractTitleCaseCandidates(source))
        {
            var key = NormalizeSessionTermKey(term);
            if (key.Length is < 3 or > 80)
            {
                continue;
            }

            uniqueTerms.Add(key);
        }

        foreach (var t in uniqueTerms)
        {
            if (termCounts.TryGetValue(t, out var n))
            {
                termCounts[t] = n + 1;
            }
            else
            {
                termCounts[t] = 1;
            }
        }
    }

    private static void TryAccumulateSample(
        List<ProjectContextSample> samples,
        HashSet<string> seenSamples,
        string? recRaw,
        string source
    )
    {
        if (samples.Count >= 28)
        {
            return;
        }

        var isInteresting = PlaceholderTagRegex.IsMatch(source)
            || PagebreakRegex.IsMatch(source)
            || source.IndexOf('/', StringComparison.Ordinal) >= 0;

        if (!isInteresting)
        {
            return;
        }

        var rec = (recRaw ?? "").Trim();
        var sample = $"[{rec}] {source}".Trim();
        if (seenSamples.Contains(sample))
        {
            return;
        }

        seenSamples.Add(sample);
        samples.Add(new ProjectContextSample(rec, TruncateExample(source, 220)));
    }

    private static List<ProjectContextRecCount> BuildTopRec(Dictionary<string, int> recCounts)
    {
        return recCounts
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .Select(kv => new ProjectContextRecCount(kv.Key, kv.Value))
            .ToList();
    }

    private static List<ProjectContextTermInfo> BuildTopTerms(
        Dictionary<string, int> termCounts,
        Dictionary<string, string> glossaryByKey,
        Dictionary<string, string> editedByKey
    )
    {
        return termCounts
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => kv.Key.Length)
            .Where(kv => kv.Value >= 3)
            .Take(80)
            .Select(
                kv =>
                {
                    var key = kv.Key;
                    string? target = null;

                    if (glossaryByKey.TryGetValue(key, out var g) && !string.IsNullOrWhiteSpace(g))
                    {
                        target = g;
                    }
                    else if (editedByKey.TryGetValue(key, out var e) && !string.IsNullOrWhiteSpace(e))
                    {
                        target = e;
                    }

                    return new ProjectContextTermInfo(Source: key, Count: kv.Value, Target: string.IsNullOrWhiteSpace(target) ? null : target);
                }
            )
            .ToList();
    }

    private static IEnumerable<string> ExtractTitleCaseCandidates(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            yield break;
        }

        foreach (Match m in EnglishPhraseRegex.Matches(sourceText))
        {
            if (m.Success && !string.IsNullOrWhiteSpace(m.Value))
            {
                yield return m.Value;
            }
        }

        foreach (Match m in EnglishTitleWordRegex.Matches(sourceText))
        {
            if (m.Success && !string.IsNullOrWhiteSpace(m.Value))
            {
                yield return m.Value;
            }
        }
    }

    private static bool IsSessionTermRec(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return false;
        }

        return rec.IndexOf(":FULL", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":NAME", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":NAM", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":TITLE", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSessionTermDefinitionText(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var s = sourceText.Trim();
        if (s.Length is < 3 or > 60)
        {
            return false;
        }

        if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
        {
            return false;
        }

        if (XtTokenRegex.IsMatch(s))
        {
            return false;
        }

        if (s.IndexOf(' ') < 0 && !IsSessionTermSingleWordCandidate(s))
        {
            return false;
        }

        // Avoid obvious sentences.
        if (s.IndexOfAny(new[] { '.', '!', '?', ':', ';' }) >= 0)
        {
            return false;
        }

        // Keep conservative: allow letters/digits/spaces plus common name punctuation.
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '’')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsSessionTermSingleWordCandidate(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var s = term.Trim();
        if (s.Length is < 3 or > 40)
        {
            return false;
        }

        // Require TitleCase-ish to avoid learning generic words.
        if (s[0] is < 'A' or > 'Z')
        {
            return false;
        }

        var hasLower = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is >= 'a' and <= 'z')
            {
                hasLower = true;
                break;
            }
        }

        return hasLower;
    }

    private static string NormalizeSessionTermKey(string term)
    {
        var s = (term ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(4).TrimStart();
        }
        else if (s.StartsWith("An ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(3).TrimStart();
        }
        else if (s.StartsWith("A ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(2).TrimStart();
        }

        return s.Trim();
    }

    private static string TruncateExample(string text, int maxChars)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0 || maxChars <= 0)
        {
            return "";
        }

        s = CollapseWhitespace(s);
        if (s.Length <= maxChars)
        {
            return s;
        }

        return s[..maxChars].TrimEnd() + "…";
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(capacity: text.Length);
        var wasWhitespace = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                if (!wasWhitespace)
                {
                    sb.Append(' ');
                    wasWhitespace = true;
                }

                continue;
            }

            sb.Append(ch);
            wasWhitespace = false;
        }

        return sb.ToString();
    }
}
