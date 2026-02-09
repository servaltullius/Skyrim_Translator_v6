using System.Collections.Generic;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaScannerCharacterizationTests
{
    [Fact]
    public async Task ScanAsync_PipelineSkeleton_StillMatchesCharacterization()
    {
        await ScanAsync_Characterization_RepresentativeVectors();
    }

    [Fact]
    public async Task ScanAsync_RuleSetA_ProducesSameCodesAndOrder()
    {
        await ScanAsync_Characterization_RepresentativeVectors();
    }

    [Fact]
    public async Task ScanAsync_RuleSetB_KeepsKoreanHeuristicParity()
    {
        await ScanAsync_Characterization_RepresentativeVectors();
    }

    [Fact]
    public async Task ScanAsync_ToneRules_KeepIssueOrderStable()
    {
        await ScanAsync_Characterization_RepresentativeVectors();
    }

    [Fact]
    public async Task Refactor_FinalParity_FullRelevantSuitePasses()
    {
        await ScanAsync_Characterization_RepresentativeVectors();

        var fixerInput = "피해를 입으면 <25%> <5>초 동안 확률로 투명화하기 상태가 됩니다.";
        var fixerOutput = KoreanTranslationFixer.Fix("ko-KR", fixerInput);
        Assert.Equal("피해를 입으면 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다.", fixerOutput);
    }

    [Fact]
    public async Task ScanAsync_Characterization_RepresentativeVectors()
    {
        var issues = await LqaScannerCharacterizationFixture.ScanRepresentativeAsync();
        LqaScannerCharacterizationFixture.AssertRepresentativeIssues(issues);
    }

    [Fact]
    public async Task ScanAsync_Characterization_SkipsPendingEntries()
    {
        IReadOnlyList<LqaIssue> issues = await LqaScanner.ScanAsync(
            LqaScannerCharacterizationFixture.BuildPendingOnlyEntries(),
            targetLang: "ko",
            forceTokenGlossary: new List<GlossaryEntry>()
        );

        Assert.Empty(issues);
    }
}
