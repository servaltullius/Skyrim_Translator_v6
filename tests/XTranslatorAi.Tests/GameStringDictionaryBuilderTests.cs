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

public sealed class GameStringDictionaryBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-game-strings-" + Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(_root, "Data");

    // Mod Organizer keeps the patch as mods\<name>\Strings; the user may pick either folder.
    private string PatchMod => Path.Combine(_root, "Korean Patch");

    public GameStringDictionaryBuilderTests()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllBytes(Path.Combine(Data, "Skyrim.esm"),
            Header(localized: true).Concat(Group(Record("WEAP", 0x800, Sub("FULL", UInt(1))))).ToArray());
        WriteTable(Path.Combine(Data, "Strings", "Skyrim_english.STRINGS"), new() { [1] = "Iron Sword" });
    }

    [Fact]
    public async Task PairsTheGamesEnglishWithThePatchByStringId()
    {
        WriteTable(Path.Combine(PatchMod, "Strings", "Skyrim_english.STRINGS"), new() { [1] = "강철 검" });

        var (dictionary, report) = await GameStringDictionaryBuilder.BuildAsync(Data, PatchMod, CancellationToken.None);

        Assert.Equal(new[] { "Skyrim.esm" }, report.Plugins);
        Assert.Contains("Dragonborn.esm", report.Skipped);
        var identity = GameStringDictionary.FieldIdentity("WEAP/00000800/0/FULL/0", new[] { "Skyrim.esm" }, "Mod.esp");
        Assert.Equal("강철 검", dictionary.Find(identity, "WEAP:FULL", "Iron Sword")!.Text);
    }

    // Built once, the dictionary is read again by the next app start (a new service) from the global folder, and it
    // replaces the dictionary the app ships: the player's own patch decides the wording.
    [Fact]
    public async Task BuiltDictionary_IsReadAgainAfterARestart_InsteadOfTheBundledOne()
    {
        WriteTable(Path.Combine(PatchMod, "Strings", "Skyrim_english.STRINGS"), new() { [1] = "강철 검" });
        var global = Path.Combine(_root, "global");
        var bundled = new XTranslatorAi.App.Services.GameStringService(global, XTranslatorAi.App.Services.EmbeddedAssets.OpenBundledGameStrings);
        Assert.StartsWith("기본 게임 번역 사전(한글패치)", await bundled.DescribeAsync(CancellationToken.None));
        Assert.Equal("철 검", (await bundled.TryLoadAsync(CancellationToken.None))!.Find(null, "WEAP:FULL", "Iron Sword")!.Text);

        await new XTranslatorAi.App.Services.GameStringService(global, () => null).BuildAsync(Data, PatchMod, CancellationToken.None);

        var restarted = new XTranslatorAi.App.Services.GameStringService(global, XTranslatorAi.App.Services.EmbeddedAssets.OpenBundledGameStrings);
        var dictionary = await restarted.TryLoadAsync(CancellationToken.None);
        Assert.Equal("강철 검", dictionary!.Find(null, "WEAP:FULL", "Iron Sword")!.Text);
        Assert.StartsWith("직접 만든 게임 번역 사전: 레코드 1개", await restarted.DescribeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WithoutABundledOrBuiltDictionary_ThereIsNone()
    {
        var service = new XTranslatorAi.App.Services.GameStringService(Path.Combine(_root, "global"), () => null);

        Assert.Null(await service.TryLoadAsync(CancellationToken.None));
        Assert.Equal("게임 번역 사전 없음", await service.DescribeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FolderWithoutThePatchTables_IsReported()
    {
        Directory.CreateDirectory(PatchMod);

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => GameStringDictionaryBuilder.BuildAsync(Data, PatchMod, CancellationToken.None));
        Assert.Contains("Skyrim_english.STRINGS", error.Message);
    }

    private static void WriteTable(string path, Dictionary<uint, string> values)
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
            data.Write(Encoding.UTF8.GetBytes(value + "\0"));
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
