using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

/// <summary>Korean display names for row statuses. Filtering still works on the enum.</summary>
public static class StringEntryStatusLabels
{
    private static readonly IReadOnlyDictionary<StringEntryStatus, string> Labels = new Dictionary<StringEntryStatus, string>
    {
        [StringEntryStatus.Pending] = "대기",
        [StringEntryStatus.InProgress] = "번역 중",
        [StringEntryStatus.Done] = "완료",
        [StringEntryStatus.Skipped] = "건너뜀",
        [StringEntryStatus.Error] = "오류",
        [StringEntryStatus.Edited] = "직접 수정",
    };

    public static IEnumerable<StringEntryStatus> FilterOrder { get; } = new[]
    {
        StringEntryStatus.Pending, StringEntryStatus.InProgress, StringEntryStatus.Done,
        StringEntryStatus.Skipped, StringEntryStatus.Error, StringEntryStatus.Edited,
    };

    public static string ToLabel(StringEntryStatus status)
        => Labels.TryGetValue(status, out var label) ? label : status.ToString();

    /// <summary>Accepts a Korean label or, for older saved values, the enum name.</summary>
    public static bool TryParse(string? text, out StringEntryStatus status)
    {
        var trimmed = (text ?? "").Trim();
        foreach (var (value, label) in Labels)
        {
            if (string.Equals(label, trimmed, StringComparison.Ordinal))
            {
                status = value;
                return true;
            }
        }
        return Enum.TryParse(trimmed, ignoreCase: true, out status) && Enum.IsDefined(status);
    }

    public static IEnumerable<string> FilterLabels() => FilterOrder.Select(ToLabel);
}
