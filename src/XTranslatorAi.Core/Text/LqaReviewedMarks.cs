using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// "확인함" marks on quality-check results (ProjectDb.GetLqaReviewedMarksAsync). A mark holds while the row's
/// translation is the one it was marked with; after an edit the result is shown again, since the new text may
/// have the problem for another reason or not at all.
/// </summary>
/// <remarks>
/// The speech-level checks are one concern: a mark made on one of them holds for the others. An update of the checks
/// moves a line between them without its text changing (Serana's "There you are, father!" from 같은 화자 말투 갈림
/// to 말투 다름 when 1.16 named the scene's listener; a line without a speaker from 플러그인 대사와 말투 다름 to a
/// speaker's group once the speakers are read), and the line looked at and left as it is showed up again.
/// </remarks>
public static class LqaReviewedMarks
{
    private static readonly string[] SpeechLevelCodes =
    {
        "speaker_tone_inconsistent", "speaker_tone_mixed", "player_tone_inconsistent", "tone_inconsistent", "tone_differs_from_plugin",
    };

    public static bool Holds(IReadOnlyDictionary<(long StringId, string Code), string> marks, long stringId, string code, string? destText)
    {
        if (HoldsExactly(marks, stringId, code, destText))
        {
            return true;
        }

        return Array.IndexOf(SpeechLevelCodes, code) >= 0
               && Array.Exists(SpeechLevelCodes, other => other != code && HoldsExactly(marks, stringId, other, destText));
    }

    private static bool HoldsExactly(IReadOnlyDictionary<(long StringId, string Code), string> marks, long stringId, string code, string? destText)
        => marks.TryGetValue((stringId, code), out var marked) && string.Equals(marked, destText ?? "", StringComparison.Ordinal);
}
