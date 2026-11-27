using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Applications;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ApplicationLog.Wpf;

internal static class DocumentPreviews
{
    public static FrameworkElement Create(Library library, Entry entry)
    {
        switch (Path.GetExtension(entry.Name).ToLowerInvariant())
        {
            case ".pdf":
                return CreatePdf(library, entry);
            case ".docx":
                return CreateWord(PreviewReader.Read(library, entry).Word!);
            case ".xlsx":
                return CreateWorkbook(PreviewReader.Read(library, entry).Sheets!);
            default:
                return Notice("Preview is available for PDF, TXT, DOCX and XLSX files.");
        }
    }

    internal static bool TrySetPdfZoom(FrameworkElement preview, double factor)
    {
        if (preview is not PdfPreviewHost host)
            return false;

        host.ZoomFactor = factor;
        if (host.Children.OfType<WebView2>().FirstOrDefault() is WebView2 viewer)
            viewer.ZoomFactor = factor;

        return true;
    }

    private static FrameworkElement CreatePdf(Library library, Entry entry)
    {
        using (library.Open(entry.Path)) { }

        Uri source = new(Path.Combine(library.Root, entry.Path));
        PdfPreviewHost host = new();
        WebView2? activeViewer = null;
        host.Loaded += async (_, _) =>
        {
            if (activeViewer is not null)
                return;

            TextBlock status = Notice("Loading PDF…");
            WebView2 viewer = new() { Visibility = Visibility.Hidden, ZoomFactor = host.ZoomFactor };
            activeViewer = viewer;
            host.Children.Clear();
            host.Children.Add(status);
            host.Children.Add(viewer);
            try
            {
                string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ApplicationLog.Wpf", "WebView2");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
                if (!ReferenceEquals(activeViewer, viewer))
                    return;

                await viewer.EnsureCoreWebView2Async(environment);
                if (!ReferenceEquals(activeViewer, viewer))
                    return;

                CoreWebView2 browser = viewer.CoreWebView2;
                browser.Settings.AreDevToolsEnabled = false;
                browser.Settings.AreDefaultContextMenusEnabled = false;
                browser.Settings.AreHostObjectsAllowed = false;
                browser.Settings.IsWebMessageEnabled = false;
                browser.Settings.IsScriptEnabled = false;
                browser.Settings.IsStatusBarEnabled = false;
                browser.Settings.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Save |
                    CoreWebView2PdfToolbarItems.SaveAs | CoreWebView2PdfToolbarItems.Print;
                browser.NavigationStarting += (_, args) =>
                {
                    args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? target) ||
                        !target.IsFile || !string.Equals(target.LocalPath, source.LocalPath, StringComparison.Ordinal);
                };
                browser.FrameNavigationStarting += (_, args) => args.Cancel = true;
                browser.NewWindowRequested += (_, args) => args.Handled = true;
                browser.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
                browser.DownloadStarting += (_, args) => args.Cancel = true;
                browser.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess)
                    {
                        viewer.Visibility = Visibility.Visible;
                        status.Visibility = Visibility.Collapsed;
                    }
                    else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    {
                        viewer.Visibility = Visibility.Hidden;
                        status.Visibility = Visibility.Visible;
                        status.Text = "This PDF could not be previewed.";
                    }
                };
                viewer.Source = source;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                status.Text = "PDF preview requires the Microsoft Edge WebView2 Runtime.";
            }
            catch (Exception)
            {
                status.Text = "This PDF could not be previewed.";
            }
        };
        host.Unloaded += (_, _) =>
        {
            WebView2? viewer = activeViewer;
            activeViewer = null;
            viewer?.Dispose();
            host.Children.Clear();
        };

        return host;
    }

    private static FlowDocumentScrollViewer CreateWord(WordBlock[] blocks)
    {
        FlowDocument document = new() { FontSize = 14, PagePadding = new Thickness(22) };
        foreach (WordBlock block in blocks)
        {
            if (block is WordParagraph paragraph)
                document.Blocks.Add(CreateParagraph(paragraph));
            else if (block is WordTable table && table.Rows.Any(row => row.Length > 0))
                document.Blocks.Add(CreateWordTable(table));
        }

        return new FlowDocumentScrollViewer
        {
            Document = document,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static Paragraph CreateParagraph(WordParagraph paragraph)
    {
        Paragraph result = new() { Margin = new Thickness(0, 0, 0, 8) };
        foreach (WordRun run in paragraph.Runs)
        {
            string[] lines = run.Text.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                    result.Inlines.Add(new LineBreak());

                result.Inlines.Add(new Run(lines[index])
                {
                    FontWeight = run.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = run.Italic ? FontStyles.Italic : FontStyles.Normal
                });
            }
        }

        return result;
    }

    private static Table CreateWordTable(WordTable wordTable)
    {
        Table table = new() { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8) };
        int columnCount = wordTable.Rows.Max(row => row.Length);
        for (int column = 0; column < columnCount; column++)
            table.Columns.Add(new TableColumn());

        TableRowGroup group = new();
        table.RowGroups.Add(group);
        foreach (WordParagraph[][] row in wordTable.Rows)
        {
            TableRow tableRow = new();
            group.Rows.Add(tableRow);
            foreach (WordParagraph[] cell in row)
            {
                TableCell tableCell = new()
                {
                    Padding = new Thickness(6),
                    BorderThickness = new Thickness(0.5),
                    BorderBrush = SystemColors.ControlDarkBrush
                };
                foreach (WordParagraph paragraph in cell)
                    tableCell.Blocks.Add(CreateParagraph(paragraph));

                tableRow.Cells.Add(tableCell);
            }
        }

        return table;
    }

    private static FrameworkElement CreateWorkbook(Sheet[] sheets)
    {
        if (sheets.Length == 0)
            return Notice("This workbook is empty.");

        DockPanel host = new();
        ComboBox picker = new()
        {
            ItemsSource = sheets.Select(sheet => sheet.Name),
            Margin = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160
        };
        System.Windows.Automation.AutomationProperties.SetName(picker, "Worksheets");
        DockPanel.SetDock(picker, Dock.Top);
        host.Children.Add(picker);
        ContentControl content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        host.Children.Add(content);
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
                content.Content = CreateSheet(sheets[picker.SelectedIndex]);
        };
        picker.SelectedIndex = 0;

        return host;
    }

    private static DockPanel CreateSheet(Sheet sheet)
    {
        DockPanel panel = new();
        if (sheet.Truncated)
        {
            TextBlock truncation = Notice("Showing up to 200 rows and 40 columns.");
            DockPanel.SetDock(truncation, Dock.Top);
            panel.Children.Add(truncation);
        }

        if (sheet.Cells.Length == 0)
        {
            panel.Children.Add(Notice("This sheet is empty."));

            return panel;
        }

        DataTable data = new() { Locale = CultureInfo.InvariantCulture };
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserReorderColumns = false,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.All
        };
        System.Windows.Automation.AutomationProperties.SetName(grid, $"Worksheet {sheet.Name}");
        int width = sheet.Cells[0].Length;
        for (int column = 0; column <= width; column++)
        {
            string key = $"Column{column}";
            data.Columns.Add(key, typeof(string));
            grid.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(key),
                Header = column == 0 ? "" : ColumnName(sheet.FirstColumn + column - 1)
            });
        }

        for (int index = 0; index < sheet.Cells.Length; index++)
        {
            DataRow row = data.NewRow();
            row[0] = (sheet.FirstRow + index).ToString(CultureInfo.InvariantCulture);
            for (int column = 0; column < width; column++)
                row[column + 1] = sheet.Cells[index][column];

            data.Rows.Add(row);
        }

        grid.FrozenColumnCount = 1;
        grid.ItemsSource = data.DefaultView;
        panel.Children.Add(grid);

        return panel;
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

    private static TextBlock Notice(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(12),
        VerticalAlignment = VerticalAlignment.Top
    };
}

internal sealed class PdfPreviewHost : Grid
{
    public double ZoomFactor { get; set; } = 1;
}