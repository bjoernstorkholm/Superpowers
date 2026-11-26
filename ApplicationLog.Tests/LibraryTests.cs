using Applications;

namespace ApplicationLog.Tests;

public sealed class LibraryTests
{
    private static readonly DateTime Earlier = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Earlier.AddDays(1);

    [Theory]
    [InlineData(TreeSortOrder.Name)]
    [InlineData(TreeSortOrder.DateModified)]
    public void SortEntries_PinsLogRegardlessOfCaseAndTimestamp(TreeSortOrder order)
    {
        Entry[] entries =
        [
            new("Applications", "Applications", true, Later),
            new("alpha.txt", "alpha.txt", false, Later),
            new("LOG.TXT", "LOG.TXT", false, Earlier)
        ];

        Assert.Equal(new[] { "LOG.TXT", "Applications", "alpha.txt" },
            Library.SortEntries(entries, order).Select(entry => entry.Name));
    }

    [Fact]
    public void SortEntries_OnlyPinsFilesNamedLog()
    {
        Entry[] entries =
        [
            new("log.txt", "log.txt", true, Later),
            new("log.txt.bak", "log.txt.bak", false, Later),
            new("beta.txt", "beta.txt", false, Earlier),
            new("Applications", "Applications", true, Earlier)
        ];

        Assert.Equal(new[] { "Applications", "log.txt", "beta.txt", "log.txt.bak" },
            Library.SortEntries(entries, TreeSortOrder.Name).Select(entry => entry.Name));
    }

    [Fact]
    public void SortEntries_ByNameGroupsFoldersAndIgnoresCase()
    {
        Entry[] entries =
        [
            new("zulu.txt", "zulu.txt", false, Later),
            new("Zulu", "Zulu", true, Later),
            new("ALPHA.txt", "ALPHA.txt", false, Earlier),
            new("alpha", "alpha", true, Earlier)
        ];

        Assert.Equal(new[] { "alpha", "Zulu", "ALPHA.txt", "zulu.txt" },
            Library.SortEntries(entries, TreeSortOrder.Name).Select(entry => entry.Name));
    }

    [Fact]
    public void SortEntries_ByDateGroupsFoldersAndUsesNamesToBreakTies()
    {
        Entry[] entries =
        [
            new("zulu.txt", "zulu.txt", false, Later),
            new("alpha.txt", "alpha.txt", false, Later),
            new("A Old", "A Old", true, Earlier.AddDays(-1)),
            new("Z New", "Z New", true, Earlier),
            new("a old.txt", "a old.txt", false, Earlier)
        ];

        Assert.Equal(new[] { "Z New", "A Old", "alpha.txt", "zulu.txt", "a old.txt" },
            Library.SortEntries(entries, TreeSortOrder.DateModified).Select(entry => entry.Name));
    }

    [Fact]
    public void List_UsesRequestedSortAndReturnsOnlyDirectChildrenWithRootRelativePaths()
    {
        using var directory = new TestDirectory();
        directory.CreateFolder(Path.Combine("Client", "Interview"));
        directory.CreateText(Path.Combine("Client", "Interview", "followup.txt"), "follow up");
        var older = directory.CreateText(Path.Combine("Client", "alpha.txt"), "first");
        var newer = directory.CreateText(Path.Combine("Client", "zulu.txt"), "second");
        File.SetLastWriteTimeUtc(older, Earlier);
        File.SetLastWriteTimeUtc(newer, Later);
        var library = new Library(directory.Path);

        var entries = library.List("Client", TreeSortOrder.DateModified);

        Assert.Collection(entries,
            child =>
            {
                Assert.True(child.Folder);
                Assert.Equal(Path.Combine("Client", "Interview"), child.Path);
            },
            file =>
            {
                Assert.False(file.Folder);
                Assert.Equal("zulu.txt", file.Name);
                Assert.Equal(Path.Combine("Client", "zulu.txt"), file.Path);
                Assert.Equal(File.GetLastWriteTime(newer), file.Modified);
            },
            file => Assert.Equal("alpha.txt", file.Name));
    }

    [Fact]
    public void List_UsesNameOrderWhenNoSortIsSpecified()
    {
        using var directory = new TestDirectory();
        var newer = directory.CreateText("zulu.txt", "");
        var older = directory.CreateText("alpha.txt", "");
        File.SetLastWriteTimeUtc(newer, Later);
        File.SetLastWriteTimeUtc(older, Earlier);

        Assert.Equal(new[] { "alpha.txt", "zulu.txt" },
            new Library(directory.Path).List(".").Select(entry => entry.Name));
    }

    [Fact]
    public void CountFolders_CountsNestedAndEmptyFoldersButExcludesRootAndFiles()
    {
        using var directory = new TestDirectory();
        directory.CreateFolder(Path.Combine("Client", "Interview"));
        directory.CreateFolder("Empty");
        directory.CreateText(Path.Combine("Client", "log.txt"), "");
        directory.CreateText("summary.txt", "");

        Assert.Equal(3, new Library(directory.Path).CountFolders());
    }

    [Fact]
    public void CountFolders_ReturnsZeroWhenRootContainsOnlyFiles()
    {
        using var directory = new TestDirectory();
        directory.CreateText("summary.txt", "");

        Assert.Equal(0, new Library(directory.Path).CountFolders());
    }

    [Fact]
    public void Describe_ReturnsOriginalRelativePathAndFilesystemMetadata()
    {
        using var directory = new TestDirectory();
        var relative = Path.Combine("Client", "Summary.TXT");
        var fullPath = directory.CreateText(relative, "");
        File.SetLastWriteTimeUtc(fullPath, Earlier);
        var library = new Library(directory.Path);

        var entry = library.Describe(relative);

        Assert.Equal(new Entry(relative, "Summary.TXT", false, File.GetLastWriteTime(fullPath)), entry);
        Assert.True(library.Describe(".").Folder);
    }

    [Fact]
    public void Open_ProvidesReadOnlyAccessToRootRelativeFile()
    {
        using var directory = new TestDirectory();
        var relative = Path.Combine("Client", "log.txt");
        directory.CreateText(relative, "First interview");
        var library = new Library(directory.Path);

        using var stream = library.Open(relative);
        using var reader = new StreamReader(stream);

        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
        Assert.Equal("First interview", reader.ReadToEnd());
    }

    [Fact]
    public void Open_RejectsPathsOutsideTheLibraryAndAbsolutePaths()
    {
        using var directory = new TestDirectory();
        var root = directory.CreateFolder("Applications");
        var outside = directory.CreateText("outside.txt", "Outside the library");
        var library = new Library(root);

        Assert.Throws<ArgumentException>(() => library.Open(Path.GetRelativePath(root, outside)));
        Assert.Throws<ArgumentException>(() => library.Open(outside));
    }

    [Fact]
    public void DirectoryLinks_AreExcludedFromCountAndListAndCannotBeOpened()
    {
        // Windows requires Developer Mode or elevated rights to create symbolic links.
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TestDirectory();
        var root = directory.CreateFolder("Applications");
        var target = directory.CreateFolder("LinkedTarget");
        directory.CreateText(Path.Combine("LinkedTarget", "log.txt"), "outside the library");
        Directory.CreateSymbolicLink(Path.Combine(root, "Shortcut"), target);
        var library = new Library(root);

        Assert.Empty(library.List("."));
        Assert.Equal(0, library.CountFolders());
        Assert.Throws<IOException>(() => library.Open(Path.Combine("Shortcut", "log.txt")));
    }
}
