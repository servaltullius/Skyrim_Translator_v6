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

    // These say what the built-in rules say, but the phrase matched and translation could not start.
    [Theory]
    [InlineData("미번역 문장을 남기지 말 것.")]
    [InlineData("미번역 문장이 생기지 않도록 하고, 모든 문장을 번역한다.")]
    [InlineData("설명을 덧붙이지 말고 번역문만 출력할 것")]
    [InlineData("markdown으로 출력하지 마세요. 코드 펜스 금지.")]
    [InlineData("Never leave untranslated text. Do not output markdown or include explanation.")]
    public void Analyze_WhenTheInstructionNegatesThePhrase_ReturnsNoIssues(string prompt)
    {
        Assert.Empty(PromptConflictLint.Analyze(useCustomPrompt: true, customPrompt: prompt, enableProjectContext: false, projectContext: null));
    }

    [Theory]
    [InlineData("SkyUI 같은 제품명은 번역하지 말 것", "번역하지 말")]
    [InlineData("모드 이름은 원문 유지", "원문 유지")]
    [InlineData("Do not translate product names such as SkyUI or MCM.", "do not translate")]
    public void Analyze_WhenTheInstructionNamesWhatItKeeps_WarnsWithoutBlocking(string prompt, string matched)
    {
        var issues = PromptConflictLint.Analyze(useCustomPrompt: true, customPrompt: prompt, enableProjectContext: false, projectContext: null);

        var issue = Assert.Single(issues);
        Assert.Equal(PromptLintSeverity.Warning, issue.Severity);
        Assert.Equal(matched, issue.MatchedText);
        Assert.False(PromptConflictLint.HasBlockingIssues(issues));
    }

    [Theory]
    [InlineData("번역하지 말 것.")]
    [InlineData("원문은 번역하지 말 것")]
    [InlineData("Do not translate anything.")]
    [InlineData("원문 유지하고 번역하지 않는다.")]
    [InlineData("Keep tokens, do not reorder them, and output markdown.")]
    public void Analyze_WhenTheInstructionIsBlanket_StillBlocks(string prompt)
    {
        Assert.True(PromptConflictLint.HasBlockingIssues(
            PromptConflictLint.Analyze(useCustomPrompt: true, customPrompt: prompt, enableProjectContext: false, projectContext: null)));
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
