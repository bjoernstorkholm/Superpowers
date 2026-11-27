using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Applications;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ApplicationLog.Wpf;

internal sealed partial class PdfPreviewHost : Grid, IDisposable
{
    private const string VirtualHost = "applicationlog-pdf.invalid";
    private const string ViewerPath = "/web/viewer.html";
    private const string PdfPath = "/document.pdf";
    private readonly Library _library;
    private readonly Entry _entry;
    private readonly TextBlock _status = new()
    {
        Text = "Loading PDF…",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(12)
    };
    private WebView2? _viewer;
    private bool _ready;
    private double _zoom = 1;

    public string? Position { get; private set; }

    public PdfPreviewHost(Library library, Entry entry)
    {
        _library = library;
        _entry = entry;
        using (_library.Open(_entry.Path)) { }

        Children.Add(_status);
        Loaded += LoadViewer;
        Unloaded += (_, _) => ReleaseViewer();
    }

    public void RestorePosition(string? position)
    {
        if (position is { Length: <= 100 } && PositionPattern().IsMatch(position))
            Position = position;
    }

    public void SetZoom(double factor)
    {
        _zoom = factor;
        if (_ready)
            _viewer?.CoreWebView2.PostWebMessageAsJson(factor.ToString(CultureInfo.InvariantCulture));
    }

    private async void LoadViewer(object sender, RoutedEventArgs e)
    {
        if (_viewer is not null)
            return;

        _status.Text = "Loading PDF…";
        _status.Visibility = Visibility.Visible;
        WebView2 viewer = new();
        _viewer = viewer;
        Children.Add(viewer);
        try
        {
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApplicationLog.Wpf", "WebView2");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (_viewer != viewer)
                return;

            await viewer.EnsureCoreWebView2Async(environment);
            if (_viewer != viewer)
                return;

            CoreWebView2 browser = viewer.CoreWebView2;
            ConfigureBrowser(browser, environment);
            double initialZoom = _zoom;
            browser.WebMessageReceived += (_, args) =>
            {
                if (_viewer != viewer || !IsLocalUrl(args.Source, ViewerPath))
                    return;

                string message = args.TryGetWebMessageAsString();
                switch (message)
                {
                    case "ready":
                        if (!_ready)
                        {
                            _ready = true;
                            viewer.Visibility = Visibility.Visible;
                            _status.Visibility = Visibility.Collapsed;
                            if (initialZoom != _zoom)
                                SetZoom(_zoom);
                        }
                        break;
                    case "error":
                        ShowError("This PDF could not be previewed.");
                        break;
                    default:
                        RestorePosition(message);
                        break;
                }
            };
            browser.NavigationCompleted += (_, args) =>
            {
                if (_viewer == viewer && !args.IsSuccess &&
                    args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    ShowError("This PDF could not be previewed.");
            };

            string percent = (initialZoom * 100).ToString(CultureInfo.InvariantCulture);
            browser.Navigate($"https://{VirtualHost}{ViewerPath}?file={PdfPath}&appZoom={percent}{Position}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (_viewer == viewer)
                ShowError("PDF preview requires the Microsoft Edge WebView2 Runtime.");
        }
        catch (Exception)
        {
            if (_viewer == viewer)
                ShowError("This PDF could not be previewed.");
        }
    }

    private void ConfigureBrowser(CoreWebView2 browser, CoreWebView2Environment environment)
    {
        browser.Settings.AreDevToolsEnabled = false;
        browser.Settings.AreDefaultContextMenusEnabled = false;
        browser.Settings.AreHostObjectsAllowed = false;
        browser.Settings.AreBrowserAcceleratorKeysEnabled = false;
        browser.Settings.IsZoomControlEnabled = false;
        browser.Settings.IsStatusBarEnabled = false;

        browser.NavigationStarting += (_, args) => args.Cancel = !IsLocalUrl(args.Uri, ViewerPath);
        browser.FrameNavigationStarting += (_, args) => args.Cancel = true;
        browser.NewWindowRequested += (_, args) => args.Handled = true;
        browser.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        browser.DownloadStarting += (_, args) => args.Cancel = true;
        browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        browser.WebResourceRequested += (_, args) =>
        {
            if (!IsLocalUrl(args.Request.Uri))
            {
                args.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
                return;
            }

            try
            {
                Uri request = new(args.Request.Uri);
                string relative = Uri.UnescapeDataString(request.AbsolutePath).TrimStart('/');
                string assetRoot = Path.Combine(AppContext.BaseDirectory, "PdfViewer") + Path.DirectorySeparatorChar;
                string assetPath = Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), assetRoot);
                if (!assetPath.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
                    return;
                }

                bool document = request.AbsolutePath == PdfPath;
                FileStream stream = document ? _library.Open(_entry.Path) : File.OpenRead(assetPath);
                string mime = document ? "application/pdf" : AssetMime(assetPath);
                args.Response = environment.CreateWebResourceResponse(stream, 200, "OK",
                    $"Content-Type: {mime}\r\nContent-Length: {stream.Length}\r\nCache-Control: no-store");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                args.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            }
        };
    }

    private static string AssetMime(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".ftl" => "text/plain; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream"
    };

    private static bool IsLocalUrl(string text, string? path = null) =>
        Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host == VirtualHost && uri.IsDefaultPort && (path is null || uri.AbsolutePath == path);

    private void ShowError(string text)
    {
        if (_viewer is not null)
            _viewer.Visibility = Visibility.Hidden;
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
    }

    private void ReleaseViewer()
    {
        _ready = false;
        if (_viewer is not WebView2 viewer)
            return;

        _viewer = null;
        Children.Remove(viewer);
        viewer.Dispose();
    }

    public void Dispose()
    {
        Loaded -= LoadViewer;
        ReleaseViewer();
    }

    [GeneratedRegex(@"\A#page=[1-9][0-9]*(?:&zoom=(?:[0-9]+(?:\.[0-9]+)?|auto|page-fit|page-width|page-height|page-actual),-?[0-9]+,-?[0-9]+)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex PositionPattern();
}