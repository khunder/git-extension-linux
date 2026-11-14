using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class ViewOptionsTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private ViewOptionsStore _previous = null!;
    private ViewOptionsStore _store = null!;

    [SetUp]
    public void UseFreshStore()
    {
        _previous = ViewOptionsStore.Current;
        _store = new ViewOptionsStore(Path.Combine(Path.GetTempPath(), "ge-avalonia-vo-" + Guid.NewGuid().ToString("N") + ".json"));
        ViewOptionsStore.Current = _store;
    }

    [TearDown]
    public void Restore()
    {
        ViewOptionsStore.Current = _previous;
        File.Delete(_store.FilePath);
    }

    private static MainWindowViewModel NewViewModel() => new(GitEnvironment.Initialize(), new NoFolderPicker());

    [AvaloniaTest]
    public void The_branch_filter_defaults_to_all_branches()
        => MainWindowViewModel.DefaultBranchChoice.Should().Be(MainWindowViewModel.AllBranchesChoice);

    [AvaloniaTest]
    public void Defaults_show_all_tabs_but_the_build_report()
    {
        _store.Load().Should().Be(new ViewOptionsSettings());
        ViewOptionsSettings defaults = new();
        defaults.ShowBuildReportTab.Should().BeFalse();
        defaults.CommitInfoPosition.Should().Be(CommitInfoLayout.Below);
    }

    [AvaloniaTest]
    public void Options_are_persisted_and_restored_by_the_next_view_model()
    {
        MainWindowViewModel first = NewViewModel();
        first.SetCommitInfoPositionCommand.Execute(CommitInfoLayout.Right);
        first.ToggleSplitLayoutCommand.Execute(null);
        first.ViewOptions.ShowConsoleTab = false;
        first.ViewOptions.ShowFilterBar = false;
        first.ShowRemoteBranches = false;
        first.ShowTags = false;

        ViewOptionsSettings stored = _store.Load();
        stored.CommitInfoPosition.Should().Be(CommitInfoLayout.Right);
        stored.SideBySide.Should().BeTrue();
        stored.ShowConsoleTab.Should().BeFalse();
        stored.ShowFilterBar.Should().BeFalse();
        stored.ShowRemoteBranches.Should().BeFalse();
        stored.ShowTags.Should().BeFalse();

        MainWindowViewModel second = NewViewModel();
        second.ViewOptions.IsCommitInfoRight.Should().BeTrue();
        second.ViewOptions.SideBySide.Should().BeTrue();
        second.ViewOptions.ShowConsoleTab.Should().BeFalse();
        second.ViewOptions.ShowFilterBar.Should().BeFalse();
        second.ShowRemoteBranches.Should().BeFalse();
        second.ShowTags.Should().BeFalse();
        second.RevisionTabs.ShowConsoleTab.Should().BeFalse();
        second.RevisionTabs.ShowCommitTab.Should().BeFalse("the commit info is a separate pane on the right");
    }

    [AvaloniaTest]
    public void Tab_toggles_hide_tabs_and_move_the_selection_to_a_visible_one()
    {
        MainWindowViewModel viewModel = NewViewModel();
        RevisionTabsViewModel tabs = viewModel.RevisionTabs;
        tabs.SelectedTabIndex = RevisionTabsViewModel.DiffTab;

        viewModel.ViewOptions.ShowDiffTab = false;
        tabs.ShowDiffTab.Should().BeFalse();
        tabs.SelectedTabIndex.Should().NotBe(RevisionTabsViewModel.DiffTab);

        viewModel.ViewOptions.ShowBuildReportTab = true;
        tabs.ShowBuildReport.Should().BeTrue();
        viewModel.ViewOptions.ShowCommitTab = false;
        tabs.ShowCommitTab.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Commit_info_position_and_split_layout_rearrange_the_main_window()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = NewViewModel();
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        Dispatcher.UIThread.RunJobs();

        int InfoViews() => window.GetVisualDescendants().OfType<CommitInfoView>().Count();
        InfoViews().Should().Be(1);
        window.Find<TabItem>("CommitTabItem")!.IsVisible.Should().BeTrue();

        viewModel.SetCommitInfoPositionCommand.Execute(CommitInfoLayout.Left);
        Dispatcher.UIThread.RunJobs();
        InfoViews().Should().Be(1, "the pane replaces the tab");
        window.Find<TabItem>("CommitTabItem")!.IsVisible.Should().BeFalse();
        window.Find<Border>("CommitInfoLeftPane")!.IsVisible.Should().BeTrue();
        window.Find<Border>("CommitInfoRightPane")!.IsVisible.Should().BeFalse();

        viewModel.SetCommitInfoPositionCommand.Execute(CommitInfoLayout.Right);
        Dispatcher.UIThread.RunJobs();
        window.Find<Border>("CommitInfoRightPane")!.IsVisible.Should().BeTrue();

        Grid rows = window.FindControl<Grid>("BrowseRows")!;
        viewModel.ToggleSplitLayoutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        rows.ColumnDefinitions.Should().HaveCount(3);
        Grid.GetColumn(window.Find<Control>("RevisionTabs")!).Should().Be(2);

        viewModel.ToggleSplitLayoutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        rows.ColumnDefinitions.Should().BeEmpty();
        Grid.GetRow(window.Find<Control>("RevisionTabs")!).Should().Be(2);

        viewModel.ViewOptions.ShowFilterBar = false;
        window.Find<Control>("FilterBar")!.IsVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_view_menu_has_no_stubs_left_for_these_options()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = NewViewModel();
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        Dispatcher.UIThread.RunJobs();
        MainMenuView menu = window.GetVisualDescendants().OfType<MainMenuView>().Single();
        foreach (string name in new[]
        {
            "CommitInfoBelowMenuItem", "CommitInfoLeftMenuItem", "CommitInfoRightMenuItem", "ShowCommitTabMenuItem", "ShowDiffTabMenuItem",
            "ShowFileTreeTabMenuItem", "ShowConsoleTabMenuItem", "ShowBuildReportTabMenuItem", "FiltersMenu", "ShowRemoteBranchesMenuItem", "ShowTagsMenuItem",
            "SolveConflictsMenuItem",
        })
        {
            MenuItem item = menu.GetLogicalDescendants().OfType<MenuItem>().FirstOrDefault(m => m.Name == name)!;
            item.Should().NotBeNull(name);
            item.Classes.Contains("stub").Should().BeFalse(name);
        }

        window.Find<Button>("LayoutButton")!.IsEnabled.Should().BeTrue();
        window.Find<Button>("CommitInfoPositionButton")!.IsEnabled.Should().BeTrue();
        window.Close();
    }
}
