using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace XTranslatorAi.Core.Xml;

public static partial class XTranslatorXmlImporter
{
    private sealed class XmlReadState
    {
        private readonly bool _hasBom;
        private readonly string _prolog;
        public string Addon = "";
        public string SourceLang = "english";
        public string DestLang = "korean";
        public string Version = "2";

        public XmlReadState(string path) => (_hasBom, _prolog) = ReadBomAndProlog(path);
        public XTranslatorXmlInfo Info => new(Addon, SourceLang, DestLang, Version, _hasBom, _prolog);
    }

    private static async IAsyncEnumerable<XTranslatorXmlStringRow> ReadRowsAsync(
        string path,
        XmlReadState state,
        bool ignoreDestText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        });
        var rootSeen = false;
        var contentSeen = false;
        var contentDepth = -1;
        var orderIndex = 0;
        // ReadFromAsync leaves the reader at the NEXT node. Process that node before advancing again.
        await reader.ReadAsync();
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0)
            {
                if (rootSeen || reader.Name != "SSTXMLRessources" || reader.NamespaceURI.Length != 0)
                    throw XTranslatorXmlFormatError.Create("xTranslator XML이 아닙니다(최상위 요소가 SSTXMLRessources가 아님). xTranslator에서 내보낸 XML을 여세요.");
                rootSeen = true;
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.Name == "Params")
            {
                var element = (XElement)await XElement.ReadFromAsync(reader, cancellationToken);
                ApplyParamsFromElement(element, ref state.Addon, ref state.SourceLang, ref state.DestLang, ref state.Version);
                continue;
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.Name == "Content")
            {
                if (contentSeen) throw XTranslatorXmlFormatError.Create("xTranslator XML에 Content 요소가 둘 이상 있습니다. 파일을 xTranslator에서 다시 내보내세요.");
                contentSeen = true;
                contentDepth = reader.IsEmptyElement ? -1 : reader.Depth;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == contentDepth && reader.Name == "Content")
            {
                contentDepth = -1;
            }
            else if (contentDepth >= 0 && reader.NodeType == XmlNodeType.Element
                     && reader.Depth == contentDepth + 1)
            {
                if (reader.Name != "String" || reader.NamespaceURI.Length != 0)
                    throw XTranslatorXmlFormatError.Create("xTranslator XML의 Content 안에 String이 아닌 요소가 있습니다. 파일을 xTranslator에서 다시 내보내세요.");
                var element = (XElement)await XElement.ReadFromAsync(reader, cancellationToken);
                if (element.Element("Source") == null)
                    throw XTranslatorXmlFormatError.Create("xTranslator XML의 String 요소에 Source가 없습니다. 파일을 xTranslator에서 다시 내보내세요.");
                yield return TryParseStringRow(element, orderIndex++, ignoreDestText)!;
                continue;
            }
            await reader.ReadAsync();
        }
        if (!rootSeen || !contentSeen)
            throw XTranslatorXmlFormatError.Create("xTranslator XML이 아니거나 문자열 목록(SSTXMLRessources/Content)이 없습니다. xTranslator에서 내보낸 XML을 여세요.");
    }
}
