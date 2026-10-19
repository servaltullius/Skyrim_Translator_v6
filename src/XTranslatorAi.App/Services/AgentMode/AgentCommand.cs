using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// One agent command's arguments and what every command shares: the session, the settings, the options taken from the
/// arguments, and the positional arguments left (<see cref="Rest"/>; a command may still take an option of its own,
/// such as open's --strings, with <see cref="Option"/>).
/// </summary>
internal sealed class AgentCommand
{
    private AgentCommand(List<string> rest) => Rest = rest;

    public List<string> Rest { get; }
    public CancellationToken Ct => CancellationToken.None;
    public bool Help { get; private init; }
    public string Work { get; private init; } = "";
    public AppSettings Settings { get; private init; } = null!;
    public GlobalProjectDbService GlobalDb { get; private init; } = null!;
    public string? ProjectsRoot { get; private init; }
    public string? Find { get; private init; }
    public string? Rec { get; private init; }
    public string? Ids { get; private init; }
    public string? After { get; private init; }
    public string? Count { get; private init; }
    public bool All { get; private init; }
    public bool Undo { get; private init; }
    public string? To { get; private init; }
    public string? Note { get; private init; }
    public bool Clear { get; private init; }
    public AgentLane? Lane { get; private init; }
    public AgentSession Session { get; private init; } = null!;

    /// <summary>Takes the shared options out of the arguments; a bad --lane is refused here.</summary>
    public static AgentCommand Parse(List<string> rest)
    {
        var c = new AgentCommand(rest);
        var help = rest.Contains("--help") || rest.Contains("-h") || rest.Contains("/?");
        var work = c.Option("--work") ?? Path.Combine(Environment.CurrentDirectory, "work");
        var settings = new AppSettingsStore().Load();
        var globalDb = new GlobalProjectDbService(new BuiltInGlossaryService());
        var projectsRoot = c.Option("--projects");
        var find = c.Option("--find");
        var rec = c.Option("--rec");
        var ids = c.ListOption("--id");
        var after = c.Option("--after");
        var count = c.Option("--count");
        var all = rest.Remove("--all");
        var undo = rest.Remove("--undo");
        var to = c.Option("--to");
        var note = c.Option("--note");
        var clear = rest.Remove("--clear");
        var lane = AgentLane.Parse(c.Option("--lane"));
        return new AgentCommand(rest)
        {
            Help = help, Work = work, Settings = settings, GlobalDb = globalDb, ProjectsRoot = projectsRoot, Find = find, Rec = rec,
            Ids = ids, After = after, Count = count, All = all, Undo = undo, To = to, Note = note, Clear = clear, Lane = lane,
            Session = new AgentSession(work, globalDb, new GameStringService(), settings, projectsRoot),
        };
    }

    /// <summary>The value after an option, taken out of the arguments; null when it is not given.</summary>
    public string? Option(string name)
    {
        var i = Rest.IndexOf(name);
        if (i < 0 || i + 1 >= Rest.Count) return null;
        var value = Rest[i + 1];
        Rest.RemoveRange(i, 2);
        return value;
    }

    /// <summary>
    /// An option whose value is a list of numbers ("--id 12,40"), with the list's pieces gathered when a shell passed them
    /// as separate arguments (a PowerShell function's 1008,383 arrives as 1008 and 383).
    /// </summary>
    public string? ListOption(string name)
    {
        var i = Rest.IndexOf(name);
        if (i < 0 || i + 1 >= Rest.Count) return null;
        var end = i + 1;
        while (end + 1 < Rest.Count && IsNumberList(Rest[end + 1])) end++;
        var value = string.Join(",", Rest.GetRange(i + 1, end - i).Select(part => part.Trim(',', ' ')));
        Rest.RemoveRange(i, end - i + 1);
        return value;

        static bool IsNumberList(string text) => text.Length > 0 && text.All(ch => char.IsAsciiDigit(ch) || ch is ',' or ' ');
    }

    public string Arg(int index, string what)
        => index < Rest.Count ? Rest[index] : throw new AgentModeException($"{what}을(를) 지정하세요. (tullius.cmd help)");

    public void RefuseWhileAppRuns()
    {
        if (AgentCli.AppIsRunning())
        {
            throw new AgentModeException("번역기 앱(TulliusTranslator)이 켜져 있습니다. 같은 프로젝트를 함께 쓰지 않도록 앱을 닫고 다시 실행하세요.");
        }
    }

    public async Task<List<StringEntry>> RowsAsync(ProjectDb db)
    {
        var rows = new List<StringEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, Ct);
            rows.AddRange(page);
            if (page.Count < 1000) break;
        }

        return rows;
    }
}
