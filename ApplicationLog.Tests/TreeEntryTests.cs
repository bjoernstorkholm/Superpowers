using AngleSharp.Dom;
using Applications;
using Applications.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace ApplicationLog.Tests;

public sealed class TreeEntryTests
{
    [Fact]
    public void OpeningAnotherTopLevelFolderCollapsesThePreviousOne()
    {
        using var files = new TestDirectory();
        files.CreateText("Alpha/first.txt", "First");
        files.CreateText("Beta/second.txt", "Second");
        using var context = CreateContext(files, out var library);
        var tree = RenderRoot(context, library);

        FindButton(tree, "Alpha").Click();
        Assert.Equal("true", FindButton(tree, "Alpha").GetAttribute("aria-expanded"));
        Assert.Contains("first.txt", tree.Markup);

        FindButton(tree, "Beta").Click();
        Assert.Equal("false", FindButton(tree, "Alpha").GetAttribute("aria-expanded"));
        Assert.Equal("true", FindButton(tree, "Beta").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("first.txt", tree.Markup);
        Assert.Contains("second.txt", tree.Markup);

        FindButton(tree, "Beta").Click();
        Assert.Equal("false", FindButton(tree, "Beta").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("second.txt", tree.Markup);
    }

    [Fact]
    public void NestedSiblingFoldersCanBeExpandedTogether()
    {
        using var files = new TestDirectory();
        files.CreateText("Alpha/One/first.txt", "First");
        files.CreateText("Alpha/Two/second.txt", "Second");
        using var context = CreateContext(files, out var library);
        var tree = RenderRoot(context, library);

        FindButton(tree, "Alpha").Click();
        FindButton(tree, "One").Click();
        FindButton(tree, "Two").Click();

        Assert.Equal("true", FindButton(tree, "One").GetAttribute("aria-expanded"));
        Assert.Equal("true", FindButton(tree, "Two").GetAttribute("aria-expanded"));
        Assert.Contains("first.txt", tree.Markup);
        Assert.Contains("second.txt", tree.Markup);
    }

    [Fact]
    public void SwitchingSortPreservesExpandedFolderAndSelectedFile()
    {
        using var files = new TestDirectory();
        var first = files.CreateText("Alpha/apple.txt", "Selected");
        var second = files.CreateText("Alpha/zebra.txt", "Newer");
        files.CreateText("Alpha/log.txt", "Pinned");
        File.SetLastWriteTimeUtc(first, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(second, new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        using var context = CreateContext(files, out var library);
        var selectedPath = Path.Combine("Alpha", "apple.txt");
        var tree = context.Render<TreeEntry>(parameters => parameters
            .Add(p => p.Item, library.Describe("."))
            .Add(p => p.Selected, selectedPath)
            .Add(p => p.StartExpanded, true));

        FindButton(tree, "Alpha").Click();
        Assert.Equal(new[] { "log.txt", "apple.txt", "zebra.txt" }, FileNames(tree));

        tree.Render(parameters => parameters.Add(p => p.SortOrder, TreeSortOrder.DateModified));

        Assert.Equal("true", FindButton(tree, "Alpha").GetAttribute("aria-expanded"));
        Assert.Equal("true", FindButton(tree, "apple.txt").GetAttribute("aria-pressed"));
        Assert.Equal(new[] { "log.txt", "zebra.txt", "apple.txt" }, FileNames(tree));
    }

    [Fact]
    public void SelectingAFilePassesItsRelativePathToTheParent()
    {
        using var files = new TestDirectory();
        files.CreateText("Alpha/log.txt", "Log");
        using var context = CreateContext(files, out var library);
        Entry? selected = null;
        var tree = context.Render<TreeEntry>(parameters => parameters
            .Add(p => p.Item, library.Describe("."))
            .Add(p => p.StartExpanded, true)
            .Add(p => p.OnSelect, entry => { selected = entry; }));

        FindButton(tree, "Alpha").Click();
        FindButton(tree, "log.txt").Click();

        Assert.NotNull(selected);
        Assert.Equal(Path.Combine("Alpha", "log.txt"), selected.Path);
        Assert.False(selected.Folder);
    }

    private static BunitContext CreateContext(TestDirectory files, out Library library)
    {
        var context = new BunitContext();
        library = new Library(files.Path);
        context.Services.AddSingleton(library);
        return context;
    }

    private static IRenderedComponent<TreeEntry> RenderRoot(BunitContext context, Library library) =>
        context.Render<TreeEntry>(parameters => parameters
            .Add(p => p.Item, library.Describe("."))
            .Add(p => p.StartExpanded, true));

    private static IElement FindButton(IRenderedComponent<TreeEntry> tree, string name) =>
        Assert.Single(tree.FindAll("button"), button => button.LastElementChild?.TextContent == name);

    private static IEnumerable<string> FileNames(IRenderedComponent<TreeEntry> tree) =>
        tree.FindAll("button[aria-pressed]").Select(button => button.LastElementChild!.TextContent);
}
