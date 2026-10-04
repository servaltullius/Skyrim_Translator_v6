using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private int _promptLintIssueCount;
    private int _promptLintBlockingCount;

    private bool TryValidatePromptLintBeforeStart()
    {
        RefreshPromptLint();
        if (!HasPromptLintBlockingIssues)
        {
            return true;
        }

        StatusMessage = $"프롬프트 충돌 경고: 차단 {_promptLintBlockingCount}건 / 전체 {_promptLintIssueCount}건. 프롬프트 탭에서 수정한 뒤 다시 시도하세요.";
        return false;
    }

    private void RefreshPromptLint()
    {
        var issues = PromptConflictLint.Analyze(
            useCustomPrompt: UseCustomPrompt,
            customPrompt: CustomPromptText,
            enableProjectContext: EnableProjectContext,
            projectContext: ProjectContextPreview
        );

        _promptLintIssueCount = issues.Count;
        _promptLintBlockingCount = 0;
        for (var i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == PromptLintSeverity.Error)
            {
                _promptLintBlockingCount++;
            }
        }

        HasPromptLintIssues = issues.Count > 0;
        HasPromptLintBlockingIssues = _promptLintBlockingCount > 0;

        if (issues.Count == 0)
        {
            PromptLintSummary = "프롬프트 검사: 눈에 띄는 충돌이 없습니다.";
            PromptLintDetails = "";
            return;
        }

        PromptLintSummary = HasPromptLintBlockingIssues
            ? $"프롬프트 검사: 문제 {_promptLintIssueCount}개, 그중 번역을 막는 것 {_promptLintBlockingCount}개."
            : $"프롬프트 검사: 경고 {_promptLintIssueCount}개.";

        var lines = new List<string>(capacity: issues.Count);
        for (var i = 0; i < issues.Count; i++)
        {
            var issue = issues[i];
            var level = issue.Severity == PromptLintSeverity.Error ? "[차단]" : "[경고]";
            lines.Add($"{level} {issue.Source}: {issue.Message} (matched: \"{issue.MatchedText}\")");
        }

        PromptLintDetails = string.Join(Environment.NewLine, lines);
    }

    partial void OnBasePromptTextChanged(string value) => RefreshPromptLint();
    partial void OnCustomPromptTextChanged(string value) => RefreshPromptLint();
    partial void OnUseCustomPromptChanged(bool value) => RefreshPromptLint();
    partial void OnEnableProjectContextChanged(bool value) => RefreshPromptLint();
    partial void OnProjectContextPreviewChanged(string value) => RefreshPromptLint();
}
