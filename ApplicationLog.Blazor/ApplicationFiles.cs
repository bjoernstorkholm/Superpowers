using System.Diagnostics;
using Applications;

namespace ApplicationLog;

public sealed record FileEntry(string Name, string RelativePath, bool IsFolder, DateTime Modified = default);

public sealed class ApplicationFiles(string root)
{
    private static readonly HashSet<string> DesktopDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods",
        ".ppt", ".pptx", ".odp", ".msg", ".eml", ".heic", ".tif", ".tiff"
    };

    public string Root { get; } = Path.GetFullPath(root);

    public FileEntry[] List(string relativePath, bool sortByDate = false)
    {
        var directory = new DirectoryInfo(Resolve(relativePath));
        IOrderedEnumerable<FileEntry> entries = directory.EnumerateFileSystemInfos()
            .Where(entry => (entry.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(entry => new FileEntry(entry.Name, Path.GetRelativePath(Root, entry.FullName),
                entry is DirectoryInfo, entry.LastWriteTime))
            .OrderByDescending(entry => !entry.IsFolder &&
                string.Equals(entry.Name, "log.txt", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(entry => entry.IsFolder);

        if (sortByDate)
            entries = entries.ThenByDescending(entry => entry.Modified.ToUniversalTime());

        return entries.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public int CountFolders() =>
        Directory.EnumerateDirectories(Resolve(""), "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Count();

    public FileEntry Describe(string relativePath)
    {
        string path = Resolve(relativePath);
        FileAttributes attributes = File.GetAttributes(path);
        return new FileEntry(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            Path.GetRelativePath(Root, path), (attributes & FileAttributes.Directory) != 0,
            File.GetLastWriteTime(path));
    }

    public void OpenRootOnDesktop()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Opening folders in File Explorer is available on Windows.");

        string path = Resolve("");
        if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
            throw new DirectoryNotFoundException();

        using Process? process = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }

    public static string? PreviewType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".txt" or ".md" or ".csv" or ".log" => "text/plain; charset=utf-8",
        _ => null
    };

    public static bool CanOpenOnDesktop(string path) =>
        PreviewType(path) is not null || DesktopDocuments.Contains(Path.GetExtension(path));

    public static bool CanPreviewOffice(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".xlsx";

    public Preview ReadOfficePreview(string relativePath)
    {
        if (!CanPreviewOffice(relativePath))
            throw new NotSupportedException("Only DOCX and XLSX can be previewed as Office documents.");

        string path = ResolveFile(relativePath);
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return PreviewReader.Read(file, Path.GetExtension(path));
    }

    public string ResolveFile(string relativePath)
    {
        var path = Resolve(relativePath);
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            throw new FileNotFoundException();

        return path;
    }

    public void OpenOnDesktop(string relativePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Opening files in desktop apps is available on Windows.");
        if (!CanOpenOnDesktop(relativePath))
            throw new InvalidOperationException("This file type cannot be opened from ApplicationLog.");

        using var process = Process.Start(new ProcessStartInfo(ResolveFile(relativePath))
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }

    private string Resolve(string relativePath)
    {
        var path = Root;
        RejectLink(path);
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        foreach (var segment in relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new FileNotFoundException();

            path = Path.Join(path, segment);
            RejectLink(path);
        }

        return path;
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException();
    }
}