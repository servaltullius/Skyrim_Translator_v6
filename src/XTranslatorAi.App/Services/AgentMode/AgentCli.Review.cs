using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    private static Task<(List<LqaIssue> Issues, Dictionary<long, string> Dest)> ScanProjectAsync(AgentCommand c, ProjectDb db)
        => ScanProjectAsync(db, c.GlobalDb, c.Ct);

    // The quality check as the GUI runs it on the whole project (ProjectLqaScan), with each row's translation by id.
    internal static async Task<(List<LqaIssue> Issues, Dictionary<long, string> Dest)> ScanProjectAsync(
        ProjectDb db, GlobalProjectDbService globalDb, CancellationToken ct)
    {
        var global = await globalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, ct);
        if (global == null)
        {
            Console.WriteLine("경고: 전체 DB를 열지 못해 전체 용어집·시리즈 TM 없이 검사했습니다(용어·한글패치 이름 표기 결과가 빠질 수 있음).");
        }

        var rows = await ProjectLqaScan.LoadRowsAsync(db, ct);
        IReadOnlyList<GlossaryEntry>? globalGlossary = null;
        try
        {
            if (global != null) globalGlossary = await global.GetGlossaryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unreadable global glossary ended the check with an exception, where the run goes on without it.
            AppLog.Write($"WARN 품질 검사용 전체 용어집을 읽지 못했습니다: {ex.Message}");
            Console.WriteLine("경고: 전체 DB의 전체 용어집을 읽지 못해 그 용어 없이 검사했습니다.");
        }

        // In the project's languages, as the GUI's check reads them (it read english → korean whatever they were).
        var (sourceLang, targetLang) = await AgentSession.LanguagesAsync(db, ct);
        var glossary = ProjectLqaScan.CheckGlossary(targetLang, await db.GetGlossaryAsync(ct), globalGlossary);
        // An unreadable series TM ended the check; the GUI's goes on without the TM name index, and so does this one.
        IReadOnlyList<(string, string)>? memory = null;
        try
        {
            memory = await ProjectLqaScan.LoadReferenceMemoryAsync(global, sourceLang, targetLang, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"WARN 품질 검사용 TM 이름 색인을 읽지 못했습니다: {ex.Message}");
            Console.WriteLine("경고: 전체 DB의 시리즈 TM을 읽지 못해 TM 이름 색인 없이 검사했습니다(한글패치 이름 표기 결과가 빠질 수 있음).");
        }

        var result = await ProjectLqaScan.ScanAsync(db, rows, targetLang, glossary, memory, onProgress: null, ct);
        return (result.Issues, rows.ToDictionary(row => row.Id, row => row.DestText));
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

        // A held line has no translation to check, nor is it 남은 영어: without a word its English went unseen here.
        if (await HeldNoteAsync(c, project) is { } held) Console.WriteLine($"{held}. 이 줄들은 검사하지 않았습니다.");
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
        Console.WriteLine($"확인함으로 표시: {picked.Count}건(줄 {marked.Count}개). 문장을 다듬어도 말투나 남은 영어가 그대로면 표시가 남고, 그 밖에는 번역문이 바뀌면 다시 나옵니다.");
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

    /// <summary>
    /// Items with no new translation: a missing or misnamed key ({"id": 12, "translation": ".."}) read as no text, and
    /// the row was saved empty as 직접 수정, which the export writes, blanking the name in game.
    /// </summary>
    internal static IReadOnlyList<string> WithoutText(IEnumerable<EditLine> edits)
        => edits.Where(e => string.IsNullOrWhiteSpace(e.Text))
            .Select(e => e.Id is { } id ? $"id {id}" : e.Order is { } o ? $"순번 {o}" : "번호 없음").ToList();

    // Fixes after the quality check: [{"id": 12, "text": "..."}] (the id of requests, check and show; "order" is the
    // row's 순번, as before), saved as the GUI saves a hand edit (직접 수정).
    private static async Task EditAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        var file = c.Arg(1, "수정 파일");
        if (!File.Exists(file)) throw new AgentModeException($"수정 파일이 없습니다: {file}");
        List<EditLine>? edits;
        try
        {
            edits = System.Text.Json.JsonSerializer.Deserialize<List<EditLine>>(await ReadAgentFileAsync(file, c.Ct),
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
        if (WithoutText(list) is { Count: > 0 } blank)
        {
            throw new AgentModeException($"새 번역(text)이 없는 항목: {string.Join(", ", blank)}. 아무것도 저장하지 않았습니다. "
                                         + "[{\"id\": 번호, \"text\": \"새 번역\"}]처럼 text를 쓰세요.");
        }

        // A held line's text is its English: an edit keeping it ("영어 그대로 두자") was passed over as unchanged, and the
        // line stayed held, listed at every end.
        var holds = await db.GetHoldNotesAsync(c.Ct);
        var same = 0;
        foreach (var edit in list)
        {
            var row = Row(edit)!;
            var held = ProjectDb.IsHeld(row.Status, row.Id, holds);
            if (!held && string.Equals(row.DestText, edit.Text, StringComparison.Ordinal))
            {
                same++;
                continue;
            }

            await db.UpdateStringTranslationAsync(row.Id, edit.Text!, StringEntryStatus.Edited, null, c.Ct);
            Console.WriteLine($"#{row.Id} {Short(row.SourceText ?? "")}: {Flat(row.DestText ?? "")} → {Flat(edit.Text!)}");
        }

        Console.WriteLine($"고친 줄: {list.Count - same}" + (same > 0 ? $" (이미 같은 줄 {same}은 그대로)" : "") + $". 다시 확인: tullius.cmd check {project.Key}");
        // A line held with an edited one stays held (and English in the export): the edit is the line named's.
        var edited = list.Select(edit => Row(edit)!.Id).ToHashSet();
        var notes = await db.GetHoldNotesAsync(c.Ct);
        var heldWith = notes
            .Where(pair => !edited.Contains(pair.Key) && byId.TryGetValue(pair.Key, out var other) && ProjectDb.IsHeld(other.Status, other.Id, notes)
                           && edited.Any(id => AgentSession.HeldWith(pair.Value, id)))
            .Select(pair => "#" + pair.Key).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (heldWith.Count > 0)
        {
            Console.WriteLine($"알림: 함께 보류했던 줄 {string.Join(", ", heldWith)}은 아직 보류입니다(export 때 영어 그대로). "
                              + "같은 번역을 넣으려면 그 줄도 edit에 넣으세요.");
        }
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
        // The rows written without a translation of their own, counted as the GUI's 저장 확인 counts them: this export
        // said nothing of them, and a run stopped halfway went out half English with only the path printed.
        var unfinished = await ExportUnfinishedRows.CountAsync(db, (await c.RowsAsync(db)).Select(r => (r.Id, r.Status, r.SourceText, r.DestText)), c.Ct);
        PluginExportResult result;
        try
        {
            result = await new ProjectWorkspaceService(c.GlobalDb, c.ProjectsRoot).ExportPluginAsync(db, document, new PluginExportOptions(output, project.TargetEncoding), c.Ct);
        }
        catch (IOException ex)
        {
            throw new AgentModeException($"저장하지 못했습니다({output}): {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            // A translation the writer refuses (a tag or token lost, often in a line the Korean release filled; an empty
            // name; a character the output encoding lacks; lines sharing a string) is named by its field or its 순번,
            // which the agent cannot edit by: the rows' ids are given (PluginExportErrors, as the GUI's save names its rows).
            IReadOnlyList<long> rows;
            try
            {
                rows = await PluginExportErrors.RowIdsAsync(db, document, ex.Message, c.Ct);
            }
            catch (Exception lookup) when (lookup is not OperationCanceledException)
            {
                AppLog.Write($"WARN 저장을 막은 줄을 찾지 못했습니다: {lookup.Message}");
                rows = Array.Empty<long>();
            }

            var reason = PluginExportErrors.Reason(ex.Message);
            var tags = ex is InvalidOperationException;
            throw new AgentModeException(rows.Count > 0
                ? $"저장하지 않았습니다. 줄 {string.Join(", ", rows.Take(40).Select(id => "#" + id))}{(rows.Count > 40 ? " …" : "")}"
                  + (tags ? "의 번역이 원문의 태그·토큰과 맞지 않습니다" : "") + (tags && rows.Count > 1 ? $"(첫 줄의 사유: {reason})" : $": {reason}")
                  + $" → tullius.cmd show {project.Key} --id {string.Join(",", rows.Take(40))}로 보고 edit로 고친 뒤(check로 확인) 다시 export하세요."
                : $"저장하지 않았습니다: {reason}");
        }

        Console.WriteLine($"저장: {result.PluginPath} (바뀐 필드 {result.ChangedFields})");
        foreach (var file in result.Files.Where(f => f != result.PluginPath)) Console.WriteLine($"  {file}");
        if (unfinished.Unfinished > 0)
        {
            Console.WriteLine($"알림: 번역이 끝나지 않은 줄 {unfinished.Unfinished}(오류 {unfinished.Errors}, 대기 {unfinished.Pending})은 영어 그대로 저장했습니다. "
                              + $"tullius.cmd next {project.Key}로 마저 번역한 뒤 다시 export하세요.");
        }

        if (unfinished.Held > 0)
        {
            Console.WriteLine($"알림: 보류한 줄 {unfinished.Held}은 영어 그대로 저장했습니다. 목록: tullius.cmd hold {project.Key}. 끝 보고에 알리세요.");
        }

        if (unfinished.Emptied > 0) Console.WriteLine($"알림: 번역문을 비워 둔 직접 수정 줄 {unfinished.Emptied}은 빈 글로 저장했습니다.");
        var earlier = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(c.Work)) ?? Environment.CurrentDirectory, "output", project.Key);
        if (c.Rest.Count <= 1 && !string.Equals(output, earlier, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"알림: 앞서 저장한 {earlier}는 그대로 두었습니다. 게임에 넣을 것은 이번 폴더입니다.");
        }
    }
}
