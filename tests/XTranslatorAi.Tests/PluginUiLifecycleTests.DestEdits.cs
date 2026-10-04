using System.Reflection;
using System.Xml.Linq;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Xml;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// Text typed into the translation editor used to reach the DB only through "번역문 저장": selecting another row,
/// exporting, opening another file or closing kept the edit in the grid while the export and the next run used
/// the old DB text. Edits are now saved when the row is left and before anything that reads the DB.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task LeavingAnEditedRow_SavesTheEditAsEdited_AndNothingElse()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Sword");
            var db = fixture.State.Db!;

            rows[0].EditableDestText = "내가 고친 철검";
            Assert.True(rows[0].HasUnsavedDestEdit);
            fixture.Vm.SelectedEntry = rows[1];
            await LeftRowCommits(fixture.Vm);

            var saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal((StringEntryStatus.Edited, "내가 고친 철검"), (saved[0].Status, saved[0].DestText));
            Assert.Equal(StringEntryStatus.Edited, rows[0].Status);
            Assert.False(rows[0].HasUnsavedDestEdit);
            Assert.Equal((1, 2), (fixture.Vm.DoneCount, fixture.Vm.PendingCount));

            // Typing and then restoring the saved text is no change, so leaving the row writes nothing.
            rows[1].EditableDestText = "임시";
            rows[1].EditableDestText = "";
            fixture.Vm.SelectedEntry = rows[2];
            await LeftRowCommits(fixture.Vm);
            fixture.Vm.SelectedEntry = null;
            await LeftRowCommits(fixture.Vm);
            saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal((StringEntryStatus.Pending, ""), (saved[1].Status, saved[1].DestText));
            Assert.Equal((StringEntryStatus.Pending, ""), (saved[2].Status, saved[2].DestText));

            // Translation results and tools set DestText from what they wrote to the DB; that is not an edit.
            rows[2].DestText = "엘프 검";
            Assert.False(rows[2].HasUnsavedDestEdit);
        });

    [Fact]
    public Task ExportXml_WithAnEditStillInTheEditor_SavesItAndWritesItToTheFile()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var row = Assert.Single(await LoadXmlWorkspaceAsync(fixture, "Iron Sword"));
            row.EditableDestText = "철검";
            var output = Path.Combine(fixture.Root, "Test.translated.xml");
            fixture.Ui.SavePath = output;

            Assert.True(fixture.Vm.ExportXmlCommand.CanExecute(null));
            await fixture.Vm.ExportXmlCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("철검", Assert.Single(XDocument.Load(output).Descendants("String")).Element("Dest")!.Value);
            Assert.Equal(StringEntryStatus.Edited, row.Status);
            Assert.Same(row, fixture.Vm.SelectedEntry);
        });

    [Fact]
    public Task ExportPlugin_WithAnEditStillInTheEditor_SavesItAndWritesItToTheEsp()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            fixture.Vm.SelectedEntry = row;
            row.EditableDestText = "철검";
            var output = Path.Combine(fixture.Root, "translated");
            fixture.Ui.SavePath = output;

            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            var translated = await PluginReader.ReadAsync(Path.Combine(output, "Test.esp"), new PluginReadOptions(), CancellationToken.None);
            Assert.Equal("철검", Assert.Single(translated.Fields).SourceText);
            Assert.Equal(StringEntryStatus.Edited, row.Status);
        });

    [Fact]
    public Task OpeningAnotherFile_SavesTheEditToTheProjectItBelongsTo()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var row = Assert.Single(await LoadXmlWorkspaceAsync(fixture, "Iron Sword"));
            row.EditableDestText = "철검";
            var other = Path.Combine(fixture.Root, "Other.xml");
            await File.WriteAllTextAsync(other, "not an xTranslator XML");

            await fixture.Vm.OpenDroppedFileAsync(other).WaitAsync(TimeSpan.FromSeconds(10));

            // The open failed after the old project was closed; the edit was saved into the old project's DB first.
            Assert.Null(fixture.State.Db);
            await using var reopened = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
            var saved = Assert.Single(await reopened.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal((StringEntryStatus.Edited, "철검"), (saved.Status, saved.DestText));
        });

    [Fact]
    public Task Close_SavesAnEditStillInTheEditor()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var row = Assert.Single(await LoadXmlWorkspaceAsync(fixture, "Iron Sword"));
            row.EditableDestText = "철검";

            Assert.True(await fixture.Vm.TryCloseWorkspaceAsync());

            Assert.Null(fixture.State.Db);
            await using var reopened = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
            var saved = Assert.Single(await reopened.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal((StringEntryStatus.Edited, "철검"), (saved.Status, saved.DestText));
        });

    [Fact]
    public Task LeavingARowOfAReplacedProject_DoesNotWriteItsEditIntoTheNewProject()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var oldRow = Assert.Single(await LoadXmlWorkspaceAsync(fixture, "Iron Sword"));
            var oldDb = fixture.State.Db!;
            oldRow.EditableDestText = "철검";

            // Another project whose first row has the same Id replaces the workspace before the selection moves.
            var newDbPath = Path.Combine(fixture.Root, "other.sqlite");
            var newDb = await ProjectDb.OpenOrCreateAsync(newDbPath, CancellationToken.None);
            await InsertXmlRowsAsync(newDb, "Glass Sword");
            var newRow = await ShowXmlWorkspaceAsync(fixture, newDb);
            Assert.Equal(oldRow.Id, newRow.Id);
            await LeftRowCommits(fixture.Vm);

            Assert.Equal((StringEntryStatus.Pending, ""), Summary(Assert.Single(await newDb.GetStringsAsync(10, 0, CancellationToken.None))));
            Assert.Equal(("", StringEntryStatus.Pending), (newRow.DestText, newRow.Status));

            await fixture.State.DisposeDbAsync();
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(newDbPath);
            await oldDb.DisposeAsync();

            static (StringEntryStatus, string) Summary(StringEntry row) => (row.Status, row.DestText);
        });

    [Fact]
    public Task StartTranslation_SavesATypedEditFirst_AndDoesNotTranslateOverIt()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "철검" };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            fixture.Vm.SelectedEntry = row;
            row.EditableDestText = "내가 고친 철검";

            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(handler.Requests);
            Assert.Equal((StringEntryStatus.Edited, "내가 고친 철검"), (row.Status, row.DestText));
            var saved = Assert.Single(await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal((StringEntryStatus.Edited, "내가 고친 철검"), (saved.Status, saved.DestText));
        });

    [Fact]
    public Task Retranslate_SavesATypedEditFirst_AndTreatsItAsAManualEdit()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            await fixture.State.Db!.UpdateStringTranslationAsync(row.Id, "철검", StringEntryStatus.Done, null, CancellationToken.None);
            row.DestText = "철검";
            row.Status = StringEntryStatus.Done;
            fixture.Vm.SelectedEntry = row;
            row.EditableDestText = "강철 검";

            // Without the save the row still looked finished, and "Yes" cleared the typed text unasked.
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Cancel);
            await fixture.Vm.RetranslateSelectedCommand.ExecuteAsync(new List<object> { row }).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal((StringEntryStatus.Edited, "강철 검"), (row.Status, row.DestText));
            var saved = Assert.Single(await fixture.State.Db.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal((StringEntryStatus.Edited, "강철 검"), (saved.Status, saved.DestText));
        });

    [Fact]
    public Task TranslationEditors_AreReadOnlyWhileTranslating()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var changed = new List<string?>();
            fixture.Vm.StringsTab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            Assert.False(fixture.Vm.StringsTab.IsTranslating);

            // A row the run finishes replaces the editor text, so typing during a run would be lost.
            fixture.Vm.IsTranslating = true;
            Assert.True(fixture.Vm.StringsTab.IsTranslating);
            Assert.True(fixture.Vm.LqaTab.IsTranslating);
            Assert.Contains(nameof(fixture.Vm.StringsTab.IsTranslating), changed);
            fixture.Vm.IsTranslating = false;

            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            foreach (var view in new[] { "StringsTabView.xaml", "LqaTabView.xaml" })
            {
                var boxes = XDocument.Load(MainWindowSourcePath(Path.Combine("Views", view))).Root!.Descendants(wpf + "TextBox").ToList();
                var editor = Assert.Single(boxes, box => ((string?)box.Attribute("Text"))?.Contains("SelectedEntry.EditableDestText") == true);
                Assert.Equal("{Binding IsTranslating}", (string?)editor.Attribute("IsReadOnly"));
                Assert.DoesNotContain(boxes, box => ((string?)box.Attribute("Text"))?.Contains("SelectedEntry.DestText") == true);
            }
        });

    private static Task LeftRowCommits(MainViewModel vm)
        => (Task)typeof(MainViewModel).GetField("_leftRowCommits", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;

    /// <summary>An xTranslator XML project in the fixture's DB, with the first row selected as after opening it.</summary>
    private static async Task<IReadOnlyList<StringEntryViewModel>> LoadXmlWorkspaceAsync(Fixture fixture, params string[] sources)
    {
        var db = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
        await InsertXmlRowsAsync(db, sources);
        await ShowXmlWorkspaceAsync(fixture, db);
        return fixture.Vm.Entries.ToList();
    }

    private static Task InsertXmlRowsAsync(ProjectDb db, params string[] sources)
        => db.BulkInsertStringsAsync(sources.Select((source, i) => (OrderIndex: i, ListAttr: (string?)null, PartialAttr: (string?)null,
            AttributesJson: (string?)null, Edid: (string?)$"TestItem{i}", Rec: (string?)"WEAP:FULL", SourceText: source, DestText: "",
            Status: StringEntryStatus.Pending,
            RawStringXml: $"<String><EDID>TestItem{i}</EDID><REC>WEAP:FULL</REC><Source>{source}</Source><Dest></Dest></String>")),
            CancellationToken.None);

    private static async Task<StringEntryViewModel> ShowXmlWorkspaceAsync(Fixture fixture, ProjectDb db)
    {
        fixture.State.SetWorkspace(db, new XTranslatorXmlInfo("Test.esp", "english", "korean", "2", false, ""), Path.Combine(fixture.Root, "Test.xml"));
        var entries = (await db.GetStringsAsync(100, 0, CancellationToken.None))
            .Select(row => new StringEntryViewModel(row.Id, row.OrderIndex)
            {
                Edid = row.Edid, Rec = row.Rec, SourceText = row.SourceText, DestText = row.DestText, Status = row.Status,
            })
            .ToList();
        fixture.State.SetEntries(entries);
        fixture.Vm.TotalCount = entries.Count;
        fixture.Vm.DoneCount = 0;
        fixture.Vm.PendingCount = entries.Count;
        fixture.Vm.IsProjectLoaded = true;
        fixture.Vm.SelectedEntry = entries[0];
        return entries[0];
    }
}
