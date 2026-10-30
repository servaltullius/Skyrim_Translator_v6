using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

/// <summary>
/// A row's notes (StringNote) and which of them an event on the row takes away, in one table. The rules were spread over
/// the statements that change rows: the agent_hold note, added in one of them, needed three more, and two reviews found
/// the ones left out. A new kind goes in <see cref="Groups"/> (a test fails until it does), and the events follow.
/// </summary>
public static class StringNotePolicy
{
    public enum NoteGroup
    {
        /// <summary>The row was finished from a known translation without a request (TM, the game's text, an earlier release).</summary>
        KnownTranslation,

        /// <summary>A known translation was set aside for a request (its tags did not fit).</summary>
        TmFallback,

        /// <summary>What the user marked on the row: "원문 오류 수정" and the quality check's "확인함".</summary>
        UserMark,

        /// <summary>What agent mode set on the row: a line held, its model would not translate.</summary>
        AgentState,
    }

    public enum RowEvent
    {
        /// <summary>The row made 대기 again to be translated anew (다시 번역).</summary>
        Retranslate,

        /// <summary>A hand edit saved (수정됨): the translation is no longer the known one, nor held.</summary>
        HandEdit,

        /// <summary>A translation saved with a status other than 건너뜀 and 수정됨 (완료, 오류, 대기): no longer held.</summary>
        Translated,

        /// <summary>A held row released to 대기.</summary>
        Released,

        /// <summary>The row finished from a known translation (TM, the game's text, an earlier release); its own note follows.</summary>
        KnownFill,
    }

    /// <summary>The note kinds and their group; the review marks are a prefix (<see cref="TranslationConstants.LqaReviewedNoteKindPrefix"/>).</summary>
    public static readonly IReadOnlyDictionary<string, NoteGroup> Groups = new Dictionary<string, NoteGroup>(StringComparer.Ordinal)
    {
        [TranslationConstants.TmHitNoteKind] = NoteGroup.KnownTranslation,
        [TranslationConstants.GameStringNoteKind] = NoteGroup.KnownTranslation,
        [TranslationConstants.PreviousReleaseNoteKind] = NoteGroup.KnownTranslation,
        [TranslationConstants.TmFallbackNoteKind] = NoteGroup.TmFallback,
        [TranslationConstants.SourceFixNoteKind] = NoteGroup.UserMark,
        [TranslationConstants.AgentHoldNoteKind] = NoteGroup.AgentState,
    };

    public static NoteGroup? GroupOf(string kind)
        => kind.StartsWith(TranslationConstants.LqaReviewedNoteKindPrefix, StringComparison.Ordinal) ? NoteGroup.UserMark
            : Groups.TryGetValue(kind, out var group) ? group : null;

    /// <summary>The kinds of the groups (the review marks' prefix aside), in a fixed order.</summary>
    public static IReadOnlyList<string> KindsOf(params NoteGroup[] groups)
        => Groups.Where(pair => groups.Contains(pair.Value)).Select(pair => pair.Key).OrderBy(kind => kind, StringComparer.Ordinal).ToList();

    /// <summary>The groups an event takes off the row.</summary>
    public static IReadOnlyList<NoteGroup> ClearedBy(RowEvent rowEvent) => rowEvent switch
    {
        RowEvent.Retranslate => new[] { NoteGroup.KnownTranslation, NoteGroup.TmFallback, NoteGroup.UserMark, NoteGroup.AgentState },
        RowEvent.HandEdit or RowEvent.KnownFill => new[] { NoteGroup.KnownTranslation, NoteGroup.TmFallback, NoteGroup.AgentState },
        RowEvent.Translated or RowEvent.Released => new[] { NoteGroup.AgentState },
        _ => throw new ArgumentOutOfRangeException(nameof(rowEvent)),
    };

    /// <summary>The event of saving a translation with this status; null for 건너뜀, which keeps every note.</summary>
    public static RowEvent? SavedAs(StringEntryStatus status) => status switch
    {
        StringEntryStatus.Edited => RowEvent.HandEdit,
        StringEntryStatus.Skipped => null,
        _ => RowEvent.Translated,
    };

    /// <summary>
    /// Whether a note comes back with a translation kept when a row goes out and comes back (set aside by an import, an
    /// XML row kept): all of them, or for a hand edit only the user's marks (its other notes describe a text it no longer has).
    /// </summary>
    public static bool KeptWith(string kind, StringEntryStatus status)
        => status != StringEntryStatus.Edited || GroupOf(kind) == NoteGroup.UserMark;
}
