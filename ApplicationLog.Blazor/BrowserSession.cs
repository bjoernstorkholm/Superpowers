namespace ApplicationLog;

public sealed class BrowserSession
{
    public int TreeZoom { get; set; } = 100;
    public int PreviewZoom { get; set; } = 100;
    public bool SortByDate { get; set; }
    public string? SelectedTreePath { get; set; }
    public string? PreviewPath { get; set; }
    public string[] ExpandedFolders { get; set; } = [];
}