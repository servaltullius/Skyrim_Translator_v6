namespace XTranslatorAi.Core.Models;

public enum StringEntryStatus
{
    Pending = 0,
    InProgress = 1,
    Done = 2,
    Skipped = 3,
    Error = 4,
    Edited = 5,
}

public static class StringEntryStatuses
{
    /// <summary>
    /// A row with no translation yet: 대기, 번역 중 (a run that stopped) and 오류. What 남은 줄, 번역할 줄 and the export's
    /// 끝나지 않은 행 count; the agent's open left 번역 중 out of its 번역할 줄 where status counted it.
    /// </summary>
    public static readonly IReadOnlyList<StringEntryStatus> Unfinished =
        new[] { StringEntryStatus.Pending, StringEntryStatus.InProgress, StringEntryStatus.Error };

    public static bool IsUnfinished(this StringEntryStatus status)
        => status is StringEntryStatus.Pending or StringEntryStatus.InProgress or StringEntryStatus.Error;
}
