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
    [InlineData("Do not translate product names such as SkyUI or MCM.", "Do not translate")]
    [InlineData("대사는 번역하지 마세요.", "번역하지 마")]
    [InlineData("Thu'um: 영문 유지", "영문 유지")]
    [InlineData("'SkyUI'는 번역하지 말 것.", "번역하지 말")]
    [InlineData("Leave names untranslated.", "Leave names untranslated")]
    // A term line the context generator writes: the aside keeps that term.
    [InlineData("- Ashe Impressionist: Ashe Impressionist (모드명 및 고유 명칭으로 원문 유지)", "원문 유지")]
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
    // The forms the check missed, and a kind of line made everything by "모든" before it.
    [InlineData("번역하지 마세요.")]
    [InlineData("번역 금지.")]
    [InlineData("원문 그대로 둘 것.")]
    [InlineData("영어 그대로 남길 것.")]
    [InlineData("미번역 상태로 둘 것.")]
    [InlineData("Don't translate anything.")]
    [InlineData("Never translate the text.")]
    [InlineData("Leave everything in English.")]
    [InlineData("Keep the original English text.")]
    [InlineData("모든 대사는 번역하지 말 것.")]
    [InlineData("영어는 번역하지 말 것.")]
    [InlineData("규칙: 번역하지 말 것.")]
    [InlineData("Rule: 번역하지 말 것.")]
    [InlineData("아무것도 번역하지 마세요.")]
    [InlineData("하나도 번역하지 말 것.")]
    [InlineData("Do not translate at all.")]
    [InlineData("Do not translate, keep the original.")]
    [InlineData("Don't translate English text.")]
    [InlineData("원문 그대로 남깁니다.")]
    [InlineData("번역하지 않음.")]
    [InlineData("번역은 하지 마세요.")]
    [InlineData("번역할 필요 없음.")]
    [InlineData("원문을 유지할 것.")]
    [InlineData("미번역 그대로 둘 것.")]
    [InlineData("Keep original texts.")]
    // Quoted for emphasis, an instruction is still one.
    [InlineData("\"번역하지 말 것\"")]
    [InlineData("\"모든 텍스트\"는 번역하지 말 것.")]
    [InlineData("\"Do not translate anything.\"")]
    // How to translate names no object for the next instruction; a setting is no term line.
    [InlineData("함부로 번역하지 말고 원문 그대로 둘 것.")]
    [InlineData("따로 번역하지 말고 원문 그대로 출력할 것.")]
    [InlineData("출력 형식 = 원문 그대로 출력.")]
    public void Analyze_WhenTheInstructionIsBlanket_StillBlocks(string prompt)
    {
        Assert.True(PromptConflictLint.HasBlockingIssues(
            PromptConflictLint.Analyze(useCustomPrompt: true, customPrompt: prompt, enableProjectContext: false, projectContext: null)));
    }

    // How to translate, or what the translator keeps anyway: nothing is left in English by it.
    [Theory]
    [InlineData("고유명사를 잘못 번역하지 않도록 주의.")]
    [InlineData("재번역하지 말 것.")]
    [InlineData("과잉 번역 금지.")]
    [InlineData("Do not translate literally.")]
    [InlineData("태그와 토큰은 원문 그대로 둘 것.")]
    [InlineData("Do not translate tokens or tags.")]
    [InlineData("번역 금지어 목록: SkyUI, MCM.")]
    [InlineData("번역하지 않은 줄이 없어야 한다.")]
    [InlineData("원문을 그대로 옮기지 말고 자연스럽게 번역할 것.")]
    [InlineData("영어 두 줄을 합친다.")]
    // Asked to translate the descriptions too, not to add one.
    [InlineData("아이템 이름과 설명을 포함해 모두 한국어로 번역하세요.")]
    [InlineData("무기 설명도 포함해서 번역해 주세요.")]
    [InlineData("번역 시 아이템 설명을 포함한 모든 줄을 자연스럽게.")]
    // What was done, not asked.
    [InlineData("원작자가 영어 그대로 남긴 책이 있다.")]
    [InlineData("엔진 한계로 원문 그대로 출력됩니다.")]
    [InlineData("미번역 처리됨.")]
    [InlineData("제작자가 번역 금지를 풀어 주었다.")]
    [InlineData("원문 그대로 남길 필요 없음.")]
    [InlineData("한국어판에 영어 그대로 남겨진 메뉴가 있다.")]
    [InlineData("원작자가 영어 그대로 남겨 둔 책이 있다.")]
    [InlineData("한글패치가 원문 그대로 유지한 이름이 많다.")]
    [InlineData("원문 그대로 유지하는 줄은 없다.")]
    [InlineData("에이전트가 번역하지 않기로 한 줄이 3개 있다.")]
    [InlineData("기존 한국어판으로 충분해 추가 번역 불필요.")]
    // The second verb's object is the first one's, and the translator keeps tags anyway.
    [InlineData("Do not translate or modify tags, tokens, or placeholders.")]
    public void Analyze_WhenTheInstructionKeepsNoText_ReturnsNoIssues(string prompt)
    {
        Assert.Empty(PromptConflictLint.Analyze(useCustomPrompt: true, customPrompt: prompt, enableProjectContext: false, projectContext: null));
    }

    // A second instruction that names nothing keeps what the first named; a word that qualifies the object (영문 약어,
    // 영어 대사) or stresses the instruction (절대, 꼭) does not make it everything. These blocked the run.
    [Theory]
    [InlineData("고유명사는 번역하지 말고 원문 그대로 남길 것.")]
    [InlineData("SkyUI, MCM은 번역하지 말고 원문 그대로 둘 것.")]
    [InlineData("이름은 번역하지 않고 영문 그대로 둔다.")]
    [InlineData("고유명사는 음역하지 말고 원문 그대로 둘 것.")]
    [InlineData("Don't translate 'Dawnguard', keep it in English.")]
    [InlineData("영문 약어는 번역하지 말 것.")]
    [InlineData("영어 대사는 원문 유지.")]
    [InlineData("고유명사는 절대 번역하지 마세요.")]
    [InlineData("아이템 이름은 꼭 영문 그대로 둘 것.")]
    [InlineData("코드명은 번역하지 말 것.")]
    [InlineData("대사는번역하지 말 것.")]
    [InlineData("- 탈모르 대사관 → 원문 유지")]
    // "모든" before a kind of name, "아무리" (not 아무것), a quantifier after the object, the first instruction's
    // 원문 read as the second one's, and the second verb of "translate or romanize".
    [InlineData("모든 고유명사는 번역하지 말 것.")]
    [InlineData("모든 NPC 이름은 원문 유지.")]
    [InlineData("아무리 길어도 이름은 번역하지 마세요.")]
    [InlineData("이름은 모두 원문 유지.")]
    [InlineData("인명은 원문 유지 / 지명은 원문 유지")]
    [InlineData("Do not translate or romanize names.")]
    public void Analyze_WhenEveryInstructionNamesWhatItKeeps_OnlyWarns(string prompt)
    {
        var issues = PromptConflictLint.Analyze(useCustomPrompt: true, customPrompt: prompt, enableProjectContext: false, projectContext: null);

        Assert.NotEmpty(issues);
        Assert.False(PromptConflictLint.HasBlockingIssues(issues), string.Join(" | ", issues.Select(i => i.Severity + " " + i.MatchedText)));
    }

    // The check runs as the user types: a long line without a full stop read all of itself again for each match.
    [Fact]
    public void Analyze_ALongLineWithoutAFullStop_IsReadQuickly()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        PromptConflictLint.Analyze(useCustomPrompt: true, customPrompt: string.Concat(Enumerable.Repeat("Do not translate names, ", 2000)),
            enableProjectContext: true, projectContext: string.Concat(Enumerable.Repeat("아이템 이름 원문 유지 / ", 2000)));

        Assert.True(stopwatch.ElapsedMilliseconds < 2000, stopwatch.ElapsedMilliseconds + " ms");
    }

    // A project context describes the mod: these were read as instructions and blocked the run.
    [Theory]
    [InlineData("이 모드는 무기 설명 포함 300개 아이템을 추가한다.")]
    [InlineData("퀘스트 일지에 해설을 추가하는 모드다.")]
    [InlineData("미번역 상태로 배포된 한국어판이 있다.")]
    [InlineData("미번역 줄은 없어야 한다.")]
    [InlineData("원문 그대로 출력되는 줄이 있다.")]
    [InlineData("#120 원문: 'Do not translate this letter, it is cursed.'")]
    [InlineData("'번역하지 말 것'이라는 표지판이 있다.")]
    [InlineData("한국어판은 번역된 책마다 해설을 추가합니다.")]
    public void Analyze_WhenTheProjectContextStatesAFact_ReturnsNoIssues(string context)
    {
        Assert.Empty(PromptConflictLint.Analyze(useCustomPrompt: false, customPrompt: null, enableProjectContext: true, projectContext: context));
    }

    [Theory]
    [InlineData("번역 결과에 설명 포함.")]
    [InlineData("번역문 뒤에 설명을 포함할 것.")]
    [InlineData("각 줄마다 주석을 달 것.")]
    public void Analyze_WhenTheProjectContextAsksForExplanationInTheOutput_Blocks(string context)
    {
        var issues = PromptConflictLint.Analyze(useCustomPrompt: false, customPrompt: null, enableProjectContext: true, projectContext: context);

        Assert.True(PromptConflictLint.HasBlockingIssues(issues));
    }

    [Fact]
    public void Analyze_ShowsTheMatchedWordsAndTheInstructionAsWritten()
    {
        var issue = Assert.Single(PromptConflictLint.Analyze(
            useCustomPrompt: false, customPrompt: null, enableProjectContext: true,
            projectContext: "The mod adds 'Old Tales'. 원문은 번역하지 마세요!"));

        Assert.Equal("번역하지 마", issue.MatchedText);
        Assert.Equal("원문은 번역하지 마세요", issue.Clause);
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
