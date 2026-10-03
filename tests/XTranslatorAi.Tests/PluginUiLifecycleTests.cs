using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Xml;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task InvalidPluginOpen_AfterWaitingForAnotherTool_DoesNotWaitForItselfOrDiscardWorkspace()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var db = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
            fixture.State.SetWorkspace(db, new XTranslatorXmlInfo("old.esp", "english", "korean", "2", false, ""), "old.xml");
            fixture.Vm.IsProjectLoaded = true;
            var bad = Path.Combine(fixture.Root, "invalid.esp");
            await File.WriteAllTextAsync(bad, "Not a plugin");
            fixture.Ui.OpenPath = bad;
            var tracker = (ProjectOperationTracker)typeof(MainViewModel).GetField("_projectOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Vm)!;
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tool = tracker.RunAsync(async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { canceled.SetResult(); await release.Task; }
            });
            var opening = fixture.Vm.OpenPluginCommand.ExecuteAsync(null);
            await canceled.Task;
            Assert.False(opening.IsCompleted);
            release.SetResult();
            await opening.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool);
            Assert.Same(db, fixture.State.Db);
            Assert.True(fixture.Vm.IsProjectLoaded);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            Assert.False(fixture.Vm.IsPluginIoBusy);
            Assert.Equal("old.xml", fixture.Vm.CurrentXmlFileName);
        });

    [Fact]
    public Task PluginWorkspace_EnablesPluginExportWithoutXmlInfo_AndPreservesGameScope()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var path = Path.Combine(fixture.Root, "Test.esp");
            await File.WriteAllBytesAsync(path, PluginProjectIntegrationTests.CreateMinimalPlugin());
            var document = await PluginReader.ReadAsync(path, new PluginReadOptions(), CancellationToken.None);
            var db = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
            fixture.State.SetPluginWorkspace(db, document, "utf-8");
            fixture.Vm.IsProjectLoaded = true;
            Assert.Null(fixture.State.XmlInfo);
            Assert.Null(fixture.State.InputXmlPath);
            Assert.True(fixture.State.HasSource);
            Assert.Equal("Test.esp", fixture.Vm.CurrentXmlFileName);
            Assert.True(fixture.Vm.ExportPluginCommand.CanExecute(null));
            Assert.False(fixture.Vm.ExportXmlCommand.CanExecute(null));
            fixture.Vm.SelectedFranchise = BethesdaFranchise.Fallout;
            Assert.Equal(BethesdaFranchise.ElderScrolls, fixture.Vm.SelectedFranchise);
            Assert.False(fixture.Ui.NetworkOrDialogUsed);
            fixture.Vm.IsPluginIoBusy = true;
            Assert.False(fixture.Vm.IsWorkspaceInteractive);
            Assert.False(fixture.Vm.ExportPluginCommand.CanExecute(null));
            fixture.Vm.IsPluginIoBusy = false;
        });

    [Fact]
    public Task PluginImportDefaults_RoundTripInIsolatedSettings()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            fixture.Vm.PluginSourceLanguage = "german";
            fixture.Vm.PluginTargetLanguage = "korean";
            fixture.Vm.PluginSourceEncoding = "windows-1252";
            Assert.Equal("windows-1252", fixture.Vm.PluginMetadataEncoding);
            fixture.Vm.PluginMetadataEncoding = "ks_c_5601-1987";
            fixture.Vm.PluginTargetEncoding = "ks_c_5601-1987";
            fixture.Vm.PluginStringsDirectory = Path.Combine(fixture.Root, "Strings");
            var saved = fixture.Settings.Load();
            Assert.Equal("german", saved.PluginSourceLanguage);
            Assert.Equal("korean", saved.PluginTargetLanguage);
            Assert.Equal("windows-1252", saved.PluginSourceEncoding);
            Assert.Equal("ks_c_5601-1987", saved.PluginMetadataEncoding);
            Assert.Equal("ks_c_5601-1987", saved.PluginTargetEncoding);
            Assert.Equal(fixture.Vm.PluginStringsDirectory, saved.PluginStringsDirectory);
        });

    [Fact]
    public Task PluginBusy_NotifiesEveryAffectedCommand_AndRestoresXmlExportOnlyForXml()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var commands = new ICommand[]
            {
                fixture.Vm.OpenXmlCommand, fixture.Vm.OpenPluginCommand, fixture.Vm.StartTranslationCommand,
                fixture.Vm.EstimateCostCommand, fixture.Vm.GenerateProjectContextCommand,
                fixture.Vm.SaveProjectContextCommand, fixture.Vm.ClearProjectContextCommand,
                fixture.Vm.ExportXmlCommand, fixture.Vm.ExportPluginCommand,
            };
            var notifications = new int[commands.Length];
            for (var i = 0; i < commands.Length; i++)
            {
                var index = i;
                commands[i].CanExecuteChanged += (_, _) => notifications[index]++;
            }
            fixture.Vm.IsPluginIoBusy = true;
            Assert.All(commands, command => Assert.False(command.CanExecute(null)));
            Assert.All(notifications, count => Assert.True(count > 0));
            fixture.Vm.IsPluginIoBusy = false;
            Assert.True(fixture.Vm.StartTranslationCommand.CanExecute(null));
            Assert.True(fixture.Vm.SaveProjectContextCommand.CanExecute(null));
            Assert.True(fixture.Vm.ExportPluginCommand.CanExecute(null));
            Assert.False(fixture.Vm.ExportXmlCommand.CanExecute(null));
            fixture.State.SetWorkspace(fixture.State.Db!, new XTranslatorXmlInfo("other.esp", "english", "korean", "2", false, ""), "other.xml");
            fixture.Vm.IsPluginIoBusy = true;
            fixture.Vm.IsPluginIoBusy = false;
            Assert.True(fixture.Vm.ExportXmlCommand.CanExecute(null));
            Assert.False(fixture.Vm.ExportPluginCommand.CanExecute(null));
        });

    [Fact]
    public Task PluginLoaded_CostEstimateCountsTokensWithoutChangingTranslationOrSource()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler();
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            var before = await fixture.State.Db!.TryGetPluginSourceAsync(CancellationToken.None);
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No); // Remaining rows.
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No); // countTokens only.
            await fixture.Vm.EstimateCostCommand.ExecuteAsync(null);
            Assert.True(handler.Requests.Count >= 2);
            Assert.All(handler.Requests, request => Assert.Contains(":countTokens", request.Path));
            Assert.Contains(handler.Requests, request => request.Body.Contains("Iron Sword", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(fixture.Vm.LastCostEstimateSummary));
            var row = Assert.Single(await fixture.State.Db.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal(StringEntryStatus.Pending, row.Status);
            Assert.Equal("Iron Sword", row.DestText);
            Assert.Equal(before!.Info.Sha256, (await fixture.State.Db.TryGetPluginSourceAsync(CancellationToken.None))!.Info.Sha256);
            Assert.Null(fixture.State.XmlInfo);
        });

    [Fact]
    public Task PluginLoaded_ProjectContextUsesPluginNameAndRows_AndPersistsToItsDb()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "{\"context\":\"스카이림 무기 이름의 일관성을 유지합니다.\"}" };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            await fixture.Vm.GenerateProjectContextCommand.ExecuteAsync(null);
            var request = Assert.Single(handler.Requests);
            Assert.Contains(":generateContent", request.Path);
            using var requestJson = JsonDocument.Parse(request.Body);
            var prompt = requestJson.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
            // Dialogue mods get one speech level per relationship (Serana switched between 반말 and 해요체).
            Assert.Contains("Speech levels (말투)", prompt);
            const string marker = "Project scan report JSON:";
            var reportStart = prompt.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(reportStart >= 0);
            using var report = JsonDocument.Parse(prompt[(reportStart + marker.Length)..].TrimStart().Split('\n')[0]);
            Assert.Equal("Test.esp", report.RootElement.GetProperty("inputFile").GetString());
            Assert.Equal(1, report.RootElement.GetProperty("totalStrings").GetInt32());
            Assert.Equal("Iron Sword", Assert.Single(report.RootElement.GetProperty("samples").EnumerateArray()).GetProperty("text").GetString());
            Assert.Equal("스카이림 무기 이름의 일관성을 유지합니다.", fixture.Vm.ProjectContextPreview);
            Assert.Equal(fixture.Vm.ProjectContextPreview,
                (await fixture.State.Db!.TryGetProjectContextAsync(CancellationToken.None))!.ContextText);
            Assert.Equal(StringEntryStatus.Pending, Assert.Single(await fixture.State.Db.GetStringsAsync(10, 0, CancellationToken.None)).Status);
        });

    [Fact]
    public Task PluginLoaded_StartUsesTranslationPipeline_AndPreservesPluginExportIdentity()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler();
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            handler.GeneratedText = "철검"; // The existing pipeline uses its text lane for a single row.
            Assert.True(fixture.Vm.StartTranslationCommand.CanExecute(null));
            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            var request = Assert.Single(handler.Requests);
            Assert.Contains(":generateContent", request.Path);
            Assert.Contains("Iron Sword", request.Body);
            var translated = Assert.Single(await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal(StringEntryStatus.Done, translated.Status);
            Assert.Equal("철검", translated.DestText);
            Assert.Equal("철검", row.DestText);
            Assert.False(fixture.Vm.IsTranslating);
            var document = fixture.State.PluginDocument!;
            var snapshot = await fixture.State.Db.GetPluginTranslationsForExportAsync(document.Info.Sha256, CancellationToken.None);
            Assert.Equal("철검", snapshot[Assert.Single(document.Fields).Key]);
            var project = await fixture.State.Db.TryGetProjectAsync(CancellationToken.None);
            Assert.Equal("", project!.InputXmlPath);
            Assert.Equal("Test.esp", project.AddonName);
            Assert.True(fixture.Vm.ExportPluginCommand.CanExecute(null));
            Assert.False(fixture.Vm.ExportXmlCommand.CanExecute(null));
        });

    [Fact]
    public Task PluginPreferences_AffectNextOpenOnly_AndPreserveCredentialsAndTranslationSettings()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture(initialSettings: new AppSettings(ApiKey: "fixture-key", BatchSize: 17,
                EnablePromptCache: false, PluginMetadataEncoding: "ks_c_5601-1987"));
            await fixture.LoadPluginWorkspaceAsync();
            fixture.Vm.PluginSourceLanguage = "german";
            fixture.Vm.PluginTargetLanguage = "japanese";
            fixture.Vm.PluginSourceEncoding = "windows-1252";
            fixture.Vm.PluginTargetEncoding = "ks_c_5601-1987";
            var saved = fixture.Settings.Load();
            Assert.Equal("fixture-key", saved.ApiKey);
            Assert.Equal(17, saved.BatchSize);
            Assert.False(saved.EnablePromptCache);
            Assert.Equal("ks_c_5601-1987", saved.PluginMetadataEncoding);
            Assert.Equal("ks_c_5601-1987", fixture.Vm.PluginMetadataEncoding);
            Assert.Equal("english", fixture.Vm.SourceLang);
            Assert.Equal("korean", fixture.Vm.TargetLang);
            Assert.Equal("utf-8", fixture.State.PluginTargetEncoding);
            var source = await fixture.State.Db!.TryGetPluginSourceAsync(CancellationToken.None);
            Assert.Equal("english", source!.Info.Options.SourceLanguage);
            Assert.Equal("utf-8", source.Info.Options.SourceEncoding);
            Assert.Equal("windows-1252", source.Info.Options.MetadataEncoding);
            Assert.Equal("utf-8", source.TargetEncoding);
            Assert.Equal("korean", (await fixture.State.Db.TryGetProjectAsync(CancellationToken.None))!.DestLang);
        });

    [Fact]
    public Task CancelPluginOpen_WhileStoppingOldWork_PreservesCurrentWorkspaceAndCreatesNoImportDb()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var originalDb = fixture.State.Db;
            var originalRow = Assert.Single(fixture.Vm.Entries);
            var path = Path.Combine(fixture.Root, "replacement.esp");
            await File.WriteAllBytesAsync(path, PluginProjectIntegrationTests.CreateMinimalPlugin());
            fixture.Ui.OpenPath = path;
            var tracker = (ProjectOperationTracker)typeof(MainViewModel).GetField("_projectOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Vm)!;
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var previous = tracker.RunAsync(async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { canceled.SetResult(); await release.Task; }
            });
            var opening = fixture.Vm.OpenPluginCommand.ExecuteAsync(null);
            await canceled.Task;
            fixture.Vm.CancelPluginIoCommand.Execute(null);
            release.SetResult();
            await opening.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previous);
            Assert.Same(originalDb, fixture.State.Db);
            Assert.Same(originalRow, Assert.Single(fixture.Vm.Entries));
            Assert.True(fixture.Vm.IsProjectLoaded);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            Assert.False(File.Exists(ProjectPaths.GetPluginProjectDbPath(path, new PluginReadOptions(), "korean", "utf-8", fixture.Root)));
        });

    [Fact]
    public Task ReopenPlugin_WhenPreparationFails_DoesNotReplaceRowsInTheAlreadyOpenDb()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var originalDb = fixture.State.Db!;
            var originalVm = Assert.Single(fixture.Vm.Entries);
            var connection = (SqliteConnection)typeof(ProjectDb).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(originalDb)!;
            await using (var command = connection.CreateCommand())
            {
                // An unreadable auxiliary table reproduces failure after the original implementation's import.
                command.CommandText = "ALTER TABLE Glossary RENAME COLUMN SrcTerm TO InvalidSourceColumn; UPDATE StringEntry SET SourceText='Saved source', DestText='Saved working text';";
                await command.ExecuteNonQueryAsync();
            }
            originalVm.SourceText = "Saved source";
            originalVm.DestText = "Saved working text";
            fixture.Ui.OpenPath = fixture.State.PluginDocument!.Info.InputPath;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(originalDb, fixture.State.Db);
            Assert.Same(originalVm, Assert.Single(fixture.Vm.Entries));
            Assert.True(fixture.Vm.IsProjectLoaded);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            var saved = Assert.Single(await originalDb.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal("Saved source", saved.SourceText);
            Assert.Equal("Saved working text", saved.DestText);
            Assert.Contains("플러그인 읽기", fixture.Vm.StatusMessage);
        });

    private static Task RunOnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new StaContext();
            SynchronizationContext.SetSynchronizationContext(context);
            context.Post(async _ =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { context.Complete(); }
            }, null);
            context.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class StaContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        public void Run() { foreach (var item in _queue.GetConsumingEnumerable()) item.Callback(item.State); }
        public void Complete() => _queue.CompleteAdding();
        public void Dispose() => _queue.Dispose();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "plugin-ui-tests", Guid.NewGuid().ToString("N"));
        private string? _pluginDbPath;
        public string DbPath => _pluginDbPath ?? Path.Combine(Root, "workspace.sqlite");
        public MainViewModel Vm { get; }
        public FakeUi Ui { get; } = new();
        public AppSettingsStore Settings { get; }
        private readonly HttpClient _client;
        private readonly GlobalProjectDbService _globalDbService;
        private ProjectDb? _globalDb;
        public ProjectState State => (ProjectState)typeof(MainViewModel).GetField("_projectState", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Vm)!;
        public Fixture(HttpMessageHandler? handler = null, AppSettings? initialSettings = null)
        {
            Directory.CreateDirectory(Root);
            _client = new HttpClient(handler ?? new NoNetwork());
            Settings = new AppSettingsStore(Path.Combine(Root, "settings.json"));
            if (initialSettings != null) Settings.Save(initialSettings);
            var builtIn = new BuiltInGlossaryService();
            var globalDb = new GlobalProjectDbService(builtIn);
            _globalDbService = globalDb;
            var glossary = new ProjectGlossaryService(new GlossaryImportService(new GlossaryFileService()));
            Vm = new MainViewModel(_client, new MainViewModelServices(Settings, new ApiCallLogService(), new SystemPromptBuilder(), Ui,
                new BundledFranchiseTmSeedService(Root), globalDb, glossary, new GlobalGlossaryService(globalDb, glossary),
                new FranchiseTranslationMemoryService(globalDb), new ProjectWorkspaceService(globalDb, builtIn, Root),
                new TranslationRunnerService(globalDb), new CompareTranslationService(glossary)));
        }
        public async Task LoadPluginWorkspaceAsync(byte[]? plugin = null, Action<string>? prepareFiles = null, string targetEncoding = "utf-8")
        {
            // Seed the service's cache with a disposable test DB: commands must never open the user's global DB.
            _globalDb = await ProjectDb.OpenOrCreateAsync(Path.Combine(Root, "global.sqlite"), CancellationToken.None);
            var cache = (ConcurrentDictionary<BethesdaFranchise, ProjectDb>)typeof(GlobalProjectDbService)
                .GetField("_dbByFranchise", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_globalDbService)!;
            cache[BethesdaFranchise.ElderScrolls] = _globalDb;
            var path = Path.Combine(Root, "Test.esp");
            await File.WriteAllBytesAsync(path, plugin ?? PluginProjectIntegrationTests.CreateMinimalPlugin());
            prepareFiles?.Invoke(path);
            var document = await PluginReader.ReadAsync(path, new PluginReadOptions(), CancellationToken.None);
            _pluginDbPath = ProjectPaths.GetPluginProjectDbPath(path, document.Info.Options, "korean", targetEncoding, Root);
            var db = await ProjectDb.OpenOrCreateAsync(DbPath, CancellationToken.None);
            var project = new ProjectInfo(1, "", "Test.esp", BethesdaFranchise.ElderScrolls, "english", "korean", "", false,
                "", Vm.SelectedModel, Vm.BasePromptText, "", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var imported = await db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, project, targetEncoding, CancellationToken.None);
            State.SetPluginWorkspace(db, document, targetEncoding);
            State.SetEntries(imported.Select(row => new StringEntryViewModel(row.Id, row.OrderIndex)
            {
                Edid = row.Edid, Rec = row.Rec, SourceText = row.SourceText, DestText = row.DestText, Status = row.Status,
                PluginLocation = document.Fields[row.OrderIndex],
            }).ToList());
            Vm.TotalCount = imported.Count;
            Vm.PendingCount = imported.Count;
            Vm.SourceLang = "english";
            Vm.TargetLang = "korean";
            Vm.ApiKey = "fixture-key";
            Vm.EnablePromptCache = false;
            Vm.EnableProjectContext = false;
            Vm.EnableSessionTermMemory = false;
            Vm.EnableApiCallLogging = false;
            Vm.IsProjectLoaded = true;
        }
        public async ValueTask DisposeAsync()
        {
            await State.DisposeDbAsync();
            if (_globalDb != null) await _globalDb.DisposeAsync();
            _client.Dispose();
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(DbPath);
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(Path.Combine(Root, "global.sqlite"));
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Plugin lifecycle tests must not use the network.");
    }

    private sealed class FakeUi : IUiInteractionService
    {
        public string? OpenPath { get; set; }
        public string? SavePath { get; set; }
        public Queue<UiMessageBoxResult> Responses { get; } = new();
        public bool NetworkOrDialogUsed { get; private set; }
        public UiMessageBoxResult ShowMessage(string message, string title, UiMessageBoxButton button, UiMessageBoxImage image, UiMessageBoxResult defaultResult)
        { NetworkOrDialogUsed = true; return Responses.Count > 0 ? Responses.Dequeue() : UiMessageBoxResult.Ok; }
        public string? ShowOpenFileDialog(OpenFileDialogRequest request) { NetworkOrDialogUsed = true; return OpenPath; }
        public string? ShowSaveFileDialog(SaveFileDialogRequest request) { NetworkOrDialogUsed = true; return SavePath; }
        public bool TryOpenFolder(string path) { NetworkOrDialogUsed = true; return false; }
    }

    private sealed class FakeGeminiHandler : HttpMessageHandler
    {
        public ConcurrentQueue<(string Path, string Body)> Requests { get; } = new();
        public string GeneratedText { get; set; } = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue((path, body));
            var response = path.EndsWith(":countTokens", StringComparison.Ordinal)
                ? "{\"totalTokens\":120}"
                : path.EndsWith(":generateContent", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new
                    {
                        candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = GeneratedText } } } } },
                        usageMetadata = new { promptTokenCount = 120, candidatesTokenCount = 8, totalTokenCount = 128 },
                    })
                    : throw new InvalidOperationException("Unexpected fixture HTTP route: " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
