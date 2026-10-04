using System.Net.Http;
using System.Collections.Concurrent;
using System.Reflection;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public class TranslationUiLifecycleTests
{
    [Fact]
    public Task ManualEdit_UpdatesProgressImmediately_WithoutCountingRepeatedSavesTwice()
        => RunOnSta(async () =>
        {
            await using var fixture = new ViewModelFixture();
            var db = await ProjectDb.OpenOrCreateAsync(Path.Combine(fixture.DirectoryPath, "manual-edit.sqlite"), CancellationToken.None);
            fixture.State.SetWorkspace(db, new XTranslatorXmlInfo("test.esp", "english", "korean", "2", false, ""), "test.xml");
            await db.BulkInsertStringsAsync(new[] { (0, (string?)null, (string?)null, (string?)null,
                (string?)null, (string?)null, "Source", "Source", StringEntryStatus.Pending, "<String />") }, CancellationToken.None);
            var row = Assert.Single(await db.GetStringsAsync(10, 0, CancellationToken.None));
            var entry = new StringEntryViewModel(row.Id, row.OrderIndex) { SourceText = row.SourceText, DestText = row.DestText ?? "" };
            fixture.State.SetEntries(new[] { entry });
            fixture.ViewModel.TotalCount = 1;
            fixture.ViewModel.PendingCount = 1;

            await fixture.ViewModel.CommitDestEditAsync(entry, "수동 번역");
            Assert.Equal(1, fixture.ViewModel.DoneCount);
            Assert.Equal(0, fixture.ViewModel.PendingCount);
            Assert.Equal(1d, fixture.ViewModel.ProgressRatio);
            await fixture.ViewModel.CommitDestEditAsync(entry, "수정한 번역");
            Assert.Equal(1, fixture.ViewModel.DoneCount);
            Assert.Equal(0, fixture.ViewModel.PendingCount);
            var saved = Assert.Single(await db.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal("수정한 번역", saved.DestText);
            Assert.Equal(StringEntryStatus.Edited, saved.Status);
        });

    [Fact]
    public Task FailedManualEdit_DoesNotMarkTheRowCompleteOrChangeProgress()
        => RunOnSta(async () =>
        {
            await using var fixture = new ViewModelFixture();
            var db = await ProjectDb.OpenOrCreateAsync(Path.Combine(fixture.DirectoryPath, "failed-edit.sqlite"), CancellationToken.None);
            fixture.State.SetWorkspace(db, new XTranslatorXmlInfo("test.esp", "english", "korean", "2", false, ""), "test.xml");
            var entry = new StringEntryViewModel(1, 0) { SourceText = "Source", DestText = "Source" };
            fixture.State.SetEntries(new[] { entry });
            fixture.ViewModel.TotalCount = 1;
            fixture.ViewModel.PendingCount = 1;
            await db.DisposeAsync();

            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => fixture.ViewModel.CommitDestEditAsync(entry, "저장 실패"));
                Assert.Equal("Source", entry.DestText);
                Assert.Equal(StringEntryStatus.Pending, entry.Status);
                Assert.Equal(0, fixture.ViewModel.DoneCount);
                Assert.Equal(1, fixture.ViewModel.PendingCount);
            }
            finally
            {
                // The fixture must not dispose the deliberately closed database again.
                fixture.State.Clear();
            }
        });

    [Fact]
    public Task LateRowFromPreviousRun_DoesNotChangeNewProjectWithSameRowId()
        => RunOnSta(async () =>
        {
            await using var fixture = new ViewModelFixture();
            var vm = fixture.ViewModel;
            var entry = new StringEntryViewModel(1, 0) { SourceText = "new source", DestText = "new translation" };
            fixture.State.SetEntries(new[] { entry });
            SetField(vm, "_rowUpdateGeneration", 2L);

            Enqueue(vm, 1, "stale translation");
            Invoke(vm, "DrainRowUpdates");
            Assert.Equal("new translation", entry.DestText);
            Assert.Equal(StringEntryStatus.Pending, entry.Status);

            Enqueue(vm, 2, "current translation");
            Invoke(vm, "DrainRowUpdates");
            Assert.Equal("current translation", entry.DestText);
            Assert.Equal(StringEntryStatus.Done, entry.Status);
        });

    [Fact]
    public Task ResetProject_DiscardsQueuedRowsAndResetsSelectionAndCounters()
        => RunOnSta(async () =>
        {
            await using var fixture = new ViewModelFixture();
            var vm = fixture.ViewModel;
            vm.TotalCount = 10;
            vm.DoneCount = 5;
            vm.PendingCount = 5;
            vm.SelectedEntry = new StringEntryViewModel(1, 0);
            Enqueue(vm, 0, "old queued translation");
            Invoke(vm, "ResetProjectState");
            var next = new StringEntryViewModel(1, 0);
            fixture.State.SetEntries(new[] { next });
            Invoke(vm, "DrainRowUpdates");

            Assert.Equal("", next.DestText);
            Assert.Null(vm.SelectedEntry);
            Assert.Equal(0, vm.TotalCount);
            Assert.Equal(0, vm.DoneCount);
            Assert.Equal(0, vm.PendingCount);
        });

    [Fact]
    public Task Resume_PreservesCompletedAndEditedRowsAndSelectsOnlyUnfinishedRows()
        => RunOnSta(async () =>
        {
            await using var fixture = new ViewModelFixture();
            var db = await ProjectDb.OpenOrCreateAsync(Path.Combine(fixture.DirectoryPath, "resume.sqlite"), CancellationToken.None);
            fixture.State.SetWorkspace(db, new XTranslatorXmlInfo("test.esp", "english", "korean", "2", false, ""), "test.xml");
            var statuses = new[] { StringEntryStatus.Done, StringEntryStatus.Edited, StringEntryStatus.InProgress, StringEntryStatus.Error };
            await db.BulkInsertStringsAsync(statuses.Select((status, i) => (
                OrderIndex: i, ListAttr: (string?)null, PartialAttr: (string?)null, AttributesJson: (string?)null,
                Edid: (string?)null, Rec: (string?)null, SourceText: $"Source {i}", DestText: $"Dest {i}",
                Status: status, RawStringXml: "<String />")), CancellationToken.None);
            var rows = await db.GetStringsAsync(10, 0, CancellationToken.None);
            fixture.State.SetEntries(rows.Select(row => new StringEntryViewModel(row.Id, row.OrderIndex)
            { Status = row.Status, SourceText = row.SourceText, DestText = row.DestText ?? "" }).ToArray());

            await (Task)Invoke(fixture.ViewModel, "PrepareTranslationsForResumeAsync", CancellationToken.None)!;
            var pending = await (Task<IReadOnlyList<long>>)Invoke(fixture.ViewModel, "LoadPendingIdsAsync", CancellationToken.None)!;
            var after = await db.GetStringsAsync(10, 0, CancellationToken.None);

            Assert.Equal("Dest 0", after[0].DestText);
            Assert.Equal(StringEntryStatus.Done, after[0].Status);
            Assert.Equal("Dest 1", after[1].DestText);
            Assert.Equal(StringEntryStatus.Edited, after[1].Status);
            Assert.Equal(StringEntryStatus.Pending, after[2].Status);
            Assert.Equal(new[] { after[2].Id, after[3].Id }, pending);
            Assert.Equal(2, fixture.ViewModel.DoneCount);
        });

    [Fact]
    public Task Close_WaitsForProjectToolCleanupBeforeDisposingDb()
        => RunOnSta(async () =>
        {
            await using var fixture = new ViewModelFixture();
            var db = await ProjectDb.OpenOrCreateAsync(Path.Combine(fixture.DirectoryPath, "close.sqlite"), CancellationToken.None);
            fixture.State.SetWorkspace(db, new XTranslatorXmlInfo("test.esp", "english", "korean", "2", false, ""), "test.xml");
            var tracker = (ProjectOperationTracker)typeof(MainViewModel)
                .GetField("_projectOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.ViewModel)!;
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = tracker.RunAsync(async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally
                {
                    canceled.TrySetResult();
                    await cleanup.Task;
                    Assert.Equal(0, await db.GetStringCountAsync(CancellationToken.None));
                }
            });

            var closing = fixture.ViewModel.TryCloseWorkspaceAsync();
            await canceled.Task;
            Assert.False(closing.IsCompleted);
            Assert.Same(db, fixture.State.Db);
            Assert.False(fixture.ViewModel.IsWorkspaceInteractive);
            cleanup.SetResult();
            Assert.True(await closing);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.Null(fixture.State.Db);
        });

    [Fact]
    public Task Close_CancelsCompareHttpRequestAndWaitsForCommandCompletion()
        => RunOnSta(async () =>
        {
            using var handler = new BlockingHttpHandler();
            await using var fixture = new ViewModelFixture(handler);
            var vm = fixture.ViewModel;
            vm.ApiKey = "unit-test-key";
            vm.IsProjectLoaded = true;
            vm.SelectedEntry = new StringEntryViewModel(1, 0) { SourceText = "Iron Sword", Rec = "WEAP:FULL" };
            vm.CompareIncludeProjectGlossary = false;
            vm.CompareIncludeGlobalGlossary = false;
            vm.CompareIncludeFranchiseTranslationMemory = false;
            vm.EnablePromptCache = false;
            vm.EnableRepairPass = false;
            var statuses = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Compare1Status)) statuses.Add(vm.Compare1Status);
            };
            var comparing = vm.RunCompare1Command.ExecuteAsync(null);
            await handler.Started.Task;

            Assert.True(await vm.TryCloseWorkspaceAsync());
            await comparing;
            Assert.True(handler.CancellationObserved);
            Assert.False(vm.Compare1IsRunning);
            // The slot reported the stop before closing cleared the closed project's compare results.
            Assert.Equal(new[] { "실행 중...", "중지됨", "" }, statuses);
        });

    /// <summary>Translating without the global glossary, series TM and official names used to happen silently.</summary>
    [Fact]
    public Task StartTranslation_AsksBeforeTranslatingWithoutTheGlobalDb()
        => RunOnSta(async () =>
        {
            await using var fixture = new ViewModelFixture(unopenableGlobalDb: true);
            var vm = fixture.ViewModel;
            var db = await ProjectDb.OpenOrCreateAsync(Path.Combine(fixture.DirectoryPath, "no-global.sqlite"), CancellationToken.None);
            fixture.State.SetWorkspace(db, new XTranslatorXmlInfo("test.esp", "english", "korean", "2", false, ""), "test.xml");
            await db.BulkInsertStringsAsync(new[] { (0, (string?)null, (string?)null, (string?)null,
                (string?)null, (string?)"WEAP:FULL", "Iron Sword", "", StringEntryStatus.Pending, "<String />") }, CancellationToken.None);
            vm.ApiKey = "unit-test-key";
            vm.IsProjectLoaded = true;

            await vm.StartTranslationCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "전체 DB를 열 수 없음" }, fixture.Ui.Titles);
            Assert.StartsWith("번역을 시작하지 않았습니다", vm.StatusMessage);
            Assert.False(vm.IsTranslating);
            Assert.Equal(StringEntryStatus.Pending, Assert.Single(await db.GetStringsAsync(10, 0, CancellationToken.None)).Status);
        });

    private static void Enqueue(MainViewModel vm, long generation, string text)
        => typeof(MainViewModel).GetMethod("OnRowUpdatedAsync", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(long), typeof(long), typeof(StringEntryStatus), typeof(string) }, null)!
            .Invoke(vm, new object[] { generation, 1L, StringEntryStatus.Done, text });

    private static object? Invoke(MainViewModel vm, string method, params object[] args)
        => typeof(MainViewModel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);

    private static void SetField(MainViewModel vm, string field, object value)
        => typeof(MainViewModel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);

    private static Task RunOnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new StaTestSynchronizationContext();
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

    private sealed class StaTestSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        public void Run()
        {
            foreach (var work in _queue.GetConsumingEnumerable()) work.Callback(work.State);
        }
        public void Complete() => _queue.CompleteAdding();
        public void Dispose() => _queue.Dispose();
    }

    private sealed class ViewModelFixture : IAsyncDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly GlobalProjectDbService _globalDb;
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "TulliusTranslator-tests", Guid.NewGuid().ToString("N"));
        public MainViewModel ViewModel { get; }
        public ProjectState State => (ProjectState)typeof(MainViewModel).GetField("_projectState", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ViewModel)!;

        public NoUi Ui { get; } = new();

        public ViewModelFixture(HttpMessageHandler? handler = null, bool unopenableGlobalDb = false)
        {
            _httpClient = new HttpClient(handler ?? new NoNetworkHandler());
            Directory.CreateDirectory(DirectoryPath);
            if (unopenableGlobalDb)
            {
                // A folder where the global DB file should be: SQLite cannot open it.
                Directory.CreateDirectory(Path.Combine(DirectoryPath, "global", "global-glossary.sqlite"));
            }

            var builtIn = new BuiltInGlossaryService();
            // Never the user's global DB under %LOCALAPPDATA%.
            var globalDb = _globalDb = new GlobalProjectDbService(builtIn, Path.Combine(DirectoryPath, "global"));
            var glossary = new ProjectGlossaryService(new GlossaryImportService(new GlossaryFileService()));
            ViewModel = new MainViewModel(_httpClient, new MainViewModelServices(
                new AppSettingsStore(Path.Combine(DirectoryPath, "settings.json")), new ApiCallLogService(),
                new SystemPromptBuilder(), Ui, new BundledFranchiseTmSeedService(DirectoryPath), globalDb,
                glossary, new GlobalGlossaryService(globalDb, glossary), new FranchiseTranslationMemoryService(globalDb),
                new ProjectWorkspaceService(globalDb), new TranslationRunnerService(globalDb), new CompareTranslationService(glossary)));
        }

        public async ValueTask DisposeAsync()
        {
            await State.DisposeDbAsync();
            await _globalDb.DisposeAsync();
            _httpClient.Dispose();
            foreach (var db in Directory.GetFiles(DirectoryPath, "*.sqlite", SearchOption.AllDirectories))
            {
                TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
            }

            try { Directory.Delete(DirectoryPath, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Network calls are forbidden in UI lifecycle tests.");
    }

    private sealed class BlockingHttpHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    private sealed class NoUi : IUiInteractionService
    {
        public List<string> Titles { get; } = new();
        public UiMessageBoxResult ShowMessage(string message, string title, UiMessageBoxButton button, UiMessageBoxImage image, UiMessageBoxResult defaultResult)
        {
            Titles.Add(title);
            return UiMessageBoxResult.Ok;
        }
        public string? ShowOpenFileDialog(OpenFileDialogRequest request) => null;
        public string? ShowSaveFileDialog(SaveFileDialogRequest request) => null;
        public bool TryOpenFolder(string path) => false;
    }
}
