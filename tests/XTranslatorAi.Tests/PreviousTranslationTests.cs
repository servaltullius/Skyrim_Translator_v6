using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

public class PreviousTranslationTests
{
    [Fact]
    public void Matcher_KeepsOnlyRealTranslationsOfTheSameField()
    {
        var current = new[]
        {
            Field("MGEF/05000867/0/FULL/0", "Corpse mountain Blood Sea chop hit 1"),
            Field("MGEF/05000868/0/FULL/0", "Elden Parry"),
            Field("MGEF/05000869/0/FULL/0", "MCO Attack"),
            Field("MGEF/0500086A/0/FULL/0", "New in this release"),
        };
        var previous = new[]
        {
            Field("MGEF/05000867/0/FULL/0", "시산혈해 참격 타격 1"),
            Field("MGEF/05000868/0/FULL/0", "Elden Parry"),
            Field("MGEF/05000869/0/FULL/0", "MCO Strike"),
        };

        var result = PreviousTranslationMatcher.Match(current, previous, requireHangul: true);

        Assert.Equal("시산혈해 참격 타격 1", Assert.Single(result.TextByFieldKey).Value);
        Assert.Equal((1, 1, 1), (result.NotInPreviousRelease, result.SameAsSource, result.NotTranslated));
    }

    /// <summary>
    /// v2 adds a master, so v1's own records (02xxxxxx) are 03xxxxxx in v2 and 02xxxxxx now names the new master.
    /// Pairing by key gave v2's override of a new-master record the translation of v1's own record.
    /// </summary>
    [Fact]
    public void Matcher_MapsFormIdsThroughTheMasterListsByFileName()
    {
        var currentMasters = new[] { "Skyrim.esm", "Update.esm", "NewDependency.esp" };
        var previousMasters = new[] { "Skyrim.esm", "Update.esm" };
        var current = new[]
        {
            PluginFieldAt("WEAP", 0x03000800, "ModSword", "FULL", 0, "Blood Sea Blade"),
            PluginFieldAt("WEAP", 0x02000800, "DepSword", "FULL", 0, "Dependency Sword"),
            PluginFieldAt("WEAP", 0x00012EB7, "IronSword", "FULL", 0, "Iron Sword"),
        };
        var previous = new[]
        {
            PluginFieldAt("WEAP", 0x02000800, "ModSword", "FULL", 0, "시산혈해 검"),
            PluginFieldAt("WEAP", 0x00012EB7, "IronSword", "FULL", 0, "철검"),
        };

        var result = PreviousTranslationMatcher.Match(current, currentMasters, previous, previousMasters, requireHangul: true);

        Assert.Equal("시산혈해 검", result.TextByFieldKey[current[0].Key]);
        Assert.Equal("철검", result.TextByFieldKey[current[2].Key]);
        Assert.False(result.TextByFieldKey.ContainsKey(current[1].Key));
    }

    [Fact]
    public void Matcher_SkipsRecordsOfAMasterTheCurrentReleaseDropped()
    {
        var current = new[] { PluginFieldAt("WEAP", 0x01000800, "ModSword", "FULL", 0, "Blood Sea Blade") };
        var previous = new[] { PluginFieldAt("WEAP", 0x01000800, "OldDepSword", "FULL", 0, "옛 검") };

        // v1 had Skyrim.esm + OldDependency.esp, so its 01000800 belongs to OldDependency.esp; v2's is its own record.
        var result = PreviousTranslationMatcher.Match(current, new[] { "Skyrim.esm" }, previous,
            new[] { "Skyrim.esm", "OldDependency.esp" }, requireHangul: true);

        Assert.Empty(result.TextByFieldKey);
        Assert.Equal(1, result.NotInPreviousRelease);
    }

    [Fact]
    public void Matcher_DoesNotPairRecordsWithAnotherEditorId()
    {
        var current = new[] { PluginFieldAt("MESG", 0x01000900, "MsgRestWarning", "FULL", 0, "Rest") };
        var previous = new[] { PluginFieldAt("MESG", 0x01000900, "MsgTravelPrompt", "FULL", 0, "여행") };

        var result = PreviousTranslationMatcher.Match(current, new[] { "Skyrim.esm" }, previous, new[] { "Skyrim.esm" }, requireHangul: true);

        Assert.Empty(result.TextByFieldKey);
        Assert.Equal(1, result.ChangedRecord);
    }

