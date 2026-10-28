using System.Buffers.Binary;
using System.Text;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Plugins;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

/// <summary>
/// Serana Dialogue Add-On names its speaker once for a whole quest, by GetIsID or by Serana's voice type, and by aliases
/// filled with Dawnguard's Serana; only 967 of its 8,602 lines had a speaker. A line now takes its quest's speaker, an
/// alias's NPC, and what only the masters know comes from the game dictionary's speaker index.
/// </summary>
public sealed class PluginQuestSpeakerTests : IDisposable
{
    private const ushort GetIsId = 72, GetIsVoiceType = 426, GetIsAliasRef = 566;
    private const uint Serana = 0x00002B6C, SeranaVoice = 0x00002B6F, SeranaRef = 0x00002B74, MasterQuest = 0x00004BBF;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-quest-speakers-" + Guid.NewGuid().ToString("N"));

    public PluginQuestSpeakerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The quest's dialogue conditions (before NEXT) name the speaker of its lines; an event condition (after NEXT) does
    /// not, and a line's own GetIsID still wins. An alias of the topic's quest names its NPC, through another quest's
    /// alias too (ALEQ/ALEA).
    /// </summary>
    [Fact]
    public async Task QuestConditionsAndAliases_NameTheSpeaker()
    {
        const uint ondolemar = 0x01000800, agent = 0x01000801;
        var plugin = Header()
            .Concat(Group(
                Record("QUST", 0x01000700, Sub("EDID", Z("OndolemarTalk")), Sub("CTDA", Condition(GetIsId, ondolemar)), Sub("NEXT", Array.Empty<byte>())),
                Record("QUST", 0x01000701, Sub("EDID", Z("EventOnly")), Sub("NEXT", Array.Empty<byte>()), Sub("CTDA", Condition(GetIsId, ondolemar))),
                Record("QUST", 0x01000702, Sub("EDID", Z("Aliases")), Sub("NEXT", Array.Empty<byte>()),
                    Sub("ALST", UInt(1)), Sub("ALID", Z("Agent")), Sub("ALUA", UInt(agent)), Sub("ALED", Array.Empty<byte>()),
                    Sub("ALST", UInt(2)), Sub("ALID", Z("Borrowed")), Sub("ALEQ", UInt(0x01000703)), Sub("ALEA", UInt(5)), Sub("ALED", Array.Empty<byte>())),
                Record("QUST", 0x01000703, Sub("EDID", Z("Lender")), Sub("NEXT", Array.Empty<byte>()),
                    Sub("ALST", UInt(5)), Sub("ALID", Z("Ondolemar")), Sub("ALUA", UInt(ondolemar)), Sub("ALED", Array.Empty<byte>()))))
            .Concat(DialogueGroup(
                Topic(0x01001000, quest: 0x01000700,
                    Record("INFO", 0x01001001, Sub("NAM1", Z("Mind your tongue."))),
                    Record("INFO", 0x01001002, Sub("CTDA", Condition(GetIsId, agent)), Sub("NAM1", Z("Yes, sir.")))),
                Topic(0x01002000, quest: 0x01000701, Record("INFO", 0x01002001, Sub("NAM1", Z("Anyone may say this.")))),
                Topic(0x01003000, quest: 0x01000702,
                    Record("INFO", 0x01003001, Sub("CTDA", Condition(GetIsAliasRef, 1)), Sub("NAM1", Z("Reporting in."))),
                    Record("INFO", 0x01003002, Sub("CTDA", Condition(GetIsAliasRef, 2)), Sub("NAM1", Z("Borrowed voice."))))))
            .ToArray();

        var document = await Read("Quest.esp", plugin);

        var speakers = document.Fields.Where(f => f.RecordType == "INFO").ToDictionary(f => f.SourceText, f => f.SpeakerFormId);
        Assert.Equal(ondolemar, speakers["Mind your tongue."]);
        Assert.Equal(agent, speakers["Yes, sir."]);
        Assert.Null(speakers["Anyone may say this."]);
        Assert.Equal(agent, speakers["Reporting in."]);
        Assert.Equal(ondolemar, speakers["Borrowed voice."]);
        Assert.All(document.Fields, f => Assert.Null(f.SpeakerHint));
    }

