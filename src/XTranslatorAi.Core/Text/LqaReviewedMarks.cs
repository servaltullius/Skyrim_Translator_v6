using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Text.Lqa.Internal.Rules;

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
/// <para>
/// A rewording keeps what a mark judged when it can be told from the two texts: a speech-level mark holds while the
/// line reads at the level it was marked at, and an English-residue mark while the line has no Latin word the marked
/// text did not. Polishing Serana Dialogue Add-On's 11,689 lines brought back 5 marks of the first kind (a 존댓말
/// line to a court mage, reworded and still 존댓말), and each had to be looked at again for nothing.
/// </para>
/// </remarks>
public static class LqaReviewedMarks
{
    private static readonly string[] SpeakerLevelCodes =
    {
        SpeakerToneConsistencyRule.Code, SpeakerToneConsistencyRule.MixedCode, SpeakerToneConsistencyRule.PlayerCode,
    };

    private static readonly string[] DialogueLevelCodes = { "tone_inconsistent", "tone_differs_from_plugin" };

    private const string EnglishResidueCode = "english_residue";

    public static bool Holds(IReadOnlyDictionary<(long StringId, string Code), string> marks, long stringId, string code, string? destText)
    {
        if (HoldsExactly(marks, stringId, code, destText))
        {
            return true;
        }

        var speaker = Array.IndexOf(SpeakerLevelCodes, code) >= 0;
        if (speaker || Array.IndexOf(DialogueLevelCodes, code) >= 0)
        {
            // Read with the check that raised this result: 하오체 and 반말 are one level to the dialogue check alone.
            Func<string?, string?, bool> sameLevel = speaker ? SpeakerToneConsistencyRule.SameLevel : DialogueToneConsistencyRule.SameSpeech;
            return SpeakerLevelCodes.Concat(DialogueLevelCodes)
                .Any(other => marks.TryGetValue((stringId, other), out var marked)
                              && (string.Equals(marked, destText ?? "", StringComparison.Ordinal) || sameLevel(marked, destText)));
        }

        return code == EnglishResidueCode
               && marks.TryGetValue((stringId, code), out var residueMarked)
               && LqaScanner.LatinWords(destText).Any()
               && LqaScanner.LatinWords(destText).All(new HashSet<string>(LqaScanner.LatinWords(residueMarked), StringComparer.Ordinal).Contains);
    }

    private static bool HoldsExactly(IReadOnlyDictionary<(long StringId, string Code), string> marks, long stringId, string code, string? destText)
        => marks.TryGetValue((stringId, code), out var marked) && string.Equals(marked, destText ?? "", StringComparison.Ordinal);
}
