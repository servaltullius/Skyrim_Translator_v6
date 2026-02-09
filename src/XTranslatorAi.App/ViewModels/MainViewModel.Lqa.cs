using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanScanLqa))]
    private async Task ScanLqaAsync()
    {
        if (!IsProjectLoaded)
        {
            return;
        }

        IsLqaScanning = true;
        try
        {
            StatusMessage = "LQA: scanning...";
            var issues = await BuildLqaIssuesAsync();
            LqaIssues.ReplaceAll(issues);
            LqaIssuesView.Refresh();

            if (LqaIssues.Count > 0 && SelectedLqaIssue == null)
            {
                SelectedLqaIssue = LqaIssues[0];
            }

            StatusMessage = $"LQA: {LqaIssues.Count} issues.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("LQA", ex);
        }
        finally
        {
            IsLqaScanning = false;
            ScanLqaCommand.NotifyCanExecuteChanged();
            ClearLqaCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScanLqa() => IsProjectLoaded && !IsTranslating && !IsLqaScanning;

    [RelayCommand(CanExecute = nameof(CanClearLqa))]
    private void ClearLqa()
    {
        LqaIssues.Clear();
        SelectedLqaIssue = null;
        StatusMessage = "LQA: cleared.";
        ClearLqaCommand.NotifyCanExecuteChanged();
    }

    private bool CanClearLqa() => IsProjectLoaded && !IsLqaScanning && LqaIssues.Count > 0;

    partial void OnLqaFilterTextChanged(string value) => LqaIssuesView.Refresh();

    private bool LqaIssueFilter(object obj)
    {
        if (obj is not LqaIssueViewModel issue)
        {
            return true;
        }

        var q = (LqaFilterText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return issue.MatchesQuery(q);
    }

    partial void OnSelectedLqaIssueChanged(LqaIssueViewModel? value)
    {
        if (value == null)
        {
            return;
        }

        if (_projectState.TryGetById(value.Id, out var entry))
        {
            SelectedEntry = entry;
        }
    }
}
