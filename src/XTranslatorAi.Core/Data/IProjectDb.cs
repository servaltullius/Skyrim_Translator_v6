using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Data;

public interface IProjectDb
{
    Task<IReadOnlyList<GlossaryEntry>> GetGlossaryAsync(CancellationToken cancellationToken);

    Task<bool> TryInsertGlossaryIfMissingAsync(GlossaryUpsertRequest request, CancellationToken cancellationToken);

    Task UpdateStringTranslationAsync(
        long id,
        string destText,
        StringEntryStatus status,
        string? errorMessage,
        CancellationToken cancellationToken
    );

    Task UpdateStringTranslationsAsync(
        IReadOnlyList<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)> rows,
        CancellationToken cancellationToken
    );

    Task UpdateStringStatusAsync(
        long id,
        StringEntryStatus status,
        string? errorMessage,
        CancellationToken cancellationToken
    );

    Task UpdateStringStatusesAsync(
        IReadOnlyList<long> ids,
        StringEntryStatus status,
        string? errorMessage,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyDictionary<long, (long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status, string? DialogueScope)>>
        GetStringTranslationContextsByIdsAsync(
            IReadOnlyList<long> ids,
            CancellationToken cancellationToken
        );

    Task<IReadOnlyDictionary<long, StringEntryStatus>>
        GetStringStatusesByIdsAsync(
            IReadOnlyList<long> ids,
            CancellationToken cancellationToken
        );

    Task<IReadOnlyDictionary<string, string>> GetTranslationMemoryAsync(
        string sourceLang,
        string destLang,
        CancellationToken cancellationToken
    );

    Task UpsertStringNoteAsync(
        long stringId,
        string kind,
        string message,
        CancellationToken cancellationToken
    );

    Task DeleteStringNoteAsync(
        long stringId,
        string kind,
        CancellationToken cancellationToken
    );

    Task ResetInProgressToPendingAsync(CancellationToken cancellationToken);

    /// <summary>Earlier translations of the current rows, by row id (see <see cref="ProjectDb.ReplacePreviousTranslationsAsync"/>).</summary>
    Task<IReadOnlyDictionary<long, string>> GetPreviousTranslationsByStringIdAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());

    /// <summary>Source and translation of the rows already translated (Done or Edited).</summary>
    Task<IReadOnlyList<(string SourceText, string DestText)>> GetTranslatedPairsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(string SourceText, string DestText)>>(new List<(string, string)>());

    Task<IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)>>
        GetStringSourceContextsByStatusAsync(
            IReadOnlyList<StringEntryStatus> statuses,
            CancellationToken cancellationToken
        );
}
