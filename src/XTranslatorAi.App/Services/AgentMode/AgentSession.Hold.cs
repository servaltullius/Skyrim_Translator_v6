using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    /// What a hold did: the lines held, those of them held because the waiting request asked once for them with a line
    /// named (the same text and context, to be given its translation), and the waiting lines of the same English left
    /// as they are: asked apart, in another scene or as another kind of name, they may well be translatable. The agent
    /// holds them if they are not.
    /// </summary>
    public sealed record HoldResult(IReadOnlyList<HeldLine> Held, IReadOnlyList<long> Together, IReadOnlyList<long> SameEnglishWaiting);

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
    /// unless a new one is given. The lines the waiting request asks once for with a named one are held with it, their
    /// reason naming it: held one at a time, the next request asked for them again. The other waiting lines of the same
    /// English are named, not held: holding every one of them held "Come here." in every scene and the same name as a
    /// weapon's, lines the model may well translate.
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
        var named = rows.Select(r => r.Id).ToHashSet();
        var byId = all.ToDictionary(r => r.Id);
        var grouped = ReadGrouped(project);
        var together = new List<HeldLine>();
        foreach (var line in held)
        {
            foreach (var id in AskedWith(grouped, line.Row.Id))
            {
                if (named.Contains(id) || together.Any(t => t.Row.Id == id) || !byId.TryGetValue(id, out var row) || !Waiting(row)
                    || lane != null && !lane.Contains(id))
                {
                    continue;
                }

                together.Add(new HeldLine(row, line.Reason.Length > 0 ? line.Reason + TogetherMark(line.Row.Id) : TogetherMark(line.Row.Id).TrimStart()));
            }
        }

        held.AddRange(together);
        await db.HoldStringsAsync(held.Select(h => (h.Row.Id, h.Reason.Length > 0 ? h.Reason : HeldMark)).ToList(), cancellationToken);

        var heldIds = held.Select(h => h.Row.Id).ToHashSet();
        var english = rows.Select(r => r.SourceText).ToHashSet(StringComparer.Ordinal);
        var same = all.Where(r => !heldIds.Contains(r.Id) && english.Contains(r.SourceText) && Waiting(r) && (lane == null || lane.Contains(r.Id)))
            .Select(r => r.Id).OrderBy(id => id).ToList();
        return new HoldResult(held, together.Select(t => t.Row.Id).OrderBy(id => id).ToList(), same);
    }

    /// <summary>The reason's end on a line held with another (the whole reason when none was given): releasing that line releases it too.</summary>
    private static string TogetherMark(long id) => $" (#{id} 줄과 함께)";

    /// <summary>Whether a held line's note says it was held with that line.</summary>
    internal static bool HeldWith(string note, long id) => note.EndsWith(TogetherMark(id), StringComparison.Ordinal) || note == TogetherMark(id).TrimStart();

    private static bool Waiting(StringEntry row) => row.Status.IsUnfinished();

    /// <summary>The other rows the waiting request asks once for with this one, whichever of them it asks.</summary>
    private static IReadOnlyList<long> AskedWith(IReadOnlyDictionary<long, List<long>> grouped, long id)
    {
        foreach (var (asked, given) in grouped)
        {
            if (asked == id || given.Contains(id))
            {
                return given.Append(asked).Where(other => other != id).ToList();
            }
        }

        return Array.Empty<long>();
    }

    /// <summary>
    /// The rows the waiting requests ask once for (grouped.json, written with each); none without a request. Held without
    /// a lane, a line may be in a lane's request: every lane's is read.
    /// </summary>
    private IReadOnlyDictionary<long, List<long>> ReadGrouped(AgentProject project)
    {
        var paths = new List<string> { GroupedPath(project) };
        if (project.Lane == null && Directory.Exists(ProjectDir(project)))
        {
            paths.AddRange(Directory.GetDirectories(ProjectDir(project), "lane-*").Select(dir => Path.Combine(dir, "grouped.json")));
        }

        var grouped = new Dictionary<long, List<long>>();
        foreach (var path in paths.Where(File.Exists))
        {
            try
            {
                foreach (var (asked, given) in JsonSerializer.Deserialize<Dictionary<long, List<long>>>(File.ReadAllText(path)) ?? new())
                {
                    grouped[asked] = given;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A file another lane is writing now: its lines are held one at a time, as before.
            }
        }

        return grouped;
    }

    /// <summary>
    /// Puts the held ones of the lines back to 대기, with the lines held together with them (TogetherMark); the others
    /// stay as they are. Returns the lines released.
    /// </summary>
    public async Task<IReadOnlyList<HeldLine>> ReleaseAsync(AgentProject project, IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var all = await AllRowsAsync(db, cancellationToken);
        var lane = await LaneIdsAsync(db, project, cancellationToken);
        var rows = RowsById(all, project, lane, ids);
        var holds = await db.GetStringNotesByKindAsync(TranslationConstants.AgentHoldNoteKind, cancellationToken);
        var heldWith = all.Where(r => IsHeld(r, holds) && (lane == null || lane.Contains(r.Id)) && !rows.Contains(r)
                                      && rows.Any(named => HeldWith(holds[r.Id], named.Id)));
        var held = rows.Concat(heldWith).Where(r => IsHeld(r, holds)).Select(r => new HeldLine(r, Reason(holds[r.Id]))).ToList();
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
