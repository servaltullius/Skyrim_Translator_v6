using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Rows held by agent mode (<c>Skipped</c> + <c>agent_hold</c> note) used to show only as plain 건너뜀 in the GUI.
/// Now their status, message column, tooltip and source-pane banner show the hold reason and clear when edited or
/// sent back for retranslation.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task AgentHeldRow_ShowsReasonInGuiForPlugin_AndClearsOnEditOrRetranslate()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var plugin = Path.Combine(fixture.Root, "Held.esp");
            await File.WriteAllBytesAsync(plugin, PluginProjectIntegrationTests.CreateMinimalPlugin("Line One"));
            fixture.Ui.OpenPath = plugin;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);

            var initialDb = fixture.State.Db!;
            var id = Assert.Single(fixture.Vm.Entries).Id;
            await initialDb.HoldStringsAsync(new[] { (id, "성적 표현으로 보류") }, CancellationToken.None);

            // Reopen the plugin to load persisted agent_hold notes into the GUI view models.
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);
            var row = Assert.Single(fixture.Vm.Entries);

            Assert.True(row.IsAgentHeld);
            Assert.Equal("건너뜀(보류)", row.StatusText);
            Assert.Equal("에이전트 보류: 성적 표현으로 보류", row.AgentHoldDisplayText);
            Assert.Equal("에이전트 보류: 성적 표현으로 보류", row.UserFacingErrorMessage);
            Assert.Contains("에이전트 보류: 성적 표현으로 보류", row.RowToolTip);

            // Search box finds held rows by reason text.
            fixture.Vm.EntryFilterText = "성적 표현";
            Assert.Single(fixture.Vm.EntriesView.Cast<object>());
            fixture.Vm.EntryFilterText = "";

            // Editing a held row clears its hold marker.
            await fixture.Vm.CommitDestEditAsync(row, "직접 번역한 줄");
            Assert.False(row.IsAgentHeld);
            Assert.Equal("직접 수정", row.StatusText);
            Assert.Equal("", row.AgentHoldDisplayText);
            Assert.Equal("", row.UserFacingErrorMessage);
        });

    [Fact]
    public Task AgentHeldRow_ShowsReasonInGuiForXml_AndClearsOnRetranslate()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var xmlPath = Path.Combine(fixture.Root, "HeldXml.xml");
            await File.WriteAllTextAsync(xmlPath, XmlProjectReopenTests.Document("HeldXml.esp",
                XmlProjectReopenTests.Row("1", "Line One", "") + XmlProjectReopenTests.Row("2", "Line Two", "")), new System.Text.UTF8Encoding(true));
            fixture.Ui.OpenPath = xmlPath;
            await fixture.Vm.OpenXmlCommand.ExecuteAsync(null);
            var rows = fixture.Vm.Entries.ToList();
            // A hold given no reason is saved as the mark itself (AgentSession.HoldAsync): the GUI showed "에이전트 보류: 보류".
            await fixture.State.Db!.HoldStringsAsync(new[] { (rows[0].Id, "표현 확인 필요"), (rows[1].Id, AgentSession.HeldMark) }, CancellationToken.None);

            // Reopen the same XML to exercise LoadEntriesAsync loading agent_hold notes.
            await fixture.Vm.OpenXmlCommand.ExecuteAsync(null);
            var first = fixture.Vm.Entries[0];
            var second = fixture.Vm.Entries[1];

            Assert.True(first.IsAgentHeld);
            Assert.Equal("에이전트 보류: 표현 확인 필요", first.AgentHoldDisplayText);
            Assert.True(second.IsAgentHeld);
            Assert.Equal("건너뜀(보류)", second.StatusText);
            Assert.Equal("에이전트 보류", second.AgentHoldDisplayText);
            Assert.Equal("에이전트 보류", second.UserFacingErrorMessage);

            // Sending a held row back for retranslation clears its hold marker.
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            await fixture.Vm.RetranslateSelectedCommand.ExecuteAsync(new List<object> { second });
            Assert.False(second.IsAgentHeld);
            Assert.Equal("대기", second.StatusText);
            Assert.Equal("", second.AgentHoldDisplayText);
            Assert.Equal("", second.UserFacingErrorMessage);

            // A row that leaves 건너뜀 any other way (a translation run) is no longer held either: the project drops the
            // note then, and a reason left on the row came back if the row was skipped again.
            first.Status = StringEntryStatus.Done;
            first.Status = StringEntryStatus.Skipped;
            Assert.False(first.IsAgentHeld);
            Assert.Equal("건너뜀", first.StatusText);
        });
}
