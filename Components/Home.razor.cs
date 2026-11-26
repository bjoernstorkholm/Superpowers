using Microsoft.AspNetCore.Components;

namespace Applications.Components;

public partial class Home : IDisposable
{
    [Inject] private Library Library { get; set; } = default!;
    [Inject] private ILogger<Home> Logger { get; set; } = default!;

    private Entry? _root;
    private int? _folderCount;
    private TreeSortOrder _sortOrder = TreeSortOrder.Name;
    private Entry? _selected;
    private Preview? _preview;
    private string? _pdfUrl;
    private string? _message;
    private int _revision;
    private int _sheet;
    private TextDocument? _textDocument;
    private string _draft = "";
    private string _saveStatus = "";
    private bool _draftPending;
    private bool _saveConflict;
    private int _textEditorRevision;
    private CancellationTokenSource? _saveDelay;

    private string FolderCountLabel => _folderCount switch
    {
        null => "Folder count unavailable",
        1 => "1 folder",
        int count => $"{count:N0} folders"
    };

    protected override void OnInitialized() => Refresh();

    private void Refresh()
    {
        if (!SaveDraft())
            return;

        _revision++;
        try
        {
            _root = Library.Describe(".");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _root = null;
        }

        if (_root is not { Folder: true })
        {
            _root = null;
            _selected = null;
            return;
        }

        try
        {
            _folderCount = Library.CountFolders();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _folderCount = null;
        }

        if (_selected is null)
            return;

        try
        {
            Entry updated = Library.Describe(_selected.Path);
            if (updated.Folder)
                _selected = null;
            else
                Select(updated);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _selected = null;
        }
    }

    private void Select(Entry entry)
    {
        if (SaveDraft())
            LoadPreview(entry);
    }

    private void LoadPreview(Entry entry)
    {
        _textDocument = null;
        _draftPending = false;
        _saveConflict = false;
        _selected = entry;
        _preview = null;
        _pdfUrl = null;
        _message = null;
        _sheet = 0;
        try
        {
            switch (Path.GetExtension(entry.Name).ToLowerInvariant())
            {
                case ".pdf":
                    using (Library.Open(entry.Path)) { }
                    _pdfUrl = "preview/pdf?path=" + Uri.EscapeDataString(entry.Path) + "&v=" + _revision;
                    break;
                case ".txt":
                    _textDocument = Library.ReadText(entry.Path);
                    if (_textDocument is not null)
                    {
                        _draft = _textDocument.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                        _saveStatus = "Autosave on";
                        _textEditorRevision++;
                        break;
                    }
                    _preview = PreviewReader.Read(Library, entry);
                    break;
                case ".docx":
                case ".xlsx":
                    _preview = PreviewReader.Read(Library, entry);
                    break;
                default:
                    _message = "Preview is available for PDF, TXT, DOCX and XLSX files.";
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Unable to preview the selected file.");
            _message = "This file could not be previewed.";
        }
    }

    private async Task ScheduleSave()
    {
        _draftPending = true;
        if (_saveConflict)
            return;

        _saveStatus = "Saving…";
        _saveDelay?.Cancel();
        using CancellationTokenSource delay = new CancellationTokenSource();
        _saveDelay = delay;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), delay.Token);
            _saveDelay = null;
            SaveDraft();
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested)
        {
        }
    }

    private bool SaveDraft()
    {
        _saveDelay?.Cancel();
        _saveDelay = null;
        if (!_draftPending || _textDocument is null || _selected is null)
            return true;

        if (_saveConflict)
            return false;

        try
        {
            TextDocument? saved = Library.SaveText(_selected.Path, _textDocument, _draft);
            if (saved is null)
            {
                _saveConflict = true;
                _saveStatus = "The file changed on disk. Copy your draft before reloading.";

                return false;
            }

            _textDocument = saved;
            _draftPending = false;
            _saveStatus = "Saved";

            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            _saveConflict = true;
            _saveStatus = "The file was removed. Copy your draft before reloading.";

            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger.LogWarning(e, "Unable to save the selected text file.");
            _saveStatus = "Could not save. Your draft is still here. Edit again or select a file to retry.";

            return false;
        }
    }

    private void ReloadText()
    {
        if (_selected is not null)
            LoadPreview(_selected);
    }

    public void Dispose()
    {
        _saveDelay?.Cancel();
        _saveDelay = null;
    }

    private static string ColumnName(int column)
    {
        string name = "";
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }
}