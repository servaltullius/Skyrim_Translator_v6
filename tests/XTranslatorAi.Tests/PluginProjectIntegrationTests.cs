using System.Text;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public sealed class PluginProjectIntegrationTests
{
    [Fact]
    public void PluginPaths_IsolateXmlInputLocationLanguageAndEncoding()
    {
        var root = Path.Combine(Path.GetTempPath(), "plugin-paths-" + Guid.NewGuid().ToString("N"));
        var input = Path.Combine(root, "input", "Same.esp");
        var options = new PluginReadOptions();
        var paths = new[]
        {
            ProjectPaths.GetProjectDbPath(BethesdaFranchise.ElderScrolls, "Same.esp", "english", "korean", root),
            ProjectPaths.GetPluginProjectDbPath(input, options, "korean", "utf-8", root),
            ProjectPaths.GetPluginProjectDbPath(Path.Combine(root, "other", "Same.esp"), options, "korean", "utf-8", root),
            ProjectPaths.GetPluginProjectDbPath(input, options, "japanese", "utf-8", root),
            ProjectPaths.GetPluginProjectDbPath(input, options with { SourceEncoding = "windows-1252" }, "korean", "utf-8", root),
            ProjectPaths.GetPluginProjectDbPath(input, options with { MetadataEncoding = "ks_c_5601-1987" }, "korean", "utf-8", root),
            ProjectPaths.GetPluginProjectDbPath(input, options, "korean", "ks_c_5601-1987", root),
        };
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Reimport_PreservesDoneAndEmptyManualEditByFieldIdentity_NotTextOrOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = Field("first", 0, "Iron Sword");
        var second = Field("second", 1, "Iron Sword");
        var changed = Field("changed", 2, "Old source");
        await fixture.ImportAsync(first, second, changed);
        var rows = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[0].Id, "철검", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[1].Id, "", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[2].Id, "이전 번역", StringEntryStatus.Edited, null, CancellationToken.None);

        await fixture.Db.DisposeAsync();
        fixture.Db = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
        await fixture.ImportAsync(second with { OrderIndex = 0 }, changed with { OrderIndex = 1, SourceText = "New source" }, first with { OrderIndex = 2 });
        var after = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        Assert.Equal(StringEntryStatus.Edited, after[0].Status);
        Assert.Equal("", after[0].DestText);
        Assert.Equal(StringEntryStatus.Pending, after[1].Status);
        Assert.Equal("New source", after[1].DestText);
        Assert.Equal(StringEntryStatus.Done, after[2].Status);
        Assert.Equal("철검", after[2].DestText);
        Assert.All(await fixture.Db.GetStringsForExportAsync(20, 0, CancellationToken.None), row => Assert.Equal("", row.RawStringXml));
        Assert.Equal(fixture.Source.Sha256, (await fixture.Db.TryGetPluginSourceAsync(CancellationToken.None))!.Info.Sha256);
    }

    // A row without letters (" " in MEI) is finished by keeping its source. Reopening the plugin dropped it as an
    // empty translation, so the project showed one row waiting after every reopen.
    [Fact]
    public async Task Reimport_KeepsABlankRowThatWasFinishedAsItsSource()
    {
        await using var fixture = await Fixture.CreateAsync();
        var blank = Field("blank", 0, " ");
        await fixture.ImportAsync(blank);
        var row = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(row.Id, " ", StringEntryStatus.Done, null, CancellationToken.None);

        await fixture.ImportAsync(blank);

        var after = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        Assert.Equal(StringEntryStatus.Done, after.Status);
        Assert.Equal(" ", after.DestText);
    }

    [Fact]
    public async Task InvalidImport_RollsBackStringsBindingsAndSourceMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("original", 0, "Original"));
        var row = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(row.Id, "원문", StringEntryStatus.Done, null, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Db.ReplaceImportedPluginStringsAsync(
            fixture.Source with { Sha256 = "different" }, new[] { Field("duplicate", 0, "One"), Field("duplicate", 1, "Two") },
            fixture.Project, "windows-1252", CancellationToken.None));
        var after = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        Assert.Equal("Original", after.SourceText);
        Assert.Equal("원문", after.DestText);
        var source = await fixture.Db.TryGetPluginSourceAsync(CancellationToken.None);
        Assert.Equal(fixture.Source.Sha256, source!.Info.Sha256);
        Assert.Equal("utf-8", source.TargetEncoding);
        Assert.Equal("원문", (await fixture.Db.GetPluginTranslationsForExportAsync(fixture.Source.Sha256, CancellationToken.None))["original"]);
    }

    [Fact]
    public async Task CanceledImport_LeavesExistingProjectIntact()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("original", 0, "Original"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Db.ReplaceImportedPluginStringsAsync(
            fixture.Source, new[] { Field("replacement", 0, "Replacement") }, fixture.Project, "utf-8", canceled.Token));
        Assert.Equal("Original", Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None)).SourceText);
    }

    [Fact]
    public async Task CancellationDuringImport_RollsBackAlreadyInsertedRowsAndMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("original", 0, "Original"));
        var original = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(original.Id, "수동 번역", StringEntryStatus.Edited, null, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var fields = new CancelingFields(new[] { Field("first", 0, "First"), Field("second", 1, "Second") }, cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Db.ReplaceImportedPluginStringsAsync(
            fixture.Source with { Sha256 = "new-revision" }, fields, fixture.Project with { AddonName = "new.esp" },
            "windows-1252", cancellation.Token));
        Assert.True(fields.FirstRowProcessed);
        var after = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        Assert.Equal(original.Id, after.Id);
        Assert.Equal("Original", after.SourceText);
        Assert.Equal("수동 번역", after.DestText);
        Assert.Equal(StringEntryStatus.Edited, after.Status);
        Assert.Equal("Test.esp", (await fixture.Db.TryGetProjectAsync(CancellationToken.None))!.AddonName);
        Assert.Equal("utf-8", (await fixture.Db.TryGetPluginSourceAsync(CancellationToken.None))!.TargetEncoding);
        Assert.Equal("수동 번역", (await fixture.Db.GetPluginTranslationsForExportAsync(fixture.Source.Sha256, CancellationToken.None))["original"]);
    }

    [Fact]
    public async Task SuccessfulImport_ReturnsExactCommittedRowsForAtomicUiAdoption()
    {
        await using var fixture = await Fixture.CreateAsync();
        var field = Field("original", 0, "Original");
        await fixture.ImportAsync(field);
        var original = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(original.Id, "수동 번역", StringEntryStatus.Edited, null, CancellationToken.None);
        var snapshot = await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source,
            new[] { field, Field("new", 1, "New") }, fixture.Project, "utf-8", CancellationToken.None);
        var committed = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        Assert.Equal(committed, snapshot);
        Assert.Equal(StringEntryStatus.Edited, snapshot[0].Status);
        Assert.Equal("수동 번역", snapshot[0].DestText);
        Assert.Equal(StringEntryStatus.Pending, snapshot[1].Status);
    }

    [Fact]
    public async Task PluginSourceMetadata_PersistsIndependentMetadataEncoding()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = fixture.Source with
        {
            Options = new PluginReadOptions(SourceEncoding: "utf-8", MetadataEncoding: "ks_c_5601-1987"),
        };
        await fixture.Db.ReplaceImportedPluginStringsAsync(source, new[] { Field("original", 0, "Original") },
            fixture.Project, "windows-1252", CancellationToken.None);
        await fixture.Db.DisposeAsync();
        fixture.Db = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
        var saved = (await fixture.Db.TryGetPluginSourceAsync(CancellationToken.None))!;
        Assert.Equal("utf-8", saved.Info.Options.SourceEncoding);
        Assert.Equal("ks_c_5601-1987", saved.Info.Options.MetadataEncoding);
        Assert.Equal("windows-1252", saved.TargetEncoding);
    }

    [Fact]
    public async Task ExportSnapshot_OnlyIncludesCompletedAndManualFields_AndRejectsWrongRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("done", 0, "Done"), Field("edited", 1, "Edited"), Field("pending", 2, "Pending"), Field("skipped", 3, "Skipped"));
        var rows = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[0].Id, "완료", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[1].Id, "", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[3].Id, "저장 제외", StringEntryStatus.Skipped, null, CancellationToken.None);
        var map = await fixture.Db.GetPluginTranslationsForExportAsync(fixture.Source.Sha256, CancellationToken.None);
        Assert.Equal(2, map.Count);
        Assert.Equal("완료", map["done"]);
        Assert.Equal("", map["edited"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.GetPluginTranslationsForExportAsync("wrong", CancellationToken.None));
    }

    [Fact]
    public async Task ImportIntoXmlDb_IsRejectedBeforeAnyRowsAreDeleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Db.BulkInsertStringsAsync(new[] { (0, (string?)null, (string?)null, (string?)null, (string?)null,
            (string?)"WEAP:FULL", "XML source", "번역", StringEntryStatus.Done, "<String><Source>XML source</Source></String>") }, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync(Field("plugin", 0, "Plugin")));
        Assert.Equal("XML source", Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None)).SourceText);
        Assert.Null(await fixture.Db.TryGetPluginSourceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SourceToDbToPlugin_ExportsTranslatedFieldsWithoutChangingOriginal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = Path.Combine(fixture.DirectoryPath, "Test.esp");
        var bytes = CreateMinimalPlugin();
        await File.WriteAllBytesAsync(input, bytes);
        var document = await PluginReader.ReadAsync(input, new PluginReadOptions(), CancellationToken.None);
        await fixture.Db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, fixture.Project, "utf-8", CancellationToken.None);
        var row = Assert.Single(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None));
        await fixture.Db.UpdateStringTranslationAsync(row.Id, "철검", StringEntryStatus.Edited, null, CancellationToken.None);
        var workspace = new ProjectWorkspaceService(new GlobalProjectDbService(new BuiltInGlossaryService(), fixture.DirectoryPath), fixture.DirectoryPath);
        var output = Path.Combine(fixture.DirectoryPath, "translated");
        var result = await workspace.ExportPluginAsync(fixture.Db, document, new PluginExportOptions(output), CancellationToken.None);
        Assert.Equal(Path.Combine(output, "Test.esp"), result.PluginPath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(input));
        var translated = await PluginReader.ReadAsync(result.PluginPath, new PluginReadOptions(), CancellationToken.None);
        Assert.Equal("철검", Assert.Single(translated.Fields).SourceText);
        Assert.Equal(document.Fields[0].Key, translated.Fields[0].Key);
    }

    [Fact]
    public async Task CanceledExport_DoesNotCreateOutputDirectoryOrChangeInput()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = Path.Combine(fixture.DirectoryPath, "Test.esp");
        var bytes = CreateMinimalPlugin();
        await File.WriteAllBytesAsync(input, bytes);
        var document = await PluginReader.ReadAsync(input, new PluginReadOptions(), CancellationToken.None);
        await fixture.Db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, fixture.Project, "utf-8", CancellationToken.None);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var workspace = new ProjectWorkspaceService(new GlobalProjectDbService(new BuiltInGlossaryService(), fixture.DirectoryPath), fixture.DirectoryPath);
        var output = Path.Combine(fixture.DirectoryPath, "canceled");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.ExportPluginAsync(fixture.Db, document, new PluginExportOptions(output), canceled.Token));
        Assert.False(Directory.Exists(output));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(input));
    }

    internal static byte[] CreateMinimalPlugin(string itemName = "Iron Sword")
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        using var header = new MemoryStream();
        using (var data = new BinaryWriter(header, Encoding.UTF8, leaveOpen: true))
        {
            data.Write(Encoding.ASCII.GetBytes("HEDR")); data.Write((ushort)12);
            data.Write(1.7f); data.Write(1u); data.Write(0x801u);
        }
        WriteRecord("TES4", 0, header.ToArray());
        using var fields = new MemoryStream();
        using (var data = new BinaryWriter(fields, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var (type, text) in new[] { ("EDID", "TestSword"), ("FULL", itemName) })
            {
                var payload = Encoding.UTF8.GetBytes(text + "\0");
                data.Write(Encoding.ASCII.GetBytes(type)); data.Write((ushort)payload.Length); data.Write(payload);
            }
        }
        WriteRecord("WEAP", 0x800u, fields.ToArray());
        return output.ToArray();

        void WriteRecord(string type, uint form, byte[] payload)
        {
            writer.Write(Encoding.ASCII.GetBytes(type)); writer.Write((uint)payload.Length); writer.Write(0u);
            writer.Write(form); writer.Write(0u); writer.Write((ushort)44); writer.Write((ushort)0); writer.Write(payload);
        }
    }

    // A mod update that changes one source string used to delete that row's translation, reviewed ones included,
    // and reopening the earlier file did not bring it back.
    [Fact]
    public async Task ChangedSource_KeepsTheOldTranslationAside_AndRestoresItWhenTheSourceComesBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("sword", 0, "Iron Sword"), Field("shield", 1, "Iron Shield"));
        var rows = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[0].Id, "철 검", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[1].Id, "철 방패", StringEntryStatus.Done, null, CancellationToken.None);

        var updated = await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v2" },
            new[] { Field("sword", 0, "Iron Sword of Doom"), Field("shield", 1, "Iron Shield") }, fixture.Project, "utf-8", CancellationToken.None);
        Assert.Equal(StringEntryStatus.Pending, updated[0].Status);
        Assert.Equal("철 방패", updated[1].DestText);
        Assert.Equal(1, await fixture.Db.GetRetiredPluginTranslationCountAsync(CancellationToken.None));

        var reverted = await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source,
            new[] { Field("sword", 0, "Iron Sword"), Field("shield", 1, "Iron Shield") }, fixture.Project, "utf-8", CancellationToken.None);
        Assert.Equal("철 검", reverted[0].DestText);
        Assert.Equal(StringEntryStatus.Edited, reverted[0].Status);
        Assert.Equal(0, await fixture.Db.GetRetiredPluginTranslationCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RemovedField_KeepsItsTranslationAside()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("sword", 0, "Iron Sword"), Field("shield", 1, "Iron Shield"));
        var rows = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[1].Id, "철 방패", StringEntryStatus.Done, null, CancellationToken.None);

        await fixture.ImportAsync(Field("sword", 0, "Iron Sword"));
        Assert.Equal(1, await fixture.Db.GetRetiredPluginTranslationCountAsync(CancellationToken.None));

        var back = await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source,
            new[] { Field("sword", 0, "Iron Sword"), Field("shield", 1, "Iron Shield") }, fixture.Project, "utf-8", CancellationToken.None);
        Assert.Equal("철 방패", back[1].DestText);
    }

    // Exporting writes the translated plugin next to the original with the same file names; opening that file as the
    // source made every translated row look changed and wiped the whole project.
    [Fact]
    public async Task OpeningTheTranslatedPluginAsSource_IsRefused_AndKeepsTheProject()
    {
        await using var fixture = await Fixture.CreateAsync();
        var english = Enumerable.Range(0, 6).Select(i => Field($"k{i}", i, $"Sword {i}")).ToArray();
        await fixture.ImportAsync(english);
        foreach (var row in await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None))
            await fixture.Db.UpdateStringTranslationAsync(row.Id, $"검 {row.OrderIndex}", StringEntryStatus.Done, null, CancellationToken.None);

        var korean = Enumerable.Range(0, 6).Select(i => Field($"k{i}", i, $"검 {i}")).ToArray();
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Db.ReplaceImportedPluginStringsAsync(
            fixture.Source with { Sha256 = "translated" }, korean, fixture.Project, "utf-8", CancellationToken.None));
        Assert.StartsWith("번역된 플러그인을 원문으로 열 수 없습니다", ex.Message);
        Assert.All(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None), r => Assert.StartsWith("검 ", r.DestText));
    }

    [Fact]
    public void TranslatedPluginAsSource_HasItsOwnUserFacingMessage()
    {
        var error = PluginUserFacingErrorClassifier.Classify(new InvalidDataException("번역된 플러그인을 원문으로 열 수 없습니다: 6행"));
        Assert.Equal("E460", error!.Value.Code);
        Assert.Contains("원본 플러그인", error.Value.Message);
    }

    // Reopening a plugin deleted every row note, so the grid lost its TM marks and the quality check its TM notes.
    [Fact]
    public async Task Reopening_KeepsTheNotesOfRowsWhoseSourceIsUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("sword", 0, "Iron Sword"), Field("shield", 1, "Iron Shield"));
        var rows = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[0].Id, "철 검", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(rows[1].Id, "철 방패", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpsertStringNoteAsync(rows[0].Id, "tm_hit", "TM 적용", CancellationToken.None);
        await fixture.Db.UpsertStringNoteAsync(rows[1].Id, "tm_hit", "TM 적용", CancellationToken.None);

        var reopened = await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v2" },
            new[] { Field("sword", 0, "Iron Sword"), Field("shield", 1, "Steel Shield") }, fixture.Project, "utf-8", CancellationToken.None);

        var notes = await fixture.Db.GetStringNotesByKindAsync("tm_hit", CancellationToken.None);
        Assert.Equal(new[] { reopened[0].Id }, notes.Keys.ToArray());
    }

    // A row whose translation is not kept (it failed, so it reopens as Pending) showed a stale TM mark.
    [Fact]
    public async Task Reopening_DropsTheNotesOfRowsWhoseTranslationIsNotKept()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("sword", 0, "Iron Sword"));
        var row = (await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None))[0];
        await fixture.Db.UpdateStringTranslationAsync(row.Id, "", StringEntryStatus.Error, "boom", CancellationToken.None);
        await fixture.Db.UpsertStringNoteAsync(row.Id, "tm_fallback", "TM 대체", CancellationToken.None);

        var reopened = await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v2" },
            new[] { Field("sword", 0, "Iron Sword") }, fixture.Project, "utf-8", CancellationToken.None);

        Assert.Equal(StringEntryStatus.Pending, reopened[0].Status);
        Assert.Empty(await fixture.Db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None));
    }

    // The status bar said "번역 N개를 보관해 두었습니다" on every later reopen, counting all translations kept so far.
    [Fact]
    public async Task RetiredCount_CountsOnlyWhatThisImportSetAside()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Field("sword", 0, "Iron Sword"));
        var row = (await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None))[0];
        await fixture.Db.UpdateStringTranslationAsync(row.Id, "철 검", StringEntryStatus.Done, null, CancellationToken.None);

        await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v2" },
            new[] { Field("sword", 0, "Iron Sword of Doom") }, fixture.Project, "utf-8", CancellationToken.None);
        Assert.Equal(1, fixture.Db.LastPluginImportRetiredCount);

        await fixture.Db.ReplaceImportedPluginStringsAsync(fixture.Source with { Sha256 = "v2" },
            new[] { Field("sword", 0, "Iron Sword of Doom") }, fixture.Project, "utf-8", CancellationToken.None);
        Assert.Equal(0, fixture.Db.LastPluginImportRetiredCount);
        Assert.Equal(1, await fixture.Db.GetRetiredPluginTranslationCountAsync(CancellationToken.None));
    }

    // E457 said only that some character somewhere could not be written in the output encoding.
    [Fact]
    public void UnencodableCharacter_NamesTheRowAndTheCharacter()
    {
        var error = PluginUserFacingErrorClassifier.Classify(
            new InvalidDataException("출력 인코딩으로 표현할 수 없는 문자가 있습니다: WEAP:FULL/00000800 U+2014"));
        Assert.Equal("E457", error!.Value.Code);
        Assert.Contains("WEAP:FULL/00000800", error.Value.Message);
        Assert.Contains("U+2014", error.Value.Message);
        Assert.Contains("form:00000800", error.Value.Message);
    }

    private static PluginField Field(string key, int index, string source)
        => new(key, index, "WEAP", "FULL", (uint)(0x800 + index), "TestSword", index + 1, 1, source);

    private sealed class CancelingFields(IReadOnlyList<PluginField> fields, CancellationTokenSource cancellation) : IReadOnlyList<PluginField>
    {
        public int Count => fields.Count;
        public PluginField this[int index] => fields[index];
        public bool FirstRowProcessed { get; private set; }
        public IEnumerator<PluginField> GetEnumerator()
        {
            yield return fields[0];
            // This resumes only after the importer finished inserting the first replacement row.
            FirstRowProcessed = true;
            cancellation.Cancel();
            yield return fields[1];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "plugin-project-tests", Guid.NewGuid().ToString("N"));
        public string DbPath => Path.Combine(DirectoryPath, "project.sqlite");
        public ProjectDb Db { get; set; } = null!;
        public PluginSourceInfo Source => new(Path.Combine(DirectoryPath, "Test.esp"), "original-hash", new PluginReadOptions(), false,
            Array.Empty<string>(), Array.Empty<PluginDiagnostic>());
        public ProjectInfo Project => new(1, "", "Test.esp", BethesdaFranchise.ElderScrolls, "english", "korean", "", false, "", "test", "", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.Db = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
            return fixture;
        }
        public Task ImportAsync(params PluginField[] fields)
            => Db.ReplaceImportedPluginStringsAsync(Source, fields, Project, "utf-8", CancellationToken.None);
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(DbPath);
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
