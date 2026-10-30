using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public enum PromptLintSeverity
{
    Warning = 0,
    Error = 1,
}

/// <param name="MatchedText">The words of the instruction that matched, as written.</param>
/// <param name="Clause">The instruction (sentence) the phrase is in: two of them with the same phrase are two issues.</param>
public sealed record PromptLintIssue(
    PromptLintSeverity Severity,
    string Source,
    string Message,
    string MatchedText,
    string Clause = ""
);

public static class PromptConflictLint
{
    /// <param name="Prohibition">
    /// The phrase is itself a prohibition ("번역하지 말", "번역 금지", "Don't translate"): a negation next to it does not
    /// reverse it.
    /// </param>
    /// <param name="ObjectAfter">An English phrase whose object follows it ("Do not translate product names").</param>
    private sealed record Needle(Regex Pattern, bool Prohibition = false, bool ObjectAfter = false);

    /// <param name="ScopedIsWarning">
    /// An instruction that names what it keeps ("SkyUI 같은 제품명은 번역하지 말 것", "Do not translate mod names")
    /// is a narrow rule, not a blanket one; it is reported without blocking the run.
    /// </param>
    /// <param name="AboutTheOutput">
    /// In a project context, which describes the mod, the phrase is an instruction only where the clause speaks of the
    /// translation: "번역 결과에 설명 포함" is one, "무기 설명 포함 300개 아이템" is a fact about the mod.
    /// </param>
    private sealed record PhraseRule(
        PromptLintSeverity Severity,
        string Message,
        Needle[] Needles,
        bool ScopedIsWarning = false,
        bool AboutTheOutput = false
    );

    private const string CustomPromptSource = "custom prompt";
    private const string ProjectContextSource = "project context";

    private const string ScopedKeepMessage = "이 지시가 가리키는 것은 번역하지 않고 남습니다. 이름이나 용어에만 해당하는지 확인하세요.";

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static Needle Text(string text) => new(new Regex(Regex.Escape(text), Options));

    private static Needle Pattern(string pattern, bool prohibition = false, bool objectAfter = false)
        => new(new Regex(pattern, Options), prohibition, objectAfter);

    // A verb that reports what was done ("유지된", "출력됩니다", "처리됨") rather than asks for it.
    private const string Done = "[되된됩됨돼됐]";

    // "유지한다" asks; "유지한 이름", "유지하는 줄", "유지했다" report.
    private const string KeepItself = "유지(?!" + Done + "|한(?!다)|하는|했)";

    // What a keep instruction does to the text ("둘 것", "남겨", "남깁니다", "유지한다"), not what a fact reports of it
    // ("남는", "남긴 책", "남겨진 메뉴", "남겨 둔 책", "유지된") or a number ("두 줄").
    private const string KeepVerb = @"(?:둘\s*것|둘\s*수|둔다|둡니다|둬|두(?:고|어|세요|라|십시오|도록|기)|놔|놓|남(?:겨(?!진|져|\s*둔(?!다))|길|기(?!는)|긴다|깁)|" + KeepItself + ")";

    // "번역" on its own or right after a particle ("대사는번역하지"), not inside 재번역, 오번역, 미번역.
    private const string Translate = @"(?:(?<![가-힣])|(?<=[은는을를]))번역";

    // An instruction's own ending: "포함할 것", "덧붙이세요", "달아 주세요", "설명 포함." at the end. Not "포함해 모두
    // 번역하세요" or "포함한 모든 줄", which ask to translate the descriptions too.
    private const string AskEnding = @"(?:할\s*것|하라|하세요|하십시오|해\s*주세요|해\s*줘|해라|합니다|한다|해야|(?:해서|하여|해)\s*(?:출력|답|작성|쓸|써)|\s*$)";

