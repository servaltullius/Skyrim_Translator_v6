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

    private static byte[] Acbs(bool unique)
    {
        var data = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(data, unique ? 0x20u : 0u);
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
