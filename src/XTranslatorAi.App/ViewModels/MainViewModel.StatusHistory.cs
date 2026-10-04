using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// The status bar shows one message, and the next one replaces it: opening a plugin showed the series TM import's
/// encoding error and then, a moment later, "플러그인 읽기 완료". The status bar's tooltip now lists recent messages.
/// </summary>
public partial class MainViewModel
{
    private const int StatusHistorySize = 20;
    private readonly LinkedList<string> _statusHistory = new();

    public string StatusHistoryText
        => _statusHistory.Count == 0 ? "" : "최근 메시지:" + Environment.NewLine + string.Join(Environment.NewLine, _statusHistory);

    partial void OnStatusMessageChanged(string value)
    {
        // Progress updates repeat a running message ("품질 검사 중... 40%"); only finished states are worth keeping.
        if (string.IsNullOrWhiteSpace(value) || value.EndsWith("...", StringComparison.Ordinal) || value.Contains("중... ", StringComparison.Ordinal))
        {
            return;
        }

        _statusHistory.AddFirst($"{DateTime.Now:HH:mm:ss} {value}");
        while (_statusHistory.Count > StatusHistorySize)
        {
            _statusHistory.RemoveLast();
        }

        OnPropertyChanged(nameof(StatusHistoryText));
    }

}
