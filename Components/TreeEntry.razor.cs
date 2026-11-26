using Microsoft.AspNetCore.Components;

namespace Applications.Components;

public partial class TreeEntry
{
    [Inject] private Library Library { get; set; } = default!;

    [Parameter, EditorRequired] public Entry Item { get; set; } = default!;
    [Parameter] public string? Selected { get; set; }
    [Parameter] public EventCallback<Entry> OnSelect { get; set; }
    [Parameter] public bool StartExpanded { get; set; }
    [Parameter] public TreeSortOrder SortOrder { get; set; }
    [Parameter] public string? ExpandedSiblingPath { get; set; }
    [Parameter] public EventCallback<string?> OnExpansionChanged { get; set; }
    private Entry[]? _children;
    private string? _expandedChildPath;
    private TreeSortOrder _childrenSortOrder;
    private string? _error;

    private string ModifiedLabel => Item.Modified.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private EventCallback<string?> ChildExpansionChanged =>
        StartExpanded ? EventCallback.Factory.Create<string?>(this, SetExpandedChild) : default;

    protected override void OnInitialized()
    {
        if (StartExpanded)
            LoadChildren();
    }

    protected override void OnParametersSet()
    {
        if (OnExpansionChanged.HasDelegate && ExpandedSiblingPath != Item.Path)
            _children = null;

        if (_children is null || _childrenSortOrder == SortOrder)
            return;

        _children = Library.SortEntries(_children, SortOrder);
        _childrenSortOrder = SortOrder;
    }

    private async Task Toggle()
    {
        if (_children is not null)
        {
            _children = null;
            await OnExpansionChanged.InvokeAsync(null);
            return;
        }

        LoadChildren();
        if (_children is not null)
            await OnExpansionChanged.InvokeAsync(Item.Path);
    }

    private void SetExpandedChild(string? path) => _expandedChildPath = path;

    private void LoadChildren()
    {
        _error = null;
        try
        {
            _children = Library.List(Item.Path, SortOrder);
            _childrenSortOrder = SortOrder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _error = "This folder could not be read.";
        }
    }
}
