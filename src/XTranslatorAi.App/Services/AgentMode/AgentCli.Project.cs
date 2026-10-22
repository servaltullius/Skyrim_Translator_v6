using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// The context and terms commands: the project's context (the GUI's 프로젝트 문맥 tab) and glossary (프로젝트 용어집),
/// set by the agent. A mod set in Oblivion's or ESO's time needs the user's setting and names before its first request,
/// and both lived only in the GUI.
/// </summary>
public static partial class AgentCli
{
    /// <summary>
    /// A term as the agent writes it: {"source": "Fury", "target": "분노", "note": "..", "hint": false}, or
    /// {"source": "Fury", "remove": true} to take the source out.
    /// </summary>
    internal sealed record TermLine(string? Source, string? Target, string? Note, bool Hint, bool Remove);

    // context <프로젝트> shows the project's context; context <프로젝트> <파일> saves the file's text as it, --clear
    // removes it. Every request carries it at the head of its instructions.
    private static async Task ContextAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var saved = await db.TryGetProjectContextAsync(c.Ct);
        var current = saved?.ContextText.Trim() ?? "";
        if (c.Rest.Count <= 1 && !c.Clear)
        {
            if (current.Length == 0)
            {
                Console.WriteLine($"프로젝트 문맥: 없음. 사용자가 알려 준 배경·인물 관계·말투를 파일에 써서 context {project.Key} <파일>로 저장하면 모든 요청의 지시에 들어갑니다.");
                return;
            }

            Console.WriteLine($"프로젝트 문맥 ({saved!.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm} 저장, {current.Length}자):");
            Console.WriteLine(current);
            return;
        }

        if (c.Clear && c.Rest.Count > 1) throw new AgentModeException("문맥 파일과 --clear는 함께 쓰지 않습니다. 저장하려면 파일만, 지우려면 --clear만 주세요.");
        c.RefuseWhileAppRuns();
        if (c.Clear)
        {
            if (current.Length == 0)
            {
                Console.WriteLine("프로젝트 문맥이 이미 없습니다.");
                return;
            }

            await db.ClearProjectContextAsync(c.Ct);
            Console.WriteLine("프로젝트 문맥을 지웠습니다(GUI의 프로젝트 문맥 탭에서도 지워집니다).");
        }
        else
        {
            var file = c.Arg(1, "문맥 파일");
            if (!File.Exists(file)) throw new AgentModeException($"문맥 파일이 없습니다: {file}");
            var text = (await ReadAgentFileAsync(file, c.Ct)).Trim();
            if (text.Length == 0) throw new AgentModeException("문맥 파일이 비어 있습니다. 지우려면 --clear를 쓰세요.");
            if (string.Equals(text, current, StringComparison.Ordinal))
            {
                Console.WriteLine("프로젝트 문맥이 이미 이 내용입니다.");
                return;
            }

            await db.UpsertProjectContextAsync(text, c.Ct);
            Console.WriteLine($"프로젝트 문맥을 저장했습니다({text.Length}자). 다음 요청부터 지시에 들어갑니다(GUI의 프로젝트 문맥 탭에도 보입니다).");
        }

