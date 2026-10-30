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

/// <summary>
/// The agent mode's commands (TulliusTranslator.exe agent ..., run through agent-kit/tullius.cmd): a subscription app
/// (Claude, Codex, Antigravity) opened on the agent-kit folder runs them; the app's model writes the translations, these
/// only read, protect, validate and save. They never call an AI and never start one. They were a second program
/// (tullius.exe, 81MB) beside the translator; now the translator is the one program, its window or its commands.
/// This file parses and dispatches; the commands are in AgentCli.Translate, .Review, .Read and .Style, and
/// <see cref="AgentCommand"/> holds what they share.
/// </summary>
public static partial class AgentCli
{
    /// <summary>The translator's window holds this while it runs (App.SingleInstanceName).</summary>
    public static bool AppIsRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(App.SingleInstanceName, out var mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // The window runs as administrator (as MO2 setups often do) and this command does not: it is open.
            return true;
        }
    }


    public static Task<int> RunAsync(string[] args) => RunAsync(args, argumentFiles: true);

    /// <param name="argumentFiles">Whether "@파일" stands for the arguments in that file. The MCP server passes its tool's
    /// values as they are: a speech style kept for an NPC whose master left is named "@Dawnguard.esm|01001234", and
    /// was taken for a file that does not exist, so it could not be cleared.</param>
    internal static async Task<int> RunAsync(string[] args, bool argumentFiles)
    {
        try
        {
            if (argumentFiles) args = ExpandArgumentFiles(args);
        }
        catch (AgentModeException ex)
        {
            Console.WriteLine("실패: " + ex.Message);
            return 2;
        }

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        AgentCommand c;
        try
        {
            c = AgentCommand.Parse(args.Skip(1).ToList());
        }
        catch (AgentModeException ex)
        {
            Console.WriteLine("실패: " + ex.Message);
            return 2;
        }

        if (c.Help) command = "help";
        try
        {
            switch (command)
            {
                case "open": await OpenAsync(c); break;
                case "projects": Projects(c); break;
                case "status": await StatusAsync(c); break;
                case "next": await NextAsync(c); break;
                case "submit": await SubmitAsync(c); break;
                case "check": await CheckAsync(c); break;
                case "review": await ReviewAsync(c); break;
                case "export": await ExportAsync(c); break;
                case "edit": await EditAsync(c); break;
                case "hold": await HoldAsync(c); break;
                case "show": await ShowAsync(c); break;
                case "lookup": await LookupAsync(c); break;
                case "names": await NamesAsync(c); break;
                case "voice": await VoiceAsync(c); break;
                case "style": await StyleAsync(c); break;
                case "context": await ContextAsync(c); break;
                case "terms": await TermsAsync(c); break;
                case "korean": await KoreanAsync(c); break;
                case "version": Console.WriteLine(Version()); break;
                case "mcp": await AgentMcpServer.RunAsync(c); break;
                case "mcp-config":
                    Console.WriteLine(c.Option("--install") is { } app ? AgentMcpServer.Install(app) : AgentMcpServer.SetupText());
                    break;
                default: Help(); break;
            }

            return 0;
        }
        catch (AgentModeException ex)
        {
            Console.WriteLine("실패: " + ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            // A plugin that cannot be read or written (a CP949 Korean file given as the source, missing Strings) ended
            // the process with .NET's "Unhandled exception" and a stack trace, and an MCP call with a protocol error,
            // where the agent looks for "실패:".
            Console.WriteLine("실패: " + ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else (a project file cut short by another process, a DB locked for long) as well: the agent and
            // the MCP client read "실패:", and the details go to app.log.
            AppLog.Write($"ERROR agent {command}: {ex}");
            Console.WriteLine($"실패: {ex.Message} (자세한 내용: {AppLog.PathForUser})");
            return 2;
        }
        finally
        {
            // The MCP server runs a command per tool call in one process: a global DB left open by each kept hundreds
            // of SQLite connections on the global glossary and its WAL over a session of submits.
            await c.GlobalDb.DisposeAsync();
        }
    }

    private static string Flat(string text) => text.Replace("\r\n", "⏎").Replace("\n", "⏎");

    private static string Short(string text, int max = 60)
    {
        var flat = Flat(text);
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    // The GUI's names: show said 수정됨 for the GUI's 직접 수정.
    private static string StatusLabel(StringEntryStatus status) => XTranslatorAi.App.ViewModels.StringEntryStatusLabels.ToLabel(status);

    // The build the kit runs, as the window's title shows it ("1.15-preview19+<commit>").
    internal static string VersionText() => Version();

    private static string Version()
        => "툴리우스 번역기 " + (typeof(AgentCli).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?");

    private static void Help() => Console.WriteLine("""
        tullius.cmd: 툴리우스 번역기 에이전트 모드 (API 키 없이 구독 앱의 모델로 번역)

          tullius.cmd open <플러그인.esp> [--strings <Strings 폴더>] [--batch-size N]   프로젝트 만들기(GUI와 같은 프로젝트)
          tullius.cmd projects                                                       프로젝트 목록
          tullius.cmd status <프로젝트>                                              진행 상황
          tullius.cmd next <프로젝트>                                                첫 요청 만들기(끊긴 뒤 다시 시작할 때도)
          tullius.cmd submit <프로젝트> <답 파일>                                    답 저장, 다음 요청 만들기
          tullius.cmd check <프로젝트> [--all]                                       품질검사(--all: 고칠 것 없는 참고·확인함까지)
          tullius.cmd review <프로젝트> --id 12,40 [--undo]                          그대로 둘 줄의 품질검사 결과를 확인함으로 표시(말투·남은 영어가 바뀌면 다시 나옴)
          tullius.cmd show <프로젝트> [--find 글자] [--rec WEAP:FULL] [--id 12,40] [--after <id>] [--count 40]  줄 보기(#id, 영어, 번역; --after·--count로 쪽 나눠 읽기)
          tullius.cmd korean <프로젝트> <한국어판 경로> [--strings <폴더>] [--english <영어 원본>]  기존 한국어판을 연결하고 아직 번역 안 된 줄을 채움(검수 모드, 영어가 바뀐 줄은 번역)
          tullius.cmd lookup <프로젝트> <낱말>                                       이름·용어의 기존 표기(용어집·게임 사전·TM, 앱이 켜져 있어도 됨)
          tullius.cmd names <프로젝트>                                               새로 정한 이름(용어집·TM·게임 사전·한국어판에 없던 이름 줄, 끝 보고용)
          tullius.cmd voice <프로젝트> <이름|FormID>                                 바닐라 인물이 한글패치에서 하는 말(말투 참고, 앱이 켜져 있어도 됨)
          tullius.cmd style <프로젝트> [<화자> <말투> [--to <상대>] [--note <메모>] [--clear]]  화자별로 정한 말투 보기·정하기(요청과 품질검사가 따름)
          tullius.cmd context <프로젝트> [<문맥 파일> | --clear]                     프로젝트 문맥(모드 배경·인물 관계·말투) 보기·저장·지우기(모든 요청의 지시에 들어감)
          tullius.cmd terms <프로젝트> [--find 낱말 | <용어.json> | <영어> --clear]   프로젝트 용어집 보기·추가([{"source": "영어", "target": "한국어"}], 한글패치 표기보다 먼저)·지우기
          tullius.cmd edit <프로젝트> <수정.json>                                    줄 고치기([{"id": 번호, "text": "새 번역"}])
          tullius.cmd hold <프로젝트> [--id 12,40 [--note 이유] [--undo]]           번역할 수 없는 줄 보류(요청에서 빼고 영어 그대로 저장)·목록·풀기
          tullius.cmd export <프로젝트> [출력 폴더]                                  번역된 플러그인 저장(이미 있으면 output\<키>-2처럼 새 폴더)
          tullius.cmd version                                                        번역기 버전
          tullius.cmd mcp-config                                                     이 명령들을 MCP 도구로 쓰는 앱 설정(안티그라비티·Gemini CLI 등)
          tullius.cmd mcp-config --install antigravity                                안티그라비티에 바로 연결(안티그라비티 연결.cmd와 같음)

        <프로젝트>는 키, 플러그인 파일 이름(확장자 없이도), 플러그인 경로 중 하나. 번호는 요청·check·show의 id로 같다.
        작업 파일은 ./work(--work로 바꿈).
        프로젝트 DB는 GUI 번역기와 같은 곳(--projects로 바꿈). 전역 DB(전체 용어집·한글패치 TM)도 GUI와 같은 곳(--global로 바꿈, 시험·비교용).
        인자에 & | ^ 가 들어가면 한 줄에 하나씩 파일에 쓰고 @파일로 넘긴다(tullius.cmd @인자.txt).
        next·submit·status·hold에 --lane 번호/갈래(예: 2/3)를 붙이면 줄을 갈래로 나눠 그 몫만 번역한다(하위 에이전트가 동시에, 대화는 한 갈래에).
        """);

    /// <summary>
    /// "@인자.txt" stands for the arguments written in that file, one per line. tullius.cmd hands the arguments to cmd,
    /// which splits an unquoted "A&amp;B" (PowerShell quotes only those with spaces) into two commands; a file keeps
    /// such a path or word whole.
    /// </summary>
    public static string[] ExpandArgumentFiles(string[] args)
    {
        var expanded = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (arg.Length < 2 || arg[0] != '@')
            {
                expanded.Add(arg);
                continue;
            }

            var path = arg[1..];
            if (!File.Exists(path))
            {
                throw new AgentModeException($"인자 파일이 없습니다: {path}");
            }

            expanded.AddRange(Decode(File.ReadAllBytes(path), path).ReplaceLineEndings("\n").Split('\n')
                .Select(line => line.Trim()).Where(line => line.Length > 0));
        }

        return expanded.ToArray();
    }

    /// <summary>
    /// An answer, edit or argument file as the agent app wrote it. Windows PowerShell 5.1 writes UTF-16 (Out-File, &gt;)
    /// or the ANSI code page, CP949 on Korean Windows (Set-Content), where PowerShell 7 writes UTF-8: read as UTF-8, a
    /// CP949 answer's Korean became replacement characters and was saved so. The bytes are decoded as GUI imports are
    /// (ImportTextFileReader: a byte-order mark, else UTF-8, else CP949), and broken text is refused.
    /// </summary>
    internal static async Task<string> ReadAgentFileAsync(string path, System.Threading.CancellationToken cancellationToken)
        => Decode(await File.ReadAllBytesAsync(path, cancellationToken), path);

    private static string Decode(byte[] bytes, string path)
    {
        try
        {
            return ImportTextFileReader.Decode(bytes, Path.GetFileName(path));
        }
        catch (ImportFileEncodingException)
        {
            // The import reader's own message speaks of importing; the agent wrote the file and only has to write it again.
            throw new AgentModeException($"{Path.GetFileName(path)}의 글자를 읽지 못해 저장하지 않았습니다(이미 깨진 글자 �가 있거나 UTF-8·UTF-16·CP949가 아님). "
                                         + "파일을 UTF-8로 다시 쓰세요 (PowerShell: [IO.File]::WriteAllText(경로, 내용, [Text.UTF8Encoding]::new($false))).");
        }
    }

    internal sealed record EditLine(long? Id, int? Order, string? Text);
}
