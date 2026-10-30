using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal;

/// <summary>
/// How the tone checks take a line's speech apart, alike for the dialogue check (DialogueToneConsistencyRule), the
/// speaker check (SpeakerToneConsistencyRule) and the shared classifier (LqaToneClassifier). Each had its own copy and
/// they read the same line differently: the dialogue check left out "(고개를 끄덕인다)" but took "알았어. *웃음*" for
/// a line with no level, the speaker check the other way round ("알겠소. (고개를 끄덕인다)" read as 반말 by 끄덕인다), and
/// neither read "…요~".
/// </summary>
internal static class SpeechReading
{
    /// <summary>A stage direction, narration and not speech: "[양초를 제작하는 방법을 설명한다]", "(서류철을 보여준다)", "*웃음*".</summary>
    public static readonly Regex StageDirection = new(@"\[[^\]\n]*\]|\([^)\n]*\)|\*[^*\n]+\*", RegexOptions.CultureInvariant);

    /// <summary>
    /// A quotation is someone else's words: "\"살려 주세요!\" 하고 울부짖더군." is said in 반말, and Serana's "아, 속편이네.
    /// '내 창을 좀 닦아 주시겠어요, 내 사랑?'" reads a book's line. Single quotes mark a quotation only around a sentence
    /// (with its own . ! ? or …) and where no letter is before the first, so neither the apostrophe in a name (J'zargo,
    /// 지'자르고) nor an echoed word in the speaker's own sentence ("문학의 '학자가 아니다'?") is taken for one.
    /// </summary>
    public static readonly Regex Quoted = new(
        "\"[^\"]*\"|“[^”]*”|‘[^’]*’|「[^」]*」|『[^』]*』|(?<![\\p{L}\\p{N}])'[^'\\n]*[.!?…][^'\\n]*'",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The speaker's own words: the line without its stage directions and quotations. A line of nothing but quotations
    /// is judged by them ("\"살려 주세요!\"", a bard's tale in quotes, a "(을/를)" inside them or not). One with a stage
    /// direction beside them is read aloud or said in another's voice ("(낭독하며) \"…펼쳐졌다.\"", "(자신의 것이 아닌
    /// 목소리로) \"안녕하세요!\""): it has no words of its own (""), and neither has a line of nothing but stage directions.
    /// </summary>
    public static string OwnWords(string? text)
    {
        var line = (text ?? "").Trim();
        if (IsHeading(line)) return "";
        var outsideQuotes = Quoted.Replace(line, " ");
        var own = StageDirection.Replace(outsideQuotes, " ").Trim();
        if (own.Any(char.IsLetter)) return own;
        return StageDirection.IsMatch(outsideQuotes) ? "" : line;
    }

    /// <summary>
    /// A heading drawn with dashes or equals signs ("----리치 영지----", "== 상점 =="), a dialogue menu's divider: no
    /// speech. Its last word was read once the trailing dash was ("----펠 황야----" as 반말).
    /// </summary>
    public static bool IsHeading(string line)
        => line.Length > 4 && (line.StartsWith("--", System.StringComparison.Ordinal) && line.EndsWith("--", System.StringComparison.Ordinal)
                               || line.StartsWith("==", System.StringComparison.Ordinal) && line.EndsWith("==", System.StringComparison.Ordinal));

    /// <summary>The line without its last sentence ("" when it has one only), its end marks trimmed first.</summary>
    public static string WithoutLastSentence(string text)
    {
        var trimmed = TrimEnd(text);
        var end = trimmed.LastIndexOfAny(new[] { '.', '!', '?', '…' });
        return end > 0 ? trimmed[..(end + 1)] : "";
    }

    /// <summary>The marks after a line's last word: punctuation, closing quotes and brackets, and a drawn-out ~ or -.</summary>
    public static readonly char[] TrailingMarks =
        { ' ', '\t', '\r', '\n', '.', ',', '!', '?', '…', '"', '\'', '”', '’', ')', ']', '」', '』', '~', '-' };

    /// <summary>The line up to its last word, without the marks after it.</summary>
    public static string TrimEnd(string text) => text.TrimEnd(TrailingMarks);

    /// <summary>
    /// A question in ~ㄹ까 asked in 반말 ("할까", "갈까", "볼까"): the ㄹ before 까 keeps out 그러니까, 합니까 and ~ㄹ까요.
    /// </summary>
    public static bool IsCasualKka(string word) => word.Length >= 2 && word[^1] == '까' && KoreanSyllables.HasFinalRieul(word[^2]);

    // "야를님, 이걸 어떻게 말씀드려야 할지...", "어떻게 될지", "왜 그런지": this 지 asks "whether" in a clause left open,
    // and is not 반말's 지. A polite line that trails off so was counted as 반말 among Serana's lines to the Jarl.
    private static readonly string[] OpenClauseEndings =
    {
        "할지", "될지", "을지", "는지", "은지", "인지", "던지", "런지", "올지", "갈지", "볼지", "낼지", "릴지",
        // "황량한 땅의 구석구석까지, 그대가 ... 전해주기를.": the particle 까지, not 반말's 지.
        "까지",
    };

    /// <summary>Whether the line, its end marks trimmed, ends in a clause left open (~ㄹ지, ~는지) or the particle 까지.</summary>
    public static bool EndsInOpenClause(string cleaned) => OpenClauseEndings.Any(ending => cleaned.EndsWith(ending, System.StringComparison.Ordinal));
}
