using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    // The grid filters live on the translation text and status. Typing a fix into the editor under a search or
    // "보호 요소 불일치만" used to remove the row mid-edit, which cleared the selection and blanked the editor. The
    // selected row therefore stays visible until the selection moves (IsOpenInEditor turns false and
    // OnEntryPropertyChanged checks the row again), or until the user changes the filter itself.
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

    // Row properties the filter reads. A change can move the row into or out of the filtered grid.
    private static readonly HashSet<string> EntryFilterProperties = new(StringComparer.Ordinal)
    {
        nameof(StringEntryViewModel.Edid),
        nameof(StringEntryViewModel.Rec),
        nameof(StringEntryViewModel.SourceText),
        nameof(StringEntryViewModel.DestText),
        nameof(StringEntryViewModel.Status),
        nameof(StringEntryViewModel.ErrorMessage),
        nameof(StringEntryViewModel.IsOpenInEditor),
    };

    private readonly HashSet<StringEntryViewModel> _watchedEntries = new();
    private bool _entriesRefreshQueued;

    /// <summary>How many rows the search and status filter leave, next to the search box; it was never shown.</summary>
    public string VisibleEntrySummary
    {
        get
        {
            if (Entries.Count == 0)
            {
                return "";
            }

            var visible = EntriesView is System.Windows.Data.CollectionView view ? view.Count : TotalCount;
            return visible == Entries.Count ? $"전체 {Entries.Count}행" : $"표시 {visible} / 전체 {Entries.Count}행";
        }
    }

    private bool RecordEntryFilter(object obj)
    {
        var shown = EntryFilter(obj);
        if (obj is StringEntryViewModel entry)
        {
            entry.IsShownInEntriesView = shown;
        }

        return shown;
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _watchedEntries)
            {
                entry.PropertyChanged -= OnEntryPropertyChanged;
            }

            _watchedEntries.Clear();
            foreach (var entry in Entries)
            {
                Watch(entry);
            }

            return;
        }

        foreach (var entry in e.OldItems?.OfType<StringEntryViewModel>() ?? Enumerable.Empty<StringEntryViewModel>())
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
            _watchedEntries.Remove(entry);
        }

        foreach (var entry in e.NewItems?.OfType<StringEntryViewModel>() ?? Enumerable.Empty<StringEntryViewModel>())
        {
            Watch(entry);
        }
    }

    private void Watch(StringEntryViewModel entry)
    {
        if (_watchedEntries.Add(entry))
        {
            entry.PropertyChanged += OnEntryPropertyChanged;
        }
    }

    /// <summary>
    /// A row that stops matching (translated under a "대기" filter, or left after an edit) is taken out of the view
    /// on its own. A hidden row that starts matching needs a refresh, queued once for many such changes: without
    /// live filtering a refresh of 67,390 rows takes milliseconds.
    /// </summary>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not StringEntryViewModel entry || e.PropertyName == null || !EntryFilterProperties.Contains(e.PropertyName))
        {
            return;
        }

        var shouldShow = EntryFilter(entry);
        if (shouldShow == entry.IsShownInEntriesView)
        {
            return;
        }

        if (!shouldShow && EntriesView is IEditableCollectionView editable && editable.CurrentEditItem == null && !editable.IsAddingNew)
        {
            // Committing an edit filters that item again; it leaves the view and records its new filter result.
            editable.EditItem(entry);
            editable.CommitEdit();
            return;
        }

        QueueEntriesRefresh();
    }

    private void QueueEntriesRefresh()
    {
        if (_entriesRefreshQueued)
        {
            return;
        }

        _entriesRefreshQueued = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _entriesRefreshQueued = false;
            EntriesView.Refresh();
        }));
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
