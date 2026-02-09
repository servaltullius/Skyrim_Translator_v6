using System;
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
    private readonly ApiCallLogService _apiCallLogService;
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly IUiInteractionService _uiInteractionService;
    private readonly GlobalProjectDbService _globalProjectDbService;
    private readonly ProjectGlossaryService _projectGlossaryService;
    private readonly GlobalGlossaryService _globalGlossaryService;
    private readonly GlobalTranslationMemoryService _globalTranslationMemoryService;
    private readonly ProjectWorkspaceService _projectWorkspaceService;
    private readonly TranslationRunnerService _translationRunnerService;
    private readonly CompareTranslationService _compareTranslationService;

    private readonly ProjectState _projectState = new();
    private CancellationTokenSource? _translationCts;
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
    public ObservableRangeCollection<TranslationMemoryEntryViewModel> GlobalTranslationMemory { get; } = new();
    public ICollectionView GlobalTranslationMemoryView { get; }
    public ObservableRangeCollection<GlossaryLookupResultViewModel> GlossaryLookupResults { get; } = new();
    public ICollectionView GlossaryLookupResultsView { get; }
    public ObservableRangeCollection<ApiCallLogRow> ApiCallLogs => _apiCallLogService.Rows;
    public ObservableRangeCollection<LqaIssueViewModel> LqaIssues { get; } = new();
    public ICollectionView LqaIssuesView { get; }

    public ObservableRangeCollection<SavedApiKeyViewModel> SavedApiKeys { get; } = new();

    public ObservableRangeCollection<string> AvailableModels { get; } = new();

    public MainViewModel(
        HttpClient httpClient,
        AppSettingsStore appSettings,
        ApiCallLogService apiCallLogService,
        SystemPromptBuilder systemPromptBuilder,
        IUiInteractionService uiInteractionService,
        GlobalProjectDbService globalProjectDbService,
        ProjectGlossaryService projectGlossaryService,
        GlobalGlossaryService globalGlossaryService,
        GlobalTranslationMemoryService globalTranslationMemoryService,
        ProjectWorkspaceService projectWorkspaceService,
        TranslationRunnerService translationRunnerService,
        CompareTranslationService compareTranslationService
    )
    {
        _httpClient = httpClient;
        _appSettings = appSettings;
        _apiCallLogService = apiCallLogService;
        _systemPromptBuilder = systemPromptBuilder;
        _uiInteractionService = uiInteractionService;
        _globalProjectDbService = globalProjectDbService;
        _projectGlossaryService = projectGlossaryService;
        _globalGlossaryService = globalGlossaryService;
        _globalTranslationMemoryService = globalTranslationMemoryService;
        _projectWorkspaceService = projectWorkspaceService;
        _translationRunnerService = translationRunnerService;
        _compareTranslationService = compareTranslationService;

        // Long book texts on some models (e.g., Gemini 3 preview) can take several minutes per request.
        // Use a higher client timeout and rely on cancellation tokens for user-initiated Stop.
        _httpClient.Timeout = TimeSpan.FromMinutes(15);
        _geminiClient = new GeminiClient(_httpClient, new UiGeminiCallLogger(this));

        // Model availability varies by API key; use Refresh to populate the real list.
        AvailableModels.ReplaceAll(new[] { "gemini-3-flash-preview" });
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
                nameof(StringEntryStatus.Pending),
                nameof(StringEntryStatus.InProgress),
                nameof(StringEntryStatus.Done),
                nameof(StringEntryStatus.Skipped),
                nameof(StringEntryStatus.Error),
                nameof(StringEntryStatus.Edited),
            }
        );

        GlossaryView = CollectionViewSource.GetDefaultView(Glossary);
        GlossaryView.Filter = GlossaryFilter;
        GlossaryCategoryFilterValues.ReplaceAll(new[] { GlossaryCategoryAll, GlossaryCategoryNone });

        GlobalGlossaryView = CollectionViewSource.GetDefaultView(GlobalGlossary);
        GlobalGlossaryView.Filter = GlobalGlossaryFilter;
        GlobalGlossaryCategoryFilterValues.ReplaceAll(new[] { GlossaryCategoryAll, GlossaryCategoryNone });

        GlobalTranslationMemoryView = CollectionViewSource.GetDefaultView(GlobalTranslationMemory);
        GlobalTranslationMemoryView.Filter = GlobalTranslationMemoryFilter;

        GlossaryLookupResultsView = CollectionViewSource.GetDefaultView(GlossaryLookupResults);

        LqaIssuesView = CollectionViewSource.GetDefaultView(LqaIssues);
        LqaIssuesView.Filter = LqaIssueFilter;

        StringsTab = new StringsTabViewModel(this);
        CompareTab = new CompareTabViewModel(this);
        LqaTab = new LqaTabViewModel(this);
        ProjectGlossaryTab = new ProjectGlossaryTabViewModel(this);
        GlobalGlossaryTab = new GlobalGlossaryTabViewModel(this);
        GlobalTranslationMemoryTab = new GlobalTranslationMemoryTabViewModel(this);
        PromptTab = new PromptTabViewModel(this);
        ProjectContextTab = new ProjectContextTabViewModel(this);
        ApiLogsTab = new ApiLogsTabViewModel(this);

        RefreshPromptLint();
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

    partial void OnEnableApiKeyFailoverChanged(bool value) => SaveTranslationPreferences();

    partial void OnEnableBookFullModelOverrideChanged(bool value) => SaveTranslationPreferences();

    partial void OnBookFullModelChanged(string value) => SaveTranslationPreferences();

    partial void OnEnableQualityEscalationChanged(bool value) => SaveTranslationPreferences();

    partial void OnQualityEscalationModelChanged(string value) => SaveTranslationPreferences();

    partial void OnEnablePromptCacheChanged(bool value) => SaveTranslationPreferences();

    partial void OnEnableRiskyCandidateRerankChanged(bool value) => SaveTranslationPreferences();

    partial void OnRiskyCandidateCountChanged(int value)
    {
        var clamped = Math.Clamp(value, 2, 8);
        if (clamped != value)
        {
            RiskyCandidateCount = clamped;
            return;
        }

        SaveTranslationPreferences();
    }

    partial void OnSelectedFranchiseChanged(BethesdaFranchise value)
    {
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
            StatusMessage = "Franchise changed. (Restart translation to apply.)";
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
            await ReloadGlobalTranslationMemoryAsync();
            StatusMessage = "Franchise changed.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Franchise switch", ex);
        }
    }

    private void SaveTranslationPreferences()
    {
        try
        {
            var current = _appSettings.Load();
            _appSettings.Save(
                current with
                {
                    EnableApiKeyFailover = EnableApiKeyFailover,
                    EnableBookFullModelOverride = EnableBookFullModelOverride,
                    BookFullModel = string.IsNullOrWhiteSpace(BookFullModel) ? null : BookFullModel.Trim(),
                    EnablePromptCache = EnablePromptCache,
                    EnableQualityEscalation = EnableQualityEscalation,
                    QualityEscalationModel = string.IsNullOrWhiteSpace(QualityEscalationModel) ? null : QualityEscalationModel.Trim(),
                    EnableRiskyCandidateRerank = EnableRiskyCandidateRerank,
                    RiskyCandidateCount = Math.Clamp(RiskyCandidateCount, 2, 8),
                }
            );
        }
        catch
        {
            // ignore
        }
    }
}
