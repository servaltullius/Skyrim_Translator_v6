using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// A plugin moved while the window shows its project, then opened in its new place in that same window: the usual way
/// a GUI user meets a move. The open cannot retire the old project there (the window holds its DB until the new one
/// is adopted), so it is retired right after the window lets go; left in place, moving the plugin back opened it again.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task MovedPlugin_OpenedInTheWindowShowingItsProject_RetiresTheOldProject_AndMovedBackOpensWhatWasDoneThere()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var pathA = Path.Combine(fixture.Root, "A", "Moved.esp");
            var pathB = Path.Combine(fixture.Root, "B", "Moved.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(pathA)!);
            Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
            await File.WriteAllBytesAsync(pathA, PluginProjectIntegrationTests.CreateMinimalPlugin("Line One"));

            fixture.Ui.OpenPath = pathA;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);
            var dbA = fixture.State.Db!.DbPath;
            await fixture.Vm.CommitDestEditAsync(Assert.Single(fixture.Vm.Entries), "A에서 번역");

            File.Move(pathA, pathB);
            fixture.Ui.OpenPath = pathB;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);
            var dbB = fixture.State.Db!.DbPath;

            Assert.NotEqual(dbA, dbB);
            Assert.Contains("옮기기 전 위치", fixture.Vm.StatusMessage);
            Assert.DoesNotContain("치우지 못했습니다", fixture.Vm.StatusMessage);
            Assert.False(File.Exists(dbA));
            Assert.True(File.Exists(dbA + ".moved"));
            var atB = Assert.Single(fixture.Vm.Entries);
            Assert.Equal("A에서 번역", atB.DestText);
            await fixture.Vm.CommitDestEditAsync(atB, "B에서 고침");

            File.Move(pathB, pathA);
            fixture.Ui.OpenPath = pathA;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);

            Assert.Equal(dbA, fixture.State.Db!.DbPath);
            Assert.Equal("B에서 고침", Assert.Single(fixture.Vm.Entries).DestText);
            Assert.DoesNotContain("치우지 못했습니다", fixture.Vm.StatusMessage);
            Assert.False(File.Exists(dbB));
            Assert.True(File.Exists(dbB + ".moved"));
        });
}
