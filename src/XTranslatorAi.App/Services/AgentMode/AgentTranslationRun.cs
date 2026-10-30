using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// One agent-mode step: the GUI's translation run (TranslationRunnerService) over every remaining row, with
/// <see cref="AgentRelayClient"/> as its model. The rows are always all the remaining ones, as the GUI's run takes
/// them: the dialogue context window and the TM ambiguity check are built from the request's own rows, so a request
/// for one batch would lose the lines around it. What would make a recording and its replay differ is fixed or off:
/// one request at a time, no prompt cache, no candidate rerank, no quality escalation, no session terms, one model
/// profile.
/// </summary>
public static class AgentTranslationRun
{
    /// <summary>The model name the runs use: it picks batch and output budgets, and is part of each request's hash.</summary>
    public const string DefaultModelProfile = "agent-app";

    public sealed record Options(string ModelProfile, int BatchSize, int MaxChars, int MaxOutputTokens = 65536)
    {
        /// <summary>The rows of the lane the run translates (AgentLane), or null for all.</summary>
        public IReadOnlySet<long>? Lane { get; init; }
    }

    public sealed record Outcome(AgentRequest? Next, IReadOnlySet<(string Hash, int Attempt)> Used, int PendingBefore,
        int PendingAfter, int ErrorRows, int GenerationCalls, Exception? Error)
    {
        /// <summary>What of the global DB the run could not read and went without (TranslationRunnerService.Result).</summary>
        public IReadOnlyList<string> MissingGlobalData { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The rows the run's requests ask once for: each row a request asks, and the rows given its translation (the same
        /// text and context; TranslateIdsRequest.OnDuplicateRows). A hold takes them together.
        /// </summary>
        public IReadOnlyDictionary<long, IReadOnlyList<long>> Grouped { get; init; } = new Dictionary<long, IReadOnlyList<long>>();
    }

    public static async Task<Outcome> RunAsync(ProjectDb db, AppSettings settings, Options options,
        Func<string, int, string?> answer, GlobalProjectDbService globalDb, GameStringDictionary? gameStrings,
        CancellationToken cancellationToken)
    {
        await ResetInProgressAsync(db, options.Lane, cancellationToken);
        var pending = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, cancellationToken);
        if (options.Lane is { } lane)
        {
            pending = pending.Where(lane.Contains).ToList();
        }

        if (pending.Count == 0)
        {
            return new Outcome(null, new HashSet<(string, int)>(), 0, 0, 0, 0, null);
        }

        var project = await db.TryGetProjectAsync(cancellationToken)
                      ?? throw new InvalidOperationException("프로젝트 정보가 없습니다. open을 다시 실행하세요.");
        var recById = new Dictionary<long, string?>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, cancellationToken);
            foreach (var row in page) recById[row.Id] = row.Rec;
            if (page.Count < 1000) break;
        }

        // The agent saves the project's context itself (context): a saved one is used whatever the GUI's
        // "생성한 프로젝트 문맥 사용" box says, and context --clear is how it stops.
        var context = (await db.TryGetProjectContextAsync(cancellationToken))?.ContextText;
        var systemPrompt = new SystemPromptBuilder().Build(EmbeddedAssets.LoadMetaPrompt(BethesdaFranchise.ElderScrolls), settings, context);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = new AgentRelayClient(answer, stop);
        var host = new QuietHost();
        var grouped = new Dictionary<long, IReadOnlyList<long>>();
        var request = TranslationRunRequests.Create(
            new TranslationRunRequests.Ports(db, client, host, host, host, stop.Token),
            TranslationRunRequests.ForAgent(settings, options.BatchSize, options.MaxChars),
            new TranslationRunRequests.Model(options.ModelProfile, IsBookFullRec: _ => false, BookFullModelName: model => model,
                MaxOutputTokens: _ => options.MaxOutputTokens),
            pending, id => recById.GetValueOrDefault(id), systemPrompt, BethesdaFranchise.ElderScrolls, project.SourceLang, project.DestLang,
            gameStrings, nameMemoryRows: options.Lane,
            onDuplicateRows: rows => { foreach (var (asked, given) in rows) grouped[asked] = given; });

        var result = await new TranslationRunnerService(globalDb).RunAsync(request);
        // A recording stops the run mid-batch; its rows are 대기 again, as after the GUI's stop button.
        await ResetInProgressAsync(db, options.Lane, cancellationToken);
        var after = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, cancellationToken);
        var errors = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Error }, cancellationToken);
        if (options.Lane is { } own)
        {
            after = after.Where(own.Contains).ToList();
            errors = errors.Where(own.Contains).ToList();
        }

        return new Outcome(client.Recorded, client.Used, pending.Count, after.Count, errors.Count, client.GenerationCalls,
            result.Error ?? host.Error) { MissingGlobalData = result.MissingGlobalData ?? Array.Empty<string>(), Grouped = grouped };
    }

    /// <summary>
    /// Puts the run's 번역 중 rows back to 대기. Another lane's run works on its own rows at the same time, in its own
    /// process: resetting every row put that lane's batch back to 대기 mid-run, its split dropped them, and its
    /// answer no longer fit the request the run made again. A lane resets only its own rows.
    /// </summary>
    private static async Task ResetInProgressAsync(ProjectDb db, IReadOnlySet<long>? lane, CancellationToken cancellationToken)
    {
        if (lane == null)
        {
            await db.ResetInProgressToPendingAsync(cancellationToken);
            return;
        }

        var statuses = await db.GetStringStatusesByIdsAsync(lane.ToList(), cancellationToken);
        var stuck = statuses.Where(s => s.Value == StringEntryStatus.InProgress).Select(s => s.Key).ToList();
        if (stuck.Count > 0)
        {
            await db.UpdateStringStatusesAsync(stuck, StringEntryStatus.Pending, null, cancellationToken);
        }
    }

    private sealed class QuietHost : ITranslationRunnerStatusPort, ITranslationRunnerFlowControlPort, ITranslationRunnerFailoverPort
    {
        public Exception? Error { get; private set; }
        public Task DispatchAsync(Action action) { action(); return Task.CompletedTask; }
        public void SetStatusMessage(string message) { }
        public void SetUserFacingError(string operation, Exception ex) => Error ??= ex;
        public Task OnRowUpdatedAsync(long id, StringEntryStatus status, string text) => Task.CompletedTask;
        public Task WaitIfPausedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public string ApiKey { get; set; } = "agent-app";
        public bool EnableApiKeyFailover => false;
        public IReadOnlyList<TranslationRunnerSavedApiKey> SavedApiKeys => Array.Empty<TranslationRunnerSavedApiKey>();
        public void SelectSavedApiKey(TranslationRunnerSavedApiKey savedKey) { }
    }
}
