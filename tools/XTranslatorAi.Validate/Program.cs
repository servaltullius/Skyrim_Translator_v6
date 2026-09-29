using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Xml;

// Usage: XTranslatorAi.Validate input.xml [validation.sqlite] [output.xml] [--existing-db]
// --existing-db exports an existing project without importing over its intentional Dest edits.
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (args.Any(a => a.StartsWith("--", StringComparison.Ordinal) && a != "--existing-db") || positional.Length > 3)
{
    Console.Error.WriteLine("Usage: XTranslatorAi.Validate input.xml [validation.sqlite] [output.xml] [--existing-db]");
    return 2;
}
var input = Path.GetFullPath(positional.Length > 0 ? positional[0] : "LegacyoftheDragonborn_english_korean.xml");
var existingDb = args.Contains("--existing-db", StringComparer.Ordinal);
var dbPath = Path.GetFullPath(positional.Length > 1 ? positional[1]
    : Path.Combine(Path.GetTempPath(), $"xtranslator-validate-{Guid.NewGuid():N}.sqlite"));
var output = Path.GetFullPath(positional.Length > 2 ? positional[2] : Path.ChangeExtension(input, ".validate.out.xml"));
if (!File.Exists(input) || (existingDb && (positional.Length < 2 || !File.Exists(dbPath))))
{
    Console.Error.WriteLine("The input XML must exist. --existing-db also requires an existing database path.");
    return 2;
}
if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase)
    || string.Equals(dbPath, input, StringComparison.OrdinalIgnoreCase)
    || string.Equals(dbPath, output, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Input XML, validation database and output XML must use separate paths.");
    return 2;
}

try
{
    // Deliberately use an independent XML reader: the importer must not validate its own omissions.
    var original = ReadDocument(input);
    var originalRows = ReadRows(original);
    var ct = CancellationToken.None;
    await using var db = await ProjectDb.OpenOrCreateAsync(dbPath, ct);
    var info = existingDb
        ? await XTranslatorXmlImporter.ReadInfoAsync(input, ct)
        : await XTranslatorXmlImporter.ImportToDbAsync(db, input, ct);
    var failures = new List<string>();
    var databaseCount = await db.GetStringCountAsync(ct);
    if (databaseCount != originalRows.Count)
        failures.Add($"Input/DB count mismatch: input={originalRows.Count}, DB={databaseCount}");

    var databaseRows = new List<(long Id, int OrderIndex, string DestText, string RawStringXml)>();
    var storedFields = new List<XTranslatorAi.Core.Models.StringEntry>();
    const int pageSize = 500;
    for (var offset = 0; offset < databaseCount; offset += pageSize)
    {
        databaseRows.AddRange(await db.GetStringsForExportAsync(pageSize, offset, ct));
        storedFields.AddRange(await db.GetStringsAsync(pageSize, offset, ct));
    }

    // Check imported IDs, attributes, source and every untouched child against the original.
    // With an existing project, only Dest contents may intentionally differ.
    for (var i = 0; i < Math.Min(originalRows.Count, databaseRows.Count); i++)
    {
        var stored = XElement.Parse(databaseRows[i].RawStringXml, LoadOptions.PreserveWhitespace);
        CheckRow(originalRows[i], stored, $"input/DB row {i + 1}", failures, compareDestination: !existingDb);
        var fields = storedFields[i];
        if (fields.SourceText != (originalRows[i].Element("Source")?.Value ?? "")
            || fields.Edid != originalRows[i].Element("EDID")?.Value
            || fields.Rec != originalRows[i].Element("REC")?.Value
            || fields.ListAttr != (string?)originalRows[i].Attribute("List")
            || fields.PartialAttr != (string?)originalRows[i].Attribute("Partial"))
            failures.Add($"input/DB row {i + 1}: stored Source/EDID/REC/List/Partial fields differ");
        var storedAttributes = JsonSerializer.Deserialize<Dictionary<string, string>>(fields.AttributesJson ?? "{}")
            ?? new Dictionary<string, string>();
        var expectedAttributes = originalRows[i].Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
        if (!storedAttributes.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(
            expectedAttributes.OrderBy(p => p.Key, StringComparer.Ordinal)))
            failures.Add($"input/DB row {i + 1}: stored AttributesJson differs");
        if (databaseRows[i].OrderIndex != i)
            failures.Add($"DB row {i + 1}: OrderIndex={databaseRows[i].OrderIndex}, expected {i}");
        if (!existingDb && databaseRows[i].DestText != (originalRows[i].Element("Dest")?.Value ?? ""))
            failures.Add($"input/DB row {i + 1}: DestText differs");
    }

    if (failures.Count == 0)
    {
        await XTranslatorXmlExporter.ExportAsync(db, info, output, ct);
        var exported = ReadDocument(output);
        var exportedRows = ReadRows(exported);
        if (exportedRows.Count != originalRows.Count || exportedRows.Count != databaseCount)
            failures.Add($"Export count mismatch: input={originalRows.Count}, DB={databaseCount}, output={exportedRows.Count}");
        foreach (var name in new[] { "Addon", "Source", "Dest", "Version" })
        {
            if ((original.Root!.Element("Params")?.Element(name)?.Value ?? "")
                != (exported.Root!.Element("Params")?.Element(name)?.Value ?? ""))
                failures.Add($"Params/{name} differs");
        }
        if (HasUtf8Bom(input) != HasUtf8Bom(output))
            failures.Add("UTF-8 BOM differs");
        if (!string.Equals(exported.Declaration?.Encoding, "UTF-8", StringComparison.OrdinalIgnoreCase))
            failures.Add("Output declaration does not describe UTF-8");
        for (var i = 0; i < Math.Min(originalRows.Count, exportedRows.Count); i++)
        {
            CheckRow(originalRows[i], exportedRows[i], $"input/output row {i + 1}", failures, compareDestination: false);
            if (i < databaseRows.Count && (exportedRows[i].Element("Dest")?.Value ?? "") != databaseRows[i].DestText)
                failures.Add($"DB/output row {i + 1}: Dest differs from the saved DB value");
        }
    }

    if (failures.Count > 0)
    {
        foreach (var failure in failures.Take(50)) Console.Error.WriteLine(failure);
        if (failures.Count > 50) Console.Error.WriteLine($"... {failures.Count - 50} more mismatches.");
        return 1;
    }
    Console.WriteLine($"PASS: {originalRows.Count} rows; input/DB/output counts, row order, identifiers, attributes, Source and saved Dest verified.");
    Console.WriteLine($"Output: {output}");
    Console.WriteLine($"Database: {dbPath}{(existingDb ? " (existing translations retained)" : "")}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Validation failed: {ex.Message}");
    return 1;
}

