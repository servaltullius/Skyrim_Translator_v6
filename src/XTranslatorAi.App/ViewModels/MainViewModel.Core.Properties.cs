using System;
using CommunityToolkit.Mvvm.ComponentModel;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private const string GlossaryCategoryAll = "(전체)";
    private const string GlossaryCategoryNone = "(없음)";
    private const string EntryStatusAll = "(전체)";
    private const string EntryStatusNeedsReview = "(검토 필요)";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSelectedDestCommand))]
    private StringEntryViewModel? _selectedEntry;

    [ObservableProperty] private LqaIssueViewModel? _selectedLqaIssue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteGlossaryEntryCommand))]
    private GlossaryEntryViewModel? _selectedGlossaryEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteGlobalGlossaryEntryCommand))]
    private GlossaryEntryViewModel? _selectedGlobalGlossaryEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteFranchiseTranslationMemoryEntryCommand))]
    private TranslationMemoryEntryViewModel? _selectedFranchiseTranslationMemoryEntry;

    [ObservableProperty] private SavedApiKeyViewModel? _selectedSavedApiKey;
    [ObservableProperty] private string _savedApiKeyName = "";

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _hasSavedApiKey;
    [ObservableProperty] private bool _enableApiKeyFailover = true;
    [ObservableProperty] private string _selectedModel = GeminiModelCatalog.DefaultModel;
    [ObservableProperty] private BethesdaFranchise _selectedFranchise = BethesdaFranchise.ElderScrolls;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookModelOverrideEnabled))]
    private bool _enableBookFullModelOverride;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookModelOverrideEnabled))]
    private bool _enableBookBodyModelOverride;
    public bool IsBookModelOverrideEnabled => EnableBookFullModelOverride || EnableBookBodyModelOverride;
    [ObservableProperty] private string _bookFullModel = GeminiModelCatalog.DefaultModel;
    [ObservableProperty] private bool _enableQualityEscalation;
    [ObservableProperty] private string _qualityEscalationModel = GeminiModelCatalog.DefaultModel;
    [ObservableProperty] private string _sourceLang = "english";
    [ObservableProperty] private string _targetLang = "korean";

    [ObservableProperty] private int _batchSize = 12;
    [ObservableProperty] private int _maxCharsPerBatch = 15000;
    [ObservableProperty] private int _maxParallelRequests = 2;
    [ObservableProperty] private int _maxOutputTokensOverride;
    [ObservableProperty] private int _maxRetryGenerations = 8;
    [ObservableProperty] private int _maxTotalGenerations;
    [ObservableProperty] private bool _enableAdaptiveOutputBudget;
    [ObservableProperty] private bool _enableBookContext;

    [ObservableProperty] private string _entryFilterText = "";
    [ObservableProperty] private bool _entryFilterTagsOnly;
    [ObservableProperty] private bool _entryFilterTagMismatchOnly;
    [ObservableProperty] private string _entryFilterStatus = EntryStatusAll;

    [ObservableProperty] private string _lqaFilterText = "";

    [ObservableProperty] private string _glossaryLookupText = "";
    [ObservableProperty] private bool _glossaryLookupIncludeProject = true;
    [ObservableProperty] private bool _glossaryLookupIncludeGlobal = true;

    [ObservableProperty] private string _basePromptText = "";
    [ObservableProperty] private string _customPromptText = "";
    [ObservableProperty] private bool _useCustomPrompt;
    [ObservableProperty] private bool _hasPromptLintIssues;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTranslationCommand))]
    private bool _hasPromptLintBlockingIssues;
    [ObservableProperty] private string _promptLintSummary = "Prompt lint: no obvious conflicts detected.";
    [ObservableProperty] private string _promptLintDetails = "";

    [ObservableProperty] private bool _useRecStyleHints = true;
    [ObservableProperty] private bool _enableDialogueContextWindow = true;
    [ObservableProperty] private bool _enableRepairPass = true;
    [ObservableProperty] private PlaceholderSemanticRepairMode _semanticRepairMode = PlaceholderSemanticRepairMode.Soft;
    [ObservableProperty] private bool _enableTemplateFixer;
    [ObservableProperty] private bool _keepSkyrimTagsRaw = true;
    [ObservableProperty] private bool _enableSessionTermMemory = true;
    [ObservableProperty] private bool _enablePromptCache = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRiskyCandidateCountEnabled))]
    [NotifyPropertyChangedFor(nameof(EffectiveRiskyCandidateCount))]
    [NotifyPropertyChangedFor(nameof(EffectiveRiskyCandidateSummary))]
    private bool _enableRiskyCandidateRerank = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveRiskyCandidateCount))]
    [NotifyPropertyChangedFor(nameof(EffectiveRiskyCandidateSummary))]
    private int _riskyCandidateCount = 3;
    [ObservableProperty] private bool _enableProjectContext = true;
    [ObservableProperty] private string _projectContextPreview = "";

    [ObservableProperty] private string _lastCostEstimateSummary = "";
    [ObservableProperty] private bool _enableApiCallLogging = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportXmlCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportPluginCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartTranslationCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveGlossaryChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGlossaryEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportGlobalGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveGlobalGlossaryChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGlobalGlossaryEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportGlobalGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(EstimateCostCommand))]
    [NotifyCanExecuteChangedFor(nameof(FixMagDurPlaceholdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReapplyPostEditsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSelectedDestCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateProjectContextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectContextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearProjectContextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanLqaCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearLqaCommand))]
    private bool _isProjectLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportXmlCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportPluginCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartTranslationCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTranslationCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseTranslationCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveGlossaryChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGlossaryEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportGlobalGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveGlobalGlossaryChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGlobalGlossaryEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportGlobalGlossaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(EstimateCostCommand))]
    [NotifyCanExecuteChangedFor(nameof(FixMagDurPlaceholdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReapplyPostEditsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportFranchiseTranslationMemoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadFranchiseTranslationMemoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFranchiseTranslationMemoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveFranchiseTranslationMemoryChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteFranchiseTranslationMemoryEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportFranchiseTranslationMemoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportFranchiseTranslationMemoryFromTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSelectedDestCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateProjectContextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectContextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearProjectContextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanLqaCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearLqaCommand))]
    private bool _isTranslating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanLqaCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearLqaCommand))]
    private bool _isLqaScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseTranslationCommand))]
    private bool _isPaused;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _doneCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private string _statusMessage = "준비됨.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGlossaryCommand))]
    private string _glossarySourceTerm = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGlossaryCommand))]
    private string _glossaryTargetTerm = "";

    [ObservableProperty] private string _glossaryCategory = "";
    [ObservableProperty] private GlossaryMatchMode _glossaryMatchMode = GlossaryMatchMode.WordBoundary;
    [ObservableProperty] private GlossaryForceMode _glossaryForceMode = GlossaryForceMode.ForceToken;
    [ObservableProperty] private int _glossaryPriority = 10;

    [ObservableProperty] private string _glossaryFilterText = "";
    [ObservableProperty] private string _glossaryFilterCategory = GlossaryCategoryAll;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalGlossaryCommand))]
    private string _globalGlossarySourceTerm = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalGlossaryCommand))]
    private string _globalGlossaryTargetTerm = "";

    [ObservableProperty] private string _globalGlossaryCategory = "";
    [ObservableProperty] private GlossaryMatchMode _globalGlossaryMatchMode = GlossaryMatchMode.WordBoundary;
    [ObservableProperty] private GlossaryForceMode _globalGlossaryForceMode = GlossaryForceMode.ForceToken;
    [ObservableProperty] private int _globalGlossaryPriority = 10;

    [ObservableProperty] private string _globalGlossaryFilterText = "";
    [ObservableProperty] private string _globalGlossaryFilterCategory = GlossaryCategoryAll;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFranchiseTranslationMemoryCommand))]
    private string _franchiseTranslationMemorySourceText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFranchiseTranslationMemoryCommand))]
    private string _franchiseTranslationMemoryDestText = "";

    [ObservableProperty] private string _franchiseTranslationMemoryFilterText = "";

    public Array GlossaryMatchModeValues => Enum.GetValues(typeof(GlossaryMatchMode));
    public Array GlossaryForceModeValues => Enum.GetValues(typeof(GlossaryForceMode));
    public Array SemanticRepairModeValues => Enum.GetValues(typeof(PlaceholderSemanticRepairMode));

    public FranchiseOption[] FranchiseOptions { get; } =
    {
        new(BethesdaFranchise.ElderScrolls, "Elder Scrolls"),
        new(BethesdaFranchise.Fallout, "Fallout"),
        new(BethesdaFranchise.Starfield, "Starfield"),
    };

    public sealed record FranchiseOption(BethesdaFranchise Value, string DisplayName);
}
