namespace Applications;

public sealed record Entry(string Path, string Name, bool Folder, DateTime Modified);

public sealed class Library(string root)
{
    private readonly object _textSaveLock = new();

    public string Root { get; } = Path.GetFullPath(root);

    public Entry Describe(string path)
    {
        var fullPath = Resolve(path);
        var attributes = File.GetAttributes(fullPath);
        return new Entry(path, Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath)),
            attributes.HasFlag(FileAttributes.Directory), File.GetLastWriteTime(fullPath));
    }

    public Entry[] List(string path) =>
        new DirectoryInfo(Resolve(path))
            .EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            .Select(item => new Entry(Path.GetRelativePath(Root, item.FullName), item.Name,
                item is DirectoryInfo, item.LastWriteTime))
            .OrderByDescending(entry => entry.Folder)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public int CountFolders() =>
        Directory.EnumerateDirectories(Resolve("."), "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Count();

    public FileStream Open(string path) =>
        new(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    internal TextDocument? ReadText(string path)
    {
        EnsureTextFile(path);
        using var stream = Open(path);
        return TextDocument.Read(stream);
    }

    internal TextDocument? SaveText(string path, TextDocument original, string draft)
    {
        EnsureTextFile(path);
        if (draft.Length > TextDocument.MaxCharacters)
            throw new ArgumentException("The draft exceeds the text editor limit.");

        var bytes = original.Encode(draft);
        lock (_textSaveLock)
        {
            var fullPath = Resolve(path);
            var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Guid.NewGuid():N}.tmp");
            var pending = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write);
            try
            {
                using (pending)
                {
                    pending.Write(bytes);
                    pending.Flush(flushToDisk: true);
                }

                byte[]? currentBytes;
                using (var current = Open(path))
                    currentBytes = TextDocument.ReadBytes(current);
                if (currentBytes is null || !currentBytes.AsSpan().SequenceEqual(original.Version))
                    return null;

                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
                return original with { Version = bytes };
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureTextFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only TXT files can be edited.");
    }

    private string Resolve(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains(':'))
            throw new ArgumentException("Invalid relative path.");

        var fullPath = Path.GetFullPath(path, Root);
        var relative = Path.GetRelativePath(Root, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative))
            throw new ArgumentException("Path is outside the applications folder.");

        var current = Root;
        CheckLink(current);
        if (relative != ".")
        {
            foreach (var part in relative.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                CheckLink(current);
            }
        }

        return fullPath;
    }

    private static void CheckLink(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Linked folders and files are skipped.");
    }
}
