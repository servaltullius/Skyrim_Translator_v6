using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core;

public static class TranslationConstants
{
    public static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    /// <summary>
    /// Angle brackets around two or more plain words are a stage direction shown as text ("<Take a deep breath>"
    /// in Serana Dialogue Add-On), not a tag, so they are translated. Tags keep a single word, '=', '/', a quote,
    /// a digit or '%': &lt;mag&gt;, &lt;Alias=Player&gt;, &lt;font face='$HandwrittenFont'&gt;, &lt;br /&gt;, &lt;100%&gt;.
    /// Legacy of the Dragonborn's "&lt;page break&gt;" marker stays a tag.
    /// Brackets holding Hangul are always text: the model translates "&lt;Clears throat&gt;" to the single word
    /// "&lt;헛기침&gt;", and reading that as a tag missing from the source deleted it from the translation.
    /// Three or more words starting with a letter are text even with an apostrophe, a slash or a digit, as long as
    /// there is no '=', '%' or double quote: MEI's player option "&lt; Tell Senna you're visiting with a friend. &gt;"
    /// stayed in English as a tag. Tags with quotes or slashes carry an attribute ('=') or have fewer words (&lt;br /&gt;).
    /// Words around a term token are text too: with "Maven" forced, "&lt; __XT_TERM_…__ - What do you think? &gt;" read as a tag
    /// and the translated option was rejected. A tag never holds a term token; tags are masked before terms.
    /// </summary>
    public const string StageDirectionGuard = @"(?!(?!\s*[Pp]age\s+[Bb]reak\s*>)(?:\s*[^\s<>=/'""\d%]+(?:\s+[^\s<>=/'""\d%]+)+\s*>"
        + @"|\s*[A-Za-z][^\s<>=%""]*(?:\s+[^\s<>=%""]+){2,}\s*>"
        + @"|(?=[^<>]*__XT_)\s*[^\s<>=%""]+(?:\s+[^\s<>=%""]+)+\s*>))"
        + @"(?![^<>]*[\u1100-\u11FF\u3130-\u318F\uAC00-\uD7A3][^<>]*>)";

    public static readonly Regex UiTagTokenRegex = new(
        pattern: @"[+-]?<" + StageDirectionGuard + @"\s*[^>]+\s*>|\[page ?break\]|__XT_[A-Za-z0-9_]+__",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    public const string EndSentinelToken = "__XT_PH_9999__";

    public const string TmHitNoteKind = "tm_hit";

    public const string TmFallbackNoteKind = "tm_fallback";

    public const string GameStringNoteKind = "game_string";

    /// <summary>A row given its earlier translation as it is, because its English did not change since that release.</summary>
    public const string PreviousReleaseNoteKind = "previous_release";

    /// <summary>Notes of a row finished from a known translation without a request; the grid colors these rows.</summary>
    public static readonly IReadOnlyList<string> KnownTranslationNoteKinds = Data.StringNotePolicy.KindsOf(Data.StringNotePolicy.NoteGroup.KnownTranslation);

    /// <summary>
    /// A row the agent app's model would not translate by its own rules, held (건너뜀) with its reason as the message. A
    /// note, not the row's error message: a reopened plugin keeps the notes of the rows whose translation it keeps, and
    /// set the message empty, which left the row 건너뜀 with nothing to say it was held.
    /// </summary>
    public const string AgentHoldNoteKind = "agent_hold";

    /// <summary>A row translated against its English on purpose because the English is wrong ("원문 오류 수정").</summary>
    public const string SourceFixNoteKind = "source_fix";

    /// <summary>
    /// A marked row whose translation an XML import replaced: the mark stays, but the quality check reads the new text in
    /// full until the user marks the row again (a mark that judged the old text would have hidden the new one's errors).
    /// </summary>
    public const string SourceFixRecheckNoteKind = "source_fix_recheck";

    /// <summary>
    /// A hand edit sent to be translated again (다시 번역), its text as the message: the project TM learned the edit, and
    /// filled it straight back into the row the user asked to translate anew.
    /// </summary>
    public const string RetranslatedEditNoteKind = "retranslated_edit";

    /// <summary>
    /// A quality-check result the user looked at and left as it is ("확인함"): the note's kind is this prefix and the
    /// result's code, its message the row's translation when it was marked. A changed translation is judged again.
    /// </summary>
    public const string LqaReviewedNoteKindPrefix = "lqa_reviewed:";

    public static string RemoveInvisibleSeparators(string text)
    {
        if (text.IndexOf('\u200B') < 0 && text.IndexOf('\uFEFF') < 0 && text.IndexOf('\u2060') < 0)
        {
            return text;
        }

        return text.Replace("\u200B", "", StringComparison.Ordinal)
            .Replace("\uFEFF", "", StringComparison.Ordinal)
            .Replace("\u2060", "", StringComparison.Ordinal);
    }
}
