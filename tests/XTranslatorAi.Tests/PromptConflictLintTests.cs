using System;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class PromptConflictLintTests
{
    [Fact]
    public void Analyze_WhenCustomPromptContainsUntranslatedDirective_ReturnsBlockingIssue()
    {
        var issues = PromptConflictLint.Analyze(
            useCustomPrompt: true,
            customPrompt: "확신이 없으면 원문 유지",
            enableProjectContext: false,
            projectContext: null
        );

        Assert.Contains(
            issues,
            i => i.Severity == PromptLintSeverity.Error
                 && i.Source == "custom prompt"
                 && i.MatchedText.Contains("원문 유지", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Analyze_WhenProjectContextContainsOutputFormatConflict_ReturnsBlockingIssue()
    {
        var issues = PromptConflictLint.Analyze(
            useCustomPrompt: false,
            customPrompt: null,
            enableProjectContext: true,
            projectContext: "Output markdown with explanation."
        );

        Assert.Contains(
            issues,
            i => i.Severity == PromptLintSeverity.Error
                 && i.Source == "project context"
                 && i.MatchedText.Contains("output markdown", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void Analyze_WhenPromptTextIsBenign_ReturnsNoIssues()
    {
        var issues = PromptConflictLint.Analyze(
            useCustomPrompt: true,
            customPrompt: "Use glossary first. Keep tokens exactly. Output valid JSON only.",
            enableProjectContext: true,
            projectContext: "용어집/스타일 일관성을 유지하고 숫자 템플릿을 보존한다."
        );

        Assert.Empty(issues);
    }
}