    private static readonly PhraseRule[] Rules =
    {
        new(
            Severity: PromptLintSeverity.Error,
            Message: "내용을 번역하지 않고 남기게 할 수 있는 지시입니다.",
            Needles: new[]
            {
                Pattern(@"원문\s*(?:을|은)?\s*" + KeepItself),
                Pattern(@"미번역\s*(?:상태\s*)?(?:그대로\s*|으로\s*|로\s*)?(?:" + KeepVerb + @"|처리(?!" + Done + "))"),
                // Not "번역하지 않은 줄" (a line that is not translated) nor "번역하지 않기로 한 줄".
                Pattern(Translate + @"\s*(?:은|을|도)?\s*하지\s*(?:말|마|않(?:는다|는\s*것|을\s*것|도록|고|아야|습니다|음|기(?!로)))", prohibition: true),
                // Not a list of the words kept ("번역 금지어: SkyUI, MCM"), nor a fact ("번역 금지를 풀어 주었다").
                Pattern(Translate + @"\s*(?:은|을|도)?\s*금지(?!\s*(?:어|단어|목록|용어|대상|항목|를|가|된|되|해제|여부|요청))", prohibition: true),
                Pattern(Translate + @"(?:할\s*필요\s*(?:가|는)?\s*없|\s*불필요)", prohibition: true),
                Pattern(@"원문\s*(?:을|은)?\s*(?:그대로|대로|으로)\s*(?:" + KeepVerb + @"|출력(?!" + Done + "))"),
                Pattern(@"(?:영어|영문)\s*(?:표기\s*)?(?:그대로\s*|으로\s*|로\s*)?" + KeepVerb),
                Pattern(@"\b(?:do\s+not|don['’]?t|never|must\s+not|mustn['’]?t|should\s+not|shouldn['’]?t)\s+translate\b", prohibition: true, objectAfter: true),
                Pattern(@"\bleave\b(?<obj>(?:[\s,]+[\w'’-]+){0,6}?)\s+(?:untranslated\b|as[\s-]is\b|in\s+english\b)"),
                Pattern(@"\bkeep\s+(?:the\s+)?original\s+(?:english\s+)?texts?\b"),
                Pattern(@"\bkeep\b(?<obj>(?:[\s,]+[\w'’-]+){1,6}?)\s+in\s+english\b"),
                Text("return source text unchanged"),
            },
            ScopedIsWarning: true
        ),
        new(
            Severity: PromptLintSeverity.Error,
            Message: "번역기가 요구하는 출력 형식과 어긋날 수 있는 지시입니다.",
            Needles: new[]
            {
                Text("output markdown"),
                Text("return markdown"),
                Text("markdown으로 출력"),
                Text("json 말고"),
                Text("do not output json"),
                Text("return yaml"),
                Text("yaml으로 출력"),
                Text("include code fence"),
                Text("코드 펜스"),
                Text("코드펜스"),
                Text("output xml"),
            }
        ),
        new(
            Severity: PromptLintSeverity.Error,
            Message: "번역문에 설명이나 해설을 덧붙이게 할 수 있는 지시입니다.",
            Needles: new[]
            {
                Text("include explanation"),
                Text("add explanation"),
                Text("with commentary"),
                Pattern(@"(?:설명|해설|주석)\s*(?:을|를|도)?\s*(?:(?:포함|추가|첨부)" + AskEnding
                        + @"|덧붙(?:일\s*것|이라|이세요|여\s*주세요|여라|인다|입니다|여\s*(?:출력|답|작성|쓸|써))"
                        + @"|(?:달\s*것|다세요|달아\s*주세요|달아라|단다|답니다))"),
            },
            AboutTheOutput: true
        ),
        new(
            Severity: PromptLintSeverity.Warning,
            Message: "모델이 원문을 그대로 두는 쪽으로 기울 수 있는 지시입니다.",
            Needles: new[]
            {
                Text("if not sure, keep original"),
                Text("if unsure keep original"),
                Text("확신이 없으면 원문"),
                Text("불확실하면 원문"),
            }
        ),
    };

    public static IReadOnlyList<PromptLintIssue> Analyze(
        bool useCustomPrompt,
        string? customPrompt,
        bool enableProjectContext,
        string? projectContext
    )
    {
        var issues = new List<PromptLintIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (useCustomPrompt)
        {
            AnalyzeSource(CustomPromptSource, customPrompt, issues, seen);
        }

        if (enableProjectContext)
        {
            AnalyzeSource(ProjectContextSource, projectContext, issues, seen);
        }

        return issues;
    }

