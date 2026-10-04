using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class BookStructureRules
{
    // The same page break forms the masker protects, so a dropped "[page break]" is reported here too.
    private static readonly Regex PagebreakRegex = new(
        pattern: ProtectedTextKinds.PageBreakPattern,
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    // Match opening and closing HTML tags commonly used in BOOK records.
    private static readonly Regex HtmlTagRegex = new(
        pattern: @"<\s*/?\s*(?:p|br|div|font|img)\b[^>]*>",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private const int MinSourceLengthForShortCheck = 200;

    public static void Apply(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var recBase = LqaScanner.GetRecBase(entry.Rec);
        if (recBase != "BOOK")
        {
            return;
        }

        CheckPagebreakCount(entry, sourceText, destText, issues);
        CheckHtmlTagPairs(entry, sourceText, destText, issues);
        CheckLengthRatio(entry, sourceText, destText, issues);
    }

    private static void CheckPagebreakCount(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var sourceCount = PagebreakRegex.Matches(sourceText).Count;
        var destCount = PagebreakRegex.Matches(destText).Count;

        if (sourceCount == 0 && destCount == 0)
        {
            return;
        }

        if (sourceCount != destCount)
        {
            issues.Add(new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "book_pagebreak_mismatch",
                Message: $"BOOK: 쪽 나눔([pagebreak]) 수 불일치 (원본: {sourceCount}, 번역: {destCount})",
                SourceText: sourceText,
                DestText: destText
            ));
        }
    }

    private static void CheckHtmlTagPairs(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var sourceTags = HtmlTagRegex.Matches(sourceText).Count;
        var destTags = HtmlTagRegex.Matches(destText).Count;

        if (sourceTags == 0 && destTags == 0)
        {
            return;
        }

        if (sourceTags != destTags)
        {
            issues.Add(new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "book_html_tag_mismatch",
                Message: "BOOK: HTML 태그 수 불일치",
                SourceText: sourceText,
                DestText: destText
            ));
        }
    }

    private static void CheckLengthRatio(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var sourceClean = LqaScanner.StripUiTokens(sourceText).Trim();
        var destClean = LqaScanner.StripUiTokens(destText).Trim();

        if (sourceClean.Length <= 0 || destClean.Length <= 0)
        {
            return;
        }

        var ratio = (double)destClean.Length / sourceClean.Length;

        // Korean usually takes about half the characters of English, so a short title such as
        // "Spell Tome: Thunderbolt" → "마법책: 벼락" falls below 0.4 without anything missing.
        // A low ratio only suggests dropped text in a long body.
        var tooShort = ratio < 0.4 && sourceClean.Length >= MinSourceLengthForShortCheck;
        if (tooShort || ratio > 2.5)
        {
            issues.Add(new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "book_length_ratio",
                Message: $"BOOK: 번역 길이 비율 이상 ({ratio:F1}x)",
                SourceText: sourceText,
                DestText: destText
            ));
        }
    }
}
