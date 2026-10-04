using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public enum PromptLintSeverity
{
    Warning = 0,
    Error = 1,
}

public sealed record PromptLintIssue(
    PromptLintSeverity Severity,
    string Source,
    string Message,
    string MatchedText
);

public static class PromptConflictLint
{
    /// <param name="ScopedIsWarning">
    /// An instruction that names what it keeps ("SkyUI 같은 제품명은 번역하지 말 것", "Do not translate mod names")
    /// is a narrow rule, not a blanket one; it is reported without blocking the run.
    /// </param>
    private sealed record PhraseRule(
        PromptLintSeverity Severity,
        string Message,
        string[] Needles,
        bool ScopedIsWarning = false
    );

    private const string ScopedKeepMessage =
        "This instruction keeps the content it names untranslated; make sure it only covers names or terms.";

    private static readonly PhraseRule[] Rules =
    {
        new(
            Severity: PromptLintSeverity.Error,
            Message: "This instruction can leave content untranslated.",
            Needles: new[]
            {
                "원문 유지",
                "미번역",
                "번역하지 말",
                "do not translate",
                "leave untranslated",
                "leave as-is",
                "keep original text",
                "return source text unchanged",
            },
            ScopedIsWarning: true
        ),
        new(
            Severity: PromptLintSeverity.Error,
            Message: "This instruction can conflict with required output format.",
            Needles: new[]
            {
                "output markdown",
                "return markdown",
                "markdown으로 출력",
                "json 말고",
                "do not output json",
                "return yaml",
                "yaml으로 출력",
                "include code fence",
                "코드 펜스",
                "코드펜스",
                "output xml",
            }
        ),
        new(
            Severity: PromptLintSeverity.Error,
            Message: "This instruction can add commentary/explanations to output.",
            Needles: new[]
            {
                "include explanation",
                "add explanation",
                "with commentary",
                "설명 포함",
                "설명을 덧붙",
                "해설을 추가",
            }
        ),
        new(
            Severity: PromptLintSeverity.Warning,
            Message: "This instruction may bias the model toward source-keep behavior.",
            Needles: new[]
            {
                "if not sure, keep original",
                "if unsure keep original",
                "확신이 없으면 원문",
                "불확실하면 원문",
            }
        ),
    };

    public static IReadOnlyList<PromptLintIssue> Analyze(
        bool useCustomPrompt,
        string? customPrompt,
        bool enableProjectContext,
        string? projectContext
    )
    {
        var issues = new List<PromptLintIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (useCustomPrompt)
        {
            AnalyzeSource("custom prompt", customPrompt, issues, seen);
        }

        if (enableProjectContext)
        {
            AnalyzeSource("project context", projectContext, issues, seen);
        }

        return issues;
    }

