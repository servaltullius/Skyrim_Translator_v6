using System.Collections.Generic;
using XTranslatorAi.Core.Text.Lqa.Internal;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class RecToneRule
{
    public static void Apply(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var recBase = LqaScanner.GetRecBase(entry.Rec);
        if (recBase is not ("BOOK" or "QUST" or "MESG"))
        {
            return;
        }

        var tone = LqaToneClassifier.Classify(destText);
        if (tone == ToneKind.Unknown)
        {
            return;
        }

        var expected = recBase switch
        {
            "BOOK" => ToneKind.PlainDa,
            "QUST" => ToneKind.Hamnida,
            "MESG" => ToneKind.Hamnida,
            _ => ToneKind.Unknown,
        };

        if (expected == ToneKind.Unknown || tone == expected)
        {
            return;
        }

        var message = recBase == "BOOK"
            ? $"BOOK 톤: 서술체(…다/…한다) 권장 (현재={tone})"
            : $"UI/퀘스트 톤: 합니다체 권장 (현재={tone})";

        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "rec_tone",
                Message: message,
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
