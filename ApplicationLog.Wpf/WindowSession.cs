using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ApplicationLog.Wpf;

internal sealed class WindowSession
{
    public int? TreeZoom { get; set; }
    public int? PreviewZoom { get; set; }
    public string? TreePath { get; set; }
    public string? PreviewPath { get; set; }
    public int SortIndex { get; set; }
    public string[]? ExpandedFolders { get; set; }
    public double? TreeHorizontalOffset { get; set; }
    public double? TreeVerticalOffset { get; set; }
    public double? PreviewHorizontalOffset { get; set; }
    public double? PreviewVerticalOffset { get; set; }
    public string? Worksheet { get; set; }
    public string? PdfPosition { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool Maximized { get; set; }
    public double? TreeWidthShare { get; set; }

    public static string? GetPath(string root)
    {
        string dataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(dataDirectory))
            return null;

        string rootKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(root).ToUpperInvariant())));

        return Path.Combine(dataDirectory, "ApplicationLog.Wpf", "Sessions", rootKey + ".json");
    }

    public static WindowSession Load(string? path)
    {
        if (path is null)
            return new WindowSession();

        try
        {
            using FileStream stream = File.OpenRead(path);

            return JsonSerializer.Deserialize<WindowSession>(stream) ?? new WindowSession();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new WindowSession();
        }
    }

    public void Save(string? path)
    {
        if (path is null)
            return;

        string directory = Path.GetDirectoryName(path)!;
        string temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(stream, this);

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Session settings must not prevent the window from closing.
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}