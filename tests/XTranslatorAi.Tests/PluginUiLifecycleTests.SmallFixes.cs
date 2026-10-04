using System.Reflection;
using System.Windows.Input;
using System.Xml.Linq;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>Smaller UI defects from the 2026-10-04 audit.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Theory]
    [InlineData("ProjectGlossaryTabView.xaml", "GlossarySourceTerm")]
    [InlineData("ProjectGlossaryTabView.xaml", "GlossaryTargetTerm")]
    [InlineData("GlobalGlossaryTabView.xaml", "GlobalGlossarySourceTerm")]
    [InlineData("GlobalGlossaryTabView.xaml", "GlobalGlossaryTargetTerm")]
    [InlineData("GlobalTranslationMemoryTabView.xaml", "FranchiseTranslationMemorySourceText")]
    [InlineData("GlobalTranslationMemoryTabView.xaml", "FranchiseTranslationMemoryDestText")]
    public void AddButtons_AreEnabledWhileTyping_NotOnlyAfterTheTextBoxLosesFocus(string view, string property)
    {
        // "추가" is enabled from these two texts; with the default LostFocus trigger it stayed disabled while typing.
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var box = Assert.Single(XDocument.Load(MainWindowSourcePath(Path.Combine("Views", view))).Root!.Descendants(wpf + "TextBox"),
            element => ((string?)element.Attribute("Text"))?.StartsWith("{Binding " + property + ",", StringComparison.Ordinal) == true
                       || (string?)element.Attribute("Text") == "{Binding " + property + "}");
        Assert.Equal("{Binding " + property + ", UpdateSourceTrigger=PropertyChanged}", (string?)box.Attribute("Text"));
    }

    [Fact]
    public Task WaitingCount_IsTheRowsTheNextRunTranslates_AfterAnEditAndWhenARunEnds()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Sword");
            await fixture.State.Db!.UpdateStringTranslationAsync(rows[2].Id, "", StringEntryStatus.Error, "E999", CancellationToken.None);
            rows[2].Status = StringEntryStatus.Error;

            // The start of a run counts failed rows as waiting (it translates them again); saving an edit used
            // to recount only pending rows, so 대기 dropped from 2 to 1 here.
            rows[0].EditableDestText = "철검";
            fixture.Vm.SelectedEntry = rows[1];
            await LeftRowCommits(fixture.Vm);
            Assert.Equal((1, 2), (fixture.Vm.DoneCount, fixture.Vm.PendingCount));

            // A stopped run leaves its unfinished rows pending; the counters are recounted from the rows.
            rows[1].Status = StringEntryStatus.InProgress;
            fixture.Vm.PendingCount = 0;
            await (Task)typeof(MainViewModel).GetMethod("FinishTranslationUiStateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(fixture.Vm, new object?[] { true, null })!;
            Assert.Equal(StringEntryStatus.Pending, rows[1].Status);
            Assert.Equal((1, 2), (fixture.Vm.DoneCount, fixture.Vm.PendingCount));
        });

    [Fact]
    public Task DeletingTheActiveSavedApiKey_ClearsTheKeyField_SoItIsNotSavedAgain()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture(initialSettings: new AppSettings(ApiKey: "key-a",
                ApiKeys: new[] { new SavedApiKey("A", "key-a"), new SavedApiKey("B", "key-b") }));
            var vm = fixture.Vm;
            Assert.Equal("key-a", vm.ApiKey);
            Assert.Equal("A", vm.SelectedSavedApiKey!.Name);

            vm.ClearSavedApiKeyCommand.Execute(null);

            Assert.Equal("", vm.ApiKey);
            Assert.Equal("B", Assert.Single(vm.SavedApiKeys).Name);
            var saved = fixture.Settings.Load();
            Assert.Null(saved.ApiKey);
            Assert.Equal("key-b", Assert.Single(saved.ApiKeys!).ApiKey);

            // A key typed after picking a saved one is not the deleted key and stays.
            vm.SelectedSavedApiKey = vm.SavedApiKeys[0];
            vm.ApiKey = "key-c";
            vm.ClearSavedApiKeyCommand.Execute(null);
            Assert.Equal("key-c", vm.ApiKey);
            Assert.Empty(vm.SavedApiKeys);
            Assert.Equal("key-c", fixture.Settings.Load().ApiKey);
        });

    [Fact]
    public Task CompareAndModelListRefresh_AreDisabledWhileTranslatingOrSwitching()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var vm = fixture.Vm;
            // These used to stay enabled and silently do nothing during a run.
            var commands = new ICommand[]
            {
                vm.RunCompare1Command, vm.RunCompare2Command, vm.RunCompare3Command, vm.RunCompareAllCommand, vm.RefreshModelsCommand,
            };
            var notifications = new int[commands.Length];
            for (var i = 0; i < commands.Length; i++)
            {
                var index = i;
                commands[i].CanExecuteChanged += (_, _) => notifications[index]++;
            }
            Assert.All(commands, command => Assert.True(command.CanExecute(null)));

            vm.IsTranslating = true;
            Assert.All(commands, command => Assert.False(command.CanExecute(null)));
            Assert.All(notifications, count => Assert.True(count > 0));
            vm.IsTranslating = false;
            Assert.All(commands, command => Assert.True(command.CanExecute(null)));

            Array.Clear(notifications);
            vm.IsPluginIoBusy = true;
            Assert.All(commands, command => Assert.False(command.CanExecute(null)));
            Assert.All(notifications, count => Assert.True(count > 0));
            vm.IsPluginIoBusy = false;
            Assert.All(commands, command => Assert.True(command.CanExecute(null)));
        });
}
