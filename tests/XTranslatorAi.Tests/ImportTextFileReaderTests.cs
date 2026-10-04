using System.Text;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// Glossary and TM imports assumed UTF-8. A TSV saved from Excel on Korean Windows is CP949, so every Korean
/// target came in as U+FFFD replacement characters and was imported as a forced term.
/// </summary>
public sealed class ImportTextFileReaderTests : IDisposable
{
    private const string Tsv = "Source\tTarget\nWhiterun\t화이트런\nDragonborn\t드래곤본\n";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-import-text-" + Guid.NewGuid().ToString("N"));

    public ImportTextFileReaderTests()
    {
        Directory.CreateDirectory(_root);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> Encodings => new() { "cp949", "utf-8", "utf-8-bom", "utf-16le-bom" };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Decode_ReadsKoreanInTheEncodingsUsersSave(string encoding)
    {
        Assert.Equal(Tsv, ImportTextFileReader.Decode(Encode(Tsv, encoding), "terms.tsv"));
    }

    [Fact]
    public void Decode_RefusesTextThatIsAlreadyBroken()
    {
        var error = Assert.Throws<ImportFileEncodingException>(
            () => ImportTextFileReader.Decode(Encoding.UTF8.GetBytes("Whiterun\t���\n"), "broken.tsv"));

        Assert.Contains("broken.tsv", error.Message);
        Assert.Contains("깨진 글자", error.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x20, 0x61 })] // windows-1252 "café a"
    [InlineData(new byte[] { 0x41, 0xFF, 0x20, 0x42 })] // CP949 reads a lone 0xFF as a private-use character
    [InlineData(new byte[] { 0x80, 0x31, 0x30 })] // and a lone 0x80 (€ in windows-1252) as a C1 control
    public void Decode_RefusesBytesThatAreNeitherUtf8NorCp949(byte[] bytes)
    {
        Assert.Throws<ImportFileEncodingException>(() => ImportTextFileReader.Decode(bytes, "odd.tsv"));
    }

    [Fact]
    public async Task GlossaryImport_ReadsAnExcelTsvFromKoreanWindows()
    {
        var path = Path.Combine(_root, "terms.tsv");
        await File.WriteAllBytesAsync(path, Encode(Tsv, "cp949"));
        var dbPath = Path.Combine(_root, "project.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(dbPath, CancellationToken.None);
            await new GlossaryImportService(new GlossaryFileService()).ImportFromFileAsync(db, path,
                new GlossaryImportService.GlossaryImportOptions(10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null),
                CancellationToken.None);

            Assert.Equal(new[] { "드래곤본", "화이트런" },
                (await db.GetGlossaryAsync(CancellationToken.None)).Select(e => e.TargetTerm).OrderBy(t => t, StringComparer.Ordinal));
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(dbPath);
        }
    }

    [Fact]
    public async Task TmImport_ReadsAnExcelTsvFromKoreanWindows()
    {
        var path = Path.Combine(_root, "tm.tsv");
        await File.WriteAllBytesAsync(path, Encode("Source\tTarget\r\nIron Sword\t철검\r\n", "cp949"));
        var global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(_root, "global"));
        try
        {
            var tm = new FranchiseTranslationMemoryService(global);
            Assert.Equal(1, await tm.ImportFromTsvAsync("english", "korean", path, CancellationToken.None));
            Assert.Contains(await tm.GetEntriesAsync("english", "korean", CancellationToken.None),
                e => e.SourceText == "Iron Sword" && e.DestText == "철검");
        }
        finally
        {
            await global.DisposeAsync();
            foreach (var db in Directory.GetFiles(_root, "*.sqlite", SearchOption.AllDirectories))
            {
                TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
            }
        }
    }

    private static byte[] Encode(string text, string encoding) => encoding switch
    {
        "cp949" => Encoding.GetEncoding(949).GetBytes(text),
        "utf-8" => new UTF8Encoding(false).GetBytes(text),
        "utf-8-bom" => new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(text)).ToArray(),
        "utf-16le-bom" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };
}
