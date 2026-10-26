using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// What the user decided about a row besides its translation: the earlier release the check reads, the marks a set-aside
/// translation carries back, and the speech styles of a speaker whose FormID an update renumbered.
/// </summary>
public sealed partial class PluginProjectIntegrationTests
{
    [Fact]
    public async Task ProjectRows_CarryTheLinkedEarlierRelease_SoItsEnglishIsNotLeftOver()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string key = "WEAP/02000800/0/FULL/1";
        await fixture.ImportAsync(new PluginField(key, 0, "WEAP", "FULL", 0x02000800, "TheOneSword", 1, 1, "The One's Sword"));
        var row = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(row.Id, "The One의 검", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.ReplacePreviousTranslationsAsync("Old.esp", new Dictionary<string, string> { [key] = "The One의 검" }, CancellationToken.None);

        var rows = await ProjectLqaScan.LoadRowsAsync(fixture.Db, CancellationToken.None);
        var entries = rows.Select(row => ProjectLqaScan.ToEntry(row)).ToList();
        var issues = (await ProjectLqaScan.ScanAsync(fixture.Db, rows, "korean", Array.Empty<GlossaryEntry>(), null, null, CancellationToken.None)).Issues;

        Assert.Equal("The One의 검", Assert.Single(rows).PreviousText);
        // The earlier release kept "The One" too, so the GUI does not call it left over; the agent's check did.
        Assert.DoesNotContain(issues, issue => issue.Code == "english_residue");
        var withoutIt = await LqaScanner.ScanAsync(entries.Select(e => e with { PreviousText = null }).ToList(), "korean", Array.Empty<GlossaryEntry>());
        Assert.Contains(withoutIt, issue => issue.Code == "english_residue");
    }

