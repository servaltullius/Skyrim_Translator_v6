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
}
