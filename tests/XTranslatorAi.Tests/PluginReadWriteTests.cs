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
    [InlineData("trailing-data")]
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

    [Fact]
    public async Task PluginMixingUtf8AndWindows1252_KeepsTheEncodingError()
    {
        // Reading it as Windows-1252 would turn the valid UTF-8 "Café" into "CafÃ©".
        using var fixture = new Fixture(Header().Concat(Group(
            Record("WEAP", 0x800, Sub("FULL", Encoding.UTF8.GetBytes("Café\0"))),
            Record("WEAP", 0x801, Sub("FULL", PluginBinary.GetEncoding("windows-1252").GetBytes("Cliché\0"))))).ToArray());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginReader.ReadAsync(fixture.Input, new(), default));

        Assert.IsType<DecoderFallbackException>(error.InnerException);
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
                var text = (encoding ?? Encoding.UTF8).GetBytes(value + "\0");
                if (kind != PluginStringTableKind.Strings) data.Write(UInt((uint)text.Length));
                data.Write(text); index++;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(4), (uint)data.Length);
            File.WriteAllBytes(Path.Combine(_root, "Strings", "Example_english." + PluginReader.Extension(kind)), directory.Concat(data.ToArray()).ToArray());
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