    [Fact]
    public async Task ChangedSourceThatComesBack_BringsBackTheMarksOnItsTranslation()
    {
        await using var fixture = await Fixture.CreateAsync();
        static PluginField Sword(string source) => new("WEAP/00000800/0/FULL/1", 0, "WEAP", "FULL", 0x00000800, "Sword", 1, 1, source);
        await fixture.ImportAsync(Sword("Steel Sword"));
        var row = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(row.Id, "강철 대검", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpsertStringNoteAsync(row.Id, TranslationConstants.SourceFixNoteKind, "원문 오타", CancellationToken.None);
        await fixture.Db.UpsertStringNoteAsync(row.Id, TranslationConstants.LqaReviewedNoteKindPrefix + "tone", "확인", CancellationToken.None);

        await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v2" }, new[] { Sword("Steel Greatsword") },
            fixture.Project, "utf-8", CancellationToken.None);
        Assert.Empty(await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None));
        var back = Assert.Single(await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v3" }, new[] { Sword("Steel Sword") },
            fixture.Project, "utf-8", CancellationToken.None));

        Assert.Equal(("강철 대검", StringEntryStatus.Edited), (back.DestText, back.Status));
        Assert.Equal("원문 오타", (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None))[back.Id]);
        Assert.Equal("확인", (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.LqaReviewedNoteKindPrefix + "tone", CancellationToken.None))[back.Id]);
    }

    [Fact]
    public async Task OlderDb_GetsTheMarksColumnOnTheRetiredTables()
    {
        var directory = Path.Combine(Path.GetTempPath(), "plugin-project-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "old.sqlite");
        try
        {
            await using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await old.OpenAsync();
                await using var create = old.CreateCommand();
                create.CommandText = """
                    CREATE TABLE RetiredTranslation (ImportIdentity TEXT NOT NULL, OrderIndex INTEGER NOT NULL, DestText TEXT NOT NULL,
                      Status INTEGER NOT NULL, RetiredAt TEXT NOT NULL, PRIMARY KEY (ImportIdentity, OrderIndex));
                    CREATE TABLE PluginRetiredTranslation (FieldKey TEXT NOT NULL, SourceText TEXT NOT NULL, DestText TEXT NOT NULL,
                      Status INTEGER NOT NULL, RetiredAt TEXT NOT NULL, PRIMARY KEY (FieldKey, SourceText));
                    INSERT INTO PluginRetiredTranslation VALUES ('WEAP/00000800/0/FULL/1', 'Steel Sword', '강철 검', 4, 'then');
                    """;
                await create.ExecuteNonQueryAsync();
            }

            await (await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None)).DisposeAsync();

            await using var check = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            await check.OpenAsync();
            foreach (var table in new[] { "RetiredTranslation", "PluginRetiredTranslation" })
            {
                await using var columns = check.CreateCommand();
                columns.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='Marks';";
                Assert.Equal(1L, (long)(await columns.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(path);
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task MasterListChange_MovesTheSpeakerStylesToTheNewFormIds()
    {
        await using var fixture = await Fixture.CreateAsync();
        static PluginField Line(uint formId) => new($"INFO/{formId:X8}/0/NAM1/1", 0, "INFO", "NAM1", formId, "", 1, 1, "Well met.");
        var v1 = fixture.Source with { Masters = new[] { "Skyrim.esm", "Dawnguard.esm" } };
        await fixture.Db.ReplaceImportedPluginStringsAsync(v1, new[] { Line(0x02000900) }, fixture.Project, "utf-8", CancellationToken.None);
        await fixture.Db.SetSpeakerStyleAsync(new SpeakerStyle("02000800", "", "하오체"), CancellationToken.None);
        await fixture.Db.SetSpeakerStyleAsync(new SpeakerStyle("player", "02000800", "해요체"), CancellationToken.None);
        await fixture.Db.SetSpeakerStyleAsync(new SpeakerStyle("01001234", "*", "반말"), CancellationToken.None);   // Dawnguard's NPC
        await fixture.Db.SetSpeakerStyleAsync(new SpeakerStyle("00013BBF", "00000000", "합쇼체"), CancellationToken.None); // Skyrim's

        // Update.esm comes in before Dawnguard: the plugin's own NPC moves from 02 to 03, Dawnguard's from 01 to 02.
        var v2 = v1 with { Sha256 = "v2", Masters = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm" } };
        await fixture.Db.ReplaceImportedPluginStringsAsync(v2, new[] { Line(0x03000900) }, fixture.Project, "utf-8", CancellationToken.None);

        Assert.Equal(new[]
            {
                new SpeakerStyle("00013BBF", "00000000", "합쇼체"), new SpeakerStyle("02001234", "*", "반말"),
                new SpeakerStyle("03000800", "", "하오체"), new SpeakerStyle("player", "03000800", "해요체"),
            },
            await fixture.Db.GetSpeakerStylesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SpeakerStyleOfAMasterThatLeft_IsOnNoNpc_AndComesBackWithTheMaster()
    {
        await using var fixture = await Fixture.CreateAsync();
        static PluginField Line(uint formId) => new($"INFO/{formId:X8}/0/NAM1/1", 0, "INFO", "NAM1", formId, "", 1, 1, "Well met.");
        var v1 = fixture.Source with { Masters = new[] { "Skyrim.esm", "Dawnguard.esm" } };
        await fixture.Db.ReplaceImportedPluginStringsAsync(v1, new[] { Line(0x02000900) }, fixture.Project, "utf-8", CancellationToken.None);
        await fixture.Db.SetSpeakerStyleAsync(new SpeakerStyle("01001234", "", "반말"), CancellationToken.None);

        // Dawnguard leaves: 01 now names the plugin's own records, so the style must not stay on 01001234.
        await fixture.Db.ReplaceImportedPluginStringsAsync(v1 with { Sha256 = "v2", Masters = new[] { "Skyrim.esm" } },
            new[] { Line(0x01000900) }, fixture.Project, "utf-8", CancellationToken.None);
        Assert.Equal("@Dawnguard.esm|01001234", Assert.Single(await fixture.Db.GetSpeakerStylesAsync(CancellationToken.None)).Speaker);

        await fixture.Db.ReplaceImportedPluginStringsAsync(v1 with { Sha256 = "v3", Masters = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm" } },
            new[] { Line(0x03000900) }, fixture.Project, "utf-8", CancellationToken.None);
        Assert.Equal(new SpeakerStyle("02001234", "", "반말"), Assert.Single(await fixture.Db.GetSpeakerStylesAsync(CancellationToken.None)));
    }

    // A row still present keeps every note of a machine translation (the grid's TM mark, the check's TM notes); one
    // set aside and back kept only the user's marks.
    [Fact]
    public async Task ChangedSourceThatComesBack_BringsBackTheTmNoteOfAMachineTranslation()
    {
        await using var fixture = await Fixture.CreateAsync();
        static PluginField Sword(string source) => new("WEAP/00000800/0/FULL/1", 0, "WEAP", "FULL", 0x00000800, "Sword", 1, 1, source);
        await fixture.ImportAsync(Sword("Steel Sword"));
        var row = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(row.Id, "강철 검", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpsertStringNoteAsync(row.Id, "tm_fallback", "TM", CancellationToken.None);

        await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v2" }, new[] { Sword("Steel Greatsword") },
            fixture.Project, "utf-8", CancellationToken.None);
        var back = Assert.Single(await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v3" }, new[] { Sword("Steel Sword") },
            fixture.Project, "utf-8", CancellationToken.None));

        Assert.Equal(("강철 검", StringEntryStatus.Done), (back.DestText, back.Status));
        Assert.Equal("TM", (await fixture.Db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None))[back.Id]);
    }
}
