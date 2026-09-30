using System;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    partial void OnEntryFilterTextChanged(string value) => EntriesView.Refresh();

    partial void OnEntryFilterStatusChanged(string value) => EntriesView.Refresh();

    partial void OnEntryFilterTagsOnlyChanged(bool value)
    {
        if (!value && EntryFilterTagMismatchOnly)
        {
            EntryFilterTagMismatchOnly = false;
            return;
        }

        EntriesView.Refresh();
    }

    partial void OnEntryFilterTagMismatchOnlyChanged(bool value)
    {
        if (value && !EntryFilterTagsOnly)
        {
            EntryFilterTagsOnly = true;
            return;
        }

        EntriesView.Refresh();
    }

    private bool EntryFilter(object obj)
    {
        if (obj is not StringEntryViewModel entry)
        {
            return true;
        }

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
            else if (Enum.TryParse<StringEntryStatus>(statusFilter, ignoreCase: true, out var status))
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
