using System.Text;
using Applications;

namespace ApplicationLog.Tests;

public sealed class TextSaveTests
{
    [Fact]
    public void SavedSnapshotSupportsSubsequentEditsWithoutLosingBomOrCrLf()
    {
        using TestDirectory directory = new TestDirectory();
        string path = directory.CreateText("log.TXT", "");
        UnicodeEncoding encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        File.WriteAllText(path, "First\r\nSecond", encoding);
        Library library = new Library(directory.Path);
        TextDocument original = Assert.IsType<TextDocument>(library.ReadText("log.TXT"));

        TextDocument firstSave = Assert.IsType<TextDocument>(library.SaveText("log.TXT", original, "Edited\nSecond"));
        TextDocument secondSave = Assert.IsType<TextDocument>(library.SaveText("log.TXT", firstSave, "Edited\nFinal"));

        byte[] expected = [.. encoding.GetPreamble(), .. encoding.GetBytes("Edited\r\nFinal")];
        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Equal(expected, secondSave.Version);
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void AZeroByteFileCanReceiveItsFirstNoteAndBeClearedAgain()
    {
        using TestDirectory directory = new TestDirectory();
        string path = directory.CreateText("log.txt", "");
        Library library = new Library(directory.Path);
        TextDocument empty = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        TextDocument written = Assert.IsType<TextDocument>(library.SaveText("log.txt", empty, "First note"));
        Assert.Equal("First note", File.ReadAllText(path));
        Assert.NotNull(library.SaveText("log.txt", written, ""));

        Assert.Empty(File.ReadAllBytes(path));
        Assert.Empty(Assert.IsType<TextDocument>(library.ReadText("log.txt")).Text);
    }

    [Fact]
    public void SameLengthExternalEditWithMatchingTimestampIsStillDetected()
    {
        using TestDirectory directory = new TestDirectory();
        string path = directory.CreateText("log.txt", "first");
        Library library = new Library(directory.Path);
        TextDocument original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));
        DateTime modified = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "other");
        File.SetLastWriteTimeUtc(path, modified);

        TextDocument? result = library.SaveText("log.txt", original, "draft");

        Assert.Null(result);
        Assert.Equal("other", File.ReadAllText(path));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void AnExternallyEnlargedFileIsPreserved()
    {
        using TestDirectory directory = new TestDirectory();
        string path = directory.CreateText("log.txt", "original");
        Library library = new Library(directory.Path);
        TextDocument original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));
        string replacement = new string('x', TextDocument.MaxCharacters * 4 + 5);
        File.WriteAllText(path, replacement);

        Assert.Null(library.SaveText("log.txt", original, "draft"));

        Assert.Equal(replacement, File.ReadAllText(path));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void SaveDoesNotRecreateADeletedFile()
    {
        using TestDirectory directory = new TestDirectory();
        string path = directory.CreateText("log.txt", "original");
        Library library = new Library(directory.Path);
        TextDocument original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));
        File.Delete(path);

        Assert.Throws<FileNotFoundException>(() => library.SaveText("log.txt", original, "draft"));

        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void OversizedDraftLeavesOriginalFileUntouched()
    {
        using TestDirectory directory = new TestDirectory();
        string path = directory.CreateText("log.txt", "original");
        Library library = new Library(directory.Path);
        TextDocument original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        Assert.Throws<ArgumentException>(() =>
            library.SaveText("log.txt", original, new string('x', TextDocument.MaxCharacters + 1)));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void SaveRefusesNonTextExtensionsEvenWithAnOtherwiseValidSnapshot()
    {
        using TestDirectory directory = new TestDirectory();
        directory.CreateText("log.txt", "original");
        string path = directory.CreateText("log.csv", "original");
        Library library = new Library(directory.Path);
        TextDocument original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        Assert.Throws<ArgumentException>(() => library.ReadText("log.csv"));
        Assert.Throws<ArgumentException>(() => library.SaveText("log.csv", original, "draft"));

        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public async Task ConcurrentSavesFromTheSameSnapshotHaveExactlyOneWinner()
    {
        using TestDirectory directory = new TestDirectory();
        string path = directory.CreateText("log.txt", "original");
        Library library = new Library(directory.Path);
        TextDocument original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        TextDocument?[] results = await Task.WhenAll(
            Task.Run(() => library.SaveText("log.txt", original, "draft one")),
            Task.Run(() => library.SaveText("log.txt", original, "draft two")));

        TextDocument winner = Assert.IsType<TextDocument>(Assert.Single(results, result => result is not null));
        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(winner.Version, saved);
        Assert.Contains(Encoding.UTF8.GetString(saved), new[] { "draft one", "draft two" });
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }
}