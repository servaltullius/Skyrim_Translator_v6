using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text.Lqa.Internal;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// Flags BOOK/QUST/MESG text whose sentence ending differs from most rows of the same field.
/// No single register is right for a field: quest journals (QUST:CNAM) are written in 해라체
/// ("~해야 한다"), UI messages are mostly 합니다체, and the prompt asks to keep the source's tone,
/// so the project's own majority is the reference. Titles and button labels have no sentence ending.
/// </summary>
internal static class RecToneRule
{
    public static Dictionary<string, ToneKind> BuildFieldMajorities(IReadOnlyList<LqaScanEntry> entries)
    {
        var tonesByField = new Dictionary<string, List<ToneKind>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Status != StringEntryStatus.Done && entry.Status != StringEntryStatus.Edited)
            {
                continue;
            }

            var field = GetCheckedField(entry.Rec);
            if (field == null)
            {
                continue;
            }

            if (!tonesByField.TryGetValue(field, out var tones))
            {
                tones = new List<ToneKind>();
                tonesByField[field] = tones;
            }

            tones.Add(LqaToneClassifier.Classify(entry.DestText));
        }

        var majorities = new Dictionary<string, ToneKind>(StringComparer.Ordinal);
        foreach (var (field, tones) in tonesByField)
        {
            if (LqaToneClassifier.TryGetStrongMajorityTone(tones, out var majority))
            {
                majorities[field] = majority;
            }
        }

        return majorities;
    }

    public static void Apply(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        IReadOnlyDictionary<string, ToneKind> fieldMajorities,
        List<LqaIssue> issues
    )
    {
        var field = GetCheckedField(entry.Rec);
        if (field == null || !fieldMajorities.TryGetValue(field, out var majority))
        {
            return;
        }

        var tone = LqaToneClassifier.Classify(destText);
        if (tone == ToneKind.Unknown || tone == majority || IsRhetoricalQuestionAfterHamnida(destText, tone, majority))
        {
            return;
        }

        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "rec_tone",
                Message: $"같은 종류({field}) 항목 대부분과 말투가 다릅니다: 대부분 {LqaToneClassifier.ToDisplay(majority)}, 이 항목 {LqaToneClassifier.ToDisplay(tone)}",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }

    /// <summary>
    /// "…살려두었습니다. 어째서 그녀를 도왔던 걸까요?": written 합니다체 narration may close with a rhetorical
    /// -ㄹ까요 question. Only the last sentence is classified, so the sentence before it decides here.
    /// </summary>
    private static bool IsRhetoricalQuestionAfterHamnida(string destText, ToneKind tone, ToneKind majority)
    {
        if (tone != ToneKind.Haeyo || majority != ToneKind.Hamnida)
        {
            return false;
        }

        var text = destText.TrimEnd(' ', '\t', '\r', '\n', '"', '\'', '”', '’');
        if (!text.EndsWith("까요?", StringComparison.Ordinal))
        {
            return false;
        }

        var lastStart = text.LastIndexOfAny(new[] { '.', '!', '?', '\n' }, text.Length - 2);
        return lastStart > 0 && LqaToneClassifier.Classify(text[..(lastStart + 1)]) == ToneKind.Hamnida;
    }

    private static string? GetCheckedField(string? rec)
    {
        var recBase = LqaScanner.GetRecBase(rec);
        if (recBase is not ("BOOK" or "QUST" or "MESG"))
        {
            return null;
        }

        var trimmed = (rec ?? "").Trim();
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var subtype = colon >= 0 ? trimmed[(colon + 1)..].Trim().ToUpperInvariant() : "";
        if (subtype is "FULL" or "NAME" or "TITLE" or "ITXT")
        {
            return null;
        }

        return subtype.Length == 0 ? recBase : $"{recBase}:{subtype}";
    }
}