    /// <summary>
    /// v2 inserts a second quest objective: pairing by position gave objective 2 the translation of objective 1's
    /// successor. Only the quest name, whose count did not change, is paired.
    /// </summary>
    [Fact]
    public void Matcher_SkipsRepeatedSubrecordsWhoseCountChanged()
    {
        var current = new[]
        {
            PluginFieldAt("QUST", 0x01000D62, "ModQuest", "FULL", 0, "The Blood Sea"),
            PluginFieldAt("QUST", 0x01000D62, "ModQuest", "NNAM", 0, "Find the blade"),
            PluginFieldAt("QUST", 0x01000D62, "ModQuest", "NNAM", 1, "Speak to the smith"),
            PluginFieldAt("QUST", 0x01000D62, "ModQuest", "NNAM", 2, "Return the blade"),
        };
        var previous = new[]
        {
            PluginFieldAt("QUST", 0x01000D62, "ModQuest", "FULL", 0, "피의 바다"),
            PluginFieldAt("QUST", 0x01000D62, "ModQuest", "NNAM", 0, "검 찾기"),
            PluginFieldAt("QUST", 0x01000D62, "ModQuest", "NNAM", 1, "검 돌려주기"),
        };

        var result = PreviousTranslationMatcher.Match(current, new[] { "Skyrim.esm" }, previous, new[] { "Skyrim.esm" }, requireHangul: true);

        Assert.Equal("피의 바다", Assert.Single(result.TextByFieldKey).Value);
        Assert.Equal(2, result.ChangedRecord);
    }

    [Fact]
    public void Reference_DropsMarkupAndLimitsLength()
    {
        var reference = TranslationPreviousReference.Build("<font face='$DaedricFont'>적에게 <mag>의 피해[pagebreak]" + new string('가', 900));

        Assert.NotNull(reference);
        Assert.Contains("Earlier translation: 적에게 의 피해", reference);
        Assert.DoesNotContain("<mag>", reference);
        Assert.DoesNotContain("[pagebreak]", reference);
        Assert.EndsWith("…", reference);
        Assert.Null(TranslationPreviousReference.Build("<br>"));
    }

    [Fact]
    public async Task References_AreKeptByFieldKey_AcrossReopeningThePlugin()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-previous-{Guid.NewGuid():N}.sqlite");
        var input = Path.Combine(Path.GetTempPath(), $"xt-previous-{Guid.NewGuid():N}.esp");
        await File.WriteAllBytesAsync(input, PluginProjectIntegrationTests.CreateMinimalPlugin());
        var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
        try
        {
            var document = await PluginReader.ReadAsync(input, new PluginReadOptions(), CancellationToken.None);
            var project = new ProjectInfo(1, "", "Test.esp", BethesdaFranchise.ElderScrolls, "english", "korean", "", false,
                "", "model", "", "", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, project, "utf-8", CancellationToken.None);
            var key = Assert.Single(document.Fields).Key;

            await db.ReplacePreviousTranslationsAsync("Old.esp", new Dictionary<string, string> { [key] = "철검" }, CancellationToken.None);
            // Reopening re-imports the rows with new ids and clears row notes.
            var reimported = await db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, project, "utf-8", CancellationToken.None);

            var byId = await db.GetPreviousTranslationsByStringIdAsync(CancellationToken.None);
            Assert.Equal("철검", byId[Assert.Single(reimported).Id]);
            Assert.Equal(("Old.esp", 1), await db.GetPreviousTranslationSourceAsync(CancellationToken.None));

            await db.ClearPreviousTranslationsAsync(CancellationToken.None);
            Assert.Empty(await db.GetPreviousTranslationsByStringIdAsync(CancellationToken.None));
            Assert.Null(await db.GetPreviousTranslationSourceAsync(CancellationToken.None));
        }
        finally
        {
            await db.DisposeAsync();
            TestDbHelper.TryDeleteDbFiles(path);
            File.Delete(input);
        }
    }

    private static PluginField Field(string key, string source)
        => new(key, 0, "MGEF", "FULL", 0x05000867u, "ES_BloodCombo1", 1, 1, source);

    /// <summary>A field keyed the way <see cref="PluginReader"/> keys it.</summary>
    private static PluginField PluginFieldAt(string record, uint formId, string editorId, string subrecord, int ordinal, string source)
        => new($"{record}/{formId:X8}/0/{subrecord}/{ordinal}", 0, record, subrecord, formId, editorId, 1, 1, source);
}
