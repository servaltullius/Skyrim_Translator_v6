using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private IReadOnlyList<(long Id, string Source, MaskedText Mask)> GetDuplicateRows(long canonicalId)
    {
        if (_duplicateRowsByCanonicalId == null
            || !_duplicateRowsByCanonicalId.TryGetValue(canonicalId, out var rows)
            || rows.Count == 0)
        {
            return Array.Empty<(long Id, string Source, MaskedText Mask)>();
        }

        return rows;
    }

    private async Task UpdateDuplicateStatusesAsync(
        long canonicalId,
        StringEntryStatus status,
        string? errorMessage,
        Func<long, StringEntryStatus, string, Task>? onRowUpdated,
        CancellationToken cancellationToken
    )
    {
        var duplicates = GetDuplicateRows(canonicalId);
        if (duplicates.Count == 0)
        {
            return;
        }

        var ids = new long[duplicates.Count];
        for (var i = 0; i < duplicates.Count; i++)
        {
            ids[i] = duplicates[i].Id;
        }

        await _db.UpdateStringStatusesAsync(ids, status, errorMessage, cancellationToken);

        if (onRowUpdated != null)
        {
            var message = errorMessage ?? "";
            for (var i = 0; i < ids.Length; i++)
            {
                NotifyRowUpdated(onRowUpdated, ids[i], status, message);
            }
        }
    }
}

