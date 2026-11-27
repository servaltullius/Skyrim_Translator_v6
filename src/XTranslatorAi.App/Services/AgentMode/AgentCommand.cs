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

    /// <summary>Whether --work was given (else the work folder is DefaultWork's).</summary>
    public bool WorkGiven { get; private init; }
    public AppSettings Settings { get; private init; } = null!;
    public GlobalProjectDbService GlobalDb { get; private init; } = null!;

    /// <summary>
    /// The folder of the global DBs (--global) when not the user's: a comparison over project copies, and the tests,
    /// opened the user's own global DB, which an open migrates and tops up, and which the user may change between runs.
    /// </summary>
    public string? GlobalRoot { get; private init; }

    /// <summary>The game-string dictionary, from the global folder: a dictionary built by the user lives there.</summary>
    public GameStringService GameStrings { get; private init; } = null!;

    public string? ProjectsRoot { get; private init; }
    public string? Find { get; private init; }
    public string? Rec { get; private init; }
    public string? Ids { get; private init; }
    public string? Except { get; private init; }
    public bool Save { get; private init; }
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
        var givenWork = c.Option("--work");
        var work = givenWork ?? DefaultWork(AgentMcpServer.ExeFolder, Environment.CurrentDirectory);
        var settings = new AppSettingsStore(SettingsPathForTests).Load();
        var globalRoot = GlobalRootOption(c);
        var globalDb = new GlobalProjectDbService(new BuiltInGlossaryService(), globalRoot);
        var gameStrings = new GameStringService(globalRoot);
        var projectsRoot = c.Option("--projects");
        var find = c.Option("--find");
        var rec = c.Option("--rec");
        var ids = c.ListOption("--id");
        var except = c.ListOption("--except");
        var after = c.Option("--after");
        var count = c.Option("--count");
        var all = rest.Remove("--all");
        var undo = rest.Remove("--undo");
        var to = c.Option("--to");
        var note = c.Option("--note");
        var clear = rest.Remove("--clear");
        var save = rest.Remove("--save");
        var lane = AgentLane.Parse(c.Option("--lane"));
        return new AgentCommand(rest)
        {
            Help = help, Work = work, WorkGiven = givenWork != null, Settings = settings, GlobalDb = globalDb, GlobalRoot = globalRoot,
            GameStrings = gameStrings, ProjectsRoot = projectsRoot, Find = find, Rec = rec,
            Ids = ids, Except = except, Save = save, After = after, Count = count, All = all, Undo = undo, To = to, Note = note, Clear = clear, Lane = lane,
            Session = new AgentSession(work, globalDb, gameStrings, settings, projectsRoot, new BundledFranchiseTmSeedService(globalRoot)),
        };
    }

    /// <summary>
    /// The work folder without --work: ./work, which in agent-kit is the kit's. An app that opened the translator folder
    /// rather than agent-kit ran agent-kit's tullius.cmd from there and kept its projects (and output) in a work folder of
    /// its own, which the MCP server (agent-kit's work) did not see: from the translator's own folder it is agent-kit's
    /// work too. Any other folder keeps its own, the one the app's sandbox lets it write.
    /// </summary>
    internal static string DefaultWork(string exeFolder, string currentDirectory)
    {
        var kit = Path.Combine(exeFolder, "agent-kit");
        var inTranslatorFolder = string.Equals(Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(exeFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        return Path.Combine(inTranslatorFolder && Directory.Exists(kit) ? kit : currentDirectory, "work");
    }

    // --global with its folder left out was ignored (the last argument) or took the next option as the folder
    // ("--global --projects P"), and the command went on with the user's own global DB, or projects; "" ended it.
    private static string? GlobalRootOption(AgentCommand c)
    {
        if (!c.Rest.Contains("--global")) return null;
        var value = c.Option("--global");
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
        {
            throw new AgentModeException("--global 뒤에 전역 DB 폴더를 쓰세요(예: --global D:\\시험\\global).");
        }

        return Path.GetFullPath(value);
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

    /// <summary>
    /// The test assembly's: its commands read the user's settings.json, and a write command was refused while the user's
    /// translator window was open, so a test's result depended on the machine it ran on.
    /// </summary>
    internal static string? SettingsPathForTests { get; set; }

    /// <inheritdoc cref="SettingsPathForTests"/>
    internal static bool IgnoreRunningAppForTests { get; set; }

    public void RefuseWhileAppRuns()
    {
        if (!IgnoreRunningAppForTests && AgentCli.AppIsRunning())
        {
            throw new AgentModeException("번역기 앱(TulliusTranslator)이 켜져 있습니다. 같은 프로젝트를 함께 쓰지 않도록 앱을 닫고 다시 실행하세요.");
        }
    }

    /// <summary>
    /// Single write gate for commands targeting an existing project: checks the running app first, then refuses if the
    /// plugin is no longer at the project's path so agent mode stops before touching an old DB after a move.
    /// </summary>
    public void RefuseWrite(AgentProject project)
    {
        RefuseWhileAppRuns();
        if (File.Exists(project.PluginPath)) return;
        throw new AgentModeException(MissingPluginMessage(project));
    }

    private string MissingPluginMessage(AgentProject project)
    {
        var message = $"플러그인이 '{project.PluginPath}'에 없습니다. 파일을 옮겼다면 open \"<새 경로>\"로 다시 연 뒤 이어서 하세요(이 프로젝트에는 더 쓰지 않습니다).";
        if (project.Lane != null)
        {
            message += " 갈래를 맡은 하위 에이전트는 open하지 말고 이 내용을 부모에게 알리세요.";
        }

        var fileName = Path.GetFileName(project.PluginPath);
        var replacements = Session.Projects()
            .Where(other => !string.Equals(other.Key, project.Key, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(Path.GetFileName(other.PluginPath), fileName, StringComparison.OrdinalIgnoreCase)
                            && File.Exists(other.PluginPath))
            .OrderBy(other => other.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (replacements.Count > 0)
        {
            message += " 같은 이름으로 열려 있는 프로젝트: " + string.Join(", ", replacements.Select(other => $"{other.Key} ({other.PluginPath})"));
            // There and back (A -> B -> A): the project in place is the older one, and nothing else said so.
            if (ProjectWorkspaceService.DbLastWriteUtc(project.DbPath) is { } written
                && replacements.Any(other => ProjectWorkspaceService.DbLastWriteUtc(other.DbPath) is { } theirs && theirs < written))
            {
                message += " 다만 이 프로젝트가 더 나중에 작업됐습니다. 그쪽에는 여기서 번역한 줄이 없을 수 있으니, 이어서 하기 전에 사용자에게 알리세요.";
            }
        }

        return message;
    }

    private static string MissingDbMessage(AgentProject project)
        => $"이 프로젝트의 DB가 없습니다. 플러그인을 옮겼다가 되돌렸다면 open \"{project.PluginPath}\"로 다시 여세요(다른 위치에서 하던 번역을 이어서 엽니다)."
           + (project.Lane != null ? " 갈래를 맡은 하위 에이전트는 open하지 말고 이 내용을 부모에게 알리세요." : "");

    /// <summary>
    /// Resolves the project for a CLI command, applies <see cref="RefuseWrite"/> when writing, refuses commands whose
    /// project DB does not exist (only open creates a DB), and emits the missing-plugin read warning or round-trip note.
    /// </summary>
    public AgentProject FindProject(string nameOrKeyOrPath, AgentLane? lane = null, bool write = false)
    {
        var project = Session.Find(nameOrKeyOrPath) with { Lane = lane };
        if (write)
        {
            RefuseWrite(project);
        }

        if (!File.Exists(project.DbPath))
        {
            throw new AgentModeException(File.Exists(project.PluginPath)
                ? MissingDbMessage(project)
                : MissingPluginMessage(project));
        }

        if (!write && !File.Exists(project.PluginPath))
        {
            Console.WriteLine($"경고: 플러그인이 '{project.PluginPath}'에 없습니다. 읽기만 됩니다. 파일을 옮겼다면 open \"<새 경로>\"로 다시 여세요.");
        }

        if (Session.MovedProjectNote is { } note) Console.WriteLine(note);
        return project;
    }

    public Task<List<StringEntry>> RowsAsync(ProjectDb db) => db.GetAllStringsAsync(Ct);
}
