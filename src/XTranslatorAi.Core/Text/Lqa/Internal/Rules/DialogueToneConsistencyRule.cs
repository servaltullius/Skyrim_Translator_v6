using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text.Lqa.Internal;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class DialogueToneConsistencyRule
{
    public static Dictionary<string, ToneKind> BuildDialogueGroupMajorities(IReadOnlyList<LqaScanEntry> ordered)
    {
        var groupToTones = new Dictionary<string, List<ToneKind>>(StringComparer.Ordinal);

        var seq = 0;
        var prevWasSeqDialogue = false;

        foreach (var entry in ordered)
        {
            if (entry.Status != StringEntryStatus.Done && entry.Status != StringEntryStatus.Edited)
            {
                continue;
            }

            var recBase = LqaScanner.GetRecBase(entry.Rec);
            if (recBase is not ("DIAL" or "INFO"))
            {
                prevWasSeqDialogue = false;
                continue;
            }

            var edidStem = LqaScanner.NormalizeEdidStem(entry.Edid);
            string groupKey;
            if (!string.IsNullOrWhiteSpace(edidStem))
            {
                groupKey = "edid:" + edidStem;
                prevWasSeqDialogue = false;
            }
            else
            {
                if (!prevWasSeqDialogue)
                {
                    seq++;
                    prevWasSeqDialogue = true;
                }

                groupKey = "seq:" + seq;
            }

            var tone = LqaToneClassifier.Classify(entry.DestText);
            if (!groupToTones.TryGetValue(groupKey, out var list))
            {
                list = new List<ToneKind>();
                groupToTones[groupKey] = list;
            }

            list.Add(tone);
        }

        var majorityByGroup = new Dictionary<string, ToneKind>(StringComparer.Ordinal);
        foreach (var (groupKey, tones) in groupToTones)
        {
            if (LqaToneClassifier.TryGetStrongMajorityTone(tones, out var majority))
            {
                majorityByGroup[groupKey] = majority;
            }
        }

        return majorityByGroup;
    }

    public static void Apply(
        LqaScanEntry entry,
        IReadOnlyDictionary<string, ToneKind> strongDialogueMajority,
        List<LqaIssue> issues
    )
    {
        if (entry.Status != StringEntryStatus.Done && entry.Status != StringEntryStatus.Edited)
        {
            return;
        }

        var recBase = LqaScanner.GetRecBase(entry.Rec);
        if (recBase is not ("DIAL" or "INFO"))
        {
            return;
        }

        var groupKey = ComputeDialogueGroupKeyForIssue(entry);
        if (string.IsNullOrWhiteSpace(groupKey) || !strongDialogueMajority.TryGetValue(groupKey, out var majority))
        {
            return;
        }

        var tone = LqaToneClassifier.Classify(entry.DestText);
        if (tone == ToneKind.Unknown || tone == majority)
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
                Code: "tone_inconsistent",
                Message: $"대사 그룹 내 말투가 섞여있을 수 있습니다. (majority={majority})",
                SourceText: entry.SourceText ?? "",
                DestText: entry.DestText ?? ""
            )
        );
    }

    private static string ComputeDialogueGroupKeyForIssue(LqaScanEntry entry)
    {
        var edidStem = LqaScanner.NormalizeEdidStem(entry.Edid);
        if (!string.IsNullOrWhiteSpace(edidStem))
        {
            return "edid:" + edidStem;
        }

        // Fallback: no stable EDID stem => do not flag tone mismatches (avoid high false positives).
        return "";
    }
}
