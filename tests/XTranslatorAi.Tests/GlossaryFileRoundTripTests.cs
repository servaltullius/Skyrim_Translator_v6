using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The glossary export starts uncategorized rows with a tab. The import trimmed the whole line first, so every
/// column moved left ("Elder Scroll → 엘더스크롤" came back as source 엘더스크롤, target "1"), and the export's
/// settings were replaced by the import dialog's choices.
/// </summary>
public class GlossaryFileRoundTripTests
{
    private static readonly (string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, GlossaryMatchMode MatchMode, GlossaryForceMode ForceMode, string? Note)[] Exported =
    {
        (null, "Elder Scroll", "엘더스크롤", true, 10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null),
        ("Elden Rim / 전기", "Weapon Art", "전기", false, 20, GlossaryMatchMode.WordBoundary, GlossaryForceMode.PromptOnly, "Elden Rim 기준 용어"),
        (null, "Hearthfire", "9월", true, 10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.PromptOnly, "Built-in default glossary"),
        (null, "Hearthfire", "허스파이어", true, 10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.PromptOnly, "Built-in default glossary"),
    };

    [Fact]
    public void ParsesTheExportWithItsColumnsAndSettings()
    {
        var parsed = GlossaryFileService.ParseTsvGlossaryEntries(GlossaryFileService.BuildGlossaryTsv(Exported));

        Assert.Equal(4, parsed.Count);
        Assert.Equal((null, "Elder Scroll", "엘더스크롤"), (parsed[0].Category, parsed[0].Source, parsed[0].Target));
        Assert.Equal(new GlossaryFileService.GlossaryFileSettings(true, 10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null), parsed[0].Settings);
        Assert.Equal(new GlossaryFileService.GlossaryFileSettings(false, 20, GlossaryMatchMode.WordBoundary, GlossaryForceMode.PromptOnly, "Elden Rim 기준 용어"), parsed[1].Settings);
    }

    [Theory]
    [InlineData("Source\tTarget\nDragonborn\t드래곤본\n", null)]
    [InlineData("Category\tSource\tTarget\nNames\tDragonborn\t드래곤본\n", "Names")]
    [InlineData("\tDragonborn\t드래곤본\n", null)]
    public void ParsesHandWrittenFilesWithoutSettings(string text, string? category)
    {
        var entry = Assert.Single(GlossaryFileService.ParseTsvGlossaryEntries(text));

        Assert.Equal((category, "Dragonborn", "드래곤본"), (entry.Category, entry.Source, entry.Target));
        Assert.Null(entry.Settings);
    }

    [Fact]
    public async Task ExportThenImport_RestoresEveryEntryAsItWas()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xt-glossary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "project.sqlite");
        var tsvPath = Path.Combine(root, "glossary.tsv");
        try
        {
            await File.WriteAllTextAsync(tsvPath, GlossaryFileService.BuildGlossaryTsv(Exported));
            await using (var db = await ProjectDb.OpenOrCreateAsync(dbPath, CancellationToken.None))
            {
                var result = await new GlossaryImportService(new GlossaryFileService()).ImportFromFileAsync(db, tsvPath,
                    new GlossaryImportService.GlossaryImportOptions(5, GlossaryMatchMode.Substring, GlossaryForceMode.ForceToken, "import dialog"),
                    CancellationToken.None);

                Assert.Equal(4, result!.Value.InsertedCount);
                Assert.Equal(0, result.Value.ConflictCount);
                var imported = (await db.GetGlossaryAsync(CancellationToken.None))
                    .Select(g => (g.Category, g.SourceTerm, g.TargetTerm, g.Enabled, g.Priority, g.MatchMode, g.ForceMode, g.Note))
                    .OrderBy(g => g.SourceTerm).ThenBy(g => g.TargetTerm)
                    .ToArray();
                Assert.Equal(Exported.OrderBy(g => g.SourceTerm).ThenBy(g => g.TargetTerm).ToArray(), imported);
            }
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(dbPath);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
