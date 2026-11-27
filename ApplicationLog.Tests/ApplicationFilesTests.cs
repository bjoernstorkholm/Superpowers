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

        Assert.Collection(entries,
            entry => Assert.Equal(new FileEntry("alpha", "alpha", true), entry),
            entry => Assert.Equal(new FileEntry("Zebra", "Zebra", true), entry),
            entry => Assert.Equal(new FileEntry("a.txt", "a.txt", false), entry),
            entry => Assert.Equal(new FileEntry("B.pdf", "B.pdf", false), entry));
        Assert.Equal(new FileEntry("notes.txt", Path.Combine("Zebra", "notes.txt"), false),
            Assert.Single(files.List("Zebra")));
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
}