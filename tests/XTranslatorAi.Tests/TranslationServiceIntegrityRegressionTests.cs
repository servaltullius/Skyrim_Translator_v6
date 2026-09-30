using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using System.Text.Json;

namespace XTranslatorAi.Tests;

public sealed class TranslationServiceIntegrityRegressionTests
{
    [Theory]
    [InlineData("Hello %s")]
    [InlineData("Count: %02d")]
    [InlineData("Hello {name}")]
    [InlineData("Hello {{name}}")]
    [InlineData("Hello $PLAYER$")]
    [InlineData("First[pagebreak]Second")]
    [InlineData("First\r\nSecond")]
    [InlineData("Chance: 25%")]
    public async Task TranslationMemory_WithMissingProtectedText_FallsBackToModel(string source)
    {
        await using var fixture = await Fixture.CreateAsync((source, "MESG:DESC", null));
        var request = fixture.Request with
        {
            GlobalTranslationMemory = new Dictionary<string, string>
            {
                [TranslationMemoryKey.NormalizeSource(source)] = "invalid translation",
            },
        };

        await fixture.Service.TranslateIdsAsync(request);

        var state = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Equal(StringEntryStatus.Done, state.Status);
        Assert.Equal(source, state.DestText);
        Assert.True(fixture.Client.Calls > 0);
        Assert.Contains(fixture.Ids[0], (await fixture.Db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None)).Keys);
    }

    [Fact]
    public async Task TranslationMemory_WithAllProtectedText_ReusesWithoutApi()
    {
        const string source = "Hello %s, {name}.[pagebreak]Next\r\n$PLAYER$ 25%";
        const string translated = "Greetings %s, {name}.[pagebreak]Following\r\n$PLAYER$ 25%";
        await using var fixture = await Fixture.CreateAsync((source, "BOOK:DESC", null));
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            EnablePromptCache = true,
            GlobalTranslationMemory = new Dictionary<string, string>
            {
                [TranslationMemoryKey.NormalizeSource(source)] = translated,
            },
        });

        var state = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Equal(translated, state.DestText);
        Assert.Equal(0, fixture.Client.Calls);
        Assert.Equal(0, fixture.Client.CacheCreates);
        Assert.Equal(0, fixture.Client.CacheDeletes);
    }

    [Fact]
    public async Task EmptyRun_DoesNotCreatePromptCache()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.TranslateIdsAsync(fixture.Request with { EnablePromptCache = true });
        Assert.Equal(0, fixture.Client.Calls);
        Assert.Equal(0, fixture.Client.CacheCreates);
        Assert.Equal(0, fixture.Client.CacheDeletes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromptCache_IsCreatedOnceOnDemandOrFallsBackToSystemPrompt(bool unavailable)
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null), ("World", "MESG", null));
        fixture.Client.CacheUnavailable = unavailable;
        await fixture.Service.TranslateIdsAsync(fixture.Request with { EnablePromptCache = true });
        Assert.Equal(1, fixture.Client.CacheCreates);
        Assert.Equal(unavailable ? 0 : 1, fixture.Client.CacheDeletes);
        Assert.Equal(2, fixture.Client.Calls);
        Assert.All(fixture.Client.Requests, request =>
        {
            if (unavailable)
            {
                Assert.Null(request.CachedContent);
                Assert.NotNull(request.SystemInstruction);
            }
            else
            {
                Assert.Equal("cachedContents/fixture", request.CachedContent);
                Assert.Null(request.SystemInstruction);
            }
        });
        Assert.All(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None),
            row => Assert.Equal(StringEntryStatus.Done, row.Status));
    }

    [Theory]
    [InlineData("WEAP:FULL", true)]
    [InlineData("BOOK:FULL", true)]
    [InlineData("INFO:NAM1", false)]
    [InlineData("DIAL:FULL", false)]
    public async Task AutomaticTermLearning_QueuesNamesForReviewAndExcludesDialogue(string rec, bool expected)
    {
        await using var fixture = await Fixture.CreateAsync(("Ancient Sword", rec, null));
        await fixture.Service.TranslateIdsAsync(fixture.Request with { EnableSessionTermMemory = true });
        var glossary = await fixture.Db.GetGlossaryAsync(CancellationToken.None);
        if (expected)
        {
            var suggestion = Assert.Single(glossary);
            Assert.False(suggestion.Enabled);
            Assert.Equal(GlossaryForceMode.PromptOnly, suggestion.ForceMode);
        }
        else
        {
            Assert.Empty(glossary);
        }
    }

    [Theory]
    [InlineData("Hello", "Hello %s")]
    [InlineData("Hello %s", "Hello %s %s")]
    [InlineData("First[pagebreak]Second", "FirstSecond")]
    [InlineData("Hello {name}", "Hello {other}")]
    [InlineData("Hello", "<b>Hello</b>")]
    [InlineData("Hello", "Hello <Alias=Player>")]
    public void FinalValidation_RejectsChangedProtectedText(string source, string translated)
        => Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateFinalTextIntegrity(source, translated, "test"));

    [Theory]
    [InlineData("Value <mag> lasts <dur>.", "For <dur>, value <mag>.")]
    [InlineData("Hello %s {name} $PLAYER$", "$PLAYER$ {name} %s Hello")]
    [InlineData("<Alias=Player> found <Global=Value> and <CustomName>", "<CustomName> and <Global=Value> found <Alias=Player>")]
    [InlineData("<b>Value <mag> lasts <dur>.</b>[pagebreak]\r\nNext", "<b>For <dur>, value <mag>.</b>[pagebreak]\r\nFollowing")]
    public void FinalValidation_AllowsRuntimePlaceholderReordering(string source, string translated)
        => TokenValidator.ValidateFinalTextIntegrity(source, translated, "test");

    [Theory]
    [InlineData("<b>Hello</b>", "</b>Hello<b>")]
    [InlineData("<b><i>Hello</i></b>", "<b><i>Hello</b></i>")]
    [InlineData("<font face='A'>Hello</font><br>World", "<br><font face='A'>Hello</font>World")]
    [InlineData("<custom>Hello</custom>", "</custom>Hello<custom>")]
    [InlineData("<b>Hello</b>[pagebreak]World", "<b>Hello[pagebreak]</b>World")]
    [InlineData("First[pagebreak]Second\r\nThird", "First\r\nSecond[pagebreak]Third")]
    [InlineData("First\rSecond\nThird", "First\nSecond\rThird")]
    public void FinalValidation_RejectsReorderedFormatting(string source, string translated)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => TokenValidator.ValidateFinalTextIntegrity(source, translated, "test"));
        Assert.Contains("formatting order", error.Message);
    }

    [Theory]
    [InlineData("<b>Hello</b>", "</b>Hello<b>")]
    [InlineData("<b>Hello</b>[pagebreak]World", "<b>Hello[pagebreak]</b>World")]
    [InlineData("First[pagebreak]Second\r\nThird", "First\r\nSecond[pagebreak]Third")]
    public async Task TranslationMemory_WithReorderedFormatting_FallsBackToModel(string source, string translated)
    {
        await using var fixture = await Fixture.CreateAsync((source, "BOOK:DESC", null));
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            GlobalTranslationMemory = new Dictionary<string, string>
            {
                [TranslationMemoryKey.NormalizeSource(source)] = translated,
            },
        });

        var state = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Equal(StringEntryStatus.Done, state.Status);
        Assert.Equal(source, state.DestText);
        Assert.True(fixture.Client.Calls > 0);
        Assert.Contains(fixture.Ids[0], (await fixture.Db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None)).Keys);
    }

    [Theory]
    [InlineData("first second", 6)]
    [InlineData("first  second", 7)]
    [InlineData("first\tsecond", 6)]
    public async Task LongText_PreservesWhitespaceAtChunkBoundaries(string source, int maxChars)
    {
        await using var fixture = await Fixture.CreateAsync((source, "BOOK:DESC", null));
        await fixture.Service.TranslateIdsAsync(fixture.Request with { MaxChars = maxChars });

        var state = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Equal(StringEntryStatus.Done, state.Status);
        Assert.Equal(source, state.DestText);
        Assert.True(fixture.Client.Calls >= 2);
    }

    [Fact]
    public async Task Glossary_PreservesAdjacentRepeatedTerms()
    {
        await using var fixture = await Fixture.CreateAsync(("Draugr Draugr", "INFO:NAM1", null));
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            GlobalGlossary = new[]
            {
                new GlossaryEntry(1, null, "Draugr", "드라우그르", true, GlossaryMatchMode.WordBoundary,
                    GlossaryForceMode.ForceToken, 10, null),
            },
        });

        var state = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Equal(StringEntryStatus.Done, state.Status);
        Assert.Equal("드라우그르 드라우그르", state.DestText);
    }

    [Fact]
    public async Task DuplicateReuse_SeparatesRecordTypes()
    {
        await using var fixture = await Fixture.CreateAsync(("Charge", "WEAP:FULL", null), ("Charge", "INFO:NAM1", null));
        await fixture.Service.TranslateIdsAsync(fixture.Request);
        Assert.Equal(2, fixture.Client.Calls);
    }

    [Theory]
    [InlineData("WEAP:FULL", "Item", "INFO:NAM1", "Item")]
    [InlineData("INFO:NAM1", "Guard", "INFO:NAM1", "Mage")]
    public async Task TranslationMemory_SourceWithConflictingContextsUsesIndividualTranslations(
        string firstRec, string firstEdid, string secondRec, string secondEdid)
    {
        await using var fixture = await Fixture.CreateAsync(("Charge", firstRec, firstEdid), ("Charge", secondRec, secondEdid));
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            GlobalTranslationMemory = new Dictionary<string, string> { [TranslationMemoryKey.NormalizeSource("Charge")] = "Wrong context" },
        });
        Assert.Equal(2, fixture.Client.Calls);
        Assert.All(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None), row => Assert.Equal("Charge", row.DestText));
        Assert.Equal(2, (await fixture.Db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None)).Count);
    }

    [Fact]
    public async Task DuplicateReuse_SeparatesEditorIds()
    {
        await using var fixture = await Fixture.CreateAsync(("Charge", "INFO:NAM1", "Guard01"), ("Charge", "INFO:NAM1", "Mage01"));
        await fixture.Service.TranslateIdsAsync(fixture.Request);
        Assert.Equal(2, fixture.Client.Calls);
    }

    [Fact]
    public async Task DuplicateReuse_SeparatesDialogueContext()
    {
        await using var fixture = await Fixture.CreateAsync(
            ("First context", "INFO:NAM1", "Scene"), ("Yes", "INFO:NAM1", "Scene"),
            ("Second context", "INFO:NAM1", "Scene"), ("Yes", "INFO:NAM1", "Scene"));
        await fixture.Service.TranslateIdsAsync(fixture.Request);
        Assert.Equal(4, fixture.Client.Calls);
    }

    [Fact]
    public async Task DuplicateReuse_KeepsSafeReuseForIdenticalContext()
    {
        await using var fixture = await Fixture.CreateAsync(("Charge", "WEAP:FULL", "Item"), ("Charge", "WEAP:FULL", "Item"));
        await fixture.Service.TranslateIdsAsync(fixture.Request);
        Assert.Equal(1, fixture.Client.Calls);
        foreach (var id in fixture.Ids)
        {
            Assert.Equal(StringEntryStatus.Done, (await fixture.Db.GetStringTranslationStateAsync(id, CancellationToken.None)).Status);
        }
    }

    [Fact]
    public async Task Cancellation_RestoresOnlyUnfinishedRowsAndPropagates()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null), ("World", "MESG", null));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.BeforeGenerate = async (call, token) =>
        {
            if (call == 2)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
        };
        var run = fixture.Service.TranslateIdsAsync(fixture.Request with { CancellationToken = cancellation.Token });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var states = await fixture.Db.GetStringStatusesByIdsAsync(fixture.Ids, CancellationToken.None);
        Assert.Single(states.Values, status => status == StringEntryStatus.Done);
        Assert.Single(states.Values, status => status == StringEntryStatus.Pending);
        Assert.DoesNotContain(StringEntryStatus.InProgress, states.Values);
        Assert.DoesNotContain(StringEntryStatus.Error, states.Values);
    }

    [Fact]
    public async Task ConcurrentRun_IsRejectedWithoutDisturbingActiveRun()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.BeforeGenerate = async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var run = fixture.Service.TranslateIdsAsync(fixture.Request with { CancellationToken = cancellation.Token });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TranslateIdsAsync(fixture.Request));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        fixture.Client.BeforeGenerate = null;
        await fixture.Service.TranslateIdsAsync(fixture.Request);
        Assert.Equal(StringEntryStatus.Done, (await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[0], CancellationToken.None)).Status);
    }

    [Fact]
    public async Task SplitBatch_CredentialFailurePreservesAlreadyCompletedRows()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null), ("World", "MESG", null));
        fixture.Client.BeforeGenerate = (call, _) => call == 3
            ? Task.FromException(new GeminiHttpException("generateContent", 401, "Unauthorized", null, "invalid API key"))
            : Task.CompletedTask;
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.TranslateIdsAsync(fixture.Request with { BatchSize = 2 }));

        var states = await fixture.Db.GetStringStatusesByIdsAsync(fixture.Ids, CancellationToken.None);
        Assert.Single(states.Values, status => status == StringEntryStatus.Done);
        Assert.Single(states.Values, status => status == StringEntryStatus.Pending);
    }

    [Fact]
    public async Task PartialBatch_CommitsValidPeerBeforeRequestingOnlyMissingRow()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null), ("World", "MESG", null));
        fixture.Client.ResponseOverride = (call, _) => call == 1
            ? JsonSerializer.Serialize(new { translations = new[] { new { id = fixture.Ids[0], text = "Hello" } } }) : null;
        fixture.Client.BeforeGenerate = async (call, _) =>
        {
            if (call == 2)
                Assert.Equal(StringEntryStatus.Done, (await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[0], CancellationToken.None)).Status);
        };
        await fixture.Service.TranslateIdsAsync(fixture.Request with { BatchSize = 2 });
        Assert.Equal(2, fixture.Client.Calls);
        Assert.DoesNotContain("Hello", fixture.Client.Requests[1].Contents[0].Parts[0].Text!);
        Assert.All(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None), row => Assert.Equal(StringEntryStatus.Done, row.Status));
    }

    [Fact]
    public async Task PartialBatch_GlobalCapKeepsCompletedPeerAndRestoresMissingRow()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null), ("World", "MESG", null));
        fixture.Client.ResponseOverride = (_, _) => JsonSerializer.Serialize(new { translations = new[] { new { id = fixture.Ids[0], text = "Hello" } } });
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.TranslateIdsAsync(fixture.Request with { BatchSize = 2, MaxTotalGenerations = 1 }));
        Assert.Equal(1, fixture.Client.Calls);
        Assert.Equal(StringEntryStatus.Done, (await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[0], CancellationToken.None)).Status);
        Assert.Equal(StringEntryStatus.Pending, (await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[1], CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task LongText_TransportFailureDoesNotRecursivelySplit(int status)
    {
        await using var fixture = await Fixture.CreateAsync((string.Concat(Enumerable.Repeat("Long source text. ", 300)), "BOOK:DESC", null));
        fixture.Client.BeforeGenerate = (_, _) => Task.FromException(new GeminiHttpException("generateContent", status, "fixture", null, "fixture"));
        var run = () => fixture.Service.TranslateIdsAsync(fixture.Request with { MaxChars = 1000, MaxRetries = 0 });
        if (status is 401 or 403) await Assert.ThrowsAnyAsync<Exception>(run);
        else await run();
        Assert.Equal(1, fixture.Client.Calls);
    }

    [Fact]
    public async Task RepairBatch_CommitsLaterValidPeerBeforeMissingRowHitsRunCap()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello %s", "MESG", null), ("World %s", "MESG", null));
        fixture.Client.ResponseOverride = (call, request) =>
        {
            if (call == 1)
                return JsonSerializer.Serialize(new { translations = fixture.Ids.Select(id => new { id, text = "missing token" }) });
            Assert.Equal("repair-batch", request.Purpose);
            var prompt = request.Contents[0].Parts[0].Text!;
            using var json = JsonDocument.Parse(prompt[(prompt.IndexOf("Input JSON:\n", StringComparison.Ordinal) + "Input JSON:\n".Length)..]);
            var repaired = json.RootElement.GetProperty("items")[1].GetProperty("source").GetString();
            return JsonSerializer.Serialize(new { translations = new[] { new { id = fixture.Ids[1], text = repaired } } });
        };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.TranslateIdsAsync(fixture.Request with
        { BatchSize = 2, EnableRepairPass = true, MaxTotalGenerations = 2 }));
        Assert.Equal(2, fixture.Client.Calls);
        var first = await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[0], CancellationToken.None);
        var second = await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[1], CancellationToken.None);
        Assert.Equal(StringEntryStatus.Pending, first.Status);
        Assert.Equal(StringEntryStatus.Done, second.Status);
        Assert.Equal("World %s", (await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None)).Single(row => row.Id == fixture.Ids[1]).DestText);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OptionalEscalation_ExhaustedBudgetPreservesValidatedBaseline(int rowCount)
    {
        var rows = new[] { ("Hello friend", "MESG", (string?)null), ("Goodbye friend", "MESG", (string?)null) };
        await using var fixture = await Fixture.CreateAsync(rows.Take(rowCount).ToArray());
        if (rowCount == 2)
            fixture.Client.ResponseOverride = (_, _) => JsonSerializer.Serialize(new
            { translations = fixture.Ids.Select((id, i) => new { id, text = rows[i].Item1 }) });
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        { BatchSize = rowCount, TargetLang = "korean", EnableQualityEscalation = true, QualityEscalationModelName = "gemini-2.5-pro", MaxTotalGenerations = 1 });
        Assert.True(LqaHeuristics.IsLikelyUntranslated(rows[0].Item1, rows[0].Item1));
        Assert.Equal(1, fixture.Client.Calls);
        Assert.All(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None), row =>
        {
            Assert.Equal(StringEntryStatus.Done, row.Status);
            Assert.Equal(row.SourceText, row.DestText);
        });
    }

    [Fact]
    public async Task PartialBatch_RepairHttpFailureNeverRetriesCompletedPeer()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null), ("World", "MESG", null));
        fixture.Client.ResponseOverride = (call, _) => call == 1
            ? JsonSerializer.Serialize(new { translations = new[] { new { id = fixture.Ids[0], text = "Hello" } } }) : null;
        fixture.Client.BeforeGenerate = (call, _) => call > 1
            ? Task.FromException(new GeminiHttpException("generateContent", 503, "fixture", TimeSpan.Zero, "fixture")) : Task.CompletedTask;
        await fixture.Service.TranslateIdsAsync(fixture.Request with { BatchSize = 2, MaxRetries = 2 });
        Assert.Equal(3, fixture.Client.Calls); // Initial JSON + bounded single-row repair attempts.
        Assert.All(fixture.Client.Requests.Skip(1), request => Assert.DoesNotContain("Hello", request.Contents[0].Parts[0].Text!));
        Assert.Equal(StringEntryStatus.Done, (await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[0], CancellationToken.None)).Status);
        Assert.Equal(StringEntryStatus.Error, (await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[1], CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public async Task BatchResponse_InvalidEntriesCannotBecomeCompletedTranslations(string kind)
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null), ("World", "MESG", null));
        fixture.Client.ResponseOverride = (call, _) => call != 1 ? null : kind switch
        {
            "empty" => JsonSerializer.Serialize(new { translations = new[] { new { id = fixture.Ids[0], text = "" }, new { id = fixture.Ids[1], text = "World" } } }),
            "unknown" => JsonSerializer.Serialize(new { translations = new[] { new { id = long.MaxValue, text = "bogus" }, new { id = fixture.Ids[1], text = "bogus" } } }),
            _ => JsonSerializer.Serialize(new { translations = new[] { new { id = fixture.Ids[0], text = "bogus" }, new { id = fixture.Ids[0], text = "duplicate" } } }),
        };
        await fixture.Service.TranslateIdsAsync(fixture.Request with { BatchSize = 2 });
        Assert.All(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None), row =>
        {
            Assert.Equal(StringEntryStatus.Done, row.Status);
            Assert.Equal(row.SourceText, row.DestText);
        });
        Assert.Equal(kind == "empty" ? 2 : 3, fixture.Client.Calls);
    }

    [Fact]
    public async Task RowRecoveryCap_StopsRepeatedOutputRepair()
    {
        await using var fixture = await Fixture.CreateAsync(("Hello %s", "MESG:DESC", null));
        fixture.Client.ResponseOverride = (_, _) => "invalid output without required token";
        await fixture.Service.TranslateIdsAsync(fixture.Request with { MaxRetryGenerations = 1 });
        Assert.Equal(2, fixture.Client.Calls); // One initial generation, one recovery.
        Assert.Equal(StringEntryStatus.Error, (await fixture.Db.GetStringTranslationStateAsync(fixture.Ids[0], CancellationToken.None)).Status);
    }

    [Fact]
    public async Task SeedRowRecoveryLimit_PreservesPeerAndContinuesRemainingWork()
    {
        await using var fixture = await Fixture.CreateAsync(("Ancient Sword", "WEAP:FULL", null),
            ("Ancient Shield", "ARMO:FULL", null), ("Please bring Ancient Sword. Please bring Ancient Shield.", "INFO:NAM1", null));
        fixture.Client.ResponseOverride = (call, _) => call == 1
            ? JsonSerializer.Serialize(new { translations = new[] { new { id = fixture.Ids[0], text = "Ancient Sword" } } }) : null;
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        { BatchSize = 2, EnableSessionTermMemory = true, MaxRetryGenerations = 0 });
        Assert.Equal(2, fixture.Client.Calls);
        var statuses = await fixture.Db.GetStringStatusesByIdsAsync(fixture.Ids, CancellationToken.None);
        Assert.Equal(StringEntryStatus.Done, statuses[fixture.Ids[0]]);
        Assert.Equal(StringEntryStatus.Error, statuses[fixture.Ids[1]]);
        Assert.Equal(StringEntryStatus.Done, statuses[fixture.Ids[2]]);
    }

    [Fact]
    public async Task ValidInitialBookChunks_DoNotSpendRecoveryAllowance()
    {
        var source = string.Concat(Enumerable.Repeat("Long source text. ", 300));
        await using var fixture = await Fixture.CreateAsync((source, "BOOK:DESC", null));
        await fixture.Service.TranslateIdsAsync(fixture.Request with { MaxChars = 1000, MaxRetryGenerations = 0 });
        Assert.True(fixture.Client.Calls > 1);
        var row = Assert.Single(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None));
        Assert.Equal(StringEntryStatus.Done, row.Status);
        Assert.Equal(source, row.DestText);
    }

    [Theory]
    [InlineData(false, 65536)]
    [InlineData(true, 4096)]
    public async Task AdaptiveOutputBudget_IsOptInAndReachesActualRequest(bool enabled, int expected)
    {
        await using var fixture = await Fixture.CreateAsync(("Hello", "MESG", null));
        await fixture.Service.TranslateIdsAsync(fixture.Request with { MaxOutputTokens = 65536, EnableAdaptiveOutputBudget = enabled });
        Assert.Equal(expected, Assert.Single(fixture.Client.Requests).GenerationConfig!.MaxOutputTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BookReference_UsesSelectedExactTitleAndOriginalNeighborsOnlyWhenEnabled(bool enabled)
    {
        var source = string.Concat(Enumerable.Repeat("Before the first sunrise. After the last sunset. ", 100));
        await using var fixture = await Fixture.CreateAsync(("My Book Title", "BOOK:FULL", "Book01"), (source, "BOOK:DESC", "Book01"));
        await fixture.Service.TranslateIdsAsync(fixture.Request with { MaxChars = 1000, EnableBookContext = enabled });
        var chunks = fixture.Client.Requests.Where(r => r.Purpose == "translate-chunk").ToArray();
        Assert.NotEmpty(chunks);
        Assert.Equal(enabled, chunks.Any(r => r.Contents[0].Parts[0].Text!.Contains("Next source excerpt")));
        Assert.Equal(enabled, chunks.Any(r => r.Contents[0].Parts[0].Text!.Contains("Book title (source): My Book Title")));
        Assert.All(await fixture.Db.GetStringsAsync(10, 0, CancellationToken.None), row => Assert.Equal(StringEntryStatus.Done, row.Status));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path;
        public ProjectDb Db { get; }
        public EchoClient Client { get; } = new();
        public TranslationService Service { get; }
        public IReadOnlyList<long> Ids { get; }
        public TranslateIdsRequest Request => new(
            "DUMMY", "gemini-2.5-flash", "english", "english", "base", Ids,
            BatchSize: 1, MaxChars: 5000, MaxConcurrency: 1, Temperature: 0,
            MaxOutputTokens: 1024, MaxRetries: 0, UseRecStyleHints: true,
            EnableRepairPass: false, EnableSessionTermMemory: false,
            OnRowUpdated: null, WaitIfPaused: null, CancellationToken: CancellationToken.None,
            EnablePromptCache: false, EnableRiskyCandidateRerank: false);

        private Fixture(string path, ProjectDb db, IReadOnlyList<long> ids)
        {
            _path = path;
            Db = db;
            Ids = ids;
            Service = new TranslationService(db, Client);
        }

        public static async Task<Fixture> CreateAsync(params (string Source, string Rec, string? Edid)[] rows)
        {
            var path = Path.Combine(Path.GetTempPath(), $"xt-integrity-{Guid.NewGuid():N}.sqlite");
            var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            await db.UpsertProjectAsync(new ProjectInfo(1, "dummy.xml", "Dummy", null, "english", "english",
                "1", false, "<?xml version=\"1.0\"?>", "gemini-2.5-flash", "base", null, false, now, now), CancellationToken.None);
            await db.BulkInsertStringsAsync(rows.Select((row, index) => (
                OrderIndex: index, ListAttr: (string?)null, PartialAttr: (string?)null, AttributesJson: (string?)null,
                Edid: row.Edid, Rec: (string?)row.Rec, SourceText: row.Source, DestText: "",
                Status: StringEntryStatus.Pending, RawStringXml: "<r/>"
            )).ToArray(), CancellationToken.None);
            return new Fixture(path, db, await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            TestDbHelper.TryDeleteDbFiles(_path);
        }
    }

    private sealed class EchoClient : IGeminiClient
    {
        public int Calls { get; private set; }
        public int CacheCreates { get; private set; }
        public int CacheDeletes { get; private set; }
        public bool CacheUnavailable { get; set; }
        public List<GeminiGenerateContentRequest> Requests { get; } = new();
        public Func<int, CancellationToken, Task>? BeforeGenerate { get; set; }
        public Func<int, GeminiGenerateContentRequest, string?>? ResponseOverride { get; set; }

        public async Task<string> GenerateContentAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request);
            if (BeforeGenerate != null)
            {
                await BeforeGenerate(Calls, cancellationToken);
            }
            if (ResponseOverride?.Invoke(Calls, request) is { } response) return response;
            var prompt = request.Contents[0].Parts[0].Text!;
            if (prompt.Contains("Input JSON:", StringComparison.Ordinal))
            {
                // Deliberately exercise the batch-to-single-row fallback.
                return "invalid batch JSON";
            }
            const string start = "<<<TEXT";
            const string end = "TEXT>>>";
            var content = prompt[(prompt.IndexOf(start, StringComparison.Ordinal) + start.Length)..];
            content = content.TrimStart('\r', '\n');
            content = content[..content.LastIndexOf(end, StringComparison.Ordinal)].TrimEnd('\r', '\n');
            return GlossarySemanticHintInjector.Strip(PlaceholderSemanticHintInjector.Strip(content));
        }

        public async Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
            => new[] { await GenerateContentAsync(apiKey, modelName, request, cancellationToken) };

        public Task<int> CountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
            => Task.FromResult(Math.Max(1, text.Length / 4));
        public Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<string> CreateCachedContentAsync(string apiKey, string modelName, string systemInstructionText, TimeSpan ttl, CancellationToken cancellationToken)
        {
            CacheCreates++;
            if (CacheUnavailable)
                throw new GeminiException("Fixture cache unavailable");
            return Task.FromResult("cachedContents/fixture");
        }
        public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken)
        {
            CacheDeletes++;
            return Task.CompletedTask;
        }
    }
}
