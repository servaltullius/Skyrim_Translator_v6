using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using XTranslatorAi.Core.Data;

namespace XTranslatorAi.Core.Xml;

public static class XTranslatorXmlExporter
{
    /// @critical: Exports Project DB back to xTranslator XML (atomic write + backup).
    public static async Task ExportAsync(
        ProjectDb db,
        XTranslatorXmlInfo info,
        string outputPath,
        CancellationToken cancellationToken
    )
    {
        var tmpPath = outputPath + ".tmp";

        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using (var stream = File.Open(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await WriteXmlHeaderAsync(stream, utf8NoBom, info, cancellationToken);
            using var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = utf8NoBom,
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            });

            xmlWriter.WriteStartElement("SSTXMLRessources");

            WriteParams(xmlWriter, info);

            xmlWriter.WriteStartElement("Content");
            await WriteContentAsync(db, xmlWriter, cancellationToken);

            xmlWriter.WriteEndElement(); // Content
            xmlWriter.WriteEndElement(); // SSTXMLRessources
            xmlWriter.Flush();
        }

        AtomicReplace(tmpPath, outputPath);
    }

    private static async Task WriteXmlHeaderAsync(
        Stream stream,
        System.Text.UTF8Encoding utf8NoBom,
        XTranslatorXmlInfo info,
        CancellationToken cancellationToken
    )
    {
        if (info.HasBom)
        {
            await stream.WriteAsync(new byte[] { 0xEF, 0xBB, 0xBF }, cancellationToken);
        }

        var prologBytes = utf8NoBom.GetBytes(info.PrologLine + "\n");
        await stream.WriteAsync(prologBytes, cancellationToken);
    }

    private static void WriteParams(XmlWriter xmlWriter, XTranslatorXmlInfo info)
    {
        xmlWriter.WriteStartElement("Params");
        xmlWriter.WriteElementString("Addon", info.AddonName);
        xmlWriter.WriteElementString("Source", info.SourceLang);
        xmlWriter.WriteElementString("Dest", info.DestLang);
        xmlWriter.WriteElementString("Version", info.Version);
        xmlWriter.WriteEndElement(); // Params
    }

    private static async Task WriteContentAsync(ProjectDb db, XmlWriter xmlWriter, CancellationToken cancellationToken)
    {
        var total = await db.GetStringCountAsync(cancellationToken);
        const int pageSize = 500;
        for (var offset = 0; offset < total; offset += pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await db.GetStringsForExportAsync(pageSize, offset, cancellationToken);
            foreach (var row in rows)
            {
                var el = XElement.Parse(row.RawStringXml, LoadOptions.None);
                UpsertDestElement(el, row.DestText);
                el.WriteTo(xmlWriter);
            }
        }
    }

    private static void UpsertDestElement(XElement el, string? destText)
    {
        var destEl = el.Element("Dest");
        if (destEl == null)
        {
            destEl = new XElement("Dest");
            el.Add(destEl);
        }

        destEl.Value = destText ?? "";
    }

    private static void AtomicReplace(string tmpPath, string outputPath)
    {
        var backupPath = outputPath + ".bak";
        if (File.Exists(outputPath))
        {
            File.Copy(outputPath, backupPath, overwrite: true);
        }
        File.Move(tmpPath, outputPath, overwrite: true);
    }
}
