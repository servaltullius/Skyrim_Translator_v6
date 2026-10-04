using System;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    // The grid filters live on the translation text and status. Typing a fix into the editor under a search or
    // "보호 요소 불일치만" used to remove the row mid-edit, which cleared the selection and blanked the editor. The
    // selected row therefore stays visible until the selection moves (IsOpenInEditor turns false and the live
    // filter checks the row again), or until the user changes the filter itself.
    private bool _isApplyingEntryFilterChange;

    partial void OnEntryFilterTextChanged(string value) => ApplyEntryFilterChange();

    partial void OnEntryFilterStatusChanged(string value) => ApplyEntryFilterChange();

    partial void OnEntryFilterTagsOnlyChanged(bool value)
    {
        if (!value && EntryFilterTagMismatchOnly)
        {
            EntryFilterTagMismatchOnly = false;
            return;
        }

        ApplyEntryFilterChange();
    }

    partial void OnEntryFilterTagMismatchOnlyChanged(bool value)
    {
        if (value && !EntryFilterTagsOnly)
        {
            EntryFilterTagsOnly = true;
            return;
        }

        ApplyEntryFilterChange();
    }

    private void ApplyEntryFilterChange()
    {
        _isApplyingEntryFilterChange = true;
        try
        {
            EntriesView.Refresh();
        }
        finally
        {
            _isApplyingEntryFilterChange = false;
        }

        // The new filter hides the open row, so it is no longer kept for the editor (leaving it saves its edit).
        if (SelectedEntry is { } selected && !MatchesEntryFilter(selected))
        {
            SelectedEntry = null;
        }
    }

    private static void KeepSelectedEntryInView(StringEntryViewModel? oldValue, StringEntryViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.IsOpenInEditor = false;
        }

        if (newValue != null)
        {
            newValue.IsOpenInEditor = true;
        }
    }

    private bool EntryFilter(object obj)
    {
        if (obj is not StringEntryViewModel entry)
        {
            return true;
        }

        if (entry.IsOpenInEditor && !_isApplyingEntryFilterChange)
        {
            return true;
        }

        return MatchesEntryFilter(entry);
    }

    private bool MatchesEntryFilter(StringEntryViewModel entry)
    {
        var statusFilter = (EntryFilterStatus ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != EntryStatusAll)
        {
            if (statusFilter == EntryStatusNeedsReview)
            {
                if (entry.Status != StringEntryStatus.Pending
                    && entry.Status != StringEntryStatus.Error
                    && entry.Status != StringEntryStatus.Edited)
                {
                    return false;
                }
            }
            else if (StringEntryStatusLabels.TryParse(statusFilter, out var status))
            {
                if (entry.Status != status)
                {
                    return false;
                }
            }
        }

        var q = (EntryFilterText ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(q) && !MatchesEntryQuery(entry, q))
        {
            return false;
        }

        if (EntryFilterTagMismatchOnly)
        {
            return HasTokenMismatch(entry.SourceText, entry.DestText);
        }

        if (EntryFilterTagsOnly)
        {
            return HasAnyUiTags(entry.SourceText);
        }

        return true;
    }

    private static bool MatchesEntryQuery(StringEntryViewModel entry, string q)
    {
        if (entry.TryMatchLocationQuery(q, out var locationMatch)) return locationMatch;
        return ContainsIgnoreCase(entry.OrderIndex.ToString(), q)
               || ContainsIgnoreCase(entry.Id.ToString(), q)
               || ContainsIgnoreCase(entry.Edid ?? "", q)
               || ContainsIgnoreCase(entry.Rec ?? "", q)
               || ContainsIgnoreCase(entry.SourceText, q)
               || ContainsIgnoreCase(entry.DestText, q)
               || ContainsIgnoreCase(entry.ErrorMessage ?? "", q);
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HasAnyUiTags(string text) => LqaScanner.HasProtectedText(text);

    private static bool HasTokenMismatch(string sourceText, string destText)
        => LqaScanner.HasTokenMismatch(sourceText, destText);
}
