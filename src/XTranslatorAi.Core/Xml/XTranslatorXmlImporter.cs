using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Xml;

public static partial class XTranslatorXmlImporter
{
    private static void ApplyParamsFromElement(
        XElement? paramsEl,
        ref string addon,
        ref string srcLang,
        ref string dstLang,
        ref string version
    )
    {
        addon = paramsEl?.Element("Addon")?.Value ?? addon;
        srcLang = paramsEl?.Element("Source")?.Value ?? srcLang;
        dstLang = paramsEl?.Element("Dest")?.Value ?? dstLang;
        version = paramsEl?.Element("Version")?.Value ?? version;
    }

    private static XTranslatorXmlStringRow? TryParseStringRow(XElement? el, int orderIndex, bool ignoreDestText)
    {
        if (el == null)
        {
            return null;
        }

        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in el.Attributes())
        {
            attrs[a.Name.LocalName] = a.Value;
        }

        attrs.TryGetValue("List", out var listAttr);
        attrs.TryGetValue("Partial", out var partialAttr);

        var edid = el.Element("EDID")?.Value;
        var rec = el.Element("REC")?.Value;
        var sourceText = el.Element("Source")?.Value ?? "";
        var destText = ignoreDestText ? "" : (el.Element("Dest")?.Value ?? "");

        var status = ignoreDestText
            ? StringEntryStatus.Pending
            : string.IsNullOrWhiteSpace(destText) || string.Equals(sourceText.Trim(), destText.Trim(), StringComparison.Ordinal)
                ? StringEntryStatus.Pending
                : StringEntryStatus.Skipped;

        var raw = SerializeStringElement(el);

        return new XTranslatorXmlStringRow(
            OrderIndex: orderIndex,
            ListAttr: listAttr,
            PartialAttr: partialAttr,
            AttributesJson: Data.ProjectDb.ToAttributesJson(attrs),
            Edid: edid,
            Rec: rec,
            SourceText: sourceText,
            DestText: destText,
            Status: status,
            RawStringXml: raw
        );
    }

    private static string SerializeStringElement(XElement element)
    {
        // The stored fragment is parsed again on export. Literal CR characters would
        // be normalized to LF then, so preserve them as character references here.
        var buffer = new StringBuilder();
        using var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
        });
        element.WriteTo(writer);
        writer.Flush();
        return buffer.ToString();
    }

    private static (bool HasBom, string PrologLine) ReadBomAndProlog(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> buf = stackalloc byte[3];
        var read = fs.Read(buf);
        var hasBom = read == 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF;

        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        if (reader.Read() && reader.NodeType == XmlNodeType.XmlDeclaration)
        {
            // Output is UTF-8 even if the input was UTF-16. Never retain body text from the first line.
            return (hasBom, new XDeclaration("1.0", "UTF-8", reader.GetAttribute("standalone")).ToString());
        }

        return (hasBom, "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
    }
}
