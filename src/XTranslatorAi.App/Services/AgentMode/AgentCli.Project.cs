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
    /// <summary>A term as the agent writes it: {"source": "Fury", "target": "분노", "note": "..", "hint": false}.</summary>
    internal sealed record TermLine(string? Source, string? Target, string? Note, bool Hint);

    // context <프로젝트> shows the project's context; context <프로젝트> <파일> saves the file's text as it, --clear
    // removes it. Every request carries it at the head of its instructions.
    private static async Task ContextAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        if (c.Rest.Count <= 1 && !c.Clear)
        {
            var saved = await db.TryGetProjectContextAsync(c.Ct);
            if (saved == null || string.IsNullOrWhiteSpace(saved.ContextText))
            {
                Console.WriteLine($"프로젝트 문맥: 없음. 사용자가 알려 준 배경·인물 관계·말투를 파일에 써서 context {project.Key} <파일>로 저장하면 모든 요청의 지시에 들어갑니다.");
                return;
            }

            Console.WriteLine($"프로젝트 문맥 ({saved.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm} 저장, {saved.ContextText.Trim().Length}자):");
            Console.WriteLine(saved.ContextText.Trim());
            return;
        }

        c.RefuseWhileAppRuns();
        if (c.Clear)
        {
            await db.ClearProjectContextAsync(c.Ct);
            Console.WriteLine("프로젝트 문맥을 지웠습니다.");
        }
        else
        {
            var file = c.Arg(1, "문맥 파일");
            if (!File.Exists(file)) throw new AgentModeException($"문맥 파일이 없습니다: {file}");
            var text = (await ReadAgentFileAsync(file, c.Ct)).Trim();
            if (text.Length == 0) throw new AgentModeException("문맥 파일이 비어 있습니다. 지우려면 --clear를 쓰세요.");
            await db.UpsertProjectContextAsync(text, c.Ct);
            Console.WriteLine($"프로젝트 문맥을 저장했습니다({text.Length}자). 다음 요청부터 지시에 들어갑니다(GUI의 프로젝트 문맥 탭에도 보입니다).");
        }

        await RemakeWaitingAsync(c, project, "바뀐 문맥으로", "옛 문맥으로");
    }

    // terms <프로젝트> [--find 낱말] lists the project glossary; terms <프로젝트> <파일> adds or changes the file's terms
    // ([{"source": "Fury", "target": "분노"}]); terms <프로젝트> <영어> --clear removes that source's entries.
    private static async Task TermsAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        if (c.Rest.Count <= 1)
        {
            var all = await db.GetGlossaryAsync(c.Ct);
            var shown = all.Where(e => e.Enabled)
                .Where(e => c.Find == null || e.SourceTerm.Contains(c.Find, StringComparison.OrdinalIgnoreCase) || e.TargetTerm.Contains(c.Find, StringComparison.Ordinal))
                .OrderBy(e => e.SourceTerm, StringComparer.OrdinalIgnoreCase).ToList();
            var off = all.Count(e => !e.Enabled);
            Console.WriteLine($"프로젝트 용어집: {shown.Count}" + (c.Find != null ? $" ('{c.Find}'이(가) 든 것)" : "")
                              + (off > 0 ? $" (꺼 둔 후보 {off}은 빼고)" : ""));
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

        c.RefuseWhileAppRuns();
        if (c.Clear)
        {
            var source = c.Arg(1, "지울 영어 용어").Trim();
            var gone = (await db.GetGlossaryAsync(c.Ct))
                .Where(e => string.Equals(e.SourceTerm.Trim(), source, StringComparison.OrdinalIgnoreCase)).ToList();
            if (gone.Count == 0) throw new AgentModeException($"프로젝트 용어집에 '{source}'이(가) 없습니다. terms {project.Key} --find {source}로 찾아보세요.");
            foreach (var e in gone) await db.DeleteGlossaryEntryAsync(e.Id, c.Ct);
            Console.WriteLine($"지움: {string.Join(", ", gone.Select(e => $"{e.SourceTerm} → {e.TargetTerm}"))}");
        }
        else
        {
            var file = c.Arg(1, "용어 파일");
            if (!File.Exists(file)) throw new AgentModeException($"용어 파일이 없습니다: {file}");
            foreach (var line in await SaveTermsAsync(db, ParseTerms(await ReadAgentFileAsync(file, c.Ct)), c.Ct)) Console.WriteLine(line);
        }

        await RemakeWaitingAsync(c, project, "바뀐 용어집으로", "옛 용어집으로");
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
        var blank = list.Where(t => string.IsNullOrWhiteSpace(t.Source) || string.IsNullOrWhiteSpace(t.Target)).ToList();
        if (list.Count == 0 || blank.Count > 0)
        {
            throw new AgentModeException("source와 target이 다 있는 용어만 쓰세요([{\"source\": \"영어\", \"target\": \"한국어\"}]). 아무것도 저장하지 않았습니다."
                                         + (blank.Count > 0 ? $" 빈 항목: {string.Join(", ", blank.Select(t => t.Source ?? t.Target ?? "(빈 항목)"))}" : ""));
        }

        return list;
    }

    /// <summary>
    /// Adds the terms as the GUI's 추가 button does (whole words, forced, priority 10); a hint is sent as a hint only. A
    /// source already in the glossary takes the new target, and the line says what it had.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> SaveTermsAsync(ProjectDb db, IReadOnlyList<TermLine> terms, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var term in terms)
        {
            var (source, target) = (term.Source!.Trim(), term.Target!.Trim());
            var outcome = await db.UpsertGlossaryAsync(new GlossaryUpsertRequest(null, source, target, Enabled: true, Priority: 10,
                GlossaryMatchMode.WordBoundary, term.Hint ? GlossaryForceMode.PromptOnly : GlossaryForceMode.ForceToken,
                string.IsNullOrWhiteSpace(term.Note) ? null : term.Note.Trim()), cancellationToken);
            lines.Add(!outcome.Updated ? $"추가: {source} → {target}"
                : string.Equals((outcome.PreviousTarget ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase) ? $"그대로: {source} → {target}"
                : $"바꿈: {source} → {target} (이전: {outcome.PreviousTarget})");
        }

        return lines;
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
