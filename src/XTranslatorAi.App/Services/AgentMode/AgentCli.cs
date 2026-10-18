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
/// </summary>
public static class AgentCli
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

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            args = ExpandArgumentFiles(args);
        }
        catch (AgentModeException ex)
        {
            Console.WriteLine("실패: " + ex.Message);
            return 2;
        }

        var ct = CancellationToken.None;
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        var rest = args.Skip(1).ToList();
        string? Option(string name)
        {
            var i = rest.IndexOf(name);
            if (i < 0 || i + 1 >= rest.Count) return null;
            var value = rest[i + 1];
            rest.RemoveRange(i, 2);
            return value;
        }

        var work = Option("--work") ?? Path.Combine(Environment.CurrentDirectory, "work");
        var settings = new AppSettingsStore().Load();
        var globalDb = new GlobalProjectDbService(new BuiltInGlossaryService());
        var projectsRoot = Option("--projects");
        var find = Option("--find");
        var rec = Option("--rec");
        var idsOption = Option("--id");
        var all = rest.Remove("--all");
        var session = new AgentSession(work, globalDb, new GameStringService(), settings, projectsRoot);

        try
        {
            switch (command)
            {
                case "open": await OpenAsync(); break;
                case "projects": Projects(); break;
                case "status": await StatusAsync(); break;
                case "next": RefuseWhileAppRuns(); Print(await session.NextAsync(session.Find(Arg(0, "프로젝트")), ct)); break;
                case "submit": await SubmitAsync(); break;
                case "check": await CheckAsync(); break;
                case "export": await ExportAsync(); break;
                case "edit": await EditAsync(); break;
                case "show": await ShowAsync(); break;
                case "lookup": await LookupAsync(); break;
                case "version": Console.WriteLine(Version()); break;
                default: Help(); break;
            }

            return 0;
        }
        catch (AgentModeException ex)
        {
            Console.WriteLine("실패: " + ex.Message);
            return 2;
        }

        string Arg(int index, string what)
            => index < rest.Count ? rest[index] : throw new AgentModeException($"{what}을(를) 지정하세요. (tullius.cmd help)");

        void RefuseWhileAppRuns()
        {
            if (AppIsRunning())
            {
                throw new AgentModeException("번역기 앱(TulliusTranslator)이 켜져 있습니다. 같은 프로젝트를 함께 쓰지 않도록 앱을 닫고 다시 실행하세요.");
            }
        }

        async Task OpenAsync()
        {
            RefuseWhileAppRuns();
            var strings = Option("--strings") ?? (string.IsNullOrWhiteSpace(settings.PluginStringsDirectory) ? null : settings.PluginStringsDirectory.Trim());
            var batch = int.TryParse(Option("--batch-size"), out var b) ? b : 30;
            var plugin = Arg(0, "플러그인 경로");
            if (!File.Exists(plugin)) throw new AgentModeException($"플러그인이 없습니다: {plugin}");
            await new BundledFranchiseTmSeedService().EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, ct);
            var options = new PluginReadOptions(PluginGame.SkyrimSpecialEdition, settings.PluginSourceLanguage.Trim(), settings.PluginSourceEncoding,
                strings, MetadataEncoding: settings.PluginMetadataEncoding);
            var (project, rows, pending) = await session.OpenAsync(plugin, options, settings.PluginTargetLanguage.Trim(), settings.PluginTargetEncoding, batch, ct);
            Console.WriteLine($"프로젝트: {project.Key}");
            Console.WriteLine($"플러그인: {project.PluginPath}");
            Console.WriteLine($"DB: {project.DbPath}");
            Console.WriteLine($"줄: {rows}, 번역할 줄: {pending}, 묶음 크기: {project.BatchSize}");
            Console.WriteLine($"다음: tullius.cmd next {project.Key}");
        }

        void Projects()
        {
            foreach (var p in session.Projects())
            {
                Console.WriteLine($"{p.Key}\t{p.PluginPath}{(session.Pending(p) != null ? "\t(요청 대기)" : "")}");
            }
        }

        async Task StatusAsync()
        {
            var project = session.Find(Arg(0, "프로젝트"));
            var (rows, pending, errors, edited) = await session.StatusAsync(project, ct);
            Console.WriteLine($"프로젝트: {project.Key} ({Path.GetFileName(project.PluginPath)})");
            Console.WriteLine($"줄: {rows}, 남은 줄: {pending} (오류 {errors}), 직접 수정: {edited}");
            var request = session.Pending(project);
            Console.WriteLine(request == null ? "대기 중인 요청: 없음" : $"대기 중인 요청: {session.RequestPath(project)} ({request.Chars:N0}자)");
        }

        async Task SubmitAsync()
        {
            RefuseWhileAppRuns();
            var project = session.Find(Arg(0, "프로젝트"));
            var file = Arg(1, "답 파일");
            if (!File.Exists(file)) throw new AgentModeException($"답 파일이 없습니다: {file}");
            Print(await session.SubmitAsync(project, await File.ReadAllTextAsync(file, ct), ct));
        }

        void Print(AgentStepReport report)
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
                    Console.WriteLine($"새 지시 절: {string.Join(", ", report.NewInstructionSections)} → {session.InstructionsPath(report.Project)}에서 읽기");
                }
                Console.WriteLine($"답을 파일에 쓴 뒤: tullius.cmd submit {report.Project.Key} <답 파일>");
            }
            else if (report.PendingAfter == 0)
            {
                Console.WriteLine($"번역 완료. 다음: tullius.cmd check {report.Project.Key}, tullius.cmd export {report.Project.Key}");
            }
        }

        async Task CheckAsync()
        {
            var project = session.Find(Arg(0, "프로젝트"));
            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            var global = await globalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, ct);
            var rows = new List<StringEntry>();
            for (var offset = 0; ; offset += 1000)
            {
                var page = await db.GetStringsAsync(1000, offset, ct);
                rows.AddRange(page);
                if (page.Count < 1000) break;
            }

            var speakers = await DialogueSpeakerNames.LoadFormIdsAsync(db, ct);
            static StringEntryStatus Status(StringEntry e)
                => e.Status == StringEntryStatus.Skipped && !string.IsNullOrWhiteSpace(e.DestText)
                   && !string.Equals(e.DestText.Trim(), e.SourceText.Trim(), StringComparison.Ordinal) ? StringEntryStatus.Done : e.Status;
            var entries = rows.Select(e => new LqaScanEntry(e.Id, e.OrderIndex, e.Edid, e.Rec, Status(e), e.SourceText ?? "", e.DestText ?? "",
                Speaker: speakers.GetValueOrDefault(e.Id))).ToList();
            var glossary = GlossaryMerger.Merge(await db.GetGlossaryAsync(ct), global == null ? null : await global.GetGlossaryAsync(ct))
                .Where(e => e.Enabled && e.ForceMode == GlossaryForceMode.ForceToken && e.MatchMode != GlossaryMatchMode.Regex)
                .Where(e => !string.IsNullOrWhiteSpace(e.SourceTerm) && !string.IsNullOrWhiteSpace(e.TargetTerm))
                .OrderByDescending(e => e.Priority).ThenByDescending(e => e.SourceTerm.Length).ToList();
            var memory = global == null ? new List<(string, string)>()
                : (await global.GetTranslationMemoryEntriesAsync("english", "korean", ct)).Select(e => (e.SourceText, e.DestText)).ToList();
            var tmNotes = await db.GetStringNotesByKindAsync("tm_fallback", ct);
            var sourceFix = (await db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, ct)).Keys.ToHashSet();
            var notes = memory.Count > 0 ? LqaScanner.DropTmFallbacksMatchingMemory(tmNotes, entries, memory) : tmNotes;
            var issues = await LqaScanner.ScanAsync(entries, "korean", glossary, tmFallbackNotes: notes,
                referenceNames: memory.Count > 0 ? ReferenceNameIndex.Build(memory) : null, sourceFixIds: sourceFix);
            // 참고(Info) findings need no fix (a TM line translated anew, say); they are listed only on request.
            // The speaker checks' reference findings are worth a look (a line in another level than the speaker's
            // others), so they are listed with the warnings; the other reference findings need no fix.
            var shown = all ? issues : issues.Where(i => i.Severity != "Info" || i.Code is "speaker_tone_mixed" or "player_tone_inconsistent").ToList();
            var hidden = issues.Count - shown.Count;
            Console.WriteLine($"품질검사: {shown.Count}건" + (hidden > 0 ? $" (참고 {hidden}건은 고칠 것 없음, 보려면 check {project.Key} --all)" : ""));
            foreach (var i in shown)
            {
                Console.WriteLine($"- #{i.Id} [{LqaIssueLabels.Severity(i.Severity)}] {LqaIssueLabels.Code(i.Code)}: {IssueMessage(i)}");
                Console.WriteLine($"  영어: {Flat(i.SourceText)}");
                Console.WriteLine($"  번역: {Flat(i.DestText)}");
            }
        }

        // The TM fallback note is the run's own diagnostic (English, with the row's id): said in words, the detail kept.
        static string IssueMessage(LqaIssue issue)
            => issue.Code == "tm_fallback"
                ? "게임 번역(TM)과 태그·토큰이 맞지 않아 TM 대신 새로 번역한 줄입니다. 번역이 맞으면 고칠 것 없음. (원인: "
                  + System.Text.RegularExpressions.Regex.Replace(Flat(issue.Message).Replace("⏎", " "), @"\s+", " ").Trim() + ")"
                : Flat(issue.Message);

        static string Flat(string text) => text.Replace("\r\n", "⏎").Replace("\n", "⏎");

        static string Short(string text, int max = 60)
        {
            var flat = Flat(text);
            return flat.Length <= max ? flat : flat[..max] + "…";
        }

        async Task<List<StringEntry>> RowsAsync(ProjectDb db)
        {
            var rows = new List<StringEntry>();
            for (var offset = 0; ; offset += 1000)
            {
                var page = await db.GetStringsAsync(1000, offset, ct);
                rows.AddRange(page);
                if (page.Count < 1000) break;
            }

            return rows;
        }

        // Lines by id (the id requests and check use), filtered: show <프로젝트> [--find 글자] [--rec WEAP:FULL] [--id 12,40]
        async Task ShowAsync()
        {
            var project = session.Find(Arg(0, "프로젝트"));
            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            var rows = await RowsAsync(db);
            var ids = idsOption?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var v) ? v : throw new AgentModeException($"--id에 숫자가 아닌 값: {s}")).ToHashSet();
            var picked = rows
                .Where(r => rec == null || string.Equals(r.Rec, rec, StringComparison.OrdinalIgnoreCase))
                .Where(r => ids == null || ids.Contains(r.Id))
                .Where(r => find == null || (r.SourceText ?? "").Contains(find, StringComparison.OrdinalIgnoreCase)
                                         || (r.DestText ?? "").Contains(find, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.OrderIndex)
                .ToList();
            Console.WriteLine($"줄: {picked.Count}" + (picked.Count != rows.Count ? $" / {rows.Count}" : ""));
            foreach (var r in picked)
            {
                Console.WriteLine($"#{r.Id} {r.Rec} [{StatusLabel(r.Status)}]");
                Console.WriteLine($"  영어: {Flat(r.SourceText ?? "")}");
                Console.WriteLine($"  번역: {Flat(r.DestText ?? "")}");
            }
        }

        // How the translator already writes an English name or term: lookup <프로젝트> <낱말> (read only, the app may run).
        async Task LookupAsync()
        {
            var project = session.Find(Arg(0, "프로젝트"));
            var word = string.Join(" ", rest.Skip(1)).Trim();
            if (word.Length == 0) throw new AgentModeException("찾을 영어 낱말을 지정하세요. (tullius.cmd lookup <프로젝트> <낱말>)");
            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            var global = await globalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, ct);
            var game = await new GameStringService().TryLoadAsync(ct);
            var found = await AgentLookup.FindAsync(db, global, game, word, 12, ct);
            Console.WriteLine($"찾은 낱말: {word}");
            if (found.Glossary.Count == 0 && found.Game == null && found.Memory.Count == 0)
            {
                Console.WriteLine("용어집·게임 사전·TM에 없습니다. 새 이름이면 앞뒤 요청과 같은 표기로 쓴다.");
                return;
            }

            foreach (var hit in found.Glossary) Console.WriteLine($"[{hit.Where}] {hit.Source} → {hit.Target}");
            if (found.Game != null) Console.WriteLine($"[게임 사전] {word} → {Flat(found.Game.Text)}");
            foreach (var hit in found.Memory) Console.WriteLine($"[TM] {Short(hit.Source, 120)} → {Short(hit.Target, 120)}");
            if (found.MemoryTotal > found.Memory.Count) Console.WriteLine($"(TM {found.MemoryTotal}건 중 짧은 {found.Memory.Count}건)");
        }

        static string StatusLabel(StringEntryStatus status) => status switch
        {
            StringEntryStatus.Pending => "대기",
            StringEntryStatus.InProgress => "번역 중",
            StringEntryStatus.Done => "완료",
            StringEntryStatus.Edited => "수정됨",
            StringEntryStatus.Error => "오류",
            StringEntryStatus.Skipped => "건너뜀",
            _ => status.ToString(),
        };

        // Fixes after the quality check: [{"id": 12, "text": "..."}] (the id of requests, check and show; "order" is the
        // row's 순번, as before), saved as the GUI saves a hand edit (수정됨).
        async Task EditAsync()
        {
            RefuseWhileAppRuns();
            var project = session.Find(Arg(0, "프로젝트"));
            var file = Arg(1, "수정 파일");
            if (!File.Exists(file)) throw new AgentModeException($"수정 파일이 없습니다: {file}");
            List<EditLine>? edits;
            try
            {
                edits = System.Text.Json.JsonSerializer.Deserialize<List<EditLine>>(await File.ReadAllTextAsync(file, ct),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new AgentModeException("수정 파일 형식이 맞지 않습니다([{\"id\": 번호, \"text\": \"새 번역\"}]): " + ex.Message);
            }

            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            var rows = await RowsAsync(db);
            var byId = rows.ToDictionary(r => r.Id);
            var byOrder = rows.ToDictionary(r => r.OrderIndex);
            StringEntry? Row(EditLine e) => e.Id is { } id ? byId.GetValueOrDefault(id) : e.Order is { } order ? byOrder.GetValueOrDefault(order) : null;
            var list = edits ?? new();
            var missing = list.Where(e => Row(e) == null).Select(e => e.Id is { } id ? $"id {id}" : e.Order is { } o ? $"순번 {o}" : "번호 없음").ToList();
            if (missing.Count > 0) throw new AgentModeException($"없는 줄: {string.Join(", ", missing)}. 아무것도 저장하지 않았습니다.");
            foreach (var edit in list)
            {
                var row = Row(edit)!;
                await db.UpdateStringTranslationAsync(row.Id, edit.Text, StringEntryStatus.Edited, null, ct);
                Console.WriteLine($"#{row.Id} {Short(row.SourceText ?? "")}: {Flat(row.DestText ?? "")} → {Flat(edit.Text)}");
            }

            Console.WriteLine($"고친 줄: {list.Count}. 다시 확인: tullius.cmd check {project.Key}");
        }

        async Task ExportAsync()
        {
            RefuseWhileAppRuns();
            var project = session.Find(Arg(0, "프로젝트"));
            string output;
            if (rest.Count > 1)
            {
                output = Path.GetFullPath(rest[1]);
                if (Directory.Exists(output) || File.Exists(output))
                {
                    throw new AgentModeException($"출력 폴더가 이미 있습니다: {output}. 기존 파일을 덮어쓰지 않으니 없는 폴더를 지정하세요(지정하지 않으면 output\\{project.Key}-2처럼 새 폴더를 고릅니다).");
                }
            }
            else
            {
                // An export again after edits goes next to the first one: the plugin writer never overwrites a folder.
                var root = Path.Combine(Environment.CurrentDirectory, "output");
                output = Path.Combine(root, project.Key);
                for (var n = 2; Directory.Exists(output) || File.Exists(output); n++) output = Path.Combine(root, $"{project.Key}-{n}");
            }

            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            var document = await PluginReader.ReadAsync(project.PluginPath, project.ReadOptions, ct);
            PluginExportResult result;
            try
            {
                result = await new ProjectWorkspaceService(globalDb, projectsRoot).ExportPluginAsync(db, document, new PluginExportOptions(output, project.TargetEncoding), ct);
            }
            catch (IOException ex)
            {
                throw new AgentModeException($"저장하지 못했습니다({output}): {ex.Message}");
            }

            Console.WriteLine($"저장: {result.PluginPath} (바뀐 필드 {result.ChangedFields})");
            foreach (var file in result.Files.Where(f => f != result.PluginPath)) Console.WriteLine($"  {file}");
            var earlier = Path.Combine(Environment.CurrentDirectory, "output", project.Key);
            if (rest.Count <= 1 && !string.Equals(output, earlier, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"알림: 앞서 저장한 {earlier}는 그대로 두었습니다. 게임에 넣을 것은 이번 폴더입니다.");
            }
        }

        // The build the kit runs, as the window's title shows it ("1.15-preview19+<commit>").
        static string Version()
            => "툴리우스 번역기 " + (typeof(AgentCli).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?");

        static void Help() => Console.WriteLine("""
            tullius.cmd: 툴리우스 번역기 에이전트 모드 (API 키 없이 구독 앱의 모델로 번역)

              tullius.cmd open <플러그인.esp> [--strings <Strings 폴더>] [--batch-size N]   프로젝트 만들기(GUI와 같은 프로젝트)
              tullius.cmd projects                                                       프로젝트 목록
              tullius.cmd status <프로젝트>                                              진행 상황
              tullius.cmd next <프로젝트>                                                첫 요청 만들기(끊긴 뒤 다시 시작할 때도)
              tullius.cmd submit <프로젝트> <답 파일>                                    답 저장, 다음 요청 만들기
              tullius.cmd check <프로젝트> [--all]                                       품질검사(--all: 고칠 것 없는 참고까지)
              tullius.cmd show <프로젝트> [--find 글자] [--rec WEAP:FULL] [--id 12,40]   줄 보기(#id, 영어, 번역)
              tullius.cmd lookup <프로젝트> <낱말>                                       이름·용어의 기존 표기(용어집·게임 사전·TM, 앱이 켜져 있어도 됨)
              tullius.cmd edit <프로젝트> <수정.json>                                    줄 고치기([{"id": 번호, "text": "새 번역"}])
              tullius.cmd export <프로젝트> [출력 폴더]                                  번역된 플러그인 저장(이미 있으면 output\<키>-2처럼 새 폴더)
              tullius.cmd version                                                        번역기 버전

            <프로젝트>는 키, 플러그인 파일 이름(확장자 없이도), 플러그인 경로 중 하나. 번호는 요청·check·show의 id로 같다.
            작업 파일은 ./work(--work로 바꿈).
            프로젝트 DB는 GUI 번역기와 같은 곳(--projects로 바꿈).
            인자에 & | ^ 가 들어가면 한 줄에 하나씩 파일에 쓰고 @파일로 넘긴다(tullius.cmd @인자.txt).
            """);
    }

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

            expanded.AddRange(File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0));
        }

        return expanded.ToArray();
    }

    private sealed record EditLine(long? Id, int? Order, string Text);
}
