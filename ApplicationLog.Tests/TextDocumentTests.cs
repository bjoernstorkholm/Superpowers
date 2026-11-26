using System.Text;
using Applications;

namespace ApplicationLog.Tests;

public sealed class TextDocumentTests
{
    [Theory]
    [InlineData(65001, false)]
    [InlineData(65001, true)]
    [InlineData(1200, true)]
    [InlineData(1201, true)]
    [InlineData(12000, true)]
    [InlineData(12001, true)]
    public void ReadRecognizesEncodingAndRetainsOriginalBytes(int codePage, bool withBom)
    {
        const string text = "Ærø — résumé 📝\r\nSecond line";
        Encoding encoding = Encoding.GetEncoding(codePage);
        byte[] bytes = [.. (withBom ? encoding.GetPreamble() : []), .. encoding.GetBytes(text)];
        using MemoryStream stream = new MemoryStream(bytes);

        TextDocument document = Assert.IsType<TextDocument>(TextDocument.Read(stream));

        Assert.Equal(text, document.Text);
        Assert.Equal(codePage, document.Encoding.CodePage);
        Assert.Equal(withBom ? encoding.GetPreamble() : [], document.Preamble);
        Assert.Equal(bytes, document.Version);
        Assert.Equal(bytes, document.Encode("Ærø — résumé 📝\nSecond line"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyFileCanBeOpenedAndEdited(bool withBom)
    {
        byte[] preamble = withBom ? new UTF8Encoding(true).GetPreamble() : [];
        using MemoryStream stream = new MemoryStream(preamble);

        TextDocument document = Assert.IsType<TextDocument>(TextDocument.Read(stream));

        Assert.Empty(document.Text);
        Assert.Equal(preamble, document.Encode(""));
        byte[] expected = [.. preamble, .. Encoding.UTF8.GetBytes("First note")];
        Assert.Equal(expected, document.Encode("First note"));
    }

    [Theory]
    [InlineData(new byte[] { 0xC3, 0x28 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0xC3 })]
    [InlineData(new byte[] { 0x41, 0x00, 0x42 })]
    public void InvalidOrBinaryTextIsNotEditable(byte[] bytes)
    {
        using MemoryStream stream = new MemoryStream(bytes);

        Assert.Null(TextDocument.Read(stream));
    }

    [Theory]
    [InlineData(TextDocument.MaxCharacters, true)]
    [InlineData(TextDocument.MaxCharacters + 1, false)]
    public void CharacterLimitAppliesAfterDecoding(int characterCount, bool editable)
    {
        string text = new string('ø', characterCount);
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        TextDocument? document = TextDocument.Read(stream);

        if (editable)
            Assert.Equal(text, Assert.IsType<TextDocument>(document).Text);
        else
            Assert.Null(document);
    }

    [Fact]
    public void OversizedStreamIsRejectedBeforeItsContentsAreRead()
    {
        using MemoryStream stream = new MemoryStream(new byte[TextDocument.MaxCharacters * 4 + 5]);

        Assert.Null(TextDocument.Read(stream));
        Assert.Equal(0, stream.Position);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void EncodePreservesUniformLineEndings(string lineEnding)
    {
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes($"First{lineEnding}Second"));
        TextDocument document = Assert.IsType<TextDocument>(TextDocument.Read(stream));

        string savedText = Encoding.UTF8.GetString(document.Encode("Edited\nLast\n"));

        Assert.Equal($"Edited{lineEnding}Last{lineEnding}", savedText);
    }
}