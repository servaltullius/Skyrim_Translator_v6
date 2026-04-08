using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services;

public sealed record MainViewModelServices(
    AppSettingsStore AppSettings,
    ApiCallLogService ApiCallLogService,
    SystemPromptBuilder SystemPromptBuilder,
    IUiInteractionService UiInteractionService,
    GlobalProjectDbService GlobalProjectDbService,
    ProjectGlossaryService ProjectGlossaryService,
    GlobalGlossaryService GlobalGlossaryService,
    FranchiseTranslationMemoryService FranchiseTranslationMemoryService,
    BundledFranchiseTmSeedService BundledFranchiseTmSeedService,
    ProjectWorkspaceService ProjectWorkspaceService,
    TranslationRunnerService TranslationRunnerService,
    CompareTranslationService CompareTranslationService
);
