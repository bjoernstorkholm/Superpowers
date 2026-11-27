using System.Diagnostics;

namespace ApplicationLog;

public sealed record FileEntry(string Name, string RelativePath, bool IsFolder);

public sealed class ApplicationFiles(string root)
{
    private static readonly HashSet<string> DesktopDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods",
        ".ppt", ".pptx", ".odp", ".msg", ".eml", ".heic", ".tif", ".tiff"
    };

    public string Root { get; } = Path.GetFullPath(root);

    public FileEntry[] List(string relativePath)
    {
        var directory = new DirectoryInfo(Resolve(relativePath));
        return directory.EnumerateFileSystemInfos()
            .Where(entry => (entry.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(entry => new FileEntry(entry.Name, Path.GetRelativePath(Root, entry.FullName),
                entry is DirectoryInfo))
            .OrderByDescending(entry => entry.IsFolder)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
