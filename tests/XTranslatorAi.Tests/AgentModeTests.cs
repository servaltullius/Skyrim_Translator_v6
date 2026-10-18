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
        (_project, _, _) = await _session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _global.DisposeAsync();
        TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(_project.DbPath);
        foreach (var db in _extraDbs) TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
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
        Assert.Contains("\"text\":\"Deals <mag> damage.\"", rendered);
        Assert.Contains("수치 자리표시자", rendered);
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

    private async Task<AgentProject> OpenAsync(string name, byte[] plugin)
    {
        var path = Path.Combine(_root, "Mod", name);
        await File.WriteAllBytesAsync(path, plugin);
        var (project, _, _) = await _session.OpenAsync(path, new PluginReadOptions(), "korean", "utf-8", batchSize: 5, CancellationToken.None);
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
