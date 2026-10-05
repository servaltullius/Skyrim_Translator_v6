using System;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// The translation editor writes typed text into the selected row, but XML/ESP export, reopening, the next run
/// and the quality check work from the project DB. Before this, an edit was saved only by "번역문 저장": moving to
/// another row, exporting, opening another file or closing kept the edited text in the grid while the DB (and the
/// exported file) still had the old translation. Edits are now saved, exactly as that button saves them, when the
/// user leaves the row and before anything that reads the DB or replaces the project.
/// </summary>
public partial class MainViewModel
{
    // Saves started by leaving a row run without the user waiting. Everything that reads the DB or disposes it
    // awaits them first, so a save neither lands after an export read the row nor runs against a disposed DB.
    private Task _leftRowCommits = Task.CompletedTask;

    partial void OnSelectedEntryChanged(StringEntryViewModel? oldValue, StringEntryViewModel? newValue)
    {
        KeepSelectedEntryInView(oldValue, newValue);
        FollowSelectedEntryInLqaIssues(newValue);

        // Resolve the DB now: when the project is being replaced, the row no longer belongs to the open
        // project and its edit must not be written into the new project's row with the same Id.
        if (oldValue is not { HasUnsavedDestEdit: true } || !TryGetOwningDb(oldValue, out var db))
        {
            return;
        }

        _leftRowCommits = CommitLeftRowAsync(_leftRowCommits, db, oldValue);
    }

    private async Task CommitLeftRowAsync(Task earlierCommits, ProjectDb db, StringEntryViewModel entry)
    {
        // Earlier saves never throw (failures are reported below), so the chain keeps the user's order.
        await earlierCommits;
        if (!entry.HasUnsavedDestEdit || !ReferenceEquals(db, _projectState.Db))
        {
            return;
        }

        try
        {
            await CommitDestEditAsync(db, entry, entry.DestText);
            StatusMessage = $"#{entry.OrderIndex} 행의 번역문 수정을 저장했습니다.";
        }
        catch (Exception ex)
        {
            // The row keeps its unsaved edit, so the next export, open or close tries to save it again.
            SetUserFacingError("번역문 저장", ex);
        }
    }

    /// <summary>
    /// Saves every edit of the open project that is still only in the editor. False when one could not be
    /// saved; the caller then stops instead of exporting, replacing or closing without it.
    /// </summary>
    private async Task<bool> TryCommitPendingDestEditsAsync()
    {
        await _leftRowCommits;
        var db = _projectState.Db;
        if (db == null)
        {
            return true;
        }

        foreach (var entry in Entries.Where(row => row.HasUnsavedDestEdit).ToList())
        {
            try
            {
                await CommitDestEditAsync(db, entry, entry.DestText);
            }
            catch (Exception ex)
            {
                SetUserFacingError("번역문 저장", ex);
                return false;
            }
        }

        // The prompt uses the context box as typed, but only its button saved it: closing or opening another file
        // dropped the edit and the next run went back to the old context. An emptied box is left to the clear button.
        var context = (ProjectContextPreview ?? "").Trim();
        if (context.Length > 0)
        {
            try
            {
                var saved = await db.TryGetProjectContextAsync(CancellationToken.None);
                if (!string.Equals(saved?.ContextText?.Trim(), context, StringComparison.Ordinal))
                {
                    await db.UpsertProjectContextAsync(context, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                SetUserFacingError("프로젝트 문맥 저장", ex);
                return false;
            }
        }

        return true;
    }

    private bool TryGetOwningDb(StringEntryViewModel entry, out ProjectDb db)
    {
        if (_projectState.Db is { } current && _projectState.TryGetById(entry.Id, out var owned) && ReferenceEquals(owned, entry))
        {
            db = current;
            return true;
        }

        db = null!;
        return false;
    }
}
