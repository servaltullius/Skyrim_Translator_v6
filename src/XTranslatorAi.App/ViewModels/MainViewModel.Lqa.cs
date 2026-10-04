using System;
using System.Linq;
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

        // The check reads the rows' text; save a typed edit first so it judges what export will write.
        if (!await TryCommitPendingDestEditsAsync())
        {
            return;
        }

        IsLqaScanning = true;
        var db = _projectState.Db;
        try
        {
            StatusMessage = "품질 검사 중...";
            var issues = await BuildLqaIssuesAsync();

            // The scan runs off the UI thread, so the window stays usable; row Ids restart at 1 in every
            // project, so results of a project the user has since left must not be shown over the new one.
            if (!ReferenceEquals(db, _projectState.Db))
            {
                return;
            }

            LqaIssues.ReplaceAll(issues);
            LqaIssuesView.Refresh();

            if (LqaIssues.Count > 0 && SelectedLqaIssue == null)
            {
                SelectedLqaIssue = LqaIssues[0];
            }

            StatusMessage = LqaIssues.Count == 0
                ? "품질 검사: 문제를 찾지 못했습니다."
                : $"품질 검사: 오류 {CountSeverity("Error")}건, 경고 {CountSeverity("Warn")}건, 참고 {CountSeverity("Info")}건";
        }
        catch (Exception ex)
        {
            SetUserFacingError("품질 검사", ex);
        }
        finally
        {
            IsLqaScanning = false;
            ScanLqaCommand.NotifyCanExecuteChanged();
            ClearLqaCommand.NotifyCanExecuteChanged();
        }
    }

    private int CountSeverity(string severity)
        => LqaIssues.Count(i => string.Equals(i.Severity, severity, StringComparison.OrdinalIgnoreCase));

    private bool CanScanLqa() => IsProjectLoaded && !IsTranslating && !IsLqaScanning;

    [RelayCommand(CanExecute = nameof(CanClearLqa))]
    private void ClearLqa()
    {
        LqaIssues.Clear();
        SelectedLqaIssue = null;
        StatusMessage = "품질 검사 결과를 지웠습니다.";
        ClearLqaCommand.NotifyCanExecuteChanged();
    }

    private bool CanClearLqa() => IsProjectLoaded && !IsLqaScanning && LqaIssues.Count > 0;

    /// <summary>
    /// Quality-check issues and compare outputs describe rows of the project they came from. Row Ids restart at 1
    /// in every project, so after a switch a leftover issue selected an unrelated row of the new project with the
    /// same Id, and the compare slots showed the old row's translations under the new selection.
    /// </summary>
    private void ClearProjectResults()
    {
        SelectedLqaIssue = null;
        LqaIssues.Clear();
        LqaIssuesView.Refresh();
        ClearLqaCommand.NotifyCanExecuteChanged();
        ClearCompareOutputs();
    }

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
