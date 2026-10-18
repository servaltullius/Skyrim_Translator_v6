using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// One NPC's lines across every topic of the plugin: when nearly all of them are 존댓말 (or 반말), a line in the other
/// level is flagged. The dialogue-group check compares lines of one topic only, so in IDE Thalmor Ondolemar went from
/// 하오 to 해라 to 합니다 to 해요 from one topic to the next with three warnings in all. 합니다체 and 해요체 mix
/// naturally in polite speech, so the levels compared are only 존댓말 and 반말.
/// </summary>
internal static class SpeakerToneConsistencyRule
{
    public const string Code = "speaker_tone_inconsistent";

    public static IReadOnlyDictionary<string, ToneKind> BuildSpeakerMajorities(IReadOnlyList<LqaScanEntry> entries)
    {
        var levels = new Dictionary<string, List<ToneKind>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!IsSpokenLine(entry))
            {
                continue;
            }

            if (!levels.TryGetValue(entry.Speaker!, out var list))
            {
                list = new List<ToneKind>();
                levels[entry.Speaker!] = list;
            }

            list.Add(Level(entry.DestText));
        }

        var majorities = new Dictionary<string, ToneKind>(StringComparer.Ordinal);
        foreach (var (speaker, list) in levels)
        {
            if (LqaToneClassifier.TryGetStrongMajorityTone(list, out var majority))
            {
                majorities[speaker] = majority;
            }
        }

        return majorities;
    }

    public static void Apply(LqaScanEntry entry, IReadOnlyDictionary<string, ToneKind> majorities, List<LqaIssue> issues)
    {
        if (!IsSpokenLine(entry) || !majorities.TryGetValue(entry.Speaker!, out var majority))
        {
            return;
        }

        var level = Level(entry.DestText);
        if (level == ToneKind.Unknown || level == majority)
        {
            return;
        }

        issues.Add(new LqaIssue(
            Id: entry.Id,
            OrderIndex: entry.OrderIndex,
            Edid: entry.Edid,
            Rec: entry.Rec,
            Severity: "Warn",
            Code: Code,
            Message: $"같은 화자의 다른 대사와 말투가 다릅니다: 대부분 {Display(majority)}, 이 대사 {Display(level)}",
            SourceText: entry.SourceText ?? "",
            DestText: entry.DestText ?? ""));
    }

    // NPC lines only: the player's choices (DIAL:FULL, INFO:RNAM) change level with the attitude they pick.
    private static bool IsSpokenLine(LqaScanEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Speaker)
           && entry.Status is StringEntryStatus.Done or StringEntryStatus.Edited
           && string.Equals(entry.Rec, "INFO:NAM1", StringComparison.OrdinalIgnoreCase);

    /// <summary>존댓말 (Hamnida) or 반말 (Casual) by the last sentence's ending; a lone word in 다 is usually a name.</summary>
    private static ToneKind Level(string? text)
    {
        var tone = LqaToneClassifier.Classify(text);
        return tone switch
        {
            ToneKind.Hamnida or ToneKind.Haeyo => ToneKind.Hamnida,
            ToneKind.Casual => ToneKind.Casual,
            ToneKind.PlainDa => LqaScanner.StripUiTokens(text ?? "").Trim().Any(char.IsWhiteSpace) ? ToneKind.Casual : ToneKind.Unknown,
            _ => ToneKind.Unknown,
        };
    }

    private static string Display(ToneKind level) => level == ToneKind.Hamnida ? "존댓말" : "반말";
}