    public static bool HasBlockingIssues(IReadOnlyList<PromptLintIssue> issues)
    {
        for (var i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == PromptLintSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeSource(
        string source,
        string? text,
        List<PromptLintIssue> issues,
        HashSet<string> seen
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Quoted mod text ("#120 원문: 'Do not translate this letter.'", "'번역하지 말 것'이라는 표지판") is no
        // instruction: it is read as a name (the same length, so a clause is shown as written). A quoted Korean
        // instruction is one, quoted for emphasis ("모든 텍스트"는 번역하지 말 것).
        var masked = QuotedRegex.Replace(text, m => IsQuotedModText(text, m) ? new string('Q', m.Length) : m.Value);

        // Each instruction is read on its own, so a negation in one sentence does not excuse another.
        foreach (Match clauseMatch in ClauseRegex.Matches(masked))
        {
            var clause = clauseMatch.Value;
            var instruction = text.Substring(clauseMatch.Index, clauseMatch.Length).Trim();
            for (var i = 0; i < Rules.Length; i++)
            {
                var rule = Rules[i];
                if (rule.AboutTheOutput && source == ProjectContextSource && !OutputWordsRegex.IsMatch(clause))
                {
                    continue;
                }

                // In the order written: a second instruction may say no more of its object than the first did
                // ("고유명사는 번역하지 말고 원문 그대로 둘 것").
                var matches = new List<(int Needle, Match Match)>();
                for (var j = 0; j < rule.Needles.Length; j++)
                {
                    foreach (Match match in rule.Needles[j].Pattern.Matches(clause))
                    {
                        matches.Add((j, match));
                    }
                }

                matches.Sort((a, b) => a.Match.Index.CompareTo(b.Match.Index));
                KeepReach? earlier = null;
                // An instruction's object is read from where the one before it ended: in "인명은 원문 유지 / 지명은 원문
                // 유지" the first one's 원문 is not the second one's.
                var earlierEnd = 0;
                var blocked = new HashSet<int>();
                foreach (var (j, match) in matches)
                {
                    // One blocking issue per phrase and instruction is enough: a long line repeating it took a second.
                    if (blocked.Contains(j))
                    {
                        continue;
                    }

                    var needle = rule.Needles[j];
                    // "미번역 문장을 남기지 말 것" and "Never output markdown" say what the rules say; they blocked the
                    // run because the phrase matched. A phrase that is itself a prohibition ("번역하지 말") is not
                    // reversed by it.
                    if (!needle.Prohibition && IsNegated(clause, match.Index, match.Length))
                    {
                        continue;
                    }

                    var severity = rule.Severity;
                    var message = rule.Message;
                    if (rule.ScopedIsWarning)
                    {
                        var reach = Reach(clause, match, needle, earlier, earlierEnd);
                        // How to translate ("함부로 번역하지 말고") is not what the next instruction keeps.
                        earlier = reach == KeepReach.Manner ? null : reach;
                        earlierEnd = match.Index + match.Length;
                        if (reach is KeepReach.Nothing or KeepReach.Manner)
                        {
                            continue;
                        }

                        if (reach == KeepReach.NamedThings)
                        {
                            severity = PromptLintSeverity.Warning;
                            message = ScopedKeepMessage;
                        }
                    }

                    if (severity == PromptLintSeverity.Error)
                    {
                        blocked.Add(j);
                    }

                    if (!seen.Add(source + "|" + i + ":" + j + "|" + severity + "|" + instruction))
                    {
                        continue;
                    }

                    issues.Add(new PromptLintIssue(
                        severity,
                        source,
                        message,
                        text.Substring(clauseMatch.Index + match.Index, match.Length),
                        instruction));
                }
            }
        }
    }

    private static readonly Regex ClauseRegex = new(
        pattern: @"[^\r\n.!?;。]+",
        options: RegexOptions.CultureInvariant
    );

    // A quote opened after a Latin letter is an apostrophe ("Don't").
    private static readonly Regex QuotedRegex = new(
        pattern: "\"[^\"\\r\\n]*\"|“[^”\\r\\n]*”|‘[^’\\r\\n]*’|「[^」\\r\\n]*」|『[^』\\r\\n]*』|(?<![A-Za-z])'[^'\\r\\n]*'(?![A-Za-z])",
        options: RegexOptions.CultureInvariant
    );

    // Korean puts its particle right after a quote it reports: "'번역하지 말 것'이라는 표지".
    private static readonly Regex ReportedQuoteRegex = new(
        pattern: @"\A(?:이라는|라는|이라고|라고|이라며|라며|이란|란)",
        options: RegexOptions.CultureInvariant
    );

    // What introduces a line of the mod: "#120 원문: '…'", "대사: '…'", "the sign reads '…'".
    private static readonly Regex QuoteIntroRegex = new(
        pattern: @"(?:원문|대사|문구|문장|글귀|text|line|reads|says|#\d+)\s*[:：]?\s*\z",
        options: Options
    );

    /// <summary>
    /// A quote the instruction reports: one a particle says is reported, one a line of the mod comes in, and an English
    /// name ("'SkyUI'는", "'Dawnguard'") that is no sentence. An instruction in quotes is still one, Korean or English
    /// ("\"Do not translate anything.\"").
    /// </summary>
    private static bool IsQuotedModText(string text, Match quote)
    {
        var end = quote.Index + quote.Length;
        if (ReportedQuoteRegex.IsMatch(text.Substring(end, Math.Min(3, text.Length - end))))
        {
            return true;
        }

        if (quote.Value.Any(IsHangul))
        {
            return false;
        }

        var inside = quote.Value[1..^1].Trim();
        return QuoteIntroRegex.IsMatch(text[Math.Max(0, quote.Index - 20)..quote.Index])
               || (inside.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3 && !inside.EndsWith('.') && !inside.EndsWith('!'));
    }

    // "번역된 책마다 해설을 추가합니다" is a fact of the release, not of this translation's output.
    private static readonly Regex OutputWordsRegex = new(
        pattern: @"번역(?![된되])|출력|응답|답변|결과|줄마다|문장마다|각\s*줄|translation|output|response|answer|each line",
        options: Options
    );

    // Korean puts the negation after the phrase: "남기지 말 것", "출력하지 마세요", "덧붙이지 않는다", "금지".
    private static readonly string[] KoreanNegationsAfter =
    {
        "지 말", "지말", "지 마", "지마", "말 것", "말것", "말라", "금지", "않", "없도록", "없게", "없이", "안 된", "안된", "안 됨", "안됨",
        "필요 없", "필요없", "필요는 없", "필요가 없",
    };

    // English puts it before: "Never output markdown", "Do not include explanation".
    private static readonly Regex EnglishNegationBeforeRegex = new(
        pattern: @"\b(?:do not|don't|dont|never|avoid|must not|mustn't|should not|shouldn't|without)\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    // The negation must belong to the phrase, not to the next verb: in "원문 유지하고 번역하지 않는다" or
    // "Keep tokens, do not reorder them, and output markdown" the phrase itself is meant.
    private static readonly string[] KoreanPredicateEnds = { ",", "고 ", "며 ", "면서", "지만", "는데" };

    private static readonly Regex EnglishPredicateStartRegex = new(
        pattern: @".*(?:,|\band\b|\bbut\b|\bthen\b)",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

    // How far back an instruction's own words are read: a 15,000-character line with no full stop read all of it
    // for each match, and the check runs as the user types.
    private const int ReachWindow = 200;

    private static string Before(string clause, int idx) => clause[Math.Max(0, idx - ReachWindow)..idx];

    private static bool IsNegated(string clause, int idx, int length)
    {
        var after = clause[(idx + length)..Math.Min(clause.Length, idx + length + ReachWindow)];
        foreach (var end in KoreanPredicateEnds)
        {
            var cut = after.IndexOf(end, StringComparison.Ordinal);
            if (cut >= 0)
            {
                after = after[..cut];
            }
        }

        var before = Before(clause, idx);
        var start = EnglishPredicateStartRegex.Match(before);
        if (start.Success)
        {
            before = before[(start.Index + start.Length)..];
        }

        return KoreanNegationsAfter.Any(n => after.Contains(n, StringComparison.Ordinal))
               || EnglishNegationBeforeRegex.IsMatch(before);
    }

    private enum KeepReach
    {
        /// <summary>The text itself: the run would leave it untranslated.</summary>
        Everything,

        /// <summary>Things the instruction names (names, terms, a kind of line): a choice, reported.</summary>
        NamedThings,

        /// <summary>What the translator keeps anyway ("태그와 토큰은 원문 그대로 둘 것"): nothing to report.</summary>
        Nothing,

        /// <summary>
        /// How to translate, not what to keep ("잘못 번역하지 않도록", "Do not translate literally"): nothing to report,
        /// and no object for the next instruction ("함부로 번역하지 말고 원문 그대로 둘 것" keeps everything).
        /// </summary>
        Manner,
    }

    // Korean topic/object particles that end the name of what is kept: "제품명은 번역하지 말 것", "고유명사를 원문 유지".
    private static readonly char[] ScopeParticles = { '은', '는', '을', '를', '도', '만' };

    // Words that make the instruction cover everything again: "원문은 번역하지 말 것", "나머지는", "아무것도".
    private static readonly string[] KoreanBlanketStarts = { "원문", "문장", "텍스트", "내용", "나머지", "아무것" };

    // Everything of what follows: "모든 대사는" is all of it, "모든 고유명사는" and "모든 NPC 이름은" are names.
    private static readonly string[] KoreanAllWords = { "모든", "전체", "전부", "모두" };

    // As the object itself ("영어는 번역하지 말 것", "하나도"), not before what it qualifies ("영문 약어는", "영어 대사는").
    private static readonly string[] KoreanBlanketWords = { "영어", "영문", "하나" };

    // Kinds of name: what a "모든" before them still names. Not 설명 (a description is text).
    private static readonly string[] KoreanNameKinds = { "이름", "명", "명사", "명칭", "용어", "약어", "단어" };

    // A quantifier after the object counts it ("이름은 모두 원문 유지"), it names nothing.
    private static readonly HashSet<string> KoreanTrailingQuantifiers = new(StringComparer.Ordinal) { "모두", "전부", "다", "전체" };

    private static readonly string[] KoreanMannerWords =
    {
        "잘못", "함부로", "마음대로", "임의로", "멋대로", "과하게", "지나치게", "억지로", "무리하게", "대충", "따로", "다르게",
        "새로", "다시", "중복", "과잉", "직역", "의역", "기계", "자동", "엉뚱하게", "어색하게", "추가",
    };

    private static readonly string[] KoreanKeptAnyway = { "태그", "토큰", "숫자", "변수", "코드", "서식", "플레이스홀더" };

    // Words that stress an instruction without naming anything: "고유명사는 절대 번역하지 마세요".
    private static readonly HashSet<string> KoreanStressWords = new(StringComparer.Ordinal)
    {
        "절대", "절대로", "꼭", "반드시", "항상", "되도록", "무조건", "가급적", "최대한",
    };

    // What a Korean instruction's object phrase starts after: a clause, a colon, a parenthesis, an arrow.
    private static readonly Regex KoreanPhraseStartRegex = new(
        pattern: @".*(?:(?<sep>,|:|\(|（|→|=)|(?<link>고\s|며\s|면서|지만|는데))",
        options: RegexOptions.CultureInvariant | RegexOptions.Singleline
    );

    // A term line, from the start of the instruction: "Thu'um: 영문 유지", "- 탈모르 대사관 → 원문 유지". A colon or
    // an equals sign after Korean is a heading or a setting ("규칙: 번역하지 말 것", "출력 형식 = 원문 그대로 출력"), as
    // are a few English words ("Rule:").
    private static readonly Regex TermLineRegex = new(
        pattern: @"^\s*(?:[-*•]\s*)?(?:(?<latin>[A-Za-z][A-Za-z0-9'’ .&-]*?)\s*[:=]|(?<term>[^:→=]+?)\s*→)\s*$",
        options: RegexOptions.CultureInvariant
    );

    private static readonly HashSet<string> EnglishHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "rule", "rules", "note", "notes", "important", "style", "tone", "instruction", "instructions", "guideline",
        "guidelines", "policy", "warning", "tip", "tips", "context", "translation", "translations",
    };

    private static readonly HashSet<string> EnglishBlanketWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anything", "everything", "it", "this", "that", "these", "those", "them", "text", "texts", "source", "content",
        "input", "any", "all", "strings", "lines", "sentences",
        // "Do not translate at all", "Do not translate and keep the original".
        "at", "and", "unless", "even",
    };

    private static readonly HashSet<string> EnglishPronouns = new(StringComparer.OrdinalIgnoreCase) { "it", "them", "this", "that", "these", "those" };

    private static readonly HashSet<string> EnglishMannerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "literally", "word", "word-for-word", "loosely", "freely", "twice", "again", "separately", "differently",
    };

    private static readonly HashSet<string> EnglishKeptAnyway = new(StringComparer.OrdinalIgnoreCase)
    {
        "tag", "tags", "token", "tokens", "placeholder", "placeholders", "number", "numbers", "variable", "variables",
        "markup", "code",
    };

    private static readonly Regex LeadingCommaRegex = new(@"\A\s*[,;]", RegexOptions.CultureInvariant);

    /// <param name="earlier">What an earlier instruction of the same clause kept, for one that names nothing itself.</param>
    /// <param name="earlierEnd">Where that instruction ended: this one's words are read from there.</param>
    private static KeepReach Reach(string clause, Match match, Needle needle, KeepReach? earlier, int earlierEnd)
    {
        var from = Math.Min(match.Index, Math.Max(earlierEnd, match.Index - ReachWindow));
        var before = clause[from..match.Index];
        return match.Value.Any(IsHangul)
            ? KoreanReach(before, from == 0 ? before : null, earlier)
            : EnglishReach(clause, match, needle, before, earlier);
    }

    /// <param name="lineStart">The words before the instruction from the start of its line, or null when it is not the
    /// line's first (a term line has it first).</param>
    private static KeepReach KoreanReach(string before, string? lineStart, KeepReach? earlier)
    {
        var start = KoreanPhraseStartRegex.Match(before);
        var words = ObjectWords(start.Success ? before[(start.Index + start.Length)..] : before);
        if (IsKoreanBlanket(words))
        {
            return KeepReach.Everything;
        }

        var last = words.LastOrDefault();
        if (last != null && IsKoreanManner(last))
        {
            return KeepReach.Manner;
        }

        // An aside to a term ("Ashe Impressionist: Ashe Impressionist (모드명 및 고유 명칭으로 원문 유지)") or a term
        // line keeps that term.
        if (IsInsideParentheses(before))
        {
            return KeepReach.NamedThings;
        }

        if (lineStart != null && TermLineRegex.Match(lineStart) is { Success: true } term)
        {
            if (term.Groups["latin"].Success && !EnglishHeadings.Contains(term.Groups["latin"].Value.Trim()))
            {
                return KeepReach.NamedThings;
            }

            if (term.Groups["term"].Success)
            {
                return IsKoreanBlanket(ObjectWords(term.Groups["term"].Value)) ? KeepReach.Everything : KeepReach.NamedThings;
            }
        }

        if (last == null)
        {
            // "고유명사는 번역하지 말고 원문 그대로 둘 것": the second instruction's object is the first one's, or the
            // topic before the first verb ("고유명사는 음역하지 말고 원문 그대로 둘 것"). Right after the first one
            // ("이름은 번역하지 않고 영문 그대로 둔다", its 고 in the match) it is the first one's too.
            if (start.Success && start.Groups["link"].Success)
            {
                return earlier ?? TopicReach(before[..start.Groups["link"].Index]);
            }

            return !start.Success && earlier is { } previous ? previous : KeepReach.Everything;
        }

        return NamedReach(last);
    }

    // The topic before a verb: "고유명사는" in "고유명사는 음역하지 말". Not an object (을/를): in "각 줄을 확인하고"
    // that is the verb's own.
    private static KeepReach TopicReach(string before)
    {
        var start = KoreanPhraseStartRegex.Match(before);
        var words = ObjectWords(start.Success ? before[(start.Index + start.Length)..] : before);
        var at = words.FindLastIndex(w => w.Length >= 2 && w[^1] is '은' or '는');
        if (at < 0)
        {
            return KeepReach.Everything;
        }

        var topic = words.Take(at + 1).ToList();
        return IsKoreanBlanket(topic) ? KeepReach.Everything : NamedReach(words[at]);
    }

    private static KeepReach NamedReach(string last)
    {
        var named = (last.Length >= 2 && ScopeParticles.Contains(last[^1])) || (last.Any(char.IsAsciiLetter) && last.All(c => c < 0x80));
        if (!named)
        {
            return KeepReach.Everything;
        }

        return KoreanKeptAnyway.Contains(WithoutParticle(last)) ? KeepReach.Nothing : KeepReach.NamedThings;
    }

    // The words before an instruction, without quote marks ('"번역하지 말 것"' named nothing), stress words and a
    // quantifier after the object.
    private static List<string> ObjectWords(string phrase)
    {
        var words = phrase.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('"', '\'', '“', '”', '‘', '’', '「', '」', '『', '』', '[', ']'))
            .Where(w => w.Length > 0 && !KoreanStressWords.Contains(w))
            .ToList();
        while (words.Count >= 2 && KoreanTrailingQuantifiers.Contains(words[^1]) && WithoutParticle(words[^2]) != words[^2])
        {
            words.RemoveAt(words.Count - 1);
        }

        return words;
    }

