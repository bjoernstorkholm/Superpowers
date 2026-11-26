namespace ApplicationLog.Tests;

internal sealed class TestDirectory : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("application-log-tests-");

    public string Path => _directory.FullName;

    public string CreateFolder(string relativePath)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateText(string relativePath, string text)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
