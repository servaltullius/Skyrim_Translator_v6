namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private const int DefaultSessionTermMemoryMaxTerms = 200;
    private const int DefaultSessionTermSeedCount = 20;
    private const int MaxSessionTermPairsPerRequest = 60;
    private static readonly bool EnableSessionTermAutoGlossaryPersistence = true;
}

