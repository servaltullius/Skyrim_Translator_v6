using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using K4os.Compression.LZ4.Streams;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Tests;

public sealed class PluginArchiveReaderTests
{
    private const string TargetPath = "strings/example_english.strings";
    private static readonly byte[] Text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
        "스텐다르의 망치: <Alias=Player>\0Preserve the original archive.\n", 128)));

    public static IEnumerable<object[]> CompressionCases()
    {
        foreach (var version in new[] { 104u, 105u })
        foreach (var archiveCompressed in new[] { false, true })
        foreach (var flipCompression in new[] { false, true })
        foreach (var embeddedNames in new[] { false, true })
            yield return new object[] { version, archiveCompressed, flipCompression, embeddedNames };
    }

    [Theory]
    [MemberData(nameof(CompressionCases))]
    public async Task ReadsNamedMember_WithCompressionXorAndEmbeddedNames_WithoutChangingArchive(
        uint version, bool archiveCompressed, bool flipCompression, bool embeddedNames)
    {
        var fixture = CreateArchive(version, archiveCompressed, flipCompression, embeddedNames);
        await WithArchive(fixture.Bytes, async path =>
        {
            var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var actual = await PluginArchiveReader.ReadFileAsync(path, "STRINGS\\EXAMPLE_ENGLISH.STRINGS", default);
            Assert.Equal(Text, actual);
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        });
    }

    [Theory]
    [InlineData(104u)]
    [InlineData(105u)]
    public async Task MissingMember_ReturnsNull(uint version)
    {
        var fixture = CreateArchive(version);
        await WithArchive(fixture.Bytes, async path =>
            Assert.Null(await PluginArchiveReader.ReadFileAsync(path, "strings/missing.strings", default)));
    }

    [Fact]
    public async Task SseCompressedFlag_WithEqualSizeRawPayload_ReadsRawBytes()
    {
        var fixture = CreateArchive(105, archiveCompressed: true, equalSizeRawPayload: true);
        await WithArchive(fixture.Bytes, async path =>
            Assert.Equal(Text, await PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData(103u, 3u)]
    [InlineData(106u, 3u)]
    [InlineData(105u, 1u)]
    [InlineData(105u, 0x43u)]
    public async Task UnsupportedVersionOrArchiveFlags_ThrowExplicitException(uint version, uint flags)
    {
        var fixture = CreateArchive(105);
        PutU32(fixture.Bytes, 4, version);
        PutU32(fixture.Bytes, 12, flags);
        await WithArchive(fixture.Bytes, async path =>
            await Assert.ThrowsAsync<NotSupportedException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("folder_offset")]
    [InlineData("folder_count")]
    [InlineData("folder_name_terminator")]
    [InlineData("file_name_terminator")]
    [InlineData("data_offset_inside_directory")]
    [InlineData("data_offset_past_eof")]
    [InlineData("excessive_directory")]
    [InlineData("excessive_entries")]
    public async Task MalformedDirectory_IsRejected(string defect)
    {
        var fixture = CreateArchive(105);
        switch (defect)
        {
            case "signature": fixture.Bytes[0] = 0; break;
            case "folder_offset": PutU32(fixture.Bytes, 36 + 16, 0); break;
            case "folder_count": PutU32(fixture.Bytes, 36 + 8, 3); break;
            case "folder_name_terminator": fixture.Bytes[fixture.FirstFileRecord - 1] = 65; break;
            case "file_name_terminator": fixture.Bytes[fixture.DataStart - 1] = 65; break;
            case "data_offset_inside_directory": PutU32(fixture.Bytes, fixture.TargetFileRecord + 12, 0); break;
            case "data_offset_past_eof": PutU32(fixture.Bytes, fixture.TargetFileRecord + 12, uint.MaxValue); break;
            case "excessive_directory": PutU32(fixture.Bytes, 28, uint.MaxValue); break;
            case "excessive_entries": PutU32(fixture.Bytes, 20, uint.MaxValue); break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        await WithArchive(fixture.Bytes, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(80)]
    public async Task TruncatedHeaderOrDirectory_IsRejected(int length)
    {
        var fixture = CreateArchive(105);
        await WithArchive(fixture.Bytes[..length], async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData(104u, -1)]
    [InlineData(104u, 1)]
    [InlineData(105u, -1)]
    [InlineData(105u, 1)]
    public async Task ExpandedLengthMustMatchExactly(uint version, int difference)
    {
        var fixture = CreateArchive(version, archiveCompressed: true);
        PutU32(fixture.Bytes, fixture.TargetData, (uint)(Text.Length + difference));
        await WithArchive(fixture.Bytes, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData(104u)]
    [InlineData(105u)]
    public async Task OversizedExpandedLength_IsRejectedBeforeAllocation(uint version)
    {
        var fixture = CreateArchive(version, archiveCompressed: true);
        PutU32(fixture.Bytes, fixture.TargetData, 128 * 1024 * 1024 + 1u);
        await WithArchive(fixture.Bytes, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData(104u)]
    [InlineData(105u)]
    public async Task CorruptCompressedStream_IsRejected(uint version)
    {
        var fixture = CreateArchive(version, archiveCompressed: true);
        fixture.Bytes.AsSpan(fixture.TargetData + 4, 4).Clear();
        await WithArchive(fixture.Bytes, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Fact]
    public async Task TruncatedEmbeddedName_IsRejected()
    {
        var fixture = CreateArchive(105, embeddedNames: true);
        PutU32(fixture.Bytes, fixture.TargetFileRecord + 8, 1);
        await WithArchive(fixture.Bytes, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData("../example.strings")]
    [InlineData("strings/../example.strings")]
    [InlineData("C:\\example.strings")]
    [InlineData("\\example.strings")]
    public async Task NonRelativeMemberPath_IsRejectedBeforeOpeningArchive(string member)
        => await Assert.ThrowsAsync<InvalidDataException>(() =>
            PluginArchiveReader.ReadFileAsync("nonexistent.bsa", member, default));

    [Fact]
    public async Task PreCanceledRequest_DoesNotOpenArchive()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PluginArchiveReader.ReadFileAsync("nonexistent.bsa", TargetPath, cancellation.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ZlibMissingChecksumFooter_IsRejectedEvenWhenBsaRangesRemainValid(int removedBytes)
    {
        var fixture = CreateArchive(104, archiveCompressed: true);
        var truncated = fixture.Bytes[..^removedBytes];
        // The last member still fits its BSA record. Only the zlib trailer is incomplete.
        PutU32(truncated, fixture.TargetFileRecord + 8, (uint)(truncated.Length - fixture.TargetData));
        await WithArchive(truncated, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Fact]
    public async Task ZlibIncorrectAdlerChecksum_IsRejected()
    {
        var fixture = CreateArchive(104, archiveCompressed: true);
        fixture.Bytes[^1] ^= 0xFF;
        await WithArchive(fixture.Bytes, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZlibTrailingBytesInsideMember_AreRejected(bool anotherZlibStream)
    {
        var fixture = CreateArchive(104, archiveCompressed: true);
        var extra = anotherZlibStream
            ? MakePayload(104, Text, "unused", compressed: true, embeddedName: false, equalSizeRawPayload: false)[4..]
            : new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var extended = fixture.Bytes.Concat(extra).ToArray();
        PutU32(extended, fixture.TargetFileRecord + 8, (uint)(extended.Length - fixture.TargetData));
        await WithArchive(extended, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lz4TrailingBytesInsideMember_AreRejected(bool anotherLz4Frame)
    {
        var fixture = CreateArchive(105, archiveCompressed: true);
        var extra = anotherLz4Frame
            ? MakePayload(105, Text, "unused", compressed: true, embeddedName: false, equalSizeRawPayload: false)[4..]
            : new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var extended = fixture.Bytes.Concat(extra).ToArray();
        // Both frames/junk belong to this member, so its directory range is still valid.
        PutU32(extended, fixture.TargetFileRecord + 8, (uint)(extended.Length - fixture.TargetData));
        await WithArchive(extended, async path =>
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginArchiveReader.ReadFileAsync(path, TargetPath, default)));
    }

    /// <summary>
    /// In Skyrim SE the string tables of Update, Dawnguard, HearthFires and Dragonborn are inside
    /// "Skyrim - Interface.bsa", which the default archive search (same name, or "name - …") did not include, so
    /// those masters could not be opened from the Data folder without naming the archive.
    /// </summary>
    [Fact]
    public async Task LocalizedReader_FindsTablesInTheInterfaceArchiveOfTheSameFolder()
    {
        using var fixture = new LocalizedFixture();
        var archive = fixture.Archive("Skyrim - Interface.bsa", "Interface title");

        var document = await PluginReader.ReadAsync(fixture.Input, new(), default);

        Assert.Equal("Interface title", document.Fields.Single(field => field.TableKind == PluginStringTableKind.Strings).SourceText);
        Assert.Equal(archive.Path, document.Info.StringTableSources![PluginStringTableKind.Strings]);
    }

    [Fact]
    public async Task LocalizedReader_LooseTableOverridesConflictingArchives_AndTracksEachSource()
    {
        using var fixture = new LocalizedFixture();
        var first = fixture.Archive("first.bsa", "First archive title");
        var second = fixture.Archive("second.bsa", "Conflicting archive title");
        var loosePath = fixture.LooseTable(PluginStringTableKind.Strings, "Loose title");
        var document = await PluginReader.ReadAsync(fixture.Input,
            new(ArchivePaths: new[] { first.Path, second.Path }), default);

        Assert.Equal("Loose title", document.Fields.Single(field => field.TableKind == PluginStringTableKind.Strings).SourceText);
        Assert.Equal("Book text", document.Fields.Single(field => field.TableKind == PluginStringTableKind.DlStrings).SourceText);
        Assert.Equal("Dialogue text", document.Fields.Single(field => field.TableKind == PluginStringTableKind.IlStrings).SourceText);
        Assert.NotNull(document.Info.StringTableSources);
        Assert.Equal(loosePath, document.Info.StringTableSources![PluginStringTableKind.Strings]);
        Assert.Equal(first.Path, document.Info.StringTableSources[PluginStringTableKind.DlStrings]);
        Assert.Equal(first.Path, document.Info.StringTableSources[PluginStringTableKind.IlStrings]);

        var result = await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default);
        var reread = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.Equal(document.Fields.Select(field => field.SourceText), reread.Fields.Select(field => field.SourceText));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalizedReader_ConflictingArchiveTables_AreRejectedRegardlessOfOrder(bool reverse)
    {
        using var fixture = new LocalizedFixture();
        var first = fixture.Archive("first.bsa", "First archive title");
        var second = fixture.Archive("second.bsa", "Different archive title");
        var archives = reverse ? new[] { second.Path, first.Path } : new[] { first.Path, second.Path };
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            PluginReader.ReadAsync(fixture.Input, new(ArchivePaths: archives), default));
        Assert.Contains("example_english.STRINGS", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(first.Path, error.Message);
        Assert.Contains(second.Path, error.Message);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task LocalizedReader_IdenticalTablesInDifferentArchiveFormats_ExportSuccessfully()
    {
        using var fixture = new LocalizedFixture();
        var first = fixture.Archive("first.bsa", "Archive title", version: 104, compressed: true, embeddedNames: true);
        var second = fixture.Archive("second.bsa", "Archive title", version: 105);
        var document = await PluginReader.ReadAsync(fixture.Input,
            new(ArchivePaths: new[] { first.Path, second.Path }), default);

        Assert.Equal(3, document.Fields.Count);
        Assert.NotNull(document.Info.StringTableSources);
        Assert.Equal(3, document.Info.StringTableSources!.Count);
        Assert.All(document.Info.StringTableSources.Values, path => Assert.Equal(first.Path, path));
        // Reading the second/third table must not hash the stream again from its previous EOF position.
        Assert.Equal(2, document.DependencyHashes.Count);
        foreach (var archive in new[] { first.Path, second.Path })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))), document.DependencyHashes[archive]);

        var translations = document.Fields.ToDictionary(field => field.Key, field => "번역: " + field.SourceText);
        var result = await PluginWriter.ExportAsync(document, translations, new(fixture.Output), default);
        Assert.Equal(3, result.ChangedFields);
        Assert.Equal(await File.ReadAllBytesAsync(fixture.Input), await File.ReadAllBytesAsync(result.PluginPath));
        var reread = await PluginReader.ReadAsync(result.PluginPath, new(), default);
        Assert.All(reread.Fields, field => Assert.Equal(translations[field.Key], field.SourceText));
    }

    [Fact]
    public async Task LocalizedReader_RepeatedExplicitArchivePath_IsOneDependency()
    {
        using var fixture = new LocalizedFixture();
        var archive = fixture.Archive("example.bsa", "Archive title");
        var document = await PluginReader.ReadAsync(fixture.Input,
            new(ArchivePaths: new[] { archive.Path, archive.Path }), default);
        Assert.Equal(3, document.Fields.Count);
        Assert.Single(document.DependencyHashes);
        Assert.Single(document.Info.Options.ArchivePaths!);
        await PluginWriter.ExportAsync(document, new Dictionary<string, string>(), new(fixture.Output), default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LocalizedExport_ChangedArchiveIsRejected_EvenWhenAllStringTablesAreUnchanged(int changedArchive)
    {
        using var fixture = new LocalizedFixture();
        var archives = new[]
        {
            fixture.Archive("first.bsa", "Archive title"),
            fixture.Archive("second.bsa", "Archive title"),
        };
        var document = await PluginReader.ReadAsync(fixture.Input,
            new(ArchivePaths: archives.Select(archive => archive.Path).ToArray()), default);
        var changed = archives[changedArchive];
        var originalTable = await PluginArchiveReader.ReadFileAsync(changed.Path, TargetPath, default);
        var bytes = await File.ReadAllBytesAsync(changed.Path);
        bytes[changed.Layout.DataStart] ^= 1; // Change only misc/readme.txt; the archive remains structurally valid.
        await File.WriteAllBytesAsync(changed.Path, bytes);
        Assert.Equal(originalTable, await PluginArchiveReader.ReadFileAsync(changed.Path, TargetPath, default));

        await Assert.ThrowsAsync<InvalidDataException>(() => PluginWriter.ExportAsync(document,
            new Dictionary<string, string>(), new(fixture.Output), default));
        Assert.False(Directory.Exists(fixture.Output));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Root, ".translated.*.tmp"));
    }

    private sealed record Fixture(byte[] Bytes, int FirstFileRecord, int TargetFileRecord, int DataStart, int TargetData);

    // Hash values are deliberately unused: named lookup must honor both name tables and record order.
    // The two folders ensure that selecting a strings member does not accidentally read the first entry.
    private static Fixture CreateArchive(uint version, bool archiveCompressed = false,
        bool flipCompression = false, bool embeddedNames = false, bool equalSizeRawPayload = false,
        byte[]? targetContents = null, IReadOnlyDictionary<string, byte[]>? additionalStringTables = null)
    {
        var folders = new[] { "misc", "strings" };
        var additional = additionalStringTables?.ToArray() ?? Array.Empty<KeyValuePair<string, byte[]>>();
        var names = new[] { "readme.txt", "example_english.strings" }.Concat(additional.Select(pair => pair.Key)).ToArray();
        var contents = new[] { Encoding.UTF8.GetBytes("A different file."), targetContents ?? Text }
            .Concat(additional.Select(pair => pair.Value)).ToArray();
        var folderCounts = new[] { 1, names.Length - 1 };
        var folderNames = folders.Select(name => Encoding.UTF8.GetBytes(name + '\0')).ToArray();
        var fileNames = Encoding.UTF8.GetBytes(string.Join('\0', names) + '\0');
        var compressed = archiveCompressed ^ flipCompression;
        var payloads = contents.Select((content, index) => MakePayload(version, content,
            folders[index == 0 ? 0 : 1] + "\\" + names[index], compressed, embeddedNames, equalSizeRawPayload)).ToArray();
        var folderHeaderSize = version == 105 ? 24 : 16;
        var dataStart = 36 + 2 * folderHeaderSize + 2 + folderNames.Sum(name => name.Length) + names.Length * 16 + fileNames.Length;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("BSA\0"u8);
        writer.Write(version);
        writer.Write(36u);
        writer.Write(3u | (archiveCompressed ? 4u : 0) | (embeddedNames ? 0x100u : 0));
        writer.Write(2u);
        writer.Write((uint)names.Length);
        writer.Write((uint)folderNames.Sum(name => name.Length));
        writer.Write((uint)fileNames.Length);
        writer.Write(0u);
        var folderPosition = 36 + 2 * folderHeaderSize;
        for (var index = 0; index < 2; index++)
        {
            writer.Write(0ul);
            writer.Write((uint)folderCounts[index]);
            var folderOffset = folderPosition + fileNames.Length;
            if (version == 105)
            {
                writer.Write(0u);
                writer.Write((ulong)folderOffset);
            }
            else writer.Write((uint)folderOffset);
            folderPosition += 1 + folderNames[index].Length + folderCounts[index] * 16;
        }
        var fileRecordPositions = new int[names.Length];
        var dataPosition = dataStart;
        var fileIndex = 0;
        for (var index = 0; index < 2; index++)
        {
            writer.Write((byte)folderNames[index].Length);
            writer.Write(folderNames[index]);
            for (var member = 0; member < folderCounts[index]; member++, fileIndex++)
            {
                fileRecordPositions[fileIndex] = (int)stream.Position;
                writer.Write(0ul);
                writer.Write((uint)payloads[fileIndex].Length | (flipCompression ? 0x40000000u : 0));
                writer.Write((uint)dataPosition);
                dataPosition += payloads[fileIndex].Length;
            }
        }
        writer.Write(fileNames);
        foreach (var payload in payloads) writer.Write(payload);
        return new Fixture(stream.ToArray(), fileRecordPositions[0], fileRecordPositions[1],
            dataStart, dataStart + payloads[0].Length);
    }

    private static byte[] MakePayload(uint version, byte[] content, string name,
        bool compressed, bool embeddedName, bool equalSizeRawPayload)
    {
        using var stream = new MemoryStream();
        if (embeddedName)
        {
            var bytes = Encoding.UTF8.GetBytes(name); // Embedded names are length-prefixed, without NUL.
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        if (!compressed) stream.Write(content);
        else
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)content.Length);
            stream.Write(length);
            if (equalSizeRawPayload) stream.Write(content);
            else
            {
                using Stream encoder = version == 105
                    ? LZ4Stream.Encode(stream, leaveOpen: true)
                    : new ZLibStream(stream, CompressionLevel.SmallestSize, leaveOpen: true);
                encoder.Write(content);
            }
        }
        return stream.ToArray();
    }

    private static void PutU32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    private sealed class LocalizedFixture : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"xt-localized-bsa-{Guid.NewGuid():N}"));
        public string Input => Path.Combine(Root, "example.esp");
        public string Output => Path.Combine(Root, "translated");

        public LocalizedFixture()
        {
            Directory.CreateDirectory(Root);
            var plugin = PluginReadWriteTests.Header(localized: true)
                .Concat(PluginReadWriteTests.Group(PluginReadWriteTests.Record("WEAP", 0x01000800,
                    PluginReadWriteTests.Sub("FULL", PluginReadWriteTests.UInt(1)))))
                .Concat(PluginReadWriteTests.Group(PluginReadWriteTests.Record("BOOK", 0x01000801,
                    PluginReadWriteTests.Sub("DESC", PluginReadWriteTests.UInt(2)))))
                .Concat(PluginReadWriteTests.Group(PluginReadWriteTests.Record("INFO", 0x01000802,
                    PluginReadWriteTests.Sub("NAM1", PluginReadWriteTests.UInt(3))))).ToArray();
            File.WriteAllBytes(Input, plugin);
        }

        public (string Path, Fixture Layout) Archive(string name, string title,
            uint version = 105, bool compressed = false, bool embeddedNames = false)
        {
            var extra = new Dictionary<string, byte[]>
            {
                ["example_english.dlstrings"] = TableBytes(PluginStringTableKind.DlStrings, "Book text"),
                ["example_english.ilstrings"] = TableBytes(PluginStringTableKind.IlStrings, "Dialogue text"),
            };
            var archive = CreateArchive(version, archiveCompressed: compressed, embeddedNames: embeddedNames,
                targetContents: TableBytes(PluginStringTableKind.Strings, title), additionalStringTables: extra);
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, archive.Bytes);
            return (path, archive);
        }

        public string LooseTable(PluginStringTableKind kind, string text)
        {
            var directory = Path.Combine(Root, "Strings");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "example_english." + PluginReader.Extension(kind));
            File.WriteAllBytes(path, TableBytes(kind, text));
            return path;
        }

        private static byte[] TableBytes(PluginStringTableKind kind, string text)
        {
            var value = Encoding.UTF8.GetBytes(text + '\0');
            var payload = kind == PluginStringTableKind.Strings
                ? value : BitConverter.GetBytes((uint)value.Length).Concat(value).ToArray();
            var bytes = new byte[16 + payload.Length];
            PutU32(bytes, 0, 1);
            PutU32(bytes, 4, (uint)payload.Length);
            PutU32(bytes, 8, kind switch
            {
                PluginStringTableKind.Strings => 1,
                PluginStringTableKind.DlStrings => 2,
                PluginStringTableKind.IlStrings => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            });
            payload.CopyTo(bytes, 16);
            return bytes;
        }

        public void Dispose()
        {
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(Root), temporaryRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(Root).StartsWith("xt-localized-bsa-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup path.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static async Task WithArchive(byte[] bytes, Func<string, Task> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-bsa-{Guid.NewGuid():N}.bsa");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            await action(path);
        }
        finally { File.Delete(path); }
    }
}
