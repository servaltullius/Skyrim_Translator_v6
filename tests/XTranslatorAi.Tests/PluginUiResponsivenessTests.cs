using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Tests;

public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public void PluginCancel_IsFirstStatusItem_OutsideEveryDisabledWorkspaceAncestor()
    {
        // Inspect source XAML without constructing or showing any WPF window.
        var root = XDocument.Load(MainWindowSourcePath("MainWindow.xaml")).Root!;
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var cancel = Assert.Single(root.Descendants(wpf + "Button")
            .Where(element => (string?)element.Attribute("Command") == "{Binding CancelPluginIoCommand}"));
        Assert.Equal("{Binding IsPluginIoBusy}", (string?)cancel.Attribute("IsEnabled"));
        var status = Assert.Single(cancel.Ancestors(wpf + "StatusBar"));
        Assert.Same(status.Elements(wpf + "StatusBarItem").First(), cancel.Parent);
        Assert.DoesNotContain(cancel.Ancestors(), element => element.Name == wpf + "Expander"
            || (string?)element.Attribute("IsEnabled") is "{Binding IsWorkspaceInteractive}" or "False");

        var toolbar = Assert.Single(root.Elements(wpf + "Grid").Elements(wpf + "Border")
            .Where(element => (string?)element.Attribute("Grid.Row") == "0"));
        Assert.Equal("{Binding IsWorkspaceInteractive}", (string?)toolbar.Attribute("IsEnabled"));
        var tabs = Assert.Single(root.Descendants(wpf + "TabControl"));
        Assert.Equal("{Binding IsWorkspaceInteractive}", (string?)tabs.Attribute("IsEnabled"));
        var editors = root.Descendants().Where(element => element.Name.LocalName is
            "Button" or "TextBox" or "PasswordBox" or "ComboBox" or "CheckBox" or "Slider");
        Assert.All(editors.Where(element => element != cancel), element =>
            Assert.Contains(toolbar, element.Ancestors()));
        // Window-wide disabling is reserved for Close; it must not override the cancel binding.
        Assert.DoesNotContain("IsWorkspaceInteractive", File.ReadAllText(MainWindowSourcePath("MainWindow.xaml.cs")));
    }

    [Fact]
    public Task PluginLoad_SynchronousReaderPreparation_AllowsStaCancellationBeforeAnyImport()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var input = Path.Combine(fixture.Root, "cancel-before-import.esp");
            await File.WriteAllBytesAsync(input, PluginProjectIntegrationTests.CreateMinimalPlugin());
            var service = (ProjectWorkspaceService)typeof(MainViewModel)
                .GetField("_projectWorkspaceService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Vm)!;
            var uiContext = SynchronizationContext.Current!;
            var uiThread = Environment.CurrentManagedThreadId;
            using var cancellation = new CancellationTokenSource();
            using var uiResponded = new ManualResetEventSlim();
            var probeThread = 0;
            var uiRanDuringPreparation = false;
            var options = new PluginReadOptions(ArchivePaths: new ProbeArchivePaths(() =>
            {
                probeThread = Environment.CurrentManagedThreadId;
                uiContext.Post(_ => { cancellation.Cancel(); uiResponded.Set(); }, null);
                // This bounded synchronous phase models slow parsing. A captured UI context cannot
                // process the posted cancel until this wait expires, so the old implementation fails.
                uiRanDuringPreparation = uiResponded.Wait(TimeSpan.FromSeconds(5));
            }));
            var request = new ProjectWorkspaceService.LoadFromPluginRequest(input, options,
                "korean", "utf-8", fixture.Vm.SelectedModel, "", false);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadFromPluginAsync(request, cancellation.Token));
            Assert.NotEqual(uiThread, probeThread);
            Assert.True(uiRanDuringPreparation, "The STA cancellation callback was blocked by reader preparation.");
            Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.sqlite", SearchOption.AllDirectories));
            Assert.Null(fixture.State.Db);
        });

    [Fact]
    public Task PluginExport_SynchronousSqliteSnapshot_AllowsStaCancelAndPublishesNothing()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var db = fixture.State.Db!;
            var source = fixture.State.PluginDocument!.Info.InputPath;
            var originalBytes = await File.ReadAllBytesAsync(source);
            var output = Path.Combine(fixture.Root, "canceled-export");
            fixture.Ui.SavePath = output;
            var uiContext = SynchronizationContext.Current!;
            var uiThread = Environment.CurrentManagedThreadId;
            using var uiResponded = new ManualResetEventSlim();
            var probeThread = 0;
            var uiRanDuringQuery = false;
            var workspaceWasLocked = false;
            var connection = (SqliteConnection)typeof(ProjectDb)
                .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(db)!;
            connection.CreateFunction<string, string>("ui_cancel_probe", key =>
            {
                probeThread = Environment.CurrentManagedThreadId;
                uiContext.Post(_ =>
                {
                    workspaceWasLocked = fixture.Vm.IsPluginIoBusy && !fixture.Vm.IsWorkspaceInteractive;
                    fixture.Vm.CancelPluginIoCommand.Execute(null);
                    uiResponded.Set();
                }, null);
                // Run inside the actual SQLite snapshot query, not an artificial async delay.
                uiRanDuringQuery = uiResponded.Wait(TimeSpan.FromSeconds(5));
                return key;
            });
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    ALTER TABLE PluginStringBinding RENAME TO ExportProbeBindings;
                    CREATE VIEW PluginStringBinding AS
                      SELECT StringId, ui_cancel_probe(FieldKey) AS FieldKey, FieldJson FROM ExportProbeBindings;
                    UPDATE StringEntry SET DestText='Translated sword', Status=$done;
                    """;
                command.Parameters.AddWithValue("$done", (int)StringEntryStatus.Done);
                await command.ExecuteNonQueryAsync();
            }
            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null);
            Assert.NotEqual(uiThread, probeThread);
            Assert.True(uiRanDuringQuery, "The STA cancellation callback was blocked by a synchronous SQLite query.");
            Assert.True(workspaceWasLocked);
            Assert.False(fixture.Vm.IsPluginIoBusy);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            Assert.Same(db, fixture.State.Db);
            Assert.Contains("중지", fixture.Vm.StatusMessage);
            Assert.False(Directory.Exists(output));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(source));
        });

    [Fact]
    public Task PluginOpen_LateCancelAdoptsCommittedRows_AndRaisesCollectionEventsOnSta()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var previousDb = fixture.State.Db;
            var uiThread = Environment.CurrentManagedThreadId;
            var eventThreads = new List<int>();
            var cancellationRequested = false;
            fixture.Vm.Entries.CollectionChanged += (_, _) =>
            {
                eventThreads.Add(Environment.CurrentManagedThreadId);
                if (!cancellationRequested)
                {
                    // ResetProjectState runs only after the service commits. Request cancellation
                    // before SetPluginWorkspace, exercising the commit/adopt boundary without a window.
                    cancellationRequested = true;
                    fixture.Vm.CancelPluginIoCommand.Execute(null);
                }
            };
            fixture.Ui.OpenPath = fixture.State.PluginDocument!.Info.InputPath;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);
            Assert.True(cancellationRequested);
            Assert.NotEmpty(eventThreads);
            Assert.All(eventThreads, thread => Assert.Equal(uiThread, thread));
            Assert.NotSame(previousDb, fixture.State.Db);
            Assert.True(fixture.Vm.IsProjectLoaded);
            Assert.False(fixture.Vm.IsPluginIoBusy);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            Assert.Contains("플러그인은 열렸습니다", fixture.Vm.StatusMessage);
            var saved = Assert.Single(await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None));
            var visible = Assert.Single(fixture.Vm.Entries);
            Assert.Equal(saved.Id, visible.Id);
            Assert.Equal(saved.SourceText, visible.SourceText);
            Assert.Equal(saved.DestText, visible.DestText);
        });

    private static string MainWindowSourcePath(string file)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "XTranslatorAi.App", file));

    private sealed class ProbeArchivePaths(Action onEnumerate) : IReadOnlyList<string>
    {
        public int Count => 0;
        public string this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<string> GetEnumerator()
        {
            onEnumerate();
            return Enumerable.Empty<string>().GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
