using System;
using System.Collections.Generic;
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

            if (!IsTokenInTimeContext(outputText, dur))
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

            if (IsTokenInTimeContext(outputText, mag))
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

            if (IsTokenInTimeContext(outputText, num))
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

    private static bool IsTokenInTimeContext(string text, string token)
    {
        // Check token followed by time units
        var idx = text.IndexOf(token, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var afterToken = idx + token.Length;
            if (afterToken < text.Length)
            {
                var remaining = text.AsSpan(afterToken).TrimStart();
                if (StartsWithAny(remaining, "초간", "초", "분", "시간", "일", "주", "개월", "년", "동안", "간"))
                {
                    return true;
                }
            }
            idx = text.IndexOf(token, afterToken, StringComparison.Ordinal);
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
        var magInTime = IsTokenInTimeContext(text, magToken);
        var durInAmount = IsTokenInAmountContext(text, durToken);
        if (!magInTime || !durInAmount)
        {
            return false;
        }

        var durInTime = IsTokenInTimeContext(text, durToken);
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
