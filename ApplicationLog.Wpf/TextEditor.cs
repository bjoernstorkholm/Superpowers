using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Applications;

namespace ApplicationLog.Wpf;

public sealed class TextEditor : UserControl
{
    private readonly Library _library;
    private readonly Entry _entry;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(12)
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0)
    };
    private readonly Button _reload = new()
    {
        Content = "Discard draft and reload",
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 8, 0, 0)
    };

    private TextDocument? _document;
    private bool _loading;
    private bool _pending;
    private bool _conflict;

    public TextEditor(Library library, Entry entry)
    {
        _library = library;
        _entry = entry;

        DockPanel panel = new();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(_reload, Dock.Bottom);
        panel.Children.Add(_reload);
        panel.Children.Add(_status);
        panel.Children.Add(_editor);
        Content = panel;

        _saveTimer.Tick += (_, _) => SaveDraft();
        _editor.TextChanged += OnTextChanged;
        _reload.Click += (_, _) => LoadText();
        LoadText();
    }

    public bool SaveDraft()
    {
        StopAutosave();
        if (!_pending || _document is null)
            return true;

        if (_conflict)
            return false;

        string draft = NormalizeNewlines(_editor.Text);
        if (draft.Length > TextDocument.MaxCharacters)
        {
            ShowStatus("The draft exceeds 200,000 characters. Shorten it to save.", canReload: true);

            return false;
        }

        try
        {
            TextDocument? saved = _library.SaveText(_entry.Path, _document, draft);
            if (saved is null)
            {
                _conflict = true;
                ShowStatus("The file changed on disk. Copy your draft before reloading.", canReload: true);

                return false;
            }

            _document = saved;
            _pending = false;
            ShowStatus("Saved");

            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            _conflict = true;
            ShowStatus("The file was removed. Copy your draft before reloading.", canReload: true);

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowStatus("Could not save. Your draft is still here. Edit again or select a file to retry.", canReload: true);

            return false;
        }
    }

    public void StopAutosave() => _saveTimer.Stop();

    private void LoadText()
    {
        StopAutosave();
        _loading = true;
        _pending = false;
        _conflict = false;
        _document = null;
        try
        {
            _document = _library.ReadText(_entry.Path);
            _editor.IsReadOnly = _document is null;
            _editor.Text = _document?.Text ?? PreviewReader.Read(_library, _entry).Text ?? "";
            ShowStatus(_document is null
                ? "Read-only preview. The file is too large or uses an unsupported encoding for editing."
                : "Autosave on");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _editor.IsReadOnly = true;
            _editor.Text = "";
            ShowStatus("This file could not be previewed.");
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (_loading || _document is null)
            return;

        _pending = true;
        if (_conflict)
            return;

        ShowStatus("Saving…");
        StopAutosave();
        _saveTimer.Start();
    }

    private void ShowStatus(string message, bool canReload = false)
    {
        _status.Text = message;
        _reload.Visibility = canReload ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}