    /// <summary>
    /// A master's voice type, placed actor or quest alias is left as a hint; the masters' index names the NPC: the one
    /// that uses the voice type, the placed actor's base, the NPC the alias holds. A scene listener the masters name
    /// replaces "another character".
    /// </summary>
    [Fact]
    public async Task MasterHints_AreResolvedByTheMastersIndex()
    {
        var index = await MasterIndexAsync();
        var mod = await Read("Mod.esp", ModPlugin());

        var info = mod.Fields.Where(f => f.RecordType == "INFO").ToDictionary(f => f.SourceText);
        Assert.Equal(("V:00002B6F", (uint?)null), (info["By my voice."].SpeakerHint, info["By my voice."].SpeakerFormId));
        Assert.Equal("A:00004BBF:0", info["By Dawnguard's alias."].SpeakerHint);
        Assert.Equal("R:00002B74", info["By my placed self."].SpeakerHint);
        Assert.Equal(((uint?)0x01000800, (uint?)0u, "R:00002B74"),
            (info["Then go, Serana."].SpeakerFormId, info["Then go, Serana."].ListenerFormId, info["Then go, Serana."].ListenerHint));

        var resolved = index.Resolve(mod.Fields, mod.Info.Masters, "Mod.esp").Where(f => f.RecordType == "INFO").ToDictionary(f => f.SourceText);
        Assert.Equal(Serana, resolved["By my voice."].SpeakerFormId);
        Assert.Equal(Serana, resolved["By Dawnguard's alias."].SpeakerFormId);
        Assert.Equal(Serana, resolved["By my placed self."].SpeakerFormId);
        Assert.Equal(Serana, resolved["Then go, Serana."].ListenerFormId);
        // A voice type that two differently named NPCs use is no one's.
        Assert.Null(resolved["A guard's voice."].SpeakerFormId);
    }

    // A dictionary the player built that cannot be read (damaged, an older format) left the run with no game text at all,
    // and every call read it again.
    [Fact]
    public async Task BuiltDictionaryThatCannotBeRead_GivesWayToTheBundledOne()
    {
        var bytes = new MemoryStream();
        GameStringDictionary.Build(Array.Empty<GameStringPlugin>()).Save(bytes);
        var gameStrings = new GameStringService(Path.Combine(_root, "game-broken"), () => new MemoryStream(bytes.ToArray()));
        Directory.CreateDirectory(Path.GetDirectoryName(gameStrings.DictionaryPath)!);
        await File.WriteAllBytesAsync(gameStrings.DictionaryPath, new byte[] { 1, 2, 3, 4 });

        Assert.NotNull(await gameStrings.TryLoadAsync(CancellationToken.None));
        Assert.Contains("읽지 못해", await gameStrings.DescribeAsync(CancellationToken.None));

        // Made again, it is read again.
        await File.WriteAllBytesAsync(gameStrings.DictionaryPath, bytes.ToArray());
        File.SetLastWriteTimeUtc(gameStrings.DictionaryPath, DateTime.UtcNow.AddMinutes(1));
        Assert.StartsWith("직접 만든 게임 번역 사전:", await gameStrings.DescribeAsync(CancellationToken.None));
    }

