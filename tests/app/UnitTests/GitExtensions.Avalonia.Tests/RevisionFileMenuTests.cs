using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.History;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensionsDesktopTests;

/// <summary>The file menu of a commit's files (Diff tab, Commit tab, File tree) and the git commands behind it.</summary>
[TestFixture]
public class RevisionFileMenuTests
{
    private static string Outline(IEnumerable<Control> items) => string.Join("|", items.Select(MenuKit.TextOf));

    [AvaloniaTest]
    public void Menu_of_a_commit_file_has_the_original_entries()
    {
        using TestRepository repository = new();
        string head = repository.GitOutput("rev-parse", "HEAD").Trim();
        List<string> log = [];
        List<Control> menu = FileListContextMenu.Build(new FileMenuContext
        {
            Paths = ["b.txt"],
            WorkingDir = repository.Path,
            Revision = head,
            OpenHistory = (path, blame) => log.Add((blame ? "blame " : "history ") + path),
            ShowInFileTree = path => log.Add("tree " + path),
            FilterInGrid = path => log.Add("filter " + path),
            PickSaveFile = (_, _, _) => Task.FromResult<string?>(null),
        });

        Outline(menu).Should().Be(
            "Reset file(s) to|Cherry pick changes|-|Open with difftool|Open working directory file|Open working directory file with…|Open this revision (temp file)|"
            + "Open this revision with… (temp file)|Edit working directory file|Save selected as…|Rename / move…|-|Copy path(s)|Show in folder|-|"
            + "Show in File tree|Filter file in grid|File history|Blame|-|Stop tracking this file");

        foreach (MenuItem item in menu.OfType<MenuItem>().Where(i => MenuKit.TextOf(i) is "Show in File tree" or "Filter file in grid" or "File history" or "Blame"))
        {
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        }

        log.Should().Equal("tree b.txt", "filter b.txt", "history b.txt", "blame b.txt");
    }

    [Test]
    public async Task Reset_cherry_pick_save_and_move_change_the_working_directory()
    {
        using TestRepository repository = new();
        string a = Path.Combine(repository.Path, "a.txt");
        string b = Path.Combine(repository.Path, "b.txt");
        string head = repository.GitOutput("rev-parse", "HEAD").Trim();
        string first = repository.GitOutput("rev-parse", "HEAD~1").Trim();

        // The parent of the commit that added b.txt has no b.txt: resetting to it removes the file.
        (await RevisionFileCommands.ResolveBaseAsync(repository.Path, head, null)).Should().Be(first);
        (await RevisionFileCommands.ResolveBaseAsync(repository.Path, first, null)).Should().Be(RevisionFileCommands.EmptyTree);
        (await RevisionFileCommands.ResetToAsync(repository.Path, first, ["b.txt"])).Should().BeNull();
        File.Exists(b).Should().BeFalse();

        // Cherry-picking the change of that commit brings it back.
        (await RevisionFileCommands.CherryPickChangesAsync(repository.Path, first, head, ["b.txt"])).Should().BeNull();
        File.ReadAllText(b).Should().Be("second");

        // Reset to this revision drops the local modification of a.txt.
        File.ReadAllText(a).Should().Be("changed");
        (await RevisionFileCommands.ResetToAsync(repository.Path, head, ["a.txt"])).Should().BeNull();
        File.ReadAllText(a).Should().Be("first");

        string saved = Path.Combine(repository.Path, "saved.txt");
        (await RevisionFileCommands.SaveAsync(repository.Path, head, "b.txt", saved)).Should().BeNull();
        File.ReadAllText(saved).Should().Be("second");
        (await RevisionFileCommands.SaveAsync(repository.Path, first, "b.txt", saved)).Should().NotBeNull("b.txt does not exist in the first commit");

        (string? temp, string? error) = await RevisionFileCommands.SaveTemporaryAsync(repository.Path, head, "a.txt");
        error.Should().BeNull();
        Path.GetFileName(temp).Should().Be("a.txt");

        (await RevisionFileCommands.MoveAsync(repository.Path, "a.txt", "docs/a.txt")).Should().BeNull();
        File.Exists(Path.Combine(repository.Path, "docs", "a.txt")).Should().BeTrue();
    }

    [Test]
    public void Path_filter_is_passed_after_the_revisions()
    {
        RevisionFilter filter = new() { Path = "src/a b.txt" };
        filter.IsFiltering.Should().BeTrue();
        RevisionFilterArgs.PathSpec(filter).Should().Be("\"src/a b.txt\"");
        RevisionFilterArgs.PathSpec(new RevisionFilter()).Should().BeEmpty();
    }
}
