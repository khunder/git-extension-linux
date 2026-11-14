using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitCommands;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class DashboardCategoryTests
{
    private static DashboardViewModel Create(FakeHistory history, List<string>? errors = null)
        => new(history, _ => Task.CompletedTask, () => Task.CompletedTask, () => { }, () => { }, message => errors?.Add(message))
        {
            ReadBranch = _ => Task.FromResult<string?>("main"),
        };

    private static DashboardEntry Entry(DashboardViewModel vm, string path)
        => vm.Categories.SelectMany(c => c.Entries).First(e => e.Path == path);

    [AvaloniaTest]
    public async Task Assign_rename_and_delete_a_category()
    {
        FakeHistory history = new();
        history.Recent.AddRange([new Repository("/r/one"), new Repository("/r/two"), new Repository("/r/three")]);
        DashboardViewModel vm = Create(history);
        await vm.LoadAsync();

        // New category from the recent list.
        vm.BeginAssignCategoryCommand.Execute(Entry(vm, "/r/one"));
        vm.IsEditorOpen.Should().BeTrue();
        vm.ApplyEditorCommand.Execute(null);
        vm.EditorError.Should().NotBeNull("an empty name is refused");
        vm.EditorText = "Work";
        await vm.ApplyEditorCommand.ExecuteAsync(null);
        vm.IsEditorOpen.Should().BeFalse();
        vm.Categories.Select(c => c.Title).Should().Equal("Work", DashboardViewModel.RecentTitle);
        history.Favorites.Single().Category.Should().Be("Work");

        // Existing categories are offered for the next repository.
        vm.BeginAssignCategoryCommand.Execute(Entry(vm, "/r/two"));
        vm.ExistingCategories.Should().Equal("Work");
        vm.PickCategoryCommand.Execute("Work");
        vm.EditorText.Should().Be("Work");
        await vm.ApplyEditorCommand.ExecuteAsync(null);
        vm.Categories[0].Entries.Should().HaveCount(2);

        // Rename.
        DashboardCategory work = vm.Categories[0];
        work.IsFavoriteCategory.Should().BeTrue();
        vm.BeginRenameCategoryCommand.Execute(work);
        vm.EditorText.Should().Be("Work");
        vm.EditorText = "Home";
        await vm.ApplyEditorCommand.ExecuteAsync(null);
        vm.Categories.Select(c => c.Title).Should().Equal("Home", DashboardViewModel.RecentTitle);
        history.Favorites.Should().OnlyContain(f => f.Category == "Home");

        // Delete needs confirmation and drops the repositories from the favorites (the recent list is kept).
        vm.BeginDeleteCategoryCommand.Execute(vm.Categories[0]);
        vm.IsDeleting.Should().BeTrue();
        vm.CancelEditorCommand.Execute(null);
        history.Favorites.Should().HaveCount(2);
        vm.BeginDeleteCategoryCommand.Execute(vm.Categories[0]);
        await vm.ApplyEditorCommand.ExecuteAsync(null);
        history.Favorites.Should().BeEmpty();
        vm.Categories.Select(c => c.Title).Should().Equal(DashboardViewModel.RecentTitle);
        history.Recent.Should().HaveCount(3);
    }

    [AvaloniaTest]
    public async Task Recent_list_has_no_category_actions()
    {
        FakeHistory history = new();
        history.Recent.Add(new Repository("/r/one"));
        DashboardViewModel vm = Create(history);
        await vm.LoadAsync();

        vm.BeginRenameCategoryCommand.Execute(vm.Categories[0]);
        vm.BeginDeleteCategoryCommand.Execute(vm.Categories[0]);
        vm.IsEditorOpen.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Pin_moves_a_repository_to_the_top_and_unpin_restores_it()
    {
        FakeHistory history = new();
        history.Recent.AddRange([new Repository("/r/one"), new Repository("/r/two"), new Repository("/r/three")]);
        DashboardViewModel vm = Create(history);
        await vm.LoadAsync();

        await vm.TogglePinCommand.ExecuteAsync(Entry(vm, "/r/three"));
        vm.Categories[0].Entries.Select(e => e.Path).Should().Equal("/r/three", "/r/one", "/r/two");
        vm.Categories[0].Entries[0].IsPinned.Should().BeTrue();
        vm.Categories[0].Entries[0].PinMenuText.Should().Be("Unpin from top");
        history.Recent.Single(r => r.Path == "/r/three").Anchor.Should().Be(Repository.RepositoryAnchor.AnchoredInTop);

        await vm.TogglePinCommand.ExecuteAsync(vm.Categories[0].Entries[0]);
        vm.Categories[0].Entries.Select(e => e.IsPinned).Should().AllBeEquivalentTo(false);
        vm.Categories[0].Entries.Select(e => e.Path).Should().Equal("/r/one", "/r/two", "/r/three");
    }

    [AvaloniaTest]
    public async Task Remove_from_list_removes_a_recent_repository()
    {
        FakeHistory history = new();
        history.Recent.AddRange([new Repository("/r/one"), new Repository("/r/two")]);
        DashboardViewModel vm = Create(history);
        await vm.LoadAsync();

        await vm.RemoveFromListCommand.ExecuteAsync(Entry(vm, "/r/one"));
        history.Recent.Select(r => r.Path).Should().Equal("/r/two");
        vm.Categories.Single().Entries.Should().HaveCount(1);
    }

    [AvaloniaTest]
    public async Task Category_editor_and_actions_render_in_both_themes()
    {
        FakeHistory history = new();
        history.Recent.AddRange([new Repository("/r/one"), new Repository("/r/two")]);
        history.Favorites.Add(new Repository("/r/fav") { Category = "Work" });
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker(), history);
        MainWindow window = new() { DataContext = viewModel, Width = 1000, Height = 700 };
        window.Show();
        try
        {
            await viewModel.Dashboard.LoadAsync();
            ShellTestSupport.Settle();
            window.Find<Border>("CategoryEditor").IsVisible.Should().BeFalse();

            viewModel.Dashboard.BeginAssignCategoryCommand.Execute(viewModel.Dashboard.Categories[1].Entries[0]);
            ShellTestSupport.Settle();
            window.Find<Border>("CategoryEditor").IsVisible.Should().BeTrue();
            window.Find<ItemsControl>("ExistingCategoryList").ItemCount.Should().Be(1);

            foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                ShellTestSupport.Settle();
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"dashboard-categories-{suffix}.png"));
            }

            viewModel.Dashboard.CancelEditorCommand.Execute(null);
            ShellTestSupport.Settle();
            window.Find<Border>("CategoryEditor").IsVisible.Should().BeFalse();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }

    // ---- Favorite repositories dialog ----

    [AvaloniaTest]
    public async Task Repository_lists_dialog_saves_settings_and_lists_only_on_save()
    {
        FakeHistory history = new();
        history.Recent.AddRange([new Repository("/r/one"), new Repository("/r/two")]);
        history.Favorites.Add(new Repository("/r/fav") { Category = "Work" });
        RepositoryListOptions stored = new(3, 30, false, false, false, ShorteningRecentRepoPathStrategy.MostSignDir);
        bool reloaded = false;
        RepositoryListsViewModel vm = new(history, _ => { }, () =>
            {
                reloaded = true;
                return Task.CompletedTask;
            }, () => stored, options => stored = options);

        await vm.OpenAsync();
        vm.IsOpen.Should().BeTrue();
        vm.MaxTopRepositories.Should().Be(3);
        vm.Recent.Should().HaveCount(2);
        vm.Favorites.Should().HaveCount(1);

        vm.TogglePinCommand.Execute(vm.Recent[1]);
        vm.Recent[0].Path.Should().Be("/r/two");
        vm.Recent[0].IsPinned.Should().BeTrue();
        vm.RemoveCommand.Execute(vm.Recent[1]);
        vm.RemoveCommand.Execute(vm.Favorites[0]);
        history.Recent.Should().HaveCount(2, "nothing is stored before Save");

        vm.HistorySize = 10;
        vm.MaxTopRepositories = 1;
        vm.SortRecent = true;
        vm.ShorteningIndex = (int)ShorteningRecentRepoPathStrategy.MiddleDots;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.IsOpen.Should().BeFalse();
        reloaded.Should().BeTrue();
        stored.Should().Be(new RepositoryListOptions(1, 10, false, false, true, ShorteningRecentRepoPathStrategy.MiddleDots));
        history.Recent.Select(r => r.Path).Should().Equal("/r/two");
        history.Recent[0].Anchor.Should().Be(Repository.RepositoryAnchor.AnchoredInTop);
        history.Favorites.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Repository_lists_cancel_keeps_everything_and_missing_folders_can_be_dropped()
    {
        string existing = Directory.CreateTempSubdirectory("ge-lists-").FullName;
        try
        {
            FakeHistory history = new();
            history.Recent.AddRange([new Repository(existing), new Repository("/definitely/not/here")]);
            RepositoryListsViewModel vm = new(history, _ => { }, load: () => new RepositoryListOptions(0, 30, false, false, false, ShorteningRecentRepoPathStrategy.None), save: _ => { });
            await vm.OpenAsync();

            vm.Recent.Single(r => !r.Exists).Path.Should().Be("/definitely/not/here");
            vm.RemoveMissingCommand.Execute(null);
            vm.Recent.Should().HaveCount(1);
            vm.CancelCommand.Execute(null);
            vm.IsOpen.Should().BeFalse();
            history.Recent.Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(existing);
        }
    }

    [AvaloniaTest]
    public async Task Repository_lists_dialog_opens_from_the_menu_and_renders()
    {
        FakeHistory history = new();
        history.Recent.AddRange([new Repository("/r/one"), new Repository("/r/two")]);
        history.Favorites.Add(new Repository("/r/fav") { Category = "Work" });
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker(), history);
        MainWindow window = new() { DataContext = viewModel, Width = 1000, Height = 760 };
        window.Show();
        try
        {
            await viewModel.ShowRepositoryListsCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            window.Find<RepositoryListsView>("RepositoryListsOverlay").IsVisible.Should().BeTrue();
            window.Find<ListBox>("RecentList").ItemCount.Should().Be(2);
            window.Find<ListBox>("FavoriteList").ItemCount.Should().Be(1);

            foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                ShellTestSupport.Settle();
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"repository-lists-{suffix}.png"));
            }

            viewModel.RepositoryLists.CancelCommand.Execute(null);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }
}