static XDocument ReadDocument(string path)
{
    using var reader = XmlReader.Create(path, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    });
    var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    if (document.Root?.Name != "SSTXMLRessources")
        throw new InvalidDataException("Expected SSTXMLRessources root.");
    return document;
}

static IReadOnlyList<XElement> ReadRows(XDocument document)
{
    var content = document.Root!.Elements("Content").Single();
    if (content.Elements().Any(e => e.Name != "String"))
        throw new InvalidDataException("Content has unsupported elements.");
    return content.Elements("String").ToArray();
}

static void CheckRow(XElement expected, XElement actual, string label, List<string> failures, bool compareDestination)
{
    // XML attribute order and indentation are irrelevant. All attribute names/values matter.
    if (!Attributes(expected).SequenceEqual(Attributes(actual)))
        failures.Add($"{label}: String attributes differ");
    foreach (var name in new[] { "EDID", "REC", "Source" })
    {
        var before = expected.Element(name);
        var after = actual.Element(name);
        if (before?.Value != after?.Value || (before != null && after != null
            && !Attributes(before).SequenceEqual(Attributes(after))))
            failures.Add($"{label}: {name} value or attributes differ");
    }
    var expectedDest = expected.Element("Dest");
    var actualDest = actual.Element("Dest");
    if (!Attributes(expectedDest).SequenceEqual(Attributes(actualDest)))
        failures.Add($"{label}: Dest attributes differ");
    if (compareDestination && (expectedDest?.Value ?? "") != (actualDest?.Value ?? ""))
        failures.Add($"{label}: Dest differs");
    // Preserve additional xTranslator row data too, beyond the well-known fields.
    var expectedUntouched = new XElement(expected);
    var actualUntouched = new XElement(actual);
    expectedUntouched.Elements("Dest").Remove();
    actualUntouched.Elements("Dest").Remove();
    // Only the String container's element separators are formatting. Keep every
    // field's text, including whitespace-only Source, nested content and CDATA.
    // Check inherited xml:space before cloning loses the original ancestors.
    if (CanIgnoreRowIndentation(expected) && CanIgnoreRowIndentation(actual))
    {
        RemoveRowIndentation(expectedUntouched);
        RemoveRowIndentation(actualUntouched);
    }
    CanonicalizeAttributes(expectedUntouched);
    CanonicalizeAttributes(actualUntouched);
    if (!XNode.DeepEquals(expectedUntouched, actualUntouched))
        failures.Add($"{label}: non-Dest row structure differs");
}

static bool CanIgnoreRowIndentation(XElement row)
{
    var space = row.AncestorsAndSelf()
        .Select(element => (string?)element.Attribute(XNamespace.Xml + "space"))
        .FirstOrDefault(value => value != null);
    if (space == "preserve") return false;
    return row.Nodes().OfType<XText>().All(text => text is not XCData && IsXmlWhitespace(text.Value));
}

static void RemoveRowIndentation(XElement row) => row.Nodes().OfType<XText>()
    .Where(text => text is not XCData && IsXmlWhitespace(text.Value)).Remove();

static bool IsXmlWhitespace(string value) => value.All(character => character is ' ' or '\t' or '\r' or '\n');

static IEnumerable<string> Attributes(XElement? element) => element == null
    ? Enumerable.Empty<string>()
    : element.Attributes().Select(a => a.Name + "=" + a.Value).OrderBy(a => a, StringComparer.Ordinal);

static void CanonicalizeAttributes(XElement root)
{
    foreach (var element in root.DescendantsAndSelf())
    {
        var sorted = element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToArray();
        element.RemoveAttributes();
        element.Add(sorted);
    }
}

static bool HasUtf8Bom(string path)
{
    using var stream = File.OpenRead(path);
    Span<byte> bytes = stackalloc byte[3];
    return stream.Read(bytes) == 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}
