using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class IconTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Screenshot(Window window, string name)
    {
        Pump();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name + ".png");
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.AddTestAttachment(file);
        TestContext.Out.WriteLine(file);
    }

    private static async Task<MainWindow> OpenAsync(TestRepository repository)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        Pump();
        return window;
    }

    [AvaloniaTest]
    public async Task The_toolbar_buttons_are_in_the_original_order_with_the_original_icons()
    {
        using TestRepository repository = new();
        MainWindow window = await OpenAsync(repository);
        MainToolbarView toolbar = window.FindControl<MainToolbarView>("MainToolbar")!;

        string[] expected =
        [
            "RefreshButton", "LeftPanelButton", "LayoutButton", "CommitInfoPositionButton", "LevelUpButton", "RepoPathButton", "BranchButton",
            "PullButton", "PushButton", "CommitButton", "StashMenuButton", "FileExplorerButton", "ShellButton", "SettingsButton",
        ];
        string[] actual = toolbar.GetVisualDescendants().OfType<Control>()
            .Where(b => b.IsVisible && b.Name is not null && expected.Contains(b.Name))
            .OrderBy(b => b.TranslatePoint(default, window)!.Value.X)
            .Select(b => b.Name!)
            .ToArray();
        actual.Should().Equal(expected);

        Dictionary<string, string> icons = new()
        {
            ["RefreshButton"] = "ReloadRevisions", ["LeftPanelButton"] = "LayoutSidebarLeft", ["LayoutButton"] = "LayoutFooter",
            ["CommitInfoPositionButton"] = "LayoutFooterTab", ["LevelUpButton"] = "SubmodulesManage", ["RepoPathButton"] = "RepoOpen",
            ["BranchButton"] = "Branch", ["PullButton"] = "Pull", ["PushButton"] = "Push", ["CommitButton"] = "RepoStateClean",
            ["StashMenuButton"] = "Stash", ["FileExplorerButton"] = "BrowseFileExplorer", ["ShellButton"] = "GitForWindows", ["SettingsButton"] = "Settings",
        };
        foreach ((string button, string icon) in icons)
        {
            Control control = toolbar.GetVisualDescendants().OfType<Control>().First(c => c.Name == button);
            control.GetVisualDescendants().OfType<AppIcon>().First().IconName.Should().Be(icon, button);
            IconCache.Get(icon, dark: false).Should().NotBeNull(icon);
            IconCache.Get(icon, dark: true).Should().NotBeNull(icon);
        }

        window.Close();
    }

    [AvaloniaTest]
    public async Task Every_icon_named_in_the_main_window_loads_and_the_tree_and_tabs_show_icons()
    {
        using TestRepository repository = new();
        repository.RunGit("branch", "feature/x");
        repository.RunGit("tag", "v1");
        MainWindow window = await OpenAsync(repository);
        Pump();

        List<AppIcon> icons = window.GetVisualDescendants().OfType<AppIcon>().ToList();
        icons.Count.Should().BeGreaterThan(20, "toolbar, tabs and the refs tree");
        foreach (string name in icons.Select(i => i.IconName).Distinct())
        {
            IconCache.Get(name, dark: false).Should().NotBeNull(name);
        }

        string[] tabIcons = window.FindControl<RevisionTabsView>("RevisionTabs")!.GetVisualDescendants().OfType<AppIcon>().Select(i => i.IconName!).ToArray();
        tabIcons.Should().Contain(["CommitSummary", "Diff", "FileTree", "Console"]);
        icons.Select(i => i.IconName).Should().Contain(["BranchLocalRoot", "BranchRemoteRoot", "TagHorizontal", "BranchLocal"]);
        window.Close();
    }

    [AvaloniaTest]
    public void Dark_variants_lighten_mostly_dark_icons_and_leave_light_ones_alone()
    {
        // Branch is adapted explicitly by the original; Pull is a mid-tone blue arrow that stays as it is.
        IconCache.MeanLuma(IconCache.Get("Branch", dark: true)!).Should().BeGreaterThan(IconCache.MeanLuma(IconCache.Get("Branch", dark: false)!));
        IconCache.MeanLuma(IconCache.Get("Pull", dark: true)!).Should().BeApproximately(IconCache.MeanLuma(IconCache.Get("Pull", dark: false)!), 2);
    }

    [AvaloniaTest]
    public async Task Screenshots_of_the_toolbar_and_menus_with_icons_in_light_and_dark()
    {
        using TestRepository repository = new();
        MainWindow window = await OpenAsync(repository);
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = variant;
            window.RequestedThemeVariant = variant;
            Pump();
            string suffix = variant == ThemeVariant.Dark ? "dark" : "light";
            Screenshot(window, "icons-toolbar-" + suffix);

            MainMenuView menu = window.FindControl<MainMenuView>("MainMenuBar")!;
            foreach (string name in new[] { "RepositoryMenu", "CommandsMenu" })
            {
                MenuItem item = menu.FindControl<MenuItem>(name)!;
                item.IsSubMenuOpen = true;
                Pump();
                Screenshot(window, $"icons-{name}-{suffix}");
                item.IsSubMenuOpen = false;
            }
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        window.Close();
    }
}
