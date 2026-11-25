using System.Text;

namespace Applications;

internal sealed record TextDocument(string Text, Encoding Encoding, byte[] Preamble, byte[] Version)
{
    public const int MaxCharacters = 200_000;
    private const int MaxBytes = MaxCharacters * 4 + 4;

    private static readonly Encoding[] Encodings =
    [
        new UTF32Encoding(false, true, true),
        new UTF32Encoding(true, true, true),
        new UTF8Encoding(true, true),
        new UnicodeEncoding(false, true, true),
        new UnicodeEncoding(true, true, true)
    ];

    public static TextDocument? Read(Stream stream)
    {
        var bytes = ReadBytes(stream);
        if (bytes is null)
            return null;

        var encoding = Encodings.FirstOrDefault(candidate => bytes.AsSpan().StartsWith(candidate.Preamble))
            ?? new UTF8Encoding(false, true);
        var preamble = encoding.GetPreamble();
        try
        {
            var text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
            return text.Length <= MaxCharacters && !text.Contains('\0')
                ? new TextDocument(text, encoding, preamble, bytes)
                : null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    public byte[] Encode(string draft)
    {
        var withoutCrLf = Text.Replace("\r\n", "", StringComparison.Ordinal);
        if (Text.Contains("\r\n", StringComparison.Ordinal) && !withoutCrLf.Contains('\n') && !withoutCrLf.Contains('\r'))
            draft = draft.Replace("\n", "\r\n", StringComparison.Ordinal);
        else if (Text.Contains('\r') && !Text.Contains('\n'))
            draft = draft.Replace("\n", "\r", StringComparison.Ordinal);

        var content = Encoding.GetBytes(draft);
        return Preamble.Length == 0 ? content : [.. Preamble, .. content];
    }

    public static byte[]? ReadBytes(Stream stream)
    {
        if (stream.Length > MaxBytes)
            return null;

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return stream.ReadByte() == -1 ? bytes : null;
    }
}
