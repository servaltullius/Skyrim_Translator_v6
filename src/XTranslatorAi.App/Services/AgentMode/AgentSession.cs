using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>An agent-mode project: the plugin, its project DB (the GUI's), and the fixed run settings.</summary>
public sealed record AgentProject(string Key, string PluginPath, string DbPath, PluginReadOptions ReadOptions,
    string TargetEncoding, string ModelProfile, int BatchSize, int MaxChars)
{
    /// <summary>The lane a command works in (<c>--lane 2/3</c>); null for the whole project. Not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public AgentLane? Lane { get; init; }

    /// <summary>The project's work folder under the work root, or its lane's ("Key/lane-2-of-3").</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string WorkFolder => Lane == null ? Key : Path.Combine(Key, Lane.Folder);
}

/// <summary>
/// One of several lanes a project is translated in at once, each by its own agent: lane <see cref="Index"/> of
/// <see cref="Count"/>. An agent app answers one request at a time and the requests of a project come one after
/// another, so a project took as long as all its requests (Meridia's Order, 4,062 lines: 89 minutes); the time is the
/// model writing, not the translator.
/// </summary>
public sealed record AgentLane(int Index, int Count)
{
    public string Folder => $"lane-{Index}-of-{Count}";

    public override string ToString() => $"{Index}/{Count}";

    public static AgentLane? Parse(string? value)
    {
        if (value == null) return null;
        var parts = value.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var index) || !int.TryParse(parts[1], out var count)
            || count is < 2 or > 8 || index < 1 || index > count)
        {
            throw new AgentModeException("--lane은 '번호/갈래 수'로 쓰세요(예: 2/3, 갈래는 2~8개).");
        }

        return new AgentLane(index, count);
    }
}

/// <summary>
/// What a step did: rows saved, the lines of the answered request it did not save and why, the next request (null
/// when the plugin is done, with the size of its request.md) and why it stopped.
/// </summary>
/// <summary>What linking a Korean release did: rows it filled, rows already translated it left as they are, rows it
/// has no translation for (still 대기), the filled rows whose game text (the Korean patch's) is different, and the rows
/// it did not fill because their English changed since the release (its English original linked) or their numbers
/// are not the release's (without it).</summary>
public sealed record AgentKoreanLink(PreviousTranslationMatcher.Result Match, int Filled, int KeptTranslated, int Untranslated,
    IReadOnlyList<long> DifferentFromGame, IReadOnlyList<long> EnglishChanged, bool ComparedWithEnglish)
{
    /// <summary>The release was an older one saved in CP949 and was read as CP949 (PluginReader.ReadTranslatedReleaseAsync).</summary>
    public bool ReadAsCp949 { get; init; }

    /// <summary>How many of its lines read as UTF-8 though CP949 too, and may be missing from the link; null when none.</summary>
    public string? Cp949Ambiguous { get; init; }
}

/// <summary>
/// The project's glossary terms in use, and those a new project took from the same mod's other projects (the GUI says so
/// when it opens one): the agent learns its names come from them rather than finding out line by line.
/// </summary>
public sealed record AgentGlossary(int Count, int Inherited, IReadOnlyList<string> From);

public sealed record AgentStepReport(AgentProject Project, int PendingBefore, int PendingAfter, int ErrorRows,
    AgentRequest? Next, string? NextRequestPath, string? Problem)
{
    public int Saved => Math.Max(0, PendingBefore - PendingAfter);
    public bool Finished => Next == null && Problem == null && PendingAfter == 0;
    public IReadOnlyList<AgentRejection> Rejected { get; init; } = Array.Empty<AgentRejection>();

    /// <summary>The run itself failed (a locked DB, a full disk): Problem says why.</summary>
    public bool RunFailed { get; init; }
    public int NextRequestChars { get; init; }
    public IReadOnlySet<string> NewInstructionSections { get; init; } = new HashSet<string>();

    /// <summary>
    /// What of the global DB (global glossary, series TM, TM name index) the run could not read and translated without.
    /// The GUI says so; the agent's step used to report 번역 완료 with only a WARN line in app.log.
    /// </summary>
    public IReadOnlyList<string> MissingGlobalData { get; init; } = Array.Empty<string>();
}

public sealed class AgentModeException(string message) : Exception(message);

