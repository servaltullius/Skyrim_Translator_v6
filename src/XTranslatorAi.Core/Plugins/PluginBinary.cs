using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace XTranslatorAi.Core.Plugins;

internal static class PluginBinary
{
    internal const int HeaderSize = 24;
    internal const uint CompressedFlag = 0x00040000;
    internal const uint LocalizedFlag = 0x00000080;
    internal const uint DeletedFlag = 0x00000020;
    internal const int MaxDecompressedRecord = 256 * 1024 * 1024;
    internal static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    internal static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    internal static void SetU32(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
    internal static void SetU16(Span<byte> data, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);
    internal static string Signature(ReadOnlySpan<byte> data, bool allowBinary = false)
    {
        if (data.Length < 4) throw new InvalidDataException("잘린 레코드 서명입니다.");
        if (!allowBinary)
            for (var i = 0; i < 4; i++)
                if (data[i] is < 32 or > 126) throw new InvalidDataException("유효하지 않은 레코드 서명입니다.");
        // IMAD parameter subrecords use binary prefixes such as 00 49 41 44.
        return Encoding.Latin1.GetString(data[..4]);
    }
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static Encoding GetEncoding(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized is not ("utf-8" or "utf8" or "windows-1252" or "cp1252" or "1252" or "949" or "cp949" or "ks_c_5601-1987"))
            throw new NotSupportedException($"지원하지 않는 플러그인 인코딩: {name}");
        if (normalized is "cp1252" or "1252") normalized = "windows-1252";
        if (normalized is "cp949" or "949") normalized = "ks_c_5601-1987";
        if (normalized == "utf8") normalized = "utf-8";
        return Encoding.GetEncoding(normalized, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
    internal static string ReadZString(ReadOnlySpan<byte> data, Encoding encoding, string context)
    {
        if (data.Length == 0 || data[^1] != 0 || data[..^1].Contains((byte)0))
            throw new InvalidDataException($"문자열 종료/길이가 잘못되었습니다: {context}");
        try { return encoding.GetString(data[..^1]); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException($"원문 인코딩으로 해석할 수 없습니다: {context}. 올바른 인코딩을 선택하세요.", ex); }
    }
    internal static byte[] EncodeZString(string text, Encoding encoding)
    {
        if (text.Contains('\0')) throw new InvalidDataException("번역문 안에 NUL 문자를 저장할 수 없습니다.");
        var bytes = new byte[checked(encoding.GetByteCount(text) + 1)];
        encoding.GetBytes(text, bytes);
        return bytes;
    }
    internal static byte[] Inflate(ReadOnlyMemory<byte> bytes) => Inflate(bytes, allowTrailingBytes: false, out _);

    /// <summary>
    /// With <paramref name="allowTrailingBytes"/>, bytes after a complete stream whose checksum and declared length
    /// match are accepted and counted instead of rejected. Some plugins ship records like that (The Great Town of
    /// Karthwasten: 10 records with 1-27 extra bytes) and the game reads them.
    /// </summary>
    internal static byte[] Inflate(ReadOnlyMemory<byte> bytes, bool allowTrailingBytes, out int trailingBytes)
    {
        trailingBytes = 0;
        if (bytes.Length < 6) throw new InvalidDataException("잘린 압축 레코드입니다.");
        var length = U32(bytes.Span, 0);
        if (length > MaxDecompressedRecord) throw new InvalidDataException("압축 해제 레코드가 지원 크기(256 MiB)를 초과합니다.");
        var result = new byte[checked((int)length)];
        // Stream readers may buffer trailing/truncated bytes. Inflater exposes the
        // actual end marker, checksum and remaining input, which all must agree.
        var inflater = new Inflater(noHeader: false);
        inflater.SetInput(bytes[4..].ToArray());
        var offset = 0;
        var overflow = new byte[1];
        try
        {
            while (!inflater.IsFinished)
            {
                var read = offset < result.Length
                    ? inflater.Inflate(result, offset, result.Length - offset)
                    : inflater.Inflate(overflow);
                if (offset == result.Length && read > 0) throw new InvalidDataException("압축 해제 결과가 선언 길이를 초과합니다.");
                offset += read;
                if (read == 0 && !inflater.IsFinished) throw new InvalidDataException("잘렸거나 사전이 필요한 zlib 레코드입니다.");
            }
        }
        catch (SharpZipBaseException ex) { throw new InvalidDataException("zlib 레코드의 압축 데이터/체크섬이 잘못되었습니다.", ex); }
        if (offset == result.Length && inflater.RemainingInput != 0 && allowTrailingBytes)
            trailingBytes = inflater.RemainingInput;
        else if (offset != result.Length || inflater.RemainingInput != 0)
            throw new InvalidDataException("압축 레코드의 길이 또는 종료 위치가 일치하지 않습니다.");
        return result;
    }
    internal static byte[] Deflate(ReadOnlySpan<byte> payload)
    {
        using var output = new MemoryStream();
        output.Write(new byte[4]);
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(payload);
        var result = output.ToArray();
        SetU32(result, 0, checked((uint)payload.Length));
        return result;
    }
}
