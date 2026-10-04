using System;
using System.Collections.Generic;
using System.Linq;
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

            var tone = ClassifySpeech(entry.DestText);
            if (!groupToTones.TryGetValue(groupKey, out var list))
            {
                list = new List<ToneKind>();
                groupToTones[groupKey] = list;
            }

            list.Add(tone);
            if (IsPluginWideLine(entry))
            {
                if (!groupToTones.TryGetValue(PluginWideKey, out var pluginWide))
                {
                    pluginWide = new List<ToneKind>();
                    groupToTones[PluginWideKey] = pluginWide;
                }

                pluginWide.Add(tone);
            }
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
        if (string.IsNullOrWhiteSpace(groupKey))
        {
            ApplyPluginWide(entry, strongDialogueMajority, issues);
            return;
        }

        if (!strongDialogueMajority.TryGetValue(groupKey, out var majority))
        {
            return;
        }

        var tone = ClassifySpeech(entry.DestText);
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
                Message: $"같은 대화 묶음의 다른 대사와 말투가 다릅니다: 대부분 {LqaToneClassifier.ToDisplay(majority)}, 이 대사 {LqaToneClassifier.ToDisplay(tone)}",
                SourceText: entry.SourceText ?? "",
                DestText: entry.DestText ?? ""
            )
        );
    }

    private const string PluginWideKey = "plugin-info";

    private static bool IsPluginWideLine(LqaScanEntry entry)
        => LqaScanner.GetRecBase(entry.Rec) == "INFO" && string.IsNullOrWhiteSpace(LqaScanner.NormalizeEdidStem(entry.Edid));

    private static void ApplyPluginWide(LqaScanEntry entry, IReadOnlyDictionary<string, ToneKind> strongDialogueMajority, List<LqaIssue> issues)
    {
        if (!IsPluginWideLine(entry) || !strongDialogueMajority.TryGetValue(PluginWideKey, out var majority))
        {
            return;
        }

        var tone = ClassifySpeech(entry.DestText);
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
                Severity: "Info",
                Code: "tone_differs_from_plugin",
                Message: $"이 플러그인 대사 대부분과 말투가 다릅니다(다른 화자일 수 있음): 대부분 {LqaToneClassifier.ToDisplay(majority)}, 이 대사 {LqaToneClassifier.ToDisplay(tone)}",
                SourceText: entry.SourceText ?? "",
                DestText: entry.DestText ?? ""
            )
        );
    }

    // In speech, 해라체 statements mix naturally into 반말 ("가자, 나도 모르겠다."), so both count as 반말.
    // A lone word in 다 or 라 is usually a name ("사만다.", "젤다.", "키아라."), not a speech level.
    private static ToneKind ClassifySpeech(string? text)
    {
        var tone = LqaToneClassifier.Classify(text);
        if (tone != ToneKind.PlainDa)
        {
            return tone;
        }

        return LqaScanner.StripUiTokens(text ?? "").Trim().Any(char.IsWhiteSpace) ? ToneKind.Casual : ToneKind.Unknown;
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
