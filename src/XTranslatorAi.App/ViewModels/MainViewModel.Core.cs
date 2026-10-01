using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using XTranslatorAi.App.Collections;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels.Tabs;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Core.Xml;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel : ObservableObject, ITranslationRunnerStatusPort, ITranslationRunnerFlowControlPort, ITranslationRunnerFailoverPort, IStringsTabHost, ICompareTabHost, IProjectGlossaryTabHost, IGlobalGlossaryTabHost, IGlobalTranslationMemoryTabHost, IPromptTabHost, IProjectContextTabHost, IApiLogsTabHost, ILqaTabHost
{
    private readonly HttpClient _httpClient;
    private readonly GeminiClient _geminiClient;
    private readonly Dictionary<string, GeminiModel> _modelInfoByName = new(StringComparer.Ordinal);
    private readonly AppSettingsStore _appSettings;
    private bool _isUpdatingTranslationPreferences = true;
    private readonly ApiCallLogService _apiCallLogService;
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly IUiInteractionService _uiInteractionService;
    private readonly BundledFranchiseTmSeedService _bundledFranchiseTmSeedService;
    private readonly GlobalProjectDbService _globalProjectDbService;
    private readonly ProjectGlossaryService _projectGlossaryService;
    private readonly GlobalGlossaryService _globalGlossaryService;
    private readonly FranchiseTranslationMemoryService _globalTranslationMemoryService;
    private readonly ProjectWorkspaceService _projectWorkspaceService;
    private readonly TranslationRunnerService _translationRunnerService;
    private readonly CompareTranslationService _compareTranslationService;

    private readonly ProjectState _projectState = new();
    private readonly TranslationOperation _translationOperation = new();
    private long _rowUpdateGeneration;
    private bool _isSwitchingProject;
    private TaskCompletionSource<bool>? _resumeTcs;

    private readonly ConcurrentQueue<RowUpdate> _rowUpdates = new();
    private int _rowUpdatePumpScheduled;
    private DispatcherTimer? _rowUpdateTimer;
    private readonly HashSet<long> _inProgressSinceTranslationStart = new();

    public ObservableRangeCollection<StringEntryViewModel> Entries => _projectState.Entries;
    public ICollectionView EntriesView { get; }
    public ObservableRangeCollection<string> EntryStatusFilterValues { get; } = new();
    public ObservableRangeCollection<GlossaryEntryViewModel> Glossary { get; } = new();
    public ICollectionView GlossaryView { get; }
    public ObservableRangeCollection<string> GlossaryCategoryFilterValues { get; } = new();
    public ObservableRangeCollection<GlossaryEntryViewModel> GlobalGlossary { get; } = new();
    public ICollectionView GlobalGlossaryView { get; }
    public ObservableRangeCollection<string> GlobalGlossaryCategoryFilterValues { get; } = new();
    public ObservableRangeCollection<TranslationMemoryEntryViewModel> FranchiseTranslationMemory { get; } = new();
    public ICollectionView FranchiseTranslationMemoryView { get; }
    public ObservableRangeCollection<GlossaryLookupResultViewModel> GlossaryLookupResults { get; } = new();
    public ICollectionView GlossaryLookupResultsView { get; }
    public ObservableRangeCollection<ApiCallLogRow> ApiCallLogs => _apiCallLogService.Rows;
    public ObservableRangeCollection<LqaIssueViewModel> LqaIssues { get; } = new();
    public ICollectionView LqaIssuesView { get; }

    public ObservableRangeCollection<SavedApiKeyViewModel> SavedApiKeys { get; } = new();

    public ObservableRangeCollection<string> AvailableModels { get; } = new();

    public MainViewModel(HttpClient httpClient, MainViewModelServices services)
    {
        _httpClient = httpClient;
        _appSettings = services.AppSettings;
        _apiCallLogService = services.ApiCallLogService;
        _systemPromptBuilder = services.SystemPromptBuilder;
        _uiInteractionService = services.UiInteractionService;
        _bundledFranchiseTmSeedService = services.BundledFranchiseTmSeedService;
        _globalProjectDbService = services.GlobalProjectDbService;
        _projectGlossaryService = services.ProjectGlossaryService;
        _globalGlossaryService = services.GlobalGlossaryService;
        _globalTranslationMemoryService = services.FranchiseTranslationMemoryService;
        _projectWorkspaceService = services.ProjectWorkspaceService;
        _translationRunnerService = services.TranslationRunnerService;
        _compareTranslationService = services.CompareTranslationService;

        // Long book texts on some models (e.g., Gemini 3 preview) can take several minutes per request.
        // Use a higher client timeout and rely on cancellation tokens for user-initiated Stop.
        _httpClient.Timeout = TimeSpan.FromMinutes(15);
        _geminiClient = new GeminiClient(_httpClient, new UiGeminiCallLogger(this));

        // Model availability varies by API key; use Refresh to populate the real list.
        AvailableModels.ReplaceAll(GeminiModelCatalog.PreferredModels);
        BasePromptText = EmbeddedAssets.LoadMetaPrompt(SelectedFranchise);

        var settings = _appSettings.Load();
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            ApiKey = settings.ApiKey.Trim();
            HasSavedApiKey = true;
        }
        LoadSavedApiKeys(settings);
        EnableApiKeyFailover = settings.EnableApiKeyFailover;
        EnableBookFullModelOverride = settings.EnableBookFullModelOverride;
        EnableBookBodyModelOverride = settings.EnableBookBodyModelOverride;
        if (!string.IsNullOrWhiteSpace(settings.BookFullModel))
        {
            BookFullModel = settings.BookFullModel.Trim();
        }
        EnableQualityEscalation = settings.EnableQualityEscalation;
        if (!string.IsNullOrWhiteSpace(settings.QualityEscalationModel))
        {
            QualityEscalationModel = settings.QualityEscalationModel.Trim();
        }
        EnablePromptCache = settings.EnablePromptCache;
        EnableRiskyCandidateRerank = settings.EnableRiskyCandidateRerank;
        RiskyCandidateCount = Math.Clamp(settings.RiskyCandidateCount, 2, 8);
        BatchSize = settings.BatchSize;
        MaxCharsPerBatch = settings.MaxCharsPerBatch;
        MaxParallelRequests = settings.MaxParallelRequests;
        MaxOutputTokensOverride = settings.MaxOutputTokensOverride;
        EnableRepairPass = settings.EnableRepairPass;
        SemanticRepairMode = settings.SemanticRepairMode;
        KeepSkyrimTagsRaw = settings.KeepSkyrimTagsRaw;
        EnableDialogueContextWindow = settings.EnableDialogueContextWindow;
        EnableSessionTermMemory = settings.EnableSessionTermMemory;
        UseRecStyleHints = settings.UseRecStyleHints;
        EnableTemplateFixer = settings.EnableTemplateFixer;
        EnableProjectContext = settings.EnableProjectContext;
        EnableAdaptiveOutputBudget = settings.EnableAdaptiveOutputBudget;
        EnableBookContext = settings.EnableBookContext;
        MaxRetryGenerations = settings.MaxRetryGenerations;
        MaxTotalGenerations = settings.MaxTotalGenerations;
        PluginSourceLanguage = settings.PluginSourceLanguage;
        PluginTargetLanguage = settings.PluginTargetLanguage;
        PluginSourceEncoding = settings.PluginSourceEncoding;
        PluginMetadataEncoding = settings.PluginMetadataEncoding;
        PluginTargetEncoding = settings.PluginTargetEncoding;
        PluginStringsDirectory = settings.PluginStringsDirectory;
        if (!string.IsNullOrWhiteSpace(settings.SelectedModel))
        {
            if (!AvailableModels.Contains(settings.SelectedModel)) AvailableModels.Add(settings.SelectedModel);
            SelectedModel = settings.SelectedModel;
        }

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = EntryFilter;
        if (EntriesView is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            live.LiveFilteringProperties.Add(nameof(StringEntryViewModel.Edid));
            live.LiveFilteringProperties.Add(nameof(StringEntryViewModel.Rec));
            live.LiveFilteringProperties.Add(nameof(StringEntryViewModel.SourceText));
            live.LiveFilteringProperties.Add(nameof(StringEntryViewModel.DestText));
            live.LiveFilteringProperties.Add(nameof(StringEntryViewModel.Status));
            live.LiveFilteringProperties.Add(nameof(StringEntryViewModel.ErrorMessage));
            live.IsLiveFiltering = true;
        }
        EntryStatusFilterValues.ReplaceAll(
            new[]
            {
                EntryStatusAll,
                EntryStatusNeedsReview,
            }.Concat(StringEntryStatusLabels.FilterLabels())
        );

        GlossaryView = CollectionViewSource.GetDefaultView(Glossary);
        GlossaryView.Filter = GlossaryFilter;
        GlossaryCategoryFilterValues.ReplaceAll(new[] { GlossaryCategoryAll, GlossaryCategoryNone });

        GlobalGlossaryView = CollectionViewSource.GetDefaultView(GlobalGlossary);
        GlobalGlossaryView.Filter = GlobalGlossaryFilter;
        GlobalGlossaryCategoryFilterValues.ReplaceAll(new[] { GlossaryCategoryAll, GlossaryCategoryNone });

        FranchiseTranslationMemoryView = CollectionViewSource.GetDefaultView(FranchiseTranslationMemory);
        FranchiseTranslationMemoryView.Filter = FranchiseTranslationMemoryFilter;

        GlossaryLookupResultsView = CollectionViewSource.GetDefaultView(GlossaryLookupResults);

        LqaIssuesView = CollectionViewSource.GetDefaultView(LqaIssues);
        LqaIssuesView.Filter = LqaIssueFilter;

        StringsTab = new StringsTabViewModel(this);
        CompareTab = new CompareTabViewModel(this);
        LqaTab = new LqaTabViewModel(this);
        ProjectGlossaryTab = new ProjectGlossaryTabViewModel(this);
        GlobalGlossaryTab = new GlobalGlossaryTabViewModel(this);
        FranchiseTranslationMemoryTab = new GlobalTranslationMemoryTabViewModel(this);
        PromptTab = new PromptTabViewModel(this);
        ProjectContextTab = new ProjectContextTabViewModel(this);
        ApiLogsTab = new ApiLogsTabViewModel(this);

        RefreshPromptLint();
        _isUpdatingTranslationPreferences = false;
        PropertyChanged += SaveChangedTranslationPreference;
    }

    private void LoadSavedApiKeys(AppSettings settings)
    {
        SavedApiKeys.Clear();

        if (settings.ApiKeys is { Length: > 0 })
        {
            foreach (var k in settings.ApiKeys)
            {
                if (k == null || string.IsNullOrWhiteSpace(k.ApiKey))
                {
                    continue;
                }

                SavedApiKeys.Add(
                    new SavedApiKeyViewModel
                    {
                        Name = k.Name?.Trim() ?? "",
                        ApiKey = k.ApiKey.Trim(),
                    }
                );
            }
        }

        if (!string.IsNullOrWhiteSpace(ApiKey) && SavedApiKeys.Count > 0)
        {
            foreach (var k in SavedApiKeys)
            {
                if (string.Equals(k.ApiKey, ApiKey, StringComparison.Ordinal))
                {
                    SelectedSavedApiKey = k;
                    break;
                }
            }
        }

        if (SavedApiKeys.Count > 0)
        {
            HasSavedApiKey = true;
        }
    }

    public double ProgressRatio => TotalCount == 0 ? 0 : (double)DoneCount / TotalCount;

    partial void OnDoneCountChanged(int value) => OnPropertyChanged(nameof(ProgressRatio));
    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(ProgressRatio));

    partial void OnRiskyCandidateCountChanged(int value)
    {
        var clamped = Math.Clamp(value, 2, 8);
        if (clamped != value)
        {
            RiskyCandidateCount = clamped;
            return;
        }
    }

    partial void OnMaxRetryGenerationsChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value) MaxRetryGenerations = clamped;
    }

    partial void OnMaxTotalGenerationsChanged(int value)
    {
        if (value < 0) MaxTotalGenerations = 0;
    }

    partial void OnSelectedFranchiseChanged(BethesdaFranchise value)
    {
        if (_projectState.PluginDocument != null)
        {
            _globalProjectDbService.SelectedFranchise = BethesdaFranchise.ElderScrolls;
            BasePromptText = EmbeddedAssets.LoadMetaPrompt(BethesdaFranchise.ElderScrolls);
            if (value != BethesdaFranchise.ElderScrolls) SelectedFranchise = BethesdaFranchise.ElderScrolls;
            return;
        }
        _globalProjectDbService.SelectedFranchise = value;

        try
        {
            BasePromptText = EmbeddedAssets.LoadMetaPrompt(value);
        }
        catch
        {
            BasePromptText = EmbeddedAssets.LoadMetaPrompt(BethesdaFranchise.ElderScrolls);
        }

        if (!IsProjectLoaded)
        {
            return;
        }

        if (IsTranslating)
        {
            StatusMessage = "게임 시리즈를 바꿨습니다. 번역을 다시 시작하면 적용됩니다.";
            return;
        }

        _ = ReloadAfterFranchiseChangeAsync();
    }

    private async Task ReloadAfterFranchiseChangeAsync()
    {
        try
        {
            try
            {
                await SaveProjectInfoAsync();
            }
            catch
            {
                // best-effort
            }

            await ReloadGlobalGlossaryAsync();
            await ReloadFranchiseTranslationMemoryAsync();
            StatusMessage = "게임 시리즈를 바꿨습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("게임 시리즈 전환", ex);
        }
    }

    private void SaveChangedTranslationPreference(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(EnableApiKeyFailover)
            or nameof(EnableBookFullModelOverride)
            or nameof(EnableBookBodyModelOverride)
            or nameof(BookFullModel)
            or nameof(EnablePromptCache)
            or nameof(EnableQualityEscalation)
            or nameof(QualityEscalationModel)
            or nameof(EnableRiskyCandidateRerank)
            or nameof(RiskyCandidateCount)
            or nameof(SelectedModel)
            or nameof(BatchSize)
            or nameof(MaxCharsPerBatch)
            or nameof(MaxParallelRequests)
            or nameof(MaxOutputTokensOverride)
            or nameof(EnableRepairPass)
            or nameof(SemanticRepairMode)
            or nameof(KeepSkyrimTagsRaw)
            or nameof(EnableDialogueContextWindow)
            or nameof(EnableSessionTermMemory)
            or nameof(UseRecStyleHints)
            or nameof(EnableTemplateFixer)
            or nameof(EnableProjectContext)
            or nameof(EnableAdaptiveOutputBudget)
            or nameof(EnableBookContext)
            or nameof(MaxRetryGenerations)
            or nameof(MaxTotalGenerations)
            or nameof(PluginSourceLanguage)
            or nameof(PluginTargetLanguage)
            or nameof(PluginSourceEncoding)
            or nameof(PluginMetadataEncoding)
            or nameof(PluginTargetEncoding)
            or nameof(PluginStringsDirectory))
        {
            SaveTranslationPreferences();
        }
    }

    private void SaveTranslationPreferences()
    {
        if (_isUpdatingTranslationPreferences) return;
        try
        {
            var current = _appSettings.Load();
            _appSettings.Save(
                current with
                {
                    EnableApiKeyFailover = EnableApiKeyFailover,
                    EnableBookFullModelOverride = EnableBookFullModelOverride,
                    EnableBookBodyModelOverride = EnableBookBodyModelOverride,
                    BookFullModel = string.IsNullOrWhiteSpace(BookFullModel) ? null : BookFullModel.Trim(),
                    EnablePromptCache = EnablePromptCache,
                    EnableQualityEscalation = EnableQualityEscalation,
                    QualityEscalationModel = string.IsNullOrWhiteSpace(QualityEscalationModel) ? null : QualityEscalationModel.Trim(),
                    EnableRiskyCandidateRerank = EnableRiskyCandidateRerank,
                    RiskyCandidateCount = Math.Clamp(RiskyCandidateCount, 2, 8),
                    SelectedModel = SelectedModel,
                    BatchSize = BatchSize,
                    MaxCharsPerBatch = MaxCharsPerBatch,
                    MaxParallelRequests = MaxParallelRequests,
                    MaxOutputTokensOverride = MaxOutputTokensOverride,
                    EnableRepairPass = EnableRepairPass,
                    SemanticRepairMode = SemanticRepairMode,
                    KeepSkyrimTagsRaw = KeepSkyrimTagsRaw,
                    EnableDialogueContextWindow = EnableDialogueContextWindow,
                    EnableSessionTermMemory = EnableSessionTermMemory,
                    UseRecStyleHints = UseRecStyleHints,
                    EnableTemplateFixer = EnableTemplateFixer,
                    EnableProjectContext = EnableProjectContext,
                    EnableAdaptiveOutputBudget = EnableAdaptiveOutputBudget,
                    EnableBookContext = EnableBookContext,
                    MaxRetryGenerations = MaxRetryGenerations,
                    MaxTotalGenerations = MaxTotalGenerations,
                    PluginSourceLanguage = PluginSourceLanguage,
                    PluginTargetLanguage = PluginTargetLanguage,
                    PluginSourceEncoding = PluginSourceEncoding,
                    PluginMetadataEncoding = PluginMetadataEncoding,
                    PluginTargetEncoding = PluginTargetEncoding,
                    PluginStringsDirectory = PluginStringsDirectory,
                }
            );
        }
        catch
        {
            // ignore
        }
    }
}
