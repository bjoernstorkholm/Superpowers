using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace Applications;

public sealed record WordRun(string Text, bool Bold, bool Italic);
public abstract record WordBlock;
public sealed record WordParagraph(WordRun[] Runs) : WordBlock;
public sealed record WordTable(WordParagraph[][][] Rows) : WordBlock;
public sealed record Sheet(string Name, int FirstRow, int FirstColumn, string[][] Cells, bool Truncated);
public sealed record Preview(string? Text = null, WordBlock[]? Word = null, Sheet[]? Sheets = null);

public static class PreviewReader
{
    private static readonly XNamespace WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static Preview Read(Library library, Entry entry)
    {
        using FileStream file = library.Open(entry.Path);

        return Path.GetExtension(entry.Name).ToLowerInvariant() switch
        {
            ".txt" => new Preview(Text: ReadText(file)),
            ".docx" => new Preview(Word: ReadWord(file)),
            ".xlsx" => new Preview(Sheets: ReadWorkbook(file)),
            _ => throw new NotSupportedException()
        };
    }

    private static string ReadText(Stream file)
    {
        using StreamReader reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        char[] buffer = new char[200_000];
        int count = reader.ReadBlock(buffer);

        return new string(buffer, 0, count) + (reader.Peek() == -1 ? "" : "\n\n[Preview truncated]");
    }

    private static WordBlock[] ReadWord(Stream file)
    {
        using ZipArchive archive = new ZipArchive(file);
        ZipArchiveEntry document = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException();
        using Stream stream = document.Open();
        XElement body = XDocument.Load(stream).Root?.Element(WordNamespace + "body")
            ?? throw new InvalidDataException();

        return body.Elements().Select<XElement, WordBlock?>(element => element.Name.LocalName switch
        {
            "p" => ReadParagraph(element),
            "tbl" => new WordTable(element.Elements(WordNamespace + "tr")
                .Select(row => row.Elements(WordNamespace + "tc")
                    .Select(cell => cell.Elements(WordNamespace + "p").Select(ReadParagraph).ToArray())
                    .ToArray())
                .ToArray()),
            _ => null
        }).OfType<WordBlock>().ToArray();
    }

    private static WordParagraph ReadParagraph(XElement paragraph) =>
        new(paragraph.Descendants(WordNamespace + "r").Select(run =>
        {
            string text = string.Concat(run.Elements().Select(part => part.Name.LocalName switch
            {
                "t" => part.Value,
                "tab" => "\t",
                "br" or "cr" => "\n",
                _ => ""
            }));
            XElement? properties = run.Element(WordNamespace + "rPr");

            return new WordRun(text, IsEnabled(properties?.Element(WordNamespace + "b")),
                IsEnabled(properties?.Element(WordNamespace + "i")));
        }).ToArray());

    private static bool IsEnabled(XElement? element) =>
        element is not null && (string?)element.Attribute(WordNamespace + "val") is not ("0" or "false" or "off");

    private static Sheet[] ReadWorkbook(Stream file)
    {
        using XLWorkbook workbook = new XLWorkbook(file);

        return workbook.Worksheets.Select(sheet =>
        {
            IXLRange? range = sheet.RangeUsed();
            if (range is null)
                return new Sheet(sheet.Name, 1, 1, [], false);

            IXLAddress first = range.FirstCell().Address;
            int height = Math.Min(range.RowCount(), 200);
            int width = Math.Min(range.ColumnCount(), 40);
            string[][] rows = Enumerable.Range(first.RowNumber, height)
                .Select(row => Enumerable.Range(first.ColumnNumber, width)
                    .Select(column => FormatCell(sheet.Cell(row, column))).ToArray())
                .ToArray();

            return new Sheet(sheet.Name, first.RowNumber, first.ColumnNumber, rows,
                height < range.RowCount() || width < range.ColumnCount());
        }).ToArray();
    }

    private static string FormatCell(IXLCell cell) =>
        cell.HasFormula ? cell.CachedValue.ToString() : cell.GetFormattedString();
}