    // The object is the last word; "모든" before a kind of name still names.
    private static bool IsKoreanBlanket(IReadOnlyList<string> words)
    {
        var objectIsName = words.Count > 0 && IsKoreanNameKind(words[^1]);
        foreach (var word in words)
        {
            var bare = WithoutParticle(word);
            if (KoreanBlanketStarts.Any(b => word.StartsWith(b, StringComparison.Ordinal))
                || word == "아무"
                || (bare != word && KoreanBlanketWords.Contains(bare))
                || (!objectIsName && KoreanAllWords.Any(a => word.StartsWith(a, StringComparison.Ordinal))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKoreanNameKind(string word)
    {
        var bare = WithoutParticle(word);
        return KoreanNameKinds.Any(kind => bare.EndsWith(kind, StringComparison.Ordinal)) && !bare.EndsWith("설명", StringComparison.Ordinal);
    }

    private static bool IsKoreanManner(string word)
    {
        foreach (var manner in KoreanMannerWords)
        {
            if (word == manner || word == manner + "으로" || word == manner + "로")
            {
                return true;
            }
        }

        return false;
    }

    private static string WithoutParticle(string word)
        => word.Length >= 2 && ScopeParticles.Contains(word[^1]) ? word[..^1] : word;

    private static KeepReach EnglishReach(string clause, Match match, Needle needle, string before, KeepReach? earlier)
    {
        var obj = match.Groups["obj"];
        var end = match.Index + match.Length;
        var objectText = obj.Success ? obj.Value : needle.ObjectAfter ? clause[end..Math.Min(clause.Length, end + ReachWindow)] : null;
        // "Do not translate, keep the original": nothing named after the verb.
        if (!obj.Success && objectText != null && LeadingCommaRegex.IsMatch(objectText))
        {
            return KeepReach.Everything;
        }

        var words = Articleless((objectText ?? "")
            .Split(new[] { ' ', '\t', ',', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('(', ')', '"', '\'', '[', ']', '<', '>'))
            .Where(w => w.Length > 0));
        // "Do not translate or modify tags": the object is the second verb's too.
        if (words.Count >= 2 && words[0].Equals("or", StringComparison.OrdinalIgnoreCase))
        {
            words = Articleless(words.Skip(2));
        }

        var first = words.FirstOrDefault();
        // "Don't translate English text" keeps it all; "English names" are named.
        if (first != null && first.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            first = words.ElementAtOrDefault(1);
            if (first == null || EnglishBlanketWords.Contains(first))
            {
                return KeepReach.Everything;
            }
        }

        if (first != null && EnglishBlanketWords.Contains(first))
        {
            // "Don't translate 'Dawnguard', keep it in English": it is what the first instruction named.
            return EnglishPronouns.Contains(first) && earlier != null ? earlier.Value : KeepReach.Everything;
        }

        if (IsInsideParentheses(before))
        {
            return KeepReach.NamedThings;
        }

        if (first == null)
        {
            return KeepReach.Everything;
        }

        return EnglishMannerWords.Contains(first) ? KeepReach.Manner
            : EnglishKeptAnyway.Contains(first) ? KeepReach.Nothing
            : KeepReach.NamedThings;

        static List<string> Articleless(IEnumerable<string> words)
            => words.SkipWhile(w => w.Equals("the", StringComparison.OrdinalIgnoreCase) || w is "a" or "A" or "an" or "An").ToList();
    }

    private static bool IsInsideParentheses(string before)
        => before.Count(c => c is '(' or '（') > before.Count(c => c is ')' or '）');

    private static bool IsHangul(char c) => c >= '가' && c <= '힣';
}
