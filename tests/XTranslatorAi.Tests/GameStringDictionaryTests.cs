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

    // A patch that fixes a vanilla line's English (USSEP) is reviewed against the line it replaces.
    [Fact]
    public void RecordText_IsFoundForChangedEnglish_AndSaysSo()
    {
        var game = Build();

        Assert.Equal(("뭐가 필요한가?", false), game.FindRecord(Identity("INFO/00012345/0/NAM1/0"), "What do you want, stranger?"));
        Assert.Equal(("뭐가 필요한가?", true), game.FindRecord(Identity("INFO/00012345/0/NAM1/0"), "What do you want?"));
        Assert.Null(game.FindRecord(Identity("INFO/03012345/0/NAM1/0"), "What do you want?"));
    }

    /// <summary>
    /// Game 1.6 renamed some 1.5.97 strings "Creation Club Cell" for its hidden Creation Club cells, while a Dragonborn
    /// magic effect still points at one; the patch translates that string for what the 1.5.97 game shows there. The
    /// record keeps its translation, but the placeholder text is no text to translate any other record's name by.
    /// </summary>
    [Fact]
    public void CreationClubCellPlaceholder_IsMatchedByRecordOnly()
    {
        var dragonborn = new GameStringPlugin("Dragonborn.esm", new[] { "Skyrim.esm" }, new[]
        {
            Field("MGEF/010145CD/0/FULL/0", "Creation Club Cell", PluginStringTableKind.Strings, 0xDF),
        }, new Dictionary<(PluginStringTableKind, uint), string>
        {
            [(PluginStringTableKind.Strings, 0xDF)] = "폭발형 거미",
        });

        var game = GameStringDictionary.Build(new[] { dragonborn });

        Assert.Null(game.Find(null, "CELL:FULL", "Creation Club Cell"));
        Assert.Equal(("폭발형 거미", true), game.FindRecord(
            GameStringDictionary.FieldIdentity("MGEF/010145CD/0/FULL/0", new[] { "Skyrim.esm" }, "Dragonborn.esm"), "Creation Club Cell"));
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

    private const string FalmerEnglish =
        "[pagebreak]\r\n<p align=\"center\">The Falmer: A Study\r\n\r\nby\r\nUrsa Uthrax</p>\r\n\r\n[pagebreak]\r\n<p align=\"left\">"
        + "<img src='img://Textures/Interface/Books/Illuminated_Letters/I_letter.png'> have studied, and traveled, and my hypothesis has finally been confirmed. \r\n\r\n"
        + "No one really knows when the story of the Snow Elves began, but the ancient work tells of its ending.\r\n\r\n"
        + "But as is always the story with slaves and their masters, the Falmer rose up. The overthrew the dwarves, and fled.\r\n\r\n"
        + "For decade upon decade, the two sides waged a bitter conflict, the War of the Crag, deep below Skyrim.\r\n";

    private static GameStringDictionary WithoutLines(byte[] gz, string prefix)
    {
        string text;
        using (var reader = new StreamReader(new System.IO.Compression.GZipStream(new MemoryStream(gz), System.IO.Compression.CompressionMode.Decompress)))
        {
            text = reader.ReadToEnd();
        }

        var kept = string.Join("\n", text.Split('\n').Where(line => !line.StartsWith(prefix, StringComparison.Ordinal)));
        using var output = new MemoryStream();
        using (var writer = new StreamWriter(new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true)))
        {
            writer.Write(kept);
        }

        output.Position = 0;
        return GameStringDictionary.Load(output);
    }

    private static GameStringDictionary BuildWithBook()
        => GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[]
            {
                Field("BOOK/000E0D68/0/DESC/0", FalmerEnglish, PluginStringTableKind.DlStrings, 1),
                Field("INFO/00012345/0/NAM1/0", FalmerEnglish, PluginStringTableKind.IlStrings, 2),
            }, new Dictionary<(PluginStringTableKind, uint), string>
            {
                [(PluginStringTableKind.DlStrings, 1)] = "팔머: 연구 … 크래그 전쟁 …",
                [(PluginStringTableKind.IlStrings, 2)] = "대사",
            }),
        });

    /// <summary>
    /// LotD's copy of a base game book drops trailing spaces and fixes a typo ("The overthrew"), so the whole text never
    /// matches; most of its paragraphs do, and the Korean patch's text is offered as a reference. A different book, a
    /// short text and dialogue are not.
    /// </summary>
    [Fact]
    public void EditedCopyOfABaseGameBook_FindsItsKoreanAsAReference()
    {
        var game = BuildWithBook();
        var lotd = FalmerEnglish.Replace(". \r\n", ".\r\n").Replace("The overthrew", "They overthrew").TrimEnd();

        Assert.Null(game.Find("BOOK/skyrim.esm:0E0D68/0/DESC/0", "BOOK:DESC", lotd));
        var similar = game.FindSimilar("BOOK:DESC", lotd);
        Assert.Equal("팔머: 연구 … 크래그 전쟁 …", similar!.Text);
        Assert.Contains("비슷한 글 3/4문단", similar.Origin);
        // The changed paragraph is named, for the request to translate only it.
        Assert.Equal(new[] { "But as is always the story with slaves and their masters, the Falmer rose up. They overthrew the dwarves, and fled." },
            similar.ChangedParagraphs);
        Assert.Null(game.FindSameParagraphs("BOOK:DESC", lotd));

        Assert.Null(game.FindSimilar("BOOK:DESC", "A different book about something else entirely, and long enough to count.\n\nIts second paragraph is also nothing like the base game's text.\n\nNor is its third paragraph, which closes the book."));
        Assert.Null(game.FindSimilar("BOOK:DESC", "No one really knows when the story of the Snow Elves began, but the ancient work tells of its ending."));
        Assert.Null(game.FindSimilar("INFO:NAM1", lotd));
        Assert.Equal(1, game.ParagraphTextCount);
    }

    /// <summary>
    /// A copy that changes only spacing and markup (74 of LotD's books) is the base game's text: its Korean is applied.
    /// One that also dropped a paragraph is not.
    /// </summary>
    [Fact]
    public void CopyChangingOnlySpacingAndMarkup_TakesTheKoreanAsTheGamesText()
    {
        var game = BuildWithBook();
        var spaced = FalmerEnglish.Replace(". \r\n", ".\r\n").Replace("<p align=\"left\">", "<p align=\"justify\">").TrimEnd();

        var same = game.FindSameParagraphs("BOOK:DESC", spaced);
        Assert.Equal("팔머: 연구 … 크래그 전쟁 …", same!.Text);
        Assert.Equal("Skyrim.esm 같은 글(공백·꾸밈만 다름)", same.Origin);
        Assert.Null(same.ChangedParagraphs);

        var shorter = spaced.Replace("For decade upon decade, the two sides waged a bitter conflict, the War of the Crag, deep below Skyrim.", "");
        Assert.Null(game.FindSameParagraphs("BOOK:DESC", shorter));
        Assert.NotEmpty(game.FindSimilar("BOOK:DESC", shorter)!.ChangedParagraphs!);

        // A copy that changed only a short line (the author) keeps every long paragraph, but the patch's old author
        // line would not fit it: it is translated with the reference instead of filled.
        var newAuthor = spaced.Replace("Ursa Uthrax", "Ursa Uthrax the Elder");
        Assert.Null(game.FindSameParagraphs("BOOK:DESC", newAuthor));
        Assert.Empty(game.FindSimilar("BOOK:DESC", newAuthor)!.ChangedParagraphs!);
    }

    private const string PatchKorean =
        "[pagebreak]\r\n<p align=\"center\">팔머: 연구\r\n\r\n저자\r\n우르사 우스락스</p>\r\n\r\n[pagebreak]\r\n<p align=\"left\">"
        + "<img src='img://Textures/Interface/Books/Illuminated_Letters/I_letter.png'>연구, 여행을 거쳐 드디어 나의 가설이 뒷받침되었다. \r\n\r\n"
        + "스노우 엘프의 이야기가 언제 시작되었는지 아는 사람은 없다.\r\n\r\n"
        + "팔머도 반역을 일으켰다. 드워프를 타도하며 도망쳤다.\r\n\r\n"
        + "몇 십년이나 양쪽 모두 격렬하게 싸웠다. '크래그 전쟁'이 스카이림의 아래에서 일어났다.\r\n";

    /// <summary>
    /// The model's translation of an edited copy keeps only the changed block: the blocks the mod left as the base
    /// game wrote them are the Korean patch's, with the source's spacing. A model that retranslated 크래그 전쟁 is
    /// overruled where the English is the base game's.
    /// </summary>
    [Fact]
    public void ModelTranslationOfAnEditedCopy_TakesThePatchsBlocksWhereTheEnglishIsTheBaseGames()
    {
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[] { Field("BOOK/000E0D68/0/DESC/0", FalmerEnglish, PluginStringTableKind.DlStrings, 1) },
                new Dictionary<(PluginStringTableKind, uint), string> { [(PluginStringTableKind.DlStrings, 1)] = PatchKorean }),
        });
        var lotd = FalmerEnglish.Replace(". \r\n", ".\r\n").Replace("The overthrew", "They overthrew").TrimEnd();
        var model = "[pagebreak]\r\n<p align=\"center\">팔머: 연구\r\n\r\n저자\r\n우르사 우스락스</p>\r\n\r\n[pagebreak]\r\n<p align=\"left\">"
                    + "<img src='img://Textures/Interface/Books/Illuminated_Letters/I_letter.png'>나는 연구했다.\r\n\r\n"
                    + "아무도 모른다.\r\n\r\n"
                    + "팔머는 봉기했다. 그들은 드워프를 쓰러뜨리고 달아났다.\r\n\r\n"
                    + "수십 년 동안 '암벽 전쟁'이 벌어졌다.";

        var spliced = GameStringDictionary.SpliceUnchanged(lotd, model, game.FindSimilar("BOOK:DESC", lotd)!);

        Assert.Contains("연구, 여행을 거쳐 드디어 나의 가설이 뒷받침되었다.\r\n\r\n", spliced);
        Assert.Contains("'크래그 전쟁'", spliced);
        Assert.DoesNotContain("암벽 전쟁", spliced);
        Assert.Contains("팔머는 봉기했다. 그들은 드워프를 쓰러뜨리고 달아났다.", spliced);
        Assert.False(spliced!.EndsWith("\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// The patch's blocks pair with the source's by position. A copy that drops one of the base game's paragraphs and
    /// adds its own keeps the block count, so the base game's paragraphs after the gap stand where the patch has other
    /// ones: those keep the model's text. A copy with a paragraph added (or dropped) pairs nothing.
    /// </summary>
    [Fact]
    public void BlocksOutOfTheirBaseGamePosition_KeepTheModelsText()
    {
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[] { Field("BOOK/000E0D68/0/DESC/0", FalmerEnglish, PluginStringTableKind.DlStrings, 1) },
                new Dictionary<(PluginStringTableKind, uint), string> { [(PluginStringTableKind.DlStrings, 1)] = PatchKorean }),
        });
        const string secondEnglish = "No one really knows when the story of the Snow Elves began, but the ancient work tells of its ending.\r\n\r\n";
        const string added = "\r\n\r\nA new closing paragraph the mod wrote itself, long enough to be counted as one.";
        var shifted = FalmerEnglish.Replace(secondEnglish, "").TrimEnd() + added;
        var model = "[pagebreak]\r\n<p align=\"center\">팔머: 연구\r\n\r\n저자\r\n우르사 우스락스</p>\r\n\r\n[pagebreak]\r\n<p align=\"left\">"
                    + "<img src='img://Textures/Interface/Books/Illuminated_Letters/I_letter.png'>나는 연구했다.\r\n\r\n"
                    + "팔머는 봉기했다.\r\n\r\n"
                    + "수십 년 동안 전쟁이 벌어졌다.\r\n\r\n"
                    + "모드가 새로 쓴 마지막 문단.";

        var spliced = GameStringDictionary.SpliceUnchanged(shifted, model, game.FindSimilar("BOOK:DESC", shifted)!);

        // The first paragraph is where the base game has it; the next two are not.
        Assert.Contains("연구, 여행을 거쳐 드디어 나의 가설이 뒷받침되었다.", spliced);
        Assert.DoesNotContain("스노우 엘프의 이야기가", spliced);
        Assert.Contains("팔머는 봉기했다.", spliced);
        Assert.Contains("수십 년 동안 전쟁이 벌어졌다.", spliced);

        var longer = FalmerEnglish.TrimEnd() + added;
        Assert.Null(GameStringDictionary.SpliceUnchanged(longer, model, game.FindSimilar("BOOK:DESC", longer)!));
    }

    /// <summary>
    /// A copy that moved a short heading up one block keeps every long paragraph in the base game's order and the same
    /// block count. Pairing by the long paragraphs alone put the patch's heading (2부) in place of the paragraph that
    /// moved down; blocks now pair only where the whole block, short lines included, is the base game's.
    /// </summary>
    [Fact]
    public void BlockShiftedByAMovedHeading_KeepsTheModelsText()
    {
        const string first = "The first paragraph of the base game book, long enough to be counted.";
        const string second = "The second paragraph of the base game book, also long enough to count.";
        const string third = "The third paragraph of the base game book, long enough to be counted too.";
        const string fourth = "The fourth paragraph of the base game book, long enough to be counted again.";
        var english = string.Join("\r\n\r\n", "A Book", first, second, "Part Two", third, fourth);
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[] { Field("BOOK/000E0D68/0/DESC/0", english, PluginStringTableKind.DlStrings, 1) },
                new Dictionary<(PluginStringTableKind, uint), string>
                {
                    [(PluginStringTableKind.DlStrings, 1)] = string.Join("\r\n\r\n", "어떤 책", "첫 문단.", "둘째 문단.", "2부", "셋째 문단.", "넷째 문단."),
                }),
        });
        var moved = string.Join("\r\n\r\n", "A Book", first, "Interlude", second, third, fourth);
        var model = string.Join("\r\n\r\n", "모델의 제목", "모델의 첫 문단.", "막간", "모델의 둘째 문단.", "모델의 셋째 문단.", "모델의 넷째 문단.");

        var spliced = GameStringDictionary.SpliceUnchanged(moved, model, game.FindSimilar("BOOK:DESC", moved)!);

        Assert.Equal(string.Join("\r\n\r\n", "어떤 책", "첫 문단.", "막간", "모델의 둘째 문단.", "셋째 문단.", "넷째 문단."), spliced);
    }

    /// <summary>A copy with one blank line more than the base game's still takes the patch's text, laid out as the source is.</summary>
    [Fact]
    public void SameParagraphsWithOtherBlankLines_TakeThePatchLaidOutAsTheSource()
    {
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[] { Field("BOOK/000E0D68/0/DESC/0", FalmerEnglish, PluginStringTableKind.DlStrings, 1) },
                new Dictionary<(PluginStringTableKind, uint), string> { [(PluginStringTableKind.DlStrings, 1)] = PatchKorean }),
        });
        var lotd = FalmerEnglish.Replace("began, but", "began, but").Replace("its ending.\r\n\r\n", "its ending.\r\n\r\n\r\n").TrimEnd();

        var same = game.FindSameParagraphs("BOOK:DESC", lotd)!;

        Assert.Contains("아는 사람은 없다.\r\n\r\n\r\n팔머도", same.Text);
        Assert.False(same.Text.EndsWith("\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// The patch's Japhet's Journal breaks the line after its font tag where the English does not, so even the game's own
    /// text failed the line check: the Korean is laid out with the source's line breaks around the same tags.
    /// </summary>
    [Fact]
    public void PatchTextWithItsOwnLineBreaksAroundTags_IsLaidOutAsTheSource()
    {
        const string english = "<font face=\"$HandwrittenFont\">A smaller band than was expected joined with me departing Dawnstar today. \r\n[pagebreak]\r\n"
                               + "This first winter was difficult, with the abandonment of many men and women.\r\n[pagebreak]\r\n"
                               + "The stories of ghosts inhabiting this place are just stories, nothing more.\r\n</font>";
        const string korean = "<font face=\"$HandwrittenFont\">\r\n던스타를 출발할 때 예상보다 작은 무리가 합류했다.\r\n[pagebreak]\r\n"
                              + "최초의 겨울은 힘들었다.\r\n[pagebreak]\r\n이곳에 악령이 산다는 이야기는 그저 이야기일 뿐이다.\r\n</font>";
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[] { Field("BOOK/000F6844/0/DESC/0", english, PluginStringTableKind.DlStrings, 1) },
                new Dictionary<(PluginStringTableKind, uint), string> { [(PluginStringTableKind.DlStrings, 1)] = korean }),
        });

        var same = game.FindSameParagraphs("BOOK:DESC", english)!;

        Assert.StartsWith("<font face=\"$HandwrittenFont\">던스타를", same.Text);
        Assert.Equal(english.Count(c => c == '\n'), same.Text.Count(c => c == '\n'));
    }

    [Fact]
    public void ParagraphFingerprints_SurviveSaveAndLoad_AndCanBeAddedToAnOlderDictionary()
    {
        using var stream = new MemoryStream();
        BuildWithBook().Save(stream);
        stream.Position = 0;
        var loaded = GameStringDictionary.Load(stream);
        Assert.Equal(1, loaded.ParagraphTextCount);
        Assert.NotNull(loaded.FindSimilar("BOOK:DESC", FalmerEnglish.Replace("The overthrew", "They overthrew")));
        // The base game's blocks come back too, so the patch's blocks are still spliced in.
        Assert.Equal(BuildWithBook().FindSimilar("BOOK:DESC", FalmerEnglish)!.BaseBlocks,
            loaded.FindSimilar("BOOK:DESC", FalmerEnglish)!.BaseBlocks);
        Assert.Equal(6, loaded.FindSimilar("BOOK:DESC", FalmerEnglish)!.BaseBlocks!.Count);
        Assert.True(loaded.HasBaseBlocks);

        // A dictionary the player built before the blocks were kept splices nothing, and says so.
        var beforeBlocks = WithoutLines(stream.ToArray(), "PB\t");
        Assert.False(beforeBlocks.HasBaseBlocks);
        Assert.Null(beforeBlocks.FindSimilar("BOOK:DESC", FalmerEnglish)!.BaseBlocks);
        // The short lines' fingerprints come back too: the same text with other spacing is still filled.
        Assert.NotNull(loaded.FindSameParagraphs("BOOK:DESC", FalmerEnglish.Replace(". \r\n", ".\r\n")));
        Assert.Null(loaded.FindSameParagraphs("BOOK:DESC", FalmerEnglish.Replace("Ursa Uthrax", "Ursa")));

        // The shipped dictionary gains them from the game's English; a record whose English is not the one its Korean
        // translates is skipped.
        var older = Build();
        Assert.Equal(0, older.ParagraphTextCount);
        var added = older.WithParagraphs(new (string, string?, string)[]
        {
            ("BOOK/skyrim.esm:001000/0/DESC/0", "BOOK:DESC", FalmerEnglish),
        });
        Assert.Equal(0, added.ParagraphTextCount);
    }

    /// <summary>
    /// A source that ends (or starts) with a blank line split into one block more than the patch's, so nothing paired
    /// and the model's text stayed. Blocks pair on the trimmed texts, and the answer keeps its own ends.
    /// </summary>
    [Fact]
    public void SourceEndingWithABlankLine_StillTakesThePatchsBlocks()
    {
        const string first = "The first paragraph of the base game book, long enough to be counted.";
        const string second = "The second paragraph of the base game book, also long enough to count.";
        const string third = "The third paragraph of the base game book, which the mod left alone too.";
        const string fourth = "The fourth paragraph of the base game book, which the mod rewrote a little.";
        const string gap = "\r\n\r\n";
        var english = string.Join(gap, first, second, third, fourth);
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[] { Field("BOOK/000E0D68/0/DESC/0", english, PluginStringTableKind.DlStrings, 1) },
                new Dictionary<(PluginStringTableKind, uint), string>
                {
                    [(PluginStringTableKind.DlStrings, 1)] = string.Join(gap, "첫 문단.", "둘째 문단.", "셋째 문단.", "넷째 문단."),
                }),
        });
        var copy = gap + string.Join(gap, first, second, third, fourth.Replace("a little.", "a little more.")) + gap;
        var model = string.Join(gap, "모델의 첫 문단.", "모델의 둘째 문단.", "모델의 셋째 문단.", "모델이 옮긴 넷째 문단.") + "\r\n";
        var match = game.FindSimilar("BOOK:DESC", copy);
        Assert.NotNull(match);

        var spliced = GameStringDictionary.SpliceUnchanged(copy, model, match!);

        Assert.Equal(string.Join(gap, "첫 문단.", "둘째 문단.", "셋째 문단.", "모델이 옮긴 넷째 문단.") + "\r\n", spliced);
        Assert.Equal(string.Join(gap, "모델의 첫 문단.", "둘째 문단.", "셋째 문단.", "모델이 옮긴 넷째 문단.") + "\r\n",
            GameStringDictionary.SpliceUnchanged(copy, model, match!, (_, patch) => patch != "첫 문단."));
    }
}
