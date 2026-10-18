using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// "확인함" marks on quality-check results (ProjectDb.GetLqaReviewedMarksAsync). A mark holds while the row's
/// translation is the one it was marked with; after an edit the result is shown again, since the new text may
/// have the problem for another reason or not at all.
/// </summary>
public static class LqaReviewedMarks
{
    public static bool Holds(IReadOnlyDictionary<(long StringId, string Code), string> marks, long stringId, string code, string? destText)
        => marks.TryGetValue((stringId, code), out var marked) && string.Equals(marked, destText ?? "", StringComparison.Ordinal);
}
