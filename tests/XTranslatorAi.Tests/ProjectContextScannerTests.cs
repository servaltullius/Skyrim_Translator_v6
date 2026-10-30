using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.ProjectContext;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectContextScannerTests
{
    [Fact]
    public async Task ScanAsync_BuildsStableTopRec_Terms_AndSamples()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            await db.UpsertGlossaryAsync(
                new GlossaryUpsertRequest(
                    Category: "Test",
                    SourceTerm: "Saarthal",
                    TargetTerm: "사아쌀",
                    Enabled: true,
                    Priority: 10,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Note: null
                ),
                CancellationToken.None
            );

            await db.BulkInsertStringsAsync(
                new[]
                {
                    CreateRow(1, "INFO:NAM1", "Saarthal Amulet"),
                    CreateRow(2, "INFO:NAM1", "Saarthal Amulet"),
                    CreateRow(3, "INFO:NAM1", "Saarthal Amulet"),
                    CreateRow(4, "MGEF", "Absorb <mag> points."),
                },
                CancellationToken.None
            );

            var scanner = new ProjectContextScanner();
            var report = await scanner.ScanAsync(
                db,
                globalDb: null,
                options: new ProjectContextScanOptions(
                    AddonName: "Dummy",
                    InputFile: "dummy.xml",
                    SourceLang: "english",
                    TargetLang: "korean"
                ),
                cancellationToken: CancellationToken.None
            );

            Assert.Equal(4, report.TotalStrings);

            Assert.True(report.TopRec.Count >= 2);
            Assert.Equal("INFO:NAM1", report.TopRec[0].Rec);
            Assert.Equal(3, report.TopRec[0].Count);

            Assert.Contains(report.TopTerms, t => t.Source == "Saarthal" && t.Count == 3 && t.Target == "사아쌀");

            Assert.Contains(report.Samples, s => s.Rec == "MGEF" && s.Text.Contains("<mag>", StringComparison.Ordinal));
            Assert.Single(report.Samples); // Existing special-sample reports keep their payload unchanged.
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task ScanAsync_WithoutSpecialSamples_ProvidesBoundedDistinctOriginalText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-context-plain-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var rows = new[]
            {
                CreateRow(0, "WEAP:FULL", "Iron Sword"),
                CreateRow(1, "WEAP:FULL", "Iron Sword"),
                CreateRow(2, "WEAP:FULL", "Iron Mace"),
                CreateRow(3, "WEAP:FULL", "Iron Dagger"),
                CreateRow(4, "BOOK:DESC", new string('x', 500)),
            }.Concat(Enumerable.Range(0, 12).Select(i => CreateRow(5 + i, "REC" + i, "Original row " + i))).ToArray();
            await db.BulkInsertStringsAsync(rows, CancellationToken.None);
            var first = (await db.GetStringsAsync(1, 0, CancellationToken.None)).Single();
            await db.UpdateStringTranslationAsync(first.Id, "번역문은 원문 샘플이 아닙니다", StringEntryStatus.Edited, null, CancellationToken.None);

            var report = await new ProjectContextScanner().ScanAsync(db, null,
                new ProjectContextScanOptions("Plain.esp", "Plain.esp", "english", "korean"), CancellationToken.None);

            Assert.Equal(8, report.Samples.Count);
            Assert.All(report.Samples.GroupBy(sample => sample.Rec), group => Assert.InRange(group.Count(), 1, 2));
            Assert.Single(report.Samples.Where(sample => sample.Text == "Iron Sword"));
            Assert.Contains(report.Samples, sample => sample.Text == "Iron Mace");
            Assert.DoesNotContain(report.Samples, sample => sample.Text == "Iron Dagger");
            Assert.DoesNotContain(report.Samples, sample => sample.Text.Contains("번역문", StringComparison.Ordinal));
            Assert.All(report.Samples, sample => Assert.InRange(sample.Text.Length, 1, 221));
            Assert.Equal(new string('x', 220) + "…", report.Samples.Single(sample => sample.Rec == "BOOK:DESC").Text);
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(path);
        }
    }

    // A row marked "원문 오류 수정" is translated against its English on purpose; its translation is not the term's.
    [Fact]
    public async Task ScanAsync_DoesNotOfferASourceFixRowsTranslationAsTheTerm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-context-fix-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            await db.BulkInsertStringsAsync(new[]
            {
                CreateRow(0, "MGEF:FULL", "Sudden Tornado"),
                CreateRow(1, "SPEL:FULL", "Sudden Tornado"),
                CreateRow(2, "SCRL:FULL", "Sudden Tornado"),
            }, CancellationToken.None);
            var fix = (await db.GetStringsAsync(1, 0, CancellationToken.None)).Single();
            await db.UpdateStringTranslationAsync(fix.Id, "약한 충격: 비틀거림", StringEntryStatus.Edited, null, CancellationToken.None);
            var options = new ProjectContextScanOptions("Rim.esp", "Rim.esp", "english", "korean");
            Assert.Contains((await new ProjectContextScanner().ScanAsync(db, null, options, CancellationToken.None)).TopTerms,
                t => t.Source == "Sudden Tornado" && t.Target == "약한 충격: 비틀거림");

            await db.SetSourceFixAsync(fix.Id, true, CancellationToken.None);

            var term = Assert.Single((await new ProjectContextScanner().ScanAsync(db, null, options, CancellationToken.None)).TopTerms,
                t => t.Source == "Sudden Tornado");
            Assert.Null(term.Target);
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(path);
        }
    }

    /// <summary>
    /// A short line of dialogue a person edited is no term's definition: the scan's copy of the rule let INFO:NAM1 in (as
    /// ":NAM"), and the term table showed the edited line as the term's translation.
    /// </summary>
    [Fact]
    public async Task AnEditedLineOfDialogue_DoesNotTranslateATerm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            await db.BulkInsertStringsAsync(new[]
            {
                CreateRow(0, "INFO:NAM1", "Sudden Tornado"),
                CreateRow(1, "MGEF:FULL", "Sudden Tornado"),
                CreateRow(2, "SPEL:FULL", "Sudden Tornado"),
                CreateRow(3, "SCRL:FULL", "Sudden Tornado"),
            }, CancellationToken.None);
            var line = (await db.GetStringsAsync(1, 0, CancellationToken.None)).Single();
            await db.UpdateStringTranslationAsync(line.Id, "갑작스러운 회오리라니, 조심해", StringEntryStatus.Edited, null, CancellationToken.None);

            var report = await new ProjectContextScanner().ScanAsync(db, null, new ProjectContextScanOptions("Rim.esp", "Rim.esp", "english", "korean"), CancellationToken.None);

            Assert.Null(Assert.Single(report.TopTerms, t => t.Source == "Sudden Tornado").Target);
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(path);
        }
    }

    private static (int OrderIndex, string? ListAttr, string? PartialAttr, string? AttributesJson, string? Edid, string? Rec, string SourceText, string DestText, StringEntryStatus Status, string RawStringXml) CreateRow(
        int orderIndex,
        string rec,
        string sourceText
    )
    {
        return (
            OrderIndex: orderIndex,
            ListAttr: null,
            PartialAttr: null,
            AttributesJson: null,
            Edid: null,
            Rec: rec,
            SourceText: sourceText,
            DestText: "",
            Status: StringEntryStatus.Pending,
            RawStringXml: "<r/>"
        );
    }

    private static async Task SeedProjectAsync(ProjectDb db)
    {
        var now = DateTimeOffset.UtcNow;
        await db.UpsertProjectAsync(
            new ProjectInfo(
                Id: 1,
                InputXmlPath: "C:\\dummy.xml",
                AddonName: "Dummy",
                Franchise: null,
                SourceLang: "english",
                DestLang: "korean",
                XmlVersion: "1",
                XmlHasBom: false,
                XmlPrologLine: "<?xml version=\"1.0\"?>",
                ModelName: "gemini-3.0-flash-preview",
                BasePromptText: "base",
                CustomPromptText: null,
                UseCustomPrompt: false,
                CreatedAt: now,
                UpdatedAt: now
            ),
            CancellationToken.None
        );
    }

}
