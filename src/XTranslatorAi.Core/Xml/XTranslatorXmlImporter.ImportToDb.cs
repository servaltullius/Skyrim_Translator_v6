using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Xml;

public static partial class XTranslatorXmlImporter
{
    /// <summary>Replace project rows atomically. A parse, cancellation, or write failure preserves the previous project.</summary>
    public static async Task<XTranslatorXmlInfo> ImportToDbAsync(
        ProjectDb db,
        string xmlPath,
        CancellationToken cancellationToken,
        bool ignoreDestText = false,
        int batchSize = 500,
        bool preserveExistingTranslations = false,
        Func<XTranslatorXmlInfo, ProjectInfo>? projectFactory = null)
    {
        // Retained for source compatibility; all rows now belong to one transaction.
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var state = new XmlReadState(xmlPath);
        await db.ReplaceImportedStringsAsync(
            ReadRowsAsync(xmlPath, state, ignoreDestText, cancellationToken),
            preserveExistingTranslations,
            projectFactory == null ? null : () => projectFactory(state.Info),
            cancellationToken);
        return state.Info;
    }
}
