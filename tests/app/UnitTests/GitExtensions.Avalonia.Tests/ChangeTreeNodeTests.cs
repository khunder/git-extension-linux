using GitExtensions.Desktop.ViewModels;
using GitExtensions.Extensibility.Git;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class ChangeTreeNodeTests
{
    private static ChangedFileViewModel File(string path) => new(new GitItemStatus(path));

    [Test]
    public void Build_groups_files_by_folder_with_folders_first()
    {
        List<ChangeTreeNode> roots = ChangeTreeNode.Build([File("z.txt"), File("src/b.cs"), File("src/a.cs"), File("src/sub/c.cs"), File("a.txt")]);

        roots.Select(n => n.Name).Should().Equal("src", "a.txt", "z.txt");
        ChangeTreeNode src = roots[0];
        src.IsFolder.Should().BeTrue();
        src.Children.Select(n => n.Name).Should().Equal("sub", "a.cs", "b.cs");
        src.Children[0].Children.Select(n => n.FullPath).Should().Equal("src/sub/c.cs");
        src.Label.Should().Be("src (3)");
    }

    [Test]
    public void Files_of_a_folder_are_all_the_files_below_it()
    {
        List<ChangeTreeNode> roots = ChangeTreeNode.Build([File("src/a.cs"), File("src/sub/c.cs"), File("top.txt")]);

        roots[0].Files().Select(f => f.Name).Should().BeEquivalentTo("src/a.cs", "src/sub/c.cs");
        roots[1].Files().Select(f => f.Name).Should().Equal("top.txt");
    }

    [Test]
    public void Collapsed_folders_stay_collapsed_when_the_tree_is_rebuilt()
    {
        HashSet<string> collapsed = [];
        List<ChangeTreeNode> first = ChangeTreeNode.Build([File("src/a.cs")], collapsed, "unstaged");
        first[0].IsExpanded.Should().BeTrue("folders start expanded");

        first[0].IsExpanded = false;
        List<ChangeTreeNode> second = ChangeTreeNode.Build([File("src/a.cs"), File("src/b.cs")], collapsed, "unstaged");

        second[0].IsExpanded.Should().BeFalse();
        ChangeTreeNode.Build([File("src/a.cs")], collapsed, "staged")[0].IsExpanded.Should().BeTrue("the staged tree remembers separately");
    }

    [Test]
    public void A_folder_whose_only_child_is_a_folder_is_shown_as_one_node()
    {
        List<ChangeTreeNode> roots = ChangeTreeNode.Build([File("src/app/Core/a.cs"), File("src/app/Core/b.cs"), File("top.txt")]);

        roots.Select(n => n.Name).Should().Equal("src/app/Core", "top.txt");
        roots[0].FullPath.Should().Be("src/app/Core");
        roots[0].Children.Select(n => n.Name).Should().Equal("a.cs", "b.cs");
        roots[0].Label.Should().Be("src/app/Core (2)");
    }
}
