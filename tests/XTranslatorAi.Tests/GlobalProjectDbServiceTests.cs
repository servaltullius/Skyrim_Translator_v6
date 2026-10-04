using System.Text;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// Every failure to open the global DB used to become a silent null: translation ran without the global glossary,
/// series TM and official names, project loads copied the whole built-in glossary into the project, and a global
/// glossary whose first open failed after creating the file was never seeded.
/// </summary>
public sealed class GlobalProjectDbServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-global-db-" + Guid.NewGuid().ToString("N"));
    private readonly List<GlobalProjectDbService> _services = new();
    private string GlobalRoot => Path.Combine(_root, "global");
    private string GlobalDbPath => Path.Combine(GlobalRoot, "global-glossary.sqlite");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(GlobalRoot);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var service in _services)
        {
            await service.DisposeAsync();
        }

        foreach (var db in Directory.GetFiles(_root, "*.sqlite", SearchOption.AllDirectories))
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
        }

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task OpenFailure_IsReportedAndRetriedOnTheNextCall()
    {
        // A folder where the DB file should be: SQLite cannot open it.
        Directory.CreateDirectory(GlobalDbPath);
        var service = NewService();

        Assert.Null(await service.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None));
        Assert.NotNull(service.GetLastOpenError(BethesdaFranchise.ElderScrolls));
        Assert.False(service.IsOpen(BethesdaFranchise.ElderScrolls));

        Directory.Delete(GlobalDbPath);
        Assert.NotNull(await service.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None));
        Assert.Null(service.GetLastOpenError(BethesdaFranchise.ElderScrolls));
    }

    [Fact]
    public async Task EmptyGlossaryLeftByAnEarlierFailedOpen_IsSeeded()
    {
        await (await ProjectDb.OpenOrCreateAsync(GlobalDbPath, CancellationToken.None)).DisposeAsync();

        var db = await NewService().GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

        Assert.Contains(await db!.GetGlossaryAsync(CancellationToken.None), e => e.SourceTerm == "Whiterun");
    }

    [Fact]
    public async Task GlossaryTheUserEmptied_StaysEmpty()
    {
        var first = NewService();
        var db = await first.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
        foreach (var entry in await db!.GetGlossaryAsync(CancellationToken.None))
        {
            await db.DeleteGlossaryEntryAsync(entry.Id, CancellationToken.None);
        }

        await first.DisposeAsync();
        var reopened = await NewService().GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

        Assert.Empty(await reopened!.GetGlossaryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProjectLoad_WithoutTheGlobalDb_DoesNotCopyTheBuiltInGlossaryIntoTheProject()
    {
        Directory.CreateDirectory(GlobalDbPath);
        var service = NewService();
        var xml = Path.Combine(_root, "input.xml");
        await File.WriteAllTextAsync(xml, XmlProjectReopenTests.Document("Mod.esp", XmlProjectReopenTests.Row("1", "Whiterun", "")),
            new UTF8Encoding(true));

        var result = await new ProjectWorkspaceService(service, Path.Combine(_root, "projects")).LoadFromXmlAsync(
            new ProjectWorkspaceService.LoadFromXmlRequest(xml, BethesdaFranchise.ElderScrolls, "model", "", false), CancellationToken.None);
        await using var project = result.Db;

        Assert.Empty(await project.GetGlossaryAsync(CancellationToken.None));
        Assert.NotNull(service.GetLastOpenError(BethesdaFranchise.ElderScrolls));
    }

    private GlobalProjectDbService NewService()
    {
        var service = new GlobalProjectDbService(new BuiltInGlossaryService(), GlobalRoot);
        _services.Add(service);
        return service;
    }
}
