using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// The model client of an agent-mode run: a generation call it has an answer for (by hash and attempt) gets that
/// answer; the first one it has none for is recorded and the run is stopped by cancellation. An exception would make
/// the pipeline split the batch and ask again for each half; a cancellation goes straight up. Nothing here reaches a
/// network: the answers are the agent app's model's, written to files.
/// </summary>
public sealed class AgentRelayClient : IGeminiClient
{
    private readonly Func<string, int, string?> _answer;
    private readonly CancellationTokenSource _stop;
    private readonly Dictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public AgentRelayClient(Func<string, int, string?> answer, CancellationTokenSource stop)
    {
        _answer = answer;
        _stop = stop;
    }

    /// <summary>The call the run stopped at, or null when it finished.</summary>
    public AgentRequest? Recorded { get; private set; }

    /// <summary>The answers the run used: (hash, attempt).</summary>
    public HashSet<(string Hash, int Attempt)> Used { get; } = new();

    public int GenerationCalls { get; private set; }

    public Task<string> GenerateContentAsync(string apiKey, string modelName, GeminiGenerateContentRequest request,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            GenerationCalls++;
            var call = AgentRequest.From(modelName, request);
            var attempt = _attempts.GetValueOrDefault(call.Hash);
            _attempts[call.Hash] = attempt + 1;
            if (Recorded == null && _answer(call.Hash, attempt) is { } text)
            {
                Used.Add((call.Hash, attempt));
                return Task.FromResult(text);
            }

            Recorded ??= call with { Attempt = attempt };
        }

        _stop.Cancel();
        throw new OperationCanceledException(_stop.Token);
    }

    public async Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(string apiKey, string modelName,
        GeminiGenerateContentRequest request, CancellationToken cancellationToken)
        => new[] { await GenerateContentAsync(apiKey, modelName, request, cancellationToken) };

    // Batch sizes come from token counts; a fixed estimate keeps them the same between recording and replay.
    public Task<int> CountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
        => Task.FromResult(Math.Max(1, (text ?? "").Length / 3));

    public Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GeminiModel>>(Array.Empty<GeminiModel>());

    public Task<string> CreateCachedContentAsync(string apiKey, string modelName, string systemInstructionText, TimeSpan ttl,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("에이전트 모드는 프롬프트 캐시를 쓰지 않습니다.");

    public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken) => Task.CompletedTask;
}
