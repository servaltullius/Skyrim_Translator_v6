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
        await new BundledFranchiseTmSeedService().EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, c.Ct);
        var options = new PluginReadOptions(PluginGame.SkyrimSpecialEdition, c.Settings.PluginSourceLanguage.Trim(), c.Settings.PluginSourceEncoding,
            strings, MetadataEncoding: c.Settings.PluginMetadataEncoding);
        var (project, rows, pending, glossary) = await c.Session.OpenAsync(plugin, options, c.Settings.PluginTargetLanguage.Trim(), c.Settings.PluginTargetEncoding, batch, c.Ct);
        Console.WriteLine($"프로젝트: {project.Key}");
        Console.WriteLine($"플러그인: {project.PluginPath}");
        Console.WriteLine($"DB: {project.DbPath}");
        Console.WriteLine($"줄: {rows}, 번역할 줄: {pending}, 묶음 크기: {project.BatchSize}");
        Console.WriteLine($"용어집: 이 프로젝트 {glossary.Count}개"
                          + (glossary.Inherited > 0 ? $" (같은 모드의 다른 프로젝트 {string.Join(", ", glossary.From.Take(3))}에서 {glossary.Inherited}개를 가져옴)" : "")
                          + (glossary.Count > 0 ? ". 요청이 이 용어를 따르고, lookup으로 찾을 수 있다." : ""));
        Console.WriteLine($"다음: tullius.cmd next {project.Key}");
    }

    private static void Projects(AgentCommand c)
    {
        foreach (var p in c.Session.Projects())
        {
            Console.WriteLine($"{p.Key}\t{p.PluginPath}{(c.Session.Pending(p) != null ? "\t(요청 대기)" : "")}");
        }
    }

    private static async Task StatusAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트")) with { Lane = c.Lane };
        var (rows, pending, errors, edited) = await c.Session.StatusAsync(project, c.Ct);
        Console.WriteLine($"프로젝트: {project.Key} ({Path.GetFileName(project.PluginPath)})" + (c.Lane != null ? $", 갈래 {c.Lane}" : ""));
        Console.WriteLine($"줄: {rows}, 남은 줄: {pending} (오류 {errors}), 직접 수정: {edited}");
        var request = c.Session.Pending(project);
        Console.WriteLine(request == null ? "대기 중인 요청: 없음" : $"대기 중인 요청: {c.Session.RequestPath(project)} ({request.Chars:N0}자)");
    }

    private static async Task NextAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        Print(c, await c.Session.NextAsync(c.Session.Find(c.Arg(0, "프로젝트")) with { Lane = c.Lane }, c.Ct));
    }

    private static async Task SubmitAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var project = c.Session.Find(c.Arg(0, "프로젝트")) with { Lane = c.Lane };
        var file = c.Arg(1, "답 파일");
        if (!File.Exists(file)) throw new AgentModeException($"답 파일이 없습니다: {file}");
        Print(c, await c.Session.SubmitAsync(project, await ReadAgentFileAsync(file, c.Ct), c.Ct));
    }

    private static void Print(AgentCommand c, AgentStepReport report)
    {
        Console.WriteLine($"저장한 줄: {report.Saved}, 남은 줄: {report.PendingAfter}" + (report.ErrorRows > 0 ? $" (오류 {report.ErrorRows})" : "")
                          + (report.Rejected.Count > 0 ? $", 다시 물을 줄: {report.Rejected.Count}" : ""));
        foreach (var r in report.Rejected)
        {
            var source = Flat(r.Source);
            if (source.Length > 50) source = source[..50] + "…";
            Console.WriteLine($"  - {(r.Id is { } id ? $"id {id}" : "이 줄")} ({source}): {r.Reason}");
        }

        if (report.Problem != null) Console.WriteLine("알림: " + report.Problem);
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
        }
    }

    // Review mode: korean <프로젝트> <한국어판 경로> [--strings <폴더>] links an existing Korean release and fills the
    // rows not yet translated with it; the agent then reviews them a page at a time (show --after --count) and fixes
    // only the wrong lines (edit), and translates the rows the release lacks with next.
    private static async Task KoreanAsync(AgentCommand c)
    {
        c.RefuseWhileAppRuns();
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        var link = await c.Session.LinkKoreanAsync(project, c.Arg(1, "한국어판 경로"), c.Option("--strings"), c.Ct);
        var m = link.Match;
        Console.WriteLine($"한국어판 연결: {m.TextByFieldKey.Count}줄 (빠짐: 한국어판에 없음 {m.NotInPreviousRelease}, 레코드 구성이 바뀜 {m.ChangedRecord}, "
                          + $"영어 그대로 {m.SameAsSource}, 한글 없음 {m.NotTranslated})");
        Console.WriteLine($"한국어판으로 채운 줄: {link.Filled}" + (link.KeptTranslated > 0 ? $", 이미 번역돼 그대로 둔 줄: {link.KeptTranslated}" : ""));
        if (link.DifferentFromGame.Count > 0)
        {
            Console.WriteLine($"바닐라 대사·이름인데 한글패치와 다르게 옮긴 줄: {link.DifferentFromGame.Count} → "
                              + $"tullius.cmd show {project.Key} --id {string.Join(",", link.DifferentFromGame.Take(40))}" + (link.DifferentFromGame.Count > 40 ? " …" : ""));
        }

        Console.WriteLine(link.Untranslated > 0
            ? $"번역할 줄(한국어판에 없음): {link.Untranslated} → tullius.cmd next {project.Key}"
            : "번역할 줄: 0");
        Console.WriteLine($"검수: tullius.cmd check {project.Key}, 이어서 tullius.cmd show {project.Key} --count 40 으로 차례로 읽기");
    }
}
