using System.Collections.Generic;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaScannerTests
{
    [Fact]
    public async Task ScanAsync_FlagsUntranslated_WhenSourceEqualsDest()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TEST",
                Rec: "INFO:00000001",
                Status: StringEntryStatus.Done,
                SourceText: "Saarthal Amulet",
                DestText: "Saarthal Amulet"
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.Contains(issues, i => i.Code == "untranslated" && i.Id == 1);
    }

    [Fact]
    public async Task ScanAsync_FlagsMissingGlossary_WhenForceTokenTargetNotPresent()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TEST",
                Rec: "INFO:00000001",
                Status: StringEntryStatus.Done,
                SourceText: "Saarthal Amulet",
                DestText: "사르달 아뮬렛"
            ),
        };

        var glossary = new List<GlossaryEntry>
        {
            new(
                Id: 1,
                Category: null,
                SourceTerm: "Saarthal",
                TargetTerm: "사아쌀",
                Enabled: true,
                MatchMode: GlossaryMatchMode.WordBoundary,
                ForceMode: GlossaryForceMode.ForceToken,
                Priority: 10,
                Note: null
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: glossary);
        Assert.Contains(issues, i => i.Code == "glossary_missing" && i.Id == 1);
    }

    [Fact]
    public async Task ScanAsync_FlagsParticleDouble_WhenConcatenatedParticlesRemain()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TEST",
                Rec: "MGEF",
                Status: StringEntryStatus.Done,
                SourceText: "Absorb Magicka.",
                DestText: "매지카을를 흡수합니다."
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.Contains(issues, i => i.Code == "particle_double" && i.Id == 1);
    }

    [Fact]
    public async Task ScanAsync_FlagsParticleMismatch_WhenHangulParticleLooksWrong()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TEST",
                Rec: "MGEF",
                Status: StringEntryStatus.Done,
                SourceText: "Absorb Magicka.",
                DestText: "매지카을 흡수합니다."
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.Contains(issues, i => i.Code == "particle_mismatch" && i.Id == 1);
    }

    [Fact]
    public async Task ScanAsync_FlagsTmFallback_WhenNoteIsPresent()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TEST",
                Rec: "MGEF",
                Status: StringEntryStatus.Done,
                SourceText: "Absorb <mag> points of Magicka.",
                DestText: "<mag> 흡수합니다."
            ),
        };

        var notes = new Dictionary<long, string>
        {
            [1] = "TM 폴백: Skyrim placeholder mismatch for id=1 tm post-edits: <mag> (expected 1, got 0).",
        };

        var issues = await LqaScanner.ScanAsync(
            entries,
            targetLang: "ko",
            forceTokenGlossary: new List<GlossaryEntry>(),
            tmFallbackNotes: notes
        );

        Assert.Contains(issues, i => i.Code == "tm_fallback" && i.Id == 1);
    }
}
