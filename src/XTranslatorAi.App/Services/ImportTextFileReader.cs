using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.App.Services;

/// <summary>A glossary or TM file that was not imported because its text could not be decoded reliably.</summary>
public sealed class ImportFileEncodingException : Exception
{
    public ImportFileEncodingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads glossary and TM files that users make by hand. Imports assumed UTF-8, but a TSV saved from Excel on Korean
/// Windows ("텍스트(탭으로 분리)") is CP949: read as UTF-8 every Korean target became U+FFFD replacement characters,
/// and those were imported as forced glossary terms. The text is now decoded strictly: a byte-order mark decides
/// (UTF-8, UTF-16), otherwise UTF-8 is tried and then CP949, and text that still holds U+FFFD (a file already
/// broken by an earlier save) is refused with a Korean message instead of being imported.
/// </summary>
public static class ImportTextFileReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        => Decode(await File.ReadAllBytesAsync(path, cancellationToken), Path.GetFileName(path));

    /// <summary>Lines split the way <see cref="File.ReadAllLines(string)"/> does (CR, LF or CRLF).</summary>
    public static async Task<IReadOnlyList<string>> ReadAllLinesAsync(string path, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        using var reader = new StringReader(await ReadAllTextAsync(path, cancellationToken));
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    public static string Decode(byte[] bytes, string fileName)
    {
        var text = DecodeStrict(bytes, fileName);
        if (text.Contains('�', StringComparison.Ordinal))
        {
            throw new ImportFileEncodingException(
                $"{fileName}에 깨진 글자(�)가 들어 있어 가져오지 않았습니다. 이미 글자가 깨진 채 저장된 파일입니다. "
                + "원본 파일을 UTF-8로 다시 저장해 가져오세요.");
        }

        return text;
    }

    private static string DecodeStrict(byte[] bytes, string fileName)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return DecodeWithBom(StrictUtf8, bytes, 3, fileName, "UTF-8");
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return DecodeWithBom(new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), bytes, 2, fileName, "UTF-16");
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return DecodeWithBom(new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), bytes, 2, fileName, "UTF-16");
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: the ANSI code page of Korean Windows, which Excel and Notepad use for "ANSI" text.
        }

        string cp949;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            cp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw NotUtf8OrCp949(fileName, ex);
        }

        // CP949 decodes a lone 0x80 or 0xFF (e.g. € or ÿ of a Western ANSI file) to a C1 control or a private-use
        // character instead of failing; neither occurs in Korean text.
        foreach (var c in cp949)
        {
            if (c is >= '\u0080' and <= '\u009F' or >= '' and <= '')
            {
                throw NotUtf8OrCp949(fileName, null);
            }
        }

        return cp949;
    }

    private static ImportFileEncodingException NotUtf8OrCp949(string fileName, Exception? innerException)
        => new($"{fileName}을(를) UTF-8로도 CP949(한국어 Windows 기본 인코딩)로도 읽을 수 없어 가져오지 않았습니다. "
            + "파일을 UTF-8로 다시 저장해 가져오세요.", innerException);

    private static string DecodeWithBom(Encoding encoding, byte[] bytes, int bomLength, string fileName, string name)
    {
        try
        {
            return encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ImportFileEncodingException(
                $"{fileName}은(는) {name} 표시(BOM)가 있지만 {name}로 읽을 수 없는 내용이 있어 가져오지 않았습니다. "
                + "파일을 UTF-8로 다시 저장해 가져오세요.", ex);
        }
    }
}
