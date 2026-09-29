using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private readonly IProjectDb _db;
    private readonly IGeminiClient _gemini;
    internal TranslationRunContext? _ctx;
    private int _runActive;

    private TranslationRunContext Ctx
        => _ctx ?? throw new InvalidOperationException("Not in a translation run. TranslateIdsAsync must be called first.");

    public TranslationService(IProjectDb db, IGeminiClient gemini)
    {
        _db = db;
        _gemini = gemini;
    }

    /// @critical: Core translation pipeline entrypoint.
    public async Task TranslateIdsAsync(TranslateIdsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Interlocked.CompareExchange(ref _runActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("A translation run is already active on this service.");
        }

        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            await TranslateIdsCoreAsync(request);
        }
        finally
        {
            Volatile.Write(ref _runActive, 0);
        }
    }
}
