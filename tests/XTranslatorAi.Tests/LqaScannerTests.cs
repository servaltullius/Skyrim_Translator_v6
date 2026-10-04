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

    // --- BOOK structure rules ---

    [Fact]
    public async Task Scan_Book_PagebreakCountMismatch_ReturnsBookPagebreakMismatch()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "BookTest",
                Rec: "BOOK:00000001",
                Status: StringEntryStatus.Done,
                SourceText: "Page one.[pagebreak]Page two.[pagebreak]Page three.",
                DestText: "1페이지.[pagebreak]2페이지."
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.Contains(issues, i => i.Code == "book_pagebreak_mismatch" && i.Id == 1);
    }

    // The masker protects LotD's "[page break]" and "<page break>" too, so the book check counts them.
    [Theory]
    [InlineData("[page break]")]
    [InlineData("<page break>")]
    public async Task Scan_Book_CountsEveryPageBreakForm(string pageBreak)
    {
        var entries = new List<LqaScanEntry>
        {
            new(1, 1, "BookTest", "BOOK:00000001", StringEntryStatus.Done, $"Page one.{pageBreak}Page two.", "1페이지. 2페이지."),
            new(2, 2, "BookTest2", "BOOK:00000002", StringEntryStatus.Done, $"Page one.{pageBreak}Page two.", $"1페이지.{pageBreak}2페이지."),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());

        Assert.Contains(issues, i => i.Code == "book_pagebreak_mismatch" && i.Id == 1);
        Assert.DoesNotContain(issues, i => i.Code == "book_pagebreak_mismatch" && i.Id == 2);
    }

    [Fact]
    public async Task Scan_Book_PagebreakCountMatch_NoPagebreakIssue()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "BookTest",
                Rec: "BOOK:00000001",
                Status: StringEntryStatus.Done,
                SourceText: "Page one.[pagebreak]Page two.",
                DestText: "1페이지.[pagebreak]2페이지."
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.DoesNotContain(issues, i => i.Code == "book_pagebreak_mismatch");
    }

    [Fact]
    public async Task Scan_Book_HtmlTagMismatch_ReturnsBookHtmlTagMismatch()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "BookTest",
                Rec: "BOOK:00000001",
                Status: StringEntryStatus.Done,
                SourceText: "<p>Hello</p><br>World",
                DestText: "<p>안녕</p>세계"
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.Contains(issues, i => i.Code == "book_html_tag_mismatch" && i.Id == 1);
    }

    [Fact]
    public async Task Scan_Book_LengthRatioExceeded_ReturnsBookLengthRatio()
    {
        var source = "Short book text.";
        var dest = new string('가', 200); // Extremely long translation

        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "BookTest",
                Rec: "BOOK:00000001",
                Status: StringEntryStatus.Done,
                SourceText: source,
                DestText: dest
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.Contains(issues, i => i.Code == "book_length_ratio" && i.Id == 1);
    }

    [Fact]
    public async Task Scan_NonBook_NoBookRules()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "InfoTest",
                Rec: "INFO:00000001",
                Status: StringEntryStatus.Done,
                SourceText: "Page one.[pagebreak]Page two.",
                DestText: "1페이지."
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.DoesNotContain(issues, i => i.Code == "book_pagebreak_mismatch");
    }

    // --- NameVerbEndingRule tests ---

    [Fact]
    public async Task Scan_ActiFull_VerbEnding_ReturnsNameVerbEnding()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TestActivator",
                Rec: "ACTI:FULL",
                Status: StringEntryStatus.Done,
                SourceText: "Explorer's Excavation",
                DestText: "탐험이 발굴하기"
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.Contains(issues, i => i.Code == "name_verb_ending" && i.Id == 1);
    }

    [Fact]
    public async Task Scan_ActiFull_NounPhrase_NoIssue()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TestActivator",
                Rec: "ACTI:FULL",
                Status: StringEntryStatus.Done,
                SourceText: "Broken Door",
                DestText: "부서진 문"
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.DoesNotContain(issues, i => i.Code == "name_verb_ending");
    }

    [Fact]
    public async Task Scan_QustFull_VerbEnding_NoIssue()
    {
        var entries = new List<LqaScanEntry>
        {
            new(
                Id: 1,
                OrderIndex: 1,
                Edid: "TestQuest",
                Rec: "QUST:FULL",
                Status: StringEntryStatus.Done,
                SourceText: "Find the Lost Artifact",
                DestText: "잃어버린 유물 찾기"
            ),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());
        Assert.DoesNotContain(issues, i => i.Code == "name_verb_ending");
    }
}
