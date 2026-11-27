using System.Text.Json;
using System.Windows;
using Applications;

namespace ApplicationLog.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string root = Environment.GetEnvironmentVariable("ApplicationsRoot") ?? ReadConfiguredRoot();
            MainWindow = new MainWindow(new Library(root));
            MainWindow.Show();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show("Check ApplicationsRoot in appsettings.json or in the environment.",
                "ApplicationLog could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string ReadConfiguredRoot()
    {
        using FileStream settings = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        using JsonDocument document = JsonDocument.Parse(settings, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return document.RootElement.TryGetProperty("ApplicationsRoot", out JsonElement root)
            ? root.GetString() ?? ""
            : throw new InvalidOperationException("ApplicationsRoot is missing.");
    }
}