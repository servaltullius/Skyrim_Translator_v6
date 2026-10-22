using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    // The project's "확인함" marks as the last scan read them, kept up to date by ToggleLqaReviewedAsync.
    private Dictionary<(long StringId, string Code), string> _lqaReviewedMarks = new();

    /// <summary>
    /// Marks the selected results "확인함" or takes the mark off. A result left as it is on purpose (Serana speaking
    /// politely to her father, a mod name kept in English) came back on every scan, among the ones still to fix.
    /// The mark holds the row's translation; when the translation changes the result is shown again. Results that
    /// are all marked are unmarked; otherwise all are marked.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleLqaReviewed))]
    private async Task ToggleLqaReviewedAsync(IList? selectedIssues)
    {
        var picked = selectedIssues?.OfType<LqaIssueViewModel>().ToList() is { Count: > 0 } selected
            ? selected
            : SelectedLqaIssue is { } one ? new List<LqaIssueViewModel> { one } : new List<LqaIssueViewModel>();
        var keys = picked.Select(issue => (issue.Id, issue.Code)).ToHashSet();
        var db = _projectState.Db;
        if (db == null || keys.Count == 0)
        {
            StatusMessage = "확인함으로 표시할 품질 검사 결과를 먼저 선택하세요.";
            return;
        }

        // The mark is for the saved translation: a typed edit is saved first (and the row checked again).
        if (!await TryCommitPendingDestEditsAsync() || !ReferenceEquals(db, _projectState.Db))
        {
            return;
        }

        var issues = LqaIssues.Where(issue => keys.Contains((issue.Id, issue.Code))).ToList();
        if (issues.Count == 0)
        {
            StatusMessage = "선택한 결과는 번역문을 저장하면서 없어졌습니다.";
            return;
        }

        var mark = !issues.All(issue => issue.IsReviewed);
        var results = issues
            .Select(issue => (issue.Id, issue.Code, Dest: _projectState.TryGetById(issue.Id, out var entry) ? entry.DestText ?? "" : null))
            .Where(result => result.Dest != null)
            .Select(result => (result.Id, result.Code, result.Dest!))
            .Distinct()
            .ToList();
        try
        {
            await db.SetLqaReviewedAsync(results, mark, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetUserFacingError("확인함 표시", ex);
            return;
        }

        if (!ReferenceEquals(db, _projectState.Db))
        {
            return;
        }

        foreach (var (id, code, dest) in results)
        {
            if (mark)
            {
                _lqaReviewedMarks[(id, code)] = dest;
            }
            else
            {
                _lqaReviewedMarks.Remove((id, code));
            }
        }

        // With "확인한 항목 숨기기" the marked results leave the list; the one after them is selected so the review
        // goes on from there.
        var shown = LqaIssuesView.Cast<LqaIssueViewModel>().ToList();
        var position = issues.Select(issue => shown.IndexOf(issue)).Where(index => index >= 0).DefaultIfEmpty(0).Min();
        ApplyLqaReviewedMarks(issues);
        LqaIssuesView.Refresh();
        OnPropertyChanged(nameof(LqaVisibleSummary));
        if (mark && LqaHideReviewed)
        {
            var after = LqaIssuesView.Cast<LqaIssueViewModel>().ToList();
            SelectedLqaIssue = after.Count == 0 ? null : after[Math.Min(position, after.Count - 1)];
        }

        StatusMessage = mark
            ? $"{issues.Count}건을 확인함으로 표시했습니다. 문장을 다듬어도 말투나 남은 영어가 그대로면 표시가 남습니다."
            : $"{issues.Count}건의 확인함 표시를 해제했습니다.";
    }

    private bool CanToggleLqaReviewed() => IsProjectLoaded && !IsLqaScanning;

    /// <summary>Sets each result's "확인함" from the marks and its row's translation now.</summary>
    private void ApplyLqaReviewedMarks(IEnumerable<LqaIssueViewModel> issues)
    {
        foreach (var issue in issues)
        {
            issue.IsReviewed = _projectState.TryGetById(issue.Id, out var entry)
                               && LqaReviewedMarks.Holds(_lqaReviewedMarks, issue.Id, issue.Code, entry.DestText);
        }
    }
}
