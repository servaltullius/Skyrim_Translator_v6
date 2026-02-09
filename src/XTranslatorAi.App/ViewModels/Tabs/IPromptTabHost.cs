using System.ComponentModel;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IPromptTabHost : INotifyPropertyChanged
{
    string BasePromptText { get; }
    bool UseCustomPrompt { get; set; }
    bool UseRecStyleHints { get; set; }
    string CustomPromptText { get; set; }
    bool HasPromptLintIssues { get; }
    bool HasPromptLintBlockingIssues { get; }
    string PromptLintSummary { get; }
    string PromptLintDetails { get; }
}
