using XTranslatorAi.App.ViewModels;
using XTranslatorAi.App.Services;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>Files dropped on the window open like the toolbar buttons, without the open dialog.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task DroppedPlugin_OpensAsAProject_WithoutTheOpenDialog()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var dropped = Path.Combine(fixture.Root, "dropped", "Dropped.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(dropped)!);
            await File.WriteAllBytesAsync(dropped, PluginProjectIntegrationTests.CreateMinimalPlugin());

            Assert.True(fixture.Vm.CanOpenDroppedFile(dropped));
            await fixture.Vm.OpenDroppedFileAsync(dropped).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(dropped, fixture.State.PluginDocument!.Info.InputPath);
            Assert.True(fixture.Vm.IsProjectLoaded);
            Assert.Single(fixture.Vm.Entries);
            Assert.False(fixture.Ui.NetworkOrDialogUsed);

            // The dropped plugin has its own project DB; release it so the fixture can delete its folder.
            var droppedDb = ProjectPaths.GetPluginProjectDbPath(dropped, fixture.State.PluginDocument.Info.Options, "korean", "utf-8", fixture.Root);
            await fixture.State.DisposeDbAsync();
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(droppedDb);
        });

    [Fact]
    public Task DroppedFiles_ThatAreNotPluginsOrXml_AreNotOpened()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var text = Path.Combine(fixture.Root, "notes.txt");
            await File.WriteAllTextAsync(text, "not a plugin");
            var originalDb = fixture.State.Db;

            Assert.False(fixture.Vm.CanOpenDroppedFile(text));
            Assert.False(fixture.Vm.CanOpenDroppedFile(Path.Combine(fixture.Root, "missing.esp")));
            Assert.False(fixture.Vm.CanOpenDroppedFile(null));
            await fixture.Vm.OpenDroppedFileAsync(text);

            Assert.Same(originalDb, fixture.State.Db);
            Assert.True(MainViewModel.IsPluginFile(@"C:\mods\Example.ESM"));
            Assert.True(MainViewModel.IsXmlFile(@"C:\mods\Example_english_korean.xml"));
            Assert.False(MainViewModel.IsPluginFile(@"C:\mods\Example.bsa"));
        });

    [Fact]
    public Task OpeningFiles_IsUnavailableWhileTranslating_SoADropNeverStopsTheRun()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var originalDb = fixture.State.Db;
            var dropped = Path.Combine(fixture.Root, "old", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(dropped)!);
            await File.WriteAllBytesAsync(dropped, PluginProjectIntegrationTests.CreateMinimalPlugin("강철 장검"));
            fixture.Ui.OpenPath = dropped;
            var notified = 0;
            fixture.Vm.OpenXmlCommand.CanExecuteChanged += (_, _) => notified++;
            fixture.Vm.OpenPluginCommand.CanExecuteChanged += (_, _) => notified++;

            fixture.Vm.IsTranslating = true;
            Assert.Equal(2, notified);
            Assert.False(fixture.Vm.OpenXmlCommand.CanExecute(null));
            Assert.False(fixture.Vm.OpenPluginCommand.CanExecute(null));
            // The old Korean release dropped on "이전 번역 참고" cannot be linked during a run either; the window
            // shows "지금은 열 수 없습니다" instead of opening it as the project.
            Assert.False(fixture.Vm.CanLinkDroppedPreviousTranslation(dropped));
            Assert.False(fixture.Vm.CanOpenDroppedFile(dropped));

            await fixture.Vm.OpenDroppedFileAsync(dropped).WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(originalDb, fixture.State.Db);
            Assert.True(fixture.Vm.IsTranslating);
            Assert.False(fixture.Ui.NetworkOrDialogUsed);

            fixture.Vm.IsTranslating = false;
            Assert.True(fixture.Vm.CanOpenDroppedFile(dropped));
            Assert.True(fixture.Vm.OpenPluginCommand.CanExecute(null));
        });

    [Fact]
    public Task DroppedPlugin_OnThePreviousTranslationRow_IsLinkedAsTheEarlierTranslation()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            var oldRelease = Path.Combine(fixture.Root, "old", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            await File.WriteAllBytesAsync(oldRelease, PluginProjectIntegrationTests.CreateMinimalPlugin("강철 장검"));

            Assert.False(fixture.Vm.CanLinkDroppedPreviousTranslation(Path.ChangeExtension(oldRelease, ".xml")));
            Assert.True(fixture.Vm.ProjectContextTab.CanLinkDroppedPreviousTranslation(oldRelease));
            await fixture.Vm.ProjectContextTab.LinkDroppedPreviousTranslationAsync(oldRelease).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("강철 장검", row.PreviousTranslation);
            Assert.True(fixture.Vm.HasPreviousTranslation);
            Assert.False(fixture.Ui.NetworkOrDialogUsed);
        });
}
