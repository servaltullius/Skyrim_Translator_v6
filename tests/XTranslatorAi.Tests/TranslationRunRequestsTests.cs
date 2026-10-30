using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

/// <summary>
/// The run request the GUI and agent mode both build (TranslationRunRequests). Built in two places, a setting with a
/// default left out of one went unnoticed: every setting here differs from its default, so one the request does not
/// take from the settings fails.
/// </summary>
public sealed class TranslationRunRequestsTests
{
    private static readonly AppSettings Settings = new(
        EnableBookFullModelOverride: true, EnableBookBodyModelOverride: true, EnablePromptCache: false, EnableQualityEscalation: true,
        QualityEscalationModel: "escalation-model", EnableRiskyCandidateRerank: false, RiskyCandidateCount: 5, BatchSize: 7,
        MaxCharsPerBatch: 9000, MaxParallelRequests: 4, EnableRepairPass: false, SemanticRepairMode: PlaceholderSemanticRepairMode.Strict,
        KeepSkyrimTagsRaw: false, EnableDialogueContextWindow: false, EnableSessionTermMemory: false, UseRecStyleHints: false,
        EnableTemplateFixer: true, EnableAdaptiveOutputBudget: true, EnableBookContext: true, MaxRetryGenerations: 3, MaxTotalGenerations: 50);

    private static readonly CancellationTokenSource Stop = new();

    private static TranslationRunnerService.Request Create(AppSettings settings)
        => TranslationRunRequests.Create(
            new TranslationRunRequests.Ports(null!, null!, null!, null!, null!, Stop.Token),
            settings,
            new TranslationRunRequests.Model("primary", _ => true, model => model + "-book", _ => 1234),
            new long[] { 1, 2 }, _ => "WEAP:FULL", "system", BethesdaFranchise.Fallout, "english", "korean", gameStrings: null,
            nameMemoryRows: new HashSet<long> { 2 });

    [Fact]
    public void TheRequest_TakesEverySettingFromTheSettings()
    {
        var request = Create(Settings);

        Assert.Equal(
            (true, true, false, true, "escalation-model", false, 5, 7, 9000, 4),
            (request.EnableBookFullModelOverride, request.EnableBookBodyModelOverride, request.EnablePromptCache, request.EnableQualityEscalation,
                request.QualityEscalationModelName, request.EnableRiskyCandidateRerank, request.RiskyCandidateCount, request.BatchSize,
                request.MaxChars, request.Parallel));
        Assert.Equal(
            (false, PlaceholderSemanticRepairMode.Strict, false, false, false, false, true, true, true, 3, 50),
            (request.EnableRepairPass, request.SemanticRepairMode, request.KeepSkyrimTagsRaw, request.EnableDialogueContextWindow,
                request.EnableSessionTermMemory, request.UseRecStyleHints, request.EnableTemplateFixer, request.EnableAdaptiveOutputBudget,
                request.EnableBookContext, request.MaxRetryGenerations, request.MaxTotalGenerations));
        Assert.Equal(("primary", "x-book", 1234, true, "system", BethesdaFranchise.Fallout, "english", "korean"),
            (request.PrimaryModel, request.GetEffectiveBookFullModelName("x"), request.ComputeMaxOutputTokens("m"), request.IsBookFullRec("BOOK:FULL"),
                request.SystemPrompt, request.Franchise, request.SourceLang, request.TargetLang));
        Assert.Equal(new long[] { 1, 2 }, request.Ids);
        Assert.Equal(new HashSet<long> { 2 }, request.NameMemoryRows);
        Assert.Equal(Stop.Token, request.CancellationToken);
    }

    /// <summary>One setting changed from its default changes its own field: a setting wired to another's field passes the test above.</summary>
    [Theory]
    [MemberData(nameof(OneSetting))]
    public void EachSetting_ReachesItsOwnField(string name, Func<AppSettings, AppSettings> change, Func<TranslationRunnerService.Request, object?> field)
    {
        var defaults = Create(new AppSettings());
        var changed = Create(change(new AppSettings()));

        Assert.NotEqual(field(defaults), field(changed));
        Assert.True(name.Length > 0);
    }

