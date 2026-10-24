using System;
using System.Text;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// A plugin whose strings are partly UTF-8 and partly Windows-1252: an English mod edited in tools that saved some
/// records each way ("Café" in UTF-8 beside a "’" byte 0x92 in Windows-1252). Read whole either way, one half
/// became mojibake, so such a file stopped with an encoding error and had to be saved once in xTranslator first.
/// Each string is decoded as UTF-8 when it is valid UTF-8 and as Windows-1252 otherwise; strings are written as
/// UTF-8. Its code page is its own, so an export always re-encodes every string (into the output encoding) instead
/// of keeping the mixed bytes.
/// </summary>
internal sealed class Utf8OrWindows1252Encoding : Encoding
{
    internal const string Name = "utf-8+windows-1252";

    // Not a registered code page: it only has to differ from UTF-8's and Windows-1252's.
    private const int OwnCodePage = 0x7F12_52FD;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly Encoding _windows1252;

    internal Utf8OrWindows1252Encoding(Encoding windows1252)
        : base(0, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        => _windows1252 = windows1252;

    public override int CodePage => OwnCodePage;
    public override string WebName => Name;
    public override string EncodingName => Name;
    public override string BodyName => Name;
    public override string HeaderName => Name;

    /// <summary>Whether these bytes are read as Windows-1252 (they are not valid UTF-8).</summary>
    internal static bool IsWindows1252(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = Utf8.GetCharCount(bytes);
            return false;
        }
        catch (DecoderFallbackException)
        {
            return true;
        }
    }

    private Encoding For(ReadOnlySpan<byte> bytes) => IsWindows1252(bytes) ? _windows1252 : Utf8;

    public override int GetByteCount(char[] chars, int index, int count) => Utf8.GetByteCount(chars, index, count);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        => Utf8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
    public override int GetCharCount(byte[] bytes, int index, int count)
        => For(bytes.AsSpan(index, count)).GetCharCount(bytes, index, count);
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        => For(bytes.AsSpan(byteIndex, byteCount)).GetChars(bytes, byteIndex, byteCount, chars, charIndex);
    public override string GetString(byte[] bytes, int index, int count)
        => For(bytes.AsSpan(index, count)).GetString(bytes, index, count);
    public override int GetMaxByteCount(int charCount) => Utf8.GetMaxByteCount(charCount);
    public override int GetMaxCharCount(int byteCount) => Utf8.GetMaxCharCount(byteCount);
}
