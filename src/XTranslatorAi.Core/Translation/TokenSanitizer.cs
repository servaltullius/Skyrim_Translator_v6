using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

internal static class TokenSanitizer
{
    // ── Regex fields (moved from TranslationService.cs) ──

    internal static readonly Regex RawMarkupTagRegex = new(
        pattern: @"<" + TranslationConstants.StageDirectionGuard + @"[^>]+>",
        options: RegexOptions.CultureInvariant
    );

    internal static readonly Regex RawPagebreakRegex = new(
        pattern: @"\[page ?break\]",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    // ── Regex fields (moved from TokenSanitization.Repair.cs) ──

    private static readonly Regex NumericXtTokenBadParticleRegex = new(
        pattern: @"(?<t>__XT_PH_(?:MAG|NUM)_[0-9]{4}__)\s*(?:에게서|에게|에서|으로|로)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex RawMagOrBurBadParticleRegex = new(
        pattern: @"(?<t>[+-]?<\s*(?:mag|bur)\s*>)\s*(?:에게서|에게|에서|으로|로)",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex RawMagTagRegex = new(
        pattern: @"[+-]?<\s*mag\s*>",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex RawDurTagRegex = new(
        pattern: @"[+-]?<\s*dur\s*>",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // Static compiled regexes for mag/dur/bur tag normalization in SanitizeModelTranslationText
    private static readonly Regex RawMagTagNormalizeRegex = new(
        @"(?<sign>[+-]?)<\s*mag\s*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex RawDurTagNormalizeRegex = new(
        @"(?<sign>[+-]?)<\s*dur\s*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex RawBurTagNormalizeRegex = new(
        @"(?<sign>[+-]?)<\s*bur\s*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // ── Sanitize ──

    internal static string RemoveBrokenXtTokenMarkers(string text)
    {
        var idx = text.IndexOf("__XT_", StringComparison.Ordinal);
        if (idx < 0)
        {
            return text;
        }

        var sb = new StringBuilder(capacity: text.Length);
        var cursor = 0;

        while (idx >= 0)
        {
            sb.Append(text.AsSpan(cursor, idx - cursor));

            var m = TranslationConstants.XtTokenRegex.Match(text, idx);
            if (m.Success && m.Index == idx)
            {
                sb.Append(m.Value);
                cursor = idx + m.Length;
            }
            else
            {
                var end = idx;
                while (end < text.Length && IsXtTokenChar(text[end]))
                {
                    end++;
                }
                cursor = end;
            }

            idx = text.IndexOf("__XT_", cursor, StringComparison.Ordinal);
        }

        sb.Append(text.AsSpan(cursor));
        return sb.ToString();
    }

    private static bool IsXtTokenChar(char c)
        => c == '_' || (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    internal static string SanitizeModelTranslationText(string text)
        => SanitizeModelTranslationText(text, inputText: null);

    internal static string SanitizeModelTranslationText(string text, string? inputText)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var working = text;

        if (working.IndexOf('<') >= 0)
        {
            if (!string.IsNullOrWhiteSpace(inputText) && inputText.IndexOf('<') >= 0)
            {
                if (inputText.IndexOf("<mag", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    working = RawMagTagNormalizeRegex.Replace(
                        working,
                        m => m.Groups["sign"].Value + "<mag>"
                    );
                }
                if (inputText.IndexOf("<dur", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    working = RawDurTagNormalizeRegex.Replace(
                        working,
                        m => m.Groups["sign"].Value + "<dur>"
                    );
                }
                if (inputText.IndexOf("<bur", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    working = RawBurTagNormalizeRegex.Replace(
                        working,
                        m => m.Groups["sign"].Value + "<bur>"
                    );
                }

                var allowed = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in RawMarkupTagRegex.Matches(inputText))
                {
                    allowed.Add(m.Value);
                }

                if (allowed.Count == 0)
                {
                    working = RawMarkupTagRegex.Replace(working, "");
                }
                else
                {
                    working = RawMarkupTagRegex.Replace(working, m => allowed.Contains(m.Value) ? m.Value : "");
                }
            }
            else
            {
                working = RawMarkupTagRegex.Replace(working, "");
            }
        }
        if (RawPagebreakRegex.IsMatch(working))
        {
            var allowPagebreak = !string.IsNullOrWhiteSpace(inputText) && RawPagebreakRegex.IsMatch(inputText);
            if (!allowPagebreak)
            {
                working = RawPagebreakRegex.Replace(working, "");
            }
        }

        if (working.IndexOf('\n') >= 0 || working.IndexOf('\r') >= 0)
        {
            working = working.Replace('\r', ' ').Replace('\n', ' ');
        }

        if (!string.IsNullOrWhiteSpace(inputText))
        {
            working = PromptLeakCleaner.StripLeakedPlaceholderInstructions(inputText, working);
        }

        return working;
    }

    // ── Ensure / Validate wrappers ──

    internal static string EnsureTokensPreservedOrRepair(
        string inputText,
        string outputText,
        string context,
        IReadOnlyDictionary<string, string>? glossaryTokenToReplacement = null
    )
    {
        outputText = SanitizeModelTranslationText(outputText, inputText);

        var cleanedOutput = RemoveBrokenXtTokenMarkers(outputText);
        if (!ReferenceEquals(cleanedOutput, outputText) && !string.Equals(cleanedOutput, outputText, StringComparison.Ordinal))
        {
            outputText = cleanedOutput;
        }

        outputText = RepairMagDurSemanticMixups(outputText, inputText);
        outputText = RepairDurTokenMisplacedAfterKoreanTimePhrase(outputText, inputText);
        outputText = RepairKoreanBadParticlesOnNumericPlaceholders(outputText, inputText);

        try
        {
            TokenValidator.ValidateTokensPreserved(inputText, outputText, context);
            TokenValidator.ValidateNotTruncatedOrOmitted(inputText, outputText, context);
            TokenValidator.ValidateRawTagsPreserved(inputText, outputText, context);
            return outputText;
        }
        catch (InvalidOperationException)
        {
            if (TokenValidator.TryRepairTokens(inputText, outputText, glossaryTokenToReplacement, out var repaired))
            {
                TokenValidator.ValidateTokensPreserved(inputText, repaired, context);
                TokenValidator.ValidateNotTruncatedOrOmitted(inputText, repaired, context);
                TokenValidator.ValidateRepairKeptLines(inputText, outputText, context);
                TokenValidator.ValidateRawTagsPreserved(inputText, repaired, context);
                return repaired;
            }

            throw;
        }
    }

    // ── Semantic repair trigger ──

    internal static bool NeedsPlaceholderSemanticRepair(
        string inputText,
        string outputText,
        string targetLang,
        PlaceholderSemanticRepairMode mode
    )
    {
        if (!ShouldConsiderPlaceholderSemanticRepair(inputText, outputText, targetLang, mode))
        {
            return false;
        }

        var tokens = ExtractPlaceholderTokenSets(inputText);
        if (!tokens.HasAny)
        {
            return false;
        }

        return HasPlaceholderSemanticIssues(outputText, tokens, mode);
    }

    /// <summary>
    /// What <see cref="NeedsPlaceholderSemanticRepair"/> found, in words for whoever wrote the translation (the agent
    /// mode's model): the same checks in the same order, null when it finds nothing.
    /// </summary>
    internal static string? DescribePlaceholderSemanticIssue(
        string inputText,
        string outputText,
        string targetLang,
        PlaceholderSemanticRepairMode mode
    )
    {
        if (!ShouldConsiderPlaceholderSemanticRepair(inputText, outputText, targetLang, mode))
        {
            return null;
        }

        var tokens = ExtractPlaceholderTokenSets(inputText);
        foreach (var dur in tokens.DurTokens)
        {
            if (outputText.IndexOf(dur, StringComparison.Ordinal) < 0)
            {
                return $"`{dur}`(지속시간)가 번역문에 없습니다.";
            }

            if (!IsDurationInTimeContext(outputText, dur))
            {
                return $"`{dur}`(지속시간) 바로 뒤에 시간 표현이 없습니다. `{dur}초 동안`처럼 쓰세요.";
            }

            if (IsTokenInAmountContext(outputText, dur))
            {
                return $"`{dur}`(지속시간)가 수량처럼 쓰였습니다(뒤에 '{Following(outputText, dur)}'). `{dur}초 동안`처럼 쓰세요.";
            }
        }

        foreach (var number in tokens.MagTokens.Concat(tokens.NumTokens))
        {
            if (outputText.IndexOf(number, StringComparison.Ordinal) < 0)
            {
                return $"`{number}`(수치)가 번역문에 없습니다.";
            }

            if (IsNumberInTimeContext(outputText, number))
            {
                return $"`{number}`(수치)가 시간으로 읽힙니다(뒤에 '{Following(outputText, number)}'). 수치 바로 앞뒤에 "
                    + "시간 단위(초·분·시간·일·주·년·동안)를 두지 말고, "
                    + $"`{number}의 마법 피해를 줍니다`, `{number}포인트`, `{number}% 감소`처럼 수량 명사 앞에 두세요.";
            }

            if (mode == PlaceholderSemanticRepairMode.Strict && IsNumericTokenInBadParticleContext(outputText, number))
            {
                return $"`{number}`(수치) 바로 뒤에 조사가 붙었습니다(뒤에 '{Following(outputText, number)}'). "
                    + $"`{number}를`, `{number}에게`, `{number}와 체력`처럼 수치에 조사를 붙이지 말고 `체력이 {number} 감소합니다`처럼 명사에 붙이세요.";
            }
        }

        return null;
    }

    private static string Following(string text, string token)
    {
        var at = text.IndexOf(token, StringComparison.Ordinal) + token.Length;
        var rest = text[at..].TrimStart();
        return rest.Length <= 6 ? rest : rest[..6];
    }

    // ── Korean repair ──

    internal static string RepairMagDurSemanticMixups(string outputText, string inputText)
    {
        if (!TryGetSingleMagAndDurPlaceholders(inputText, out var magToken, out var durToken))
        {
            return outputText;
        }

        if (outputText.IndexOf(magToken, StringComparison.Ordinal) < 0 || outputText.IndexOf(durToken, StringComparison.Ordinal) < 0)
        {
            return outputText;
        }

        if (!LooksLikeMagDurSwap(outputText, magToken, durToken))
        {
            return outputText;
        }

        return SwapTokens(outputText, magToken, durToken);
    }

    internal static string RepairKoreanBadParticlesOnNumericPlaceholders(string outputText, string inputText)
    {
        if (string.IsNullOrWhiteSpace(outputText) || string.IsNullOrWhiteSpace(inputText))
        {
            return outputText;
        }

        var working = outputText;

        if (inputText.Contains("__XT_PH_MAG_", StringComparison.Ordinal) || inputText.Contains("__XT_PH_NUM_", StringComparison.Ordinal))
        {
            working = NumericXtTokenBadParticleRegex.Replace(working, m => m.Groups["t"].Value);
        }

        if (inputText.IndexOf("<mag", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("<bur", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            working = RawMagOrBurBadParticleRegex.Replace(working, m => m.Groups["t"].Value);
        }

        return working;
    }

    internal static string RepairDurTokenMisplacedAfterKoreanTimePhrase(string outputText, string inputText)
    {
        if (string.IsNullOrWhiteSpace(outputText) || string.IsNullOrWhiteSpace(inputText))
        {
            return outputText;
        }

        if (outputText.IndexOf('초') < 0 && outputText.IndexOf("동안", StringComparison.Ordinal) < 0)
        {
            return outputText;
        }

        var durToken = TryGetSingleDurToken(inputText);
        if (durToken == null)
        {
            return outputText;
        }

        var esc = Regex.Escape(durToken);

        var subjectPattern = @"(?<subject>[\p{L}\p{N}][\p{L}\p{N} \-'\u2019]{0,40})";
        var timeUnitPattern = @"(?:초간|초|분|시간|일|주|개월|년)";

        var patternA = subjectPattern + @"\s*초\s*동안\s*" + esc + @"\s*" + timeUnitPattern + @"?\s*의";
        var replaced = Regex.Replace(
            outputText,
            patternA,
            m => $"{durToken}초 동안 {m.Groups["subject"].Value.Trim()}의",
            RegexOptions.CultureInvariant
        );

        var patternB = subjectPattern + @"\s*초\s*동안\s*" + esc + @"\s*" + timeUnitPattern + @"?";
        replaced = Regex.Replace(
            replaced,
            patternB,
            m => $"{durToken}초 동안 {m.Groups["subject"].Value.Trim()}",
            RegexOptions.CultureInvariant
        );

        return replaced;
    }

    // ── Private helpers (SemanticRepair) ──

    private static bool ShouldConsiderPlaceholderSemanticRepair(
        string inputText,
        string outputText,
        string targetLang,
        PlaceholderSemanticRepairMode mode
    )
    {
        if (mode == PlaceholderSemanticRepairMode.Off)
        {
            return false;
        }

        if (!LanguageHelper.IsKoreanLanguage(targetLang))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(inputText) || string.IsNullOrWhiteSpace(outputText))
        {
            return false;
        }

        return inputText.Length <= 2000;
    }

    private readonly record struct PlaceholderTokenSets(
        HashSet<string> MagTokens,
        HashSet<string> DurTokens,
        HashSet<string> NumTokens
    )
    {
        public bool HasAny => MagTokens.Count > 0 || DurTokens.Count > 0 || NumTokens.Count > 0;
    }

    private static PlaceholderTokenSets ExtractPlaceholderTokenSets(string inputText)
    {
        var expectedTokens = TokenValidator.ExtractTokens(inputText);
        var magTokens = new HashSet<string>(StringComparer.Ordinal);
        var durTokens = new HashSet<string>(StringComparer.Ordinal);
        var numTokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in expectedTokens)
        {
            if (t.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal))
            {
                durTokens.Add(t);
            }
            else if (t.StartsWith("__XT_PH_MAG_", StringComparison.Ordinal))
            {
                magTokens.Add(t);
            }
            else if (t.StartsWith("__XT_PH_NUM_", StringComparison.Ordinal))
            {
                numTokens.Add(t);
            }
        }

        if (inputText.IndexOf("<dur>", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("< dur", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            durTokens.Add("<dur>");
        }

        if (inputText.IndexOf("<mag>", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("< mag", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            magTokens.Add("<mag>");
        }

        if (inputText.IndexOf("<bur>", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("< bur", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            magTokens.Add("<bur>");
        }

        return new PlaceholderTokenSets(magTokens, durTokens, numTokens);
    }

    private static bool HasPlaceholderSemanticIssues(
        string outputText,
        PlaceholderTokenSets tokens,
        PlaceholderSemanticRepairMode mode
    )
    {
        return HasDurationSemanticIssues(outputText, tokens)
            || HasMagnitudeSemanticIssues(outputText, tokens, mode)
            || HasNumericSemanticIssues(outputText, tokens, mode);
    }

    private static bool HasDurationSemanticIssues(string outputText, PlaceholderTokenSets tokens)
    {
        foreach (var dur in tokens.DurTokens)
        {
            if (outputText.IndexOf(dur, StringComparison.Ordinal) < 0)
            {
                return true;
            }

            if (!IsDurationInTimeContext(outputText, dur))
            {
                return true;
            }

            if (IsTokenInAmountContext(outputText, dur))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMagnitudeSemanticIssues(
        string outputText,
        PlaceholderTokenSets tokens,
        PlaceholderSemanticRepairMode mode
    )
    {
        foreach (var mag in tokens.MagTokens)
        {
            if (outputText.IndexOf(mag, StringComparison.Ordinal) < 0)
            {
                return true;
            }

            if (IsNumberInTimeContext(outputText, mag))
            {
                return true;
            }

            if (mode == PlaceholderSemanticRepairMode.Strict && IsNumericTokenInBadParticleContext(outputText, mag))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNumericSemanticIssues(
        string outputText,
        PlaceholderTokenSets tokens,
        PlaceholderSemanticRepairMode mode
    )
    {
        foreach (var num in tokens.NumTokens)
        {
            if (outputText.IndexOf(num, StringComparison.Ordinal) < 0)
            {
                return true;
            }

            if (IsNumberInTimeContext(outputText, num))
            {
                return true;
            }

            if (mode == PlaceholderSemanticRepairMode.Strict && IsNumericTokenInBadParticleContext(outputText, num))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a duration token has its time expression: any unit right after it counts ("초인 경우", "초면", "주일
    /// 동안"). Reading a unit strictly (<see cref="StartsWithTimeUnit"/>) is for numbers, which must not read as a
    /// time; applied here it sent correct durations back for repair.
    /// </summary>
    private static bool IsDurationInTimeContext(string text, string token)
        => IsTokenInTimeContext(text, token, strictUnit: false, amountAfterIsNoTime: false);

    /// <summary>
    /// Whether a number token reads as a time: a unit right after it, read strictly ("피해를 &lt;mag&gt; 주고" is no
    /// week), or a time word right before it unless an amount follows: "&lt;dur&gt;초 동안 &lt;mag&gt;포인트의 피해", the
    /// prompt's own example, and "매초 &lt;mag&gt;의 피해" are amounts after a time.
    /// </summary>
    private static bool IsNumberInTimeContext(string text, string token)
        => IsTokenInTimeContext(text, token, strictUnit: true, amountAfterIsNoTime: IsTokenInAmountContext(text, token));

    private static bool IsTokenInTimeContext(string text, string token, bool strictUnit, bool amountAfterIsNoTime)
    {
        // Check token followed by time units
        var idx = text.IndexOf(token, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var afterToken = idx + token.Length;
            if (afterToken < text.Length)
            {
                var remaining = text.AsSpan(afterToken).TrimStart();
                if (strictUnit ? StartsWithTimeUnit(remaining) : StartsWithAny(remaining, TimeUnitsAfterToken) && !StartsWithAny(remaining, WordsLikeTimeUnits))
                {
                    return true;
                }
            }
            idx = text.IndexOf(token, afterToken, StringComparison.Ordinal);
        }

        if (amountAfterIsNoTime)
        {
            return false;
        }

        // Check time units followed by token
        string[] timeUnitsBefore = ["초간", "초", "분", "시간", "일", "주", "개월", "년", "동안"];
        foreach (var unit in timeUnitsBefore)
        {
            var unitIdx = text.IndexOf(unit, StringComparison.Ordinal);
            while (unitIdx >= 0)
            {
                var afterUnit = unitIdx + unit.Length;
                if (afterUnit <= text.Length)
                {
                    var remaining = text.AsSpan(afterUnit).TrimStart();
                    if (remaining.StartsWith(token.AsSpan(), StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                unitIdx = text.IndexOf(unit, afterUnit, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static readonly string[] TimeUnitsAfterToken = ["초간", "초", "분", "시간", "일", "주", "개월", "년", "동안", "간"];

    // Syllables that can follow a one-syllable unit and keep it a unit: particles, the copula and time words
    // ("초간", "초 동안" without the space, "초마다", "일째", "분의", "초입니다").
    // Words a duration's unit check took for a unit ("<dur> 주고" is 주다, not a week): a duration followed by one has
    // no time expression. Any other unit start counts for a duration ("초인 경우", "초면", "주일 동안").
    private static readonly string[] WordsLikeTimeUnits =
    [
        "주고", "주며", "주는", "주어", "주면", "주지", "주기", "주게", "주었", "주던", "주도", "주시", "주세",
        "일격", "일으", "일어", "일부", "일정", "일반", "분노", "분명", "분량", "초래", "초과", "간격", "년도",
    ];

    // Not 과: "<mag> 초과" is exceeding.
    private const string TimeUnitFollowers = "간동마후뒤의가이을를에씩내까부도은는만째로보정와당입였쯤짜";

    /// <summary>
    /// "피해를 &lt;mag&gt; 주고", "&lt;mag&gt; 일격", "&lt;mag&gt; 분노": a one-syllable unit that runs on into another
    /// syllable is a word (주다, 일격, 분노), not a unit, unless that syllable is in <see cref="TimeUnitFollowers"/>.
    /// 주는/주도록 are 주다 too; a week count is "주 동안" or "주간".
    /// </summary>
    private static bool StartsWithTimeUnit(ReadOnlySpan<char> text)
    {
        foreach (var unit in TimeUnitsAfterToken)
        {
            if (!text.StartsWith(unit.AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            if (unit.Length > 1 || text.Length == unit.Length || !IsHangulSyllable(text[1]))
            {
                return true;
            }

            var next = text[1];
            if (TimeUnitFollowers.Contains(next) && !(unit == "주" && next is '는' or '도'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHangulSyllable(char c) => c is >= '가' and <= '힣';

    private static bool IsTokenInAmountContext(string text, string token)
    {
        var idx = text.IndexOf(token, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var afterToken = idx + token.Length;
            if (afterToken < text.Length)
            {
                var remaining = text.AsSpan(afterToken).TrimStart();

                // token followed by amount units
                if (StartsWithAny(remaining, "%", "퍼센트", "만큼", "점", "포인트", "수치"))
                {
                    return true;
                }

                // token followed by 의 + (피해|회복|흡수)
                if (remaining.StartsWith("의", StringComparison.Ordinal))
                {
                    var afterUi = remaining.Slice(1).TrimStart();
                    if (StartsWithAny(afterUi, "피해", "회복", "흡수"))
                    {
                        return true;
                    }
                }

                // token followed by up to 8 chars then action words (matches original `.{0,8}(keyword)` regex)
                // The keyword is up to 2 chars (6 bytes in UTF-16), so we need to look at up to 8+2=10 chars after token.
                var maxScan = Math.Min(10, text.Length - afterToken);
                if (maxScan > 0)
                {
                    var nearText = text.AsSpan(afterToken, maxScan);
                    if (SpanContainsAny(nearText, "피해", "회복", "증가", "감소", "강화", "약화"))
                    {
                        return true;
                    }
                }
            }
            idx = text.IndexOf(token, afterToken, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsNumericTokenInBadParticleContext(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var idx = text.IndexOf(token, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var afterToken = idx + token.Length;
            if (afterToken < text.Length)
            {
                var remaining = text.AsSpan(afterToken).TrimStart();

                if (StartsWithAny(remaining, "을(를)", "을", "를"))
                {
                    return true;
                }
                if (StartsWithAny(remaining, "에게", "한테", "께"))
                {
                    return true;
                }
                // Check for (와(과)|과|와) followed by (체력|매지카|지구력)
                if (TryMatchParticleWithStat(remaining))
                {
                    return true;
                }
            }
            idx = text.IndexOf(token, afterToken, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryMatchParticleWithStat(ReadOnlySpan<char> remaining)
    {
        string[] particles = ["와(과)", "과", "와"];
        foreach (var particle in particles)
        {
            if (!remaining.StartsWith(particle.AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            var afterParticle = remaining.Slice(particle.Length).TrimStart();
            if (StartsWithAny(afterParticle, "체력", "매지카", "지구력"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsWithAny(ReadOnlySpan<char> span, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (span.StartsWith(candidate.AsSpan(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SpanContainsAny(ReadOnlySpan<char> span, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (span.IndexOf(candidate.AsSpan(), StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // ── Private helpers (Repair) ──

    private static string? TryGetSingleDurToken(string inputText)
    {
        var expectedTokens = TokenValidator.ExtractTokens(inputText);
        var durToken = default(string);
        foreach (var t in expectedTokens)
        {
            if (!t.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal))
            {
                continue;
            }

            if (durToken == null)
            {
                durToken = t;
                continue;
            }

            if (!string.Equals(durToken, t, StringComparison.Ordinal))
            {
                return null;
            }
        }

        if (durToken != null)
        {
            return durToken;
        }

        var rawMatches = RawDurTagRegex.Matches(inputText);
        if (rawMatches.Count == 1)
        {
            return "<dur>";
        }

        return null;
    }

    private static bool TryGetSingleMagAndDurPlaceholders(string inputText, out string magToken, out string durToken)
    {
        magToken = "";
        durToken = "";

        var expectedTokens = TokenValidator.ExtractTokens(inputText);
        var magTokens = new List<string>();
        var durTokens = new List<string>();
        foreach (var t in expectedTokens)
        {
            if (t.StartsWith("__XT_PH_MAG_", StringComparison.Ordinal))
            {
                if (!magTokens.Contains(t))
                {
                    magTokens.Add(t);
                }
            }
            else if (t.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal))
            {
                if (!durTokens.Contains(t))
                {
                    durTokens.Add(t);
                }
            }
        }

        if (magTokens.Count == 1 && durTokens.Count == 1)
        {
            magToken = magTokens[0];
            durToken = durTokens[0];
            return true;
        }

        var rawMag = RawMagTagRegex.Matches(inputText);
        var rawDur = RawDurTagRegex.Matches(inputText);
        if (rawMag.Count == 1 && rawDur.Count == 1)
        {
            magToken = "<mag>";
            durToken = "<dur>";
            return true;
        }

        return false;
    }

    private static bool LooksLikeMagDurSwap(string text, string magToken, string durToken)
    {
        var magInTime = IsNumberInTimeContext(text, magToken);
        var durInAmount = IsTokenInAmountContext(text, durToken);
        if (!magInTime || !durInAmount)
        {
            return false;
        }

        var durInTime = IsDurationInTimeContext(text, durToken);
        var magInAmount = IsTokenInAmountContext(text, magToken);
        return !(durInTime && magInAmount);
    }

    private static string SwapTokens(string text, string a, string b)
    {
        const string tmp = "__XT_SWAP_TMP__";
        var working = text.Replace(a, tmp, StringComparison.Ordinal);
        working = working.Replace(b, a, StringComparison.Ordinal);
        working = working.Replace(tmp, b, StringComparison.Ordinal);
        return working;
    }
}
