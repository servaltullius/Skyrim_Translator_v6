using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

/// <summary>
/// The requests agent mode makes for a fixed plugin, kept in Baselines/agent-requests.txt: their hashes, instructions
/// (by hash) and messages. A refactoring meant to leave the requests as they are is checked against it; a change that
/// moves a hash answers no request waiting at the update (the agent's answer is dropped and the request made again).
/// Set XT_UPDATE_BASELINES=1 to write the file anew after a change meant to move the requests, and read its diff.
/// The plugin has dialogue (a topic's player prompt, a player response, an alias tag), names, a magic effect with
/// &lt;mag&gt;, and a book; the project has a saved context, a term and the player's style; one answer drops a token, so
/// a line is asked again. Out of it: the GUI's own requests, a game-string dictionary, lanes, and the built-in glossary's
/// terms (their tokens carry the global row's id): no line names a vanilla place.
/// </summary>
public sealed class AgentRequestBaselineTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agent-baseline-tests", Guid.NewGuid().ToString("N"));
    private GlobalProjectDbService _global = null!;
    private string? _dbPath;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(_root, "global"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _global.DisposeAsync();
        if (_dbPath != null) TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(_dbPath);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task AgentRequests_ForAFixedPlugin_StayAsTheBaselineHasThem()
    {
        var plugin = Path.Combine(_root, "Mod", "Baseline.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        await File.WriteAllBytesAsync(plugin, Plugin());
        var session = new AgentSession(Path.Combine(_root, "work"), _global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects"));
        var (project, _, _, _) = await session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 4, CancellationToken.None);
        _dbPath = project.DbPath;
        await using (var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None))
        {
            await db.UpsertProjectContextAsync("The mod is set in the Ash Road, a made-up road of ash plains. The Echo is a ghost.", CancellationToken.None);
            await AgentCli.SaveTermsAsync(db, new[] { new AgentCli.TermLine("Aetherial Echo", "에테르의 메아리", null, false, false) }, CancellationToken.None);
            await db.SetSpeakerStyleAsync(new SpeakerStyle(SpeakerStyles.PlayerSpeaker, "", "반말"), CancellationToken.None);
        }

        var written = new StringBuilder();
        var report = await session.NextAsync(project, CancellationToken.None);
        var dropped = false;
        for (var n = 1; report.Next is { } request; n++)
        {
            Assert.True(n <= 30, "the run did not finish");
            Describe(written, n, request);
            // The first answer with a protected token drops it once, so the run asks that line again.
            var drop = !dropped && AgentAnswerCheck.IsJson(request) && AgentAnswerCheck.Lines(request).Any(line => line.Text.Contains("__XT_", StringComparison.Ordinal));
            dropped |= drop;
            report = await session.SubmitAsync(project, Answer(request, drop), CancellationToken.None);
        }

        Assert.True(dropped, "no answer dropped a token: the fixture has no protected token in a JSON request");

        Assert.Equal(0, report.PendingAfter);
        var actual = written.ToString();
        var path = BaselinePath();
        if (Environment.GetEnvironmentVariable("XT_UPDATE_BASELINES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(path), $"No baseline at {path}: run with XT_UPDATE_BASELINES=1 to write it.");
        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        Assert.True(expected == actual, FirstDifference(expected, actual));
    }

    /// <summary>
    /// The text a request's hash takes from the build has one form of line break, CRLF as the release's checkout writes
    /// it: the meta prompt was LF in a working copy and CRLF in CI, and the baseline above failed in CI alone.
    /// </summary>
    [Theory]
    [InlineData(XTranslatorAi.Core.Models.BethesdaFranchise.ElderScrolls)]
    [InlineData(XTranslatorAi.Core.Models.BethesdaFranchise.Fallout)]
    [InlineData(XTranslatorAi.Core.Models.BethesdaFranchise.Starfield)]
    public void TheHashedTextOfTheBuild_HasCrlfLineBreaksWhateverTheCheckout(XTranslatorAi.Core.Models.BethesdaFranchise franchise)
    {
        static bool OnlyCrlf(string text) => !text.Replace("\r\n", "").Contains('\n') && !text.Replace("\r\n", "").Contains('\r');
        Assert.True(OnlyCrlf(EmbeddedAssets.LoadMetaPrompt(franchise)));
        Assert.True(OnlyCrlf(TranslationPrompt.BuildResponseSchema().GetRawText()));
    }

    private static void Describe(StringBuilder written, int n, AgentRequest request)
    {
        written.Append("=== request ").Append(n).Append('\n')
            .Append("hash: ").Append(request.Hash).Append(" attempt ").Append(request.Attempt).Append('\n')
            .Append("purpose: ").Append(request.Purpose ?? "-").Append(", mime: ").Append(request.ResponseMimeType ?? "-").Append('\n')
            .Append("instructions: ").Append(Short(request.SystemInstruction)).Append(", schema: ").Append(Short(request.ResponseSchema ?? "")).Append('\n');
        foreach (var message in request.Messages)
        {
            written.Append("--- ").Append(message.Role).Append('\n').Append(message.Text.ReplaceLineEndings("\n")).Append('\n');
        }
    }

    private static string Short(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    // The lines that differ (the first 20), as the assertion's message: the hash line alone said nothing of what moved.
    private static string FirstDifference(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        var lines = new List<string>();
        for (var i = 0; i < Math.Max(a.Length, b.Length) && lines.Count < 20; i++)
        {
            var left = i < a.Length ? a[i] : "(none)";
            var right = i < b.Length ? b[i] : "(none)";
            if (left != right) lines.Add($"line {i + 1}:\n  baseline: {left}\n  now:      {right}");
        }

        return lines.Count == 0 ? "" : "The requests moved from the baseline:\n" + string.Join("\n", lines) + "\n(XT_UPDATE_BASELINES=1 writes the baseline anew.)";
    }

    private static string BaselinePath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Baselines", "agent-requests.txt");

    // Each line answered as written, its protected tokens kept (or dropped from the first line that has one): the run
    // moves on to the next request.
    private static string Answer(AgentRequest request, bool dropToken = false)
    {
        var lines = AgentAnswerCheck.Lines(request);
        var dropFrom = dropToken ? lines.First(line => line.Text.Contains("__XT_", StringComparison.Ordinal)).Id : null;
        string Text(long? id, string text)
            => "번역: " + (id != null && id == dropFrom ? System.Text.RegularExpressions.Regex.Replace(text, "__XT_[A-Za-z0-9_]+__", "") : text);
        return AgentAnswerCheck.IsJson(request)
            ? JsonSerializer.Serialize(new { translations = lines.Select(line => new { id = line.Id, text = Text(line.Id, line.Text) }) })
            : "번역: " + lines.Single().Text;
    }

    // A plugin with what the requests carry: a dialogue (a topic's player prompt, a player response, lines with an
    // alias tag), names, a magic effect with <mag>, and a book with a page break.
    private static byte[] Plugin()
        => Header().Concat(DialogueGroup(
                Record("DIAL", 0x1000, Sub("EDID", Z("aaAshTopic")), Sub("FULL", Z("What happened on the Ash Road?"))),
                TopicChildren(0x1000,
                    Record("INFO", 0x1001, Sub("NAM1", Z("Your loyalty to the Dominion has been noted."))),
                    Record("INFO", 0x1002, Sub("NAM1", Z("Then we have an understanding."))),
                    Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>."))),
                    Record("INFO", 0x1004, Sub("RNAM", Z("I serve no one.")), Sub("NAM1", Z("Then you will learn, <Alias=Player>."))))))
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEcho")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x2002, Sub("EDID", Z("aaLantern")), Sub("FULL", Z("Shadow Lantern")))))
            .Concat(Group(Record("MGEF", 0x3001, Sub("EDID", Z("aaFireBite")), Sub("FULL", Z("Fire Bite")),
                Sub("DNAM", Z("Deals <mag> fire damage and burns the target.")))))
            .Concat(Group(Record("BOOK", 0x4001, Sub("EDID", Z("aaJournal")), Sub("FULL", Z("Journal of the Ash Road")),
                Sub("DESC", Z("Day one. The ash fell like snow over the old road.[pagebreak]Day two. We found the shrine, and the shrine found us.")))))
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
