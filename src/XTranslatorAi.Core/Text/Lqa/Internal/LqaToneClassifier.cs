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

        // A line that ends in one of these nouns has no speech level ("재시작 필요.", "치명적인 피해!", "새 메시지").
        var lastSpace = cleaned.LastIndexOfAny(new[] { ' ', '\t', '\n', ':' });
        if (NounsLikeEndings.Contains(cleaned[(lastSpace + 1)..]))
        {
            return ToneKind.Unknown;
        }

        // Every ㅂ니다/ㅂ니까/ㅂ시다 form: 합니다, 됩니다, 줍니다, 갑니까, 됩시다.
        if (cleaned.Length >= 3
            && EndsWithAny(cleaned, "니다", "니까", "시다")
            && KoreanSyllables.HasFinalBieup(cleaned[^3]))
        {
            return ToneKind.Hamnida;
        }

        if (cleaned.EndsWith("시오", StringComparison.Ordinal))
        {
            return ToneKind.Hamnida;
        }

        if (cleaned.EndsWith("요", StringComparison.Ordinal))
        {
            return ToneKind.Haeyo;
        }

        // 해라체: statements in 다 and imperatives in 라 ("넣어라", "보아라", "명심하라").
        // A bare 라 is often a name ("헤르메우스 모라"), so it is not classified.
        if (cleaned.EndsWith("다", StringComparison.Ordinal) || EndsWithAny(cleaned, PlainImperativeEndings))
        {
            return ToneKind.PlainDa;
        }

        if (EndsWithAny(cleaned, "해", "야", "지", "냐"))
        {
            return ToneKind.Casual;
        }

        return ToneKind.Unknown;
    }

    private static readonly string[] PlainImperativeEndings = { "아라", "어라", "여라", "거라", "너라", "해라", "하라" };

    // Nouns that end like 요 (해요체), 해 or 지 (반말) or 다 (해라체).
    private static readonly HashSet<string> NounsLikeEndings = new(StringComparer.Ordinal)
    {
        "필요", "중요", "주요", "수요", "개요", "요요",
        "피해", "방해", "손해", "재해", "이해", "오해", "견해", "동해", "서해", "남해",
        "메시지", "이미지", "페이지", "지지", "의지", "부지", "토지", "휴지",
        "바다", "시다",
    };

    public static string ToDisplay(ToneKind tone) => tone switch
    {
        ToneKind.Hamnida => "합니다체",
        ToneKind.Haeyo => "해요체",
        ToneKind.PlainDa => "해라체(…다)",
        ToneKind.Casual => "반말",
        _ => "알 수 없음",
    };

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
