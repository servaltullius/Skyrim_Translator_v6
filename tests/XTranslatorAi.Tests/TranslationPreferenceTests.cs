using System.Net.Http;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class TranslationPreferenceTests
{
    [Fact]
    public void LegacySettings_UseExistingDefaultsAndKeepExperimentsOff()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(fixture.Path, """{"enablePromptCache":false,"riskyCandidateCount":5}""");
        var settings = fixture.Store.Load();
        Assert.Equal(12, settings.BatchSize);
        Assert.Equal(15000, settings.MaxCharsPerBatch);
        Assert.Equal(2, settings.MaxParallelRequests);
        Assert.Equal(0, settings.MaxOutputTokensOverride);
        Assert.Equal(PlaceholderSemanticRepairMode.Soft, settings.SemanticRepairMode);
        Assert.True(settings.EnableRepairPass);
        Assert.True(settings.KeepSkyrimTagsRaw);
        Assert.True(settings.EnableDialogueContextWindow);
        Assert.True(settings.EnableSessionTermMemory);
        Assert.True(settings.UseRecStyleHints);
        Assert.True(settings.EnableProjectContext);
        Assert.False(settings.EnableTemplateFixer);
        Assert.False(settings.EnableAdaptiveOutputBudget);
        Assert.False(settings.EnableBookContext);
        Assert.Equal(8, settings.MaxRetryGenerations);
        Assert.Equal(0, settings.MaxTotalGenerations);
        Assert.False(settings.EnablePromptCache);
        Assert.Equal(5, settings.RiskyCandidateCount);
    }

    [Fact]
    public void AllTranslationPreferences_RoundTripWithoutResettingOldOptions()
    {
        using var fixture = new SettingsFixture();
        var expected = ChangedSettings();
        fixture.Store.Save(expected);
        Assert.Equal(expected, fixture.Store.Load());
    }

    [Fact]
    public void InvalidPersistedValues_AreBoundedWithoutDiscardingOtherPreferences()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(fixture.Path, """
            {"batchSize":-2,"maxCharsPerBatch":999999,"maxParallelRequests":0,
             "maxOutputTokensOverride":1,"semanticRepairMode":999,"riskyCandidateCount":99,
             "maxRetryGenerations":-1,"maxTotalGenerations":-3,"enableRepairPass":false,
             "enableBookBodyModelOverride":true}
            """);
        var saved = fixture.Store.Load();
        Assert.Equal(1, saved.BatchSize);
        Assert.Equal(50000, saved.MaxCharsPerBatch);
        Assert.Equal(1, saved.MaxParallelRequests);
        Assert.Equal(256, saved.MaxOutputTokensOverride);
        Assert.Equal(PlaceholderSemanticRepairMode.Soft, saved.SemanticRepairMode);
        Assert.Equal(8, saved.RiskyCandidateCount);
        Assert.Equal(0, saved.MaxRetryGenerations);
        Assert.Equal(0, saved.MaxTotalGenerations);
        Assert.False(saved.EnableRepairPass);
        Assert.True(saved.EnableBookBodyModelOverride);
    }

    [Fact]
    public Task LoadingViewModel_DoesNotRewritePreferencesDuringInitialization()
        => RunOnSta(() =>
        {
            using var fixture = new SettingsFixture();
            var expected = ChangedSettings();
            fixture.Store.Save(expected);
            var before = File.ReadAllBytes(fixture.Path);
            using var ui = new ViewModelFixture(fixture);
            Assert.Equal(before, File.ReadAllBytes(fixture.Path));
            Assert.Equal(expected.SelectedModel, ui.ViewModel.SelectedModel);
            Assert.Equal(expected.BatchSize, ui.ViewModel.BatchSize);
            Assert.Equal(expected.SemanticRepairMode, ui.ViewModel.SemanticRepairMode);
            Assert.False(ui.ViewModel.EnableProjectContext);
            Assert.True(ui.ViewModel.EnableAdaptiveOutputBudget);
            Assert.True(ui.ViewModel.EnableBookContext);
            Assert.Equal(expected.MaxRetryGenerations, ui.ViewModel.MaxRetryGenerations);
            Assert.Equal(expected.MaxTotalGenerations, ui.ViewModel.MaxTotalGenerations);
            Assert.Contains(expected.SelectedModel!, ui.ViewModel.AvailableModels);
        });

    [Fact]
    public Task EditingViewModelPreferences_PersistsEveryAddedOption()
        => RunOnSta(() =>
        {
            using var fixture = new SettingsFixture();
            using var ui = new ViewModelFixture(fixture);
            Assert.False(File.Exists(fixture.Path));
            var vm = ui.ViewModel;
            vm.SelectedModel = "gemini-2.5-flash";
            vm.BatchSize = 7;
            vm.MaxCharsPerBatch = 6000;
            vm.MaxParallelRequests = 4;
            vm.MaxOutputTokensOverride = 4096;
            vm.EnableRepairPass = false;
            vm.SemanticRepairMode = PlaceholderSemanticRepairMode.Strict;
            vm.KeepSkyrimTagsRaw = false;
            vm.EnableDialogueContextWindow = false;
            vm.EnableSessionTermMemory = false;
            vm.UseRecStyleHints = false;
            vm.EnableTemplateFixer = true;
            vm.EnableProjectContext = false;
            vm.EnableAdaptiveOutputBudget = true;
            vm.EnableBookContext = true;
            vm.MaxRetryGenerations = 17;
            vm.MaxTotalGenerations = 123;

            using var restored = new ViewModelFixture(fixture);
            var copy = restored.ViewModel;
            Assert.Equal(vm.SelectedModel, copy.SelectedModel);
            Assert.Equal(7, copy.BatchSize);
            Assert.Equal(6000, copy.MaxCharsPerBatch);
            Assert.Equal(4, copy.MaxParallelRequests);
            Assert.Equal(4096, copy.MaxOutputTokensOverride);
            Assert.False(copy.EnableRepairPass);
            Assert.Equal(PlaceholderSemanticRepairMode.Strict, copy.SemanticRepairMode);
            Assert.False(copy.KeepSkyrimTagsRaw);
            Assert.False(copy.EnableDialogueContextWindow);
            Assert.False(copy.EnableSessionTermMemory);
            Assert.False(copy.UseRecStyleHints);
            Assert.True(copy.EnableTemplateFixer);
            Assert.False(copy.EnableProjectContext);
            Assert.True(copy.EnableAdaptiveOutputBudget);
            Assert.True(copy.EnableBookContext);
            Assert.Equal(17, copy.MaxRetryGenerations);
            Assert.Equal(123, copy.MaxTotalGenerations);
        });

    // Started while settings.json was locked, the window holds defaults and no keys. Once the lock cleared, a changed
    // preference wrote every default over the file and saving a key kept only that key; the status still said saved.
    [Fact]
    public Task StartedWhileSettingsWereUnreadable_DoesNotWriteItsDefaultsOverThem()
        => RunOnSta(() =>
        {
            using var fixture = new SettingsFixture();
            fixture.Store.Save(new AppSettings(BatchSize: 30, ApiKeys: new[] { new SavedApiKey("A", "key-a"), new SavedApiKey("B", "key-b") }));

            ViewModelFixture ui;
            using (new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                ui = new ViewModelFixture(fixture);
            }

            using (ui)
            {
                ui.ViewModel.BatchSize = 7;
                ui.ViewModel.ApiKey = "key-c";
                ui.ViewModel.SaveApiKeyCommand.Execute(null);

                Assert.DoesNotContain("저장했습니다", ui.ViewModel.StatusMessage);
                var stored = new AppSettingsStore(fixture.Path).Load();
                Assert.Equal(30, stored.BatchSize);
                Assert.Equal(new[] { "key-a", "key-b" }, stored.ApiKeys!.Select(k => k.ApiKey).ToArray());
            }
        });

    [Fact]
    public Task UnsupportedCandidateModel_ShowsOneWithoutErasingSavedPreference()
        => RunOnSta(() =>
        {
            using var fixture = new SettingsFixture();
            fixture.Store.Save(new AppSettings(EnableRiskyCandidateRerank: true, RiskyCandidateCount: 6));
            using var ui = new ViewModelFixture(fixture);
            var vm = ui.ViewModel;
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            Assert.Equal(GeminiModelCatalog.DefaultModel, vm.SelectedModel);
            Assert.False(vm.SelectedModelSupportsMultipleCandidates);
            Assert.False(vm.IsRiskyCandidateCountEnabled);
            Assert.Equal(1, vm.EffectiveRiskyCandidateCount);
            Assert.True(vm.EnableRiskyCandidateRerank);
            Assert.Equal(6, vm.RiskyCandidateCount);

            vm.SelectedModel = "gemini-2.5-flash";
            Assert.True(vm.IsRiskyCandidateCountEnabled);
            Assert.Equal(6, vm.EffectiveRiskyCandidateCount);
            Assert.Contains(nameof(vm.EffectiveRiskyCandidateCount), changed);
            vm.EnableRiskyCandidateRerank = false;
            Assert.Equal(1, vm.EffectiveRiskyCandidateCount);
            vm.EnableRiskyCandidateRerank = true;
            vm.SelectedModel = "unknown-model";
            Assert.False(vm.SelectedModelSupportsMultipleCandidates);
            Assert.Equal(1, vm.EffectiveRiskyCandidateCount);
            Assert.Contains("저장값 6개", vm.EffectiveRiskyCandidateSummary);
            Assert.Equal(6, fixture.Store.Load().RiskyCandidateCount);
            Assert.True(fixture.Store.Load().EnableRiskyCandidateRerank);
        });

    [Fact]
    public Task Presets_ApplyDocumentedScopeAndPreserveQualityAndExperimentPreferences()
        => RunOnSta(() =>
        {
            using var fixture = new SettingsFixture();
            fixture.Store.Save(ChangedSettings());
            using var ui = new ViewModelFixture(fixture);
            var vm = ui.ViewModel;
            vm.ApplyFreeTierPresetCommand.Execute(null);
            Assert.Equal(GeminiModelCatalog.LowCostModel, vm.SelectedModel);
            Assert.Equal(8, vm.BatchSize);
            Assert.Equal(1, vm.MaxParallelRequests);
            Assert.False(vm.EnablePromptCache);
            Assert.Equal(0, vm.MaxOutputTokensOverride);
            AssertPreserved();
            Assert.Equal(vm.SelectedModel, fixture.Store.Load().SelectedModel);

            vm.ApplyPaidPresetCommand.Execute(null);
            Assert.Equal(GeminiModelCatalog.DefaultModel, vm.SelectedModel);
            Assert.Equal(12, vm.BatchSize);
            Assert.Equal(2, vm.MaxParallelRequests);
            Assert.True(vm.EnablePromptCache);
            Assert.Equal(15000, vm.MaxCharsPerBatch);
            AssertPreserved();
            Assert.Equal(vm.BatchSize, fixture.Store.Load().BatchSize);

            void AssertPreserved()
            {
                Assert.True(vm.EnableBookFullModelOverride);
                Assert.True(vm.EnableBookBodyModelOverride);
                Assert.True(vm.EnableQualityEscalation);
                Assert.False(vm.EnableRepairPass);
                Assert.Equal(PlaceholderSemanticRepairMode.Strict, vm.SemanticRepairMode);
                Assert.Equal(6, vm.RiskyCandidateCount);
                Assert.True(vm.EnableAdaptiveOutputBudget);
                Assert.True(vm.EnableBookContext);
                Assert.Equal(17, vm.MaxRetryGenerations);
                Assert.Equal(123, vm.MaxTotalGenerations);
            }
        });

    [Fact]
    public Task GenerationLimits_ValidateViewModelValuesBeforePersistence()
        => RunOnSta(() =>
        {
            using var fixture = new SettingsFixture();
            using var ui = new ViewModelFixture(fixture);
            ui.ViewModel.MaxRetryGenerations = -1;
            ui.ViewModel.MaxTotalGenerations = -1;
            Assert.Equal(0, ui.ViewModel.MaxRetryGenerations);
            Assert.Equal(0, ui.ViewModel.MaxTotalGenerations);
            ui.ViewModel.MaxRetryGenerations = 999;
            Assert.Equal(100, ui.ViewModel.MaxRetryGenerations);
            Assert.Equal(100, fixture.Store.Load().MaxRetryGenerations);
            Assert.Equal(0, fixture.Store.Load().MaxTotalGenerations);
        });

    private static AppSettings ChangedSettings() => new(
        EnableApiKeyFailover: false, EnableBookFullModelOverride: true, BookFullModel: "gemini-3.8-flash",
        EnablePromptCache: false, EnableQualityEscalation: true, QualityEscalationModel: "gemini-3.8-flash",
        EnableRiskyCandidateRerank: true, RiskyCandidateCount: 6, EnableBookBodyModelOverride: true,
        SelectedModel: "gemini-2.5-flash", BatchSize: 7, MaxCharsPerBatch: 6000,
        MaxParallelRequests: 4, MaxOutputTokensOverride: 4096, EnableRepairPass: false,
        SemanticRepairMode: PlaceholderSemanticRepairMode.Strict, KeepSkyrimTagsRaw: false,
        EnableDialogueContextWindow: false, EnableSessionTermMemory: false, UseRecStyleHints: false,
        EnableTemplateFixer: true, EnableProjectContext: false, EnableAdaptiveOutputBudget: true,
        EnableBookContext: true, MaxRetryGenerations: 17, MaxTotalGenerations: 123);

    private static Task RunOnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class SettingsFixture : IDisposable
    {
        public string DirectoryPath { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TulliusTranslator-tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(DirectoryPath, "settings.json");
        public AppSettingsStore Store { get; }
        public SettingsFixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            Store = new AppSettingsStore(Path);
        }
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class ViewModelFixture : IDisposable
    {
        private readonly HttpClient _httpClient = new(new NoNetworkHandler());
        private readonly GlobalProjectDbService _globalDb;
        public MainViewModel ViewModel { get; }
        public ViewModelFixture(SettingsFixture settings)
        {
            var builtIn = new BuiltInGlossaryService();
            // Never the user's global DB under %LOCALAPPDATA%.
            var globalDb = _globalDb = new GlobalProjectDbService(builtIn, System.IO.Path.Combine(settings.DirectoryPath, "global"));
            var glossary = new ProjectGlossaryService(new GlossaryImportService(new GlossaryFileService()));
            ViewModel = new MainViewModel(_httpClient, new MainViewModelServices(
                settings.Store, new ApiCallLogService(), new SystemPromptBuilder(), new NoUi(),
                new BundledFranchiseTmSeedService(settings.DirectoryPath), globalDb, glossary,
                new GlobalGlossaryService(globalDb, glossary), new FranchiseTranslationMemoryService(globalDb),
                new ProjectWorkspaceService(globalDb), new TranslationRunnerService(globalDb),
                new CompareTranslationService(glossary)));
        }
        public void Dispose()
        {
            _globalDb.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _httpClient.Dispose();
        }
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Network calls are forbidden in preference tests.");
    }

    private sealed class NoUi : IUiInteractionService
    {
        public UiMessageBoxResult ShowMessage(string message, string title, UiMessageBoxButton button, UiMessageBoxImage image, UiMessageBoxResult defaultResult) => UiMessageBoxResult.Ok;
        public string? ShowOpenFileDialog(OpenFileDialogRequest request) => null;
        public string? ShowSaveFileDialog(SaveFileDialogRequest request) => null;
        public bool TryOpenFolder(string path) => false;
    }
}
