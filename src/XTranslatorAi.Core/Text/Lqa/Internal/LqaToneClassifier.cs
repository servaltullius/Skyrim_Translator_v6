using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal;

internal enum ToneKind
{
    Unknown = 0,
    Hamnida,
    Haeyo,
    PlainDa,
    Casual,
}

internal static class LqaToneClassifier
{
    public static ToneKind Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ToneKind.Unknown;
        }

        var cleaned = LqaScanner.StripUiTokens(text).Trim();
        cleaned = cleaned.TrimEnd(
            ' ',
            '\t',
            '\r',
            '\n',
            '.',
            ',',
            '!',
            '?',
            '…',
            '"',
            '\'',
            '”',
            '’',
            ')',
            ']',
            '」',
            '』'
        );

        if (cleaned.Length == 0)
        {
            return ToneKind.Unknown;
        }

        if (EndsWithAny(cleaned, "습니다", "읍니다", "입니다", "합니다", "됩니까", "됩시다", "십시오", "습니까", "나요", "군요"))
        {
            // Note: "나요/군요" can be haeyo-ish; we keep this conservative (only used for majority).
            if (cleaned.EndsWith("나요", StringComparison.Ordinal) || cleaned.EndsWith("군요", StringComparison.Ordinal))
            {
                return ToneKind.Haeyo;
            }

            return ToneKind.Hamnida;
        }

        if (cleaned.EndsWith("요", StringComparison.Ordinal) || cleaned.EndsWith("세요", StringComparison.Ordinal))
        {
            return ToneKind.Haeyo;
        }

        if (cleaned.EndsWith("다", StringComparison.Ordinal) || cleaned.EndsWith("한다", StringComparison.Ordinal))
        {
            return ToneKind.PlainDa;
        }

        if (EndsWithAny(cleaned, "해", "야", "지", "냐", "라"))
        {
            return ToneKind.Casual;
        }

        return ToneKind.Unknown;
    }

    public static bool TryGetStrongMajorityTone(IReadOnlyList<ToneKind> tones, out ToneKind majority)
    {
        majority = ToneKind.Unknown;

        var counts = new Dictionary<ToneKind, int>();
        var total = 0;
        foreach (var tone in tones)
        {
            if (tone == ToneKind.Unknown)
            {
                continue;
            }

            total++;
            counts[tone] = counts.TryGetValue(tone, out var c) ? c + 1 : 1;
        }

        if (total < 4 || counts.Count < 2)
        {
            return false;
        }

        var best = ToneKind.Unknown;
        var bestCount = 0;
        foreach (var (tone, count) in counts)
        {
            if (count > bestCount)
            {
                best = tone;
                bestCount = count;
            }
        }

        if (bestCount < 3)
        {
            return false;
        }

        var ratio = (double)bestCount / total;
        if (ratio < 0.75)
        {
            return false;
        }

        majority = best;
        return true;
    }

    private static bool EndsWithAny(string value, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
