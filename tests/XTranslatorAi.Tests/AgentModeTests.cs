using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Tests.TestSupport;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

/// <summary>
/// Agent mode: the GUI's translation run with the agent app's model writing the answers. A request covers every
/// remaining row (as the GUI's run does), a recording stops the run without splitting or leaving 번역 중 rows, and an
/// answer is used only where the run asks for it.
/// </summary>
public sealed class AgentModeTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agent-mode-tests", Guid.NewGuid().ToString("N"));
    private GlobalProjectDbService _global = null!;
    private AgentSession _session = null!;
    private AgentProject _project = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var plugin = Path.Combine(_root, "Mod", "Thalmor.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        await File.WriteAllBytesAsync(plugin, Plugin());
        _global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(_root, "global"));
        _session = new AgentSession(Path.Combine(_root, "work"), _global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects"));
        (_project, _, _, _) = await _session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _global.DisposeAsync();
        TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(_project.DbPath);
        foreach (var db in _extraDbs) TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>tullius.cmd hands the arguments to cmd, which splits an unquoted "A&amp;B"; a file of arguments keeps them whole.</summary>
    [Fact]
    public async Task ArgumentFile_StandsForTheArgumentsInIt()
    {
        var file = Path.Combine(_root, "args.txt");
        await File.WriteAllLinesAsync(file, new[] { "open", @"G:\Mods\Fists & Fury ^1\Fists.esp", "", "  --batch-size  " });

        var args = AgentCli.ExpandArgumentFiles(new[] { "@" + file, "20" });

        Assert.Equal(new[] { "open", @"G:\Mods\Fists & Fury ^1\Fists.esp", "--batch-size", "20" }, args);
        Assert.Equal(new[] { "check", "@" }, AgentCli.ExpandArgumentFiles(new[] { "check", "@" }));
        Assert.Throws<AgentModeException>(() => AgentCli.ExpandArgumentFiles(new[] { "@" + file + ".missing" }));
    }

    /// <summary>
    /// An answer as each PowerShell writes it: 7 in UTF-8, 5.1 in UTF-16 (Out-File, &gt;) or CP949 (Set-Content on Korean
    /// Windows). Each reads back as written; text already broken is refused rather than saved.
    /// </summary>
    [Fact]
    public async Task AnswerFiles_AreReadInTheEncodingTheAppWroteThem()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        const string answer = "{\"translations\":[{\"id\":1,\"text\":\"파린스, 같이 가자 & 끝.\"}]}";
        var encodings = new (string Name, System.Text.Encoding Encoding)[]
        {
            ("utf8", new System.Text.UTF8Encoding(false)), ("utf8bom", new System.Text.UTF8Encoding(true)),
            ("utf16", System.Text.Encoding.Unicode), ("cp949", System.Text.Encoding.GetEncoding(949)),
        };
        foreach (var (name, encoding) in encodings)
        {
            var path = Path.Combine(_root, $"answer-{name}.txt");
            await File.WriteAllTextAsync(path, answer, encoding);
            Assert.Equal(answer, await AgentCli.ReadAgentFileAsync(path, CancellationToken.None));
        }

        var args = Path.Combine(_root, "args-cp949.txt");
        await File.WriteAllTextAsync(args, "lookup\r\nMeridia\r\n검은 & 손\r\n", System.Text.Encoding.GetEncoding(949));
        Assert.Equal(new[] { "lookup", "Meridia", "검은 & 손" }, AgentCli.ExpandArgumentFiles(new[] { "@" + args }));

        var broken = Path.Combine(_root, "answer-broken.txt");
        await File.WriteAllTextAsync(broken, "\uFFFD\uFFFD 깨진 답", new System.Text.UTF8Encoding(false));
        await Assert.ThrowsAsync<AgentModeException>(() => AgentCli.ReadAgentFileAsync(broken, CancellationToken.None));
    }

    /// <summary>
    /// The Codex trial: a level decided with a request waiting dropped that request's answer. The request is made again
    /// at once, and request.md names the instructions file by its full path (--work moved it from the relative work).
    /// </summary>
    [Fact]
    public async Task WaitingRequest_IsMadeAgain_AndNamesItsInstructionsByFullPath()
    {
        Assert.Null((await _session.RemakePendingAsync(_project, CancellationToken.None)).Report);
        await _session.NextAsync(_project, CancellationToken.None);

        var (remade, lanes) = await _session.RemakePendingAsync(_project, CancellationToken.None);

        Assert.NotNull(remade?.Next);
        Assert.Equal(0, lanes);
        Assert.NotNull(_session.Pending(_project));
        Assert.Contains(_session.InstructionsPath(_project), await File.ReadAllTextAsync(_session.RequestPath(_project)));
    }

    /// <summary>A PowerShell function passes "--id 1008,383" as separate arguments; the list is gathered whole.</summary>
    [Fact]
    public void IdList_SplitIntoArguments_IsGatheredWhole()
    {
        var command = AgentCommand.Parse(new List<string> { "proj", "--id", "1008", "383,275", "--work", Path.Combine(_root, "w"), "extra" });

        Assert.Equal("1008,383,275", command.Ids);
        Assert.Equal(new[] { "proj", "extra" }, command.Rest);
    }

    /// <summary>
    /// The MCP server: one JSON-RPC reply per request line and none for a notification, a version the client asked for
    /// when it is known, and errors as replies rather than a stopped server. The request tool reads the waiting request.
    /// </summary>
    [Fact]
    public async Task McpServer_AnswersEachRequest_AndItsRequestToolReadsTheWaitingRequest()
    {
        await _session.NextAsync(_project, CancellationToken.None);
        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects") });
        var lines = new[]
        {
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            "{not json",
            """{"jsonrpc":"2.0","id":3,"method":"prompts/list"}""",
            """{"jsonrpc":"2.0","id":"r4","method":"tools/call","params":{"name":"nosuch","arguments":{}}}""",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"request","arguments":{"project":"Thalmor"}}}""",
            // A method that is not a string ended the server; an unknown project came back as a protocol error.
            """{"jsonrpc":"2.0","id":7,"method":42}""",
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"request","arguments":{"project":"NoSuchProject"}}}""",
        };
        var output = new StringWriter();

        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(string.Join("\n", lines)), output));

        var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => JsonDocument.Parse(r).RootElement).ToList();
        Assert.Equal(8, replies.Count);
        Assert.Equal(-32600, replies[6].GetProperty("error").GetProperty("code").GetInt32());
        Assert.True(replies[7].GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("2025-03-26", replies[0].GetProperty("result").GetProperty("protocolVersion").GetString());
        var tools = replies[1].GetProperty("result").GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
        Assert.Contains("submit", tools.Keys);
        Assert.Equal(new[] { "project", "answer" }, tools["submit"].GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(-32700, replies[2].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32601, replies[3].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("r4", replies[4].GetProperty("id").GetString());
        Assert.True(replies[4].GetProperty("result").GetProperty("isError").GetBoolean());
        var request = replies[5].GetProperty("result");
        Assert.False(request.GetProperty("isError").GetBoolean());
        Assert.Equal(await File.ReadAllTextAsync(_session.RequestPath(_project)), request.GetProperty("content")[0].GetProperty("text").GetString());

        // The answer goes through a file of its own, gone after the call: lanes' servers share the work folder, and a
        // fixed answer.txt let one lane's answer be saved into another lane's request. The call goes to a project that
        // does not exist: a real submit would open the user's global DB (the server's commands have no test root).
        var submit = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 6, method = "tools/call",
            @params = new { name = "submit", arguments = new { project = "NoSuchProject", answer = "번역" } },
        });
        var submitted = new StringWriter();
        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(submit), submitted));
        Assert.True(JsonDocument.Parse(submitted.ToString()).RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "work", ".mcp")));
    }

    // Codex ran in a kit folder that was not named agent-kit, its shell failed, and the guide tool answered
    // "없는 안내: translate" for a name that exists, so it stopped. The server now also reads the kit it is started in,
    // and when there is none says where it looked.
    [Fact]
    public async Task McpServer_GuideReadsTheRenamedKitItRunsIn_AndSaysWhereItLookedOtherwise()
    {
        var exeFolder = Path.Combine(_root, "translator");
        var kit = Path.Combine(exeFolder, "kit-codex");
        Directory.CreateDirectory(kit);
        var beside = Path.Combine(exeFolder, "agent-kit");
        // Another project's AGENTS.md is not a kit: Antigravity starts the global server in whatever folder is open.
        await File.WriteAllTextAsync(Path.Combine(kit, "AGENTS.md"), "rules");
        Assert.Equal(beside, AgentMcpServer.KitFolder(exeFolder, kit));
        await File.WriteAllTextAsync(Path.Combine(kit, "tullius.cmd"), "");
        Assert.Equal(kit, AgentMcpServer.KitFolder(exeFolder, kit));
        Directory.CreateDirectory(beside);
        Assert.Equal(beside, AgentMcpServer.KitFolder(exeFolder, kit));

        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects") });
        var call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "guide", arguments = new { name = "translate" } },
        });
        var output = new StringWriter();
        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), output));
        var result = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains(Path.Combine("agent-kit", "guide-translate.md"), text);
        Assert.Contains("TulliusTranslator.exe 바로 옆", text);
    }

    // Antigravity reads one global MCP config; pasting the mcp-config text into it was its one manual setup step.
    // The install keeps the other servers, copies a file with content aside, and leaves a file that is not JSON alone.
    [Fact]
    public void AntigravityInstall_AddsTulliusBesideTheOtherServers()
    {
        var config = Path.Combine(_root, "gemini", "config", "mcp_config.json");
        var first = AgentMcpServer.InstallInto(config, @"C:\Tullius\TulliusTranslator.exe");
        Assert.True(first.Changed);
        Assert.Null(first.Backup);
        var written = JsonDocument.Parse(File.ReadAllText(config)).RootElement.GetProperty("mcpServers").GetProperty("tullius");
        Assert.Equal(@"C:\Tullius\TulliusTranslator.exe", written.GetProperty("command").GetString());
        Assert.Equal(new[] { "agent", "mcp" }, written.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        Assert.False(AgentMcpServer.InstallInto(config, @"C:\Tullius\TulliusTranslator.exe").Changed);

        File.WriteAllText(config, """{"mcpServers":{"other":{"command":"x.exe"}},"theme":"dark"}""");
        var moved = AgentMcpServer.InstallInto(config, @"D:\Moved\TulliusTranslator.exe");
        Assert.True(moved.Changed);
        Assert.True(File.Exists(moved.Backup));
        var root = JsonDocument.Parse(File.ReadAllText(config)).RootElement;
        Assert.Equal("x.exe", root.GetProperty("mcpServers").GetProperty("other").GetProperty("command").GetString());
        Assert.Equal("dark", root.GetProperty("theme").GetString());
        Assert.Equal(@"D:\Moved\TulliusTranslator.exe", root.GetProperty("mcpServers").GetProperty("tullius").GetProperty("command").GetString());

        File.WriteAllText(config, "{ broken");
        Assert.Throws<AgentModeException>(() => AgentMcpServer.InstallInto(config, @"C:\Tullius\TulliusTranslator.exe"));
        Assert.Equal("{ broken", File.ReadAllText(config));
    }

    [Fact]
    public async Task Request_CoversTheRemainingRows_SoTheDialogueAroundALineIsInIt()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        Assert.Equal(0, report.Saved);
        Assert.Empty(await RowsWithStatusAsync(StringEntryStatus.InProgress));
        await Assert.ThrowsAsync<AgentModeException>(() => _session.NextAsync(_project, CancellationToken.None));

        var request = await AnswerUntilAsync(report, r => Items(r).Any(item => item.Text.StartsWith("Your loyalty", StringComparison.Ordinal)));

        // The next line of the scene is not translated in this request (batch size 2), but it is the first line's context.
        Assert.DoesNotContain(Items(request), item => item.Text.StartsWith("Then we have", StringComparison.Ordinal));
        Assert.Contains("Then we have an understanding.", UserText(request));
    }

    /// <summary>
    /// IDE Thalmor's first request: the run saved a line from the game dictionary first, then asked with that line still
    /// in its neighbor's context. The next run (the one an answer is replayed in) leaves saved lines out of the context,
    /// so it asked differently and the answer was dropped.
    /// </summary>
    [Fact]
    public async Task FirstRequest_AfterTheRunSavedGameLines_IsTheOneTheNextRunAsks()
    {
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", Array.Empty<string>(), new[]
            {
                new PluginField("INFO/00001002/0/NAM1/0", 0, "INFO", "NAM1", 0x1002, null, 0, 0, "Then we have an understanding.",
                    PluginStringTableKind.IlStrings, 1),
            }, new Dictionary<(PluginStringTableKind, uint), string> { [(PluginStringTableKind.IlStrings, 1)] = "그럼 서로 이해한 걸로 하지." }),
        });
        using var saved = new MemoryStream();
        game.Save(saved);
        var bytes = saved.ToArray();
        var session = new AgentSession(Path.Combine(_root, "work-game"), _global,
            new GameStringService(Path.Combine(_root, "game"), () => new MemoryStream(bytes)), new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects-game"));
        // Dialogue only, so the first request is the one with the saved line in its context.
        var plugin = Path.Combine(_root, "Mod", "Dialogue.esp");
        await File.WriteAllBytesAsync(plugin, Header().Concat(DialogueGroup(TopicChildren(0x1000,
            Record("INFO", 0x1001, Sub("NAM1", Z("Your loyalty to the Dominion has been noted."))),
            Record("INFO", 0x1002, Sub("NAM1", Z("Then we have an understanding."))),
            Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>.")))))).ToArray());
        var (project, _, _, _) = await session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
        _extraDbs.Add(project.DbPath);

        var report = await session.NextAsync(project, CancellationToken.None);
        Assert.Equal(1, report.Saved);

        report = await session.SubmitAsync(project, Answer(report.Next!), CancellationToken.None);

        Assert.Null(report.Problem);
        Assert.True(report.Saved > 0);
    }

    /// <summary>
    /// Lanes: each sub-agent runs its own share of the rows (a dialogue kept whole) with its own request, and an
    /// answer stays good while other lanes save theirs, even when a saved row names something its lines mention.
    /// </summary>
    [Fact]
    public async Task Lanes_SplitTheRows_AndAnAnswerSurvivesTheOtherLanesSaves()
    {
        var plugin = Path.Combine(_root, "Mod", "Echo.esp");
        await File.WriteAllBytesAsync(plugin, Header().Concat(DialogueGroup(
                TopicChildren(0x1000,
                    Record("INFO", 0x1001, Sub("NAM1", Z("I told Drelorea that the road is clear."))),
                    Record("INFO", 0x1002, Sub("NAM1", Z("We will find Drelorea at the inn.")))),
                TopicChildren(0x1100,
                    Record("INFO", 0x1101, Sub("NAM1", Z("You should ask Drelorea about the old road."))))))
            .ToArray());
        var (project, _, _, _) = await _session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 5, CancellationToken.None);
        _extraDbs.Add(project.DbPath);
        var one = project with { Lane = AgentLane.Parse("1/2") };
        var two = project with { Lane = AgentLane.Parse("2/2") };

        var first = await _session.NextAsync(one, CancellationToken.None);
        // The lane count is written once, not by every step of every lane (two lanes met on the file and one step failed).
        var lanesFile = Path.Combine(_root, "work", project.Key, "lanes.json");
        File.SetLastWriteTimeUtc(lanesFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = await _session.NextAsync(two, CancellationToken.None);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(lanesFile));

        var ids1 = Ids(first.Next!);
        var ids2 = Ids(second.Next!);
        Assert.Equal(2, ids1.Count);
        Assert.Single(ids2);
        Assert.Empty(ids1.Intersect(ids2));
        Assert.NotEqual(_session.RequestPath(one), _session.RequestPath(two));
        Assert.Throws<AgentModeException>(() => AgentLane.Parse("3/2"));
        await Assert.ThrowsAsync<AgentModeException>(() => _session.StatusAsync(project with { Lane = AgentLane.Parse("1/3") }, CancellationToken.None));

        // Lane 2 saves its spelling of the name first; lane 1's answer, written before, is still the one its run asks for.
        var done2 = await _session.SubmitAsync(two, JsonSerializer.Serialize(new { translations = new[] { new { id = ids2[0], text = "드렐로레아에게 옛길에 대해 물어봐." } } }), CancellationToken.None);
        Assert.Null(done2.Problem);
        Assert.Null(done2.Next);
        var done1 = await _session.SubmitAsync(one, Answer(first.Next!), CancellationToken.None);
        Assert.Null(done1.Problem);
        Assert.Equal(2, done1.Saved);
        Assert.Null(done1.Next);

        // A different number of lanes is refused while a lane has a request waiting, and fine once none has.
        await _session.StatusAsync(project with { Lane = AgentLane.Parse("1/3") }, CancellationToken.None);
    }

    /// <summary>
    /// Review mode: an existing Korean release fills the rows it translates, which the agent then reviews; a row it lacks
    /// stays 대기 and is the only one the next request asks for.
    /// </summary>
    [Fact]
    public async Task KoreanRelease_FillsItsRows_AndOnlyTheRestAreAsked()
    {
        var korean = Path.Combine(_root, "Korean", "Thalmor.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(korean)!);
        await File.WriteAllBytesAsync(korean, Header().Concat(DialogueGroup(TopicChildren(0x1000,
                Record("INFO", 0x1001, Sub("NAM1", Z("자치령에 대한 네 충성은 기록해 두었다."))),
                Record("INFO", 0x1002, Sub("NAM1", Z("그럼 서로 이해한 걸로 하지."))),
                Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>."))))))
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEcho")), Sub("FULL", Z("Aetherial Echo")))))
            .ToArray());

        var link = await _session.LinkKoreanAsync(_project, korean, null, CancellationToken.None);

        Assert.Equal(2, link.Filled);
        Assert.Equal(2, link.Untranslated);
        Assert.Empty(link.DifferentFromGame);
        Assert.Equal(2, link.Match.NotTranslated + link.Match.SameAsSource);
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            Assert.Contains(rows, r => r.Status == StringEntryStatus.Done && r.DestText == "그럼 서로 이해한 걸로 하지.");
        }

        var report = await _session.NextAsync(_project, CancellationToken.None);
        var asked = Items(report.Next!).Select(item => item.Text).ToList();
        Assert.Equal(2, report.PendingBefore);
        Assert.DoesNotContain(asked, text => text.StartsWith("Then we have", StringComparison.Ordinal));

        // With a request waiting, the rows it covers are not filled under it.
        await Assert.ThrowsAsync<AgentModeException>(() => _session.LinkKoreanAsync(_project, korean, null, CancellationToken.None));
    }

    /// <summary>
    /// A Korean release made for an earlier version of the mod: a row whose English changed since was filled with the
    /// old text as 완료. With the release's English original, only rows whose English is the same are filled; without
    /// it, rows whose numbers are not the release's are left to translate (with the release's line as a reference).
    /// </summary>
    [Fact]
    public async Task KoreanRelease_LeavesRowsWhoseEnglishChanged()
    {
        static byte[] Bites(string fireName, string fire, string frostName, string frost)
            => Header().Concat(Group(
                    Record("MGEF", 0x3001, Sub("EDID", Z("aaFireBite")), Sub("FULL", Z(fireName)), Sub("DNAM", Z(fire))),
                    Record("MGEF", 0x3002, Sub("EDID", Z("aaFrostBite")), Sub("FULL", Z(frostName)), Sub("DNAM", Z(frost)))))
                .ToArray();
        var korean = Path.Combine(_root, "Korean", "Bites.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(korean)!);
        await File.WriteAllBytesAsync(korean, Bites("불꽃 물기", "15의 화염 피해를 줍니다.", "냉기 물기", "10의 냉기 피해를 줍니다."));
        var english = Path.Combine(_root, "English", "Bites.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(english)!);
        await File.WriteAllBytesAsync(english, Bites("Fire Bite", "Deals 15 fire damage.", "Frost Nip", "Deals 10 frost damage."));
        var current = Bites("Fire Bite", "Deals 25 fire damage.", "Frost Bite", "Deals 10 frost damage.");

        // Without the English original: the magnitude that changed is caught by its number.
        var byNumbers = await OpenAsync("Bites.esp", current);
        var link = await _session.LinkKoreanAsync(byNumbers, korean, null, CancellationToken.None);

        Assert.False(link.ComparedWithEnglish);
        Assert.Equal(3, link.Filled);
        var fireRow = Assert.Single(link.EnglishChanged);
        Assert.Equal(1, link.Untranslated);
        await using (var db = await ProjectDb.OpenOrCreateAsync(byNumbers.DbPath, CancellationToken.None))
        {
            var row = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.Id == fireRow);
            Assert.Equal((StringEntryStatus.Pending, "Deals 25 fire damage."), (row.Status, row.SourceText));
        }

        // With it: the renamed spell is caught too, and the line whose English is the same is filled.
        var byEnglish = await OpenAsync("Bites2.esp", current);
        var compared = await _session.LinkKoreanAsync(byEnglish, korean, null, CancellationToken.None, english);

        Assert.True(compared.ComparedWithEnglish);
        Assert.Equal(2, compared.Filled);
        Assert.Equal(2, compared.EnglishChanged.Count);
        await using (var db = await ProjectDb.OpenOrCreateAsync(byEnglish.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            Assert.Contains(rows, r => r.Status == StringEntryStatus.Done && r.DestText == "10의 냉기 피해를 줍니다.");
            Assert.Contains(rows, r => r.Status == StringEntryStatus.Pending && r.SourceText == "Frost Bite");
        }

        Assert.True(AgentSession.NumbersFit("Deals 10 damage for 10 seconds.", "10초 동안 10의 피해를 줍니다."));
        Assert.False(AgentSession.NumbersFit("Deals 10 damage for 10 seconds.", "10의 피해를 줍니다."));
        // A font size the release changed for Korean is markup, not text; "1,000" is one number.
        Assert.True(AgentSession.NumbersFit("<font size='60'>Deals 10 damage.</font>", "<font size='48'>10의 피해를 줍니다.</font>"));
        Assert.True(AgentSession.NumbersFit("Costs 1,000 gold.", "1000골드가 듭니다."));
        Assert.False(AgentSession.NumbersFit("Costs 1,500 gold.", "1,000골드가 듭니다."));

        // A wrong --english path is refused before anything is linked.
        var wrong = await OpenAsync("Bites3.esp", current);
        await Assert.ThrowsAsync<AgentModeException>(() =>
            _session.LinkKoreanAsync(wrong, korean, null, CancellationToken.None, Path.Combine(_root, "English", "NoSuch.esp")));
        await using (var db = await ProjectDb.OpenOrCreateAsync(wrong.DbPath, CancellationToken.None))
        {
            Assert.Null(await db.GetPreviousTranslationSourceAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Answers_AreSavedOneRequestAtATime_UntilThePluginIsDone()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        var requests = 0;
        while (report.Next != null)
        {
            requests++;
            Assert.True(requests < 20, "the run kept asking");
            report = await _session.SubmitAsync(_project, Answer(report.Next), CancellationToken.None);
            Assert.Null(report.Problem);
        }

        Assert.True(report.Finished);
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
        Assert.All(rows, row => Assert.Equal(StringEntryStatus.Done, row.Status));
        Assert.Contains(rows, row => row.DestText == "번역: Then we have an understanding.");
        Assert.Null(_session.Pending(_project));
    }

    [Fact]
    public async Task Recording_MakesOneCall_AndLeavesNoRowInProgress()
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var outcome = await AgentTranslationRun.RunAsync(db, new AppSettings(),
            new AgentTranslationRun.Options(AgentTranslationRun.DefaultModelProfile, 1, 15000), (_, _) => null, _global, null,
            CancellationToken.None);

        Assert.NotNull(outcome.Next);
        Assert.Equal(1, outcome.GenerationCalls);
        Assert.Null(outcome.Error);
        Assert.Equal(outcome.PendingBefore, outcome.PendingAfter);
        Assert.Empty(await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.InProgress }, CancellationToken.None));
    }

    // Lanes run at the same time in their own processes: a lane's run used to put the other lane's 번역 중 batch back
    // to 대기, so that lane's split dropped the rows and its answer no longer fit.
    [Fact]
    public async Task LaneRun_LeavesTheOtherLanesRowsInProgress()
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
        var otherLane = ids[^1];
        await db.UpdateStringStatusesAsync(new[] { otherLane }, StringEntryStatus.InProgress, null, CancellationToken.None);

        var options = new AgentTranslationRun.Options(AgentTranslationRun.DefaultModelProfile, 1, 15000) { Lane = ids.Take(ids.Count - 1).ToHashSet() };
        var outcome = await AgentTranslationRun.RunAsync(db, new AppSettings(), options, (_, _) => null, _global, null, CancellationToken.None);

        Assert.NotNull(outcome.Next);
        Assert.Equal(new[] { otherLane }, await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.InProgress }, CancellationToken.None));
    }

    // A misnamed key ({"id": 12, "translation": ".."}) read as no text, and the row was saved empty as 수정됨.
    [Fact]
    public void EditWithoutText_IsRefused()
    {
        var edits = System.Text.Json.JsonSerializer.Deserialize<List<AgentCli.EditLine>>(
            """[{"id": 12, "translation": "새 이름"}, {"order": 3, "text": " "}, {"id": 14, "text": "고친 번역"}]""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(new[] { "id 12", "순번 3" }, AgentCli.WithoutText(edits));
    }

    [Fact]
    public async Task AnswerNotInTheRequestedFormat_IsRefused_AndTheRequestStays()
    {
        var request = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);

        var error = await Assert.ThrowsAsync<AgentModeException>(() => _session.SubmitAsync(_project, "번역했습니다!", CancellationToken.None));

        Assert.Contains("형식", error.Message);
        Assert.Equal(request.Hash, _session.Pending(_project)?.Hash);
    }

    /// <summary>The rows changed after the request was written: the answer fits no call of the new run.</summary>
    [Fact]
    public async Task AnswerToAStaleRequest_IsNotWritten_AndANewRequestIsMade()
    {
        var first = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.UpdateStringTranslationAsync(Ids(first)[0], "사용자가 고친 번역", StringEntryStatus.Edited, null, CancellationToken.None);
        }

        var report = await _session.SubmitAsync(_project, Answer(first), CancellationToken.None);

        Assert.Contains("쓰지 않았습니다", report.Problem);
        Assert.NotNull(report.Next);
        Assert.NotEqual(first.Hash, report.Next!.Hash);
        await using var check = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var rows = await check.GetStringsAsync(50, 0, CancellationToken.None);
        Assert.DoesNotContain(rows, row => Ids(first).Contains(row.Id) && row.DestText.StartsWith("번역:", StringComparison.Ordinal));
    }

    /// <summary>An answer that drops a line's tag is never saved with the tag missing.</summary>
    [Fact]
    public async Task AnswerThatDropsATag_DoesNotSaveTheLineWithoutIt()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        for (var i = 0; i < 20 && report.Next != null; i++)
        {
            var request = report.Next;
            var answer = Answer(request, dropTokens: true);
            report = await _session.SubmitAsync(_project, answer, CancellationToken.None);
        }

        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var tagged = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(row => row.SourceText.Contains("<Alias=Player>"));
        Assert.True(tagged.Status != StringEntryStatus.Done || tagged.DestText.Contains("<Alias=Player>", StringComparison.Ordinal),
            $"saved without the tag: {tagged.DestText}");
    }

    /// <summary>
    /// A line the run gave up on (오류) is asked with the same request next time. Its stored answers were replayed and
    /// refused again, so `next` said "남은 줄 1 (오류 1)" with no request, however often it was called. Once nothing is
    /// left to ask, the answers the run used are dropped and `next` asks the line afresh.
    /// </summary>
    [Fact]
    public async Task LineLeftInError_IsAskedAfresh_ByTheNextNext()
    {
        // The model keeps dropping the magnitude the check asks for, until the run gives the line up.
        const string Wrong = "화염 피해를 주고 적을 불태웁니다.";
        var project = await OpenAsync("Magic.esp", MagicPlugin());
        var report = await _session.NextAsync(project, CancellationToken.None);
        for (var i = 0; i < 30 && report.Next is { } request; i++)
        {
            var answer = AgentAnswerCheck.IsJson(request) ? JsonAnswer(request, _ => Wrong)
                : AgentAnswerCheck.IsRepair(request) ? Wrong
                : Wrong + " __XT_PH_9999__";
            report = await _session.SubmitAsync(project, answer, CancellationToken.None);
        }

        Assert.Null(report.Next);
        await using (var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None))
        {
            Assert.NotEmpty(await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Error }, CancellationToken.None));
        }

        var again = await _session.NextAsync(project, CancellationToken.None);

        Assert.NotNull(again.Next);
        Assert.Equal(0, again.Next!.Attempt);
    }

    /// <summary>
    /// "&lt;mag&gt;초 동안 화염 피해를" reads as a duration to the run's placeholder check: the line is asked again,
    /// and submit and the next request.md say which line and why. A text answer's file-ending newline is not saved.
    /// </summary>
    [Fact]
    public async Task LineTheRunAsksAgain_IsReportedWithItsReason_AndTheFixIsSavedClean()
    {
        var project = await OpenAsync("Magic.esp", MagicPlugin());
        var report = await _session.NextAsync(project, CancellationToken.None);
        var batch = report.Next!;
        Assert.True(AgentAnswerCheck.IsJson(batch));
        var magId = AgentAnswerCheck.Lines(batch).Single(line => line.Text.Contains("<mag>", StringComparison.Ordinal)).Id!.Value;

        report = await _session.SubmitAsync(project, JsonAnswer(batch, _ => "<mag>초 동안 화염 피해를 주고 적을 불태웁니다."), CancellationToken.None);

        var rejected = Assert.Single(report.Rejected);
        Assert.Equal(magId, rejected.Id);
        Assert.Contains("시간으로 읽힙니다", rejected.Reason);
        Assert.Contains("초 동안", rejected.Reason);
        Assert.NotNull(report.Next);
        Assert.Contains("앞 답이 걸린 이유", await File.ReadAllTextAsync(report.NextRequestPath!));

        // The rest of the batch is saved, so a fresh run asks for the line alone; the same mistake there brings the
        // one-line fix request, answered right in a file that ends with a newline.
        var textRejections = new List<AgentRejection>();
        var sawTextRepair = false;
        for (var i = 0; i < 10 && report.Next is { } request; i++)
        {
            const string Fixed = "<mag>의 화염 피해를 주고 적을 불태웁니다.";
            var answer = AgentAnswerCheck.IsJson(request) ? JsonAnswer(request, _ => Fixed)
                : AgentAnswerCheck.IsRepair(request) ? Fixed + "\r\n"
                : "<mag>초 동안 화염 피해를 주고 적을 불태웁니다. __XT_PH_9999__";
            if (!AgentAnswerCheck.IsJson(request) && AgentAnswerCheck.IsRepair(request))
            {
                // The fix request says to rewrite CURRENT, with no end sentinel (the text request's own rule).
                var md = await File.ReadAllTextAsync(report.NextRequestPath!);
                Assert.Contains("`<<<CURRENT`", md);
                Assert.DoesNotContain("끝의 `__XT_PH_9999__` 포함", md);
                Assert.Contains("한 줄 고쳐 쓰기", md);
                sawTextRepair = true;
            }
            report = await _session.SubmitAsync(project, answer, CancellationToken.None);
            textRejections.AddRange(report.Rejected.Where(r => r.Id == null));
        }

        Assert.True(sawTextRepair, "no one-line fix request");
        Assert.Contains(textRejections, r => r.Reason.Contains("시간으로 읽힙니다", StringComparison.Ordinal));
        Assert.True(report.Finished);
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        var row = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.Id == magId);
        Assert.Equal("<mag>의 화염 피해를 주고 적을 불태웁니다.", row.DestText);
    }

    /// <summary>
    /// Meridia's Order: line 3184 was asked again only after a book's one-line request, and its reason came with the
    /// book's request instead of its own. A reason now waits for the request that asks its line.
    /// </summary>
    [Fact]
    public void RejectionReason_WaitsForTheRequestThatAsksItsLineAgain()
    {
        static AgentRequest Batch(params long[] ids)
        {
            var payload = JsonSerializer.Serialize(new { source_language = "english", items = ids.Select(id => new { id, text = $"Line {id}." }) });
            return new AgentRequest("h" + ids[0], 0, "agent-app", null, "system", new[] { new AgentMessage("user", "Input JSON:\n" + payload) },
                "application/json", "{}");
        }

        var book = new AgentRequest("book", 0, "agent-app", null, "system", new[] { new AgentMessage("user", "A book's text.") }, null, null);
        var dir = Path.Combine(Path.GetTempPath(), "xt-carry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reason = new AgentRejection(3184, "Line 3184.", "토큰이 빠졌습니다");

            Assert.Empty(AgentSession.CarryRejections(dir, new[] { reason }, book));
            Assert.Empty(AgentSession.CarryRejections(dir, Array.Empty<AgentRejection>(), Batch(10, 11)));
            Assert.Equal(new[] { reason }, AgentSession.CarryRejections(dir, Array.Empty<AgentRejection>(), Batch(3184, 3185)));
            Assert.Empty(AgentSession.CarryRejections(dir, Array.Empty<AgentRejection>(), Batch(3184)));
            Assert.False(File.Exists(Path.Combine(dir, "rejections.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Instructions_AreWrittenOnce_AndRequestMdNamesThem()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        var first = report.Next!;
        var instructions = await File.ReadAllTextAsync(_session.InstructionsPath(_project));
        var requestMd = await File.ReadAllTextAsync(report.NextRequestPath!);

        Assert.Contains(first.SystemInstruction.Trim(), instructions);
        Assert.DoesNotContain(first.SystemInstruction.Trim(), requestMd);
        Assert.Contains("새 절", requestMd);
        Assert.True(requestMd.Length < first.Chars, $"request.md {requestMd.Length} chars, request {first.Chars}");
        Assert.NotEmpty(report.NewInstructionSections);

        report = await _session.SubmitAsync(_project, Answer(first), CancellationToken.None);
        Assert.NotNull(report.Next);
        Assert.Empty(report.NewInstructionSections.Where(id => id.StartsWith("S-", StringComparison.Ordinal)));
        Assert.Contains("앞 요청과 같음", await File.ReadAllTextAsync(report.NextRequestPath!));
    }

    // A mod set in ESO's time needs its setting and names before the first request, and both lived only in the GUI,
    // whose "생성한 프로젝트 문맥 사용" box also decided whether agent mode read the context at all.
    [Fact]
    public async Task ContextAndTermsTheAgentSaved_AreInTheRequest_WhateverTheGuiBoxSays()
    {
        var session = new AgentSession(Path.Combine(_root, "work"), _global, gameStrings: null, new AppSettings(EnableProjectContext: false),
            projectsRootOverride: Path.Combine(_root, "projects"));
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.UpsertProjectContextAsync("이 모드는 ESO 시대 배경이다.", CancellationToken.None);
            var saved = await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms("""[{"source": "Dominion", "target": "알드메리 자치령", "hint": true}]"""), CancellationToken.None);
            Assert.Equal(new[] { "추가: Dominion → 알드메리 자치령" }, saved);
        }

        var request = (await session.NextAsync(_project, CancellationToken.None)).Next!;

        Assert.Contains("이 모드는 ESO 시대 배경이다.", request.SystemInstruction);
        Assert.Contains("알드메리 자치령", string.Join("\n", request.Messages.Select(m => m.Text)));
    }

    [Fact]
    public async Task Terms_AreAddedAsTheGuiAddsThem_AndAChangedOneSaysWhatItHad()
    {
        Assert.Throws<AgentModeException>(() => AgentCli.ParseTerms("""[{"source": "Fury", "target": " "}]"""));
        Assert.Throws<AgentModeException>(() => AgentCli.ParseTerms("[]"));
        Assert.Throws<AgentModeException>(() => AgentCli.ParseTerms("{not json"));
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);

        await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms("""[{"source": "Fury", "target": "격노"}, {"source": "Frenzy", "target": "광분", "hint": true}]"""), CancellationToken.None);
        var lines = await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms("""[{"Source": "fury", "Target": "분노"}, {"source": "Frenzy", "target": "광분", "hint": true}]"""), CancellationToken.None);

        Assert.Equal(new[] { "바꿈: fury → 분노 (이전: 격노)", "그대로: Frenzy → 광분" }, lines);
        var glossary = await db.GetGlossaryAsync(CancellationToken.None);
        var fury = Assert.Single(glossary, e => e.SourceTerm.Equals("fury", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(("분노", XTranslatorAi.Core.Text.GlossaryForceMode.ForceToken, XTranslatorAi.Core.Text.GlossaryMatchMode.WordBoundary, 10),
            (fury.TargetTerm, fury.ForceMode, fury.MatchMode, fury.Priority));
        Assert.Equal(XTranslatorAi.Core.Text.GlossaryForceMode.PromptOnly, Assert.Single(glossary, e => e.SourceTerm == "Frenzy").ForceMode);
    }

    [Fact]
    public void StyleSharedByLines_IsWrittenOnceAsASection_AndTheLinesNameIt()
    {
        const string described = "REC=*:DESC/DNAM (effect or item description shown in menus). Write every sentence in 합니다체.";
        const string own = "REC=BOOK title. Translate as a book title.";
        var items = new object[]
        {
            new { id = 1, text = "Deals <mag> damage.", rec = "MGEF:DNAM", style = described },
            new { id = 2, text = "Fire Bite", rec = "MGEF:FULL" },
            new { id = 3, text = "Heals <mag> points.", rec = "MGEF:DNAM", style = described },
            new { id = 4, text = "The Lusty Argonian", rec = "BOOK:FULL", style = own },
        };
        var payload = JsonSerializer.Serialize(new { source_language = "english", items },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var user = new string('-', 400) + "\n\nInput JSON:\n" + payload;
        var request = new AgentRequest("hash", 0, "agent-app", null, "system", new[] { new AgentMessage("user", user) },
            "application/json", "{}");

        var parts = AgentRequestRenderer.Split(request);
        var style = Assert.Single(parts.Sections, section => section.Id.StartsWith("ST-", StringComparison.Ordinal));
        Assert.Equal(described, style.Text);
        Assert.DoesNotContain(described, parts.UserPayload);
        Assert.Equal(2, parts.UserPayload.Split($"\"style\":\"[{style.Id}]\"").Length - 1);
        Assert.Contains(own, parts.UserPayload);
        Assert.Contains("합니다체", style.Text);

        var project = new AgentProject("Key", "Mod.esp", "db", new PluginReadOptions(), "utf-8", "agent-app", 30, 15000);
        var rendered = AgentRequestRenderer.Render(project, request, new HashSet<string> { style.Id }, Array.Empty<AgentRejection>());
        Assert.Contains($"- [{style.Id}] 줄 style (새 절)", rendered);
        Assert.DoesNotContain(described, rendered);
        Assert.Contains($"1 MGEF:DNAM | Deals <mag> damage.\n    style: [{style.Id}]", rendered.Replace("\r\n", "\n"));
        Assert.Contains("수치 자리표시자", rendered);
    }

    [Fact]
    public void BatchRequestMd_ListsOneLinePerItem_AndKeepsTheJsonAnswerFormat()
    {
        var items = new object[]
        {
            new { id = 7, text = "Bring me the Stalhrim.\nQuickly.", rec = "INFO:NAM1", edid = "DLC2Quest",
                ctx = "Prev (reference only):\n- Who are you?\nNext (reference only):\n- Fine." },
            new { id = 8, text = "Ratway", rec = "LCTN:FULL" },
        };
        var payload = JsonSerializer.Serialize(new
            {
                source_language = "english", target_language = "korean",
                glossary = new[] { new { source = "Magicka", target = "매지카" }, new { source = "Thalmor", target = "탈모르" } },
                items,
            },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var user = new string('-', 400) + "\n\nReturn JSON schema:\n{\"translations\":[{\"id\":123,\"text\":\"...\"}]}\n\nInput JSON:\n" + payload;
        var request = new AgentRequest("hash", 0, "agent-app", null, "system", new[] { new AgentMessage("user", user) },
            "application/json", "{}");
        var project = new AgentProject("Key", "Mod.esp", "db", new PluginReadOptions(), "utf-8", "agent-app", 30, 15000);

        var rendered = AgentRequestRenderer.Render(project, request, new HashSet<string>(), Array.Empty<AgentRejection>()).Replace("\r\n", "\n");

        Assert.Contains("7 INFO:NAM1 DLC2Quest | Bring me the Stalhrim.\\nQuickly.\n    ctx: Prev: / - Who are you? / Next: / - Fine.\n8 LCTN:FULL | Ratway", rendered);
        Assert.Contains("용어집: Magicka → 매지카; Thalmor → 탈모르", rendered);
        Assert.DoesNotContain("Input JSON:", rendered);
        Assert.DoesNotContain("\"source_language\"", rendered);
        Assert.Contains("{\"translations\":[{\"id\":<요청의 id>,\"text\":\"<번역>\"}, ...]}", rendered);
        // Only what the model reads changes: the request (its hash, what a replay asks) keeps the run's JSON prompt.
        Assert.Contains("Input JSON:\n{\"source_language\"", AgentRequestRenderer.Split(request).UserPayload);
    }

    [Fact]
    public async Task RequestMdOfTheRun_ListsItsLines_WithoutTheInputJson()
    {
        var request = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);
        var requestMd = (await File.ReadAllTextAsync(_session.RequestPath(_project))).Replace("\r\n", "\n");

        Assert.DoesNotContain("Input JSON:", requestMd);
        foreach (var (id, text) in Items(request))
        {
            Assert.Contains($"\n{id} ", requestMd);
            Assert.Contains($" | {text.Replace("\r", "\\r").Replace("\n", "\\n")}\n", requestMd);
        }
    }

    [Fact]
    public async Task Lookup_FindsTheTmSpellingOfAName_TheWordItselfFirst()
    {
        var global = (await _global.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None))!;
        await global.BulkUpsertTranslationMemoryAsync("english", "korean", new[]
        {
            ("Stalhrim War Axe", "스탈림 전투 도끼"),
            ("The Stalhrim must be worked with ancient Nord techniques.", "스탈림은 고대 노드의 기법으로 다뤄야 합니다."),
            ("Stalhrim", "스탈림"),
            ("Stalhrimite", "엉뚱한 낱말"),
            ("Ratway", "쥐소굴"),
        }, CancellationToken.None);
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);

        var found = await AgentLookup.FindAsync(db, global, game: null, "stalhrim", 10, CancellationToken.None);

        Assert.Equal(new[] { "Stalhrim", "Stalhrim War Axe", "The Stalhrim must be worked with ancient Nord techniques." },
            found.Memory.Select(h => h.Source));
        Assert.Equal("스탈림", found.Memory[0].Target);
        Assert.Equal(3, found.MemoryTotal);
        Assert.Single((await AgentLookup.FindAsync(db, global, null, "Stalhrim", 1, CancellationToken.None)).Memory);
        Assert.Empty((await AgentLookup.FindAsync(db, global, null, "Concordat", 10, CancellationToken.None)).Memory);

        // The mod's own translated rows: what another lane already called a name.
        var echo = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.SourceText == "Aetherial Echo");
        await db.UpdateStringTranslationAsync(echo.Id, "에테르의 메아리", StringEntryStatus.Done, null, CancellationToken.None);
        var mod = await AgentLookup.FindAsync(db, global, null, "Aetherial", 10, CancellationToken.None);
        Assert.Equal("에테르의 메아리", Assert.Single(mod.Mod!).Target);
        Assert.Equal(1, mod.ModTotal);
    }

    [Fact]
    public async Task AnswerWhoseIdsFitNoLineOfTheRequest_IsRefused()
    {
        var request = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);

        var error = await Assert.ThrowsAsync<AgentModeException>(() => _session.SubmitAsync(_project,
            "{\"translations\":[{\"id\":987654,\"text\":\"앞 요청의 답\"}]}", CancellationToken.None));

        Assert.Contains("하나도 맞지 않아", error.Message);
        Assert.Equal(request.Hash, _session.Pending(_project)?.Hash);
    }

    /// <summary>
    /// The closing report's new names: translated name fields that no glossary term, TM, game text or Korean release
    /// gave, once per English (EldenWarAshPack1 names each skill in its effect and its scroll). Not yet translated
    /// names and other fields stay out.
    /// </summary>
    [Fact]
    public async Task NewNames_AreTheTranslatedNamesNothingGave_OncePerEnglish()
    {
        var project = await OpenAsync("Names.esp", Header()
            .Concat(Group(
                Record("MISC", 0x4001, Sub("EDID", Z("aaEcho1")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x4002, Sub("EDID", Z("aaEcho2")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x4003, Sub("EDID", Z("aaIron")), Sub("FULL", Z("Iron Ingot"))),
                Record("MISC", 0x4004, Sub("EDID", Z("aaAsh")), Sub("FULL", Z("Ash of War"))),
                Record("MISC", 0x4005, Sub("EDID", Z("aaStorm")), Sub("FULL", Z("Storm Fist"))),
                Record("MISC", 0x4006, Sub("EDID", Z("aaFloat")), Sub("FULL", Z("Learning - Floating Passage"))),
                Record("MISC", 0x4007, Sub("EDID", Z("aaFloat2")), Sub("FULL", Z("Rim Scroll - Floating Passage")))))
            .ToArray());
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        var rows = new List<StringEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, CancellationToken.None);
            rows.AddRange(page);
            if (page.Count < 1000) break;
        }

        long Id(string source) => rows.First(r => r.SourceText == source).Id;
        var done = new[]
        {
            ("Aetherial Echo", "에테르의 메아리"), ("Iron Ingot", "주괴 - 강철"), ("Ash of War", "전회"),
            ("Learning - Floating Passage", "배우기 - 쪽배 건너기"), ("Rim Scroll - Floating Passage", "림 주문서 - 쪽배 건너기"),
        };
        await db.UpdateStringTranslationsAsync(rows.Where(r => done.Any(d => d.Item1 == r.SourceText))
            .Select(r => (r.Id, done.First(d => d.Item1 == r.SourceText).Item2, StringEntryStatus.Done, (string?)null)).ToList(), CancellationToken.None);
        await db.UpsertStringNoteAsync(Id("Iron Ingot"), XTranslatorAi.Core.TranslationConstants.TmHitNoteKind, "TM", CancellationToken.None);
        await db.BulkInsertGlossaryAsync(new[] { ((string?)null, "Ash of War", "전회", true, 10, 0, 0, (string?)null) }, CancellationToken.None);
        rows.Clear();
        rows.AddRange(await db.GetStringsAsync(1000, 0, CancellationToken.None));

        var names = await AgentSession.NewNamesAsync(db, rows, CancellationToken.None);

        Assert.Equal(new[] { "Aetherial Echo", "Learning - Floating Passage" }, names.Select(n => n.SourceText));
    }

    /// <summary>
    /// Names inside a text, as the LotD book test's bodies had them: runs of capitalized words joined by "of the",
    /// without the sentence's first word ("During the War of Betony"), a possessive or a lone word that only starts a
    /// sentence (Greetings, The Barber).
    /// </summary>
    [Theory]
    [InlineData("During the War of Betony, the Bretons in the Isle of Craghold were under siege.", "War of Betony|Bretons|Isle of Craghold")]
    [InlineData("Greetings, my friend. Auryen Morellus writes from the Dragonborn Gallery to Shalidor's heirs.", "Auryen Morellus|Dragonborn Gallery|Shalidor")]
    [InlineData("<p align=\"center\">The Barber\n\n[pagebreak]In the market district of Cyrodiil, there once was a barber. I am sure.", "Cyrodiil")]
    [InlineData("What is the thinnest book? Redguard Heroes of the War of Betony.", "Redguard Heroes of the War of Betony")]
    public void TextNames_AreTheCapitalizedRunsThatReadAsNames(string text, string expected)
    {
        Assert.Equal(expected.Split('|'), AgentTextNames.Candidates(text));
    }

    /// <summary>
    /// names reports the names inside translated bodies that nothing knows: a glossary term (Shalidor) and a word the
    /// texts also write in lowercase (Order) stay out, and rows not yet translated are not read.
    /// </summary>
    [Fact]
    public async Task TextNames_AreTheBodyNamesNothingKnows()
    {
        var project = await OpenAsync("Books.esp", Header()
            .Concat(Group(
                Record("BOOK", 0x4011, Sub("EDID", Z("aaNote")), Sub("FULL", Z("A Letter")),
                    Sub("DESC", Z("Greetings, my friend. Auryen Morellus writes from the Dragonborn Gallery about Shalidor and the Order. Our order is old."))),
                Record("BOOK", 0x4012, Sub("EDID", Z("aaLater")), Sub("FULL", Z("Later")),
                    Sub("DESC", Z("Nobody has read what Brelyna Maryon wrote here yet, so it is not reported.")))))
            .ToArray());
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        var rows = (await db.GetStringsAsync(1000, 0, CancellationToken.None)).ToList();
        var letter = rows.First(r => r.SourceText.StartsWith("Greetings", StringComparison.Ordinal));
        await db.UpdateStringTranslationsAsync(new[] { (letter.Id, "안녕하신가. 오리엔 모렐루스가 드래곤본 갤러리에서 샬리도어와 교단에 대해 쓰네.", StringEntryStatus.Done, (string?)null) },
            CancellationToken.None);
        await db.BulkInsertGlossaryAsync(new[] { ((string?)null, "Shalidor", "샬리도어", true, 10, 0, 0, (string?)null) }, CancellationToken.None);
        rows = (await db.GetStringsAsync(1000, 0, CancellationToken.None)).ToList();

        var names = await AgentTextNames.FindAsync(db, null, null, rows, CancellationToken.None);

        Assert.Equal(new[] { "Auryen Morellus", "Dragonborn Gallery" }, names.Select(n => n.Name));
        Assert.Equal(new[] { letter.Id }, names[0].Ids);
    }

    /// <summary>A new plugin of the same mod takes the glossary of the mod's other projects, and open says how many.</summary>
    [Fact]
    public async Task Open_SaysHowManyGlossaryTermsANewProjectTookFromTheSameMod()
    {
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.BulkInsertGlossaryAsync(new[] { ((string?)null, "Thalmor Justiciar", "탈모어 집행관", true, 10, 0, 0, (string?)null) }, CancellationToken.None);
        }

        var path = Path.Combine(_root, "Mod", "Thalmor Addon.esp");
        await File.WriteAllBytesAsync(path, Plugin());
        var (project, _, _, glossary) = await _session.OpenAsync(path, new PluginReadOptions(), "korean", "utf-8", batchSize: 5, CancellationToken.None);
        _extraDbs.Add(project.DbPath);

        Assert.Equal(1, glossary.Count);
        Assert.Equal(1, glossary.Inherited);
        Assert.Equal(new[] { "Thalmor.esp" }, glossary.From);
    }

    private async Task<AgentProject> OpenAsync(string name, byte[] plugin)
    {
        var path = Path.Combine(_root, "Mod", name);
        await File.WriteAllBytesAsync(path, plugin);
        var (project, _, _, _) = await _session.OpenAsync(path, new PluginReadOptions(), "korean", "utf-8", batchSize: 5, CancellationToken.None);
        _extraDbs.Add(project.DbPath);
        return project;
    }

    private readonly List<string> _extraDbs = new();

    private static string JsonAnswer(AgentRequest request, Func<string, string> translate)
        => JsonSerializer.Serialize(new
        {
            translations = AgentAnswerCheck.Lines(request).Select(line => new
            {
                id = line.Id,
                text = line.Text.Contains("<mag>", StringComparison.Ordinal) ? translate(line.Text) : "번역: " + line.Text,
            }),
        });

    private static byte[] MagicPlugin()
        => Header().Concat(Group(Record("MGEF", 0x3001, Sub("EDID", Z("aaFireBite")), Sub("FULL", Z("Fire Bite")),
                Sub("DNAM", Z("Deals <mag> fire damage and burns the target.")))))
            .ToArray();

    private async Task<IReadOnlyList<long>> RowsWithStatusAsync(StringEntryStatus status)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        return await db.GetStringIdsByStatusAsync(new[] { status }, CancellationToken.None);
    }

    /// <summary>Answers requests until one matches, and returns that one, unanswered and pending.</summary>
    private async Task<AgentRequest> AnswerUntilAsync(AgentStepReport report, Func<AgentRequest, bool> wanted)
    {
        for (var i = 0; i < 20; i++)
        {
            var request = report.Next ?? throw new InvalidOperationException("the run finished first");
            if (wanted(request)) return request;
            report = await _session.SubmitAsync(_project, Answer(request), CancellationToken.None);
        }

        throw new InvalidOperationException("no such request");
    }

    private static bool IsJson(AgentRequest request) => request.ResponseMimeType == "application/json";

    private static string UserText(AgentRequest request) => string.Join("\n", request.Messages.Where(m => m.Role == "user").Select(m => m.Text));

    private static List<(long Id, string Text)> Items(AgentRequest request)
    {
        var text = UserText(request);
        if (!IsJson(request))
        {
            var from = text.IndexOf("<<<TEXT", StringComparison.Ordinal) + "<<<TEXT".Length;
            return new List<(long, string)> { (0, text[from..text.LastIndexOf("TEXT>>>", StringComparison.Ordinal)].Trim('\r', '\n')) };
        }

        var start = text.IndexOf("{\"source_language\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "no payload in the request");
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text[start..]));
        using var payload = JsonDocument.ParseValue(ref reader);
        return payload.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("id").GetInt64(), item.GetProperty("text").GetString() ?? "")).ToList();
    }

    private static List<long> Ids(AgentRequest request) => Items(request).Select(item => item.Id).ToList();

    /// <summary>An answer the way the model writes it: every item, its protected tokens kept (unless told to drop them).</summary>
    private static string Answer(AgentRequest request, bool dropTokens = false)
        => !IsJson(request)
            ? "번역: " + Items(request)[0].Text
            : JsonSerializer.Serialize(new
        {
            translations = Items(request).Select(item => new
            {
                id = item.Id,
                text = "번역: " + (dropTokens ? System.Text.RegularExpressions.Regex.Replace(item.Text, "__XT_[A-Z0-9_]+__", "") : item.Text),
            }),
        });

    private static byte[] Plugin()
        => Header().Concat(DialogueGroup(TopicChildren(0x1000,
                Record("INFO", 0x1001, Sub("NAM1", Z("Your loyalty to the Dominion has been noted."))),
                Record("INFO", 0x1002, Sub("NAM1", Z("Then we have an understanding."))),
                Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>."))))))
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEcho")), Sub("FULL", Z("Aetherial Echo")))))
            .ToArray();

    private static byte[] DialogueGroup(params byte[][] children)
    {
        var bytes = Group(children);
        Encoding.ASCII.GetBytes("DIAL").CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] TopicChildren(uint topicFormId, params byte[][] children)
    {
        var bytes = Group(children);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), topicFormId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 7);
        return bytes;
    }
}