        await RemakeWaitingAsync(c, project, "바뀐 문맥으로", "옛 문맥으로");
    }

    // terms <프로젝트> [--find 낱말] lists the project glossary; terms <프로젝트> <파일> adds, changes or removes the
    // file's terms ([{"source": "Fury", "target": "분노"}]); terms <프로젝트> <영어> --clear removes that source.
    private static async Task TermsAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        if (c.Rest.Count <= 1)
        {
            if (c.Clear) throw new AgentModeException($"지울 영어 용어를 쓰세요: terms {project.Key} <영어> --clear");
            var all = await db.GetGlossaryAsync(c.Ct);
            var shown = all.Where(e => e.Enabled)
                .Where(e => c.Find == null || e.SourceTerm.Contains(c.Find, StringComparison.OrdinalIgnoreCase) || e.TargetTerm.Contains(c.Find, StringComparison.Ordinal))
                .OrderBy(e => e.SourceTerm, StringComparer.OrdinalIgnoreCase).ToList();
            var off = all.Count(e => !e.Enabled);
            Console.WriteLine($"프로젝트 용어집: {shown.Count}개" + (c.Find != null ? $" ('{c.Find}'이(가) 든 것)" : "")
                              + (off > 0 ? $" (꺼 둔 항목 {off}개는 빼고)" : ""));
            foreach (var e in shown)
            {
                var marks = new List<string>();
                if (e.ForceMode == GlossaryForceMode.PromptOnly) marks.Add("참고만");
                if (e.MatchMode == GlossaryMatchMode.Regex) marks.Add("정규식");
                if (e.Note is { Length: > 0 } note) marks.Add(note);
                Console.WriteLine($"- {e.SourceTerm} → {e.TargetTerm}" + (marks.Count > 0 ? $" ({string.Join(", ", marks)})" : ""));
            }

            return;
        }

        // An unquoted "Mages Guild --clear" arrives as two words: the source is all of them.
        var terms = c.Clear
            ? new[] { new TermLine(string.Join(" ", c.Rest.Skip(1)), null, null, false, Remove: true) }
            : await ReadTermsFileAsync(c);
        c.RefuseWhileAppRuns();
        var (lines, changed) = await SaveTermsAsync(db, terms, c.Ct);
        foreach (var line in lines) Console.WriteLine(line);
        if (changed) await RemakeWaitingAsync(c, project, "바뀐 용어집으로", "옛 용어집으로");
    }

    private static async Task<IReadOnlyList<TermLine>> ReadTermsFileAsync(AgentCommand c)
    {
        if (c.Rest.Count > 2) throw new AgentModeException($"용어 파일 하나만 주세요. 용어를 지우려면 terms <프로젝트> <영어> --clear입니다. 받은 인자: {string.Join(" ", c.Rest.Skip(1))}");
        var file = c.Rest[1];
        if (!File.Exists(file)) throw new AgentModeException($"용어 파일이 없습니다: {file}");
        return ParseTerms(await ReadAgentFileAsync(file, c.Ct));
    }

    internal static IReadOnlyList<TermLine> ParseTerms(string json)
    {
        List<TermLine>? terms;
        try
        {
            terms = JsonSerializer.Deserialize<List<TermLine>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new AgentModeException("용어 파일 형식이 맞지 않습니다([{\"source\": \"영어\", \"target\": \"한국어\"}]): " + ex.Message);
        }

        var list = terms ?? new();
        // A null item ([{..}, null]) is a blank one, not a crash.
        var blank = list.Where(t => t is null || string.IsNullOrWhiteSpace(t.Source) || !t.Remove && string.IsNullOrWhiteSpace(t.Target)).ToList();
        if (list.Count == 0 || blank.Count > 0)
        {
            throw new AgentModeException("source와 target이 다 있는 용어만 쓰세요([{\"source\": \"영어\", \"target\": \"한국어\"}], 지울 것은 {\"source\": \"영어\", \"remove\": true}). 아무것도 저장하지 않았습니다."
                                         + (blank.Count > 0 ? $" 빈 항목: {string.Join(", ", blank.Select(t => t?.Source ?? t?.Target ?? "(빈 항목)"))}" : ""));
        }

        return list;
    }

    /// <summary>
    /// Saves the terms: one entry per source, as the GUI's 추가 button makes it (whole words, priority 10), forced or
    /// a hint only. A source already in the glossary is replaced, its earlier entries (a hint beside a hint, an
    /// auto-learned candidate) with it, and the line says what it had. Returns the lines and whether anything changed.
    /// </summary>
    internal static async Task<(IReadOnlyList<string> Lines, bool Changed)> SaveTermsAsync(ProjectDb db, IReadOnlyList<TermLine> terms, CancellationToken cancellationToken)
    {
        static string Mode(GlossaryForceMode mode) => mode == GlossaryForceMode.PromptOnly ? "참고만" : "강제";
        var lines = new List<string>();
        var changed = false;
        foreach (var term in terms)
        {
            var source = term.Source!.Trim();
            var existing = (await db.GetGlossaryAsync(cancellationToken))
                .Where(e => string.Equals(e.SourceTerm.Trim(), source, StringComparison.OrdinalIgnoreCase)).ToList();
            if (term.Remove)
            {
                if (existing.Count == 0)
                {
                    lines.Add($"없음: {source} (지울 것이 없습니다)");
                    continue;
                }

                foreach (var e in existing) await db.DeleteGlossaryEntryAsync(e.Id, cancellationToken);
                lines.Add($"지움: {string.Join(", ", existing.Select(e => $"{e.SourceTerm} → {e.TargetTerm}"))}");
                changed = true;
                continue;
            }

            var target = term.Target!.Trim();
            var mode = term.Hint ? GlossaryForceMode.PromptOnly : GlossaryForceMode.ForceToken;
            var note = string.IsNullOrWhiteSpace(term.Note) ? null : term.Note.Trim();
            if (existing.Count == 1 && existing[0] is { Enabled: true, MatchMode: GlossaryMatchMode.WordBoundary } same
                && same.TargetTerm.Trim() == target && same.ForceMode == mode && (note == null || same.Note == note))
            {
                lines.Add($"그대로: {source} → {target}");
                continue;
            }

            foreach (var e in existing) await db.DeleteGlossaryEntryAsync(e.Id, cancellationToken);
            await db.UpsertGlossaryAsync(new GlossaryUpsertRequest(null, source, target, Enabled: true, Priority: 10,
                GlossaryMatchMode.WordBoundary, mode, note), cancellationToken);
            var was = existing.Where(e => e.Enabled).Select(e => e.TargetTerm.Trim() == target ? $"{Mode(e.ForceMode)}" : $"{e.TargetTerm}, {Mode(e.ForceMode)}").ToList();
            lines.Add(existing.Count == 0 ? $"추가: {source} → {target}" + (term.Hint ? " (참고만)" : "")
                : $"바꿈: {source} → {target} ({Mode(mode)}" + (was.Count > 0 ? $"; 이전: {string.Join(" / ", was)})" : ")"));
            changed = true;
        }

        return (lines, changed);
    }

    /// <summary>A waiting request shows what was there before the change: it is made again, so its answer follows the new one.</summary>
    private static async Task RemakeWaitingAsync(AgentCommand c, AgentProject project, string made, string old)
    {
        var (remade, lanesWaiting) = await c.Session.RemakePendingAsync(project, c.Ct);
        if (remade?.Next != null)
        {
            Console.WriteLine($"대기 중이던 요청을 {made} 다시 만들었습니다: {remade.NextRequestPath}. 이 요청을 읽고 답하세요(앞 요청에 쓴 답은 내지 마세요).");
        }

        if (lanesWaiting > 0)
        {
            Console.WriteLine($"알림: 갈래 {lanesWaiting}개에 대기 중인 요청이 있습니다. 그 요청들은 {old} 만들어졌으니 open으로 다시 열어 대기 요청을 지운 뒤 갈래를 이어 가세요.");
        }
    }
}
