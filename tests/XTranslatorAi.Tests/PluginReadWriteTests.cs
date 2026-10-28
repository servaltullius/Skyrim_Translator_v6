using System.Buffers.Binary;
using System.Text;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Tests;

public sealed class PluginReadWriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoEdits_PreservesEveryInputByte(bool compressed)
    {
        using var fixture = new Fixture(Build(compressed));
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        Assert.Equal("Steel Sword", Assert.Single(document.Fields).SourceText);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default);
        Assert.Equal(File.ReadAllBytes(fixture.Input), File.ReadAllBytes(result.PluginPath));
        Assert.Equal(0, result.ChangedFields);
    }

    [Theory]
    [InlineData(1.71f, true)]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(1.0f, false)]
    public async Task HeaderVersion_AcceptsUpdatedSkyrimAndRejectsWrongOrNonfiniteVersion(float version, bool accepted)
    {
        var bytes = Build(false);
        BitConverter.GetBytes(version).CopyTo(bytes, 30);
        using var fixture = new Fixture(bytes);
        if (accepted) Assert.Single((await PluginReader.ReadAsync(fixture.Input, new(), default)).Fields);
        else await Assert.ThrowsAsync<NotSupportedException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslatesDisplayField_PreservesRecordHeaderAndAllOtherData(bool compressed)
    {
        using var fixture = new Fixture(Build(compressed));
        var originalBytes = File.ReadAllBytes(fixture.Input);
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var field = Assert.Single(document.Fields);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [field.Key] = "강철 검" }, new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal("강철 검", Assert.Single(output.Fields).SourceText);
        Assert.Equal(field.Key, output.Fields[0].Key);
        var before = document.Records[1]; var after = output.Records[1];
        Assert.Equal(before.Raw.Span.Slice(8, 16).ToArray(), after.Raw.Span.Slice(8, 16).ToArray());
        Assert.Equal(before.Subrecords.Single(s => s.Type == "DATA").Data.ToArray(), after.Subrecords.Single(s => s.Type == "DATA").Data.ToArray());
        Assert.Equal(before.Subrecords.Single(s => s.Type == "ZZZZ").Data.ToArray(), after.Subrecords.Single(s => s.Type == "ZZZZ").Data.ToArray());
        Assert.Equal(originalBytes, File.ReadAllBytes(fixture.Input));
    }

    // A large plugin's translations are checked all at once: the export would stop at the first one it refuses.
    [Fact]
    public async Task InvalidTranslations_AreAllListedBeforeExport()
    {
        using var fixture = new Fixture(Build(false));
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var field = Assert.Single(document.Fields);

        Assert.Empty(PluginWriter.FindInvalidTranslations(document, new Dictionary<string, string> { [field.Key] = "강철 검" }));
        var problems = PluginWriter.FindInvalidTranslations(document, new Dictionary<string, string>
        {
            [field.Key] = "<b>강철 검</b>",
            ["WEAP/00000999/0/FULL/0"] = "없는 필드",
        });
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("WEAP/00000999/0/FULL/0", StringComparison.Ordinal));
    }

    // An existing Korean translation leaves out the English initial's picture and the English pronoun the game fills
    // in; a caller carrying such translations asks for that, and nothing else, to pass.
    [Fact]
    public void DroppedInitialAndPronoun_PassOnlyWhenAllowed()
    {
        const string english = "<img src='img://Textures/Interface/Books/Illuminated_Letters/T_letter.png'>he <Alias.PronounCap=Smith> forged <mag> blades.";
        const string korean = "그 대장장이는 칼날 <mag>개를 벼렸다.";

        Assert.Single(PluginWriter.FindTranslationProblems(english, korean, "plugin:x", allowDroppedInitialsAndPronouns: false));
        Assert.Empty(PluginWriter.FindTranslationProblems(english, korean, "plugin:x", allowDroppedInitialsAndPronouns: true));
        // Losing a placeholder the game fills with a value still fails.
        Assert.Single(PluginWriter.FindTranslationProblems(english, "그 대장장이는 칼날을 벼렸다.", "plugin:x", allowDroppedInitialsAndPronouns: true));
    }

    // Writing into an earlier translated release: its text is a translation, and the new one is checked against the
    // English it translates, not against the old translation's tags.
    [Fact]
    public async Task ChecksAgainstGivenEnglish_WhenTheDocumentIsATranslation()
    {
        using var fixture = new Fixture(Build(false));
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var field = Assert.Single(document.Fields);
        var translations = new Dictionary<string, string> { [field.Key] = "강철 검 <mag>" };
        var english = new Dictionary<string, string> { [field.Key] = "Steel sword <mag>" };

        Assert.NotEmpty(PluginWriter.FindInvalidTranslations(document, translations));
        Assert.Empty(PluginWriter.FindInvalidTranslations(document, translations, checkAgainst: english));
        var result = await PluginWriter.ExportAsync(document, translations, new(fixture.Output, CheckAgainst: english), default);
        Assert.Equal("강철 검 <mag>", Assert.Single((await PluginReader.ReadAsync(result.PluginPath, new(), default)).Fields).SourceText);
    }

    [Theory]
    [InlineData(65534, 65535, false)]
    [InlineData(65535, 65534, true)]
    public async Task ExtendedSubrecordLength_CrossesUShortBoundaryWithoutTruncation(int sourceLength, int targetLength, bool inputExtended)
    {
        var record = Record("BOOK", 0x800, Sub("EDID", Z("Book01")), Sub("DESC", Z(new string('A', sourceLength))));
        using var fixture = new Fixture(Header().Concat(Group(record)).ToArray());
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        Assert.Equal(inputExtended, document.Records[1].Subrecords[1].HeaderLength == 16);
        var target = new string('B', targetLength);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [document.Fields[0].Key] = target }, new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal(target, output.Fields[0].SourceText);
        Assert.Equal(16, output.Records[1].Subrecords[1].HeaderLength);
    }

    /// <summary>
    /// MoreNastyCritters.esp has 72 DESC subrecords with no bytes at all (not even the terminator). One of them made
    /// the whole plugin unreadable ("문자열 종료/길이가 잘못되었습니다"); they are empty fields and stay as they were.
    /// </summary>
    [Fact]
    public async Task ZeroLengthStringSubrecord_IsReadAsEmptyAndKeptOnExport()
    {
        using var fixture = new Fixture(Header().Concat(Group(Record("ARMO", 0x800,
            Sub("EDID", Z("Armor01")), Sub("FULL", Z("Hide Armor")), Sub("DESC", Array.Empty<byte>())))).ToArray());
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var field = Assert.Single(document.Fields);
        Assert.Equal("FULL", field.SubrecordType);

        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [field.Key] = "가죽 갑옷" }, new(fixture.Output), default);

        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal("가죽 갑옷", Assert.Single(output.Fields).SourceText);
        Assert.Equal(0, output.Records[1].Subrecords.Single(s => s.Type == "DESC").Data.Length);
    }

    [Fact]
    public async Task LocalizedExport_PreservesPluginAndAllTableIdsIncludingUnusedStrings()
    {
        using var fixture = new Fixture(Header(localized: true).Concat(Group(Record("BOOK", 0x800,
            Sub("EDID", Z("Book01")), Sub("FULL", UInt(1)), Sub("DESC", UInt(1))))).ToArray());
        fixture.Table(PluginStringTableKind.Strings, new() { [1] = "A title", [9] = "Unused name" });
        fixture.Table(PluginStringTableKind.DlStrings, new() { [1] = "A body", [8] = "Unused body" });
        var original = File.ReadAllBytes(fixture.Input);
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var edits = document.Fields.ToDictionary(field => field.Key, field => field.SubrecordType == "FULL" ? "책 제목" : "책 본문");
        var result = await PluginWriter.ExportAsync(document, edits, new(fixture.Output), default);
        Assert.Equal(original, File.ReadAllBytes(result.PluginPath));
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal("책 제목", output.Fields[0].SourceText);
        Assert.Equal("책 본문", output.Fields[1].SourceText);
        Assert.Equal("Unused name", output.Tables[PluginStringTableKind.Strings].Strings[9]);
        Assert.Equal("Unused body", output.Tables[PluginStringTableKind.DlStrings].Strings[8]);
        Assert.All(output.Fields, field => Assert.Equal(1u, field.StringId));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "Strings", "Example_english.STRINGS")));
    }

    // Converting a localized plugin's tables converts their unused strings too. One the output encoding cannot hold
    // failed with only "some character could not be written", which no row in the project could fix.
    [Fact]
    public async Task LocalizedExport_NamesAnUnusedStringTheOutputEncodingCannotHold()
    {
        using var fixture = new Fixture(Header(localized: true).Concat(Group(Record("WEAP", 0x800, Sub("FULL", UInt(1))))).ToArray());
        fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Sword", [9] = "한글 이름" });
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginWriter.ExportAsync(document,
            new Dictionary<string, string> { [document.Fields[0].Key] = "Sword" }, new(fixture.Output, "windows-1252"), default));

        Assert.Equal("쓰이지 않는 문자열에 출력 인코딩으로 표현할 수 없는 문자가 있습니다: STRINGS/9 U+D55C", error.Message);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task LocalizedPerk_TranslatesDisplayNameAndPreservesGraphVariableAndNumericParameters()
    {
        var record = Record("PERK", 0x800, Sub("EDID", Z("BlockRunner")), Sub("FULL", UInt(1)), Sub("PRKE", new byte[] { 2, 0, 0 }),
            Sub("DATA", new byte[] { 54, 11, 1 }), Sub("EPFT", new byte[] { 6 }), Sub("EPFD", Z("bPerkShieldCharge")), Sub("PRKF", Array.Empty<byte>()),
            Sub("PRKE", new byte[] { 2, 0, 0 }), Sub("EPFT", new byte[] { 1 }), Sub("EPFD", UInt(123)), Sub("PRKF", Array.Empty<byte>()));
        using var fixture = new Fixture(Header(localized: true).Concat(Group(record)).ToArray());
        fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Block Runner" });
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var field = Assert.Single(document.Fields);
        Assert.Equal("FULL", field.SubrecordType);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [field.Key] = "방패 질주" }, new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal("방패 질주", Assert.Single(output.Fields).SourceText);
        Assert.Equal(File.ReadAllBytes(fixture.Input), File.ReadAllBytes(result.PluginPath));
        Assert.Equal(Z("bPerkShieldCharge"), output.Records[1].Subrecords.First(sub => sub.Type == "EPFD").Data.ToArray());
        Assert.Equal(UInt(123), output.Records[1].Subrecords.Last(sub => sub.Type == "EPFD").Data.ToArray());
    }

    [Fact]
    public async Task NonLocalizedPerk_EditingEveryExposedFieldCannotRenameGraphVariable()
    {
        using var fixture = new Fixture(Header().Concat(Group(Record("PERK", 0x00106253, true,
            Sub("EDID", Z("BlockRunner")), Sub("FULL", Z("Block Runner")), Sub("DESC", Z("Move faster with a raised shield.")),
            Sub("PRKE", new byte[] { 2, 0, 0 }), Sub("DATA", new byte[] { 54, 11, 1 }),
            Sub("EPFT", new byte[] { 6 }), Sub("EPFD", Z("bPerkShieldCharge")), Sub("PRKF", Array.Empty<byte>())))).ToArray());
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        Assert.Equal(2, document.Fields.Count);
        Assert.DoesNotContain(document.Fields, field => field.SubrecordType == "EPFD");
        var result = await PluginWriter.ExportAsync(document, document.Fields.ToDictionary(field => field.Key, field => field.SourceText + " 검증"), new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.All(output.Fields, field => Assert.EndsWith(" 검증", field.SourceText));
        Assert.Equal(Z("bPerkShieldCharge"), output.Records[1].Subrecords.Single(sub => sub.Type == "EPFD").Data.ToArray());
    }

    [Fact]
    public async Task SharedStringId_ConflictingTranslationsAreRejectedBeforeWriting()
    {
        using var fixture = new Fixture(Header(localized: true).Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", UInt(1))), Record("ARMO", 0x801, Sub("FULL", UInt(1))))).ToArray());
        fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Shared" });
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginWriter.ExportAsync(document,
            new Dictionary<string, string> { [document.Fields[0].Key] = "변경" }, new(fixture.Output), default));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("strings")]
    public async Task ChangedInputSnapshot_IsRejected(string changed)
    {
        using var fixture = new Fixture(changed == "source" ? Build(false)
            : Header(localized: true).Concat(Group(Record("WEAP", 0x800, Sub("FULL", UInt(1))))).ToArray());
        if (changed == "strings") fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Sword" });
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        if (changed == "source") File.AppendAllText(fixture.Input, "change");
        else fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Different" });
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task ExistingOutputAndCanceledExport_PreserveAllExistingData()
    {
        using var fixture = new Fixture(Build(false));
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        Directory.CreateDirectory(fixture.Output);
        File.WriteAllText(Path.Combine(fixture.Output, "keep.txt"), "original");
        await Assert.ThrowsAsync<IOException>(() => PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default));
        Assert.Equal("original", File.ReadAllText(Path.Combine(fixture.Output, "keep.txt")));
        var cancelOutput = fixture.Output + "-cancel";
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(cancelOutput), cancellation.Token));
        Assert.False(Directory.Exists(cancelOutput));
    }

    [Fact]
    public async Task UnrepresentableTargetAndUnknownField_AreRejected()
    {
        using var fixture = new Fixture(Build(false));
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        // The row and the character are named (E457 used to say only that some character could not be written).
        var unencodable = await Assert.ThrowsAsync<InvalidDataException>(() => PluginWriter.ExportAsync(document,
            new Dictionary<string, string> { [document.Fields[0].Key] = "강철 검" }, new(fixture.Output, "windows-1252"), default));
        Assert.IsType<EncoderFallbackException>(unencodable.InnerException);
        Assert.EndsWith("U+AC15", unencodable.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginWriter.ExportAsync(document,
            new Dictionary<string, string> { ["not-a-field"] = "test" }, new(fixture.Output), default));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task SoundDescriptorFlags_SharingAStringIdValue_AreNotReportedAsAmbiguousText()
    {
        // Skyrim.esm SNDR FNAM values 1, 2 and 20 equal real STRINGS IDs but are loop/unknown flags.
        using var fixture = new Fixture(Header(localized: true)
            .Concat(Group(Record("SNDR", 0x800, Sub("EDID", Z("NPCDwarvenSphereEquip")), Sub("FNAM", UInt(1)))))
            .Concat(Group(Record("WEAP", 0x801, Sub("FULL", UInt(1))))).ToArray());
        fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Sword" });
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        Assert.DoesNotContain(document.Info.Diagnostics, diagnostic => diagnostic.Code == "ambiguous_field");
        var field = Assert.Single(document.Fields);
        Assert.Equal("WEAP", field.RecordType);
    }

    [Fact]
    public async Task UnknownRecord_IsReportedAndBlocksExport()
    {
        using var fixture = new Fixture(Header().Concat(Group(Record("ZZZZ", 0x800, Sub("FULL", Z("Unknown"))))).ToArray());
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        Assert.Contains(document.Info.Diagnostics, diagnostic => diagnostic.BlocksExport);
        await Assert.ThrowsAsync<NotSupportedException>(() => PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default));
    }

    [Fact]
    public async Task BinaryImageAdapterSignature_IsPreservedByteForByte()
    {
        using var fixture = new Fixture(Header().Concat(Group(Record("IMAD", 0x800,
            Sub("\0IAD", new byte[] { 1, 2, 3, 4 }), Sub("\u0001IAD", new byte[] { 5, 6, 7, 8 })))).ToArray());
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default);
        Assert.Equal(File.ReadAllBytes(fixture.Input), File.ReadAllBytes(result.PluginPath));
    }

    [Theory]
    [InlineData("truncated-footer")]
    [InlineData("wrong-checksum")]
    [InlineData("wrong-size")]
    public async Task DamagedCompressedRecord_IsRejected(string fault)
    {
        var record = Record("WEAP", 0x800, true, Sub("FULL", Z("Sword")));
        if (fault == "truncated-footer") record = record[..^1];
        else if (fault == "trailing-data") record = record.Concat(new byte[] { 1, 2, 3 }).ToArray();
        else if (fault == "wrong-checksum") record[^1] ^= 1;
        else record[24]++;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)(record.Length - 24));
        using var fixture = new Fixture(Header().Concat(Group(record)).ToArray());
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));
    }

    /// <summary>
    /// The Great Town of Karthwasten has 10 compressed records with 1-27 bytes after a complete zlib stream; the whole
    /// plugin could not be opened. The stream itself is intact (checksum and declared length match).
    /// </summary>
    [Fact]
    public async Task CompressedRecordWithTrailingBytes_IsReadKeptAndReported()
    {
        var record = Record("WEAP", 0x800, true, Sub("EDID", Z("Sword01")), Sub("FULL", Z("Sword")));
        record = record.Concat(new byte[] { 1, 2, 3 }).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)(record.Length - 24));
        var other = Record("WEAP", 0x801, true, Sub("EDID", Z("Axe01")), Sub("FULL", Z("Axe")));
        using var fixture = new Fixture(Header().Concat(Group(record, other)).ToArray());

        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        Assert.Equal(new[] { "Sword", "Axe" }, document.Fields.Select(field => field.SourceText));
        var diagnostic = Assert.Single(document.Info.Diagnostics, d => d.Code == "compressed_trailing_bytes");
        Assert.False(diagnostic.BlocksExport);
        var unchanged = await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(Path.Combine(fixture.Output, "same")), default);
        Assert.Equal(File.ReadAllBytes(fixture.Input), File.ReadAllBytes(unchanged.PluginPath));
        var translated = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [document.Fields[0].Key] = "검" }, new(Path.Combine(fixture.Output, "ko")), default);
        var output = await PluginReader.ReadAsync(translated.PluginPath, new(), default);
        Assert.Equal(new[] { "검", "Axe" }, output.Fields.Select(field => field.SourceText));
        Assert.DoesNotContain(output.Info.Diagnostics, d => d.Code == "compressed_trailing_bytes");
    }

    [Fact]
    public async Task MissingLocalizedTable_FailsBeforeImport()
    {
        using var fixture = new Fixture(Header(localized: true).Concat(Group(Record("WEAP", 0x800, Sub("FULL", UInt(1))))).ToArray());
        await Assert.ThrowsAsync<FileNotFoundException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(23)]
    [InlineData(40)]
    public async Task TruncatedPlugin_IsRejected(int count)
    {
        using var fixture = new Fixture(Build(false)[..count]);
        await Assert.ThrowsAnyAsync<Exception>(() => PluginReader.ReadAsync(fixture.Input, new(), default));
    }

    [Fact]
    public async Task SourceEncodingConversion_ConvertsUntranslatedDisplayStringsToo()
    {
        var bytes = Encoding.Latin1.GetBytes("Café\0");
        using var fixture = new Fixture(Header().Concat(Group(Record("WEAP", 0x800, Sub("FULL", bytes)))).ToArray());
        var document = await PluginReader.ReadAsync(fixture.Input, new(SourceEncoding: "windows-1252"), default);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal("Café", Assert.Single(output.Fields).SourceText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows1252Plugin_OpenedWithTheUtf8Default_IsReadAsWindows1252(bool localized)
    {
        // Serana Dialogue Add-On: 11,668 ASCII strings and five like "cliché" in Windows-1252.
        var cp1252 = PluginBinary.GetEncoding("windows-1252");
        var plugin = Header(localized).Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", localized ? UInt(1) : cp1252.GetBytes("Cliché\0"))),
            Record("WEAP", 0x801, Sub("FULL", localized ? UInt(2) : Z("Sword"))))).ToArray();
        using var fixture = new Fixture(plugin);
        if (localized) fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Cliché", [2] = "Sword" }, cp1252);

        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        Assert.Equal(new[] { "Cliché", "Sword" }, document.Fields.Select(field => field.SourceText));
        Assert.Equal("windows-1252", document.Info.Options.SourceEncoding);
        Assert.Contains(document.Info.Diagnostics, item => item.Code == "source_encoding_fallback" && !item.BlocksExport);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [document.Fields[1].Key] = "검" },
            new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal(new[] { "Cliché", "검" }, output.Fields.Select(field => field.SourceText));
    }

    // A user's mod stopped at an INFO:NAM1 with a Windows-1252 "’" (0x92) while other strings were UTF-8, and opened only
    // after being saved once in xTranslator. Read whole as Windows-1252, the valid UTF-8 "Café" became "CafÃ©"; each
    // string is now read by itself, and the export writes every string as UTF-8.
    [Fact]
    public async Task PluginMixingUtf8AndWindows1252_IsReadStringByString_AndSavedAsUtf8()
    {
        var cp1252 = PluginBinary.GetEncoding("windows-1252");
        using var fixture = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", Encoding.UTF8.GetBytes("Café\0"))),
            Record("WEAP", 0x801, Sub("FULL", cp1252.GetBytes("Don\u2019t move.\0"))),
            Record("WEAP", 0x802, Sub("FULL", Z("Sword"))))).ToArray());

        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        Assert.Equal(new[] { "Café", "Don\u2019t move.", "Sword" }, document.Fields.Select(field => field.SourceText));
        Assert.Equal("utf-8+windows-1252", document.Info.Options.SourceEncoding);
        var note = Assert.Single(document.Info.Diagnostics, item => item.Code == "source_encoding_mixed");
        Assert.False(note.BlocksExport);
        Assert.Contains("windows-1252로 읽은 줄 1개", note.Message);

        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [document.Fields[2].Key] = "검" },
            new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal("utf-8", output.Info.Options.SourceEncoding);
        Assert.Equal(new[] { "Café", "Don\u2019t move.", "검" }, output.Fields.Select(field => field.SourceText));
    }

    // CP949 Korean beside valid UTF-8 (two tools saved it): the UTF-8 text outweighed the CP949 bytes in the Korean
    // check, and the file opened string by string with "강철 검" as "°­Ã¶ °Ë".
    [Theory]
    [InlineData("강철 단검")]
    [InlineData("Café — \u201Cquoted\u201D résumé naïve")]
    public async Task Cp949KoreanBesideUtf8_KeepsTheErrorWithAKoreanHint(string utf8Text)
    {
        using var fixture = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", PluginBinary.GetEncoding("ks_c_5601-1987").GetBytes("강철 검\0"))),
            Record("WEAP", 0x801, Sub("FULL", Encoding.UTF8.GetBytes(utf8Text + "\0"))))).ToArray());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));

        Assert.EndsWith(PluginReader.Cp949KoreanHint, error.Message);
    }

    [Fact]
    public async Task LocalizedPluginMixingUtf8AndWindows1252_CountsOnlyItsRows()
    {
        var plugin = Header(localized: true).Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", UInt(1))),
            Record("WEAP", 0x801, Sub("FULL", UInt(2))))).ToArray();
        using var fixture = new Fixture(plugin);
        var cp1252 = PluginBinary.GetEncoding("windows-1252");
        fixture.Table(PluginStringTableKind.Strings, new() { [1] = "Café", [2] = "Smith\u2019s Hammer", [3] = "Unused \u2019" },
            entry => entry == "Café" ? Encoding.UTF8 : cp1252);

        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        Assert.Equal(new[] { "Café", "Smith\u2019s Hammer" }, document.Fields.Select(field => field.SourceText));
        Assert.Contains("windows-1252로 읽은 줄 1개", Assert.Single(document.Info.Diagnostics, item => item.Code == "source_encoding_mixed").Message);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default);
        // The export writes the table's unused Windows-1252 string as UTF-8 too.
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal("utf-8", output.Info.Options.SourceEncoding);
        Assert.Equal("Unused \u2019", output.Tables.Values.Single().Strings[3]);
    }

    // An earlier English release is read with ForOtherRelease of the current plugin's options: a current plugin read as
    // windows-1252 under the UTF-8 setting made a UTF-8 earlier release read as windows-1252 too, "Café" as "CafÃ©".
    [Theory]
    [InlineData("windows-1252")]
    [InlineData("utf-8+windows-1252")]
    public async Task OtherRelease_OfAPluginReadByFallback_IsReadWithTheChosenEncoding(string currentEncoding)
    {
        using var fixture = new Fixture(Header().Concat(Group(Record("WEAP", 0x800, Sub("FULL", Encoding.UTF8.GetBytes("Café\0"))))).ToArray());

        var options = new PluginReadOptions(SourceEncoding: currentEncoding).ForOtherRelease("utf-8");
        var earlier = await PluginReader.ReadAsync(fixture.Input, options, default);

        Assert.Equal("utf-8", options.SourceEncoding);
        Assert.Equal("Café", Assert.Single(earlier.Fields).SourceText);
        // A windows-1252 the user chose stays so (a CP949-looking English mod opened that way links its earlier release).
        Assert.Equal("windows-1252", new PluginReadOptions(SourceEncoding: "windows-1252").ForOtherRelease("windows-1252").SourceEncoding);
        Assert.Equal("ks_c_5601-1987", new PluginReadOptions(SourceEncoding: "ks_c_5601-1987").ForOtherRelease().SourceEncoding);
    }

    // An older Korean release saved in CP949 was refused as a translated release (read in the UTF-8 output encoding),
    // so neither the GUI's 이전 번역 nor the agent's korean could use it. Read whole as CP949, a UTF-8 line of a mixed
    // release ("강철") became other Hangul ("媛뺤쿋") without an error; each line is read by itself.
    [Fact]
    public async Task TranslatedRelease_SavedInCp949_IsReadAsCp949_LineByLine()
    {
        var cp949 = PluginBinary.GetEncoding("ks_c_5601-1987");
        using var korean = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", cp949.GetBytes("강철 장검\0"))),
            Record("WEAP", 0x801, Sub("FULL", Encoding.UTF8.GetBytes("강철\0"))))).ToArray());
        using var utf8 = new Fixture(Header().Concat(Group(Record("WEAP", 0x800, Sub("FULL", Encoding.UTF8.GetBytes("강철 장검\0"))))).ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() => PluginReader.ReadAsync(korean.Input, new(), default));
        var read = await PluginReader.ReadTranslatedReleaseAsync(korean.Input, new(), default);
        var unchanged = await PluginReader.ReadTranslatedReleaseAsync(utf8.Input, new(), default);

        Assert.Equal(new[] { "강철 장검", "강철" }, read.Fields.Select(field => field.SourceText));
        Assert.Contains(read.Info.Diagnostics, item => item.Code == PluginReader.Cp949ReadDiagnostic);
        Assert.Equal("강철 장검", Assert.Single(unchanged.Fields).SourceText);
        Assert.DoesNotContain(unchanged.Info.Diagnostics, item => item.Code == PluginReader.Cp949ReadDiagnostic);
    }

    // 217 common CP949 syllables are valid UTF-8 too: a line of only those ("천", "화환") reads as UTF-8 with no Hangul
    // ("õ", "ȭȯ") and is left out of the earlier translation. It is counted and told.
    [Fact]
    public async Task TranslatedRelease_InCp949_TellsTheLinesThatReadAsUtf8Too()
    {
        var cp949 = PluginBinary.GetEncoding("ks_c_5601-1987");
        using var korean = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", cp949.GetBytes("강철 장검\0"))),
            Record("WEAP", 0x801, Sub("FULL", cp949.GetBytes("천\0"))),
            Record("WEAP", 0x802, Sub("FULL", cp949.GetBytes("화환\0"))),
            Record("WEAP", 0x803, Sub("FULL", Encoding.UTF8.GetBytes("강철\0"))),
            Record("WEAP", 0x804, Sub("FULL", cp949.GetBytes("Range 10°\0"))))).ToArray());

        var read = await PluginReader.ReadTranslatedReleaseAsync(korean.Input, new(), default);

        // "10°" saved in CP949 is not valid UTF-8 and was read as CP949: not counted.
        Assert.Equal(new[] { "강철 장검", "õ", "ȭȯ", "강철", "Range 10°" }, read.Fields.Select(field => field.SourceText));
        var note = Assert.Single(read.Info.Diagnostics, item => item.Code == PluginReader.Cp949AmbiguousDiagnostic);
        Assert.Contains("2개", note.Message);
    }

    // A line neither UTF-8 nor CP949 reads: the error no longer says to choose CP949 as the source encoding.
    [Fact]
    public async Task TranslatedRelease_NeitherUtf8NorCp949_SaysSo()
    {
        using var korean = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", PluginBinary.GetEncoding("ks_c_5601-1987").GetBytes("강철 장검\0"))),
            Record("WEAP", 0x801, Sub("FULL", new byte[] { 0x41, 0x81, 0 })))).ToArray());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginReader.ReadTranslatedReleaseAsync(korean.Input, new(), default));

        Assert.StartsWith(PluginReader.TranslatedReleaseUnreadable, error.Message);
    }

    [Fact]
    public async Task MixedPlugin_ReopenedWithTheEncodingItWasReadWith_ReadsTheSame()
    {
        // An agent project keeps the encoding the plugin was read with and reads it again with it to export.
        using var fixture = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", Encoding.UTF8.GetBytes("Café\0"))),
            Record("WEAP", 0x801, Sub("FULL", PluginBinary.GetEncoding("windows-1252").GetBytes("Cliché\0"))))).ToArray());
        var first = await PluginReader.ReadAsync(fixture.Input, new(), default);

        var again = await PluginReader.ReadAsync(fixture.Input, first.Info.Options, default);

        Assert.Equal(first.Fields.Select(field => field.SourceText), again.Fields.Select(field => field.SourceText));
    }

    [Fact]
    public void MixedEncoding_ReadsEachStringByItself_AndWritesUtf8()
    {
        var mixed = PluginBinary.GetEncoding("utf-8+windows-1252");

        Assert.Equal("Café", mixed.GetString(Encoding.UTF8.GetBytes("Café")));
        Assert.Equal("Don\u2019t", mixed.GetString(new byte[] { 0x44, 0x6F, 0x6E, 0x92, 0x74 }));
        Assert.Equal(Encoding.UTF8.GetBytes("Don\u2019t 검"), mixed.GetBytes("Don\u2019t 검"));
        Assert.NotEqual(Encoding.UTF8.CodePage, mixed.CodePage);
        Assert.NotEqual(PluginBinary.GetEncoding("windows-1252").CodePage, mixed.CodePage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cp949KoreanPlugin_OpenedWithTheUtf8Default_KeepsTheErrorWithAKoreanHint(bool localized)
    {
        // An older Korean translation saved in CP949. Its bytes are valid Windows-1252 too, which would read
        // "검을 찾아라" as "°ËÀ» Ã£¾Æ¶ó" and write that mojibake into the next UTF-8 export.
        var cp949 = PluginBinary.GetEncoding("ks_c_5601-1987");
        var plugin = Header(localized).Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", localized ? UInt(1) : cp949.GetBytes("강철 검을 찾아라\0"))),
            Record("WEAP", 0x801, Sub("FULL", localized ? UInt(2) : Z("Sword"))))).ToArray();
        using var fixture = new Fixture(plugin);
        if (localized) fixture.Table(PluginStringTableKind.Strings, new() { [1] = "강철 검을 찾아라", [2] = "Sword" }, cp949);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));

        Assert.IsType<DecoderFallbackException>(error.InnerException);
        Assert.EndsWith(PluginReader.Cp949KoreanHint, error.Message);
        var korean = await PluginReader.ReadAsync(fixture.Input, new(SourceEncoding: "ks_c_5601-1987"), default);
        Assert.Equal(new[] { "강철 검을 찾아라", "Sword" }, korean.Fields.Select(field => field.SourceText));
    }

    [Fact]
    public async Task Windows1252SmartPunctuation_IsNotMistakenForCp949Korean()
    {
        // "Don’t—ever" and "Smith’s" are valid CP949 byte pairs (rare syllables), and "Æð" is even one common
        // Hangul syllable, but none forms a Korean word, so these English strings still fall back.
        var cp1252 = PluginBinary.GetEncoding("windows-1252");
        var texts = new[]
        {
            "Smith’s Hammer", "Don’t—ever—stop", "“Wait…” – she said", "‘Cliché’ naïve señor", "Æðelred’s shield", "Sword",
        };
        var plugin = Header().Concat(Group(texts.Select((text, index) =>
            Record("WEAP", 0x800u + (uint)index, Sub("FULL", cp1252.GetBytes(text + "\0")))).ToArray())).ToArray();
        using var fixture = new Fixture(plugin);

        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        Assert.Equal(texts, document.Fields.Select(field => field.SourceText));
        Assert.Equal("windows-1252", document.Info.Options.SourceEncoding);
        Assert.Contains(document.Info.Diagnostics, item => item.Code == "source_encoding_fallback");
    }

    [Theory]
    [InlineData("windows-1252", "windows-1252", "Café", false)]
    [InlineData("windows-1252", "windows-1252", "Café", true)]
    [InlineData("windows-1252", "utf-8", "Café", false)]
    [InlineData("windows-1252", "utf-8", "Café", true)]
    [InlineData("utf-8", "windows-1252", "한글", false)]
    [InlineData("utf-8", "windows-1252", "한글", true)]
    [InlineData("cp949", "cp949", "한글", false)]
    [InlineData("cp949", "cp949", "한글", true)]
    public async Task MetadataEncoding_IsIndependentAndPreservesMasterAndEditorIdBytes(
        string metadataEncoding, string sourceEncoding, string metadataName, bool localized)
    {
        var meta = PluginBinary.GetEncoding(metadataEncoding);
        var source = PluginBinary.GetEncoding(sourceEncoding);
        var sourceText = sourceEncoding == "windows-1252" ? "Épée" : "원문 검";
        var plugin = Header(localized, meta.GetBytes(metadataName + ".esm\0")).Concat(Group(Record("WEAP", 0x1000800,
            Sub("EDID", meta.GetBytes(metadataName + "Sword\0")), Sub("FULL", localized ? UInt(1) : source.GetBytes(sourceText + "\0"))))).ToArray();
        using var fixture = new Fixture(plugin);
        if (localized) fixture.Table(PluginStringTableKind.Strings, new() { [1] = sourceText, [99] = sourceText }, source);
        var readOptions = new PluginReadOptions(SourceEncoding: sourceEncoding, MetadataEncoding: metadataEncoding);
        var document = await PluginReader.ReadAsync(fixture.Input, readOptions, default);
        Assert.Equal(metadataName + ".esm", Assert.Single(document.Info.Masters));
        Assert.Equal(metadataName + "Sword", Assert.Single(document.Fields).EditorId);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [document.Fields[0].Key] = "강철 검" }, new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, readOptions with { SourceEncoding = "utf-8" }, default);
        Assert.Equal("강철 검", Assert.Single(output.Fields).SourceText);
        Assert.Equal(document.Info.Masters, output.Info.Masters);
        Assert.Equal(document.Fields[0].EditorId, output.Fields[0].EditorId);
        Assert.Equal(document.Records[0].Raw.ToArray(), output.Records[0].Raw.ToArray());
        Assert.Equal(document.Records[1].Subrecords[0].Data.ToArray(), output.Records[1].Subrecords[0].Data.ToArray());
        if (localized)
        {
            Assert.Equal(plugin, File.ReadAllBytes(result.PluginPath));
            Assert.Equal(sourceText, output.Tables[PluginStringTableKind.Strings].Strings[99]);
        }
    }

    [Fact]
    public async Task InvalidMetadataEncoding_ThrowsInsteadOfReplacingBytes()
    {
        using var fixture = new Fixture(Header().Concat(Group(Record("WEAP", 0x1000800,
            Sub("EDID", Encoding.Latin1.GetBytes("CaféSword\0")), Sub("FULL", Z("Sword"))))).ToArray());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginReader.ReadAsync(fixture.Input, new(MetadataEncoding: "utf-8"), default));
        Assert.IsType<DecoderFallbackException>(error.InnerException);
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        Assert.Equal("CaféSword", Assert.Single(document.Fields).EditorId);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default);
        Assert.Equal(File.ReadAllBytes(fixture.Input), File.ReadAllBytes(result.PluginPath));
    }

    [Fact]
    public async Task EditingOneRecord_PreservesUntouchedCompressedRecordBytes()
    {
        using var fixture = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x1000800, true, Sub("FULL", Z("Sword"))),
            Record("WEAP", 0x1000801, true, Sub("FULL", Z("Axe")), Sub("DATA", new byte[] { 42, 1, 2, 3 })))).ToArray());
        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [document.Fields[0].Key] = "검" }, new(fixture.Output), default);
        var output = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal(document.Records[2].Raw.ToArray(), output.Records[2].Raw.ToArray());
        Assert.Equal("검", output.Fields[0].SourceText);
        Assert.Equal("Axe", output.Fields[1].SourceText);
    }

    [Theory]
    [InlineData("WEAP")]
    [InlineData("ARMO")]
    public async Task DuplicateNonzeroFormId_IsRejectedEvenAcrossRecordTypes(string otherType)
    {
        using var fixture = new Fixture(Header()
            .Concat(Group(Record("WEAP", 0x1000800, Sub("FULL", Z("Sword")))))
            .Concat(Group(Record(otherType, 0x1000800, Sub("FULL", Z("Other"))))).ToArray());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));
        Assert.Contains("중복 FormID", error.Message);
        Assert.False(Directory.Exists(fixture.Output));
    }

    /// <summary>
    /// Skyrim LE plugins keep HEDR 0.94 and the SE game loads them (LyPlayerVoice.esp, masters Skyrim.esm, form 43/44).
    /// They were rejected as another game's file. Fallout 3/New Vegas also use 0.94, so a 0.94 header is accepted only
    /// with Skyrim.esm among the masters or a Skyrim form version (40-44) on the header record.
    /// </summary>
    [Theory]
    [InlineData("Skyrim.esm", 0, true)]
    [InlineData("Dawnguard.esm", 43, true)]
    [InlineData("FalloutNV.esm", 15, false)]
    [InlineData("Fallout3.esm", 0, false)]
    public async Task SkyrimLeHeader_IsAcceptedButNotFalloutsWithTheSameVersion(string master, ushort formVersion, bool accepted)
    {
        var bytes = Header(masterBytes: Z(master)).Concat(Group(Record("WEAP", 0x01000800, Sub("EDID", Z("ExampleSword")), Sub("FULL", Z("Steel Sword"))))).ToArray();
        BitConverter.GetBytes(0.94f).CopyTo(bytes, 30);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), formVersion);
        using var fixture = new Fixture(bytes);
        if (!accepted)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));
            return;
        }

        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);
        Assert.Single(document.Fields);
        Assert.False(Assert.Single(document.Info.Diagnostics, d => d.Code == "skyrim_le_header").BlocksExport);
        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string> { [document.Fields[0].Key] = "강철 검" }, new(fixture.Output), default);
        Assert.Equal("강철 검", Assert.Single((await PluginReader.ReadAsync(result.PluginPath, new(), default)).Fields).SourceText);
    }

    private static byte[] Build(bool compressed) => Header().Concat(Group(Record("WEAP", 0x01000800,
        compressed, Sub("EDID", Z("ExampleSword")), Sub("FULL", Z("Steel Sword")), Sub("DATA", new byte[] { 1, 0, 255, 88 }), Sub("ZZZZ", new byte[] { 9, 8, 7 })))).ToArray();

    internal static byte[] Header(bool localized = false, byte[]? masterBytes = null)
    {
        var hedr = new byte[12]; BitConverter.GetBytes(1.7f).CopyTo(hedr, 0);
        var record = Record("TES4", 0, Sub("HEDR", hedr), Sub("CNAM", Z("fixture author")), Sub("MAST", masterBytes ?? Z("Skyrim.esm")), Sub("DATA", new byte[8]));
        if (localized) BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), 0x80);
        return record;
    }
    internal static byte[] Record(string type, uint formId, params byte[][] subs) => Record(type, formId, false, subs);
    internal static byte[] Record(string type, uint formId, bool compressed, params byte[][] subs)
    {
        var payload = subs.SelectMany(sub => sub).ToArray();
        if (compressed) payload = PluginBinary.Deflate(payload);
        var result = new byte[24 + payload.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), compressed ? 0x40000u : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), formId);
        result[20] = 44; payload.CopyTo(result, 24); return result;
    }
    internal static byte[] Group(params byte[][] records)
    {
        var payload = records.SelectMany(record => record).ToArray();
        var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length);
        if (records.Length > 0) records[0].AsSpan(0, 4).CopyTo(result.AsSpan(8));
        payload.CopyTo(result, 24); return result;
    }
    internal static byte[] Sub(string type, byte[] payload)
    {
        var extended = payload.Length > ushort.MaxValue;
        var result = new byte[(extended ? 16 : 6) + payload.Length];
        var offset = 0;
        if (extended)
        {
            Encoding.ASCII.GetBytes("XXXX").CopyTo(result, 0); result[4] = 4;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(6), (uint)payload.Length); offset = 10;
        }
        Encoding.ASCII.GetBytes(type).CopyTo(result, offset);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 4), extended ? (ushort)0 : (ushort)payload.Length);
        payload.CopyTo(result, offset + 6); return result;
    }
    internal static byte[] Z(string text) => Encoding.UTF8.GetBytes(text + "\0");
    internal static byte[] UInt(uint value) => BitConverter.GetBytes(value);

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-plugin-" + Guid.NewGuid().ToString("N"));
        public string Input => Path.Combine(_root, "Example.esp");
        public string Output => Path.Combine(_root, "translated");
        public Fixture(byte[] plugin) { Directory.CreateDirectory(_root); File.WriteAllBytes(Input, plugin); }
        public void Table(PluginStringTableKind kind, Dictionary<uint, string> values, Encoding? encoding = null)
            => Table(kind, values, _ => encoding ?? Encoding.UTF8);
        public void Table(PluginStringTableKind kind, Dictionary<uint, string> values, Func<string, Encoding> encodingOf)
        {
            Directory.CreateDirectory(Path.Combine(_root, "Strings"));
            using var data = new MemoryStream();
            var directory = new byte[8 + values.Count * 8];
            BinaryPrimitives.WriteUInt32LittleEndian(directory, (uint)values.Count);
            var index = 0;
            foreach (var (id, value) in values)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(8 + index * 8), id);
                BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(12 + index * 8), (uint)data.Length);
                var text = encodingOf(value).GetBytes(value + "\0");
                if (kind != PluginStringTableKind.Strings) data.Write(UInt((uint)text.Length));
                data.Write(text); index++;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(4), (uint)data.Length);
            File.WriteAllBytes(Path.Combine(_root, "Strings", "Example_english." + PluginReader.Extension(kind)), directory.Concat(data.ToArray()).ToArray());
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
