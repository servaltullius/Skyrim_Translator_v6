using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services;

public sealed record MainViewModelServices(
    AppSettingsStore AppSettings,
    ApiCallLogService ApiCallLogService,
    SystemPromptBuilder SystemPromptBuilder,
    IUiInteractionService UiInteractionService,
    BundledFranchiseTmSeedService BundledFranchiseTmSeedService,
    GlobalProjectDbService GlobalProjectDbService,
    ProjectGlossaryService ProjectGlossaryService,
    GlobalGlossaryService GlobalGlossaryService,
    FranchiseTranslationMemoryService FranchiseTranslationMemoryService,
    ProjectWorkspaceService ProjectWorkspaceService,
    TranslationRunnerService TranslationRunnerService,
    CompareTranslationService CompareTranslationService,
    // Null in tests: the player's game translation stays off instead of reading the real global folder.
    GameStringService? GameStringService = null
);
