using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Applications;

namespace ApplicationLog.Wpf;

public partial class MainWindow : Window
{
    private const int MinimumZoom = 50;
    private const int DefaultZoom = 100;
    private const int MaximumZoom = 200;
    private const int ZoomStep = 25;

    private readonly Library _library;
    private readonly string? _sessionPath;
    private readonly object _unloaded = new();
    private readonly object _folderMessage = new();
    private readonly object _emptyPreview;
    private TreeViewItem? _root;
    private Entry? _selected;
    private bool _updatingTree;
    private int _treeZoom = DefaultZoom;
    private int _previewZoom = DefaultZoom;

    public MainWindow(Library library) : this(library, WindowSession.GetPath(library.Root))
    {
    }

    internal MainWindow(Library library, string? sessionPath)
    {
        _library = library;
        _sessionPath = sessionPath;
        InitializeComponent();
        _emptyPreview = PreviewPane.Content;
        RootPath.Text = library.Root;
        WindowSession session = WindowSession.Load(_sessionPath);
        SetTreeZoom(session.TreeZoom ?? DefaultZoom);
        _previewZoom = Math.Clamp(session.PreviewZoom ?? DefaultZoom, MinimumZoom, MaximumZoom);
        RefreshTree();
        RestoreSession(session);
    }

    private string? SelectedTreePath => FileTree.SelectedItem is TreeViewItem { Tag: Entry entry }
        ? entry.Path
        : null;

    private void RestoreSession(WindowSession session)
    {
        if (_root is null)
            return;

        _updatingTree = true;
        try
        {
            if (session.PreviewPath is string previewPath)
            {
                try
                {
                    Entry entry = _library.Describe(previewPath);
                    if (!entry.Folder)
                        ShowPreview(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // The previous document may have moved or been removed.
                }
            }

            if (session.TreePath is string treePath)
                RestoreTreePath(treePath);
        }
        finally
        {
            _updatingTree = false;
        }
    }

    private void RestoreTreePath(string path)
    {
        TreeViewItem? item = _root;
        if (item is null)
            return;

        if (path != ".")
        {
            string relativePath = "";
            foreach (string part in path.Split(Path.DirectorySeparatorChar))
            {
                relativePath = Path.Combine(relativePath, part);
                if (!item.IsExpanded && item.Tag is Entry { Folder: true } folder)
                {
                    LoadChildren(item, folder);
                    item.IsExpanded = true;
                }

                item = item.Items.OfType<TreeViewItem>().FirstOrDefault(child =>
                    child.Tag is Entry entry && entry.Path == relativePath);
                if (item is null)
                    return;
            }
        }

        item.IsSelected = true;
        item.BringIntoView();
    }

    private TreeSortOrder CurrentSort => SortOrder.SelectedIndex == 1
        ? TreeSortOrder.DateModified
        : TreeSortOrder.Name;

    private void RefreshFolders(object sender, RoutedEventArgs e)
    {
        if (SaveDraft())
            RefreshTree();
    }

    private void RefreshTree()
    {
        string? treePath = SelectedTreePath;
        _updatingTree = true;
        try
        {
            FileTree.Items.Clear();
            _root = null;
            FolderCount.Text = "";
            FolderMessage.Visibility = Visibility.Collapsed;
            try
            {
                Entry root = _library.Describe(".");
                if (!root.Folder)
                    throw new IOException();

                TreeViewItem rootItem = CreateItem(root);
                _root = rootItem;
                FileTree.Items.Add(rootItem);
                rootItem.IsExpanded = true;
                LoadChildren(rootItem, root);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                FolderMessage.Text = "The applications folder could not be opened. Check the configured folder path.";
                FolderMessage.Visibility = Visibility.Visible;
                ClearPreview();
                return;
            }

            try
            {
                int count = _library.CountFolders();
                FolderCount.Text = count == 1 ? "1 folder" : $"{count:N0} folders";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                FolderCount.Text = "Folder count unavailable";
            }

            if (_selected is null)
            {
                RestoreVisibleSelection(_root, treePath);
                return;
            }

            try
            {
                Entry updated = _library.Describe(_selected.Path);
                if (updated.Folder)
                    ClearPreview();
                else
                    ShowPreview(updated);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ClearPreview();
            }

            RestoreVisibleSelection(_root, treePath);
        }
        finally
        {
            _updatingTree = false;
        }
    }

    private TreeViewItem CreateItem(Entry entry)
    {
        TextBlock label = new TextBlock { Padding = new Thickness(2, 4, 8, 4) };
        label.Inlines.Add(new Run(entry.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
        {
            FontSize = 12
        });
        label.Inlines.Add(new Run("  " + entry.Name)
        {
            FontWeight = entry.Folder ? FontWeights.SemiBold : FontWeights.Normal
        });
        TreeViewItem item = new TreeViewItem { Header = label, Tag = entry };
        if (entry.Folder)
        {
            item.Items.Add(_unloaded);
            label.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount != 1)
                    return;

                item.Focus();
                item.IsExpanded = !item.IsExpanded;
            };
        }

        return item;
    }

