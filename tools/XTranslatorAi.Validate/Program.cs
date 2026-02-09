using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Xml;

static long CountStringElements(string xmlPath)
{
    using var fs = File.OpenRead(xmlPath);
    using var reader = XmlReader.Create(fs, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
    long count = 0;
    while (reader.Read())
    {
        if (reader.NodeType == XmlNodeType.Element && reader.Name == "String")
        {
            count++;
        }
    }
    return count;
}

static bool HasUtf8Bom(string path)
{
    using var fs = File.OpenRead(path);
    Span<byte> buf = stackalloc byte[3];
    var read = fs.Read(buf);
    return read == 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF;
}

var input = args.Length > 0 ? args[0] : "LegacyoftheDragonborn_english_korean.xml";
if (!File.Exists(input))
{
    Console.Error.WriteLine($"Input not found: {input}");
    return 2;
}

var dbPath = args.Length > 1
    ? args[1]
    : Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(input)}.validate.sqlite");
var output = args.Length > 2 ? args[2] : Path.ChangeExtension(input, ".validate.out.xml");

var ct = CancellationToken.None;

await using var db = await ProjectDb.OpenOrCreateAsync(dbPath, ct);
await db.ClearStringsAsync(ct);

var info = await XTranslatorXmlImporter.ImportToDbAsync(db, input, ct);
var total = await db.GetStringCountAsync(ct);
Console.WriteLine($"Imported: {total} strings ({info.SourceLang} -> {info.DestLang}), addon={info.AddonName}");
Console.WriteLine($"Input BOM: {HasUtf8Bom(input)}  Prolog: {info.PrologLine}");

await XTranslatorXmlExporter.ExportAsync(db, info, output, ct);
var outCount = CountStringElements(output);
Console.WriteLine($"Exported: {output}");
Console.WriteLine($"Output BOM: {HasUtf8Bom(output)}  StringCount: {outCount}");

if (outCount != total)
{
    Console.Error.WriteLine($"Count mismatch: DB={total} XML={outCount}");
    return 1;
}

return 0;
