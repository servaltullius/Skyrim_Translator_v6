using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// Keyboard review: the only shortcuts were Delete keys in three grids, so reviewing meant clicking a row, the editor
/// and the next row each time. Ctrl+Enter saves and moves to the next row of the filtered grid, F8/Shift+F8 jump to
/// the next/previous row still pending or in error (MainWindow maps the keys).
/// </summary>
public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanNavigateEntries))]
    private async Task SaveAndNextAsync()
    {
        var next = FindEntryInView(SelectedEntry, forward: true, _ => true);
        if (next == null)
        {
            // Last row: nothing to move to, so save in place.
            await SaveSelectedDestCommand.ExecuteAsync(null);
            return;
        }

        // Leaving a row saves its edit (see KeepSelectedEntryInView and the selection change).
        SelectedEntry = next;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateEntries))]
    private void NextUnfinished() => SelectUnfinished(forward: true);

    [RelayCommand(CanExecute = nameof(CanNavigateEntries))]
    private void PreviousUnfinished() => SelectUnfinished(forward: false);

    private bool CanNavigateEntries() => IsProjectLoaded && !IsTranslating;

    private void SelectUnfinished(bool forward)
    {
        var target = FindEntryInView(SelectedEntry, forward,
            e => e.Status is StringEntryStatus.Pending or StringEntryStatus.Error);
        if (target == null)
        {
            StatusMessage = forward ? "아래쪽에 대기·오류 행이 없습니다." : "위쪽에 대기·오류 행이 없습니다.";
            return;
        }

        SelectedEntry = target;
    }

    /// <summary>The next (or previous) row of the filtered, sorted grid after <paramref name="from"/> that matches.</summary>
    private StringEntryViewModel? FindEntryInView(StringEntryViewModel? from, bool forward, System.Func<StringEntryViewModel, bool> match)
    {
        var rows = new List<StringEntryViewModel>();
        foreach (var item in EntriesView)
        {
            if (item is StringEntryViewModel row)
            {
                rows.Add(row);
            }
        }

        var start = from == null ? -1 : rows.IndexOf(from);
        if (!forward && start < 0)
        {
            start = rows.Count;
        }

        for (var i = forward ? start + 1 : start - 1; i >= 0 && i < rows.Count; i += forward ? 1 : -1)
        {
            if (!ReferenceEquals(rows[i], from) && match(rows[i]))
            {
                return rows[i];
            }
        }

        return null;
    }
}
