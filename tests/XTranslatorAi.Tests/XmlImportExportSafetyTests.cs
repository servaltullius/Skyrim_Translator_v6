using System.Text;
using System.Xml.Linq;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public sealed class XmlImportExportSafetyTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-xml-tests-" + Guid.NewGuid().ToString("N"));
    private ProjectDb _db = null!;
    private string XmlPath => Path.Combine(_root, "input.xml");
    private string DbPath => Path.Combine(_root, "project.sqlite");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _db = await ProjectDb.OpenOrCreateAsync(DbPath, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(DbPath);
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n  ")]
    public async Task AdjacentRows_ImportAndExportWithoutLosingRecords(string separator)
    {
        var rows = Enumerable.Range(1, 3).Select(i => Row(i.ToString(), "source " + i, "번역 " + i));
        var xml = Document(string.Join(separator, rows));
        await File.WriteAllTextAsync(XmlPath, xml, new UTF8Encoding(true));
        var read = await XTranslatorXmlImporter.ReadAllAsync(XmlPath, CancellationToken.None);
        Assert.Equal(3, read.Rows.Count);
        var info = await XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath, CancellationToken.None);
        Assert.Equal(3, await _db.GetStringCountAsync(CancellationToken.None));
        Assert.DoesNotContain("SSTXMLRessources", info.PrologLine);
        var output = Path.Combine(_root, "output.xml");
        await XTranslatorXmlExporter.ExportAsync(_db, info, output, CancellationToken.None);
        var parsed = XDocument.Load(output);
        var original = XDocument.Parse(xml);
        Assert.True(XNode.DeepEquals(original.Root, parsed.Root));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, (await File.ReadAllBytesAsync(output)).Take(3));
    }

    [Fact]
    public async Task MalformedTail_RollsBackPreviousRowsNotesAndProject()
    {
        await ImportAsync(Row("1", "old", "이전"));
        await _db.UpsertStringNoteAsync(1, "tm_hit", "old note", CancellationToken.None);
        var oldProject = await _db.TryGetProjectAsync(CancellationToken.None);
        var broken = Document(string.Concat(Enumerable.Range(0, 501).Select(i => Row(i.ToString(), "new", ""))))
            .Replace("</Content>", "<String><Source>broken</Content>");
        await File.WriteAllTextAsync(XmlPath, broken);
        await Assert.ThrowsAnyAsync<Exception>(() => XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath,
            CancellationToken.None, projectFactory: info => Project(info, "new-model")));
        var row = Assert.Single(await _db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Equal("old", row.SourceText);
        Assert.Equal("이전", row.DestText);
        Assert.Equal("old note", (await _db.GetStringNotesByKindAsync("tm_hit", CancellationToken.None))[1]);
        Assert.Equal(oldProject, await _db.TryGetProjectAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancellationAtEnd_RollsBackReplacement()
    {
        await ImportAsync(Row("1", "old", "이전"));
        await File.WriteAllTextAsync(XmlPath, Document(Row("2", "new", "새 내용")));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => XTranslatorXmlImporter.ImportToDbAsync(
            _db, XmlPath, cancellation.Token, projectFactory: info =>
            {
                cancellation.Cancel();
                return Project(info);
            }));
        Assert.Equal("old", Assert.Single(await _db.GetStringsAsync(10, 0, CancellationToken.None)).SourceText);
    }

    [Fact]
    public async Task Reopen_PreservesEditsAndCompletedRowsButAcceptsIncomingTranslations()
    {
        await ImportAsync(Row("1", "a", "") + Row("2", "b", "") + Row("3", "c", "") + Row("4", "d", ""));
        await _db.UpdateStringTranslationAsync(1, "", StringEntryStatus.Edited, null, CancellationToken.None);
        await _db.UpdateStringTranslationAsync(2, "완료", StringEntryStatus.Done, null, CancellationToken.None);
        await _db.UpdateStringTranslationAsync(3, "old", StringEntryStatus.Done, null, CancellationToken.None);
        await _db.UpdateStringTranslationAsync(4, "edited", StringEntryStatus.Edited, null, CancellationToken.None);
        await _db.UpsertStringNoteAsync(1, "tm_hit", "stale", CancellationToken.None);
        // Reordering is safe when the record identity is unique. Changed source is a new translation task.
        await ImportAsync(Row("2", "b", "") + Row("1", "a", "XML value") + Row("3", "c", "새 번역") + Row("4", "changed", ""), preserve: true);
        var loaded = await _db.GetStringsAsync(10, 0, CancellationToken.None);
        Assert.Equal("완료", loaded[0].DestText);
        Assert.Equal(StringEntryStatus.Done, loaded[0].Status);
        Assert.Equal("", loaded[1].DestText);
        Assert.Equal(StringEntryStatus.Edited, loaded[1].Status);
        Assert.Equal("새 번역", loaded[2].DestText);
        Assert.Equal(StringEntryStatus.Pending, loaded[3].Status);
        Assert.Empty(await _db.GetStringNotesByKindAsync("tm_hit", CancellationToken.None));
    }

    // Reopening the XML deleted every note, so the grid lost its TM marks and the quality check its TM notes. A row
    // that keeps its translation keeps them; a row whose translation the file replaced does not.
    [Fact]
    public async Task Reopen_KeepsNotesOfRowsThatKeepTheirTranslation()
    {
        await ImportAsync(Row("1", "Iron Sword", "") + Row("2", "Steel Sword", "") + Row("3", "Iron Axe", ""));
        await _db.UpdateStringTranslationAsync(1, "철검", StringEntryStatus.Done, null, CancellationToken.None);
        await _db.UpdateStringTranslationAsync(2, "강철 검", StringEntryStatus.Done, null, CancellationToken.None);
        await _db.UpdateStringTranslationAsync(3, "철 도끼", StringEntryStatus.Done, null, CancellationToken.None);
        await _db.UpsertStringNoteAsync(1, "tm_fallback", "TM 대신 번역", CancellationToken.None);
        await _db.UpsertStringNoteAsync(2, "tm_hit", "TM 적용", CancellationToken.None);
        await _db.UpsertStringNoteAsync(3, "tm_hit", "TM 적용", CancellationToken.None);

        // Row 1 comes back untranslated, row 2 with the same translation, row 3 with another one.
        await ImportAsync(Row("1", "Iron Sword", "") + Row("2", "Steel Sword", "강철 검") + Row("3", "Iron Axe", "쇠도끼"), preserve: true);

        var loaded = await _db.GetStringsAsync(10, 0, CancellationToken.None);
        var fallback = await _db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None);
        var hits = await _db.GetStringNotesByKindAsync("tm_hit", CancellationToken.None);
        Assert.Equal("TM 대신 번역", fallback[loaded.Single(r => r.SourceText == "Iron Sword").Id]);
        Assert.Equal("TM 적용", hits[loaded.Single(r => r.SourceText == "Steel Sword").Id]);
        Assert.False(hits.ContainsKey(loaded.Single(r => r.SourceText == "Iron Axe").Id));
    }

    // Rows sharing an identity (LotD has 76, mostly repeated objectives such as "Talk to Captain Falx") were matched by
    // position only, so a row added above them dropped their translations without keeping them aside.
    [Fact]
    public async Task Reopen_RowsSharingAnIdentity_KeepTheirTranslationAfterAShift()
    {
        await ImportAsync(Row("7", "Talk to Falx", "") + Row("7", "Talk to Falx", "") + Row("8", "Done", ""));
        await _db.UpdateStringTranslationAsync(1, "팔크스와 대화하기", StringEntryStatus.Done, null, CancellationToken.None);
        await _db.UpdateStringTranslationAsync(2, "팔크스와 대화하기", StringEntryStatus.Done, null, CancellationToken.None);

        await ImportAsync(Row("9", "New row", "") + Row("7", "Talk to Falx", "") + Row("7", "Talk to Falx", "") + Row("8", "Done", ""), preserve: true);

        var loaded = await _db.GetStringsAsync(10, 0, CancellationToken.None);
        Assert.All(loaded.Where(r => r.SourceText == "Talk to Falx"), r => Assert.Equal(("팔크스와 대화하기", StringEntryStatus.Done), (r.DestText, r.Status)));
    }

    [Fact]
    public async Task Reopen_DifferentRecordIdDoesNotBorrowTranslation()
    {
        await ImportAsync(Row("0001", "Same source", ""));
        await _db.UpdateStringTranslationAsync(1, "edited", StringEntryStatus.Edited, null, CancellationToken.None);
        await ImportAsync(Row("0002", "Same source", ""), preserve: true);
        var row = Assert.Single(await _db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Empty(row.DestText);
        Assert.Equal(StringEntryStatus.Pending, row.Status);
    }

    [Fact]
    public async Task NonXTranslatorXml_DoesNotEraseProject()
    {
        await ImportAsync(Row("1", "old", "이전"));
        await File.WriteAllTextAsync(XmlPath, "<unrelated/>");
        await Assert.ThrowsAsync<InvalidDataException>(() => XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath, CancellationToken.None));
        Assert.Equal(1, await _db.GetStringCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Export_UsesUtf8DeclarationAndPreservesControlWhitespace()
    {
        var xml = Document(Row("1", " first&#xD;&#xA;second&#x9;third ", " 첫째&#xD;둘째 "))
            .Replace("encoding=\"UTF-8\"", "encoding=\"UTF-16\"");
        await File.WriteAllTextAsync(XmlPath, xml, Encoding.Unicode);
        var originalSource = XDocument.Parse(xml).Descendants("String").Single().Element("Source")!.Value;
        var read = await XTranslatorXmlImporter.ReadAllAsync(XmlPath, CancellationToken.None);
        var readRow = Assert.Single(read.Rows);
        Assert.Equal(originalSource, readRow.SourceText);
        Assert.Equal(originalSource, XElement.Parse(readRow.RawStringXml).Element("Source")!.Value);
        var info = await XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath, CancellationToken.None);
        var stored = Assert.Single(await _db.GetStringsForExportAsync(10, 0, CancellationToken.None));
        Assert.Equal(originalSource, XElement.Parse(stored.RawStringXml).Element("Source")!.Value);
        var output = Path.Combine(_root, "utf8.xml");
        await XTranslatorXmlExporter.ExportAsync(_db, info, output, CancellationToken.None);
        var parsed = XDocument.Load(output);
        Assert.Equal("UTF-8", parsed.Declaration!.Encoding);
        Assert.Equal(originalSource, parsed.Descendants("String").Single().Element("Source")!.Value);
        Assert.Equal(" 첫째\r둘째 ", parsed.Descendants("String").Single().Element("Dest")!.Value);
    }

    [Fact]
    public async Task Export_OldCorruptPrologIsSanitizedAndFailedWritePreservesOutput()
    {
        var info = await ImportAsync(Row("1", "source", "translated"));
        var output = Path.Combine(_root, "output.xml");
        await XTranslatorXmlExporter.ExportAsync(_db, info with { PrologLine = Document(Row("1", "source", "old")) }, output, CancellationToken.None);
        Assert.Single(XDocument.Load(output).Descendants("String"));
        var original = await File.ReadAllBytesAsync(output);
        await _db.UpdateStringTranslationAsync(1, "invalid\0", StringEntryStatus.Done, null, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => XTranslatorXmlExporter.ExportAsync(_db, info, output, CancellationToken.None));
        Assert.Equal(original, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    private async Task<XTranslatorXmlInfo> ImportAsync(string rows, bool preserve = false)
    {
        await File.WriteAllTextAsync(XmlPath, Document(rows));
        return await XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath, CancellationToken.None,
            preserveExistingTranslations: preserve, projectFactory: info => Project(info));
    }

    private ProjectInfo Project(XTranslatorXmlInfo info, string model = "model") => new(1, XmlPath,
        info.AddonName, BethesdaFranchise.ElderScrolls, info.SourceLang, info.DestLang, info.Version,
        info.HasBom, info.PrologLine, model, "base", "custom", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    // The exporter pages through the DB 500 rows at a time; rows past the first page were never tested.
    [Fact]
    public async Task Export_1201Rows_KeepsEveryRowInOrder()
    {
        var rows = Enumerable.Range(1, 1201).Select(i => Row(i.ToString(), "source " + i, "번역 " + i));
        await File.WriteAllTextAsync(XmlPath, Document(string.Concat(rows)), new UTF8Encoding(false));
        var info = await XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath, CancellationToken.None);
        var output = Path.Combine(_root, "output.xml");

        await XTranslatorXmlExporter.ExportAsync(_db, info, output, CancellationToken.None);

        var dests = XDocument.Load(output).Descendants("String").Select(e => e.Element("Dest")!.Value).ToList();
        Assert.Equal(Enumerable.Range(1, 1201).Select(i => "번역 " + i), dests);
    }

    [Fact]
    public async Task Export_AddsAMissingDestElement()
    {
        var row = "<String List=\"0\" Partial=\"1\"><EDID>Record</EDID><REC id=\"1\">FULL</REC><Source>Iron Sword</Source></String>";
        await File.WriteAllTextAsync(XmlPath, Document(row), new UTF8Encoding(false));
        var info = await XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath, CancellationToken.None);
        var stored = Assert.Single(await _db.GetStringsAsync(10, 0, CancellationToken.None));
        await _db.UpdateStringTranslationAsync(stored.Id, "철검", XTranslatorAi.Core.Models.StringEntryStatus.Edited, null, CancellationToken.None);
        var output = Path.Combine(_root, "output.xml");

        await XTranslatorXmlExporter.ExportAsync(_db, info, output, CancellationToken.None);

        Assert.Equal("철검", XDocument.Load(output).Descendants("Dest").Last().Value);
    }

    [Fact]
    public async Task CanceledExport_LeavesThePreviousFileAndNoTemporaryFile()
    {
        await File.WriteAllTextAsync(XmlPath, Document(Row("1", "Iron Sword", "철검")), new UTF8Encoding(false));
        var info = await XTranslatorXmlImporter.ImportToDbAsync(_db, XmlPath, CancellationToken.None);
        var output = Path.Combine(_root, "output.xml");
        await File.WriteAllTextAsync(output, "previous export");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => XTranslatorXmlExporter.ExportAsync(_db, info, output, canceled.Token));

        Assert.Equal("previous export", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.EnumerateFiles(_root, "output.xml.*.tmp"));
    }

    private static string Document(string rows) => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<SSTXMLRessources><Params><Addon>Test.esp</Addon><Source>english</Source><Dest>korean</Dest><Version>2</Version></Params><Content>"
        + rows + "</Content></SSTXMLRessources>";

    private static string Row(string id, string source, string dest) => $"<String List=\"0\" Partial=\"1\"><EDID>Record</EDID><REC id=\"{id}\">FULL</REC><Source>{source}</Source><Dest>{dest}</Dest></String>";
}