/// <summary>
/// The work folder of agent mode (inside the folder the agent app opened, which its sandbox lets it write):
/// <c>&lt;work&gt;/&lt;key&gt;/project.json</c>, the one pending request (<c>pending.json</c>, <c>request.md</c> for the model
/// to read) and every answer given (<c>responses/&lt;hash&gt;.&lt;attempt&gt;.txt</c>). Answers are kept: a replay starts the
/// run over and must answer the calls before the pending one again (a batch the run had not saved yet).
/// </summary>
public sealed class AgentSession
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _workRoot;
    private readonly GlobalProjectDbService _globalDb;
    private readonly GameStringService? _gameStrings;
    private readonly AppSettings _settings;
    private readonly string? _projectsRootOverride;
    private readonly BundledFranchiseTmSeedService? _tmSeed;

    /// <param name="tmSeed">Writes the bundled Korean patch TM for import (null: none, as in tests).</param>
    public AgentSession(string workRoot, GlobalProjectDbService globalDb, GameStringService? gameStrings, AppSettings settings,
        string? projectsRootOverride = null, BundledFranchiseTmSeedService? tmSeed = null)
    {
        _workRoot = Path.GetFullPath(workRoot);
        _globalDb = globalDb;
        _gameStrings = gameStrings;
        _settings = settings;
        _projectsRootOverride = projectsRootOverride;
        _tmSeed = tmSeed;
    }

    /// <summary>What the last <see cref="EnsureSeriesTmAsync"/> imported, for open to say; null when nothing.</summary>
    public string? SeriesTmNote { get; private set; }

    /// <summary>
    /// The bundled series TM, written and imported as the GUI does when it opens a project. Agent mode wrote it and never
    /// imported it, so the kit alone translated with an empty series TM. The global DB is opened first: creating it
    /// forgets the seed's stamps, which written before would be lost and the seed written again.
    /// </summary>
    private async Task EnsureSeriesTmAsync(CancellationToken cancellationToken)
    {
        SeriesTmNote = null;
        try
        {
            await _globalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, cancellationToken);
            if (_tmSeed != null) await _tmSeed.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, cancellationToken);
            var (applied, unreadable) = await new FranchiseTranslationMemoryService(_globalDb).ImportPendingFilesAsync(
                _settings.PluginSourceLanguage, _settings.PluginTargetLanguage, onFile: null, cancellationToken);
            SeriesTmNote = unreadable != null ? $"시리즈 TM 가져오기: {unreadable}"
                : applied > 0 ? $"시리즈 TM: {applied}개 항목을 가져왔습니다(내장 한글패치 TM 등)."
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As in the GUI, the import never stops the work, and is told as the GUI's status line tells it: the run read
            // the global DB and said nothing, so the kit translated with an empty series TM and no warning.
            AppLog.Write($"WARN 시리즈 TM 자동 가져오기 실패: {ex}");
            SeriesTmNote = $"경고: 시리즈 TM 가져오기 실패({ex.Message}). 내장 한글패치 TM 없이 번역합니다(자세한 내용: app.log).";
        }
    }

    public async Task<(AgentProject Project, int Rows, int Pending, AgentGlossary Glossary)> OpenAsync(string pluginPath, PluginReadOptions readOptions,
        string targetLanguage, string targetEncoding, int batchSize, CancellationToken cancellationToken)
    {
        await EnsureSeriesTmAsync(cancellationToken);
        var workspace = new ProjectWorkspaceService(_globalDb, _projectsRootOverride, _gameStrings);
        var loaded = await workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(
            Path.GetFullPath(pluginPath), readOptions, targetLanguage, targetEncoding, AgentTranslationRun.DefaultModelProfile,
            _settings.CustomPromptText ?? "", _settings.UseCustomPrompt), cancellationToken);
        await using var db = loaded.Db;
        var pending = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, cancellationToken);
        var glossary = new AgentGlossary((await db.GetGlossaryAsync(cancellationToken)).Count(g => g.Enabled),
            loaded.InheritedGlossary.Count, loaded.InheritedGlossary.FromPlugins ?? Array.Empty<string>());
        var key = ProjectKey(loaded.Document.Info.InputPath, db.DbPath);
        var project = new AgentProject(key, loaded.Document.Info.InputPath, db.DbPath, loaded.Document.Info.Options, targetEncoding,
            AgentTranslationRun.DefaultModelProfile, Math.Clamp(batchSize, 1, 100), Math.Clamp(_settings.MaxCharsPerBatch, 1000, 50000));
        var existing = TryRead(key);
        if (existing != null)
        {
            // The plugin was opened again with requests still out: their rows may have changed under them.
            ClearPending(existing);
            foreach (var lane in Directory.Exists(ProjectDir(existing)) ? Directory.GetDirectories(ProjectDir(existing), "lane-*") : Array.Empty<string>())
            {
                File.Delete(Path.Combine(lane, "pending.json"));
                File.Delete(Path.Combine(lane, "request.md"));
            }
        }

        Directory.CreateDirectory(ProjectDir(project));
        await File.WriteAllTextAsync(Path.Combine(ProjectDir(project), "project.json"), JsonSerializer.Serialize(project, Json), cancellationToken);
        return (project, loaded.Entries.Count, pending.Count, glossary);
    }

    /// <summary>
    /// Review mode: links an existing Korean release of the plugin (the GUI's 이전 번역 불러오기) and fills the rows not
    /// yet translated with it, so the agent reviews what a person translated instead of writing every line again, and
    /// keeps that person's names and choices. Rows the release has no translation for stay 대기 for next, whose requests
    /// show an earlier translation as reference where the release has one.
    /// </summary>
    public async Task<AgentKoreanLink> LinkKoreanAsync(AgentProject project, string koreanPath, string? stringsDirectory,
        CancellationToken cancellationToken, string? englishPath = null)
    {
        if (Pending(project) != null || Directory.Exists(ProjectDir(project))
            && Directory.GetDirectories(ProjectDir(project), "lane-*").Any(d => File.Exists(Path.Combine(d, "pending.json"))))
        {
            throw new AgentModeException("대기 중인 요청이 있습니다. 그 요청에 답해 끝낸 뒤(또는 open으로 다시 연 뒤) 한국어판을 연결하세요.");
        }

        var path = Path.GetFullPath(koreanPath);
        if (!File.Exists(path)) throw new AgentModeException($"한국어판 플러그인이 없습니다: {koreanPath}");
        var current = await PluginReader.ReadAsync(project.PluginPath, project.ReadOptions, cancellationToken);
        // A translated release is written in the target encoding, with its Strings or BSA next to it.
        var options = project.ReadOptions.ForOtherRelease() with { SourceEncoding = project.TargetEncoding, StringsDirectory = stringsDirectory };
        var korean = await PluginReader.ReadTranslatedReleaseAsync(path, options, cancellationToken);
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var info = await db.TryGetProjectAsync(cancellationToken);
        var match = PreviousTranslationMatcher.Match(current.Fields, current.Info.Masters, korean.Fields, korean.Info.Masters,
            XTranslatorAi.Core.LanguageHelper.IsKoreanLanguage(info?.DestLang ?? "korean"));
        if (match.TextByFieldKey.Count == 0)
        {
            throw new AgentModeException($"{Path.GetFileName(path)}에서 같은 레코드의 번역문을 찾지 못했습니다. 같은 모드의 한국어판인지 확인하세요.");
        }

        // The English original is read and checked first: a wrong --english path, refused after the release was linked,
        // left the release linked without its English, and the next request asked every row.
        var englishSources = englishPath == null ? null
            : await ReadEnglishSourcesAsync(current, englishPath, _settings.PluginSourceEncoding, cancellationToken);
        await db.ReplacePreviousTranslationsAsync(Path.GetFileName(path), match.TextByFieldKey, cancellationToken);
        if (englishSources != null)
        {
            await db.ReplacePreviousSourcesAsync(Path.GetFileName(Path.GetFullPath(englishPath!)), englishSources, cancellationToken);
        }

        var earlier = await db.GetPreviousTranslationsByStringIdAsync(cancellationToken);
        var open = (await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, cancellationToken)).ToHashSet();
        // A release made for an earlier version of the mod: a row whose English changed since (new magnitudes, a
        // rewritten line) was filled with the old text as 완료, and nothing told the review it differed. With the
        // release's English original linked, only rows whose English is the same are filled (as the GUI reuses an
        // earlier translation); without it, rows whose numbers are not the release's. The others are translated with
        // the release's line as a reference.
        var sameEnglish = englishPath == null ? null : (await db.GetReusablePreviousTranslationsByStringIdAsync(cancellationToken)).Keys.ToHashSet();
        var sources = (await db.GetStringTranslationContextsByIdsAsync(earlier.Keys.Where(open.Contains).ToList(), cancellationToken))
            .ToDictionary(kv => kv.Key, kv => kv.Value.SourceText);
        var changed = earlier.Where(kv => open.Contains(kv.Key)
                && (sameEnglish != null ? !sameEnglish.Contains(kv.Key) : !NumbersFit(sources.GetValueOrDefault(kv.Key) ?? "", kv.Value)))
            .Select(kv => kv.Key).ToHashSet();
        open.ExceptWith(changed);
        // The release fills the base game's lines too: the Teldryn release has him speak 반말 where the Korean patch has
        // 하오체, and the game's text for his 44 vanilla lines left him switching between the two. Those that differ from
        // the game's text are listed, for the review to judge (a name the patch spells otherwise, or a speech level).
        var game = _gameStrings == null ? null : await _gameStrings.TryLoadAsync(cancellationToken);
        var gameRows = game == null ? new Dictionary<long, GameStringMatch>()
            : await game.MatchAsync(db, open.ToList(), cancellationToken);
        var fill = earlier.Where(kv => open.Contains(kv.Key))
            .Select(kv => (Id: kv.Key, DestText: kv.Value, Status: StringEntryStatus.Done, ErrorMessage: (string?)null))
            .ToList();
        await db.UpdateStringTranslationsAsync(fill, cancellationToken);
        var note = $"기존 한국어판({Path.GetFileName(path)}) 그대로, 검수 전";
        foreach (var row in fill)
        {
            await db.UpsertStringNoteAsync(row.Id, XTranslatorAi.Core.TranslationConstants.PreviousReleaseNoteKind, note, cancellationToken);
        }

        var different = fill.Where(row => gameRows.TryGetValue(row.Id, out var g) && g.Text.Trim() != row.DestText.Trim())
            .Select(row => row.Id).OrderBy(id => id).ToList();
        return new AgentKoreanLink(match, fill.Count, earlier.Count - fill.Count - changed.Count, open.Count - fill.Count + changed.Count, different,
            changed.OrderBy(id => id).ToList(), englishPath != null)
        {
            ReadAsCp949 = korean.Info.Diagnostics.Any(item => item.Code == PluginReader.Cp949ReadDiagnostic),
            Cp949Ambiguous = korean.Info.Diagnostics.FirstOrDefault(item => item.Code == PluginReader.Cp949AmbiguousDiagnostic)?.Message,
        };
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadEnglishSourcesAsync(PluginDocument current, string englishPath,
        string sourceEncoding, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(englishPath);
        if (!File.Exists(path)) throw new AgentModeException($"한국어판의 영어 원본 플러그인이 없습니다: {englishPath}");
        // The earlier English is read as the current English is, with its own Strings or BSA next to it, from the
        // chosen source encoding (the current one may be its fallback).
        // A Korean file given here is read too, and refused below as Korean (not as an encoding error).
        var english = await PluginReader.ReadTranslatedReleaseAsync(path, current.Info.Options.ForOtherRelease(sourceEncoding), cancellationToken);
        var sources = PreviousTranslationMatcher.MatchSources(current.Fields, current.Info.Masters, english.Fields, english.Info.Masters);
        if (sources.SourceByFieldKey.Count == 0)
        {
            throw new AgentModeException($"{Path.GetFileName(path)}에서 같은 레코드를 찾지 못했습니다. 한국어판이 번역한 영어 원본인지 확인하세요.");
        }

        if (sources.WithHangul * 2 > sources.SourceByFieldKey.Count)
        {
            throw new AgentModeException($"{Path.GetFileName(path)}은 한국어로 번역된 파일입니다. --english에는 한국어판이 번역한 영어 원본을 주세요.");
        }

        return sources.SourceByFieldKey;
    }

    // A number, its thousands separators included ("1,000" is one number, not 1 and 000).
    private static readonly System.Text.RegularExpressions.Regex Digits = new(@"\d{1,3}(?:,\d{3})+(?!\d)|\d+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // Markup: a Korean release that changed a font size (<font size='60'>) or an image's width is not a changed text.
    private static readonly System.Text.RegularExpressions.Regex Markup = new(@"<[^<>]*>", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Every number of the English text (markup left out) is in the Korean as many times (a changed magnitude is not).</summary>
    internal static bool NumbersFit(string english, string korean)
    {
        static IEnumerable<string> Numbers(string text)
            => Digits.Matches(Markup.Replace(text, " ")).Select(m => m.Value.Replace(",", ""));
        var left = Numbers(korean).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        foreach (var number in Numbers(english))
        {
            var count = left.GetValueOrDefault(number);
            if (count == 0) return false;
            left[number] = count - 1;
        }

        return true;
    }

    /// <summary>
    /// Makes the waiting request again after a change to what requests show (a decided speech level): its answer,
    /// written for the old request, would be dropped. In the Codex trial a level decided with a request waiting cost
    /// that request's 30 answers. Null when no request waits; a lane's waiting requests are left (count returned).
    /// </summary>
    public async Task<(AgentStepReport? Report, int LanesWaiting, bool LeftAsItWas)> RemakePendingAsync(AgentProject project, CancellationToken cancellationToken)
    {
        var lanes = Directory.Exists(ProjectDir(project))
            ? Directory.GetDirectories(ProjectDir(project), "lane-*").Count(d => File.Exists(Path.Combine(d, "pending.json")))
            : 0;
        if (Pending(project) == null) return (null, lanes, false);
        // A request that next would refuse to make (another lane's or the laneless run's request waiting) stays: cleared,
        // it was gone and the command ended in 실패. It still shows what was there before (LeftAsItWas).
        if (LaneConflict(project, making: true) != null) return (null, lanes, true);
        ClearPending(project);
        return (await NextAsync(project, cancellationToken), lanes, false);
    }

    public IReadOnlyList<AgentProject> Projects()
        => !Directory.Exists(_workRoot)
            ? Array.Empty<AgentProject>()
            : Directory.GetDirectories(_workRoot).Select(dir => TryRead(Path.GetFileName(dir))).OfType<AgentProject>().ToList();

    /// <summary>A project by its key, its plugin's file name or its plugin's path.</summary>
    public AgentProject Find(string nameOrKeyOrPath)
    {
        var all = Projects();
        var full = File.Exists(nameOrKeyOrPath) ? Path.GetFullPath(nameOrKeyOrPath) : null;
        var matches = all.Where(p => string.Equals(p.Key, nameOrKeyOrPath, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Path.GetFileName(p.PluginPath), nameOrKeyOrPath, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Path.GetFileNameWithoutExtension(p.PluginPath), nameOrKeyOrPath, StringComparison.OrdinalIgnoreCase)
                                     || (full != null && string.Equals(p.PluginPath, full, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new AgentModeException($"'{nameOrKeyOrPath}' 프로젝트가 없습니다. 먼저 open으로 플러그인을 여세요."),
            _ => throw new AgentModeException($"'{nameOrKeyOrPath}'에 맞는 프로젝트가 여럿입니다: {string.Join(", ", matches.Select(m => m.Key))}. 키로 지정하세요."),
        };
    }

    public AgentRequest? Pending(AgentProject project)
        => File.Exists(PendingPath(project)) ? JsonSerializer.Deserialize<AgentRequest>(File.ReadAllText(PendingPath(project))) : null;

    public string RequestPath(AgentProject project) => Path.Combine(LaneDir(project), "request.md");

    public async Task<(int Rows, int Pending, int Errors, int Edited)> StatusAsync(AgentProject project, CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var all = await db.GetStringIdsByStatusAsync(Enum.GetValues<StringEntryStatus>(), cancellationToken);
        var pending = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error, StringEntryStatus.InProgress }, cancellationToken);
        var errors = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Error }, cancellationToken);
        var edited = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Edited }, cancellationToken);
        if (await LaneIdsAsync(db, project, cancellationToken) is { } lane)
        {
            return (all.Count(lane.Contains), pending.Count(lane.Contains), errors.Count(lane.Contains), edited.Count(lane.Contains));
        }

        return (all.Count, pending.Count, errors.Count, edited.Count);
    }

    /// <summary>
    /// The names the run decided itself: translated name fields (FULL, SHRT) that no glossary term, TM, game text, Korean
    /// release or TM fallback gave, once per English. The MCP trial's three models agreed on 20 of 50 new skill names, so
    /// these are the lines a person should look over and fix in the glossary.
    /// </summary>
    public static async Task<IReadOnlyList<StringEntry>> NewNamesAsync(ProjectDb db, IReadOnlyList<StringEntry> rows, CancellationToken cancellationToken)
    {
        var given = new HashSet<long>();
        foreach (var kind in XTranslatorAi.Core.TranslationConstants.KnownTranslationNoteKinds.Append(XTranslatorAi.Core.TranslationConstants.TmFallbackNoteKind))
        {
            given.UnionWith((await db.GetStringNotesByKindAsync(kind, cancellationToken)).Keys);
        }

        var terms = (await db.GetGlossaryAsync(cancellationToken)).Where(g => g.Enabled)
            .Select(g => g.SourceTerm.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A name under two prefixes is one name: "Learning - Kaiten" and "Rim Scroll - Kaiten" both end 회천.
        static string Tail(string text) => text.IndexOf('-') is var dash and > 0 ? text[(dash + 1)..].Trim() : text.Trim();
        var seen = new HashSet<(string, string)>();
        return rows
            .Where(r => r.Status is StringEntryStatus.Done or StringEntryStatus.Edited && !string.IsNullOrWhiteSpace(r.DestText))
            .Where(r => r.Rec is { } rec && (rec.EndsWith(":FULL", StringComparison.OrdinalIgnoreCase) || rec.EndsWith(":SHRT", StringComparison.OrdinalIgnoreCase)))
            .Where(r => !given.Contains(r.Id) && !terms.Contains(r.SourceText.Trim()) && seen.Add((Tail(r.SourceText), Tail(r.DestText))))
            .ToList();
    }

    /// <summary>The lanes ("2/3") with a request waiting.</summary>
    public IReadOnlyList<string> LanesWaiting(AgentProject project)
        => Directory.Exists(ProjectDir(project))
            ? Directory.GetDirectories(ProjectDir(project), "lane-*")
                .Where(d => File.Exists(Path.Combine(d, "pending.json")))
                .Select(d => Path.GetFileName(d)["lane-".Length..].Replace("-of-", "/", StringComparison.Ordinal))
                .OrderBy(lane => lane, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>
    /// A run without a lane covers every lane's rows: with a lane's request waiting it asked the same lines again, and
    /// the lane's answer, saved second, was dropped (twice the usage). A new conversation that saw no waiting request in
    /// status (it showed only the run's own) went that way, and so did a lane's agent that left out --lane. The other
    /// way round as well: a lane's first request, made while the laneless one waited, asked that request's lines again.
    /// A lane may still answer its own request then (<paramref name="making"/> false): made before, it is the older.
    /// </summary>
    private string? LaneConflict(AgentProject project, bool making)
    {
        if (project.Lane == null && LanesWaiting(project) is { Count: > 0 } lanes)
        {
            return $"갈래 {string.Join(", ", lanes)}에 대기 중인 요청이 있습니다. 갈래로 나눠 번역 중이면 next·submit에 "
                   + $"--lane {lanes[0]}처럼 갈래를 붙여 그 요청부터 답하세요(갈래를 그만두려면 open으로 다시 열어 대기 요청을 지운 뒤 next).";
        }

        if (making && project.Lane != null && File.Exists(PendingPath(project with { Lane = null })))
        {
            return $"갈래 없이 만든 요청이 대기 중입니다({RequestPath(project with { Lane = null })}). 그 요청이 모든 갈래의 줄을 묻습니다: "
                   + "--lane 없이 그 요청에 먼저 답하거나, 갈래로 나누려면 open으로 다시 열어 대기 요청을 지운 뒤 갈래마다 next하세요.";
        }

        return null;
    }

    private void RefuseOnLaneConflict(AgentProject project, bool making)
    {
        if (LaneConflict(project, making) is { } refusal)
        {
            throw new AgentModeException(refusal);
        }
    }

    /// <summary>Starts (or, after a break, restarts) the run and writes its first request.</summary>
    public async Task<AgentStepReport> NextAsync(AgentProject project, CancellationToken cancellationToken)
    {
        if (Pending(project) != null)
        {
            throw new AgentModeException($"대기 중인 요청이 있습니다({RequestPath(project)}). 먼저 그 결과를 submit하세요.");
        }

        RefuseOnLaneConflict(project, making: true);
        await EnsureSeriesTmAsync(cancellationToken);

        var (report, _) = await StepAsync(project, cancellationToken);
        return await WriteNextAsync(report, Array.Empty<AgentRejection>(), cancellationToken);
    }

    /// <summary>
    /// Saves the model's answer to the pending request and runs again: the answer is used where the run asks for it,
    /// and the run stops at its next request. An answer the run never asks for (the rows changed since the request
    /// was written) is dropped with nothing saved.
    /// </summary>
    public async Task<AgentStepReport> SubmitAsync(AgentProject project, string answer, CancellationToken cancellationToken)
    {
        RefuseOnLaneConflict(project, making: false);
        var pending = Pending(project) ?? throw new AgentModeException("대기 중인 요청이 없습니다. next로 요청을 만드세요"
                                                                     + (project.Lane == null ? "(갈래로 나눠 번역 중이면 --lane을 붙이세요)." : "."));
        var json = AgentAnswerCheck.IsJson(pending);
        if (json)
        {
            try
            {
                if (TranslationResultParser.ParseTranslations(answer).Count == 0)
                {
                    throw new InvalidOperationException("translations 배열에 항목이 없습니다.");
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new AgentModeException($"결과 형식이 맞지 않아 저장하지 않았습니다: {ex.Message} " +
                                             "요청의 응답 형식({\"translations\":[{\"id\":..,\"text\":\"..\"}]}) 그대로 다시 쓰세요.");
            }

            var asked = AgentAnswerCheck.Lines(pending).Select(line => line.Id).OfType<long>().ToHashSet();
            if (asked.Count > 0 && AgentAnswerCheck.AnsweredIds(answer) is { } given && !given.Any(asked.Contains))
            {
                throw new AgentModeException("이 답의 id가 지금 요청의 id와 하나도 맞지 않아 저장하지 않았습니다(앞 요청의 답을 다시 낸 것 같습니다). " +
                                             $"{RequestPath(project)}의 id로 답을 새로 쓰세요.");
            }
        }
        else
        {
            if (AgentAnswerCheck.AnsweredIds(answer) is { Count: > 1 })
            {
                throw new AgentModeException("이 요청은 한 줄 글 요청인데 답이 여러 줄 JSON이라 저장하지 않았습니다(앞 요청의 답을 다시 낸 것 같습니다). " +
                                             $"{RequestPath(project)}의 글을 옮긴 번역문만 쓰세요.");
            }

            // A file ends with a newline; the run would keep it as spaces at the line's end (a repaired line is not trimmed).
            answer = answer.TrimEnd('\r', '\n');
        }

        var (targetLang, mode) = await CheckSettingsAsync(project, cancellationToken);
        var reasons = AgentAnswerCheck.Explain(pending, answer, targetLang, mode, out var textReason);

        var answerPath = AnswerPath(project, pending.Hash, pending.Attempt);
        Directory.CreateDirectory(Path.GetDirectoryName(answerPath)!);
        await File.WriteAllTextAsync(answerPath, answer, cancellationToken);
        // The request is cleared once the run has gone through: a run that failed (a lane count typed wrong, lanes.json
        // busy) used to leave no request, and the output said only "실패".
        var (report, used) = await StepAsync(project, cancellationToken);
        if (report.RunFailed && !used.Contains((pending.Hash, pending.Attempt)))
        {
            // The run stopped before it came to the answer: "다시 만들지 않아 … 새로 만든 요청에 답하세요" hid the error,
            // and the request and the answer were deleted with no new request written.
            return report with { Problem = report.Problem + " 요청과 답은 그대로 두었습니다. 원인을 해결한 뒤 같은 답 파일로 다시 submit하세요." };
        }

        ClearPending(project);
        if (!used.Contains((pending.Hash, pending.Attempt)))
        {
            File.Delete(answerPath);
            return await WriteNextAsync(report with
            {
                Problem = "번역기가 이 답이 맞는 요청을 다시 만들지 않아 답을 쓰지 않았습니다(요청을 만든 뒤 프로젝트가 바뀜). " +
                          "request.md에 새로 만든 요청에 답하세요.",
            }, Array.Empty<AgentRejection>(), cancellationToken);
        }

        var rejected = await RejectedAsync(project, pending, report, reasons, textReason, cancellationToken);
        // Both waiting (made by an earlier version): the lane's answer is saved, and its next request, which would ask
        // the laneless request's lines again, is not made.
        if (LaneConflict(project, making: true) is { } refusal)
        {
            return report with
            {
                Rejected = rejected, Next = null,
                Problem = (report.Problem == null ? "" : report.Problem + " ") + "이 답은 저장했고, 이 갈래의 다음 요청은 만들지 않았습니다. " + refusal,
            };
        }

        return await WriteNextAsync(report with { Rejected = rejected }, rejected, cancellationToken);
    }

    /// <summary>The lines of the answered request the run did not save: rows still 대기/오류, or a one-line request asked again.</summary>
    private static async Task<IReadOnlyList<AgentRejection>> RejectedAsync(AgentProject project, AgentRequest answered,
        AgentStepReport report, IReadOnlyDictionary<long, string> reasons, string? textReason, CancellationToken cancellationToken)
    {
        const string Unknown = "번역기 검증에 걸렸습니다(이유를 특정하지 못함). 토큰·태그·줄바꿈 수와 수치 자리표시자 위치를 확인하세요.";
        var lines = AgentAnswerCheck.Lines(answered);
        if (!AgentAnswerCheck.IsJson(answered))
        {
            var askedAgain = report.Next != null && (report.Next.Hash == answered.Hash || AgentAnswerCheck.IsRepair(report.Next));
            return askedAgain && lines.Count == 1
                ? new[] { new AgentRejection(null, Plain(lines[0].Text), textReason ?? Unknown) }
                : Array.Empty<AgentRejection>();
        }

        var ids = lines.Select(line => line.Id).OfType<long>().ToList();
        if (ids.Count == 0) return Array.Empty<AgentRejection>();
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var statuses = await db.GetStringStatusesByIdsAsync(ids, cancellationToken);
        return lines
            .Where(line => line.Id is { } id && statuses.TryGetValue(id, out var status) && status is StringEntryStatus.Pending or StringEntryStatus.Error)
            .Select(line => new AgentRejection(line.Id, Plain(line.Text), reasons.GetValueOrDefault(line.Id!.Value) ?? Unknown))
            .ToList();
    }

    private static string Plain(string requestText)
        => System.Text.RegularExpressions.Regex.Replace(requestText.Replace(" __XT_PH_9999__", ""), "⟦XT_[^⟧]*⟧", "");

    private async Task<(string TargetLang, PlaceholderSemanticRepairMode Mode)> CheckSettingsAsync(AgentProject project,
        CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var info = await db.TryGetProjectAsync(cancellationToken);
        // The batch result check runs the placeholder check only with the repair pass on (as the run is set up).
        return (info?.DestLang ?? "korean", _settings.EnableRepairPass ? _settings.SemanticRepairMode : PlaceholderSemanticRepairMode.Off);
    }

    private async Task<(AgentStepReport Report, IReadOnlySet<(string Hash, int Attempt)> Used)> StepAsync(AgentProject project,
        CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var gameStrings = _gameStrings == null ? null : await _gameStrings.TryLoadAsync(cancellationToken);
        var options = new AgentTranslationRun.Options(project.ModelProfile, project.BatchSize, project.MaxChars)
        {
            Lane = await LaneIdsAsync(db, project, cancellationToken),
        };
        string? Answer(string hash, int attempt) => File.Exists(AnswerPath(project, hash, attempt)) ? File.ReadAllText(AnswerPath(project, hash, attempt)) : null;
        var outcome = await AgentTranslationRun.RunAsync(db, _settings, options, Answer, _globalDb, gameStrings, cancellationToken);
        var pendingBefore = outcome.PendingBefore;
        var used = new HashSet<(string Hash, int Attempt)>(outcome.Used);
        var missing = new List<string>(outcome.MissingGlobalData);
        // A request recorded after answers in the same run was built while the answered rows were still that run's
        // rows (and its name memory had their translations); the next run starts without them and asks differently.
        // So the request kept is the first one of a fresh run, the one a replay will ask for. The same goes for rows the
        // run saved itself (game dictionary, TM): they were in the context of their neighbors' request, and are not
        // among a later run's rows.
        for (var pass = 0; pass < 5 && outcome.Next != null && (outcome.Used.Count > 0 || outcome.PendingAfter < outcome.PendingBefore)
                           && outcome.Error == null; pass++)
        {
            outcome = await AgentTranslationRun.RunAsync(db, _settings, options, Answer, _globalDb, gameStrings, cancellationToken);
            used.UnionWith(outcome.Used);
            missing.AddRange(outcome.MissingGlobalData.Where(what => !missing.Contains(what)));
        }

        // A line the run gave up on (오류) is asked with the same request the next time, so its stored answers were
        // replayed and refused again, and `next` never asked anything new. With nothing left to ask, the answers this
        // run used are dropped: the next run asks the 오류 lines afresh (lines already saved are not asked again).
        if (outcome.Next == null && outcome.ErrorRows > 0 && outcome.Error == null)
        {
            foreach (var (hash, attempt) in used)
            {
                File.Delete(AnswerPath(project, hash, attempt));
            }
        }

        outcome = outcome with { PendingBefore = pendingBefore, Used = used };
        string? problem = outcome.Error == null ? null : "오류: " + outcome.Error.Message;
        if (outcome.Next is { Attempt: > 0 } again)
        {
            problem ??= $"같은 요청을 다시 받았습니다({again.Attempt + 1}번째). 앞선 답을 번역기가 받아들이지 않았습니다. request.md의 '앞 답이 걸린 이유'를 보고 답하세요.";
        }

        return (new AgentStepReport(project, outcome.PendingBefore, outcome.PendingAfter, outcome.ErrorRows, outcome.Next,
            outcome.Next == null ? null : RequestPath(project), problem) { MissingGlobalData = missing, RunFailed = outcome.Error != null }, outcome.Used);
    }

    /// <summary>Writes the report's next request (pending.json, instructions.md, request.md), if it has one.</summary>
    private async Task<AgentStepReport> WriteNextAsync(AgentStepReport report, IReadOnlyList<AgentRejection> rejected,
        CancellationToken cancellationToken)
    {
        var carried = CarryRejections(LaneDir(report.Project), rejected, report.Next);
        if (report.Next is not { } request) return report;
        var dir = LaneDir(report.Project);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(PendingPath(report.Project), JsonSerializer.Serialize(request, Json), cancellationToken);
        var added = AgentRequestRenderer.WriteInstructions(dir, AgentRequestRenderer.Split(request).Sections);
        var text = AgentRequestRenderer.Render(report.Project, request, added, carried, InstructionsPath(report.Project));
        await File.WriteAllTextAsync(RequestPath(report.Project), text, cancellationToken);
        return report with { NextRequestChars = text.Length, NewInstructionSections = added };
    }

    /// <summary>
    /// The reasons to show with the next request: those of its own lines. A line the run asks again later, after
    /// another request (a book's one-line request came between in Meridia's Order), had its reason shown with that
    /// other request and none with its own; such a reason is kept in rejections.json until its line is asked.
    /// </summary>
    internal static IReadOnlyList<AgentRejection> CarryRejections(string projectDir, IReadOnlyList<AgentRejection> rejected, AgentRequest? next)
    {
        var path = Path.Combine(projectDir, "rejections.json");
        var kept = File.Exists(path) ? JsonSerializer.Deserialize<List<AgentRejection>>(File.ReadAllText(path)) ?? new() : new List<AgentRejection>();
        foreach (var rejection in rejected.Where(r => r.Id != null))
        {
            kept.RemoveAll(r => r.Id == rejection.Id);
            kept.Add(rejection);
        }

        if (next == null)
        {
            if (File.Exists(path)) File.Delete(path);
            return Array.Empty<AgentRejection>();
        }

        // A one-line text request names no id: its line is known by its text.
        var lines = AgentAnswerCheck.Lines(next);
        var asked = lines.Select(line => line.Id).OfType<long>().ToHashSet();
        var texts = lines.Where(line => line.Id == null).Select(line => Plain(line.Text)).ToHashSet(StringComparer.Ordinal);
        bool Asked(AgentRejection r) => asked.Contains(r.Id!.Value) || texts.Contains(r.Source);
        var shown = kept.Where(Asked).ToList();
        kept.RemoveAll(Asked);
        if (kept.Count > 0)
        {
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(path, JsonSerializer.Serialize(kept, Json));
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }

        // A one-line text request's rejection has no id: it is that request asked again.
        return shown.Concat(rejected.Where(r => r.Id == null)).ToList();
    }

    public string InstructionsPath(AgentProject project) => Path.Combine(LaneDir(project), AgentRequestRenderer.InstructionsFileName);

    /// <summary>
    /// The rows of the project's lane, or null without one. Rows go to lanes a dialogue at a time (a topic's lines,
    /// or a record's), by the whole project so that every run divides it alike, each dialogue to the lane with the
    /// least English so far. The count is kept: lanes of another count would share rows.
    /// </summary>
    private async Task<IReadOnlySet<long>?> LaneIdsAsync(ProjectDb db, AgentProject project, CancellationToken cancellationToken)
    {
        if (project.Lane is not { } lane) return null;
        var countPath = Path.Combine(ProjectDir(project), "lanes.json");
        var keptText = await WithRetryAsync(() => File.Exists(countPath) ? File.ReadAllText(countPath).Trim() : null);
        var hasKept = int.TryParse(keptText, out var kept);
        if (hasKept && kept != lane.Count
            && Directory.GetDirectories(ProjectDir(project), "lane-*").Any(d => File.Exists(Path.Combine(d, "pending.json"))))
        {
            throw new AgentModeException($"이 프로젝트는 {kept}갈래로 나눠 번역 중입니다(대기 중인 요청이 있음). --lane ?/{kept}로 이어 하세요.");
        }

        // Written only when the count changes: every lane's every step rewrote it, and two lanes at once could meet on
        // the file (a sharing violation that failed the step).
        if (!hasKept || kept != lane.Count)
        {
            Directory.CreateDirectory(ProjectDir(project));
            await WithRetryAsync(() =>
            {
                File.WriteAllText(countPath, lane.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return true;
            });
        }
        var rows = new List<StringEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, cancellationToken);
            rows.AddRange(page);
            if (page.Count < 1000) break;
        }

        var scopes = await db.GetStringTranslationContextsByIdsAsync(rows.Select(r => r.Id).ToList(), cancellationToken);
        // A dialogue, and every other row with the same English: a saved row (edited, so in the project TM) fills
        // the rows with its English, which in another lane would change that lane's request under its pending answer.
        // Dialogue lines are not filled from the TM, and "Yes." would join half the dialogues into one lane.
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string RootOf(string x)
        {
            while (parent.TryGetValue(x, out var up) && up != x) x = parent[x] = parent.GetValueOrDefault(up, up);
            return x;
        }

        void Join(string a, string b)
        {
            parent.TryAdd(a, a);
            parent.TryAdd(b, b);
            var (ra, rb) = (RootOf(a), RootOf(b));
            if (ra != rb) parent[rb] = ra;
        }

        string ScopeOf(StringEntry r) => scopes.TryGetValue(r.Id, out var c) && c.DialogueScope is { Length: > 0 } scope ? scope : "row:" + r.Id;
        foreach (var r in rows)
        {
            var scope = ScopeOf(r);
            if (scope.StartsWith("row:", StringComparison.Ordinal)) Join(scope, "text:" + XTranslatorAi.Core.Text.TranslationMemoryKey.NormalizeSource(r.SourceText ?? ""));
            else parent.TryAdd(scope, scope);
        }

        var groups = rows.GroupBy(r => RootOf(ScopeOf(r)))
            .OrderBy(g => g.Min(r => r.OrderIndex))
            .ToList();
        var sizes = new long[lane.Count];
        var mine = new HashSet<long>();
        foreach (var group in groups)
        {
            var target = Array.IndexOf(sizes, sizes.Min());
            sizes[target] += group.Sum(r => (long)(r.SourceText?.Length ?? 0)) + 1;
            if (target == lane.Index - 1) mine.UnionWith(group.Select(r => r.Id));
        }

        return mine;
    }

    private AgentProject? TryRead(string key)
    {
        var path = Path.Combine(_workRoot, key, "project.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<AgentProject>(File.ReadAllText(path)) : null;
    }

    // A small shared file another lane's process may hold for a moment.
    private static async Task<T> WithRetryAsync<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(50 * attempt);
            }
        }
    }

    private void ClearPending(AgentProject project)
    {
        File.Delete(PendingPath(project));
        File.Delete(RequestPath(project));
    }

    private string ProjectDir(AgentProject project) => Path.Combine(_workRoot, project.Key);
    private string LaneDir(AgentProject project) => Path.Combine(_workRoot, project.WorkFolder);
    private string PendingPath(AgentProject project) => Path.Combine(LaneDir(project), "pending.json");
    private string AnswerPath(AgentProject project, string hash, int attempt) => Path.Combine(ProjectDir(project), "responses", $"{hash}.{attempt}.txt");

    private static string ProjectKey(string pluginPath, string dbPath)
    {
        var stem = new string(Path.GetFileNameWithoutExtension(pluginPath).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dbPath.ToLowerInvariant())))[..8].ToLowerInvariant();
        return $"{stem}-{hash}";
    }
}