    public static IEnumerable<object[]> OneSetting()
    {
        object[] Row(string name, Func<AppSettings, AppSettings> change, Func<TranslationRunnerService.Request, object?> field) => new object[] { name, change, field };
        yield return Row("book full model", s => s with { EnableBookFullModelOverride = !s.EnableBookFullModelOverride }, r => r.EnableBookFullModelOverride);
        yield return Row("book body model", s => s with { EnableBookBodyModelOverride = !s.EnableBookBodyModelOverride }, r => r.EnableBookBodyModelOverride);
        yield return Row("prompt cache", s => s with { EnablePromptCache = !s.EnablePromptCache }, r => r.EnablePromptCache);
        yield return Row("quality escalation", s => s with { EnableQualityEscalation = !s.EnableQualityEscalation }, r => r.EnableQualityEscalation);
        yield return Row("escalation model", s => s with { QualityEscalationModel = "m" }, r => r.QualityEscalationModelName);
        yield return Row("rerank", s => s with { EnableRiskyCandidateRerank = !s.EnableRiskyCandidateRerank }, r => r.EnableRiskyCandidateRerank);
        yield return Row("candidates", s => s with { RiskyCandidateCount = s.RiskyCandidateCount + 1 }, r => r.RiskyCandidateCount);
        yield return Row("batch", s => s with { BatchSize = s.BatchSize + 1 }, r => r.BatchSize);
        yield return Row("chars", s => s with { MaxCharsPerBatch = s.MaxCharsPerBatch + 1 }, r => r.MaxChars);
        yield return Row("parallel", s => s with { MaxParallelRequests = s.MaxParallelRequests + 1 }, r => r.Parallel);
        yield return Row("repair pass", s => s with { EnableRepairPass = !s.EnableRepairPass }, r => r.EnableRepairPass);
        yield return Row("semantic repair", s => s with { SemanticRepairMode = PlaceholderSemanticRepairMode.Strict }, r => r.SemanticRepairMode);
        yield return Row("skyrim tags", s => s with { KeepSkyrimTagsRaw = !s.KeepSkyrimTagsRaw }, r => r.KeepSkyrimTagsRaw);
        yield return Row("dialogue window", s => s with { EnableDialogueContextWindow = !s.EnableDialogueContextWindow }, r => r.EnableDialogueContextWindow);
        yield return Row("session terms", s => s with { EnableSessionTermMemory = !s.EnableSessionTermMemory }, r => r.EnableSessionTermMemory);
        yield return Row("rec style", s => s with { UseRecStyleHints = !s.UseRecStyleHints }, r => r.UseRecStyleHints);
        yield return Row("template fixer", s => s with { EnableTemplateFixer = !s.EnableTemplateFixer }, r => r.EnableTemplateFixer);
        yield return Row("output budget", s => s with { EnableAdaptiveOutputBudget = !s.EnableAdaptiveOutputBudget }, r => r.EnableAdaptiveOutputBudget);
        yield return Row("book context", s => s with { EnableBookContext = !s.EnableBookContext }, r => r.EnableBookContext);
        yield return Row("retries", s => s with { MaxRetryGenerations = s.MaxRetryGenerations + 1 }, r => r.MaxRetryGenerations);
        yield return Row("generations", s => s with { MaxTotalGenerations = s.MaxTotalGenerations + 1 }, r => r.MaxTotalGenerations);
    }

    /// <summary>What agent mode fixes so a recording and its replay make the same requests; the rest is the user's.</summary>
    [Fact]
    public void AgentMode_FixesWhatWouldMakeAReplayDiffer_AndKeepsTheRest()
    {
        // What agent mode turns off is on in the settings it gets (the defaults are on for three of them).
        var request = Create(TranslationRunRequests.ForAgent(
            Settings with { EnablePromptCache = true, EnableRiskyCandidateRerank = true, EnableSessionTermMemory = true },
            batchSize: 30, maxChars: 20000));

        Assert.Equal(
            (false, false, false, false, null, false, 1, 30, 20000, 1, false),
            (request.EnableBookFullModelOverride, request.EnableBookBodyModelOverride, request.EnablePromptCache, request.EnableQualityEscalation,
                request.QualityEscalationModelName, request.EnableRiskyCandidateRerank, request.RiskyCandidateCount, request.BatchSize,
                request.MaxChars, request.Parallel, request.EnableSessionTermMemory));
        Assert.Equal(
            (false, PlaceholderSemanticRepairMode.Strict, false, false, false, true, true, true, 3, 50),
            (request.EnableRepairPass, request.SemanticRepairMode, request.KeepSkyrimTagsRaw, request.EnableDialogueContextWindow,
                request.UseRecStyleHints, request.EnableTemplateFixer, request.EnableAdaptiveOutputBudget, request.EnableBookContext,
                request.MaxRetryGenerations, request.MaxTotalGenerations));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("모드 배경", true)]
    public void TheSystemPrompt_TakesTheCustomPromptFromTheSettings_AndAContextThatIsThere(string? context, bool hasContext)
    {
        var builder = new SystemPromptBuilder();
        var settings = new AppSettings(UseCustomPrompt: true, CustomPromptText: "사용자 지시");

        var prompt = builder.Build("기본", settings, context);

        Assert.Equal(builder.Build("기본", true, "사용자 지시", hasContext, context), prompt);
        Assert.Contains("사용자 지시", prompt);
        Assert.Equal(hasContext, prompt.Contains("모드 배경"));
    }
}
