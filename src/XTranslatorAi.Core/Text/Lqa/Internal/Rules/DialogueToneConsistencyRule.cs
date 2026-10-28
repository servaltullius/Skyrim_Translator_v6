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

            if (IsSpeechCheckChoice(entry))
            {
                continue;
            }

            var edidStem = LqaScanner.NormalizeEdidStem(entry.Edid);
            string groupKey;
            if (!string.IsNullOrWhiteSpace(edidStem))
            {
                groupKey = EdidGroupKey(entry, edidStem);
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

                pluginWide.Add(ClassifySpeech(entry.DestText, mergePolite: false));
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

        if (IsSpeechCheckChoice(entry))
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
        // A 하오체 speaker closes sentences with ~지, ~군, ~다 and ~네 as well ("그건 이미 끝난 일이다."), which read as 반말
        // line by line, and asks with "~ㅂ시다" ("그럼 출발합시다!"), which reads as 존댓말: in a group mostly 하오체 they
        // are the same voice. A line plainly 반말 ("그건 네 몫이야.", "~거든", 너, "자네도 알잖아") is still marked, as the
        // speaker check marks it against a decided 하오체, and so is a line with a 해요체 sentence of its own.
        if (tone == ToneKind.Unknown || tone == majority
            || majority == ToneKind.Hao && tone == ToneKind.Casual && !SpeakerToneConsistencyRule.IsPlainCasual(entry.DestText)
            || majority == ToneKind.Hao && tone == ToneKind.Haeyo && SpeakerToneConsistencyRule.IsOnlySharedPolite(entry.DestText))
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
                Message: IsPlayerLine(entry)
                    ? $"같은 대화 묶음의 다른 플레이어 선택지와 말투가 다릅니다: 대부분 {Display(majority)}, 이 선택지 {Display(tone)}"
                    : $"같은 대화 묶음의 다른 대사와 말투가 다릅니다: 대부분 {Display(majority)}, 이 대사 {Display(tone)}",
                SourceText: entry.SourceText ?? "",
                DestText: entry.DestText ?? ""
            )
        );
    }

    private const string PluginWideKey = "plugin-info";

    // A line whose speaker is known is compared with that speaker's other lines (SpeakerToneConsistencyRule). Left in,
    // the speakers who say most of a plugin set its level: Serana Dialogue Add-On's 반말 marked 105 lines, 93 of them
    // Serana's own lines to her father or the Jarl and other NPCs' lines to her.
    // The player's prompts stay out too, as in an EDID group: a prompt with no speaker condition was counted with the
    // NPC lines, and the player's 반말 "그게 뭐지?" was marked against the NPCs' 해요체.
    private static bool IsPluginWideLine(LqaScanEntry entry)
        => LqaScanner.GetRecBase(entry.Rec) == "INFO" && string.IsNullOrWhiteSpace(LqaScanner.NormalizeEdidStem(entry.Edid))
           && string.IsNullOrWhiteSpace(entry.Speaker) && !IsPlayerLine(entry);

    private static void ApplyPluginWide(LqaScanEntry entry, IReadOnlyDictionary<string, ToneKind> strongDialogueMajority, List<LqaIssue> issues)
    {
        if (!IsPluginWideLine(entry) || !strongDialogueMajority.TryGetValue(PluginWideKey, out var majority))
        {
            return;
        }

        var tone = ClassifySpeech(entry.DestText, mergePolite: false);
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

    /// <summary>Whether two versions of a line read at the same known level, as this rule reads them (LqaReviewedMarks).</summary>
    internal static bool SameSpeech(string? before, string? after)
    {
        var tone = ClassifySpeech(before);
        return tone != ToneKind.Unknown && tone == ClassifySpeech(after);
    }

    // In speech, 해라체 statements mix naturally into 반말 ("가자, 나도 모르겠다."), so both count as 반말, and
    // 합니다체 mixes into 해요체 ("환영합니다. 참으로 흥미롭군요."), so both count as 존댓말, as the speaker check
    // reads them. A lone word in 다 or 라 is usually a name ("사만다.", "젤다.", "키아라."), not a speech level.
    // 하오체 is read with the speaker check's endings: the shared classifier took "…있는거요?" for 해요체 and "무슨
    // 뜻이오?" for nothing. The plugin-wide comparison does not read it: there a 하오체 line is another speaker's. (The speaker check's whole reading was tried and dropped: it is tuned for a speaker's lines
    // taken together, and line by line it read 반말's "~네" as 하게체, 102 false results over the user's projects.)
    // The plugin-wide comparison keeps 합니다체 and 해요체 apart (mergePolite false): lines without a known speaker are
    // many people's, and with them merged IDE Thalmor's officials' 반말 was 30 notes against the others' 존댓말.
    private static ToneKind ClassifySpeech(string? text, bool mergePolite = true)
    {
        // A stage direction is not speech: "[양초를 제작하는 방법을 설명한다]" and "…있네요. (서류철을 보여준다)" read as 반말.
        // Nor is a quotation, for every reading below: …이렇게 말했어. "조심하시오." took the quoted 존댓말 for its own.
        var spoken = SpeakerToneConsistencyRule.SpokenPart(StageDirection.Replace(text ?? "", " ")).Trim();
        if (!spoken.Any(char.IsLetter))
        {
            return ToneKind.Unknown;
        }

        if (mergePolite)
        {
            switch (SpeakerToneConsistencyRule.ReadHao(spoken))
            {
                case SpeakerToneConsistencyRule.HaoReading.Hao:
                    return ToneKind.Hao;
                case SpeakerToneConsistencyRule.HaoReading.PoliteBeside:
                    return ToneKind.Haeyo;
            }
        }

        var tone = LqaToneClassifier.Classify(spoken);
        if (tone == ToneKind.Hamnida && mergePolite)
        {
            return ToneKind.Haeyo;
        }

        // "당신은 누구요?" is a 하오체 speaker's as much as a 해요체 one's: such a line is not counted either way.
        if (tone == ToneKind.Haeyo && mergePolite && SpeakerToneConsistencyRule.IsSharedWithHao(spoken))
        {
            return ToneKind.Unknown;
        }

        if (tone == ToneKind.Unknown && mergePolite)
        {
            return SpokenCasual(spoken) ? ToneKind.Casual : ToneKind.Unknown;
        }

        if (tone != ToneKind.PlainDa)
        {
            return tone;
        }

        return LqaScanner.StripUiTokens(spoken).Trim().Any(char.IsWhiteSpace) ? ToneKind.Casual : ToneKind.Unknown;
    }

    // Spoken 반말 endings the shared classifier leaves unread, since a book or a description ending in one is quoting
    // speech: ~거든, ~네 (a lone 네 is the polite "yes"), ~어/아 after ㅆ or ㅄ (있어, 했어, 겠어) and a few common
    // verbs, with laughter at the end set aside ("…것 같아! 하하!").
    private static bool SpokenCasual(string spoken)
    {
        var cleaned = TrailingLaughter.Replace(LqaScanner.StripUiTokens(spoken).Trim().TrimEnd(' ', '.', '!', '?', '…', '~'), "").TrimEnd(' ', '.', ',', '!', '?', '…', '~', '"', '\'', '”', '’');
        if (cleaned.Length < 2)
        {
            return false;
        }

        var last = cleaned[(cleaned.LastIndexOfAny(new[] { ' ', '\t', '\n', '.', ',', '!', '?' }) + 1)..];
        return last.EndsWith("거든", StringComparison.Ordinal)
               || last.Length >= 2 && last.EndsWith("네", StringComparison.Ordinal)
               || last is "같아" or "좋아" or "싫어" or "알아" or "몰라" || last.EndsWith("잖아", StringComparison.Ordinal)
               || cleaned[^1] is '어' or '아' && KoreanSyllables.IsHangulSyllable(cleaned[^2]) && (cleaned[^2] - '가') % 28 is 18 or 20;
    }

    private static readonly System.Text.RegularExpressions.Regex TrailingLaughter = new(
        @"[\s.,!?…~]*(?:하하|호호|후후|흐흐|헤헤|히히|크크|껄껄|ㅎㅎ|ㅋㅋ)[하호후흐헤히크껄ㅎㅋ]*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex StageDirection = new(@"\[[^\]]*\]|\([^)]*\)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string ComputeDialogueGroupKeyForIssue(LqaScanEntry entry)
    {
        var edidStem = LqaScanner.NormalizeEdidStem(entry.Edid);
        if (!string.IsNullOrWhiteSpace(edidStem))
        {
            return EdidGroupKey(entry, edidStem);
        }

        // Fallback: no stable EDID stem => do not flag tone mismatches (avoid high false positives).
        return "";
    }

    // The player's choices (the topic text and an INFO's prompt) are compared with each other, not with the NPC's
    // answers: the player may speak 반말 to an NPC who answers in 존댓말. An INFO's prompt and responses share its EDID,
    // so Serana's 존댓말 answer was marked against the player's 반말 "확실해? 이 흡혈귀들은 네 친구들 아니야?"
    // (1 of the 47 warnings over the user's 56 projects; the other 46, player choices against each other, stay).
    private static string EdidGroupKey(LqaScanEntry entry, string edidStem)
        => (IsPlayerLine(entry) ? "player-edid:" : "edid:") + edidStem;

    private static bool IsPlayerLine(LqaScanEntry entry) => entry.Rec is "DIAL:FULL" or "INFO:RNAM";

    // A persuasion, threat or bribe takes its own attitude ("(위협) 당장 비켜."), as the speaker check reads it: compared
    // with the player's other choices it was marked as unlike them.
    private static bool IsSpeechCheckChoice(LqaScanEntry entry)
        => IsPlayerLine(entry) && SpeakerToneConsistencyRule.SpeechCheck.IsMatch(entry.SourceText ?? "");

    private static string Display(ToneKind tone) => tone == ToneKind.Haeyo ? "존댓말" : LqaToneClassifier.ToDisplay(tone);
}
