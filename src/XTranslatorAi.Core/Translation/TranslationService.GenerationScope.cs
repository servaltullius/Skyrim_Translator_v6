using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using XTranslatorAi.Core.Diagnostics;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private sealed record GenerationScope(IReadOnlyList<long> RowIds, bool Recovery);
    private readonly AsyncLocal<GenerationScope?> _generationScope = new();

    private IDisposable EnterGenerationScope(IEnumerable<long>? ids = null, bool recovery = false)
    {
        var previous = _generationScope.Value;
        var rowIds = ids?.ToArray() ?? previous?.RowIds ?? Array.Empty<long>();
        _generationScope.Value = new GenerationScope(rowIds,
            recovery || previous?.Recovery == true || (previous == null && Ctx.GenerationBudget?.HasAttemptedAny(rowIds) == true));
        return new ScopeRestore(() => _generationScope.Value = previous);
    }

    private sealed class ScopeRestore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private void ConsumeGenerationBudget()
    {
        var scope = _generationScope.Value;
        Ctx.GenerationBudget?.Consume(scope?.RowIds ?? Array.Empty<long>(), scope?.Recovery == true);
    }

    private static bool IsRunGenerationLimit(Exception ex)
        => ExceptionTraversal.Enumerate(ex).Any(e => e is TranslationGenerationLimitException { IsRunLimit: true });

    // Transport/auth/quota/model errors are not fixed by translating smaller pieces.
    // Their bounded transport retries have already run before reaching recovery.
    private static bool MustStopRecovery(Exception ex)
        => ExceptionTraversal.Enumerate(ex).Any(e => e is TranslationGenerationLimitException
            or GeminiHttpException or HttpRequestException);
}