    public static bool HasBlockingIssues(IReadOnlyList<PromptLintIssue> issues)
    {
        for (var i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == PromptLintSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeSource(
        string source,
        string? text,
        List<PromptLintIssue> issues,
        HashSet<string> seen
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Each instruction is read on its own, so a negation in one sentence does not excuse another.
        foreach (var clause in ClauseSeparatorRegex.Split(text))
        {
            for (var i = 0; i < Rules.Length; i++)
            {
                var rule = Rules[i];
                for (var j = 0; j < rule.Needles.Length; j++)
                {
                    var needle = rule.Needles[j];
                    var idx = clause.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                    {
                        continue;
                    }

                    // "미번역 문장을 남기지 말 것" and "Never output markdown" say what the rules say; they blocked
                    // the run because the phrase matched. A phrase that is itself a prohibition ("번역하지 말")
                    // is not reversed by it.
                    if (!IsProhibition(needle) && IsNegated(clause, idx, needle.Length))
                    {
                        continue;
                    }

                    var severity = rule.Severity;
                    var message = rule.Message;
                    if (rule.ScopedIsWarning && IsScoped(clause, idx, needle))
                    {
                        severity = PromptLintSeverity.Warning;
                        message = ScopedKeepMessage;
                    }

                    if (!seen.Add(source + "|" + needle + "|" + severity))
                    {
                        continue;
                    }

                    issues.Add(new PromptLintIssue(severity, source, message, needle));
                }
            }
        }
    }

    private static readonly Regex ClauseSeparatorRegex = new(
        pattern: @"[\r\n.!?;。]+",
        options: RegexOptions.CultureInvariant
    );

    // Korean puts the negation after the phrase: "남기지 말 것", "출력하지 마세요", "덧붙이지 않는다", "금지".
    private static readonly string[] KoreanNegationsAfter =
    {
        "지 말", "지말", "지 마", "지마", "말 것", "말것", "말라", "금지", "않", "없도록", "없게", "없이", "안 된", "안된", "안 됨", "안됨",
    };

    // English puts it before: "Never output markdown", "Do not include explanation".
    private static readonly Regex EnglishNegationBeforeRegex = new(
        pattern: @"\b(?:do not|don't|dont|never|avoid|must not|mustn't|should not|shouldn't|without)\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    // Korean topic/object particles that end the name of what is kept: "제품명은 번역하지 말 것", "고유명사를 원문 유지".
    private static readonly char[] ScopeParticles = { '은', '는', '을', '를', '도', '만' };

    // Words that make the instruction cover everything again: "원문은 번역하지 말 것", "Do not translate anything".
    private static readonly string[] KoreanBlanketWords = { "원문", "문장", "텍스트", "내용", "전체", "전부", "모든", "모두", "나머지" };

    private static readonly HashSet<string> EnglishBlanketWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anything", "everything", "it", "this", "that", "text", "texts", "source", "content", "input", "any", "all",
        "strings", "lines", "sentences",
    };

    private static bool IsProhibition(string needle)
        => needle.StartsWith("do not", StringComparison.OrdinalIgnoreCase) || needle.Contains('말');

    // The negation must belong to the phrase, not to the next verb: in "원문 유지하고 번역하지 않는다" or
    // "Keep tokens, do not reorder them, and output markdown" the phrase itself is meant.
    private static readonly string[] KoreanPredicateEnds = { ",", "고 ", "며 ", "면서", "지만", "는데" };

    private static readonly Regex EnglishPredicateStartRegex = new(
        pattern: @".*(?:,|\band\b|\bbut\b|\bthen\b)",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

    private static bool IsNegated(string clause, int idx, int length)
    {
        var after = clause[(idx + length)..];
        foreach (var end in KoreanPredicateEnds)
        {
            var cut = after.IndexOf(end, StringComparison.Ordinal);
            if (cut >= 0)
            {
                after = after[..cut];
            }
        }

        var before = clause[..idx];
        var start = EnglishPredicateStartRegex.Match(before);
        if (start.Success)
        {
            before = before[(start.Index + start.Length)..];
        }

        return KoreanNegationsAfter.Any(n => after.Contains(n, StringComparison.Ordinal))
               || EnglishNegationBeforeRegex.IsMatch(before);
    }

    private static bool IsScoped(string clause, int idx, string needle)
    {
        if (needle.Any(c => c >= '가' && c <= '힣'))
        {
            var before = clause[..idx].TrimEnd();
            var lastSpace = before.LastIndexOfAny(new[] { ' ', '\t' });
            var word = before[(lastSpace + 1)..];
            return word.Length >= 2
                   && ScopeParticles.Contains(word[^1])
                   && !KoreanBlanketWords.Any(b => word.StartsWith(b, StringComparison.Ordinal));
        }

        if (!needle.Equals("do not translate", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var objectWords = clause[(idx + needle.Length)..]
            .Split(new[] { ' ', '\t', ',', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(w => w is "the" or "The" or "a" or "an");
        var first = objectWords.FirstOrDefault();
        return first != null && !EnglishBlanketWords.Contains(first);
    }
}
