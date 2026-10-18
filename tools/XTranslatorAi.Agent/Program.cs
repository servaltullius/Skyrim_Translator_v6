// tullius: agent mode of the Tullius translator. A subscription app (Claude, Codex, Antigravity) opened on this folder
// runs it; the app's model writes the translations, this tool only reads, protects, validates and saves. It never calls
// an AI and never starts one. Usage: tullius <command> ... (tullius help)
using System.Diagnostics;
using System.IO;
using System.Text;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

Console.OutputEncoding = Encoding.UTF8;
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
    => index < rest.Count ? rest[index] : throw new AgentModeException($"{what}을(를) 지정하세요. (tullius help)");

void RefuseWhileAppRuns()
{
    if (Process.GetProcessesByName("TulliusTranslator").Length > 0)
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
    Console.WriteLine($"다음: tullius next {project.Key}");
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
    Console.WriteLine($"저장한 줄: {report.Saved}, 남은 줄: {report.PendingAfter}" + (report.ErrorRows > 0 ? $" (오류 {report.ErrorRows})" : ""));
    if (report.Problem != null) Console.WriteLine("알림: " + report.Problem);
    if (report.Next != null)
    {
        Console.WriteLine($"다음 요청: {report.NextRequestPath} ({report.Next.Chars:N0}자, 추정 {report.Next.Chars / 3:N0}토큰)");
        Console.WriteLine($"답을 파일에 쓴 뒤: tullius submit {report.Project.Key} <답 파일>");
    }
    else if (report.PendingAfter == 0)
    {
        Console.WriteLine($"번역 완료. 다음: tullius check {report.Project.Key}, tullius export {report.Project.Key}");
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

    var speakers = await db.GetStringSpeakersAsync(ct);
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
    Console.WriteLine($"품질검사: {issues.Count}건");
    foreach (var i in issues)
    {
        Console.WriteLine($"- #{i.OrderIndex} [{i.Severity}] {i.Code}: {i.Message}");
        Console.WriteLine($"  영어: {Flat(i.SourceText)}");
        Console.WriteLine($"  번역: {Flat(i.DestText)}");
    }
}

static string Flat(string text) => text.Replace("\r\n", "⏎").Replace("\n", "⏎");

// Fixes after the quality check: [{"order": 12, "text": "..."}], saved as the GUI saves a hand edit (수정됨).
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
        throw new AgentModeException("수정 파일 형식이 맞지 않습니다([{\"order\": 순번, \"text\": \"새 번역\"}]): " + ex.Message);
    }

    await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
    var rows = new Dictionary<int, StringEntry>();
    for (var offset = 0; ; offset += 1000)
    {
        var page = await db.GetStringsAsync(1000, offset, ct);
        foreach (var row in page) rows[row.OrderIndex] = row;
        if (page.Count < 1000) break;
    }

    var missing = (edits ?? new()).Where(e => !rows.ContainsKey(e.Order)).Select(e => e.Order).ToList();
    if (missing.Count > 0) throw new AgentModeException($"없는 순번: {string.Join(", ", missing)}. 아무것도 저장하지 않았습니다.");
    foreach (var edit in edits!)
    {
        await db.UpdateStringTranslationAsync(rows[edit.Order].Id, edit.Text, StringEntryStatus.Edited, null, ct);
    }

    Console.WriteLine($"고친 줄: {edits.Count}. 다시 확인: tullius check {project.Key}");
}

async Task ExportAsync()
{
    RefuseWhileAppRuns();
    var project = session.Find(Arg(0, "프로젝트"));
    var output = rest.Count > 1 ? rest[1] : Path.Combine(Environment.CurrentDirectory, "output", project.Key);
    await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, ct);
    var document = await PluginReader.ReadAsync(project.PluginPath, project.ReadOptions, ct);
    var result = await new ProjectWorkspaceService(globalDb, projectsRoot).ExportPluginAsync(db, document, new PluginExportOptions(output, project.TargetEncoding), ct);
    Console.WriteLine($"저장: {result.PluginPath} (바뀐 필드 {result.ChangedFields})");
    foreach (var file in result.Files.Where(f => f != result.PluginPath)) Console.WriteLine($"  {file}");
}

static void Help() => Console.WriteLine("""
    tullius: 툴리우스 번역기 에이전트 모드 (API 키 없이 구독 앱의 모델로 번역)

      tullius open <플러그인.esp> [--strings <Strings 폴더>] [--batch-size N]   프로젝트 만들기(GUI와 같은 프로젝트)
      tullius projects                                                       프로젝트 목록
      tullius status <프로젝트>                                              진행 상황
      tullius next <프로젝트>                                                첫 요청 만들기(끊긴 뒤 다시 시작할 때도)
      tullius submit <프로젝트> <답 파일>                                    답 저장, 다음 요청 만들기
      tullius check <프로젝트>                                               품질검사
      tullius edit <프로젝트> <수정.json>                                    줄 고치기([{"order": 순번, "text": "새 번역"}])
      tullius export <프로젝트> [출력 폴더]                                  번역된 플러그인 저장

    <프로젝트>는 키, 플러그인 파일 이름, 플러그인 경로 중 하나. 작업 파일은 ./work(--work로 바꿈).
    프로젝트 DB는 GUI 번역기와 같은 곳(--projects로 바꿈).
    """);

sealed record EditLine(int Order, string Text);
