using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Xml;

public static partial class XTranslatorXmlImporter
{
    public static async Task<(XTranslatorXmlInfo Info, IReadOnlyList<XTranslatorXmlStringRow> Rows)> ReadAllAsync(
        string xmlPath, CancellationToken cancellationToken)
    {
        var state = new XmlReadState(xmlPath);
        var rows = new List<XTranslatorXmlStringRow>();
        await foreach (var row in ReadRowsAsync(xmlPath, state, ignoreDestText: false, cancellationToken))
        {
            rows.Add(row);
        }
        return (state.Info, rows);
    }
}
