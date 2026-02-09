using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Xml;

public static partial class XTranslatorXmlImporter
{
    private sealed class ImportState
    {
        public ImportState(bool ignoreDestText, int batchSize)
        {
            IgnoreDestText = ignoreDestText;
            BatchSize = batchSize;
            Batch = new List<XTranslatorXmlStringRow>(capacity: batchSize);
        }

        public bool IgnoreDestText { get; }
        public int BatchSize { get; }
        public string Addon = "";
        public string SourceLang = "english";
        public string DestLang = "korean";
        public string Version = "2";
        public int OrderIndex;
        public List<XTranslatorXmlStringRow> Batch { get; }
    }

    /// @critical: Imports xTranslator XML into Project DB (streaming).
    public static async Task<XTranslatorXmlInfo> ImportToDbAsync(
        Data.ProjectDb db,
        string xmlPath,
        CancellationToken cancellationToken,
        bool ignoreDestText = false,
        int batchSize = 500
    )
    {
        var (hasBom, prologLine) = ReadBomAndProlog(xmlPath);

        await using var stream = File.OpenRead(xmlPath);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
        });

        var state = new ImportState(ignoreDestText, batchSize);
        await ImportRowsFromReaderAsync(
            db,
            reader,
            cancellationToken,
            state
        );

        return new XTranslatorXmlInfo(
            AddonName: state.Addon,
            SourceLang: state.SourceLang,
            DestLang: state.DestLang,
            Version: state.Version,
            HasBom: hasBom,
            PrologLine: prologLine
        );
    }

    private static async Task ImportRowsFromReaderAsync(
        Data.ProjectDb db,
        XmlReader reader,
        CancellationToken cancellationToken,
        ImportState state
    )
    {
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (await TryReadParamsAsync(reader, cancellationToken, state))
            {
                continue;
            }

            var row = await TryReadStringRowAsync(reader, cancellationToken, state);
            if (row == null)
            {
                continue;
            }

            state.Batch.Add(row);
            state.OrderIndex++;

            await FlushIfNeededAsync(db, state.Batch, state.BatchSize, cancellationToken);
        }

        if (state.Batch.Count > 0)
        {
            await FlushAsync(db, state.Batch, cancellationToken);
        }
    }

    private static async Task<bool> TryReadParamsAsync(
        XmlReader reader,
        CancellationToken cancellationToken,
        ImportState state
    )
    {
        if (reader.Name != "Params")
        {
            return false;
        }

        var paramsEl = await XElement.ReadFromAsync(reader, cancellationToken) as XElement;
        ApplyParamsFromElement(paramsEl, ref state.Addon, ref state.SourceLang, ref state.DestLang, ref state.Version);
        return true;
    }

    private static async Task<XTranslatorXmlStringRow?> TryReadStringRowAsync(
        XmlReader reader,
        CancellationToken cancellationToken,
        ImportState state
    )
    {
        if (reader.Name != "String")
        {
            return null;
        }

        var el = await XElement.ReadFromAsync(reader, cancellationToken) as XElement;
        return TryParseStringRow(el, state.OrderIndex, state.IgnoreDestText);
    }

    private static async Task FlushIfNeededAsync(
        Data.ProjectDb db,
        List<XTranslatorXmlStringRow> batch,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        if (batch.Count >= batchSize)
        {
            await FlushAsync(db, batch, cancellationToken);
        }
    }

    private static async Task FlushAsync(Data.ProjectDb db, List<XTranslatorXmlStringRow> batch, CancellationToken cancellationToken)
    {
        await db.BulkInsertStringsAsync(
            batch.ConvertAll(
                r =>
                    (
                        r.OrderIndex,
                        r.ListAttr,
                        r.PartialAttr,
                        r.AttributesJson,
                        r.Edid,
                        r.Rec,
                        r.SourceText,
                        r.DestText,
                        r.Status,
                        r.RawStringXml
                    )
            ),
            cancellationToken
        );
        batch.Clear();
    }
}