    /// <summary>The index travels in the game dictionary: saved and loaded with it, and used when a plugin is opened.</summary>
    [Fact]
    public async Task OpeningAPlugin_UsesTheDictionarysSpeakerIndex()
    {
        var index = await MasterIndexAsync();
        var bytes = new MemoryStream();
        GameStringDictionary.Build(Array.Empty<GameStringPlugin>()).WithSpeakers(index).Save(bytes);
        var loaded = GameStringDictionary.Load(new MemoryStream(bytes.ToArray()));
        Assert.Equal(index.Count, loaded.Speakers!.Count);

        var input = Path.Combine(_root, "Mod.esp");
        await File.WriteAllBytesAsync(input, ModPlugin());
        var gameStrings = new GameStringService(_root, () => new MemoryStream(bytes.ToArray()));
        var workspace = new ProjectWorkspaceService(new GlobalProjectDbService(new BuiltInGlossaryService(), _root),
            Path.Combine(_root, "projects"), gameStrings);
        var result = await workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(
            input, new PluginReadOptions(), "korean", "utf-8", "gemini-2.5-flash", "", false), CancellationToken.None);
        await using var db = result.Db;

        var speakers = await db.GetStringSpeakersAsync(CancellationToken.None);
        var bySource = result.Entries.ToDictionary(e => e.SourceText, e => speakers.GetValueOrDefault(e.Id));
        Assert.Equal("00002B6C", bySource["By my voice."]);
        Assert.Equal("01000800>00002B6C", bySource["Then go, Serana."]);
        Assert.Null(bySource["A guard's voice."]);
    }

    /// <summary>
    /// The index also lists each master NPC's own lines, for the agent mode's <c>voice</c>: Teldryn's 하오체 was found
    /// only by reading the lines the dictionary had filled. A line by GetIsID and one by the NPC's own voice type are hers;
    /// a voice two NPCs share is no one's. The lines travel in the dictionary.
    /// </summary>
    [Fact]
    public async Task MasterNpcLines_AreFoundByNameOrRecord_AndSavedWithTheDictionary()
    {
        var master = Header(masterBytes: Z("Skyrim.esm"))
            .Concat(Group(
                Record("NPC_", 0x01002B6C, Sub("ACBS", Acbs(unique: true, female: true)), Sub("FULL", Z("Serana")), Sub("VTCK", UInt(0x01002B6F))),
                Record("NPC_", 0x01003010, Sub("ACBS", Acbs(unique: false, female: true, useTraits: true)), Sub("TPLT", UInt(0x01003011)), Sub("FULL", Z("Bandit"))),
                Record("NPC_", 0x01003000, Sub("ACBS", Acbs(unique: false)), Sub("FULL", Z("Guard")), Sub("VTCK", UInt(0x01003001))),
                Record("NPC_", 0x01003002, Sub("ACBS", Acbs(unique: false)), Sub("FULL", Z("Soldier")), Sub("VTCK", UInt(0x01003001)))))
            .Concat(Group(Record("QUST", 0x01004BBF, Sub("EDID", Z("DLC1Quest")), Sub("NEXT", Array.Empty<byte>()))))
            .Concat(Group(Record("VTYP", 0x01003001, Sub("EDID", Z("MaleGuard")))))
            .Concat(DialogueGroup(Topic(0x01005000, 0x01004BBF,
                Record("INFO", 0x01005001, Sub("CTDA", Condition(GetIsId, 0x01002B6C)), Sub("NAM1", Z("By my name."))),
                Record("INFO", 0x01005002, Sub("CTDA", Condition(GetIsVoiceType, 0x01002B6F)), Sub("NAM1", Z("By my voice."))),
                Record("INFO", 0x01005003, Sub("CTDA", Condition(GetIsVoiceType, 0x01003001)), Sub("NAM1", Z("A guard's voice."))))))
            .ToArray();
        var builder = new MasterSpeakerIndex.Builder();
        builder.Add(await Read("Dawnguard.esm", master));
        var index = builder.Build();

        var serana = Assert.Single(index.FindNpcs("serana"));
        Assert.Equal(("dawnguard.esm:002b6c", "Serana", 2), serana);
        Assert.Equal(serana, Assert.Single(index.FindNpcs("02002B6C")));
        Assert.Equal(new[] { "INFO/dawnguard.esm:005001/0/NAM1/0", "INFO/dawnguard.esm:005002/0/NAM1/0" },
            index.LinesOf(serana.Npc).OrderBy(l => l, StringComparer.Ordinal));
        Assert.Empty(index.FindNpcs("Guard"));

        var bytes = new MemoryStream();
        GameStringDictionary.Build(Array.Empty<GameStringPlugin>()).WithSpeakers(index).Save(bytes);
        var loaded = GameStringDictionary.Load(new MemoryStream(bytes.ToArray())).Speakers!;
        Assert.Equal(index.LinesOf(serana.Npc), loaded.LinesOf(serana.Npc));
        Assert.Equal(serana, Assert.Single(loaded.FindNpcs("Serana")));

        // A voice two NPCs share names no speaker, but its type is named for the request ("NPC with voice MaleGuard").
        var modMasters = new[] { "Skyrim.esm", "Dawnguard.esm" };
        Assert.Equal("MaleGuard", loaded.VoiceName("V:01003001", modMasters, "Mod.esp"));
        Assert.Null(loaded.VoiceName("V:01002B6F", modMasters, "Mod.esp"));
        Assert.Null(loaded.VoiceName("R:01002B74", modMasters, "Mod.esp"));

        // An NPC's sex, for the speaker label (형 or 오빠); unknown for one whose traits a template gives.
        Assert.True(loaded.IsFemale(0x01002B6C, modMasters, "Mod.esp"));
        Assert.False(loaded.IsFemale(0x01003000, modMasters, "Mod.esp"));
        Assert.Null(loaded.IsFemale(0x01003010, modMasters, "Mod.esp"));
    }

    /// <summary>A plugin's own NPC carries its sex on its name field, which the project keeps for the labels.</summary>
    [Fact]
    public async Task PluginNpcName_CarriesTheNpcsSex()
    {
        var plugin = Header()
            .Concat(Group(
                Record("NPC_", 0x01000801, Sub("ACBS", Acbs(unique: true, female: true)), Sub("FULL", Z("Kariss"))),
                Record("NPC_", 0x01000802, Sub("ACBS", Acbs(unique: true)), Sub("FULL", Z("Faryl")))))
            .ToArray();

        var fields = (await Read("Mod.esp", plugin)).Fields;

        Assert.True(Assert.Single(fields, f => f.SourceText == "Kariss").Female);
        Assert.False(Assert.Single(fields, f => f.SourceText == "Faryl").Female);
    }

    // Dawnguard's part: Serana, her voice type, her placed actor, a quest alias she fills, and a voice two guards share.
    private async Task<MasterSpeakerIndex> MasterIndexAsync()
    {
        var master = Header(masterBytes: Z("Skyrim.esm"))
            .Concat(Group(
                Record("NPC_", 0x01002B6C, Sub("ACBS", Acbs(unique: true)), Sub("FULL", Z("Serana")), Sub("VTCK", UInt(0x01002B6F))),
                Record("NPC_", 0x01002B6D, Sub("ACBS", Acbs(unique: true)), Sub("FULL", Z("Serana")), Sub("VTCK", UInt(0x01002B6F))),
                Record("NPC_", 0x01003000, Sub("ACBS", Acbs(unique: false)), Sub("FULL", Z("Guard")), Sub("VTCK", UInt(0x01003001))),
                Record("NPC_", 0x01003002, Sub("ACBS", Acbs(unique: false)), Sub("FULL", Z("Soldier")), Sub("VTCK", UInt(0x01003001)))))
            .Concat(Group(Record("ACHR", 0x01002B74, Sub("NAME", UInt(0x01002B6C)))))
            .Concat(Group(Record("QUST", 0x01004BBF, Sub("EDID", Z("DLC1Quest")), Sub("NEXT", Array.Empty<byte>()),
                Sub("ALST", UInt(0)), Sub("ALID", Z("Serana")), Sub("ALFR", UInt(0x01002B74)), Sub("ALED", Array.Empty<byte>()))))
            .ToArray();
        var builder = new MasterSpeakerIndex.Builder();
        builder.Add(await Read("Dawnguard.esm", master));
        return builder.Build();
    }

    // The mod: Dawnguard.esm is its master (index 0), its own records are 01.
    private static byte[] ModPlugin()
        => Header(masterBytes: Z("Dawnguard.esm"))
            .Concat(Group(Record("NPC_", 0x01000800, Sub("FULL", Z("Harkon")))))
            .Concat(Group(
                Record("QUST", 0x01000700, Sub("EDID", Z("SeranaTalk")), Sub("CTDA", Condition(GetIsVoiceType, SeranaVoice)), Sub("NEXT", Array.Empty<byte>()),
                    Sub("ALST", UInt(1)), Sub("ALID", Z("Serana")), Sub("ALFR", UInt(SeranaRef)), Sub("ALED", Array.Empty<byte>()),
                    Sub("ALST", UInt(2)), Sub("ALID", Z("Harkon")), Sub("ALUA", UInt(0x01000800)), Sub("ALED", Array.Empty<byte>()),
                    Sub("ALST", UInt(3)), Sub("ALID", Z("SeranaElsewhere")), Sub("ALEQ", UInt(MasterQuest)), Sub("ALEA", UInt(0)), Sub("ALED", Array.Empty<byte>()))))
            .Concat(Group(Record("SCEN", 0x01000600, [
                Sub("EDID", Z("FatherScene")), Sub("ALID", UInt(2)), Sub("ALID", UInt(1)),
                Sub("ANAM", new byte[] { 0, 0 }), Sub("ALID", UInt(2)), Sub("DATA", UInt(0x01004000)), Sub("HTID", UInt(1)), Sub("ANAM", Array.Empty<byte>()),
                Sub("PNAM", UInt(0x01000700))])))
            .Concat(DialogueGroup(
                Topic(0x01001000, quest: 0x01000700, Record("INFO", 0x01001001, Sub("NAM1", Z("By my voice.")))),
                Topic(0x01002000, quest: 0x01000700,
                    Record("INFO", 0x01002001, Sub("CTDA", Condition(GetIsAliasRef, 3)), Sub("NAM1", Z("By Dawnguard's alias."))),
                    Record("INFO", 0x01002002, Sub("CTDA", Condition(GetIsAliasRef, 1)), Sub("NAM1", Z("By my placed self."))),
                    Record("INFO", 0x01002003, Sub("CTDA", Condition(GetIsVoiceType, 0x00003001)), Sub("NAM1", Z("A guard's voice.")))),
                Topic(0x01004000, quest: 0x01000700, Record("INFO", 0x01004001, Sub("NAM1", Z("Then go, Serana."))))))
            .ToArray();

    private async Task<PluginDocument> Read(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, bytes);
        return await PluginReader.ReadAsync(path, new PluginReadOptions(), CancellationToken.None);
    }

    private static byte[] Acbs(bool unique, bool female = false, bool useTraits = false)
    {
        var data = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (unique ? 0x20u : 0u) | (female ? 0x1u : 0u));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), useTraits ? (ushort)1 : (ushort)0);
        return data;
    }

    private static byte[] Condition(ushort function, uint parameter)
    {
        var data = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1f);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), function);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), parameter);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28), -1);
        return data;
    }

    private static byte[] DialogueGroup(params byte[][] children)
    {
        var bytes = Group(children);
        Encoding.ASCII.GetBytes("DIAL").CopyTo(bytes, 8);
        return bytes;
    }

    // A topic (DIAL with its quest) and its INFOs (Topic Children group).
    private static byte[] Topic(uint topic, uint quest, params byte[][] infos)
    {
        var children = Group(infos);
        BinaryPrimitives.WriteUInt32LittleEndian(children.AsSpan(8), topic);
        BinaryPrimitives.WriteUInt32LittleEndian(children.AsSpan(12), 7);
        return Record("DIAL", topic, Sub("EDID", Z($"Topic{topic:X8}")), Sub("QNAM", UInt(quest))).Concat(children).ToArray();
    }
}
