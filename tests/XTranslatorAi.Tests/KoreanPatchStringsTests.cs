using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Plugins;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

/// <summary>The Korean patch is improved by reviews written back into its own string tables.</summary>
public sealed class KoreanPatchStringsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-korean-patch-" + Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(_root, "Data");

    private string Patch => Path.Combine(_root, "Patch");

    public KoreanPatchStringsTests()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllBytes(Path.Combine(Data, "Skyrim.esm"), Header(localized: true).Concat(Group(
            Record("WEAP", 0x800, Sub("EDID", Z("IronSword")), Sub("FULL", UInt(1))))).Concat(Group(
            Record("SPEL", 0x801, Sub("FULL", UInt(3)), Sub("DESC", UInt(2))))).ToArray());
        WriteTable(Path.Combine(Data, "Strings", "Skyrim_english.STRINGS"), false, new() { [1] = "Iron Sword", [3] = "Fireball" });
        WriteTable(Path.Combine(Data, "Strings", "Skyrim_english.DLSTRINGS"), true, new() { [2] = "Deals <mag> damage." });
        WriteTable(Path.Combine(Patch, "Strings", "Skyrim_english.STRINGS"), false, new() { [1] = "철 검", [3] = "화염구" });
        WriteTable(Path.Combine(Patch, "Strings", "Skyrim_english.DLSTRINGS"), true, new() { [2] = "<mag>의 피해를 줍니다." });
    }

    [Fact]
    public async Task Strings_ComeWithTheirEnglishAndRecord()
    {
        var strings = await KoreanPatchStrings.ReadAsync(Data, Patch, CancellationToken.None);

        var sword = strings.Single(s => s.Id == 1);
        Assert.Equal(("Skyrim.esm", PluginStringTableKind.Strings, "WEAP:FULL", "IronSword", "Iron Sword", "철 검"),
            (sword.Plugin, sword.Table, sword.Rec, sword.Edid, sword.English, sword.Korean));
        Assert.Equal("<mag>의 피해를 줍니다.", strings.Single(s => s.Id == 2).Korean);
    }

    [Fact]
    public async Task ReviewedChanges_AreWrittenIntoThePatch_WithABackup()
    {
        var backup = Path.Combine(_root, "backup");
        var result = await KoreanPatchStrings.ApplyAsync(Data, Patch, new[]
        {
            new KoreanPatchChange("Skyrim.esm", PluginStringTableKind.Strings, 1, "철 검", "강철 검"),
            new KoreanPatchChange("Skyrim.esm", PluginStringTableKind.DlStrings, 2, "<mag>의 피해를 줍니다.", "<mag>만큼 피해를 줍니다."),
        }, write: true, backup, CancellationToken.None);

        Assert.Equal(2, result.Applied.Count);
        var strings = await KoreanPatchStrings.ReadAsync(Data, Patch, CancellationToken.None);
        Assert.Equal("강철 검", strings.Single(s => s.Id == 1).Korean);
        Assert.Equal("화염구", strings.Single(s => s.Id == 3).Korean);
        Assert.Equal("<mag>만큼 피해를 줍니다.", strings.Single(s => s.Id == 2).Korean);
        Assert.True(File.Exists(Path.Combine(backup, "Skyrim_english.STRINGS")));
    }

    // A review of an older patch, or a suggestion that drops the <mag>, must not reach the patch.
    [Fact]
    public async Task StaleOrTagBreakingChanges_AreSkipped()
    {
        var result = await KoreanPatchStrings.ApplyAsync(Data, Patch, new[]
        {
            new KoreanPatchChange("Skyrim.esm", PluginStringTableKind.Strings, 1, "쇠 검", "강철 검"),
            new KoreanPatchChange("Skyrim.esm", PluginStringTableKind.DlStrings, 2, "<mag>의 피해를 줍니다.", "피해를 줍니다."),
        }, write: true, backupFolder: null, CancellationToken.None);

        Assert.Empty(result.Applied);
        Assert.Contains(result.Skipped, s => s.Reason == "현재 번역이 검수 때와 다름");
        Assert.Contains(result.Skipped, s => s.Reason.StartsWith("태그 불일치"));
        Assert.Equal("철 검", (await KoreanPatchStrings.ReadAsync(Data, Patch, CancellationToken.None)).Single(s => s.Id == 1).Korean);
    }

    // Books whose patch text already lays out its tags unlike the English: a spelling fix that keeps the patch's own
    // tags (덮힌 → 덮인) is not a tag problem.
    [Fact]
    public async Task AFixKeepingThePatchsOwnTags_IsApplied_WhenThePatchAlreadyDiffersFromTheEnglish()
    {
        WriteTable(Path.Combine(Patch, "Strings", "Skyrim_english.DLSTRINGS"), true, new() { [2] = "눈덮힌 곳에 피해를 줍니다." });

        var result = await KoreanPatchStrings.ApplyAsync(Data, Patch, new[]
        {
            new KoreanPatchChange("Skyrim.esm", PluginStringTableKind.DlStrings, 2, "눈덮힌 곳에 피해를 줍니다.", "눈덮인 곳에 피해를 줍니다."),
        }, write: true, backupFolder: null, CancellationToken.None);

        Assert.Single(result.Applied);
        Assert.Equal("눈덮인 곳에 피해를 줍니다.", (await KoreanPatchStrings.ReadAsync(Data, Patch, CancellationToken.None)).Single(s => s.Id == 2).Korean);
    }

    private static void WriteTable(string path, bool lengthPrefixed, Dictionary<uint, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var data = new MemoryStream();
        var directory = new byte[8 + values.Count * 8];
        BinaryPrimitives.WriteUInt32LittleEndian(directory, (uint)values.Count);
        var index = 0;
        foreach (var (id, value) in values)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(8 + index * 8), id);
            BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(12 + index * 8), (uint)data.Length);
            var text = Encoding.UTF8.GetBytes(value + "\0");
            if (lengthPrefixed)
            {
                data.Write(BitConverter.GetBytes((uint)text.Length));
            }

            data.Write(text);
            index++;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(4), (uint)data.Length);
        File.WriteAllBytes(path, directory.Concat(data.ToArray()).ToArray());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
