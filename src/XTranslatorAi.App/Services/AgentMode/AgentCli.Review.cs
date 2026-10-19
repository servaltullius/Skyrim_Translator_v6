using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>The review commands: check (the GUI's quality check), review (확인함 marks), edit and export.</summary>
public static partial class AgentCli
{
    // The quality check as the GUI runs it on the whole project, with each row's translation by id.
    private static async Task<(List<LqaIssue> Issues, Dictionary<long, string> Dest)> ScanProjectAsync(AgentCommand c, ProjectDb db)
    {
        var global = await c.GlobalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, c.Ct);
        var rows = new List<StringEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, c.Ct);
            rows.AddRange(page);
            if (page.Count < 1000) break;
        }

        var speakers = await DialogueSpeakerNames.LoadCheckGroupsAsync(db, c.Ct);
        static StringEntryStatus Status(StringEntry e)
            => e.Status == StringEntryStatus.Skipped && !string.IsNullOrWhiteSpace(e.DestText)
               && !string.Equals(e.DestText.Trim(), e.SourceText.Trim(), StringComparison.Ordinal) ? StringEntryStatus.Done : e.Status;
        var entries = rows.Select(e => new LqaScanEntry(e.Id, e.OrderIndex, e.Edid, e.Rec, Status(e), e.SourceText ?? "", e.DestText ?? "",
            Speaker: speakers.GetValueOrDefault(e.Id))).ToList();
        var glossary = GlossaryMerger.Merge(await db.GetGlossaryAsync(c.Ct), global == null ? null : await global.GetGlossaryAsync(c.Ct))
            .Where(e => e.Enabled && e.ForceMode == GlossaryForceMode.ForceToken && e.MatchMode != GlossaryMatchMode.Regex)
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceTerm) && !string.IsNullOrWhiteSpace(e.TargetTerm))
            .OrderByDescending(e => e.Priority).ThenByDescending(e => e.SourceTerm.Length).ToList();
        var memory = global == null ? new List<(string, string)>()
            : (await global.GetTranslationMemoryEntriesAsync("english", "korean", c.Ct)).Select(e => (e.SourceText, e.DestText)).ToList();
        var tmNotes = await db.GetStringNotesByKindAsync("tm_fallback", c.Ct);
        var sourceFix = (await db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, c.Ct)).Keys.ToHashSet();
        var notes = memory.Count > 0 ? LqaScanner.DropTmFallbacksMatchingMemory(tmNotes, entries, memory) : tmNotes;
        var issues = await LqaScanner.ScanAsync(entries, "korean", glossary, tmFallbackNotes: notes,
            referenceNames: memory.Count > 0 ? ReferenceNameIndex.Build(memory) : null, sourceFixIds: sourceFix,
            speakerStyles: await db.GetSpeakerStylesAsync(c.Ct));
        return (issues, rows.ToDictionary(row => row.Id, row => row.DestText ?? ""));
    }

    private static async Task CheckAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var (issues, dest) = await ScanProjectAsync(c, db);
        // Results the user marked "확인함" (looked at and left as they are) are not listed again while the row's
        // translation is the one they were marked with; --all lists them with the mark.
        var marks = await db.GetLqaReviewedMarksAsync(c.Ct);
        bool Reviewed(LqaIssue i) => LqaReviewedMarks.Holds(marks, i.Id, i.Code, dest.GetValueOrDefault(i.Id));
        var reviewedCount = issues.Count(Reviewed);
        // 참고(Info) findings need no fix (a TM line translated anew, say); they are listed only on request.
        // The speaker checks' reference findings are worth a look (a line in another level than the speaker's
        // others), so they are listed with the warnings; the other reference findings need no fix.
        var unreviewed = issues.Where(i => !Reviewed(i)).ToList();
        var shown = c.All ? issues : unreviewed.Where(i => i.Severity != "Info" || i.Code is "speaker_tone_mixed" or "player_tone_inconsistent").ToList();
        var hidden = unreviewed.Count - (c.All ? unreviewed.Count : shown.Count);
        var notes = new List<string>();
        if (hidden > 0) notes.Add($"참고 {hidden}건은 고칠 것 없음");
        if (reviewedCount > 0) notes.Add(c.All ? $"확인함 {reviewedCount}건 포함" : $"확인함 {reviewedCount}건은 숨김");
        Console.WriteLine($"품질검사: {shown.Count}건" + (notes.Count > 0 ? $" ({string.Join(", ", notes)}" + (c.All ? ")" : $", 보려면 check {project.Key} --all)") : ""));
        foreach (var i in shown)
        {
            var label = LqaIssueLabels.Severity(i.Severity) + (Reviewed(i) ? "·확인함" : "");
            Console.WriteLine($"- #{i.Id} [{label}] {LqaIssueLabels.Code(i.Code)}: {IssueMessage(i)}");
            Console.WriteLine($"  영어: {Flat(i.SourceText)}");
            Console.WriteLine($"  번역: {Flat(i.DestText)}");
        }
    }

    // Marks the rows' quality-check results "확인함" (looked at and left as they are; the GUI's button does the same),
    // or with --undo takes the marks off. A mark holds while the row's translation stays as it is.
    private static async Task ReviewAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        var ids = c.Ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var v) ? v : throw new AgentModeException($"--id에 숫자가 아닌 값: {s}")).ToHashSet()
            ?? throw new AgentModeException("확인함으로 표시할 줄을 --id 12,40처럼 지정하세요.");
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        if (c.Undo)
        {
            var off = (await db.GetLqaReviewedMarksAsync(c.Ct)).Keys.Where(key => ids.Contains(key.StringId))
                .Select(key => (key.StringId, key.Code, "")).ToList();
            await db.SetLqaReviewedAsync(off, false, c.Ct);
            Console.WriteLine($"확인함 표시 해제: {off.Count}건");
            return;
        }

        var (issues, dest) = await ScanProjectAsync(c, db);
        var picked = issues.Where(i => ids.Contains(i.Id)).ToList();
        await db.SetLqaReviewedAsync(picked.Select(i => (i.Id, i.Code, dest.GetValueOrDefault(i.Id, ""))).Distinct().ToList(), true, c.Ct);
        foreach (var i in picked)
        {
            Console.WriteLine($"- #{i.Id} [{LqaIssueLabels.Severity(i.Severity)}] {LqaIssueLabels.Code(i.Code)}: {IssueMessage(i)}");
        }

        var marked = picked.Select(i => i.Id).ToHashSet();
        Console.WriteLine($"확인함으로 표시: {picked.Count}건(줄 {marked.Count}개). 번역문이 바뀌면 다시 나옵니다.");
        var none = ids.Where(id => !marked.Contains(id)).OrderBy(id => id).ToList();
        if (none.Count > 0)
        {
            Console.WriteLine($"품질검사 결과가 없는 줄: {string.Join(", ", none.Select(id => "#" + id))}");
        }
    }

    // The TM fallback note is the run's own diagnostic (English, with the row's id): said in words, the detail kept.
    private static string IssueMessage(LqaIssue issue)
        => issue.Code == "tm_fallback"
            ? "게임 번역(TM)과 태그·토큰이 맞지 않아 TM 대신 새로 번역한 줄입니다. 번역이 맞으면 고칠 것 없음. (원인: "
              + System.Text.RegularExpressions.Regex.Replace(Flat(issue.Message).Replace("⏎", " "), @"\s+", " ").Trim() + ")"
            : Flat(issue.Message);

    // Fixes after the quality check: [{"id": 12, "text": "..."}] (the id of requests, check and show; "order" is the
    // row's 순번, as before), saved as the GUI saves a hand edit (수정됨).
    private static async Task EditAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        var file = c.Arg(1, "수정 파일");
        if (!File.Exists(file)) throw new AgentModeException($"수정 파일이 없습니다: {file}");
        List<EditLine>? edits;
        try
        {
            edits = System.Text.Json.JsonSerializer.Deserialize<List<EditLine>>(await File.ReadAllTextAsync(file, c.Ct),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new AgentModeException("수정 파일 형식이 맞지 않습니다([{\"id\": 번호, \"text\": \"새 번역\"}]): " + ex.Message);
        }

        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var rows = await c.RowsAsync(db);
        var byId = rows.ToDictionary(r => r.Id);
        var byOrder = rows.ToDictionary(r => r.OrderIndex);
        StringEntry? Row(EditLine e) => e.Id is { } id ? byId.GetValueOrDefault(id) : e.Order is { } order ? byOrder.GetValueOrDefault(order) : null;
        var list = edits ?? new();
        var missing = list.Where(e => Row(e) == null).Select(e => e.Id is { } id ? $"id {id}" : e.Order is { } o ? $"순번 {o}" : "번호 없음").ToList();
        if (missing.Count > 0) throw new AgentModeException($"없는 줄: {string.Join(", ", missing)}. 아무것도 저장하지 않았습니다.");
        var same = 0;
        foreach (var edit in list)
        {
            var row = Row(edit)!;
            if (string.Equals(row.DestText, edit.Text, StringComparison.Ordinal))
            {
                same++;
                continue;
            }

            await db.UpdateStringTranslationAsync(row.Id, edit.Text, StringEntryStatus.Edited, null, c.Ct);
            Console.WriteLine($"#{row.Id} {Short(row.SourceText ?? "")}: {Flat(row.DestText ?? "")} → {Flat(edit.Text)}");
        }

        Console.WriteLine($"고친 줄: {list.Count - same}" + (same > 0 ? $" (이미 같은 줄 {same}은 그대로)" : "") + $". 다시 확인: tullius.cmd check {project.Key}");
    }

    private static async Task ExportAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        string output;
        if (c.Rest.Count > 1)
        {
            output = Path.GetFullPath(c.Rest[1]);
            if (Directory.Exists(output) || File.Exists(output))
            {
                throw new AgentModeException($"출력 폴더가 이미 있습니다: {output}. 기존 파일을 덮어쓰지 않으니 없는 폴더를 지정하세요(지정하지 않으면 output\\{project.Key}-2처럼 새 폴더를 고릅니다).");
            }
        }
        else
        {
            // An export again after edits goes next to the first one: the plugin writer never overwrites a folder.
            // Next to the work folder: a trial's --work keeps its output with it.
            var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(c.Work)) ?? Environment.CurrentDirectory, "output");
            output = Path.Combine(root, project.Key);
            for (var n = 2; Directory.Exists(output) || File.Exists(output); n++) output = Path.Combine(root, $"{project.Key}-{n}");
        }

        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var document = await PluginReader.ReadAsync(project.PluginPath, project.ReadOptions, c.Ct);
        PluginExportResult result;
        try
        {
            result = await new ProjectWorkspaceService(c.GlobalDb, c.ProjectsRoot).ExportPluginAsync(db, document, new PluginExportOptions(output, project.TargetEncoding), c.Ct);
        }
        catch (IOException ex)
        {
            throw new AgentModeException($"저장하지 못했습니다({output}): {ex.Message}");
        }

        Console.WriteLine($"저장: {result.PluginPath} (바뀐 필드 {result.ChangedFields})");
        foreach (var file in result.Files.Where(f => f != result.PluginPath)) Console.WriteLine($"  {file}");
        var earlier = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(c.Work)) ?? Environment.CurrentDirectory, "output", project.Key);
        if (c.Rest.Count <= 1 && !string.Equals(output, earlier, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"알림: 앞서 저장한 {earlier}는 그대로 두었습니다. 게임에 넣을 것은 이번 폴더입니다.");
        }
    }
}