    private void FolderExpanded(object sender, RoutedEventArgs e)
    {
        if (_updatingTree || e.OriginalSource is not TreeViewItem { Tag: Entry { Folder: true } entry } item)
            return;

        string? treePath = SelectedTreePath;
        _updatingTree = true;
        try
        {
            if (ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), _root))
            {
                foreach (TreeViewItem sibling in _root!.Items.OfType<TreeViewItem>())
                {
                    if (!ReferenceEquals(sibling, item))
                        sibling.IsExpanded = false;
                }
            }

            LoadChildren(item, entry);
            RestoreVisibleSelection(item, treePath);
        }
        finally
        {
            _updatingTree = false;
        }
    }

    private void LoadChildren(TreeViewItem parent, Entry entry)
    {
        try
        {
            Entry[] children = _library.List(entry.Path, CurrentSort);
            parent.Items.Clear();
            if (children.Length == 0)
                ShowFolderMessage(parent, "Empty folder");
            else
                foreach (Entry child in children)
                    parent.Items.Add(CreateItem(child));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            parent.Items.Clear();
            ShowFolderMessage(parent, "This folder could not be read.");
        }
    }

    private void ShowFolderMessage(TreeViewItem parent, string message)
    {
        parent.Items.Add(new TreeViewItem
        {
            Header = message,
            Tag = _folderMessage,
            Focusable = false,
            IsHitTestVisible = false,
            Margin = new Thickness(2, 2, 0, 2)
        });
    }

    private void SortOrderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_root is null)
            return;

        string? treePath = SelectedTreePath;
        _updatingTree = true;
        try
        {
            SortChildren(_root);
            RestoreVisibleSelection(_root, treePath);
        }
        finally
        {
            _updatingTree = false;
        }
    }

    private void SortChildren(TreeViewItem parent)
    {
        TreeViewItem[] children = parent.Items.OfType<TreeViewItem>()
            .Where(item => item.Tag is Entry).ToArray();
        if (children.Length == 0)
            return;

        Dictionary<string, TreeViewItem> byPath = children.ToDictionary(item => ((Entry)item.Tag).Path);
        Entry[] sorted = Library.SortEntries(children.Select(item => (Entry)item.Tag), CurrentSort);
        parent.Items.Clear();
        foreach (Entry entry in sorted)
        {
            TreeViewItem item = byPath[entry.Path];
            parent.Items.Add(item);
            SortChildren(item);
        }
    }

    private static void RestoreVisibleSelection(TreeViewItem? parent, string? treePath)
    {
        if (parent is not { IsExpanded: true } || treePath is null)
            return;

        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            if (child.Tag is Entry entry && entry.Path == treePath)
            {
                child.IsSelected = true;
                return;
            }

            RestoreVisibleSelection(child, treePath);
        }
    }

    private void SelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_updatingTree || e.NewValue is not TreeViewItem { Tag: Entry { Folder: false } entry } item
            || entry.Path == _selected?.Path)
            return;

        if (SaveDraft())
        {
            ShowPreview(entry);
            return;
        }

        _updatingTree = true;
        try
        {
            if (e.OldValue is TreeViewItem oldItem)
                oldItem.IsSelected = true;
            else
                item.IsSelected = false;
        }
        finally
        {
            _updatingTree = false;
        }
    }

    private bool SaveDraft() => PreviewPane.Content is not TextEditor editor || editor.SaveDraft();

    private void ClearPreview()
    {
        ReleasePreview();
        _selected = null;
        PreviewPane.Content = _emptyPreview;
        DocumentHeader.Visibility = Visibility.Collapsed;
        PreviewZoomControls.Visibility = Visibility.Collapsed;
    }

    private void ShowPreview(Entry entry)
    {
        ReleasePreview();
        _selected = entry;
        DocumentName.Text = entry.Name;
        DocumentPath.Text = entry.Path;
        DocumentHeader.Visibility = Visibility.Visible;
        PreviewZoomControls.Visibility = Visibility.Collapsed;
        try
        {
            string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
            PreviewPane.Content = extension == ".txt"
                ? new TextEditor(_library, entry)
                : DocumentPreviews.Create(_library, entry);
            if (extension is ".txt" or ".pdf" or ".docx" or ".xlsx")
            {
                PreviewZoomControls.Visibility = Visibility.Visible;
                SetPreviewZoom(_previewZoom);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            PreviewPane.Content = new TextBlock
            {
                Text = "This file could not be previewed.",
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    private void SetTreeZoom(int percent)
    {
        _treeZoom = Math.Clamp(percent, MinimumZoom, MaximumZoom);
        FileTree.LayoutTransform = ScaleForZoom(_treeZoom);
        UpdateZoomButtons(TreeZoomOutButton, TreeZoomResetButton, TreeZoomInButton, _treeZoom, "folder tree");
    }

    private void SetPreviewZoom(int percent)
    {
        if (PreviewZoomControls.Visibility != Visibility.Visible || PreviewPane.Content is not FrameworkElement preview)
            return;

        _previewZoom = Math.Clamp(percent, MinimumZoom, MaximumZoom);
        if (!DocumentPreviews.TrySetPdfZoom(preview, _previewZoom / 100.0))
            preview.LayoutTransform = ScaleForZoom(_previewZoom);

        UpdateZoomButtons(ZoomOutButton, ZoomResetButton, ZoomInButton, _previewZoom, "preview");
    }

    private static Transform ScaleForZoom(int percent) =>
        percent == DefaultZoom ? Transform.Identity : new ScaleTransform(percent / 100.0, percent / 100.0);

    private static void UpdateZoomButtons(Button zoomOut, Button reset, Button zoomIn, int percent, string region)
    {
        zoomOut.IsEnabled = percent > MinimumZoom;
        zoomIn.IsEnabled = percent < MaximumZoom;
        reset.Content = $"{percent}%";
        AutomationProperties.SetName(reset, $"Reset {region} zoom from {percent}% to 100%");
    }

    private void TreeZoomOut(object sender, RoutedEventArgs e) => SetTreeZoom(_treeZoom - ZoomStep);

    private void TreeZoomReset(object sender, RoutedEventArgs e) => SetTreeZoom(DefaultZoom);

    private void TreeZoomIn(object sender, RoutedEventArgs e) => SetTreeZoom(_treeZoom + ZoomStep);

    private void PreviewZoomOut(object sender, RoutedEventArgs e) => SetPreviewZoom(_previewZoom - ZoomStep);

    private void PreviewZoomReset(object sender, RoutedEventArgs e) => SetPreviewZoom(DefaultZoom);

    private void PreviewZoomIn(object sender, RoutedEventArgs e) => SetPreviewZoom(_previewZoom + ZoomStep);

    private void TreeZoomWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0)
            return;

        SetTreeZoom(_treeZoom + Math.Sign(e.Delta) * ZoomStep);
        e.Handled = true;
    }

    private void PreviewZoomWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0 ||
            PreviewZoomControls.Visibility != Visibility.Visible)
            return;

        SetPreviewZoom(_previewZoom + Math.Sign(e.Delta) * ZoomStep);
        e.Handled = true;
    }

    private void ZoomKeyDown(object sender, KeyEventArgs e)
    {
        bool tree = TreePane.IsKeyboardFocusWithin;
        if (!tree && (!DocumentPane.IsKeyboardFocusWithin || PreviewZoomControls.Visibility != Visibility.Visible))
            return;

        int currentZoom = tree ? _treeZoom : _previewZoom;
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool control = modifiers == ModifierKeys.Control;
        int? requested = (e.Key, control) switch
        {
            (Key.OemPlus, _) when control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
                => currentZoom + ZoomStep,
            (Key.Add, true) => currentZoom + ZoomStep,
            (Key.Subtract or Key.OemMinus, true) => currentZoom - ZoomStep,
            (Key.D0 or Key.NumPad0, true) => DefaultZoom,
            _ => null
        };
        if (requested is not int percent)
            return;

        if (tree)
            SetTreeZoom(percent);
        else
            SetPreviewZoom(percent);

        e.Handled = true;
    }

    private void ReleasePreview()
    {
        if (PreviewPane.Content is TextEditor editor)
            editor.StopAutosave();
        if (PreviewPane.Content is IDisposable disposable)
            disposable.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel)
            e.Cancel = !SaveDraft();
    }

    protected override void OnClosed(EventArgs e)
    {
        new WindowSession
        {
            TreeZoom = _treeZoom,
            PreviewZoom = _previewZoom,
            TreePath = SelectedTreePath,
            PreviewPath = _selected?.Path
        }.Save(_sessionPath);
        ReleasePreview();
        base.OnClosed(e);
    }
}