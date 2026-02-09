using System;
using System.Collections.Generic;

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
    private sealed record PhraseRule(
        PromptLintSeverity Severity,
        string Message,
        string[] Needles
    );

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
            }
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

        var content = text.Trim();
        for (var i = 0; i < Rules.Length; i++)
        {
            var rule = Rules[i];
            for (var j = 0; j < rule.Needles.Length; j++)
            {
                var needle = rule.Needles[j];
                if (content.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var key = source + "|" + needle;
                if (!seen.Add(key))
                {
                    continue;
                }

                issues.Add(new PromptLintIssue(rule.Severity, source, rule.Message, needle));
            }
        }
    }
}
