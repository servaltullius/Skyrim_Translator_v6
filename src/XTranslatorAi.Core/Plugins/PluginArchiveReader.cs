using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4.Streams;

namespace XTranslatorAi.Core.Plugins;

/// <summary>Reads one named member without extracting or modifying its BSA archive.</summary>
public static class PluginArchiveReader
{
    private const int HeaderSize = 36;
    private const int MaxEntries = 1_000_000;
    private const int MaxDirectoryBytes = 64 * 1024 * 1024;
    private const int MaxMemberBytes = 128 * 1024 * 1024;
    private const uint FlipCompression = 0x40000000;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    // Format references (layout, compression XOR, and embedded name prefix):
    // https://github.com/Mutagen-Modding/Mutagen/tree/dev/Mutagen.Bethesda.Core/Archives/Bsa
    // https://github.com/TES5Edit/TES5Edit/blob/xedit-4.1.5/Core/wbBSArchive.pas
    public static async Task<byte[]?> ReadFileAsync(string archivePath, string relativePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var target = NormalizePath(relativePath, allowEmpty: false);
        ct.ThrowIfCancellationRequested();
        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        try
        {
            var header = new byte[HeaderSize];
            await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
            if (!header.AsSpan(0, 4).SequenceEqual("BSA\0"u8))
                throw new InvalidDataException("BSA 서명이 올바르지 않습니다.");
            var version = U32(header, 4);
            if (version is not (104 or 105))
                throw new NotSupportedException($"지원하지 않는 BSA 버전입니다: {version}. 104/105만 지원합니다.");
            var flags = U32(header, 12);
            if ((flags & 0x40) != 0) throw new NotSupportedException("Xbox BSA는 지원하지 않습니다.");
            if ((flags & 3) != 3)
                throw new NotSupportedException("폴더/파일 이름 테이블이 없는 BSA는 이름으로 찾을 수 없습니다.");
            var folderCount = U32(header, 16);
            var fileCount = U32(header, 20);
            if (folderCount > MaxEntries || fileCount > MaxEntries)
                throw new InvalidDataException("BSA 항목 수가 지원 상한을 초과합니다.");
            var folderNamesLength = U32(header, 24);
            var fileNamesLength = U32(header, 28);
            var folderHeaderSize = version == 105 ? 24 : 16;
            var folderRecordOffset = U32(header, 8);
            var directoryLength = (long)folderCount * folderHeaderSize + folderCount +
                folderNamesLength + (long)fileCount * 16 + fileNamesLength;
            if (folderRecordOffset < HeaderSize || directoryLength > MaxDirectoryBytes)
                throw new InvalidDataException("BSA 디렉터리 크기/위치가 지원 범위를 벗어납니다.");
            RequireRange(folderRecordOffset, directoryLength, stream.Length, "디렉터리");
            var directory = new byte[(int)directoryLength];
            stream.Position = folderRecordOffset;
            await stream.ReadExactlyAsync(directory, ct).ConfigureAwait(false);
            var dataStart = folderRecordOffset + directoryLength;
            var member = FindMember(directory, folderRecordOffset, folderHeaderSize, folderCount, fileCount,
                folderNamesLength, fileNamesLength, dataStart, stream.Length, target, ct);
            if (member is null) return null;

            var record = member.Value;
            var diskLength = record.RawSize & ~FlipCompression;
            if (diskLength > MaxMemberBytes)
                throw new InvalidDataException("BSA 문자열 파일의 저장 크기가 128 MiB를 초과합니다.");
            var stored = new byte[(int)diskLength];
            stream.Position = record.Offset;
            await stream.ReadExactlyAsync(stored, ct).ConfigureAwait(false);
            var start = 0;
            if ((flags & 0x100) != 0)
            {
                if (stored.Length == 0) throw new InvalidDataException("BSA 내장 파일명이 잘렸습니다.");
                start = 1 + stored[0];
                if (start > stored.Length) throw new InvalidDataException("BSA 내장 파일명 길이가 잘못되었습니다.");
            }
            var compressed = ((flags & 4) != 0) ^ ((record.RawSize & FlipCompression) != 0);
            if (!compressed) return stored.AsSpan(start).ToArray();
            if (stored.Length - start < 4) throw new InvalidDataException("BSA 압축 해제 길이가 잘렸습니다.");
            var expandedLength = U32(stored, start);
            start += 4;
            if (expandedLength > MaxMemberBytes)
                throw new InvalidDataException("BSA 문자열 파일의 압축 해제 크기가 128 MiB를 초과합니다.");

            // SSE archives can store an equal-sized, unencoded payload despite the compressed flag.
            // This is the same branch used by Mutagen's BsaFileRecord.AsStream.
            if (version == 105 && expandedLength == stored.Length - start)
                return stored.AsSpan(start).ToArray();
            if (version == 104)
                return PluginBinary.Inflate(stored.AsMemory(start - 4));
            try
            {
                using var input = new MemoryStream(stored, start, stored.Length - start, writable: false);
                using Stream decoder = LZ4Stream.Decode(input);
                var result = new byte[(int)expandedLength];
                await decoder.ReadExactlyAsync(result, ct).ConfigureAwait(false);
                var extra = new byte[1];
                if (await decoder.ReadAsync(extra, ct).ConfigureAwait(false) != 0)
                    throw new InvalidDataException("BSA 압축 해제 길이가 헤더와 다릅니다.");
                if (input.Position != input.Length)
                    throw new InvalidDataException("BSA LZ4 프레임 뒤에 불필요한 데이터가 있습니다.");
                ct.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException)
            {
                throw new InvalidDataException("BSA 압축 스트림이 올바르지 않습니다.", ex);
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("BSA 데이터 또는 압축 스트림이 잘렸습니다.", ex);
        }
    }

    private readonly record struct Member(string Folder, uint RawSize, uint Offset);

    private static Member? FindMember(byte[] directory, uint absoluteStart, int folderHeaderSize,
        uint folderCount, uint fileCount, uint folderNamesLength, uint fileNamesLength,
        long dataStart, long archiveLength, string target, CancellationToken ct)
    {
        var records = new Member[(int)fileCount];
        var cursor = checked((int)folderCount * folderHeaderSize);
        var nameBytes = 0L;
        var recordIndex = 0;
        for (var folderIndex = 0; folderIndex < folderCount; folderIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var headerPosition = folderIndex * folderHeaderSize;
            var count = U32(directory, headerPosition + 8);
            if (count > fileCount - recordIndex)
                throw new InvalidDataException("BSA 폴더별 파일 수가 전체 파일 수와 다릅니다.");
            var offset = folderHeaderSize == 24
                ? BinaryPrimitives.ReadUInt64LittleEndian(directory.AsSpan(headerPosition + 16, 8))
                : U32(directory, headerPosition + 12);
            // BSA folder offsets include the (physically later) file-name block length.
            if (offset != (ulong)absoluteStart + (uint)cursor + fileNamesLength)
                throw new InvalidDataException("BSA 폴더 레코드 offset이 이름/파일 레코드 위치와 다릅니다.");
            RequireRange(cursor, 1, directory.Length, "폴더 이름 길이");
            var length = directory[cursor++];
            RequireRange(cursor, length, directory.Length, "폴더 이름");
            if (length == 0 || directory[cursor + length - 1] != 0)
                throw new InvalidDataException("BSA 폴더 이름의 종료 문자가 없습니다.");
            var folder = NormalizePath(DecodeName(directory.AsSpan(cursor, length - 1)), allowEmpty: true);
            cursor += length;
            nameBytes += length;
            RequireRange(cursor, (long)count * 16, directory.Length, "파일 레코드");
            for (var index = 0; index < count; index++)
            {
                var rawSize = U32(directory, cursor + 8);
                var fileOffset = U32(directory, cursor + 12);
                var size = rawSize & ~FlipCompression;
                if (fileOffset < dataStart) throw new InvalidDataException("BSA 파일 데이터가 디렉터리와 겹칩니다.");
                RequireRange(fileOffset, size, archiveLength, "파일 데이터");
                records[recordIndex++] = new Member(folder, rawSize, fileOffset);
                cursor += 16;
            }
        }
        if (recordIndex != fileCount || nameBytes != folderNamesLength || directory.Length - cursor != fileNamesLength)
            throw new InvalidDataException("BSA 디렉터리의 파일 수/이름 길이가 일치하지 않습니다.");

        Member? found = null;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = directory.AsSpan(cursor);
            var length = remaining.IndexOf((byte)0);
            if (length <= 0) throw new InvalidDataException("BSA 파일 이름이 비어 있거나 잘렸습니다.");
            var name = DecodeName(remaining[..length]);
            if (name.Contains('/') || name.Contains('\\'))
                throw new InvalidDataException("BSA 파일 이름 테이블에 폴더 구분자가 있습니다.");
            var path = NormalizePath(record.Folder.Length == 0 ? name : record.Folder + "\\" + name, allowEmpty: false);
            if (!paths.Add(path)) throw new InvalidDataException($"BSA에 중복 파일 경로가 있습니다: {path}");
            if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase)) found = record;
            cursor += length + 1;
        }
        if (cursor != directory.Length) throw new InvalidDataException("BSA 파일 이름 테이블에 남는 데이터가 있습니다.");
        return found;
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static void RequireRange(long offset, long size, long total, string label)
    {
        if (offset < 0 || size < 0 || offset > total || size > total - offset)
            throw new InvalidDataException($"BSA {label} 범위가 파일을 벗어납니다.");
    }

    private static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0)) throw new InvalidDataException("BSA 이름에 잘못된 NUL 문자가 있습니다.");
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static string NormalizePath(string path, bool allowEmpty)
    {
        var normalized = path.Replace('/', '\\');
        if ((normalized.Length == 0 && !allowEmpty) || normalized.StartsWith('\\') ||
            normalized.Contains(':') || normalized.Contains('\0') ||
            normalized.Split('\\').Any(part => part is "." or ".."))
            throw new InvalidDataException("BSA 내부 상대 경로가 올바르지 않습니다.");
        return normalized;
    }
}
