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
    string TargetEncoding, string ModelProfile, int BatchSize, int MaxChars);

/// <summary>
/// What a step did: rows saved, the lines of the answered request it did not save and why, the next request (null
/// when the plugin is done, with the size of its request.md) and why it stopped.
/// </summary>
public sealed record AgentStepReport(AgentProject Project, int PendingBefore, int PendingAfter, int ErrorRows,
    AgentRequest? Next, string? NextRequestPath, string? Problem)
{
    public int Saved => Math.Max(0, PendingBefore - PendingAfter);
    public bool Finished => Next == null && Problem == null && PendingAfter == 0;
    public IReadOnlyList<AgentRejection> Rejected { get; init; } = Array.Empty<AgentRejection>();
    public int NextRequestChars { get; init; }
    public IReadOnlySet<string> NewInstructionSections { get; init; } = new HashSet<string>();
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

    public AgentSession(string workRoot, GlobalProjectDbService globalDb, GameStringService? gameStrings, AppSettings settings,
        string? projectsRootOverride = null)
    {
        _workRoot = Path.GetFullPath(workRoot);
        _globalDb = globalDb;
        _gameStrings = gameStrings;
        _settings = settings;
        _projectsRootOverride = projectsRootOverride;
    }

    public async Task<(AgentProject Project, int Rows, int Pending)> OpenAsync(string pluginPath, PluginReadOptions readOptions,
        string targetLanguage, string targetEncoding, int batchSize, CancellationToken cancellationToken)
    {
        var workspace = new ProjectWorkspaceService(_globalDb, _projectsRootOverride, _gameStrings);
        var loaded = await workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(
            Path.GetFullPath(pluginPath), readOptions, targetLanguage, targetEncoding, AgentTranslationRun.DefaultModelProfile,
            _settings.CustomPromptText ?? "", _settings.UseCustomPrompt), cancellationToken);
        await using var db = loaded.Db;
        var pending = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, cancellationToken);
        var key = ProjectKey(loaded.Document.Info.InputPath, db.DbPath);
        var project = new AgentProject(key, loaded.Document.Info.InputPath, db.DbPath, loaded.Document.Info.Options, targetEncoding,
            AgentTranslationRun.DefaultModelProfile, Math.Clamp(batchSize, 1, 100), Math.Clamp(_settings.MaxCharsPerBatch, 1000, 50000));
        var existing = TryRead(key);
        if (existing != null && File.Exists(PendingPath(existing)))
        {
            // The plugin was opened again with a request still out: its rows may have changed under it.
            ClearPending(existing);
        }

        Directory.CreateDirectory(ProjectDir(project));
        await File.WriteAllTextAsync(Path.Combine(ProjectDir(project), "project.json"), JsonSerializer.Serialize(project, Json), cancellationToken);
        return (project, loaded.Entries.Count, pending.Count);
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

    public string RequestPath(AgentProject project) => Path.Combine(ProjectDir(project), "request.md");

    public async Task<(int Rows, int Pending, int Errors, int Edited)> StatusAsync(AgentProject project, CancellationToken cancellationToken)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, cancellationToken);
        var all = await db.GetStringIdsByStatusAsync(Enum.GetValues<StringEntryStatus>(), cancellationToken);
        var pending = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error, StringEntryStatus.InProgress }, cancellationToken);
        var errors = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Error }, cancellationToken);
        var edited = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Edited }, cancellationToken);
        return (all.Count, pending.Count, errors.Count, edited.Count);
    }

    /// <summary>Starts (or, after a break, restarts) the run and writes its first request.</summary>
    public async Task<AgentStepReport> NextAsync(AgentProject project, CancellationToken cancellationToken)
    {
        if (Pending(project) != null)
        {
            throw new AgentModeException($"대기 중인 요청이 있습니다({RequestPath(project)}). 먼저 그 결과를 submit하세요.");
        }

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
        var pending = Pending(project) ?? throw new AgentModeException("대기 중인 요청이 없습니다. next로 요청을 만드세요.");
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
        ClearPending(project);
        var (report, used) = await StepAsync(project, cancellationToken);
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
        var options = new AgentTranslationRun.Options(project.ModelProfile, project.BatchSize, project.MaxChars);
        string? Answer(string hash, int attempt) => File.Exists(AnswerPath(project, hash, attempt)) ? File.ReadAllText(AnswerPath(project, hash, attempt)) : null;
        var outcome = await AgentTranslationRun.RunAsync(db, _settings, options, Answer, _globalDb, gameStrings, cancellationToken);
        var pendingBefore = outcome.PendingBefore;
        var used = new HashSet<(string Hash, int Attempt)>(outcome.Used);
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
        }

        outcome = outcome with { PendingBefore = pendingBefore, Used = used };
        string? problem = outcome.Error == null ? null : "오류: " + outcome.Error.Message;
        if (outcome.Next is { Attempt: > 0 } again)
        {
            problem ??= $"같은 요청을 다시 받았습니다({again.Attempt + 1}번째). 앞선 답을 번역기가 받아들이지 않았습니다. request.md의 '앞 답이 걸린 이유'를 보고 답하세요.";
        }

        return (new AgentStepReport(project, outcome.PendingBefore, outcome.PendingAfter, outcome.ErrorRows, outcome.Next,
            outcome.Next == null ? null : RequestPath(project), problem), outcome.Used);
    }

    /// <summary>Writes the report's next request (pending.json, instructions.md, request.md), if it has one.</summary>
    private async Task<AgentStepReport> WriteNextAsync(AgentStepReport report, IReadOnlyList<AgentRejection> rejected,
        CancellationToken cancellationToken)
    {
        if (report.Next is not { } request) return report;
        var dir = ProjectDir(report.Project);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(PendingPath(report.Project), JsonSerializer.Serialize(request, Json), cancellationToken);
        var added = AgentRequestRenderer.WriteInstructions(dir, AgentRequestRenderer.Split(request).Sections);
        var text = AgentRequestRenderer.Render(report.Project, request, added, rejected);
        await File.WriteAllTextAsync(RequestPath(report.Project), text, cancellationToken);
        return report with { NextRequestChars = text.Length, NewInstructionSections = added };
    }

    public string InstructionsPath(AgentProject project) => Path.Combine(ProjectDir(project), AgentRequestRenderer.InstructionsFileName);

    private AgentProject? TryRead(string key)
    {
        var path = Path.Combine(_workRoot, key, "project.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<AgentProject>(File.ReadAllText(path)) : null;
    }

    private void ClearPending(AgentProject project)
    {
        File.Delete(PendingPath(project));
        File.Delete(RequestPath(project));
    }

    private string ProjectDir(AgentProject project) => Path.Combine(_workRoot, project.Key);
    private string PendingPath(AgentProject project) => Path.Combine(ProjectDir(project), "pending.json");
    private string AnswerPath(AgentProject project, string hash, int attempt) => Path.Combine(ProjectDir(project), "responses", $"{hash}.{attempt}.txt");

    private static string ProjectKey(string pluginPath, string dbPath)
    {
        var stem = new string(Path.GetFileNameWithoutExtension(pluginPath).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dbPath.ToLowerInvariant())))[..8].ToLowerInvariant();
        return $"{stem}-{hash}";
    }
}
