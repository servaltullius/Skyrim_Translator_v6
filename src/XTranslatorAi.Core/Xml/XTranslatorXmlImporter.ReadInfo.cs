using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Xml;

public static partial class XTranslatorXmlImporter
{
    /// @critical: Reads xTranslator XML header/params.
    public static async Task<XTranslatorXmlInfo> ReadInfoAsync(string xmlPath, CancellationToken cancellationToken)
    {
        var (hasBom, prologLine) = ReadBomAndProlog(xmlPath);

        await using var stream = File.OpenRead(xmlPath);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
        });

        string addon = "";
        string srcLang = "english";
        string dstLang = "korean";
        string version = "2";

        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Name != "Params")
            {
                continue;
            }

            var paramsEl = await XElement.ReadFromAsync(reader, cancellationToken) as XElement;
            ApplyParamsFromElement(paramsEl, ref addon, ref srcLang, ref dstLang, ref version);
            break;
        }

        return new XTranslatorXmlInfo(
            AddonName: addon,
            SourceLang: srcLang,
            DestLang: dstLang,
            Version: version,
            HasBom: hasBom,
            PrologLine: prologLine
        );
    }
}
