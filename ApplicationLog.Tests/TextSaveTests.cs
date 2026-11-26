using System.Text;
using Applications;

namespace ApplicationLog.Tests;

public sealed class TextSaveTests
{
    [Fact]
    public void SavedSnapshotSupportsSubsequentEditsWithoutLosingBomOrCrLf()
    {
        using var directory = new TestDirectory();
        var path = directory.CreateText("log.TXT", "");
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        File.WriteAllText(path, "First\r\nSecond", encoding);
        var library = new Library(directory.Path);
        var original = Assert.IsType<TextDocument>(library.ReadText("log.TXT"));

        var firstSave = Assert.IsType<TextDocument>(library.SaveText("log.TXT", original, "Edited\nSecond"));
        var secondSave = Assert.IsType<TextDocument>(library.SaveText("log.TXT", firstSave, "Edited\nFinal"));

        byte[] expected = [.. encoding.GetPreamble(), .. encoding.GetBytes("Edited\r\nFinal")];
        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Equal(expected, secondSave.Version);
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void AZeroByteFileCanReceiveItsFirstNoteAndBeClearedAgain()
    {
        using var directory = new TestDirectory();
        var path = directory.CreateText("log.txt", "");
        var library = new Library(directory.Path);
        var empty = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        var written = Assert.IsType<TextDocument>(library.SaveText("log.txt", empty, "First note"));
        Assert.Equal("First note", File.ReadAllText(path));
        Assert.NotNull(library.SaveText("log.txt", written, ""));

        Assert.Empty(File.ReadAllBytes(path));
        Assert.Empty(Assert.IsType<TextDocument>(library.ReadText("log.txt")).Text);
    }

    [Fact]
    public void SameLengthExternalEditWithMatchingTimestampIsStillDetected()
    {
        using var directory = new TestDirectory();
        var path = directory.CreateText("log.txt", "first");
        var library = new Library(directory.Path);
        var original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));
        var modified = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "other");
        File.SetLastWriteTimeUtc(path, modified);

        var result = library.SaveText("log.txt", original, "draft");

        Assert.Null(result);
        Assert.Equal("other", File.ReadAllText(path));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void AnExternallyEnlargedFileIsPreserved()
    {
        using var directory = new TestDirectory();
        var path = directory.CreateText("log.txt", "original");
        var library = new Library(directory.Path);
        var original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));
        var replacement = new string('x', TextDocument.MaxCharacters * 4 + 5);
        File.WriteAllText(path, replacement);

        Assert.Null(library.SaveText("log.txt", original, "draft"));

        Assert.Equal(replacement, File.ReadAllText(path));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void SaveDoesNotRecreateADeletedFile()
    {
        using var directory = new TestDirectory();
        var path = directory.CreateText("log.txt", "original");
        var library = new Library(directory.Path);
        var original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));
        File.Delete(path);

        Assert.Throws<FileNotFoundException>(() => library.SaveText("log.txt", original, "draft"));

        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void OversizedDraftLeavesOriginalFileUntouched()
    {
        using var directory = new TestDirectory();
        var path = directory.CreateText("log.txt", "original");
        var library = new Library(directory.Path);
        var original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        Assert.Throws<ArgumentException>(() =>
            library.SaveText("log.txt", original, new string('x', TextDocument.MaxCharacters + 1)));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }

    [Fact]
    public void SaveRefusesNonTextExtensionsEvenWithAnOtherwiseValidSnapshot()
    {
        using var directory = new TestDirectory();
        directory.CreateText("log.txt", "original");
        var path = directory.CreateText("log.csv", "original");
        var library = new Library(directory.Path);
        var original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        Assert.Throws<ArgumentException>(() => library.ReadText("log.csv"));
        Assert.Throws<ArgumentException>(() => library.SaveText("log.csv", original, "draft"));

        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public async Task ConcurrentSavesFromTheSameSnapshotHaveExactlyOneWinner()
    {
        using var directory = new TestDirectory();
        var path = directory.CreateText("log.txt", "original");
        var library = new Library(directory.Path);
        var original = Assert.IsType<TextDocument>(library.ReadText("log.txt"));

        var results = await Task.WhenAll(
            Task.Run(() => library.SaveText("log.txt", original, "draft one")),
            Task.Run(() => library.SaveText("log.txt", original, "draft two")));

        var winner = Assert.IsType<TextDocument>(Assert.Single(results, result => result is not null));
        var saved = File.ReadAllBytes(path);
        Assert.Equal(winner.Version, saved);
        Assert.Contains(Encoding.UTF8.GetString(saved), new[] { "draft one", "draft two" });
        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(directory.Path)));
    }
}
