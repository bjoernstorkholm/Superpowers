using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private Point? _pathClickStart;
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
        SortOrder.DropDownClosed += (_, _) => RevealSelectedTreeRow();
        _emptyPreview = PreviewPane.Content;
        RootPath.Text = library.Root;
        WindowSession session = WindowSession.Load(_sessionPath);
        RestoreWindowLayout(session);
        SortOrder.SelectedIndex = session.SortIndex == 1 ? 1 : 0;
        SetTreeZoom(session.TreeZoom ?? DefaultZoom);
        _previewZoom = Math.Clamp(session.PreviewZoom ?? DefaultZoom, MinimumZoom, MaximumZoom);
        RefreshTree();
        RestoreSession(session);
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            () => RestoreScrollPositions(session));
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
                    {
                        ShowPreview(entry);
                        DocumentPreviews.RestoreWorksheet(PreviewPane.Content as FrameworkElement, session.Worksheet);
                        if (PreviewPane.Content is PdfPreviewHost pdf)
                            pdf.RestorePosition(session.PdfPosition);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // The previous document may have moved or been removed.
                }
            }

            if (session.ExpandedFolders is string[] expandedFolders)
            {
                _root.IsExpanded = expandedFolders.Contains(".", StringComparer.Ordinal);
                RestoreExpandedFolders(_root, expandedFolders.ToHashSet(StringComparer.Ordinal));
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
    }

    private void RestoreExpandedFolders(TreeViewItem item, HashSet<string> expanded)
    {
        if (item.Tag is not Entry { Folder: true } folder || !expanded.Contains(folder.Path))
            return;

        if (!item.IsExpanded)
        {
            LoadChildren(item, folder);
            item.IsExpanded = true;
        }

        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            RestoreExpandedFolders(child, expanded);
    }

    private static IEnumerable<string> ExpandedFolders(TreeViewItem item)
    {
        if (!item.IsExpanded || item.Tag is not Entry { Folder: true } folder)
            yield break;

        yield return folder.Path;
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
        foreach (string path in ExpandedFolders(child))
            yield return path;
    }

    private void RestoreWindowLayout(WindowSession session)
    {
        if (session.TreeWidthShare is double share && share is > 0 and < 1)
        {
            TreeColumn.Width = new GridLength(share, GridUnitType.Star);
            PreviewColumn.Width = new GridLength(1 - share, GridUnitType.Star);
        }

        if (session.WindowWidth is double width && double.IsFinite(width) && width >= MinWidth &&
            session.WindowHeight is double height && double.IsFinite(height) && height >= MinHeight)
        {
            Width = Math.Min(width, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
            Height = Math.Min(height, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));
        }

        if (session.WindowLeft is double left && double.IsFinite(left) &&
            session.WindowTop is double top && double.IsFinite(top))
        {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
        }

        if (session.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void RestoreScrollPositions(WindowSession session)
    {
        RestoreScroll(FindScrollViewer(FileTree), session.TreeHorizontalOffset, session.TreeVerticalOffset);
        if (session.PreviewPath == _selected?.Path)
            RestoreScroll(FindScrollViewer(PreviewPane), session.PreviewHorizontalOffset, session.PreviewVerticalOffset);
    }

    private static void RestoreScroll(ScrollViewer? viewer, double? horizontal, double? vertical)
    {
        if (viewer is null)
            return;

        if (horizontal is double x && double.IsFinite(x) && x >= 0)
            viewer.ScrollToHorizontalOffset(x);
        if (vertical is double y && double.IsFinite(y) && y >= 0)
            viewer.ScrollToVerticalOffset(y);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer)
            return viewer;

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            ScrollViewer? child = FindScrollViewer(VisualTreeHelper.GetChild(parent, index));
            if (child is not null)
                return child;
        }

        return null;
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

            if (treePath is not null)
            {
                RestoreTreePath(treePath);
                RevealSelectedTreeRow();
            }

            if (_selected is null)
                return;

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
            RevealSelectedTreeRow();
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
            if (treePath is not null)
                RestoreTreePath(treePath);

            if (!SortOrder.IsDropDownOpen)
                RevealSelectedTreeRow();
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
        DocumentPath.Text = Path.GetFullPath(entry.Path, _library.Root);
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

    private void PathMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pathClickStart = e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None
            ? e.GetPosition((TextBox)sender)
            : null;
    }

    private void PathMouseUp(object sender, MouseButtonEventArgs e)
    {
        Point? start = _pathClickStart;
        _pathClickStart = null;
        if (start is not Point origin || sender is not TextBox path || path.SelectionLength != 0)
            return;

        Vector movement = e.GetPosition(path) - origin;
        if (Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance)
            OpenPath(path);
    }

    private void PathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || sender is not TextBox path)
            return;

        e.Handled = true;
        OpenPath(path);
    }

    private void OpenPath(TextBox path)
    {
        try
        {
            if (ReferenceEquals(path, RootPath))
            {
                _library.Describe(".");
                using Process? explorer = Process.Start(new ProcessStartInfo(_library.Root) { UseShellExecute = true });
                return;
            }

            if (_selected is not Entry selected)
                return;

            _library.Describe(selected.Path);
            using Process? document = Process.Start(new ProcessStartInfo(path.Text) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            string message = ReferenceEquals(path, RootPath)
                ? "This folder could not be opened in File Explorer."
                : "This document could not be opened in its default app.";
            MessageBox.Show(this, message, "ApplicationLog",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TreeToolbarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Size unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        // Measure at natural widths so the current layout cannot hide the width needed for a single row.
        TreeSortControls.Measure(unconstrained);
        TreeZoomControls.Measure(unconstrained);
        double zoomWidth = TreeZoomControls.DesiredSize.Width - TreeZoomControls.Margin.Left;
        bool wraps = e.NewSize.Width < TreeSortControls.DesiredSize.Width + 8 + zoomWidth;
        Grid.SetRow(TreeZoomControls, wraps ? 1 : 0);
        Grid.SetColumn(TreeZoomControls, wraps ? 0 : 1);
        Grid.SetColumnSpan(TreeZoomControls, wraps ? 2 : 1);
        TreeZoomControls.HorizontalAlignment = wraps ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        double dropdownLeft = TreeSortControls.DesiredSize.Width - SortOrder.DesiredSize.Width;
        TreeZoomControls.Margin = wraps ? new Thickness(dropdownLeft, 6, 0, 0) : new Thickness(8, 0, 0, 0);
    }

    private void RevealSelectedTreeRow()
    {
        if (FileTree.SelectedItem is not TreeViewItem { Header: FrameworkElement header } item)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (ReferenceEquals(FileTree.SelectedItem, item))
                header.BringIntoView();
        });
    }

    private void SetTreeZoom(int percent)
    {
        _treeZoom = Math.Clamp(percent, MinimumZoom, MaximumZoom);
        FileTree.LayoutTransform = ScaleForZoom(_treeZoom);
        UpdateZoomButtons(TreeZoomOutButton, TreeZoomResetButton, TreeZoomInButton, _treeZoom, "folder tree");
        RevealSelectedTreeRow();
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
        if (e.Cancel)
            return;

        e.Cancel = !SaveDraft();
        if (!e.Cancel)
            SaveSession();
    }

    private void SaveSession()
    {
        ScrollViewer? tree = FindScrollViewer(FileTree);
        ScrollViewer? preview = FindScrollViewer(PreviewPane);
        Rect bounds = RestoreBounds;
        double paneWidth = TreeColumn.ActualWidth + PreviewColumn.ActualWidth;
        new WindowSession
        {
            TreeZoom = _treeZoom,
            PreviewZoom = _previewZoom,
            TreePath = SelectedTreePath,
            PreviewPath = _selected?.Path,
            SortIndex = SortOrder.SelectedIndex,
            ExpandedFolders = _root is null ? null : ExpandedFolders(_root).ToArray(),
            TreeHorizontalOffset = tree?.HorizontalOffset,
            TreeVerticalOffset = tree?.VerticalOffset,
            PreviewHorizontalOffset = preview?.HorizontalOffset,
            PreviewVerticalOffset = preview?.VerticalOffset,
            Worksheet = DocumentPreviews.GetWorksheet(PreviewPane.Content as FrameworkElement),
            PdfPosition = (PreviewPane.Content as PdfPreviewHost)?.Position,
            WindowLeft = bounds.IsEmpty ? null : bounds.Left,
            WindowTop = bounds.IsEmpty ? null : bounds.Top,
            WindowWidth = bounds.IsEmpty ? null : bounds.Width,
            WindowHeight = bounds.IsEmpty ? null : bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
            TreeWidthShare = paneWidth > 0 ? TreeColumn.ActualWidth / paneWidth : null
        }.Save(_sessionPath);
    }

    protected override void OnClosed(EventArgs e)
    {
        ReleasePreview();
        base.OnClosed(e);
    }
}