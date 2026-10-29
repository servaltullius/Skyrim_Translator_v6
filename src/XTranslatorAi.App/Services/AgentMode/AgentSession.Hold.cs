using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// Held lines: lines the agent app's model will not translate by its own rules, set aside so the rest goes on. A line it
/// left out of an answer was asked again ("답에 이 id가 없습니다"), and the run stood at that request; a user's Codex
/// refused a whole mod over one character's age. A held line is 건너뜀 with an agent_hold note (its reason): no request
/// asks for it until it is released, the export keeps its English (only 완료 and 수정됨 are written), a reopened
/// plugin keeps it with its note, and an edit, or the GUI's 다시 번역, takes it back.
/// </summary>
public sealed partial class AgentSession
{
    public const string HeldMark = "보류";

    /// <summary>A held line and its reason ("" when none was given).</summary>
    public sealed record HeldLine(StringEntry Row, string Reason)
    {
        public string Label => Reason.Length == 0 ? HeldMark : $"{HeldMark}: {Reason}";
    }

    /// <summary>
    /// What a hold did: the lines held, and the waiting lines of the same English left as they are. A request asks once
    /// for some of those (same record type and dialogue), and they come back in the next one; others are asked apart,
    /// in another scene or as another kind of name, and may well be translatable. The agent holds them if they are not.
    /// </summary>
    public sealed record HoldResult(IReadOnlyList<HeldLine> Held, IReadOnlyList<long> SameEnglishWaiting);

    /// <summary>The held lines (of the project's lane, when it has one).</summary>
    public async Task<IReadOnlyList<HeldLine>> HeldAsync(AgentProject project, CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var lane = await LaneIdsAsync(db, project, cancellationToken);
        var holds = await db.GetStringNotesByKindAsync(TranslationConstants.AgentHoldNoteKind, cancellationToken);
        return (await AllRowsAsync(db, cancellationToken))
            .Where(r => IsHeld(r, holds) && (lane == null || lane.Contains(r.Id)))
            .Select(r => new HeldLine(r, Reason(holds[r.Id]))).ToList();
    }

    /// <summary>
    /// Holds the lines. Nothing changes when a line is missing, outside the project's lane, or already has a translation
    /// (its translation would stay in the export, and "보류" would say otherwise). A line held again keeps its reason
    /// unless a new one is given. The waiting lines of the same English are named, not held: holding every one of them
    /// held "Come here." in every scene and the same name as a weapon's, lines the model may well translate.
    /// </summary>
    public async Task<HoldResult> HoldAsync(AgentProject project, IReadOnlyCollection<long> ids, string? reason,
        CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var all = await AllRowsAsync(db, cancellationToken);
        var lane = await LaneIdsAsync(db, project, cancellationToken);
        var rows = RowsById(all, project, lane, ids);
        var holds = await db.GetStringNotesByKindAsync(TranslationConstants.AgentHoldNoteKind, cancellationToken);
        var finished = rows.Where(r => r.Status is StringEntryStatus.Done or StringEntryStatus.Edited
                                       || r.Status == StringEntryStatus.Skipped && !IsHeld(r, holds)).ToList();
        if (finished.Count > 0)
        {
            throw new AgentModeException($"이미 번역이 있는 줄이라 보류하지 않습니다: {string.Join(", ", finished.Select(r => "#" + r.Id))}. 아무것도 바꾸지 않았습니다. "
                                         + "남은 줄만 보류합니다(번역을 고치려면 edit).");
        }

        // One line, as the list prints it.
        var given = string.Join(" ", (reason ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var held = rows.Select(r => new HeldLine(r, given.Length > 0 ? given : IsHeld(r, holds) ? Reason(holds[r.Id]) : "")).ToList();
        await db.HoldStringsAsync(held.Select(h => (h.Row.Id, h.Reason.Length > 0 ? h.Reason : HeldMark)).ToList(), cancellationToken);

        var named = rows.Select(r => r.Id).ToHashSet();
        var english = rows.Select(r => r.SourceText).ToHashSet(StringComparer.Ordinal);
        var same = all.Where(r => !named.Contains(r.Id) && english.Contains(r.SourceText)
                                  && r.Status is StringEntryStatus.Pending or StringEntryStatus.Error or StringEntryStatus.InProgress
                                  && (lane == null || lane.Contains(r.Id)))
            .Select(r => r.Id).OrderBy(id => id).ToList();
        return new HoldResult(held, same);
    }

    /// <summary>Puts the held ones of the lines back to 대기; the others stay as they are. Returns the lines released.</summary>
    public async Task<IReadOnlyList<HeldLine>> ReleaseAsync(AgentProject project, IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var rows = RowsById(await AllRowsAsync(db, cancellationToken), project, await LaneIdsAsync(db, project, cancellationToken), ids);
        var holds = await db.GetStringNotesByKindAsync(TranslationConstants.AgentHoldNoteKind, cancellationToken);
        var held = rows.Where(r => IsHeld(r, holds)).Select(r => new HeldLine(r, Reason(holds[r.Id]))).ToList();
        await db.ReleaseHeldStringsAsync(held.Select(h => h.Row.Id).ToList(), cancellationToken);
        return held;
    }

    private static bool IsHeld(StringEntry row, IReadOnlyDictionary<long, string> holds)
        => row.Status == StringEntryStatus.Skipped && holds.ContainsKey(row.Id);

    // A hold given no reason is saved as the mark itself (a note needs a message).
    private static string Reason(string note) => note == HeldMark ? "" : note;

    private static List<StringEntry> RowsById(IReadOnlyList<StringEntry> all, AgentProject project, IReadOnlySet<long>? lane, IReadOnlyCollection<long> ids)
    {
        var byId = all.ToDictionary(r => r.Id);
        var missing = ids.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new AgentModeException($"없는 줄: {string.Join(", ", missing.Select(id => "#" + id))}. 아무것도 바꾸지 않았습니다.");
        // A lane's agent holds its own lines: another lane's request would not be made again, and its answer dropped.
        if (lane != null && ids.Where(id => !lane.Contains(id)).ToList() is { Count: > 0 } outside)
        {
            throw new AgentModeException($"갈래 {project.Lane}의 줄이 아닙니다: {string.Join(", ", outside.Select(id => "#" + id))}. 아무것도 바꾸지 않았습니다. 자기 갈래의 줄만 보류합니다.");
        }

        return ids.Distinct().Select(id => byId[id]).ToList();
    }

    private static async Task<List<StringEntry>> AllRowsAsync(ProjectDb db, CancellationToken cancellationToken)
    {
        var rows = new List<StringEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, cancellationToken);
            rows.AddRange(page);
            if (page.Count < 1000) break;
        }

        return rows;
    }
}
