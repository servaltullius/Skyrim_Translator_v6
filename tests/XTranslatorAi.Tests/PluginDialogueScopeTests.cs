using System.Buffers.Binary;
using System.Text;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

public sealed class PluginDialogueScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderAndDb_PreserveTopicScopeWithoutInventingEditorIds(bool includeParentRecords)
    {
        var original = TwoTopics(includeParentRecords);
        await using var fixture = await Fixture.CreateAsync(original);
        var info = fixture.Document.Fields.Where(field => field.RecordType == "INFO").ToArray();
        Assert.Equal(4, info.Length);
        Assert.Equal((uint?)0x1000, info[0].DialogueTopicFormId);
        Assert.Equal(info[0].DialogueTopicFormId, info[1].DialogueTopicFormId);
        Assert.Equal((uint?)0x2000, info[2].DialogueTopicFormId);
        Assert.Equal(info[2].DialogueTopicFormId, info[3].DialogueTopicFormId);
        Assert.All(info, field => Assert.Null(field.EditorId));
        Assert.All(fixture.Document.Fields.Where(field => field.RecordType == "DIAL"),
            field => Assert.Equal((uint?)field.FormId, field.DialogueTopicFormId));

        await fixture.ReopenDbAsync();
        var contexts = await fixture.Db.GetStringTranslationContextsByIdsAsync(fixture.Ids, CancellationToken.None);
        Assert.All(contexts.Values.Where(row => row.SourceText.StartsWith("Alpha", StringComparison.Ordinal)),
            row => Assert.Equal("plugin:topic:00001000", row.DialogueScope));
        Assert.All(contexts.Values.Where(row => row.SourceText.StartsWith("Beta", StringComparison.Ordinal)),
            row => Assert.Equal("plugin:topic:00002000", row.DialogueScope));
        var rows = await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None);
        Assert.All(rows, row => Assert.Null(row.AttributesJson));
        Assert.All(rows.Where(row => row.Rec == "INFO:NAM1"), row => Assert.Null(row.Edid));
        Assert.All(await fixture.Db.GetStringsForExportAsync(20, 0, CancellationToken.None), row => Assert.Equal("", row.RawStringXml));
        var output = await PluginWriter.ExportAsync(fixture.Document, new Dictionary<string, string>(),
            new PluginExportOptions(Path.Combine(fixture.Root, "unchanged")), CancellationToken.None);
        Assert.Equal(original, await File.ReadAllBytesAsync(output.PluginPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DialoguePrompts_UseSameTopicNeighbors_AndExcludeAdjacentOtherTopics(bool includeParentRecords)
    {
        await using var fixture = await Fixture.CreateAsync(TwoTopics(includeParentRecords));
        await fixture.Service.TranslateIdsAsync(fixture.Request);
        var alpha = fixture.Client.Requests.Single(request => request.Source == "Alpha reply");
        Assert.Contains("Alpha opening", alpha.Prompt);
        Assert.DoesNotContain("Beta", alpha.Prompt);
        var beta = fixture.Client.Requests.Single(request => request.Source == "Beta opening");
        Assert.Contains("Beta reply", beta.Prompt);
        Assert.DoesNotContain("Alpha", beta.Prompt);
        Assert.All(await fixture.Db.GetStringsAsync(20, 0, CancellationToken.None), row => Assert.Equal(StringEntryStatus.Done, row.Status));
    }

    [Fact]
    public async Task InfoWithoutTopicGroup_UsesOnlyItsOwnRecordAsContext()
    {
        var plugin = Header().Concat(DialogueGroup(
            Record("INFO", 0x801, Sub("NAM1", Z("First orphan line")), Sub("NAM1", Z("Second orphan line"))),
            Record("INFO", 0x802, Sub("NAM1", Z("Unrelated orphan line"))))).ToArray();
        await using var fixture = await Fixture.CreateAsync(plugin);
        Assert.All(fixture.Document.Fields, field => Assert.Null(field.DialogueTopicFormId));
        var contexts = await fixture.Db.GetStringTranslationContextsByIdsAsync(fixture.Ids, CancellationToken.None);
        Assert.Equal(contexts[fixture.Ids[0]].DialogueScope, contexts[fixture.Ids[1]].DialogueScope);
        Assert.NotNull(contexts[fixture.Ids[0]].DialogueScope);
        Assert.NotEqual(contexts[fixture.Ids[0]].DialogueScope, contexts[fixture.Ids[2]].DialogueScope);

        await fixture.Service.TranslateIdsAsync(fixture.Request);
        var first = fixture.Client.Requests.Single(request => request.Source == "First orphan line");
        Assert.Contains("Second orphan line", first.Prompt);
        Assert.DoesNotContain("Unrelated orphan line", first.Prompt);
        var unrelated = fixture.Client.Requests.Single(request => request.Source == "Unrelated orphan line");
        Assert.DoesNotContain("First orphan line", unrelated.Prompt);
        Assert.DoesNotContain("Second orphan line", unrelated.Prompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextWindowDisabled_DuplicateReuseStillRequiresSameTopic(bool sameTopic)
    {
        await using var fixture = await Fixture.CreateAsync(RepeatedReply(sameTopic));
        fixture.Client.NumberReplies = true;
        await fixture.Service.TranslateIdsAsync(fixture.Request with { EnableDialogueContextWindow = false });
        Assert.Equal(sameTopic ? 1 : 2, fixture.Client.Requests.Count);
        var rows = await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None);
        Assert.Equal(sameTopic ? 1 : 2, rows.Select(row => row.DestText).Distinct().Count());
        Assert.All(rows, row => Assert.Equal(StringEntryStatus.Done, row.Status));
    }

    [Fact]
    public async Task ContextWindowDisabled_OrphanInfosDoNotShareDuplicateTranslation()
    {
        var plugin = Header().Concat(DialogueGroup(
            Record("INFO", 0x801, Sub("NAM1", Z("Yes"))),
            Record("INFO", 0x802, Sub("NAM1", Z("Yes"))))).ToArray();
        await using var fixture = await Fixture.CreateAsync(plugin);
        fixture.Client.NumberReplies = true;
        await fixture.Service.TranslateIdsAsync(fixture.Request with { EnableDialogueContextWindow = false });
        Assert.Equal(2, fixture.Client.Requests.Count);
        var rows = await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None);
        Assert.Equal(2, rows.Select(row => row.DestText).Distinct().Count());
        Assert.All(rows, row => Assert.Equal(StringEntryStatus.Done, row.Status));
    }

    [Theory]
    [InlineData(StringEntryStatus.Done)]
    [InlineData(StringEntryStatus.Edited)]
    public async Task PartialResume_DoesNotReuseSourceOnlyTmFromAnotherTopic(StringEntryStatus existingStatus)
    {
        await using var fixture = await Fixture.CreateAsync(RepeatedReply(sameTopic: false));
        await fixture.Db.UpdateStringTranslationAsync(fixture.Ids[0], "Existing topic reply", existingStatus, null, CancellationToken.None);
        fixture.Client.NumberReplies = true;
        // Loaded project/global TM dictionaries use normalized keys. An uppercase
        // fixture key would silently miss the TM before the scope policy runs.
        var sourceKey = TranslationMemoryKey.NormalizeSource("Yes");
        var sharedTm = new Dictionary<string, string> { [sourceKey] = "Existing topic reply" };
        var projectTmBefore = await fixture.Db.GetTranslationMemoryAsync("english", "english", CancellationToken.None);
        Assert.Equal(existingStatus == StringEntryStatus.Edited, projectTmBefore.ContainsKey(sourceKey));
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            Ids = new[] { fixture.Ids[1] }, EnableDialogueContextWindow = false, GlobalTranslationMemory = sharedTm,
        });
        Assert.Single(fixture.Client.Requests);
        var rows = await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None);
        Assert.Equal("Existing topic reply", rows[0].DestText);
        Assert.Equal(existingStatus, rows[0].Status);
        Assert.Equal("Reply 1", rows[1].DestText);
        Assert.Equal(StringEntryStatus.Done, rows[1].Status);
        Assert.Equal("Existing topic reply", sharedTm[sourceKey]);
        var projectTmAfter = await fixture.Db.GetTranslationMemoryAsync("english", "english", CancellationToken.None);
        Assert.Equal(projectTmBefore.OrderBy(pair => pair.Key), projectTmAfter.OrderBy(pair => pair.Key));
        var notes = await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.TmFallbackNoteKind, CancellationToken.None);
        Assert.True(notes.TryGetValue(fixture.Ids[1], out var note), "The available source-only TM must be bypassed with a recorded reason.");
        Assert.Contains("topic", note!);
    }

    [Fact]
    public async Task XmlRows_KeepNullScopeAndExistingAdjacencyHeuristic()
    {
        await using var fixture = await Fixture.CreateAsync(Header(), importPlugin: false);
        await fixture.Db.BulkInsertStringsAsync(new[]
        {
            (0, (string?)null, (string?)null, (string?)null, (string?)null, (string?)"INFO:NAM1", "First XML line", "", StringEntryStatus.Pending, "<String/>"),
            (1, (string?)null, (string?)null, (string?)null, (string?)null, (string?)"INFO:NAM1", "Second XML line", "", StringEntryStatus.Pending, "<String/>"),
        }, CancellationToken.None);
        var ids = await fixture.Db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
        var contexts = await fixture.Db.GetStringTranslationContextsByIdsAsync(ids, CancellationToken.None);
        Assert.All(contexts.Values, row => Assert.Null(row.DialogueScope));
        await fixture.Service.TranslateIdsAsync(fixture.Request with { Ids = ids });
        Assert.Contains("Second XML line", fixture.Client.Requests.Single(request => request.Source == "First XML line").Prompt);
    }

    private static byte[] TwoTopics(bool includeParentRecords)
    {
        var children = new List<byte[]>();
        if (includeParentRecords) children.Add(Record("DIAL", 0x1000, Sub("EDID", Z("RealTopicAlpha")), Sub("FULL", Z("Alpha topic"))));
        children.Add(TopicChildren(0x1000, Record("INFO", 0x1001, Sub("NAM1", Z("Alpha opening"))), Record("INFO", 0x1002, Sub("NAM1", Z("Alpha reply")))));
        if (includeParentRecords) children.Add(Record("DIAL", 0x2000, Sub("EDID", Z("RealTopicBeta")), Sub("FULL", Z("Beta topic"))));
        children.Add(TopicChildren(0x2000, Record("INFO", 0x2001, Sub("NAM1", Z("Beta opening"))), Record("INFO", 0x2002, Sub("NAM1", Z("Beta reply")))));
        return Header().Concat(DialogueGroup(children.ToArray())).ToArray();
    }

    private static byte[] RepeatedReply(bool sameTopic)
    {
        var first = Record("INFO", 0x1001, Sub("NAM1", Z("Yes")));
        var second = Record("INFO", 0x2001, Sub("NAM1", Z("Yes")));
        return Header().Concat(sameTopic
            ? DialogueGroup(TopicChildren(0x1000, first, second))
            : DialogueGroup(TopicChildren(0x1000, first), TopicChildren(0x2000, second))).ToArray();
    }

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

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; }
        private string DbPath => Path.Combine(Root, "project.sqlite");
        public ProjectDb Db { get; private set; }
        public PluginDocument Document { get; }
        public IReadOnlyList<long> Ids { get; }
        public EchoClient Client { get; } = new();
        public TranslationService Service => new(Db, Client);
        public TranslateIdsRequest Request => new("fixture", "gemini-2.5-flash", "english", "english", "base", Ids,
            BatchSize: 1, MaxChars: 5000, MaxConcurrency: 1, Temperature: 0, MaxOutputTokens: 1024, MaxRetries: 0,
            UseRecStyleHints: true, EnableRepairPass: false, EnableSessionTermMemory: false,
            OnRowUpdated: null, WaitIfPaused: null, CancellationToken: CancellationToken.None,
            EnablePromptCache: false, EnableRiskyCandidateRerank: false);
        private Fixture(string root, ProjectDb db, PluginDocument document, IReadOnlyList<long> ids)
        { Root = root; Db = db; Document = document; Ids = ids; }
        public static async Task<Fixture> CreateAsync(byte[] bytes, bool importPlugin = true)
        {
            var root = Path.Combine(Path.GetTempPath(), "plugin-dialogue-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var input = Path.Combine(root, "Dialogue.esp");
            await File.WriteAllBytesAsync(input, bytes);
            var document = await PluginReader.ReadAsync(input, new PluginReadOptions(), CancellationToken.None);
            var db = await ProjectDb.OpenOrCreateAsync(Path.Combine(root, "project.sqlite"), CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            var project = new ProjectInfo(1, "", "Dialogue.esp", BethesdaFranchise.ElderScrolls, "english", "english", "", false,
                "", "gemini-2.5-flash", "base", "", false, now, now);
            if (importPlugin)
            {
                var imported = await db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, project, "utf-8", CancellationToken.None);
                return new Fixture(root, db, document, imported.Select(row => row.Id).ToArray());
            }
            await db.UpsertProjectAsync(project with { InputXmlPath = "Dialogue.xml", AddonName = "Dialogue.xml" }, CancellationToken.None);
            return new Fixture(root, db, document, Array.Empty<long>());
        }
        public async Task ReopenDbAsync()
        {
            await Db.DisposeAsync();
            Db = await ProjectDb.OpenOrCreateAsync(DbPath, CancellationToken.None);
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(DbPath);
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class EchoClient : IGeminiClient
    {
        public List<(string Source, string Prompt)> Requests { get; } = new();
        public bool NumberReplies { get; set; }
        public Task<string> GenerateContentAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
        {
            var prompt = request.Contents[0].Parts[0].Text!;
            const string marker = "<<<TEXT";
            var start = prompt.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0);
            var text = prompt[(start + marker.Length)..].TrimStart('\r', '\n');
            text = text[..text.LastIndexOf("TEXT>>>", StringComparison.Ordinal)].TrimEnd('\r', '\n');
            Requests.Add((text.Replace(TranslationConstants.EndSentinelToken, "", StringComparison.Ordinal).TrimEnd(), prompt));
            return Task.FromResult(NumberReplies ? $"Reply {Requests.Count} {TranslationConstants.EndSentinelToken}" : text);
        }
        public async Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
            => new[] { await GenerateContentAsync(apiKey, modelName, request, cancellationToken) };
        public Task<int> CountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken) => Task.FromResult(Math.Max(1, text.Length / 4));
        public Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CreateCachedContentAsync(string apiKey, string modelName, string systemInstructionText, TimeSpan ttl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
