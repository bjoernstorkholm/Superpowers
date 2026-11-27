using System.IO.Compression;
using System.Text;
using System.Xml;
using Applications;
using ClosedXML.Excel;

namespace ApplicationLog.Tests;

public sealed class PreviewReaderTests : IDisposable
{
    private readonly TestDirectory _files = new();
    private const string WordDocumentStart = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
        """;
    private const string WordDocumentEnd = "</w:body></w:document>";

    [Fact]
    public void Read_TextPreviewRecognizesUppercaseExtensionAndPreservesLineEndings()
    {
        const string text = "Første linje\r\n第二行\n<last>";
        _files.CreateText("notes.TXT", text);

        Preview preview = Read("notes.TXT");

        Assert.Equal(text, preview.Text);
        Assert.Null(preview.Word);
        Assert.Null(preview.Sheets);
    }

    [Theory]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void Read_TextPreviewDetectsUnicodeByteOrderMarks(int codePage)
    {
        const string text = "Æøå, 日本語, 🌱";
        Encoding encoding = Encoding.GetEncoding(codePage);
        string path = _files.CreateText("notes.txt", "");
        File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);

        Assert.Equal(text, Read("notes.txt").Text);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(200_000, false)]
    [InlineData(200_001, true)]
    public void Read_TextPreviewOnlyReportsTruncationBeyondTheLimit(int length, bool truncated)
    {
        string text = new string('å', length);
        _files.CreateText("notes.txt", text);
        string expected = truncated ? text[..200_000] + "\n\n[Preview truncated]" : text;

        Assert.Equal(expected, Read("notes.txt").Text);
    }

    [Fact]
    public void Read_WordPreviewPreservesParagraphOrderAndInlineFormatting()
    {
        WriteWordDocument("""
            <w:p>
              <w:r><w:rPr><w:b/><w:i w:val="1"/></w:rPr><w:t>Lead &amp; Co.</w:t><w:tab/><w:t>Next</w:t><w:br/><w:t>Line</w:t></w:r>
              <w:r><w:rPr><w:b w:val="0"/><w:i w:val="false"/></w:rPr><w:cr/><w:t>Normal</w:t></w:r>
            </w:p>
            <w:sectPr/>
            <w:p>
              <w:hyperlink><w:r><w:rPr><w:b w:val="off"/><w:i/></w:rPr><w:t>Linked text</w:t></w:r></w:hyperlink>
            </w:p>
            <w:p/>
            """);

        Preview preview = Read("document.docx");

        Assert.Null(preview.Text);
        Assert.Null(preview.Sheets);
        Assert.Collection(preview.Word!,
            block => Assert.Equal(
                [new WordRun("Lead & Co.\tNext\nLine", true, true), new WordRun("\nNormal", false, false)],
                Assert.IsType<WordParagraph>(block).Runs),
            block => Assert.Equal(new WordRun("Linked text", false, true), Assert.Single(Assert.IsType<WordParagraph>(block).Runs)),
            block => Assert.Empty(Assert.IsType<WordParagraph>(block).Runs));
    }

    [Fact]
    public void Read_WordPreviewKeepsTableRowsCellsAndCellParagraphsInOrder()
    {
        WriteWordDocument("""
            <w:p><w:r><w:t>Introduction</w:t></w:r></w:p>
            <w:tbl>
              <w:tr>
                <w:tc>
                  <w:p><w:r><w:t>First</w:t></w:r></w:p>
                  <w:p><w:r><w:t>Continuation</w:t></w:r></w:p>
                </w:tc>
                <w:tc><w:p><w:r><w:t>Second cell</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr><w:tc><w:p><w:r><w:t>Second row</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><w:t>Conclusion</w:t></w:r></w:p>
            """);

        Assert.Collection(Read("document.docx").Word!,
            block => Assert.Equal("Introduction", ParagraphText(Assert.IsType<WordParagraph>(block))),
            block => Assert.Collection(Assert.IsType<WordTable>(block).Rows,
                row => Assert.Collection(row,
                    paragraphs => Assert.Equal(["First", "Continuation"], paragraphs.Select(ParagraphText)),
                    paragraphs => Assert.Equal("Second cell", ParagraphText(Assert.Single(paragraphs)))),
                row => Assert.Equal("Second row", ParagraphText(Assert.Single(Assert.Single(row))))),
            block => Assert.Equal("Conclusion", ParagraphText(Assert.IsType<WordParagraph>(block))));
    }

    [Fact]
    public void Read_WordPreviewRejectsNonZipInput()
    {
        _files.CreateText("document.docx", "This is plain text.");

        Assert.Throws<InvalidDataException>(() => Read("document.docx"));
    }

    [Theory]
    [InlineData("word/other.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>")]
    [InlineData("word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>")]
    public void Read_WordPreviewRejectsMissingDocumentPartOrBody(string part, string xml)
    {
        WriteWordPart(part, xml);

        Assert.Throws<InvalidDataException>(() => Read("document.docx"));
    }

    [Fact]
    public void Read_WordPreviewReportsMalformedXml()
    {
        WriteWordPart("word/document.xml", "<broken>");

        Assert.Throws<XmlException>(() => Read("document.docx"));
    }

    [Fact]
    public void Read_WorkbookPreviewPreservesSheetOrderAndSparseCoordinates()
    {
        using XLWorkbook workbook = new XLWorkbook();
        IXLWorksheet summary = workbook.AddWorksheet("Summary");
        summary.Cell("C4").Value = "Start";
        summary.Cell("E6").Value = "End";
        workbook.AddWorksheet("Empty");
        workbook.AddWorksheet("Other").Cell("B3").Value = "Other data";
        SaveWorkbook(workbook);

        Preview preview = Read("workbook.xlsx");

        Assert.Null(preview.Text);
        Assert.Null(preview.Word);
        Assert.Collection(preview.Sheets!,
            sheet =>
            {
                Assert.Equal("Summary", sheet.Name);
                Assert.Equal((4, 3), (sheet.FirstRow, sheet.FirstColumn));
                Assert.False(sheet.Truncated);
                Assert.Collection(sheet.Cells,
                    row => Assert.Equal(["Start", "", ""], row),
                    row => Assert.Equal(["", "", ""], row),
                    row => Assert.Equal(["", "", "End"], row));
            },
            sheet =>
            {
                Assert.Equal("Empty", sheet.Name);
                Assert.Equal((1, 1), (sheet.FirstRow, sheet.FirstColumn));
                Assert.Empty(sheet.Cells);
                Assert.False(sheet.Truncated);
            },
            sheet =>
            {
                Assert.Equal("Other", sheet.Name);
                Assert.Equal((3, 2), (sheet.FirstRow, sheet.FirstColumn));
                Assert.Equal("Other data", Assert.Single(Assert.Single(sheet.Cells)));
            });
    }

    [Fact]
    public void Read_WorkbookPreviewUsesCellNumberFormatAndSavedFormulaValue()
    {
        using XLWorkbook workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet("Numbers");
        sheet.Cell("D7").Value = 12;
        sheet.Cell("D7").Style.NumberFormat.Format = "0000";
        sheet.Cell("E7").FormulaA1 = "D7*2";
        SaveWorkbook(workbook, evaluateFormulas: true);

        Sheet previewSheet = Assert.Single(Read("workbook.xlsx").Sheets!);

        Assert.Equal(["0012", "24"], Assert.Single(previewSheet.Cells));
    }

    [Theory]
    [InlineData(200, 40, false)]
    [InlineData(201, 40, true)]
    [InlineData(200, 41, true)]
    public void Read_WorkbookPreviewBoundsEachDimensionFromTheFirstUsedCell(int rows, int columns, bool truncated)
    {
        using XLWorkbook workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet("Large");
        const int firstRow = 3;
        const int firstColumn = 5;
        sheet.Cell(firstRow, firstColumn).Value = "First";
        sheet.Cell(firstRow + 199, firstColumn + 39).Value = "Last visible";
        if (truncated)
            sheet.Cell(firstRow + rows - 1, firstColumn + columns - 1).Value = "Outside the preview";
        SaveWorkbook(workbook);

        Sheet previewSheet = Assert.Single(Read("workbook.xlsx").Sheets!);

        Assert.Equal((firstRow, firstColumn), (previewSheet.FirstRow, previewSheet.FirstColumn));
        Assert.Equal(truncated, previewSheet.Truncated);
        Assert.Equal(200, previewSheet.Cells.Length);
        Assert.All(previewSheet.Cells, row => Assert.Equal(40, row.Length));
        Assert.Equal("First", previewSheet.Cells[0][0]);
        Assert.Equal("Last visible", previewSheet.Cells[^1][^1]);
        Assert.DoesNotContain("Outside the preview", previewSheet.Cells.SelectMany(row => row));
    }

    [Theory]
    [InlineData("legacy.xls")]
    [InlineData("manual.pdf")]
    [InlineData("archive")]
    public void Read_RejectsFormatsHandledOutsideThePreviewReader(string fileName)
    {
        _files.CreateText(fileName, "content");

        Assert.Throws<NotSupportedException>(() => Read(fileName));
    }

    private Preview Read(string fileName)
    {
        Library library = new Library(_files.Path);

        return PreviewReader.Read(library, library.Describe(fileName));
    }

    private void WriteWordDocument(string body) =>
        WriteWordPart("word/document.xml", WordDocumentStart + body + WordDocumentEnd);

    private void WriteWordPart(string part, string xml)
    {
        using ZipArchive archive = ZipFile.Open(Path.Combine(_files.Path, "document.docx"), ZipArchiveMode.Create);
        using StreamWriter writer = new StreamWriter(archive.CreateEntry(part).Open());
        writer.Write(xml);
    }

    private void SaveWorkbook(XLWorkbook workbook, bool evaluateFormulas = false) =>
        workbook.SaveAs(Path.Combine(_files.Path, "workbook.xlsx"),
            new SaveOptions { EvaluateFormulasBeforeSaving = evaluateFormulas });

    private static string ParagraphText(WordParagraph paragraph) =>
        string.Concat(paragraph.Runs.Select(run => run.Text));

    public void Dispose() => _files.Dispose();
}