namespace Applications;

public sealed record Entry(string Path, string Name, bool Folder, DateTime Modified);

public sealed class Library(string root)
{
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
