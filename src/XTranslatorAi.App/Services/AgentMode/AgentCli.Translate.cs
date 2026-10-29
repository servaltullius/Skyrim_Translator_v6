using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>The translation commands: open, projects, status, next, submit, and korean (review mode's start).</summary>
public static partial class AgentCli
{
    private static async Task OpenAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var strings = c.Option("--strings") ?? (string.IsNullOrWhiteSpace(c.Settings.PluginStringsDirectory) ? null : c.Settings.PluginStringsDirectory.Trim());
        var batch = int.TryParse(c.Option("--batch-size"), out var b) ? b : 30;
        var plugin = c.Arg(0, "플러그인 경로");
        if (!File.Exists(plugin)) throw new AgentModeException($"플러그인이 없습니다: {plugin}");
        var options = new PluginReadOptions(PluginGame.SkyrimSpecialEdition, c.Settings.PluginSourceLanguage.Trim(), c.Settings.PluginSourceEncoding,
            strings, MetadataEncoding: c.Settings.PluginMetadataEncoding);
        var (project, rows, pending, glossary) = await c.Session.OpenAsync(plugin, options, c.Settings.PluginTargetLanguage.Trim(), c.Settings.PluginTargetEncoding, batch, c.Ct);
        Console.WriteLine($"프로젝트: {project.Key}");
        Console.WriteLine($"플러그인: {project.PluginPath}");
        Console.WriteLine($"DB: {project.DbPath}");
        Console.WriteLine($"줄: {rows}, 번역할 줄: {pending}, 묶음 크기: {project.BatchSize}");
        if (c.Session.SeriesTmNote is { } tmNote) Console.WriteLine(tmNote);
        // The plugin was not UTF-8 (or only partly): it was read another way, and the export writes it all as UTF-8.
        if (!string.Equals(project.ReadOptions.SourceEncoding, options.SourceEncoding, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(project.ReadOptions.SourceEncoding == "windows-1252"
                ? "원문 인코딩: windows-1252로 읽었습니다(UTF-8이 아님). 저장하면 출력 인코딩으로 바뀝니다."
                : $"원문 인코딩: UTF-8과 windows-1252 문자열이 섞여 있어 문자열마다 판별해 읽었습니다({project.ReadOptions.SourceEncoding}). 저장하면 모두 출력 인코딩으로 통일됩니다.");
        }
        Console.WriteLine($"용어집: 이 프로젝트 {glossary.Count}개"
                          + (glossary.Inherited > 0 ? $" (같은 모드의 다른 프로젝트 {string.Join(", ", glossary.From.Take(3))}에서 {glossary.Inherited}개를 가져옴)" : "")
                          + (glossary.Count > 0 ? ". 요청이 이 용어를 따르고, lookup으로 찾을 수 있다." : ""));
        // Agent mode uses a saved context whatever the GUI's box says: one generated in the GUI long ago is named here.
        await using (var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct))
        {
            if ((await db.TryGetProjectContextAsync(c.Ct))?.ContextText.Trim() is { Length: > 0 } context)
            {
                Console.WriteLine($"프로젝트 문맥: 있음({context.Length}자). 모든 요청의 지시에 들어간다. 내용은 context {project.Key}로 보고, 맞지 않으면 사용자에게 묻고 바꾸거나 지운다.");
            }
        }

        Console.WriteLine($"다음: tullius.cmd next {project.Key}");
    }

    private static void Projects(AgentCommand c)
    {
        foreach (var p in c.Session.Projects())
        {
            var lanes = c.Session.LanesWaiting(p);
            Console.WriteLine($"{p.Key}\t{p.PluginPath}{(c.Session.Pending(p) != null ? "\t(요청 대기)" : "")}"
                              + (lanes.Count > 0 ? $"\t(갈래 요청 대기: {string.Join(", ", lanes)})" : ""));
        }
    }

    private static async Task StatusAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트")) with { Lane = c.Lane };
        var (rows, pending, errors, edited) = await c.Session.StatusAsync(project, c.Ct);
        Console.WriteLine($"프로젝트: {project.Key} ({Path.GetFileName(project.PluginPath)})" + (c.Lane != null ? $", 갈래 {c.Lane}" : ""));
        Console.WriteLine($"줄: {rows}, 남은 줄: {pending} (오류 {errors}), 직접 수정: {edited}");
        if (await HeldNoteAsync(c, project) is { } held) Console.WriteLine(held);
        var request = c.Session.Pending(project);
        Console.WriteLine(request == null ? "대기 중인 요청: 없음" : $"대기 중인 요청: {c.Session.RequestPath(project)} ({request.Chars:N0}자)");
        if (c.Lane == null && c.Session.LanesWaiting(project) is { Count: > 0 } lanes)
        {
            Console.WriteLine($"갈래 대기 요청: {string.Join(", ", lanes)} → next·submit·status에 --lane {lanes[0]}처럼 갈래를 붙여 이어 하세요.");
        }
    }

    private static async Task NextAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        await PrintAsync(c, await c.Session.NextAsync(c.Session.Find(c.Arg(0, "프로젝트")) with { Lane = c.Lane }, c.Ct));
    }

    private static async Task SubmitAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var project = c.Session.Find(c.Arg(0, "프로젝트")) with { Lane = c.Lane };
        var file = c.Arg(1, "답 파일");
        if (!File.Exists(file)) throw new AgentModeException($"답 파일이 없습니다: {file}");
        await PrintAsync(c, await c.Session.SubmitAsync(project, await ReadAgentFileAsync(file, c.Ct), c.Ct));
    }

    private static async Task PrintAsync(AgentCommand c, AgentStepReport report)
    {
        // next imports the series TM as open does (AgentSession.EnsureSeriesTmAsync): what it took or could not take.
        if (c.Session.SeriesTmNote is { } tmNote) Console.WriteLine(tmNote);
        Console.WriteLine($"저장한 줄: {report.Saved}, 남은 줄: {report.PendingAfter}" + (report.ErrorRows > 0 ? $" (오류 {report.ErrorRows})" : "")
                          + (report.Rejected.Count > 0 ? $", 다시 물을 줄: {report.Rejected.Count}" : ""));
        foreach (var r in report.Rejected)
        {
            var source = Flat(r.Source);
            if (source.Length > 50) source = source[..50] + "…";
            Console.WriteLine($"  - {(r.Id is { } id ? $"id {id}" : "이 줄")} ({source}): {r.Reason}");
        }

        if (report.Problem != null) Console.WriteLine("알림: " + report.Problem);
        if (report.MissingGlobalData.Count > 0)
        {
            Console.WriteLine($"경고: 전체 DB의 {string.Join("·", report.MissingGlobalData)}을(를) 읽지 못해 없이 번역했습니다"
                              + "(용어 고정·한글패치 이름이 빠졌을 수 있음). 사용자에게 알리고, 이번에 저장한 줄은 check로 용어·이름을 확인하세요.");
        }
        if (report.Next != null)
        {
            var chars = report.NextRequestChars > 0 ? report.NextRequestChars : report.Next.Chars;
            Console.WriteLine($"다음 요청: {report.NextRequestPath} ({chars:N0}자, 추정 {chars / 3:N0}토큰)");
            if (report.NewInstructionSections.Count > 0)
            {
                Console.WriteLine($"새 지시 절: {string.Join(", ", report.NewInstructionSections)} → {c.Session.InstructionsPath(report.Project)}에서 읽기");
            }
            Console.WriteLine($"답을 파일에 쓴 뒤: tullius.cmd submit {report.Project.Key} <답 파일>"
                              + (report.Project.Lane is { } own ? $" --lane {own}" : ""));
        }
        else if (report.PendingAfter == 0)
        {
            Console.WriteLine($"번역 완료. 다음: tullius.cmd check {report.Project.Key}, tullius.cmd export {report.Project.Key}");
            // A held line is not 남은 줄: without this the run said 번역 완료 and nothing of the lines left in English.
            if (await HeldNoteAsync(c, report.Project) is { } held) Console.WriteLine($"{held}. 끝 보고에 이 목록을 {Whom(report.Project)} 알리세요.");
        }
    }

    // Review mode: korean <프로젝트> <한국어판 경로> [--strings <폴더>] links an existing Korean release and fills the
    // rows not yet translated with it; the agent then reviews them a page at a time (show --after --count) and fixes
    // only the wrong lines (edit), and translates the rows the release lacks with next.
    private static async Task KoreanAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        // The options first: "korean <키> --english <영어판> <한국어판>" read --english as the release's path.
        var strings = c.Option("--strings");
        var english = c.Option("--english");
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        var link = await c.Session.LinkKoreanAsync(project, c.Arg(1, "한국어판 경로"), strings, c.Ct, english);
        var m = link.Match;
        Console.WriteLine($"한국어판 연결: {m.TextByFieldKey.Count}줄 (빠짐: 한국어판에 없음 {m.NotInPreviousRelease}, 레코드 구성이 바뀜 {m.ChangedRecord}, "
                          + $"영어 그대로 {m.SameAsSource}, 한글 없음 {m.NotTranslated})");
        if (link.ReadAsCp949) Console.WriteLine("한국어판 인코딩: 옛 방식(CP949)으로 저장된 한국어판이라 UTF-8이 아닌 줄을 CP949로 읽었습니다. 저장하는 번역은 출력 인코딩 그대로입니다.");
        if (link.Cp949Ambiguous is { } ambiguous) Console.WriteLine("알림: " + ambiguous);
        Console.WriteLine($"한국어판으로 채운 줄: {link.Filled}" + (link.KeptTranslated > 0 ? $", 이미 번역돼 그대로 둔 줄: {link.KeptTranslated}" : ""));
        if (link.DifferentFromGame.Count > 0)
        {
            Console.WriteLine($"바닐라 대사·이름인데 한글패치와 다르게 옮긴 줄: {link.DifferentFromGame.Count} → "
                              + $"tullius.cmd show {project.Key} --id {string.Join(",", link.DifferentFromGame.Take(40))}" + (link.DifferentFromGame.Count > 40 ? " …" : ""));
        }

        if (link.EnglishChanged.Count > 0)
        {
            Console.WriteLine((link.ComparedWithEnglish
                                  ? $"영어가 한국어판 때와 달라 채우지 않은 줄: {link.EnglishChanged.Count}"
                                  : $"숫자가 한국어판과 달라 채우지 않은 줄(모드가 바뀌었을 수 있음): {link.EnglishChanged.Count}")
                              + $" → next 요청에 한국어판 번역이 참고로 붙습니다. 보기: tullius.cmd show {project.Key} --id "
                              + string.Join(",", link.EnglishChanged.Take(40)) + (link.EnglishChanged.Count > 40 ? " …" : ""));
        }

        if (!link.ComparedWithEnglish)
        {
            Console.WriteLine("영어가 바뀐 줄은 숫자로만 골랐습니다. 한국어판이 번역한 영어 원본이 있으면 다음에는 korean에 --english \"<영어 원본 경로>\"를 함께 주세요(영어가 바뀐 줄을 모두 골라 번역).");
        }

        Console.WriteLine(link.Untranslated > 0
            ? $"번역할 줄(한국어판에 없거나 바뀜): {link.Untranslated} → tullius.cmd next {project.Key}"
            : "번역할 줄: 0");
        Console.WriteLine($"검수: tullius.cmd check {project.Key}, 이어서 tullius.cmd show {project.Key} --count 40 으로 차례로 읽기");
    }
}
