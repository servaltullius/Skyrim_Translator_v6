using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public sealed class GameStringDictionaryTests
{
    private static readonly string[] ModMasters = { "Skyrim.esm", "Update.esm", "Dawnguard.esm" };

    private static GameStringDictionary Build()
    {
        var skyrim = new GameStringPlugin("Skyrim.esm", new string[0], new[]
        {
            Field("INFO/00012345/0/NAM1/0", "What do you want?", PluginStringTableKind.IlStrings, 1),
            Field("INFO/00012346/0/NAM1/0", "What do you want?", PluginStringTableKind.IlStrings, 2),
            Field("SPEL/0002B96C/0/FULL/0", "Ice Storm", PluginStringTableKind.Strings, 3),
            Field("SPEL/0002B96D/0/FULL/0", "Ice Storm", PluginStringTableKind.Strings, 4),
            Field("BOOK/00001000/0/DESC/0", "A long book.", PluginStringTableKind.DlStrings, 5),
            Field("WEAP/00001001/0/FULL/0", "Iron Sword", PluginStringTableKind.Strings, 6),
            Field("WEAP/00001002/0/FULL/0", "Untranslated", PluginStringTableKind.Strings, 7),
        }, new Dictionary<(PluginStringTableKind, uint), string>
        {
            [(PluginStringTableKind.IlStrings, 1)] = "뭐가 필요한가?",
            [(PluginStringTableKind.IlStrings, 2)] = "뭐가 필요합니까?",
            [(PluginStringTableKind.Strings, 3)] = "얼음 폭풍",
            [(PluginStringTableKind.Strings, 4)] = "얼음 폭풍",
            [(PluginStringTableKind.DlStrings, 5)] = "긴 책.",
            [(PluginStringTableKind.Strings, 6)] = "강철 검",
            [(PluginStringTableKind.Strings, 7)] = "Untranslated",
        });
        // Dawnguard overrides Skyrim's Iron Sword (00), rewrites the book, and adds its own record (02).
        var dawnguard = new GameStringPlugin("Dawnguard.esm", new[] { "Skyrim.esm", "Update.esm" }, new[]
        {
            Field("WEAP/00001001/0/FULL/0", "Iron Sword", PluginStringTableKind.Strings, 10),
            Field("WEAP/02000800/0/FULL/0", "Dawnguard Rune Axe", PluginStringTableKind.Strings, 11),
            Field("BOOK/00001000/0/DESC/0", "A longer book.", PluginStringTableKind.DlStrings, 12),
        }, new Dictionary<(PluginStringTableKind, uint), string>
        {
            [(PluginStringTableKind.Strings, 10)] = "철검",
            [(PluginStringTableKind.Strings, 11)] = "던가드 룬 도끼",
            [(PluginStringTableKind.DlStrings, 12)] = "더 긴 책.",
        });
        return GameStringDictionary.Build(new[] { skyrim, dawnguard });
    }

    private static PluginField Field(string key, string text, PluginStringTableKind kind, uint id)
    {
        var parts = key.Split('/');
        return new PluginField(key, 0, parts[0], parts[3], uint.Parse(parts[1], System.Globalization.NumberStyles.HexNumber),
            null, 0, 0, text, kind, id);
    }

    private static string? Identity(string modKey) => GameStringDictionary.FieldIdentity(modKey, ModMasters, "SeranaDialog.esp");

    // A mod that overrides a vanilla line keeps the speaker, so the record's own Korean is safe even for dialogue.
    [Fact]
    public void OverriddenRecordWithTheSameEnglish_TakesThatRecordsKorean()
    {
        var game = Build();

        Assert.Equal("뭐가 필요합니까?", game.Find(Identity("INFO/00012346/0/NAM1/0"), "INFO:NAM1", "What do you want?")!.Text);
        Assert.Equal("뭐가 필요한가?", game.Find(Identity("INFO/00012345/0/NAM1/0"), "INFO:NAM1", "What do you want?")!.Text);
        // Dawnguard (master index 2) defines its own record.
        Assert.Equal("던가드 룬 도끼", game.Find(Identity("WEAP/02000800/0/FULL/0"), "WEAP:FULL", "Dawnguard Rune Axe")!.Text);
        // A later master's text replaces the earlier one.
        Assert.Equal("철검", game.Find(Identity("WEAP/00001001/0/FULL/0"), "WEAP:FULL", "Iron Sword")!.Text);
    }

    [Fact]
    public void ChangedText_OrTheModsOwnRecord_IsNotTheGamesLine()
    {
        var game = Build();

        Assert.Null(game.Find(Identity("INFO/00012345/0/NAM1/0"), "INFO:NAM1", "What do you want, stranger?"));
        // Index 3 is past the masters: the mod's own record, even with a vanilla-looking ID.
        Assert.Null(game.Find(Identity("INFO/03012345/0/NAM1/0"), "INFO:NAM1", "What do you want?"));
    }

    [Fact]
    public void SameText_IsMatchedOutsideDialogue_ByTheMostCommonSpelling()
    {
        var game = Build();

        Assert.Equal("얼음 폭풍", game.Find(null, "SPEL:FULL", "Ice Storm")!.Text);
        Assert.Equal("긴 책.", game.Find(null, "BOOK:DESC", "A long book.")!.Text);
        // The patch words a dialogue line by speaker, so the text alone does not decide it.
        Assert.Null(game.Find(null, "INFO:NAM1", "What do you want?"));
        // An untranslated patch string is no translation.
        Assert.Null(game.Find(null, "WEAP:FULL", "Untranslated"));
        // The game shows Dawnguard's text for the record it overrides; the overridden text does not count. Skyrim's
        // "Smithing" skill was 뼈다귀 제련 under Update.esm's 제련, and with a Dragonborn perk outvoted it 2 to 1.
        Assert.Equal("철검", game.Find(null, "WEAP:FULL", "Iron Sword")!.Text);
        // An override that changes the English leaves the earlier pair a translation of its own English ("A long book."
        // above): Update.esm renames a staff Skyrim.esm calls Alteration Staff (변화마법 지팡이).
        Assert.Equal("더 긴 책.", game.Find(null, "BOOK:DESC", "A longer book.")!.Text);
    }

    // Through a real plugin project: rows know their field keys and the plugin's masters from the import.
    [Fact]
    public async Task PluginProjectRows_AreMatchedByTheirRecordAndText()
    {
        var root = Path.Combine(Path.GetTempPath(), "xt-game-match-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "project.sqlite");
        var db = await ProjectDb.OpenOrCreateAsync(dbPath, CancellationToken.None);
        try
        {
            var source = new PluginSourceInfo(Path.Combine(root, "SeranaDialog.esp"), "hash", new PluginReadOptions(), false,
                new[] { "Skyrim.esm" }, Array.Empty<PluginDiagnostic>());
            var project = new ProjectInfo(1, "", "SeranaDialog.esp", BethesdaFranchise.ElderScrolls, "english", "korean", "", false, "", "test", "", null,
                false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await db.ReplaceImportedPluginStringsAsync(source, new[]
            {
                new PluginField("INFO/00012346/0/NAM1/0", 0, "INFO", "NAM1", 0x00012346, null, 1, 1, "What do you want?"),
                new PluginField("INFO/01000800/0/NAM1/0", 1, "INFO", "NAM1", 0x01000800, null, 2, 1, "What do you want?"),
                new PluginField("SPEL/01000801/0/FULL/0", 2, "SPEL", "FULL", 0x01000801, null, 3, 1, "Ice Storm"),
            }, project, "utf-8", CancellationToken.None);
            var rows = await db.GetStringsAsync(10, 0, CancellationToken.None);

            var matches = await Build().MatchAsync(db, rows.Select(r => r.Id).ToList(), CancellationToken.None);

            Assert.Equal("뭐가 필요합니까?", matches[rows[0].Id].Text);
            Assert.Equal("Skyrim.esm 같은 레코드", matches[rows[0].Id].Origin);
            Assert.False(matches.ContainsKey(rows[1].Id));
            Assert.Equal("얼음 폭풍", matches[rows[2].Id].Text);
        }
        finally
        {
            await db.DisposeAsync();
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(dbPath);
            Directory.Delete(root, recursive: true);
        }
    }

    // The app ships a dictionary, so it keeps only fingerprints of the game's English: the text itself is Bethesda's.
    [Fact]
    public void SavedDictionary_HoldsNoEnglishText()
    {
        using var stream = new MemoryStream();
        Build().Save(stream);
        stream.Position = 0;
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        var text = new StreamReader(gzip).ReadToEnd();

        Assert.Contains("얼음 폭풍", text);
        Assert.DoesNotContain("Ice Storm", text);
        Assert.DoesNotContain("What do you want", text);
        Assert.DoesNotContain("A long book", text);
    }

    [Fact]
    public void SavedDictionary_LoadsTheSameMatches()
    {
        using var stream = new MemoryStream();
        Build().Save(stream);
        stream.Position = 0;
        var loaded = GameStringDictionary.Load(stream);

        Assert.Equal(Build().FieldCount, loaded.FieldCount);
        Assert.Equal(Build().TextCount, loaded.TextCount);
        Assert.Equal("뭐가 필요합니까?", loaded.Find(Identity("INFO/00012346/0/NAM1/0"), "INFO:NAM1", "What do you want?")!.Text);
        Assert.Equal("긴 책.", loaded.Find(null, "BOOK:DESC", "A long book.")!.Text);
    }
}
