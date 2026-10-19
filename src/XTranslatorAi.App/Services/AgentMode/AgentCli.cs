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
        var undo = rest.Remove("--undo");
        var toOption = Option("--to");
        var noteOption = Option("--note");
        var clearStyle = rest.Remove("--clear");
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
                case "review": await ReviewAsync(); break;
                case "export": await ExportAsync(); break;
                case "edit": await EditAsync(); break;
                case "show": await ShowAsync(); break;
                case "lookup": await LookupAsync(); break;
                case "voice": await VoiceAsync(); break;
                case "style": await StyleAsync(); break;
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

        // The quality check as the GUI runs it on the whole project, with each row's translation by id.
        async Task<(List<LqaIssue> Issues, Dictionary<long, string> Dest)> ScanProjectAsync(ProjectDb db)
        {
            var global = await globalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, ct);
            var rows = new List<StringEntry>();
            for (var offset = 0; ; offset += 1000)
            {
                var page = await db.GetStringsAsync(1000, offset, ct);
                rows.AddRange(page);
                if (page.Count < 1000) break;
            }

            var speakers = await DialogueSpeakerNames.LoadCheckGroupsAsync(db, ct);
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
                referenceNames: memory.Count > 0 ? ReferenceNameIndex.Build(memory) : null, sourceFixIds: sourceFix,
                speakerStyles: await db.GetSpeakerStylesAsync(ct));
            return (issues, rows.ToDictionary(row => row.Id, row => row.DestText ?? ""));
        }

        async Task CheckAsync()
        {
            var project = session.Find(Arg(0, "프로젝트"));
            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            var (issues, dest) = await ScanProjectAsync(db);
            // Results the user marked "확인함" (looked at and left as they are) are not listed again while the row's
            // translation is the one they were marked with; --all lists them with the mark.
            var marks = await db.GetLqaReviewedMarksAsync(ct);
            bool Reviewed(LqaIssue i) => LqaReviewedMarks.Holds(marks, i.Id, i.Code, dest.GetValueOrDefault(i.Id));
            var reviewedCount = issues.Count(Reviewed);
            // 참고(Info) findings need no fix (a TM line translated anew, say); they are listed only on request.
            // The speaker checks' reference findings are worth a look (a line in another level than the speaker's
            // others), so they are listed with the warnings; the other reference findings need no fix.
            var unreviewed = issues.Where(i => !Reviewed(i)).ToList();
            var shown = all ? issues : unreviewed.Where(i => i.Severity != "Info" || i.Code is "speaker_tone_mixed" or "player_tone_inconsistent").ToList();
            var hidden = unreviewed.Count - (all ? unreviewed.Count : shown.Count);
            var notes = new List<string>();
            if (hidden > 0) notes.Add($"참고 {hidden}건은 고칠 것 없음");
            if (reviewedCount > 0) notes.Add(all ? $"확인함 {reviewedCount}건 포함" : $"확인함 {reviewedCount}건은 숨김");
            Console.WriteLine($"품질검사: {shown.Count}건" + (notes.Count > 0 ? $" ({string.Join(", ", notes)}" + (all ? ")" : $", 보려면 check {project.Key} --all)") : ""));
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
        async Task ReviewAsync()
        {
            RefuseWhileAppRuns();
            var project = session.Find(Arg(0, "프로젝트"));
            var ids = idsOption?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var v) ? v : throw new AgentModeException($"--id에 숫자가 아닌 값: {s}")).ToHashSet()
                ?? throw new AgentModeException("확인함으로 표시할 줄을 --id 12,40처럼 지정하세요.");
            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            if (undo)
            {
                var off = (await db.GetLqaReviewedMarksAsync(ct)).Keys.Where(key => ids.Contains(key.StringId))
                    .Select(key => (key.StringId, key.Code, "")).ToList();
                await db.SetLqaReviewedAsync(off, false, ct);
                Console.WriteLine($"확인함 표시 해제: {off.Count}건");
                return;
            }

            var (issues, dest) = await ScanProjectAsync(db);
            var picked = issues.Where(i => ids.Contains(i.Id)).ToList();
            await db.SetLqaReviewedAsync(picked.Select(i => (i.Id, i.Code, dest.GetValueOrDefault(i.Id, ""))).Distinct().ToList(), true, ct);
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
            // Who says each line, as the requests name it: the review needs it to judge a speech level, and had to read
            // the project DB for it in Meridia's Order.
            var speakers = picked.Count == 0 ? new Dictionary<long, string>()
                : await DialogueSpeakerNames.LoadAsync(db, await new GameStringService().TryLoadAsync(ct), ct);
            foreach (var r in picked)
            {
                Console.WriteLine($"#{r.Id} {r.Rec} [{StatusLabel(r.Status)}]");
                if (speakers.TryGetValue(r.Id, out var speaker))
                {
                    var playerLine = string.Equals(r.Rec, "INFO:RNAM", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Rec, "DIAL:FULL", StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine($"  화자: {(playerLine ? TranslationPrompt.PlayerSpeakerPrefix : "")}{speaker}");
                }

                Console.WriteLine($"  영어: {Flat(r.SourceText ?? "")}");
                Console.WriteLine($"  번역: {Flat(r.DestText ?? "")}");
            }
        }

        // The speech levels decided for the project: style <프로젝트> lists them and the speakers still undecided;
        // style <프로젝트> <화자> <말투> [--to <상대>] [--note <메모>] decides one, --clear takes it back. Requests name a
        // decided level next to the speaker, and check compares lines with it instead of with the speaker's other lines.
        async Task StyleAsync()
        {
            var project = session.Find(Arg(0, "프로젝트"));
            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
            var values = await DialogueSpeakerNames.LoadFormIdsAsync(db, ct);
            var game = await new GameStringService().TryLoadAsync(ct);
            var labels = await DialogueSpeakerNames.LoadAsync(db, game, ct);
            var masters = await db.GetPluginMastersAsync(ct);
            static string Bare(string label)
            {
                foreach (var cut in new[] { DialogueSpeakerNames.GuessedListenerLabelSeparator, DialogueSpeakerNames.ListenerLabelSeparator, " (정한 말투" })
                {
                    var at = label.IndexOf(cut, StringComparison.Ordinal);
                    if (at >= 0) label = label[..at];
                }

                return label;
            }

            // Each NPC speaker of the project by FormID, with its name and line count.
            var npcs = values.Where(v => labels.ContainsKey(v.Key))
                .GroupBy(v => v.Value.Split(DialogueSpeakerNames.ListenerSeparator)[0])
                .ToDictionary(g => g.Key, g => (Name: Bare(labels[g.First().Key]), Lines: g.Count()), StringComparer.Ordinal);
            string NameOf(string who) => who == SpeakerStyles.PlayerSpeaker ? "플레이어"
                : who == SpeakerStyles.Player ? "플레이어"
                : who == SpeakerStyles.Other ? "다른 인물"
                : who == SpeakerStyles.Anyone ? "누구에게나"
                : npcs.TryGetValue(who, out var npc) ? $"{npc.Name} ({who})" : who;
            string Resolve(string query, bool listener)
            {
                var q = query.Trim();
                if (q is "player" or "플레이어") return listener ? SpeakerStyles.Player : SpeakerStyles.PlayerSpeaker;
                if (listener && q is "other" or "다른 인물" or "다른인물") return SpeakerStyles.Other;
                if (listener && q is "*" or "anyone" or "누구에게나" or "모두") return SpeakerStyles.Anyone;
                if (q.Length == 8 && q.All(Uri.IsHexDigit)) return q.ToUpperInvariant();
                var found = npcs.Where(n => n.Value.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                // A base game NPC's label is its Korean name: its English name is in the dictionary's speaker index.
                if (found.Count == 0 && game?.Speakers is { } index && masters is { } m)
                {
                    var known = index.FindNpcs(q).Select(n =>
                    {
                        var owner = n.Npc[..n.Npc.LastIndexOf(':')];
                        var at = m.Masters.ToList().FindIndex(x => string.Equals(x, owner, StringComparison.OrdinalIgnoreCase));
                        return at < 0 ? null : $"{at:X2}{n.Npc[(n.Npc.LastIndexOf(':') + 1)..].ToUpperInvariant()}";
                    }).OfType<string>().ToHashSet(StringComparer.Ordinal);
                    found = npcs.Where(n => known.Contains(n.Key)).ToList();
                }
                return found.Count switch
                {
                    1 => found[0].Key,
                    0 => throw new AgentModeException($"'{q}'인 화자가 이 프로젝트에 없습니다. FormID로 지정하세요."),
                    _ => throw new AgentModeException($"'{q}'에 맞는 화자가 여럿입니다: {string.Join(", ", found.Take(6).Select(f => $"{f.Value.Name}({f.Key})"))}. FormID로 지정하세요."),
                };
            }

            if (rest.Count <= 1)
            {
                var styles = await db.GetSpeakerStylesAsync(ct);
                Console.WriteLine(styles.Count == 0 ? "정한 말투: 없음" : $"정한 말투: {styles.Count}");
                foreach (var style in styles)
                {
                    Console.WriteLine($"- {NameOf(style.Speaker)} → {NameOf(style.Listener)}: {style.Level}" + (style.Note is { Length: > 0 } note ? $" ({note})" : ""));
                }

                var undecided = npcs.Where(n => styles.All(s => s.Speaker != n.Key)).OrderByDescending(n => n.Value.Lines).Take(12).ToList();
                if (undecided.Count > 0)
                {
                    Console.WriteLine("말투를 정하지 않은 화자: " + string.Join(", ", undecided.Select(n => $"{n.Value.Name}({n.Key}, {n.Value.Lines}줄)")));
                }

                return;
            }

            RefuseWhileAppRuns();
            var speaker = Resolve(Arg(1, "화자"), listener: false);
            var listenerKey = toOption == null ? SpeakerStyles.Player : Resolve(toOption, listener: true);
            var level = "";
            if (!clearStyle)
            {
                level = SpeakerStyles.Normalize(Arg(2, "말투")) ?? throw new AgentModeException(
                    $"말투는 {string.Join(", ", SpeakerStyles.Levels)} 중 하나로 쓰세요.");
            }

            await db.SetSpeakerStyleAsync(new SpeakerStyle(speaker, listenerKey, level, noteOption), ct);
            Console.WriteLine(clearStyle
                ? $"지움: {NameOf(speaker)} → {NameOf(listenerKey)}"
                : $"정함: {NameOf(speaker)} → {NameOf(listenerKey)}: {level}");
        }

        // How a base game NPC speaks in the Korean patch: voice <프로젝트> <영어 이름|한국어 이름|FormID> (read only). The
        // Teldryn trial's agent found Teldryn's 하오체 only by reading the lines the dictionary had filled.
        async Task VoiceAsync()
        {
            var project = session.Find(Arg(0, "프로젝트"));
            var query = string.Join(" ", rest.Skip(1)).Trim();
            if (query.Length == 0) throw new AgentModeException("찾을 인물을 지정하세요. (tullius.cmd voice <프로젝트> <이름 또는 FormID>)");
            var game = await new GameStringService().TryLoadAsync(ct);
            if (game?.Speakers is not { } index) throw new AgentModeException("게임 번역 사전에 화자 색인이 없습니다.");
            string FieldOf(string npc) => "NPC_/" + npc[..(npc.LastIndexOf(':') + 1)] + npc[(npc.LastIndexOf(':') + 1)..].ToUpperInvariant() + "/0/FULL/0";
            string? Korean(string npc) => game.FindRecord(FieldOf(npc), "")?.Korean;

            // A FormID as the request names it ("NPC 04038560") is in this plugin's master order.
            var found = index.FindNpcs(query).ToList();
            if (query.Length == 8 && query.All(Uri.IsHexDigit))
            {
                await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
                var plugin = await db.GetPluginMastersAsync(ct);
                var formId = uint.Parse(query, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                if (plugin is { } p && formId >> 24 < p.Masters.Count)
                {
                    var npc = $"{p.Masters[(int)(formId >> 24)].ToLowerInvariant()}:{formId & 0xFFFFFF:x6}";
                    found = index.FindNpcs(npc).ToList();
                }
            }

            if (found.Count == 0)
            {
                found = index.Names.Keys.Where(npc => Korean(npc) is { } k && k.Contains(query, StringComparison.Ordinal))
                    .Select(npc => (npc, index.Names[npc], index.LinesOf(npc).Count)).OrderByDescending(x => x.Item3).ToList();
            }

            if (found.Count == 0)
            {
                Console.WriteLine($"'{query}'인 바닐라 인물을 찾지 못했습니다(대사가 있는 NPC만 있습니다).");
                return;
            }

            if (found.Count > 1)
            {
                Console.WriteLine("찾은 인물: " + string.Join(", ", found.Take(6).Select(f => $"{f.Name}({Korean(f.Npc)}, {f.Npc}, {f.Lines}줄)")));
            }

            var (who, name, count) = found[0];
            var lines = index.LinesOf(who).Select(id => game.FindRecord(id, "")?.Korean).OfType<string>()
                .Select(Flat).Where(t => t.Length is >= 8 and <= 120).Distinct(StringComparer.Ordinal).ToList();
            Console.WriteLine($"{name} ({Korean(who) ?? "한국어 이름 없음"}, {who}): 한글패치 대사 {count}줄");
            var step = Math.Max(1, lines.Count / 15);
            foreach (var line in lines.Where((_, i) => i % step == 0).Take(15)) Console.WriteLine("- " + line);
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
              tullius.cmd check <프로젝트> [--all]                                       품질검사(--all: 고칠 것 없는 참고·확인함까지)
              tullius.cmd review <프로젝트> --id 12,40 [--undo]                          그대로 둘 줄의 품질검사 결과를 확인함으로 표시(번역문이 바뀌면 다시 나옴)
              tullius.cmd show <프로젝트> [--find 글자] [--rec WEAP:FULL] [--id 12,40]   줄 보기(#id, 영어, 번역)
              tullius.cmd lookup <프로젝트> <낱말>                                       이름·용어의 기존 표기(용어집·게임 사전·TM, 앱이 켜져 있어도 됨)
              tullius.cmd voice <프로젝트> <이름|FormID>                                 바닐라 인물이 한글패치에서 하는 말(말투 참고, 앱이 켜져 있어도 됨)
              tullius.cmd style <프로젝트> [<화자> <말투> [--to <상대>] [--note <메모>] [--clear]]  화자별로 정한 말투 보기·정하기(요청과 품질검사가 따름)
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
