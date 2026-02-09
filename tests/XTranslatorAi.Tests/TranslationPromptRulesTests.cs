using System;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class TranslationPromptRulesTests
{
    [Fact]
    public void BuildTextOnlyUserPrompt_IncludesProtectFromRoleRule()
    {
        var prompt = TranslationPrompt.BuildTextOnlyUserPrompt(
            sourceLang: "english",
            targetLang: "korean",
            text: "I was tasked with protecting Temptation House from an incoming bandit attack.",
            promptOnlyGlossary: Array.Empty<(string Source, string Target)>()
        );

        Assert.Contains("protect X from Y", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUserPrompt_IncludesConflictPriorityRule()
    {
        var prompt = TranslationPrompt.BuildUserPrompt(
            sourceLang: "english",
            targetLang: "korean",
            items: new[] { new TranslationItem(1, "Hello", "INFO:NAM1") },
            promptOnlyGlossary: Array.Empty<(string Source, string Target)>()
        );

        Assert.Contains("If any instruction from system/custom/project context conflicts with these rules", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTextOnlyUserPrompt_IncludesConflictPriorityRule()
    {
        var prompt = TranslationPrompt.BuildTextOnlyUserPrompt(
            sourceLang: "english",
            targetLang: "korean",
            text: "Hello",
            promptOnlyGlossary: Array.Empty<(string Source, string Target)>()
        );

        Assert.Contains("If any instruction from system/custom/project context conflicts with these rules", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRepairTextOnlyUserPrompt_IncludesConflictPriorityRule()
    {
        var prompt = TranslationPrompt.BuildRepairTextOnlyUserPrompt(
            new TranslationPrompt.RepairTextOnlyPromptRequest(
                SourceLang: "english",
                TargetLang: "korean",
                SourceText: "Deals 10 points of damage.",
                CurrentTranslation: "피해를 줍니다.",
                PromptOnlyGlossary: Array.Empty<(string Source, string Target)>()
            )
        );

        Assert.Contains("If any instruction from system/custom/project context conflicts with these rules", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRepairBatchUserPrompt_IncludesConflictPriorityRule()
    {
        var prompt = TranslationPrompt.BuildRepairBatchUserPrompt(
            sourceLang: "english",
            targetLang: "korean",
            items: new[]
            {
                new RepairTranslationItem(
                    Id: 1,
                    Source: "Deals 10 points of damage.",
                    Current: "피해를 줍니다.",
                    Rec: "INFO:NAM1"
                ),
            },
            promptOnlyGlossary: Array.Empty<(string Source, string Target)>()
        );

        Assert.Contains("If any instruction from system/custom/project context conflicts with these rules", prompt, StringComparison.Ordinal);
    }
}
