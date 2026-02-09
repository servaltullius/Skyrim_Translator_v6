using System.Collections.Concurrent;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private const int DefaultSessionTermMemoryMaxTerms = 200;
    private const int DefaultSessionTermSeedCount = 20;
    private const int MaxSessionTermPairsPerRequest = 60;
    private static readonly bool EnableSessionTermAutoGlossaryPersistence = false;

    private SessionTermMemory? _sessionTermMemory;
    private bool _enableSessionTermMemory;
    private ConcurrentQueue<(string Source, string Target)>? _pendingSessionAutoGlossaryInserts;
    private ConcurrentDictionary<string, byte>? _sessionAutoGlossaryKnownKeys;
}

