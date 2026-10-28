using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// A plugin whose strings are partly UTF-8 and partly another encoding, read string by string: each is decoded as
/// UTF-8 when it is valid UTF-8 and with the other encoding otherwise; strings are written as UTF-8.
/// <list type="bullet">
/// <item>Windows-1252: an English mod edited in tools that saved some records each way ("Café" in UTF-8 beside a "’"
/// byte 0x92 in Windows-1252). Read whole either way, one half became mojibake, so such a file stopped with an encoding
/// error and had to be saved once in xTranslator first.</item>
/// <item>CP949: an older Korean release (only ever read, as an earlier translation). Read whole as CP949, its UTF-8 lines
/// ("강철") became other Hangul ("媛뺤쿋") without an error. Most CP949 Korean is not valid UTF-8, but 217 of the 2,350
/// common syllables are (lead byte C2–C8, trail A1–BF): a line of only those and ASCII reads as UTF-8 ("천" as "õ",
/// "화환" as "ȭȯ"). Such lines are counted by their bytes while a release is read (<see cref="CountReadAsUtf8Too"/>) and
/// told (PluginReader.Cp949AmbiguousDiagnostic) rather than read as CP949, which would turn a UTF-8 "Café" left in the
/// release into Hangul.</item>
/// </list>
/// Its code page is its own, so an export always re-encodes every string (into the output encoding) instead of keeping
/// the mixed bytes.
/// </summary>
internal sealed class Utf8OrFallbackEncoding : Encoding
{
    internal const string Windows1252Name = "utf-8+windows-1252";
    internal const string Cp949Name = "utf-8+ks_c_5601-1987";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly string _name;
    private readonly int _codePage;
    private readonly Encoding _fallback;
    private readonly Encoding? _strictCp949;

    private Utf8OrFallbackEncoding(string name, int codePage, Encoding fallback, Encoding? strictCp949 = null)
        : base(0, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        => (_name, _codePage, _fallback, _strictCp949) = (name, codePage, fallback, strictCp949);

    // Not registered code pages: they only have to differ from UTF-8's, Windows-1252's and CP949's.
    internal static Utf8OrFallbackEncoding OrWindows1252(Encoding windows1252) => new(Windows1252Name, 0x7F12_52FD, windows1252);

    internal static Utf8OrFallbackEncoding OrCp949(Encoding cp949)
    {
        var strict = (Encoding)cp949.Clone();
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        return new(Cp949Name, 0x7F09_49FD, cp949, strict);
    }

    // While a release is read: the strings valid as UTF-8 and read so that CP949 reads as Hangul, by their bytes (a string
    // is decoded more than once: its length, then its text).
    private static readonly AsyncLocal<ConcurrentDictionary<string, byte>?> ReadAsUtf8Too = new();

    /// <summary>Counts, into <paramref name="seen"/>, the CP949 strings read as UTF-8 until the result is disposed.</summary>
    internal static IDisposable CountReadAsUtf8Too(ConcurrentDictionary<string, byte> seen)
    {
        var previous = ReadAsUtf8Too.Value;
        ReadAsUtf8Too.Value = seen;
        return new Restore(() => ReadAsUtf8Too.Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    public override int CodePage => _codePage;
    public override string WebName => _name;
    public override string EncodingName => _name;
    public override string BodyName => _name;
    public override string HeaderName => _name;

    /// <summary>Whether these bytes are read with the other encoding (they are not valid UTF-8).</summary>
    internal static bool IsFallback(ReadOnlySpan<byte> bytes) => !System.Text.Unicode.Utf8.IsValid(bytes);

    private Encoding For(ReadOnlySpan<byte> bytes)
    {
        if (IsFallback(bytes))
        {
            return _fallback;
        }

        if (_strictCp949 != null && ReadAsUtf8Too.Value is { } seen && LosesHangulAsUtf8(bytes))
        {
            seen.TryAdd(System.Convert.ToHexString(bytes), 0);
        }

        return Utf8;
    }

    // Read as UTF-8 with no Hangul, though CP949 reads Hangul in it ("\u00F5" for "\uCC9C"); a UTF-8 "\uAC15\uCCA0" is read right.
    private bool LosesHangulAsUtf8(ReadOnlySpan<byte> bytes)
    {
        static bool HasHangul(string text) => text.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');
        if (!bytes.ContainsAnyInRange((byte)0x80, (byte)0xFF) || HasHangul(Utf8.GetString(bytes)))
        {
            return false;
        }

        try
        {
            return HasHangul(_strictCp949!.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

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

    // A string is judged whole: a decoder fed it in pieces would judge each piece ("é" split in two read as "Ã©").
    public override Decoder GetDecoder()
        => throw new NotSupportedException("문자열마다 판별하는 인코딩은 나눠 읽을 수 없습니다.");
    public override Encoder GetEncoder() => Utf8.GetEncoder();
}
