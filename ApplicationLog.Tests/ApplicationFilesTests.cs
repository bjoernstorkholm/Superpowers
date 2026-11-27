using System.IO.Compression;
using Applications;
using ClosedXML.Excel;

namespace ApplicationLog.Tests;

public sealed class ApplicationFilesTests
{
    [Fact]
    public void ListSortsFoldersFirstAndReportsPathsRelativeToRoot()
    {
        using TestDirectory fixtures = new();
        fixtures.CreateText("Zebra/notes.txt", "");
        fixtures.CreateFolder("alpha");
        fixtures.CreateText("B.pdf", "");
        fixtures.CreateText("a.txt", "");
        ApplicationFiles files = new(fixtures.Path);

        FileEntry[] entries = files.List("");

        Assert.Equal(
            new[] { ("alpha", "alpha", true), ("Zebra", "Zebra", true), ("a.txt", "a.txt", false), ("B.pdf", "B.pdf", false) },
            entries.Select(entry => (entry.Name, entry.RelativePath, entry.IsFolder)));
        FileEntry nested = Assert.Single(files.List("Zebra"));
        Assert.Equal(("notes.txt", Path.Combine("Zebra", "notes.txt"), false),
            (nested.Name, nested.RelativePath, nested.IsFolder));
    }

    [Fact]
    public void ListCanSortByModificationTimeAndKeepsTheLogFirst()
    {
        using TestDirectory fixtures = new();
        string alpha = fixtures.CreateFolder("Alpha");
        string zebra = fixtures.CreateFolder("Zebra");
        string oldDocument = fixtures.CreateText("a.txt", "");
        string newDocument = fixtures.CreateText("z.txt", "");
        string log = fixtures.CreateText("LOG.TXT", "");
        DateTime old = new(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        DateTime recent = old.AddDays(1);
        Directory.SetLastWriteTimeUtc(alpha, old);
        Directory.SetLastWriteTimeUtc(zebra, recent);
        File.SetLastWriteTimeUtc(oldDocument, old);
        File.SetLastWriteTimeUtc(newDocument, recent);
        File.SetLastWriteTimeUtc(log, old.AddDays(-1));
        ApplicationFiles files = new(fixtures.Path);

        Assert.Equal(new[] { "LOG.TXT", "Alpha", "Zebra", "a.txt", "z.txt" },
            files.List("").Select(entry => entry.Name));
        Assert.Equal(new[] { "LOG.TXT", "Zebra", "Alpha", "z.txt", "a.txt" },
            files.List("", sortByDate: true).Select(entry => entry.Name));
    }

    [Fact]
    public void CountFoldersIncludesNestedFoldersAndDescribeReturnsLastModified()
    {
        using TestDirectory fixtures = new();
        string path = fixtures.CreateText("Alpha/Nested/Notes.txt", "");
        fixtures.CreateFolder("Beta");
        DateTime modified = new(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, modified);
        ApplicationFiles files = new(fixtures.Path);

        Assert.Equal(3, files.CountFolders());
        FileEntry entry = files.Describe("Alpha/Nested/Notes.txt");
        Assert.Equal(("Notes.txt", Path.Combine("Alpha", "Nested", "Notes.txt"), false),
            (entry.Name, entry.RelativePath, entry.IsFolder));
        Assert.Equal(modified, entry.Modified.ToUniversalTime());
        Assert.Throws<FileNotFoundException>(() => files.Describe("Alpha/../Notes.txt"));
    }

    [Fact]
    public void ResolveFileReturnsAnExistingNestedFile()
    {
        using TestDirectory fixtures = new();
        string file = fixtures.CreateText("Alpha/notes.txt", "Test notes");
        ApplicationFiles files = new(fixtures.Path);

        Assert.Equal(Path.GetFullPath(file), files.ResolveFile(Path.Combine("Alpha", "notes.txt")));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("Alpha/../notes.txt")]
    [InlineData("nested:name.txt")]
    public void ResolveFileRejectsTraversalAndInvalidSegments(string relativePath)
    {
        using TestDirectory fixtures = new();
        fixtures.CreateFolder("Alpha");
        fixtures.CreateText("notes.txt", "");
        ApplicationFiles files = new(fixtures.Path);

        Assert.Throws<FileNotFoundException>(() => files.ResolveFile(relativePath));
    }

    [Fact]
    public void ResolveFileRejectsDirectories()
    {
        using TestDirectory fixtures = new();
        fixtures.CreateFolder("Alpha");
        ApplicationFiles files = new(fixtures.Path);

        Assert.Throws<FileNotFoundException>(() => files.ResolveFile("Alpha"));
    }

    [Theory]
    [InlineData("letter.PDF", "application/pdf", true)]
    [InlineData("photo.JPEG", "image/jpeg", true)]
    [InlineData("notes.LOG", "text/plain; charset=utf-8", true)]
    [InlineData("resume.DOCX", null, true)]
    [InlineData("program.exe", null, false)]
    public void PreviewAndDesktopOpeningUseFileExtension(string file, string? previewType, bool canOpen)
    {
        Assert.Equal(previewType, ApplicationFiles.PreviewType(file));
        Assert.Equal(canOpen, ApplicationFiles.CanOpenOnDesktop(file));
    }

    [Fact]
    public void ReadOfficePreviewUsesTheSharedReaderForWordAndExcel()
    {
        using TestDirectory fixtures = new();
        string nested = fixtures.CreateFolder("Nested");
        using (ZipArchive archive = ZipFile.Open(Path.Combine(nested, "Resume.DOCX"), ZipArchiveMode.Create))
        using (StreamWriter document = new(archive.CreateEntry("word/document.xml").Open()))
        {
            document.Write("""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                    <w:body><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Résumé</w:t></w:r></w:p></w:body>
                </w:document>
                """);
        }

        using (XLWorkbook workbook = new())
        {
            workbook.AddWorksheet("Applications").Cell("C4").Value = "Interview";
            workbook.SaveAs(Path.Combine(nested, "Tracker.XLSX"));
        }

        ApplicationFiles files = new(fixtures.Path);
        WordParagraph paragraph = Assert.IsType<WordParagraph>(
            Assert.Single(files.ReadOfficePreview(Path.Combine("Nested", "Resume.DOCX")).Word!));
        Assert.Equal(new WordRun("Résumé", Bold: true, Italic: false), Assert.Single(paragraph.Runs));
        Sheet sheet = Assert.Single(files.ReadOfficePreview(Path.Combine("Nested", "Tracker.XLSX")).Sheets!);
        Assert.Equal((4, 3, "Interview"), (sheet.FirstRow, sheet.FirstColumn, sheet.Cells[0][0]));
        Assert.Throws<FileNotFoundException>(() => files.ReadOfficePreview("Nested/../Resume.DOCX"));
        Assert.Throws<NotSupportedException>(() => files.ReadOfficePreview("Legacy.doc"));
    }
}